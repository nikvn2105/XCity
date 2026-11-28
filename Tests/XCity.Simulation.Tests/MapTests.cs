using System;
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
        public void ClassicMap_MatchesTheOriginalFormulaEverywhere()
        {
            float[] grid = MapGenerator.Generate(MapSpec.Classic, out _);
            for (int x = 0; x < Size; x++)
            for (int z = 0; z < Size; z++)
                Assert.Equal(WorldTerrain.ClassicHeight(x, z), grid[x * Size + z]);
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
                float[] a = MapGenerator.Generate(spec, out var start);
                float[] b = MapGenerator.Generate(spec, out var startAgain);
                Assert.Equal(a, b);
                Assert.Equal(start, startAgain);
                // Never the classic fallback: every seed here must pass the quality check itself.
                Assert.NotEqual(classic, a);

                // The start area is all land with sea close by.
                int half = MapGenerator.StartArea / 2;
                for (int x = start.X - half; x < start.X + half; x++)
                for (int z = start.Z - half; z < start.Z + half; z++)
                    Assert.True(a[x * Size + z] >= 0f, $"{style} seed {seed}: water inside the start area at ({x}, {z})");
                Assert.NotNull(MapGenerator.FindStart(a));
                // A ring of sea always surrounds the map.
                Assert.True(a[0] < 0f && a[Size * Size - 1] < 0f);
            }
        }

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
