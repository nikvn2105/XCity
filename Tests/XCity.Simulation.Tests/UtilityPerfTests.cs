using System;
using System.Collections.Generic;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace XCity.Tests
{
    [CollectionDefinition("Utility performance", DisableParallelization = true)]
    public sealed class UtilityPerformanceCollection { }

    [Collection("Utility performance")]
    public sealed class UtilityPerfTests
    {
        private readonly ITestOutputHelper _output;

        public UtilityPerfTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void LargeCity_UtilityRefreshAfterRoadBuild()
        {
            CitySimulation city = CreateLargeCity(out int occupied);
            var elapsed = new double[21];
            var allocated = new long[elapsed.Length];
            for (int i = -8; i < elapsed.Length; i++)
            {
                long bytes = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                bool built = city.TrySetCell(256, 257, CellType.Road);
                ReadDerivedState(city);
                double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                if (i >= 0)
                {
                    elapsed[i] = milliseconds;
                    allocated[i] = GC.GetAllocatedBytesForCurrentThread() - bytes;
                }
                Assert.True(built);
                Assert.True(city.TryUndoBuild());
                ReadDerivedState(city);
            }

            Array.Sort(elapsed);
            Array.Sort(allocated);
            _output.WriteLine($"{occupied:N0} occupied tiles; median {elapsed[10]:F3} ms, p90 {elapsed[18]:F3} ms; " +
                $"median allocated {allocated[10]:N0} bytes per road build + derived reads.");
        }

        [Fact]
        public void LargeCity_UtilityAndServiceStateMatchesReference()
        {
            CitySimulation city = CreateLargeCity(out _);
            var states = new List<ulong> { Fingerprint(city) };
            Assert.True(city.TrySetCell(256, 257, CellType.Road));
            states.Add(Fingerprint(city));
            Assert.True(city.TrySetCell(255, 256, CellType.Empty));
            states.Add(Fingerprint(city));
            Assert.True(city.TryUndoBuild());
            states.Add(Fingerprint(city));
            Assert.True(city.TryUndoBuild());
            states.Add(Fingerprint(city));
            city.Tick();
            states.Add(Fingerprint(city));
            // Golden values captured on 6f1cdab before replacing the utility algorithms.
            Assert.Equal(new[]
            {
                0x3394BA78B2E9D378UL, 0xBE934DC03F8225F9UL, 0x9AE3CF32875D73B0UL,
                0xBE934DC03F8225F9UL, 0x3394BA78B2E9D378UL, 0x1E1545788E623FA9UL
            }, states);
        }

        private static void ReadDerivedState(CitySimulation city)
        {
            _ = city.UnpoweredZones;
            _ = city.HasWater(256, 256);
            _ = city.HasSewage(256, 256);
            _ = city.GetCoverage(256, 256);
            _ = city.GetTrafficLoad(256, 256);
        }

        private static CitySimulation CreateLargeCity(out int occupied)
        {
            var indices = new List<int>();
            var cells = new List<CityCell>();
            int shorelineUtilities = 0;
            for (int x = 105; x <= 407; x++)
            for (int z = 105; z <= 407; z++)
            {
                if ((x == 256 && z == 257) || !CitySimulation.IsBuildable(x, z)) continue;
                CityCell cell;
                if (x % 3 == 0 || z % 24 == 16)
                    cell = new CityCell { Type = CellType.Road };
                else if (shorelineUtilities < 24 && CitySimulation.TryHarborFacing(x, z, out _, out _, out _))
                    cell = Developed(shorelineUtilities++ % 2 == 0 ? BuildingModel.WaterPump : BuildingModel.SewagePlant);
                else if ((z + x * 9) % 14 == 0)
                    cell = new CityCell { Type = CellType.PowerPlant, Level = 1, Height = CitySimulation.PowerPlantHeight };
                else
                {
                    int kind = (x * 59 + z * 127) % 149;
                    BuildingModel model = kind switch
                    {
                        0 => BuildingModel.University,
                        1 => BuildingModel.Preschool,
                        2 => BuildingModel.PoliceStation,
                        3 => BuildingModel.Hospital,
                        4 => BuildingModel.FireStation,
                        < 40 => BuildingModel.Factory,
                        < 72 => BuildingModel.HighStreet,
                        _ => BuildingModel.Apartment
                    };
                    cell = Developed(model);
                    if (cell.Type == CellType.Residential) cell.Level = 2 + ((x ^ z) & 1);
                }
                indices.Add(CitySimulation.CellIndex(x, z));
                cells.Add(cell);
            }

            Assert.Equal(24, shorelineUtilities);
            occupied = indices.Count;
            Assert.True(occupied > 70000);
            Assert.True(CitySimulation.TryRestore(new CitySnapshot
            {
                schemaVersion = CitySimulation.CurrentSchemaVersion,
                money = 1000000,
                randomState = 1,
                cellIndices = indices.ToArray(),
                cells = cells.ToArray()
            }, out var city));
            Assert.Equal(0, city.DroppedCellsOnLoad);
            return city;
        }

        private static CityCell Developed(BuildingModel model)
        {
            var entry = BuildingCatalog.Get(model);
            return new CityCell { Type = entry.Type, Model = model, Height = entry.Height, Level = 1 };
        }

        private static ulong Fingerprint(CitySimulation city)
        {
            ulong hash = 14695981039346656037UL;
            for (int x = 100; x <= 412; x++)
            for (int z = 100; z <= 412; z++)
            {
                Add((int)city.GetZoneStatus(x, z));
                Add(city.HasWater(x, z) ? 1 : 0);
                Add(city.HasSewage(x, z) ? 1 : 0);
                Add((int)city.GetCoverage(x, z));
                Add(city.GetSchoolStudents(x, z));
                Add(city.GetTrafficLoad(x, z));
                Add(city.GetHappiness(x, z));
            }
            Add(city.UnpoweredZones);
            Add(city.Jobs);
            Add(city.EmploymentPercent);
            Add(city.ResidentialDemand);
            Add(city.CommercialDemand);
            Add(city.IndustrialDemand);
            Add(city.AverageHappiness);
            Add(city.LastNetIncome);
            return hash;

            void Add(int value) => hash = unchecked((hash ^ (uint)value) * 1099511628211UL);
        }
    }
}
