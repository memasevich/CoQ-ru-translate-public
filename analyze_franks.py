import re
import json
import os

def detect_frankensteins(log_path):
    with open(log_path, 'r', encoding='utf-8') as f:
        lines = f.readlines()
    
    frankensteins = []
    tag_regex = re.compile(r'<[^>]+>')
    
    for line in lines:
        stripped = line.strip()
        if not stripped.startswith('[TEXT]:'): continue
        
        content = stripped[7:].strip()
        clean = tag_regex.sub('', content)
        
        # If it has both Cyrillic and English letters
        if re.search(r'[а-яА-Я]', clean) and re.search(r'[a-zA-Z]{2,}', clean):
            frankensteins.append(stripped)
            
    return frankensteins

franks = detect_frankensteins('C:/Users/Lecoo/AppData/LocalLow/Freehold Games/CavesOfQud/Mods/RussianLocalization/all_gameplay_texts.txt')

with open('DETAILED_FRANKS.txt', 'w', encoding='utf-8') as f:
    for fr in franks:
        f.write(fr + '\n')

print(f"Found {len(franks)} frankensteins.")
