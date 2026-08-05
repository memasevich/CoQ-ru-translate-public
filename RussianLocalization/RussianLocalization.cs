using System;

using System.IO;

using System.Text;

using System.Collections.Generic;

using System.Collections.Concurrent;

using System.Reflection;

using HarmonyLib;

using Newtonsoft.Json;

using TMPro;

using UnityEngine;

using XRL;

using ConsoleLib.Console;



namespace RussianLocalization

{

    [HasModSensitiveStaticCache]

    public static class TranslationEngine

    {

        public static ConcurrentDictionary<string, string> staticDictionary = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        public static ConcurrentDictionary<string, string> wordDictionary = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static ConcurrentDictionary<string, string> translationCache = new ConcurrentDictionary<string, string>();

        public static List<string> sortedKeys = new List<string>();

        public static List<string> sortedWordKeys = new List<string>();

        public static List<KeyValuePair<System.Text.RegularExpressions.Regex, string>> patternDictionary = new List<KeyValuePair<System.Text.RegularExpressions.Regex, string>>();

        public static ConcurrentDictionary<string, string> normalizedKeyDictionary = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static Dictionary<string, Dictionary<string, string>> factionCases = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        public static object FileLock = new object();

        public static bool Initialized = false;

        // 2026-07-06 (v17): захватываем ID главного потока Unity при инициализации мода — нужно
        // проверить гипотезу, что после патча 1.0.5/211.45 XRL.UI.UITextSkin.SetText() стал
        // вызываться не только из главного потока (например, из воркера построения UI Toolkit),
        // и наш тяжёлый Translate() внутри Harmony-префикса небезопасен в этом контексте.
        public static int MainThreadId = -1;

        /// <summary>
        /// Флаг вкл/выкл перевода. Можно переключать хоткеем (по умолч. F1).
        /// </summary>
        public static bool IsEnabled = true;

        public static string CachedModPath = null;

        // FIX B3 (2026-07-20): условный флаг файлового дебаг-логирования.
        // false (по умолчанию) — дебаг-файлы (popup_args_debug.txt, screenbuffer_text_debug.txt,
        // generate_tooltip_output.txt, screenbuffer_methods.txt, show_method_check.txt,
        // lambda_calls.txt) НЕ пишутся. true — поведение как раньше (для диагностики).
        // internal, а не private — флаг читается также из Look_Patch (отдельный класс).
        internal static readonly bool DebugFileLogging = false;

        // FIX B1 (2026-07-20): методы Popup, чей string-результат — ВВОД ПОЛЬЗОВАТЕЛЯ
        // или служебное значение, а не отображаемый текст. На них postfix-перевод
        // __result не вешается (AskString — текст игрока, ShowColorPicker — код цвета).
        private static readonly System.Collections.Generic.HashSet<string> PopupPostfixSkipMethods =
            new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
            { "AskString", "AskStringAsync", "ShowColorPicker", "ShowColorPickerAsync" };

        // FIX B2 (2026-07-20): служебные параметры Popup-методов — ключи логики игры
        // (Commands/Hotkeys/CommandLine), их переводить НЕЛЬЗЯ. Переводим только
        // отображаемые тексты (Message, Title, Options и пр.).
        private static readonly System.Collections.Generic.HashSet<string> PopupServiceParamNames =
            new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
            { "Commands", "Hotkeys", "CommandLine" };

        // Имя файла лога с датой для записи в Documents
        public static string GameplayLogFileName = null;

        // 2026-07-06 (v19): диагностический флаг — пропускает весь блок regex-очистки внутри
        // Translate() (включая do/while со схлопыванием цветовых блоков через back-reference regex),
        // чтобы проверить гипотезу катастрофического backtracking как причины краша в торговле.
        // 2026-07-06 (v20): блок очистки НЕ был виноват (краш пережил его отключение) — возвращаем
        // обратно. Настоящий кандидат — рекурсия TranslateInternal без лимита глубины (см. выше).
        public const bool Translate_DIAG_SKIP_CLEANUP_REGEX = false;

        public static int disableWordReplacementCounter;

        public static bool EnableFileLogging = true;

        // Лимит кэша переводов. Сбрасываем только при превышении, не на каждом вызове.
        private const int TranslationCacheMaxEntries = 200000;
        // Сбрасываем кэш не чаще, чем раз в N мс, чтобы не нагружать GC.
        private static long lastCacheResetMs;
        private const long CacheResetIntervalMs = 30000;

        /// <summary>
        /// Периодически сбрасывает translationCache, чтобы избежать неограниченного роста.
        /// Использует Interlocked counter для дешёвой проверки на горячем пути.
        /// </summary>
        private static int cacheCallCounter;
        private static void MaybeResetTranslationCache()
        {
            int n = System.Threading.Interlocked.Increment(ref cacheCallCounter);
            // Проверяем размер и время только каждый 1024-й вызов.
            if ((n & 1023) != 0) return;
            if (translationCache.Count < TranslationCacheMaxEntries) return;
            // Environment.TickCount (int) переполняется через 24.8 дней; используем Interlocked для атомарного доступа.
            int nowMs = Environment.TickCount;
            int lastMs = (int)System.Threading.Interlocked.Read(ref lastCacheResetMs);
            if (lastMs != 0 && (uint)(nowMs - lastMs) < (uint)CacheResetIntervalMs) return;
            // Защита от гонки при сбросе: только один поток выполнит реальный сброс.
            if (System.Threading.Interlocked.CompareExchange(ref lastCacheResetMs, (long)nowMs, (long)lastMs) == (long)lastMs)
            {
                translationCache.Clear();
                producedOutputs.Clear(); // Guard B: сбрасываем вместе с кэшем, чтобы не росло бесконечно.
            }
        }

        private static readonly System.Text.RegularExpressions.Regex RelationInterestRegex = new System.Text.RegularExpressions.Regex(@"^(?<subj>are|is)\s+interested\s+in\s+(?<verb>trading\s+secrets\s+about|sharing\s+secrets\s+about|learning\s+about|sharing\s+secrets\s+of|hearing\s+gossip\s+that\'s\s+about|hearing\s+gossip\s+about|the\s+resources\s+necessary\s+for\s+building\s+new\s+societies:)\s+(?<rest>.*)$", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        private static readonly System.Text.RegularExpressions.Regex RelationAlsoRegex = new System.Text.RegularExpressions.Regex(@"\.\s*They\'re\s+also\s+interested\s+in\s+(?<clause>.*)$", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        private static readonly System.Text.RegularExpressions.Regex RelationGossip1Regex = new System.Text.RegularExpressions.Regex(@"\.\s*They\'re\s+also\s+interested\s+in\s+(?:hearing\s+)?gossip\s+that\'s\s+about\s+them$", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        private static readonly System.Text.RegularExpressions.Regex RelationGossip2Regex = new System.Text.RegularExpressions.Regex(@",\s*and\s+(?:hearing\s+)?gossip\s+that\'s\s+about\s+them$", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        private static readonly System.Text.RegularExpressions.Regex RelationSultansRegex = new System.Text.RegularExpressions.Regex(@"\bsultans\s+they\s+admire\s+or\s+despise\b", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        private static readonly System.Text.RegularExpressions.Regex ColorWrapperRegex = new System.Text.RegularExpressions.Regex(@"^(?<pref><color=[^>]+>)(?<content>.*?)(?<suff></color>)$", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        private static readonly System.Text.RegularExpressions.Regex ColorBlockRegex = new System.Text.RegularExpressions.Regex(@"(?<pref><color=[^>]+>)(?<content>.*?)(?<suff></color>)", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        private static readonly System.Text.RegularExpressions.Regex ShadowRegex = new System.Text.RegularExpressions.Regex(@"(?<core>.+?)(?<deco>\s*(?:!|\.|\?)*\s*(?:\[?\d+(?:\s+vs\s+\d+)?\]?|\(unburnt\)|x\d+)(?:!|\.|\?)*)$", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private static readonly HashSet<string> InternalGameKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "BodyText", "DisplayName", "ConText", "LongDescription",
            "WoundLevel", "WoundLevel2", "Name", "Title", "Description",
            "PlainName", "ShortDescription", "LongDescription",
            "RenderString", "RenderStringSimple", "DisplayNameShort",
            "DisplayNameLong", "DisplayNameStripped"
        };



        // Потокобезопасный сборщик непереведенных строк

        // Потокобезопасный сборщик непереведенных строк (без lock через ConcurrentDictionary)
        private static ConcurrentDictionary<string, byte> loggedStrings = new ConcurrentDictionary<string, byte>();



        // Потокобезопасный сборщик пословных автозамен (для отлова Франкенштейнов)
        private static ConcurrentDictionary<string, byte> loggedReplacements = new ConcurrentDictionary<string, byte>();



        // Потокобезопасный сборщик вообще всего игрового текста (и русского, и английского)
        // Оптимизация: используем ConcurrentDictionary вместо HashSet+lock, чтобы убрать contention на каждый вызов Translate.
        private static ConcurrentDictionary<string, byte> loggedAllTexts = new ConcurrentDictionary<string, byte>();

        // 2026-07-22 (Guard B — защита от двойного прогона): множество УЖЕ ВЫДАННЫХ нами
        // СМЕШАННЫХ переводов (кириллица + латиница). Игра иногда подаёт готовый перевод обратно
        // в Translate(); точного совпадения по ключу-ОРИГИНАЛУ нет (это результат, а не вход),
        // и пословный проход портит бренды/ключи внутри ("Steam Input"->"Пар Ввод",
        // "+Tab"->"+Вкладка"). Если строка на входе — наш прошлый результат, возвращаем её как есть.
        // Регистрируем ТОЛЬКО смешанные строки: чистый русский уже отсекается проверкой выше,
        // а составные имена предметов выходят чисто по-русски и сюда не попадают.
        private static ConcurrentDictionary<string, byte> producedOutputs = new ConcurrentDictionary<string, byte>();



        // 2026-07-30: класс символов ВНУТРИ тега исключает '<', иначе одиночный '<' съедается.
        // Панель способностей присылает хоткей разорванным по цветовым регионам:
        //   <color=#FFFFFFFF><</color><color=#98875FFF>6</color><color=#FFFFFFFF>></color>
        // Со старым @"<[^>]+>" подстрока "<</color>" целиком опознавалась как тег (потому что
        // [^>]+ поглощал "</color"), и '<' исчезал и из origStrip, и из карты цветов
        // ExtractColors. На экране выходило «Бурный рост 6>» вместо «Бурный рост <6>» —
        // 87 строк лога 30.07 (все способности: Sting, Toast, Telepathy, Deploy Turret...).
        // С @"<[^<>]+>" совпадение начинается со второго '<', и одиночный '<' остаётся текстом.
        //
        // 2026-08-04: та же болезнь в НЕразорванном виде. Когда панель присылает хоткей одним
        // блоком — "<color=#98875FFF><6></color>" — предыдущая правка не помогает: "<6>"
        // подходит под <[^<>]+> целиком и опознаётся как тег. Хоткей исчезал из origStrip, и
        // на экране оставалось «Бурный рост» без «<6>» (в логе 03.08 — 20 строк: Бурный рост,
        // Поджарить, Телепатия, Жалить, Установка турели, Луч замораживания, "<X> Photonic...",
        // "Some Ability Name <W>").
        // Негативный просмотр (?![A-Z0-9]>) исключает ОДИНОЧНЫЕ прописные буквы и цифры —
        // ими не бывает ни одного настоящего тега TextMeshPro. Разметочные "<b>", "<i>",
        // "<u>", "<s>" — строчные, под исключение не попадают и по-прежнему снимаются.
        private static readonly System.Text.RegularExpressions.Regex TagRegex = new System.Text.RegularExpressions.Regex(@"<(?![A-Z0-9]>)[^<>]+>");

        private static readonly System.Text.RegularExpressions.Regex ModernUIMenuRegex = new System.Text.RegularExpressions.Regex(@"^\[([^\]]+)\]\s*(.*)$");

        private static readonly System.Text.RegularExpressions.Regex InlineKeyRegex = 
            new System.Text.RegularExpressions.Regex(@"^<color=[^>]+>([^<]+)</color><color=[^>]+>(.*)</color>$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private static readonly System.Text.RegularExpressions.Regex ColorBracketKeyRegex = 
            new System.Text.RegularExpressions.Regex(@"^<color=[^>]+>\[([^\]]+)\]</color>\s*(?:<color=[^>]+>)?(.*?)(?:</color>)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private static readonly System.Text.RegularExpressions.Regex ParagraphSplitRegex = 
            new System.Text.RegularExpressions.Regex(@"(?:\r?\n)+(?:</?color(?:=#[0-9A-Fa-f]+)?>|\s+)*(?:\r?\n)+", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private static readonly System.Text.RegularExpressions.Regex FactionSplitRegex = 
            new System.Text.RegularExpressions.Regex(@"(?=<color=#[0-9A-Fa-f]+>[^<\r\n]+</color><color=#[0-9A-Fa-f]+>\s*(?:are\s+interested|is\s+interested|don't\s+care|doesn't\s+care|despise|dislike|favor))", System.Text.RegularExpressions.RegexOptions.IgnoreCase);



        public static string TryTranslateModernUI(string text, out bool success)
        {
            success = false;
            if (string.IsNullOrEmpty(text)) return text;

            string strippedForMatch = text.Contains("<color=") ? TagRegex.Replace(text, "") : text;

            // 0. Чистые хоткеи в скобках: [Esc], [Space], [PgUp] и т.д.
            // Восстанавливаем структуру цвета: [bracket-color]key-color[bracket-color]
            var pureHotkeyMatch = System.Text.RegularExpressions.Regex.Match(strippedForMatch, @"^\[([^\]]+)\]\s*$");
            if (pureHotkeyMatch.Success)
            {
                string rawKey = pureHotkeyMatch.Groups[1].Value.Trim();
                string key = MapCyrillicHotkeyToEnglish(rawKey);
                if (IsHotkey(key) && text.Contains("<color="))
                {
                    // Паттерн 3-сегментный: <color=C1>[</color><color=C2>key</color><color=C3>]</color>
                    // или 1-сегментный: <color=C>[key]</color>
                    var m3 = System.Text.RegularExpressions.Regex.Match(text,
                        @"^<color=(?<c1>[^>]+)>\[</color><color=(?<c2>[^>]+)>" + System.Text.RegularExpressions.Regex.Escape(rawKey) + @"</color><color=(?<c3>[^>]+)>\](?:</color>)?(?<trail>.*)$",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (m3.Success)
                    {
                        success = true;
                        string c1 = m3.Groups["c1"].Value;
                        string c2 = m3.Groups["c2"].Value;
                        string c3 = m3.Groups["c3"].Value;
                        string trail = m3.Groups["trail"].Value;
                        return "<color=" + c1 + ">[</color><color=" + c2 + ">" + key + "</color><color=" + c3 + ">]</color>" + trail;
                    }
                    // 1-сегментный
                    var m1 = System.Text.RegularExpressions.Regex.Match(text,
                        @"^<color=(?<c>[^>]+)>\[" + System.Text.RegularExpressions.Regex.Escape(rawKey) + @"\](?:</color>)?(?<trail>.*)$",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (m1.Success)
                    {
                        success = true;
                        return "<color=" + m1.Groups["c"].Value + ">[" + key + "]</color>" + m1.Groups["trail"].Value;
                    }
                    // Общий случай — доминантный цвет
                    success = true;
                    string dc = GetDominantColor(text);
                    if (dc != null) return "<color=" + dc + ">[" + key + "]</color>";
                    return "[" + key + "]";
                }
                if (IsHotkey(key))
                {
                    success = true;
                    return "[" + key + "]";
                }
            }

            // 0b. Хоткей + действие с цветами: реконструкция по сегментам
            if (text.Contains("<color="))
            {
                // Паттерн: <color=C1>[key]</color><color=C2> </color><color=C3>action</color>
                var hotkeyActionM = System.Text.RegularExpressions.Regex.Match(text,
                    @"^(?<pre><color=[^>]+>\[</color><color=[^>]+>[^\[]+?</color><color=[^>]+>\](?:</color>)?)(?<sp><color=[^>]+>\s*</color>)?(?<act><color=[^>]+>.+?</color>)(?<trail>.*)$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
                if (hotkeyActionM.Success)
                {
                    string keySegment = hotkeyActionM.Groups["pre"].Value;
                    string spacerSeg = hotkeyActionM.Groups["sp"].Success ? hotkeyActionM.Groups["sp"].Value : "";
                    string actSegment = hotkeyActionM.Groups["act"].Value;
                    string trail = hotkeyActionM.Groups["trail"].Value;

                    string keyStripped = TagRegex.Replace(keySegment, "").Trim();
                    var keyM = System.Text.RegularExpressions.Regex.Match(keyStripped, @"^\[([^\]]+)\]$");
                    if (keyM.Success)
                    {
                        string rawKey = keyM.Groups[1].Value;
                        string key = MapCyrillicHotkeyToEnglish(rawKey);
                        if (IsHotkey(key))
                        {
                            string actionStripped = TagRegex.Replace(actSegment, "").Trim();
                            string translatedAction = TranslateTextStrict(actionStripped);
                            if ((translatedAction != actionStripped && !string.IsNullOrEmpty(translatedAction)) || key != rawKey)
                            {
                                success = true;
                                string finalAction = (translatedAction != actionStripped && !string.IsNullOrEmpty(translatedAction)) ? translatedAction : actionStripped;
                                string keySegmentMapped = ReplaceRawKeyInSegment(keySegment, rawKey, key);
                                return keySegmentMapped + spacerSeg + "<color=" + GetDominantColor(actSegment) + ">" + finalAction + "</color>" + trail;
                            }
                        }
                    }
                }
            }

            // 1. Попытка перевода текста без тегов с последующим распределением цветов
            if (text.Contains("<color="))
            {
                string translatedStripped = TranslateTextStrict(strippedForMatch);
                if (translatedStripped != strippedForMatch && !string.IsNullOrEmpty(translatedStripped))
                {
                    // Хоткей в скобках + действие — пропускаем через ModernUIMenuRegex
                    var bracketM = ModernUIMenuRegex.Match(strippedForMatch);
                    if (bracketM.Success)
                    {
                        string rawKey = bracketM.Groups[1].Value;
                        string key = MapCyrillicHotkeyToEnglish(rawKey);
                        if (IsHotkey(key))
                        {
                            string translatedAction = bracketM.Groups[2].Value.Trim();
                            string translatedActionText = TranslateTextStrict(translatedAction);
                            if ((translatedActionText != translatedAction && !string.IsNullOrEmpty(translatedActionText)) || key != rawKey)
                            {
                                success = true;
                                string finalAction = (translatedActionText != translatedAction && !string.IsNullOrEmpty(translatedActionText)) ? translatedActionText : translatedAction;
                                string result = string.Format("[{0}] {1}", key, finalAction);
                                string dominantColor = GetDominantColor(text);
                                if (dominantColor != null)
                                {
                                    return "<color=" + dominantColor + ">" + result + "</color>";
                                }
                                return result;
                            }
                        }
                    }

                    success = true;
                    return DistributeColors(text, translatedStripped);
                }
            }

            // 2. Безцветный хоткей + действие
            var bracketMatch = ModernUIMenuRegex.Match(strippedForMatch);
            if (bracketMatch.Success)
            {
                string rawKey = bracketMatch.Groups[1].Value;
                string key = MapCyrillicHotkeyToEnglish(rawKey);
                if (IsHotkey(key))
                {
                    string action = bracketMatch.Groups[2].Value.Trim();
                    string translatedAction = TranslateTextStrict(action);
                    if ((translatedAction != action && !string.IsNullOrEmpty(translatedAction)) || key != rawKey)
                    {
                        success = true;
                        string finalAction = (translatedAction != action && !string.IsNullOrEmpty(translatedAction)) ? translatedAction : action;
                        string finalTranslated = string.Format("[{0}] {1}", key, finalAction);
                        if (text.Contains("<color="))
                        {
                            string dominantColor = GetDominantColor(text);
                            if (dominantColor != null)
                            {
                                return "<color=" + dominantColor + ">" + finalTranslated + "</color>";
                            }
                            return finalTranslated;
                        }
                        return finalTranslated;
                    }
                }
            }

            return text;
        }

        private static string MapCyrillicHotkeyToEnglish(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;
            if (key.Length == 1)
            {
                char ch = key[0];
                char mapped = MapCyrillicCharToEnglish(ch);
                if (mapped != ch) return mapped.ToString();
            }
            else
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                foreach (char ch in key)
                {
                    sb.Append(MapCyrillicCharToEnglish(ch));
                }
                return sb.ToString();
            }
            return key;
        }

        private static char MapCyrillicCharToEnglish(char ch)
        {
            switch (ch)
            {
                case 'Й': return 'Q'; case 'й': return 'q';
                case 'Ц': return 'W'; case 'ц': return 'w';
                case 'У': return 'E'; case 'у': return 'e';
                case 'К': return 'R'; case 'к': return 'r';
                case 'Е': return 'T'; case 'е': return 't';
                case 'Н': return 'Y'; case 'н': return 'y';
                case 'Г': return 'U'; case 'г': return 'u';
                case 'Ш': return 'I'; case 'ш': return 'i';
                case 'Щ': return 'O'; case 'щ': return 'o';
                case 'З': return 'P'; case 'з': return 'p';
                case 'Х': return '['; case 'х': return '[';
                case 'Ъ': return ']'; case 'ъ': return ']';
                case 'Ф': return 'A'; case 'ф': return 'a';
                case 'Ы': return 'S'; case 'ы': return 's';
                case 'В': return 'D'; case 'в': return 'd';
                case 'А': return 'F'; case 'а': return 'f';
                case 'П': return 'G'; case 'п': return 'g';
                case 'Р': return 'H'; case 'р': return 'h';
                case 'О': return 'J'; case 'о': return 'j';
                case 'Л': return 'K'; case 'л': return 'k';
                case 'Д': return 'L'; case 'д': return 'l';
                case 'Ж': return ';'; case 'ж': return ';';
                case 'Э': return '\''; case 'э': return '\'';
                case 'Я': return 'Z'; case 'я': return 'z';
                case 'Ч': return 'X'; case 'ч': return 'x';
                case 'С': return 'C'; case 'с': return 'c';
                case 'М': return 'V'; case 'м': return 'v';
                case 'И': return 'B'; case 'и': return 'b';
                case 'Т': return 'N'; case 'т': return 'n';
                case 'Ь': return 'M'; case 'ь': return 'm';
                case 'Б': return ','; case 'б': return ',';
                case 'Ю': return '.'; case 'ю': return '.';
                default: return ch;
            }
        }

        private static string ReplaceRawKeyInSegment(string segment, string rawKey, string mappedKey)
        {
            int openBrac = segment.IndexOf('[');
            int closeBrac = segment.LastIndexOf(']');
            if (openBrac >= 0 && closeBrac > openBrac)
            {
                string before = segment.Substring(0, openBrac + 1);
                string after = segment.Substring(closeBrac);
                string middle = segment.Substring(openBrac + 1, closeBrac - openBrac - 1);
                string middleStripped = TagRegex.Replace(middle, "").Trim();
                if (middleStripped == rawKey)
                {
                    string middleMapped = middle.Replace(rawKey, mappedKey);
                    return before + middleMapped + after;
                }
            }
            return segment.Replace(rawKey, mappedKey);
        }

        private static string GetDominantColor(string textWithTags)
        {
            var colors = ExtractColors(textWithTags);
            if (colors.Count == 0) return null;
            var counts = new Dictionary<string, int>();
            foreach (var c in colors)
            {
                if (c == null) continue;
                if (counts.ContainsKey(c)) counts[c]++;
                else counts[c] = 1;
            }
            string best = null; int max = 0;
            foreach (var kvp in counts) { if (kvp.Value > max) { max = kvp.Value; best = kvp.Key; } }
            return best;
        }

        private static bool IsHotkey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (key.Length <= 5) return true;
            string lk = key.ToLower();
            if (lk == "space" || lk == "enter" || lk == "tab" || lk == "esc" || lk == "escape" || 
                lk == "backspace" || lk == "delete" || lk == "insert" || lk == "home" || lk == "end" || 
                lk == "pageup" || lk == "pagedown" || lk == "pgup" || lk == "pgdn" || 
                lk == "up" || lk == "down" || lk == "left" || lk == "right")
            {
                return true;
            }
            if (lk.StartsWith("ctrl") || lk.StartsWith("shift") || lk.StartsWith("alt") || 
                lk.StartsWith("num") || lk.StartsWith("mouse"))
            {
                return true;
            }
            return false;
        }

        private static int CountSubstring(string text, string sub)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(sub)) return 0;
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(sub, index, StringComparison.OrdinalIgnoreCase)) != -1)
            {
                count++;
                index += sub.Length;
            }
            return count;
        }

        public static void LogInfo(string msg)
        {
            try
            {
                UnityEngine.Debug.Log(msg);
            }
            catch
            {
                // Console.WriteLine("[INFO] " + msg);
            }
        }

        public static void LogError(string msg)
        {
            try
            {
                UnityEngine.Debug.LogError(msg);
            }
            catch
            {
                // Console.WriteLine("[ERROR] " + msg);
            }
        }

        static TranslationEngine()
        {
            try
            {
                Initialize();
            }
            catch (Exception ex)
            {
                LogError("[RussianLocalization] Static constructor initialization failed: " + ex.ToString());
            }
        }

        // ============================================================
        // ЗАЩИТА ОТ ДВОЙНОЙ УСТАНОВКИ (2026-08-03)
        // ============================================================
        // Игроки ставят мод и подпиской в Workshop, и ручной копией в
        // CavesOfQud\Mods\RussianLocalization. Обе копии — РАЗНЫЕ СБОРКИ, поэтому
        // статик-флаг Initialized их не связывает: у каждой свой экземпляр. В логе
        // это выглядит так (Player.log игрока, 2026-08-02):
        //   INFO - Enabled mods: Русификатор 1.0.5 by memasevich, Русификатор 1.0.5 by memasevich
        //   ...десятки CS0436 (MorphCase/FormsEntry конфликтуют с 3728849656.dll)
        //   ...вся инициализация дважды, потом у второй копии Popup=0, ScreenBuffer=0
        // Двойной патч TextElement.text и два RuntimeTranslator прогоняют каждую строку
        // через перевод дважды — игра не доходит до главного меню.
        //
        // ОПОЗНАНИЕ ПО ТИПУ, А НЕ ПО ID. В manifest.json нет поля ID, а папки у копий
        // разные ("RussianLocalization" против "3728849656") — по ID их не сматчить.
        // Признак «это копия того же мода» — сборка определяет наш собственный тип.
        //
        // ИНВАРИАНТ: обе копии не отключаются НИКОГДА. При любой ошибке разбора копия
        // считает победителем себя. Отказ в сторону «работают обе» — это сегодняшнее
        // поведение: плохое, но известное. Отказ в сторону «не работает ни одна» дал бы
        // полностью английскую игру без всяких объяснений, что заметно хуже.
        private const string TranslationEngineTypeName = "RussianLocalization.TranslationEngine";

        // Слот в AppDomain — общая память процесса, видна обеим сборкам без общего типа.
        // Страховка второго уровня: если разбор через ModManager не сработает, деградируем
        // до «первый выиграл». ИМЯ СЛОТА МЕНЯТЬ НЕЛЬЗЯ — иначе разные версии мода перестанут
        // видеть друг друга.
        private const string ActiveInstanceSlot = "RussianLocalization.ActiveInstance";

        /// <summary>Сообщение о дубликате для показа игроку; заполняет ТОЛЬКО копия-победитель.</summary>
        private static string _duplicateInstallMessage;
        private static bool _duplicateInstallMessageShown;

        /// <summary>
        /// Возвращает true, если эта копия должна отключиться: рядом работает другая,
        /// более приоритетная. Заодно заполняет _duplicateInstallMessage у победителя.
        /// </summary>
        private static bool ShouldYieldToAnotherInstall()
        {
            System.Reflection.Assembly selfAssembly = typeof(TranslationEngine).Assembly;

            try
            {
                // 1. Собираем все активные моды, чья сборка определяет наш тип.
                var copies = new List<ModInfo>();
                foreach (var mod in ModManager.ActiveMods)
                {
                    if (mod == null || mod.Assembly == null) continue;
                    System.Type t = null;
                    try { t = mod.Assembly.GetType(TranslationEngineTypeName, false); } catch { }
                    if (t != null) copies.Add(mod);
                }

                if (copies.Count <= 1)
                {
                    // Единственная копия — обычный случай. Маркер всё равно ставим:
                    // он нужен, если ВТОРАЯ копия не сможет разобрать ModManager.
                    try { System.AppDomain.CurrentDomain.SetData(ActiveInstanceSlot, selfAssembly.FullName); } catch { }
                    return false;
                }

                // 2. Победитель: старшая версия -> Steam-источник -> первый путь по ordinal.
                // Правило детерминированное, поэтому каждая копия приходит к одному и тому
                // же выводу независимо, и порядок загрузки ни на что не влияет.
                ModInfo winner = null;
                foreach (var mod in copies)
                {
                    if (winner == null) { winner = mod; continue; }
                    if (ComparePriority(mod, winner) > 0) winner = mod;
                }

                bool iAmWinner = winner != null && ReferenceEquals(winner.Assembly, selfAssembly);

                if (iAmWinner)
                {
                    _duplicateInstallMessage = BuildDuplicateInstallMessage(winner, copies);
                    LogError("[RussianLocalization] Мод установлен дважды. Активна копия: " +
                             DescribeCopy(winner) + ". Остальные отключены.");
                    try { System.AppDomain.CurrentDomain.SetData(ActiveInstanceSlot, selfAssembly.FullName); } catch { }
                    return false;
                }

                LogError("[RussianLocalization] Обнаружена более приоритетная копия мода (" +
                         DescribeCopy(winner) + ") — эта копия (" +
                         DescribeCopy(ModManager.GetMod(selfAssembly)) +
                         ") отключается, чтобы не патчить игру дважды.");
                return true;
            }
            catch (Exception ex)
            {
                // Разбор через ModManager не удался — падаем на маркер в AppDomain.
                LogError("[RussianLocalization] Проверка двойной установки через ModManager не удалась (" +
                         ex.Message + "), переключаюсь на маркер AppDomain.");
                try
                {
                    object marker = System.AppDomain.CurrentDomain.GetData(ActiveInstanceSlot);
                    string owner = marker as string;
                    if (!string.IsNullOrEmpty(owner))
                    {
                        // Свой собственный маркер — это переинициализация нас же
                        // (Qud умеет перезапускать мод без перезапуска игры), не отключаемся.
                        if (string.Equals(owner, selfAssembly.FullName, StringComparison.Ordinal)) return false;
                        LogError("[RussianLocalization] Мод уже активен в сборке " + owner +
                                 " — эта копия отключается.");
                        return true;
                    }
                    System.AppDomain.CurrentDomain.SetData(ActiveInstanceSlot, selfAssembly.FullName);
                }
                catch { }
                return false;
            }
        }

        /// <summary>
        /// Страховка третьего уровня, на самом патчинге. 0Harmony.dll грузится из Managed
        /// один раз, поэтому его реестр патчей — общее состояние обеих копий мода: если
        /// наш Harmony ID уже кем-то занят, патчит не первый экземпляр и вставать поверх
        /// нельзя. Спасает, даже когда оба предыдущих уровня почему-то не сработали.
        /// </summary>
        private static bool AnotherInstallAlreadyPatched(string harmonyId, string what)
        {
            try
            {
                if (!Harmony.HasAnyPatches(harmonyId)) return false;
                UnityEngine.Debug.LogWarning("[RussianLocalization] " + what + ": патчи под ID " + harmonyId +
                    " уже установлены другой копией мода — пропускаем, чтобы не патчить дважды.");
                return true;
            }
            catch { return false; }
        }

        /// <summary>Больше нуля — приоритетнее. Версия, затем Steam-источник, затем путь.</summary>
        private static int ComparePriority(ModInfo a, ModInfo b)
        {
            // ModManifest.Version — это XRL.Version, и она СТРУКТУРА (проверено рефлексией
            // по Assembly-CSharp: IsValueType = true). Отсюда два следствия:
            //
            // 1. Сравнивать её с null бессмысленно — значение есть всегда, по умолчанию 0.0.0.0.
            //    «Версия неизвестна» выражается только через Manifest == null (сам манифест —
            //    ссылочный тип).
            // 2. Присваивать ей null НЕЛЬЗЯ ВООБЩЕ, даже просто "XRL.Version v = null;". Это
            //    компилируется через implicit-оператор из System.Version, то есть в
            //    op_Implicit(null) -> XRL.Version..ctor((System.Version)null), а тот кидает
            //    NullReferenceException. Первая редакция этого метода падала так на КАЖДОМ
            //    вызове; поймано офлайн-тестом ComparePriority на подставных ModInfo.
            bool hasA = a != null && a.Manifest != null;
            bool hasB = b != null && b.Manifest != null;
            if (hasA != hasB) return hasA ? 1 : -1;
            if (hasA)
            {
                int byVersion = a.Manifest.Version.CompareTo(b.Manifest.Version);
                if (byVersion != 0) return byVersion;
            }

            // Одинаковая версия в обеих папках — самый вероятный случай (подписался,
            // не удалив ручную установку). Workshop-копия каноничнее забытой ручной.
            bool sa = a != null && a.Source == ModSource.Steam;
            bool sb = b != null && b.Source == ModSource.Steam;
            if (sa != sb) return sa ? 1 : -1;

            string pa = a != null ? (a.Path ?? "") : "";
            string pb = b != null ? (b.Path ?? "") : "";
            return -string.Compare(pa, pb, StringComparison.Ordinal);
        }

        private static string DescribeCopy(ModInfo mod)
        {
            if (mod == null) return "неизвестная копия";
            // Version — структура, сравнивать её с null нельзя (см. ComparePriority):
            // признак «версия неизвестна» — это отсутствие самого манифеста.
            string version = mod.Manifest != null
                ? mod.Manifest.Version.ToString() : "версия неизвестна";
            return (mod.Path ?? "путь неизвестен") + " (" + version + ", " + mod.Source + ")";
        }

        private static string BuildDuplicateInstallMessage(ModInfo winner, List<ModInfo> copies)
        {
            var sb = new StringBuilder();
            sb.Append("{{R|Русификатор установлен дважды.}}\n\n");
            sb.Append("{{G|Активна:}}   ").Append(DescribeCopy(winner)).Append('\n');
            foreach (var mod in copies)
            {
                if (mod == null || ReferenceEquals(mod, winner)) continue;
                sb.Append("{{K|Отключена:}} ").Append(DescribeCopy(mod)).Append('\n');
            }
            sb.Append("\nУдалите лишнюю папку и перезапустите игру.");
            return sb.ToString();
        }

        /// <summary>
        /// Показывает окно о двойной установке — один раз за сессию. Зовётся из
        /// [CallAfterGameLoaded] (см. DuplicateInstallNotice): из статического
        /// конструктора Popup.Show звать нельзя, UI на той стадии ещё не живой —
        /// именно там игра и висла при двойной установке.
        /// </summary>
        public static void ShowDuplicateInstallNoticeOnce()
        {
            if (_duplicateInstallMessageShown) return;
            if (string.IsNullOrEmpty(_duplicateInstallMessage)) return;
            _duplicateInstallMessageShown = true;
            try { XRL.UI.Popup.Show(_duplicateInstallMessage); }
            catch (Exception ex)
            {
                LogError("[RussianLocalization] Не удалось показать окно о двойной установке: " + ex.Message);
            }
        }

        public static void Initialize()
        {
            lock (FileLock)

            {

                if (Initialized) return;



                try

                {

                    // Проверяем ДО загрузки словарей: у проигравшей копии это экономит
                    // ~10 секунд и вторые 165807 фраз + 49044 формы в памяти.
                    if (ShouldYieldToAnotherInstall())
                    {
                        IsEnabled = false;
                        return;
                    }

                    string modPath = GetModPath();

                    if (string.IsNullOrEmpty(modPath)) return;



                    // 1. Загрузка основного словаря фраз

                    string dictPath = Path.Combine(modPath, "dictionary.json");

                    if (File.Exists(dictPath))

                    {

                        string jsonText = File.ReadAllText(dictPath, Encoding.UTF8);

                        var dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonText);

                        if (dict != null)

                        {

                            staticDictionary.Clear();

                            foreach (var kvp in dict)

                            {

                                if (kvp.Key == null) continue;

                                string normKey = kvp.Key.Replace('\u00A0', ' ')

                                                        .Replace('\u2007', ' ')

                                                        .Replace('\u200B', ' ')

                                                        .Replace('\u202F', ' ')

                                                        .Trim();

                                if (!string.IsNullOrEmpty(normKey))

                                {

                                    // Фильтр безопасности: в основном словаре отбрасываем 1-2 буквенные записи,
                                    // которые НЕ являются игровыми сокращениями (HP, MP, SP, и т.д.).
                                    // Опасные 1-2 буквенные слова: 'St', 'To', 'Me', 'in', 'on' — ломают длинные строки
                                    // при разбиении движком на подстроки (например 'together' -> 'toвзятьher' из-за
                                    // записи 'et'->'взять' в word_dictionary, что было исправлено в коммите 080bf99).
                                    string trimmedKey = normKey.Trim();
                                    if (trimmedKey.Length > 0 && trimmedKey.Length <= 2 &&
                                        System.Text.RegularExpressions.Regex.IsMatch(trimmedKey, @"^[A-Za-z]+$") &&
                                        !IsGameAbbreviation(trimmedKey))
                                    {
                                        // Слишком короткое английское слово (не сокращение) — опасно.
                                        continue;
                                    }

                                    // 2026-07-23 (Guard C — «схлопнутые» записи словаря): если длинному ключу
                                    // (>= 150 символов без тегов — описание/проза) соответствует значение,
                                    // короче него в 4+ раза, запись отравлена при сборке словаря (кейс
                                    // 23.07: описание hyena tribeskin, 397 симв. -> "{{W|[t]}} {{y|цель}}}" —
                                    // целиковое совпадение детерминированно заменяло ВЕСЬ длинный текст
                                    // мусором из чужой строки меню). Такие записи пропускаем и пишем в
                                    // dict_suspicious.txt (Documents\CavesOfQud_RU_Logs) на вычистку словаря.
                                    if (normKey.Length >= 150 && kvp.Value != null)
                                    {
                                        string guardStrippedKey = TagRegex.Replace(normKey, "");
                                        string guardStrippedVal = TagRegex.Replace(kvp.Value.Trim(), "");
                                        if (guardStrippedKey.Length >= 150 && guardStrippedVal.Length * 4 < guardStrippedKey.Length)
                                        {
                                            LogSuspiciousDictionaryEntry(normKey, kvp.Value, "collapsed");
                                            continue;
                                        }
                                    }

                                    staticDictionary[normKey] = kvp.Value;

                                    string sn = SuperNormalize(normKey);

                                    if (!string.IsNullOrEmpty(sn))

                                    {

                                         if (!normalizedKeyDictionary.TryGetValue(sn, out string existingKey))
                                         {
                                             normalizedKeyDictionary[sn] = normKey;
                                         }
                                         else
                                         {
                                             int existingPenalty = existingKey.Length - sn.Length;
                                             int newPenalty = normKey.Length - sn.Length;
                                             if (newPenalty < existingPenalty)
                                             {
                                                 normalizedKeyDictionary[sn] = normKey;
                                             }
                                         }

                                    }

                                }

                            }



                            sortedKeys.Clear();

                            sortedKeys.AddRange(staticDictionary.Keys);

                            sortedKeys.Sort((x, y) => y.Length.CompareTo(x.Length));

                        }

                    }



                    // 2. Загрузка пословного словаря

                    string wordDictPath = Path.Combine(modPath, "word_dictionary.json");

                    if (File.Exists(wordDictPath))

                    {

                        string wordJsonText = File.ReadAllText(wordDictPath, Encoding.UTF8);

                        var wordDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(wordJsonText);

                        if (wordDict != null)

                        {

                            wordDictionary.Clear();

                            foreach (var kvp in wordDict)

                            {

                                if (kvp.Key == null) continue;

                                // Фильтр безопасности: блокируем 1-буквенные английские слова (не сокращения).
                                // 2-буквенные слова (in, to, at, of, is, by, as, on, be, it) разрешены,
                                // так как TryWordReplacement ходит только по целым словам (split по пробелам),
                                // а не по подстрокам — 'together' не станет 'toвзятьher'.
                                string rawKey = kvp.Key;
                                string trimmedKey = rawKey.Trim();

                                if (trimmedKey.Length == 1 &&
                                    System.Text.RegularExpressions.Regex.IsMatch(trimmedKey, @"^[A-Za-z]+$") &&
                                    !IsGameAbbreviation(trimmedKey))
                                {
                                    // Слишком короткое английское слово (не сокращение) — опасно, пропускаем.
                                    continue;
                                }

                                string normKey = rawKey.Replace('\u00A0', ' ')

                                                        .Replace('\u2007', ' ')

                                                        .Replace('\u200B', ' ')

                                                        .Replace('\u202F', ' ')

                                                        .Trim();

                                if (!string.IsNullOrEmpty(normKey))

                                    wordDictionary[normKey] = kvp.Value;

                            }



                            sortedWordKeys.Clear();

                            sortedWordKeys.AddRange(wordDictionary.Keys);

                            sortedWordKeys.Sort((x, y) => y.Length.CompareTo(x.Length));

                        }

                    }



                    // 3. Загрузка словаря паттернов (регулярных выражений)

                    string patternDictPath = Path.Combine(modPath, "pattern_dictionary.json");

                    if (File.Exists(patternDictPath))

                    {

                        string patternJsonText = File.ReadAllText(patternDictPath, Encoding.UTF8);

                        var patternObj = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(patternJsonText);

                        if (patternObj != null)

                        {

                            patternDictionary.Clear();

                            // Сначала собираем все валидные паттерны
                            var tempList = new List<KeyValuePair<System.Text.RegularExpressions.Regex, string>>();
                            foreach (var property in patternObj.Properties())

                            {

                                string patternKey = property.Name;

                                string patternValue = property.Value.ToString();

                                if (string.IsNullOrEmpty(patternKey)) continue;

                                try

                                {

                                    var regex = new System.Text.RegularExpressions.Regex(patternKey, System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                                    tempList.Add(new KeyValuePair<System.Text.RegularExpressions.Regex, string>(regex, patternValue));

                                }

                                catch (Exception regexEx)

                                {

                                    LogError("[RussianLocalization] Failed to compile pattern regex '" + patternKey + "': " + regexEx.Message);

                                }

                            }

                            // Сортируем по убыванию длины regex-строки: более специфичные паттерны
                            // (например, "You take the (?<item>.+?)$") проверяются раньше общих
                            // (например, "You pass by (?<item>.+?)$"). Это важно, потому что при матче
                            // строки "You take the bronze sword." оба паттерна матчатся — побеждает
                            // более длинный, что даёт правильный перевод.
                                                        tempList.Sort((a, b) => {
                                string strA = a.Key.ToString();
                                string strB = b.Key.ToString();
                                string cleanA = System.Text.RegularExpressions.Regex.Replace(strA, @"\(\?<[a-zA-Z0-9_]+>", "(");
                                string cleanB = System.Text.RegularExpressions.Regex.Replace(strB, @"\(\?<[a-zA-Z0-9_]+>", "(");
                                int lenA = cleanA.Length;
                                int lenB = cleanB.Length;
                                int comp = lenB.CompareTo(lenA);
                                if (comp != 0) return comp;
                                int groupsA = a.Key.GetGroupNames().Length;
                                int groupsB = b.Key.GetGroupNames().Length;
                                return groupsA.CompareTo(groupsB);
                            });

                            foreach (var kv in tempList)
                            {
                                patternDictionary.Add(kv);
                            }

                        }
                    }

                    // 4. Загрузка склонений фракций
                    string factionCasesPath = Path.Combine(modPath, "faction_cases.json");
                    if (File.Exists(factionCasesPath))
                    {
                        try
                        {
                            string factionJsonText = File.ReadAllText(factionCasesPath, Encoding.UTF8);
                            var loadedCases = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(factionJsonText);
                            if (loadedCases != null)
                            {
                                factionCases.Clear();
                                foreach (var kvp in loadedCases)
                                {
                                    if (!string.IsNullOrEmpty(kvp.Key))
                                    {
                                        factionCases[kvp.Key] = kvp.Value;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogError("[RussianLocalization] Failed to load faction_cases.json: " + ex.Message);
                        }
                    }
                    try
                    {
                        MorphologyService.Initialize(modPath);
                    }
                    catch (Exception ex)
                    {
                        LogError("[RussianLocalization] Failed to initialize MorphologyService: " + ex.Message);
                    }

                    Initialized = true;
                    MainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
                    translationCache.Clear();
                    CachedModPath = modPath;
                    BuildTemplateDictionary();
                    
                    // Генерируем имя файла лога с датой
                    GameplayLogFileName = $"all_gameplay_texts_{DateTime.Now:dd_MM_yyyy}.txt";

                    LogInfo("[RussianLocalization] Initialized successfully. Loaded " + staticDictionary.Count + " phrases, " + wordDictionary.Count + " words, " + patternDictionary.Count + " patterns, and " + factionCases.Count + " faction case entries.");

                    string gameVersion = Application.version;
                    string manifestPath = Path.Combine(modPath, "manifest.json");
                    if (File.Exists(manifestPath))
                    {
                        try
                        {
                            string manifestJson = File.ReadAllText(manifestPath, Encoding.UTF8);
                            var manifest = JsonConvert.DeserializeObject<Dictionary<string, object>>(manifestJson);
                            if (manifest != null && manifest.TryGetValue("GameVersion", out object expectedVersionObj))
                            {
                                string expectedVersion = expectedVersionObj.ToString();
                                if (gameVersion != expectedVersion)
                                {
                                    LogError("[RussianLocalization] WARNING: Game version mismatch! Mod tested on " + expectedVersion + ", current game is " + gameVersion + ". Translation may be incomplete or broken.");
                                }
                                else
                                {
                                    LogInfo("[RussianLocalization] Game version " + gameVersion + " matches expected " + expectedVersion + ".");
                                }
                            }
                        }
            catch {}
                    }

                    // ВРЕМЕННО ОТКЛЮЧЕНО ДЛЯ ДИАГНОСТИКИ КРАША (2026-07-02): игра стабильно
                    // крашится (access violation в ntdll.dll, WER-код 0xc0000005) в момент
                    // старта "Loading object blueprints" — после того, как эти 5 хуков были
                    // добавлены 2026-07-01. Отключаем всё разом одним тестовым прогоном,
                    // чтобы подтвердить, что причина среди них, затем включим по одному.
                    // См. память ru-qud-crash-fix-20260702 (v2) за подробностями.
                    // 2026-07-02 (v3): бисекция показала, что 5 хуков НЕ виноваты (краш был при
                    // них отключённых), причина — RuntimeTranslator без лимита глубины рекурсии.
                    // Хуки снова включены (флаг = false).
                    // 2026-07-05: НОВЫЙ краш (0xc0000005 в ntdll.dll, тот же паттерн) при открытии
                    // торговли с большим ассортиментом. RuntimeTranslator уже отключён насовсем,
                    // отключение TranslateVisualTree в UIDocument_OnEnable_Postfix не помогло —
                    // значит причина этого краша НЕ там же, где была причина краша на загрузке.
                    // Бисекция для СТАРТОВОГО краша (выше) не покрывает этот кейс: отключение
                    // всех 5 хуков подтвердило (2026-07-05), что краш пропадает без них — то
                    // есть виноват один из них. Найдено: INotifyValueChanged_SetValueWithoutNotify_
                    // Prefix (часть PatchUIElements) переводит текст описания предмета через
                    // TranslateMarkup — рекурсия по вложенным {{color|...}} блокам БЕЗ лимита
                    // глубины. Добавлен MaxMarkupDepth=48 (см. TranslateMarkup), хуки включены
                    // обратно (флаг = false).
                    // 2026-07-05 (v7, ИСПРАВЛЕНО в v9): предыдущая версия этого комментария
                    // ошибочно заявляла, что все 5 хуков сняты с подозрения — Description_Patches
                    // действительно виноват в краше НА ОПИСАНИИ, но включение всех 5 хуков обратно
                    // вернуло ДРУГОЙ, ранее уже описанный краш (см. запись выше про
                    // INotifyValueChanged_SetValueWithoutNotify_Prefix/TranslateMarkup) — теперь
                    // на самом первом кадре после загрузки (хоткей-плашки способностей). Это ДВА
                    // независимых бага в разных хуках. Оставляем общий флаг выключенным (=false,
                    // группа включена), но добавляем отдельный переключатель ТОЛЬКО для
                    // PatchUIElements — самого подозреваемого по старому диагнозу — чтобы отключить
                    // именно его, сохранив QudTranslator/UITextSkin/Popup/GameText.
                    // 2026-07-05 (v11): отключение PatchUIElements НЕ остановило краш в торговле —
                    // гипотеза опровергнута. Реальная причина найдена через WinDbg-анализ дампа:
                    // gameoverlayrenderer64.dll (Steam Overlay), см. комментарий у
                    // UITextReentrancyGuard.DIAG_DISABLE_TMP_HOOKS выше. Флаг возвращён в false.
                    // 2026-07-06 (v22): ПЕРВОПРИЧИНА КРАША УСТРАНЕНА на уровне движка
                    // (числовые плейсхолдеры {0} в TryTranslatePattern → бесконечная рекурсия).
                    // Все хуки маршрутизируются через тот же Translate(), теперь он безопасен —
                    // возвращаем ВСЕ хуки для полного покрытия перевода.
                    const bool DIAG_DISABLE_20260701_HOOKS = false;
                    const bool DIAG_DISABLE_UIELEMENTS_HOOK = false;
                    // 2026-07-05 (v12): пользователь настаивает, что причина в коде мода (раньше тем
                    // же модом краша не было), и он прав закрывать вопрос на Steam-оверлее рано —
                    // порча памяти нашим кодом вполне может проявляться как краш в постороннем коде.
                    // Известный чистый результат (v8): Description + все 5 хуков выключены = НЕТ краша.
                    // Включение всех 4 оставшихся (QudTranslator/UITextSkin/Popup/GameText) разом
                    // вернуло краш в торговле (v10/v11). Сужаем по одному.
                    // v12 шаг 1: только PatchPopup — краша НЕТ. Popup очищен.
                    // v12 шаг 2: только PatchQudTranslator — краша НЕТ (подтверждено). Тоже чист.
                    // v12 шаг 3: только PatchUITextSkin — КРАШ ВОСПРОИЗВЁЛСЯ. Виновник найден.
                    // v13: добавлен EnsureSufficientExecutionStack() в UITextSkin_SetText_Prefix /
                    // UITextSkin_SetTheText_Prefix (см. их код ниже). Проверяем фикс с тем же
                    // изолированным набором (только UITextSkin), прежде чем включать остальные.
                    const bool DIAG_DISABLE_QUDTRANSLATOR_HOOK = false;
                    const bool DIAG_DISABLE_UITEXTSKIN_HOOK = false;
                    const bool DIAG_DISABLE_POPUP_HOOK = false;
                    const bool DIAG_DISABLE_GAMETEXT_HOOK = false;
                    if (!DIAG_DISABLE_20260701_HOOKS)
                    {
                    // Динамический патч для Modern UI (UI Toolkit / UIElements)

                    if (!DIAG_DISABLE_UIELEMENTS_HOOK) PatchUIElements();

                    // Динамический патч для встроенного транслятора QudTranslator (≥ 2.0.214).
                    // Запускаем ПОСЛЕ PatchUIElements, чтобы QudTranslator.dll уже был загружен.
                    if (!DIAG_DISABLE_QUDTRANSLATOR_HOOK) { try { PatchQudTranslator(); } catch (Exception exQ) { LogError("[RussianLocalization] PatchQudTranslator dispatch error: " + exQ.ToString()); } }

                    // Главный хук Modern UI: текст нового интерфейса рисуется через XRL.UI.UITextSkin.SetText().
                    if (!DIAG_DISABLE_UITEXTSKIN_HOOK) { try { PatchUITextSkin(); } catch (Exception exT) { LogError("[RussianLocalization] PatchUITextSkin dispatch error: " + exT.ToString()); } }

                    // Патч XRL.UI.Popup — popup-сообщения, меню выбора, запросы строки/числа.
                    // ~277 вызовов в коде (ShowYesNo=108, PickOption=84, AskString=23, AskNumber=13...).
                    if (!DIAG_DISABLE_POPUP_HOOK) { try { PatchPopup(); } catch (Exception exP) { LogError("[RussianLocalization] PatchPopup dispatch error: " + exP.ToString()); } }

                    // Патч XRL.UI.BookUI.AutoformatPages — перевод текста книги ДО нарезки
                    // на строки, иначе перенос считается по английской ширине (см. PatchBookUI).
                    try { PatchBookUI(); } catch (Exception exB) { LogError("[RussianLocalization] PatchBookUI dispatch error: " + exB.ToString()); }

                    // Динамический патч ScreenBuffer для поддержки пропущенных методов рендеринга ретро-консоли
                    try { PatchScreenBufferDynamic(); } catch (Exception exSB) { LogError("[RussianLocalization] PatchScreenBufferDynamic dispatch error: " + exSB.ToString()); }

                    // Патч XRL.GameText.VariableReplace — подстановка плейсхолдеров (=subject.X=, =verb:X=).
                    // 11 перегрузок, 78 вызовов в коде.
                    if (!DIAG_DISABLE_GAMETEXT_HOOK) { try { PatchGameText(); } catch (Exception exG) { LogError("[RussianLocalization] PatchGameText dispatch error: " + exG.ToString()); } }
                    }

                    // ДИАГНОСТИКА КРАША (2026-07-02, шаг 2): с отключёнными 5 хуками игра
                    // ВСЁ РАВНО крашится на "Loading object blueprints". PatchAll в моде нет,
                    // все аннотационные [HarmonyPatch]-классы инертны — значит единственный
                    // оставшийся активный per-frame код это RuntimeTranslator (LateUpdate →
                    // WalkVisualTree, рекурсия по дереву UI без ограничения глубины). Отключаем
                    // его этим прогоном, чтобы подтвердить/исключить как причину.
                    // 2026-07-02 (v3): подтверждено — он и был причиной. Включён обратно с лимитом
                    // MaxWalkDepth=512.
                    // 2026-07-02 (v5, ОКОНЧАТЕЛЬНО): даже С лимитом глубины игра ВСЁ РАВНО крашится
                    // (Player.log 13:11: сборка с фиксом в 13:11:37, краш на 00:00:07 — фикс был
                    // активен и не спас). Значит причина НЕ в глубине рекурсии: RuntimeTranslator.
                    // LateUpdate дёргает Resources.FindObjectsOfTypeAll<TMP_Text>() и
                    // FindObjectsOfType(UIDocument) из кадра ПОСРЕДИ фазы "Loading object blueprints",
                    // когда объектная система Unity ещё наполовину построена → обращение к
                    // полупостроенному нативному объекту → access violation 0xc0000005 в ntdll.dll
                    // (нативный, без managed-исключения — потому в логе пусто). Диаг-строка
                    // "RuntimeTranslator scan: docs=" в логе так и не появилась = смерть в первом же
                    // скане. RuntimeTranslator — лишь ДУБЛИРУЮЩИЙ fallback для Modern UI, а сам
                    // Modern UI уже покрыт хуком PatchUIElements (UIElements.TextElement.text). Поэтому
                    // отключаем RT НАСОВСЕМ: 5 хуков остаются и дают почти весь перевод, а краша нет.
                    const bool DIAG_DISABLE_RUNTIME_TRANSLATOR = false;
                    if (!DIAG_DISABLE_RUNTIME_TRANSLATOR)
                    {
                        try { EnsureRuntimeTranslator(); } catch (Exception exR) { LogError("[RussianLocalization] EnsureRuntimeTranslator error: " + exR.ToString()); }
                    }

                }

                catch (Exception ex)

                {

                    LogError("[RussianLocalization] Init Error: " + ex.ToString());

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

                UnityEngine.Application.persistentDataPath,

                Path.Combine("Mods", "RussianLocalization")

            );

            if (Directory.Exists(defaultPath))

            {

                return defaultPath;

            }



            return null;

        }



        public static void ExtractRussianPrefix(string text, out string prefix, out string englishPart)

        {

            prefix = "";

            englishPart = text;

            if (string.IsNullOrEmpty(text)) return;



            int firstEnglishIdx = -1;

            for (int i = 0; i < text.Length; i++)

            {

                char c = text[i];

                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))

                {

                    firstEnglishIdx = i;

                    break;

                }

            }

            if (firstEnglishIdx <= 0) return;



            string candidatePrefix = text.Substring(0, firstEnglishIdx);

            if (!ContainsCyrillic(candidatePrefix)) return;



            prefix = candidatePrefix;

            englishPart = text.Substring(firstEnglishIdx);

        }



        // Хоткей-обёртки нового UI: игра вставляет {{hotkey|X}} ВНУТРЬ слова (X — подсвеченная
        // буква-клавиша), напр. {{hotkey|c}}ollect liquid, drin{{hotkey|k}}. Если оставить обёртку,
        // получается «cсобрать»/«выпитьk». Просто убираем обёртку, возвращая букву X на место —
        // тогда обычный конвейер переводит слово/фразу целиком. Клавиша остаётся в соседней
        // [X]-скобке и привязана командой, так что хоткеи продолжают работать.
        private static readonly System.Text.RegularExpressions.Regex HotkeyWrapperRegex =
            new System.Text.RegularExpressions.Regex(@"\{\{hotkey\|(?<k>[^|}]+)\}\}", System.Text.RegularExpressions.RegexOptions.Compiled);

        // 2026-07-06 (v23): распознаёт технические ID-строки без пробелов вида "Xxx:571",
        // "InventoryActionMenu:(noid)" — идентификатор, затем ':' , затем цифры или "(...)".
        // Такие строки не переводим и не логируем как непереведённые.
        private static bool IsInternalIdString(string text)
        {
            int colon = text.IndexOf(':');
            // Требуем длинный идентификатор-класс (≥8 символов) до двоеточия, чтобы НЕ задеть
            // короткие статус-префиксы с паттернами перевода: "T:5ø", "HP:10/20", "XP:5/10".
            if (colon < 8 || colon >= text.Length - 1) return false;
            for (int i = 0; i < colon; i++)
            {
                char c = text[i];
                if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')) return false;
            }
            char after = text[colon + 1];
            return (after >= '0' && after <= '9') || after == '(';
        }

        // 2026-07-06 (v24): формат меню Modern UI "{{W|N}}ew game" — первая буква-хоткей выделена
        // цветом в {{X|N}}, а остаток слова ("ew game") идёт СНАРУЖИ разметки. Раньше markup-парсер
        // дробил их и переводил порознь → "Новаяновая игра", "Qкостюм", "Mкоэффициенты". Собираем
        // слово целиком, переводим через словарь ("New game"→"Новая игра"), затем выделяем первую
        // букву перевода той же цветовой разметкой → "{{W|Н}}овая игра".
        private static readonly System.Text.RegularExpressions.Regex MenuHotkeyWordRegex =
            new System.Text.RegularExpressions.Regex(
                @"^\{\{(?<c>[A-Za-z0-9])\|(?<L>[A-Za-z])\}\}(?<rest>[A-Za-z][A-Za-z ]*)$",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private static string TryTranslateMenuHotkeyWord(string text, out bool success)
        {
            success = false;
            if (string.IsNullOrEmpty(text) || text.IndexOf("{{", System.StringComparison.Ordinal) != 0) return text;
            var m = MenuHotkeyWordRegex.Match(text);
            if (!m.Success) return text;
            string plain = m.Groups["L"].Value + m.Groups["rest"].Value; // "New game"
            string translated = TranslateInternal(plain);
            if (string.IsNullOrEmpty(translated) || translated == plain || !ContainsCyrillic(translated)) return text;
            success = true;
            string color = m.Groups["c"].Value;
            return "{{" + color + "|" + translated.Substring(0, 1) + "}}" + translated.Substring(1);
        }

        public static string Translate(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            // Если перевод отключён хоткеем — возвращаем оригинал без кэширования,
            // чтобы при включении сразу начали переводиться новые строки.
            if (!IsEnabled) return text;
            string cached;
            if (translationCache.TryGetValue(text, out cached)) return cached;

            // 2026-07-22 (Guard B): если это наш собственный прошлый результат, поданный обратно
            // (обратная подача уже переведённой строки), — не трогаем, иначе пословный проход
            // испортит бренды/ключи внутри неё. См. комментарий у producedOutputs.
            if (producedOutputs.ContainsKey(text)) return text;

            string originalText = text;
            // Хоткей-обёртки нового UI убираем (буква возвращается в слово) ДО любой обработки.
            if (text.IndexOf("{{hotkey|", System.StringComparison.Ordinal) >= 0)
            {
                text = HotkeyWrapperRegex.Replace(text, "${k}");
            }
            // 2026-07-22: если после снятия обёртки ВСЯ строка — имя клавиши (PgDown, Num 7, Tab,
            // arrows...), это физическая клавиша — возвращаем как есть, не переводим. Проверка
            // строго по всей строке, поэтому слова-омонимы внутри предложений не затрагиваются.
            if (IsHotkeyLiteralKey(text.Trim()))
            {
                translationCache[originalText] = text;
                return text;
            }
            // Commented out to allow translating resource loading logs (e.g., "Loading Bodies.xml" -> "Загрузка Bodies.xml")
            // if (text.StartsWith("Loading ") && (text.EndsWith(".xml") || text.EndsWith(".txt") || text.EndsWith(".json")))
            // {
            //     translationCache[originalText] = text;
            //     return text;
            // }
            // 2026-07-06 (v23): технические строки-идентификаторы, которые НИКОГДА не должны
            // переводиться и не должны попадать в untranslated.txt как «непереведённые»:
            //   - пути ресурсов: "Sounds/UI/ui_notification", "Creatures/sw_beetle" и т.п.
            //   - внутренние ID меню: "InventoryActionMenu:571", "InventoryActionMenu:(noid)"
            // Признак: нет пробелов и есть '/' или ':(' или ведущий сегмент вида "Xxx:number".
            if (text.IndexOf(' ') < 0 && (text.IndexOf('/') >= 0 || IsInternalIdString(text)))
            {
                translationCache[originalText] = text;
                return text;
            }
            // Сначала обрабатываем «радужные» слова (каждая буква в своём цвете): переводим
            // и распределяем исходные цвета по буквам перевода, сохраняя радужный эффект.
            // То, что перевести не удалось, остаётся для обычной компактизации ниже.
            text = ExpandRainbowWords(text);
            text = ExpandAmpRainbowWords(text);
            text = CompactColorFragments(text);

            // 2026-07-06 (v24): меню-хоткей "{{W|N}}ew game" — собираем слово и переводим целиком.
            bool menuHotkeyOk;
            string menuHotkeyTr = TryTranslateMenuHotkeyWord(text, out menuHotkeyOk);
            if (menuHotkeyOk)
            {
                translationCache[originalText] = menuHotkeyTr;
                return menuHotkeyTr;
            }

            // Если строка содержит кириллицу И не содержит английских букв,
            // значит она полностью переведена. Пропускаем.
            // Один проход по строке вместо двух — частый hot path.
            bool hasCyrillic, hasEnglish;
            ScanAlpha(text, out hasCyrillic, out hasEnglish);
            if (hasCyrillic && !hasEnglish)
            {
                translationCache[originalText] = text;
                return text;
            }

            if (InternalGameKeys.Contains(text.Trim()) || IsKeyInBrackets(text))
            {
                translationCache[originalText] = text;
                return text;
            }

            // if (translationCache.Count > 50000) translationCache.Clear();
            // Кэш больше не сбрасывается — это вызывало повторный перевод одних и тех же строк и лаги.

            // Расширенное обнаружение credits-секции.
            // Игра может разбивать credits на отдельные строки-имена,
            // поэтому кроме маркеров используем эвристику по отступам.
            bool isCredits = text.Contains("Brian Bucklew") || 
                             text.Contains("Kitfox Games") || 
                             text.Contains("OPEN SOURCE LICENSES") ||
                             text.Contains("MIT License") ||
                             text.Contains("Created by") ||
                             text.Contains("Published by") ||
                             text.Contains("Special thanks") ||
                             text.Contains("Patrons") ||
                             text.Contains("Additional Programming") ||
                             text.Contains("Additional Design") ||
                             text.Contains("Additional Writing") ||
                             text.Contains("Additional Music") ||
                             text.Contains("Additional UI Design") ||
                             text.Contains("Logo Art") ||
                             text.Contains("Tile Art") ||
                             text.Contains("Background Art") ||
                             text.Contains("Sound Design") ||
                             text.Contains("Legal Counsel") ||
                             text.Contains("Community Management") ||
                             text.Contains("Copyright (c)") ||
                             text.Contains("Permission is hereby granted");
            
            // Эвристика: строки с >= 20 ведущих пробелов, содержащие только латиницу/пробелы/пунктуацию —
            // это типичный формат имён/никнеймов в credits-секции
            if (!isCredits)
            {
                string trimmedForCredits = text.TrimStart();
                int leadingSpaces = text.Length - trimmedForCredits.Length;
                if (leadingSpaces >= 20 && trimmedForCredits.Length > 0 && trimmedForCredits.Length <= 60)
                {
                    bool allAsciiLike = true;
                    foreach (char ch in trimmedForCredits)
                    {
                        // Допускаем латинские буквы, цифры, пробелы и типичную пунктуацию имён
                        if (!((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || 
                              (ch >= '0' && ch <= '9') || ch == ' ' || ch == '.' || 
                              ch == ',' || ch == '-' || ch == '_' || ch == '\'' || 
                              ch == '"' || ch == '(' || ch == ')' || ch == '&' ||
                              ch == '!' || ch == '@' || ch == ':' || ch == ';' ||
                              ch >= 0x80)) // разрешаем extended ASCII (ÅŒ и т.д.)
                        {
                            allAsciiLike = false;
                            break;
                        }
                    }
                    if (allAsciiLike)
                    {
                        isCredits = true;
                    }
                }
            }

            

            if (isCredits)

            {

                System.Threading.Interlocked.Increment(ref disableWordReplacementCounter);

            }



            string result;

            try

            {

                result = TranslateInternal(text);

            }

            finally

            {

                if (isCredits)

                {

                    System.Threading.Interlocked.Decrement(ref disableWordReplacementCounter);

                }

            }

            // FIX C2 (2026-07-20): буква хоткея [x] в начале строки не транслитерируется
            // (см. PreserveLeadingHotkey). result здесь уже собран любым из путей —
            // словарь, паттерн, ModernUI или пословный перевод.
            result = PreserveLeadingHotkey(text, result);
            // 2026-07-23 (Guard D): финальная сверка хоткей-токенов по ВСЕЙ строке
            // ([x] в середине, {{W|[x]}}, {{hotkey|X}}) — ловит испорченные значения
            // словаря, возвращаемые целиковым совпадением в обход гардов выше.
            result = PreserveHotkeyTokens(originalText, result);

            if (result != null && result != text && result.Length > 1)
            {
                bool needsCleanup =
                    result.Contains("}}") || result.Contains("]]") ||
                    result.Contains("[[") || result.Contains("{{") ||
                    result.Contains("=now.dayOfYear=") || result.Contains("=now.year=") ||
                    (result.Contains("&") && ContainsCyrillic(result)) ||
                    result.Contains("<color=") ||
                    (ContainsCyrillic(result) && result.Contains("%")) ||
                    System.Text.RegularExpressions.Regex.IsMatch(result, @"\[[a-zA-Z]\].*\[[a-zA-Z]\]");

                // 2026-07-06 (v19): проверяем гипотезу катастрофического regex backtracking —
                // ниже есть do/while с regex на back-reference (\k<c>) и ленивым negative lookahead
                // ((?:(?!</color>).)*?), схлопывающий одинаковые цветовые блоки. .NET regex-движок
                // рекурсивно использует нативный стек для backtracking — при большом числе цветовых
                // блоков подряд (как в списке предметов торговли) это может переполнить стек ПОСЛЕ
                // нашей проверки EnsureSufficientExecutionStack() в самом начале префикса (которая
                // проверяется один раз при входе, а не во время самого regex).
                if (Translate_DIAG_SKIP_CLEANUP_REGEX) { /* пропускаем весь блок очистки для теста */ }
                else
                if (needsCleanup)
                {
                    if (text == " serving]")
                    {
                        // Console.WriteLine($"[DEBUG Translate] input: '{text}', initial result: '{result}'");
                        // Console.WriteLine(Environment.StackTrace);
                    }
                    // Финальная очистка от "мусорных" скобок, которые могли остаться после процедурной сборки
                    if (result.Contains("}}") && !result.Contains("{{") && !text.Contains("}}"))
                    {
                        result = result.Replace("}}", "");
                    }
                    
                    // Исправление лишних фигурных скобок (результат двойного прохода или процедурной сборки)
                    if (BrokenBraceRegex.IsMatch(result))
                    {
                        int openCount = CountSubstring(result, "{{");
                        int closeCount = CountSubstring(result, "}}");
                        if (openCount != closeCount)
                        {
                            result = BrokenBraceRegex.Replace(result, m => m.Value[0] == '{' ? "{{" : "}}");
                        }
                    }

                    if (result.Contains("]]") && !text.Contains("]]"))
                    {
                        result = result.Replace("]]", "]");
                    }

                    // Устраняем дублирование хоткеев в скобках (например, [L] [L] посмотреть -> [L] посмотреть)
                    result = System.Text.RegularExpressions.Regex.Replace(result, 
                        @"((?:<color=[^>]+>)?\s*\[([a-zA-Z])\]\s*(?:</color>)?)\s*(?:<color=[^>]+>)?\s*(?:</color>)?\s*(?:<color=[^>]+>)?\s*\[\2\]\s*(?:</color>)?", 
                        "$1", 
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                    // Устраняем дублирование открывающих скобок с закрывающими: [[a] -> [a]
                    result = System.Text.RegularExpressions.Regex.Replace(result, @"\[{2,}([^\[\]\n]{1,20})\]", "[$1]");

                    // Устраняем дублирование открывающих скобок без закрывающих (только для клавиш и цветовых амперсандов)
                    result = System.Text.RegularExpressions.Regex.Replace(result, @"\[{2,}([a-zA-Z])", "[$1");
                    result = System.Text.RegularExpressions.Regex.Replace(result, @"\[{2,}\s*(&\s*[a-zA-Z])", "[$1");
                    result = System.Text.RegularExpressions.Regex.Replace(result, @"\[{2,}(Esc|Tab|Enter|Space|Backspace|Num \d)", "[$1");



                    // Устраняем дублирование закрывающих скобок: [a]] -> [a]

                    result = System.Text.RegularExpressions.Regex.Replace(result, @"\[([^\[\]\n]{1,20})\]{2,}", "[$1]");



                    // Удаление дублирующих букв в НАЧАЛЕ слова (например, [r] rпереименовать -> [r] переименовать)
                    result = System.Text.RegularExpressions.Regex.Replace(result, 
                        @"\b([a-zA-Z])\b(\s*)(</color>)?(\s*)(<color=[^>]+>)?(\s*)([а-яА-ЯёЁ])", 
                        m => {
                            if (m.Groups[2].Value.Length > 0 || m.Groups[4].Value.Length > 0 || m.Groups[6].Value.Length > 0)
                            {
                                return m.Value;
                            }
                            return m.Groups[3].Value + m.Groups[4].Value + m.Groups[5].Value + m.Groups[6].Value + m.Groups[7].Value;
                        }, 
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                    // Удаление дублирующих букв в КОНЦЕ слова (например, атаковать k -> атаковать)
                    result = System.Text.RegularExpressions.Regex.Replace(result, 
                        @"([а-яА-ЯёЁ])(\s*)(</color>)?(\s*)(<color=[^>]+>)?(\s*)\b([a-zA-Z])\b(\s*)(</color>)?", 
                        m => {
                            if (m.Groups[2].Value.Length > 0 || m.Groups[4].Value.Length > 0 || m.Groups[6].Value.Length > 0 || m.Groups[8].Value.Length > 0)
                            {
                                return m.Value;
                            }
                            return m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value + m.Groups[4].Value + m.Groups[5].Value + m.Groups[6].Value + m.Groups[8].Value + m.Groups[9].Value;
                        }, 
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);



                    // Восстановление оригинальных латинских тегов цвета Caves of Qud при случайной транслитерации символа цвета
                    // Используем regex с единым проходом вместо цепочки .Replace() (предыдущая версия не работала,
                    // т.к. каждое .Replace() модифицирует строку и последующие не находят исходный паттерн).
                    // Приоритет: транслитерация (ю→y, н→y) > клавиатура (н→h, у→e) > фонетика.
                    result = System.Text.RegularExpressions.Regex.Replace(result, @"&([а-яА-ЯёЁ])",
                        m => {
                            char c = m.Groups[1].Value[0];
                            // Самые частые случаи транслитерации (audit: Y→Ю, y→й, Y→Н)
                            if (c == 'ю') return "&y";
                            if (c == 'Ю') return "&Y";
                            if (c == 'н') return "&y";
                            if (c == 'Н') return "&Y";
                            if (c == 'й') return "&q";
                            if (c == 'Й') return "&Q";
                            // ЙЦУКЕН-клавиатура
                            if (c == 'ц' || c == 'Ц') return c == 'ц' ? "&w" : "&W";
                            if (c == 'у' || c == 'У') return c == 'у' ? "&e" : "&E";
                            if (c == 'е' || c == 'Е') return c == 'е' ? "&t" : "&T";
                            if (c == 'г' || c == 'Г') return c == 'г' ? "&u" : "&U";
                            if (c == 'ш' || c == 'Ш') return c == 'ш' ? "&i" : "&I";
                            if (c == 'щ' || c == 'Щ') return c == 'щ' ? "&o" : "&O";
                            if (c == 'з' || c == 'З') return c == 'з' ? "&p" : "&P";
                            if (c == 'к' || c == 'К') return c == 'к' ? "&r" : "&R";
                            if (c == 'х' || c == 'Х') return "[";
                            if (c == 'ъ' || c == 'Ъ') return "]";
                            if (c == 'ф' || c == 'Ф') return c == 'ф' ? "&a" : "&A";
                            if (c == 'ы' || c == 'Ы') return c == 'ы' ? "&s" : "&S";
                            if (c == 'а' || c == 'А') return c == 'а' ? "&f" : "&F";
                            if (c == 'п' || c == 'П') return c == 'п' ? "&g" : "&G";
                            if (c == 'р' || c == 'Р') return c == 'р' ? "&h" : "&H";
                            if (c == 'о' || c == 'О') return c == 'о' ? "&j" : "&J";
                            if (c == 'л' || c == 'Л') return c == 'л' ? "&k" : "&K";
                            if (c == 'д' || c == 'Д') return c == 'д' ? "&l" : "&L";
                            if (c == 'ж' || c == 'Ж') return ";";
                            if (c == 'э' || c == 'Э') return "'";
                            if (c == 'я' || c == 'Я') return c == 'я' ? "&z" : "&Z";
                            if (c == 'ч' || c == 'Ч') return c == 'ч' ? "&x" : "&X";
                            if (c == 'с' || c == 'С') return c == 'с' ? "&c" : "&C";
                            if (c == 'м' || c == 'М') return c == 'м' ? "&v" : "&V";
                            if (c == 'и' || c == 'И') return c == 'и' ? "&b" : "&B";
                            if (c == 'т' || c == 'Т') return c == 'т' ? "&n" : "&N";
                            if (c == 'ь' || c == 'Ь') return c == 'ь' ? "&m" : "&M";
                            if (c == 'б' || c == 'Б') return ",";
                            if (c == 'в' || c == 'В') return c == 'в' ? "&w" : "&W";
                            if (c == 'ё' || c == 'Ё') return c == 'ё' ? "&`" : "&~";
                            return m.Value;
                        });



                    if (result.Contains("=now.dayOfYear="))

                    {

                        result = result.Replace("=now.dayOfYear=", DateTime.Now.DayOfYear.ToString());

                    }



                    if (result.Contains("=now.year="))
                    {
                        result = result.Replace("=now.year=", DateTime.Now.Year.ToString());
                    }

                    // --- ФИНАЛЬНАЯ ОЧИСТКА МУСОРА ---

                    // 1. Устраняем "франкенштейнов" и рост процентов: %%%WoundLevel% -> WoundLevel
                    // Убираем проценты из технических плейсхолдеров
                    // result = System.Text.RegularExpressions.Regex.Replace(result, 
                    //     @"%+(BodyText|DisplayName|ConText|LongDescription|WoundLevel|Name|Title|Description)%*", 
                    //     "$1", 
                    //     System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                    // 2. Убираем лидирующие и завершающие проценты (кроме числовых процентов в конце, типа "10%")
                    // Если в строке есть кириллица, считаем её переведенной и чистим технический мусор
                    if (ContainsCyrillic(result))
                    {
                        // Убираем любые проценты в начале строки (паддинг UI)
                        result = System.Text.RegularExpressions.Regex.Replace(result, @"^%+", "");
                        
                        // Убираем проценты в конце, ТОЛЬКО если перед ними нет цифры (чтобы не сломать "10%")
                        result = System.Text.RegularExpressions.Regex.Replace(result, @"(?<!\d)%+$", "");
                        
                        // Схлопываем двойные проценты внутри строки
                        result = result.Replace("%%", "%");
                    }

                    // 3. Исправляем поврежденные теги цвета (результат DistributeColors)
                    // Удаляем пустые блоки <color=...></color> (без пробелов)
                    result = System.Text.RegularExpressions.Regex.Replace(result, @"<color=[^>]+></color>", "");
                    // Схлопываем последовательные одинаковые открывающие цветовые теги
                    result = System.Text.RegularExpressions.Regex.Replace(result, @"(<color=[^>]+>)([ \t]*\1)+", "$1");
                    // Схлопываем последовательные закрывающие теги
                    result = System.Text.RegularExpressions.Regex.Replace(result, @"(</color>)([ \t]*\1)+", "$1");
                    
                    // Схлопываем идущие подряд блоки одинакового цвета: <color=X>A</color> <color=X>B</color> -> <color=X>A B</color>
                    string prevResult;
                    do
                    {
                        prevResult = result;
                        result = System.Text.RegularExpressions.Regex.Replace(result, @"(?<tag><color=(?<c>[^>]+)>)(?<content>(?:(?!</color>).)*?)</color>(?<sp>\s*)<color=\k<c>>", "${tag}${content}${sp}", System.Text.RegularExpressions.RegexOptions.Singleline);
                    } while (result != prevResult);

                    // Удаляем висящий закрывающий тег в самом начале
                    if (result.StartsWith("</color>")) result = result.Substring(8);
                }
            }

            // Финальная очистка пустых цветовых блоков ДО логирования и кэширования,
            // чтобы лог all_gameplay_texts.txt не содержал мусора.
            if (result != null) result = StripEmptyColorBlocks(result);
            // Финальная нормализация русского текста: убираем накопление пробелов, лишние точки и т.д.
            // Применяется ДО логирования, чтобы лог all_gameplay_texts.txt не накапливал артефакты.
            if (result != null) result = NormalizeRussianText(result);
            if (result != null)
            {
                try
                {
                    result = MorphologyService.ApplyMorphMarkers(result);
                }
                catch (Exception ex)
                {
                    LogError("[RussianLocalization] Morphology marker processing failed: " + ex.Message);
                }
            }

            // Английский «клей» (артикли и "of"), прилипший к русским словам. Стоит здесь по той
            // же причине, что и фунты ниже: строку собирают три независимых прохода, и латать
            // каждый из них по отдельности — гарантированный разнобой. Обязательно ПОСЛЕ
            // ApplyMorphMarkers, иначе правила лезут внутрь "{{case:...|gen|auto|sg}}".
            if (result != null) result = StripLeftoverEnglishGlue(result);

            // Снятый артикль часто был ЕДИНСТВЕННЫМ содержимым цветного блока — игра режет
            // строку ровно по его границе ("<color=#B1C9C3FF>The </color><color=...>окровавленный").
            // После StripLeftoverEnglishGlue остаётся "<color=#B1C9C3FF></color>", поэтому чистку
            // пустых блоков из строки 1811 приходится повторить: там она отработала раньше.
            if (result != null) result = StripEmptyColorBlocks(result);

            // Регистр первой буквы — ПОСЛЕ снятия артикля: до него в начале строки
            // стоит "The", и поднимать было бы нечего.
            if (result != null) result = CapitalizeMessageLine(result);

            // Фунты -> килограммы. Стоит В САМОМ КОНЦЕ конвейера намеренно: сюда приходит
            // финальная строка независимо от того, кто её собрал — словарь, паттерн или
            // пословный проход. Правь мы вместо этого ~30 паттернов с "lbs", любой
            // непокрытый случай дал бы разнобой «тут кг, там фунты», что хуже честных фунтов.
            if (result != null) result = ConvertPoundsToKilograms(result);

            // if (text == " serving]") // Console.WriteLine($"[DEBUG Translate] final returned result: '{result}'");
            LogAllGameplayText(originalText, result);

            if (result != null)
            {
                translationCache[originalText] = result;
                // Guard B: регистрируем только СМЕШАННЫЕ (рус+лат) результаты, отличные от входа —
                // именно они рискуют испортиться при обратной подаче. Чистый русский и чистый
                // английский сюда не попадают, поэтому множество маленькое и безопасное.
                if (result != originalText && result.Length > 1)
                {
                    bool rHasCyr, rHasEng;
                    ScanAlpha(result, out rHasCyr, out rHasEng);
                    if (rHasCyr && rHasEng) producedOutputs.TryAdd(result, 0);
                }
                MaybeResetTranslationCache();
            }

            return result;
        }

        private static readonly System.Text.RegularExpressions.Regex FactionRegex = new System.Text.RegularExpressions.Regex(@"^<color=(?<c1>#[0-9A-Fa-f]+)>(?<faction>.*?)</color><color=(?<c2>#[0-9A-Fa-f]+)>(?<relation>.*?)(?<end></color>)?$", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);


        public static string TranslateRelationText(string relation)
        {
            if (string.IsNullOrEmpty(relation)) return relation;
            string clean = relation.Replace("\r", "").Replace("\n", " ").Trim();
            while (clean.Contains("  ")) clean = clean.Replace("  ", " ");
            bool hasDot = clean.EndsWith(".");
            if (hasDot) clean = clean.Substring(0, clean.Length - 1).Trim();

            // 1. Проверяем простые статические отношения
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "don't care about you, but aggressive ones will attack you", "не обращают на вас внимания, но агрессивные особи будут атаковать вас" },
                { "don't care about you, but aggressive members will attack you", "не обращают на вас внимания, но агрессивные представители будут атаковать вас" },
                { "doesn't care about you, but aggressive ones will attack you", "не обращает на вас внимания, но агрессивные особи будут атаковать вас" },
                { "doesn't care about you, but aggressive members will attack you", "не обращает на вас внимания, но агрессивные представители будут атаковать вас" },
                { "despise you. Even docile ones will attack you", "презирают вас. Даже миролюбивые особи будут атаковать вас" },
                { "despise you. Even docile members will attack you", "презирают вас. Даже миролюбивые представители будут атаковать вас" },
                { "despises you. Even docile ones will attack you", "презирает вас. Даже миролюбивые особи будут атаковать вас" },
                { "despises you. Even docile members will attack you", "презирает вас. Даже миролюбивые представители будут атаковать вас" },
                { "dislike you, but docile ones won't attack you", "недолюбливают вас, но миролюбивые особи не станут атаковать вас" },
                { "dislike you, but docile members won't attack you", "недолюбливают вас, но миролюбивые представители не станут атаковать вас" },
                { "dislikes you, but docile ones won't attack you", "недолюбливает вас, но миролюбивые особи не станут атаковать вас" },
                { "dislikes you, but docile members won't attack you", "недолюбливает вас, но миролюбивые представители не станут атаковать вас" },
                { "favor you. Aggressive ones won't attack you", "благоволят вам. Агрессивные особи не станут атаковать вас" },
                { "favor you. Aggressive members won't attack you", "благоволят вам. Агрессивные представители не станут атаковать вас" },
                { "favors you. Aggressive ones won't attack you", "благоволит вам. Агрессивные особи не станут атаковать вас" },
                { "favors you. Aggressive members won't attack you", "благоволит вам. Агрессивные представители не станут атаковать вас" },
                { "are interested in hearing gossip that's about them", "заинтересованы в прослушивании слухов, которые их касаются" },
                { "dislike you, but", "недолюбливают вас, но" },
                { "dislikes you, but", "недолюбливает вас, но" },
                { "are interested in trading", "заинтересованы в торговле" },
                { "is interested in trading", "интересуется торговлей" },
                { "are interested in sharing secrets", "заинтересованы в обмене секретами" },
                { "is interested in sharing secrets", "интересуется обменом секретами" },
                { "don't care about you,", "не обращают на вас внимания," },
                { "doesn't care about you,", "не обращает на вас внимания," },
                { "don't care about you", "не обращают на вас внимания" },
                { "doesn't care about you", "не обращает на вас внимания" }
            };

            string trans;
            if (dict.TryGetValue(clean, out trans))
            {
                return trans + (hasDot ? "." : "");
            }

            // 2. Динамический разбор сложных интересов с перечислением тем
            var matchPref = RelationInterestRegex.Match(clean);

            if (matchPref.Success)
            {
                string subj = matchPref.Groups["subj"].Value.ToLower();
                string verb = matchPref.Groups["verb"].Value.ToLower();
                string rest = matchPref.Groups["rest"].Value.Trim();

                string gossipSuffix = "";

                // Проверяем окончание с общим вторым предложением "They're also interested in ..."
                var alsoMatch = RelationAlsoRegex.Match(rest);

                if (alsoMatch.Success)
                {
                    string clause = alsoMatch.Groups["clause"].Value;
                    rest = RelationAlsoRegex.Replace(rest, "").Trim();
                    string translatedClause = TranslateRelationText("are interested in " + clause);
                    if (translatedClause.StartsWith("заинтересованы в ", StringComparison.OrdinalIgnoreCase))
                    {
                        gossipSuffix = ". Они также заинтересованы в " + translatedClause.Substring("заинтересованы в ".Length);
                    }
                    else if (translatedClause.StartsWith("интересуется ", StringComparison.OrdinalIgnoreCase))
                    {
                        gossipSuffix = ". Они также заинтересованы в " + translatedClause.Substring("интересуется ".Length);
                    }
                    else
                    {
                        gossipSuffix = ". Они также заинтересованы в " + translatedClause;
                    }
                }
                else
                {
                    // Проверяем окончание со слухами вариант 1: . They're also interested in hearing gossip that's about them
                    if (RelationGossip1Regex.IsMatch(rest))
                    {
                        rest = RelationGossip1Regex.Replace(rest, "").Trim();
                        gossipSuffix = ". Им также интересно слушать слухи, которые их касаются";
                    }
                    else
                    {
                        // Вариант 2: , and gossip that's about them
                        if (RelationGossip2Regex.IsMatch(rest))
                        {
                            rest = RelationGossip2Regex.Replace(rest, "").Trim();
                            gossipSuffix = " и слухах, которые их касаются";
                        }
                    }
                }

                // Префикс на русском
                string ruVerb = "";
                if (subj == "are")
                {
                    if (verb.Contains("trading") || verb.Contains("sharing"))
                    {
                        ruVerb = "заинтересованы в обмене секретами о ";
                    }
                    else if (verb.Contains("gossip"))
                    {
                        ruVerb = "заинтересованы в прослушивании слухов о ";
                    }
                    else if (verb.Contains("resources"))
                    {
                        ruVerb = "заинтересованы в ресурсах, необходимых для построения новых обществ: ";
                    }
                    else
                    {
                        ruVerb = "заинтересованы в получении сведений о ";
                    }
                }
                else // is
                {
                    if (verb.Contains("trading") || verb.Contains("sharing"))
                    {
                        ruVerb = "интересуется обменом секретами о ";
                    }
                    else if (verb.Contains("gossip"))
                    {
                        ruVerb = "интересуется прослушиванием слухов о ";
                    }
                    else if (verb.Contains("resources"))
                    {
                        ruVerb = "интересуется ресурсами, необходимыми для построения новых обществ: ";
                    }
                    else
                    {
                        ruVerb = "интересуется получением сведений о ";
                    }
                }

                // Заменяем фразу с or на плейсхолдер до разделения по or/and
                bool hasSultans = RelationSultansRegex.IsMatch(rest);
                if (hasSultans)
                {
                    rest = RelationSultansRegex.Replace(rest, "__SULTANS_ADMIRE_DESPISE__");
                }

                // Разбор списка тем с учетом союзов and / or
                bool isOr = false;
                var themes = new List<string>();

                if (rest.Contains(","))
                {
                    string[] parts;
                    if (System.Text.RegularExpressions.Regex.IsMatch(rest, @",\s*or\s+", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    {
                        isOr = true;
                        parts = System.Text.RegularExpressions.Regex.Split(rest, @",\s*or\s+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    }
                    else
                    {
                        parts = System.Text.RegularExpressions.Regex.Split(rest, @",\s*and\s+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    }

                    if (parts.Length > 0)
                    {
                        string[] subThemes = parts[0].Split(',');
                        foreach (var st in subThemes)
                        {
                            string tTrim = st.Trim();
                            if (!string.IsNullOrEmpty(tTrim)) themes.Add(tTrim);
                        }
                    }
                    if (parts.Length > 1)
                    {
                        string tTrim = parts[1].Trim();
                        if (!string.IsNullOrEmpty(tTrim)) themes.Add(tTrim);
                    }
                }
                else
                {
                    string[] parts;
                    if (System.Text.RegularExpressions.Regex.IsMatch(rest, @"\s+or\s+", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    {
                        isOr = true;
                        parts = System.Text.RegularExpressions.Regex.Split(rest, @"\s+or\s+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    }
                    else
                    {
                        parts = System.Text.RegularExpressions.Regex.Split(rest, @"\s+and\s+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    }

                    foreach (var pt in parts)
                    {
                        string tTrim = pt.Trim();
                        if (!string.IsNullOrEmpty(tTrim)) themes.Add(tTrim);
                    }
                }

                string conj = isOr ? " или " : " и ";
                var translatedThemes = new List<string>();
                foreach (var t in themes)
                {
                    if (t == "__SULTANS_ADMIRE_DESPISE__")
                    {
                        translatedThemes.Add("султанах, которыми они восхищаются или которых презирают");
                    }
                    else
                    {
                        string transTheme = TranslateFactionCase(t, "prep");
                        translatedThemes.Add(transTheme);
                    }
                }

                string ruThemes = "";
                if (translatedThemes.Count == 1)
                {
                    ruThemes = translatedThemes[0];
                }
                else if (translatedThemes.Count > 1)
                {
                    var firstParts = translatedThemes.GetRange(0, translatedThemes.Count - 1);
                    ruThemes = string.Join(", ", firstParts.ToArray()) + conj + translatedThemes[translatedThemes.Count - 1];
                }

                string finalTranslation = ruVerb + ruThemes + gossipSuffix;
                if (hasDot && !finalTranslation.EndsWith("."))
                {
                    finalTranslation += ".";
                }
                return finalTranslation;
            }

            return clean + (hasDot ? "." : "");
        }

        public static string TryTranslateFactionReputation(string text, out bool success)
        {
            success = false;
            if (string.IsNullOrEmpty(text)) return text;

            // Если в строке несколько репутаций фракций, разбиваем их и переводим по отдельности
            if (FactionSplitRegex.Matches(text).Count > 1)
            {
                string[] parts = FactionSplitRegex.Split(text);
                if (parts.Length > 1)
                {
                    var sb = new System.Text.StringBuilder(text.Length);
                    bool anySuccess = false;
                    foreach (var part in parts)
                    {
                        if (string.IsNullOrEmpty(part)) continue;
                        
                        bool partSuccess;
                        string transPart = TryTranslateFactionReputation(part, out partSuccess);
                        if (partSuccess)
                        {
                            sb.Append(transPart);
                            anySuccess = true;
                        }
                        else
                        {
                            sb.Append(TranslateInternal(part));
                        }
                    }
                    if (anySuccess)
                    {
                        success = true;
                        return sb.ToString();
                    }
                }
            }

            var leadMatch = System.Text.RegularExpressions.Regex.Match(text, @"^(?<lead>(?:</color>|<color=#[0-9A-Fa-f]+></color>|\s+)*)(?<real>.*)$", System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            string lead = leadMatch.Groups["lead"].Value;
            string realText = leadMatch.Groups["real"].Value;

            // 1. Создаем нормализованную версию только для поиска (сопоставления с регуляркой)
            // Мы временно объединяем теги и убираем переносы, чтобы найти совпадение в словаре
            string normalized = System.Text.RegularExpressions.Regex.Replace(realText, @"[\r\n]+</color>\s*<color=#[0-9A-Fa-f]+>", " ");
            normalized = normalized.Replace("\r", " ").Replace("\n", " ");
            while (normalized.Contains("  ")) normalized = normalized.Replace("  ", " ");
            normalized = normalized.Trim();

            var match = FactionRegex.Match(normalized);
            if (match.Success)
            {
                string faction = match.Groups["faction"].Value.Trim();
                string relation = match.Groups["relation"].Value.Trim();

                string translatedFaction = TranslateInternal(faction);
                string translatedRelation = TranslateRelationText(relation);

                if (translatedRelation != relation)
                {
                    success = true;
                    // ВАЖНО: Вместо жесткого string.Format используем DistributeColors
                    // Это позволит перенести все \n и сложные теги из оригинального realText в перевод
                    string combinedTranslation = translatedFaction + " " + translatedRelation;
                    return lead + DistributeColors(realText, combinedTranslation);
                }
            }

            // Резервный пошаблонный поиск отношений (fallback для строк без тегов или со странной структурой)
            string cleanText = realText.Trim();
            bool hasDot = cleanText.EndsWith(".");
            if (hasDot)
            {
                cleanText = cleanText.Substring(0, cleanText.Length - 1).Trim();
            }

            var templates = new[]
            {
                new {
                    Eng = " don't care about you, but aggressive &lt;ones&gt; will attack you",
                    Ru = " не обращают на вас внимания, но агрессивные &lt;особи&gt; будут атаковать вас"
                },
                new {
                    Eng = " don't care about you, but aggressive &lt;members&gt; will attack you",        
                    Ru = " не обращают на вас внимания, но агрессивные &lt;представители&gt; будут атаковать вас"
                },
                new {
                    Eng = " doesn't care about you, but aggressive &lt;ones&gt; will attack you",
                    Ru = " не обращает на вас внимания, но агрессивные &lt;особи&gt; будут атаковать вас"
                },
                new {
                    Eng = " doesn't care about you, but aggressive &lt;members&gt; will attack you",      
                    Ru = " не обращает на вас внимания, но агрессивные &lt;представители&gt; будут атаковать вас"
                },
                new {
                    Eng = " despise you. Even docile &lt;ones&gt; will attack you",
                    Ru = " презирают вас. Даже миролюбивые &lt;особи&gt; будут атаковать вас"
                },
                new {
                    Eng = " despise you. Even docile &lt;members&gt; will attack you",
                    Ru = " презирают вас. Даже миролюбивые &lt;представители&gt; будут атаковать вас"
                },
                new {
                    Eng = " despises you. Even docile &lt;ones&gt; will attack you",
                    Ru = " презирает вас. Даже миролюбивые &lt;особи&gt; будут атаковать вас"
                },
                new {
                    Eng = " despises you. Even docile &lt;members&gt; will attack you",
                    Ru = " презирает вас. Даже миролюбивые &lt;представители&gt; будут атаковать вас"
                },
                new {
                    Eng = " dislike you, but docile &lt;ones&gt; won't attack you",
                    Ru = " недолюбливают вас, но миролюбивые &lt;особи&gt; не станут вас атаковать"
                },
                new {
                    Eng = " dislike you, but docile &lt;members&gt; won't attack you",
                    Ru = " недолюбливают вас, но миролюбивые &lt;представители&gt; не станут вас атаковать"
                },
                new {
                    Eng = " dislikes you, but docile &lt;ones&gt; won't attack you",
                    Ru = " недолюбливает вас, но миролюбивые &lt;особи&gt; не станут вас атаковать"
                },
                new {
                    Eng = " dislikes you, but docile &lt;members&gt; won't attack you",
                    Ru = " недолюбливает вас, но миролюбивые &lt;представители&gt; не станут вас атаковать"
                },
                new {
                    Eng = " favor you. Aggressive &lt;ones&gt; won't attack you",
                    Ru = " благоволят вам. Агрессивные &lt;особи&gt; не станут вас атаковать"
                },
                new {
                    Eng = " favor you. Aggressive &lt;members&gt; won't attack you",
                    Ru = " благоволят вам. Агрессивные &lt;представители&gt; не станут вас атаковать"
                },
                new {
                    Eng = " favors you. Aggressive &lt;ones&gt; won't attack you",
                    Ru = " благоволит вам. Агрессивные &lt;особи&gt; не станут вас атаковать"
                },
                new {
                    Eng = " favors you. Aggressive &lt;members&gt; won't attack you",
                    Ru = " благоволит вам. Агрессивные &lt;представители&gt; не станут вас атаковать"
                }
            };

            foreach (var t in templates)
            {
                if (cleanText.EndsWith(t.Eng, StringComparison.OrdinalIgnoreCase))
                {
                    string factionPart = cleanText.Substring(0, cleanText.Length - t.Eng.Length).Trim();  
                    string translatedFaction = TranslateText(factionPart);
                    success = true;
                    return lead + translatedFaction + t.Ru + (hasDot ? "." : "");
                }

                string cleanEng = t.Eng.Replace("&lt;", "<").Replace("&gt;", ">");
                string cleanRu = t.Ru.Replace("&lt;", "<").Replace("&gt;", ">");
                if (cleanText.EndsWith(cleanEng, StringComparison.OrdinalIgnoreCase))
                {
                    string factionPart = cleanText.Substring(0, cleanText.Length - cleanEng.Length).Trim();
                    string translatedFaction = TranslateText(factionPart);
                    success = true;
                    return lead + translatedFaction + cleanRu + (hasDot ? "." : "");
                }
            }

            if (RelationInterestRegex.IsMatch(cleanText) ||
                cleanText.StartsWith("don't care about ", StringComparison.OrdinalIgnoreCase) ||
                cleanText.StartsWith("doesn't care about ", StringComparison.OrdinalIgnoreCase) ||
                cleanText.StartsWith("despise ", StringComparison.OrdinalIgnoreCase) ||
                cleanText.StartsWith("despises ", StringComparison.OrdinalIgnoreCase) ||
                cleanText.StartsWith("dislike ", StringComparison.OrdinalIgnoreCase) ||
                cleanText.StartsWith("dislikes ", StringComparison.OrdinalIgnoreCase) ||
                cleanText.StartsWith("favor ", StringComparison.OrdinalIgnoreCase) ||
                cleanText.StartsWith("favors ", StringComparison.OrdinalIgnoreCase))
            {
                string transRelation = TranslateRelationText(cleanText);
                if (transRelation != cleanText)
                {
                    success = true;
                    return lead + transRelation + (hasDot ? "." : "");
                }
            }

            return text;
        }


        // 2026-07-06 (v22): страховочный лимит глубины взаимной рекурсии TryTranslatePattern ↔
        // TranslateText. Первопричину краша уже устранил фикс числовых плейсхолдеров выше, но этот
        // guard гарантирует, что ЛЮБОЙ будущий кривой паттерн не сможет переполнить нативный стек.
        private const int MaxPatternDepth = 16;
        [ThreadStatic]
        private static int _patternDepth;

        // 2026-08-03: лимит длины (введён вместе с фиксом краша v1.0.5) режет длинные
        // рантайм-строки, чтобы не гонять 6 тысяч регулярок по книжным страницам.
        // Но хроника памятника султану ("At twilight ... thenceforth called them X-in-Y")
        // собирается движком и выходит на 360-500 символов — она упиралась в лимит и
        // целиком уезжала в пословный перевод (лог 03.08, строка 3663: английский текст
        // с русскими вставками). Поднимать лимит целиком нельзя — замер на образце из
        // лога дал 211 мс на один прогон по всем паттернам. Поэтому исключение точечное:
        // длинную строку пропускаем, только если в ней есть дешёвый литеральный маркер
        // шаблона хроники. Проверка — IndexOf по подстроке, до регулярок дело не доходит.
        // Результат кладётся в translationCache, так что цена платится один раз.
        private const int PatternMaxLength = 350;
        private static readonly string[] LongTextPatternMarkers =
        {
            "saw an image on the horizon",
        };

        private static bool IsLongTextWorthMatching(string text)
        {
            for (int i = 0; i < LongTextPatternMarkers.Length; i++)
            {
                if (text.IndexOf(LongTextPatternMarkers[i], StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        public static string TryTranslatePattern(string text, out bool success)

        {

            success = false;

            if (string.IsNullOrEmpty(text)) return text;

            if (text.Length > PatternMaxLength && !IsLongTextWorthMatching(text)) return text;

            if (_patternDepth >= MaxPatternDepth) return text;
            _patternDepth++;
            try { return TryTranslatePatternBody(text, ref success); }
            finally { _patternDepth--; }
        }

        private static string TryTranslatePatternBody(string text, ref bool success)
        {

            // Префиксы строк лога/сводки: ":: " (журнал), "> " и "· " (экран смерти Game summary).
            // Снимаем префикс перед матчингом и возвращаем обратно — так одни и те же паттерны
            // работают и в обычном логе, и внутри блоков Game summary/Chronology/Final messages.
            string logPrefix = "";
            if (text.StartsWith(":: ")) logPrefix = ":: ";
            else if (text.StartsWith("> ")) logPrefix = "> ";
            else if (text.StartsWith("· ")) logPrefix = "· ";
            string matchText = logPrefix.Length > 0 ? text.Substring(logPrefix.Length) : text;

            // if (text.Contains("Weight: 5 lbs"))
            // {
            //     Console.WriteLine($"[DEBUG C# TryTranslatePattern] text='{text}', matchText='{matchText}'");
            // }

            // Кандидаты для матчинга: сначала исходный текст, затем — если есть переносы строк —
            // его версия с \n, схлопнутыми в пробелы. Паттерны компилируются без Singleline, поэтому
            // "." не матчит \n: фразы, перенесённые по ширине окна (например, длинный текст интересов
            // фракций), иначе не находятся и уходят в пословный перевод. Нормализованный кандидат это чинит.
            // 2026-07-31: добавлены КАНДИДАТЫ БЕЗ ЦВЕТОРАЗМЕТКИ. Внутриигровой лог сообщений
            // использует классические коды "&X" ("&yYou put the &Bwater-stained leather cap&y…"),
            // а почти все паттерны написаны либо под чистый текст, либо под XML "<color=…>".
            // Из-за этого фразовые паттерны не матчились и строка уходила в пословный перевод —
            // отсюда «Вы помещать the …», «бассейн солоноватый асфальт», «на запад» и т.п.
            // Порядок кандидатов важен: сначала ИСХОДНЫЙ текст (цветоразметка сохраняется, если
            // есть точный цветной паттерн), и только если ничего не совпало — очищенные версии.
            // Компромисс осознанный: на очищенном кандидате теряются цвета внутри фразы, но
            // взамен получаем верную грамматику вместо франкенштейна.
            string bareColorPrefix = "";
            var candidateList = new List<string>(4) { matchText };
            if (matchText.IndexOf('\n') >= 0)
            {
                string collapsed = matchText.Replace("\r", " ").Replace("\n", " ");
                while (collapsed.Contains("  ")) collapsed = collapsed.Replace("  ", " ");
                collapsed = collapsed.Trim();
                if (collapsed.Length > 0 && collapsed != matchText) candidateList.Add(collapsed);
            }
            {
                string bare = candidateList[candidateList.Count - 1];
                if (bare.IndexOf('&') >= 0) bare = StripAmpColorCodes(bare);
                if (bare.IndexOf('<') >= 0) bare = ColorTagRegex.Replace(bare, "");
                if (bare.IndexOf("  ") >= 0)
                {
                    while (bare.Contains("  ")) bare = bare.Replace("  ", " ");
                }
                bare = bare.Trim();
                if (bare.Length > 0 && !candidateList.Contains(bare))
                {
                    candidateList.Add(bare);
                    // Ведущий цветокод строки задаёт её базовый цвет в логе. На «голом» кандидате
                    // разметка теряется, поэтому ведущий токен возвращаем обратно в результат.
                    if (matchText.Length > 1 && matchText[0] == '&' && char.IsLetter(matchText[1]))
                        bareColorPrefix = matchText.Substring(0, 2);
                    else
                    {
                        var lead = ColorTagRegex.Match(matchText);
                        if (lead.Success && lead.Index == 0) bareColorPrefix = lead.Value;
                    }
                }
            }
            string[] candidates = candidateList.ToArray();
            // Индекс последнего кандидата — того самого «голого»; только для него нужен bareColorPrefix.
            int bareIndex = bareColorPrefix.Length > 0 ? candidates.Length - 1 : -1;

            var placeholderRegex = new System.Text.RegularExpressions.Regex(@"\{(?<name>[a-zA-Z0-9_]+)(?::(?<case>[a-z]+))?\}");

            for (int ci = 0; ci < candidates.Length; ci++)
            {
                string candidate = candidates[ci];
                string restoreColor = (ci == bareIndex) ? bareColorPrefix : "";
                for (int i = 0; i < patternDictionary.Count; i++)
                {
                    var rule = patternDictionary[i];
                    var regex = rule.Key;
                    var match = regex.Match(candidate);

                    if (match.Success && match.Index == 0 && match.Length == candidate.Length)
                    {
                        string template = rule.Value;
                        string candidateForClosure = candidate;
                        string result = placeholderRegex.Replace(template, (placeholderMatch) =>
                        {
                            string name = placeholderMatch.Groups["name"].Value;
                            string caseName = placeholderMatch.Groups["case"].Value;
                            // 2026-07-06 (v22 — ПЕРВОПРИЧИНА КРАША В ТОРГОВЛЕ): числовые плейсхолдеры
                            // {0},{1},... в шаблонах — это 0-ИНДЕКСИРОВАННЫЕ ссылки на CAPTURE-ГРУППЫ
                            // (т.е. {0} = первая группа = match.Groups[1]), как задумано авторами шаблонов
                            // (напр. "^HP: (\d+)/(\d+)$" -> "ОЗ: {0}/{1}"). РАНЬШЕ здесь бралась
                            // match.Groups["0"], а в .NET группа "0" = ВСЯ совпавшая строка. Из-за этого
                            // TranslateText получал на вход всё совпадение целиком, оно снова матчило тот
                            // же паттерн -> бесконечная рекурсия -> нативный stack overflow (0xc0000005 в
                            // ntdll, без managed-исключения). Триггер — новый Modern UI вывод боезапаса
                            // "lead slug x69 {{c|}}4 {{r|}}1d2": фрагмент "1d2" по паттерну "^(\d+)d(\d+)$"
                            // -> шаблон "{0}d{1}" -> {0}=вся строка "1d2" -> TranslateText("1d2") -> ...
                            System.Text.RegularExpressions.Group group;
                            int numIdx;
                            if (int.TryParse(name, out numIdx))
                            {
                                group = match.Groups[numIdx + 1];
                            }
                            else
                            {
                                group = match.Groups[name];
                            }
                            if (group.Success)
                            {
                                // Дополнительная защита: НИКОГДА не переводим значение, равное всей
                                // совпавшей строке (или исходному тексту) — иначе оно снова сматчит тот
                                // же паттерн и получится та же бесконечная рекурсия. Возвращаем как есть.
                                if (group.Value == candidateForClosure || group.Value == text)
                                {
                                    return group.Value;
                                }
                                if (name == "features" || name == "mutations")
                                {
                                    string[] parts = group.Value.Split(',');
                                    for (int pIdx = 0; pIdx < parts.Length; pIdx++)
                                    {
                                        parts[pIdx] = TranslateText(parts[pIdx].Trim(), false);
                                    }
                                    return string.Join(", ", parts);
                                }
                                if (!string.IsNullOrEmpty(caseName))
                                {
                                    if (caseName == "raw" || caseName == "asis" || caseName == "none")
                                    {
                                        return TranslateText(group.Value, true);
                                    }
                                    // Явная падежная аннотация в шаблоне: {name:gen}, {object:acc} и т.п.
                                    // Известные фракции — через рукописную таблицу падежей (faction_cases.json).
                                    // Ключ может быть захвачен без ведущего "the" (или наоборот с ним,
                                    // когда в faction_cases.json ключ без артикля) — пробуем оба варианта.
                                    // Прочие существительные — через морфологическое склонение по указанному падежу.
                                    string factionKeyTrim = group.Value.Trim();
                                    string factionKeyNoThe = factionKeyTrim.StartsWith("the ", StringComparison.OrdinalIgnoreCase)
                                        ? factionKeyTrim.Substring(4) : factionKeyTrim;
                                    if (factionCases.ContainsKey(factionKeyTrim) || factionCases.ContainsKey("The " + factionKeyTrim)
                                        || factionCases.ContainsKey(factionKeyNoThe) || factionCases.ContainsKey("The " + factionKeyNoThe))
                                    {
                                        return TranslateFactionCase(group.Value, caseName);
                                    }
                                    MorphCase explicitCase = ParseCaseName(caseName);
                                    string trExplicit = TranslateText(group.Value, true);
                                    try
                                    {
                                        trExplicit = MorphologyService.Decline(trExplicit, explicitCase);
                                    }
                                    catch (Exception ex)
                                    {
                                        LogError("[RussianLocalization] Explicit-case declension failed for '" + trExplicit + "': " + ex.Message);
                                    }
                                    return trExplicit;
                                }
                                else
                                {
                                    MorphCase inferredCase = InferCaseFromTemplate(template, name);
                                    string translated = TranslateText(group.Value, true);
                                    try
                                    {
                                        translated = MorphologyService.Decline(translated, inferredCase);
                                    }
                                    catch (Exception ex)
                                    {
                                        LogError("[RussianLocalization] Auto-declension failed for '" + translated + "': " + ex.Message);
                                    }
                                    return translated;
                                }
                            }
                            if (regex.GroupNumberFromName(name) >= 0)
                            {
                                return "";
                            }
                            return placeholderMatch.Value;
                        });

                        // Эвфония предлога с/со перед "втор-" («со второй роднёй», а не «с второй»).
                        result = System.Text.RegularExpressions.Regex.Replace(result, @"\b[сС] (?=[Вв]тор)",
                            m => char.IsUpper(m.Value[0]) ? "Со " : "со ");

                        success = true;
                        return logPrefix + restoreColor + result;
                    }
                }
            }

            return text;

        }



        // Быстрая проверка имён клавиш через HashSet — избегаем повторных сравнений строк.
        private static readonly HashSet<string> KeyNameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "space", "enter", "esc", "escape", "tab", "backspace", "insert", "delete",
            "home", "end", "pgup", "pgdn", "pageup", "pagedown"
        };

        // 2026-07-22: содержимое {{hotkey|X}} — это ФИЗИЧЕСКАЯ клавиша; переводить её нельзя
        // ("PgDown"->"СтрВниз", "Num 7"->"Номер 7", "arrows"->"стрелы"). Гард срабатывает ТОЛЬКО
        // когда ВСЯ строка целиком — имя клавиши (после снятия обёртки hotkey). Поэтому "arrows"
        // как снаряды внутри предложения не затрагиваются (там не вся строка). end/delete исключены:
        // пересекаются с действиями меню ([End] диалога, кнопка delete -> KeyNameCommandOverrides).
        private static bool IsHotkeyLiteralKey(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            string lower = s.ToLowerInvariant();
            if (lower == "end" || lower == "delete") return false;
            if (KeyNameSet.Contains(lower)) return true;
            if (lower == "pgup" || lower == "pgdown" || lower == "pageup" || lower == "pagedown") return true;
            if (lower == "arrows" || lower == "arrow keys" || lower == "numpad") return true;
            if (lower.Length > 4 && lower.StartsWith("num ")) return true;
            if (lower.Length >= 2 && lower[0] == 'f' && char.IsDigit(lower[1]))
            {
                int fVal;
                if (int.TryParse(lower.Substring(1), out fVal) && fVal >= 1 && fVal <= 12) return true;
            }
            return false;
        }

        private static bool IsKeyName(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            // Кэшируем lowercase-нормализованную строку, чтобы не делать ToLower несколько раз в горячих путях.
            string s = text.Length <= 3 && text[0] == 'f' ? text : text.ToLowerInvariant();
            if (KeyNameSet.Contains(s)) return true;
            if (s.Length > 4 && (s.StartsWith("num ") || s.StartsWith("numpad"))) return true;
            return false;
        }

        private static bool IsKeyNameEx(string word)
        {
            if (string.IsNullOrEmpty(word)) return false;
            string lower = word.ToLowerInvariant();
            if (lower == "end") return false; // Exclude "end" action
            if (KeyNameSet.Contains(lower)) return true;
            if (lower == "ctrl" || lower == "control" || lower == "alt" || lower == "shift" ||
                lower == "up" || lower == "down" || lower == "left" || lower == "right" ||
                lower == "page up" || lower == "page down" || lower == "pgup" || lower == "pgdn") return true;
            if (lower.Length > 4 && (lower.StartsWith("num ") || lower.StartsWith("numpad"))) return true;
            if (lower.Length == 1) return true; // Single char key indicators / hotkeys
            if (lower.Length >= 2 && lower[0] == 'f' && char.IsDigit(lower[1]))
            {
                int fVal;
                if (int.TryParse(lower.Substring(1), out fVal) && fVal >= 1 && fVal <= 12) return true;
            }
            return false;
        }

        private static bool IsKeyInBrackets(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            // Check if it's bracketed (even with color markup like <color...>[</color>...<color...>]</color>)
            if (text.Contains("[") && text.Contains("]"))
            {
                string prefix, core, suffix;
                ExtractCoreText(text, out prefix, out core, out suffix);
                
                string cleaned = core.Trim();
                if (cleaned.StartsWith("[") && cleaned.EndsWith("]"))
                {
                    cleaned = cleaned.Substring(1, cleaned.Length - 2).Trim();
                }
                
                // Strip Qud custom markup like {{W|Delete}} -> Delete
                cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\{\{[^|]+\|", "");
                cleaned = cleaned.Replace("}}", "").Trim();
                
                return IsKeyNameEx(cleaned);
            }
            return false;
        }

        // FIX C1 (2026-07-20): кнопки-команды, совпадающие с именами клавиш.
        // В попапе управления сейвами кнопка "delete" — ОТОБРАЖАЕМЫЙ текст
        // (проверено по метаданным Assembly-CSharp.dll: Popup.PickOption/
        // ShowOptionList в билде 2.0.211.50 возвращают int-индекс, строковых
        // Commands-параметров нет вообще, Hotkeys — IReadOnlyList<char>),
        // поэтому её можно и нужно переводить, но KeyNameSet защищает "delete"
        // как имя клавиши, и перевод не срабатывал. Белый список: переводим
        // только эти слова и только при наличии точной записи в основном
        // словаре. Защита букв в скобках ([Delete] — IsKeyInBrackets/IsHotkey)
        // и hold-confirm клавиш "QUIT"/"DELETE" (регистрозависимые проверки
        // в TranslateInternalClean/TranslateText) НЕ затрагивается.
        private static readonly HashSet<string> KeyNameCommandOverrides = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "delete"
        };

        private static bool TryTranslateKeyNameCommand(string text, out string result)
        {
            result = null;
            if (string.IsNullOrEmpty(text)) return false;
            string trimmed = text.Trim();
            if (!KeyNameCommandOverrides.Contains(trimmed)) return false;
            string exact;
            if (!staticDictionary.TryGetValue(trimmed, out exact)) return false;
            if (string.IsNullOrEmpty(exact) || exact == trimmed) return false;
            // Сохраняем ведущие/ведомые пробелы (игра использует их как паддинг кнопок).
            int lead = 0;
            while (lead < text.Length && char.IsWhiteSpace(text[lead])) lead++;
            int trail = 0;
            while (trail < text.Length - lead && char.IsWhiteSpace(text[text.Length - 1 - trail])) trail++;
            result = text.Substring(0, lead) + exact + text.Substring(text.Length - trail);
            return true;
        }

        // FIX C2 (2026-07-20): защита буквы хоткея в начале строки.
        // "[f] fire" -> "[f] огонь", а НЕ "[ф] огонь": физическая клавиша не
        // меняется, транслитерация буквы в квадратных скобках путает игрока.
        // Если перевод (из словаря или собранный процедурно) начинается с
        // одиночной буквы в скобках, отличной от исходной (в т.ч. кириллической)
        // — восстанавливаем исходную букву. Двойные скобки "[[x]]" (книжная
        // разметка) не матчатся regex'ом и не затрагиваются.
        private static readonly System.Text.RegularExpressions.Regex LeadingHotkeyRegex =
            new System.Text.RegularExpressions.Regex(@"^\[(?<k>[A-Za-z])\]", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex LeadingHotkeyAnyRegex =
            new System.Text.RegularExpressions.Regex(@"^\[.\]", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static string PreserveLeadingHotkey(string source, string translated)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(translated)) return translated;
            var mSrc = LeadingHotkeyRegex.Match(source);
            if (!mSrc.Success) return translated;
            string srcKey = mSrc.Groups["k"].Value;
            var mDst = LeadingHotkeyRegex.Match(translated);
            if (mDst.Success && mDst.Groups["k"].Value == srcKey) return translated;
            var mDstAny = LeadingHotkeyAnyRegex.Match(translated);
            if (mDstAny.Success)
                return "[" + srcKey + "]" + translated.Substring(mDstAny.Length);
            return translated;
        }

        // === 2026-07-23 (Guard D — хоткеи не переводятся НИГДЕ в строке) ===
        // Кейсы из логов 20-23.07: "Ctesiphus [N]" -> "Ктесиф [Н]" (транслитерация
        // одиночной буквы в середине строки), "{{W|[space]}}" -> "{{W|[пробел]}}",
        // "{{hotkey|Space}}" -> "{{hotkey|Пробел}}" (испорченные значения лежат в
        // dictionary.json и возвращаются целиковым совпадением в обход всех гардов).
        // PreserveLeadingHotkey закрывал только [x] В НАЧАЛЕ строки; этот валидатор —
        // финальная сверка хоткей-токенов источника с результатом для всей строки.
        // Принцип: физическая клавиша при переводе не меняется. Мутировавший токен
        // восстанавливаем ТОЛЬКО когда это однозначно безопасно (см. правила ниже).
        private static readonly System.Text.RegularExpressions.Regex HotkeyTokenRegex =
            new System.Text.RegularExpressions.Regex(@"\{\{hotkey\|(?<k>[^{}]*)\}\}", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex WBracketTokenRegex =
            new System.Text.RegularExpressions.Regex(@"\{\{[Ww]\|\[(?<k>[^\]]+)\]\}\}", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex LatinSingleBracketRegex =
            new System.Text.RegularExpressions.Regex(@"\[(?<k>[A-Za-z])\]", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex CyrillicSingleBracketRegex =
            new System.Text.RegularExpressions.Regex(@"\[(?<k>[А-Яа-яЁё])\]", System.Text.RegularExpressions.RegexOptions.Compiled);

        // Содержимое скобок — физическая клавиша (одиночная буква или имя клавиши).
        // "[Unlearned]" и прочие текстовые метки сюда НЕ попадают — их перевод разрешён.
        private static bool IsProtectedBracketKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            string t = key.Trim();
            if (t.Length == 1 && char.IsLetter(t[0])) return true;
            if (KeyNameSet.Contains(t)) return true;
            if (IsHotkeyLiteralKey(t)) return true; // pgup/pgdown/arrows/f1-f12/num N
            return false;
        }

        // Обратная транслитерация одиночных букв (зеркало таблицы Transliterate(),
        // только однобуквенные соответствия). Нужна, чтобы отличить порчу хоткея
        // ([N]->[Н] транслитерацией) от осознанного перевода направления ([N]->[С]).
        private static char MapCyrillicTranslitBack(char c)
        {
            switch (c)
            {
                case 'а': return 'a'; case 'А': return 'A';
                case 'б': return 'b'; case 'Б': return 'B';
                case 'в': return 'v'; case 'В': return 'V';
                case 'г': return 'g'; case 'Г': return 'G';
                case 'д': return 'd'; case 'Д': return 'D';
                case 'е': return 'e'; case 'Е': return 'E';
                case 'з': return 'z'; case 'З': return 'Z';
                case 'и': return 'i'; case 'И': return 'I';
                case 'к': return 'k'; case 'К': return 'K';
                case 'л': return 'l'; case 'Л': return 'L';
                case 'м': return 'm'; case 'М': return 'M';
                case 'н': return 'n'; case 'Н': return 'N';
                case 'о': return 'o'; case 'О': return 'O';
                case 'п': return 'p'; case 'П': return 'P';
                case 'р': return 'r'; case 'Р': return 'R';
                case 'с': return 's'; case 'С': return 'S';
                case 'т': return 't'; case 'Т': return 'T';
                case 'у': return 'u'; case 'У': return 'U';
                case 'ф': return 'f'; case 'Ф': return 'F';
                case 'ы': return 'y'; case 'Ы': return 'Y';
                default: return '\0';
            }
        }

        // Токен считается уцелевшим, если он (или его регистровый вариант для
        // одиночной буквы: [w] -> [W] — та же клавиша) есть в результате.
        private static bool HotkeyTokenSurvives(string result, string token, string key)
        {
            if (result.Contains(token)) return true;
            if (!string.IsNullOrEmpty(key) && key.Length == 1 && char.IsLetter(key[0]))
            {
                char swapped = char.IsUpper(key[0]) ? char.ToLowerInvariant(key[0]) : char.ToUpperInvariant(key[0]);
                if (result.Contains(token.Replace("[" + key + "]", "[" + swapped + "]"))) return true;
                if (result.Contains(token.Replace("|" + key + "}}", "|" + swapped + "}}"))) return true;
            }
            return false;
        }

        // Попарное восстановление токенов одного типа ({{hotkey|X}} или {{W|[X]}}).
        // Восстанавливаем только когда число мутировавших спанов в результате РАВНО
        // числу потерянных токенов источника — иначе не угадываем и ничего не трогаем.
        private static string RestoreMissingHotkeyTokens(string source, string result,
            System.Text.RegularExpressions.Regex tokenRegex,
            System.Predicate<System.Text.RegularExpressions.Match> protect)
        {
            var srcTokens = new List<System.Tuple<string, string>>(); // (полный токен, ключ)
            foreach (System.Text.RegularExpressions.Match m in tokenRegex.Matches(source))
            {
                if (protect != null && !protect(m)) continue;
                srcTokens.Add(System.Tuple.Create(m.Value, m.Groups["k"].Value));
            }
            if (srcTokens.Count == 0) return result;
            var srcSet = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var t in srcTokens) srcSet.Add(t.Item1);
            var missing = new List<System.Tuple<string, string>>();
            foreach (var t in srcTokens)
            {
                if (HotkeyTokenSurvives(result, t.Item1, t.Item2)) continue;
                bool already = false;
                foreach (var x in missing) if (x.Item1 == t.Item1) { already = true; break; }
                if (!already) missing.Add(t);
            }
            if (missing.Count == 0) return result;
            var mutated = new List<System.Text.RegularExpressions.Match>();
            foreach (System.Text.RegularExpressions.Match m in tokenRegex.Matches(result))
            {
                if (!srcSet.Contains(m.Value)) mutated.Add(m);
            }
            if (mutated.Count != missing.Count) return result; // счётчики не сошлись — безопасный выход
            // Заменяем с КОНЦА строки, чтобы индексы ранних совпадений оставались валидны.
            for (int i = missing.Count - 1; i >= 0; i--)
            {
                var mm = mutated[i];
                result = result.Substring(0, mm.Index) + missing[i].Item1 + result.Substring(mm.Index + mm.Length);
            }
            return result;
        }

        private static string PreserveHotkeyTokens(string source, string translated)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(translated)) return translated;
            if (source == translated) return translated;
            string result = translated;

            // A. {{hotkey|X}} — кроме «хоткей-в-слове» (одиночная буква, слитная со
            // словом: "{{hotkey|t}}arget" — движок штатно сливает букву со словом).
            result = RestoreMissingHotkeyTokens(source, result, HotkeyTokenRegex, m =>
            {
                string k = m.Groups["k"].Value;
                if (k.Length == 0) return false;
                if (k.Length == 1)
                {
                    int next = m.Index + m.Length;
                    if (next < source.Length && char.IsLetter(source[next])) return false;
                }
                return true;
            });

            // B. {{W|[X]}} / {{w|[X]}} с клавишным содержимым. Пропускаем тип целиком,
            // если в результате есть =commandKey: — это осознанная подстановка клавиши
            // команды ("{{W|[y]}} {{y|Yes}}" -> "{{W|[=commandKey:Accept=]}} {{y|Да}}").
            if (result.IndexOf("=commandKey:", System.StringComparison.Ordinal) < 0)
            {
                result = RestoreMissingHotkeyTokens(source, result, WBracketTokenRegex,
                    m => IsProtectedBracketKey(m.Groups["k"].Value));
            }

            // C. Одиночная [x] в любом месте строки: восстанавливаем точечно, только
            // если в результате нашлась кириллическая [буква], которая является
            // транслитерацией или раскладкой именно этой буквы ([N]->[Н], [f]->[ф],
            // [r]->[к]). Осознанный перевод направлений ([N]->[С], [W]->[З]) НЕ трогаем.
            var srcLatin = new List<System.Tuple<string, char>>();
            foreach (System.Text.RegularExpressions.Match m in LatinSingleBracketRegex.Matches(source))
            {
                // буквы внутри {{W|[x]}} / {{w|[x]}} — это тип B выше, здесь пропускаем
                int st = m.Index - 4;
                if (st >= 0 && (source.Substring(st, 4) == "{{W|" || source.Substring(st, 4) == "{{w|")) continue;
                srcLatin.Add(System.Tuple.Create(m.Value, m.Groups["k"].Value[0]));
            }
            foreach (var t in srcLatin)
            {
                if (HotkeyTokenSurvives(result, t.Item1, t.Item2.ToString())) continue;
                foreach (System.Text.RegularExpressions.Match mc in CyrillicSingleBracketRegex.Matches(result))
                {
                    char cyr = mc.Groups["k"].Value[0];
                    char back = MapCyrillicTranslitBack(cyr);
                    bool isMutation =
                        (back != '\0' && char.ToLowerInvariant(back) == char.ToLowerInvariant(t.Item2)) ||
                        char.ToLowerInvariant(MapCyrillicCharToEnglish(cyr)) == char.ToLowerInvariant(t.Item2);
                    if (isMutation)
                    {
                        result = result.Substring(0, mc.Index) + t.Item1 + result.Substring(mc.Index + mc.Length);
                        break;
                    }
                }
            }

            // D. 2026-08-03: форма "[{{W|X}}]" — скобка СНАРУЖИ разметки. Тип B ловит обратный
            // порядок "{{W|[X]}}", тип C такие буквы пропускает как «внутри разметки», и в щель
            // между ними уезжали клавиши меню: лог 03.08 дал "Ежедневно [Д]" вместо [D],
            // "Учебное пособие [Е]" вместо [E], "Классический [А]" вместо [A]. Игрок видит
            // букву, которой нет на клавише. Сопоставление позиционное: i-я такая скобка
            // источника соответствует i-й в результате.
            if (source.IndexOf("[{{", System.StringComparison.Ordinal) >= 0)
            {
                var srcKeys = new List<string>();
                foreach (System.Text.RegularExpressions.Match m in MarkedBracketKeyRegex.Matches(source))
                {
                    string k = m.Groups["k"].Value;
                    if (k.Length == 1 && ((k[0] >= 'a' && k[0] <= 'z') || (k[0] >= 'A' && k[0] <= 'Z'))) srcKeys.Add(k);
                    else srcKeys.Add(null);
                }
                if (srcKeys.Count > 0)
                {
                    int idx = 0;
                    result = MarkedBracketKeyRegex.Replace(result, m =>
                    {
                        string lat = idx < srcKeys.Count ? srcKeys[idx] : null;
                        idx++;
                        if (lat == null) return m.Value;
                        string cur = m.Groups["k"].Value;
                        if (cur == lat) return m.Value;
                        if (!ContainsCyrillic(cur)) return m.Value;
                        return m.Value.Replace("|" + cur + "}}", "|" + lat + "}}");
                    });
                }
            }
            return result;
        }

        // "[{{W|A}}]" — клавиша, обёрнутая разметкой цвета, скобка снаружи.
        private static readonly System.Text.RegularExpressions.Regex MarkedBracketKeyRegex =
            new System.Text.RegularExpressions.Regex(@"\[\{\{[A-Za-z&]+\|(?<k>[^}\]]{1,2})\}\}\]",
                System.Text.RegularExpressions.RegexOptions.Compiled);


        // 2026-07-06 (v20 — ВОЗМОЖНАЯ НАСТОЯЩАЯ ПРИЧИНА): TranslateInternal() вызывает сам себя
        // (через TranslateInternalClean) БЕЗ КАКОГО-ЛИБО лимита глубины сразу в 4 местах: разбор
        // одного цветового тега вокруг всей строки, перевод по абзацам, перевод по строкам при
        // переносах, и цикл по всем цветовым блокам (ColorBlockRegex). В отличие от TranslateMarkup
        // (MaxMarkupDepth=48) и Description_Patches (MaxDescriptionRecursionDepth=24), эта рекурсия
        // никогда не была защищена. Список предметов в торговле с несколькими цветовыми тегами
        // (качество, цена и т.д.) — ровно то, что могло дать существенную глубину именно тут.
        private const int MaxTranslateInternalDepth = 24;

        // Порог "огромного текста" в TranslateInternalClean (см. там же).
        private const int OversizeThreshold = 3000;
        [ThreadStatic]
        private static int _translateInternalDepth;
        private static string TranslateInternal(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string cached;
            if (translationCache.TryGetValue(text, out cached)) return cached;
            if (_translateInternalDepth >= MaxTranslateInternalDepth) return text;
            _translateInternalDepth++;
            try
            {
                return TranslateInternalBody(text);
            }
            finally
            {
                _translateInternalDepth--;
            }
        }

        private static string TranslateInternalBody(string text)
        {
            string originalText = text;
            // Предварительно схлопываем посимвольные цветовые блоки английских слов,
            // чтобы словарь мог перевести цельное слово вместо отдельных букв.
            text = CompactColorFragments(text);

            // Восстановление битых кавычек из-за кодировок консоли
            text = text.Replace('½', '«').Replace('╗', '»');


            // Защита горячих клавиш и системных имен
            string sn = text.Trim().ToLower();
            if (sn.Length == 1 && ((sn[0] >= 'a' && sn[0] <= 'z') || (sn[0] >= '0' && sn[0] <= '9')))
            {
                return text; // Не переводим одиночные буквы/цифры (хоткеи)
            }
            if (IsKeyName(sn) || IsKeyInBrackets(text))
            {
                // FIX C1 (2026-07-20): кнопки-команды из белого списка (delete)
                // переводим по словарю; остальные имена клавиш не трогаем.
                string keyCommandTr;
                if (TryTranslateKeyNameCommand(text, out keyCommandTr)) return keyCommandTr;
                return text; // Не переводим системные имена клавиш
            }

            // Очищаем \r для предотвращения поломки ключей при переносах строк в Windows
            text = text.Replace("\r", "");

            // Обработка цветовых префиксов Caves of Qud: &K, &C, &Y, &y, &g, &r, &W, &B, &R, &M и т.д.
            string colorPrefix = "";
            if (text.Length >= 2 && text[0] == '&' && char.IsLetter(text[1]))
            {
                colorPrefix = text.Substring(0, 2);
                text = text.Substring(2);
            }

            // Рекурсивный сбор и очистка unmatched ведущих/ведомых тегов цвета
            List<string> leadTags = new List<string>();
            List<string> trailTags = new List<string>();
            bool changed = true;

            while (changed)
            {
                changed = false;
                if (text.StartsWith("</color>"))
                {
                    leadTags.Add("</color>");
                    text = text.Substring(8);
                    changed = true;
                }
                else if (text.StartsWith("<color="))
                {
                    int gt = text.IndexOf('>');
                    if (gt > 0)
                    {
                        string tag = text.Substring(0, gt + 1);
                        if (!text.Substring(gt + 1).Contains("</color>"))
                        {
                            leadTags.Add(tag);
                            text = text.Substring(gt + 1);
                            changed = true;
                        }
                    }
                }

                if (text.EndsWith("</color>"))
                {
                    if (!text.Substring(0, text.Length - 8).Contains("<color="))
                    {
                        trailTags.Insert(0, "</color>");
                        text = text.Substring(0, text.Length - 8);
                        changed = true;
                    }
                }
                else if (text.EndsWith(">"))
                {
                    int lt = text.LastIndexOf("<color=");
                    if (lt >= 0)
                    {
                        string tag = text.Substring(lt);
                        if (tag.StartsWith("<color=") && tag.EndsWith(">"))
                        {
                            if (!text.Substring(0, lt).Contains("</color>"))
                            {
                                trailTags.Insert(0, tag);
                                text = text.Substring(0, lt);
                                changed = true;
                            }
                        }
                    }
                }
            }

            string result = TranslateInternalClean(text);

            if (leadTags.Count > 0) result = string.Join("", leadTags) + result;
            if (trailTags.Count > 0) result = result + string.Join("", trailTags);

            // Возвращаем цветовой префикс
            if (!string.IsNullOrEmpty(colorPrefix))
            {
                result = colorPrefix + result;
            }

            if (result != null)
            {
                translationCache[originalText] = result;
            }
            return result;
        }

        private static string TranslateInternalClean(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            bool success = false;

            // Защита горячих клавиш и системных имен
            string sn = text.Trim().ToLower();
            if (sn.Length == 1 && ((sn[0] >= 'a' && sn[0] <= 'z') || (sn[0] >= '0' && sn[0] <= '9')))
            {
                return text; // Не переводим одиночные буквы/цифры (хоткеи)
            }
            if (IsKeyName(sn) || IsKeyInBrackets(text))
            {
                // FIX C1 (2026-07-20): кнопки-команды из белого списка (delete)
                // переводим по словарю; остальные имена клавиш не трогаем.
                string keyCommandTr;
                if (TryTranslateKeyNameCommand(text, out keyCommandTr)) return keyCommandTr;
                return text; // Не переводим системные имена клавиш
            }

            // Очищаем \r для предотвращения поломки ключей при переносах строк в Windows
            text = text.Replace("\r", "");

            // Обработка цветовых префиксов Caves of Qud: &K, &C, &Y, &y, &g, &r, &W, &B, &R, &M и т.д.
            // Извлекаем префикс, переводим текст, возвращаем префикс обратно.
            string colorPrefix = "";
            if (text.Length >= 2 && text[0] == '&' && char.IsLetter(text[1]))
            {
                colorPrefix = text.Substring(0, 2);
                text = text.Substring(2);
            }

            string earlyTrimmed = text.Replace('\u00A0', ' ')
                                      .Replace('\u2007', ' ')
                                      .Replace('\u200B', ' ')
                                      .Replace('\u202F', ' ')
                                      .Trim();

            // Case-sensitive check for uppercase confirmation keys only.
            if (earlyTrimmed == "QUIT" || earlyTrimmed == "ABANDON" || earlyTrimmed == "RETIRE" || earlyTrimmed == "ABANDONED" || earlyTrimmed == "DELETE" ||
                earlyTrimmed == "Q U I T" || earlyTrimmed == "A B A N D O N" || earlyTrimmed == "R E T I R E" || earlyTrimmed == "A B A N D O N E D" || earlyTrimmed == "D E L E T E")
            {
                return text;
            }

            if (!string.IsNullOrEmpty(earlyTrimmed))
            {
                string earlyExactMatch;
                if (staticDictionary.TryGetValue(earlyTrimmed, out earlyExactMatch))
                {
                    int startSpaces = 0;
                    while (startSpaces < text.Length && char.IsWhiteSpace(text[startSpaces])) startSpaces++;

                    int endSpaces = 0;
                    while (endSpaces < text.Length && char.IsWhiteSpace(text[text.Length - 1 - endSpaces])) endSpaces++;

                    string prefix = text.Substring(0, startSpaces);
                    string suffix = text.Substring(text.Length - endSpaces);

                    string result = colorPrefix + prefix + earlyExactMatch + suffix;
                    translationCache[text] = result;
                    return result;
                }
            }

            // БЫСТРЫЙ ВЫХОД ДЛЯ ОГРОМНЫХ ТЕКСТОВ:
            // Если строка > 3000 символов (справка, титры) и её нет в словаре/кэше,
            // мы не пускаем её в тяжёлую пословную обработку, чтобы не вешать игру.
            if (text.Length > OversizeThreshold)
            {
                string earlyCached;
                if (translationCache.TryGetValue(text, out earlyCached)) return earlyCached;

                string earlyExact;
                string cleanL = text.Trim();
                if (staticDictionary.TryGetValue(cleanL, out earlyExact)) return earlyExact;

                string earlySn = SuperNormalize(cleanL);
                string origKey;
                if (normalizedKeyDictionary.TryGetValue(earlySn, out origKey))
                {
                    if (staticDictionary.TryGetValue(origKey, out earlyExact)) return earlyExact;
                }

                // 2026-07-28: ПОПЫТКА пускать длинные многострочные тексты на разбиение по
                // абзацам/строкам ОТКАЧЕНА — она вешала игру при открытии справки. Разбиение
                // само по себе дешёвое, но каждая из ~200 строк Credits уходила в полную
                // пословную обработку (в т.ч. прогон по всему patternDictionary), и всё это
                // синхронно на одном кадре. Длинные страницы справки переводим только
                // целостраничным точным ключом — он проверяется выше и стоит один поиск в хеше.

                // Не нашли - возвращаем оригинал, кэшируем "неудачу"
                translationCache[text] = text;
                return text;
            }

            // Если вся строка обернута в один цветовой тег, сначала снимаем его и переводим содержимое
            var wrapperMatch = ColorWrapperRegex.Match(text);
            if (wrapperMatch.Success && !wrapperMatch.Groups["content"].Value.Contains("</color>"))
            {
                return wrapperMatch.Groups["pref"].Value + TranslateInternal(wrapperMatch.Groups["content"].Value) + wrapperMatch.Groups["suff"].Value;
            }

            // То же самое для новой разметки {{y|...}}. Popup-окна приходят обёрнутыми целиком,
            // и без снятия обёртки не срабатывает ни точный ключ, ни один паттерн с "^":
            // в логе 04.08 "&yYou can't remove your quills..." переводится, а тот же текст
            // в виде "{{y|&yYou can't remove your quills...}}" возвращается как есть.
            string qudTag, qudContent;
            if (TryUnwrapQudMarkup(text, out qudTag, out qudContent))
            {
                return "{{" + qudTag + "|" + TranslateInternal(qudContent) + "}}";
            }

            string modernUITranslated = TryTranslateModernUI(text, out success);
            if (success)
            {
                return modernUITranslated;
            }

            string factionTranslated = TryTranslateFactionReputation(text, out success);
            if (success)
            {
                return factionTranslated;
            }

            if (ParagraphSplitRegex.IsMatch(text))
            {
                string[] paragraphs = ParagraphSplitRegex.Split(text);
                bool canSplit = true;
                foreach (string p in paragraphs)
                {
                    int openCount = CountSubstring(p, "<color=");
                    int closeCount = CountSubstring(p, "</color>");
                    int openBraces = CountSubstring(p, "{{");
                    int closeBraces = CountSubstring(p, "}}");
                    if (openCount != closeCount || openBraces != closeBraces)
                    {
                        canSplit = false;
                        break;
                    }
                }
                if (canSplit)
                {
                    string[] translatedParagraphs = new string[paragraphs.Length];
                    for (int i = 0; i < paragraphs.Length; i++)
                    {
                        translatedParagraphs[i] = TranslateInternal(paragraphs[i]);
                    }
                    return string.Join("\n\n", translatedParagraphs);
                }
            }

            // 1.5. Перед построчным разбиением: пробуем перевести весь блок как ОДНУ строку
            // со схлопнутыми переносами. Длинные шаблонные фразы (текст интересов фракций
            // "X are interested in sharing secrets about the locations of ...") разрываются
            // переносом по ширине окна, и по отдельным строкам паттерн их не находит —
            // строки уходят в пословный перевод ("are interested in обмен secrets ...").
            // Если паттерн/словарь матчит схлопнутый блок целиком — берём его (игра сама перевернёт).
            if (text.IndexOf('\n') >= 0)
            {
                string collapsed = System.Text.RegularExpressions.Regex.Replace(
                    text.Replace("\r", " ").Replace("\n", " "), @"[ \t]{2,}", " ").Trim();
                if (collapsed.Length > 0 && collapsed != text.Trim())
                {
                    bool pSucc;
                    string pTrans = TryTranslatePattern(collapsed, out pSucc);
                    if (pSucc && pTrans != collapsed) return pTrans;
                    string exactC;
                    if (staticDictionary.TryGetValue(collapsed, out exactC)) return exactC;
                }
            }

            // 2. Построчный перевод при наличии \n (для сохранения форматирования и каст)
            bool isQuestDialog = text.Contains("Come, close! I have an errand") ||
                                 text.Contains("Friend, there was a time") ||
                                 text.Contains("welcome to the village of");
            if (text.Contains("\n") && !isQuestDialog)
            {
                bool canSplitLines = true;
                string[] lines = text.Split('\n');
                foreach (string line in lines)
                {
                    int openBraces = CountSubstring(line, "{{");
                    int closeBraces = CountSubstring(line, "}}");
                    if (openBraces != closeBraces)
                    {
                        canSplitLines = false;
                        break;
                    }
                }
                if (canSplitLines)
                {
                    string[] translatedLines = new string[lines.Length];
                    bool anyChanged = false;
                    for (int i = 0; i < lines.Length; i++)
                    {
                        translatedLines[i] = TranslateInternal(lines[i]);
                        if (translatedLines[i] != lines[i]) anyChanged = true;
                    }
                    if (anyChanged)
                    {
                        return string.Join("\n", translatedLines);
                    }
                }
            }

            // Попытка перевода всей строки без тегов с последующим распределением цветов (только при наличии точного совпадения или паттерна)
            if (text.Contains("<color="))
            {
                string stripped = TagRegex.Replace(text, "");
                bool isExact = false;
                string translatedStripped = TranslateTextStrict(stripped);
                if (translatedStripped != stripped)
                {
                    isExact = true;
                }
                
                bool isPattern = false;
                if (!isExact)
                {
                    string patTrans = TryTranslatePattern(stripped, out isPattern);
                    if (isPattern)
                    {
                        translatedStripped = patTrans;
                    }
                }
                
                if (isExact || isPattern)
                {
                    if (translatedStripped != stripped && !string.IsNullOrEmpty(translatedStripped))
                    {
                        string finalTranslated = DistributeColors(text, translatedStripped);
                        if (finalTranslated != text)
                        {
                            translationCache[text] = finalTranslated;
                            return finalTranslated;
                        }
                    }
                }
            }

            // 2. Рекурсивный разбор цветовых блоков (color blocks)
            var colorMatches = ColorBlockRegex.Matches(text);
            if (colorMatches.Count > 0 && (colorMatches.Count > 1 || colorMatches[0].Length != text.Length))
            {
                var sb = new System.Text.StringBuilder(text.Length);
                int lastIdx = 0;
                for (int i = 0; i < colorMatches.Count; i++)
                {
                    var m = colorMatches[i];
                    if (m.Index > lastIdx)
                    {
                        string between = text.Substring(lastIdx, m.Index - lastIdx);
                        sb.Append(TranslateInternal(between));
                    }

                    string pref = m.Groups["pref"].Value;
                    string content = m.Groups["content"].Value;
                    string suff = m.Groups["suff"].Value;

                    sb.Append(pref);
                    sb.Append(TranslateInternal(content));
                    sb.Append(suff);

                    lastIdx = m.Index + m.Length;
                }
                if (lastIdx < text.Length)
                {
                    string rest = text.Substring(lastIdx);
                    sb.Append(TranslateInternal(rest));
                }
                return sb.ToString();
            }
            else if (colorMatches.Count == 1 && colorMatches[0].Length == text.Length)
            {
                var m = colorMatches[0];
                string pref = m.Groups["pref"].Value;
                string content = m.Groups["content"].Value;
                string suff = m.Groups["suff"].Value;
                return pref + TranslateInternal(content) + suff;
            }



            string patternTranslated = TryTranslatePattern(text, out success);

            if (success)

            {

                return patternTranslated;

            }



            if (!ContainsEnglish(text)) return text;



            // Нормализуем все типы неразрывных и невидимых пробелов в стандартный ASCII пробел

            string normalized = text.Replace('\u00A0', ' ')

                                    .Replace('\u2007', ' ')

                                    .Replace('\u200B', ' ')

                                    .Replace('\u202F', ' ');



            string trimmed = normalized.Trim();

            if (trimmed.Length == 0) return text;



            // Проверяем кэш переводов по оригинальной входящей строке

            string cached;

            if (translationCache.TryGetValue(text, out cached))

            {

                return cached;

            }



            // Выделяем русский префикс (например, имя фракции), если игра подставила его до перевода

            string rusPrefix;

            string engPart;

            ExtractRussianPrefix(normalized, out rusPrefix, out engPart);

            if (!string.IsNullOrEmpty(rusPrefix) && !string.IsNullOrEmpty(engPart))

            {

                string translatedEng = TranslateInternal(engPart);

                string result = colorPrefix + rusPrefix + translatedEng;

                translationCache[text] = result;

                return result;

            }



            // Ищем точное совпадение в словаре по нормализованному ключу (ВСЯ строка целиком)

            string exactMatch;

            if (staticDictionary.TryGetValue(trimmed, out exactMatch))

            {

                int startSpaces = 0;

                while (startSpaces < text.Length && char.IsWhiteSpace(text[startSpaces])) startSpaces++;



                int endSpaces = 0;

                while (endSpaces < text.Length && char.IsWhiteSpace(text[text.Length - 1 - endSpaces])) endSpaces++;



                string prefix = text.Substring(0, startSpaces);

                string suffix = text.Substring(text.Length - endSpaces);



                string result = colorPrefix + prefix + exactMatch + suffix;

                translationCache[text] = result;

                return result;

            }

            else
            {
                // Попытка найти нормализованный ключ по всей строке
                string snFull = SuperNormalize(trimmed);

                // ЗАЩИТА: Если оригинальный trimmed текст не в скобках,
                // но его супер-нормализованная форма - это одиночная латинская буква или известное название клавиши,
                // то мы запрещаем перевод через normalizedKeyDictionary, так как это ASCII-арт, тайл или граффити.
                bool isKeyName = snFull.Length == 1 || KeyNameSet.Contains(snFull);
                bool hasBrackets = trimmed.StartsWith("[") && trimmed.EndsWith("]");

                if (!isKeyName || hasBrackets)
                {
                    string originalKey;
                    if (normalizedKeyDictionary.TryGetValue(snFull, out originalKey))
                    {
                        if (staticDictionary.TryGetValue(originalKey, out exactMatch))
                        {
                            int startSpaces = 0;
                            while (startSpaces < text.Length && char.IsWhiteSpace(text[startSpaces])) startSpaces++;

                            int endSpaces = 0;
                            while (endSpaces < text.Length && char.IsWhiteSpace(text[text.Length - 1 - endSpaces])) endSpaces++;

                            string prefix = text.Substring(0, startSpaces);
                            string suffix = text.Substring(text.Length - endSpaces);

                            string restoredExact = RestoreStrippedPunctuation(trimmed, originalKey, exactMatch);
                            string result = colorPrefix + prefix + restoredExact + suffix;
                            translationCache[text] = result;
                            return result;
                        }
                    }
                }
            }



            // Попытка найти перевод по тексту с удалёнными тегами и нормализованными переносами строк.

            // Это покрывает диалоги NPC, где <color=...> разрезает фразу, разбивая совпадение со словарём.

            if (text.Contains("<color=") || text.Contains("\r") || text.Contains("\n"))

            {

                string strippedText = TagRegex.Replace(trimmed, "");

                strippedText = strippedText.Replace("\r", " ").Replace("\n", " ");

                // Схлопываем множественные пробелы в один

                while (strippedText.Contains("  "))

                    strippedText = strippedText.Replace("  ", " ");

                strippedText = strippedText.Trim();



                if (!string.IsNullOrEmpty(strippedText))
                {
                    string strippedExact;
                    if (staticDictionary.TryGetValue(strippedText, out strippedExact))
                    {
                        string result = text.Contains("<color=") ? DistributeColors(text, strippedExact) : strippedExact;
                        translationCache[text] = colorPrefix + result;
                        return colorPrefix + result;
                    }

                    string strippedSn = SuperNormalize(strippedText);
                    string strippedOrigKey;
                    if (normalizedKeyDictionary.TryGetValue(strippedSn, out strippedOrigKey))
                    {
                        if (staticDictionary.TryGetValue(strippedOrigKey, out strippedExact))
                        {
                            string result = text.Contains("<color=") ? DistributeColors(text, strippedExact) : strippedExact;
                            translationCache[text] = colorPrefix + result;
                            return colorPrefix + result;
                        }
                    }
                }

            }



            // Если точного совпадения по всей строке нет, используем разбор разметки для защиты тегов

            string processedText = TranslateMarkup(normalized);

            if (ContainsEnglish(processedText))

            {

                LogUntranslated(trimmed);

            }

            translationCache[text] = colorPrefix + processedText;

            return colorPrefix + processedText;

        }



        public static string TranslateMarkup(string text)

        {

            return TranslateMarkup(text, 0);

        }

        // 2026-07-05: лимит глубины рекурсии для вложенных {{color|...}} блоков. Сама функция
        // рекурсивно вызывает себя на содержимом справа от "|" (см. ниже) без ограничения —
        // при аномально глубоко вложенной разметке (например, в описании предмета в магазине)
        // это стабильно давало access violation 0xc0000005 в ntdll.dll (нативный стек-оверфлоу,
        // без managed-исключения в логе) — тот же класс бага, что уже чинили для WalkVisualTree
        // (MaxWalkDepth=512) и для TranslateVisualTree. На глубине MaxMarkupDepth дальнейшую
        // вложенную разметку не переводим (возвращаем как есть), чтобы никогда не переполнить стек.
        private const int MaxMarkupDepth = 48;

        private static string TranslateMarkup(string text, int depth)

        {

            if (string.IsNullOrEmpty(text)) return text;

            if (depth >= MaxMarkupDepth) return text;



            StringBuilder result = new StringBuilder();

            StringBuilder currentText = new StringBuilder();

            int i = 0;

            int len = text.Length;



            while (i < len)

            {

                // Проверяем игровой Markup {{

                if (i < len - 1 && text[i] == '{' && text[i + 1] == '{' && text.IndexOf("}}", i) != -1)

                {

                    if (currentText.Length > 0)

                    {

                        result.Append(TranslateText(currentText.ToString()));

                        currentText.Length = 0;

                    }



                    i += 2; // пропускаем {{

                    int braceCount = 1;

                    StringBuilder markupContent = new StringBuilder();



                    while (i < len)

                    {

                        if (i < len - 1 && text[i] == '}' && text[i + 1] == '}')

                        {

                            braceCount--;

                            if (braceCount == 0)

                            {

                                i += 2; // пропускаем }}

                                break;

                            }

                            // Вложенная закрывающая }} — потребляем ОБА символа (фикс потери скобок)

                            markupContent.Append("}}");

                            i += 2;

                            continue;

                        }

                        else if (i < len - 1 && text[i] == '{' && text[i + 1] == '{')

                        {

                            braceCount++;

                            markupContent.Append("{{");

                            i += 2;

                            continue;

                        }

                        markupContent.Append(text[i]);

                        i++;

                    }



                    string content = markupContent.ToString();

                    int pipeIdx = content.IndexOf('|');

                    if (pipeIdx != -1)

                    {

                        string left = content.Substring(0, pipeIdx);

                        string right = content.Substring(pipeIdx + 1);

                        result.Append("{{" + left + "|" + TranslateMarkup(right, depth + 1) + "}}");

                    }

                    else

                    {

                        result.Append("{{" + content + "}}");

                    }

                    continue;

                }



                // Проверяем XML RTF-тег &lt;

                if (i < len - 3 && text[i] == '&' && text[i + 1] == 'l' && text[i + 2] == 't' && text[i + 3] == ';')

                {

                    int gtIdx = text.IndexOf("&gt;", i + 4);

                    if (gtIdx != -1)

                    {

                        if (currentText.Length > 0)

                        {

                            result.Append(TranslateText(currentText.ToString()));

                            currentText.Length = 0;

                        }



                        result.Append("&lt;");

                        i += 4; // пропускаем &lt;



                        while (i < gtIdx)

                        {

                            result.Append(text[i]);

                            i++;

                        }



                        result.Append("&gt;");

                        i += 4; // пропускаем &gt;

                        continue;

                    }

                }



                // Проверяем Unity RTF-тег <

                if (text[i] == '<' && text.IndexOf('>', i) != -1)

                {

                    if (currentText.Length > 0)

                    {

                        result.Append(TranslateText(currentText.ToString()));

                        currentText.Length = 0;

                    }



                    StringBuilder tagContent = new StringBuilder();

                    tagContent.Append('<');

                    i++; // пропускаем <



                    while (i < len)

                    {

                        tagContent.Append(text[i]);

                        if (text[i] == '>')

                        {

                            i++;

                            break;

                        }

                        i++;

                    }



                    result.Append(tagContent.ToString());

                    continue;

                }



                // Проверяем цветной амперсанд-тег &

                if (text[i] == '&' && i < len - 1 && (char.IsLetter(text[i + 1]) || text[i + 1] == '&'))

                {

                    if (currentText.Length > 0)

                    {

                        result.Append(TranslateText(currentText.ToString()));

                        currentText.Length = 0;

                    }



                    result.Append(text[i]);

                    result.Append(text[i + 1]);

                    i += 2;

                    continue;

                }



                currentText.Append(text[i]);

                i++;

            }



            if (currentText.Length > 0)

            {

                result.Append(TranslateText(currentText.ToString()));

            }



            return result.ToString();

        }



        private static readonly HashSet<char> PunctuationAndSpaces = new HashSet<char>
        {
            ' ', '\t', '\r', '\n', '.', ',', '!', '?', ':', ';', '~', '-', '_', '"', '\'', '(', ')', '\u00A0', '\u2007', '\u200B', '\u202F'
        };



        public static void ExtractCoreText(string text, out string prefix, out string core, out string suffix)

        {

            prefix = "";

            core = text;

            suffix = "";



            if (string.IsNullOrEmpty(text)) return;



            int start = 0;

            int len = text.Length;



            while (start < len && PunctuationAndSpaces.Contains(text[start]))

            {

                start++;

            }



            if (start == len)

            {

                prefix = text;

                core = "";

                suffix = "";

                return;

            }



            int end = len - 1;

            while (end >= start && PunctuationAndSpaces.Contains(text[end]))

            {

                if (text[end] == '.' && end >= start + 3)

                {

                    char c1 = char.ToLowerInvariant(text[end - 1]);

                    char c2 = char.ToLowerInvariant(text[end - 2]);

                    char c3 = char.ToLowerInvariant(text[end - 3]);

                    if (c1 == 's' && c2 == 'b' && c3 == 'l')

                    {

                        break;

                    }

                }

                end--;

            }



            prefix = text.Substring(0, start);

            core = text.Substring(start, end - start + 1);

            suffix = text.Substring(end + 1);
        }

        private static readonly Dictionary<string, MorphCase> PrepositionCases = 
            new Dictionary<string, MorphCase>(StringComparer.OrdinalIgnoreCase)
        {
            // 2026-07-31: "в"/"на" ОТСУТСТВОВАЛИ здесь, хотя ниже в InferCaseFromTemplate есть
            // ветка `if (w == "в" || w == "на")`, уточняющая Acc/Prep по глаголу. Без ключа
            // TryGetValue не срабатывал, ветка была недостижима, и падеж оставался Nom — отсюда
            // «на запад» вместо «на западе» и «в луже асфальт» вместо «в луже асфальта» во всех
            // паттернах сразу. Значение здесь — лишь заглушка: реальный падеж выбирает та ветка.
            { "в", MorphCase.Prep },
            { "на", MorphCase.Prep },
            { "со", MorphCase.Gen },
            { "мимо", MorphCase.Gen },
            { "около", MorphCase.Gen },
            { "у", MorphCase.Gen },
            { "возле", MorphCase.Gen },
            { "возле/у", MorphCase.Gen },
            { "после", MorphCase.Gen },
            { "для", MorphCase.Gen },
            { "без", MorphCase.Gen },
            { "из", MorphCase.Gen },
            { "от", MorphCase.Gen },
            { "до", MorphCase.Gen },
            { "к", MorphCase.Dat },
            { "по", MorphCase.Dat },
            { "через", MorphCase.Acc },
            { "про", MorphCase.Acc },
            { "сквозь", MorphCase.Acc },
            { "над", MorphCase.Ins },
            { "под", MorphCase.Ins },
            { "перед", MorphCase.Ins },
            { "за", MorphCase.Ins },
            { "между", MorphCase.Ins },
            { "перед/за", MorphCase.Ins },
            { "при", MorphCase.Prep },
            { "о", MorphCase.Prep },
            { "об", MorphCase.Prep },
            { "обо", MorphCase.Prep }
            // 2026-08-02, НАЙДЕННЫЙ, НО СОЗНАТЕЛЬНО НЕ ЗАКРЫТЫЙ ЗДЕСЬ БАГ.
            // "в" и "на" в этой таблице отсутствуют — из-за этого ветка их разбора в
            // InferCaseFromTemplate (см. проверку "кладёте"/"бросаете"/... → Acc, иначе Prep)
            // НЕДОСТИЖИМА: TryGetValue не находит предлог, и вывод падежа сразу падает в Nom.
            // Отсюда "Вы видите военачальника на юго-восток" вместо "на юго-востоке".
            //
            // Просто дописать сюда { "в", Prep } и { "на", Prep } НЕЛЬЗЯ: в шаблонах 248
            // вхождений "на {x}"/"в {x}" без явного падежа, и заметная их часть требует
            // винительного — "вы прибываете в {where}", "Вы отправляетесь в {place}",
            // "Вы врезаетесь в {target}", "{target} садится на {seat}", "экипировать {item}
            // на {slot}". Список-исключение в InferCaseFromTemplate покрывает лишь 6 форм
            // ("садитесь" есть, а "садится" уже нет), поэтому включение предлогов заменит
            // один класс ошибок на другой.
            // Чтобы закрыть по-настоящему, нужно сперва расширить AccusativeVerbs до полного
            // набора глаголов движения/помещения, встречающихся в шаблонах, и прогнать
            // дифференциальный тест по всем 248 вхождениям.
            // Пока направления исправлены точечно — явными аннотациями {dir:prep}/{dir:gen}
            // в pattern_dictionary.json (68 шаблонов), явный падеж имеет приоритет над выводом.
        };

        // Переходные глаголы, управляющие винительным падежом прямого объекта.
        // Безопасно: для неодушевлённых Acc = Nom (без изменений), для одушевлённых даёт верную форму.
        // Намеренно НЕ включены: "преграждает"/"убил" (за ними идёт подлежащее в Nom),
        // а также глаголы, управляющие Dat/Ins ("помогаете", "владеете").
        private static readonly HashSet<string> AccusativeVerbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "видите", "берёте", "берете", "берет", "взяли", "получаете", "получили",
            "снимаете", "надеваете", "экипируете", "бросаете", "бросает", "съедаете", "съели",
            "выпили", "подобрали", "подбираете", "собираете", "собрали", "обнаружили", "замечаете",
            "находите", "найдите", "осматриваете", "опознаёте", "прочитали", "использовали",
            "чувствуете", "почувствовали", "слышите", "атакуете", "атаковали", "убили",
            "бьёт", "бьет", "поражает", "притягивает", "блокирует", "отрубаете", "тушите",
            "верните", "восстанавливаете", "перезаряжаете", "назвали", "поглощает",
            "вонзаете", "толкаете", "хватаете", "поднимаете", "роняете", "раните",
            "ударяете", "открываете", "закрываете", "носите"
        };

        // Притяжательные/указательные в творительном падеже: следующий за ними объект тоже в Ins
        // (напр. "своим {weapon}" → "своим мечом"). Только однозначные формы на -им/-ыми/-ими.
        private static readonly HashSet<string> InstrumentalCarriers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "своим", "своими", "вашим", "вашими", "твоим", "твоими",
            "моим", "моими", "нашим", "нашими"
        };

        // Глаголы движения/помещения: после них "в"/"на" требуют винительного падежа
        // («отправляетесь в Джоппу», «кладёте меч в сундук»), а не предложного.
        // Подстроки, а не целые слова: в шаблонах встречаются и «вы прибываете», и «он прибывает».
        private static readonly string[] MotionVerbs = new[]
        {
            "входим", "входите", "войти", "кладёте", "кладете", "положить",
            "бросаете", "бросает", "наступаете", "наступает", "направляется", "направляетесь",
            "садитесь", "садится", "прибываете", "прибывает", "прибыл", "отправляетесь", "отправляется",
            "экипиров", "надева", "надеть", "вешаете", "вешает",
            "отправляйтесь", "врезаетесь", "врезается", "возвращаетесь", "возвращается",
            "переходите", "переходит", "спускаетесь", "спускается", "поднимаетесь", "поднимается",
            "уходите", "уходит", "убираете", "убирает", "помещаете", "помещает", "суёте", "суете",
            // Глаголы движения из подсказок-инструкций («Снова двигайтесь на юг, чтобы войти»).
            // Без них ветка "в"/"на" отдавала предложный падеж: «двигайтесь на юге».
            "двигайтесь", "двигайся", "двигаетесь", "двигается", "идите", "иди", "идёте", "идете",
            "шагайте", "шагаете", "бегите", "бежите", "плывите", "плывёте", "плывете",
            "переместитесь", "перемещаетесь", "перемещается", "нажмите"
        };

        private static MorphCase InferCaseFromTemplate(string template, string placeholderName)
        {
            if (string.IsNullOrEmpty(template) || string.IsNullOrEmpty(placeholderName))
                return MorphCase.Nom;

            string marker = "{" + placeholderName;
            int markerIdx = template.IndexOf(marker);
            if (markerIdx <= 0) return MorphCase.Nom;

            string leftContext = template.Substring(0, markerIdx).TrimEnd();
            if (string.IsNullOrEmpty(leftContext)) return MorphCase.Nom;

            string[] words = leftContext.Split(new[] { ' ', '\t', '<', '>', '/', '=' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return MorphCase.Nom;

            for (int i = words.Length - 1; i >= 0; i--)
            {
                string w = words[i].Trim(new[] { '.', ',', '!', '?', ':', ';', '"', '\'', '[', ']', '{', '}' }).ToLowerInvariant();
                if (string.IsNullOrEmpty(w)) continue;

                if (PrepositionCases.TryGetValue(w, out MorphCase targetCase))
                {
                    if (w == "в" || w == "на")
                    {
                        // "в"/"на" двухпадежные: направление -> Acc («идёте в Джоппу»),
                        // место -> Prep («видите крокодила на западе»). Решает глагол слева.
                        string lowerContext = leftContext.ToLowerInvariant();
                        foreach (string mv in MotionVerbs)
                        {
                            if (lowerContext.Contains(mv)) return MorphCase.Acc;
                        }
                        return MorphCase.Prep;
                    }
                    return targetCase;
                }

                // Не предлог — проверяем глагольное/притяжательное управление.
                // Ближайшее к плейсхолдеру совпадение выигрывает (предлоги уже обработаны выше).
                if (AccusativeVerbs.Contains(w)) return MorphCase.Acc;
                if (InstrumentalCarriers.Contains(w)) return MorphCase.Ins;
            }

            return MorphCase.Nom;
        }

        // Разбор строки падежа из явной аннотации шаблона ({name:gen}).
        // Принимает и морфологические коды (ins), и фракционные (inst) — на всякий случай.
        private static MorphCase ParseCaseName(string s)
        {
            if (string.IsNullOrEmpty(s)) return MorphCase.Nom;
            switch (s.Trim().ToLowerInvariant())
            {
                case "gen": case "genitive": return MorphCase.Gen;
                case "dat": case "dative": return MorphCase.Dat;
                case "acc": case "accusative": return MorphCase.Acc;
                case "ins": case "inst": case "instrumental": return MorphCase.Ins;
                case "prep": case "prepositional": return MorphCase.Prep;
                default: return MorphCase.Nom;
            }
        }

        public static string TranslateFactionCase(string englishFaction, string caseName)
        {
            if (string.IsNullOrEmpty(englishFaction)) return englishFaction;
            string key = englishFaction.Trim();
            // Ключи faction_cases.json не uniform: часть с ведущим "The", часть без.
            // Пробуем: как есть → с "The " → без ведущего "the" → без "the" + "The ".
            if (!factionCases.ContainsKey(key))
            {
                if (factionCases.ContainsKey("The " + key)) key = "The " + key;
                else if (key.StartsWith("the ", StringComparison.OrdinalIgnoreCase))
                {
                    string noThe = key.Substring(4);
                    if (factionCases.ContainsKey(noThe)) key = noThe;
                    else if (factionCases.ContainsKey("The " + noThe)) key = "The " + noThe;
                }
            }
            if (factionCases.TryGetValue(key, out var cases))
            {
                if (cases.TryGetValue(caseName, out string trans))
                {
                    return trans;
                }
                // Падежная аннотация могла прийти в короткой форме ("ins" вместо "inst") — нормализуем.
                if (caseName == "ins" && cases.TryGetValue("inst", out trans))
                {
                    return trans;
                }
            }
            return TranslateInternal(key);
        }

        public static string TranslateText(string text, bool forceWordReplacement = false)

        {

            if (string.IsNullOrEmpty(text)) return text;

            string trimmedText = text.Trim();
            // CRITICAL: DO NOT TOUCH, MODIFY, OR REMOVE THE PROTECTION BELOW!
            // It prevents the game from translating confirmation hold keys (QUIT, ABANDON, RETIRE) to Russian.
            // If they are translated, it breaks the game's confirmation checks (e.g. key-hold to quit/abandon),
            // and the game will NOT register the hold action, falling through to gameplay.
            if (trimmedText == "QUIT" || trimmedText == "ABANDON" || trimmedText == "RETIRE" || trimmedText == "DELETE") return text;

            if (InternalGameKeys.Contains(trimmedText) || IsKeyInBrackets(text)) return text;

            // Делегируем в TranslateMarkup ТОЛЬКО при корректно закрытой разметке.
            // TranslateMarkup обрабатывает "{{" лишь когда дальше по строке есть "}}"; иначе
            // скобки попадают в currentText и хвост функции зовёт TranslateText(тот же текст)
            // — взаимная рекурсия TranslateText <-> TranslateMarkup с неизменным аргументом,
            // depth при этом сбрасывается в 0, поэтому MaxMarkupDepth не спасает. Итог —
            // StackOverflow и молчаливое падение процесса без managed-исключения в логе.
            // Условие ниже повторяет проверку ветки разметки в TranslateMarkup: если оно
            // выполнено, ветка гарантированно сработает и TranslateText получит строго
            // более короткую подстроку.
            int markupStart = text.IndexOf("{{", StringComparison.Ordinal);
            if (markupStart >= 0 && text.IndexOf("}}", markupStart, StringComparison.Ordinal) >= 0)
            {
                // 2026-07-17: ПОРЯДОК ЗДЕСЬ КРИТИЧЕН — сначала строка ЦЕЛИКОМ, дробление последним.
                // TranslateMarkup режет текст по границам {{...}} и переводит куски порознь.
                // Если уйти в него сразу, полный перевод из словаря не найдётся НИКОГДА:
                // "{{y|Your health has dropped below &C40%&R!}}" распадался на "Your", " health
                // has dropped below " и т.д., хотя в словаре лежит готовое
                // "Your health has dropped below 40%!" -> "Ваше здоровье упало ниже 40%".
                // Обрубки не находились, строка падала в пословный перевод и получался грут.
                // Ровно так же терялись "{{K|Weight: 1 lbs.}}" и десятки описаний предметов.
                string wholeExact;
                if (staticDictionary.TryGetValue(text, out wholeExact)) return wholeExact;
                if (staticDictionary.TryGetValue(trimmedText, out wholeExact)) return wholeExact;

                bool wholePatternOk;
                string wholePattern = TryTranslatePattern(text, out wholePatternOk);
                if (wholePatternOk) return wholePattern;

                return TranslateMarkup(text);
            }



            if (text.Contains("serving"))
            {
                // Console.WriteLine($"[DEBUG TranslateText] Contains serving: text='{text}', length={text.Length}");
                // foreach (char c in text) // Console.Write($"{(int)c} ");
                // Console.WriteLine();
            }



            bool success;

            string modernUITranslated = TryTranslateModernUI(text, out success);

            if (success)

            {

                return modernUITranslated;

            }

            string factionTranslated = TryTranslateFactionReputation(text, out success);

            if (success)

            {

                return factionTranslated;

            }



            if (!ContainsEnglish(text)) return text;



            // Защита для одиночных латинских букв (ASCII-тайлы, буквы на стенах, граффити)

            // Предотвращает их ошибочную супер-нормализацию и перевод в скобки (например, G -> [G])

            if (text.Length == 1 && ((text[0] >= 'a' && text[0] <= 'z') || (text[0] >= 'A' && text[0] <= 'Z')))

            {

                return text;

            }



            string cached;

            if (translationCache.TryGetValue(text, out cached))

            {

                return cached;

            }



            string prefix;

            string core;

            string suffix;

            ExtractCoreText(text, out prefix, out core, out suffix);

            if (text == " serving]" || text == "serving]") 
            {
                // Console.WriteLine($"[DEBUG TranslateText] text='{text}'");
                // Console.WriteLine($"  prefix='{prefix}', core='{core}', suffix='{suffix}'");
            }



            if (string.IsNullOrEmpty(core))

            {

                translationCache[text] = text;

                return text;

            }



            string normalizedCore = core.Replace("\r\n", "\n")
                                        .Replace('\u00A0', ' ')
                                        .Replace('\u2007', ' ')
                                        .Replace('\u200B', ' ')
                                        .Replace('\u202F', ' ');



            string trimmedCore = normalizedCore.Trim();
            // Case-sensitive check for uppercase confirmation keys only.
            // This prevents translating QUIT / ABANDON / RETIRE while allowing
            // title-case (Abandon / Quit) and lowercase (abandon / quit) in menus to be translated.
            if (trimmedCore == "QUIT" || trimmedCore == "ABANDON" || trimmedCore == "RETIRE" || trimmedCore == "ABANDONED" ||
                trimmedCore == "Q U I T" || trimmedCore == "A B A N D O N" || trimmedCore == "R E T I R E" || trimmedCore == "A B A N D O N E D")
            {
                translationCache[text] = text;
                return text;
            }

            string translatedCore = "";



            string exactMatch;

            if (staticDictionary.TryGetValue(trimmedCore, out exactMatch))

            {

                translatedCore = exactMatch;

            }

            else
            {
                // Попытка найти нормализованный ключ для ядра текста
                string sn = SuperNormalize(trimmedCore);
                
                bool isKeyName = sn.Length == 1 || KeyNameSet.Contains(sn);
                bool hasBrackets = trimmedCore.StartsWith("[") && trimmedCore.EndsWith("]");
                
                if (!isKeyName || hasBrackets)
                {
                    string originalKey;
                    if (normalizedKeyDictionary.TryGetValue(sn, out originalKey))
                    {
                        if (staticDictionary.TryGetValue(originalKey, out exactMatch))
                        {
                            translatedCore = RestoreStrippedPunctuation(trimmedCore, originalKey, exactMatch);
                        }
                    }
                }
            }



            
            if (string.IsNullOrEmpty(translatedCore))
            {
                if (IsEnglishProse(normalizedCore))
                {
                    // Развёрнутая проза, которой нет в словаре целиком. Пословный перевод здесь
                    // не достигает НИ ОДНОЙ цели, поэтому оставляем английский как есть — см.
                    // комментарий к IsEnglishProse.
                    //
                    // 2026-07-22: ИСКЛЮЧЕНИЕ — многоабзацные попапы обучения. Игра шлёт их одной
                    // строкой вида "Абзац1\n\nPress X": целого композита в словаре нет, а маркер
                    // прозы (can/have/will/be) заставлял вернуть ВЕСЬ текст английским, хотя каждый
                    // абзац по отдельности в словаре ЕСТЬ ("Ascend." -> "Подняться." и т.п.).
                    // Поэтому для МНОГОСТРОЧНОЙ прозы сначала пробуем построчный перевод точным
                    // совпадением (тот же приём и с той же семантикой, что и в не-прозовой ветке
                    // ниже). Если НИ ОДНА строка не изменилась — оставляем английский ровно как
                    // раньше, поэтому для «настоящей» однострочной прозы регрессий нет.
                    if (normalizedCore.IndexOf('\n') >= 0)
                    {
                        string[] proseNlParts = normalizedCore.Split('\n');
                        List<string> translatedProseNl = new List<string>(proseNlParts.Length);
                        bool anyProseNlChanged = false;
                        foreach (var pnp in proseNlParts)
                        {
                            string tpnp = TranslateText(pnp);
                            translatedProseNl.Add(tpnp);
                            if (tpnp != pnp) anyProseNlChanged = true;
                        }
                        translatedCore = anyProseNlChanged ? string.Join("\n", translatedProseNl) : normalizedCore;
                    }
                    else
                    {
                        translatedCore = normalizedCore;
                    }
                }
                else
                {
                    // Защита от "Франкенштейнов": не пытаемся переводить пословно длинные предложения,
                    // так как это портит грамматику и делает текст нечитаемым.
                    // Пословный перевод разрешен только для коротких фраз (до 3 слов).
                    int wordCount = trimmedCore.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
                    bool endsWithSentencePunct = trimmedCore.EndsWith(".") || trimmedCore.EndsWith("!") || trimmedCore.EndsWith("?");
                    int maxWords = endsWithSentencePunct ? 3 : 5;
                    
                    if ((wordCount <= maxWords || forceWordReplacement) && disableWordReplacementCounter == 0)
                    {
                        translatedCore = TryWordReplacement(normalizedCore);
                        if (translatedCore != normalizedCore)
                        {
                            LogWordReplacement(normalizedCore, translatedCore);
                        }
                    }
                    else
                    {
                        // Многострочные блоки (например, содержимое {{rules|...}}): переводим построчно.
                        // Это покрывает блоки характеристик предметов вида
                        // "Strength Bonus Cap: 2\nWeapon Class: Cudgel (...)", которые целиком в словаре
                        // не найти, но каждая строка — отдельная словарная запись.
                        if (normalizedCore.Contains("\n"))
                        {
                            string[] nlParts = normalizedCore.Split('\n');
                            List<string> translatedNl = new List<string>();
                            bool anyNlChanged = false;
                            foreach (var np in nlParts)
                            {
                                string tnp = TranslateText(np);
                                translatedNl.Add(tnp);
                                if (tnp != np)
                                {
                                    anyNlChanged = true;
                                }
                            }
                            translatedCore = anyNlChanged ? string.Join("\n", translatedNl) : normalizedCore;
                        }
                        else if (trimmedCore.Contains(", "))
                        {
                            string[] parts = trimmedCore.Split(new[] { ", " }, StringSplitOptions.None);
                            List<string> translatedParts = new List<string>();
                            bool anyChanged = false;
                            foreach (var part in parts)
                            {
                                string tp = TranslateText(part);
                                translatedParts.Add(tp);
                                if (tp != part)
                                {
                                    anyChanged = true;
                                }
                            }
                            if (anyChanged)
                            {
                                translatedCore = string.Join(", ", translatedParts);
                            }
                            else
                            {
                                translatedCore = normalizedCore;
                            }
                        }
                        else
                        {
                            translatedCore = TryWordReplacement(normalizedCore);
                            if (translatedCore != normalizedCore)
                            {
                                LogWordReplacement(normalizedCore, translatedCore);
                            }
                        }
                    }
                }
            }
            



            if (!string.IsNullOrEmpty(suffix) && !string.IsNullOrEmpty(translatedCore))

            {

                char lastCoreChar = translatedCore[translatedCore.Length - 1];

                if (lastCoreChar == '?' || lastCoreChar == '!' || lastCoreChar == '.')

                {

                    int sIdx = 0;

                    while (sIdx < suffix.Length && suffix[sIdx] == lastCoreChar)

                    {

                        sIdx++;

                    }

                    if (sIdx > 0)

                    {

                        suffix = suffix.Substring(sIdx);

                    }

                }

            }



            // Restore capital letter case if needed
            if (translatedCore.Length > 0 && char.IsUpper(trimmedCore[0]) && char.IsLower(translatedCore[0]))
            {
                translatedCore = char.ToUpper(translatedCore[0]) + translatedCore.Substring(1);
            }

            string result = prefix + translatedCore + suffix;

            if (text == " serving]" || text == "serving]")
            {
                // Console.WriteLine($"  translatedCore before return: '{translatedCore}'");
                // Console.WriteLine($"  TranslateText returning result: '{result}'");
            }

            translationCache[text] = result;

            return result;

        }



        // 2026-07-06 (v25): вырезает цветокоды классического UI Qud "&X" (& + буква) из строки.
        // Escaped "&&" (литеральный амперсанд) и "& " (амперсанд-пробел) НЕ трогаем.
        // XML-цветоразметка Qud: "<color=#RRGGBBAA>" / "</color>". Используется для построения
        // кандидата без разметки при матчинге паттернов (см. TryTranslatePatternBody).
        private static readonly System.Text.RegularExpressions.Regex ColorTagRegex =
            new System.Text.RegularExpressions.Regex(@"</?color(?:=[^>]*)?>",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private static string StripAmpColorCodes(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('&') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '&' && i + 1 < s.Length && char.IsLetter(s[i + 1])) { i++; continue; }
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        // ===== ШАБЛОНЫ ИГРЫ (prefix-путь) =====
        // Словарь ШАБЛОНОВ: ключ — оригинальный шаблон с =переменными= игры ДО подстановки.
        // Отдельный от staticDictionary, потому что подменять шаблон в Message можно только
        // после проверки безопасности (см. BuildTemplateDictionary) — это влияет на поведение
        // игры, а не только на отображение.
        public static readonly ConcurrentDictionary<string, string> templateDictionary =
            new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        private static readonly System.Text.RegularExpressions.Regex GameVarRegex =
            new System.Text.RegularExpressions.Regex(@"=([a-zA-Zа-яА-Я][^=]*)=",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        // Имена переменных (до первого ':'), отсортированные. Именно ИМЯ решает, что подставит
        // игра; содержимое после ':' — литералы, которые переводить МОЖНО и НУЖНО
        // (=ifplayerplural:ye:thee= -> =ifplayerplural:вам:тебе=), поэтому сравниваем только имена.
        private static List<string> GameVarHeads(string s)
        {
            var list = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in GameVarRegex.Matches(s))
            {
                string inner = m.Groups[1].Value;
                int c = inner.IndexOf(':');
                list.Add(c >= 0 ? inner.Substring(0, c) : inner);
            }
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        // Собираем карту шаблонов из общего словаря ОДИН раз при инициализации.
        // Берём запись, только если набор имён переменных в переводе ТОЧНО совпадает с оригиналом.
        // Иначе игра подставит значение не туда (или не подставит вовсе) — а это уже поломка
        // логики, а не косметика. Замер по текущему словарю: годны ~10730 из ~11159 (96%).
        private static void BuildTemplateDictionary()
        {
            try
            {
                templateDictionary.Clear();
                int skipped = 0;
                foreach (var kv in staticDictionary)
                {
                    string key = kv.Key, val = kv.Value;
                    if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(val)) continue;
                    if (key.IndexOf('=') < 0) continue;
                    var kh = GameVarHeads(key);
                    if (kh.Count == 0) continue;
                    var vh = GameVarHeads(val);
                    bool same = kh.Count == vh.Count;
                    if (same)
                    {
                        for (int i = 0; i < kh.Count; i++)
                        {
                            if (!string.Equals(kh[i], vh[i], StringComparison.Ordinal)) { same = false; break; }
                        }
                    }
                    if (!same) { skipped++; continue; }
                    templateDictionary[key] = val;
                }
                LogInfo("[RussianLocalization] Templates ready: " + templateDictionary.Count +
                        " (skipped " + skipped + " — набор переменных в переводе не совпал с оригиналом).");
            }
            catch (Exception ex)
            {
                LogError("[RussianLocalization] BuildTemplateDictionary failed: " + ex.Message);
            }
        }

        // Формы фунтов, встречающиеся в словарях: "фнт."×303, "фунтов"×231, "фунт"×63,
        // "фунта"×36, "фн."×4, плюс голое "фнт" без точки (из "lb" -> "фнт" в словарях).
        // Порядок альтернатив важен: длинные формы первыми, чтобы "фунтов" не съедалось
        // как "фунт". Точку после "фнт"/"фн" забираем вместе с сокращением.
        private static readonly System.Text.RegularExpressions.Regex PoundUnitsRegex =
            new System.Text.RegularExpressions.Regex(
                @"фнт\.?|фн\.|фунтов|фунта|фунт",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        // Заменяет подпись единиц веса "фнт."/"фунтов"/"фунт"/"фунта"/"фн." на "кг"
        // прямо в готовой строке. Числа НЕ пересчитываются (решение пользователя от
        // 2026-07-21): значения остаются игровыми, меняется только текст единицы —
        // поэтому дробных чисел вида "29,5 кг" больше не появляется.
        private static string ConvertPoundsToKilograms(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            // Быстрый выход: подавляющее большинство строк веса не содержат вовсе.
            if (text.IndexOf("фнт", StringComparison.Ordinal) < 0 &&
                text.IndexOf("фунт", StringComparison.Ordinal) < 0 &&
                text.IndexOf("фн.", StringComparison.Ordinal) < 0) return text;

            return PoundUnitsRegex.Replace(text, "кг");
        }

        // Английские связки и вспомогательные глаголы — надёжный признак развёрнутой прозы.
        // Их наличие означает, что у строки есть синтаксис, который пословная подстановка
        // гарантированно разрушит.
        private static readonly HashSet<string> EnglishProseMarkers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "is", "are", "was", "were", "be", "been", "being", "am",
            "has", "have", "had", "will", "would", "shall", "should",
            "can", "could", "may", "might", "must", "does", "do", "did"
        };

        // 2026-07-17: ПРИЧИНА «Я ЕСТЬ ГРУТ». Замер по word_replacements.txt (1181 замена):
        // этот признак отделяет прозу от строк вида «метка: данные», и решает вопрос
        // «пословно или не трогать» по данным, а не на глаз:
        //   • 39 уникальных предложений с этим признаком -> пословный перевод даёт мусор
        //     («pig farmer slouches in the heat. A beaten, leather hat is pulled low...» ->
        //      «свиновод сутулится в жар. A beaten, кожаный шляпа is pulled низкий»).
        //     При этом он НЕ достигает ни одной цели: грамматику ломает целиком, а английский
        //     всё равно остаётся в 62% таких строк (убирает лишь 83% английских слов).
        //     Плюс 5 коротких строк того же класса («has restocked her inventory» ->
        //     «имеет пополнен её инвентарь») — поэтому проверка стоит ДО лимита по словам.
        //   • 218 строк без этого признака -> пословный перевод ПОЛЕЗЕН и их трогать нельзя
        //     («{{C|Last saved:}} Sunday, 12 July 2026» -> «Последнее сохранение: Воскресенье,
        //      12 июля 2026», «hulking baboon slips on the gel» -> «громадный бабуин скользит
        //      на гель»). Именно поэтому глушим не «длинные строки», а именно прозу.
        // Для прозы возвращаем английский как есть: читаемый оригинал лучше нечитаемой каши.
        // Это ВРЕМЕННАЯ сетка безопасности — настоящее лечение только одно: перевод фразы
        // целиком в dictionary.json, тогда до пословного прохода дело вообще не дойдёт.
        private static bool IsEnglishProse(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            int i = 0, len = text.Length;
            while (i < len)
            {
                while (i < len && !char.IsLetter(text[i])) i++;
                int start = i;
                while (i < len && char.IsLetter(text[i])) i++;
                if (i > start && i - start <= 6)
                {
                    if (EnglishProseMarkers.Contains(text.Substring(start, i - start))) return true;
                }
            }
            return false;
        }

        private static string TryWordReplacement(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            string[] words = text.Split(' ');
            if (words.Length == 0) return text;

            var sb = new StringBuilder(text.Length * 2);
            int wordIdx = 0;

            while (wordIdx < words.Length)
            {
                // Защита игровой разметки: слово, начинающееся с "{{" (например "{{rules|"),
                // никогда не переводим — иначе служебный токен ("rules" -> "правила")
                // ломает разметку, которую парсит игра.
                if (words[wordIdx].StartsWith("{{"))
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(words[wordIdx]);
                    wordIdx++;
                    continue;
                }
                // 2026-07-22: Защита плейсхолдеров подстановки движка. Символы $ * = срезаются как
                // краевая пунктуация, поэтому "$focus" даёт ядро "focus"->"фокус"->сборка "$фокус",
                // а "*CultSymbol*"->"*КультСимвол*", "=name="->"=имя=" — рантайм-подстановка ломается.
                // Такие токены (начинается с '$'; касается '*' на краю; обёрнут в '=...=') НИКОГДА
                // не переводим — оставляем как есть. Живой текст такими маркерами не начинается.
                {
                    string wtok = words[wordIdx];
                    int wlen = wtok.Length;
                    if (wlen > 0 &&
                        (wtok[0] == '$' ||
                         wtok[0] == '*' || wtok[wlen - 1] == '*' ||
                         (wlen > 1 && (wtok[0] == '=' || wtok[wlen - 1] == '='))))
                    {
                        if (sb.Length > 0) sb.Append(' ');
                        sb.Append(wtok);
                        wordIdx++;
                        continue;
                    }
                }
                string bestMatch = null;
                int bestLen = 0;

                for (int seqLen = 3; seqLen >= 1; seqLen--)
                {
                    if (wordIdx + seqLen > words.Length) continue;

                    string candidate = string.Join(" ", words, wordIdx, seqLen);
                    // 2026-07-06 (v25): цветокоды классического UI "&X" (& + буква) раньше прилипали
                    // к слову — "&Ysteel&y" давало core="Ysteel&y" (буква цвета 'Y' не срезалась как
                    // пунктуация) → нет в словаре → материал/слово не переводились (это ~73% франкенов
                    // в боевом логе и названиях предметов). Теперь на краях потребляем "&X" как
                    // пунктуацию, а внутренние коды вырезаем из ключа словаря (StripAmpColorCodes).
                    int start = 0;
                    while (start < candidate.Length)
                    {
                        char sc = candidate[start];
                        if (sc == '&' && start + 1 < candidate.Length && candidate[start + 1] == '&') { start += 2; continue; } // экранированный &&
                        if (sc == '&' && start + 1 < candidate.Length && char.IsLetter(candidate[start + 1])) { start += 2; continue; } // цветокод &X
                        if (!char.IsLetterOrDigit(sc)) { start++; continue; }
                        break;
                    }
                    int end = candidate.Length;
                    while (end > start)
                    {
                        char ec = candidate[end - 1];
                        if (end - 2 >= start && candidate[end - 2] == '&' && candidate[end - 1] == '&') { end -= 2; continue; } // экранированный &&
                        if (end - 2 >= start && candidate[end - 2] == '&' && char.IsLetter(ec)) { end -= 2; continue; } // цветокод &X
                        if (!char.IsLetterOrDigit(ec)) { end--; continue; }
                        break;
                    }

                    if (start >= end) continue; // Only punctuation/symbols, skip core lookup

                    string leadingPunct = candidate.Substring(0, start);
                    string trailingPunct = candidate.Substring(end);
                    string core = candidate.Substring(start, end - start);
                    // Ключ поиска — без внутренних "&X" кодов (например "steel&y long" → "steel long").
                    string lookupCore = core.IndexOf('&') >= 0 ? StripAmpColorCodes(core) : core;

                    string translation = null;
                    if (wordDictionary.TryGetValue(lookupCore, out translation) ||
                        wordDictionary.TryGetValue(lookupCore.ToLower(), out translation))
                    {
                        if (translation != null)
                        {
                            // Match case of the core (по очищенному от цветокодов ключу)
                            bool isAllLower = true, isAllUpper = true;
                            for (int c = 0; c < lookupCore.Length; c++)
                            {
                                if (char.IsUpper(lookupCore[c])) isAllLower = false;
                                if (char.IsLower(lookupCore[c])) isAllUpper = false;
                            }

                            string finalCoreTrans = translation;
                            if (isAllLower) finalCoreTrans = finalCoreTrans.ToLower();
                            else if (isAllUpper) finalCoreTrans = finalCoreTrans.ToUpper();
                            else if (finalCoreTrans.Length > 0 && lookupCore.Length > 0 && char.IsUpper(lookupCore[0]))
                                finalCoreTrans = char.ToUpper(finalCoreTrans[0]) + finalCoreTrans.Substring(1);

                            bestMatch = leadingPunct + finalCoreTrans + trailingPunct;
                            bestLen = seqLen;
                            break;
                        }
                    }
                }

                if (bestMatch != null)
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(bestMatch);
                    wordIdx += bestLen;
                }
                else
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(words[wordIdx]);
                    wordIdx++;
                }
            }

            string result = sb.ToString();
            try
            {
                result = MorphologyService.Decline(result, MorphCase.Nom);
            }
            catch (Exception ex)
            {
                LogError("[RussianLocalization] Word replacement declension failed: " + ex.Message);
            }
            return result;
        }



        private static bool ContainsCyrillic(string text)
        {
            bool hasCyrillic, hasEnglish;
            ScanAlpha(text, out hasCyrillic, out hasEnglish);
            return hasCyrillic;
        }

        /// <summary>
        /// Internal-доступ к ContainsCyrillic для других классов внутри сборки (FontUtils).
        /// </summary>
        internal static bool ContainsCyrillicInternal(string text) => ContainsCyrillic(text);

        /// <summary>
        /// Однопроходное определение наличия кириллических и латинских букв в строке.
        /// Заменяет два вызова ContainsCyrillic + ContainsEnglish в горячих путях.
        /// Пропускает XML-теги цвета и фигурные скобки Qud разметки, чтобы не ломать
        /// определение языка для переведенного текста.
        /// </summary>
        private static void ScanAlpha(string text, out bool hasCyrillic, out bool hasEnglish)
        {
            hasCyrillic = false;
            hasEnglish = false;
            if (string.IsNullOrEmpty(text)) return;

            int i = 0;
            int len = text.Length;
            while (i < len)
            {
                // Skip XML-like tags <color=...> or </color>
                if (text[i] == '<')
                {
                    int closeIdx = text.IndexOf('>', i);
                    if (closeIdx > i)
                    {
                        i = closeIdx + 1;
                        continue;
                    }
                }
                // Skip Qud markup tag prefix {{tag|
                if (i < len - 1 && text[i] == '{' && text[i + 1] == '{')
                {
                    int pipeIdx = text.IndexOf('|', i + 2);
                    int closeBraceIdx = text.IndexOf("}}", i + 2);
                    if (pipeIdx > i && (closeBraceIdx == -1 || pipeIdx < closeBraceIdx))
                    {
                        i = pipeIdx + 1;
                        continue;
                    }
                    else if (closeBraceIdx > i)
                    {
                        i = closeBraceIdx + 2;
                        continue;
                    }
                }
                // Skip closing }}
                if (i < len - 1 && text[i] == '}' && text[i + 1] == '}')
                {
                    i += 2;
                    continue;
                }

                char c = text[i];
                if (!hasCyrillic && ((c >= '\u0430' && c <= '\u044f') || (c >= '\u0410' && c <= '\u042f') || c == '\u0451' || c == '\u0401'))
                {
                    hasCyrillic = true;
                }
                else if (!hasEnglish && ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')))
                {
                    hasEnglish = true;
                }
                if (hasCyrillic && hasEnglish) return;
                i++;
            }
        }

        private static bool ContainsEnglish(string text)
        {
            bool hasCyrillic, hasEnglish;
            ScanAlpha(text, out hasCyrillic, out hasEnglish);
            return hasEnglish;
        }

        public static string RestoreStrippedPunctuation(string original, string key, string translation)
        {
            if (string.IsNullOrEmpty(original) || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(translation))
                return translation;

            int origStart = 0;
            int keyStart = 0;
            while (origStart < original.Length && (char.IsPunctuation(original[origStart]) || char.IsWhiteSpace(original[origStart])))
            {
                if (keyStart < key.Length && original[origStart] == key[keyStart])
                {
                    keyStart++;
                }
                origStart++;
            }
            string leadPunct = original.Substring(0, origStart - keyStart);

            int origEnd = original.Length - 1;
            int keyEnd = key.Length - 1;
            while (origEnd >= 0 && (char.IsPunctuation(original[origEnd]) || char.IsWhiteSpace(original[origEnd])))
            {
                if (keyEnd >= 0 && original[origEnd] == key[keyEnd])
                {
                    keyEnd--;
                }
                origEnd--;
            }
            string trailPunct = original.Substring(origEnd + 1 + (key.Length - 1 - keyEnd));

            return leadPunct + translation + trailPunct;
        }

        public static string SuperNormalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            StringBuilder sb = new StringBuilder(text.Length);
            bool lastWasSpace = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (char.IsWhiteSpace(c))
                {
                    if (!lastWasSpace) { sb.Append(' '); lastWasSpace = true; }
                }
                else if (!char.IsPunctuation(c) || c == '{' || c == '}' || c == '[' || c == ']' || c == '(' || c == ')' || c == '|' || c == '<' || c == '>')
                {
                    sb.Append(char.ToLowerInvariant(c));
                    lastWasSpace = false;
                }
            }

            return sb.ToString().Trim();
        }



        // 2026-07-23 (Guard C): журнал подозрительных записей словаря. Пишем ТОЛЬКО в
        // Documents\CavesOfQud_RU_Logs — в папке мода лишних файлов быть не должно.
        private static void LogSuspiciousDictionaryEntry(string key, string value, string reason)
        {
            try
            {
                string docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (string.IsNullOrEmpty(docsPath)) return;
                string targetFolder = Path.Combine(docsPath, "CavesOfQud_RU_Logs");
                if (!Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);
                string oneLineKey = (key ?? "").Replace("\r", " ").Replace("\n", "\\n");
                string oneLineVal = (value ?? "").Replace("\r", " ").Replace("\n", "\\n");
                File.AppendAllText(Path.Combine(targetFolder, "dict_suspicious.txt"),
                    "[" + reason + "] KEY(" + oneLineKey.Length + "): " + oneLineKey + Environment.NewLine +
                    "    VAL(" + oneLineVal.Length + "): " + oneLineVal + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch {}
        }

        private static void AppendToLogFile(string filename, string content)

        {

            // Не пишем all_gameplay_texts.txt в папку мода — только в Documents
            if (filename != "all_gameplay_texts.txt" && !string.IsNullOrEmpty(CachedModPath))

            {

                try

                {

                    string logPath = Path.Combine(CachedModPath, filename);

                    File.AppendAllText(logPath, content, Encoding.UTF8);

                }

                catch (Exception ex)

                {

                    LogError("[RussianLocalization] Failed to write log " + filename + ": " + ex.Message);

                }

            }



            // Пишем в Документы пользователя ТОЛЬКО all_gameplay_texts.txt с датой в имени

            if (filename != "all_gameplay_texts.txt") return;



            try

            {

                string docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                if (!string.IsNullOrEmpty(docsPath))

                {

                    string targetFolder = Path.Combine(docsPath, "CavesOfQud_RU_Logs");

                    if (!Directory.Exists(targetFolder))

                    {

                        Directory.CreateDirectory(targetFolder);

                    }

                    
                    // Используем имя файла с датой
                    string logFileName = !string.IsNullOrEmpty(GameplayLogFileName) ? GameplayLogFileName : filename;
                    string logPath = Path.Combine(targetFolder, logFileName);

                    File.AppendAllText(logPath, content, Encoding.UTF8);

                }

            }

            catch (Exception ex)
            {
                LogError("[RussianLocalization] Failed to write to MyDocuments log: " + ex.Message + "\n" + ex.StackTrace);
            }

        }



        private static void LogUntranslated(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text)) return;

                string trimmed = text.Trim();

                if (trimmed.Length < 3) return;

                if (IsJunkText(trimmed)) return;

                if (trimmed.StartsWith("<") && trimmed.EndsWith(">")) return;

                if (trimmed.StartsWith("{{") && trimmed.EndsWith("}}")) return;

                // ConcurrentDictionary.TryAdd атомарен и не требует lock.
                if (loggedStrings.TryAdd(trimmed, 0))
                {
                    AppendToLogFile("untranslated.txt", trimmed + Environment.NewLine);
                }
            }
            catch {}
        }



        private static void LogWordReplacement(string original, string replaced)
        {
            try
            {
                if (string.IsNullOrEmpty(original) || original == replaced) return;

                // ConcurrentDictionary.TryAdd атомарен и не требует lock.
                if (loggedReplacements.TryAdd(original, 0))
                {
                    string logEntry = "[Original]: " + original + Environment.NewLine +
                                      "[Replaced]: " + replaced + Environment.NewLine +
                                      "--------------------------------------------------" + Environment.NewLine;
                    AppendToLogFile("word_replacements.txt", logEntry);
                }
            }
            catch {}
        }



        private static void LogAllGameplayText(string original, string translated)
        {
            try
            {
                if (IsJunkText(original)) return;
                string trimmed = original.Trim();

                // Потокобезопасная проверка и добавление без lock благодаря ConcurrentDictionary.
                if (loggedAllTexts.TryAdd(trimmed, 0))
                {
                    string logOriginal = original.Replace("\r\n", "\n").Trim();
                    string logTranslated = translated.Replace("\r\n", "\n").Trim();
                    string fullEntry = "---" + Environment.NewLine +
                                       "[RAW]: " + logOriginal + Environment.NewLine +
                                       "[RES]: " + logTranslated + Environment.NewLine;
                    AppendToLogFile("all_gameplay_texts.txt", fullEntry);
                }
            }
            catch {}
        }



        // Мемоизация IsJunkText для повторяющихся коротких строк (UUID, hotkey, MB-размеры и т.п.).
        // Ограничиваем размер кэша, чтобы не рос бесконтрольно.
        private const int JunkTextCacheMaxEntries = 4096;
        private static readonly ConcurrentDictionary<string, bool> junkTextCache = new ConcurrentDictionary<string, bool>();
        private static int junkTextCacheOverflow;

        private static bool IsJunkText(string text)
        {
            if (string.IsNullOrEmpty(text)) return true;
            string trimmed = text.Trim();
            if (trimmed.Length <= 1) return true;

            // Быстрый путь: для коротких строк (<=64) ищем в кэше.
            if (trimmed.Length <= 64)
            {
                bool cached;
                if (junkTextCache.TryGetValue(trimmed, out cached)) return cached;
                bool result = IsJunkTextCore(trimmed);
                // Кэшируем, но только пока не превышен лимит.
                if (System.Threading.Interlocked.CompareExchange(ref junkTextCacheOverflow, 0, 0) == 0)
                {
                    if (junkTextCache.Count < JunkTextCacheMaxEntries)
                    {
                        junkTextCache[trimmed] = result;
                    }
                    else
                    {
                        System.Threading.Interlocked.Exchange(ref junkTextCacheOverflow, 1);
                    }
                }
                return result;
            }
            return IsJunkTextCore(trimmed);
        }

        private static bool IsJunkTextCore(string trimmed)
        {
            if (trimmed == "WoundLevel" ||
                trimmed == "WoundLevel2" ||
                trimmed == "DisplayName" ||
                trimmed == "ConText" ||
                trimmed == "LongDescription" ||
                trimmed == "Esc]" ||
                trimmed == "PgDown" ||
                trimmed == "PgUp" ||
                trimmed == "Copyright" ||
                trimmed == "Freehold Games (perevod: memasevich")
                return true;

            if (trimmed.StartsWith("Location: ")) return true;
            if (trimmed.StartsWith("<...>/")) return true;
            if (trimmed.StartsWith("/Mods/")) return true;
            if (trimmed.EndsWith(" MB")) return true;
            if (trimmed == "DevAssets" || trimmed == "_DevAssets") return true;

            // Технические сообщения загрузки ресурсов игры — не переводим, не логируем как непереведённые.
            if (trimmed.StartsWith("Loading ")) return true;

            // Размеры экрана вида "640x480" / "1920x1080" — числовые метки, не переводим.
            if (IsScreenResolution(trimmed)) return true;

            // Одиночные ANSI-цветовые маркеры CoQ (&y, &y^b, &W, &K, &w, &yX, &WX и т.п.) — не переводим.
            if (IsAnsiColorMarker(trimmed)) return true;

            if (IsUUID(trimmed)) return true;
            if (IsDiceNotation(trimmed)) return true;
            if (IsHotkeyOnly(trimmed)) return true;

            bool hasLetters = false;

            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (char.IsLetter(c))
                {
                    hasLetters = true;
                    break;
                }
            }

            if (!hasLetters) return true;

            if (trimmed.StartsWith("<") && trimmed.EndsWith(">") && !trimmed.Contains("</color>")) return true;

            return false;
        }

        private static bool IsUUID(string text)
        {
            if (text.Length != 36) return false;
            int dashCount = 0;
            foreach (char c in text)
            {
                if (c == '-') dashCount++;
                else if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
            }
            return dashCount == 4;
        }

        private static bool IsDiceNotation(string text)
        {
            if (text.Length > 20) return false;
            return System.Text.RegularExpressions.Regex.IsMatch(text, @"^[+-]?\d+d\d+([+-]\d+)?$");
        }

        private static bool IsHotkeyOnly(string text)
        {
            string t = text.Trim();
            if (t.Length == 1 && ((t[0] >= 'a' && t[0] <= 'z') || (t[0] >= 'A' && t[0] <= 'Z'))) return true;
            if (t.StartsWith("[") && t.EndsWith("]") && t.Length <= 8) return true;
            return false;
        }

        // Регулярка для распознавания ANSI-цветовых маркеров Caves of Qud (&y, &y^b, &W, &K, &w, &wX, &WX и т.п.).
        // Это управляющие последовательности терминала, не осмысленный текст.
        private static readonly System.Text.RegularExpressions.Regex AnsiColorRegex =
            new System.Text.RegularExpressions.Regex(@"^&[a-zA-Z\^]+[a-zA-Z]?$", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static bool IsAnsiColorMarker(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (!text.StartsWith("&")) return false;
            return AnsiColorRegex.IsMatch(text);
        }

        // Разрешение экрана вида "640x480", "1920x1080", "2560x1600".
        private static readonly System.Text.RegularExpressions.Regex ScreenResolutionRegex =
            new System.Text.RegularExpressions.Regex(@"^\d{3,4}x\d{3,4}$", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static bool IsScreenResolution(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return ScreenResolutionRegex.IsMatch(text);
        }

        // Whitelist игровых сокращений Caves of Qud, которые должны переводиться
        // (атрибуты, статы, направления, форматы).
        // Защитный фильтр в staticDictionary отбрасывает 1-2 буквенные ключи,
        // но эти сокращения — исключения, они НЕ ломают другие слова.
        private static readonly HashSet<string> GameAbbreviations = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Атрибуты
            "HP", "MP", "SP", "AP", "AV", "DV", "XP", "MA", "LV", "QN", "UR", "UI", "MS",
            // Направления
            "NE", "NW", "SE", "SW", "NO", "SO", "EA", "WE",
            // Управление / устройства
            "PS", "OK", "CR", "ER", "HR", "Eq", "PC", "VR", "AR",
            // Игровые / мод-специфичные
            "Ud", "Ut", "Ux", "No", "on",
        };

        private static bool IsGameAbbreviation(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            return GameAbbreviations.Contains(key);
        }

        // Регулярки для финальной очистки цветовых блоков (постпроцессинг).
        // Применяются ДО записи в translationCache и в LogAllGameplayText,
        // чтобы лог и кэш не содержали визуального мусора.
        // Не затрагивают текст внутри блоков, только удаляют пустые и висящие теги.
        private static readonly System.Text.RegularExpressions.Regex EmptyColorBlockRegex =
            new System.Text.RegularExpressions.Regex(@"<color=[^>]+>[ \t]*</color>", System.Text.RegularExpressions.RegexOptions.Compiled);

        // Битый закрывающий тег с атрибутом: </color="green"> — присылает сама игра.
        private static readonly System.Text.RegularExpressions.Regex MalformedCloseColorRegex =
            new System.Text.RegularExpressions.Regex(@"</color[ \t]*=[^>]*>",
                System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        // Подряд идущие одинаковые открывающие теги: <color=X><color=X> -> <color=X>
        // (?![ \t]*</color>) — не схлопываем, если после тега идёт закрывающий (это валидный пустой блок).
        private static readonly System.Text.RegularExpressions.Regex DoubleOpenColorRegex =
            new System.Text.RegularExpressions.Regex(@"<color=([^>]+)>(?![ \t]*</color>)(<color=\1>(?![ \t]*</color>))+", System.Text.RegularExpressions.RegexOptions.Compiled);
        // Подряд идущие закрывающие теги: </color></color> -> </color>
        private static readonly System.Text.RegularExpressions.Regex DoubleCloseColorRegex =
            new System.Text.RegularExpressions.Regex(@"(?:</color>[ \t]*){2,}", System.Text.RegularExpressions.RegexOptions.Compiled);
        // Висящий открывающий тег в самом конце строки: <color=X> (без </color> в конце).
        // Это безопасно удалять: TMP_Text всё равно отбросит непарный тег, а лог станет чище.
        private static readonly System.Text.RegularExpressions.Regex TrailingOpenColorRegex =
            new System.Text.RegularExpressions.Regex(@"<color=[^>]+>[ \t]*$", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// Удаляет пустые цветовые блоки <color=...></color> и схлопывает подряд
        /// идущие одинаковые открывающие/закрывающие теги.
        /// Безопасно для UI: не трогает блоки с непустым содержимым и не меняет цвета.
        /// </summary>
        internal static string StripEmptyColorBlocks(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (text.IndexOf("<color=", StringComparison.OrdinalIgnoreCase) < 0 &&
                text.IndexOf("</color>", StringComparison.OrdinalIgnoreCase) < 0) return text;
            // 0. Чиним битый закрывающий тег самой игры: "</color=\"green\">". Такого тега не
            //    существует — TextMeshPro печатает его БУКВАЛЬНО. Он встречается в экране
            //    создания персонажа ("Skill Points: </color=\"green\">2</color>"): строка
            //    переводилась ("Очки навыков"), а мусорный тег доезжал до игрока как есть.
            //    Приводим к нормальному "</color>", дальше лишнюю пару снимет шаг 5.
            text = MalformedCloseColorRegex.Replace(text, "</color>");
            // 1. Удаляем блоки только с пробелами <color=X>   </color>
            text = EmptyColorBlockRegex.Replace(text, "");
            // 2. Схлопываем 2+ подряд идущих одинаковых открывающих тегов.
            //    НЕ схлопываем разные цвета, чтобы не сломать валидные переходы.
            //    НЕ схлопываем если после тега идёт </color> (валидный пустой блок).
            text = DoubleOpenColorRegex.Replace(text, "<color=$1>");
            // 3. Схлопываем подряд идущие </color>
            text = DoubleCloseColorRegex.Replace(text, "</color>");
            // 4. Удаляем висящий открывающий тег в самом конце строки (без пары).
            text = TrailingOpenColorRegex.Replace(text, "");
            // 5. Удаляем ЛИШНИЕ закрывающие теги (их больше, чем открывающих).
            text = DropUnmatchedColorCloses(text);
            return text;
        }

        // Перевод длинных книг/журналов склеивает несколько цветовых отрезков оригинала в один
        // абзац, а закрывающие теги от съеденных отрезков остаются. Разметка перекашивается
        // ("[Обращение к читателю]</color>\n</color><color=...": 2 открывающих, 3 закрывающих),
        // и TextMeshPro печатает лишний "</color>" БУКВАЛЬНО — игрок видит тег в тексте книги.
        // Считаем глубину слева направо и выбрасываем закрывающие теги на нулевой глубине.
        // Недостающие закрывающие НЕ дописываем: тег до конца строки безвреден, а лишний текст
        // в переводе — нет.
        internal static string DropUnmatchedColorCloses(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            int closes = 0;
            for (int i = text.IndexOf("</color>", StringComparison.OrdinalIgnoreCase); i >= 0;
                 i = text.IndexOf("</color>", i + 8, StringComparison.OrdinalIgnoreCase)) closes++;
            if (closes == 0) return text;

            var sb = new System.Text.StringBuilder(text.Length);
            int depth = 0;
            int pos = 0;
            while (pos < text.Length)
            {
                if (pos + 7 <= text.Length && string.Compare(text, pos, "<color=", 0, 7, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    int close = text.IndexOf('>', pos);
                    if (close < 0) { sb.Append(text, pos, text.Length - pos); break; }
                    sb.Append(text, pos, close - pos + 1);
                    depth++;
                    pos = close + 1;
                    continue;
                }
                if (pos + 8 <= text.Length && string.Compare(text, pos, "</color>", 0, 8, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    if (depth > 0) { sb.Append("</color>"); depth--; }
                    // depth == 0 — пары нет, тег просто выбрасываем
                    pos += 8;
                    continue;
                }
                sb.Append(text[pos]);
                pos++;
            }
            return sb.ToString();
        }

        // Английский артикль перед русским словом: "У The слабый ...", "В the месяц ...".
        //
        // Между артиклем и русским словом может стоять разметка — игра режет строку на
        // цветные блоки ровно по границе артикля:
        //   "<color=#B1C9C3FF>The </color><color=#A64A2EFF>окровавленный ..."
        //   ":: The<color=#00C420FF> мусорный монах ..."
        // Поэтому кириллицу ищем через lookahead, пропуская теги, &-коды и пробелы,
        // а СЪЕДАЕМ только сам артикль и пробелы за ним — разметка должна уцелеть
        // (инвариант: число <color=/</color>/{{/}} до и после замены совпадает).
        private static readonly System.Text.RegularExpressions.Regex LeftoverArticleRegex =
            new System.Text.RegularExpressions.Regex(
                @"(^|[\s>|(\[""'—-])(?:the|an?)\b[ \t]*(?=(?:</?color[^>]*>|\{\{[A-Za-z]+\||\}\}|&[A-Za-z]|[ \t])*[А-Яа-яЁё])",
                System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Разметка, которую не надо считать при определении «языка» строки:
        // цветные теги, {{x|...}}-обёртки и &-коды. Внутри тега "<color=#B1C9C3FF>"
        // 11 латинских букв, поэтому по сырому тексту почти любая строка выглядит
        // латинской и StripLeftoverEnglishGlue не срабатывает.
        private static readonly System.Text.RegularExpressions.Regex MarkupForLetterCountRegex =
            new System.Text.RegularExpressions.Regex(
                @"</?color[^>]*>|\{\{[A-Za-z]+\||\}\}|&[A-Za-z]",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        // Английское "of" после русского слова: "сияющий стрела of Annulus Колесо Sharqushur".
        // Ведущий артикль второй части снимаем той же заменой ("of The Ellipse" -> " Ellipse").
        private static readonly System.Text.RegularExpressions.Regex LeftoverOfRegex =
            new System.Text.RegularExpressions.Regex(
                @"(?<=[А-Яа-яЁё][.,)\]""']?)[ \t]+of[ \t]+(?:the[ \t]+)?",
                System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>
        /// Снимает английские артикли и "of", прилипшие к русскому тексту.
        /// В русском нет артиклей, и ни один проход перевода их не убирает: словарь их не знает,
        /// а пословный проход оставляет как есть. Отсюда "У The слабый ... нечего продать" и
        /// сгенерированные названия вида "сияющий стрела of Annulus Колесо Sharqushur".
        ///
        /// Два разных условия применения — намеренно:
        ///  * артикли режем ТОЛЬКО когда кириллицы больше латиницы. В ещё не переведённом
        ///    английском предложении ("...looked like a miniature glacier bathed in ivory")
        ///    артикль — законная часть текста, и удалять его нельзя;
        ///  * "of" режем ещё и в коротких строках-названиях без границы предложения: там
        ///    латиницы часто больше ("аналоговый сабо of The Ellipse Sharqushur"), но это
        ///    заведомо имя предмета, а не английская проза.
        /// Прогон по логу 03.08 (2512 записи): 65 изменений, все в плюс, побочек нет.
        /// </summary>
        /// <summary>
        /// Распознаёт строку, ЦЕЛИКОМ обёрнутую в одну метку новой разметки Qud —
        /// "{{y|...}}", "{{rules|...}}" и т.п. — и отдаёт метку и содержимое отдельно.
        ///
        /// Обёртка снимается только когда открывающая "{{tag|" закрывается ровно
        /// финальными "}}" (глубина вложенности впервые обнуляется на самом конце).
        /// Строки вида "{{y|A}} и {{y|B}}" не трогаем: там обёртка не одна, и снятие
        /// внешних скобок склеило бы куски с разным цветом.
        /// </summary>
        // Первая буква сообщения журнала после префикса ":: " и любой разметки.
        private static readonly System.Text.RegularExpressions.Regex MessageLeadLetterRegex =
            new System.Text.RegularExpressions.Regex(
                @"^(::[ \t]*(?:</?color[^>]*>|\{\{[A-Za-z]+\||&[A-Za-z]|[ \t])*)(\p{Ll})",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// Поднимает регистр первой буквы в строке журнала (":: ...").
        ///
        /// Русское слово встаёт в начало предложения там, где в оригинале стоял артикль
        /// или служебное слово: "The giant dragonfly flinches..." -> "гигантская стрекоза
        /// уворачивается...". Ни словарь, ни пословный проход регистр не поднимают —
        /// они не знают, что фрагмент оказался первым.
        ///
        /// Ограничено префиксом ":: " намеренно: это разделитель журнала сообщений, то есть
        /// заведомо законченная фраза. Названия предметов и пункты списков приходят без него,
        /// и капитализировать их нельзя ("деревянная стрела x8" в инвентаре).
        /// </summary>
        internal static string CapitalizeMessageLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (text.Length < 3 || text[0] != ':' || text[1] != ':') return text;

            var m = MessageLeadLetterRegex.Match(text);
            if (!m.Success) return text;

            int idx = m.Groups[2].Index;
            char upper = char.ToUpperInvariant(text[idx]);
            if (upper == text[idx]) return text;

            var sb = new System.Text.StringBuilder(text);
            sb[idx] = upper;
            return sb.ToString();
        }

        internal static bool TryUnwrapQudMarkup(string text, out string tag, out string content)
        {
            tag = null;
            content = null;
            if (string.IsNullOrEmpty(text) || text.Length < 5) return false;
            if (text[0] != '{' || text[1] != '{') return false;
            if (!text.EndsWith("}}", StringComparison.Ordinal)) return false;

            int bar = -1;
            for (int i = 2; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '|') { bar = i; break; }
                // Метка — это короткий идентификатор цвета/стиля. Всё прочее (в т.ч.
                // вложенная "{{" сразу за открывающей) означает, что это не обёртка.
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-') return false;
            }
            if (bar < 0 || bar == 2) return false;

            int depth = 1;
            for (int i = bar + 1; i < text.Length - 1; i++)
            {
                if (text[i] == '{' && text[i + 1] == '{') { depth++; i++; continue; }
                if (text[i] == '}' && text[i + 1] == '}')
                {
                    depth--;
                    // Обнулились раньше конца — обёрток несколько, выходим.
                    if (depth == 0) return i == text.Length - 2 && Assign(text, bar, out tag, out content);
                    i++;
                }
            }
            return false;
        }

        private static bool Assign(string text, int bar, out string tag, out string content)
        {
            tag = text.Substring(2, bar - 2);
            content = text.Substring(bar + 1, text.Length - 2 - (bar + 1));
            return content.Length > 0;
        }

        internal static string StripLeftoverEnglishGlue(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (!ContainsCyrillic(text)) return text;

            // Соотношение кириллицы к латинице считаем по тексту БЕЗ разметки:
            // теги и &-коды состоят из латиницы и иначе перевешивают любую строку.
            string bare = MarkupForLetterCountRegex.Replace(text, " ");
            int cyr = 0, lat = 0;
            for (int i = 0; i < bare.Length; i++)
            {
                char c = bare[i];
                if ((c >= 'А' && c <= 'я') || c == 'Ё' || c == 'ё') cyr++;
                else if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) lat++;
            }
            bool cyrillicDominant = cyr > lat;

            // Строка-название: короткая, без границы предложения и без переносов.
            bool nameLike = text.Length <= 100 && text.IndexOf('\n') < 0
                && !System.Text.RegularExpressions.Regex.IsMatch(text, @"[.!?][ \t]");

            string result = text;
            if (cyrillicDominant) result = LeftoverArticleRegex.Replace(result, "$1");
            if (cyrillicDominant || nameLike) result = LeftoverOfRegex.Replace(result, " ");
            return result;
        }

        // Регулярки для нормализации текста
        private static readonly System.Text.RegularExpressions.Regex MultiSpaceRegex =
            new System.Text.RegularExpressions.Regex(@"[ \t]{2,}", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex MultiNewlineRegex =
            new System.Text.RegularExpressions.Regex(@"(\r?\n){3,}", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex DoubleDotRegex =
            new System.Text.RegularExpressions.Regex(@"(?<!http:|https:)(?<!\d)\.\.(?!\d)", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex SpaceBeforePunctRegex =
            new System.Text.RegularExpressions.Regex(@"[ \t]+([,;.!?]|:(?!:))", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex SpaceAfterPunctRegex =
            new System.Text.RegularExpressions.Regex(@"([,;:.!?])(?=[А-ЯЁA-Z][а-яёa-z])", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex NoSpaceAfterPunctRegex =
            new System.Text.RegularExpressions.Regex(@"([,;:.!?])(?=[а-яёa-z])", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// Финальная нормализация русского текста: схлопывает множественные пробелы,
        /// убирает лишние переносы строк, исправляет двойные точки на троеточие,
        /// убирает пробелы перед знаками препинания.
        /// Вызывается ПОСЛЕ StripEmptyColorBlocks, но ДО записи в лог all_gameplay_texts.txt.
        /// </summary>
        internal static string NormalizeRussianText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (!ContainsCyrillic(text) && !ContainsEnglish(text)) return text;

            string result = text;

            // 1. Заменяем все Unicode-пробелы на обычный ASCII пробел (неразрывные и т.д.)
            result = result.Replace('\u00A0', ' ')
                           .Replace('\u2007', ' ')
                           .Replace('\u200B', ' ')
                           .Replace('\u202F', ' ');

            // 2. Схлопываем 2+ подряд идущих пробела/таба в один
            // ВАЖНО: пропускаем содержимое внутри <color=...>...</color>, чтобы не сломать структуру
            result = NormalizeOutsideColorBlocks(result, MultiSpaceRegex, " ");

            // 2а. Двойной пробел ВНУТРИ цветового блока шаг 2 не трогает — и правильно:
            // титры и таблицы выравниваются как раз пробелами внутри блоков, схлопывать их
            // нельзя. Но два узких случая безопасны и видны игроку:
            //   * после двоеточия — "АКТИВНЫЕ ЭФФЕКТЫ:  переход вброд";
            //   * перед закрывающим тегом — "ЭФФЕКТЫ:  </color><color=...>окровавленный".
            //   * РОВНО два пробела между словом и строчной русской буквой (или скобкой) —
            //     "окровавленный  гигантская", "факел  (в основном сгорел)". Так склеиваются
            //     прилагательное с пустым слотом и следующее слово.
            // Ни один из них не может быть колонкой выравнивания: колонки шире двух пробелов
            // и выравнивают начало ячейки, а там заглавная буква или цифра, не строчная.
            result = System.Text.RegularExpressions.Regex.Replace(result, @"(?<=:)[ \t]{2,}(?=[А-Яа-яЁёA-Za-z])", " ");
            result = System.Text.RegularExpressions.Regex.Replace(result, @"(?<=[^\s])[ \t]{2,}(?=</color>)", " ");
            result = System.Text.RegularExpressions.Regex.Replace(result, @"(?<=\p{L})[ ]{2}(?=[а-яё(])", " ");

            // 3. Схлопываем 3+ подряд идущих переноса строк в 2 (\n\n)
            result = MultiNewlineRegex.Replace(result, "\n\n");

            // 4. Двойные точки -> троеточие (только если не URL и не диапазон)
            // 5+ точек — также троеточие
            result = System.Text.RegularExpressions.Regex.Replace(result, @"\.{4,}", "…");
            result = DoubleDotRegex.Replace(result, "…");

            // 5. Убираем пробелы перед знаками препинания: " ," -> ",", " ." -> "." и т.д.
            result = SpaceBeforePunctRegex.Replace(result, "$1");

            // 6. Добавляем пробел после запятой/точки/троеточия, если после идёт буква
            // Шаблон: .,[а-яёa-zА-ЯЁA-Z] без пробела
            // Сначала после запятой/точки с запятой
            result = System.Text.RegularExpressions.Regex.Replace(result, @"([,;:])([а-яёa-zА-ЯЁA-Z])", "$1 $2");
            // После точки — ТОЛЬКО перед заглавной буквой (граница предложения).
            // НЕ трогаем точку перед строчной, иначе ломаются имена файлов/URL: Mods.csproj, Colors.xml, qud.com
            result = System.Text.RegularExpressions.Regex.Replace(result, @"(?<!\d)\.([А-ЯЁA-Z])", ". $1");
            // После троеточия
            result = System.Text.RegularExpressions.Regex.Replace(result, @"…([а-яёa-zА-ЯЁA-Z])", "… $1");

            // 7. Чистим пробелы вокруг тегов: " </color> " -> "</color> ", "<color=X>  " -> "<color=X> "
            // и " </color>" -> "</color>", "<color=X> " без изменений (теги должны быть в начале)
            // ВАЖНО: НЕ убираем пробел перед </color>, если сразу за ним идёт новый <color=...>,
            // потому что этот пробел — разделитель слов между цветными блоками
            // (иначе "скоростью </color><color>2x" склеивается в "скоростью2x").
            result = System.Text.RegularExpressions.Regex.Replace(result, @"[ \t]+</color>(?!<color=)", "</color>");
            result = System.Text.RegularExpressions.Regex.Replace(result, @"<color=[^>]+>[ \t]+", m => m.Value.TrimEnd() + " ");

            // 8. Пробел после метки клавиши/опции "[x]" перед цветным текстом.
            // DistributeColors теряет пробел на границе цветных блоков:
            // "<color=A>[c]</color><color=B>Назначение</color>" -> рендерится "[c]Назначение".
            // Вставляем пробел между "]</color>" и "<color=...>Буква", чтобы кнопки читались "[c] Назначение".
            result = System.Text.RegularExpressions.Regex.Replace(result, @"(\]</color>)(<color=[^>]+>)(?=[A-Za-zА-Яа-яЁё])", "$1 $2");
            // То же без тегов между: "[c]Буква" -> "[c] Буква" (только латиница в скобках = клавиша/опция).
            result = System.Text.RegularExpressions.Regex.Replace(result, @"(\[[A-Za-z0-9]{1,6}\])(?=[A-Za-zА-Яа-яЁё])", "$1 ");

            return result;
        }

        /// <summary>
        /// Применяет regex-замену только к тексту ВНЕ цветовых блоков <color=X>...</color>,
        /// чтобы не сломать структуру (внутри тегов могут быть валидные пробелы).
        /// </summary>
        private static string NormalizeOutsideColorBlocks(string text, System.Text.RegularExpressions.Regex regex, string replacement)
        {
            var colorBlockRegex = new System.Text.RegularExpressions.Regex(@"<color=[^>]+>.*?</color>", System.Text.RegularExpressions.RegexOptions.Singleline);
            var matches = colorBlockRegex.Matches(text);
            if (matches.Count == 0) return regex.Replace(text, replacement);

            var sb = new System.Text.StringBuilder(text.Length);
            int lastIdx = 0;
            for (int i = 0; i < matches.Count; i++)
            {
                var m = matches[i];
                if (m.Index > lastIdx)
                {
                    string between = text.Substring(lastIdx, m.Index - lastIdx);
                    sb.Append(regex.Replace(between, replacement));
                }
                sb.Append(m.Value);
                lastIdx = m.Index + m.Length;
            }
            if (lastIdx < text.Length)
            {
                string rest = text.Substring(lastIdx);
                sb.Append(regex.Replace(rest, replacement));
            }
            return sb.ToString();
        }

        // --- ТРАНСЛИТЕРАЦИЯ КИРИЛЛИЦЫ В ЛАТИНИЦУ (ДЛЯ КЛАССИЧЕСКОГО ASCII-ТЕРМИНАЛА) ---

                        public static string Transliterate(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            bool hasRus = false;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] > 127) { hasRus = true; break; }
            }
            if (!hasRus) return text;

            StringBuilder sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                switch (c)
                {
                    // Строчные
                    case '\u0430': sb.Append("a"); break;
                    case '\u0431': sb.Append("b"); break;
                    case '\u0432': sb.Append("v"); break;
                    case '\u0433': sb.Append("g"); break;
                    case '\u0434': sb.Append("d"); break;
                    case '\u0435': sb.Append("e"); break;
                    case '\u0451': sb.Append("yo"); break;
                    case '\u0436': sb.Append("zh"); break;
                    case '\u0437': sb.Append("z"); break;
                    case '\u0438': sb.Append("i"); break;
                    case '\u0439': sb.Append("j"); break;
                    case '\u043a': sb.Append("k"); break;
                    case '\u043b': sb.Append("l"); break;
                    case '\u043c': sb.Append("m"); break;
                    case '\u043d': sb.Append("n"); break;
                    case '\u043e': sb.Append("o"); break;
                    case '\u043f': sb.Append("p"); break;
                    case '\u0440': sb.Append("r"); break;
                    case '\u0441': sb.Append("s"); break;
                    case '\u0442': sb.Append("t"); break;
                    case '\u0443': sb.Append("u"); break;
                    case '\u0444': sb.Append("f"); break;
                    case '\u0445': sb.Append("kh"); break;
                    case '\u0446': sb.Append("ts"); break;
                    case '\u0447': sb.Append("ch"); break;
                    case '\u0448': sb.Append("sh"); break;
                    case '\u0449': sb.Append("shch"); break;
                    case '\u044b': sb.Append("y"); break;
                    case '\u044d': sb.Append("e"); break;
                    case '\u044e': sb.Append("yu"); break;
                    case '\u044f': sb.Append("ya"); break;

                    // Заглавные
                    case '\u0410': sb.Append("A"); break;
                    case '\u0411': sb.Append("B"); break;
                    case '\u0412': sb.Append("V"); break;
                    case '\u0413': sb.Append("G"); break;
                    case '\u0414': sb.Append("D"); break;
                    case '\u0415': sb.Append("E"); break;
                    case '\u0401': sb.Append("Yo"); break;
                    case '\u0416': sb.Append("Zh"); break;
                    case '\u0417': sb.Append("Z"); break;
                    case '\u0418': sb.Append("I"); break;
                    case '\u0419': sb.Append("J"); break;
                    case '\u041a': sb.Append("K"); break;
                    case '\u041b': sb.Append("L"); break;
                    case '\u041c': sb.Append("M"); break;
                    case '\u041d': sb.Append("N"); break;
                    case '\u041e': sb.Append("O"); break;
                    case '\u041f': sb.Append("P"); break;
                    case '\u0420': sb.Append("R"); break;
                    case '\u0421': sb.Append("S"); break;
                    case '\u0422': sb.Append("T"); break;
                    case '\u0423': sb.Append("U"); break;
                    case '\u0424': sb.Append("F"); break;
                    case '\u0425': sb.Append("Kh"); break;
                    case '\u0426': sb.Append("Ts"); break;
                    case '\u0427': sb.Append("Ch"); break;
                    case '\u0428': sb.Append("Sh"); break;
                    case '\u0429': sb.Append("Shch"); break;
                    case '\u042b': sb.Append("Y"); break;
                    case '\u042d': sb.Append("E"); break;
                    case '\u042e': sb.Append("Yu"); break;
                    case '\u042f': sb.Append("Ya"); break;

                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }



        // --- ДИНАМИЧЕСКИЙ ПАТЧ UIElements.TextElement ЧЕРЕЗ РЕФЛЕКСИЮ ---

        public static void PatchUIElements()

        {

            try

            {

                System.Type textElementType = null;

                System.Type uiDocumentType = null;

                System.Type callbackEventHandlerType = null;

                System.Type eventBaseType = null;



                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())

                {

                    try

                    {

                        if (textElementType == null) textElementType = asm.GetType("UnityEngine.UIElements.TextElement");

                        if (uiDocumentType == null) uiDocumentType = asm.GetType("UnityEngine.UIElements.UIDocument");

                        if (callbackEventHandlerType == null) callbackEventHandlerType = asm.GetType("UnityEngine.UIElements.CallbackEventHandler");

                        if (eventBaseType == null) eventBaseType = asm.GetType("UnityEngine.UIElements.EventBase");

                    }

                    catch { }

                }



                if (AnotherInstallAlreadyPatched("com.russianlocalization.uielements", "PatchUIElements")) return;
                
                var harmony = new Harmony("com.russianlocalization.uielements");



                // 1. Патч TextElement.text (Setter)

                if (textElementType != null)

                {

                    var textProperty = textElementType.GetProperty("text", BindingFlags.Public | BindingFlags.Instance);

                    if (textProperty != null)

                    {

                        var textSetter = textProperty.GetSetMethod();

                        var prefixMethod = typeof(UIElementsDynamicPatch).GetMethod("TextElement_Prefix", BindingFlags.Public | BindingFlags.Static);

                        if (textSetter != null && prefixMethod != null)

                        {

                            harmony.Patch(textSetter, prefix: new HarmonyMethod(prefixMethod));

                            UnityEngine.Debug.Log("[RussianLocalization] UIElements.TextElement.text patched dynamically (Modern UI support enabled).");

                        }

                    }

                }



                // 2. Патч UIDocument.OnEnable (Postfix)

                if (uiDocumentType != null)

                {

                    var onEnableMethod = uiDocumentType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

                    var postfixMethod = typeof(UIElementsDynamicPatch).GetMethod("UIDocument_OnEnable_Postfix", BindingFlags.Public | BindingFlags.Static);

                    if (onEnableMethod != null && postfixMethod != null)

                    {

                        harmony.Patch(onEnableMethod, postfix: new HarmonyMethod(postfixMethod));

                        UnityEngine.Debug.Log("[RussianLocalization] UIElements.UIDocument.OnEnable patched dynamically.");

                    }

                }



                // 3. Патч CallbackEventHandler.ExecuteDefaultAction (Postfix)

                if (callbackEventHandlerType != null && eventBaseType != null)

                {

                    var execMethod = callbackEventHandlerType.GetMethod("ExecuteDefaultAction", BindingFlags.NonPublic | BindingFlags.Instance, null, new System.Type[] { eventBaseType }, null);

                    var postfixMethod = typeof(UIElementsDynamicPatch).GetMethod("VisualElement_ExecuteDefaultAction_Postfix", BindingFlags.Public | BindingFlags.Static);

                    if (execMethod != null && postfixMethod != null)

                    {

                        harmony.Patch(execMethod, postfix: new HarmonyMethod(postfixMethod));

                        UnityEngine.Debug.Log("[RussianLocalization] UIElements.CallbackEventHandler.ExecuteDefaultAction patched dynamically.");

                    }

                }



                // 4. Патч INotifyValueChanged<T>.SetValueWithoutNotify (Postfix) — мгновенный перевод
                // ------------------------------------------------------------------------------------
                // Modern UI обновляет текст в TextElement не через свойство text (которое мы
                // уже патчим), а напрямую через SetValueWithoutNotify<T>(T value) из
                // INotifyValueChanged<T>. Этот метод специально обходит setter-патчи,
                // потому что записывает значение в backing field без нотификации.
                //
                // Решение: Harmony Prefix на SetValueWithoutNotify — подменяем value на
                // переведённый текст ПЕРЕД записью в поле. Это срабатывает мгновенно,
                // без задержки RuntimeTranslator-полллинга.

                if (textElementType != null)

                {

                    System.Type[] interfaces;

                    try { interfaces = textElementType.GetInterfaces(); }

                    catch { interfaces = System.Array.Empty<System.Type>(); }



                    foreach (var iface in interfaces)

                    {

                        if (iface == null) continue;

                        if (!iface.IsGenericType) continue;

                        if (iface.GetGenericTypeDefinition().FullName != "UnityEngine.UIElements.INotifyValueChanged`1") continue;



                        System.Type[] typeArgs = iface.GetGenericArguments();

                        if (typeArgs == null || typeArgs.Length != 1) continue;

                        if (typeArgs[0] != typeof(string)) continue;



                        // Нашли INotifyValueChanged<string> — ищем метод SetValueWithoutNotify на ИНТЕРФЕЙСЕ.
                        // (Реализация в BindableElement — internal, мы патчим интерфейсный метод,
                        //  а Harmony навешивается на concrete-implementation через ResolveMethod.)
                        var ifaceMethod = iface.GetMethod("SetValueWithoutNotify");

                        if (ifaceMethod == null) continue;



                        // Ищем concrete-реализацию этого метода на textElementType.
                        System.Reflection.MethodInfo implMethod = null;

                        try

                        {

                            // GetInterfaceMap даёт прямое соответствие iface-метода → реализации.
                            var map = textElementType.GetInterfaceMap(iface);

                            for (int mi = 0; mi < map.InterfaceMethods.Length; mi++)

                            {

                                if (map.InterfaceMethods[mi] == ifaceMethod)

                                {

                                    implMethod = map.TargetMethods[mi];

                                    break;

                                }

                            }

                        }

                        catch { }



                        if (implMethod == null) continue;



                        // Проверяем, не запатчили ли уже.
                        var existingSvc = Harmony.GetPatchInfo(implMethod);

                        bool alreadySvc = false;

                        if (existingSvc != null && existingSvc.Prefixes != null)

                        {

                            foreach (var pf in existingSvc.Prefixes)

                            {

                                if (pf != null && pf.owner == "com.russianlocalization.uielements")

                                {

                                    alreadySvc = true; break;

                                }

                            }

                        }

                        if (alreadySvc) continue;



                        var prefixSvc = typeof(UIElementsDynamicPatch).GetMethod("INotifyValueChanged_SetValueWithoutNotify_Prefix", BindingFlags.Public | BindingFlags.Static);

                        if (prefixSvc == null) continue;



                        try

                        {

                            harmony.Patch(implMethod, prefix: new HarmonyMethod(prefixSvc));

                            UnityEngine.Debug.Log("[RussianLocalization] TextElement.SetValueWithoutNotify(string) patched dynamically — мгновенный перевод Modern UI.");

                        }

                        catch (Exception exSvc)

                        {

                            UnityEngine.Debug.LogWarning("[RussianLocalization] Failed to patch SetValueWithoutNotify: " + exSvc.Message);

                        }



                        break; // хватит одной реализации

                    }

                }

            }

            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[RussianLocalization] UIElements dynamic patch error: " + ex.ToString());
            }
            }

        // --- ДИНАМИЧЕСКИЙ ПАТЧ QudTranslator.dll (ОФИЦИАЛЬНЫЙ ПЕРЕВОД РАЗРАБОТЧИКА) ---
        //
        // В Caves of Qud ≥ 2.0.214 появилась отдельная сборка QudTranslator с методом Translate,
        // через которую Modern UI тянет переведённые строки. Чтобы наш словарь выигрывал у встроенного,
        // перехватываем любые публичные методы Translate(string, ...) этого типа.
        //
        // Подход: Reflection + Harmony.Patch на найденный MethodInfo. Это совместимо с тем,
        // что имя класса/пространства имён может меняться между патчами игры (QudTranslator.Translate,
        // XRL.Translation.Translator.Translate и т.п.).

        public static void PatchQudTranslator()
        {
            try
            {
                int patched = 0;
                var ourAssembly = System.Reflection.Assembly.GetExecutingAssembly();

                // Принудительно загружаем QudTranslator.dll, если он ещё не подгружен.
                try { System.Reflection.Assembly.Load("QudTranslator"); }
                catch { /* не страшно — проверим в цикле */ }

                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    System.Type[] types;
                    try { types = asm.GetTypes(); }
                    catch (System.Reflection.ReflectionTypeLoadException rtle) { types = rtle.Types; }
                    catch { continue; }

                    foreach (var t in types)
                    {
                        if (t == null) continue;
                        // Пропускаем свою же сборку — патчить самих себя бессмысленно.
                        if (t.Assembly == ourAssembly) continue;
                        // Эвристика: класс из сборки, в названии есть "Translator" / "Translation".
                        // Это покрывает и "QudTranslator", и "XRL.Translation.Translator", и будущие рефакторы.
                        string tn = t.Name ?? string.Empty;
                        string ns = t.Namespace ?? string.Empty;
                        bool looksLikeTranslator =
                            tn.IndexOf("Translator", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                            tn.IndexOf("Translation", System.StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!looksLikeTranslator) continue;

                        System.Reflection.MethodInfo[] methods;
                        try { methods = t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static); }
                        catch { continue; }

                        foreach (var m in methods)
                        {
                            if (m == null) continue;
                            if (!string.Equals(m.Name, "Translate", System.StringComparison.Ordinal)) continue;

                            var parameters = m.GetParameters();
                            if (parameters == null || parameters.Length == 0) continue;

                            // Нас интересуют перегрузки, у которых хотя бы один параметр — string,
                            // и строка входит первой (основной текст для перевода).
                            bool firstIsString = parameters[0].ParameterType == typeof(string);
                            if (!firstIsString) continue;

                            // ВАЖНО: метод обязан возвращать string. Постфикс ниже объявлен как
                            // (ref string __result, string text) — если реальная сигнатура метода
                            // возвращает void/bool/другой тип, Harmony всё равно может принять патч
                            // при регистрации, но сгенерированный IL окажется рассинхронизирован
                            // с реальным возвращаемым типом и упадёт при первом реальном вызове —
                            // нативный краш (access violation) без единой строки в managed-логе.
                            // Эвристика по имени класса ("...Translator...") слишком широкая и легко
                            // цепляет чужой метод Translate(string, ...), не имеющий отношения к
                            // языковому переводу (например, служебный маппинг тегов/ключей).
                            if (m.ReturnType != typeof(string)) continue;

                            // Пропускаем generic-методы — Harmony их не умеет патчить.
                            if (m.IsGenericMethodDefinition) continue;

                            // Пропускаем, если уже есть Postfix с нашим ID (защита от двойной установки).
                            var existing = Harmony.GetPatchInfo(m);
                            if (existing != null && existing.Postfixes != null)
                            {
                                bool alreadyPatched = false;
                                foreach (var pf in existing.Postfixes)
                                {
                                    if (pf != null && pf.owner == "com.russianlocalization.qudtranslator")
                                    {
                                        alreadyPatched = true; break;
                                    }
                                }
                                if (alreadyPatched) continue;
                            }

                            try
                            {
                                var postfix = typeof(TranslationEngine).GetMethod(
                                    "QudTranslator_Translate_Postfix",
                                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

                                if (postfix == null) continue;

                                var harmony = new Harmony("com.russianlocalization.qudtranslator");
                                harmony.Patch(m, postfix: new HarmonyMethod(postfix));

                                UnityEngine.Debug.Log("[RussianLocalization] Patched QudTranslator.Translate on " + t.FullName + " :: " +
                                    m.Name + "(" + parameters.Length + " params).");
                                patched++;
                            }
                            catch (Exception exInner)
                            {
                                UnityEngine.Debug.LogWarning("[RussianLocalization] Failed to patch " + t.FullName + "." + m.Name + ": " + exInner.Message);
                            }
                        }
                    }
                }

                if (patched == 0)
                {
                    UnityEngine.Debug.Log("[RussianLocalization] QudTranslator.Translate not found (no compatible types/methods). Native translator path will be untouched.");
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[RussianLocalization] PatchQudTranslator error: " + ex.ToString());
            }
        }

        // Постфикс для QudTranslator.Translate: после того, как родной транслятор вернул результат,
        // пропускаем его через наш движок. Если родной транслятор ничего не вернул/вернул исходник —
        // наш словарь всё равно отработает.
        //
        // Сигнатура намеренно максимально общая: первый параметр — входной текст (string),
        // второй — возвращаемый результат (ref string). Harmony нормально склеивает это с любой
        // перегрузкой Translate(string, ...). Дополнительные параметры передаются как обычные
        // аргументы и игнорируются телом метода.
        private static bool HasCyrillic(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= 'Ѐ' && c <= 'ӿ') || c == 'ё' || c == 'Ё')
                    return true;
            }
            return false;
        }

        public static void QudTranslator_Translate_Postfix(ref string __result, string text)
        {
            try
            {
                if (!TranslationEngine.Initialized) return;
                if (__result == null) return;

                // Если родной транслятор уже вернул что-то осмысленное на русском без английского — оставляем.
                bool hasCyr, hasEng;
                ScanAlpha(__result, out hasCyr, out hasEng);
                if (hasCyr && !hasEng) return;

                // Быстрый пропуск: слишком короткие строки (1-2 символа) не тратим время на словарь.
                if (string.IsNullOrEmpty(text) || text.Length <= 2) return;

                // Пробуем наш словарь.
                string our = TranslationEngine.Translate(text);
                if (!string.IsNullOrEmpty(our) && our != text)
                {
                    __result = our;
                }
            }
            catch { /* не ломаем чужой транслятор */ }
        }

        // --- ГЛАВНЫЙ ХУК MODERN UI: XRL.UI.UITextSkin ---
        //
        // В Caves of Qud ≥ 2.0.211 текст нового интерфейса (лист персонажа, инвентарь,
        // сайдбар, тултипы) рисуется НЕ через TMP_Text и НЕ через UIElements.TextElement,
        // а через собственный MonoBehaviour XRL.UI.UITextSkin, метод SetText(string).
        // Поэтому все прежние хуки мода его не видят. Перехватываем SetText() и SetTheText()
        // динамически (рефлексия + try/catch, чтобы не уронить PatchAll при смене сигнатуры).
        private static System.Reflection.FieldInfo _utsTextField;

        public static void PatchUITextSkin()
        {
            try
            {
                System.Type t = null;
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    try { t = asm.GetType("XRL.UI.UITextSkin"); } catch { }
                    if (t != null) break;
                }
                if (t == null)
                {
                    UnityEngine.Debug.Log("[RussianLocalization] XRL.UI.UITextSkin not found — Modern UI hook skipped.");
                    return;
                }

                _utsTextField = t.GetField("text",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (AnotherInstallAlreadyPatched("com.russianlocalization.uitextskin", "PatchUITextSkin")) return;
                
                var harmony = new Harmony("com.russianlocalization.uitextskin");
                int patched = 0;

                var setText = t.GetMethod("SetText",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                    null, new System.Type[] { typeof(string) }, null);
                if (setText != null)
                {
                    var pre = typeof(TranslationEngine).GetMethod("UITextSkin_SetText_Prefix",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (pre != null) { harmony.Patch(setText, prefix: new HarmonyMethod(pre)); patched++; }
                }

                // 2026-07-28: ФОЛБЭК ДЛЯ ПРЯМОЙ ЗАПИСИ В ПОЛЕ .text.
                //
                // Часть Modern UI пишет текст НЕ через SetText(string), а прямо в public-поле
                // UITextSkin.text и затем зовёт Apply(). Самый заметный пример — экран справки:
                //   Qud.UI.HelpRow.setData():
                //       categoryDescription.text = "{{C|" + row.Description.ToUpper() + "}}";
                //       categoryDescription.Apply();
                //       description.text = row.HelpText;   // + подстановка ~CmdLook → {{hotkey|…}}
                //       description.Apply();
                // Из-за этого ни SetText-патч, ни TMP_Text-патчи не видят текст (финальная запись
                // в TMP идёт через tmp.SetCharArray(char[], int, int), а не через сеттер .text).
                //
                // Раньше фолбэк вешался на SetTheText() с ПУСТЫМ списком параметров, но в текущей
                // версии игры сигнатура — private void SetTheText(ReadOnlySpan<char> Text), поэтому
                // GetMethod(..., Type.EmptyTypes, ...) молча возвращал null и патч не ставился
                // (в логе было "methods patched = 1" вместо 2).
                //
                // Правильная точка — public void Apply(): это единственная воронка (SetTheText
                // вызывается только из неё), сигнатура стабильна, и на момент вызова HelpRow уже
                // закончил подстановку ~Cmd-токенов — то есть в префикс приходит ровно такой же
                // размеченный текст, какой получает SetText-патч. Патчить сам
                // SetTheText(ReadOnlySpan<char>) не стоит: Span — ref struct, byref-параметр в
                // Harmony-префиксе на нём хрупкий.
                if (_utsTextField != null)
                {
                    var apply = t.GetMethod("Apply",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                        null, System.Type.EmptyTypes, null);
                    if (apply != null)
                    {
                        var pre2 = typeof(TranslationEngine).GetMethod("UITextSkin_SetTheText_Prefix",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        if (pre2 != null) { harmony.Patch(apply, prefix: new HarmonyMethod(pre2)); patched++; }
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning("[RussianLocalization] UITextSkin.Apply() not found — прямая запись в .text останется без перевода.");
                    }
                }

                UnityEngine.Debug.Log("[RussianLocalization] Patched XRL.UI.UITextSkin (Modern UI core hook), methods patched = " + patched + ".");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[RussianLocalization] PatchUITextSkin error: " + ex.ToString());
            }
        }

        // ============================================================
        // ПАТЧ XRL.UI.Popup — popup-сообщения, меню, запросы (≥ 2.0.211)
        // 40+ методов, ~277 вызовов в коде. ShowYesNo=108, PickOption=84 и т.д.
        // Динамически находим все методы со string-параметрами и патчим.
        // ============================================================
        // ============================================================
        public static void PatchPopup()
        {
            try
            {
                System.Type t = null;
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    try { t = asm.GetType("XRL.UI.Popup"); } catch { }
                    if (t != null) break;
                }
                if (t == null)
                {
                    UnityEngine.Debug.Log("[RussianLocalization] XRL.UI.Popup not found — popup hook skipped.");
                    return;
                }

                if (AnotherInstallAlreadyPatched("com.russianlocalization.popup", "PatchPopup")) return;
                
                var harmony = new Harmony("com.russianlocalization.popup");
                int patched = 0;

                var prefixMethod = typeof(TranslationEngine).GetMethod("Popup_Generic_Prefix",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

                var methods = t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                foreach (var m in methods)
                {
                    if (m == null) continue;
                    if (m.IsGenericMethodDefinition) continue;

                    // Пропускаем get/set properties (имена вида get_X / set_X)
                    if (m.Name.StartsWith("get_") || m.Name.StartsWith("set_")) continue;

                    // Пропускаем, если уже есть prefix с нашим ID
                    var existing = Harmony.GetPatchInfo(m);
                    if (existing != null && existing.Prefixes != null)
                    {
                        bool alreadyPatched = false;
                        foreach (var pf in existing.Prefixes)
                        {
                            if (pf != null && pf.owner == "com.russianlocalization.popup")
                            {
                                alreadyPatched = true; break;
                            }
                        }
                        if (alreadyPatched) continue;
                    }

                    // Патчим только методы, у которых хотя бы один параметр — string, IReadOnlyList<string>, List<string> или string[]
                    var parameters = m.GetParameters();
                    bool hasStringParam = false;
                    foreach (var p in parameters)
                    {
                        if (p.ParameterType == typeof(string) || 
                            p.ParameterType == typeof(System.Collections.Generic.IReadOnlyList<string>) ||
                            p.ParameterType == typeof(System.Collections.Generic.List<string>) ||
                            p.ParameterType == typeof(string[]))
                        {
                            hasStringParam = true; break;
                        }
                    }
                    if (!hasStringParam) continue;

                    try
                    {
                        harmony.Patch(m, prefix: new HarmonyMethod(prefixMethod));
                        patched++;
                    }
                    catch (Exception exPatch)
                    {
                        UnityEngine.Debug.LogWarning("[RussianLocalization] Failed to patch Popup." + m.Name + ": " + exPatch.Message);
                    }
                }

                // Также патчим методы, возвращающие string (AskString, AskNumber) — postfix для __result
                var postfixMethod = typeof(TranslationEngine).GetMethod("Popup_Generic_Postfix",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (postfixMethod != null)
                {
                    foreach (var m in methods)
                    {
                        if (m == null || m.IsGenericMethodDefinition) continue;
                        if (m.ReturnType != typeof(string)) continue;
                        // FIX B1 (2026-07-20): не переводим результат методов, возвращающих
                        // ввод пользователя / служебные значения (AskString, ShowColorPicker и пр.)
                        if (PopupPostfixSkipMethods.Contains(m.Name)) continue;

                        var existing = Harmony.GetPatchInfo(m);
                        if (existing != null && existing.Postfixes != null)
                        {
                            bool alreadyPatched = false;
                            foreach (var pf in existing.Postfixes)
                            {
                                if (pf != null && pf.owner == "com.russianlocalization.popup")
                                {
                                    alreadyPatched = true; break;
                                }
                            }
                            if (alreadyPatched) continue;
                        }

                        try
                        {
                            harmony.Patch(m, postfix: new HarmonyMethod(postfixMethod));
                        }
                        catch (Exception exPatch)
                        {
                            // Игнорируем — prefix уже покрывает основные случаи
                        }
                    }
                }

                UnityEngine.Debug.Log("[RussianLocalization] Patched XRL.UI.Popup, methods patched = " + patched + ".");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[RussianLocalization] PatchPopup error: " + ex.ToString());
            }
        }

        // ============================================================
        // ПАТЧ XRL.UI.BookUI.AutoformatPages — перенос строк в книгах
        // ============================================================
        // 2026-08-02: игроки жалуются, что в сгенерированных книгах длинный текст
        // «не переносится» — строки вылезают за поля страницы и рвутся посреди слова.
        //
        // Причина. Книги с Format="Auto" (25 штук в Books.xml) и процедурно
        // сгенерированные книги приходят в AutoformatPages одним куском НЕнарезанного
        // текста: абзацы разделены \n, внутри абзаца переносов нет. AutoformatPages
        // сама режет его на строки по ширине страницы (NextLine/NextWordLength) и
        // складывает готовые BookPage — каждая с уже жёстко проставленными переносами.
        //
        // Мод до сих пор видел книгу только ПОСЛЕ нарезки: в классическом UI через
        // ScreenBuffer.Write построчно, в Modern UI через BookPage.RenderForModernUI →
        // UITextSkin. То есть переводилась каждая готовая строка по отдельности, а
        // русская строка длиннее английской на 15-30% — при этом перенос, посчитанный
        // по английской ширине, оставался на прежнем месте. Отсюда и вылезающий текст.
        //
        // Правильная точка — ДО нарезки. Переводим Title и Text в префиксе, и игра
        // сама переносит уже русский текст по фактической ширине страницы.
        public static void PatchBookUI()
        {
            try
            {
                System.Type t = null;
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    try { t = asm.GetType("XRL.UI.BookUI"); } catch { }
                    if (t != null) break;
                }
                if (t == null)
                {
                    UnityEngine.Debug.Log("[RussianLocalization] XRL.UI.BookUI not found — book word-wrap hook skipped.");
                    return;
                }

                if (AnotherInstallAlreadyPatched("com.russianlocalization.bookui", "PatchBookUI")) return;
                
                var harmony = new Harmony("com.russianlocalization.bookui");
                var pre = typeof(TranslationEngine).GetMethod("BookUI_AutoformatPages_Prefix",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                int patched = 0;

                if (pre != null)
                {
                    // Две перегрузки: (Title, Text, Format, Margins) и
                    // (Title, Text, Format, Left, Right, Top, Bottom). Имена первых двух
                    // параметров в обеих одинаковы — Harmony связывает префикс по имени.
                    foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                                                   System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance))
                    {
                        if (m == null || m.IsGenericMethodDefinition) continue;
                        if (m.Name != "AutoformatPages") continue;

                        var ps = m.GetParameters();
                        if (ps.Length < 2) continue;
                        if (ps[0].ParameterType != typeof(string) || ps[1].ParameterType != typeof(string)) continue;
                        if (ps[0].Name != "Title" || ps[1].Name != "Text") continue;

                        try
                        {
                            harmony.Patch(m, prefix: new HarmonyMethod(pre));
                            patched++;
                        }
                        catch (Exception exPatch)
                        {
                            UnityEngine.Debug.LogWarning("[RussianLocalization] Failed to patch BookUI.AutoformatPages: " + exPatch.Message);
                        }
                    }
                }

                UnityEngine.Debug.Log("[RussianLocalization] Patched XRL.UI.BookUI (book word-wrap fix), methods patched = " + patched + ".");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[RussianLocalization] PatchBookUI error: " + ex.ToString());
            }
        }

        // Потолок для пофразового фолбэка в книге. Статичные книги берутся одним ключом
        // (см. ниже) и сюда не попадают; фолбэк нужен только процедурно сгенерированным,
        // а они заметно короче. Ограничение страхует от полного пословного прохода по
        // очень длинному тексту прямо в кадре открытия книги.
        private const int BookParagraphFallbackLimit = 20000;

        public static void BookUI_AutoformatPages_Prefix(ref string Title, ref string Text)
        {
            try
            {
                if (!Initialized || !IsEnabled) return;

                if (!string.IsNullOrEmpty(Title))
                {
                    string translatedTitle = Translate(Title);
                    if (!string.IsNullOrEmpty(translatedTitle)) Title = translatedTitle;
                }

                if (string.IsNullOrEmpty(Text)) return;

                bool hasCyr, hasEng;
                ScanAlpha(Text, out hasCyr, out hasEng);
                if (hasCyr && !hasEng) return;

                // 1. Целая страница одним ключом. Статичные книги Books.xml лежат в словаре
                //    именно так — целиком, вместе с \n между абзацами. Для текста длиннее
                //    OversizeThreshold это ровно тот путь, который Translate() и так проверяет
                //    первым (точное совпадение), то есть стоит один поиск в хеше.
                string whole = Translate(Text);
                if (!string.IsNullOrEmpty(whole) && whole != Text)
                {
                    Text = whole;
                    return;
                }

                // 2. Процедурно сгенерированная книга: целого ключа нет и быть не может —
                //    текст собирается на лету. Переводим по абзацам. Все \n здесь авторские
                //    (нарезки по ширине ещё не было), поэтому границы абзацев сохраняем
                //    как есть — игра расставит переносы сама.
                if (Text.Length > BookParagraphFallbackLimit) return;

                string[] parts = Text.Split('\n');
                var sb = new StringBuilder(Text.Length + Text.Length / 3);
                bool changed = false;
                for (int i = 0; i < parts.Length; i++)
                {
                    if (i > 0) sb.Append('\n');

                    string part = parts[i];
                    // На CRLF-тексте split('\n') оставляет '\r' в хвосте: он не должен
                    // попасть в ключ словаря, но обязан вернуться в результат.
                    bool hadCr = part.Length > 0 && part[part.Length - 1] == '\r';
                    if (hadCr) part = part.Substring(0, part.Length - 1);

                    if (part.Trim().Length == 0)
                    {
                        sb.Append(part);
                        if (hadCr) sb.Append('\r');
                        continue;
                    }

                    string translatedPart = Translate(part);
                    if (!string.IsNullOrEmpty(translatedPart) && translatedPart != part)
                    {
                        changed = true;
                        sb.Append(translatedPart);
                    }
                    else
                    {
                        sb.Append(part);
                    }
                    if (hadCr) sb.Append('\r');
                }
                if (changed) Text = sb.ToString();
            }
            catch
            {
                // Книга должна открыться в любом случае — даже непереведённой.
            }
        }

        public static void PatchScreenBufferDynamic()
        {
            try
            {
                System.Type t = typeof(ConsoleLib.Console.ScreenBuffer);
                if (AnotherInstallAlreadyPatched("com.russianlocalization.screenbuffer.dynamic", "PatchScreenBufferDynamic")) return;
                
                var harmony = new Harmony("com.russianlocalization.screenbuffer.dynamic");
                int patched = 0;

                var prefixMethod = typeof(TranslationEngine).GetMethod("ScreenBuffer_Generic_Prefix",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                var methods = t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance);
                
                // Сбор отладочной информации о всех методах ScreenBuffer
                // FIX B3 (2026-07-20): диагностический дамп — только при флаге DebugFileLogging
                if (DebugFileLogging)
                {
                    try
                    {
                        string logPath = Path.Combine(GetModPath(), "screenbuffer_methods.txt");
                        List<string> methodLogs = new List<string>();
                        foreach (var m in methods)
                        {
                            if (m == null) continue;
                            var pars = m.GetParameters();
                            List<string> parNames = new List<string>();
                            foreach (var p in pars) parNames.Add(p.ParameterType.ToString() + " " + p.Name);
                            methodLogs.Add(m.ReturnType.ToString() + " " + m.Name + "(" + string.Join(", ", parNames) + ")");
                        }
                        File.WriteAllLines(logPath, methodLogs.ToArray(), Encoding.UTF8);
                    }
                    catch { }
                }

                // Диагностика классов UI для поиска экрана осмотра
                // FIX B3 (2026-07-20): диагностический дамп — только при флаге DebugFileLogging
                if (DebugFileLogging)
                {
                    try
                    {
                        string logPath = Path.Combine(GetModPath(), "show_method_check.txt");
                        List<string> lines = new List<string>();
                        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                        {
                            string asmName = asm.FullName;
                            if (!asmName.Contains("Assembly-CSharp") && !asmName.Contains("XRL") && !asmName.Contains("Qud") && !asmName.Contains("ConsoleLib")) continue;
                            foreach (var type in asm.GetTypes())
                            {
                                try
                                {
                                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
                                    {
                                        if (method.Name == "Show")
                                        {
                                            var pars = method.GetParameters();
                                            if (pars.Length >= 4 && pars[0].ParameterType == typeof(string))
                                            {
                                                List<string> pTypes = new List<string>();
                                                foreach (var p in pars) pTypes.Add(p.ParameterType.FullName);
                                                lines.Add("Type: " + type.FullName + " -> Show(" + string.Join(", ", pTypes.ToArray()) + ")");
                                            }
                                        }
                                    }
                                }
                                catch {}
                            }
                        }
                        File.WriteAllLines(logPath, lines.ToArray(), Encoding.UTF8);
                    }
                    catch (Exception ex)
                    {
                        File.WriteAllText(Path.Combine(GetModPath(), "show_method_check.txt"), "Error: " + ex.ToString(), Encoding.UTF8);
                    }
                }

                foreach (var m in methods)
                {
                    if (m == null) continue;
                    if (m.IsGenericMethodDefinition) continue;
                    if (!m.Name.StartsWith("Write")) continue;

                    var parameters = m.GetParameters();
                    bool hasStringParam = false;
                    foreach (var p in parameters)
                    {
                        if (p.ParameterType == typeof(string) || 
                            p.ParameterType == typeof(string).MakeByRefType() ||
                            p.ParameterType == typeof(string[]) ||
                            p.ParameterType == typeof(System.Text.StringBuilder))
                        {
                            hasStringParam = true; break;
                        }
                    }
                    if (!hasStringParam) continue;

                    var existing = Harmony.GetPatchInfo(m);
                    if (existing != null && existing.Prefixes != null && existing.Prefixes.Count > 0) continue;

                    try
                    {
                        harmony.Patch(m, prefix: new HarmonyMethod(prefixMethod));
                        patched++;
                    }
                    catch (Exception exPatch)
                    {
                        UnityEngine.Debug.LogWarning("[RussianLocalization] Failed to patch ScreenBuffer." + m.Name + ": " + exPatch.Message);
                    }
                }
                UnityEngine.Debug.Log("[RussianLocalization] Patched ScreenBuffer dynamically, methods patched = " + patched + ".");

                // Диагностика классов UI для поиска экрана осмотра
                // FIX B3 (2026-07-20): диагностический дамп — только при флаге DebugFileLogging
                if (DebugFileLogging)
                {
                    try
                    {
                        string logPath = Path.Combine(GetModPath(), "lambda_calls.txt");
                        var tPopup = System.Type.GetType("XRL.UI.Popup, Assembly-CSharp") ??
                                     System.Type.GetType("XRL.UI.Popup");
                        if (tPopup != null)
                        {
                            var tDisplay = tPopup.GetNestedType("<>c__DisplayClass36_0", BindingFlags.Public | BindingFlags.NonPublic);
                            if (tDisplay != null)
                            {
                                var method = tDisplay.GetMethod("<NewPopupMessageAsync>b__0", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                if (method != null)
                                {
                                    List<string> lines = new List<string>();
                                    lines.Add("Instructions for <>c__DisplayClass36_0.<NewPopupMessageAsync>b__0:");
                                    var instructions = HarmonyLib.PatchProcessor.GetOriginalInstructions(method);
                                    foreach (var inst in instructions)
                                    {
                                        if (inst.opcode == System.Reflection.Emit.OpCodes.Call || inst.opcode == System.Reflection.Emit.OpCodes.Callvirt)
                                        {
                                            lines.Add("  Call: " + inst.operand?.ToString());
                                        }
                                    }
                                    File.WriteAllLines(logPath, lines.ToArray(), Encoding.UTF8);
                                }
                                else
                                {
                                    File.WriteAllText(logPath, "Method <NewPopupMessageAsync>b__0 NOT FOUND", Encoding.UTF8);
                                }
                            }
                            else
                            {
                                File.WriteAllText(logPath, "Nested display class NOT FOUND", Encoding.UTF8);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        File.WriteAllText(Path.Combine(GetModPath(), "lambda_calls.txt"), "Error: " + ex.ToString(), Encoding.UTF8);
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[RussianLocalization] PatchScreenBufferDynamic error: " + ex.ToString());
            }
        }

        public static void ScreenBuffer_Generic_Prefix(System.Reflection.MethodBase __originalMethod, object[] __args)
        {
            try
            {
                if (!Initialized || !IsEnabled) return;
                if (__args == null || __args.Length == 0) return;
                if (__originalMethod == null) return;

                var parameters = __originalMethod.GetParameters();
                for (int i = 0; i < parameters.Length && i < __args.Length; i++)
                {
                    if (parameters[i].ParameterType == typeof(string) || parameters[i].ParameterType == typeof(string).MakeByRefType())
                    {
                        string s = __args[i] as string;
                        if (string.IsNullOrEmpty(s)) continue;

                        // FIX B3 (2026-07-20): дебаг-запись в screenbuffer_text_debug.txt — только при флаге
                        if (DebugFileLogging && (s.Contains("Perfect") || s.Contains("flaming") || s.Contains("features") || s.Contains("bite")))
                        {
                            try
                            {
                                File.AppendAllText(Path.Combine(CachedModPath, "screenbuffer_text_debug.txt"),
                                    "Method: " + __originalMethod.Name + " (string) | Arg: '" + s + "'\n", Encoding.UTF8);
                            }
                            catch {}
                        }

                        if (ContainsCyrillic(s))
                        {
                            __args[i] = Transliterate(s);
                            continue;
                        }

                        string trans = Translate(s);
                        if (!string.IsNullOrEmpty(trans) && trans != s)
                        {
                            string transliterated = Transliterate(trans);
                            __args[i] = transliterated;
                        }
                    }
                    else if (parameters[i].ParameterType == typeof(string[]))
                    {
                        string[] arr = __args[i] as string[];
                        if (arr == null || arr.Length == 0) continue;

                        for (int j = 0; j < arr.Length; j++)
                        {
                            string s = arr[j];
                            if (string.IsNullOrEmpty(s)) continue;

                            // FIX B3 (2026-07-20): дебаг-запись в screenbuffer_text_debug.txt — только при флаге
                            if (DebugFileLogging && (s.Contains("Perfect") || s.Contains("flaming") || s.Contains("features") || s.Contains("bite")))
                            {
                                try
                                {
                                    File.AppendAllText(Path.Combine(CachedModPath, "screenbuffer_text_debug.txt"),
                                        "Method: " + __originalMethod.Name + " (string[]) | Arg[" + j + "]: '" + s + "'\n", Encoding.UTF8);
                                }
                                catch {}
                            }

                            if (ContainsCyrillic(s))
                            {
                                arr[j] = Transliterate(s);
                                continue;
                            }

                            string tr = Translate(s);
                            if (!string.IsNullOrEmpty(tr) && tr != s)
                            {
                                arr[j] = Transliterate(tr);
                            }
                        }
                    }
                    else if (parameters[i].ParameterType == typeof(System.Text.StringBuilder))
                    {
                        System.Text.StringBuilder sbArg = __args[i] as System.Text.StringBuilder;
                        if (sbArg == null || sbArg.Length == 0) continue;

                        string s = sbArg.ToString();
                        if (string.IsNullOrEmpty(s)) continue;

                        // FIX B3 (2026-07-20): дебаг-запись в screenbuffer_text_debug.txt — только при флаге
                        if (DebugFileLogging && (s.Contains("Perfect") || s.Contains("flaming") || s.Contains("features") || s.Contains("bite")))
                        {
                            try
                            {
                                File.AppendAllText(Path.Combine(CachedModPath, "screenbuffer_text_debug.txt"),
                                    "Method: " + __originalMethod.Name + " (StringBuilder) | Arg: '" + s + "'\n", Encoding.UTF8);
                            }
                            catch {}
                        }

                        if (ContainsCyrillic(s))
                        {
                            string transliterated = Transliterate(s);
                            sbArg.Clear();
                            sbArg.Append(transliterated);
                            continue;
                        }

                        string tr = Translate(s);
                        if (!string.IsNullOrEmpty(tr) && tr != s)
                        {
                            string transliterated = Transliterate(tr);
                            sbArg.Clear();
                            sbArg.Append(transliterated);
                        }
                    }
                }
            }
            catch { }
        }

        // Универсальный prefix: переводит все string-параметры Popup-методов.
        // __originalMethod — MethodInfo текущего метода (магическое имя Harmony 2.x).
        // __args — массив всех аргументов; модификация __args[i] меняет то, что получит оригинал.
        public static void Popup_Generic_Prefix(System.Reflection.MethodBase __originalMethod, object[] __args)
        {
            try
            {
                if (!Initialized || !IsEnabled) return;
                if (__args == null || __args.Length == 0) return;
                if (__originalMethod == null) return;

                var parameters = __originalMethod.GetParameters();
                for (int i = 0; i < parameters.Length && i < __args.Length; i++)
                {
                    // FIX B2 (2026-07-20): служебные параметры — ключи логики игры
                    // (Commands/Hotkeys/CommandLine) не переводим: игра сравнивает их
                    // как внутренние ID. Переводятся только отображаемые тексты.
                    string pName = parameters[i].Name;
                    if (!string.IsNullOrEmpty(pName) && PopupServiceParamNames.Contains(pName)) continue;

                    if (parameters[i].ParameterType == typeof(string))
                    {
                        string s = __args[i] as string;
                        if (string.IsNullOrEmpty(s)) continue;

                        // FIX B3 (2026-07-20): дебаг-запись в popup_args_debug.txt — только при флаге
                        if (DebugFileLogging)
                        {
                            try
                            {
                                File.AppendAllText(Path.Combine(CachedModPath, "popup_args_debug.txt"),
                                    "Method: " + __originalMethod.Name + " | Arg: '" + s + "'\n", Encoding.UTF8);
                            }
                            catch {}
                        }

                        if (ContainsCyrillic(s)) continue;
                        string tr = Translate(s);
                        if (!string.IsNullOrEmpty(tr) && tr != s)
                            __args[i] = tr;
                    }
                    else if (parameters[i].ParameterType == typeof(System.Collections.Generic.IReadOnlyList<string>))
                    {
                        var list = __args[i] as System.Collections.Generic.IReadOnlyList<string>;
                        if (list == null || list.Count == 0) continue;
                        var newList = new System.Collections.Generic.List<string>(list.Count);
                        bool changed = false;
                        for (int j = 0; j < list.Count; j++)
                        {
                            string s = list[j];
                            if (string.IsNullOrEmpty(s) || ContainsCyrillic(s))
                            {
                                newList.Add(s);
                                continue;
                            }
                            string tr = Translate(s);
                            if (!string.IsNullOrEmpty(tr) && tr != s)
                            {
                                newList.Add(tr);
                                changed = true;
                            }
                            else
                                newList.Add(s);
                        }
                        if (changed)
                            __args[i] = newList.AsReadOnly();
                    }
                    else if (parameters[i].ParameterType == typeof(System.Collections.Generic.List<string>))
                    {
                        var list = __args[i] as System.Collections.Generic.List<string>;
                        if (list == null || list.Count == 0) continue;
                        for (int j = 0; j < list.Count; j++)
                        {
                            string s = list[j];
                            if (string.IsNullOrEmpty(s) || ContainsCyrillic(s)) continue;
                            string tr = Translate(s);
                            if (!string.IsNullOrEmpty(tr) && tr != s)
                                list[j] = tr;
                        }
                    }
                    else if (parameters[i].ParameterType == typeof(string[]))
                    {
                        var arr = __args[i] as string[];
                        if (arr == null || arr.Length == 0) continue;
                        for (int j = 0; j < arr.Length; j++)
                        {
                            string s = arr[j];
                            if (string.IsNullOrEmpty(s) || ContainsCyrillic(s)) continue;
                            string tr = Translate(s);
                            if (!string.IsNullOrEmpty(tr) && tr != s)
                                arr[j] = tr;
                        }
                    }
                }
            }
            catch { /* не ломаем popup */ }
        }

        // Postfix: переводит возвращаемую строку (AskString, AskNumber возвращают string).
        // FIX B1 (2026-07-20): __originalMethod добавлен для страховочного фильтра — результат
        // методов из PopupPostfixSkipMethods (ввод пользователя) не переводим ни при каких условиях.
        public static void Popup_Generic_Postfix(ref string __result, System.Reflection.MethodBase __originalMethod)
        {
            try
            {
                if (!Initialized || !IsEnabled) return;
                if (__originalMethod != null && PopupPostfixSkipMethods.Contains(__originalMethod.Name)) return;
                if (string.IsNullOrEmpty(__result)) return;
                if (ContainsCyrillic(__result)) return;
                string tr = Translate(__result);
                if (!string.IsNullOrEmpty(tr) && tr != __result)
                    __result = tr;
            }
            catch { /* не ломаем popup */ }
        }

        // ============================================================
        // ПАТЧ XRL.GameText.VariableReplace — подстановка плейсхолдеров (≥ 2.0.211)
        // 11 перегрузок, 78 вызовов в коде. Все возвращают string.
        // Postfix переводит __result, если в нём остался английский.
        // ============================================================
        public static void PatchGameText()
        {
            try
            {
                System.Type t = null;
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    try { t = asm.GetType("XRL.GameText"); } catch { }
                    if (t != null) break;
                }
                if (t == null)
                {
                    UnityEngine.Debug.Log("[RussianLocalization] XRL.GameText not found — GameText hook skipped.");
                    return;
                }

                if (AnotherInstallAlreadyPatched("com.russianlocalization.gametext", "PatchGameText")) return;
                
                var harmony = new Harmony("com.russianlocalization.gametext");
                int patched = 0;

                var postfixMethod = typeof(TranslationEngine).GetMethod("GameText_VariableReplace_Postfix",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                var prefixMethod = typeof(TranslationEngine).GetMethod("GameText_VariableReplace_Prefix",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                int prefixed = 0;

                var methods = t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                foreach (var m in methods)
                {
                    if (m == null) continue;
                    if (m.IsGenericMethodDefinition) continue;
                    if (m.Name != "VariableReplace") continue;
                    if (m.ReturnType != typeof(string)) continue;

                    // Пропускаем, если уже есть postfix с нашим ID
                    var existing = Harmony.GetPatchInfo(m);
                    if (existing != null && existing.Postfixes != null)
                    {
                        bool alreadyPatched = false;
                        foreach (var pf in existing.Postfixes)
                        {
                            if (pf != null && pf.owner == "com.russianlocalization.gametext")
                            {
                                alreadyPatched = true; break;
                            }
                        }
                        if (alreadyPatched) continue;
                    }

                    try
                    {
                        // Prefix вешаем только на перегрузки, где первый параметр — именно
                        // `string Message`: Harmony связывает параметры префикса ПО ИМЕНИ, а для
                        // перегрузок с `StringBuilder Message` сигнатура `ref string` не подойдёт
                        // (их оставляем постфиксу — они всё равно проходят через ту же логику).
                        var ps = m.GetParameters();
                        bool stringMessageFirst = ps.Length > 0
                            && ps[0].ParameterType == typeof(string)
                            && ps[0].Name == "Message";

                        harmony.Patch(m,
                            prefix: (stringMessageFirst && prefixMethod != null) ? new HarmonyMethod(prefixMethod) : null,
                            postfix: new HarmonyMethod(postfixMethod));
                        patched++;
                        if (stringMessageFirst && prefixMethod != null) prefixed++;
                    }
                    catch (Exception exPatch)
                    {
                        UnityEngine.Debug.LogWarning("[RussianLocalization] Failed to patch GameText." + m.Name + ": " + exPatch.Message);
                    }
                }

                // Также патчим ReplaceBuilder.Execute() и ReplaceBuilder.ToString()
                // — если код использует ReplaceBuilder напрямую (минуя VariableReplace).
                System.Type rbType = null;
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    try { rbType = asm.GetType("XRL.World.Text.ReplaceBuilder"); } catch { }
                    if (rbType != null) break;
                }
                if (rbType != null)
                {
                    // Execute() возвращает ReplaceBuilder (this), не string — пропускаем.
                    // ToString() возвращает string — патчим postfix.
                    // ВАЖНО: DeclaredOnly обязателен. Без него, если ReplaceBuilder не
                    // переопределяет ToString(), GetMethod вернёт унаследованный
                    // System.Object.ToString(), и Harmony пропатчит его ГЛОБАЛЬНО —
                    // postfix будет вызываться на каждый ToString() во всём процессе,
                    // включая вызовы внутри самого Translate(), что даёт бесконечную
                    // рекурсию → StackOverflowException (нативный краш без записи в лог).
                    var toString = rbType.GetMethod("ToString",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly,
                        null, System.Type.EmptyTypes, null);
                    if (toString != null && toString.DeclaringType == rbType && toString.ReturnType == typeof(string) && postfixMethod != null)
                    {
                        var existing = Harmony.GetPatchInfo(toString);
                        if (existing == null || existing.Postfixes == null || existing.Postfixes.Count == 0)
                        {
                            try
                            {
                                harmony.Patch(toString, postfix: new HarmonyMethod(postfixMethod));
                                patched++;
                            }
                            catch { }
                        }
                    }
                }

                UnityEngine.Debug.Log("[RussianLocalization] Patched XRL.GameText.VariableReplace, methods patched = " + patched +
                    " (из них с prefix-перехватом шаблона = " + prefixed + ").");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[RussianLocalization] PatchGameText error: " + ex.ToString());
            }
        }

        // 2026-07-17: PREFIX для GameText.VariableReplace — ЛОВИМ ШАБЛОН ДО ПОДСТАНОВКИ.
        //
        // Зачем. VariableReplace — шаблонизатор игры: берёт "The villagers of =village= demanded…"
        // и подставляет =village=. Раньше мод висел на нём ТОЛЬКО постфиксом, т.е. видел строку
        // уже ПОСЛЕ подстановки — шаблон к этому моменту уничтожен. Из-за этого 10 459 ключей-
        // шаблонов в dictionary.json (с =name=, =pronouns.possessive= и т.д.) не могли совпасть
        // НИКОГДА: замер по логам — из 24 115 строк на входе мода переменные есть лишь в 5 (0%).
        // Мод был вынужден восстанавливать шаблоны регэкспами (pattern_dictionary) — бесконечная
        // погоня за результатами вместо конечного набора шаблонов.
        //
        // Как работает. Prefix подменяет Message на русский шаблон, а подставляет переменные уже
        // сама игра — в русский текст. Postfix при этом ОСТАЁТСЯ и становится этапом падежей:
        //   шаблон "…{{case:=name=|gen|auto|sg}}" -> игра -> "…{{case:Наппир|gen|auto|sg}}"
        //   -> postfix -> Translate -> ApplyMorphMarkers -> "…Наппира"
        //
        // Безопасность. Подменяем ТОЛЬКО из templateDictionary, куда попадают записи с точно
        // совпавшим набором имён переменных (см. BuildTemplateDictionary). Это правка ПОВЕДЕНИЯ
        // игры, а не отображения: битый синтаксис переменных сломал бы подстановку, а не текст.
        public static void GameText_VariableReplace_Prefix(ref string Message)
        {
            try
            {
                if (!Initialized || !IsEnabled) return;
                if (string.IsNullOrEmpty(Message)) return;
                // Быстрый выход: без '=' это не шаблон, а обычная строка — её сделает postfix.
                if (Message.IndexOf('=') < 0) return;
                string ru;
                if (templateDictionary.TryGetValue(Message, out ru) && !string.IsNullOrEmpty(ru))
                {
                    Message = ru;
                }
            }
            catch { /* не ломаем GameText */ }
        }

        // Postfix для всех перегрузок GameText.VariableReplace и ReplaceBuilder.ToString().
        // Переводит __result, если в нём остался непереведённый английский.
        public static void GameText_VariableReplace_Postfix(ref string __result)
        {
            try
            {
                if (!Initialized || !IsEnabled) return;
                if (string.IsNullOrEmpty(__result)) return;
                // Быстрый пропуск: если уже есть кириллица и нет английского — переведено.
                if (ContainsCyrillic(__result))
                {
                    bool hasCyr, hasEng;
                    ScanAlpha(__result, out hasCyr, out hasEng);
                    if (hasCyr && !hasEng) return;
                }
                string tr = Translate(__result);
                if (!string.IsNullOrEmpty(tr) && tr != __result)
                    __result = tr;
            }
            catch { /* не ломаем GameText */ }
        }

        // 2026-07-06 (v13 — НАЙДЕН РЕАЛЬНЫЙ ВИНОВНИК): бисекция по одному хуку (PatchPopup,
        // PatchQudTranslator, PatchUITextSkin, PatchGameText) показала, что краш в торговле
        // воспроизводится ТОЛЬКО когда включён PatchUITextSkin — единственный из четырёх хуков,
        // чьи префиксы вызывали тяжёлый TranslationEngine.Translate() БЕЗ какой-либо защиты от
        // глубины стека (в отличие от Description_Patches/ScreenBuffer_Patch/TMP-группы, где такая
        // защита уже была добавлена ранее). UITextSkin.SetText — самый "горячий" путь перевода
        // Modern UI (вызывается на КАЖДЫЙ текстовый элемент), и при построении большого списка
        // предметов в торговле UI Toolkit строит глубокое дерево элементов рекурсивно — каждый
        // вызов Translate() добавляет стек поверх и без того глубокого стека движка. Дело не в
        // рекурсии САМОГО патча (guard с [ThreadStatic] тут не поможет и не нужен), а в суммарной
        // глубине стека к моменту вызова — поэтому используем RuntimeHelpers.EnsureSufficientExecutionStack(),
        // которая кидает ПЕРЕХВАТЫВАЕМОЕ исключение заранее, пока стек ещё не переполнен целиком
        // (в отличие от жёсткого access violation, который ловить/логировать невозможно).
        // 2026-07-06 (v14 — ДИАГНОСТИКА): фикс EnsureSufficientExecutionStack НЕ помог, краш
        // идентичен побайтово (те же смещения в gameoverlayrenderer64.dll/mono/UnityPlayer каждый
        // раз) — это не похоже на переполнение стека (которое давало бы разброс по глубине).
        // Тестируем радикальную гипотезу: сам факт патчинга Harmony-трамплином UITextSkin.SetText
        // (независимо от того, что делает наш код внутри) меняет тайминг вызова и задевает гонку
        // в потоке рендера Unity (kGfxThreadingModeThreaded) или в хуке Steam-оверлея. Временно
        // делаем тело патча ПОЛНОСТЬЮ пустым (return сразу, без Translate/рефлексии) — если краш
        // всё равно происходит, виноват сам факт патчинга метода, а не наша логика внутри.
        // 2026-07-06 (v15): пустое тело ОБОИХ методов — краша НЕТ. Значит дело в логике, не в
        // самом факте патчинга. Разделяем на два отдельных флага: включаем логику только в
        // SetText_Prefix (самый частый путь — вызывается на каждый текстовый элемент), оставляя
        // SetTheText_Prefix (редкий фолбэк через рефлексию) пустым, чтобы понять, кто из двух виноват.
        public const bool DIAG_NOOP_SETTEXT_BODY = false;
        public const bool DIAG_NOOP_SETTHETEXT_BODY = false;
        // 2026-07-06 (v16): SetText_Prefix с полной логикой (EnsureSufficientExecutionStack +
        // Translate) — краш ВОСПРОИЗВЁЛСЯ (тот же offset). SetTheText_Prefix пустой, значит виноват
        // либо EnsureSufficientExecutionStack, либо сам Translate(). Другие хуки (GameText,
        // QudTranslator) тоже зовут Translate() и не крашились — значит либо дело именно в
        // Translate()'s поведении с ТЕКСТОМ ИЗ ТОРГОВЛИ конкретно (не общее свойство функции), либо
        // в том, что тяжёлая обработка внутри PREFIX (до вызова оригинального SetText) удерживает
        // что-то (лок/синхронизацию с потоком рендера) дольше обычного. Тестируем самый дешёвый
        // возможный путь — только проверка стека + быстрая ContainsCyrillicInternal, БЕЗ Translate().
        public const bool DIAG_SKIP_TRANSLATE_CALL = false;
        // 2026-07-06 (v17): DIAG_SKIP_TRANSLATE_CALL=true — краша НЕТ. Подтверждено: виноват именно
        // вызов Translate(), не сам факт патчинга SetText. Пользователь указал, что краша не было
        // до апдейта 1.0.5 (build 211.45), где патчноут явно упоминает переделку Modern UI
        // ("Fixed a bug that caused the Modern UI option not to be shown"). Гипотеза: в новой версии
        // UITextSkin.SetText может вызываться НЕ ТОЛЬКО из главного потока Unity (например, из
        // воркера построения дерева UI Toolkit), и наш тяжёлый Translate() внутри Harmony-префикса
        // становится небезопасным именно в этом контексте (не в главном потоке). Логируем один раз
        // при первом вызове не из главного потока, и пропускаем Translate() в этом случае —
        // единственный "правильный" перевод в фоновом потоке всё равно рискован.
        private static volatile bool _loggedOffMainThread = false;
        // 2026-07-06 (v21): диагностическое логирование входных строк SetText для поимки
        // конкретной строки, вызывающей краш в торговле. Файл переоткрывается на каждую запись
        // (AppendAllText) — гарантированный flush на диск, последняя строка = виновник краша.
        public const bool DIAG_LOG_SETTEXT_INPUT = false;
        public static readonly string DIAG_SETTEXT_LOG_PATH =
            System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
                "CavesOfQud_RU_Logs", "settext_input_trace.txt");
        // Перехват входящего текста до отрисовки в новом UI.
        public static void UITextSkin_SetText_Prefix(ref string text)
        {
            if (DIAG_NOOP_SETTEXT_BODY) return;
            try
            {
                System.Runtime.CompilerServices.RuntimeHelpers.EnsureSufficientExecutionStack();
                if (!TranslationEngine.Initialized || string.IsNullOrEmpty(text)) return;
                
                bool hasCyr, hasEng;
                ScanAlpha(text, out hasCyr, out hasEng);
                if (hasCyr && !hasEng) return;

                if (DIAG_SKIP_TRANSLATE_CALL) return;
                if (TranslationEngine.MainThreadId != -1 &&
                    System.Threading.Thread.CurrentThread.ManagedThreadId != TranslationEngine.MainThreadId)
                {
                    if (!_loggedOffMainThread)
                    {
                        _loggedOffMainThread = true;
                        UnityEngine.Debug.LogWarning("[RussianLocalization] UITextSkin.SetText вызван НЕ из главного потока (thread " +
                            System.Threading.Thread.CurrentThread.ManagedThreadId + ", главный = " + TranslationEngine.MainThreadId +
                            ") — пропускаем Translate() в этом вызове.");
                    }
                    return;
                }
                // 2026-07-06 (v18): проверяем гипотезу "дело в ДЛИТЕЛЬНОСТИ вызова, а не в его
                // логике" — вместо настоящего Translate() делаем паузу той же длительности и
                // возвращаемся без перевода. Если краш всё равно случится — виноват тайминг (гонка
                // с чем-то ещё), а не код Translate() как таковой.
                if (DIAG_FAKE_DELAY_MS > 0)
                {
                    System.Threading.Thread.Sleep(DIAG_FAKE_DELAY_MS);
                    return;
                }
                // 2026-07-06 (v21): ХВАТИТ ГАДАТЬ. Логируем КАЖДУЮ входную строку в файл с
                // немедленным flush ПЕРЕД вызовом Translate(). Краш синхронный внутри Translate(),
                // поэтому последняя строка в diag-файле = ровно тот текст, что убил игру.
                // IN: до вызова, OUT: после. Виновник краша = строка с IN без соответствующего OUT.
                string escForLog = null;
                if (DIAG_LOG_SETTEXT_INPUT)
                {
                    try
                    {
                        escForLog = text.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
                        System.IO.File.AppendAllText(DIAG_SETTEXT_LOG_PATH,
                            System.DateTime.Now.ToString("HH:mm:ss.fff") + "\tIN \t[len=" + text.Length + "]\t" + escForLog + System.Environment.NewLine);
                    }
                    catch { }
                }
                // Полный пайплайн (faction + паттерны по целой строке + радужные слова + кэш),
                // как у остальных хуков. TranslateMarkup дробил по цвету и терял паттерны → франкенштейны.
                string tr = TranslationEngine.Translate(text);
                if (!string.IsNullOrEmpty(tr) && tr != text) text = tr;
                if (DIAG_LOG_SETTEXT_INPUT)
                {
                    try
                    {
                        System.IO.File.AppendAllText(DIAG_SETTEXT_LOG_PATH,
                            System.DateTime.Now.ToString("HH:mm:ss.fff") + "\tOUT\t[len=" + text.Length + "]\t" + escForLog + System.Environment.NewLine);
                    }
                    catch { }
                }
            }
            catch { /* никогда не ломаем UI, включая InsufficientExecutionStackException */ }
        }

        public const int DIAG_FAKE_DELAY_MS = 0;

        // Мемо «мы сами это уже перевели»: instance -> последняя строка, которую мы записали в .text.
        // Apply() зовётся не только из setData, но и из Start() и из Updated() (GetPreferredValues),
        // а переведённая справка всё равно содержит латиницу (имена команд, "Alt"), то есть дешёвый
        // guard hasCyr && !hasEng её не отсечёт. Сравнение по ССЫЛКЕ (ReferenceEquals) стоит копейки
        // и снимает повторный Translate() многокилобайтной страницы на каждом Apply.
        // ConditionalWeakTable не держит UITextSkin от сборки мусора.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, string> _utsLastApplied =
            new System.Runtime.CompilerServices.ConditionalWeakTable<object, string>();

        // Длина, начиная с которой блок из UITextSkin.Apply() переводится ТОЛЬКО точным
        // совпадением по словарю, без пословной обработки (см. UITextSkin_SetTheText_Prefix).
        // 300 — ниже самой короткой страницы справки (Action Costs, 352 символа) и заметно выше
        // обычных подписей UI.
        private const int UITextSkinExactOnlyThreshold = 300;

        /// <summary>
        /// Только точное совпадение по словарю, без пословного прохода: один поиск в хеше.
        /// Ведущие/замыкающие пробелы оригинала сохраняются (ключи в словаре — .Trim()'нутые).
        /// Используется для больших статичных блоков (страницы справки), где полный Translate()
        /// неприемлемо дорог — см. UITextSkin_SetTheText_Prefix и XRLManualPage_GetData_Patch.
        /// </summary>
        public static bool TryTranslateExactPreservingPadding(string text, out string result)
        {
            result = null;
            if (string.IsNullOrEmpty(text)) return false;

            string trimmed = text.Trim();
            string exact;
            if (trimmed.Length == 0 || !staticDictionary.TryGetValue(trimmed, out exact)) return false;

            int lead = 0;
            while (lead < text.Length && char.IsWhiteSpace(text[lead])) lead++;
            int trail = 0;
            while (trail < text.Length - lead && char.IsWhiteSpace(text[text.Length - 1 - trail])) trail++;

            result = text.Substring(0, lead) + exact + text.Substring(text.Length - trail);
            return true;
        }

        // Фолбэк: если текст записали прямо в поле .text, минуя SetText — переводим перед применением.
        // Висит на UITextSkin.Apply() (см. PatchUITextSkin): именно так рисуется экран справки
        // (Qud.UI.HelpRow) и другие места, пишущие в поле напрямую.
        public static void UITextSkin_SetTheText_Prefix(object __instance)
        {
            if (DIAG_NOOP_SETTHETEXT_BODY) return;
            try
            {
                System.Runtime.CompilerServices.RuntimeHelpers.EnsureSufficientExecutionStack();
                if (!TranslationEngine.Initialized || _utsTextField == null || __instance == null) return;
                string cur = _utsTextField.GetValue(__instance) as string;
                if (string.IsNullOrEmpty(cur)) return;

                string lastApplied;
                if (_utsLastApplied.TryGetValue(__instance, out lastApplied) && ReferenceEquals(lastApplied, cur)) return;

                bool hasCyr, hasEng;
                ScanAlpha(cur, out hasCyr, out hasEng);
                if (hasCyr && !hasEng) return;

                // 2026-07-28: ПРЕДОХРАНИТЕЛЬ ПРОТИВ ЗАВИСАНИЯ НА БОЛЬШИХ БЛОКАХ.
                //
                // Этот хук — не редкий фолбэк, а массовый путь: при открытии справки
                // Qud.UI.HelpScreen создаёт строки сразу для ВСЕХ топиков Manual.xml (~24 КБ),
                // и все они прилетают сюда синхронно на одном кадре. Полный Translate() режет
                // такой текст на строки и гоняет каждую по patternDictionary (~6100 регулярок) —
                // это сотни тысяч сопоставлений за кадр, игра просто виснет.
                //
                // Поэтому большие блоки переводим ТОЛЬКО точным совпадением по словарю: один
                // поиск в хеше, никакого пословного прохода. Для справки это и есть рабочая
                // схема — целостраничные ключи из Manual.xml. Короткие подписи (кнопки,
                // заголовки, ярлыки) идут полным путём как раньше.
                if (cur.Length >= UITextSkinExactOnlyThreshold)
                {
                    string bigResult;
                    if (!TryTranslateExactPreservingPadding(cur, out bigResult)) return;

                    _utsTextField.SetValue(__instance, bigResult);
                    _utsLastApplied.Remove(__instance);
                    _utsLastApplied.Add(__instance, bigResult);
                    return;
                }

                string tr = TranslationEngine.Translate(cur);
                if (!string.IsNullOrEmpty(tr) && tr != cur)
                {
                    _utsTextField.SetValue(__instance, tr);
                    _utsLastApplied.Remove(__instance);
                    _utsLastApplied.Add(__instance, tr);
                }
            }
            catch { /* никогда не ломаем UI, включая InsufficientExecutionStackException */ }
        }

        // --- ОПТИМИЗИРОВАННЫЙ RuntimeTranslator ДЛЯ MODERN UI ---
        //
        // Modern UI в Caves of Qud 2.0.211.46 записывает текст в TextElement через путь,
        // который Harmony-патчи не перехватывают (прямой binding через Unity Localization
        // или аналогичный internal API). Только активный обход VisualElement-дерева даёт
        // перевод. Делаем его лёгким:
        //
        //   - Интервал 1.5с — Modern UI меняет текст редко.
        //   - Кеш по identity элемента (System.Object) — повторно не переводим.
        //   - Только активный UIDocument (он один на сцену во время Modern UI).
        //   - Жёсткий try/catch — один битый элемент не ломает весь обход.

        private static RuntimeTranslator _runtimeTranslator;
        private static GameObject _runtimeTranslatorGO;

        public static void EnsureRuntimeTranslator()
        {
            try
            {
                if (_runtimeTranslator != null && _runtimeTranslatorGO != null) return;

                _runtimeTranslatorGO = new GameObject("RussianLocalization_RuntimeTranslator");
                UnityEngine.Object.DontDestroyOnLoad(_runtimeTranslatorGO);
                _runtimeTranslator = _runtimeTranslatorGO.AddComponent<RuntimeTranslator>();
                UnityEngine.Debug.Log("[RussianLocalization] RuntimeTranslator spawned (catch-async-UIToolkit fallback).");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[RussianLocalization] EnsureRuntimeTranslator error: " + ex.ToString());
            }
        }

        public class RuntimeTranslator : MonoBehaviour
        {
            private static readonly HashSet<int> _translatedRefs = new HashSet<int>(1024);
            private static readonly System.Collections.Generic.Dictionary<int, string> _tmpLastText = new System.Collections.Generic.Dictionary<int, string>();
            private static RuntimeTranslator _instance;
            private static System.Type _cachedTextElementType;
            private static System.Type _cachedUIDocumentType;
            private static System.Reflection.PropertyInfo _rootProp;
            private static System.Reflection.MethodInfo _findDocumentMethod;
            private static bool _typesResolved;
            private bool _fullScanDone;

            // --- Оптимизация: троттлинг + кэш рефлексии (фикс просадки FPS) ---
            private float _scanAccum;
            private const float ScanIntervalSeconds = 0.25f;
            private static System.Reflection.PropertyInfo _textPropInfo;
            private static System.Reflection.PropertyInfo _childrenPropInfo;
            private static readonly object[] EmptyArgs = new object[0];
            private static bool _scanDiagLogged;
            private static int _diagTranslatedCount;

            public static void TriggerRescan()
            {
                if (_instance != null)
                {
                    _instance._fullScanDone = false;
                }
            }

            private void Start()
            {
                _instance = this;
                ResolveUITypes();
            }

            private static void ResolveUITypes()
            {
                if (_typesResolved) return;
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        if (_cachedUIDocumentType == null) _cachedUIDocumentType = asm.GetType("UnityEngine.UIElements.UIDocument");
                        if (_cachedTextElementType == null) _cachedTextElementType = asm.GetType("UnityEngine.UIElements.TextElement");
                        if (_cachedUIDocumentType != null && _cachedTextElementType != null)
                        {
                            _rootProp = _cachedUIDocumentType.GetProperty("rootVisualElement",
                                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                            ResolveFindMethod();
                            _typesResolved = true;
                            break;
                        }
                    }
                    catch { }
                }
            }

            private static void ResolveFindMethod()
            {
                var findMethods = typeof(UnityEngine.Object).GetMethods(
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                for (int i = 0; i < findMethods.Length; i++)
                {
                    var m = findMethods[i];
                    if (m.Name != "FindObjectsOfType" || !m.IsGenericMethodDefinition) continue;
                    if (m.GetParameters().Length == 0)
                    {
                        _findDocumentMethod = m.MakeGenericMethod(_cachedUIDocumentType);
                        break;
                    }
                }
            }

            private void LateUpdate()
            {
                if (!TranslationEngine.Initialized || !_typesResolved) return;

                // 2026-07-19: Безопасно запускаем сканирование только во время активной игры (сцена "Game"),
                // чтобы избежать крашей Unity/Mono при сканировании объектов во время загрузки blueprints.
                try
                {
                    if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Game") return;
                }
                catch { return; }

                // Полный разовый проход по TMP_Text при старте; UIElements ловим ниже по таймеру.
                if (!_fullScanDone)
                {
                    try { ScanAllTMPText(); } catch { }
                    _fullScanDone = true;
                }

                // Троттлинг: обходим дерево UI не каждый кадр, а раз в ScanIntervalSeconds.
                // Это убирает просадку FPS — задержка 0.25с глазу незаметна.
                _scanAccum += Time.unscaledDeltaTime;
                if (_scanAccum < ScanIntervalSeconds) return;
                _scanAccum = 0f;

                try { ScanAllActiveUIDocuments(); } catch { }
            }

            private static void ScanAllTMPText()
            {
                var all = Resources.FindObjectsOfTypeAll<TMPro.TMP_Text>();
                if (all == null || all.Length == 0) return;

                foreach (var t in all)
                {
                    if (t == null) continue;
                    int id;
                    try { id = t.GetInstanceID(); }
                    catch { continue; }

                    string cur;
                    try { cur = t.text; }
                    catch { continue; }
                    if (string.IsNullOrEmpty(cur)) continue;

                    if (_tmpLastText.TryGetValue(id, out string lastText) && lastText == cur) continue;
                    _tmpLastText[id] = cur;

                    if (!HasEnglish(cur)) continue;

                    string translated;
                    try { translated = TranslationEngine.Translate(cur); }
                    catch { continue; }
                    if (!string.IsNullOrEmpty(translated) && translated != cur)
                    {
                        try 
                        {
                            t.text = translated;
                            _tmpLastText[id] = translated;
                        }
                        catch { }
                    }
                }
            }

            

            // Обходит ВСЕ активные UIDocument'ы (док, лист персонажа, оверлеи) раз в тик.
            // Вечный кэш _translatedRefs здесь НЕ используется: вместо него — дешёвая проверка
            // HasEnglish (уже переведённый кириллический текст пропускается без перевода),
            // чтобы динамически обновляемый текст (лог, HP, описания) тоже подхватывался.
            private static void ScanAllActiveUIDocuments()
            {
                // 1. Прямое сканирование всех активных UI Toolkit панелей через UIElementsUtility
                int panelCount = 0;
                try
                {
                    System.Type utilityType = null;
                    foreach (var ass in System.AppDomain.CurrentDomain.GetAssemblies())
                    {
                        utilityType = ass.GetType("UnityEngine.UIElements.UIElementsUtility");
                        if (utilityType != null) break;
                    }
                    if (utilityType != null)
                    {
                        // Сканируем кэш-словарь
                        var cacheField = utilityType.GetField("s_UIElementsCache", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                        if (cacheField != null)
                        {
                            var dict = cacheField.GetValue(null) as System.Collections.IDictionary;
                            if (dict != null)
                            {
                                var values = dict.Values;
                                foreach (var panel in values)
                                {
                                    if (panel == null) continue;
                                    var visualTreeProp = panel.GetType().GetProperty("visualTree", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                                    if (visualTreeProp == null) continue;
                                    var root = visualTreeProp.GetValue(panel);
                                    if (root == null) continue;
                                    WalkVisualTree(root, 0);
                                    panelCount++;
                                }
                            }
                        }

                        // Сканируем список для итерации (в зависимости от внутреннего состояния Unity)
                        var listField = utilityType.GetField("s_PanelsIterationList", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                        if (listField != null)
                        {
                            var list = listField.GetValue(null) as System.Collections.IList;
                            if (list != null)
                            {
                                foreach (var panel in list)
                                {
                                    if (panel == null) continue;
                                    var visualTreeProp = panel.GetType().GetProperty("visualTree", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                                    if (visualTreeProp == null) continue;
                                    var root = visualTreeProp.GetValue(panel);
                                    if (root == null) continue;
                                    WalkVisualTree(root, 0);
                                    panelCount++;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    try { System.IO.File.WriteAllText("C:\\Users\\Lecoo\\AppData\\LocalLow\\Freehold Games\\CavesOfQud\\uielements_debug_err.txt", ex.ToString()); } catch {}
                }

                if (_rootProp == null || _cachedUIDocumentType == null) return;

                UnityEngine.Object[] docs = null;

                // 2. Предпочтительно: генерик FindObjectsOfType<UIDocument>() — все активные документы.
                if (_findDocumentMethod != null)
                {
                    try { docs = _findDocumentMethod.Invoke(null, EmptyArgs) as UnityEngine.Object[]; }
                    catch { docs = null; }
                }

                // 3. Фолбэк: не-генерик плюрал.
                if (docs == null || docs.Length == 0)
                {
                    try { docs = UnityEngine.Object.FindObjectsOfType(_cachedUIDocumentType); }
                    catch { docs = null; }
                }

                // 4. Последний фолбэк: один активный документ.
                if (docs == null || docs.Length == 0)
                {
                    try
                    {
                        var one = UnityEngine.Object.FindObjectOfType(_cachedUIDocumentType);
                        if (one != null) docs = new UnityEngine.Object[] { one };
                    }
                    catch { docs = null; }
                }

                int docCount = (docs == null) ? 0 : docs.Length;
                if (docs != null)
                {
                    foreach (var doc in docs)
                    {
                        if (doc == null) continue;
                        object root;
                        try { root = _rootProp.GetValue(doc); }
                        catch { continue; }
                        if (root == null) continue;
                        WalkVisualTree(root, 0);
                    }
                }

                // Однократная диагностика после первого прохода: сколько документов и переводов.
                if (!_scanDiagLogged)
                {
                    _scanDiagLogged = true;
                    UnityEngine.Debug.Log("[RussianLocalization] RuntimeTranslator scan: panels=" + panelCount + ", docs=" + docCount +
                        ", generic=" + (_findDocumentMethod != null) +
                        ", textElementType=" + (_cachedTextElementType != null) +
                        ", translatedThisPass=" + _diagTranslatedCount);
                }
            }

            

            // Жёсткий предел глубины рекурсии. Реальные UI-деревья Modern UI редко глубже
            // 40-50 уровней; 512 — заведомо безопасный потолок. Без него транзиентный цикл
            // в дереве (узел, чьи children содержат предка — возможно во время загрузки,
            // когда дерево в промежуточном состоянии) уводил рекурсию в бесконечность и ронял
            // нативный стек → access violation 0xc0000005 в ntdll.dll без managed-исключения.
            private const int MaxWalkDepth = 512;

            private static void WalkVisualTree(object element, int depth)
            {
                if (element == null || _cachedTextElementType == null) return;
                if (depth >= MaxWalkDepth) return;

                if (_cachedTextElementType.IsInstanceOfType(element))
                {
                    if (_textPropInfo == null)
                    {
                        _textPropInfo = _cachedTextElementType.GetProperty("text",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    }
                    if (_textPropInfo != null && _textPropInfo.CanRead && _textPropInfo.CanWrite)
                    {
                        try
                        {
                            string cur = _textPropInfo.GetValue(element) as string;
                            if (!string.IsNullOrEmpty(cur) && HasEnglish(cur))
                            {
                                string translated = TranslationEngine.TranslateMarkup(cur);
                                if (!string.IsNullOrEmpty(translated) && translated != cur)
                                {
                                    _textPropInfo.SetValue(element, translated);
                                    _diagTranslatedCount++;
                                }
                            }
                        }
                        catch { }
                    }
                }

                // children объявлено на VisualElement — PropertyInfo один и тот же для всех узлов,
                // поэтому резолвим один раз и переиспользуем (без рефлексии на каждом узле — это и был
                // главный источник просадки FPS).
                if (_childrenPropInfo == null)
                {
                    _childrenPropInfo = element.GetType().GetProperty("children",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                }
                if (_childrenPropInfo == null) return;

                System.Collections.IEnumerable children;
                try { children = _childrenPropInfo.GetValue(element) as System.Collections.IEnumerable; }
                catch { return; }
                if (children == null) return;

                foreach (var child in children)
                {
                    WalkVisualTree(child, depth + 1);
                }
            }

            private static bool HasEnglish(string s)
            {
                if (string.IsNullOrEmpty(s)) return false;
                int latin = 0, cyr = 0;
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) latin++;
                    else if ((c >= 'Ѐ' && c <= 'ӿ') || c == 'ё' || c == 'Ё') cyr++;
                    if (latin >= 2) return true;
                }
                if (cyr > 0 && latin == 0) return false;
                return latin >= 2 && cyr == 0;
            }
        }

        // --- ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ДЛЯ РАБОТЫ С ЦВЕТОВЫМИ ФРАГМЕНТАМИ ---
        private static readonly System.Text.RegularExpressions.Regex BrokenBraceRegex =
            new System.Text.RegularExpressions.Regex(@"\{{3,}|\}{3,}", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex ColorLetterFragmentRegex =
            new System.Text.RegularExpressions.Regex(@"(<color=([^>]+)>)([a-zA-Z])</color>", System.Text.RegularExpressions.RegexOptions.Compiled);

        // Радужный прогон: 3+ подряд идущих однобуквенных латинских цветовых блока.
        private static readonly System.Text.RegularExpressions.Regex RainbowRunRegex =
            new System.Text.RegularExpressions.Regex(@"(?:<color=#[0-9A-Fa-f]+>[A-Za-z]</color>){3,}", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex RainbowLetterRegex =
            new System.Text.RegularExpressions.Regex(@"<color=(#[0-9A-Fa-f]+)>([A-Za-z])</color>", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// Обрабатывает «радужные» слова (каждая буква в своём цвете, напр. lacquered/engraved/painted):
        /// собирает слово, переводит его, и РАСПРЕДЕЛЯЕТ исходные цвета по буквам перевода,
        /// сохраняя радужный эффект даже при другой длине русского слова.
        /// Непереводимое (процедурные имена) оставляет нетронутым — его подхватит CompactColorFragments.
        /// </summary>
        private static string ExpandRainbowWords(string text)
        {
            if (string.IsNullOrEmpty(text) || !text.Contains("<color=")) return text;
            if (text.Length > 1000) return text;

            return RainbowRunRegex.Replace(text, m =>
            {
                var letters = RainbowLetterRegex.Matches(m.Value);
                if (letters.Count < 3) return m.Value;

                var colors = new List<string>(letters.Count);
                var wordSb = new StringBuilder(letters.Count);
                foreach (System.Text.RegularExpressions.Match lm in letters)
                {
                    colors.Add(lm.Groups[1].Value);
                    wordSb.Append(lm.Groups[2].Value);
                }
                string word = wordSb.ToString();

                string translated = TranslateText(word, true);
                // Если не перевели (процедурное имя и т.п.) — оставляем как было.
                if (string.IsNullOrEmpty(translated) || translated == word || !ContainsCyrillic(translated))
                    return m.Value;

                // Распределяем N исходных цветов по M буквам перевода (пропорционально).
                int n = colors.Count;
                int mlen = translated.Length;
                var outSb = new StringBuilder(mlen * 24);
                for (int i = 0; i < mlen; i++)
                {
                    int ci = (int)((long)i * n / mlen);
                    if (ci >= n) ci = n - 1;
                    outSb.Append("<color=").Append(colors[ci]).Append('>').Append(translated[i]).Append("</color>");
                }
                return outSb.ToString();
            });
        }

        // 2026-07-06 (v26): аналог ExpandRainbowWords для СТАРОГО формата цветокодов "&X" классического
        // UI. Радужное слово там — это 3+ подряд идущих "&<цвет><буква>" (напр. "&Yl&ya&Kc&yq&Yu&ye&Kr&ye&Yd"
        // = "lacquered", "&Ye&yn&cg&Cr&Ya&yv&ce&Cd" = "engraved"). Такие слова НЕ переводились: v25-фикс
        // в TryWordReplacement трогает только цветокоды на КРАЯХ слова, а тут код перед КАЖДОЙ буквой.
        // Собираем слово, переводим, распределяем исходные цвета по буквам перевода.
        // Требует ровно "&<буква><буква>" на звено, поэтому обычные одноцветные слова ("&Ysteel&y" —
        // 1 звено) не трогает: их обрабатывает TryWordReplacement.
        private static readonly System.Text.RegularExpressions.Regex AmpRainbowRunRegex =
            new System.Text.RegularExpressions.Regex(@"(?:&[A-Za-z][A-Za-z]){3,}", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex AmpRainbowLetterRegex =
            new System.Text.RegularExpressions.Regex(@"&([A-Za-z])([A-Za-z])", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static string ExpandAmpRainbowWords(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('&') < 0) return text;
            if (text.Length > 1000) return text;

            return AmpRainbowRunRegex.Replace(text, m =>
            {
                var letters = AmpRainbowLetterRegex.Matches(m.Value);
                if (letters.Count < 3) return m.Value;

                var colors = new List<string>(letters.Count);
                var wordSb = new StringBuilder(letters.Count);
                foreach (System.Text.RegularExpressions.Match lm in letters)
                {
                    colors.Add(lm.Groups[1].Value);   // буква цвета, напр. "Y"
                    wordSb.Append(lm.Groups[2].Value); // сама буква слова
                }
                string word = wordSb.ToString();

                string translated = TranslateText(word, true);
                if (string.IsNullOrEmpty(translated) || translated == word || !ContainsCyrillic(translated))
                    return m.Value;

                int n = colors.Count;
                int mlen = translated.Length;
                var outSb = new StringBuilder(mlen * 3);
                for (int i = 0; i < mlen; i++)
                {
                    int ci = (int)((long)i * n / mlen);
                    if (ci >= n) ci = n - 1;
                    outSb.Append('&').Append(colors[ci]).Append(translated[i]);
                }
                return outSb.ToString();
            });
        }

        /// <summary>
        /// Схлопывает последовательности вида <color=C1>L</color><color=C2>e</color>... в единый цветовой блок.
        /// Игнорирует меняющиеся цвета: используется цвет первой буквы, так как перевод
        /// цельного слова важнее сохранения посимвольной раскраски.
        /// </summary>
        private static string CompactColorFragments(string text)
        {
            if (string.IsNullOrEmpty(text) || !text.Contains("<color=")) return text;

            // Оптимизация: для длинных текстов компактизация цветовых фрагментов слишком дорогая
            // и не даёт заметного визуального эффекта. Ограничиваем только короткими UI-строками.
            if (text.Length > 1000) return text;

            bool changed;
            int iterations = 0;
            do
            {
                changed = false;
                var matches = ColorLetterFragmentRegex.Matches(text);
                if (matches.Count < 2) break;

                var sb = new System.Text.StringBuilder();
                int lastIdx = 0;
                for (int i = 0; i < matches.Count; i++)
                {
                    var m = matches[i];
                    if (m.Index < lastIdx) continue;

                    string firstColor = m.Groups[2].Value;
                    int chainEnd = m.Index + m.Length;
                    var wordChars = new System.Text.StringBuilder();
                    wordChars.Append(m.Groups[3].Value);

                    // Схлопываем любые подряд идущие однобуквенные цветовые латинские фрагменты
                    while (i + 1 < matches.Count)
                    {
                        var next = matches[i + 1];
                        if (next.Index != chainEnd) break;
                        wordChars.Append(next.Groups[3].Value);
                        chainEnd = next.Index + next.Length;
                        i++;
                    }

                    if (wordChars.Length > 1)
                    {
                        sb.Append(text.Substring(lastIdx, m.Index - lastIdx));
                        sb.Append("<color=" + firstColor + ">" + wordChars.ToString() + "</color>");
                        lastIdx = chainEnd;
                        changed = true;
                    }
                    else
                    {
                        sb.Append(text.Substring(lastIdx, m.Index - lastIdx));
                        sb.Append(m.Value);
                        lastIdx = m.Index + m.Length;
                    }
                }
                sb.Append(text.Substring(lastIdx));
                text = sb.ToString();
                iterations++;
            } while (changed && iterations < 10);

            return text;
        }

            public static string DistributeColors(string originalTextWithTags, string translatedText)
            {
            if (string.IsNullOrEmpty(translatedText)) return translatedText;
            if (string.IsNullOrEmpty(originalTextWithTags) || !originalTextWithTags.Contains("<color=")) return translatedText;

            // БЫСТРЫЙ ВЫХОД: Если текст не изменился после перевода, возвращаем оригинал как есть.
            // Это экономит кучу времени на больших текстах справки (Help), которые не переведены.
            string origStrip = TagRegex.Replace(originalTextWithTags, "");
            if (translatedText == origStrip) return originalTextWithTags;

            if (originalTextWithTags.Contains("\n") && translatedText.Contains("[") && !translatedText.Contains("\n"))
            {
                int bracketIdx = translatedText.IndexOf('[');
                translatedText = translatedText.Insert(bracketIdx, "\n");
            }

            var colors = ExtractColors(originalTextWithTags);
            if (colors.Count == 0) return translatedText;

            return DistributeColorsInternal(colors, translatedText, origStrip);
            }

            private static List<string> ExtractColors(string originalTextWithTags)
            {
            var colors = new List<string>();
            int i = 0;
            int len = originalTextWithTags.Length;
            var colorStack = new Stack<string>();
            string currentParentColor = null;

            while (i < len)
            {
                if (i < len - 7 && originalTextWithTags.Substring(i).StartsWith("<color=", StringComparison.OrdinalIgnoreCase))
                {
                    int closeBracket = originalTextWithTags.IndexOf('>', i);
                    if (closeBracket != -1)
                    {
                        string colorValue = originalTextWithTags.Substring(i + 7, closeBracket - (i + 7));
                        if (currentParentColor != null) colorStack.Push(currentParentColor);
                        currentParentColor = colorValue;
                        i = closeBracket + 1;
                        continue;
                    }
                }
                else if (i < len - 8 && originalTextWithTags.Substring(i).StartsWith("</color>", StringComparison.OrdinalIgnoreCase))
                {
                    if (colorStack.Count > 0) currentParentColor = colorStack.Pop();
                    else currentParentColor = null;
                    i += 8;
                    continue;
                }

                if (originalTextWithTags[i] == '<' && originalTextWithTags.IndexOf('>', i) != -1)
                {
                    int closeBracket = originalTextWithTags.IndexOf('>', i);
                    string potentialTag = originalTextWithTags.Substring(i, closeBracket - i + 1);
                    if (TagRegex.IsMatch(potentialTag))
                    {
                        i = closeBracket + 1;
                        continue;
                    }
                }

                colors.Add(currentParentColor);
                i++;
            }
            return colors;
            }

            private static string DistributeColorsInternal(List<string> colors, string translatedText, string origStrip)
            {
            if (colors.Count == 0) return translatedText;

            // --- Инъекция переносов строк из оригинала для сохранения верстки ---
            if (origStrip.Contains("\n") && !translatedText.Contains("\n") && translatedText.Length > 20)
            {
                var nlRatios = new List<double>();
                for (int i = 0; i < origStrip.Length; i++)
                {
                    if (origStrip[i] == '\n') nlRatios.Add((double)i / origStrip.Length);
                }

                if (nlRatios.Count > 0)
                {
                    char[] transChars = translatedText.ToCharArray();
                    foreach (var ratio in nlRatios)
                    {
                        int targetIdx = (int)(ratio * translatedText.Length);
                        int bestIdx = -1;
                        int minDist = 15;
                        for (int k = Math.Max(0, targetIdx - 7); k < Math.Min(translatedText.Length, targetIdx + 7); k++)
                        {
                            if (transChars[k] == ' ')
                            {
                                if (Math.Abs(k - targetIdx) < minDist)
                                {
                                    minDist = Math.Abs(k - targetIdx);
                                    bestIdx = k;
                                }
                            }
                        }
                        if (bestIdx != -1 && transChars[bestIdx] != '\n') transChars[bestIdx] = '\n';
                    }
                    translatedText = new string(transChars);
                }
            }
            // -------------------------------------------------------------------

            // Считаем доминантный цвет
            var colorCounts = new Dictionary<string, int>();
            foreach (var c in colors)
            {
                if (c == null) continue;
                if (colorCounts.ContainsKey(c)) colorCounts[c]++;
                else colorCounts[c] = 1;
            }

            string dominantColor = null;
            int maxCount = 0;
            foreach (var kvp in colorCounts)
            {
                if (kvp.Value > maxCount) { maxCount = kvp.Value; dominantColor = kvp.Key; }
            }

            // Оптимизация: для длинных текстов пословное распределение цветов слишком дорого.
            // Если доминирующий цвет покрывает большинство символов, оборачиваем весь текст в него.
            if (dominantColor != null && translatedText.Length > 1500 && maxCount >= colors.Count * 0.80)
            {
                return "<color=" + dominantColor + ">" + translatedText + "</color>";
            }

            // Если один цвет >= 95% — красим весь текст одним цветом.
            // 95% достаточно: для "You hit (x3) for 7 damage" — серый ~65%, и слово "hit"
            // не разорвётся. Для 80% слишком много паттернов с 2 цветами ломаются.
            if (dominantColor != null && maxCount >= colors.Count * 0.95)
            {
                return "<color=" + dominantColor + ">" + translatedText + "</color>";
            }

            // Сегментное распределение: разбиваем переведённый текст на слова
            // и маппим каждый сегмент на соответствующий цветовой сегмент оригинала
            string[] words = System.Text.RegularExpressions.Regex.Split(translatedText, @"(\s+)");
            if (words.Length == 0) return translatedText;

            // Собираем уникальные цветовые сегменты из оригинала (цвет + текст)
            // Каждый сегмент — это непрерывный блок одного цвета в оригинале
            var segments = new List<KeyValuePair<string, int>>(); // цвет, длина в символах
            {
                string segColor = null;
                int segLen = 0;
                for (int i = 0; i < colors.Count; i++)
                {
                    if (colors[i] != segColor)
                    {
                        if (segLen > 0) segments.Add(new KeyValuePair<string, int>(segColor, segLen));
                        segColor = colors[i];
                        segLen = 1;
                    }
                    else
                    {
                        segLen++;
                    }
                }
                if (segLen > 0) segments.Add(new KeyValuePair<string, int>(segColor, segLen));
            }

            // Если сегментов 1 — просто маппим
            if (segments.Count == 1)
            {
                StringBuilder sb2 = new StringBuilder(translatedText.Length * 2);
                string lastColor = null;
                foreach (var seg in segments)
                {
                    if (seg.Key != lastColor)
                    {
                        if (lastColor != null) sb2.Append("</color>");
                        if (seg.Key != null) sb2.Append("<color=" + seg.Key + ">");
                        lastColor = seg.Key;
                    }
                }
                sb2.Append(translatedText);
                if (lastColor != null) sb2.Append("</color>");
                return sb2.ToString();
            }

            // Пропорциональное сегментное распределение
            // Каждому слову (или пробелу) назначаем цвет сегмента оригинала,
            // соответствующий его пропорциональной позиции
            int totalOrigChars = 0;
            foreach (var seg in segments) totalOrigChars += seg.Value;

            StringBuilder sb = new StringBuilder(translatedText.Length * 2);
            string activeColor = null;
            int charOffset = 0;

            foreach (string word in words)
            {
                if (string.IsNullOrEmpty(word)) continue;

                bool isWhitespace = word.Trim().Length == 0;
                int wordStart = charOffset;
                int wordEnd = charOffset + word.Length - 1;

                // Определяем доминантный цвет для этого слова
                // Берём середину слова и смотрим какой сегмент туда попадает
                int midPos = wordStart + word.Length / 2;
                float ratio = (float)midPos / translatedText.Length;
                int origPos = (int)(ratio * totalOrigChars);

                // Находим сегмент для этой позиции
                string wordColor = segments[segments.Count - 1].Key; // default = последний сегмент
                int accum = 0;
                foreach (var seg in segments)
                {
                    accum += seg.Value;
                    if (origPos < accum)
                    {
                        wordColor = seg.Key;
                        break;
                    }
                }

                // Пробелам даём цвет предыдущего слова или доминантный
                if (isWhitespace && activeColor != null)
                {
                    wordColor = activeColor;
                }

                // Если слово — это скобочный хоткей [X], красим хоткей цветом из сегмента
                // Предотвращает "[Esc]" → "Esc" (теряет скобки)
                if (wordColor != activeColor)
                {
                    if (activeColor != null) sb.Append("</color>");
                    if (wordColor != null) sb.Append("<color=" + wordColor + ">");
                    activeColor = wordColor;
                }
                sb.Append(word);
                charOffset += word.Length;
            }

            if (activeColor != null) sb.Append("</color>");
            return sb.ToString();
            }
            public static string TranslateTextStrict(string text)
            {
            if (string.IsNullOrEmpty(text)) return text;

            if (InternalGameKeys.Contains(text.Trim()) || IsKeyInBrackets(text)) return text;

            bool success;
            string modernUITranslated = TryTranslateModernUI(text, out success);
            if (success) return modernUITranslated;

            if (!ContainsEnglish(text)) return text;

            if (text.Length == 1 && ((text[0] >= 'a' && text[0] <= 'z') || (text[0] >= 'A' && text[0] <= 'Z')))
            {
                return text;
            }

            string cached;
            if (translationCache.TryGetValue(text, out cached))
            {
                return cached;
            }

            string prefix;
            string core;
            string suffix;
            ExtractCoreText(text, out prefix, out core, out suffix);

            if (string.IsNullOrEmpty(core)) return text;

            string normalizedCore = core.Replace("\r\n", "\n")
                                        .Replace('\u00A0', ' ')
                                        .Replace('\u2007', ' ')
                                        .Replace('\u200B', ' ')
                                        .Replace('\u202F', ' ');
            string trimmedCore = normalizedCore.Trim();
            // Case-sensitive check for uppercase confirmation keys only.
            if (trimmedCore == "QUIT" || trimmedCore == "ABANDON" || trimmedCore == "RETIRE" || trimmedCore == "ABANDONED" || trimmedCore == "DELETE" ||
                trimmedCore == "Q U I T" || trimmedCore == "A B A N D O N" || trimmedCore == "R E T I R E" || trimmedCore == "A B A N D O N E D" || trimmedCore == "D E L E T E")
            {
                translationCache[text] = text;
                return text;
            }
            string translatedCore = "";

            string exactMatch;
            if (staticDictionary.TryGetValue(trimmedCore, out exactMatch))
            {
                translatedCore = exactMatch;
            }
            else
            {
                string sn = SuperNormalize(trimmedCore);
                bool isKeyName = sn.Length == 1 || KeyNameSet.Contains(sn);
                bool hasBrackets = trimmedCore.StartsWith("[") && trimmedCore.EndsWith("]");

                if (!isKeyName || hasBrackets)
                {
                    string originalKey;
                    if (normalizedKeyDictionary.TryGetValue(sn, out originalKey))
                    {
                        if (staticDictionary.TryGetValue(originalKey, out exactMatch))
                        {
                            translatedCore = RestoreStrippedPunctuation(trimmedCore, originalKey, exactMatch);
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(translatedCore))
            {
                // 2026-07-19: Добавляем фоллбек на пословный перевод для коротких фраз (до 3 слов),
                // чтобы поддержать перевод специфичных кнопок Modern UI (например, "[f] fire", "[r] reload" и т.д.)
                int wordCount = trimmedCore.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
                if (wordCount <= 3 && disableWordReplacementCounter == 0)
                {
                    translatedCore = TryWordReplacement(normalizedCore);
                    if (translatedCore != normalizedCore)
                    {
                        // Restore capital letter case if needed
                        if (translatedCore.Length > 0 && char.IsUpper(trimmedCore[0]) && char.IsLower(translatedCore[0]))
                        {
                            translatedCore = char.ToUpper(translatedCore[0]) + translatedCore.Substring(1);
                        }
                        string res = prefix + translatedCore + suffix;
                        translationCache[text] = res;
                        return res;
                    }
                }
                return text; // Strict mode: no fallback!
            }

            // Restore capital letter case if needed
            if (translatedCore.Length > 0 && char.IsUpper(trimmedCore[0]) && char.IsLower(translatedCore[0]))
            {
                translatedCore = char.ToUpper(translatedCore[0]) + translatedCore.Substring(1);
            }

            string result = prefix + translatedCore + suffix;
            translationCache[text] = result;
            return result;
            }

            public static string TranslateDialogueLine(string text)
            {
            if (string.IsNullOrEmpty(text)) return text;

            string numPrefix = "";
            string rest = text;
            var numMatch = System.Text.RegularExpressions.Regex.Match(text, @"^\[(?<num>\d+)\]\s*(?<rest>.*)$");
            if (numMatch.Success)
            {
                numPrefix = "[" + numMatch.Groups["num"].Value + "] ";
                rest = numMatch.Groups["rest"].Value.Trim();
            }

            var actionMatch = System.Text.RegularExpressions.Regex.Match(rest, @"^(?<dialog>.*?)\s*\[(?<action>[^\]]+)\]$");
            if (actionMatch.Success)
            {
                string dialog = actionMatch.Groups["dialog"].Value.Trim();
                string action = actionMatch.Groups["action"].Value.Trim();

                string translatedDialog = TranslateTextStrict(dialog);

                string translatedAction = TranslateTextStrict("[" + action + "]");
                if (translatedAction != "[" + action + "]")
                {
                    if (translatedAction.StartsWith("[") && translatedAction.EndsWith("]"))
                    {
                        translatedAction = translatedAction.Substring(1, translatedAction.Length - 2);
                    }
                }
                else
                {
                    translatedAction = TranslateTextStrict(action);
                }

                return numPrefix + translatedDialog + " [" + translatedAction + "]";
            }

            return numPrefix + TranslateTextStrict(rest);
            }
            }



    // --- УТИЛИТЫ ДЛЯ ШРИФТОВ (DYNAMIC CYRILLIC INJECTION) ---

    public static class FontUtils

    {

        private static TMP_FontAsset cyrillicFallback = null;

        private static HashSet<int> processedFonts = new HashSet<int>();

        private static bool loggedFallbackMissing = false;



        public static bool ContainsRussian(string text) => TranslationEngine.ContainsCyrillicInternal(text);



        public static void ForceCyrillicFont(TMP_Text textComponent)

        {

            if (textComponent == null) return;



            try

            {

                if (cyrillicFallback == null)

                {

                    cyrillicFallback = FindCyrillicFontAsset();

                    if (cyrillicFallback == null && !loggedFallbackMissing)

                    {

                        UnityEngine.Debug.LogWarning("[RussianLocalization] Cyrillic font is not loaded in memory yet.");

                        loggedFallbackMissing = true;

                    }

                }



                if (cyrillicFallback != null && textComponent.font != cyrillicFallback)

                {

                    if (ContainsRussian(textComponent.text))

                    {

                        string oldFontName = textComponent.font != null ? textComponent.font.name : "null";

                        textComponent.font = cyrillicFallback;

                        UnityEngine.Debug.Log("[RussianLocalization] Forced cyrillic font for text '" + textComponent.text + "' (switched from '" + oldFontName + "' to '" + cyrillicFallback.name + "')");

                    }

                }

            }

            catch (Exception ex)

            {

                UnityEngine.Debug.LogError("[RussianLocalization] Force Font Error: " + ex.ToString());

            }

        }



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



                if (cyrillicFallback != null)

                {

                    if (currentFont != cyrillicFallback)

                    {

                        if (currentFont.fallbackFontAssetTable == null)

                        {

                            currentFont.fallbackFontAssetTable = new List<TMP_FontAsset>();

                        }



                        if (!currentFont.fallbackFontAssetTable.Contains(cyrillicFallback))

                        {

                            currentFont.fallbackFontAssetTable.Add(cyrillicFallback);

                            UnityEngine.Debug.Log("[RussianLocalization] Injected fallback '" + cyrillicFallback.name + "' into '" + currentFont.name + "'");

                        }

                    }

                    processedFonts.Add(fontId);

                }

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

                    hasCyrillic = font.HasCharacter('\u0430') || font.HasCharacter((char)1072);

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



    // --- HARMONY PATCHES ---

    // 2026-07-05 (v3): в двух подряд крашах (см. ru-qud-crash-fix-20260705-trade) Player.log
    // обрывался БЕЗ единого исключения ровно на строке "Forced cyrillic font for text '<буква>'" —
    // а этот лог пишет ТОЛЬКО FontUtils.ForceCyrillicFont, вызываемая из Postfix-ов патчей на
    // TMP_Text.text/SetText/font ниже. FontUtils.ForceCyrillicFont сама присваивает
    // textComponent.font, а сеттер font тоже пропатчен (TMPText_Font_Patch) и снова вызывает
    // ForceCyrillicFont — цикл самозавершается проверкой "font != cyrillicFallback", но TMPro
    // внутри себя может дополнительно дёргать SetText/text при смене шрифта (перегенерация меша),
    // а те патчи снова вызывают Translate()+ForceCyrillicFont. При интенсивном построении
    // Modern UI (много хоткей-плашек/строк сразу, например экран торговли) это даёт заметную
    // вложенность НАШИХ вызовов поверх и без того глубокого стека Unity Awake/OnEnable —
    // access violation 0xc0000005 в ntdll.dll. Общий guard на все патчи этой группы: если
    // впервые упираемся в предел глубины — один раз пишем в reentrancy_diag.txt реальный стек
    // вызовов (чтобы при повторном краше видеть, что именно рекурсит), и просто пропускаем
    // дальнейшую обработку (текст останется как есть на этом уровне), не добавляя стека.
    internal static class UITextReentrancyGuard
    {
        // 2026-07-05 (v4 — БИСЕКЦИЯ): depth-guard (v3) не помог и ни разу не сработал
        // (reentrancy_diag.txt не появился), значит эти 11 патчей НЕ рекурсят друг в друга —
        // depth-guard тут ни при чём. Прежде чем гадать про четвёртый механизм, проверяем
        // гипотезу целиком: полностью выключаем ВСЮ эту группу патчей (TMP_Text.text/SetText/
        // font, TextMeshPro/UGUI.Awake) одним флагом — если краш на экране торговли ПРОПАДЁТ
        // (ценой английского текста в этих элементах, включая хоткей-плашки), то причина
        // ТОЧНО в этой группе, и дальше сужаем виновника внутри неё по одному патчу.
        // Если краш ОСТАНЕТСЯ даже с ПОЛНОСТЬЮ выключенной группой — дело не в ней вообще,
        // и следующий подозреваемый — XRL.UI.Popup (42 патченных метода) или что-то в самой
        // игре, что переводчик лишь провоцирует. Именно так был найден RuntimeTranslator
        // 2026-07-02 (см. ru-qud-crash-fix-20260702) — бисекцией, а не гаданием.
        // 2026-07-05 (v6): бисекция подтвердила, что эта группа НЕ виновата — краш пережил
        // полное отключение (см. ru-qud-crash-fix-20260705-trade v5). Возвращено обратно.
        // 2026-07-05 (v10): ВАЖНО — v5/v6 тестировали эту группу только против краша В ТОРГОВЛЕ.
        // Против НОВОГО стартового краша (хоткей-плашки сразу после загрузки, лог обрывается
        // ровно на "Forced cyrillic font for text '<буква>'") эта группа никогда не проверялась
        // отдельно. PatchUIElements уже отключён (DIAG_DISABLE_UIELEMENTS_HOOK=true), Description
        // тоже (DIAG_DISABLE_DESCRIPTION_HOOKS=true), но краш на хоткей-плашках остался — точное
        // совпадение с сигнатурой этой группы. Проверяем её отдельно для СТАРТОВОГО краша.
        // 2026-07-05 (v11 — НАСТОЯЩАЯ ПРИЧИНА НАЙДЕНА, это НЕ мы): анализ дампа краша через WinDbg
        // (!analyze -v на C:\Users\Lecoo\Documents\CavesOfQud_RU_Logs\CrashDumps\analyze.log) дал
        // FAILURE_BUCKET_ID: INVALID_POINTER_READ_c0000005_gameoverlayrenderer64.dll!Unknown —
        // падение происходит внутри gameoverlayrenderer64.dll (оверлей Steam), а НЕ в моде. Тот же
        // offset 0x539c2 в ntdll.dll на КАЖДОМ краше сегодня (независимо от того, что отключено в
        // моде) — это гонка в хуке Steam-оверлея на DirectX (OverlayHookD3D3 → GetModuleHandleW),
        // а не рекурсия/переполнение стека в наших Harmony-патчах. Отключение хуков мода иногда
        // "помогало" лишь потому, что снижало нагрузку/рендер-churn и меняло тайминг гонки, а не
        // потому что чинило причину. Все диагностические флаги возвращены в штатное положение —
        // реальный фикс: отключить Steam Overlay для игры (Steam → Свойства игры → Общие → снять
        // "Enable Steam Overlay"), см. память ru-qud-crash-fix-20260705-trade (v11).
        // 2026-07-05/06 (v12): пользователь подтвердил — краш пережил и полный рестарт Steam с
        // оверлеем выключенным (дамп это подтвердил), но настаивает что дело в моде. Возможный
        // механизм: наш код портит память, а падает потом посторонний код (тот же offset каждый
        // раз это скорее подтверждает, чем опровергает — детерминированная порча). Возвращаем в
        // выключенное состояние для чистой точечной проверки PatchPopup (см. флаги выше по файлу).
        public const bool DIAG_DISABLE_TMP_HOOKS = false;

        private const int MaxDepth = 12;

        [ThreadStatic]
        private static int _depth;

        private static volatile bool _loggedOverflow = false;

        public static bool TryEnter()
        {
            if (DIAG_DISABLE_TMP_HOOKS) return false;
            return TryEnterReal();
        }

        private static bool TryEnterReal()
        {
            if (_depth >= MaxDepth)
            {
                if (!_loggedOverflow)
                {
                    _loggedOverflow = true;
                    try
                    {
                        string trace = new System.Diagnostics.StackTrace(1, false).ToString();
                        string msg = "[RussianLocalization] UI text reentrancy depth cap (" + MaxDepth + ") reached — skipping further nested Translate/ForceCyrillicFont calls to avoid native stack overflow. Call chain:\n" + trace;
                        UnityEngine.Debug.LogWarning(msg);
                        if (!string.IsNullOrEmpty(TranslationEngine.CachedModPath))
                        {
                            string p = System.IO.Path.Combine(TranslationEngine.CachedModPath, "reentrancy_diag.txt");
                            System.IO.File.AppendAllText(p, DateTime.Now + "\r\n" + msg + "\r\n\r\n", System.Text.Encoding.UTF8);
                        }
                    }
                    catch { /* диагностика не должна ронять игру */ }
                }
                return false;
            }
            _depth++;
            return true;
        }

        public static void Exit()
        {
            _depth--;
        }
    }

    [HarmonyPatch(typeof(UnityEngine.UI.Text), "text", MethodType.Setter)]

    public static class UnityUIText_Patch

    {

        public static void Prefix(ref string value)

        {

            if (TranslationEngine.Initialized)

            {

                if (!UITextReentrancyGuard.TryEnter()) return;
                try
                {
                    value = TranslationEngine.Translate(value);
                }
                finally
                {
                    UITextReentrancyGuard.Exit();
                }

            }

        }

    }



    [HarmonyPatch(typeof(TMPro.TMP_Text), "text", MethodType.Setter)]
    [HarmonyPriority(100)]

    public static class TMPText_Patch

    {

        public static void Prefix(ref string value)

        {

            if (TranslationEngine.Initialized)

            {

                if (!UITextReentrancyGuard.TryEnter()) return;
                try
                {
                    value = TranslationEngine.Translate(value);
                }
                finally
                {
                    UITextReentrancyGuard.Exit();
                }

            }

        }

        public static void Postfix(TMPro.TMP_Text __instance)

        {

            if (TranslationEngine.Initialized && __instance != null)

            {

                if (!UITextReentrancyGuard.TryEnter()) return;
                try
                {
                    FontUtils.ForceCyrillicFont(__instance);
                }
                finally
                {
                    UITextReentrancyGuard.Exit();
                }

            }

        }

    }



    [HarmonyPatch(typeof(TMPro.TMP_Text), "SetText", new Type[] { typeof(string) })]
    [HarmonyPriority(100)]

    public static class TMPText_SetText_Patch

    {

        public static void Prefix(ref string sourceText)

        {

            if (TranslationEngine.Initialized)

            {

                if (!UITextReentrancyGuard.TryEnter()) return;
                try
                {
                    sourceText = TranslationEngine.Translate(sourceText);
                }
                finally
                {
                    UITextReentrancyGuard.Exit();
                }

            }

        }

        public static void Postfix(TMPro.TMP_Text __instance)

        {

            if (TranslationEngine.Initialized && __instance != null)

            {

                if (!UITextReentrancyGuard.TryEnter()) return;
                try
                {
                    FontUtils.ForceCyrillicFont(__instance);
                }
                finally
                {
                    UITextReentrancyGuard.Exit();
                }

            }

        }

    }



    [HarmonyPatch(typeof(TMPro.TMP_Text), "SetText", new Type[] { typeof(string), typeof(bool) })]
    [HarmonyPriority(100)]

    public static class TMPText_SetText_Bool_Patch

    {

        public static void Prefix(ref string sourceText)

        {

            if (TranslationEngine.Initialized)

            {

                if (!UITextReentrancyGuard.TryEnter()) return;
                try
                {
                    sourceText = TranslationEngine.Translate(sourceText);
                }
                finally
                {
                    UITextReentrancyGuard.Exit();
                }

            }

        }

        public static void Postfix(TMPro.TMP_Text __instance)

        {

            if (TranslationEngine.Initialized && __instance != null)

            {

                if (!UITextReentrancyGuard.TryEnter()) return;
                try
                {
                    FontUtils.ForceCyrillicFont(__instance);
                }
                finally
                {
                    UITextReentrancyGuard.Exit();
                }

            }

        }

    }



    [HarmonyPatch(typeof(TMPro.TMP_Text), "SetText", new Type[] { typeof(string), typeof(float) })]
    [HarmonyPriority(100)]

    public static class TMPText_SetText_Float1_Patch

    {

        public static void Prefix(ref string sourceText)

        {

            if (TranslationEngine.Initialized)

            {

                if (!UITextReentrancyGuard.TryEnter()) return;
                try
                {
                    sourceText = TranslationEngine.Translate(sourceText);
                }
                finally
                {
                    UITextReentrancyGuard.Exit();
                }

            }

        }

        public static void Postfix(TMPro.TMP_Text __instance)

        {

            if (TranslationEngine.Initialized && __instance != null)

            {

                if (!UITextReentrancyGuard.TryEnter()) return;
                try
                {
                    FontUtils.ForceCyrillicFont(__instance);
                }
                finally
                {
                    UITextReentrancyGuard.Exit();
                }

            }

        }

    }



    [HarmonyPatch(typeof(TMPro.TMP_Text), "SetText", new Type[] { typeof(string), typeof(float), typeof(float) })]
    [HarmonyPriority(100)]

    public static class TMPText_SetText_Float2_Patch

    {

        public static void Prefix(ref string sourceText)

        {

            if (TranslationEngine.Initialized)

            {

                if (!UITextReentrancyGuard.TryEnter()) return;
                try
                {
                    sourceText = TranslationEngine.Translate(sourceText);
                }
                finally
                {
                    UITextReentrancyGuard.Exit();
                }

            }

        }

        public static void Postfix(TMPro.TMP_Text __instance)

        {

            if (TranslationEngine.Initialized && __instance != null)

            {

                if (!UITextReentrancyGuard.TryEnter()) return;
                try
                {
                    FontUtils.ForceCyrillicFont(__instance);
                }
                finally
                {
                    UITextReentrancyGuard.Exit();
                }

            }

        }

    }



    [HarmonyPatch(typeof(TMPro.TMP_Text), "SetText", new Type[] { typeof(string), typeof(float), typeof(float), typeof(float) })]
    [HarmonyPriority(100)]

    public static class TMPText_SetText_Float3_Patch

    {

        public static void Prefix(ref string sourceText)

        {

            if (TranslationEngine.Initialized)

            {

                if (!UITextReentrancyGuard.TryEnter()) return;
                try
                {
                    sourceText = TranslationEngine.Translate(sourceText);
                }
                finally
                {
                    UITextReentrancyGuard.Exit();
                }

            }

        }

        public static void Postfix(TMPro.TMP_Text __instance)

        {

            if (TranslationEngine.Initialized && __instance != null)

            {

                if (!UITextReentrancyGuard.TryEnter()) return;
                try
                {
                    FontUtils.ForceCyrillicFont(__instance);
                }
                finally
                {
                    UITextReentrancyGuard.Exit();
                }

            }

        }

    }



    [HarmonyPatch(typeof(TMPro.TMP_Text), "SetText", new Type[] { typeof(StringBuilder) })]
    [HarmonyPriority(100)]

    public static class TMPText_SetTextStringBuilder_Patch

    {

        public static void Prefix(StringBuilder sourceText)

        {

            if (TranslationEngine.Initialized && sourceText != null)

            {

                if (!UITextReentrancyGuard.TryEnter()) return;
                try
                {
                    string text = sourceText.ToString();
                    string translated = TranslationEngine.Translate(text);
                    sourceText.Clear();
                    sourceText.Append(translated);
                }
                finally
                {
                    UITextReentrancyGuard.Exit();
                }

            }

        }

        public static void Postfix(TMPro.TMP_Text __instance)

        {

            if (TranslationEngine.Initialized && __instance != null)

            {

                if (!UITextReentrancyGuard.TryEnter()) return;
                try
                {
                    FontUtils.ForceCyrillicFont(__instance);
                }
                finally
                {
                    UITextReentrancyGuard.Exit();
                }

            }

        }

    }



    [HarmonyPatch(typeof(TMPro.TextMeshPro), "Awake")]

    public static class TextMeshPro_Awake_Patch

    {

        public static void Postfix(TMPro.TextMeshPro __instance)

        {

            if (TranslationEngine.Initialized && __instance != null)

            {

                if (!UITextReentrancyGuard.TryEnter()) return;
                try
                {
                    FontUtils.ForceCyrillicFont(__instance);
                }
                finally
                {
                    UITextReentrancyGuard.Exit();
                }

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

                if (!UITextReentrancyGuard.TryEnter()) return;
                try
                {
                    FontUtils.ForceCyrillicFont(__instance);
                }
                finally
                {
                    UITextReentrancyGuard.Exit();
                }

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

                if (!UITextReentrancyGuard.TryEnter()) return;
                try
                {
                    FontUtils.ForceCyrillicFont(__instance);
                }
                finally
                {
                    UITextReentrancyGuard.Exit();
                }

            }

        }

    }



    // --- ДИНАМИЧЕСКИЙ ПАТЧ ДЛЯ MODERN UI (UI TOOLKIT / UIELEMENTS) ---

    public static class UIElementsDynamicPatch

    {

        // Prefix для SetValueWithoutNotify(string) — переводим ДО записи в backing field.
// Это срабатывает мгновенно (без 0.5с задержки), потому что Harmony перехватывает
// вызов метода до того, как значение уйдёт в поле.
//
// Сигнатура в Unity 6: SetValueWithoutNotify(System.String newValue). Harmony требует
// ТОЧНОГО совпадения имени параметра с целевым методом — поэтому пишем "newValue",
// а не "value". (Если переименовать в "value", Harmony не найдёт параметр в целевом
// методе и выбросит "Parameter 'value' not found in method" — именно это было в логе.)
        public static void INotifyValueChanged_SetValueWithoutNotify_Prefix(ref string newValue)

        {

            if (!TranslationEngine.Initialized) return;

            if (string.IsNullOrEmpty(newValue)) return;

            // Пропускаем уже переведённые строки (содержат кириллицу) — экономим CPU.
            bool alreadyCyr = false;

            for (int i = 0; i < newValue.Length; i++)

            {

                char c = newValue[i];

                if ((c >= 'Ѐ' && c <= 'ӿ') || c == 'ё' || c == 'Ё') { alreadyCyr = true; break; }

            }

            if (alreadyCyr) return;



            // Переводим через markup-aware путь (Modern UI часто оборачивает текст в <color=…>).
            string translated = TranslationEngine.TranslateMarkup(newValue);

            if (!string.IsNullOrEmpty(translated) && translated != newValue)

            {

                newValue = translated;

            }

        }



        public static void TextElement_Prefix(ref string value)

        {

            if (TranslationEngine.Initialized)

            {

                value = TranslationEngine.Translate(value);

            }

        }



        // 2026-07-05: TranslateVisualTree(root) отключён насовсем. Это рекурсивный обход
        // ВСЕГО дерева VisualElement через рефлексию (GetProperty("children")) БЕЗ лимита
        // глубины, запускаемый синхронно в момент UIDocument.OnEnable — то есть в тот же
        // кадр, когда Unity ещё достраивает/пересобирает виртуализированные строки списка.
        // При открытии торговли с большим инвентарём это стабильно давало access violation
        // 0xc0000005 в ntdll.dll (WER-логи: идентичный краш 02.07/04.07/05.07, всегда на
        // одном и том же смещении) — тот же класс бага, что уже диагностирован и вылечен
        // выше для RuntimeTranslator.WalkVisualTree (см. комментарии у DIAG_DISABLE_RUNTIME_
        // TRANSLATOR): обход дерева мидконструкции нативных UI-объектов через рефлексию.
        // try/catch тут не спасает — access violation нативный, managed-исключение не летит.
        // Функция избыточна: UIElements.TextElement.text уже патчен (TextElement_Prefix) и
        // переводит любой текст в момент его установки, включая новые строки списка лавки.
        public static void UIDocument_OnEnable_Postfix(object __instance)

        {

            return;

        }



        public static void VisualElement_ExecuteDefaultAction_Postfix(object __instance, object evt)

        {

            if (__instance == null || evt == null || !TranslationEngine.Initialized) return;

            try

            {

                System.Type type = __instance.GetType();

                var textProp = type.GetProperty("text", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                if (textProp != null && textProp.CanWrite && textProp.PropertyType == typeof(string))

                {

                    string currentText = (string)textProp.GetValue(__instance, null);

                    if (!string.IsNullOrEmpty(currentText))

                    {

                        string translated = TranslationEngine.Translate(currentText);

                        if (translated != currentText)

                        {

                            textProp.SetValue(__instance, translated, null);

                        }

                    }

                }



                var tooltipProp = type.GetProperty("tooltip", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                if (tooltipProp != null && tooltipProp.CanWrite && tooltipProp.PropertyType == typeof(string))

                {

                    string currentTooltip = (string)tooltipProp.GetValue(__instance, null);

                    if (!string.IsNullOrEmpty(currentTooltip))

                    {

                        string translated = TranslationEngine.Translate(currentTooltip);

                        if (translated != currentTooltip)

                        {

                            tooltipProp.SetValue(__instance, translated, null);

                        }

                    }

                }

            }

            catch (Exception ex)

            {

                UnityEngine.Debug.LogError("[RussianLocalization] VisualElement_ExecuteDefaultAction_Postfix error: " + ex.ToString());

            }

        }



        public static void TranslateVisualTree(object element)

        {

            if (element == null) return;

            try

            {

                System.Type type = element.GetType();



                var textProp = type.GetProperty("text", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                if (textProp != null && textProp.CanWrite && textProp.PropertyType == typeof(string))

                {

                    string currentText = (string)textProp.GetValue(element, null);

                    if (!string.IsNullOrEmpty(currentText))

                    {

                        string translated = TranslationEngine.Translate(currentText);

                        if (translated != currentText)

                        {

                            textProp.SetValue(element, translated, null);

                        }

                    }

                }



                var tooltipProp = type.GetProperty("tooltip", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                if (tooltipProp != null && tooltipProp.CanWrite && tooltipProp.PropertyType == typeof(string))

                {

                    string currentTooltip = (string)tooltipProp.GetValue(element, null);

                    if (!string.IsNullOrEmpty(currentTooltip))

                    {

                        string translated = TranslationEngine.Translate(currentTooltip);

                        if (translated != currentTooltip)

                        {

                            tooltipProp.SetValue(element, translated, null);

                        }

                    }

                }



                var childrenProp = type.GetProperty("children", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                if (childrenProp != null)

                {

                    var children = childrenProp.GetValue(element, null) as System.Collections.IEnumerable;

                    if (children != null)

                    {

                        foreach (var child in children)

                        {

                            TranslateVisualTree(child);

                        }

                    }

                }

            }

            catch (Exception ex)

            {

                UnityEngine.Debug.LogError("[RussianLocalization] TranslateVisualTree error: " + ex.ToString());

            }

        }

    }



    // --- ПАТЧИ ДЛЯ КЛАССИЧЕСКОГО ASCII-БУФЕРА (SCREENBUFFER) ---

    [HarmonyPatch(typeof(ConsoleLib.Console.ScreenBuffer))]

    public static class ScreenBuffer_Patch

    {

        // Диагностический кэш для лога "уже-кириллица-в-ScreenBuffer". Ограничен по размеру,
        // чтобы не разрастаться бесконтрольно при длинных сессиях.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _asciiInCyrSeen
            = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();
        private const int AsciiInCyrSeenCap = 2048;

        // Записывает в <ModPath>/ascii_input_cyrillic.txt строки, пришедшие в ScreenBuffer.Write
        // уже содержащими кириллицу. Это диагностика: если лог пуст — движок сначала зовёт наш Translate,
        // если непуст — игра где-то по дороге уже сама частично перевела / подмешала кириллицу.
        private static void LogAsciiInputAlreadyCyrillic(string marker, string value)
        {
            try
            {
                if (string.IsNullOrEmpty(value)) return;
                bool hasCyr = false;
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    if ((c >= 'Ѐ' && c <= 'ӿ') || c == 'ё' || c == 'Ё')
                    {
                        hasCyr = true; break;
                    }
                }
                if (!hasCyr) return;

                string trimmed = value.Trim();
                if (trimmed.Length < 2) return;

                if (_asciiInCyrSeen.Count > AsciiInCyrSeenCap) return;
                if (!_asciiInCyrSeen.TryAdd(trimmed, 0)) return;

                string entry = "[" + marker + "] " + trimmed + Environment.NewLine;
                if (!string.IsNullOrEmpty(TranslationEngine.CachedModPath))
                {
                    try
                    {
                        string p = System.IO.Path.Combine(TranslationEngine.CachedModPath, "ascii_input_cyrillic.txt");
                        System.IO.File.AppendAllText(p, entry, System.Text.Encoding.UTF8);
                    }
                    catch { /* некритично — диагностика */ }
                }
            }
            catch { /* никогда не ломаем игровой цикл из-за лога */ }
        }

        // 2026-07-05 (v2): те же Write/WriteAt-префиксы падали с идентичным access violation
        // 0xc0000005 в ntdll.dll, что и Description.get_Short/get_Long (см. Description_Patches
        // выше) — ScreenBuffer.Write у классического ASCII-интерфейса (используется, в частности,
        // экраном торговли) может вызывать сам себя реентрантно при отрисовке составных тайлов
        // (фон + иконка + рамка), и КАЖДЫЙ такой вложенный вызов заново прогонял тяжёлый
        // TranslationEngine.Translate() (десятки regex) поверх уже глубокого игрового стека
        // рендеринга. В большом магазине это стабильно роняло игру прямо на экране торговли.
        // Тот же приём, что и в Description_Patches: ThreadStatic-счётчик глубины, глубже которого
        // просто возвращаем оригинальный (непереведённый) текст, не добавляя стека.
        private const int MaxWriteRecursionDepth = 8;

        [ThreadStatic]
        private static int _writeDepth;

        [HarmonyPrefix]
        [HarmonyPriority(100)]

        [HarmonyPatch("Write", new Type[] { typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(bool), typeof(bool), typeof(bool) })]

        public static void Write_TilePrefix(ref string RenderString)

        {

            if (TranslationEngine.Initialized)

            {

                LogAsciiInputAlreadyCyrillic("Write(tile)", RenderString);

                if (_writeDepth >= MaxWriteRecursionDepth) return;
                _writeDepth++;
                try
                {
                    string trans = TranslationEngine.Translate(RenderString);
                    RenderString = TranslationEngine.Transliterate(trans);
                }
                finally
                {
                    _writeDepth--;
                }

            }

        }



        [HarmonyPrefix]
        [HarmonyPriority(100)]

        [HarmonyPatch("Write", new Type[] { typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(System.Collections.Generic.List<string>), typeof(int) })]

        public static void Write_Prefix(ref string s)

        {

            if (TranslationEngine.Initialized)

            {

                LogAsciiInputAlreadyCyrillic("Write", s);

                if (_writeDepth >= MaxWriteRecursionDepth) return;
                _writeDepth++;
                try
                {
                    string trans = TranslationEngine.Translate(s);
                    s = TranslationEngine.Transliterate(trans);
                }
                finally
                {
                    _writeDepth--;
                }

            }

        }



        [HarmonyPrefix]
        [HarmonyPriority(100)]

        [HarmonyPatch("WriteAt", new Type[] { typeof(int), typeof(int), typeof(string), typeof(bool) })]

        public static void WriteAt_Prefix1(ref string s)

        {

            if (TranslationEngine.Initialized)

            {

                LogAsciiInputAlreadyCyrillic("WriteAt", s);

                if (_writeDepth >= MaxWriteRecursionDepth) return;
                _writeDepth++;
                try
                {
                    string trans = TranslationEngine.Translate(s);
                    s = TranslationEngine.Transliterate(trans);
                }
                finally
                {
                    _writeDepth--;
                }

            }

        }



        [HarmonyPrefix]
        [HarmonyPriority(100)]

        [HarmonyPatch("WriteAt", new Type[] { typeof(XRL.World.Cell), typeof(string), typeof(bool) })]

        public static void WriteAt_Prefix2(ref string s)

        {

            if (TranslationEngine.Initialized)

            {

                LogAsciiInputAlreadyCyrillic("WriteAt(Cell)", s);

                if (_writeDepth >= MaxWriteRecursionDepth) return;
                _writeDepth++;
                try
                {
                    string trans = TranslationEngine.Translate(s);
                    s = TranslationEngine.Transliterate(trans);
                }
                finally
                {
                    _writeDepth--;
                }

            }

        }



        [HarmonyPrefix]
        [HarmonyPriority(100)]

        [HarmonyPatch("WriteAt", new Type[] { typeof(XRL.World.GameObject), typeof(string), typeof(bool) })]

        public static void WriteAt_Prefix3(ref string s)

        {

            if (TranslationEngine.Initialized)

            {

                LogAsciiInputAlreadyCyrillic("WriteAt(GO)", s);

                if (_writeDepth >= MaxWriteRecursionDepth) return;
                _writeDepth++;
                try
                {
                    string trans = TranslationEngine.Translate(s);
                    s = TranslationEngine.Transliterate(trans);
                }
                finally
                {
                    _writeDepth--;
                }

            }

        }

    }



    // --- ПАТЧИ ДЛЯ КЛАССА ОПИСАНИЙ (DESCRIPTION PART PATCHES) ---

    [HarmonyPatch(typeof(XRL.World.Parts.Description))]

    public static class Description_Patches

    {

        // 2026-07-05: get_Short/get_Long патчатся Harmony-постфиксом БЕЗУСЛОВНО (это
        // атрибутный [HarmonyPatch]-класс — активен всегда, независимо от диагностических
        // флагов RussianLocalization.Initialize). Игра может строить описание предмета
        // РЕКУРСИВНО (например, контейнер описывает вложенные предметы через тот же
        // get_Long/get_Short) — Harmony перехватывает КАЖДЫЙ такой вложенный вызов, и наш
        // Translate() (regex, словарные подстановки, StringBuilder) добавляет заметный расход
        // стека на КАЖДЫЙ уровень этой рекурсии. В большом магазине со сложными/вложенными
        // предметами это стабильно давало access violation 0xc0000005 в ntdll.dll при открытии
        // описания (краш воспроизводился даже с ПОЛНОСТЬЮ отключёнными 5 динамическими хуками
        // мода — то есть дело было именно здесь). Защита по глубине реентрантности: глубже
        // MaxDescriptionRecursionDepth уровней вложенности перевод пропускаем (возвращаем
        // оригинал как есть), чтобы не добавлять стек поверх и без того глубокой игровой рекурсии.
        //
        // 2026-07-05 (v7 — БИСЕКЦИЯ): cap=24 НЕ спас — краш воспроизвёлся сегодня ночью ЕЩЁ РАЗ
        // именно на открытии описания предмета в торговле, уже ПОСЛЕ полного отключения всех
        // 5 динамических хуков (DIAG_DISABLE_20260701_HOOKS=true) — то есть это единственный
        // оставшийся активный переводчик текста описания (игровой загрузчик модов сам патчит все
        // [HarmonyPatch]-классы через PatchAll при компиляции, независимо от кода Initialize —
        // отсюда и работал этот патч, хотя явного PatchAll() в файле нет). Чтобы окончательно
        // подтвердить виновника одним чистым тестом — полный выключатель: при true оба постфикса
        // становятся no-op (описание остаётся английским, но переполнить стек не может).
        // 2026-07-05 (v11): краш в торговле пережил ПОЛНОЕ отключение этого патча + PatchUIElements
        // + всей TMP-группы одновременно — этот патч тоже не виноват. Реальная причина: WinDbg-анализ
        // дампа (см. ru-qud-crash-fix-20260705-trade v11) дал FAILURE_BUCKET_ID указывающий на
        // gameoverlayrenderer64.dll (оверлей Steam), не на код мода. Флаг возвращён в false.
        // 2026-07-05/06 (v12): краш пережил и полный рестарт Steam с оверлеем выключенным —
        // выключаем этот патч снова для чистой точечной проверки ТОЛЬКО PatchPopup.
        public const bool DIAG_DISABLE_DESCRIPTION_HOOKS = false;

        private const int MaxDescriptionRecursionDepth = 24;

        [ThreadStatic]
        private static int _descriptionDepth;

        [HarmonyPostfix]

        [HarmonyPatch("get_Short")]

        public static void get_Short_Postfix(ref string __result)

        {

            if (DIAG_DISABLE_DESCRIPTION_HOOKS) return;
            if (!TranslationEngine.Initialized || string.IsNullOrEmpty(__result)) return;
            if (_descriptionDepth >= MaxDescriptionRecursionDepth) return;

            _descriptionDepth++;
            try
            {
                __result = TranslationEngine.Translate(__result);
            }
            finally
            {
                _descriptionDepth--;
            }

        }



        [HarmonyPostfix]

        [HarmonyPatch("get_Long")]

        public static void get_Long_Postfix(ref string __result)

        {

            if (DIAG_DISABLE_DESCRIPTION_HOOKS) return;
            if (!TranslationEngine.Initialized || string.IsNullOrEmpty(__result)) return;
            if (_descriptionDepth >= MaxDescriptionRecursionDepth) return;

            _descriptionDepth++;
            try
            {
                __result = TranslationEngine.Translate(__result);
            }
            finally
            {
                _descriptionDepth--;
            }
        }
    }

    // --- ПАТЧИ ДЛЯ TRANSLATION OF MEMORY DATABASES ---

    // ============================================================
    // СПРАВКА (Base/Manual.xml) — перевод страницы ДО подстановки клавиш.
    //
    // Цепочка отрисовки: XRLManualPage.GetData() -> HelpScreen.HelpMenu() кладёт результат в
    // HelpDataRow.HelpText -> HelpRow.setData() заменяет ~CmdLook и т.п. на {{hotkey|...}} по
    // текущей раскладке игрока -> UITextSkin.text + Apply().
    //
    // Перехватывать в конце (на Apply) для страниц с ~Cmd-токенами бесполезно: к тому моменту
    // текст уже зависит от раскладки, и точное совпадение по словарю не сойдётся. Поэтому
    // переводим на входе — здесь страница ещё ровно такая, как в Manual.xml, и ключ стабилен.
    // Перевод обязан сохранять ~Cmd-токены: подстановку клавиш игра сделает сама, уже по русскому
    // тексту.
    //
    // Только точное совпадение: страницы большие (до ~8.5 КБ), полный Translate() с прогоном по
    // patternDictionary вешает игру при открытии справки.
    [HarmonyPatch(typeof(XRL.Help.XRLManualPage))]
    public static class XRLManualPage_GetData_Patch
    {
        [HarmonyPostfix]
        [HarmonyPatch("GetData", new Type[] { typeof(bool) })]
        public static void GetData_Postfix(ref string __result)
        {
            try
            {
                if (!TranslationEngine.Initialized || string.IsNullOrEmpty(__result)) return;

                string translated;
                if (TranslationEngine.TryTranslateExactPreservingPadding(__result, out translated))
                {
                    __result = translated;
                }
            }
            catch { /* справка не должна ронять UI */ }
        }
    }

    [HarmonyPatch(typeof(XRL.World.QuestLoader))]
    public static class QuestLoader_Patch
    {
        [HarmonyPostfix]
        [HarmonyPatch("LoadQuests")]
        public static void LoadQuests_Postfix(XRL.World.QuestLoader __instance)
        {
            if (!TranslationEngine.Initialized || __instance == null) return;
            try
            {
                var quests = __instance.QuestsByID;
                if (quests != null)
                {
                    foreach (var kvp in quests)
                    {
                        var quest = kvp.Value;
                        if (quest == null) continue;
                        
                        if (!string.IsNullOrEmpty(quest.Name))
                            quest.Name = TranslationEngine.TranslateTextStrict(quest.Name);
                        if (!string.IsNullOrEmpty(quest.Accomplishment))
                            quest.Accomplishment = TranslationEngine.TranslateTextStrict(quest.Accomplishment);
                        if (!string.IsNullOrEmpty(quest.Achievement))
                            quest.Achievement = TranslationEngine.TranslateTextStrict(quest.Achievement);
                        if (!string.IsNullOrEmpty(quest.Gospel))
                            quest.Gospel = TranslationEngine.TranslateTextStrict(quest.Gospel);
                        if (!string.IsNullOrEmpty(quest.Hagiograph))
                            quest.Hagiograph = TranslationEngine.TranslateTextStrict(quest.Hagiograph);
                            
                        if (quest.StepsByID != null)
                        {
                            foreach (var stepKvp in quest.StepsByID)
                            {
                                var step = stepKvp.Value;
                                if (step == null) continue;
                                if (!string.IsNullOrEmpty(step.Name))
                                    step.Name = TranslationEngine.TranslateTextStrict(step.Name);
                                if (!string.IsNullOrEmpty(step.Text))
                                    step.Text = TranslationEngine.TranslateTextStrict(step.Text);
                            }
                        }
                    }
                    UnityEngine.Debug.Log("[RussianLocalization] Translated loaded Quests in memory.");
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[RussianLocalization] Quest translation error: " + ex.ToString());
            }
        }
    }

    [HarmonyPatch(typeof(XRL.World.Conversations.ConversationLoader))]
    public static class ConversationLoader_Patch
    {
        [HarmonyPostfix]
        [HarmonyPatch("LoadConversations")]
        public static void LoadConversations_Postfix()
        {
            if (!TranslationEngine.Initialized) return;
            try
            {
                var blueprints = XRL.World.Conversations.Conversation.Blueprints;
                if (blueprints != null)
                {
                    foreach (var kvp in blueprints)
                    {
                        TranslateBlueprint(kvp.Value);
                    }
                    UnityEngine.Debug.Log("[RussianLocalization] Translated loaded Conversations in memory.");
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[RussianLocalization] Conversation translation error: " + ex.ToString());
            }
        }

        private static void TranslateBlueprint(XRL.World.Conversations.ConversationXMLBlueprint blueprint)
        {
            if (blueprint == null) return;
            try
            {
                if (!string.IsNullOrEmpty(blueprint.Text))
                {
                    blueprint.Text = TranslationEngine.TranslateTextStrict(blueprint.Text);
                }
                if (blueprint.Attributes != null && blueprint.Attributes.TryGetValue("text", out string attrText))
                {
                    if (!string.IsNullOrEmpty(attrText))
                    {
                        blueprint.Attributes["text"] = TranslationEngine.TranslateTextStrict(attrText);
                    }
                }
                if (blueprint.Children != null)
                {
                    foreach (var child in blueprint.Children)
                    {
                        TranslateBlueprint(child);
                    }
                }
            }
            catch {}
        }
    }

    [HarmonyPatch(typeof(XRL.World.Skills.SkillFactory))]
    public static class SkillFactory_Patch
    {
        private static bool hasTranslated = false;
        private static readonly object translateLock = new object();

        [HarmonyPostfix]
        [HarmonyPatch("Factory", MethodType.Getter)]
        public static void Factory_Getter_Postfix(XRL.World.Skills.SkillFactory __result)
        {
            if (!TranslationEngine.Initialized || __result == null) return;
            lock (translateLock)
            {
                if (hasTranslated) return;
                hasTranslated = true;
            }
            try
            {
                var skills = __result.SkillList;
                if (skills != null)
                {
                    foreach (var kvp in skills)
                    {
                        var entry = kvp.Value;
                        if (entry == null) continue;
                        if (!string.IsNullOrEmpty(entry.Name))
                            entry.Name = TranslationEngine.TranslateTextStrict(entry.Name);
                        if (!string.IsNullOrEmpty(entry.Description))
                            entry.Description = TranslationEngine.TranslateTextStrict(entry.Description);
                            
                        if (entry.PowerList != null)
                        {
                            foreach (var power in entry.PowerList)
                            {
                                if (power == null) continue;
                                if (!string.IsNullOrEmpty(power.Name))
                                    power.Name = TranslationEngine.TranslateTextStrict(power.Name);
                                if (!string.IsNullOrEmpty(power.Description))
                                    power.Description = TranslationEngine.TranslateTextStrict(power.Description);
                            }
                        }
                    }
                    UnityEngine.Debug.Log("[RussianLocalization] Translated loaded Skills in memory.");
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[RussianLocalization] Skill translation error: " + ex.ToString());
            }
        }
    }

    [HarmonyPatch(typeof(XRL.GenotypeFactory))]
    public static class GenotypeFactory_Patch
    {
        [HarmonyPostfix]
        [HarmonyPatch("Init")]
        public static void Init_Postfix()
        {
            if (!TranslationEngine.Initialized) return;
            try
            {
                var genotypes = XRL.GenotypeFactory.GenotypesByName;
                if (genotypes != null)
                {
                    foreach (var kvp in genotypes)
                    {
                        var entry = kvp.Value;
                        if (entry == null) continue;
                        if (!string.IsNullOrEmpty(entry.DisplayName))
                            entry.DisplayName = TranslationEngine.TranslateTextStrict(entry.DisplayName);
                        if (entry.ExtraInfo != null)
                        {
                            for (int i = 0; i < entry.ExtraInfo.Count; i++)
                            {
                                if (!string.IsNullOrEmpty(entry.ExtraInfo[i]))
                                {
                                    entry.ExtraInfo[i] = TranslationEngine.TranslateTextStrict(entry.ExtraInfo[i]);
                                }
                            }
                        }
                    }
                    UnityEngine.Debug.Log("[RussianLocalization] Translated loaded Genotypes in memory.");
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[RussianLocalization] Genotype translation error: " + ex.ToString());
            }
        }
    }

    [HarmonyPatch(typeof(XRL.SubtypeFactory))]
    public static class SubtypeFactory_Patch
    {
        // __result типизирован как object, а не List<SubtypeEntry>: в разных версиях игры
        // геттер Classes возвращает разный тип (SubtypeEntry vs SubtypeClass). Жёсткий тип
        // ломает привязку Harmony ("Cannot assign method return type"), а это прерывает весь
        // PatchAll и отключает все патчи, объявленные ниже (история, деревни, хоткей F1).
        // Поэтому работаем через рефлексию — это совместимо с любой версией.
        [HarmonyPostfix]
        [HarmonyPatch("Classes", MethodType.Getter)]
        public static void Classes_Getter_Postfix(object __result)
        {
            TranslateSubtypes(__result);
        }

        [HarmonyPostfix]
        [HarmonyPatch("Subtypes", MethodType.Getter)]
        public static void Subtypes_Getter_Postfix(object __result)
        {
            TranslateSubtypes(__result);
        }

        private static string GetStr(object obj, string member)
        {
            var t = obj.GetType();
            var f = t.GetField(member);
            if (f != null && f.FieldType == typeof(string)) return f.GetValue(obj) as string;
            var p = t.GetProperty(member);
            if (p != null && p.PropertyType == typeof(string) && p.CanRead) return p.GetValue(obj) as string;
            return null;
        }

        private static void SetStr(object obj, string member, string value)
        {
            var t = obj.GetType();
            var f = t.GetField(member);
            if (f != null && f.FieldType == typeof(string)) { f.SetValue(obj, value); return; }
            var p = t.GetProperty(member);
            if (p != null && p.PropertyType == typeof(string) && p.CanWrite) p.SetValue(obj, value);
        }

        private static void TranslateSubtypes(object resultObj)
        {
            if (!TranslationEngine.Initialized) return;
            var subtypes = resultObj as System.Collections.IEnumerable;
            if (subtypes == null) return;
            try
            {
                foreach (var entry in subtypes)
                {
                    if (entry == null) continue;
                    string dn = GetStr(entry, "DisplayName");
                    if (!string.IsNullOrEmpty(dn))
                        SetStr(entry, "DisplayName", TranslationEngine.TranslateTextStrict(dn));

                    var ei = entry.GetType().GetField("ExtraInfo");
                    if (ei != null)
                    {
                        var lst = ei.GetValue(entry) as System.Collections.Generic.List<string>;
                        if (lst != null)
                        {
                            for (int i = 0; i < lst.Count; i++)
                            {
                                if (!string.IsNullOrEmpty(lst[i]))
                                    lst[i] = TranslationEngine.TranslateTextStrict(lst[i]);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[RussianLocalization] Subtype translation error: " + ex.ToString());
            }
        }
    }

    [HarmonyPatch(typeof(XRL.MutationFactory))]
    public static class MutationFactory_Patch
    {
        private static bool _done;

        // Переводит строковый член (поле ИЛИ записываемое свойство) на месте.
        // Безопасно: если члена нет или он не записываемая строка — ничего не делает.
        private static void TrMember(object obj, string member)
        {
            if (obj == null) return;
            var t = obj.GetType();
            var f = t.GetField(member);
            if (f != null && f.FieldType == typeof(string))
            {
                var v = f.GetValue(obj) as string;
                if (!string.IsNullOrEmpty(v))
                    f.SetValue(obj, TranslationEngine.TranslateTextStrict(v));
                return;
            }
            var p = t.GetProperty(member);
            if (p != null && p.PropertyType == typeof(string) && p.CanRead && p.CanWrite)
            {
                var v = p.GetValue(obj) as string;
                if (!string.IsNullOrEmpty(v))
                    p.SetValue(obj, TranslationEngine.TranslateTextStrict(v));
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("MutationsByName", MethodType.Getter)]
        public static void MutationsByName_Getter_Postfix(object __result)
        {
            if (_done || !TranslationEngine.Initialized) return;
            try
            {
                // Категории мутаций (заголовки в пикере): только описание/заголовок, не id.
                var cats = XRL.MutationFactory.CategoriesByName as System.Collections.IDictionary;
                if (cats != null)
                {
                    foreach (System.Collections.DictionaryEntry de in cats)
                    {
                        TrMember(de.Value, "DisplayName");
                        TrMember(de.Value, "Help");
                    }
                }

                // Записи мутаций: имя (XMLDisplayName) + описания. Name/Class/Type НЕ трогаем (это id).
                var byName = __result as System.Collections.IDictionary;
                if (byName != null)
                {
                    foreach (System.Collections.DictionaryEntry de in byName)
                    {
                        var e = de.Value;
                        TrMember(e, "XMLDisplayName");
                        TrMember(e, "DisplayName");
                        TrMember(e, "Help");
                        TrMember(e, "Snippet");
                        TrMember(e, "BearerDescription");
                    }
                    _done = true;
                    UnityEngine.Debug.Log("[RussianLocalization] Translated loaded Mutations in memory.");
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[RussianLocalization] Mutation translation error: " + ex.ToString());
            }
        }
    }

    [HarmonyPatch(typeof(HistoryKit.HistoricStringExpander))]
    public static class HistoricStringExpander_Patch
    {
        [HarmonyPostfix]
        [HarmonyPatch("ExpandString", new Type[] { typeof(string), typeof(System.Random) })]
        public static void ExpandString_Postfix1(ref string __result)
        {
            if (TranslationEngine.Initialized && !string.IsNullOrEmpty(__result))
            {
                __result = TranslationEngine.Translate(__result);
            }
        }
    }

    [HarmonyPatch(typeof(Qud.API.HistoryAPI))]
    public static class HistoryAPI_Patch
    {
        [HarmonyPostfix]
        [HarmonyPatch("ExpandVillageText", new Type[] { typeof(string), typeof(string), typeof(HistoryKit.HistoricEntitySnapshot) })]
        public static void ExpandVillageText_Postfix1(ref string __result)
        {
            if (TranslationEngine.Initialized && !string.IsNullOrEmpty(__result))
            {
                __result = TranslationEngine.Translate(__result);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("ExpandVillageText", new Type[] { typeof(StringBuilder), typeof(string), typeof(HistoryKit.HistoricEntitySnapshot) })]
        public static void ExpandVillageText_Postfix2(StringBuilder Text)
        {
            if (TranslationEngine.Initialized && Text != null && Text.Length > 0)
            {
                string original = Text.ToString();
                string translated = TranslationEngine.Translate(original);
                if (translated != original)
                {
                    Text.Clear();
                    Text.Append(translated);
                }
            }
        }
    }

    // --- ХОТКЕЙ ДЛЯ ВКЛ/ВЫКЛ ПЕРЕВОДА ---
    // По умолчанию F1. Использует WinAPI GetAsyncKeyState — работает на уровне ОС,
    // не зависит от игровой системы ввода.
    [HarmonyPatch]
    public static class ToggleHotkey_Patch
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private const int VK_F1 = 0x70;
        private static bool _wasPressed = false;

        // Вешаемся на ScreenBuffer.Write — он вызывается каждый кадр при отрисовке.
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ConsoleLib.Console.ScreenBuffer), "Write",
            new System.Type[] { typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(bool), typeof(bool), typeof(bool) })]
        public static void Write_Prefix()
        {
            if (!TranslationEngine.Initialized) return;

            bool isPressed = (GetAsyncKeyState(VK_F1) & 0x8000) != 0;
            if (isPressed && !_wasPressed)
            {
                TranslationEngine.IsEnabled = !TranslationEngine.IsEnabled;
                UnityEngine.Debug.Log("[RussianLocalization] перевод " +
                    (TranslationEngine.IsEnabled ? "включён" : "отключён") +
                    " (F1)");
            }
            _wasPressed = isPressed;
        }
    }

    [HarmonyPatch(typeof(XRL.UI.Look))]
    public static class Look_Patch
    {
        [HarmonyPostfix]
        [HarmonyPatch("GenerateTooltipContent", new Type[] { typeof(XRL.World.GameObject) })]
        public static void GenerateTooltipContent_Postfix(ref string __result)
        {
            if (TranslationEngine.Initialized && TranslationEngine.IsEnabled && !string.IsNullOrEmpty(__result))
            {
                // FIX B3 (2026-07-20): дебаг-запись в generate_tooltip_output.txt — только при флаге
                if (TranslationEngine.DebugFileLogging)
                {
                    try
                    {
                        string path = Path.Combine(TranslationEngine.CachedModPath, "generate_tooltip_output.txt");
                        File.AppendAllText(path, "--- TOOLTIP START ---\n" + __result + "\n--- TOOLTIP END ---\n", Encoding.UTF8);
                    }
                    catch {}
                }

                __result = TranslationEngine.Translate(__result);
            }
        }
    }

    // Показ окна о двойной установке. Окно показывает копия-ПОБЕДИТЕЛЬ: у проигравшей
    // нет ни одного хука (она вышла из Initialize до патчинга), а победитель полностью
    // инициализирован и уже знает о дубликатах — он их перечислял.
    //
    // Момент показа: [CallAfterGameLoaded], то есть при входе в игру, а не в главном
    // меню. Готового хука «UI ожил на главном меню» у Qud нет, а самодельный на
    // MonoBehaviour.Update со счётчиком кадров — ровно тот класс кода, который в этом
    // моде уже дважды приводил к крашам на загрузке (см. историю RuntimeTranslator).
    // После фикса игра запускается нормально, так что до загрузки персонажа игрок дойдёт.
    [HasCallAfterGameLoaded]
    public static class DuplicateInstallNotice
    {
        [CallAfterGameLoaded]
        public static void OnGameLoaded()
        {
            try { TranslationEngine.ShowDuplicateInstallNoticeOnce(); }
            catch { /* уведомление не должно ломать загрузку игры */ }
        }
    }

}