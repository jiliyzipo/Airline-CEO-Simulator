using System;
using System.Collections.Generic;
using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 一条已下达过的 CEO 指令。存「原话 + 当时的回执 + 时点」三样，
    /// 目的是**回溯**而不是重放：点开历史只看当时发生了什么，不会再把指令跑一遍。
    /// </summary>
    [Serializable]
    public class CommandLogEntry
    {
        /// <summary>CEO 原话（召回时原样填回输入框）。</summary>
        public string raw;
        /// <summary>当时命令条上那一行回执。空串表示当时没有回执（例如只弹了确认卡）。</summary>
        public string receipt;
        /// <summary>时点标签（「第 3 天 14:20」），由调用方按当前游戏时钟生成。</summary>
        public string stamp;
        /// <summary>true = 受理 / 执行成功；false = 被拒或未识别。仅用于着色。</summary>
        public bool ok;
    }

    /// <summary>
    /// 命令台的历史与收藏（T 系列 §19「命令台历史指令 / 回执回溯 / 指令收藏」）。
    ///
    /// 三条设计约束，都是为了让「历史」不会变成第二个账本：
    /// · **只记录，不参与判定**。历史里存了什么，不影响任何路由、台账或账务；
    ///   把整个 `commandLog` 清空，游戏行为一字不变。
    /// · **召回只填字，不执行**。上下键与点击历史把原话填回输入框，回执单独显示；
    ///   绝不因为「点了一下历史」就重下一遍指令 —— 关线那种写指令重放一次就是事故。
    /// · **收藏是独立列表，不是历史里的标记位**。历史有 60 条上限，收藏必须活得更久；
    ///   若把 favorite 挂在 entry 上，清一次历史收藏就全丢。
    /// </summary>
    public static class CommandLog
    {
        /// <summary>历史条数上限。够翻回几次交班，又不至于让存档无限长。</summary>
        public const int MaxEntries = 60;
        /// <summary>收藏上限。超出时淘汰最久未用的（见 <see cref="ToggleFavorite"/>）。</summary>
        public const int MaxFavorites = 12;

        /// <summary>老存档没有这两个字段时的兜底；必须幂等（每次调用都不能丢已有数据）。</summary>
        public static void Ensure(GameState gs)
        {
            if (gs == null) return;
            if (gs.commandLog == null) gs.commandLog = new List<CommandLogEntry>();
            if (gs.commandFavorites == null) gs.commandFavorites = new List<string>();
        }

        /// <summary>
        /// 记一条。最新在最前（与收件箱同向，UI 从上往下读就是「刚刚 → 更早」）。
        ///
        /// 去重只针对**紧邻的完全相同一条**：同一句话同一条回执连着记两次，是回车被派发两遍
        /// 或玩家手抖，不是两次真实操作。不同回执的同一句话要照记 —— 「再次下令、这次被拒」
        /// 本身就是信息。
        /// </summary>
        public static void Record(GameState gs, string raw, string receipt, bool ok, string stamp)
        {
            Ensure(gs);
            string text = (raw ?? "").Trim();
            if (text.Length == 0) return;   // 空回车不占历史（命令台本来就不受理）

            string body = receipt ?? "";
            if (gs.commandLog.Count > 0)
            {
                var head = gs.commandLog[0];
                if (head != null && string.Equals(head.raw, text, StringComparison.Ordinal)
                    && string.Equals(head.receipt, body, StringComparison.Ordinal))
                    return;
            }

            gs.commandLog.Insert(0, new CommandLogEntry
            {
                raw = text,
                receipt = body,
                stamp = stamp ?? "",
                ok = ok
            });

            while (gs.commandLog.Count > MaxEntries)
                gs.commandLog.RemoveAt(gs.commandLog.Count - 1);
        }

        /// <summary>最近 <paramref name="count"/> 条（0 或负数 = 全部）。返回的是内部列表的视图，只读用途。</summary>
        public static IReadOnlyList<CommandLogEntry> Recent(GameState gs, int count)
        {
            Ensure(gs);
            if (count <= 0 || count >= gs.commandLog.Count) return gs.commandLog;
            var list = new List<CommandLogEntry>(count);
            for (int i = 0; i < count; i++) list.Add(gs.commandLog[i]);
            return list;
        }

        /// <summary>
        /// 按偏移取原话：0 = 最近一条，1 = 上一条……越界返回空串。
        /// 上下键翻历史用它，翻到头就停在最后一条（与终端的行为一致）。
        /// </summary>
        public static string Recall(GameState gs, int offset)
        {
            Ensure(gs);
            if (offset < 0 || offset >= gs.commandLog.Count) return "";
            var e = gs.commandLog[offset];
            return e != null ? (e.raw ?? "") : "";
        }

        /// <summary>取某条历史当时的回执（回溯用）；越界返回空串。</summary>
        public static string ReceiptAt(GameState gs, int offset)
        {
            Ensure(gs);
            if (offset < 0 || offset >= gs.commandLog.Count) return "";
            var e = gs.commandLog[offset];
            return e != null ? (e.receipt ?? "") : "";
        }

        /// <summary>
        /// 把最近一条原话匹配的历史改写回执。
        ///
        /// 用途只有一个：写指令先弹确认卡、之后才执行/取消 —— 若在执行时才追加一条新历史，
        /// 同一句话会在列表里出现两次，看起来像「下过两遍令」。所以改写那条受理记录，
        /// 让它从「已弹确认卡（尚未执行）」变成真实结果。
        /// 找不到匹配（例如历史被清过）就返回 false，静默放行 —— 历史是体验数据，不该挡住执行。
        /// </summary>
        public static bool Amend(GameState gs, string raw, string receipt, bool ok)
        {
            Ensure(gs);
            string text = (raw ?? "").Trim();
            if (text.Length == 0) return false;
            for (int i = 0; i < gs.commandLog.Count; i++)
            {
                var e = gs.commandLog[i];
                if (e == null || !string.Equals(e.raw, text, StringComparison.Ordinal)) continue;
                e.receipt = receipt ?? "";
                e.ok = ok;
                return true;
            }
            return false;
        }

        public static bool IsFavorite(GameState gs, string raw)
        {
            Ensure(gs);
            string text = (raw ?? "").Trim();
            if (text.Length == 0) return false;
            for (int i = 0; i < gs.commandFavorites.Count; i++)
                if (string.Equals(gs.commandFavorites[i], text, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// 收藏 / 取收。返回**操作后**是否在收藏里。
        /// 新收藏插到最前（最近用的在最上）；超出上限时淘汰列表末尾那条，而不是拒绝新增 ——
        /// 「收藏满了」不该成为玩家在一次操作里需要处理的错误。
        /// </summary>
        public static bool ToggleFavorite(GameState gs, string raw)
        {
            Ensure(gs);
            string text = (raw ?? "").Trim();
            if (text.Length == 0) return false;

            for (int i = 0; i < gs.commandFavorites.Count; i++)
            {
                if (!string.Equals(gs.commandFavorites[i], text, StringComparison.Ordinal)) continue;
                gs.commandFavorites.RemoveAt(i);
                return false;
            }

            gs.commandFavorites.Insert(0, text);
            while (gs.commandFavorites.Count > MaxFavorites)
                gs.commandFavorites.RemoveAt(gs.commandFavorites.Count - 1);
            return true;
        }

        public static IReadOnlyList<string> Favorites(GameState gs)
        {
            Ensure(gs);
            return gs.commandFavorites;
        }

        /// <summary>清历史但保留收藏（收藏是玩家显式保留的东西，清历史不该连坐）。</summary>
        public static void ClearHistory(GameState gs)
        {
            Ensure(gs);
            gs.commandLog.Clear();
        }
    }

    /// <summary>存档块：历史 + 收藏。两者一起存，避免读档后收藏对不上历史。</summary>
    [Serializable]
    public class CommandLogSave
    {
        public CommandLogEntry[] entries;
        public string[] favorites;
    }
}
