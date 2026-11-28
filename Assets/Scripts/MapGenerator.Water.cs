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

            // Coast distance sets source and lake clearance, independently of the river routes.
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
            int salt = (int)(unchecked((uint)seed * 2654435761u) % 99000u) + 1000;
            int lakeTarget = rng.Next(1, 5);
            var lakes = new List<(int Cell, int Radius)>(lakeTarget);
            for (int i = 0; i < lakeTarget; i++)
            {
                int radius = rng.Next(5, 15);
                int center = PickInland(rng, height, coast, islands, largest, radius + 10, lakes, null);
                if (center < 0) break;
                lakes.Add((center, radius));
                int cx = center / Size, cz = center % Size;
                int extent = LakeExtent(radius);
                for (int dx = -extent; dx <= extent; dx++)
                for (int dz = -extent; dz <= extent; dz++)
                {
                    float outline = WorldTerrain.Noise(cx + dx + salt, cz + dz + salt, 7);
                    float edge = Math.Max(5f, Math.Min(14f, radius + 4f * (outline - 0.5f)));
                    if (dx * dx + dz * dz > edge * edge) continue;
                    int index = (cx + dx) * Size + cz + dz;
                    water[index] = WaterKind.Lake;
                    height[index] = -0.2f;
                }
            }

            int target = style == MapStyle.Continent ? rng.Next(1, 4) : rng.Next(0, 2);
            int[] route = target > 0 ? BuildRiverRoutes(water, coast, islands, largest, salt, style == MapStyle.Islands ? 24 : 34) : Array.Empty<int>();
            var sources = new List<int>(target);
            var path = new List<int>(Size);
            int rivers = 0;
            for (int attempt = 0; attempt < target * 12 && rivers < target; attempt++)
            {
                int source = attempt == 0 && lakes.Count > 0 && (seed & 1) == 0
                    ? LakeOutlet(lakes[0], water, coast, route)
                    : PickInland(rng, height, coast, islands, largest, 22, lakes, sources);
                if (source < 0 || !TraceRiver(source, water, route, path))
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
                    if (Distance(cell, lake.Cell) <= clearance + LakeExtent(lake.Radius)) clear = false;
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

        private static int LakeExtent(int radius) => Math.Min(14, radius + 2);

        private static int LakeOutlet((int Cell, int Radius) lake, WaterKind[] water, int[] coast, int[] route)
        {
            int best = -1;
            int extent = LakeExtent(lake.Radius);
            for (int x = lake.Cell / Size - extent; x <= lake.Cell / Size + extent; x++)
            for (int z = lake.Cell % Size - extent; z <= lake.Cell % Size + extent; z++)
            {
                int cell = x * Size + z;
                if (water[cell] != WaterKind.Lake) continue;
                for (int d = 0; d < 4; d++)
                {
                    int next = Neighbour(cell, d);
                    if (next >= 0 && route[next] >= 0 &&
                        (best < 0 || coast[next] < coast[best])) best = next;
                }
            }
            return best;
        }

        // Reverse Dijkstra from the shore. Traversing into a land cell always costs the
        // same regardless of which neighbor led there, so the first insertion is optimal.
        // The bounded integer weights let us use a circular bucket queue with one link per cell.
        private static int[] BuildRiverRoutes(WaterKind[] water, int[] coast, int[] islands, int largest, int salt, int span)
        {
            const int bucketCount = 105; // max step is 2 + 96 + 6
            var buckets = new int[bucketCount];
            var links = new int[water.Length];
            var route = new int[water.Length];
            Array.Fill(buckets, -1);
            Array.Fill(route, -1);
            int pending = 0;

            for (int cell = 0; cell < water.Length; cell++)
            {
                if (islands[cell] != largest || coast[cell] != 1 || water[cell] != WaterKind.Land) continue;
                for (int d = 0; d < 4; d++)
                {
                    int next = Neighbour(cell, d);
                    if (next < 0 || water[next] != WaterKind.Sea) continue;
                    Enqueue(cell, next, 0);
                    break;
                }
            }

            for (int distance = 0; pending > 0; distance++)
            {
                int bucket = distance % bucketCount;
                while (buckets[bucket] >= 0)
                {
                    int cell = buckets[bucket];
                    buckets[bucket] = links[cell];
                    pending--;
                    for (int d = 0; d < 4; d++)
                    {
                        int next = Neighbour(cell, d);
                        if (next < 0 || route[next] >= 0 || water[next] != WaterKind.Land) continue;
                        Enqueue(next, cell, distance);
                    }
                }
            }
            return route;

            void Enqueue(int cell, int parent, int distance)
            {
                int x = cell / Size + salt, z = cell % Size + salt;
                float broad = WorldTerrain.Noise(x, z, span);
                int step = 2 + (int)(96f * broad * broad * broad) +
                    (int)(6f * WorldTerrain.Noise(x + 263, z + 157, 9));
                int bucket = (distance + step) % bucketCount;
                route[cell] = parent;
                links[cell] = buckets[bucket];
                buckets[bucket] = cell;
                pending++;
            }
        }

        private static bool TraceRiver(int source, WaterKind[] water, int[] route, List<int> path)
        {
            path.Clear();
            int cell = source;
            while (water[cell] == WaterKind.Land)
            {
                path.Add(cell);
                cell = route[cell];
                if (cell < 0) return false;
            }
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
