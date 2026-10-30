using System;
using System.Collections.Generic;
using AirlineCEO.Data;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 俯视机型剪影的分类。
    ///
    /// ★2026-10-30 CEO 要求「地图上的白三角换成执飞的机型图标」：
    /// 目录里有 100+ 个机型，不可能一机一图 —— 真实世界里机队图上也是按**外形族**区分的。
    /// 判据只用肉眼在 30~50 px 上真的看得出来的三条：**几台发动机、装在机翼还是尾吊、机翼后掠还是平直**。
    /// 发动机台数/位置由目录的 family + ICAO 代号推出（目录里没有发动机字段，硬加字段要改存档与平衡工具）。
    /// </summary>
    public enum AircraftSilhouette
    {
        /// <summary>单通道双发（A320 族、737 族、C919、MD-80/90、E-Jet、SSJ100）。</summary>
        Narrowbody = 0,
        /// <summary>双通道双发（A300/A310/A330/A350、767/777/787）。</summary>
        Widebody = 1,
        /// <summary>双通道四发（A340、A380、747、Il-96）。</summary>
        QuadJet = 2,
        /// <summary>三发（MD-11）：两翼各一 + 尾部中央一。</summary>
        TriJet = 3,
        /// <summary>涡桨（ATR 族、Dash 8、Twin Otter、EMB 120）：平直翼 + 大直径螺旋桨盘。</summary>
        Turboprop = 4,
        /// <summary>尾吊双发支线（CRJ 族、ERJ 145 族）：细长机身，发动机贴在后机身两侧。</summary>
        RegionalJet = 5,
    }

    /// <summary>
    /// 机型剪影的几何定义与光栅化（**不依赖 UnityEngine**）。
    ///
    /// 为什么要生成而不是画 PNG：一个族一张 PNG 也要 6 张图 + 6 个 .meta + 图集配置，
    /// 而且尺寸一改就要重出图。这里的形状由米制尺寸（翼展/机长/机身宽/后掠角/发动机位置）
    /// 推出，改一个参数就能重出全族，并且能在离线工具（tools/slotcheck）里断言，
    /// 不用开 Unity 就能验证「四种族的剪影确实长得不一样」。
    /// </summary>
    public static class AircraftSilhouetteGeometry
    {
        /// <summary>
        /// 剪影在图标坐标系里的半尺寸（坐标范围 -1..1）。
        /// 留 0.06 的边距不是为了好看：描边是「同一张图放大 1.18 倍垫底」实现的，
        /// 剪影顶到图边就只剩 1/0.18 的余量，翼尖会先于机身被裁掉。
        /// </summary>
        public const float HalfExtent = 0.94f;

        public struct Pt
        {
            public float x;
            public float y;
            public Pt(float x, float y) { this.x = x; this.y = y; }
        }

        /// <summary>一个外形族的米制尺寸。取该族最典型的代表机型，不取平均值（平均值谁都像）。</summary>
        public struct Spec
        {
            public float spanM;        // 翼展
            public float lengthM;      // 机长
            public float fusWidthM;    // 机身宽度（宽体与窄体最直观的差别之一）
            public int wingEngines;    // 每侧机翼下的发动机数（0/1/2）
            public bool tailEngines;   // 尾吊双发
            public bool centerTailEngine; // 尾部中央第三发（MD-11）
            public bool prop;          // 涡桨（画桨盘）
            public float sweepDeg;     // 机翼前缘后掠角
            public float tailSpanRatio;// 平尾半展 / 机翼半展
            public float tailChordRatio;
        }

        /// <summary>
        /// 外形族 → 代表机型尺寸。
        ///
        /// 数值取自公开的三视图尺寸，四舍五入到 0.1 m：
        /// 窄体取 A320-200（35.8 / 37.6 / 3.95）、宽体取 787-9（60.1 / 62.8 / 5.75）、
        /// 四发取 A380-800（79.8 / 72.7 / 7.14）、三发取 MD-11（51.7 / 61.6 / 6.0）、
        /// 涡桨取 ATR 72-600（27.05 / 27.17 / 2.87）、支线取 CRJ900（24.9 / 36.4 / 2.70）。
        /// </summary>
        public static Spec SpecOf(AircraftSilhouette kind)
        {
            switch (kind)
            {
                case AircraftSilhouette.Widebody:
                    return new Spec { spanM = 60.1f, lengthM = 62.8f, fusWidthM = 5.75f, wingEngines = 1, sweepDeg = 28f, tailSpanRatio = 0.36f, tailChordRatio = 0.33f };
                case AircraftSilhouette.QuadJet:
                    return new Spec { spanM = 79.8f, lengthM = 72.7f, fusWidthM = 7.14f, wingEngines = 2, sweepDeg = 30f, tailSpanRatio = 0.33f, tailChordRatio = 0.30f };
                case AircraftSilhouette.TriJet:
                    return new Spec { spanM = 51.7f, lengthM = 61.6f, fusWidthM = 6.00f, wingEngines = 1, centerTailEngine = true, sweepDeg = 30f, tailSpanRatio = 0.34f, tailChordRatio = 0.32f };
                case AircraftSilhouette.Turboprop:
                    return new Spec { spanM = 27.05f, lengthM = 27.17f, fusWidthM = 2.87f, wingEngines = 1, prop = true, sweepDeg = 3f, tailSpanRatio = 0.34f, tailChordRatio = 0.38f };
                case AircraftSilhouette.RegionalJet:
                    return new Spec { spanM = 24.9f, lengthM = 36.4f, fusWidthM = 2.70f, wingEngines = 0, tailEngines = true, sweepDeg = 22f, tailSpanRatio = 0.36f, tailChordRatio = 0.30f };
                default:
                    return new Spec { spanM = 35.8f, lengthM = 37.6f, fusWidthM = 3.95f, wingEngines = 1, sweepDeg = 22f, tailSpanRatio = 0.36f, tailChordRatio = 0.35f };
            }
        }

        /// <summary>
        /// 机型代号 → 外形族。**先按目录的 family 判，再按代号兜底**：
        /// 目录里没有的型号（存档里可能留着旧代号）走代号子串匹配，绝不返回 null。
        /// </summary>
        public static AircraftSilhouette Classify(string typeId)
        {
            if (string.IsNullOrEmpty(typeId)) return AircraftSilhouette.Narrowbody;
            string id = typeId.Trim();
            string up = id.ToUpperInvariant();
            var e = AircraftCatalog.Find(id);
            string fam = e != null && !string.IsNullOrEmpty(e.family) ? e.family.ToUpperInvariant() : "";

            // 涡桨：ATR / Dash 8 / Twin Otter / EMB 120。必须排在最前 ——
            // 它们的 family 里没有「宽体/四发」字样，但 ATR 的 A 开头很容易被后掠角判据误伤。
            if (fam.Contains("ATR") || fam.Contains("DASH 8") || fam.Contains("TWIN OTTER") || fam.Contains("EMB 120")
                || up.StartsWith("ATR") || up.StartsWith("DHC") || up.StartsWith("EMB120"))
                return AircraftSilhouette.Turboprop;

            // 四发：A340/A380/747/Il-96
            if (fam.Contains("A340") || fam.Contains("A380") || fam.Contains("747") || fam.Contains("IL-96"))
                return AircraftSilhouette.QuadJet;

            // 三发：MD-11
            if (fam.Contains("MD-11")) return AircraftSilhouette.TriJet;

            // 尾吊双发支线：CRJ 族、ERJ 135/140/145
            if (fam.Contains("CRJ") || fam.Contains("ERJ FAMILY")
                || up.StartsWith("CRJ") || up.StartsWith("ERJ") || up.StartsWith("EMB1"))
                return AircraftSilhouette.RegionalJet;

            // 双通道双发：判据复用目录里的 ICAO 白名单，避免这里再抄一份「哪些算宽体」
            if (AircraftCatalog.IsWidebody(id)) return AircraftSilhouette.Widebody;

            return AircraftSilhouette.Narrowbody;
        }

        /// <summary>
        /// 屏幕上的图标边长（px）。**按翼展线性映射到 26~50 px**：
        /// 地图上一个 A380 和一个 CRJ 该有肉眼可见的大小差，否则「宽体」这个概念在地图上不存在。
        /// 下限 26 是「还能认出有机翼/尾翼」的最小尺寸，上限 50 是「不会压住隔壁机场圆点」的最大尺寸。
        /// </summary>
        public static float IconSizePx(AircraftSilhouette kind)
        {
            float span = SpecOf(kind).spanM;
            float t = (span - 24.9f) / (79.8f - 24.9f);
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            return 26f + 24f * t;
        }

        /// <summary>外形族的中文名（日志/自检用）。</summary>
        public static string Label(AircraftSilhouette kind)
        {
            switch (kind)
            {
                case AircraftSilhouette.Widebody: return "宽体双发";
                case AircraftSilhouette.QuadJet: return "四发";
                case AircraftSilhouette.TriJet: return "三发";
                case AircraftSilhouette.Turboprop: return "涡桨";
                case AircraftSilhouette.RegionalJet: return "尾吊支线";
                default: return "窄体双发";
            }
        }

        /// <summary>
        /// 生成 size×size 的覆盖度图（行优先，第 0 行是**底部**，与 Texture2D 的 RGBA32 原始布局一致）。
        /// 每格取值 0~1，1 = 完全在剪影内。4×4 超采样后降采样，得到边缘抗锯齿。
        /// </summary>
        public static float[] Coverage(AircraftSilhouette kind, int size)
        {
            if (size < 8) size = 8;
            const int SS = 4;
            int res = size * SS;
            var hit = new bool[res * res];

            var polys = Outlines(kind);
            for (int i = 0; i < polys.Count; i++) Fill(polys[i], hit, res);

            var cov = new float[size * size];
            float inv = 1f / (SS * SS);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int c = 0;
                    for (int sy = 0; sy < SS; sy++)
                    {
                        int row = y * SS + sy;
                        int baseIdx = row * res + x * SS;
                        for (int sx = 0; sx < SS; sx++)
                            if (hit[baseIdx + sx]) c++;
                    }
                    cov[y * size + x] = c * inv;
                }
            return cov;
        }

        /// <summary>
        /// 剪影的全部多边形（图标坐标系，机头朝 +y）。返回的是**完整**形状（左右都已镜像），
        /// 供绘制与自检共用：自检可以断言「左右对称」「包围盒不吃边」「四个族互不相同」。
        /// </summary>
        public static List<Pt[]> Outlines(AircraftSilhouette kind)
        {
            var s = SpecOf(kind);
            float maxM = Math.Max(s.spanM, s.lengthM);
            float k = (HalfExtent * 2f) / maxM;   // 单位/米
            float hy = s.lengthM * 0.5f * k;      // 半机长
            float hx = s.spanM * 0.5f * k;        // 半翼展
            float hw = s.fusWidthM * 0.5f * k;    // 机身半宽

            var polys = new List<Pt[]>(10);

            // 三发机型尾部要留出发动机的位置：机身尾锥提前收口，否则第三台发动机整段被机身盖住，
            // 剪影上就变成「和双发一模一样」—— 那这个族就白分了。
            float tailTrim = s.centerTailEngine ? 0.86f : 1f;
            polys.Add(Fuselage(hy, hw, tailTrim));

            AddMirrored(polys, Wing(hx, hy, hw, s.sweepDeg));
            AddMirrored(polys, Tailplane(hx, hy, hw, s.tailSpanRatio, s.tailChordRatio, s.sweepDeg));

            float rootX = hw * 0.55f;
            float chordRoot = hy * 0.34f;
            float leRoot = hy * 0.14f;
            float tan = (float)Math.Tan(s.sweepDeg * Math.PI / 180.0);
            Func<float, float> leAt = x => leRoot - (x - rootX) * tan;

            if (s.wingEngines > 0)
            {
                // 发动机尺寸**刻意放大**（约为真实比例的 1.6 倍）：40 px 上一台按真比例画的
                // 短舱只有 1 px，四发和双发看起来完全一样，这个族就白分了。
                float nacLenM = s.prop ? 2.4f : (kind == AircraftSilhouette.QuadJet ? 4.2f : (kind == AircraftSilhouette.Widebody ? 5.4f : 3.8f));
                float nacWidM = s.prop ? 1.7f : (kind == AircraftSilhouette.QuadJet ? 2.6f : (kind == AircraftSilhouette.Widebody ? 3.0f : 2.3f));
                float nacLen = nacLenM * k;
                float nacWid = nacWidM * k;
                for (int side = -1; side <= 1; side += 2)
                {
                    for (int n = 0; n < s.wingEngines; n++)
                    {
                        // 内侧发动机在 32% 半展处（A320 实测 5.7/17.9 ≈ 0.32），四发的外侧在 58%
                        float frac = s.wingEngines == 1 ? 0.32f : (n == 0 ? 0.26f : 0.58f);
                        float cx = side * (rootX + (hx - rootX) * frac);
                        float cy = leAt(Math.Abs(cx)) - nacLen * 0.10f;
                        polys.Add(Ellipse(cx, cy, nacWid * 0.5f, nacLen * 0.5f, 14));

                        if (s.prop)
                        {
                            // 螺旋桨盘：桨盘平面垂直于航向，俯视看到的是**一条横线**。
                            // 画成圆盘反而像「多发雷达罩」，玩家认不出是涡桨。
                            float propHalf = 2.1f * k;   // ATR 72 桨径 3.93 m，同样放大到看得清
                            float py = cy + nacLen * 0.5f;
                            float t = Math.Max(0.005f, k * 0.24f);
                            polys.Add(Rect(cx - propHalf, py - t, cx + propHalf, py + t));
                        }
                    }
                }
            }

            if (s.tailEngines)
            {
                // 尾吊：发动机贴在后机身两侧，从俯视看是机身外缘的两个凸起
                float nacLen = 2.6f * k;
                float nacWid = 1.5f * k;
                float cy = -hy * 0.50f;
                for (int side = -1; side <= 1; side += 2)
                    polys.Add(Ellipse(side * (hw * 0.92f + nacWid * 0.42f), cy, nacWid * 0.5f, nacLen * 0.5f, 14));
            }

            if (s.centerTailEngine)
            {
                // 第三发：机尾正中央。长度要**越过后机身收口**才看得见（见上面的 tailTrim），
                // 短舱也比真机画粗一点 —— 否则「三发」和「双发」在 40 px 上差不到 2 px，族分不出来。
                float nacLen = 3.9f * k;
                float nacWid = 2.3f * k;
                polys.Add(Ellipse(0f, -hy * 0.90f, nacWid * 0.5f, nacLen * 0.5f, 14));
            }

            return polys;
        }

        // ── 形状构造 ──

        static Pt[] Fuselage(float hy, float hw, float tailTrim)
        {
            float tail = -hy * tailTrim;
            var right = new[]
            {
                new Pt(0f, hy),
                new Pt(hw * 0.50f, hy - (hy - tail) * 0.07f),
                new Pt(hw * 0.86f, hy * 0.72f),
                new Pt(hw, hy * 0.30f),
                new Pt(hw, -hy * 0.62f),
                new Pt(hw * 0.80f, -hy * 0.86f),
                new Pt(hw * 0.32f, tail + (hy + tail) * 0.01f),
                new Pt(0f, tail),
            };
            return Close(right);
        }

        static Pt[] Wing(float hx, float hy, float hw, float sweepDeg)
        {
            float rootX = hw * 0.55f;
            // 弦长比取真机偏大一号：真机 A320 根弦约机长的 15%，照实画在 40 px 上会细成一条线，
            // 整架飞机看着像「战斗机」。图标是示意图不是三视图，这里放大到 20%（根）/ 6%（尖）。
            float chordRoot = hy * 0.40f;
            float chordTip = hy * 0.115f;
            float leRoot = hy * 0.14f;
            float tan = (float)Math.Tan(sweepDeg * Math.PI / 180.0);
            Func<float, float> le = x => leRoot - (x - rootX) * tan;
            float teRoot = leRoot - chordRoot;
            float leTip = le(hx);
            float teTip = leTip - chordTip;
            Func<float, float> te = x => teRoot + (teTip - teRoot) * ((x - rootX) / Math.Max(1e-4f, hx - rootX));

            // 翼尖画成**钝头**（前后缘都是直切 + 小倒角）—— 真机的翼尖不是针尖，
            // 画尖了整架飞机就变成战斗机剪影（2026-10-30 首版就是这么错的）。
            return new[]
            {
                new Pt(rootX, leRoot),
                new Pt(hx * 0.90f, le(hx * 0.90f)),
                new Pt(hx, leTip + chordTip * 0.10f),
                new Pt(hx, teTip - chordTip * 0.10f),
                new Pt(hx * 0.94f, te(hx * 0.94f)),
                new Pt(rootX, teRoot),
            };
        }

        static Pt[] Tailplane(float hx, float hy, float hw, float spanRatio, float chordRatio, float sweepDeg)
        {
            float rootX = hw * 0.50f;
            float span = Math.Max(rootX + hw * 0.2f, hx * spanRatio);
            float chordRoot = hy * 0.34f * chordRatio;
            float leRoot = -hy * 0.74f;
            float sweep = Math.Max(18f, sweepDeg * 0.6f);
            float tan = (float)Math.Tan(sweep * Math.PI / 180.0);
            Func<float, float> le = x => leRoot - (x - rootX) * tan;
            float teRoot = leRoot - chordRoot;
            float teTip = le(span) - chordRoot * 0.5f;
            Func<float, float> te = x => teRoot + (teTip - teRoot) * ((x - rootX) / Math.Max(1e-4f, span - rootX));

            return new[]
            {
                new Pt(rootX, leRoot),
                new Pt(span * 0.97f, le(span * 0.97f)),
                new Pt(span, te(span) + chordRoot * 0.15f),
                new Pt(span * 0.97f, te(span * 0.97f)),
                new Pt(rootX, teRoot),
            };
        }

        static Pt[] Ellipse(float cx, float cy, float rx, float ry, int n)
        {
            var pts = new Pt[n];
            for (int i = 0; i < n; i++)
            {
                double a = i * 2.0 * Math.PI / n;
                pts[i] = new Pt(cx + rx * (float)Math.Cos(a), cy + ry * (float)Math.Sin(a));
            }
            return pts;
        }

        static Pt[] Rect(float x0, float y0, float x1, float y1)
            => new[] { new Pt(x0, y0), new Pt(x1, y0), new Pt(x1, y1), new Pt(x0, y1) };

        /// <summary>把右半侧（从轴上一点到轴上另一点）的多边形补成完整闭合形状。</summary>
        static Pt[] Close(Pt[] rightHalf)
        {
            var pts = new Pt[rightHalf.Length * 2 - 2];
            for (int i = 0; i < rightHalf.Length; i++) pts[i] = rightHalf[i];
            int w = rightHalf.Length;
            for (int i = w - 2; i >= 1; i--) pts[w + (w - 2 - i)] = new Pt(-rightHalf[i].x, rightHalf[i].y);
            return pts;
        }

        static void AddMirrored(List<Pt[]> polys, Pt[] right)
        {
            polys.Add(right);
            var m = new Pt[right.Length];
            for (int i = 0; i < right.Length; i++) m[i] = new Pt(-right[i].x, right[i].y);
            polys.Add(m);
        }

        // ── 光栅化：扫描线 + 4×4 超采样（点采样由调用方的降采样完成抗锯齿）──

        static void Fill(Pt[] poly, bool[] hit, int res)
        {
            int n = poly.Length;
            if (n < 3) return;
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                if (poly[i].y < minY) minY = poly[i].y;
                if (poly[i].y > maxY) maxY = poly[i].y;
            }
            int row0 = RowOf(minY, res);
            int row1 = RowOf(maxY, res);
            var xs = new List<float>(8);
            for (int row = row0; row <= row1; row++)
            {
                float y = ((row + 0.5f) / res) * 2f - 1f;
                xs.Clear();
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    float yi = poly[i].y, yj = poly[j].y;
                    if ((yi <= y && yj > y) || (yj <= y && yi > y))
                    {
                        float t = (y - yi) / (yj - yi);
                        xs.Add(poly[i].x + t * (poly[j].x - poly[i].x));
                    }
                }
                if (xs.Count < 2) continue;
                xs.Sort();
                int baseIdx = row * res;
                for (int s = 0; s + 1 < xs.Count; s += 2)
                {
                    int c0 = ColOf(xs[s], res);
                    int c1 = ColOf(xs[s + 1], res);
                    if (c1 < c0) continue;
                    for (int c = c0; c <= c1; c++) hit[baseIdx + c] = true;
                }
            }
        }

        static int RowOf(float y, int res) => Clamp((int)Math.Floor((y + 1f) * 0.5f * res), 0, res - 1);
        static int ColOf(float x, int res) => Clamp((int)Math.Floor((x + 1f) * 0.5f * res), 0, res - 1);
        static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
