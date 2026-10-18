// 昼夜混合地球：白天卫星图 + NASA 夜光图（含城市灯光），按 UTC 太阳直射点逐像素混合。
// 夜区不完全变黑——陆地轮廓保留少量日间纹理，城市灯光在暗处发亮。
Shader "Hidden/AirlineCEO/EarthDayNight"
{
    Properties
    {
        _MainTex ("Day Texture", 2D) = "white" {}
        _NightTex ("Night Texture", 2D) = "black" {}
        _DayTint ("Day Tint", Color) = (0.66, 0.73, 0.84, 1)
        _SunLon ("Sun Longitude (deg)", Float) = 0
        _SunDec ("Sun Declination (deg)", Float) = 0
        _MipBias ("Mip Bias", Float) = -1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Blend Off
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _NightTex;
            fixed4 _DayTint;
            float _SunLon;
            float _SunDec;
            float _MipBias;
            float4 _MainTex_ST;

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // uv 0..1 → 经纬度
                float lon = (i.uv.x - 0.5) * 360.0;
                float lat = (i.uv.y - 0.5) * 180.0;

                float latR = radians(lat);
                float lonR = radians(lon - _SunLon);
                float decR = radians(_SunDec);

                // 太阳天顶角余弦（球面余弦公式）
                float cosC = sin(latR) * sin(decR) + cos(latR) * cos(decR) * cos(lonR);

                // 晨昏过渡带：白天 > 0.18，夜晚 < -0.18，之间柔和渐变
                float night = smoothstep(0.18, -0.18, cosC);

                // tex2Dbias：负 bias 采更精细 mip，放大不发糊
                float4 uvBias = float4(i.uv, 0.0, _MipBias);
                fixed3 day = tex2Dbias(_MainTex, uvBias).rgb * _DayTint.rgb;
                fixed3 nightTex = tex2Dbias(_NightTex, uvBias).rgb;

                // 夜晚底色：夜光贴图（城市灯光）+ 少量日间纹理保留陆地轮廓（月光感）
                fixed3 nightCol = nightTex.rgb * 1.25 + day.rgb * 0.16;

                fixed3 col = lerp(day, nightCol, night);
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
