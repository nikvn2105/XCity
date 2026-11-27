using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace XCity.Tests
{
    public class NetworkTests
    {
        private static readonly CityCell Road = new CityCell { Type = CellType.Road };
        private static readonly CityCell Plant = new CityCell { Type = CellType.PowerPlant, Level = 1, Height = CitySimulation.PowerPlantHeight };
        private static CityCell Apartment(int level) => new CityCell { Type = CellType.Residential, Model = BuildingModel.Apartment, Level = level, Height = 5f };
        private static CityCell Factory() => new CityCell { Type = CellType.Industrial, Model = BuildingModel.Factory, Level = 1, Height = 3.8f };
        private static CityCell Service(BuildingModel model) =>
            new CityCell { Type = BuildingCatalog.Get(model).Type, Model = model, Level = 1, Height = BuildingCatalog.Get(model).Height };

        private static (int X, int Z) FindLand(int minX, int width, int depth)
        {
            for (int x = minX; x < CitySimulation.GridSize - width; x++)
            for (int z = CitySimulation.GridSize / 2 - 60; z < CitySimulation.GridSize - depth; z++)
            {
                bool fits = true;
                for (int dx = 0; dx < width && fits; dx++)
                for (int dz = 0; dz < depth && fits; dz++)
                    fits = CitySimulation.IsBuildable(x + dx, z + dz);
                if (fits) return (x, z);
            }
            throw new InvalidOperationException("No land found");
        }

        // Builds a city straight from developed cells, so tests do not depend on random growth.
        private static CitySimulation City(Dictionary<(int X, int Z), CityCell> cells)
        {
            var sorted = cells.Select(kv => (Index: CitySimulation.CellIndex(kv.Key.X, kv.Key.Z), Cell: kv.Value))
                .OrderBy(e => e.Index).ToList();
            var snapshot = new CitySnapshot
            {
                schemaVersion = CitySimulation.CurrentSchemaVersion,
                money = 50000,
                randomState = 1,
                cellIndices = sorted.Select(e => e.Index).ToArray(),
                cells = sorted.Select(e => e.Cell).ToArray()
            };
            Assert.True(CitySimulation.TryRestore(snapshot, out var city));
            Assert.Equal(0, city.DroppedCellsOnLoad);
            return city;
        }

        [Fact]
        public void School_ServesNearestHomesUntilSeatsRunOut()
        {
            // Preschool: 60 seats, radius 6. Level-3 apartments hold 60 residents, so 15 students each:
            // of the 6 homes in reach, the nearest 4 fit.
            var (x, z) = FindLand(CitySimulation.GridSize / 2 - 60, 3, 22);
            var cells = new Dictionary<(int, int), CityCell>();
            for (int dz = 0; dz < 22; dz++) cells[(x + 1, z + dz)] = Road;
            cells[(x + 2, z)] = Plant;
            cells[(x + 2, z + 1)] = Plant;
            cells[(x, z)] = Service(BuildingModel.Preschool);
            for (int dz = 1; dz < 22; dz++) cells[(x, z + dz)] = Apartment(3);
            var city = City(cells);

            int students = BuildingCatalog.Residents(Apartment(3)) * CitySimulation.StudentPercent / 100;
            Assert.Equal(15, students);
            Assert.Equal(60, city.GetSchoolStudents(x, z));
            int radius = ServiceCatalog.Get(BuildingModel.Preschool).Radius;
            bool Served(int dz) => (city.GetCoverage(x, z + dz) & ServiceCoverage.Education) != 0;
            Assert.Equal(6, radius);
            Assert.All(Enumerable.Range(1, 4), dz => Assert.True(Served(dz), $"home {dz} should have a seat"));
            Assert.All(Enumerable.Range(5, 2), dz => Assert.False(Served(dz), $"home {dz} is past capacity"));
            // Roads and other non-home cells in reach still count as covered.
            Assert.True((city.GetCoverage(x + 1, z + 5) & ServiceCoverage.Education) != 0);
        }

        [Fact]
        public void Jobs_OnlyHireWorkersOnTheSameRoadNetwork()
        {
            var (x, z) = FindLand(CitySimulation.GridSize / 2 - 60, 3, 25);
            var cells = new Dictionary<(int, int), CityCell>();
            // Network A, rows 0-9: homes and a factory. Network B, rows 15-24: a factory with no homes.
            for (int dz = 0; dz < 10; dz++) cells[(x + 1, z + dz)] = Road;
            for (int dz = 15; dz < 25; dz++) cells[(x + 1, z + dz)] = Road;
            cells[(x + 2, z)] = Plant;
            cells[(x + 2, z + 15)] = Plant;
            for (int dz = 1; dz < 9; dz++) cells[(x, z + dz)] = Apartment(1);
            cells[(x + 2, z + 1)] = Factory();
            cells[(x + 2, z + 16)] = Factory();
            var city = City(cells);

            Assert.Equal(100, city.EmploymentAt(x + 2, z + 1));
            Assert.Equal(0, city.EmploymentAt(x + 2, z + 16));
        }

        [Fact]
        public void Traffic_JamsWhenTooFewRoadsCarryTheCommute()
        {
            // Two road tiles carry 20 commuters; level-3 apartments (60 residents, 36 workers) and two factories (40 jobs) overload them.
            var (x, z) = FindLand(CitySimulation.GridSize / 2 - 60, 3, 2);
            var cells = new Dictionary<(int, int), CityCell>
            {
                [(x + 1, z)] = Road,
                [(x + 1, z + 1)] = Road,
                [(x, z)] = Apartment(3),
                [(x, z + 1)] = Plant,
                [(x + 2, z)] = Factory(),
                [(x + 2, z + 1)] = Factory()
            };
            var city = City(cells);

            int load = city.GetTrafficLoad(x + 1, z);
            Assert.True(load > 100, $"load {load}");
            Assert.Equal(load, city.GetTrafficLoad(x, z));
            Assert.Equal(CitySimulation.BaseHappiness - 2 * CitySimulation.MissingUtilityPenalty - CitySimulation.CongestionPenalty,
                city.GetHappiness(x, z));
        }

        [Fact]
        public void Traffic_IsLightOnAWellConnectedStreet()
        {
            var (x, z) = FindLand(CitySimulation.GridSize / 2 - 60, 3, 20);
            var cells = new Dictionary<(int, int), CityCell>();
            for (int dz = 0; dz < 20; dz++) cells[(x + 1, z + dz)] = Road;
            cells[(x, z)] = Apartment(3);
            cells[(x, z + 1)] = Plant;
            cells[(x + 2, z)] = Factory();
            var city = City(cells);

            Assert.InRange(city.GetTrafficLoad(x + 1, z + 10), 1, 100);
        }
    }
}
