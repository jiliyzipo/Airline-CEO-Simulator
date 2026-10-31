using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AirlineCEO.Data;
using AirlineCEO.Systems;
using AirlineCEO.UI.Shell;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace AirlineCEO.Tests.PlayMode
{
    /// <summary>
    /// T05.7「1× 实时节奏」的机器取证（2026-10-31，用户授权以脚本化真时钟运行代替人手观感）。
    ///
    /// 这条用例**真的等钟**：不推进模拟时间、不调 timeScale、不切 DevDay，就让
    /// <see cref="TimeManager"/> 在 1× 下用 <c>Time.unscaledDeltaTime</c> 走满若干现实秒，
    /// 再把「现实过了多少秒」与「游戏过了多少分钟」对账。1× 的定义见 <see cref="GameSpeed"/>：
    /// **现实 1 分钟 = 游戏 1 分钟**（所以 4 游戏小时的回执 ≈ 现实 4 小时——这也是为什么
    /// 加速推进不能冒充 1× 体验，而本用例只能覆盖分钟级的节奏与交互副作用）。
    ///
    /// 覆盖两处最容易出错的交互副作用：
    /// · 打开／读信／返回时，时钟不得被 UI 顺手停掉（`Time.timeScale` 必须仍是 1），
    ///   也不得因为面板刷新而跳表（累计误差仍按 1× 折算）；
    /// · 有到期强制待办时**应该**停表（T05.3 的设计），停表期间一秒都不许走。
    ///
    /// 明确不覆盖：真人手速下的点击手感、字号／截断观感、完整 4 小时邮件往返。
    /// 这些在 `-nographics` 下没有渲染结果，任何断言都只是自欺——任务书里如实标注。
    ///
    /// 运行时长约 8 分钟，故标 <c>Explicit</c>：默认回归套件不跑它，取证时用
    /// `-testFilter T057OneXPlayModeTests` 显式点名。
    /// </summary>
    public class T057OneXPlayModeTests
    {
        readonly List<GameObject> _owned = new List<GameObject>();

        /// <summary>
        /// 1× 的允许漂移。时钟按**整分钟**推进，测量窗口里天然有 ±1 分的截断误差，
        /// 再给 0.3 分的调度余量（batchmode 帧率抖动）。
        /// </summary>
        const double ToleranceMinutes = 1.3d;

        [TearDown]
        public void Cleanup()
        {
            TimeManager.SimulationEnabled = false;
            GameSpeed.Set(GameSpeed.L1);
            for (int i = 0; i < _owned.Count; i++)
                if (_owned[i] != null) UnityEngine.Object.DestroyImmediate(_owned[i]);
            _owned.Clear();
        }

        void RequireBaseline()
        {
            if (Application.companyName != "AirlineCEORegression")
                Assert.Ignore("Run in the isolated baseline project only.");
        }

        GameObject Own(GameObject go) { _owned.Add(go); return go; }

        GameState NewState(string name)
        {
            RequireBaseline();
            var gs = Own(new GameObject(name)).AddComponent<GameState>();
            gs.year = 1; gs.quarter = 1; gs.cash = 10000f;
            gs.SetSimUtc(new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc));
            gs.simDay = 1; gs.simHour = 0; gs.simMinute = 0;
            gs.inbox.Clear(); gs.pendingInbox.Clear();
            gs.activeRoutes.Clear(); gs.fleet.Clear();
            gs.stg = new StrategyState();
            return gs;
        }

        void StartClock()
        {
            Own(new GameObject("T057Clock")).AddComponent<TimeManager>();
            GameSpeed.Set(GameSpeed.L1);
            TimeManager.SimulationEnabled = true;
        }

        /// <summary>让真时钟走满 <paramref name="seconds"/> 现实秒，返回「现实秒 / 游戏分」实测对。</summary>
        static IEnumerator RealWait(GameState gs, float seconds, Action<double, double> report)
        {
            DateTime sim0 = gs.SimUtc;
            var sw = Stopwatch.StartNew();
            yield return new WaitForSecondsRealtime(seconds);
            sw.Stop();
            double real = sw.Elapsed.TotalSeconds;
            double sim = (gs.SimUtc - sim0).TotalMinutes;
            report?.Invoke(real, sim);
        }

        static void AssertOneX(double realSeconds, double simMinutes, string what)
        {
            Assert.Greater(realSeconds, 30d, what + "：测量窗口太短，断言没有意义");
            double expected = realSeconds / 60d;          // 1×：现实 1 分钟 = 游戏 1 分钟
            Assert.AreEqual(expected, simMinutes, ToleranceMinutes,
                $"{what}：1× 应「现实 1 分钟 = 游戏 1 分钟」，实测现实 {realSeconds:F1}s → 游戏 {simMinutes:F1} 分" +
                $"（应约 {expected:F1} 分）");
        }

        [UnityTest, Explicit, Timeout(900000)]
        public IEnumerator OneX_NewGamePacing_AndMailInteraction_KeepTheClockHonest()
        {
            var gs = NewState("T057New");
            StartClock();

            // ── 1) 纯等待 + 面板打开：两段之间插入「读信」，累计仍须满足 1× ──
            double realA = 0, simA = 0;
            yield return RealWait(gs, 120f, (r, s) => { realA = r; simA = s; });
            Debug.Log($"[T05.7] 新建开局·纯等待：现实 {realA:F1}s → 游戏 {simA:F1} 分（1× 应约 {realA / 60d:F1} 分）");
            AssertOneX(realA, simA, "新建开局·纯等待");
            Assert.AreEqual(1f, Time.timeScale, 0.001f, "正常走时不得被压成暂停");

            // 面板常驻打开 = 玩家正在「看信 → 返回 → 再看下一封」的那段时间窗；
            // 返回栈本身由 T03.8 的用例钉住，这里只问：开面板会不会把钟停掉或打乱。
            gs.inbox.Add(new InboxMail
            {
                id = "T057-MAIL", sender = "运营部", role = "航线呈报",
                subject = "关于 PEK—SHA 的时刻确认", body = "请知悉。",
                status = "info", kind = "report", receivedYear = 1, receivedQuarter = 1
            });
            var inboxGo = Own(new GameObject("T057Inbox", typeof(RectTransform)));
            var inbox = inboxGo.AddComponent<InboxPanelUI>();
            Assert.IsNotNull(inbox, "收件箱面板没建起来");

            double realB = 0, simB = 0;
            yield return RealWait(gs, 120f, (r, s) => { realB = r; simB = s; });
            Debug.Log($"[T05.7] 新建开局·面板打开期间：现实 {realB:F1}s → 游戏 {simB:F1} 分");
            AssertOneX(realB, simB, "新建开局·面板打开期间");
            Assert.AreEqual(1f, Time.timeScale, 0.001f,
                "打开收件箱不得把时钟压成暂停（那是危机暂停的专属行为）");

            // 两段合计仍须对得上——面板刷新不得造成跳表或多走。
            AssertOneX(realA + realB, simA + simB, "新建开局·两段累计");

            // ── 2) 有到期强制待办时**应该**停表（T05.3 设计），停表期间一秒都不许走 ──
            gs.quarter = 4;                          // 让第 1 季收的那封待办件越过搁置期限
            gs.inbox.Add(new InboxMail
            {
                id = "T057-DUE", sender = "运营部", role = "时刻槽提案",
                subject = "到期待办", body = "", status = "pending", action = "slot_select",
                receivedYear = 1, receivedQuarter = 1, deadlineQuarters = 2
            });
            yield return null;                       // 让 TimeManager.Update 看到到期件
            Assert.IsTrue(GameSpeed.IsPaused, "到期强制待办必须停表（否则玩家会错过强制决策）");

            DateTime frozen = gs.SimUtc;
            double realC = 0, simC = 0;
            yield return RealWait(gs, 90f, (r, s) => { realC = r; simC = s; });
            Assert.AreEqual(0d, simC, 1e-6d,
                "停表期间游戏时间不得前进（停表语义 = 玩家处理完才能继续）");
            Assert.IsTrue(gs.SimUtc == frozen, "停表期间 SimUtc 不得变化");
            Debug.Log($"[T05.7] 到期停表：现实 {realC:F1}s → 游戏 {simC:F1} 分（应为 0）");
        }

        [UnityTest, Explicit, Timeout(900000)]
        public IEnumerator OneX_TakeoverPacing_RunsTheRealClockAtOneToOne()
        {
            var gs = NewState("T057Takeover");
            var profile = TakeoverWorldBuilder.BuildPreview(PresetAirlines.Regional);
            TakeoverWorldBuilder.Apply(gs, profile);
            StartClock();

            Assert.Greater(gs.fleet.Count, 0, "前置：接管开局必须带着机队进局，否则测不到真实开局节奏");
            // 接管局的邮箱本来就可能有件，先清空以免「到期停表」干扰纯节奏测量。
            gs.inbox.Clear();
            gs.pendingInbox.Clear();

            double real = 0, sim = 0;
            yield return RealWait(gs, 180f, (r, s) => { real = r; sim = s; });
            Debug.Log($"[T05.7] 接管开局·纯等待：现实 {real:F1}s → 游戏 {sim:F1} 分，" +
                      $"机队 {gs.fleet.Count} 架、现金 {gs.cash:F1} 万、季度 {gs.year}Q{gs.quarter}");
            AssertOneX(real, sim, "接管开局·纯等待");
            Assert.AreEqual(1f, Time.timeScale, 0.001f, "继续推进不得被压成暂停");
        }
    }
}
