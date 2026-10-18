using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>
    /// n114 人员信息：概况 / 合同 / 历史 / 培训（在职），概况 / 历史 / 资质（待聘候选人）。
    ///
    /// CEO 裁定 2026-10-10：本页同时是**人员处置处**——职业介绍所名单右侧那一列动作
    /// （聘请 / 录用 / 退回 / 调岗）整体搬到这里，名单变只读。
    ///
    /// 页签按**是否在职**分为两套，且必须按语义（而非下标）判别：
    /// 非在职者没有在职合同、不参加公司在办培训，也没有继任席位，故三项一律不出现在其页面上。
    /// 两套页签若共用同一个下标字段，切人时必然错位（合同 = 历史），故本页用
    /// <see cref="Tab"/> 枚举自持状态。
    /// </summary>
    public class StaffProfilePanel : BasePanel
    {
        /// <summary>页签的语义标识。下标会因在职/待聘两套集合而错位，故不用下标。</summary>
        enum Tab { Overview, Contract, History, Training, Certs }

        static Tab _tab = Tab.Overview;

        Transform _content;
        TextMeshProUGUI _toast;

        public static StaffProfilePanel Build()
        {
            var root = UIFactory.Panel(null, "n114_员工信息", UIFactory.Fill,
                new Color(0.02f, 0.05f, 0.1f, 0.72f), rounded: false);
            var card = UIFactory.Panel(root.transform, "Card",
                new UIFactory.Anchor(new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.95f), Vector2.zero, Vector2.zero),
                UITheme.Panel);
            UIFactory.AddBorder(card, UITheme.Border, new Vector2(1.2f, -1.2f));
            var cg = card.AddComponent<CanvasGroup>();

            var title = UIFactory.Label(card.transform, "Title", "员工信息", 22,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.90f), new Vector2(0.70f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);
            var close = UIFactory.Button(card.transform, "Close", "×",
                new UIFactory.Anchor(new Vector2(0.90f, 0.90f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 20);
            UIFactory.Button(card.transform, "Back", "← 返回",
                new UIFactory.Anchor(new Vector2(0.04f, 0.02f), new Vector2(0.20f, 0.10f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 14);

            var content = UIFactory.Panel(card.transform, "Content",
                new UIFactory.Anchor(new Vector2(0.03f, 0.12f), new Vector2(0.97f, 0.88f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0), rounded: false);
            content.GetComponent<Image>().raycastTarget = false;

            var toast = UIFactory.Label(card.transform, "Toast", "", 12, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.22f, 0.02f), new Vector2(0.88f, 0.10f), Vector2.zero, Vector2.zero),
                UITheme.Warning);

            var panel = root.AddComponent<StaffProfilePanel>();
            panel._content = content.transform;
            panel._toast = toast;
            panel.AssignRefs(root.GetComponent<RectTransform>(), title, close, cg);
            panel.BindPage("n114_员工信息", "员工信息");
            return panel;
        }

        protected override void Awake()
        {
            base.Awake();
            WireBackButton();
        }

        public override void Show()
        {
            base.Show();
            Refresh();
        }

        void OnEnable() => Refresh();

        void Refresh()
        {
            if (_content == null)
            {
                var c = transform.Find("Card/Content");
                if (c != null) _content = c;
            }
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);

            string id = HrContext.SelectedStaffId;
            var mgr = DepartmentManager.Instance;
            var gs = GameState.Instance;
            PersonnelGenerator.EnsurePool(gs);

            var p = PersonnelGenerator.Find(gs, id);

            // 退回介绍所会把这个人**从池里移除**（HrManager.ReleaseCandidate），
            // 之后再 Refresh 就查不到档案了。此处给一个明确的收尾状态 + 返回入口，
            // 否则页面会停在「未选择人员 / 全是零」的空壳上，看起来像坏了。
            if (p == null)
            {
                if (titleLabel != null) titleLabel.text = "人员信息";
                L("None", "该档案已不在名册（可能已被退回介绍所或已离开公司）。",
                    0.00f, 0.56f, 1.00f, 0.68f, 14, UITheme.Warning);
                B("Back2", "← 返回职业介绍所", 0.00f, 0.44f, 0.26f, 0.53f,
                    UITheme.PanelDeep, () => HrPanelUI.OpenHrPage("n112_职业介绍所"), 13);
                return;
            }

            string name = p.fullName;
            string dept = p.department;
            string rank = p.rank;
            string role = StaffEstablishment.DisplayTitle(p.jobTitle, p.rank);
            string nation = p.nationality;
            int age = p.age;
            float salary = p.baseSalary;
            float loyalty = p.loyalty;
            float pot = p.potential;
            float eff = p.efficiencyBonus;
            string traits = string.IsNullOrEmpty(p.traitsCsv) ? "—" : p.traitsCsv;
            bool employed = !p.onMarket && (mgr != null && mgr.IsOccupied(p.id)
                || StaffEstablishment.IsEmployedId(p.id, gs, mgr));

            // 待聘 = 介绍所候选人（onMarket，尚未在编）。
            // 既不在职也不待聘（如已解聘且已离开介绍所）走「无处置动作」分支。
            bool candidate = !employed && p.onMarket;

            if (titleLabel != null) titleLabel.text = name;
            L("Name", name, 0.00f, 0.90f, 0.70f, 1.00f, 22, UITheme.TextPrimary, true);

            // 副标题不再打印「部门 · 职位」给非在职者 —— 那会读成「他已是运营部总监」，
            // 而那把椅子可能正有人坐着。非在职者改为标注身份与来源（意向岗位在「历史」页）。
            if (employed)
                L("Sub", $"{DepartmentCatalog.DisplayName(dept)}  ·  {role}",
                    0.00f, 0.82f, 0.70f, 0.90f, 14, UITheme.TextMuted);
            else if (candidate)
                L("Sub", $"待聘候选人  ·  来源 {HrEconomics.SourceName(p.source)}",
                    0.00f, 0.82f, 0.70f, 0.90f, 14, UITheme.TextMuted);
            else
                L("Sub", "不在职（无在编岗位）", 0.00f, 0.82f, 0.70f, 0.90f, 14, UITheme.TextMuted);

            // 两套页签集合：非在职只留「看能力 / 看履历 / 看资质」三项。
            var tabs = candidate
                ? new[] { Tab.Overview, Tab.History, Tab.Certs }
                : new[] { Tab.Overview, Tab.Contract, Tab.History, Tab.Training };
            string[] tabNames = candidate
                ? new[] { "概况", "历史", "资质" }
                : new[] { "概况", "合同", "历史", "培训" };
            if (System.Array.IndexOf(tabs, _tab) < 0) _tab = Tab.Overview;   // 切人时回落到概况

            for (int t = 0; t < tabs.Length; t++)
            {
                var which = tabs[t];
                bool sel = which == _tab;
                float x0 = t * 0.16f;
                UIFactory.Button(_content, "Tab" + t, tabNames[t],
                    new UIFactory.Anchor(new Vector2(x0, 0.74f), new Vector2(x0 + 0.15f, 0.81f), Vector2.zero, Vector2.zero),
                    sel ? UITheme.NavActive : UITheme.NavIdle, () =>
                    {
                        _tab = which;
                        Refresh();
                    }, 13);
            }

            RenderActions(p, employed, candidate, id, gs);

            if (_tab == Tab.Overview) RenderOverview(dept, rank, age, nation, traits, loyalty, pot, eff);
            else if (_tab == Tab.Contract) RenderContract(id, p, salary, gs);
            else if (_tab == Tab.History) RenderHistory(name, dept, role, employed, p, gs);
            else if (_tab == Tab.Training) RenderTraining(id, p, gs);
            else RenderCerts(p, gs);
        }

        /// <summary>
        /// 人员处置区（原职业介绍所名单右侧那一列动作）。
        /// 这是本页的**唯一入口**，故按人员状态给出不同动作，且每类只出现一个语义：
        /// 待聘总监 → 聘请（CEO 直接任命）；渠道候选人 → 录用 / 退回；
        /// 在编可调岗者 → 调岗；在编者 → 继任计划（原动作，保留）。
        /// 动作区起点 0.66：页签最多 4 个、末个止于 0.63，再往左就会压住「培训」页签。
        /// </summary>
        void RenderActions(PersonnelData p, bool employed, bool candidate, string id,
            GameState gs)
        {
            const float y0 = 0.74f, y1 = 0.81f;

            // —— 待聘总监：聘请（= 任命总监，占该部门总监席）——
            if (candidate && StaffRecruitment.CanCeoHire(p))
            {
                B("Hire", "聘请", 0.66f, y0, 0.92f, y1, UITheme.Accent, () =>
                {
                    var mgr = DepartmentManager.Instance;
                    if (mgr == null) { Toast("DepartmentManager 未就绪。"); return; }
                    Toast(mgr.HireFromMarket(id)
                        ? $"已聘请 {p.fullName} 出任{DepartmentCatalog.DisplayName(p.department)}总监。"
                        : "无法聘请：该部门总监席可能已有人。");
                    Refresh();
                }, 14);
                return;
            }

            // —— 渠道候选人：录用（人力办理入职）/ 退回（离开介绍所）——
            if (candidate)
            {
                B("HireC", "录用", 0.66f, y0, 0.785f, y1, UITheme.Accent, () =>
                {
                    string msg = "录用失败。";
                    if (HrManager.Instance != null) HrManager.Instance.HireCandidate(id, out msg);
                    else msg = "HrManager 未就绪。";
                    Toast(msg);
                    Refresh();
                }, 13);
                B("Reject", "退回", 0.795f, y0, 0.92f, y1, UITheme.PanelDeep, () =>
                {
                    string msg = "退回失败。";
                    if (HrManager.Instance != null) HrManager.Instance.RejectCandidate(id, out msg);
                    else msg = "HrManager 未就绪。";
                    Toast(msg);
                    Refresh();   // 人被移出池 → Refresh 走「已不在名册」分支，提示保留在 Toast
                }, 13);
                return;
            }

            if (!employed) return;

            // —— 在编可调岗者：调岗（目标岗位由口径函数给出，与名单推导同源）——
            bool canTransfer = HrManager.CanTransfer(gs, p);
            bool showSucc = StaffRecruitment.CanCeoHire(p) || StaffRecruitment.CanCeoFire(p);
            if (canTransfer && showSucc)
            {
                B("Xfer", "调岗", 0.66f, y0, 0.775f, y1, UITheme.Success, () => DoTransfer(id, gs), 13);
                B("Succ", "继任计划", 0.785f, y0, 0.92f, y1, UITheme.Accent,
                    () => HrPanelUI.OpenHrPage("n91_人才档案与继任"), 13);
            }
            else if (canTransfer)
            {
                B("Xfer", "调岗", 0.66f, y0, 0.92f, y1, UITheme.Success, () => DoTransfer(id, gs), 14);
            }
            else if (showSucc)
            {
                // 总监：继任计划是原入口，保留在右上角（与改动前一致）
                B("Succ", "继任计划", 0.66f, y0, 0.92f, y1, UITheme.Accent,
                    () => HrPanelUI.OpenHrPage("n91_人才档案与继任"), 13);
            }
        }

        void DoTransfer(string id, GameState gs)
        {
            var p = PersonnelGenerator.Find(gs, id);
            if (p == null || !HrManager.TryFindTransferTarget(gs, p, out string dept, out string role))
            {
                Toast("暂无可匹配的空缺岗位。");
                return;
            }
            string msg = "调岗失败。";
            if (HrManager.Instance != null) HrManager.Instance.TransferStaff(id, dept, role, out msg);
            else msg = "HrManager 未就绪。";
            Toast(msg);
            Refresh();
        }

        /// <summary>
        /// 「培训」页签：持有证照 / 在办培训 / 培训履历 / 可考取证照。
        /// 此前个人页没有任何培训入口，玩家无处指定「给这个人安排培训」或「考某本证」。
        /// </summary>
        void RenderTraining(string id, PersonnelData p, GameState gs)
        {
            var hr = HrManager.EnsureState(gs);
            if (p == null || gs == null || hr == null)
            {
                L("TRnone", "未选择在职人员，或游戏未就绪。", 0.00f, 0.60f, 1.00f, 0.70f, 13, UITheme.TextMuted);
                return;
            }
            HrManager.EnsurePoolCerts(gs);
            var now = gs.SimUtc;
            int lv = hr.trainingCenterLevel;

            // —— 左栏：已持证 / 在办 / 履历 ——
            L("TH", "持证情况", 0.00f, 0.680f, 0.50f, 0.750f, 14, UITheme.Accent, true);
            var certSb = new System.Text.StringBuilder();
            if (p.certs == null || p.certs.Count == 0)
                certSb.Append("  暂无持证记录。");
            else
            {
                for (int i = 0; i < p.certs.Count; i++)
                {
                    var h = p.certs[i];
                    var d = CertificationCatalog.Find(h != null ? h.id : null);
                    if (d == null) continue;
                    certSb.AppendLine("  " + d.name + "　" + ExpiryText(h, now));
                }
            }
            L("THlist", certSb.ToString().TrimEnd(), 0.00f, 0.480f, 0.55f, 0.670f, 12, UITheme.TextPrimary);

            L("PB", "在办培训", 0.00f, 0.415f, 0.55f, 0.470f, 13, UITheme.Accent, true);
            var pbSb = new System.Text.StringBuilder();
            int inCourse = 0;
            for (int i = 0; i < hr.trainings.Count; i++)
            {
                var t = hr.trainings[i];
                if (t == null || t.graduated || !OnRoster(t, id)) continue;
                inCourse++;
                pbSb.AppendLine($"  {t.name}　完成率 {t.completionRate * 100f:F0}%" +
                                (t.certId != null ? "（考证）" : ""));
            }
            if (inCourse == 0) pbSb.Append("  当前未参加任何在办培训。");
            L("PBlist", pbSb.ToString().TrimEnd(), 0.00f, 0.250f, 0.55f, 0.410f, 12, UITheme.TextMuted);

            L("HI", "培训履历", 0.00f, 0.185f, 0.55f, 0.240f, 13, UITheme.Accent, true);
            var hiSb = new System.Text.StringBuilder();
            int done = 0;
            for (int i = 0; i < hr.trainings.Count; i++)
            {
                var t = hr.trainings[i];
                if (t == null || !t.graduated || !OnRoster(t, id)) continue;
                done++;
                hiSb.AppendLine($"  {t.name}　已结业");
            }
            if (done == 0) hiSb.Append("  暂无结业记录。");
            L("HIlist", hiSb.ToString().TrimEnd(), 0.00f, 0.060f, 0.55f, 0.180f, 12, UITheme.TextMuted);

            // —— 右栏：可考取证照 ——
            L("CVH", "可考取证照", 0.600f, 0.415f, 1.00f, 0.470f, 13, UITheme.Accent, true);
            var avail = CertificationLogic.AvailableFor(p, lv, now);
            if (avail.Count == 0)
            {
                L("CVtip", "  暂无可办证照（岗位不符、已持有，或培训中心等级不足）。\n" +
                           $"  当前中心 {lv} 级「{HrManager.CenterName(lv)}」。",
                    0.600f, 0.250f, 1.00f, 0.410f, 12, UITheme.TextMuted);
            }
            else
            {
                int cap = avail.Count > 3 ? 3 : avail.Count;
                for (int i = 0; i < cap; i++)
                {
                    var d = avail[i];
                    int q = CertificationLogic.DurationQuarters(d);
                    float cost = d.costWan * HrEconomics.TrainingCostFactor(lv);
                    float y = 0.300f - i * 0.095f;
                    UIFactory.Button(_content, "Cert" + i,
                        $"考取 {d.name} · {cost:F0} 万 · {q} 季",
                        new UIFactory.Anchor(new Vector2(0.600f, y), new Vector2(0.990f, y + 0.085f),
                            Vector2.zero, Vector2.zero),
                        UITheme.PanelDeep, () =>
                        {
                            string msg;
                            var mgr2 = HrManager.Instance;
                            if (mgr2 == null) msg = "HrManager 未就绪。";
                            else mgr2.StartCertification(id, d.id, out msg);
                            Toast(msg);
                            Refresh();
                        }, 11);
                }
            }

            // —— 安排培训：把此人加入某个在办的同轨项目 ——
            int joined = 0;
            for (int i = 0; i < hr.trainings.Count && joined < 2; i++)
            {
                var t = hr.trainings[i];
                if (t == null || t.graduated || t.track == "cert") continue;
                if (OnRoster(t, id)) continue;
                float x = 0.600f + joined * 0.205f;
                string pname = t.name;
                UIFactory.Button(_content, "Enroll" + joined, "加入 " + pname,
                    new UIFactory.Anchor(new Vector2(x, 0.020f), new Vector2(x + 0.190f, 0.100f),
                        Vector2.zero, Vector2.zero),
                    UITheme.PanelDeep, () =>
                    {
                        string msg;
                        if (HrManager.Instance != null) HrManager.Instance.EnrollInProgram(pname, id, out msg);
                        else msg = "HrManager 未就绪。";
                        Toast(msg);
                        Refresh();
                    }, 11);
                joined++;
            }
            if (joined == 0)
                L("EnrNone", "要为此人安排培训：先到「培训 → 人员培训」启动一个项目（如高潜管理培训）。",
                    0.600f, 0.020f, 1.000f, 0.100f, 11, UITheme.TextMuted);
        }

        static bool OnRoster(TrainingProgram t, string personId) =>
            t != null && t.memberIds != null && !string.IsNullOrEmpty(personId) && t.memberIds.Contains(personId);

        static string ExpiryText(CertHeld h, System.DateTime nowUtc)
        {
            int days = CertificationLogic.DaysToExpiry(h, nowUtc);
            if (days < 0) return "长期有效";
            if (days <= 0) return "已过期";
            return $"{days} 天后到期";
        }

        void RenderOverview(string dept, string rank, int age, string nation, string traits,
            float loyalty, float pot, float eff)
        {
            L("Bio", $"年龄 {(age > 0 ? age + " 岁" : "—")}　　国籍 {nation}　　职级 {rank}　　潜力 {(pot * 100f):F0}　　效率 {eff:F2}　　特性 {traits}",
                0.00f, 0.66f, 1.00f, 0.73f, 13, UITheme.TextMuted);

            var blocks = StaffRatingLogic.Build(dept, rank, loyalty, pot, eff);
            for (int b = 0; b < blocks.Length; b++)
            {
                float x0 = b / (float)blocks.Length;
                float x1 = (b + 1) / (float)blocks.Length - 0.02f;
                L("Bh" + b, blocks[b].heading, x0, 0.58f, x1, 0.65f, 14, UITheme.Accent, true);
                var items = blocks[b].items;
                for (int i = 0; i < items.Length; i++)
                {
                    float y1 = 0.56f - i * 0.10f;
                    float y0 = y1 - 0.09f;
                    int v = items[i].value;
                    L("Bn" + b + i, items[i].name, x0, y0, x0 + 0.18f, y1, 13, UITheme.TextMuted);
                    L("Bv" + b + i, v.ToString(), x0 + 0.18f, y0, x1, y1, 16, RateColor(v), true);
                }
            }
        }

        /// <summary>
        /// 「合同」页签。**仅在编人员有此页**（非在职者没有本公司在职合同，
        /// 页签集合在 <see cref="Refresh"/> 里就已按是否在职分流），故此处按在职口径直写。
        /// </summary>
        void RenderContract(string id, PersonnelData p, float salary, GameState gs)
        {
            int y = gs != null ? gs.year : 1;
            int q = gs != null ? gs.quarter : 1;
            L("CH", "合同细节", 0.00f, 0.64f, 0.50f, 0.72f, 14, UITheme.Accent, true);
            L("T1", "类型　　全职合同", 0.00f, 0.56f, 0.55f, 0.64f, 13, UITheme.TextPrimary);
            L("T2", salary > 0 ? $"年薪　　{salary:F0} 万" : "年薪　　—", 0.00f, 0.48f, 0.55f, 0.56f, 13, UITheme.TextPrimary);
            L("T3", $"期限　　至 本公司第 {y + 2} 年第 {q} 季（可续）",
                0.00f, 0.40f, 0.80f, 0.48f, 13, UITheme.TextPrimary);
            L("T4", StaffRecruitment.CanCeoHire(p) || StaffRecruitment.CanCeoFire(p)
                ? "总监合同由您直接签署或解除。"
                : "该岗位由人力与职能部门招聘、面试，录用须您在收件箱批准。解雇不由您直接办理。",
                0.00f, 0.32f, 0.80f, 0.40f, 13, UITheme.TextMuted);

            if (StaffRecruitment.CanCeoFire(p))
            {
                UIFactory.Button(_content, "Term", "中止合同",
                    new UIFactory.Anchor(new Vector2(0.00f, 0.14f), new Vector2(0.28f, 0.28f), Vector2.zero, Vector2.zero),
                    UITheme.Danger, () =>
                    {
                        var mgr = DepartmentManager.Instance;
                        if (mgr == null) return;
                        float fee = mgr.TerminateContract(id, false);
                        Toast(fee > 0 ? $"已解聘总监，遣散费 {fee:F1} 万。" : "无法解聘。");
                        Refresh();
                    }, 14);
                UIFactory.Button(_content, "Mut", "协商解约",
                    new UIFactory.Anchor(new Vector2(0.32f, 0.14f), new Vector2(0.60f, 0.28f), Vector2.zero, Vector2.zero),
                    UITheme.Warning, () =>
                    {
                        var mgr = DepartmentManager.Instance;
                        if (mgr == null) return;
                        float fee = mgr.TerminateContract(id, true);
                        Toast(fee > 0 ? $"协商解约成功，遣散费减半 {fee:F1} 万。" : "无法解约。");
                        Refresh();
                    }, 14);
            }
        }

        void RenderHistory(string name, string dept, string role, bool employed, PersonnelData p, GameState gs)
        {
            int y = gs != null ? gs.year : 1;
            L("HH", "履历", 0.00f, 0.64f, 0.40f, 0.72f, 14, UITheme.Accent, true);
            // 非在职者在这里（而不是页首副标题）标注意向岗位：
            // 副标题必须避免读成「他已是运营部总监」，而「他想去哪把椅子」是决策必需信息。
            L("H1", employed
                ? $"第 {y} 年　在职　{DepartmentCatalog.DisplayName(dept)} · {role}"
                : $"第 {y} 年　待聘　职业介绍所候选人（来源 {HrEconomics.SourceName(p.source)}"
                  + $" · 意向 {DepartmentCatalog.DisplayName(dept)} · {role}）",
                0.00f, 0.52f, 1.00f, 0.62f, 13, UITheme.TextPrimary);
            L("H2", "更早　职业经历尚未建档（后续接入人事事件）。", 0.00f, 0.42f, 1.00f, 0.52f, 13, UITheme.TextMuted);
            L("H3", $"{name} 的详细履历将随合同、任命与培训写入本页。", 0.00f, 0.32f, 1.00f, 0.42f, 13, UITheme.TextMuted);
        }

        /// <summary>
        /// 「资质」页签（仅非在职候选人有）：持证情况 + 到期提示，**只读**。
        /// 「培训」页签里的「在办培训 / 可考取证照 / 安排培训」都属于在编员工口径
        /// （培训项目按工号入册、考证占用培训中心产能），对候选人显示只会误导，故不出现。
        /// </summary>
        void RenderCerts(PersonnelData p, GameState gs)
        {
            if (p == null || gs == null)
            {
                L("CNnone", "游戏未就绪。", 0.00f, 0.60f, 1.00f, 0.70f, 13, UITheme.TextMuted);
                return;
            }
            var now = gs.SimUtc;
            L("CH", "持证情况", 0.00f, 0.64f, 0.50f, 0.72f, 14, UITheme.Accent, true);

            var sb = new System.Text.StringBuilder();
            int n = 0;
            if (p.certs != null)
            {
                for (int i = 0; i < p.certs.Count; i++)
                {
                    var h = p.certs[i];
                    var d = CertificationCatalog.Find(h != null ? h.id : null);
                    if (d == null) continue;
                    n++;
                    sb.AppendLine("  " + d.name + "　" + ExpiryText(h, now));
                }
            }
            if (n == 0) sb.Append("  暂无持证记录。");
            L("Clist", sb.ToString().TrimEnd(), 0.00f, 0.40f, 0.60f, 0.63f, 13, UITheme.TextPrimary);

            L("Ctip", "候选人尚未入职：可考取证照与在办培训在入职后于「培训」页办理。",
                0.00f, 0.28f, 1.00f, 0.38f, 12, UITheme.TextMuted);
        }

        static Color RateColor(int v)
        {
            if (v >= 16) return UITheme.Success;
            if (v >= 12) return UITheme.Accent;
            if (v <= 6) return UITheme.Danger;
            return UITheme.TextPrimary;
        }

        void Toast(string m)
        {
            if (_toast != null) _toast.text = m ?? "";
        }

        TextMeshProUGUI L(string name, string text, float x0, float y0, float x1, float y1,
            int size, Color color, bool bold = false)
        {
            return UIFactory.Label(_content, name, text, size, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(x0, y0), new Vector2(x1, y1), Vector2.zero, Vector2.zero),
                color, bold ? FontStyles.Bold : FontStyles.Normal);
        }

        void B(string name, string text, float x0, float y0, float x1, float y1,
            Color color, UnityEngine.Events.UnityAction act, int size = 12)
        {
            UIFactory.Button(_content, name, text,
                new UIFactory.Anchor(new Vector2(x0, y0), new Vector2(x1, y1), Vector2.zero, Vector2.zero),
                color, act, size);
        }
    }
}
