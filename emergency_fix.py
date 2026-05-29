import os

path = 'RussianLocalization.cs'
with open(path, 'r', encoding='utf-8', errors='replace') as f:
    content = f.read()

# 1. Fix the garbage at the end
# Look for the last proper closing brace of the namespace
last_brace = content.rfind('}')
# We need to find the brace before the garbage `n }`n }
# Actually, let's just find the last occurrence of "    }\n}" which should be the end of the namespace
# Wait, let's look at the structure. 
# ...
#     } // end of class
# } // end of namespace

# Let's just find the string "`n    }`n}" and remove it
garbage = "`n    }`n}"
if garbage in content:
    content = content.replace(garbage, "")
    print("Removed garbage from the end.")

# 2. Fix the character literal error
# hasCyrillic = font.HasCharacter('Р°') || font.HasCharacter((char)1072);
# The character 'Р°' is a corrupted 'а'. 
# We should replace it with 'а' or just keep the (char)1072 part.
if "font.HasCharacter('Р°')" in content:
    content = content.replace("font.HasCharacter('Р°')", "font.HasCharacter('а')")
    print("Fixed character literal.")

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
