import json
import os
import re

GAME_BASE_DIR = r"D:\steam\steamapps\common\Caves of Qud\CoQ_Data\StreamingAssets\Base"

def apply_new_xml_translations():
    with open('apply_missing_xml.json', 'r', encoding='utf-8') as f:
        new_translations = json.load(f)
    
    # Нормализуем для поиска (уже сделано в apply_missing_xml.json)
    print(f"Applying {len(new_translations)} missing translations to XML...")
    
    files_processed = 0
    replacements_made = 0

    for root, dirs, files in os.walk(GAME_BASE_DIR):
        for file in files:
            if file.endswith('.xml'):
                path = os.path.join(root, file)
                try:
                    with open(path, 'r', encoding='utf-8') as f:
                        content = f.read()
                    
                    def replace_any_match(match):
                        nonlocal replacements_made
                        attr_or_node = match.group(1)
                        val = match.group(2)
                        
                        if val in new_translations:
                            replacements_made += 1
                            return f'{attr_or_node}="\u25b6{new_translations[val]}"'
                        return match.group(0)

                    # Заменяем атрибуты
                    new_content = re.sub(r'(\w+)="([^"]+)"', replace_any_match, content)
                    
                    # Заменяем текстовые узлы
                    def replace_text_node(match):
                        nonlocal replacements_made
                        inner = match.group(2).strip()
                        if inner in new_translations:
                            replacements_made += 1
                            return f'{match.group(1)}>\u25b6{new_translations[inner]}</'
                        return match.group(0)
                        
                    new_content = re.sub(r'(<[^>]+>)([^<]+)</', replace_text_node, new_content)

                    if new_content != content:
                        with open(path, 'w', encoding='utf-8') as f:
                            f.write(new_content)
                        files_processed += 1
                        
                except Exception as e:
                    print(f"Error in {file}: {e}")

    print(f"Finished. Modified {files_processed} files, made {replacements_made} replacements.")

if __name__ == "__main__":
    apply_new_xml_translations()
