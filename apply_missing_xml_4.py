import json
import os

MASTER_DICT = 'dictionary_master.json'
LEX_DICT = 'word_dictionary.json'
MOD_DIR = r"C:\Users\Lecoo\AppData\LocalLow\Freehold Games\CavesOfQud\Mods\RussianLocalization"

with open(MASTER_DICT, 'r', encoding='utf-8') as f:
    master = json.load(f)
with open(LEX_DICT, 'r', encoding='utf-8') as f:
    lex = json.load(f)

# 1. Исправляем целые фразы для Силового браслета
master_updates = {
    "You generate a forcefield around yourself.": "Вы создаете силовое поле вокруг себя.",
    "Creates a 3x3 forcefield centered on yourself.": "Создает силовое поле 3x3 вокруг вас.",
    "Creates a 3x3 forcefield centered on yourself": "Создает силовое поле 3x3 вокруг вас",
    "You may fire missile weapons through the forcefield.": "Вы можете стрелять стрелковым оружием сквозь силовое поле.",
    "High charge draw per round.": "Высокое потребление заряда за раунд.",
    "High charge draw per round": "Высокое потребление заряда за раунд",
}

# 2. Добавляем "атомы" в лексикон для динамических строк
lex_updates = {
    "generate": "создает",
    "forcefield": "силовое поле",
    "yourself": "себя",
    "centered": "центрировано",
    "fire": "стрелять",
    "missile": "стрелковое",
    "weapons": "оружие",
    "through": "сквозь",
    "high": "высокое",
    "charge": "заряда",
    "draw": "потребление",
    "per": "за",
    "round": "раунд",
    "around": "вокруг"
}

master.update(master_updates)
lex.update(lex_updates)

# Сохраняем проект
with open(MASTER_DICT, 'w', encoding='utf-8') as f:
    json.dump(master, f, ensure_ascii=False, indent=2)
with open(LEX_DICT, 'w', encoding='utf-8') as f:
    json.dump(lex, f, ensure_ascii=False, indent=2)

# Деплоим
with open(os.path.join(MOD_DIR, 'dictionary.json'), 'w', encoding='utf-8') as f:
    json.dump(master, f, ensure_ascii=False, indent=2)
with open(os.path.join(MOD_DIR, 'word_dictionary.json'), 'w', encoding='utf-8') as f:
    json.dump(lex, f, ensure_ascii=False, indent=2)

print(f"Forcefield ability fixes applied. Master keys: {len(master)}, Lexicon: {len(lex)}")
