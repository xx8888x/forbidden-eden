// Transparent overlay for HS2 eyelashes / eye shadow: one texture channel is the alpha.
Shader "EDEN/HS2 Overlay"
{
    Properties
    {
        _MainTex ("Mask", 2D) = "white" {}
        _Color ("Colour", Color) = (0.12,0.10,0.10,1)
        [Enum(R,0,G,1,B,2,A,3)] _Channel ("Alpha channel", Float) = 0
        _Strength ("Strength", Range(0,2)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 200
        Cull Off
        ZWrite Off
        CGPROGRAM
        #pragma surface surf Lambert alpha:fade
        sampler2D _MainTex;
        fixed4 _Color;
        half _Channel, _Strength;
        struct Input { float2 uv_MainTex; };
        void surf (Input IN, inout SurfaceOutput o)
        {
            fixed4 t = tex2D(_MainTex, IN.uv_MainTex);
            fixed a = _Channel < 0.5 ? t.r : (_Channel < 1.5 ? t.g : (_Channel < 2.5 ? t.b : t.a));
            o.Albedo = _Color.rgb;
            o.Alpha = saturate(a * _Strength * _Color.a);
        }
        ENDCG
    }
    FallBack "Transparent/Diffuse"
}
