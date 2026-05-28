# -*- coding: utf-8 -*-
import json
import os

MASTER_DICT = 'dictionary_master.json'
LEXICON_OUT = 'word_dictionary.json'

# Список системных слов Caves of Qud, которые нельзя трогать в пословном словаре
SYSTEM_WORDS = {
    "level", "wound", "name", "displayname", "status", "context", "id", 
    "type", "value", "description", "text", "body", "target", "chance",
    "ma", "dv", "av", "ac", "str", "agi", "int", "wil", "tou", "per"
}

# Список служебных слов английского языка, которые категорически запрещено переводить пословно
DANGEROUS_WORDS = {
    # Артикли
    "a", "an", "the",
    # Предлоги
    "of", "to", "in", "for", "on", "by", "at", "from", "with", "under", "above", "into", "through", "out", "off", "about", "over", "down", "up", "after", "before", "near",
    # Местоимения
    "you", "your", "me", "my", "he", "she", "it", "we", "they", "him", "her", "us", "them", "his", "its", "our", "their", "myself", "yourself", "himself", "herself", "itself", "ourselves", "themselves",
    "who", "what", "which", "this", "that", "these", "those", "whose", "whom", "anyone", "anything", "someone", "something", "everyone", "everything", "noone", "nothing",
    # Союзы
    "and", "but", "or", "nor", "so", "yet", "if", "then", "else", "because", "as", "while", "until", "than", "either", "neither", "both",
    # Вспомогательные и модальные глаголы
    "is", "am", "are", "was", "were", "be", "been", "being", "have", "has", "had", "do", "does", "did",
    "can", "could", "will", "would", "shall", "should", "may", "might", "must", "let",
    # Наречия, частицы, вводные слова
    "here", "there", "now", "only", "just", "more", "less", "some", "any", "no", "not", "very", "too", "also", "well", "already", "still", "even", "never", "always", "often", "sometimes", "ever",
    # Мелкий мусор
    "i", "o", "u", "y", "s", "t", "d", "m", "re", "ve", "ll", "let's", "say", "ask", "get", "set", "make", "take", "go", "come", "see", "look", "want", "use", "find", "give", "tell", "work", "call", "try"
}

def is_safe_word(w):
    w_low = w.lower().strip()
    if len(w_low) < 3:
        return False
    if w_low in DANGEROUS_WORDS:
        return False
    if w_low in SYSTEM_WORDS:
        return False
    # Исключаем строки, содержащие любые символы кроме букв английского алфавита
    if not w_low.isalpha():
        return False
    return True

print("Building safe lexicon...")

with open(MASTER_DICT, 'r', encoding='utf-8') as f:
    data = json.load(f)

word_map = {}

# Загружаем словари с жестким контролем
for eng, rus in data.items():
    # Берем ТОЛЬКО точные пары 1-в-1
    if len(eng.split()) == 1 and len(rus.split()) == 1:
        e = eng.strip()
        r = rus.strip()
        if is_safe_word(e) and not e.isdigit():
            word_map[e.lower()] = r

# Ручные проверенные и гарантированно безопасные слова
manual = {
    "poison": "ядовитый",
    "gas": "газ",
    "blaze": "пламя",
    "fidget": "вибро",
    "cell": "ячейка",
    "broken": "сломанный",
    "rusty": "ржавый",
    "old": "старый",
    "copper": "медный",
    "iron": "железный",
    "steel": "стальной",
    "bronze": "бронзовый",
    "dagger": "кинжал",
    "hammer": "молот",
    "axe": "топор",
    "lead": "свинцовый",
    "salve": "мазь",
    "skulk": "скрытность",
    "injector": "инъектор"
}

# Обновляем
for k, v in manual.items():
    if is_safe_word(k):
        word_map[k.lower()] = v

with open(LEXICON_OUT, 'w', encoding='utf-8') as f:
    json.dump(word_map, f, ensure_ascii=False, indent=2)

print(f"Safe lexicon created in project with {len(word_map)} words.")
