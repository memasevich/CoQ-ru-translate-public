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
    # Тэм (дромадер)
    "human? We are greeted! What do you desire?": "человек? Приветствуем! Чего ты желаешь?",
    "It is a pleasure to know this, human друг Fayumet! I am Tam.": "Приятно познакомиться, человек-друг Fayumet! Я Тэм.",
    "What kind creature are you?": "Что ты за существо?",
    "What kind существо are you?": "Что ты за существо?",
    "I am дромадер, human друг. Some say saltstrider. Do you know this?": "Я дромадер, человек-друг. Некоторые зовут нас солеходами. Знаешь о таких?",
    "My люди have walked the salt for thousands years, meeting every существо": "Мой народ ходит по солям тысячи лет, встречая каждое существо,",
    "that lives and thinks. From Pale Sea to the marsh Джоппа, and under the": "которое живет и мыслит. От Бледного Моря до болот Джоппы и под",
    "Висячие холмы, наши груди прижаты": "Висячими холмами, наши груди прижаты",
    "the camelfolk. His ears are arrayed with gilded кольца, and eyelashes flow": "верблюжьего народа. Его уши украшены позолоченными кольцами, а ресницы спадают",
    "down his лицо like влагопаутинник fronds. Across his back, a hump moistured": "на его лицо, как ветви влагопаутинника. На его спине горб, полный влаги",
    "and здоровый fat pushes his center mass skyward.": "и здорового жира, возносит его центр масс к небу.",
    
    # Аржив и Ирудад
    "There are пещеры everywhere, you dolt! Scoop the поверхность , marsh, or голова": "Пещеры повсюду, болван! Обыщи поверхность, болото или",
    "все": "вершины.",
    "Принеси Аргиву безделушку": "Принеси Арживу безделушку",
    "Fetch Аржив": "Принести Арживу",
    "You have finished the step, Найдите безделушку, , Квест Принеси Аргиву безделушку!": "Вы завершили этап: Найдите безделушку. Квест: Принеси Арживу безделушку!",
    
    # Предметы и интерфейс
    "You опознать the bizarre contraption as an электрический generator.": "Вы опознали странное приспособление как электрический генератор.",
    "You опознать the bizarre contraption as громкоговоритель.": "Вы опознали странное приспособление как громкоговоритель.",
    "Содержит электропроводка enabling it to function as part энергия grid, producing электрический рывок.": "Содержит проводку, позволяющую ему работать как часть энергосети, вырабатывая электрический заряд.",
    "Содержит электропроводка enabling it to function as part энергия grid, consuming электрический рывок.": "Содержит проводку, позволяющую ему работать как часть энергосети, потребляя электрический заряд.",
    "The biped form emerges out кокон wood and fiberglass.": "Двуногая форма появляется из кокона дерева и стекловолокна.",
    "Slide drawers galvanized metal are arranged in a cabinet as rectangles of assorted размеры. Storage is фрактальный.": "Выдвижные ящики из оцинкованного металла расположены в шкафу в виде прямоугольников разных размеров. Хранилище фрактально.",
}

# 2. ПАТТЕРНЫ ДЛЯ "ФРАНКЕНШТЕЙНОВ"
massive_patterns = {
    # Исправление "рывок" в техническом контексте
    r"электрический рывок": "электрический заряд",
    r"producing (?<p>.*?) рывок": "вырабатывая {p} заряд",
    r"consuming (?<p>.*?) рывок": "потребляя {p} заряд",
    
    # Боевой лог
    r"You проводите атаку \((?<w>.*?)\) против opponent\.": "Вы проводите атаку ({w}) против противника.",
    r"If you hit and penetrate, you расчленить one of their limbs at случайный": "Если вы попадете и пробьете броню, вы случайно расчлените одну из конечностей",
    
    # Мутации
    r"Испускает  9-square луч frost in the direction of ваш choice\.": "Испускает ледяной луч длиной 9 клеток в выбранном направлении.",
}

# 3. ПОСЛОВНЫЙ СЛОВАРЬ (Word Dictionary) - Убираем мусор
word_cleanup = {
    "charge": "заряд",
    "Charge": "Рывок",
    "of": " ",
    "the": " ",
    "and": "и",
    "is": " ",
    "are": " ",
    "for": "для",
    "from": "из",
    "with": "с",
    "about": "о",
    "your": "ваш",
    "Your": "Ваш",
    "determines": "определяет",
    "number": "количество",
}

update_any_json('dictionary.json', massive_exact)
update_any_json('dictionary_master.json', massive_exact)
update_any_json('pattern_dictionary.json', massive_patterns)
update_any_json('word_dictionary.json', word_cleanup)

print("Targeted audit fix applied. Dictionaries cleaned based on real gameplay logs.")
