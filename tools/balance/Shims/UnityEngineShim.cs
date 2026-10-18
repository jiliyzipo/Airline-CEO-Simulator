using System.Collections.Generic;
using AirlineCEO.Data;

namespace UnityEngine
{
    /// <summary>
    /// 离线平衡工具用的 UnityEngine 垫片（仅提供共享逻辑真正用到的那几个 API）。
    /// 存在的意义：让 tools/balance 能链接 <c>TakeoverWorldBuilder</c> 这类「只在 Unity 工程里、
    /// 但业务逻辑其实与引擎无关」的种子代码，从而在无 Editor 的情况下复核全航司班次分布。
    /// </summary>
    internal static class Mathf
    {
        public const float PI = 3.14159274f;
        public const float Deg2Rad = PI / 180f;
        public const float Rad2Deg = 180f / PI;

        public static float Max(float a, float b) => a > b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
        public static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
        public static float Clamp01(float v) => Clamp(v, 0f, 1f);
        public static float Abs(float v) => v < 0f ? -v : v;
        public static int Abs(int v) => v < 0 ? -v : v;
        public static float Round(float v) => (float)System.Math.Round(v, System.MidpointRounding.AwayFromZero);
        public static int RoundToInt(float v) => (int)System.Math.Round(v, System.MidpointRounding.AwayFromZero);
        public static float Floor(float v) => (float)System.Math.Floor(v);
        public static float Ceil(float v) => (float)System.Math.Ceiling(v);
        public static float Sqrt(float v) => (float)System.Math.Sqrt(v);
        public static float Pow(float a, float b) => (float)System.Math.Pow(a, b);
        public static float Exp(float v) => (float)System.Math.Exp(v);
        public static float Log(float v) => (float)System.Math.Log(v);
        public static float Sin(float v) => (float)System.Math.Sin(v);
        public static float Cos(float v) => (float)System.Math.Cos(v);
        public static float Tan(float v) => (float)System.Math.Tan(v);
        public static float Atan(float v) => (float)System.Math.Atan(v);
        public static float Asin(float v) => (float)System.Math.Asin(v);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
    }
}

namespace AirlineCEO.UI
{
    /// <summary>
    /// 地图微件的离线替身：只保留航距计算，且直接委托给 <see cref="AirportGeoCatalog"/>，
    /// 保证「地图口径」与「经济口径」用的是同一份坐标（不再复刻第二张经纬度表）。
    /// </summary>
    internal static class MapWidget
    {
        public static float GreatCircleKm(string origin, string dest)
            => AirportGeoCatalog.GreatCircleKm(origin, dest);
    }
}

namespace AirlineCEO.Data
{
    /// <summary>
    /// `DataBase`（Unity 侧为 MonoBehaviour + ScriptableObject 资源库）的离线替身。
    /// 种子构建只用它取董事会姓名，拿不到时 `AttachBoard` 本就有 null 保护，故返回 null。
    /// </summary>
    internal class DataBase
    {
        public static DataBase? Instance => null;

        public IReadOnlyList<BoardMemberData>? BoardMembers => null;
    }

    /// <summary>董事会成员资源的最小替身（离线只用到 fullName）。</summary>
    internal class BoardMemberData
    {
        public string id = string.Empty;
        public string fullName = string.Empty;
    }
}
