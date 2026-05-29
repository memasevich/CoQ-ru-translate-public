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

joppa_dialogues = {
    # Mehmet
    "-mm, work? The farmers are plagued by cave vermin. You might speak to Mehmet o' there, by the southern watervine patch.": "-мм, работа? Фермеры страдают от пещерных вредителей. Тебе стоит поговорить с Мехметом там, у южной грядки влагопаутинника.",
    "And Argyve, too, =player.formalAddressTerm=. The tinker. Always looking for trinkets to wire between, heh. Go through his hut of sheet metal, to the southwest.": "А еще Аржив, =player.formalAddressTerm=. Изобретатель. Вечно ищет безделушки, чтобы соединить их проводами, хех. Его хижина из листового металла на юго-западе.",
    "Live and drink, =name=. Any news from Red Rock?": "Живи и пей, =name=. Есть новости из Красного Рока?",
    "Mehmet, the tongue says. Live and drink, =name=.": "Мехмет, так меня зовут. Живи и пей, =name=.",
    "Watervine farm in the lap of the marsh. You've sucked the moisture out a vinewafer, yea? We tend the plant here.": "Ферма влагопаутинника в самом сердце болот. Ты ведь высасывал влагу из сушеного стебля, верно? Мы выращиваем их здесь.",
    "Anywhile, that's where my wandering mind goes. More's about Joppa I say speak to our Elderfriend. Irudad. Up northwise the path.": "Впрочем, мысли мои разбрелись. О Джоппе тебе лучше расскажет наш друг-старейшина. Ирудад. Его дом дальше на север по тропе.",
    "Oh? Oh. {{emote|*Mehmet pauses.*}}": "О? О... {{emote|*Мехмет замолкает.*}}",
    "Don't like the look o' that thing. Best bring it to Elder. Hut's northwise up the path.": "Не нравится мне вид этой штуки. Лучше отнеси её Старейшине. Его хижина на севере по тропе.",
    "Joppa is my home, yes.": "Да, Джоппа — мой дом.",
    "I walked Moghra'yi in the caravans of my brethren and the saltback, but upon meeting Elder Irudad, knew at once to settle down here. You will understand, =player.formalAddressTerm= =player.apparentSpecies=, if you speak to him.": "Я ходил по Могра-йи в караванах моих братьев и солеспинов, но встретив Старейшину Ирудада, сразу понял, что осяду здесь. Ты поймешь, =player.formalAddressTerm= =player.apparentSpecies=, если поговоришь с ним.",
    "I am dromad, =player.apparentSpecies= =player.formalAddressTerm=. Some say saltstrider. Do you know this?": "Я дромадер, =player.apparentSpecies= =player.formalAddressTerm=. Некоторые зовут нас солеходами. Знаешь о таких?",
    "Taste what on the wind today, =name=?": "Чувствуешь, что сегодня приносит ветер, =name=?",
    "Aye, the pest-vanquisher! Live and drink, =name=.": "А, победитель вредителей! Живи и пей, =name=.",
    "A waterhand? Aye. Live and drink, traveller.": "Водный житель? Да. Живи и пей, путник.",
    
    # Irudad
    "-mm. Mmm? =player.FormalAddressTerm=?": "-мм. Ммм? =player.FormalAddressTerm=?",
    "{{emote|*Elder Irudad smiles.*}}": "{{emote|*Старейшина Ирудад улыбается.*}}",
    "Live and drink. Come in- come sit 'neath the cool shade, 'cross a pillow there. And welcome to Joppa.": "Живи и пей. Заходи, присядь в прохладной тени на подушку. Добро пожаловать в Джоппу.",
    "You may drink of our freshwater, too, and quench your thirst.": "Ты можешь испить нашей пресной воды и утолить жажду.",
    "Speak to my daughter through the east door, for herbs. And sitting Tam in the southeast has all manner of trinket, against his chests o' drawers.": "Поговори с моей дочерью за восточной дверью насчет трав. А Тэм на юго-востоке держит всякие безделушки в своих комодах.",
    "-this? Oh? Oh...": "-это? О? О...",
    "{{emote|*Elder Irudad pauses for several minutes.*}}": "{{emote|*Старейшина Ирудад умолкает на несколько минут.*}}",
    "Warted leg? mm. Foul smell of sour gum? mmm. Moon and sun....": "Бородавчатая лапа? мм. Мерзкий запах кислой смолы? ммм. Луна и солнце...",
    "A girshling? This is a girshling.": "Гиршлинг? Это гиршлинг.",
    "Creature of plague, =player.formalAddressTerm=. mmm. This one...": "Тварь чумы, =player.formalAddressTerm=. ммм. Эта...",
    "{{emote|*Elder Irudad pauses.*}}": "{{emote|*Старейшина Ирудад замолкает.*}}",
    "This one covered in slick and muscled out the bilge hose of sleeping Agolgot, in the cave under the Cloaca...": "Она покрыта слизью и вылезла из нечистот спящего Аголгота, в пещере под Клоакой...",
    "-mm, but here? Girshling this far west? There must be hundreds for one to reach... mm, does the Gyre widen again??": "-мм, но здесь? Гиршлинг так далеко на западе? Должно быть, их сотни, раз одна добралась сюда... мм, неужели Вихрь снова расширяется??",
    "-mm, plagues. Of our great grandsires many-times-over. Salt, darkness, svardym-frog... Girshling.": "-мм, напасти. Времен наших прадедов и прадедов их прадедов. Соль, тьма, свардим-лягушка... Гиршлинг.",
    "-mm, =player.formalAddressTerm=. Do not like to sour the air with a harsh word! But this tiding bites my liver like acid. mm, must ask Nima for an elixir of yuckwheat...": "-мм, =player.formalAddressTerm=. Не люблю осквернять воздух суровым словом! Но эти вести жгут мою печень, как кислота. мм, надо попросить Ниму эликсир из противной пшеницы...",
    "I journey North, to the Six Day Stilt.": "Я отправляюсь на север, к Шестидневному Столпу.",
}

update_json('dictionary.json', joppa_dialogues)
update_json('dictionary_master.json', joppa_dialogues)

print("Stage 3 (Mehmet & Irudad) exact dialogues added.")
