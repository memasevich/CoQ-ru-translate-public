import json
import re

tag_regex = re.compile(r'<[^>]+>')

def analyze_fragments():
    with open('untranslated.txt', 'r', encoding='utf-8-sig', errors='replace') as f:
        lines = f.readlines()
    
    clean_fragments = set()
    for l in lines:
        clean = tag_regex.sub('', l.strip())
        if clean and len(clean) > 3:
            clean_fragments.add(clean)
            
    # Save clean fragments to a file for review
    with open('clean_untranslated.txt', 'w', encoding='utf-8') as f:
        for cf in sorted(list(clean_fragments)):
            f.write(cf + '\n')
            
    print(f"Extracted {len(clean_fragments)} unique clean fragments from untranslated.txt")

if __name__ == "__main__":
    analyze_fragments()
