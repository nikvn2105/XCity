using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace XCity.Tests
{
    public sealed class GarbageTests
    {
        private const int Center = CitySimulation.GridSize / 2;

        [Fact]
        public void LandfillServesClosestWholeBuildingsFirstWithinRadius()
        {
            var city = Restore((0, 0, Building(BuildingModel.Landfill)),
                (1, 0, Home), (-2, 0, Home), (3, 0, Home), (4, 0, Home), (12, 0, Home), (13, 0, Home));
            city.SetFunding(BudgetService.Garbage, 50);

            Assert.Equal(90, city.GarbageProduced);
            Assert.Equal(60, city.GarbageCollected);
            Assert.Equal((60, 60), city.GarbageFacilityAt(Center, Center));
            foreach (int x in new[] { 1, -2, 3, 4 }) Assert.True(city.HasGarbageService(Center + x, Center));
            Assert.False(city.HasGarbageService(Center + 12, Center));

            city.SetFunding(BudgetService.Garbage, 150);
            Assert.Equal(75, city.GarbageCollected);
            Assert.True(city.HasGarbageService(Center + 12, Center));
            Assert.False(city.HasGarbageService(Center + 13, Center));
            Assert.Equal((75, 180), city.GarbageFacilityAt(Center, Center));

            city.Tick();
            Assert.Equal(27, city.LastBudget.Garbage);
            Assert.Equal(city.LastBudget.Income - city.LastBudget.Spending, city.LastNetIncome);
        }

        [Fact]
        public void DevelopedWorkplacesProduceByJobsEvenWhenDisconnected()
        {
            var market = new CityCell { Type = CellType.Commercial, Model = BuildingModel.Market, Level = 1, Height = 2.3f };
            var factory = new CityCell { Type = CellType.Industrial, Model = BuildingModel.Factory, Level = 1, Height = 3.8f };
            var city = Restore((0, 0, market), (1, 0, factory), (2, 0, Building(BuildingModel.Clinic)),
                (3, 0, new CityCell { Type = CellType.Commercial, Model = BuildingModel.Market }));
            Assert.Equal(2 + 7 + 4, city.GarbageProduced);
            Assert.Equal(0, city.GarbageCollected);
            Assert.False(city.ProducesGarbage(Center + 3, Center));
        }

        [Fact]
        public void FacilitiesDoNotCountTheSameProducerTwice()
        {
            var city = Restore((-2, 0, Building(BuildingModel.Landfill)),
                (2, 0, Building(BuildingModel.Landfill)), (0, 0, Home), (13, 0, Home));
            Assert.Equal(30, city.GarbageProduced);
            Assert.Equal(30, city.GarbageCollected);
            Assert.Equal((15, 120), city.GarbageFacilityAt(Center - 2, Center));
            Assert.Equal((15, 120), city.GarbageFacilityAt(Center + 2, Center));
        }

        [Fact]
        public void IncineratorNeedsPowerAndPowerChangesRefreshAssignments()
        {
            var city = Restore((0, 0, Building(BuildingModel.Incinerator)), (0, 1, new CityCell { Type = CellType.Road }),
                (0, 2, Home));
            Assert.Equal((0, 360), city.GarbageFacilityAt(Center, Center));
            Assert.False(city.HasGarbageService(Center, Center + 2));
            Assert.True(city.TrySetCell(Center + 1, Center + 1, CellType.PowerPlant));
            Assert.True(city.HasGarbageService(Center, Center + 2));
            Assert.Equal((15, 360), city.GarbageFacilityAt(Center, Center));
            Assert.True(city.TryUndoBuild());
            Assert.False(city.HasGarbageService(Center, Center + 2));
        }

        [Fact]
        public void GracePeriodEndsAtExactly300Residents()
        {
            var small = Restore((0, 0, Home), (0, 1, Home), (0, 2, Home), (0, 3, Home));
            var large = Restore((0, 0, Home), (0, 1, Home), (0, 2, Home), (0, 3, Home), (0, 4, Home));
            Assert.Equal(240, small.Population);
            Assert.Equal(300, large.Population);
            Assert.False(small.HasGarbageService(Center, Center));
            Assert.False(small.NeedsGarbageCollection(Center, Center));
            Assert.True(large.NeedsGarbageCollection(Center, Center));
            Assert.Equal(small.GetHappiness(Center, Center) - 10, large.GetHappiness(Center, Center));
            Assert.Equal(small.GetLandValue(Center, Center) - 10, large.GetLandValue(Center, Center));
            Assert.Equal(3, large.GetCell(Center, Center).Level);
        }

        [Fact]
        public void UnservedIndustryPaysTwentyPercentLessTaxAfterGracePeriod()
        {
            var factory = new CityCell { Type = CellType.Industrial, Model = BuildingModel.Factory, Level = 1, Height = 3.8f };
            var plant = new CityCell { Type = CellType.PowerPlant, Level = 1, Height = CitySimulation.PowerPlantHeight };
            var road = new CityCell { Type = CellType.Road };
            var cells = new List<(int, int, CityCell)> { (1, 0, factory), (1, 1, plant) };
            for (int z = 0; z < 5; z++)
            {
                cells.Add((0, z, road));
                cells.Add((-1, z, Home));
            }
            var unserved = Restore(cells.ToArray());
            Assert.True(unserved.NeedsGarbageCollection(Center + 1, Center));
            unserved.Tick();
            Assert.Equal(12, unserved.LastBudget.IndustrialTax);

            cells.Add((7, 0, Building(BuildingModel.Landfill)));
            var served = Restore(cells.ToArray());
            Assert.True(served.HasGarbageService(Center + 1, Center));
            served.Tick();
            Assert.Equal(15, served.LastBudget.IndustrialTax);
        }

        [Theory]
        [InlineData(11)]
        [InlineData(12)]
        public void OlderFundingKeepsAllFourRatesAndNewServiceDefaultsTo100(int schema)
        {
            var snapshot = new CitySimulation().CreateSnapshot();
            snapshot.schemaVersion = schema;
            snapshot.serviceFunding = new[] { 50, 70, 110, 150 };
            Assert.True(CitySimulation.TryRestore(snapshot, out var city));
            Assert.Equal(50, city.GetFunding(BudgetService.Education));
            Assert.Equal(70, city.GetFunding(BudgetService.Health));
            Assert.Equal(110, city.GetFunding(BudgetService.Police));
            Assert.Equal(150, city.GetFunding(BudgetService.Fire));
            Assert.Equal(100, city.GetFunding(BudgetService.Garbage));
            city.SetFunding(BudgetService.Garbage, 130);
            Assert.True(CitySimulation.TryRestore(city.CreateSnapshot(), out var restored));
            Assert.Equal(130, restored.GetFunding(BudgetService.Garbage));
        }

        [Fact]
        public void WasteFacilitiesInvalidateLandValueAndUseTheirOwnPollutionStrengths()
        {
            var city = new CitySimulation();
            int initial = city.GetLandValue(Center, Center);
            Assert.True(city.TrySetCell(Center + 1, Center, CellType.CivicService, BuildingModel.Landfill));
            Assert.Equal(initial - 18, city.GetLandValue(Center, Center));
            Assert.True(city.TryUndoBuild());
            Assert.Equal(initial, city.GetLandValue(Center, Center));

            var incinerator = Restore((1, 0, Building(BuildingModel.Incinerator)));
            Assert.Equal(initial - 10, incinerator.GetLandValue(Center, Center));
            var snapshot = incinerator.CreateSnapshot();
            Assert.True(snapshot.schemaVersion >= 13);
            snapshot.schemaVersion = 12;
            Assert.False(CitySimulation.TryRestore(snapshot, out _));
        }

        private static CityCell Home =>
            new CityCell { Type = CellType.Residential, Model = BuildingModel.Apartment, Level = 3, Height = 6.4f };

        private static CityCell Building(BuildingModel model) =>
            new CityCell { Type = CellType.CivicService, Model = model, Level = 1, Height = BuildingCatalog.Get(model).Height };

        private static CitySimulation Restore(params (int X, int Z, CityCell Cell)[] source)
        {
            var ordered = source.OrderBy(cell => cell.X).ThenBy(cell => cell.Z).ToArray();
            var snapshot = new CitySimulation().CreateSnapshot();
            snapshot.cellIndices = ordered.Select(cell => CitySimulation.CellIndex(Center + cell.X, Center + cell.Z)).ToArray();
            snapshot.cells = ordered.Select(cell => cell.Cell).ToArray();
            Assert.True(CitySimulation.TryRestore(snapshot, out var city));
            return city;
        }
    }
}
