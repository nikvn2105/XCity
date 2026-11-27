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
        private const float ViewDistance = 278f;

        private readonly Plane[] _frustum = new Plane[6];
        private readonly ChunkDetail[,] _loaded = new ChunkDetail[Count, Count];
        private readonly HashSet<Vector2Int> _active = new HashSet<Vector2Int>();
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
            int minX = Mathf.Clamp(Mathf.FloorToInt((eye.x - ViewDistance) / _chunkSpan), 0, Count - 1);
            int minZ = Mathf.Clamp(Mathf.FloorToInt((eye.z - ViewDistance) / _chunkSpan), 0, Count - 1);
            int maxX = Mathf.Clamp(Mathf.FloorToInt((eye.x + ViewDistance) / _chunkSpan), 0, Count - 1);
            int maxZ = Mathf.Clamp(Mathf.FloorToInt((eye.z + ViewDistance) / _chunkSpan), 0, Count - 1);
            foreach (Vector2Int chunk in _active)
            {
                if (chunk.x < minX || chunk.x > maxX || chunk.y < minZ || chunk.y > maxZ)
                    _changes.Add((chunk.x, chunk.y, ChunkDetail.Hidden, -200000f));
            }

            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    var center = new Vector3((x + 0.5f) * _chunkSpan, 4f, (z + 0.5f) * _chunkSpan);
                    var bounds = new Bounds(center, new Vector3(_chunkSpan, 12f, _chunkSpan));
                    var bufferedBounds = bounds;
                    bufferedBounds.Expand(_chunkSpan * 0.40f);
                    ChunkDetail current = _loaded[x, z];
                    ChunkDetail desired = ChunkDetail.Hidden;

                    if (bufferedBounds.SqrDistance(eye) <= ViewDistance * ViewDistance &&
                        GeometryUtility.TestPlanesAABB(_frustum, bufferedBounds))
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
                var position = new Vector2Int(change.X, change.Z);
                if (change.Detail == ChunkDetail.Hidden) _active.Remove(position);
                else _active.Add(position);
            }
        }

        public (int Full, int Overview, int Hidden) GetChunkCounts()
        {
            int full = 0;
            int overview = 0;
            foreach (Vector2Int chunk in _active)
            {
                ChunkDetail detail = _loaded[chunk.x, chunk.y];
                if (detail == ChunkDetail.Full) full++;
                if (detail == ChunkDetail.Overview) overview++;
            }
            return (full, overview, Count * Count - full - overview);
        }
    }
}
