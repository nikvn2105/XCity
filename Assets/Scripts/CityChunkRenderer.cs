using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace XCity
{
    internal sealed class CityChunkRenderer : IDisposable
    {
        internal const float BuildingWidthRatio = 0.72f;
        internal const float BuildingBase = 0.1f;
        private const int ChunkSize = ChunkRenderScheduler.Size;
        private const int ChunkCount = ChunkRenderScheduler.Count;
        private const float RoadY = 0.048f;
        private const float CurbY = 0.106f;
        private const float PaintY = RoadY + 0.004f;
        private static readonly RoadConnections[] Directions =
        {
            RoadConnections.North, RoadConnections.East, RoadConnections.South, RoadConnections.West
        };
        private static readonly Color32 Asphalt = new Color32(62, 73, 84, 255);
        private static readonly Color32 Paving = new Color32(167, 163, 151, 255);
        private static readonly Color32 Curb = new Color32(135, 135, 127, 255);
        private static readonly Color32 Paint = new Color32(240, 231, 200, 255);
        private static readonly Color32 LanePaint = new Color32(236, 189, 98, 255);
        private static readonly Color32 GrassLight = new Color32(111, 144, 92, 255);
        private static readonly Color32 GrassDark = new Color32(79, 115, 77, 255);

        private readonly HashSet<Vector2Int> _dirty = new HashSet<Vector2Int>();
        private readonly Mesh[,] _surfaces = new Mesh[ChunkCount, ChunkCount];
        private readonly Mesh[,] _scenery = new Mesh[ChunkCount, ChunkCount];
        private readonly GameObject[,] _groundLayers = new GameObject[ChunkCount, ChunkCount];
        private readonly GameObject[,] _sceneryLayers = new GameObject[ChunkCount, ChunkCount];
        private readonly ChunkDetail[,] _details = new ChunkDetail[ChunkCount, ChunkCount];
        private readonly CellType[,] _renderedTypes = new CellType[CitySimulation.GridSize, CitySimulation.GridSize];
        private readonly MeshBuilder _surface = new MeshBuilder();
        private readonly MeshBuilder _props = new MeshBuilder();
        private readonly CitySimulation _city;
        private readonly Material _material;
        private readonly Transform _root;
        private readonly float _cellSize;

        public CityChunkRenderer(CitySimulation city, float cellSize, Material material, Transform parent)
        {
            _city = city;
            _cellSize = cellSize;
            _material = material;
            _root = new GameObject("Streets and Landscape").transform;
            _root.SetParent(parent, false);
            _city.CellChanged += MarkDirty;

        }

        public void SetDetail(int x, int z, ChunkDetail detail)
        {
            _details[x, z] = detail;
            var chunk = new Vector2Int(x, z);
            _dirty.Remove(chunk);
            if (detail != ChunkDetail.Full)
            {
                ReleaseLayer(ref _surfaces[x, z], ref _groundLayers[x, z]);
                ReleaseLayer(ref _scenery[x, z], ref _sceneryLayers[x, z]);
            }
            if (detail != ChunkDetail.Hidden) RebuildChunk(chunk);
        }

        private static void ReleaseLayer(ref Mesh mesh, ref GameObject layer)
        {
            if (layer != null)
            {
                layer.SetActive(false);
                UnityEngine.Object.Destroy(layer);
                layer = null;
            }
            if (mesh != null)
            {
                UnityEngine.Object.Destroy(mesh);
                mesh = null;
            }
        }

        private void MarkDirty(int x, int z)
        {
            CellType before = _renderedTypes[x, z];
            CellType after = _city.GetCell(x, z).Type;
            if (before == after) return;
            MarkChunk(x, z);
            if (before != CellType.Road && after != CellType.Road) return;
            // A connection changes the facing arm of the road across the chunk boundary too.
            MarkChunk(x - 1, z);
            MarkChunk(x + 1, z);
            MarkChunk(x, z - 1);
            MarkChunk(x, z + 1);
        }

        private void MarkChunk(int x, int z)
        {
            if (x < 0 || z < 0 || x >= CitySimulation.GridSize || z >= CitySimulation.GridSize) return;
            int cx = x / ChunkSize;
            int cz = z / ChunkSize;
            if (_details[cx, cz] != ChunkDetail.Hidden) _dirty.Add(new Vector2Int(cx, cz));
        }

        public void RebuildDirtyChunks(int budget)
        {
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
            _surface.Clear();
            _props.Clear();
            bool full = _details[chunk.x, chunk.y] == ChunkDetail.Full;
            int startX = chunk.x * ChunkSize;
            int startZ = chunk.y * ChunkSize;
            for (int x = startX; x < Mathf.Min(startX + ChunkSize, CitySimulation.GridSize); x++)
            {
                for (int z = startZ; z < Mathf.Min(startZ + ChunkSize, CitySimulation.GridSize); z++)
                {
                    CityCell cell = _city.GetCell(x, z);
                    _renderedTypes[x, z] = cell.Type;
                    var center = new Vector3((x - startX + 0.5f) * _cellSize, 0, (z - startZ + 0.5f) * _cellSize);
                    _surface.SetFrame(center, 0);
                    _props.SetFrame(center, 0);
                    if (!WorldTerrain.IsLand(x, z)) continue;
                    AddCoast(x, z);
                    if (cell.Type == CellType.Road)
                    {
                        if (full) AddStreet(center, x, z);
                        else AddOverviewStreet(center, x, z);
                    }
                    else
                        AddLot(cell.Type, x, z, full);
                }
            }

            var origin = new Vector3(startX * _cellSize, 0, startZ * _cellSize);
            ApplyLayer(_surface, ref _surfaces[chunk.x, chunk.y], ref _groundLayers[chunk.x, chunk.y], $"Ground_{chunk.x}_{chunk.y}", origin, ShadowCastingMode.Off);
            if (full) ApplyLayer(_props, ref _scenery[chunk.x, chunk.y], ref _sceneryLayers[chunk.x, chunk.y], $"Scenery_{chunk.x}_{chunk.y}", origin, ShadowCastingMode.On);
        }

        private void ApplyLayer(MeshBuilder builder, ref Mesh mesh, ref GameObject layer, string name, Vector3 origin, ShadowCastingMode shadows)
        {
            if (mesh == null)
            {
                if (builder.VertexCount == 0) return;
                mesh = new Mesh { name = name };
                mesh.MarkDynamic();
                layer = new GameObject(name);
                layer.transform.SetParent(_root, false);
                layer.transform.localPosition = origin;
                layer.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = layer.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _material;
                renderer.shadowCastingMode = shadows;
            }
            builder.ApplyTo(mesh);
        }

        private void AddCoast(int x, int z)
        {
            if (WorldTerrain.LandHeight(x, z) > 0.13f) return;
            float edge = _cellSize * 0.5f;
            const float waterline = -1.08f;
            var sand = new Color32(164, 153, 119, 255);
            var shore = new Color32(122, 134, 106, 255);
            if (!WorldTerrain.IsLand(x + 1, z)) Cliff(edge, -edge, edge, edge, Vector3.right);
            if (!WorldTerrain.IsLand(x - 1, z)) Cliff(-edge, edge, -edge, -edge, Vector3.left);
            if (!WorldTerrain.IsLand(x, z + 1)) Cliff(edge, edge, -edge, edge, Vector3.forward);
            if (!WorldTerrain.IsLand(x, z - 1)) Cliff(-edge, -edge, edge, -edge, Vector3.back);

            void Cliff(float x0, float z0, float x1, float z1, Vector3 normal)
            {
                _surface.AddQuad(new Vector3(x0, waterline, z0), new Vector3(x1, waterline, z1),
                    new Vector3(x1, 0.007f, z1), new Vector3(x0, 0.007f, z0), normal, shore);
                Vector3 outside = normal * (_cellSize * 0.18f);
                var a = new Vector3(x0, -0.97f, z0);
                var b = new Vector3(x1, -0.97f, z1);
                _surface.AddQuad(a, b, b + outside, a + outside, Vector3.up, sand);
            }
        }

        private void AddOverviewStreet(Vector3 center, int x, int z)
        {
            RoadConnections links = _city.Roads.GetConnections(x, z);
            float half = _cellSize * 0.5f;
            float lane = half * 0.67f;
            Rect(_surface, -half, -half, half, half, RoadY - 0.012f, Paving);
            Rect(_surface, -lane, -lane, lane, lane, RoadY, Asphalt);
            for (int turn = 0; turn < Directions.Length; turn++)
            {
                if ((links & Directions[turn]) == 0) continue;
                _surface.SetFrame(center, turn);
                Rect(_surface, -lane, lane, lane, half, RoadY, Asphalt);
            }
        }

        private void AddStreet(Vector3 center, int x, int z)
        {
            RoadConnections links = _city.Roads.GetConnections(x, z);
            float half = _cellSize * 0.5f;
            float lane = half * 0.67f;
            float shoulder = (half + lane) * 0.5f;
            int degree = 0;
            foreach (RoadConnections direction in Directions)
                if ((links & direction) != 0) degree++;

            Rect(_surface, -lane, -lane, lane, lane, RoadY, Asphalt);
            for (int turn = 0; turn < Directions.Length; turn++)
            {
                _surface.SetFrame(center, turn);
                Rect(_surface, lane, lane, half, half, CurbY, Paving);

                bool connected = (links & Directions[turn]) != 0;
                bool rightConnected = (links & Directions[(turn + 1) % 4]) != 0;
                if (connected)
                {
                    Rect(_surface, -lane, lane, lane, half, RoadY, Asphalt);
                    AddCurb(lane, lane, lane, half, Vector3.left);
                    AddCurb(-lane, half, -lane, lane, Vector3.right);

                    if (degree >= 3)
                        AddCrosswalk(lane, half);
                    else
                        Rect(_surface, -0.044f, shoulder - 0.17f, 0.044f, shoulder + 0.17f, PaintY, LanePaint);
                }
                else
                {
                    Rect(_surface, -lane, lane, lane, half, CurbY, Paving);
                    AddCurb(-lane, lane, lane, lane, Vector3.back);
                }

                // At the inner corner of a bend the two curb faces meet without covering the road.
                if (degree == 2 && connected && rightConnected)
                    Rect(_surface, lane + 0.12f, lane + 0.12f, half - 0.12f, half - 0.12f,
                        CurbY + 0.003f, Curb);
            }

            uint seed = BuildingMeshFactory.Hash(x, z, 412);
            if (degree > 1 && seed % 5u == 0)
            {
                _props.SetFrame(center, (int)((seed >> 8) & 3u));
                AddStreetlight(shoulder);
            }
            else if (degree >= 2 && seed % 7u == 3u)
            {
                _props.SetFrame(center, (int)((seed >> 8) & 3u));
                var planterBase = new Vector3(shoulder, CurbY, shoulder);
                TreeMeshFactory.AddStreetTree(_props, planterBase, 0.50f, seed);
                }
        }

        private void AddCrosswalk(float lane, float half)
        {
            float near = Mathf.Lerp(lane, half, 0.32f);
            float far = Mathf.Lerp(lane, half, 0.77f);
            float stripe = lane * 0.17f;
            for (int i = -2; i <= 2; i++)
            {
                float x = i * lane * 0.34f;
                Rect(_surface, x - stripe * 0.5f, near, x + stripe * 0.5f, far, PaintY, Paint);
            }
        }

        private void AddCurb(float x0, float z0, float x1, float z1, Vector3 outward)
        {
            _surface.AddQuad(new Vector3(x0, RoadY, z0), new Vector3(x1, RoadY, z1),
                new Vector3(x1, CurbY, z1), new Vector3(x0, CurbY, z0), outward, Curb);
        }

        private void AddStreetlight(float offset)
        {
            var metal = new Color32(52, 72, 79, 255);
            _props.AddBox(new Vector3(offset, 0.17f, offset), new Vector3(0.19f, 0.14f, 0.19f), Curb, Paving);
            _props.AddBox(new Vector3(offset, 1.10f, offset), new Vector3(0.08f, 1.84f, 0.08f), metal, metal);
            _props.AddBox(new Vector3(offset - 0.18f, 2.04f, offset), new Vector3(0.43f, 0.09f, 0.08f), metal, metal);
            _props.AddBox(new Vector3(offset - 0.39f, 2.02f, offset), new Vector3(0.24f, 0.10f, 0.16f), Paint, metal);
        }

        private void AddLot(CellType type, int x, int z, bool full)
        {
            float half = _cellSize * 0.5f;
            float grass = WorldTerrain.Vegetation(x, z);
            Rect(_surface, -half, -half, half, half, 0.007f, Color32.Lerp(GrassDark, GrassLight, grass));

            if (type != CellType.Empty)
            {
                float plotWidth = _cellSize * 0.95f;
                Color32 plot = type switch
                {
                    CellType.Residential => new Color32(105, 153, 106, 255),
                    CellType.Commercial => new Color32(126, 151, 161, 255),
                    CellType.Industrial => new Color32(163, 146, 113, 255),
                    CellType.School => new Color32(136, 151, 116, 255),
                    CellType.CivicService => new Color32(155, 159, 158, 255),
                    _ => new Color32(146, 145, 144, 255)
                };
                _surface.AddBox(new Vector3(0, BuildingBase * 0.5f, 0),
                    new Vector3(plotWidth, BuildingBase, plotWidth), Curb, plot);
                return;
            }

            if (!full) return;

            uint seed = BuildingMeshFactory.Hash(x, z, 921);
            float grove = WorldTerrain.Vegetation(x, z);
            float threshold = _city.Roads.HasRoadAccess(x, z) ? 0.53f : 0.39f;
            if (grove > threshold && seed % 5u < 3u)
            {
                float dx = ((seed >> 8 & 255u) / 255f - 0.5f) * half * 0.85f;
                float dz = ((seed >> 16 & 255u) / 255f - 0.5f) * half * 0.85f;
                float size = 0.78f + (seed >> 24 & 255u) / 255f * 0.44f;
                TreeMeshFactory.AddWildTree(_props, new Vector3(dx, 0.008f, dz), size, seed, grove);
                if (grove > 0.62f && seed % 3u == 0)
                {
                    float sdx = dx * -0.55f;
                    float sdz = dz * -0.55f + (dz > 0 ? -0.67f : 0.67f);
                    TreeMeshFactory.AddWildTree(_props, new Vector3(sdx, 0.008f, sdz), size * 0.64f, seed ^ 0x9E3779B9u, grove + 0.12f);
                }
            }
            else if (seed % 19u == 0)
            {
                var rock = new Color32(138, 149, 132, 255);
                _props.AddCylinder(new Vector3(0.37f, 0.01f, -0.44f), 0.34f, 0.22f, 0.22f, 5, rock, true, Paving);
            }
        }

        private static void Rect(MeshBuilder mesh, float x0, float z0, float x1, float z1, float y, Color32 color)
        {
            mesh.AddQuad(new Vector3(x0, y, z0), new Vector3(x0, y, z1),
                new Vector3(x1, y, z1), new Vector3(x1, y, z0), Vector3.up, color);
        }

        public void Dispose()
        {
            _city.CellChanged -= MarkDirty;
            foreach (Mesh mesh in _surfaces)
                if (mesh != null) UnityEngine.Object.Destroy(mesh);
            foreach (Mesh mesh in _scenery)
                if (mesh != null) UnityEngine.Object.Destroy(mesh);
            UnityEngine.Object.Destroy(_root.gameObject);
        }
    }
}
