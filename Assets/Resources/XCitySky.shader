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
            #pragma target 3.0
            #include "UnityCG.cginc"

            fixed4 _XCitySkyHorizon;
            fixed4 _XCitySkyZenith;
            fixed4 _XCitySkyGlow;
            float4 _XCitySunDir;
            float _XCityNight;
            float _XCityQuality;
            float _XCityTime;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            inline float SkyHash(float2 p)
            {
                float h = dot(p, float2(127.1, 311.7));
                return frac(sin(h) * 43758.5453);
            }

            inline float SkyNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = SkyHash(i);
                float b = SkyHash(i + float2(1.0, 0.0));
                float c = SkyHash(i + float2(0.0, 1.0));
                float d = SkyHash(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

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
                float night = saturate(_XCityNight);

                // Smooth horizon -> zenith sky gradient.
                float zenithCurve = h > 0.0001 ? pow(h, 0.54) : 0.0;
                fixed3 col = lerp(_XCitySkyHorizon.rgb, _XCitySkyZenith.rgb, zenithCurve);

                // Subtle below-horizon darkening so there is no hard seam at y = 0.
                float below = saturate(-d.y * 4.5);
                col = lerp(col, _XCitySkyHorizon.rgb * 0.72, below);

                // Low (0): fast gradient-only path.
                if (_XCityQuality < 0.5)
                {
                    return fixed4(col, 1.0);
                }

                float t = max(_Time.y, _XCityTime);
                float3 sunDir = normalize(_XCitySunDir.xyz);
                float sunDot = saturate(dot(d, sunDir));
                float safeSunDot = max(sunDot, 0.0001);
                float sunAbove = smoothstep(-0.12, 0.08, sunDir.y);

                // Warm sunrise / sunset orange-pink atmospheric belt when sun is low.
                float twilight = smoothstep(-0.16, 0.06, sunDir.y) * (1.0 - smoothstep(0.14, 0.46, sunDir.y));
                float horizonBand = exp(-h * 5.2);
                float sunAzimuth = pow(safeSunDot, 2.2);
                fixed3 sunsetWarm = lerp(fixed3(0.92, 0.44, 0.50), fixed3(1.0, 0.54, 0.28), saturate(sunAzimuth * 0.85 + (1.0 - h) * 0.35));
                col = lerp(col, sunsetWarm, saturate(twilight * horizonBand * (0.42 + 0.44 * sunAzimuth)));

                // Sun atmospheric halo + stylized glowing sun disc.
                float bandMask = smoothstep(0.0, 0.08, h) * (1.0 - smoothstep(0.12, 0.62, h));
                float broadHalo = pow(safeSunDot, 4.5) * (0.35 + 0.65 * bandMask);
                float tightHalo = pow(safeSunDot, 36.0) * smoothstep(0.0, 0.04, h) * 0.72;
                float sunGlowMask = saturate((broadHalo + tightHalo) * _XCitySkyGlow.a * sunAbove);
                col = lerp(col, _XCitySkyGlow.rgb, sunGlowMask);

                float sunDisc = smoothstep(0.9986, 0.9996, sunDot) * smoothstep(0.0, 0.03, h) * sunAbove * (1.0 - night);
                fixed3 discCol = lerp(fixed3(1.0, 0.96, 0.86), fixed3(1.0, 0.74, 0.46), twilight);
                col += discCol * (sunDisc * 1.15);

                // Nighttime soft city light-pollution glow hugging the horizon (Medium+).
                if (night > 0.05)
                {
                    float cityBand = exp(-h * 11.5) * smoothstep(-0.08, 0.02, d.y);
                    float azimuthVar = 0.78 + 0.22 * sin(atan2(d.x, d.z) * 3.0 + 0.8);
                    fixed3 cityGlowCol = fixed3(0.96, 0.62, 0.32);
                    col += cityGlowCol * (cityBand * azimuthVar * night * 0.24);
                }

                // Nighttime gently twinkling pinpoint stars (Medium+).
                if (night > 0.12 && h > 0.10)
                {
                    float2 starUv = float2(atan2(d.x, d.z) * 38.0, asin(h) * 76.0);
                    float2 cellId = floor(starUv);
                    float2 cellF = frac(starUv) - 0.5;
                    float rnd = SkyHash(cellId);

                    if (rnd > 0.972)
                    {
                        float2 jitter = (float2(SkyHash(cellId + 17.3), SkyHash(cellId + 41.9)) - 0.5) * 0.55;
                        float dist = length(cellF - jitter);
                        float starPoint = smoothstep(0.24, 0.04, dist);
                        float twinkle = 0.68 + 0.32 * sin(t * (1.8 + frac(rnd * 37.0) * 2.6) + rnd * 62.8);
                        float elevFade = smoothstep(0.10, 0.38, h);
                        fixed3 starTint = lerp(fixed3(0.76, 0.86, 1.0), fixed3(1.0, 0.92, 0.78), frac(rnd * 19.0));
                        col += starTint * (starPoint * twinkle * elevFade * night * 0.72);
                    }
                }

                // Procedural wispy drifting clouds (High: 2 noise layers, Ultra: 3 noise layers).
                if (_XCityQuality >= 1.5 && d.y > 0.015)
                {
                    // Perspective sky-dome projection so clouds thin & recede smoothly toward the horizon.
                    float2 domeUv = d.xz / (d.y + 0.24);
                    float2 wind = float2(t * 0.018, t * 0.009);

                    float n1 = SkyNoise(domeUv * 0.85 + wind);
                    float n2 = SkyNoise(domeUv * 1.95 - wind * 0.65 + float2(4.7, 9.2));
                    float cloudFbm = n1 * 0.66 + n2 * 0.34;

                    if (_XCityQuality >= 2.5)
                    {
                        float n3 = SkyNoise(domeUv * 4.10 + wind * 1.35 + float2(13.1, 2.9));
                        cloudFbm = cloudFbm * 0.82 + n3 * 0.18;
                    }

                    float horizonFade = smoothstep(0.015, 0.22, d.y) * (1.0 - smoothstep(0.78, 0.99, d.y) * 0.35);
                    float cloudMask = smoothstep(0.50, 0.80, cloudFbm) * horizonFade;

                    // Stylized CS2 cloud shading: soft ivory in daytime, warm coral-gold at sunset, muted slate at night.
                    fixed3 cloudDay = lerp(fixed3(0.76, 0.82, 0.89), fixed3(0.98, 0.97, 0.95), smoothstep(0.54, 0.82, cloudFbm));
                    fixed3 cloudSunset = lerp(fixed3(0.78, 0.46, 0.52), fixed3(1.0, 0.74, 0.50), saturate(sunDot * 0.8 + 0.25));
                    fixed3 cloudNight = fixed3(0.18, 0.22, 0.32);

                    fixed3 cloudCol = lerp(cloudDay, cloudSunset, twilight * 0.85);
                    cloudCol = lerp(cloudCol, cloudNight, night);

                    // Subtle silver-lining rim around sun at Ultra.
                    if (_XCityQuality >= 2.5)
                    {
                        float rim = pow(safeSunDot, 8.0) * (1.0 - cloudMask * 0.55) * sunAbove * (1.0 - night);
                        cloudCol += _XCitySkyGlow.rgb * (rim * 0.45);
                    }

                    float cloudOpacity = cloudMask * lerp(0.52, 0.28, night);
                    col = lerp(col, cloudCol, saturate(cloudOpacity));
                }

                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
}
