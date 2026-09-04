using HarmonyLib;
using System.Text.RegularExpressions;

namespace RussianLocalization.Patches
{
    [HarmonyPatch(typeof(XRL.Language.Grammar))]
    [HarmonyPatch("ThirdPerson", new System.Type[] { typeof(string), typeof(bool) })]
    public class Grammar_MakeThirdPerson_Patch
    {
        public static bool Prefix(string word, bool PrependSpace, ref string __result)
        {
            if (string.IsNullOrEmpty(word)) return true;

            if (Regex.IsMatch(word, @"[а-яА-ЯёЁ]"))
            {
                // The game attempts to append 's' or 'es' to verbs for third-person conjugation.
                // For Russian strings, this results in "мяукатьs".
                // We just return the string as-is to avoid English suffixes on Cyrillic words.
                __result = PrependSpace ? " " + word : word;
                return false;
            }

            return true;
        }
    }
}
