import json
import os

def update_json(path, updates):
    if not os.path.exists(path): return
    with open(path, 'r', encoding='utf-8') as f:
        data = json.load(f)
    for k, v in updates.items():
        data[k] = v
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)

def update_patterns(path, new_patterns):
    if not os.path.exists(path): return
    with open(path, 'r', encoding='utf-8') as f:
        data = json.load(f)
    for k, v in new_patterns.items():
        data[k] = v
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)

# 1. Linguistic Fixes in word_dictionary.json (Removing contextual collisions)
word_fixes = {
    "charge": "Рывок", # Changed from 'заряда'
    "Charge": "Рывок",
    "ray": "луч",
    "Ray": "луч",
    "Edition": "издание",
    "Dromad": "Дромадер",
    "of": "из",
    "corpses": "трупов",
    "Corpses": "Трупов",
    "Butcher": "Разделать",
    "butcher": "разделать",
    "Bleed": "Кровотечение",
    "save": "спасбросок",
    "spaces": "клеток",
    "lbs": "фунтов",
    "locations": "местоположения",
    "location": "местоположение",
    "cybernetics": "кибернетика",
    "settlements": "поселения",
    "members": "члены",
    "Options": "Настройки",
    "options": "настройки",
    "advanced": "расширенные",
    "Warden": "Страж",
    "Elder": "Старейшина",
    "trinket": "безделушка",
    "trinkets": "безделушки",
    "plant": "растение",
    "Farmer": "Фермер",
    "farmer": "фермер",
    "Quest": "Квест",
    "quest": "квест"
}
update_json('word_dictionary.json', word_fixes)

# 2. Specific Patterns for cases seen in word_replacements.txt
pattern_fixes = {
    # UI Elements with letters
    r"^(?<p>[a-z]\)) Butcher Corpses$": "{p} Разделать трупы",
    r"^(?<p>[a-z]\)) Sprint$": "{p} Спринт",
    
    # Phrases from Joppa and world
    r"^(?<pref>.*?)Dromad Edition(?<suff>.*?)$": "{pref}Издание Дромадеров{suff}",
    r"^(?<pref>.*?)Caves of Qud(?<suff>.*?)$": "{pref}Пещеры Куда{suff}",
    r"^(?<pref>.*?)ACTIVE EFFECTS(?<suff>.*?)$": "{pref}АКТИВНЫЕ ЭФФЕКТЫ{suff}",
    
    # Log entries from word_replacements
    r"^(?<pref>.*?)Bleed save: Toughness (?<val>\d+)(?<suff>.*?)$": "{pref}Спасбросок от кровотечения: Стойкость {val}{suff}",
    r"^(?<pref>.*?)Bleed damage: (?<val>.*?) per round(?<suff>.*?)$": "{pref}Урон от кровотечения: {val} за раунд{suff}",
    r"^(?<pref>.*?)Range: (?<val>.*?) spaces(?<suff>.*?)$": "{pref}Дальность: {val} кл.{suff}",
    
    # Faction details
    r"^(?<pref>.*?)are interested in sharing secrets about the locations(?<suff>.*?)$": "{pref}заинтересованы в обмене секретами о расположении{suff}",
    r"^(?<pref>.*?)interested in trading secrets about(?<suff>.*?)$": "{pref}заинтересованы в торговле секретами о{suff}",
}
update_patterns('pattern_dictionary.json', pattern_fixes)

# 3. Attributes cleanup in main dictionary
exact_fixes = {
    "Strength Bonus Cap: 2": "Предел бонуса силы: 2",
    "Offhand Attack Chance: 15%": "Шанс атаки второй рукой: 15%",
    "Physical Defect": "Физический дефект",
    "Mental Mutations": "Ментальные мутации",
    "Physical Mutations": "Физические мутации",
    "Weight: 1 lbs": "Вес: 1 фунт",
    "Weight: 5 lbs": "Вес: 5 фунтов",
    "b) Freezing Ray": "b) Замораживающий луч",
    "e) Charge": "e) Рывок",
    "c) Make Camp": "c) Разбить лагерь"
}
update_json('dictionary.json', exact_fixes)
update_json('dictionary_master.json', exact_fixes)

print("Linguistic audit and cleanup completed.")
