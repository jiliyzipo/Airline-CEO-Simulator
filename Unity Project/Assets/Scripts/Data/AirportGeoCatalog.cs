using System;
using System.Collections.Generic;

namespace AirlineCEO.Data
{
    /// <summary>
    /// 机场经纬度表（IATA → lon/lat，WGS-84）与大圆航距计算的唯一真源。
    /// 刻意不依赖 UnityEngine：地图侧（MapWidget 投影/矢量图）与离线平衡工具（tools/balance）
    /// 共用同一份坐标，避免「地图一套坐标、经济另一套航距」的口径分叉。
    /// </summary>
    public static class AirportGeoCatalog
    {
        // 4~5 位小数：0.1° 精度（约 10km）在矢量底图上会让机场圆点/标注明显偏离跑道。
        public static readonly (string code, float lon, float lat)[] All =
        {
            ("PEK",116.5846f,40.0799f),("PKX",116.4106f,39.5097f),("SHA",121.3363f,31.1979f),("PVG",121.8052f,31.1434f),
            ("CAN",113.2984f,23.3924f),("SZX",113.8107f,22.6393f),("CTU",103.9471f,30.5785f),("TFU",104.4414f,30.3125f),
            ("CKG",106.6417f,29.7192f),("XIY",108.7538f,34.4386f),("KMG",102.9267f,25.1019f),("HGH",120.4340f,30.2295f),
            ("NKG",118.8620f,31.7420f),("WUH",114.2081f,30.7838f),("CSX",113.2196f,28.1892f),("XMN",118.1277f,24.5440f),
            ("FOC",119.6633f,25.9340f),("TAO",120.0885f,36.3616f),("TSN",117.3462f,39.1244f),("DLC",121.5386f,38.9657f),
            ("SHE",123.4825f,41.6401f),("HRB",126.2503f,45.6233f),("URC",87.4742f,43.9071f),("LHW",103.6206f,36.5152f),
            ("SYX",109.4120f,18.3029f),("HAK",110.4589f,19.9349f),
            ("ICN",126.4408f,37.4602f),("NRT",140.3863f,35.7719f),("HND",139.7798f,35.5494f),("KIX",135.2440f,34.4347f),
            ("HKG",113.9145f,22.3089f),("MFM",113.5917f,22.1496f),("TPE",121.2330f,25.0777f),("SIN",103.9915f,1.3644f),
            ("BKK",100.7476f,13.6900f),("KUL",101.7022f,2.7456f),("DEL",77.1030f,28.5562f),("BOM",72.8656f,19.0896f),
            ("CGK",106.6557f,-6.1256f),("MNL",121.0199f,14.5086f),
            ("LHR",-0.4543f,51.4700f),("CDG",2.5479f,49.0097f),("FRA",8.5705f,50.0379f),("AMS",4.7639f,52.3086f),
            ("IST",28.8142f,41.2603f),("MAD",-3.5668f,40.4719f),("BCN",2.0785f,41.2971f),("FCO",12.2508f,41.8003f),
            ("MUC",11.7861f,48.3538f),("ZRH",8.5492f,47.4647f),("VIE",16.5697f,48.1103f),("CPH",12.6508f,55.6180f),
            ("ARN",17.9186f,59.6519f),("HEL",24.9633f,60.3172f),("SVO",37.4146f,55.9726f),
            ("LAX",-118.4081f,33.9425f),("SFO",-122.3750f,37.6189f),("JFK",-73.7781f,40.6413f),("ORD",-87.9048f,41.9786f),
            ("ATL",-84.4277f,33.6367f),("SEA",-122.3088f,47.4502f),("YVR",-123.1815f,49.1967f),("YYZ",-79.6306f,43.6772f),
            ("DFW",-97.0403f,32.8998f),("DEN",-104.6731f,39.8617f),("CLT",-80.9431f,35.2140f),("MIA",-80.2870f,25.7959f),
            ("PHX",-112.0116f,33.4343f),("LAS",-115.1523f,36.0840f),("MCO",-81.3081f,28.4312f),("IAH",-95.3414f,29.9844f),
            ("BOS",-71.0096f,42.3656f),("MSP",-93.2218f,44.8848f),("DTW",-83.3534f,42.2124f),("SAN",-117.1900f,32.7338f),
            ("GRU",-46.4695f,-23.4356f),("EZE",-58.5358f,-34.8222f),
            ("BOG",-74.1469f,4.7016f),("SCL",-70.7858f,-33.3930f),("LIM",-77.1143f,-12.0219f),
            ("JNB",28.2460f,-26.1337f),("CAI",31.4053f,30.1219f),
            ("ADD",38.7993f,8.9779f),("CPT",18.6017f,-33.9648f),("LOS",3.3212f,6.5774f),("NBO",36.9278f,-1.3192f),
            ("ACC",-0.1668f,5.6052f),
            ("SYD",151.1772f,-33.9461f),("MEL",144.8432f,-37.6733f),("AKL",174.7913f,-37.0082f),
            ("BNE",153.1175f,-27.3842f),("PER",115.9672f,-31.9403f),
            ("DXB",55.3644f,25.2528f),("DOH",51.6081f,25.2731f),
        };

        /// <summary>坐标缺失时的回退航距；调用方不得据此判定「短航」（见 RouteGeography 注释）。</summary>
        public const float UnknownKm = 2200f;

        static readonly Dictionary<string, (float lon, float lat)> Index = BuildIndex();

        static Dictionary<string, (float lon, float lat)> BuildIndex()
        {
            var map = new Dictionary<string, (float lon, float lat)>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < All.Length; i++) map[All[i].code] = (All[i].lon, All[i].lat);
            return map;
        }

        public static bool TryLonLat(string code, out float lon, out float lat)
        {
            lon = lat = 0f;
            if (string.IsNullOrEmpty(code)) return false;
            if (!Index.TryGetValue(code.ToUpperInvariant(), out var ll)) return false;
            lon = ll.lon;
            lat = ll.lat;
            return true;
        }

        /// <summary>两点大圆航距（km）。任一坐标缺失时返回 <see cref="UnknownKm"/>。</summary>
        public static float GreatCircleKm(string origin, string dest)
        {
            if (!TryLonLat(origin, out float lon1, out float lat1)) return UnknownKm;
            if (!TryLonLat(dest, out float lon2, out float lat2)) return UnknownKm;
            const float R = 6371f;
            const float Deg2Rad = 0.017453292f; // Mathf.Deg2Rad 的等值常量
            float p1 = lat1 * Deg2Rad;
            float p2 = lat2 * Deg2Rad;
            float dPhi = (lat2 - lat1) * Deg2Rad;
            float dLam = (lon2 - lon1) * Deg2Rad;
            float h = MathF.Sin(dPhi * 0.5f) * MathF.Sin(dPhi * 0.5f)
                      + MathF.Cos(p1) * MathF.Cos(p2) * MathF.Sin(dLam * 0.5f) * MathF.Sin(dLam * 0.5f);
            return 2f * R * MathF.Asin(Math.Clamp(MathF.Sqrt(h), 0f, 1f));
        }
    }
}
