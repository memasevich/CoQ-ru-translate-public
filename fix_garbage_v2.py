import os

def fix_file_v2(path):
    with open(path, 'r', encoding='utf-8') as f:
        content = f.read()
    
    bad_sequence = "`n"
    if bad_sequence in content:
        print(f"Found bad sequence in {path}")
        # Find the last legitimate '}' before the bad sequence
        idx = content.find(bad_sequence)
        # Truncate at idx
        new_content = content[:idx].strip() + "\n"
        # Ensure it ends with exactly two braces if we truncated too much? No, let's just be precise.
        # The bad sequence I added was literally "`n    }`n}"
        with open(path, 'w', encoding='utf-8') as f:
            f.write(new_content)
        print(f"Fixed {path}")
    else:
        print(f"Bad sequence not found in {path}")

fix_file_v2('RussianLocalization.cs')
fix_file_v2('../RussianLocalization_NoWorkshop/RussianLocalization.cs')
