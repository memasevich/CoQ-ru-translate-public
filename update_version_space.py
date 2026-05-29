import json
import os

def update_json(path, updates):
    if not os.path.exists(path):
        return
    with open(path, 'r', encoding='utf-8') as f:
        data = json.load(f)
    for k, v in updates.items():
        data[k] = v
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    print(f"Updated {path}")

updates = {
    "1.0.4 ": "Переведено memasevich - 1.0.4 ",
    "<color=#B1C9C3FF>1.0.4 ": "<color=#B1C9C3FF>Переведено memasevich - 1.0.4 ",
    "1.0.4": "Переведено memasevich - 1.0.4",
}

update_json('dictionary.json', updates)
update_json('dictionary_master.json', updates)
