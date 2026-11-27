using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace XCity.Tests
{
    public class UnlockTests
    {
        internal static CitySimulation UnlockedCity(uint seed = 1)
        {
            CitySnapshot snapshot = new CitySimulation(seed).CreateSnapshot();
            snapshot.peakPopulation = 5000;
            Assert.True(CitySimulation.TryRestore(snapshot, out var city));
            return city;
        }

        private static CitySimulation CityWithPeak(int peakPopulation, int money = 50000, uint seed = 1)
        {
            CitySnapshot snapshot = new CitySimulation(seed).CreateSnapshot();
            snapshot.peakPopulation = peakPopulation;
            snapshot.money = money;
            Assert.True(CitySimulation.TryRestore(snapshot, out var city));
            return city;
        }

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

        private static (int X, int Z) FindShore()
        {
            for (int x = 1; x < CitySimulation.GridSize - 1; x++)
            for (int z = 1; z < CitySimulation.GridSize - 1; z++)
            {
                if (CitySimulation.IsBuildable(x, z) &&
                    CitySimulation.TryHarborFacing(x, z, out _, out _, out _))
                    return (x, z);
            }
            throw new InvalidOperationException("No shore found");
        }

        private static (int StartX, int EndX, int Z) FindBridgeSpan()
        {
            for (int z = 0; z < CitySimulation.GridSize; z++)
            for (int x = 0; x < CitySimulation.GridSize - 2; x++)
            {
                if (!CitySimulation.IsBuildable(x, z) || WorldTerrain.IsLand(x + 1, z)) continue;
                int end = x + 1;
                while (end < CitySimulation.GridSize && !WorldTerrain.IsLand(end, z)) end++;
                int water = end - x - 1;
                if (end < CitySimulation.GridSize && water >= 2 && water <= 10 && CitySimulation.IsBuildable(end, z))
                    return (x, end, z);
            }
            throw new InvalidOperationException("No bridge span found");
        }

        [Fact]
        public void UnlockPopulation_MatchesMilestoneSchedule()
        {
            Assert.Equal(0, BuildingCatalog.UnlockPopulation(BuildingModel.Default));
            Assert.Equal(0, BuildingCatalog.UnlockPopulation(BuildingModel.RowHouse));
            Assert.Equal(0, BuildingCatalog.UnlockPopulation(BuildingModel.Villa));
            Assert.Equal(0, BuildingCatalog.UnlockPopulation(BuildingModel.Market));
            Assert.Equal(0, BuildingCatalog.UnlockPopulation(BuildingModel.HighStreet));
            Assert.Equal(0, BuildingCatalog.UnlockPopulation(BuildingModel.Workshop));
            Assert.Equal(0, BuildingCatalog.UnlockPopulation(BuildingModel.Warehouse));
            Assert.Equal(0, BuildingCatalog.UnlockPopulation(BuildingModel.WaterPump));
            Assert.Equal(0, BuildingCatalog.UnlockPopulation(BuildingModel.SewagePlant));

            Assert.Equal(500, BuildingCatalog.UnlockPopulation(BuildingModel.Apartment));
            Assert.Equal(500, BuildingCatalog.UnlockPopulation(BuildingModel.Office));
            Assert.Equal(500, BuildingCatalog.UnlockPopulation(BuildingModel.Factory));
            Assert.Equal(500, BuildingCatalog.UnlockPopulation(BuildingModel.Preschool));
            Assert.Equal(500, BuildingCatalog.UnlockPopulation(BuildingModel.PrimarySchool));
            Assert.Equal(500, BuildingCatalog.UnlockPopulation(BuildingModel.PoliceStation));

            Assert.Equal(2000, BuildingCatalog.UnlockPopulation(BuildingModel.HighSchool));
            Assert.Equal(2000, BuildingCatalog.UnlockPopulation(BuildingModel.Hospital));
            Assert.Equal(2000, BuildingCatalog.UnlockPopulation(BuildingModel.FireStation));
            Assert.Equal(2000, BuildingCatalog.UnlockPopulation(BuildingModel.Harbor));

            Assert.Equal(5000, BuildingCatalog.UnlockPopulation(BuildingModel.University));
            Assert.Equal(5000, BuildingCatalog.UnlockPopulation(BuildingModel.OfficeTower));
            Assert.Equal(5000, BuildingCatalog.UnlockPopulation(BuildingModel.ShoppingMall));
        }

        [Fact]
        public void LockedModel_IsRejectedAtZeroAndAcceptedOncePeakReachesThreshold()
        {
            var (x, z) = FindLand(4, 4);
            var (sx, sz) = FindShore();
            var (bsx, bex, bz) = FindBridgeSpan();

            var starter = new CitySimulation();
            Assert.True(starter.TrySetCell(x, z, CellType.Road));
            Assert.True(starter.TrySetCell(x + 1, z, CellType.PowerPlant));
            Assert.True(starter.TrySetCell(x + 2, z, CellType.Residential));
            Assert.True(starter.TrySetCell(x + 2, z + 1, CellType.Residential, BuildingModel.RowHouse));
            Assert.True(starter.TryBuildBridge(bsx, bz, bex, bz));

            Assert.False(starter.TrySetCell(x, z + 1, CellType.Residential, BuildingModel.Apartment));
            Assert.False(starter.TryApplyBuild(new[] { (x, z + 1) }, CellType.School, BuildingModel.Preschool));
            Assert.False(starter.TrySetCell(x, z + 2, CellType.CivicService, BuildingModel.Hospital));
            Assert.False(starter.TrySetCell(sx, sz, CellType.CivicService, BuildingModel.Harbor));
            Assert.False(starter.TrySetCell(x, z + 3, CellType.School, BuildingModel.University));

            var at500 = CityWithPeak(500);
            Assert.True(at500.TrySetCell(x, z + 1, CellType.Residential, BuildingModel.Apartment));
            Assert.True(at500.TrySetCell(x, z + 2, CellType.School, BuildingModel.Preschool));
            Assert.False(at500.TrySetCell(x, z + 3, CellType.CivicService, BuildingModel.Hospital));

            var at2000 = CityWithPeak(2000);
            Assert.True(at2000.TrySetCell(x, z + 1, CellType.CivicService, BuildingModel.Hospital));
            Assert.True(at2000.TrySetCell(sx, sz, CellType.CivicService, BuildingModel.Harbor));
            Assert.False(at2000.TrySetCell(x, z + 2, CellType.Commercial, BuildingModel.ShoppingMall));

            var at5000 = CityWithPeak(5000);
            Assert.True(at5000.TrySetCell(x, z + 1, CellType.School, BuildingModel.University));
            Assert.True(at5000.TrySetCell(x, z + 2, CellType.Commercial, BuildingModel.OfficeTower));
            Assert.True(at5000.TrySetCell(x, z + 3, CellType.Commercial, BuildingModel.ShoppingMall));
        }

        [Fact]
        public void PeakPopulation_UpdatesOnTickAndNeverDropsWhenPopulationFalls()
        {
            var (x, z) = FindLand(6, 30);
            var city = new CitySimulation(5);
            Assert.True(city.TryApplyBuild(Enumerable.Range(z, 30).Select(dz => (x + 1, dz)), CellType.Road));
            Assert.True(city.TryApplyBuild(Enumerable.Range(z, 30).Select(dz => (x + 4, dz)), CellType.Road));
            Assert.True(city.TryApplyBuild(new[] { (x + 2, z), (x + 3, z) }, CellType.Road));
            for (int dz = 0; dz < 5; dz++)
                Assert.True(city.TrySetCell(x + 5, z + dz, CellType.PowerPlant));

            var homes = new List<(int X, int Z)>();
            for (int dz = 1; dz < 30; dz++)
            {
                Assert.True(city.TrySetCell(x, z + dz, CellType.Residential));
                Assert.True(city.TrySetCell(x + 2, z + dz, CellType.Residential));
                homes.Add((x, z + dz));
                homes.Add((x + 2, z + dz));
                Assert.True(city.TrySetCell(x + 3, z + dz, dz % 3 == 0 ? CellType.Commercial : CellType.Industrial));
            }

            for (int i = 0; i < 300 && city.PeakPopulation < 500; i++)
                city.Tick();

            Assert.True(city.PeakPopulation >= 500, $"expected peak >= 500, got {city.PeakPopulation}");
            Assert.True(city.TrySetCell(x + 5, z + 6, CellType.CivicService, BuildingModel.PoliceStation));
            int recordedPeak = city.PeakPopulation;

            Assert.True(city.TryApplyBuild(homes, CellType.Empty));
            city.Tick();

            Assert.Equal(0, city.Population);
            Assert.Equal(recordedPeak, city.PeakPopulation);
            Assert.True(city.TrySetCell(x + 5, z + 7, CellType.School, BuildingModel.PrimarySchool));
        }

        [Fact]
        public void LockedBuildingsInSave_AreKeptAndCanBeDemolishedAndUndone()
        {
            var (x, z) = FindLand(3, 4);
            var snapshot = new CitySnapshot
            {
                schemaVersion = CitySimulation.CurrentSchemaVersion,
                money = 50000,
                randomState = 1,
                peakPopulation = 0,
                cellIndices = new[]
                {
                    CitySimulation.CellIndex(x, z),
                    CitySimulation.CellIndex(x + 1, z)
                },
                cells = new[]
                {
                    new CityCell
                    {
                        Type = CellType.CivicService,
                        Model = BuildingModel.Hospital,
                        Level = 1,
                        Height = BuildingCatalog.Get(BuildingModel.Hospital).Height
                    },
                    new CityCell
                    {
                        Type = CellType.School,
                        Model = BuildingModel.University,
                        Level = 1,
                        Height = BuildingCatalog.Get(BuildingModel.University).Height
                    }
                }
            };

            Assert.True(CitySimulation.TryRestore(snapshot, out var city));
            Assert.Equal(0, city.PeakPopulation);
            Assert.Equal(BuildingModel.Hospital, city.GetCell(x, z).Model);
            Assert.Equal(BuildingModel.University, city.GetCell(x + 1, z).Model);

            // Placing a locked model fails, but demolishing an already-saved locked building and undoing it succeed.
            Assert.False(city.TrySetCell(x + 2, z, CellType.CivicService, BuildingModel.FireStation));
            Assert.True(city.TrySetCell(x, z, CellType.Empty));
            Assert.Equal(CellType.Empty, city.GetCell(x, z).Type);

            Assert.True(city.TryUndoBuild());
            Assert.Equal(BuildingModel.Hospital, city.GetCell(x, z).Model);
        }

        [Fact]
        public void Save_RoundTripsPeakPopulationAndRejectsNegativePeak()
        {
            var city = CityWithPeak(2400);
            CitySnapshot saved = city.CreateSnapshot();
            Assert.Equal(CitySimulation.CurrentSchemaVersion, saved.schemaVersion);
            Assert.Equal(2400, saved.peakPopulation);

            Assert.True(CitySimulation.TryRestore(saved, out var restored));
            Assert.Equal(2400, restored.PeakPopulation);

            CitySnapshot tampered = city.CreateSnapshot();
            tampered.peakPopulation = -1;
            Assert.False(CitySimulation.TryRestore(tampered, out _));
        }

        [Fact]
        public void Schema8Save_LoadsWithPeakEqualToCurrentPopulation()
        {
            var (x, z) = FindLand(3, 6);
            var cellList = new List<(int Index, CityCell Cell)>();
            for (int dz = 0; dz < 4; dz++)
            {
                cellList.Add((CitySimulation.CellIndex(x, z + dz), new CityCell
                {
                    Type = CellType.Residential,
                    Model = BuildingModel.Apartment,
                    Level = 3,
                    Height = 5f
                }));
            }
            cellList.Sort((a, b) => a.Index.CompareTo(b.Index));

            var schema8 = new CitySnapshot
            {
                schemaVersion = 8,
                money = 50000,
                randomState = 1,
                peakPopulation = 0,
                cellIndices = cellList.Select(e => e.Index).ToArray(),
                cells = cellList.Select(e => e.Cell).ToArray()
            };

            Assert.True(CitySimulation.TryRestore(schema8, out var restored));
            Assert.Equal(240, restored.Population);
            Assert.Equal(restored.Population, restored.PeakPopulation);
        }

        [Fact]
        public void StarterCityWithOnlyMilestoneZeroBuildings_ExceedsFiveHundredPopulationInThousandTicks()
        {
            var (x, z) = FindLand(6, 30);
            var city = new CitySimulation(1);
            Assert.Equal(0, city.PeakPopulation);

            // Milestone-0 buildings only, arranged across two connected streets with enough power plants.
            var starterModels = new[]
            {
                BuildingModel.RowHouse,
                BuildingModel.Market,
                BuildingModel.HighStreet,
                BuildingModel.Workshop,
                BuildingModel.Warehouse
            };
            for (int i = 0; i < starterModels.Length; i++)
                Assert.Equal(0, BuildingCatalog.UnlockPopulation(starterModels[i]));

            Assert.True(city.TryApplyBuild(Enumerable.Range(z, 30).Select(dz => (x + 1, dz)), CellType.Road));
            Assert.True(city.TryApplyBuild(Enumerable.Range(z, 30).Select(dz => (x + 4, dz)), CellType.Road));
            Assert.True(city.TryApplyBuild(new[] { (x + 2, z), (x + 3, z) }, CellType.Road));

            for (int dz = 0; dz < 5; dz++)
                Assert.True(city.TrySetCell(x + 5, z + dz, CellType.PowerPlant));

            for (int dz = 1; dz < 30; dz++)
            {
                Assert.True(city.TrySetCell(x, z + dz, CellType.Residential, BuildingModel.RowHouse));
                Assert.True(city.TrySetCell(x + 2, z + dz, CellType.Residential));
                if (dz % 3 == 0)
                {
                    BuildingModel comModel = dz % 6 == 0 ? BuildingModel.HighStreet : BuildingModel.Market;
                    Assert.True(city.TrySetCell(x + 3, z + dz, CellType.Commercial, comModel));
                }
                else
                {
                    BuildingModel indModel = dz % 2 == 0 ? BuildingModel.Warehouse : BuildingModel.Workshop;
                    Assert.True(city.TrySetCell(x + 3, z + dz, CellType.Industrial, indModel));
                }
            }

            for (int i = 0; i < 1000; i++)
                city.Tick();

            Assert.True(city.Population > 500, $"expected population > 500 after 1000 ticks, got {city.Population} (peak {city.PeakPopulation}, happiness {city.AverageHappiness})");
            Assert.True(city.PeakPopulation > 500);
            Assert.False(city.IsBankrupt);
            Assert.True(city.Money > 0);
        }
    }
}
