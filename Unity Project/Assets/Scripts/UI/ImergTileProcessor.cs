using System.Collections.Generic;
using UnityEngine;

namespace AirlineCEO.UI
{
    /// <summary>
    /// GIBS IMERG 瓦片平滑：官方 PNG 是 ~10km 网格的最近邻放大。
    /// ExtractGrid 把瓦片还原为「格心采样网格」（全量真实信息，~4KB）；
    /// Smooth 流程（解决方块感的三连击）：
    ///   1) 外扩 2 格取邻居网格边缘格点（跨瓦片连续）；
    ///   2) 网格 [1 2 1] 高斯模糊——同值平台揉成圆顶，消灭方块直线边；
    ///   3) smoothstep 缓动双线性插值——C1 连续，格点边界折痕变圆润过渡。
    /// </summary>
    public static class ImergTileProcessor
    {
        /// <summary>瓦片的格心网格。a=255 有效回波，a=0 无回波。</summary>
        public sealed class TileGrid
        {
            public int BlockSize;
            public int CW, CH;
            public Color32[] Cells; // CW*CH
        }

        /// <summary>从瓦片提取格心网格（检测块边长，采样各块中心像素）。</summary>
        public static TileGrid ExtractGrid(Texture2D tex)
        {
            return ExtractGrid(tex.GetPixels32(), tex.width, tex.height);
        }

        /// <summary>从已读出的像素数组提取格心网格（供后台平滑复用，避免重复 GetPixels32）。</summary>
        public static TileGrid ExtractGrid(Color32[] px, int w, int h)
        {
            int b = DetectBlockSize(px, w, h);
            if (b < 4)
                return new TileGrid { BlockSize = 1, CW = w, CH = h, Cells = px };

            int cw = w / b, ch = h / b;
            var cells = new Color32[cw * ch];
            for (int cy = 0; cy < ch; cy++)
            {
                int py = cy * b + b / 2;
                for (int cx = 0; cx < cw; cx++)
                {
                    var p = px[py * w + cx * b + b / 2];
                    cells[cy * cw + cx] = new Color32(p.r, p.g, p.b, (byte)(p.a > 0 ? 255 : 0));
                }
            }
            return new TileGrid { BlockSize = b, CW = cw, CH = ch, Cells = cells };
        }

        /// <summary>跨瓦片影响区：外扩格数（1 格 = 半个色块，2 格 ≈ 40px @512/16px 块）。</summary>
        const int Margin = 2;

        /// <summary>
        /// 高斯模糊趟数：1 = 单趟五点核（对比保留）；2 = 双趟（等效 σ×√2，更柔和）。
        /// 观感不满可改回 1。
        /// </summary>
        const int BlurPasses = 2;

        /// <summary>主线程同步封装：读像素 → 计算 → 写回（供需要原地同步的场景使用）。</summary>
        public static void Smooth(Texture2D tex, TileGrid g,
            TileGrid west, TileGrid east, TileGrid north, TileGrid south)
        {
            if (tex == null || g == null || g.BlockSize < 4) return;
            var px = tex.GetPixels32();
            ComputePixels(px, tex.width, tex.height, g, west, east, north, south);
            tex.SetPixels32(px);
            tex.Apply(false, false);
        }

        /// <summary>
        /// 平滑核心计算（高斯 + quintic 插值），就地修改传入的 px 数组。
        /// 纯数学操作、不碰任何 Unity 纹理 API，可安全地在后台线程（Task.Run）执行；
        /// 主线程只负责 GetPixels32 / SetPixels32 / Apply 的读与写回。
        /// </summary>
        public static void ComputePixels(Color32[] px, int w, int h, TileGrid g,
            TileGrid west, TileGrid east, TileGrid north, TileGrid south)
        {
            if (px == null || g == null || g.BlockSize < 4) return;
            int b = g.BlockSize, cw = g.CW, ch = g.CH;

            // 1) 外扩网格（含邻居边缘格点）
            int ew = cw + Margin * 2, eh = ch + Margin * 2;
            var raw = new Color32[ew * eh];
            for (int ey = -Margin; ey < ch + Margin; ey++)
                for (int ex = -Margin; ex < cw + Margin; ex++)
                    raw[(ey + Margin) * ew + (ex + Margin)] = SampleCell(g, west, east, north, south, ex, ey);

            // 2) 网格五点核高斯模糊（可双趟）：平台变圆顶、直线边消失；RGB 用预乘加权避免边缘发黑
            var cell = Gaussian5(raw, ew, eh);
            for (int p = 1; p < BlurPasses; p++)
                cell = Gaussian5(cell, ew, eh);

            // 3) quintic 缓动双线性（6t⁵−15t⁴+10t³，C2 连续）：色彩梯度过渡比 smoothstep 更柔
            for (int y = 0; y < h; y++)
            {
                float uy = (y + 0.5f) / b - 0.5f + Margin;
                int ey = Mathf.Clamp(Mathf.FloorToInt(uy), 0, eh - 2);
                float ty = Mathf.Clamp01(uy - ey);
                ty = ty * ty * ty * (ty * (ty * 6f - 15f) + 10f);
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    float ux = (x + 0.5f) / b - 0.5f + Margin;
                    int ex = Mathf.Clamp(Mathf.FloorToInt(ux), 0, ew - 2);
                    float tx = Mathf.Clamp01(ux - ex);
                    tx = tx * tx * tx * (tx * (tx * 6f - 15f) + 10f);

                    int i00 = ey * ew + ex, i10 = i00 + 1, i01 = i00 + ew, i11 = i01 + 1;
                    float cov = Mathf.Lerp(Mathf.Lerp(cell[i00].a, cell[i10].a, tx),
                                           Mathf.Lerp(cell[i01].a, cell[i11].a, tx), ty) / 255f;
                    int idx = row + x;
                    if (cov <= 0.02f)
                    {
                        px[idx] = new Color32(0, 0, 0, 0);
                        continue;
                    }
                    float r = Mathf.Lerp(Mathf.Lerp(cell[i00].r, cell[i10].r, tx), Mathf.Lerp(cell[i01].r, cell[i11].r, tx), ty);
                    float gg = Mathf.Lerp(Mathf.Lerp(cell[i00].g, cell[i10].g, tx), Mathf.Lerp(cell[i01].g, cell[i11].g, tx), ty);
                    float bb = Mathf.Lerp(Mathf.Lerp(cell[i00].b, cell[i10].b, tx), Mathf.Lerp(cell[i01].b, cell[i11].b, tx), ty);
                    px[idx] = new Color32(
                        (byte)Mathf.RoundToInt(r), (byte)Mathf.RoundToInt(gg), (byte)Mathf.RoundToInt(bb),
                        (byte)Mathf.RoundToInt(cov * 255f));
                }
            }
        }

        /// <summary>
        /// 取扩展格点值：界内取自身；出界取相邻瓦片网格最靠本瓦片的行/列（支持 Margin 深度）；
        /// 角点用正交邻网格近似；邻居缺失回退 clamp。
        /// 纹理空间：行 0 = 南。ex&lt;0 是西侧、ey&lt;0 是南侧。
        /// </summary>
        static Color32 SampleCell(TileGrid g, TileGrid west, TileGrid east, TileGrid north, TileGrid south, int ex, int ey)
        {
            bool xOut = ex < 0 || ex >= g.CW;
            bool yOut = ey < 0 || ey >= g.CH;
            if (!xOut && !yOut) return g.Cells[ey * g.CW + ex];

            if (xOut)
            {
                var nb = ex < 0 ? west : east;
                if (Compatible(nb, g))
                {
                    int col = ex < 0 ? nb.CW + ex : ex - g.CW;
                    if (col >= 0 && col < nb.CW)
                        return nb.Cells[Mathf.Clamp(ey, 0, nb.CH - 1) * nb.CW + col];
                }
            }
            if (yOut)
            {
                var nb = ey < 0 ? south : north;
                if (Compatible(nb, g))
                {
                    int rowIdx = ey < 0 ? nb.CH + ey : ey - g.CH;
                    if (rowIdx >= 0 && rowIdx < nb.CH)
                        return nb.Cells[rowIdx * nb.CW + Mathf.Clamp(ex, 0, nb.CW - 1)];
                }
            }
            // 回退：clamp 自身
            return g.Cells[Mathf.Clamp(ey, 0, g.CH - 1) * g.CW + Mathf.Clamp(ex, 0, g.CW - 1)];
        }

        static bool Compatible(TileGrid nb, TileGrid g) =>
            nb != null && nb.BlockSize == g.BlockSize && nb.CW == g.CW && nb.CH == g.CH;

        /// <summary>
        /// [1 4 6 4 1]/16 可分离五点核。RGB 以 alpha 预乘加权、模糊后除回，
        /// 避免回波边缘被无数据格子的黑色拉暗；色彩与覆盖度同步揉平。
        /// </summary>
        static Color32[] Gaussian5(Color32[] src, int w, int h)
        {
            int n = src.Length;
            var tmp = new float[n * 4]; // rgba floats
            var dst = new Color32[n];

            // 预乘 RGB → 横向 [1 4 6 4 1]
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    int xm2 = Mathf.Max(0, x - 2), xm1 = Mathf.Max(0, x - 1);
                    int xp1 = Mathf.Min(w - 1, x + 1), xp2 = Mathf.Min(w - 1, x + 2);
                    var cm2 = src[row + xm2]; var cm1 = src[row + xm1];
                    var c0 = src[row + x];
                    var cp1 = src[row + xp1]; var cp2 = src[row + xp2];
                    float am2 = cm2.a / 255f, am1 = cm1.a / 255f;
                    float a0 = c0.a / 255f, ap1 = cp1.a / 255f, ap2 = cp2.a / 255f;
                    int o = (row + x) * 4;
                    tmp[o + 0] = (cm2.r * am2 + 4f * cm1.r * am1 + 6f * c0.r * a0 + 4f * cp1.r * ap1 + cp2.r * ap2) * 0.0625f;
                    tmp[o + 1] = (cm2.g * am2 + 4f * cm1.g * am1 + 6f * c0.g * a0 + 4f * cp1.g * ap1 + cp2.g * ap2) * 0.0625f;
                    tmp[o + 2] = (cm2.b * am2 + 4f * cm1.b * am1 + 6f * c0.b * a0 + 4f * cp1.b * ap1 + cp2.b * ap2) * 0.0625f;
                    tmp[o + 3] = (cm2.a + 4f * cm1.a + 6f * c0.a + 4f * cp1.a + cp2.a) * 0.0625f;
                }
            }
            // 纵向 [1 4 6 4 1] + 除回预乘
            for (int y = 0; y < h; y++)
            {
                int rm2 = Mathf.Max(0, y - 2) * w, rm1 = Mathf.Max(0, y - 1) * w;
                int r1 = y * w;
                int rp1 = Mathf.Min(h - 1, y + 1) * w, rp2 = Mathf.Min(h - 1, y + 2) * w;
                for (int x = 0; x < w; x++)
                {
                    int im2 = (rm2 + x) * 4, im1 = (rm1 + x) * 4, i1 = (r1 + x) * 4;
                    int ip1 = (rp1 + x) * 4, ip2 = (rp2 + x) * 4;
                    float pr = tmp[im2] + 4f * tmp[im1] + 6f * tmp[i1] + 4f * tmp[ip1] + tmp[ip2];
                    float pg = tmp[im2 + 1] + 4f * tmp[im1 + 1] + 6f * tmp[i1 + 1] + 4f * tmp[ip1 + 1] + tmp[ip2 + 1];
                    float pb = tmp[im2 + 2] + 4f * tmp[im1 + 2] + 6f * tmp[i1 + 2] + 4f * tmp[ip1 + 2] + tmp[ip2 + 2];
                    float pa = (tmp[im2 + 3] + 4f * tmp[im1 + 3] + 6f * tmp[i1 + 3] + 4f * tmp[ip1 + 3] + tmp[ip2 + 3]) * 0.0625f;
                    float a = pa / 255f;
                    dst[r1 + x] = a > 1e-3f
                        ? new Color32(
                            (byte)Mathf.Clamp(Mathf.RoundToInt(pr / (16f * a)), 0, 255),
                            (byte)Mathf.Clamp(Mathf.RoundToInt(pg / (16f * a)), 0, 255),
                            (byte)Mathf.Clamp(Mathf.RoundToInt(pb / (16f * a)), 0, 255),
                            (byte)Mathf.RoundToInt(pa))
                        : new Color32(0, 0, 0, 0);
                }
            }
            return dst;
        }

        /// <summary>检测同值色块边长（中部若干扫描行的 run 长中位数，归一到 2 的幂）。</summary>
        static int DetectBlockSize(Color32[] px, int w, int h)
        {
            var runs = new List<int>(64);
            for (int row = h / 4; row <= h * 3 / 4; row += Mathf.Max(1, h / 8))
            {
                int run = 1;
                for (int x = 1; x < w; x++)
                {
                    var cur = px[row * w + x];
                    var prv = px[row * w + x - 1];
                    if (cur.r == prv.r && cur.g == prv.g && cur.b == prv.b && cur.a == prv.a) run++;
                    else
                    {
                        if (run >= 2) runs.Add(run);
                        run = 1;
                    }
                }
                if (run >= 2) runs.Add(run);
            }
            if (runs.Count < 4) return 1;
            runs.Sort();
            int med = runs[runs.Count / 2];
            int b = 4;
            while (b * 2 <= med) b *= 2;
            return Mathf.Clamp(b, 4, 64);
        }
    }
}
