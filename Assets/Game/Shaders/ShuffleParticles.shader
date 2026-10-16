// Immutable particle records; only the shared, split-precision Tween clock changes per frame.
// UI stencil, soft rectangular clipping, inherited alpha and both existing blend modes remain.
Shader "GlimmerGrove/Shuffle Particles"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Records ("Particle records", 2D) = "black" {}
        _SrcBlend ("Source blend", Float) = 1
        _DstBlend ("Destination blend", Float) = 10
        _Premultiply ("Premultiply", Float) = 1
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend [_SrcBlend] [_DstBlend]
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 record : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct varyings
            {
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 mask : TEXCOORD1;
                float2 local : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            sampler2D _MainTex, _Records;
            float4 _Color, _TextureSampleAdd, _ClipRect;
            float4 _ShuffleFxClock;
            float4x4 _FxLocalToCanvas;
            float _UIMaskSoftnessX, _UIMaskSoftnessY, _Premultiply;

            float4 Record(float index, float row)
            {
                return tex2Dlod(_Records, float4((index + .5) / 256, (row + .5) / 5, 0, 0));
            }
            float OutQuad(float u) { return 1 - (1-u)*(1-u); }
            float OutCubic(float u) { return 1 - pow(1-u, 3); }
            float OutQuint(float u) { return 1 - pow(1-u, 5); }

            varyings vert(appdata v)
            {
                varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float4 shape = Record(v.record.x, 0); // at, size
                float4 motion = Record(v.record.x, 1); // velocity, spin, scale
                float4 time = Record(v.record.x, 2); // born high/low, duration, delay
                float4 tint = Record(v.record.x, 3);
                float4 kind = Record(v.record.x, 4); // angle, gravity, phase, motion id
                float age = ((_ShuffleFxClock.x - time.x) + (_ShuffleFxClock.y - time.y)) - time.w;
                float u = saturate(age / max(.0001, time.z));
                float2 scale = 1, at = shape.xy;
                float angle = kind.x, t;
                int mode = (int)kind.w;
                if (age <= 0)
                {
                    if (mode == 0) scale = .4;
                    if (mode == 4 || mode == 6 || mode == 7) tint.a = 0;
                    if (mode == 8) { scale = 0; tint.a = 0; }
                    if (mode == 5) tint = float4(lerp(tint.rgb, 1, .45), 1);
                }
                else
                {
                    if (mode == 0 || mode == 1)
                    { scale = lerp(mode == 0 ? .4 : 1, motion.w, OutCubic(u)); tint.a *= 1-OutQuad(u); }
                    else if (mode == 2)
                    { t = OutQuint(u); scale = lerp(.35, 1.6, t); tint.a *= 1-t; }
                    else if (mode == 3)
                    { t = OutCubic(u); at += motion.xy*t; scale = lerp(1.15, .05, t*t); angle = motion.z*t; tint.a *= 1-t*t; }
                    else if (mode == 4)
                    { t = OutQuad(u); at += motion.xy*t; scale = lerp(1, .15, t*t); tint.a *= 1-t*t; }
                    else if (mode == 5)
                    {
                        float2 velocity = float2(motion.x, motion.y - kind.y*age);
                        at += float2(motion.x*age, motion.y*age - .5*kind.y*age*age);
                        angle = atan2(velocity.y, velocity.x)*57.295779513 - 90;
                        scale = float2(lerp(1, .3, u), lerp(1, .5, u)*(1+length(velocity)/motion.w));
                        tint = float4(lerp(1, tint.rgb, u), 1-u*u);
                    }
                    else if (mode == 6)
                    { tint.a *= (u < .2 ? 1 : 1-(u-.2)/.8)*(.58+.42*abs(sin(u*34+kind.z))); }
                    else if (mode == 7)
                    {
                        t = OutQuint(u); scale = lerp(.12, 1, t);
                        float a = t < .12 ? t/.12 : 1-(t-.12)/.88;
                        tint = float4(lerp(1, tint.rgb, saturate(t*3)), a*a);
                    }
                    else if (mode == 8)
                    { t = sin(u*UNITY_PI); scale = t; angle = motion.z*u; tint.a = t; }
                    else if (mode == 9)
                    { scale = lerp(.3, 1, OutQuint(saturate(u/.3))); tint = float4(lerp(lerp(tint.rgb, 1, .6), tint.rgb, saturate(u*2)), 1-u*u); }
                    else if (mode == 10)
                    { scale = float2(lerp(.45, 1.1, OutQuint(u)), 1-u*.5); tint.a *= 1-u; }
                    else if (mode == 11)
                    { scale = lerp(.5, 1.25, OutQuad(u)); angle = 25+motion.z*u; tint.a *= 1-u; }
                    else if (mode == 12) tint.a *= 1-OutQuad(u);
                }
                // Expired slots stay in the immutable mesh until the page is recycled.
                if (age >= time.z) tint.a = 0;
                // Degenerate invisible quads instead of shading their transparent pixels.
                o.color = round(saturate(tint)*255)/255 * v.color * _Color;
                if (o.color.a <= 0) scale = 0;
                float s, c; sincos(angle * .01745329252, s, c);
                float2 local = v.record.yz * scale;
                local = at + float2(c*local.x-s*local.y, s*local.x+c*local.y);
                float4 canvasPosition = mul(_FxLocalToCanvas, float4(local, 0, 1));
                o.vertex = UnityObjectToClipPos(canvasPosition);
                o.local = canvasPosition.xy;
                o.uv = v.uv;
                // Color32 quantization precedes CanvasGroup modulation, as it does for Image.
                float2 pixelSize = o.vertex.w / abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float4 rect = clamp(_ClipRect, -2e10, 2e10);
                o.mask = float4(canvasPosition.xy*2-rect.xy-rect.zw, .25/(.25*float2(_UIMaskSoftnessX, _UIMaskSoftnessY)+abs(pixelSize)));
                return o;
            }
            fixed4 frag(varyings i) : SV_Target
            {
                half4 color = (tex2D(_MainTex, i.uv) + _TextureSampleAdd) * i.color;
                #ifdef UNITY_UI_CLIP_RECT
                // Additive's existing shader has hard clipping; UI/Default uses soft clipping.
                half2 m = saturate((_ClipRect.zw-_ClipRect.xy-abs(i.mask.xy))*i.mask.zw);
                color.a *= lerp(UnityGet2DClipping(i.local, _ClipRect), m.x*m.y, _Premultiply);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a-.001);
                #endif
                color.rgb *= lerp(1, color.a, _Premultiply);
                return color;
            }
            ENDCG
        }
    }
}
