import json
import os

def sanitize_string(s):
    if not isinstance(s, str): return s
    # If the string contains the 'РЎ' artifact, it's a sign of double encoding
    # D0 A1 in CP1251 -> UTF-8 is 'РЎ'
    # We want to revert it to 'С'
    try:
        # This is a hack to fix the "garbage" Russian text in the dictionary
        # if it was accidentally saved as UTF-8 but treated as CP1251 or vice versa
        b = s.encode('cp1251', errors='ignore')
        return b.decode('utf-8')
    except:
        return s

def fix_dictionary(path):
    if not os.path.exists(path): return
    with open(path, 'r', encoding='utf-8-sig') as f:
        data = json.load(f)
    
    new_data = {}
    for k, v in data.items():
        # Check if value looks like the garbage in the log
        if 'Р' in v and len(v) > 5:
            v = sanitize_string(v)
        new_data[k] = v
        
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(new_data, f, ensure_ascii=False, indent=2)

fix_dictionary('dictionary.json')
fix_dictionary('dictionary_master.json')
print("Dictionary sanitized.")
