Shader "XCity/VertexColor"
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
            float4 color : COLOR;
        };

        void surf(Input IN, inout SurfaceOutput output)
        {
            output.Albedo = IN.color.rgb * _Color.rgb;
            output.Alpha = 1;
        }
        ENDCG
    }
}
