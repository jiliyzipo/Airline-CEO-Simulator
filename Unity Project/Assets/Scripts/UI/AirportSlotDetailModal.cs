using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.UI.Navigation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>
    /// 「机场时刻 · 时刻详情」弹窗（★2026-11-08，对齐 UI 原型）。
    ///
    /// **只读**：原型里那两个「申请时刻 / 二级市场买入」按钮**没有**接进来，这不是漏做。
    /// 2026-11-04 用户裁定「买卖具体哪个机场的哪个时刻，不是 CEO 的工作，是运营部的工作」，
    /// 取得时刻槽的正路只有一条 —— 运营部按航线缺口自动递 apply_slot / buy_slot 提案进收件箱，
    /// CEO 只批「买不买」。在这里放一个能直接下单的按钮，等于把已废止的手动渠道又装回来，
    /// 而且会绕过审批与收件箱。故弹窗底部明写渠道，不伪造动作。
    ///
    /// 数据全部来自调用方已算好的 <see cref="IndustryAirlineLogic.SlotChip"/>：
    /// 容量 / 已占 / 可申请与时刻闸门 <c>RoutePermitLogic.SlotsAvailableAt</c> 同源，
    /// 本类不做任何二次推导，也不缓存 —— 关掉即销毁，不会带着过期数字留在屏幕上。
    /// </summary>
    public class AirportSlotDetailModal : MonoBehaviour
    {
        // 与 UI 原型同一套配色。
        static readonly Color PanelBg = new Color(0.078f, 0.161f, 0.271f, 0.99f); // #142944
        static readonly Color SubPanel = new Color(0.106f, 0.212f, 0.337f, 1f);   // #1b3656
        static readonly Color NumBg = new Color(0.082f, 0.169f, 0.278f, 1f);      // #152b47
        static readonly Color Green = new Color(0.298f, 0.890f, 0.620f, 1f);      // #4ce39e
        static readonly Color MineBlue = new Color(0.282f, 0.541f, 0.980f, 1f);   // #488afa
        static readonly Color OtherGrey = new Color(0.514f, 0.600f, 0.706f, 1f);  // #8399b4
        static readonly Color IconWhite = new Color(0.945f, 0.969f, 1f, 1f);      // #f1f7ff

        static AirportSlotDetailModal _current;

        /// <summary>
        /// 打开（同一时间只留一个）。<paramref name="canvasRoot"/> 由调用方给出 ——
        /// 弹窗必须挂在画布级，挂在页面里会被页面的 ScrollRect 裁掉。
        /// </summary>
        public static void Open(Transform canvasRoot, string code, string cityName, int band,
            string clock, IndustryAirlineLogic.SlotChip chip)
        {
            Close();
            if (canvasRoot == null) return;

            var root = UIFactory.Panel(canvasRoot, "SlotDetailOverlay", UIFactory.Fill,
                new Color(0.02f, 0.04f, 0.08f, 0.76f), rounded: false);
            root.transform.SetAsLastSibling();

            // 蒙层点击关闭：按钮挂在蒙层上，卡片自己的 Image 会先吃掉落在卡片上的点击。
            var dim = root.AddComponent<Button>();
            dim.transition = Selectable.Transition.None;

            var modal = root.AddComponent<AirportSlotDetailModal>();
            modal.Build(root.transform, code, cityName, band, clock, chip);

            dim.onClick.AddListener(modal.Hide);
            _current = modal;
            modal.RegisterBack();
        }

        public static void Close()
        {
            if (_current != null) _current.Hide();
        }

        void Build(Transform root, string code, string cityName, int band, string clock,
            IndustryAirlineLogic.SlotChip chip)
        {
            var card = UIFactory.Panel(root, "Card",
                new UIFactory.Anchor(new Vector2(0.235f, 0.185f), new Vector2(0.765f, 0.855f),
                    Vector2.zero, Vector2.zero), PanelBg);
            UIFactory.AddBorder(card, new Color(0.325f, 0.435f, 0.592f, 1f), new Vector2(1.4f, -1.4f));

            UIFactory.Label(card.transform, "Title", $"{code} · {clock}", 21,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.035f, 0.885f), new Vector2(0.70f, 0.975f),
                    Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            UIFactory.ClampSingleLine(UIFactory.Label(card.transform, "SubTitle",
                $"{cityName} · {ScheduleSlots.SlotBandName(band)}班 · "
                + $"{ScheduleSlots.BandHourStart(band):00}:00–{ScheduleSlots.BandHourEnd(band):00}:45",
                11, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.035f, 0.815f), new Vector2(0.70f, 0.880f),
                    Vector2.zero, Vector2.zero),
                UITheme.TextMuted));

            var close = UIFactory.Button(card.transform, "Close", "关闭 ✕",
                new UIFactory.Anchor(new Vector2(0.855f, 0.885f), new Vector2(0.965f, 0.965f),
                    Vector2.zero, Vector2.zero),
                new Color(0.141f, 0.259f, 0.404f, 1f), Hide, 12);
            DisableChildLabelRaycasts(close);

            bool curfew = AirportCatalog.InCurfew(code, ScheduleSlots.ParseClock(clock));

            BuildDirection(card.transform, "Dep", "离港", true, chip, curfew,
                new UIFactory.Anchor(new Vector2(0.035f, 0.285f), new Vector2(0.487f, 0.795f),
                    Vector2.zero, Vector2.zero));
            BuildDirection(card.transform, "Arr", "到港", false, chip, curfew,
                new UIFactory.Anchor(new Vector2(0.513f, 0.285f), new Vector2(0.965f, 0.795f),
                    Vector2.zero, Vector2.zero));

            // —— 底部：数据口径与取得渠道（不伪造动作）——
            UIFactory.Label(card.transform, "FootNote",
                "取得渠道：缺槽时运营部按航线自动递交「申请时刻 / 二级市场买入」提案，由你在收件箱审批。"
                + "\n本页只做查询 —— 容量、已占、可申请与时刻闸门同源，不在此处直接下单。",
                10, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.035f, 0.045f), new Vector2(0.965f, 0.255f),
                    Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
        }

        void BuildDirection(Transform card, string name, string title, bool departure,
            IndustryAirlineLogic.SlotChip chip, bool curfew, UIFactory.Anchor anchor)
        {
            var panel = UIFactory.Panel(card, name, anchor, SubPanel);
            UIFactory.AddBorder(panel, new Color(0.208f, 0.329f, 0.455f, 1f), new Vector2(1f, -1f));

            // 标题行：起飞 / 降落图标 + 方向名
            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(panel.transform, false);
            var iconRt = iconGo.GetComponent<RectTransform>();
            iconRt.anchorMin = new Vector2(0.045f, 0.845f);
            iconRt.anchorMax = new Vector2(0.115f, 0.955f);
            iconRt.offsetMin = Vector2.zero;
            iconRt.offsetMax = Vector2.zero;
            var icon = iconGo.AddComponent<Image>();
            icon.sprite = departure ? UIFactory.TakeoffSprite : UIFactory.LandingSprite;
            icon.preserveAspect = true;
            icon.color = IconWhite;
            icon.raycastTarget = false;

            UIFactory.Label(panel.transform, "Name", title, 14, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.130f, 0.845f), new Vector2(0.62f, 0.955f),
                    Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            int capacity = departure ? chip.capacity : chip.arrCapacity;
            int held = departure ? chip.held : chip.arrHeldTotal;
            int mine = departure ? chip.mineHeld : chip.arrHeld;
            if (!departure && mine > held) mine = held;
            int other = held - mine;
            if (other < 0) other = 0;
            int free = departure ? chip.freeLeft : chip.arrFreeLeft;

            // 三个数：总容量 / 已占用 / 可申请
            NumberBox(panel.transform, "Cap", 0.035f, 0.345f, capacity.ToString(), "总容量",
                UITheme.TextPrimary);
            NumberBox(panel.transform, "Held", 0.345f, 0.655f, held.ToString(), "已占用",
                UITheme.TextPrimary);
            NumberBox(panel.transform, "Free", 0.655f, 0.965f, free.ToString(), "可申请", Green);

            // 堆叠条：他航 / 我司 / 可申请
            var barBg = UIFactory.Panel(panel.transform, "StackBg",
                new UIFactory.Anchor(new Vector2(0.035f, 0.545f), new Vector2(0.965f, 0.585f),
                    Vector2.zero, Vector2.zero),
                new Color(0.063f, 0.129f, 0.231f, 1f));
            var barImg = barBg.GetComponent<Image>();
            if (barImg != null) barImg.raycastTarget = false;

            if (capacity > 0)
            {
                float x = 0.035f;
                const float w = 0.930f;
                x = Segment(panel.transform, "SegOther", x, w, other / (float)capacity, OtherGrey);
                x = Segment(panel.transform, "SegMine", x, w, mine / (float)capacity, MineBlue);
                Segment(panel.transform, "SegFree", x, w, free / (float)capacity, Green);
            }

            // 我司 / 他航 + 谁占着
            string ownerLine = departure
                ? (string.IsNullOrEmpty(chip.holderNames) ? "—" : chip.holderNames)
                : (string.IsNullOrEmpty(chip.arrOwnerCode) ? "—" : chip.arrOwnerCode);
            UIFactory.Label(panel.transform, "MineOther",
                $"我司 {mine} · 他航 {other}", 11, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.035f, 0.415f), new Vector2(0.965f, 0.520f),
                    Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);

            var owner = UIFactory.Label(panel.transform, "Owners",
                $"持有方：{ownerLine}", 10, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.035f, 0.255f), new Vector2(0.965f, 0.415f),
                    Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            UIFactory.ClampLines(owner, 2);

            // 状态：把「宵禁 / 未公布 / 已满 / 池满但逐刻空」讲清楚 —— 三种「不可申请」不是一回事。
            UIFactory.Label(panel.transform, "Status", StatusText(capacity, held, free, curfew),
                10, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.035f, 0.045f), new Vector2(0.965f, 0.185f),
                    Vector2.zero, Vector2.zero),
                curfew ? new Color(0.894f, 0.627f, 0.655f, 1f) : UITheme.TextMuted);
        }

        static string StatusText(int capacity, int held, int free, bool curfew)
        {
            if (curfew) return "该刻处于宵禁时段，禁止正常申请";
            if (capacity <= 0) return "该刻未公布时刻容量，不可申请";
            int clockFree = capacity - held;
            if (clockFree < 0) clockFree = 0;
            if (free <= 0 && clockFree > 0)
                return $"逐刻还空 {clockFree} 条，但本段池余量已用尽 —— 需等让位或走二级市场";
            if (free > 0) return "该刻仍可申请";
            return "该刻已满";
        }

        static void NumberBox(Transform parent, string name, float x0, float x1, string value,
            string caption, Color valueColor)
        {
            var box = UIFactory.Panel(parent, name,
                new UIFactory.Anchor(new Vector2(x0 + 0.008f, 0.630f), new Vector2(x1 - 0.008f, 0.805f),
                    Vector2.zero, Vector2.zero), NumBg);
            var img = box.GetComponent<Image>();
            if (img != null) img.raycastTarget = false;

            UIFactory.Label(box.transform, "V", value, 17, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0f, 0.34f), new Vector2(1f, 0.98f),
                    Vector2.zero, Vector2.zero),
                valueColor, FontStyles.Bold);
            UIFactory.Label(box.transform, "C", caption, 9, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0f, 0.02f), new Vector2(1f, 0.38f),
                    Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
        }

        /// <summary>堆叠条的一段：从 x0 起按 frac 占满整个条宽 w。返回下一段的起点。</summary>
        static float Segment(Transform parent, string name, float x0, float w, float frac, Color color)
        {
            float f = Mathf.Clamp01(frac);
            if (f <= 0f) return x0;
            var go = UIFactory.Panel(parent, name,
                new UIFactory.Anchor(new Vector2(x0, 0.545f), new Vector2(x0 + w * f, 0.585f),
                    Vector2.zero, Vector2.zero),
                color, rounded: false);
            var img = go.GetComponent<Image>();
            if (img != null) img.raycastTarget = false;
            return x0 + w * f;
        }

        /// <summary>
        /// 挂到底栏 &lt; 的返回目标上（与「开通航线」弹窗同一套）。
        /// 蒙层是全屏 raycast 目标，不抬高底栏的话点根本到不了 &lt;。
        /// </summary>
        void RegisterBack()
        {
            ModalBackStack.Push(this, TryBack);
            PrefabNavIntegration.RaiseChromeAboveOverlays();
        }

        public bool TryBack()
        {
            if (!gameObject.activeSelf) return false;
            Hide();
            return true;
        }

        public void Hide()
        {
            ModalBackStack.Pop(this);
            if (_current == this) _current = null;
            gameObject.SetActive(false);
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            ModalBackStack.Pop(this);
            if (_current == this) _current = null;
        }

        static void DisableChildLabelRaycasts(Button btn)
        {
            if (btn == null) return;
            var labels = btn.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < labels.Length; i++) labels[i].raycastTarget = false;
        }
    }
}
