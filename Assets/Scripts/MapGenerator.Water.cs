using System;
using System.Collections.Generic;

namespace XCity
{
    internal static partial class MapGenerator
    {
        private static readonly int[] WaterDx = { 1, 0, -1, 0 };
        private static readonly int[] WaterDz = { 0, 1, 0, -1 };

        private static int Neighbour(int cell, int direction)
        {
            int x = cell / Size + WaterDx[direction], z = cell % Size + WaterDz[direction];
            return (uint)x < Size && (uint)z < Size ? x * Size + z : -1;
        }

        private static int CarveWater(float[] height, MapStyle style, int seed, out WaterKind[] water, out int[] downstream)
        {
            var rng = new Random(unchecked(seed * 397 ^ 0x537A));
            water = new WaterKind[height.Length];
            downstream = new int[height.Length];
            var queue = new int[height.Length];
            var coast = new int[height.Length];
            Array.Fill(coast, -1);

            // The map has a continuous sea border. Flood it so isolated low spots are not ocean.
            int tail = 1;
            coast[0] = 0;
            queue[0] = 0;
            for (int head = 0; head < tail; head++)
            {
                int cell = queue[head];
                water[cell] = WaterKind.Sea;
                for (int d = 0; d < 4; d++)
                {
                    int next = Neighbour(cell, d);
                    if (next < 0 || coast[next] >= 0 || height[next] >= 0f) continue;
                    coast[next] = 0;
                    queue[tail++] = next;
                }
            }

            // A multi-source distance field gives every inland cell a downhill route to the sea.
            for (int head = 0; head < tail; head++)
            {
                int cell = queue[head];
                for (int d = 0; d < 4; d++)
                {
                    int next = Neighbour(cell, d);
                    if (next < 0 || coast[next] >= 0) continue;
                    coast[next] = coast[cell] + 1;
                    queue[tail++] = next;
                    height[next] = Math.Max(0f, height[next]);
                }
            }

            int[] islands = LabelIslands(water, queue, out int largest);
            int lakeTarget = rng.Next(1, 5);
            var lakes = new List<(int Cell, int Radius)>(lakeTarget);
            for (int i = 0; i < lakeTarget; i++)
            {
                int radius = rng.Next(3, 10);
                int center = PickInland(rng, height, coast, islands, largest, radius + 8, lakes, null);
                if (center < 0) break;
                lakes.Add((center, radius));
                int cx = center / Size, cz = center % Size;
                for (int dx = -radius; dx <= radius; dx++)
                for (int dz = -radius; dz <= radius; dz++)
                {
                    if (dx * dx + dz * dz > radius * radius) continue;
                    int index = (cx + dx) * Size + cz + dz;
                    water[index] = WaterKind.Lake;
                    height[index] = -0.2f;
                }
            }

            int target = style == MapStyle.Continent ? rng.Next(1, 4) : rng.Next(0, 2);
            var sources = new List<int>(target);
            var path = new List<int>(Size);
            int rivers = 0;
            for (int attempt = 0; attempt < target * 12 && rivers < target; attempt++)
            {
                int source = attempt == 0 && lakes.Count > 0 && (seed & 1) == 0
                    ? LakeOutlet(lakes[0], water, coast)
                    : PickInland(rng, height, coast, islands, largest, 22, lakes, sources);
                if (source < 0 || !TraceRiver(source, rng.Next(1, 100000), water, height, coast, path))
                    continue;
                sources.Add(source);
                CutRiver(path, rng.Next(2, 5), water);
                rivers++;
            }

            // Give the full channel (including widened banks) a monotonic flow field.
            // Flood from river mouths through river cells only; each edge goes to an earlier layer.
            tail = 0;
            Array.Fill(downstream, -1);
            for (int i = 0; i < water.Length; i++)
            {
                if (water[i] != WaterKind.River) continue;
                for (int d = 0; d < 4; d++)
                {
                    int next = Neighbour(i, d);
                    if (next < 0 || water[next] != WaterKind.Sea) continue;
                    queue[tail++] = i;
                    downstream[i] = -2;
                    break;
                }
            }
            for (int head = 0; head < tail; head++)
            {
                int cell = queue[head];
                for (int d = 0; d < 4; d++)
                {
                    int next = Neighbour(cell, d);
                    if (next < 0 || water[next] != WaterKind.River || downstream[next] != -1) continue;
                    downstream[next] = cell;
                    queue[tail++] = next;
                }
            }
            for (int i = 0; i < water.Length; i++)
            {
                if (water[i] != WaterKind.River) continue;
                if (downstream[i] == -1) water[i] = WaterKind.Land;
                else
                {
                    height[i] = -0.2f;
                    if (downstream[i] == -2) downstream[i] = -1;
                }
            }
            return rivers;
        }

        private static int[] LabelIslands(WaterKind[] water, int[] queue, out int largest)
        {
            var labels = new int[water.Length];
            largest = 0;
            int maxSize = 0;
            for (int i = 0; i < water.Length; i++)
            {
                if (water[i] != WaterKind.Land || labels[i] != 0) continue;
                int id = i + 1, tail = 1;
                labels[i] = id;
                queue[0] = i;
                for (int head = 0; head < tail; head++)
                for (int d = 0; d < 4; d++)
                {
                    int next = Neighbour(queue[head], d);
                    if (next < 0 || water[next] != WaterKind.Land || labels[next] != 0) continue;
                    labels[next] = id;
                    queue[tail++] = next;
                }
                if (tail <= maxSize) continue;
                maxSize = tail;
                largest = id;
            }
            return labels;
        }

        private static int PickInland(Random rng, float[] height, int[] coast, int[] islands, int largest,
            int clearance, List<(int Cell, int Radius)> lakes, List<int> sources)
        {
            int best = -1;
            float bestHeight = float.NegativeInfinity;
            for (int i = 0; i < 512; i++)
            {
                int cell = rng.Next(height.Length);
                if (islands[cell] != largest || coast[cell] < clearance || height[cell] <= bestHeight) continue;
                bool clear = true;
                foreach (var lake in lakes)
                    if (Distance(cell, lake.Cell) <= clearance + lake.Radius) clear = false;
                if (sources != null)
                    foreach (int source in sources)
                        if (Distance(cell, source) < 42) clear = false;
                if (!clear) continue;
                best = cell;
                bestHeight = height[cell];
            }
            return best;
        }

        private static int Distance(int a, int b) =>
            Math.Max(Math.Abs(a / Size - b / Size), Math.Abs(a % Size - b % Size));

        private static int LakeOutlet((int Cell, int Radius) lake, WaterKind[] water, int[] coast)
        {
            int best = -1;
            for (int x = lake.Cell / Size - lake.Radius; x <= lake.Cell / Size + lake.Radius; x++)
            for (int z = lake.Cell % Size - lake.Radius; z <= lake.Cell % Size + lake.Radius; z++)
            {
                int cell = x * Size + z;
                if (water[cell] != WaterKind.Lake) continue;
                for (int d = 0; d < 4; d++)
                {
                    int next = Neighbour(cell, d);
                    if (next >= 0 && water[next] == WaterKind.Land && coast[next] < coast[cell] &&
                        (best < 0 || coast[next] < coast[best])) best = next;
                }
            }
            return best;
        }

        private static bool TraceRiver(int source, int salt, WaterKind[] water, float[] height, int[] coast, List<int> path)
        {
            path.Clear();
            bool lateral = false;
            int cell = source;
            while (water[cell] == WaterKind.Land)
            {
                path.Add(cell);
                int best = -1;
                float score = float.PositiveInfinity;
                for (int d = 0; d < 4; d++)
                {
                    int next = Neighbour(cell, d);
                    if (next < 0 || water[next] == WaterKind.Lake || coast[next] > coast[cell] ||
                        (lateral && coast[next] == coast[cell])) continue;
                    float weight = coast[next] * 0.25f + height[next] * 0.6f +
                        WorldTerrain.Noise(next / Size + salt, next % Size + salt, 5) * 2.5f;
                    if (weight >= score) continue;
                    best = next;
                    score = weight;
                }
                if (best < 0) return false;
                lateral = coast[best] == coast[cell];
                cell = best;
            }
            // Avoid counting a short tributary or a source that already falls on a river.
            return path.Count >= 12 && water[cell] == WaterKind.Sea;
        }

        private static void CutRiver(List<int> path, int width, WaterKind[] water)
        {
            int low = -(width - 1) / 2;
            foreach (int cell in path)
            for (int dx = low; dx < low + width; dx++)
            for (int dz = low; dz < low + width; dz++)
            {
                int x = cell / Size + dx, z = cell % Size + dz;
                if ((uint)x >= Size || (uint)z >= Size) continue;
                int index = x * Size + z;
                if (water[index] != WaterKind.Land) continue;
                water[index] = WaterKind.River;
            }
        }
    }
}
