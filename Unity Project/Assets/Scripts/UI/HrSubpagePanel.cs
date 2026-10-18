using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>HR 子页共享上下文。</summary>
    public static class HrContext
    {
        // 招聘条件：由 n112 职业介绍所的「过滤 · 招聘条件」面板读写。
        // 渠道不在这里存——它就是顶栏「来源」芯片的当前档（RecruitmentOfficePanel._src），存两份必然漂移。
        public static string RecruitPosition = "飞行员";
        public static int RecruitNeeded = 6;
        public static int RecruitUrgency = 1;   // 0常绿 1计划 2紧急
        public static string SelectedDept = "OPS";
        public static string SelectedStaffId = "";
        public static bool SelectedStaffIsExec;

        /// <summary>
        /// 设定招募岗位并同步默认需求人数。需求取「编制 − 在职 − 在途」的真实缺口，
        /// 避免默认 6 人在小机队下被 LaunchRecruitment 静默夹紧，玩家看到的数字与实际发出的不一致。
        /// </summary>
        public static void PresetPosition(string position)
        {
            if (string.IsNullOrEmpty(position)) return;
            RecruitPosition = position;
            int suggest = RecruitNeeded;
            if (HrManager.RecruitRoomLeft(GameState.Instance, position, out _, out _, out _, out int room) && room > 0)
                suggest = Mathf.Clamp(room, 1, 12);
            else if (suggest <= 0) suggest = 6;
            RecruitNeeded = suggest;
        }
    }

    /// <summary>
    /// HR 子页：n86 组织规划与招聘 / n88 绩效与薪酬 /
    /// n89 员工关系与文化 / n90 合规与风险 / n37 工会谈判桌 / n91 人才档案与继任。
    ///
    /// 原 n99「招聘策略弹窗」已整体并入 n112 职业介绍所（CEO 裁定 2026-10-08）。
    /// 原 n87 培训与发展在本类的实现是死代码（HrRuntimeFactory 对该 id 直接返回
    /// TrainingHubPanel），已删除；培训入口走「人力职能」的职能卡。
    /// </summary>
    public class HrSubpagePanel : BasePanel
    {
        /// <summary>收件箱工会提案对应的邮件 id，选姿态后结案。</summary>
        public static string PendingUnionMailId;

        [SerializeField] string pageKind;

        string _pageKind;
        Transform _content;
        TextMeshProUGUI _toast;

        public static HrSubpagePanel Build(string pageKind, string title)
        {
            var root = UIFactory.Panel(null, "HrSub_" + pageKind, UIFactory.Fill,
                new Color(0.02f, 0.05f, 0.1f, 0.72f), rounded: false);
            var card = UIFactory.Panel(root.transform, "Card",
                new UIFactory.Anchor(new Vector2(0.06f, 0.08f), new Vector2(0.94f, 0.94f), Vector2.zero, Vector2.zero),
                UITheme.Panel);
            UIFactory.AddBorder(card, UITheme.Border, new Vector2(1.2f, -1.2f));
            var cg = card.AddComponent<CanvasGroup>();

            var titleLabel = UIFactory.Label(card.transform, "Title", title, 22,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.90f), new Vector2(0.78f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);
            var close = UIFactory.Button(card.transform, "Close", "×",
                new UIFactory.Anchor(new Vector2(0.90f, 0.90f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 20);
            UIFactory.Button(card.transform, "Back", "← 返回",
                new UIFactory.Anchor(new Vector2(0.04f, 0.02f), new Vector2(0.20f, 0.10f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 14);

            var content = UIFactory.Panel(card.transform, "Content",
                new UIFactory.Anchor(new Vector2(0.04f, 0.12f), new Vector2(0.96f, 0.88f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0), rounded: false);
            content.GetComponent<Image>().raycastTarget = false;

            var toast = UIFactory.Label(card.transform, "Toast", "", 12,
                TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.20f, 0.005f), new Vector2(0.80f, 0.03f), Vector2.zero, Vector2.zero),
                UITheme.Warning);

            var panel = root.AddComponent<HrSubpagePanel>();
            panel.pageKind = pageKind;
            panel._pageKind = pageKind;
            panel._content = content.transform;
            panel._toast = toast;
            panel.AssignRefs(root.GetComponent<RectTransform>(), titleLabel, close, cg);
            panel.BindPage(pageKind, pageKind == "n86" ? "人力职能" : title);
            panel.RefreshBody();
            return panel;
        }

        public override void BindPage(string pageId, string title = null)
        {
            base.BindPage(pageId, title);
            _pageKind = NormalizeKind(pageId);
            pageKind = _pageKind;
            if (titleLabel != null && !string.IsNullOrEmpty(title))
                titleLabel.text = _pageKind == "n86" ? "人力职能" : title;
        }

        protected override void Awake()
        {
            if (root == null) root = transform as RectTransform;
            ResolveRefs();
            WireBackButton();
        }

        void OnEnable()
        {
            ResolveRefs();
            WireBackButton();
            RefreshBody();
        }

        public override void Show()
        {
            base.Show();
            ResolveRefs();
            RefreshBody();
        }

        void ResolveRefs()
        {
            if (_content == null)
            {
                var c = transform.Find("Card/Content");
                if (c != null) _content = c;
            }
            if (_toast == null)
            {
                var t = transform.Find("Card/Toast");
                if (t != null) _toast = t.GetComponent<TextMeshProUGUI>();
            }
            if (string.IsNullOrEmpty(_pageKind))
                _pageKind = NormalizeKind(!string.IsNullOrEmpty(pageKind) ? pageKind : PageId);
        }

        public void RefreshBody()
        {
            ResolveRefs();
            if (_content == null) return;
            Clear(_content);

            switch (_pageKind)
            {
                case "n86": RenderOrgRecruit(); break;
                // n87 培训与发展由 TrainingHubPanel 承载（HrRuntimeFactory 对 n87 直接返回该面板），
                // 本类原有的 RenderTraining 从未被执行，属死代码，已删除。入口走「人力职能」的职能卡。
                case "n88": RenderPerformance(); break;
                case "n89": RenderEmployeeRelation(); break;
                case "n90": RenderCompliance(); break;
                case "n37": RenderUnion(); break;
                case "n91": RenderTalent(); break;
                default: RenderFallback(); break;
            }
        }

        static string NormalizeKind(string pageId)
        {
            if (string.IsNullOrEmpty(pageId)) return "";
            if (pageId.StartsWith("n86")) return "n86";
            if (pageId.StartsWith("n88")) return "n88";
            if (pageId.StartsWith("n89")) return "n89";
            if (pageId.StartsWith("n90")) return "n90";
            if (pageId.StartsWith("n37")) return "n37";
            if (pageId.StartsWith("n91")) return "n91";
            return pageId;
        }

        void Toast(string msg) { if (_toast != null) _toast.text = msg ?? ""; }

        void Clear(Transform host)
        {
            for (int i = host.childCount - 1; i >= 0; i--)
                Destroy(host.GetChild(i).gameObject);
        }

        TextMeshProUGUI L(string name, string text, float x0, float y0, float x1, float y1,
            int size, Color? color = null, bool bold = false, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            return UIFactory.Label(_content, name, text, size, align,
                new UIFactory.Anchor(new Vector2(x0, y0), new Vector2(x1, y1), Vector2.zero, Vector2.zero),
                color ?? UITheme.TextPrimary, bold ? FontStyles.Bold : FontStyles.Normal);
        }

        Button B(string name, string text, float x0, float y0, float x1, float y1,
            Color? color, UnityEngine.Events.UnityAction action, int size = 13)
        {
            return UIFactory.Button(_content, name, text,
                new UIFactory.Anchor(new Vector2(x0, y0), new Vector2(x1, y1), Vector2.zero, Vector2.zero),
                color ?? UITheme.Accent, action, size);
        }

        void RenderFallback()
        {
            L("F", "该子页规格尚未展开。", 0.0f, 0.5f, 1.0f, 0.7f, 16, UITheme.TextMuted);
        }

        // ================= n86 人力职能（职能 hub）=================
        //
        // 设计（CEO 裁定 · 方案A + 2026-10-08「避免与职业介绍所重复」）：
        // 本页只做职能入口，不承载任何招聘流程。
        // 职业介绍所（n112）已拥有完整招聘闭环——候选人名单 + 岗位/渠道选择 + 编制缺口 +
        // 在途管道与 ETA + 确认到岗，因此本页删除：
        //   空缺率 / 人力成本（n112 KPI 条已有）
        //   编制缺口数字（n112 KPI「运行岗缺口」+ ④ 编制与成本预估已有）
        //   在途招聘管道明细与「推进管道 → 确认到岗」（连同 ETA 一起搬进 n112 ④ 在途行）
        //   发起招聘入口（职业介绍所与人力职能是同排页签，无需二次跳转）
        // 仅保留别处没有的两块：在岗总人数、机组战力对航线利润的折损乘数
        //（该乘数无任何其它 UI 出口，删了玩家只会看到营收莫名下降）。
        void RenderOrgRecruit()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            var hr = HrManager.EnsureState(gs);

            float revenueWan = gs.monthlyRevenue * 12f;
            float ratio = HrEconomics.LaborCostRatio(gs.LaborCostAnnualWan, revenueWan);
            L("H", "人力职能 · 各职能入口", 0.0f, 0.905f, 1.0f, 0.980f, 15, UITheme.Accent, true);
            // 总人数为派生值（每机 90 人 × 在役机队 + 招聘增量），故把构成一并显示，
            // 否则玩家看到数字随买/退飞机跳变会以为出错。
            int heads = gs.TotalHeadcount;
            int headsBase = HrEconomics.EffectiveHeadcount(gs.ActiveFleetCount, hr.headcountPerAircraft, 0);
            int headsAdd = heads - headsBase;
            string headsNote = headsAdd > 0
                ? $"在编 {heads} 人（机队基准 {headsBase} + 招聘增量 {headsAdd}）"
                : $"在编 {heads} 人（机队基准 {headsBase}）";
            L("Kpi", headsNote, 0.0f, 0.835f, 1.0f, 0.900f, 13);

            // —— 唯一件：机组战力对航线利润的折损乘数 ——
            // 未建 / 未达标机组时航线利润按 CrewQualityMultiplier 折损；该乘数无任何其它 UI 出口。
            // 编制缺口数字与补齐渠道不在此重复，统一指向「职业介绍所」。
            //
            // ★2026-10-14 第十三次问询（CEO：「机组是包括乘务人员吗？」）：本乘数**只按驾驶舱覆盖率**算
            //   （`ComputeCrewQualityMultiplier` 走 IsPilot）。「机组」二字在界面上容易被读成全套机组，
            //   故所有文案一律写明**驾驶舱**；客舱乘务有自己的编制与招聘渠道（见「乘务员」岗位），
            //   并在排班闸门 `CrewCapacityLogic` 里与驾驶舱两岗取小 —— 但不参与这个利润乘数。
            gs.GetFleetStaffing(out int actFleet, out _, out _);
            int pilotEst = gs.PilotEstablishment;
            int pilotOn = gs.PilotFilled;
            float crewMult = gs.CrewQualityMultiplier;
            // 乘数 = 保底 0.85 + (质量乘数 − 0.85) × 覆盖率。因此有两个独立抓手：
            // 缺编（覆盖率）与战力（质量），UI 必须区分，否则玩家不知道该招人还是该培训。
            string crewEffect;
            if (pilotEst <= 0)
            {
                crewEffect = $"驾驶舱机组战力：暂无在役机队（在役 {actFleet} 架），无机组折损";
            }
            else if (pilotOn <= 0)
            {
                crewEffect = $"⚠ 未建驾驶舱机组（飞行员）→ 航线利润折损至下限 ×{HrEconomics.CrewUnstaffedMultiplier:F2}"
                           + $"（在编 {pilotEst} 人 · 招任意渠道飞行员都优于不招）";
            }
            else
            {
                int gap = HrEconomics.CrewGapHeadcount(pilotOn, pilotEst);
                float cover = HrEconomics.CrewCoverage(pilotOn, pilotEst);
                bool weak = crewMult < 1f;
                string cause = gap > 0
                    ? (weak ? $"缺编 {gap} 人 · 战力亦低于达标线 {HrEconomics.CrewAdequacyBaseline:F2}"
                            : $"缺编 {gap} 人，补足编制即达 ×1.00")
                    : (weak ? $"编制齐备 · 战力低于达标线 {HrEconomics.CrewAdequacyBaseline:F2}（培训养成或改招猎头 / 海外）"
                            : "编制齐备 · 战力达标");
                crewEffect = (weak ? "⚠ " : "") + $"驾驶舱机组覆盖 {cover * 100:F0}%（飞行员 {pilotOn}/{pilotEst}）"
                           + $" → 航线利润 ×{crewMult:F2}（上限 ×{HrEconomics.CrewQualityMax:F2}）"
                           + "\n" + cause;
            }
            bool crewAtRisk = pilotEst > 0 && (pilotOn < pilotEst || crewMult < 1f);
            // 指标正常时不追加指路文案，避免占满三行（该标签高度按三行上限设计）。
            string crewHint = crewAtRisk ? "\n编制缺口与补齐渠道见「职业介绍所」（同排页签）" : "";
            L("Crew", crewEffect + crewHint,
                0.0f, 0.680f, 1.0f, 0.820f, 12,
                crewAtRisk ? UITheme.Warning : UITheme.Success, true);

            // —— 各职能入口（n88–n91 此前只能靠邮件深链到达）——
            L("FnH", "各职能入口", 0.0f, 0.600f, 0.60f, 0.665f, 12, UITheme.Accent, true);
            int hipo = SuccessionLogic.HipoEmployed(gs.staffPool).Count;
            FnCard(0, $"培训与发展 · {hr.trainings.Count} 个项目", "n87_培训与发展");
            FnCard(1, $"绩效与薪酬 · 占营收 {ratio * 100:F0}%", "n88_绩效与薪酬");
            FnCard(2, $"员工关系 · 满意度 {hr.employeeSatisfaction:F0}", "n89_员工关系与文化");
            FnCard(3, "合规与风险 · 资质 / 疲劳 / EEO", "n90_合规与风险");
            FnCard(4, $"人才继任 · 高潜 {hipo} 人", "n91_人才档案与继任");
            FnCard(5, $"工会谈判 · 满意度 {hr.union.satisfaction:F0}", "n37_工会谈判桌");

            L("Tip", "招聘 / 编制缺口 / 在途管道 / 候选人处置都在「职业介绍所」（同排页签）",
                0.0f, 0.030f, 1.0f, 0.110f, 11, UITheme.TextMuted);
        }

        /// <summary>职能入口卡（3 列 × 2 行），点击进入对应职能页。</summary>
        void FnCard(int index, string label, string pageId)
        {
            int col = index % 3;
            int row = index / 3;
            float x0 = 0.005f + col * 0.332f;
            float x1 = x0 + 0.322f;
            float y1 = row == 0 ? 0.580f : 0.435f;
            B("Fn" + index, label, x0, y1 - 0.125f, x1, y1, UITheme.PanelDeep,
                () => HrPanelUI.OpenHrPage(pageId), 12);
        }

        // ================= n88 绩效与薪酬 =================
        void RenderPerformance()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            var hr = HrManager.EnsureState(gs);
            float revenueWan = gs.monthlyRevenue * 12f;
            float ratio = HrEconomics.LaborCostRatio(gs.LaborCostAnnualWan, revenueWan);
            L("H", "绩效与薪酬管理 · 激励体系的指挥棒", 0.0f, 0.92f, 1.0f, 0.98f, 15, UITheme.Accent, true);
            L("Cost", $"总人力成本 {gs.LaborCostAnnualWan:F0} 万 · 人力成本占营收比 {ratio * 100:F0}%（目标<25%）",
                0.0f, 0.82f, 1.0f, 0.90f, 13, ratio < 0.25f ? UITheme.Success : UITheme.Warning, true);
            if (!string.IsNullOrEmpty(hr.lastCompNote))
                L("Last", hr.lastCompNote, 0.0f, 0.74f, 1.0f, 0.82f, 12, UITheme.TextMuted);
            L("S1", "薪酬结构：基本工资 / 飞行小时费 / 绩效奖金 / 福利成本（五险一金+补充）", 0.0f, 0.66f, 1.0f, 0.74f, 12, UITheme.TextMuted);
            L("S2", "分岗位薪酬对标：飞行员 vs 行业P50/P75 · 机务 · 乘务 · 行政 · 高管（固定+长期激励）", 0.0f, 0.60f, 1.0f, 0.68f, 12, UITheme.TextMuted);
            L("S3", "绩效看板：S/A/B/C/D 五档强制分布 · 高潜人才池 · 离职预警 · 绩效与奖金联动", 0.0f, 0.52f, 1.0f, 0.60f, 12, UITheme.TextMuted);
            L("S4", "奖金池分配模拟器：利润→奖金池提取比例 · 各部门分配权重 · 实时预估", 0.0f, 0.44f, 1.0f, 0.52f, 12, UITheme.TextMuted);

            B("Salary", "年度调薪 → 收件箱", 0.02f, 0.28f, 0.34f, 0.38f, UITheme.Accent,
                () =>
                {
                    bool ok = InboxManager.QueueCompensationMail(gs, "raise");
                    Toast(ok ? "调薪方案已送收件箱，请 CEO 批准。" : "本年度调薪方案已提交过。");
                }, 12);
            B("Lti", "高管长期激励", 0.38f, 0.28f, 0.70f, 0.38f, UITheme.PanelDeep,
                () =>
                {
                    bool ok = InboxManager.QueueCompensationMail(gs, "exec");
                    Toast(ok ? "高管激励方案已送收件箱。" : "本年度高管激励已提交过。");
                }, 12);
            B("Bonus", "奖金池方案", 0.74f, 0.28f, 0.98f, 0.38f, UITheme.Accent,
                () =>
                {
                    bool ok = InboxManager.QueueCompensationMail(gs, "bonus");
                    Toast(ok ? "奖金池方案已送收件箱。" : "本年度奖金方案已提交过。");
                }, 12);
        }

        // ================= n89 员工关系与文化 =================
        void RenderEmployeeRelation()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            var hr = HrManager.EnsureState(gs);
            float attrition = HrEconomics.AttritionRate(hr.employeeSatisfaction);
            L("H", "员工关系与企业文化 · 和谐软环境构建", 0.0f, 0.92f, 1.0f, 0.98f, 15, UITheme.Accent, true);
            L("Sat", $"员工满意度 {hr.employeeSatisfaction:F0} · 预估年流失率 {attrition * 100:F1}%",
                0.0f, 0.82f, 1.0f, 0.90f, 13, hr.employeeSatisfaction >= 70 ? UITheme.Success : UITheme.Warning, true);
            L("S1", "企业文化：使命/愿景/价值观 · 文化大使网络 · 新员工文化融入度", 0.0f, 0.68f, 1.0f, 0.78f, 12, UITheme.TextMuted);
            L("S2", "员工沟通：季度全员大会 · CEO信箱 · 匿名反馈通道 · 员工论坛", 0.0f, 0.60f, 1.0f, 0.68f, 12, UITheme.TextMuted);
            L("S3", "员工关怀：EAP心理援助 · 健康体检 · 免票福利 · 子女教育基金", 0.0f, 0.52f, 1.0f, 0.60f, 12, UITheme.TextMuted);
            L("S4", "离职洞察：自愿/非自愿离职率 · 关键人才离职率 · 离职面谈摘要", 0.0f, 0.44f, 1.0f, 0.52f, 12, UITheme.TextMuted);

            B("Care", "员工关怀计划", 0.02f, 0.28f, 0.34f, 0.38f, UITheme.Accent,
                () => { Toast("员工满意度行动计划已提交。"); }, 12);
            B("Dispute", "劳动争议处理", 0.38f, 0.28f, 0.70f, 0.38f, UITheme.PanelDeep,
                () => { Toast("当前争议案件 2 件 · 调解成功率 78%。"); }, 12);
            B("Employer", "雇主品牌", 0.74f, 0.28f, 0.98f, 0.38f, UITheme.PanelDeep,
                () => { Toast("雇主品牌：最佳雇主排名 +2 位 · 员工内推率 25%。"); }, 12);
        }

        // ================= n90 合规与风险 =================
        void RenderCompliance()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            var hr = HrManager.EnsureState(gs);
            L("H", "合规与风险管理 · 人力资源防火墙", 0.0f, 0.92f, 1.0f, 0.98f, 15, UITheme.Accent, true);
            L("Score", "合规检查总分：92 / 100（劳动法 · 民航规 · 数据保护）", 0.0f, 0.82f, 1.0f, 0.90f, 13, UITheme.Success, true);
            L("S1", "劳动法合规：合同签署率 · 加班费支付 · 社保公积金缴纳率 · 劳务派遣比例上限", 0.0f, 0.68f, 1.0f, 0.78f, 12, UITheme.TextMuted);
            L("S2", "民航局人员资质：飞行员执照/体检 · 乘务安全培训记录 · 机务执照/机型授权 · 到期预警", 0.0f, 0.60f, 1.0f, 0.68f, 12, UITheme.TextMuted);
            L("S3", "疲劳风险管理（FRMS）：FTL/FDTL 合规 · 休息期保障 · 疲劳报告处理率", 0.0f, 0.52f, 1.0f, 0.60f, 12, UITheme.TextMuted);
            L("S4", "员工数据保护（PIPL/GDPR）：信息收集合规 · 访问权限审计 · 跨境传输", 0.0f, 0.44f, 1.0f, 0.52f, 12, UITheme.TextMuted);

            B("Renew", "资质到期 → 续期通知", 0.02f, 0.28f, 0.34f, 0.38f, UITheme.Accent,
                () => { Toast("已发送续期通知至相关部门收件箱。"); }, 12);
            B("Fatigue", "疲劳报告 → 调查", 0.38f, 0.28f, 0.70f, 0.38f, UITheme.PanelDeep,
                () => { Toast("疲劳安全调查已发起（事件弹窗）。"); }, 12);
            B("Eeo", "平等就业（EEO）报告", 0.74f, 0.28f, 0.98f, 0.38f, UITheme.PanelDeep,
                () => { Toast("年度 EEO 报告：招聘公平性 +2% · 薪酬性别差距收窄。"); }, 12);
        }

        // ================= n37 工会谈判桌 =================
        void RenderUnion()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            var hr = HrManager.EnsureState(gs);
            var u = hr.union;
            L("H", "工会谈判桌", 0.0f, 0.92f, 1.0f, 0.98f, 15, UITheme.Accent, true);
            L("State", $"工会满意度 {u.satisfaction:F0} · 罢工倒计时 {u.strikeCountdown} 月 · CBA 到期 {u.cbaExpiresIn} 月 · 罢工风险 {hr.strikeRisk:F0}",
                0.0f, 0.80f, 1.0f, 0.90f, 13, hr.strikeRisk >= 60 ? UITheme.Danger : UITheme.Warning, true);
            L("Demand", "诉求列表：加薪 8% · 改善排班 · 增加年假 · 提高夜班补贴", 0.0f, 0.68f, 1.0f, 0.78f, 12, UITheme.TextMuted);

            B("Yield", "让步（成本高/满意度↑/风险↓）", 0.02f, 0.50f, 0.60f, 0.60f, UITheme.Success,
                () => PickUnion(0), 12);
            B("Tough", "强硬（成本低/满意度↓/风险↑）", 0.02f, 0.36f, 0.60f, 0.46f, UITheme.Danger,
                () => PickUnion(1), 12);
            B("Delay", "拖延（折中）", 0.02f, 0.22f, 0.60f, 0.32f, UITheme.Warning,
                () => PickUnion(2), 12);

            L("Est", "结果回写：员工满意度与准点率联动 · 停飞风险预警 · 收件箱提案在此选姿态后结案", 0.64f, 0.34f, 1.0f, 0.60f, 12, UITheme.TextMuted);
        }

        void PickUnion(int choice)
        {
            if (HrManager.Instance == null)
            {
                var go = new GameObject("HrManager");
                go.AddComponent<HrManager>();
            }
            string msg = "谈判失败";
            bool ok = HrManager.Instance.NegotiateUnion(choice, out msg);
            Toast(msg);
            if (ok)
            {
                InboxManager.CompleteUnionMail(PendingUnionMailId, msg);
                PendingUnionMailId = null;
            }
            RefreshBody();
        }

        // ================= n91 人才档案与继任 =================
        void RenderTalent()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            PersonnelGenerator.EnsurePool(gs);
            var hipo = SuccessionLogic.HipoEmployed(gs.staffPool);
            int watch = SuccessionLogic.AttritionWatch(gs.staffPool);
            float cover = SuccessionLogic.CoverPercent(hipo.Count);
            L("H", "人才档案与继任计划 · 关键人才库", 0.0f, 0.92f, 1.0f, 0.98f, 15, UITheme.Accent, true);
            L("Hipo", $"高潜人才池：{hipo.Count} 人 · 关键岗位继任覆盖率 {cover:F0}% · 离职预警 {watch} 人",
                0.0f, 0.82f, 1.0f, 0.90f, 13, UITheme.Success, true);
            if (hipo.Count == 0)
                L("S1", "暂无高潜在职人员。培训或招聘后会出现在此名单，点击姓名打开员工档案。", 0.0f, 0.62f, 1.0f, 0.78f, 13, UITheme.TextMuted);
            else
            {
                int take = hipo.Count > 4 ? 4 : hipo.Count;
                for (int i = 0; i < take; i++)
                {
                    var p = hipo[i];
                    int idx = i;
                    float y1 = 0.78f - i * 0.10f;
                    B("P" + i, $"{p.fullName} · {StaffEstablishment.DisplayTitle(p.jobTitle, p.rank)} · 潜力 {(p.potential * 100f):F0}%",
                        0.02f, y1 - 0.09f, 0.98f, y1, UITheme.PanelDeep,
                        () =>
                        {
                            HrContext.SelectedStaffId = hipo[idx].id;
                            HrContext.SelectedStaffIsExec = hipo[idx].rank == "负责人";
                            HrPanelUI.OpenHrPage("n114_员工信息");
                        }, 13);
                }
            }

            B("Appoint", "打开首位继任档案", 0.02f, 0.18f, 0.34f, 0.28f, UITheme.Accent,
                () =>
                {
                    if (hipo.Count == 0) { Toast("暂无继任候选人。"); return; }
                    HrContext.SelectedStaffId = hipo[0].id;
                    HrContext.SelectedStaffIsExec = hipo[0].rank == "负责人";
                    HrPanelUI.OpenHrPage("n114_员工信息");
                }, 12);
            B("Develop", "高潜培养计划", 0.38f, 0.18f, 0.70f, 0.28f, UITheme.PanelDeep,
                () => HrPanelUI.OpenHrPage("n87_培训与发展"), 12);
            B("Dept", "部门编制", 0.74f, 0.18f, 0.98f, 0.28f, UITheme.PanelDeep,
                () => HrPanelUI.OpenHrPage("n113_部门信息"), 12);
        }

        // n99「招聘策略弹窗」已于 2026-10-08 整体并入 n112 职业介绍所：
        // 岗位选择 / 编制缺口 / 紧缺指数 / 四渠道比选 / 呈请批复，现在都是
        // RecruitmentOfficePanel 里的「过滤 · 招聘条件」过滤面板（FM 球探搜索样式）。
        // 招聘口径（RecruitRoomLeft / RecruitCostPerHire / RecruitSuccessRate / RecruitLeadWeeks）
        // 未做任何改动，只是换了承载页面，不再有两处重复实现。
    }
}
