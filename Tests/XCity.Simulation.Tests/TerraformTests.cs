using System.Linq;
using System.Text.Json;
using Xunit;

namespace XCity.Tests
{
    [Collection("WorldMap")]
    public sealed class TerraformTests : System.IDisposable
    {
        public TerraformTests() => WorldTerrain.Use(MapSpec.Classic);
        public void Dispose() => WorldTerrain.Use(MapSpec.Classic);

        [Fact]
        public void Tools_RespectTierLimitsAndLevelUsesFirstTile()
        {
            var city = new CitySimulation();
            var (x, z) = WorldTerrain.Start;
            var source = new[] { (x, z) };
            var destination = new[] { (x + 1, z) };
            for (int i = 0; i < 3; i++) Assert.True(city.TryTerraform(source, TerrainTool.Raise));
            Assert.False(city.TryTerraform(source, TerrainTool.Raise));
            Assert.Equal(3, WorldTerrain.Tier(x, z));
            int money = city.Money;
            Assert.True(city.TryTerraform(new[] { source[0], destination[0], destination[0] }, TerrainTool.Level));
            Assert.Equal(3, WorldTerrain.Tier(x + 1, z));
            Assert.Equal(money - 150, city.Money);
            for (int i = 0; i < 3; i++) Assert.True(city.TryTerraform(destination, TerrainTool.Lower));
            Assert.False(city.TryTerraform(destination, TerrainTool.Lower));
            Assert.Equal(0, WorldTerrain.Tier(x + 1, z));
            Assert.Single(city.CreateSnapshot().terrainIndices);
        }

        [Fact]
        public void Rules_ProtectOccupiedTilesBanksAndNearbyBuildings()
        {
            var city = new CitySimulation();
            var (x, z) = WorldTerrain.Start;
            Assert.True(city.TrySetCell(x + 2, z, CellType.Residential));
            Assert.True(city.TrySetCell(x, z + 5, CellType.Road));
            var bank = (from bx in Enumerable.Range(1, CitySimulation.GridSize - 2)
                        from bz in Enumerable.Range(1, CitySimulation.GridSize - 2)
                        where WorldTerrain.IsLand(bx, bz) && !WorldTerrain.IsLand(bx - 1, bz)
                        select (bx, bz)).First();
            int money = city.Money;
            Assert.False(city.TryTerraform(new[] { (x + 2, z) }, TerrainTool.Raise));
            Assert.False(city.TryTerraform(new[] { (x, z + 5) }, TerrainTool.Raise));
            Assert.False(city.TryTerraform(new[] { (x, z) }, TerrainTool.Raise));
            Assert.False(city.TryTerraform(new[] { bank }, TerrainTool.Raise));
            Assert.False(city.TryTerraform(new[] { (bank.bx - 1, bank.bz) }, TerrainTool.Raise));
            Assert.Equal(money, city.Money);
            Assert.False(WorldTerrain.IsSlope(x + 2, z));
        }

        [Fact]
        public void Budget_StopsStrokeAndUndoRestoresTerrainAndMoney()
        {
            var snapshot = new CitySimulation().CreateSnapshot();
            snapshot.money = 75;
            Assert.True(CitySimulation.TryRestore(snapshot, out var city));
            var (x, z) = WorldTerrain.Start;
            Assert.True(city.TryTerraform(new[] { (x, z), (x + 1, z), (x + 2, z) }, TerrainTool.Raise));
            Assert.Equal(25, city.Money);
            Assert.Equal(1, WorldTerrain.Tier(x, z));
            Assert.Equal(0, WorldTerrain.Tier(x + 1, z));
            Assert.Equal(0, WorldTerrain.Tier(x + 2, z));
            Assert.True(WorldTerrain.IsSlope(x, z));
            Assert.True(WorldTerrain.HasTerraces);
            Assert.True(city.TryUndoBuild());
            Assert.Equal(75, city.Money);
            Assert.Equal(0, WorldTerrain.Tier(x, z));
            Assert.False(WorldTerrain.IsSlope(x, z));
            Assert.False(WorldTerrain.HasTerraces);
        }

        [Fact]
        public void Saves_UseSparseEditsAndLegacySaveIgnoresThem()
        {
            var city = new CitySimulation();
            var (x, z) = WorldTerrain.Start;
            Assert.True(city.TrySetCell(x + 1, z, CellType.Road));
            Assert.True(city.TryTerraform(new[] { (x, z) }, TerrainTool.Raise));
            Assert.True(WorldTerrain.IsSlope(x + 1, z));
            var options = new JsonSerializerOptions { IncludeFields = true };
            var snapshot = JsonSerializer.Deserialize<CitySnapshot>(JsonSerializer.Serialize(city.CreateSnapshot(), options), options);
            Assert.Equal(17, snapshot.schemaVersion);
            Assert.Equal(new[] { CitySimulation.CellIndex(x, z) }, snapshot.terrainIndices);
            WorldTerrain.Use(MapSpec.Classic);
            Assert.True(CitySimulation.TryRestore(snapshot, out var restored));
            Assert.Equal(1, WorldTerrain.Tier(x, z));
            Assert.Equal(CellType.Road, restored.GetCell(x + 1, z).Type);

            snapshot.schemaVersion = 16;
            Assert.True(CitySimulation.TryRestore(snapshot, out _));
            Assert.Equal(0, WorldTerrain.Tier(x, z));
            Assert.False(WorldTerrain.IsSlope(x + 1, z));
        }

        [Theory]
        [InlineData(MapStyle.Classic, 0)]
        [InlineData(MapStyle.Continent, 1)]
        public void LocalCorners_MatchFullRebuildAfterEditAndUndo(MapStyle style, int seed)
        {
            var spec = new MapSpec(style, seed);
            WorldTerrain.Use(spec);
            var city = new CitySimulation();
            var (x, z) = WorldTerrain.Start;
            var points = new[] { (x, z), (x + 1, z + 1), (x + 2, z) };
            var tool = WorldTerrain.Tier(x, z) == 3 ? TerrainTool.Lower : TerrainTool.Raise;
            Assert.True(city.TryTerraform(points, tool));
            AssertCorners(city.CreateSnapshot());
            Assert.True(city.TryUndoBuild());
            AssertCorners(city.CreateSnapshot());

            void AssertCorners(CitySnapshot snapshot)
            {
                MapGenerator.Generate(spec, out _, out var water, out _, out var tiers);
                if (snapshot.terrainIndices != null)
                    for (int i = 0; i < snapshot.terrainIndices.Length; i++)
                        tiers[snapshot.terrainIndices[i]] = snapshot.terrainTiers[i];
                var expected = MapGenerator.BuildSurfaceCorners(tiers, water);
                int size = CitySimulation.GridSize + 1;
                for (int cx = 0; cx < size; cx++)
                for (int cz = 0; cz < size; cz++)
                    Assert.Equal(expected[cx * size + cz], WorldTerrain.CornerY(cx, cz));
            }
        }
    }
}
