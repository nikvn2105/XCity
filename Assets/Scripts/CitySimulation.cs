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

    public struct CityCell
    {
        public CellType Type;
        public int Level;
        public float Height;
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

                    if (cell.Level == 0 && HasAdjacentRoad(x, z) && NextRandom() < 0.35f)
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

        private bool HasAdjacentRoad(int x, int z) =>
            IsRoad(x - 1, z) || IsRoad(x + 1, z) || IsRoad(x, z - 1) || IsRoad(x, z + 1);

        private bool IsRoad(int x, int z) => IsInBounds(x, z) && _cells[x, z].Type == CellType.Road;

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
