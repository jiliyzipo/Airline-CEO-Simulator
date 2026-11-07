using UnityEngine;

namespace AirlineCEO.UI
{
    /// <summary>
    /// 「页面上一次的意图」类静态量的**进局复位**（★2026-11-07，为「禁用域重载」准备）。
    ///
    /// 背景：这些 <c>public static</c> 字段是页面之间传参用的（「点财务确认页 → 记住选了哪种融资方式」），
    /// 由各页面的 <c>*Context</c> 静态类持有。域重载开着时它们每局自动归零，所以一直没人管。
    /// 一旦关掉域重载，上一局最后一次选择就会原样留在新局里：比如上一局停在
    /// 「购机会议 → 3 架 B787」，新局直接开购机面板可能就带着这个数量与机型，点一下就成交。
    ///
    /// 集中在一个文件里复位，比散落到十几个页面各写一段好审计：**凡是新局必须回到初始值的
    /// 页面意图，都列在下面**；没列的（纯筛选器 / 排序 / 展开态）属于视觉残留，不影响业务。
    ///
    /// <c>SubsystemRegistration</c> 在每次进入播放模式时都会跑（域重载开着也一样），
    /// 所以这段常驻没有副作用 —— 域重载开着时它只是把刚被重置过的值再赋一次默认值。
    /// </summary>
    internal static class UiIntentReset
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForNewSession()
        {
            // —— 人事：任命 / 招聘 / 工会 ——
            AppointmentContext.DeptCode = "OPS";
            HrContext.RecruitPosition = "飞行员";
            HrContext.RecruitNeeded = 6;
            HrContext.RecruitUrgency = 1;
            HrContext.SelectedDept = "OPS";
            HrContext.SelectedStaffId = "";
            HrContext.SelectedStaffIsExec = false;
            HrSubpagePanel.PendingUnionMailId = null;

            // —— 财务：融资方式与待处理邮件 ——
            FinanceContext.FinancingKind = -1;
            FinanceContext.PendingFinanceMailId = null;

            // —— 机队：选中的飞机 / 航线 / 城市，以及购机会议参数 ——
            FleetNetContext.SelectedRegistration = null;
            FleetNetContext.SelectedRouteId = null;
            FleetNetContext.SelectedCityIata = null;
            FleetNetContext.OpenEconomyTab = false;
            FleetNetContext.OpenRightsTab = false;
            FleetNetContext.PurchaseTypeId = null;
            FleetNetContext.PurchaseCount = 1;
            FleetUI.PendingTab = -1;

            // —— 市场：航线 / 节日 / 舱位 / 竞对 ——
            MktContext.SelectedRouteId = null;
            MktContext.SelectedFestival = "春节";
            MktContext.SelectedCabin = AirlineCEO.Data.CabinClasses.Economy;
            MktContext.SelectedCompetitorId = null;

            // —— 运行 / 安全 / 维修：确认页与调查参数 ——
            OpsSubpageContext.SelectedRouteId = null;
            OpsSubpageContext.SelectedRingNote = null;
            OpsSubpageContext.SelectedRegistration = null;
            OpsSubpageContext.SelectedFlightIndex = -1;
            OpsIrropsModalUI.PendingMailId = null;
            SafetyContext.InvestigationSeverity = 1;
            SafetyContext.InvestigationDepth = 1;
            SafetyContext.PendingInspectMailId = null;
            MaintenanceContext.ConfirmOrderNo = "";

            // —— 战略：谈判对象与分成 ——
            StrategyContext.NegotiatePartner = "新加坡航空";
            StrategyContext.NegotiateSplit = 0.5f;

            // —— 地图 / 导航 ——
            MapCityPopup.PendingOriginIata = null;

            // —— 测试专用开关：测试进程里若异常退出没复位，后续正常游玩会被静默锁死 ——
            PrefabNavIntegration.SuppressAutoAttachForTests = false;
            Setup.SetupFlowController.BypassForTests = false;
        }
    }
}
