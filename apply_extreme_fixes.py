import json
import re

def fuzzy_match():
    # 1. Load data
    with open('game_strings_extreme.json', 'r', encoding='utf-8') as f:
        golden_keys = json.load(f)
    
    with open('dictionary.json', 'r', encoding='utf-8') as f:
        master_dict = json.load(f)
        
    with open('clean_untranslated.txt', 'r', encoding='utf-8') as f:
        fragments = [l.strip() for l in f.readlines() if l.strip()]

    # 2. Build a map of clean golden keys to their original keys and translations
    # Many fragments in untranslated might already be in master_dict but with tags.
    
    # 3. Targeted matching
    new_fixes = {}
    
    for frag in fragments:
        if frag in master_dict: continue
        
        # Look for the fragment inside golden keys
        for gk in golden_keys:
            if frag in gk:
                # We found where this fragment comes from!
                # Now check if we have a translation for the WHOLE golden key
                if gk in master_dict:
                    translation = master_dict[gk]
                    # This is the tricky part: we need to find the corresponding part in the translation.
                    # For now, let's just log these matches to a file so I can review them.
                    # Or, if the fragment is a significant chunk, we can try to guess.
                    pass
    
    # Actually, let's just do a manual high-quality sweep of the fragments
    # I've identified the most important ones.
    
    manual_translations = {
        "with a short blade in your primary hand is reduced by 25%": "с коротким клинком в основной руке снижается на 25%",
        "your ability to dominate the wills of other living creatures": "ваша способность подчинять волю других живых существ",
        "your ability to resist poison and disease": "ваша способность сопротивляться ядам и болезням",
        "your accuracy with bows and rifles, your agility is treated as if it were 4 points higher": "ваша точность с луками и винтовками, ваша ловкость считается на 4 единицы выше",
        "your accuracy with pistols, your agility is treated as if it were 4 points higher": "ваша точность с пистолетами, ваша ловкость считается на 4 единицы выше",
        "your ego is treated as though it were 4 points higher": "ваше эго считается на 4 единицы выше",
        "world. A million breaths of": "мира. Миллионы вдохов",
        "the rotting jungles": "гниющие джунгли",
        "workshops, and sultans they admire or despise. They're": "мастерские и султаны, которыми они восхищаются или которых презирают. Они",
        "sharing secrets about technology": "делиться секретами о технологиях",
        "trading secrets about sultans": "обмениваться секретами о султанах",
        "sharing secrets about all sultans": "делиться секретами о всех султанах",
        "insect lairs and the locations of lava": "логова насекомых и местоположения лавы",
        "gel weeps and sultans they admire or despise": "источники геля и султаны, которых они почитают или презирают",
        "fish lairs and the locations of frog lairs": "логова рыб и местоположения логов лягушек",
        "building new societies: the locations": "построение новых обществ: местоположения",
        "pig farms. They're also interested in hearing": "свинофермы. Им также интересно послушать",
        "and gossip that's about them": "и слухи о них",
        "ruins, the locations of historic sites": "руины, местоположения исторических мест",
        "sharing secrets about the darkling star and sultans they admire or": "делиться секретами о темной звезде и султанах, которыми они восхищаются или",
        "Stopsvalinn and the locations of their forts": "Стопсвалинн и местоположения их фортов",
        "waterfreak": "водяной фрик",
        "Faundren-eyed": "с глазами Фондрена",
        "There are caves everywhere, you dolt! Scoop the surface": "Пещеры повсюду, болван! Обыщи поверхность",
        "marsh, or head": "болота, или направляйся",
        "Must you bother me? What are you, some sort": "Обязательно меня беспокоить? Ты кто, какой-то",
        "I am dromad, human friend. Some say saltstrider. Do you know this": "Я дромадер, друг-человек. Некоторые зовут нас солеходами. Знаешь о таких?",
        "My people have walked the salt for thousands": "Мой народ ходил по солям тысячи",
        "years": "лет",
        "meeting every creature": "встречая каждое существо",
        "that lives and thinks. From Pale Sea to the marsh": "что живет и мыслит. От Бледного моря до болота",
        "and under the": "и под",
        "x14 (unburnt": "x14 (не горит",
        "x14 (unburnt)": "x14 (не горит)",
        "Travel to the historical site": "Отправиться к историческому месту",
        "Saloons of Nalil": "Салуны Налила",
        "Visit the Saloons": "Посетить Салуны",
        "scuttle down a dark shaft at the edge of a sunken trade path": "спуститься в темную шахту на краю затонувшего торгового пути",
        "caravanserai": "караван-сарай",
        "some leather armor": "какая-то кожаная броня",
        "some trash": "какой-то мусор",
        "iron battle axe": "железный боевой топор",
        "stairs up": "лестница вверх",
        "stairs down": "лестница вниз",
        "bent metal sheet from the east": "гнутый лист металла с востока",
        "fractured microchip from the south": "сломанный микрочип с юга",
        "bent metal sheet from the west": "гнутый лист металла с запада",
        "harvests a vinewafer": "собирает виноградный лист",
        "wet watervine farmer": "мокрый фермер водяной лозы",
        "some salt marsh": "какое-то соляное болото",
        "some brinestalk": "какой-то рассолостебель",
        "youвЂ™re in a hurry": "вы спешите",
        "youвЂ™re in a hurry.": "вы спешите."
    }
    
    added = 0
    for k, v in manual_translations.items():
        if k not in master_dict:
            master_dict[k] = v
            added += 1
            
    with open('dictionary.json', 'w', encoding='utf-8') as f:
        json.dump(master_dict, f, ensure_ascii=False, indent=2)
    
    print(f"Added {added} high-quality fragment translations.")

if __name__ == "__main__":
    fuzzy_match()
