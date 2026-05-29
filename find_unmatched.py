def find_unmatched_braces(file_path):
    with open(file_path, 'r', encoding='utf-8') as f:
        lines = f.readlines()
    
    stack = []
    for line_num, line in enumerate(lines, 1):
        for col_num, char in enumerate(line, 1):
            if char == '{':
                stack.append((line_num, col_num))
            elif char == '}':
                if not stack:
                    print(f"Extra closing brace at Line {line_num}, Col {col_num}")
                else:
                    stack.pop()
    
    if stack:
        for line_num, col_num in stack:
            print(f"Unclosed opening brace at Line {line_num}, Col {col_num}: {lines[line_num-1].strip()}")

if __name__ == "__main__":
    find_unmatched_braces('RussianLocalization.cs')
