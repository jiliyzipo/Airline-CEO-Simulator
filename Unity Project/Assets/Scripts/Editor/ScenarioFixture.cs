#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEditor;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 场景夹具：把任务书里「需要先凑出某种局面」的手测前置条件做成一次点击。
    ///
    /// 为什么要有它 —— T05.2 的缺员／停飞／维护、T05.6 的缺槽与备选时刻，
    /// 这些条款都要求「先让游戏进入某个状态」才能观察。此前只能手工凑：
    /// 裁到机组不足、把唯一执飞机退租、去热点机场抢不到槽 … 一次十几分钟，
    /// 而且不可逆（B4 清单明写「别在 T03UI 上做」）。结果就是 B 类观察长期没跑。
    ///
    /// 设计约束：
    /// <list type="number">
    /// <item><b>只在 Play 模式改内存态，不写存档文件</b>。夹具不碰 PlayerPrefs，
    ///   退出 Play 即恢复，因此不会污染存档，也不存在「改坏了怎么回滚」的问题。
    ///   想保留现场就自己走一次游戏内存档。</item>
    /// <item><b>只做「把状态推到目标」，不做「恢复」</b>。逆向恢复要还原一整套派生量
    ///   （月预测、机组缓存、地面判定），做不干净反而制造假象；需要恢复就重进 Play。</item>
    /// <item><b>判定口径复用生产代码</b>：缺员读 <see cref="GameState.CabinFilled"/> /
    ///   <see cref="GameState.CabinEstablishment"/>，停飞读
    ///   <see cref="FlightGroundLogic.RouteOf"/>，缺槽读 <see cref="RoutePermitLogic.SlotsAvailable"/>。
    ///   夹具不自己发明「算不算缺员」的判据，否则手测结论会与游戏内显示对不上。</item>
    /// <item><b>已经处于目标状态时返回 false 并说明原因</b>，不假装「已生效」。</item>
    /// </list>
    /// </summary>
    public static class ScenarioFixture
    {
        /* ------------------------------------------------------------------ *
         * 客舱缺员 —— T05.2「缺员提示」、B3
         * ------------------------------------------------------------------ */

        /// <summary>
        /// 把在职客舱乘务转到人才市场，直到在职数低于编制。
        ///
        /// 用「转到市场（onMarket）」而不是从 <c>staffPool</c> 删除：删除会留下
        /// 悬空的人事档案引用，而转市场正是游戏内离职的真实路径，派生量（薪酬、
        /// 战力乘数）都按同一条路径重算。
        /// </summary>
        public static bool CrewShortage(GameState gs, out string report)
        {
            report = null;
            if (gs == null) return Refuse("GameState 未就绪", out report);
            int before = gs.CabinFilled;
            int establishment = gs.CabinEstablishment;
            if (before < establishment)
            {
                report = $"已经是缺员状态：在职 {before} / 编制 {establishment}，无需再改。";
                return false;
            }
            if (establishment <= 0)
            {
                report = "编制为 0（机队为空或座位数为 0），这种情况下游戏判定「不缺员」，"
                         + "先把机队规模做起来再试。";
                return false;
            }
            if (gs.staffPool == null) gs.staffPool = new List<PersonnelData>();

            // 缺口按「编制 - 目标在职」算，留 1 名在岗：全部转走会让缺员提示
            // 退化成「乘务 0 人」的极端态，反而看不出「缺多少人」这类文案。
            int target = Mathf.Max(0, establishment - 1);
            int released = 0;
            for (int i = 0; i < gs.staffPool.Count && gs.CabinFilled > target; i++)
            {
                var p = gs.staffPool[i];
                if (p == null || p.onMarket || !GameState.IsCabinCrew(p)) continue;
                p.onMarket = true;
                released++;
            }
            gs.InvalidateCrewCache();
            report = $"客舱缺员已构造：转出 {released} 名，在职 {before} → {gs.CabinFilled} / 编制 {establishment}"
                     + $"（缺口 {Mathf.Max(0, establishment - gs.CabinFilled)} 人）。"
                     + "去 n43 运力调整提交一次加密请求，看呈报的机组段怎么说。";
            return released > 0;
        }

        /* ------------------------------------------------------------------ *
         * 停飞 / 维护 —— T05.2「无机·停飞提示」「维护停场」、B4 / B5
         * ------------------------------------------------------------------ */

        /// <summary>
        /// 把所有在役飞机标为不可用（退租/退役语义）。
        ///
        /// 判据取 <c>isActive=false</c>：<see cref="FlightGroundLogic.OfRegistration"/>
        /// 对它的结论是 <c>Retired</c>，属「持续性停飞」，因此航线卡会立刻显示
        /// 「⚠ …」+「停飞不计营收」，而不是一闪而过的瞬时冲突。
        /// </summary>
        public static bool GroundFleet(GameState gs, out string report)
        {
            report = null;
            if (gs == null) return Refuse("GameState 未就绪", out report);
            if (gs.fleet == null || gs.fleet.Count == 0)
                return Refuse("机队为空，没有可停飞的飞机。", out report);

            int grounded = 0;
            var regs = new StringBuilder();
            for (int i = 0; i < gs.fleet.Count; i++)
            {
                var ac = gs.fleet[i];
                if (ac == null || !ac.isActive) continue;
                ac.isActive = false;
                grounded++;
                if (regs.Length < 60) regs.Append(regs.Length > 0 ? "、" : "").Append(ac.registration);
            }
            if (grounded == 0)
            {
                report = "机队里已经没有在役飞机了（此前已停飞）。";
                return false;
            }
            report = $"已停飞 {grounded} 架（{regs}）。"
                     + "看航线卡是否显示「⚠ …」+「停飞不计营收」，以及运营页的停飞提示与决策日志。";
            return true;
        }

        /// <summary>
        /// 让第一架在役飞机进入「维护中」。判据是 <c>status=Maintenance</c>，
        /// 地面判定返回 <see cref="GroundReason.Maintenance"/>，机号后缀显示「（停场维修）」。
        /// </summary>
        public static bool SendToMaintenance(GameState gs, out string report)
        {
            report = null;
            if (gs == null) return Refuse("GameState 未就绪", out report);
            if (gs.fleet == null || gs.fleet.Count == 0)
                return Refuse("机队为空，没有可排检的飞机。", out report);
            for (int i = 0; i < gs.fleet.Count; i++)
            {
                var ac = gs.fleet[i];
                if (ac == null || !ac.isActive) continue;
                if (ac.status == AircraftStatuses.Maintenance)
                {
                    report = $"{ac.registration} 已在维护中。";
                    return false;
                }
                string was = ac.status;
                ac.status = AircraftStatuses.Maintenance;
                report = $"{ac.registration} 状态 {was} → {AircraftStatuses.Maintenance}。"
                         + "看机号后缀是否显示「（停场维修）」、下一步建议是否写「等出厂或改派」。";
                return true;
            }
            report = "机队里没有在役飞机（都被停飞了？）。";
            return false;
        }

        /* ------------------------------------------------------------------ *
         * 缺槽 —— T05.6「备选时刻表」、B7
         * ------------------------------------------------------------------ */

        /// <summary>
        /// 填满某机场某波次的空闲持仓，直到协调池「连申请都无门」。
        ///
        /// 这是 <see cref="SlotCandidateLogic"/> 摆出「备选时刻对」的触发条件：
        /// 池已满 **且** 该时刻无空闲持仓（<c>SlotPoolFull &amp;&amp; FreeSlotsAt &lt;= 0</c>）。
        /// 只从池里"少给几条"是造不出这个局面的 —— 那样游戏会直接走申请路径。
        /// </summary>
        public static bool FillPool(GameState gs, string airport, int band, out string report)
        {
            report = null;
            if (gs == null) return Refuse("GameState 未就绪", out report);
            if (string.IsNullOrWhiteSpace(airport)) return Refuse("没填机场 IATA。", out report);
            airport = airport.Trim().ToUpperInvariant();
            if (band < 0 || band > 3) return Refuse($"波次 {band} 不在 0–3 范围内。", out report);

            EnsureSlots(gs);
            int freeBefore = RoutePermitLogic.SlotsAvailable(gs, airport, band);
            if (freeBefore <= 0)
            {
                report = $"{airport} 波次{band} 的协调池已经没有余量了，无需再填。";
                return false;
            }
            var grid = ScheduleSlots.SlotGridLabels(band);
            if (grid == null || grid.Count == 0) return Refuse($"波次{band} 的网格为空。", out report);

            int added = 0;
            for (int guard = 0; guard < 512 && RoutePermitLogic.SlotsAvailable(gs, airport, band) > 0; guard++)
            {
                gs.stg.slots.Add(new SlotHolding
                {
                    airport = airport,
                    slotTime = grid[added % grid.Count],
                    inUse = true,
                });
                added++;
            }
            bool full = RoutePermitLogic.SlotPoolFull(gs, airport, band);
            report = $"{airport} 波次{band}：补 {added} 条占用持仓，可用 {freeBefore} → "
                     + $"{RoutePermitLogic.SlotsAvailable(gs, airport, band)}，池已满={full}。"
                     + "现在对这条机场下一张开线/加密请求，才会走到「备选时刻」多选题。";
            return added > 0;
        }

        /// <summary>
        /// 清空某机场的全部时刻持仓（模拟「这条线两端一条槽都没有」）。
        /// 与 <see cref="FillPool"/> 方向相反：这是把池子腾出来，用来对比
        /// 「有槽直接排」与「没槽才要选备选」的差别。
        /// </summary>
        public static bool ClearSlots(GameState gs, string airport, out string report)
        {
            report = null;
            if (gs == null) return Refuse("GameState 未就绪", out report);
            if (string.IsNullOrWhiteSpace(airport)) return Refuse("没填机场 IATA。", out report);
            airport = airport.Trim().ToUpperInvariant();
            EnsureSlots(gs);

            int removed = 0;
            for (int i = gs.stg.slots.Count - 1; i >= 0; i--)
            {
                var h = gs.stg.slots[i];
                if (h == null || !string.Equals(h.airport, airport, System.StringComparison.OrdinalIgnoreCase)) continue;
                gs.stg.slots.RemoveAt(i);
                removed++;
            }
            if (removed == 0)
            {
                report = $"{airport} 本来就没有持仓。";
                return false;
            }
            report = $"已移除 {airport} 的 {removed} 条持仓。";
            return true;
        }

        /* ------------------------------------------------------------------ *
         * 共有工具
         * ------------------------------------------------------------------ */

        /// <summary>T05.5 的四类输入样例：手测时直接抄进命令台，省去现编句子。</summary>
        public static string[] AmbiguousInputSamples()
        {
            return new[]
            {
                "询问：深圳到上海现在每天几班？",          // 询问 —— 不得改班
                "深圳到上海每天加密到 3 班",              // 明确指令 —— 走呈报
                "深圳上海那个",                            // 歧义 —— 先确认，不得直接执行
                "把那条线弄快点",                          // 未识别 —— 如实提示，不得猜
                "深圳到上海减到 1 班，然后再说",            // 多义 —— 确认后才动
            };
        }

        static void EnsureSlots(GameState gs)
        {
            if (gs.stg == null) gs.stg = new StrategyState();
            if (gs.stg.slots == null) gs.stg.slots = new List<SlotHolding>();
        }

        static bool Refuse(string why, out string report)
        {
            report = why;
            return false;
        }

        /* ------------------------------------------------------------------ *
         * 菜单入口：只在 Play 模式可用，且不写存档
         * ------------------------------------------------------------------ */

        const string MenuRoot = "AirlineCEO/场景夹具/";
        const string NeedPlay = "场景夹具需要在 Play 模式下运行：先进入游戏，再点这一项。";

        [MenuItem(MenuRoot + "打开面板", false, 90)]
        public static void OpenWindow()
        {
            var w = EditorWindow.GetWindow<ScenarioFixtureWindow>(false, "场景夹具", true);
            w.minSize = new Vector2(460f, 400f);
            w.Show();
        }

        [MenuItem(MenuRoot + "客舱缺员（T05.2 缺员提示）", false, 91)]
        public static void MenuCrewShortage()
        {
            if (!Application.isPlaying || GameState.Instance == null) { Warn(NeedPlay); return; }
            Run(CrewShortage(GameState.Instance, out string report), "客舱缺员", report);
        }

        [MenuItem(MenuRoot + "全机队停飞（T05.2 无机·停飞）", false, 92)]
        public static void MenuGroundFleet()
        {
            if (!Application.isPlaying || GameState.Instance == null) { Warn(NeedPlay); return; }
            Run(GroundFleet(GameState.Instance, out string report), "全机队停飞", report);
        }

        [MenuItem(MenuRoot + "单机进入维护（T05.2 维护停场）", false, 93)]
        public static void MenuMaintenance()
        {
            if (!Application.isPlaying || GameState.Instance == null) { Warn(NeedPlay); return; }
            Run(SendToMaintenance(GameState.Instance, out string report), "维护停场", report);
        }

        [MenuItem(MenuRoot + "列出歧义输入样例（T05.5）", false, 94)]
        public static void MenuSamples()
        {
            var sb = new StringBuilder("T05.5 四类输入样例（抄进命令台逐条试）：\n\n");
            var samples = AmbiguousInputSamples();
            for (int i = 0; i < samples.Length; i++) sb.Append(i + 1).Append(". ").Append(samples[i]).Append('\n');
            sb.Append("\n判读：询问不得改班；歧义先确认；未识别如实提示；拒绝确认不产生后果。");
            Debug.Log("[场景夹具] " + sb);
            EditorUtility.DisplayDialog("T05.5 输入样例", sb.ToString(), "确定");
        }

        /// <summary>
        /// 自检入口：EditMode 下直接跑 <see cref="ScenarioFixtureRegression"/>，
        /// 不必开 Test Runner。夹具造出来的局面是不是游戏认的那个局面，这里说了算。
        /// </summary>
        [MenuItem(MenuRoot + "运行夹具回归（EditMode 自检）", false, 95)]
        public static void MenuSelfCheck()
        {
            bool ok = ScenarioFixtureRegression.Run();
            EditorUtility.DisplayDialog("场景夹具回归",
                ok ? "全部通过。详见 Console「场景夹具回归」。"
                   : "存在失败项：看 Console 里的 ✗ 行。",
                "确定");
        }

        static void Run(bool ok, string title, string report)
        {
            string text = string.IsNullOrEmpty(report) ? "（夹具未给出说明）" : report;
            Debug.Log($"[场景夹具] {title}：{(ok ? "已生效" : "未生效")} —— {text}");
            EditorUtility.DisplayDialog(title, text, "确定");
        }

        static void Warn(string text) => EditorUtility.DisplayDialog("场景夹具", text, "确定");
    }

    /// <summary>带机场/波次输入的薄面板：时刻槽夹具需要两个参数，菜单项给不了。</summary>
    public class ScenarioFixtureWindow : EditorWindow
    {
        string _airport = "PEK";
        int _band;
        Vector2 _scroll;
        readonly List<string> _log = new List<string>();

        static readonly string[] BandNames = { "早（06:30–09:30）", "午（11:30–14:00）", "晚（16:30–20:00）", "夜（20:00–23:00）" };

        void OnGUI()
        {
            bool ready = Application.isPlaying && GameState.Instance != null;

            EditorGUILayout.LabelField("场景夹具", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                ready
                    ? "Play 模式已就绪。夹具只改内存态，不写存档；退出 Play 即恢复。"
                    : "需要先在 Play 模式下进入游戏。夹具不会写存档文件。",
                ready ? MessageType.Info : MessageType.Warning);

            using (new EditorGUI.DisabledScope(!ready))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("机组 / 机队（T05.2）", EditorStyles.boldLabel);
                if (GUILayout.Button("客舱缺员：把乘务转到人才市场，直到在职 < 编制"))
                    Apply(ScenarioFixture.CrewShortage(GameState.Instance, out string r), "客舱缺员", r);
                if (GUILayout.Button("全机队停飞：所有在役机标为不可用"))
                    Apply(ScenarioFixture.GroundFleet(GameState.Instance, out string r), "全机队停飞", r);
                if (GUILayout.Button("单机进入维护：第一架在役机 status=维护中"))
                    Apply(ScenarioFixture.SendToMaintenance(GameState.Instance, out string r), "维护停场", r);

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("时刻槽（T05.6）", EditorStyles.boldLabel);
                _airport = EditorGUILayout.TextField("机场 IATA", _airport);
                _band = EditorGUILayout.Popup("波次", _band, BandNames);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("填满池（制造申请无门）"))
                        Apply(ScenarioFixture.FillPool(GameState.Instance, _airport, _band, out string r), "填满时刻池", r);
                    if (GUILayout.Button("清空该机场持仓"))
                        Apply(ScenarioFixture.ClearSlots(GameState.Instance, _airport, out string r), "清空持仓", r);
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("指令输入（T05.5）", EditorStyles.boldLabel);
                if (GUILayout.Button("列出四类输入样例"))
                {
                    _log.Clear();
                    var samples = ScenarioFixture.AmbiguousInputSamples();
                    for (int i = 0; i < samples.Length; i++) _log.Add($"{i + 1}. {samples[i]}");
                }
            }

            EditorGUILayout.Space();
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(120f));
            for (int i = _log.Count - 1; i >= 0; i--) EditorGUILayout.LabelField(_log[i], EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndScrollView();
        }

        void Apply(bool ok, string title, string report)
        {
            string text = string.IsNullOrEmpty(report) ? "（夹具未给出说明）" : report;
            _log.Add($"{(ok ? "✔" : "✘")} {title}：{text}");
            Debug.Log($"[场景夹具] {title}：{(ok ? "已生效" : "未生效")} —— {text}");
            Repaint();
        }
    }
}
#endif
