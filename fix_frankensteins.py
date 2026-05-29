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

# 1. ТОТАЛЬНАЯ ЗАЧИСТКА ВЕТОК ДИАЛОГОВ (Exact Matches)
# Эти строки я взял напрямую из вашего лога
massive_exact = {
    # Мехмет
    "-mm, work? The farmers are plagued by cave vermin. You might speak to Mehmet": "-мм, работа? Фермеры страдают от пещерных вредителей. Тебе стоит поговорить с Мехметом",
    "' there, by the southern watervine patch.": "' там, у южной грядки влагопаутинника.",
    " Mehmet, the tongue says. Live and drink, Fayumet": " Мехмет, так меня зовут. Живи и пей, Fayumet",
    
    # Ирудад
    "I grew up right here in Joppa, daughter of the Elder and all. Spent some years": "Я выросла прямо здесь, в Джоппе, я дочь Старейшины. Провела несколько лет",
    "delving, but gave it up after a few near escapes and nearly deadly wounds. I’m": "в поисках приключений, но бросила это после пары опасных переделок и тяжелых ран. Я",
    "better suited to herbalism anyway, and my father worries less.": "в любом случае больше подхожу для траволечения, да и мой отец меньше волнуется.",
    "Fates forfend. I would hardly like to lead a settlement and run a shop at the": "Судьба упаси. Я бы вряд ли захотел возглавлять поселение и одновременно заправлять лавкой",
    "same time.": "одновременно.",
    
    # Аржив
    "And Argyve, too, friend. The tinker. Always looking for trinkets to wire": "А еще Аржив, друг. Изобретатель. Вечно ищет безделушки, чтобы",
    "between, heh. Go through his hut of sheet metal, to the southwest. ": "соединить их проводами, хех. Пройди в его хижину из листового металла на юго-западе.",
    
    # Описания Куда
    "-mm, there? Land slopes up and is toothed in chrome steeples, ageless things.": "-мм, там? Земля уходит вверх и усеяна хромированными шпилями, вечными вещами.",
    "Looming over the old-earth tunnels, pillowed in rotting jungle and fungus": "Нависающими над туннелями древней Земли, утопающими в гниющих джунглях и грибных",
    "groves. And broken against the mounts of the north, they are, in the shadow of": "рощах. И разбитыми о северные горы, они стоят в тени",
    "the Spindle.": "Шпинделя.",
    "There are people, friends, communities. And friends-to-noone, too. History is": "Там есть люди, друзья, общины. И враги всех и каждого тоже. История там",
    "thick as the high salt sun, friend, and where the past is ground up like matz": "густа, как высокое соляное солнце, друг, и там прошлое перетерто в муку,",
    "meal, a mix of life sets in.": "в которой зарождается новая жизнь.",
    "-mm, adventurous ones set off for their own splinter of artifact. To both": "-мм, искатели приключений отправляются за своим собственным осколком артефакта. К обеим",
    "Fates, life and death...": "Судьбам, жизни и смерти...",
    
    # Атрибуты (Полные строки из лога)
    "Your Intelligence determines your number of skill points and your ability to examine artifacts.": "Ваш Интеллект определяет количество очков навыков и вашу способность изучать артефакты.",
    "Your Agility determines your accuracy with melee and ranged weapons, as well as your chance to dodge attacks.": "Ваша Ловкость определяет вашу меткость в ближнем и дальнем бою, а также ваш шанс уклониться от атак.",
    
    # UI
    "[c] collect liquid": "[c] собрать жидкость",
    "[f] fill": "[f] наполнить",
    "[K] attack": "[K] атаковать",
    "[s] seal": "[s] запечатать",
    "[h] chat": "[h] говорить",
    "[k] drink": "[k] пить",
    "[p] pour": "[p] вылить",
    "show cybernetics": "показать кибернетику",
    "switch to modifications": "переключиться на модификации",
    "Perfect": "Идеально",
}

# 2. ПАТТЕРНЫ ДЛЯ "ФРАНКЕНШТЕЙНОВ"
massive_patterns = {
    # Фракции (исправление are Заинтересованы в ...)
    r"^(?<pref>.*?) are Interested in (?<action>.*?) (?<topic>.*?)(?<suff>.*?)$": "{pref} заинтересованы в {action} {topic}{suff}",
    r"^(?<pref>.*?) is Interested in (?<action>.*?) (?<topic>.*?)(?<suff>.*?)$": "{pref} заинтересован(а) в {action} {topic}{suff}",
    r"^(?<pref>.*?)sharing secrets about the(?<suff>.*?)$": "{pref}обмене секретами о{suff}",
    r"^(?<pref>.*?)learning about the(?<suff>.*?)$": "{pref}получении сведений о{suff}",
    r"^(?<pref>.*?)trading secrets about(?<suff>.*?)$": "{pref}торговле секретами о{suff}",
    r"^(?<pref>.*?)hearing gossip that's about them(?<suff>.*?)$": "{pref}прослушивании слухов о них{suff}",
    
    # Квесты
    r"^(?<pref>.*?)Travel to the historic site of (?<loc>.*?)(?<suff>.*?)$": "{pref}Отправляйтесь к историческому месту: {loc}{suff}",
    r"^(?<p>\+) Visit the (?<loc>.*?) of (?<town>.*?)$": "{p} Посетить {loc} ({town})",
    r"^(?<p>\-) Visit the (?<loc>.*?) of (?<town>.*?)$": "{p} Посетить {loc} ({town})",
    
    # Календарь
    r"^(?<pref>.*?) of Tishru (?<suff>.*?)$": "{pref} Тишру {suff}",
    r"^(?<pref>.*?) of (?<month>.*?) (?<suff>.*?)$": "{pref} {month} {suff}",
    
    # "Заряда" (Charge) в навыках
    r"^(?<pref>.*?), charge(?<suff>.*?)$": "{pref}, рывок{suff}",
    r"^(?<pref>.*?):charge(?<suff>.*?)$": "{pref}:рывок{suff}",
}

# 3. ПОСЛОВНЫЙ СЛОВАРЬ (Word Dictionary) - Убираем мусор
word_cleanup = {
    "charge": "заряд",
    "Charge": "Рывок",
    "of": " ", # Убираем перевод 'of' как 'из' в середине предложений, это чаще всего портит всё
    "the": " ",
    "and": "и",
    "Interested": "заинтересованы",
    "interested": "заинтересованы",
    "sharing": "обмене",
    "learning": "изучении",
    "trading": "торговле",
    "secrets": "секретов",
    "locations": "местоположений",
    "location": "местоположение",
    "about": "о",
    "for": "для",
    "from": "из",
    "with": "с",
    "your": "ваш",
    "Your": "Ваш",
    "determines": "определяет",
    "number": "количество",
}

update_any_json('dictionary.json', massive_exact)
update_any_json('dictionary_master.json', massive_exact)
update_any_json('pattern_dictionary.json', massive_patterns)
update_any_json('word_dictionary.json', word_cleanup)

print("Massive Frankenstein fix applied.")
