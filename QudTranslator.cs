using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.RegularExpressions;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using XRL;
using ConsoleLib.Console;

namespace QudTranslator
{
    [XRL.HasModSensitiveStaticCache]
    public class HookLoader
    {
        static HookLoader()
        {
            try {
                var harmony = new Harmony("com.voidsector.qudtranslator");
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                TranslationEngine.Initialize();
                Debug.Log("[QudTranslator] v21.1 (Deep Hybrid) ONLINE.");
            } catch (Exception ex) { Debug.LogError(ex.ToString()); }
        }
        public static void Init() {}
    }

    public static class TranslationEngine
    {
        public static ConcurrentDictionary<string, string> dict = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public static ConcurrentDictionary<string, string> wordDict = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public static List<string> sortedKeys = new List<string>();
        public static bool Initialized = false;
        private static readonly string ModDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low", @"Freehold Games\CavesOfQud\Mods\RussianLocalization");

        private static readonly Regex SafeRegex = new Regex(@"(\{\{.*?\}\}|%.*?%|=.*?=|&[a-zA-Z]|\^[a-zA-Z]|<.*?>)", RegexOptions.Compiled);

        public static void Initialize()
        {
            try {
                string path = Path.Combine(ModDir, "dictionary.json");
                string wpath = Path.Combine(ModDir, "word_dictionary.json");
                if (File.Exists(path)) {
                    var data = SimpleJsonParser.Parse(File.ReadAllText(path, Encoding.UTF8));
                    foreach (var kvp in data) if (!string.IsNullOrEmpty(kvp.Key)) dict[kvp.Key.Trim()] = kvp.Value;
                }
                if (File.Exists(wpath)) {
                    var data = SimpleJsonParser.Parse(File.ReadAllText(wpath, Encoding.UTF8));
                    foreach (var kvp in data) if (!string.IsNullOrEmpty(kvp.Key)) wordDict[kvp.Key.Trim()] = kvp.Value;
                }
                sortedKeys = dict.Keys.ToList();
                sortedKeys.Sort(delegate(string a, string b) { return b.Length.CompareTo(a.Length); });
                Initialized = true;
            } catch { }
        }

        public static string Translate(string text, bool useTranslit)
        {
            if (string.IsNullOrEmpty(text) || !Initialized) return text;
            
            string trimmed = text.Trim();
            string match;
            if (dict.TryGetValue(trimmed, out match)) return text.Replace(trimmed, match);

            var tags = new List<string>();
            string masked = SafeRegex.Replace(text, new MatchEvaluator(delegate(Match m) {
                string placeholder = "___V" + tags.Count + "___";
                tags.Add(m.Value);
                return placeholder;
            }));

            string result = masked;
            foreach (var key in sortedKeys)
            {
                if (key.Length < 10) continue;
                if (result.IndexOf(key, StringComparison.OrdinalIgnoreCase) != -1)
                {
                    result = Regex.Replace(result, @"\b" + Regex.Escape(key) + @"\b", new MatchEvaluator(delegate(Match m) {
                        string val = dict[key];
                        if (IsAllUpper(m.Value)) return val.ToUpper();
                        if (char.IsUpper(m.Value[0]) && val.Length > 0) return char.ToUpper(val[0]) + val.Substring(1);
                        return val;
                    }), RegexOptions.IgnoreCase);
                }
            }

            string[] parts = Regex.Split(result, @"([^a-zA-Z]+)");
            StringBuilder sb = new StringBuilder();
            foreach (string part in parts)
            {
                if (string.IsNullOrEmpty(part)) continue;
                string trans;
                if (char.IsLetter(part[0]) && wordDict.TryGetValue(part, out trans))
                {
                    string final = useTranslit ? Transliterate(trans) : trans;
                    if (IsAllUpper(part)) sb.Append(final.ToUpper());
                    else if (char.IsUpper(part[0]) && final.Length > 0)
                        sb.Append(char.ToUpper(final[0]) + final.Substring(1));
                    else
                        sb.Append(final);
                }
                else sb.Append(part);
            }

            string finalResult = sb.ToString();
            for (int i = 0; i < tags.Count; i++) finalResult = finalResult.Replace("___V" + i + "___", tags[i]);

            return useTranslit ? Transliterate(finalResult) : finalResult;
        }

        private static bool IsAllUpper(string s)
        {
            for (int i = 0; i < s.Length; i++) if (char.IsLetter(s[i]) && !char.IsUpper(s[i])) return false;
            return s.Length > 1;
        }

        public static string Transliterate(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            bool hasRus = false;
            for(int i=0; i<text.Length; i++) if(text[i] > 127) { hasRus = true; break; }
            if (!hasRus) return text;

            StringBuilder sb = new StringBuilder();
            foreach (char c in text) {
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
    }

    [HarmonyPatch(typeof(TMP_Text), "text", MethodType.Setter)]
    public static class TMPText_Patch { 
        static void Prefix(ref string value) { value = TranslationEngine.Translate(value, false); }
    }

    [HarmonyPatch(typeof(ScreenBuffer))]
    public static class ScreenBuffer_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch("Write", new Type[] { typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(bool), typeof(bool), typeof(bool) })]
        public static void Prefix1(ref string Text) { Text = TranslationEngine.Translate(Text, true); }

        [HarmonyPrefix]
        [HarmonyPatch("Write", new Type[] { typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(List<string>), typeof(int) })]
        public static void Prefix2(ref string Text) { Text = TranslationEngine.Translate(Text, true); }

        [HarmonyPrefix]
        [HarmonyPatch("WriteAt", new Type[] { typeof(int), typeof(int), typeof(string), typeof(bool) })]
        public static void Prefix3(ref string Text) { Text = TranslationEngine.Translate(Text, true); }
    }

    public static class SimpleJsonParser
    {
        public static Dictionary<string, string> Parse(string json)
        {
            var dict = new Dictionary<string, string>();
            try {
                int state = 0; var k = new StringBuilder(); var v = new StringBuilder(); bool q = false; bool esc = false;
                for (int i = 0; i < json.Length; i++) {
                    char c = json[i];
                    if (esc) { if (state == 0) k.Append(c); else v.Append(c); esc = false; continue; }
                    if (c == '\\') { esc = true; continue; }
                    if (c == '"') { if (q) { if (state == 1) { dict[k.ToString().Trim()] = v.ToString(); state = 0; k.Clear(); v.Clear(); } else state = 1; q = false; } else q = true; continue; }
                    if (q) { if (state == 0) k.Append(c); else v.Append(c); }
                }
            } catch { }
            return dict;
        }
    }
}
