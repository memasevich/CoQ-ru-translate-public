import json
import re

def process_untranslated():
    # Load dictionaries
    with open('dictionary.json', 'r', encoding='utf-8') as f:
        d = json.load(f)
    
    with open('untranslated.txt', 'r', encoding='utf-8-sig', errors='replace') as f:
        lines = [l.strip() for l in f.readlines() if l.strip()]

    # Deduplicate and count frequency
    counts = {}
    for l in lines:
        counts[l] = counts.get(l, 0) + 1
    
    # Sort by frequency
    sorted_lines = sorted(counts.items(), key=lambda x: x[1], reverse=True)
    
    # Fragments to translate (Manual high-priority from the log)
    fixes = {
        # Combat
        "for 1 damage with your": "на 1 урона вашим",
        "for 2 damage with your": "на 2 урона вашим",
        "for 3 damage with your": "на 3 урона вашим",
        "for 4 damage with her bite. [18": "на 4 урона её укусом. [18",
        "The snapjaw scavenger dies": "Падальщик снапджавов умирает",
        "The bear dies": "Медведь умирает",
        "The bear hits": "Медведь попадает",
        "You miss with your": "Вы промахнулись вашим",
        "dagger! [18": "кинжалом! [18",
        "dagger! [14": "кинжалом! [14",
        "dagger! [19": "кинжалом! [19",
        "bite! [7 vs 7": "укусом! [7 против 7",
        "bite! [2 vs 7": "укусом! [2 против 7",
        
        # Interactions
        "You stop moving because there is a bear in your way": "Вы остановились, так как на вашем пути медведь",
        "You identification the odd trinket as a": "Вы опознали странную безделушку как",
        "You pass by a": "Вы проходите мимо",
        "an iron battle axe": "железного боевого топора",
        "waterskin from the north": "бурдюк с севера",
        "from the north.": "с севера.",
        
        # Joppa Dialogues (Fragments)
        "this? The oasis-hamlet. 'Neath the shelf": "это? Оазис-деревня. Под уступом",
        "world. A million breaths of": "мира. Миллионы вдохов",
        "the rotting jungles": "гниющие джунгли",
        "rotting jungles": "гниющие джунгли",
        "o' there, by the southern watervine patch": "вон там, у южной грядки водяной лозы",
        "o' there, by the southern watervine patch.": "вон там, у южной грядки водяной лозы.",
        "between, heh. Go through his hut": "между ними, хех. Ступай в его хижину",
        "sheet": "листового",
        "metal, to the southwest": "металла, на юго-запад",
        "Must you bother me? What are you, some sort": "Обязательно меня беспокоить? Ты что, какой-то",
        "Accept Quest": "Принять квест",
        "Fetch Argyve a": "Принести Арживу",
        "Knickknack": "Безделушку",
        
        # Secrets/Interests
        "sharing secrets about technology": "делиться секретами о технологиях",
        "trading secrets about sultans": "обмениваться секретами о султанах",
        "sharing secrets about all sultans": "делиться секретами о всех султанах",
        "pig farms, the locations": "свинофермы, местоположения",
        "and all sultans": "и все султаны",
        "building new societies: the locations": "построение новых обществ: местоположения",
        "insect lairs and the locations of lava": "логова насекомых и местоположения лавы",
        "gel weeps and sultans they admire or despise": "источники геля и султаны, которых они почитают или презирают",
        
        # World
        "Salt marsh, surface, Harvest Dawn": "Соляное болото, поверхность, Рассвет Урожая",
        "Qud, surface, Harvest Dawn": "Куд, поверхность, Рассвет Урожая",
        "Joppa, Beetle Moon Zenith": "Джоппа, Зенит Луны Жука",
        "bent metal sheet from the east": "гнутый лист металла с востока",
        "fractured microchip from the south": "сломанный микрочип с юга",
        "bent metal sheet from the west": "гнутый лист металла с запада",
        "harvests a vinewafer": "собирает виноградный лист",
        "wet watervine farmer": "мокрый фермер водяной лозы",
        "some salt marsh": "какое-то соляное болото",
        "some brinestalk": "какой-то рассолостебель"
    }

    # Add to dictionary
    added = 0
    for k, v in fixes.items():
        if k not in d or d[k] != v:
            d[k] = v
            added += 1
    
    with open('dictionary.json', 'w', encoding='utf-8') as f:
        json.dump(d, f, ensure_ascii=False, indent=2)
    
    print(f"Added {added} targeted fragment fixes to dictionary.json")

if __name__ == "__main__":
    process_untranslated()
