using UnityEngine;

namespace XCity
{
    internal static class IndustrialMeshFactory
    {
        private enum IndustrialArchetype
        {
            LogisticsHub = 0,
            SawtoothPlant = 1,
            ChemicalTankFarm = 2,
            BulkSilosMill = 3,
            HeavyFoundry = 4,
            CleanTechFab = 5
        }

        private const float CatalogStackHeightRatio = 1.08f;

        // CS2 Industrial Palette
        private static readonly Color32 PadAsphalt = new Color32(114, 116, 118, 255);
        private static readonly Color32 ApronConcrete = new Color32(158, 156, 150, 255);
        private static readonly Color32 BundConcrete = new Color32(146, 144, 138, 255);
        private static readonly Color32 BasaltPlinth = new Color32(64, 68, 74, 255);
        private static readonly Color32 SteelBeam = new Color32(76, 82, 90, 255);
        private static readonly Color32 RoofDark = new Color32(82, 88, 96, 255);
        private static readonly Color32 RoofLight = new Color32(142, 148, 154, 255);
        private static readonly Color32 SafetyYellow = new Color32(226, 176, 48, 255);
        private static readonly Color32 OxideRed = new Color32(168, 68, 54, 255);
        private static readonly Color32 LogisticsBlue = new Color32(54, 104, 156, 255);
        private static readonly Color32 SafetyOrange = new Color32(208, 104, 44, 255);
        private static readonly Color32 DarkShutter = new Color32(48, 52, 58, 255);
        private static readonly Color32 ShutterMetal = new Color32(148, 152, 156, 255);
        private static readonly Color32 DarkGlass = new Color32(66, 90, 112, 255);
        private static readonly Color32 FurnaceGlow = new Color32(238, 146, 58, 255);
        private static readonly Color32 TankSilver = new Color32(198, 202, 206, 255);
        private static readonly Color32 SiloConcrete = new Color32(186, 182, 174, 255);
        private static readonly Color32 PowerViolet = new Color32(124, 98, 156, 255);
        private static readonly Color32 FlueDark = new Color32(42, 44, 48, 255);

        private static readonly Color32[] CladdingPalette =
        {
            new Color32(176, 182, 188, 255), // Aluzinc silver
            new Color32(96, 106, 116, 255),  // Weathered slate steel
            new Color32(188, 182, 172, 255), // Precast warm concrete
            new Color32(152, 92, 74, 255),   // Industrial red-brown brick
            new Color32(126, 138, 128, 255), // Olive/sage sheet metal
            new Color32(182, 168, 146, 255)  // Muted sand cladding
        };

        private static readonly Color32[] ContainerPalette =
        {
            new Color32(62, 122, 168, 255),  // Maersk blue
            new Color32(162, 72, 52, 255),   // Rust corten red
            new Color32(56, 118, 82, 255),   // Evergreen
            new Color32(198, 148, 54, 255)   // Ochre
        };

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

            public int Range(int a, int bExcl) => a + (int)(Next() * (bExcl - a));
        }

        public static void Build(MeshBuilder b, int x, int z, CityCell cell, int quarterTurns, Vector3 cellCenterLocal)
        {
            b.SetFrame(cellCenterLocal + Vector3.up * CityChunkRenderer.BuildingBase, quarterTurns);
            BuildCompoundBase(b);

            var paletteRng = new CellRng(BuildingMeshFactory.Hash(x, z, (int)cell.Type + 404));
            int shapeLevel = cell.Model == BuildingModel.Default ? cell.Level : 1;
            var shapeRng = new CellRng(BuildingMeshFactory.Hash(x, z, (int)cell.Type * 19 + shapeLevel + 707));
            float h = Mathf.Max(1.8f, cell.Height);

            if (cell.Type == CellType.PowerPlant)
            {
                BuildPowerPlant(b);
                return;
            }

            switch (cell.Model)
            {
                case BuildingModel.Workshop:
                    BuildWorkshop(b, h, ref paletteRng, ref shapeRng, BuildingMeshFactory.CatalogVariant(x, z));
                    return;
                case BuildingModel.Warehouse:
                    BuildWarehouse(b, h, ref paletteRng, ref shapeRng, BuildingMeshFactory.CatalogVariant(x, z));
                    return;
                case BuildingModel.Factory:
                    BuildHeavyFactoryCatalog(b, h, ref paletteRng, BuildingMeshFactory.CatalogVariant(x, z));
                    return;
            }

            IndustrialArchetype archetype = PickZonedArchetype(x, z, h);
            switch (archetype)
            {
                case IndustrialArchetype.LogisticsHub:
                    BuildWarehouse(b, h, ref paletteRng, ref shapeRng);
                    break;
                case IndustrialArchetype.SawtoothPlant:
                    BuildSawtoothPlant(b, h, ref paletteRng);
                    break;
                case IndustrialArchetype.ChemicalTankFarm:
                    BuildChemicalPlant(b, x, z, h, ref paletteRng);
                    break;
                case IndustrialArchetype.BulkSilosMill:
                    BuildBulkSilosMill(b, h, ref paletteRng);
                    break;
                case IndustrialArchetype.HeavyFoundry:
                    BuildHeavyFoundry(b, h, ref paletteRng);
                    break;
                case IndustrialArchetype.CleanTechFab:
                    BuildCleanTechFab(b, h, ref paletteRng);
                    break;
            }
        }

        public static bool TryGetPlumeEmitter(
            int x, int z, CityCell cell, int quarterTurns, float cellSize,
            out Vector3 worldPos, out bool isCoolingSteam)
        {
            Vector3 localOffset;
            if (cell.Type == CellType.PowerPlant)
            {
                localOffset = new Vector3(-0.60f, 3.28f, -0.58f);
                isCoolingSteam = true;
            }
            else if (cell.Model == BuildingModel.Incinerator)
            {
                localOffset = new Vector3(1.14f, GarbageCatalog.IncineratorChimneyHeight, -1.12f);
                isCoolingSteam = false;
            }
            else if (cell.Type == CellType.Industrial && cell.Level > 0)
            {
                float h = Mathf.Max(1.8f, cell.Height);
                if (cell.Model == BuildingModel.Factory)
                {
                    localOffset = new Vector3(-1.18f, 0.04f + h * CatalogStackHeightRatio, -1.38f);
                    isCoolingSteam = false;
                }
                else if (cell.Model == BuildingModel.Default)
                {
                    IndustrialArchetype archetype = PickZonedArchetype(x, z, h);
                    if (archetype == IndustrialArchetype.HeavyFoundry)
                    {
                        localOffset = new Vector3(1.18f, 0.04f + h * 1.38f, -1.38f);
                        isCoolingSteam = false;
                    }
                    else if (archetype == IndustrialArchetype.ChemicalTankFarm && HasChemicalStack(x, z))
                    {
                        localOffset = new Vector3(-1.22f, 0.04f + h * 1.24f, -1.35f);
                        isCoolingSteam = false;
                    }
                    else
                    {
                        worldPos = default;
                        isCoolingSteam = false;
                        return false;
                    }
                }
                else
                {
                    worldPos = default;
                    isCoolingSteam = false;
                    return false;
                }
            }
            else
            {
                worldPos = default;
                isCoolingSteam = false;
                return false;
            }

            Vector3 rotated;
            switch (((quarterTurns % 4) + 4) % 4)
            {
                case 1:
                    rotated = new Vector3(localOffset.z, localOffset.y, -localOffset.x);
                    break;
                case 2:
                    rotated = new Vector3(-localOffset.x, localOffset.y, -localOffset.z);
                    break;
                case 3:
                    rotated = new Vector3(-localOffset.z, localOffset.y, localOffset.x);
                    break;
                default:
                    rotated = localOffset;
                    break;
            }

            worldPos = new Vector3((x + 0.5f) * cellSize, CityChunkRenderer.BuildingBase, (z + 0.5f) * cellSize) + rotated;
            return true;
        }

        private static IndustrialArchetype PickZonedArchetype(int x, int z, float h)
        {
            uint roll = BuildingMeshFactory.Hash(x, z, 313) % 10u;
            if (h < 2.8f)
            {
                // Lighter plants & logistics more common on low-height plots
                if (roll < 3) return IndustrialArchetype.LogisticsHub;
                if (roll < 5) return IndustrialArchetype.SawtoothPlant;
                if (roll < 7) return IndustrialArchetype.CleanTechFab;
                if (roll < 8) return IndustrialArchetype.BulkSilosMill;
                if (roll < 9) return IndustrialArchetype.ChemicalTankFarm;
                return IndustrialArchetype.HeavyFoundry;
            }

            if (roll < 2) return IndustrialArchetype.LogisticsHub;
            if (roll < 4) return IndustrialArchetype.SawtoothPlant;
            if (roll < 6) return IndustrialArchetype.ChemicalTankFarm;
            if (roll < 8) return IndustrialArchetype.BulkSilosMill;
            if (roll < 9) return IndustrialArchetype.HeavyFoundry;
            return IndustrialArchetype.CleanTechFab;
        }

        private static bool HasChemicalStack(int x, int z) =>
            (BuildingMeshFactory.Hash(x, z, 577) & 1u) == 0u;

        private static void BuildCompoundBase(MeshBuilder b)
        {
            b.AddBox(new Vector3(0f, 0.02f, 0f), new Vector3(3.70f, 0.04f, 3.70f), PadAsphalt, PadAsphalt);

            // Concrete truck apron at +Z front
            b.AddBox(new Vector3(0f, 0.024f, 1.18f), new Vector3(3.38f, 0.048f, 1.10f), ApronConcrete, ApronConcrete);

            // Yellow hazard apron edge stripes
            b.AddBox(new Vector3(-1.32f, 0.049f, 1.62f), new Vector3(0.45f, 0.008f, 0.06f), SafetyYellow, SafetyYellow);
            b.AddBox(new Vector3(1.32f, 0.049f, 1.62f), new Vector3(0.45f, 0.008f, 0.06f), SafetyYellow, SafetyYellow);

            // Low perimeter steel security fence along back (-Z) and rear side edges (z < 0.6)
            b.AddBox(new Vector3(0f, 0.17f, -1.78f), new Vector3(3.56f, 0.05f, 0.04f), SteelBeam, SteelBeam);
            b.AddBox(new Vector3(-1.78f, 0.17f, -0.60f), new Vector3(0.04f, 0.05f, 2.36f), SteelBeam, SteelBeam);
            b.AddBox(new Vector3(1.78f, 0.17f, -0.60f), new Vector3(0.04f, 0.05f, 2.36f), SteelBeam, SteelBeam);
            for (int i = -2; i <= 2; i++)
                b.AddBox(new Vector3(i * 0.88f, 0.12f, -1.78f), new Vector3(0.05f, 0.18f, 0.05f), SteelBeam, SteelBeam);
            for (int j = 0; j <= 2; j++)
            {
                float fz = -1.78f + j * 1.15f;
                b.AddBox(new Vector3(-1.78f, 0.12f, fz), new Vector3(0.05f, 0.18f, 0.05f), SteelBeam, SteelBeam);
                b.AddBox(new Vector3(1.78f, 0.12f, fz), new Vector3(0.05f, 0.18f, 0.05f), SteelBeam, SteelBeam);
            }
        }

        private static void BuildWorkshop(MeshBuilder b, float h, ref CellRng paletteRng, ref CellRng shapeRng, int variant)
        {
            const float baseY = 0.04f;
            Color32 hallWall = CladdingPalette[paletteRng.Range(0, 2) == 0 ? 0 : 4];
            Color32 officeBrick = CladdingPalette[3];
            float hallH = h * (variant == 2 ? 0.91f : 0.74f);

            // Fabrication hall on left/rear
            Vector3 hallCenter = new Vector3(-0.42f, baseY + hallH * 0.5f, -0.35f);
            Vector3 hallSize = new Vector3(2.15f, hallH, 2.05f);
            AddSteelHall(b, hallCenter, hallSize, hallWall, RoofDark, 3);
            var roof = new Vector3(hallCenter.x, baseY + hallH, hallCenter.z);
            if (variant == 0)
                b.AddGableRoof(roof, hallSize.x, hallSize.z, h * 0.19f, 0.06f, RoofDark, hallWall);
            else if (variant == 1)
                AddSawtoothSkylights(b, roof, hallSize.x, hallSize.z, h * 0.23f, 2, hallWall);
            else
                b.AddBox(roof + new Vector3(0.24f, h * 0.045f, -0.30f),
                    new Vector3(1.12f, h * 0.09f, 1.18f), SteelBeam, RoofLight, topSurface: BuildingSurface.Roof);
            AddLoadingDockRow(b, hallCenter, hallSize, 2, SafetyOrange);

            // Brick front office annex on right
            float offH = variant == 2 ? h * 0.63f : 1.15f;
            Vector3 offCenter = new Vector3(1.12f, baseY + offH * 0.5f, variant == 2 ? -0.18f : 0.15f);
            Vector3 offSize = new Vector3(1.05f, offH, 1.35f);
            b.AddBox(offCenter, offSize, officeBrick, RoofLight, wallSurface: BuildingSurface.Masonry, topSurface: BuildingSurface.Roof);
            b.AddFacadeRect(offCenter, offSize, 0, -0.38f, 0.38f, baseY + 0.52f, baseY + 0.92f, DarkGlass, BuildingSurface.Glazing);
            b.AddBox(new Vector3(1.12f, baseY + 0.48f, offCenter.z + 0.77f), new Vector3(0.68f, 0.06f, 0.24f), SteelBeam, SteelBeam);

            // Dust collector cyclone + ISO yard container
            AddStorageTank(b, new Vector3(-1.42f, baseY, 1.05f), 0.24f, 1.28f, TankSilver, SafetyOrange);
            if (shapeRng.Next() < 0.7f)
                AddIsoContainer(b, new Vector3(1.12f, baseY + 0.32f, -1.15f), 1.05f, 0, ContainerPalette[paletteRng.Range(0, ContainerPalette.Length)]);
        }

        private static void BuildWarehouse(MeshBuilder b, float h, ref CellRng paletteRng, ref CellRng shapeRng,
            int variant = -1)
        {
            const float baseY = 0.04f;
            Color32 wall = CladdingPalette[paletteRng.Range(0, 3)];
            Color32 accent = paletteRng.Next() < 0.5f ? LogisticsBlue : SafetyOrange;
            float hallH = variant < 0 ? Mathf.Clamp(h * 0.80f, 1.75f, 2.65f) :
                h * (variant == 0 ? 0.82f : 0.72f);

            Vector3 hallCenter = new Vector3(0f, baseY + hallH * 0.5f, -0.45f);
            Vector3 hallSize = new Vector3(3.16f, hallH, 2.02f);
            AddSteelHall(b, hallCenter, hallSize, wall, RoofLight, 4);

            // Horizontal accent fascia stripe & clerestory glazing
            b.AddFacadeRect(hallCenter, hallSize, 0, -1.52f, 1.52f, baseY + hallH - 0.34f, baseY + hallH - 0.16f, accent);
            b.AddFacadeRect(hallCenter, hallSize, 2, -0.94f, 0.94f, baseY + hallH - 0.44f, baseY + hallH - 0.18f, DarkGlass, BuildingSurface.Glazing);
            b.AddFacadeRect(hallCenter, hallSize, 3, -0.94f, 0.94f, baseY + hallH - 0.44f, baseY + hallH - 0.18f, DarkGlass, BuildingSurface.Glazing);

            // 3-bay loading dock on +Z
            AddLoadingDockRow(b, hallCenter, hallSize, 3, accent);

            // 2 rooftop HVAC chillers
            var roof = new Vector3(0f, baseY + hallH, hallCenter.z);
            if (variant == 1)
                AddBarrelRoof(b, roof, hallSize.x, hallSize.z, h * 0.25f, wall);
            else if (variant == 2)
                AddSawtoothSkylights(b, roof, hallSize.x - 0.12f, hallSize.z - 0.10f, h * 0.24f, 3, wall);
            else
            {
                AddRooftopHvac(b, new Vector3(-0.78f, baseY + hallH, -0.50f), 1.0f);
                AddRooftopHvac(b, new Vector3(0.74f, baseY + hallH, -0.35f), 0.9f);
            }

            // Stacked ISO containers on front-left apron
            int c1 = paletteRng.Range(0, ContainerPalette.Length);
            int c2 = (c1 + 1) % ContainerPalette.Length;
            AddIsoContainer(b, new Vector3(-1.12f, baseY + 0.30f, 1.18f), 1.10f, 1, ContainerPalette[c1]);
            if (shapeRng.Next() < 0.65f)
                AddIsoContainer(b, new Vector3(-1.12f, baseY + 0.90f, 1.18f), 1.06f, 1, ContainerPalette[c2]);
        }

        private static void BuildHeavyFactoryCatalog(MeshBuilder b, float h, ref CellRng paletteRng, int variant)
        {
            const float baseY = 0.04f;
            Color32 wall = CladdingPalette[paletteRng.Range(1, 4)];
            float hallH = h * (variant == 2 ? 0.76f : 0.62f);

            // High-bay main manufacturing hall shifted slightly right
            Vector3 hallCenter = new Vector3(0.32f, baseY + hallH * 0.5f, -0.32f);
            Vector3 hallSize = new Vector3(2.15f, hallH, 2.20f);
            AddSteelHall(b, hallCenter, hallSize, wall, RoofDark, 3);
            var roof = new Vector3(hallCenter.x, baseY + hallH, hallCenter.z);
            switch (variant)
            {
                case 1:
                    b.AddGableRoof(roof, hallSize.x, hallSize.z, h * 0.28f, 0.06f, RoofDark, wall);
                    AddStorageTank(b, new Vector3(-1.16f, baseY, 0.36f), 0.49f, h * 0.70f, TankSilver, LogisticsBlue);
                    break;
                case 2:
                    b.AddBox(roof + new Vector3(0, h * 0.105f, -0.20f), new Vector3(1.48f, h * 0.21f, 1.48f),
                        SteelBeam, RoofLight, topSurface: BuildingSurface.Roof);
                    b.AddBox(new Vector3(-1.10f, baseY + h * 0.24f, 0.38f), new Vector3(0.85f, h * 0.48f, 1.28f),
                        wall, RoofDark, wallSurface: BuildingSurface.Masonry);
                    break;
                default:
                    AddSawtoothSkylights(b, roof, 2.10f, 2.10f, h * 0.15f, 3, wall);
                    AddStorageTank(b, new Vector3(-1.22f, baseY, -0.25f), 0.36f, 2.15f, TankSilver, LogisticsBlue);
                    AddStorageTank(b, new Vector3(-1.22f, baseY, 0.62f), 0.36f, 1.95f, TankSilver, LogisticsBlue);
                    break;
            }
            AddLoadingDockRow(b, hallCenter, hallSize, 2, OxideRed);
            AddPipeRack(b, new Vector3(-1.22f, baseY, 0.15f), new Vector3(-0.65f, baseY, 0.15f), 1.65f, TankSilver, SteelBeam);
            AddTaperedStack(b, new Vector3(-1.18f, baseY, -1.38f), 0.24f, 0.14f,
                h * CatalogStackHeightRatio, SiloConcrete, OxideRed);
        }

        private static void BuildSawtoothPlant(MeshBuilder b, float h, ref CellRng paletteRng)
        {
            const float baseY = 0.04f;
            Color32 upperWall = CladdingPalette[paletteRng.Range(0, 3)];
            Color32 brickPlinth = CladdingPalette[3];
            float hallH = Mathf.Clamp(h * 0.58f, 1.55f, 2.25f);

            Vector3 hallCenter = new Vector3(0f, baseY + hallH * 0.5f, -0.38f);
            Vector3 hallSize = new Vector3(2.96f, hallH, 2.02f);
            AddSteelHall(b, hallCenter, hallSize, upperWall, RoofDark, 4);

            // Brick lower wainscot band
            b.AddBox(new Vector3(0f, baseY + 0.34f, -0.38f), new Vector3(3.01f, 0.44f, 2.07f),
                brickPlinth, brickPlinth, wallSurface: BuildingSurface.Masonry);

            // 3 north-light sawtooth bays
            AddSawtoothSkylights(b, new Vector3(0f, baseY + hallH, -0.38f), 2.90f, 1.96f, 0.54f, 3, upperWall);

            // Front office wing & side transformer
            Vector3 offCenter = new Vector3(-0.78f, baseY + 0.55f, 0.96f);
            Vector3 offSize = new Vector3(1.38f, 1.10f, 0.68f);
            b.AddBox(offCenter, offSize, brickPlinth, RoofLight, wallSurface: BuildingSurface.Masonry, topSurface: BuildingSurface.Roof);
            b.AddFacadeRect(offCenter, offSize, 0, -0.52f, 0.52f, baseY + 0.45f, baseY + 0.88f, DarkGlass, BuildingSurface.Glazing);

            AddLoadingDockRow(b, hallCenter, hallSize, 2, SafetyYellow);
            b.AddBox(new Vector3(1.18f, baseY + 0.34f, 1.12f), new Vector3(0.54f, 0.68f, 0.52f), SteelBeam, RoofLight);
        }

        private static void BuildChemicalPlant(MeshBuilder b, int x, int z, float h, ref CellRng paletteRng)
        {
            const float baseY = 0.04f;
            Color32 controlWall = CladdingPalette[paletteRng.Range(0, 2)];

            // Control/pump house on front-right
            Vector3 pumpCenter = new Vector3(0.86f, baseY + 0.68f, 0.48f);
            Vector3 pumpSize = new Vector3(1.32f, 1.36f, 1.48f);
            AddSteelHall(b, pumpCenter, pumpSize, controlWall, RoofDark, 2);
            b.AddFacadeRect(pumpCenter, pumpSize, 0, -0.45f, 0.45f, baseY + 0.72f, baseY + 1.12f, DarkGlass, BuildingSurface.Glazing);

            // Bunded tank farm on left/back
            b.AddBox(new Vector3(-0.78f, baseY + 0.11f, -0.25f), new Vector3(1.52f, 0.22f, 2.38f), BundConcrete, BundConcrete);
            AddStorageTank(b, new Vector3(-1.05f, baseY + 0.12f, -0.68f), 0.40f, 1.75f, TankSilver, LogisticsBlue);
            AddStorageTank(b, new Vector3(-0.44f, baseY + 0.12f, -0.78f), 0.32f, 2.10f, TankSilver, SafetyOrange);
            AddStorageTank(b, new Vector3(-0.82f, baseY + 0.12f, 0.36f), 0.46f, 1.45f, TankSilver, LogisticsBlue);

            // Process distillation column & overhead steel pipe rack
            AddStorageTank(b, new Vector3(0.88f, baseY, -1.02f), 0.24f, 2.75f, SiloConcrete, SafetyYellow);
            AddPipeRack(b, new Vector3(-0.65f, baseY, -0.25f), new Vector3(0.86f, baseY, -0.25f), 1.55f, TankSilver, SteelBeam);

            if (HasChemicalStack(x, z))
                AddTaperedStack(b, new Vector3(-1.22f, baseY, -1.35f), 0.16f, 0.10f, h * 1.24f, SteelBeam, SafetyOrange);
        }

        private static void BuildBulkSilosMill(MeshBuilder b, float h, ref CellRng paletteRng)
        {
            const float baseY = 0.04f;
            Color32 shedWall = CladdingPalette[paletteRng.Range(2, 6)];
            float siloH = Mathf.Clamp(h * 1.05f, 2.6f, 3.65f);

            // Row of 3 concrete silos along back
            for (int i = -1; i <= 1; i++)
                AddStorageTank(b, new Vector3(i * 0.90f, baseY, -0.78f), 0.42f, siloH, SiloConcrete, SafetyYellow);

            // Top conveyor headhouse gallery spanning across all 3 silos
            b.AddBox(new Vector3(0f, baseY + siloH + 0.20f, -0.78f), new Vector3(2.45f, 0.40f, 0.56f),
                RoofLight, RoofDark, wallSurface: BuildingSurface.Masonry, topSurface: BuildingSurface.Roof);

            // Adjacent grain/cement loading shed on front
            Vector3 shedCenter = new Vector3(-0.22f, baseY + 0.78f, 0.46f);
            Vector3 shedSize = new Vector3(2.38f, 1.56f, 1.18f);
            AddSteelHall(b, shedCenter, shedSize, shedWall, RoofDark, 3);
            AddLoadingDockRow(b, shedCenter, shedSize, 2, SafetyYellow);

            // Diagonal/elevated feed bridge from silos to shed
            AddPipeRack(b, new Vector3(0f, baseY, -0.55f), new Vector3(0f, baseY, 0.25f), 1.72f, RoofLight, SteelBeam);
        }

        private static void BuildHeavyFoundry(MeshBuilder b, float h, ref CellRng paletteRng)
        {
            const float baseY = 0.04f;
            Color32 steelWall = CladdingPalette[1];
            float hallH = Mathf.Clamp(h * 0.68f, 1.95f, 2.65f);

            // Heavy weathered steel high-bay hall
            Vector3 hallCenter = new Vector3(-0.28f, baseY + hallH * 0.5f, -0.32f);
            Vector3 hallSize = new Vector3(2.28f, hallH, 2.15f);
            AddSteelHall(b, hallCenter, hallSize, steelWall, RoofDark, 3);

            // Raised roof monitor lantern with glowing warm furnace slit
            Vector3 monCenter = new Vector3(-0.28f, baseY + hallH + 0.24f, -0.32f);
            Vector3 monSize = new Vector3(1.55f, 0.48f, 0.92f);
            b.AddBox(monCenter, monSize, RoofDark, RoofLight, topSurface: BuildingSurface.Roof);
            b.AddFacadeRect(monCenter, monSize, 0, -0.64f, 0.64f, baseY + hallH + 0.12f, baseY + hallH + 0.34f, FurnaceGlow);
            b.AddFacadeRect(monCenter, monSize, 1, -0.64f, 0.64f, baseY + hallH + 0.12f, baseY + hallH + 0.34f, FurnaceGlow);

            // Furnace louver slit & dock on front
            b.AddFacadeRect(hallCenter, hallSize, 0, -0.82f, -0.22f, baseY + 0.65f, baseY + 0.95f, FurnaceGlow);
            AddLoadingDockRow(b, hallCenter, hallSize, 1, OxideRed);

            // Side scrap yard with yellow gantry crane frame
            b.AddBox(new Vector3(1.22f, baseY + 0.18f, 0.55f), new Vector3(0.55f, 0.36f, 0.85f), OxideRed, SteelBeam);
            b.AddBox(new Vector3(0.95f, baseY + 0.72f, 0.55f), new Vector3(0.08f, 1.44f, 0.08f), SafetyYellow, SafetyYellow);
            b.AddBox(new Vector3(1.52f, baseY + 0.72f, 0.55f), new Vector3(0.08f, 1.44f, 0.08f), SafetyYellow, SafetyYellow);
            b.AddBox(new Vector3(1.24f, baseY + 1.46f, 0.55f), new Vector3(0.72f, 0.12f, 0.14f), SafetyYellow, SafetyYellow);

            // Tapered brick/concrete smokestack
            Color32 stackColor = paletteRng.Next() < 0.5f ? CladdingPalette[3] : SiloConcrete;
            AddTaperedStack(b, new Vector3(1.18f, baseY, -1.38f), 0.25f, 0.14f, h * 1.38f, stackColor, OxideRed);
        }

        private static void BuildCleanTechFab(MeshBuilder b, float h, ref CellRng paletteRng)
        {
            const float baseY = 0.04f;
            Color32 panelWhite = new Color32(214, 218, 222, 255);
            Color32 accent = paletteRng.Next() < 0.5f ? LogisticsBlue : new Color32(62, 152, 148, 255);
            float mainH = Mathf.Clamp(h * 0.74f, 1.65f, 2.55f);

            // Main crisp architectural fab block
            Vector3 hallCenter = new Vector3(0f, baseY + mainH * 0.5f, -0.30f);
            Vector3 hallSize = new Vector3(3.04f, mainH, 2.08f);
            AddSteelHall(b, hallCenter, hallSize, panelWhite, RoofLight, 4);

            // Wrap-around dark architectural glazing + accent trim
            float g0 = baseY + mainH * 0.42f;
            float g1 = baseY + mainH * 0.78f;
            b.AddFacadeRect(hallCenter, hallSize, 0, -1.38f, 1.38f, g0, g1, DarkGlass, BuildingSurface.Glazing);
            b.AddFacadeRect(hallCenter, hallSize, 2, -0.92f, 0.92f, g0, g1, DarkGlass, BuildingSurface.Glazing);
            b.AddFacadeRect(hallCenter, hallSize, 3, -0.92f, 0.92f, g0, g1, DarkGlass, BuildingSurface.Glazing);
            b.AddFacadeRect(hallCenter, hallSize, 0, -1.42f, 1.42f, baseY + mainH - 0.18f, baseY + mainH - 0.04f, accent);

            // Clean-room rooftop AHU penthouse & silver ducts
            b.AddBox(new Vector3(-0.35f, baseY + mainH + 0.24f, -0.35f), new Vector3(1.55f, 0.48f, 1.05f),
                RoofLight, RoofDark, topSurface: BuildingSurface.Roof);
            AddRooftopHvac(b, new Vector3(0.88f, baseY + mainH, -0.35f), 1.1f);
            AddLoadingDockRow(b, hallCenter, hallSize, 2, accent);
        }

        private static void BuildPowerPlant(MeshBuilder b)
        {
            const float baseY = 0.04f;
            Vector3 towerPos = new Vector3(-0.60f, baseY, -0.58f);

            // Dark water cooling basin + 8 open air-intake colonnade pillars
            b.AddCylinder(towerPos, 1.24f, 1.22f, 0.14f, 16, BasaltPlinth, true, new Color32(42, 66, 78, 255));
            for (int i = 0; i < 8; i++)
            {
                float a = i * (Mathf.PI * 2f / 8f);
                float px = towerPos.x + Mathf.Cos(a) * 1.10f;
                float pz = towerPos.z + Mathf.Sin(a) * 1.10f;
                b.AddBox(new Vector3(px, baseY + 0.28f, pz), new Vector3(0.11f, 0.34f, 0.11f), SiloConcrete, SiloConcrete);
            }

            // 16-segment hyperbolic cooling tower shell (3 smooth rings + stiffening top ring)
            b.AddCylinder(new Vector3(towerPos.x, baseY + 0.42f, towerPos.z), 1.18f, 0.86f, 1.12f, 16, SiloConcrete, false, default);
            b.AddCylinder(new Vector3(towerPos.x, baseY + 1.54f, towerPos.z), 0.86f, 0.82f, 0.86f, 16, SiloConcrete, false, default);
            b.AddCylinder(new Vector3(towerPos.x, baseY + 2.40f, towerPos.z), 0.82f, 0.92f, 0.80f, 16, SiloConcrete, false, default);
            b.AddCylinder(new Vector3(towerPos.x, baseY + 3.20f, towerPos.z), 0.96f, 0.96f, 0.08f, 16, ApronConcrete, true, FlueDark);

            // Turbine & boiler hall at front-right with muted violet-slate roof trim for PowerPlant readability
            Vector3 hallCenter = new Vector3(0.76f, baseY + 0.72f, 0.42f);
            Vector3 hallSize = new Vector3(1.68f, 1.44f, 1.85f);
            AddSteelHall(b, hallCenter, hallSize, CladdingPalette[2], PowerViolet, 3);
            b.AddFacadeRect(hallCenter, hallSize, 0, -0.68f, 0.68f, baseY + 0.96f, baseY + 1.28f, DarkGlass, BuildingSurface.Glazing);
            b.AddFacadeRect(hallCenter, hallSize, 2, -0.74f, 0.74f, baseY + 0.96f, baseY + 1.28f, DarkGlass, BuildingSurface.Glazing);
            AddLoadingDockRow(b, hallCenter, hallSize, 1, PowerViolet);

            // Outdoor high-voltage transformer bay at back-right + steel busbar portal
            b.AddBox(new Vector3(0.68f, baseY + 0.28f, -0.92f), new Vector3(0.40f, 0.56f, 0.44f), SteelBeam, TankSilver);
            b.AddBox(new Vector3(1.18f, baseY + 0.28f, -0.92f), new Vector3(0.40f, 0.56f, 0.44f), SteelBeam, TankSilver);
            b.AddBox(new Vector3(0.45f, baseY + 0.58f, -1.25f), new Vector3(0.06f, 1.16f, 0.06f), SteelBeam, SteelBeam);
            b.AddBox(new Vector3(1.38f, baseY + 0.58f, -1.25f), new Vector3(0.06f, 1.16f, 0.06f), SteelBeam, SteelBeam);
            b.AddBox(new Vector3(0.92f, baseY + 1.10f, -1.25f), new Vector3(0.98f, 0.06f, 0.06f), SteelBeam, SteelBeam);

            // Overhead steam pipe bridge from turbine hall to cooling tower
            AddPipeRack(b, new Vector3(-0.08f, baseY, 0.15f), new Vector3(0.32f, baseY, 0.15f), 1.38f, TankSilver, SteelBeam);

            // Tapered flue stack
            AddTaperedStack(b, new Vector3(1.32f, baseY, -1.38f), 0.22f, 0.13f, 4.15f, SiloConcrete, OxideRed);
        }

        private static void AddSteelHall(MeshBuilder b, Vector3 center, Vector3 size, Color32 wallColor, Color32 roofColor, int pilasterCount)
        {
            b.AddBox(center, size, wallColor, roofColor, wallSurface: BuildingSurface.Masonry, topSurface: BuildingSurface.Roof);

            // Dark concrete base plinth
            float baseY = center.y - size.y * 0.5f;
            b.AddBox(new Vector3(center.x, baseY + 0.13f, center.z),
                new Vector3(size.x + 0.04f, 0.26f, size.z + 0.04f), BasaltPlinth, BasaltPlinth);

            // Parapet coping overhang
            b.AddBox(new Vector3(center.x, baseY + size.y - 0.02f, center.z),
                new Vector3(size.x + 0.07f, 0.06f, size.z + 0.07f), SteelBeam, roofColor, topSurface: BuildingSurface.Roof);

            // Exterior vertical steel I-beam pilasters on front & back walls
            for (int i = 0; i < pilasterCount; i++)
            {
                float t = pilasterCount == 1 ? 0.5f : (float)i / (pilasterCount - 1);
                float px = center.x - size.x * 0.44f + t * size.x * 0.88f;
                b.AddBox(new Vector3(px, center.y, center.z + size.z * 0.5f + 0.02f),
                    new Vector3(0.08f, size.y - 0.04f, 0.07f), SteelBeam, SteelBeam);
                b.AddBox(new Vector3(px, center.y, center.z - size.z * 0.5f - 0.02f),
                    new Vector3(0.08f, size.y - 0.04f, 0.07f), SteelBeam, SteelBeam);
            }
        }

        private static void AddLoadingDockRow(MeshBuilder b, Vector3 hallCenter, Vector3 hallSize, int bays, Color32 accentColor)
        {
            float baseY = hallCenter.y - hallSize.y * 0.5f;
            float frontZ = hallCenter.z + hallSize.z * 0.5f;
            float baySpan = Mathf.Min(0.84f, (hallSize.x - 0.30f) / Mathf.Max(1, bays));
            float startX = hallCenter.x - (bays - 1) * baySpan * 0.5f;

            // Projecting steel rain canopy over dock row
            float canopyW = bays * baySpan + 0.28f;
            b.AddBox(new Vector3(hallCenter.x, baseY + 0.92f, frontZ + 0.18f),
                new Vector3(canopyW, 0.06f, 0.36f), SteelBeam, accentColor);

            for (int i = 0; i < bays; i++)
            {
                float bx = startX + i * baySpan;
                float u0 = bx - hallCenter.x - 0.28f;
                float u1 = bx - hallCenter.x + 0.28f;
                b.AddFacadeRect(hallCenter, hallSize, 0, u0, u1, baseY + 0.02f, baseY + 0.78f, DarkShutter);
                b.AddFacadeRect(hallCenter, hallSize, 0, u0 + 0.04f, u1 - 0.04f, baseY + 0.14f, baseY + 0.70f, ShutterMetal);

                // Black rubber dock bumpers & yellow truck bay guides
                b.AddBox(new Vector3(bx - 0.28f, baseY + 0.12f, frontZ + 0.04f), new Vector3(0.06f, 0.22f, 0.07f), DarkShutter, DarkShutter);
                b.AddBox(new Vector3(bx + 0.28f, baseY + 0.12f, frontZ + 0.04f), new Vector3(0.06f, 0.22f, 0.07f), DarkShutter, DarkShutter);
                b.AddBox(new Vector3(bx - 0.34f, baseY + 0.006f, frontZ + 0.38f), new Vector3(0.04f, 0.01f, 0.62f), SafetyYellow, SafetyYellow);
                b.AddBox(new Vector3(bx + 0.34f, baseY + 0.006f, frontZ + 0.38f), new Vector3(0.04f, 0.01f, 0.62f), SafetyYellow, SafetyYellow);
            }
        }

        private static void AddBarrelRoof(MeshBuilder b, Vector3 center, float width, float depth, float rise, Color32 endWall)
        {
            const int segments = 6;
            float halfWidth = width * 0.5f;
            Vector3 front = center + new Vector3(0, 0, depth * 0.5f);
            Vector3 back = center - new Vector3(0, 0, depth * 0.5f);
            Vector3 edge = new Vector3(-halfWidth, 0, 0);
            for (int i = 1; i <= segments; i++)
            {
                float angle = Mathf.PI * i / segments;
                Vector3 next = i == segments ? new Vector3(halfWidth, 0, 0) :
                    new Vector3(-Mathf.Cos(angle) * halfWidth, Mathf.Sin(angle) * rise, 0);
                b.AddQuad(front + edge, front + next, back + next, back + edge, Vector3.up, RoofLight, BuildingSurface.Roof);
                b.AddTriangle(front, front + edge, front + next, Vector3.forward, endWall, BuildingSurface.Masonry);
                b.AddTriangle(back, back + next, back + edge, Vector3.back, endWall, BuildingSurface.Masonry);
                edge = next;
            }
        }

        private static void AddSawtoothSkylights(MeshBuilder b, Vector3 roofCenter, float width, float depth, float height, int teeth, Color32 endWall)
        {
            float toothW = width / teeth;
            float z0 = roofCenter.z - depth * 0.5f;
            float z1 = roofCenter.z + depth * 0.5f;

            for (int i = 0; i < teeth; i++)
            {
                float x0 = roofCenter.x - width * 0.5f + i * toothW;
                float x1 = x0 + toothW;

                b.AddQuad(
                    new Vector3(x1, roofCenter.y, z1),
                    new Vector3(x1, roofCenter.y, z0),
                    new Vector3(x1, roofCenter.y + height, z0),
                    new Vector3(x1, roofCenter.y + height, z1),
                    Vector3.right, DarkGlass, BuildingSurface.Glazing);

                b.AddQuad(
                    new Vector3(x0, roofCenter.y, z1),
                    new Vector3(x1, roofCenter.y + height, z1),
                    new Vector3(x1, roofCenter.y + height, z0),
                    new Vector3(x0, roofCenter.y, z0),
                    new Vector3(-height, toothW, 0f), RoofDark, BuildingSurface.Roof);

                b.AddTriangle(new Vector3(x0, roofCenter.y, z1), new Vector3(x1, roofCenter.y, z1),
                    new Vector3(x1, roofCenter.y + height, z1), Vector3.forward, endWall, BuildingSurface.Masonry);
                b.AddTriangle(new Vector3(x1, roofCenter.y, z0), new Vector3(x0, roofCenter.y, z0),
                    new Vector3(x1, roofCenter.y + height, z0), Vector3.back, endWall, BuildingSurface.Masonry);
            }
        }

        private static void AddIsoContainer(MeshBuilder b, Vector3 center, float length, int quarterTurn, Color32 color)
        {
            float w = quarterTurn == 0 ? length : 0.58f;
            float d = quarterTurn == 0 ? 0.58f : length;
            var size = new Vector3(w, 0.56f, d);
            b.AddBox(center, size, color, color);
            b.AddBox(center + Vector3.up * 0.285f, new Vector3(w * 0.94f, 0.015f, d * 0.94f), Darken(color, 0.85f), Darken(color, 0.85f));
        }

        private static void AddRooftopHvac(MeshBuilder b, Vector3 roofPos, float scale)
        {
            b.AddBox(roofPos + new Vector3(0f, 0.14f * scale, 0f),
                new Vector3(0.62f * scale, 0.28f * scale, 0.48f * scale), RoofLight, RoofLight);
            b.AddCylinder(roofPos + new Vector3(-0.14f * scale, 0.28f * scale, 0f),
                0.13f * scale, 0.13f * scale, 0.04f * scale, 8, SteelBeam, true, DarkShutter);
            b.AddCylinder(roofPos + new Vector3(0.14f * scale, 0.28f * scale, 0f),
                0.13f * scale, 0.13f * scale, 0.04f * scale, 8, SteelBeam, true, DarkShutter);
            b.AddBox(roofPos + new Vector3(0f, 0.10f * scale, 0.34f * scale),
                new Vector3(0.16f * scale, 0.14f * scale, 0.26f * scale), TankSilver, TankSilver);
        }

        private static void AddStorageTank(MeshBuilder b, Vector3 basePos, float radius, float height, Color32 tankColor, Color32 bandColor)
        {
            b.AddCylinder(basePos, radius * 1.06f, radius * 1.06f, 0.12f, 12, BasaltPlinth, false, default);
            b.AddCylinder(new Vector3(basePos.x, basePos.y + 0.12f, basePos.z), radius, radius, height - 0.12f, 12, tankColor, true, RoofLight);
            b.AddCylinder(new Vector3(basePos.x, basePos.y + height - 0.22f, basePos.z),
                radius + 0.012f, radius + 0.012f, 0.10f, 12, bandColor, false, default);
        }

        private static void AddPipeRack(MeshBuilder b, Vector3 p0, Vector3 p1, float height, Color32 pipeColor, Color32 steelColor)
        {
            b.AddBox(new Vector3(p0.x, p0.y + height * 0.5f, p0.z), new Vector3(0.08f, height, 0.08f), steelColor, steelColor);
            b.AddBox(new Vector3(p1.x, p1.y + height * 0.5f, p1.z), new Vector3(0.08f, height, 0.08f), steelColor, steelColor);

            Vector3 mid = (p0 + p1) * 0.5f + Vector3.up * height;
            float dx = Mathf.Abs(p1.x - p0.x);
            float dz = Mathf.Abs(p1.z - p0.z);
            if (dx >= dz)
            {
                float span = Mathf.Max(0.2f, dx + 0.12f);
                b.AddBox(mid + new Vector3(0f, 0f, -0.09f), new Vector3(span, 0.09f, 0.09f), pipeColor, pipeColor);
                b.AddBox(mid + new Vector3(0f, 0f, 0.09f), new Vector3(span, 0.09f, 0.09f), pipeColor, pipeColor);
            }
            else
            {
                float span = Mathf.Max(0.2f, dz + 0.12f);
                b.AddBox(mid + new Vector3(-0.09f, 0f, 0f), new Vector3(0.09f, 0.09f, span), pipeColor, pipeColor);
                b.AddBox(mid + new Vector3(0.09f, 0f, 0f), new Vector3(0.09f, 0.09f, span), pipeColor, pipeColor);
            }
        }

        private static void AddTaperedStack(MeshBuilder b, Vector3 basePos, float rBottom, float rTop, float height, Color32 baseColor, Color32 stripeColor)
        {
            float pedH = 0.34f;
            float shaftH = Mathf.Max(0.8f, height - pedH);
            b.AddCylinder(basePos, rBottom * 1.32f, rBottom * 1.15f, pedH, 10, BasaltPlinth, false, default);
            b.AddCylinder(new Vector3(basePos.x, basePos.y + pedH, basePos.z),
                rBottom, rTop, shaftH, 10, baseColor, true, FlueDark);

            // Two subtle warning stripes near top
            float bandY1 = basePos.y + height - 0.50f;
            float bandY2 = basePos.y + height - 0.24f;
            float r1 = Mathf.Lerp(rBottom, rTop, (bandY1 - pedH) / shaftH) + 0.01f;
            float r2 = Mathf.Lerp(rBottom, rTop, (bandY2 - pedH) / shaftH) + 0.01f;
            b.AddCylinder(new Vector3(basePos.x, bandY1, basePos.z), r1, r1, 0.12f, 10, stripeColor, false, default);
            b.AddCylinder(new Vector3(basePos.x, bandY2, basePos.z), r2, r2, 0.12f, 10, stripeColor, false, default);
        }

        private static Color32 Darken(Color32 c, float factor) =>
            new Color32((byte)(c.r * factor), (byte)(c.g * factor), (byte)(c.b * factor), c.a);
    }
}
