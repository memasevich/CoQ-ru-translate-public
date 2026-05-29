import json

def upgrade_patterns_v2():
    path = 'pattern_dictionary.json'
    with open(path, 'r', encoding='utf-8') as f:
        p = json.load(f)

    # 1. Святыни и Султаны (динамические имена)
    p[r"(?i)The shrine depicts a significant event from the life of the ancient sultan (?<name>.+?):"] = "Эта святыня изображает важное событие из жизни древнего султана {name}:"
    p[r"(?i)In (?<year>.+?), (?<name>.+?) cleansed the marshlands of the plagues of the Gyre"] = "В {year} году {name} очистил болотные земли от болезней Вихря"
    p[r"(?i)taught Abram to sow (?<plant>.+?) along its fertile tracks"] = "научил Абрама сеять {plant} вдоль плодородных путей"
    
    # 2. Журнал и информация
    p[r"(?i)You note this piece of information in the (?<section>.+?) section of your journal"] = "Вы записали эти сведения в раздел «{section}» вашего журнала"
    p[r"(?i)You note this piece information in (?<section>.+?) section your journal"] = "Вы записали эти сведения в раздел «{section}» вашего журнала"
    p[r"(?i)You note the location (?<loc>.+?) in the (?<section>.+?) section of your journal"] = "Вы отметили местоположение {loc} в разделе «{section}» вашего журнала"

    # 3. Предметы и окружение (убираем артикли 'a', 'an')
    p[r"(?i)Вы проходите мимо: a (?<item>.+?)\."] = "Вы проходите мимо: {item}."
    p[r"(?i)Вы проходите мимо: an (?<item>.+?)\."] = "Вы проходите мимо: {item}."
    p[r"(?i)lying on a (?<bed>.+?)"] = "лежит на {bed}"
    
    # 4. Боевой лог (дополнение)
    p[r"(?i)The (?<name>.+?) hits (?<target>.+?) for (?<dmg>\d+) damage with (?:her|his|its|their) (?<weapon>.+?)!"] = "{name} наносит {dmg} ед. урона ({target}) своим {weapon}!"
    p[r"(?i)for (?<dmg>\d+) damage with (?:her|his|its|their) (?<weapon>.+?)\. \[(?<roll>.+?)\]"] = "на {dmg} ед. урона ({weapon}). [{roll}]"
    
    # 5. Квесты
    p[r"(?i)Вы завершили этап, (?<step>.+?), , Квест (?<quest>.+?)!"] = "Вы завершили этап «{step}» квеста «{quest}»!"
    p[r"(?i)Fetch Argyve a (?<item>.+)"] = "Принести Арживу: {item}"

    # 6. Разное из логов
    p[r"(?i)A stone crack is revealed to the (?<dir>.+?)!"] = "На {dir}е открылась каменная трещина!"
    p[r"(?i)wet bizarre contraption"] = "мокрое причудливое устройство"

    with open(path, 'w', encoding='utf-8') as f:
        json.dump(p, f, ensure_ascii=False, indent=2)
    
    print("Upgraded to Pattern Dictionary v2.0 (Shadow-Ready)")

if __name__ == "__main__":
    upgrade_patterns_v2()
