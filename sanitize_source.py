path = 'RussianLocalization.cs'
with open(path, 'r', encoding='utf-8', errors='replace') as f:
    content = f.read()

# Remove anything after the last }
idx = content.rfind('}')
if idx != -1:
    content = content[:idx+1]

# Double check character literals
content = content.replace("HasCharacter('а')", "HasCharacter('\\u0430')")
content = content.replace("HasCharacter('Р°')", "HasCharacter('\\u0430')")
content = content.replace("HasCharacter('╨░')", "HasCharacter('\\u0430')")

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)

print("File cleaned and sanitized.")
