using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// 全局应用设置（语言/色盲/字号/遥测退出）持久化与加载（GDB 29.5 / 33.7 / 38.1）。
    /// 注意：这些是「玩家偏好」，独立于游戏存档槽，不随清档重置，也不进 AllSaveKeys。
    /// </summary>
    public static class AppSettings
    {
        const string KeyLanguage = "ACEO_Settings_Language";
        const string KeyColorblind = "ACEO_Settings_Colorblind";
        const string KeyFontScale = "ACEO_Settings_FontScale";
        const string KeyTelemetryOptOut = "ACEO_Settings_TelemetryOptOut";
        const string KeyFullscreen = "ACEO_Settings_Fullscreen";
        const string KeyDefaultSpeed = "ACEO_Settings_DefaultSpeed";

        /// <summary>启动时从 PlayerPrefs 恢复并应用到纯 C# 静态态。</summary>
        public static void Load()
        {
            Loc.SetLanguage((Language)Mathf.Clamp(PlayerPrefs.GetInt(KeyLanguage, 0), 0, 1));
            Accessibility.SetColorblind((ColorblindMode)Mathf.Clamp(PlayerPrefs.GetInt(KeyColorblind, 0), 0, 3));
            Accessibility.SetFontScale((FontScale)Mathf.Clamp(PlayerPrefs.GetInt(KeyFontScale, 0), 0, 2));
            Telemetry.SetOptOut(PlayerPrefs.GetInt(KeyTelemetryOptOut, 0) == 1);
            ApplyFullscreenMode(PlayerPrefs.GetInt(KeyFullscreen, 1) == 1, force: true);
        }

        /// <summary>当前是否全屏（默认全屏，GDB n40「显示模式」）。</summary>
        public static bool Fullscreen => Screen.fullScreen;

        public static void ApplyFullscreen(bool fullscreen)
        {
            ApplyFullscreenMode(fullscreen, force: false);
            PlayerPrefs.SetInt(KeyFullscreen, fullscreen ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// 用无边框全屏（FullScreenWindow）而非独占全屏：独占模式会触发 Windows
        /// ChangeDisplaySettings，部分显示环境切换失败并刷「ChangeDisplaySettings
        /// Failed. 2. 1」黄警。仅在状态变化时赋值，避免重复触发。
        /// </summary>
        static void ApplyFullscreenMode(bool fullscreen, bool force)
        {
            var target = fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            if (!force && Screen.fullScreenMode == target) return;
            Screen.fullScreenMode = target;
        }

        /// <summary>默认游戏速度档位（1×/2×/3×，开局应用，GDB n40「速度默认值」）。</summary>
        public static int DefaultSpeedLevel
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(KeyDefaultSpeed, 0), 0, GameSpeed.L3);
            set { PlayerPrefs.SetInt(KeyDefaultSpeed, Mathf.Clamp(value, 0, GameSpeed.L3)); PlayerPrefs.Save(); }
        }

        public static void ApplyDefaultSpeed(int level)
        {
            DefaultSpeedLevel = level;
            GameSpeed.Set(level);
        }

        public static void ApplyLanguage(Language l)
        {
            Loc.SetLanguage(l);
            PlayerPrefs.SetInt(KeyLanguage, (int)l);
            PlayerPrefs.Save();
        }

        public static void ApplyColorblind(ColorblindMode m)
        {
            Accessibility.SetColorblind(m);
            PlayerPrefs.SetInt(KeyColorblind, (int)m);
            PlayerPrefs.Save();
        }

        public static void ApplyFontScale(FontScale f)
        {
            Accessibility.SetFontScale(f);
            PlayerPrefs.SetInt(KeyFontScale, (int)f);
            PlayerPrefs.Save();
        }

        public static void ApplyTelemetryOptOut(bool optOut)
        {
            Telemetry.SetOptOut(optOut);
            PlayerPrefs.SetInt(KeyTelemetryOptOut, optOut ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
