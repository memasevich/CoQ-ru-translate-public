using System;
using XRL.World.Text.Attributes;
using XRL.World.Text.Delegates;

namespace RussianLocalization
{
    [HasVariableReplacer]
    public static class RussianDialogueTags
    {
        private static void DeclineContext(DelegateContext Context, MorphCase targetCase)
        {
            if (Context.Value.Length > 0)
            {
                try
                {
                    string raw = Context.Value.ToString();
                    
                    // Переводим сгенерированное английское слово на русский
                    string translated = TranslationEngine.Translate(raw);
                    
                    // Если перевода нет, используем оригинал
                    if (string.IsNullOrEmpty(translated)) 
                    {
                        translated = raw;
                    }

                    // Склоняем
                    string declined = MorphologyService.Decline(translated, targetCase);

                    // Записываем результат обратно
                    Context.Value.Clear();
                    Context.Value.Append(declined);
                }
                catch (Exception ex)
                {
                    TranslationEngine.LogInfo("[RussianLocalization] Failed to process dialogue tag: " + ex.Message);
                }
            }
        }

        [VariablePostProcessor("nom")]
        public static void Nominative(DelegateContext Context) => DeclineContext(Context, MorphCase.Nom);

        [VariablePostProcessor("gen")]
        public static void Genitive(DelegateContext Context) => DeclineContext(Context, MorphCase.Gen);

        [VariablePostProcessor("dat")]
        public static void Dative(DelegateContext Context) => DeclineContext(Context, MorphCase.Dat);

        [VariablePostProcessor("acc")]
        public static void Accusative(DelegateContext Context) => DeclineContext(Context, MorphCase.Acc);

        [VariablePostProcessor("ins")]
        public static void Instrumental(DelegateContext Context) => DeclineContext(Context, MorphCase.Ins);

        [VariablePostProcessor("prep")]
        public static void Prepositional(DelegateContext Context) => DeclineContext(Context, MorphCase.Prep);
    }
}
