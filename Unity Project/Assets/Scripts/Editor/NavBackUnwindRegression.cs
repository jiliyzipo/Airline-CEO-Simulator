#if UNITY_EDITOR
using AirlineCEO.UI;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 「深链借来安放子页的那个部门页，不许吃掉一次返回」的隔离回归。
    ///
    /// 2026-11-04 用户手测（两次）：
    ///   ① 读过渡经营邮件 → 点正文机号 → 飞机详情 → 按 &lt; 落到「机队 › 过渡业务」，再按一次才回信；
    ///   ② 修完第一版后再走一遍，落到「机队总览」，再按一次直接退到首页地图。
    /// 第二次说明：只靠 OpenSubPage when 记的临时标记不够 —— 标记一旦被任何一次额外的
    /// OpenSubPage / 切页抹掉，返回就退回「停在借来的部门页」。故判定必须**同时**认浏览历史：
    /// 历史里当前页的前一格才是用户真正来的地方。
    ///
    /// 这里钉的是那段纯函数（<see cref="PrefabNavIntegration.ShouldUnwindPastHostTab"/>）：
    /// 正例必须退、反例必须不退，外加一条 canary 证明「只认标记」的旧口径会被本判定判错。
    /// 界面是否真的回到了正文仍属界面手测，本回归不宣称已目视确认。
    /// </summary>
    public static class NavBackUnwindRegression
    {
        const string Inbox = "n7_收件箱";
        const string Fleet = "n9_机队";
        const string Network = "n12_航线网络";
        const string AircraftDetail = "n10_飞机详情_履历";
        const string SingleRoute = "n41_单航线";

        public static bool Run()
        {
            bool all = true;

            // ① 报障路径：邮件点机号 → 详情 → <。历史：… → 收件箱 → 详情。
            all &= Expect("邮件点机号返回应收件箱", Fleet, Inbox, Inbox, Fleet, true, Inbox);

            // ② 标记被抹掉的同一路径（2026-11-04 第二次复现的实际形态）：
            //    只有历史能证明「机队是借来的」，标记已经空了 —— 也必须退。
            all &= Expect("标记丢失时仍按历史退回收件箱", Fleet, Inbox, null, null, true, Inbox);

            // ③ 负面对照：用户本来就在机队里点进详情，前后两格都是机队 —— 不许弹走。
            all &= Expect("从机队点进详情应停在机队", Fleet, Fleet, null, null, false, null);

            // ④ 从航线模块点机号：借的是机队，来路是航线 —— 退到航线。
            all &= Expect("航线点机号应退回航线", Fleet, Network, Network, Fleet, true, Network);

            // ⑤ 来路本身是子页（单航线）时，靠标记拿回它所在的部门页。
            all &= Expect("单航线点机号应退回航线", Fleet, SingleRoute, Network, Fleet, true, Network);

            // ⑥ 落点不是部门页（例如连开两层子页）时一律不动，交给普通返回。
            all &= Expect("落点是子页时不介入", AircraftDetail, Inbox, Inbox, Fleet, false, null);

            // ⑦ 历史为空（第一页就是详情）时不介入，别把不会画的页面硬画出来。
            all &= Expect("无从判断时不介入", Fleet, null, null, null, false, null);

            // ⑧ canary：「只认临时标记」的旧口径必须喂出错误答案，否则上面 ② 那条断言没有意义。
            bool oldRule = MarkerOnlyRule(Fleet, null, null);   // ② 的输入
            bool newRule = PrefabNavIntegration.ShouldUnwindPastHostTab(Fleet, Inbox, null, null, out _);
            if (oldRule || !newRule)
            {
                all = false;
                Debug.LogError("返回口径-⑧ canary 失效：判定与「只认临时标记」的旧口径给出同样答案，"
                               + "说明这条检查抓不住 2026-11-04 的复发形态");
            }

            if (all) Debug.Log("返回口径：借来的部门页一次 < 就退回原页（8 组口径通过，含标记丢失与反例）");
            return all;
        }

        /// <summary>2026-11-04 第一版的判据：只看 OpenSubPage 记下的临时标记（此处仅作 canary 对照）。</summary>
        static bool MarkerOnlyRule(string landing, string markedFrom, string markedTo) =>
            !string.IsNullOrEmpty(markedFrom)
            && string.Equals(markedTo, landing, System.StringComparison.Ordinal);

        static bool Expect(string what, string landing, string historyPrevious,
            string markedFrom, string markedTo, bool wantUnwind, string wantRestore)
        {
            bool got = PrefabNavIntegration.ShouldUnwindPastHostTab(
                landing, historyPrevious, markedFrom, markedTo, out string restore);
            bool ok = got == wantUnwind
                      && (!wantUnwind || string.Equals(restore, wantRestore, System.StringComparison.Ordinal));
            if (!ok)
                Debug.LogError($"返回口径-✗ {what}：应退={wantUnwind}（退回「{wantRestore}」），"
                               + $"实际退={got}（退回「{restore}」）；"
                               + $"landing={landing} 历史前一格={historyPrevious ?? "(空)"} "
                               + $"标记={markedFrom ?? "(空)"}→{markedTo ?? "(空)"}");
            return ok;
        }
    }
}
#endif
