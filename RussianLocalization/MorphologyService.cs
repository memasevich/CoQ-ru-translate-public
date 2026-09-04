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

    public class StemInfo
    {
        [JsonProperty("stem")]
        public string Stem { get; set; }
        [JsonProperty("type")]
        public string Type { get; set; }
        // Одушевлённость: влияет на винительный падеж (Acc = Gen для одушевлённых
        // мужского рода ед.ч. и всех родов во мн.ч.). По умолчанию false — старое поведение.
        [JsonProperty("anim")]
        public bool Anim { get; set; }
    }

    // ===== СГЕНЕРИРОВАННЫЙ СЛОВАРЬ ФОРМ (forms_dictionary.json, pymorphy3/OpenCorpora) =====
    // Записи трёх видов (поле "p"):
    //   "n" — существительное: g (род), a (одуш.), sg[6], pl[6]
    //   "a" — прилагательное/причастие: m[6], f[6], n[6] (ед.ч. по родам), pl[6]
    //   "r" — алиас: ref (лемма), n ("pl" если алиас — форма мн.ч.)
    public enum FormsKind { Noun, Adjective, Alias }

    public class FormsEntry
    {
        public FormsKind Kind;
        public string Gender;      // "masc"/"fem"/"neut" (только сущ.)
        public bool Anim;          // одушевлённость (только сущ.)
        public string[] Sg;        // сущ.: ед.ч. 6 падежей
        public string[] Pl;        // сущ./прил.: мн.ч. 6 падежей
        public string[] M;         // прил.: ед.ч. masc
        public string[] F;         // прил.: ед.ч. fem
        public string[] N;         // прил.: ед.ч. neut
        public string RefLemma;    // алиас: лемма
        public bool AliasPlural;   // алиас: форма мн.ч.
    }

    public static class MorphologyService
    {
        private static readonly ConcurrentDictionary<string, string> morphCache =
            new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static Dictionary<string, NounForms> nounDictionary;
        private static Dictionary<string, StemInfo> stemDictionary;
        private static Dictionary<string, FormsEntry> formsDictionary;
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
                stemDictionary = new Dictionary<string, StemInfo>(StringComparer.OrdinalIgnoreCase);

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
                        try { TranslationEngine.LogError("[RussianLocalization] MorphologyService failed to load morphology_dictionary.json: " + ex.Message); } catch { }
                    }
                }
                else
                {
                    try { TranslationEngine.LogInfo("[RussianLocalization] MorphologyService: no morphology_dictionary.json found, using rules only."); } catch { }
                }

                string stemPath = Path.Combine(modPath, "stem_dictionary.json");
                if (File.Exists(stemPath))
                {
                    try
                    {
                        string json = File.ReadAllText(stemPath, Encoding.UTF8);
                        var loaded = JsonConvert.DeserializeObject<Dictionary<string, StemInfo>>(json);
                        if (loaded != null)
                        {
                            foreach (var kvp in loaded)
                            {
                                if (!string.IsNullOrEmpty(kvp.Key) && kvp.Value != null)
                                    stemDictionary[kvp.Key] = kvp.Value;
                            }
                        }
                        try { TranslationEngine.LogInfo("[RussianLocalization] MorphologyService loaded " + stemDictionary.Count + " stem entries."); } catch { }
                    }
                    catch (Exception ex)
                    {
                        try { TranslationEngine.LogError("[RussianLocalization] MorphologyService failed to load stem_dictionary.json: " + ex.Message); } catch { }
                    }
                }

                // Сгенерированный словарь полных парадигм (pymorphy3/OpenCorpora).
                // Приоритет: morphology_dictionary.json (ручной) > forms_dictionary.json >
                // stem_dictionary.json > правила. Формат см. у FormsEntry.
                string formsPath = Path.Combine(modPath, "forms_dictionary.json");
                if (File.Exists(formsPath))
                {
                    try
                    {
                        string json = File.ReadAllText(formsPath, Encoding.UTF8);
                        formsDictionary = new Dictionary<string, FormsEntry>(StringComparer.OrdinalIgnoreCase);
                        var root = Newtonsoft.Json.Linq.JObject.Parse(json);
                        foreach (var prop in root.Properties())
                        {
                            FormsEntry fe = ParseFormsEntry(prop.Value as Newtonsoft.Json.Linq.JObject);
                            if (fe != null && !string.IsNullOrEmpty(prop.Name))
                                formsDictionary[prop.Name] = fe;
                        }
                        try { TranslationEngine.LogInfo("[RussianLocalization] MorphologyService loaded " + formsDictionary.Count + " generated forms entries."); } catch { }
                    }
                    catch (Exception ex)
                    {
                        try { TranslationEngine.LogError("[RussianLocalization] MorphologyService failed to load forms_dictionary.json: " + ex.Message); } catch { }
                    }
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
        // 2026-07-31: в классической разметке Qud код "&R" сбрасывает цвет ПОСЛЕ слова
        // ("&wbrackish&R &Ktarry&R"), то есть является суффиксом, а не только префиксом.
        // Без него "солоноватый&R" не опознавалось как слово и оставалось несклонённым.
        private static readonly Regex TagSuffixRegex = new Regex(@"(?:</color>|\}\}|&[a-zA-Z]|[!?:.,;])+$", RegexOptions.Compiled);

        // Только цветовые коды Qud. Служит для снятия разметки перед проверками на латиницу
        // и сложность фразы — сами буквы кодов не должны считаться английским текстом.
        private static readonly Regex ColorCodeRegex = new Regex(@"&[a-zA-Z]", RegexOptions.Compiled);

        // Разбор одной записи forms_dictionary.json (форматы см. у FormsEntry).
        private static FormsEntry ParseFormsEntry(Newtonsoft.Json.Linq.JObject o)
        {
            if (o == null) return null;
            string p = o.Value<string>("p");
            if (string.IsNullOrEmpty(p)) return null;
            FormsEntry fe = new FormsEntry();
            if (p == "r")
            {
                fe.Kind = FormsKind.Alias;
                fe.RefLemma = o.Value<string>("ref");
                fe.AliasPlural = o.Value<string>("n") == "pl";
                return string.IsNullOrEmpty(fe.RefLemma) ? null : fe;
            }
            if (p == "n")
            {
                fe.Kind = FormsKind.Noun;
                fe.Gender = o.Value<string>("g") ?? "masc";
                fe.Anim = o.Value<int?>("a") == 1;
                fe.Sg = o["sg"] != null ? o["sg"].ToObject<string[]>() : null;
                fe.Pl = o["pl"] != null ? o["pl"].ToObject<string[]>() : null;
                return fe;
            }
            if (p == "a")
            {
                fe.Kind = FormsKind.Adjective;
                fe.M = o["m"] != null ? o["m"].ToObject<string[]>() : null;
                fe.F = o["f"] != null ? o["f"].ToObject<string[]>() : null;
                fe.N = o["n"] != null ? o["n"].ToObject<string[]>() : null;
                fe.Pl = o["pl"] != null ? o["pl"].ToObject<string[]>() : null;
                return fe;
            }
            return null;
        }

        // Поиск в forms_dictionary с разрешением алиаса.
        // aliasPlural=true, если входное слово — форма мн.ч. (алиас с n:"pl").
        private static FormsEntry ResolveForms(string word, out bool aliasPlural)
        {
            aliasPlural = false;
            if (string.IsNullOrEmpty(word) || formsDictionary == null) return null;
            FormsEntry e;
            if (!formsDictionary.TryGetValue(word, out e) || e == null) return null;
            if (e.Kind == FormsKind.Alias)
            {
                aliasPlural = e.AliasPlural;
                FormsEntry target;
                if (e.RefLemma != null && formsDictionary.TryGetValue(e.RefLemma, out target))
                    return target;
                return null;
            }
            return e;
        }

        private static string GetForm(string[] forms, int idx)
        {
            if (forms == null || idx < 0 || idx >= forms.Length) return null;
            string v = forms[idx];
            return string.IsNullOrEmpty(v) ? null : v;
        }

        // Склонение существительного по сгенерированной парадигме.
        // Одушевлённость леммы уже "зашита" в хранимые формы (acc=gen для одуш.);
        // исключение — ср.род: pymorphy хранит acc=nom даже для одуш., поэтому
        // для одуш. ср.рода берём родительный ("существа", а не "существо").
        private static string DeclineFromNounForms(FormsEntry fe, MorphCase targetCase, MorphNumber number)
        {
            int idx = (int)targetCase;
            if (number == MorphNumber.Singular)
            {
                if (targetCase == MorphCase.Acc && fe.Anim &&
                    string.Equals(fe.Gender, "neut", StringComparison.OrdinalIgnoreCase))
                    idx = (int)MorphCase.Gen;
                return GetForm(fe.Sg, idx);
            }
            return GetForm(fe.Pl, idx);
        }

        // Склонение прилагательного/причастия по сгенерированной парадигме.
        // В хранимых формах acc для masc/pl — одушевлённый вариант (=род.п.);
        // для неодуш. контекста подставляем форму им.п.
        private static string DeclineFromAdjForms(string word, FormsEntry fe, MorphGender gender, MorphCase targetCase, MorphNumber number, bool animate, string reflexive, bool genderFromHead = false)
        {
            // Если вход совпадает с канонической формой рода — верим форме, а не угаданному роду
            // ("светящаяся" — это fem-форма, даже если вызывающий код угадал род иначе).
            // Исключение: род пришёл от главного слова словосочетания — там согласование
            // обязательно и важнее формы на входе ("солоноватый вода" -> "солоноватой воды").
            MorphGender g = gender;
            string w = word;
            if (!string.IsNullOrEmpty(reflexive) && w.EndsWith(reflexive, StringComparison.OrdinalIgnoreCase))
                w = w.Substring(0, w.Length - reflexive.Length);
            // 2026-08-03: genderFromHead действует ТОЛЬКО если слово стоит в мужском именительном.
            // Такую форму выдаёт пословный перевод (прилагательное и существительное берутся из
            // разных статей), и только её можно безопасно пересогласовать. Уже женская/средняя/
            // множественная форма пришла из готового перевода — переписывать её нельзя.
            string canonM = GetForm(fe.M, 0);
            bool isMascNomForm = CanonEquals(w, canonM, reflexive);
            if (isMascNomForm) g = genderFromHead ? gender : MorphGender.Masc;
            else
            {
                string canon = GetForm(fe.F, 0);
                if (CanonEquals(w, canon, reflexive)) g = MorphGender.Fem;
                else
                {
                    canon = GetForm(fe.N, 0);
                    if (CanonEquals(w, canon, reflexive)) g = MorphGender.Neut;
                }
            }

            string[] forms;
            if (number == MorphNumber.Plural)
                forms = fe.Pl;
            else if (g == MorphGender.Fem)
                forms = fe.F;
            else if (g == MorphGender.Neut)
                forms = fe.N;
            else
                forms = fe.M;

            int idx = (int)targetCase;
            if (targetCase == MorphCase.Acc && !animate)
            {
                // неодуш.: acc = nom (кроме жен.рода ед.ч. — там своя форма "мокрую")
                if (number == MorphNumber.Plural) idx = 0;
                else if (g != MorphGender.Fem) idx = 0;
            }
            else if (targetCase == MorphCase.Acc && animate)
            {
                // У прилагательного нет собственной одушевлённости: в сгенерированной
                // парадигме Acc часто совпадает с Nom. В составе именной группы
                // одушевлённый мужской/средний род и множественное число требуют
                // формы Gen ("вижу нового щелкуна", "вижу новых щелкунов").
                // Жен. ед. сохраняет отдельную Acc-форму ("вижу новую ...").
                if (number == MorphNumber.Plural || g == MorphGender.Masc || g == MorphGender.Neut)
                    idx = (int)MorphCase.Gen;
            }
            return GetForm(forms, idx);
        }

        private static bool CanonEquals(string word, string canon, string reflexive)
        {
            if (string.IsNullOrEmpty(canon)) return false;
            if (string.Equals(word, canon, StringComparison.OrdinalIgnoreCase)) return true;
            // Сравнение с учётом снятого возвратного постфикса: "светящая" == "светящаяся" - "ся"
            if (!string.IsNullOrEmpty(reflexive) && canon.EndsWith(reflexive, StringComparison.OrdinalIgnoreCase))
            {
                string stripped = canon.Substring(0, canon.Length - reflexive.Length);
                if (string.Equals(word, stripped, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        // Токен, не содержащий ни одной буквы, но содержащий хотя бы одну цифру:
        // "5", "40%", "1d2", "(x3)", "12,5". Такие токены не склоняются ни в каком падеже.
        private static bool IsNumericToken(string word)
        {
            if (string.IsNullOrEmpty(word)) return false;
            bool hasDigit = false;
            for (int i = 0; i < word.Length; i++)
            {
                char c = word[i];
                if (char.IsLetter(c)) return false;
                if (char.IsDigit(c)) hasDigit = true;
            }
            return hasDigit;
        }

        private static bool IsAdjectiveBase(string word)
        {
            if (string.IsNullOrEmpty(word)) return false;
            string w = word.ToLower();
            return w.EndsWith("ый") || w.EndsWith("ий") || w.EndsWith("ой") ||
                   w.EndsWith("ая") || w.EndsWith("яя") ||
                   w.EndsWith("ое") || w.EndsWith("ее") ||
                   w.EndsWith("ые") || w.EndsWith("ие") ||
                   w.EndsWith("ья") || w.EndsWith("ье") || w.EndsWith("ьи");
        }

        // Возвратный постфикс -ся прячет прилагательное/причастное окончание: "светящаяся"
        // оканчивается на "ся", а не на "ая", поэтому раньше слово не опознавалось как
        // прилагательное. Из-за этого оно (а) могло стать «главным словом» фразы вместо
        // существительного и (б) склонялось как существительное: "светящаяся" -> "светящаяси".
        // Срезаем постфикс и проверяем основу. Режем только "ся" — "сь" трогать нельзя,
        // иначе пострадают существительные вроде "рысь".
        private static bool HasReflexivePostfix(string word)
        {
            if (string.IsNullOrEmpty(word) || word.Length <= 4) return false;
            if (!word.ToLower().EndsWith("ся")) return false;
            return IsAdjectiveBase(word.Substring(0, word.Length - 2));
        }

        private static bool IsAdjective(string word)
        {
            if (string.IsNullOrEmpty(word)) return false;
            if (HasReflexivePostfix(word)) return true;
            return IsAdjectiveBase(word);
        }

        // genderFromHead=true означает, что род пришёл от главного существительного словосочетания
        // и обязателен для согласования. Тогда прилагательное НЕ имеет права оставить свой род:
        // игра склеивает "brackish" + "water" из разных словарных статей, и на входе оказывается
        // рассогласованное "солоноватый вода" — по-русски определение обязано стать "солоноватой".
        private static string DeclineSingleWord(string word, MorphGender gender, MorphCase targetCase, MorphNumber number, bool animate = false, bool genderFromHead = false)
        {
            if (string.IsNullOrEmpty(word)) return word;

            var preMatch = TagPrefixRegex.Match(word);
            string prefix = preMatch.Success ? preMatch.Value : "";
            var sufMatch = TagSuffixRegex.Match(word);
            string suffix = sufMatch.Success ? sufMatch.Value : "";

            // Токен целиком состоит из разметки ("&y", "</color>", "}}") — склонять нечего.
            // Префиксная и суффиксная маски здесь ПЕРЕКРЫВАЮТСЯ (обе матчат один и тот же "&y"),
            // и наивное Substring(0, clean.Length - suffix.Length) уходило в отрицательную длину
            // -> ArgumentOutOfRangeException. Исключение всплывало из MatchEvaluator'а в
            // Regex.Replace, ApplyMorphMarkers ловил его целиком и возвращал ВСЮ строку
            // нетронутой — игрок видел сырой "{{case:&wбронзовый &y длинный меч|acc|auto|sg}}".
            // Одиночные "&y" встречаются в цепочках жидкостей ("&rокровавленный &y &Kасфальт"),
            // поэтому падал каждый такой текст, а не редкий угол.
            if (prefix.Length + suffix.Length >= word.Length) return word;

            string clean = word;
            if (prefix.Length > 0) clean = clean.Substring(prefix.Length);
            if (suffix.Length > 0) clean = clean.Substring(0, clean.Length - suffix.Length);

            if (string.IsNullOrEmpty(clean)) return word;

            // Числовой токен склонению не подлежит НИКОГДА. Decline() уже отсекает строки,
            // целиком состоящие из цифр, но фраза вида "5 против 7" туда не попадает: она
            // уходит в разбор по частям, где "5" не опознаётся ни как существительное, ни
            // как прилагательное и потому становится «главным словом» группы. Дальше к нему
            // приклеивалось падежное окончание, и игрок видел "[5у против 7]" вместо
            // "[5 против 7]" (дательный приходил от предлога "по" в шаблоне промаха).
            // Проверяем именно clean — цифры могли быть обёрнуты в цветовые коды ("&C40").
            if (IsNumericToken(clean)) return word;

            // Возвратный постфикс -ся склонению не подлежит: склоняем основу, затем возвращаем
            // постфикс на место ("светящаяся" -> основа "светящая" -> род. "светящей" -> "светящейся").
            // Без этого движок принимал конечное "я" в "ся" за окончание и выдавал "светящаяси".
            string reflexive = "";
            if (HasReflexivePostfix(clean))
            {
                reflexive = clean.Substring(clean.Length - 2);
                clean = clean.Substring(0, clean.Length - 2);
            }

            string declined = null;

            // 0. Сгенерированный словарь полных парадигм (pymorphy3) — самый точный источник.
            // Алиас мн.ч. принудительно переключает число ("торговцы" склоняется как мн.ч.).
            // Для возвратных причастий пробуем и полную форму с -ся ("светящий" -> "светящийся").
            bool aliasPlural;
            FormsEntry fe = ResolveForms(clean, out aliasPlural);
            if (fe == null && reflexive.Length > 0)
            {
                fe = ResolveForms(clean + reflexive, out aliasPlural);
            }
            if (fe != null)
            {
                MorphNumber effNumber = aliasPlural ? MorphNumber.Plural : number;
                if (fe.Kind == FormsKind.Noun)
                {
                    declined = MatchLeadingCase(DeclineFromNounForms(fe, targetCase, effNumber), clean);
                }
                else if (fe.Kind == FormsKind.Adjective)
                {
                    declined = MatchLeadingCase(DeclineFromAdjForms(clean, fe, gender, targetCase, effNumber, animate, reflexive, genderFromHead), clean);
                }
                // Запрошенной формы нет в парадигме (угадайка pymorphy для дефисных
                // композитов часто неполная) — отдаём слово как есть, а не гоним
                // дефисную строку через правила, которые выдадут мусор.
                if (declined == null)
                {
                    declined = clean;
                }
                // pluralia tantum: парадигма ед.ч. пуста — не ломаем слово, вернём как есть
                if (declined == null && effNumber == MorphNumber.Singular &&
                    fe.Kind == FormsKind.Noun && GetForm(fe.Pl, 0) != null && GetForm(fe.Sg, 0) == null)
                {
                    declined = clean;
                }
                // Формы из сгенерированного словаря уже корректны (в т.ч. для вымышленных
                // слов Qud вроде "агыр"→"агыра") — CorrectSpelling к ним НЕ применяем,
                // иначе правило жи-ши ломает фантазийные слова ("агыра"→"агира").
                if (declined != null)
                {
                    string spelledForms = prefix + declined + suffix;
                    if (reflexive.Length > 0 && !spelledForms.EndsWith(reflexive + suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        spelledForms = prefix + declined + reflexive + suffix;
                    }
                    return spelledForms;
                }
            }

            // 1. Попытка склонения по словарю основ.
            // Для существительных берём одушевлённость из самой записи (info.Anim),
            // для прилагательных — из переданного контекста фразы (animate).
            if (string.IsNullOrEmpty(declined) && stemDictionary != null && stemDictionary.TryGetValue(clean, out StemInfo info))
            {
                bool wordAnim = IsAdjective(clean) ? animate : (info.Anim || animate);
                declined = DeclineUsingStemDictionary(clean, info, targetCase, number, gender, wordAnim);
            }

            // 2. Если по словарям не найдено — используем старую логику правил/словарей
            if (string.IsNullOrEmpty(declined))
            {
                // Существительные на -ие/-ое/-ее ("здание", "навершие") по окончанию
                // похожи на прилагательные — словарям верим больше, чем окончанию.
                if (IsAdjective(clean) && !IsKnownNoun(clean))
                {
                    declined = DeclineAdjective(clean, gender, targetCase, number, animate);
                }
                else
                {
                    if (nounDictionary != null && nounDictionary.TryGetValue(clean, out NounForms forms))
                    {
                        string fromDict = forms.Get(targetCase, number);
                        if (!string.IsNullOrEmpty(fromDict))
                            declined = fromDict;
                        else
                            declined = ApplyNounRules(clean, DetectGender(forms.Gender), targetCase, number, animate);
                    }
                    else
                    {
                        MorphGender wGender = DetectGenderByEnding(clean);
                        declined = ApplyNounRules(clean, wGender, targetCase, number, animate);
                    }
                }
            }

            // CorrectSpelling применяли только в ветках словаря основ и прилагательных, из-за чего
            // ветка ApplyNounRules (самая частая — слов нет в stem_dictionary) отдавала формы,
            // нарушающие правило «жи-ши»: "кожаной кепкы" вместо "кожаной кепки". Правило
            // орфографическое и обязательное, поэтому прогоняем результат в любом случае;
            // для веток, где оно уже применялось, вызов идемпотентен.
            // Возвратный постфикс не дублируем: формы из сгенерированного словаря уже
            // содержат -ся ("светящейся"), а из правил — нет ("светящей" + "ся").
            string spelled = prefix + CorrectSpelling(declined) + suffix;
            if (reflexive.Length > 0 && !spelled.EndsWith(reflexive + suffix, StringComparison.OrdinalIgnoreCase))
            {
                spelled = prefix + CorrectSpelling(declined) + reflexive + suffix;
            }
            return spelled;
        }

        private static string DeclineUsingStemDictionary(string word, StemInfo info, MorphCase targetCase, MorphNumber number, MorphGender fallbackGender, bool animate = false)
        {
            if (info == null || string.IsNullOrEmpty(info.Stem) || string.IsNullOrEmpty(info.Type))
                return null;

            string stem = info.Stem;
            string type = info.Type.ToLower();

            // Прилагательные
            if (type == "adj_hard")
            {
                return MatchLeadingCase(CorrectSpelling(DeclineAdjectiveStem(stem, true, fallbackGender, targetCase, number, animate)), word);
            }
            if (type == "adj_soft")
            {
                return MatchLeadingCase(CorrectSpelling(DeclineAdjectiveStem(stem, false, fallbackGender, targetCase, number, animate)), word);
            }

            // Существительные
            string result = null;
            if (number == MorphNumber.Singular)
            {
                switch (type)
                {
                    case "m1":
                        switch (targetCase)
                        {
                            case MorphCase.Nom: result = word; break;
                            case MorphCase.Gen: result = stem + "а"; break;
                            case MorphCase.Dat: result = stem + "у"; break;
                            case MorphCase.Acc: result = animate ? stem + "а" : word; break; // одуш.: Acc = Gen
                            case MorphCase.Ins: result = stem + "ом"; break;
                            case MorphCase.Prep: result = stem + "е"; break;
                        }
                        break;
                    case "m2":
                        switch (targetCase)
                        {
                            case MorphCase.Nom: result = stem + "ь"; break;
                            case MorphCase.Gen: result = stem + "я"; break;
                            case MorphCase.Dat: result = stem + "ю"; break;
                            case MorphCase.Acc: result = animate ? stem + "я" : stem + "ь"; break; // одуш.: Acc = Gen
                            case MorphCase.Ins: result = stem + "ем"; break;
                            case MorphCase.Prep: result = stem + "е"; break;
                        }
                        break;
                    case "f1a":
                        switch (targetCase)
                        {
                            case MorphCase.Nom: result = stem + "а"; break;
                            case MorphCase.Gen: result = stem + "ы"; break;
                            case MorphCase.Dat: result = stem + "е"; break;
                            case MorphCase.Acc: result = stem + "у"; break;
                            case MorphCase.Ins: result = stem + "ой"; break;
                            case MorphCase.Prep: result = stem + "е"; break;
                        }
                        break;
                    case "f1b":
                        switch (targetCase)
                        {
                            case MorphCase.Nom: result = stem + "я"; break;
                            case MorphCase.Gen: result = stem + "и"; break;
                            case MorphCase.Dat: result = stem + "е"; break;
                            case MorphCase.Acc: result = stem + "ю"; break;
                            case MorphCase.Ins: result = stem + "ей"; break;
                            case MorphCase.Prep: result = stem + "е"; break;
                        }
                        break;
                    case "f2":
                        switch (targetCase)
                        {
                            case MorphCase.Nom: result = stem + "ь"; break;
                            case MorphCase.Gen: result = stem + "и"; break;
                            case MorphCase.Dat: result = stem + "и"; break;
                            case MorphCase.Acc: result = stem + "ь"; break;
                            case MorphCase.Ins: result = stem + "ью"; break;
                            case MorphCase.Prep: result = stem + "и"; break;
                        }
                        break;
                    case "n1":
                        switch (targetCase)
                        {
                            case MorphCase.Nom: result = stem + "о"; break;
                            case MorphCase.Gen: result = stem + "а"; break;
                            case MorphCase.Dat: result = stem + "у"; break;
                            case MorphCase.Acc: result = stem + "о"; break;
                            case MorphCase.Ins: result = stem + "ом"; break;
                            case MorphCase.Prep: result = stem + "е"; break;
                        }
                        break;
                    case "n2":
                        switch (targetCase)
                        {
                            case MorphCase.Nom: result = stem + "е"; break;
                            case MorphCase.Gen: result = stem + "я"; break;
                            case MorphCase.Dat: result = stem + "ю"; break;
                            case MorphCase.Acc: result = stem + "е"; break;
                            case MorphCase.Ins: result = stem + "ем"; break;
                            case MorphCase.Prep: result = stem + "е"; break;
                        }
                        break;
                }
            }
            else // Plural
            {
                result = DeclinePluralNoun(stem, type, targetCase, animate);
            }

            return result != null ? MatchLeadingCase(CorrectSpelling(result), word) : null;
        }

        // Приводит регистр первой буквы результата к регистру исходного слова.
        // Основы в словаре могут храниться с заглавной буквы; склонённая форма
        // должна повторять регистр слова, пришедшего из перевода (напр. "кабан" → "кабана", не "Кабана").
        private static string MatchLeadingCase(string result, string source)
        {
            if (string.IsNullOrEmpty(result) || string.IsNullOrEmpty(source)) return result;
            char r = result[0], s = source[0];
            if (char.IsUpper(s) && char.IsLower(r)) return char.ToUpper(r) + result.Substring(1);
            if (char.IsLower(s) && char.IsUpper(r)) return char.ToLower(r) + result.Substring(1);
            return result;
        }

        private static string DeclineAdjectiveStem(string stem, bool hard, MorphGender gender, MorphCase targetCase, MorphNumber number, bool animate = false)
        {
            if (number == MorphNumber.Plural)
            {
                switch (targetCase)
                {
                    case MorphCase.Nom: return stem + (hard ? "ые" : "ие");
                    case MorphCase.Gen: return stem + (hard ? "ых" : "их");
                    case MorphCase.Dat: return stem + (hard ? "ым" : "им");
                    case MorphCase.Acc: return stem + (animate ? (hard ? "ых" : "их") : (hard ? "ые" : "ие"));
                    case MorphCase.Ins: return stem + (hard ? "ыми" : "ими");
                    case MorphCase.Prep: return stem + (hard ? "ых" : "их");
                }
            }

            switch (gender)
            {
                case MorphGender.Fem:
                    switch (targetCase)
                    {
                        case MorphCase.Nom: return stem + (hard ? "ая" : "яя");
                        case MorphCase.Gen: return stem + (hard ? "ой" : "ей");
                        case MorphCase.Dat: return stem + (hard ? "ой" : "ей");
                        case MorphCase.Acc: return stem + (hard ? "ую" : "юю");
                        case MorphCase.Ins: return stem + (hard ? "ой" : "ей");
                        case MorphCase.Prep: return stem + (hard ? "ой" : "ей");
                    }
                    break;

                case MorphGender.Neut:
                    switch (targetCase)
                    {
                        case MorphCase.Nom: return stem + (hard ? "ое" : "ее");
                        case MorphCase.Gen: return stem + (hard ? "ого" : "его");
                        case MorphCase.Dat: return stem + (hard ? "ому" : "ему");
                        case MorphCase.Acc: return stem + (hard ? "ое" : "ее");
                        case MorphCase.Ins: return stem + (hard ? "ым" : "им");
                        case MorphCase.Prep: return stem + (hard ? "ом" : "ем");
                    }
                    break;

                default: // Masc
                    switch (targetCase)
                    {
                        case MorphCase.Nom: return stem + (hard ? "ый" : "ий");
                        case MorphCase.Gen: return stem + (hard ? "ого" : "его");
                        case MorphCase.Dat: return stem + (hard ? "ому" : "ему");
                        case MorphCase.Acc: return stem + (animate ? (hard ? "ого" : "его") : (hard ? "ый" : "ий"));
                        case MorphCase.Ins: return stem + (hard ? "ым" : "им");
                        case MorphCase.Prep: return stem + (hard ? "ом" : "ем");
                    }
                    break;
            }
            return stem;
        }

        private static string DeclinePluralNoun(string stem, string type, MorphCase targetCase, bool animate = false)
        {
            switch (targetCase)
            {
                case MorphCase.Nom:
                    if (type == "m1" || type == "f1a") return stem + "ы";
                    if (type == "m2" || type == "f1b" || type == "f2") return stem + "и";
                    if (type == "n1") return stem + "а";
                    if (type == "n2") return stem + "я";
                    break;
                case MorphCase.Gen:
                    if (type == "m1") return stem + "ов";
                    if (type == "m2" || type == "f2" || type == "n2") return stem + "ей";
                    if (type == "f1a" || type == "f1b" || type == "n1") return stem; // нулевое окончание
                    break;
                case MorphCase.Dat:
                    if (type == "m2" || type == "f1b" || type == "f2" || type == "n2") return stem + "ям";
                    return stem + "ам";
                case MorphCase.Acc:
                    if (animate)
                    {
                        // Одуш. мн.ч.: Acc = Gen
                        if (type == "m1") return stem + "ов";
                        if (type == "m2" || type == "f2" || type == "n2") return stem + "ей";
                        if (type == "f1a" || type == "f1b" || type == "n1") return stem;
                    }
                    if (type == "m1" || type == "f1a") return stem + "ы";
                    if (type == "m2" || type == "f1b" || type == "f2") return stem + "и";
                    if (type == "n1") return stem + "а";
                    if (type == "n2") return stem + "я";
                    break;
                case MorphCase.Ins:
                    if (type == "m2" || type == "f1b" || type == "f2" || type == "n2") return stem + "ями";
                    return stem + "ами";
                case MorphCase.Prep:
                    if (type == "m2" || type == "f1b" || type == "f2" || type == "n2") return stem + "ях";
                    return stem + "ах";
            }
            return stem;
        }

        private static string CorrectSpelling(string word)
        {
            if (string.IsNullOrEmpty(word)) return word;
            return word.Replace("гы", "ги").Replace("кы", "ки").Replace("хы", "хи")
                       .Replace("жы", "жи").Replace("чы", "чи").Replace("шы", "ши").Replace("щы", "щи");
        }

        // Предлоги и союзы: всё, что идёт ПОСЛЕ них, к главной именной группе уже не относится
        // ("навершие из шести лопастей", "бурдюк с водой"), поэтому поиск главного слова здесь
        // останавливается, а хвост замораживается.
        private static readonly Dictionary<string, MorphCase> PrepositionCases = new Dictionary<string, MorphCase>(StringComparer.OrdinalIgnoreCase)
        {
            { "из", MorphCase.Gen },
            { "от", MorphCase.Gen },
            { "до", MorphCase.Gen },
            { "без", MorphCase.Gen },
            { "для", MorphCase.Gen },
            { "у", MorphCase.Gen },
            { "около", MorphCase.Gen },
            { "возле", MorphCase.Gen },
            { "мимо", MorphCase.Gen },
            { "после", MorphCase.Gen },
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
            { "с", MorphCase.Ins },
            { "со", MorphCase.Ins },
            { "в", MorphCase.Prep },
            { "во", MorphCase.Prep },
            { "на", MorphCase.Prep },
            { "о", MorphCase.Prep },
            { "об", MorphCase.Prep },
            { "обо", MorphCase.Prep },
            { "при", MorphCase.Prep },
            { "ко", MorphCase.Dat },
            { "кроме", MorphCase.Gen },
            { "против", MorphCase.Gen },
            { "из-за", MorphCase.Gen },
            { "из-под", MorphCase.Gen }
        };

        private static readonly HashSet<string> HeadStopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "из", "от", "до", "без", "для", "у", "около", "возле", "мимо", "после", "к", "по",
            "через", "про", "сквозь", "над", "под", "перед", "за", "между", "с", "со", "в", "во",
            "на", "о", "об", "обо", "при", "и", "или", "а", "но",
            "ко", "кроме", "против", "из-за", "из-под"
        };

        // ===== Списки исключений для fallback-правил =====
        // (служат только для слов ВНЕ всех словарей — неологизмов Qud;
        //  покрытые словарями слова склоняются по точным парадигмам)

        // Существительные только мн.ч. — ед.ч. не образуем и слово не ломаем.
        private static readonly HashSet<string> PluraliaTantum = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ножницы", "сумерки", "очки", "ворота", "щипцы", "санки", "часы", "брюки",
            "штаны", "носилки", "качели", "деньги", "дрова", "перила", "чернила",
            "каникулы", "будни", "хлопья", "макароны", "похороны", "проводы", "дебри"
        };

        // Жен. род на -ь, не определяемый по окончанию (мягкий знак не после шипящих).
        private static readonly HashSet<string> FemSoftSignNouns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "тень", "мишень", "дверь", "соль", "пыль", "мель", "фасоль", "мозоль",
            "степь", "цепь", "топь", "верфь", "гибель", "трель", "дрель", "мочь"
        };

        // Муж. род на -ь с творительным на -ём (короткие ударные): конём, дождём.
        private static readonly HashSet<string> MascSoftInsYo = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "конь", "дождь", "рубль", "гвоздь", "пень", "лёд", "лед"
        };

        // Муж. род на -ень с беглой гласной: день→дня, кремень→кремня.
        private static readonly HashSet<string> MascFleetingEn = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "день", "кремень", "камень", "пень", "лень"
        };

        // Притяжательные прилагательные на -ий со вставным ь: волчий→волчьего.
        private static readonly HashSet<string> PossessiveAdjStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "волч", "лис", "птич", "заяч", "медвеж", "собач", "кошач", "щуч", "барсуч", "овеч"
        };

        // Неопределённость рода для -ь: -ость/-есть/-сть/-знь/-вь/-зь → жен.род.
        private static bool LooksFeminineSoftSign(string word)
        {
            string w = word.ToLower();
            if (FemSoftSignNouns.Contains(w)) return true;
            if (w.EndsWith("ость") || w.EndsWith("есть") || w.EndsWith("сть") ||
                w.EndsWith("знь") || w.EndsWith("вь") || w.EndsWith("зь")) return true;
            return false;
        }

        // IsAdjective опознаёт часть речи только по окончанию, поэтому существительные на
        // -ие/-ое/-ее ("навершие", "оружие", "снаряжение") ошибочно считаются прилагательными.
        // Словари знают истину — если слово записано как существительное, верим им, а не окончанию.
        private static bool IsKnownNoun(string word)
        {
            if (string.IsNullOrEmpty(word)) return false;
            bool aliasPlural;
            FormsEntry fe = ResolveForms(word, out aliasPlural);
            if (fe != null) return fe.Kind == FormsKind.Noun;
            if (stemDictionary != null && stemDictionary.TryGetValue(word, out StemInfo si))
            {
                if (si != null && !string.IsNullOrEmpty(si.Type))
                    return !si.Type.ToLower().StartsWith("adj");
            }
            // ВНИМАНИЕ: morphology_dictionary.json — это рукописные ПАРАДИГМЫ, а не разметка частей
            // речи. Из 3432 записей ~2000 на деле прилагательные ("агрессивный", "обглоданный"), и
            // здесь они считаются существительными. Из-за этого определение может стать «главным
            // словом» группы, а настоящее существительное после него — замёрзнуть
            // ("обглоданного влагопаутинник"). Массовая правка проверялась (2026-07-31) и была
            // отклонена: она меняет 5100 форм, в том числе 16 в худшую сторону
            // ("солёной водой" -> "солёным водой"). Чинить надо данными, по одной записи.
            if (nounDictionary != null && nounDictionary.ContainsKey(word)) return true;
            return false;
        }

        // true, если часть с индексом idx соединена с главным словом ТОЛЬКО дефисами.
        // Дефисный композит склоняется целиком ("щелкун-охотник" -> "щелкуна-охотника"),
        // а вот отделённое пробелом слово после главного — уже зависимый родительный.
        private static bool IsHyphenBoundTo(string[] parts, int headIdx, int idx)
        {
            if (headIdx < 0 || idx <= headIdx) return false;
            for (int i = headIdx + 1; i < idx; i++)
            {
                string p = parts[i];
                if (string.IsNullOrEmpty(p)) continue;
                if (p == "-") continue;
                // Любой пробельный разделитель обрывает композит.
                if (string.IsNullOrWhiteSpace(p)) return false;
            }
            // Непосредственно перед idx должен стоять дефис.
            return idx - 1 >= 0 && parts[idx - 1] == "-";
        }

        // Наречная приставка дефисного композита ("кроваво-мокрый", "тёмно-зелёный"):
        // часть на -о/-е, за которой стоит дефис. Склонению не подлежит и не может
        // быть главным словом фразы (иначе "кроваво" склонялось как сущ. ср.рода → "кровава").
        private static bool IsAdverbCompoundPrefix(string[] parts, int idx)
        {
            if (idx < 0 || idx + 2 >= parts.Length) return false;
            if (parts[idx + 1] != "-") return false;
            string clean = TagPrefixRegex.Replace(parts[idx], "");
            clean = TagSuffixRegex.Replace(clean, "").Trim();
            if (clean.Length < 3) return false;
            char last = clean[clean.Length - 1];
            return last == 'о' || last == 'е';
        }

        public static string Decline(string nominative, MorphCase targetCase, MorphNumber number = MorphNumber.Singular, MorphGender? forcedGender = null)
        {
            if (string.IsNullOrEmpty(nominative)) return nominative;

            // Временно убираем суффиксы количества вида " x2", " x3", " (x2)", " x10" перед проверкой на латынь и склонением
            string quantitySuffix = "";
            string baseNominative = nominative;
            var suffixMatch = System.Text.RegularExpressions.Regex.Match(nominative, @"\s+\(?[xх]\d+\)?$");
            if (suffixMatch.Success)
            {
                quantitySuffix = suffixMatch.Value;
                baseNominative = nominative.Substring(0, suffixMatch.Index);
            }

            // Проверки ниже смотрят на текст БЕЗ цветовых кодов Qud. Иначе буква кода принималась
            // за английский текст, и вся раскрашенная группа ("солоноватый&R &Kсмолистый&R &Bвода")
            // возвращалась несклонённой. Сами коды остаются в baseNominative — их снимает и
            // возвращает на место DeclineSingleWord через TagPrefixRegex/TagSuffixRegex.
            string guardText = baseNominative.IndexOf('&') >= 0
                ? ColorCodeRegex.Replace(baseNominative, "")
                : baseNominative;

            // Не склоняем слова/фразы, содержащие латинские буквы (имена собственные, английские названия)
            if (System.Text.RegularExpressions.Regex.IsMatch(guardText, "[a-zA-Z]"))
            {
                return nominative;
            }

            // Не склоняем числа — иначе "7" превращается в "7ом"
            if (System.Text.RegularExpressions.Regex.IsMatch(guardText, @"^\d+$"))
            {
                return nominative;
            }

            // 2026-09-03: Не склоняем плейсхолдеры, переменные и макросы игры ({имя}, {месяц}, {год}, =name=, <spice...>)
            if ((guardText.StartsWith("{") && guardText.EndsWith("}")) ||
                (guardText.StartsWith("=") && guardText.EndsWith("=")) ||
                (guardText.StartsWith("<") && guardText.EndsWith(">")) ||
                (guardText.StartsWith("[") && guardText.EndsWith("]")))
            {
                return nominative;
            }

            // Защита от порчи сложных фраз (со знаками препинания, союзами, предлогами или слишком длинных).
            // Склонять автоматически по правилам можно только простые словосочетания (прилагательное + существительное).
            // Исключение: цельный дефисный композит, известный словарю форм ("а-ха-ха-ха-ха" —
            // это лексема, а не фраза), склоняем по его парадигме.
            bool wholeCompoundKnown = false;
            if (!baseNominative.Contains(" ") && baseNominative.Contains("-"))
            {
                bool tmpAliasPlural;
                wholeCompoundKnown = ResolveForms(baseNominative, out tmpAliasPlural) != null;
            }
            if (guardText.Contains(",") ||
                guardText.Contains(";") ||
                guardText.Contains(" и ") ||
                guardText.Contains(" или ") ||
                guardText.Contains(" которых ") ||
                guardText.Contains(" они ") ||
                guardText.Contains(" о ") ||
                guardText.Contains(" в ") ||
                guardText.Contains(" на ") ||
                (guardText.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries).Length > 4 && !wholeCompoundKnown))
            {
                return nominative;
            }

            // Слова только мн.ч. (ножницы, сумерки): в ед.ч. не склоняем — вернём как есть.
            if (number == MorphNumber.Singular && PluraliaTantum.Contains(baseNominative))
            {
                return nominative;
            }

            // Явный род из {{case:...|...|gender|...}} должен проходить даже для им.п.:
            // иначе {{case:солоноватый|nom|fem|sg}} преждевременно возвращал мужскую форму.
            if (forcedGender == null && targetCase == MorphCase.Nom && number == MorphNumber.Singular && !baseNominative.Contains(" ") && !baseNominative.Contains("-"))
                return baseNominative + quantitySuffix;

            MaybeResetCache();

            // Род входит в ключ: один и тот же adjective может последовательно
            // склоняться для разных heads (например, masc и fem).
            string genderKey = forcedGender.HasValue ? ((int)forcedGender.Value).ToString() : "-";
            string cacheKey = baseNominative + "|" + (int)targetCase + "|" + (int)number + "|" + genderKey;
            if (morphCache.TryGetValue(cacheKey, out string cached))
                return cached + quantitySuffix;

            string result;

            if (baseNominative.Contains(" ") || baseNominative.Contains("-"))
            {
                // Цельный дефисный композит, известный словарю форм (в т.ч. вымышленные
                // "голем-антилопа", "асподель-светский", "а-банджаг"), склоняем по его
                // парадигме целиком — pymorphy корректно замораживает префикс там, где
                // это нужно. Неизвестные композиты уходят в разбор по частям ниже.
                if (!baseNominative.Contains(" "))
                {
                    bool wholeAliasPlural;
                    if (ResolveForms(baseNominative, out wholeAliasPlural) != null)
                    {
                        result = DeclineSingleWord(baseNominative, DetectGenderForWord(baseNominative),
                            targetCase, number, DetectAnimacyForWord(baseNominative));
                        morphCache[cacheKey] = result;
                        return result + quantitySuffix;
                    }
                }

                string[] rawParts = Regex.Split(baseNominative, @"(\s+|-)", RegexOptions.IgnoreCase);

                // Фраза, НАЧИНАЮЩАЯСЯ с предлога/союза («с руками», «из кабаньей кожи»), —
                // предложная группа, а не именная: склонять в ней нечего, а поиск головы
                // ошибается (стоп-слово обрывает поиск сразу, запасная ветка берет последнее
                // слово: «с руками» -> Nom.Sg -> «с рука»). Возвращаем исходную строку.
                for (int i = 0; i < rawParts.Length; i++)
                {
                    string first = TagPrefixRegex.Replace(rawParts[i], "");
                    first = TagSuffixRegex.Replace(first, "").Trim();
                    if (string.IsNullOrEmpty(first) || first == "-") continue;
                    if (HeadStopWords.Contains(first))
                    {
                        morphCache[cacheKey] = baseNominative;
                        return nominative;
                    }
                    break;
                }

                // ГЛАВНОЕ СЛОВО ФРАЗЫ = ПЕРВОЕ существительное, а не последнее.
                // В русской именной группе всё, что стоит ПОСЛЕ главного существительного, —
                // это зависимый родительный, который при склонении НЕ меняется:
                //   "труп щелкуна"  -> род. "трупа щелкуна"   (а не "трупа щелкуны")
                //   "навершие из шести лопастей" -> "навершия из шести лопастей"
                // Прежний код брал ПОСЛЕДНЕЕ несклоняемое слово ("щелкуна") за главное, из-за чего
                // (а) род всей фразы определялся по зависимому слову ("мокрой трупа" вместо
                // "мокрого трупа") и (б) зависимый родительный сам склонялся ("щелкуна" -> "щелкуны",
                // т.к. по окончанию -а он выглядит как женский именительный).
                // Для "прилагательное + существительное" ("мокрая дикая собака") первое
                // существительное и есть последнее, поэтому этот случай работает как раньше.
                int headIdx = -1;
                int lastBeforeStop = -1;
                for (int i = 0; i < rawParts.Length; i++)
                {
                    string clean = TagPrefixRegex.Replace(rawParts[i], "");
                    clean = TagSuffixRegex.Replace(clean, "").Trim();
                    if (string.IsNullOrEmpty(clean) || clean == "-") continue;
                    // Наречная приставка дефисного композита ("кроваво-мокрый") — не главное слово.
                    if (IsAdverbCompoundPrefix(rawParts, i)) continue;
                    // Предлог/союз — главная группа закончилась, дальше зависимый хвост.
                    if (PrepositionCases.ContainsKey(clean) || clean == "и" || clean == "или" || clean == "а" || clean == "но") break;
                    lastBeforeStop = i;
                    if (IsKnownNoun(clean) || !IsAdjective(clean))
                    {
                        headIdx = i;
                        break;
                    }
                }
                // Существительное не опознано (например, "навершие" — по окончанию похоже на
                // прилагательное, и в словарях его нет): берём последнее слово до предлога.
                if (headIdx < 0) headIdx = lastBeforeStop;
                if (headIdx < 0)
                {
                    // Совсем ничего не нашли — главным считаем последнее слово фразы.
                    for (int i = rawParts.Length - 1; i >= 0; i--)
                    {
                        string clean = TagPrefixRegex.Replace(rawParts[i], "");
                        clean = TagSuffixRegex.Replace(clean, "").Trim();
                        if (!string.IsNullOrEmpty(clean) && clean != "-")
                        {
                            headIdx = i;
                            break;
                        }
                    }
                }

                string headWord = "";
                if (headIdx >= 0)
                {
                    headWord = TagPrefixRegex.Replace(rawParts[headIdx], "");
                    headWord = TagSuffixRegex.Replace(headWord, "").Trim();
                }

                MorphGender phraseGender = forcedGender ?? DetectGenderForWord(headWord);
                bool phraseAnim = DetectAnimacyForWord(headWord);

                StringBuilder sb = new StringBuilder();
                int currentPrepIdx = -1;
                MorphCase currentPrepCase = MorphCase.Nom;

                for (int i = 0; i < rawParts.Length; i++)
                {
                    string part = rawParts[i];
                    if (string.IsNullOrWhiteSpace(part) || part == "-")
                    {
                        sb.Append(part);
                    }
                    else if (IsAdverbCompoundPrefix(rawParts, i))
                    {
                        // Наречная приставка дефисного композита ("кроваво-мокрый") — не склоняем.
                        sb.Append(part);
                    }
                    else if (i <= headIdx || IsHyphenBoundTo(rawParts, headIdx, i))
                    {
                        // Склоняем определения перед главным словом, само главное слово и части
                        // дефисного композита ("щелкун-охотник" -> "щелкуна-охотника").
                        // Определения (i < headIdx) обязаны согласоваться с родом главного слова.
                        // Ограничение на само согласование — в DeclineFromAdjForms: переписывается
                        // только форма мужского именительного. Дифференциальный прогон 03.08 по
                        // 474 378 формам: 646 изменений от базы, все в плюс.
                        sb.Append(DeclineSingleWord(part, phraseGender, targetCase, number, phraseAnim, i < headIdx));
                    }
                    else
                    {
                        // Зависимый хвост после главного слова.
                        // Проверяем, не предлог ли это
                        string clean = TagPrefixRegex.Replace(part, "");
                        clean = TagSuffixRegex.Replace(clean, "").Trim();
                        if (PrepositionCases.TryGetValue(clean, out MorphCase pCase))
                        {
                            currentPrepIdx = i;
                            currentPrepCase = pCase;
                            // Делаем предлог в середине фразы строчным ("Из" -> "из", "С" -> "с")
                            if (part.Equals("Из", StringComparison.Ordinal)) sb.Append("из");
                            else if (part.Equals("С", StringComparison.Ordinal)) sb.Append("с");
                            else if (part.Equals("Со", StringComparison.Ordinal)) sb.Append("со");
                            else sb.Append(part);
                        }
                        else if (currentPrepIdx >= 0 && currentPrepCase != MorphCase.Nom)
                        {
                            // Склоняем зависимые слова после предлога в нужный падеж (например, Gen для "из")
                            sb.Append(DeclineSingleWord(part, DetectGenderForWord(clean), currentPrepCase, MorphNumber.Singular, DetectAnimacyForWord(clean)));
                        }
                        else
                        {
                            sb.Append(part);
                        }
                    }
                }
                result = sb.ToString();
            }
            else
            {
                result = DeclineSingleWord(baseNominative, forcedGender ?? DetectGenderForWord(baseNominative), targetCase, number, DetectAnimacyForWord(baseNominative));
            }

            morphCache[cacheKey] = result;
            return result + quantitySuffix;
        }

        public static string DeclineAdjective(string adjective, MorphGender gender, MorphCase targetCase, MorphNumber number, bool animate = false)
        {
            if (string.IsNullOrEmpty(adjective)) return adjective;
            if (targetCase == MorphCase.Nom && number == MorphNumber.Singular) return adjective;

            string cacheKey = "adj|" + adjective + "|" + (int)gender + "|" + (int)targetCase + "|" + (int)number + "|" + (animate ? 1 : 0);
            if (morphCache.TryGetValue(cacheKey, out string cached))
                return cached;

            string result = ApplyAdjectiveRules(adjective, gender, targetCase, number, animate);
            morphCache[cacheKey] = result;
            return result;
        }

        // Совместимый публичный вход для AdjectivePatches.
        // В DescriptionBuilder род прилагательного приходит от главного существительного,
        // поэтому передаём genderFromHead=true и не даём внутреннему определению рода
        // переопределить согласование.
        public static string ForceDeclineAdjective(string adjective, MorphGender targetGender, MorphCase targetCase, MorphNumber targetNumber)
        {
            if (string.IsNullOrEmpty(adjective)) return adjective;
            return DeclineSingleWord(adjective, targetGender, targetCase, targetNumber, false, true);
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
                // -ость/-есть/-вь/-зь и список исключений — жен. род (тень, соль, любовь)
                if (LooksFeminineSoftSign(word)) return MorphGender.Fem;
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

        private static string ApplyNounRules(string word, MorphGender gender, MorphCase targetCase, MorphNumber number, bool animate = false)
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
                return ApplyPluralRules(word, stem, last, gender, targetCase, animate);

            // Singular
            switch (gender)
            {
                case MorphGender.Fem:
                    return ApplyFemSingular(word, stem, last, targetCase);
                case MorphGender.Neut:
                    return ApplyNeutSingular(word, stem, last, targetCase);
                default:
                    return ApplyMascSingular(word, stem, last, targetCase, animate);
            }
        }

        // Основа с учётом беглых гласных для муж. рода:
        // котёнок→котёнк-, отец→отц-, ковёр→ковр-, день→дн- (по списку).
        private static string MascFleetingStem(string word)
        {
            if (word.EndsWith("ёнок")) return word.Substring(0, word.Length - 2) + "к";    // котёнок→котёнк
            if (word.EndsWith("ец") && word.Length >= 4)
                return word.Substring(0, word.Length - 2) + "ц";                            // отец→отц, китайец→китайц
            if (word.EndsWith("ёр") && word.Length >= 4)
                return word.Substring(0, word.Length - 2) + "р";                            // ковёр→ковр
            if (word.EndsWith("ёк") && word.Length >= 4)
                return word.Substring(0, word.Length - 2) + "ьк";                           // играёк→играьк
            return null;
        }

        private static string ApplyMascSingular(string word, string stem, char last, MorphCase c, bool animate = false)
        {
            // Согласная или -ь (masc)
            bool soft = last == 'ь';
            bool sibilant = last == 'ж' || last == 'ш' || last == 'ч' || last == 'щ';

            // Беглые гласные (только для твёрдой основы): замок-не-трогаем, ковёр→ковр-.
            if (!soft && c != MorphCase.Nom)
            {
                string fleeting = MascFleetingStem(word);
                if (fleeting != null)
                {
                    switch (c)
                    {
                        case MorphCase.Gen: return fleeting + "а";
                        case MorphCase.Dat: return fleeting + "у";
                        case MorphCase.Acc: return animate ? fleeting + "а" : word;
                        case MorphCase.Ins: return fleeting + "ом";
                        case MorphCase.Prep: return fleeting + "е";
                    }
                }
            }

            if (soft)
            {
                // Тип m2 (конь, дождь, учитель): основа без -ь
                string s2 = word.Substring(0, word.Length - 1);
                // Беглая в -ень по списку: день→дн-, кремень→кремн-
                if (MascFleetingEn.Contains(word) && word.EndsWith("ень"))
                    s2 = word.Substring(0, word.Length - 3) + "н";
                switch (c)
                {
                    case MorphCase.Nom: return word;
                    case MorphCase.Gen: return s2 + "я";
                    case MorphCase.Dat: return s2 + "ю";
                    case MorphCase.Acc: return animate ? s2 + "я" : word;
                    case MorphCase.Ins:
                        if (MascSoftInsYo.Contains(word)) return s2 + "ём";
                        return s2 + "ем";
                    case MorphCase.Prep: return s2 + "е";
                    default: return word;
                }
            }

            switch (c)
            {
                case MorphCase.Nom: return word;
                case MorphCase.Gen:
                    return word + "а";
                case MorphCase.Dat:
                    return word + "у";
                case MorphCase.Acc:
                    return animate ? word + "а" : word; // одуш.: Acc = Gen
                case MorphCase.Ins:
                    if (sibilant) return word + "ем";
                    return word + "ом";
                case MorphCase.Prep:
                    return word + "е";
                default: return word;
            }
        }

        private static string ApplyFemSingular(string word, string stem, char last, MorphCase c)
        {
            bool soft = last == 'ь';
            bool sibilant = stem.Length > 0 && (stem[stem.Length - 1] == 'ж' || stem[stem.Length - 1] == 'ш' || stem[stem.Length - 1] == 'ч' || stem[stem.Length - 1] == 'щ');

            // Тип f2 (тень, ночь, мышь): ген/дат/пр. -и, тв. -ью
            if (soft)
            {
                string s2 = word.Substring(0, word.Length - 1);
                // Беглые в fem -ь: рожь→рж-, любовь→любов-, церковь→церков-
                if (word.EndsWith("ожь") && word.Length >= 4) s2 = word.Substring(0, word.Length - 3) + "ж";
                else if (word.EndsWith("овь") && word.Length >= 5) s2 = word.Substring(0, word.Length - 1);
                switch (c)
                {
                    case MorphCase.Nom: return word;
                    case MorphCase.Gen: return s2 + "и";
                    case MorphCase.Dat: return s2 + "и";
                    case MorphCase.Acc: return word;
                    case MorphCase.Ins: return s2 + "ью";
                    case MorphCase.Prep: return s2 + "и";
                    default: return word;
                }
            }

            // Слова на -ия (литания, мелодия): дат./пр. -ии
            bool endsIya = last == 'я' && stem.Length > 0 && stem[stem.Length - 1] == 'и';

            switch (c)
            {
                case MorphCase.Nom: return word;
                case MorphCase.Gen:
                    if (last == 'а')
                    {
                        // После г/к/х и шипящих пишется -и: руки, мухи, души.
                        bool iEnding = stem.Length > 0 &&
                            "гкхжчшщ".IndexOf(stem[stem.Length - 1]) >= 0;
                        return stem + (iEnding ? "и" : "ы");
                    }
                    if (last == 'я') return stem + "и";
                    return word;
                case MorphCase.Dat:
                    if (endsIya) return stem + "и";
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
                        // столица→столицей, мышца? — после ц и шипящих тв. на -ей
                        bool tsStem = stem.Length > 0 && stem[stem.Length - 1] == 'ц';
                        if (sibilant || tsStem) return stem + "ей";
                        return stem + "ой";
                    }
                    if (last == 'я') return stem + "ей";
                    return word;
                case MorphCase.Prep:
                    if (endsIya) return stem + "и";
                    if (last == 'а') return stem + "е";
                    if (last == 'я') return stem + "е";
                    return word;
                default: return word;
            }
        }

        // Неправильные существительные на -мя: время→времен-, имя→имен-.
        private static readonly HashSet<string> MyaNouns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "время", "имя", "пламя", "знамя", "семя", "бремя", "вымя", "темя", "племя", "стремя"
        };

        private static string ApplyNeutSingular(string word, string stem, char last, MorphCase c)
        {
            // -мя: основа на -ен (времен-, имен-)
            if (MyaNouns.Contains(word))
            {
                string s2 = word.Substring(0, word.Length - 2) + "ен";
                switch (c)
                {
                    case MorphCase.Nom:
                    case MorphCase.Acc:
                        return word;
                    case MorphCase.Gen: return s2 + "и";
                    case MorphCase.Dat: return s2 + "и";
                    case MorphCase.Ins: return s2 + "ем";
                    case MorphCase.Prep: return s2 + "и";
                    default: return word;
                }
            }

            // -ие (здание, снаряжение): предл. -ии
            bool endsIye = word.EndsWith("ие");

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
                    if (endsIye) return stem + "и";
                    if (last == 'о') return stem + "е";
                    if (last == 'е') return stem + "е";
                    return word;
                default: return word;
            }
        }

        private static bool IsConsonant(char ch)
        {
            return "бвгджзйклмнпрстфхцчшщ".IndexOf(ch) >= 0;
        }

        // Род.мн.ч. с нулевым окончанием и вставной гласной:
        // кепка→кепок, девушка→девушек, песня→песен, капля→капель.
        private static string ZeroGenPlural(string stem)
        {
            if (stem.Length < 2) return stem;
            char c1 = stem[stem.Length - 2];
            char c2 = stem[stem.Length - 1];
            if (!IsConsonant(c1) || !IsConsonant(c2) || c1 == 'й') return stem;
            if (c2 == 'к' || c2 == 'г' || c2 == 'х')
            {
                // после шипящих/ц — е (девушек, кружек), иначе — о (кепок, досок)
                char ins = (c1 == 'ж' || c1 == 'ш' || c1 == 'ч' || c1 == 'щ' || c1 == 'ц') ? 'е' : 'о';
                return stem.Substring(0, stem.Length - 1) + ins + c2;
            }
            if (c2 == 'н' || c2 == 'л')
            {
                return stem.Substring(0, stem.Length - 1) + 'е' + c2; // песен, башен, капел
            }
            return stem;
        }

        private static string ApplyPluralRules(string word, string stem, char last, MorphGender gender, MorphCase c, bool animate = false)
        {
            // Слова только мн.ч. — не ломаем
            if (PluraliaTantum.Contains(word)) return word;

            switch (c)
            {
                case MorphCase.Acc:
                    if (animate)
                    {
                        // Одуш. мн.ч.: Acc = Gen — повторяем ветку Gen
                        goto case MorphCase.Gen;
                    }
                    goto case MorphCase.Nom;
                case MorphCase.Nom:
                    if (last == 'ы' || last == 'и') return word; // уже мн.ч.
                    if (last == 'а')
                    {
                        // После г/к/х и шипящих окончание -ы меняется на -и:
                        // рука→руки, река→реки, душа→души.
                        bool iEnding = stem.Length > 0 &&
                            "гкхжчшщ".IndexOf(stem[stem.Length - 1]) >= 0;
                        return stem + (iEnding ? "и" : "ы");
                    }
                    if (last == 'я') return stem + "и";
                    if (last == 'о' || last == 'е') return stem + "а";
                    if (last == 'ь') return stem + "и"; // конь→кони, тень→тени
                    return word + "ы";
                case MorphCase.Gen:
                    if (last == 'а' || last == 'я')
                    {
                        // -ия → -ий (литаний), иначе нулевое + вставная гласная
                        if (last == 'я' && stem.Length > 0 && stem[stem.Length - 1] == 'и')
                            return stem + "й";
                        return ZeroGenPlural(stem);
                    }
                    if (last == 'о' || last == 'е')
                    {
                        // существо→существ, стекло→стекол, число→чисел; одиночная согласная — +ов (облаков)
                        if (stem.Length >= 2 && IsConsonant(stem[stem.Length - 1]) && IsConsonant(stem[stem.Length - 2]))
                            return ZeroGenPlural(stem);
                        return stem + "ов";
                    }
                    // муж. род на согласную
                    if (last == 'й') return stem + "ев"; // героев
                    if (last == 'ь') return stem + "ей"; // коней, дождей, учителей
                    if (last == 'ж' || last == 'ш' || last == 'ч' || last == 'щ') return word + "ей"; // мечей, ножей
                    if (last == 'ц') return word + "ов"; // огурцов, концов
                    return word + "ов"; // пауков
                case MorphCase.Dat:
                    if (last == 'а') return stem + "ам";
                    if (last == 'я') return stem + "ям";
                    if (last == 'о' || last == 'е') return stem + "ам";
                    if (last == 'ь') return stem + "ям";
                    return word + "ам";
                case MorphCase.Ins:
                    if (last == 'а') return stem + "ами";
                    if (last == 'я') return stem + "ями";
                    if (last == 'о' || last == 'е') return stem + "ами";
                    if (last == 'ь') return stem + "ями";
                    return word + "ами";
                case MorphCase.Prep:
                    if (last == 'а') return stem + "ах";
                    if (last == 'я') return stem + "ях";
                    if (last == 'о' || last == 'е') return stem + "ах";
                    if (last == 'ь') return stem + "ях";
                    return word + "ах";
                default: return word;
            }
        }

        private static string ApplyAdjectiveRules(string adj, MorphGender gender, MorphCase targetCase, MorphNumber number, bool animate = false)
        {
            if (string.IsNullOrEmpty(adj)) return adj;

            char last = adj[adj.Length - 1];
            string stem;

            // Определяем основу прилагательного
            if (last == 'й' || adj.EndsWith("ый") || adj.EndsWith("ий") || adj.EndsWith("ой") || adj.EndsWith("ей"))
            {
                if (adj.EndsWith("ый") || adj.EndsWith("ий") || adj.EndsWith("ой") || adj.EndsWith("ей"))
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
            bool kghcStem = stem.Length > 0 &&
                "гкхц".IndexOf(stem[stem.Length - 1]) >= 0;
            // Мягкие окончания: -ний (последний→последнем), но НЕ -ный (длинный→длинном).
            bool softStem = sibilantStem || stem.EndsWith("ь") || adj.EndsWith("ний");

            // 2026-08-03: до появления согласования в им.п. ветки Nom/Acc для жен. и ср. рода были
            // недостижимы, поэтому в них зашиты твёрдые окончания. Теперь они работают:
            //   щ/ш/ж/ч -> ср.р. "-ее" (командующее), жен.р. "-ая/-ую" (командующая);
            //   мягкая   -> ср.р. "-ее", жен.р. "-яя/-юю" (последняя);
            //   ударное окончание "-ой" (большой, чужой) ведёт себя как твёрдая основа:
            //   "большое", а НЕ "большее".
            bool stressedEnding = adj.EndsWith("ой");
            bool trulySoftStem = !sibilantStem && !stressedEnding && (stem.EndsWith("ь") || adj.EndsWith("ний"));
            bool neutSoftEnding = !stressedEnding && (sibilantStem || trulySoftStem);

            // Притяжательные прилагательные на -ий со вставным ь: волчий→волчьего.
            if (adj.EndsWith("ий") && PossessiveAdjStems.Contains(stem))
            {
                stem = stem + "ь";
                softStem = true;
            }

            if (number == MorphNumber.Plural)
            {
                // Для основ на г/к/х/ц множественное число также получает -ие/-их/-им:
                // русский→русские, тихий→тихие, немецкий→немецкие.
                bool pluralSoft = softStem || kghcStem;
                switch (targetCase)
                {
                    case MorphCase.Acc:
                        if (animate) return stem + (pluralSoft ? "их" : "ых");
                        return stem + (pluralSoft ? "ие" : "ые");
                    case MorphCase.Nom:
                        return stem + (pluralSoft ? "ие" : "ые");
                    case MorphCase.Gen:
                        return stem + (pluralSoft ? "их" : "ых");
                    case MorphCase.Dat:
                        return stem + (pluralSoft ? "им" : "ым");
                    case MorphCase.Ins:
                        return stem + (pluralSoft ? "ими" : "ыми");
                    case MorphCase.Prep:
                        return stem + (pluralSoft ? "их" : "ых");
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
                        case MorphCase.Acc: return animate ? stem + (softStem ? "его" : "ого") : adj;
                        case MorphCase.Ins: return stem + ((softStem || kghcStem) ? "им" : "ым");
                        case MorphCase.Prep: return stem + (softStem ? "ем" : "ом");
                        default: return adj;
                    }

                case MorphGender.Fem:
                    switch (targetCase)
                    {
                        case MorphCase.Nom: return stem + (trulySoftStem ? "яя" : "ая");
                        case MorphCase.Gen: return stem + (softStem ? "ей" : "ой");
                        case MorphCase.Dat: return stem + (softStem ? "ей" : "ой");
                        case MorphCase.Acc: return stem + (trulySoftStem ? "юю" : "ую");
                        case MorphCase.Ins: return stem + (softStem ? "ей" : "ой");
                        case MorphCase.Prep: return stem + (softStem ? "ей" : "ой");
                        default: return adj;
                    }

                case MorphGender.Neut:
                    switch (targetCase)
                    {
                        case MorphCase.Nom:
                        case MorphCase.Acc:
                            return stem + (neutSoftEnding ? "ее" : "ое");
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
        private static string ReplaceNestedMarker(string text, string markerName, Func<string, string> evaluator)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string markerPrefix = "{{" + markerName + ":";
            int startIdx = 0;
            while (startIdx < text.Length)
            {
                int found = text.IndexOf(markerPrefix, startIdx, StringComparison.Ordinal);
                if (found < 0) break;

                int contentStart = found + markerPrefix.Length;
                int braceDepth = 1;
                int cur = contentStart;
                while (cur < text.Length - 1 && braceDepth > 0)
                {
                    if (text[cur] == '{' && text[cur + 1] == '{')
                    {
                        braceDepth++;
                        cur += 2;
                    }
                    else if (text[cur] == '}' && text[cur + 1] == '}')
                    {
                        braceDepth--;
                        if (braceDepth == 0) break;
                        cur += 2;
                    }
                    else
                    {
                        cur++;
                    }
                }

                if (braceDepth == 0)
                {
                    string payload = text.Substring(contentStart, cur - contentStart);
                    string replacement;
                    try
                    {
                        replacement = evaluator(payload);
                    }
                    catch
                    {
                        replacement = payload;
                    }
                    text = text.Substring(0, found) + replacement + text.Substring(cur + 2);
                    startIdx = found + (replacement != null ? replacement.Length : 0);
                }
                else
                {
                    startIdx = contentStart;
                }
            }
            return text;
        }

        public static string ApplyMorphMarkers(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (!text.Contains("{{case:") && !text.Contains("{{agree:")) return text;

            if (text.Contains("{{agree:"))
            {
                text = ReplaceNestedMarker(text, "agree", payload =>
                {
                    string[] parts = payload.Split('|');
                    if (parts.Length < 4) return payload;
                    string number = parts[parts.Length - 1].Trim().ToLowerInvariant();
                    string caseStr = parts[parts.Length - 2].Trim().ToLowerInvariant();
                    string targetRaw = parts[parts.Length - 3].Trim();
                    string adjective = string.Join("|", parts, 0, parts.Length - 3).Trim();

                    try
                    {
                        string target = Regex.Replace(
                            targetRaw,
                            @"<[^>]+>|&[A-Za-z]|\{\{[^|]+\||\}\}",
                            "").Trim();
                        string[] p = target.Split(
                            new[] { ' ' },
                            StringSplitOptions.RemoveEmptyEntries);
                        string head = p.Length > 0 ? p[p.Length - 1] : target;
                        MorphGender gender = DetectGenderForWord(head);
                        MorphCase targetCase = ParseCase(caseStr);
                        MorphNumber morphNumber = (number == "pl" || number == "plural")
                            ? MorphNumber.Plural
                            : MorphNumber.Singular;
                        return DeclineAdjective(adjective, gender, targetCase, morphNumber, false);
                    }
                    catch
                    {
                        return adjective;
                    }
                });
            }

            if (text.Contains("{{case:"))
            {
                text = ReplaceNestedMarker(text, "case", payload =>
                {
                    string[] parts = payload.Split('|');
                    if (parts.Length < 4)
                    {
                        return parts.Length > 0 ? parts[0].Trim() : payload;
                    }

                    string numberStr = parts[parts.Length - 1].Trim().ToLowerInvariant();
                    string genderStr = parts[parts.Length - 2].Trim().ToLowerInvariant();
                    string caseStr = parts[parts.Length - 3].Trim().ToLowerInvariant();
                    string word = string.Join("|", parts, 0, parts.Length - 3).Trim();

                    try
                    {
                        MorphCase mc = ParseCase(caseStr);
                        MorphNumber mn = (numberStr == "pl" || numberStr == "plural") ? MorphNumber.Plural : MorphNumber.Singular;
                        MorphGender? forcedGender = genderStr == "auto" ? (MorphGender?)null : ParseGender(genderStr);
                        return Decline(word, mc, mn, forcedGender);
                    }
                    catch
                    {
                        return word;
                    }
                });
            }

            // Страховка: маркер не должен доживать до экрана ни при каких обстоятельствах.
            if (text.Contains("{{case:"))
            {
                try
                {
                    text = Regex.Replace(text, @"\{\{case:([^|}]+)(?:\|[^|}]*){0,3}\}\}", m => m.Groups[1].Value.Trim());
                }
                catch { }
            }

            if (text.Contains("{{agree:"))
            {
                try
                {
                    text = Regex.Replace(text, @"\{\{agree:([^|}]+)(?:\|[^|}]*){0,3}\}\}", m => m.Groups[1].Value.Trim());
                }
                catch { }
            }

            return text;
        }
        
        // Определение рода слова для морфологических маркеров
        public static MorphGender DetectGenderForWord(string word)
        {
            if (string.IsNullOrEmpty(word)) return MorphGender.Masc;

            // Сначала сгенерированный словарь форм (точный род из OpenCorpora)
            bool aliasPlural;
            FormsEntry fe = ResolveForms(word, out aliasPlural);
            if (fe != null && fe.Kind == FormsKind.Noun && !string.IsNullOrEmpty(fe.Gender))
                return DetectGender(fe.Gender);

            // Затем ручной словарь
            if (nounDictionary != null && nounDictionary.TryGetValue(word, out NounForms forms))
            {
                return DetectGender(forms.Gender);
            }

            // Если нет в словарях — определяем по окончанию
            return DetectGenderByEnding(word);
        }

        // Определение одушевлённости: сначала сгенерированный словарь форм,
        // затем словарь основ. Вне словарей — неодушевлённое (консервативно).
        private static bool DetectAnimacyForWord(string word)
        {
            if (string.IsNullOrEmpty(word)) return false;
            bool aliasPlural;
            FormsEntry fe = ResolveForms(word, out aliasPlural);
            if (fe != null && fe.Kind == FormsKind.Noun)
                return fe.Anim;
            if (stemDictionary != null && stemDictionary.TryGetValue(word, out StemInfo info))
                return info.Anim;
            return false;
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
