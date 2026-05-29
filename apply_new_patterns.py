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

def update_exact(path, new_exact):
    if not os.path.exists(path): return
    with open(path, 'r', encoding='utf-8') as f:
        data = json.load(f)
    for k, v in new_exact.items():
        data[k] = v
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)

new_patterns = {
    r"^(?<pref>.*?)The villagers of (?<town>.*?)$": "{pref}Жители поселения {town}",
    r"^(?<pref>.*?)The Cult of the (?<cult>.*?)$": "{pref}Культ: {cult}",
    r"^(?<pref>.*?)(?<pronoun>You|you) pass by (?<item>.*?)\.(?<suff>.*?)$": "{pref}Вы проходите мимо: {item}.{suff}",
    r"^(?<pref>.*?)(?<pronoun>You|you) hit (?<roll>.*?) for (?<dmg>\d+) damage with (?<your>your) (?<weapon>.*?)! \[(?<dice>.*?)\](?<suff>.*?)$": "{pref}Вы наносите удар {roll} на {dmg} ед. урона вашим оружием: {weapon}! [{dice}]{suff}",
    r"^(?<pref>.*?)They're also interested in(?<suff>.*?)$": "{pref}Они также заинтересованы в{suff}",
    r"^(?<pref>.*?)are interested in learning about the locations of(?<suff>.*?)$": "{pref}заинтересованы в получении информации о расположении{suff}"
}

new_exact = {
    "Weight: 1 lbs.": "Вес: 1 фунт.",
    "1 lbs.": "1 фунт.",
    "Make Camp": "Разбить лагерь",
    "c) Make Camp": "c) Разбить лагерь",
    "[D] Desecrate": "[D] Осквернить",
    "the rotting jungles of Qud.": "гниющие джунгли Куда.",
    "there. And welcome to Joppa.": "здесь. И добро пожаловать в Джоппу."
}

update_patterns('pattern_dictionary.json', new_patterns)
update_exact('dictionary.json', new_exact)
update_exact('dictionary_master.json', new_exact)
