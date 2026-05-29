import json
import os
import re

def update_any_json(path, updates):
    if not os.path.exists(path): return
    with open(path, 'r', encoding='utf-8') as f:
        data = json.load(f)
    for k, v in updates.items():
        data[k] = v
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)

# 1. ROBUST REPUTATION PATTERNS
# We need to catch these BEFORE word-by-word kicks in.
# Faction names are usually already translated (or in Cyrillic).
reputation_patterns = {
    # General Interests
    r"^(?<pref>.*?) are interested in sharing secrets about the (?<topic>.*?)(?<suff>.*?)$": "{pref} заинтересованы в обмене секретами о {topic}{suff}",
    r"^(?<pref>.*?) are interested in trading secrets about (?<topic>.*?)(?<suff>.*?)$": "{pref} заинтересованы в торговле секретами о {topic}{suff}",
    r"^(?<pref>.*?) are interested in learning about the (?<topic>.*?)(?<suff>.*?)$": "{pref} заинтересованы в изучении {topic}{suff}",
    r"^(?<pref>.*?) are interested in hearing (?<topic>.*?)(?<suff>.*?)$": "{pref} заинтересованы в прослушивании {topic}{suff}",
    r"^(?<pref>.*?) is interested in (?<topic>.*?)(?<suff>.*?)$": "{pref} заинтересован(а) в {topic}{suff}",

    # Specific sub-parts often seen in log
    "sharing secrets about the": "обмене секретами о",
    "trading secrets about": "торговле секретами о",
    "learning about the": "изучении",
    "hearing gossip that's about them": "прослушивании слухов о них",

    # Faction attitudes
    r"^(?<pref>.*?) don't care about you, but (?<suff>.*?)$": "{pref} не обращают на вас внимания, но {suff}",
    r"^(?<pref>.*?) favor you\. (?<suff>.*?)$": "{pref} благосклонны к вам. {suff}",
    r"^(?<pref>.*?) aggressive members will attack you(?<suff>.*?)$": "{pref} агрессивные члены будут атаковать вас{suff}",
    r"^(?<pref>.*?) docile members will usually let you pet them(?<suff>.*?)$": "{pref} послушные члены обычно позволяют вам гладить их{suff}",
}

# 2. COMBAT LOG REPAIR
combat_patterns = {
    r"^(?<pref>.*?) misses вас с its (?<weapon>.*?)!(?<suff>.*?)$": "{pref} промахивается по вам ({weapon})!{suff}",
    r"^(?<pref>.*?) hits вас for (?<dmg>\d+) damage with its (?<weapon>.*?)!(?<suff>.*?)$": "{pref} наносит вам {dmg} ед. урона ({weapon})!{suff}",
}

# 3. CALENDAR
calendar_patterns = {
    r"^(?<time>.*?) (?<day>\d+)th of (?<month>.*?) i Ux(?<suff>.*?)$": "{time} {day}-е {month} i Ux{suff}",
    r"^(?<time>.*?) of (?<month>.*?) i Ux(?<suff>.*?)$": "{time} {month} i Ux{suff}",
}

# 4. UI FIXES
ui_patterns = {
    r"^(?<name>.*?)'s Skills$": "Навыки {name}",
    r"^(?<val>\d+)lbs\.(?<suff>.*?)$": "{val} фунт.{suff}",
}

update_any_json('pattern_dictionary.json', reputation_patterns)
update_any_json('pattern_dictionary.json', combat_patterns)
update_any_json('pattern_dictionary.json', calendar_patterns)
update_any_json('pattern_dictionary.json', ui_patterns)

# 5. DICTIONARY UPDATES (From game files found in previous turns)
# Adding strings directly from ObjectBlueprints/Items.xml
game_file_strings = {
    "Wood was shaved into a bludgeoning bulb.": "Дерево обтесано в форме дубины.",
    "Brinestalk was scythed from its marsh home and dried in the high salt sun. Its top is spiraled into a crook.": "Рассолостебель был срезан в родном болоте и высушен под жарким соляным солнцем. Его верхушка закручена в спираль.",
    "A corm of bell bronze is screw-fitted to a scarred wooden shaft.": "Навершие из колокольной бронзы привинчено к исцарапанному деревянному древку.",
    "Iron lines unfold into a head of six flanges.": "Железные линии раскрываются в навершие с шестью фланцами.",
}
update_any_json('dictionary.json', game_file_strings)
update_any_json('dictionary_master.json', game_file_strings)

print("Applied robust repair for Frankensteins and Reputation.")
