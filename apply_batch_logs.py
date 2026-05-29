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
    # Фракционные статусы
    r"^(?<pref>.*?)don't care about you, but(?<suff>.*?)$": "{pref}не обращают на вас внимания, но{suff}",
    r"^(?<pref>.*?)you, but агрессивный ones(?<suff>.*?)$": "{pref}вас, но агрессивные особи{suff}",
    r"^(?<pref>.*?)care about you, but(?<suff>.*?)$": "{pref}обращают на вас внимание, но{suff}",
    r"^(?<pref>.*?)but агрессивный members(?<suff>.*?)$": "{pref}но агрессивные члены фракции{suff}",
    r"^(?<pref>.*?)ones will атаковать you\.(?<suff>.*?)$": "{pref}особи будут атаковать вас.{suff}",
    r"^(?<pref>.*?)dislikes you, but послушный(?<suff>.*?)$": "{pref}недолюбливают вас, но послушные{suff}",
    r"^(?<pref>.*?)members не будут attack you(?<suff>.*?)$": "{pref}члены фракции не будут атаковать вас{suff}",
    r"^(?<pref>.*?)but docile ones не будут(?<suff>.*?)$": "{pref}но послушные особи не будут{suff}",

    # Боевой лог
    r"^(?<pref>.*?)The (?<enemy>.*?) misses вас с (?<its>its) (?<weapon>.*?)!(?<suff>.*?)$": "{pref}{enemy} промахивается по вам ({weapon})!{suff}",
    r"^(?<pref>.*?)The (?<enemy>.*?) misses you with (?<its>its) (?<weapon>.*?)!(?<suff>.*?)$": "{pref}{enemy} промахивается по вам ({weapon})!{suff}",
    r"^(?<pref>.*?)You hit (?<target>.*?) for (?<dmg>\d+) damage with your (?<weapon>.*?)!(?<suff>.*?)$": "{pref}Вы наносите удар по {target} на {dmg} ед. урона вашим оружием: {weapon}!{suff}",
    r"^(?<pref>.*?)The (?<enemy>.*?) hits (?<roll>.*?) for (?<dmg>\d+) damage with (?<pronoun>her|his|its) (?<weapon>.*?)!(?<suff>.*?)$": "{pref}{enemy} наносит удар {roll} на {dmg} ед. урона ({weapon})!{suff}",
    r"^(?<pref>.*?)The (?<enemy>.*?) hits (?<roll>.*?) for (?<dmg>\d+) damage with (?<pronoun>her|his|its) (?<weapon>.*?)\.(?<suff>.*?)$": "{pref}{enemy} наносит удар {roll} на {dmg} ед. урона ({weapon}).{suff}",

    # Действия и предметы
    r"^(?<pref>.*?)You take (?<item>.*?) from the (?<dir>.*?)\.(?<suff>.*?)$": "{pref}Вы берете {item} ({dir}).{suff}",
    r"^(?<pref>.*?)You take the (?<item>.*?)\.(?<suff>.*?)$": "{pref}Вы берете: {item}.{suff}",
    r"^(?<pref>.*?)You identify the odd trinket as (?<item>.*?)\.(?<suff>.*?)$": "{pref}Вы опознали странную безделушку как: {item}.{suff}",
    r"^(?<pref>.*?)You wade through бассейн of (?<item>.*?)\.(?<suff>.*?)$": "{pref}Вы идете вброд через бассейн с: {item}.{suff}",
    r"^(?<pref>.*?)You stop движущийся because there is a (?<enemy>.*?) in your way\.(?<suff>.*?)$": "{pref}Вы остановились, потому что путь преграждает {enemy}.{suff}",

    # Интерфейс и статусы
    r"^(?<pref>.*?)ACTIVE Эффекты:(?<suff>.*?)$": "{pref}АКТИВНЫЕ Эффекты:{suff}",
    r"^(?<pref>.*?)Last saved: (?<date>.*?)$": "{pref}Последнее сохранение: {date}",
    r"^(?<pref>.*?)Последнее сохранение: (?<day>.*?), (?<d>\d+) (?<m>.*?) (?<y>\d+) at (?<t>.*?)$": "{pref}Последнее сохранение: {day}, {d} {m} {y} в {t}",
    
    # Ремонт слов с разбитыми тегами (p-a-i-n-t-e-d подушка)
    r"^(?<pref>.*?)p(?<c1>.*?)a(?<c2>.*?)i(?<c3>.*?)n(?<c4>.*?)t(?<c5>.*?)e(?<c6>.*?)d(?<suff>.*?)$": "{pref}расписная{suff}",
    r"^(?<pref>.*?)e(?<c1>.*?)n(?<c2>.*?)g(?<c3>.*?)r(?<c4>.*?)a(?<c5>.*?)v(?<c6>.*?)e(?<c7>.*?)d(?<suff>.*?)$": "{pref}гравированная{suff}"
}

new_exact = {
    "I am called Fayumet.": "Меня зовут Fayumet.",
    "The влагопаутинник farmer wakes up.": "Фермер влагопаутинника просыпается.",
    "You pass by some мусор.": "Вы прошли мимо кучи мусора.",
    "You pass by some лестница up.": "Вы прошли мимо лестницы вверх.",
    "You pass by some лестница down.": "Вы прошли мимо лестницы вниз.",
    "You pass by some watervine.": "Вы прошли мимо влагопаутинника.",
    "You pass by some brinestalk.": "Вы прошли мимо рассолостебля.",
    "The oasis-hamlet. 'Neath the shelf of the world. A million breaths of": "Оазис-деревня. Под уступом мира. Миллион вздохов",
    "salt the ветер heaves over the Отлично Salt Desert Moghra'yi. And to the восток,": "соли, которую ветер несет над Великой Соляной Пустыней Могра-йи. А на востоке,",
    "the rotting jungles of Qud.": "гниющие джунгли Куда.",
    "Live and пить. Come in- come sit 'neath the cool тень, 'cross a pillow": "Живи и пей. Заходи, присядь в прохладной тени на подушку",
    "there. And welcome to Джоппа.": "здесь. И добро пожаловать в Джоппу.",
    "Witchwood for pain, bandages for кровотечение, yuckwheat and мёд for when your": "Ведьмин лес от боли, бинты от кровотечения, противная пшеница и мёд для",
    "guts act up. Coldcaps when I can find them. Why not take a look?": "вашего желудка. Ледяные грибы, когда удается их найти. Почему бы не взглянуть?",
    "Fates forfend. I would hardly like to свинцовый поселение and run a shop at the": "Судьба упаси. Я бы вряд ли захотел возглавить поселение и управлять лавкой",
    "same time.": "одновременно.",
    "Adds simple taste-based эффекты to cooked meals.": "Добавляет простые вкусовые эффекты к приготовленным блюдам.",
    "You need драм of either blood -или- гниение to осквернить святыня. ": "Вам нужна порция крови или гнили, чтобы осквернить святыню."
}

update_patterns('pattern_dictionary.json', new_patterns)
update_exact('dictionary.json', new_exact)
update_exact('dictionary_master.json', new_exact)

print("Batch processing of logs completed.")
