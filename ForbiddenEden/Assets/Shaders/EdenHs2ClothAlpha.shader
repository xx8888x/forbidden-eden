// Translucent parts of HS2 accessories (glasses lenses, veils): EDEN/HS2 Cloth colouring with alpha blending.
// Colour 4 tints the part and its alpha scales the opacity (HS2's "rendAlpha" colour).
Shader "EDEN/HS2 Cloth Alpha"
{
    Properties
    {
        _MainTex ("Main", 2D) = "white" {}
        _ColorMask ("Colour mask (_mc)", 2D) = "black" {}
        _Color ("Colour 1", Color) = (1,1,1,1)
        _Color2 ("Colour 2 (mask R)", Color) = (1,1,1,1)
        _Color3 ("Colour 3 (mask G)", Color) = (1,1,1,1)
        _Color4 ("Colour 4 (tint, alpha = opacity)", Color) = (1,1,1,0.35)
        _Glossiness ("Smoothness", Range(0,1)) = 0.8
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        LOD 300
        Cull Off
        ZWrite Off
        CGPROGRAM
        #pragma surface surf Standard alpha:fade
        #pragma target 3.0
        sampler2D _MainTex, _ColorMask;
        fixed4 _Color, _Color2, _Color3, _Color4;
        half _Glossiness;
        struct Input { float2 uv_MainTex; };
        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 t = tex2D(_MainTex, IN.uv_MainTex);
            o.Albedo = t.rgb * _Color4.rgb;
            o.Smoothness = _Glossiness;
            o.Alpha = saturate(t.a * _Color4.a);
        }
        ENDCG
    }
}
