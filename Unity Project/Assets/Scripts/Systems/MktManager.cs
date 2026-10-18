using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// MKT 市场销售部系统（Phase 2 Week 7 · 批次 A + Week 8 · 批次 B）：
    /// 定价 / 动态定价 / 辅助收入 / FFP / 产品工坊 / 旺季策略（A）+ 渠道预算 / 大客户合同 / 品牌危机（B）。
    /// 全部回写 GameState.mkt。
    /// </summary>
    public class MktManager : MonoBehaviour
    {
        public static MktManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public static MktState EnsureState(GameState gs)
        {
            if (gs == null) return null;
            if (gs.mkt == null) gs.mkt = new MktState();
            if (gs.mkt.fareOverrides == null) gs.mkt.fareOverrides = new List<RouteFareOverride>();
            if (gs.mkt.dynamicPricing == null) gs.mkt.dynamicPricing = new DynamicPricingSettings();
            if (gs.mkt.ancillary == null) gs.mkt.ancillary = new AncillarySettings();
            if (gs.mkt.ffp == null) gs.mkt.ffp = new FfpSettings();
            if (gs.mkt.products == null) gs.mkt.products = new List<ProductDesign>();
            if (gs.mkt.seasonPackages == null) gs.mkt.seasonPackages = new List<PeakSeasonPackage>();
            if (gs.mkt.channelBudget == null) gs.mkt.channelBudget = new ChannelBudget();
            if (gs.mkt.keyAccounts == null) gs.mkt.keyAccounts = new List<KeyAccount>();
            if (gs.mkt.brand == null) gs.mkt.brand = new BrandCrisis();
            if (gs.mkt.brand.feed == null) gs.mkt.brand.feed = new List<SentimentEntry>();
            if (gs.mkt.brand.repairMeasures == null) gs.mkt.brand.repairMeasures = new List<RepairMeasure>();
            return gs.mkt;
        }

        #region 定价

        /// <summary>获取某航线某舱位票价乘数（无覆盖返回 1）。</summary>
        public static float FareMultFor(MktState mkt, string routeId, string cabin)
        {
            if (mkt?.fareOverrides == null) return 1f;
            for (int i = 0; i < mkt.fareOverrides.Count; i++)
            {
                if (mkt.fareOverrides[i].routeId == routeId)
                    return CabinMult(mkt.fareOverrides[i], cabin);
            }
            return 1f;
        }

        static float CabinMult(RouteFareOverride o, string cabin)
        {
            switch (cabin)
            {
                case CabinClasses.First: return o.firstMult;
                case CabinClasses.Business: return o.businessMult;
                case CabinClasses.PremiumEconomy: return o.premiumEconomyMult;
                default: return o.economyMult;
            }
        }

        /// <summary>设置航线舱位票价乘数并回写 GameState。</summary>
        public bool SetRouteFare(string routeId, string cabin, float mult, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            var mkt = EnsureState(gs);
            mult = Mathf.Clamp(mult, 0.5f, 2.0f);

            RouteFareOverride o = null;
            for (int i = 0; i < mkt.fareOverrides.Count; i++)
                if (mkt.fareOverrides[i].routeId == routeId) { o = mkt.fareOverrides[i]; break; }
            if (o == null)
            {
                o = new RouteFareOverride { routeId = routeId };
                mkt.fareOverrides.Add(o);
            }
            switch (cabin)
            {
                case CabinClasses.First: o.firstMult = mult; break;
                case CabinClasses.Business: o.businessMult = mult; break;
                case CabinClasses.PremiumEconomy: o.premiumEconomyMult = mult; break;
                default: o.economyMult = mult; break;
            }
            o.updatedBy = "CEO";
            message = $"已更新 {routeId} · {cabin} 票价 ×{mult:F2}。";
            gs.AddDecisionLog("市场：" + message);
            gs.NotifyChanged();
            return true;
        }

        /// <summary>一键应用 AI 建议票价（最优乘数）。</summary>
        public bool ApplyAiFare(string routeId, string passengerType, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            var route = gs.activeRoutes != null ? gs.activeRoutes.Find(r => r != null && r.routeId == routeId) : null;
            if (route == null) { message = "航线不存在"; return false; }

            int seats = EconomyBalanceParams.DefaultSeatsFor(route.aircraftType);
            float baseFare = EstimateBaseFareUsd(route);
            float baseCostWan = route.dailyProfit > 0f
                ? Mathf.Max(0.1f, route.dailyProfit * 0.6f)
                : 1f;
            float mult = MktEconomics.OptimalPriceMult(seats, EconomyBalanceParams.DefaultLoadFactor, baseFare, baseCostWan, passengerType);
            return SetRouteFare(routeId, CabinClasses.Economy, mult, out message);
        }

        /// <summary>估算基准经济舱票价（美元）：航程 × 每公里票价。</summary>
        public static float EstimateBaseFareUsd(RouteData route)
        {
            if (route == null) return 800f;
            float perKm = 0.55f; // 美元/公里（经济舱均价近似）
            return Mathf.Clamp(route.distanceKm * perKm, 300f, 4000f);
        }

        #endregion

        #region 动态定价

        public bool SaveDynamicPricing(DynamicPricingSettings s, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            var mkt = EnsureState(gs);
            mkt.dynamicPricing = s ?? new DynamicPricingSettings();
            message = s != null && s.enabled ? "AI 动态调价已启用。"
                : "AI 动态调价已暂停。";
            gs.AddDecisionLog("市场：" + message);
            gs.NotifyChanged();
            return true;
        }

        #endregion

        #region 辅助收入

        public bool SaveAncillary(AncillarySettings s, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            var mkt = EnsureState(gs);
            mkt.ancillary = s ?? new AncillarySettings();
            float perPax = MktEconomics.AuxRevenuePerPaxUsd(mkt.ancillary);
            message = $"辅助收入已更新（人均约 ${perPax:F1}）。";
            gs.AddDecisionLog("市场：" + message);
            gs.NotifyChanged();
            return true;
        }

        #endregion

        #region FFP

        public bool SaveFfp(FfpSettings s, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            var mkt = EnsureState(gs);
            mkt.ffp = s ?? new FfpSettings();
            float liability = MktEconomics.FfpLiabilityWan(mkt.ffp);
            message = $"FFP 规则已保存（里程负债约 {liability:F0} 万）。";
            gs.AddDecisionLog("市场：" + message);
            gs.NotifyChanged();
            return true;
        }

        #endregion

        #region 产品工坊

        public bool SubmitProduct(ProductDesign d, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            if (d == null || string.IsNullOrEmpty(d.name)) { message = "产品名称不能为空"; return false; }
            var mkt = EnsureState(gs);
            if (string.IsNullOrEmpty(d.id))
                d.id = "prod_" + mkt.products.Count.ToString("D2");
            d.launched = true;
            int idx = mkt.products.FindIndex(p => p.id == d.id);
            if (idx >= 0) mkt.products[idx] = d;
            else mkt.products.Add(d);
            message = $"产品「{d.name}」已上线（预估转化 {d.expectedConversion * 100:F0}%）。";
            gs.AddDecisionLog("市场：" + message);
            gs.NotifyChanged();
            return true;
        }

        #endregion

        #region 旺季策略

        public bool SaveSeasonPackage(PeakSeasonPackage pkg, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            if (pkg == null || string.IsNullOrEmpty(pkg.festival)) { message = "请选择节日"; return false; }
            var mkt = EnsureState(gs);
            pkg.applied = true;
            int idx = mkt.seasonPackages.FindIndex(p => p.festival == pkg.festival);
            if (idx >= 0) mkt.seasonPackages[idx] = pkg;
            else mkt.seasonPackages.Add(pkg);
            message = $"「{pkg.festival}」旺季策略包已保存。";
            gs.AddDecisionLog("市场：" + message);
            gs.NotifyChanged();
            return true;
        }

        public static PeakSeasonPackage GetSeasonPackage(MktState mkt, string festival)
        {
            if (mkt?.seasonPackages == null) return null;
            return mkt.seasonPackages.Find(p => p.festival == festival);
        }

        #endregion

        #region 渠道预算（批次 B）

        public bool SaveChannelBudget(ChannelBudget s, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            var mkt = EnsureState(gs);
            mkt.channelBudget = s ?? new ChannelBudget();
            message = $"渠道预算已保存（直销占比 {mkt.channelBudget.directRatio * 100:F0}%，预算 {mkt.channelBudget.annualBudgetWan:F0} 万）。";
            gs.AddDecisionLog("市场：" + message);
            gs.NotifyChanged();
            return true;
        }

        #endregion

        #region 大客户合同（批次 B）

        public bool SignKeyAccount(KeyAccount a, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            if (a == null || string.IsNullOrEmpty(a.name)) { message = "客户名称不能为空"; return false; }
            var mkt = EnsureState(gs);
            if (string.IsNullOrEmpty(a.id))
                a.id = "ka_" + mkt.keyAccounts.Count.ToString("D2");
            a.signed = true;
            int idx = mkt.keyAccounts.FindIndex(k => k.id == a.id);
            if (idx >= 0) mkt.keyAccounts[idx] = a;
            else mkt.keyAccounts.Add(a);
            float val = MktCompetitionEconomics.KeyAccountValueWan(a.annualSpendWan, a.frontDiscount, a.tier);
            message = $"大客户「{a.name}」已签约（年度价值约 {val:F0} 万）。";
            gs.AddDecisionLog("市场：" + message);
            gs.NotifyChanged();
            return true;
        }

        #endregion

        #region 品牌危机（批次 B）

        public bool UpdateBrandCrisis(BrandCrisis b, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            var mkt = EnsureState(gs);
            if (b != null)
            {
                b.feed = mkt.brand.feed ?? new List<SentimentEntry>();
                b.repairMeasures = mkt.brand.repairMeasures ?? new List<RepairMeasure>();
                mkt.brand = b;
            }
            message = mkt.brand.active
                ? $"品牌危机状态已更新（当前等级 {LevelName(mkt.brand.crisisLevel)}）。"
                : "品牌危机已解除，信任指数恢复跟踪中。";
            gs.AddDecisionLog("市场：" + message);
            gs.NotifyChanged();
            return true;
        }

        public bool SubmitRepairMeasure(RepairMeasure m, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            if (m == null || string.IsNullOrEmpty(m.name)) { message = "措施名称不能为空"; return false; }
            var mkt = EnsureState(gs);
            mkt.brand.repairMeasures.Add(m);
            m.done = true;
            message = $"整改措施「{m.name}」已提交（责任：{m.dept}）。";
            gs.AddDecisionLog("市场：" + message);
            gs.NotifyChanged();
            return true;
        }

        public static string LevelName(int level) => level switch
        {
            2 => "重大",
            1 => "重要",
            _ => "一般"
        };

        #endregion
    }
}
