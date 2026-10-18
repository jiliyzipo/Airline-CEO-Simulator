# -*- coding: utf-8 -*-
"""Sync recent UI changes into the interactive whiteboard CATALOG."""
import json
import re
import sys
import io
from pathlib import Path

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

BOARD = Path(
    r"C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Game UI\Interface Planning"
    r"\Airline_CEO__UI交互白板 V1.0.html"
)

NEW_REV = "20260813b"

# --- Patch definitions ---
PATCHES = {
    "n0_主界面框架_常驻": {
        "title": "🖥️ 主界面框架（常驻）",
        "meta": "线框图核心：地图为舞台，UI 为覆层；顶栏顾问替代旧右下角 AO/战略顾问浮窗",
        "items": [
            "顶部栏：公司 LOGO · 公司名称 · CEO 名字 · 日期/星期 · 经济阶段",
            "顶部栏按钮：设置 · 顾问（打开居中顾问弹窗）",
            "速度控制：▶（1×）→ ▶▶（2×）→ ▶▶▶（4×）→ ‖（暂停）；无 8 倍速",
            "底部主菜单 Tab：主页 / 收件箱 / 机队 / 航线 | 运营 / 市场 / 财务 / 战略 / 机务 / 人力 / 安全 | 董事会",
            "分隔竖线：航线与运营之间；安全与董事会之间",
            "驾驶舱氛围底：左侧装饰栏色带（无按钮）+ 右侧内容区衬底；非旧左侧导航菜单",
            "地图舞台：世界卫星地图（昼夜）· 航线发光弧 · 飞机图标 · 枢纽光点 · 蓝色数据覆层",
            "已取消：右下角 AI/AO 圆形浮标、Neural Command「战略顾问」对话窗",
            "术语：专业词虚线下划线 → Tooltip",
            "暂停态：边缘遮罩 +「已暂停」；飞机与进度冻结",
            "弹窗关闭约定：右上角 × = 一键关闭整层弹窗（清空叠层回到底层页）；← 返回 = 只退一层到父页。二者不得同功能。",
            "凡「计划」类页面（周总计划/详细计划/航季总纲等）正文默认表格排版，禁止管道符挤成单行",
            "表格行交互：光标移入某一行时该行微亮，便于读清内容",
        ],
        "clicks": [
            ["底部 Tab", "打开所选 Tab 对应的一级页"],
            ["速度键 ▶/▶▶/▶▶▶/‖", "切换 1×/2×/4×/暂停"],
            ["顶部「顾问」", "打开居中顾问弹窗（原 Dashboard KPI/快捷入口改由此承载；正文内容待补）"],
            ["顶部「设置」", "L2 设置/存档"],
            ["底部 Tab 战略", "L1 战略发展部"],
            ["底部 Tab 安全", "L1 安全质量部"],
            ["右上角 ×（任意叠层弹窗）", "关闭整层弹窗，回到打开前的 L1/玩法页"],
            ["← 返回（叠层弹窗）", "只退回上一层（如 n43→n42→n13）"],
        ],
    },
    "n3_公司注册": {
        "items": [
            "公司中文名（玩家填写，默认空）",
            "公司英文名（拉丁字母，可含数字/空格/-）",
            "IATA代码：2 位字母，或字母与数字组合（如 CA、U2、3U；禁止两位纯数字）",
            "ICAO代码：限定三个字母（如 CCA）",
            "总部基地：机场列表选择（IATA 机场码）",
            "启动资金档位：循环选择（如 300/500/800/1200 万）",
            "确认注册 → 进入董事会组建（抽卡）",
        ],
        "clicks": [
            ["← 返回", "L0 玩家资料创建"],
            ["总部基地·选择", "打开机场列表选择器"],
            ["启动资金 ◀/▶", "切换资金档位"],
            ["确认注册 →", "L0 董事会组建（抽卡）"],
        ],
    },
    "n5_年度目标确认": {
        "items": [
            "展示本财年董事会考核目标清单（阈值 · 门槛 · 单位）",
            "确认后进入主页（空壳舞台）；不自动弹出顾问",
            "顶栏「顾问」由玩家手动打开；顾问正文内容待后续指令填充",
            "原「AO 强制引导 / 战略顾问对话」已取消",
        ],
        "clicks": [
            ["← 返回", "L0 董事会组建（抽卡）"],
            ["开始经营 / 确认", "L1 主页（空壳）"],
        ],
    },
    "n6_主页_Dashboard": {
        "title": "🏠 主页",
        "meta": "空壳舞台：KPI/待办/快捷入口已迁至顶栏「顾问」弹窗（内容待补）",
        "badge": "空壳",
        "items": [
            "本页为底栏「主页」Tab 的空壳：不展示 KPI / 待办文案 / 快捷按钮条",
            "视觉：左侧装饰栏 + 右侧内容衬底 + 地图舞台（与 n0 氛围底一致）",
            "原 Dashboard 功能（现金跑道、待办、董事会、竞争、宏观、航线快捷跳转）改由顶栏「顾问」弹窗承载",
            "顾问弹窗正文内容待下一步指令加入；当前仅标题「顾问」+ 关闭 ×",
            "切换其他底栏 Tab 时自动关闭顾问弹窗",
        ],
        "clicks": [
            ["（本页无正文点击项）", "功能入口见顶栏「顾问」与底栏 Tab"],
            ["顶部「顾问」", "打开居中顾问弹窗"],
        ],
    },
    "n8_航空事务官_AO": {
        "title": "💬 顾问弹窗",
        "meta": "由顶栏「顾问」打开的居中主内容区弹窗；替代旧右下角 AO/战略顾问",
        "badge": "入口改",
        "items": [
            "入口：顶栏绿色「顾问」按钮（不再使用右下角 AO/AI 浮标）",
            "布局：居中占满顶栏与底栏之间的主内容区（左侧装饰栏右侧）",
            "标题「顾问」+ 右上角 × 关闭",
            "正文内容待后续指令填充（原 Dashboard KPI/快捷、原 AO 预设跳转将迁入此处）",
            "已取消：Neural Command「战略顾问」输入指令对话窗",
            "已取消：开局强制打开本页/强制 AO 引导",
        ],
        "clicks": [
            ["右上角 ×", "关闭顾问弹窗，回到当前底栏页"],
            ["顶部「顾问」（再次点击）", "开关顾问弹窗"],
            ["（正文点击项）", "待内容填充后补充"],
        ],
    },
}

EDGE_REMOVE = {
    ("n6_主页_Dashboard", "n8_航空事务官_AO"),
    ("n6_主页_Dashboard", "n7_收件箱"),
    ("n6_主页_Dashboard", "n15_财务部"),
    ("n6_主页_Dashboard", "n4_董事会组建_抽卡"),
    ("n6_主页_Dashboard", "n28_宏观经济面板"),
    ("n6_主页_Dashboard", "n29_竞争态势_AI_航司"),
    ("n0_主界面框架_常驻", "n8_航空事务官_AO"),  # 重建为「顶部顾问」；去掉旧「右下角 AO」
}

EDGE_ADD = [
    ["n0_主界面框架_常驻", "n8_航空事务官_AO", "顶部「顾问」"],
]


def load_json_array(h: str, marker: str):
    cs = h.index(marker) + len(marker)
    depth = 0
    ce = None
    for i in range(cs, len(h)):
        if h[i] == "[":
            depth += 1
        elif h[i] == "]":
            depth -= 1
            if depth == 0:
                ce = i + 1
                break
    return cs, ce, json.loads(h[cs:ce])


def main():
    h = BOARD.read_text(encoding="utf-8")

    # CONTENT_REV
    h2, n = re.subn(
        r"var CONTENT_REV = '[^']+';",
        f"var CONTENT_REV = '{NEW_REV}';",
        h,
        count=1,
    )
    if n != 1:
        raise SystemExit(f"CONTENT_REV replace failed: {n}")
    h = h2

    # UX note
    note = "  /* UX 2026-08-13b: 顶栏顾问替代右下角AO/战略顾问；主页空壳；公司注册 IATA+ICAO代码；左侧装饰栏氛围底 */\n"
    if "UX 2026-08-13b" not in h:
        h = h.replace(
            f"var CONTENT_REV = '{NEW_REV}';",
            f"var CONTENT_REV = '{NEW_REV}';\n{note}",
            1,
        )

    # CATALOG
    cs, ce, catalog = load_json_array(h, "var CATALOG = ")
    by_id = {c["id"]: c for c in catalog}
    for pid, patch in PATCHES.items():
        if pid not in by_id:
            raise SystemExit(f"missing {pid}")
        c = by_id[pid]
        for k, v in patch.items():
            c[k] = v
        print(f"patched {pid}")

    new_cat = json.dumps(catalog, ensure_ascii=False, separators=(",", ":"))
    h = h[:cs] + new_cat + h[ce:]

    # DEFAULT_EDGES
    es, ee, edges = load_json_array(h, "var DEFAULT_EDGES = ")
    filtered = []
    for e in edges:
        key = (e[0], e[1]) if isinstance(e, list) and len(e) >= 2 else None
        if key in EDGE_REMOVE:
            print("removed edge", e)
            continue
        filtered.append(e)
    # avoid dup adds
    existing = {(e[0], e[1], e[2] if len(e) > 2 else "") for e in filtered if isinstance(e, list)}
    for e in EDGE_ADD:
        tup = (e[0], e[1], e[2])
        if tup not in existing:
            filtered.append(e)
            print("added edge", e)
    new_edges = json.dumps(filtered, ensure_ascii=False, separators=(",", ":"))
    h = h[:es] + new_edges + h[ee:]

    # NOTE_FEATURE_PACK if present
    marker = "var NOTE_FEATURE_PACK = "
    if marker in h:
        ns = h.index(marker) + len(marker)
        # object not array
        depth = 0
        ne = None
        for i in range(ns, len(h)):
            if h[i] == "{":
                depth += 1
            elif h[i] == "}":
                depth -= 1
                if depth == 0:
                    ne = i + 1
                    break
        pack = json.loads(h[ns:ne])
        for pid, patch in PATCHES.items():
            pack[pid] = {
                "items": patch.get("items", pack.get(pid, {}).get("items", [])),
                "clicks": patch.get("clicks", pack.get(pid, {}).get("clicks", [])),
            }
            if "title" in patch:
                pack[pid]["title"] = patch["title"]
            if "meta" in patch:
                pack[pid]["meta"] = patch["meta"]
        new_pack = json.dumps(pack, ensure_ascii=False, separators=(",", ":"))
        h = h[:ns] + new_pack + h[ne:]
        print("updated NOTE_FEATURE_PACK")

    BOARD.write_text(h, encoding="utf-8")
    print("OK wrote", BOARD.name, "CONTENT_REV=", NEW_REV)


if __name__ == "__main__":
    main()
