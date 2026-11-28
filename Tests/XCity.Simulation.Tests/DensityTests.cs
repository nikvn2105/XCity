using System.Linq;
using Xunit;

namespace XCity.Tests
{
    public class DensityTests
    {
        // A powered street with an apartment block at (x + 1, z + 8), far from the plant's pollution.
        private static (CitySimulation City, int X, int Z) ApartmentStreet(bool withServices)
        {
            var (x, z) = EconomyTests.FindLand(3, 12);
            var city = EconomyTests.PoweredStreet(x, z, 12, unlocked: true);
            Assert.True(city.TrySetCell(x + 1, z + 8, CellType.Residential, BuildingModel.Apartment));
            if (withServices)
            {
                Assert.True(city.TrySetCell(x + 1, z + 9, CellType.CivicService, BuildingModel.PolicePost));
                Assert.True(city.TrySetCell(x + 1, z + 10, CellType.CivicService, BuildingModel.Clinic));
            }
            return (city, x, z);
        }

        [Fact]
        public void HighDensity_NeedsLandValueToDevelop()
        {
            var (bare, x, z) = ApartmentStreet(withServices: false);
            var (served, _, _) = ApartmentStreet(withServices: true);
            Assert.True(bare.LandValueBlocksGrowth(x + 1, z + 8));
            Assert.False(served.LandValueBlocksGrowth(x + 1, z + 8));
            for (int i = 0; i < 200; i++)
            {
                bare.Tick();
                served.Tick();
            }
            Assert.Equal(0, bare.GetCell(x + 1, z + 8).Level);
            Assert.True(served.GetCell(x + 1, z + 8).Level > 0);
        }

        [Fact]
        public void LandValueDrop_StopsGrowthButNeverRemovesLevels()
        {
            var (city, x, z) = ApartmentStreet(withServices: true);
            for (int i = 0; i < 200; i++) city.Tick();
            int level = city.GetCell(x + 1, z + 8).Level;
            Assert.True(level > 0);

            Assert.True(city.TrySetCell(x - 1, z + 8, CellType.Industrial));
            Assert.True(city.LandValueBlocksGrowth(x + 1, z + 8));
            for (int i = 0; i < 200; i++) city.Tick();
            Assert.Equal(level, city.GetCell(x + 1, z + 8).Level);
        }

        [Fact]
        public void Densities_FollowTheBuildingType()
        {
            Assert.Equal(Density.Low, BuildingCatalog.DensityOf(BuildingModel.Villa));
            Assert.Equal(Density.Medium, BuildingCatalog.DensityOf(BuildingModel.RowHouse));
            Assert.Equal(Density.High, BuildingCatalog.DensityOf(BuildingModel.Apartment));
            Assert.Equal(Density.High, BuildingCatalog.DensityOf(BuildingModel.OfficeTower));
            // Industry is never gated: it lowers land value itself.
            Assert.Equal(0, BuildingCatalog.MinLandValue(BuildingCatalog.DensityOf(BuildingModel.Factory)));
        }
    }
}
