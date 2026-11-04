using System;
using AirlineCEO.Data;
using AirlineCEO.Systems;
using NUnit.Framework;
using UnityEngine;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// 「游戏快进到 11 月 19 日，保存，再打开继续 → 收到 11 月 3 日的邮件、现金为 0」的**端到端**回归
    /// （2026-11-03 用户报障）。走的是真实的 <see cref="SaveManager.Save"/> / <see cref="SaveManager.Load"/>，
    /// 而不是只测克隆函数 —— 病根的两种表现都在这里钉死：
    ///
    /// 1. 数字键被写成字符串文本：<c>GetFloat</c> 读不到就静默返回默认值 → 现金 0、第 1 年 Q1。
    /// 2. 公历时钟键丢失：Load 落到「现实日期」，于是玩家回到 11 月 3 日，而收件箱里 11 月 19 日的邮件
    ///    原样还在，按天结算的投递又从第 1 天重跑一遍（再收一遍 11 月 3 日的邮件）。
    /// </summary>
    public class SaveLoadClockRepairEditModeTests
    {
        const string Prefix = "ACEO_T_CLOCK_";
        static readonly DateTime TruthUtc = new DateTime(2026, 11, 19, 9, 30, 0, DateTimeKind.Utc);

        string _savedPrefix;
        GameObject _gsGo;
        GameObject _smGo;

        [SetUp]
        public void SetUp()
        {
            _savedPrefix = SaveManager.ActivePrefix;
            SaveManager.ActivePrefix = Prefix;
            CleanupKeys();
        }

        [TearDown]
        public void TearDown()
        {
            CleanupKeys();
            if (_smGo != null) UnityEngine.Object.DestroyImmediate(_smGo);
            if (_gsGo != null) UnityEngine.Object.DestroyImmediate(_gsGo);
            _smGo = null;
            _gsGo = null;
            // 松开单例指针，别把「已销毁对象」留给后面的回归
            BindSingleton(typeof(SaveManager), null);
            BindSingleton(typeof(GameState), null);
            SaveManager.ActivePrefix = _savedPrefix;
            PlayerPrefs.Save();
        }

        void CleanupKeys()
        {
            foreach (var k in SaveManager.AllSaveKeys)
                PlayerPrefs.DeleteKey(Prefix + k);
            PlayerPrefs.Save();
        }

        /// <summary>起一局「快进到 11 月 19 日」的现场：现金 2292.5 万、本季第 17 天、收件箱里有当天的邮件。</summary>
        GameState SeedNovember19(int day = 17)
        {
            _gsGo = new GameObject("T_GameState");
            var gs = _gsGo.AddComponent<GameState>();
            gs.EnsureDefaults();
            if (gs.inbox == null) gs.inbox = new System.Collections.Generic.List<InboxMail>();
            gs.cash = 2292.5f;
            gs.year = 2;
            gs.quarter = 1;
            gs.SetSimUtc(TruthUtc);
            gs.simDay = day;
            gs.inbox.Clear();
            // ★2026-11-04：这封信必须走**真实投递路径**（InboxManager.Add）而不是手工 new + 塞 ticks。
            //   收件时刻与「收件当日本季天数」都由生产代码打上；手拼字段会把「字段根本没被写入」
            //   这类缺陷一起绕过 —— 检测报告的 3 号失败正是从这种手拼夹具里漏出来的。
            InboxManager.Add(gs, new InboxMail
            {
                id = "m_parts_arrived",
                sender = "航材与供应链",
                subject = "航材入库 · PN-ENG-001 ×5",
                status = "report"
            });
            Assert.AreEqual(1, gs.inbox.Count, "夹具失败：邮件没有进箱");
            Assert.AreEqual(day, gs.inbox[0].receivedSimDay,
                "收件时没有把本季天数写进信里：读档修复挂钟时就没有可对齐的水位");

            _smGo = new GameObject("T_SaveManager");
            var sm = _smGo.AddComponent<SaveManager>();
            BindSingleton(typeof(GameState), gs);
            BindSingleton(typeof(SaveManager), sm);
            Assert.IsNotNull(SaveManager.Instance, "SaveManager.Instance 未建立");
            return gs;
        }

        /// <summary>
        /// 编辑模式下 AddComponent 不跑 Awake，而 <c>GameState.Instance</c> / <c>SaveManager.Instance</c>
        /// 只在 Awake 里赋值 —— 也就没法直接调 <c>Save()</c>/<c>Load()</c>。这里用反射写私有 setter，
        /// 把两个单例指到本次测试的对象上（不改生产代码、不依赖任何测试专用 API）。
        /// 换成直接跑 Awake 行不通：Awake 里的 <c>DontDestroyOnLoad</c> 在非播放态会抛异常。
        /// </summary>
        static void BindSingleton(Type type, object instance)
        {
            var prop = type.GetProperty("Instance",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            Assert.IsNotNull(prop, $"{type.Name}.Instance 不存在");
            var setter = prop.GetSetMethod(nonPublic: true);
            Assert.IsNotNull(setter, $"{type.Name}.Instance 没有 setter");
            setter.Invoke(null, new[] { instance });
        }

        [Test]
        public void SaveThenLoad_KeepsCashAndClock()
        {
            var gs = SeedNovember19();
            SaveManager.Instance.Save();

            // 模拟「退出到开始界面再继续」：内存里这一局被清掉（ResetForNewGame 的等效口径）
            gs.cash = 0f;
            gs.year = 1;
            gs.quarter = 1;
            gs.simDay = 1;
            gs.SetSimUtc(new DateTime(2026, 11, 3, 0, 1, 0, DateTimeKind.Utc));

            SaveManager.Instance.Load();

            Assert.AreEqual(2292.5f, gs.cash, 0.01f, "读档丢了现金");
            Assert.AreEqual(2, gs.year);
            Assert.AreEqual(11, gs.wallMonth, "读档丢了公历月份");
            Assert.AreEqual(19, gs.wallDay, "读档丢了公历日期");
            Assert.AreEqual(17, gs.simDay);
        }

        [Test]
        public void LegacySaveWithoutWallClock_LoadsAtNewestMailDay()
        {
            var gs = SeedNovember19();
            long mailTicks = gs.inbox[0].receivedUtcTicks;
            SaveManager.Instance.Save();

            // 老档 / 被写坏的档：公历时钟三个键不在，本季天数也落回默认值 1
            PlayerPrefs.DeleteKey(Prefix + "ACEO_WallYear");
            PlayerPrefs.DeleteKey(Prefix + "ACEO_WallMonth");
            PlayerPrefs.DeleteKey(Prefix + "ACEO_WallDay");
            PlayerPrefs.SetInt(Prefix + "ACEO_SimDay", 1);
            PlayerPrefs.Save();

            // 现场也得是「重开游戏/退出到开始界面」之后的样子：GameState 的时钟字段归零
            // （ResetForNewGame 会把 public 字段抄回出厂值，wall* 就是 0），否则测的是内存旧值。
            gs.wallYear = 0;
            gs.wallMonth = 0;
            gs.wallDay = 0;
            gs.simDay = 1;

            SaveManager.Instance.Load();

            Assert.AreEqual(2026, gs.wallYear);
            Assert.AreEqual(11, gs.wallMonth);
            Assert.AreEqual(19, gs.wallDay,
                "时钟没有抬回已收邮件的水位：玩家会「回到 11 月 3 日」，再收一遍 11 月 3 日的邮件");
            Assert.AreEqual(17, gs.simDay,
                "时钟抬回去了但本季天数没跟上：按天结算的投递会从第 1 天重跑");
            Assert.GreaterOrEqual(gs.SimUtc.Ticks, mailTicks,
                "读回来的时钟不能早于已投递的最新邮件");
        }

        /// <summary>
        /// 负面对照：天数必须来自**信上记的收件日**，而不是「坏档被兜底成的今天」。
        ///
        /// ★2026-11-04 独立检测 3 号失败的原貌：旧算法按「今天 → 最新邮件日」补天数，
        /// 于是同一个存档 11-03 读是第 17 天、11-04 读是第 16 天（每过一天少一天）。
        /// 这里故意把存档里的计数器写成 5，与信上的 17 拉开距离：按现实日期推的加法只会得到
        /// 「5 + 今天到 11-19 的天数」（约 20），绝不可能正好等于 17。
        /// </summary>
        [Test]
        public void LegacyMailDayWins_OverBrokenCounter()
        {
            var gs = SeedNovember19();
            SaveManager.Instance.Save();

            PlayerPrefs.DeleteKey(Prefix + "ACEO_WallYear");
            PlayerPrefs.DeleteKey(Prefix + "ACEO_WallMonth");
            PlayerPrefs.DeleteKey(Prefix + "ACEO_WallDay");
            PlayerPrefs.SetInt(Prefix + "ACEO_SimDay", 5);
            PlayerPrefs.Save();

            gs.wallYear = 0;
            gs.wallMonth = 0;
            gs.wallDay = 0;
            gs.simDay = 5;

            SaveManager.Instance.Load();

            Assert.AreEqual(19, gs.wallDay);
            Assert.AreEqual(17, gs.simDay,
                "本季天数被「今天」带偏了：信上记着收件当日是第 17 天，读回来必须还是 17，"
                + "与哪天打开游戏无关");
        }

        [Test]
        public void StringTypedLegacyCash_IsSalvagedOnLoad()
        {
            var gs = SeedNovember19();
            SaveManager.Instance.Save();

            // 旧版快照克隆把 float 写成了字符串文本
            PlayerPrefs.SetString(Prefix + "ACEO_Cash", "2292.5");
            PlayerPrefs.Save();

            SaveManager.Instance.Load();

            Assert.AreEqual(2292.5f, gs.cash, 0.01f,
                "被写成字符串文本的现金没有按文本救回：玩家会看到现金 0");
        }

        [Test]
        public void HealthySave_IsNotTouchedByClockRepair()
        {
            var gs = SeedNovember19();
            SaveManager.Instance.Save();
            SaveManager.Instance.Load();

            // 时钟本来就晚于所有邮件：修复逻辑必须原样返回，不能把时间「往前推」
            Assert.AreEqual(PathsEqual(TruthUtc, gs.SimUtc), true, $"时钟被修复逻辑改动了：{gs.SimUtc:O} ≠ {TruthUtc:O}");
            Assert.AreEqual(17, gs.simDay);
        }

        static bool PathsEqual(DateTime a, DateTime b)
            => a.Year == b.Year && a.Month == b.Month && a.Day == b.Day
               && a.Hour == b.Hour && a.Minute == b.Minute;
    }
}
