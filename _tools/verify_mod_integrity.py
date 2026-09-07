import os, sys, json, re, filecmp

def run_integrity_check():
    print("=" * 60)
    print(" CAVES OF QUD RUSSIAN LOCALIZATION - INTEGRITY VERIFIER")
    print("=" * 60)

    repo_root = r"D:\Lecoo_Profile\projects\CavesOfQudTranslator"
    repo_mod = os.path.join(repo_root, "RussianLocalization")
    appdata_mod = r"C:\Users\Lecoo\AppData\LocalLow\Freehold Games\CavesOfQud\Mods\RussianLocalization"

    errors_found = 0
    warnings_found = 0

    # 1. JSON Dictionaries Syntax & Statistics
    print("\n[1/4] Checking JSON Dictionaries...")
    json_files = [
        "dictionary.json", "word_dictionary.json", "pattern_dictionary.json",
        "faction_cases.json", "forms_dictionary.json", "morphology_dictionary.json",
        "stem_dictionary.json", "manifest.json", "workshop.json"
    ]

    for jf in json_files:
        path = os.path.join(repo_mod, jf)
        if not os.path.exists(path):
            print(f"  [ERROR] Missing expected file: {jf}")
            errors_found += 1
            continue
        try:
            with open(path, "r", encoding="utf-8-sig") as f:
                data = json.load(f)
            count = len(data) if isinstance(data, (dict, list)) else 1
            size_mb = os.path.getsize(path) / (1024 * 1024)
            print(f"  [OK] {jf:<26} : {count:>7} items ({size_mb:>5.2f} MB)")
        except Exception as ex:
            print(f"  [ERROR] {jf}: Failed to parse JSON: {ex}")
            errors_found += 1

    # 2. Regex Patterns Validation in pattern_dictionary.json
    print("\n[2/4] Validating Regex Patterns in pattern_dictionary.json...")
    pat_path = os.path.join(repo_mod, "pattern_dictionary.json")
    if os.path.exists(pat_path):
        with open(pat_path, "r", encoding="utf-8-sig") as f:
            patterns = json.load(f)
        
        invalid_patterns = 0
        for p, r in patterns.items():
            py_pattern = re.sub(r'\(\?<([a-zA-Z0-9_]+)>', r'(?P<\1>', p)
            py_pattern = re.sub(r'\\k<([a-zA-Z0-9_]+)>', r'(?P=\1)', py_pattern)
            try:
                re.compile(py_pattern)
            except re.error as ex:
                invalid_patterns += 1
                if invalid_patterns <= 5:
                    print(f"  [WARN] Regex error on: {p[:60]}... => {ex}")
        
        if invalid_patterns == 0:
            print(f"  [OK] All {len(patterns)} regex patterns compiled successfully.")
        else:
            print(f"  [WARN] {invalid_patterns} patterns with potential syntax warnings.")
            warnings_found += invalid_patterns

    # 3. Parity Check (Repository <-> Game Mod AppData)
    print("\n[3/4] Checking Parity (Repository <-> Game Mod AppData)...")
    if not os.path.exists(appdata_mod):
        print(f"  [WARN] AppData mod folder not found at: {appdata_mod}")
        warnings_found += 1
    else:
        repo_items = set(os.listdir(repo_mod))
        appdata_items = set(os.listdir(appdata_mod))

        missing_in_appdata = repo_items - appdata_items
        missing_in_repo = appdata_items - repo_items

        if missing_in_appdata:
            print(f"  [ERROR] Files missing in AppData: {missing_in_appdata}")
            errors_found += len(missing_in_appdata)
        if missing_in_repo:
            print(f"  [ERROR] Files missing in Repo: {missing_in_repo}")
            errors_found += len(missing_in_repo)

        diff_files = []
        runtime_logs = {"untranslated.txt"}
        for item in repo_items.intersection(appdata_items):
            if item in runtime_logs:
                continue
            p1 = os.path.join(repo_mod, item)
            p2 = os.path.join(appdata_mod, item)
            if os.path.isfile(p1) and os.path.isfile(p2):
                if not filecmp.cmp(p1, p2, shallow=False):
                    diff_files.append(item)

        if diff_files:
            print(f"  [ERROR] Code/Dictionary mismatch between Repo and AppData: {diff_files}")
            errors_found += len(diff_files)
        else:
            print(f"  [OK] All core code and dictionary files are 100% in sync with AppData.")

    # 4. Mod Cleanliness
    print("\n[4/4] Checking Mod Directory Cleanliness...")
    junk_extensions = [".rar", ".zip", ".bak", ".tmp", ".log"]
    junk_files = []
    for d in [repo_mod, appdata_mod]:
        if os.path.exists(d):
            for f in os.listdir(d):
                if any(f.endswith(ext) for ext in junk_extensions):
                    junk_files.append(os.path.join(d, f))

    if junk_files:
        print(f"  [WARN] Junk/temporary files found in mod directory: {junk_files}")
        warnings_found += len(junk_files)
    else:
        print("  [OK] No temporary or backup archives found in mod directories.")

    # Summary
    print("\n" + "=" * 60)
    if errors_found == 0:
        print(f" RESULT: ALL CHECKS PASSED (Errors: 0, Warnings: {warnings_found})")
    else:
        print(f" RESULT: INTEGRITY CHECK FAILED (Errors: {errors_found}, Warnings: {warnings_found})")
    print("=" * 60)

    return errors_found == 0

if __name__ == "__main__":
    success = run_integrity_check()
    sys.exit(0 if success else 1)
