import json

def update_patterns():
    path = 'pattern_dictionary.json'
    with open(path, 'r', encoding='utf-8') as f:
        p = json.load(f)

    # Добавляем мощные паттерны для боевого лога и переменных
    
    # 1. Базовые атаки и урон (фрагменты)
    p[r"(?i)for (?<dmg>\d+) damage with (?:your|her|his|its|their)"] = "на {dmg} ед. урона своим(и)"
    p[r"(?i)hits for (?<dmg>\d+) damage"] = "наносит {dmg} ед. урона"
    p[r"(?i)with (?:your|her|his|its|their) (?<weapon>.+?)! \[(?<roll>.+?)\]"] = "своим(и) {weapon}! [{roll}]"
    p[r"(?i)with (?:your|her|his|its|their) (?<weapon>.+?)\. \[(?<roll>.+?)\]"] = "своим(и) {weapon}. [{roll}]"
    
    # 2. Промахи
    p[r"(?i)misses with (?:your|her|his|its|their) (?<weapon>.+?)! \[(?<roll>.+?)\]"] = "промахивается своим(и) {weapon}! [{roll}]"
    p[r"(?i)You miss with your (?<weapon>.+?)! \[(?<roll>.+?)\]"] = "Вы промахиваетесь своим(и) {weapon}! [{roll}]"
    
    # 3. Смерть и статусы
    p[r"(?i)The (?<enemy>.+?) dies!?"] = "{enemy} умирает!"
    p[r"(?i)You stop moving because there is a (?<enemy>.+?) in your way"] = "Вы остановились, потому что {enemy} преграждает путь"
    
    # 4. Предметы и количество
    p[r"(?i)^(?<item>.+?) x(?<count>\d+)(?: \((?<state>.+?)\))?$"] = "{item} x{count} ({state})"
    p[r"(?i)^(?<val>\d+) lbs\.$"] = "{val} фунт."
    
    # 5. Опыт и квесты
    p[r"(?i)You gain (?<exp>\d+) experience!"] = "Вы получаете {exp} опыта!"
    p[r"(?i)You have completed the quest: (?<quest>.+?)\."] = "Вы выполнили квест: {quest}."
    
    # 6. Улучшаем паттерны интересов (делаем их короче и надежнее)
    p[r"(?i)(?:are|is) interested in (?:trading|sharing) secrets about (?:the )?(?<topic>.+)"] = "заинтересованы в обмене секретами о {topic}"
    p[r"(?i)(?:are|is) interested in learning about (?:the )?(?<topic>.+)"] = "заинтересованы в получении сведений о {topic}"
    p[r"(?i)They're also interested in (?<topic>.+)"] = "Им также интересно: {topic}"

    with open(path, 'w', encoding='utf-8') as f:
        json.dump(p, f, ensure_ascii=False, indent=2)
    
    print("Pattern dictionary updated with Master Combat Patterns.")

if __name__ == "__main__":
    update_patterns()
