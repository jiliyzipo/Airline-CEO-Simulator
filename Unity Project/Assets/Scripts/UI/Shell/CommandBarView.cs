using System.Collections;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI.Navigation;
using TMPro;
using UnityEngine;

namespace AirlineCEO.UI.Shell
{
    /// <summary>
    /// 底栏上方的 **CEO 命令台**（设计稿 c 栏）：一句话 → 按「意图 + 当前页面」交给对应部门，
    /// 部门立刻回一行结论，并把完整汇报件投进收件箱。
    ///
    /// 四条边界（都是踩过的坑）：
    /// · **读指令直接回，写指令先确认**。汇报类只读台账；关线是唯一的写指令，
    ///   必须先弹确认卡（列出日利/班期/机号/要退的槽），点「确认停飞」才落账 ——
    ///   一句话认错机场的代价是一条在营航线，不可撤销。
    /// · **页面是提示，不是过滤器**。占位文字随页面变，但分派看的是句子（见 <see cref="CeoCommandRouter"/>）。
    /// · **没听懂就不发信**。只把引导显示在命令条上，避免收件箱被「我不明白」刷屏。
    /// · **确认卡不吃整条点击**。回执条点哪儿都能收，确认卡只有「取消」能收 —— 免得手滑关线。
    /// </summary>
    public class CommandBarView : MonoBehaviour
    {
        TMP_InputField _input;
        TMP_Text _placeholder;
        GameObject _answerCard;
        TextMeshProUGUI _answerText;
        GameObject _confirmCard;
        TextMeshProUGUI _confirmTitle;
        TextMeshProUGUI _confirmBody;
        string _pendingRouteKey;
        string _pendingRaw;
        string _pageId;
        /// <summary>上一次被受理的回车所在帧：用来吞掉 EventSystem 对同一次 Enter 的第二次派发。</summary>
        int _lastSubmitFrame = -1;

        public static CommandBarView Build(Transform parent, float x0, float x1)
        {
            var go = UIFactory.Panel(parent, "CommandBar",
                new UIFactory.Anchor(new Vector2(x0, 0f), new Vector2(x1, 1f), new Vector2(4f, 0f), new Vector2(0f, 0f)),
                new Color(0f, 0f, 0f, 0f), rounded: false);
            var img = go.GetComponent<UnityEngine.UI.Image>();
            if (img != null) img.raycastTarget = false;

            var view = go.AddComponent<CommandBarView>();
            view.BuildUi();
            return view;
        }

        void BuildUi()
        {
            _input = UIFactory.Input(transform, "CeoCommand", CeoCommandRouter.PageHint(null),
                new UIFactory.Anchor(new Vector2(0f, 0.14f), new Vector2(0.975f, 0.86f), Vector2.zero, Vector2.zero));
            _input.lineType = TMP_InputField.LineType.SingleLine;
            _input.onSubmit.RemoveAllListeners();
            _input.onSubmit.AddListener(Submit);
            if (_input.textComponent != null) _input.textComponent.fontSize = 14f;
            _placeholder = _input.placeholder as TMP_Text;
            if (_placeholder != null) _placeholder.fontSize = 12f;

            var enter = UIFactory.Label(transform, "EnterHint", "↵",
                14, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.975f, 0.14f), new Vector2(1f, 0.86f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            enter.raycastTarget = false;

            BuildAnswerCard();
            BuildConfirmCard();
        }

        /// <summary>
        /// 回执条：贴在命令条正上方、只占 c 栏宽度，**不自动消失**。
        ///
        /// 原来设了 9 秒自动收起，结果是：CEO 看完「日利润 / 评级」，一点进收件箱再回来就找不到了，
        /// 只能靠记忆去翻信。结论卡要么留在原地，要么等于没有 —— 现在改成
        /// 「下一条指令替换 / 点条上任意位置（或右侧 ×）收起」，切页面也不再清掉。
        /// </summary>
        void BuildAnswerCard()
        {
            var host = transform.parent != null ? transform.parent : transform;
            // 左边界对齐 a/c 分栏（40.4%），别压在左侧财务数字上
            var card = UIFactory.Panel(host, "CommandAnswer",
                new UIFactory.Anchor(new Vector2(0.404f, 1f), new Vector2(1f, 1f),
                    new Vector2(0f, 4f), new Vector2(0f, 40f)),
                new Color(0.06f, 0.14f, 0.26f, 0.97f), rounded: false);
            UIFactory.AddBorder(card, UITheme.Accent, new Vector2(1f, -1f));
            var img = card.GetComponent<UnityEngine.UI.Image>();
            if (img != null)
            {
                // 整条可点：CEO 看到回执的第一反应是「点掉它」，不该被迫去瞄那个小 ×。
                img.raycastTarget = true;
                var dismiss = card.AddComponent<UnityEngine.UI.Button>();
                dismiss.targetGraphic = img;
                dismiss.transition = UnityEngine.UI.Selectable.Transition.None;  // 整条泛白太吵
                dismiss.onClick.AddListener(HideAnswer);
            }

            _answerText = UIFactory.Label(card.transform, "Text", "", 12,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.012f, 0.06f), new Vector2(0.94f, 0.94f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            _answerText.raycastTarget = false;
            _answerText.textWrappingMode = TextWrappingModes.Normal;
            _answerText.overflowMode = TextOverflowModes.Truncate;

            // 显式收起，代替过去那次「时间到了自己消失」（玩家读信时卡片不该陪着一起消失）
            UIFactory.Button(card.transform, "Dismiss", "×",
                new UIFactory.Anchor(new Vector2(0.945f, 0.10f), new Vector2(0.995f, 0.90f),
                    Vector2.zero, Vector2.zero),
                new Color(0f, 0f, 0f, 0f), HideAnswer, 13);

            _answerCard = card;
            _answerCard.SetActive(false);
        }

        /// <summary>
        /// 确认卡：写指令（目前只有关线）落账前的最后一道闸。
        ///
        /// 与回执条的区别：这张卡**不响应整条点击**、也不自动消失 —— 只有「确认停飞」与「取消」两个出口。
        /// 回执条那种「点哪儿都能收」的做法放在这里就是事故：手一抖，一条在营航线没了。
        /// </summary>
        void BuildConfirmCard()
        {
            var host = transform.parent != null ? transform.parent : transform;
            var card = UIFactory.Panel(host, "CommandConfirm",
                new UIFactory.Anchor(new Vector2(0.404f, 1f), new Vector2(1f, 1f),
                    new Vector2(0f, 4f), new Vector2(0f, 104f)),
                new Color(0.10f, 0.08f, 0.04f, 0.98f), rounded: false);
            UIFactory.AddBorder(card, UITheme.Warning, new Vector2(2f, -2f));
            var img = card.GetComponent<UnityEngine.UI.Image>();
            if (img != null) img.raycastTarget = true;   // 挡住卡片下方的点击，但不吞成「收起」

            _confirmTitle = UIFactory.Label(card.transform, "Title", "", 13,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.014f, 0.72f), new Vector2(0.985f, 0.97f),
                    Vector2.zero, Vector2.zero),
                UITheme.Warning);
            _confirmTitle.raycastTarget = false;
            _confirmTitle.overflowMode = TextOverflowModes.Ellipsis;

            _confirmBody = UIFactory.Label(card.transform, "Body", "", 11,
                TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.014f, 0.26f), new Vector2(0.985f, 0.71f),
                    Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            _confirmBody.raycastTarget = false;
            _confirmBody.textWrappingMode = TextWrappingModes.Normal;
            _confirmBody.overflowMode = TextOverflowModes.Truncate;

            UIFactory.Button(card.transform, "Close", "确认停飞",
                new UIFactory.Anchor(new Vector2(0.815f, 0.05f), new Vector2(0.985f, 0.25f),
                    Vector2.zero, Vector2.zero),
                new Color(0.52f, 0.20f, 0.10f, 1f), ConfirmClose, 12);

            UIFactory.Button(card.transform, "Cancel", "取消",
                new UIFactory.Anchor(new Vector2(0.685f, 0.05f), new Vector2(0.805f, 0.25f),
                    Vector2.zero, Vector2.zero),
                new Color(0.20f, 0.22f, 0.26f, 1f), CancelClose, 12);

            _confirmCard = card;
            _confirmCard.SetActive(false);
        }

        /// <summary>页面切换时刷新提示语（占位文字 = 这一页能问什么）。回执条保留。</summary>
        public void RefreshPageHint(string pageId)
        {
            _pageId = pageId;
            if (_placeholder != null) _placeholder.text = CeoCommandRouter.PageHint(pageId);
        }

        void Submit(string text)
        {
            // Enter 会**两次**打到 onSubmit：一次来自 TMP_InputField 自己在 KeyPressed 里
            // 处理 Return（SendOnSubmit），一次来自 EventSystem 把 Enter 当「Submit」轴再发一遍。
            // 第一次我们已清空输入框，于是第二次读到空串 —— 屏幕上就出现「请先输入一句话」，
            // 而邮件其实早已寄出（2026-10-20 实测：主页首条指令，收件箱有信、回执报错）。
            // 同一帧内的重复提交一律丢弃；空回车也不占回执。
            if (Time.frameCount == _lastSubmitFrame) return;

            string raw = (text ?? "").Trim();
            if (raw.Length == 0) return;   // 占位文字已在说明能问什么，空回车不必再弹一条
            _lastSubmitFrame = Time.frameCount;

            var gs = GameState.Instance;
            if (gs == null)
            {
                ShowAnswer("游戏尚未开局。", false);
                return;
            }

            string pageId = _pageId;
            var router = NavigationRouter.Instance;
            if (router != null && !string.IsNullOrEmpty(router.CurrentPageId)) pageId = router.CurrentPageId;

            var ask = CeoCommandRouter.Route(pageId, raw, DeptReportLogic.RouteNameResolver(gs));
            var ans = DeptReportLogic.Build(gs, ask, raw);

            if (!ans.ok)
            {
                // 没听懂 / 缺实体：只在命令条上引导，不发信（收件箱不该被「我不明白」刷屏）
                ShowAnswer(string.IsNullOrEmpty(ans.note) ? "没听懂这句话。" : ans.note, false);
                RefocusInput();
                return;
            }

            if (ans.needsQuery)
            {
                // 问某条线：交运营部按本线台账答复（汇报件，无需批复），不动任何台账。
                bool done = DeptReportLogic.RunRouteQuery(gs, ans.routeKey, raw, out string receipt);
                ShowAnswer($"{(done ? "已回函" : "未回函")}｜{receipt}", done);
                if (_input != null) _input.text = "";
                RefocusInput();
                return;
            }

            if (ans.needsRun)
            {
                // 运力调令：解析 + 调起既有管线。管线自己会产出呈报件（CEO 在收件箱批一次），
                // 所以这里不再弹确认卡 —— 同一件事问两遍会让人以为第一次没生效。
                bool done = DeptReportLogic.RunCapacityDirective(gs, ans.routeKey, raw, out string receipt);
                ShowAnswer($"{(done ? "已下达" : "未下达")}｜{receipt}", done);
                if (_input != null) _input.text = "";
                RefocusInput();
                return;
            }

            if (ans.needsConfirm)
            {
                // 写指令：不投递、不动账，只弹确认卡。清空输入框，避免 CEO 重复回车时又弹一次。
                ShowConfirm(ans, raw);
                if (_input != null) _input.text = "";
                RefocusInput();
                return;
            }

            InboxManager.QueueDeptAnswer(gs, ans, raw, out string msg);
            ShowAnswer($"回执｜{ans.oneLine}　·　{ans.sender}已回函", true);

            if (_input != null) _input.text = "";
            RefocusInput();
        }

        // ———————————————————————————— 写指令：确认 / 取消 ————————————————————————————

        void ShowConfirm(DeptReportLogic.DeptAnswer ans, string raw)
        {
            if (_confirmCard == null) return;
            _pendingRouteKey = ans.routeKey;
            _pendingRaw = raw;
            if (_confirmTitle != null) _confirmTitle.text = ans.confirmTitle ?? "确认执行？";
            if (_confirmBody != null) _confirmBody.text = ans.confirmBody ?? "";
            HideAnswer();
            _confirmCard.SetActive(true);
            _confirmCard.transform.SetAsLastSibling();
        }

        void CancelClose()
        {
            HideConfirm();
            ShowAnswer(DeptReportLogic.CloseCancelNotice, true);
        }

        void ConfirmClose()
        {
            if (string.IsNullOrEmpty(_pendingRouteKey))
            {
                HideConfirm();
                ShowAnswer("确认已失效，请重新下达关线指令。", false);
                return;
            }

            var gs = GameState.Instance;
            if (gs == null)
            {
                HideConfirm();
                ShowAnswer("游戏尚未开局。", false);
                return;
            }

            // 先取值再收起：HideConfirm 会把待办键清空（旧版顺序反了，确认永远落不了账）
            string raw = _pendingRaw ?? "";
            string routeKey = _pendingRouteKey;
            HideConfirm();

            bool ok = DeptReportLogic.ExecuteClose(gs, routeKey, raw,
                out var ops, out var stg, out string message);

            // 处置函与网络复核函用不同盐：否则同一句原话的第二封会把第一封覆盖掉
            if (ops.ok) InboxManager.QueueDeptAnswer(gs, ops, raw, "|close-ops", out _);
            if (stg.ok) InboxManager.QueueDeptAnswer(gs, stg, raw, "|close-stg", out _);

            if (ok)
                ShowAnswer($"处置｜{message}　·　运营部与战略部已呈报", true);
            else
            {
                if (ops.ok) ShowAnswer($"未执行｜{message}　·　运营部已说明原因", false);
                else ShowAnswer($"未执行｜{message}", false);
            }
            RefocusInput();
        }

        /// <summary>
        /// 下一帧再夺回焦点。
        /// TMP 在 SendOnSubmit() 之后紧接着会调 DeactivateInputField()（清选区、清 text），
        /// 在回调里直接 ActivateInputField() 会被它当场取消，于是「回车后还得再点一下输入框」。
        /// </summary>
        void RefocusInput()
        {
            if (!isActiveAndEnabled) return;
            StartCoroutine(RefocusNextFrame());
        }

        IEnumerator RefocusNextFrame()
        {
            yield return null;   // 等 TMP 的 DeactivateInputField 走完
            if (_input != null && _input.isActiveAndEnabled) _input.ActivateInputField();
        }

        void ShowAnswer(string text, bool ok)
        {
            if (_answerCard == null) return;
            HideConfirm();               // 回执与确认卡同一位置，不能叠着显示
            _answerCard.SetActive(true);
            _answerCard.transform.SetAsLastSibling();
            if (_answerText != null)
            {
                _answerText.color = ok ? UITheme.TextPrimary : UITheme.Warning;
                _answerText.text = text ?? "";
                // 回执长度不定（运力调令带机号/班期/环号，引导语更长）：按实际行数把卡片往上撑，
                // 否则关键结论会被截成「…」——CEO 只能靠猜判断到底办没办成。
                var cardRt = _answerCard.GetComponent<RectTransform>();
                float width = _answerText.rectTransform.rect.width;
                if (cardRt != null && width > 1f)
                {
                    float need = _answerText.GetPreferredValues(_answerText.text, width, 0f).y;
                    float h = Mathf.Clamp(need + 12f, 36f, 96f);
                    cardRt.offsetMax = new Vector2(cardRt.offsetMax.x, 4f + h);
                }
            }
        }

        void HideAnswer()
        {
            if (_answerCard != null) _answerCard.SetActive(false);
        }

        void HideConfirm()
        {
            _pendingRouteKey = null;
            _pendingRaw = null;
            if (_confirmCard != null) _confirmCard.SetActive(false);
        }

        void OnDisable()
        {
            HideAnswer();
            HideConfirm();
        }
    }
}
