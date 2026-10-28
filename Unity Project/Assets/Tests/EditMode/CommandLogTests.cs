using System.Collections.Generic;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using NUnit.Framework;
using UnityEngine;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// 命令台历史 / 回执回溯 / 收藏。
    ///
    /// 纯逻辑、不碰 UI：UI 那条路径（点历史 → 填字 + 显示旧回执，绝不重放）由
    /// `CommandBarView.OnHistoryLink` 保证，并在界面手测里确认。
    /// </summary>
    public class CommandLogTests
    {
        GameState _gs;
        GameObject _go;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("CommandLogTests");
            _gs = _go.AddComponent<GameState>();
            _gs.commandLog = new List<CommandLogEntry>();
            _gs.commandFavorites = new List<string>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void Record_KeepsNewestFirst_AndDedupesOnlyAdjacentIdenticalPairs()
        {
            CommandLog.Record(_gs, "汇报一下公司整体情况", "回执｜顾问已回函", true, "第 1 天 08:00");
            CommandLog.Record(_gs, "汇报一下公司整体情况", "回执｜顾问已回函", true, "第 1 天 08:00");
            Assert.AreEqual(1, _gs.commandLog.Count, "紧邻的完全相同一条必须去重");

            // 同一句话、不同回执 = 两次真实操作（再下一次、这次被拒），不能吞
            CommandLog.Record(_gs, "汇报一下公司整体情况", "没听懂这句话。", false, "第 1 天 09:00");
            Assert.AreEqual(2, _gs.commandLog.Count);
            Assert.IsFalse(_gs.commandLog[0].ok);
            Assert.AreEqual("没听懂这句话。", _gs.commandLog[0].receipt);
        }

        [Test]
        public void Record_IgnoresBlankAndCapsAtMaxEntries_FromTheTail()
        {
            CommandLog.Record(_gs, "  ", "x", true, "");
            Assert.AreEqual(0, _gs.commandLog.Count, "空白指令不占历史");

            for (int i = 0; i < CommandLog.MaxEntries + 5; i++)
                CommandLog.Record(_gs, $"第{i}条", $"回执{i}", true, "第 1 天");

            Assert.AreEqual(CommandLog.MaxEntries, _gs.commandLog.Count);
            Assert.AreEqual($"第{CommandLog.MaxEntries + 4}条", _gs.commandLog[0].raw, "最新一条不能被挤掉");
            Assert.AreEqual("第5条", _gs.commandLog[_gs.commandLog.Count - 1].raw, "淘汰必须从最旧的一端");
        }

        [Test]
        public void Recall_WalksBackwardsFromNewest_AndStopsAtTheEnd()
        {
            CommandLog.Record(_gs, "A", "", true, "");
            CommandLog.Record(_gs, "B", "", true, "");
            CommandLog.Record(_gs, "C", "", true, "");

            Assert.AreEqual("C", CommandLog.Recall(_gs, 0));
            Assert.AreEqual("B", CommandLog.Recall(_gs, 1));
            Assert.AreEqual("A", CommandLog.Recall(_gs, 2));
            Assert.AreEqual("", CommandLog.Recall(_gs, 3), "越界要返回空串，不能取到别人的指令");
            Assert.AreEqual("", CommandLog.Recall(_gs, -1));
        }

        [Test]
        public void Amend_RewritesTheMatchingEntry_WithoutAddingANewOne()
        {
            CommandLog.Record(_gs, "关线：北京—上海", "已弹确认卡（尚未执行）", true, "第 2 天 10:00");
            Assert.IsTrue(CommandLog.Amend(_gs, "关线：北京—上海", "已执行｜已停飞", true));

            Assert.AreEqual(1, _gs.commandLog.Count, "Amend 不能追加条目（否则一句话看起来下过两遍）");
            Assert.AreEqual("已执行｜已停飞", _gs.commandLog[0].receipt);
            Assert.IsFalse(CommandLog.Amend(_gs, "从没下过的令", "x", true));
        }

        [Test]
        public void Favorites_SurviveClearHistory_AndCapAtTwelve()
        {
            Assert.IsTrue(CommandLog.ToggleFavorite(_gs, "把北京—上海加密到每天 3 班"));
            Assert.IsTrue(CommandLog.IsFavorite(_gs, "把北京—上海加密到每天 3 班"));

            CommandLog.Record(_gs, "随便一句", "", true, "");
            CommandLog.ClearHistory(_gs);
            Assert.IsTrue(CommandLog.IsFavorite(_gs, "把北京—上海加密到每天 3 班"),
                "清历史不能连坐收藏");

            Assert.IsFalse(CommandLog.ToggleFavorite(_gs, "把北京—上海加密到每天 3 班"), "再点一次应取消");

            for (int i = 0; i < CommandLog.MaxFavorites + 3; i++)
                CommandLog.ToggleFavorite(_gs, $"收藏{i}");

            Assert.AreEqual(CommandLog.MaxFavorites, CommandLog.Favorites(_gs).Count);
            Assert.AreEqual($"收藏{CommandLog.MaxFavorites + 2}", CommandLog.Favorites(_gs)[0],
                "最新收藏应在最前，超限淘汰最旧");
        }

        [Test]
        public void Ensure_IsIdempotent_AndDoesNotWipeExistingData()
        {
            _gs.commandLog = null;
            _gs.commandFavorites = null;
            CommandLog.Ensure(_gs);

            Assert.IsNotNull(_gs.commandLog);
            Assert.IsNotNull(_gs.commandFavorites);
            Assert.AreEqual(0, _gs.commandLog.Count);

            CommandLog.Record(_gs, "保住了吗", "是的", true, "");
            CommandLog.ToggleFavorite(_gs, "保住了吗");
            CommandLog.Ensure(_gs);
            CommandLog.Ensure(_gs);

            Assert.AreEqual(1, _gs.commandLog.Count, "反复 Ensure 不能洗掉已有历史");
            Assert.IsTrue(CommandLog.IsFavorite(_gs, "保住了吗"));
        }
    }
}
