import re
import os
import json

def get_problematic_lines(file_path):
    if not os.path.exists(file_path):
        return []
    
    with open(file_path, 'r', encoding='utf-8', errors='replace') as f:
        lines = f.readlines()
    
    tag_regex = re.compile(r'<[^>]+>')
    brace_regex = re.compile(r'{{[^}]+}}')
    
    problems = []
    
    for line in lines:
        original = line.strip()
        if not original: continue
        
        # Determine content to check
        content = original
        if "[TEXT]:" in original:
            content = original.split("[TEXT]:", 1)[1].strip()
        elif "[Replaced]:" in original:
            content = original.split("[Replaced]:", 1)[1].strip()
        elif "[Original]:" in original:
            # We don't want to check originals for "frankensteins", 
            # but they are good for finding untranslated strings.
            content = original.split("[Original]:", 1)[1].strip()
            
        # Clean content for language detection
        clean = tag_regex.sub('', content)
        clean = brace_regex.sub('', clean)
        
        has_cyrillic = bool(re.search(r'[а-яА-Я]', clean))
        has_english = bool(re.search(r'[a-zA-Z]{3,}', clean)) # 3+ letters to ignore short tags or junk
        
        if has_english:
            # Filter out technical stuff we don't translate
            if any(x in clean for x in ['xml', 'txt', 'http', 'Copyright', 'memasevich', 'Fayumet', '2.0.210.24']):
                continue
                
            problems.append({
                'type': 'mixed' if has_cyrillic else 'english',
                'full': content,
                'clean': clean
            })
            
    return problems

log_path = 'C:/Users/Lecoo/AppData/LocalLow/Freehold Games/CavesOfQud/Mods/RussianLocalization/all_gameplay_texts.txt'
word_path = 'C:/Users/Lecoo/AppData/LocalLow/Freehold Games/CavesOfQud/Mods/RussianLocalization/word_replacements.txt'

all_problems = get_problematic_lines(log_path) + get_problematic_lines(word_path)

# Deduplicate
unique_problems = {}
for p in all_problems:
    unique_problems[p['full']] = p

# Save to a file for manual review and batch processing
with open('THOROUGH_AUDIT.txt', 'w', encoding='utf-8') as f:
    for full in sorted(unique_problems.keys()):
        p = unique_problems[full]
        f.write(f"[{p['type'].upper()}]: {full}\n")

print(f"Total unique problematic lines found: {len(unique_problems)}")
