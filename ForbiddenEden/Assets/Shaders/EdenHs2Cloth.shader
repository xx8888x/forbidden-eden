// Clothes and accessories for exported HS2 parts, recoloured at runtime like HS2's own clothes shader:
// the _ColorMask (HS2 "_mc") picks the colour per texel — black → colour 1, R → 2, G → 3, B → 4 — and the
// colour multiplies the (mostly grey) main texture. Without a mask the whole part takes colour 1.
Shader "EDEN/HS2 Cloth"
{
    Properties
    {
        _MainTex ("Main", 2D) = "white" {}
        _ColorMask ("Colour mask (_mc)", 2D) = "black" {}
        _Color ("Colour 1", Color) = (1,1,1,1)
        _Color2 ("Colour 2 (mask R)", Color) = (1,1,1,1)
        _Color3 ("Colour 3 (mask G)", Color) = (1,1,1,1)
        _Color4 ("Colour 4 (mask B)", Color) = (1,1,1,1)
        [Normal] _BumpMap ("Normal", 2D) = "bump" {}
        _OcclusionMap ("Occlusion", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.25
        _Metallic ("Metallic", Range(0,1)) = 0
        _Cutoff ("Alpha cutoff (0 = opaque)", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        LOD 300
        Cull Off
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow alphatest:_Cutoff
        #pragma target 3.0
        sampler2D _MainTex, _ColorMask, _BumpMap, _OcclusionMap;
        fixed4 _Color, _Color2, _Color3, _Color4;
        half _Glossiness, _Metallic;
        struct Input { float2 uv_MainTex; };
        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 t = tex2D(_MainTex, IN.uv_MainTex);
            fixed3 m = tex2D(_ColorMask, IN.uv_MainTex).rgb;
            fixed3 c = _Color.rgb;
            c = lerp(c, _Color2.rgb, m.r);
            c = lerp(c, _Color3.rgb, m.g);
            c = lerp(c, _Color4.rgb, m.b);
            o.Albedo = t.rgb * c;
            o.Normal = UnpackNormal(tex2D(_BumpMap, IN.uv_MainTex));
            o.Occlusion = tex2D(_OcclusionMap, IN.uv_MainTex).r;
            o.Smoothness = _Glossiness;
            o.Metallic = _Metallic;
            o.Alpha = t.a;
        }
        ENDCG
    }
    FallBack "Legacy Shaders/Transparent/Cutout/Diffuse"
}
