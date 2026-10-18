using UnityEngine;

namespace AirlineCEO.Data
{
    /// <summary>玩家档案（Phase 1.4 / 白板 n2）。</summary>
    [CreateAssetMenu(fileName = "PlayerProfile", menuName = "AirlineCEO/Setup/Player Profile", order = 20)]
    public class PlayerProfile : ScriptableObject
    {
        public string playerName = "玩家";
        public string gender = "未指定";
        public int age = 35;
        public string nationality = "中国";
        public string ceoPortraitId;
        public string preferredLanguage = "zh-CN";
        public string currencyDisplay = "USD";
        public int defaultSpeedIndex = 1; // 0=1x 1=2x 2=4x
        public bool colorBlindMode;
    }

    /// <summary>公司档案（白板 n3）。</summary>
    [CreateAssetMenu(fileName = "CompanyProfile", menuName = "AirlineCEO/Setup/Company Profile", order = 21)]
    public class CompanyProfile : ScriptableObject
    {
        public string companyNameZh = "新星航空";
        public string companyNameEn = "Nova Air";
        public string icaoCode = "NVA";
        public string hubIata = "PEK";
        public string baseCurrency = "USD";
        public string businessModel = "full_service"; // full_service / lcc / regional / charter
        public float initialCashWan = 500f;
        public Color brandColor = new Color(0.1f, 0.36f, 1f);
        public string liveryId;
    }

    /// <summary>难度配置（白板 n4/n5 相关）。</summary>
    [CreateAssetMenu(fileName = "DifficultyConfig", menuName = "AirlineCEO/Setup/Difficulty Config", order = 22)]
    public class DifficultyConfig : ScriptableObject
    {
        public AirlineCEO.Logic.DifficultyMode mode = AirlineCEO.Logic.DifficultyMode.Normal;
        public float reappointmentThresholdOverride = -1f; // 负数表示使用默认阈值
        public float startingCashMultiplier = 1f;
        public float eventSeverityMultiplier = 1f;
        public bool enableProbation = true;
        [TextArea(2, 4)] public string description = "标准难度";
    }
}
