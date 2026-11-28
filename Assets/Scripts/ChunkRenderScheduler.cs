using System.Collections.Generic;
using UnityEngine;

namespace XCity
{
    internal enum ChunkDetail
    {
        Hidden,
        Far,
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
        private const float OverviewEnterAltitude = 165f;
        private const float OverviewExitAltitude = 195f;
        private const float OverviewEnterDistance = 210f;
        private const float OverviewExitDistance = 240f;
        private const float MinViewDistance = 278f;

        private readonly Plane[] _frustum = new Plane[6];
        private readonly ChunkDetail[,] _loaded = new ChunkDetail[Count, Count];
        private readonly HashSet<Vector2Int> _active = new HashSet<Vector2Int>();
        private readonly List<(int X, int Z, ChunkDetail Detail, float Priority)> _changes =
            new List<(int X, int Z, ChunkDetail Detail, float Priority)>();
        private readonly CityChunkRenderer _terrain;
        private readonly BuildingChunkRenderer _buildings;
        private readonly float _chunkSpan;

        private bool _allowOverview = true;
        private Vector3 _lastEye = new Vector3(float.NaN, 0f, 0f);
        private Vector3 _lastForward;
        private float _lastViewDistance;
        private bool _hasPendingChanges = true;

        public ChunkRenderScheduler(CityChunkRenderer terrain, BuildingChunkRenderer buildings, float cellSize)
        {
            _terrain = terrain;
            _buildings = buildings;
            _chunkSpan = Size * cellSize;
        }

        public bool ShowEffects { get; private set; } = true;

        private float _detailScale = 1f;

        /// <summary>Stretches the full-detail range (distance and camera altitude); set per graphics level.</summary>
        public float DetailScale
        {
            get => _detailScale;
            set
            {
                _detailScale = value;
                _hasPendingChanges = true;
            }
        }

        public void Refresh(Camera camera, int budget)
        {
            Vector3 eye = camera.transform.position;
            Vector3 forward = camera.transform.forward;
            float viewDistance = Mathf.Max(MinViewDistance, camera.farClipPlane + _chunkSpan);

            if (budget < int.MaxValue / 2 && !_hasPendingChanges &&
                (eye - _lastEye).sqrMagnitude < 0.0001f &&
                (forward - _lastForward).sqrMagnitude < 0.000001f &&
                Mathf.Abs(viewDistance - _lastViewDistance) < 0.1f)
            {
                return;
            }

            _lastEye = eye;
            _lastForward = forward;
            _lastViewDistance = viewDistance;
            _terrain.SetCameraPosition(eye);
            _buildings.SetCameraPosition(eye);

            GeometryUtility.CalculateFrustumPlanes(camera, _frustum);
            ShowEffects = eye.y < (ShowEffects ? FullExitAltitude : FullEnterAltitude) * _detailScale;
            _allowOverview = eye.y < (_allowOverview ? OverviewExitAltitude : OverviewEnterAltitude);
            _changes.Clear();

            int minX = Mathf.Clamp(Mathf.FloorToInt((eye.x - viewDistance) / _chunkSpan), 0, Count - 1);
            int minZ = Mathf.Clamp(Mathf.FloorToInt((eye.z - viewDistance) / _chunkSpan), 0, Count - 1);
            int maxX = Mathf.Clamp(Mathf.FloorToInt((eye.x + viewDistance) / _chunkSpan), 0, Count - 1);
            int maxZ = Mathf.Clamp(Mathf.FloorToInt((eye.z + viewDistance) / _chunkSpan), 0, Count - 1);
            foreach (Vector2Int chunk in _active)
            {
                if (chunk.x < minX || chunk.x > maxX || chunk.y < minZ || chunk.y > maxZ)
                    _changes.Add((chunk.x, chunk.y, ChunkDetail.Hidden, -200000f));
            }

            float viewDistanceSq = viewDistance * viewDistance;
            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    var center = new Vector3((x + 0.5f) * _chunkSpan, 4f, (z + 0.5f) * _chunkSpan);
                    var bounds = new Bounds(center, new Vector3(_chunkSpan, 12f + 2f * WorldTerrain.MaxSurfaceY, _chunkSpan));
                    var bufferedBounds = bounds;
                    bufferedBounds.Expand(_chunkSpan * 0.40f);
                    ChunkDetail current = _loaded[x, z];
                    ChunkDetail desired = ChunkDetail.Hidden;

                    if (bufferedBounds.SqrDistance(eye) <= viewDistanceSq &&
                        GeometryUtility.TestPlanesAABB(_frustum, bufferedBounds))
                    {
                        float fullDist = (current == ChunkDetail.Full ? FullExitDistance : FullEnterDistance) * _detailScale;
                        float overviewDist = current == ChunkDetail.Overview || current == ChunkDetail.Full
                            ? OverviewExitDistance : OverviewEnterDistance;
                        float distSq = bounds.SqrDistance(eye);

                        if (ShowEffects && distSq < fullDist * fullDist)
                            desired = ChunkDetail.Full;
                        else if (_allowOverview && distSq < overviewDist * overviewDist)
                            desired = ChunkDetail.Overview;
                        else
                            desired = ChunkDetail.Far;
                    }

                    if (desired == current) continue;
                    float priority = (center - eye).sqrMagnitude;
                    if (current == ChunkDetail.Hidden) priority -= 100000f;
                    else if (desired == ChunkDetail.Hidden) priority -= 40000f;
                    else if (desired == ChunkDetail.Overview || desired == ChunkDetail.Far) priority -= 20000f;
                    _changes.Add((x, z, desired, priority));
                }
            }

            _changes.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            int remaining = budget;
            _hasPendingChanges = false;
            for (int i = 0; i < _changes.Count; i++)
            {
                var change = _changes[i];
                bool requiresImmediateBuild = change.Detail == ChunkDetail.Full || change.Detail == ChunkDetail.Overview;
                if (requiresImmediateBuild)
                {
                    if (remaining <= 0)
                    {
                        _hasPendingChanges = true;
                        continue;
                    }
                    remaining--;
                }

                _terrain.SetDetail(change.X, change.Z, change.Detail);
                _buildings.SetDetail(change.X, change.Z, change.Detail);
                _loaded[change.X, change.Z] = change.Detail;
                var position = new Vector2Int(change.X, change.Z);
                if (change.Detail == ChunkDetail.Hidden) _active.Remove(position);
                else _active.Add(position);
            }

            if (budget >= int.MaxValue / 2)
            {
                _terrain.RebuildDirtyChunks(int.MaxValue);
                _buildings.RebuildDirtyChunks(int.MaxValue);
            }
        }

        public (int Full, int Overview, int Hidden) GetChunkCounts()
        {
            var counts = GetDetailCounts();
            return (counts.Full, counts.Overview + counts.Far, counts.Hidden);
        }

        public (int Full, int Overview, int Far, int Hidden) GetDetailCounts()
        {
            int full = 0;
            int overview = 0;
            int far = 0;
            foreach (Vector2Int chunk in _active)
            {
                ChunkDetail detail = _loaded[chunk.x, chunk.y];
                if (detail == ChunkDetail.Full) full++;
                else if (detail == ChunkDetail.Overview) overview++;
                else if (detail == ChunkDetail.Far) far++;
            }
            return (full, overview, far, Count * Count - full - overview - far);
        }
    }
}
