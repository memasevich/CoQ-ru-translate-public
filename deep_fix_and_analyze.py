import json
import os
import re

def update_json(path, updates):
    if not os.path.exists(path): return
    with open(path, 'r', encoding='utf-8') as f:
        data = json.load(f)
    for k, v in updates.items():
        data[k] = v
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)

# 1. Fixing the "charge" issue everywhere
# We need to distinguish between 'Рывок' (skill) and 'Заряд' (item property)
word_updates = {
    "charge": "заряд",
    "Charge": "Рывок", # Usually skill names are capitalized in UI
    "charged": "заряжен",
    "recharge": "перезарядить",
    "recharges": "перезаряжается",
}
update_json('word_dictionary.json', word_updates)

# 2. Fix specific bad translations found in grep
exact_updates = {
    "Maximum charge: ": "Максимальный заряд: ",
    "Maximum charge: </color><color=#77BFCFFF>4000</color><color=#B1C9C3FF>": "Максимальный заряд: </color><color=#77BFCFFF>4000</color><color=#B1C9C3FF>",
    "Drink Charge": "Плата за напитки", # This is actually 'charge' as in 'cost'
}
update_json('dictionary.json', exact_updates)
update_json('dictionary_master.json', exact_updates)

# 3. Analyze all_gameplay_texts.txt (1600 lines)
def analyze_log_deep(file_path):
    with open(file_path, 'r', encoding='utf-8') as f:
        lines = f.readlines()
    
    untranslated = set()
    tag_regex = re.compile(r'<[^>]+>')
    markup_regex = re.compile(r'\{\{.*?\|(.*?)\}\}')
    prefix_regex = re.compile(r'^\[TEXT\]:\s*')
    
    for line in lines:
        original = line.strip()
        if not original: continue
        
        # Clean
        clean = prefix_regex.sub('', original)
        clean = tag_regex.sub('', clean)
        clean = markup_regex.sub(r'\1', clean)
        clean = clean.strip()
        
        # Check for English sentences (not just single words)
        # We look for lines that have 3+ consecutive English words
        if re.search(r'[a-zA-Z]{3,}\s+[a-zA-Z]{3,}\s+[a-zA-Z]{3,}', clean):
            untranslated.add(original)
            
    return untranslated

problematic = analyze_log_deep('C:/Users/Lecoo/AppData/LocalLow/Freehold Games/CavesOfQud/Mods/RussianLocalization/all_gameplay_texts.txt')

print(f"Found {len(problematic)} major untranslated sentences.")
for line in list(problematic)[:100]: # Showing first 100 for brevity in terminal
    print(line)

print("\n--- Summary of 'charge' fix applied ---")
