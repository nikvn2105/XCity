using System;

namespace XCity
{
    internal static partial class MapGenerator
    {
        private static void BuildTerraces(float[] height, WaterKind[] water, int[] downstream,
            int[] coast, (int X, int Z) start, byte[] tiers)
        {
            // Smooth the height signal before quantizing, so terraces have continuous interiors.
            const int reach = 8;
            var signal = new float[height.Length];
            for (int i = 0; i < height.Length; i++)
                signal[i] = Math.Max(0f, height[i]) * 0.72f + Math.Min(coast[i], 75) * (0.28f / 75f);
            for (int x = reach; x < Size - reach; x++)
            for (int z = reach; z < Size - reach; z++)
            {
                int cell = x * Size + z;
                if (water[cell] != WaterKind.Land || coast[cell] <= 5) continue;
                float average = 0f;
                for (int dx = -reach; dx <= reach; dx += reach)
                for (int dz = -reach; dz <= reach; dz += reach)
                    average += signal[cell + dx * Size + dz];
                tiers[cell] = average >= 7.02f ? (byte)3 : average >= 5.04f ? (byte)2 :
                    average >= 2.88f ? (byte)1 : (byte)0;
            }

            // Reserve the start plateau plus a two-cell land shoulder for building on its edges.
            const int half = StartArea / 2;
            int minX = start.X - half - 2, maxX = start.X + half + 1;
            int minZ = start.Z - half - 2, maxZ = start.Z + half + 1;
            byte startTier = tiers[start.X * Size + start.Z];
            for (int x = minX; x <= maxX; x++)
            for (int z = minZ; z <= maxZ; z++)
                if (water[x * Size + z] == WaterKind.Land)
                    startTier = Math.Min(startTier, tiers[x * Size + z]);
            for (int x = minX; x <= maxX; x++)
            for (int z = minZ; z <= maxZ; z++)
                if (water[x * Size + z] == WaterKind.Land)
                    tiers[x * Size + z] = startTier;

            GradeFreshWater(water, downstream, tiers);
        }

        internal static float[] BuildSurfaceCorners(byte[] tiers, WaterKind[] water)
        {
            const int stride = Size + 1;
            var corners = new float[stride * stride];
            // For a straight edge the two interior samples are smoothstep(1/3) and smoothstep(2/3).
            // Bias the three-cell band by half a cell so its two outer limits land on grid corners.
            for (int x = 0; x <= Size; x++)
            for (int z = 0; z <= Size; z++)
            {
                int weightedTiers = 0, totalWeight = 0;
                for (int dx = -2; dx <= 0; dx++)
                {
                    int nx = x + dx;
                    if ((uint)nx >= Size) continue;
                    int row = nx * Size;
                    int wx = dx == -1 ? 13 : 7;
                    for (int dz = -2; dz <= 0; dz++)
                    {
                        int nz = z + dz;
                        if ((uint)nz >= Size || water[row + nz] != WaterKind.Land) continue;
                        int weight = wx * (dz == -1 ? 13 : 7);
                        weightedTiers += tiers[row + nz] * weight;
                        totalWeight += weight;
                    }
                }
                if (totalWeight == 0) continue;
                float elevation = WorldTerrain.TerraceStep * weightedTiers / totalWeight;
                // Keep the land lip above any adjoining water surface without grading the water itself.
                for (int dx = -1; dx <= 0; dx++)
                for (int dz = -1; dz <= 0; dz++)
                {
                    int nx = x + dx, nz = z + dz;
                    if ((uint)nx >= Size || (uint)nz >= Size) continue;
                    int cell = nx * Size + nz;
                    if (water[cell] == WaterKind.River || water[cell] == WaterKind.Lake)
                        elevation = Math.Max(elevation, tiers[cell] * WorldTerrain.TerraceStep + BridgeProfile.WaterSurface + 0.05f);
                }
                corners[x * stride + z] = elevation;
            }
            return corners;
        }

        private static void GradeFreshWater(WaterKind[] water, int[] downstream, byte[] tiers)
        {
            var arrivals = new byte[tiers.Length];
            var queue = new int[tiers.Length];
            int riverTail = 0;
            for (int cell = 0; cell < tiers.Length; cell++)
                if (water[cell] == WaterKind.Lake && arrivals[cell] == 0)
                    LevelLake(cell);

            for (int cell = 0; cell < tiers.Length; cell++)
            {
                if (water[cell] != WaterKind.River) continue;
                if (downstream[cell] >= 0) arrivals[downstream[cell]]++;
                tiers[cell] = downstream[cell] < 0 ? (byte)0 : RiverBankTier(cell);
            }

            for (int cell = 0; cell < tiers.Length; cell++)
                if (water[cell] == WaterKind.River && arrivals[cell] == 0)
                    queue[riverTail++] = cell;
            for (int head = 0; head < riverTail; head++)
            {
                int cell = queue[head];
                int next = downstream[cell];
                if (next < 0) continue;
                tiers[next] = Math.Min(tiers[next], tiers[cell]);
                if (--arrivals[next] == 0) queue[riverTail++] = next;
            }

            byte BankTier(int cell)
            {
                byte result = 3;
                for (int d = 0; d < 4; d++)
                {
                    int neighbor = Neighbour(cell, d);
                    if (neighbor < 0 || water[neighbor] == WaterKind.Sea) return 0;
                    if (water[neighbor] == WaterKind.Land) result = Math.Min(result, tiers[neighbor]);
                }
                return result;
            }

            byte RiverBankTier(int cell)
            {
                byte level = BankTier(cell);
                // A channel can be several cells wide. Use neighboring banks and the lake outlet level.
                for (int direction = 0; direction < 4; direction++)
                {
                    int neighbor = cell;
                    for (int reach = 1; reach <= 3; reach++)
                    {
                        neighbor = Neighbour(neighbor, direction);
                        if (neighbor < 0) break;
                        if (water[neighbor] == WaterKind.River) continue;
                        level = Math.Min(level, tiers[neighbor]);
                        break;
                    }
                }
                return level;
            }

            void LevelLake(int first)
            {
                int tail = 1;
                byte level = 3;
                queue[0] = first;
                arrivals[first] = 1;
                for (int head = 0; head < tail; head++)
                {
                    int cell = queue[head];
                    level = Math.Min(level, BankTier(cell));
                    for (int d = 0; d < 4; d++)
                    {
                        int neighbor = Neighbour(cell, d);
                        if (neighbor < 0 || water[neighbor] != WaterKind.Lake || arrivals[neighbor] != 0) continue;
                        arrivals[neighbor] = 1;
                        queue[tail++] = neighbor;
                    }
                }
                for (int i = 0; i < tail; i++) tiers[queue[i]] = level;
            }
        }
    }
}
