using UnityEngine;

namespace XCity
{
    internal enum TreeSpecies
    {
        Oak,
        Spruce,
        Birch,
        Poplar
    }

    internal static class TreeMeshFactory
    {
        private static readonly Color32 OakBark = new Color32(92, 74, 58, 255);
        private static readonly Color32 OakBarkDark = new Color32(66, 53, 42, 255);
        private static readonly Color32 SpruceBark = new Color32(84, 63, 49, 255);
        private static readonly Color32 BirchBark = new Color32(214, 210, 200, 255);
        private static readonly Color32 BirchMark = new Color32(94, 88, 82, 255);
        private static readonly Color32 SoilDark = new Color32(72, 58, 45, 255);
        private static readonly Color32 PlanterStone = new Color32(162, 160, 150, 255);
        private static readonly Color32 PlanterRim = new Color32(184, 182, 173, 255);

        private static readonly Color32 OakShadow = new Color32(46, 82, 52, 255);
        private static readonly Color32 OakMid = new Color32(72, 114, 68, 255);
        private static readonly Color32 OakSun = new Color32(106, 144, 86, 255);

        private static readonly Color32 SpruceShadow = new Color32(31, 61, 56, 255);
        private static readonly Color32 SpruceMid = new Color32(46, 88, 76, 255);
        private static readonly Color32 SpruceSun = new Color32(72, 114, 92, 255);

        private static readonly Color32 BirchShadow = new Color32(76, 108, 60, 255);
        private static readonly Color32 BirchMid = new Color32(114, 148, 78, 255);
        private static readonly Color32 BirchSun = new Color32(154, 178, 98, 255);

        private static readonly Color32 PoplarShadow = new Color32(52, 92, 62, 255);
        private static readonly Color32 PoplarMid = new Color32(82, 126, 78, 255);
        private static readonly Color32 PoplarSun = new Color32(118, 154, 94, 255);

        private static readonly Color32 AutumnShadow = new Color32(118, 78, 42, 255);
        private static readonly Color32 AutumnMid = new Color32(164, 112, 52, 255);
        private static readonly Color32 AutumnSun = new Color32(198, 150, 68, 255);

        public static void AddWildTree(MeshBuilder b, Vector3 basePos, float scale, uint seed, float biomeBias)
        {
            TreeSpecies species = PickSpecies(seed, biomeBias);
            AddTreeBySpecies(b, basePos, scale, seed, species, allowAutumn: true);
        }

        public static void AddStreetTree(MeshBuilder b, Vector3 planterBase, float planterWidth, uint seed)
        {
            float h = 0.13f;
            b.AddBox(planterBase + Vector3.up * (h * 0.5f), new Vector3(planterWidth, h, planterWidth), PlanterStone, PlanterRim);
            b.AddBox(planterBase + Vector3.up * (h + 0.01f), new Vector3(planterWidth * 0.78f, 0.02f, planterWidth * 0.78f), SoilDark, SoilDark);

            Vector3 trunkBase = planterBase + Vector3.up * (h + 0.01f);
            float scale = 0.68f + Hash01(seed, 3) * 0.18f;
            bool isBirch = (seed % 5u) == 0u;
            bool isAutumn = !isBirch && (seed % 11u) == 0u;

            Color32 barkBot = isBirch ? BirchMark : OakBarkDark;
            Color32 barkTop = isBirch ? BirchBark : OakBark;

            float trunkH = 1.05f * scale;
            Vector3 lean = new Vector3((Hash01(seed, 7) - 0.5f) * 0.08f, 0f, (Hash01(seed, 11) - 0.5f) * 0.08f);
            Vector3 crownAnchor = trunkBase + new Vector3(lean.x, trunkH, lean.z);

            AddBranch(b, trunkBase, trunkBase + Vector3.up * (trunkH * 0.55f), 0.095f * scale, 0.068f * scale, 6,
                barkBot, barkTop, 0f, 0.06f);
            AddBranch(b, trunkBase + Vector3.up * (trunkH * 0.52f), crownAnchor, 0.068f * scale, 0.048f * scale, 5,
                barkTop, barkTop, 0.06f, 0.14f);

            Color32 cShadow = isAutumn ? AutumnShadow : (isBirch ? BirchShadow : OakShadow);
            Color32 cMid = isAutumn ? AutumnMid : (isBirch ? BirchMid : OakMid);
            Color32 cSun = isAutumn ? AutumnSun : (isBirch ? BirchSun : OakSun);
            ApplyTintVariation(seed, ref cShadow, ref cMid, ref cSun);

            float baseAngle = Hash01(seed, 19) * Mathf.PI * 2f;
            for (int i = 0; i < 4; i++)
            {
                float a = baseAngle + i * (Mathf.PI * 0.5f) + (Hash01(seed, 30 + i) - 0.5f) * 0.35f;
                float rOff = (0.28f + Hash01(seed, 40 + i) * 0.14f) * scale;
                float yOff = (0.14f + Hash01(seed, 50 + i) * 0.16f) * scale;
                Vector3 lobePos = crownAnchor + new Vector3(Mathf.Cos(a) * rOff, yOff, Mathf.Sin(a) * rOff);
                AddBranch(b, crownAnchor - Vector3.up * (0.08f * scale), lobePos, 0.038f * scale, 0.02f * scale, 4,
                    barkTop, barkTop, 0.12f, 0.24f);
                AddFoliageCluster(b, lobePos, (0.34f + Hash01(seed, 60 + i) * 0.10f) * scale,
                    0.82f, seed + (uint)(i * 97), cShadow, cMid, cSun, 0.22f, 0.44f);
            }

            AddFoliageCluster(b, crownAnchor + Vector3.up * (0.44f * scale), 0.39f * scale,
                0.86f, seed + 511u, cShadow, cMid, cSun, 0.34f, 0.56f);
        }

        public static void AddYardTree(MeshBuilder b, Vector3 basePos, float scale, uint seed)
        {
            TreeSpecies species = (seed % 4u) switch
            {
                0u => TreeSpecies.Birch,
                1u => TreeSpecies.Poplar,
                _ => TreeSpecies.Oak
            };
            AddTreeBySpecies(b, basePos, scale, seed, species, allowAutumn: true);
        }

        public static void AddPlanterShrub(MeshBuilder b, Vector3 foot, float width, uint seed)
        {
            float boxH = 0.22f;
            b.AddBox(foot + Vector3.up * (boxH * 0.5f), new Vector3(width, boxH, width), PlanterStone, PlanterRim);
            b.AddBox(foot + Vector3.up * (boxH + 0.01f), new Vector3(width * 0.80f, 0.02f, width * 0.80f), SoilDark, SoilDark);

            Vector3 soilTop = foot + Vector3.up * (boxH + 0.02f);
            Color32 cShadow = OakShadow;
            Color32 cMid = OakMid;
            Color32 cSun = OakSun;
            ApplyTintVariation(seed, ref cShadow, ref cMid, ref cSun);

            float r = width * 0.36f;
            AddFoliageCluster(b, soilTop + Vector3.up * (r * 0.65f), r, 0.80f, seed + 13u, cShadow, cMid, cSun, 0.08f, 0.24f);
            AddFoliageCluster(b, soilTop + new Vector3(r * 0.48f, r * 0.42f, r * 0.22f), r * 0.72f, 0.76f, seed + 37u, cShadow, cMid, cSun, 0.08f, 0.22f);
            AddFoliageCluster(b, soilTop + new Vector3(-r * 0.42f, r * 0.44f, -r * 0.26f), r * 0.74f, 0.78f, seed + 71u, cShadow, cMid, cSun, 0.08f, 0.22f);
        }

        private static TreeSpecies PickSpecies(uint seed, float biomeBias)
        {
            float roll = Hash01(seed, 1);
            if (biomeBias > 0.58f)
            {
                if (roll < 0.48f) return TreeSpecies.Spruce;
                if (roll < 0.72f) return TreeSpecies.Birch;
                if (roll < 0.88f) return TreeSpecies.Oak;
                return TreeSpecies.Poplar;
            }
            if (roll < 0.38f) return TreeSpecies.Oak;
            if (roll < 0.62f) return TreeSpecies.Spruce;
            if (roll < 0.82f) return TreeSpecies.Birch;
            return TreeSpecies.Poplar;
        }

        private static void AddTreeBySpecies(MeshBuilder b, Vector3 basePos, float scale, uint seed, TreeSpecies species, bool allowAutumn)
        {
            switch (species)
            {
                case TreeSpecies.Spruce:
                    BuildSpruce(b, basePos, scale, seed);
                    break;
                case TreeSpecies.Birch:
                    BuildBirch(b, basePos, scale, seed);
                    break;
                case TreeSpecies.Poplar:
                    BuildPoplar(b, basePos, scale, seed, allowAutumn);
                    break;
                default:
                    BuildOak(b, basePos, scale, seed, allowAutumn);
                    break;
            }
        }

        private static void BuildOak(MeshBuilder b, Vector3 basePos, float scale, uint seed, bool allowAutumn)
        {
            bool autumn = allowAutumn && (seed % 9u) == 0u;
            Color32 cShadow = autumn ? AutumnShadow : OakShadow;
            Color32 cMid = autumn ? AutumnMid : OakMid;
            Color32 cSun = autumn ? AutumnSun : OakSun;
            ApplyTintVariation(seed, ref cShadow, ref cMid, ref cSun);

            float leanX = (Hash01(seed, 5) - 0.5f) * 0.14f * scale;
            float leanZ = (Hash01(seed, 9) - 0.5f) * 0.14f * scale;

            Vector3 p0 = basePos;
            Vector3 p1 = basePos + new Vector3(leanX * 0.35f, 0.34f * scale, leanZ * 0.35f);
            Vector3 p2 = basePos + new Vector3(leanX, 0.96f * scale, leanZ);

            AddBranch(b, p0, p1, 0.17f * scale, 0.115f * scale, 6, OakBarkDark, OakBark, 0f, 0.04f);
            AddBranch(b, p1 - Vector3.up * (0.03f * scale), p2, 0.115f * scale, 0.082f * scale, 6, OakBark, OakBark, 0.04f, 0.14f);

            float baseAngle = Hash01(seed, 17) * Mathf.PI * 2f;
            for (int i = 0; i < 4; i++)
            {
                float a = baseAngle + i * (Mathf.PI * 0.5f) + (Hash01(seed, 23 + i) - 0.5f) * 0.45f;
                float reach = (0.42f + Hash01(seed, 31 + i) * 0.22f) * scale;
                float rise = (0.34f + Hash01(seed, 41 + i) * 0.24f) * scale;
                Vector3 limbTip = p2 + new Vector3(Mathf.Cos(a) * reach, rise, Mathf.Sin(a) * reach);

                AddBranch(b, p2 - Vector3.up * (0.08f * scale), limbTip, 0.062f * scale, 0.032f * scale, 5,
                    OakBark, OakBarkDark, 0.12f, 0.28f);

                float lobeRadius = (0.44f + Hash01(seed, 53 + i) * 0.14f) * scale;
                AddFoliageCluster(b, limbTip + Vector3.up * (0.08f * scale), lobeRadius, 0.78f,
                    seed + (uint)(i * 131 + 17), cShadow, cMid, cSun, 0.24f, 0.52f);
            }

            Vector3 crownTop = p2 + new Vector3(-leanX * 0.3f, 0.68f * scale, -leanZ * 0.3f);
            AddFoliageCluster(b, crownTop, 0.54f * scale, 0.84f, seed + 613u, cShadow, cMid, cSun, 0.38f, 0.66f);

            float topAngle = baseAngle + 1.1f;
            Vector3 subPeak = crownTop + new Vector3(Mathf.Cos(topAngle) * 0.26f * scale, 0.18f * scale, Mathf.Sin(topAngle) * 0.26f * scale);
            AddFoliageCluster(b, subPeak, 0.38f * scale, 0.80f, seed + 827u, cShadow, cMid, cSun, 0.46f, 0.70f);
        }

        private static void BuildSpruce(MeshBuilder b, Vector3 basePos, float scale, uint seed)
        {
            Color32 cShadow = SpruceShadow;
            Color32 cMid = SpruceMid;
            Color32 cSun = SpruceSun;
            ApplyTintVariation(seed, ref cShadow, ref cMid, ref cSun);

            float totalH = (2.45f + Hash01(seed, 3) * 0.55f) * scale;
            float leanX = (Hash01(seed, 7) - 0.5f) * 0.08f * scale;
            float leanZ = (Hash01(seed, 11) - 0.5f) * 0.08f * scale;

            Vector3 midTrunk = basePos + new Vector3(leanX * 0.45f, totalH * 0.45f, leanZ * 0.45f);
            Vector3 topTrunk = basePos + new Vector3(leanX, totalH * 0.90f, leanZ);
            AddBranch(b, basePos, midTrunk, 0.125f * scale, 0.075f * scale, 6, OakBarkDark, SpruceBark, 0f, 0.08f);
            AddBranch(b, midTrunk, topTrunk, 0.075f * scale, 0.025f * scale, 5, SpruceBark, SpruceBark, 0.08f, 0.38f);

            const int tiers = 5;
            float baseAngle = Hash01(seed, 19) * Mathf.PI * 2f;
            for (int t = 0; t < tiers; t++)
            {
                float frac = t / (float)(tiers - 1);
                float y = Mathf.Lerp(0.46f * scale, totalH * 0.78f, frac);
                float outerR = Mathf.Lerp(0.76f, 0.28f, frac) * scale;
                float innerR = outerR * 0.56f;
                float tierH = Mathf.Lerp(0.58f, 0.42f, frac) * scale;
                float droop = Mathf.Lerp(0.14f, 0.05f, frac) * scale;
                float swayBot = Mathf.Lerp(0.12f, 0.44f, frac);
                float swayTop = Mathf.Lerp(0.24f, 0.66f, frac);

                Color32 tierLow = Color32.Lerp(cShadow, cMid, frac * 0.35f);
                Color32 tierHigh = Color32.Lerp(cMid, cSun, 0.25f + frac * 0.65f);
                Vector3 tierCenter = basePos + new Vector3(leanX * (y / totalH), y, leanZ * (y / totalH));

                AddConiferTier(b, tierCenter, outerR, innerR, tierH, droop, 6,
                    baseAngle + t * 0.58f, seed + (uint)(t * 101), tierLow, tierHigh, swayBot, swayTop);
            }

            Vector3 tipBase = basePos + new Vector3(leanX * 0.92f, totalH * 0.80f, leanZ * 0.92f);
            AddConiferTier(b, tipBase, 0.22f * scale, 0.13f * scale, totalH * 0.22f, 0.02f * scale, 5,
                baseAngle + 2.9f, seed + 919u, cMid, cSun, 0.52f, 0.74f);
        }

        private static void BuildBirch(MeshBuilder b, Vector3 basePos, float scale, uint seed)
        {
            Color32 cShadow = BirchShadow;
            Color32 cMid = BirchMid;
            Color32 cSun = BirchSun;
            ApplyTintVariation(seed, ref cShadow, ref cMid, ref cSun);

            float totalH = (2.15f + Hash01(seed, 5) * 0.45f) * scale;
            float bendX = (Hash01(seed, 13) - 0.5f) * 0.24f * scale;
            float bendZ = (Hash01(seed, 17) - 0.5f) * 0.24f * scale;

            Vector3 p0 = basePos;
            Vector3 p1 = basePos + new Vector3(bendX * 0.25f, totalH * 0.18f, bendZ * 0.25f);
            Vector3 p2 = basePos + new Vector3(bendX * 0.70f, totalH * 0.54f, bendZ * 0.70f);
            Vector3 p3 = basePos + new Vector3(bendX, totalH * 0.84f, bendZ);

            AddBranch(b, p0, p1, 0.095f * scale, 0.072f * scale, 5, BirchMark, BirchBark, 0f, 0.04f);
            AddBranch(b, p1, p2, 0.072f * scale, 0.052f * scale, 5, BirchBark, BirchBark, 0.04f, 0.16f);
            AddBranch(b, p2, p3, 0.052f * scale, 0.026f * scale, 5, BirchBark, BirchMark, 0.16f, 0.34f);

            float baseAngle = Hash01(seed, 29) * Mathf.PI * 2f;
            for (int i = 0; i < 5; i++)
            {
                float frac = i / 4f;
                float a = baseAngle + i * 2.25f;
                Vector3 anchor = Vector3.Lerp(p2, p3, frac);
                float reach = Mathf.Lerp(0.36f, 0.18f, frac) * scale;
                Vector3 puffPos = anchor + new Vector3(Mathf.Cos(a) * reach, 0.18f * scale, Mathf.Sin(a) * reach);

                AddBranch(b, anchor - Vector3.up * (0.06f * scale), puffPos, 0.028f * scale, 0.014f * scale, 4,
                    BirchBark, BirchBark, 0.14f + frac * 0.18f, 0.28f + frac * 0.25f);

                float rad = Mathf.Lerp(0.36f, 0.26f, frac) * scale;
                AddFoliageCluster(b, puffPos, rad, 0.92f, seed + (uint)(i * 149 + 31),
                    cShadow, cMid, cSun, 0.26f + frac * 0.22f, 0.48f + frac * 0.24f);
            }

            AddFoliageCluster(b, p3 + Vector3.up * (0.22f * scale), 0.30f * scale, 1.05f,
                seed + 733u, cShadow, cMid, cSun, 0.48f, 0.72f);
        }

        private static void BuildPoplar(MeshBuilder b, Vector3 basePos, float scale, uint seed, bool allowAutumn)
        {
            bool autumn = allowAutumn && (seed % 10u) == 0u;
            Color32 cShadow = autumn ? AutumnShadow : PoplarShadow;
            Color32 cMid = autumn ? AutumnMid : PoplarMid;
            Color32 cSun = autumn ? AutumnSun : PoplarSun;
            ApplyTintVariation(seed, ref cShadow, ref cMid, ref cSun);

            float totalH = (2.35f + Hash01(seed, 3) * 0.45f) * scale;
            Vector3 pMid = basePos + Vector3.up * (totalH * 0.45f);
            Vector3 pTop = basePos + Vector3.up * (totalH * 0.80f);
            AddBranch(b, basePos, pMid, 0.11f * scale, 0.072f * scale, 5, OakBarkDark, OakBark, 0f, 0.08f);
            AddBranch(b, pMid, pTop, 0.072f * scale, 0.035f * scale, 5, OakBark, OakBark, 0.08f, 0.28f);

            float baseAngle = Hash01(seed, 19) * Mathf.PI * 2f;
            float[] heights = { 0.38f, 0.54f, 0.68f, 0.82f, 0.95f };
            float[] radii = { 0.36f, 0.44f, 0.42f, 0.34f, 0.24f };
            for (int i = 0; i < heights.Length; i++)
            {
                float a = baseAngle + i * 2.1f;
                float off = (i == heights.Length - 1 ? 0.02f : 0.11f) * scale;
                Vector3 pos = basePos + new Vector3(Mathf.Cos(a) * off, totalH * heights[i], Mathf.Sin(a) * off);
                float swayBot = 0.16f + i * 0.10f;
                float swayTop = 0.28f + i * 0.11f;
                AddFoliageCluster(b, pos, radii[i] * scale, 1.08f, seed + (uint)(i * 173 + 9),
                    cShadow, cMid, cSun, swayBot, swayTop);
            }
        }

        private static void AddBranch(MeshBuilder b, Vector3 start, Vector3 end, float rStart, float rEnd, int segments,
            Color32 colStart, Color32 colEnd, float swayStart, float swayEnd)
        {
            Vector3 axis = end - start;
            float len = axis.magnitude;
            if (len < 0.001f) return;
            Vector3 dir = axis / len;
            Vector3 upRef = Mathf.Abs(dir.y) > 0.92f ? Vector3.right : Vector3.up;
            Vector3 tangent = Vector3.Normalize(Vector3.Cross(upRef, dir));
            Vector3 bitangent = Vector3.Cross(dir, tangent);

            Color32 c0 = MeshBuilder.WithSway(colStart, swayStart);
            Color32 c1 = MeshBuilder.WithSway(colEnd, swayEnd);
            float step = Mathf.PI * 2f / segments;

            for (int i = 0; i < segments; i++)
            {
                float a0 = i * step;
                float a1 = (i + 1) * step;
                Vector3 n0 = tangent * Mathf.Cos(a0) + bitangent * Mathf.Sin(a0);
                Vector3 n1 = tangent * Mathf.Cos(a1) + bitangent * Mathf.Sin(a1);
                Vector3 outward = (n0 + n1).normalized;

                Vector3 p00 = start + n0 * rStart;
                Vector3 p10 = start + n1 * rStart;
                Vector3 p11 = end + n1 * rEnd;
                Vector3 p01 = end + n0 * rEnd;

                b.AddQuadColors(p10, p00, p01, p11, c0, c0, c1, c1, outward);
            }
        }

        private static void AddFoliageCluster(MeshBuilder b, Vector3 center, float radius, float verticalScale, uint seed,
            Color32 shadowCol, Color32 midCol, Color32 sunCol, float swayLow, float swayHigh)
        {
            const int segs = 6;
            float step = Mathf.PI * 2f / segs;
            float yRadius = radius * verticalScale;

            Vector3 botPole = center + Vector3.down * (yRadius * 0.82f);
            Vector3 topPole = center + Vector3.up * (yRadius * 0.96f);

            Color32 cBot = MeshBuilder.WithSway(shadowCol, swayLow);
            Color32 cLow = MeshBuilder.WithSway(Color32.Lerp(shadowCol, midCol, 0.48f), Mathf.Lerp(swayLow, swayHigh, 0.35f));
            Color32 cHigh = MeshBuilder.WithSway(Color32.Lerp(midCol, sunCol, 0.62f), Mathf.Lerp(swayLow, swayHigh, 0.78f));
            Color32 cTop = MeshBuilder.WithSway(sunCol, swayHigh);

            Vector3[] lowRing = new Vector3[segs];
            Vector3[] highRing = new Vector3[segs];

            for (int i = 0; i < segs; i++)
            {
                float aLow = i * step;
                float aHigh = aLow + step * 0.35f;
                float jLow = 0.84f + Hash01(seed, i * 3 + 1) * 0.30f;
                float jHigh = 0.82f + Hash01(seed, i * 3 + 2) * 0.30f;
                float yJitLow = (Hash01(seed, i * 3 + 19) - 0.5f) * 0.14f * yRadius;
                float yJitHigh = (Hash01(seed, i * 3 + 37) - 0.5f) * 0.14f * yRadius;

                lowRing[i] = center + new Vector3(
                    Mathf.Cos(aLow) * radius * 0.96f * jLow,
                    -yRadius * 0.28f + yJitLow,
                    Mathf.Sin(aLow) * radius * 0.96f * jLow);

                highRing[i] = center + new Vector3(
                    Mathf.Cos(aHigh) * radius * 0.74f * jHigh,
                    yRadius * 0.40f + yJitHigh,
                    Mathf.Sin(aHigh) * radius * 0.74f * jHigh);
            }

            for (int i = 0; i < segs; i++)
            {
                int next = (i + 1) % segs;
                Vector3 pL0 = lowRing[i];
                Vector3 pL1 = lowRing[next];
                Vector3 pH0 = highRing[i];
                Vector3 pH1 = highRing[next];

                Vector3 botOut = ((pL0 + pL1) * 0.5f - center + Vector3.down * 0.5f).normalized;
                b.AddTriangle(botPole, pL0, pL1, botOut, cBot);

                Vector3 midOut = ((pL0 + pL1 + pH0 + pH1) * 0.25f - center).normalized;
                b.AddQuadColors(pL1, pL0, pH0, pH1, cLow, cLow, cHigh, cHigh, midOut);

                Vector3 topOut = ((pH0 + pH1) * 0.5f - center + Vector3.up * 0.6f).normalized;
                b.AddTriangle(topPole, pH1, pH0, topOut, cTop);
            }
        }

        private static void AddConiferTier(MeshBuilder b, Vector3 baseCenter, float outerR, float innerR, float height,
            float droop, int boughCount, float angleOffset, uint seed,
            Color32 lowCol, Color32 highCol, float swayLow, float swayHigh)
        {
            int pts = boughCount * 2;
            float step = Mathf.PI * 2f / pts;
            Vector3 apex = baseCenter + Vector3.up * height;
            Vector3 underCenter = baseCenter + Vector3.up * (height * 0.14f);

            Color32 cUnder = MeshBuilder.WithSway(Darken(lowCol, 0.76f), swayLow);
            Color32 cLow = MeshBuilder.WithSway(lowCol, swayLow);
            Color32 cHigh = MeshBuilder.WithSway(highCol, swayHigh);

            Vector3[] rim = new Vector3[pts];
            for (int i = 0; i < pts; i++)
            {
                bool isTip = (i & 1) == 0;
                float a = angleOffset + i * step;
                float jitter = 0.90f + Hash01(seed, i + 5) * 0.20f;
                float r = (isTip ? outerR : innerR) * jitter;
                float yOff = isTip ? -droop : droop * 0.35f;
                rim[i] = baseCenter + new Vector3(Mathf.Cos(a) * r, yOff, Mathf.Sin(a) * r);
            }

            for (int i = 0; i < pts; i++)
            {
                int next = (i + 1) % pts;
                Vector3 r0 = rim[i];
                Vector3 r1 = rim[next];

                Vector3 upOut = ((r0 + r1) * 0.5f - baseCenter + Vector3.up * 0.45f).normalized;
                b.AddQuadColors(r1, r0, apex, apex, cLow, cLow, cHigh, cHigh, upOut);
                b.AddTriangle(underCenter, r0, r1, Vector3.down, cUnder);
            }
        }

        private static void ApplyTintVariation(uint seed, ref Color32 shadow, ref Color32 mid, ref Color32 sun)
        {
            float shift = (Hash01(seed, 79) - 0.5f) * 0.16f;
            shadow = ShiftBrightness(shadow, shift);
            mid = ShiftBrightness(mid, shift);
            sun = ShiftBrightness(sun, shift * 0.8f);
        }

        private static Color32 ShiftBrightness(Color32 c, float delta)
        {
            float f = 1f + delta;
            return new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * f), 10, 250),
                (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * f), 10, 250),
                (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * f), 10, 250),
                c.a);
        }

        private static Color32 Darken(Color32 c, float factor) =>
            new Color32((byte)(c.r * factor), (byte)(c.g * factor), (byte)(c.b * factor), c.a);

        private static float Hash01(uint seed, int salt)
        {
            uint h = seed ^ (uint)(salt * 374761393);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFu) / 65535f;
        }
    }
}
