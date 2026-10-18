using System;

namespace AirlineCEO.UI
{
    /// <summary>
    /// GCJ-02（火星坐标）与 WGS-84 互转，公开标准算法。
    /// 高德/腾讯底图瓦片使用 GCJ-02，而机场/航线/晨昏线/台风等数据使用 WGS-84，
    /// 二者相差约 50~700 米（非线性、随地点变化）。国内地图展示必须做此纠偏，
    /// 否则矢量标注会整体偏离底图。境外坐标不在偏移范围内，原样返回。
    /// </summary>
    public static class CoordConverter
    {
        const double A = 6378245.0;                 // 克拉索夫斯基椭球长半轴
        const double EE = 0.00669342162296594323;   // 椭球第一偏心率平方

        /// <summary>是否在 GCJ-02 偏移适用范围之外（境外 + 台湾）。</summary>
        public static bool OutOfChina(double lon, double lat)
            => lon < 72.004 || lon > 137.8347 || lat < 0.8293 || lat > 55.8271 || IsTaiwan(lon, lat);

        /// <summary>台湾本岛/澎湖：高德在台湾使用 WGS-84 不偏移（港澳仍用 GCJ-02 偏移）。</summary>
        static bool IsTaiwan(double lon, double lat)
            => lon >= 119.3 && lon <= 122.3 && lat >= 21.9 && lat <= 25.4;

        /// <summary>WGS-84 → GCJ-02（正向，把真实坐标画到高德/腾讯底图上时用）。</summary>
        public static void Wgs84ToGcj02(double wgsLon, double wgsLat, out double gcjLon, out double gcjLat)
        {
            if (OutOfChina(wgsLon, wgsLat)) { gcjLon = wgsLon; gcjLat = wgsLat; return; }

            double dLat = TransformLat(wgsLon - 105.0, wgsLat - 35.0);
            double dLon = TransformLon(wgsLon - 105.0, wgsLat - 35.0);
            double radLat = wgsLat / 180.0 * Math.PI;
            double magic = Math.Sin(radLat);
            magic = 1.0 - EE * magic * magic;
            double sqrtMagic = Math.Sqrt(magic);
            dLat = (dLat * 180.0) / ((A * (1.0 - EE)) / (magic * sqrtMagic) * Math.PI);
            dLon = (dLon * 180.0) / (A / sqrtMagic * Math.Cos(radLat) * Math.PI);
            gcjLat = wgsLat + dLat;
            gcjLon = wgsLon + dLon;
        }

        /// <summary>GCJ-02 → WGS-84（反向，点击高德地图反算真实坐标时用）。</summary>
        public static void Gcj02ToWgs84(double gcjLon, double gcjLat, out double wgsLon, out double wgsLat)
        {
            if (OutOfChina(gcjLon, gcjLat)) { wgsLon = gcjLon; wgsLat = gcjLat; return; }
            Wgs84ToGcj02(gcjLon, gcjLat, out double tLon, out double tLat);
            wgsLon = gcjLon - (tLon - gcjLon);
            wgsLat = gcjLat - (tLat - gcjLat);
        }

        static double TransformLat(double x, double y)
        {
            double ret = -100.0 + 2.0 * x + 3.0 * y + 0.2 * y * y + 0.1 * x * y + 0.2 * Math.Sqrt(Math.Abs(x));
            ret += (20.0 * Math.Sin(6.0 * x * Math.PI) + 20.0 * Math.Sin(2.0 * x * Math.PI)) * 2.0 / 3.0;
            ret += (20.0 * Math.Sin(y * Math.PI) + 40.0 * Math.Sin(y / 3.0 * Math.PI)) * 2.0 / 3.0;
            ret += (160.0 * Math.Sin(y / 12.0 * Math.PI) + 320.0 * Math.Sin(y * Math.PI / 30.0)) * 2.0 / 3.0;
            return ret;
        }

        static double TransformLon(double x, double y)
        {
            double ret = 300.0 + x + 2.0 * y + 0.1 * x * x + 0.1 * x * y + 0.1 * Math.Sqrt(Math.Abs(x));
            ret += (20.0 * Math.Sin(6.0 * x * Math.PI) + 20.0 * Math.Sin(2.0 * x * Math.PI)) * 2.0 / 3.0;
            ret += (20.0 * Math.Sin(x * Math.PI) + 40.0 * Math.Sin(x / 3.0 * Math.PI)) * 2.0 / 3.0;
            ret += (150.0 * Math.Sin(x / 12.0 * Math.PI) + 300.0 * Math.Sin(x / 30.0 * Math.PI)) * 2.0 / 3.0;
            return ret;
        }
    }
}
