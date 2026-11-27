using System;

namespace XCity
{
    [Flags]
    public enum ServiceCoverage : byte
    {
        None = 0,
        Education = 1,
        Health = 2,
        Police = 4,
        Fire = 8
    }

    internal static class ServiceCatalog
    {
        public static (ServiceCoverage Coverage, int Radius) Get(BuildingModel model) => model switch
        {
            BuildingModel.Preschool => (ServiceCoverage.Education, 6),
            BuildingModel.PrimarySchool => (ServiceCoverage.Education, 10),
            BuildingModel.HighSchool => (ServiceCoverage.Education, 14),
            BuildingModel.University => (ServiceCoverage.Education, 20),
            BuildingModel.PoliceStation => (ServiceCoverage.Police, 14),
            BuildingModel.Hospital => (ServiceCoverage.Health, 18),
            BuildingModel.FireStation => (ServiceCoverage.Fire, 14),
            _ => (ServiceCoverage.None, 0)
        };

        public static int Jobs(CityCell cell)
        {
            if (cell.Level == 0) return 0;
            return cell.Model switch
            {
                BuildingModel.Market => 6,
                BuildingModel.HighStreet => 10,
                BuildingModel.Office => 12,
                BuildingModel.ShoppingMall => 24,
                BuildingModel.OfficeTower => 30,
                BuildingModel.Workshop => 10,
                BuildingModel.Warehouse => 12,
                BuildingModel.Factory => 20,
                _ => cell.Type switch
                {
                    CellType.Commercial => 8,
                    CellType.Industrial => 12,
                    CellType.School or CellType.CivicService => 10,
                    _ => 0
                }
            };
        }

        public static int Count(ServiceCoverage coverage)
        {
            int count = 0;
            for (int bits = (int)coverage; bits != 0; bits &= bits - 1) count++;
            return count;
        }
    }

    // Demand, jobs and service coverage are all derived from the cells, so saves need no extra data.
    public sealed partial class CitySimulation
    {
        internal const int WorkforcePercent = 60;
        internal const int BaseHappiness = 40;
        internal const int HappinessPerService = 15;
        private readonly ServiceCoverage[] _coverage = new ServiceCoverage[GridSize * GridSize];
        private bool _coverageDirty = true;

        /// <summary>Demand for new homes, shops and industry, from -100 (oversupplied) to +100.</summary>
        public int ResidentialDemand { get; private set; }
        public int CommercialDemand { get; private set; }
        public int IndustrialDemand { get; private set; }
        public int Jobs { get; private set; }
        public int Workers => Population * WorkforcePercent / 100;
        /// <summary>Share of jobs that can be filled, 0-100. Understaffed businesses pay less tax.</summary>
        public int EmploymentPercent { get; private set; } = 100;
        public int AverageHappiness { get; private set; }

        public ServiceCoverage GetCoverage(int x, int z)
        {
            RefreshCoverage();
            return IsInBounds(x, z) ? _coverage[CellIndex(x, z)] : ServiceCoverage.None;
        }

        public int GetHappiness(int x, int z) =>
            BaseHappiness + HappinessPerService * ServiceCatalog.Count(GetCoverage(x, z));

        // A home needs one service type in reach to reach level 2, and two for level 3.
        private bool CanUpgradeResidence(int x, int z, int level) =>
            ResidentialDemand > 0 && ServiceCatalog.Count(GetCoverage(x, z)) >= level;

        private float DevelopmentChance(CellType type)
        {
            int demand = type switch
            {
                CellType.Residential => ResidentialDemand,
                CellType.Commercial => CommercialDemand,
                _ => IndustrialDemand
            };
            return demand <= 0 ? 0f : 0.05f + 0.30f * demand / 100f;
        }

        private int OperatingJobs(int x, int z, CityCell cell) =>
            cell.Type != CellType.Residential && cell.Type != CellType.Road && cell.Type != CellType.PowerPlant &&
            GetZoneStatus(x, z) == ZoneStatus.Developed ? ServiceCatalog.Jobs(cell) : 0;

        private void RecalculateEconomy()
        {
            int jobs = 0, commercialJobs = 0;
            long happiness = 0;
            foreach (int index in _occupied)
            {
                int x = index / GridSize;
                int z = index % GridSize;
                CityCell cell = _cells[x, z];
                int cellJobs = OperatingJobs(x, z, cell);
                jobs += cellJobs;
                if (cell.Type == CellType.Commercial) commercialJobs += cellJobs;
                if (cell.Type == CellType.Residential)
                    happiness += (long)GetHappiness(x, z) * BuildingCatalog.Residents(cell);
            }
            UpdateDemand(jobs, commercialJobs, happiness);
        }

        private void UpdateDemand(int jobs, int commercialJobs, long happinessByResident)
        {
            int population = Population;
            int workers = Workers;
            Jobs = jobs;
            EmploymentPercent = jobs == 0 ? 100 : Math.Min(100, workers * 100 / jobs);
            AverageHappiness = population == 0 ? 0 : (int)(happinessByResident / population);
            int industrialJobs = jobs - commercialJobs;
            // Homes want jobs; workers want slightly more jobs than exist, split between shops and industry.
            // The small surplus keeps a balanced city growing instead of settling where every demand is zero.
            ResidentialDemand = Clamp(10 + (jobs - workers) * 100 / Math.Max(40, workers));
            int wantedJobs = workers * 110 / 100 + 20;
            int wantedCommercial = wantedJobs * 35 / 100;
            int wantedIndustrial = wantedJobs - wantedCommercial;
            CommercialDemand = Clamp((wantedCommercial - commercialJobs) * 100 / Math.Max(20, wantedCommercial));
            IndustrialDemand = Clamp((wantedIndustrial - industrialJobs) * 100 / Math.Max(20, wantedIndustrial));

            static int Clamp(int value) => Math.Max(-100, Math.Min(100, value));
        }

        private void RefreshCoverage()
        {
            if (!_coverageDirty) return;
            _coverageDirty = false;
            Array.Clear(_coverage, 0, _coverage.Length);
            foreach (int index in _occupied)
            {
                int x = index / GridSize;
                int z = index % GridSize;
                var (coverage, radius) = ServiceCatalog.Get(_cells[x, z].Model);
                if (coverage == ServiceCoverage.None || GetZoneStatus(x, z) != ZoneStatus.Developed) continue;
                int minX = Math.Max(0, x - radius), maxX = Math.Min(GridSize - 1, x + radius);
                int minZ = Math.Max(0, z - radius), maxZ = Math.Min(GridSize - 1, z + radius);
                for (int cx = minX; cx <= maxX; cx++)
                for (int cz = minZ; cz <= maxZ; cz++)
                {
                    int dx = cx - x, dz = cz - z;
                    if (dx * dx + dz * dz <= radius * radius) _coverage[CellIndex(cx, cz)] |= coverage;
                }
            }
        }
    }
}
