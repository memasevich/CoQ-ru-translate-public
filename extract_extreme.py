import os
import re
import json

base_path = r"D:\steam\steamapps\common\Caves of Qud\CoQ_Data\StreamingAssets\Base"
output_file = "game_strings_extreme.json"

strings = set()

# Regex for finding things that look like visible text in XML
# 1. Content between tags: <text>Visible Text</text>
tag_content_regex = re.compile(r'>\s*([^<>\n\r]+)\s*<', re.MULTILINE)

# 2. Values of specific attributes: DisplayName="Visible Text"
attr_content_regex = re.compile(r'(?:DisplayName|Description|ShortDescription|Text|Hint|Name|Title)\s*=\s*"([^"]+)"', re.IGNORECASE)

def extract_strings(file_path):
    try:
        with open(file_path, 'r', encoding='utf-8', errors='replace') as f:
            content = f.read()
            
        # Find tag contents
        for match in tag_content_regex.finditer(content):
            s = match.group(1).strip()
            if s and len(s) > 1:
                strings.add(s)
                
        # Find attribute contents
        for match in attr_content_regex.finditer(content):
            s = match.group(1).strip()
            if s and len(s) > 1:
                strings.add(s)
                
    except Exception as e:
        print(f"Error reading {file_path}: {e}")

# Crawl
for root, dirs, files in os.walk(base_path):
    for file in files:
        if file.endswith(".xml") or file.endswith(".txt"):
            extract_strings(os.path.join(root, file))

# Filter out common technical strings or very short strings
filtered = []
for s in strings:
    # Ignore strings that are just numbers, IDs (CamelCaseWithoutSpaces), or technical
    if re.match(r'^[A-Za-z0-9_.]+$', s) and " " not in s:
        # Most game IDs are one word. Real text usually has spaces or special chars.
        # But some item names are one word (e.g. "Sword").
        # Let's keep one-word strings if they are capitalized (likely names).
        if not s[0].isupper():
            continue
    
    # Ignore very technical strings
    if "{" in s and "}" in s and len(s) < 10: continue
    
    filtered.append(s)

# Save
result = sorted(filtered)
with open(output_file, 'w', encoding='utf-8') as f:
    json.dump(result, f, ensure_ascii=False, indent=2)

print(f"Extracted {len(result)} candidate strings to {output_file}")
