using System;

namespace XCity
{
    public enum CellType
    {
        Empty,
        Road,
        Residential,
        Commercial,
        Industrial
    }

    [Serializable]
    public struct CityCell
    {
        public CellType Type;
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
        public CityCell[] cells;
    }

    public sealed class CitySimulation
    {
        public const int GridSize = 32;
        public const int RoadCost = 100;
        public const int ZoneCost = 50;
        private readonly CityCell[,] _cells = new CityCell[GridSize, GridSize];
        private uint _randomState;

        public CitySimulation(uint seed = 1)
        {
            _randomState = seed == 0 ? 1u : seed;
        }

        public int Money { get; private set; } = 50000;
        public int Population { get; private set; }
        public long TickCount { get; private set; }
        public event Action<int, int> CellChanged;
        public RoadGraph Roads { get; } = new RoadGraph(GridSize);

        public CityCell GetCell(int x, int z) => _cells[x, z];

        public bool TrySetCell(int x, int z, CellType type)
        {
            if (!IsInBounds(x, z) || type < CellType.Empty || type > CellType.Industrial) return false;
            ref CityCell cell = ref _cells[x, z];
            if (cell.Type == type) return false;

            int cost = type == CellType.Road ? RoadCost : type == CellType.Empty ? 10 : ZoneCost;
            if (type != CellType.Empty && Money < cost) return false;

            if (cell.Type == CellType.Residential) Population -= 12 * cell.Level;
            cell = new CityCell { Type = type };
            Money -= cost;
            Roads.SetRoad(x, z, type == CellType.Road);
            CellChanged?.Invoke(x, z);
            return true;
        }

        public void Tick()
        {
            int population = 0;
            int netIncome = 0;
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

                    if (cell.Level == 0 && Roads.HasRoadAccess(x, z) && NextRandom() < 0.35f)
                    {
                        cell.Level = 1;
                        cell.Height = cell.Type switch
                        {
                            CellType.Residential => 2.4f + NextRandom() * 2.1f,
                            CellType.Commercial => 4f + NextRandom() * 3.5f,
                            _ => 2.2f + NextRandom() * 1.6f
                        };
                        CellChanged?.Invoke(x, z);
                    }

                    if (cell.Type == CellType.Residential) population += 12 * cell.Level;
                    netIncome += 15 * cell.Level;
                }
            }

            Population = population;
            Money += netIncome;
            TickCount++;
        }

        internal CitySnapshot CreateSnapshot()
        {
            var snapshot = new CitySnapshot
            {
                schemaVersion = 1,
                money = Money,
                tick = TickCount,
                randomState = _randomState,
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
            if (snapshot == null || snapshot.schemaVersion != 1 || snapshot.tick < 0 ||
                snapshot.randomState <= 0 || snapshot.randomState > uint.MaxValue ||
                snapshot.cells == null || snapshot.cells.Length != GridSize * GridSize)
                return false;

            var restored = new CitySimulation((uint)snapshot.randomState)
            {
                Money = snapshot.money,
                TickCount = snapshot.tick
            };
            for (int x = 0; x < GridSize; x++)
            {
                for (int z = 0; z < GridSize; z++)
                {
                    CityCell cell = snapshot.cells[x * GridSize + z];
                    if (cell.Type < CellType.Empty || cell.Type > CellType.Industrial ||
                        cell.Level < 0 || cell.Level > 1 ||
                        (cell.Level == 0 && cell.Height != 0f) ||
                        (cell.Level > 0 && (cell.Type < CellType.Residential || !(cell.Height >= 2.2f && cell.Height <= 7.5f))))
                        return false;

                    restored._cells[x, z] = cell;
                    restored.Roads.SetRoad(x, z, cell.Type == CellType.Road);
                    if (cell.Type == CellType.Residential) restored.Population += 12 * cell.Level;
                }
            }

            city = restored;
            return true;
        }

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
