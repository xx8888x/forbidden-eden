// Skin for exported HS2 characters: the base skin texture, an eyebrow layer on UV2
// (HS2 stores the brow as a luminance mask), and an optional normal map.
Shader "EDEN/HS2 Skin"
{
    Properties
    {
        _MainTex ("Skin", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [Normal] _BumpMap ("Normal", 2D) = "bump" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.32
        _BrowTex ("Eyebrow mask (UV2)", 2D) = "black" {}
        _BrowColor ("Eyebrow color", Color) = (0.22,0.16,0.13,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        sampler2D _MainTex, _BumpMap, _BrowTex;
        fixed4 _Color, _BrowColor;
        half _Glossiness;
        struct Input
        {
            float2 uv_MainTex;
            float2 uv2_BrowTex;
        };
        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            fixed brow = tex2D(_BrowTex, IN.uv2_BrowTex).r * _BrowColor.a;
            o.Albedo = lerp(c.rgb, _BrowColor.rgb, saturate(brow));
            o.Normal = UnpackNormal(tex2D(_BumpMap, IN.uv_MainTex));
            o.Smoothness = _Glossiness;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
