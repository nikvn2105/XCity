using System.Collections.Generic;
using UnityEngine;

namespace XCity
{
    internal enum ChunkDetail
    {
        Hidden,
        Overview,
        Full
    }

    internal sealed class ChunkRenderScheduler
    {
        internal const int Size = 8;
        internal const int Count = (CitySimulation.GridSize + Size - 1) / Size;
        private const float FullEnterAltitude = 49f;
        private const float FullExitAltitude = 57f;
        private const float FullEnterDistance = 54f;
        private const float FullExitDistance = 64f;

        private readonly Plane[] _frustum = new Plane[6];
        private readonly ChunkDetail[,] _loaded = new ChunkDetail[Count, Count];
        private readonly List<(int X, int Z, ChunkDetail Detail, float Priority)> _changes =
            new List<(int X, int Z, ChunkDetail Detail, float Priority)>();
        private readonly CityChunkRenderer _terrain;
        private readonly BuildingChunkRenderer _buildings;
        private readonly float _chunkSpan;

        public ChunkRenderScheduler(CityChunkRenderer terrain, BuildingChunkRenderer buildings, float cellSize)
        {
            _terrain = terrain;
            _buildings = buildings;
            _chunkSpan = Size * cellSize;
        }

        public bool ShowEffects { get; private set; } = true;

        public void Refresh(Camera camera, int budget)
        {
            GeometryUtility.CalculateFrustumPlanes(camera, _frustum);
            Vector3 eye = camera.transform.position;
            ShowEffects = eye.y < (ShowEffects ? FullExitAltitude : FullEnterAltitude);
            _changes.Clear();
            for (int x = 0; x < Count; x++)
            {
                for (int z = 0; z < Count; z++)
                {
                    var center = new Vector3((x + 0.5f) * _chunkSpan, 4f, (z + 0.5f) * _chunkSpan);
                    var bounds = new Bounds(center, new Vector3(_chunkSpan, 12f, _chunkSpan));
                    var bufferedBounds = bounds;
                    bufferedBounds.Expand(_chunkSpan * 0.40f);
                    ChunkDetail current = _loaded[x, z];
                    ChunkDetail desired = ChunkDetail.Hidden;

                    if (GeometryUtility.TestPlanesAABB(_frustum, bufferedBounds))
                    {
                        float distance = current == ChunkDetail.Full ? FullExitDistance : FullEnterDistance;
                        desired = ShowEffects && bounds.SqrDistance(eye) < distance * distance
                            ? ChunkDetail.Full : ChunkDetail.Overview;
                    }

                    if (desired == current) continue;
                    float priority = (center - eye).sqrMagnitude;
                    if (current == ChunkDetail.Hidden) priority -= 100000f;
                    else if (desired == ChunkDetail.Hidden) priority -= 40000f;
                    else if (desired == ChunkDetail.Overview) priority -= 20000f;
                    _changes.Add((x, z, desired, priority));
                }
            }

            _changes.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            for (int i = 0; i < _changes.Count && i < budget; i++)
            {
                var change = _changes[i];
                _terrain.SetDetail(change.X, change.Z, change.Detail);
                _buildings.SetDetail(change.X, change.Z, change.Detail);
                _loaded[change.X, change.Z] = change.Detail;
            }
        }

        public (int Full, int Overview, int Hidden) GetChunkCounts()
        {
            int full = 0;
            int overview = 0;
            foreach (ChunkDetail detail in _loaded)
            {
                if (detail == ChunkDetail.Full) full++;
                if (detail == ChunkDetail.Overview) overview++;
            }
            return (full, overview, Count * Count - full - overview);
        }
    }
}
