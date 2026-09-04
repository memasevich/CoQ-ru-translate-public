using System;
using System.Collections.Generic;
using XRL.World;
using XRL.World.Text.Delegates;
using XRL.World.Text.Attributes;

namespace RussianLocalization
{
    public class RussianVariableReplacer
    {
        private static string GetGender(DelegateContext Context)
        {
            if (Context.Target != null)
            {
                var pronounProvider = Context.Target.GetPronounProvider();
                if (pronounProvider != null)
                {
                    return pronounProvider.Name.ToLowerInvariant(); 
                }
            }
            return "neuter";
        }

        [VariableReplacer(new string[] { "ru:pronoun.nom", "ru:pronouns.subjective" })]
        public static string RuPronounNom(DelegateContext Context)
        {
            string gender = GetGender(Context);
            string pronoun = "оно";
            
            if (gender == "male") pronoun = "он";
            else if (gender == "female") pronoun = "она";
            else if (gender == "plural") pronoun = "они";

            return Context.Capitalize ? char.ToUpper(pronoun[0]) + pronoun.Substring(1) : pronoun;
        }

        [VariableReplacer(new string[] { "ru:pronoun.acc", "ru:pronouns.objective" })]
        public static string RuPronounAcc(DelegateContext Context)
        {
            string gender = GetGender(Context);
            string pronoun = "его";
            
            if (gender == "male") pronoun = "его";
            else if (gender == "female") pronoun = "ее";
            else if (gender == "plural") pronoun = "их";
            
            return Context.Capitalize ? char.ToUpper(pronoun[0]) + pronoun.Substring(1) : pronoun;
        }

        [VariableReplacer(new string[] { "ru:pronoun.gen", "ru:pronoun.possessive" })]
        public static string RuPronounGen(DelegateContext Context)
        {
            string gender = GetGender(Context);
            string pronoun = "его";
            
            if (gender == "male") pronoun = "его";
            else if (gender == "female") pronoun = "ее";
            else if (gender == "plural") pronoun = "их";
            
            return Context.Capitalize ? char.ToUpper(pronoun[0]) + pronoun.Substring(1) : pronoun;
        }

        [VariableReplacer(new string[] { "ru:verb" })]
        public static string RuVerb(DelegateContext Context)
        {
            if (Context.Parameters == null || Context.Parameters.Count == 0)
                return "ГЛАГОЛ_ОШИБКА";

            string verb = Context.Parameters[0];
            string gender = GetGender(Context);
            bool isPlural = (gender == "plural");
            
            string conjugated = ConjugateVerb(verb, isPlural);
            return Context.Capitalize ? char.ToUpper(conjugated[0]) + conjugated.Substring(1) : conjugated;
        }

        private static string ConjugateVerb(string infinitive, bool isPlural)
        {
            // Simple rule-based conjugator for 3rd person present tense
            if (string.IsNullOrEmpty(infinitive)) return infinitive;

            // Handle exceptions/irregular verbs that might be common in descriptions
            if (infinitive == "быть") return isPlural ? "суть" : "есть"; 
            if (infinitive == "чуять") return isPlural ? "чуют" : "чует";
            if (infinitive == "висеть") return isPlural ? "висят" : "висит";
            if (infinitive == "стоять") return isPlural ? "стоят" : "стоит";
            if (infinitive == "сидеть") return isPlural ? "сидят" : "сидит";
            if (infinitive == "лежать") return isPlural ? "лежат" : "лежит";
            if (infinitive == "жить") return isPlural ? "живут" : "живет";

            if (infinitive.EndsWith("ть"))
            {
                if (infinitive.EndsWith("ать") || infinitive.EndsWith("ять"))
                {
                    // 1st conjugation mostly: делать -> делает / делают
                    // Remove "ть"
                    string stem = infinitive.Substring(0, infinitive.Length - 2);
                    return stem + (isPlural ? "ют" : "ет");
                }
                else if (infinitive.EndsWith("ить") || infinitive.EndsWith("еть"))
                {
                    // 2nd conjugation mostly: строить -> строит / строят
                    string stem = infinitive.Substring(0, infinitive.Length - 3);
                    return stem + (isPlural ? "ят" : "ит");
                }
            }
            
            // Fallback: just return infinitive if rules fail
            return infinitive;
        }
    }
}
