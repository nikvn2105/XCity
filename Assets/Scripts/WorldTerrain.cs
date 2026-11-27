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

    internal static class WorldTerrain
    {
        public static bool IsLand(int x, int z) =>
            x >= 0 && z >= 0 && x < CitySimulation.GridSize && z < CitySimulation.GridSize && LandHeight(x, z) >= 0f;

        public static float LandHeight(int x, int z)
        {
            float nx = (x + 0.5f) / CitySimulation.GridSize;
            float nz = (z + 0.5f) / CitySimulation.GridSize;
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
            float edge = Math.Min(Math.Min(nx, 1f - nx), Math.Min(nz, 1f - nz));
            return Math.Min(Math.Max(land + shape, offshore), (edge - 0.022f) * 12f);
        }

        public static float Vegetation(int x, int z) =>
            0.66f * Noise(x + 41, z + 17, 48) + 0.34f * Noise(x + 75, z + 12, 11);

        private static float Island(float x, float z, float cx, float cz, float rx, float rz)
        {
            float dx = (x - cx) / rx;
            float dz = (z - cz) / rz;
            return 1f - dx * dx - dz * dz;
        }

        private static float Noise(int x, int z, int span)
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

        private static float Hash(int x, int z)
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
