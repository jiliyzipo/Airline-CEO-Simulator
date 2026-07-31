using System.Collections;
using AirlineCEO.AI;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace AirlineCEO.UI
{
    /// <summary>
    /// 第 6 章 MVP：运行时搭建 UGUI「首航体验」界面，并绑定逻辑。
    /// 挂到场景空物体即可 Play。
    /// </summary>
    public class MVPBootstrap : MonoBehaviour
    {
        private const float FirstFlightReward = 15f; // 万美元
        private const float FirstFlightSeconds = 5f;

        private Font _font;
        private Text _dateText;
        private Text _cashText;
        private Text _runwayText;
        private Image _runwayFill;
        private Text _statusText;
        private Text _popupText;
        private GameObject _popupRoot;
        private GameObject _progressRoot;
        private Image _progressFill;
        private Text _progressLabel;
        private GameObject _settleRoot;
        private Text _settleText;
        private Button _firstFlightBtn;
        private InputField _originInput;
        private InputField _destInput;
        private Text _rpmResultText;
        private Image _rpmLight;
        private InputField _chatInput;
        private Text _chatOutput;
        private bool _flightInProgress;

        private void Awake()
        {
            EnsureCoreSystems();
            EnsureEventSystem();
            _font = ResolveFont();
            BuildUI();
            RefreshHud();

            if (GameState.Instance != null)
                GameState.Instance.OnStateChanged += RefreshHud;
        }

        private void OnDestroy()
        {
            if (GameState.Instance != null)
                GameState.Instance.OnStateChanged -= RefreshHud;
        }

        private void EnsureCoreSystems()
        {
            if (GameState.Instance == null)
            {
                var go = new GameObject("GameState");
                go.AddComponent<GameState>();
            }

            if (SaveManager.Instance == null)
            {
                var go = new GameObject("SaveManager");
                go.AddComponent<SaveManager>();
            }

            if (DataBase.Instance == null)
            {
                var go = new GameObject("DataBase");
                go.AddComponent<DataBase>();
            }

            if (AIChatManager.Instance == null)
            {
                var go = new GameObject("AIChatManager");
                go.AddComponent<AIChatManager>();
            }
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<InputSystemUIInputModule>();
#else
            es.AddComponent<StandaloneInputModule>();
#endif
        }

        private static Font ResolveFont()
        {
            var font = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Arial Unicode MS", "Arial" },
                24);
            return font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private void BuildUI()
        {
            var canvasGo = new GameObject("MVP_Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // 背景
            CreatePanel(canvasGo.transform, "Background",
                AnchorFill(), new Color(0.08f, 0.12f, 0.18f, 1f));

            BuildTopBar(canvasGo.transform);
            BuildMapArea(canvasGo.transform);
            BuildRpmPanel(canvasGo.transform);
            BuildChatPanel(canvasGo.transform);
            BuildDevButtons(canvasGo.transform);
            BuildPopup(canvasGo.transform);
            BuildProgress(canvasGo.transform);
            BuildSettle(canvasGo.transform);
        }

        private void BuildTopBar(Transform parent)
        {
            var bar = CreatePanel(parent, "TopBar",
                new AnchorData(new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -90), new Vector2(0, 0)),
                new Color(0.12f, 0.18f, 0.28f, 0.95f));

            _dateText = CreateText(bar.transform, "DateText", "第 1 年 Q1", 28,
                TextAnchor.MiddleLeft, new AnchorData(new Vector2(0, 0), new Vector2(0.28f, 1), Vector2.zero, Vector2.zero),
                new Color(0.9f, 0.95f, 1f));

            _cashText = CreateText(bar.transform, "CashText", "现金：500 万", 28,
                TextAnchor.MiddleCenter, new AnchorData(new Vector2(0.28f, 0), new Vector2(0.55f, 1), Vector2.zero, Vector2.zero),
                new Color(0.6f, 1f, 0.7f));

            // 跑道油量表
            var runwayLabel = CreateText(bar.transform, "RunwayLabel", "现金跑道", 16,
                TextAnchor.MiddleLeft, new AnchorData(new Vector2(0.55f, 0.55f), new Vector2(0.68f, 1), Vector2.zero, Vector2.zero),
                Color.white);

            var track = CreatePanel(bar.transform, "RunwayTrack",
                new AnchorData(new Vector2(0.58f, 0.2f), new Vector2(0.88f, 0.5f), Vector2.zero, Vector2.zero),
                new Color(0.2f, 0.25f, 0.3f, 1f));
            _runwayFill = CreatePanel(track.transform, "RunwayFill",
                new AnchorData(new Vector2(0, 0), new Vector2(0.5f, 1), Vector2.zero, Vector2.zero),
                new Color(0.3f, 0.85f, 0.4f, 1f)).GetComponent<Image>();
            _runwayFill.type = Image.Type.Filled;
            _runwayFill.fillMethod = Image.FillMethod.Horizontal;
            _runwayFill.fillAmount = 0.5f;

            _runwayText = CreateText(bar.transform, "RunwayText", "流动性还可以撑 — 个月", 15,
                TextAnchor.MiddleLeft, new AnchorData(new Vector2(0.68f, 0), new Vector2(0.98f, 0.55f), Vector2.zero, Vector2.zero),
                Color.white);

            _statusText = CreateText(bar.transform, "StatusText", "F5 存档 · F9 读档", 16,
                TextAnchor.MiddleRight, new AnchorData(new Vector2(0.72f, 0.55f), new Vector2(0.98f, 0.95f), Vector2.zero, Vector2.zero),
                new Color(0.7f, 0.75f, 0.8f));
        }

        private void BuildMapArea(Transform parent)
        {
            var map = CreatePanel(parent, "MapArea",
                new AnchorData(new Vector2(0.05f, 0.22f), new Vector2(0.58f, 0.88f), Vector2.zero, Vector2.zero),
                new Color(0.15f, 0.22f, 0.30f, 0.9f));

            CreateText(map.transform, "MapTitle", "航线地图（占位）", 26,
                TextAnchor.UpperCenter, new AnchorData(new Vector2(0, 0.85f), new Vector2(1, 1), Vector2.zero, Vector2.zero),
                new Color(0.75f, 0.85f, 0.95f));

            CreateText(map.transform, "MapHint", "首航体验：完成第一班飞行，入账 15 万美元", 20,
                TextAnchor.MiddleCenter, new AnchorData(new Vector2(0.1f, 0.55f), new Vector2(0.9f, 0.75f), Vector2.zero, Vector2.zero),
                new Color(0.65f, 0.7f, 0.75f));

            _firstFlightBtn = CreateButton(map.transform, "FirstFlightButton", "确认首航",
                new AnchorData(new Vector2(0.3f, 0.25f), new Vector2(0.7f, 0.42f), Vector2.zero, Vector2.zero),
                new Color(0.15f, 0.55f, 0.85f, 1f), OnFirstFlightClicked);
        }

        private void BuildRpmPanel(Transform parent)
        {
            var panel = CreatePanel(parent, "RpmPanel",
                new AnchorData(new Vector2(0.62f, 0.38f), new Vector2(0.97f, 0.88f), Vector2.zero, Vector2.zero),
                new Color(0.14f, 0.18f, 0.24f, 0.95f));

            CreateText(panel.transform, "RpmTitle", "RPM 航线分析", 24,
                TextAnchor.UpperLeft, new AnchorData(new Vector2(0.05f, 0.88f), new Vector2(0.95f, 0.98f), Vector2.zero, Vector2.zero),
                Color.white);

            CreateText(panel.transform, "OriginLabel", "出发", 18,
                TextAnchor.MiddleLeft, new AnchorData(new Vector2(0.05f, 0.72f), new Vector2(0.25f, 0.84f), Vector2.zero, Vector2.zero),
                new Color(0.8f, 0.8f, 0.8f));
            _originInput = CreateInput(panel.transform, "OriginInput", "北京",
                new AnchorData(new Vector2(0.28f, 0.72f), new Vector2(0.95f, 0.84f), Vector2.zero, Vector2.zero));

            CreateText(panel.transform, "DestLabel", "到达", 18,
                TextAnchor.MiddleLeft, new AnchorData(new Vector2(0.05f, 0.56f), new Vector2(0.25f, 0.68f), Vector2.zero, Vector2.zero),
                new Color(0.8f, 0.8f, 0.8f));
            _destInput = CreateInput(panel.transform, "DestInput", "伦敦",
                new AnchorData(new Vector2(0.28f, 0.56f), new Vector2(0.95f, 0.68f), Vector2.zero, Vector2.zero));

            CreateButton(panel.transform, "AnalyzeButton", "分析",
                new AnchorData(new Vector2(0.2f, 0.38f), new Vector2(0.55f, 0.5f), Vector2.zero, Vector2.zero),
                new Color(0.2f, 0.45f, 0.35f, 1f), OnAnalyzeRpm);

            _rpmLight = CreatePanel(panel.transform, "RpmLight",
                new AnchorData(new Vector2(0.65f, 0.38f), new Vector2(0.85f, 0.5f), Vector2.zero, Vector2.zero),
                Color.gray).GetComponent<Image>();

            _rpmResultText = CreateText(panel.transform, "RpmResult", "点击分析查看绿/黄/红灯", 18,
                TextAnchor.UpperLeft, new AnchorData(new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.34f), Vector2.zero, Vector2.zero),
                new Color(0.85f, 0.9f, 0.95f));
            _rpmResultText.alignment = TextAnchor.UpperLeft;
        }

        private void BuildChatPanel(Transform parent)
        {
            var panel = CreatePanel(parent, "ChatPanel",
                new AnchorData(new Vector2(0.62f, 0.05f), new Vector2(0.97f, 0.35f), Vector2.zero, Vector2.zero),
                new Color(0.12f, 0.16f, 0.22f, 0.95f));

            CreateText(panel.transform, "ChatTitle", "Neural Command（DeepSeek / 本地容灾）", 18,
                TextAnchor.UpperLeft, new AnchorData(new Vector2(0.04f, 0.82f), new Vector2(0.96f, 0.98f), Vector2.zero, Vector2.zero),
                Color.white);

            _chatOutput = CreateText(panel.transform, "ChatOutput",
                "战略顾问：输入指令后发送。已配置 Key 走 DeepSeek；否则自动本地话术。", 16,
                TextAnchor.UpperLeft, new AnchorData(new Vector2(0.04f, 0.35f), new Vector2(0.96f, 0.8f), Vector2.zero, Vector2.zero),
                new Color(0.8f, 0.88f, 0.8f));
            _chatOutput.alignment = TextAnchor.UpperLeft;

            _chatInput = CreateInput(panel.transform, "ChatInput", "输入指令…",
                new AnchorData(new Vector2(0.04f, 0.08f), new Vector2(0.68f, 0.28f), Vector2.zero, Vector2.zero));

            CreateButton(panel.transform, "SendButton", "发送",
                new AnchorData(new Vector2(0.72f, 0.08f), new Vector2(0.96f, 0.28f), Vector2.zero, Vector2.zero),
                new Color(0.25f, 0.4f, 0.55f, 1f), OnChatSend);
        }

        private void BuildDevButtons(Transform parent)
        {
            string[] labels = { "机队", "董事会", "融资", "市场" };
            float x0 = 0.05f;
            float w = 0.12f;
            for (int i = 0; i < labels.Length; i++)
            {
                float x = x0 + i * (w + 0.02f);
                string name = labels[i];
                CreateButton(parent, $"Dev_{name}", name,
                    new AnchorData(new Vector2(x, 0.08f), new Vector2(x + w, 0.16f), Vector2.zero, Vector2.zero),
                    new Color(0.25f, 0.28f, 0.32f, 1f),
                    () => ShowPopup($"「{name}」开发中"));
            }
        }

        private void BuildPopup(Transform parent)
        {
            _popupRoot = CreatePanel(parent, "Popup",
                new AnchorData(new Vector2(0.3f, 0.4f), new Vector2(0.7f, 0.6f), Vector2.zero, Vector2.zero),
                new Color(0.1f, 0.1f, 0.12f, 0.96f)).gameObject;
            _popupText = CreateText(_popupRoot.transform, "PopupText", "开发中", 28,
                TextAnchor.MiddleCenter, AnchorFill(), Color.white);
            CreateButton(_popupRoot.transform, "PopupClose", "关闭",
                new AnchorData(new Vector2(0.35f, 0.1f), new Vector2(0.65f, 0.32f), Vector2.zero, Vector2.zero),
                new Color(0.4f, 0.4f, 0.45f, 1f), () => _popupRoot.SetActive(false));
            _popupRoot.SetActive(false);
        }

        private void BuildProgress(Transform parent)
        {
            _progressRoot = CreatePanel(parent, "FlightProgress",
                new AnchorData(new Vector2(0.25f, 0.42f), new Vector2(0.55f, 0.58f), Vector2.zero, Vector2.zero),
                new Color(0.08f, 0.1f, 0.14f, 0.95f)).gameObject;

            _progressLabel = CreateText(_progressRoot.transform, "ProgressLabel", "起飞中…", 22,
                TextAnchor.UpperCenter, new AnchorData(new Vector2(0, 0.6f), new Vector2(1, 1), Vector2.zero, Vector2.zero),
                Color.white);

            var track = CreatePanel(_progressRoot.transform, "Track",
                new AnchorData(new Vector2(0.1f, 0.25f), new Vector2(0.9f, 0.45f), Vector2.zero, Vector2.zero),
                new Color(0.2f, 0.25f, 0.3f));
            _progressFill = CreatePanel(track.transform, "Fill",
                AnchorFill(), new Color(0.2f, 0.7f, 1f)).GetComponent<Image>();
            _progressFill.type = Image.Type.Filled;
            _progressFill.fillMethod = Image.FillMethod.Horizontal;
            _progressFill.fillAmount = 0f;

            _progressRoot.SetActive(false);
        }

        private void BuildSettle(Transform parent)
        {
            _settleRoot = CreatePanel(parent, "SettlePopup",
                new AnchorData(new Vector2(0.28f, 0.35f), new Vector2(0.58f, 0.65f), Vector2.zero, Vector2.zero),
                new Color(0.1f, 0.15f, 0.12f, 0.97f)).gameObject;

            _settleText = CreateText(_settleRoot.transform, "SettleText", "首航成功！赚了 15 万", 26,
                TextAnchor.MiddleCenter, new AnchorData(new Vector2(0.05f, 0.35f), new Vector2(0.95f, 0.9f), Vector2.zero, Vector2.zero),
                new Color(0.7f, 1f, 0.75f));

            CreateButton(_settleRoot.transform, "SettleClose", "确认",
                new AnchorData(new Vector2(0.3f, 0.1f), new Vector2(0.7f, 0.28f), Vector2.zero, Vector2.zero),
                new Color(0.2f, 0.55f, 0.35f, 1f), () => _settleRoot.SetActive(false));

            _settleRoot.SetActive(false);
        }

        private void RefreshHud()
        {
            var gs = GameState.Instance;
            if (gs == null) return;

            if (_dateText != null) _dateText.text = gs.DateLabel;
            if (_cashText != null) _cashText.text = $"现金：{gs.cash:F0} 万美元";

            float months = CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.monthlyBurnRate);
            string warn = CashRunnelCalculator.GetRunwayWarning(months);
            if (_runwayText != null)
                _runwayText.text = $"流动性还可以撑 {months:F1} 个月 · {CashRunnelCalculator.FormatWarningPlain(months)}";

            if (_runwayFill != null)
            {
                // 6 个月满格
                _runwayFill.fillAmount = Mathf.Clamp01(months / 6f);
                _runwayFill.color = warn switch
                {
                    "红" => new Color(0.9f, 0.25f, 0.25f),
                    "黄" => new Color(0.95f, 0.75f, 0.2f),
                    _ => new Color(0.3f, 0.85f, 0.4f)
                };
            }

            if (_firstFlightBtn != null)
            {
                var label = _firstFlightBtn.GetComponentInChildren<Text>();
                if (gs.firstFlightCompleted)
                {
                    _firstFlightBtn.interactable = false;
                    if (label != null) label.text = "首航已完成";
                }
            }

            if (_statusText != null && SaveManager.Instance != null)
                _statusText.text = SaveManager.Instance.LastStatus;
        }

        private void OnFirstFlightClicked()
        {
            var gs = GameState.Instance;
            if (gs == null || gs.firstFlightCompleted || _flightInProgress) return;
            StartCoroutine(FirstFlightRoutine());
        }

        private IEnumerator FirstFlightRoutine()
        {
            _flightInProgress = true;
            if (_firstFlightBtn != null) _firstFlightBtn.interactable = false;
            _progressRoot.SetActive(true);
            _progressFill.fillAmount = 0f;

            float t = 0f;
            while (t < FirstFlightSeconds)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / FirstFlightSeconds);
                _progressFill.fillAmount = p;
                _progressLabel.text = $"起飞中… {Mathf.CeilToInt((FirstFlightSeconds - t))}s";
                yield return null;
            }

            _progressRoot.SetActive(false);

            var gs = GameState.Instance;
            gs.firstFlightCompleted = true;
            gs.AddCash(FirstFlightReward);

            _settleText.text = $"首航成功！\n本班结算赚了 {FirstFlightReward:F0} 万美元\n当前现金：{gs.cash:F0} 万";
            _settleRoot.SetActive(true);
            _flightInProgress = false;
            RefreshHud();
        }

        private void OnAnalyzeRpm()
        {
            string origin = _originInput != null ? _originInput.text.Trim() : "";
            string dest = _destInput != null ? _destInput.text.Trim() : "";

            // MVP：识别北京-伦敦；其他城市给占位提示但仍可算一条默认长线
            bool isPekLhr =
                (origin.Contains("北京") || origin.ToUpperInvariant().Contains("PEK")) &&
                (dest.Contains("伦敦") || dest.ToUpperInvariant().Contains("LHR") || dest.ToUpperInvariant().Contains("LON"));

            var input = new RPMCalculator.RPMInput
            {
                distance = isPekLhr ? 8150f : 4500f,
                fuelPricePerTon = 850f,
                competitorSupply = isPekLhr ? 0.65f : 0.4f,
                demandMultiplier = 1.2f,
                brandEquity = 55f
            };

            var output = RPMCalculator.Calculate(input);
            _rpmResultText.text =
                $"{origin} → {dest}\n" +
                $"评分 {output.score}（{output.rating}）\n" +
                $"日收入 {output.dailyRevenue:F2} / 成本 {output.dailyCost:F2}\n" +
                $"日利润 {output.dailyProfit:F2} 万美元" +
                (isPekLhr ? "" : "\n（非预设 OD，使用简化参数）");

            _rpmLight.color = output.rating switch
            {
                "绿" => new Color(0.25f, 0.85f, 0.35f),
                "黄" => new Color(0.95f, 0.8f, 0.2f),
                _ => new Color(0.9f, 0.25f, 0.25f)
            };
        }

        private void OnChatSend()
        {
            string msg = _chatInput != null ? _chatInput.text : "";
            if (_chatInput != null) _chatInput.text = "";

            if (AIChatManager.Instance == null)
            {
                _chatOutput.text = NeuralCommandMatcher.Reply(msg);
                return;
            }

            if (AIChatManager.Instance.IsBusy)
            {
                _chatOutput.text = "战略顾问：上一条还在处理，请稍候。";
                return;
            }

            _chatOutput.text = "战略顾问：思考中…";
            AIChatManager.Instance.Ask(msg, response =>
            {
                if (_chatOutput == null || response == null) return;
                string text = response.ToDisplayText();
                if (response.fromFallback && !string.IsNullOrEmpty(response.errorHint))
                    text += $"\n（{response.errorHint}）";
                _chatOutput.text = text;

                // 可选：根据 suggestedAction 做轻量反馈
                var action = SuggestedActionParser.Parse(response.suggestedAction);
                if (action == SuggestedAction.OpenRoute && _rpmResultText != null)
                {
                    // 不强制改 RPM 面板，仅在聊天中已含数值
                }
            });
        }

        private void ShowPopup(string message)
        {
            _popupText.text = message;
            _popupRoot.SetActive(true);
        }

        // ---------- UI helpers ----------

        private struct AnchorData
        {
            public Vector2 anchorMin, anchorMax, offsetMin, offsetMax;
            public AnchorData(Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
            {
                anchorMin = min; anchorMax = max; offsetMin = offMin; offsetMax = offMax;
            }
        }

        private static AnchorData AnchorFill() =>
            new AnchorData(Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        private GameObject CreatePanel(Transform parent, string name, AnchorData a, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            ApplyAnchor(go.GetComponent<RectTransform>(), a);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = true;
            return go;
        }

        private Text CreateText(Transform parent, string name, string content, int size,
            TextAnchor align, AnchorData a, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            ApplyAnchor(go.GetComponent<RectTransform>(), a);
            var text = go.GetComponent<Text>();
            text.font = _font;
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.alignment = align;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private InputField CreateInput(Transform parent, string name, string placeholder, AnchorData a)
        {
            var go = CreatePanel(parent, name, a, new Color(0.18f, 0.22f, 0.28f, 1f));
            var text = CreateText(go.transform, "Text", "", 18, TextAnchor.MiddleLeft,
                new AnchorData(new Vector2(0.04f, 0), new Vector2(0.96f, 1), Vector2.zero, Vector2.zero), Color.white);
            text.supportRichText = false;
            var ph = CreateText(go.transform, "Placeholder", placeholder, 18, TextAnchor.MiddleLeft,
                new AnchorData(new Vector2(0.04f, 0), new Vector2(0.96f, 1), Vector2.zero, Vector2.zero),
                new Color(1, 1, 1, 0.35f));

            var input = go.AddComponent<InputField>();
            input.textComponent = text;
            input.placeholder = ph;
            input.text = placeholder.Contains("…") ? "" : placeholder;
            return input;
        }

        private Button CreateButton(Transform parent, string name, string label, AnchorData a,
            Color color, UnityEngine.Events.UnityAction onClick)
        {
            var go = CreatePanel(parent, name, a, color);
            CreateText(go.transform, "Label", label, 22, TextAnchor.MiddleCenter, AnchorFill(), Color.white);
            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = color * 1.15f;
            colors.pressedColor = color * 0.85f;
            btn.colors = colors;
            btn.onClick.AddListener(onClick);
            return btn;
        }

        private static void ApplyAnchor(RectTransform rt, AnchorData a)
        {
            rt.anchorMin = a.anchorMin;
            rt.anchorMax = a.anchorMax;
            rt.offsetMin = a.offsetMin;
            rt.offsetMax = a.offsetMax;
            rt.localScale = Vector3.one;
        }
    }
}
