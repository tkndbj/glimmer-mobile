Shader "GlimmerGrove/Shuffle Flipbook"
{
    Properties
    {
        [PerRendererData] _MainTex ("Halo", 2D) = "white" {}
        _Frames ("Original frames", 2DArray) = "" {}
        _FrameRects ("Original sprite rectangles", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Tint ("Frame tint", Color) = (1,1,1,1)
        _HaloTint ("Halo tint", Color) = (0,0,0,0)
        _FrameCount ("Frame count", Float) = 1
        _BookPhase ("Frame phase", Float) = 0
        _HaloSize ("Halo size", Float) = 1
        _AnchorOffset ("Anchor", Float) = 0
        _Size ("Frame size", Vector) = (1,1,0,0)
        _Flying ("Flight enabled", Float) = 0
        _Progress ("Flight phase", Float) = -1
        _Travel ("Travel and scale", Vector) = (0,0,1,0)
        _Crop ("Trail crop", Vector) = (0,0,1,0)
        _Curve ("Boss trajectory", Vector) = (0,0,0,0)
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
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma require 2darray
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 layer : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct varyings
            {
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                float3 uv : TEXCOORD0;
                float4 mask : TEXCOORD1;
                float layer : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            UNITY_DECLARE_TEX2DARRAY(_Frames);
            sampler2D _MainTex, _FrameRects;
            float4 _Color, _Tint, _HaloTint, _Size, _Travel, _Crop, _Curve, _ClipRect, _TextureSampleAdd;
            float _FrameCount, _BookPhase, _HaloSize, _AnchorOffset, _Flying, _Progress;
            float _UIMaskSoftnessX, _UIMaskSoftnessY;
            float4x4 _FxLocalToCanvas;
            varyings vert(appdata v)
            {
                varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float frame = fmod(floor(_BookPhase), _FrameCount);
                float x = (frame+.5)/_FrameCount;
                float4 uv = tex2Dlod(_FrameRects, float4(x, .25, 0, 0));
                float4 drawing = tex2Dlod(_FrameRects, float4(x, .75, 0, 0));
                float2 corner = v.uv;
                bool flying = _Flying > .5 && _Progress >= 0;
                float t = saturate(_Progress);
                if (flying && _Flying < 1.5 && v.layer.x > .5)
                {
                    // Image.Type.Filled, vertical, origin at the head (top).
                    float fill = saturate(_Crop.y+_Crop.x*t);
                    corner.y = 1-(1-corner.y)*fill;
                }
                float2 local = lerp(drawing.xy, drawing.zw, corner)*_Size.xy;
                float4 tint = _Tint;
                if (v.layer.x < .5)
                {
                    local = (v.uv-.5)*_HaloSize - float2(0, _AnchorOffset);
                    uv = float4(0,0,1,1);
                    corner = v.uv;
                    tint = _HaloTint;
                }
                if (flying)
                {
                    local += float2(0, _AnchorOffset);
                    local *= _Flying < 1.5 ? lerp(.86, 1.12, t)*_Travel.z : lerp(.8, 1.25, t);
                    local += _Travel.xy*t - float2(0, _AnchorOffset);
                    if (_Flying > 1.5)
                    {
                        local += _Curve.xy*_Curve.z*1.4*sin(t*UNITY_PI);
                        local += _Curve.xy*.55*sin(t*UNITY_PI*2)*(1-t)*_Curve.w;
                    }
                }
                o.color = round(saturate(tint)*255)/255 * v.color * _Color;
                if (o.color.a <= 0) local = 0;
                float4 canvasPosition = mul(_FxLocalToCanvas, float4(local,0,1));
                o.vertex = UnityObjectToClipPos(canvasPosition);
                o.uv = float3(lerp(uv.xy, uv.zw, corner), frame);
                o.layer = v.layer.x;
                float2 pixelSize = o.vertex.w / abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float4 rect = clamp(_ClipRect,-2e10,2e10);
                o.mask = float4(canvasPosition.xy*2-rect.xy-rect.zw,.25/(.25*float2(_UIMaskSoftnessX,_UIMaskSoftnessY)+abs(pixelSize)));
                return o;
            }
            fixed4 frag(varyings i) : SV_Target
            {
                half4 sample = i.layer < .5 ? tex2D(_MainTex,i.uv.xy) : UNITY_SAMPLE_TEX2DARRAY(_Frames,i.uv);
                half4 color = (sample+_TextureSampleAdd)*i.color;
                #ifdef UNITY_UI_CLIP_RECT
                half2 m = saturate((_ClipRect.zw-_ClipRect.xy-abs(i.mask.xy))*i.mask.zw);
                color.a *= m.x*m.y;
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a-.001);
                #endif
                color.rgb *= color.a;
                return color;
            }
            ENDCG
        }
    }
}
