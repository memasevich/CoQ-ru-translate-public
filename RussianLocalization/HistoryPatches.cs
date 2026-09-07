using HarmonyLib;
using HistoryKit;
using System;

namespace RussianLocalization
{
    [HarmonyPatch(typeof(HistoricStringExpander), "ExpandQuery")]
    public static class HistoricStringExpander_ExpandQuery_Patch
    {
        public static void Prefix(ref string query, out MorphCase? __state)
        {
            __state = null;
            if (string.IsNullOrEmpty(query)) return;

            string inner = query;
            bool hasBrackets = inner.Length > 0 && inner[0] == '<';
            if (hasBrackets)
            {
                inner = inner.Substring(1, inner.Length - 2);
            }

            if (inner.EndsWith(".nom", StringComparison.OrdinalIgnoreCase))
            {
                __state = MorphCase.Nom;
                inner = inner.Substring(0, inner.Length - 4);
            }
            else if (inner.EndsWith(".gen", StringComparison.OrdinalIgnoreCase))
            {
                __state = MorphCase.Gen;
                inner = inner.Substring(0, inner.Length - 4);
            }
            else if (inner.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
            {
                __state = MorphCase.Dat;
                inner = inner.Substring(0, inner.Length - 4);
            }
            else if (inner.EndsWith(".acc", StringComparison.OrdinalIgnoreCase))
            {
                __state = MorphCase.Acc;
                inner = inner.Substring(0, inner.Length - 4);
            }
            else if (inner.EndsWith(".ins", StringComparison.OrdinalIgnoreCase))
            {
                __state = MorphCase.Ins;
                inner = inner.Substring(0, inner.Length - 4);
            }
            else if (inner.EndsWith(".prep", StringComparison.OrdinalIgnoreCase))
            {
                __state = MorphCase.Prep;
                inner = inner.Substring(0, inner.Length - 5);
            }

            // Restore brackets if they existed so the original method processes it normally
            if (__state.HasValue)
            {
                if (hasBrackets)
                {
                    query = "<" + inner + ">";
                }
                else
                {
                    query = inner;
                }
            }
        }

        public static void Postfix(ref string __result, MorphCase? __state)
        {
            if (__state.HasValue && !string.IsNullOrEmpty(__result))
            {
                // Сначала переводим сгенерированное английское слово на русский
                string translated = TranslationEngine.Translate(__result);
                
                // Если не нашли перевод, пробуем склонять оригинал (авось уже по-русски)
                if (string.IsNullOrEmpty(translated)) 
                {
                    translated = __result;
                }

                // Затем склоняем в запрошенный падеж
                try
                {
                    __result = MorphologyService.Decline(translated, __state.Value);
                }
                catch (Exception ex)
                {
                    TranslationEngine.LogInfo("[RussianLocalization] Failed to decline history tag: " + ex.Message);
                }
            }
        }
    }
}
