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
        North = 8,
        All = West | East | South | North
    }

    public sealed class RoadGraph
    {
        private readonly RoadConnections[,] _allowed;
        private readonly RoadConnections[,] _connections;
        private readonly int _size;

        public RoadGraph(int size)
        {
            _size = size;
            _allowed = new RoadConnections[size, size];
            _connections = new RoadConnections[size, size];
        }

        public int Version { get; private set; }
        public RoadConnections GetConnections(int x, int z) => _connections[x, z];

        public bool HasRoadAccess(int x, int z) => GetAccess(x, z) != RoadConnections.None;

        internal RoadConnections GetAccess(int x, int z)
        {
            RoadConnections access = RoadConnections.None;
            if (Allows(x - 1, z, RoadConnections.East)) access |= RoadConnections.West;
            if (Allows(x + 1, z, RoadConnections.West)) access |= RoadConnections.East;
            if (Allows(x, z - 1, RoadConnections.North)) access |= RoadConnections.South;
            if (Allows(x, z + 1, RoadConnections.South)) access |= RoadConnections.North;
            return access;
        }

        internal void SetRoad(int x, int z, bool present, RoadConnections directions = RoadConnections.All)
        {
            RoadConnections allowed = present ? directions : RoadConnections.None;
            if (_allowed[x, z] == allowed) return;

            _allowed[x, z] = allowed;
            _connections[x, z] = RoadConnections.None;
            UpdateEdge(x, z, x - 1, z, RoadConnections.West, RoadConnections.East);
            UpdateEdge(x, z, x + 1, z, RoadConnections.East, RoadConnections.West);
            UpdateEdge(x, z, x, z - 1, RoadConnections.South, RoadConnections.North);
            UpdateEdge(x, z, x, z + 1, RoadConnections.North, RoadConnections.South);
            Version++;
        }

        private bool Allows(int x, int z, RoadConnections direction) =>
            x >= 0 && z >= 0 && x < _size && z < _size && (_allowed[x, z] & direction) != 0;

        private void UpdateEdge(int x, int z, int nx, int nz, RoadConnections direction, RoadConnections reverse)
        {
            if (nx < 0 || nz < 0 || nx >= _size || nz >= _size) return;
            if (Allows(x, z, direction) && Allows(nx, nz, reverse))
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
