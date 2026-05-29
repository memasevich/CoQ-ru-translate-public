import json

def cross_reference_dictionaries():
    with open('dictionary.json', 'r', encoding='utf-8') as f:
        d = json.load(f)
    with open('word_dictionary.json', 'r', encoding='utf-8') as f:
        wd = json.load(f)
    with open('pattern_dictionary.json', 'r', encoding='utf-8') as f:
        p = json.load(f)

    # 1. Overlap between exact phrases and words
    phrase_keys = set(d.keys())
    word_keys = set(wd.keys())
    overlap = phrase_keys.intersection(word_keys)
    
    conflicts = []
    for key in overlap:
        if d[key] != wd[key]:
            conflicts.append(f"CONFLICT: '{key}' is '{d[key]}' in phrases but '{wd[key]}' in words")
    
    # 2. Redundancy (Word exists inside a phrase with same translation)
    # This is actually normal, but if a word in word_dict is very common, it might break phrases.
    
    # 3. Pattern vs Exact match
    # Usually exact matches should take priority, and our C# engine does that.
    
    print(f"Total Phrases: {len(d)}")
    print(f"Total Words: {len(wd)}")
    print(f"Total Patterns: {len(p)}")
    print(f"Overlap count: {len(overlap)}")
    
    if conflicts:
        print("\n--- Found Translation Conflicts ---")
        for c in conflicts[:20]: # Show first 20
            print(c)
    else:
        print("\nNo direct translation conflicts found between word and phrase dictionaries.")

if __name__ == "__main__":
    cross_reference_dictionaries()
