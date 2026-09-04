using HarmonyLib;
using System;
using System.Text.RegularExpressions;
using XRL.Messages;
using XRL.World;
using XRL.World.Parts;

namespace RussianLocalization
{
    // Глобальный контекст боевки для текущего потока
    public static class CombatContext
    {
        [ThreadStatic] public static GameObject Attacker;
        [ThreadStatic] public static GameObject Defender;
        [ThreadStatic] public static GameObject Weapon;
    }

    // Перехват данных перед расчетом удара/промаха
    [HarmonyPatch(typeof(Combat), "MeleeAttackWithWeaponInternal")]
    public static class Combat_MeleeAttackWithWeaponInternal_Patch
    {
        public static void Prefix(GameObject Attacker, GameObject Defender, GameObject Weapon, out object[] __state)
        {
            // Сохраняем предыдущее состояние (на случай рекурсии/прока)
            __state = new object[] { 
                CombatContext.Attacker, 
                CombatContext.Defender, 
                CombatContext.Weapon 
            };

            CombatContext.Attacker = Attacker;
            CombatContext.Defender = Defender;
            CombatContext.Weapon = Weapon;
        }

        public static void Postfix(object[] __state)
        {
            CombatContext.Attacker = __state[0] as GameObject;
            CombatContext.Defender = __state[1] as GameObject;
            CombatContext.Weapon = __state[2] as GameObject;
        }
    }

    // Перехват отправки сообщений игроку
    [HarmonyPatch(typeof(MessageQueue), "AddPlayerMessage", new Type[] { typeof(string), typeof(char), typeof(bool) })]
    public static class MessageQueue_AddPlayerMessage_Patch
    {
        // Механика ближнего боя с CombatContext
        private static readonly Regex MeleeRegex = new Regex(@"(critically )?(hit|hits|miss|misses).*?(?:\((x\d+)\))?(?: for (\d+) damage)?(?: with .+?)?!\s*(\[.*?\])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // Универсальная регулярка для дальнего боя и мутаций (извлекает субъекта и объекта прямо из текста)
        // Пример: "The musket turret hits you (x1) with a lead slug for 7 damage!"
        private static readonly Regex RangedRegex = new Regex(@"^(.*?)\s+(critically )?(hits?|misses?)(?:\s+([A-Za-z -]+?))?(?:\s+\((x\d+)\))?(?:\s+with\s+((?:(?!for\s+\d+\s+damage).)*))?(?:\s+for\s+(\d+)\s+damage)?(?:\s+with\s+(.*?))?[!.]?\s*(\[.*?\])?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        
        private static readonly Regex ColorRegex = new Regex(@"\{\{(.*?)\|");

        // Единица урона по числу: «1 урон», «2 урона», «5 урона», «11 урона», «21 урон».
        private static string DamageUnit(string damage)
        {
            long n;
            if (!long.TryParse(damage, out n)) return " урона";
            if (n % 10 == 1 && n % 100 != 11) return " урон";
            return " урона";
        }

        // Имена предметов приходят из нескольких источников: иногда уже с
        // частично склонённым модификатором и иногда с дублированным
        // прилагательным. Нельзя отдавать такие строки в общий Decline без
        // нормализации — иначе получаем «зажженнымом» и «стальной стальной».
        private static string PrepareWeaponName(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return source;

            string result = TranslationEngine.Translate(source);
            result = result.Replace("стальной стальной", "стальной");
            result = result.Replace("слизью-запятнанной", "запятнанной слизью");
            result = result.Replace("слизь-запятнанный", "запятнанный слизью");
            result = result.Replace("кровь-запятнанный", "запятнанный кровью");
            result = result.Replace("вода-запятнанный", "запятнанный водой");
            result = result.Replace("зажженнымом", "зажжённым");
            return result;
        }

        public static void Prefix(ref string Message)
        {
            try
            {
                // Старый формат консольных сообщений использует ampersand-цвета
                // (&g, &Y, &C и т. п.). Этот перехват умеет восстановить только
                // один {{color|...}}-тег и при перестроении строки терял границы
                // цветов вокруг прилагательных/оружия. Оставляем такие сообщения
                // основному ScreenBuffer-переводчику, который сохраняет исходную
                // последовательность цветовых маркеров и roll-блоков.
                if (!string.IsNullOrEmpty(Message) && Regex.IsMatch(Message, @"&[A-Za-z]"))
                    return;

                string stripped = ConsoleLib.Console.ColorUtility.StripFormatting(Message);
                if (stripped == null) return;

                Match colorMatch = ColorRegex.Match(Message);
                string colorTag = colorMatch.Success ? "{{" + colorMatch.Groups[1].Value + "|" : "";
                string colorEnd = colorMatch.Success ? "}}" : "";

                // СЦЕНАРИЙ 1: Контекст ближнего боя существует (точнейший перевод)
                if (CombatContext.Attacker != null && CombatContext.Defender != null)
                {
                    Match match = MeleeRegex.Match(stripped);
                    if (match.Success)
                    {
                        bool isCrit = match.Groups[1].Success;
                        string verb = match.Groups[2].Value.ToLower();
                        string penetrations = match.Groups[3].Success ? match.Groups[3].Value : "";
                        string damage = match.Groups[4].Success ? match.Groups[4].Value : "";
                        string roll = match.Groups[5].Value;

                        bool isHit = verb == "hit" || verb == "hits";
                        bool attackerIsPlayer = CombatContext.Attacker.IsPlayer();
                        
                        string attackerName = attackerIsPlayer ? "Вы" : TranslationEngine.Translate(CombatContext.Attacker.DisplayNameOnly);
                        attackerName = MorphologyService.Decline(attackerName, MorphCase.Nom);

                        string defenderName = CombatContext.Defender.IsPlayer() ? "вас" : TranslationEngine.Translate(CombatContext.Defender.DisplayNameOnly);
                        // «попадать по» и «промахиваться по» требуют дательного,
                        // а не винительного: по щелкуну-охотнику.
                        defenderName = MorphologyService.Decline(defenderName, MorphCase.Dat);

                        string weaponName = CombatContext.Weapon != null ? MorphologyService.Decline(PrepareWeaponName(CombatContext.Weapon.DisplayNameOnly), MorphCase.Ins) : "голыми руками";

                        string action = isHit ? (isCrit ? (attackerIsPlayer ? "наносите критический удар по" : "наносит критический удар по") : (attackerIsPlayer ? "попадаете по" : "попадает по")) : (attackerIsPlayer ? "промахиваетесь по" : "промахивается по");

                        System.Text.StringBuilder sb = new System.Text.StringBuilder();
                        sb.Append(colorTag).Append(attackerName).Append(" ").Append(action).Append(" ");
                        sb.Append(CombatContext.Defender.IsPlayer() ? "вас" : defenderName);
                        if (!string.IsNullOrEmpty(penetrations)) sb.Append(" (").Append(penetrations).Append(")");
                        if (!string.IsNullOrEmpty(damage)) sb.Append(" на ").Append(damage).Append(DamageUnit(damage));
                        sb.Append(" ").Append(weaponName).Append("!").Append(colorEnd).Append(" ").Append(roll);
                        Message = sb.ToString();
                        Message = Message.Substring(0, 1).ToUpper() + Message.Substring(1);
                        return;
                    }
                }
                
                // СЦЕНАРИЙ 2: Дальний бой и мутации (парсинг прямо из текста)
                Match rMatch = RangedRegex.Match(stripped);
                if (rMatch.Success)
                {
                    string subjStr = rMatch.Groups[1].Value.Trim();
                    bool isCrit = rMatch.Groups[2].Success;
                    string verb = rMatch.Groups[3].Value.ToLower();
                    string objStr = rMatch.Groups[4].Value.Trim();
                    string penetrations = rMatch.Groups[5].Value.Trim();
                    string wpn1 = rMatch.Groups[6].Value.Trim();
                    string damage = rMatch.Groups[7].Value.Trim();
                    string wpn2 = rMatch.Groups[8].Value.Trim();
                    string roll = rMatch.Groups[9].Value.Trim();

                    // Иногда объект пуст (например, You hit for 5 damage!)
                    if (string.IsNullOrEmpty(objStr))
                    {
                        // В дальнем бое без объекта, объект обычно скрыт в контексте. Для простоты опустим его.
                    }

                    bool attackerIsPlayer = subjStr.Equals("you", StringComparison.OrdinalIgnoreCase);
                    bool isHit = verb.StartsWith("hit");
                    
                    // Переводим субъекта
                    string attackerName = attackerIsPlayer ? "Вы" : TranslationEngine.Translate(subjStr.Replace("The ", "").Replace("the ", ""));
                    attackerName = MorphologyService.Decline(attackerName, MorphCase.Nom);

                    // Переводим объект
                    string defenderName = "";
                    if (!string.IsNullOrEmpty(objStr))
                    {
                        if (objStr.Equals("you", StringComparison.OrdinalIgnoreCase)) defenderName = "вас";
                        else defenderName = MorphologyService.Decline(TranslationEngine.Translate(objStr.Replace("The ", "").Replace("the ", "")), MorphCase.Acc);
                    }

                    // Переводим оружие
                    string weaponStr = !string.IsNullOrEmpty(wpn1) ? wpn1 : wpn2;
                    string weaponName = "";
                    if (!string.IsNullOrEmpty(weaponStr))
                    {
                        weaponStr = weaponStr.Replace("a ", "").Replace("an ", "").Replace("your ", "").Replace("his ", "").Replace("her ", "").Replace("their ", "");
                        weaponName = MorphologyService.Decline(PrepareWeaponName(weaponStr), MorphCase.Ins);
                    }

                    string action = isHit ? (isCrit ? (attackerIsPlayer ? "наносите критический удар" : "наносит критический удар") : (attackerIsPlayer ? "попадаете" : "попадает")) : (attackerIsPlayer ? "промахиваетесь" : "промахивается");
                    if (!string.IsNullOrEmpty(defenderName)) action += " по";

                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    sb.Append(colorTag).Append(attackerName).Append(" ").Append(action);
                    
                    if (!string.IsNullOrEmpty(defenderName)) sb.Append(" ").Append(defenderName);
                    if (!string.IsNullOrEmpty(penetrations)) sb.Append(" (").Append(penetrations).Append(")");
                    if (!string.IsNullOrEmpty(damage)) sb.Append(" на ").Append(damage).Append(DamageUnit(damage));
                    if (!string.IsNullOrEmpty(weaponName)) sb.Append(" ").Append(weaponName);
                    
                    sb.Append("!").Append(colorEnd);
                    if (!string.IsNullOrEmpty(roll)) sb.Append(" ").Append(roll);

                    Message = sb.ToString();
                    Message = Message.Substring(0, 1).ToUpper() + Message.Substring(1);
                    return;
                }

                // Кастомные сообщения
                if (stripped.Equals("You stop moving because something is shooting at you.", StringComparison.OrdinalIgnoreCase))
                {
                    Message = colorTag + "Вы прекращаете движение, потому что в вас кто-то стреляет." + colorEnd;
                }
            }
            catch (Exception ex)
            {
                TranslationEngine.LogInfo("[RussianLocalization] Combat hook failed: " + ex.Message);
            }
        }
    }
}
