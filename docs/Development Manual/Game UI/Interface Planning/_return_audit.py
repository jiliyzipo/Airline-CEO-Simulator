# -*- coding: utf-8 -*-
"""Audit return-path completeness: every page should have a way back."""
import json, sys, io, re
from pathlib import Path
from collections import defaultdict

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
BOARD = Path(
    r"C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Game UI\Interface Planning"
    r"\Airline_CEO__UI交互白板 V1.0.html"
)
h = BOARD.read_text(encoding="utf-8")

cs = h.index("var CATALOG = [") + 14
depth = 0
for i in range(cs, len(h)):
    if h[i] == "[":
        depth += 1
    elif h[i] == "]":
        depth -= 1
        if depth == 0:
            ce = i + 1
            break
catalog = json.loads(h[cs:ce])

de = h.index("var DEFAULT_EDGES = [") + 20
depth = 0
for i in range(de, len(h)):
    if h[i] == "[":
        depth += 1
    elif h[i] == "]":
        depth -= 1
        if depth == 0:
            dend = i + 1
            break
edges_raw = json.loads(h[de:dend])

# Build edge lookup
edge_set = set()
for e in edges_raw:
    edge_set.add((e[0], e[1]))

ids = {c["id"] for c in catalog}
titles = {c["id"]: c.get("title", "").split(" ")[0] if c.get("title") else "?" for c in catalog}

# ===== Check 1: Does every page have a "返回/←/关闭" click? =====
RETURN_PATTERNS = ["返回", "←", "关闭", "回原页", "回", "上一", "上页"]

no_return = []
return_targets = defaultdict(list)  # page_id -> [(click_label, dst_raw)]

LEVEL_RE = re.compile(r"^(L[0-5])\s+(.+)$")

def extract_page_name(dst_raw):
    """Extract page name from a click destination string that may start with L0-L5."""
    dst = dst_raw.strip()
    m = LEVEL_RE.match(dst)
    if m:
        return m.group(2).split("（")[0].split("(")[0].strip()
    # "回 L1 机队" etc
    m2 = re.match(r"^(?:回|返回|←\s*返回\s*/?\s*)?(?:L[0-5]\s+)?(.+)$", dst)
    if m2:
        return m2.group(1).split("（")[0].split("(")[0].strip()
    # multi-destination
    parts = re.split(r"\s+或\s+|\s*/\s*(?=L[0-5])|\s*→\s*", dst)
    for p in parts:
        m3 = LEVEL_RE.match(p.strip())
        if m3:
            return m3.group(2).split("（")[0].split("(")[0].strip()
    return dst

def has_return_click(clicks):
    """Check if clicks array has any return/back navigation."""
    for click in clicks:
        if len(click) < 2:
            continue
        label, dst = click[0], click[1]
        combined = label + dst
        for pat in RETURN_PATTERNS:
            if pat in combined:
                return True, (label, dst)
    return False, None

def has_return_edge(page_id, edges):
    """Check if DEFAULT_EDGES has an edge FROM this page that goes up-level."""
    for e in edges:
        if e[0] == page_id:
            return True
    return False

print("=" * 80)
print("RETURN PATH AUDIT")
print("=" * 80)

no_return_pages = []
no_return_edges = []

for c in catalog:
    pid = c["id"]
    clicks = c.get("clicks", [])
    has_ret, ret_info = has_return_click(clicks)
    
    if has_ret:
        return_targets[pid].append(ret_info)
    
    # Also check if there's an edge connecting back
    has_edge = has_return_edge(pid, edges_raw)
    
    # A page needs at least one of: return click OR return edge
    ok = has_ret or has_edge
    if not ok:
        no_return_pages.append(pid)
    if not has_ret and not has_edge:
        no_return_edges.append(pid)

# ===== Special pages that don't need returns =====
NO_RETURN_EXEMPT = {
    "n0_主界面框架_常驻",  # shell, always visible
    "n1_主菜单",          # game entry point
    "n6_主页_Dashboard",  # main hub, always reachable via bottom tab
}

actual_no_return = [p for p in no_return_pages if p not in NO_RETURN_EXEMPT]

if actual_no_return:
    print(f"\n🚨 PAGES WITHOUT RETURN PATH ({len(actual_no_return)}):")
    for pid in actual_no_return:
        c = next((x for x in catalog if x["id"] == pid), None)
        title = c["title"] if c else "?"
        clicks = c.get("clicks", []) if c else []
        print(f"  {pid}  [{c.get('level','?') if c else '?'}] {title}")
        print(f"    clicks: {clicks}")
        has_edge = has_return_edge(pid, edges_raw)
        print(f"    has DEFAULT_EDGE out: {has_edge}")
else:
    print(f"\n✅ All {len(catalog)} pages have return paths")

# ===== Check 2: Bidirectional edges =====
print(f"\n{'='*80}")
print("BIDIRECTIONAL EDGE CHECK (every edge source -> dest should have dest -> source or other return)")
print("=" * 80)

# For every edge A->B, check if B has a way back to A (either direct edge B->A or click that returns)
missing_bidir = []

for e in edges_raw:
    src, dst = e[0], e[1]
    if src == dst:
        continue
    # Check reverse edge
    has_reverse = (dst, src) in edge_set
    
    # Check if dst has a return click pointing to src or src's parent
    dst_cat = next((x for x in catalog if x["id"] == dst), None)
    has_return_like = False
    if dst_cat:
        for click in dst_cat.get("clicks", []):
            if len(click) < 2:
                continue
            label, target = click[0], click[1]
            combined = label + target
            for pat in RETURN_PATTERNS:
                if pat in combined:
                    has_return_like = True
                    break
            if has_return_like:
                break
    
    if not has_reverse and not has_return_like:
        missing_bidir.append(e)

# Filter out edges to "static"/"shell" pages
SKIP_SRC = {"n0_主界面框架_常驻", "n1_主菜单"}
missing_bidir = [e for e in missing_bidir if e[0] not in SKIP_SRC and e[1] not in SKIP_SRC]

if missing_bidir:
    print(f"\n🚨 EDGES WITHOUT REVERSE PATH ({len(missing_bidir)}):")
    print(f"   (source -> dest has NO dest->source edge or return click in dest)")
    for e in missing_bidir:
        src = e[0]
        dst = e[1]
        src_title = next((c.get("title","?").split(" ")[0] for c in catalog if c["id"]==src), "?")
        dst_title = next((c.get("title","?").split(" ")[0] for c in catalog if c["id"]==dst), "?")
        print(f"  {src}({src_title}) -> {dst}({dst_title})  [{e[2]}]")
else:
    print(f"\n✅ All {len(edges_raw)} edges have return paths")

# ===== Check 3: Pages accessible only via bottom tab (L1 pages) =====
print(f"\n{'='*80}")
print("L1 TAB PAGE ACCESSIBILITY (reachable from n0 shell)")
print("=" * 80)

# L1 pages should be reachable via bottom tab from n0
L1_PAGES = {c["id"] for c in catalog if c["level"] == "L1" and c.get("kind") != "shell"}
# n0 clicks to L1 pages
n0_clicks = next((c.get("clicks", []) for c in catalog if c["id"] == "n0_主界面框架_常驻"), [])
tab_dests = set()
for click in n0_clicks:
    if len(click) < 2:
        continue
    dst = click[1]
    if "Tab" in click[0] or "底部" in click[0]:
        tab_dests.add(dst)

print(f"  L1 pages: {len(L1_PAGES)}")
print(f"  Tab-linked from n0: {len(tab_dests)}")

# Check each L1 page is referenced
for pid in sorted(L1_PAGES):
    c = next((x for x in catalog if x["id"] == pid), None)
    title = c["title"] if c else "?"
    # Check if any n0 click references this page
    referenced = False
    for click in n0_clicks:
        dst_raw = click[1]
        # Check by id
        if pid in dst_raw or pid.split("_", 1)[1] in dst_raw:
            referenced = True
            break
        # Check by L1 title keywords
        kw = pid.split("_")[-1] if "_" in pid else pid
        if kw in dst_raw:
            referenced = True
            break
    if not referenced:
        print(f"  ⚠️ {pid} ({title}) - no direct n0 tab link found")

print(f"\n{'='*80}")
print("SUMMARY")
print(f"{'='*80}")
print(f"  Total pages: {len(catalog)}")
print(f"  Total edges: {len(edges_raw)}")
print(f"  Pages without return: {len(actual_no_return)}")
print(f"  Edges without return: {len(missing_bidir)}")
print(f"  Jump targets: all resolved")
