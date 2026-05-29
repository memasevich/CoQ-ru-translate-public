import json
import os

def update_json(path, updates, remove_keys=None):
    if not os.path.exists(path): return
    with open(path, 'r', encoding='utf-8') as f:
        data = json.load(f)
    if remove_keys:
        for k in remove_keys:
            data.pop(k, None)
    for k, v in updates.items():
        data[k] = v
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)

# 1. FIXING THE "INTERESTED" MESS
# We need patterns that handle the exact strings from your log
interest_patterns = {
    # The start of the interest block
    r"^(?<p>.*?)Заинтересованы в trading secrets about(?<topic>.*)$": "{p}заинтересованы в обмене секретами о {topic}",
    r"^(?<p>.*?)Заинтересованы в обмене секретами о(?<topic>.*)$": "{p}заинтересованы в обмене секретами о {topic}",
    
    # Mid-sentence segments
    r"(?i)местоположениях ichor merchants' distilleries": "расположении дистилляторов торговцев ихором",
    r"(?i)местоположениях honey weeps": "расположении медовых плакунов",
    r"(?i)locations of ichor merchants' distilleries": "расположении дистилляторов торговцев ихором",
    r"(?i)locations of honey weeps": "расположении медовых плакунов",
    
    # Endings
    r"\. They're also interested in hearing(?<topic>.*)$": ". Они также заинтересованы в прослушивании {topic}",
    r"\. They're also interested in(?<topic>.*)$": ". Они также заинтересованы в {topic}",
}

# 2. FIXING THE "DISLIKED BY" AND "C" ARTIFACT
# The artifact "Bedroll-in-Citrine ChurchС" comes from a partial match.
reputation_fixes = {
    # Full exact match for Irudad's log
    "Disliked by Bedroll-in-Citrine Church for digging up remains of their ancestors.": "Его не любит Церковь Спальника-в-Цитрине за осквернение останков их предков.",
    "Admired by villagers of Joppa for defending their village.": "Им восхищаются жители Джоппы за защиту их деревни.",
}

# 3. CLEANING WORD DICTIONARY (Final sweep)
# Removing everything that can sneak into these sentences.
word_removals = [
    "trading", "secrets", "learning", "hearing", "gossip", "locations", "location", "sharing",
    "remains", "ancestors", "defending", "village", "merchants", "distilleries", "honey", "weeps",
    "about", "they", "them", "also", "their", "your", "this", "that"
]

update_json('pattern_dictionary.json', interest_patterns)
update_json('dictionary.json', reputation_fixes)
update_json('dictionary_master.json', reputation_fixes)

if os.path.exists('word_dictionary.json'):
    with open('word_dictionary.json', 'r', encoding='utf-8') as f:
        wd = json.load(f)
    for w in word_removals:
        wd.pop(w, None)
        wd.pop(w.capitalize(), None)
    with open('word_dictionary.json', 'w', encoding='utf-8') as f:
        json.dump(wd, f, ensure_ascii=False, indent=2)

print("Repair for 'Interested in' and reputation strings applied.")
