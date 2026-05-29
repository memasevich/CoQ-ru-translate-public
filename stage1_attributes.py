import json
import os

def update_json(path, updates):
    if not os.path.exists(path): return
    with open(path, 'r', encoding='utf-8') as f:
        data = json.load(f)
    for k, v in updates.items():
        data[k] = v
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)

attribute_translations = {
    # Strength
    "Your Strength determines how much melee damage you do (improving your armor penetration), your ability to resist forced movement, and your carry capacity": "Ваша Сила определяет, сколько урона вы наносите в ближнем бою (улучшая пробитие брони), вашу способность сопротивляться принудительному перемещению и переносимый вес",
    
    # Intelligence
    "Your Intelligence determines your number of skill points and your ability to examine artifacts.": "Ваш Интеллект определяет количество ваших очков навыков и вашу способность исследовать артефакты.",
    
    # Willpower
    "Your Willpower modifies the cooldowns of your activated abilities, determines your ability to resist mental attacks, and modifies your hit point regeneration rate.": "Ваша Сила воли влияет на время восстановления ваших способностей, определяет вашу способность сопротивляться ментальным атакам и изменяет скорость восстановления здоровья.",
    
    # Ego
    "Your Ego determines the potency of your mental mutations, your ability to haggle with merchants, and your ability to dominate the wills of other living creatures.": "Ваше Эго определяет силу ваших ментальных мутаций, вашу способность торговаться с торговцами и вашу способность подчинять волю других живых существ.",
    
    # Toughness
    "Your Toughness determines your number of hit points, your hit point regeneration rate, and your ability to resist poison and disease.": "Ваша Стойкость определяет количество ваших очков здоровья, скорость восстановления здоровья и вашу сопротивляемость ядам и болезням.",
    
    # Agility
    "Your Agility determines your accuracy with melee and ranged weapons, as well as your chance to dodge attacks.": "Ваша Ловкость определяет вашу точность обращения с оружием ближнего и дальнего боя, а также ваш шанс уклониться от атак.",
    
    # AV / DV / MA (Just in case they are not caught)
    "Your Armor Value (AV) determines how well-protected you are from being hit by physical attacks. The higher the score, the less likely an opponent's attack will penetrate your armor and deal damage. Your base AV is 0.": "Ваш показатель брони (AV) определяет степень вашей защиты от попаданий физических атак. Чем выше этот показатель, тем меньше вероятность того, что атака противника пробьет вашу броню и нанесет урон. Ваша базовая AV равна 0.",
    "Your dodge value (DV) is a measurement of how likely you are to be hit by physical attacks. The higher your score, the less likely an opponent's attack will hit you. Your DV is modified by your Agility modifier. Most characters start with a base DV of 6.": "Ваш показатель уклонения (DV) — это мера вероятности того, что по вам попадут физической атакой. Чем выше показатель, тем меньше шансов у противника попасть по вам. УК модифицируется вашей Ловкостью. Базовое УК обычно равно 6.",
    "Your mental armor (MA) determines how well-protected you are from mental attacks. The higher the score, the less likely an opponent's mental attack will penetrate your defense and deal damage. Your MA is modified by your Willpower modifier. Your base MA is 4.": "Ваша ментальная броня (МА) определяет степень вашей защиты от ментальных атак. Чем выше этот показатель, тем меньше вероятность того, что ментальная атака противника пробьет вашу защиту и нанесет вам вред. Ваша МА изменяется модификатором Силы воли. Ваша базовая МА равна 4."
}

update_json('dictionary.json', attribute_translations)
update_json('dictionary_master.json', attribute_translations)

print("Stage 1 (Attributes) exact strings added.")
