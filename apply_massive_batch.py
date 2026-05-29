import json
import os

def update_patterns(path, new_patterns, remove_keys=None):
    if not os.path.exists(path): return
    with open(path, 'r', encoding='utf-8') as f:
        data = json.load(f)
    if remove_keys:
        for k in remove_keys:
            if k in data: del data[k]
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

# 1. Remove the dangerous "broken tags" patterns
remove_patterns = [
    r"^(?<pref>.*?)p(?<c1>.*?)a(?<c2>.*?)i(?<c3>.*?)n(?<c4>.*?)t(?<c5>.*?)e(?<c6>.*?)d(?<suff>.*?)$",
    r"^(?<pref>.*?)e(?<c1>.*?)n(?<c2>.*?)g(?<c3>.*?)r(?<c4>.*?)a(?<c5>.*?)v(?<c6>.*?)e(?<c7>.*?)d(?<suff>.*?)$"
]

new_patterns = {
    # Improved Faction/Villager patterns (more flexible ends)
    r"^(?<pref>.*?)The villagers of (?<town>.*?)(?<suff>(?:</color>)?.*?)$": "{pref}Жители поселения {town}{suff}",
    r"^(?<pref>.*?)The Cult of the (?<cult>.*?)(?<suff>(?:</color>)?.*?)$": "{pref}Культ: {cult}{suff}",
    r"^(?<pref>.*?)The (?<tribe>.*?) tribe(?<suff>(?:</color>)?.*?)$": "{pref}Племя {tribe}{suff}",
    
    # Interests
    r"^(?<pref>.*?) заинтер(?<mid>.*?) в (?<action>.*?) о(?<suff>.*?)$": "{pref} заинтер{mid} в {action} о{suff}",
    r"^(?<pref>.*?)They're also interested in(?<suff>.*?)$": "{pref}Они также заинтересованы в{suff}",
    r"^(?<pref>.*?)They're also interested in (?<action>.*?)(?<suff>.*?)$": "{pref}Они также заинтересованы в {action}{suff}",
    r"^(?<pref>.*?)Interested in (?<action>.*?)(?<suff>.*?)$": "{pref}Заинтересованы в {action}{suff}",
    r"^(?<pref>.*?)interested in hearing (?<topic>.*?)(?<suff>.*?)$": "{pref}заинтересованы в получении {topic}{topic}{suff}",

    # Attribute Descriptions
    r"^(?<pref>.*?)Ваш (?<attr>.*?) determines (?<desc>.*?)(?<suff>\.?)$": "{pref}Ваш {attr} определяет {desc}{suff}",
    r"^(?<pref>.*?)Ваш (?<attr>.*?) measures (?<desc>.*?)(?<suff>\.?)$": "{pref}Ваш {attr} измеряет {desc}{suff}",
    
    # Action Log
    r"^(?<pref>.*?)You take (?<item>.*?) from the (?<dir>.*?)\.(?<suff>.*?)$": "{pref}Вы берете {item} ({dir}).{suff}",
    r"^(?<pref>.*?)You identify the odd trinket as (?<item>.*?)\.(?<suff>.*?)$": "{pref}Вы опознали странную безделушку как: {item}.{suff}",
    r"^(?<pref>.*?)You wade through бассейн of (?<liquid>.*?)\.(?<suff>.*?)$": "{pref}Вы идете вброд через бассейн с: {liquid}.{suff}",
    r"^(?<pref>.*?)You stop движущийся because there is a (?<enemy>.*?) in your way\.(?<suff>.*?)$": "{pref}Вы остановились, потому что путь преграждает {enemy}.{suff}",
    r"^(?<pref>.*?)You emit a ray of frost from your (?<part>.*?)\.(?<suff>.*?)$": "{pref}Вы выпускаете ледяной луч из ваших {part}.{suff}",
    r"^(?<pref>.*?)You make an (?<atk>.*?) attack against an (?<target>.*?) and they start (?<eff>.*?)\.(?<suff>.*?)$": "{pref}Вы проводите атаку ({atk}) против {target}, и у цели начинается {eff}.{suff}"
}

new_exact = {
    "human? We are greeted! What do you desire?": "человек? Приветствуем! Чего ты желаешь?",
    "I am Fayumet. Who are you?": "Я Fayumet. А ты кто?",
    "I am called Fayumet.": "Меня зовут Fayumet.",
    "It is a pleasure to know this, human друг Fayumet! I am Tam.": "Приятно познакомиться, человек-друг Fayumet! Я Тэм.",
    "I am дромады, human друг.": "Я дромадер, человек-друг.",
    "Some say saltstrider. Do you know this?": "Некоторые зовут нас солеходами. Знаешь о таких?",
    "My люди have walked the salt for thousands of years, meeting every существо": "Мой народ ходит по солям тысячи лет, встречая каждое существо",
    "there. And welcome to Джоппа.": "здесь. И добро пожаловать в Джоппу.",
    "The tatters of a sweat-запятнанный kufiya thrash in the ветер": "Обрывки пропотевшей куфии развеваются на ветру",
    "hang about the farmer's neck.": "висят на шее фермера.",
    "Classical растение called влагопаутинник.": "Классическое растение под названием влагопаутинник.",
    "You emit a ray of frost from your руки.": "Вы выпускаете ледяной луч из ваших рук.",
    "show кибернетика": "показать кибернетику",
    "Offhand Атаковать Chance: 15%": "Шанс атаки второй рукой: 15%",
    "Adds simple taste-based эффекты to cooked meals.": "Добавляет простые вкусовые эффекты к приготовленным блюдам.",
    "resting until healed...": "отдых до исцеления...",
    "show advanced options": "показать расширенные настройки",
    "I grew up right here in Джоппа, дочь of the Старейшина and all.": "Я выросла прямо здесь, в Джоппе, я дочь Старейшины.",
    "Spent some years delving, but gave it up after a few near escapes and nearly deadly wounds.": "Несколько лет я была искательницей, но бросила это после пары опасных переделок и тяжелых ран.",
    "I’m better suited to herbalism anyway, and my отец worries less.": "В любом случае, я больше склонна к траволечению, да и отец меньше волнуется.",
    "Fates forfend. I would hardly like to свинцовый поселение and run a shop at the same time.": "Упаси боги. Я бы вряд ли захотел возглавлять поселение и одновременно заправлять лавкой."
}

update_patterns('pattern_dictionary.json', new_patterns, remove_patterns)
update_exact('dictionary.json', new_exact)
update_exact('dictionary_master.json', new_exact)

print("Batch processing completed.")
