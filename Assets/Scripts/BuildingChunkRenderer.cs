using System;
using System.Collections.Generic;
using UnityEngine;

namespace XCity
{
    internal sealed class BuildingChunkRenderer : IDisposable
    {
        private const int ChunkSize = 8;
        private const int ChunkCount = CitySimulation.GridSize / ChunkSize;

        private readonly HashSet<Vector2Int> _dirty = new HashSet<Vector2Int>();
        private readonly Mesh[,] _meshes = new Mesh[ChunkCount, ChunkCount];
        private readonly bool[,] _waitingRendered = new bool[CitySimulation.GridSize, CitySimulation.GridSize];
        private readonly MeshBuilder _builder = new MeshBuilder();
        private readonly CitySimulation _city;
        private readonly Material _material;
        private readonly Transform _root;
        private readonly float _cellSize;

        private Func<int, int, bool> _isUnderConstruction;
        private long _lastSyncedTick = -1;

        public BuildingChunkRenderer(CitySimulation city, float cellSize, Material material, Transform parent)
        {
            _city = city;
            _cellSize = cellSize;
            _material = material;
            _root = new GameObject("BuildingChunks").transform;
            _root.SetParent(parent, false);
            _city.CellChanged += MarkDirty;

            for (int x = 0; x < CitySimulation.GridSize; x++)
            {
                for (int z = 0; z < CitySimulation.GridSize; z++)
                {
                    CityCell cell = _city.GetCell(x, z);
                    bool waiting = IsZone(cell.Type) && cell.Level == 0 &&
                                   _city.GetZoneStatus(x, z) == ZoneStatus.WaitingForDevelopment;
                    if (cell.Level > 0 || cell.Type == CellType.PowerPlant || waiting)
                        _dirty.Add(new Vector2Int(x / ChunkSize, z / ChunkSize));
                }
            }
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

        private void AddChunk(int x, int z) => _dirty.Add(new Vector2Int(x / ChunkSize, z / ChunkSize));

        private void SyncWaitingZones()
        {
            long tick = _city.TickCount;
            if (tick == _lastSyncedTick) return;
            _lastSyncedTick = tick;

            for (int x = 0; x < CitySimulation.GridSize; x++)
            {
                for (int z = 0; z < CitySimulation.GridSize; z++)
                {
                    CityCell cell = _city.GetCell(x, z);
                    bool waiting = IsZone(cell.Type) && cell.Level == 0 &&
                                   _city.GetZoneStatus(x, z) == ZoneStatus.WaitingForDevelopment;
                    if (waiting != _waitingRendered[x, z])
                        AddChunk(x, z);
                }
            }
        }

        public void RebuildDirtyChunks()
        {
            SyncWaitingZones();
            if (_dirty.Count == 0) return;

            foreach (Vector2Int chunk in _dirty)
                RebuildChunk(chunk);
            _dirty.Clear();
        }

        private void RebuildChunk(Vector2Int chunk)
        {
            _builder.Clear();
            int startX = chunk.x * ChunkSize;
            int startZ = chunk.y * ChunkSize;

            for (int x = startX; x < startX + ChunkSize; x++)
            {
                for (int z = startZ; z < startZ + ChunkSize; z++)
                {
                    if (_isUnderConstruction != null && _isUnderConstruction(x, z))
                    {
                        _waitingRendered[x, z] = false;
                        continue;
                    }

                    CityCell cell = _city.GetCell(x, z);
                    bool isDevelopedZone = IsZone(cell.Type) && cell.Level > 0;
                    if (isDevelopedZone || cell.Type == CellType.PowerPlant)
                    {
                        _waitingRendered[x, z] = false;
                        int quarterTurns = PickQuarterTurns(_city, x, z);
                        Vector3 centerLocal = new Vector3((x - startX + 0.5f) * _cellSize, 0f, (z - startZ + 0.5f) * _cellSize);
                        BuildingMeshFactory.Build(_builder, x, z, cell, quarterTurns, centerLocal);
                    }
                    else if (IsZone(cell.Type) && cell.Level == 0 &&
                             _city.GetZoneStatus(x, z) == ZoneStatus.WaitingForDevelopment)
                    {
                        _waitingRendered[x, z] = true;
                        int quarterTurns = PickQuarterTurns(_city, x, z);
                        Vector3 centerLocal = new Vector3((x - startX + 0.5f) * _cellSize, 0f, (z - startZ + 0.5f) * _cellSize);
                        BuildingMeshFactory.BuildGroundwork(_builder, x, z, cell.Type, quarterTurns, centerLocal);
                    }
                    else
                    {
                        _waitingRendered[x, z] = false;
                    }
                }
            }

            if (_builder.VertexCount > 60000)
                Debug.LogWarning($"Building chunk ({chunk.x}, {chunk.y}) has {_builder.VertexCount} vertices.");

            Mesh mesh = _meshes[chunk.x, chunk.y];
            if (_builder.VertexCount == 0)
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
                layer.AddComponent<MeshRenderer>().sharedMaterial = _material;
            }

            _builder.ApplyTo(mesh);
        }

        internal static int PickQuarterTurns(CitySimulation city, int x, int z)
        {
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
