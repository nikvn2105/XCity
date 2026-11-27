using System;
using System.Collections.Generic;

namespace XCity
{
    internal enum BridgeQuote
    {
        Ready,
        BanksRequired,
        StraightSpanRequired,
        SpanObstructed,
        TooLong,
        InsufficientFunds
    }

    internal readonly struct BridgeProfile
    {
        internal const float MaxRise = 3.0f;
        internal const float DeckThickness = 0.25f;
        internal const float WaterSurface = -1.05f;
        private const float RisePerTile = 0.85f;

        public BridgeProfile(RoadConnections axis, int index, int count)
        {
            Axis = axis;
            Index = index;
            Count = count;
        }

        public RoadConnections Axis { get; }
        public int Index { get; }
        public int Count { get; }
        public bool HasNavigationOpening => Math.Abs(Index + 0.5f - Count * 0.5f) <= 1.5f;
        public float Start => Rise(Index, Count);
        public float Middle => Rise(Index + 0.5f, Count);
        public float End => Rise(Index + 1f, Count);

        internal static float Rise(float offset, int count) =>
            Math.Max(0f, Math.Min(MaxRise, Math.Min(offset, count - offset) * RisePerTile));

        public float HeightAt(float progress) => progress <= 0.5f
            ? Start + (Middle - Start) * (progress * 2f)
            : Middle + (End - Middle) * (progress * 2f - 1f);

        public float SlopeAt(float progress) => progress <= 0.5f
            ? (Middle - Start) * 2f : (End - Middle) * 2f;
    }

    public sealed partial class CitySimulation
    {
        internal const int MaxBridgeWaterTiles = 96;
        internal const int BridgeTileCost = 180;
        internal const RoadConnections BridgeAlongX = RoadConnections.West | RoadConnections.East;
        internal const RoadConnections BridgeAlongZ = RoadConnections.South | RoadConnections.North;
        private readonly Dictionary<int, BridgeProfile> _bridgeProfiles = new Dictionary<int, BridgeProfile>();
        private int _bridgeProfilesRoadVersion = -1;

        internal bool TryGetBridgeProfile(int x, int z, out BridgeProfile profile)
        {
            RoadConnections axis = _cells[x, z].BridgeAxis;
            profile = default;
            if (axis == RoadConnections.None) return false;

            if (_bridgeProfilesRoadVersion != Roads.Version)
            {
                _bridgeProfilesRoadVersion = Roads.Version;
                _bridgeProfiles.Clear();
            }
            int index = CellIndex(x, z);
            if (_bridgeProfiles.TryGetValue(index, out profile)) return true;

            int dx = axis == BridgeAlongX ? 1 : 0;
            int dz = axis == BridgeAlongZ ? 1 : 0;
            while (IsInBounds(x - dx, z - dz) && _cells[x - dx, z - dz].BridgeAxis == axis)
            {
                x -= dx;
                z -= dz;
            }

            int count = 1;
            while (IsInBounds(x + dx * count, z + dz * count) &&
                _cells[x + dx * count, z + dz * count].BridgeAxis == axis)
                count++;
            for (int i = 0; i < count; i++)
                _bridgeProfiles[CellIndex(x + dx * i, z + dz * i)] = new BridgeProfile(axis, i, count);

            profile = _bridgeProfiles[index];
            return true;
        }

        internal BridgeQuote QuoteBridge(int x, int z, int endX, int endZ, out int cost)
        {
            cost = 0;
            if (!IsBuildable(x, z) || !IsBuildable(endX, endZ)) return BridgeQuote.BanksRequired;
            if (x != endX && z != endZ) return BridgeQuote.StraightSpanRequired;
            int distance = Math.Abs(endX - x) + Math.Abs(endZ - z);
            if (distance < 2) return BridgeQuote.BanksRequired;
            if (distance > MaxBridgeWaterTiles + 1) return BridgeQuote.TooLong;
            CellType start = _cells[x, z].Type;
            CellType end = _cells[endX, endZ].Type;
            if (!CanUseBridgeBank(start) || !CanUseBridgeBank(end)) return BridgeQuote.SpanObstructed;

            int dx = Math.Sign(endX - x);
            int dz = Math.Sign(endZ - z);
            for (int i = 1; i < distance; i++)
            {
                int cx = x + i * dx;
                int cz = z + i * dz;
                if (WorldTerrain.IsLand(cx, cz) || _cells[cx, cz].Type != CellType.Empty || IsHarborBerth(cx, cz))
                    return BridgeQuote.SpanObstructed;
            }

            cost = (distance - 1) * BridgeTileCost + (start == CellType.Empty ? RoadCost : 0) +
                (end == CellType.Empty ? RoadCost : 0);
            return Money >= cost ? BridgeQuote.Ready : BridgeQuote.InsufficientFunds;
        }

        internal bool TryBuildBridge(int x, int z, int endX, int endZ)
        {
            if (QuoteBridge(x, z, endX, endZ, out int cost) != BridgeQuote.Ready) return false;

            int dx = Math.Sign(endX - x);
            int dz = Math.Sign(endZ - z);
            int distance = Math.Abs(endX - x) + Math.Abs(endZ - z);
            var changed = new List<(int X, int Z, CityCell Cell)>(distance + 1);
            for (int i = 0; i <= distance; i++)
            {
                int cx = x + i * dx;
                int cz = z + i * dz;
                if (_cells[cx, cz].Type == CellType.Road) continue;
                RoadConnections axis = i == 0 || i == distance ? RoadConnections.None :
                    dx == 0 ? BridgeAlongZ : BridgeAlongX;
                changed.Add((cx, cz, _cells[cx, cz]));
                _cells[cx, cz] = new CityCell { Type = CellType.Road, BridgeAxis = axis };
                TrackCell(cx, cz, CellType.Road);
            }

            _undoCells = changed;
            _undoMoney = Money;
            _undoPopulation = Population;
            Money -= cost;
            _powerDirty = true;
            foreach (var entry in changed)
                CellChanged?.Invoke(entry.X, entry.Z);
            return true;
        }

        private bool RemoveBridge(int x, int z, List<(int X, int Z, CityCell Cell)> changed)
        {
            RoadConnections axis = _cells[x, z].BridgeAxis;
            int dx = axis == BridgeAlongX ? 1 : 0;
            int dz = axis == BridgeAlongZ ? 1 : 0;
            while (IsInBounds(x - dx, z - dz) && _cells[x - dx, z - dz].BridgeAxis == axis)
            {
                x -= dx;
                z -= dz;
            }

            int firstChange = changed.Count;
            while (IsInBounds(x, z) && _cells[x, z].BridgeAxis == axis)
            {
                changed.Add((x, z, _cells[x, z]));
                _cells[x, z] = default;
                TrackCell(x, z, CellType.Empty);
                x += dx;
                z += dz;
            }

            Money -= 10 * (changed.Count - firstChange);
            _powerDirty = true;
            for (int i = firstChange; i < changed.Count; i++)
                CellChanged?.Invoke(changed[i].X, changed[i].Z);
            return true;
        }

        internal bool CanPlaceHarbor(int x, int z) =>
            IsBuildable(x, z) && TryHarborFacing(x, z, out _, out int waterX, out int waterZ) &&
            !_cells[waterX, waterZ].IsBridge;

        internal static bool TryHarborFacing(int x, int z, out int turn, out int waterX, out int waterZ)
        {
            for (turn = 0; turn < 4; turn++)
            {
                waterX = x + (turn == 1 ? 1 : turn == 3 ? -1 : 0);
                waterZ = z + (turn == 0 ? 1 : turn == 2 ? -1 : 0);
                if (IsInBounds(waterX, waterZ) && !WorldTerrain.IsLand(waterX, waterZ))
                    return true;
            }
            waterX = x;
            waterZ = z;
            return false;
        }

        private bool IsHarborBerth(int x, int z) =>
            FacesHarborBerth(x - 1, z, x, z) || FacesHarborBerth(x + 1, z, x, z) ||
            FacesHarborBerth(x, z - 1, x, z) || FacesHarborBerth(x, z + 1, x, z);

        private bool FacesHarborBerth(int harborX, int harborZ, int x, int z) =>
            IsInBounds(harborX, harborZ) && _cells[harborX, harborZ].Model == BuildingModel.Harbor &&
            TryHarborFacing(harborX, harborZ, out _, out int berthX, out int berthZ) && berthX == x && berthZ == z;

        private static bool CanUseBridgeBank(CellType type) => type == CellType.Empty || type == CellType.Road;
    }
}
