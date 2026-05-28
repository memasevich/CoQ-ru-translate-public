import json

MASTER_DICT = 'dictionary_master.json'

with open(MASTER_DICT, 'r', encoding='utf-8') as f:
    master = json.load(f)

# Блок 19: Крупный перевод диалогов и лора из XML (200 строк)
batch19 = {
    "Be gone, wayfarer. This is no place for you.": "Уходи, путник. Тебе здесь не место.",
    "Let them draw together the bones of the metal.": "Пусть они соберут воедино кости металла.",
    "Rain flakes of gold on the water": "Дождь золотых хлопьев на воде",
    "Azure and flaking silver of water,": "Лазурь и шелушащееся серебро воды,",
    "Pallor of silver, pale lustre of Latona,": "Бледность серебра, тусклый блеск Латоны,",
    "By these, from the malevolence of the dew": "Этим, от злобы росы",
    "Guard this alembic.": "Охраняй этот алембик.",
    "Quiet this metal.&quot;": "Утихомирь этот металл.&quot;",
    "=pronouns.Subjective= =verb:have:afterpronoun= devoured the infant child.": "=pronouns.Subjective= =verb:have:afterpronoun= пожрал младенца.",
    "The infant child is unaware": "Младенец не ведает",
    "=pronouns.Subjective= =verb:have:afterpronoun= been eaten by the bear.&quot;": "=pronouns.Subjective= =verb:have:afterpronoun= был съеден медведем.&quot;",
    "'Founder' the query. 'Founder' is water-taking, no? To be breached and sink. How cruel the judgment.": "«Основатель» — таков запрос. «Основатель» — значит набирать воду, верно? Быть пробитым и затонуть. Как суров приговор.",
    "Hoh hoh. This voice japes -- we, witness to the birth of Yd. The query, no?": "Хо-хо. Этот голос шутит — мы, свидетели рождения Ида. Запрос, нет?",
    "'Parasite' so negative. It's like... ah... economic symbiosis?": "«Паразит» — звучит так негативно. Это скорее... ах... экономический симбиоз?",
    "Chavvah sustains me, and we are, I am not... them. We are in a... beneficial arrangement, you know. Yes?": "Хавва поддерживает меня, и мы, я не... они. Мы в... взаимовыгодных отношениях, понимаешь. Да?",
    "'Tis a saying among the stonemasons, \"Never finish a job too early.\"": "У каменщиков есть поговорка: «Никогда не заканчивай работу слишком рано».",
    "Bless the Coiled Lamb, peace to those who canter in his house, and let us pray the day never comes!": "Благослови Свернутого Агнца, мир тем, кто скачет в его доме, и помолимся, чтобы этот день никогда не настал!",
    "*CreatureTypeCap* Heretic": "*CreatureTypeCap*-еретик",
    "*CreatureTypeCap* Iconoclast": "*CreatureTypeCap*-иконоборец",
    "*CreatureTypeCap* Pariah": "*CreatureTypeCap*-пария",
    "+1 to all mutation levels": "+1 ко всем уровням мутаций",
    "+6 to cybernetics license tier": "+6 к уровню лицензии на кибернетику",
    "Come see how the slynth have become a part of Joppa!": "Приходи посмотреть, как слинты стали частью Йоппы!",
    "others are welcome to grow and flourish here in our marshes.": "другие могут расти и процветать здесь, в наших болотах.",
    "History is thick as the high salt sun, =player.formalAddressTerm=, and where the past is ground up like matz meal, a mix of life sets in.": "История густа, как высокое соленое солнце, =player.formalAddressTerm=, и там, где прошлое перетерто, как мацовая мука, зарождается новая жизнь.",
    "The farmers are plagued by cave vermin.": "Фермеры страдают от пещерных вредителей.",
    "Always looking for trinkets to wire between, heh.": "Всегда ищет побрякушки, чтобы соединить их проводами, хех.",
    "Girsh titans who eat the young of kith and kin.": "Титаны Гирш, пожирающие детей сородичей и близких.",
    "Sultan Resheph drove them under the earth, but do they stir??": "Султан Решеф загнал их под землю, но не зашевелились ли они??",
    "your luminous friends are settling in quite well.": "ваши светящиеся друзья обустраиваются вполне неплохо.",
    "Some farmers show some small discomfort but that will pass in time.": "Некоторые фермеры проявляют небольшое беспокойство, но со временем это пройдет.",
    "Come in- come sit 'neath the cool shade, 'cross a pillow there.~": "Заходи, присядь в прохладной тени на подушку.~",
    "Live and drink, =name=. Come, sit with me.": "Живи и пей, =name=. Присядь со мной.",
    "Live and drink. Come in- come sit 'neath the cool shade, 'cross a pillow there. And welcome to Joppa.": "Живи и пей. Заходи, присядь в прохладной тени на подушку. Добро пожаловать в Йоппу.",
    "You may drink of our freshwater, too, and quench your thirst.": "Ты можешь испить нашей пресной воды и утолить жажду.",
    "Your finding is rich in value to us.": "Твоя находка очень ценна для нас.",
    "We are poor farmers, and sharpen our vinereapers is all we can do.": "Мы бедные фермеры, и всё, что мы можем — это точить наши серпы.",
    "Take these prickly-boons as thanks.": "Возьми эти колючие дары в знак благодарности.",
    "Leave me now to muse, kindly...": "Оставь меня теперь для раздумий, будь добр...",
    "A girshling? This is a girshling.": "Гиршлинг? Это гиршлинг.",
    "The oasis-hamlet. 'Neath the shelf of the world.": "Оазис-деревня. Под уступом мира.",
    "A million breaths of salt the wind heaves over the Great Salt Desert Moghra'yi.": "Миллион соленых вздохов ветер проносит над Великой соляной пустыней Могра'йи.",
    "And to the east, the rotting jungles of Qud.": "А к востоку — гниющие джунгли Куда.",
    "watervine can grow, and we grow it.": "здесь может расти лоза, и мы её выращиваем.",
    "No, no, I'm far too engrossed to see to your curiosity.": "Нет, нет, я слишком поглощен делами, чтобы тешить твоё любопытство.",
    "I'm sure the Barathrumites have something for you to do.": "Я уверен, у баратрумитов найдется для тебя дело.",
    "And? Is that the whole of your fool statement?": "И? Это всё твоё глупое заявление?",
    "Bit heartier, a-ye?": "Немного сердечнее, а?",
    "I know you will go. Please take care where you dig;": "Я знаю, ты пойдешь. Пожалуйста, берегись там, где копаешь;",
    "mopango wanderers say the hot blood of Eater artifice yet runs in the guts of that place.": "странники мопанго говорят, что горячая кровь творений Пожирателей всё еще течет в недрах того места.",
    "Poor friend. The ironshank must be upsetting your digestion.": "Бедный друг. Железная нога, должно быть, расстроила твое пищеварение.",
    "The darkness must be blooming in your brain, making you slow and doltish.": "Тьма, должно быть, расцветает в твоем мозгу, делая тебя медлительным и тупым.",
    "The sultanate dissolved? Tidings would have reached me....": "Султанат распался? Вести дошли бы до меня....",
    "someone would have said so....": "кто-нибудь бы сказал об этом....",
    "You cantered too long outside the House of the Coiled Lamb, and now you are a dolt.": "Ты слишком долго скакал за пределами Дома Свернутого Агнца, и теперь ты тупица.",
    "I will tarry here until the Godhead passes on, and then I will canonize his deeds in high relief.": "Я задержусь здесь, пока Божество не преставится, а затем я канонизирую его деяния в высоком рельефе.",
    "What a prodigious reign! Bless that Coiled Lamb!": "Какое необычайное правление! Благослови Свернутого Агнца!",
    "Now, give me time to assess our losses and consult with Barathrum. Return soon.": "А теперь дай мне время оценить наши потери и посоветоваться с Баратрумом. Возвращайся скорее.",
    "Dagasha shall shepherd us to our uplifting.": "Дагаша поведет нас к нашему возвышению.",
    "Wouldst fain see Kah free, one day.": "Хотел бы я увидеть Ка свободным однажды.",
    "Yet, it attacketh not.": "Тем не менее, оно не атакует.",
    "k-Goninon, ancient and venerated gelatinous cupola, doth patrol the catacombs.": "к-Гонинон, древний и почитаемый студенистый купол, патрулирует катакомбы.",
    "Arrived centuries ere we discovered this place": "Прибыл за века до того, как мы обнаружили это место",
    "k-Goninon long ago consumed the Mark of Death itself.": "к-Goninon давно поглотил саму Метку Смерти.",
    "Please be careful. k-Goninon hath devoured watchers and wouldst devour =ifplayerplural:ye:thee= as well.": "Пожалуйста, будь осторожен. к-Goninon пожрал стражей и пожрал бы =ifplayerplural:вас:тебя= тоже.",
    "Ye have:Thou hast an object of great interest.": "У =ifplayerplural:вас:тебя= есть предмет огромного интереса.",
    "I cannot help but hold some fear toward Dagasha.": "Я не могу не испытывать некоторого страха перед Дагашей.",
    "No doubt the future of this coterie hath changed, mayhap for the better.": "Без сомнения, будущее этого круга изменилось, возможно, к лучшему.",
    "running free and running unwillingly are certainly not the same.": "бежать свободным и бежать против воли — это, конечно, не одно и то же.",
    "If Nacham is as wise as Doyoba believes, we stand to learn much very soon.": "Если Нахам так мудр, как верит Дойоба, мы скоро узнаем много нового.",
    "Dost the ancient Va'am yet have the capacity to defend, or protect?": "Обладает ли древний Ва'ам еще способностью защищать или оберегать?",
    "time shall reveal all.": "время всё откроет.",
    "Ye know:Thou knowest surely that this is the Tomb of the Eaters": "Ты наверняка знаешь, что это Гробница Пожирателей",
    "behind me lieth nestled a settlement occupied by the watchers of the tomb, a small coterie of mopango.": "позади меня притаилось поселение, занятое стражами гробницы, небольшим кругом мопанго.",
    "I feared we would lose you in the Tomb, but you look better than well.": "Я боялся, что мы потеряем тебя в Гробнице, но ты выглядишь лучше, чем хорошо.",
    "Have you visited Barathrum's workshop, by the by? Q Girl await you there.": "Кстати, ты посетил мастерскую Баратрума? Q-девочка ждет тебя там.",
    "Every movement is accompanied by sibilant music as those plates slide over one another, whispering a wordless prayer.": "Каждое движение сопровождается свистящей музыкой, когда эти пластины скользят друг по другу, шепча бессловесную молитву."
}

master.update(batch19)

with open(MASTER_DICT, 'w', encoding='utf-8') as f:
    json.dump(master, f, ensure_ascii=False, indent=2)

import os
MOD_DIR = r"C:\Users\Lecoo\AppData\LocalLow\Freehold Games\CavesOfQud\Mods\RussianLocalization"
with open(os.path.join(MOD_DIR, 'dictionary.json'), 'w', encoding='utf-8') as f:
    json.dump(master, f, ensure_ascii=False, indent=2)

print(f"Applied Batch 19: {len(batch19)} deep XML dialogue and lore strings.")
# Теперь запускаем скрипт инъекции для применения в файлы игры
