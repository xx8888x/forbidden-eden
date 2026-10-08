// Eye for exported HS2 characters. HS2 iris textures are channel masks:
// B = iris disc, R = the lighter lower half. Colours come from the material.
Shader "EDEN/HS2 Eye"
{
    Properties
    {
        _MainTex ("Sclera", 2D) = "white" {}
        _IrisTex ("Iris mask (B=iris, R=lower)", 2D) = "black" {}
        _IrisColor ("Iris", Color) = (0.30,0.22,0.16,1)
        _IrisLower ("Iris lower", Color) = (0.62,0.46,0.30,1)
        _HighTex ("Highlight", 2D) = "black" {}
        _HighStrength ("Highlight strength", Range(0,2)) = 0.8
        _Glossiness ("Smoothness", Range(0,1)) = 0.85
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        sampler2D _MainTex, _IrisTex, _HighTex;
        fixed4 _IrisColor, _IrisLower;
        half _HighStrength, _Glossiness;
        struct Input { float2 uv_MainTex; };
        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed3 sclera = tex2D(_MainTex, IN.uv_MainTex).rgb;
            fixed4 m = tex2D(_IrisTex, IN.uv_MainTex);
            fixed3 iris = lerp(_IrisColor.rgb, _IrisLower.rgb, saturate(m.r));
            o.Albedo = lerp(sclera, iris, saturate(m.b * 1.4));
            o.Emission = tex2D(_HighTex, IN.uv_MainTex).rgb * _HighStrength;
            o.Smoothness = _Glossiness;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
