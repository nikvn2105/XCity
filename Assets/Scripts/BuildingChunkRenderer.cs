using System;
using System.Collections.Generic;
using UnityEngine;

namespace XCity
{
    internal sealed class BuildingChunkRenderer : IDisposable
    {
        private const int ChunkSize = ChunkRenderScheduler.Size;
        private const int ChunkCount = ChunkRenderScheduler.Count;

        private readonly HashSet<Vector2Int> _dirty = new HashSet<Vector2Int>();
        private readonly Mesh[,] _meshes = new Mesh[ChunkCount, ChunkCount];
        private readonly MeshRenderer[,] _layers = new MeshRenderer[ChunkCount, ChunkCount];
        private readonly ChunkDetail[,] _details = new ChunkDetail[ChunkCount, ChunkCount];
        private readonly bool[,] _waitingRendered = new bool[CitySimulation.GridSize, CitySimulation.GridSize];
        private readonly MeshBuilder _builder = new MeshBuilder(buildingSurfaces: true);
        private readonly MeshBuilder _overviewBuilder = new MeshBuilder();
        private readonly CitySimulation _city;
        private readonly Material _material;
        private readonly Material _overviewMaterial;
        private readonly Transform _root;
        private readonly float _cellSize;

        private Func<int, int, bool> _isUnderConstruction;
        private long _lastSyncedTick = -1;

        public BuildingChunkRenderer(CitySimulation city, float cellSize, Material material, Material overviewMaterial, Transform parent)
        {
            _city = city;
            _cellSize = cellSize;
            _material = material;
            _overviewMaterial = overviewMaterial;
            _root = new GameObject("BuildingChunks").transform;
            _root.SetParent(parent, false);
            _city.CellChanged += MarkDirty;

        }

        public void SetDetail(int x, int z, ChunkDetail detail)
        {
            _details[x, z] = detail;
            var chunk = new Vector2Int(x, z);
            _dirty.Remove(chunk);
            if (detail != ChunkDetail.Full)
                ReleaseChunk(x, z);
            if (detail != ChunkDetail.Hidden)
                RebuildChunk(chunk);
        }

        private void ReleaseChunk(int x, int z)
        {
            if (_meshes[x, z] != null) UnityEngine.Object.Destroy(_meshes[x, z]);
            if (_layers[x, z] != null)
            {
                _layers[x, z].enabled = false;
                UnityEngine.Object.Destroy(_layers[x, z].gameObject);
            }
            _meshes[x, z] = null;
            _layers[x, z] = null;
        }

        public void SetConstructionFilter(Func<int, int, bool> filter) => _isUnderConstruction = filter;

        public void RefreshCell(int x, int z) => MarkDirty(x, z);

        private void MarkDirty(int x, int z)
        {
            AddChunk(x, z);
            if (x > 0) AddChunk(x - 1, z);
            if (x < CitySimulation.GridSize - 1) AddChunk(x + 1, z);
            if (z > 0) AddChunk(x, z - 1);
            if (z < CitySimulation.GridSize - 1) AddChunk(x, z + 1);
        }

        private void AddChunk(int x, int z)
        {
            int cx = x / ChunkSize;
            int cz = z / ChunkSize;
            if (_details[cx, cz] != ChunkDetail.Hidden)
                _dirty.Add(new Vector2Int(cx, cz));
        }

        private void SyncWaitingZones()
        {
            long tick = _city.TickCount;
            if (tick == _lastSyncedTick) return;
            _lastSyncedTick = tick;

            foreach (int index in _city.OccupiedIndices)
            {
                int x = index / CitySimulation.GridSize;
                int z = index % CitySimulation.GridSize;
                if (_details[x / ChunkSize, z / ChunkSize] != ChunkDetail.Full ||
                    _isUnderConstruction != null && _isUnderConstruction(x, z)) continue;

                CityCell cell = _city.GetCell(x, z);
                bool waiting = IsZone(cell.Type) && cell.Level == 0 &&
                               _city.GetZoneStatus(x, z) == ZoneStatus.WaitingForDevelopment;
                if (waiting != _waitingRendered[x, z]) AddChunk(x, z);
            }
        }

        public void RebuildDirtyChunks(int budget)
        {
            SyncWaitingZones();
            while (_dirty.Count > 0 && budget-- > 0)
            {
                var pending = _dirty.GetEnumerator();
                pending.MoveNext();
                Vector2Int chunk = pending.Current;
                _dirty.Remove(chunk);
                RebuildChunk(chunk);
            }
        }

        private void RebuildChunk(Vector2Int chunk)
        {
            bool full = _details[chunk.x, chunk.y] == ChunkDetail.Full;
            MeshBuilder meshBuilder = full ? _builder : _overviewBuilder;
            meshBuilder.Clear();
            int startX = chunk.x * ChunkSize;
            int startZ = chunk.y * ChunkSize;

            for (int x = startX; x < Mathf.Min(startX + ChunkSize, CitySimulation.GridSize); x++)
            {
                for (int z = startZ; z < Mathf.Min(startZ + ChunkSize, CitySimulation.GridSize); z++)
                {
                    if (full && _isUnderConstruction != null && _isUnderConstruction(x, z))
                    {
                        _waitingRendered[x, z] = false;
                        continue;
                    }

                    CityCell cell = _city.GetCell(x, z);
                    if (cell.Level > 0)
                    {
                        _waitingRendered[x, z] = false;
                        var centerLocal = new Vector3((x - startX + 0.5f) * _cellSize, 0f, (z - startZ + 0.5f) * _cellSize);
                        int quarterTurns = PickQuarterTurns(_city, x, z);
                        if (full)
                            BuildingMeshFactory.Build(meshBuilder, x, z, cell, quarterTurns, centerLocal);
                        else
                            BuildingMeshFactory.BuildOverview(meshBuilder, cell, quarterTurns, centerLocal);
                    }
                    else if (full && IsZone(cell.Type) && cell.Level == 0 &&
                             _city.GetZoneStatus(x, z) == ZoneStatus.WaitingForDevelopment)
                    {
                        _waitingRendered[x, z] = true;
                        int quarterTurns = PickQuarterTurns(_city, x, z);
                        Vector3 centerLocal = new Vector3((x - startX + 0.5f) * _cellSize, 0f, (z - startZ + 0.5f) * _cellSize);
                        BuildingMeshFactory.BuildGroundwork(meshBuilder, x, z, cell.Type, quarterTurns, centerLocal);
                    }
                    else
                    {
                        _waitingRendered[x, z] = false;
                    }
                }
            }

            if (meshBuilder.VertexCount > 60000)
                Debug.LogWarning($"Building chunk ({chunk.x}, {chunk.y}) has {meshBuilder.VertexCount} vertices.");

            Mesh mesh = _meshes[chunk.x, chunk.y];
            if (meshBuilder.VertexCount == 0)
            {
                if (mesh != null) mesh.Clear();
                return;
            }

            if (mesh == null)
            {
                mesh = new Mesh { name = $"BuildingChunk_{chunk.x}_{chunk.y}" };
                _meshes[chunk.x, chunk.y] = mesh;
                var layer = new GameObject($"BuildingChunk_{chunk.x}_{chunk.y}");
                layer.transform.SetParent(_root, false);
                layer.transform.localPosition = new Vector3(startX * _cellSize, 0f, startZ * _cellSize);
                layer.AddComponent<MeshFilter>().sharedMesh = mesh;
                _layers[chunk.x, chunk.y] = layer.AddComponent<MeshRenderer>();
            }

            MeshRenderer renderer = _layers[chunk.x, chunk.y];
            renderer.sharedMaterial = full ? _material : _overviewMaterial;
            renderer.shadowCastingMode = full
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.Off;
            meshBuilder.ApplyTo(mesh);
        }

        internal static int PickQuarterTurns(CitySimulation city, int x, int z)
        {
            if (city.GetCell(x, z).Model == BuildingModel.Harbor &&
                CitySimulation.TryHarborFacing(x, z, out int towardWater, out _, out _))
                return towardWater;
            if (IsRoad(city, x, z + 1)) return 0; // +Z
            if (IsRoad(city, x + 1, z)) return 1; // +X
            if (IsRoad(city, x, z - 1)) return 2; // -Z
            if (IsRoad(city, x - 1, z)) return 3; // -X
            return (int)(BuildingMeshFactory.Hash(x, z, 99) & 3u);
        }

        private static bool IsRoad(CitySimulation city, int x, int z) =>
            x >= 0 && z >= 0 && x < CitySimulation.GridSize && z < CitySimulation.GridSize &&
            city.GetCell(x, z).Type == CellType.Road;

        private static bool IsZone(CellType type) =>
            type >= CellType.Residential && type <= CellType.Industrial;

        public void Dispose()
        {
            _city.CellChanged -= MarkDirty;
            foreach (Mesh mesh in _meshes)
                if (mesh != null) UnityEngine.Object.Destroy(mesh);
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
        }
    }
}
