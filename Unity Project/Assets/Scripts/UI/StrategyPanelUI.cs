using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI.Navigation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>底栏「愿景」：年度目标 / 董事会 / 战略研究 / 历史回放。</summary>
    public class StrategyPanelUI : MonoBehaviour
    {
        GameObject _homeRoot;
        Transform _body;
        TextMeshProUGUI _toast;
        readonly Button[] _tabs = new Button[4];
        int _tab;

        public static StrategyPanelUI Build(Transform parent)
        {
            var panel = UIFactory.Panel(parent, "Panel_Strategy", UIFactory.Fill, UITheme.Panel);
            UIFactory.AddBorder(panel, UITheme.Border, new Vector2(1f, -1f));
            var ui = panel.AddComponent<StrategyPanelUI>();
            ui.BuildContent(panel.transform);
            return ui;
        }

        public static void OpenStrategyPage(string pageId)
        {
            var nav = FindFirstObjectByType<PrefabNavIntegration>();
            if (nav != null) { nav.OpenSubPage(pageId); return; }
            var router = NavigationRouter.Instance;
            if (router != null) router.Navigate(pageId, replaceCurrent: false);
            else Debug.LogWarning("[STG] NavigationRouter 未就绪：" + pageId);
        }

        public void OnOpened()
        {
            if (_homeRoot != null) _homeRoot.SetActive(true);
            SetTab(_tab);
        }

        public void ShowBoardTab() => SetTab(1);
        public void ShowTargetsTab() => SetTab(0);

        public void Refresh() => Rebuild();

        void BuildContent(Transform panel)
        {
            UIFactory.Label(panel, "Title", "愿景", 20, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.94f), new Vector2(0.12f, 0.995f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            var bar = UIFactory.Panel(panel, "VisionTabs",
                new UIFactory.Anchor(new Vector2(0.13f, 0.935f), new Vector2(0.98f, 0.995f), Vector2.zero, Vector2.zero),
                new Color(0.04f, 0.10f, 0.18f, 0.55f));
            bar.GetComponent<Image>().raycastTarget = false;

            _homeRoot = UIFactory.Panel(panel, "StgHome",
                new UIFactory.Anchor(new Vector2(0.02f, 0.08f), new Vector2(0.98f, 0.93f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            UIFactory.AddBorder(_homeRoot, UITheme.Border, new Vector2(1f, -1f));
            _body = _homeRoot.transform;

            _toast = UIFactory.Label(panel, "Toast", "", 13, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.01f), new Vector2(0.97f, 0.07f), Vector2.zero, Vector2.zero),
                UITheme.Warning);

            string[] labels = { "年度目标", "董事会", "战略研究", "历史回放" };
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                float x0 = i / 4f;
                _tabs[i] = UIFactory.Button(bar.transform, "T" + i, labels[i],
                    new UIFactory.Anchor(new Vector2(x0 + 0.01f, 0.12f), new Vector2(x0 + 0.24f, 0.88f),
                        Vector2.zero, Vector2.zero),
                    UITheme.NavIdle, () => SetTab(idx), 13);
            }
            SetTab(0);
        }

        void SetTab(int idx)
        {
            _tab = Mathf.Clamp(idx, 0, 3);
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
                Object.Destroy(c.gameObject);
            }
            if (_tab == 0) FillTargets();
            else if (_tab == 1) FillBoard();
            else if (_tab == 2) FillStrategy();
            else FillHistory();
        }

        void FillTargets()
        {
            var gs = GameState.Instance;
            if (BoardManager.Instance != null)
                BoardManager.Instance.RefreshTargetProgress();

            int done = 0, total = gs?.currentTargets != null ? gs.currentTargets.Count : 0;
            if (gs?.currentTargets != null)
                for (int i = 0; i < gs.currentTargets.Count; i++)
                    if (gs.currentTargets[i] != null && gs.currentTargets[i].completed) done++;
            float rate = total > 0 ? done * 100f / total : 0f;
            float line = BoardEconomics.ReappointmentThreshold(gs != null ? gs.difficulty : DifficultyMode.Normal);

            Head("年度目标确认",
                gs == null ? "游戏未就绪" :
                $"第 {gs.targetsFiscalYear} 财年 · {(gs.AnnualGoalsConfirmed ? "已确认" : "待确认")} · 完成 {done}/{total}（{rate:F0}%）· 续聘线 {line:F0}% · 董事会满意度 {gs.boardSatisfaction:F0}");

            if (gs == null || gs.currentTargets == null || gs.currentTargets.Count == 0)
                Note("尚无考核清单。开局或新年后由董事会下达。");
            else
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < gs.currentTargets.Count; i++)
                {
                    var t = gs.currentTargets[i];
                    if (t == null) continue;
                    sb.AppendLine($"{(t.completed ? "✓" : "○")} {t.ProgressLabel}  {(t.isGuarantee ? "[保底]" : "")}");
                }
                Note(sb.ToString().TrimEnd());
            }

            Btn("Confirm", gs != null && gs.AnnualGoalsConfirmed ? "本财年已确认" : "确认本财年目标", 0.04f, UITheme.Accent, () =>
            {
                if (gs == null) { Toast("游戏未就绪"); return; }
                if (gs.AnnualGoalsConfirmed) { Toast("本财年目标已确认。"); return; }
                if (BoardManager.Instance == null || !BoardManager.Instance.ConfirmAnnualGoals())
                {
                    Toast("尚无考核清单。");
                    return;
                }
                Toast("本财年目标已确认，计入决策日志。");
                Rebuild();
            });
            Btn("Settle", "预览财年结算", 0.36f, UITheme.PanelDeep,
                () => OpenStrategyPage("n26_财年结算_续聘_解雇"));
            Btn("Full", "打开完整董事会", 0.68f, UITheme.PanelDeep,
                () => OpenStrategyPage("n20_董事会"));
        }

        void FillBoard()
        {
            var gs = GameState.Instance;
            Head("董事会",
                gs == null ? "游戏未就绪" :
                $"全体满意度 {gs.boardSatisfaction:F0} · 合同至第 {gs.CeoContractEndYear} 财年 · 剩余任期 {gs.CeoContractYearsRemaining} 年");

            var members = DataBase.Instance != null ? DataBase.Instance.BoardMembers : null;
            var sb = new System.Text.StringBuilder();
            if (members == null || members.Count == 0)
                sb.Append("暂无董事数据。");
            else
            {
                var faction = new Dictionary<string, int>();
                for (int i = 0; i < members.Count; i++)
                {
                    var m = members[i];
                    if (m == null) continue;
                    float sat = 50f;
                    if (BoardManager.Instance != null &&
                        BoardManager.Instance.memberSatisfaction.TryGetValue(m.id, out float v))
                        sat = v;
                    sb.AppendLine($"{m.fullName} · {m.faction} · 满意度 {sat:F0}");
                    string f = m.faction ?? "其他";
                    faction.TryGetValue(f, out int c);
                    faction[f] = c + 1;
                }
                sb.Append("派系：");
                foreach (var kv in faction)
                    sb.Append($" {kv.Key} {kv.Value}席");
            }
            Note(sb.ToString().TrimEnd());

            Btn("Budget", "预算投票", 0.04f, UITheme.PanelDeep,
                () => OpenStrategyPage("n22_年度预算编制_审批"));
            Btn("Year", "财年结算 / 续聘", 0.36f, UITheme.Accent,
                () => OpenStrategyPage("n26_财年结算_续聘_解雇"));
            Btn("Appt", "任命总监", 0.68f, UITheme.PanelDeep,
                () => OpenStrategyPage("n23_部门负责人任命"));
        }

        void FillStrategy()
        {
            var gs = GameState.Instance;
            var stg = StrategyManager.EnsureState(gs);
            int routes = gs?.activeRoutes != null ? gs.activeRoutes.Count : 0;
            int slots = stg?.slots != null ? stg.slots.Count : 0;
            int inv = stg?.investments != null ? stg.investments.Count : 0;
            int partners = stg?.alliance != null ? stg.alliance.codeSharePartners : 0;
            float c = stg != null ? stg.strategicConsistency : 0f;
            string band = c > 80 ? "绿" : c >= 60 ? "黄" : "红";
            float sur = stg != null ? StrategyEconomics.GeoRiskSurcharge(stg.geoRiskIndex) : 0f;

            Head("战略研究与预案",
                $"航线 {routes} · 合作 {partners} · 时刻槽 {slots} · 投资 {inv} · 一致性 {c:F0}（{band}）· 地缘风险 {stg?.geoRiskIndex ?? 0}（航路费 +{sur * 100f:F0}%）" +
                " · 入盟/航权/时刻槽提案在收件箱批准，下季度生效；持仓与办理进度见「航线」模块的「航权与时刻槽」页签");

            Entry(0, "战略研究与预案", "n80_战略研究与预案");
            Entry(1, "联盟与战略合作", "n78_联盟与战略合作");
            Entry(2, "战略投资与并购", "n79_战略投资与并购");
            Entry(3, "机队与运力规划", "n76_机队与运力规划");
            Entry(4, "临空商业", "n108_临空商业");
            Entry(5, "新航线 RPM", "n35_新航线_RPM_评估台");
        }

        void FillHistory()
        {
            var gs = GameState.Instance;
            int n = gs?.decisionLog != null ? gs.decisionLog.Count : 0;
            Head("历史回放", n > 0 ? $"决策日志 {n} 条（最近在下）" : "尚无决策记录。");

            var sb = new System.Text.StringBuilder();
            if (gs?.decisionLog != null && gs.decisionLog.Count > 0)
            {
                int from = Mathf.Max(0, gs.decisionLog.Count - 10);
                for (int i = gs.decisionLog.Count - 1; i >= from; i--)
                    sb.AppendLine("· " + gs.decisionLog[i]);
            }
            else sb.Append("推进季度、开航线、续约后会写入这里。");
            Note(sb.ToString().TrimEnd());

            Btn("Open", "打开完整历史回放", 0.04f, UITheme.Accent,
                () => OpenStrategyPage("n38_历史回放"));
            Btn("Board", "去董事会", 0.36f, UITheme.PanelDeep, () => SetTab(1));
        }

        void Head(string title, string sub)
        {
            UIFactory.Label(_body, "H", title, 16, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.86f), new Vector2(0.97f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.Accent, FontStyles.Bold);
            var v = UIFactory.Label(_body, "S", sub ?? "—", 13, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.72f), new Vector2(0.97f, 0.86f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            v.textWrappingMode = TextWrappingModes.Normal;
        }

        void Note(string text)
        {
            var l = UIFactory.Label(_body, "N", text ?? "", 13, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.28f), new Vector2(0.97f, 0.70f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            l.textWrappingMode = TextWrappingModes.Normal;
        }

        void Btn(string name, string label, float x0, Color color, UnityEngine.Events.UnityAction onClick)
        {
            UIFactory.Button(_body, name, label,
                new UIFactory.Anchor(new Vector2(x0, 0.06f), new Vector2(x0 + 0.28f, 0.20f), Vector2.zero, Vector2.zero),
                color, onClick, 13);
        }

        void Entry(int idx, string label, string pageId)
        {
            int row = idx / 3;
            int col = idx % 3;
            float x0 = 0.03f + col * 0.32f;
            float y0 = 0.50f - row * 0.16f;
            UIFactory.Button(_body, "E" + idx, label,
                new UIFactory.Anchor(new Vector2(x0, y0), new Vector2(x0 + 0.30f, y0 + 0.13f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => OpenStrategyPage(pageId), 12);
        }

        void Toast(string msg)
        {
            if (_toast != null) _toast.text = msg ?? "";
        }
    }
}
