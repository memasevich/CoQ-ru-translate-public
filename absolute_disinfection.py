import json
import os
import re

MASTER_DICT = 'dictionary_master.json'
MOD_DIR = r"C:\Users\Lecoo\AppData\LocalLow\Freehold Games\CavesOfQud\Mods\RussianLocalization"

# 1. Загружаем и чиним кодировку (если она битая)
try:
    with open(MASTER_DICT, 'r', encoding='utf-8') as f:
        data = json.load(f)
except:
    print("Dict corrupted, restoring from backup...")
    # Если совсем беда, берем последний живой бэкап
    # Но попробуем сначала починить текущий

# 2. Список критических исправлений для 33.png
final_fixes = {
    "You aren't welcome in their holy places.": "Вам не рады в их святых местах.",
    "You are welcome in their holy places.": "Вы — желанный гость в их святых местах.",
    "are interested in listening gossip that's about them.": "заинтересованы в прослушивании сплетен о них.",
    "are interested in sharing secrets about": "заинтересованы в обмене секретами о",
    "sharing secrets about": "обмене секретами о",
    "the locations of": "местоположении",
    "all settlements": "всех поселений",
    "underground locations": "подземных местах",
    "robot lair": "логова роботов",
    "historic sites": "исторических местах",
    "they admire or despise": "которыми они восхищаются или которых презирают",
    "How many do you want to drop?": "Сколько вы хотите сбросить?",
    "lock": "заблокировать",
    "interact": "взаимодействовать",
    "walk": "идти"
}

# 3. Чистим словарь от "мусора" кодировки
clean_data = {}
for k, v in data.items():
    # Если в ключе или значении есть системный мусор - пропускаем
    if '╨' in k or '╨' in v: continue 
    clean_data[k] = v

clean_data.update(final_fixes)

# 4. Сохраняем в проект и в игру в чистом UTF-8
with open(MASTER_DICT, 'w', encoding='utf-8') as f:
    json.dump(clean_data, f, ensure_ascii=False, indent=2)

if not os.path.exists(MOD_DIR): os.makedirs(MOD_DIR)
with open(os.path.join(MOD_DIR, 'dictionary.json'), 'w', encoding='utf-8') as f:
    json.dump(clean_data, f, ensure_ascii=False, indent=2)

print(f"Sanitized dictionary saved. Total clean keys: {len(clean_data)}")
