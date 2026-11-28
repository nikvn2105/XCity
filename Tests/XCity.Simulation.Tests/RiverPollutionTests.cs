using Xunit;

namespace XCity.Tests
{
    [Collection("WorldMap")]
    public class RiverPollutionTests
    {
        private const int Size = CitySimulation.GridSize;

        [Fact]
        public void SewagePlantBesideRiver_DirtiesDownstreamAndThePumpThere()
        {
            WorldTerrain.Use(new MapSpec(MapStyle.Continent, 1));
            try
            {
                var city = new CitySimulation();
                Assert.True(FindRiverBank(city, out int bankX, out int bankZ, out int pumpX, out int pumpZ));
                Assert.False(city.PumpOnPollutedWater(pumpX, pumpZ));

                Assert.True(city.TrySetCell(bankX, bankZ, CellType.CivicService, BuildingModel.SewagePlant));
                Assert.True(city.PumpOnPollutedWater(pumpX, pumpZ));

                city.TrySetCell(bankX, bankZ, CellType.Empty);
                Assert.False(city.PumpOnPollutedWater(pumpX, pumpZ));
            }
            finally
            {
                WorldTerrain.Use(MapSpec.Classic);
            }
        }

        // A buildable cell beside a river cell, and a land cell beside the river 10 cells downstream.
        private static bool FindRiverBank(CitySimulation city, out int bankX, out int bankZ, out int pumpX, out int pumpZ)
        {
            for (int x = 1; x < Size - 1; x++)
            for (int z = 1; z < Size - 1; z++)
            {
                bankX = x; bankZ = z;
                if (!CitySimulation.IsBuildable(x, z) || !TryRiverNeighbour(x, z, out int river)) continue;
                int down = river;
                for (int i = 0; i < 10 && down >= 0; i++) down = WorldTerrain.Downstream(down / Size, down % Size);
                if (down < 0) continue;
                int dx = down / Size, dz = down % Size;
                foreach (var (px, pz) in new[] { (dx - 1, dz), (dx + 1, dz), (dx, dz - 1), (dx, dz + 1) })
                {
                    if (!WorldTerrain.IsLand(px, pz) || System.Math.Abs(px - x) + System.Math.Abs(pz - z) < 3) continue;
                    pumpX = px; pumpZ = pz;
                    return true;
                }
            }
            bankX = bankZ = pumpX = pumpZ = -1;
            return false;
        }

        private static bool TryRiverNeighbour(int x, int z, out int river)
        {
            foreach (var (nx, nz) in new[] { (x - 1, z), (x + 1, z), (x, z - 1), (x, z + 1) })
                if (WorldTerrain.WaterKind(nx, nz) == WaterKind.River) { river = nx * Size + nz; return true; }
            river = -1;
            return false;
        }
    }
}
