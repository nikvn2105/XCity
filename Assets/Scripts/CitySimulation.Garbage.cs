using System;
using System.Collections.Generic;

namespace XCity
{
    internal static class GarbageCatalog
    {
        public const int MaxRadius = 16;
        public const float IncineratorChimneyHeight = 3.9f;

        public static bool IsFacility(BuildingModel model) =>
            model == BuildingModel.Landfill || model == BuildingModel.Incinerator;

        public static (string Name, int Cost, float Height, int Upkeep, int Radius, int Capacity, int Unlock) Get(BuildingModel model) =>
            model switch
            {
                BuildingModel.Landfill => (UiText.Landfill, 450, 1.6f, 18, 12, 120, 0),
                BuildingModel.Incinerator => (UiText.Incinerator, 3200, IncineratorChimneyHeight, 65, MaxRadius, 360, 2000),
                _ => default
            };

        public static (CellType Type, string Name, int Cost, float Height, int Seats) Building(BuildingModel model)
        {
            var facility = Get(model);
            return (CellType.CivicService, facility.Name, facility.Cost, facility.Height, 0);
        }
    }

    public sealed partial class CitySimulation
    {
        internal const int GarbageGracePopulation = 300;
        private static readonly (int X, int Z, int Distance)[] GarbageOffsets = CreateGarbageOffsets();

        private readonly SortedSet<int> _garbageFacilities = new SortedSet<int>();
        private readonly List<int> _garbageAssignments = new List<int>();
        private readonly Dictionary<int, int> _garbageUsed = new Dictionary<int, int>();
        private byte[] _garbageDemand;
        private bool[] _garbageServed;
        private bool _garbageDirty = true;
        private bool _garbageTickSnapshot;
        private int _garbageDemandTotal;
        private int _garbageProduced;
        private int _garbageCollected;

        public int GarbageProduced { get { RefreshGarbage(); return _garbageProduced; } }
        public int GarbageCollected { get { RefreshGarbage(); return _garbageCollected; } }

        public bool HasGarbageService(int x, int z)
        {
            if (!IsInBounds(x, z)) return false;
            RefreshGarbage();
            return _garbageServed != null && _garbageServed[CellIndex(x, z)];
        }

        internal bool ProducesGarbage(int x, int z) =>
            IsInBounds(x, z) && _garbageDemand != null && _garbageDemand[CellIndex(x, z)] > 0;

        internal bool NeedsGarbageCollection(int x, int z) =>
            Population >= GarbageGracePopulation && ProducesGarbage(x, z) && !HasGarbageService(x, z);

        internal (int Used, int Capacity) GarbageFacilityAt(int x, int z)
        {
            if (!IsInBounds(x, z)) return default;
            RefreshGarbage();
            int index = CellIndex(x, z);
            _garbageUsed.TryGetValue(index, out int used);
            return (used, FundedGarbageCapacity(_cells[x, z].Model));
        }

        internal int FundedGarbageCapacity(BuildingModel model) =>
            GarbageCatalog.Get(model).Capacity * GetFunding(BudgetService.Garbage) / 100;

        private static int GarbageUnits(CityCell cell)
        {
            if (cell.Level == 0) return 0;
            return cell.Type == CellType.Residential
                ? (BuildingCatalog.Residents(cell) + 3) / 4
                : (ServiceCatalog.Jobs(cell) + 2) / 3;
        }

        private void TrackGarbageCell(int index, CityCell previous, CityCell current)
        {
            if (previous.Model != current.Model)
            {
                if (GarbageCatalog.IsFacility(previous.Model)) _garbageFacilities.Remove(index);
                if (GarbageCatalog.IsFacility(current.Model)) _garbageFacilities.Add(index);
                if (GarbageCatalog.IsFacility(previous.Model) || GarbageCatalog.IsFacility(current.Model))
                    _garbageDirty = true;
            }
            TrackGarbageDemand(index, current);
        }

        private void TrackGarbageDemand(int index, CityCell current)
        {
            int units = GarbageUnits(current);
            int previous = _garbageDemand == null ? 0 : _garbageDemand[index];
            if (previous == units) return;
            _garbageDemand ??= new byte[GridSize * GridSize];
            _garbageDemand[index] = (byte)units;
            _garbageDemandTotal += units - previous;
            _garbageDirty = true;
        }

        private void RefreshGarbage()
        {
            // All reads within a tick share one assignment. Growth dirties the following refresh.
            if (_garbageTickSnapshot) return;
            if (_garbageFacilities.Count > 0) RefreshPower();
            if (!_garbageDirty) return;

            _garbageDirty = false;
            _garbageProduced = _garbageDemandTotal;
            _garbageCollected = 0;
            foreach (int index in _garbageAssignments) _garbageServed[index] = false;
            _garbageAssignments.Clear();
            _garbageUsed.Clear();
            if (_garbageProduced == 0 || _garbageFacilities.Count == 0) return;

            _garbageServed ??= new bool[GridSize * GridSize];
            foreach (int facility in _garbageFacilities)
            {
                int fx = facility / GridSize, fz = facility % GridSize;
                BuildingModel model = _cells[fx, fz].Model;
                if (model == BuildingModel.Incinerator && !_powered[facility]) continue;

                int radius = GarbageCatalog.Get(model).Radius;
                int radiusSquared = radius * radius;
                int capacity = FundedGarbageCapacity(model);
                int remaining = capacity;
                foreach (var offset in GarbageOffsets)
                {
                    if (offset.Distance > radiusSquared || remaining == 0) break;
                    int x = fx + offset.X, z = fz + offset.Z;
                    if (!IsInBounds(x, z)) continue;
                    int index = CellIndex(x, z);
                    int demand = _garbageDemand[index];
                    if (demand == 0 || demand > remaining || _garbageServed[index]) continue;

                    _garbageServed[index] = true;
                    _garbageAssignments.Add(index);
                    remaining -= demand;
                }
                int used = capacity - remaining;
                _garbageUsed[facility] = used;
                _garbageCollected += used;
            }
        }

        private static (int X, int Z, int Distance)[] CreateGarbageOffsets()
        {
            const int radius = GarbageCatalog.MaxRadius;
            var offsets = new List<(int X, int Z, int Distance)>();
            for (int x = -radius; x <= radius; x++)
            for (int z = -radius; z <= radius; z++)
            {
                int distance = x * x + z * z;
                if (distance <= radius * radius) offsets.Add((x, z, distance));
            }
            offsets.Sort((a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) :
                a.X != b.X ? a.X.CompareTo(b.X) : a.Z.CompareTo(b.Z));
            return offsets.ToArray();
        }
    }
}
