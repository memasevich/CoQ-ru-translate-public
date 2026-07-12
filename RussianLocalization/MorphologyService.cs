using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace RussianLocalization
{
    public enum MorphCase { Nom, Gen, Dat, Acc, Ins, Prep }
    public enum MorphGender { Masc, Fem, Neut }
    public enum MorphNumber { Singular, Plural }

    public class NounForms
    {
        public string Gender { get; set; } = "masc";
        public string[] Singular { get; set; } = new string[6];
        public string[] Plural { get; set; } = new string[6];

        public string Get(MorphCase c, MorphNumber n)
        {
            var forms = n == MorphNumber.Singular ? Singular : Plural;
            int idx = (int)c;
            if (forms != null && idx < forms.Length && !string.IsNullOrEmpty(forms[idx]))
                return forms[idx];
            return null;
        }
    }

    public static class MorphologyService
    {
        private static readonly ConcurrentDictionary<string, string> morphCache =
            new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static Dictionary<string, NounForms> nounDictionary;
        private static bool Initialized = false;
        private static readonly object initLock = new object();

        private const int MaxCacheSize = 50000;
        private static int cacheCallCounter;

        public static void Initialize(string modPath)
        {
            lock (initLock)
            {
                if (Initialized) return;

                nounDictionary = new Dictionary<string, NounForms>(StringComparer.OrdinalIgnoreCase);

                string morphPath = Path.Combine(modPath, "morphology_dictionary.json");
                if (File.Exists(morphPath))
                {
                    try
                    {
                        string json = File.ReadAllText(morphPath, Encoding.UTF8);
                        var loaded = JsonConvert.DeserializeObject<Dictionary<string, NounForms>>(json);
                        if (loaded != null)
                        {
                            foreach (var kvp in loaded)
                            {
                                if (!string.IsNullOrEmpty(kvp.Key) && kvp.Value != null)
                                    nounDictionary[kvp.Key] = kvp.Value;
                            }
                        }
                        try { TranslationEngine.LogInfo("[RussianLocalization] MorphologyService loaded " + nounDictionary.Count + " noun forms."); } catch { }
                    }
                    catch (Exception ex)
                    {
                        try { TranslationEngine.LogError("[RussianLocalization] MorphologyService failed to load: " + ex.Message); } catch { }
                    }
                }
                else
                {
                    try { TranslationEngine.LogInfo("[RussianLocalization] MorphologyService: no morphology_dictionary.json found, using rules only."); } catch { }
                }

                Initialized = true;
            }
        }

        private static void MaybeResetCache()
        {
            int n = System.Threading.Interlocked.Increment(ref cacheCallCounter);
            if ((n & 2047) != 0) return;
            if (morphCache.Count < MaxCacheSize) return;
            morphCache.Clear();
        }

        private static readonly Regex TagPrefixRegex = new Regex(@"^(?:<color=[^>]+>|\{\{[a-zA-Z0-9_-]+\||&[a-zA-Z]|\{\{\[?[a-zA-Z]\]?\|)+", RegexOptions.Compiled);
        private static readonly Regex TagSuffixRegex = new Regex(@"(?:</color>|\}\})+$", RegexOptions.Compiled);

        private static bool IsAdjective(string word)
        {
            if (string.IsNullOrEmpty(word)) return false;
            string w = word.ToLower();
            return w.EndsWith("ый") || w.EndsWith("ий") || w.EndsWith("ой") ||
                   w.EndsWith("ая") || w.EndsWith("яя") ||
                   w.EndsWith("ое") || w.EndsWith("ее") ||
                   w.EndsWith("ые") || w.EndsWith("ие") ||
                   w.EndsWith("ья") || w.EndsWith("ье") || w.EndsWith("ьи");
        }

        private static string DeclineSingleWord(string word, MorphGender gender, MorphCase targetCase, MorphNumber number)
        {
            if (string.IsNullOrEmpty(word)) return word;

            var preMatch = TagPrefixRegex.Match(word);
            string prefix = preMatch.Success ? preMatch.Value : "";
            var sufMatch = TagSuffixRegex.Match(word);
            string suffix = sufMatch.Success ? sufMatch.Value : "";

            string clean = word;
            if (prefix.Length > 0) clean = clean.Substring(prefix.Length);
            if (suffix.Length > 0) clean = clean.Substring(0, clean.Length - suffix.Length);

            if (string.IsNullOrEmpty(clean)) return word;

            string declined;
            if (IsAdjective(clean))
            {
                declined = DeclineAdjective(clean, gender, targetCase, number);
            }
            else
            {
                if (nounDictionary != null && nounDictionary.TryGetValue(clean, out NounForms forms))
                {
                    string fromDict = forms.Get(targetCase, number);
                    if (!string.IsNullOrEmpty(fromDict))
                        declined = fromDict;
                    else
                        declined = ApplyNounRules(clean, DetectGender(forms.Gender), targetCase, number);
                }
                else
                {
                    MorphGender wGender = DetectGenderByEnding(clean);
                    declined = ApplyNounRules(clean, wGender, targetCase, number);
                }
            }

            return prefix + declined + suffix;
        }

        public static string Decline(string nominative, MorphCase targetCase, MorphNumber number = MorphNumber.Singular)
        {
            if (string.IsNullOrEmpty(nominative)) return nominative;
            if (targetCase == MorphCase.Nom && number == MorphNumber.Singular) return nominative;

            MaybeResetCache();

            string cacheKey = nominative + "|" + (int)targetCase + "|" + (int)number;
            if (morphCache.TryGetValue(cacheKey, out string cached))
                return cached;

            string result;

            if (nominative.Contains(" ") || nominative.Contains("-"))
            {
                string[] rawParts = Regex.Split(nominative, @"(\s+|-)", RegexOptions.IgnoreCase);
                string lastWord = "";
                for (int i = rawParts.Length - 1; i >= 0; i--)
                {
                    string clean = TagPrefixRegex.Replace(rawParts[i], "");
                    clean = TagSuffixRegex.Replace(clean, "").Trim();
                    if (!string.IsNullOrEmpty(clean) && clean != "-" && !IsAdjective(clean))
                    {
                        lastWord = clean;
                        break;
                    }
                }
                if (string.IsNullOrEmpty(lastWord))
                {
                    for (int i = rawParts.Length - 1; i >= 0; i--)
                    {
                        string clean = TagPrefixRegex.Replace(rawParts[i], "");
                        clean = TagSuffixRegex.Replace(clean, "").Trim();
                        if (!string.IsNullOrEmpty(clean) && clean != "-")
                        {
                            lastWord = clean;
                            break;
                        }
                    }
                }

                MorphGender phraseGender = DetectGenderForWord(lastWord);

                StringBuilder sb = new StringBuilder();
                foreach (string part in rawParts)
                {
                    if (string.IsNullOrWhiteSpace(part) || part == "-")
                    {
                        sb.Append(part);
                    }
                    else
                    {
                        sb.Append(DeclineSingleWord(part, phraseGender, targetCase, number));
                    }
                }
                result = sb.ToString();
            }
            else
            {
                result = DeclineSingleWord(nominative, DetectGenderForWord(nominative), targetCase, number);
            }

            morphCache[cacheKey] = result;
            return result;
        }

        public static string DeclineAdjective(string adjective, MorphGender gender, MorphCase targetCase, MorphNumber number)
        {
            if (string.IsNullOrEmpty(adjective)) return adjective;
            if (targetCase == MorphCase.Nom && number == MorphNumber.Singular) return adjective;

            string cacheKey = "adj|" + adjective + "|" + (int)gender + "|" + (int)targetCase + "|" + (int)number;
            if (morphCache.TryGetValue(cacheKey, out string cached))
                return cached;

            string result = ApplyAdjectiveRules(adjective, gender, targetCase, number);
            morphCache[cacheKey] = result;
            return result;
        }

        public static MorphGender DetectGender(string genderStr)
        {
            if (string.IsNullOrEmpty(genderStr)) return MorphGender.Masc;
            switch (genderStr.ToLower().Trim())
            {
                case "fem":
                case "f":
                case "feminine": return MorphGender.Fem;
                case "neut":
                case "n":
                case "neuter": return MorphGender.Neut;
                default: return MorphGender.Masc;
            }
        }

        public static MorphGender DetectGenderByEnding(string word)
        {
            if (string.IsNullOrEmpty(word)) return MorphGender.Masc;
            char last = word[word.Length - 1];

            if (last == 'а' || last == 'я') return MorphGender.Fem;
            if (last == 'о' || last == 'е') return MorphGender.Neut;
            if (last == 'ь')
            {
                // -ь может быть masc или fem, проверяем предпоследнюю
                if (word.Length >= 2)
                {
                    char prev = word[word.Length - 2];
                    // Мягкий знак после шипящих — чаще fem
                    if (prev == 'ш' || prev == 'ч' || prev == 'щ' || prev == 'ж')
                        return MorphGender.Fem;
                }
                return MorphGender.Masc;
            }
            return MorphGender.Masc;
        }

        private static string ApplyNounRules(string word, MorphGender gender, MorphCase targetCase, MorphNumber number)
        {
            if (string.IsNullOrEmpty(word)) return word;

            char last = word[word.Length - 1];
            string stem;

            // Определяем основу
            if (last == 'а' || last == 'я' || last == 'о' || last == 'е' || last == 'ь' || last == 'ы' || last == 'и')
                stem = word.Substring(0, word.Length - 1);
            else
                stem = word;

            if (number == MorphNumber.Plural)
                return ApplyPluralRules(word, stem, last, gender, targetCase);

            // Singular
            switch (gender)
            {
                case MorphGender.Fem:
                    return ApplyFemSingular(word, stem, last, targetCase);
                case MorphGender.Neut:
                    return ApplyNeutSingular(word, stem, last, targetCase);
                default:
                    return ApplyMascSingular(word, stem, last, targetCase);
            }
        }

        private static string ApplyMascSingular(string word, string stem, char last, MorphCase c)
        {
            // Согласная или -ь (masc)
            bool soft = last == 'ь';
            bool sibilant = last == 'ж' || last == 'ш' || last == 'ч' || last == 'щ';

            switch (c)
            {
                case MorphCase.Nom: return word;
                case MorphCase.Gen:
                    if (soft || sibilant) return word + "а";
                    return word + "а";
                case MorphCase.Dat:
                    return word + "у";
                case MorphCase.Acc:
                    return word; // inanimate = nom
                case MorphCase.Ins:
                    if (soft) return word + "ем";
                    if (sibilant) return word + "ем";
                    return word + "ом";
                case MorphCase.Prep:
                    if (soft || sibilant) return word + "е";
                    return word + "е";
                default: return word;
            }
        }

        private static string ApplyFemSingular(string word, string stem, char last, MorphCase c)
        {
            bool soft = last == 'ь';
            bool sibilant = stem.Length > 0 && (stem[stem.Length - 1] == 'ж' || stem[stem.Length - 1] == 'ш' || stem[stem.Length - 1] == 'ч' || stem[stem.Length - 1] == 'щ');

            switch (c)
            {
                case MorphCase.Nom: return word;
                case MorphCase.Gen:
                    if (last == 'а')
                    {
                        // После шипяных — ударение важно, но упрощаем
                        return stem + "ы";
                    }
                    if (last == 'я') return stem + "и";
                    return word;
                case MorphCase.Dat:
                    if (last == 'а') return stem + "е";
                    if (last == 'я') return stem + "е";
                    return word;
                case MorphCase.Acc:
                    if (last == 'а') return stem + "у";
                    if (last == 'я') return stem + "ю";
                    return word;
                case MorphCase.Ins:
                    if (last == 'а')
                    {
                        if (sibilant) return stem + "ей";
                        return stem + "ой";
                    }
                    if (last == 'я') return stem + "ей";
                    return word;
                case MorphCase.Prep:
                    if (last == 'а') return stem + "е";
                    if (last == 'я') return stem + "е";
                    return word;
                default: return word;
            }
        }

        private static string ApplyNeutSingular(string word, string stem, char last, MorphCase c)
        {
            switch (c)
            {
                case MorphCase.Nom:
                case MorphCase.Acc:
                    return word;
                case MorphCase.Gen:
                    if (last == 'о') return stem + "а";
                    if (last == 'е') return stem + "я";
                    return word;
                case MorphCase.Dat:
                    if (last == 'о') return stem + "у";
                    if (last == 'е') return stem + "ю";
                    return word;
                case MorphCase.Ins:
                    if (last == 'о') return stem + "ом";
                    if (last == 'е') return stem + "ем";
                    return word;
                case MorphCase.Prep:
                    if (last == 'о') return stem + "е";
                    if (last == 'е') return stem + "е";
                    return word;
                default: return word;
            }
        }

        private static string ApplyPluralRules(string word, string stem, char last, MorphGender gender, MorphCase c)
        {
            switch (c)
            {
                case MorphCase.Nom:
                case MorphCase.Acc:
                    if (last == 'а') return stem + "ы";
                    if (last == 'я') return stem + "и";
                    if (last == 'о' || last == 'е') return stem + "а";
                    return word + "ы";
                case MorphCase.Gen:
                    // Нулевое окончание для многих, упрощаем
                    if (last == 'а') return stem;
                    if (last == 'я') return stem;
                    if (last == 'о' || last == 'е') return stem + "ов";
                    return word;
                case MorphCase.Dat:
                    if (last == 'а') return stem + "ам";
                    if (last == 'я') return stem + "ям";
                    if (last == 'о' || last == 'е') return stem + "ам";
                    return word + "ам";
                case MorphCase.Ins:
                    if (last == 'а') return stem + "ами";
                    if (last == 'я') return stem + "ями";
                    if (last == 'о' || last == 'е') return stem + "ами";
                    return word + "ами";
                case MorphCase.Prep:
                    if (last == 'а') return stem + "ах";
                    if (last == 'я') return stem + "ях";
                    if (last == 'о' || last == 'е') return stem + "ах";
                    return word + "ах";
                default: return word;
            }
        }

        private static string ApplyAdjectiveRules(string adj, MorphGender gender, MorphCase targetCase, MorphNumber number)
        {
            if (string.IsNullOrEmpty(adj)) return adj;

            char last = adj[adj.Length - 1];
            string stem;

            // Определяем основу прилагательного
            if (last == 'й' || adj.EndsWith("ый") || adj.EndsWith("ий"))
            {
                if (adj.EndsWith("ый") || adj.EndsWith("ий"))
                    stem = adj.Substring(0, adj.Length - 2);
                else
                    stem = adj.Substring(0, adj.Length - 1);
            }
            else if (adj.EndsWith("ая"))
                stem = adj.Substring(0, adj.Length - 2);
            else if (adj.EndsWith("ое"))
                stem = adj.Substring(0, adj.Length - 2);
            else if (adj.EndsWith("ые") || adj.EndsWith("ие"))
                stem = adj.Substring(0, adj.Length - 2);
            else if (adj.EndsWith("ую"))
                stem = adj.Substring(0, adj.Length - 2);
            else if (adj.EndsWith("ой") || adj.EndsWith("ей"))
                stem = adj.Substring(0, adj.Length - 2);
            else if (adj.EndsWith("ом") || adj.EndsWith("ем"))
                stem = adj.Substring(0, adj.Length - 2);
            else if (adj.EndsWith("ья") || adj.EndsWith("ье") || adj.EndsWith("ьи"))
                stem = adj.Substring(0, adj.Length - 1); // e.g. волч-ь
            else
                stem = adj;

            bool sibilantStem = stem.Length > 0 && (stem[stem.Length - 1] == 'ж' || stem[stem.Length - 1] == 'ш' || stem[stem.Length - 1] == 'ч' || stem[stem.Length - 1] == 'щ');
            bool softStem = sibilantStem || stem.EndsWith("ь");

            if (number == MorphNumber.Plural)
            {
                switch (targetCase)
                {
                    case MorphCase.Nom:
                    case MorphCase.Acc:
                        return stem + (softStem ? "ие" : "ые");
                    case MorphCase.Gen:
                        return stem + (softStem ? "их" : "ых");
                    case MorphCase.Dat:
                        return stem + (softStem ? "им" : "ым");
                    case MorphCase.Ins:
                        return stem + (softStem ? "ими" : "ыми");
                    case MorphCase.Prep:
                        return stem + (softStem ? "их" : "ых");
                    default: return adj;
                }
            }

            switch (gender)
            {
                case MorphGender.Masc:
                    switch (targetCase)
                    {
                        case MorphCase.Nom: return adj;
                        case MorphCase.Gen: return stem + (softStem ? "его" : "ого");
                        case MorphCase.Dat: return stem + (softStem ? "ему" : "ому");
                        case MorphCase.Acc: return adj;
                        case MorphCase.Ins: return stem + (softStem ? "им" : "ым");
                        case MorphCase.Prep: return stem + (softStem ? "ем" : "ом");
                        default: return adj;
                    }

                case MorphGender.Fem:
                    switch (targetCase)
                    {
                        case MorphCase.Nom: return stem + "ая";
                        case MorphCase.Gen: return stem + (softStem ? "ей" : "ой");
                        case MorphCase.Dat: return stem + (softStem ? "ей" : "ой");
                        case MorphCase.Acc: return stem + "ую";
                        case MorphCase.Ins: return stem + (softStem ? "ей" : "ой");
                        case MorphCase.Prep: return stem + (softStem ? "ей" : "ой");
                        default: return adj;
                    }

                case MorphGender.Neut:
                    switch (targetCase)
                    {
                        case MorphCase.Nom:
                        case MorphCase.Acc:
                            return stem + "ое";
                        case MorphCase.Gen: return stem + (softStem ? "его" : "ого");
                        case MorphCase.Dat: return stem + (softStem ? "ему" : "ому");
                        case MorphCase.Ins: return stem + (softStem ? "им" : "ым");
                        case MorphCase.Prep: return stem + (softStem ? "ем" : "ом");
                        default: return adj;
                    }
            }

            return adj;
        }

        // Обработка маркеров морфологии в тексте
        // Формат: {{case:word|case|gender|number}}
        // Пример: {{case:щелкун|gen|masc|sg}} → щелкуна
        // "auto" для gender — автоматическое определение по словарю или окончанию
        public static string ApplyMorphMarkers(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (!text.Contains("{{case:")) return text;

            try
            {
                return Regex.Replace(text, @"\{\{case:([^|]+)\|([^|]+)\|([^|]+)\|([^}]+)\}\}", match =>
                {
                    string word = match.Groups[1].Value.Trim();
                    string caseStr = match.Groups[2].Value.Trim().ToLower();
                    string genderStr = match.Groups[3].Value.Trim().ToLower();
                    string numberStr = match.Groups[4].Value.Trim().ToLower();

                    MorphCase mc = ParseCase(caseStr);
                    MorphNumber mn = numberStr == "pl" || numberStr == "plural" ? MorphNumber.Plural : MorphNumber.Singular;
                    
                    MorphGender mg;
                    if (genderStr == "auto")
                    {
                        // Автоматическое определение рода
                        mg = DetectGenderForWord(word);
                    }
                    else
                    {
                        mg = ParseGender(genderStr);
                    }

                    // Для автоматического рода используем Decline, который сам определит род
                    if (genderStr == "auto")
                    {
                        return Decline(word, mc, mn);
                    }
                    
                    return Decline(word, mc, mn);
                });
            }
            catch
            {
                return text;
            }
        }
        
        // Определение рода слова для морфологических маркеров
        private static MorphGender DetectGenderForWord(string word)
        {
            if (string.IsNullOrEmpty(word)) return MorphGender.Masc;
            
            // Сначала проверяем словарь
            if (nounDictionary != null && nounDictionary.TryGetValue(word, out NounForms forms))
            {
                return DetectGender(forms.Gender);
            }
            
            // Если нет в словаре — определяем по окончанию
            return DetectGenderByEnding(word);
        }

        private static MorphCase ParseCase(string s)
        {
            switch (s)
            {
                case "nom": case "nominative": return MorphCase.Nom;
                case "gen": case "genitive": return MorphCase.Gen;
                case "dat": case "dative": return MorphCase.Dat;
                case "acc": case "accusative": return MorphCase.Acc;
                case "ins": case "instrumental": return MorphCase.Ins;
                case "prep": case "prepositional": return MorphCase.Prep;
                default: return MorphCase.Nom;
            }
        }

        private static MorphGender ParseGender(string s)
        {
            switch (s)
            {
                case "masc": case "m": return MorphGender.Masc;
                case "fem": case "f": return MorphGender.Fem;
                case "neut": case "n": return MorphGender.Neut;
                default: return MorphGender.Masc;
            }
        }
    }
}
