using System;
using System.Collections.Generic;

namespace XCity
{
    public enum TerrainTool : byte { Raise, Lower, Level }

    public sealed partial class CitySimulation
    {
        public const int TerraformCostPerLevel = 50;
        private readonly Dictionary<int, byte> _terrainEdits = new Dictionary<int, byte>();

        internal event Action<int, int> TerrainChanged;
        internal int TerrainVersion { get; private set; }

        internal void ActivateTerrain() => WorldTerrain.Use(Map, _terrainEdits);

        internal static byte TerraformTarget(TerrainTool tool, byte current, byte anchor) => tool switch
        {
            TerrainTool.Raise => (byte)Math.Min(3, current + 1),
            TerrainTool.Lower => (byte)Math.Max(0, current - 1),
            _ => anchor
        };

        private static bool IsTerraformLand(int x, int z)
        {
            // Protect both cardinal banks and the corners used by shoreline rendering.
            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
                if (!WorldTerrain.IsLand(x + dx, z + dz)) return false;
            return true;
        }

        internal bool TryQuoteTerraform(int x, int z, byte target, out int cost,
            IReadOnlyDictionary<int, byte> planned = null)
        {
            cost = 0;
            if (IsBankrupt || target > 3 || !IsInBounds(x, z) ||
                _cells[x, z].Type != CellType.Empty || !IsTerraformLand(x, z)) return false;

            int difference = Math.Abs(target - WorldTerrain.Tier(x, z));
            if (difference == 0) return false;

            int changedIndex = CellIndex(x, z);
            // Every corner that samples this tile belongs to one of these touching cells.
            for (int nx = Math.Max(0, x - 1); nx <= Math.Min(GridSize - 1, x + 2); nx++)
            for (int nz = Math.Max(0, z - 1); nz <= Math.Min(GridSize - 1, z + 2); nz++)
            {
                CellType type = _cells[nx, nz].Type;
                if (type != CellType.Empty && type != CellType.Road &&
                    WorldTerrain.WouldBeSlope(nx, nz, changedIndex, target, planned))
                    return false;
            }
            cost = difference * TerraformCostPerLevel;
            return true;
        }

        public bool TryTerraform(IEnumerable<(int X, int Z)> points, TerrainTool tool)
        {
            if (IsBankrupt || tool > TerrainTool.Level) return false;
            using var stroke = points.GetEnumerator();
            if (!stroke.MoveNext()) return false;
            var first = stroke.Current;
            if (tool == TerrainTool.Level && !WorldTerrain.IsLand(first.X, first.Z)) return false;
            byte anchor = WorldTerrain.Tier(first.X, first.Z);
            var previous = new List<(int X, int Z, byte Tier)>();
            var seen = new HashSet<int>();
            int moneyBefore = Money;
            do
            {
                var (x, z) = stroke.Current;
                if (!IsInBounds(x, z) || !seen.Add(CellIndex(x, z))) continue;
                byte old = WorldTerrain.Tier(x, z);
                byte target = TerraformTarget(tool, old, anchor);
                if (!TryQuoteTerraform(x, z, target, out int cost)) continue;
                if (cost > Money) break;

                Money -= cost;
                previous.Add((x, z, old));
                SetTerrainTier(x, z, target);
            } while (stroke.MoveNext());

            if (previous.Count == 0) return false;
            PushUndo(null, moneyBefore - Money, previous);
            NotifyTerrainChanged(previous);
            return true;
        }

        private void SetTerrainTier(int x, int z, byte tier)
        {
            int index = CellIndex(x, z);
            if (WorldTerrain.SetTier(x, z, tier) == tier) _terrainEdits.Remove(index);
            else _terrainEdits[index] = tier;
        }

        private void NotifyTerrainChanged(List<(int X, int Z, byte Tier)> edits)
        {
            TerrainVersion++;
            _bridgeProfiles.Clear();
            var dirty = new HashSet<int>();
            var spans = new HashSet<int>();
            foreach (var (x, z, _) in edits)
            for (int nx = Math.Max(0, x - 1); nx <= Math.Min(GridSize - 1, x + 2); nx++)
            for (int nz = Math.Max(0, z - 1); nz <= Math.Min(GridSize - 1, z + 2); nz++)
            {
                if (!dirty.Add(CellIndex(nx, nz)) || !WorldTerrain.IsLand(nx, nz) || _cells[nx, nz].Type != CellType.Road)
                    continue;
                MarkSpan(nx, nz, -1, 0, BridgeAlongX);
                MarkSpan(nx, nz, 1, 0, BridgeAlongX);
                MarkSpan(nx, nz, 0, -1, BridgeAlongZ);
                MarkSpan(nx, nz, 0, 1, BridgeAlongZ);
            }
            dirty.UnionWith(spans);
            foreach (int index in dirty)
            {
                TerrainChanged?.Invoke(index / GridSize, index % GridSize);
                CellChanged?.Invoke(index / GridSize, index % GridSize);
            }

            void MarkSpan(int x, int z, int dx, int dz, RoadConnections axis)
            {
                x += dx;
                z += dz;
                while (IsInBounds(x, z) && _cells[x, z].BridgeAxis == axis && spans.Add(CellIndex(x, z)))
                {
                    x += dx;
                    z += dz;
                }
            }
        }

        private void WriteTerrainEdits(CitySnapshot snapshot)
        {
            if (_terrainEdits.Count == 0) return;
            snapshot.terrainIndices = new int[_terrainEdits.Count];
            _terrainEdits.Keys.CopyTo(snapshot.terrainIndices, 0);
            Array.Sort(snapshot.terrainIndices);
            snapshot.terrainTiers = new byte[snapshot.terrainIndices.Length];
            for (int i = 0; i < snapshot.terrainIndices.Length; i++)
                snapshot.terrainTiers[i] = _terrainEdits[snapshot.terrainIndices[i]];
        }

        private bool RestoreTerrainEdits(CitySnapshot snapshot)
        {
            if (snapshot.schemaVersion < 17 || (snapshot.terrainIndices == null && snapshot.terrainTiers == null)) return true;
            if (snapshot.terrainIndices == null || snapshot.terrainTiers == null ||
                snapshot.terrainIndices.Length != snapshot.terrainTiers.Length ||
                snapshot.terrainIndices.Length > GridSize * GridSize) return false;
            int previous = -1;
            for (int i = 0; i < snapshot.terrainIndices.Length; i++)
            {
                int index = snapshot.terrainIndices[i];
                byte tier = snapshot.terrainTiers[i];
                if (index <= previous || index >= GridSize * GridSize || tier > 3 ||
                    !IsTerraformLand(index / GridSize, index % GridSize)) return false;
                previous = index;
                SetTerrainTier(index / GridSize, index % GridSize, tier);
            }
            return true;
        }
    }
}
