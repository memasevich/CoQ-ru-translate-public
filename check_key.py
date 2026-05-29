import json

key = "-here? Speak to my daughter through the east door, for herbs. And sitting Tam in the southeast has all manner of trinket, against his chests o' drawers."

with open('dictionary.json', 'r', encoding='utf-8') as f:
    d = json.load(f)

print(f"Key exists in dictionary: {key in d}")
if key in d:
    print(f"Translation: {d[key]}")
else:
    # Try with leading/trailing spaces
    print(f"Key with leading spaces exists: {' ' + key in d}")
    print(f"Key with tab exists: {'\t' + key in d}")
    
    # List keys containing part of the string
    print("\nKeys containing 'Speak to my daughter':")
    for k in d:
        if "Speak to my daughter" in k:
            print(f"[{repr(k)}]")
