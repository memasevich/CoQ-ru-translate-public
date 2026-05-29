import re
import json
import os

def extract_problematic_lines(log_path):
    with open(log_path, 'r', encoding='utf-8') as f:
        lines = f.readlines()
    
    # Регулярки
    tag_regex = re.compile(r'<[^>]+>')
    prefix_regex = re.compile(r'^\[TEXT\]:\s*')
    
    unique_problems = set()
    
    for line in lines:
        original = line.strip()
        if not original: continue
        
        # Убираем префикс [TEXT]:
        stripped = prefix_regex.sub('', original)
        # Убираем все теги <color=...>...</color>
        clean = tag_regex.sub('', stripped).strip()
        
        # Если в очищенной строке есть английские слова (3+ буквы)
        if re.search(r'[a-zA-Z]{3,}', clean):
            # Игнорируем технические строки
            if not any(x in clean for x in ['xml', '.txt', 'Copyright', 'memasevich', 'Edition']):
                unique_problems.add(original)
                
    return sorted(list(unique_problems))

# Извлекаем
problems = extract_problematic_lines('C:/Users/Lecoo/AppData/LocalLow/Freehold Games/CavesOfQud/Mods/RussianLocalization/all_gameplay_texts.txt')

with open('PROBLEMS_DETECTED.txt', 'w', encoding='utf-8') as f:
    for p in problems:
        f.write(p + '\n')

print(f"Detected {len(problems)} unique lines with English remains.")
