using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace XCity.Tests
{
    public sealed class EconomyReportTests
    {
        public enum Scenario { Balanced, HousingHeavy, IndustryHeavy, EarlyServices, Growth }

        private const int Turns = 2000;
        private const int StreetDepth = 22;
        private const int StreetCount = 14;
        private static readonly int[] Milestones = { 500, 2000, 5000 };
        private readonly ITestOutputHelper _output;

        public EconomyReportTests(ITestOutputHelper output) => _output = output;

        [Theory]
        [InlineData(Scenario.Balanced)]
        [InlineData(Scenario.HousingHeavy)]
        [InlineData(Scenario.IndustryHeavy)]
        [InlineData(Scenario.EarlyServices)]
        [InlineData(Scenario.Growth, 8)]
        [InlineData(Scenario.Growth, 10)]
        [InlineData(Scenario.Growth, 13)]
        [InlineData(Scenario.Growth, 16)]
        public void ReportEconomyOverTwoThousandTurns(Scenario scenario, int taxRate = CitySimulation.DefaultTaxRate)
        {
            var plan = new CityPlan();
            foreach (TaxZone zone in new[] { TaxZone.Residential, TaxZone.Commercial, TaxZone.Industrial })
                plan.City.SetTaxRate(zone, taxRate);
            _output.WriteLine($"Scenario {scenario}, tax {taxRate}%");
            CitySimulation city = plan.City;
            var unlockedAt = new Dictionary<int, int>();
            var servicePacks = new HashSet<int>();
            int? firstDebt = null, bankruptcy = null, first100K = null, firstMillion = null;
            int startingCash = city.Money;
            int minimumCash = startingCash;
            long operatingResult = 0;
            int expansion = 0;
            switch (scenario)
            {
                case Scenario.Balanced:
                    plan.Zone(30, StarterMix);
                    break;
                case Scenario.HousingHeavy:
                    plan.Zone(36, new[] { BuildingModel.RowHouse });
                    plan.Zone(1, new[] { BuildingModel.Market });
                    break;
                case Scenario.IndustryHeavy:
                    plan.Zone(36, new[]
                    {
                        BuildingModel.RowHouse, BuildingModel.Market, BuildingModel.Workshop,
                        BuildingModel.Warehouse, BuildingModel.Workshop, BuildingModel.RowHouse
                    });
                    break;
                case Scenario.Growth:
                    plan.Zone(20, StarterMix);
                    plan.BuildShore(BuildingModel.WaterPump);
                    plan.BuildShore(BuildingModel.SewagePlant);
                    break;
                case Scenario.EarlyServices:
                    plan.Zone(90, StarterMix);
                    plan.BuildShore(BuildingModel.WaterPump);
                    plan.BuildShore(BuildingModel.SewagePlant);
                    break;
            }

            _output.WriteLine("| Lượt | Tick thực tế | Tiền | Ròng/tick | Dân | Việc | Hạnh phúc | R | C | I |");
            _output.WriteLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            for (int turn = 1; turn <= Turns; turn++)
            {
                if (scenario == Scenario.EarlyServices && !city.IsBankrupt)
                {
                    BuildAvailableServices(plan, servicePacks);
                    if (turn % 25 == 0 && city.PeakPopulation >= 500 && expansion < 9 && city.Money >= 25000)
                    {
                        plan.Zone(50, ExpansionMix);
                        expansion++;
                    }
                }

                // Auto-mayor: every 10 turns zone what demand asks for while cash allows, plus a park per ~30 homes.
                if (scenario == Scenario.Growth && !city.IsBankrupt)
                {
                    BuildAvailableServices(plan, servicePacks);
                    if (turn % 10 == 0 && plan.LotsLeft > 8)
                    {
                        bool high = city.PeakPopulation >= 500;
                        if (city.ResidentialDemand > 0 && city.Money > 4000)
                            plan.Zone(4, new[] { high && turn % 20 == 0 ? BuildingModel.Apartment : BuildingModel.RowHouse });
                        if (city.CommercialDemand > 0 && city.Money > 4000)
                            plan.Zone(2, new[] { high ? BuildingModel.Office : BuildingModel.Market, BuildingModel.HighStreet });
                        if (city.IndustrialDemand > 0 && city.Money > 4000)
                            plan.Zone(2, new[] { BuildingModel.Workshop, BuildingModel.Warehouse });
                        if (plan.Homes >= plan.Parks * 30 + 30 && city.Money > 3000)
                            plan.BuildService(BuildingModel.SmallPark);
                        // Garbage: at most one facility per 50 turns while collection is clearly short (placement is
                        // just the next free lot, so faster building would spam facilities out of reach).
                        if (turn % 50 == 0 && city.GarbageProduced - city.GarbageCollected > 60 && city.Money > 5000)
                            plan.BuildService(city.PeakPopulation >= 2000 ? BuildingModel.Incinerator : BuildingModel.Landfill);
                    }
                }

                long previousTick = city.TickCount;
                city.Tick();
                if (city.TickCount != previousTick) operatingResult += city.LastNetIncome;
                foreach (int milestone in Milestones)
                    if (city.PeakPopulation >= milestone) unlockedAt.TryAdd(milestone, turn);
                if (city.Money < 0) firstDebt ??= turn;
                if (city.IsBankrupt) bankruptcy ??= turn;
                if (city.Money >= 100000) first100K ??= turn;
                if (city.Money >= 1000000) firstMillion ??= turn;
                minimumCash = Math.Min(minimumCash, Math.Min(plan.LowestAfterBuild, city.Money));

                if (turn % 100 == 0)
                    _output.WriteLine($"| {turn} | {city.TickCount} | {city.Money} | {city.LastNetIncome} | " +
                        $"{city.Population} | {city.Jobs} | {city.AverageHappiness} | {city.ResidentialDemand} | " +
                        $"{city.CommercialDemand} | {city.IndustrialDemand} |");
            }

            Assert.Equal((long)(bankruptcy ?? Turns), city.TickCount);
            Assert.Equal(0, city.UnpoweredZones);
            Assert.Equal(startingCash + operatingResult - plan.Capex.Sum(), (long)city.Money);
            var upkeep = plan.Upkeep();
            Assert.Equal(city.LastTaxIncome - upkeep.Road - upkeep.Power - upkeep.Services - upkeep.Water, city.LastNetIncome);
            _output.WriteLine($"Milestones (500/2000/5000): {Reached(unlockedAt, 500)} / {Reached(unlockedAt, 2000)} / {Reached(unlockedAt, 5000)}");
            _output.WriteLine($"Debt: {Turn(firstDebt)}; bankruptcy: {Turn(bankruptcy)}; 100k: {Turn(first100K)}; " +
                $"1m: {Turn(firstMillion)}; minimum cash: {minimumCash}; peak: {city.PeakPopulation}; expansions: {expansion}");
            _output.WriteLine($"Capex (road / power / zones / services / water): {plan.Capex[(int)Cost.Road]} / {plan.Capex[(int)Cost.Power]} / " +
                $"{plan.Capex[(int)Cost.Zones]} / {plan.Capex[(int)Cost.Services]} / {plan.Capex[(int)Cost.Water]}");
            _output.WriteLine($"Upkeep (road / power / services / water): {upkeep.Road} / {upkeep.Power} / {upkeep.Services} / {upkeep.Water}; " +
                $"tax: {city.LastTaxIncome}");
            var homes = city.OccupiedIndices.Select(index => (X: index / CitySimulation.GridSize, Z: index % CitySimulation.GridSize))
                .Where(p => city.GetCell(p.X, p.Z).Type == CellType.Residential && city.GetCell(p.X, p.Z).Level > 0)
                .ToArray();
            int residentialTax = city.LastBudget.ResidentialTax;
            int waterAndSewage = homes.Count(p => city.HasWater(p.X, p.Z) && city.HasSewage(p.X, p.Z));
            int upgraded = homes.Count(p => city.GetCell(p.X, p.Z).Level > 1);
            _output.WriteLine($"Final homes (developed / water and sewage / upgraded): {homes.Length} / {waterAndSewage} / {upgraded}; " +
                $"residential tax: {residentialTax}; business tax: {city.LastTaxIncome - residentialTax}");
            BudgetReport b = city.LastBudget;
            _output.WriteLine($"Budget: tax R/C/I {b.ResidentialTax}/{b.CommercialTax}/{b.IndustrialTax}; roads {b.Roads}, power {b.Power}, " +
                $"education {b.Education}, health {b.Health}, police {b.Police}, fire {b.Fire}, garbage {b.Garbage}, other {b.Other}, loan {b.Loan}; " +
                $"lots left {plan.LotsLeft}");
            _output.WriteLine($"Site: ({plan.Origin.X}, {plan.Origin.Z}); builds: {string.Join(", ", plan.BuildLog)}");
        }

        private static string Turn(int? turn) => turn?.ToString() ?? "—";
        private static string Reached(Dictionary<int, int> unlocks, int threshold) =>
            unlocks.TryGetValue(threshold, out int turn) ? Turn(turn) : Turn(null);

        private static readonly BuildingModel[] StarterMix =
        {
            BuildingModel.RowHouse, BuildingModel.Market, BuildingModel.RowHouse, BuildingModel.Workshop,
            BuildingModel.RowHouse, BuildingModel.Market, BuildingModel.RowHouse, BuildingModel.Workshop,
            BuildingModel.RowHouse, BuildingModel.Warehouse
        };
        private static readonly BuildingModel[] ExpansionMix =
        {
            BuildingModel.Apartment, BuildingModel.Office, BuildingModel.Apartment, BuildingModel.Factory,
            BuildingModel.Apartment, BuildingModel.Office, BuildingModel.Apartment, BuildingModel.Factory,
            BuildingModel.Apartment, BuildingModel.Factory
        };

        private static void BuildAvailableServices(CityPlan plan, HashSet<int> built)
        {
            CitySimulation city = plan.City;
            if (city.PeakPopulation >= 500 && city.Money >= 5000 && built.Add(500))
            {
                plan.BuildService(BuildingModel.Preschool);
                plan.BuildService(BuildingModel.PrimarySchool);
                plan.BuildService(BuildingModel.PoliceStation);
            }
            if (city.PeakPopulation >= 2000 && city.Money >= 10000 && built.Add(2000))
            {
                plan.BuildService(BuildingModel.HighSchool);
                plan.BuildService(BuildingModel.Hospital);
                plan.BuildService(BuildingModel.FireStation);
                plan.BuildShore(BuildingModel.Harbor);
            }
            if (city.PeakPopulation >= 5000 && city.Money >= 5000 && built.Add(5000))
            {
                plan.BuildService(BuildingModel.University);
                plan.Zone(2, new[] { BuildingModel.OfficeTower, BuildingModel.ShoppingMall });
            }
        }

        private enum Cost { Road, Power, Zones, Services, Water }

        private sealed class CityPlan
        {
            private readonly Queue<(int X, int Z)> _lots;
            private readonly Queue<(int X, int Z)> _shore;
            private int _consumers;
            private int _plants;
            public readonly CitySimulation City = new CitySimulation(seed: 1);
            public readonly long[] Capex = new long[5];
            public readonly List<string> BuildLog = new List<string>();
            public readonly (int X, int Z) Origin;
            public int LowestAfterBuild { get; private set; } = int.MaxValue;
            public int LotsLeft => _lots.Count;
            public int Homes { get; private set; }
            public int Parks { get; private set; }

            public CityPlan()
            {
                Origin = FindSite();
                var lots = (from street in Enumerable.Range(0, StreetCount)
                            from dz in Enumerable.Range(1, StreetDepth)
                            from side in new[] { 0, 2 }
                            select (X: Origin.X + street * 3 + side, Z: Origin.Z + dz)).ToArray();
                var shore = lots.Where(p => p.Z == Origin.Z + StreetDepth &&
                    CitySimulation.TryHarborFacing(p.X, p.Z, out _, out _, out _)).Take(3).ToArray();
                Assert.Equal(3, shore.Length);
                _shore = new Queue<(int, int)>(shore);
                _lots = new Queue<(int, int)>(lots.Except(shore));
            }

            public void Zone(int count, BuildingModel[] mix)
            {
                for (int i = 0; i < count; i++) BuildConsumer(mix[i % mix.Length], Cost.Zones);
            }

            public void BuildService(BuildingModel model) => BuildConsumer(model, Cost.Services);
            public void BuildShore(BuildingModel model) =>
                BuildConsumer(model, model == BuildingModel.Harbor ? Cost.Services : Cost.Water, shore: true);

            private void BuildConsumer(BuildingModel model, Cost cost, bool shore = false)
            {
                if (_consumers == _plants * CitySimulation.PowerPlantCapacity)
                {
                    BuildLot(_lots.Dequeue(), CellType.PowerPlant, BuildingModel.Default, Cost.Power);
                    _plants++;
                }

                BuildLot((shore ? _shore : _lots).Dequeue(), BuildingCatalog.Get(model).Type, model, cost);
                _consumers++;
                if (BuildingCatalog.Get(model).Type == CellType.Residential) Homes++;
                if (model == BuildingModel.SmallPark) Parks++;
                if (cost != Cost.Zones || model == BuildingModel.OfficeTower || model == BuildingModel.ShoppingMall)
                    BuildLog.Add($"{model}@{City.TickCount + 1}");
            }

            private void BuildLot((int X, int Z) lot, CellType type, BuildingModel model, Cost cost)
            {
                int street = Origin.X + (lot.X - Origin.X) / 3 * 3 + 1;
                for (int x = Origin.X + 1; x <= street; x++) BuildRoadIfEmpty(x, Origin.Z);
                for (int z = Origin.Z + 1; z <= lot.Z; z++) BuildRoadIfEmpty(street, z);
                Build(lot.X, lot.Z, type, model, cost);
            }

            private void BuildRoadIfEmpty(int x, int z)
            {
                if (City.GetCell(x, z).Type == CellType.Empty)
                    Build(x, z, CellType.Road, BuildingModel.Default, Cost.Road);
            }

            private void Build(int x, int z, CellType type, BuildingModel model, Cost cost)
            {
                int before = City.Money;
                Assert.Equal(CellType.Empty, City.GetCell(x, z).Type);
                Assert.True(City.PeakPopulation >= BuildingCatalog.UnlockPopulation(model));
                Assert.True(City.TrySetCell(x, z, type, model),
                    $"Cannot build {type}/{model} at ({x}, {z}), turn {City.TickCount + 1}, cash {before}.");
                Capex[(int)cost] += before - City.Money;
                LowestAfterBuild = Math.Min(LowestAfterBuild, City.Money);
            }

            public (int Road, int Power, int Services, int Water) Upkeep()
            {
                int roads = 0, services = 0, water = 0;
                foreach (int index in City.OccupiedIndices)
                {
                    var cell = City.GetCell(index / CitySimulation.GridSize, index % CitySimulation.GridSize);
                    if (cell.Type == CellType.Road) roads++;
                    if (cell.Model == BuildingModel.WaterPump || cell.Model == BuildingModel.SewagePlant)
                        water += BuildingCatalog.ServiceUpkeep(cell.Model);
                    else if (BuildingCatalog.IsPublicService(cell.Type))
                        services += BuildingCatalog.ServiceUpkeep(cell.Model);
                }
                // Tick keeps these two rates private or inline. The report reconciles them against actual net income.
                return (roads * 2, _plants * 60, services, water);
            }

            private static (int X, int Z) FindSite()
            {
                int width = StreetCount * 3;
                for (int z = 420; z >= 180; z--)
                for (int x = 150; x < 350; x++)
                {
                    int shore = 0;
                    for (int dx = 0; dx < width && shore < 3; dx++)
                        if (dx % 3 != 1 && CitySimulation.TryHarborFacing(x + dx, z + StreetDepth, out _, out _, out _))
                            shore++;
                    if (shore < 3) continue;
                    if (Enumerable.Range(x, width).All(cx =>
                        Enumerable.Range(z, StreetDepth + 1).All(cz => CitySimulation.IsBuildable(cx, cz))))
                        return (x, z);
                }
                throw new InvalidOperationException("No connected land for the economy report and its three shore lots.");
            }
        }
    }
}
