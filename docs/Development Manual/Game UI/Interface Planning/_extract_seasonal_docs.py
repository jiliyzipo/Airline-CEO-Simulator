# -*- coding: utf-8 -*-
from __future__ import annotations

import zipfile
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parent
NS = {"w": "http://schemas.openxmlformats.org/wordprocessingml/2006/main"}


def doc_text(path: Path) -> str:
    with zipfile.ZipFile(path) as z:
        root = ET.fromstring(z.read("word/document.xml"))
    parts: list[str] = []
    for el in root.iter():
        tag = el.tag.split("}")[-1]
        if tag == "p":
            texts = [t.text or "" for t in el.findall(".//w:t", NS)]
            line = "".join(texts).strip()
            if line:
                parts.append(line)
        elif tag == "tbl":
            parts.append("[TABLE]")
            for row in el.findall("./w:tr", NS):
                cells = []
                for cell in row.findall("./w:tc", NS):
                    cells.append("".join(t.text or "" for t in cell.findall(".//w:t", NS)).strip())
                parts.append(" | ".join(cells))
            parts.append("[/TABLE]")
    return "\n".join(parts)


def main() -> None:
    paths = []
    att = ROOT / "attachment"
    if att.exists():
        paths.extend(sorted(att.rglob("*.docx")))
    paths.extend(sorted(ROOT.glob("*季节*.docx")))
    paths.extend(sorted(ROOT.glob("*旺季*.docx")))
    # unique
    seen = set()
    uniq = []
    for p in paths:
        if p.resolve() in seen:
            continue
        seen.add(p.resolve())
        uniq.append(p)
    chunks = []
    for p in uniq:
        rel = p.relative_to(ROOT)
        try:
            chunks.append(f"## {rel}\n\n{doc_text(p)}\n")
        except Exception as e:
            chunks.append(f"## {rel}\n\n(error: {e})\n")
    out = ROOT / "_seasonal_docs_bundle.md"
    out.write_text("\n".join(chunks) if chunks else "(no docs)", encoding="utf-8")
    print("docs", len(uniq), "->", out)


if __name__ == "__main__":
    main()
