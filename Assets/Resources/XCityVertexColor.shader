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
        #pragma target 3.0
        #pragma multi_compile_local _ XCITY_ARCHITECTURE
        fixed4 _Color;
        float _XCityTime;
        float _XCityNight;
        float _XCityInfoView;

        struct Input
        {
            float4 color : COLOR;
            float3 worldPos;
            #if defined(XCITY_ARCHITECTURE)
            float3 worldNormal;
            float surfaceType;
            #endif
        };

        void vert(inout appdata_full v, out Input data)
        {
            UNITY_INITIALIZE_OUTPUT(Input, data);
            #if defined(XCITY_ARCHITECTURE)
            data.surfaceType = v.texcoord1.x;
            #endif
            float a = v.color.a;
            if (a > 0.05 && a < 0.995)
            {
                float sway = 1.0 - a;
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                float phase = wp.x * 0.45 + wp.z * 0.37;
                float gust = sin(_XCityTime * 2.2 + phase) * 0.12 + sin(_XCityTime * 3.9 + phase * 1.7) * 0.045;
                float3 worldOffset = float3(gust * sway, -abs(gust) * 0.16 * sway, gust * 0.65 * sway);
                v.vertex.xyz += mul((float3x3)unity_WorldToObject, worldOffset);
            }
        }

        #if defined(XCITY_ARCHITECTURE)
        // Let grout and roof seams disappear when a course projects to less than a pixel.
        float Joint(float coordinate, float footprint, float halfWidth)
        {
            float distanceToJoint = abs(frac(coordinate + 0.5) - 0.5);
            float edge = max(footprint, 0.006);
            return (1.0 - smoothstep(halfWidth, halfWidth + edge, distanceToJoint))
                * (1.0 - smoothstep(0.12, 0.55, footprint));
        }

        fixed3 BuildingFinish(Input surface, fixed3 albedo)
        {
            float3 position = surface.worldPos;
            float3 normal = normalize(surface.worldNormal);

            // Contact AO near lot pads (y = 0.14), inner corners, and setback junctions.
            float hAboveGround = max(0.0, position.y - 0.14);
            float2 tileLocal = frac(position.xz * 0.25) * 4.0 - 2.0;
            float wallMask = 1.0 - saturate(abs(normal.y));
            float groundAO = 1.0
                - 0.30 * (1.0 - smoothstep(0.0, 0.32, hAboveGround))
                - 0.12 * (1.0 - smoothstep(0.0, 1.35, hAboveGround));
            float outward = dot(normal.xz, tileLocal);
            float innerCornerAO = lerp(0.78, 1.0, smoothstep(-0.30, 0.95, outward));
            float footprintDist = max(abs(tileLocal.x), abs(tileLocal.y));
            float lotContactAO = 1.0 - 0.24 * (1.0 - smoothstep(0.85, 1.62, footprintDist))
                * (1.0 - smoothstep(0.02, 0.18, hAboveGround));
            float podiumCreaseAO = 1.0 - 0.16 * (1.0 - smoothstep(0.60, 1.25, footprintDist))
                * smoothstep(0.20, 0.60, hAboveGround) * (1.0 - smoothstep(3.8, 4.6, hAboveGround));
            albedo *= lerp(lotContactAO * podiumCreaseAO, groundAO * innerCornerAO, wallMask);

            float wallU = abs(normal.x) > abs(normal.z) ? position.z : position.x;
            float row = position.y * 7.8;
            float course = wallU * 4.4 + fmod(floor(row), 2.0) * 0.5;
            float2 roofUV = position.xz * 4.6;
            float slopeZ = step(abs(normal.x), abs(normal.z));
            float roofRow = lerp(roofUV.x, roofUV.y, slopeZ);
            float roofColumn = lerp(roofUV.y, roofUV.x, slopeZ);
            float rowFootprint = fwidth(row);
            float columnFootprint = fwidth(wallU) * 4.4;
            float panelFootprint = fwidth(wallU) * 2.0;
            float roofFootprint = max(fwidth(roofUV.x), fwidth(roofUV.y));

            if (surface.surfaceType < 0.5) return albedo;
            if (surface.surfaceType < 1.5)
            {
                float mortar = max(Joint(row, rowFootprint, 0.032), Joint(course, columnFootprint, 0.023));
                float variation = frac(floor(course) * 0.381 + floor(row) * 0.617) - 0.5;
                float detail = 1.0 - smoothstep(0.18, 0.55, max(rowFootprint, columnFootprint));
                return albedo * (1.0 + variation * 0.11 * detail - mortar * 0.085);
            }
            if (surface.surfaceType < 2.5)
            {
                float seam = Joint(roofRow, roofFootprint, 0.035);
                float join = Joint(roofColumn * 0.5 + floor(roofRow) * 0.5, roofFootprint * 0.5, 0.018);
                float tile = frac(roofRow);
                return albedo * (1.0 - max(seam, join) * 0.21 + tile * 0.055 *
                    (1.0 - smoothstep(0.12, 0.55, roofFootprint)));
            }

            float3 view = normalize(_WorldSpaceCameraPos.xyz - position);
            float facing = 1.0 - saturate(dot(normal, view));
            float horizon = saturate(reflect(-view, normal).y * 1.45 + 0.48);
            fixed3 reflectedSky = lerp(fixed3(0.22, 0.29, 0.32), fixed3(0.58, 0.70, 0.77), horizon);
            if (surface.surfaceType < 3.5)
                return lerp(albedo, reflectedSky, 0.19 + 0.49 * facing * facing);

            float seamPanel = Joint(wallU * 2.0, panelFootprint, 0.025);
            return lerp(albedo, reflectedSky, 0.17 + 0.32 * facing * facing) * (1.0 - seamPanel * 0.16);
        }
        #endif

        void surf(Input IN, inout SurfaceOutput output)
        {
            fixed3 albedo = IN.color.rgb * _Color.rgb;
            #if defined(XCITY_ARCHITECTURE)
            albedo = BuildingFinish(IN, albedo);
            // Info views: pale grey buildings so the overlay colours read clearly.
            albedo = lerp(albedo, fixed3(0.80, 0.82, 0.85), _XCityInfoView * 0.85);
            #endif

            fixed3 emission = fixed3(0, 0, 0);
            if (IN.color.a < 0.005)
            {
                emission = IN.color.rgb * lerp(0.12, 1.35, _XCityNight);
            }
            else if (IN.color.a < 0.025)
            {
                emission = IN.color.rgb * 1.45;
            }
            #if defined(XCITY_ARCHITECTURE)
            if (_XCityNight > 0.01 && IN.surfaceType > 2.5 && IN.surfaceType < 3.5)
            {
                float3 room = floor(IN.worldPos * float3(1.55, 1.28, 1.55));
                float h = frac(sin(dot(room, float3(12.9898, 78.233, 37.719))) * 43758.5453);
                float lit = step(0.30, h) * smoothstep(0.05 + h * 0.35, 0.50 + h * 0.35, _XCityNight);
                fixed3 winColor = lerp(fixed3(1.0, 0.78, 0.44), fixed3(0.76, 0.91, 1.0), step(0.78, h));
                emission += winColor * (lit * 0.96);
            }
            #endif

            output.Albedo = albedo;
            output.Emission = emission;
            output.Alpha = 1;
        }
        ENDCG
    }
}
