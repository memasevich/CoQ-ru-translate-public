import re
import sys

def analyze_log(file_path, out_path):
    with open(file_path, 'r', encoding='utf-8') as f:
        lines = f.readlines()
    
    untranslated = set()
    
    # Регулярки для очистки от мусора
    tag_regex = re.compile(r'<[^>]+>')
    markup_regex = re.compile(r'\{\{.*?\|(.*?)\}\}')
    markup_simple_regex = re.compile(r'\{\{.*?\}\}')
    prefix_regex = re.compile(r'^\[TEXT\]:\s*')
    
    for line in lines:
        original_line = line.strip()
        if not original_line: continue
        
        # Очищаем строку
        clean_line = prefix_regex.sub('', original_line)
        clean_line = tag_regex.sub('', clean_line)
        clean_line = markup_regex.sub(r'\1', clean_line)
        clean_line = markup_simple_regex.sub('', clean_line)
        clean_line = clean_line.strip()
        
        # Ищем английские слова (игнорируем hex-коды цветов, если они остались, и системные слова)
        if re.search(r'[A-Za-z]{3,}', clean_line):
            # Игнорируем строки, которые состоят только из системных слов
            if not all(w in ['TEXT', 'Compat', 'SparkingBaetyls', 'PronounSets', 'Relics', 'Colors', 'Naming', 'Mutations', 'ZoneTemplates', 'Quests', 'BuildingTiles', 'Conversations', 'Edition', 'Copyright', 'Freehold', 'Games', 'memasevich', 'PgUp', 'PgDown', 'Esc', 'Space', 'Tab', 'UI', 'ConsoleUI', 'Turn'] for w in re.findall(r'[A-Za-z]+', clean_line)):
                # Игнорируем уже переведенные "франкенштейны"
                if not any(w in original_line for w in ['blood frenzy', 'Meals cooked', 'Slam has no', 'infant child', 'Ctesiphus', 'Qawan', 'Qtafa', 'Resheph', 'was unequipped', 'is unequipped', 'equip', 'misses', 'fail to penetrate']):
                    untranslated.add(original_line)
                
    # Выводим в файл
    with open(out_path, 'w', encoding='utf-8') as f:
        for i, line in enumerate(list(untranslated)[:50]):
            f.write(line + '\n')
        f.write(f"\nВсего найдено строк с английским: {len(untranslated)}\n")

analyze_log('C:/Users/Lecoo/AppData/LocalLow/Freehold Games/CavesOfQud/Mods/RussianLocalization/all_gameplay_texts.txt', 'clean_output.txt')
