using System;
using AirlineCEO.Data;
using AirlineCEO.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>Phase 10 · 购买飞机弹窗。</summary>
    public class FleetPurchaseUI : MonoBehaviour
    {
        private TextMeshProUGUI _hintText;
        private string _selectedTypeId;
        private Action _onClosed;
        private readonly System.Collections.Generic.List<Button> _typeBtns =
            new System.Collections.Generic.List<Button>();

        public static FleetPurchaseUI Create(Transform canvasParent)
        {
            var host = new GameObject("FleetPurchaseUI", typeof(RectTransform));
            host.transform.SetParent(canvasParent, false);
            UIFactory.ApplyAnchor(host.GetComponent<RectTransform>(), UIFactory.Fill);
            var ui = host.AddComponent<FleetPurchaseUI>();
            ui.Build(host.transform);
            host.SetActive(false);
            return ui;
        }

        private void Build(Transform parent)
        {
            var dim = UIFactory.Panel(parent, "Dim", UIFactory.Fill,
                new Color(0.02f, 0.03f, 0.06f, 0.78f), rounded: false);
            dim.GetComponent<Image>().raycastTarget = true;

            var card = UIFactory.Panel(parent, "Card",
                new UIFactory.Anchor(new Vector2(0.16f, 0.14f), new Vector2(0.84f, 0.86f), Vector2.zero, Vector2.zero),
                new Color(0.05f, 0.1f, 0.18f, 0.98f)).gameObject;
            UIFactory.AddBorder(card, UITheme.Accent, new Vector2(2f, -2f));

            UIFactory.Label(card.transform, "Title", "购买飞机", 24, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.06f, 0.88f), new Vector2(0.94f, 0.97f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            _hintText = UIFactory.Label(card.transform, "Hint", "请选择机型", 13, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.08f, 0.80f), new Vector2(0.92f, 0.88f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            BuildTypeCards(card.transform);

            UIFactory.Button(card.transform, "Confirm", "确认购买",
                new UIFactory.Anchor(new Vector2(0.12f, 0.06f), new Vector2(0.48f, 0.16f), Vector2.zero, Vector2.zero),
                UITheme.Success, OnConfirm, 16);

            UIFactory.Button(card.transform, "Cancel", "取消",
                new UIFactory.Anchor(new Vector2(0.52f, 0.06f), new Vector2(0.88f, 0.16f), Vector2.zero, Vector2.zero),
                UITheme.NavIdle, Hide, 16);
        }

        private void BuildTypeCards(Transform card)
        {
            var types = DataBase.Instance != null ? DataBase.Instance.AircraftTypes : null;
            if (types == null || types.Count == 0)
            {
                UIFactory.Label(card, "Empty", "机型目录未加载", 16, TextAlignmentOptions.Center,
                    new UIFactory.Anchor(new Vector2(0.1f, 0.4f), new Vector2(0.9f, 0.7f), Vector2.zero, Vector2.zero),
                    UITheme.Warning);
                return;
            }

            float w = 0.28f;
            float gap = 0.03f;
            float start = 0.06f;
            for (int i = 0; i < types.Count && i < 5; i++)
            {
                var t = types[i];
                float x0 = start + i * (w + gap);
                float x1 = x0 + w;
                string label =
                    $"{t.typeId}\n载客 {t.maxPassengers}\n航程 {t.maxRangeKm:F0} km\n${t.priceWan:F0} 万";
                string typeId = t.typeId;
                var btn = UIFactory.Button(card, "Type_" + t.typeId, label,
                    new UIFactory.Anchor(new Vector2(x0, 0.22f), new Vector2(x1, 0.78f), Vector2.zero, Vector2.zero),
                    UITheme.PanelDeep, () => SelectType(typeId), 14);
                _typeBtns.Add(btn);
            }
        }

        public void Show(Action onClosed = null)
        {
            _onClosed = onClosed;
            _selectedTypeId = null;
            if (_hintText != null)
            {
                var gs = GameState.Instance;
                _hintText.text = gs != null
                    ? $"当前现金 {gs.cash:F0} 万 · 请选择机型"
                    : "请选择机型";
            }
            Highlight();
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            var cb = _onClosed;
            _onClosed = null;
            cb?.Invoke();
        }

        private void SelectType(string typeId)
        {
            _selectedTypeId = typeId;
            if (_hintText != null)
                _hintText.text = $"已选：{typeId}";
            Highlight();
        }

        private void Highlight()
        {
            var types = DataBase.Instance != null ? DataBase.Instance.AircraftTypes : null;
            for (int i = 0; i < _typeBtns.Count; i++)
            {
                bool on = types != null && i < types.Count && types[i].typeId == _selectedTypeId;
                var img = _typeBtns[i].GetComponent<Image>();
                if (img != null) img.color = on ? UITheme.NavActive : UITheme.PanelDeep;
                var hover = _typeBtns[i].GetComponent<UIHoverBrighten>();
                if (hover != null) hover.RefreshBase();
            }
        }

        private void OnConfirm()
        {
            if (string.IsNullOrEmpty(_selectedTypeId))
            {
                if (_hintText != null) _hintText.text = "请先选择一种机型。";
                return;
            }

            if (FleetManager.Instance == null)
            {
                if (_hintText != null) _hintText.text = "FleetManager 未就绪";
                return;
            }

            bool ok = FleetManager.Instance.TryBuyAircraft(_selectedTypeId, out string msg);
            if (_hintText != null) _hintText.text = msg;
            if (ok) Hide();
        }
    }
}
