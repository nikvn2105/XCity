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
        FireStation
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
                _ => default
            };

        public static bool IsPublicService(CellType type) => type == CellType.School || type == CellType.CivicService;

        public static int ServiceUpkeep(BuildingModel model) => model switch
        {
            BuildingModel.PoliceStation => 18,
            BuildingModel.Hospital => 35,
            BuildingModel.FireStation => 24,
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
    }

    public sealed class CitySimulation
    {
        public const int GridSize = 32;
        public const int RoadCost = 100;
        public const int ZoneCost = 50;
        private readonly CityCell[,] _cells = new CityCell[GridSize, GridSize];
        public const int PowerPlantCost = 1000;
        public const int PowerPlantCapacity = 24;
        public const int ResidentsPerLevel = 12;
        private const int MaxResidentialLevel = 3;
        private const int PowerPlantUpkeep = 60;
        internal const float PowerPlantHeight = 3.2f;
        internal const int CurrentSchemaVersion = 5;
        private readonly bool[,] _powered = new bool[GridSize, GridSize];
        private bool _powerDirty = true;
        private int _unpoweredZones;
        private uint _randomState;
        private List<(int X, int Z, CityCell Cell)> _undoCells;
        private int _undoMoney;
        private int _undoPopulation;

        public bool CanUndoBuild => _undoCells != null;

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
        }

        public int Money { get; private set; } = 50000;
        public int Population { get; private set; }
        public long TickCount { get; private set; }
        public int LastTaxIncome { get; private set; }
        public int LastNetIncome { get; private set; }
        public event Action<int, int> CellChanged;
        public RoadGraph Roads { get; } = new RoadGraph(GridSize);

        public CityCell GetCell(int x, int z) => _cells[x, z];

        public ZoneStatus GetZoneStatus(int x, int z)
        {
            if (!IsInBounds(x, z)) return ZoneStatus.NotZoned;
            CityCell cell = _cells[x, z];
            if (!UsesPower(cell.Type)) return ZoneStatus.NotZoned;
            if (!Roads.HasRoadAccess(x, z))
                return cell.Level == 0 ? ZoneStatus.NeedsRoad : ZoneStatus.DevelopedWithoutRoad;
            RefreshPower();
            if (!_powered[x, z])
                return cell.Level == 0 ? ZoneStatus.NeedsPower : ZoneStatus.DevelopedWithoutPower;
            return cell.Level == 0 ? ZoneStatus.WaitingForDevelopment : ZoneStatus.Developed;
        }

        public bool TrySetCell(int x, int z, CellType type, BuildingModel model = BuildingModel.Default) =>
            TryApplyBuild(new[] { (x, z) }, type, model);

        public bool TryApplyBuild(IEnumerable<(int X, int Z)> points, CellType type, BuildingModel model = BuildingModel.Default)
        {
            int moneyBefore = Money;
            int populationBefore = Population;
            var changed = new List<(int X, int Z, CityCell Cell)>();
            foreach (var point in points)
                TrySetCell(point.X, point.Z, type, model, changed);

            if (changed.Count == 0) return false;
            _undoCells = changed;
            _undoMoney = moneyBefore;
            _undoPopulation = populationBefore;
            return true;
        }

        public bool TryUndoBuild()
        {
            if (_undoCells == null) return false;

            var cells = _undoCells;
            _undoCells = null;
            foreach (var entry in cells)
            {
                _cells[entry.X, entry.Z] = entry.Cell;
                Roads.SetRoad(entry.X, entry.Z, entry.Cell.Type == CellType.Road);
            }

            Money = _undoMoney;
            Population = _undoPopulation;
            _powerDirty = true;
            foreach (var entry in cells)
                CellChanged?.Invoke(entry.X, entry.Z);
            return true;
        }

        private bool TrySetCell(int x, int z, CellType type, BuildingModel model,
            List<(int X, int Z, CityCell Cell)> changed)
        {
            if (!IsInBounds(x, z) || type < CellType.Empty || type > CellType.CivicService) return false;
            var option = BuildingCatalog.Get(model);
            if (model == BuildingModel.Default ? BuildingCatalog.IsPublicService(type) : option.Cost == 0 || option.Type != type)
                return false;
            ref CityCell cell = ref _cells[x, z];
            if (cell.Type == type && cell.Model == model) return false;

            int cost = type switch
            {
                CellType.Road => RoadCost,
                CellType.Empty => 10,
                CellType.PowerPlant => PowerPlantCost,
                _ => model == BuildingModel.Default ? ZoneCost : option.Cost
            };
            if (type != CellType.Empty && Money < cost) return false;

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
            Roads.SetRoad(x, z, type == CellType.Road);
            CellChanged?.Invoke(x, z);
            return true;
        }

        public void Tick()
        {
            _undoCells = null;
            int population = 0;
            int netIncome = 0;
            int taxIncome = 0;
            for (int x = 0; x < GridSize; x++)
            {
                for (int z = 0; z < GridSize; z++)
                {
                    ref CityCell cell = ref _cells[x, z];
                    if (cell.Type == CellType.Empty) continue;
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
                        continue;
                    }

                    ZoneStatus status = GetZoneStatus(x, z);
                    bool operating = status == ZoneStatus.Developed || status == ZoneStatus.WaitingForDevelopment;
                    if (status == ZoneStatus.WaitingForDevelopment && NextRandom() < 0.35f)
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
                             cell.Level < MaxResidentialLevel && NextRandom() < 0.12f)
                    {
                        cell.Level++;
                        cell.Height = Math.Min(cell.Height + 0.9f, 7.5f);
                        CellChanged?.Invoke(x, z);
                    }

                    if (cell.Type == CellType.Residential) population += BuildingCatalog.Residents(cell);
                    if (operating)
                        taxIncome += cell.Type == CellType.Residential ? BuildingCatalog.Residents(cell) * 5 / 4 : 15 * cell.Level;
                }
            }

            Population = population;
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
                cells = new CityCell[GridSize * GridSize]
            };
            for (int x = 0; x < GridSize; x++)
                for (int z = 0; z < GridSize; z++)
                    snapshot.cells[x * GridSize + z] = _cells[x, z];
            return snapshot;
        }

        internal static bool TryRestore(CitySnapshot snapshot, out CitySimulation city)
        {
            city = null;
            if (snapshot == null || (snapshot.schemaVersion < 1 || snapshot.schemaVersion > CurrentSchemaVersion) || snapshot.tick < 0 ||
                snapshot.randomState <= 0 || snapshot.randomState > uint.MaxValue ||
                snapshot.cells == null || snapshot.cells.Length != GridSize * GridSize)
                return false;

            var restored = new CitySimulation((uint)snapshot.randomState)
            {
                Money = snapshot.money,
                LastTaxIncome = snapshot.schemaVersion >= 3 ? snapshot.lastTaxIncome : 0,
                LastNetIncome = snapshot.schemaVersion >= 3 ? snapshot.lastNetIncome : 0,
                TickCount = snapshot.tick
            };
            for (int x = 0; x < GridSize; x++)
            {
                for (int z = 0; z < GridSize; z++)
                {
                    CityCell cell = snapshot.cells[x * GridSize + z];
                    if (!IsValidSavedCell(cell, snapshot.schemaVersion))
                        return false;

                    restored._cells[x, z] = cell;
                    restored.Roads.SetRoad(x, z, cell.Type == CellType.Road);
                    if (cell.Type == CellType.Residential) restored.Population += BuildingCatalog.Residents(cell);
                }
            }

            city = restored;
            return true;
        }

        private static bool IsValidSavedCell(CityCell cell, int version)
        {
            if (cell.Model != BuildingModel.Default)
            {
                var option = BuildingCatalog.Get(cell.Model);
                int modelVersion = cell.Model >= BuildingModel.HighStreet ? 5 : 4;
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

            var components = new int[GridSize, GridSize];
            var pending = new Queue<int>();
            int component = 0;
            for (int x = 0; x < GridSize; x++)
            {
                for (int z = 0; z < GridSize; z++)
                {
                    if (_cells[x, z].Type != CellType.Road || components[x, z] != 0) continue;
                    component++;
                    Visit(x, z);
                    while (pending.Count > 0)
                    {
                        int index = pending.Dequeue();
                        int rx = index / GridSize;
                        int rz = index % GridSize;
                        RoadConnections edges = Roads.GetConnections(rx, rz);
                        if ((edges & RoadConnections.West) != 0) Visit(rx - 1, rz);
                        if ((edges & RoadConnections.East) != 0) Visit(rx + 1, rz);
                        if ((edges & RoadConnections.South) != 0) Visit(rx, rz - 1);
                        if ((edges & RoadConnections.North) != 0) Visit(rx, rz + 1);
                    }
                }
            }

            var capacity = new int[component + 1];
            for (int x = 0; x < GridSize; x++)
                for (int z = 0; z < GridSize; z++)
                    if (_cells[x, z].Type == CellType.PowerPlant)
                        capacity[AdjacentComponent(x, z)] += PowerPlantCapacity;

            _unpoweredZones = 0;
            Array.Clear(_powered, 0, _powered.Length);
            for (int x = 0; x < GridSize; x++)
            {
                for (int z = 0; z < GridSize; z++)
                {
                    if (!UsesPower(_cells[x, z].Type)) continue;
                    int grid = AdjacentComponent(x, z);
                    if (grid == 0) continue;
                    if (capacity[grid] == 0)
                    {
                        _unpoweredZones++;
                        continue;
                    }

                    _powered[x, z] = true;
                    capacity[grid]--;
                }
            }
            _powerDirty = false;

            void Visit(int x, int z)
            {
                if (components[x, z] != 0) return;
                components[x, z] = component;
                pending.Enqueue(x * GridSize + z);
            }

            int AdjacentComponent(int x, int z)
            {
                if (x > 0 && components[x - 1, z] != 0) return components[x - 1, z];
                if (x < GridSize - 1 && components[x + 1, z] != 0) return components[x + 1, z];
                if (z > 0 && components[x, z - 1] != 0) return components[x, z - 1];
                return z < GridSize - 1 ? components[x, z + 1] : 0;
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
