using System;
using System.Collections.Generic;

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

        private readonly Queue<int> _frontier = new Queue<int>();
        private int[] _visited;
        private byte[] _arrivedBy;
        private int _search;

        /// <summary>
        /// Shortest route over connected road cells, as direction indices (0=W, 1=E, 2=S, 3=N, matching the
        /// RoadConnections bits) leaving each cell from the start. Tiles cost the same, so breadth-first is optimal.
        /// </summary>
        // shortcut: gives up after maxCells visited cells, so very distant goals fall back to wandering; add A* if cities outgrow it.
        public bool TryFindPath(int startX, int startZ, int goalX, int goalZ, List<int> route, int maxCells = 20000)
        {
            route.Clear();
            if (_connections[startX, startZ] == RoadConnections.None || _connections[goalX, goalZ] == RoadConnections.None)
                return false;
            if (_visited == null)
            {
                _visited = new int[_size * _size];
                _arrivedBy = new byte[_size * _size];
            }
            if (++_search == int.MaxValue)
            {
                Array.Clear(_visited, 0, _visited.Length);
                _search = 1;
            }

            int start = startX * _size + startZ, goal = goalX * _size + goalZ;
            Queue<int> frontier = _frontier;
            frontier.Clear();
            frontier.Enqueue(start);
            _visited[start] = _search;
            int seen = 1;
            while (frontier.Count > 0 && _visited[goal] != _search && seen < maxCells)
            {
                int cell = frontier.Dequeue();
                RoadConnections edges = _connections[cell / _size, cell % _size];
                for (int d = 0; d < 4; d++)
                {
                    if (((int)edges & (1 << d)) == 0) continue;
                    int next = cell + Step(d);
                    if (_visited[next] == _search) continue;
                    _visited[next] = _search;
                    _arrivedBy[next] = (byte)d;
                    frontier.Enqueue(next);
                    seen++;
                }
            }
            if (_visited[goal] != _search) return false;

            for (int cell = goal; cell != start; cell -= Step(_arrivedBy[cell]))
                route.Add(_arrivedBy[cell]);
            route.Reverse();
            return true;

            int Step(int d) => d == 0 ? -_size : d == 1 ? _size : d == 2 ? -1 : 1;
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
