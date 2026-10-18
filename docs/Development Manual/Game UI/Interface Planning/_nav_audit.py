# -*- coding: utf-8 -*-
"""Audit all CATALOG clicks destinations against existing pages."""
import json, re, sys, io
from pathlib import Path
from collections import defaultdict

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
BOARD = Path(r"C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Game UI\Interface Planning\Airline_CEO__UI交互白板 V1.0.html")
h = BOARD.read_text(encoding='utf-8')

cs = h.index('var CATALOG = [') + 14
depth = 0
for i in range(cs, len(h)):
    if h[i] == '[': depth += 1
    elif h[i] == ']':
        depth -= 1
        if depth == 0:
            ce = i + 1
            break
catalog = json.loads(h[cs:ce])

# Build title/id indexes
ids = {c['id'] for c in catalog}
titles = {c['title']: c['id'] for c in catalog}
# Also map simplified titles (without emoji / after ·)
def normalize(s):
    s = re.sub(r'^[\U00010000-\U0010ffff\u2600-\u27bf\ufe0f\u200d]+\s*', '', s)  # strip leading emoji
    s = s.split(' · ')[0].split('（')[0].split('(')[0].strip()
    return s

norm_titles = defaultdict(list)
for c in catalog:
    norm_titles[normalize(c['title'])].append(c['id'])
    # also by id suffix without nXX_
    short = c['id'].split('_', 1)[1] if '_' in c['id'] else c['id']
    norm_titles[short].append(c['id'])

print(f'Total pages: {len(catalog)}')
print('='*80)

# Parse destination from click target string
# Examples: "L2 SMS与风险管理", "L1 安全质量部", "L4 危机/IRROPS 弹窗", "同页编辑", "收件箱发送..."
LEVEL_RE = re.compile(r'^(L[0-5])\s+(.+)$')

def extract_page_ref(dst):
    """Return (level, page_name) if dst looks like a page jump, else None."""
    dst = dst.strip()
    # Skip non-page actions
    skip_prefixes = (
        '同页', '展开', '关闭', '生效', '执行', '结算', '写回', '回写', '加载', '保存',
        '下载', '一键', '发送', '标记', '切换', '拖拽', '调节', '确认关闭',
        '对应 L1', '存档列表', '文章详情', '成员详情', '目标详情', '详情',
        '展开完整', '钻取', '定位',
    )
    if any(dst.startswith(p) for p in skip_prefixes):
        return None
    if dst.startswith('收件箱') and 'L' not in dst[:3]:
        # "收件箱发送..." etc - sometimes has page refs later
        if '→' in dst or '->' in dst:
            pass
        else:
            return None

    m = LEVEL_RE.match(dst)
    if m:
        level, name = m.group(1), m.group(2)
        # Strip parenthetical notes: "L4 危机/IRROPS 弹窗（调查决策）"
        name = re.sub(r'[（(].*$', '', name).strip()
        # Strip trailing arrows notes
        name = name.split('→')[0].split('->')[0].strip()
        return (level, name)

    # "回 L2 航班定价" / "回市场部" / "来源页"
    m2 = re.match(r'^(?:回|返回|←\s*返回\s*/?\s*)?(?:L[0-5]\s+)?(.+)$', dst)
    if dst.startswith('回 ') or dst.startswith('←'):
        rest = re.sub(r'^(←\s*)?返回\s*(/\s*关闭)?\s*', '', dst).strip()
        rest = re.sub(r'^回\s*', '', rest).strip()
        m3 = LEVEL_RE.match(rest) if rest else None
        if m3:
            name = re.sub(r'[（(].*$', '', m3.group(2)).strip()
            return (m3.group(1), name)
        if rest and not rest.startswith('同页'):
            return ('?', rest)
    return None

def resolve_page(level, name):
    """Try to find matching catalog entry. Return id or None."""
    # Direct id match attempts
    candidates = []
    # Exact title contains
    for c in catalog:
        t = c['title']
        nt = normalize(t)
        # name might be "SMS与风险管理" matching title "📊 SMS与风险管理 · ..."
        if name in t or name == nt or nt in name or name.replace(' ', '') in t.replace(' ', ''):
            candidates.append(c['id'])
        # id contains name
        if name in c['id']:
            candidates.append(c['id'])

    # Special aliases
    aliases = {
        '主页 Dashboard': 'n6_主页_Dashboard',
        '主页': 'n6_主页_Dashboard',
        '主页 + AO 引导': 'n6_主页_Dashboard',
        '财务部': 'n15_财务部',
        '市场部': 'n14_市场销售部',
        '市场销售部': 'n14_市场销售部',
        '运营部': 'n13_运营部',
        '机务工程部': 'n17_机务工程部',
        '人力资源部': 'n18_人力资源部',
        '安全质量部': 'n19_安全质量部',
        '战略发展部': 'n16_战略发展部',
        '董事会': 'n20_董事会',
        '机队': 'n9_机队',
        '飞机详情': 'n10_飞机详情_履历',
        '航线网络': 'n12_航线网络',
        '单航线': 'n41_单航线',
        '竞争雷达': 'n60_竞争雷达',
        '航班定价': 'n49_航班定价',
        'CEO决策会议': 'n21_CEO_决策会议',
        'CEO 决策会议': 'n21_CEO_决策会议',
        '开线会议': 'n21_CEO_决策会议',
        '危机/IRROPS 弹窗': 'n24_危机_IRROPS_弹窗',
        '危机弹窗': 'n24_危机_IRROPS_弹窗',
        '事件弹窗': 'n24_危机_IRROPS_弹窗',
        '随机事件日志': 'n30_随机事件日志',
        '碳配额管理': 'n32_碳配额管理',
        '对冲工作台': 'n31_对冲工作台',
        '年度预算编制': 'n22_年度预算编制_审批',
        '紧急融资/破产': 'n25_紧急融资_破产保护',
        '宏观经济面板': 'n28_宏观经济面板',
        '宏观面板': 'n28_宏观经济面板',
        'FFP 管理': 'n33_FFP_常旅客管理',
        '产品工坊': 'n34_产品工坊',
        'RPM 评估台': 'n35_新航线_RPM_评估台',
        'RPM评估台': 'n35_新航线_RPM_评估台',
        '航班环构建器': 'n36_航班环构建器',
        '工会谈判桌': 'n37_工会谈判桌',
        '渠道预算': 'n68_渠道预算',
        '品牌危机公关': 'n72_品牌危机公关',
        '大客户合同': 'n67_大客户合同',
        '季节性日历': 'n55_季节性日历',
        '航线收益分析': 'n44_航线收益分析',
        '设置/存档': 'n40_设置_存档',
        '飞行学院': 'n39_飞行学院',
        'AO 对话窗': 'n8_航空事务官_AO',
        '续聘/解雇': 'n26_财年结算_续聘_解雇',
        '部门负责人任命': 'n23_部门负责人任命',
        '投票弹窗': 'n20_董事会',  # related
    }
    if name in aliases:
        return aliases[name]

    # Fuzzy: remove spaces
    name_c = name.replace(' ', '').replace('·', '')
    for c in catalog:
        tid = c['id']
        tt = normalize(c['title']).replace(' ', '').replace('·', '')
        if name_c == tt or name_c in tid or name_c in tt:
            return c['id']
        if tt in name_c and len(tt) >= 4:
            return c['id']

    if candidates:
        # prefer unique
        uniq = list(dict.fromkeys(candidates))
        if len(uniq) == 1:
            return uniq[0]
        return uniq[0]  # first match
    return None

missing = []  # (source_id, click_label, dst_raw, level, name)
matched = []
skipped = []
ambiguous_or_multi = []

for c in catalog:
    for click in c.get('clicks', []):
        if len(click) < 2:
            continue
        label, dst = click[0], click[1]
        # Split multi-target destinations like "L2 A 或 L3 B"
        parts = re.split(r'\s+或\s+|\s*/\s*(?=L[0-5])', dst)
        if len(parts) == 1:
            parts = [dst]

        for part in parts:
            part = part.strip()
            ref = extract_page_ref(part)
            if ref is None:
                # Also try if part contains "L2 xxx" embedded
                embedded = re.findall(r'L[0-5]\s+[^\s→\->,，/]+(?:\s*[^\s→\->,，/（(]*)?', part)
                if not embedded:
                    skipped.append((c['id'], label, part))
                    continue
                for emb in embedded:
                    ref2 = extract_page_ref(emb.strip())
                    if not ref2:
                        continue
                    level, name = ref2
                    rid = resolve_page(level, name)
                    if rid:
                        matched.append((c['id'], label, emb, rid))
                    else:
                        missing.append((c['id'], label, emb, level, name))
                continue

            level, name = ref
            rid = resolve_page(level, name)
            if rid:
                matched.append((c['id'], label, part, rid))
            else:
                missing.append((c['id'], label, part, level, name))

print(f'\nMatched jumps: {len(matched)}')
print(f'Skipped non-page actions: {len(skipped)}')
print(f'MISSING destinations: {len(missing)}')
print('='*80)

# Deduplicate missing by target name
by_target = defaultdict(list)
for src, label, dst, level, name in missing:
    by_target[(level, name)].append((src, label, dst))

print(f'\nUnique missing targets: {len(by_target)}\n')
for (level, name), refs in sorted(by_target.items(), key=lambda x: (x[0][0], x[0][1])):
    print(f'  [{level}] {name}')
    for src, label, dst in refs[:5]:
        print(f'      <- {src} | click="{label}" | dst="{dst}"')
    if len(refs) > 5:
        print(f'      ... +{len(refs)-5} more')

# Also check reverse: pages with no inbound edges/clicks?
print('\n' + '='*80)
print('Pages never referenced as click destination (informational):')
referenced_ids = set()
for _,_,_,rid in matched:
    referenced_ids.add(rid)
# also from DEFAULT_EDGES
de = h.index('var DEFAULT_EDGES = [') + 20
depth = 0
for i in range(de, len(h)):
    if h[i] == '[': depth += 1
    elif h[i] == ']':
        depth -= 1
        if depth == 0:
            dend = i + 1
            break
edges = json.loads(h[de:dend])
for e in edges:
    referenced_ids.add(e[0]); referenced_ids.add(e[1])

orphans = [c for c in catalog if c['id'] not in referenced_ids]
print(f'  count={len(orphans)}')
for c in orphans:
    print(f'  {c["id"]} | {c["title"][:40]}')

# Save report
report = {
    'missing_unique': [
        {'level': lv, 'name': nm, 'refs': [{'src': s, 'click': c, 'dst': d} for s,c,d in refs]}
        for (lv, nm), refs in sorted(by_target.items())
    ],
    'matched_count': len(matched),
    'missing_count': len(missing),
}
Path(r'C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Game UI\Interface Planning\_nav_audit.json').write_text(
    json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print('\nWrote _nav_audit.json')
