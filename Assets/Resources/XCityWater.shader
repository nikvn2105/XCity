Shader "XCity/Water"
{
    Properties
    {
        _Color ("Color", Color) = (0.14, 0.34, 0.48, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            fixed4 _Color;
            fixed4 _XCityWaterShallow;
            fixed4 _XCitySkyHorizon;
            fixed4 _XCitySkyZenith;
            sampler2D _XCityShoreTex;
            float _XCityQuality;
            float _XCityTime;
            float _XCityNight;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                fixed3 vertexLit : COLOR0;
                UNITY_FOG_COORDS(1)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldPos = worldPos;

                float3 worldNormal = UnityObjectToWorldNormal(v.normal);
                float ndotl = saturate(dot(worldNormal, _WorldSpaceLightPos0.xyz));
                fixed3 ambient = ShadeSH9(float4(worldNormal, 1.0));
                o.vertexLit = ambient + _LightColor0.rgb * ndotl;

                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed3 deepCol = _Color.rgb;

                // Low: flat colour fast path (strictly cheaper than XCity/World Surface Shader).
                if (_XCityQuality < 0.5)
                {
                    fixed3 col = deepCol * i.vertexLit;
                    UNITY_APPLY_FOG(i.fogCoord, col);
                    return fixed4(col, 1.0);
                }

                float2 shoreUv = i.worldPos.xz * (1.0 / 2048.0);
                float shore = tex2D(_XCityShoreTex, shoreUv).r;
                fixed3 shallowCol = _XCityWaterShallow.rgb;

                // Medium: cheap shallow-water tint with vertex lighting and zero wave trig.
                if (_XCityQuality < 1.5)
                {
                    fixed3 baseCol = lerp(deepCol, shallowCol, saturate(shore * 1.35));
                    fixed3 col = baseCol * i.vertexLit;
                    UNITY_APPLY_FOG(i.fogCoord, col);
                    return fixed4(col, 1.0);
                }

                // High & Ultra: fragment-stage animated waves, shallow-water band, surf foam and Fresnel sky reflection.
                float t = max(_Time.y, _XCityTime);
                float3 viewVec = _WorldSpaceCameraPos.xyz - i.worldPos;
                float camDist = length(viewVec);
                float3 viewDir = viewVec / max(camDist, 0.0001);
                float waveFade = 1.0 - smoothstep(55.0, 210.0, camDist);

                float2 xz = i.worldPos.xz;
                float p1 = xz.x * 0.72 + xz.y * 0.41 + t * 1.45;
                float p2 = xz.x * -0.58 + xz.y * 0.96 - t * 1.18;
                float p3 = xz.x * 0.31 - xz.y * 1.12 + t * 1.68;
                float dHx = cos(p1) * 0.026 + cos(p2) * -0.020 + cos(p3) * 0.012;
                float dHz = cos(p1) * 0.015 + cos(p2) * 0.032 + cos(p3) * -0.022;
                float waveH = sin(p1) * 0.42 + sin(p2) * 0.35 + sin(p3) * 0.23;

                if (_XCityQuality >= 2.5)
                {
                    float p4 = xz.x * 1.65 + xz.y * -1.45 + t * 2.25;
                    dHx += cos(p4) * 0.022;
                    dHz += cos(p4) * -0.019;
                    waveH = waveH * 0.78 + sin(p4) * 0.22;
                }

                float3 n = normalize(float3(dHx * waveFade, 1.0, dHz * waveFade));
                float shallowMask = smoothstep(0.08, 0.88, shore);
                fixed3 albedo = lerp(deepCol, shallowCol, shallowMask);

                float crest = smoothstep(0.45, 0.95, waveH) * waveFade * 0.03;
                albedo += fixed3(0.10, 0.14, 0.16) * crest;

                float foamMask = smoothstep(0.86, 0.97, shore + waveH * 0.035 * waveFade) * (1.0 - _XCityNight * 0.55);
                albedo = lerp(albedo, fixed3(0.80, 0.88, 0.90), foamMask * 0.58);

                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float ndotl = saturate(dot(n, lightDir));
                fixed3 lit = albedo * (ShadeSH9(float4(n, 1.0)) + _LightColor0.rgb * ndotl);

                float fresnel = pow(max(1.0 - saturate(dot(n, viewDir)), 0.0001), 3.8);
                fixed3 skyReflect = lerp(_XCitySkyZenith.rgb, _XCitySkyHorizon.rgb, 0.65);
                lit = lerp(lit, skyReflect, fresnel * (0.42 + 0.12 * waveFade));

                float3 halfVec = normalize(lightDir + viewDir);
                float specPower = _XCityQuality >= 2.5 ? 112.0 : 64.0;
                float spec = pow(max(saturate(dot(n, halfVec)), 0.0001), specPower) * waveFade;
                lit += _LightColor0.rgb * (spec * 0.26);

                float farBlend = smoothstep(3100.0, 4650.0, camDist);
                lit = lerp(lit, _XCitySkyHorizon.rgb, farBlend);

                UNITY_APPLY_FOG(i.fogCoord, lit);
                return fixed4(lit, 1.0);
            }
            ENDCG
        }
    }
}
