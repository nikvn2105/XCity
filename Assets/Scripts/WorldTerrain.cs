using System;

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

    internal static class WorldTerrain
    {
        private const int Size = CitySimulation.GridSize;
        // Signed land height per cell (>= 0 is land), computed once per map.
        private static WaterKind[] _water;
        private static int[] _downstream;
        private static byte[] _tiers;
        private static float[] _height = MapGenerator.Generate(MapSpec.Classic, out _start, out _water, out _downstream, out _tiers);
        private static (int X, int Z) _start;
        private static readonly object Gate = new object();

        public static MapSpec Current { get; private set; } = MapSpec.Classic;

        /// <summary>Centre of the guaranteed buildable start area (for the first camera position).</summary>
        public static (int X, int Z) Start => _start;

        /// <summary>
        /// Switches the world to another map. Call before building a scene or restoring a city on it;
        /// cities and renderers built earlier keep reading the old map's cells until rebuilt.
        /// </summary>
        public static void Use(MapSpec spec)
        {
            lock (Gate)
            {
                if (spec == Current) return;
                _height = MapGenerator.Generate(spec, out _start, out _water, out _downstream, out _tiers);
                Current = spec;
            }
        }

        public static bool IsLand(int x, int z) =>
            x >= 0 && z >= 0 && x < Size && z < Size && _height[x * Size + z] >= 0f;

        internal const float TerraceStep = 2f;
        internal const float MaxSurfaceY = 3 * TerraceStep;

        public static byte Tier(int x, int z) =>
            (uint)x < Size && (uint)z < Size ? _tiers[x * Size + z] : (byte)0;

        public static float SurfaceY(int x, int z) => Tier(x, z) * TerraceStep;

        public static float SurfaceY(float worldX, float worldZ, float cellSize) =>
            SurfaceY((int)Math.Floor(worldX / cellSize), (int)Math.Floor(worldZ / cellSize));

        public static bool IsSlope(int x, int z)
        {
            if (Current.Style == MapStyle.Classic || !IsLand(x, z)) return false;
            byte tier = Tier(x, z);
            return Tier(x - 1, z) != tier || Tier(x + 1, z) != tier ||
                Tier(x, z - 1) != tier || Tier(x, z + 1) != tier;
        }

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
