# -*- coding: utf-8 -*-
from __future__ import annotations

import hashlib
import re
import zipfile
from pathlib import Path
from xml.etree import ElementTree as ET

OUT = Path(__file__).with_name("_extracted_docs")
REPORT = Path(__file__).with_name("_extracted_docs_text.md")
NS = {"w": "http://schemas.openxmlformats.org/wordprocessingml/2006/main"}


def docx_text(path: Path) -> str:
    with zipfile.ZipFile(path) as z:
        xml = z.read("word/document.xml")
    root = ET.fromstring(xml)
    parts: list[str] = []
    for p in root.findall(".//w:p", NS):
        texts = [t.text or "" for t in p.findall(".//w:t", NS)]
        line = "".join(texts).strip()
        if line:
            parts.append(line)
    # tables as TSV-ish
    for tbl in root.findall(".//w:tbl", NS):
        parts.append("[TABLE]")
        for row in tbl.findall("./w:tr", NS):
            cells = []
            for cell in row.findall("./w:tc", NS):
                cell_text = "".join(t.text or "" for t in cell.findall(".//w:t", NS)).strip()
                cells.append(cell_text)
            parts.append(" | ".join(cells))
        parts.append("[/TABLE]")
    return "\n".join(parts)


def main() -> None:
    seen: dict[str, Path] = {}
    for f in sorted(OUT.glob("*.docx"), key=lambda p: -p.stat().st_size):
        h = hashlib.md5(f.read_bytes()).hexdigest()
        if h in seen:
            continue
        seen[h] = f

    chunks: list[str] = []
    for h, f in seen.items():
        try:
            text = docx_text(f)
        except Exception as e:
            text = f"(read error: {e})"
        if len(text.strip()) < 8:
            continue
        chunks.append(f"## {f.name} ({f.stat().st_size} bytes)\n\n{text}\n")

    REPORT.write_text("\n".join(chunks), encoding="utf-8")
    print("unique with text", len(chunks), "->", REPORT)


if __name__ == "__main__":
    main()
