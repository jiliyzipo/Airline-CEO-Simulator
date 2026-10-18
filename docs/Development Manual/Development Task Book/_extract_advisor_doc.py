# -*- coding: utf-8 -*-
"""Extract text from .doc via Word COM or binary UTF-16 scrape."""
import sys, io, re
from pathlib import Path

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
path = Path(r"c:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Development Task Book\Airline_CEO_顾问开局引导线_v1.1.doc")
out = Path(r"c:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Development Task Book\_advisor_guide_v1.1_extracted.txt")

text = None
# 1) Word COM
try:
    import comtypes.client  # type: ignore
    w = comtypes.client.CreateObject("Word.Application")
    w.Visible = False
    doc = w.Documents.Open(str(path), ReadOnly=True)
    text = doc.Content.Text
    doc.Close(False)
    w.Quit()
    print("via comtypes")
except Exception as e1:
    print("comtypes fail:", e1)
    try:
        import subprocess, tempfile, os
        # LibreOffice
        tmp = Path(tempfile.gettempdir()) / "advisor_guide_extract"
        tmp.mkdir(exist_ok=True)
        r = subprocess.run(
            ["soffice", "--headless", "--convert-to", "txt:Text", "--outdir", str(tmp), str(path)],
            capture_output=True, text=True, timeout=60,
        )
        print("soffice", r.returncode, r.stdout, r.stderr)
        cand = list(tmp.glob("*.txt"))
        if cand:
            text = cand[0].read_text(encoding="utf-8", errors="ignore")
    except Exception as e2:
        print("soffice fail:", e2)

if text is None:
    data = path.read_bytes()
    # Word .doc often stores text as UTF-16LE
    t = data.decode("utf-16le", errors="ignore")
    # Keep CJK + ascii runs
    chunks = re.findall(r"[\u4e00-\u9fffA-Za-z0-9，。；：、（）【】《》\.\,\:\;\-\+\*\/\%\s\n\r]{6,}", t)
    # Also try latin-1 readable
    t2 = "".join(chr(b) if 32 <= b < 127 or b in (10, 13) else " " for b in data)
    chunks2 = re.findall(r"[A-Za-z0-9\u4e00-\u9fff，。；：、（）【】\.\,\:\;\-\+\*\/\% ]{8,}", t2)
    text = "\n".join(chunks)
    if len(text) < 200:
        # try ole compound: WordDocument stream
        try:
            import olefile
            ole = olefile.OleFileIO(str(path))
            if ole.exists("WordDocument"):
                stream = ole.openstream("WordDocument").read()
                # extract ascii + utf16
                a = "".join(chr(b) if 32 <= b < 127 else "\n" for b in stream)
                u = stream.decode("utf-16le", errors="ignore")
                u = re.sub(r"[^\u4e00-\u9fffA-Za-z0-9，。；：、（）【】《》\.\n\r\-\+\*\/\%\:\s]", " ", u)
                text = a + "\n" + u
            ole.close()
            print("via olefile")
        except Exception as e3:
            print("olefile fail:", e3)
            text = text + "\n" + "\n".join(chunks2[:500])

out.write_text(text or "", encoding="utf-8")
print("wrote", out, "chars", len(text or ""))
print((text or "")[:8000])
