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

factions = {
    "Antelopes": "Антилопы",
    "apes": "обезьяны",
    "Arachnids": "Арахниды",
    "baboons": "Бабуины",
    "Baetyls": "Баэтилы",
    "Barathrumites": "Баратрумиты",
    "Bears": "Медведи",
    "Beasts": "Звери",
    "birds": "Птицы",
    "cannibals": "Каннибалы",
    "Cats": "Кошки",
    "Cherubim": "Херувимы",
    "Children of Mamon": "Дети Мамона",
    "Consortium of Phyta": "Консорциум Фита",
    "crabs": "Крабы",
    "Cragmensch": "Крагменши",
    "Cult of the Coiled Lamb": "Культ Свернутого Агнца",
    "Daughters of Exile": "Дочери Изгнания",
    "denizens of the Yd Freehold": "жители Идского Фрихольда",
    "Dogs": "Собаки",
    "Dromad": "Дромадеры",
    "dromad merchants": "Торговцы-дромадеры",
    "Entropic": "Энтропийцы",
    "Equines": "Лошадиные",
    "Ezra": "Эзра",
    "Farmers": "Фермеры",
    "Fellowship of Wardens": "Братство Стражей",
    "Fish": "Рыбы",
    "Flowers": "Цветы",
    "Frogs": "Лягушки",
    "fungi": "Грибы",
    "Girsh": "Гирш",
    "goatfolk": "Козлолюды",
    "grazing hedonists": "Пасущиеся гедонисты",
    "Gyre Wights": "Призраки Вихря",
    "highly entropic beings": "высокоэнтропийные существа",
    "Hindren": "Хиндрены",
    "Insects": "Насекомые",
    "Issachari tribe": "Племя Иссахари",
    "Joppa": "Джоппа",
    "Mechanimists": "Механимисты",
    "Merchants' Guild": "Гильдия торговцев",
    "Mopango": "Мопанго",
    "Naphtaali tribe": "Племя Нафтаали",
    "Newly Sentient Beings": "Новообретенные разумные существа",
    "Oozes": "Слизи",
    "pariahs": "Парии",
    "Plants": "Растения",
    "Prey": "Добыча",
    "Ptoh": "Птох",
    "Putus Templar": "Чистокровные Тамплиеры",
    "Robots": "Роботы",
    "Seekers of the Sightless Way": "Искатели Незрячего Пути",
    "snapjaws": "Снапджо",
    "Svardym": "Свардимы",
    "swine": "Свиньи",
    "trees": "Деревья",
    "trolls": "Тролли",
    "unshelled reptiles": "Голые рептилии",
    "villagers of Ezra": "жители Эзры",
    "villagers of Joppa": "жители Джоппы",
    "villagers of Kyakukya": "жители Киакуки",
    "Vines": "Лианы",
    "Wardens": "Стражи",
    "Winged Mammals": "Крылатые млекопитающие",
    "Worms": "Черви",
}

# Add common reputation prefixes
reputation_prefixes = {
    "Loved by ": "Любим фракцией: ",
    "Liked by ": "Понравился фракции: ",
    "Admired by ": "Им восхищаются: ",
    "Disliked by ": "Его не любит фракция: ",
    "Hated by ": "Его ненавидят: ",
    "for ": "за ",
}

update_json('dictionary.json', factions)
update_json('dictionary.json', reputation_prefixes)
update_json('dictionary_master.json', factions)
update_json('dictionary_master.json', reputation_prefixes)

print("Stage 5 (Factions) initial data added.")
