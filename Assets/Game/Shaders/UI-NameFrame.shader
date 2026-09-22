// The name frame's material: UI/Default with two things a still picture cannot do.
//
// A *sheen* — a band of light that crosses the painting, brightening each texel by its own colour
// so the gold flares gold and the outlines stay ink — and *eyes* that glow: a soft disc of warm
// light at one spot of the painting, in the painting's own aspect so it is round rather than a
// three-to-one smear. Both live in UV space, which is what makes them follow the deformation for
// free: a vertex the skin moves carries its UV with it.
//
// Everything else is UI/Default verbatim — the stencil block, the clip rect, the alpha clip —
// because this is drawn by a MaskableGraphic and has to behave under a Mask and a RectMask2D
// exactly as an Image does. Driven by `NameFrame`; listed in the always-included shaders so
// `Shader.Find` answers in a player build.
Shader "Gemfire/UI Name Frame"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _Shine ("Sheen position (-1 is none)", Float) = -1
        _ShineWidth ("Sheen width", Float) = 0.09
        _ShineStrength ("Sheen strength", Float) = 0.85
        _Glow ("Eye glow", Float) = 0
        _EyeUV ("Eye (uv)", Vector) = (0.5, 0.5, 0, 0)
        _GlowRadius ("Eye glow radius (of height)", Float) = 0.05
        _Aspect ("Texture aspect", Float) = 3

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
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;

            float _Shine, _ShineWidth, _ShineStrength;
            float _Glow, _GlowRadius, _Aspect;
            float4 _EyeUV;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 color = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;

                // The sheen leans back as it crosses, the way light does over a raised edge.
                float s = (IN.texcoord.x + (1.0 - IN.texcoord.y) * 0.22) / 1.22;
                float band = 1.0 - smoothstep(0.0, _ShineWidth, abs(s - _Shine));
                band *= step(-0.5, _Shine);
                color.rgb += color.rgb * band * _ShineStrength;

                // The eyes: a round disc in the painting's own aspect, only over painted texels.
                float2 d = (IN.texcoord - _EyeUV.xy) * float2(_Aspect, 1.0);
                float glow = exp(-dot(d, d) / (_GlowRadius * _GlowRadius)) * _Glow;
                color.rgb += float3(1.0, 0.86, 0.45) * glow * color.a;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip (color.a - 0.001);
                #endif

                return color;
            }
        ENDCG
        }
    }
}
