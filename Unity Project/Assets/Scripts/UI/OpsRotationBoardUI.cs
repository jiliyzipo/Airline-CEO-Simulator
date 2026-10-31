using System.Collections.Generic;
using System.Text;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>
    /// n36 航班环看板 —— **运营部编成结果的只读视图**（2026-10-30 重构）。
    ///
    /// ★报障原文：「这个航班环系统设计的极其糟糕，玩家既看不懂也无从下手，需要重新设计。
    ///   航班环的构建完全在后台进行，CEO 只在命令台下达指定，由运营部负责跟进和反馈结果。」
    ///
    /// 旧页让 CEO 按起飞顺序一段段点航段、再点「校验连班」，拼出来的东西只是两个航线 ID
    /// 加一句合格/不合格 —— 既不像排班，也没告诉他「这架飞机一天到底怎么飞」。
    /// 现在页面上一条可选航段都没有：
    ///
    /// · 顶部六步条：运营部每一步算出了什么、卡在哪一步（<see cref="OpsRotationBuilder.Plan.steps"/>）；
    /// · 中部甘特时间轴：每架机一行 24 小时，去程块 + 过站灰条 + 回程块，空档一眼可见；
    /// · 底部运力缺口：排不进的航段按「缺机/缺槽/缺时间/过站不足」逐条归因；
    /// · 唯一的按钮「请求运营部重编」—— 想改就下指令，不是自己动手拼。
    ///
    /// 数据每次 Refresh 由 <see cref="OpsRotationBuilder.BuildFromGameState"/> 现算（纯函数、确定性），
    /// 因此看板与收件箱呈报来自同一套六步口径，不会出现「页面说排得进、邮件说排不进」。
    /// </summary>
    public class OpsRotationBoardUI : MonoBehaviour
    {
        const int DayMinutes = 24 * 60;
        /// <summary>甘特轨道左沿：左边 21.5% 留给机号与当日小计。</summary>
        const float TrackLeft = 0.215f;
        const float TrackWidth = 0.775f;

        Transform _stepRoot;
        Transform _rowsRoot;
        TextMeshProUGUI _headline;
        TextMeshProUGUI _gapText;

        readonly List<GameObject> _stepCards = new List<GameObject>();
        readonly List<GameObject> _rows = new List<GameObject>();

        OpsRotationBuilder.Plan _plan;
        string _notice;

        static readonly Color[] LegColors =
        {
            new Color(0.16f, 0.55f, 0.92f),
            new Color(0.13f, 0.72f, 0.62f),
            new Color(0.62f, 0.44f, 0.92f),
            new Color(0.92f, 0.60f, 0.20f),
            new Color(0.22f, 0.68f, 0.90f),
            new Color(0.85f, 0.38f, 0.48f),
            new Color(0.45f, 0.78f, 0.32f)
        };

        public static OpsRotationBoardUI Ensure(Transform card)
        {
            if (card == null) return null;
            var existing = card.GetComponentInChildren<OpsRotationBoardUI>(true);
            if (existing != null)
            {
                existing.gameObject.SetActive(true);
                if (existing._rowsRoot == null)
                {
                    // 旧 Prefab 烘焙组件：运行时字段未初始化，清掉旧子节点后重建
                    for (int i = existing.transform.childCount - 1; i >= 0; i--)
                        Destroy(existing.transform.GetChild(i).gameObject);
                    existing.Build(existing.transform);
                }
                existing.Refresh();
                return existing;
            }

            var root = UIFactory.Panel(card, "RotationBoard",
                new UIFactory.Anchor(new Vector2(0.04f, 0.11f), new Vector2(0.96f, 0.885f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0.01f), rounded: false);
            var img = root.GetComponent<Image>();
            if (img != null) img.raycastTarget = false;

            var ui = root.AddComponent<OpsRotationBoardUI>();
            ui.Build(root.transform);
            ui.Refresh();
            return ui;
        }

        void Build(Transform root)
        {
            // 顶栏：一句话结论 + 唯一可点的「请求重编」。页标题由 OpsSubpagePanel 提供，这里不重复。
            _headline = UIFactory.Label(root, "Headline", "", 12, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0f, 0.93f), new Vector2(0.74f, 1f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            _headline.textWrappingMode = TextWrappingModes.Normal;

            UIFactory.Button(root, "BtnReplan", "请求运营部重编",
                new UIFactory.Anchor(new Vector2(0.76f, 0.935f), new Vector2(1f, 0.995f), Vector2.zero, Vector2.zero),
                UITheme.Accent, RequestReplan, 11);

            // 左轨六步：竖排六格，空间够写两行结论（横排六列时每个才 130px，字挤成一团）。
            _stepRoot = UIFactory.Panel(root, "Steps",
                new UIFactory.Anchor(new Vector2(0f, 0.115f), new Vector2(0.335f, 0.92f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0)).transform;
            var stepImg = _stepRoot.GetComponent<Image>();
            if (stepImg != null) stepImg.raycastTarget = false;

            // 甘特区：一行一架飞机 / 一段缺口。右侧常驻滚动条，机队再多也知道下面还有。
            var content = UIFactory.VerticalScrollArea(root, "TimelineList",
                new UIFactory.Anchor(new Vector2(0.345f, 0.115f), new Vector2(1f, 0.92f), Vector2.zero, Vector2.zero));
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(0, 4, 4, 4);
            layout.spacing = 6;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _rowsRoot = content;

            _gapText = UIFactory.Label(root, "Gaps", "", 11, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(1f, 0.105f), Vector2.zero, Vector2.zero),
                UITheme.Warning);
            _gapText.textWrappingMode = TextWrappingModes.Normal;
            _gapText.verticalAlignment = VerticalAlignmentOptions.Top;
        }

        public void Refresh()
        {
            var gs = GameState.Instance;
            _plan = OpsRotationBuilder.BuildFromGameState(gs);

            if (_headline != null)
            {
                var pending = gs != null ? InboxManager.FindOpenRotationPlan(gs) : null;
                string head = !string.IsNullOrEmpty(_notice)
                    ? _notice
                    : OpsRotationBuilder.Headline(_plan);
                string tail = pending != null
                    ? "　·　有一封待批方案在收件箱"
                    : _plan != null && _plan.HasPlan ? "　·　批准后才落账" : "";
                _headline.text = head + tail;
            }

            RebuildSteps();
            RebuildRows();

            if (_gapText != null)
            {
                if (_plan == null || !_plan.HasGap)
                {
                    _gapText.color = UITheme.Success;
                    _gapText.text = "运力缺口：无。待飞航段全部入环。"
                        + "\n想改排法不必自己排：在命令台说「把深圳基地的飞机编满」，运营部会按新条件重算。";
                }
                else
                {
                    _gapText.color = UITheme.Warning;
                    var sb = new StringBuilder();
                    sb.Append("运力缺口 ").Append(_plan.gaps.Count).Append(" 段（缺口留着，下一轮再补）：");
                    int show = Mathf.Min(3, _plan.gaps.Count);
                    for (int i = 0; i < show; i++)
                    {
                        if (i > 0) sb.Append("　｜　");
                        sb.Append('[').Append(_plan.gaps[i].kind).Append("] ")
                          .Append(_plan.gaps[i].line).Append('：')
                          .Append(_plan.gaps[i].reason);
                    }
                    if (_plan.gaps.Count > show) sb.Append($"　…… 其余 {_plan.gaps.Count - show} 段见收件箱");
                    _gapText.text = sb.ToString();
                }
            }

            _notice = null;
        }

        void RebuildSteps()
        {
            for (int i = 0; i < _stepCards.Count; i++)
                if (_stepCards[i] != null) Destroy(_stepCards[i]);
            _stepCards.Clear();
            if (_stepRoot == null || _plan == null) return;

            int n = Mathf.Max(1, _plan.steps.Count);
            float slot = 1f / n;
            for (int i = 0; i < _plan.steps.Count; i++)
            {
                var step = _plan.steps[i];
                // 自上而下排：①在最上面，与邮件正文的六步顺序一致。
                float y1 = 1f - i * slot;
                float y0 = y1 - slot + 0.008f;
                var card = UIFactory.Panel(_stepRoot, "Step_" + i,
                    new UIFactory.Anchor(new Vector2(0f, y0), new Vector2(1f, y1), Vector2.zero, Vector2.zero),
                    step.ok ? new Color(0.10f, 0.26f, 0.30f, 0.95f) : new Color(0.33f, 0.15f, 0.13f, 0.95f));

                UIFactory.Label(card.transform, "Cap", StepCap(i) + step.name, 11,
                    TextAlignmentOptions.MidlineLeft,
                    new UIFactory.Anchor(new Vector2(0.05f, 0.5f), new Vector2(0.97f, 1f), Vector2.zero, Vector2.zero),
                    step.ok ? UITheme.Success : UITheme.Warning, FontStyles.Bold);

                var detail = UIFactory.Label(card.transform, "Detail", Shorten(step.detail, 42), 9,
                    TextAlignmentOptions.TopLeft,
                    new UIFactory.Anchor(new Vector2(0.05f, 0f), new Vector2(0.97f, 0.52f), Vector2.zero, Vector2.zero),
                    UITheme.TextMuted);
                detail.textWrappingMode = TextWrappingModes.Normal;
                detail.verticalAlignment = VerticalAlignmentOptions.Top;
                _stepCards.Add(card);
            }
        }

        void RebuildRows()
        {
            for (int i = 0; i < _rows.Count; i++)
                if (_rows[i] != null) Destroy(_rows[i]);
            _rows.Clear();
            if (_rowsRoot == null || _plan == null) return;

            AddAxisRow();

            if (_plan.rings.Count == 0)
            {
                AddEmptyRow(_plan.TotalLegs == 0
                    ? "暂无待编航段：全网航班都已排定。新增航线后运营部会自动重编。"
                    : "本轮一个环也没编成 —— 原因见下方运力缺口，完整报告在收件箱。");
            }
            else
            {
                for (int i = 0; i < _plan.rings.Count; i++) AddRingRow(_plan.rings[i]);
            }
            for (int i = 0; i < _plan.unplaced.Count; i++) AddGapRow(_plan.unplaced[i]);
        }

        /// <summary>时间轴刻度行：没有刻度的甘特图，玩家读不出「哪段空着」。</summary>
        void AddAxisRow()
        {
            var row = UIFactory.Panel(_rowsRoot, "Axis", UIFactory.Fill, new Color(0, 0, 0, 0));
            row.AddComponent<LayoutElement>().preferredHeight = 16;
            for (int h = 0; h <= 24; h += 6)
            {
                float x = TrackLeft + TrackWidth * (h / 24f);
                var label = UIFactory.Label(row.transform, "H" + h,
                    h == 24 ? "24" : h.ToString("00"), 9, TextAlignmentOptions.Center,
                    new UIFactory.Anchor(new Vector2(Mathf.Clamp01(x - 0.02f), 0f),
                                         new Vector2(Mathf.Clamp01(x + 0.02f), 1f), Vector2.zero, Vector2.zero),
                    new Color(1f, 1f, 1f, 0.42f));
                label.raycastTarget = false;
            }
            _rows.Add(row);
        }

        void AddEmptyRow(string text)
        {
            var row = UIFactory.Panel(_rowsRoot, "EmptyRow", UIFactory.Fill, UITheme.Panel);
            row.AddComponent<LayoutElement>().preferredHeight = 64;
            var label = UIFactory.Label(row.transform, "T", text, 12, TextAlignmentOptions.Center,
                UIFactory.Fill, UITheme.TextMuted);
            label.textWrappingMode = TextWrappingModes.Normal;
            _rows.Add(row);
        }

        void AddRingRow(OpsRotationBuilder.Ring ring)
        {
            var row = UIFactory.Panel(_rowsRoot, "Ring_" + (ring.registration ?? ""), UIFactory.Fill, UITheme.Panel);
            row.AddComponent<LayoutElement>().preferredHeight = 62;

            // 左栏：机号 + 机型 + 当日小计（先看这一格就知道这架机排得好不好）。
            UIFactory.Label(row.transform, "Reg", ring.registration, 13, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.012f, 0.48f), new Vector2(0.2f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);
            UIFactory.Label(row.transform, "Type", ring.type, 9, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.012f, 0.25f), new Vector2(0.2f, 0.5f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            UIFactory.Label(row.transform, "Sum",
                $"{ring.legs.Count} 段 · {ring.UtilizationHours:F1}h · {ring.ProfitWan:F0}万",
                9, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.012f, 0.02f), new Vector2(0.2f, 0.28f), Vector2.zero, Vector2.zero),
                ring.Valid ? UITheme.Success : UITheme.Warning);

            var track = UIFactory.Panel(row.transform, "Track",
                new UIFactory.Anchor(new Vector2(TrackLeft, 0.14f), new Vector2(0.995f, 0.86f), Vector2.zero, Vector2.zero),
                new Color(0.03f, 0.07f, 0.13f, 0.9f), rounded: false);

            for (int h = 3; h < 24; h += 3)
            {
                float x = h / 24f;
                UIFactory.Panel(track.transform, "Tick" + h,
                    new UIFactory.Anchor(new Vector2(x, 0f), new Vector2(x + 0.0018f, 1f), Vector2.zero, Vector2.zero),
                    new Color(1f, 1f, 1f, h % 6 == 0 ? 0.16f : 0.08f), rounded: false);
            }

            for (int i = 0; i < ring.legs.Count; i++)
            {
                var leg = ring.legs[i];
                Color color = LegColors[StableIndex(leg.routeId, LegColors.Length)];

                AddBlock(track.transform, $"L{i}_out", leg.depMin, leg.ArrMin, color,
                    ring.legs.Count <= 5 ? leg.destination : "");
                // 过站灰条；过站不足点红 —— 这正是「两段靠太近」的可视化。
                AddBlock(track.transform, $"L{i}_turn", leg.ArrMin, leg.BackDepMin,
                    leg.turnMin < OpsRotationBuilder.MinTurnMinutes
                        ? new Color(0.86f, 0.26f, 0.26f, 0.95f)
                        : new Color(0.55f, 0.58f, 0.64f, 0.8f), "");
                AddBlock(track.transform, $"L{i}_back", leg.BackDepMin, leg.BackArrMin,
                    new Color(color.r * 0.72f, color.g * 0.72f, color.b * 0.72f, 1f), "");

                float x0 = Mathf.Clamp01(leg.depMin / (float)DayMinutes);
                var t = UIFactory.Label(track.transform, $"L{i}_t", OpsRotationLogic.FormatMinute(leg.depMin),
                    8, TextAlignmentOptions.TopLeft,
                    new UIFactory.Anchor(new Vector2(x0, 0f), new Vector2(Mathf.Clamp01(x0 + 0.1f), 0.32f),
                                         Vector2.zero, Vector2.zero),
                    new Color(1f, 1f, 1f, 0.5f));
                t.raycastTarget = false;
            }

            // 没过校验的环：把失败原因写在行尾（玩家不必再去读收件箱才知道为什么是黄的）。
            if (!ring.Valid && !string.IsNullOrEmpty(ring.Note))
            {
                var warn = UIFactory.Label(row.transform, "Warn", Shorten(ring.Note, 30), 8,
                    TextAlignmentOptions.BottomRight,
                    new UIFactory.Anchor(new Vector2(TrackLeft, 0.86f), new Vector2(0.995f, 1f), Vector2.zero, Vector2.zero),
                    UITheme.Warning);
                warn.raycastTarget = false;
            }

            _rows.Add(row);
        }

        void AddBlock(Transform track, string name, int from, int to, Color color, string caption)
        {
            float x0 = Mathf.Clamp01(from / (float)DayMinutes);
            float x1 = Mathf.Clamp01(to / (float)DayMinutes);
            if (x1 - x0 < 0.0018f) x1 = x0 + 0.0018f;

            var block = UIFactory.Panel(track, name,
                new UIFactory.Anchor(new Vector2(x0, 0.30f), new Vector2(x1, 0.92f), Vector2.zero, Vector2.zero),
                color, rounded: false);
            var img = block.GetComponent<Image>();
            if (img != null) img.raycastTarget = false;

            if (!string.IsNullOrEmpty(caption) && x1 - x0 > 0.045f)
            {
                var label = UIFactory.Label(block.transform, "Cap", caption, 8, TextAlignmentOptions.Center,
                    UIFactory.Fill, new Color(1f, 1f, 1f, 0.92f));
                label.raycastTarget = false;
            }
        }

        void AddGapRow(OpsRotationBuilder.Leg leg)
        {
            var row = UIFactory.Panel(_rowsRoot, "Gap_" + leg.Key, UIFactory.Fill,
                new Color(0.20f, 0.10f, 0.10f, 0.85f));
            UIFactory.AddBorder(row, UITheme.Warning, new Vector2(1f, -1f));
            row.AddComponent<LayoutElement>().preferredHeight = 34;

            UIFactory.Label(row.transform, "T",
                $"〔未编入〕{leg.flightNumber} {leg.origin}→{leg.destination} {OpsRotationLogic.FormatMinute(leg.depMin)}"
                + $"　过站 {leg.turnMin} 分",
                11, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.015f, 0f), new Vector2(0.99f, 1f), Vector2.zero, Vector2.zero),
                UITheme.Warning);
            _rows.Add(row);
        }

        void RequestReplan()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            var plan = OpsRotationBuilder.BuildFromGameState(gs);
            InboxManager.QueueRotationPlan(gs, plan, "CEO 在航班环看板点了「请求运营部重编」", 0);
            _notice = "已交运营部重编，方案在收件箱待批。";
            Refresh();
        }

        static string StepCap(int i)
        {
            switch (i)
            {
                case 0: return "① ";
                case 1: return "② ";
                case 2: return "③ ";
                case 3: return "④ ";
                case 4: return "⑤ ";
                default: return "⑥ ";
            }
        }

        static string Shorten(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }

        static int StableIndex(string key, int modulo)
        {
            if (modulo <= 1) return 0;
            int h = 0;
            if (!string.IsNullOrEmpty(key))
                for (int i = 0; i < key.Length; i++) h = (h * 31 + key[i]) & 0x7fffffff;
            return h % modulo;
        }
    }
}
