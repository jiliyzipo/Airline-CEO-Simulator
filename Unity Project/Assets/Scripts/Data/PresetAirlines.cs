using System.Collections.Generic;

namespace AirlineCEO.Data
{
    /// <summary>可接管航司：四家具名预设 + 按城市经济目录为每座枢纽生成一家。</summary>
    public static class PresetAirlines
    {
        public const string Startup = "preset_startup";
        public const string Regional = "preset_regional";
        public const string Legacy = "preset_legacy";
        public const string Distressed = "preset_distressed";
        public const string HubPrefix = "hub_";

        public static readonly string[] AllIds = { Startup, Regional, Legacy, Distressed };

        static List<AirlineProfile> _selectable;

        public static AirlineProfile Template(string id)
        {
            return Resolve(id);
        }

        public static AirlineProfile Resolve(string id)
        {
            if (string.IsNullOrEmpty(id)) id = Regional;
            if (id.StartsWith(HubPrefix))
            {
                string hub = id.Substring(HubPrefix.Length);
                return FromHub(hub);
            }
            switch (id)
            {
                case Regional:
                    return StampGeo(Base("preset_regional", "北辰快线", "North Star Express", "NS", "NSX", "PEK", 3,
                        10, 8, 1200f, 0.65f, 12, "区域网络已成型，扩张与亏损航线并存。"));
                case Legacy:
                    return StampGeo(Base("preset_legacy", "东海联合", "East Sea United", "EU", "ESU", "PVG", 4,
                        30, 25, 2000f, 0.78f, 18, "老牌全服务航司，机队老化、成本高企。"));
                case Distressed:
                    return StampGeo(Base("preset_distressed", "天枢航空", "Sky Pivot Air", "SP", "SPV", "CTU", 5,
                        50, 40, 500f, 0.92f, 22, "现金告急，网络过大，董事会要求立刻止血。"));
                default:
                    return StampGeo(Base("preset_startup", "青禾航空", "Green Harvest Air", "GH", "GHA", "CAN", 2,
                        3, 2, 500f, 0.35f, 6, "刚完成首轮开航，规模小、包袱轻。"));
            }
        }

        public static List<AirlineProfile> AllSelectable()
        {
            if (_selectable != null) return _selectable;
            var list = new List<AirlineProfile>();
            var usedIata = new HashSet<string> { "GH", "NS", "EU", "SP" };
            var all = CityEconomyCatalog.All;
            for (int i = 0; i < all.Length; i++)
            {
                var e = all[i];
                if (e == null || string.IsNullOrEmpty(e.iata)) continue;
                list.Add(FromHub(e.iata, usedIata));
            }
            _selectable = list;
            return list;
        }

        public static AirlineProfile FromHub(string hub)
        {
            var used = new HashSet<string> { "GH", "NS", "EU", "SP" };
            return FromHub(hub, used);
        }

        static AirlineProfile FromHub(string hub, HashSet<string> usedIata)
        {
            if (string.IsNullOrEmpty(hub)) hub = "PEK";
            hub = hub.ToUpperInvariant();
            if (hub == "CAN") return StampGeo(Resolve(Startup));
            if (hub == "PEK") return StampGeo(Resolve(Regional));
            if (hub == "PVG") return StampGeo(Resolve(Legacy));
            if (hub == "CTU") return StampGeo(Resolve(Distressed));

            var e = CityEconomyCatalog.Find(hub);
            int stars = StarsFor(e);
            string seedId = stars <= 2 ? Startup : stars == 3 ? Regional : stars == 4 ? Legacy : Distressed;
            var t = Resolve(seedId);
            t.id = HubPrefix + hub;
            t.hubAirport = hub;
            StampGeo(t);
            string city = t.cityZh ?? hub;
            t.nameCN = stars <= 2 ? city + "通航" : stars == 3 ? city + "快线" : stars == 4 ? city + "航空" : city + "联合";
            t.nameEN = (t.cityZh ?? hub) + " Air";
            t.iataCode = NextCode(hub, usedIata, 2);
            t.icaoCode = NextCode(hub + "X", usedIata, 3);
            t.blurb = stars <= 2
                ? "枢纽规模较小，包袱轻，适合稳健起步。"
                : stars == 3
                    ? "区域网络已成型，扩张与亏损航线并存。"
                    : stars == 4
                        ? "全服务骨干航司，成本与机队老化需正视。"
                        : "现金紧张、网络过大，董事会要求立刻止血。";
            return t;
        }

        static AirlineProfile StampGeo(AirlineProfile p)
        {
            if (p == null) return null;
            var e = CityEconomyCatalog.Find(p.hubAirport);
            if (e != null)
            {
                p.continentZh = e.continentZh;
                p.countryZh = e.countryZh;
                p.cityZh = e.cityZh;
            }
            return p;
        }

        static int StarsFor(CityEconomyEntry e)
        {
            float pax = e != null ? e.paxMillion : 20f;
            int h = 0;
            string iata = e != null ? e.iata : "";
            for (int i = 0; i < iata.Length; i++) h = h * 31 + iata[i];
            if (h < 0) h = -h;
            if (pax >= 70f) return h % 5 == 0 ? 5 : 4;
            if (pax >= 40f) return 3;
            return 2;
        }

        static string NextCode(string seed, HashSet<string> used, int len)
        {
            string raw = (seed ?? "XX").ToUpperInvariant();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < raw.Length && sb.Length < len; i++)
            {
                char c = raw[i];
                if (c >= 'A' && c <= 'Z') sb.Append(c);
            }
            while (sb.Length < len) sb.Append('X');
            string code = sb.ToString();
            if (used == null)
            {
                return code;
            }
            if (used.Add(code)) return code;
            for (int n = 0; n < 26 * 26; n++)
            {
                char a = (char)('A' + (n % 26));
                char b = (char)('A' + ((n / 26) % 26));
                string alt = len == 2 ? "" + a + b : "" + a + b + 'A';
                if (used.Add(alt)) return alt;
            }
            used.Add(code);
            return code;
        }

        static AirlineProfile Base(string id, string cn, string en, string iata, string icao, string hub,
            int stars, int fleet, int routes, float cash, float debt, int avgAge, string blurb)
        {
            return new AirlineProfile
            {
                id = id,
                nameCN = cn,
                nameEN = en,
                iataCode = iata,
                icaoCode = icao,
                hubAirport = hub,
                difficulty = stars,
                fleetCount = fleet,
                routeCount = routes,
                cash = cash,
                debtRatio = debt,
                avgFleetAge = avgAge,
                blurb = blurb,
                departmentHeads = new List<DepartmentHeadSnapshot>
                {
                    new DepartmentHeadSnapshot { dept = "运营", name = "林启航", title = "运营总监" },
                    new DepartmentHeadSnapshot { dept = "机务", name = "周衡", title = "机务工程总监" },
                    new DepartmentHeadSnapshot { dept = "市场", name = "陈昭", title = "市场销售总监" },
                    new DepartmentHeadSnapshot { dept = "人力", name = "吴宁", title = "人力资源总监" },
                    new DepartmentHeadSnapshot { dept = "财务", name = "赵衡书", title = "财务总监" },
                    new DepartmentHeadSnapshot { dept = "安全", name = "孙安", title = "安全质量总监" }
                }
            };
        }
    }
}
