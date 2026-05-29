import json
import re

def auto_translate_xml_strings():
    with open('game_strings_extreme.json', 'r', encoding='utf-8') as f:
        xml_strings = json.load(f)
    
    with open('dictionary.json', 'r', encoding='utf-8') as f:
        d = json.load(f)

    # Common terms and their translations for auto-mapping
    terms = {
        "Short Blade": "Короткий клинок",
        "Long Blade": "Длинный клинок",
        "Axe": "Топор",
        "Mace": "Булава",
        "Dagger": "Кинжал",
        "Bow": "Лук",
        "Rifle": "Винтовка",
        "Pistol": "Пистолет",
        "Armor": "Броня",
        "Shield": "Щит",
        "Helmet": "Шлем",
        "Gloves": "Перчатки",
        "Boots": "Сапоги",
        "Backpack": "Рюкзак",
        "Torch": "Факел",
        "Waterskin": "Бурдюк",
        "Bandage": "Бинт",
        "Ration": "Паек",
        "Corpse": "Труп",
        "Snapjaw": "Снапджав",
        "Bear": "Медведь",
        "Crocodile": "Крокодил",
        "Beetle": "Жук",
        "Spider": "Паук",
        "Merchant": "Торговец",
        "Farmer": "Фермер",
        "Elder": "Старейшина",
        "Warden": "Страж",
        "Joppa": "Джоппа",
        "Qud": "Куд",
        "Salt Marsh": "Соляное болото",
        "Desert": "Пустыня",
        "Jungle": "Джунгли",
        "Ruins": "Руины",
        "Canyon": "Каньон",
        "Cave": "Пещера",
        "Historical Site": "Историческое место",
        "Sultan": "Султан",
        "Quest": "Квест",
        "Artifact": "Артефакт",
        "Trinket": "Безделушка",
        "Scrap": "Лом",
        "Energy Cell": "Энергоячейка",
        "Grenade": "Граната",
        "Liquid": "Жидкость",
        "Freshwater": "Пресная вода",
        "Blood": "Кровь",
        "Wine": "Вино",
        "Honey": "Мед",
        "Cider": "Сидр",
        "Oil": "Масло",
        "Acid": "Кислота",
        "Lava": "Лава"
    }

    # Only add if the string IS the term or very similar
    added = 0
    for s in xml_strings:
        if s in d: continue
        
        # Simple heuristic: if it's exactly a term
        if s in terms:
            d[s] = terms[s]
            added += 1
            continue
            
        # If it's "Some [Term]"
        for eng, rus in terms.items():
            if s == f"some {eng.lower()}":
                d[s] = f"какой-то {rus.lower()}"
                added += 1
                break
            if s == f"a {eng.lower()}":
                d[s] = f"{rus.lower()}"
                added += 1
                break

    with open('dictionary.json', 'w', encoding='utf-8') as f:
        json.dump(d, f, ensure_ascii=False, indent=2)
    
    print(f"Auto-mapped {added} terms from XML to dictionary.")

if __name__ == "__main__":
    auto_translate_xml_strings()
