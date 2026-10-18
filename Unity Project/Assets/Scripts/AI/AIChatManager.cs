using System;
using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEngine;
using UnityEngine.Networking;

namespace AirlineCEO.AI
{
    /// <summary>
    /// 智能指挥中枢：调用 DeepSeek，失败则回落本地话术库。
    /// 超时默认 3 秒；回复中强制带上 RPM 真实数值。
    /// </summary>
    public class AIChatManager : MonoBehaviour
    {
        public static AIChatManager Instance { get; private set; }

        [SerializeField] private DeepSeekConfig config;

        public bool IsBusy { get; private set; }

        /// <summary>最近一次顾问回复（供「执行建议」二次确认）。</summary>
        public AdvisorResponse LastAdvice { get; private set; }

        public event Action<AdvisorResponse> OnAdviceReceived;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (config == null)
                config = Resources.Load<DeepSeekConfig>("DeepSeekConfig");
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void SetConfig(DeepSeekConfig cfg) => config = cfg;

        /// <summary>运营改班：只要模型返回的 JSON 文本，不写入顾问 LastAdvice。</summary>
        public void AskRawJson(string systemPrompt, string userPrompt, Action<string> onComplete)
        {
            if (IsBusy)
            {
                onComplete?.Invoke(null);
                return;
            }
            StartCoroutine(AskRawJsonRoutine(systemPrompt, userPrompt, onComplete));
        }

        IEnumerator AskRawJsonRoutine(string systemPrompt, string userPrompt, Action<string> onComplete)
        {
            IsBusy = true;
            string apiKey = config != null ? config.ResolveApiKey() : PlayerPrefs.GetString(DeepSeekConfig.PlayerPrefsKey, "");
            int timeout = config != null ? Mathf.Max(1, config.timeoutSeconds) : 3;
            string endpoint = config != null && !string.IsNullOrEmpty(config.endpoint)
                ? config.endpoint
                : DeepSeekConfig.DefaultEndpoint;
            string model = config != null && !string.IsNullOrEmpty(config.model)
                ? config.model
                : DeepSeekConfig.DefaultModel;

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                onComplete?.Invoke(null);
                IsBusy = false;
                yield break;
            }

            string body = BuildRequestJson(model, systemPrompt, userPrompt);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(body);
            using (var req = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST))
            {
                req.uploadHandler = new UploadHandlerRaw(bodyRaw);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Authorization", "Bearer " + apiKey);
                req.timeout = timeout;
                WebRequestGuard.Track(req);
                try { yield return req.SendWebRequest(); }
                finally { WebRequestGuard.Untrack(req); }
                string json = null;
                if (req.result == UnityWebRequest.Result.Success)
                {
                    TryExtractContentJson(req.downloadHandler.text, out json);
                }
                onComplete?.Invoke(json);
            }
            IsBusy = false;
        }

        static bool TryExtractContentJson(string apiRaw, out string json)
        {
            json = null;
            try
            {
                var wrapper = JsonUtility.FromJson<DeepSeekChatResponse>(apiRaw);
                string content = null;
                if (wrapper?.choices != null && wrapper.choices.Length > 0)
                    content = wrapper.choices[0]?.message?.content;
                if (string.IsNullOrWhiteSpace(content))
                    content = ExtractJsonObject(apiRaw);
                else
                    content = ExtractJsonObject(content);
                json = string.IsNullOrWhiteSpace(content) ? null : content;
                return json != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>异步咨询；完成回调到主线程。</summary>
        public void Ask(string playerInput, Action<AdvisorResponse> onComplete)
        {
            if (IsBusy)
            {
                onComplete?.Invoke(new AdvisorResponse
                {
                    advisor = "战略顾问",
                    emotion = "平和",
                    message = "上一条指令还在处理，请稍候。",
                    suggestedAction = "none",
                    fromFallback = true
                });
                return;
            }

            StartCoroutine(AskRoutine(playerInput, onComplete));
        }

        private IEnumerator AskRoutine(string playerInput, Action<AdvisorResponse> onComplete)
        {
            IsBusy = true;
            string userPrompt = GameDataSnapshot.BuildUserPrompt(playerInput);
            var rpm = GameDataSnapshot.PekLhrRpm;

            string apiKey = config != null ? config.ResolveApiKey() : PlayerPrefs.GetString(DeepSeekConfig.PlayerPrefsKey, "");
            int timeout = config != null ? Mathf.Max(1, config.timeoutSeconds) : 3;
            string endpoint = config != null && !string.IsNullOrEmpty(config.endpoint)
                ? config.endpoint
                : DeepSeekConfig.DefaultEndpoint;
            string model = config != null && !string.IsNullOrEmpty(config.model)
                ? config.model
                : DeepSeekConfig.DefaultModel;
            string systemPrompt = config != null
                ? config.GetSystemPrompt()
                : DeepSeekConfig.PlainLanguageSystemPrompt;

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                var local = BuildFallback(playerInput, "未配置 API Key，已用本地话术");
                EnsureRpmInMessage(local, rpm);
                DeliverAdvice(local, onComplete);
                IsBusy = false;
                yield break;
            }

            string body = BuildRequestJson(model, systemPrompt, userPrompt);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(body);

            using (var req = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST))
            {
                req.uploadHandler = new UploadHandlerRaw(bodyRaw);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Authorization", "Bearer " + apiKey);
                req.timeout = timeout;

                WebRequestGuard.Track(req);
                try { yield return req.SendWebRequest(); }
                finally { WebRequestGuard.Untrack(req); }

                bool ok = req.result == UnityWebRequest.Result.Success;
                if (!ok)
                {
                    string reason = string.IsNullOrEmpty(req.error) ? $"HTTP {req.responseCode}" : req.error;
                    var local = BuildFallback(playerInput, $"API 失败（{reason}），已用本地话术");
                    EnsureRpmInMessage(local, rpm);
                    DeliverAdvice(local, onComplete);
                    IsBusy = false;
                    yield break;
                }

                string raw = req.downloadHandler.text;
                if (!TryParseAdvisor(raw, out AdvisorResponse parsed, out string parseError))
                {
                    var local = BuildFallback(playerInput, $"解析失败（{parseError}），已用本地话术");
                    EnsureRpmInMessage(local, rpm);
                    DeliverAdvice(local, onComplete);
                    IsBusy = false;
                    yield break;
                }

                EnsureRpmInMessage(parsed, rpm);
                parsed.fromFallback = false;
                DeliverAdvice(parsed, onComplete);
            }

            IsBusy = false;
        }

        private void DeliverAdvice(AdvisorResponse advice, Action<AdvisorResponse> onComplete)
        {
            if (advice != null)
            {
                if (advice.data == null)
                    advice.data = new AdvisorDataPayload();

                // 默认补齐北京-伦敦，便于 open_route 执行
                if (string.IsNullOrWhiteSpace(advice.data.origin))
                    advice.data.origin = "北京";
                if (string.IsNullOrWhiteSpace(advice.data.destination))
                    advice.data.destination = "伦敦";
                if (Mathf.Approximately(advice.data.profit, 0f) && GameDataSnapshot.PekLhrRpm.dailyProfit != 0f)
                    advice.data.profit = GameDataSnapshot.PekLhrRpm.dailyProfit;
            }

            LastAdvice = advice;
            OnAdviceReceived?.Invoke(advice);
            onComplete?.Invoke(advice);
        }

        private static string BuildRequestJson(string model, string systemPrompt, string userPrompt)
        {
            // 手动拼 JSON，避免转义问题
            return "{"
                   + $"\"model\":\"{Escape(model)}\","
                   + "\"messages\":["
                   + $"{{\"role\":\"system\",\"content\":\"{Escape(systemPrompt)}\"}},"
                   + $"{{\"role\":\"user\",\"content\":\"{Escape(userPrompt)}\"}}"
                   + "],"
                   + "\"response_format\":{\"type\":\"json_object\"},"
                   + "\"temperature\":0.3"
                   + "}";
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        private static bool TryParseAdvisor(string apiRaw, out AdvisorResponse response, out string error)
        {
            response = null;
            error = "";

            try
            {
                var wrapper = JsonUtility.FromJson<DeepSeekChatResponse>(apiRaw);
                string content = null;
                if (wrapper?.choices != null && wrapper.choices.Length > 0)
                    content = wrapper.choices[0]?.message?.content;

                if (string.IsNullOrWhiteSpace(content))
                {
                    // 有时直接返回 JSON 对象
                    content = ExtractJsonObject(apiRaw);
                }
                else
                {
                    content = ExtractJsonObject(content);
                }

                if (string.IsNullOrWhiteSpace(content))
                {
                    error = "空内容";
                    return false;
                }

                response = JsonUtility.FromJson<AdvisorResponse>(content);
                if (response == null || string.IsNullOrWhiteSpace(response.message))
                {
                    error = "message 为空";
                    return false;
                }

                if (response.data == null)
                    response.data = new AdvisorDataPayload();

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string ExtractJsonObject(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = text.Trim();
            if (text.StartsWith("```"))
            {
                text = Regex.Replace(text, "^```(?:json)?\\s*", "", RegexOptions.IgnoreCase);
                text = Regex.Replace(text, "\\s*```$", "");
            }

            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            if (start >= 0 && end > start)
                return text.Substring(start, end - start + 1);
            return text;
        }

        private static AdvisorResponse BuildFallback(string playerInput, string hint)
        {
            string localMsg = NeuralCommandMatcher.Reply(playerInput);
            // NeuralCommandMatcher 返回「顾问：正文」，拆开
            string advisor = "战略顾问";
            string message = localMsg;
            int idx = localMsg.IndexOf('：');
            if (idx < 0) idx = localMsg.IndexOf(':');
            if (idx > 0)
            {
                advisor = localMsg.Substring(0, idx).Trim();
                message = localMsg.Substring(idx + 1).Trim();
            }

            var rpm = GameDataSnapshot.PekLhrRpm.score > 0
                ? GameDataSnapshot.PekLhrRpm
                : RPMFromSnapshot();

            return new AdvisorResponse
            {
                advisor = advisor,
                emotion = "谨慎",
                message = message,
                suggestedAction = GuessAction(playerInput),
                data = new AdvisorDataPayload
                {
                    profit = rpm.dailyProfit,
                    risk = rpm.rating == "红" ? "high" : rpm.rating == "黄" ? "medium" : "low"
                },
                fromFallback = true,
                errorHint = hint
            };
        }

        private static RPMCalculator.RPMOutput RPMFromSnapshot()
        {
            GameDataSnapshot.Build();
            return GameDataSnapshot.PekLhrRpm;
        }

        private static string GuessAction(string input)
        {
            if (string.IsNullOrEmpty(input)) return "none";
            if (input.Contains("现金") || input.Contains("跑道") || input.Contains("流动性"))
                return "check_cash";
            if (input.Contains("开通航线") || input.Contains("开航") || input.Contains("首航")) return "open_route";
            if (input.Contains("航线") || input.Contains("北京") || input.Contains("伦敦"))
                return "open_route";
            return "none";
        }

        /// <summary>任务书 5.3：回复须含 RPM 真值；末尾统一加「综合评定 XX 分」便于新手理解。</summary>
        private static void EnsureRpmInMessage(AdvisorResponse resp, RPMCalculator.RPMOutput rpm)
        {
            if (resp == null) return;
            if (resp.data == null) resp.data = new AdvisorDataPayload();
            resp.data.profit = rpm.dailyProfit;
            if (string.IsNullOrEmpty(resp.data.risk))
            {
                resp.data.risk = rpm.rating == "红" ? "high" : rpm.rating == "黄" ? "medium" : "low";
            }

            string msg = (resp.message ?? "").Trim();
            string scoreTag = $"综合评定 {rpm.score} 分";

            // 去掉旧版黑话注脚，避免重复
            int rpmIdx = msg.IndexOf("（RPM", System.StringComparison.OrdinalIgnoreCase);
            if (rpmIdx > 0) msg = msg.Substring(0, rpmIdx).Trim();
            int refIdx = msg.IndexOf("参考：", System.StringComparison.Ordinal);
            if (refIdx > 0) msg = msg.Substring(0, refIdx).Trim();
            int oldScoreIdx = msg.IndexOf("综合评定", System.StringComparison.Ordinal);
            if (oldScoreIdx > 0) msg = msg.Substring(0, oldScoreIdx).Trim();

            if (msg.Length > 40) msg = msg.Substring(0, 40) + "…";

            // 正文里若还没有分数，统一追加通俗注脚（含综合评定）
            if (msg.IndexOf(scoreTag, System.StringComparison.Ordinal) < 0
                && msg.IndexOf($"{rpm.score} 分", System.StringComparison.Ordinal) < 0
                && msg.IndexOf($"{rpm.score}分", System.StringComparison.Ordinal) < 0)
            {
                resp.message = $"{msg} {GameDataSnapshot.FormatRpmFootnote(rpm)}";
            }
            else if (msg.IndexOf("每天能赚", System.StringComparison.Ordinal) < 0)
            {
                resp.message = $"{msg}（北京-伦敦大约每天能赚 {rpm.dailyProfit:F1} 万美元）";
            }
            else
            {
                resp.message = msg;
            }
        }

        [Serializable]
        private class DeepSeekChatResponse
        {
            public Choice[] choices;
        }

        [Serializable]
        private class Choice
        {
            public Msg message;
        }

        [Serializable]
        private class Msg
        {
            public string content;
        }
    }
}
