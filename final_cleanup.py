import json
import os

def final_cleanup():
    # Final check to remove very common words that are still in word_dictionary
    path = 'word_dictionary.json'
    with open(path, 'r', encoding='utf-8') as f:
        wd = json.load(f)
    
    # These words ARE in the logs as "Frankensteins"
    final_remove = [
        "is", "are", "was", "were", "the", "and", "of", "in", "to", "for", "with", "from", "on", "at", "by",
        "It", "The", "And", "In", "To", "For", "With", "From", "On", "At", "By"
    ]
    
    count = 0
    for w in final_remove:
        if w in wd:
            del wd[w]
            count += 1
            
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(wd, f, ensure_ascii=False, indent=2)
    print(f"Removed {count} final problematic words.")

if __name__ == "__main__":
    final_cleanup()
