import os

def update_csproj():
    path = r'C:\Users\Lecoo\projects\RussianLocalization_NoWorkshop\RussianLocalization.csproj'
    if not os.path.exists(path):
        print("csproj not found")
        return

    with open(path, 'r', encoding='utf-8') as f:
        lines = f.readlines()

    new_lines = []
    skip = False
    for line in lines:
        if '<EmbeddedResource Include=' in line:
            continue
        new_lines.append(line)

    with open(path, 'w', encoding='utf-8') as f:
        f.writelines(new_lines)
    print("Cleaned csproj from embedded resources.")

if __name__ == "__main__":
    update_csproj()
