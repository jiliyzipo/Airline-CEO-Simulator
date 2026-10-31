using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI.Shell
{
    /// <summary>
    /// n6 主页：空壳舞台（不展示 KPI/待办，功能入口在顶栏「顾问」）。
    /// 视觉对齐 MVP2.1「昼夜对照平面地图」：深色大陆底图 + 晨昏夜幕 + 发光大圆弧航线 + 枢纽光点 + 航班箭标。
    /// 左侧装饰栏 + 右侧地图舞台 + 左上统计 HUD + 底部图例。
    /// </summary>
    public class DashboardPanelUI : BasePanel
    {
        Image _weatherIconImg;                 // 云图标着色（开=亮蓝 / 关=灰）
        GameObject _weatherBtnGo;
        Image _routeIconImg;                   // 航线图标着色（开=亮青 / 关=灰暗）
        Image _routeBtnImg;                    // 航线按钮底色（开=青蓝底，整块亮起来）
        Outline _routeBorder;                  // 航线按钮描边（开=亮边）
        bool _routeIconOn;                     // 上一帧的图层状态，用于检测外部改动
        MapFlightCard _flightCard;             // 选中飞机后的航班卡
        GameObject _stylePopup;                // 地球按钮弹出的样式选择面板
        Image _globeIconImg;                   // 地球图标着色（弹窗开着=高亮）
        readonly Outline[] _thumbBorders = new Outline[2];  // 缩略图选中描边
        readonly GameObject[] _thumbBtns = new GameObject[2];
        GameObject _dossierOverlay;
        Transform _dossierBody;
        readonly Button[] _homeTabs = new Button[3];
        int _homeTab;
        TextMeshProUGUI _ticker;

        // 按钮序 → MapStyle 枚举：弹窗上=默认，下=卫星（简约、深色模式已移除）
        static readonly MapStyle[] StyleOrder = { MapStyle.Default, MapStyle.Satellite };

        public static DashboardPanelUI Build()
        {
            var root = UIFactory.Panel(null, "n6_主页_Dashboard", UIFactory.Fill, new Color(0f, 0f, 0f, 0f), rounded: false);
            var img = root.GetComponent<Image>();
            if (img != null) img.raycastTarget = false;

            var cg = root.AddComponent<CanvasGroup>();
            var title = UIFactory.Label(root.transform, "Title", "", 1,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            title.gameObject.SetActive(false);

            var close = UIFactory.Button(root.transform, "Close", "×",
                new UIFactory.Anchor(new Vector2(0.92f, 0.90f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 22);
            close.gameObject.SetActive(false);

            var panel = root.AddComponent<DashboardPanelUI>();
            panel.AssignRefs(root.GetComponent<RectTransform>(), title, close, cg);
            panel.BuildStage(root.transform);
            return panel;
        }

        void BuildStage(Transform root)
        {
            // 全幅地图舞台（cover 铺满 + 拖拽平移 + 滚轮缩放 + 左右无限循环由 MapWidget 处理）
            var stage = UIFactory.Panel(root, "Stage",
                new UIFactory.Anchor(Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                new Color(0.02f, 0.05f, 0.10f, 1f), rounded: false);
            stage.GetComponent<Image>().raycastTarget = false;
            var mask = stage.AddComponent<RectMask2D>();
            mask.enabled = true;

            MapWidget.Build(stage.transform);

            // 浮层挂在 Stage 内并置为最后兄弟，保证在地图之上；缩放按钮通过 Bridge 找 MapViewport
            var overlay = new GameObject("StageOverlay", typeof(RectTransform));
            overlay.transform.SetParent(stage.transform, false);
            var ort = overlay.GetComponent<RectTransform>();
            ort.anchorMin = Vector2.zero;
            ort.anchorMax = Vector2.one;
            ort.offsetMin = Vector2.zero;
            ort.offsetMax = Vector2.zero;
            overlay.transform.SetAsLastSibling();

            _ticker = UIFactory.Label(overlay.transform, "EventTicker", "", 13,
                TextAlignmentOptions.Midline,
                new UIFactory.Anchor(new Vector2(0.18f, 1f), new Vector2(0.82f, 1f),
                    new Vector2(0f, -48f), new Vector2(0f, -14f)),
                UITheme.TextPrimary);
            _ticker.textWrappingMode = TextWrappingModes.Normal;
            _ticker.overflowMode = TextOverflowModes.Truncate;
            RefreshTicker();

            var bridge = overlay.AddComponent<MapWidgetBridge>();
            bridge.vp = stage.GetComponentInChildren<MapViewport>(true);

            // 左上角统计 HUD 与底部图例条已按新主页设计移除：
            // 财务/航线/机队状态整合进底栏上方的独立状态栏（StatusBarView）

            // 右侧图层栏：横排三个正方形图标按钮（航线 / 地球=地图模式 / 云=天气开关），对齐飞常准
            // overlay 是 stage 全幅；用固定像素 sizeDelta 保证 1:1 正方形（不随分辨率比例变形）
            const float IconPx = 60f;
            const float IconGap = 8f;
            const int IconCount = 3;
            var layerBar = new GameObject("LayerBar", typeof(RectTransform));
            layerBar.transform.SetParent(overlay.transform, false);
            var lbrt = layerBar.GetComponent<RectTransform>();
            lbrt.anchorMin = lbrt.anchorMax = new Vector2(1f, 1f);  // 右上角锚点
            lbrt.pivot = new Vector2(1f, 1f);
            lbrt.anchoredPosition = new Vector2(-14f, -14f);        // 右上留 14px 边距
            lbrt.sizeDelta = new Vector2(IconCount * IconPx + (IconCount - 1) * IconGap, IconPx);
            layerBar.AddComponent<CanvasRenderer>();

            // 航线按钮：最左。★2026-10-30 CEO 要求——地图默认只有飞机在动，点了才显示航线
            var routeBtn = UIFactory.IconButton(layerBar.transform, "Routes", UIFactory.RouteSprite,
                PixelSlot(0, IconPx, IconGap),
                RouteBtnBg, ToggleRoutes, RouteBtnOffIcon);
            _routeBorder = UIFactory.AddBorder(routeBtn.gameObject, UITheme.Border, new Vector2(1f, -1f));
            _routeBtnImg = routeBtn.GetComponent<Image>();
            _routeIconImg = routeBtn.transform.Find("Icon").GetComponent<Image>();

            // 地球按钮：中，点击弹出样式选择面板（默认/卫星缩略图）
            var globeBtn = UIFactory.IconButton(layerBar.transform, "Globe", UIFactory.GlobeSprite,
                PixelSlot(1, IconPx, IconGap),
                new Color(0.03f, 0.08f, 0.15f, 0.88f), ToggleStylePopup, new Color(0.75f, 0.88f, 1f, 1f));
            UIFactory.AddBorder(globeBtn.gameObject, UITheme.Border, new Vector2(1f, -1f));
            _globeIconImg = globeBtn.transform.Find("Icon").GetComponent<Image>();

            // 云按钮：右，点击直接开关天气
            var weatherBtn = UIFactory.IconButton(layerBar.transform, "Weather", UIFactory.CloudSprite,
                PixelSlot(2, IconPx, IconGap),
                new Color(0.03f, 0.08f, 0.15f, 0.88f), ToggleWeather, new Color(0.75f, 0.92f, 1f, 1f));
            UIFactory.AddBorder(weatherBtn.gameObject, UITheme.Border, new Vector2(1f, -1f));
            _weatherIconImg = weatherBtn.transform.Find("Icon").GetComponent<Image>();
            _weatherBtnGo = weatherBtn.gameObject;

            // 样式选择弹窗：挂到地球按钮下（相对定位，与地球同尺寸）
            BuildStylePopup(globeBtn.transform, IconPx, IconGap);
            RefreshStyleHighlight();
            RefreshWeatherIcon();
            RefreshRouteIcon();

            BuildHomeTabs(overlay.transform);

            // 航班卡：选中地图上的飞机后弹出（对齐 Airline Sim 的底部航班条）。
            // 挂在 overlay 最后，压在地图与按钮之上。
            _flightCard = MapFlightCard.Build(overlay.transform);

            // 右下角顾问气泡已并入画布级 AdvisorModalUI（收起=图标，展开=同一矩形伸展成弹窗）。
        }

        /// <summary>
        /// 图层栏里第 index 个按钮的锚位（左下角起按固定像素排）。
        /// 不用「anchorMin=0 / anchorMax=1/3」那种均分写法：栏宽里含 2 个间距，
        /// 均分后中间那个会比两侧窄 4 px（肉眼看得出来大小不齐）。这里直接给像素。
        /// </summary>
        static UIFactory.Anchor PixelSlot(int index, float px, float gap)
        {
            float x = index * (px + gap);
            return new UIFactory.Anchor(Vector2.zero, Vector2.zero,
                new Vector2(x, 0f), new Vector2(x + px, px));
        }

        void BuildHomeTabs(Transform overlay)
        {
            var bar = UIFactory.Panel(overlay, "HomeTabs",
                new UIFactory.Anchor(new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(14f, -50f), new Vector2(380f, -14f)),
                new Color(0.04f, 0.10f, 0.18f, 0.88f));
            UIFactory.AddBorder(bar, UITheme.Border, new Vector2(1f, -1f));

            string[] labels = { "地图", "个人资料", "合同" };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                float x0 = i / 3f;
                float x1 = (i + 1) / 3f;
                var btn = UIFactory.Button(bar.transform, "HT_" + labels[i], labels[i],
                    new UIFactory.Anchor(new Vector2(x0 + 0.01f, 0.12f), new Vector2(x1 - 0.01f, 0.88f),
                        Vector2.zero, Vector2.zero),
                    UITheme.NavIdle, () => SetHomeTab(idx), 13);
                _homeTabs[i] = btn;
            }

            _dossierOverlay = UIFactory.Panel(overlay, "DossierOverlay",
                new UIFactory.Anchor(new Vector2(0.04f, 0.08f), new Vector2(0.72f, 0.86f), Vector2.zero, Vector2.zero),
                UITheme.Panel);
            UIFactory.AddBorder(_dossierOverlay, UITheme.Border, new Vector2(1.2f, -1.2f));
            _dossierBody = _dossierOverlay.transform;
            _dossierOverlay.SetActive(false);
            SetHomeTab(0);
        }

        void SetHomeTab(int idx)
        {
            _homeTab = Mathf.Clamp(idx, 0, 2);
            for (int i = 0; i < _homeTabs.Length; i++)
            {
                if (_homeTabs[i] == null) continue;
                var img = _homeTabs[i].GetComponent<Image>();
                if (img != null) img.color = i == _homeTab ? UITheme.NavActive : UITheme.NavIdle;
            }

            bool showDossier = _homeTab > 0;
            if (_dossierOverlay != null) _dossierOverlay.SetActive(showDossier);
            if (showDossier && _dossierBody != null)
                CeoDossierPanel.Fill(_dossierBody, _homeTab == 2);
        }

        void SetStyle(MapStyle s)
        {
            MapWidget.SetStyle(s);
            RefreshStyleHighlight();
        }

        void RefreshStyleHighlight()
        {
            var cur = MapTileLayer.Style;
            for (int i = 0; i < 2; i++)
            {
                if (_thumbBtns[i] == null) continue;
                // 选中项：描边亮蓝；未选中：暗边
                if (_thumbBorders[i] != null)
                    _thumbBorders[i].effectColor = (StyleOrder[i] == cur)
                        ? new Color(0.35f, 0.70f, 1f, 1f)
                        : UITheme.Border;
            }
        }

        /// <summary>地球按钮：弹出/收起样式选择面板。</summary>
        void ToggleStylePopup()
        {
            if (_stylePopup == null) return;
            bool show = !_stylePopup.activeSelf;
            _stylePopup.SetActive(show);
            RefreshStyleHighlight();
        }

        /// <summary>
        /// 样式选择弹窗：竖排两张裸缩略图（默认/卫星），无卡片底、无文字。
        /// 尺寸与地球/云按钮一致；挂在地球按钮下，与地球/两图之间等距。
        /// </summary>
        void BuildStylePopup(Transform globeBtn, float iconPx, float gapPx)
        {
            // 相对地球按钮定位：锚在地球底边中心，弹窗 pivot 在顶边中心
            // y = -gapPx：地球底边与第一张缩略图顶边留 gap，与两图间距、地球↔天气横向间距一致（等距）
            var popupGo = new GameObject("StylePopup", typeof(RectTransform));
            popupGo.transform.SetParent(globeBtn, false);
            var prt = popupGo.GetComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0f); // 地球按钮底边中心
            prt.pivot = new Vector2(0.5f, 1f);                     // 弹窗顶边中心
            prt.anchoredPosition = new Vector2(0f, -gapPx);
            prt.sizeDelta = new Vector2(iconPx, 2f * iconPx + gapPx);
            _stylePopup = popupGo;

            for (int i = 0; i < 2; i++)
            {
                int idx = i;
                // 裸缩略图：正方形，无背景框（图本身就是内容）
                var picGo = new GameObject("Thumb_" + i, typeof(RectTransform));
                picGo.transform.SetParent(popupGo.transform, false);
                var prt2 = picGo.GetComponent<RectTransform>();
                // 必须锚在父级顶边：默认锚点是中心，y=0 会落在弹窗中部 → 第一张图上方空出约半个弹窗高度
                prt2.anchorMin = prt2.anchorMax = new Vector2(0.5f, 1f);
                prt2.pivot = new Vector2(0.5f, 1f);
                prt2.anchoredPosition = new Vector2(0f, -i * (iconPx + gapPx));
                prt2.sizeDelta = new Vector2(iconPx, iconPx);
                var tImg = picGo.AddComponent<Image>();
                tImg.sprite = UIFactory.LoadSprite(idx == 0 ? "UI/Icons/MapDefault" : "UI/Icons/MapSatellite");
                tImg.type = Image.Type.Simple;
                tImg.preserveAspect = true;
                tImg.raycastTarget = true; // 整图可点

                // 点击切换样式并收起
                var btn = picGo.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() =>
                {
                    SetStyle(StyleOrder[idx]);
                    if (_stylePopup != null) _stylePopup.SetActive(false); // 选完即收
                });
                _thumbBtns[idx] = picGo;

                // 选中描边（Outline，effectColor 切换高亮/暗）
                var border = picGo.AddComponent<Outline>();
                border.effectColor = UITheme.Border;
                border.effectDistance = new Vector2(2f, -2f);
                _thumbBorders[idx] = border;
            }
            popupGo.SetActive(false);
        }

        void ToggleWeather()
        {
            MapWeatherLayer.SetEnabled(!MapWeatherLayer.Enabled);
            RefreshWeatherIcon();
        }

        /// <summary>
        /// 航线按钮的两套配色。★2026-10-30 CEO（第二次）：「整块亮度不够，要和旁边的地图按钮一样的亮度」。
        ///
        /// 上一版「开」态把底色刷成青蓝 (0.06,0.30,0.46)、图标刷成淡青 —— 实测采样（截图像素）：
        /// 底色 (33,85,104)、图标最亮处只有 (52,99,116)，而旁边的地球按钮是深藏青 (18,33,53)
        /// 配亮白图标 (189,221,252)。**亮度差全在图标上**：青蓝底把淡青图标吃掉了，
        /// 整块看着比邻居更暗更浑。
        ///
        /// 现在与邻居完全一致：底色固定为同一个深藏青（不开/关两套底色），
        /// 状态只由**图标亮度**表达 —— 关=中灰、开=接近纯白。另外图标线稿加粗过
        /// （见 <see cref="UIFactory.RouteSprite"/>），否则 60 px 上细线再亮也只是一道浅痕。
        /// </summary>
        static readonly Color RouteBtnBg = new Color(0.03f, 0.08f, 0.15f, 0.88f);      // 与地球/云按钮同一色
        static readonly Color RouteBtnOnIcon = new Color(0.92f, 0.97f, 1f, 1f);
        static readonly Color RouteBtnOffIcon = new Color(0.55f, 0.63f, 0.71f, 0.9f);
        static readonly Color RouteBtnOnBorder = new Color(0.42f, 0.86f, 1f, 1f);

        /// <summary>航线图层开关：地图默认不画航线，点一下才显示（对齐 Airline Sim 的地图图层）。</summary>
        void ToggleRoutes()
        {
            MapWidget.ToggleRoutes();
            RefreshRouteIcon();
        }

        /// <summary>把按钮外观同步成 <see cref="MapWidget.RoutesVisible"/> 的当前值。</summary>
        void RefreshRouteIcon()
        {
            bool on = MapWidget.RoutesVisible;
            if (_routeIconImg != null) _routeIconImg.color = on ? RouteBtnOnIcon : RouteBtnOffIcon;
            if (_routeBtnImg != null) _routeBtnImg.color = RouteBtnBg;   // 底色恒定，与邻居一致
            if (_routeBorder != null) _routeBorder.effectColor = on ? RouteBtnOnBorder : UITheme.Border;
        }

        /// <summary>
        /// 每帧对一次按钮外观。**这不是防御性代码，是在修一个真实的失同步**：
        /// <see cref="MapWidget.RoutesVisible"/> 是静态的，可能被别处（快捷键、地图内部）改动；
        /// 只在点击回调里刷新，就会出现「图层其实开着、按钮还是灭的」。
        /// 每帧比较一次布尔值，代价可以忽略，状态却永远不会骗人。
        /// </summary>
        void Update()
        {
            if (MapWidget.RoutesVisible != _routeIconOn)
            {
                _routeIconOn = MapWidget.RoutesVisible;
                RefreshRouteIcon();
            }
        }

        void RefreshWeatherIcon()
        {
            // 开=亮蓝（与云图呼应）；关=灰暗
            if (_weatherIconImg != null)
                _weatherIconImg.color = MapWeatherLayer.Enabled
                    ? new Color(0.45f, 0.85f, 1f, 1f)
                    : new Color(0.5f, 0.58f, 0.66f, 0.75f);
        }

        // 地图源切换已移除：全局只用 Google 源（MapTileLayer._source 默认 Google）
        // SourceLabel/ToggleSource/_sourceLabel 一并删除；若日后恢复高德，参照 git 历史找回

        void OnEnable()
        {
            MapWidget.Refresh();
            MapWidget.RefreshTerminator();
            RefreshWeatherIcon();  // 重建层级后图标颜色可能未同步开关状态
            _routeIconOn = MapWidget.RoutesVisible;
            RefreshRouteIcon();
            MapSelection.Changed -= RefreshFlightCard;
            MapSelection.Changed += RefreshFlightCard;
            RefreshFlightCard();
            var gs = GameState.Instance;
            if (gs != null) gs.OnStateChanged += OnStateChanged;
            if (TimeManager.Instance != null)
            {
                TimeManager.Instance.OnClockTick -= OnClockTick;
                TimeManager.Instance.OnClockTick += OnClockTick;
            }
            ExternalEventManager.Changed -= RefreshTicker;
            ExternalEventManager.Changed += RefreshTicker;
            RefreshTicker();
        }

        void OnDisable()
        {
            var gs = GameState.Instance;
            if (gs != null) gs.OnStateChanged -= OnStateChanged;
            if (TimeManager.Instance != null)
                TimeManager.Instance.OnClockTick -= OnClockTick;
            ExternalEventManager.Changed -= RefreshTicker;
            MapSelection.Changed -= RefreshFlightCard;
            if (_flightCard != null) _flightCard.Hide();
        }

        /// <summary>
        /// 时钟每走一格：刷新滚动条 + 卡片的进度。
        ///
        /// 卡片必须挂在这里，而不是只在 <see cref="OnStateChanged"/> 里：时钟前进不一定会触发存档变更
        /// （没有新邮件、没有现金落账），只靠状态变更刷新，卡片上的进度条会停在点击那一刻不动 ——
        /// 那正好是玩家最会盯着它看的场景（「这一班还有多久落地」）。
        /// </summary>
        void OnClockTick()
        {
            RefreshTicker();
            if (MapSelection.Has) RefreshFlightCard();
        }

        /// <summary>
        /// 选中变化 → 开关航班卡。订阅放在 OnEnable/OnDisable 成对出现：
        /// 静态事件不做对称退订，切页几次之后每帧会同时刷新好几张已销毁的卡（MissingReference）。
        /// </summary>
        void RefreshFlightCard()
        {
            if (_flightCard == null) return;
            if (MapSelection.Has) _flightCard.Refresh();
            else _flightCard.Hide();
        }

        void OnStateChanged()
        {
            MapWidget.Refresh();
            RefreshTicker();
            // 卡片上的进度条/时刻要跟着时钟走：只在选中变化时刷新，会让卡片停在点击那一刻的数据。
            if (MapSelection.Has) RefreshFlightCard();
            if (_dossierOverlay != null && _dossierOverlay.activeSelf && _dossierBody != null)
                CeoDossierPanel.Fill(_dossierBody, _homeTab == 2);
        }

        void RefreshTicker()
        {
            if (_ticker == null) return;
            string text = ExternalEventManager.TickerText(GameState.Instance);
            _ticker.text = string.IsNullOrEmpty(text) ? "" : text;
            _ticker.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }

        public override void Show()
        {
            base.Show();
            MapWidget.Refresh();
            SetHomeTab(0);
        }
    }
}
