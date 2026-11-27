Shader "XCity/World"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf Lambert addshadow
        fixed4 _Color;

        struct Input
        {
            float3 worldPos;
        };

        void surf(Input input, inout SurfaceOutput output)
        {
            output.Albedo = _Color.rgb;
            output.Alpha = 1;
        }
        ENDCG
    }
}
