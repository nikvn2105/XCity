using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
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
            // This large city has no garbage facilities. The phase 2d happiness penalty is included
            // in the fingerprint, so keep checking both state restoration and the affected economy.
            Assert.Equal(new[]
            {
                0xDD7A83EF322AE240UL, 0x4519FEFACB88C175UL, 0xABA3117097FE7504UL,
                0x4519FEFACB88C175UL, 0xDD7A83EF322AE240UL, 0xF316F0EF56E3AF77UL
            }, states);
        }

        [Fact]
        public void LargeCity_TickPathTrafficAndUndo()
        {
            CitySimulation city = CreateLargeCity(out int occupied);
            _output.WriteLine($"Fixture: {occupied:N0} occupied cells");
            Measure("Tick", city.Tick);

            var route = new List<int>(CitySimulation.GridSize * 2);
            void FindPath()
            {
                if (!city.Roads.TryFindPath(150, 160, 270, 258, route))
                    throw new InvalidOperationException("Benchmark route must reach its destination.");
            }
            Measure("TryFindPath", FindPath);
            Assert.Equal(218, route.Count);

            CitySimulation trafficCity = CreateLargeCity(out _);
            var method = typeof(CitySimulation).GetMethod("RefreshTraffic", BindingFlags.Instance | BindingFlags.NonPublic);
            var refreshTraffic = (Action)method.CreateDelegate(typeof(Action), trafficCity);
            bool roadBuilt = false;
            Measure("RefreshTraffic (dirty, utilities current)", refreshTraffic, () =>
            {
                Assert.True(roadBuilt ? trafficCity.TryUndoBuild() : trafficCity.TrySetCell(256, 257, CellType.Road));
                roadBuilt = !roadBuilt;
                _ = trafficCity.UnpoweredZones;
            });

            CitySimulation undoCity = CreateLargeCity(out _);
            Measure("Undo road build (one tile)", () =>
            {
                if (!undoCity.TryUndoBuild()) throw new InvalidOperationException("Benchmark undo must succeed.");
            }, () => Assert.True(undoCity.TrySetCell(256, 257, CellType.Road)));
        }

        [Fact]
        public void LargeCity_TicksAndFarRouteMatchReference()
        {
            CitySimulation city = CreateLargeCity(out _);
            var route = new List<int>();
            Assert.True(city.Roads.TryFindPath(150, 160, 270, 258, route));
            ulong path = 14695981039346656037UL;
            foreach (int step in route) path = unchecked((path ^ (uint)step) * 1099511628211UL);
            Assert.Equal(0xB130B91B09D9AA5BUL, path);
            Assert.True(city.TrySetCell(256, 257, CellType.Residential));
            Assert.True(city.TrySetCell(257, 257, CellType.Industrial));
            // Phase 2d reduces taxable income in this unserved city. Keep the original RNG
            // expectations and the unchanged route assertion above.
            var expected = new (ulong Fingerprint, int Money, uint RandomState)[]
            {
                (0x25787966927BF7D9UL, 2467755, 2647435461),
                (0x8526E67FE75B297DUL, 3935610, 307599695),
                (0x8526E67FE75B297DUL, 5403465, 2398689233),
                (0x8526E67FE75B297DUL, 6871320, 745495504),
                (0x8526E67FE75B297DUL, 8339175, 632435482)
            };
            foreach (var state in expected)
            {
                city.Tick();
                Assert.Equal(state, (Fingerprint(city), city.Money, city.CreateSnapshot().randomState));
            }
        }

        private void Measure(string label, Action operation, Action prepare = null)
        {
            var elapsed = new double[21];
            var allocated = new long[elapsed.Length];
            for (int i = -8; i < elapsed.Length; i++)
            {
                prepare?.Invoke();
                long bytes = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                operation();
                double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                if (i < 0) continue;
                elapsed[i] = milliseconds;
                allocated[i] = GC.GetAllocatedBytesForCurrentThread() - bytes;
            }

            Array.Sort(elapsed);
            Array.Sort(allocated);
            _output.WriteLine($"{label}: median {elapsed[10]:F3} ms; p90 {elapsed[18]:F3} ms; median allocated {allocated[10]:N0} bytes.");
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
