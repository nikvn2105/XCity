using System;

namespace XCity
{
    internal static class ParkCatalog
    {
        internal readonly struct Park
        {
            public readonly string Name;
            public readonly int Cost;
            public readonly int Upkeep;
            public readonly int Radius;
            public readonly int Bonus;
            public readonly int UnlockPopulation;

            public Park(string name, int cost, int upkeep, int radius, int bonus, int unlockPopulation)
            {
                Name = name;
                Cost = cost;
                Upkeep = upkeep;
                Radius = radius;
                Bonus = bonus;
                UnlockPopulation = unlockPopulation;
            }
        }

        public static Park Get(BuildingModel model) => model switch
        {
            BuildingModel.SmallPark => new Park(UiText.SmallPark, 250, 5, 4, 4, 0),
            BuildingModel.Plaza => new Park(UiText.Plaza, 600, 12, 7, 7, 500),
            BuildingModel.LargePark => new Park(UiText.LargePark, 1250, 24, 10, 10, 2000),
            _ => default
        };

        public static bool IsPark(BuildingModel model) =>
            model >= BuildingModel.SmallPark && model <= BuildingModel.LargePark;

        public static (CellType Type, string Name, int Cost, float Height, int Seats) Building(BuildingModel model)
        {
            Park park = Get(model);
            return (CellType.CivicService, park.Name, park.Cost, 1.5f, 0);
        }
    }

    public sealed partial class CitySimulation
    {
        internal const int LandWaterRadius = 4;
        internal const int LandPollutionRadius = 5;
        private const int MaxParkBonus = 24;

        private const byte WaterKnown = 1;
        private const byte NearWater = 2;
        private const byte PollutionKnown = 4;
        private const byte NearPollution = 8;
        private const byte NearIncinerator = 16;

        // Unknown (or invalidated) flags are resolved only for cells somebody actually reads.
        private byte[] _landFeatures;
        private byte[] _terrainKinds;
        private ushort[] _parkReach;

        public int GetLandValue(int x, int z)
        {
            if (!IsInBounds(x, z)) return 0;

            int index = CellIndex(x, z);
            byte features = LandFeaturesAt(x, z, index);
            int value = 50 + 8 * ServiceCatalog.Count(GetCoverage(x, z)) + ParkInfluence(index);
            if ((features & NearWater) != 0) value += 12;
            if ((features & NearPollution) != 0) value -= 18;
            else if ((features & NearIncinerator) != 0) value -= 10;
            bool residential = _cells[x, z].Type == CellType.Residential;
            bool avenue = false, boulevard = false;
            CheckRoad(x - 1, z);
            CheckRoad(x + 1, z);
            CheckRoad(x, z - 1);
            CheckRoad(x, z + 1);
            if (avenue) value -= RoadCatalog.AvenueResidentialNoise;
            if (boulevard) value += RoadCatalog.BoulevardLandBonus;
            if (NeedsGarbageCollection(x, z)) value -= 10;
            if (GetTrafficLoad(x, z) > 100) value -= 12;
            return Math.Max(0, Math.Min(100, value));

            void CheckRoad(int nx, int nz)
            {
                if (!IsInBounds(nx, nz)) return;
                CityCell adjacent = _cells[nx, nz];
                if (adjacent.Type != CellType.Road) return;
                avenue |= residential && adjacent.RoadType == RoadType.Avenue;
                boulevard |= adjacent.RoadType == RoadType.Boulevard;
            }
        }

        internal int LandValueTaxPercent(int x, int z) => 50 + GetLandValue(x, z);

        /// <summary>
        /// True when this cell's building needs more land value to develop or level up. Existing levels
        /// are never lost: a drop below the threshold only stops further growth.
        /// </summary>
        public bool LandValueBlocksGrowth(int x, int z)
        {
            if (!IsInBounds(x, z)) return false;
            int needed = BuildingCatalog.MinLandValue(BuildingCatalog.DensityOf(_cells[x, z].Model));
            return needed > 0 && GetLandValue(x, z) < needed;
        }

        internal int ParkBonusAt(int x, int z) =>
            _parkReach != null && IsInBounds(x, z) && _cells[x, z].Type == CellType.Residential
                ? ParkInfluence(CellIndex(x, z)) : 0;

        private int ParkInfluence(int index) => _parkReach == null ? 0 : Math.Min(MaxParkBonus, (int)_parkReach[index]);

        private void TrackLandValueCell(int x, int z, CityCell previous, CityCell current)
        {
            if (previous.Model != current.Model)
            {
                if (ParkCatalog.IsPark(previous.Model)) StampPark(x, z, ParkCatalog.Get(previous.Model), -1);
                if (ParkCatalog.IsPark(current.Model)) StampPark(x, z, ParkCatalog.Get(current.Model), 1);
            }

            if (_landFeatures == null || PollutionFlag(previous) == PollutionFlag(current)) return;
            // Only this emitter's radius can have changed. The water flags describe immutable terrain.
            const int radius = LandPollutionRadius;
            int maxX = Math.Min(GridSize - 1, x + radius);
            int minZ = Math.Max(0, z - radius), maxZ = Math.Min(GridSize - 1, z + radius);
            for (int cx = Math.Max(0, x - radius); cx <= maxX; cx++)
            for (int cz = minZ; cz <= maxZ; cz++)
                if (InRadius(x, z, cx, cz, radius))
                    _landFeatures[CellIndex(cx, cz)] &= WaterKnown | NearWater;
        }

        private void StampPark(int x, int z, ParkCatalog.Park park, int direction)
        {
            _parkReach ??= new ushort[GridSize * GridSize];
            int delta = direction * park.Bonus;
            int radius = park.Radius;
            int maxX = Math.Min(GridSize - 1, x + radius);
            int minZ = Math.Max(0, z - radius), maxZ = Math.Min(GridSize - 1, z + radius);
            for (int cx = Math.Max(0, x - radius); cx <= maxX; cx++)
            for (int cz = minZ; cz <= maxZ; cz++)
            {
                if (!InRadius(x, z, cx, cz, radius)) continue;
                int index = CellIndex(cx, cz);
                _parkReach[index] = (ushort)(_parkReach[index] + delta);
            }
        }

        private byte LandFeaturesAt(int x, int z, int index)
        {
            _landFeatures ??= new byte[GridSize * GridSize];
            byte features = _landFeatures[index];
            if ((features & (WaterKnown | PollutionKnown)) == (WaterKnown | PollutionKnown)) return features;
            if ((features & WaterKnown) == 0)
            {
                features |= WaterKnown;
                if (LandFeatureWithin(x, z, LandWaterRadius, water: true) != 0) features |= NearWater;
            }
            if ((features & PollutionKnown) == 0)
            {
                features |= PollutionKnown;
                features |= LandFeatureWithin(x, z, LandPollutionRadius, water: false);
            }
            _landFeatures[index] = features;
            return features;
        }

        private byte LandFeatureWithin(int x, int z, int radius, bool water)
        {
            int maxX = Math.Min(GridSize - 1, x + radius);
            int minZ = Math.Max(0, z - radius), maxZ = Math.Min(GridSize - 1, z + radius);
            byte nearby = 0;
            for (int cx = Math.Max(0, x - radius); cx <= maxX; cx++)
            for (int cz = minZ; cz <= maxZ; cz++)
            {
                if (!InRadius(x, z, cx, cz, radius)) continue;
                if (water)
                {
                    if (IsWaterTerrain(cx, cz)) return NearWater;
                }
                else
                {
                    byte flag = PollutionFlag(_cells[cx, cz]);
                    if (flag == NearPollution) return flag;
                    if (flag == NearIncinerator) nearby = flag;
                }
            }
            return nearby;
        }

        private bool IsWaterTerrain(int x, int z)
        {
            _terrainKinds ??= new byte[GridSize * GridSize];
            int i = CellIndex(x, z);
            if (_terrainKinds[i] == 0)
                _terrainKinds[i] = WorldTerrain.IsLand(x, z) ? (byte)1 : (byte)2;
            return _terrainKinds[i] == 2;
        }

        private static bool InRadius(int x, int z, int cx, int cz, int radius)
        {
            int dx = cx - x;
            int dz = cz - z;
            return dx * dx + dz * dz <= radius * radius;
        }

        private static byte PollutionFlag(CityCell cell) =>
            cell.Type == CellType.Industrial || cell.Type == CellType.PowerPlant || cell.Model == BuildingModel.Landfill
                ? NearPollution : cell.Model == BuildingModel.Incinerator ? NearIncinerator : (byte)0;
    }
}
