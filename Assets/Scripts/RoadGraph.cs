using System;

namespace XCity
{
    [Flags]
    public enum RoadConnections : byte
    {
        None = 0,
        West = 1,
        East = 2,
        South = 4,
        North = 8
    }

    public sealed class RoadGraph
    {
        private readonly bool[,] _roads;
        private readonly RoadConnections[,] _connections;
        private readonly int _size;

        public RoadGraph(int size)
        {
            _size = size;
            _roads = new bool[size, size];
            _connections = new RoadConnections[size, size];
        }

        public int Version { get; private set; }
        public RoadConnections GetConnections(int x, int z) => _connections[x, z];

        public bool HasRoadAccess(int x, int z) =>
            IsRoad(x - 1, z) || IsRoad(x + 1, z) || IsRoad(x, z - 1) || IsRoad(x, z + 1);

        internal void SetRoad(int x, int z, bool present)
        {
            if (_roads[x, z] == present) return;

            _roads[x, z] = present;
            _connections[x, z] = RoadConnections.None;
            UpdateEdge(x, z, x - 1, z, RoadConnections.West, RoadConnections.East);
            UpdateEdge(x, z, x + 1, z, RoadConnections.East, RoadConnections.West);
            UpdateEdge(x, z, x, z - 1, RoadConnections.South, RoadConnections.North);
            UpdateEdge(x, z, x, z + 1, RoadConnections.North, RoadConnections.South);
            Version++;
        }

        private bool IsRoad(int x, int z) => x >= 0 && z >= 0 && x < _size && z < _size && _roads[x, z];

        private void UpdateEdge(int x, int z, int nx, int nz, RoadConnections direction, RoadConnections reverse)
        {
            if (!IsRoad(nx, nz)) return;
            if (_roads[x, z])
            {
                _connections[x, z] |= direction;
                _connections[nx, nz] |= reverse;
            }
            else
            {
                _connections[nx, nz] &= ~reverse;
            }
        }
    }
}
