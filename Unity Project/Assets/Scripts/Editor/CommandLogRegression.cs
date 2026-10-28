#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 命令台历史 / 回执回溯 / 收藏的隔离回归。
    ///
    /// 钉住的都是**容易被"体验功能"这个身份豁免掉**的边界：
    /// ① 去重只压「紧邻且完全相同」的一条 —— 同一句话配上不同回执（再次下令、这次被拒）必须照记；
    /// ② 60 条上限从**队尾**淘汰，不能把新的一条挤掉；
    /// ③ `Amend` 只改最近那条匹配原话的条目，且找不到时返回 false 而不抛；
    /// ④ 收藏与历史分家：清历史不得连坐收藏；收藏满 12 条淘汰最旧；
    /// ⑤ `Ensure` 幂等 —— 老存档没有这两个字段时，反复调用不能把已有数据洗掉。
    ///
    /// 本回归不碰 UI；"点历史会不会重放指令"由 `CommandBarView.OnHistoryLink` 的注释与手测保证，
    /// 因为那条路径只有 TMP 点击才走得到。
    /// </summary>
    public static class CommandLogRegression
    {
        public static bool Run()
        {
            var go = new GameObject("__command_log__");
            try
            {
                var gs = go.AddComponent<GameState>();
                gs.commandLog = new List<CommandLogEntry>();
                gs.commandFavorites = new List<string>();
                bool all = true;

                // —— ① 去重：紧邻相同 → 只留一条 ——
                CommandLog.Record(gs, "汇报一下公司整体情况", "回执｜顾问已回函", true, "第 1 天 08:00");
                CommandLog.Record(gs, "汇报一下公司整体情况", "回执｜顾问已回函", true, "第 1 天 08:00");
                bool dedupeOk = gs.commandLog.Count == 1;
                all &= dedupeOk;
                if (!dedupeOk) Debug.LogError($"命令台-① 紧邻重复未去重：条数 {gs.commandLog.Count}，期望 1");

                // —— ①b 同句不同回执 → 必须记两条 ——
                CommandLog.Record(gs, "汇报一下公司整体情况", "没听懂这句话。", false, "第 1 天 09:00");
                bool distinctOk = gs.commandLog.Count == 2 && gs.commandLog[0].ok == false;
                all &= distinctOk;
                if (!distinctOk)
                    Debug.LogError($"命令台-①b 同句不同回执被吞：条数 {gs.commandLog.Count}，期望 2（最新为失败态）");

                // —— ①c 空话不占历史 ——
                CommandLog.Record(gs, "   ", "x", true, "第 1 天 09:01");
                bool blankOk = gs.commandLog.Count == 2;
                all &= blankOk;
                if (!blankOk) Debug.LogError($"命令台-①c 空白指令占了历史：条数 {gs.commandLog.Count}，期望 2");

                // —— ② 上限 60：新条目挤掉最旧，不回退写坏 ——
                CommandLog.ClearHistory(gs);
                for (int i = 0; i < CommandLog.MaxEntries + 7; i++)
                    CommandLog.Record(gs, $"第{i}条", $"回执{i}", true, "第 1 天");
                bool capOk = gs.commandLog.Count == CommandLog.MaxEntries;
                bool newestKept = gs.commandLog[0].raw == $"第{CommandLog.MaxEntries + 6}条";
                bool oldestDropped = gs.commandLog[gs.commandLog.Count - 1].raw == "第7条";
                all &= capOk && newestKept && oldestDropped;
                if (!capOk) Debug.LogError($"命令台-② 历史超限未截断：{gs.commandLog.Count}，期望 {CommandLog.MaxEntries}");
                if (!newestKept) Debug.LogError($"命令台-② 最新一条丢失：头部是「{gs.commandLog[0].raw}」");
                if (!oldestDropped) Debug.LogError($"命令台-② 淘汰方向错：尾部是「{gs.commandLog[gs.commandLog.Count - 1].raw}」");

                // —— ③ Amend：改写最近匹配；找不到返回 false ——
                CommandLog.ClearHistory(gs);
                CommandLog.Record(gs, "关线：北京—上海", "已弹确认卡（尚未执行）", true, "第 2 天 10:00");
                bool amendOk = CommandLog.Amend(gs, "关线：北京—上海", "已执行｜已停飞", true)
                               && gs.commandLog.Count == 1
                               && gs.commandLog[0].receipt == "已执行｜已停飞";
                all &= amendOk;
                if (!amendOk) Debug.LogError($"命令台-③ Amend 未改写：{gs.commandLog[0].receipt}");
                bool amendMiss = !CommandLog.Amend(gs, "从没下过的令", "x", true);
                all &= amendMiss;
                if (!amendMiss) Debug.LogError("命令台-③b Amend 对不存在原话返回了 true");

                // —— ④ 收藏独立：清历史不连坐；上限 12 淘汰最旧 ——
                bool favAdd = CommandLog.ToggleFavorite(gs, "把北京—上海加密到每天 3 班");
                bool favListed = CommandLog.IsFavorite(gs, "把北京—上海加密到每天 3 班")
                                 && CommandLog.Favorites(gs).Count == 1;
                CommandLog.ClearHistory(gs);
                bool favSurvives = CommandLog.IsFavorite(gs, "把北京—上海加密到每天 3 班");
                bool favOff = !CommandLog.ToggleFavorite(gs, "把北京—上海加密到每天 3 班")
                              && CommandLog.Favorites(gs).Count == 0;
                all &= favAdd && favListed && favSurvives && favOff;
                if (!favAdd || !favListed) Debug.LogError("命令台-④ 收藏未生效");
                if (!favSurvives) Debug.LogError("命令台-④b 清历史把收藏连坐清掉了");
                if (!favOff) Debug.LogError("命令台-④c 再点一次未取消收藏");

                for (int i = 0; i < CommandLog.MaxFavorites + 3; i++)
                    CommandLog.ToggleFavorite(gs, $"收藏{i}");
                bool favCap = CommandLog.Favorites(gs).Count == CommandLog.MaxFavorites;
                bool favNewest = CommandLog.Favorites(gs)[0] == $"收藏{CommandLog.MaxFavorites + 2}";
                all &= favCap && favNewest;
                if (!favCap) Debug.LogError($"命令台-④d 收藏超限未截断：{CommandLog.Favorites(gs).Count}");
                if (!favNewest) Debug.LogError($"命令台-④e 最新收藏不在头部：{CommandLog.Favorites(gs)[0]}");

                // —— ⑤ Ensure 幂等：老存档（字段为空）补默认，但不许洗掉已有数据 ——
                gs.commandLog = null;
                gs.commandFavorites = null;
                CommandLog.Ensure(gs);
                bool ensureOk = gs.commandLog != null && gs.commandFavorites != null
                                && gs.commandLog.Count == 0;
                all &= ensureOk;
                if (!ensureOk) Debug.LogError("命令台-⑤ Ensure 未补空表");

                CommandLog.Record(gs, "保住了吗", "是的", true, "第 3 天");
                int before = gs.commandLog.Count;
                CommandLog.Ensure(gs);
                CommandLog.Ensure(gs);
                bool idempotent = gs.commandLog.Count == before && before == 1;
                all &= idempotent;
                if (!idempotent) Debug.LogError($"命令台-⑤b Ensure 洗掉了已有历史：{before} → {gs.commandLog.Count}");

                Debug.Log("CEO 命令台 → 历史/回溯/收藏 " + (all ? "✓" : "✗"));
                return all;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
#endif
