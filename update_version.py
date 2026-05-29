import json
import os

def update_json(path, key, value):
    if not os.path.exists(path):
        print(f"File {path} not found")
        return
    with open(path, 'r', encoding='utf-8') as f:
        data = json.load(f)
    data[key] = value
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    print(f"Updated {path}")

# Update dictionaries
update_json('dictionary.json', "1.0.4", "Переведено memasevich - 1.0.4")
update_json('dictionary_master.json', "1.0.4", "Переведено memasevich - 1.0.4")

# Update manifest.json
def update_manifest(path):
    if not os.path.exists(path): return
    with open(path, 'r', encoding='utf-8') as f:
        data = json.load(f)
    data["Version"] = "1.0.4"
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    print(f"Updated manifest version to 1.0.4 in {path}")

update_manifest('manifest.json')
update_manifest('../RussianLocalization_NoWorkshop/manifest.json')
