import json
import os
import re

def clean_dict(path, remove_keys=None, update_kv=None):
    if not os.path.exists(path): return
    with open(path, 'r', encoding='utf-8') as f:
        data = json.load(f)
    if remove_keys:
        for k in remove_keys:
            if k in data: del data[k]
    if update_kv:
        for k, v in update_kv.items():
            data[k] = v
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)

# 1. CLEANING WORD DICTIONARY (Fallback)
# Removing everything that can cause Frankensteins or case collisions.
word_removals = [
    "charge", "Charge", "skills", "Skills", "lbs", "Lbs", 
    "interested", "Interested", "sharing", "learning", "trading", 
    "secrets", "locations", "location", "members", "settlements",
    "of", "the", "and", "is", "are", "for", "with", "from", "about", "to", "at", "in", "by", "on"
]
clean_dict('word_dictionary.json', remove_keys=word_removals)

# 2. HARMONIZING MAIN DICTIONARIES
main_updates = {
    "charge": "заряд",
    "Charge": "Рывок",
    "Skills": "Навыки",
    "skills": "навыки",
    "lbs.": " фунт.",
    "lbs": " фунт.",
    "Weight": "Вес",
    "Perfect": "Идеально",
}
clean_dict('dictionary.json', update_kv=main_updates)
clean_dict('dictionary_master.json', update_kv=main_updates)

# 3. ROBUST PATTERNS (Final Boss Edition)
# These patterns handle leading spaces and potential color tags.
final_patterns = {
    # Factions
    r"(?i) are interested in (?<action>.*?) secrets about the (?<topic>.*)": " заинтересованы в {action} секретов о {topic}",
    r"(?i) are interested in (?<action>.*?) about the (?<topic>.*)": " заинтересованы в {action} о {topic}",
    r"(?i) are interested in (?<action>.*?) about (?<topic>.*)": " заинтересованы в {action} о {topic}",
    r"(?i) is interested in (?<topic>.*)": " заинтересован(а) в {topic}",
    
    # Skills UI
    r"^(?<p>[a-z]\)) (?<skill>.*)$": "{p} {skill}", # This might be too broad, but let's see.
    
    # Specific Skill Fixes (Force translate if word-by-word fails)
    "Freezing Ray": "Замораживающий луч",
    "Flaming Ray": "Пламенный луч",
    "Make Camp": "Разбить лагерь",
    "Butcher Corpses": "Разделать трупы",
    "Sprint": "Спринт",
    "Teleport": "Телепорт",
    "Dismember": "Расчленить",
    
    # Calendar
    r"^(?<p>.*?) (?<d>\d+)(?:st|nd|rd|th) of (?<m>.*?) i Ux$": "{p} {d}-е {m} i Ux",
    
    # Item Names in tooltips (Fixing 'Edition')
    r"(?i)Dromad Edition": "Издание Дромадеров",
    r"(?i)Caves of Qud": "Пещеры Куда",
}

clean_dict('pattern_dictionary.json', update_kv=final_patterns)

print("CLEAN SWEEP COMPLETED. Dictionaries harmonized. Frankensteins targeted.")
