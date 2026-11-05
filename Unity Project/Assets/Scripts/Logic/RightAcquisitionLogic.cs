using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 独立航权购置（★2026-11-05 用户裁定）。
    ///
    /// 用户原话：「除了开通航线而购买航权外，航司会为了执飞不定期航班、代码共享与联营、
    /// 战略储备与占坑、航权置换而单独购买航权。」故航权申请有**两个来源**：
    /// <list type="number">
    /// <item>开航联动：国际线许可审查缺额度时，<c>InboxManager.AdvanceRoutePermit</c>
    ///   自动递 <c>apply_right</c>（既有管线，不动）；</item>
    /// <item><b>独立购置</b>：运营部每季按本类摇一份提案进收件箱，CEO 只批「买不买」。</item>
    /// </list>
    ///
    /// 两条来源递的是**同一种邮件**（<c>apply_right</c>，走 <c>QueueRightDecision</c> 的
    /// 成本/判重/交割管线），所以「航权办理进度」一栏天然把两类都列出来；
    /// 按用户要求，**申请原因不在栏里标出**（邮件正文也只写通用用途，不写本次动机）。
    ///
    /// 与时刻槽「运营部代挂闲置槽」同构：提案由运营部发，CEO 不挑机场对 ——
    /// 候选池由本类按「枢纽出发的国际航点、未持有、无在办单」筛出后随机摇。
    /// </summary>
    public static class RightAcquisitionLogic
    {
        /// <summary>每季至多递一份独立购置提案（与时刻槽代挂同节奏，避免收件箱被淹）。</summary>
        public const int MaxPerQuarter = 1;

        /// <summary>每季摇提案的概率。40 季期望约 18 份，够玩家见到这条回路又不至于刷屏。</summary>
        public const float ProposalChance = 0.45f;

        /// <summary>独立购置邮件 id 前缀（与开航联动递的单区分，供回归与统计用）。</summary>
        public const string MailIdPrefix = "m_right_stg_";

        /// <summary>季末钩子（<c>TimeManager</c> 调用）：摇一份独立购置提案进收件箱。</summary>
        public static void TickQuarter(GameState gs)
        {
            if (gs == null) return;
            if (StrategyManager.EnsureState(gs) == null) return;

            var rng = new System.Random(gs.year * 31 + gs.quarter * 57 + 9041);
            if ((float)rng.NextDouble() > ProposalChance) return;

            var pool = Candidates(gs);
            if (pool.Count == 0) return;
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                string t = pool[i]; pool[i] = pool[j]; pool[j] = t;
            }

            int made = 0;
            for (int i = 0; i < pool.Count && made < MaxPerQuarter; i++)
            {
                if (TryQueueStandalone(gs, gs.hubIata, pool[i], out _)) made++;
            }
        }

        /// <summary>
        /// 递一份独立购置提案。复用 <see cref="InboxManager.QueueRightDecision"/>：
        /// 成本、档位、按对判重、已持有拒收、10 天办结交割全部同源，不另起一套账。
        /// </summary>
        public static bool TryQueueStandalone(GameState gs, string origin, string dest, out string message)
        {
            string id = MailIdPrefix + origin + "_" + dest + "_" + gs.year + "q" + gs.quarter;
            return InboxManager.QueueRightDecision(gs, origin, dest, id, out message);
        }

        /// <summary>这封 apply_right 是不是独立购置递的（开航联动的 id 是 m_right_&lt;o&gt;_&lt;d&gt;_…）。</summary>
        public static bool IsStandalone(InboxMail m)
            => m != null && m.id != null && m.id.StartsWith(MailIdPrefix, System.StringComparison.Ordinal);

        /// <summary>候选航点：枢纽出发的国际航点（档位 &gt; 0）。判重/已持有由递单管线兜底。</summary>
        static List<string> Candidates(GameState gs)
        {
            var pool = new List<string>();
            string hub = gs != null ? gs.hubIata : null;
            if (string.IsNullOrEmpty(hub)) return pool;
            var list = AirportCatalog.Airports;
            if (list == null) return pool;
            for (int i = 0; i < list.Count; i++)
            {
                var ap = list[i];
                if (string.IsNullOrEmpty(ap.code)) continue;
                if (string.Equals(ap.code, hub, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (RoutePermitLogic.FreedomFor(gs, hub, ap.code) <= 0) continue;
                pool.Add(ap.code);
            }
            return pool;
        }
    }
}
