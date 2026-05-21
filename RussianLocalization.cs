using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Collections.Concurrent;
using HarmonyLib;
using XRL;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;

namespace RussianLocalization
{
    [HasModSensitiveStaticCache]
    public static class TranslationEngine
    {
        public static ConcurrentDictionary<string, string> staticDictionary = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public static ConcurrentDictionary<int, string> hashedDictionary = new ConcurrentDictionary<int, string>();
        public static ConcurrentDictionary<string, string> translationCache = new ConcurrentDictionary<string, string>();
        public static List<string> sortedKeys = new List<string>();
        public static object FileLock = new object();
        public static bool Initialized = false;

        // Автоматический сборщик непереведенных строк
        private static HashSet<string> loggedStrings = new HashSet<string>();
        private static object LogLock = new object();

        // Набор одушевленных существ для морфологического склонения
        private static HashSet<string> animates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "шакал", "краб", "жук", "бык", "бабуин", "гиршлинг", "козел", "волк", "червь", "паук", "слизень", "вестник",
            "садовник", "погонщик", "страж", "сектант", "шаман", "охотник", "разбойник", "рейдер", "пехотинец", "рыцарь",
            "гризли", "вор", "принц", "житель", "сталкер", "солдат", "стрелок", "лидер", "ткач", "киборг"
        };

        static TranslationEngine()
        {
            Initialize();
        }

        // Стабильный DJB2 алгоритм хэширования в нижнем регистре
        public static int GetStableHashCode(string str)
        {
            if (string.IsNullOrEmpty(str)) return 0;
            unchecked
            {
                int hash = 5381;
                for (int i = 0; i < str.Length; i++)
                {
                    char c = char.ToLowerInvariant(str[i]);
                    hash = ((hash << 5) + hash) ^ c;
                }
                return hash;
            }
        }

        public static void Initialize()
        {
            lock (FileLock)
            {
                if (Initialized) return;
                
                try
                {
                    string modPath = GetModPath();
                    if (string.IsNullOrEmpty(modPath)) return;

                    string dictPath = Path.Combine(modPath, "dictionary.json");
                    if (!File.Exists(dictPath)) return;

                    string jsonText = File.ReadAllText(dictPath, Encoding.UTF8);
                    var dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonText);
                    if (dict != null)
                    {
                        staticDictionary.Clear();
                        hashedDictionary.Clear();
                        
                        foreach (var kvp in dict)
                        {
                            string normKey = kvp.Key.Replace('\u00A0', ' ')
                                                    .Replace('\u2007', ' ')
                                                    .Replace('\u200B', ' ')
                                                    .Replace('\u202F', ' ')
                                                    .Trim();
                            staticDictionary[normKey] = kvp.Value;
                            
                            int keyHash = GetStableHashCode(normKey);
                            hashedDictionary[keyHash] = kvp.Value;
                        }

                        sortedKeys.Clear();
                        sortedKeys.AddRange(staticDictionary.Keys);
                        sortedKeys.Sort((x, y) => y.Length.CompareTo(x.Length));

                        Initialized = true;
                        UnityEngine.Debug.Log("[RussianLocalization] Initialized successfully. Loaded " + staticDictionary.Count + " keys.");
                    }
                }
                catch (Exception ex)
                {
                    XRL.Messages.MessageQueue.AddPlayerMessage("Ошибка инициализации локализации: " + ex.Message);
                    UnityEngine.Debug.LogError("[RussianLocalization] Init Error: " + ex.ToString());
                }
            }
        }

        private static string GetModPath()
        {
            try
            {
                ModInfo callingMod = null;
                System.Diagnostics.StackFrame stack = null;
                if (ModManager.TryGetCallingMod(out callingMod, out stack))
                {
                    if (callingMod != null && !string.IsNullOrEmpty(callingMod.Path))
                    {
                        return callingMod.Path;
                    }
                }
            }
            catch {}

            try
            {
                var runningMods = ModManager.GetRunningMods();
                if (runningMods != null)
                {
                    foreach (string mod in runningMods)
                    {
                        if (mod != null && (mod == "RussianLocalization" || mod.Contains("RussianLocalization")))
                        {
                            var modInfo = ModManager.GetMod(mod);
                            if (modInfo != null && !string.IsNullOrEmpty(modInfo.Path))
                            {
                                return modInfo.Path;
                            }
                        }
                    }
                }
            }
            catch {}

            string defaultPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low",
                @"Freehold Games\CavesOfQud\Mods\RussianLocalization"
            );
            if (Directory.Exists(defaultPath))
            {
                return defaultPath;
            }

            return null;
        }

        public static string Translate(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (!ContainsEnglish(text)) return text;

            // Нормализуем все типы неразрывных и невидимых пробелов в стандартный ASCII пробел
            string normalized = text.Replace('\u00A0', ' ')
                                    .Replace('\u2007', ' ')
                                    .Replace('\u200B', ' ')
                                    .Replace('\u202F', ' ');

            string trimmed = normalized.Trim();
            if (trimmed.Length == 0) return text;

            // Проверяем кэш переводов по оригинальной входящей строке
            if (translationCache.TryGetValue(text, out string cached))
            {
                return cached;
            }

            // Ищем точное совпадение в словаре по хэш-коду DJB2
            int textHash = GetStableHashCode(trimmed);
            if (hashedDictionary.TryGetValue(textHash, out string exactMatch))
            {
                // Применяем морфологию, если это контекст винительного падежа
                if (IsAccusativeContext(text))
                {
                    exactMatch = DeclinePhraseToAccusative(exactMatch);
                }
                
                string result = normalized.Replace(trimmed, exactMatch);
                translationCache[text] = result;
                return result;
            }

            // Пытаемся сделать пофразовые замены на нормализованном тексте
            string processedText = TryWordReplacement(normalized);
            
            // Если текст остался на английском (или содержит английские буквы) и его нет в словаре - логируем
            if (ContainsEnglish(processedText) && !hashedDictionary.ContainsKey(textHash))
            {
                LogUntranslated(trimmed);
            }

            translationCache[text] = processedText;
            return processedText;
        }

        private static string TryWordReplacement(string text)
        {
            string result = text;

            for (int i = 0; i < sortedKeys.Count; i++)
            {
                // Если в строке больше нет английских букв, перевод завершен, выходим досрочно
                if (!ContainsEnglish(result)) return result;

                string key = sortedKeys[i];
                // Если ключ пустой или он длиннее, чем текущая строка, он не может быть подстрокой
                if (string.IsNullOrEmpty(key) || result.Length < key.Length) continue;

                if (staticDictionary.TryGetValue(key, out string translation))
                {
                    int index = result.IndexOf(key, StringComparison.OrdinalIgnoreCase);
                    while (index != -1)
                    {
                        // Защита границ слов: предотвращает ложные совпадения (например, "int" внутри "points")
                        bool isWordBoundaryStart = true;
                        if (index > 0)
                        {
                            char prev = result[index - 1];
                            if (char.IsLetterOrDigit(prev) && char.IsLetterOrDigit(key[0]))
                            {
                                // Проверка на спец-теги цветов Caves of Qud, например, "&Y" или "^R"
                                bool isColorTag = false;
                                if (index >= 2 && (result[index - 2] == '&' || result[index - 2] == '^'))
                                {
                                    isColorTag = true;
                                }
                                
                                if (!isColorTag)
                                {
                                    isWordBoundaryStart = false;
                                }
                            }
                        }

                        bool isWordBoundaryEnd = true;
                        if (index + key.Length < result.Length)
                        {
                            char next = result[index + key.Length];
                            if (char.IsLetterOrDigit(next) && char.IsLetterOrDigit(key[key.Length - 1]))
                            {
                                isWordBoundaryEnd = false;
                            }
                        }

                        if (isWordBoundaryStart && isWordBoundaryEnd)
                        {
                            if (translation.Length > 0)
                            {
                                string finalTrans = translation;
                                
                                // Применяем морфологию винительного падежа при пофразовой замене
                                if (IsAccusativeContext(text))
                                {
                                    finalTrans = DeclinePhraseToAccusative(finalTrans);
                                }

                                char origChar = result[index];
                                if (char.IsLower(origChar))
                                {
                                    bool skipLowering = char.IsUpper(finalTrans[0]) && 
                                        (key.StartsWith("the ", StringComparison.OrdinalIgnoreCase) || 
                                         key.StartsWith("a ", StringComparison.OrdinalIgnoreCase) || 
                                         key.StartsWith("an ", StringComparison.OrdinalIgnoreCase));

                                    if (!skipLowering)
                                    {
                                        finalTrans = char.ToLower(finalTrans[0]) + finalTrans.Substring(1);
                                    }
                                }
                                else if (char.IsUpper(origChar))
                                {
                                    finalTrans = char.ToUpper(finalTrans[0]) + finalTrans.Substring(1);
                                }

                                result = result.Remove(index, key.Length).Insert(index, finalTrans);
                                index = result.IndexOf(key, index + finalTrans.Length, StringComparison.OrdinalIgnoreCase);
                            }
                            else
                            {
                                break;
                            }
                        }
                        else
                        {
                            // Это ложное срабатывание подстроки, пропускаем его и ищем дальше
                            index = result.IndexOf(key, index + 1, StringComparison.OrdinalIgnoreCase);
                        }
                    }
                }
            }

            return result;
        }

        private static bool ContainsEnglish(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
                {
                    return true;
                }
            }
            return false;
        }

        private static void LogUntranslated(string text)
        {
            try
            {
                string trimmed = text.Trim();
                if (trimmed.Length < 3) return; // Пропускаем короткие фразы
                
                // Пропускаем технические теги Unity/TMPro
                if (trimmed.StartsWith("<") && trimmed.EndsWith(">")) return;

                lock (LogLock)
                {
                    if (!loggedStrings.Contains(trimmed))
                    {
                        loggedStrings.Add(trimmed);
                    }
                }
            }
            catch {}
        }

        // --- МОРФОЛОГИЧЕСКИЙ МОДУЛЬ SMART WORD REPLACEMENT ---

        private static bool IsAccusativeContext(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string lower = text.ToLowerInvariant();
            return lower.Contains("hit ") || 
                   lower.Contains("kill ") || 
                   lower.Contains("attack ") || 
                   lower.Contains("bite ") || 
                   lower.Contains("strike ") || 
                   lower.Contains("defeat ") || 
                   lower.Contains("garrote ") || 
                   lower.Contains("destroy ") ||
                   lower.Contains("equip ") ||
                   lower.Contains("equipped ");
        }

        private static bool IsConsonant(char c)
        {
            return "бвгджзклмнпрстфхцчшщ".Contains(char.ToLowerInvariant(c));
        }

        private static bool IsAnimateNoun(string word)
        {
            string lower = word.ToLowerInvariant();
            if (animates.Contains(lower)) return true;
            return lower.EndsWith("ец") || lower.EndsWith("тель") || lower.EndsWith("ник") || lower.EndsWith("арь");
        }

        private static string DeclinePhraseToAccusative(string phrase)
        {
            if (string.IsNullOrEmpty(phrase)) return phrase;
            
            // Если фраза содержит HTML-теги или Caves of Qud разметку, пропускаем её склонение во избежание поломок
            if (phrase.Contains("<") || phrase.Contains("&") || phrase.Contains("^")) return phrase;

            string[] words = phrase.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                string word = words[i];
                if (word.Length < 3) continue;

                // Проверяем женский род прилагательных
                if (word.EndsWith("ая"))
                {
                    words[i] = word.Substring(0, word.Length - 2) + "ую";
                }
                else if (word.EndsWith("яя"))
                {
                    words[i] = word.Substring(0, word.Length - 2) + "юю";
                }
                // Женский род существительных
                else if (word.EndsWith("а") && !word.EndsWith("ова") && !word.EndsWith("ева"))
                {
                    words[i] = word.Substring(0, word.Length - 1) + "у";
                }
                else if (word.EndsWith("я"))
                {
                    words[i] = word.Substring(0, word.Length - 1) + "ю";
                }
                // Мужской род одушевленных существ
                else if (IsAnimateNoun(word))
                {
                    if (word.EndsWith("ь"))
                    {
                        words[i] = word.Substring(0, word.Length - 1) + "я";
                    }
                    else if (word.EndsWith("й"))
                    {
                        words[i] = word.Substring(0, word.Length - 1) + "я";
                    }
                    else if (IsConsonant(word[word.Length - 1]))
                    {
                        words[i] = word + "а";
                    }
                }
            }
            return string.Join(" ", words);
        }
    }

    // --- УТИЛИТЫ ДЛЯ ШРИФТОВ (DYNAMIC CYRILLIC INJECTION) ---
    public static class FontUtils
    {
        private static TMP_FontAsset cyrillicFallback = null;
        private static HashSet<int> processedFonts = new HashSet<int>();

        public static void EnsureCyrillicFallback(TMP_Text textComponent)
        {
            if (textComponent == null) return;
            TMP_FontAsset currentFont = textComponent.font;
            if (currentFont == null) return;

            int fontId = currentFont.GetInstanceID();
            if (processedFonts.Contains(fontId)) return;

            try
            {
                if (cyrillicFallback == null)
                {
                    cyrillicFallback = FindCyrillicFontAsset();
                }

                if (cyrillicFallback != null && currentFont != cyrillicFallback)
                {
                    if (currentFont.fallbackFontAssetTable == null)
                    {
                        currentFont.fallbackFontAssetTable = new List<TMP_FontAsset>();
                    }

                    if (!currentFont.fallbackFontAssetTable.Contains(cyrillicFallback))
                    {
                        currentFont.fallbackFontAssetTable.Add(cyrillicFallback);
                        UnityEngine.Debug.Log("[RussianLocalization] Injected cyrillic fallback '" + cyrillicFallback.name + "' into '" + currentFont.name + "'");
                    }
                }
                
                processedFonts.Add(fontId);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[RussianLocalization] Font Injection Error: " + ex.ToString());
            }
        }

        private static TMP_FontAsset FindCyrillicFontAsset()
        {
            TMP_FontAsset[] allFonts = null;
            try
            {
                allFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[RussianLocalization] Error finding fonts: " + ex.Message);
                return null;
            }

            if (allFonts == null) return null;

            TMP_FontAsset liberation = null;
            TMP_FontAsset arial = null;
            TMP_FontAsset anyCyrillic = null;

            foreach (var font in allFonts)
            {
                if (font == null) continue;
                
                string nameLower = font.name.ToLower();
                bool hasCyrillic = false;

                try
                {
                    hasCyrillic = font.HasCharacter('а') || font.HasCharacter((char)1072);
                }
                catch
                {
                    hasCyrillic = nameLower.Contains("cyrillic") || nameLower.Contains("russian") || nameLower.Contains("liberation") || nameLower.Contains("arial");
                }

                if (hasCyrillic)
                {
                    if (nameLower.Contains("liberationsans") || nameLower.Contains("liberation sans"))
                    {
                        liberation = font;
                    }
                    else if (nameLower.Contains("arial"))
                    {
                        arial = font;
                    }
                    else
                    {
                        anyCyrillic = font;
                    }
                }
            }

            if (liberation != null) return liberation;
            if (arial != null) return arial;
            return anyCyrillic;
        }
    }

    // --- HARMONY PATCHES (STABLE & HIGH-PERFORMANCE) ---

    [HarmonyPatch(typeof(UnityEngine.UI.Text), "text", MethodType.Setter)]
    public static class UnityUIText_Patch
    {
        public static void Prefix(ref string value)
        {
            if (TranslationEngine.Initialized)
            {
                value = TranslationEngine.Translate(value);
            }
        }
    }

    [HarmonyPatch(typeof(TMPro.TMP_Text), "text", MethodType.Setter)]
    public static class TMPText_Patch
    {
        public static void Prefix(ref string value)
        {
            if (TranslationEngine.Initialized)
            {
                value = TranslationEngine.Translate(value);
            }
        }
    }

    [HarmonyPatch(typeof(TMPro.TMP_Text), "SetText", new Type[] { typeof(string) })]
    public static class TMPText_SetText_Patch
    {
        public static void Prefix(ref string sourceText)
        {
            if (TranslationEngine.Initialized)
            {
                sourceText = TranslationEngine.Translate(sourceText);
            }
        }
    }

    [HarmonyPatch(typeof(TMPro.TMP_Text), "SetText", new Type[] { typeof(string), typeof(float) })]
    public static class TMPText_SetText_Float1_Patch
    {
        public static void Prefix(ref string sourceText)
        {
            if (TranslationEngine.Initialized)
            {
                sourceText = TranslationEngine.Translate(sourceText);
            }
        }
    }

    [HarmonyPatch(typeof(TMPro.TMP_Text), "SetText", new Type[] { typeof(string), typeof(float), typeof(float) })]
    public static class TMPText_SetText_Float2_Patch
    {
        public static void Prefix(ref string sourceText)
        {
            if (TranslationEngine.Initialized)
            {
                sourceText = TranslationEngine.Translate(sourceText);
            }
        }
    }

    [HarmonyPatch(typeof(TMPro.TMP_Text), "SetText", new Type[] { typeof(string), typeof(float), typeof(float), typeof(float) })]
    public static class TMPText_SetText_Float3_Patch
    {
        public static void Prefix(ref string sourceText)
        {
            if (TranslationEngine.Initialized)
            {
                sourceText = TranslationEngine.Translate(sourceText);
            }
        }
    }

    // --- ФОНТ-ИНЖЕКЦИЯ ХУКИ ---

    [HarmonyPatch(typeof(TMPro.TextMeshPro), "Awake")]
    public static class TextMeshPro_Awake_Patch
    {
        public static void Postfix(TMPro.TextMeshPro __instance)
        {
            if (TranslationEngine.Initialized && __instance != null)
            {
                FontUtils.EnsureCyrillicFallback(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(TMPro.TextMeshProUGUI), "Awake")]
    public static class TextMeshProUGUI_Awake_Patch
    {
        public static void Postfix(TMPro.TextMeshProUGUI __instance)
        {
            if (TranslationEngine.Initialized && __instance != null)
            {
                FontUtils.EnsureCyrillicFallback(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(TMPro.TMP_Text), "font", MethodType.Setter)]
    public static class TMPText_Font_Patch
    {
        public static void Postfix(TMPro.TMP_Text __instance)
        {
            if (TranslationEngine.Initialized && __instance != null)
            {
                FontUtils.EnsureCyrillicFallback(__instance);
            }
        }
    }
}
