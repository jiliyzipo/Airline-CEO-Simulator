# -*- coding: utf-8 -*-
"""Emit UTF-8 page inventory for plan update."""
import json
from pathlib import Path
from collections import OrderedDict

BOARD = Path(
    r"C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Game UI\Interface Planning"
    r"\Airline_CEO__UI交互白板 V1.0.html"
)
OUT = Path(
    r"C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Development Task Book"
    r"\_page_inventory.md"
)

h = BOARD.read_text(encoding="utf-8")
cs = h.index("var CATALOG = [") + 14
d = 0
for i in range(cs, len(h)):
    if h[i] == "[":
        d += 1
    elif h[i] == "]":
        d -= 1
        if d == 0:
            cat = json.loads(h[cs : i + 1])
            break

# Manual authoritative buckets (whiteboard IDs)
BUCKETS = OrderedDict([
    ("SHELL 壳层/开局", [
        "n0_主界面框架_常驻", "n1_主菜单", "n2_玩家资料创建", "n3_公司注册",
        "n4_董事会组建_抽卡", "n5_年度目标确认", "n6_主页_Dashboard", "n7_收件箱",
        "n8_航空事务官_AO", "n39_飞行学院", "n40_设置_存档",
    ]),
    ("FLEET 机队", [
        "n9_机队", "n10_飞机详情_履历", "n11_座位配置", "n97_购机决策会议", "n107_预算确认",
    ]),
    ("NET 航线网络", [
        "n12_航线网络", "n41_单航线",
    ]),
    ("OPS 运营部", [
        "n13_运营部", "n66_航季总纲", "n42_航班周总计划", "n43_航班详细计划",
        "n36_航班环构建器", "n98_航线航班环选择器", "n24_危机_IRROPS_弹窗",
    ]),
    ("FIN 财务部", [
        "n15_财务部", "n22_年度预算编制_审批", "n31_对冲工作台", "n32_碳配额管理",
        "n25_紧急融资_破产保护", "n103_条款确认", "n106_重组条件勾选",
    ]),
    ("MKT 市场销售部", [
        "n14_市场销售部", "n44_航线收益分析", "n49_航班定价", "n45_单航线定价详情",
        "n46_动态定价算法", "n47_收益管理仪表盘", "n48_辅助收入管理", "n50_外部环境分析",
        "n55_季节性日历", "n56_航季对比详情", "n57_旺季策略配置器", "n58_历史趋势分析",
        "n59_旺季影响预测弹窗", "n60_竞争雷达", "n61_竞对详情", "n62_竞争_航线网络",
        "n63_竞争_产品服务", "n64_竞争_运营成本", "n65_竞争_战略资本",
        "n29_竞争态势_AI_航司", "n33_FFP_常旅客管理", "n34_产品工坊",
        "n67_大客户合同", "n68_渠道预算", "n69_分销渠道管理", "n70_直销渠道管理",
        "n71_渠道ROI分析", "n72_品牌危机公关", "n73_舆情监测中心",
        "n74_危机响应指挥部", "n75_品牌修复计划",
    ]),
    ("STG 战略发展部", [
        "n16_战略发展部", "n76_机队与运力规划", "n77_航权与时刻槽", "n78_联盟与战略合作",
        "n79_战略投资与并购", "n80_战略研究与预案", "n35_新航线_RPM_评估台", "n102_谈判弹窗",
    ]),
    ("MNT 机务工程部", [
        "n17_机务工程部", "n81_航线与定检维修", "n82_工程技术与计划", "n83_航材与供应链",
        "n84_安全质量与适航", "n85_维修培训与资质", "n105_订单确认",
    ]),
    ("HR 人力资源部", [
        "n18_人力资源部", "n86_组织规划与招聘", "n87_培训与发展", "n88_绩效与薪酬",
        "n89_员工关系与文化", "n90_合规与风险", "n37_工会谈判桌", "n91_人才档案与继任",
        // n99_招聘策略弹窗 已于 2026-10-08 并入 n112_职业介绍所（同页完成，不再跳转）
    ]),
    ("SAF 安全质量部", [
        "n19_安全质量部", "n92_SMS与风险管理", "n93_安全监察与审核", "n94_事件调查与分析",
        "n95_局方与法规事务", "n96_安全文化与培训", "n100_检查应对弹窗", "n101_策略切换确认弹窗",
    ]),
    ("BOARD 董事会", [
        "n20_董事会", "n26_财年结算_续聘_解雇",
    ]),
    ("CROSS 跨部门/系统", [
        "n21_CEO_决策会议", "n23_部门负责人任命", "n28_宏观经济面板", "n30_随机事件日志",
        "n38_历史回放", "n104_补条件表单",
    ]),
])

by_id = {c["id"]: c for c in cat}
assigned = set()
lines = [
    f"# 白板全量页面清单（共 {len(cat)} 页）",
    "",
    "层级分布：",
]
lvl = {}
for c in cat:
    lvl[c.get("level", "?")] = lvl.get(c.get("level", "?"), 0) + 1
for k in sorted(lvl):
    lines.append(f"- {k}: {lvl[k]}")
lines.append("")

ops_done = {
    "n13_运营部", "n66_航季总纲", "n42_航班周总计划", "n43_航班详细计划",
    "n36_航班环构建器", "n98_航线航班环选择器", "n24_危机_IRROPS_弹窗",
}
fin_done = {
    "n15_财务部", "n22_年度预算编制_审批", "n31_对冲工作台", "n32_碳配额管理",
    "n25_紧急融资_破产保护", "n103_条款确认", "n106_重组条件勾选",
}
fleet_net_done = {
    "n9_机队", "n10_飞机详情_履历", "n11_座位配置", "n97_购机决策会议", "n107_预算确认",
    "n12_航线网络", "n41_单航线",
}
mkt_a_done = {
    "n14_市场销售部", "n44_航线收益分析", "n49_航班定价",
    "n45_单航线定价详情", "n46_动态定价算法", "n47_收益管理仪表盘",
    "n48_辅助收入管理", "n50_外部环境分析", "n55_季节性日历",
    "n56_航季对比详情", "n57_旺季策略配置器", "n58_历史趋势分析",
    "n59_旺季影响预测弹窗", "n33_FFP_常旅客管理", "n34_产品工坊",
}
mkt_b_done = {
    "n60_竞争雷达", "n61_竞对详情", "n62_竞争_航线网络",
    "n63_竞争_产品服务", "n64_竞争_运营成本", "n65_竞争_战略资本",
    "n29_竞争态势_AI_航司", "n67_大客户合同", "n68_渠道预算",
    "n69_分销渠道管理", "n70_直销渠道管理", "n71_渠道ROI分析",
    "n72_品牌危机公关", "n73_舆情监测中心", "n74_危机响应指挥部",
    "n75_品牌修复计划",
}
stg_done = {
    "n16_战略发展部", "n76_机队与运力规划", "n77_航权与时刻槽",
    "n78_联盟与战略合作", "n79_战略投资与并购", "n80_战略研究与预案",
    "n35_新航线_RPM_评估台", "n102_谈判弹窗",
}
mnt_done = {
    "n17_机务工程部", "n81_航线与定检维修", "n82_工程技术与计划",
    "n83_航材与供应链", "n84_安全质量与适航", "n85_维修培训与资质",
    "n105_订单确认",
}
hr_done = {
    "n18_人力资源部", "n86_组织规划与招聘", "n87_培训与发展",
    "n88_绩效与薪酬", "n89_员工关系与文化", "n90_合规与风险",
    "n37_工会谈判桌", "n91_人才档案与继任", "n99_招聘策略弹窗",
}
saf_done = {
    "n19_安全质量部", "n92_SMS与风险管理", "n93_安全监察与审核",
    "n94_事件调查与分析", "n95_局方与法规事务", "n96_安全文化与培训",
    "n100_检查应对弹窗", "n101_策略切换确认弹窗",
}
board_done = {
    "n20_董事会", "n26_财年结算_续聘_解雇",
}
cross_done = {
    "n21_CEO_决策会议", "n23_部门负责人任命", "n28_宏观经济面板",
    "n30_随机事件日志", "n38_历史回放", "n104_补条件表单",
}
shell_done = {
    "n8_航空事务官_AO",  # 顾问开局引导线 S0–S6 全流程
}
# 最小可玩：主路径已通，但深度项延期（Week 14 审计标注）
shell_partial = {
    "n0_主界面框架_常驻": "地图微件/术语 Tooltip/暂停遮罩待加深",
    "n1_主菜单": "最小表单闸门（新游戏/读档/设置/学院/退出）",
    "n2_玩家资料创建": "最小表单",
    "n3_公司注册": "中文名校验已通（汉字≥2）",
    "n4_董事会组建_抽卡": "真抽卡选座待做",
    "n5_年度目标确认": "最小表单",
    "n6_主页_Dashboard": "空壳舞台 + 顶栏顾问；地图微件待做",
    "n7_收件箱": "双栏批准流；完整邮件模型待做",
    "n39_飞行学院": "教程列表；文章详情+XP 待做",
    "n40_设置_存档": "F5/F9 存读档；显示/语言/音效待做",
}
_all_done = (ops_done | fin_done | fleet_net_done | mkt_a_done | mkt_b_done
             | stg_done | mnt_done | hr_done | saf_done | board_done
             | cross_done | shell_done)

for bucket, ids in BUCKETS.items():
    lines.append(f"## {bucket}（{len(ids)}）")
    lines.append("| ID | 层级 | 标题 | 交互状态 |")
    lines.append("|---|---|---|---|")
    for pid in ids:
        n = by_id.get(pid)
        if not n:
            lines.append(f"| `{pid}` | ? | （白板缺失） | — |")
            continue
        assigned.add(pid)
        if pid in _all_done:
            st = "✅ 金标准"
        elif pid in shell_partial:
            st = f"🟡 最小可玩 · 延期：{shell_partial[pid]}"
        else:
            st = "⬜ 待做"
        title = (n.get("title") or "").replace("|", "/")
        lines.append(f"| `{pid}` | {n.get('level')} | {title} | {st} |")
    lines.append("")

missing = [c["id"] for c in cat if c["id"] not in assigned]
if missing:
    lines.append("## 未分桶（需核对）")
    for pid in missing:
        n = by_id[pid]
        lines.append(f"- `{pid}` | {n.get('level')} | {n.get('title')}")
    lines.append("")

OUT.write_text("\n".join(lines), encoding="utf-8")
print("wrote", OUT, "assigned", len(assigned), "missing", len(missing), "total", len(cat))
