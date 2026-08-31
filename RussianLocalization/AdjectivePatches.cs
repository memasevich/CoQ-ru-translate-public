using System;
using System.Collections.Generic;
using HarmonyLib;
using XRL.World;

namespace RussianLocalization
{
    [HarmonyPatch(typeof(DescriptionBuilder), "ToString")]
    public static class DescriptionBuilder_ToString_Patch
    {
        public static void Prefix(DescriptionBuilder __instance)
        {
            // Если компонентов мало или нет базового слова - выходим
            if (__instance.Count <= 1 || string.IsNullOrEmpty(__instance.PrimaryBase))
                return;

            try
            {
                // Сначала переводим базовое существительное
                string primaryBase = __instance.PrimaryBase;
                string translatedBase = TranslationEngine.TranslateText(primaryBase, true);
                if (string.IsNullOrEmpty(translatedBase)) translatedBase = primaryBase;
                
                string strippedBase = ConsoleLib.Console.ColorUtility.StripFormatting(translatedBase);

                // Узнаем род существительного (Masc, Fem, Neut)
                MorphGender baseGender = MorphologyService.DetectGenderForWord(strippedBase);

                // Сохраняем текущие элементы, так как нельзя менять словарь во время итерации
                Dictionary<string, int> currentEntries = new Dictionary<string, int>();
                foreach (var kvp in __instance)
                {
                    currentEntries[kvp.Key] = kvp.Value;
                }

                // Очищаем оригинальный билдер
                __instance.Clear();
                
                foreach (var kvp in currentEntries)
                {
                    string key = kvp.Key;
                    int order = kvp.Value;

                    // Переводим компонент
                    string translatedKey = TranslationEngine.TranslateText(key, true);
                    if (string.IsNullOrEmpty(translatedKey)) translatedKey = key;

                    // Если это прилагательное (например, order = -500)
                    // Мы применяем правило для всех модификаторов, идущих ДО существительного (order < 10)
                    if (order < 10 && key != primaryBase)
                    {
                        string stripped = ConsoleLib.Console.ColorUtility.StripFormatting(translatedKey);
                        
                        // Склоняем прилагательное по роду базы
                        string declined = MorphologyService.ForceDeclineAdjective(stripped, baseGender, MorphCase.Nom, MorphNumber.Singular);
                        
                        // Если склонение изменило слово, заменяем его в оригинальной строке (чтобы сохранить цвета {{y|...}})
                        if (stripped != declined && !string.IsNullOrEmpty(stripped))
                        {
                            translatedKey = translatedKey.Replace(stripped, declined);
                        }
                    }

                    // Добавляем обратно в билдер
                    __instance.Add(translatedKey, order);
                    
                    // Восстанавливаем ссылку на PrimaryBase, если это была она
                    if (key == primaryBase)
                    {
                        __instance.PrimaryBase = translatedKey;
                    }
                }
            }
            catch (Exception ex)
            {
                TranslationEngine.LogError("[AdjectivePatches] Error: " + ex.Message);
            }
        }
    }
}
