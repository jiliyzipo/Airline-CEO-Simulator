using AirlineCEO.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>
    /// GDD 7.7 · 战略性扩张提案弹窗（蓝金机遇风）。
    /// </summary>
    public class ExpansionProposalUI : MonoBehaviour
    {
        private static readonly Color GoldBorder = new Color(0.85f, 0.7f, 0.25f, 1f);
        private static readonly Color CardBg = new Color(0.08f, 0.14f, 0.28f, 1f);

        private GameObject _card;
        private Button _btnAccept;
        private Button _btnDelay;
        private Button _btnReject;
        private TextMeshProUGUI _hint;

        #region Build

        public static ExpansionProposalUI Create(Transform canvasParent)
        {
            var host = new GameObject("ExpansionProposalUI", typeof(RectTransform));
            host.transform.SetParent(canvasParent, false);
            UIFactory.ApplyAnchor(host.GetComponent<RectTransform>(), UIFactory.Fill);
            var ui = host.AddComponent<ExpansionProposalUI>();
            ui.Build(host.transform);
            host.SetActive(false);
            return ui;
        }

        private void Build(Transform parent)
        {
            var dim = UIFactory.Panel(parent, "Dim", UIFactory.Fill,
                new Color(0.02f, 0.04f, 0.1f, 0.8f), rounded: false);
            dim.GetComponent<Image>().raycastTarget = true;

            _card = UIFactory.Panel(parent, "ProposalCard",
                new UIFactory.Anchor(new Vector2(0.14f, 0.1f), new Vector2(0.86f, 0.92f), Vector2.zero, Vector2.zero),
                new Color(0.05f, 0.1f, 0.2f, 0.98f)).gameObject;
            UIFactory.AddBorder(_card, GoldBorder, new Vector2(2.5f, -2.5f));

            UIFactory.Label(_card.transform, "Title",
                "🌏 战略性扩张提案：开辟远程国际航线", 24, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.04f, 0.9f), new Vector2(0.96f, 0.98f), Vector2.zero, Vector2.zero),
                GoldBorder, FontStyles.Bold);

            UIFactory.Label(_card.transform, "Body",
                "经过三年的稳健运营，公司已具备国际化发展的基础。战略发展部建议开辟一条远程国际航线，打开新的增长空间。\n请决定是否启动此扩张计划。",
                14, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.05f, 0.72f), new Vector2(0.95f, 0.9f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);

            BuildDataCards(_card.transform);

            _btnAccept = UIFactory.Button(_card.transform, "BtnAccept",
                "【接受提案】启动扩张\n现金 -500 万 · 开通北京→纽约 · 满意度 +10 · 解锁伟大航路",
                new UIFactory.Anchor(new Vector2(0.06f, 0.28f), new Vector2(0.94f, 0.4f), Vector2.zero, Vector2.zero),
                UITheme.Success, () => Choose(1), 14);

            _btnDelay = UIFactory.Button(_card.transform, "BtnDelay",
                "【暂缓提案】再观望一年\n不扣钱 · 满意度 -3 · 下年 Q1 再议（最多 3 次）",
                new UIFactory.Anchor(new Vector2(0.06f, 0.15f), new Vector2(0.94f, 0.27f), Vector2.zero, Vector2.zero),
                UITheme.Warning, () => Choose(2), 14);
            var delayLabel = _btnDelay.GetComponentInChildren<TextMeshProUGUI>();
            if (delayLabel != null) delayLabel.color = new Color(0.12f, 0.1f, 0.05f, 1f);

            _btnReject = UIFactory.Button(_card.transform, "BtnReject",
                "【拒绝提案】专注现有市场\n不扣钱 · 满意度 -8 · 不再提案",
                new UIFactory.Anchor(new Vector2(0.06f, 0.02f), new Vector2(0.94f, 0.14f), Vector2.zero, Vector2.zero),
                UITheme.Danger, () => Choose(3), 14);

            _hint = UIFactory.Label(_card.transform, "Hint", "", 12, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.1f, 0.4f), new Vector2(0.9f, 0.46f), Vector2.zero, Vector2.zero),
                UITheme.Warning);
        }

        private void BuildDataCards(Transform parent)
        {
            float[] x0 = { 0.04f, 0.35f, 0.66f };
            float[] x1 = { 0.33f, 0.64f, 0.96f };
            string[] titles = { "航线", "机型", "财务预估" };
            string[] bodies =
            {
                "北京 → 纽约（JFK）\n远程国际\n航权竞争：中等\n通过率约 55%",
                "B787-9\n航程 14,000 km\n载客 290 人\n宽体远程主力",
                "启动资金 $500 万\n日利润约 $8.5 万\n月收入约 $255 万\n增长空间：高"
            };

            for (int i = 0; i < 3; i++)
            {
                var card = UIFactory.Panel(parent, "DataCard_" + i,
                    new UIFactory.Anchor(new Vector2(x0[i], 0.48f), new Vector2(x1[i], 0.72f), Vector2.zero, Vector2.zero),
                    CardBg);
                UIFactory.AddBorder(card, UITheme.Accent, new Vector2(1f, -1f));
                UIFactory.Label(card.transform, "T", titles[i], 15, TextAlignmentOptions.Center,
                    new UIFactory.Anchor(new Vector2(0.05f, 0.72f), new Vector2(0.95f, 0.95f), Vector2.zero, Vector2.zero),
                    UITheme.Accent, FontStyles.Bold);
                UIFactory.Label(card.transform, "B", bodies[i], 13, TextAlignmentOptions.Top,
                    new UIFactory.Anchor(new Vector2(0.06f, 0.05f), new Vector2(0.94f, 0.72f), Vector2.zero, Vector2.zero),
                    UITheme.TextPrimary);
            }
        }

        #endregion

        #region Show / Hide

        private void Choose(int choice)
        {
            if (ExpansionProposalManager.Instance != null)
                ExpansionProposalManager.Instance.OnProposalChoiceMade(choice);
            else
                Debug.LogError("[ExpansionProposalUI] Manager 为空。");
        }

        public void ShowProposal(bool allowDelay)
        {
            gameObject.SetActive(true);
            if (_card != null) _card.SetActive(true);
            if (_btnAccept != null) _btnAccept.gameObject.SetActive(true);
            if (_btnReject != null) _btnReject.gameObject.SetActive(true);
            if (_btnDelay != null) _btnDelay.gameObject.SetActive(allowDelay);
            if (_hint != null)
            {
                _hint.text = allowDelay
                    ? "游戏已暂停。接受将扣除启动资金并开通航线。"
                    : "已达暂缓上限（3 次），请接受或拒绝提案。";
            }
            transform.SetAsLastSibling();
        }

        public void ShowCashShortage(float need, float have)
        {
            if (_hint != null)
                _hint.text = $"现金不足：需要 {need:F0} 万，当前仅 {have:F0} 万。请暂缓/拒绝，或先积累现金。";
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        #endregion
    }
}
