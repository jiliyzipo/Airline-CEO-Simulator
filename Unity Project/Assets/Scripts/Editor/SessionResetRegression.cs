#if UNITY_EDITOR
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 「设置 → 退出到开始界面」的重置口径回归（2026-10-20）。
    ///
    /// 背景：这个入口之前不存在 —— 玩家想重开一局只能重启进程。现在可以中途退回开始界面，
    /// 于是「下一局必须干净」变成了硬要求：`GameState` 是 DontDestroyOnLoad 的单例，
    /// 若不重置，接着点「新游戏 / 从零创建」会继承上一局的财年、机队、航线、收件箱与董事会。
    ///
    /// <see cref="GameState.ResetForNewGame"/> 用「临时模板实例 + 反射抄字段」实现（不手写清单，
    /// 免得像清档键表那样漂移），这组断言钉住它的四条契约：
    /// · 标量回到出厂值（现金 / 财年 / 公司名 / 终局标记 …）；
    /// · 集合清空且**沿用原实例**（外部缓存的 List 引用不会突然指向一片旧数据）；
    /// · 懒建的子状态对象（mkt / cargo / stg …）重置后非空，不会一把 NRE；
    /// · 「已订阅 OnStateChanged」的会话关系不被抄掉（HUD 刷新全靠它）。
    ///
    /// 与 <see cref="RpmPreviewRegression"/> 一样是隔离回归：只碰内存 GameState，不碰存档。
    /// </summary>
    public static class SessionResetRegression
    {
        public static void Run()
        {
            var go = new GameObject("__session_reset_gs__");
            try
            {
                var gs = go.AddComponent<GameState>();
                // 编辑模式下 Awake 不执行：先手动补齐懒建对象，后面才有一个真实可弄脏的初始状态。
                gs.EnsureDefaults();

                // 订阅关系：重置不能把它抄没，否则整条 HUD 刷新链在退出后静默失效。
                int stateChanged = 0;
                gs.OnStateChanged += () => stateChanged++;

                // —— 造一局「玩了七年的脏状态」——
                gs.cash = 12345f;
                gs.year = 7;
                gs.quarter = 3;
                gs.simDay = 42;
                gs.companyNameZh = "旧航空公司";
                gs.companyIata = "OLD";
                gs.ceoDisplayName = "上一任 CEO";
                gs.boardSatisfaction = 12f;
                gs.gameOver = true;
                gs.gameOverReason = "上一局的解雇理由";
                gs.advisorTutorialStep = 5;
                gs.advisorTutorialCompleted = true;
                gs.takeoverMode = true;
                gs.nextRegistrationNumber = 88;
                gs.firstFlightCompleted = true;
                gs.difficulty = DifficultyMode.Hard;
                gs.activeMode = 2;
                gs.legendModeActive = true;
                gs.decisionLog.Add("上一局的一条决策记录");

                var routeList = gs.activeRoutes;
                gs.activeRoutes.Add(new RouteData { origin = "PEK", destination = "LHR" });
                gs.fleet.Add(new Aircraft());
                gs.inbox.Add(new InboxMail());
                gs.boardMemberIds.Add("director_old");
                gs.currentTargets.Add(new BoardTarget());

                // 子状态对象也弄脏：重置后必须是全空的新对象，而不是沿用旧值。
                gs.mkt.fareOverrides.Add(new RouteFareOverride());
                gs.hr.employeeSatisfaction = 3f;

                gs.ResetForNewGame();

                // ① 标量回到出厂值
                bool scalars = Mathf.Abs(gs.cash - 500f) < 0.001f
                               && gs.year == 1
                               && gs.quarter == 1
                               && gs.simDay == 1
                               && gs.companyNameZh == "天际航空"
                               && gs.companyIata == "NV"
                               && gs.boardSatisfaction == 50f
                               && !gs.gameOver
                               && string.IsNullOrEmpty(gs.gameOverReason)
                               && !gs.takeoverMode
                               && gs.advisorTutorialStep == 0
                               && !gs.advisorTutorialCompleted
                               && gs.nextRegistrationNumber == 1
                               && !gs.firstFlightCompleted
                               && gs.difficulty == DifficultyMode.Normal
                               && !gs.legendModeActive
                               && gs.decisionLog.Count == 0;

                // ② 集合清空，且沿用原实例（Clear + 回填，不是换一个新 List）
                bool collections = gs.activeRoutes.Count == 0
                                   && gs.fleet.Count == 0
                                   && gs.inbox.Count == 0
                                   && gs.boardMemberIds.Count == 0
                                   && gs.currentTargets.Count == 0
                                   && ReferenceEquals(routeList, gs.activeRoutes);

                // ③ 懒建子状态对象非空（模板上补跑过 EnsureDefaults，不能抄回一堆 null）
                bool lazyStates = gs.mkt != null && gs.cargo != null && gs.stg != null
                                  && gs.commerce != null && gs.mnt != null && gs.hr != null
                                  && gs.saf != null && gs.decision != null
                                  && gs.restructuring != null && gs.fuelProcurement != null
                                  && gs.carbonCompliance != null;

                // ④ 派生字段重算：期初现金 = 重置后的现金；安全指数按出厂评级 B 推出 82
                bool derived = Mathf.Abs(gs.yearStartCash - gs.cash) < 0.001f
                               && Mathf.Abs(gs.safetyIndex - 82f) < 0.001f;

                // ⑤ 订阅关系存活：重置自身会 NotifyChanged 一次；之后再加现金仍能触发
                int afterReset = stateChanged;
                gs.AddCash(10f);
                bool events = afterReset >= 1 && stateChanged > afterReset;

                bool all = scalars && collections && lazyStates && derived && events;

                Debug.Log($"退出到开始界面 · 重置口径 → 标量出厂值={scalars}"
                          + $"（cash={gs.cash:F0} year={gs.year} company={gs.companyNameZh}）"
                          + $"　集合清空且沿用实例={collections}（routes={gs.activeRoutes.Count} 机队={gs.fleet.Count}）"
                          + $"　懒建子状态={lazyStates}　派生字段={derived}"
                          + $"　事件订阅存活={events}（重置触发 {afterReset} 次，加现金后再 +{stateChanged - afterReset}）"
                          + $"　{(all ? "✓" : "✗")}");
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }
        }
    }
}
#endif
