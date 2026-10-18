using System;
using UnityEngine;

namespace AirlineCEO.Systems
{
        /// <summary>
        /// 游戏速度：正式功能仅 1×/2×/3×（驱动游戏时钟，不加速 Unity timeScale）。
        /// 暂停仅供危机强制停表；DevDay 仅编辑器/开发包顶栏。
        /// <para>
        /// 换算（相对现实时间）：
        /// 1× → 现实 1 分钟 = 游戏 1 分钟；
        /// 2× → 现实 1 分钟 = 游戏 1 小时；
        /// 3× → 现实 1 分钟 = 游戏 3 小时；
        /// DevDay → 现实 1 分钟 = 游戏 1 天。
        /// </para>
        /// </summary>
        public static class GameSpeed
        {
        public const int L1 = 0;
        public const int L2 = 1;
        public const int L3 = 2;
        public const int Paused = 3;
        /// <summary>开发档：现实 1 分钟 = 游戏 1 天。不写入正式存档倍速。</summary>
        public const int DevDay = 4;

        /// <summary>展示用倍率标签（设置面板等）。</summary>
        public static readonly float[] Scales = { 1f, 2f, 3f, 0f, 1440f };
        public static readonly string[] Labels = { "1×", "2×", "3×", "‖", "1日" };

        /// <summary>每现实秒推进的游戏分钟数。</summary>
        public static readonly float[] GameMinutesPerRealSecond =
        {
            1f / 60f, // 1×：1 现实分 = 1 游戏分
            1f,       // 2×：1 现实分 = 60 游戏分 = 1 游戏时
            3f,       // 3×：1 现实分 = 180 游戏分 = 3 游戏时
            0f,       // 暂停
            24f       // DevDay：1 现实分 = 1440 游戏分 = 1 游戏日
        };

        static int _level = L1;
        static int _resumeLevel = L1;

        public static int Level => _level;
        public static int ResumeLevel => _resumeLevel;
        public static bool IsPaused => _level == Paused;
        public static float GameMinutesPerSecond => GameMinutesPerRealSecond[Mathf.Clamp(_level, 0, GameMinutesPerRealSecond.Length - 1)];

        public static event Action<int> OnLevelChanged;

        /// <summary>设置档位。L1~L3 写入存档；Paused / DevDay 不改正式存档倍速。</summary>
        public static void Set(int level)
        {
            level = Mathf.Clamp(level, 0, Scales.Length - 1);
            if (level != Paused)
                _resumeLevel = level;

            _level = level;
            Time.timeScale = _level == Paused ? 0f : 1f;

            var gs = GameState.Instance;
            if (gs != null && _level != Paused && _level != DevDay)
                gs.speedLevel = _level;

            OnLevelChanged?.Invoke(_level);
        }

        /// <summary>强制暂停（危机 / 开发工具）。恢复用 <see cref="Resume"/>。</summary>
        public static void Pause()
        {
            if (_level != Paused)
                _resumeLevel = _level;
            _level = Paused;
            Time.timeScale = 0f;
            OnLevelChanged?.Invoke(_level);
        }

        /// <summary>恢复到暂停前的档位（可回到 DevDay）。</summary>
        public static void Resume() => Set(_resumeLevel == Paused ? L1 : _resumeLevel);

        /// <summary>兼容旧调用：恢复到 1×。</summary>
        public static void Resume1x() => Set(L1);

        public static string Label(int level)
        {
            if (level == Paused) return "暂停";
            if (level == DevDay) return "1日";
            return Scales[Mathf.Clamp(level, 0, 2)] + "×";
        }

        public static string CurrentLabel => Label(_level);
    }
}
