using System;
using Xunit;

namespace XCity.Tests
{
    public sealed class LandValueTests
    {
        private const int Center = CitySimulation.GridSize / 2;

        [Fact]
        public void ServiceAndParkRaiseLandValueAndParkDoesNotCountAsCoverage()
        {
            var city = new CitySimulation();
            int x = Center, z = Center;
            Assert.True(city.TrySetCell(x, z, CellType.Road));
            Assert.True(city.TrySetCell(x + 1, z, CellType.PowerPlant));
            int initial = city.GetLandValue(x, z);
            Assert.True(city.TrySetCell(x - 1, z, CellType.CivicService, BuildingModel.PolicePost));
            Assert.Equal(ServiceCoverage.Police, city.GetCoverage(x, z));
            Assert.Equal(initial + 8, city.GetLandValue(x, z));

            Assert.True(city.TrySetCell(x, z + 2, CellType.CivicService, BuildingModel.SmallPark));
            Assert.Equal(ServiceCoverage.Police, city.GetCoverage(x, z));
            Assert.Equal(initial + 12, city.GetLandValue(x, z));
        }

        [Fact]
        public void NaturalWaterRaisesLandValueWithoutUtilities()
        {
            var city = new CitySimulation();
            for (int x = Center - 1; x >= 1; x--)
            {
                if (!WorldTerrain.IsLand(x, Center) || WorldTerrain.IsLand(x - 1, Center)) continue;
                Assert.False(city.HasWater(x, Center));
                Assert.Equal(50, city.GetLandValue(Center, Center));
                Assert.Equal(62, city.GetLandValue(x, Center));
                Assert.Equal(112, city.LandValueTaxPercent(x, Center));
                return;
            }
            throw new InvalidOperationException("No shore on the test transect.");
        }

        [Theory]
        [InlineData(CellType.Industrial)]
        [InlineData(CellType.PowerPlant)]
        public void PollutionInvalidatesOnlyCellsInReachOnBuildAndUndo(CellType type)
        {
            var city = new CitySimulation();
            int x = Center, z = Center, radius = CitySimulation.LandPollutionRadius;
            int near = city.GetLandValue(x + radius, z);
            int outside = city.GetLandValue(x + radius + 1, z);
            Assert.True(city.TrySetCell(x, z, type));

            Assert.Equal(near - 18, city.GetLandValue(x + radius, z));
            Assert.Equal(outside, city.GetLandValue(x + radius + 1, z));
            Assert.True(city.TryUndoBuild());
            Assert.Equal(near, city.GetLandValue(x + radius, z));
        }

        [Fact]
        public void TrafficPenaltyUpdatesWhenTheSameNetworkStopsJamming()
        {
            int x = Center, z = Center;
            var snapshot = new CitySimulation().CreateSnapshot();
            snapshot.cellIndices = new[]
            {
                CitySimulation.CellIndex(x, z), CitySimulation.CellIndex(x, z + 1),
                CitySimulation.CellIndex(x + 1, z), CitySimulation.CellIndex(x + 1, z + 1),
                CitySimulation.CellIndex(x + 2, z), CitySimulation.CellIndex(x + 2, z + 1)
            };
            var factory = new CityCell { Type = CellType.Industrial, Model = BuildingModel.Factory, Level = 1, Height = 3.8f };
            snapshot.cells = new[]
            {
                new CityCell { Type = CellType.Residential, Model = BuildingModel.Apartment, Level = 3, Height = 6.4f },
                new CityCell { Type = CellType.PowerPlant, Level = 1, Height = CitySimulation.PowerPlantHeight },
                new CityCell { Type = CellType.Road },
                new CityCell { Type = CellType.Road },
                factory,
                factory
            };

            Assert.True(CitySimulation.TryRestore(snapshot, out var city));
            Assert.True(city.GetTrafficLoad(x, z) > 100);
            int jammed = city.GetLandValue(x, z);
            Assert.True(city.TrySetCell(x + 2, z + 1, CellType.Empty));
            Assert.Equal(100, city.GetTrafficLoad(x, z));
            Assert.Equal(jammed + 12, city.GetLandValue(x, z));
        }

        [Fact]
        public void ParksAffectHappinessOnlyInRangeAndUndoRestoresInfluence()
        {
            var city = new CitySimulation();
            int x = Center, z = Center;
            int radius = ParkCatalog.Get(BuildingModel.SmallPark).Radius;
            Assert.True(city.TrySetCell(x + radius, z, CellType.Residential));
            Assert.True(city.TrySetCell(x + radius, z + 1, CellType.Residential));
            int baseline = city.GetHappiness(x + radius, z);

            Assert.True(city.TrySetCell(x, z, CellType.CivicService, BuildingModel.SmallPark));
            Assert.Equal(4, city.ParkBonusAt(x + radius, z));
            Assert.Equal(0, city.ParkBonusAt(x + radius, z + 1));
            Assert.Equal(0, city.ParkBonusAt(x, z));
            Assert.Equal(baseline + 4, city.GetHappiness(x + radius, z));
            Assert.Equal(ZoneStatus.Developed, city.GetZoneStatus(x, z));
            Assert.True(city.TrySetCell(x, z, CellType.Empty));
            Assert.Equal(0, city.ParkBonusAt(x + radius, z));
            Assert.True(city.TryUndoBuild());
            Assert.Equal(4, city.ParkBonusAt(x + radius, z));
        }

        [Theory]
        [InlineData(10)]
        [InlineData(11)]
        public void OlderSavesLoadAndParkModelsRequireSchema12(int version)
        {
            var city = new CitySimulation();
            Assert.True(city.TrySetCell(Center, Center + 1, CellType.Residential));
            var snapshot = city.CreateSnapshot();
            snapshot.schemaVersion = version;
            Assert.True(CitySimulation.TryRestore(snapshot, out var legacy));
            Assert.Equal(city.Money, legacy.Money);
            Assert.Equal(0, legacy.ParkBonusAt(Center, Center + 1));

            Assert.True(city.TrySetCell(Center, Center, CellType.CivicService, BuildingModel.SmallPark));
            snapshot = city.CreateSnapshot();
            Assert.Equal(CitySimulation.CurrentSchemaVersion, snapshot.schemaVersion);
            snapshot.schemaVersion = 12;
            Assert.True(CitySimulation.TryRestore(snapshot, out var restored));
            Assert.Equal(4, restored.ParkBonusAt(Center, Center + 1));
            snapshot.schemaVersion = version;
            Assert.False(CitySimulation.TryRestore(snapshot, out _));
        }
    }
}
