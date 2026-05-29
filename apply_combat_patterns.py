import json
import os

def update_patterns(path, new_patterns):
    if not os.path.exists(path): return
    with open(path, 'r', encoding='utf-8') as f:
        data = json.load(f)
    for k, v in new_patterns.items():
        data[k] = v
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)

def update_exact(path, new_exact):
    if not os.path.exists(path): return
    with open(path, 'r', encoding='utf-8') as f:
        data = json.load(f)
    for k, v in new_exact.items():
        data[k] = v
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)

new_patterns = {
    r"^(?<pref>.*?)(?<pronoun>You|you) fail to penetrate the (?<enemy>.*?)'s armor with (?<your>your) (?<weapon>.*?) \[(?<roll>.*?)\](?<suff>.*?)$": "{pref}Вам не удается пробить броню существа ({enemy}) оружием: {weapon} [{roll}]{suff}",
    r"^(?<pref>.*?)The (?<enemy>.*?) misses (?<pronoun>you) with (?<its>its) (?<weapon>.*?)! \[(?<roll>.*?)\](?<suff>.*?)$": "{pref}{enemy} промахивается по вам ({weapon})! [{roll}]{suff}",
    r"^(?<pref>.*?)(?<pronoun>You|you) take the (?<item>.*?)\.(?<suff>.*?)$": "{pref}Вы берете: {item}.{suff}",
    r"^(?<pref>.*?)(?<pronoun>You|you) extinguish the (?<item>.*?)\.(?<suff>.*?)$": "{pref}Вы тушите: {item}.{suff}",
    r"^(?<pref>.*?)Look \| (?<k1>.*?) \| (?<k2>.*?) lock \| (?<k3>.*?) change selection \| (?<k4>.*?) Interact \| (?<k5>.*?)$": "{pref}Смотреть | {k1} | {k2} Зафиксировать | {k3} Выбор | {k4} Взаимодействовать | {k5}",
    r"^(?<pref>.*?)The most advanced artifact in your possession was (?<item>.*?)\.(?<suff>.*?)$": "{pref}Самым продвинутым артефактом в вашем владении был: {item}.{suff}",
    r"^(?<pref>.*?)You have lost sight of the (?<enemy>.*?)\.(?<suff>.*?)$": "{pref}Вы потеряли из виду существо: {enemy}.{suff}",
    r"^(?<pref>.*?)The (?<enemy>.*?) takes (?<dmg>\d+) damage from (?<your>your) (?<eff>.*?) effect!(?<suff>.*?)$": "{pref}{enemy} получает {dmg} ед. урона от вашего эффекта: {eff}!{suff}",
    r"^(?<pref>.*?)You pour fresh water from (?<your>your) (?<item>.*?) over yourself\.(?<suff>.*?)$": "{pref}Вы обливаете себя свежей водой из: {item}.{suff}"
}

new_exact = {
    "Emits sleep gas to disable enemies": "Выделяет усыпляющий газ, отключающий врагов",
    "Emits sleep gas to disable enemies.": "Выделяет усыпляющий газ, отключающий врагов.",
    "Who ventures into the Great Salt Desert, and nearer the Six Day Stilt?": "Кто осмелится войти в Великую Соляную Пустыню и приблизиться к Шестидневному Столпу?",
    "Anywhile, that's where my wandering mind goes. More of Joppa, say speak": "Впрочем, туда заводят меня мысли. Если хочешь узнать о Джоппе, только скажи.",
    "I tasted a bit of red fleck in the pool. Shale, like we find in the soil by": "Я попробовал немного красной взвеси из лужи. Сланцы, как те, что мы находим в почве возле",
    "Resting until healed...": "Отдых до полного исцеления...",
    "subterranean salt marsh, depth 1, High Salt Sun": "подземное соляное болото, глубина 1, Высокое Соляное Солнце",
    "Apes are interested in learning about the locations of": "Обезьяны заинтересованы в получении информации о местоположении",
    "about the locations of Consortium merchants and all kinds of": "о местоположении торговцев Консорциума и всех видов",
    "despise, and rumors that're about them. ": "ненавидят, и слухи о них.",
    "brine, and blade of grass.": "рассола и травинки."
}

update_patterns('pattern_dictionary.json', new_patterns)
update_exact('dictionary.json', new_exact)
update_exact('dictionary_master.json', new_exact)
