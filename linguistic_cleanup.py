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

# 1. Linguistic Fixes in word_dictionary.json
word_fixes = {
    "charge": "заряд",
    "lairs": "логова",
    "lair": "логово",
    "robot": "робот",
    "ray": "луч",
    "Ray": "луч",
    "misses": "промахивается",
    "hits": "бьет",
    "equipped": "экипировано",
    "unequipped": "снято",
    "Weight": "Вес",
    "lbs": "фунтов"
}
update_json('word_dictionary.json', word_fixes)

# 2. Pattern fixes for abilities and attributes
pattern_fixes = {
    # Abilities in UI
    r"^(?<p>[a-z]\)) Charge$": "{p} Рывок",
    r"^(?<p>[a-z]\)) Freezing Ray$": "{p} Замораживающий луч",
    r"^(?<p>[a-z]\)) Flaming Ray$": "{p} Пламенный луч",
    
    # Plural locations
    r"^(?<pref>.*?)robot lairs(?<suff>.*?)$": "{pref}логова роботов{suff}",
    r"^(?<pref>.*?)bear lairs(?<suff>.*?)$": "{pref}логова медведей{suff}",
    r"^(?<pref>.*?)insect lairs(?<suff>.*?)$": "{pref}логова насекомых{suff}",
    r"^(?<pref>.*?)baboon lairs(?<suff>.*?)$": "{pref}логова бабуинов{suff}",
    r"^(?<pref>.*?)ape lairs(?<suff>.*?)$": "{pref}логова обезьян{suff}",
    
    # Attribute descriptions (fixing genders)
    r"^(?<pref>.*?)Ваша (?<attr>Сила|Ловкость|Стойкость|Воля) determines (?<desc>.*?)(?<suff>\.?)$": "{pref}Ваша {attr} определяет {desc}{suff}",
    r"^(?<pref>.*?)Ваш (?<attr>Интеллект|Эго|Показатель брони \(AV\)|Показатель уклонения \(DV\)|Теплостойкость|Сопр\. холоду) determines (?<desc>.*?)(?<suff>\.?)$": "{pref}Ваш {attr} определяет {desc}{suff}",
}
update_patterns('pattern_dictionary.json', pattern_fixes)

# 3. Exact fixes for the attributes in dictionary.json
exact_fixes = {
    "Your Strength determines how much melee damage you do": "Ваша Сила определяет, какой урон вы наносите в ближнем бою",
    "Your Agility determines your accuracy with melee and ranged weapons": "Ваша Ловкость определяет вашу меткость в ближнем и дальнем бою",
    "Your Toughness determines your number of hit points": "Ваша Стойкость определяет количество ваших очков здоровья",
    "Your Willpower determines how well you resist mental": "Ваша Воля определяет, насколько хорошо вы сопротивляетесь ментальным",
    "Freezing Ray": "Замораживающий луч",
    "Flaming Ray": "Пламенный луч",
    "Charge": "Рывок",
    "Your Strength": "Ваша Сила",
    "Your Agility": "Ваша Ловкость",
    "Your Toughness": "Ваша Стойкость",
    "Your Willpower": "Ваша Воля"
}
update_json('dictionary.json', exact_fixes)
update_json('dictionary_master.json', exact_fixes)

print("Linguistic cleanup completed.")
