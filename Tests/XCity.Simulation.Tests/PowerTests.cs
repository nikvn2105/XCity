using System.Linq;
using Xunit;

namespace XCity.Tests
{
    public class PowerTests
    {
        [Fact]
        public void GasPlant_UnlocksAt2000AndOutpowersTheBasicPlant()
        {
            var (x, z) = EconomyTests.FindLand(3, 4);
            var fresh = new CitySimulation(2);
            Assert.True(fresh.TryApplyBuild(Enumerable.Range(z, 4).Select(dz => (x, dz)), CellType.Road));
            Assert.False(fresh.TrySetCell(x + 1, z, CellType.PowerPlant, BuildingModel.GasPlant)); // locked

            var city = UnlockTests.UnlockedCity();
            Assert.True(city.TryApplyBuild(Enumerable.Range(z, 4).Select(dz => (x, dz)), CellType.Road));
            int money = city.Money;
            Assert.True(city.TrySetCell(x + 1, z, CellType.PowerPlant, BuildingModel.GasPlant));
            Assert.Equal(CitySimulation.GasPlantCost, money - city.Money);
            Assert.Equal(BuildingModel.GasPlant, city.GetCell(x + 1, z).Model);
            Assert.True(CitySimulation.PowerCapacity(BuildingModel.GasPlant) > 4 * CitySimulation.PowerPlantCapacity);

            city.Tick();
            Assert.Equal(CitySimulation.GasPlantUpkeep, city.LastBudget.Power);

            CitySnapshot snapshot = city.CreateSnapshot();
            Assert.True(CitySimulation.TryRestore(snapshot, out var restored));
            Assert.Equal(BuildingModel.GasPlant, restored.GetCell(x + 1, z).Model);
            snapshot.schemaVersion = 14;
            Assert.False(CitySimulation.TryRestore(snapshot, out _));
        }
    }
}
