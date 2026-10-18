# -*- coding: utf-8 -*-
from __future__ import annotations

import zipfile
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parent
NS = {"w": "http://schemas.openxmlformats.org/wordprocessingml/2006/main"}
FILES = [
    "页面间连线建议.docx",
    "航班定价.docx",
    "L3 辅助收入管理.docx",
    "L3 单航线定价详情.docx",
    "L3 收益管理仪表盘.docx",
    "L3 动态定价算法面板.docx",
]


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
    out_parts = []
    for name in FILES:
        p = ROOT / name
        if not p.exists():
            out_parts.append(f"## MISSING {name}\n")
            continue
        out_parts.append(f"## {name}\n\n{doc_text(p)}\n")
    out = ROOT / "_pricing_docs_bundle.md"
    out.write_text("\n".join(out_parts), encoding="utf-8")
    print("wrote", out, "chars", out.stat().st_size)


if __name__ == "__main__":
    main()
