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

big_patch = {
    # --- TAM (DROMAD) ---
    "It is a pleasure to know this, human friend Fayumet! I am Tam.": "Приятно познакомиться, человек-друг Fayumet! Я Тэм.",
    "I am a dromad, human friend. Some say saltstrider. Do you know this?": "Я дромадер, человек-друг. Некоторые называют нас солеходами. Знаешь о таких?",
    "My people have walked the salt for thousands of years, meeting every creature that lives and thinks. From the Pale Sea to the marsh of Joppa, and under the hanging hills, our chests were pressed to": "Мой народ ходил по солям тысячи лет, встречая каждое живое и мыслящее существо. От Бледного моря до болот Джоппы, и под висячими холмами, наша грудь была прижата к",
    "the camelfolk. His ears are arrayed with gilded rings, and eyelashes flow down his face like watervine fronds. Across his back, a hump moistured and healthy fat pushes his center mass skyward.": "верблюжьего народа. Его уши украшены позолоченными кольцами, а ресницы спадают на его лицо, как ветви влагопаутинника. На его спине горб, полный влаги и здорового жира, возносит его центр масс к небу.",
    "Shot ink decorates the dulla on his long neck in one thousand patterns of the camelfolk.": "Чернила украшают дуллу на его длинной шее тысячью узоров верблюжьего народа.",
    
    # --- ARGYVE (TINKER) ---
    "There are caves everywhere, you dolt! Scoop the surface, marsh, or head all": "Пещеры повсюду, болван! Обыщи поверхность, болото или все вершины",
    "I journey North, to the Six Day Stilt.": "Я отправляюсь на север, к Шестидневному Столпу.",
    "Fetch Argyve a Knick-knack!": "Принеси Арживу безделушку!",
    "Wait! My name is =name=. I've come into possession of a data disk stamped with a peculiar sigil. Does this have meaning to your order of tinkers? Might you examine it?": "Подождите! Меня зовут =name=. Ко мне попал диск с данными, помеченный странным символом. Имеет ли это значение для вашего ордена изобретателей? Не могли бы вы его изучить?",
    "Be gone, wayfarer. This is no place for you.": "Уходи, путник. Тебе здесь не место.",
    "I come by way of Joppa. The elder Irudad calls me friend.": "Я пришел из Джоппы. Старейшина Ирудад называет меня другом.",

    # --- CTESIPHUS (CAT) ---
    "One star-ribboned night, a cat crossed the marshy loam and wandered into Joppa. The watervine farmers, wakened by the joyful cries of a small girl, gathered around to revel at the favorable omen and extol the generosity of the Beetle Moon. Since then, Ctesiphus has spent his days curled under the shade of brinestalk huts and sauntering over dirt paths. So long as he's approached with care, he welcomes the hands of friends and strangers alike.": "Одной звездной ночью кот пересек болотистую почву и забрел в Джоппу. Фермеры, разбуженные радостными криками маленькой девочки, собрались вокруг, чтобы порадоваться благоприятному предзнаменованию и восславить щедрость Жучиной Луны. С тех пор Ктесифий проводит свои дни, свернувшись калачиком в тени хижин из соляного стебля и прогуливаясь по грунтовым тропам. Пока к нему приближаются с осторожностью, он приветствует руки как друзей, так и незнакомцев.",

    # --- WARDEN YRAME ---
    "Big Yrame shifts her weight between hind and forelegs. She shakes a storm of rust dander from the tines of her great horns, and she blows a halo of freezing air from her rime-veined hands.": "Большая Ирам переминается с задних ног на передние. Она стряхивает бурю ржавой перхоти с зубцов своих огромных рогов и выдыхает ореол морозного воздуха из своих покрытых инеем рук.",

    # --- NIMA RUDA ---
    "Sharp jade eyes are crow-worn to late middle age and her lips are scarred with gas burns. Climbing over her back and neck are the jeweled segments of a great spiral tail and stinger; a bead of smoking gut juice gleams on the tip.": "Острые нефритовые глаза тронуты морщинами среднего возраста, а её губы в шрамах от газовых ожогов. По её спине и шее поднимаются украшенные драгоценными камнями сегменты большого спирального хвоста с жалом; на кончике блестит капля дымящегося кишечного сока.",

    # --- GENERAL SYSTEM & UI ---
    "You make some progress understanding the bizarre contraption.": "Вы немного продвинулись в понимании этого странного приспособления.",
    "The biped form emerges out of a cocoon of wood and fiberglass.": "Двуногая форма появляется из кокона из дерева и стекловолокна.",
    "Slide drawers of galvanized metal are arranged in a cabinet as rectangles of assorted sizes. Storage is fractal.": "Выдвижные ящики из оцинкованного металла расположены в шкафу в виде прямоугольников разных размеров. Хранилище фрактально.",
    "It reads, 'Common Mill'.": "Там написано: 'Общая мельница'.",
    "It reads, 'Salt Sundries'.": "Там написано: 'Соляные товары'.",
    "Physical features: stinger": "Физические особенности: жало",
    "Physical features: horns": "Физические особенности: рога",
    "Equipped: sturdy woven tunic, wide-brimmed hat, iron vinereaper, sandals": "Экипировано: прочная тканая туника, широкополая шляпа, железный сборщик лозы, сандалии",
    "Equipped: black robes, spectacles, walking stick, sandals": "Экипировано: черное одеяние, очки, трость, сандалии",
    "Equipped: woven tunic, wide-brimmed hat, iron vinereaper, sandals": "Экипировано: тканая туника, широкополая шляпа, железный сборщик лозы, сандалии",
}

# REPUTATION & INTEREST FRAGMENTS (The "Glue")
fragments = {
    "Interested in sharing secrets about ": "заинтересованы в обмене секретами о ",
    "Interested in learning about the ": "заинтересованы в изучении ",
    "Interested in hearing ": "заинтересованы в прослушивании ",
    "They're also interested in hearing gossip that's about them.": "Они также заинтересованы в слухах о себе.",
    "They're also interested in ": "Они также заинтересованы в ",
    "locations of ": "расположении ",
    "location of ": "расположении ",
    "settlements and ": "поселений и ",
    "sultans they admire or despise": "султанах, которыми они восхищаются или которых презирают",
    "technology, and ": "технологиях, и ",
}

update_json('dictionary.json', big_patch)
update_json('dictionary.json', fragments)
update_json('dictionary_master.json', big_patch)
update_json('dictionary_master.json', fragments)

print("Massive NPC and Fragment Patch applied.")
