# -*- coding: utf-8 -*-
"""Add n67_大客户合同 L2 page + wiring from market dept."""
import json, re
from pathlib import Path

BOARD = Path(r"C:/Users/jiliy/Airline-CEO-Simulator/docs/Development Manual/Game UI/Interface Planning/Airline_CEO__UI交互白板 V1.0.html")

N67 = {
    "id": "n67_大客户合同",
    "level": "L2",
    "title": "🤝 大客户合同 · 企业差旅协议",
    "meta": "市场销售部 · 长期商务合作 · 双赢战略协议",
    "badge": "NEW",
    "items": [
        "合同看板：已签合同数 · 年协议采购总额 · 已返还奖励 · 续约到期预警",
        "客户分级：全球性大客户 / 全国性大客户 / 地区性大客户，按年度差旅经费分级",
        "合作模式：二方合作（航司直签）· 三方合作（引入 TMC 差旅管理公司）",
        "价格优惠 · 前返：购票时直接享受指定舱位固定折扣率（如 Y 舱 8 折）",
        "价格优惠 · 后返：合同期满按总采购金额返还奖励（里程/免费机票/现金）",
        "尊享服务：专属客户经理 · 机场优先值机 · 节假日票源保障 · 免费贵宾休息室",
        "差旅管理：提供差旅数据分析报告，帮助企业优化差旅政策",
        "企业义务：协议价仅限员工因公出行 · 须通过指定渠道出票 · 严禁转售或代买",
        "合同期限：通常一年或更长 · 续约谈判窗口与自动续约条款",
        "战略价值：锁定高价值客源 · 提升收益稳定性 · 竞对大客户争夺情报"
    ],
    "clicks": [
        ["签约/续约合同", "L4 CEO决策会议（重大合同审批）"],
        ["差旅管理分析报告", "L3 FFP 管理（联动忠诚度数据）"],
        ["打包差旅产品", "L3 产品工坊（票+酒店/贵宾厅）"],
        ["查看折扣率与定价联动", "L2 航班定价（大客户折扣视图）"],
        ["竞对大客户情报", "L2 竞争雷达（竞争态势分析）"],
        ["← 返回", "L1 市场销售部"]
    ],
    "entry": "入口：L1 市场销售部 → 大客户合同",
    "kind": "new"
}

NEW_EDGES = [
    ["n14_市场销售部", "n67_大客户合同", "大客户合同"],
    ["n67_大客户合同", "n14_市场销售部", "← 返回"],
    ["n67_大客户合同", "n21_CEO_决策会议", "签约/续约审批"],
    ["n67_大客户合同", "n33_FFP_常旅客管理", "忠诚度联动"],
    ["n67_大客户合同", "n34_产品工坊", "打包差旅产品"],
    ["n67_大客户合同", "n49_航班定价", "折扣定价联动"],
    ["n67_大客户合同", "n60_竞争雷达", "竞对大客户情报"],
    ["n49_航班定价", "n67_大客户合同", "大客户折扣视图"],
]


def main() -> None:
    html = BOARD.read_text(encoding="utf-8")

    # 1) Append N67 to CATALOG after n66
    # Find the closing ] of n66 entry which ends with "kind": "new"}]
    marker = '"kind": "new"}];'
    idx = html.index(marker)
    insert_point = idx + len(marker) - 3  # before ];
    html = html[:insert_point] + ",\n    " + json.dumps(N67, ensure_ascii=False) + "\n  ];\n" + html[insert_point + 3 :]

    # 2) Append new edges
    edges = json.loads(re.search(r'var DEFAULT_EDGES = (\[[\s\S]*?\]);', html).group(1))
    before_edges = len(edges)
    edges.extend(NEW_EDGES)
    m_edges = re.search(r"var DEFAULT_EDGES = \[[\s\S]*?\];", html)
    old_edges_block = m_edges.group()
    new_edges_block = "var DEFAULT_EDGES = " + json.dumps(edges, ensure_ascii=False).replace("],", "],\n ") + ";\n"
    html = html.replace(old_edges_block, new_edges_block, 1)

    # 3) Fix n14 click: 价格战 / 合同 → split into 大客户合同 + 价格战
    old_click = '["价格战 / 合同", "L4 事件弹窗"]'
    if old_click in html:
        html = html.replace(old_click, '["大客户合同", "L2 大客户合同"], ["价格战", "L4 事件弹窗"]', 1)
    else:
        print("WARN: n14 price war / contract click not found")

    # 4) Bump CONTENT_REV
    html = html.replace("var CONTENT_REV = '20260810j';", "var CONTENT_REV = '20260810k';", 1)

    BOARD.write_text(html, encoding="utf-8")

    ns = html.split("<script>")[1].split("</script>")[0]
    print("CONTENT_REV", "20260810k" in html)
    print("n67 present", "n67_大客户合同" in html)
    final_edges = json.loads(re.search(r'var DEFAULT_EDGES = (\[[\s\S]*?\]);', html).group(1))
    print(f"edges: {before_edges} -> {len(final_edges)}")
    n67_edges = [e for e in final_edges if "n67" in e[0] or "n67" in e[1]]
    print("n67 edges:")
    for e in n67_edges:
        print(f"  {e}")
    final_cat = json.loads(re.search(r"var CATALOG = (\[[\s\S]*?\]);", html).group(1))
    print(f"catalog: {len(final_cat)} entries")
    print("braces", ns.count("{"), ns.count("}"))


if __name__ == "__main__":
    main()
