using System;

namespace XCity
{
    // Builds the height grid for a map. Seeded maps are retried on the next seed until they pass the
    // quality check (sensible land share and a flat 40 x 40 start area on the coast), so a bad roll
    // never reaches the player. The seed that was actually used is the one in the spec the caller keeps.
    internal static class MapGenerator
    {
        private const int Size = CitySimulation.GridSize;
        internal const int StartArea = 40;
        private const int MaxAttempts = 24;

        public static float[] Generate(MapSpec spec, out (int X, int Z) start)
        {
            var height = new float[Size * Size];
            if (spec.Style == MapStyle.Classic)
            {
                for (int x = 0; x < Size; x++)
                for (int z = 0; z < Size; z++)
                    height[x * Size + z] = WorldTerrain.ClassicHeight(x, z);
                start = FindStart(height) ?? (Size / 2, Size / 2);
                return height;
            }

            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                Fill(height, spec.Style, spec.Seed + attempt);
                float land = LandShare(height);
                (float min, float max) = spec.Style == MapStyle.Islands ? (0.25f, 0.55f) : (0.40f, 0.75f);
                if (land < min || land > max) continue;
                var found = FindStart(height);
                if (found == null) continue;
                start = found.Value;
                return height;
            }
            // Every attempt failed (should not happen): fall back to the classic map rather than an unplayable one.
            return Generate(MapSpec.Classic, out start);
        }

        private static void Fill(float[] height, MapStyle style, int seed)
        {
            var rng = new Random(seed);
            int ox = rng.Next(1, 100000), oz = rng.Next(1, 100000);
            // Island centres: one big continent, or several medium islands.
            int count = style == MapStyle.Continent ? 1 : 5 + rng.Next(3);
            var centres = new (float X, float Z, float Rx, float Rz)[count];
            for (int i = 0; i < count; i++)
            {
                bool continent = style == MapStyle.Continent;
                centres[i] = (
                    continent ? 0.5f + (float)(rng.NextDouble() - 0.5) * 0.1f : 0.18f + (float)rng.NextDouble() * 0.64f,
                    continent ? 0.5f + (float)(rng.NextDouble() - 0.5) * 0.1f : 0.18f + (float)rng.NextDouble() * 0.64f,
                    continent ? 0.38f + (float)rng.NextDouble() * 0.06f : 0.10f + (float)rng.NextDouble() * 0.09f,
                    continent ? 0.38f + (float)rng.NextDouble() * 0.06f : 0.10f + (float)rng.NextDouble() * 0.09f);
            }

            for (int x = 0; x < Size; x++)
            for (int z = 0; z < Size; z++)
            {
                float nx = (x + 0.5f) / Size, nz = (z + 0.5f) / Size;
                float land = -1f;
                foreach (var c in centres) land = Math.Max(land, WorldTerrain.Island(nx, nz, c.X, c.Z, c.Rx, c.Rz));
                // Coastline detail: large bays, then smaller headlands.
                float shape = 0.42f * (WorldTerrain.Noise(x + ox, z + oz, 64) - 0.5f) +
                    0.18f * (WorldTerrain.Noise(x + oz, z + ox, 21) - 0.5f);
                height[x * Size + z] = Math.Min(land + shape, WorldTerrain.EdgeLimit(nx, nz));
            }
        }

        private static float LandShare(float[] height)
        {
            int land = 0;
            for (int i = 0; i < height.Length; i++) if (height[i] >= 0f) land++;
            return land / (float)height.Length;
        }

        // The all-land 40 x 40 block closest to the map centre that has sea within 4 cells of it.
        internal static (int X, int Z)? FindStart(float[] height)
        {
            const int step = 8;
            (int X, int Z)? best = null;
            long bestDistance = long.MaxValue;
            for (int x = 8; x + StartArea < Size - 8; x += step)
            for (int z = 8; z + StartArea < Size - 8; z += step)
            {
                int cx = x + StartArea / 2, cz = z + StartArea / 2;
                long distance = (long)(cx - Size / 2) * (cx - Size / 2) + (long)(cz - Size / 2) * (cz - Size / 2);
                if (distance >= bestDistance || !AllLand(height, x, z, StartArea) || !SeaNear(height, x, z)) continue;
                best = (cx, cz);
                bestDistance = distance;
            }
            return best;
        }

        private static bool AllLand(float[] height, int x0, int z0, int size)
        {
            for (int x = x0; x < x0 + size; x++)
            for (int z = z0; z < z0 + size; z++)
                if (height[x * Size + z] < 0f) return false;
            return true;
        }

        private static bool SeaNear(float[] height, int x0, int z0)
        {
            const int reach = 4;
            for (int x = x0 - reach; x < x0 + StartArea + reach; x++)
            for (int z = z0 - reach; z < z0 + StartArea + reach; z++)
            {
                bool border = x < x0 || z < z0 || x >= x0 + StartArea || z >= z0 + StartArea;
                if (border && x >= 0 && z >= 0 && x < Size && z < Size && height[x * Size + z] < 0f) return true;
            }
            return false;
        }
    }
}
