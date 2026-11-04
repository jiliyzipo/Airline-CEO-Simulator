using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Systems;
using NUnit.Framework;
using UnityEngine;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// 多槽位快照（时间回溯 / 时间胶囊 / What-If 分支 / 滚动自动存档）的**类型保真**回归。
    ///
    /// 2026-11-03 用户报障：「游戏快进到 11 月 19 日后保存，再打开继续，收到 11 月 3 日的邮件，现金为 0。」
    /// 病根不在 Load 的读取顺序，而在快照克隆：`CaptureToSlot` / `RestoreFromSlot` 曾用
    /// `PlayerPrefs.GetString/SetString` 无差别拷贝全部存档键 —— 收件箱、机队、订单这些本来就是
    /// 字符串的字段完好无损，而 float/int 写的键（现金、财年、季度、simDay 与公历时钟）全被写成
    /// 字符串或空串：`GetFloat` 读不到就静默返回默认值 → 现金 0、第 1 年 Q1、公历时钟落到
    /// 「现实日期 00:00」。于是玩家「回到 11 月 3 日」，而收件箱里 11 月 19 日的邮件还在。
    /// </summary>
    public class SaveSlotTypeRoundTripEditModeTests
    {
        const string TestActive = "ACEO_T_ACTIVE_";
        const string TestSlot = "ACEO_T_SLOT_";

        string _savedPrefix;

        [SetUp]
        public void SetUp()
        {
            _savedPrefix = SaveManager.ActivePrefix;
            SaveManager.ActivePrefix = TestActive;
            Cleanup();
        }

        [TearDown]
        public void TearDown()
        {
            Cleanup();
            SaveManager.ActivePrefix = _savedPrefix;
            PlayerPrefs.Save();
        }

        void Cleanup()
        {
            foreach (var k in SaveManager.AllSaveKeys)
            {
                PlayerPrefs.DeleteKey(TestActive + k);
                PlayerPrefs.DeleteKey(TestSlot + k);
            }
            PlayerPrefs.Save();
        }

        void SeedActive()
        {
            PlayerPrefs.SetFloat(TestActive + "ACEO_Cash", 2292.5f);
            PlayerPrefs.SetFloat(TestActive + "ACEO_BoardSatisfaction", 63.25f);
            PlayerPrefs.SetInt(TestActive + "ACEO_Year", 2);
            PlayerPrefs.SetInt(TestActive + "ACEO_Quarter", 3);
            PlayerPrefs.SetInt(TestActive + "ACEO_SimDay", 17);
            PlayerPrefs.SetInt(TestActive + "ACEO_SimHour", 9);
            PlayerPrefs.SetInt(TestActive + "ACEO_SimMinute", 30);
            PlayerPrefs.SetInt(TestActive + "ACEO_WallYear", 2026);
            PlayerPrefs.SetInt(TestActive + "ACEO_WallMonth", 11);
            PlayerPrefs.SetInt(TestActive + "ACEO_WallDay", 19);
            PlayerPrefs.SetString(TestActive + "ACEO_CompanyNameZh", "北辰快线");
            PlayerPrefs.SetString(TestActive + "ACEO_Inbox", "{\"items\":[]}");
            PlayerPrefs.Save();
        }

        [Test]
        public void SlotClone_KeepsNumbersAsNumbers()
        {
            SeedActive();
            // 记录平台口径：float 键用 GetString 读出来是空串还是数字文本 ——
            // 决定旧版快照里被写坏的数字是否还能按文本救回（见 LegacyStringTypedSlot_IsSalvagedOnRestore）。
            TestContext.Progress.WriteLine($"[slot-type] GetString(float 键) = '{PlayerPrefs.GetString(TestActive + "ACEO_Cash", "<无此键>")}'");
            SaveSlotManager.CaptureToSlot(TestSlot);

            Assert.AreEqual(2292.5f, PlayerPrefs.GetFloat(TestSlot + "ACEO_Cash", -1f), 0.001f,
                "现金在快照里丢了类型：读档会变成 0");
            Assert.AreEqual(17, PlayerPrefs.GetInt(TestSlot + "ACEO_SimDay", -1),
                "simDay 在快照里丢了类型");
            Assert.AreEqual(2026, PlayerPrefs.GetInt(TestSlot + "ACEO_WallYear", -1));
            Assert.AreEqual(19, PlayerPrefs.GetInt(TestSlot + "ACEO_WallDay", -1),
                "公历日期在快照里丢了类型：读档会退回现实日期");
            Assert.AreEqual("北辰快线", PlayerPrefs.GetString(TestSlot + "ACEO_CompanyNameZh", ""),
                "字符串键必须照常复制");
        }

        [Test]
        public void SlotRestore_BringsBackCashAndClock()
        {
            SeedActive();
            SaveSlotManager.CaptureToSlot(TestSlot);

            // 模拟「恢复前」的现场：主存档被别的东西打回默认值
            PlayerPrefs.SetFloat(TestActive + "ACEO_Cash", 0f);
            PlayerPrefs.SetInt(TestActive + "ACEO_SimDay", 1);
            PlayerPrefs.SetInt(TestActive + "ACEO_WallYear", 0);
            PlayerPrefs.SetInt(TestActive + "ACEO_WallDay", 0);
            PlayerPrefs.Save();

            SaveSlotManager.RestoreFromSlot(TestSlot);

            Assert.AreEqual(2292.5f, PlayerPrefs.GetFloat(TestActive + "ACEO_Cash", -1f), 0.001f,
                "恢复后现金没有回到快照值：玩家会看到现金 0");
            Assert.AreEqual(17, PlayerPrefs.GetInt(TestActive + "ACEO_SimDay", -1));
            Assert.AreEqual(2026, PlayerPrefs.GetInt(TestActive + "ACEO_WallYear", -1));
            Assert.AreEqual(19, PlayerPrefs.GetInt(TestActive + "ACEO_WallDay", -1));
        }

        /// <summary>
        /// 已经被旧版本写坏的快照（数字被存成字符串文本）也要能救回来 —— 玩家机器上现存的
        /// 时间胶囊 / 自动存档都是那个版本留下的。
        /// </summary>
        [Test]
        public void LegacyStringTypedSlot_IsSalvagedOnRestore()
        {
            PlayerPrefs.SetString(TestSlot + "ACEO_Cash", "2292.5");
            PlayerPrefs.SetString(TestSlot + "ACEO_SimDay", "17");
            PlayerPrefs.SetString(TestSlot + "ACEO_WallYear", "2026");
            PlayerPrefs.SetString(TestSlot + "ACEO_WallMonth", "11");
            PlayerPrefs.SetString(TestSlot + "ACEO_WallDay", "19");
            PlayerPrefs.SetString(TestSlot + "ACEO_CompanyNameZh", "北辰快线");
            PlayerPrefs.Save();

            SaveSlotManager.RestoreFromSlot(TestSlot);

            Assert.AreEqual(2292.5f, PlayerPrefs.GetFloat(TestActive + "ACEO_Cash", -1f), 0.001f,
                "字符串化的旧快照没有按文本救回现金");
            Assert.AreEqual(17, PlayerPrefs.GetInt(TestActive + "ACEO_SimDay", -1));
            Assert.AreEqual(19, PlayerPrefs.GetInt(TestActive + "ACEO_WallDay", -1));
        }

        /// <summary>
        /// 类型表漂移守卫：新增 SetF/SetI 写的键却忘了登记，快照就会再次把它写成字符串。
        /// 这里把「一旦丢失就会造成玩家可见损坏」的关键键钉死。
        /// </summary>
        [Test]
        public void CriticalKeys_AreClassified()
        {
            var floats = new[] { "ACEO_Cash", "ACEO_BoardSatisfaction", "ACEO_YearNet", "ACEO_LiveQExpense" };
            for (int i = 0; i < floats.Length; i++)
                Assert.AreEqual(SaveManager.PrefKind.Float, SaveManager.KindOf(floats[i]), floats[i]);

            var ints = new[]
            {
                "ACEO_Year", "ACEO_Quarter", "ACEO_SimDay", "ACEO_SimHour", "ACEO_SimMinute",
                "ACEO_WallYear", "ACEO_WallMonth", "ACEO_WallDay", "ACEO_SpeedLevel", "ACEO_FirstFlight",
            };
            for (int i = 0; i < ints.Length; i++)
                Assert.AreEqual(SaveManager.PrefKind.Int, SaveManager.KindOf(ints[i]), ints[i]);

            var strings = new[] { "ACEO_Inbox", "ACEO_Fleet", "ACEO_CompanyNameZh" };
            for (int i = 0; i < strings.Length; i++)
                Assert.AreEqual(SaveManager.PrefKind.String, SaveManager.KindOf(strings[i]), strings[i]);
        }
    }
}
