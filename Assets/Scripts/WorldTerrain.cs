using System;
using System.Collections.Generic;

namespace XCity
{
    internal readonly struct WorldView
    {
        private const int Step = 8;
        private const int Radius = 17;
        private readonly int _anchorX;
        private readonly int _anchorZ;

        public WorldView(int x, int z)
        {
            _anchorX = x / Step * Step;
            _anchorZ = z / Step * Step;
        }

        public int MinX => Math.Max(0, _anchorX - Radius);
        public int MaxX => Math.Min(CitySimulation.GridSize - 1, _anchorX + Step + Radius);
        public int MinZ => Math.Max(0, _anchorZ - Radius);
        public int MaxZ => Math.Min(CitySimulation.GridSize - 1, _anchorZ + Step + Radius);

        public bool Contains(int x, int z) => x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;
        public bool SameAnchor(WorldView other) => _anchorX == other._anchorX && _anchorZ == other._anchorZ;
    }

    public enum MapStyle
    {
        Classic,   // the original archipelago; every save before schema 14 uses it
        Continent,
        Islands
    }

    public readonly struct MapSpec : IEquatable<MapSpec>
    {
        public readonly MapStyle Style;
        public readonly int Seed;

        public MapSpec(MapStyle style, int seed)
        {
            Style = style;
            Seed = style == MapStyle.Classic ? 0 : seed;
        }

        public static MapSpec Classic => new MapSpec(MapStyle.Classic, 0);
        public bool Equals(MapSpec other) => Style == other.Style && Seed == other.Seed;
        public override bool Equals(object obj) => obj is MapSpec other && Equals(other);
        public override int GetHashCode() => ((int)Style * 397) ^ Seed;
        public static bool operator ==(MapSpec a, MapSpec b) => a.Equals(b);
        public static bool operator !=(MapSpec a, MapSpec b) => !a.Equals(b);
    }

    public enum WaterKind : byte { Land, Sea, River, Lake }

    internal readonly struct TerrainPatch
    {
        private readonly float _sw, _se, _nw, _ne;

        public TerrainPatch(float sw, float se, float nw, float ne)
        {
            _sw = sw;
            _se = se;
            _nw = nw;
            _ne = ne;
        }

        public bool IsSlope => _sw != _se || _sw != _nw || _sw != _ne;

        public float Sample(float u, float v) => Lerp(Lerp(_sw, _se, u), Lerp(_nw, _ne, u), v);
        public float DeltaX(float v) => Lerp(_se - _sw, _ne - _nw, v);
        public float DeltaZ(float u) => Lerp(_nw - _sw, _ne - _se, u);

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }

    internal static class WorldTerrain
    {
        private const int Size = CitySimulation.GridSize;
        // Signed land height per cell (>= 0 is land), computed once per map.
        private static WaterKind[] _water;
        private static int[] _downstream;
        private static byte[] _tiers;
        private static float[] _corners = new float[(Size + 1) * (Size + 1)];
        private static float[] _height = MapGenerator.Generate(MapSpec.Classic, out _start, out _water, out _downstream, out _tiers);
        private static (int X, int Z) _start;
        private static readonly Dictionary<int, byte> OriginalTiers = new Dictionary<int, byte>();
        private static readonly object Gate = new object();

        public static MapSpec Current { get; private set; } = MapSpec.Classic;
        internal static bool HasTerraces => Current.Style != MapStyle.Classic || OriginalTiers.Count != 0;

        /// <summary>Centre of the guaranteed buildable start area (for the first camera position).</summary>
        public static (int X, int Z) Start => _start;

        /// <summary>
        /// Activates a map and its city-specific terrain edits before building a scene or restoring a city.
        /// </summary>
        public static void Use(MapSpec spec, IReadOnlyDictionary<int, byte> edits = null)
        {
            lock (Gate)
            {
                if (spec == Current)
                {
                    foreach (var item in OriginalTiers) _tiers[item.Key] = item.Value;
                    foreach (var item in OriginalTiers) RefreshEditedCorners(item.Key / Size, item.Key % Size);
                }
                else
                {
                    _height = MapGenerator.Generate(spec, out _start, out _water, out _downstream, out _tiers);
                    _corners = spec.Style == MapStyle.Classic ? new float[(Size + 1) * (Size + 1)] :
                        MapGenerator.BuildSurfaceCorners(_tiers, _water);
                    Current = spec;
                }
                OriginalTiers.Clear();
                if (edits == null) return;
                foreach (var item in edits) SetTier(item.Key / Size, item.Key % Size, item.Value);
            }
        }

        internal static Dictionary<int, byte> CopyEdits()
        {
            if (OriginalTiers.Count == 0) return null;
            var edits = new Dictionary<int, byte>(OriginalTiers.Count);
            foreach (int index in OriginalTiers.Keys) edits.Add(index, _tiers[index]);
            return edits;
        }

        internal static byte SetTier(int x, int z, byte tier)
        {
            int index = x * Size + z;
            if (!OriginalTiers.TryGetValue(index, out byte original)) original = _tiers[index];
            if (tier == original) OriginalTiers.Remove(index);
            else OriginalTiers[index] = original;
            _tiers[index] = tier;
            RefreshEditedCorners(x, z);
            return original;
        }

        // The corner kernel samples the inclusive offsets [-2, 0] in both axes.
        // Only these nine corners and their touching land tiles can change.
        private static void RefreshEditedCorners(int x, int z)
        {
            for (int cx = x; cx <= Math.Min(Size, x + 2); cx++)
            for (int cz = z; cz <= Math.Min(Size, z + 2); cz++)
                _corners[cx * (Size + 1) + cz] = MapGenerator.SurfaceCorner(_tiers, _water, cx, cz);
        }

        internal static bool WouldBeSlope(int x, int z, int changedIndex, byte tier, IReadOnlyDictionary<int, byte> planned)
        {
            float sw = Corner(x, z);
            return sw != Corner(x + 1, z) || sw != Corner(x, z + 1) || sw != Corner(x + 1, z + 1);

            float Corner(int cx, int cz) => MapGenerator.SurfaceCorner(_tiers, _water, cx, cz, changedIndex, tier, planned);
        }

        public static bool IsLand(int x, int z) =>
            x >= 0 && z >= 0 && x < Size && z < Size && _height[x * Size + z] >= 0f;

        internal const float TerraceStep = 2f;
        internal const float MaxSurfaceY = 3 * TerraceStep;

        public static byte Tier(int x, int z) =>
            (uint)x < Size && (uint)z < Size ? _tiers[x * Size + z] : (byte)0;

        public static float CornerY(int cx, int cz) =>
            (uint)cx <= Size && (uint)cz <= Size ? _corners[cx * (Size + 1) + cz] : 0f;

        internal static TerrainPatch GetSurface(int x, int z)
        {
            if ((uint)x >= Size || (uint)z >= Size) return default;
            if (!IsLand(x, z))
            {
                // Water has a level per tile; the terrain lattice describes land, not waterfalls.
                float level = Tier(x, z) * TerraceStep;
                return new TerrainPatch(level, level, level, level);
            }
            int index = x * (Size + 1) + z;
            return new TerrainPatch(_corners[index], _corners[index + Size + 1], _corners[index + 1], _corners[index + Size + 2]);
        }

        public static float SurfaceY(int x, int z) => GetSurface(x, z).Sample(0.5f, 0.5f);

        public static float SurfaceY(float worldX, float worldZ, float cellSize)
        {
            float u = worldX / cellSize, v = worldZ / cellSize;
            int x = (int)Math.Floor(u), z = (int)Math.Floor(v);
            return GetSurface(x, z).Sample(u - x, v - z);
        }

        public static bool IsSlope(int x, int z) => GetSurface(x, z).IsSlope;

        public static float LandHeight(int x, int z) =>
            x >= 0 && z >= 0 && x < Size && z < Size ? _height[x * Size + z] : -1f;

        public static WaterKind WaterKind(int x, int z) =>
            x >= 0 && z >= 0 && x < Size && z < Size ? _water[x * Size + z] : XCity.WaterKind.Sea;

        public static bool IsFreshWater(int x, int z) =>
            WaterKind(x, z) is XCity.WaterKind.River or XCity.WaterKind.Lake;

        // The next river cell, packed as x * GridSize + z; -1 at the mouth or off the river.
        public static int Downstream(int x, int z) =>
            WaterKind(x, z) == XCity.WaterKind.River ? _downstream[x * Size + z] : -1;

        // The original archipelago formula, kept exact so seed-less saves load unchanged.
        internal static float ClassicHeight(int x, int z)
        {
            float nx = (x + 0.5f) / Size;
            float nz = (z + 0.5f) / Size;
            float mainland = Island(nx, nz, 0.49f, 0.54f, 0.34f, 0.37f);
            float west = Island(nx, nz, 0.30f, 0.40f, 0.22f, 0.18f);
            float south = Island(nx, nz, 0.58f, 0.27f, 0.20f, 0.19f);
            float east = Island(nx, nz, 0.87f, 0.50f, 0.10f, 0.20f);
            float northwest = Island(nx, nz, 0.15f, 0.76f, 0.10f, 0.13f);
            float southeast = Island(nx, nz, 0.82f, 0.17f, 0.11f, 0.093f);
            float land = Math.Max(Math.Max(Math.Max(mainland, west), south), Math.Max(east, Math.Max(northwest, southeast)));
            float shape = 0.36f * (Noise(x, z, 51) - 0.5f) + 0.16f * (Noise(x + 27, z + 63, 15) - 0.5f);
            float outerWest = Island(nx, nz, 0.097f, 0.18f, 0.052f, 0.066f);
            float outerEast = Island(nx, nz, 0.89f, 0.81f, 0.057f, 0.076f);
            float offshore = Math.Max(outerWest, outerEast) + shape * 1.4f;
            return Math.Min(Math.Max(land + shape, offshore), EdgeLimit(nx, nz));
        }

        // Keeps a ring of sea around every map.
        internal static float EdgeLimit(float nx, float nz) =>
            (Math.Min(Math.Min(nx, 1f - nx), Math.Min(nz, 1f - nz)) - 0.022f) * 12f;

        public static float Vegetation(int x, int z) =>
            0.66f * Noise(x + 41, z + 17, 48) + 0.34f * Noise(x + 75, z + 12, 11);

        internal static float Island(float x, float z, float cx, float cz, float rx, float rz)
        {
            float dx = (x - cx) / rx;
            float dz = (z - cz) / rz;
            return 1f - dx * dx - dz * dz;
        }

        internal static float Noise(int x, int z, int span)
        {
            int sx = x / span;
            int sz = z / span;
            float tx = (x - sx * span) / (float)span;
            float tz = (z - sz * span) / (float)span;
            tx = tx * tx * (3f - 2f * tx);
            tz = tz * tz * (3f - 2f * tz);
            float a = Lerp(Hash(sx, sz), Hash(sx + 1, sz), tx);
            float b = Lerp(Hash(sx, sz + 1), Hash(sx + 1, sz + 1), tx);
            return Lerp(a, b, tz);
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        internal static float Hash(int x, int z)
        {
            unchecked
            {
                uint value = (uint)x * 0x8DA6B343u ^ (uint)z * 0xD8163841u;
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                return (value & 65535u) / 65535f;
            }
        }
    }
}
