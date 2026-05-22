import json
import os

MASTER_DICT = 'dictionary_master.json'
LEX_DICT = 'word_dictionary.json'
MOD_DIR = r"C:\Users\Lecoo\AppData\LocalLow\Freehold Games\CavesOfQud\Mods\RussianLocalization"

with open(MASTER_DICT, 'r', encoding='utf-8') as f:
    master = json.load(f)
with open(LEX_DICT, 'r', encoding='utf-8') as f:
    lex = json.load(f)

# 1. Исправляем целые фразы для диалогов
master_updates = {
    "Do you have work that needs doing?": "Есть ли у вас работа, которую нужно сделать?",
    "How many do you want to drop?": "Сколько вы хотите сбросить?",
    "Let's trade.": "Давай поторгуем.",
    "Let's trade": "Давай поторгуем",
    "Live and drink.": "Живи и пей.",
    "live and drink": "живи и пей",
    "*pounds chest*": "*бьет себя в грудь*",
    "*pounds сундук*": "*бьет по сундуку*",
    "1 dram water": "1 драхма воды",
}

# 2. Чистим лексикон (делаем всё строчными, мод сам подправит регистр)
lex_clean = {k.lower(): v.lower() for k, v in lex.items()}

# Добавляем новые атомы
lex_updates = {
    "pounds": "бьет",
    "chest": "грудь",
    "let": "давай",
    "s": "",
    "trade": "торговля",
    "dram": "драхма",
    "water": "вода",
    "work": "работа",
    "needs": "нужно",
    "doing": "сделать"
}
lex_clean.update(lex_updates)

# Сохраняем в проект
with open(MASTER_DICT, 'w', encoding='utf-8') as f:
    json.dump(master, f, ensure_ascii=False, indent=2)
with open(LEX_DICT, 'w', encoding='utf-8') as f:
    json.dump(lex_clean, f, ensure_ascii=False, indent=2)

# Деплоим
with open(os.path.join(MOD_DIR, 'dictionary.json'), 'w', encoding='utf-8') as f:
    json.dump(master, f, ensure_ascii=False, indent=2)
with open(os.path.join(MOD_DIR, 'word_dictionary.json'), 'w', encoding='utf-8') as f:
    json.dump(lex_clean, f, ensure_ascii=False, indent=2)

print(f"Applied fixes for Baboon Queen dialogue. Lexicon cleaned: {len(lex_clean)} words.")
