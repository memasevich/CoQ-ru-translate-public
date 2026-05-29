import json

def sync_dictionaries():
    with open('dictionary.json', 'r', encoding='utf-8') as f:
        d = json.load(f)
    with open('word_dictionary.json', 'r', encoding='utf-8') as f:
        wd = json.load(f)

    synced = 0
    # Нам нужно, чтобы word_dictionary использовал те же переводы, что и точные совпадения в основном словаре
    for eng_word in wd:
        if eng_word in d:
            # Если в основном словаре есть точный перевод этого слова
            if wd[eng_word] != d[eng_word]:
                # Синхронизируем, но сохраняем капитализацию если она была важна
                old = wd[eng_word]
                wd[eng_word] = d[eng_word]
                synced += 1
    
    # Дополнительная чистка: удаляем из word_dictionary всё, что длиннее 20 символов 
    # (это должны быть фразы, им место в dictionary.json)
    to_move = {}
    keys_to_del = []
    for eng, rus in wd.items():
        if len(eng) > 20 or " " in eng:
            to_move[eng] = rus
            keys_to_del.append(eng)
    
    for k in keys_to_del:
        del wd[k]
        d[k] = to_move[k]

    with open('dictionary.json', 'w', encoding='utf-8') as f:
        json.dump(d, f, ensure_ascii=False, indent=2)
    with open('word_dictionary.json', 'w', encoding='utf-8') as f:
        json.dump(wd, f, ensure_ascii=False, indent=2)

    print(f"Synced {synced} words and moved {len(keys_to_del)} phrases to the main dictionary.")

if __name__ == "__main__":
    sync_dictionaries()
