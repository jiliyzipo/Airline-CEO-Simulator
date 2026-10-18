using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.Logic
{
    /// <summary>离线底表 + 宏观/开航演化，供航线看板与开线 RPM。</summary>
    public static class CityEconomyLogic
    {
        public static CityEconomyView Resolve(GameState gs, string iata)
        {
            string code = (iata ?? "").Trim().ToUpperInvariant();
            var raw = CityEconomyCatalog.Find(code);
            var ap = AirportCatalog.FindByCode(code);
            var v = new CityEconomyView
            {
                iata = code,
                continentZh = raw != null ? raw.continentZh : "其他",
                countryZh = raw != null ? raw.countryZh : "未分类",
                cityZh = raw != null ? raw.cityZh : code,
                airportZh = !string.IsNullOrEmpty(ap.nameZh) ? ap.nameZh : code,
                gdpUsdBn = raw != null ? raw.gdpUsdBn : 40f,
                popMillion = raw != null ? raw.popMillion : 2f,
                paxMillion = raw != null ? raw.paxMillion : 8f,
                cargoWanTon = raw != null ? raw.cargoWanTon : 6f,
                odIndex = raw != null ? raw.odIndex : 50f
            };

            float drift = DriftOf(gs, code);
            v.demandDrift = drift;
            float macro = 1f + (gs != null ? gs.gdpGrowthRate : 3f) * 0.012f;
            if (gs != null && gs.economicPhase == "recession") macro *= 0.92f;
            else if (gs != null && gs.economicPhase == "boom") macro *= 1.06f;
            v.liveIndex = Mathf.Clamp(v.odIndex * macro + drift, 20f, 120f);
            v.businessIndex = 82f + v.odIndex * 0.22f + Mathf.Log10(v.gdpUsdBn + 1f) * 6f;
            v.tourismIndex = 80f + v.odIndex * 0.28f + v.paxMillion * 0.12f;
            v.isHub = gs != null && string.Equals(gs.hubIata, code, StringComparison.OrdinalIgnoreCase);
            v.served = v.isHub || IsServed(gs, code);
            return v;
        }

        public static float OdDemandMultiplier(GameState gs, string origin, string dest)
        {
            var a = Resolve(gs, origin);
            var b = Resolve(gs, dest);
            float gravity = Mathf.Sqrt(a.liveIndex * b.liveIndex) / 70f;
            return Mathf.Clamp(gravity, 0.55f, 2.4f);
        }

        public static List<string> VisibleCodes(GameState gs, bool showAll)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (gs != null && !string.IsNullOrEmpty(gs.hubIata))
                set.Add(gs.hubIata.ToUpperInvariant());
            if (gs?.activeRoutes != null)
            {
                for (int i = 0; i < gs.activeRoutes.Count; i++)
                {
                    var r = gs.activeRoutes[i];
                    if (r == null) continue;
                    if (!string.IsNullOrEmpty(r.origin)) set.Add(r.origin.ToUpperInvariant());
                    if (!string.IsNullOrEmpty(r.destination)) set.Add(r.destination.ToUpperInvariant());
                    if (!string.IsNullOrEmpty(r.stopover)) set.Add(r.stopover.ToUpperInvariant());
                }
            }

            if (showAll)
            {
                for (int i = 0; i < CityEconomyCatalog.All.Length; i++)
                    set.Add(CityEconomyCatalog.All[i].iata);
            }

            var list = new List<string>(set);
            list.Sort(StringComparer.Ordinal);
            if (gs != null && !string.IsNullOrEmpty(gs.hubIata))
            {
                string hub = gs.hubIata.ToUpperInvariant();
                int hi = list.FindIndex(c => c == hub);
                if (hi > 0)
                {
                    list.RemoveAt(hi);
                    list.Insert(0, hub);
                }
            }
            return list;
        }

        public static string TickQuarter(GameState gs)
        {
            if (gs == null) return "";
            if (gs.cityEconomyDeltas == null)
                gs.cityEconomyDeltas = new List<CityEconomyDelta>();

            var served = VisibleCodes(gs, false);
            for (int i = 0; i < served.Count; i++)
            {
                string code = served[i];
                float add = 0.35f;
                if (gs.economicPhase == "boom") add = 0.7f;
                else if (gs.economicPhase == "recession") add = -0.25f;
                AddDrift(gs, code, add);
            }
            return "";
        }

        public static void ReloadOfflineTable(GameState gs)
        {
            if (gs == null) return;
            gs.cityEconomyDeltas = new List<CityEconomyDelta>();
            gs.cityEconomyRevision = CityEconomyCatalog.Revision;
        }

        public const float BuyBaseCostWan = 500f;

        public static bool IsCompanyBase(GameState gs, string iata)
        {
            if (gs == null || string.IsNullOrEmpty(iata)) return false;
            string code = iata.Trim().ToUpperInvariant();
            if (string.Equals(gs.hubIata, code, StringComparison.OrdinalIgnoreCase)) return true;
            string city = CityEconomyCatalog.Find(code)?.cityZh;
            string hubCity = CityEconomyCatalog.Find(gs.hubIata)?.cityZh;
            if (!string.IsNullOrEmpty(city) && city == hubCity) return true;
            if (gs.companyBases != null)
            {
                for (int i = 0; i < gs.companyBases.Count; i++)
                {
                    if (string.Equals(gs.companyBases[i], code, StringComparison.OrdinalIgnoreCase))
                        return true;
                    var bCity = CityEconomyCatalog.Find(gs.companyBases[i])?.cityZh;
                    if (!string.IsNullOrEmpty(city) && city == bCity) return true;
                }
            }
            return false;
        }

        public static bool TryBuyBase(GameState gs, string iata, out string msg)
        {
            msg = "";
            if (gs == null) { msg = "游戏未就绪。"; return false; }
            string code = (iata ?? "").Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(code)) { msg = "未选择城市。"; return false; }
            if (IsCompanyBase(gs, code)) { msg = "该城市已是基地。"; return false; }
            if (gs.cash < BuyBaseCostWan)
            {
                msg = $"现金不足：购买基地需要 {BuyBaseCostWan:F0} 万美元。";
                return false;
            }
            gs.cash -= BuyBaseCostWan;
            if (gs.companyBases == null) gs.companyBases = new List<string>();
            gs.companyBases.Add(code);
            gs.AddDecisionLog($"购买运营基地 {code}（−{BuyBaseCostWan:F0} 万）。");
            gs.NotifyChanged();
            msg = $"已在 {code} 设立运营基地。";
            return true;
        }

        static bool IsServed(GameState gs, string code)
        {
            if (gs?.activeRoutes == null) return false;
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r == null) continue;
                if (string.Equals(r.origin, code, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(r.destination, code, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(r.stopover, code, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        static float DriftOf(GameState gs, string code)
        {
            if (gs?.cityEconomyDeltas == null) return 0f;
            for (int i = 0; i < gs.cityEconomyDeltas.Count; i++)
            {
                var d = gs.cityEconomyDeltas[i];
                if (d != null && string.Equals(d.iata, code, StringComparison.OrdinalIgnoreCase))
                    return d.demandDrift;
            }
            return 0f;
        }

        static void AddDrift(GameState gs, string code, float add)
        {
            for (int i = 0; i < gs.cityEconomyDeltas.Count; i++)
            {
                var d = gs.cityEconomyDeltas[i];
                if (d != null && string.Equals(d.iata, code, StringComparison.OrdinalIgnoreCase))
                {
                    d.demandDrift = Mathf.Clamp(d.demandDrift + add, -15f, 18f);
                    return;
                }
            }
            gs.cityEconomyDeltas.Add(new CityEconomyDelta { iata = code, demandDrift = add });
        }
    }
}
