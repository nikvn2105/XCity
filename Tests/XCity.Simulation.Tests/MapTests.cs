using System;
using System.Linq;
using Xunit;

namespace XCity.Tests
{
    // These tests switch the shared world map, so they never run alongside other tests.
    [CollectionDefinition("WorldMap", DisableParallelization = true)]
    public class WorldMapCollection { }

    [Collection("WorldMap")]
    public class MapTests
    {
        private const int Size = CitySimulation.GridSize;

        [Fact]
        public void ClassicCornersAreFlatAndEveryLandTileIsBuildable()
        {
            WorldTerrain.Use(MapSpec.Classic);
            for (int x = 0; x <= Size; x++)
            for (int z = 0; z <= Size; z++)
            {
                Assert.Equal(0f, WorldTerrain.CornerY(x, z));
                Assert.False(WorldTerrain.IsSlope(x, z));
            }
        }

        [Fact]
        public void StraightTierEdgeUsesExactlyThreeContinuousSlopeTiles()
        {
            var tiers = new byte[Size * Size];
            var water = new WaterKind[tiers.Length];
            int edge = Size / 2;
            for (int x = edge; x < Size; x++)
            for (int z = 0; z < Size; z++)
                tiers[x * Size + z] = 1;
            float[] corners = MapGenerator.BuildSurfaceCorners(tiers, water);
            float At(int x, int z) => corners[x * (Size + 1) + z];

            Assert.Equal(0f, At(edge - 1, edge));
            Assert.Equal(2f * 7f / 27f, At(edge, edge));
            Assert.Equal(2f * 20f / 27f, At(edge + 1, edge));
            Assert.Equal(2f, At(edge + 2, edge));

            for (int x = edge - 2; x <= edge + 2; x++)
            {
                var tile = new TerrainPatch(At(x, edge), At(x + 1, edge), At(x, edge + 1), At(x + 1, edge + 1));
                Assert.Equal(x >= edge - 1 && x < edge + 2, tile.IsSlope);
                Assert.Equal(At(x + 1, edge), tile.Sample(1f, 0.5f));
                Assert.Equal(At(x, edge), tile.Sample(0f, 0.5f));
            }
        }

        [Fact]
        public void ClassicMap_MatchesTheOriginalFormulaEverywhere()
        {
            float[] grid = MapGenerator.Generate(MapSpec.Classic, out _, out var water, out _, out var tiers);
            for (int x = 0; x < Size; x++)
            for (int z = 0; z < Size; z++)
            {
                int i = x * Size + z;
                Assert.Equal(WorldTerrain.ClassicHeight(x, z), grid[i]);
                Assert.Equal(0, tiers[i]);
                Assert.Equal(grid[i] >= 0 ? WaterKind.Land : WaterKind.Sea, water[i]);
            }
        }

        [Theory]
        [InlineData(MapStyle.Continent)]
        [InlineData(MapStyle.Islands)]
        public void SeededMaps_AreDeterministicAndPlayable(MapStyle style)
        {
            float[] classic = MapGenerator.Generate(MapSpec.Classic, out _);
            for (int seed = 1; seed <= 6; seed++)
            {
                var spec = new MapSpec(style, seed);
                float[] a = MapGenerator.Generate(spec, out var start, out var water, out var flow, out var tiers);
                float[] b = MapGenerator.Generate(spec, out var startAgain, out var waterAgain, out var flowAgain, out var tiersAgain);
                Assert.Equal(a, b);
                Assert.Equal(water, waterAgain);
                Assert.Equal(flow, flowAgain);
                Assert.Equal(tiers, tiersAgain);
                var corners = MapGenerator.BuildSurfaceCorners(tiers, water);
                Assert.Equal(corners, MapGenerator.BuildSurfaceCorners(tiersAgain, waterAgain));
                int stride = Size + 1;
                int core = 0;
                float startHeight = corners[start.X * stride + start.Z];
                if (style == MapStyle.Continent) Assert.True(tiers.Distinct().Count() >= 2);
                Assert.Contains(WaterKind.Lake, water);
                Assert.Equal(start, startAgain);
                // Never the classic fallback: every seed here must pass the quality check itself.
                Assert.NotEqual(classic, a);

                // The start area is all land with water close by.
                int half = MapGenerator.StartArea / 2;
                for (int x = start.X - half; x < start.X + half; x++)
                for (int z = start.Z - half; z < start.Z + half; z++)
                {
                    Assert.True(a[x * Size + z] >= 0f, $"{style} seed {seed}: water inside the start area at ({x}, {z})");
                    Assert.Equal(tiers[start.X * Size + start.Z], tiers[x * Size + z]);
                }
                for (int x = start.X - half; x <= start.X + half; x++)
                for (int z = start.Z - half; z <= start.Z + half; z++)
                    Assert.Equal(startHeight, corners[x * stride + z]);
                for (int x = 2; x < Size - 1; x++)
                for (int z = 2; z < Size - 1; z++)
                {
                    byte tier = tiers[x * Size + z];
                    if (tier == 0 || water[x * Size + z] != WaterKind.Land) continue;
                    bool uniform = true;
                    for (int dx = -2; dx <= 1; dx++)
                    for (int dz = -2; dz <= 1; dz++)
                    {
                        int i = (x + dx) * Size + z + dz;
                        uniform &= water[i] == WaterKind.Land && tiers[i] == tier;
                    }
                    if (!uniform) continue;
                    int c = x * stride + z;
                    float flat = tier * WorldTerrain.TerraceStep;
                    Assert.Equal(flat, corners[c]);
                    Assert.Equal(flat, corners[c + 1]);
                    Assert.Equal(flat, corners[c + stride]);
                    Assert.Equal(flat, corners[c + stride + 1]);
                    core++;
                }
                Assert.True(core > 0);
                Assert.Contains(Enumerable.Range(start.X - half - 4, MapGenerator.StartArea + 8)
                    .SelectMany(x => Enumerable.Range(start.Z - half - 4, MapGenerator.StartArea + 8)
                        .Select(z => water[x * Size + z])), kind => kind is WaterKind.River or WaterKind.Lake);
                Assert.NotNull(MapGenerator.FindStart(a));
                // A ring of sea always surrounds the map.
                Assert.True(a[0] < 0f && a[Size * Size - 1] < 0f);
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        [InlineData(9)]
        [InlineData(10)]
        public void Continent_RiverFlowReachesSea(int seed)
        {
            var spec = new MapSpec(MapStyle.Continent, seed);
            try
            {
                WorldTerrain.Use(spec);
                var (x0, z0) = WorldTerrain.Start;
                int half = MapGenerator.StartArea / 2;
                for (int x = x0 - half; x < x0 + half; x++)
                for (int z = z0 - half; z < z0 + half; z++)
                    Assert.True(WorldTerrain.IsLand(x, z));

                int riverCells = 0;
                for (int x = 0; x < Size; x++)
                for (int z = 0; z < Size; z++)
                {
                    if (WorldTerrain.WaterKind(x, z) != WaterKind.River) continue;
                    riverCells++;
                    Assert.True(WorldTerrain.IsFreshWater(x, z));
                    Assert.False(WorldTerrain.IsLand(x, z));
                    Assert.False(WorldTerrain.GetSurface(x, z).IsSlope);
                    int current = x * Size + z;
                    for (int links = 0; ; links++)
                    {
                        Assert.True(links < Size * Size, "Downstream has a cycle");
                        int next = WorldTerrain.Downstream(current / Size, current % Size);
                        if (next == -1) break;
                        Assert.Equal(1, Math.Abs(current / Size - next / Size) + Math.Abs(current % Size - next % Size));
                        Assert.Equal(WaterKind.River, WorldTerrain.WaterKind(next / Size, next % Size));
                        Assert.True(WorldTerrain.SurfaceY(current / Size, current % Size) >=
                            WorldTerrain.SurfaceY(next / Size, next % Size));
                        current = next;
                    }
                    Assert.True(HasNeighbour(current / Size, current % Size, WaterKind.Sea));
                }
                Assert.True(riverCells > 0);
            }
            finally
            {
                WorldTerrain.Use(MapSpec.Classic);
            }
        }

        [Fact]
        public void Continent_RiversMeanderBeyondSingleCellStairSteps()
        {
            const int seeds = 10;
            const int chord = 12;
            double pathRatio = 0, smoothedRatio = 0;
            for (int seed = 1; seed <= seeds; seed++)
            {
                MapGenerator.Generate(new MapSpec(MapStyle.Continent, seed), out _, out _, out int[] flow);
                int source = -1, longest = 0;
                for (int cell = 0; cell < flow.Length; cell++)
                {
                    if (flow[cell] < 0) continue;
                    int length = 0;
                    for (int next = cell; flow[next] >= 0; next = flow[next])
                        Assert.True(++length < flow.Length, "River contains a cycle");
                    if (length <= longest) continue;
                    longest = length;
                    source = cell;
                }
                Assert.True(longest >= chord * 2, $"Seed {seed} has no substantial river");

                double smoothedLength = 0;
                int end = source;
                for (int steps = 0; steps < longest; steps += chord)
                {
                    int start = end;
                    for (int i = 0; i < Math.Min(chord, longest - steps); i++) end = flow[end];
                    smoothedLength += StraightDistance(start, end);
                }
                double direct = StraightDistance(source, end);
                pathRatio += longest / direct;
                smoothedRatio += smoothedLength / direct;
            }

            // The chord metric filters out raster stair steps on otherwise straight diagonal channels.
            Assert.True(pathRatio / seeds >= 1.30, $"Average path ratio: {pathRatio / seeds:F3}");
            Assert.True(smoothedRatio / seeds >= 1.15, $"Average smoothed ratio: {smoothedRatio / seeds:F3}");
        }

        private static double StraightDistance(int a, int b)
        {
            int dx = a / Size - b / Size, dz = a % Size - b % Size;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        [Fact]
        public void WaterfrontRules_AndLandValueDistinguishRiverFromSea()
        {
            try
            {
                WorldTerrain.Use(new MapSpec(MapStyle.Continent, 1));
                var snapshot = new CitySimulation().CreateSnapshot();
                snapshot.peakPopulation = BuildingCatalog.UnlockPopulation(BuildingModel.Harbor);
                Assert.True(CitySimulation.TryRestore(snapshot, out var city));
                var riverBank = FindBank(WaterKind.River);
                var lakeBank = FindBank(WaterKind.Lake);
                var seaBank = FindBank(WaterKind.Sea);
                var inland = (from x in Enumerable.Range(10, Size - 20)
                              from z in Enumerable.Range(10, Size - 20)
                              where Enumerable.Range(x - 6, 13).All(cx =>
                                  Enumerable.Range(z - 6, 13).All(cz => WorldTerrain.IsLand(cx, cz)))
                              select (x, z)).First();
                Assert.Equal(12, city.GetLandValue(riverBank.X, riverBank.Z) - city.GetLandValue(inland.x, inland.z));
                Assert.Equal(12, city.GetLandValue(lakeBank.X, lakeBank.Z) - city.GetLandValue(inland.x, inland.z));
                Assert.False(city.TrySetCell(riverBank.X, riverBank.Z, CellType.CivicService, BuildingModel.Harbor));
                Assert.False(city.TrySetCell(lakeBank.X, lakeBank.Z, CellType.CivicService, BuildingModel.Harbor));

                foreach (var bank in new[] { riverBank, lakeBank, seaBank })
                {
                    Assert.True(city.TrySetCell(bank.X, bank.Z, CellType.CivicService, BuildingModel.WaterPump));
                    Assert.True(city.TrySetCell(bank.X, bank.Z, CellType.Empty));
                    Assert.True(city.TrySetCell(bank.X, bank.Z, CellType.CivicService, BuildingModel.SewagePlant));
                    Assert.True(city.TrySetCell(bank.X, bank.Z, CellType.Empty));
                }
                Assert.True(city.TrySetCell(seaBank.X, seaBank.Z, CellType.CivicService, BuildingModel.Harbor));
                Assert.True(CitySimulation.TryRestore(city.CreateSnapshot(), out var restored));
                Assert.Equal(BuildingModel.Harbor, restored.GetCell(seaBank.X, seaBank.Z).Model);
            }
            finally
            {
                WorldTerrain.Use(MapSpec.Classic);
            }
        }

        [Fact]
        public void Bridge_CrossesAFreshWaterChannel()
        {
            try
            {
                WorldTerrain.Use(new MapSpec(MapStyle.Continent, 1));
                var city = new CitySimulation();
                var crossing = (from x in Enumerable.Range(1, Size - 10)
                                from z in Enumerable.Range(1, Size - 10)
                                where WorldTerrain.IsLand(x, z)
                                from distance in Enumerable.Range(3, 4)
                                where WorldTerrain.IsLand(x + distance, z) &&
                                    Enumerable.Range(1, distance - 1).All(i => WorldTerrain.WaterKind(x + i, z) == WaterKind.River)
                                select (x, z, distance)).First();
                Assert.True(city.TryBuildBridge(crossing.x, crossing.z, crossing.x + crossing.distance, crossing.z));
                for (int i = 1; i < crossing.distance; i++)
                    Assert.True(city.GetCell(crossing.x + i, crossing.z).IsBridge);
            }
            finally
            {
                WorldTerrain.Use(MapSpec.Classic);
            }
        }

        [Fact]
        public void TerracedEdge_RejectsBuildingsAndJoinsRoadsAtTheSameHeight()
        {
            try
            {
                WorldTerrain.Use(new MapSpec(MapStyle.Continent, 1));
                var city = new CitySimulation();
                var edge = (from x in Enumerable.Range(1, Size - 2)
                            from z in Enumerable.Range(1, Size - 2)
                            where WorldTerrain.IsLand(x, z) && WorldTerrain.IsLand(x + 1, z) &&
                                WorldTerrain.IsSlope(x, z) && WorldTerrain.IsSlope(x + 1, z) &&
                                WorldTerrain.Tier(x, z) != WorldTerrain.Tier(x + 1, z)
                            select (x, z)).First();
                var (ex, ez) = edge;
                Assert.True(WorldTerrain.IsSlope(ex, ez));
                Assert.False(city.TrySetCell(ex, ez, CellType.Residential));
                Assert.False(city.TrySetCell(ex, ez, CellType.CivicService, BuildingModel.SmallPark));
                Assert.True(city.TrySetCell(ex, ez, CellType.Road));
                Assert.True(city.TrySetCell(ex + 1, ez, CellType.Road));
                TerrainPatch west = WorldTerrain.GetSurface(ex, ez);
                TerrainPatch east = WorldTerrain.GetSurface(ex + 1, ez);
                Assert.True(west.IsSlope && east.IsSlope);
                float joint = west.Sample(1f, 0.5f);
                Assert.Equal(joint, east.Sample(0f, 0.5f), 5);
                Assert.Equal(west.Sample(1f, 0.39f), east.Sample(0f, 0.39f), 5);
                Assert.Equal(joint, city.RoadSurfaceY((ex + 1) * 4f, (ez + 0.5f) * 4f, 4f), 5);

                // An older save with a house on what is now a slope drops that house on load.
                CitySnapshot snapshot = city.CreateSnapshot();
                snapshot.schemaVersion = 15;
                int slot = System.Array.IndexOf(snapshot.cellIndices, CitySimulation.CellIndex(ex, ez));
                snapshot.cells[slot] = new CityCell { Type = CellType.Residential };
                Assert.True(CitySimulation.TryRestore(snapshot, out var restored));
                Assert.Equal(1, restored.DroppedCellsOnLoad);
                Assert.Equal(CellType.Empty, restored.GetCell(ex, ez).Type);
            }
            finally
            {
                WorldTerrain.Use(MapSpec.Classic);
            }
        }

        private static (int X, int Z) FindBank(WaterKind kind) =>
            (from x in Enumerable.Range(1, Size - 2)
             from z in Enumerable.Range(1, Size - 2)
             where CitySimulation.IsBuildable(x, z, CellType.CivicService) && HasNeighbour(x, z, kind) &&
                 (kind == WaterKind.Sea || !HasNeighbour(x, z, WaterKind.Sea))
             select (x, z)).First();

        private static bool HasNeighbour(int x, int z, WaterKind kind) =>
            WorldTerrain.WaterKind(x - 1, z) == kind || WorldTerrain.WaterKind(x + 1, z) == kind ||
            WorldTerrain.WaterKind(x, z - 1) == kind || WorldTerrain.WaterKind(x, z + 1) == kind;

        [Fact]
        public void DifferentSeeds_GiveDifferentCoastlines()
        {
            float[] a = MapGenerator.Generate(new MapSpec(MapStyle.Continent, 1), out _);
            float[] b = MapGenerator.Generate(new MapSpec(MapStyle.Continent, 2), out _);
            int differ = 0;
            for (int i = 0; i < a.Length; i++) if ((a[i] >= 0f) != (b[i] >= 0f)) differ++;
            Assert.True(differ > a.Length / 50);
        }

        [Fact]
        public void Saves_RestoreOnTheirOwnMap_AndOldSavesUseTheClassicMap()
        {
            var islands = new MapSpec(MapStyle.Islands, 7);
            try
            {
                WorldTerrain.Use(islands);
                var city = new CitySimulation(3);
                Assert.Equal(islands, city.Map);
                CitySnapshot snapshot = city.CreateSnapshot();

                WorldTerrain.Use(MapSpec.Classic);
                Assert.True(CitySimulation.TryRestore(snapshot, out var restored));
                Assert.Equal(islands, WorldTerrain.Current);
                Assert.Equal(islands, restored.Map);

                snapshot.schemaVersion = 13;
                Assert.True(CitySimulation.TryRestore(snapshot, out var legacy));
                Assert.Equal(MapSpec.Classic, WorldTerrain.Current);
                Assert.Equal(MapSpec.Classic, legacy.Map);

                snapshot.schemaVersion = 14;
                snapshot.mapStyle = 99;
                Assert.False(CitySimulation.TryRestore(snapshot, out _));
            }
            finally
            {
                WorldTerrain.Use(MapSpec.Classic);
            }
        }
    }
}
