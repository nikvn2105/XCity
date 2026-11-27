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

        private static readonly Color32 WindowGlass = new Color32(70, 95, 125, 255);
        private static readonly Color32 LitWindowGlass = new Color32(162, 160, 139, 255);
        private static readonly Color32 DoorWood = new Color32(90, 60, 40, 255);
        private static readonly Color32 ConcreteLight = new Color32(200, 200, 195, 255);

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
            if (cell.Type == CellType.Industrial || cell.Type == CellType.PowerPlant)
            {
                IndustrialMeshFactory.Build(b, x, z, cell, quarterTurns, cellCenterLocal);
                return;
            }

            b.SetFrame(cellCenterLocal + Vector3.up * CityChunkRenderer.BuildingBase, quarterTurns);

            var paletteRng = new CellRng(Hash(x, z, (int)cell.Type));
            var shapeRng = new CellRng(Hash(x, z, (int)cell.Type * 16 + cell.Level + 100));

            Color32 lotColor = cell.Type switch
            {
                CellType.Residential => LotGrass,
                CellType.Commercial => LotPaving,
                CellType.School => new Color32(157, 170, 140, 255),
                CellType.CivicService => LotPaving,
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
            }
        }

        public static void BuildOverview(MeshBuilder builder, CityCell cell, int quarterTurns, Vector3 center)
        {
            builder.SetFrame(center + Vector3.up * CityChunkRenderer.BuildingBase, quarterTurns);
            var (walls, roof, width) = cell.Type switch
            {
                CellType.Residential => (new Color32(173, 156, 131, 255), new Color32(116, 86, 73, 255), 2.65f),
                CellType.Commercial => (new Color32(106, 140, 148, 255), new Color32(145, 154, 151, 255), 2.73f),
                CellType.Industrial => (new Color32(145, 142, 130, 255), new Color32(111, 118, 120, 255), 3.10f),
                CellType.School => (new Color32(201, 186, 147, 255), new Color32(88, 129, 124, 255), 2.98f),
                CellType.CivicService => (new Color32(189, 191, 177, 255), new Color32(97, 121, 132, 255), 3.06f),
                _ => (new Color32(159, 154, 141, 255), new Color32(84, 90, 93, 255), 3.02f)
            };

            if (cell.Model == BuildingModel.Harbor)
                builder.AddBox(new Vector3(0f, -0.36f, 3.02f), new Vector3(1.0f, 0.18f, 3.44f), walls, roof);

            float height = cell.Height;
            float depth = width * 0.82f;
            bool house = cell.Type == CellType.Residential && cell.Model != BuildingModel.Apartment && cell.Level == 1;
            if (house)
            {
                float wallHeight = height * 0.67f;
                builder.AddBox(new Vector3(0f, wallHeight * 0.5f, 0f), new Vector3(width, wallHeight, depth), walls, walls);
                builder.AddGableRoof(new Vector3(0f, wallHeight, 0f), width, depth,
                    height - wallHeight, 0.07f, roof, walls);
            }
            else
            {
                builder.AddBox(new Vector3(0f, height * 0.5f, 0f), new Vector3(width, height, depth), walls, roof);
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
                    BuildHousingOption(b, cell, height, ref palette, ref shape);
                    break;
                case BuildingModel.Preschool:
                case BuildingModel.PrimarySchool:
                case BuildingModel.HighSchool:
                case BuildingModel.University:
                    BuildSchool(b, cell.Model, height);
                    break;
                case BuildingModel.Office:
                case BuildingModel.OfficeTower:
                    BuildOffice(b, height, cell.Model == BuildingModel.OfficeTower, ref palette, ref shape);
                    break;
                case BuildingModel.Market:
                    BuildMarket(b, height);
                    break;
                case BuildingModel.HighStreet:
                    BuildHighStreet(b, height);
                    break;
                case BuildingModel.Harbor:
                    BuildHarbor(b);
                    break;
                case BuildingModel.PoliceStation:
                case BuildingModel.Hospital:
                case BuildingModel.FireStation:
                    BuildCivicService(b, cell.Model, height);
                    break;
                case BuildingModel.ShoppingMall:
                    BuildMall(b, height);
                    break;
                case BuildingModel.WaterPump:
                case BuildingModel.SewagePlant:
                    BuildUtility(b, cell.Model, height);
                    break;
            }
        }

        private static void BuildUtility(MeshBuilder b, BuildingModel model, float h)
        {
            var concrete = new Color32(176, 178, 170, 255);
            var slate = new Color32(84, 98, 105, 255);
            var water = new Color32(78, 128, 142, 255);
            var steel = new Color32(150, 161, 166, 255);
            b.AddBox(new Vector3(0, 0.067f, 0), new Vector3(3.38f, 0.054f, 3.38f), concrete, concrete);
            if (model == BuildingModel.WaterPump)
            {
                AddWindowBlock(b, new Vector3(-0.85f, 0.04f, -0.55f), 1.35f, 1.75f, h * 0.70f, 2, concrete, slate);
                for (int i = 0; i < 2; i++)
                {
                    var tank = new Vector3(0.85f, 0.09f, -0.80f + i * 1.45f);
                    b.AddCylinder(tank, 0.62f, 0.62f, h * 0.85f, 14, steel, true, slate);
                }
            }
            else
            {
                for (int i = 0; i < 2; i++)
                {
                    var basin = new Vector3(0.62f, 0.10f, -0.85f + i * 1.40f);
                    b.AddBox(basin, new Vector3(1.85f, 0.18f, 1.15f), concrete, concrete);
                    b.AddBox(basin + new Vector3(0, 0.095f, 0), new Vector3(1.65f, 0.01f, 0.95f), water, water);
                }
                AddWindowBlock(b, new Vector3(-1.10f, 0.04f, -0.60f), 0.95f, 1.45f, h * 0.65f, 2, concrete, slate);
            }
        }

        private static void BuildHousingOption(MeshBuilder b, CityCell cell, float h, ref CellRng palette, ref CellRng shape)
        {
            Color32 wall = WallPalette[palette.Range(0, WallPalette.Length)];
            Color32 roof = RoofPalette[palette.Range(0, RoofPalette.Length)];
            if (cell.Model == BuildingModel.Apartment)
            {
                BuildApartmentTower(b, h, wall, ref shape);
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

        private static void BuildOffice(MeshBuilder b, float h, bool tower, ref CellRng palette, ref CellRng shape)
        {
            var pavement = new Color32(161, 163, 156, 255);
            var metal = new Color32(83, 94, 99, 255);
            int form = shape.Range(0, 3);
            Color32 glass = CommercialGlassPalette[palette.Range(0, CommercialGlassPalette.Length)];
            Color32 frame = form == 1 ? new Color32(176, 172, 159, 255) : Darken(glass, 0.59f);
            const float baseY = 0.04f;
            const float podiumHeight = 0.91f;

            // A taller glazed lobby and a forecourt face the road.
            AddTowerLobby(b, new Vector3(0, baseY, -0.17f), 3.21f, 2.53f, podiumHeight, metal, glass);
            b.AddBox(new Vector3(0, 0.071f, 1.38f), new Vector3(3.25f, 0.056f, 0.76f), pavement, pavement);
            AddPlanter(b, new Vector3(-1.30f, 0.10f, 1.39f), 0.46f);

            float shaftWidth = form == 2 ? 2.13f : shape.Range(2.28f, 2.64f);
            float shaftDepth = form == 2 ? 2.30f : 2.02f;
            float shaftBottom = baseY + podiumHeight;
            float shaftHeight = h - shaftBottom - 0.31f;
            float shift = form == 2 ? -0.19f : 0.07f;
            var shaft = new Vector3(shift, shaftBottom, -0.25f);
            int floors = Mathf.Clamp(Mathf.RoundToInt(shaftHeight / 0.74f), 3, 9);
            int crownFloors = (tower || h > 5.7f) && form != 1 ? 2 : 0;
            int lowerFloors = floors - crownFloors;
            float lowerHeight = shaftHeight * lowerFloors / floors;
            AddTowerFloors(b, shaft, new Vector3(shaftWidth, lowerHeight, shaftDepth), lowerFloors,
                frame, glass, false, ref shape);

            float roofY = shaftBottom + lowerHeight;
            if (crownFloors > 0)
            {
                float upperWidth = shaftWidth - (form == 0 ? 0.36f : 0.52f);
                float upperDepth = shaftDepth - 0.39f;
                float upperHeight = shaftHeight - lowerHeight;
                var upper = new Vector3(shift + 0.14f, roofY, shaft.z - 0.10f);
                AddTowerFloors(b, upper, new Vector3(upperWidth, upperHeight, upperDepth), crownFloors,
                    frame, glass, false, ref shape);
                roofY += upperHeight;
                shaft = upper;
                shaftWidth = upperWidth;
                shaftDepth = upperDepth;
            }

            AddTowerRoof(b, new Vector3(shaft.x, roofY, shaft.z), shaftWidth, shaftDepth, metal,
                form == 2);
        }

        private static void BuildApartmentTower(MeshBuilder b, float h, Color32 wall, ref CellRng shape)
        {
            var charcoal = new Color32(79, 85, 82, 255);
            var glazing = new Color32(84, 113, 129, 255);
            var plinth = Darken(wall, 0.79f);
            float width = shape.Range(2.50f, 2.82f);
            float depth = shape.Range(1.95f, 2.20f);
            float offset = shape.Next() < 0.5f ? -0.19f : 0.19f;
            const float podiumHeight = 0.83f;
            const float baseY = 0.04f;
            AddTowerLobby(b, new Vector3(0, baseY, -0.16f), 3.02f, 2.43f, podiumHeight, plinth, glazing);
            AddPlanter(b, new Vector3(-1.24f, 0.04f, 1.33f), 0.48f);
            b.AddBox(new Vector3(1.19f, 0.077f, 1.31f), new Vector3(0.73f, 0.073f, 0.41f), plinth, ConcreteLight);

            float shaftBottom = baseY + podiumHeight;
            var foot = new Vector3(offset, shaftBottom, -0.29f);
            float shaftHeight = h - shaftBottom - 0.29f;
            int floors = Mathf.Clamp(Mathf.RoundToInt(shaftHeight / 0.77f), 3, 8);
            int mainFloors = floors - 1;
            float pitch = shaftHeight / floors;
            float mainHeight = pitch * mainFloors;
            AddTowerFloors(b, foot, new Vector3(width, mainHeight, depth), mainFloors, wall,
                glazing, true, ref shape);

            for (int floor = 0; floor < mainFloors; floor++)
            {
                float y = shaftBottom + floor * pitch + 0.10f;
                AddTowerBalcony(b, new Vector3(offset + width * 0.245f, y, foot.z + depth * 0.5f + 0.13f),
                    width * 0.39f, charcoal);
            }

            var penthouse = new Vector3(offset - 0.13f, shaftBottom + mainHeight, foot.z - 0.16f);
            const float setbackWidth = 0.52f;
            const float setbackDepth = 0.36f;
            AddTowerFloors(b, penthouse, new Vector3(width - setbackWidth, pitch, depth - setbackDepth),
                1, plinth, glazing, true, ref shape);
            AddTowerRoof(b, penthouse + Vector3.up * pitch, width - setbackWidth,
                depth - setbackDepth, charcoal, false);
        }

        private static void AddTowerLobby(MeshBuilder b, Vector3 foot, float width, float depth,
            float height, Color32 wall, Color32 glass)
        {
            var size = new Vector3(width, height, depth);
            var center = foot + Vector3.up * (height * 0.5f);
            b.AddBox(center, size, wall, wall);
            for (int side = 0; side < 4; side++)
            {
                float bay = (side < 2 ? width : depth) / 3.0f;
                for (int col = -1; col <= 1; col++)
                {
                    float bottom = foot.y + (side == 0 && col == 0 ? 0.025f : 0.15f);
                    float u = col * bay;
                    b.AddFacadeRect(center, size, side, u - bay * 0.40f, u + bay * 0.40f, bottom,
                        foot.y + height * 0.83f, glass, BuildingSurface.Glazing);
                }
            }
            float front = foot.z + depth * 0.5f;
            b.AddBox(new Vector3(foot.x, foot.y + height * 0.88f, front + 0.11f),
                new Vector3(width * 0.45f, 0.066f, 0.43f), wall, ConcreteLight);
        }

        private static void AddTowerFloors(MeshBuilder b, Vector3 foot, Vector3 size, int floors,
            Color32 wall, Color32 glass, bool residential, ref CellRng shape)
        {
            var center = foot + Vector3.up * (size.y * 0.5f);
            var roof = new Color32(111, 118, 115, 255);
            b.AddBox(center, size, wall, roof,
                wallSurface: residential ? BuildingSurface.Masonry : BuildingSurface.Plain);
            float pitch = size.y / floors;

            for (int side = 0; side < 4; side++)
            {
                float span = side < 2 ? size.x : size.z;
                int columns = residential ? 3 : span < 2.05f ? 3 : 4;
                float bay = span / columns;
                float halfPane = bay * (residential ? 0.33f : 0.46f);
                for (int floor = 0; floor < floors; floor++)
                {
                    float floorBase = foot.y + floor * pitch;
                    for (int col = 0; col < columns; col++)
                    {
                        float u = (col + 0.5f) * bay - span * 0.5f;
                        float tint = shape.Range(0.77f, 1.0f);
                        Color32 pane = residential && tint > 0.98f
                            ? new Color32(143, 142, 127, 255) : Darken(glass, tint);
                        float sill = residential ? 0.23f : 0.12f;
                        b.AddFacadeRect(center, size, side, u - halfPane, u + halfPane,
                            floorBase + pitch * sill, floorBase + pitch * 0.90f,
                            pane, BuildingSurface.Glazing);
                    }
                }
            }
        }

        private static void AddTowerBalcony(MeshBuilder b, Vector3 center, float width, Color32 rail)
        {
            b.AddBox(center, new Vector3(width, 0.066f, 0.31f), ConcreteLight, ConcreteLight);
            b.AddBox(center + new Vector3(0, 0.15f, 0.14f), new Vector3(width, 0.23f, 0.036f), rail, rail);
        }

        private static void AddTowerRoof(MeshBuilder b, Vector3 center, float width, float depth, Color32 metal, bool tallScreen)
        {
            const float rim = 0.063f;
            for (int edge = -1; edge <= 1; edge += 2)
            {
                b.AddBox(center + new Vector3(0, 0.065f, edge * (depth * 0.5f - rim * 0.5f)),
                    new Vector3(width, 0.13f, rim), metal, ConcreteLight);
                b.AddBox(center + new Vector3(edge * (width * 0.5f - rim * 0.5f), 0.065f, 0),
                    new Vector3(rim, 0.13f, depth - 2 * rim), metal, ConcreteLight);
            }
            float screen = tallScreen ? 0.35f : 0.24f;
            b.AddBox(center + new Vector3(-0.25f, screen * 0.5f, -0.28f),
                new Vector3(width * 0.47f, screen, depth * 0.43f), metal, metal, wallSurface: BuildingSurface.Panel);
            b.AddBox(center + new Vector3(width * 0.26f, 0.062f, 0.26f),
                new Vector3(0.28f, 0.124f, 0.46f), ConcreteLight, ConcreteLight);
        }

        private static void BuildMarket(MeshBuilder b, float h)
        {
            var steel = new Color32(83, 108, 107, 255);
            var canvas = new Color32(192, 158, 106, 255);
            var stone = new Color32(181, 170, 146, 255);
            float eave = h * 0.62f;
            b.AddBox(new Vector3(0, 0.065f, -0.26f), new Vector3(3.28f, 0.052f, 2.82f), stone, stone);
            b.AddGableRoof(new Vector3(0, eave, -0.43f), 3.08f, 1.85f, h * 0.28f, 0.12f,
                steel, steel);
            for (int side = -1; side <= 1; side += 2)
            {
                for (int end = -1; end <= 1; end += 2)
                    b.AddBox(new Vector3(side * 1.47f, eave * 0.5f, -0.43f + end * 0.88f),
                        new Vector3(0.09f, eave, 0.09f), steel, steel);
            }

            for (int stall = -1; stall <= 1; stall++)
            {
                float x = stall * 0.96f;
                Color32 awning = stall == 0 ? canvas : steel;
                b.AddBox(new Vector3(x, 1.04f, 0.77f), new Vector3(0.87f, 0.08f, 0.73f), awning, awning);
                b.AddBox(new Vector3(x, 0.33f, 0.26f), new Vector3(0.72f, 0.56f, 0.82f), stone, ConcreteLight);
                b.AddBox(new Vector3(x, 0.65f, 0.23f), new Vector3(0.60f, 0.10f, 0.44f), awning, canvas);
                b.AddBox(new Vector3(x - 0.35f, 0.54f, 1.07f), new Vector3(0.05f, 1.0f, 0.05f), steel, steel);
            }
            AddPlanter(b, new Vector3(1.36f, 0.06f, 1.39f), 0.46f);
        }

        private static void BuildHighStreet(MeshBuilder b, float h)
        {
            var limestone = new Color32(206, 198, 175, 255);
            var terracotta = new Color32(163, 109, 84, 255);
            var enamel = new Color32(74, 111, 106, 255);
            for (int shop = -1; shop <= 1; shop++)
            {
                float x = shop * 1.06f;
                float roof = h - (shop == 0 ? 0.08f : 0.34f);
                Color32 wall = shop == 0 ? limestone : terracotta;
                AddWindowBlock(b, new Vector3(x, 0.97f, -0.30f), 1.01f, 2.05f, roof - 0.97f,
                    2, wall, Darken(wall, 0.78f));
                var foot = new Vector3(x, 0.04f, -0.30f);
                AddShopfront(b, foot, 1.01f, 2.05f, 0.93f, wall, shop == 0 ? terracotta : enamel);
            }
            AddPlanter(b, new Vector3(-1.39f, 0.04f, 1.34f), 0.44f);
            AddPlanter(b, new Vector3(1.39f, 0.04f, 1.34f), 0.44f);
        }

        private static void BuildMall(MeshBuilder b, float h)
        {
            var wall = new Color32(184, 175, 155, 255);
            var cladding = new Color32(122, 145, 146, 255);
            var glass = new Color32(87, 148, 159, 255);
            AddShopfront(b, new Vector3(0, 0.04f, -0.37f), 3.42f, 2.48f, 1.06f, wall, cladding);
            AddWindowBlock(b, new Vector3(0, 1.10f, -0.38f), 3.36f, 2.37f, h * 0.47f,
                2, wall, cladding);
            float roof = 1.10f + h * 0.47f;
            var atrium = new Vector3(0, roof + 0.18f, -0.38f);
            b.AddBox(atrium, new Vector3(1.91f, 0.36f, 1.74f), glass, glass, wallSurface: BuildingSurface.Panel);
            b.AddGableRoof(atrium + Vector3.up * 0.18f, 1.91f, 1.74f, 0.58f, 0.06f,
                glass, glass, BuildingSurface.Glazing);
            AddRoofPlant(b, new Vector3(-1.10f, roof, -0.88f), cladding);
            var paving = new Color32(144, 151, 145, 255);
            b.AddBox(new Vector3(0, 0.07f, 1.34f), new Vector3(3.30f, 0.06f, 0.83f), paving, paving);
            b.AddBox(new Vector3(0, 1.28f, 1.10f), new Vector3(1.78f, 0.10f, 0.65f), cladding, glass);
            AddPlanter(b, new Vector3(-1.24f, 0.10f, 1.39f), 0.57f);
            AddPlanter(b, new Vector3(1.24f, 0.10f, 1.39f), 0.57f);
        }

        private static void AddShopfront(MeshBuilder b, Vector3 foot, float width, float depth, float height,
            Color32 wall, Color32 canopy)
        {
            var size = new Vector3(width, height, depth);
            var center = foot + Vector3.up * (height * 0.5f);
            float front = foot.z + depth * 0.5f;
            b.AddBox(center, size, wall, wall, wallSurface: BuildingSurface.Masonry);
            for (int pane = -1; pane <= 1; pane++)
            {
                float u = pane * width * 0.275f;
                b.AddFacadeRect(center, size, 0, u - width * 0.116f, u + width * 0.116f,
                    foot.y + 0.14f, foot.y + height * 0.78f, WindowGlass, BuildingSurface.Glazing);
            }
            b.AddBox(new Vector3(foot.x, foot.y + height * 0.86f, front + 0.13f),
                new Vector3(width * 0.94f, 0.075f, 0.33f), canopy, canopy);
        }

        private static void AddPlanter(MeshBuilder b, Vector3 foot, float width)
        {
          uint seed = Hash(Mathf.RoundToInt(foot.x * 100f), Mathf.RoundToInt(foot.z * 100f), 709);
            TreeMeshFactory.AddPlanterShrub(b, foot, width, seed);
        }

        private static void BuildHarbor(MeshBuilder b)
        {
            var siding = new Color32(164, 173, 163, 255);
            var teal = new Color32(54, 105, 108, 255);
            var dock = new Color32(129, 117, 93, 255);
            var concrete = new Color32(171, 173, 158, 255);
            AddWindowBlock(b, new Vector3(-0.52f, 0.04f, -0.65f), 1.84f, 1.41f, 1.35f, 1,
                siding, teal, BuildingSurface.Panel);
            b.AddGableRoof(new Vector3(-0.52f, 1.39f, -0.65f), 1.84f, 1.41f, 0.42f, 0.088f,
                teal, siding);
            b.AddBox(new Vector3(0.77f, 0.23f, -0.50f), new Vector3(0.61f, 0.39f, 0.66f), dock, dock);
            b.AddBox(new Vector3(1.02f, 0.15f, 0.34f), new Vector3(1.04f, 0.21f, 0.45f), concrete, dock);
            b.AddBox(new Vector3(0.0f, 0.12f, 0.82f), new Vector3(2.72f, 0.18f, 1.69f), concrete, concrete);

            b.AddBox(new Vector3(0f, -0.34f, 3.18f), new Vector3(0.93f, 0.13f, 3.33f), dock, dock);
            b.AddBox(new Vector3(0f, -0.18f, 1.58f), new Vector3(0.94f, 0.26f, 0.46f), dock, dock);
            b.AddBox(new Vector3(0f, -0.35f, 4.80f), new Vector3(1.83f, 0.12f, 0.38f), dock, dock);
            for (int i = 0; i < 4; i++)
            {
                float z = 2.07f + i * 0.83f;
                for (int side = -1; side <= 1; side += 2)
                    b.AddBox(new Vector3(side * 0.34f, -0.67f, z), new Vector3(0.11f, 1.02f, 0.12f), teal, dock);
            }

            AddMooredBoat(b, -0.91f, 3.58f, new Color32(183, 187, 169, 255), teal);
            AddMooredBoat(b, 0.91f, 2.95f, new Color32(143, 76, 61, 255), siding);
        }

        internal static void BuildPreviewWatercraft(MeshBuilder builder, Vector3 worldPosition, int quarterTurns)
        {
            builder.SetFrame(worldPosition, quarterTurns);
            AddMooredBoat(builder, 0f, 0f, new Color32(182, 193, 179, 255), new Color32(55, 113, 116, 255));
        }

        private static void AddMooredBoat(MeshBuilder b, float x, float z, Color32 hull, Color32 cabin)
        {
            const float water = -1.12f;
            const float deck = -0.83f;
            var stern = new Vector3(x, (water + deck) * 0.5f, z - 0.27f);
            var wood = new Color32(166, 145, 109, 255);
            b.AddBox(stern, new Vector3(0.64f, deck - water, 1.04f), hull, wood);
            var left = new Vector3(x - 0.32f, deck, z + 0.25f);
            var right = new Vector3(x + 0.32f, deck, z + 0.25f);
            var bow = new Vector3(x, deck + 0.04f, z + 0.94f);
            var down = Vector3.up * (deck - water);
            b.AddTriangle(left, right, bow, Vector3.up, wood);
            b.AddQuad(left, bow, bow - down, left - down, Vector3.left, hull);
            b.AddQuad(bow, right, right - down, bow - down, Vector3.right, hull);
            b.AddBox(new Vector3(x, deck + 0.15f, z - 0.30f), new Vector3(0.41f, 0.30f, 0.47f),
                WindowGlass, cabin, wallSurface: BuildingSurface.Glazing);
            b.AddBox(new Vector3(x, deck + 0.45f, z - 0.42f), new Vector3(0.035f, 0.33f, 0.035f),
                cabin, cabin);
        }

        private static void BuildCivicService(MeshBuilder b, BuildingModel model, float h)
        {
            var wall = new Color32(202, 199, 186, 255);
            var slate = new Color32(84, 98, 105, 255);
            var plaza = new Color32(157, 159, 149, 255);
            b.AddBox(new Vector3(0, 0.067f, 1.19f), new Vector3(3.38f, 0.054f, 1.13f), plaza, plaza);

            switch (model)
            {
                case BuildingModel.PoliceStation:
                {
                    var navy = new Color32(49, 80, 112, 255);
                    AddWindowBlock(b, new Vector3(-0.33f, 0.04f, -0.57f), 2.48f, 1.83f, h * 0.80f,
                        3, wall, slate);
                    AddWindowBlock(b, new Vector3(1.13f, 0.04f, -0.61f), 0.75f, 1.74f, h * 0.56f,
                        2, navy, slate, BuildingSurface.Panel);
                    AddCivicEntry(b, -0.21f, 0.38f, 1.11f, navy);
                    float x = -0.21f;
                    float y = 1.57f;
                    const float z = 0.43f;
                    var gold = new Color32(218, 191, 120, 255);
                    b.AddBox(new Vector3(x, y, 0.40f), new Vector3(0.63f, 0.45f, 0.045f), navy, navy);
                    b.AddQuad(new Vector3(x, y + 0.17f, z), new Vector3(x + 0.14f, y + 0.035f, z),
                        new Vector3(x, y - 0.16f, z), new Vector3(x - 0.14f, y + 0.035f, z), Vector3.forward, gold);
                    AddPlanter(b, new Vector3(1.24f, 0.09f, 1.30f), 0.51f);
                    break;
                }
                case BuildingModel.Hospital:
                {
                    var teal = new Color32(62, 128, 126, 255);
                    float roof = h * 0.80f + 0.04f;
                    AddWindowBlock(b, new Vector3(0.48f, 0.04f, -0.62f), 1.96f, 1.81f, h * 0.80f,
                        4, wall, slate);
                    AddWindowBlock(b, new Vector3(-1.04f, 0.04f, -0.42f), 1.01f, 2.12f, h * 0.45f,
                        2, teal, slate, BuildingSurface.Panel);
                    AddCivicEntry(b, -1.04f, 0.64f, 0.92f, teal);
                    b.AddBox(new Vector3(0.49f, roof + 0.07f, -0.60f), new Vector3(1.56f, 0.14f, 1.52f), slate, slate);
                    AddHospitalMark(b, new Vector3(0.49f, roof + 0.151f, -0.60f), 0.74f);
                    AddPlanter(b, new Vector3(1.22f, 0.09f, 1.28f), 0.52f);
                    break;
                }
                case BuildingModel.FireStation:
                {
                    var red = new Color32(155, 65, 54, 255);
                    var garage = new Color32(72, 86, 88, 255);
                    var size = new Vector3(2.77f, 1.72f, 1.97f);
                    var center = new Vector3(-0.29f, 0.90f, -0.54f);
                    b.AddBox(center, size, red, slate, wallSurface: BuildingSurface.Masonry);
                    b.AddBox(new Vector3(1.17f, h * 0.46f + 0.04f, -0.72f), new Vector3(0.69f, h * 0.92f, 1.39f),
                        wall, slate, wallSurface: BuildingSurface.Masonry);
                    for (int bay = 0; bay < 2; bay++)
                    {
                        float u = bay == 0 ? -0.68f : 0.66f;
                        b.AddFacadeRect(center, size, 0, u - 0.53f, u + 0.53f, 0.12f, 1.30f, garage);
                        b.AddBox(new Vector3(center.x + u, 1.17f, 0.48f), new Vector3(0.87f, 0.14f, 0.05f),
                            WindowGlass, WindowGlass, wallSurface: BuildingSurface.Glazing);
                        for (int bar = 1; bar <= 4; bar++)
                            b.AddBox(new Vector3(center.x + u, 0.14f + bar * 0.19f, 0.48f),
                                new Vector3(1.03f, 0.033f, 0.05f), slate, slate);
                    }
                    b.AddBox(new Vector3(-0.29f, 1.55f, 0.58f), new Vector3(2.44f, 0.11f, 0.26f), red, wall);
                    break;
                }
            }
        }

        private static void AddCivicEntry(MeshBuilder b, float x, float front, float width, Color32 color)
        {
            b.AddBox(new Vector3(x, 0.55f, front + 0.07f), new Vector3(width * 0.72f, 0.98f, 0.11f),
                WindowGlass, WindowGlass, wallSurface: BuildingSurface.Glazing);
            b.AddBox(new Vector3(x, 1.04f, front + 0.23f), new Vector3(width, 0.09f, 0.55f),
                color, color);
            b.AddBox(new Vector3(x, 0.09f, front + 0.27f), new Vector3(width, 0.10f, 0.56f),
                ConcreteLight, ConcreteLight);
        }

        private static void AddHospitalMark(MeshBuilder b, Vector3 center, float height)
        {
            var paint = new Color32(223, 228, 216, 255);
            const float thickness = 0.085f;
            for (int side = -1; side <= 1; side += 2)
                b.AddBox(center + Vector3.right * (side * height * 0.27f),
                    new Vector3(thickness, 0.008f, height), paint, paint);
            b.AddBox(center + Vector3.up * 0.002f,
                new Vector3(height * 0.54f, 0.008f, thickness), paint, paint);
        }
        private static void AddWindowBlock(MeshBuilder b, Vector3 foot, float width, float depth, float height,
            int floors, Color32 wall, Color32 top, BuildingSurface facade = BuildingSurface.Masonry)
        {
            var size = new Vector3(width, height, depth);
            var center = foot + Vector3.up * (height * 0.5f);
            b.AddBox(center, size, wall, top, wallSurface: facade, topSurface: BuildingSurface.Roof);

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
                        y, y + floorHeight * 0.38f, window, BuildingSurface.Glazing);
                    b.AddFacadeRect(center, size, 1, x - bay * 0.30f, x + bay * 0.30f,
                        y, y + floorHeight * 0.38f, WindowGlass, BuildingSurface.Glazing);
                }

                for (int side = 2; side < 4; side++)
                    b.AddFacadeRect(center, size, side, -depth * 0.31f, depth * 0.31f,
                        y, y + floorHeight * 0.34f, WindowGlass, BuildingSurface.Glazing);
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
                b.AddBox(bodyCenter, bodySize, wall, wall, wallSurface: BuildingSurface.Masonry);
                b.AddGableRoof(new Vector3(0f, baseY + wallH, 0f), w, d, roofH, 0.15f, roof, wall);

                AddPorch(b, 0, d * 0.5f, 0.92f, 0.92f);

                // Front door (+Z)
                b.AddFacadeRect(bodyCenter, bodySize, 0, -0.225f, 0.225f, baseY, baseY + 0.8f, DoorWood);

                // Windows on front and sides
                float winY0 = baseY + Mathf.Min(wallH - 0.5f, 0.65f);
                float winY1 = winY0 + 0.4f;
                if (winY1 < baseY + wallH - 0.1f)
                {
                    b.AddFacadeRect(bodyCenter, bodySize, 0, -0.78f, -0.38f, winY0, winY1, PickWindowColor(ref shapeRng), BuildingSurface.Glazing);
                    b.AddFacadeRect(bodyCenter, bodySize, 0, 0.38f, 0.78f, winY0, winY1, PickWindowColor(ref shapeRng), BuildingSurface.Glazing);

                    b.AddFacadeRect(bodyCenter, bodySize, 2, -0.65f, -0.25f, winY0, winY1, PickWindowColor(ref shapeRng), BuildingSurface.Glazing);
                    b.AddFacadeRect(bodyCenter, bodySize, 2, 0.25f, 0.65f, winY0, winY1, PickWindowColor(ref shapeRng), BuildingSurface.Glazing);
                    b.AddFacadeRect(bodyCenter, bodySize, 3, -0.65f, -0.25f, winY0, winY1, PickWindowColor(ref shapeRng), BuildingSurface.Glazing);
                    b.AddFacadeRect(bodyCenter, bodySize, 3, 0.25f, 0.65f, winY0, winY1, PickWindowColor(ref shapeRng), BuildingSurface.Glazing);
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
                b.AddBox(bodyCenter, bodySize, wall, roof,
                    wallSurface: BuildingSurface.Masonry, topSurface: BuildingSurface.Roof);

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
                            b.AddFacadeRect(bodyCenter, bodySize, side, u - 0.18f, u + 0.18f, fy0, fy1, PickWindowColor(ref shapeRng), BuildingSurface.Glazing);
                        }
                    }

                    if (f > 0)
                    {
                        AddBalcony(b, new Vector3(0f, baseY + f * floorH + 0.03f, d * 0.5f + 0.15f), 2.03f);
                    }
                }

                if (shapeRng.Next() < 0.6f)
                    AddYardTree(b, w * 0.5f, d * 0.5f, ref shapeRng);
                return;
            }

            BuildApartmentTower(b, h, wall, ref shapeRng);
        }

        private static void BuildCommercial(MeshBuilder b, float h, ref CellRng paletteRng, ref CellRng shapeRng) =>
            BuildOffice(b, h, false, ref paletteRng, ref shapeRng);

        private static void AddYardTree(MeshBuilder b, float halfW, float halfD, ref CellRng rng)
        {
            float tx = Mathf.Clamp(halfW + 0.35f, 1.25f, 1.48f) * (rng.Next() < 0.5f ? -1f : 1f);
            float tz = Mathf.Clamp(halfD + 0.3f, 1.20f, 1.45f);
            if (rng.Next() < 0.35f) tz = -tz;

          uint seed = (uint)rng.Range(1, 1000000);
            float scale = 0.46f + rng.Next() * 0.16f;
            TreeMeshFactory.AddYardTree(b, new Vector3(tx, 0.04f, tz), scale, seed);
        }

        private static Color32 PickWindowColor(ref CellRng rng) =>
            rng.Next() < 0.15f ? LitWindowGlass : WindowGlass;

        private static Color32 Darken(Color32 c, float factor) =>
            new Color32((byte)(c.r * factor), (byte)(c.g * factor), (byte)(c.b * factor), c.a);
    }
}
