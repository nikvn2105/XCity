using UnityEngine;

namespace XCity
{
    internal static class BuildingMeshFactory
    {
        private static readonly Color32 LotGrass = new Color32(105, 137, 92, 255);
        private static readonly Color32 LotPaving = new Color32(185, 185, 180, 255);
        private static readonly Color32 LotAsphalt = new Color32(120, 120, 118, 255);

        private static readonly Color32[] WallPalette =
        {
            new Color32(203, 191, 167, 255), // cream
            new Color32(172, 124, 101, 255), // peach
            new Color32(158, 169, 169, 255), // light blue
            new Color32(210, 208, 196, 255), // white
            new Color32(165, 171, 151, 255)  // sage
        };

        private static readonly Color32[] RoofPalette =
        {
            new Color32(144, 88, 68, 255),  // terracotta
            new Color32(80, 85, 95, 255),   // slate
            new Color32(120, 85, 60, 255),  // brown
            new Color32(76, 94, 96, 255)  // teal
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
        private static readonly Color32 LitWindowGlass = new Color32(162, 160, 139, 255);
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
                CellType.School => new Color32(157, 170, 140, 255),
                _ => LotAsphalt
            };
            b.AddBox(new Vector3(0f, 0.02f, 0f), new Vector3(3.7f, 0.04f, 3.7f), lotColor, lotColor);

            float h = Mathf.Max(1.5f, cell.Height);
            if (cell.Model != BuildingModel.Default)
            {
                BuildCatalogModel(b, cell, h, ref paletteRng, ref shapeRng);
                return;
            }

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

        public static void BuildGroundwork(MeshBuilder b, int x, int z, CellType type, int quarterTurns, Vector3 cellCenterLocal)
        {
            b.SetFrame(cellCenterLocal + Vector3.up * CityChunkRenderer.BuildingBase, quarterTurns);

            var rng = new CellRng(Hash(x, z, (int)type + 505));
            Color32 dirt = new Color32(154, 140, 118, 255);
            Color32 stakeWood = new Color32(138, 102, 66, 255);
            Color32 coneOrange = new Color32(235, 115, 35, 255);
            Color32 brickRed = new Color32(180, 82, 62, 255);

            // Cleared dirt/gravel pad
            b.AddBox(new Vector3(0f, 0.015f, 0f), new Vector3(2.6f, 0.03f, 2.6f), dirt, dirt);

            // 4 corner stakes
            b.AddBox(new Vector3(-1.18f, 0.18f, -1.18f), new Vector3(0.08f, 0.36f, 0.08f), stakeWood, stakeWood);
            b.AddBox(new Vector3(1.18f, 0.18f, -1.18f), new Vector3(0.08f, 0.36f, 0.08f), stakeWood, stakeWood);
            b.AddBox(new Vector3(-1.18f, 0.18f, 1.18f), new Vector3(0.08f, 0.36f, 0.08f), stakeWood, stakeWood);
            b.AddBox(new Vector3(1.18f, 0.18f, 1.18f), new Vector3(0.08f, 0.36f, 0.08f), stakeWood, stakeWood);

            // Small material pallet
            float px = rng.Next() < 0.5f ? -0.55f : 0.55f;
            b.AddBox(new Vector3(px, 0.06f, -0.45f), new Vector3(0.65f, 0.06f, 0.50f), stakeWood, stakeWood);
            b.AddBox(new Vector3(px, 0.19f, -0.45f), new Vector3(0.50f, 0.20f, 0.38f), brickRed, brickRed);

            // Two safety cones near the road edge
            b.AddCylinder(new Vector3(-0.65f, 0.03f, 1.10f), 0.11f, 0.03f, 0.28f, 6, coneOrange, true, coneOrange);
            b.AddCylinder(new Vector3(0.65f, 0.03f, 1.10f), 0.11f, 0.03f, 0.28f, 6, coneOrange, true, coneOrange);
        }

        private static void BuildCatalogModel(MeshBuilder b, CityCell cell, float height,
            ref CellRng palette, ref CellRng shape)
        {
            switch (cell.Model)
            {
                case BuildingModel.RowHouse:
                case BuildingModel.Villa:
                case BuildingModel.Apartment:
                    BuildHousingOption(b, cell, height, ref palette);
                    break;
                case BuildingModel.Preschool:
                case BuildingModel.PrimarySchool:
                case BuildingModel.HighSchool:
                case BuildingModel.University:
                    BuildSchool(b, cell.Model, height);
                    break;
                case BuildingModel.Workshop:
                case BuildingModel.Warehouse:
                    BuildIndustrialShed(b, cell.Model == BuildingModel.Warehouse, height);
                    break;
                case BuildingModel.Factory:
                    BuildIndustrial(b, height, ref palette, ref shape);
                    break;
                case BuildingModel.Office:
                case BuildingModel.OfficeTower:
                    BuildOffice(b, cell.Model == BuildingModel.OfficeTower, height);
                    break;
                case BuildingModel.Market:
                    BuildMarket(b, height);
                    break;
                case BuildingModel.ShoppingMall:
                    BuildMall(b, height);
                    break;
            }
        }

        private static void BuildHousingOption(MeshBuilder b, CityCell cell, float h, ref CellRng palette)
        {
            Color32 wall = WallPalette[palette.Range(0, WallPalette.Length)];
            Color32 roof = RoofPalette[palette.Range(0, RoofPalette.Length)];
            if (cell.Model == BuildingModel.Apartment)
            {
                AddWindowBlock(b, new Vector3(0, 0.04f, -0.12f), 2.75f, 2.48f, h - 0.30f,
                    cell.Level + 2, wall, roof);
                float floorPitch = (h - 0.30f) / (cell.Level + 2);
                for (int floor = 1; floor < cell.Level + 2; floor++)
                {
                    float y = 0.04f + floor * floorPitch;
                    AddBalcony(b, new Vector3(0.62f, y, 1.30f), 1.20f);
                }
                AddRoofPlant(b, new Vector3(-0.45f, h - 0.26f, -0.34f), roof);
                AddPorch(b, -0.56f, 1.13f, 0.84f, 0.80f);
                return;
            }

            if (cell.Model == BuildingModel.Villa)
            {
                float body = h * 0.60f;
                AddWindowBlock(b, new Vector3(-0.28f, 0.04f, -0.24f), 2.05f, 1.9f, body,
                    cell.Level, wall, roof);
                b.AddGableRoof(new Vector3(-0.28f, body + 0.04f, -0.24f),
                    2.05f, 1.9f, h * 0.19f, 0.12f, roof, wall);
                AddPorch(b, -0.48f, 0.72f, 0.94f, 0.80f);
                b.AddBox(new Vector3(1.13f, 0.59f, -0.67f), new Vector3(0.78f, 1.10f, 1.42f), wall, roof);
                b.AddBox(new Vector3(1.13f, 1.18f, -0.67f), new Vector3(0.91f, 0.11f, 1.53f), roof, roof);
                b.AddBox(new Vector3(1.06f, 0.10f, 0.77f), new Vector3(0.98f, 0.12f, 1.16f),
                    ConcreteLight, new Color32(119, 185, 188, 255));
                b.AddCylinder(new Vector3(-1.36f, 0.04f, 1.22f), 0.23f, 0.16f, 0.73f,
                    6, new Color32(89, 138, 88, 255), true, LotGrass);
                return;
            }

            for (int house = -1; house <= 1; house++)
            {
                float x = house * 0.97f;
                float body = h * 0.70f + (house == 0 ? 0.12f : 0f);
                Color32 tone = house == 0 ? wall : Darken(wall, house < 0 ? 0.88f : 0.96f);
                AddWindowBlock(b, new Vector3(x, 0.04f, -0.06f), 0.91f, 2.04f, body,
                    cell.Level, tone, roof);
                AddPorch(b, x, 0.97f, 0.57f, 0.72f);
                b.AddBox(new Vector3(x + 0.24f, body + h * 0.13f, -0.36f),
                    new Vector3(0.16f, h * 0.27f, 0.21f), tone, ConcreteLight);
                b.AddGableRoof(new Vector3(x, body + 0.04f, -0.06f),
                    0.91f, 2.04f, h * 0.18f, 0.025f, roof, tone);
            }
        }

        private static void BuildSchool(MeshBuilder b, BuildingModel model, float h)
        {
            var courtyard = new Color32(199, 171, 127, 255);
            var cream = new Color32(231, 219, 180, 255);
            var darkTeal = new Color32(66, 119, 121, 255);
            var brick = new Color32(163, 91, 71, 255);
            b.AddBox(new Vector3(0, 0.052f, 0.99f), new Vector3(2.88f, 0.024f, 1.32f), courtyard, courtyard);

            switch (model)
            {
                case BuildingModel.Preschool:
                    AddWindowBlock(b, new Vector3(0, 0.04f, -0.62f), 2.48f, 1.48f, 1.36f,
                        1, cream, brick);
                    b.AddGableRoof(new Vector3(0, 1.40f, -0.62f), 2.48f, 1.48f, 0.65f, 0.08f, brick, cream);
                    b.AddBox(new Vector3(0.68f, 0.24f, 1.12f), new Vector3(0.78f, 0.34f, 0.68f),
                        darkTeal, new Color32(239, 185, 78, 255));
                    break;
                case BuildingModel.PrimarySchool:
                    AddWindowBlock(b, new Vector3(0, 0.04f, -0.65f), 2.94f, 1.32f, h * 0.82f,
                        2, cream, darkTeal);
                    AddWindowBlock(b, new Vector3(-1.07f, 0.04f, 0.52f), 0.78f, 0.92f, 1.65f,
                        1, cream, darkTeal);
                    AddSchoolCourt(b, 0.38f, darkTeal);
                    break;
                case BuildingModel.HighSchool:
                    AddWindowBlock(b, new Vector3(0, 0.04f, -0.57f), 3.0f, 1.50f, h * 0.82f,
                        3, brick, ConcreteLight);
                    AddWindowBlock(b, new Vector3(-1.1f, 0.04f, 0.36f), 0.82f, 0.78f, h * 0.48f,
                        1, cream, ConcreteLight);
                    AddSchoolCourt(b, 0.34f, darkTeal);
                    break;
                case BuildingModel.University:
                    AddWindowBlock(b, new Vector3(-0.88f, 0.04f, -0.60f), 1.18f, 1.52f, h * 0.93f,
                        4, cream, darkTeal);
                    AddWindowBlock(b, new Vector3(0.66f, 0.04f, -0.45f), 1.48f, 1.74f, h * 0.62f,
                        3, brick, ConcreteLight);
                    b.AddCylinder(new Vector3(0.33f, 0.066f, 0.94f), 0.43f, 0.39f, 0.17f,
                        10, ConcreteLight, true, new Color32(128, 182, 183, 255));
                    break;
            }

            b.AddBox(new Vector3(-1.42f, 0.78f, 1.32f), new Vector3(0.05f, 1.45f, 0.05f),
                ConcreteLight, ConcreteLight);
            b.AddBox(new Vector3(-1.18f, 1.30f, 1.32f), new Vector3(0.43f, 0.23f, 0.035f), brick, brick);
        }

        private static void AddSchoolCourt(MeshBuilder b, float x, Color32 surface)
        {
            b.AddBox(new Vector3(x, 0.069f, 0.96f), new Vector3(1.68f, 0.016f, 1.06f), surface, surface);
            b.AddBox(new Vector3(x, 0.081f, 0.96f), new Vector3(0.033f, 0.012f, 0.97f),
                ConcreteLight, ConcreteLight);
        }

        private static void BuildIndustrialShed(MeshBuilder b, bool warehouse, float h)
        {
            var wall = new Color32(154, 151, 140, 255);
            var roof = warehouse ? new Color32(75, 112, 125, 255) : new Color32(140, 88, 70, 255);
            float body = h * (warehouse ? 0.80f : 0.66f);
            var size = new Vector3(3.10f, body, 2.14f);
            var center = new Vector3(0, body * 0.5f + 0.04f, -0.40f);
            b.AddBox(center, size, wall, roof);
            if (warehouse)
            {
                b.AddBox(new Vector3(0, body + 0.07f, -0.40f), new Vector3(3.30f, 0.10f, 2.28f), roof, roof);
                for (int bay = -1; bay <= 1; bay++)
                    b.AddFacadeRect(center, size, 0, bay * 0.86f - 0.30f, bay * 0.86f + 0.30f,
                        0.06f, body * 0.62f, roof);
            }
            else
            {
                b.AddGableRoof(new Vector3(0, body + 0.04f, -0.40f), 3.10f, 2.14f, h * 0.22f, 0.07f, roof, wall);
                b.AddFacadeRect(center, size, 0, -0.62f, 0.62f, 0.06f, body * 0.75f, WindowGlass);
                b.AddBox(new Vector3(-1.16f, 0.29f, 1.03f), new Vector3(0.52f, 0.50f, 0.61f), roof, wall);
            }
        }

        private static void BuildOffice(MeshBuilder b, bool tower, float h)
        {
            var glass = new Color32(76, 143, 165, 255);
            var roof = new Color32(136, 163, 168, 255);
            AddWindowBlock(b, new Vector3(0, 0.04f, -0.10f), 3.05f, 2.75f, 1.08f, 1, ConcreteLight, glass);
            float width = tower ? 1.89f : 2.58f;
            AddWindowBlock(b, new Vector3(0, 1.12f, -0.18f), width, 2.21f, h - 1.44f,
                tower ? 7 : 3, glass, roof);
            if (tower)
            {
                b.AddBox(new Vector3(0.56f, h - 0.12f, -0.45f), new Vector3(0.45f, 0.36f, 1.07f), roof, ConcreteLight);
                b.AddBox(new Vector3(-0.46f, h - 0.12f, -0.29f), new Vector3(0.06f, 0.44f, 0.06f), roof, ConcreteLight);
            }
        }

        private static void BuildMarket(MeshBuilder b, float h)
        {
            var timber = new Color32(144, 111, 84, 255);
            for (int stall = 0; stall < 3; stall++)
            {
                float x = (stall - 1) * 0.92f;
                Color32 canopy = AwningPalette[stall];
                b.AddBox(new Vector3(x, 0.33f, 0.07f), new Vector3(0.74f, 0.57f, 1.68f), timber, ConcreteLight);
                b.AddBox(new Vector3(x, h * 0.61f, -0.33f), new Vector3(0.78f, 0.10f, 1.89f), canopy, canopy);
                b.AddBox(new Vector3(x - 0.32f, 0.98f, 0.51f), new Vector3(0.05f, 0.95f, 0.05f), timber, timber);
                b.AddBox(new Vector3(x + 0.32f, 0.98f, 0.51f), new Vector3(0.05f, 0.95f, 0.05f), timber, timber);
            }
        }

        private static void BuildMall(MeshBuilder b, float h)
        {
            var brick = new Color32(181, 134, 104, 255);
            var ivory = new Color32(225, 218, 199, 255);
            var glass = new Color32(91, 161, 178, 255);
            AddWindowBlock(b, new Vector3(0, 0.04f, -0.16f), 3.42f, 2.8f, h * 0.62f, 2, ivory, brick);
            AddWindowBlock(b, new Vector3(0, h * 0.62f + 0.04f, -0.36f), 2.86f, 2.03f, h * 0.26f,
                1, brick, ivory);
            b.AddGableRoof(new Vector3(0, h * 0.88f + 0.04f, -0.36f), 0.89f, 1.47f, h * 0.095f,
                0.035f, glass, glass);
            b.AddBox(new Vector3(0, 1.06f, 1.42f), new Vector3(2.02f, 0.15f, 0.48f), glass, ivory);
        }

        private static void AddWindowBlock(MeshBuilder b, Vector3 foot, float width, float depth, float height,
            int floors, Color32 wall, Color32 top)
        {
            var size = new Vector3(width, height, depth);
            var center = foot + Vector3.up * (height * 0.5f);
            b.AddBox(center, size, wall, top);

            b.AddBox(new Vector3(foot.x, foot.y + height - 0.025f, foot.z),
                new Vector3(width + 0.085f, 0.05f, depth + 0.085f), Darken(top, 0.78f), top);
            float floorHeight = height / floors;
            int columns = width > 2.2f ? 3 : width > 1.1f ? 2 : 1;
            float bay = width / columns;
            for (int floor = 0; floor < floors; floor++)
            {
                float y = foot.y + (floor + 0.36f) * floorHeight;
                for (int column = 0; column < columns; column++)
                {
                    float x = (column + 0.5f) * bay - width * 0.5f;
                    // Projecting lintels catch sunlight above the glazing and shade the upper reveal.
                    if (width > 1.4f)
                        b.AddBox(new Vector3(center.x + x, y + floorHeight * 0.40f, center.z + depth * 0.5f + 0.038f),
                            new Vector3(bay * 0.68f, 0.045f, 0.095f), Darken(wall, 0.78f), wall);
                    Color32 window = (floor + column) % 5 == 3 ? LitWindowGlass : WindowGlass;
                    b.AddFacadeRect(center, size, 0, x - bay * 0.30f, x + bay * 0.30f,
                        y, y + floorHeight * 0.38f, window);
                    b.AddFacadeRect(center, size, 1, x - bay * 0.30f, x + bay * 0.30f,
                        y, y + floorHeight * 0.38f, WindowGlass);
                }

                for (int side = 2; side < 4; side++)
                    b.AddFacadeRect(center, size, side, -depth * 0.31f, depth * 0.31f,
                        y, y + floorHeight * 0.34f, WindowGlass);
            }
        }

        private static void AddPorch(MeshBuilder b, float x, float facadeZ, float width, float height)
        {
            const float depth = 0.34f;
            float z = facadeZ + depth * 0.5f;
            var metal = new Color32(70, 77, 78, 255);
            b.AddBox(new Vector3(x, 0.09f, z), new Vector3(width, 0.10f, depth + 0.13f), ConcreteLight, ConcreteLight);
            b.AddBox(new Vector3(x, height, z), new Vector3(width + 0.10f, 0.075f, depth + 0.09f), metal, metal);
            b.AddBox(new Vector3(x - width * 0.39f, height * 0.5f, facadeZ + depth),
                new Vector3(0.05f, height, 0.05f), metal, metal);
            b.AddBox(new Vector3(x, height * 0.48f, facadeZ + 0.028f),
                new Vector3(width * 0.47f, height * 0.83f, 0.06f), DoorWood, DoorWood);
        }

        private static void AddBalcony(MeshBuilder b, Vector3 center, float width)
        {
            var rail = new Color32(102, 120, 121, 255);
            b.AddBox(center, new Vector3(width, 0.085f, 0.42f), ConcreteLight, ConcreteLight);
            b.AddBox(center + new Vector3(0, 0.17f, 0.18f), new Vector3(width, 0.27f, 0.05f), rail, ConcreteLight);
            for (int side = -1; side <= 1; side += 2)
                b.AddBox(center + new Vector3(side * (width * 0.5f - 0.025f), 0.17f, 0),
                    new Vector3(0.05f, 0.27f, 0.38f), rail, ConcreteLight);
        }

        private static void AddRoofPlant(MeshBuilder b, Vector3 foot, Color32 metal)
        {
            b.AddBox(foot + Vector3.up * 0.14f, new Vector3(0.84f, 0.28f, 0.58f), metal, ConcreteLight);
            for (int i = -1; i <= 1; i += 2)
                b.AddCylinder(foot + new Vector3(i * 0.19f, 0.28f, 0), 0.14f, 0.14f, 0.04f, 8,
                    metal, true, Darken(metal, 0.62f));
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
                float roofH = Mathf.Min(h * 0.26f, 0.85f);

                Vector3 bodyCenter = new Vector3(0f, baseY + wallH * 0.5f, 0f);
                Vector3 bodySize = new Vector3(w, wallH, d);
                b.AddBox(bodyCenter, bodySize, wall, wall);
                b.AddGableRoof(new Vector3(0f, baseY + wallH, 0f), w, d, roofH, 0.15f, roof, wall);

                AddPorch(b, 0, d * 0.5f, 0.92f, 0.92f);

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
