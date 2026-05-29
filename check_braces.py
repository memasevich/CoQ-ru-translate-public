import sys

def check_braces(file_path):
    with open(file_path, 'r', encoding='utf-8') as f:
        content = f.read()
    
    stack = []
    line = 1
    col = 1
    
    for i, char in enumerate(content):
        if char == '\n':
            line += 1
            col = 1
        else:
            col += 1
            
        if char == '{':
            stack.append((line, col))
        elif char == '}':
            if not stack:
                print(f"Extra closing brace at Line {line}, Col {col}")
            else:
                stack.pop()
    
    if stack:
        for l, c in stack:
            print(f"Unclosed opening brace at Line {l}, Col {c}")

if __name__ == "__main__":
    check_braces('RussianLocalization.cs')
