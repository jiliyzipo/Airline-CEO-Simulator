# -*- coding: utf-8 -*-
"""Scan copied Chromium IndexedDB files for embedded .docx blobs."""
from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parent
IDB = ROOT / "_idb_copy"
OUT = ROOT / "_extracted_docs"


def main() -> None:
    raw = b""
    for f in sorted(IDB.glob("*")):
        if f.is_file() and f.suffix.lower() not in {".log"}:
            try:
                raw += f.read_bytes()
            except OSError:
                pass
        # also include LOG
        if f.is_file():
            try:
                raw += f.read_bytes()
            except OSError:
                pass

    print("total bytes", len(raw))
    print("has airline_ceo", b"airline_ceo_ui_board_docs" in raw)
    print("has n14", b"n14_" in raw)
    print("has docx ascii", b".docx" in raw)
    print("PK count", raw.count(b"PK\x03\x04"))

    OUT.mkdir(exist_ok=True)
    # Extract every ZIP/DOCX-like blob by scanning PK headers and finding EOCD
    starts = []
    i = 0
    while True:
        j = raw.find(b"PK\x03\x04", i)
        if j < 0:
            break
        starts.append(j)
        i = j + 4
    print("candidate starts", len(starts))

    extracted = 0
    for idx, start in enumerate(starts):
        # look for EOCD signature after start
        eocd = raw.find(b"PK\x05\x06", start)
        if eocd < 0 or eocd - start > 12 * 1024 * 1024:
            continue
        # EOCD is 22+ bytes; comment length at offset 20
        if eocd + 22 > len(raw):
            continue
        comment_len = raw[eocd + 20] | (raw[eocd + 21] << 8)
        end = eocd + 22 + comment_len
        blob = raw[start:end]
        # crude filter: must contain word xml
        if b"word/" not in blob and b"[Content_Types].xml" not in blob:
            continue
        out = OUT / f"extracted_{idx:03d}.docx"
        out.write_bytes(blob)
        extracted += 1
        print("wrote", out.name, "size", len(blob))

    # also dump nearby ascii context for keys
    for needle in [b"n14_", b"docx", b"airline_ceo_ui_board"]:
        pos = 0
        hits = 0
        while hits < 5:
            p = raw.find(needle, pos)
            if p < 0:
                break
            ctx = raw[max(0, p - 40) : p + 80]
            ascii_ctx = "".join(chr(b) if 32 <= b < 127 else "." for b in ctx)
            print("ctx", needle, ascii_ctx)
            pos = p + len(needle)
            hits += 1

    print("extracted", extracted)


if __name__ == "__main__":
    main()
