# -*- coding: utf-8 -*-
"""Count braces precisely, excluding string contents."""
from pathlib import Path, PurePath
import re

BOARD = Path(r"C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Game UI\Interface Planning\Airline_CEO__UI交互白板 V1.0.html")
h = BOARD.read_text(encoding="utf-8")
s = h.split("<script>")[1].split("</script>")[0]

# Remove strings to count structural braces only
no_str = re.sub(r'"(?:[^"\\]|\\.)*"', '""', s)
no_str = re.sub(r"'(?:[^'\\]|\\.)*'", "''", no_str)
print("structural braces:", no_str.count("{"), no_str.count("}"))

# If still imbalanced, find the extra by position
depth = 0
last_good = 0
for i, ch in enumerate(s):
    if ch == "{":
        depth += 1
    elif ch == "}":
        depth -= 1
    if depth == -1:
        print(f"Extra close at pos {i}")
        print(repr(s[i-50:i+50]))
        break
    if depth == 0:
        last_good = i

print(f"Last good pos: {last_good}")
print(f"Tail from last_good: {repr(s[last_good:last_good+200])}")
