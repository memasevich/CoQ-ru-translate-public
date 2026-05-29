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

# 1. REMOVE DANGEROUS WORD-BY-WORD TRANSLATIONS
# These words are causing most Frankenstein sentences by injecting themselves incorrectly.
words_to_remove = ["of", "the", "is", "are", "for", "with", "from", "and", "about", "to", "at", "in", "by", "on"]
if os.path.exists('word_dictionary.json'):
    with open('word_dictionary.json', 'r', encoding='utf-8') as f:
        word_dict = json.load(f)
    for w in words_to_remove:
        if w in word_dict: del word_dict[w]
        if w.capitalize() in word_dict: del word_dict[w.capitalize()]
    with open('word_dictionary.json', 'w', encoding='utf-8') as f:
        json.dump(word_dict, f, ensure_ascii=False, indent=2)

# 2. MASSIVE EXACT MATCH UPDATES (From word_replacements.txt)
massive_exact = {
    "g|Dromad Edition": "g|Издание Дромадеров",
    "C|Caves of Qud": "C|Пещеры Куда",
    "b) Freezing Ray": "b) Замораживающий луч",
    "c) Make Camp": "c) Разбить лагерь",
    "f) Butcher Corpses": "f) Разделать трупы",
    "ACTIVE EFFECTS": "АКТИВНЫЕ ЭФФЕКТЫ",
    "Type: Mental Mutations": "Тип: Ментальные мутации",
    "Type: Physical Mutations": "Тип: Физические мутации",
    "Type: Skills": "Тип: Навыки",
    "You emit a ray of frost from your hands": "Вы выпускаете ледяной луч из ваших рук",
    "show cybernetics": "показать кибернетику",
    "switch to modifications": "переключиться на модификации",
    "Offhand Attack Chance: 15%": "Шанс атаки второй рукой: 15%",
    "Weight: 0 lbs": "Вес: 0 фунтов",
    "Weight: 1 lbs": "Вес: 1 фунт",
    "Weight: 7 lbs": "Вес: 7 фунтов",
    "Travel to the historical site of the Saloons of Nalil": "Отправляйтесь к историческому месту: Салуны Налила",
    "Visit the Saloons of Nalil": "Посетить Салуны Налила",
    "the Saloons of Nalil": "Салуны Налила",
    "The Cabal of the Scrollskipper": "Кабала Скроллскиппера",
    "The Спальник-in-Citrine Church": "Церковь Спальника-в-Цитрине",
    "the Bedroll-in-Citrine Church": "Церковь Спальника-в-Цитрине",
    "for digging up the remains of their ancestors": "за осквернение останков их предков",
    "The watervine farmer wakes up": "Фермер влагопаутинника просыпается",
    "Meals cooked from recipes bestow special status effects. Meals cooked with selected ingredients bestow dynamically-generated status effects": "Блюда, приготовленные по рецептам, дают особые статусные эффекты. Блюда из выбранных ингредиентов дают динамические эффекты.",
    "Rules|Adds simple taste-based effects to cooked meals": "Правила|Добавляет простые вкусовые эффекты к блюдам",
    "Rules|Adds HP-based effects to cooked meals": "Правила|Добавляет эффекты лечения к блюдам",
    "Live and drink, aquafriend": "Живи и пей, водный друг",
}

# 3. POWERFUL PATTERNS (To catch procedural sentences)
massive_patterns = {
    # Faction Interests - Catching the full "are interested in..." phrases
    r"^(?<pref>.*?) are interested in (?<action>.*?) secrets about the (?<topic>.*?)(?<suff>.*?)$": "{pref} заинтересованы в {action} секретов о {topic}{suff}",
    r"^(?<pref>.*?) are interested in (?<action>.*?) about the (?<topic>.*?)(?<suff>.*?)$": "{pref} заинтересованы в {action} о {topic}{suff}",
    r"^(?<pref>.*?) are interested in (?<action>.*?) about (?<topic>.*?)(?<suff>.*?)$": "{pref} заинтересованы в {action} о {topic}{suff}",
    r"^(?<pref>.*?) is interested in (?<action>.*?) about the (?<topic>.*?)(?<suff>.*?)$": "{pref} заинтересован(а) в {action} о {topic}{suff}",
    
    # Specific Interest Actions
    "sharing secrets": "обмене секретами",
    "trading secrets": "торговле секретами",
    "learning about": "изучении",
    "hearing gossip": "прослушивании слухов",
    "trading secrets about books": "торговле секретами о книгах",
    "trading secrets about watery": "торговле секретами о водных",
    
    # Quest logs
    r"^(?<pref>.*?)Travel to the historical site of the (?<loc>.*?)(?<suff>.*?)$": "{pref}Отправляйтесь к историческому месту: {loc}{suff}",
    r"^(?<p>\+) Visit the (?<loc>.*?) of (?<town>.*?)$": "{p} Посетить {loc} ({town})",
    r"^(?<p>\-) Visit the (?<loc>.*?) of (?<town>.*?)$": "{p} Посетить {loc} ({town})",
}

update_any_json('dictionary.json', massive_exact)
update_any_json('dictionary_master.json', massive_exact)
update_any_json('pattern_dictionary.json', massive_patterns)

print("Targeted audit fix applied. Dangerous small words removed from word-by-word dict.")
