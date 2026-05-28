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

        public static ConcurrentDictionary<string, string> staticDictionary = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static ConcurrentDictionary<string, string> wordDictionary = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static ConcurrentDictionary<string, string> translationCache = new ConcurrentDictionary<string, string>();

        public static List<string> sortedKeys = new List<string>();

        public static List<string> sortedWordKeys = new List<string>();

        public static List<KeyValuePair<System.Text.RegularExpressions.Regex, string>> patternDictionary = new List<KeyValuePair<System.Text.RegularExpressions.Regex, string>>();

        public static ConcurrentDictionary<string, string> normalizedKeyDictionary = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static object FileLock = new object();

        public static bool Initialized = false;



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



        public static string TryTranslateModernUI(string text, out bool success)

        {

            success = false;

            if (string.IsNullOrEmpty(text)) return text;



            // 1. Проверяем подсветку первой буквы: <color=yellow>e</color><color=cyan>quip (auto)</color>

            var inlineMatch = InlineKeyRegex.Match(text);

            if (inlineMatch.Success)

            {

                string key = inlineMatch.Groups[1].Value;

                string rest = inlineMatch.Groups[2].Value;

                string fullAction = key + rest;

                

                string translated = TranslateText(fullAction);

                if (translated != fullAction)

                {

                    success = true;

                    // Если в словаре перевод уже содержит скобки (например, "[e] снарядить (авто)"), возвращаем его как есть

                    if (translated.Contains("[") && translated.Contains("]"))

                    {

                        return translated;

                    }

                    return string.Format("<color=#CFC041FF>[{0}]</color><color=#40A4B9FF> </color><color=#B1C9C3FF>{1}</color>", key.ToUpper(), translated);

                }

            }



            // 2. Проверяем скобки с цветом в начале: <color=#CFC041FF>[E]</color><color=#40A4B9FF> </color><color=#B1C9C3FF>Equip (manual)</color>

            var colorBracketMatch = ColorBracketKeyRegex.Match(text);

            if (colorBracketMatch.Success)

            {

                string key = colorBracketMatch.Groups[1].Value;

                string action = colorBracketMatch.Groups[2].Value.Trim();

                

                string translated = TranslateText(action);

                if (translated != action)

                {

                    success = true;

                    // Если в словаре перевод уже содержит скобки, возвращаем его как есть

                    if (translated.Contains("[") && translated.Contains("]"))

                    {

                        return translated;

                    }

                    return string.Format("<color=#CFC041FF>[{0}]</color><color=#40A4B9FF> </color><color=#B1C9C3FF>{1}</color>", key, translated);

                }

            }



            // 3. Проверяем обычные скобки в начале: [E] Equip (manual)

            var bracketMatch = ModernUIMenuRegex.Match(text);

            if (bracketMatch.Success)

            {

                string key = bracketMatch.Groups[1].Value;

                string action = bracketMatch.Groups[2].Value.Trim();

                

                string translated = TranslateText(action);

                if (translated != action)

                {

                    success = true;

                    // Если в словаре перевод уже содержит скобки, возвращаем его как есть

                    if (translated.Contains("[") && translated.Contains("]"))

                    {

                        return translated;

                    }

                    return string.Format("[{0}] {1}", key, translated);

                }

            }



            // 4. Дополнительный резервный вариант для строк с тегами цвета

            if (text.Contains("<color=") && (text.Contains("[") || text.Contains("]")))

            {

                string cleanText = TagRegex.Replace(text, "").Trim();

                var match = ModernUIMenuRegex.Match(cleanText);

                if (match.Success)

                {

                    string key = match.Groups[1].Value;

                    string action = match.Groups[2].Value.Trim();



                    if (!string.IsNullOrEmpty(action))

                    {

                        string translatedAction = TranslateText(action);

                        if (translatedAction != action)

                        {

                            success = true;

                            // Если в словаре перевод уже содержит скобки, возвращаем его как есть

                            if (translatedAction.Contains("[") && translatedAction.Contains("]"))

                            {

                                return translatedAction;

                            }

                            return string.Format("<color=#CFC041FF>[{0}]</color><color=#40A4B9FF> </color><color=#B1C9C3FF>{1}</color>", key, translatedAction);

                        }

                    }

                }

            }



            return text;

        }



        static TranslationEngine()

        {

            Initialize();

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

                                        normalizedKeyDictionary[sn] = normKey;

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

                        var patternDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(patternJsonText);

                        if (patternDict != null)

                        {

                            patternDictionary.Clear();

                            foreach (var kvp in patternDict)

                            {

                                if (string.IsNullOrEmpty(kvp.Key)) continue;

                                try

                                {

                                    var regex = new System.Text.RegularExpressions.Regex(kvp.Key, System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                                    patternDictionary.Add(new KeyValuePair<System.Text.RegularExpressions.Regex, string>(regex, kvp.Value));

                                }

                                catch (Exception regexEx)

                                {

                                    UnityEngine.Debug.LogError("[RussianLocalization] Failed to compile pattern regex '" + kvp.Key + "': " + regexEx.Message);

                                }

                            }

                        }

                    }



                    Initialized = true;

                    UnityEngine.Debug.Log("[RussianLocalization] Initialized successfully. Loaded " + staticDictionary.Count + " phrases, " + wordDictionary.Count + " words, and " + patternDictionary.Count + " patterns.");



                    // Динамический патч для Modern UI (UI Toolkit / UIElements)

                    PatchUIElements();

                }

                catch (Exception ex)

                {

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



            prefix = text.Substring(0, firstEnglishIdx);

            englishPart = text.Substring(firstEnglishIdx);

        }



        public static string Translate(string text)

        {

            if (string.IsNullOrEmpty(text)) return text;



            string result = TranslateInternal(text);



            if (result != null)

            {

                if (result.Contains("]]"))

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
                result = System.Text.RegularExpressions.Regex.Replace(result, @"\b([a-zA-Z])\b(\s*)(</color>)?(\s*)(<color=[^>]+>)?(\s*)([а-яА-ЯёЁ])", "$3$4$5$6$7");

                // Удаление дублирующих букв в КОНЦЕ слова (например, атаковать k -> атаковать)
                result = System.Text.RegularExpressions.Regex.Replace(result, @"([а-яА-ЯёЁ])(\s*)(</color>)?(\s*)(<color=[^>]+>)?(\s*)\b([a-zA-Z])\b(\s*)(</color>)?", "$1$2$3$4$5$6$8$9");



                // Восстановление оригинальных латинских тегов цвета Caves of Qud при случайной транслитерации символа цвета

                result = result.Replace("&у", "&y").Replace("&У", "&Y")

                               .Replace("&р", "&r").Replace("&Р", "&R")

                               .Replace("&с", "&c").Replace("&С", "&C")

                               .Replace("&в", "&w").Replace("&В", "&W")

                               .Replace("&м", "&m").Replace("&М", "&M")

                               .Replace("&г", "&g").Replace("&Г", "&G")

                               .Replace("&б", "&b").Replace("&Б", "&B")

                               .Replace("&д", "&d").Replace("&Д", "&D");



                if (result.Contains("=now.dayOfYear="))

                {

                    result = result.Replace("=now.dayOfYear=", DateTime.Now.DayOfYear.ToString());

                }



                if (result.Contains("=now.year="))

                {

                    result = result.Replace("=now.year=", DateTime.Now.Year.ToString());

                }

            }



            LogAllGameplayText(result);



            return result;

        }



                private static readonly System.Text.RegularExpressions.Regex FactionRegex = new System.Text.RegularExpressions.Regex(@"^<color=(?<c1>#[0-9A-Fa-f]+)>(?<faction>.*?)</color><color=(?<c2>#[0-9A-Fa-f]+)>(?<relation>.*?)</color>$", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        public static string TranslateRelationText(string relation)
        {
            if (string.IsNullOrEmpty(relation)) return relation;
            string clean = relation.Trim();
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
                { "are interested in hearing gossip that's about them", "заинтересованы в прослушивании слухов, которые их касаются" }
            };

            string trans;
            if (dict.TryGetValue(clean, out trans))
            {
                return trans + (hasDot ? "." : "");
            }

            // 2. Динамический разбор сложных интересов с перечислением тем
            var matchPref = System.Text.RegularExpressions.Regex.Match(clean, 
                @"^(?<subj>are|is)\s+interested\s+in\s+(?<verb>trading\s+secrets\s+about|sharing\s+secrets\s+about|learning\s+about|sharing\s+secrets\s+of)\s+(?<rest>.*)$", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (matchPref.Success)
            {
                string subj = matchPref.Groups["subj"].Value.ToLower();
                string verb = matchPref.Groups["verb"].Value.ToLower();
                string rest = matchPref.Groups["rest"].Value.Trim();

                bool hasGossip = false;
                string gossipSuffix = "";

                // Проверяем окончание со слухами вариант 1: . They're also interested in hearing gossip that's about them
                var gossipPattern1 = new System.Text.RegularExpressions.Regex(@"\.\s*They\'re\s+also\s+interested\s+in\s+(?:hearing\s+)?gossip\s+that\'s\s+about\s+them$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (gossipPattern1.IsMatch(rest))
                {
                    hasGossip = true;
                    rest = gossipPattern1.Replace(rest, "").Trim();
                    gossipSuffix = ". Им также интересно слушать слухи, которые их касаются";
                }
                else
                {
                    // Вариант 2: , and gossip that's about them
                    var gossipPattern2 = new System.Text.RegularExpressions.Regex(@",\s*and\s+(?:hearing\s+)?gossip\s+that\'s\s+about\s+them$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (gossipPattern2.IsMatch(rest))
                    {
                        hasGossip = true;
                        rest = gossipPattern2.Replace(rest, "").Trim();
                        gossipSuffix = " и слухах, которые их касаются";
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
                    else
                    {
                        ruVerb = "интересуется получением сведений о ";
                    }
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
                    string transTheme = TranslateText(t);
                    translatedThemes.Add(transTheme);
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

            return relation + (hasDot ? "." : "");
        }

        public static string TryTranslateFactionReputation(string text, out bool success)
        {
            success = false;
            if (string.IsNullOrEmpty(text)) return text;

            // 1. Очищаем стыковочные теги цвета на переносах строк
            string normalized = System.Text.RegularExpressions.Regex.Replace(text, @"[\r\n]+</color>\s*<color=#[0-9A-Fa-f]+>", " ");

            // 2. Заменяем оставшиеся \n и \r на пробелы
            normalized = normalized.Replace("\r", " ").Replace("\n", " ");

            // 3. Сжимаем пробелы
            while (normalized.Contains("  "))
            {
                normalized = normalized.Replace("  ", " ");
            }
            normalized = normalized.Trim();

            var match = FactionRegex.Match(normalized);
            if (match.Success)
            {
                string c1 = match.Groups["c1"].Value;
                string faction = match.Groups["faction"].Value.Trim();
                string c2 = match.Groups["c2"].Value;
                string relation = match.Groups["relation"].Value.Trim();

                string translatedFaction = TranslateText(faction);
                string translatedRelation = TranslateRelationText(relation);

                if (translatedRelation != relation || translatedFaction != faction)
                {
                    success = true;
                    return string.Format("<color={0}>{1}</color><color={2}> {3}</color>", c1, translatedFaction, c2, translatedRelation);
                }
            }

            // Резервный пошаблонный поиск отношений
            string cleanText = text.Trim();
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
                    return translatedFaction + t.Ru + (hasDot ? "." : "");
                }

                string cleanEng = t.Eng.Replace("&lt;", "<").Replace("&gt;", ">");
                string cleanRu = t.Ru.Replace("&lt;", "<").Replace("&gt;", ">");
                if (cleanText.EndsWith(cleanEng, StringComparison.OrdinalIgnoreCase))
                {
                    string factionPart = cleanText.Substring(0, cleanText.Length - cleanEng.Length).Trim();
                    string translatedFaction = TranslateText(factionPart);
                    success = true;
                    return translatedFaction + cleanRu + (hasDot ? "." : "");
                }
            }

            return text;
        }



        public static string TryTranslatePattern(string text, out bool success)

        {

            success = false;

            if (string.IsNullOrEmpty(text)) return text;



            for (int i = 0; i < patternDictionary.Count; i++)

            {

                var rule = patternDictionary[i];

                var regex = rule.Key;

                var match = regex.Match(text);

                if (match.Success)

                {

                    string template = rule.Value;

                    string result = template;

                    

                    string[] groupNames = regex.GetGroupNames();

                    for (int g = 0; g < groupNames.Length; g++)

                    {

                        string groupName = groupNames[g];

                        if (groupName == "0") continue;

                        

                        var group = match.Groups[groupName];

                        if (group.Success)

                        {

                            string groupValue = group.Value;

                            string translatedGroup = TranslateText(groupValue);

                            result = result.Replace("{" + groupName + "}", translatedGroup);

                        }

                    }

                    

                    success = true;

                    return result;

                }

            }

            return text;

        }



        private static string TranslateInternal(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // Очищаем \r для предотвращения поломки ключей при переносах строк в Windows
            text = text.Replace("\r", "");

            // 1. Рекурсивный разбор цветовых блоков (color blocks)
            var colorBlockPattern = new System.Text.RegularExpressions.Regex(
                @"(?<pref><color=[^>]+>)(?<content>.*?)(?<suff></color>)", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

            var colorMatches = colorBlockPattern.Matches(text);
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

            // 2. Построчный перевод при наличии \n (для сохранения форматирования и каст)
            if (text.Contains("\n"))
            {
                string[] lines = text.Split('\n');
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



            bool success;

            string patternTranslated = TryTranslatePattern(text, out success);

            if (success)

            {

                return patternTranslated;

            }



            string factionTranslated = TryTranslateFactionReputation(text, out success);

            if (success)

            {

                return factionTranslated;

            }



            string modernUITranslated = TryTranslateModernUI(text, out success);

            if (success)

            {

                return modernUITranslated;

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
                string sn = SuperNormalize(trimmed);
                
                // ЗАЩИТА: Если оригинальный trimmed текст не в скобках, 
                // но его супер-нормализованная форма - это одиночная латинская буква или известное название клавиши,
                // то мы запрещаем перевод через normalizedKeyDictionary, так как это ASCII-арт, тайл или граффити.
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
                bool hasBrackets = trimmed.StartsWith("[") && trimmed.EndsWith("]");
                
                if (!isKeyName || hasBrackets)
                {
                    string originalKey;
                    if (normalizedKeyDictionary.TryGetValue(sn, out originalKey))
                    {
                        if (staticDictionary.TryGetValue(originalKey, out exactMatch))
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

                        translationCache[text] = strippedExact;

                        return strippedExact;

                    }



                    string strippedSn = SuperNormalize(strippedText);

                    string strippedOrigKey;

                    if (normalizedKeyDictionary.TryGetValue(strippedSn, out strippedOrigKey))

                    {

                        if (staticDictionary.TryGetValue(strippedOrigKey, out strippedExact))

                        {

                            translationCache[text] = strippedExact;

                            return strippedExact;

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

            ' ', '\t', '\r', '\n', '.', ',', '!', '?', ':', ';', '~', '-', '_', '"', '\'', '(', ')', '[', ']', '{', '}', '\u00A0', '\u2007', '\u200B', '\u202F'

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



        public static string TranslateText(string text)

        {

            if (string.IsNullOrEmpty(text)) return text;



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
                            translatedCore = exactMatch;
                        }
                    }
                }
            }



            if (string.IsNullOrEmpty(translatedCore))

            {

                translatedCore = TryWordReplacement(normalizedCore);

                if (translatedCore != normalizedCore)

                {

                    LogWordReplacement(normalizedCore, translatedCore);

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



            string result = prefix + translatedCore + suffix;

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

                                char origChar = result[index];

                                if (char.IsLower(origChar))

                                {

                                    bool skipLowering = char.IsUpper(translation[0]) && 

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

                else if (!char.IsPunctuation(c))

                {

                    sb.Append(char.ToLowerInvariant(c));

                    lastWasSpace = false;

                }

            }

            return sb.ToString().Trim();

        }



        private static void AppendToLogFile(string filename, string content)

        {

            try

            {

                string modPath = GetModPath();

                if (!string.IsNullOrEmpty(modPath))

                {

                    string logPath = Path.Combine(modPath, filename);

                    File.AppendAllText(logPath, content, Encoding.UTF8);

                }

            }

            catch {}



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

            catch {}

        }



        private static void LogUntranslated(string text)

        {

            try

            {

                if (string.IsNullOrEmpty(text)) return;

                string trimmed = text.Trim();

                if (trimmed.Length < 3) return; // Пропускаем короткие фразы

                

                // Пропускаем технические теги Unity/TMPro

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



        private static void LogAllGameplayText(string text)

        {

            try

            {

                if (IsJunkText(text)) return;

                string trimmed = text.Trim();



                lock (AllTextLogLock)

                {

                    if (!loggedAllTexts.Contains(trimmed))

                    {

                        loggedAllTexts.Add(trimmed);

                        AppendToLogFile("all_gameplay_texts.txt", "[TEXT]: " + trimmed + Environment.NewLine);

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



            // Если строка состоит только из цифр, знаков препинания, скобок и пробелов - это мусор

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



            // Проверяем, не является ли строка просто тегом или кодом цвета (например, "<color=#ff0000>")

            if (trimmed.StartsWith("<") && trimmed.EndsWith(">") && !trimmed.Contains("</color>")) return true;



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

}

`n    }`n}
