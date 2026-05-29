import os
import xml.etree.ElementTree as ET
import json
import re

base_path = r"D:\steam\steamapps\common\Caves of Qud\CoQ_Data\StreamingAssets\Base"
output_file = "game_strings_extracted.json"

strings = set()

def extract_from_xml(file_path):
    try:
        # Some XMLs might have encoding issues or non-standard characters
        with open(file_path, 'r', encoding='utf-8', errors='replace') as f:
            content = f.read()
        
        # Remove common XML errors or problematic tags if needed
        # Qud XMLs often use {{...}} inside tags which might confuse some parsers, 
        # but ET is usually fine with text content.
        
        root = ET.fromstring(content)
        
        # We look for common tags that contain player-visible text
        # <text>, <description>, <displayname>, <shortdescription>, <hint>
        
        for elem in root.iter():
            if elem.tag in ['text', 'description', 'DisplayName', 'ShortDescription', 'Hint']:
                if elem.text:
                    s = elem.text.strip()
                    if s and not s.startswith("[") and not s.endswith("]"):
                        strings.add(s)
            
            # Attributes can also contain text
            for attr in ['DisplayName', 'ShortDescription', 'Description', 'Text']:
                if attr in elem.attrib:
                    s = elem.attrib[attr].strip()
                    if s:
                        strings.add(s)
                        
    except Exception as e:
        print(f"Error parsing {file_path}: {e}")

# Crawl the directory
for root_dir, dirs, files in os.walk(base_path):
    for file in files:
        if file.endswith(".xml"):
            extract_from_xml(os.path.join(root_dir, file))

# Save results
result = sorted(list(strings))
with open(output_file, 'w', encoding='utf-8') as f:
    json.dump(result, f, ensure_ascii=False, indent=2)

print(f"Extracted {len(result)} unique strings to {output_file}")
