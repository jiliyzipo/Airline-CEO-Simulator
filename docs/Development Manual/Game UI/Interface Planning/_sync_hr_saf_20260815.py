# -*- coding: utf-8 -*-
"""Sync HR 人力 + SAF 安全 金标准（Phase 2 Week 10）into the whiteboard."""
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

NEW_REV = "20260815d"


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


def main():
    h = BOARD.read_text(encoding="utf-8")

    h, n = re.subn(r"var CONTENT_REV = '[^']+';", f"var CONTENT_REV = '{NEW_REV}';", h, count=1)
    if n != 1:
        raise SystemExit(f"CONTENT_REV replace failed: {n}")

    note = (
        "  /* UX 2026-08-15d: HR 人力 + SAF 安全 金标准 — "
        "n18 人力资源部 L1 + n86 组织规划招聘 + n87 培训发展 + n88 绩效薪酬 + n89 员工关系 + "
        "n90 合规风险 + n37 工会谈判桌 + n91 人才档案继任 + n99 招聘策略弹窗；"
        "n19 安全质量部 L1 + n92 SMS风险管理 + n93 安全监察审核 + n94 事件调查 + "
        "n95 局方法规事务 + n96 安全文化培训 + n100 检查应对弹窗 + n101 策略切换确认弹窗；"
        "HrEconomics/SafetyEconomics + HrManager/SafetyManager 回写 GameState.hr/.saf；"
        "tools/balance HR/SAF 年闭环；PlayMode≥4 */\n"
    )
    if "UX 2026-08-15d" not in h:
        h = h.replace(f"var CONTENT_REV = '{NEW_REV}';", f"var CONTENT_REV = '{NEW_REV}';\n{note}", 1)

    cs, ce, catalog = load_array(h, "var CATALOG = ")
    by_id = {c["id"]: c for c in catalog}

    pages = {
        "n18_人力资源部": (
            "人力 · L1 总览（人力仪表/人才紧缺指数/关键岗位空缺/员工满意度/年流失率/培训ROI/七大功能入口/工会罢工风险/薪酬合规）",
            "金标准",
        ),
        "n86_组织规划与招聘": (
            "人力 · 组织规划与招聘（组织架构/岗位空缺看板/招聘管道/招聘渠道四选一/招聘成本成功率周期/招聘策略弹窗/到岗回写）",
            "金标准",
        ),
        "n87_培训与发展": (
            "人力 · 培训与发展（培训项目看板/课程体系/培训中心等级升级/培训ROI/机型改装培训/资质授权/职业发展路径）",
            "金标准",
        ),
        "n88_绩效与薪酬": (
            "人力 · 绩效与薪酬管理（薪酬结构/绩效体系/人力成本占营收比/激励方案/薪酬对标/绩效结果回写）",
            "金标准",
        ),
        "n89_员工关系与文化": (
            "人力 · 员工关系与企业文化（员工满意度/离职分析/企业文化指数/劳资关系健康度/员工关系事件/软环境构建）",
            "金标准",
        ),
        "n90_合规与风险": (
            "人力 · 合规与风险管理（劳动合规/薪酬合规/用工风险看板/违规记录/风险防火墙/整改跟踪）",
            "金标准",
        ),
        "n37_工会谈判桌": (
            "人力 · 工会谈判桌（谈判诉求/姿态三选一：让步/强硬/拖延/满意度与罢工风险实时预估/谈判结果回写）",
            "金标准",
        ),
        "n91_人才档案与继任": (
            "人力 · 人才档案与继任计划（关键人才库/继任者梯队/人才盘点/胜任力模型/离职风险标记）",
            "金标准",
        ),
        "n99_招聘策略弹窗": (
            "人力 · 招聘策略弹窗（渠道比选/预算分配/成本成功率周期预估/确认发起招聘回写n86）",
            "金标准",
        ),
        "n19_安全质量部": (
            "安全 · L1 总览（安全仪表/安全评级A-D徽章/零事故天数/审计通过率/安全文化指数/五大功能入口/局方关系/安全预算）",
            "金标准",
        ),
        "n92_SMS与风险管理": (
            "安全 · SMS与风险管理（安全管理体系/风险矩阵/隐患库/危险源识别/风险等级评估/纠正预防措施闭环）",
            "金标准",
        ),
        "n93_安全监察与审核": (
            "安全 · 安全监察与审核（监察计划/审核清单/审计通过率/不符合项/整改跟踪/独立监督力量）",
            "金标准",
        ),
        "n94_事件调查与分析": (
            "安全 · 事件调查与分析（事件台账/严重度分级/调查深度三选一/调查成本与周期/根因分析/结案回写）",
            "金标准",
        ),
        "n95_局方与法规事务": (
            "安全 · 局方与法规事务（CAAC/FAA/EASA关系/法规库/适航指令/检查应对弹窗/局方接管风险/改善成本）",
            "金标准",
        ),
        "n96_安全文化与培训": (
            "安全 · 安全文化与培训（安全文化类型：惩罚型/报告型切换/隐藏风险/主动报告率/安全培训体系/策略切换确认弹窗）",
            "金标准",
        ),
        "n100_检查应对弹窗": (
            "安全 · 检查应对弹窗（检查类型/应对策略/准备度评估/通过率预估/确认应对回写n95）",
            "金标准",
        ),
        "n101_策略切换确认弹窗": (
            "安全 · 策略切换确认弹窗（文化策略切换/影响预估/隐藏风险变化/确认切换回写n96）",
            "金标准",
        ),
    }
    for pid, (meta, badge) in pages.items():
        if pid not in by_id:
            raise SystemExit("missing page id: " + pid)
        by_id[pid]["meta"] = meta
        by_id[pid]["badge"] = badge

    new_cat = json.dumps(catalog, ensure_ascii=False, separators=(",", ":"))
    h = h[:cs] + new_cat + h[ce:]

    marker = "var NOTE_FEATURE_PACK = "
    if marker in h:
        ns, ne, pack = load_object(h, marker)
        for pid in pages:
            c = by_id[pid]
            pack[pid] = {
                "items": c.get("items", []),
                "clicks": c.get("clicks", []),
            }
            if "title" in c:
                pack[pid]["title"] = c["title"]
            if "meta" in c:
                pack[pid]["meta"] = c["meta"]
            if "badge" in c:
                pack[pid]["badge"] = c["badge"]
        h = h[:ns] + json.dumps(pack, ensure_ascii=False, separators=(",", ":")) + h[ne:]
        print("updated NOTE_FEATURE_PACK")

    BOARD.write_text(h, encoding="utf-8")
    print("OK wrote", BOARD.name, "CONTENT_REV=", NEW_REV)


if __name__ == "__main__":
    main()
