using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace XCity
{
    internal sealed class BuildingChunkRenderer : IDisposable
    {
        private const int ChunkSize = ChunkRenderScheduler.Size;
        private const int ChunkCount = ChunkRenderScheduler.Count;
        private const int FarBlockChunks = 8;
        private const int FarBlockCount = (ChunkCount + FarBlockChunks - 1) / FarBlockChunks;

        private readonly HashSet<Vector2Int> _dirty = new HashSet<Vector2Int>();
        private readonly HashSet<Vector2Int> _dirtyFar = new HashSet<Vector2Int>();
        private readonly Mesh[,] _meshes = new Mesh[ChunkCount, ChunkCount];
        private readonly MeshRenderer[,] _layers = new MeshRenderer[ChunkCount, ChunkCount];
        private readonly Mesh[,] _farMeshes = new Mesh[FarBlockCount, FarBlockCount];
        private readonly MeshRenderer[,] _farLayers = new MeshRenderer[FarBlockCount, FarBlockCount];
        private readonly ChunkDetail[,] _details = new ChunkDetail[ChunkCount, ChunkCount];
        private readonly bool[,] _waitingRendered = new bool[CitySimulation.GridSize, CitySimulation.GridSize];
        private readonly MeshBuilder _builder = new MeshBuilder(buildingSurfaces: true);
        private readonly MeshBuilder _overviewBuilder = new MeshBuilder();
        private readonly MeshBuilder _farBuilder = new MeshBuilder();
        private readonly CitySimulation _city;
        private readonly Material _material;
        private readonly Material _overviewMaterial;
        private readonly Transform _root;
        private readonly float _cellSize;

        private Func<int, int, bool> _isUnderConstruction;
        private long _lastSyncedTick = -1;
        private Vector3 _eye;

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

        public void SetCameraPosition(Vector3 eye) => _eye = eye;

        public void SetDetail(int x, int z, ChunkDetail detail)
        {
            ChunkDetail prev = _details[x, z];
            if (prev == detail) return;
            _details[x, z] = detail;
            var chunk = new Vector2Int(x, z);
            _dirty.Remove(chunk);

            if (detail != ChunkDetail.Full && detail != ChunkDetail.Overview)
                ReleaseChunk(x, z);
            else
                RebuildChunk(chunk);

            if (prev == ChunkDetail.Far || detail == ChunkDetail.Far)
                _dirtyFar.Add(new Vector2Int(x / FarBlockChunks, z / FarBlockChunks));
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
            ChunkDetail detail = _details[cx, cz];
            if (detail == ChunkDetail.Full || detail == ChunkDetail.Overview)
                _dirty.Add(new Vector2Int(cx, cz));
            else if (detail == ChunkDetail.Far)
                _dirtyFar.Add(new Vector2Int(cx / FarBlockChunks, cz / FarBlockChunks));
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
            while (_dirty.Count > 0 && budget > 0)
            {
                budget--;
                var pending = _dirty.GetEnumerator();
                pending.MoveNext();
                Vector2Int chunk = pending.Current;
                _dirty.Remove(chunk);
                RebuildChunk(chunk);
            }

            while (_dirtyFar.Count > 0 && budget > 0)
            {
                budget--;
                Vector2Int block = PopClosestFarBlock();
                RebuildFarBlock(block);
            }
        }

        private Vector2Int PopClosestFarBlock()
        {
            float blockSpan = FarBlockChunks * ChunkSize * _cellSize;
            Vector2Int best = default;
            float bestDistSq = float.MaxValue;
            foreach (Vector2Int block in _dirtyFar)
            {
                float bx = (block.x + 0.5f) * blockSpan - _eye.x;
                float bz = (block.y + 0.5f) * blockSpan - _eye.z;
                float distSq = bx * bx + bz * bz;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = block;
                }
            }
            _dirtyFar.Remove(best);
            return best;
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
                        var centerLocal = new Vector3((x - startX + 0.5f) * _cellSize, WorldTerrain.SurfaceY(x, z), (z - startZ + 0.5f) * _cellSize);
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
                        Vector3 centerLocal = new Vector3((x - startX + 0.5f) * _cellSize, WorldTerrain.SurfaceY(x, z), (z - startZ + 0.5f) * _cellSize);
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
            renderer.shadowCastingMode = full ? ShadowCastingMode.On : ShadowCastingMode.Off;
            meshBuilder.ApplyTo(mesh);
        }

        private void RebuildFarBlock(Vector2Int block)
        {
            _farBuilder.Clear();
            _farBuilder.SetFrame(Vector3.zero, 0);

            int startCx = block.x * FarBlockChunks;
            int startCz = block.y * FarBlockChunks;
            int endCx = Mathf.Min(startCx + FarBlockChunks, ChunkCount);
            int endCz = Mathf.Min(startCz + FarBlockChunks, ChunkCount);
            int originX = startCx * ChunkSize;
            int originZ = startCz * ChunkSize;

            for (int cx = startCx; cx < endCx; cx++)
            {
                for (int cz = startCz; cz < endCz; cz++)
                {
                    if (_details[cx, cz] != ChunkDetail.Far) continue;

                    int startX = cx * ChunkSize;
                    int startZ = cz * ChunkSize;
                    int endX = Mathf.Min(startX + ChunkSize, CitySimulation.GridSize);
                    int endZ = Mathf.Min(startZ + ChunkSize, CitySimulation.GridSize);

                    for (int x = startX; x < endX; x++)
                    {
                        for (int z = startZ; z < endZ; z++)
                        {
                            CityCell cell = _city.GetCell(x, z);
                            if (cell.Level <= 0) continue;

                            var (walls, roof, width) = GetFarPalette(cell.Type);
                            float height = Mathf.Max(1.5f, cell.Height);
                            float depth = width * 0.86f;
                            var center = new Vector3(
                                (x - originX + 0.5f) * _cellSize,
                                WorldTerrain.SurfaceY(x, z) + CityChunkRenderer.BuildingBase + height * 0.5f,
                                (z - originZ + 0.5f) * _cellSize);
                            _farBuilder.AddSimpleBox(center, new Vector3(width, height, depth), walls, roof);
                        }
                    }
                }
            }

            Mesh mesh = _farMeshes[block.x, block.y];
            MeshRenderer renderer = _farLayers[block.x, block.y];
            if (_farBuilder.VertexCount == 0)
            {
                if (renderer != null)
                {
                    renderer.enabled = false;
                    UnityEngine.Object.Destroy(renderer.gameObject);
                    _farLayers[block.x, block.y] = null;
                }
                if (mesh != null)
                {
                    UnityEngine.Object.Destroy(mesh);
                    _farMeshes[block.x, block.y] = null;
                }
                return;
            }

            if (mesh == null)
            {
                mesh = new Mesh { name = $"BuildingFar_{block.x}_{block.y}" };
                _farMeshes[block.x, block.y] = mesh;
                var layer = new GameObject($"BuildingFar_{block.x}_{block.y}");
                layer.transform.SetParent(_root, false);
                layer.transform.localPosition = new Vector3(originX * _cellSize, 0f, originZ * _cellSize);
                layer.AddComponent<MeshFilter>().sharedMesh = mesh;
                renderer = layer.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _overviewMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                _farLayers[block.x, block.y] = renderer;
            }

            _farBuilder.ApplyTo(mesh);
        }

        private static (Color32 Walls, Color32 Roof, float Width) GetFarPalette(CellType type) => type switch
        {
            CellType.Residential => (new Color32(173, 156, 131, 255), new Color32(116, 86, 73, 255), 2.65f),
            CellType.Commercial => (new Color32(106, 140, 148, 255), new Color32(145, 154, 151, 255), 2.73f),
            CellType.Industrial => (new Color32(145, 142, 130, 255), new Color32(111, 118, 120, 255), 3.10f),
            CellType.School => (new Color32(201, 186, 147, 255), new Color32(88, 129, 124, 255), 2.98f),
            CellType.CivicService => (new Color32(189, 191, 177, 255), new Color32(97, 121, 132, 255), 3.06f),
            _ => (new Color32(159, 154, 141, 255), new Color32(84, 90, 93, 255), 3.02f)
        };

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
            foreach (Mesh mesh in _farMeshes)
                if (mesh != null) UnityEngine.Object.Destroy(mesh);
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
        }
    }
}
