using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>底栏「培训」：人员 / 机务资质 / 安全文化 / 适航总览 / 安全仪表盘。</summary>
    public class TrainingHubPanel : BasePanel
    {
        Transform _body;
        TextMeshProUGUI _toast;
        readonly Button[] _tabs = new Button[5];
        int _tab;

        public static TrainingHubPanel Build()
        {
            var root = UIFactory.Panel(null, "n87_培训与发展", UIFactory.Fill, UITheme.Panel);
            UIFactory.AddBorder(root, UITheme.Border, new Vector2(1f, -1f));
            var cg = root.AddComponent<CanvasGroup>();

            var title = UIFactory.Label(root.transform, "Title", "培训", 20,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.94f), new Vector2(0.14f, 0.995f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            var close = UIFactory.Button(root.transform, "Close", "×",
                new UIFactory.Anchor(new Vector2(0.92f, 0.94f), new Vector2(0.98f, 0.995f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 20);
            close.gameObject.SetActive(false);

            var bar = UIFactory.Panel(root.transform, "TrainTabs",
                new UIFactory.Anchor(new Vector2(0.12f, 0.935f), new Vector2(0.98f, 0.995f), Vector2.zero, Vector2.zero),
                new Color(0.04f, 0.10f, 0.18f, 0.55f));
            bar.GetComponent<Image>().raycastTarget = false;

            var body = UIFactory.Panel(root.transform, "Body",
                new UIFactory.Anchor(new Vector2(0.02f, 0.08f), new Vector2(0.98f, 0.92f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            UIFactory.AddBorder(body, UITheme.Border, new Vector2(1f, -1f));

            var toast = UIFactory.Label(root.transform, "Toast", "", 13,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.01f), new Vector2(0.97f, 0.07f), Vector2.zero, Vector2.zero),
                UITheme.Warning);

            var panel = root.AddComponent<TrainingHubPanel>();
            panel._body = body.transform;
            panel._toast = toast;
            panel.AssignRefs(root.GetComponent<RectTransform>(), title, close, cg);
            panel.BindPage("n87_培训与发展", "培训");

            string[] labels = { "人员培训", "维修资质", "安全文化", "适航总览", "安全仪表盘" };
            for (int i = 0; i < 5; i++)
            {
                int idx = i;
                float x0 = i / 5f;
                var btn = UIFactory.Button(bar.transform, "T" + i, labels[i],
                    new UIFactory.Anchor(new Vector2(x0 + 0.005f, 0.12f), new Vector2(x0 + 0.195f, 0.88f),
                        Vector2.zero, Vector2.zero),
                    UITheme.NavIdle, () => panel.SetTab(idx), 12);
                panel._tabs[i] = btn;
            }

            panel.SetTab(0);
            return panel;
        }

        public override void Show()
        {
            base.Show();
            SetTab(_tab);
        }

        void SetTab(int idx)
        {
            _tab = Mathf.Clamp(idx, 0, 4);
            for (int i = 0; i < _tabs.Length; i++)
            {
                if (_tabs[i] == null) continue;
                var img = _tabs[i].GetComponent<Image>();
                if (img != null) img.color = i == _tab ? UITheme.NavActive : UITheme.NavIdle;
            }
            Rebuild();
        }

        void Rebuild()
        {
            if (_body == null) return;
            for (int i = _body.childCount - 1; i >= 0; i--)
            {
                var c = _body.GetChild(i);
                if (c.name == "Border") continue;
                Destroy(c.gameObject);
            }

            if (_tab == 0) FillPeople();
            else if (_tab == 1) FillMaint();
            else if (_tab == 2) FillSafety();
            else if (_tab == 3) FillAirworthiness();
            else FillSafetyDash();
        }

        void FillPeople()
        {
            var gs = GameState.Instance;
            var hr = HrManager.EnsureState(gs);
            PersonnelGenerator.EnsurePool(gs);
            HrManager.EnsurePoolCerts(gs);
            Row("H", "人员培训 · 人才战斗力",
                hr == null ? "游戏未就绪" :
                $"年度培训预算 {hr.trainingBudgetWan:F0} 万 · ROI {hr.trainingRoi:F2} · 中心：{HrManager.CenterName(hr.trainingCenterLevel)}",
                0.82f, UITheme.Accent);

            Row("C", "培训中心 · 等级 / 造价 / 工期", CenterLine(gs, hr), 0.62f, UITheme.Accent);
            Note("R", RosterText(gs, hr), 0.18f);

            Act("Add", "启动高潜管理培训", 0.04f, 0.05f, UITheme.Accent, () =>
            {
                string msg = "启动失败";
                if (HrManager.Instance != null)
                    HrManager.Instance.AddTraining("高潜管理培训", 12, 180f, out msg, "people");
                Toast(msg);
                Rebuild();
            });
            Act("Center", CenterButtonLabel(hr), 0.36f, 0.05f, UITheme.PanelDeep, () =>
            {
                string msg = "升级失败";
                var h = HrManager.EnsureState(GameState.Instance);
                if (HrManager.Instance != null && h != null)
                    HrManager.Instance.StartTrainingCenterUpgrade(h.trainingCenterLevel + 1, out msg);
                Toast(msg);
                Rebuild();
            });
            Act("Detail", "打开人才档案", 0.68f, 0.05f, UITheme.PanelDeep,
                () => HrPanelUI.OpenHrPage("n91_人才档案与继任"));
        }

        /// <summary>培训中心现状 / 在建 / 下一级的造价与工期（此前只有一个名字，看不到钱和时间）。</summary>
        static string CenterLine(GameState gs, HrState hr)
        {
            if (gs == null || hr == null) return "游戏未就绪";
            var sb = new System.Text.StringBuilder();
            int lv = hr.trainingCenterLevel;
            sb.Append($"当前 {lv} 级「{HrManager.CenterName(lv)}」");
            sb.Append($" · 每季进度 {HrEconomics.TrainingProgressDelta(lv):F2} · 结业效率 ×{1f + lv * 0.12f:F2}");
            sb.Append($" · 培训费折扣 ×{HrEconomics.TrainingCostFactor(lv):F2}");

            if (hr.trainingCenterBuildLevel > 0)
            {
                double days = System.Math.Max(0.0,
                    (new System.DateTime(hr.trainingCenterBuildDoneTicks, System.DateTimeKind.Utc) - gs.SimUtc).TotalDays);
                sb.Append($"\n在建：「{HrManager.CenterName(hr.trainingCenterBuildLevel)}」· 约 {days:F0} 天后启用（施工期沿用当前等级）");
            }
            else if (lv >= HrEconomics.TrainingCenterMaxLevel)
            {
                sb.Append("\n已达最高等级（航司大学）· 证照考取全部开放");
            }
            else
            {
                int next = lv + 1;
                sb.Append($"\n下一级：「{HrManager.CenterName(next)}」· {HrEconomics.TrainingCenterCostWan(next):F0} 万 · 工期 {HrEconomics.TrainingCenterBuildDays(next)} 天");
                sb.Append($" · 解锁 {UnlockText(next)}");
            }
            return sb.ToString();
        }

        static string UnlockText(int level) => level switch
        {
            1 => "培训费 9 折、基础证照补办与复检（机务执照 / 体检合格证 / 乘务安全记录）",
            2 => "培训费 8 折、进阶证照（飞行员执照 / 机型授权 / 放行授权）",
            _ => "培训费 7 折、国际运行资质（国际航线准入的第二条路径）"
        };

        static string CenterButtonLabel(HrState hr)
        {
            if (hr == null) return "升级培训中心";
            if (hr.trainingCenterBuildLevel > 0) return "施工中…";
            if (hr.trainingCenterLevel >= HrEconomics.TrainingCenterMaxLevel) return "已达最高级";
            return $"升级至 {HrManager.CenterName(hr.trainingCenterLevel + 1)}";
        }

        /// <summary>
        /// 在培人员进度列表。此前只有「12 人 · 完成率 40%」这种群体计数，
        /// 看不到名单——因为 TrainingProgram 根本没有成员字段（现已补 memberIds）。
        /// </summary>
        static string RosterText(GameState gs, HrState hr)
        {
            if (gs == null || hr?.trainings == null) return "游戏未就绪";
            int active = 0;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < hr.trainings.Count; i++)
            {
                var t = hr.trainings[i];
                if (t == null || t.graduated) continue;
                active++;
                int on = TrainingGrowthLogic.ActiveRosterCount(gs, t);
                string prog = t.durationQuarters > 0
                    ? $"{t.completionRate * 100f:F0}%（约 {t.durationQuarters} 季）"
                    : $"{t.completionRate * 100f:F0}%";
                sb.AppendLine($"· {t.name} · {prog} · 在册 {on} 人");
                sb.AppendLine("   " + MemberNames(gs, t));
            }

            if (active == 0)
                sb.Append("暂无在办培训项目。点下方「启动高潜管理培训」，或在员工档案的「培训」页为个人安排培训 / 考证。");

            int expiring = CertificationLogic.ExpiringCount(gs.staffPool, gs.SimUtc, 90);
            sb.AppendLine();
            sb.Append($"在职持证 {HolderTotal(gs)} 本 · 90 天内到期 {expiring} 本 · 证照考取需 1 级以上（国际运行资质需 3 级）");
            if (!string.IsNullOrEmpty(hr.lastTrainingNote))
                sb.Append("\n最近结业：").Append(hr.lastTrainingNote);
            return sb.ToString().TrimEnd();
        }

        static int HolderTotal(GameState gs)
        {
            if (gs?.staffPool == null) return 0;
            int n = 0;
            for (int i = 0; i < gs.staffPool.Count; i++)
            {
                var p = gs.staffPool[i];
                if (p == null || p.onMarket || p.certs == null) continue;
                n += p.certs.Count;
            }
            return n;
        }

        static string MemberNames(GameState gs, TrainingProgram t)
        {
            if (gs == null || t == null) return "";
            if (t.memberIds == null || t.memberIds.Count == 0)
                return "（未登记名单 · 结业时按潜力补足）";
            var sb = new System.Text.StringBuilder();
            int shown = 0;
            for (int i = 0; i < t.memberIds.Count; i++)
            {
                var p = PersonnelGenerator.Find(gs, t.memberIds[i]);
                if (p == null) continue;
                if (shown > 0) sb.Append('、');
                sb.Append(p.fullName).Append('（').Append(p.rank).Append(" · 潜力 ")
                  .Append((p.potential * 100f).ToString("F0")).Append("%）");
                shown++;
                if (shown >= 6) break;
            }
            if (shown == 0) return "（在册人员已全部离岗）";
            if (t.memberIds.Count > shown) sb.Append($" 等 {t.memberIds.Count} 人");
            return sb.ToString();
        }

        void FillMaint()
        {
            var gs = GameState.Instance;
            var mnt = MaintenanceManager.EnsureState(gs);
            Row("H", "维修培训与资质 · 机务人才培养",
                mnt == null ? "游戏未就绪" :
                $"持证工程师 {mnt.engineerCount} 人 · 在培 {mnt.trainingCount} 人 · 可用率 {mnt.fleetAvailability:F1}%",
                0.82f, UITheme.Accent);
            Note("N", "机型改装：自建中心 / 外包 / OEM。职业路径：机械师 → 工程师 → 放行工程师。", 0.58f);
            Act("Train", "发起机务培训（12 人 / 80 万）", 0.04f, 0.08f, UITheme.Accent, () =>
            {
                string msg = "启动失败";
                if (MaintenanceManager.Instance != null)
                    MaintenanceManager.Instance.StartTypeRatingBatch(12, 80f, out msg);
                Toast(msg);
                Rebuild();
            });
            Act("Open", "打开机务培训详情", 0.36f, 0.40f, UITheme.PanelDeep,
                () => MaintenancePanelUI.OpenMaintenancePage("n85_维修培训与资质"));
            Act("Hire", "招机务工程师 →", 0.68f, 0.72f, UITheme.PanelDeep, () =>
            {
                // 预置岗位 + 展开过滤面板：招聘发起已并入 n112 职业介绍所（不再有 n99 弹窗）。
                // 岗位与「已选目标」必须由 RequestRecruit 一起写死，否则本页按钮会发成
                // 「飞行员」招聘、或面板仍判「未选定目标」而禁用呈请，等于断链。
                RecruitmentOfficePanel.RequestRecruit("机务工程师");
                HrPanelUI.OpenHrPage("n112_职业介绍所");
            });
        }

        void FillSafety()
        {
            var gs = GameState.Instance;
            var saf = SafetyManager.EnsureState(gs);
            char band = saf != null ? SafetyEconomics.RatingBand(saf.ratingScore) : '-';
            Row("H", "安全文化与培训 · 安全意识",
                saf == null ? "游戏未就绪" :
                $"{SafetyManager.CultureName(saf.cultureType)} · 文化指数 {saf.cultureIndex:F0} · 评级 {band}（{saf.ratingScore:F0}）",
                0.82f, UITheme.Accent);
            Note("N", "惩罚型短期合规高、隐瞒风险高；鼓励报告型透明度高、真实风险低。追加培训预算会小幅提升安全评级。", 0.58f);
            Act("Cult", "切换鼓励报告型", 0.04f, 0.08f, UITheme.Accent, () =>
            {
                string msg = "切换失败";
                if (SafetyManager.Instance != null) SafetyManager.Instance.SetCultureType(1, out msg);
                Toast(msg);
                Rebuild();
            });
            Act("Bud", "追加安全培训预算 100 万", 0.36f, 0.40f, UITheme.PanelDeep, () =>
            {
                string msg = "追加失败";
                if (SafetyManager.Instance != null) SafetyManager.Instance.AllocateSafetyBudget(100f, out msg);
                Toast(msg);
                Rebuild();
            });
            Act("Open", "打开安全文化详情", 0.68f, 0.72f, UITheme.PanelDeep,
                () => SafetyPanelUI.OpenSafetyPage("n96_安全文化与培训"));
        }

        void FillAirworthiness()
        {
            var gs = GameState.Instance;
            var mnt = MaintenanceManager.EnsureState(gs);
            string strat = mnt != null ? MaintenanceManager.StrategyName(mnt.strategy) : "—";
            float exec = mnt != null && mnt.budgetWan > 0 ? mnt.budgetUsedWan / mnt.budgetWan * 100f : 0f;
            Row("H", "适航总览 · 机队健康度",
                mnt == null ? "游戏未就绪" :
                $"可用率 {mnt.fleetAvailability:F1}% · 停飞 {mnt.groundedCount} 架 · 差错率 {mnt.errorRate:F1}% · {strat}（预算执行 {exec:F0}%）",
                0.82f, UITheme.Accent);
            Note("N", "定检、工程、航材与适航质量从这里进入详情。维修策略可在维护页调整。", 0.58f);
            Act("A1", "航线与定检", 0.04f, 0.28f, UITheme.PanelDeep,
                () => MaintenancePanelUI.OpenMaintenancePage("n81_航线与定检维修"));
            Act("A2", "工程技术", 0.36f, 0.28f, UITheme.PanelDeep,
                () => MaintenancePanelUI.OpenMaintenancePage("n82_工程技术与计划"));
            Act("A3", "航材与供应链", 0.68f, 0.28f, UITheme.PanelDeep,
                () => MaintenancePanelUI.OpenMaintenancePage("n83_航材与供应链"));
            Act("A4", "适航质量", 0.04f, 0.08f, UITheme.Accent,
                () => MaintenancePanelUI.OpenMaintenancePage("n84_安全质量与适航"));
            Act("A5", "全面预防策略", 0.36f, 0.08f, UITheme.PanelDeep, () =>
            {
                string msg = "切换失败";
                if (MaintenanceManager.Instance != null)
                    MaintenanceManager.Instance.SetStrategy(0, out msg);
                Toast(msg);
                Rebuild();
            });
            Act("A6", "打开维护页", 0.68f, 0.08f, UITheme.PanelDeep, () =>
            {
                var nav = FindFirstObjectByType<PrefabNavIntegration>();
                if (nav != null) nav.SelectTab("n17_机务工程部");
            });
        }

        void FillSafetyDash()
        {
            var gs = GameState.Instance;
            var saf = SafetyManager.EnsureState(gs);
            char band = saf != null ? SafetyEconomics.RatingBand(saf.ratingScore) : '-';
            bool takeover = saf != null && SafetyEconomics.RegulatorTakeoverRisk(saf.ratingScore);
            Row("H", "安全仪表盘 · SMS / 监察 / 局方",
                saf == null ? "游戏未就绪" :
                $"评级 {band}（{saf.ratingScore:F0}）· 零重大事故 {saf.zeroAccidentDays} 天 · 审计通过 {saf.auditPassRate:F0}% · {(takeover ? "局方接管风险" : "考核正常")}",
                0.82f, UITheme.Accent);
            Note("N", $"文化：{(saf != null ? SafetyManager.CultureName(saf.cultureType) : "—")} · 隐患整改 {(saf != null ? saf.rectificationRate : 0f):F0}%", 0.58f);
            Act("S1", "SMS与风险管理", 0.04f, 0.28f, UITheme.PanelDeep,
                () => SafetyPanelUI.OpenSafetyPage("n92_SMS与风险管理"));
            Act("S2", "安全监察与审核", 0.36f, 0.28f, UITheme.PanelDeep,
                () => SafetyPanelUI.OpenSafetyPage("n93_安全监察与审核"));
            Act("S3", "事件调查与分析", 0.68f, 0.28f, UITheme.PanelDeep,
                () => SafetyPanelUI.OpenSafetyPage("n94_事件调查与分析"));
            Act("S4", "局方与法规", 0.04f, 0.08f, UITheme.PanelDeep,
                () => SafetyPanelUI.OpenSafetyPage("n95_局方与法规事务"));
            Act("S5", "检查应对", 0.36f, 0.08f, UITheme.Accent,
                () => SafetyPanelUI.OpenSafetyPage("n100_检查应对弹窗"));
            Act("S6", "打开维护·安全", 0.68f, 0.08f, UITheme.PanelDeep, () =>
            {
                var nav = FindFirstObjectByType<PrefabNavIntegration>();
                if (nav != null) nav.SelectTab("n19_安全质量部");
            });
        }

        void Row(string name, string title, string value, float y, Color titleColor)
        {
            UIFactory.Label(_body, name + "T", title, 16, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, y), new Vector2(0.97f, y + 0.10f), Vector2.zero, Vector2.zero),
                titleColor, FontStyles.Bold);
            UIFactory.Label(_body, name + "V", value ?? "—", 14, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.03f, y - 0.12f), new Vector2(0.97f, y), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
        }

        void Note(string name, string text, float y)
        {
            var l = UIFactory.Label(_body, name, text, 13, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.03f, y), new Vector2(0.97f, y + 0.18f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            l.textWrappingMode = TextWrappingModes.Normal;
        }

        void Act(string name, string label, float x0, float y0, Color color, UnityEngine.Events.UnityAction onClick)
        {
            UIFactory.Button(_body, name, label,
                new UIFactory.Anchor(new Vector2(x0, y0), new Vector2(x0 + 0.28f, y0 + 0.12f), Vector2.zero, Vector2.zero),
                color, onClick, 13);
        }

        void Toast(string msg)
        {
            if (_toast != null) _toast.text = msg ?? "";
        }
    }
}
