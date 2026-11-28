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

        private readonly float _bankStart;
        private readonly float _bankEnd;

        public BridgeProfile(RoadConnections axis, int index, int count, float bankStart = 0f, float bankEnd = 0f)
        {
            Axis = axis;
            Index = index;
            Count = count;
            _bankStart = bankStart;
            _bankEnd = bankEnd;
        }

        public RoadConnections Axis { get; }
        public int Index { get; }
        public int Count { get; }
        public bool HasNavigationOpening => Math.Abs(Index + 0.5f - Count * 0.5f) <= 1.5f;
        public float Start => Grade(Index);
        public float Middle => Grade(Index + 0.5f);
        public float End => Grade(Index + 1f);

        private float Grade(float offset) => TerrainDeck(offset, Count, _bankStart, _bankEnd);

        internal static float TerrainDeck(float offset, int count, float start, float end) =>
            start + (end - start) * (offset / count) + Rise(offset, count);

        internal static float Rise(float offset, int count) =>
            Math.Max(0f, Math.Min(MaxRise, Math.Min(offset, count - offset) * RisePerTile));

        public float HeightAt(float progress) => progress <= 0.5f
            ? Start + (Middle - Start) * (progress * 2f)
            : Middle + (End - Middle) * (progress * 2f - 1f);

        public float SlopeAt(float progress) => progress <= 0.5f
            ? (Middle - Start) * 2f : (End - Middle) * 2f;
    }

    internal readonly struct RoadGrade
    {
        // Ramp begins at the edge of the flat junction and meets the adjoining tile halfway in height.
        private const float RampStart = 0.21f;
        public const float SharedEdge = 0.40f; // Includes sidewalks and their pedestrian paths.
        public const int PatchCount = 5;
        public readonly float Base, West, East, South, North;

        public RoadGrade(float surface, float west = 0f, float east = 0f, float south = 0f, float north = 0f)
        {
            Base = surface;
            West = west;
            East = east;
            South = south;
            North = north;
        }

        public static float Boundary(int index) => index switch
        {
            0 => -0.5f, 1 => -SharedEdge, 2 => -RampStart,
            3 => RampStart, 4 => SharedEdge, _ => 0.5f
        };

        public static bool FlipDiagonal(int x, int z) => x == z && (x == 0 || x == PatchCount - 1);

        public bool IsRamp => West != 0f || East != 0f || South != 0f || North != 0f;

        public float Sample(float u, float v)
        {
            float x = Weight(u, RampStart), z = Weight(v, RampStart);
            float edgeX = u < 0f ? West : East, edgeZ = v < 0f ? South : North;
            // Keep the outer corner on its own terrace, with a diagonal crease matching the mesh.
            return Base + edgeX * Math.Min(x, 1f - Weight(v, SharedEdge)) +
                edgeZ * Math.Min(z, 1f - Weight(u, SharedEdge));
        }

        private static float Weight(float value, float threshold) =>
            Math.Max(0f, Math.Min(1f, (Math.Abs(value) - threshold) / (0.5f - threshold)));
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
            float bankStart = WorldTerrain.SurfaceY(x - dx, z - dz);
            float bankEnd = WorldTerrain.SurfaceY(x + dx * count, z + dz * count);
            for (int i = 0; i < count; i++)
                _bridgeProfiles[CellIndex(x + dx * i, z + dz * i)] = new BridgeProfile(axis, i, count, bankStart, bankEnd);

            profile = _bridgeProfiles[index];
            return true;
        }

        internal RoadGrade GetRoadGrade(int x, int z)
        {
            float surface = WorldTerrain.SurfaceY(x, z);
            if (!WorldTerrain.IsSlope(x, z) || !IsInBounds(x, z) || _cells[x, z].Type != CellType.Road)
                return new RoadGrade(surface);
            RoadConnections links = Roads.GetConnections(x, z);
            return new RoadGrade(surface, Edge(RoadConnections.West, x - 1, z), Edge(RoadConnections.East, x + 1, z),
                Edge(RoadConnections.South, x, z - 1), Edge(RoadConnections.North, x, z + 1));

            float Edge(RoadConnections flag, int nx, int nz) =>
                (links & flag) != 0 && WorldTerrain.IsLand(nx, nz)
                    ? (WorldTerrain.SurfaceY(nx, nz) - surface) * 0.5f : 0f;
        }

        internal float RoadSurfaceY(float worldX, float worldZ, float cellSize)
        {
            float gridX = worldX / cellSize, gridZ = worldZ / cellSize;
            int x = (int)Math.Floor(gridX), z = (int)Math.Floor(gridZ);
            if (!IsInBounds(x, z)) return 0f;
            if (TryGetBridgeProfile(x, z, out BridgeProfile bridge))
                return bridge.HeightAt(bridge.Axis == BridgeAlongX ? gridX - x : gridZ - z);
            return GetRoadGrade(x, z).Sample(gridX - x - 0.5f, gridZ - z - 0.5f);
        }

        internal BridgeQuote QuoteBridge(int x, int z, int endX, int endZ, out int cost, RoadType roadType = RoadType.Street)
        {
            cost = 0;
            if (roadType > RoadType.Boulevard) return BridgeQuote.SpanObstructed;
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
            RoadConnections axis = dx == 0 ? BridgeAlongZ : BridgeAlongX;
            for (int i = 0; i <= distance; i++)
            {
                int cx = x + i * dx, cz = z + i * dz;
                CityCell cell = _cells[cx, cz];
                bool bank = i == 0 || i == distance;
                if (!bank && (WorldTerrain.IsLand(cx, cz) || IsHarborBerth(cx, cz) ||
                    (cell.Type != CellType.Empty && cell.BridgeAxis != axis)))
                    return BridgeQuote.SpanObstructed;

                cost += cell.Type == CellType.Road
                    ? RoadCatalog.UpgradeCost(cell.RoadType, roadType)
                    : bank ? RoadCatalog.Cost(roadType) : BridgeTileCost + RoadCatalog.UpgradeCost(RoadType.Street, roadType);
            }

            return Money >= cost ? BridgeQuote.Ready : BridgeQuote.InsufficientFunds;
        }

        internal bool TryBuildBridge(int x, int z, int endX, int endZ, RoadType roadType = RoadType.Street)
        {
            if (IsBankrupt || QuoteBridge(x, z, endX, endZ, out int cost, roadType) != BridgeQuote.Ready) return false;

            int dx = Math.Sign(endX - x);
            int dz = Math.Sign(endZ - z);
            int distance = Math.Abs(endX - x) + Math.Abs(endZ - z);
            var changed = new List<(int X, int Z, CityCell Cell)>(distance + 1);
            RoadConnections exit = RoadCatalog.ExitForStep(roadType, dx, dz, RoadConnections.None);
            for (int i = 0; i <= distance; i++)
            {
                int cx = x + i * dx;
                int cz = z + i * dz;
                CityCell previous = _cells[cx, cz];
                if (previous.Type == CellType.Road && previous.RoadType == roadType && previous.OneWayExit == exit) continue;
                RoadConnections axis = i == 0 || i == distance ? RoadConnections.None :
                    dx == 0 ? BridgeAlongZ : BridgeAlongX;
                changed.Add((cx, cz, previous));
                _cells[cx, cz] = new CityCell { Type = CellType.Road, BridgeAxis = axis, RoadType = roadType, OneWayExit = exit };
                TrackCell(cx, cz, CellType.Road, previous);
            }

            if (changed.Count == 0) return false;
            PushUndo(changed, cost);
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

            Money -= AffordableDemolition(changed.Count - firstChange);
            _powerDirty = true;
            for (int i = firstChange; i < changed.Count; i++)
                CellChanged?.Invoke(changed[i].X, changed[i].Z);
            return true;
        }

        internal bool CanPlaceWaterfront(int x, int z, BuildingModel model) =>
            IsBuildable(x, z) && TryHarborFacing(x, z, out _, out int waterX, out int waterZ, seaOnly: model == BuildingModel.Harbor) &&
            !_cells[waterX, waterZ].IsBridge;

        internal static bool TryHarborFacing(int x, int z, out int turn, out int waterX, out int waterZ, bool seaOnly = true)
        {
            for (turn = 0; turn < 4; turn++)
            {
                waterX = x + (turn == 1 ? 1 : turn == 3 ? -1 : 0);
                waterZ = z + (turn == 0 ? 1 : turn == 2 ? -1 : 0);
                if (!IsInBounds(waterX, waterZ)) continue;
                WaterKind kind = WorldTerrain.WaterKind(waterX, waterZ);
                if (kind == WaterKind.Sea || (!seaOnly && kind != WaterKind.Land))
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
