Shader "XCity/Sky"
{
    Properties
    {
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _XCitySkyHorizon;
            fixed4 _XCitySkyZenith;
            fixed4 _XCitySkyGlow;
            float4 _XCitySunDir;
            float _XCityNight;
            float _XCityQuality;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = saturate(d.y);
                float zenithCurve = h > 0.0001 ? pow(h, 0.56) : 0.0;
                fixed3 col = lerp(_XCitySkyHorizon.rgb, _XCitySkyZenith.rgb, zenithCurve);

                float sunDot = saturate(dot(d, normalize(_XCitySunDir.xyz)));
                float safeSunDot = max(sunDot, 0.0001);
                float bandMask = smoothstep(0.0, 0.10, h) * (1.0 - smoothstep(0.10, 0.58, h));
                float sunHalo = pow(safeSunDot, 5.0) * bandMask + pow(safeSunDot, 28.0) * smoothstep(0.0, 0.06, h) * 0.65;
                col = lerp(col, _XCitySkyGlow.rgb, saturate(sunHalo * _XCitySkyGlow.a));

                if (_XCityNight > 0.15 && _XCityQuality >= 1.5 && h > 0.14)
                {
                    float2 starGrid = floor(float2(atan2(d.x, d.z) * 42.0, asin(h) * 84.0));
                    float rnd = frac(sin(dot(starGrid, float2(127.1, 311.7))) * 43758.5453);
                    float starMask = step(0.992, rnd) * smoothstep(0.14, 0.42, h);
                    col += fixed3(0.75, 0.84, 1.0) * (starMask * _XCityNight * 0.55);
                }

                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
}
