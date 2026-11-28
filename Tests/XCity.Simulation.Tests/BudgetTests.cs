using System.Linq;
using Xunit;

namespace XCity.Tests
{
    public class BudgetTests
    {
        // A grown street of homes and businesses, so every tax line has a base.
        private static CitySimulation GrownStreet()
        {
            var (x, z) = EconomyTests.FindLand(3, 20);
            var city = EconomyTests.PoweredStreet(x, z, 20);
            for (int dz = 1; dz < 20; dz++)
            {
                city.TrySetCell(x + 1, z + dz, CellType.Residential);
                city.TrySetCell(x - 1, z + dz, dz % 3 == 0 ? CellType.Commercial : CellType.Industrial);
            }
            for (int i = 0; i < 200; i++) city.Tick();
            Assert.True(city.Population > 0);
            return city;
        }

        private static CitySimulation Copy(CitySimulation city)
        {
            Assert.True(CitySimulation.TryRestore(city.CreateSnapshot(), out var copy));
            return copy;
        }

        [Fact]
        public void TaxRate_ScalesThatZonesIncome()
        {
            var city = GrownStreet();
            var none = Copy(city);
            var normal = Copy(city);
            var high = Copy(city);
            none.SetTaxRate(TaxZone.Residential, 0);
            high.SetTaxRate(TaxZone.Residential, CitySimulation.MaxTaxRate);
            none.Tick();
            normal.Tick();
            high.Tick();

            Assert.Equal(0, none.LastBudget.ResidentialTax);
            Assert.True(normal.LastBudget.ResidentialTax > 0);
            Assert.True(high.LastBudget.ResidentialTax > normal.LastBudget.ResidentialTax * 3 / 2);
            Assert.Equal(high.LastBudget.Income, high.LastTaxIncome);
        }

        [Fact]
        public void HighTax_LowersDemandAndResidentHappiness()
        {
            var city = GrownStreet();
            var normal = Copy(city);
            var high = Copy(city);
            foreach (TaxZone zone in new[] { TaxZone.Residential, TaxZone.Commercial, TaxZone.Industrial })
                high.SetTaxRate(zone, CitySimulation.MaxTaxRate);
            normal.Tick();
            high.Tick();

            // Residential demand on this street sits at the +100 cap, so only commercial is compared strictly.
            Assert.True(high.ResidentialDemand <= normal.ResidentialDemand);
            Assert.True(high.CommercialDemand < normal.CommercialDemand);
            Assert.True(high.IndustrialDemand <= normal.IndustrialDemand);
            Assert.Equal(-(CitySimulation.MaxTaxRate - CitySimulation.DefaultTaxRate) * CitySimulation.DemandPerTaxPoint,
                high.TaxDemandShift(TaxZone.Residential));
            var home = city.OccupiedIndices.Select(i => (X: i / CitySimulation.GridSize, Z: i % CitySimulation.GridSize))
                .First(p => city.GetCell(p.X, p.Z).Type == CellType.Residential);
            Assert.Equal(normal.GetHappiness(home.X, home.Z) - (CitySimulation.MaxTaxRate - CitySimulation.DefaultTaxRate),
                high.GetHappiness(home.X, home.Z));
        }

        [Fact]
        public void Funding_ChangesServiceReachAndUpkeep()
        {
            var (x, z) = EconomyTests.FindLand(3, 4);
            var city = UnlockTests.UnlockedCity();
            Assert.True(city.TryApplyBuild(Enumerable.Range(z, 4).Select(dz => (x, dz)), CellType.Road));
            Assert.True(city.TrySetCell(x + 1, z, CellType.PowerPlant));
            Assert.True(city.TrySetCell(x + 1, z + 1, CellType.CivicService, BuildingModel.PoliceStation));
            int radius = ServiceCatalog.Get(BuildingModel.PoliceStation).Radius;
            Assert.Equal(ServiceCoverage.Police, city.GetCoverage(x + 1 + radius, z + 1));

            city.SetFunding(BudgetService.Police, CitySimulation.MinFunding);
            Assert.Equal(ServiceCoverage.None, city.GetCoverage(x + 1 + radius, z + 1));
            city.Tick();
            int lowUpkeep = city.LastBudget.Police;

            city.SetFunding(BudgetService.Police, CitySimulation.MaxFunding);
            Assert.Equal(ServiceCoverage.Police, city.GetCoverage(x + 2 + radius, z + 1));
            city.Tick();
            Assert.Equal(BuildingCatalog.ServiceUpkeep(BuildingModel.PoliceStation) * 3 / 2, city.LastBudget.Police);
            Assert.True(lowUpkeep < city.LastBudget.Police);
        }

        [Fact]
        public void TaxAndFunding_RoundTripThroughSaves()
        {
            var city = new CitySimulation(5);
            city.SetTaxRate(TaxZone.Commercial, 14);
            city.SetFunding(BudgetService.Health, 70);
            var restored = Copy(city);
            Assert.Equal(14, restored.GetTaxRate(TaxZone.Commercial));
            Assert.Equal(70, restored.GetFunding(BudgetService.Health));

            CitySnapshot old = city.CreateSnapshot();
            old.schemaVersion = 10;
            old.taxRates = null;
            old.serviceFunding = null;
            Assert.True(CitySimulation.TryRestore(old, out var legacy));
            Assert.Equal(CitySimulation.DefaultTaxRate, legacy.GetTaxRate(TaxZone.Commercial));
            Assert.Equal(CitySimulation.DefaultFunding, legacy.GetFunding(BudgetService.Health));
        }

        [Fact]
        public void Rates_AreClampedToTheirRange()
        {
            var city = new CitySimulation(5);
            city.SetTaxRate(TaxZone.Industrial, 99);
            city.SetFunding(BudgetService.Fire, 5);
            Assert.Equal(CitySimulation.MaxTaxRate, city.GetTaxRate(TaxZone.Industrial));
            Assert.Equal(CitySimulation.MinFunding, city.GetFunding(BudgetService.Fire));
        }
    }
}
