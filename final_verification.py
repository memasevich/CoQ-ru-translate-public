import re
import collections

def analyze_file(path, label):
    if not os.path.exists(path):
        print(f"File not found: {path}")
        return
        
    with open(path, 'r', encoding='utf-8', errors='replace') as f:
        lines = f.readlines()
        
    tag_regex = re.compile(r'<[^>]+>')
    
    frankensteins = []
    untranslated = []
    
    for line in lines:
        line = line.strip()
        if not line: continue
        
        # Clean tags for analysis
        clean = tag_regex.sub('', line)
        if label == "word_replacements":
            # word_replacements has [Original]: and [Replaced]:
            if "[Replaced]:" in line:
                clean = line.split("[Replaced]:")[1].strip()
            else:
                continue

        # Detect Frankenstein (Cyrillic + English words longer than 2 chars)
        if re.search(r'[а-яА-Я]', clean) and re.search(r'[a-zA-Z]{3,}', clean):
            # Ignore some known technical terms
            if not any(x in clean for x in ['xml', 'txt', 'Copyright', 'memasevich', 'http', 'Fayumet']):
                frankensteins.append(line)
        
        # Detect pure English sentences (3+ consecutive words)
        if not re.search(r'[а-яА-Я]', clean) and re.search(r'[a-zA-Z]{3,}\s+[a-zA-Z]{3,}\s+[a-zA-Z]{3,}', clean):
            if not any(x in clean for x in ['xml', 'Freehold', 'Copyright']):
                untranslated.append(line)
                
    print(f"\n--- Analysis of {label} ---")
    print(f"Found {len(frankensteins)} mixed-language lines.")
    print(f"Found {len(untranslated)} pure English lines.")
    
    if frankensteins:
        print("\nTop Mixed Lines:")
        for l in frankensteins[:15]: print(f"  {l[:120]}")
        
    if untranslated:
        print("\nTop English Lines:")
        for l in untranslated[:15]: print(f"  {l[:120]}")

import os
log_path = 'C:/Users/Lecoo/AppData/LocalLow/Freehold Games/CavesOfQud/Mods/RussianLocalization/all_gameplay_texts.txt'
word_path = 'C:/Users/Lecoo/AppData/LocalLow/Freehold Games/CavesOfQud/Mods/RussianLocalization/word_replacements.txt'

analyze_file(log_path, "all_gameplay_texts")
analyze_file(word_path, "word_replacements")
