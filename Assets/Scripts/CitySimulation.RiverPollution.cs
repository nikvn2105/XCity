using System.Collections.Generic;

namespace XCity
{
    // Phase 3d: sewage plants and industry beside a river or lake dirty the water downstream. A pump
    // drawing from dirty water still supplies, but its homes count as half short of water.
    // Recomputed with the utility networks; nothing is saved.
    public sealed partial class CitySimulation
    {
        internal const int RiverPollutionLength = 60;
        internal const int LakePollutionCells = 150;
        internal const int DirtyWaterPenalty = MissingUtilityPenalty / 2;

        private readonly HashSet<int> _pollutedWater = new HashSet<int>();
        private readonly Queue<int> _pollutionQueue = new Queue<int>();
        private readonly bool[] _dirtyWater = new bool[GridSize * GridSize];
        private int[] _dirtyWaterCapacity = new int[1];

        public bool IsPollutedWater(int x, int z)
        {
            RefreshPower();
            return IsInBounds(x, z) && _pollutedWater.Contains(CellIndex(x, z));
        }

        /// <summary>True when this cell's water comes from a pump on polluted water.</summary>
        public bool HasDirtyWater(int x, int z)
        {
            RefreshPower();
            return IsInBounds(x, z) && _watered[CellIndex(x, z)] && _dirtyWater[CellIndex(x, z)];
        }

        public bool PumpOnPollutedWater(int x, int z)
        {
            RefreshPower();
            return IsInBounds(x, z) && TouchesPollutedWater(x, z);
        }

        private bool PumpDrawsPollutedWater(int index) => TouchesPollutedWater(index / GridSize, index % GridSize);

        private void RebuildRiverPollution()
        {
            _pollutedWater.Clear();
            foreach (int index in _utilitySources)
                if (_cells[index / GridSize, index % GridSize].Model == BuildingModel.SewagePlant) PolluteBeside(index);
            foreach (int index in _utilityConsumers)
                if (_cells[index / GridSize, index % GridSize].Type == CellType.Industrial) PolluteBeside(index);
        }

        private void PolluteBeside(int index)
        {
            int x = index / GridSize, z = index % GridSize;
            PolluteFrom(x - 1, z);
            PolluteFrom(x + 1, z);
            PolluteFrom(x, z - 1);
            PolluteFrom(x, z + 1);
        }

        private void PolluteFrom(int x, int z)
        {
            WaterKind kind = WorldTerrain.WaterKind(x, z);
            if (!IsInBounds(x, z) || _pollutedWater.Contains(CellIndex(x, z))) return;
            if (kind == WaterKind.River)
            {
                int next = CellIndex(x, z);
                for (int step = 0; step < RiverPollutionLength && next >= 0; step++)
                {
                    _pollutedWater.Add(next);
                    next = WorldTerrain.Downstream(next / GridSize, next % GridSize);
                }
            }
            else if (kind == WaterKind.Lake)
            {
                // Spreads through the lake from the outlet, nearest cells first.
                _pollutionQueue.Clear();
                _pollutionQueue.Enqueue(CellIndex(x, z));
                _pollutedWater.Add(CellIndex(x, z));
                int count = 1;
                while (_pollutionQueue.Count > 0 && count < LakePollutionCells)
                {
                    int cell = _pollutionQueue.Dequeue();
                    int cx = cell / GridSize, cz = cell % GridSize;
                    Spread(cx - 1, cz);
                    Spread(cx + 1, cz);
                    Spread(cx, cz - 1);
                    Spread(cx, cz + 1);
                }

                void Spread(int sx, int sz)
                {
                    if (count >= LakePollutionCells || !IsInBounds(sx, sz) || WorldTerrain.WaterKind(sx, sz) != WaterKind.Lake) return;
                    if (!_pollutedWater.Add(CellIndex(sx, sz))) return;
                    count++;
                    _pollutionQueue.Enqueue(CellIndex(sx, sz));
                }
            }
        }

        private bool TouchesPollutedWater(int x, int z) =>
            IsPolluted(x - 1, z) || IsPolluted(x + 1, z) || IsPolluted(x, z - 1) || IsPolluted(x, z + 1);

        private bool IsPolluted(int x, int z) => IsInBounds(x, z) && _pollutedWater.Contains(CellIndex(x, z));
    }
}
