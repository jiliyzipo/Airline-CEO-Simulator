# -*- coding: utf-8 -*-
"""Rewrite annotated pages as clean feature bullets in CATALOG + add Apply-Notes UI."""
from __future__ import annotations

import json
import re
from pathlib import Path

BOARD = Path(__file__).with_name("Airline_CEO__UI交互白板 V1.0.html")

# 批注整理后的「功能框」文案（产品功能描述，而非修改说明）
FEATURES = {
    "n0_主界面框架_常驻": {
        "items": [
            "顶部栏：公司 LOGO · 公司名称 · CEO 名字 · 日期/星期",
            "速度控制：▶（1×）→ ▶▶（2×）→ ▶▶▶（4×）→ ‖（暂停）；无 8 倍速",
            "底部主菜单 Tab：主页 / 收件箱 / 机队 / 航线 | 运营 / 市场 / 财务 / 战略 / 机务 / 人力 / 安全 | 董事会",
            "分隔竖线：航线与运营之间；安全与董事会之间",
            "消息与待办：全部以邮件进入收件箱（无独立消息中心 / 搜索 / 继续游戏按钮）",
            "地图舞台：世界卫星地图（昼夜）· 航线发光弧 · 飞机图标 · 枢纽光点 · 蓝色数据覆层",
            "右下角 AO 头像（绿/黄/红状态）",
            "术语：专业词虚线下划线 → Tooltip",
            "暂停态：边缘遮罩 +「已暂停」；飞机与进度冻结",
        ],
        "clicks": [
            ["底部 Tab", "对应 L1 一级页"],
            ["速度键 ▶/▶▶/▶▶▶/‖", "切换 1×/2×/4×/暂停"],
            ["右下角 AO 头像", "L2 AO 对话窗"],
            ["设置入口（若有）", "L2 设置/存档"],
        ],
    },
    "n1_主菜单": {
        "items": [
            "新游戏：进入建档流程",
            "继续 / 读档：最近存档 + 存档列表",
            "设置：语言、货币显示、音效、速度默认值、辅助选项",
            "显示设置：全屏 或 窗口",
            "飞行学院：图文教程库（可选，读完给幕僚经验）",
            "退出游戏",
        ],
        "clicks": [
            ["新游戏", "L0 玩家资料创建"],
            ["继续 / 读档", "存档列表 → L1 主页"],
            ["设置（含显示模式）", "L2 设置/存档"],
            ["飞行学院", "L2 飞行学院"],
            ["退出", "关闭游戏"],
        ],
    },
    "n2_玩家资料创建": {
        "items": [
            "基础资料：姓名 / 性别 / 年龄 / 国籍",
            "个人形象：上传图像作为 CEO 形象（支持裁剪预览）",
            "开局影响：初始航网偏好、外交倾向、幕僚生成",
            "私人幕僚：系统自动匹配（MVP 合并为 AO「小航」入口）",
        ],
        "clicks": [["下一步 / 确认", "L0 公司注册"]],
    },
    "n3_公司注册": {
        "items": [
            "航司命名：中文名 · 英文名",
            "ICAO 三字码",
            "公司 LOGO：系统自带图标库 或 玩家上传图像",
            "品牌色：主色 · 辅色",
            "涂装样式：系统提供的若干机身涂装方案",
            "总部基地（如 SZX）· 本位币",
            "初始资金 / 股东结构预览（映射董事会权重）",
            "确认后生成第一届董事会候选权重",
        ],
        "clicks": [["确认注册", "L0 董事会组建"]],
    },
    "n5_年度目标确认": {
        "items": [
            "本届董事会目标清单（类型 / 进度 / 关联董事）",
            "确认开局 → 进入主页 + AO 强制引导",
            "本页不展示：派系分布、化学反应",
        ],
        "clicks": [["开始经营", "L1 主页 + AO 引导"]],
    },
    "n6_主页_Dashboard": {
        "items": [
            "地图舞台：航线动画、飞机位置、枢纽",
            "目标×4：现金跑道（月数+趋势）· 董事会满意度 · 机队规模+可用率 · 本季净利润",
            "覆层面板：下一件待办 · 航线统计▼ · 检修计划▼ · 员工满意度 · 媒体报道",
            "竞争态势微件：区域市占饼图入口",
            "宏观经济指示灯：扩张 / 收缩 / 危机（点进宏观面板）",
            "行动中心：待决策事件 → 会议/邮件",
            "底部快讯：最新邮件摘要滚动",
            "付款期限倒计时（飞机预付款等未来应付）",
        ],
        "clicks": [
            ["下一件待办事项", "收件箱邮件 或 L4 会议"],
            ["现金跑道", "L1 财务部"],
            ["董事会满意度", "L1 董事会"],
            ["航线统计 ▼", "展开 / 可去航线 Tab"],
            ["检修计划 ▼", "展开 / 可去机务 Tab"],
            ["宏观经济指示灯", "L2 宏观经济面板"],
            ["竞争态势微件", "L2 竞争态势"],
            ["地图航线 / 飞机", "单航线详情 或 L2 飞机详情"],
        ],
        "entry": "关联 → 收件箱 / 财务 / 董事会 / AO",
    },
    "n7_收件箱": {
        "items": [
            "邮件列表：按日期分组；未读主题亮度 100% + 底色略亮；已读主题亮度 60% + 底色不亮",
            "邮件详情：正文、抄送部门、数据表、柱状图、前置进度甘特",
            "操作按钮：批准 · 发起会议 · 直接确认 · 暂缓 / 撤销 / 拒绝（「批准」适用于各类提案，不限定开航）",
            "信息范围：所有发给 CEO 的信息与待办事项均以邮件送达（类型可扩展）",
            "一般信息邮件：可略过",
            "待办事件（含暂缓）：自收件日起第 7 日截止；到期强制跳转该邮件，须批准或撤销/拒绝后方可继续游戏",
            "开局样例：航线申请等提案邮件",
        ],
        "clicks": [
            ["左侧邮件行", "右侧详情（同页）"],
            ["批准", "生效并更新相关系统"],
            ["发起会议", "L4 CEO 决策会议"],
            ["暂缓 / 撤销 / 拒绝", "标记状态；待办仍受 7 日截止约束"],
        ],
    },
    "n9_机队": {
        "items": [
            "汇总：机队总数 · 平均机龄 · 可用率%",
            "基本信息与标识：飞机名称（如「迪士尼号」）· 注册号 · 机型",
            "来源：自有 / 融资租赁 / 经营租赁",
            "机队分类：远程 / 中程 / 短程",
            "机队分类：宽体机 / 窄体机",
            "按机型折叠列表：数量▶；明细含座位、首飞日、机龄、维修/适航状态",
            "运营与性能数据：利用率、执飞状态等（列表/详情）",
            "+ 购买飞机 → 购机决策会议",
            "点击飞机 → 飞机详情页",
        ],
        "clicks": [
            ["机型 ▶ / 注册号 / 飞机名称", "L2 飞机详情"],
            ["远程·中程·短程 / 宽窄体筛选", "同页过滤"],
            ["+ 购买飞机", "L4 购机决策会议"],
        ],
    },
    "n10_飞机详情_履历": {
        "items": [
            "顶栏：注册号 · 飞机名称 · 机型 · 当前状态（在役/停场/封存/已退役）· 位置 · 机龄/退役进度",
            "载客量配置入口：舱位平面图（头等/商务/经济等座位数）",
            "利用率：日利用率 · 飞行循环（起降次数）· 可用飞机/总在册/分机型利用率 · 盈亏平衡利用率",
            "资产价值：当前市场估值",
            "租赁与融资：出租方 · 租期 · 租金（租赁机）",
            "运营历史：过往运营商记录",
            "Tab 基本/财务：MSN、交付、采购方式、FH/FC、净值、累计营收",
            "Tab 维护：发动机、近次检修、6 月定检甘特、AOG 记录",
            "Tab 运营：执飞航线、近 30 天班次、客座率、利润、推荐航线 Top3",
            "Tab 日志：首航/大修/事故/转场时间轴",
            "操作：分配航线 · 转售/退役 · 安排大修 · 申请替换 · 载客量配置",
        ],
        "clicks": [
            ["载客量配置", "L3 载客量配置"],
            ["分配航线", "L3 航线/航班环选择器"],
            ["转售 / 退役", "L5 确认 → 回机队"],
            ["安排大修", "L4/L5 预算确认"],
            ["申请替换", "邮件 + L4 购机会议"],
            ["← 返回", "L1 机队"],
        ],
    },
    "n11_座位配置": {
        "title": "🪑 载客量配置",
        "meta": "飞机详情子页 · 原座位配置",
        "items": [
            "舱位平面图：头等舱 / 商务舱 / 经济舱等座位数分布",
            "各舱座位数 · 间距 · 票价系数滑块",
            "总座位数实时重算；影响收益与重量",
            "保存配置 / 恢复默认",
        ],
        "clicks": [
            ["保存配置", "写回飞机详情"],
            ["恢复默认", "同页重置"],
            ["← 返回", "L2 飞机详情"],
        ],
    },
    "n12_航线网络": {
        "items": [
            "网络统计：总航线数 · 覆盖城市/国家 · 已有航权",
            "航线列表：OD · 执飞机型 · 周班次 · 航班频率（日/周）· 客座率 · 日利润",
            "运力投放：可用座公里（ASK）等",
            "中转效率：最短中转时间（MCT）· 航班波（Banks）",
            "网络集中度：枢纽集中 vs 多点分散",
            "平均航距：反映航线长短与成本结构",
            "代码共享：他航承运、本航销售扩展",
            "航空联盟：星空联盟 / 天合联盟 / 寰宇一家等网络联结",
            "点击航线 → 单航线详情；+ 开通新航线 → RPM/会议",
            "地图高亮本公司航线；可进全屏航线图模式",
        ],
        "clicks": [
            ["航线列表行 / 地图航线", "L2 单航线"],
            ["已有航权", "航权列表/详情（同页或侧栏）"],
            ["代码共享 / 联盟", "相关详情或战略入口"],
            ["+ 开通新航线", "L3 RPM评估 或 L4 开线会议"],
        ],
    },
    "n13_运营部": {
        "items": [
            "目标：准点率 · 航班完成率 · 单班成本 · 日利用率 · 过站效率",
            "运力饱和红条 +「向 CEO 提交购机建议」",
            "航季：夏秋航季 / 冬春航季（决定季内航网与时刻大方向）",
            "航班周总计划：在航季框架下按周安排执飞",
            "点击周总计划中任意航班 → 航班详细计划",
            "计划草案：季节性加班 / 时刻槽倾向",
            "航班环构建：AI 方案选型或手动连班",
            "IRROPS 入口：台风 / AOG / 罢工危机",
            "负责人绩效与任命（无副手任命）",
        ],
        "clicks": [
            ["向CEO提交购机建议", "收件箱 + 可开 L4 会议"],
            ["航班周总计划", "L2 航班周总计划"],
            ["周计划中的航班行", "L2 航班详细计划"],
            ["航季切换", "同页切换夏秋/冬春框架"],
            ["航班环构建 / 计划草案", "L3 航班环构建器"],
            ["IRROPS / 危机", "L4 危机弹窗"],
            ["任命 / 绩效", "L4 负责人任命或报告"],
        ],
    },
    "n14_市场销售部": {
        "items": [
            "目标：客座率 · 单位收益 · 品牌净值 · 市场份额",
            "航线收益 Top5 / Bottom5",
            "定价策略：票价 / 折扣 / 预售",
            "竞争雷达 · 价格战事件选项",
            "季节性日历：节日需求曲线 + 旺季策略包",
            "常旅客计划 · 产品工坊 · 大客户合同 · 渠道预算",
            "品牌危机公关决策入口",
        ],
        "clicks": [
            ["常旅客计划", "L3 FFP 管理"],
            ["产品工坊", "L3 产品工坊"],
            ["季节性日历", "L3 旺季策略"],
            ["价格战 / 合同", "L4 事件弹窗"],
        ],
    },
    "n40_设置_存档": {
        "items": [
            "F5/F9 快存快读；自动存档槽",
            "显示模式：全屏 或 窗口",
            "语言 · 货币 · 色盲模式 · 字体缩放",
            "新游戏+ / 清档（保留 API Key）",
            "离线简报：再次上线时的世界摘要（若启用）",
        ],
    },
    "n41_单航线": {
        "items": [
            "航班计划：每周班次 · 班期（具体周几飞行）",
            "执飞机型与运力：机型、座位数、物理舱位配置",
            "航线与时刻：航段（含经停）· 适用期限 · 最短中转时间（MCT）",
            "运营表现：客座率 · 收益率 · 航班延误率",
            "市场数据：票价水平及变动 · 航班频率（日/周）",
            "物理与地理：航程 · 预计飞行时间 · 迂回系数",
            "资产追踪：执飞飞机注册号",
        ],
        "clicks": [
            ["← 返回", "L1 航线网络"],
            ["执飞注册号", "L2 飞机详情"],
            ["发起调整/停飞等", "收件箱提案 或 L4 会议"],
        ],
        "entry": "入口：L1 航线网络",
    },
    "n42_航班周总计划": {
        "items": [
            "在当前航季（夏秋/冬春）框架下展示本周全部航班",
            "列表字段：航线 · 机型 · 航班号 · 班次 · 班期 · 时刻 · 机组摘要",
            "筛选：按航线 / 机型 / 日期",
            "点击任意航班行 → 航班详细计划",
            "展示计划草案 / 航班环回写后的结果",
        ],
        "clicks": [
            ["任意航班行", "L2 航班详细计划"],
            ["航季标签", "切换夏秋/冬春视图"],
            ["← 返回", "L1 运营部"],
        ],
        "entry": "入口：L1 运营部",
    },
    "n43_航班详细计划": {
        "items": [
            "航线：起点、终点及经停点",
            "机型：执行该航班的具体飞机型号",
            "航班号：唯一识别代码",
            "班次：一周内飞行总次数",
            "班期：一周中哪几天执飞（如每日 / 周一三五）",
            "时刻：计划起飞与到达（起飞时刻影响日利用率）",
            "机组排班：飞行员/乘务员名单；资质与休息法规校验",
        ],
        "clicks": [
            ["← 返回", "L2 航班周总计划"],
            ["机型 / 注册号", "L2 飞机详情（若已指定）"],
            ["调整时刻/机组", "同页编辑 或 提交邮件/会议"],
        ],
        "entry": "入口：L2 航班周总计划",
    },
}


def patch_catalog(html: str) -> str:
    m = re.search(r"var CATALOG = (\[[\s\S]*?\]);", html)
    if not m:
        raise SystemExit("CATALOG not found")
    catalog = json.loads(m.group(1))
    by = {c["id"]: c for c in catalog}
    for nid, patch in FEATURES.items():
        if nid not in by:
            continue
        c = by[nid]
        if "title" in patch:
            c["title"] = patch["title"]
        if "meta" in patch:
            c["meta"] = patch["meta"]
        if "items" in patch:
            c["items"] = patch["items"]
        if "clicks" in patch:
            c["clicks"] = patch["clicks"]
        if "entry" in patch:
            c["entry"] = patch["entry"]
    new_js = json.dumps(catalog, ensure_ascii=False)
    return html[: m.start(1)] + new_js + html[m.end(1) :]


def patch_ui(html: str) -> str:
    # bump content rev + migration to drop stale overrides so功能框显示新内容
    if "CONTENT_REV" not in html:
        html = html.replace(
            "  var STORAGE_KEY = 'airline_ceo_ui_board_v3_noted_20260809';",
            "  var STORAGE_KEY = 'airline_ceo_ui_board_v3_noted_20260809';\n"
            "  var CONTENT_REV = '20260809d';",
            1,
        )
    else:
        html = re.sub(r"var CONTENT_REV = '[^']*';", "var CONTENT_REV = '20260809d';", html, count=1)

    # strengthen migrate: drop extras overrides for catalog ids when content rev changes
    old_snip = """    state.extras = (state.extras || []).filter(function (x) {
      return x && x.id !== 'custom_q282zgz' && x.id !== 'custom_uqawjw8' && x.id !== 'n14_市场销售部';
    });
    // 仅首次迁移时清空「已应用批注」页的旧黄框，避免后续新批注被冲掉
    var clearedKey = 'airline_ceo_ui_board_notes_cleared_20260809';
    if (!localStorage.getItem(clearedKey)) {
      Object.keys(APPLIED_NOTE_IDS).forEach(function (id) {
        if (state.nodes[id]) state.nodes[id].note = '';
      });
      localStorage.setItem(clearedKey, '1');
    }"""

    new_snip = """    state.extras = (state.extras || []).filter(function (x) {
      return x && x.id !== 'custom_q282zgz' && x.id !== 'custom_uqawjw8' && x.id !== 'n14_市场销售部';
    });
    // 内容修订后：去掉内置页的 extras 覆盖，让功能框显示最新 CATALOG
    // 黄框批注不在此处清空——启动时由 applyNotesToFeatures 整理进功能框
    var revKey = 'airline_ceo_ui_board_content_rev';
    if (localStorage.getItem(revKey) !== CONTENT_REV) {
      var catalogIds = {};
      CATALOG.forEach(function (c) { catalogIds[c.id] = true; });
      state.extras = (state.extras || []).filter(function (x) {
        return x && x.custom && !catalogIds[x.id];
      });
      state._needApplyNotes = true;
      localStorage.setItem(revKey, CONTENT_REV);
      localStorage.setItem('airline_ceo_ui_board_notes_cleared_20260809', '1');
    }"""

    if old_snip not in html:
        raise SystemExit("migrate snip not found")
    html = html.replace(old_snip, new_snip, 1)

    # toolbar button
    if 'id="btnApplyNotes"' not in html:
        html = html.replace(
            '<button type="button" id="btnExport">导出 JSON</button>',
            '<button type="button" class="primary" id="btnApplyNotes">批注→功能框</button>\n'
            '    <button type="button" id="btnExport">导出 JSON</button>',
            1,
        )

    if "function applyNotesToFeatures" not in html:
        btn_js = r"""
  var NOTE_FEATURE_PACK = %s;
  function noteLinesToFeatures(note) {
    var lines = String(note || '').split(/\r?\n/);
    var out = [];
    lines.forEach(function (raw) {
      var s = String(raw || '').trim();
      if (!s) return;
      s = s.replace(/^[\d０-９]+[\.\)、．]\s*/, '');
      s = s.replace(/^(基本信息与标识|机队构成与规模|运营与性能数据)[:：]?\s*/, '');
      if (!s || /[:：]\s*$/.test(s)) return;
      // 改名类：保留结果侧
      var m = s.match(/[「"']?(.+?)[」"']?\s*(改成|换成|改为)\s*[「"']?(.+?)[」"']?\s*$/);
      if (m) { out.push(m[3].replace(/[。；;]+$/, '')); return; }
      s = s.replace(/^(添加|增加|取消|删除)/, '').trim();
      s = s.replace(/^[「"']|[」"']$/g, '');
      if (s) out.push(s.replace(/[。；;]+$/, ''));
    });
    return out;
  }
  function upsertOverride(item, pack, nextItems, nextClicks) {
    var payload = {
      id: item.id,
      level: item.level,
      title: (pack && pack.title) || item.title,
      meta: (pack && pack.meta) || item.meta || '',
      badge: item.badge || '',
      items: nextItems,
      clicks: nextClicks || item.clicks || [],
      entry: (pack && pack.entry) || item.entry || '',
      kind: item.kind || 'custom',
      custom: !!item.custom,
      override: !item.custom
    };
    state.extras = (state.extras || []).filter(function (x) { return x.id !== item.id; });
    state.extras.push(payload);
  }
  function applyNotesToFeatures(forcePack) {
    var count = 0;
    getCatalog().forEach(function (item) {
      var n = state.nodes[item.id];
      var note = (n && n.note) ? String(n.note).trim() : '';
      var pack = NOTE_FEATURE_PACK[item.id];
      if (!note && !(forcePack && pack)) return;
      if (pack) {
        upsertOverride(item, pack, pack.items.slice(), pack.clicks ? pack.clicks.map(function (p) { return p.slice(); }) : null);
      } else if (note) {
        var extra = noteLinesToFeatures(note);
        if (!extra.length) return;
        var nextItems = (item.items || []).slice();
        extra.forEach(function (line) {
          if (nextItems.indexOf(line) < 0) nextItems.push(line);
        });
        upsertOverride(item, null, nextItems, null);
      } else {
        return;
      }
      if (n) n.note = '';
      count++;
    });
    renderNodes(); save();
    setStatus('已将批注整理写入功能框 · ' + count + ' 个界面');
    return count;
  }
  document.getElementById('btnApplyNotes').onclick = function () {
    var hasNote = getCatalog().some(function (item) {
      var n = state.nodes[item.id];
      return n && String(n.note || '').trim();
    });
    var msg = hasNote
      ? '将把各卡片黄框批注整理写入上方「功能」列表，并清空已处理批注。继续？'
      : '当前没有黄框批注。是否刷新显示已整理进文件的功能框（并清除这些页的旧覆盖）？';
    if (!confirm(msg)) return;
    applyNotesToFeatures(!hasNote);
  };
""" % json.dumps(FEATURES, ensure_ascii=False)
        # insert before btnExport handler
        anchor = "  document.getElementById('btnExport').onclick = function () {"
        if anchor not in html:
            raise SystemExit("btnExport anchor missing")
        html = html.replace(anchor, btn_js + "\n" + anchor, 1)

    html = html.replace(
        '    <span class="hint">2026-08-09 已应用部分批注到卡片内容。未批注页可继续标注。布局可用「导入 JSON」恢复。</span>',
        '    <span class="hint">批注写在黄框 → 点「批注→功能框」整理进功能列表。未批注页可继续标注。</span>',
        1,
    )
    html = html.replace(
        "    · 黄框批注自动保存<br>\n    · 导出/导入 JSON 可备份批注与新界面",
        "    · 黄框批注自动保存<br>\n"
        "    · <b>批注→功能框</b>：把批注整理成功能点写入功能列表并清空黄框<br>\n"
        "    · 导出/导入 JSON 可备份批注与新界面",
        1,
    )
    return html


def main() -> None:
    html = BOARD.read_text(encoding="utf-8")
    html = patch_catalog(html)
    html = patch_ui(html)
    BOARD.write_text(html, encoding="utf-8")
    print("updated", BOARD.name, "size", BOARD.stat().st_size)
    print("feature packs", len(FEATURES))


if __name__ == "__main__":
    main()
