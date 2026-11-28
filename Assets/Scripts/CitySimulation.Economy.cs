using System;
using System.Collections.Generic;

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
            // Small starter versions: open from the start, cheaper, with a shorter reach.
            BuildingModel.PolicePost => (ServiceCoverage.Police, 8),
            BuildingModel.Clinic => (ServiceCoverage.Health, 10),
            BuildingModel.FireStationSmall => (ServiceCoverage.Fire, 8),
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
                BuildingModel.SmallPark or BuildingModel.Plaza or BuildingModel.LargePark or
                BuildingModel.Landfill or BuildingModel.Incinerator => 0,
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
        internal const int MissingUtilityPenalty = 10;
        internal const int CongestionPenalty = 10;
        internal const int StudentPercent = 25;
        internal const int CommutersPerRoad = 10;
        private readonly int[] _cellComponent = new int[GridSize * GridSize];
        private readonly SortedSet<int> _serviceCells = new SortedSet<int>();
        private readonly List<int> _componentRoads = new List<int>();
        private readonly Dictionary<int, int> _schoolStudents = new Dictionary<int, int>();
        private readonly List<(int Distance, int Index)> _schoolCandidates = new List<(int, int)>();
        private int[] _componentEmployment = new int[1];
        private int[] _componentTraffic = new int[1];
        private bool _trafficDirty = true;
        public const int LoanAmount = 20000;
        public const int LoanRepaid = 24000;
        public const int LoanRepayment = 250;
        public const int MaxLoanBalance = 3 * LoanRepaid;
        private readonly ServiceCoverage[] _coverage = new ServiceCoverage[GridSize * GridSize];
        private bool _coverageDirty = true;
        private bool _coverageChangedByGrowth;
        private int[] _trafficWorkers = new int[1];
        private int[] _trafficJobs = new int[1];

        /// <summary>Demand for new homes, shops and industry, from -100 (oversupplied) to +100.</summary>
        public int ResidentialDemand { get; private set; }
        public int CommercialDemand { get; private set; }
        public int IndustrialDemand { get; private set; }
        public int Jobs { get; private set; }
        public int Workers => Population * WorkforcePercent / 100;
        /// <summary>Share of jobs that can be filled, 0-100. Understaffed businesses pay less tax.</summary>
        public int EmploymentPercent { get; private set; } = 100;
        public int AverageHappiness { get; private set; }
        /// <summary>What the city still owes; repaid automatically each tick.</summary>
        public int LoanBalance { get; private set; }
        public bool CanTakeLoan => !IsBankrupt && LoanBalance + LoanRepaid <= MaxLoanBalance;

        /// <summary>Ticks in a row the treasury has ended below zero; reaching BankruptcyTicks ends the city.</summary>
        public const int BankruptcyTicks = 40;
        public int TicksInDebt { get; private set; }
        public bool IsBankrupt => TicksInDebt >= BankruptcyTicks;

        public bool TryTakeLoan()
        {
            if (!CanTakeLoan) return false;
            Money += LoanAmount;
            LoanBalance += LoanRepaid;
            return true;
        }

        public bool HasWater(int x, int z)
        {
            RefreshPower();
            return IsInBounds(x, z) && _watered[CellIndex(x, z)];
        }

        public bool HasSewage(int x, int z)
        {
            RefreshPower();
            return IsInBounds(x, z) && _sewered[CellIndex(x, z)];
        }

        public ServiceCoverage GetCoverage(int x, int z)
        {
            RefreshCoverage();
            return IsInBounds(x, z) ? _coverage[CellIndex(x, z)] : ServiceCoverage.None;
        }

        public int GetHappiness(int x, int z) =>
            Math.Min(100, CalculateHappiness(GetCoverage(x, z), HasWater(x, z), HasSewage(x, z), GetTrafficLoad(x, z))
                + ParkBonusAt(x, z) - GarbageHappinessPenalty(x, z) - (HasDirtyWater(x, z) ? DirtyWaterPenalty : 0));

        private int GarbageHappinessPenalty(int x, int z) =>
            IsInBounds(x, z) && _cells[x, z].Type == CellType.Residential && NeedsGarbageCollection(x, z) ? 10 : 0;

        private int CalculateHappiness(ServiceCoverage coverage, bool water, bool sewage, int traffic) =>
            TaxHappinessShift + BaseHappiness + HappinessPerService * ServiceCatalog.Count(coverage) -
            (water ? 0 : MissingUtilityPenalty) - (sewage ? 0 : MissingUtilityPenalty) -
            (traffic > 100 ? CongestionPenalty : 0);

        /// <summary>Students enrolled at the school on this cell (0 for other cells).</summary>
        public int GetSchoolStudents(int x, int z)
        {
            RefreshCoverage();
            return IsInBounds(x, z) && _schoolStudents.TryGetValue(CellIndex(x, z), out int students) ? students : 0;
        }

        /// <summary>
        /// Commuters on this cell's road network as a percentage of what its roads carry; above 100 it is jammed.
        /// Works for road cells and for buildings beside a road.
        /// </summary>
        public int GetTrafficLoad(int x, int z)
        {
            int network = NetworkAt(x, z);
            return network == 0 ? 0 : _componentTraffic[network];
        }

        /// <summary>Share of jobs on this cell's road network that its own residents can fill, 0-100.</summary>
        internal int EmploymentAt(int x, int z)
        {
            int network = NetworkAt(x, z);
            return network == 0 ? 0 : _componentEmployment[network];
        }

        private int NetworkAt(int x, int z)
        {
            if (!IsInBounds(x, z)) return 0;
            RefreshPower();
            RefreshTraffic();
            int index = CellIndex(x, z);
            return _cells[x, z].Type == CellType.Road ? _roadComponent[index] : _cellComponent[index];
        }

        // Workers only reach jobs on their own road network, so each network is staffed and loaded on its own.
        private void RefreshTraffic()
        {
            RefreshPower();
            if (!_trafficDirty) return;
            _trafficDirty = false;
            int networks = _componentRoads.Count;
            ClearCapacity(ref _trafficWorkers, networks);
            ClearCapacity(ref _trafficJobs, networks);
            int[] workers = _trafficWorkers;
            int[] jobs = _trafficJobs;
            foreach (int index in _orderedConsumers)
            {
                int network = _cellComponent[index];
                if (network == 0) continue;
                int x = index / GridSize, z = index % GridSize;
                CityCell cell = _cells[x, z];
                if (cell.Type == CellType.Residential) workers[network] += BuildingCatalog.Residents(cell) * WorkforcePercent / 100;
                else if (_powered[index]) jobs[network] += ServiceCatalog.Jobs(cell);
            }

            PublishTraffic(workers, jobs);
        }

        private void PublishTraffic(int[] workers, int[] jobs)
        {
            int networks = _componentRoads.Count;
            _trafficDirty = false;
            ClearCapacity(ref _componentEmployment, networks);
            ClearCapacity(ref _componentTraffic, networks);
            for (int n = 1; n < networks; n++)
            {
                _componentEmployment[n] = jobs[n] == 0 ? 100 : Math.Min(100, workers[n] * 100 / jobs[n]);
                _componentTraffic[n] = Math.Min(workers[n], jobs[n]) * 100 / Math.Max(1, _componentRoads[n] * CommutersPerRoad);
            }
        }

        // A home needs water, sewage and one service type in reach to reach level 2, and two services for level 3.
        private bool CanUpgradeResidence(int x, int z, int level) =>
            ResidentialDemand > 0 && !NeedsGarbageCollection(x, z) && HasWater(x, z) && HasSewage(x, z) && ServiceCatalog.Count(GetCoverage(x, z)) >= level;

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
            RefreshCoverage();
            RefreshTraffic();
            int jobs = 0, commercialJobs = 0;
            long happiness = 0;
            foreach (int index in _orderedConsumers)
            {
                int x = index / GridSize;
                int z = index % GridSize;
                CityCell cell = _cells[x, z];
                int cellJobs = cell.Type != CellType.Residential && _powered[index] ? ServiceCatalog.Jobs(cell) : 0;
                jobs += cellJobs;
                if (cell.Type == CellType.Commercial) commercialJobs += cellJobs;
                if (cell.Type == CellType.Residential)
                {
                    int local = Math.Min(100, CalculateHappiness(_coverage[index], _watered[index], _sewered[index],
                        _componentTraffic[_cellComponent[index]]) + ParkBonusAt(x, z) - GarbageHappinessPenalty(x, z) -
                        (_watered[index] && _dirtyWater[index] ? DirtyWaterPenalty : 0));
                    happiness += (long)local * BuildingCatalog.Residents(cell);
                }
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
            ResidentialDemand = Clamp(10 + (jobs - workers) * 100 / Math.Max(40, workers) + TaxDemandShift(TaxZone.Residential));
            int wantedJobs = workers * 110 / 100 + 20;
            int wantedCommercial = wantedJobs * 35 / 100;
            int wantedIndustrial = wantedJobs - wantedCommercial;
            CommercialDemand = Clamp((wantedCommercial - commercialJobs) * 100 / Math.Max(20, wantedCommercial) +
                TaxDemandShift(TaxZone.Commercial));
            IndustrialDemand = Clamp((wantedIndustrial - industrialJobs) * 100 / Math.Max(20, wantedIndustrial) +
                TaxDemandShift(TaxZone.Industrial));

            static int Clamp(int value) => Math.Max(-100, Math.Min(100, value));
        }

        private void RefreshCoverage()
        {
            RefreshPower();
            if (!_coverageDirty) return;
            _coverageDirty = false;
            _coverageChangedByGrowth = false;
            Array.Clear(_coverage, 0, _coverage.Length);
            _schoolStudents.Clear();
            foreach (int index in _serviceCells)
            {
                int x = index / GridSize;
                int z = index % GridSize;
                BuildingModel model = _cells[x, z].Model;
                var (coverage, baseRadius) = ServiceCatalog.Get(model);
                int radius = FundedRadius(model, baseRadius);
                if (coverage == ServiceCoverage.None || !_powered[index] || _cells[x, z].Level == 0) continue;
                int minX = Math.Max(0, x - radius), maxX = Math.Min(GridSize - 1, x + radius);
                int minZ = Math.Max(0, z - radius), maxZ = Math.Min(GridSize - 1, z + radius);
                bool school = coverage == ServiceCoverage.Education;
                _schoolCandidates.Clear();
                for (int cx = minX; cx <= maxX; cx++)
                for (int cz = minZ; cz <= maxZ; cz++)
                {
                    int dx = cx - x, dz = cz - z;
                    if (dx * dx + dz * dz > radius * radius) continue;
                    int cell = CellIndex(cx, cz);
                    // Homes already served by another school keep their seat there.
                    if (school && _cells[cx, cz].Type == CellType.Residential && (_coverage[cell] & coverage) == 0)
                        _schoolCandidates.Add((dx * dx + dz * dz, cell));
                    else _coverage[cell] |= coverage;
                }
                if (!school) continue;

                // A school fills its seats with the nearest homes first; homes beyond its capacity go unserved.
                _schoolCandidates.Sort((a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : a.Index.CompareTo(b.Index));
                int seats = BuildingCatalog.Get(model).Seats, enrolled = 0;
                foreach (var (_, cell) in _schoolCandidates)
                {
                    int students = BuildingCatalog.Residents(_cells[cell / GridSize, cell % GridSize]) * StudentPercent / 100;
                    if (enrolled + students > seats) continue;
                    enrolled += students;
                    _coverage[cell] |= coverage;
                }
                _schoolStudents[index] = enrolled;
            }
        }

    }
}
