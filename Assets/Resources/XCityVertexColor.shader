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
        #pragma surface surf Lambert vertex:vert addshadow
        fixed4 _Color;
        float _XCityTime;

        struct Input
        {
            float4 color : COLOR;
        };

        void vert(inout appdata_full v)
        {
            float a = v.color.a;
            if (a > 0.02 && a < 0.995)
            {
                float sway = 1.0 - a;
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                float phase = wp.x * 0.45 + wp.z * 0.37;
                float gust = sin(_XCityTime * 2.2 + phase) * 0.12 + sin(_XCityTime * 3.9 + phase * 1.7) * 0.045;
                float3 worldOffset = float3(gust * sway, -abs(gust) * 0.16 * sway, gust * 0.65 * sway);
                v.vertex.xyz += mul((float3x3)unity_WorldToObject, worldOffset);
            }
        }

        void surf(Input IN, inout SurfaceOutput output)
        {
            output.Albedo = IN.color.rgb * _Color.rgb;
            output.Alpha = 1;
        }
        ENDCG
    }
}
