import json
import re

def mass_fix_everything():
    # 1. Load Dictionaries
    with open('dictionary.json', 'r', encoding='utf-8') as f:
        d = json.load(f)
    with open('pattern_dictionary.json', 'r', encoding='utf-8') as f:
        p = json.load(f)
    with open('word_dictionary.json', 'r', encoding='utf-8') as f:
        wd = json.load(f)

    # --- PART 1: MASS DICTIONARY UPDATES (The Fragments from Logs) ---
    fragments = {
        "The shrine depicts a significant event from the": "Эта святыня изображает важное событие из",
        "ancient sultan": "древнего султана",
        "life of the": "жизни",
        "cleansed the marshlands of the plagues of the Gyre": "очистил болотные земли от болезней Вихря",
        "taught Abram to sow": "научил Абрама сеять",
        "along its fertile tracks": "вдоль его плодородных путей",
        "You note this piece information": "Вы записали эти сведения",
        "section of your journal": "раздел вашего журнала",
        "lying on a bed": "спит в кровати",
        "a floor cushion": "напольная подушка",
        "a dogthorn tree": "дерево собачьего шипа",
        "a bizarre contraption": "причудливое устройство",
        "exceeding mass thresholds, perhaps a ganglionic teleprojector": "превышение порогов массы, возможно, ганглиозный телепроектор",
        "the p-density": "p-плотность",
        "must you bother me? What are you, some sort": "обязательно меня беспокоить? Ты кто, какой-то",
        "waterfreak": "водяной фрик",
        "Faundren-eyed": "с глазами Фондрена",
        "There are caves everywhere, you dolt! Scoop the surface": "Пещеры повсюду, болван! Обыщи поверхность",
        "marsh, or head": "болота, или направляйся",
        "Elder will not forget to pay you later, water-dweller": "Старейшина не забудет заплатить тебе позже, водный житель",
        "You identification the odd trinket as a": "Вы опознали странную безделушку как",
        "an iron battle axe": "железный боевый топор",
        "bent metal sheet from the": "гнутый лист металла с",
        "fractured microchip from the": "сломанный микрочип с",
        "wet watervine farmer": "мокрый фермер водяной лозы",
        "harvests a vinewafer": "собирает виноградный лист",
        "The clockwork beetle and pariah to its people takes the": "Часовой жук и изгой своего народа берет",
        "You toss some stamped fish meat, a sprinkle": "Вы бросаете немного вяленого мяса рыбы, щепотку",
        "a dash of crowned fulcrete flake, and a crust of bread into a pot and stir": "порцию корончатых фулькритовых хлопьев и корку хлеба в котел и мешаете",
        "You miss with your": "Вы промахнулись вашим",
        "bite!": "укус!",
        "dagger!": "кинжал!",
        "for 1 damage": "на 1 урона",
        "for 2 damage": "на 2 урона",
        "for 3 damage": "на 3 урона",
        "for 4 damage": "на 4 урона",
        "The snapjaw scavenger dies": "Падальщик снапджавов умирает",
        "The bear dies": "Медведь умирает",
        "The bear hits": "Медведь попадает",
        "You note the location": "Вы отметили местоположение",
        "Sultan Histories": "История Султанов",
        "Locations > Historic Sites": "Локации > Исторические места",
        "Beetle Moon Zenith": "Зенит Луны Жука",
        "Waning Beetle Moon": "Убывающая Луна Жука",
        "Harvest Dawn": "Рассвет Урожая",
        "Salt marsh, surface": "Соляное болото, поверхность",
        "Qud, surface": "Куд, поверхность",
        "Joppa": "Джоппа",
        "Red Rock": "Красная Скала"
    }
    d.update(fragments)

    # --- PART 2: POWERFUL PATTERNS (The "Brains" Upgrade) ---
    # We add regex that catch the fragments and dynamic parts
    patterns = {
        # Combat Logs with any damage and weapon
        r"(?i)The (?<name>.+?) hits (?<target>.+?) for (?<dmg>\d+) damage with (?:her|his|its|their) (?<weapon>.+?)!": "{name} наносит {dmg} ед. урона ({target}) своим {weapon}!",
        r"(?i)You hit (?<target>.+?) for (?<dmg>\d+) damage with your (?<weapon>.+?)!": "Вы наносите {dmg} ед. урона ({target}) вашим {weapon}!",
        r"(?i)with (?:your|her|his|its|their) (?<weapon>.+?)! \[(?<roll>\d+.*?)\]": "своим {weapon}! [{roll}]",
        r"(?i)for (?<dmg>\d+) damage with (?:your|her|his|its|their) (?<weapon>.+?)\. \[(?<roll>\d+.*?)\]": "на {dmg} ед. урона ({weapon}). [{roll}]",
        
        # Interaction messages
        r"(?i)You pass by (?:some|a|an)? (?<item>.+?)\.": "Вы проходите мимо: {item}.",
        r"(?i)You take (?:the )?(?<item>.+?) from the (?<dir>.+?)\.": "Вы берете {item} с {dir}а.",
        r"(?i)A stone crack is revealed to the (?<dir>.+?)!": "На {dir}е открылась каменная трещина!",
        
        # UI & Item Lists
        r"(?i)^(?<item>.+?) x(?<count>\d+)$": "{item} x{count}",
        r"(?i)^(?<item>.+?) \((?<state>.+?)\)$": "{item} ({state})",
        r"(?i)^(?<val>\d+)/(?<max>\d+) lbs\.$": "{val}/{max} фунт.",
        
        # Shrine templates
        r"(?i)The shrine depicts a significant event from the life of the ancient sultan (?<name>.+?):": "Эта святыня изображает важное событие из жизни древнего султана {name}:",
        r"(?i)In (?<year>.+?), (?<name>.+?) (?<action>.+?) and (?<action2>.+?)\.": "В {year} году {name} {action} и {action2}.",
        
        # Journal
        r"(?i)You note this piece information in the (?<section>.+?) section of your journal\.": "Вы записали эти сведения в раздел «{section}» вашего журнала.",
        r"(?i)You note the location (?<loc>.+?) in the (?<section>.+?) section of your journal\.": "Вы отметили местоположение {loc} в разделе «{section}» вашего журнала."
    }
    p.update(patterns)

    # --- PART 3: WORD DICTIONARY PURGE (Anti-Frankenstein) ---
    # Remove glue words that are definitely in the logs causing mess
    bad_glue = [
        "the", "a", "an", "and", "of", "in", "to", "for", "with", "from", "on", "at", "by", "is", "are", 
        "it", "this", "that", "these", "those", "my", "your", "his", "her", "its", "our", "their",
        "some", "any", "all", "but", "or", "so", "if", "then", "else", "when", "where", "why", "how",
        "and", "и", "для", "of the", "to the", "in the"
    ]
    for w in bad_glue:
        if w in wd: del wd[w]
        if w.capitalize() in wd: del wd[w.capitalize()]
    
    # Remove any word <= 2 chars
    keys_to_del = [k for k in wd if len(k) <= 2]
    for k in keys_to_del:
        if k in wd: del wd[k]

    # --- SAVE EVERYTHING ---
    with open('dictionary.json', 'w', encoding='utf-8') as f:
        json.dump(d, f, ensure_ascii=False, indent=2)
    with open('pattern_dictionary.json', 'w', encoding='utf-8') as f:
        json.dump(p, f, ensure_ascii=False, indent=2)
    with open('word_dictionary.json', 'w', encoding='utf-8') as f:
        json.dump(wd, f, ensure_ascii=False, indent=2)

    print("MISSION COMPLETE: Applied massive localization patch based on 6600 lines of log.")

if __name__ == "__main__":
    mass_fix_everything()
