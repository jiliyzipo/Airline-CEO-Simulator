# -*- coding: utf-8 -*-
"""Sync advisor opening guide + inbox/fleet/company-reg changes into the whiteboard."""
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

NEW_REV = "20260814"


def load_array(h, marker):
    cs = h.index(marker) + len(marker)
    depth = 0
    for i in range(cs, len(h)):
        if h[i] == "[":
            depth += 1
        elif h[i] == "]":
            depth -= 1
            if depth == 0:
                return cs, i + 1, json.loads(h[cs:i + 1])
    raise SystemExit("array not found: " + marker)


def load_object(h, marker):
    cs = h.index(marker) + len(marker)
    depth = 0
    for i in range(cs, len(h)):
        if h[i] == "{":
            depth += 1
        elif h[i] == "}":
            depth -= 1
            if depth == 0:
                return cs, i + 1, json.loads(h[cs:i + 1])
    raise SystemExit("object not found: " + marker)


def replace_containing(lst, old_sub, new):
    """Replace in a list of strings, or in [click, dst] pairs (dst = index 1)."""
    hit = False
    for i, x in enumerate(lst):
        if isinstance(x, str) and old_sub in x:
            lst[i] = new
            hit = True
        elif isinstance(x, list) and len(x) >= 2 and isinstance(x[1], str) and old_sub in x[1]:
            x[1] = new
            hit = True
    return hit


def main():
    h = BOARD.read_text(encoding="utf-8")

    # CONTENT_REV
    h, n = re.subn(r"var CONTENT_REV = '[^']+';", f"var CONTENT_REV = '{NEW_REV}';", h, count=1)
    if n != 1:
        raise SystemExit(f"CONTENT_REV replace failed: {n}")

    note = "  /* UX 2026-08-14: 顾问开局引导线 S0–S6 落地；收件箱双栏+引导提案；机队分配引导；公司中文名校验；排班调整弹窗（频率×时刻+自动排班） */\n"
    if "UX 2026-08-14" not in h:
        h = h.replace(f"var CONTENT_REV = '{NEW_REV}';", f"var CONTENT_REV = '{NEW_REV}';\n{note}", 1)

    # CATALOG
    cs, ce, catalog = load_array(h, "var CATALOG = ")
    by_id = {c["id"]: c for c in catalog}

    # --- n8 顾问弹窗：整页替换为开局引导线 ---
    n8 = by_id["n8_航空事务官_AO"]
    n8["title"] = "💬 航空顾问（开局引导线）"
    n8["meta"] = "顶栏「顾问」打开；新档开局约 0.5s 自动弹出 S0–S6 顾问开局引导线（《航空顾问开局引导线》v1.1）"
    n8["badge"] = "开局引导"
    n8["items"] = [
        "入口：顶栏绿色「顾问」按钮；新档进入主页后约 0.5s 自动弹出引导",
        "布局：居中占满顶栏与底栏之间；左侧顾问头像 + 标题 + 正文 + 底部按钮条",
        "顶部右侧：跳过（一键结束引导）· ×（暂缓收起，可再由顶栏「顾问」重开）",
        "进度持久化：GameState.advisorTutorialStep（0=S0 … 7=已完成）+ advisorTutorialAwaitPage（收件箱/机队等待态）",
        "S0 市场机会：深圳—北京 O&D 客流激增情报；「查看详细评估」/「稍后再说」",
        "S1 RPM 评估：综合评分 86（绿）· 日利润 16.2 万 · 首年客座率 81%；「发起决策会议」跳收件箱",
        "S2 决策会议：财务(审慎)·运营(支持)·市场(强烈支持)；「批准开航」/「暂缓」/「拒绝」",
        "S3 分配飞机：推荐闲置机型；「去机队」跳机队点「分配至深圳—北京」",
        "S4 排班确认：自动排班方案；「确认排班」/「调整班次」",
        "S4 调整班次弹窗：班次频率(每日/每周5班/每周3班) × 起飞时刻(早/午/晚/红眼) + 自动排班 + 预估日利润预览",
        "S5 首航结算：起飞→巡航→降落动画；净利润 6.2 万 · 满意度 +5 · 客座率 89%",
        "S6 下一程钩子：成都—上海机会；「打开 RPM 评估」/「先熟悉一下界面」",
    ]
    n8["clicks"] = [
        ["右上角 ×", "暂缓引导并收起弹窗（回到当前底栏页）"],
        ["「跳过」", "一键结束引导，顾问进入待命"],
        ["S0「查看详细评估」", "S1 RPM 评估"],
        ["S1「发起决策会议」", "收起弹窗跳收件箱；收件箱点「发起会议」后回弹 S2"],
        ["S2「批准开航」", "S3 分配飞机"],
        ["S3「去机队」", "收起弹窗跳机队；机队点「分配至深圳—北京」后回弹 S4"],
        ["S4「调整班次」", "打开频率×时刻编辑弹窗（可自动排班/手动选）"],
        ["S4「确认排班」", "扣启动资金、开通航线、播放 S5 首航结算"],
        ["S6「打开 RPM 评估」", "结束引导并跳转市场销售部"],
    ]

    # --- n7 收件箱：双栏 + 引导提案 ---
    n7 = by_id["n7_收件箱"]
    n7["meta"] = "左列表 + 右详情（双栏）"
    n7["items"] = [
        "双栏布局：左侧邮件列表（头像/发件人/时间/主题摘要 + 状态角标）；右侧邮件详情",
        "邮件状态标签（中文）：待处理 / 已批准 / 已暂缓 / 已拒绝（按状态着色）",
        "邮件字段：发件部门/人 · 角色 · 头像色块 · 时间 · 主题 · 正文 · 角标（紧急/提案）",
        "操作按钮（详情底部）：发起会议 / 批准 / 暂缓 / 拒绝",
        "信息范围：所有发给 CEO 的信息与待办事项均以邮件送达（类型可扩展）",
        "待办事件（含暂缓）：自收件日起第 7 日截止；到期强制跳转该邮件，须批准或拒绝后方可继续游戏",
        "开局引导：自动插入战略发展部「深圳—北京航线建议」紧急提案（角标「紧急」），点「发起会议」继续引导",
    ]
    n7["clicks"] = [
        ["左侧邮件行", "右侧详情（同页）"],
        ["发起会议", "引导中：回弹顾问 S2 决策会议；常规：L4 CEO 决策会议"],
        ["批准", "生效并更新相关系统"],
        ["暂缓 / 拒绝", "标记状态；待办仍受 7 日截止约束"],
    ]

    # --- n9 机队：追加引导分配 ---
    n9 = by_id["n9_机队"]
    n9_items = list(n9.get("items", []))
    n9_items.append("开局引导：推荐机型的操作按钮显示「分配至深圳—北京」（绿色高亮），点击回弹顾问 S4 排班确认")
    n9["items"] = n9_items
    n9_clicks = list(n9.get("clicks", []))
    n9_clicks.append(["分配至深圳—北京（引导中）", "回弹顾问 S4 排班确认"])
    n9["clicks"] = n9_clicks

    # --- n3 公司注册：中文名校验 ---
    replace_containing(by_id["n3_公司注册"]["items"], "公司中文名",
                       "公司中文名（玩家填写，默认空；须为汉字 ≥2 个，可含 ·）")

    # --- n5 年度目标确认：开局自动弹顾问 ---
    replace_containing(by_id["n5_年度目标确认"]["items"], "不自动弹出顾问",
                       "确认后进入主页；新档开局约 0.5s 自动弹出顾问开局引导线 S0")
    replace_containing(by_id["n5_年度目标确认"]["items"], "顾问正文内容待后续指令填充",
                       "顶栏「顾问」可随时手动重开；引导完成后顾问进入待命")

    # --- n6 主页：顾问正文已填充 ---
    by_id["n6_主页_Dashboard"]["meta"] = "空壳舞台：KPI/待办/快捷入口已迁至顶栏「顾问」弹窗（承载开局引导线 S0–S6）"
    replace_containing(by_id["n6_主页_Dashboard"]["items"], "顾问弹窗正文内容待下一步指令加入",
                       "顾问弹窗承载开局引导线 S0–S6（正文已填充）")
    replace_containing(by_id["n6_主页_Dashboard"]["clicks"], "打开居中顾问弹窗",
                       "打开居中顾问弹窗（开局引导线 S0–S6）")

    # --- n0 顶栏顾问点击描述 ---
    replace_containing(by_id["n0_主界面框架_常驻"]["clicks"], "正文内容待补",
                       "打开居中顾问弹窗（承载开局引导线 S0–S6）")

    new_cat = json.dumps(catalog, ensure_ascii=False, separators=(",", ":"))
    h = h[:cs] + new_cat + h[ce:]

    # DEFAULT_EDGES
    es, ee, edges = load_array(h, "var DEFAULT_EDGES = ")
    adds = [
        ["n8_航空事务官_AO", "n7_收件箱", "发起决策会议"],
        ["n8_航空事务官_AO", "n9_机队", "去机队"],
    ]
    existing = {(e[0], e[1], e[2] if len(e) > 2 else "") for e in edges if isinstance(e, list)}
    for e in adds:
        if (e[0], e[1], e[2]) not in existing:
            edges.append(e)
            print("added edge", e)
    h = h[:es] + json.dumps(edges, ensure_ascii=False, separators=(",", ":")) + h[ee:]

    # NOTE_FEATURE_PACK
    marker = "var NOTE_FEATURE_PACK = "
    if marker in h:
        ns, ne, pack = load_object(h, marker)
        for pid in ["n8_航空事务官_AO", "n7_收件箱", "n9_机队", "n3_公司注册",
                    "n5_年度目标确认", "n6_主页_Dashboard", "n0_主界面框架_常驻"]:
            c = by_id[pid]
            pack[pid] = {
                "items": c.get("items", []),
                "clicks": c.get("clicks", []),
            }
            if "title" in c:
                pack[pid]["title"] = c["title"]
            if "meta" in c:
                pack[pid]["meta"] = c["meta"]
        h = h[:ns] + json.dumps(pack, ensure_ascii=False, separators=(",", ":")) + h[ne:]
        print("updated NOTE_FEATURE_PACK")

    BOARD.write_text(h, encoding="utf-8")
    print("OK wrote", BOARD.name, "CONTENT_REV=", NEW_REV)


if __name__ == "__main__":
    main()
