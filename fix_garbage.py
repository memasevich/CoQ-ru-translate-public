import os

def fix_file(path):
    with open(path, 'r', encoding='utf-8') as f:
        content = f.read()
    
    # Find the last closing brace
    last_brace = content.rfind('}')
    if last_brace != -1:
        # Check if there is garbage after it
        garbage = content[last_brace+1:].strip()
        if garbage:
            print(f"Found garbage in {path}: {garbage}")
            new_content = content[:last_brace+1] + "\n"
            with open(path, 'w', encoding='utf-8') as f:
                f.write(new_content)
            print(f"Fixed {path}")
        else:
            print(f"No garbage found in {path}")

fix_file('RussianLocalization.cs')
fix_file('../RussianLocalization_NoWorkshop/RussianLocalization.cs')
