using System;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace XCity.Tests
{
    public class EconomyTests
    {
        private readonly ITestOutputHelper _output;
        public EconomyTests(ITestOutputHelper output) => _output = output;

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

        // Land in columns x-1..x+1 for `depth` rows, with water just east of column x+1 on the given rows,
        // so pumps and treatment plants placed at (x+1, z+row) sit on the shore.
        private static (int X, int Z) FindShore(int depth, params int[] waterRows)
        {
            for (int x = 1; x < CitySimulation.GridSize - 2; x++)
            for (int z = 0; z < CitySimulation.GridSize - depth; z++)
            {
                bool fits = waterRows.All(r => !WorldTerrain.IsLand(x + 2, z + r));
                for (int dx = -1; dx <= 1 && fits; dx++)
                for (int dz = 0; dz < depth && fits; dz++)
                    fits = CitySimulation.IsBuildable(x + dx, z + dz);
                if (fits) return (x, z);
            }
            throw new InvalidOperationException("No shore found");
        }

        private static CitySimulation WithMoney(CitySimulation city, int money)
        {
            CitySnapshot snapshot = city.CreateSnapshot();
            snapshot.money = money;
            Assert.True(CitySimulation.TryRestore(snapshot, out var restored));
            return restored;
        }

        // A road along z at column x, with a power plant at its south end on the east side.
        private static CitySimulation PoweredStreet(int x, int z, int length, uint seed = 3, bool unlocked = false)
        {
            var city = unlocked ? UnlockTests.UnlockedCity(seed) : new CitySimulation(seed);
            Assert.True(city.TryApplyBuild(Enumerable.Range(z, length).Select(dz => (x, dz)), CellType.Road));
            Assert.True(city.TrySetCell(x + 1, z, CellType.PowerPlant));
            return city;
        }

        [Fact]
        public void NewCity_HasPositiveDemandForEveryZone()
        {
            var city = new CitySimulation();
            Assert.True(city.ResidentialDemand > 0);
            Assert.True(city.CommercialDemand > 0);
            Assert.True(city.IndustrialDemand > 0);
        }

        [Fact]
        public void HomesWithoutJobs_StopGrowingOnceResidentialDemandTurnsNegative()
        {
            var (x, z) = FindLand(3, 20);
            var city = PoweredStreet(x, z, 20);
            for (int dz = 1; dz < 20; dz++) Assert.True(city.TrySetCell(x + 1, z + dz, CellType.Residential));
            for (int dz = 1; dz < 20; dz++) Assert.True(city.TrySetCell(x - 1, z + dz, CellType.Residential));
            for (int i = 0; i < 200; i++) city.Tick();

            Assert.True(city.ResidentialDemand <= 0);
            int developed = Enumerable.Range(1, 19).Count(dz => city.GetCell(x + 1, z + dz).Level > 0) +
                Enumerable.Range(1, 19).Count(dz => city.GetCell(x - 1, z + dz).Level > 0);
            Assert.True(developed < 38, $"all {developed} homes developed without any jobs");
        }

        [Fact]
        public void Jobs_RaiseResidentialDemand()
        {
            var (x, z) = FindLand(3, 10);
            var city = PoweredStreet(x, z, 10);
            for (int dz = 1; dz < 10; dz++) Assert.True(city.TrySetCell(x + 1, z + dz, CellType.Industrial));
            int before = city.ResidentialDemand;
            for (int i = 0; i < 100 && city.Jobs == 0; i++) city.Tick();

            Assert.True(city.Jobs > 0);
            Assert.True(city.ResidentialDemand > before);
        }

        [Fact]
        public void UnderstaffedBusinesses_PayLessTax()
        {
            var (x, z) = FindLand(3, 10);
            var city = PoweredStreet(x, z, 10);
            for (int dz = 1; dz < 10; dz++) Assert.True(city.TrySetCell(x + 1, z + dz, CellType.Industrial));
            for (int i = 0; i < 100 && city.Jobs < 100; i++) city.Tick();
            city.Tick();

            Assert.Equal(0, city.Population);
            Assert.Equal(0, city.EmploymentPercent);
            Assert.Equal(0, city.LastTaxIncome);
        }

        [Fact]
        public void Services_CoverCellsWithinRadiusOnlyWhenOperating()
        {
            var (x, z) = FindLand(3, 4);
            var city = UnlockTests.UnlockedCity();
            Assert.True(city.TrySetCell(x + 1, z + 1, CellType.CivicService, BuildingModel.PoliceStation));
            Assert.Equal(ServiceCoverage.None, city.GetCoverage(x + 1, z + 1));

            Assert.True(city.TryApplyBuild(Enumerable.Range(z, 4).Select(dz => (x, dz)), CellType.Road));
            Assert.True(city.TrySetCell(x + 1, z, CellType.PowerPlant));
            int radius = ServiceCatalog.Get(BuildingModel.PoliceStation).Radius;
            Assert.Equal(ServiceCoverage.Police, city.GetCoverage(x + 1, z + 1));
            Assert.Equal(ServiceCoverage.Police, city.GetCoverage(x + 1 + radius, z + 1));
            Assert.Equal(ServiceCoverage.None, city.GetCoverage(x + 2 + radius, z + 1));
            Assert.Equal(CitySimulation.BaseHappiness + CitySimulation.HappinessPerService - 2 * CitySimulation.MissingUtilityPenalty,
                city.GetHappiness(x + 1, z + 2));
        }

        [Fact]
        public void Homes_NeedServicesToReachHigherLevels()
        {
            var (x, z) = FindShore(12, 10, 11);
            var bare = BuildMixedStreet(x, z, withServices: false);
            var served = BuildMixedStreet(x, z, withServices: true);
            for (int i = 0; i < 300; i++)
            {
                bare.Tick();
                served.Tick();
            }

            int MaxLevel(CitySimulation c) => Enumerable.Range(z + 1, 11)
                .Where(dz => c.GetCell(x + 1, dz).Type == CellType.Residential)
                .Max(dz => c.GetCell(x + 1, dz).Level);
            Assert.Equal(1, MaxLevel(bare));
            Assert.Equal(3, MaxLevel(served));
        }

        private static CitySimulation BuildMixedStreet(int x, int z, bool withServices)
        {
            var city = PoweredStreet(x, z, 12, unlocked: true);
            for (int dz = 1; dz < 6; dz++) Assert.True(city.TrySetCell(x + 1, z + dz, CellType.Residential));
            for (int dz = 1; dz < 12; dz++) Assert.True(city.TrySetCell(x - 1, z + dz, CellType.Industrial));
            Assert.True(city.TrySetCell(x + 1, z + 6, CellType.Commercial));
            Assert.True(city.TrySetCell(x + 1, z + 7, CellType.PowerPlant));
            if (withServices)
            {
                Assert.True(city.TrySetCell(x + 1, z + 8, CellType.School, BuildingModel.PrimarySchool));
                Assert.True(city.TrySetCell(x + 1, z + 9, CellType.CivicService, BuildingModel.Hospital));
                Assert.True(city.TrySetCell(x + 1, z + 10, CellType.CivicService, BuildingModel.WaterPump));
                Assert.True(city.TrySetCell(x + 1, z + 11, CellType.CivicService, BuildingModel.SewagePlant));
            }
            return city;
        }

        [Fact]
        public void Restore_RecomputesDemandFromCells()
        {
            var (x, z) = FindLand(3, 10);
            var city = PoweredStreet(x, z, 10);
            for (int dz = 1; dz < 10; dz++) Assert.True(city.TrySetCell(x + 1, z + dz, CellType.Industrial));
            for (int i = 0; i < 60; i++) city.Tick();

            Assert.True(CitySimulation.TryRestore(city.CreateSnapshot(), out var restored));
            Assert.Equal(city.Jobs, restored.Jobs);
            Assert.Equal(city.ResidentialDemand, restored.ResidentialDemand);
            Assert.Equal(city.IndustrialDemand, restored.IndustrialDemand);
        }

        // Not an assertion of exact balance: prints how a balanced starter town evolves, and checks it grows.
        [Fact]
        public void BalancedStarterTown_Grows()
        {
            var (x, z) = FindLand(5, 30);
            var city = PoweredStreet(x, z, 30, unlocked: true);
            Assert.True(city.TryApplyBuild(Enumerable.Range(z, 30).Select(dz => (x + 3, dz)), CellType.Road));
            for (int dz = 1; dz < 30; dz++)
            {
                city.TrySetCell(x + 1, z + dz, CellType.Residential);
                city.TrySetCell(x + 2, z + dz, CellType.Residential);
                city.TrySetCell(x - 1, z + dz, dz % 3 == 0 ? CellType.Commercial : CellType.Industrial);
            }
            city.TrySetCell(x + 4, z + 2, CellType.PowerPlant);
            city.TrySetCell(x + 4, z + 4, CellType.PowerPlant);
            city.TrySetCell(x + 4, z + 10, CellType.School, BuildingModel.PrimarySchool);
            city.TrySetCell(x + 4, z + 20, CellType.CivicService, BuildingModel.PoliceStation);

            for (int i = 0; i <= 400; i++)
            {
                if (i % 50 == 0)
                    _output.WriteLine($"tick {i,3}: pop {city.Population,4} jobs {city.Jobs,4} emp {city.EmploymentPercent,3}% " +
                        $"R {city.ResidentialDemand,4} C {city.CommercialDemand,4} I {city.IndustrialDemand,4} " +
                        $"happy {city.AverageHappiness,3} net {city.LastNetIncome,5} money {city.Money}");
                city.Tick();
            }
            Assert.True(city.Population > 300);
            Assert.True(city.EmploymentPercent > 50);
        }

        [Fact]
        public void WaterAndSewage_FlowAlongRoadsOnlyFromPoweredPlants()
        {
            var (x, z) = FindShore(20, 0, 1);
            var city = new CitySimulation();
            Assert.True(city.TryApplyBuild(Enumerable.Range(z, 20).Select(dz => (x, dz)), CellType.Road));
            Assert.True(city.TrySetCell(x + 1, z + 5, CellType.Residential));
            Assert.True(city.TrySetCell(x + 1, z, CellType.CivicService, BuildingModel.WaterPump));
            Assert.True(city.TrySetCell(x + 1, z + 1, CellType.CivicService, BuildingModel.SewagePlant));
            Assert.False(city.HasWater(x + 1, z + 5));
            Assert.False(city.HasSewage(x + 1, z + 5));

            Assert.True(city.TrySetCell(x - 1, z, CellType.PowerPlant));
            Assert.True(city.HasWater(x + 1, z + 5));
            Assert.True(city.HasSewage(x + 1, z + 5));
            // A second, unconnected street gets nothing from the first street's pump.
            Assert.False(city.HasWater(x + 1 + 50, z + 1));
        }

        [Fact]
        public void WaterBuildings_MustBeBuiltOnTheShore()
        {
            var (x, z) = FindLand(3, 4);
            Assert.False(CitySimulation.TryHarborFacing(x + 1, z + 1, out _, out _, out _));
            var city = new CitySimulation();
            Assert.False(city.TrySetCell(x + 1, z + 1, CellType.CivicService, BuildingModel.WaterPump));
            Assert.False(city.TrySetCell(x + 1, z + 1, CellType.CivicService, BuildingModel.SewagePlant));
        }

        [Fact]
        public void WaterPump_ServesAtMostItsCapacity()
        {
            var (x, z) = FindShore(30, 0);
            var city = new CitySimulation();
            Assert.True(city.TryApplyBuild(Enumerable.Range(z, 30).Select(dz => (x, dz)), CellType.Road));
            Assert.True(city.TrySetCell(x + 1, z, CellType.CivicService, BuildingModel.WaterPump));
            Assert.True(city.TrySetCell(x - 1, z, CellType.PowerPlant));
            Assert.True(city.TrySetCell(x + 1, z + 29, CellType.PowerPlant));
            for (int dz = 1; dz < 29; dz++) Assert.True(city.TrySetCell(x + 1, z + dz, CellType.Residential));
            for (int dz = 1; dz < 30; dz++) Assert.True(city.TrySetCell(x - 1, z + dz, CellType.Residential));

            // Every building on the network draws water, the pump included.
            int served = Enumerable.Range(0, 30).Count(dz => city.HasWater(x + 1, z + dz)) +
                Enumerable.Range(0, 30).Count(dz => city.HasWater(x - 1, z + dz));
            Assert.Equal(CitySimulation.UtilityCapacity, served);
        }

        [Fact]
        public void Loans_AddMoneyAndAreRepaidEachTick()
        {
            var city = new CitySimulation();
            int money = city.Money;
            Assert.True(city.TryTakeLoan());
            Assert.Equal(money + CitySimulation.LoanAmount, city.Money);
            Assert.Equal(CitySimulation.LoanRepaid, city.LoanBalance);

            city.Tick();
            Assert.Equal(CitySimulation.LoanRepaid - CitySimulation.LoanRepayment, city.LoanBalance);
            Assert.Equal(money + CitySimulation.LoanAmount - CitySimulation.LoanRepayment, city.Money);
            Assert.Equal(-CitySimulation.LoanRepayment, city.LastNetIncome);
        }

        [Fact]
        public void Loans_AreCappedAndSurviveSaving()
        {
            var city = new CitySimulation();
            int loans = 0;
            while (city.TryTakeLoan()) loans++;
            Assert.Equal(CitySimulation.MaxLoanBalance / CitySimulation.LoanRepaid, loans);
            Assert.False(city.CanTakeLoan);

            Assert.True(CitySimulation.TryRestore(city.CreateSnapshot(), out var restored));
            Assert.Equal(city.LoanBalance, restored.LoanBalance);

            CitySnapshot tampered = city.CreateSnapshot();
            tampered.loanBalance = -1;
            Assert.False(CitySimulation.TryRestore(tampered, out _));
        }

        [Fact]
        public void OldSaves_LoadWithoutLoansButCannotContainNewBuildings()
        {
            var (x, z) = FindShore(1, 0);
            var city = new CitySimulation();
            Assert.True(city.TrySetCell(x, z, CellType.Road));
            CitySnapshot old = city.CreateSnapshot();
            old.schemaVersion = 7;
            Assert.True(CitySimulation.TryRestore(old, out var restored));
            Assert.Equal(0, restored.LoanBalance);

            Assert.True(city.TrySetCell(x + 1, z, CellType.CivicService, BuildingModel.WaterPump));
            CitySnapshot forged = city.CreateSnapshot();
            forged.schemaVersion = 7;
            Assert.False(CitySimulation.TryRestore(forged, out _));
        }

        [Fact]
        public void City_GoesBankruptAfterStayingInDebt()
        {
            var (x, z) = FindLand(3, 4);
            var built = new CitySimulation();
            Assert.True(built.TrySetCell(x, z, CellType.Road));
            var city = WithMoney(built, -1);

            for (int i = 1; i < CitySimulation.BankruptcyTicks; i++) city.Tick();
            Assert.False(city.IsBankrupt);
            city.Tick();
            Assert.True(city.IsBankrupt);

            long ticks = city.TickCount;
            city.Tick();
            Assert.Equal(ticks, city.TickCount);
            Assert.False(city.CanTakeLoan);
            Assert.False(city.TrySetCell(x, z, CellType.Empty));
            Assert.False(city.TryUndoBuild());

            Assert.True(CitySimulation.TryRestore(city.CreateSnapshot(), out var restored));
            Assert.True(restored.IsBankrupt);
        }

        [Fact]
        public void Debt_CounterResetsOnceMoneyIsPositiveAgain()
        {
            var (x, z) = FindLand(3, 4);
            var built = new CitySimulation();
            Assert.True(built.TrySetCell(x, z, CellType.Road));
            var city = WithMoney(built, -1);
            for (int i = 0; i < 10; i++) city.Tick();
            Assert.Equal(10, city.TicksInDebt);

            Assert.True(city.TryTakeLoan());
            city.Tick();
            Assert.Equal(0, city.TicksInDebt);
        }
    }
}
