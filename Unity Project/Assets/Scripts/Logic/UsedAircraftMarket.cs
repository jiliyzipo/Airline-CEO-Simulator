using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    /// <summary>二手飞机季度货架：机龄 5–15、折扣 30–60%、高龄陷阱（维修更高）。</summary>
    public static class UsedAircraftMarket
    {
        public static void Ensure(GameState gs, bool refreshQuarter = false)
        {
            if (gs == null) return;
            if (gs.usedMarket == null) gs.usedMarket = new List<UsedListing>();
            bool stale = gs.usedMarket.Count == 0
                || gs.usedMarketYear != gs.year
                || gs.usedMarketQuarter != gs.quarter;
            if (refreshQuarter || stale)
                Roll(gs);
        }

        public static void Roll(GameState gs)
        {
            gs.usedMarket = new List<UsedListing>();
            gs.usedMarketYear = gs.year;
            gs.usedMarketQuarter = gs.quarter;
            var types = DataBase.Instance != null ? DataBase.Instance.AircraftTypes : null;
            if (types == null || types.Count == 0) return;

            int seed = gs.year * 17 + gs.quarter * 31 + 908;
            var rng = new System.Random(seed);
            int n = 8 + rng.Next(0, 5);
            for (int i = 0; i < n; i++)
            {
                var cat = types[rng.Next(types.Count)];
                if (cat == null) continue;
                int age = 5 + rng.Next(0, 11);
                float disc = 0.30f + (float)rng.NextDouble() * 0.30f;
                bool trap = age >= 12;
                gs.usedMarket.Add(new UsedListing
                {
                    id = $"USED-{gs.year}Q{gs.quarter}-{i + 1}",
                    typeId = cat.typeId,
                    age = age,
                    discount = disc,
                    askPriceWan = cat.priceWan * (1f - disc),
                    remainingLife = System.Math.Max(4, 25 - age),
                    trap = trap
                });
            }
        }

        public static UsedListing Find(GameState gs, string id)
        {
            if (gs?.usedMarket == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < gs.usedMarket.Count; i++)
            {
                if (gs.usedMarket[i] != null && gs.usedMarket[i].id == id)
                    return gs.usedMarket[i];
            }
            return null;
        }
    }
}
