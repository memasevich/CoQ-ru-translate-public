import sys

file_path = 'RussianLocalization.cs'
with open(file_path, 'r', encoding='utf-8-sig') as f:
    lines = f.readlines()

new_lines = []
skip = False

# We will look for the exactMatch block in TranslateText and add Shadow Matching after it
for i, line in enumerate(lines):
    new_lines.append(line)
    
    # Target: After the dictionary lookup and its normalized fallback in TranslateText
    if 'translatedCore = exactMatch;' in line and 'normalizedKeyDictionary.TryGetValue(sn, out originalKey)' in lines[i-2]:
        # We are inside the else block of the dictionary lookup
        # Let's insert Shadow Matching after the normalizedKeyDictionary check
        
        # We need to find where the normalizedKeyDictionary block ends
        # Looking at previous read_file, it ends with a couple of braces.
        pass

# Actually, it's easier to use string replacement for the whole TranslateText method
# to ensure everything is correct.

with open(file_path, 'r', encoding='utf-8-sig') as f:
    content = f.read()

# Define the Shadow Matching logic block
shadow_matching_code = """
            if (string.IsNullOrEmpty(translatedCore))
            {
                // Shadow Matching: Теневое сопоставление для боевого лога и предметов
                // Отсекаем хвосты вроде "! [18]", "(unburnt)", " x5"
                var shadowRegex = new System.Text.RegularExpressions.Regex(@"(?<core>.+?)(?<deco>\\s*(?:!|\\.|\\?)*\\s*(?:\\[?\\d+(?:\\s+vs\\s+\\d+)?\\]?|\\(unburnt\\)|x\\d+)(?:!|\\.|\\?)*)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var shadowMatch = shadowRegex.Match(trimmedCore);
                if (shadowMatch.Success)
                {
                    string shadowCore = shadowMatch.Groups["core"].Value.Trim();
                    string decoration = shadowMatch.Groups["deco"].Value;
                    string translatedShadowCore;
                    if (staticDictionary.TryGetValue(shadowCore, out translatedShadowCore))
                    {
                        translatedCore = translatedShadowCore + decoration;
                    }
                    else
                    {
                        // Попытка супер-нормализации для ядра тени
                        string ssn = SuperNormalize(shadowCore);
                        string origKey;
                        if (normalizedKeyDictionary.TryGetValue(ssn, out origKey))
                        {
                            if (staticDictionary.TryGetValue(origKey, out translatedShadowCore))
                            {
                                translatedCore = translatedShadowCore + decoration;
                            }
                        }
                    }
                }
            }
"""

# Insert before TryWordReplacement call
target = "if (string.IsNullOrEmpty(translatedCore))\n\n            {\n\n                translatedCore = TryWordReplacement(normalizedCore);"
# Note: the file has double newlines due to previous edits or encoding issues.

if target in content:
    content = content.replace(target, shadow_matching_code + "\n            " + target)
    print("Injected Shadow Matching logic.")
else:
    # Try with single newlines
    target_single = "if (string.IsNullOrEmpty(translatedCore))\n            {\n                translatedCore = TryWordReplacement(normalizedCore);"
    if target_single in content:
        content = content.replace(target_single, shadow_matching_code + "\n            " + target_single)
        print("Injected Shadow Matching logic (single newline).")
    else:
        print("Could not find target for injection.")
        # Debug: print a slice around where we expect it
        idx = content.find("TryWordReplacement")
        if idx != -1:
            print("Context around TryWordReplacement:")
            print(repr(content[idx-100:idx+100]))

with open(file_path, 'w', encoding='utf-8-sig') as f:
    f.write(content)
