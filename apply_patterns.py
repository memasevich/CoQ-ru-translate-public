import json
import os
import re

def update_patterns(path, new_patterns):
    if not os.path.exists(path):
        return
    with open(path, 'r', encoding='utf-8') as f:
        data = json.load(f)
    for k, v in new_patterns.items():
        data[k] = v
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    print(f"Updated patterns in {path}")

def update_exact(path, new_exact):
    if not os.path.exists(path):
        return
    with open(path, 'r', encoding='utf-8') as f:
        data = json.load(f)
    for k, v in new_exact.items():
        data[k] = v
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    print(f"Updated exact in {path}")

new_patterns = {
    r"^(?<subj>You|you) enter a blood frenzy, and for (?<r>\d+) rounds (?<your>your) chance to (?<eff>.*?) with (?<w>.*?) attacks is (?<p>\d+)%\. To use (?<s1>.*?), (?<s2>.*?) must be off cooldown, and using (?<s3>.*?) puts (?<s4>.*?) on cooldown\.$": 
    "{subj} впадаете в кровавое безумие, и на {r} раундов ваш шанс на эффект «{eff}» при атаках ({w}) становится {p}%. Чтобы использовать {s1}, навык {s2} должен быть готов, а использование {s3} отправит {s4} на перезарядку.",
    
    r"^For the next (?<r>\d+) rounds, (?<your>your) chance to (?<eff>.*?) with (?<w>.*?) attacks is (?<p>\d+)% and (?<s1>.*?) has no cooldown\. To use (?<s2>.*?), (?<s3>.*?) must be off cooldown, and using (?<s4>.*?) puts (?<s5>.*?) on cooldown\.$":
    "В течение следующих {r} раундов ваш шанс на эффект «{eff}» при атаках ({w}) становится {p}%, а {s1} не требует перезарядки. Чтобы использовать {s2}, навык {s3} должен быть готов, а использование {s4} отправит {s5} на перезарядку.",

    r"^\{\{rules\|Swarmer: This creature receives \+(?<h>\d+) to hit in melee and \+(?<p>\d+) to penetration rolls for each other hostile swarmer beyond the first who is in another square adjacent to (?<pronoun>his|her|its|their) target\. \(currently \+(?<c>\d+)\)\}\}$":
    "{{rules|Стая: Это существо получает +{h} к попаданию в ближнем бою и +{p} к бронепробитию за каждого другого враждебного члена стаи (кроме первого), находящегося в соседней клетке с целью. (сейчас: +{c})}}",

    r"^(?<pronoun>He|She|It|They) has devoured the infant child\.$":
    "{pronoun} пожирает младенца."
}

new_exact = {
    "Meals cooked from recipes bestow special status effects.": "Блюда, приготовленные по рецептам, дают особые статусные эффекты.",
    "Meals cooked with selected ingredients bestow dynamically-generated status effects.": "Блюда, приготовленные из выбранных ингредиентов, дают динамически генерируемые статусные эффекты.",
    "Meals cooked from recipes bestow special status effects": "Блюда, приготовленные по рецептам, дают особые статусные эффекты",
    "Meals cooked with selected ingredients bestow dynamically-generated status effects": "Блюда, приготовленные из выбранных ингредиентов, дают динамически генерируемые статусные эффекты"
}

update_patterns('pattern_dictionary.json', new_patterns)
update_exact('dictionary.json', new_exact)
update_exact('dictionary_master.json', new_exact)

