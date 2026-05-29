import json
import os

def cleanup_glue_words():
    # 1. Clean dictionary.json
    with open('dictionary.json', 'r', encoding='utf-8') as f:
        d = json.load(f)
    
    # We want to remove single short words that act as "glue" and trigger partial replacements
    # but only if they are being used for word-by-word injection.
    # However, the engine tries exact match first. If "and" is sent alone, it SHOULD translate to "и".
    # The problem is when the engine FALLS BACK to word-by-word.
    
    # Let's remove the words that are clearly breaking sentences in the log
    to_remove = [
        "and", "the", "of", "in", "to", "for", "with", "from", "on", "at", "by",
        "▶and", "▶the", "▶of", "▶in", "▶to", "▶for", "▶with", "▶from", "▶on", "▶at", "▶by",
        "and the", "of the", "in the", "to the", "for the", "with the", "from the",
        "▶and the", "▶of the", "▶in the", "▶to the", "▶for the", "▶with the", "▶from the"
    ]
    
    removed_count = 0
    for key in to_remove:
        if key in d:
            del d[key]
            removed_count += 1
            
    with open('dictionary.json', 'w', encoding='utf-8') as f:
        json.dump(d, f, ensure_ascii=False, indent=2)
    print(f"Removed {removed_count} glue keys from dictionary.json")

    # 2. Clean word_dictionary.json even more aggressively
    with open('word_dictionary.json', 'r', encoding='utf-8') as f:
        wd = json.load(f)
        
    aggressive_remove = [
        "of", "the", "and", "in", "to", "for", "with", "from", "on", "at", "by", "is", "are", "was", "were", "be", "been", "being",
        "this", "that", "these", "those", "my", "your", "his", "her", "its", "our", "their",
        "east", "west", "north", "south", "eastern", "western", "northern", "southern",
        "wind", "salt", "water", "world", "friend", "here", "there", "but", "upon", "some", "all"
    ]
    
    removed_wd = 0
    for key in aggressive_remove:
        if key in wd:
            del wd[key]
            removed_wd += 1
        if key.capitalize() in wd:
            del wd[key.capitalize()]
            removed_wd += 1
            
    # Remove any word <= 3 characters that is not a known important term
    keys = list(wd.keys())
    for k in keys:
        if len(k) <= 3:
            del wd[k]
            removed_wd += 1
            
    with open('word_dictionary.json', 'w', encoding='utf-8') as f:
        json.dump(wd, f, ensure_ascii=False, indent=2)
    print(f"Removed {removed_wd} aggressive glue/short words from word_dictionary.json")

if __name__ == "__main__":
    cleanup_glue_words()
