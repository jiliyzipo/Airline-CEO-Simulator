using System;
using System.Collections.Generic;

namespace AirlineCEO.Data
{
    /// <summary>制造商 → 系列 → 型号。displayName 为官方通称，icao 为 ICAO 机型代号。</summary>
    public static class AircraftCatalog
    {
        public static readonly string[] Oems =
        {
            "AIRBUS", "BOEING", "BOMBARDIER", "EMBRAER", "ATR", "DH", "COMAC", "UAC"
        };

        public static AircraftCatalogEntry[] All { get; } =
        {
            // —— AIRBUS —— 载客为典型两舱；航程为厂商公布典型航程（km）
            E("A220-100", "Airbus A220-100", "AIRBUS", "A220 Family", "BCS1", 120, 6390f, 8100f),
            E("A220-300", "Airbus A220-300", "AIRBUS", "A220 Family", "BCS3", 150, 6297f, 9100f),
            E("A318", "Airbus A318-100", "AIRBUS", "A320 Family", "A318", 107, 5750f, 7200f),
            E("A319", "Airbus A319-100", "AIRBUS", "A320 Family", "A319", 124, 6945f, 8900f),
            E("A320", "Airbus A320-200", "AIRBUS", "A320 Family", "A320", 150, 6150f, 9800f),
            E("A321-100", "Airbus A321-100", "AIRBUS", "A320 Family", "A321", 186, 4350f, 10800f),
            E("A321", "Airbus A321-200", "AIRBUS", "A320 Family", "A321", 185, 5950f, 11500f),
            E("A319neo", "Airbus A319neo", "AIRBUS", "A320neo Family", "A19N", 140, 6850f, 10100f),
            E("A320neo", "Airbus A320neo", "AIRBUS", "A320neo Family", "A20N", 165, 6300f, 11000f),
            E("A321neo", "Airbus A321neo", "AIRBUS", "A320neo Family", "A21N", 206, 7400f, 12900f),
            E("A321LR", "Airbus A321neo LR", "AIRBUS", "A320neo Family", "A21N", 180, 7400f, 13500f),
            E("A321XLR", "Airbus A321XLR", "AIRBUS", "A320neo Family", "A21N", 180, 8700f, 14200f),
            E("A300-600R", "Airbus A300-600R", "AIRBUS", "A300 Family", "A306", 266, 7500f, 8500f),
            E("A300-600F", "Airbus A300-600F", "AIRBUS", "A300 Family", "A306", 0, 4850f, 7200f, true, 54f),
            E("A310-300", "Airbus A310-300", "AIRBUS", "A310 Family", "A310", 220, 9600f, 7800f),
            E("A330-200", "Airbus A330-200", "AIRBUS", "A330 Family", "A332", 246, 13450f, 20000f),
            E("A330-300", "Airbus A330-300", "AIRBUS", "A330 Family", "A333", 300, 11750f, 21500f),
            E("A330-200F", "Airbus A330-200F", "AIRBUS", "A330 Family", "A332", 0, 7400f, 18000f, true, 65f),
            E("A330-800", "Airbus A330-800neo", "AIRBUS", "A330neo Family", "A338", 257, 15094f, 22800f),
            E("A330-900", "Airbus A330-900neo", "AIRBUS", "A330neo Family", "A339", 287, 13334f, 24000f),
            E("A340-200", "Airbus A340-200", "AIRBUS", "A340 Family", "A342", 239, 14800f, 22000f),
            E("A340-300", "Airbus A340-300", "AIRBUS", "A340 Family", "A343", 277, 13500f, 23800f),
            E("A340-500", "Airbus A340-500", "AIRBUS", "A340 Family", "A345", 293, 16670f, 26000f),
            E("A340-600", "Airbus A340-600", "AIRBUS", "A340 Family", "A346", 326, 14630f, 27500f),
            E("A350-900", "Airbus A350-900", "AIRBUS", "A350 Family", "A359", 315, 15000f, 31700f),
            E("A350-900ULR", "Airbus A350-900ULR", "AIRBUS", "A350 Family", "A359", 173, 17964f, 33200f),
            E("A350-1000", "Airbus A350-1000", "AIRBUS", "A350 Family", "A35K", 369, 16100f, 36600f),
            E("A350-1000ULR", "Airbus A350-1000ULR", "AIRBUS", "A350 Family", "A35K", 220, 18000f, 38500f),
            E("A350F", "Airbus A350F", "AIRBUS", "A350 Family", "A35F", 0, 8700f, 34000f, true, 109f),
            E("A380-800", "Airbus A380-800", "AIRBUS", "A380 Family", "A388", 525, 14800f, 44500f),

            // —— BOEING —— typeId 保留开局用 B737-800 / B787-9 / B777F
            E("B717-200", "Boeing 717-200", "BOEING", "717 Family", "B712", 106, 3815f, 4200f),
            E("B737-300", "Boeing 737-300", "BOEING", "737 Classic Family", "B733", 128, 4170f, 3500f),
            E("B737-400", "Boeing 737-400", "BOEING", "737 Classic Family", "B734", 146, 3850f, 3800f),
            E("B737-500", "Boeing 737-500", "BOEING", "737 Classic Family", "B735", 110, 4444f, 3300f),
            E("B737-600", "Boeing 737-600", "BOEING", "737 NG Family", "B736", 108, 5648f, 7200f),
            E("B737-700", "Boeing 737-700", "BOEING", "737 NG Family", "B737", 126, 6370f, 8900f),
            E("B737-800", "Boeing 737-800", "BOEING", "737 NG Family", "B738", 162, 5436f, 9600f),
            E("B737-900", "Boeing 737-900", "BOEING", "737 NG Family", "B739", 177, 5084f, 9900f),
            E("B737-900ER", "Boeing 737-900ER", "BOEING", "737 NG Family", "B739", 180, 5925f, 10200f),
            E("B737-7", "Boeing 737 MAX 7", "BOEING", "737 MAX Family", "B37M", 138, 7130f, 11400f),
            E("B737-8", "Boeing 737 MAX 8", "BOEING", "737 MAX Family", "B38M", 178, 6570f, 12100f),
            E("B737-8-200", "Boeing 737 MAX 8-200", "BOEING", "737 MAX Family", "B38M", 197, 5090f, 12400f),
            E("B737-9", "Boeing 737 MAX 9", "BOEING", "737 MAX Family", "B39M", 193, 6570f, 13200f),
            E("B737-10", "Boeing 737 MAX 10", "BOEING", "737 MAX Family", "B3XM", 204, 6110f, 13800f),
            E("B747-200", "Boeing 747-200B", "BOEING", "747 Family", "B742", 366, 12700f, 12000f),
            E("B747-200F", "Boeing 747-200F", "BOEING", "747 Family", "B742", 0, 6700f, 11000f, true, 110f),
            E("B747-400", "Boeing 747-400", "BOEING", "747 Family", "B744", 416, 13450f, 26500f),
            E("B747-400F", "Boeing 747-400F", "BOEING", "747 Family", "B744", 0, 8230f, 25000f, true, 113f),
            E("B747-8", "Boeing 747-8 Intercontinental", "BOEING", "747 Family", "B748", 410, 14815f, 37900f),
            E("B747-8F", "Boeing 747-8F", "BOEING", "747 Family", "B748", 0, 7630f, 35700f, true, 134f),
            E("B757-200", "Boeing 757-200", "BOEING", "757 Family", "B752", 200, 7275f, 7300f),
            E("B757-200PF", "Boeing 757-200PF", "BOEING", "757 Family", "B752", 0, 5834f, 6800f, true, 27f),
            E("B757-300", "Boeing 757-300", "BOEING", "757 Family", "B753", 243, 6295f, 8000f),
            E("B767-200ER", "Boeing 767-200ER", "BOEING", "767 Family", "B762", 181, 12200f, 16000f),
            E("B767-300ER", "Boeing 767-300ER", "BOEING", "767 Family", "B763", 218, 11070f, 18500f),
            E("B767-400ER", "Boeing 767-400ER", "BOEING", "767 Family", "B764", 245, 10415f, 20000f),
            E("B767-300F", "Boeing 767-300F", "BOEING", "767 Family", "B763", 0, 6025f, 18500f, true, 54f),
            E("B777-200", "Boeing 777-200", "BOEING", "777 Family", "B772", 305, 9700f, 24000f),
            E("B777-200ER", "Boeing 777-200ER", "BOEING", "777 Family", "B772", 301, 13080f, 26100f),
            E("B777-200LR", "Boeing 777-200LR", "BOEING", "777 Family", "B77L", 317, 15843f, 29100f),
            E("B777-300", "Boeing 777-300", "BOEING", "777 Family", "B773", 368, 11135f, 28400f),
            E("B777-300ER", "Boeing 777-300ER", "BOEING", "777 Family", "B77W", 365, 13650f, 34000f),
            E("B777F", "Boeing 777F", "BOEING", "777 Family", "B77L", 0, 9204f, 28000f, true, 102f),
            E("B777-8", "Boeing 777-8", "BOEING", "777X Family", "B778", 384, 16170f, 37100f),
            E("B777-9", "Boeing 777-9", "BOEING", "777X Family", "B779", 426, 13500f, 42500f),
            E("B777-8F", "Boeing 777-8F", "BOEING", "777X Family", "B778", 0, 8100f, 36000f, true, 118f),
            E("B787-8", "Boeing 787-8 Dreamliner", "BOEING", "787 Family", "B788", 242, 13620f, 22400f),
            E("B787-9", "Boeing 787-9 Dreamliner", "BOEING", "787 Family", "B789", 296, 14140f, 25000f),
            E("B787-10", "Boeing 787-10 Dreamliner", "BOEING", "787 Family", "B78X", 330, 11910f, 27600f),
            E("MD-82", "McDonnell Douglas MD-82", "BOEING", "MD-80 Family", "MD82", 155, 3797f, 2800f),
            E("MD-83", "McDonnell Douglas MD-83", "BOEING", "MD-80 Family", "MD83", 155, 4635f, 3000f),
            E("MD-88", "McDonnell Douglas MD-88", "BOEING", "MD-80 Family", "MD88", 142, 3800f, 2900f),
            E("MD-90-30", "McDonnell Douglas MD-90-30", "BOEING", "MD-90 Family", "MD90", 153, 3860f, 4100f),
            E("MD-11", "McDonnell Douglas MD-11", "BOEING", "MD-11 Family", "MD11", 298, 12670f, 14500f),
            E("MD-11F", "McDonnell Douglas MD-11F", "BOEING", "MD-11 Family", "MD11", 0, 7320f, 13200f, true, 91f),

            // —— BOMBARDIER（CRJ；Dash 8 归 DH）——
            E("CRJ100", "Bombardier CRJ100", "BOMBARDIER", "CRJ Family", "CRJ1", 50, 2650f, 2200f),
            E("CRJ200", "Bombardier CRJ200", "BOMBARDIER", "CRJ Family", "CRJ2", 50, 3150f, 2400f),
            E("CRJ550", "Bombardier CRJ550", "BOMBARDIER", "CRJ Family", "CRJ7", 50, 1850f, 3600f),
            E("CRJ700", "Bombardier CRJ700", "BOMBARDIER", "CRJ Family", "CRJ7", 70, 2553f, 3500f),
            E("CRJ900", "Bombardier CRJ900", "BOMBARDIER", "CRJ Family", "CRJ9", 76, 2876f, 4200f),
            E("CRJ1000", "Bombardier CRJ1000", "BOMBARDIER", "CRJ Family", "CRJX", 100, 2843f, 4900f),

            // —— EMBRAER ——
            E("EMB120", "Embraer EMB 120 Brasilia", "EMBRAER", "EMB 120 Family", "E120", 30, 1750f, 900f),
            E("ERJ135", "Embraer ERJ 135", "EMBRAER", "ERJ Family", "E135", 37, 3241f, 1700f),
            E("ERJ140", "Embraer ERJ 140", "EMBRAER", "ERJ Family", "E135", 44, 3056f, 1900f),
            E("ERJ145", "Embraer ERJ 145", "EMBRAER", "ERJ Family", "E145", 50, 2870f, 2100f),
            E("ERJ145XR", "Embraer ERJ 145XR", "EMBRAER", "ERJ Family", "E45X", 50, 3706f, 2400f),
            E("E170", "Embraer 170", "EMBRAER", "E-Jet Family", "E170", 72, 3982f, 4100f),
            E("E175", "Embraer 175", "EMBRAER", "E-Jet Family", "E75S", 76, 4074f, 4700f),
            E("E190", "Embraer 190", "EMBRAER", "E-Jet Family", "E190", 100, 4537f, 5800f),
            E("E195", "Embraer 195", "EMBRAER", "E-Jet Family", "E195", 116, 4260f, 6200f),
            E("E175-E2", "Embraer E175-E2", "EMBRAER", "E-Jet E2 Family", "E275", 80, 3759f, 6400f),
            E("E190-E2", "Embraer E190-E2", "EMBRAER", "E-Jet E2 Family", "E290", 97, 5300f, 6800f),
            E("E195-E2", "Embraer E195-E2", "EMBRAER", "E-Jet E2 Family", "E295", 120, 4815f, 7200f),

            // —— ATR ——
            E("ATR42-300", "ATR 42-300", "ATR", "ATR 42 Family", "AT43", 46, 1550f, 1600f),
            E("ATR42-500", "ATR 42-500", "ATR", "ATR 42 Family", "AT45", 48, 1555f, 1950f),
            E("ATR42-600", "ATR 42-600", "ATR", "ATR 42 Family", "AT46", 48, 1326f, 2100f),
            E("ATR72-200", "ATR 72-200", "ATR", "ATR 72 Family", "AT72", 66, 1648f, 2000f),
            E("ATR72-500", "ATR 72-500", "ATR", "ATR 72 Family", "AT75", 68, 1648f, 2400f),
            E("ATR72-600", "ATR 72-600", "ATR", "ATR 72 Family", "AT76", 70, 1528f, 2600f),

            // —— De Havilland ——
            E("DHC6-300", "De Havilland Canada DHC-6-300 Twin Otter", "DH", "Twin Otter Family", "DHC6", 19, 1434f, 550f),
            E("DHC6-400", "De Havilland Canada DHC-6-400 Twin Otter", "DH", "Twin Otter Family", "DHC6", 19, 1480f, 650f),
            E("DHC8-100", "De Havilland Canada Dash 8-100", "DH", "Dash 8 Family", "DH8A", 37, 1520f, 1800f),
            E("DHC8-200", "De Havilland Canada Dash 8-200", "DH", "Dash 8 Family", "DH8B", 37, 1713f, 2000f),
            E("DHC8-300", "De Havilland Canada Dash 8-300", "DH", "Dash 8 Family", "DH8C", 50, 1566f, 2500f),
            E("DHC8-400", "De Havilland Canada Dash 8-400", "DH", "Dash 8 Family", "DH8D", 78, 2040f, 3300f),

            // —— COMAC ——
            E("ARJ21-700", "COMAC ARJ21-700", "COMAC", "ARJ21 Family", "AJ27", 90, 3700f, 3800f),
            E("ARJ21-900", "COMAC ARJ21-900", "COMAC", "ARJ21 Family", "AJ27", 105, 3300f, 4300f),
            E("C919", "COMAC C919", "COMAC", "C919 Family", "C919", 158, 4075f, 9900f),
            E("C919ER", "COMAC C919ER", "COMAC", "C919 Family", "C919", 158, 5555f, 10800f),

            // —— UAC ——
            E("SSJ100", "Sukhoi Superjet 100-95B", "UAC", "Superjet Family", "SU95", 87, 3048f, 4500f),
            E("SSJ100-95LR", "Sukhoi Superjet 100-95LR", "UAC", "Superjet Family", "SU95", 87, 4578f, 4900f),
            E("MC-21-200", "Yakovlev MC-21-200", "UAC", "MC-21 Family", "MC21", 165, 6400f, 9200f),
            E("MC-21-300", "Yakovlev MC-21-300", "UAC", "MC-21 Family", "MC21", 180, 6000f, 10500f),
            E("MC-21-310", "Yakovlev MC-21-310", "UAC", "MC-21 Family", "MC21", 180, 5100f, 9800f),
            E("Tu-204-100", "Tupolev Tu-204-100", "UAC", "Tu-204 Family", "T204", 210, 4300f, 4800f),
            E("Tu-204-300", "Tupolev Tu-204-300", "UAC", "Tu-204 Family", "T204", 164, 5800f, 5200f),
            E("Tu-214", "Tupolev Tu-214", "UAC", "Tu-204 Family", "T204", 210, 4340f, 5400f),
            E("Il-96-300", "Ilyushin Il-96-300", "UAC", "Il-96 Family", "IL96", 262, 9000f, 12000f),
            E("Il-96-400", "Ilyushin Il-96-400", "UAC", "Il-96 Family", "IL96", 315, 10000f, 13500f),
        };

        static AircraftCatalogEntry E(string typeId, string display, string oem, string family, string icao,
            int seats, float rangeKm, float priceWan, bool freighter = false, float cargo = 0f)
        {
            return new AircraftCatalogEntry
            {
                typeId = typeId,
                displayName = display,
                oem = oem,
                family = family,
                icao = icao,
                maxPassengers = seats,
                maxRangeKm = rangeKm,
                priceWan = priceWan,
                isFreighter = freighter,
                cargoTons = cargo
            };
        }

        public static string Title(AircraftCatalogEntry e)
        {
            if (e == null) return "";
            string name = string.IsNullOrEmpty(e.displayName) ? e.typeId : e.displayName;
            if (string.IsNullOrEmpty(e.icao) || e.icao == name) return name;
            return name + "  ·  " + e.icao;
        }

        public static AircraftCatalogEntry Find(string typeId)
        {
            if (string.IsNullOrEmpty(typeId)) return null;
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].typeId == typeId) return All[i];
            }
            return null;
        }

        /// <summary>
        /// 机型航程真值（km）。**这是航程判据的唯一出处**：引擎里曾另有一张按机型名字做子串匹配的
        /// 硬编码表（只认 787/777/A350/A330/737/A321/A320），表外机型一律按 8,000 km 放行 ——
        /// 于是 767、A220、MAX、747 这些机型会被误判成「能飞」，而表内机型又拿不到目录里的精确值。
        /// 目录缺失返回 0，调用方须按「航程无法核实」处理（不得当成能飞）。
        /// </summary>
        public static float RangeKmOf(string typeId)
        {
            var e = Find(typeId);
            return e != null ? e.maxRangeKm : 0f;
        }

        public static string OemOf(string typeId)
        {
            var e = Find(typeId);
            return e != null ? e.oem : InferOem(typeId);
        }

        public static string FamilyOf(string typeId)
        {
            var e = Find(typeId);
            return e != null ? e.family : "其他";
        }

        public static List<string> FamiliesOf(string oem)
        {
            var list = new List<string>();
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].oem != oem) continue;
                if (!list.Contains(All[i].family)) list.Add(All[i].family);
            }
            return list;
        }

        public static List<AircraftCatalogEntry> ModelsOf(string oem, string family)
        {
            var list = new List<AircraftCatalogEntry>();
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].oem == oem && All[i].family == family)
                    list.Add(All[i]);
            }
            return list;
        }

        // ================= 机组值勤（FDP）与增援机组属性（R9 / R12） =================

        /// <summary>
        /// 双通道（宽体）ICAO 机型代号。用 ICAO 代号判定而非座位数 ——
        /// A321neo（206 座）是单通道，B767-200ER（181 座）是双通道，按座位数会误判。
        /// </summary>
        public static readonly HashSet<string> WidebodyIcao = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "A306", "A310", "A332", "A333", "A338", "A339", "A342", "A343", "A345", "A346",
            "A359", "A35K", "A35F", "A388",
            "B742", "B744", "B748", "B762", "B763", "B764", "B772", "B77L", "B773", "B77W", "B778", "B779",
            "B788", "B789", "B78X",
            "MD11", "IL96",
        };

        /// <summary>是否双通道（宽体）。仅宽体可配增援机组（窄体无休息舱）。</summary>
        public static bool IsWidebody(string typeId)
        {
            var e = Find(typeId);
            if (e != null && !string.IsNullOrEmpty(e.icao)) return WidebodyIcao.Contains(e.icao);
            return LooksWidebody(typeId);
        }

        static bool LooksWidebody(string typeId)
        {
            if (string.IsNullOrEmpty(typeId)) return false;
            string t = typeId.ToUpperInvariant();
            return t.Contains("787") || t.Contains("777") || t.Contains("767") || t.Contains("747")
                || t.Contains("A300") || t.Contains("A310") || t.Contains("A330") || t.Contains("A340")
                || t.Contains("A350") || t.Contains("A380") || t.Contains("MD11") || t.Contains("IL96");
        }

        /// <summary>
        /// 休息舱等级（EASA ORO.FTL.205(e)(iii)）：1 平躺铺位 / 2 可后仰座椅 / 3 帘隔座椅。
        /// </summary>
        public static int RestClassOf(string typeId)
        {
            var e = Find(typeId);
            string icao = (e != null ? e.icao : "").ToUpperInvariant();
            switch (icao)
            {
                // Class 1：独立平躺铺位（现代宽体机组休息舱）
                case "A359": case "A35K": case "A35F": case "A388":
                case "B788": case "B789": case "B78X":
                case "B772": case "B77L": case "B773": case "B77W": case "B778": case "B779":
                case "B748": case "A338": case "A339":
                    return 1;
                // Class 2：客舱可后仰座椅
                case "A332": case "A333": case "A342": case "A343": case "A345": case "A346":
                case "B762": case "B763": case "B764": case "B744": case "A306": case "A310":
                    return 2;
            }
            return LooksWidebody(typeId) ? 2 : 3;
        }

        /// <summary>休息舱等级名称。</summary>
        public static string RestClassLabel(int restClass) => restClass switch
        {
            1 => "Class 1 平躺铺位",
            2 => "Class 2 可后仰座椅",
            _ => "Class 3 帘隔座椅"
        };

        /// <summary>
        /// 增援机组的值勤**绝对上限**（分钟）—— 注意不是「基础值 + 加成」。
        /// 依据 EASA CS FTL.1.205(c)(2)，与 FAA Part 117 Table C 同量级。
        /// </summary>
        public static int AugmentedLimit(int restClass, int pilots)
        {
            int rc = restClass < 1 ? 1 : restClass > 3 ? 3 : restClass;
            bool four = pilots >= 4;
            switch (rc)
            {
                case 1: return (four ? 17 : 16) * 60;
                case 2: return (four ? 16 : 15) * 60;
                default: return (four ? 15 : 14) * 60;
            }
        }

        /// <summary>起降无菌驾驶舱，不计入可休息空中时间。</summary>
        public const int SterileCockpitMinutes = 45;

        /// <summary>
        /// 增援最低空中休息（分钟）。EASA CS FTL.1.205 / AMC：Class 1 自 1h30、
        /// Class 2 自 2h00、Class 3 自 3h00；计划值勤越长越高，Class 3 上限 3h50。
        /// </summary>
        public static int MinInflightRestMinutes(int restClass, int plannedFdpMin)
        {
            int rc = restClass < 1 ? 1 : restClass > 3 ? 3 : restClass;
            int fdpH = plannedFdpMin <= 0 ? 0 : (plannedFdpMin + 59) / 60;
            switch (rc)
            {
                case 1:
                    if (fdpH <= 16) return 90;
                    return 120;
                case 2:
                    if (fdpH <= 14) return 120;
                    if (fdpH <= 15) return 150;
                    return 180;
                default:
                    if (fdpH <= 13) return 180;
                    if (fdpH <= 14) return 210;
                    return 230;
            }
        }

        /// <summary>本段轮挡里实际可休息的分钟数。</summary>
        public static int AvailableInflightRestMinutes(int blockMin)
            => Math.Max(0, blockMin - SterileCockpitMinutes);

        /// <summary>最长航段是否够给增援机组完成最低空中休息。</summary>
        public static bool InflightRestSatisfied(int restClass, int plannedFdpMin, int longestBlockMin)
            => AvailableInflightRestMinutes(longestBlockMin) >= MinInflightRestMinutes(restClass, plannedFdpMin);

        public static string InferOem(string typeId)
        {
            string t = (typeId ?? "").ToUpperInvariant();
            if (t.StartsWith("A3") || t.StartsWith("A2") || t.Contains("AIRBUS")) return "AIRBUS";
            if (t.StartsWith("MD") || t.StartsWith("B7") || t.Contains("BOEING") || t.Contains("737") || t.Contains("787")
                || t.Contains("777") || t.Contains("747") || t.Contains("767") || t.Contains("757") || t.Contains("717"))
                return "BOEING";
            if (t.Contains("CRJ")) return "BOMBARDIER";
            if (t.StartsWith("E1") || t.StartsWith("E2") || t.StartsWith("ERJ") || t.Contains("EMB")) return "EMBRAER";
            if (t.StartsWith("ATR") || t.StartsWith("AT4") || t.StartsWith("AT7")) return "ATR";
            if (t.Contains("DHC") || t.Contains("DH8") || t.Contains("DASH")) return "DH";
            if (t.Contains("C919") || t.Contains("ARJ")) return "COMAC";
            if (t.Contains("SSJ") || t.Contains("MC-21") || t.Contains("SU95") || t.Contains("TU-") || t.Contains("IL-96"))
                return "UAC";
            return "AIRBUS";
        }
    }
}
