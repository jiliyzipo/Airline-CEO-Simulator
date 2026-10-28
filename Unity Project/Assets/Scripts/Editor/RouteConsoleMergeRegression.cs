#if UNITY_EDITOR
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI;
using TMPro;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 单航线页「命令台合并」的隔离回归（2026-10-28 用户裁定）。
    ///
    /// 背景：n41 单航线页底部原来自带一个「运力调整指令」输入框，和全局 CEO 命令台
    /// 是**两条入口做同一件事**（同一条 <c>CapacityDirectiveLogic</c> 管线）。
    /// 用户裁定删掉页面内那个，统一走命令台。
    ///
    /// 删框会顺手带走一样东西：那个框是靠「CEO 正看着哪条线」认线的，
    /// 「加密到每天 3 班」不带城对也能办；而命令台原先只认城对或班号。
    /// 于是**同一个页面上同一句话**会从「照办」变成「没听懂」—— 入口合并不该等于能力缩水。
    ///
    /// 本回归钉四件事：
    /// <list type="number">
    /// <item>有页面上下文时，不带城对的调令落到那条线；</item>
    /// <item>没有上下文时**仍然不猜**（否则以后任何页面都可能改错线）；</item>
    /// <item>显式点了城对/班号的句子优先于上下文（上下文是兜底，不是覆盖）；</item>
    /// <item>页面里不再有第二个输入框（防止有人把框加回来却没接上下文）。</item>
    /// </list>
    ///
    /// 不宣称的部分：命令台回执条的实际观感、输入框焦点行为 —— 属界面手测。
    /// </summary>
    public static class RouteConsoleMergeRegression
    {
        const string PageId = "n41_单航线";

        public static bool Run()
        {
            bool all = true;
            all &= CheckContextRouting();
            all &= CheckExactRouteByContext();
            all &= CheckNoDirectiveBox();
            all &= CheckPanelHasNoInput();

            if (all) Debug.Log("单航线页命令台合并：上下文兜底 + 显式优先 + 问句不误判 + 无第二个输入框，全部通过");
            return all;
        }

        /// <summary>①②③：路由层——上下文兜底、无上下文不猜、显式声明优先、问句不被当调令。</summary>
        static bool CheckContextRouting()
        {
            bool all = true;
            const string Raw = "加密到每天 3 班";

            // ① 有上下文：落到那条线，且没假装读出了城对
            var ctx = CeoCommandRouter.Route(PageId, Raw, null, "CTU-SZX-1");
            bool ctxOk = ctx.ok
                         && ctx.kind == CeoCommandRouter.CeoAskKind.Act
                         && ctx.topic == CeoCommandRouter.TopicCapacity
                         && ctx.contextRouteKey == "CTU-SZX-1"
                         && string.IsNullOrEmpty(ctx.origin);
            all &= ctxOk;
            if (!ctxOk)
            {
                Debug.LogError(
                    "命令台合并-① 带页面上下文仍没认成调令："
                    + $"ok={ctx.ok} kind={ctx.kind} topic={ctx.topic} ctxKey={ctx.contextRouteKey ?? "(null)"}｡"
                    + "若这条红了，说明删掉页面输入框后「加密到每天 3 班」在单航线页上已无人受理");
            }

            // ② 没有上下文：不猜（旧行为必须保留，否则任何页面上一句话都可能改错线）
            var noCtx = CeoCommandRouter.Route(PageId, Raw, null, null);
            bool noCtxOk = !noCtx.ok && noCtx.topic != CeoCommandRouter.TopicCapacity;
            all &= noCtxOk;
            if (!noCtxOk)
            {
                Debug.LogError(
                    $"命令台合并-② 没有页面上下文时也把「{Raw}」认成了调令（topic={noCtx.topic}）："
                    + "这会替 CEO 猜一条线去改班次，猜错事后看不出来");
            }

            // ③ 显式城对优先：上下文只兜底，绝不覆盖 CEO 亲口点明的对象
            var explicitPair = CeoCommandRouter.Route(PageId, "把北京—上海加密到每天 3 班", null, "CTU-SZX-1");
            bool pairOk = explicitPair.ok
                          && explicitPair.topic == CeoCommandRouter.TopicCapacity
                          && explicitPair.origin == "PEK" && explicitPair.dest == "SHA"
                          && string.IsNullOrEmpty(explicitPair.contextRouteKey);
            all &= pairOk;
            if (!pairOk)
            {
                Debug.LogError(
                    "命令台合并-③ 上下文盖掉了显式城对："
                    + $"origin={explicitPair.origin} dest={explicitPair.dest} ctxKey={explicitPair.contextRouteKey ?? "(null)"}｡"
                    + "期望走北京—上海，不被页面里那条线顶替");
            }

            // ④ 问句不是调令（「为什么周三不排班」若被当成加密，就真的去加密了）
            var question = CeoCommandRouter.Route(PageId, "这条线为什么周三不排班？", null, "CTU-SZX-1");
            bool questionOk = question.topic != CeoCommandRouter.TopicCapacity
                              && question.kind != CeoCommandRouter.CeoAskKind.Act;
            all &= questionOk;
            if (!questionOk) Debug.LogError("命令台合并-④ 问句被当成了调令：" + question.topic);

            return all;
        }

        /// <summary>
        /// ⑤⑥：同一机场对两条线时，上下文必须精确落到 CEO 看的那一条。
        ///
        /// 这是最容易被忽略的一档：`CapacityDirective` 里本来有一道「同城对多条线就要求指明班号」
        /// 的护栏，若上下文定位也走那道护栏，页面上「加密到每天 2 班」会被回一句「请指明班号」——
        /// 而 CEO 明明就站在那条线的详情页上。
        /// </summary>
        static bool CheckExactRouteByContext()
        {
            bool all = true;

            // GameState 是 MonoBehaviour：只能用 AddComponent 造。
            // 用 `new GameState{...}` 能编过，但 Unity 的 == 会把它判成 null，
            // 于是 DeptReportLogic 一律回「游戏尚未就绪」—— 本回归第一版就是这么假绿的（2026-10-28 实测）。
            var go = new GameObject("__route_console_gs__");
            try
            {
                var gs = go.AddComponent<GameState>();
                gs.inbox = new List<InboxMail>();
                gs.fleet = new List<Aircraft>();
                gs.activeRoutes = new List<RouteData>
                {
                    new RouteData
                    {
                        routeId = "PEK-SHA-1", origin = "PEK", destination = "SHA",
                        aircraftType = "A320neo", dailyProfit = 2.0f, weeklyFlights = 7
                    },
                    new RouteData
                    {
                        routeId = "PEK-SHA-2", origin = "PEK", destination = "SHA",
                        aircraftType = "A320neo", dailyProfit = 1.0f, weeklyFlights = 7
                    },
                };
                const string Raw = "加密到每天 2 班";

                // ⑤ 上下文 = 第二条：落到 PEK-SHA-2，不能因为同城对而要求指明班号
                var askCtx = CeoCommandRouter.Route(PageId, Raw, null, "PEK-SHA-2");
                var ansCtx = DeptReportLogic.Build(gs, askCtx, Raw);
                bool ctxOk = ansCtx.ok && ansCtx.needsRun && ansCtx.routeKey == "PEK-SHA-2";
                all &= ctxOk;
                if (!ctxOk)
                {
                    Debug.LogError(
                        "命令台合并-⑤ 页面上下文没落到指定航线："
                        + $"ok={ansCtx.ok} needsRun={ansCtx.needsRun} routeKey={ansCtx.routeKey ?? "(null)"}"
                        + $" note={ansCtx.note ?? "(null)"}；期望 PEK-SHA-2");
                }

                // ⑥ 但**不带上下文、只给同城对**时护栏仍在：不许在这两条里挑一条改
                var askPair = CeoCommandRouter.Route("n12_航线网络", "北京—上海加密到每天 2 班");
                var ansPair = DeptReportLogic.Build(gs, askPair, "北京—上海加密到每天 2 班");
                bool guardOk = !ansPair.ok && (ansPair.note ?? "").Contains("请指明班号")
                               && gs.activeRoutes.Count == 2;
                all &= guardOk;
                if (!guardOk)
                {
                    Debug.LogError(
                        "命令台合并-⑥ 同城对多条线时的护栏被上下文改动带塌了："
                        + $"ok={ansPair.ok} note={ansPair.note ?? "(null)"} routes={gs.activeRoutes.Count}；"
                        + "期望拒绝并要求指明班号、且两条线都还在");
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }

            return all;
        }

        /// <summary>⑦ 页面结构：单航线页里不该再有任何输入框（防止那条「已删除」的框换个名字又回来）。</summary>
        static bool CheckNoDirectiveBox()
        {
            var go = RouteDetailPanel.Build("单航线");
            try
            {
                if (!InputsFound(go.gameObject, out string names)) return true;
                Debug.LogError(
                    $"命令台合并-⑦ 单航线页又出现了输入框（{names}）："
                    + "页面内不再保留运力指令框，一律走底部命令台");
                return false;
            }
            finally
            {
                Object.DestroyImmediate(go.gameObject);
            }
        }

        /// <summary>
        /// 判定：给定根节点里数到几个 TMP_InputField。抽成函数是为了能**反向自证**（⑧）。
        /// </summary>
        static bool InputsFound(GameObject root, out string names)
        {
            names = "";
            if (root == null) return false;
            var inputs = root.GetComponentsInChildren<TMP_InputField>(true);
            if (inputs == null || inputs.Length == 0) return false;
            for (int i = 0; i < inputs.Length; i++)
                names += (i > 0 ? "、" : "") + (inputs[i] != null ? inputs[i].name : "(null)");
            return true;
        }

        /// <summary>
        /// ⑧ canary：⑦ 的判据必须抓得住「框又回来」这件事。
        ///
        /// 与收件箱那条同一条规矩：首跑即绿的守卫型断言默认视为什么都没测。
        /// 这里现场造一个**确实带** TMP_InputField 的节点喂给同一个判定，
        /// 它必须报告「找到了」。若它说没有，⑦ 就永远会绿。
        /// </summary>
        static bool CheckPanelHasNoInput()
        {
            var probe = new GameObject("__probe_input__", typeof(RectTransform));
            try
            {
                probe.AddComponent<TMP_InputField>();
                if (InputsFound(probe, out _)) return true;
                Debug.LogError(
                    "命令台合并-⑧ canary 失效：判定数不出一个真的 TMP_InputField，"
                    + "说明 ⑦（页面里没有输入框）永远会绿，抓不住「框又加回来」");
                return false;
            }
            finally
            {
                Object.DestroyImmediate(probe);
            }
        }
    }
}
#endif
