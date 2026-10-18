# -*- coding: utf-8 -*-
"""Fix nav completeness: alias clicks + create missing placeholder pages."""
import json, re, sys, io
from pathlib import Path

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
BOARD = Path(r"C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Game UI\Interface Planning\Airline_CEO__UI交互白板 V1.0.html")
h = BOARD.read_text(encoding='utf-8')

# ===== CONTENT_REV =====
h = h.replace("var CONTENT_REV = '20260810q';", "var CONTENT_REV = '20260810r';")
print("CONTENT_REV -> 20260810r")

# ===== Load CATALOG =====
cs = h.index('var CATALOG = [')
cat_start = cs + 14
depth = 0
for i in range(cat_start, len(h)):
    if h[i] == '[': depth += 1
    elif h[i] == ']':
        depth -= 1
        if depth == 0:
            cat_end = i + 1
            break
catalog = json.loads(h[cat_start:cat_end])
print(f"Loaded {len(catalog)} catalog entries")

# ===== 1) Fix alias clicks (point to existing pages) =====
ALIAS_FIXES = {
    # (source_id_prefix, old_dst_substring) -> new_dst
}
# Apply by scanning each click
alias_map = {
    "L4 危机/选项弹窗": "L4 危机/IRROPS 弹窗",
    "L4 负责人任命或报告": "L4 部门负责人任命",
    "L4 购机会议": "L4 购机决策会议",  # unify to new page name
}
alias_count = 0
for c in catalog:
    for click in c.get('clicks', []):
        if len(click) < 2: continue
        for old, new in alias_map.items():
            if old in click[1]:
                click[1] = click[1].replace(old, new)
                alias_count += 1
print(f"Alias click fixes: {alias_count}")

# ===== 2) Create missing placeholder pages =====
# n97-n107
new_pages = [
    {
        "id": "n97_购机决策会议",
        "level": "L4",
        "title": "🛩 购机决策会议",
        "meta": "机队 · 购买/替换飞机决策弹窗 · 待完善内容",
        "badge": "待填",
        "items": [
            "【占位】从机队「+ 购买飞机」或飞机详情「申请替换」进入",
            "议题：引进机型 · 数量 · 采购/租赁方式 · 交付窗口",
            "五部门意见卡：财务/运营/市场/机务/战略（红黄绿 + 量化意见）",
            "底部操作：批准 · 暂缓（补条件）· 拒绝",
            "待填：与 GDD 购机流程、融资方案、OEM 折扣的完整字段"
        ],
        "entry": "入口：L1 机队 → +购买飞机；L2 飞机详情 → 申请替换",
        "clicks": [
            ["批准", "生效并关闭，回机队"],
            ["暂缓", "L5 补条件表单"],
            ["拒绝", "关闭会议，回机队"],
            ["← 返回", "L1 机队"]
        ],
        "kind": "popup"
    },
    {
        "id": "n98_航线航班环选择器",
        "level": "L3",
        "title": "🔗 航线/航班环选择器",
        "meta": "飞机详情 · 分配航线子页 · 待完善内容",
        "badge": "待填",
        "items": [
            "【占位】从飞机详情「分配航线」进入",
            "可选航线列表：OD · 周班次 · 当前执飞机型 · 可用缺口",
            "可选航班环：环编号 · 覆盖航段 · 利用率影响预估",
            "校验：宵禁/噪音/CAT III/机型适航/机组资质",
            "待填：拖拽分配交互、冲突检测、一键最优推荐"
        ],
        "entry": "入口：L2 飞机详情 → 分配航线",
        "clicks": [
            ["确认分配", "写回飞机详情"],
            ["打开航班环构建器", "L3 航班环构建器"],
            ["← 返回", "L2 飞机详情"]
        ],
        "kind": "new"
    },
    {
        "id": "n99_招聘策略弹窗",
        "level": "L4",
        "title": "📋 招聘策略弹窗",
        "meta": "人力资源部 · 招聘方案审批 · 待完善内容",
        "badge": "待填",
        "items": [
            "【占位】从组织规划与招聘「关键空缺→发起招聘」进入",
            "岗位 · 人数 · 紧急度 · 预算",
            "策略选项：批量校招 / 猎头挖角 / 海外引进 / 内部转岗",
            "成本与周期预估 · 成功率（联动人才紧缺指数）",
            "待填：候选人漏斗、背调清单、与各部门需求对接字段"
        ],
        "entry": "入口：L2 组织规划与招聘 → 发起招聘",
        "clicks": [
            ["批准招聘方案", "回组织规划与招聘并刷新管道"],
            ["暂缓", "L5 补条件表单"],
            ["← 返回", "L2 组织规划与招聘"]
        ],
        "kind": "popup"
    },
    {
        "id": "n100_检查应对弹窗",
        "level": "L4",
        "title": "🏛 检查应对弹窗",
        "meta": "安全质量部 · 局方检查应对决策 · 待完善内容",
        "badge": "待填",
        "items": [
            "【占位】从局方与法规事务「局方检查→应对准备」进入",
            "检查类型/范围/日期 · 预检自查完成率",
            "应对选项：全力配合 / 重点整改后迎检 / 申请延期",
            "成本 · 时间 · 局方关系影响预估",
            "待填：检查单明细、责任部门任务分派、证据材料清单"
        ],
        "entry": "入口：L2 局方与法规事务 → 局方检查应对",
        "clicks": [
            ["确认应对方案", "执行并关闭"],
            ["← 返回", "L2 局方与法规事务"]
        ],
        "kind": "popup"
    },
    {
        "id": "n101_策略切换确认弹窗",
        "level": "L4",
        "title": "💡 策略切换确认弹窗",
        "meta": "安全质量部 · 安全文化惩罚/鼓励切换确认 · 待完善内容",
        "badge": "待填",
        "items": [
            "【占位】从安全文化与培训「切换惩罚/鼓励」进入",
            "当前文化类型 → 目标文化类型",
            "影响预估：短期报告量 · 隐瞒风险 · 合规率 · 品牌/监管观感",
            "确认 / 取消 · 不可逆提示（若有冷却期）",
            "待填：切换动画、过渡期规则、与 HR/运营联动说明"
        ],
        "entry": "入口：L2 安全文化与培训 → 切换惩罚/鼓励",
        "clicks": [
            ["确认切换", "回安全文化与培训并刷新标尺"],
            ["取消", "回安全文化与培训"],
            ["← 返回", "L2 安全文化与培训"]
        ],
        "kind": "popup"
    },
    {
        "id": "n102_谈判弹窗",
        "level": "L4",
        "title": "🤝 谈判弹窗",
        "meta": "战略发展部 · 代码共享/联营双边谈判 · 待完善内容",
        "badge": "待填",
        "items": [
            "【占位】从联盟与战略合作「代码共享新签/调整」进入",
            "对方航司 · 谈判议题 · 分成比例滑块",
            "选项：强硬 / 折中 / 让步 · 预估收入与关系变化",
            "合规：反垄断审查提示",
            "待填：多轮谈判状态机、条款对照表、历史协议模板"
        ],
        "entry": "入口：L2 联盟与战略合作 → 代码共享新签/调整",
        "clicks": [
            ["确认协议条款", "回联盟与战略合作"],
            ["提交 CEO 审批", "L4 CEO 决策会议"],
            ["← 返回", "L2 联盟与战略合作"]
        ],
        "kind": "popup"
    },
    {
        "id": "n103_条款确认",
        "level": "L5",
        "title": "✅ 条款确认",
        "meta": "紧急融资等 · 贷款/增资/卖资产条款确认 · 待完善内容",
        "badge": "待填",
        "items": [
            "【占位】从紧急融资「贷款/增资/卖资产」进入",
            "条款摘要：金额 · 利率/股权稀释 · 期限 · 抵押物",
            "风险提示 · 确认勾选",
            "确认生效 / 返回修改",
            "待填：完整合同字段、法务审查标记、信用评级影响"
        ],
        "entry": "入口：L4 紧急融资/破产保护 → 贷款/增资/卖资产",
        "clicks": [
            ["确认生效", "回紧急融资并刷新现金"],
            ["返回修改", "L4 紧急融资/破产保护"]
        ],
        "kind": "new"
    },
    {
        "id": "n104_补条件表单",
        "level": "L5",
        "title": "📝 补条件表单",
        "meta": "CEO 决策会议暂缓 · 补条件后再议 · 待完善内容",
        "badge": "待填",
        "items": [
            "【占位】从 CEO 决策会议「暂缓」进入",
            "待补条件清单（财务/运营/安全等部门提出）",
            "每条：责任人 · 截止日期 · 完成标准",
            "提交后会议标记为暂缓，回原页",
            "待填：条件模板库、自动提醒、完成核验规则"
        ],
        "entry": "入口：L4 CEO 决策会议 / 购机决策会议 → 暂缓",
        "clicks": [
            ["提交补条件", "回上一会议页"],
            ["取消", "回上一会议页"]
        ],
        "kind": "new"
    },
    {
        "id": "n105_订单确认",
        "level": "L5",
        "title": "📦 订单确认",
        "meta": "航材采购订单审批确认 · 待完善内容",
        "badge": "待填",
        "items": [
            "【占位】从航材与供应链「采购订单→审批确认」进入",
            "订单明细：件号 · 数量 · 单价 · 供应商 · 交期",
            "预算占用预览 · 紧急订单标记",
            "确认下单 / 退回修改",
            "待填：多供应商比价表、合同附件、到货验收链路"
        ],
        "entry": "入口：L2 航材与供应链 → 采购订单审批",
        "clicks": [
            ["确认下单", "回航材与供应链"],
            ["退回修改", "L2 航材与供应链"]
        ],
        "kind": "new"
    },
    {
        "id": "n106_重组条件勾选",
        "level": "L5",
        "title": "🏛 重组条件勾选",
        "meta": "破产保护 · Chapter11 式重组条件 · 待完善内容",
        "badge": "待填",
        "items": [
            "【占位】从紧急融资「申请破产保护」进入",
            "强制条件清单（如砍 50% 亏损航线等）勾选确认",
            "各条件影响预览：运力 · 品牌 · 员工 · 董事会反应",
            "确认进入重组 / 放弃",
            "待填：完整重组条款、债权人谈判、法院进度条"
        ],
        "entry": "入口：L4 紧急融资/破产保护 → 申请破产保护",
        "clicks": [
            ["确认进入重组", "回紧急融资并触发重组流程"],
            ["放弃", "L4 紧急融资/破产保护"]
        ],
        "kind": "new"
    },
    {
        "id": "n107_预算确认",
        "level": "L5",
        "title": "💰 预算确认",
        "meta": "飞机大修等预算确认 · 待完善内容",
        "badge": "待填",
        "items": [
            "【占位】从飞机详情「安排大修」进入",
            "大修类型（A/B/C/D）· 预估费用 · 停场天数",
            "预算来源：机务预算 / 追加申请",
            "确认排程 / 返回",
            "待填：与机务甘特图联动、机会成本、保险覆盖"
        ],
        "entry": "入口：L2 飞机详情 → 安排大修",
        "clicks": [
            ["确认排程", "回飞机详情"],
            ["返回", "L2 飞机详情"]
        ],
        "kind": "new"
    },
]

# Avoid duplicates if re-run
existing_ids = {c['id'] for c in catalog}
added = []
for p in new_pages:
    if p['id'] not in existing_ids:
        catalog.append(p)
        added.append(p['id'])
    else:
        print(f"  skip existing {p['id']}")
print(f"Added pages: {len(added)} -> {added}")

# ===== Update source clicks that pointed to missing names =====
# n9: already "L4 购机决策会议" - good once n97 exists
# n10: "L3 航线/航班环选择器" - keep name matching n98 title
# n10: "L4/L5 预算确认" -> ensure "L5 预算确认"
# n10: "邮件 + L4 购机会议" already aliased to 购机决策会议
# n13: already aliased to 部门负责人任命
# n21: "L5 补条件表单" - good
# n25: "L5 条款确认", "L5 重组条件勾选" - good
# n30: already aliased
# n78: "L4 谈判弹窗" - good
# n83: "L5 订单确认" - good
# n86: "L4 招聘策略弹窗" - good
# n95: "L4 检查应对弹窗" - need ensure click text is clean
# n96: "L4 策略切换确认弹窗" - good

for c in catalog:
    if c['id'].startswith('n10'):
        for click in c.get('clicks', []):
            if '预算确认' in click[1] and 'L5 预算确认' not in click[1]:
                click[1] = 'L5 预算确认'
            if '航线/航班环选择器' in click[1]:
                click[1] = 'L3 航线/航班环选择器'
    if c['id'].startswith('n95'):
        for click in c.get('clicks', []):
            if '检查应对' in click[1]:
                click[1] = 'L4 检查应对弹窗'
    if c['id'].startswith('n86'):
        for click in c.get('clicks', []):
            if '招聘策略' in click[1]:
                click[1] = 'L4 招聘策略弹窗'
    if c['id'].startswith('n96'):
        for click in c.get('clicks', []):
            if '策略切换' in click[1]:
                click[1] = 'L4 策略切换确认弹窗'
    if c['id'].startswith('n78'):
        for click in c.get('clicks', []):
            if '谈判弹窗' in click[1]:
                click[1] = 'L4 谈判弹窗'
    if c['id'].startswith('n9_机队') or c['id'] == 'n9_机队':
        for click in c.get('clicks', []):
            if '购机' in click[1]:
                click[1] = 'L4 购机决策会议'

# Write CATALOG back (full JSON array including brackets)
new_cat = json.dumps(catalog, ensure_ascii=False)
ob = h.index('var CATALOG = [') + len('var CATALOG = ')
d = 0
close = None
for i in range(ob, len(h)):
    if h[i] == '[': d += 1
    elif h[i] == ']':
        d -= 1
        if d == 0:
            close = i + 1
            break
h = h[:ob] + new_cat + h[close:]
print("CATALOG written")

# ===== Add edges =====
de_base = h.index("var DEFAULT_EDGES = [")
de = de_base + 20
depth = 0
de_cl = de
while de_cl < len(h):
    if h[de_cl] == '[': depth += 1
    elif h[de_cl] == ']':
        depth -= 1
        if depth == 0: break
    de_cl += 1

NEW_EDGES = """
  ["n9_机队", "n97_购机决策会议", "+购买飞机"],
  ["n97_购机决策会议", "n9_机队", "返回机队"],
  ["n10_飞机详情_履历", "n97_购机决策会议", "申请替换"],
  ["n10_飞机详情_履历", "n98_航线航班环选择器", "分配航线"],
  ["n98_航线航班环选择器", "n10_飞机详情_履历", "返回"],
  ["n98_航线航班环选择器", "n36_航班环构建器", "打开航班环构建器"],
  ["n10_飞机详情_履历", "n107_预算确认", "安排大修"],
  ["n107_预算确认", "n10_飞机详情_履历", "返回"],
  ["n21_CEO_决策会议", "n104_补条件表单", "暂缓"],
  ["n97_购机决策会议", "n104_补条件表单", "暂缓"],
  ["n104_补条件表单", "n21_CEO_决策会议", "返回会议"],
  ["n25_紧急融资_破产保护", "n103_条款确认", "贷款/增资/卖资产"],
  ["n103_条款确认", "n25_紧急融资_破产保护", "返回"],
  ["n25_紧急融资_破产保护", "n106_重组条件勾选", "申请破产保护"],
  ["n106_重组条件勾选", "n25_紧急融资_破产保护", "返回"],
  ["n83_航材与供应链", "n105_订单确认", "采购订单审批"],
  ["n105_订单确认", "n83_航材与供应链", "返回"],
  ["n86_组织规划与招聘", "n99_招聘策略弹窗", "发起招聘"],
  ["n99_招聘策略弹窗", "n86_组织规划与招聘", "返回"],
  ["n95_局方与法规事务", "n100_检查应对弹窗", "局方检查应对"],
  ["n100_检查应对弹窗", "n95_局方与法规事务", "返回"],
  ["n96_安全文化与培训", "n101_策略切换确认弹窗", "切换惩罚/鼓励"],
  ["n101_策略切换确认弹窗", "n96_安全文化与培训", "返回"],
  ["n78_联盟与战略合作", "n102_谈判弹窗", "代码共享谈判"],
  ["n102_谈判弹窗", "n78_联盟与战略合作", "返回"],
  ["n102_谈判弹窗", "n21_CEO_决策会议", "提交CEO审批"],"""

# Avoid double-adding if edges already present
if 'n97_购机决策会议' not in h[de:de_cl]:
    h = h[:de_cl] + "," + NEW_EDGES + h[de_cl:]
    print("Added nav edges")
else:
    print("Nav edges already present, skip")

BOARD.write_text(h, encoding='utf-8')
print("Written. Verifying...")

# ===== VERIFY with same audit logic (simplified) =====
h2 = BOARD.read_text(encoding='utf-8')
cs2 = h2.index('var CATALOG = [') + 14
depth = 0
for i in range(cs2, len(h2)):
    if h2[i] == '[': depth += 1
    elif h2[i] == ']':
        depth -= 1
        if depth == 0:
            ce2 = i + 1
            break
cat2 = json.loads(h2[cs2:ce2])
ids = {c['id'] for c in cat2}
print(f"CATALOG: {len(cat2)}  last={cat2[-1]['id']}")

# Check key pages exist
for pid in ['n97_购机决策会议','n98_航线航班环选择器','n99_招聘策略弹窗','n100_检查应对弹窗',
            'n101_策略切换确认弹窗','n102_谈判弹窗','n103_条款确认','n104_补条件表单',
            'n105_订单确认','n106_重组条件勾选','n107_预算确认']:
    print(f"  {'OK' if pid in ids else 'MISSING'} {pid}")

de = h2.index('var DEFAULT_EDGES = [') + 20
depth = 0
for i in range(de, len(h2)):
    if h2[i] == '[': depth += 1
    elif h2[i] == ']':
        depth -= 1
        if depth == 0:
            dend = i + 1
            break
edges = json.loads(h2[de:dend])
print(f"EDGES: {len(edges)}")

# Fix trailing comma if any
if h2[dend-2:dend+1] in (',];', ',]\n'):
    print("WARN trailing comma")
else:
    print("EDGES end OK:", repr(h2[dend-3:dend+2]))

js = h2.split('<script>')[1].split('</script>')[0]
print(f"braces {js.count('{')}/{js.count('}')} brackets {js.count('[')}/{js.count(']')}")
m = re.search(r"CONTENT_REV\s*=\s*'([^']+)'", h2)
print(f"CONTENT_REV: {m.group(1)}")

# Re-run quick missing check for the known 15
TARGETS = [
    ("L4 购机决策会议", "n97"),
    ("L3 航线/航班环选择器", "n98"),
    ("L4 招聘策略弹窗", "n99"),
    ("L4 检查应对弹窗", "n100"),
    ("L4 策略切换确认弹窗", "n101"),
    ("L4 谈判弹窗", "n102"),
    ("L5 条款确认", "n103"),
    ("L5 补条件表单", "n104"),
    ("L5 订单确认", "n105"),
    ("L5 重组条件勾选", "n106"),
    ("L5 预算确认", "n107"),
]
print("\nTarget resolution:")
for name, prefix in TARGETS:
    hit = [c for c in cat2 if c['id'].startswith(prefix) or name.replace('L4 ','').replace('L3 ','').replace('L5 ','') in c['title']]
    print(f"  {name} -> {[c['id'] for c in hit]}")

print("DONE")
