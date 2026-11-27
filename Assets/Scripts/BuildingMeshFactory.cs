using UnityEngine;

namespace XCity
{
    internal static class BuildingMeshFactory
    {
        private static readonly Color32 LotGrass = new Color32(110, 165, 90, 255);
        private static readonly Color32 LotPaving = new Color32(185, 185, 180, 255);
        private static readonly Color32 LotAsphalt = new Color32(120, 120, 118, 255);

        private static readonly Color32[] WallPalette =
        {
            new Color32(235, 225, 200, 255), // cream
            new Color32(240, 200, 170, 255), // peach
            new Color32(190, 215, 230, 255), // light blue
            new Color32(240, 240, 235, 255), // white
            new Color32(200, 215, 185, 255)  // sage
        };

        private static readonly Color32[] RoofPalette =
        {
            new Color32(180, 80, 60, 255),  // terracotta
            new Color32(80, 85, 95, 255),   // slate
            new Color32(120, 85, 60, 255),  // brown
            new Color32(60, 120, 125, 255)  // teal
        };

        private static readonly Color32[] AwningPalette =
        {
            new Color32(200, 60, 50, 255),  // red
            new Color32(230, 140, 40, 255), // orange
            new Color32(60, 150, 90, 255),  // green
            new Color32(50, 110, 190, 255)  // blue
        };

        private static readonly Color32[] CommercialGlassPalette =
        {
            new Color32(110, 160, 200, 255),
            new Color32(90, 130, 170, 255),
            new Color32(150, 190, 210, 255),
            new Color32(120, 140, 160, 255)
        };

        private static readonly Color32[] IndustrialWallPalette =
        {
            new Color32(170, 160, 145, 255),
            new Color32(140, 140, 135, 255),
            new Color32(120, 130, 140, 255)
        };

        private static readonly Color32 WindowGlass = new Color32(70, 95, 125, 255);
        private static readonly Color32 LitWindowGlass = new Color32(255, 215, 140, 255);
        private static readonly Color32 DoorWood = new Color32(90, 60, 40, 255);
        private static readonly Color32 ConcreteLight = new Color32(200, 200, 195, 255);
        private static readonly Color32 MullionColor = new Color32(220, 226, 232, 255);
        private static readonly Color32 TrunkBrown = new Color32(110, 80, 50, 255);

        public static uint Hash(int x, int z, int salt)
        {
            unchecked
            {
                uint h = (uint)x * 0x8DA6B343u ^ (uint)z * 0xD8163841u ^ (uint)salt * 0xCB1AB31Fu;
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return h == 0 ? 1u : h;
            }
        }

        private struct CellRng
        {
            private uint _s;

            public CellRng(uint seed)
            {
                _s = seed == 0 ? 1u : seed;
            }

            public float Next()
            {
                _s ^= _s << 13;
                _s ^= _s >> 17;
                _s ^= _s << 5;
                return (_s >> 8) * (1f / 16777216f);
            }

            public float Range(float a, float b) => a + (b - a) * Next();
            public int Range(int a, int bExcl) => a + (int)(Next() * (bExcl - a));
        }

        public static void Build(MeshBuilder b, int x, int z, CityCell cell, int quarterTurns, Vector3 cellCenterLocal)
        {
            b.SetFrame(cellCenterLocal + Vector3.up * CityChunkRenderer.BuildingBase, quarterTurns);

            var paletteRng = new CellRng(Hash(x, z, (int)cell.Type));
            var shapeRng = new CellRng(Hash(x, z, (int)cell.Type * 16 + cell.Level + 100));

            Color32 lotColor = cell.Type switch
            {
                CellType.Residential => LotGrass,
                CellType.Commercial => LotPaving,
                _ => LotAsphalt
            };
            b.AddBox(new Vector3(0f, 0.02f, 0f), new Vector3(3.7f, 0.04f, 3.7f), lotColor, lotColor);

            float h = Mathf.Max(1.5f, cell.Height);
            switch (cell.Type)
            {
                case CellType.Residential:
                    BuildResidential(b, cell.Level, h, ref paletteRng, ref shapeRng);
                    break;
                case CellType.Commercial:
                    BuildCommercial(b, h, ref paletteRng, ref shapeRng);
                    break;
                case CellType.Industrial:
                    BuildIndustrial(b, h, ref paletteRng, ref shapeRng);
                    break;
                case CellType.PowerPlant:
                    BuildPowerPlant(b);
                    break;
            }
        }

        private static void BuildResidential(MeshBuilder b, int level, float h, ref CellRng paletteRng, ref CellRng shapeRng)
        {
            Color32 wall = WallPalette[paletteRng.Range(0, WallPalette.Length)];
            Color32 roof = RoofPalette[paletteRng.Range(0, RoofPalette.Length)];
            const float baseY = 0.04f;

            if (level <= 1)
            {
                float w = shapeRng.Range(2.2f, 2.6f);
                float d = shapeRng.Range(2.0f, 2.4f);
                float wallH = h * 0.6f;
                float roofH = h * 0.4f;

                Vector3 bodyCenter = new Vector3(0f, baseY + wallH * 0.5f, 0f);
                Vector3 bodySize = new Vector3(w, wallH, d);
                b.AddBox(bodyCenter, bodySize, wall, wall);
                b.AddGableRoof(new Vector3(0f, baseY + wallH, 0f), w, d, roofH, 0.15f, roof, wall);

                // Front door (+Z)
                b.AddFacadeRect(bodyCenter, bodySize, 0, -0.225f, 0.225f, baseY, baseY + 0.8f, DoorWood);

                // Windows on front and sides
                float winY0 = baseY + Mathf.Min(wallH - 0.5f, 0.65f);
                float winY1 = winY0 + 0.4f;
                if (winY1 < baseY + wallH - 0.1f)
                {
                    b.AddFacadeRect(bodyCenter, bodySize, 0, -0.78f, -0.38f, winY0, winY1, PickWindowColor(ref shapeRng));
                    b.AddFacadeRect(bodyCenter, bodySize, 0, 0.38f, 0.78f, winY0, winY1, PickWindowColor(ref shapeRng));

                    b.AddFacadeRect(bodyCenter, bodySize, 2, -0.65f, -0.25f, winY0, winY1, PickWindowColor(ref shapeRng));
                    b.AddFacadeRect(bodyCenter, bodySize, 2, 0.25f, 0.65f, winY0, winY1, PickWindowColor(ref shapeRng));
                    b.AddFacadeRect(bodyCenter, bodySize, 3, -0.65f, -0.25f, winY0, winY1, PickWindowColor(ref shapeRng));
                    b.AddFacadeRect(bodyCenter, bodySize, 3, 0.25f, 0.65f, winY0, winY1, PickWindowColor(ref shapeRng));
                }

                // 50% chimney
                if (shapeRng.Next() < 0.5f)
                {
                    float cx = (shapeRng.Next() < 0.5f ? -1f : 1f) * (w * 0.28f);
                    float cz = -d * 0.22f;
                    float ch = roofH + 0.45f;
                    b.AddBox(new Vector3(cx, baseY + wallH + ch * 0.5f, cz), new Vector3(0.24f, ch, 0.24f),
                        new Color32(150, 75, 65, 255), new Color32(80, 75, 75, 255));
                }

                AddYardTree(b, w * 0.5f, d * 0.5f, ref shapeRng);
                return;
            }

            if (level == 2)
            {
                const float w = 2.6f;
                const float d = 2.4f;
                int floors = Mathf.Max(2, Mathf.RoundToInt(h / 1.0f));
                float bodyH = Mathf.Max(1.8f, h - 0.08f);
                float floorH = bodyH / floors;

                Vector3 bodyCenter = new Vector3(0f, baseY + bodyH * 0.5f, 0f);
                Vector3 bodySize = new Vector3(w, bodyH, d);
                b.AddBox(bodyCenter, bodySize, wall, roof);

                // Parapet border (4 thin boxes)
                float topY = baseY + bodyH;
                Color32 parapetColor = Darken(wall, 0.88f);
                b.AddBox(new Vector3(0f, topY + 0.04f, d * 0.5f - 0.05f), new Vector3(w, 0.08f, 0.1f), parapetColor, parapetColor);
                b.AddBox(new Vector3(0f, topY + 0.04f, -d * 0.5f + 0.05f), new Vector3(w, 0.08f, 0.1f), parapetColor, parapetColor);
                b.AddBox(new Vector3(w * 0.5f - 0.05f, topY + 0.04f, 0f), new Vector3(0.1f, 0.08f, d - 0.2f), parapetColor, parapetColor);
                b.AddBox(new Vector3(-w * 0.5f + 0.05f, topY + 0.04f, 0f), new Vector3(0.1f, 0.08f, d - 0.2f), parapetColor, parapetColor);

                // Small rooftop box
                b.AddBox(new Vector3(-0.4f, topY + 0.18f, -0.3f), new Vector3(0.7f, 0.36f, 0.6f), ConcreteLight, parapetColor);

                // Door on ground floor
                b.AddFacadeRect(bodyCenter, bodySize, 0, -0.22f, 0.22f, baseY, baseY + Mathf.Min(0.75f, floorH * 0.8f), DoorWood);

                int winCount = shapeRng.Range(2, 4);
                for (int f = 0; f < floors; f++)
                {
                    float fy0 = baseY + f * floorH + floorH * 0.28f;
                    float fy1 = baseY + f * floorH + floorH * 0.72f;

                    for (int side = 0; side < 4; side++)
                    {
                        float faceSpan = side < 2 ? w : d;
                        float step = faceSpan / (winCount + 1);
                        for (int wi = 0; wi < winCount; wi++)
                        {
                            if (f == 0 && side == 0 && winCount == 3 && wi == 1) continue; // keep door clear
                            float u = -faceSpan * 0.5f + step * (wi + 1);
                            b.AddFacadeRect(bodyCenter, bodySize, side, u - 0.18f, u + 0.18f, fy0, fy1, PickWindowColor(ref shapeRng));
                        }
                    }

                    if (f > 0)
                    {
                        b.AddBox(new Vector3(0f, baseY + f * floorH + 0.03f, d * 0.5f + 0.175f),
                            new Vector3(2.2f, 0.06f, 0.35f), ConcreteLight, ConcreteLight);
                    }
                }

                if (shapeRng.Next() < 0.6f)
                    AddYardTree(b, w * 0.5f, d * 0.5f, ref shapeRng);
                return;
            }

            // Level 3 apartment
            {
                const float w = 2.9f;
                const float d = 2.7f;
                int floors = Mathf.Max(3, Mathf.RoundToInt(h / 0.9f));
                float floorH = h / floors;

                Vector3 bodyCenter = new Vector3(0f, baseY + h * 0.5f, 0f);
                Vector3 bodySize = new Vector3(w, h, d);
                b.AddBox(bodyCenter, bodySize, wall, roof);

                // Vertical accent stripe on front
                b.AddFacadeRect(bodyCenter, bodySize, 0, -0.22f, 0.22f, baseY + 0.05f, baseY + h - 0.05f, roof);

                // Continuous window bands per floor on all 4 sides
                for (int f = 0; f < floors; f++)
                {
                    float fy0 = baseY + f * floorH + floorH * 0.27f;
                    float fy1 = fy0 + floorH * 0.45f;
                    for (int side = 0; side < 4; side++)
                    {
                        float halfBand = (side < 2 ? w : d) * 0.425f;
                        b.AddFacadeRect(bodyCenter, bodySize, side, -halfBand, halfBand, fy0, fy1, PickWindowColor(ref shapeRng));
                    }
                }

                float topY = baseY + h;
                b.AddBox(new Vector3(-0.5f, topY + 0.25f, -0.35f), new Vector3(0.8f, 0.5f, 0.7f), ConcreteLight, roof);
                b.AddCylinder(new Vector3(0.65f, topY, -0.45f), 0.25f, 0.25f, 0.55f, 8,
                    new Color32(165, 175, 185, 255), true, new Color32(130, 140, 150, 255));
            }
        }

        private static void BuildCommercial(MeshBuilder b, float h, ref CellRng paletteRng, ref CellRng shapeRng)
        {
            const float baseY = 0.04f;
            const float podiumH = 0.9f;
            Vector3 podiumCenter = new Vector3(0f, baseY + podiumH * 0.5f, 0f);
            Vector3 podiumSize = new Vector3(3.0f, podiumH, 3.0f);
            b.AddBox(podiumCenter, podiumSize, ConcreteLight, new Color32(165, 165, 160, 255));

            // Front shop-window band + slanted awning
            Color32 awningColor = AwningPalette[paletteRng.Range(0, AwningPalette.Length)];
            b.AddFacadeRect(podiumCenter, podiumSize, 0, -1.25f, 1.25f, baseY + 0.12f, baseY + 0.62f, WindowGlass);
            b.AddQuad(
                new Vector3(-1.35f, baseY + 0.62f, 1.85f),
                new Vector3(1.35f, baseY + 0.62f, 1.85f),
                new Vector3(1.35f, baseY + 0.84f, 1.50f),
                new Vector3(-1.35f, baseY + 0.84f, 1.50f),
                new Vector3(0f, 0.8f, 0.6f), awningColor);

            // Tower
            float towerW = shapeRng.Range(2.2f, 2.6f);
            Color32 glass = CommercialGlassPalette[paletteRng.Range(0, CommercialGlassPalette.Length)];
            Color32 roofColor = new Color32(140, 145, 152, 255);
            float towerStartY = baseY + podiumH;
            float totalTowerH = Mathf.Max(1.6f, h - podiumH);

            bool hasSetback = h > 6.0f;
            float mainH = hasSetback ? totalTowerH * 0.7f : totalTowerH;
            Vector3 mainCenter = new Vector3(0f, towerStartY + mainH * 0.5f, 0f);
            Vector3 mainSize = new Vector3(towerW, mainH, towerW);
            b.AddBox(mainCenter, mainSize, glass, roofColor);
            AddMullionBands(b, mainCenter, mainSize, towerStartY, mainH);

            float topY = towerStartY + mainH;
            float topSpan = towerW;

            if (hasSetback)
            {
                float upperW = towerW * 0.7f;
                float upperH = totalTowerH * 0.3f;
                Vector3 upperCenter = new Vector3(0f, topY + upperH * 0.5f, 0f);
                Vector3 upperSize = new Vector3(upperW, upperH, upperW);
                b.AddBox(upperCenter, upperSize, glass, roofColor);
                AddMullionBands(b, upperCenter, upperSize, topY, upperH);
                topY += upperH;
                topSpan = upperW;
            }

            if (shapeRng.Next() < 0.5f)
            {
                b.AddBox(new Vector3(0f, topY + 0.4f, 0f), new Vector3(0.06f, 0.8f, 0.06f),
                    new Color32(210, 210, 215, 255), new Color32(220, 60, 50, 255));
            }
            else
            {
                b.AddBox(new Vector3(0f, topY + 0.22f, topSpan * 0.32f), new Vector3(topSpan * 0.65f, 0.44f, 0.16f),
                    awningColor, awningColor);
            }
        }

        private static void AddMullionBands(MeshBuilder b, Vector3 center, Vector3 size, float startY, float height)
        {
            float half = size.x * 0.5f - 0.02f;
            for (float y = startY + 0.8f; y < startY + height - 0.12f; y += 0.8f)
            {
                for (int side = 0; side < 4; side++)
                    b.AddFacadeRect(center, size, side, -half, half, y, y + 0.08f, MullionColor);
            }
        }

        private static void BuildIndustrial(MeshBuilder b, float h, ref CellRng paletteRng, ref CellRng shapeRng)
        {
            const float baseY = 0.04f;
            const float w = 3.0f;
            const float d = 2.2f;
            Color32 wall = IndustrialWallPalette[paletteRng.Range(0, IndustrialWallPalette.Length)];
            Color32 roofColor = new Color32(90, 95, 100, 255);

            float wallH = h * 0.6f;
            float roofH = h * 0.4f;
            Vector3 hallCenter = new Vector3(0f, baseY + wallH * 0.5f, -0.25f);
            Vector3 hallSize = new Vector3(w, wallH, d);
            b.AddBox(hallCenter, hallSize, wall, roofColor);

            // Sawtooth of 3 teeth along X (or flat)
            if (shapeRng.Next() < 0.75f)
            {
                float topY = baseY + wallH;
                float toothW = w / 3f;
                float z0 = hallCenter.z - d * 0.5f;
                float z1 = hallCenter.z + d * 0.5f;
                Color32 toothGlass = new Color32(130, 165, 190, 255);

                for (int i = 0; i < 3; i++)
                {
                    float x0 = -w * 0.5f + i * toothW;
                    float x1 = x0 + toothW;
                    // Vertical glazed face on +X
                    b.AddQuad(
                        new Vector3(x1, topY, z1),
                        new Vector3(x1, topY, z0),
                        new Vector3(x1, topY + roofH, z0),
                        new Vector3(x1, topY + roofH, z1),
                        Vector3.right, toothGlass);
                    // Sloped roof down toward -X
                    b.AddQuad(
                        new Vector3(x0, topY, z1),
                        new Vector3(x1, topY + roofH, z1),
                        new Vector3(x1, topY + roofH, z0),
                        new Vector3(x0, topY, z0),
                        new Vector3(-roofH, toothW, 0f), roofColor);
                    // End triangles on +Z and -Z
                    b.AddTriangle(new Vector3(x0, topY, z1), new Vector3(x1, topY, z1), new Vector3(x1, topY + roofH, z1), Vector3.forward, wall);
                    b.AddTriangle(new Vector3(x1, topY, z0), new Vector3(x0, topY, z0), new Vector3(x1, topY + roofH, z0), Vector3.back, wall);
                }
            }

            // Large roller door on front
            float doorH = Mathf.Min(0.9f, wallH - 0.1f);
            b.AddFacadeRect(hallCenter, hallSize, 0, -0.45f, 0.45f, baseY, baseY + doorH, new Color32(100, 100, 105, 255));

            // Chimney at back corner with red top band
            float totalChimneyH = h * 1.4f;
            float bandH = 0.25f;
            float shaftH = Mathf.Max(0.5f, totalChimneyH - bandH);
            Vector3 chimPos = new Vector3(-1.15f, baseY, -1.55f);
            b.AddCylinder(chimPos, 0.18f, 0.18f, shaftH, 8, new Color32(135, 130, 128, 255), false, default);
            b.AddCylinder(new Vector3(chimPos.x, baseY + shaftH, chimPos.z), 0.18f, 0.18f, bandH, 8,
                new Color32(190, 50, 40, 255), true, new Color32(55, 55, 55, 255));

            // 50% side tank cylinder
            if (shapeRng.Next() < 0.5f)
            {
                b.AddCylinder(new Vector3(1.1f, baseY, 1.25f), 0.45f, 0.45f, 1.0f, 10,
                    new Color32(210, 210, 205, 255), true, new Color32(185, 185, 180, 255));
            }
        }

        private static void BuildPowerPlant(MeshBuilder b)
        {
            const float baseY = 0.04f;
            Color32 towerConcrete = ConcreteLight;
            Color32 darkCap = new Color32(60, 60, 60, 255);

            // Cooling tower at back-left in 3 stacked sections (kept inside +-1.85)
            Vector3 towerPos = new Vector3(-0.58f, baseY, -0.58f);
            b.AddCylinder(towerPos, 1.22f, 0.96f, 1.15f, 12, towerConcrete, false, default);
            b.AddCylinder(new Vector3(towerPos.x, baseY + 1.15f, towerPos.z), 0.96f, 0.86f, 1.05f, 12, towerConcrete, false, default);
            b.AddCylinder(new Vector3(towerPos.x, baseY + 2.20f, towerPos.z), 0.86f, 0.96f, 1.00f, 12, towerConcrete, true, darkCap);

            // Turbine hall at front-right with purple roof identity
            b.AddBox(new Vector3(0.75f, baseY + 0.5f, 0.85f), new Vector3(1.6f, 1.0f, 1.2f),
                new Color32(170, 160, 190, 255), new Color32(150, 100, 200, 255));

            // Tall stack
            b.AddCylinder(new Vector3(1.25f, baseY, -0.45f), 0.15f, 0.15f, 4.0f, 8,
                new Color32(150, 150, 155, 255), true, darkCap);
        }

        private static void AddYardTree(MeshBuilder b, float halfW, float halfD, ref CellRng rng)
        {
            float tx = Mathf.Clamp(halfW + 0.35f, 1.25f, 1.48f) * (rng.Next() < 0.5f ? -1f : 1f);
            float tz = Mathf.Clamp(halfD + 0.3f, 1.20f, 1.45f);
            if (rng.Next() < 0.35f) tz = -tz;

            int delta = rng.Range(-15, 16);
            byte g = (byte)Mathf.Clamp(140 + delta, 90, 185);
            Color32 foliage = new Color32((byte)(65 + delta / 2), g, (byte)(65 + delta / 2), 255);
            Color32 foliageTip = new Color32((byte)Mathf.Min(255, foliage.r + 18), (byte)Mathf.Min(255, foliage.g + 22), (byte)Mathf.Min(255, foliage.b + 14), 255);

            b.AddCylinder(new Vector3(tx, 0.04f, tz), 0.08f, 0.055f, 0.38f, 5,
                TrunkBrown, MeshBuilder.WithSway(TrunkBrown, 0.14f), false, TrunkBrown);
            b.AddCylinder(new Vector3(tx, 0.36f, tz), 0.22f, 0.44f, 0.26f, 6,
                MeshBuilder.WithSway(foliage, 0.16f), MeshBuilder.WithSway(foliage, 0.40f), false, foliage);
            b.AddCylinder(new Vector3(tx, 0.62f, tz), 0.44f, 0.16f, 0.38f, 6,
                MeshBuilder.WithSway(foliageTip, 0.40f), MeshBuilder.WithSway(foliageTip, 0.60f),
                true, MeshBuilder.WithSway(foliageTip, 0.60f));
        }

        private static Color32 PickWindowColor(ref CellRng rng) =>
            rng.Next() < 0.15f ? LitWindowGlass : WindowGlass;

        private static Color32 Darken(Color32 c, float factor) =>
            new Color32((byte)(c.r * factor), (byte)(c.g * factor), (byte)(c.b * factor), c.a);
    }
}
