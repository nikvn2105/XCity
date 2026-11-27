using System;
using System.Linq;
using Xunit;

namespace XCity.Tests
{
    public class ServiceTierTests
    {
        private static readonly (BuildingModel Small, BuildingModel Big, ServiceCoverage Coverage)[] Families =
        {
            (BuildingModel.PolicePost, BuildingModel.PoliceStation, ServiceCoverage.Police),
            (BuildingModel.Clinic, BuildingModel.Hospital, ServiceCoverage.Health),
            (BuildingModel.FireStationSmall, BuildingModel.FireStation, ServiceCoverage.Fire)
        };

        private static (int X, int Z) FindLand(int width, int depth)
        {
            int center = CitySimulation.GridSize / 2;
            for (int x = center - 60; x < CitySimulation.GridSize - width; x++)
            for (int z = center - 60; z < CitySimulation.GridSize - depth; z++)
            {
                bool fits = true;
                for (int dx = 0; dx < width && fits; dx++)
                for (int dz = 0; dz < depth && fits; dz++)
                    fits = CitySimulation.IsBuildable(x + dx, z + dz);
                if (fits) return (x, z);
            }
            throw new InvalidOperationException("No land found");
        }

        [Fact]
        public void SmallServices_AreOpenFromTheStartWhileBigOnesStayLocked()
        {
            var (x, z) = FindLand(2, 6);
            var city = new CitySimulation();
            for (int i = 0; i < Families.Length; i++)
            {
                var (small, big, _) = Families[i];
                Assert.Equal(0, BuildingCatalog.UnlockPopulation(small));
                Assert.True(BuildingCatalog.UnlockPopulation(big) > 0);
                Assert.True(city.TrySetCell(x, z + i, CellType.CivicService, small), $"{small} should be placeable at 0 population");
                Assert.False(city.TrySetCell(x + 1, z + i, CellType.CivicService, big), $"{big} should still be locked");
            }
        }

        [Fact]
        public void SmallServices_AreCheaperAndReachLessThanTheirBigVersion()
        {
            foreach (var (small, big, coverage) in Families)
            {
                Assert.Equal(coverage, ServiceCatalog.Get(small).Coverage);
                Assert.Equal(coverage, ServiceCatalog.Get(big).Coverage);
                Assert.True(ServiceCatalog.Get(small).Radius < ServiceCatalog.Get(big).Radius);
                Assert.True(BuildingCatalog.Get(small).Cost < BuildingCatalog.Get(big).Cost);
                Assert.True(BuildingCatalog.ServiceUpkeep(small) < BuildingCatalog.ServiceUpkeep(big));
            }
        }

        [Fact]
        public void PolicePost_CoversItsRadiusOnceItHasRoadAndPower()
        {
            var (x, z) = FindLand(3, 4);
            var city = new CitySimulation();
            Assert.True(city.TryApplyBuild(Enumerable.Range(z, 4).Select(dz => (x, dz)), CellType.Road));
            Assert.True(city.TrySetCell(x + 1, z, CellType.PowerPlant));
            Assert.True(city.TrySetCell(x + 1, z + 1, CellType.CivicService, BuildingModel.PolicePost));

            int radius = ServiceCatalog.Get(BuildingModel.PolicePost).Radius;
            Assert.Equal(ServiceCoverage.Police, city.GetCoverage(x + 1 + radius, z + 1));
            Assert.Equal(ServiceCoverage.None, city.GetCoverage(x + 2 + radius, z + 1));
        }

        [Fact]
        public void Saves_RoundTripSmallServicesAndOlderSchemasCannotContainThem()
        {
            var (x, z) = FindLand(1, 1);
            var city = new CitySimulation();
            Assert.True(city.TrySetCell(x, z, CellType.CivicService, BuildingModel.Clinic));

            CitySnapshot snapshot = city.CreateSnapshot();
            Assert.True(CitySimulation.TryRestore(snapshot, out var restored));
            Assert.Equal(BuildingModel.Clinic, restored.GetCell(x, z).Model);

            snapshot.schemaVersion = 9;
            Assert.False(CitySimulation.TryRestore(snapshot, out _));
        }
    }
}
