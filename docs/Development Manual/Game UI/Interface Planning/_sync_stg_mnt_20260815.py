# -*- coding: utf-8 -*-
"""Sync STG 战略 + MNT 机务 金标准（Phase 2 Week 9）into the whiteboard."""
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

NEW_REV = "20260815c"


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
        "  /* UX 2026-08-15c: STG 战略 + MNT 机务 金标准 — "
        "n16 战略发展部 L1 + n76 机队运力规划 + n77 航权时刻槽 + n78 联盟合作 + n79 投资并购 + "
        "n80 战略研究预案 + n35 RPM评估台 + n102 谈判弹窗；"
        "n17 机务工程部 L1 + n81 航线定检 + n82 工程技术 + n83 航材供应链 + n84 安全适航 + "
        "n85 维修培训 + n105 订单确认；"
        "StrategyEconomics/MaintenanceEconomics + StrategyManager/MaintenanceManager 回写 GameState.stg/.mnt；"
        "tools/balance STG/MNT 年闭环；PlayMode≥4 */\n"
    )
    if "UX 2026-08-15c" not in h:
        h = h.replace(f"var CONTENT_REV = '{NEW_REV}';", f"var CONTENT_REV = '{NEW_REV}';\n{note}", 1)

    cs, ce, catalog = load_array(h, "var CATALOG = ")
    by_id = {c["id"]: c for c in catalog}

    pages = {
        "n16_战略发展部": (
            "战略 · L1 总览（战略一致性评分/政府关系/联盟等级/核心KPI×4/六大功能入口/跨部门协同/地缘风险指数/宏观周期监测）",
            "金标准",
        ),
        "n76_机队与运力规划": (
            "战略 · 机队与运力规划（机队总览/五年规划时间轴/购机方案三维比选/OEM关系/发动机选型/飞机调配/客舱构型/退租检/运力利用率/财务影响预测）",
            "金标准",
        ),
        "n77_航权与时刻槽": (
            "战略 · 航权与时刻槽（航权地图/航权争夺战竞标评分/时刻槽交易市场/持仓管理/政府关系/航权类型/地缘风险/补贴政策红利）",
            "金标准",
        ),
        "n78_联盟与战略合作": (
            "战略 · 联盟与战略合作（联盟等级/三大联盟对比/代码共享/联营JV/并购雷达/协议看板/收益追踪/退出代价/合作伙伴健康度）",
            "金标准",
        ),
        "n79_战略投资与并购": (
            "战略 · 战略投资与并购（投资总览/五年规划编制/股权投资/固定资产投资/项目评估四维打分/可行性NPV-IRR/产权管理/董事会沟通/战略项目看板）",
            "金标准",
        ),
        "n80_战略研究与预案": (
            "战略 · 战略研究与预案（战略情报/竞对战略分析/战略一致性评分/黑天鹅预案编辑/亏损豁免/双碳ESG/课题研究库/战略季报/流程质量管理）",
            "金标准",
        ),
        "n35_新航线_RPM_评估台": (
            "战略 · 新航线 RPM 评估台（综合分绿黄红/调节班次机型时刻实时重算/纳入宏观竞对参数/提交开线会议）",
            "金标准",
        ),
        "n102_谈判弹窗": (
            "战略 · 谈判弹窗（对方航司画像/核心条款可滑调整/谈判姿态三选一/轮次计数/影响预估/反垄断审查/确认签署或CEO审批/回写n78）",
            "金标准",
        ),
        "n17_机务工程部": (
            "机务 · L1 总览（机务仪表/五大功能入口/维修预算策略选择器/OEM关系看板/停飞应急/二手飞机市场/退租检/航材库存/人员培训/跨部门联动）",
            "金标准",
        ),
        "n81_航线与定检维修": (
            "机务 · 航线与定检维修（航线维修看板/检查清单/定检甘特图/等级说明/淡季养机策略/维修执行跟踪/客舱维护/维修记录/授权资质）",
            "金标准",
        ),
        "n82_工程技术与计划": (
            "机务 · 工程技术与计划（工程技术仪表/维修方案制定/技术支援工作台/计划排程/OEM深度管理/二手飞机评估/客舱构型工程/退租检工程/发动机管理）",
            "金标准",
        ),
        "n83_航材与供应链": (
            "机务 · 航材与供应链（库存总览/实时库存看板/采购订单管理/供应商管理/共享池互援/成本控制/供应链弹性/航材技术管理/仓储物流）",
            "金标准",
        ),
        "n84_安全质量与适航": (
            "机务 · 安全质量与适航（安全质量仪表/适航管理/内部审计/局方检查/维修记录档案/安全事件调查/人员授权/SMS/CAMP）",
            "金标准",
        ),
        "n85_维修培训与资质": (
            "机务 · 维修培训与资质（培训总览/机型改装培训/资质授权/课程体系/自建培训中心/仿真虚拟培训/人力管道招聘/职业发展路径/培训合规）",
            "金标准",
        ),
        "n105_订单确认": (
            "机务 · 航材订单确认（订单编号类型/明细表/供应商信息/交期/多供应商比价/预算库存影响/供应链风险/确认下单生成PO回写n83）",
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

    # NOTE_FEATURE_PACK
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
