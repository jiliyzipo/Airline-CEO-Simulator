using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 根据董事组合 + ChemistryMatcher 生成 3～4 项年度目标（含保底）。
    /// 对应 GDD 5.3 / 5.4：净利润、客座率、安全、航线扩张。
    /// </summary>
    public static class TargetGenerator
    {
        /// <summary>生成本财年目标清单（3～4 项）。</summary>
        public static List<BoardTarget> Generate(List<string> boardIds, int fiscalYear)
        {
            var chem = ChemistryMatcher.Match(boardIds);
            var list = new List<BoardTarget>();
            var used = new HashSet<BoardTargetType>();

            // 化学反应 → 对应 CEO 目标编号（T-01~T-23）
            for (int i = 0; i < chem.Count && list.Count < 4; i++)
            {
                var def = TargetCatalog.GetByChemistry(chem[i]);
                var type = def != null ? def.type : BoardTargetType.NetProfit;
                if (used.Contains(type)) continue;
                used.Add(type);
                list.Add(MakeTarget(type, isGuarantee: false, source: chem[i], fiscalYear, list.Count, def));
            }

            // 保底：净利润 + 客座率
            EnsureType(list, used, BoardTargetType.NetProfit, fiscalYear);
            EnsureType(list, used, BoardTargetType.LoadFactor, fiscalYear);

            // 固定凑满 3 项（去掉随机第 4 项，避免 3/4 抖动与结算不一致）
            if (list.Count < 3)
                EnsureType(list, used, BoardTargetType.Safety, fiscalYear);
            if (list.Count < 3)
                EnsureType(list, used, BoardTargetType.RouteExpansion, fiscalYear);

            // 上限 3（MVP 稳定验收）
            if (list.Count > 3)
                list.RemoveRange(3, list.Count - 3);

            return list;
        }

        private static void EnsureType(List<BoardTarget> list, HashSet<BoardTargetType> used,
            BoardTargetType type, int fiscalYear)
        {
            if (used.Contains(type) || list.Count >= 4) return;
            used.Add(type);
            bool guarantee = type == BoardTargetType.NetProfit || type == BoardTargetType.LoadFactor;
            list.Add(MakeTarget(type, guarantee, guarantee ? "保底" : "补足", fiscalYear, list.Count, null));
        }

        private static BoardTarget MakeTarget(BoardTargetType type, bool isGuarantee, string source,
            int fiscalYear, int index, TargetDef def)
        {
            float threshold;
            string unit;
            string title;
            switch (type)
            {
                case BoardTargetType.NetProfit:
                    threshold = BoardEconomics.GuaranteeNetProfit;
                    unit = "万";
                    title = $"净利润 ≥ {threshold:F0} 万";
                    break;
                case BoardTargetType.LoadFactor:
                    threshold = BoardEconomics.GuaranteeLoadFactor;
                    unit = "%";
                    title = $"客座率 ≥ {threshold:F0}%";
                    break;
                case BoardTargetType.Safety:
                    threshold = BoardEconomics.DefaultSafety;
                    unit = "";
                    title = $"安全指数 ≥ {threshold:F0}";
                    break;
                case BoardTargetType.RouteExpansion:
                default:
                    threshold = BoardEconomics.DefaultRouteCount;
                    unit = "条";
                    title = $"开通航线 ≥ {threshold:F0} 条";
                    break;
            }

            return new BoardTarget
            {
                id = $"Y{fiscalYear}-T{index + 1}-{type}",
                type = type,
                title = title,
                threshold = threshold,
                currentValue = 0f,
                unit = unit,
                isGuarantee = isGuarantee,
                completed = false,
                sourceNote = source,
                targetId = def != null ? def.id : "",
                difficulty = def != null ? def.difficulty : ""
            };
        }

        /// <summary>根据 GameState 刷新各目标当前值与完成标记。</summary>
        public static void RefreshProgress(List<BoardTarget> targets, GameState gs)
        {
            if (targets == null || gs == null) return;
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                switch (t.type)
                {
                    case BoardTargetType.NetProfit:
                        t.currentValue = gs.yearNetProfit;
                        break;
                    case BoardTargetType.LoadFactor:
                        t.currentValue = gs.loadFactorPercent;
                        break;
                    case BoardTargetType.Safety:
                        t.currentValue = gs.safetyIndex;
                        break;
                    case BoardTargetType.RouteExpansion:
                        t.currentValue = gs.activeRoutes != null ? gs.activeRoutes.Count : 0;
                        break;
                }

                t.completed = t.currentValue + 0.001f >= t.threshold;
            }
        }
    }
}
