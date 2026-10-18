using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>地图点城市：详情弹窗 → 购买基地 / 再点一座城开通航线。</summary>
    public class MapCityPopup : MonoBehaviour
    {
        public static string PendingOriginIata;
        static MapCityPopup _instance;

        GameObject _cardRoot;
        GameObject _pickBar;
        TextMeshProUGUI _title;
        TextMeshProUGUI _stats;
        TextMeshProUGUI _hint;
        TextMeshProUGUI _pickLabel;
        GameObject _buyBtn;
        string _iata;

        public static MapCityPopup Ensure(Canvas canvas = null)
        {
            if (_instance != null) return _instance;
            if (canvas == null) canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null) return null;

            var go = new GameObject("MapCityPopup", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            UIFactory.ApplyAnchor(go.GetComponent<RectTransform>(), UIFactory.Fill);
            var holderImg = go.AddComponent<Image>();
            holderImg.color = new Color(0, 0, 0, 0);
            holderImg.raycastTarget = false;

            var ui = go.AddComponent<MapCityPopup>();
            ui.Build(go.transform);
            _instance = ui;
            return ui;
        }

        public static void HandleAirportClick(string iata, Transform from = null)
        {
            if (string.IsNullOrEmpty(iata)) return;
            Canvas canvas = from != null ? from.GetComponentInParent<Canvas>() : null;
            var ui = Ensure(canvas);
            if (ui == null) return;

            if (!string.IsNullOrEmpty(PendingOriginIata))
            {
                if (string.Equals(PendingOriginIata, iata, System.StringComparison.OrdinalIgnoreCase))
                {
                    ui.SetPickHint("请再点另一座城市作为目的地。");
                    return;
                }
                string origin = PendingOriginIata;
                PendingOriginIata = null;
                ui.SetPickBar(false);
                ui.HideCard();
                ui.OpenDecision(origin, iata);
                return;
            }

            ui.ShowCity(iata);
        }

        void Build(Transform root)
        {
            _cardRoot = UIFactory.Panel(root, "CityCardDim", UIFactory.Fill,
                new Color(0.02f, 0.04f, 0.08f, 0.55f), rounded: false);
            var dimBtn = _cardRoot.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener(HideCard);

            var card = UIFactory.Panel(_cardRoot.transform, "Card",
                new UIFactory.Anchor(new Vector2(0.18f, 0.32f), new Vector2(0.82f, 0.78f), Vector2.zero, Vector2.zero),
                UITheme.Panel);
            UIFactory.AddBorder(card, UITheme.Accent, new Vector2(1.2f, -1.2f));

            _title = UIFactory.Label(card.transform, "Title", "", 16,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.72f), new Vector2(0.88f, 0.96f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);
            _title.textWrappingMode = TextWrappingModes.Normal;

            _stats = UIFactory.Label(card.transform, "Stats", "", 13,
                TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.30f), new Vector2(0.96f, 0.70f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            _stats.textWrappingMode = TextWrappingModes.Normal;

            _hint = UIFactory.Label(card.transform, "Hint", "", 12,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.22f), new Vector2(0.96f, 0.30f), Vector2.zero, Vector2.zero),
                UITheme.Warning);

            _buyBtn = UIFactory.Button(card.transform, "BuyBase", "购买基地",
                new UIFactory.Anchor(new Vector2(0.04f, 0.05f), new Vector2(0.48f, 0.20f), Vector2.zero, Vector2.zero),
                UITheme.Accent, OnBuyBase, 15).gameObject;

            UIFactory.Button(card.transform, "OpenRoute", "建立航线",
                new UIFactory.Anchor(new Vector2(0.52f, 0.05f), new Vector2(0.96f, 0.20f), Vector2.zero, Vector2.zero),
                UITheme.Success, OnEstablishRoute, 15);

            _cardRoot.SetActive(false);

            _pickBar = UIFactory.Panel(root, "PickDestBar",
                new UIFactory.Anchor(new Vector2(0.18f, 0.86f), new Vector2(0.82f, 0.96f), Vector2.zero, Vector2.zero),
                UITheme.Panel);
            UIFactory.AddBorder(_pickBar, UITheme.Accent, new Vector2(1f, -1f));
            _pickLabel = UIFactory.Label(_pickBar.transform, "PickHint", "", 14,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.1f), new Vector2(0.72f, 0.9f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            UIFactory.Button(_pickBar.transform, "CancelPick", "取消",
                new UIFactory.Anchor(new Vector2(0.74f, 0.15f), new Vector2(0.96f, 0.85f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, CancelPick, 14);
            _pickBar.SetActive(false);
        }

        public void ShowCity(string iata)
        {
            _iata = iata;
            var gs = GameState.Instance;
            var v = CityEconomyLogic.Resolve(gs, iata);
            bool baseCity = CityEconomyLogic.IsCompanyBase(gs, iata);
            string tag = v.isHub ? "枢纽" : (baseCity ? "基地" : (v.served ? "已开航" : "城市"));
            if (_title != null)
                _title.text = $"{v.iata}  ·  {v.cityZh}（{v.airportZh}）  ·  {tag}  ·  指数 {v.liveIndex:F0}";
            if (_stats != null)
            {
                _stats.text =
                    $"城市生产总值 {v.gdpUsdBn * 10f:F0} 亿美元     人口 {v.popMillion * 100f:F0} 万人     " +
                    $"旅客吞吐量 {v.paxMillion * 100f:F0} 万人次     货邮吞吐量 {v.cargoWanTon:F0} 万吨     " +
                    $"起讫客流占比 {v.odIndex:F0}%";
            }
            if (_buyBtn != null) _buyBtn.SetActive(!baseCity);
            if (_hint != null)
                _hint.text = !baseCity ? $"购买基地需 {CityEconomyLogic.BuyBaseCostWan:F0} 万美元" : "";
            SetPickBar(false);
            _cardRoot.SetActive(true);
            transform.SetAsLastSibling();
        }

        public void HideCard()
        {
            if (_cardRoot != null) _cardRoot.SetActive(false);
        }

        void SetPickBar(bool on)
        {
            if (_pickBar != null) _pickBar.SetActive(on);
            if (on) transform.SetAsLastSibling();
        }

        void SetPickHint(string msg)
        {
            if (_pickLabel != null) _pickLabel.text = msg ?? "";
            SetPickBar(true);
        }

        void CancelPick()
        {
            PendingOriginIata = null;
            SetPickBar(false);
        }

        void OnBuyBase()
        {
            if (CityEconomyLogic.TryBuyBase(GameState.Instance, _iata, out string msg))
                ShowCity(_iata);
            if (_hint != null) _hint.text = msg;
        }

        void OnEstablishRoute()
        {
            PendingOriginIata = _iata;
            HideCard();
            SetPickHint($"已选起点 {_iata}，请再点击地图上另一座城市作为目的地。");
        }

        void OpenDecision(string origin, string dest)
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null) return;
            var flow = OpenRouteDecisionFlow.Ensure(canvas.transform);
            flow.Bind(
                onStateChanged: () => MapWidget.Refresh(),
                openOpsPage: pageId => OpsPanelUI.OpenOpsPage(pageId),
                gotoNav: _ => OpsPanelUI.OpenShellTab("n13_运营部"));
            flow.BeginFromMap(origin, dest);
            var modal = OpenRouteModalUI.Ensure(canvas.transform);
            if (modal == null) return;
            AirportCatalog.TryResolve(origin, out var o);
            AirportCatalog.TryResolve(dest, out var d);
            modal.Show(_ => { }, null);
            modal.ShowSubmitted(new OpenRouteModalUI.Selection
            {
                originCode = string.IsNullOrEmpty(o.code) ? origin : o.code,
                destCode = string.IsNullOrEmpty(d.code) ? dest : d.code,
                viaCode = null,
                originLabel = string.IsNullOrEmpty(o.Display) ? origin : o.Display,
                destLabel = string.IsNullOrEmpty(d.Display) ? dest : d.Display,
                viaLabel = "直飞"
            });
        }
    }
}
