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
        public static object FileLock = new object();
        public static bool Initialized = false;

        // Потокобезопасный сборщик непереведенных строк
        private static HashSet<string> loggedStrings = new HashSet<string>();
        private static object LogLock = new object();

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
                                    staticDictionary[normKey] = kvp.Value;
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

                    Initialized = true;
                    UnityEngine.Debug.Log("[RussianLocalization] Initialized successfully. Loaded " + staticDictionary.Count + " phrases and " + wordDictionary.Count + " words.");

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
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low",
                @"Freehold Games\CavesOfQud\Mods\RussianLocalization"
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
                string translatedEng = Translate(engPart);
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
            if (!ContainsEnglish(text)) return text;

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
                translatedCore = TryWordReplacement(normalizedCore);
                if (ContainsEnglish(translatedCore))
                {
                    LogUntranslated(trimmedCore);
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

                        // Записываем в файл untranslated.txt в папке мода
                        string modPath = GetModPath();
                        if (!string.IsNullOrEmpty(modPath))
                        {
                            string logPath = Path.Combine(modPath, "untranslated.txt");
                            File.AppendAllText(logPath, trimmed + Environment.NewLine, Encoding.UTF8);
                        }
                    }
                }
            }
            catch {}
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

            StringBuilder sb = new StringBuilder();
            foreach (char c in text)
            {
                if (c == 'а') sb.Append("a"); else if (c == 'б') sb.Append("b"); else if (c == 'в') sb.Append("v");
                else if (c == 'г') sb.Append("g"); else if (c == 'д') sb.Append("d"); else if (c == 'е') sb.Append("e");
                else if (c == 'ё') sb.Append("yo"); else if (c == 'ж') sb.Append("zh"); else if (c == 'з') sb.Append("z");
                else if (c == 'и') sb.Append("i"); else if (c == 'й') sb.Append("j"); else if (c == 'к') sb.Append("k");
                else if (c == 'л') sb.Append("l"); else if (c == 'м') sb.Append("m"); else if (c == 'н') sb.Append("n");
                else if (c == 'о') sb.Append("o"); else if (c == 'п') sb.Append("p"); else if (c == 'р') sb.Append("r");
                else if (c == 'с') sb.Append("s"); else if (c == 'т') sb.Append("t"); else if (c == 'у') sb.Append("u");
                else if (c == 'ф') sb.Append("f"); else if (c == 'х') sb.Append("kh"); else if (c == 'ц') sb.Append("ts");
                else if (c == 'ч') sb.Append("ch"); else if (c == 'ш') sb.Append("sh"); else if (c == 'щ') sb.Append("shch");
                else if (c == 'ы') sb.Append("y"); else if (c == 'э') sb.Append("e"); else if (c == 'ю') sb.Append("yu");
                else if (c == 'я') sb.Append("ya");
                else if (c == 'А') sb.Append("A"); else if (c == 'Б') sb.Append("B"); else if (c == 'В') sb.Append("V");
                else if (c == 'Г') sb.Append("G"); else if (c == 'Д') sb.Append("D"); else if (c == 'Е') sb.Append("E");
                else if (c == 'Ё') sb.Append("Yo"); else if (c == 'Ж') sb.Append("Zh"); else if (c == 'З') sb.Append("Z");
                else if (c == 'И') sb.Append("I"); else if (c == 'Й') sb.Append("J"); else if (c == 'К') sb.Append("K");
                else if (c == 'Л') sb.Append("L"); else if (c == 'М') sb.Append("M"); else if (c == 'Н') sb.Append("N");
                else if (c == 'О') sb.Append("O"); else if (c == 'П') sb.Append("P"); else if (c == 'Р') sb.Append("R");
                else if (c == 'С') sb.Append("S"); else if (c == 'Т') sb.Append("T"); else if (c == 'У') sb.Append("U");
                else if (c == 'Ф') sb.Append("F"); else if (c == 'Х') sb.Append("Kh"); else if (c == 'Ц') sb.Append("Ts");
                else if (c == 'Ч') sb.Append("Ch"); else if (c == 'Ш') sb.Append("Sh"); else if (c == 'Щ') sb.Append("Shch");
                else if (c == 'Ы') sb.Append("Y"); else if (c == 'Э') sb.Append("E"); else if (c == 'Ю') sb.Append("Yu");
                else if (c == 'Я') sb.Append("Ya");
                else sb.Append(c);
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
                if ((c >= 'а' && c <= 'я') || (c >= 'А' && c <= 'Я') || c == 'ё' || c == 'Ё')
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
}
