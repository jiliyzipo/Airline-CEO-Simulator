using System;
using System.Collections.Generic;
using AirlineCEO.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>
    /// 晨昏与夜幕同一层：夜半球阴影的边界即晨昏线（shader 里画虚线边），
    /// 不再另采样折线，放大后线与阴影不会错开。太阳/月亮仍贴在赤道交点两侧。
    /// </summary>
    public class MapTerminatorLayer : MonoBehaviour
    {
        static Sprite _wireSunSprite;
        static Sprite _wireMoonSprite;
        const float IconSize = 28f; // 太阳/月亮线框同尺寸

        public static MapTerminatorLayer Build(Transform tile)
        {
            var go = new GameObject("TerminatorLayer", typeof(RectTransform));
            go.transform.SetParent(tile, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(MapWidget.MapW, MapWidget.MapH);
            rt.anchoredPosition = Vector2.zero;

            var layer = go.AddComponent<MapTerminatorLayer>();
            layer.ApplySun(force: true);
            return layer;
        }

        float _dec = float.NaN;
        float _lon = float.NaN;
        Material _veilMat;

        void LateUpdate() => ApplySun(force: false);

        /// <summary>按当前游戏 UTC 重算赤纬/直射经度；读档后必须跟顶栏日期走。</summary>
        public void ApplySun(bool force)
        {
            SunPosition(out float decDeg, out float subLonDeg);
            if (_veilMat == null)
                BuildNightVeil(decDeg, subLonDeg);
            else
                ApplyNightSun(_veilMat, decDeg, subLonDeg, transform as RectTransform);
            bool same = !force && !float.IsNaN(_dec)
                        && Mathf.Abs(decDeg - _dec) < 0.12f
                        && Mathf.Abs(Mathf.DeltaAngle(_lon, subLonDeg)) < 1.2f;
            if (same) return;
            _dec = decDeg;
            _lon = subLonDeg;
            RebuildIcons(decDeg, subLonDeg);
        }

        void RebuildIcons(float decDeg, float subLonDeg)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var ch = transform.GetChild(i);
                if (ch != null && ch.name.StartsWith("TermIcons", StringComparison.Ordinal))
                    DestroyImmediate(ch.gameObject);
            }
            BuildIcons(decDeg, subLonDeg);
        }

        // —— 夜幕层：夜半球轻压暗（对齐飞常准） ——
        // 历史方案：瓦片级 shader 蒙版在日界线接缝形成深色方块（UV 与瓦片对不齐）。
        // 现方案：整张世界副本一张 RawImage + NightVeilMercator shader，
        // UV 与副本一一对应；夜幕按晨昏线硬分割，不做暗部羽化。
        static Material _nightVeilMat;
        void BuildNightVeil(float decDeg, float subLonDeg)
        {
            if (_nightVeilMat == null)
            {
                var sh = Shader.Find("Hidden/AirlineCEO/NightVeilMercator");
                if (sh == null) return;
                _nightVeilMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            }
            var m = Instantiate(_nightVeilMat);
            m.hideFlags = HideFlags.HideAndDontSave;
            ApplyNightSun(m, decDeg, subLonDeg, transform as RectTransform);

            var go = new GameObject("TermNightVeil", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            go.transform.SetParent(transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(MapWidget.MapW, MapWidget.MapH);
            rt.anchoredPosition = Vector2.zero;

            var img = go.GetComponent<RawImage>();
            img.texture = Texture2D.whiteTexture;
            img.color = Color.white;
            img.material = m;
            img.raycastTarget = false;
            // UGUI 赋值会拷贝材质，必须再写一次，否则 _SunLonDec 停在默认 (0,0)
            _veilMat = img.material;
            ApplyNightSun(_veilMat, decDeg, subLonDeg, transform as RectTransform);
        }

        static void ApplyNightSun(Material m, float decDeg, float subLonDeg, RectTransform mapRt)
        {
            if (m == null) return;
            m.SetVector("_SunLonDec", new Vector4(subLonDeg, decDeg, 0f, 0f));
            m.SetFloat("_MaxAlpha", 0.52f);
            m.SetColor("_NightColor", new Color(0.004f, 0.012f, 0.055f, 1f));
            m.SetColor("_LineColor", new Color(0.55f, 0.55f, 0.57f, 0.92f));
            m.SetFloat("_EdgePx", 1.7f);
            m.SetFloat("_DashPx", 8f);
            float w = 1920f, h = 960f;
            if (mapRt != null)
            {
                var c = new Vector3[4];
                mapRt.GetWorldCorners(c);
                var canvas = mapRt.GetComponentInParent<Canvas>();
                Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? canvas.worldCamera : null;
                Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, c[0]);
                Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, c[2]);
                w = Mathf.Max(8f, Mathf.Abs(b.x - a.x));
                h = Mathf.Max(8f, Mathf.Abs(b.y - a.y));
            }
            m.SetVector("_UvPerPx", new Vector4(1f / w, 1f / h, 0f, 0f));
        }

        // 赤道与晨昏线交点（与 shader 同一太阳经度），供日/月图标。
        void BuildIcons(float decDeg, float subLonDeg)
        {
            var anchors = new List<Vector2>(2)
            {
                MapWidget.Project(subLonDeg + 90f, 0f),
                MapWidget.Project(subLonDeg - 90f, 0f)
            };

            const float gap = 26f;
            // 每个锚点 ±MapW 各放一份，避免日界线附近平移后图标消失
            for (int i = 0; i < anchors.Count; i++)
            {
                var anchor = anchors[i];

                // 昼侧方向：太阳直射点经度在锚点东 → 昼在东；图标水平分布，同高
                float dLon = NormalizeLonDelta(subLonDeg - LonAt(anchor));
                Vector2 dayDir = dLon >= 0f ? new Vector2(1f, 0f) : new Vector2(-1f, 0f);

                for (int k = -1; k <= 1; k++)
                {
                    var root = new GameObject($"TermIcons_{i}_{k}", typeof(RectTransform));
                    root.transform.SetParent(transform, false);
                    var rrt = root.GetComponent<RectTransform>();
                    rrt.anchorMin = rrt.anchorMax = rrt.pivot = new Vector2(0.5f, 0.5f);
                    rrt.sizeDelta = Vector2.zero;
                    rrt.anchoredPosition = new Vector2(anchor.x + k * MapWidget.MapW, anchor.y);
                    root.AddComponent<MapScreenConstant>();
                    BuildSunIcon(root.transform, dayDir * gap);
                    BuildMoonIcon(root.transform, -dayDir * gap);
                }
            }
        }

        /// <summary>经度差归一到 [-180,180]。</summary>
        static float NormalizeLonDelta(float d)
        {
            while (d > 180f) d -= 360f;
            while (d < -180f) d += 360f;
            return d;
        }

        /// <summary>从锚点反推其经度（MapW 覆盖 360°，直接线性反算）。</summary>
        static float LonAt(Vector2 anchor)
        {
            float x = anchor.x + MapWidget.MapW * 0.5f; // x∈[0,MapW] ↔ lon∈[-180,180]
            return (x / MapWidget.MapW) * 360f - 180f;
        }

        void BuildSunIcon(Transform parent, Vector2 localPos)
        {
            var sun = new GameObject("SunIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            sun.transform.SetParent(parent, false);
            var rt = sun.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(IconSize, IconSize);
            rt.anchoredPosition = localPos;
            var img = sun.GetComponent<Image>();
            img.sprite = WireSunSprite();
            // 与晨昏虚线同色（浅灰），两侧图标风格统一
            img.color = new Color(0.55f, 0.55f, 0.57f, 0.92f);
            img.raycastTarget = false;
        }

        void BuildMoonIcon(Transform parent, Vector2 localPos)
        {
            var moon = new GameObject("MoonIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            moon.transform.SetParent(parent, false);
            var rt = moon.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(IconSize, IconSize);
            rt.anchoredPosition = localPos;
            var img = moon.GetComponent<Image>();
            img.sprite = WireMoonSprite();
            // 与太阳/晨昏线同色
            img.color = new Color(0.55f, 0.55f, 0.57f, 0.92f);
            img.raycastTarget = false;
        }

        /// <summary>线框太阳：空心圆 + 8 射线，无填充。</summary>
        static Sprite WireSunSprite()
        {
            if (_wireSunSprite != null) return _wireSunSprite;
            const int C = 64;
            var tex = new Texture2D(C, C, TextureFormat.RGBA32, false);
            var cols = new Color32[C * C];
            float cx = C * 0.5f, cy = C * 0.5f;
            float r = C * 0.22f;
            float stroke = 1.6f;
            // 空心圆
            StrokeCircle(cols, C, cx, cy, r, stroke);
            // 8 射线
            for (int i = 0; i < 8; i++)
            {
                float ang = i * 45f * Mathf.Deg2Rad;
                float c = Mathf.Cos(ang), s = Mathf.Sin(ang);
                StrokeLine(cols, C, cx + c * (r + 3f), cy + s * (r + 3f), cx + c * (r + 12f), cy + s * (r + 12f), stroke);
            }
            tex.SetPixels32(cols);
            tex.Apply(false, true);
            _wireSunSprite = Sprite.Create(tex, new Rect(0, 0, C, C), new Vector2(0.5f, 0.5f), C);
            return _wireSunSprite;
        }

        /// <summary>线框月亮：开口朝右的新月描边，无填充。</summary>
        static Sprite WireMoonSprite()
        {
            if (_wireMoonSprite != null) return _wireMoonSprite;
            const int C = 64;
            var tex = new Texture2D(C, C, TextureFormat.RGBA32, false);
            var cols = new Color32[C * C];
            float cx = C * 0.5f, cy = C * 0.5f;
            float R = C * 0.30f;
            float ox = cx + R * 0.42f, oy = cy;
            float r = R * 0.92f;
            float stroke = 1.6f;
            // 外圆描边减去内偏圆，只保留新月弧线
            for (int y = 0; y < C; y++)
            {
                for (int x = 0; x < C; x++)
                {
                    float d1 = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    float d2 = Mathf.Sqrt((x - ox) * (x - ox) + (y - oy) * (y - oy));
                    bool onOuter = Mathf.Abs(d1 - R) <= stroke * 0.5f;
                    bool insideCut = d2 < r - stroke * 0.15f;
                    bool onCutEdge = Mathf.Abs(d2 - r) <= stroke * 0.5f && d1 < R + stroke;
                    // 新月：外圆弧（未被切走）+ 内侧咬合弧
                    bool crescentArc = (onOuter && d2 >= r) || (onCutEdge && d1 <= R);
                    if (crescentArc && !insideCut)
                        cols[y * C + x] = new Color32(255, 255, 255, 255);
                }
            }
            tex.SetPixels32(cols);
            tex.Apply(false, true);
            _wireMoonSprite = Sprite.Create(tex, new Rect(0, 0, C, C), new Vector2(0.5f, 0.5f), C);
            return _wireMoonSprite;
        }

        static void StrokeCircle(Color32[] cols, int C, float cx, float cy, float r, float stroke)
        {
            float half = stroke * 0.5f;
            int x0 = Mathf.Max(0, (int)(cx - r - half - 1));
            int x1 = Mathf.Min(C - 1, (int)(cx + r + half + 1));
            int y0 = Mathf.Max(0, (int)(cy - r - half - 1));
            int y1 = Mathf.Min(C - 1, (int)(cy + r + half + 1));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float d = Mathf.Abs(Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r);
                    float a = Mathf.Clamp01(half + 0.5f - d);
                    if (a <= 0f) continue;
                    int idx = y * C + x;
                    byte na = (byte)Mathf.Min(255, cols[idx].a + (int)(a * 255f));
                    cols[idx] = new Color32(255, 255, 255, na);
                }
        }

        static void StrokeLine(Color32[] cols, int C, float x0, float y0, float x1, float y1, float stroke)
        {
            float dx = x1 - x0, dy = y1 - y0;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 0.01f) return;
            int steps = Mathf.Max(2, Mathf.CeilToInt(len * 2f));
            float half = stroke * 0.5f;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float px = x0 + dx * t, py = y0 + dy * t;
                int ix0 = Mathf.Max(0, (int)(px - half - 1));
                int ix1 = Mathf.Min(C - 1, (int)(px + half + 1));
                int iy0 = Mathf.Max(0, (int)(py - half - 1));
                int iy1 = Mathf.Min(C - 1, (int)(py + half + 1));
                for (int y = iy0; y <= iy1; y++)
                    for (int x = ix0; x <= ix1; x++)
                    {
                        float d = Mathf.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
                        float a = Mathf.Clamp01(half + 0.5f - d);
                        if (a <= 0f) continue;
                        int idx = y * C + x;
                        byte na = (byte)Mathf.Min(255, cols[idx].a + (int)(a * 255f));
                        cols[idx] = new Color32(255, 255, 255, na);
                    }
            }
        }

        /// <summary>太阳直射点：赤纬（近似）与游戏 UTC 时角对应的直射经度。</summary>
        static void SunPosition(out float decDeg, out float subLonDeg)
        {
            var gs = GameState.Instance;
            double doy;
            double utcH;
            if (gs != null)
            {
                // 赤纬必须用公历年积日，与顶栏日期一致；不能用财年 Q/D（Q1·D1 会算成 1 月）
                var utc = gs.SimUtc;
                doy = utc.DayOfYear - 1 + (utc.Hour + utc.Minute / 60.0) / 24.0;
                utcH = utc.Hour + utc.Minute / 60.0;
            }
            else
            {
                var now = DateTime.UtcNow;
                doy = now.DayOfYear - 1 + (now.Hour + now.Minute / 60.0 + now.Second / 3600.0) / 24.0;
                utcH = now.Hour + now.Minute / 60.0 + now.Second / 3600.0;
            }
            double dec = -23.44 * Math.Cos(2 * Math.PI * (doy + 10) / 365.0);
            double lon = 180.0 - utcH * 15.0;
            if (lon > 180.0) lon -= 360.0;
            decDeg = (float)dec;
            subLonDeg = (float)lon;
        }
    }
}
