import json
import os

def fix_dictionary():
    dict_path = 'dictionary.json'
    with open(dict_path, 'r', encoding='utf-8') as f:
        d = json.load(f)

    # 1. Битые фразы Ирудада
    d["You may drink our freshwater, too, и quench your thirst."] = "Вы также можете пить нашу пресную воду и утолять жажду."
    d["Live и drink. Come in- come sit 'neath   cool shade, 'cross a pillow"] = "Живи и пей. Заходи, присядь в прохладной тени на подушку."
    d["здесь. И добро пожаловать в Джоппу."] = "здесь. И добро пожаловать в Джоппу." # Повтор для надежности
    d["--мм. Ммм? Друг?"] = "--мм. Ммм? Друг?"
    
    # Фраза про Джоппу (сломанная)
    d["salt   ветер heaves over   Great Salt Desert Moghra'yi. И to   восток,"] = "соленый ветер веет над Великой соляной пустыней Могра-йи. А на востоке,"
    d["  rotting jungles Куд."] = "  гниющие джунгли Куда."
    d["Здесь, в щели между ними, может вырасти водяная лоза, и мы ее выращиваем. ммм"] = "Здесь, в щели между ними, растет водяная лоза, и мы ее выращиваем. ммм,"
    
    # Фраза про работу
    d["--мм, работа? Фермеры страдают от пещерных вредителей. Тебе стоит поговорить с Мехметом"] = "--мм, работа? Фермеры страдают от пещерных вредителей. Тебе стоит поговорить с Мехметом"
    d["' there, by   southern влагопаутинник patch."] = ", что стоит вон там, у южной грядки водяной лозы."
    
    # Фраза про Аржива
    d["-mm, work? Try Argyve, friend.   tinker. Always looking для trinkets to wire"] = "-мм, работа? Попробуй обратиться к Арживу, друг. Он жестянщик. Вечно ищет безделушки,"
    d["between, heh. Go through his hut sheet metal, to   southwest."] = "чтобы что-то там соединить, хех. Ступай в его хижину из листового металла, на юго-запад."
    
    # Тэм
    d["-here? Speak to my daughter through   восток дверь, для herbs. И sitting Tam"] = "-здесь? Поговори с моей дочерью за восточной дверью насчет трав. А сидящий Тэм"
    d["in   southeast has all manner trinket, against his chests o' drawers."] = "на юго-востоке держит всяческие безделушки в своих комодах."

    # 2. Очистка вредных коротких слов из word_dictionary
    word_dict_path = 'word_dictionary.json'
    if os.path.exists(word_dict_path):
        with open(word_dict_path, 'r', encoding='utf-8') as f:
            wd = json.load(f)
        
        bad_words = ["and", "for", "the", "with", "from", "into", "unto", "upon", "between", "through"]
        for w in bad_words:
            if w in wd: del wd[w]
            if w.capitalize() in wd: del wd[w.capitalize()]
            
        with open(word_dict_path, 'w', encoding='utf-8') as f:
            json.dump(wd, f, ensure_ascii=False, indent=2)
        print("Cleaned word_dictionary.json")

    with open(dict_path, 'w', encoding='utf-8') as f:
        json.dump(d, f, ensure_ascii=False, indent=2)
    print("Updated dictionary.json with broken fragments.")

if __name__ == "__main__":
    fix_dictionary()
