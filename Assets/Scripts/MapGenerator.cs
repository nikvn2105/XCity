using System;

namespace XCity
{
    // Builds the height grid for a map. Seeded maps are retried on the next seed until they pass the
    // quality check (sensible land share and a flat 40 x 40 start area on the coast), so a bad roll
    // never reaches the player. Retrying is deterministic without changing the requested spec.
    internal static partial class MapGenerator
    {
        private const int Size = CitySimulation.GridSize;
        internal const int StartArea = 40;
        private const int MaxAttempts = 24;

        public static float[] Generate(MapSpec spec, out (int X, int Z) start)
            => Generate(spec, out start, out _, out _);

        public static float[] Generate(MapSpec spec, out (int X, int Z) start,
            out WaterKind[] water, out int[] downstream)
        {
            var height = new float[Size * Size];
            if (spec.Style == MapStyle.Classic)
            {
                for (int x = 0; x < Size; x++)
                for (int z = 0; z < Size; z++)
                    height[x * Size + z] = WorldTerrain.ClassicHeight(x, z);
                water = new WaterKind[height.Length];
                downstream = Array.Empty<int>();
                for (int i = 0; i < height.Length; i++)
                    if (height[i] < 0f) water[i] = WaterKind.Sea;
                start = FindStart(height) ?? (Size / 2, Size / 2);
                return height;
            }

            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                int seed = unchecked(spec.Seed + attempt);
                Fill(height, spec.Style, seed);
                int rivers = CarveWater(height, spec.Style, seed, out water, out downstream);
                if (spec.Style == MapStyle.Continent && rivers == 0) continue;
                float land = LandShare(height);
                (float min, float max) = spec.Style == MapStyle.Islands ? (0.18f, 0.55f) : (0.40f, 0.75f);
                if (land < min || land > max) continue;
                var found = FindStart(height, water);
                if (found == null) continue;
                start = found.Value;
                return height;
            }
            // Every attempt failed (should not happen): fall back to the classic map rather than an unplayable one.
            return Generate(MapSpec.Classic, out start, out water, out downstream);
        }

        private static void Fill(float[] height, MapStyle style, int seed)
        {
            var rng = new Random(seed);
            int ox = rng.Next(1, 100000), oz = rng.Next(1, 100000);
            // Continent: a core with peninsulas and a few offshore islets. Islands: many smaller islands.
            var centres = new System.Collections.Generic.List<(float X, float Z, float Rx, float Rz)>();
            float Next(float min, float max) => min + (float)rng.NextDouble() * (max - min);
            if (style == MapStyle.Continent)
            {
                float cx = Next(0.45f, 0.55f), cz = Next(0.45f, 0.55f);
                centres.Add((cx, cz, Next(0.33f, 0.38f), Next(0.33f, 0.38f)));
                int lobes = 3 + rng.Next(2);
                for (int i = 0; i < lobes; i++)
                {
                    double angle = rng.NextDouble() * Math.PI * 2;
                    float reach = Next(0.24f, 0.30f);
                    centres.Add((cx + (float)Math.Cos(angle) * reach, cz + (float)Math.Sin(angle) * reach,
                        Next(0.12f, 0.19f), Next(0.12f, 0.19f)));
                }
                int islets = 2 + rng.Next(3);
                for (int i = 0; i < islets; i++)
                    centres.Add((Next(0.08f, 0.92f), Next(0.08f, 0.92f), Next(0.04f, 0.07f), Next(0.04f, 0.07f)));
            }
            else
            {
                int count = 6 + rng.Next(4);
                for (int i = 0; i < count; i++)
                    centres.Add((Next(0.14f, 0.86f), Next(0.14f, 0.86f), Next(0.07f, 0.14f), Next(0.07f, 0.14f)));
            }

            var islands = centres.ToArray();
            for (int x = 0; x < Size; x++)
            for (int z = 0; z < Size; z++)
            {
                float nx = (x + 0.5f) / Size, nz = (z + 0.5f) / Size;
                float land = -1f;
                foreach (var c in islands) land = Math.Max(land, WorldTerrain.Island(nx, nz, c.X, c.Z, c.Rx, c.Rz));
                // Coastline detail: large bays, then smaller headlands.
                float shape = 0.55f * (WorldTerrain.Noise(x + ox, z + oz, 48) - 0.5f) +
                    0.22f * (WorldTerrain.Noise(x + oz, z + ox, 17) - 0.5f);
                height[x * Size + z] = Math.Min(land + shape, WorldTerrain.EdgeLimit(nx, nz));
            }
        }

        private static float LandShare(float[] height)
        {
            int land = 0;
            for (int i = 0; i < height.Length; i++) if (height[i] >= 0f) land++;
            return land / (float)height.Length;
        }

        // The all-land 40 x 40 block near water, preferring fresh water on seeded maps.
        internal static (int X, int Z)? FindStart(float[] height, WaterKind[] water = null)
        {
            const int step = 8;
            (int X, int Z)? best = null;
            bool bestFresh = false;
            long bestDistance = long.MaxValue;
            for (int x = 8; x + StartArea < Size - 8; x += step)
            for (int z = 8; z + StartArea < Size - 8; z += step)
            {
                int cx = x + StartArea / 2, cz = z + StartArea / 2;
                long distance = (long)(cx - Size / 2) * (cx - Size / 2) + (long)(cz - Size / 2) * (cz - Size / 2);
                if ((bestFresh || water == null) && distance >= bestDistance) continue;
                WaterKind nearby = WaterNear(height, water, x, z);
                bool fresh = nearby is WaterKind.River or WaterKind.Lake;
                if (nearby == WaterKind.Land || (bestFresh && !fresh) ||
                    (bestFresh == fresh && distance >= bestDistance) || !AllLand(height, x, z, StartArea))
                    continue;
                bestFresh = fresh;
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

        private static WaterKind WaterNear(float[] height, WaterKind[] water, int x0, int z0)
        {
            const int reach = 4;
            WaterKind found = WaterKind.Land;
            for (int x = x0 - reach; x < x0 + StartArea + reach; x++)
            {
                int row = x * Size;
                bool side = x < x0 || x >= x0 + StartArea;
                for (int z = z0 - reach; z < z0 + StartArea + reach; z++)
                {
                    if (!side && z == z0) z += StartArea;
                    int cell = row + z;
                    if (height[cell] >= 0f) continue;
                    if (water == null) return WaterKind.Sea;
                    if (water[cell] != WaterKind.Sea) return water[cell];
                    found = WaterKind.Sea;
                }
            }
            return found;
        }
    }
}
