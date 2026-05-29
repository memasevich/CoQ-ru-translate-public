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

ui_translations = {
    # Keyboard actions (exact matches for what game sends to TranslateText)
    "collect liquid": "собрать жидкость",
    "fill": "наполнить",
    "seal": "запечатать",
    "chat": "говорить",
    "drink": "пить",
    "pour": "вылить",
    "wake": "проснуться",
    "attack": "атаковать",
    
    # Modern UI
    "show cybernetics": "показать кибернетику",
    "switch to modifications": "перейти к модификациям",
    "Perfect": "Идеально",
    "Back": "Назад",
    "Select": "Выбрать",
    "Options": "Настройки",
    "Exit": "Выход",
    "Weight": "Вес",
    "Lbs.": "фунт.",
    "lbs.": "фунт.",
    "Lbs": "фунт.",
    "lbs": "фунт.",
    
    # Complex UI parts
    "Show cybernetics": "Показать кибернетику",
    "Switch to modifications": "Перейти к модификациям",
    "Offhand Attack Chance": "Шанс атаки второй рукой",
}

# Clean word_dictionary from these UI words to prevent collision
word_removals = [
    "collect", "fill", "seal", "chat", "drink", "pour", "wake", "attack",
    "show", "switch", "perfect", "back", "select", "options", "exit", "weight"
]

if os.path.exists('word_dictionary.json'):
    with open('word_dictionary.json', 'r', encoding='utf-8') as f:
        wd = json.load(f)
    for w in word_removals:
        wd.pop(w, None)
        wd.pop(w.capitalize(), None)
    with open('word_dictionary.json', 'w', encoding='utf-8') as f:
        json.dump(wd, f, ensure_ascii=False, indent=2)

update_json('dictionary.json', ui_translations)
update_json('dictionary_master.json', ui_translations)

# Add patterns for broken hotkeys like [c] c...ollect
ui_patterns = {
    r"^(?<p>\[.\]\s+)[a-zA-Z]$": "{p}", # If it's just "[c] c", remove the extra "c"
    r"(?i)Weight: (?<v>.*?) lbs\.": "Вес: {v} фунт.",
    r"(?i)Weight: (?<v>.*?) lbs": "Вес: {v} фунт.",
    r"^(?<v>\d+)/349 lbs\.$": "{v}/349 фунт.",
}
update_json('pattern_dictionary.json', ui_patterns)

print("Stage 2 (Interface & Buttons) completed.")
