using System.Collections.Generic;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.UI
{
    /// <summary>
    /// 地图上的机型图标：把 <see cref="AircraftSilhouetteGeometry"/> 算出的剪影变成 Sprite 并缓存。
    ///
    /// ★2026-10-30 CEO 要求「地图上的白三角换成执飞的机型图标」。旧版所有航班共用一张
    /// 32×32 白纸飞机（<c>EnsurePlaneSprite</c>），于是 A380 和 CRJ 在地图上一模一样 ——
    /// 地图上看不出机队结构，而这个游戏最贵的东西就是机队。
    ///
    /// 纹理 128 px：图标实际显示 26~50 px（见 <see cref="AircraftSilhouetteGeometry.IconSizePx"/>），
    /// 2.5× 余量保证在 4K 屏 + 地图放大时不糊。白色 + alpha，颜色由调用方的 Image.color 决定
    /// （现在用近白色，将来要按公司色染色不必重出图）。
    /// </summary>
    public static class MapAircraftIcons
    {
        /// <summary>纹理边长。**必须是 2 的幂**：非 2 次幂纹理在部分 GPU 上无法生成 mipmap，缩放时会闪烁。</summary>
        const int TexSize = 128;

        static readonly Dictionary<AircraftSilhouette, Sprite> _sprites =
            new Dictionary<AircraftSilhouette, Sprite>();

        /// <summary>机型代号 → 剪影。目录里没有的代号按 <see cref="AircraftSilhouetteGeometry.Classify"/> 兜底，绝不返回空。</summary>
        public static Sprite SpriteFor(string aircraftType)
            => SpriteOf(AircraftSilhouetteGeometry.Classify(aircraftType));

        /// <summary>屏幕边长（px），按翼展 26~50 缩放。</summary>
        public static float SizeFor(string aircraftType)
            => AircraftSilhouetteGeometry.IconSizePx(AircraftSilhouetteGeometry.Classify(aircraftType));

        public static Sprite SpriteOf(AircraftSilhouette kind)
        {
            if (_sprites.TryGetValue(kind, out var cached) && cached != null) return cached;

            var cov = AircraftSilhouetteGeometry.Coverage(kind, TexSize);
            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "PlaneIcon_" + kind
            };
            var px = new Color32[TexSize * TexSize];
            for (int i = 0; i < px.Length; i++)
            {
                float a = cov[i];
                px[i] = new Color32(255, 255, 255, (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);

            var sprite = Sprite.Create(tex, new Rect(0f, 0f, TexSize, TexSize), new Vector2(0.5f, 0.5f), TexSize);
            sprite.name = "PlaneIcon_" + kind;
            _sprites[kind] = sprite;
            return sprite;
        }
    }
}
