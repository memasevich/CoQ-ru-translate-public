import json
import os
import re

def update_patterns(path, new_patterns):
    if not os.path.exists(path): return
    with open(path, 'r', encoding='utf-8') as f:
        data = json.load(f)
    for k, v in new_patterns.items():
        data[k] = v
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)

def update_exact(path, new_exact):
    if not os.path.exists(path): return
    with open(path, 'r', encoding='utf-8') as f:
        data = json.load(f)
    for k, v in new_exact.items():
        data[k] = v
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)

new_patterns = {
    r"^(?<pref>.*?)build (?<ver>\d+\.\d+\.\d+\.\d+)(?<suff>.*?)$": "{pref}сборка {ver}{suff}",
    r"^(?<pronoun>He|She|It|They) has been eaten by the (?<enemy>.*?)\.$": "{pronoun} был съеден существом: {enemy}.",
    r"^(?<pronoun>You|you) were killed by (?<enemy>.*?)\.$": "Вы были убиты существом: {enemy}.",
    r"^(?<dmg>[-+]\d+) to hit in melee combat\.$": "{dmg} к попаданию в ближнем бою."
}

new_exact = {
    "In Caves of Qud, you play a mutant or a true-kin.": "В Caves of Qud вы играете за мутанта или истинного потомка.",
    "On the right are the action buttons, the minimap, and the message log.": "Справа находятся кнопки действий, миникарта и журнал сообщений.",
    "fiends and oft succumb to fits of maddened fury.\"": "демоны и часто поддаются приступами безумной ярости.\""
}

update_patterns('pattern_dictionary.json', new_patterns)
update_exact('dictionary.json', new_exact)
update_exact('dictionary_master.json', new_exact)

print("Patterns and exact phrases added.")
