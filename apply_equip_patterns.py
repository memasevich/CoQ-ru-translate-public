import json
import os

def update_patterns(path, new_patterns):
    if not os.path.exists(path): return
    with open(path, 'r', encoding='utf-8') as f:
        data = json.load(f)
    for k, v in new_patterns.items():
        data[k] = v
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)

new_patterns = {
    r"^(?<pref>.*?)Your (?<item>.*?) was unequipped\.(?<suff>.*?)$": "{pref}Снаряжение снято: {item}.{suff}",
    r"^(?<pref>.*?)Your (?<item>.*?) is unequipped\.(?<suff>.*?)$": "{pref}Снаряжение снято: {item}.{suff}",
    r"^(?<pref>.*?)(?<pronoun>You|you) equip the (?<item>.*?)\.(?<suff>.*?)$": "{pref}Вы надеваете: {item}.{suff}",
    r"^(?<pref>.*?)(?<pronoun>You|you) unequip (?<your>your) (?<item>.*?)\.(?<suff>.*?)$": "{pref}Вы снимаете: {item}.{suff}",
    r"^(?<pref>.*?)(?<pronoun>You|you) equip (?<your>your) (?<item>.*?)\.(?<suff>.*?)$": "{pref}Вы надеваете: {item}.{suff}",
    r"^(?<pref>.*?)(?<pronoun>You|you) unequip the (?<item>.*?)\.(?<suff>.*?)$": "{pref}Вы снимаете: {item}.{suff}"
}

update_patterns('pattern_dictionary.json', new_patterns)
print("Added equip/unequip patterns.")
