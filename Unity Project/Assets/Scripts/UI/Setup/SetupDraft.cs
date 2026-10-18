using AirlineCEO.Data;
using AirlineCEO.Logic;

namespace AirlineCEO.UI.Setup
{
    /// <summary>开局流程草稿（白板 n2–n5），确认前不写死进存档。</summary>
    public sealed class SetupDraft
    {
        /// <summary>由玩家填写，默认留空。</summary>
        public string PlayerName = "";
        public string Gender = "男";
        public int BirthYear = 1990;
        public int Age => SetupCatalogs.AgeFromBirthYear(BirthYear);
        public string Nationality = "中国";

        public string CompanyNameZh = "";
        public string CompanyNameEn = "";
        /// <summary>航司 IATA 二字码（两字母，或字母+数字组合）。</summary>
        public string IataCode = "";
        /// <summary>航司 ICAO 三字码（三位字母）。</summary>
        public string IcaoCode = "";
        public string HubIata = "PEK";
        public float InitialCashWan = 500f;
        public string BusinessModel = "full_service";

        public DifficultyMode Difficulty = DifficultyMode.Normal;
        public int BoardBatchSeed = 1;

        /// <summary>玩法模式（0 标准 / 1 传奇 / 2 新游戏+ / 3 快速）。</summary>
        public int Mode = (int)AirlineCEO.Data.GameMode.Standard;

        /// <summary>铁人模式（GDB 31.1）：禁手动存档 + 季度自动存档实时覆盖 + 遗产评分 ×1.2。</summary>
        public bool Ironman = false;

        public bool FromScratch;
        public string TakeoverAirlineId = PresetAirlines.Regional;
        public string Education = "民航管理";
        public string Career = "航司运营";
        public string Trait = "稳健";
    }
}
