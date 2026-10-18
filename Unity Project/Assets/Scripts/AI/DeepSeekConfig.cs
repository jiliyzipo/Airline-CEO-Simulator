using UnityEngine;

namespace AirlineCEO.AI
{
    /// <summary>
    /// DeepSeek 配置。API Key 优先读 Inspector，其次 PlayerPrefs（由菜单写入），勿提交到 Git。
    /// </summary>
    [CreateAssetMenu(fileName = "DeepSeekConfig", menuName = "AirlineCEO/AI/DeepSeek Config", order = 10)]
    public class DeepSeekConfig : ScriptableObject
    {
        public const string PlayerPrefsKey = "ACEO_DeepSeek_ApiKey";
        public const string DefaultEndpoint = "https://api.deepseek.com/chat/completions";
        public const string DefaultModel = "deepseek-chat";

        /// <summary>面向新手的默认 System Prompt（代码内置，避免旧 asset 残留黑话提示）。</summary>
        public const string PlainLanguageSystemPrompt =
            "你是航空公司的战略顾问，面向游戏新手解说。必须只用快照里的真实数字，禁止编造。"
            + "message 用大白话，像朋友提醒老板：少用黑话；若出现专业词，立刻用括号解释。"
            + "不要只说「现金跑道/竞对运力/RPM」，要说「钱大概还能花几个月」「对手航班太多」「这条航线划不划算」。"
            + "message 结尾必须带上快照里的航线综合分，格式固定为：综合评定XX分。"
            + "语气专业但亲切，控制在 50 字以内中文。"
            + "只输出一个 JSON：advisor, emotion, message, suggestedAction, data.profit, data.risk。"
            + "suggestedAction 只能是 open_route / delay / reject / check_cash / first_flight / none。";

        [Header("API")]
        public string endpoint = DefaultEndpoint;
        public string model = DefaultModel;

        [Tooltip("可填在这里做本地调试；正式建议用菜单写入 PlayerPrefs，避免进版本库")]
        public string apiKey;

        [Header("超时（秒）")]
        public int timeoutSeconds = 3;

        [Tooltip("勾选后才使用下方自定义提示词；默认用代码内置的通俗版")]
        public bool useCustomSystemPrompt;

        [TextArea(4, 10)]
        public string systemPrompt = PlainLanguageSystemPrompt;

        public string GetSystemPrompt()
        {
            if (useCustomSystemPrompt && !string.IsNullOrWhiteSpace(systemPrompt))
                return systemPrompt.Trim();
            return PlainLanguageSystemPrompt;
        }

        public string ResolveApiKey()
        {
            if (!string.IsNullOrWhiteSpace(apiKey))
                return apiKey.Trim();

            if (PlayerPrefs.HasKey(PlayerPrefsKey))
            {
                string fromPrefs = PlayerPrefs.GetString(PlayerPrefsKey, "");
                if (!string.IsNullOrWhiteSpace(fromPrefs))
                    return fromPrefs.Trim();
            }

            return string.Empty;
        }

        public bool HasApiKey => !string.IsNullOrWhiteSpace(ResolveApiKey());
    }
}
