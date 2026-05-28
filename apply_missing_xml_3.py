import json
import os

MASTER_DICT = 'dictionary_master.json'
MOD_DIR = r"C:\Users\Lecoo\AppData\LocalLow\Freehold Games\CavesOfQud\Mods\RussianLocalization"

with open(MASTER_DICT, 'r', encoding='utf-8') as f:
    master = json.load(f)

# Добавляем все куски, которые выпали на скриншоте 33.png
updates = {
    # Фрагменты описания бабуина (проверенные из XML)
    "Wet nostrils flare at the seat of": "Влажные ноздри раздуваются у основания",
    "dropped snout": "опущенной морды",
    "and furry arms wave in": "а мохнатые руки машут в",
    "unpredicted motions": "непредсказуемом движении",
    "Her eyes are full of": "Ее глаза полны",
    "stony mischief": "каменного озорства",
    "eyes are full of stony mischief": "глаза полны каменного озорства",
    
    # Системные надписи Лупы
    "Base demeanor:": "Базовое поведение:",
    "Physical features:": "Физ. особенности:",
    "docile": "послушный",
    "vicious": "злобный",
    "small": "маленький",
    "Equipped:": "Экипировано:",
    
    # Кнопки и способности (фикс винегрета)
    "Make Camp": "Разбить лагерь",
    "Make": "Разбить",
    "Camp": "лагерь",
    "Rebuke Robot": "Упрек робота",
    "Rebuke": "Упрек",
    "Robot": "робота",
    "Force Bracelet": "Силовой браслет",
    "Force": "Силовой",
    "Bracelet": "браслет",
    "lock": "заблокировать",
    
    # Дополнительно из скриншота
    "unpredicted": "непредсказуемый",
    "motions": "движения",
    "mischief": "озорство",
    "vicious укус": "злобный укус",
    "small валун": "маленький валун"
}

master.update(updates)

# Сохраняем и деплоим
with open(MASTER_DICT, 'w', encoding='utf-8') as f:
    json.dump(master, f, ensure_ascii=False, indent=2)
with open(os.path.join(MOD_DIR, 'dictionary.json'), 'w', encoding='utf-8') as f:
    json.dump(master, f, ensure_ascii=False, indent=2)

print(f"Added {len(updates)} specific fragments to dictionary.")
