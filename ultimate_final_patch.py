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

# 1. FINAL SET OF EXACT MATCHES (CATCHING THE BIG ONES)
final_exact = {
    # Attributes / Descriptions
    "activated abilities, determines your ability to resist mental attacks, and modifies your hit point regeneration rate": "активируемые способности, определяет вашу способность сопротивляться ментальным атакам и влияет на скорость восстановления здоровья",
    "mental mutations, your ability to haggle with merchants, and your ability to dominate the wills of others": "ментальные мутации, вашу способность торговаться и вашу способность подчинять волю других",
    "points, your hit point regeneration rate, and your ability to resist poison and disease": "очки, скорость восстановления здоровья и вашу сопротивляемость ядам и болезням",
    "determines the potency of your activated abilities": "определяет силу ваших активируемых способностей",
    
    # Dialogues / Descriptions from log
    "The Cult of Yyram II": "Культ Ийрама II",
    "take care to pay you after, waterhand.": "не забудь заплатить тебе потом, водный житель.",
    "Must you bother me? What are you, some sort of waterfreak? Faundren-eyed": "Тебе обязательно меня беспокоить? Ты что, какой-то водяной урод? С глазами Фаундрена",
    "Slide drawers of galvanized metal are arranged in a cabinet as rectangles of assorted sizes. Storage is fractal.": "Выдвижные ящики из оцинкованного металла расположены в шкафу в виде прямоугольников разных размеров. Хранилище фрактально.",
    "Shot ink decorates the dulla on his long neck in one thousand patterns of the camelfolk.": "Чернила украшают дуллу на его длинной шее тысячью узоров верблюжьего народа.",
    "I walked Moghra'yi in the caravans of my brethren and the saltback, but upon the hanging hills our chests were pressed to": "Я ходил по Могра-йи в караванах моих братьев и солеспинов, но на висячих холмах наша грудь была прижата к",
    "It is a pleasure to know this, human friend Fayumet! I am Tam.": "Приятно это знать, человек-друг Fayumet! Я Тэм.",
    "I am a dromad, human friend. Some say saltstrider. Do you know this?": "Я дромадер, человек-друг. Некоторые называют нас солеходами. Знаешь об этом?",
    "My people have walked the salt for thousands of years, meeting every creature that lives and thinks. From the Pale Sea to the marsh of Joppa, and under the hanging hills, our chests were pressed to": "Мой народ ходил по солям тысячи лет, встречая каждое живое и мыслящее существо. От Бледного моря до болот Джоппы, и под висячими холмами, наша грудь была прижата к",
    "There are caves everywhere, you dolt! Scoop the surface, marsh, or head all": "Пещеры повсюду, болван! Обыщи поверхность, болото или все вершины",
    "bent metal sheet x2": "изогнутый металлический лист x2",
    "unknown> / Joppa": "неизвестно> / Джоппа",
    "Travel to the historical site of the Saloons of Nalil": "Отправьтесь к историческому месту: Салуны Налила",
}

# 2. FINAL PATTERNS
final_patterns = {
    r"^(?<p>.*?) turn (?<v>\d+)$": "{p} ход {v}",
    r"^(?<p>.*?) at (?<v>\d+:\d+:\d+)$": "{p} в {v}",
    r"^(?<p>.*?) (?<d>\d+)th of (?<m>.*?) (?<y>\d+) at (?<t>.*?)$": "{p} {d}-е {m} {y} в {t}",
    r"^(?<p>.*?) of (?<m>.*?) i Ux$": "{p} {m} i Ux",
    r"^(?<p>.*?) of (?<m>.*?) Ux$": "{p} {m} Ux",
}

update_any_json('dictionary.json', final_exact)
update_any_json('dictionary_master.json', final_exact)
update_any_json('pattern_dictionary.json', final_patterns)

# 3. EXTRA CLEANING OF WORD DICTIONARY (Remove 'your' and 'your' variations)
# 'your' is 4 letters, so it stayed. But it often causes Frankensteins.
if os.path.exists('word_dictionary.json'):
    with open('word_dictionary.json', 'r', encoding='utf-8') as f:
        wd = json.load(f)
    for w in ['your', 'Your', 'some', 'Some', 'that', 'That', 'this', 'This', 'from', 'From', 'with', 'With']:
        if w in wd: del wd[w]
    with open('word_dictionary.json', 'w', encoding='utf-8') as f:
        json.dump(wd, f, ensure_ascii=False, indent=2)

print("ULTIMATE FINAL PATCH APPLIED.")
