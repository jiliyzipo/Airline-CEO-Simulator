using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using NUnit.Framework;
using UnityEngine;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// 航材采购「批准 → 到期扣款入库」全链路回归（2026-11-03 用户报障）。
    ///
    /// 报障原文：「12月2日航材采购邮件，点批准后，下方没有提示。到17日，没有收到入库邮件。」
    /// 这两句是**两个**独立缺陷：
    /// <list type="number">
    /// <item>批准回执没显示 —— UI 层把提示条挤在 8px 高的底栏里（已无批复按钮时 footer=8，
    ///   而提示条自身 offsetMin.y 也是 8，高度算成负数，于是「什么都没弹」）。那条提示是本功能
    ///   唯一的到期日告知，看不见等于 CEO 不知道钱哪天扣。</item>
    /// <item>到期没有回执 —— 本用例钉住模型层：批准后必须排定绝对到期时刻，
    ///   到期当日必须扣款、入库并投递「航材已到货入库」回执。若这条失败，说明结算没跑。</item>
    /// </list>
    /// </summary>
    public class PartsOrderDaySettlementEditModeTests
    {
        static GameState NewGame(out GameObject host)
        {
            host = new GameObject("__partsday__");
            var gs = host.AddComponent<GameState>();
            // 编辑模式下 MonoBehaviour.Awake 不执行：静态入口与懒建容器都得手动补一次，
            // 否则 ConfirmOrder 里的 `GameState.Instance` 是 null，扣款会被当成「无 GameState」跳过。
            gs.EnsureDefaults();
            BindInstance(gs);
            gs.year = 1;
            gs.quarter = 1;
            gs.simDay = 1;
            gs.cash = 5000f;
            gs.inbox = new List<InboxMail>();
            gs.pendingInbox = new List<PendingInboxMail>();
            gs.activeRoutes = new List<RouteData>();
            gs.fleet = new List<Aircraft>();
            gs.SetSimUtc(new DateTime(2026, 12, 2, 4, 6, 0, DateTimeKind.Utc));
            return gs;
        }

        /// <summary>把静态入口指向本用例造的状态（Awake 在编辑模式下不跑）。</summary>
        static void BindInstance(GameState gs)
        {
            var prop = typeof(GameState).GetProperty(
                "Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(prop, "GameState.Instance 改了名字，这条回归需要跟着改");
            prop.GetSetMethod(true).Invoke(null, new object[] { gs });
        }

        static InboxMail AddOrderAndMail(GameState gs, int leadDays, bool urgent, out PurchaseOrder order)
        {
            var mnt = MaintenanceManager.EnsureState(gs);
            order = new PurchaseOrder
            {
                orderNo = "PO-001",
                partNo = "PN-ENG-001",
                supplier = "OEM 原厂",
                quantity = 5,
                unitCostUsd = 120000f,
                leadDays = leadDays,
                urgent = urgent,
                status = "草稿"
            };
            mnt.orders.Add(order);

            var mail = new InboxMail
            {
                id = "m_po_PO-001",
                sender = "机务工程部",
                role = "航材采购",
                glyph = "机",
                kind = "alert",
                action = "parts_order",
                origin = "PO-001",
                dest = "PN-ENG-001",
                subject = "缺件预警：请批准采购 PN-ENG-001",
                body = "",
                status = "pending",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = InboxManager.DefaultDeadlineQuarters
            };
            InboxManager.Add(gs, mail);
            return mail;
        }

        static InboxMail FindReceipt(GameState gs, string contains)
        {
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m != null && !string.IsNullOrEmpty(m.subject) && m.subject.Contains(contains)) return m;
            }
            return null;
        }

        /// <summary>推进到 <paramref name="days"/> 天后，按每天一次的顺序走投递 + 结算（与 TimeManager 同序）。</summary>
        static void AdvanceDays(GameState gs, int days)
        {
            for (int d = 0; d < days; d++)
            {
                gs.SetSimUtc(gs.SimUtc.AddDays(1));
                InboxManager.DeliverDue(gs, gs.SimUtc);
                InboxManager.SettleDueEffects(gs, gs.SimUtc);
            }
            FlushPending(gs);
        }

        /// <summary>
        /// 把一个工作日内的待投邮件放出来。
        ///
        /// 部门的回执不是当场入箱的 —— <see cref="InboxCadence.ReplyHours"/>（4 小时）后才落笔，
        /// 这样「刚点完批准，回执就已经躺在收件箱」不会显得部门没干活。真实游玩时时钟连续走，
        /// 这几个小时自然跨过去；本用例按天跳，就必须自己把这段时间走掉，否则会把
        /// 「回执排队中」误判成「回执永远不来」。
        /// </summary>
        static void FlushPending(GameState gs, int hours = 8)
        {
            for (int h = 0; h < hours; h++)
            {
                gs.SetSimUtc(gs.SimUtc.AddHours(1));
                InboxManager.DeliverDue(gs, gs.SimUtc);
            }
        }

        [Test]
        public void Approve_NonUrgent_SchedulesAbsoluteDueAndKeepsEffectPending()
        {
            var gs = NewGame(out var host);
            try
            {
                var mail = AddOrderAndMail(gs, 15, false, out _);
                Assert.IsTrue(InboxManager.Approve(gs, mail, out string msg), msg);

                Assert.AreEqual("approved", mail.status);
                Assert.IsTrue(mail.effectPending, "待结算标记没留着，那笔钱永远不会扣");
                Assert.AreEqual(gs.SimUtc.AddDays(15).Ticks, mail.dueUtcTicks);
                StringAssert.Contains("12月17日", msg, "批准回执必须给出具体到件日，不能只写季度");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void DueDay_DeductsCash_StocksParts_AndDeliversReceipt()
        {
            var gs = NewGame(out var host);
            try
            {
                var mail = AddOrderAndMail(gs, 15, false, out var order);
                float before = gs.cash;
                Assert.IsTrue(InboxManager.Approve(gs, mail, out string msg), msg);

                // 第 14 天：还不该扣（早一天都不行，否则「按交期」就是空话）
                AdvanceDays(gs, 14);
                Assert.AreEqual(before, gs.cash, 0.01f, "到期前一天就扣款，等于交期没生效");
                Assert.IsNull(FindReceipt(gs, "航材已到货入库"));

                // 第 15 天到期：扣款 + 入库 + 回执
                AdvanceDays(gs, 1);
                float total = MaintenanceEconomics.OrderTotalWan(order.quantity, order.unitCostUsd);
                Assert.AreEqual(before - total, gs.cash, 0.01f, "到期当日必须扣款");
                Assert.AreEqual("已下单", order.status);

                var part = gs.mnt.parts.Find(p => p.partNo == "PN-ENG-001");
                Assert.IsNotNull(part);
                Assert.GreaterOrEqual(part.quantity, 5, "到货没有入库");

                Assert.IsNotNull(FindReceipt(gs, "航材已到货入库"),
                    "到期没发入库回执：玩家只看到现金少了，不知道那笔钱花在哪");
                Assert.IsFalse(mail.effectPending, "结算完必须清掉待结算标记，否则每天重复扣款");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void DueDay_CashShort_FailsHonestly_AndNeverCharges()
        {
            var gs = NewGame(out var host);
            try
            {
                var mail = AddOrderAndMail(gs, 15, false, out var order);
                Assert.IsTrue(InboxManager.Approve(gs, mail, out string msg), msg);

                gs.cash = 1f;   // 到期时现金不足
                AdvanceDays(gs, 16);

                Assert.AreEqual(1f, gs.cash, 0.01f, "现金不足不许扣成负数");
                Assert.AreEqual("草稿", order.status, "没付钱就不能算已下单");
                Assert.IsNotNull(FindReceipt(gs, "航材未能入库"),
                    "失败也要有回执，否则玩家以为钱被吞了");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void UrgentParts_StillChargesOnTheSpot()
        {
            var gs = NewGame(out var host);
            try
            {
                var mail = AddOrderAndMail(gs, 3, true, out var order);
                float before = gs.cash;
                Assert.IsTrue(InboxManager.Approve(gs, mail, out string msg), msg);

                float total = MaintenanceEconomics.OrderTotalWan(order.quantity, order.unitCostUsd);
                Assert.AreEqual(before - total, gs.cash, 0.01f, "AOG 件必须当场扣款：它等不起 3 天交期");
                Assert.IsFalse(mail.effectPending, "当场扣完就不能再排一次到期扣款，否则重复扣钱");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// 底栏注释条（批准回执 / 截止说明）必须有**正**高度。
        /// 旧版 footer 在「无批复按钮」时恒为 8，而注释条下沿也是 8、上沿取 footer−4 —— 高度 −4，
        /// 于是每封「已批准」的邮件底部都是空白，正是「点批准后下方没有提示」的直接来源。
        /// </summary>
        [Test]
        public void FooterNote_HasPositiveHeight_WhenThereIsSomethingToSay()
        {
            float withBar = AirlineCEO.UI.Shell.InboxPanelUI.FooterHeight(true, true);
            float noteOnly = AirlineCEO.UI.Shell.InboxPanelUI.FooterHeight(false, true);
            float silent = AirlineCEO.UI.Shell.InboxPanelUI.FooterHeight(false, false);

            Assert.Greater(AirlineCEO.UI.Shell.InboxPanelUI.FooterNoteHeight(noteOnly), 0f,
                "没有批复按钮时提示条高度为负：批准回执会被画到画布外，玩家看到的就是「什么都没有」");
            Assert.GreaterOrEqual(AirlineCEO.UI.Shell.InboxPanelUI.FooterNoteHeight(noteOnly),
                AirlineCEO.UI.Shell.InboxPanelUI.FooterNoteMinHeight,
                "提示条放不下两行字，长一点的到期说明会被截掉");
            Assert.Greater(AirlineCEO.UI.Shell.InboxPanelUI.FooterNoteHeight(withBar), 0f,
                "有批复按钮时提示条也不许塌掉（按钮与说明同屏）");
            Assert.AreEqual(8f, silent, "没话说时底栏仍应收回，别白占正文高度");
        }

        /// <summary>
        /// 入库这件事**只**由收件箱批复驱动：模型层任何一条走到 `ConfirmOrder` 的路径都必须留下到货回执
        /// （订单页已于 2026-11-03 改成只读，此用例守住「别再有静默入库」这条不变量）。
        /// </summary>
        [Test]
        public void DirectConfirm_StillEmitsArrivalReceipt()
        {
            var gs = NewGame(out var host);
            try
            {
                AddOrderAndMail(gs, 15, false, out var order);
                float before = gs.cash;

                var mm = MaintenanceManager.EnsureInstance();
                Assert.IsNotNull(mm, "编辑模式下 EnsureInstance 必须补上静态入口（Awake 不跑）");
                Assert.IsTrue(mm.ConfirmOrder(order.orderNo, out string msg), msg);

                float total = MaintenanceEconomics.OrderTotalWan(order.quantity, order.unitCostUsd);
                Assert.AreEqual(before - total, gs.cash, 0.01f, "当场入库必须当场扣款");

                FlushPending(gs);
                var receipt = FindReceipt(gs, "航材已到货入库");
                Assert.IsNotNull(receipt, "入库却没有到货回执 —— 玩家只能看到现金变少");
                StringAssert.Contains(order.orderNo, receipt.subject);
                StringAssert.Contains("到货入库", receipt.body, "回执要交代流程走到哪一步");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// 现金不足不再成为死局（用户裁定删掉订单页「确认下单」之后新增的兜底）。
        ///
        /// 旧版 `SettleDueEffects` 一进来就清 `effectPending`，失败一次就再没人管这单；
        /// 而那时唯一的补救入口正是订单页那个按钮。按钮撤掉后必须由结算自己顺延重试：
        /// 现金补足的那天自动扣款入库并发到货回执。
        /// </summary>
        [Test]
        public void CashShort_ReArmsAndRetriesNextDay_ThenDeliversReceipt()
        {
            var gs = NewGame(out var host);
            try
            {
                var mail = AddOrderAndMail(gs, 15, false, out var order);
                Assert.IsTrue(InboxManager.Approve(gs, mail, out string msg), msg);

                gs.cash = 1f;
                AdvanceDays(gs, 16);
                Assert.IsNotNull(FindReceipt(gs, "航材未能入库"), "失败也要有回执");
                Assert.IsTrue(mail.effectPending, "现金不足的常规单必须留在途，顺延重试；否则这单永远不入库");
                Assert.IsNotNull(InboxManager.FindPendingPartsMail(gs, order.orderNo));

                // 补足现金 → 次日重试应当自动做掉
                gs.cash = 5000f;
                AdvanceDays(gs, 2);
                Assert.AreEqual("已下单", order.status, "现金够了下一次重试仍没成交");
                Assert.IsNotNull(FindReceipt(gs, "航材已到货入库"), "重试成功却没有到货回执");
                Assert.IsFalse(mail.effectPending, "成交之后不许再挂着待结算");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// 走**真实时钟**的那条路：`TimeManager.AdvanceGameMinutes` 逐日 tick → DeliverDue → SettleDueEffects。
        ///
        /// 别的用例都是直接调 `SettleDueEffects`，只证明「结算函数本身是对的」，证明不了
        /// 「玩家的时钟真的会走到它」。报障说「18 号没有收到」，所以要钉住的是整条时钟链路。
        /// </summary>
        [Test]
        public void RealClockTicks_SettleTheOrder_AndDeliverReceipt()
        {
            var gs = NewGame(out var host);
            try
            {
                gs.simDay = 63;   // 与 2026-12-02 对齐（10/1 = 第 1 天），避免季末提前触发
                var order = default(PurchaseOrder);
                var mail = AddOrderAndMail(gs, 15, false, out order);
                float before = gs.cash;
                Assert.IsTrue(InboxManager.Approve(gs, mail, out string msg), msg);

                var tm = host.AddComponent<TimeManager>();
                for (int d = 0; d < 20; d++) tm.AdvanceGameMinutes(gs, 1440);

                float total = MaintenanceEconomics.OrderTotalWan(order.quantity, order.unitCostUsd);
                // 20 天里每天还有固定成本在扣，所以不能钉等值，只能钉「至少扣了这笔采购款」
                Assert.LessOrEqual(gs.cash, before - total + 0.01f,
                    "时钟走满 20 天还没扣款：结算没被时钟触发");
                Assert.AreEqual("已下单", order.status);
                Assert.IsNotNull(FindReceipt(gs, "航材已到货入库"),
                    "时钟链路走完却没收到到货回执");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        /// <summary>一张单只能有一封到货回执（两条路径都触发时不许发两封）。</summary>
        [Test]
        public void ArrivalReceipt_IsIdempotentPerOrder()
        {
            var gs = NewGame(out var host);
            try
            {
                AddOrderAndMail(gs, 15, false, out var order);
                Assert.IsTrue(MaintenanceManager.EnsureInstance().ConfirmOrder(order.orderNo, out _));
                FlushPending(gs);
                int first = gs.inbox.FindAll(m => m != null && !string.IsNullOrEmpty(m.subject)
                    && m.subject.Contains("航材已到货入库")).Count;
                Assert.AreEqual(1, first);

                // 再结算一次（模拟重复触发）：不许再出一封
                InboxManager.SettleDueEffects(gs, gs.SimUtc);
                FlushPending(gs);
                int second = gs.inbox.FindAll(m => m != null && !string.IsNullOrEmpty(m.subject)
                    && m.subject.Contains("航材已到货入库")).Count;
                Assert.AreEqual(first, second, "同一单发了多封到货回执");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// 状态行必须报「哪天到货」。
        ///
        /// 采购函的 kind 是 alert（缺件预警），旧判据把 kind=alert 一律当成「外部通报」，
        /// 于是批准后底栏写「不适用（外部通报）｜何时推进 —」。到期日就藏在里面，
        /// CEO 只能干等到期再怀疑人生。
        /// </summary>
        [Test]
        public void ApprovedPartsMail_StatusLine_ShowsDueDate_NotExternalBulletin()
        {
            var gs = NewGame(out var host);
            try
            {
                var mail = AddOrderAndMail(gs, 15, false, out _);
                Assert.IsTrue(InboxManager.Approve(gs, mail, out string msg), msg);

                string line = InboxManager.StatusLine(gs, mail);
                StringAssert.DoesNotContain("外部通报", line,
                    "采购函是等交割的业务件，不是外部通报");
                StringAssert.Contains("交割", line, "批准后应明说在等交割/落地");
                StringAssert.Contains("12月17日", line, "状态行必须给出具体到货日");
                StringAssert.Contains("否", line);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// 失败件与成功件分开记 id：先报「未能入库」、后来补足现金才成交的单子，
        /// 必须还能收到一封到货回执（失败记录不能把后来的成功回执挡掉）。
        /// </summary>
        [Test]
        public void FailedReceipt_DoesNotBlockLaterArrivalReceipt()
        {
            var gs = NewGame(out var host);
            try
            {
                var mail = AddOrderAndMail(gs, 15, false, out var order);
                Assert.IsTrue(InboxManager.Approve(gs, mail, out string msg), msg);
                gs.cash = 1f;
                AdvanceDays(gs, 16);
                Assert.IsNotNull(FindReceipt(gs, "航材未能入库"));

                gs.cash = 5000f;
                Assert.IsTrue(MaintenanceManager.EnsureInstance().ConfirmOrder(order.orderNo, out string msg2), msg2);
                FlushPending(gs);
                Assert.IsNotNull(FindReceipt(gs, "航材已到货入库"),
                    "补足现金做掉之后没有到货回执：先前的失败件把后来的成功回执挡住了");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// 订单页（n105）必须能认出「已批复、按交期在途」的单子，才能把状态如实写成
        /// 「已批复 · 在途」而不是「草稿 · 待批复」；到货后这个标记要消失。
        /// </summary>
        [Test]
        public void PendingSettlement_IsDiscoverable_AndClearedAfterArrival()
        {
            var gs = NewGame(out var host);
            try
            {
                var mail = AddOrderAndMail(gs, 15, false, out var order);
                Assert.IsNull(InboxManager.FindPendingPartsMail(gs, order.orderNo),
                    "还没批准就不该算「已排定」");

                Assert.IsTrue(InboxManager.Approve(gs, mail, out string msg), msg);
                Assert.AreSame(mail, InboxManager.FindPendingPartsMail(gs, order.orderNo),
                    "批准后在途的单子必须能被订单页认出来，否则状态会误报成「草稿」");

                AdvanceDays(gs, 15);
                Assert.IsNull(InboxManager.FindPendingPartsMail(gs, order.orderNo),
                    "到货后不该还挂着「在途」");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// 报障的真正病根：机务状态此前**完全没有进存档**（`SaveManager` 里连 KeyMnt 都没有，
        /// `MaintenanceSave` 这个包装类写了却没人用）。于是重开游戏后订单表归零，
        /// 而收件箱里那封采购函是存过档的 —— 再点批准只能报「未找到订单」，接着被兜底标成已批准。
        /// 这里把「键必须登记」钉死：新增存档字段忘了登记，快照（时间胶囊/分支/滚动自动存档）就少一块。
        /// </summary>
        [Test]
        public void MaintenanceState_IsRegisteredInSaveKeys()
        {
            CollectionAssert.Contains(SaveManager.AllSaveKeys, "ACEO_Mnt",
                "机务状态没登记进 AllSaveKeys：时间胶囊/分支存档/滚动自动存档都会漏掉航材与订单");
        }

        [Test]
        public void MaintenanceSave_RoundTripKeepsOrdersAndStock()
        {
            var json = JsonUtility.ToJson(new MaintenanceSave
            {
                state = new MaintenanceState
                {
                    parts = new List<PartStock>
                    {
                        new PartStock { partNo = "PN-ENG-001", quantity = 6, safetyStock = 10, unitCostUsd = 120000f }
                    },
                    orders = new List<PurchaseOrder>
                    {
                        new PurchaseOrder
                        {
                            orderNo = "PO-001", partNo = "PN-ENG-001", quantity = 5,
                            unitCostUsd = 120000f, leadDays = 15, status = "草稿"
                        }
                    }
                }
            });
            var back = JsonUtility.FromJson<MaintenanceSave>(json);
            Assert.IsNotNull(back?.state);
            Assert.AreEqual(1, back.state.orders.Count, "存档 JSON 丢了采购订单，读档后批准就找不到它");
            Assert.AreEqual("PO-001", back.state.orders[0].orderNo);
            Assert.AreEqual(15, back.state.orders[0].leadDays, "交期没跟着存下来，到货日就无从计算");
            Assert.AreEqual(1, back.state.parts.Count);
        }

        /// <summary>
        /// 订单真丢了（旧档）也要能救：采购函把自己的价量参数写在 payloadCsv 里，
        /// 批准时按函重建订单，扣款与入库照常排定。
        /// </summary>
        [Test]
        public void Approve_OrderLost_RebuildsFromMailPayload()
        {
            var gs = NewGame(out var host);
            try
            {
                var mnt = MaintenanceManager.EnsureState(gs);
                mnt.parts.Add(new PartStock
                {
                    partNo = "PN-ENG-001", quantity = 3, safetyStock = 10, unitCostUsd = 120000f
                });
                var draft = new PurchaseOrder
                {
                    orderNo = "PO-001",
                    partNo = "PN-ENG-001",
                    supplier = "OEM 原厂",
                    quantity = 5,
                    unitCostUsd = 120000f,
                    leadDays = 15,
                    status = "草稿"
                };
                mnt.orders.Add(draft);
                Assert.IsTrue(InboxManager.QueuePartsMail(gs, draft));
                var mail = gs.inbox[0];
                Assert.AreEqual("parts_order", mail.action);
                StringAssert.Contains("|", mail.payloadCsv, "采购函没带价量参数，丢档后就救不回来了");

                // 模拟旧档：读档后机务状态整块为空，只有邮件活了下来
                gs.mnt = new MaintenanceState();

                Assert.IsTrue(InboxManager.Approve(gs, mail, out string msg), msg);
                Assert.IsTrue(mail.effectPending, "按函重建后仍须排定到期扣款");
                Assert.AreEqual(gs.SimUtc.AddDays(15).Ticks, mail.dueUtcTicks);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        /// <summary>旧档发的函（既没订单也没参数）：不许报假账，要如实说清楚怎么办。</summary>
        [Test]
        public void Approve_NoOrderNoPayload_ReportsHonestly_AndDoesNotFakeApproval()
        {
            var gs = NewGame(out var host);
            try
            {
                var mail = new InboxMail
                {
                    id = "m_po_PO-999",
                    sender = "机务工程部",
                    action = "parts_order",
                    origin = "PO-999",
                    dest = "PN-HYD-002",
                    subject = "缺件预警：请批准采购 PN-HYD-002",
                    status = "pending",
                    receivedYear = gs.year,
                    receivedQuarter = gs.quarter
                };
                InboxManager.Add(gs, mail);

                Assert.IsFalse(InboxManager.Approve(gs, mail, out string msg),
                    "订单不在账上却返回成功，等于把没办的事报成办完了");
                StringAssert.Contains("不在账上", msg);
                Assert.AreEqual("pending", mail.status, "失败就不许把邮件标成已批准");
                Assert.IsFalse(mail.effectPending);
                Assert.IsTrue(InboxManager.IsTransactionalAction("parts_order"),
                    "采购是付钱换货：批准失败时 UI 不许走「结案并标已批准」的兜底");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }
    }
}
