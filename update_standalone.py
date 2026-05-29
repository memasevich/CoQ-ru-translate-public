import os

file_path = '../RussianLocalization_NoWorkshop/RussianLocalization.cs'

with open(file_path, 'r', encoding='utf-8') as f:
    content = f.read()

# 1. Add LoadResource method before Initialize
load_resource_code = """
        private static string LoadResource(string resourceName)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                string fullResourceName = null;
                foreach (var name in assembly.GetManifestResourceNames())
                {
                    if (name.EndsWith(resourceName))
                    {
                        fullResourceName = name;
                        break;
                    }
                }

                if (fullResourceName == null) return null;

                using (Stream stream = assembly.GetManifestResourceStream(fullResourceName))
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[RussianLocalization] LoadResource Error (" + resourceName + "): " + ex.ToString());
                return null;
            }
        }
"""

if "private static string LoadResource" not in content:
    content = content.replace("public static void Initialize()", load_resource_code + "\n        public static void Initialize()")

# 2. Update Initialize logic for dictionary.json
old_dict_load = """                    // 1. Загрузка основного словаря фраз
                    string dictPath = Path.Combine(modPath, "dictionary.json");
                    if (File.Exists(dictPath))
                    {
                        string jsonText = File.ReadAllText(dictPath, Encoding.UTF8);
                        var dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonText);"""

new_dict_load = """                    // 1. Загрузка основного словаря фраз
                    string dictPath = Path.Combine(modPath, "dictionary.json");
                    string jsonText = File.Exists(dictPath) ? File.ReadAllText(dictPath, Encoding.UTF8) : LoadResource("dictionary.json");
                    if (!string.IsNullOrEmpty(jsonText))
                    {
                        var dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonText);"""

# 3. Update Initialize logic for word_dictionary.json
old_word_load = """                    // 2. Загрузка пословного словаря
                    string wordDictPath = Path.Combine(modPath, "word_dictionary.json");
                    if (File.Exists(wordDictPath))
                    {
                        string wordJsonText = File.ReadAllText(wordDictPath, Encoding.UTF8);
                        var wordDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(wordJsonText);"""

new_word_load = """                    // 2. Загрузка пословного словаря
                    string wordDictPath = Path.Combine(modPath, "word_dictionary.json");
                    string wordJsonText = File.Exists(wordDictPath) ? File.ReadAllText(wordDictPath, Encoding.UTF8) : LoadResource("word_dictionary.json");
                    if (!string.IsNullOrEmpty(wordJsonText))
                    {
                        var wordDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(wordJsonText);"""

# 4. Update Initialize logic for pattern_dictionary.json
old_pattern_load = """                    // 3. Загрузка словаря паттернов (регулярных выражений)
                    string patternDictPath = Path.Combine(modPath, "pattern_dictionary.json");
                    if (File.Exists(patternDictPath))
                    {
                        string patternJsonText = File.ReadAllText(patternDictPath, Encoding.UTF8);
                        var patternDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(patternJsonText);"""

new_pattern_load = """                    // 3. Загрузка словаря паттернов (регулярных выражений)
                    string patternDictPath = Path.Combine(modPath, "pattern_dictionary.json");
                    string patternJsonText = File.Exists(patternDictPath) ? File.ReadAllText(patternDictPath, Encoding.UTF8) : LoadResource("pattern_dictionary.json");
                    if (!string.IsNullOrEmpty(patternJsonText))
                    {
                        var patternDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(patternJsonText);"""

content = content.replace(old_dict_load, new_dict_load)
content = content.replace(old_word_load, new_word_load)
content = content.replace(old_pattern_load, new_pattern_load)

with open(file_path, 'w', encoding='utf-8') as f:
    f.write(content)

print("File updated successfully.")
