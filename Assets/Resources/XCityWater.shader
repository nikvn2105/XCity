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
            #include "AutoLight.cginc"

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
                SHADOW_COORDS(2)
            };

            // Cheap procedural hash/value noise for organic shoreline foam & night-light shimmer (no extra textures).
            inline float WaterHash(float2 p)
            {
                float h = dot(p, float2(127.1, 311.7));
                return frac(sin(h) * 43758.5453);
            }

            inline float WaterNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = WaterHash(i);
                float b = WaterHash(i + float2(1.0, 0.0));
                float c = WaterHash(i + float2(0.0, 1.0));
                float d = WaterHash(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

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
                TRANSFER_SHADOW(o);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed3 deepCol = _Color.rgb;
                float night = saturate(_XCityNight);

                // Low (0): ultra-fast flat path, no texture sample, no wave math.
                if (_XCityQuality < 0.5)
                {
                    fixed3 col = deepCol * lerp(1.0, 0.62, night) * i.vertexLit;
                    UNITY_APPLY_FOG(i.fogCoord, col);
                    return fixed4(col, 1.0);
                }

                float2 shoreUv = i.worldPos.xz * (1.0 / 2048.0);
                float shore = tex2D(_XCityShoreTex, shoreUv).r;
                fixed3 shallowCol = _XCityWaterShallow.rgb;

                // Medium (1): smooth shallow-to-deep ramp, soft shore edge, cheap Fresnel sky tint, no wave trig.
                if (_XCityQuality < 1.5)
                {
                    float shallowMask = smoothstep(0.05, 0.90, shore);
                    fixed3 baseCol = lerp(deepCol, shallowCol, shallowMask);
                    float softFoam = smoothstep(0.87, 0.985, shore) * (1.0 - night * 0.60);
                    baseCol = lerp(baseCol, fixed3(0.86, 0.92, 0.94), softFoam * 0.45);

                    float shadowMed = SHADOW_ATTENUATION(i);
                    fixed3 litMed = baseCol * lerp(1.0, 0.65, night) * (i.vertexLit * lerp(0.75, 1.0, shadowMed));

                    float3 viewDirMed = normalize(_WorldSpaceCameraPos.xyz - i.worldPos);
                    float fresMed = pow(max(1.0 - saturate(viewDirMed.y), 0.0001), 4.0);
                    fixed3 skyMed = lerp(_XCitySkyZenith.rgb, _XCitySkyHorizon.rgb, 0.68);
                    litMed = lerp(litMed, skyMed, fresMed * lerp(0.38, 0.24, night));

                    UNITY_APPLY_FOG(i.fogCoord, litMed);
                    return fixed4(litMed, 1.0);
                }

                // High (2) & Ultra (3): CS2-style stylized aerial water.
                float t = max(_Time.y, _XCityTime);
                float3 viewVec = _WorldSpaceCameraPos.xyz - i.worldPos;
                float camDist = length(viewVec);
                float3 viewDir = viewVec / max(camDist, 0.0001);
                float waveFade = 1.0 - smoothstep(65.0, 240.0, camDist);
                float detailFade = 1.0 - smoothstep(35.0, 150.0, camDist);

                float2 xz = i.worldPos.xz;

                // 2 gentle non-collinear wave layers at High (smooth organic motion, zero grid tiling).
                float warp = sin(xz.x * 0.14 - xz.y * 0.11 + t * 0.45) * 0.55;
                float p1 = xz.x * 0.56 + xz.y * 0.34 + warp + t * 1.15;
                float p2 = xz.x * -0.44 + xz.y * 0.78 - warp * 0.7 - t * 0.92;

                float dHx = cos(p1) * 0.022 + cos(p2) * -0.017;
                float dHz = cos(p1) * 0.013 + cos(p2) * 0.028;
                float waveH = sin(p1) * 0.56 + sin(p2) * 0.44;

                // Ultra (3): subtle micro-ripple layer for crisp sunlight & night-light sparkles.
                if (_XCityQuality >= 2.5)
                {
                    float p3 = xz.x * 1.48 - xz.y * 1.24 + t * 1.85;
                    dHx += cos(p3) * 0.016 * detailFade;
                    dHz += cos(p3) * -0.014 * detailFade;
                    waveH = waveH * 0.80 + sin(p3) * 0.20 * detailFade;
                }

                // Calm slightly near very shallow shoreline.
                float calmShore = lerp(1.0, 0.65, smoothstep(0.65, 0.98, shore));
                float3 n = normalize(float3(dHx * waveFade * calmShore, 1.0, dHz * waveFade * calmShore));

                // Smooth 3-stop depth color transition (deep -> mid teal -> shallow).
                float midMask = smoothstep(0.04, 0.52, shore);
                float shallowMask = smoothstep(0.42, 0.94, shore);
                fixed3 midCol = lerp(deepCol, shallowCol, 0.42) + fixed3(0.0, 0.02, 0.02);
                fixed3 albedo = lerp(deepCol, midCol, midMask);
                albedo = lerp(albedo, shallowCol, shallowMask);

                // Subtle sunlit crest lift during daytime; calm darker slate tone at night.
                float dayFactor = 1.0 - night;
                float crest = smoothstep(0.35, 0.95, waveH) * waveFade * dayFactor;
                albedo += fixed3(0.045, 0.075, 0.085) * crest;
                albedo = lerp(albedo, albedo * fixed3(0.46, 0.52, 0.64), night);

                // Soft organic shoreline foam (primary edge + delicate lapping secondary ripple).
                float foamNoise = WaterNoise(xz * 0.42 + float2(t * 0.14, -t * 0.11));
                float edgeFoam = smoothstep(0.84, 0.965, shore + (foamNoise - 0.5) * 0.09 + waveH * 0.022 * waveFade);
                float lapWave = sin(shore * 26.0 - t * 2.0 + foamNoise * 3.14);
                float lapBand = smoothstep(0.60, 0.84, shore) * (1.0 - smoothstep(0.84, 0.95, shore));
                float lapFoam = smoothstep(0.45, 0.92, lapWave) * lapBand * 0.36 * waveFade;
                float foamMask = saturate(edgeFoam * 0.68 + lapFoam) * (1.0 - night * 0.62);
                fixed3 foamCol = lerp(fixed3(0.88, 0.94, 0.96), fixed3(0.48, 0.56, 0.68), night);
                albedo = lerp(albedo, foamCol, foamMask);

                // Lighting with Built-in directional shadows + spherical harmonics ambient.
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float ndotl = saturate(dot(n, lightDir));
                float shadow = SHADOW_ATTENUATION(i);
                fixed3 ambient = ShadeSH9(float4(n, 1.0));
                fixed3 lit = albedo * (ambient + _LightColor0.rgb * (ndotl * lerp(0.55, 1.0, shadow)));

                // Grazing-angle Fresnel sky reflection using reflected sky elevation.
                float ndotv = saturate(dot(n, viewDir));
                float fresnel = 0.03 + 0.97 * pow(max(1.0 - ndotv, 0.0001), 4.0);
                float3 reflDir = reflect(-viewDir, n);
                float skyGrad = saturate(reflDir.y * 1.35);
                fixed3 skyReflect = lerp(_XCitySkyHorizon.rgb, _XCitySkyZenith.rgb, skyGrad);
                float reflStrength = fresnel * lerp(0.54, 0.34, night) * (1.0 - foamMask * 0.72);
                lit = lerp(lit, skyReflect, saturate(reflStrength));

                // Daytime sun sparkle (soft sheen + crisp glints, masked by shadows & foam).
                float3 halfVec = normalize(lightDir + viewDir);
                float ndoth = saturate(dot(n, halfVec));
                float softSheen = pow(max(ndoth, 0.0001), 28.0) * 0.08;
                float sparklePow = _XCityQuality >= 2.5 ? 160.0 : 88.0;
                float sunSparkle = pow(max(ndoth, 0.0001), sparklePow) * (_XCityQuality >= 2.5 ? 0.42 : 0.28);
                lit += _LightColor0.rgb * ((softSheen + sunSparkle) * waveFade * dayFactor * shadow * (1.0 - foamMask));

                // Nighttime warm coastal/river city-light reflection shimmer.
                if (night > 0.01)
                {
                    float coastGlow = smoothstep(0.16, 0.86, shore) * (1.0 - foamMask * 0.6);
                    float shimmer = WaterNoise(xz * 0.55 + n.xz * 4.5 + float2(0.0, t * 0.32));
                    float glint = smoothstep(0.58, 0.94, shimmer) * waveFade;
                    fixed3 cityWarm = lerp(fixed3(0.95, 0.68, 0.36), fixed3(1.0, 0.86, 0.62), glint);
                    float nightRefl = (coastGlow * 0.14 + coastGlow * glint * 0.30 + fresnel * glint * 0.10) * night;
                    lit += cityWarm * nightRefl;
                }

                // Far horizon atmospheric blend + Built-in fog.
                float farBlend = smoothstep(3100.0, 4650.0, camDist);
                lit = lerp(lit, _XCitySkyHorizon.rgb, farBlend);

                UNITY_APPLY_FOG(i.fogCoord, lit);
                return fixed4(lit, 1.0);
            }
            ENDCG
        }
    }
}
