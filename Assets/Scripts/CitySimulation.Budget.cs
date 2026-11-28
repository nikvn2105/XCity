using System;

namespace XCity
{
    public enum TaxZone
    {
        Residential,
        Commercial,
        Industrial
    }

    public enum BudgetService
    {
        Education,
        Health,
        Police,
        Fire,
        Garbage
    }

    /// <summary>Last tick's income and spending by line, for the budget panel.</summary>
    public struct BudgetReport
    {
        public int ResidentialTax, CommercialTax, IndustrialTax;
        public int Roads, Power, Education, Health, Police, Fire, Garbage, Other, Loan;

        public int Income => ResidentialTax + CommercialTax + IndustrialTax;
        public int Spending => Roads + Power + Education + Health + Police + Fire + Garbage + Other + Loan;
    }

    // Tax rates per zone and funding per service. 10% tax and 100% funding reproduce the old fixed economy.
    public sealed partial class CitySimulation
    {
        public const int DefaultTaxRate = 10;
        public const int MaxTaxRate = 20;
        public const int DefaultFunding = 100;
        public const int MinFunding = 50;
        public const int MaxFunding = 150;
        // Each point of tax above or below the default moves that zone's demand by this much.
        internal const int DemandPerTaxPoint = 3;

        private readonly int[] _taxRates = { DefaultTaxRate, DefaultTaxRate, DefaultTaxRate };
        private readonly int[] _funding = { DefaultFunding, DefaultFunding, DefaultFunding, DefaultFunding, DefaultFunding };

        public BudgetReport LastBudget { get; private set; }

        public int GetTaxRate(TaxZone zone) => _taxRates[(int)zone];

        public void SetTaxRate(TaxZone zone, int rate) => _taxRates[(int)zone] = Math.Max(0, Math.Min(MaxTaxRate, rate));

        public int GetFunding(BudgetService service) => _funding[(int)service];

        public void SetFunding(BudgetService service, int percent)
        {
            int clamped = Math.Max(MinFunding, Math.Min(MaxFunding, percent));
            if (clamped == _funding[(int)service]) return;
            _funding[(int)service] = clamped;
            if (service == BudgetService.Garbage) _garbageDirty = true;
            else _coverageDirty = true; // funding changes service reach
        }

        internal int TaxDemandShift(TaxZone zone) => (DefaultTaxRate - _taxRates[(int)zone]) * DemandPerTaxPoint;

        // Residents feel their own tax: one happiness point per point away from the default.
        internal int TaxHappinessShift => DefaultTaxRate - _taxRates[(int)TaxZone.Residential];

        internal static BudgetService? ServiceOf(BuildingModel model) => GarbageCatalog.IsFacility(model)
            ? BudgetService.Garbage : ServiceCatalog.Get(model).Coverage switch
        {
            ServiceCoverage.Education => BudgetService.Education,
            ServiceCoverage.Health => BudgetService.Health,
            ServiceCoverage.Police => BudgetService.Police,
            ServiceCoverage.Fire => BudgetService.Fire,
            _ => null
        };

        // 50% funding reaches 75% as far, 150% reaches 125%.
        internal int FundedRadius(BuildingModel model, int radius)
        {
            BudgetService? service = ServiceOf(model);
            return service == null || service == BudgetService.Garbage ? radius : radius * (200 + _funding[(int)service.Value]) / 300;
        }

        internal int FundedUpkeep(BuildingModel model, int upkeep)
        {
            BudgetService? service = ServiceOf(model);
            return service == null ? upkeep : upkeep * _funding[(int)service.Value] / 100;
        }

        private void RestoreBudget(CitySnapshot snapshot)
        {
            if (snapshot.schemaVersion < 11) return;
            if (snapshot.taxRates != null && snapshot.taxRates.Length == _taxRates.Length)
                for (int i = 0; i < _taxRates.Length; i++) SetTaxRate((TaxZone)i, snapshot.taxRates[i]);
            if (snapshot.serviceFunding != null && (snapshot.serviceFunding.Length == _funding.Length ||
                (snapshot.schemaVersion < 13 && snapshot.serviceFunding.Length == (int)BudgetService.Garbage)))
                for (int i = 0; i < snapshot.serviceFunding.Length; i++) SetFunding((BudgetService)i, snapshot.serviceFunding[i]);
        }
    }
}
