namespace AirlineCEO.Data
{
    /// <summary>
    /// 玩法模式（GDB 第4章 / 第31章）：通关解锁后可开局时选择。
    /// </summary>
    public enum GameMode
    {
        Standard = 0,    // 标准模式（默认）
        Legend = 1,      // 传奇模式（S 级解锁：AI 竞争压力 +50%，锁定困难难度）
        NewGamePlus = 2, // 新游戏+（A 级解锁：品牌净值 +10、董事初始关系 +10）
        Fast = 3         // 快速模式（B 级解锁：随机事件频率减半、默认 2× 速度）
    }

    public static class GameModeInfo
    {
        public static string DisplayName(int mode) => ((GameMode)mode) switch
        {
            GameMode.Legend => "传奇模式",
            GameMode.NewGamePlus => "新游戏+",
            GameMode.Fast => "快速模式",
            _ => "标准模式"
        };

        public static string Description(int mode) => ((GameMode)mode) switch
        {
            GameMode.Legend => "所有 AI 竞争压力 +50%，难度锁定「困难」。为航空传奇准备。",
            GameMode.NewGamePlus => "继承上周目遗产：品牌净值 +10、董事初始关系 +10。",
            GameMode.Fast => "随机事件频率减半，开局速度 2×，专注快节奏经营。",
            _ => "经典体验，均衡的难度与节奏。"
        };
    }
}
