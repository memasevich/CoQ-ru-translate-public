import json
import re
from collections import Counter

MASTER_DICT = 'dictionary_master.json' # Currently 23k baseline
LEXICON_OUT = 'word_dictionary.json'

# Системные слова CoQ которые НЕЛЬЗЯ трогать по словам
SYSTEM_WORDS = {
    "level", "wound", "name", "displayname", "status", "context", "id", 
    "type", "value", "description", "text", "body", "target", "chance",
    "ma", "dv", "av", "ac", "str", "agi", "int", "wil", "tou", "per"
}

def get_words(text):
    # Убираем все теги и переменные перед анализом
    clean = re.sub(r'\{.*?\}|<.*?>|=.*?=|%.*?%|&[a-zA-Z]|\^[a-zA-Z]', ' ', text)
    return re.findall(r'\b[a-zA-Z]{3,}\b', clean) # Только слова от 3 букв

print("Building safe lexicon...")

with open(MASTER_DICT, 'r', encoding='utf-8') as f:
    data = json.load(f)

word_map = {}

for eng, rus in data.items():
    # Ищем только короткие соответствия для маппинга
    if len(eng.split()) == 1 and len(rus.split()) == 1:
        e = eng.lower()
        if e not in SYSTEM_WORDS and not e.isdigit():
            word_map[e] = rus

# Добавляем частые слова из описаний (ручной контроль)
manual = {
    "copper": "медный", "iron": "железный", "steel": "стальной",
    "dagger": "кинжал", "hammer": "молот", "axe": "топор",
    "poison": "ядовитый", "gas": "газ", "blaze": "пламя",
    "lead": "свинец", "fidget": "вибро", "cell": "ячейка",
    "broken": "сломанный", "rusty": "ржавый", "old": "старый"
}
word_map.update(manual)

with open(LEXICON_OUT, 'w', encoding='utf-8') as f:
    json.dump(word_map, f, ensure_ascii=False, indent=2)

print(f"Safe lexicon created with {len(word_map)} words.")
