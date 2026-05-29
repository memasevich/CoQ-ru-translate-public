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

# 1. Broken keys to remove
bad_keys = [
    "direction.short (N)", "direction.short (S)", "direction.short (E)", "direction.short (W)",
    "Warden Yrame [N", "Ctesiphus [N", "BaseStarshipPlatform N"
]

# 2. Faction names
factions = {
    "villagers of Joppa": "жители Джоппы",
    "Fellowship of Wardens": "Братство Стражей",
    "Consortium of Phyta": "Консорциум Фита",
    "Bedroll-in-Citrine Church": "Церковь Спальника-в-Цитрине",
    "snapjaws": "челюстогрызы",
    "fungi": "грибы",
    "apes": "обезьяны",
    "baboons": "бабуины",
    "birds": "птицы",
    "crabs": "крабы",
    "Dromad": "дромадеры",
    "Farmers' Guild": "Гильдия фермеров",
    "highly entropic beings": "высокоэнтропийные существа",
    "Issachari tribe": "племя Иссахари",
    "Mechanimists": "Механимисты",
    "Merchants' Guild": "Гильдия торговцев",
    "Putus Templar": "Чистокровные Тамплиеры",
    "Robots": "роботы",
    "Seekers of the Sightless Way": "Искатели Незрячего Пути",
}

# 3. Reputation prefixes
reputation = {
    "Loved by ": "Любим фракцией: ",
    "Liked by ": "Понравился фракции: ",
    "Admired by ": "Им восхищаются: ",
    "Disliked by ": "Его не любит фракция: ",
    "Hated by ": "Его ненавидят: ",
    "for defending their village": "за защиту своей деревни",
    "for eating one of their young": "за поедание одного из их детенышей",
    "for sharing fresh water with them": "за то, что делится с ними пресной водой",
    "for reprogramming their favorite robot": "за перепрограммирование их любимого робота",
    "for digging up the remains of their ancestors": "за осквернение останков их предков",
}

update_json('dictionary.json', {**factions, **reputation}, remove_keys=bad_keys)
update_json('dictionary_master.json', {**factions, **reputation}, remove_keys=bad_keys)

# 4. Patterns
reputation_patterns = {
    r"^(?<p>.*?)Admired by (?<f>.*?) for (?<r>.*)$": "{p}Им восхищаются: {f} за {r}",
    r"^(?<p>.*?)Hated by (?<f>.*?) for (?<r>.*)$": "{p}Его ненавидят: {f} за {r}",
}
update_json('pattern_dictionary.json', reputation_patterns)

print("Stage 5 (Factions & Reputation) applied safely.")
