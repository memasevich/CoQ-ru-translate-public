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

        public static string CachedModPath = null;

        public static int disableWordReplacementCounter;

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

        private static HashSet<string> loggedStrings = new HashSet<string>();

        private static object LogLock = new object();



        // Потокобезопасный сборщик пословных автозамен (для отлова Франкенштейнов)

        private static HashSet<string> loggedReplacements = new HashSet<string>();

        private static object ReplacementLogLock = new object();



        // Потокобезопасный сборщик вообще всего игрового текста (и русского, и английского)

        private static HashSet<string> loggedAllTexts = new HashSet<string>();

        private static object AllTextLogLock = new object();



        private static readonly System.Text.RegularExpressions.Regex TagRegex = new System.Text.RegularExpressions.Regex(@"<[^>]+>");

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

        public static void Initialize()
        {
            lock (FileLock)

            {

                if (Initialized) return;

                

                try

                {

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

                                string normKey = kvp.Key.Replace('\u00A0', ' ')

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

                            foreach (var property in patternObj.Properties())

                            {

                                string patternKey = property.Name;

                                string patternValue = property.Value.ToString();

                                if (string.IsNullOrEmpty(patternKey)) continue;

                                try

                                {

                                    var regex = new System.Text.RegularExpressions.Regex(patternKey, System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                                    patternDictionary.Add(new KeyValuePair<System.Text.RegularExpressions.Regex, string>(regex, patternValue));

                                }

                                catch (Exception regexEx)

                                {

                                    LogError("[RussianLocalization] Failed to compile pattern regex '" + patternKey + "': " + regexEx.Message);

                                }

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

                    Initialized = true;
                    CachedModPath = modPath;

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

                    // Динамический патч для Modern UI (UI Toolkit / UIElements)

                    PatchUIElements();

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



        public static string Translate(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // Если строка содержит кириллицу И не содержит английских букв, 
            // значит она полностью переведена. Пропускаем.
            if (ContainsCyrillic(text) && !ContainsEnglish(text)) return text;

            if (InternalGameKeys.Contains(text.Trim())) return text;

            if (translationCache.Count > 50000) translationCache.Clear();

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

            if (result != null)
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
                
                // Исправление 4-х скобок (результат двойного прохода)
                if (result.Contains("{{{{"))
                {
                    result = result.Replace("{{{{", "{{").Replace("}}}}", "}}");
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

                result = result.Replace("&у", "&y").Replace("&У", "&Y")

                               .Replace("&р", "&r").Replace("&Р", "&R")

                               .Replace("&с", "&c").Replace("&С", "&C")

                               .Replace("&в", "&w").Replace("&В", "&W")

                               .Replace("&м", "&m").Replace("&М", "&M")

                               .Replace("&г", "&g").Replace("&Г", "&G")

                               .Replace("&б", "&b").Replace("&Б", "&B")

                               .Replace("&д", "&d").Replace("&Д", "&D")

                               .Replace("&к", "&k").Replace("&К", "&K")

                               .Replace("&о", "&o").Replace("&О", "&O");



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
                // Удаляем пустые блоки <color=...></color>
                result = System.Text.RegularExpressions.Regex.Replace(result, @"<color=[^>]+></color>", "");
                // Удаляем висящий закрывающий тег в самом начале
                if (result.StartsWith("</color>")) result = result.Substring(8);
            }

            // if (text == " serving]") // Console.WriteLine($"[DEBUG Translate] final returned result: '{result}'");
            LogAllGameplayText(text, result);

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

            return text;
        }


        public static string TryTranslatePattern(string text, out bool success)

        {

            success = false;

            if (string.IsNullOrEmpty(text)) return text;

            bool hasLogPrefix = text.StartsWith(":: ");
            string matchText = hasLogPrefix ? text.Substring(3) : text;

            for (int i = 0; i < patternDictionary.Count; i++)

            {

                var rule = patternDictionary[i];

                var regex = rule.Key;

                var match = regex.Match(matchText);

                if (match.Success && match.Index == 0 && match.Length == matchText.Length)

                {

                    string template = rule.Value;
                    var placeholderRegex = new System.Text.RegularExpressions.Regex(@"\{(?<name>[a-zA-Z0-9_]+)(?::(?<case>[a-z]+))?\}");

                    string result = placeholderRegex.Replace(template, (placeholderMatch) =>
                    {
                        string name = placeholderMatch.Groups["name"].Value;
                        string caseName = placeholderMatch.Groups["case"].Value;
                        var group = match.Groups[name];
                        if (group.Success)
                        {
                            if (!string.IsNullOrEmpty(caseName))
                            {
                                return TranslateFactionCase(group.Value, caseName);
                            }
                            else
                            {
                                return TranslateText(group.Value, true);
                            }
                        }
                        return placeholderMatch.Value;
                    });
                    
                    success = true;
                    return hasLogPrefix ? ":: " + result : result;

                }

            }

            return text;

        }



        private static bool IsKeyName(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string s = text.ToLower();
            return s == "space" || s == "enter" || s == "esc" || s == "escape" || s == "tab" ||
                   s == "backspace" || s == "insert" || s == "delete" || s == "home" || s == "end" ||
                   s == "pgup" || s == "pgdn" || s == "pageup" || s == "pagedown" ||
                   s.StartsWith("num ") || s.StartsWith("numpad") || s.StartsWith("f") && s.Length <= 3;
        }

        private static string TranslateInternal(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // Восстановление битых кавычек из-за кодировок консоли
            text = text.Replace('½', '«').Replace('╗', '»');


            // Защита горячих клавиш и системных имен
            string sn = text.Trim().ToLower();
            if (sn.Length == 1 && ((sn[0] >= 'a' && sn[0] <= 'z') || (sn[0] >= '0' && sn[0] <= '9')))
            {
                return text; // Не переводим одиночные буквы/цифры (хоткеи)
            }
            if (IsKeyName(sn))
            {
                return text; // Не переводим системные имена клавиш
            }

            // Очищаем \r для предотвращения поломки ключей при переносах строк в Windows
            text = text.Replace("\r", "");

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
            if (IsKeyName(sn))
            {
                return text; // Не переводим системные имена клавиш
            }

            // Очищаем \r для предотвращения поломки ключей при переносах строк в Windows
            text = text.Replace("\r", "");

            string earlyTrimmed = text.Replace('\u00A0', ' ')
                                      .Replace('\u2007', ' ')
                                      .Replace('\u200B', ' ')
                                      .Replace('\u202F', ' ')
                                      .Trim();

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

                    string result = prefix + earlyExactMatch + suffix;
                    translationCache[text] = result;
                    return result;
                }
            }

            // БЫСТРЫЙ ВЫХОД ДЛЯ ОГРОМНЫХ ТЕКСТОВ:
            // Если строка > 3000 символов (справка, титры) и её нет в словаре/кэше,
            // мы не пускаем её в тяжёлую пословную обработку, чтобы не вешать игру.
            if (text.Length > 3000)
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

                string result = rusPrefix + translatedEng;

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



                string result = prefix + exactMatch + suffix;

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
                bool isKeyName = snFull.Length == 1 ||
                                 snFull == "esc" ||
                                 snFull == "tab" ||
                                 snFull == "enter" ||
                                 snFull == "space" ||
                                 snFull == "backspace" ||
                                 snFull == "insert" || 
                                 snFull == "delete" ||
                                 snFull == "up" ||
                                 snFull == "down" ||
                                 snFull == "left" ||
                                 snFull == "right";
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
                            string result = prefix + restoredExact + suffix;
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
                        translationCache[text] = result;
                        return result;
                    }

                    string strippedSn = SuperNormalize(strippedText);
                    string strippedOrigKey;
                    if (normalizedKeyDictionary.TryGetValue(strippedSn, out strippedOrigKey))
                    {
                        if (staticDictionary.TryGetValue(strippedOrigKey, out strippedExact))
                        {
                            string result = text.Contains("<color=") ? DistributeColors(text, strippedExact) : strippedExact;
                            translationCache[text] = result;
                            return result;
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

            translationCache[text] = processedText;

            return processedText;

        }



        public static string TranslateMarkup(string text)

        {

            if (string.IsNullOrEmpty(text)) return text;



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

                        }

                        else if (i < len - 1 && text[i] == '{' && text[i + 1] == '{')

                        {

                            braceCount++;

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

                        result.Append("{{" + left + "|" + TranslateMarkup(right) + "}}");

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

                end--;

            }



            prefix = text.Substring(0, start);

            core = text.Substring(start, end - start + 1);

            suffix = text.Substring(end + 1);
        }

        public static string TranslateFactionCase(string englishFaction, string caseName)
        {
            if (string.IsNullOrEmpty(englishFaction)) return englishFaction;
            string key = englishFaction.Trim();
            if (factionCases.TryGetValue(key, out var cases))
            {
                if (cases.TryGetValue(caseName, out string trans))
                {
                    return trans;
                }
            }
            return TranslateInternal(key);
        }

        public static string TranslateText(string text, bool forceWordReplacement = false)

        {

            if (string.IsNullOrEmpty(text)) return text;

            if (InternalGameKeys.Contains(text.Trim())) return text;

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



            string normalizedCore = core.Replace('\u00A0', ' ')

                                        .Replace('\u2007', ' ')

                                        .Replace('\u200B', ' ')

                                        .Replace('\u202F', ' ');



            string trimmedCore = normalizedCore.Trim();

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
                
                bool isKeyName = sn.Length == 1 || 
                                 sn == "esc" || 
                                 sn == "tab" || 
                                 sn == "enter" || 
                                 sn == "space" || 
                                 sn == "backspace" || 
                                 sn == "insert" || 
                                 sn == "delete" || 
                                 sn == "up" || 
                                 sn == "down" || 
                                 sn == "left" || 
                                 sn == "right";
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
                // Shadow Matching: Теневое сопоставление для боевого лога и предметов
                // Отсекаем хвосты вроде "! [18]", "(unburnt)", " x5"
                var shadowMatch = ShadowRegex.Match(trimmedCore);
                if (shadowMatch.Success)
                {
                    string shadowCore = shadowMatch.Groups["core"].Value.Trim();
                    string decoration = shadowMatch.Groups["deco"].Value;
                    string translatedShadowCore;
                    if (staticDictionary.TryGetValue(shadowCore, out translatedShadowCore))
                    {
                        translatedCore = translatedShadowCore + decoration.Replace(" vs ", " против ");
                    }
                    else
                    {
                        // Попытка супер-нормализации для ядра тени
                        string ssn = SuperNormalize(shadowCore);
                        string origKey;
                        if (normalizedKeyDictionary.TryGetValue(ssn, out origKey))
                        {
                            if (staticDictionary.TryGetValue(origKey, out translatedShadowCore))
                            {
                                string restoredShadow = RestoreStrippedPunctuation(shadowCore, origKey, translatedShadowCore);
                                translatedCore = restoredShadow + decoration.Replace(" vs ", " против ");
                            }
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(translatedCore))
            {
                // Защита от "Франкенштейнов": не пытаемся переводить пословно длинные предложения,
                // так как это портит грамматику и делает текст нечитаемым.
                // Пословный перевод разрешен только для коротких фраз (до 3 слов).
                int wordCount = trimmedCore.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
                bool endsWithSentencePunct = trimmedCore.EndsWith(".") || trimmedCore.EndsWith("!") || trimmedCore.EndsWith("?");
                int maxWords = endsWithSentencePunct ? 3 : 5;
                // Console.WriteLine($"[DEBUG TranslateText] trimmedCore='{trimmedCore}', wordCount={wordCount}, forceWordReplacement={forceWordReplacement}, disableWordReplacementCounter={disableWordReplacementCounter}, sortedWordKeys count={sortedWordKeys.Count}");
                if ((wordCount <= maxWords || forceWordReplacement) && disableWordReplacementCounter == 0)
                {
                    translatedCore = TryWordReplacement(normalizedCore);
                    // Console.WriteLine($"[DEBUG TranslateText] TryWordReplacement returned '{translatedCore}'");
                    if (translatedCore != normalizedCore)
                    {
                        LogWordReplacement(normalizedCore, translatedCore);
                    }
                }
                else
                {
                    if (trimmedCore.Contains(", "))
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
                        translatedCore = normalizedCore;
                    }
                }

                if (ContainsEnglish(translatedCore))
                {
                    LogUntranslated(trimmedCore);
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



        private static string TryWordReplacement(string text)

        {

            string result = text;



            for (int i = 0; i < sortedWordKeys.Count; i++)

            {

                string key = sortedWordKeys[i];

                if (string.IsNullOrEmpty(key)) continue;



                string translation;

                if (wordDictionary.TryGetValue(key, out translation))

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

                                isWordBoundaryStart = false;

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
                                string matchedWord = result.Substring(index, key.Length);
                                bool isAllLower = true;
                                bool isAllUpper = true;
                                for (int c = 0; c < matchedWord.Length; c++)
                                {
                                    if (char.IsUpper(matchedWord[c])) isAllLower = false;
                                    if (char.IsLower(matchedWord[c])) isAllUpper = false;
                                }

                                if (isAllLower)
                                {
                                    finalTrans = finalTrans.ToLower();
                                }
                                else if (isAllUpper)
                                {
                                    finalTrans = finalTrans.ToUpper();
                                }
                                else if (char.IsUpper(matchedWord[0]))
                                {
                                    finalTrans = char.ToUpper(finalTrans[0]) + finalTrans.Substring(1);
                                }



                                result = result.Remove(index, key.Length).Insert(index, finalTrans);

                                index = result.IndexOf(key, index + finalTrans.Length, StringComparison.OrdinalIgnoreCase);

                            }

                            else
                            {
                                result = result.Remove(index, key.Length);
                                index = result.IndexOf(key, index, StringComparison.OrdinalIgnoreCase);
                            }

                        }

                        else

                        {

                            index = result.IndexOf(key, index + 1, StringComparison.OrdinalIgnoreCase);

                        }

                    }

                }

            }



            return result;

        }



        private static bool ContainsCyrillic(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if ((c >= '\u0430' && c <= '\u044f') || (c >= '\u0410' && c <= '\u042f') || c == '\u0451' || c == '\u0401')
                {
                    return true;
                }
            }
            return false;
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



        private static void AppendToLogFile(string filename, string content)

        {

            if (!string.IsNullOrEmpty(CachedModPath))

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



            // Пишем в Документы пользователя ТОЛЬКО all_gameplay_texts.txt

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

                    string logPath = Path.Combine(targetFolder, filename);

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



                lock (LogLock)

                {

                    if (!loggedStrings.Contains(trimmed))

                    {

                        loggedStrings.Add(trimmed);

                        AppendToLogFile("untranslated.txt", trimmed + Environment.NewLine);

                    }

                }

            }

            catch {}

        }



        private static void LogWordReplacement(string original, string replaced)

        {

            try

            {

                if (string.IsNullOrEmpty(original) || original == replaced) return;



                lock (ReplacementLogLock)

                {

                    if (!loggedReplacements.Contains(original))

                    {

                        loggedReplacements.Add(original);

                        string logEntry = "[Original]: " + original + Environment.NewLine +

                                          "[Replaced]: " + replaced + Environment.NewLine +

                                          "--------------------------------------------------" + Environment.NewLine;

                        AppendToLogFile("word_replacements.txt", logEntry);

                    }

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

                lock (AllTextLogLock)
                {
                    if (!loggedAllTexts.Contains(trimmed))
                    {
                        loggedAllTexts.Add(trimmed);
                        string fullEntry = "---" + Environment.NewLine + 
                                           "[RAW]: " + original + Environment.NewLine + 
                                           "[RES]: " + translated + Environment.NewLine;
                        AppendToLogFile("all_gameplay_texts.txt", fullEntry);
                    }
                }
            }
            catch {}
        }



        private static bool IsJunkText(string text)
        {
            if (string.IsNullOrEmpty(text)) return true;
            string trimmed = text.Trim();
            if (trimmed.Length <= 1) return true;

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

            }

            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[RussianLocalization] UIElements dynamic patch error: " + ex.ToString());
            }
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

            if (InternalGameKeys.Contains(text.Trim())) return text;

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

            string normalizedCore = core.Replace('\u00A0', ' ')
                                        .Replace('\u2007', ' ')
                                        .Replace('\u200B', ' ')
                                        .Replace('\u202F', ' ');
            string trimmedCore = normalizedCore.Trim();
            string translatedCore = "";

            string exactMatch;
            if (staticDictionary.TryGetValue(trimmedCore, out exactMatch))
            {
                translatedCore = exactMatch;
            }
            else
            {
                string sn = SuperNormalize(trimmedCore);
                bool isKeyName = sn.Length == 1 || sn == "esc" || sn == "tab" || sn == "enter" || sn == "space" || sn == "backspace" || sn == "insert" || sn == "delete" || sn == "up" || sn == "down" || sn == "left" || sn == "right";
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



                        public static bool ContainsRussian(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if ((c >= '\u0430' && c <= '\u044f') || (c >= '\u0410' && c <= '\u042f') || c == '\u0451' || c == '\u0401')
                {
                    return true;
                }
            }
            return false;
        }



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

        public static void Postfix(TMPro.TMP_Text __instance)

        {

            if (TranslationEngine.Initialized && __instance != null)

            {

                FontUtils.ForceCyrillicFont(__instance);

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

        public static void Postfix(TMPro.TMP_Text __instance)

        {

            if (TranslationEngine.Initialized && __instance != null)

            {

                FontUtils.ForceCyrillicFont(__instance);

            }

        }

    }



    [HarmonyPatch(typeof(TMPro.TMP_Text), "SetText", new Type[] { typeof(string), typeof(bool) })]

    public static class TMPText_SetText_Bool_Patch

    {

        public static void Prefix(ref string sourceText)

        {

            if (TranslationEngine.Initialized)

            {

                sourceText = TranslationEngine.Translate(sourceText);

            }

        }

        public static void Postfix(TMPro.TMP_Text __instance)

        {

            if (TranslationEngine.Initialized && __instance != null)

            {

                FontUtils.ForceCyrillicFont(__instance);

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

        public static void Postfix(TMPro.TMP_Text __instance)

        {

            if (TranslationEngine.Initialized && __instance != null)

            {

                FontUtils.ForceCyrillicFont(__instance);

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

        public static void Postfix(TMPro.TMP_Text __instance)

        {

            if (TranslationEngine.Initialized && __instance != null)

            {

                FontUtils.ForceCyrillicFont(__instance);

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

        public static void Postfix(TMPro.TMP_Text __instance)

        {

            if (TranslationEngine.Initialized && __instance != null)

            {

                FontUtils.ForceCyrillicFont(__instance);

            }

        }

    }



    [HarmonyPatch(typeof(TMPro.TMP_Text), "SetText", new Type[] { typeof(StringBuilder) })]

    public static class TMPText_SetTextStringBuilder_Patch

    {

        public static void Prefix(StringBuilder sourceText)

        {

            if (TranslationEngine.Initialized && sourceText != null)

            {

                string text = sourceText.ToString();

                string translated = TranslationEngine.Translate(text);

                sourceText.Clear();

                sourceText.Append(translated);

            }

        }

        public static void Postfix(TMPro.TMP_Text __instance)

        {

            if (TranslationEngine.Initialized && __instance != null)

            {

                FontUtils.ForceCyrillicFont(__instance);

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

                FontUtils.ForceCyrillicFont(__instance);

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

                FontUtils.ForceCyrillicFont(__instance);

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

                FontUtils.ForceCyrillicFont(__instance);

            }

        }

    }



    // --- ДИНАМИЧЕСКИЙ ПАТЧ ДЛЯ MODERN UI (UI TOOLKIT / UIELEMENTS) ---

    public static class UIElementsDynamicPatch

    {

        public static void TextElement_Prefix(ref string value)

        {

            if (TranslationEngine.Initialized)

            {

                value = TranslationEngine.Translate(value);

            }

        }



        public static void UIDocument_OnEnable_Postfix(object __instance)

        {

            if (__instance == null || !TranslationEngine.Initialized) return;

            try

            {

                var rootProp = __instance.GetType().GetProperty("rootVisualElement", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                if (rootProp != null)

                {

                    object root = rootProp.GetValue(__instance, null);

                    if (root != null)

                    {

                        TranslateVisualTree(root);

                    }

                }

            }

            catch (Exception ex)

            {

                UnityEngine.Debug.LogError("[RussianLocalization] UIDocument_OnEnable_Postfix error: " + ex.ToString());

            }

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

        [HarmonyPrefix]

        [HarmonyPatch("Write", new Type[] { typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(bool), typeof(bool), typeof(bool) })]

        public static void Write_TilePrefix(ref string RenderString)

        {

            if (TranslationEngine.Initialized)

            {

                string trans = TranslationEngine.Translate(RenderString);

                RenderString = TranslationEngine.Transliterate(trans);

            }

        }



        [HarmonyPrefix]

        [HarmonyPatch("Write", new Type[] { typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(System.Collections.Generic.List<string>), typeof(int) })]

        public static void Write_Prefix(ref string s)

        {

            if (TranslationEngine.Initialized)

            {

                string trans = TranslationEngine.Translate(s);

                s = TranslationEngine.Transliterate(trans);

            }

        }



        [HarmonyPrefix]

        [HarmonyPatch("WriteAt", new Type[] { typeof(int), typeof(int), typeof(string), typeof(bool) })]

        public static void WriteAt_Prefix1(ref string s)

        {

            if (TranslationEngine.Initialized)

            {

                string trans = TranslationEngine.Translate(s);

                s = TranslationEngine.Transliterate(trans);

            }

        }



        [HarmonyPrefix]

        [HarmonyPatch("WriteAt", new Type[] { typeof(XRL.World.Cell), typeof(string), typeof(bool) })]

        public static void WriteAt_Prefix2(ref string s)

        {

            if (TranslationEngine.Initialized)

            {

                string trans = TranslationEngine.Translate(s);

                s = TranslationEngine.Transliterate(trans);

            }

        }



        [HarmonyPrefix]

        [HarmonyPatch("WriteAt", new Type[] { typeof(XRL.World.GameObject), typeof(string), typeof(bool) })]

        public static void WriteAt_Prefix3(ref string s)

        {

            if (TranslationEngine.Initialized)

            {

                string trans = TranslationEngine.Translate(s);

                s = TranslationEngine.Transliterate(trans);

            }

        }

    }



    // --- ПАТЧИ ДЛЯ КЛАССА ОПИСАНИЙ (DESCRIPTION PART PATCHES) ---

    [HarmonyPatch(typeof(XRL.World.Parts.Description))]

    public static class Description_Patches

    {

        [HarmonyPostfix]

        [HarmonyPatch("get_Short")]

        public static void get_Short_Postfix(ref string __result)

        {

            if (TranslationEngine.Initialized && !string.IsNullOrEmpty(__result))

            {

                __result = TranslationEngine.Translate(__result);

            }

        }



        [HarmonyPostfix]

        [HarmonyPatch("get_Long")]

        public static void get_Long_Postfix(ref string __result)

        {

            if (TranslationEngine.Initialized && !string.IsNullOrEmpty(__result))

            {

                __result = TranslationEngine.Translate(__result);
            }
        }
    }

    // --- ПАТЧИ ДЛЯ TRANSLATION OF MEMORY DATABASES ---

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

    [HarmonyPatch(typeof(XRL.MutationFactory))]
    public static class MutationFactory_Patch
    {
    }

    [HarmonyPatch(typeof(XRL.World.Skills.SkillFactory))]
    public static class SkillFactory_Patch
    {
        [HarmonyPostfix]
        [HarmonyPatch("Factory", MethodType.Getter)]
        public static void Factory_Getter_Postfix(XRL.World.Skills.SkillFactory __result)
        {
            if (!TranslationEngine.Initialized || __result == null) return;
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
        [HarmonyPostfix]
        [HarmonyPatch("Classes", MethodType.Getter)]
        public static void Classes_Getter_Postfix(List<XRL.SubtypeEntry> __result)
        {
            TranslateSubtypes(__result);
        }

        [HarmonyPostfix]
        [HarmonyPatch("Subtypes", MethodType.Getter)]
        public static void Subtypes_Getter_Postfix(List<XRL.SubtypeEntry> __result)
        {
            TranslateSubtypes(__result);
        }

        private static void TranslateSubtypes(List<XRL.SubtypeEntry> subtypes)
        {
            if (!TranslationEngine.Initialized || subtypes == null) return;
            try
            {
                foreach (var entry in subtypes)
                {
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
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[RussianLocalization] Subtype translation error: " + ex.ToString());
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

        [HarmonyPostfix]
        [HarmonyPatch("ExpandString", new Type[] { typeof(string), typeof(HistoryKit.HistoricEntitySnapshot), typeof(HistoryKit.History), typeof(Dictionary<string, object>), typeof(System.Random) })]
        public static void ExpandString_Postfix2(ref string __result)
        {
            if (TranslationEngine.Initialized && !string.IsNullOrEmpty(__result))
            {
                __result = TranslationEngine.Translate(__result);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("ExpandQuery", new Type[] { typeof(string), typeof(HistoryKit.HistoricEntitySnapshot), typeof(HistoryKit.History), typeof(Dictionary<string, object>), typeof(Dictionary<string, object>), typeof(System.Random) })]
        public static void ExpandQuery_Postfix(ref string __result)
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
}