using System.Collections;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI;
using AirlineCEO.UI.Navigation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AirlineCEO.Tests.PlayMode
{
    /// <summary>
    /// HR 人力 + SAF 安全 PlayMode（Phase 2 Week 10）。
    /// 覆盖：导航工厂、人力/安全数值、Manager 回写 GameState。
    /// </summary>
    public class HrSafPlayModeTests : IsolatedPlayModeFixture
    {
        // —— 导航：运行时工厂可打开 HR + SAF 全部子页 ——

        [UnityTest]
        public IEnumerator RuntimeFactories_BuildAllSubpages()
        {
            var host = new GameObject("HrSafRuntimeHost", typeof(RectTransform));

            string[] hrPages = { "n86_组织规划与招聘", "n87_培训与发展", "n88_绩效与薪酬", "n89_员工关系与文化", "n90_合规与风险", "n37_工会谈判桌", "n91_人才档案与继任" };
            foreach (var page in hrPages)
            {
                var go = HrRuntimeFactory.TryCreate(page, host.transform);
                Assert.IsNotNull(go, "应能构建 " + page);
                if (page.StartsWith("n87"))
                    Assert.IsNotNull(go.GetComponent<TrainingHubPanel>(), page + " 应为培训总览");
                else
                    Assert.IsNotNull(go.GetComponent<HrSubpagePanel>(), page + " 应挂 HrSubpagePanel");
            }

            string[] safPages = { "n92_SMS与风险管理", "n93_安全监察与审核", "n94_事件调查与分析", "n95_局方与法规事务", "n96_安全文化与培训", "n100_检查应对弹窗", "n101_策略切换确认弹窗" };
            foreach (var page in safPages)
            {
                var go = SafetyRuntimeFactory.TryCreate(page, host.transform);
                Assert.IsNotNull(go, "应能构建 " + page);
                Assert.IsNotNull(go.GetComponent<SafetySubpagePanel>(), page + " 应挂 SafetySubpagePanel");
            }

            Object.Destroy(host);
            yield return null;
        }

        // —— 数值：招聘成功率/成本、人力成本占比、培训ROI、评级带、调查成本 ——

        [UnityTest]
        public IEnumerator HrSafetyEconomics_Scoring()
        {
            // 招聘成功率：紧缺度越高成功率越低
            Assert.Greater(HrEconomics.RecruitSuccessRate(0, 20f), HrEconomics.RecruitSuccessRate(0, 90f));

            // 招聘成本：猎头 > 校招 > 内部转岗（内部转岗无外部费用）
            Assert.Greater(HrEconomics.RecruitCostPerHire(1), HrEconomics.RecruitCostPerHire(0));
            Assert.Greater(HrEconomics.RecruitCostPerHire(0), HrEconomics.RecruitCostPerHire(3));
            // 紧缺成本加成真正生效（此前只是招聘页的装饰文案）
            Assert.Greater(HrEconomics.RecruitCostPerHire(0, 90f), HrEconomics.RecruitCostPerHire(0, 20f));
            Assert.Greater(HrEconomics.ShortageCostFactor(70f), HrEconomics.ShortageCostFactor(45f));
            Assert.Greater(HrEconomics.ShortageCostFactor(45f), HrEconomics.ShortageCostFactor(10f));

            // 来源标签与旧档归一
            Assert.AreEqual(HrEconomics.SourceHeadhunt, HrEconomics.SourceTag(1));
            Assert.AreEqual(HrEconomics.SourceMarket, HrEconomics.NormalizeSource(null));
            Assert.AreEqual(HrEconomics.SourceMarket, HrEconomics.NormalizeSource(""));
            Assert.AreEqual("猎头挖角", HrEconomics.SourceName(HrEconomics.SourceHeadhunt));

            // 人力成本占营收比：收入越高占比越低
            Assert.Greater(HrEconomics.LaborCostRatio(12000f, 30000f), HrEconomics.LaborCostRatio(12000f, 60000f));

            // 流失率：满意度越高流失越低
            Assert.Greater(HrEconomics.AttritionRate(40f), HrEconomics.AttritionRate(90f));

            // 培训ROI：预算越高ROI越高
            Assert.Greater(HrEconomics.TrainingRoi(1000f, 450), HrEconomics.TrainingRoi(100f, 450));
            Assert.Greater(HrEconomics.TrainingEfficiencyGain(0.9f, 0), HrEconomics.TrainingEfficiencyGain(0.4f, 0));
            // 晋升门槛吃「培训累积增益」(efficiencyBonus − 1)，不是 efficiencyBonus 本身：
            // 后者基准为 1.0，拿它跟阈值比会恒真，门槛形同虚设（旧缺陷）。
            Assert.IsTrue(HrEconomics.CanPromoteFromTraining("专员", 0.91f, 0.25f));
            Assert.IsFalse(HrEconomics.CanPromoteFromTraining("专员", 0.91f, 0.10f), "潜力够但培训积累不足");
            Assert.IsFalse(HrEconomics.CanPromoteFromTraining("专员", 0.85f, 0.30f), "培训够但潜力不足");
            Assert.IsFalse(HrEconomics.CanPromoteFromTraining("主管", 0.99f, 0.9f));

            // 培训中心等级表：3 级封顶，越界报价为 +∞ —— 这是「免费无限升级」漏洞的第二道闸门
            Assert.AreEqual(3, HrEconomics.TrainingCenterMaxLevel);
            Assert.IsTrue(HrEconomics.IsUpgradeCostPayable(HrEconomics.TrainingCenterCostWan(3)));
            Assert.IsFalse(HrEconomics.IsUpgradeCostPayable(HrEconomics.TrainingCenterCostWan(4)),
                "等级越界的报价不得是可支付金额");
            Assert.IsFalse(HrEconomics.IsUpgradeCostPayable(0f), "0 元升级必须被拒");
            Assert.Greater(HrEconomics.TrainingCenterBuildDays(1), 0, "必须有建设工期");
            Assert.Less(HrEconomics.TrainingCostFactor(3), HrEconomics.TrainingCostFactor(0), "等级越高培训越便宜");
            // 轨道闸门只关「经 HR 页启动」的轨道：机务/安全另走 MNT/SAF 页的外包入口（0 级可办），
            // 因此 maint 不得在此被挡住 —— 否则 UI 承诺（外包培训）与代码分叉。
            Assert.IsTrue(HrEconomics.TrainingTrackUnlocked("maint", 0), "机务培训走外包入口，0 级亦可");
            Assert.IsFalse(HrEconomics.TrainingTrackUnlocked("type_rating", 1), "机型改装需 2 级");
            Assert.IsTrue(HrEconomics.TrainingTrackUnlocked("type_rating", 2));
            Assert.IsFalse(HrEconomics.TrainingTrackUnlocked("cert", 0), "证照考取需 1 级起步");
            Assert.IsTrue(HrEconomics.TrainingTrackUnlocked("people", 0), "高潜管理培训 0 级即可开局");
            // 分档一致：HrEconomics 的季度长度必须与 GameState 对齐（它不能引用 GameState）
            Assert.AreEqual(GameState.DaysPerQuarter, HrEconomics.DaysPerQuarter, "季度长度常量漂移");

            // 工会谈判：让步满意度↑，强硬满意度↓
            Assert.Greater(HrEconomics.UnionSatisfactionDelta(0), HrEconomics.UnionSatisfactionDelta(1));

            // 评级带
            Assert.AreEqual('A', SafetyEconomics.RatingBand(92f));
            Assert.AreEqual('B', SafetyEconomics.RatingBand(75f));
            Assert.AreEqual('C', SafetyEconomics.RatingBand(55f));
            Assert.AreEqual('D', SafetyEconomics.RatingBand(40f));

            // 调查成本：深度 > 快速；重大 > 征候
            Assert.Greater(SafetyEconomics.InvestigationCostWan(2, 1), SafetyEconomics.InvestigationCostWan(0, 1));
            Assert.Greater(SafetyEconomics.InvestigationCostWan(1, 3), SafetyEconomics.InvestigationCostWan(1, 0));

            // 安全文化：惩罚型隐藏风险更高
            Assert.Greater(SafetyEconomics.CultureHiddenRisk(0), SafetyEconomics.CultureHiddenRisk(1));

            // 局方接管风险：D 级触发
            Assert.IsTrue(SafetyEconomics.RegulatorTakeoverRisk(40f));
            Assert.IsFalse(SafetyEconomics.RegulatorTakeoverRisk(82f));

            yield return null;
        }

        // —— Manager 回写：HR 招聘/培训/工会 ——

        [UnityTest]
        public IEnumerator HrManager_WritesState()
        {
            var gsGo = new GameObject("HrGS");
            var gs = gsGo.AddComponent<GameState>();
            gs.cash = 10000f;
            gs.fleet.Add(MakeAircraft("B-HR1", "A320"));   // 人力成本现为派生的「每机费率 × 在役机队」，需有机队
            var mgrGo = new GameObject("HrManager");
            var mgr = mgrGo.AddComponent<HrManager>();

            // 招聘回写
            Assert.IsTrue(mgr.LaunchRecruitment("飞行员", 6, 1, 0, out _));
            Assert.AreEqual(1, gs.hr.vacancies.Count);
            Assert.AreEqual(6, gs.hr.vacancies[0].needed);

            // 渠道周期真正生效：校招 24 周，未到期时候选人尚未落池
            Assert.IsFalse(mgr.RecruitNow("飞行员", out _));
            Assert.AreEqual(0, CountSource(gs, HrEconomics.SourceCampus));

            HrManager.TickDay(gs, gs.SimUtc.AddDays(24 * 7 + 1));
            Assert.Greater(CountSource(gs, HrEconomics.SourceCampus), 0);

            // 到岗回写（hired 增加，人数增加）
            int headBefore = gs.TotalHeadcount;
            Assert.IsTrue(mgr.RecruitNow("飞行员", out _));
            Assert.Greater(gs.hr.vacancies[0].hired, 0);
            Assert.Greater(gs.TotalHeadcount, headBefore);

            // Pilot recruitment does not create a high-potential management-training roster.
            gs.staffPool.Add(new PersonnelData { id = "training-manager", fullName = "培训测试主管", department = "HR",
                rank = "主管", jobTitle = "培训主管", onMarket = false, isOperational = false,
                potential = 0.8f, efficiencyBonus = 0.6f });
            // 培训回写
            Assert.IsTrue(mgr.AddTraining("高潜管理培训", 40, 180f, out _));
            Assert.AreEqual(1, gs.hr.trainings.Count);
            Assert.Greater(gs.hr.trainingRoi, 1f);

            PersonnelGenerator.EnsurePool(gs);
            PersonnelData hipo = null;
            for (int i = 0; i < gs.staffPool.Count; i++)
            {
                var p = gs.staffPool[i];
                if (p == null || p.onMarket || p.isOperational) continue;
                if (hipo == null || p.potential > hipo.potential) hipo = p;
            }
            Assert.IsNotNull(hipo);
            float pot0 = hipo.potential;
            float eff0 = hipo.efficiencyBonus;
            float lift0 = TrainingGrowthLogic.DeptStaffLift(gs, hipo.department);
            HrManager.TickQuarter(gs);
            Assert.Greater(gs.hr.trainings[0].completionRate, 0.4f);
            HrManager.TickQuarter(gs);
            Assert.IsTrue(gs.hr.trainings[0].graduated);
            Assert.Greater(hipo.efficiencyBonus, eff0);
            Assert.Greater(hipo.potential, pot0);
            Assert.IsFalse(string.IsNullOrEmpty(gs.hr.lastTrainingNote));
            Assert.Greater(TrainingGrowthLogic.DeptStaffLift(gs, hipo.department), lift0);

            // 培训中心升级：扣款即开工，等级要等工期走完才生效（此前是即时生效且 2 级后免费无限升）
            float cashBeforeCenter = gs.cash;
            Assert.IsTrue(mgr.StartTrainingCenterUpgrade(1, out _));
            Assert.AreEqual(0, gs.hr.trainingCenterLevel, "施工期内沿用原等级");
            Assert.AreEqual(1, gs.hr.trainingCenterBuildLevel);
            Assert.AreEqual(cashBeforeCenter - HrEconomics.TrainingCenterCostWan(1), gs.cash, 0.01f, "必须按报价扣款");
            Assert.IsFalse(mgr.StartTrainingCenterUpgrade(2, out _), "施工中不得再申请");
            HrManager.TickDay(gs, gs.SimUtc.AddDays(HrEconomics.TrainingCenterBuildDays(1) + 1));
            Assert.AreEqual(1, gs.hr.trainingCenterLevel, "完工后等级才生效");
            Assert.AreEqual(0, gs.hr.trainingCenterBuildLevel);
            Assert.IsFalse(mgr.StartTrainingCenterUpgrade(3, out _), "只能逐级升级，不得跳级");
            gs.cash = 50000f;
            Assert.IsTrue(mgr.StartTrainingCenterUpgrade(2, out _));
            HrManager.TickDay(gs, gs.SimUtc.AddDays(HrEconomics.TrainingCenterBuildDays(2) + 1));
            Assert.AreEqual(2, gs.hr.trainingCenterLevel);
            Assert.IsTrue(mgr.StartTrainingCenterUpgrade(3, out _));
            HrManager.TickDay(gs, gs.SimUtc.AddDays(HrEconomics.TrainingCenterBuildDays(3) + 1));
            Assert.AreEqual(3, gs.hr.trainingCenterLevel);
            Assert.IsFalse(mgr.StartTrainingCenterUpgrade(4, out _), "3 级封顶，不得再升");

            // 工会谈判回写
            float satBefore = gs.hr.union.satisfaction;
            Assert.IsTrue(mgr.NegotiateUnion(0, out _));
            Assert.Greater(gs.hr.union.satisfaction, satBefore);

            float labor0 = gs.LaborCostAnnualWan;
            float emp0 = gs.hr.employeeSatisfaction;
            float cash0 = gs.cash;
            Assert.IsTrue(mgr.ApplyCompensation("raise", out _));
            Assert.Greater(gs.LaborCostAnnualWan, labor0);
            Assert.Greater(gs.hr.employeeSatisfaction, emp0);
            Assert.Less(gs.cash, cash0);
            labor0 = gs.LaborCostAnnualWan;
            emp0 = gs.hr.employeeSatisfaction;
            Assert.IsTrue(mgr.RejectCompensation("bonus", out _));
            Assert.AreEqual(labor0, gs.LaborCostAnnualWan);
            Assert.Less(gs.hr.employeeSatisfaction, emp0);

            Object.Destroy(gsGo);
            Object.Destroy(mgrGo);
            yield return null;
        }

        /// <summary>
        /// 培训名单 + 证照（2026-10-08 重构）。
        /// 覆盖三个此前的缺口：① 入册时选人（此前选人发生在结业那一刻，玩家看不到名单）；
        /// ② 个人页「安排培训」入册与门槛保护；③ 考取证照与它作为国际航线准入的第二条路径。
        /// </summary>
        [UnityTest]
        public IEnumerator HrTraining_RosterAndCertification()
        {
            var gsGo = new GameObject("CertGS");
            var gs = gsGo.AddComponent<GameState>();
            gs.cash = 60000f;
            gs.fleet.Add(MakeAircraft("B-CERT1", "A320"));
            var mgrGo = new GameObject("CertHrManager");
            var mgr = mgrGo.AddComponent<HrManager>();
            PersonnelGenerator.EnsurePool(gs);

            // 入册门槛 0.60，开局主管档为 0.62 —— 故 0.60 的测试对象不会挤进 enrolled=1 的名单
            var spec = new PersonnelData
            {
                id = "sp_cert_a", fullName = "考证测试甲", department = "OPS", rank = "专员",
                jobTitle = "乘务员", potential = HrEconomics.TrainingEnrollMinPotential,
                onMarket = false, isOperational = false, baseSalary = 8f, loyalty = 70f, age = 30
            };
            gs.staffPool.Add(spec);

            // ① 入册时就把名单定下来
            Assert.IsTrue(mgr.AddTraining("高潜管理培训", 1, 180f, out string addMsg), addMsg);
            var prog = gs.hr.trainings[gs.hr.trainings.Count - 1];
            Assert.Greater(prog.memberIds.Count, 0, "入册必须立刻写入名单");
            Assert.AreEqual(prog.memberIds.Count, prog.enrolled, "enrolled 必须与名单一致");
            Assert.IsFalse(prog.memberIds.Contains(spec.id), "潜力更低者不得挤掉潜力更高者");
            for (int i = 0; i < prog.memberIds.Count; i++)
            {
                var m = PersonnelGenerator.Find(gs, prog.memberIds[i]);
                Assert.IsNotNull(m, "名单必须指向真实存在的人");
                Assert.IsFalse(m.onMarket);
                Assert.IsFalse(m.isOperational, "运行序列不参与关键岗培训");
                Assert.GreaterOrEqual(m.potential, HrEconomics.TrainingEnrollMinPotential);
            }

            // ② 个人页「安排培训」：把指定的人加进在办项目
            Assert.IsTrue(mgr.EnrollInProgram("高潜管理培训", spec.id, out string enrMsg), enrMsg);
            Assert.IsTrue(prog.memberIds.Contains(spec.id));
            Assert.AreEqual(prog.memberIds.Count, prog.enrolled);
            Assert.IsFalse(mgr.EnrollInProgram("高潜管理培训", spec.id, out _), "不得重复入册");

            var weak = new PersonnelData
            {
                id = "sp_cert_weak", fullName = "低潜测试", department = "OPS", rank = "主管",
                jobTitle = "乘务员", potential = 0.30f, baseSalary = 8f, loyalty = 70f, age = 40
            };
            gs.staffPool.Add(weak);
            Assert.IsFalse(mgr.EnrollInProgram("高潜管理培训", weak.id, out string weakMsg), weakMsg);
            StringAssert.Contains("入册门槛", weakMsg);

            // ③ 证照：中心等级不足时不得办理
            Assert.IsFalse(mgr.StartCertification(spec.id, CertificationCatalog.CabinSafety, out string lvMsg));
            StringAssert.Contains("培训中心", lvMsg);

            Assert.IsTrue(mgr.StartTrainingCenterUpgrade(1, out _));
            HrManager.TickDay(gs, gs.SimUtc.AddDays(HrEconomics.TrainingCenterBuildDays(1) + 1));
            Assert.AreEqual(1, gs.hr.trainingCenterLevel, "完工后等级才生效");

            Assert.IsFalse(mgr.StartCertification(spec.id, CertificationCatalog.IntlOps, out string intlMsg),
                "国际运行资质需 3 级，1 级不得办理");
            StringAssert.Contains("培训中心", intlMsg);

            float cashBeforeCert = gs.cash;
            Assert.IsTrue(mgr.StartCertification(spec.id, CertificationCatalog.CabinSafety, out string certMsg), certMsg);
            var certProg = gs.hr.trainings[gs.hr.trainings.Count - 1];
            Assert.AreEqual("cert", certProg.track);
            Assert.AreEqual(CertificationCatalog.CabinSafety, certProg.certId);
            Assert.AreEqual(1, certProg.memberIds.Count);
            Assert.AreEqual(spec.id, certProg.memberIds[0]);
            Assert.Less(gs.cash, cashBeforeCert, "考证须扣款");
            Assert.IsFalse(CertificationLogic.Has(spec, CertificationCatalog.CabinSafety), "结业前不得持证");

            Assert.Greater(certProg.durationQuarters, 0);
            for (int i = 0; i < certProg.durationQuarters; i++) HrManager.TickQuarter(gs);
            Assert.IsTrue(CertificationLogic.HasValid(spec, CertificationCatalog.CabinSafety, gs.SimUtc),
                "结业后必须持证");
            Assert.Greater(
                CertificationLogic.DaysToExpiry(CertificationLogic.Held(spec, CertificationCatalog.CabinSafety), gs.SimUtc),
                0, "有有效期的证必须写入到期日");

            var avail = CertificationLogic.AvailableFor(spec, gs.hr.trainingCenterLevel, gs.SimUtc);
            for (int i = 0; i < avail.Count; i++)
                Assert.AreNotEqual(CertificationCatalog.CabinSafety, avail[i].id, "已持有效证不应再出现在可办列表");

            // ④ 国际运行资质 = 国际航线准入的第二条路径（此前只有「海外引进」渠道一条）
            Assert.AreEqual(0, gs.OverseasCrewCount);
            var crew = new PersonnelData
            {
                id = "sp_op_pilot", fullName = "运行序列机长", department = "OPS", rank = "专员",
                jobTitle = "飞行员", potential = 0.80f, baseSalary = 20f, loyalty = 70f, age = 35,
                isOperational = true
            };
            gs.staffPool.Add(crew);
            Assert.IsFalse(RoutePermitLogic.HasOverseasQualification(gs), "无海外机组且未持证 → 不具备资质");

            CertificationLogic.Grant(crew, CertificationCatalog.Find(CertificationCatalog.IntlOps), gs.SimUtc);
            Assert.AreEqual(1, RoutePermitLogic.IntlOpsCertifiedCrewCount(gs));
            Assert.IsTrue(RoutePermitLogic.HasOverseasQualification(gs), "持证机组应满足国际航线准入");

            // 旧档迁移：certs 为 null 的档案不得到处 NullReference
            var legacy = new PersonnelData { id = "sp_legacy", fullName = "旧档人员", certs = null };
            gs.staffPool.Add(legacy);
            HrManager.EnsurePoolCerts(gs);
            Assert.IsNotNull(legacy.certs);
            Assert.IsFalse(CertificationLogic.Has(legacy, CertificationCatalog.PilotLicense));

            // ⑤ 入职自带基础资质：运行序列凭岗位持 1 级证；进阶证与国际运行资质永不自动发
            //（若自动发 intl_ops，等于白送一条国际航线准入通道）
            var freshPilot = new PersonnelData
            {
                id = "sp_op_fresh", fullName = "新到岗机长", department = "OPS", rank = "专员",
                jobTitle = "飞行员", potential = 0.80f, baseSalary = 20f, loyalty = 70f, age = 34,
                isOperational = true
            };
            var freshCabin = new PersonnelData
            {
                id = "sp_op_cabin", fullName = "新到岗乘务", department = "OPS", rank = "专员",
                jobTitle = "乘务员", potential = 0.62f, baseSalary = 9f, loyalty = 70f, age = 26,
                isOperational = true
            };
            gs.staffPool.Add(freshPilot);
            gs.staffPool.Add(freshCabin);
            Assert.Greater(HrManager.EnsurePoolCerts(gs), 0, "补发应返回实际发证数");
            Assert.IsTrue(CertificationLogic.Has(freshPilot, CertificationCatalog.PilotMedical),
                "运行序列飞行员到岗应自带体检合格证");
            Assert.IsTrue(CertificationLogic.Has(freshCabin, CertificationCatalog.CabinSafety),
                "乘务员到岗应自带乘务安全培训记录");
            Assert.IsFalse(CertificationLogic.Has(freshPilot, CertificationCatalog.IntlOps),
                "国际运行资质必须靠 3 级培训中心考取，不得自动授予");
            Assert.IsFalse(CertificationLogic.Has(freshPilot, CertificationCatalog.PilotLicense),
                "进阶证（2 级门槛）不得随岗自带");
            Assert.IsFalse(CertificationLogic.Has(legacy, CertificationCatalog.PilotMedical),
                "非运行序列/无岗位信息者不自动发证");
            // 幂等性用「签发时间不得被刷新」判定，而非全池计数（后者会被池内其他人的增删干扰）
            long issued = CertificationLogic.Held(freshPilot, CertificationCatalog.PilotMedical).issuedTicks;
            HrManager.EnsurePoolCerts(gs);
            Assert.AreEqual(issued,
                CertificationLogic.Held(freshPilot, CertificationCatalog.PilotMedical).issuedTicks,
                "补发必须幂等：重复调用不得刷新签发时间");

            var mntRecruit = new PersonnelData
            {
                id = "sp_op_mnt", fullName = "运行序列机务", department = "MNT", rank = "专员",
                jobTitle = "机务工程师", potential = 0.70f, baseSalary = 12f, loyalty = 70f, age = 33,
                isOperational = true
            };
            gs.staffPool.Add(mntRecruit);
            Assert.Greater(HrManager.EnsurePoolCerts(gs), 0, "新到岗机务应补发机务执照");
            Assert.IsTrue(CertificationLogic.Has(mntRecruit, CertificationCatalog.MaintLicense),
                "机务工程师到岗应自带机务执照");
            Assert.IsFalse(CertificationLogic.Has(mntRecruit, CertificationCatalog.MaintRelease),
                "放行授权（2 级门槛）不得随岗自带");

            Object.Destroy(gsGo);
            Object.Destroy(mgrGo);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PersonnelPool_HeadAndStaffSameRecord()
        {
            var gsGo = new GameObject("P15GS");
            var gs = gsGo.AddComponent<GameState>();
            gs.cash = 5000f;
            gs.staffPool = new List<PersonnelData>
            {
                new PersonnelData
                {
                    id = "OPS-P15",
                    fullName = "统一档案",
                    department = "OPS",
                    rank = "负责人",
                    jobTitle = "总监",
                    efficiencyBonus = 1.12f,
                    loyalty = 66f,
                    potential = 0.8f,
                    onMarket = true
                }
            };
            var dmGo = new GameObject("P15Dept");
            dmGo.AddComponent<DepartmentManager>();

            Assert.IsTrue(DepartmentManager.Instance.Appoint("OPS", "OPS-P15"));
            var head = DepartmentManager.Instance.GetHead("OPS");
            Assert.IsNotNull(head);
            Assert.AreSame(gs.staffPool[0], head);
            Assert.IsFalse(head.onMarket);
            Assert.AreEqual(1.12f, head.efficiencyBonus);

            DepartmentManager.Instance.Dismiss("OPS");
            Assert.IsTrue(gs.staffPool[0].onMarket);
            Assert.IsNull(DepartmentManager.Instance.GetHead("OPS"));
            Assert.IsTrue(DepartmentManager.Instance.HireFromMarket("OPS-P15"));
            Assert.AreSame(gs.staffPool[0], DepartmentManager.Instance.GetHead("OPS"));

            Object.Destroy(gsGo);
            Object.Destroy(dmGo);
            yield return null;
        }

        // —— 渠道实体化：四个招聘渠道的人物落池进职业介绍所 ——

        [UnityTest]
        public IEnumerator RecruitmentChannels_PoolIntoAgency()
        {
            var gsGo = new GameObject("AgencyGS");
            var gs = gsGo.AddComponent<GameState>();
            gs.cash = 10000f;
            var mgrGo = new GameObject("AgencyHr");
            var mgr = mgrGo.AddComponent<HrManager>();
            PersonnelGenerator.EnsurePool(gs);

            // 猎头 4 周：到期前不落池，到期后按来源标签落池
            Assert.IsTrue(mgr.LaunchRecruitment("飞行员", 4, 1, 1, out _));
            HrManager.TickDay(gs, gs.SimUtc);
            Assert.AreEqual(0, CountSource(gs, HrEconomics.SourceHeadhunt));

            HrManager.TickDay(gs, gs.SimUtc.AddDays(4 * 7 + 1));
            int pooled = CountSource(gs, HrEconomics.SourceHeadhunt);
            Assert.Greater(pooled, 0);

            // 落池者带来源标签，且池量不超过剩余需求
            var head = FirstOfSource(gs, HrEconomics.SourceHeadhunt);
            Assert.IsNotNull(head);
            Assert.AreEqual(HrEconomics.SourceHeadhunt, HrEconomics.NormalizeSource(head.source));
            Assert.IsTrue(head.onMarket);
            Assert.LessOrEqual(pooled, gs.hr.vacancies[0].needed);

            // 来源分层：猎头薪资期望高于人才市场基准（同职级 8 万 → ×1.45）
            Assert.Greater(head.baseSalary, 8f);

            // 单点录用：离开介绍所并计入人数
            int headBefore = gs.TotalHeadcount;
            float liftBefore = TrainingGrowthLogic.DeptStaffLift(gs, head.department);
            Assert.IsTrue(mgr.HireCandidate(head.id, out _));
            Assert.IsFalse(head.onMarket);
            Assert.Greater(gs.TotalHeadcount, headBefore);
            // 运行序列到岗者不参与「在职关键岗平均效率」，部门 KPI 附加不受影响
            Assert.IsTrue(head.isOperational);
            Assert.AreEqual(liftBefore, TrainingGrowthLogic.DeptStaffLift(gs, head.department), 0.0001f);

            // 退回：从人事池移除，不再出现在介绍所
            var other = FirstOfSource(gs, HrEconomics.SourceHeadhunt);
            if (other != null)
            {
                string oid = other.id;
                Assert.IsTrue(mgr.RejectCandidate(oid, out _));
                Assert.IsNull(PersonnelGenerator.Find(gs, oid));
            }

            Object.Destroy(gsGo);
            Object.Destroy(mgrGo);
            yield return null;
        }

        // —— 内部转岗：编制缺口 + 交接期打折 ——

        [UnityTest]
        public IEnumerator InternalTransfer_MovesSeatAndStartsHandover()
        {
            var gsGo = new GameObject("TrGS");
            var gs = gsGo.AddComponent<GameState>();
            gs.cash = 10000f;
            var mgrGo = new GameObject("TrHr");
            var mgr = mgrGo.AddComponent<HrManager>();
            var dmGo = new GameObject("TrDept");
            dmGo.AddComponent<DepartmentManager>();
            PersonnelGenerator.EnsurePool(gs);

            gs.staffPool.Add(new PersonnelData
            {
                id = "TR-1",
                fullName = "转岗测试",
                department = "OPS",
                rank = "专员",
                jobTitle = "地面服务主管",
                onMarket = false,
                baseSalary = 12f,
                efficiencyBonus = 0.5f,
                loyalty = 70f
            });
            var p = PersonnelGenerator.Find(gs, "TR-1");
            Assert.IsNotNull(p);

            // 存在同职级空缺，才有转岗目标
            Assert.IsTrue(HrManager.TryFindTransferTarget(gs, p, out string toDept, out string toJob));
            Assert.IsFalse(string.IsNullOrEmpty(toDept));
            int opsBefore = Filled(gs, "OPS", "地面服务主管");
            int dstBefore = Filled(gs, toDept, toJob);
            float payBefore = p.baseSalary;

            Assert.IsTrue(mgr.TransferStaff("TR-1", toDept, toJob, out _));

            // 编制缺口：原部门该岗 -1，目标部门该岗 +1（补齐由人力自动重新招聘）
            Assert.AreEqual(opsBefore - 1, Filled(gs, "OPS", "地面服务主管"));
            Assert.AreEqual(dstBefore + 1, Filled(gs, toDept, toJob));
            // 薪资上调，作为表格「期望年薪」的增量来源
            Assert.Greater(p.baseSalary, payBefore);

            // 交接期：战力打折
            Assert.Greater(p.handoverUntilTicks, gs.SimUtc.Ticks);
            Assert.Less(p.EfficiencyAt(gs.SimUtc.Ticks), p.efficiencyBonus);
            Assert.AreEqual(p.efficiencyBonus, p.EfficiencyAt(p.handoverUntilTicks + 1), 0.0001f);

            Object.Destroy(gsGo);
            Object.Destroy(mgrGo);
            Object.Destroy(dmGo);
            yield return null;
        }

        // —— 运行岗编制封顶（e4）：飞行员配额由机队规模推导 ——

        [UnityTest]
        public IEnumerator PilotEstablishment_CapsRecruitmentByFleet()
        {
            var gsGo = new GameObject("CapGS");
            var gs = gsGo.AddComponent<GameState>();
            gs.cash = 100000f;
            var mgrGo = new GameObject("CapHr");
            var mgr = mgrGo.AddComponent<HrManager>();

            // 空机队 → 编制为 0 → 不受封顶约束（既有行为保持：needed 原样接受）
            Assert.AreEqual(0, gs.PilotEstablishment);
            Assert.IsTrue(mgr.LaunchRecruitment("飞行员", 6, 1, 0, out _));
            Assert.AreEqual(6, gs.hr.vacancies[0].needed);

            // 2 架窄体 → 编制 = 2 × 8 = 16（HrEconomics.PilotsPerAircraft）
            gs.fleet.Add(MakeAircraft("B-TEST1", "A320"));
            gs.fleet.Add(MakeAircraft("B-TEST2", "A321"));
            int est = gs.PilotEstablishment;
            Assert.AreEqual(2 * (int)HrEconomics.PilotsPerAircraft, est);

            // 在途配额已占 6，余量 = 16 − 0 − 6 = 10；申请 40 应被裁剪到余量
            bool room = HrManager.RecruitRoomLeft(gs, "飞行员", out int e2, out int onDuty, out int inflight, out int left);
            Assert.IsTrue(room, "飞行员应受编制封顶约束");
            Assert.AreEqual(est, e2);
            Assert.AreEqual(6, inflight);
            Assert.AreEqual(est - 6, left);

            Assert.IsTrue(mgr.LaunchRecruitment("飞行员", 40, 1, 0, out string cappedMsg));
            Assert.AreEqual(est, gs.hr.vacancies[0].needed, "needed 应被裁剪到编制上限");

            // 配满后再招应被驳回
            Assert.IsFalse(mgr.LaunchRecruitment("飞行员", 1, 1, 0, out string fullMsg));
            StringAssert.Contains("已按机队规模配满", fullMsg);

            // 扩充机队 → 编制上调 → 又可招
            gs.fleet.Add(MakeAircraft("B-TEST3", "A320"));
            Assert.IsTrue(mgr.LaunchRecruitment("飞行员", 1, 1, 0, out _));

            // 非运行岗（编制席职务名）不受封顶约束，needed 原样接受
            Assert.IsFalse(HrManager.RecruitRoomLeft(gs, "飞行运行主管", out _, out _, out _, out _),
                "编制席「飞行运行主管」含「飞行」关键字，但不应被当作运行岗封顶");

            Object.Destroy(gsGo);
            Object.Destroy(mgrGo);
            yield return null;
        }

        static Aircraft MakeAircraft(string reg, string type) => new Aircraft
        {
            registration = reg,
            type = type,
            isActive = true,
            status = AircraftStatuses.Airworthy,
            economySeats = 180
        };

        // —— 薪酬 rollup（e3）+ 机组战力乘数（e5）+ 海外资质（e6）——

        [UnityTest]
        public IEnumerator OperationalCrew_PayrollQualityAndOverseasGate()
        {
            var gsGo = new GameObject("CrewGS");
            var gs = gsGo.AddComponent<GameState>();
            gs.fleet.Add(MakeAircraft("B-CREW1", "A320"));

            // 基线：无机组 → 未建机组折损（不再是中性 1.00，否则「永不招人」会成为免费最优解）
            Assert.AreEqual(0f, gs.OperationalPayrollMonthlyWan, 0.0001f);
            Assert.AreEqual(HrEconomics.CrewUnstaffedMultiplier, gs.CrewQualityMultiplier, 0.0001f);
            Assert.Less(gs.CrewQualityMultiplier, HrEconomics.CrewQualityMin,
                "未建机组必须严格劣于任何已建机组的下限，否则反向激励漏洞重现");
            Assert.AreEqual(0, gs.OverseasCrewCount);

            // 先备妥航权，才能把「缺海外机组」这道缺口（码 2）从「缺航权」（码 1）里隔离出来
            //（MissingPrerequisite 按 航权 → 海外资质 的顺序短路返回）。
            var stg = StrategyManager.EnsureState(gs);
            stg.rights.Add(new RouteRight { origin = "PEK", destination = "SIN", freedom = 3, status = "approved" });

            // 国际航线发证硬前置：航权已备但无海外引进机组 → 缺口码 2
            Assert.AreEqual(2, RoutePermitLogic.MissingPrerequisite(gs, "PEK", "SIN", true, out string gateNote));
            StringAssert.Contains("海外引进", gateNote);
            Assert.IsFalse(RoutePermitLogic.HasOverseasQualification(gs));
            // 缺航权时优先报码 1（航权在前）
            Assert.AreEqual(1, RoutePermitLogic.MissingPrerequisite(gs, "PEK", "NRT", true, out _));
            // 国内航线不受海外资质约束
            Assert.AreEqual(0, RoutePermitLogic.MissingPrerequisite(gs, "PEK", "SHA", false, out _));

            // 两名校招飞行员（战力低于达标线 → 折损）
            gs.staffPool.Add(MakeCrew("CR-1", "飞行员", HrEconomics.SourceCampus, 6.0f, 0.384f));
            gs.staffPool.Add(MakeCrew("CR-2", "飞行员", HrEconomics.SourceCampus, 6.0f, 0.384f));
            gs.InvalidateCrewCache(); // The fixture mutates the roster directly, outside the hiring API.
            Assert.AreEqual(2, gs.PilotFilled);

            // 薪酬 rollup：年薪合计 12 万 → 月度 1 万（增量口径，不替换 laborCostWan）
            Assert.AreEqual(12f / 12f, gs.OperationalPayrollMonthlyWan, 0.0001f);
            // 低战力 → 乘数 < 1，但仍须严格优于「一个不招」（反向激励不变量）
            float lowMult = gs.CrewQualityMultiplier;
            Assert.Less(lowMult, 1f);
            Assert.Greater(lowMult, HrEconomics.CrewUnstaffedMultiplier,
                "招低战力校招机组必须优于不招任何人，否则校招成了负收益陷阱");

            // 内部转岗者薪酬已在 laborCostWan 聚合口径内，不得二次计入
            gs.staffPool.Add(MakeCrew("CR-T", "飞行员", HrEconomics.SourceInternal, 9.6f, 0.48f));
            Assert.AreEqual(12f / 12f, gs.OperationalPayrollMonthlyWan, 0.0001f);
            Assert.AreEqual(3, gs.PilotFilled, "内转飞行员仍应计入在职数与战力");

            // 编制席「飞行运行主管」不得被当成飞行员（否则虚增在职数 + 战力重复计入）
            gs.staffPool.Add(MakeCrew("CR-SEAT", "飞行运行主管", HrEconomics.SourceMarket, 20f, 1.12f));
            Assert.AreEqual(3, gs.PilotFilled, "编制席不应计入飞行员在职数");

            // 猎头高战力机组应把乘数拉高（渠道属性差真正生效）
            gs.staffPool[0].efficiencyBonus = 0.528f;
            gs.staffPool[1].efficiencyBonus = 0.528f;
            gs.InvalidateCrewCache();
            Assert.Greater(gs.CrewQualityMultiplier, lowMult);

            // 乘数受上下限夹持，极端属性不会把利润打飞
            Assert.GreaterOrEqual(gs.CrewQualityMultiplier, HrEconomics.CrewQualityMin);
            Assert.LessOrEqual(gs.CrewQualityMultiplier, HrEconomics.CrewQualityMax);

            // 海外引进 → 解锁国际航线资质（渠道生态位）
            gs.staffPool.Add(MakeCrew("CR-OS", "飞行员", HrEconomics.SourceOverseas, 10.4f, 0.528f));
            Assert.AreEqual(1, gs.OverseasCrewCount);
            Assert.IsTrue(RoutePermitLogic.HasOverseasQualification(gs));
            Assert.AreEqual(0, RoutePermitLogic.MissingPrerequisite(gs, "PEK", "SIN", true, out _),
                "航权已备 + 有海外引进机组 → 国际航线前置应无缺口");
            // 编制席职务名不得解锁海外资质（它 isOperational=false，且属编制表）
            gs.staffPool.Add(new PersonnelData
            {
                id = "CR-SEAT2", fullName = "自测·编制席", jobTitle = "飞行运行主管",
                source = HrEconomics.SourceOverseas, isOperational = false, onMarket = false
            });
            Assert.AreEqual(1, gs.OverseasCrewCount, "编制席即使来源为海外也不应计入国际运行资质");

            Object.Destroy(gsGo);
            yield return null;
        }

        /// <summary>
        /// 存量人力成本真实接入：不再是与规模脱节的影子指标。
        /// ① 派生口径 = 每机费率 × 在役机队（5 机 → 1200 万/年）→ 月度 100 万，随日固定成本扣现金；
        /// ② 机队扩张自动缩放（6 机 → 1440）；
        /// ③ 调薪放大费率而非锁定总额（扩张后的新飞机也按加薪后费率计提）；
        /// ④ 旧档迁移：遗留锚点折算为费率，绝对值延续、之后随规模联动；
        /// ⑤ 费率归零后净烧钱随之下降 —— 证明它确实进了现金口径。
        /// </summary>
        [UnityTest]
        public IEnumerator OperationalCrew_StockLaborCostIsChargedToCash()
        {
            var gsGo = new GameObject("HRS-LABOR");
            var gs = gsGo.AddComponent<GameState>();
            gs.hr = new HrState();

            // ① 派生量级：5 机 → 1200 万/年 → 100 万/月
            for (int i = 1; i <= 5; i++) gs.fleet.Add(MakeAircraft("B-LAB" + i, "A320"));
            Assert.AreEqual(5, gs.ActiveFleetCount);
            Assert.AreEqual(HrEconomics.LaborCostPerAircraftWan, gs.hr.laborCostPerAircraftWan, 0.0001f);
            Assert.AreEqual(HrEconomics.DefaultAnnualLaborCostWan, gs.LaborCostAnnualWan, 0.0001f);
            Assert.AreEqual(1200f, gs.LaborCostAnnualWan, 0.0001f);
            Assert.AreEqual(100f, gs.LaborCostMonthlyWan, 0.0001f);
            Assert.AreEqual(HrEconomics.EstablishmentLaborCostWan(5), gs.LaborCostAnnualWan, 0.0001f);

            // ② 机队扩张自动缩放：6 机 → 1440（此前固定值不会涨）
            gs.fleet.Add(MakeAircraft("B-LAB6", "A320"));
            Assert.AreEqual(1440f, gs.LaborCostAnnualWan, 0.0001f);
            Assert.AreEqual(HrEconomics.EstablishmentLaborCostWan(3),
                HrEconomics.EffectiveAnnualLaborCostWan(3, HrEconomics.LaborCostPerAircraftWan, 0f), 0.0001f);

            // ③ 调薪 = 放大费率：调薪后扩张的新飞机也按新费率计提
            gs.hr.laborCostPerAircraftWan *= 1.035f;
            float afterRaise = gs.LaborCostAnnualWan;
            Assert.AreEqual(HrEconomics.LaborCostPerAircraftWan * 1.035f * 6f, afterRaise, 0.01f);
            gs.fleet.Add(MakeAircraft("B-LAB7", "A320"));
            Assert.Greater(gs.LaborCostAnnualWan - afterRaise, HrEconomics.LaborCostPerAircraftWan,
                "调薪后新增飞机须按加薪后费率计提，而非回到默认费率");
            gs.hr.laborCostPerAircraftWan = HrEconomics.LaborCostPerAircraftWan;   // 复位

            // ④ 日固定成本含人力：与传 0 相比恰好多 100/30 万/日
            float withLabor = LiveClockLogic.DailyFixedCostWan(0f, 0f, 0f, 0f, 0f, 0f, 100f);
            float without = LiveClockLogic.DailyFixedCostWan(0f, 0f, 0f, 0f, 0f, 0f, 0f);
            Assert.AreEqual(100f / 30f, withLabor - without, 0.0001f);

            // ⑤ 费率下调后净烧钱随之下降（人力进了现金口径，而非只用于展示/比率）
            float burnWith = gs.EffectiveMonthlyBurn;
            gs.hr.laborCostPerAircraftWan = 1f;   // 近似归零（非正费率会回落默认值，故用正的小值）
            Assert.Less(gs.LaborCostAnnualWan, 1200f);
            Assert.Less(gs.EffectiveMonthlyBurn, burnWith,
                "人力成本必须影响净烧钱；否则 HR 的「人力成本占营收 <25%」仍是影子指标");

            // ⑥ 旧档迁移：遗留锚点（rate=0、laborCostWan=1200、5 机）应折算为 240 万/机/年
            var oldGsGo = new GameObject("HRS-LABOR-LEGACY");
            var old = oldGsGo.AddComponent<GameState>();
            old.hr = new HrState
            {
                laborCostPerAircraftWan = 0f, laborCostWan = 1200f, laborRateMigrated = false,
                // 人数与成本同构：旧档同样「字段为 0」才代表缺失，需显式清零才会触发折算
                headcountPerAircraft = 0f, totalHeadcount = 450, headcountMigrated = false
            };
            for (int i = 1; i <= 5; i++) old.fleet.Add(MakeAircraft("B-OLD" + i, "A320"));
            HrManager.EnsureState(old);   // 触发迁移
            Assert.AreEqual(240f, old.hr.laborCostPerAircraftWan, 0.01f, "旧档 1200 万 / 5 机 应折算为 240 万/机");
            Assert.AreEqual(1200f, old.LaborCostAnnualWan, 0.01f, "旧档折算后绝对值应延续");
            // 人数与成本同构：同一份旧档（450 人 / 5 机）应折算为 90 人/机，绝对值同样延续
            Assert.AreEqual(90f, old.hr.headcountPerAircraft, 0.01f, "旧档 450 人 / 5 机 应折算为 90 人/机");
            Assert.AreEqual(450, old.TotalHeadcount, "旧档折算后人数绝对值应延续");
            Object.Destroy(oldGsGo);

            Object.Destroy(gsGo);
            yield return null;
        }

        /// <summary>
        /// 总人数随机队派生（与人力成本同构）。
        ///
        /// 此前 <c>hr.totalHeadcount</c> 是默认 450 的固定值，与机队脱节：20 架机队的工会
        /// 「让步」成本会被按 450 人低报约 4 倍，培训 ROI（按人均预算）也被系统性高估。
        /// 现改为「每机 90 人 × 在役机队 + 招聘增量」，与 <c>LaborCostAnnualWan</c> 同一套形状。
        /// </summary>
        [Test]
        public void TotalHeadcount_ScalesWithFleetAndFeedsUnionAndTraining()
        {
            var gsGo = new GameObject("HRS-HEADCOUNT");
            var gs = gsGo.AddComponent<GameState>();
            HrManager.EnsureState(gs);

            // 无机队 → 0 人（不再是一个凭空存在的 450 人）
            Assert.AreEqual(0, gs.TotalHeadcount, "无机队不应凭空持有员工");

            // 5 机基准 = 450 人（与 DefaultAnnualLaborCostWan 的 1200 万同锚）
            for (int i = 1; i <= 5; i++) gs.fleet.Add(MakeAircraft("B-HC" + i, "A320"));
            Assert.AreEqual(450, gs.TotalHeadcount, "5 机基准应为 450 人（锚点不得漂移）");

            // 20 机 → 1800 人：工会「让步」按人头计费，必须同步放大
            for (int i = 6; i <= 20; i++) gs.fleet.Add(MakeAircraft("B-HC" + i, "A320"));
            Assert.AreEqual(1800, gs.TotalHeadcount);
            Assert.Greater(HrEconomics.UnionCostWan(0, gs.TotalHeadcount), HrEconomics.UnionCostWan(0, 450),
                "人数翻 4 倍后工会谈判成本必须同步放大，否则大航司的劳资决策被系统性低估");

            // 人均培训预算随人数摊薄 → 人数越多 ROI 越低（方向必须单调）
            Assert.Less(HrEconomics.TrainingRoi(500f, gs.TotalHeadcount), HrEconomics.TrainingRoi(500f, 450));

            // 招聘增量叠加在机队基数之上，不是替换
            gs.hr.headcountAdditive = 12;
            Assert.AreEqual(1812, gs.TotalHeadcount, "招聘增量应叠加而非替换机队基数");

            // 退役飞机后基数回落，招聘增量保留
            gs.fleet[0].isActive = false;
            Assert.AreEqual(1722, gs.TotalHeadcount, "机队退役应回落基数，且不吞掉招聘增量");

            Object.Destroy(gsGo);
        }

        /// <summary>
        /// 反向激励不变量：未建机组必须严格劣于**任何**已建机组。
        ///
        /// 曾经的漏洞：filled&lt;=0 返回 1.00（免费满利润），而校招战力 0.384 低于达标线 0.48
        /// 被判折损到 0.952 —— 于是「永不招机组」严格支配「招校招机组」，
        /// 所有渠道都成了净亏损，渠道系统形同虚设（tools/balance --channels 的 Part B 跑出了这个倒挂）。
        ///
        /// 本测试遍历战力全域 [EfficiencyMin, 1.35]，逐点断言乘数都高于未建机组值，
        /// 使该不变量由结构保证而非调参运气。
        /// </summary>
        [Test]
        public void CrewQuality_UnstaffedIsStrictlyWorstThanAnyCrew()
        {
            float unstaffed = HrEconomics.CrewQualityMultiplier(0, 0f);
            Assert.AreEqual(HrEconomics.CrewUnstaffedMultiplier, unstaffed, 0.0001f);
            Assert.Less(unstaffed, HrEconomics.CrewQualityMin,
                "未建机组值必须低于乘数下限，才能严格劣于任何已建机组");

            // 全域扫描：任何在职机组（哪怕战力触底）都优于一个不招
            for (float eff = HrEconomics.EfficiencyMin; eff <= 1.35f; eff += 0.01f)
            {
                float mult = HrEconomics.CrewQualityMultiplier(1, eff);
                Assert.Greater(mult, unstaffed,
                    $"战力 {eff:F2} 的机组乘数 {mult:F3} 未超过未建机组 {unstaffed:F3} —— 反向激励漏洞重现");
                Assert.GreaterOrEqual(mult, HrEconomics.CrewQualityMin);
                Assert.LessOrEqual(mult, HrEconomics.CrewQualityMax);
            }

            // 达标线 = 中性 1.00，高于达标线才产生增益
            Assert.AreEqual(1f, HrEconomics.CrewQualityMultiplier(1, HrEconomics.CrewAdequacyBaseline), 0.0001f);

            // 渠道梯度仍成立：猎头/海外（0.528）> 内转（0.48）> 校招（0.384）
            float headhunt = HrEconomics.CrewQualityMultiplier(6, 0.528f);
            float internalT = HrEconomics.CrewQualityMultiplier(6, 0.480f);
            float campus = HrEconomics.CrewQualityMultiplier(6, 0.384f);
            Assert.Greater(headhunt, internalT, "猎头战力高于达标线，应优于内转");
            Assert.Greater(internalT, campus, "内转达达标线，应优于校招");
            Assert.Greater(campus, unstaffed, "校招虽低于达标线，仍须优于不招人（漏洞修复点）");

            // 交接期打折（内转 14 天 ×0.70 → 0.336）也不得跌破未建机组值
            float handover = HrEconomics.CrewQualityMultiplier(6, 0.480f * PersonnelData.HandoverEfficiencyScale);
            Assert.Greater(handover, unstaffed, "内转交接期的低战力仍须优于不招人");

            // 坏数据防线：NaN 不得退回中性值（否则「有人但数据坏掉」= 白拿满利润）
            Assert.AreEqual(HrEconomics.CrewQualityMin,
                HrEconomics.CrewQualityMultiplier(1, float.NaN), 0.0001f);

            // 两参重载 = 未指定编制（est<=0）→ 覆盖率视作齐备（1），于是退化为纯质量口径：
            // 此时人数不改变乘数。**带 est 的三参重载才让覆盖率生效**（见下个测试）。
            Assert.AreEqual(HrEconomics.CrewQualityMultiplier(1, 0.384f),
                HrEconomics.CrewQualityMultiplier(50, 0.384f), 0.0001f);
        }

        /// <summary>
        /// 编制覆盖率插值（接管局口径修复）：乘数 = 0.85 + (质量乘数 − 0.85) × 覆盖率。
        ///
        /// 修复的是接管局的口径分叉：接管航司的 8 条已运营航线此前完全没有机组入模，
        /// 无条件吃满「未建机组」0.85 折损，而叙事层写着「机组人员短缺 15%」。
        /// 现在覆盖率把「缺编」变成真实代价（按比例回升），且**不产生陷阱区**：
        /// 对人数单调不减 ⇒ 多招永远不亏，补员方向单向变好。
        /// </summary>
        [Test]
        public void CrewQuality_CoverageInterpolatesWithoutTrapZone()
        {
            const float baseEff = HrEconomics.CrewAdequacyBaseline;   // 质量乘数恰为 1.00
            float unstaffed = HrEconomics.CrewUnstaffedMultiplier;

            // est<=0 → 覆盖率视作齐备（该岗位无编制需求）：与两参重载一致
            Assert.AreEqual(1f, HrEconomics.CrewQualityMultiplier(1, 0, baseEff), 0.0001f);

            // 覆盖率线性插值：0→保底、50%→中点、100%→质量乘数
            Assert.AreEqual(unstaffed + (1f - unstaffed) * 0.5f,
                HrEconomics.CrewQualityMultiplier(50, 100, baseEff), 0.0001f);
            Assert.AreEqual(1f, HrEconomics.CrewQualityMultiplier(100, 100, baseEff), 0.0001f);

            // 北辰快线接管实况：10 架窄体 → 80 编制，difficulty 3 → 缺员 15% → 在册 68 人
            Assert.AreEqual(0.9775f, HrEconomics.CrewQualityMultiplier(68, 80, baseEff), 0.0001f);
            Assert.Less(HrEconomics.CrewQualityMultiplier(68, 80, baseEff), 1f,
                "缺编仍应有代价，否则「机组短缺 X%」再度沦为装饰性文案");

            // 单调性 + 无陷阱区：任何 1 人都严格优于 0 人，且不超过满编（战力相同时）
            float prev = unstaffed;
            for (int filled = 1; filled <= 80; filled++)
            {
                float mult = HrEconomics.CrewQualityMultiplier(filled, 80, HrEconomics.EfficiencyMin);
                Assert.Greater(mult, unstaffed,
                    $"覆盖 {filled}/80（战力触底）乘数 {mult:F4} 未超过未建机组 {unstaffed:F4} —— 出现了「招人反而更亏」的陷阱区");
                Assert.GreaterOrEqual(mult, prev, $"覆盖率插值对人数必须单调不减（{filled}/80 处回退）");
                prev = mult;
            }
        }

        /// <summary>
        /// 接管局：继承的机组必须真正入模。
        ///
        /// 此前 TakeoverWorldBuilder.Apply 只写机队 / 航线 / 贷款 / 遗留议题，从不生成人员，
        /// 于是 staffPool 里 0 名运行序列人员 → PilotFilled=0 → 8 条已运营航线吃满 0.85 折损，
        /// 而 profile.crewVacancyPercent 驱动的「机组人员短缺 15%」只是装饰性文案。
        /// </summary>
        [UnityTest]
        public IEnumerator Takeover_InheritedCrewIsSeededAndNotDoubleCounted()
        {
            var gsGo = new GameObject("TakeoverCrewGS");
            var gs = gsGo.AddComponent<GameState>();
            for (int i = 0; i < 10; i++) gs.fleet.Add(MakeAircraft("B-TK" + i, "A320"));

            float revenueBefore = gs.monthlyRevenue;
            // 航线必须有机号才算执飞中的航线：无执飞机的航线按 §12.9 不计营收
            // （幽灵航班已消除），故此处如实挂上一架在册飞机，保持「已运营航线」的前提。
            var route = new RouteData
            {
                origin = "PEK",
                destination = "SHA",
                registration = "B-TK0",
                dailyProfit = 10f
            };
            gs.activeRoutes.Add(route);
            float unstaffedRevenue = gs.monthlyRevenue;
            Assert.AreEqual(HrEconomics.CrewUnstaffedMultiplier, gs.CrewQualityMultiplier, 0.0001f);

            // 接管写入（缺员 15%）
            int seeded = PersonnelGenerator.SeedInheritedCrew(gs, 15);
            Assert.Greater(seeded, 0, "接管必须生成在册机组，否则已运营航线会被当成无人驾驶");
            Assert.AreEqual(68, gs.PilotFilled, "飞行员在册数应为 编制 80 × (1 − 15%) = 68");
            Assert.AreEqual(80, gs.PilotEstablishment);

            // 机组乘数由「保底 0.85」回升为按覆盖率插值，且严格优于未建机组
            Assert.Greater(gs.CrewQualityMultiplier, HrEconomics.CrewUnstaffedMultiplier);
            Assert.AreEqual(0.9775f, gs.CrewQualityMultiplier, 0.001f);
            Assert.Greater(gs.monthlyRevenue, unstaffedRevenue, "有在册机组后航线收入必须高于「未建机组」口径");
            _ = revenueBefore;

            // 薪酬不得双计：继承机组薪酬已含在「存量人力成本」聚合口径内
            Assert.AreEqual(0f, gs.OperationalPayrollMonthlyWan, 0.0001f,
                "继承在册机组的薪酬已在 240 万/机/年 的聚合口径内，计入运行岗增量即双计");

            // 国际准入不得白送：继承机组不是海外引进渠道
            Assert.AreEqual(0, gs.OverseasCrewCount, "继承在册机组不得计入国际运行资质（仍须海外引进或考取证照）");

            // 在册不进介绍所
            for (int i = 0; i < gs.staffPool.Count; i++)
                Assert.IsFalse(gs.staffPool[i].onMarket, "继承机组应直接在职，不应出现在职业介绍所候选人池");

            // 招聘编制封顶：只剩缺员缺口可补，而不是整支机组
            Assert.IsTrue(HrManager.RecruitRoomLeft(gs, "飞行员", out int est, out int onDuty, out _, out int roomLeft));
            Assert.AreEqual(80, est);
            Assert.AreEqual(68, onDuty);
            Assert.AreEqual(12, roomLeft, "应只剩 15% 缺员缺口，而非 80 人全额");

            Object.Destroy(gsGo);
            yield return null;
        }

        static PersonnelData MakeCrew(string id, string jobTitle, string source, float salary, float eff) =>
            new PersonnelData
            {
                id = id,
                fullName = "自测·" + id,
                jobTitle = jobTitle,
                source = source,
                isOperational = true,
                onMarket = false,
                baseSalary = salary,
                efficiencyBonus = eff
            };

        static int CountSource(GameState gs, string tag)
        {
            int n = 0;
            if (gs?.staffPool == null) return 0;
            for (int i = 0; i < gs.staffPool.Count; i++)
            {
                var p = gs.staffPool[i];
                if (p == null || !p.onMarket) continue;
                if (HrEconomics.NormalizeSource(p.source) != tag) continue;
                n++;
            }
            return n;
        }

        static PersonnelData FirstOfSource(GameState gs, string tag)
        {
            if (gs?.staffPool == null) return null;
            for (int i = 0; i < gs.staffPool.Count; i++)
            {
                var p = gs.staffPool[i];
                if (p == null || !p.onMarket) continue;
                if (HrEconomics.NormalizeSource(p.source) == tag) return p;
            }
            return null;
        }

        static int Filled(GameState gs, string dept, string jobTitle)
        {
            var roles = StaffEstablishment.Roles(dept);
            for (int i = 0; i < roles.Length; i++)
                if (roles[i].jobTitle == jobTitle)
                    return StaffEstablishment.CountFilled(dept, roles[i], gs, DepartmentManager.Instance);
            return -1;
        }

        // —— Manager 回写：SAF 文化/局方/调查/预算 ——

        [UnityTest]
        public IEnumerator SafetyManager_WritesState()
        {
            var gsGo = new GameObject("SafGS");
            var gs = gsGo.AddComponent<GameState>();
            gs.cash = 10000f;
            var mgrGo = new GameObject("SafetyManager");
            var mgr = mgrGo.AddComponent<SafetyManager>();

            // 局方列表初始化
            SafetyManager.EnsureState(gs);
            Assert.AreEqual(3, gs.saf.regulators.Count);

            // 安全文化切换回写
            Assert.IsTrue(mgr.SetCultureType(1, out _));
            Assert.AreEqual(1, gs.saf.cultureType);

            // 局方关系改善回写
            int statusBefore = gs.saf.regulators[0].status;
            Assert.IsTrue(mgr.ImproveRegulator("CAAC", out _));
            Assert.Greater(gs.saf.regulators[0].status, statusBefore);

            // 事件调查启动 + 结案回写
            Assert.IsTrue(mgr.LaunchInvestigation(1, 1, out _));
            Assert.AreEqual(1, gs.saf.investigations.Count);
            Assert.AreEqual("调查中", gs.saf.investigations[0].status);
            Assert.IsTrue(mgr.ResolveInvestigation(gs.saf.investigations[0].id, out _));
            Assert.AreEqual("已结案", gs.saf.investigations[0].status);

            // 局方检查应对回写评级与关系
            int caacBefore = gs.saf.regulators[0].status;
            float score0 = gs.saf.ratingScore;
            Assert.IsTrue(mgr.RespondInspection("CAAC", 0, out _));
            Assert.GreaterOrEqual(gs.saf.regulators[0].status, caacBefore);
            Assert.Greater(gs.saf.ratingScore, score0);

            Object.Destroy(gsGo);
            Object.Destroy(mgrGo);
            yield return null;
        }

        // —— 职业介绍所顶栏「来源」读数：小下拉 + 过滤面板不压暗名单（CEO 裁定 2026-10-11 晚）——

        /// <summary>
        /// 两个入口必须分工清楚，且过滤面板不得再挡住名单：
        ///   ① 点顶栏读数「来源：…  ▾」→ 只弹 6 档来源小下拉
        ///     （此前它打开的是整个过滤面板，还附一层压暗名单的灰）；
        ///   ② 点「来源 / 过滤 · 招聘条件 ▾」→ 打开过滤面板，且**不得**存在压暗名单的 Scrim。
        ///
        /// 断言落在节点存在性上：那层灰底就是一个名为 `Scrim` 的按钮 —— 它一旦回来，本用例即红。
        /// 点按钮走的是真 `onClick.Invoke()`（不是反射置标志位），故连线本身也在覆盖内：
        /// 哪天有人把读数的闭包改回 `_filterOpen = true`，这里立刻失败。
        /// </summary>
        [UnityTest]
        public IEnumerator RecruitmentAgency_SourceDropInsteadOfDimmedFilterPanel()
        {
            var gsGo = new GameObject("N112GS");
            var gs = gsGo.AddComponent<GameState>();
            var dmGo = new GameObject("N112Dept");
            dmGo.AddComponent<DepartmentManager>();

            var panel = RecruitmentOfficePanel.Build();
            Assert.IsNotNull(panel, "职业介绍所面板应能构建");

            var content = panel.transform.Find("Card/Content");
            Assert.IsNotNull(content, "应能定位卡片内容层（FilterPanel / SrcDrop 都画在这里）");

            // ② 过滤面板能打开，但不得再压暗 / 吃掉名单点击
            RecruitmentOfficePanel.RequestRecruit("机务工程师");
            panel.Show();
            yield return null;   // 让上一轮的 Destroy 落地，否则会读到待销毁的旧节点
            Assert.IsNotNull(content.Find("FilterPanel"), "过滤面板应能打开");
            Assert.IsNull(content.Find("Scrim"),
                "过滤面板不得再压暗名单 —— 那层 Scrim 既挡视线又吃掉点击，已按裁定移除");

            // ① 读数只弹小下拉：
            //    每次都重新 Find —— 闭包里的 Refresh() 会把整层内容销毁重建，
            //    抓住旧的 Button 引用再 Invoke 会撞 MissingReferenceException。
            var readout = content.Find("SrcReadout");
            Assert.IsNotNull(readout, "顶栏应有「来源」读数");
            // 注：读数文案里的「▾」（声明它可点开）此处不断言 —— 读文案要 TextMeshProUGUI，
            // 而测试程序集按 AirlineCEO.PlayModeTests.asmdef 的口径**拿不到** TMPro
            //（tools/semcheck/gen.js 的 notInTestAsm 明确把它剔出测试块）。该文案由源码保证。
            Assert.IsNotNull(readout.GetComponent<UnityEngine.UI.Button>(),
                "读数必须是个按钮（读数长得像标签却点了会弹东西，正是本次要修的那件事之一）");

            content.Find("SrcReadout").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return null;
            var drop = content.Find("SrcDrop");
            Assert.IsNotNull(drop, "点读数应弹出来源小下拉");
            Assert.IsNull(content.Find("FilterPanel"), "点读数不得打开整个过滤面板");
            Assert.AreEqual(6, drop.childCount, "下拉应为 6 档来源（全部来源/人才市场/校招/猎头挖角/海外引进/内部转岗）");
            for (int i = 0; i < 6; i++)
                Assert.IsNotNull(content.Find("SrcDrop/SrcOpt" + i), "下拉缺第 " + i + " 档");

            // 再点一次收起：瞬时浮层不该留在名单上
            content.Find("SrcReadout").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return null;
            Assert.IsNull(content.Find("SrcDrop"), "再点读数应收起下拉");
            Assert.IsNull(content.Find("SrcScrim"), "收起后连点击落点也不该残留");

            Object.Destroy(panel.gameObject);
            Object.Destroy(gsGo);
            Object.Destroy(dmGo);
            yield return null;
        }

        // —— 职业介绍所名单：滚动条代替翻页 + 去掉重复的「人力职能」入口（CEO 裁定 2026-10-12）——

        /// <summary>
        /// ① 名单改为右侧常驻滚动条（取消「上一页 / 下一页」与页码行）；
        /// ② 底部「人力职能 →」按钮删除 —— 顶栏已有同名标签页，同一入口不给两个；
        /// ③ 表头固定在滚动区**之外**（滚动时列名不跟着跑）。
        ///
        /// 「不再翻页」用**行数**证明：老实现一页固定 8 行，故只要让候选人多于 8 人，
        /// 数据行就必然超过 8 —— 若有人把翻页改回来（或重新加上任何 8 行上限），本用例即红。
        /// </summary>
        [UnityTest]
        public IEnumerator RecruitmentAgency_ListScrollsInsteadOfPaging()
        {
            var gsGo = new GameObject("N112ScrollGS");
            var gs = gsGo.AddComponent<GameState>();
            var dmGo = new GameObject("N112ScrollDept");
            dmGo.AddComponent<DepartmentManager>();

            // 先撒 20 名渠道候选人（校招来源 → 在任何来源视图下都进名单，不依赖职级）
            for (int i = 0; i < 20; i++)
            {
                gs.staffPool.Add(new PersonnelData
                {
                    id = "n112scroll_" + i,
                    fullName = "滚动测试" + i,
                    department = "OPS",
                    rank = "专员",
                    jobTitle = "飞行员",
                    source = HrEconomics.SourceCampus,
                    onMarket = true,
                    isOperational = true,
                    baseSalary = 10f + i,
                    potential = 0.7f,
                    loyalty = 70f,
                    age = 30
                });
            }

            var panel = RecruitmentOfficePanel.Build();
            panel.Show();
            yield return null;

            var content = panel.transform.Find("Card/Content");
            Assert.IsNotNull(content, "应能定位卡片内容层");

            // 顶栏读数 → 小下拉 → 选「全部来源」，把静态 _src 钉死，
            // 免得上一条用例（或测试顺序变化）留下的来源筛选把 20 行筛没了。
            var readout = content.Find("SrcReadout");
            Assert.IsNotNull(readout, "顶栏应有「来源」读数");
            readout.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return null;
            var allOpt = content.Find("SrcDrop/SrcOpt0");
            Assert.IsNotNull(allOpt, "读数的下拉应有「全部来源」档");
            allOpt.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return null;

            // ③ 表头固定在滚动区之外
            var header = content.Find("ListHeader");
            Assert.IsNotNull(header, "应有固定表头");
            Assert.IsNull(content.Find("ListScroll/Viewport/Content/ListHeader"),
                "表头不得画在滚动区里 —— 否则列名会跟着名单一起滚走");

            // ① 滚动条取代翻页
            var scrollGo = content.Find("ListScroll");
            Assert.IsNotNull(scrollGo, "名单应是一个滚动区");
            // 注意：ScrollRect 挂在 Viewport 上（UIFactory.VerticalScrollArea 的接线方式），
            // 不是挂在 ListScroll 这个宿主上 —— 故用 GetComponentInChildren 取，别只 GetComponent。
            var scroll = scrollGo.GetComponentInChildren<UnityEngine.UI.ScrollRect>();
            Assert.IsNotNull(scroll, "滚动区应由 ScrollRect 驱动");
            Assert.IsNotNull(scroll.verticalScrollbar, "右侧应有常驻滚动条（这就是取代翻页的那个控件）");
            Assert.IsNotNull(content.Find("ListScroll/Scrollbar"), "滚动条节点应存在");
            Assert.IsNull(content.Find("Prev"), "「上一页」应已删除");
            Assert.IsNull(content.Find("Next"), "「下一页」应已删除");
            Assert.IsNull(content.Find("Pg"), "页码行应已删除（条数已在顶栏读数旁显示）");
            Assert.IsNull(content.Find("ToHr"), "底部「人力职能 →」应已删除（顶栏已有同名标签页）");

            // 行数 > 8：这是「不再翻页 / 没有每页上限」的判据
            int rowsFound = 0;
            while (content.Find("ListScroll/Viewport/Content/N" + rowsFound) != null) rowsFound++;
            Assert.GreaterOrEqual(rowsFound, 20,
                $"20 名候选人应一次性全部建出（实得 {rowsFound} 行）—— 一页 8 行的旧上限若回来，这里会红");

            Object.Destroy(panel.gameObject);
            Object.Destroy(gsGo);
            Object.Destroy(dmGo);
            yield return null;
        }

        /// <summary>
        /// 列义（CEO 裁定 2026-10-12 第③条）：删「部门」「空缺职位」两列，改为「现状」+「偏好工作」。
        ///
        /// 钉三件事：
        /// ① 表头是「现状」「偏好工作」，且「部门」「空缺职位」在表头里**不再出现**（防回潮）；
        /// ② 待聘候选人 → 现状「待聘」、偏好工作「部门 · 岗位」；
        /// ③ 在职可转岗者 → 现状「在职」、偏好工作带「（可调入）」。
        ///
        /// ②③ 的差别是有用的：本页的下一跳要按现状分流（待聘 → 聘请/录用，在职 → 转岗），
        /// 若两行都写成同一句话，玩家就得回人员页才知道该点哪个按钮。
        /// </summary>
        [UnityTest]
        public IEnumerator RecruitmentAgency_ColumnsAreStatusAndPreferredJob()
        {
            var gsGo = new GameObject("N112ColGS");
            var gs = gsGo.AddComponent<GameState>();
            gs.cash = 10000f;
            var mgrGo = new GameObject("N112ColHr");
            mgrGo.AddComponent<HrManager>();
            var dmGo = new GameObject("N112ColDept");
            dmGo.AddComponent<DepartmentManager>();
            PersonnelGenerator.EnsurePool(gs);

            const string campusName = "列义待聘";
            gs.staffPool.Add(new PersonnelData
            {
                id = "n112col_campus", fullName = campusName, department = "MNT", rank = "专员",
                jobTitle = "机务工程师", source = HrEconomics.SourceCampus, onMarket = true,
                isOperational = true, baseSalary = 12f, potential = 0.7f, loyalty = 70f, age = 32
            });
            const string internalName = "列义在职";
            gs.staffPool.Add(new PersonnelData
            {
                id = "n112col_internal", fullName = internalName, department = "OPS", rank = "专员",
                jobTitle = "地面服务主管", onMarket = false, baseSalary = 12f, loyalty = 70f, age = 32
            });

            var panel = RecruitmentOfficePanel.Build();
            panel.Show();
            yield return null;

            var content = panel.transform.Find("Card/Content");
            Assert.IsNotNull(content, "应能定位卡片内容层");
            var header = content.Find("ListHeader");
            Assert.IsNotNull(header, "应有固定表头");

            // 测试程序集**故意**不引用 Unity.TextMeshPro（asmdef 口径，见 tools/semcheck/gen.js：
            // 那条边界 2026-10-10 真拦下过一个 Unity 才报的 CS0012）。
            // 但断言列的**内容**必须读到 TMP 文本，故按类型全名反射取 text，不引入编译期依赖。
            string LabelText(GameObject go)
            {
                if (go == null) return null;
                var comps = go.GetComponentsInChildren<Component>(true);
                for (int i = 0; i < comps.Length; i++)
                {
                    var c = comps[i];
                    if (c == null) continue;
                    var t = c.GetType();
                    if (t.FullName != "TMPro.TextMeshProUGUI") continue;
                    var prop = t.GetProperty("text");
                    return prop != null ? prop.GetValue(c) as string : null;
                }
                return null;
            }

            // ① 表头改名，且旧名消失
            var headerLabels = header.GetComponentsInChildren<Component>(true);
            var headerTexts = new List<string>();
            for (int i = 0; i < headerLabels.Length; i++)
            {
                string t = LabelText(headerLabels[i].gameObject);
                if (!string.IsNullOrEmpty(t)) headerTexts.Add(t);
            }
            CollectionAssert.Contains(headerTexts, "现状");
            CollectionAssert.Contains(headerTexts, "偏好工作");
            CollectionAssert.DoesNotContain(headerTexts, "部门", "旧「部门」列必须已删除");
            CollectionAssert.DoesNotContain(headerTexts, "空缺职位", "旧「空缺职位」列必须已删除");

            // 行单元格都是滚动内容层的**兄弟**（不是每行一个子容器），故按行号拼节点名找。
            const string rowRoot = "ListScroll/Viewport/Content/";

            int RowIndexFor(string personName)
            {
                for (int i = 0; i < 400; i++)
                {
                    var go = content.Find(rowRoot + "N" + i);
                    if (go == null) return -1;
                    if (LabelText(go.gameObject) == personName) return i;
                }
                return -1;
            }

            string CellText(string node)
            {
                var go = content.Find(rowRoot + node);
                Assert.IsNotNull(go, "找不到单元格 " + node);
                string t = LabelText(go.gameObject);
                Assert.IsNotNull(t, "单元格没有文字 " + node);
                return t;
            }

            // ② 待聘候选人
            int ci = RowIndexFor(campusName);
            Assert.GreaterOrEqual(ci, 0, "校招待聘候选人应在名单里");
            Assert.AreEqual("待聘", CellText("St" + ci), "池内候选人现状应为「待聘」");
            Assert.AreEqual("机务工程部 · 机务工程师", CellText("P" + ci),
                "偏好工作应为「部门 · 岗位」");

            // ③ 在职可转岗者
            int ii = RowIndexFor(internalName);
            Assert.GreaterOrEqual(ii, 0, "在职可转岗者应在「全部来源」视图里（内部转岗并入此视图）");
            Assert.AreEqual("在职", CellText("St" + ii), "在编者现状应为「在职」");
            StringAssert.EndsWith("（可调入）", CellText("P" + ii),
                "在职者的偏好工作写的是可调入目标，须标出这是「可调入」而非现状");

            Object.Destroy(panel.gameObject);
            Object.Destroy(gsGo);
            Object.Destroy(mgrGo);
            Object.Destroy(dmGo);
            yield return null;
        }

        /// <summary>
        /// 同一个数只允许有一个显示位（CEO 裁定 2026-10-12 晚）。
        ///
        /// CEO 问：「两个『全部来源 73 人』是重复了吗？」——是，而且是**字面上的同一个值**：
        /// 同一次 `Refresh()` 把 `rows.Total` 传给 `DrawKpiStrip` 与 `DrawToolbar` 两处，
        /// 拼出的串只差一个分隔符（`全部来源 73 人` vs `全部来源 · 73 人`）。
        /// 这种「只差一个符号」的重复比原样重复更坏：它会让人去找两者的差别，而差别不存在。
        ///
        /// 现在全池读数只在工具栏那一行，KPI 条只剩四张全局卡。故本用例断言：
        /// ① 全页含「全池」的标签**恰好 1 个**（旧实现是 2 个：KPI 首卡 + 工具栏）；
        /// ② 该标签同时含「渠道候选」（两段口径并排在同一行，而不是分居两处）；
        /// ③ 没有任何 KPI 卡出现「全部来源」/「全池」——首卡不得回潮。
        /// </summary>
        [UnityTest]
        public IEnumerator RecruitmentAgency_PoolCountHasSingleReadout()
        {
            var gsGo = new GameObject("N112DupGS");
            var gs = gsGo.AddComponent<GameState>();
            gs.cash = 10000f;
            var mgrGo = new GameObject("N112DupHr");
            mgrGo.AddComponent<HrManager>();
            var dmGo = new GameObject("N112DupDept");
            dmGo.AddComponent<DepartmentManager>();
            PersonnelGenerator.EnsurePool(gs);

            var panel = RecruitmentOfficePanel.Build();
            panel.Show();
            yield return null;

            var content = panel.transform.Find("Card/Content");
            Assert.IsNotNull(content, "应能定位卡片内容层");

            // 同 §20 用例：测试程序集故意不引用 TMPro，故按类型全名反射取 text。
            string LabelText(GameObject go)
            {
                var comps = go.GetComponentsInChildren<Component>(true);
                for (int i = 0; i < comps.Length; i++)
                {
                    var c = comps[i];
                    if (c == null) continue;
                    var t = c.GetType();
                    if (t.FullName != "TMPro.TextMeshProUGUI") continue;
                    var prop = t.GetProperty("text");
                    return prop != null ? prop.GetValue(c) as string : null;
                }
                return null;
            }

            // 名单行是滚动内容，与计数无关；只扫卡片内容层自己的直接子节点（KPI 卡 / 提示行 / 工具栏）。
            int poolHits = 0;
            string poolText = null;
            for (int i = 0; i < content.childCount; i++)
            {
                var child = content.GetChild(i).gameObject;
                if (child.name == "ListScroll" || child.name == "ListHeader") continue;
                string txt = LabelText(child);
                if (txt != null && txt.Contains("全池")) { poolHits++; poolText = txt; }
            }

            Assert.AreEqual(1, poolHits,
                $"全页只应有一处「全池」读数（实得 {poolHits} 处）—— KPI 首卡若回潮即为 2 处");
            Assert.IsNotNull(poolText, "应有「全池」读数");
            StringAssert.Contains("渠道候选", poolText, "全池与渠道候选应并排在同一行读数里");

            // ③ KPI 条应是四张全局卡，且一张都不含「全池」/「全部来源」
            int kpiCards = 0;
            var kpiTexts = new List<string>();
            for (int i = 0; i < 10; i++)
            {
                var card = content.Find("K" + i);
                if (card == null) continue;
                kpiCards++;
                var labels = card.GetComponentsInChildren<Component>(true);
                for (int j = 0; j < labels.Length; j++)
                {
                    string t = LabelText(labels[j].gameObject);
                    if (!string.IsNullOrEmpty(t)) kpiTexts.Add(t);
                }
            }
            Assert.AreEqual(4, kpiCards,
                $"KPI 条应只剩四张全局卡（实得 {kpiCards} 张）——「全池 / 当前来源」首卡若回潮即为 5 张");
            for (int i = 0; i < kpiTexts.Count; i++)
            {
                Assert.IsFalse(kpiTexts[i].Contains("全池"), "KPI 卡不得再显示全池：" + kpiTexts[i]);
                Assert.IsFalse(kpiTexts[i].Contains("全部来源"), "KPI 卡不得再显示当前来源人数：" + kpiTexts[i]);
            }

            Object.Destroy(panel.gameObject);
            Object.Destroy(gsGo);
            Object.Destroy(mgrGo);
            Object.Destroy(dmGo);
            yield return null;
        }
    }
}
