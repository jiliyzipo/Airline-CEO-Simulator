# -*- coding: utf-8 -*-
"""Apply user annotation notes from exported JSON into whiteboard CATALOG."""
from __future__ import annotations

import json
import re
from copy import deepcopy
from pathlib import Path

BOARD = Path(__file__).with_name("Airline_CEO__UI交互白板 V1.0.html")
EXPORT = Path(r"c:\Users\jiliy\Downloads\UI交互白板_含新界面_2026-08-09.json")
DOC = Path(__file__).with_name("Airline_CEO__UI交互图 V1.0.html")


def load_catalog(html: str) -> tuple[list, list, str, str]:
    m1 = re.search(r"var CATALOG = (\[[\s\S]*?\]);\s*\n\s*var DEFAULT_EDGES = (\[[\s\S]*?\]);", html)
    if not m1:
        raise SystemExit("CATALOG/DEFAULT_EDGES not found")
    catalog = json.loads(m1.group(1))
    edges = json.loads(m1.group(2))
    return catalog, edges, m1.group(0), m1.group(0)


def by_id(catalog: list) -> dict:
    return {c["id"]: c for c in catalog}


def set_items(c: dict, items: list[str]):
    c["items"] = items


def set_clicks(c: dict, clicks: list[list[str]]):
    c["clicks"] = clicks


def apply_updates(catalog: list, edges: list) -> tuple[list, list]:
    m = by_id(catalog)

    # ---- n0 shell ----
    set_items(
        m["n0_主界面框架_常驻"],
        [
            "导航：底部 Tab 切换整屏；无左侧主菜单",
            "顶部：公司 LOGO · 公司名称 · CEO 名字 · 日期/星期",
            "速度键循环：▶（1×）→ ▶▶（2×）→ ▶▶▶（4×）→ ‖（暂停）；取消 8 倍速",
            "取消顶部「继续游戏」「消息中心」「搜索」按钮",
            "所有消息/待办一律以邮件进入收件箱（不再用独立消息中心）",
            "底部 Tab：主页 / 收件箱 / 机队 / 航线 | 运营 / 市场 / 财务 / 战略 / 机务 / 人力 / 安全 | 董事会（航线↔运营、安全↔董事会之间加分隔竖线）",
            "世界卫星地图（昼夜）· 航线发光弧 · 飞机图标 · 枢纽光点 · 蓝色数据覆层",
            "右下角 AO 头像（绿/黄/红）",
            "术语：专业词虚线下划线 → Tooltip",
            "暂停：边缘遮罩 +「已暂停」；飞机/进度冻结",
        ],
    )
    set_clicks(
        m["n0_主界面框架_常驻"],
        [
            ["底部 Tab 各图标文字", "对应 L1 一级页"],
            ["速度键 ▶/▶▶/▶▶▶/‖", "切换 1×/2×/4×/暂停"],
            ["右下角 AO 头像", "L2 AO 对话窗"],
            ["设置入口（若有）", "L2 设置/存档"],
        ],
    )

    # ---- n1 menu ----
    set_items(
        m["n1_主菜单"],
        [
            "新游戏：进入建档流程",
            "继续 / 读档：最近存档 + 存档列表",
            "设置：语言、货币显示、音效、速度默认值、辅助选项",
            "设置 · 显示：全屏 或 窗口",
            "飞行学院：图文教程库（可选，读完给幕僚经验）",
            "退出",
        ],
    )
    set_clicks(
        m["n1_主菜单"],
        [
            ["新游戏", "L0 玩家资料创建"],
            ["继续 / 读档", "存档列表 → L1 主页"],
            ["设置（含显示：全屏/窗口）", "L2 设置/存档"],
            ["飞行学院", "L2 飞行学院"],
            ["退出", "关闭游戏"],
        ],
    )

    # ---- n2 profile ----
    set_items(
        m["n2_玩家资料创建"],
        [
            "姓名 / 性别 / 年龄 / 国籍",
            "个人形象：上传图像作为 CEO 形象（可裁剪预览）",
            "影响：初始航网偏好、外交倾向、幕僚生成",
            "系统自动匹配私人幕僚（MVP 合并为 AO「小航」入口）",
            "已删除：教育背景 / 从业经历 / 特质",
        ],
    )

    # ---- n3 company ----
    set_items(
        m["n3_公司注册"],
        [
            "航司中文名 · 航司英文名",
            "ICAO 三字码",
            "创建公司 LOGO：可选系统自带图标，或上传图像",
            "选择主色 · 辅色",
            "选择系统提供的几种涂装样式",
            "总部基地（如 SZX）· 本位币",
            "初始资金 / 股东结构预览（映射董事会权重）",
            "确认后生成第一届董事会候选权重",
        ],
    )

    # ---- n5 goals ----
    set_items(
        m["n5_年度目标确认"],
        [
            "列出本届董事会目标（类型/进度/关联董事）",
            "确认开局 → 进入主页 + AO 强制引导",
            "不显示派系分布与化学反应（本页隐藏）",
        ],
    )
    set_clicks(m["n5_年度目标确认"], [["开始经营", "L1 主页 + AO 引导"]])

    # ---- n6 home ----
    set_items(
        m["n6_主页_Dashboard"],
        [
            "地图舞台：航线动画、飞机位置、枢纽",
            "目标×4：现金跑道（月数+趋势）· 董事会满意度 · 机队规模+可用率 · 本季净利润",
            "覆层面板：下一件待办 · 航线统计▼ · 检修计划▼ · 员工满意度 · 媒体报道",
            "竞争态势微件：区域市占饼图入口",
            "宏观经济指示灯：扩张/收缩/危机（点进宏观面板）",
            "行动中心：待决策事件 → 会议/邮件",
            "底部快讯：最新邮件摘要滚动",
            "付款期限倒计时（未来需支付的飞机预付款等）",
        ],
    )
    set_clicks(
        m["n6_主页_Dashboard"],
        [
            ["下一件待办事项", "收件箱邮件 或 L4 会议"],
            ["现金跑道", "L1 财务部"],
            ["董事会满意度", "L1 董事会"],
            ["航线统计 ▼", "展开 / 可去航线 Tab"],
            ["检修计划 ▼", "展开 / 可去机务 Tab"],
            ["宏观经济指示灯", "L2 宏观经济面板"],
            ["竞争态势微件", "L2 竞争态势"],
            ["地图航线 / 飞机", "单航线详情 或 L2 飞机详情"],
        ],
    )
    m["n6_主页_Dashboard"]["entry"] = "关联 → 收件箱 / 财务 / 董事会 / AO"

    # ---- n7 inbox ----
    set_items(
        m["n7_收件箱"],
        [
            "按日期分组；未读：主题文字亮度 100% + 底色略亮；已读：文字亮度 60% + 底色不亮（取消红点角标）",
            "详情：正文、抄送部门、数据表、柱状图、前置进度甘特",
            "动态按钮：批准 · 发起会议 · 直接确认 · 暂缓/撤销/拒绝（「批准」不限定开航，适用于各类提案）",
            "所有发给 CEO 的信息与待办事项均以邮件进入收件箱（类型不限）",
            "一般信息邮件可略过",
            "待办事件（含暂缓）：自收到日起第 7 日为处理截止；到期强制跳转该邮件，必须批准或撤销/拒绝，否则游戏不能继续",
            "开局样例：SZX↔PEK 航线申请等",
        ],
    )
    set_clicks(
        m["n7_收件箱"],
        [
            ["左侧邮件行", "右侧详情（同页）"],
            ["批准", "生效并更新相关系统（不限开航）"],
            ["发起会议", "L4 CEO 决策会议"],
            ["暂缓 / 撤销 / 拒绝", "标记状态；待办仍受 7 日截止约束"],
        ],
    )

    # ---- n9 fleet ----
    set_items(
        m["n9_机队"],
        [
            "汇总：总数 · 平均机龄 · 可用率%",
            "基本信息：飞机名称（如「迪士尼号」）· 注册号 · 机型",
            "来源：自有 / 融资租赁 / 经营租赁",
            "分类筛选：远程 / 中程 / 短程",
            "分类筛选：宽体机 / 窄体机",
            "按机型折叠列表：数量▶；明细含座位、首飞日、机龄、维修/适航状态",
            "运营与性能数据：利用率、执飞状态等（列表/详情展示）",
            "+ 购买飞机 → 购机决策会议",
            "点击飞机 → 飞机详情页",
        ],
    )
    set_clicks(
        m["n9_机队"],
        [
            ["机型 ▶ / 注册号 / 飞机名称", "L2 飞机详情"],
            ["远程·中程·短程 / 宽窄体筛选", "同页过滤"],
            ["+ 购买飞机", "L4 购机决策会议"],
        ],
    )

    # ---- n10 aircraft detail ----
    set_items(
        m["n10_飞机详情_履历"],
        [
            "顶栏：注册号 · 飞机名称 · 机型 · 当前状态徽章（在役/停场/封存/已退役）· 位置 · 机龄/退役进度",
            "利用率：日利用率；飞行循环（起降次数）；可用飞机利用率 / 总在册机队利用率 / 分机型利用率；盈亏平衡利用率",
            "资产价值：当前市场估值",
            "租赁与融资信息：出租方 · 租期 · 租金（租赁机）",
            "运营历史：过往运营商记录",
            "Tab 基本/财务：MSN、交付、采购方式、FH/FC、净值、累计营收",
            "Tab 维护：发动机、近次检修、6 月定检甘特、AOG 记录",
            "Tab 运营：执飞航线、近 30 天班次、客座率、利润、推荐航线 Top3",
            "Tab 日志：首航/大修/事故/转场时间轴",
            "操作：分配航线 · 转售/退役 · 安排大修 · 申请替换 · 载客量配置",
        ],
    )
    set_clicks(
        m["n10_飞机详情_履历"],
        [
            ["载客量配置", "L3 载客量配置"],
            ["分配航线", "L3 航线/航班环选择器"],
            ["转售 / 退役", "L5 确认 → 回机队"],
            ["安排大修", "L4/L5 预算确认"],
            ["申请替换", "邮件 + L4 购机会议"],
            ["← 返回", "L1 机队"],
        ],
    )

    # ---- n11 rename seat config ----
    m["n11_座位配置"]["title"] = "🪑 载客量配置"
    m["n11_座位配置"]["meta"] = "飞机详情子页 · 原座位配置"
    set_items(
        m["n11_座位配置"],
        [
            "舱位平面图：头等舱 / 商务舱 / 经济舱等座位数分布",
            "各舱座位数 · 间距 · 票价系数滑块",
            "总座位数实时重算；影响收益与重量",
            "保存配置 / 恢复默认",
        ],
    )
    set_clicks(
        m["n11_座位配置"],
        [
            ["保存配置", "写回飞机详情"],
            ["恢复默认", "同页重置"],
            ["← 返回", "L2 飞机详情"],
        ],
    )

    # ---- n12 routes ----
    set_items(
        m["n12_航线网络"],
        [
            "统计：总航线数 · 覆盖城市/国家 · 已有航权",
            "列表：OD · 执飞机型 · 周班次 · 航班频率（日/周）· 客座率 · 日利润",
            "列表：运力投放（可用座公里 ASK 等）",
            "列表/面板：中转效率（MCT、航班波 Banks）",
            "网络集中度：枢纽集中 vs 多点分散",
            "平均航距：影响成本结构",
            "代码共享：他航实际承运、本航销售扩展",
            "航空联盟：星空联盟 / 天合联盟 / 寰宇一家等联结",
            "点击航线 → 单航线详情；+ 开通新航线 → RPM/会议",
            "地图高亮本公司航线；可进全屏航线图模式",
        ],
    )
    set_clicks(
        m["n12_航线网络"],
        [
            ["航线列表行 / 地图航线", "L2 单航线"],
            ["已有航权", "航权列表/详情（同页或侧栏）"],
            ["代码共享 / 联盟", "相关详情或战略入口"],
            ["+ 开通新航线", "L3 RPM评估 或 L4 开线会议"],
        ],
    )

    # ---- n13 ops ----
    set_items(
        m["n13_运营部"],
        [
            "目标：准点率 · 航班完成率 · 单班成本 · 日利用率 · 过站效率",
            "运力饱和红条 +「向 CEO 提交购机建议」",
            "航季：夏秋航季 / 冬春航季（决定季内航网与时刻大方向）",
            "航班周总计划：在航季框架下按周安排执飞",
            "点击周总计划中任意航班 → 航班详细计划",
            "计划草案：季节性加班 / 时刻槽倾向",
            "航班环构建：AI 方案选型或手动连班",
            "IRROPS 入口：台风/AOG/罢工危机",
            "负责人绩效 + 任命（取消任命副手）",
        ],
    )
    set_clicks(
        m["n13_运营部"],
        [
            ["向CEO提交购机建议", "收件箱 + 可开 L4 会议"],
            ["航班周总计划", "L2 航班周总计划"],
            ["周计划中的航班行", "L2 航班详细计划"],
            ["航季切换", "同页切换夏秋/冬春框架"],
            ["航班环构建 / 计划草案", "L3 航班环构建器"],
            ["IRROPS / 危机", "L4 危机弹窗"],
            ["任命 / 绩效", "L4 负责人任命或报告"],
        ],
    )

    # ---- marketing override already in extras: apply into catalog ----
    set_items(
        m["n14_市场销售部"],
        [
            "目标：客座率 · 单位收益 · 品牌净值 · 市场份额",
            "航线收益 Top5 / Bottom5",
            "定价策略：票价/折扣/预售",
            "竞争雷达 · 价格战事件选项",
            "季节性日历：节日需求曲线 + 旺季策略包",
            "常旅客计划 · 产品工坊 · 大客户合同 · 渠道预算",
            "品牌危机公关决策入口",
        ],
    )
    set_clicks(
        m["n14_市场销售部"],
        [
            ["常旅客计划", "L3 FFP 管理"],
            ["产品工坊", "L3 产品工坊"],
            ["季节性日历", "L3 旺季策略"],
            ["价格战 / 合同", "L4 事件弹窗"],
        ],
    )

    # ---- new screens ----
    new_single_route = {
        "id": "n41_单航线",
        "level": "L2",
        "title": "📍 单航线",
        "meta": "从航线网络点入",
        "badge": "新建",
        "items": [
            "航班计划要素：每周班次（一周几班）与班期（具体周几飞行）",
            "执飞机型与运力：机型（如 777-300）及座位数、物理舱位配置",
            "航线与时刻细节：航段（含经停）、适用期限、最短中转时间（MCT）",
            "运营表现：客座率 · 收益率 · 航班延误率",
            "市场数据：票价水平及变动趋势 · 航班频率（日/周）",
            "物理与地理：航程距离 · 预计飞行时间 · 迂回系数",
            "资产追踪：执飞飞机注册号",
        ],
        "clicks": [
            ["← 返回", "L1 航线网络"],
            ["执飞注册号", "L2 飞机详情"],
            ["发起调整/停飞等", "收件箱提案 或 L4 会议"],
        ],
        "entry": "入口：L1 航线网络",
        "kind": "new",
    }
    new_week_plan = {
        "id": "n42_航班周总计划",
        "level": "L2",
        "title": "📅 航班周总计划",
        "meta": "运营部 · 航季框架下的周计划表",
        "badge": "新建",
        "items": [
            "在当前航季（夏秋/冬春）框架下展示本周全部航班",
            "表头/列：航线 · 机型 · 航班号 · 班次 · 班期 · 时刻 · 机组摘要",
            "可按航线/机型/日期筛选",
            "点击任意航班行 → 航班详细计划",
            "支持从计划草案/航班环回写后的结果查看",
        ],
        "clicks": [
            ["任意航班行", "L2 航班详细计划"],
            ["航季标签", "切换夏秋/冬春视图"],
            ["← 返回", "L1 运营部"],
        ],
        "entry": "入口：L1 运营部",
        "kind": "new",
    }
    new_flight_detail = {
        "id": "n43_航班详细计划",
        "level": "L2",
        "title": "🧾 航班详细计划",
        "meta": "从航班周总计划点入",
        "badge": "新建",
        "items": [
            "航线：起点、终点及经停点",
            "机型：执行该航班任务的具体飞机型号",
            "航班号：唯一识别代码",
            "班次：该航班一周内飞行总次数",
            "班期：一周中哪几天执飞（如每日 / 周一三五）",
            "时刻：计划起飞与到达时刻（起飞时刻影响日利用率）",
            "机组排班：飞行员/乘务员名单；资质与休息法规校验",
        ],
        "clicks": [
            ["← 返回", "L2 航班周总计划"],
            ["机型 / 注册号", "L2 飞机详情（若已指定）"],
            ["调整时刻/机组", "同页编辑 或 提交邮件/会议"],
        ],
        "entry": "入口：L2 航班周总计划",
        "kind": "new",
    }

    # remove old custom ids if somehow present; append new
    catalog = [c for c in catalog if c["id"] not in ("custom_q282zgz", "custom_uqawjw8", "n41_单航线", "n42_航班周总计划", "n43_航班详细计划")]
    catalog.extend([new_single_route, new_week_plan, new_flight_detail])

    # edges: add new connections; keep others; fix seat config label edges if any
    def has_edge(fr, to):
        return any(e[0] == fr and e[1] == to for e in edges)

    add_edges = [
        ["n12_航线网络", "n41_单航线", "点航线"],
        ["n13_运营部", "n42_航班周总计划", "航班周总计划"],
        ["n42_航班周总计划", "n43_航班详细计划", "点航班行"],
        ["n41_单航线", "n10_飞机详情_履历", "执飞注册号"],
        ["n6_主页_Dashboard", "n41_单航线", "地图航线"],
    ]
    for e in add_edges:
        if not has_edge(e[0], e[1]):
            edges.append(e)

    # remove obsolete message center shell click emphasis? keep edge but catalog already removed 消息中心 from shell
    # Optionally remove edges from shell to message center
    edges = [e for e in edges if not (e[0] == "n0_主界面框架_常驻" and e[1] == "n27_消息中心")]
    # remove home->msg if we want - user said all mail to inbox; keep msg center page for now as unmentioned
    # fix aircraft detail -> seat config edge label
    for e in edges:
        if e[0] == "n10_飞机详情_履历" and e[1] == "n11_座位配置":
            e[2] = "载客量配置"

    return catalog, edges


def patch_board(catalog: list, edges: list) -> None:
    html = BOARD.read_text(encoding="utf-8")
    cat_js = json.dumps(catalog, ensure_ascii=False)
    edges_js = json.dumps(edges, ensure_ascii=False)
    html2, n = re.subn(
        r"var CATALOG = \[[\s\S]*?\];\s*\n\s*var DEFAULT_EDGES = \[[\s\S]*?\];",
        "var CATALOG = " + cat_js + ";\n  var DEFAULT_EDGES = " + edges_js + ";",
        html,
        count=1,
    )
    if n != 1:
        raise SystemExit(f"board patch failed n={n}")
    # bump storage key so browser reloads new catalog content while user can import JSON for positions/notes
    html2 = html2.replace(
        "var STORAGE_KEY = 'airline_ceo_ui_board_v2_full';",
        "var STORAGE_KEY = 'airline_ceo_ui_board_v3_noted_20260809';",
    )
    BOARD.write_text(html2, encoding="utf-8")
    print("patched board", BOARD.stat().st_size)


def patch_doc_cards(catalog: list) -> None:
    """Light sync: not rewriting whole doc; skip if too risky. User asked whiteboard primarily."""
    return


def main() -> None:
    html = BOARD.read_text(encoding="utf-8")
    m = re.search(r"var CATALOG = (\[[\s\S]*?\]);\s*\n\s*var DEFAULT_EDGES = (\[[\s\S]*?\]);", html)
    if not m:
        raise SystemExit("parse fail")
    catalog = json.loads(m.group(1))
    edges = json.loads(m.group(2))
    catalog, edges = apply_updates(catalog, edges)
    patch_board(catalog, edges)
    # write summary for user
    export = json.loads(EXPORT.read_text(encoding="utf-8"))
    applied = [k for k, v in export.get("nodes", {}).items() if (v.get("note") or "").strip()]
    Path(__file__).with_name("_applied_notes_summary.txt").write_text(
        "已应用批注页面:\n" + "\n".join(applied) + "\n\n新增界面:\n- n41_单航线\n- n42_航班周总计划\n- n43_航班详细计划\n",
        encoding="utf-8",
    )
    print("applied", len(applied), "new screens 3")


if __name__ == "__main__":
    main()
