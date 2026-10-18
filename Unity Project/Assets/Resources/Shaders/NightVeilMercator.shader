// 夜幕 + 晨昏边：同一 cosC，阴影边界即晨昏线（不再另画折线）。
Shader "Hidden/AirlineCEO/NightVeilMercator"
{
    Properties
    {
        _NightColor ("Night Color", Color) = (0.008, 0.024, 0.094, 1)
        _LineColor ("Terminator Color", Color) = (0.55, 0.55, 0.57, 0.92)
        _SunLonDec ("Sun Lon/Dec (deg)", Vector) = (0, 0, 0, 0)
        _MaxAlpha ("Max Night Alpha", Float) = 0.52
        _UvPerPx ("UV per screen pixel", Vector) = (0.0005, 0.0009, 0, 0)
        _EdgePx ("Edge width (px)", Float) = 2
        _DashPx ("Dash period (px)", Float) = 9
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        _ClipRect ("Clip Rect", Vector) = (-32767, -32767, 32767, 32767)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            fixed4 _NightColor;
            fixed4 _LineColor;
            float4 _SunLonDec;
            float _MaxAlpha;
            float4 _UvPerPx;
            float _EdgePx;
            float _DashPx;
            float4 _ClipRect;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.worldPosition = v.vertex;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float lon = (i.uv.x - 0.5) * 360.0;
                float mercN = (i.uv.y - 0.5) * UNITY_TWO_PI;
                float lat = degrees(atan(sinh(mercN)));

                float latR = radians(lat);
                float lonR = radians(lon - _SunLonDec.x);
                float decR = radians(_SunLonDec.y);
                float sl = sin(latR);
                float cl = cos(latR);
                float sd = sin(decR);
                float cd = cos(decR);
                float cL = cos(lonR);
                float sL = sin(lonR);
                float cosC = sl * sd + cl * cd * cL;

                float dLatR_dv = UNITY_TWO_PI / max(cosh(mercN), 1e-4);
                float dCos_du = (-cl * cd * sL) * UNITY_TWO_PI;
                float dCos_dv = (cl * sd - sl * cd * cL) * dLatR_dv;
                float2 uvPx = max(_UvPerPx.xy, 1e-6);
                float dCosPx = length(float2(dCos_du * uvPx.x, dCos_dv * uvPx.y));
                dCosPx = max(dCosPx, 1e-5);

                float night = 1.0 - smoothstep(-dCosPx, dCosPx, cosC);

                // 屏幕空间圆点：到晨昏线的像素距离 × 沿线弧长，半径/间距不随缩放变。
                float2 pPx = float2(i.uv.x / uvPx.x, i.uv.y / uvPx.y);
                float2 tPx = float2(-dCos_dv / uvPx.y, dCos_du / uvPx.x);
                tPx = tPx / max(length(tPx), 1e-6);
                float distTerm = abs(cosC) / dCosPx;
                float along = dot(pPx, tPx);
                float gap = max(_DashPx, 4.0);
                float nearest = round(along / gap) * gap;
                float dAlong = along - nearest;
                float r = max(_EdgePx, 1.4);
                float d = sqrt(dAlong * dAlong + distTerm * distTerm);
                float dotA = 1.0 - smoothstep(r - 0.7, r + 0.35, d);

                float fillA = night * _MaxAlpha;
                float lineA = dotA * _LineColor.a;
                fixed4 c;
                c.rgb = lineA > fillA * 0.35 ? _LineColor.rgb : _NightColor.rgb;
                c.a = max(fillA, lineA);
#ifdef UNITY_UI_CLIP_RECT
                c.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
#endif
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
