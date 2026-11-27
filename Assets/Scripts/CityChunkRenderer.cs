using System;
using System.Collections.Generic;
using UnityEngine;

namespace XCity
{
    internal sealed class CityChunkRenderer : IDisposable
    {
        private const int ChunkSize = 8;
        private const int ChunkCount = CitySimulation.GridSize / ChunkSize;
        private readonly HashSet<Vector2Int> _dirty = new HashSet<Vector2Int>();
        private readonly Mesh[,,] _meshes;
        private readonly List<CombineInstance> _instances = new List<CombineInstance>(ChunkSize * ChunkSize);
        private readonly CitySimulation _city;
        private readonly Mesh _cube;
        private readonly Material[] _materials;
        private readonly Transform _root;
        private readonly float _cellSize;

        public CityChunkRenderer(CitySimulation city, float cellSize, Material[] materials, Transform parent)
        {
            _city = city;
            _cube = CreateCubeMesh();
            _cellSize = cellSize;
            _materials = materials;
            _meshes = new Mesh[ChunkCount, ChunkCount, materials.Length];
            _root = new GameObject("CityChunks").transform;
            _root.SetParent(parent, false);
            _city.CellChanged += MarkDirty;
            for (int x = 0; x < CitySimulation.GridSize; x++)
                for (int z = 0; z < CitySimulation.GridSize; z++)
                    if (_city.GetCell(x, z).Type != CellType.Empty) MarkDirty(x, z);
        }

        private static Mesh CreateCubeMesh()
        {
            var normals = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            var vertices = new List<Vector3>(24);
            var vertexNormals = new List<Vector3>(24);
            var triangles = new List<int>(36);
            foreach (Vector3 normal in normals)
            {
                Vector3 tangent = Vector3.Cross(Mathf.Abs(normal.y) > 0.5f ? Vector3.forward : Vector3.up, normal);
                Vector3 bitangent = Vector3.Cross(normal, tangent);
                Vector3 center = normal * 0.5f;
                int index = vertices.Count;
                vertices.Add(center - tangent * 0.5f - bitangent * 0.5f);
                vertices.Add(center + tangent * 0.5f - bitangent * 0.5f);
                vertices.Add(center + tangent * 0.5f + bitangent * 0.5f);
                vertices.Add(center - tangent * 0.5f + bitangent * 0.5f);
                for (int vertex = 0; vertex < 4; vertex++) vertexNormals.Add(normal);
                triangles.AddRange(new[] { index, index + 1, index + 2, index, index + 2, index + 3 });
            }

            var mesh = new Mesh { name = "CityUnitCube" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(vertexNormals);
            mesh.SetTriangles(triangles, 0);
            return mesh;
        }

        private void MarkDirty(int x, int z) => _dirty.Add(new Vector2Int(x / ChunkSize, z / ChunkSize));

        public void RebuildDirtyChunks()
        {
            foreach (Vector2Int chunk in _dirty)
                for (int material = 0; material < _materials.Length; material++)
                    RebuildChunk(chunk, material);
            _dirty.Clear();
        }

        private void RebuildChunk(Vector2Int chunk, int material)
        {
            _instances.Clear();
            int startX = chunk.x * ChunkSize;
            int startZ = chunk.y * ChunkSize;
            for (int x = startX; x < startX + ChunkSize; x++)
            {
                for (int z = startZ; z < startZ + ChunkSize; z++)
                {
                    CityCell cell = _city.GetCell(x, z);
                    if (cell.Type == CellType.Empty) continue;
                    bool tile = material == (int)cell.Type - 1;
                    bool building = cell.Level > 0 && material == (int)cell.Type + 2;
                    if (!tile && !building) continue;

                    float height = tile ? 0.1f : cell.Height;
                    float elevation = tile ? 0f : 0.1f;
                    float width = _cellSize * (tile ? 0.96f : 0.72f);
                    var position = new Vector3((x - startX + 0.5f) * _cellSize,
                        elevation + height * 0.5f, (z - startZ + 0.5f) * _cellSize);
                    _instances.Add(new CombineInstance
                    {
                        mesh = _cube,
                        transform = Matrix4x4.TRS(position, Quaternion.identity, new Vector3(width, height, width))
                    });
                }
            }

            Mesh mesh = _meshes[chunk.x, chunk.y, material];
            if (mesh != null) mesh.Clear();
            if (_instances.Count == 0) return;

            if (mesh == null)
            {
                mesh = new Mesh { name = "CityChunkMesh" };
                _meshes[chunk.x, chunk.y, material] = mesh;
                var layer = new GameObject($"Chunk_{chunk.x}_{chunk.y}_{material}");
                layer.transform.SetParent(_root, false);
                layer.transform.localPosition = new Vector3(startX * _cellSize, 0f, startZ * _cellSize);
                layer.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = layer.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _materials[material];
                if (material < 4) renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            mesh.CombineMeshes(_instances.ToArray());
        }

        public void Dispose()
        {
            _city.CellChanged -= MarkDirty;
            foreach (Mesh mesh in _meshes)
                if (mesh != null) UnityEngine.Object.Destroy(mesh);
            UnityEngine.Object.Destroy(_cube);
            UnityEngine.Object.Destroy(_root.gameObject);
        }
    }
}
