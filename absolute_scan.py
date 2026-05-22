import os
import re
import json

GAME_BASE_DIR = r"D:\steam\steamapps\common\Caves of Qud\CoQ_Data\StreamingAssets\Base"
MASTER_DICT = 'dictionary_master.json'

# Список атрибутов, которые МОГУТ содержать человеческий текст
TEXT_ATTRS = {
    'About', 'Accomplishment', 'Achievement', 'Action', 'Background', 'BearerDescription',
    'BiomeAdjective', 'BiomeEpithet', 'Blurb', 'BuyDescription', 'ChargenDescription',
    'ChargenTitle', 'ConsoleDisplayText', 'Context', 'Description', 'DescriptionPrefix',
    'Display', 'DisplayName', 'DisplayText', 'FormalAddressTerm', 'Gospel', 'Gossip',
    'Hagiograph', 'Hint', 'ImmaturePersonTerm', 'Message', 'Name', 'Objective',
    'OffspringTerm', 'ParentTerm', 'PersonTerm', 'ProperName', 'RecipeText', 'SINGULAR',
    'SearchKeywords', 'SingularTitle', 'SingularTitle', 'Snippet', 'TinkerDisplayName',
    'Title', 'WiseAdjective', 'offspringTerm', 'sultanTerm', 'vehicleTerm'
}

def is_likely_text(s):
    if not s: return False
    # Чистим от маркеров
    s = s.replace('\u25b6', '').strip()
    if len(s) < 2: return False
    # Игнорируем пути к файлам и технические коды
    if '/' in s or '\\' in s or '.bmp' in s or '.png' in s: return False
    # Игнорируем чисто числовые значения
    if re.fullmatch(r'[\d\W_]+', s): return False
    # Должны быть английские буквы
    if not re.search(r'[a-zA-Z]', s): return False
    return True

print("Deep scanning ALL XML files for any human-readable text...")

found_strings = set()

for root, dirs, files in os.walk(GAME_BASE_DIR):
    for file in files:
        if file.endswith('.xml'):
            path = os.path.join(root, file)
            try:
                with open(path, 'r', encoding='utf-8', errors='ignore') as f:
                    content = f.read()
                
                # 1. Ищем по всем атрибутам из нашего списка
                for attr in TEXT_ATTRS:
                    pattern = f'\\b{attr}="([^"]+)"'
                    matches = re.findall(pattern, content)
                    for m in matches:
                        if is_likely_text(m):
                            found_strings.add(m.strip())
                            
                # 2. Ищем текст внутри тегов (все теги)
                # Берем всё что между > и <, если там есть буквы
                nodes = re.findall(r'>([^<]+)</', content)
                for n in nodes:
                    if is_likely_text(n):
                        found_strings.add(n.strip())
                        
            except Exception as e:
                print(f"Error reading {file}: {e}")

print(f"Total unique strings found in scan: {len(found_strings)}")

# Сверяем с текущим мастером
with open(MASTER_DICT, 'r', encoding='utf-8') as f:
    master = json.load(f)

missing = {}
for s in found_strings:
    # Очищаем от ▶ для проверки
    clean_s = s.replace('\u25b6', '').strip()
    if clean_s not in master:
        # Пробуем нижний регистр
        if clean_s.lower() not in [k.lower() for k in master.keys()]:
            missing[clean_s] = ""

print(f"Found {len(missing)} NEW unique strings that are not in our dictionary.")

with open('TOTAL_MISSING.json', 'w', encoding='utf-8') as f:
    json.dump(missing, f, ensure_ascii=False, indent=2)

print("Saved missing strings to TOTAL_MISSING.json")
