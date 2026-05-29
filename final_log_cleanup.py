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

# 1. Patterns for Faction Interests
faction_patterns = {
    r"^(?<pref>.*?) are Interested in (?<action>.*?) (?<topic>.*?)(?<suff>.*?)$": "{pref} заинтересованы в {action} {topic}{suff}",
    r"^(?<pref>.*?) is Interested in (?<action>.*?) (?<topic>.*?)(?<suff>.*?)$": "{pref} заинтересован(а) в {action} {topic}{suff}",
    r"^(?<pref>.*?)sharing secrets about the(?<suff>.*?)$": "{pref}обмене секретами о{suff}",
    r"^(?<pref>.*?)learning about the(?<suff>.*?)$": "{pref}получении сведений о{suff}",
    r"^(?<pref>.*?)trading secrets about(?<suff>.*?)$": "{pref}торговле секретами о{suff}",
}
update_any_json('pattern_dictionary.json', faction_patterns)

# 2. Exact phrases from the log
exact_phrases = {
    "You don't have any schematics.": "У вас нет никаких чертежей.",
    "Your Intelligence determines your number of skill points and your ability to examine artifacts.": "Ваш Интеллект определяет количество очков навыков и вашу способность изучать артефакты.",
    "I am Fayumet. Who are you?": "Я Fayumet. А ты кто?",
    "And Argyve, too, friend. The tinker. Always looking for trinkets to wire": "А еще Аржив, друг. Изобретатель. Вечно ищет безделушки, чтобы",
    "between, heh. Go through his hut of sheet metal, to the southwest. ": "соединить их проводами, хех. Его хижина из листового металла на юго-западе.",
    "The farmers are plagued by cave vermin. You might speak to Mehmet": "Фермеры страдают от пещерных вредителей. Тебе стоит поговорить с Мехметом",
    "about the locations of other merchants and all sorts of": "о местоположении других торговцев и о всякого рода",
    "the sultan they worship. They're also interested in": "султане, которому они поклоняются. Они также заинтересованы в",
    "meht, the tongue says. Live and drink, Fayumet": "мехмет, так говорят. Живи и пей, Fayumet",
    "I grew up right here in Joppa, daughter of the Elder and all. Spent some years": "Я выросла прямо здесь, в Джоппе, я дочь Старейшины. Провела несколько лет",
    "delving, but gave it up after a few near escapes and nearly deadly wounds. I’m": "в поисках приключений, но бросила это после пары опасных переделок и тяжелых ран. Я",
    "better suited to herbalism anyway, and my father worries less.": "в любом случае больше подхожу для траволечения, да и мой отец меньше волнуется.",
    "Fates forfend. I would hardly like to lead a settlement and run a shop at the": "Судьба упаси. Я бы вряд ли захотел возглавлять поселение и одновременно заправлять лавкой",
    "same time.": "в то же время."
}
update_any_json('dictionary.json', exact_phrases)
update_any_json('dictionary_master.json', exact_phrases)

# 3. Clean up the 'заряда' in word_dictionary
word_fixes = {
    "charge": "заряд",
    "Charge": "Рывок",
    "charges": "заряды",
    "charged": "заряжен",
    "Interested": "заинтересованы",
    "interested": "заинтересованы",
}
update_any_json('word_dictionary.json', word_fixes)

print("Applied final round of fixes to dictionaries (v2).")
