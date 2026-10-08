// Hair for exported HS2 characters. HS2 hair has no colour texture: a gradation mask
// (R = middle, G = top, B = ends) blends three colours, with occlusion and a normal map.
Shader "EDEN/HS2 Hair"
{
    Properties
    {
        _Color ("Middle", Color) = (0.24,0.16,0.12,1)
        _TopColor ("Top", Color) = (0.18,0.12,0.09,1)
        _UnderColor ("Ends", Color) = (0.32,0.22,0.16,1)
        _MainTex ("Gradation mask", 2D) = "red" {}
        _OcclusionTex ("Occlusion", 2D) = "white" {}
        [Normal] _BumpMap ("Normal", 2D) = "bump" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.45
        _Specular ("Sheen", Range(0,1)) = 0.25
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300
        Cull Off
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        sampler2D _MainTex, _OcclusionTex, _BumpMap;
        fixed4 _Color, _TopColor, _UnderColor;
        half _Glossiness, _Specular;
        struct Input { float2 uv_MainTex; float2 uv_BumpMap; };
        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 g = tex2D(_MainTex, IN.uv_MainTex);
            fixed3 c = _Color.rgb;
            c = lerp(c, _TopColor.rgb, saturate(g.g));
            c = lerp(c, _UnderColor.rgb, saturate(g.b));
            fixed ao = tex2D(_OcclusionTex, IN.uv_MainTex).r;
            o.Albedo = c * lerp(0.55, 1.0, ao);
            o.Normal = UnpackNormal(tex2D(_BumpMap, IN.uv_BumpMap));
            o.Smoothness = _Glossiness;
            o.Metallic = _Specular * 0.2;
            o.Occlusion = lerp(0.6, 1.0, ao);
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
