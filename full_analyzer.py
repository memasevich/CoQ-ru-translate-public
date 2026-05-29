import re
import collections

def analyze_full_log(file_path, out_path):
    with open(file_path, 'r', encoding='utf-8') as f:
        lines = f.readlines()
    
    untranslated_lines = collections.defaultdict(int)
    
    tag_regex = re.compile(r'<[^>]+>')
    markup_regex = re.compile(r'\{\{.*?\|(.*?)\}\}')
    markup_simple_regex = re.compile(r'\{\{.*?\}\}')
    prefix_regex = re.compile(r'^\[TEXT\]:\s*')
    
    # Регулярка для поиска английских слов
    english_word_regex = re.compile(r'\b[A-Za-z]{3,}\b')
    
    # Системные слова, которые мы игнорируем
    ignored_words = {
        'TEXT', 'Compat', 'SparkingBaetyls', 'PronounSets', 'Relics', 'Colors', 'Naming', 
        'Mutations', 'ZoneTemplates', 'Quests', 'BuildingTiles', 'Conversations', 'Edition', 
        'Copyright', 'Freehold', 'Games', 'memasevich', 'PgUp', 'PgDown', 'Esc', 'Space', 
        'Tab', 'Enter', 'Back', 'Caps', 'Shift', 'Ctrl', 'Alt', 'Approve', 'Option', 'Options'
    }

    for line in lines:
        original = line.strip()
        if not original: continue
        
        # Очистка
        clean = prefix_regex.sub('', original)
        clean = tag_regex.sub('', clean)
        clean = markup_regex.sub(r'\1', clean)
        clean = markup_simple_regex.sub('', clean)
        clean = clean.strip()
        
        # Поиск слов
        words = english_word_regex.findall(clean)
        
        # Если есть английские слова и это не системный мусор
        has_real_english = False
        for w in words:
            if w not in ignored_words:
                has_real_english = True
                break
        
        if has_real_english:
            untranslated_lines[original] += 1
            
    # Сортируем по частоте появления
    sorted_lines = sorted(untranslated_lines.items(), key=lambda x: x[1], reverse=True)
    
    with open(out_path, 'w', encoding='utf-8') as f:
        f.write(f"=== ОТЧЕТ О НЕПЕРЕВЕДЕННОМ ТЕКСТЕ ===\n")
        f.write(f"Всего уникальных проблемных строк: {len(sorted_lines)}\n\n")
        for line, count in sorted_lines:
            f.write(f"[{count} раз] {line}\n")

    print(f"Анализ завершен. Найдено {len(sorted_lines)} уникальных строк.")
    print(f"Результаты сохранены в файл: {out_path}")
    
    # Выведем в консоль ТОП-30 самых частых проблемных строк
    print("\nТОП-30 проблемных строк:")
    for line, count in sorted_lines[:30]:
        print(f"({count}) {line[:100]}...")

analyze_full_log('C:/Users/Lecoo/AppData/LocalLow/Freehold Games/CavesOfQud/Mods/RussianLocalization/all_gameplay_texts.txt', 'FULL_UNTRANSLATED.txt')
