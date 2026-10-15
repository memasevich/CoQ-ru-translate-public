using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;
using XRL.UI;

namespace RussianLocalization
{
    /// <summary>
    /// Информация о найденном релизе на GitHub.
    /// </summary>
    public sealed class ReleaseInfo
    {
        public string TagName { get; set; } = string.Empty;
        public Version Version { get; set; } = new Version(0, 0, 0);
        public string Title { get; set; } = string.Empty;
        public string Changelog { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public long AssetSizeBytes { get; set; } = 0;
    }

    /// <summary>
    /// Внутриигровой сервис автообновления русской локализации Caves of Qud.
    /// Работает полностью асинхронно, не блокирует загрузку игры и UI.
    /// </summary>
    public static class UpdateService
    {
        private const string RepoOwner = "memasevich";
        private const string RepoName = "CoQ-ru-translate-public";
        private const string ApiLatestReleaseUrl = "https://api.github.com/repos/" + RepoOwner + "/" + RepoName + "/releases/latest";
        private const string UserAgent = "CavesOfQud-RussianLocalization-Updater";

        private const string PrefKeyLastCheckTime = "RuMod_LastUpdateCheckUtcTicks";
        private const string PrefKeySnoozedPrefix = "RuMod_UpdateSnoozed_";
        private static readonly TimeSpan MinCheckInterval = TimeSpan.FromSeconds(60);

        private static readonly object SyncLock = new object();
        private static bool _checkStarted = false;
        private static bool _checkInProgress = false;

        public static bool IsCheckCompleted { get; private set; } = false;
        public static bool IsUpdateAvailable { get; private set; } = false;
        public static ReleaseInfo AvailableRelease { get; private set; } = null;
        public static bool PromptShownThisSession { get; set; } = false;
        public static bool IsDownloading { get; private set; } = false;
        public static string LastStatusMessage { get; private set; } = string.Empty;

        // F11 ручной триггер
        private const int VK_F11 = 0x7A;
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
        private static bool _f11WasPressed = false;

        private sealed class TimeoutWebClient : WebClient
        {
            private readonly int _timeoutMs;

            public TimeoutWebClient(int timeoutMs)
            {
                _timeoutMs = timeoutMs;
            }

            protected override WebRequest GetWebRequest(Uri address)
            {
                WebRequest request = base.GetWebRequest(address);
                if (request != null)
                {
                    request.Timeout = _timeoutMs;
                }
                return request;
            }
        }

        /// <summary>
        /// Запускает фоновую проверку при старте мода.
        /// </summary>
        public static void StartBackgroundCheck()
        {
            if (_checkStarted) return;
            _checkStarted = true;
            StartPeriodicCheckAsync();
        }

        /// <summary>
        /// Вызывается из хука главного меню Qud.UI.MainMenu.Update (MainMenu_Update_Postfix).
        /// </summary>
        public static void OnMainMenuUpdate(object mainMenuInstance)
        {
            try
            {
                // Проверка нажатия F11 для ручной проверки
                bool f11Down = (GetAsyncKeyState(VK_F11) & 0x8000) != 0;
                if (f11Down && !_f11WasPressed)
                {
                    CheckForUpdates(manual: true);
                }
                _f11WasPressed = f11Down;

                // Запуск автоматической проверки один раз при запуске игры
                if (!_checkStarted)
                {
                    StartBackgroundCheck();
                }

                // Если обновление найдено и еще не предлагалось в этой сессии
                if (IsUpdateAvailable && AvailableRelease != null && !PromptShownThisSession && !IsDownloading)
                {
                    TryShowAutoUpdatePrompt();
                }
            }
            catch (Exception ex)
            {
                TranslationEngine.LogError("[UpdateService] Ошибка в OnMainMenuUpdate: " + ex.Message);
            }
        }

        /// <summary>
        /// Запускает проверку обновлений (автоматически или вручную).
        /// </summary>
        public static void CheckForUpdates(bool manual)
        {
            Task.Run(() =>
            {
                PerformCheck(manual);
            });
        }

        private static void StartPeriodicCheckAsync()
        {
            // Проверяем, не проверяли ли мы недавно (для экономии трафика и GitHub Rate Limit)
            try
            {
                string storedTicksStr = PlayerPrefs.GetString(PrefKeyLastCheckTime, "0");
                if (long.TryParse(storedTicksStr, out long storedTicks) && storedTicks > 0)
                {
                    DateTime lastCheck = new DateTime(storedTicks, DateTimeKind.Utc);
                    if (DateTime.UtcNow - lastCheck < MinCheckInterval)
                    {
                        LastStatusMessage = "Недавняя проверка была выполнена менее 2 часов назад.";
                        return;
                    }
                }
            }
            catch
            {
                // Игнорируем ошибки доступа к PlayerPrefs
            }

            CheckForUpdates(manual: false);
        }

        private static void PerformCheck(bool manual)
        {
            lock (SyncLock)
            {
                if (_checkInProgress) return;
                _checkInProgress = true;
            }

            try
            {
                LastStatusMessage = "Проверка обновлений на GitHub...";
                TranslationEngine.LogInfo("[UpdateService] " + LastStatusMessage);

                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                }
                catch { }

                using (var client = new TimeoutWebClient(5000))
                {
                    client.Headers[HttpRequestHeader.UserAgent] = UserAgent;
                    client.Headers[HttpRequestHeader.Accept] = "application/vnd.github.v3+json";

                    string json;
                    try
                    {
                        json = client.DownloadString(ApiLatestReleaseUrl);
                    }
                    catch (WebException webEx)
                    {
                        LastStatusMessage = "Ошибка ответа GitHub: " + webEx.Message;
                        UnityEngine.Debug.LogWarning("[UpdateService] " + LastStatusMessage);
                        if (manual)
                        {
                            ShowInGameNotification("Не удалось проверить обновления: " + webEx.Message);
                        }
                        return;
                    }

                    ReleaseInfo release = ParseReleaseJson(json);
                    if (release == null)
                    {
                        LastStatusMessage = "Не удалось распарсить данные релиза GitHub.";
                        UnityEngine.Debug.LogWarning("[UpdateService] " + LastStatusMessage);
                        return;
                    }

                    Version currentVersion = GetCurrentInstalledVersion();
                    TranslationEngine.LogInfo("[UpdateService] Установлена версия: " + currentVersion + ", на GitHub доступна: " + release.Version);

                    // Сохраняем время успешной проверки
                    try
                    {
                        PlayerPrefs.SetString(PrefKeyLastCheckTime, DateTime.UtcNow.Ticks.ToString());
                        PlayerPrefs.Save();
                    }
                    catch { }

                    if (release.Version > currentVersion)
                    {
                        AvailableRelease = release;
                        IsUpdateAvailable = true;
                        LastStatusMessage = "Доступно обновление: " + release.TagName;
                        TranslationEngine.LogInfo("[UpdateService] " + LastStatusMessage);

                        if (manual)
                        {
                            PromptShownThisSession = false; // Принудительно показать окно по F11
                        }
                    }
                    else
                    {
                        IsUpdateAvailable = false;
                        AvailableRelease = null;
                        LastStatusMessage = "Установлена самая актуальная версия.";
                        TranslationEngine.LogInfo("[UpdateService] " + LastStatusMessage);

                        if (manual)
                        {
                            ShowInGameNotification("У вас установлена актуальная версия русификатора (" + currentVersion + ").");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LastStatusMessage = "Ошибка соединения: " + ex.Message;
                UnityEngine.Debug.LogWarning("[UpdateService] Ошибка при проверке GitHub: " + ex.Message);
                if (manual)
                {
                    ShowInGameNotification("Ошибка при проверке обновлений: " + ex.Message);
                }
            }
            finally
            {
                lock (SyncLock)
                {
                    _checkInProgress = false;
                    IsCheckCompleted = true;
                }
            }
        }

        private static ReleaseInfo ParseReleaseJson(string json)
        {
            try
            {
                JObject root = JObject.Parse(json);
                string tagName = root["tag_name"] != null ? root["tag_name"].ToString() : string.Empty;
                string title = root["name"] != null ? root["name"].ToString() : tagName;
                string body = root["body"] != null ? root["body"].ToString() : string.Empty;

                Version parsedVersion = NormalizeAndParseVersion(tagName);
                if (parsedVersion == null)
                {
                    parsedVersion = NormalizeAndParseVersion(title);
                }
                if (parsedVersion == null)
                {
                    return null;
                }

                string downloadUrl = string.Empty;
                long assetSize = 0;

                JArray assets = root["assets"] as JArray;
                if (assets != null)
                {
                    // 1. Приоритет: zip с модом
                    foreach (JToken asset in assets)
                    {
                        string name = asset["name"] != null ? asset["name"].ToString() : string.Empty;
                        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset["browser_download_url"] != null ? asset["browser_download_url"].ToString() : string.Empty;
                            long parsedSize;
                            if (asset["size"] != null && long.TryParse(asset["size"].ToString(), out parsedSize))
                            {
                                assetSize = parsedSize;
                            }
                            break;
                        }
                    }
                }

                // 2. Резерв: zipball исходников репозитория
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    downloadUrl = root["zipball_url"] != null ? root["zipball_url"].ToString() : string.Empty;
                }

                return new ReleaseInfo
                {
                    TagName = tagName,
                    Version = parsedVersion,
                    Title = title,
                    Changelog = CleanMarkdownChangelog(body),
                    DownloadUrl = downloadUrl,
                    AssetSizeBytes = assetSize
                };
            }
            catch (Exception ex)
            {
                TranslationEngine.LogError("[UpdateService] Ошибка парсинга JSON релиза: " + ex.Message);
                return null;
            }
        }

        public static Version NormalizeAndParseVersion(string input)
        {
            if (string.IsNullOrEmpty(input)) return null;

            Match match = Regex.Match(input, @"(\d+)\.(\d+)(?:\.(\d+))?(?:\.(\d+))?");
            if (!match.Success) return null;

            int major = int.Parse(match.Groups[1].Value);
            int minor = int.Parse(match.Groups[2].Value);
            int build = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
            int revision = match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : 0;

            return new Version(major, minor, build, revision);
        }

        public static Version GetCurrentInstalledVersion()
        {
            try
            {
                string modPath = TranslationEngine.CachedModPath;
                if (string.IsNullOrEmpty(modPath) || !Directory.Exists(modPath))
                {
                    string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    string localLow = Path.Combine(Path.GetDirectoryName(appData), "LocalLow");
                    modPath = Path.Combine(localLow, "Freehold Games", "CavesOfQud", "Mods", "RussianLocalization");
                }

                if (!string.IsNullOrEmpty(modPath) && Directory.Exists(modPath))
                {
                    string manifestPath = Path.Combine(modPath, "manifest.json");
                    if (File.Exists(manifestPath))
                    {
                        string content = File.ReadAllText(manifestPath, System.Text.Encoding.UTF8);
                        JObject j = JObject.Parse(content);
                        string vStr = j["Version"] != null ? j["Version"].ToString() : null;
                        Version v = NormalizeAndParseVersion(vStr);
                        if (v != null) return v;
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[UpdateService] Не удалось прочитать версию из manifest.json: " + ex.Message);
            }

            return new Version(1, 0, 8, 0);
        }

        private static string CleanMarkdownChangelog(string raw)
        {
            if (string.IsNullOrEmpty(raw) || string.IsNullOrEmpty(raw.Trim())) return "Список изменений отсутствует.";

            string text = raw.Replace("\r\n", "\n");
            // Убираем маркдаун-заголовки ###
            text = Regex.Replace(text, @"^#{1,6}\s*", "", RegexOptions.Multiline);
            // Заменяем маркдаун списки * и - на красивые маркеры
            text = Regex.Replace(text, @"^\s*[\*\-]\s+", "  • ", RegexOptions.Multiline);
            // Убираем жирный шрифт **текст**
            text = Regex.Replace(text, @"\*\*(.*?)\*\*", "$1");
            // Ограничиваем длину при очень больших описаниях
            if (text.Length > 1200)
            {
                text = text.Substring(0, 1190) + "\n... [полный список на GitHub]";
            }

            return text.Trim();
        }

        private static void TryShowAutoUpdatePrompt()
        {
            if (AvailableRelease == null) return;

            string snoozeKey = PrefKeySnoozedPrefix + AvailableRelease.TagName;
            try
            {
                if (PlayerPrefs.GetInt(snoozeKey, 0) == 1)
                {
                    return; // Пользователь уже нажал «Напомнить позже» для этой версии
                }
            }
            catch { }

            PromptShownThisSession = true;

            string dialogText =
                "{{G|Доступно обновление русского перевода Caves of Qud!}}\n\n" +
                "{{W|Новая версия:}} {{Y|" + AvailableRelease.TagName + "}} " +
                "{{K|(текущая: " + GetCurrentInstalledVersion() + ")}}\n\n" +
                "{{C|Что нового:}}\n" +
                AvailableRelease.Changelog + "\n\n" +
                "{{W|Скачать и установить обновление прямо сейчас?}}\n" +
                "{{K|(Игра перезапустится автоматически для применения изменений)}}";

            try
            {
                DialogResult result = Popup.ShowYesNo(dialogText, defaultResult: DialogResult.Yes);
                if (result == DialogResult.Yes)
                {
                    StartDownloadAndInstall(AvailableRelease);
                }
                else
                {
                    // Откладываем уведомление для этого релиза
                    try
                    {
                        PlayerPrefs.SetInt(snoozeKey, 1);
                        PlayerPrefs.Save();
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                TranslationEngine.LogError("[UpdateService] Ошибка отображения Popup.ShowYesNo: " + ex.Message);
            }
        }

        public static void StartDownloadAndInstall(ReleaseInfo release)
        {
            if (release == null || string.IsNullOrEmpty(release.DownloadUrl))
            {
                ShowInGameNotification("Ошибка: ссылка на скачивание релиза недоступна.");
                return;
            }

            IsDownloading = true;
            Task.Run(() =>
            {
                DownloadAndApply(release);
            });
        }

        private static void DownloadAndApply(ReleaseInfo release)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "CoQ_RussianLocalization_Update");
            string zipPath = Path.Combine(tempDir, "update.zip");
            string extractDir = Path.Combine(tempDir, "extracted");
            string batchPath = Path.Combine(tempDir, "apply_update.cmd");
            string ps1Path = Path.Combine(tempDir, "apply_update.ps1");

            try
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
                Directory.CreateDirectory(tempDir);
                Directory.CreateDirectory(extractDir);

                TranslationEngine.LogInfo("[UpdateService] Скачивание архива обновления: " + release.DownloadUrl);

                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                }
                catch { }

                using (var client = new TimeoutWebClient(180000))
                {
                    client.Headers[HttpRequestHeader.UserAgent] = UserAgent;
                    client.DownloadFile(release.DownloadUrl, zipPath);
                }

                TranslationEngine.LogInfo("[UpdateService] Архив скачан успешно. Размер: " + new FileInfo(zipPath).Length + " байт.");

                // Получаем пути
                string modPath = TranslationEngine.CachedModPath;
                if (string.IsNullOrEmpty(modPath) || !Directory.Exists(modPath))
                {
                    string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    string localLow = Path.Combine(Path.GetDirectoryName(appData), "LocalLow");
                    modPath = Path.Combine(localLow, "Freehold Games", "CavesOfQud", "Mods", "RussianLocalization");
                }

                string exePath = Process.GetCurrentProcess().MainModule.FileName;
                string exeDir = Path.GetDirectoryName(exePath);

                // Создаем скрипт обновления на PowerShell
                string ps1Content =
                    "$ErrorActionPreference = 'Stop'\n" +
                    "Start-Sleep -Seconds 2\n" +
                    "Write-Host 'Ожидание завершения Caves of Qud...'\n" +
                    "while (Get-Process -Name 'CoQ' -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 500 }\n" +
                    "Write-Host 'Распаковка файлов обновления...'\n" +
                    "Expand-Archive -LiteralPath '" + zipPath.Replace("'", "''") + "' -DestinationPath '" + extractDir.Replace("'", "''") + "' -Force\n" +
                    "$manifest = Get-ChildItem -Path '" + extractDir.Replace("'", "''") + "' -Filter 'manifest.json' -Recurse | Select-Object -First 1\n" +
                    "if ($manifest) { $source = $manifest.DirectoryName } else { $source = '" + extractDir.Replace("'", "''") + "' }\n" +
                    "$backupDir = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'CavesOfQud_Mod_Backups'\n" +
                    "if (-not (Test-Path $backupDir)) { New-Item -ItemType Directory -Force -Path $backupDir | Out-Null }\n" +
                    "$timestamp = (Get-Date -Format 'yyyyMMdd-HHmmss')\n" +
                    "$backup = Join-Path $backupDir ('RussianLocalization.backup-' + $timestamp)\n" +
                    "if (Test-Path -LiteralPath '" + modPath.Replace("'", "''") + "') {\n" +
                    "    Copy-Item -Path '" + modPath.Replace("'", "''") + "' -Destination $backup -Recurse -Force\n" +
                    "}\n" +
                    "Get-ChildItem -Path $source | ForEach-Object {\n" +
                    "    Copy-Item -Path $_.FullName -Destination '" + modPath.Replace("'", "''") + "' -Recurse -Force\n" +
                    "}\n" +
                    "Write-Host 'Обновление завершено. Перезапуск Caves of Qud...'\n" +
                    "Start-Process -FilePath '" + exePath.Replace("'", "''") + "' -WorkingDirectory '" + exeDir.Replace("'", "''") + "'\n" +
                    "Start-Sleep -Seconds 1\n" +
                    "Remove-Item -Path '" + tempDir.Replace("'", "''") + "' -Recurse -Force -ErrorAction SilentlyContinue\n";

                File.WriteAllText(ps1Path, ps1Content, System.Text.Encoding.UTF8);

                // Создаем CMD-запускатель для тихого старта
                string cmdContent =
                    "@echo off\r\n" +
                    "timeout /t 1 /nobreak >nul\r\n" +
                    "powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"" + ps1Path + "\"\r\n";

                File.WriteAllText(batchPath, cmdContent, System.Text.Encoding.ASCII);

                TranslationEngine.LogInfo("[UpdateService] Скрипт обновления сгенерирован: " + batchPath);

                // Запускаем процесс обновления
                var startInfo = new ProcessStartInfo
                {
                    FileName = batchPath,
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    WindowStyle = ProcessWindowStyle.Normal
                };

                Process.Start(startInfo);

                // Закрываем игру
                TranslationEngine.LogInfo("[UpdateService] Завершение процесса CoQ для применения обновления...");
                Application.Quit();
            }
            catch (Exception ex)
            {
                IsDownloading = false;
                TranslationEngine.LogError("[UpdateService] Сбой при обновлении: " + ex);
                ShowInGameNotification("Не удалось применить обновление: " + ex.Message);
            }
        }

        private static void ShowInGameNotification(string message)
        {
            try
            {
                Popup.Show(message);
            }
            catch
            {
                TranslationEngine.LogInfo("[UpdateService] " + message);
            }
        }
    }
}
