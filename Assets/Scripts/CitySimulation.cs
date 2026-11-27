using System;
using System.Collections.Generic;

namespace XCity
{
    public enum CellType
    {
        Empty,
        Road,
        Residential,
        Commercial,
        Industrial,
        PowerPlant,
        School,
        CivicService
    }

    public enum BuildingModel
    {
        Default,
        RowHouse,
        Villa,
        Apartment,
        Preschool,
        PrimarySchool,
        HighSchool,
        University,
        Workshop,
        Warehouse,
        Factory,
        Office,
        OfficeTower,
        Market,
        ShoppingMall,
        HighStreet,
        PoliceStation,
        Hospital,
        FireStation,
        Harbor
    }

    internal static class BuildingCatalog
    {
        public static (CellType Type, string Name, int Cost, float Height, int Seats) Get(BuildingModel model) =>
            model switch
            {
                BuildingModel.RowHouse => (CellType.Residential, "Nhà phố", 50, 2.7f, 0),
                BuildingModel.Villa => (CellType.Residential, "Biệt thự", 100, 2.5f, 0),
                BuildingModel.Apartment => (CellType.Residential, "Chung cư", 130, 4.6f, 0),
                BuildingModel.Preschool => (CellType.School, "Mầm non", 350, 2.2f, 60),
                BuildingModel.PrimarySchool => (CellType.School, "Tiểu học", 700, 3.0f, 180),
                BuildingModel.HighSchool => (CellType.School, "Trung học", 1200, 4.0f, 360),
                BuildingModel.University => (CellType.School, "Đại học", 2200, 5.0f, 700),
                BuildingModel.Workshop => (CellType.Industrial, "Xưởng sản xuất", 60, 2.4f, 0),
                BuildingModel.Warehouse => (CellType.Industrial, "Kho vận", 100, 2.8f, 0),
                BuildingModel.Factory => (CellType.Industrial, "Nhà máy", 160, 3.8f, 0),
                BuildingModel.Office => (CellType.Commercial, "Văn phòng", 90, 4.2f, 0),
                BuildingModel.OfficeTower => (CellType.Commercial, "Tháp văn phòng", 200, 7.2f, 0),
                BuildingModel.Market => (CellType.Commercial, "Chợ khu phố", 60, 2.3f, 0),
                BuildingModel.ShoppingMall => (CellType.Commercial, "Trung tâm thương mại", 240, 4.5f, 0),
                BuildingModel.HighStreet => (CellType.Commercial, "Phố cửa hàng", 95, 3.2f, 0),
                BuildingModel.PoliceStation => (CellType.CivicService, "Sở cảnh sát", 900, 3.6f, 0),
                BuildingModel.Hospital => (CellType.CivicService, "Bệnh viện", 1800, 5.0f, 0),
                BuildingModel.FireStation => (CellType.CivicService, "Trạm cứu hỏa", 1150, 3.4f, 0),
                BuildingModel.Harbor => (CellType.CivicService, "Cảng tàu thuyền", 1450, 2.4f, 0),
                _ => default
            };

        public static bool IsPublicService(CellType type) => type == CellType.School || type == CellType.CivicService;

        public static int ServiceUpkeep(BuildingModel model) => model switch
        {
            BuildingModel.PoliceStation => 18,
            BuildingModel.Hospital => 35,
            BuildingModel.FireStation => 24,
            BuildingModel.Harbor => 22,
            _ => Get(model).Seats / 15
        };

        public static int Residents(CityCell cell)
        {
            int residentsPerLevel = cell.Model switch
            {
                BuildingModel.Villa => 8,
                BuildingModel.Apartment => 20,
                _ => CitySimulation.ResidentsPerLevel
            };
            return cell.Level * residentsPerLevel;
        }
    }

    public enum ZoneStatus
    {
        NotZoned,
        NeedsRoad,
        NeedsPower,
        WaitingForDevelopment,
        DevelopedWithoutRoad,
        DevelopedWithoutPower,
        Developed
    }

    [Serializable]
    public struct CityCell
    {
        public CellType Type;
        public BuildingModel Model;
        public int Level;
        public float Height;
        public RoadConnections BridgeAxis;
        internal bool IsBridge => BridgeAxis != RoadConnections.None;
    }

    [Serializable]
    internal sealed class CitySnapshot
    {
        public int schemaVersion;
        public int money;
        public long tick;
        public long randomState;
        public int lastTaxIncome;
        public int lastNetIncome;
        public CityCell[] cells;
        public int[] cellIndices;
    }

    public sealed partial class CitySimulation
    {
        internal const int LegacySize = 32;
        public const int GridSize = 512;
        internal const int LegacyOffset = (GridSize - LegacySize) / 2;
        public const int RoadCost = 100;
        public const int ZoneCost = 50;
        private readonly CityCell[,] _cells = new CityCell[GridSize, GridSize];
        private readonly SortedSet<int> _occupied = new SortedSet<int>();
        public const int PowerPlantCost = 1000;
        public const int PowerPlantCapacity = 24;
        public const int ResidentsPerLevel = 12;
        private const int MaxResidentialLevel = 3;
        private const int PowerPlantUpkeep = 60;
        internal const float PowerPlantHeight = 3.2f;
        internal const int CurrentSchemaVersion = 7;
        private readonly HashSet<int> _powered = new HashSet<int>();
        private bool _powerDirty = true;
        private int _unpoweredZones;
        private uint _randomState;
        internal const int MaxUndoSteps = 20;
        private const int DemolishCost = 10;
        private readonly List<UndoStep> _undoSteps = new List<UndoStep>();

        private sealed class UndoStep
        {
            public List<(int X, int Z, CityCell Cell)> Cells;
            public int Spent;
        }

        public bool CanUndoBuild => _undoSteps.Count > 0;

        /// <summary>Cells discarded while loading because they no longer fit the current terrain.</summary>
        internal int DroppedCellsOnLoad { get; private set; }

        public int UnpoweredZones
        {
            get
            {
                RefreshPower();
                return _unpoweredZones;
            }
        }

        public CitySimulation(uint seed = 1)
        {
            _randomState = seed == 0 ? 1u : seed;
            UpdateDemand(0, 0, 0);
        }

        public int Money { get; private set; } = 50000;
        public int Population { get; private set; }
        public long TickCount { get; private set; }
        public int LastTaxIncome { get; private set; }
        public int LastNetIncome { get; private set; }
        public event Action<int, int> CellChanged;
        public RoadGraph Roads { get; } = new RoadGraph(GridSize);

        internal IEnumerable<int> OccupiedIndices => _occupied;

        internal static int CellIndex(int x, int z) => x * GridSize + z;

        internal static bool IsBuildable(int x, int z) => IsInBounds(x, z) && WorldTerrain.IsLand(x, z);

        private void TrackCell(int x, int z, CellType type)
        {
            int index = CellIndex(x, z);
            if (type == CellType.Empty) _occupied.Remove(index);
            else _occupied.Add(index);
            _coverageDirty = true;
            RoadConnections axis = _cells[x, z].BridgeAxis;
            Roads.SetRoad(x, z, type == CellType.Road, axis == RoadConnections.None ? RoadConnections.All : axis);
        }

        public CityCell GetCell(int x, int z) => _cells[x, z];

        public ZoneStatus GetZoneStatus(int x, int z)
        {
            if (!IsInBounds(x, z)) return ZoneStatus.NotZoned;
            CityCell cell = _cells[x, z];
            if (!UsesPower(cell.Type)) return ZoneStatus.NotZoned;
            if (!Roads.HasRoadAccess(x, z))
                return cell.Level == 0 ? ZoneStatus.NeedsRoad : ZoneStatus.DevelopedWithoutRoad;
            RefreshPower();
            if (!_powered.Contains(CellIndex(x, z)))
                return cell.Level == 0 ? ZoneStatus.NeedsPower : ZoneStatus.DevelopedWithoutPower;
            return cell.Level == 0 ? ZoneStatus.WaitingForDevelopment : ZoneStatus.Developed;
        }

        public bool TrySetCell(int x, int z, CellType type, BuildingModel model = BuildingModel.Default) =>
            TryApplyBuild(new[] { (x, z) }, type, model);

        public bool TryApplyBuild(IEnumerable<(int X, int Z)> points, CellType type, BuildingModel model = BuildingModel.Default)
        {
            int moneyBefore = Money;
            var changed = new List<(int X, int Z, CityCell Cell)>();
            foreach (var point in points)
                TrySetCell(point.X, point.Z, type, model, changed);

            if (changed.Count == 0) return false;
            PushUndo(changed, moneyBefore - Money);
            return true;
        }

        private void PushUndo(List<(int X, int Z, CityCell Cell)> cells, int spent)
        {
            if (_undoSteps.Count == MaxUndoSteps) _undoSteps.RemoveAt(0);
            _undoSteps.Add(new UndoStep { Cells = cells, Spent = spent });
        }

        // Steps are undone newest first, so each restored cell is exactly the state that step replaced.
        // Growth from later ticks is discarded with it; the money spent on the step is refunded in full.
        public bool TryUndoBuild()
        {
            if (_undoSteps.Count == 0) return false;

            UndoStep step = _undoSteps[_undoSteps.Count - 1];
            _undoSteps.RemoveAt(_undoSteps.Count - 1);
            for (int i = step.Cells.Count - 1; i >= 0; i--)
            {
                var entry = step.Cells[i];
                ref CityCell cell = ref _cells[entry.X, entry.Z];
                if (cell.Type == CellType.Residential) Population -= BuildingCatalog.Residents(cell);
                if (entry.Cell.Type == CellType.Residential) Population += BuildingCatalog.Residents(entry.Cell);
                cell = entry.Cell;
                TrackCell(entry.X, entry.Z, entry.Cell.Type);
            }

            Money += step.Spent;
            _powerDirty = true;
            foreach (var entry in step.Cells)
                CellChanged?.Invoke(entry.X, entry.Z);
            return true;
        }

        // Demolition never pushes the treasury below zero, so a broke city can still clear costly buildings.
        private int AffordableDemolition(int tiles) => Math.Min(DemolishCost * tiles, Math.Max(0, Money));

        private bool TrySetCell(int x, int z, CellType type, BuildingModel model,
            List<(int X, int Z, CityCell Cell)> changed)
        {
            if (!IsInBounds(x, z) || type < CellType.Empty || type > CellType.CivicService) return false;
            if (_cells[x, z].IsBridge)
                return type == CellType.Empty && RemoveBridge(x, z, changed);
            if (!IsBuildable(x, z)) return false;
            if (model == BuildingModel.Harbor && !CanPlaceHarbor(x, z)) return false;
            var option = BuildingCatalog.Get(model);
            if (model == BuildingModel.Default ? BuildingCatalog.IsPublicService(type) : option.Cost == 0 || option.Type != type)
                return false;
            ref CityCell cell = ref _cells[x, z];
            if (cell.Type == type && cell.Model == model) return false;

            int cost = type switch
            {
                CellType.Road => RoadCost,
                CellType.Empty => AffordableDemolition(1),
                CellType.PowerPlant => PowerPlantCost,
                _ => model == BuildingModel.Default ? ZoneCost : option.Cost
            };
            if (cost > 0 && Money < cost) return false;

            changed.Add((x, z, cell));
            if (cell.Type == CellType.Residential) Population -= BuildingCatalog.Residents(cell);
            cell = type switch
            {
                CellType.PowerPlant => new CityCell { Type = type, Level = 1, Height = PowerPlantHeight },
                CellType.School or CellType.CivicService =>
                    new CityCell { Type = type, Model = model, Level = 1, Height = option.Height },
                _ => new CityCell { Type = type, Model = model }
            };
            _powerDirty = true;
            Money -= cost;
            TrackCell(x, z, type);
            CellChanged?.Invoke(x, z);
            return true;
        }

        public void Tick()
        {
            int population = 0;
            int netIncome = 0;
            int taxIncome = 0;
            int jobs = 0;
            int commercialJobs = 0;
            long happiness = 0;
            foreach (int index in _occupied)
            {
                int x = index / GridSize;
                int z = index % GridSize;
                ref CityCell cell = ref _cells[x, z];
                if (cell.Type == CellType.Road)
                {
                    netIncome -= 2;
                    continue;
                }

                if (cell.Type == CellType.PowerPlant)
                {
                    netIncome -= PowerPlantUpkeep;
                    continue;
                }

                if (BuildingCatalog.IsPublicService(cell.Type))
                {
                    netIncome -= BuildingCatalog.ServiceUpkeep(cell.Model);
                    jobs += OperatingJobs(x, z, cell);
                    continue;
                }

                ZoneStatus status = GetZoneStatus(x, z);
                bool operating = status == ZoneStatus.Developed || status == ZoneStatus.WaitingForDevelopment;
                if (status == ZoneStatus.WaitingForDevelopment && NextRandom() < DevelopmentChance(cell.Type))
                {
                    cell.Level = 1;
                    cell.Height = cell.Model != BuildingModel.Default
                        ? BuildingCatalog.Get(cell.Model).Height : cell.Type switch
                    {
                        CellType.Residential => 2.4f + NextRandom() * 2.1f,
                        CellType.Commercial => 4f + NextRandom() * 3.5f,
                        _ => 2.2f + NextRandom() * 1.6f
                    };
                    CellChanged?.Invoke(x, z);
                }

                else if (status == ZoneStatus.Developed && cell.Type == CellType.Residential &&
                         cell.Level < MaxResidentialLevel && CanUpgradeResidence(x, z, cell.Level) &&
                         NextRandom() < 0.12f)
                {
                    cell.Level++;
                    cell.Height = Math.Min(cell.Height + 0.9f, 7.5f);
                    CellChanged?.Invoke(x, z);
                }

                if (cell.Type == CellType.Residential)
                {
                    population += BuildingCatalog.Residents(cell);
                    happiness += (long)GetHappiness(x, z) * BuildingCatalog.Residents(cell);
                }
                else if (status == ZoneStatus.Developed)
                {
                    int cellJobs = ServiceCatalog.Jobs(cell);
                    jobs += cellJobs;
                    if (cell.Type == CellType.Commercial) commercialJobs += cellJobs;
                }
                if (operating)
                    taxIncome += cell.Type == CellType.Residential ? BuildingCatalog.Residents(cell) * 5 / 4 :
                        15 * cell.Level * EmploymentPercent / 100;
            }

            Population = population;
            UpdateDemand(jobs, commercialJobs, happiness);
            LastTaxIncome = taxIncome;
            LastNetIncome = taxIncome + netIncome;
            Money += LastNetIncome;
            TickCount++;
        }

        internal CitySnapshot CreateSnapshot()
        {
            var snapshot = new CitySnapshot
            {
                schemaVersion = CurrentSchemaVersion,
                money = Money,
                tick = TickCount,
                randomState = _randomState,
                lastTaxIncome = LastTaxIncome,
                lastNetIncome = LastNetIncome,
                cells = new CityCell[_occupied.Count],
                cellIndices = new int[_occupied.Count]
            };
            int offset = 0;
            foreach (int index in _occupied)
            {
                snapshot.cellIndices[offset] = index;
                snapshot.cells[offset++] = _cells[index / GridSize, index % GridSize];
            }
            return snapshot;
        }

        internal static bool TryRestore(CitySnapshot snapshot, out CitySimulation city)
        {
            city = null;
            if (snapshot == null || (snapshot.schemaVersion < 1 || snapshot.schemaVersion > CurrentSchemaVersion) || snapshot.tick < 0 ||
                snapshot.randomState <= 0 || snapshot.randomState > uint.MaxValue ||
                snapshot.cells == null)
                return false;

            bool legacy = snapshot.schemaVersion < 6;
            if (legacy ? snapshot.cells.Length != LegacySize * LegacySize :
                snapshot.cellIndices == null || snapshot.cells.Length != snapshot.cellIndices.Length ||
                snapshot.cells.Length > GridSize * GridSize) return false;

            var restored = new CitySimulation((uint)snapshot.randomState)
            {
                Money = snapshot.money,
                LastTaxIncome = snapshot.schemaVersion >= 3 ? snapshot.lastTaxIncome : 0,
                LastNetIncome = snapshot.schemaVersion >= 3 ? snapshot.lastNetIncome : 0,
                TickCount = snapshot.tick
            };
            int previous = -1;
            for (int i = 0; i < snapshot.cells.Length; i++)
            {
                CityCell cell = snapshot.cells[i];
                if (!IsValidSavedCell(cell, snapshot.schemaVersion)) return false;
                int index = legacy ? CellIndex(i / LegacySize + LegacyOffset, i % LegacySize + LegacyOffset) :
                    snapshot.cellIndices[i];
                if (index <= previous || index >= GridSize * GridSize || (!legacy && cell.Type == CellType.Empty))
                    return false;
                previous = index;
                if (cell.Type == CellType.Empty) continue;
                int x = index / GridSize;
                int z = index % GridSize;
                if (cell.IsBridge == IsBuildable(x, z))
                {
                    restored.DroppedCellsOnLoad++;
                    continue;
                }
                restored._cells[x, z] = cell;
                restored.TrackCell(x, z, cell.Type);
                if (cell.Type == CellType.Residential) restored.Population += BuildingCatalog.Residents(cell);
            }

            var strandedHarbors = new List<int>();
            foreach (int index in restored._occupied)
            {
                int x = index / GridSize;
                int z = index % GridSize;
                if (restored._cells[x, z].Model == BuildingModel.Harbor && !restored.CanPlaceHarbor(x, z))
                    strandedHarbors.Add(index);
            }
            foreach (int index in strandedHarbors)
            {
                restored._cells[index / GridSize, index % GridSize] = default;
                restored.TrackCell(index / GridSize, index % GridSize, CellType.Empty);
                restored.DroppedCellsOnLoad++;
            }
            restored.RecalculateEconomy();
            city = restored;
            return true;
        }

        private static bool IsValidSavedCell(CityCell cell, int version)
        {
            if (cell.IsBridge)
                return version >= 7 && cell.Type == CellType.Road && cell.Model == BuildingModel.Default &&
                    cell.Height == 0f && cell.Level == 0 &&
                    (cell.BridgeAxis == BridgeAlongX || cell.BridgeAxis == BridgeAlongZ);

            if (cell.Model != BuildingModel.Default)
            {
                var option = BuildingCatalog.Get(cell.Model);
                int modelVersion = cell.Model == BuildingModel.Harbor ? 7 : cell.Model >= BuildingModel.HighStreet ? 5 : 4;
                if (version < modelVersion || option.Cost == 0 || option.Type != cell.Type) return false;
                if (BuildingCatalog.IsPublicService(cell.Type))
                    return cell.Level == 1 && cell.Height == option.Height;
            }

            if (cell.Type == CellType.PowerPlant)
                return version >= 2 && cell.Level == 1 && cell.Height == PowerPlantHeight;
            int maxLevel = version >= 3 && cell.Type == CellType.Residential ? MaxResidentialLevel : 1;
            if (cell.Type < CellType.Empty || cell.Type > CellType.Industrial || cell.Level < 0 || cell.Level > maxLevel)
                return false;
            return cell.Level == 0 ? cell.Height == 0f : UsesPower(cell.Type) && cell.Height >= 2.2f && cell.Height <= 7.5f;
        }

        private void RefreshPower()
        {
            if (!_powerDirty) return;

            var components = new Dictionary<int, int>();
            var pending = new Queue<int>();
            int component = 0;
            foreach (int index in _occupied)
            {
                int x = index / GridSize;
                int z = index % GridSize;
                if (_cells[x, z].Type != CellType.Road || components.ContainsKey(index)) continue;
                component++;
                Visit(index);
                while (pending.Count > 0)
                {
                    int road = pending.Dequeue();
                    RoadConnections edges = Roads.GetConnections(road / GridSize, road % GridSize);
                    if ((edges & RoadConnections.West) != 0) Visit(road - GridSize);
                    if ((edges & RoadConnections.East) != 0) Visit(road + GridSize);
                    if ((edges & RoadConnections.South) != 0) Visit(road - 1);
                    if ((edges & RoadConnections.North) != 0) Visit(road + 1);
                }
            }

            var capacity = new int[component + 1];
            foreach (int index in _occupied)
                if (_cells[index / GridSize, index % GridSize].Type == CellType.PowerPlant)
                    capacity[AdjacentComponent(index)] += PowerPlantCapacity;

            _unpoweredZones = 0;
            _powered.Clear();
            foreach (int index in _occupied)
            {
                if (!UsesPower(_cells[index / GridSize, index % GridSize].Type)) continue;
                int grid = AdjacentComponent(index);
                if (grid == 0) continue;
                if (capacity[grid] == 0)
                {
                    _unpoweredZones++;
                    continue;
                }
                _powered.Add(index);
                capacity[grid]--;
            }

            _powerDirty = false;

            void Visit(int index)
            {
                if (components.ContainsKey(index)) return;
                components[index] = component;
                pending.Enqueue(index);
            }

            int AdjacentComponent(int index)
            {
                RoadConnections access = Roads.GetAccess(index / GridSize, index % GridSize);
                if ((access & RoadConnections.West) != 0) return components[index - GridSize];
                if ((access & RoadConnections.East) != 0) return components[index + GridSize];
                if ((access & RoadConnections.South) != 0) return components[index - 1];
                return (access & RoadConnections.North) != 0 ? components[index + 1] : 0;
            }
        }

        private static bool UsesPower(CellType type) =>
            (type >= CellType.Residential && type <= CellType.Industrial) || BuildingCatalog.IsPublicService(type);

        private static bool IsInBounds(int x, int z) =>
            x >= 0 && x < GridSize && z >= 0 && z < GridSize;

        private float NextRandom()
        {
            _randomState ^= _randomState << 13;
            _randomState ^= _randomState >> 17;
            _randomState ^= _randomState << 5;
            return (_randomState >> 8) * (1f / 16777216f);
        }
    }
}
