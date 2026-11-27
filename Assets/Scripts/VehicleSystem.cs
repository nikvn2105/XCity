using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace XCity
{
    internal sealed class VehicleSystem : IDisposable
    {
        private const int MaxVehicles = 40;
        private const float RecountInterval = 1.0f;
        private const float SpawnInterval = 0.3f;

        // Direction indices: 0=W(-1,0), 1=E(+1,0), 2=S(0,-1), 3=N(0,+1)
        private static readonly Vector2Int[] Dir2D =
        {
            new Vector2Int(-1, 0),
            new Vector2Int(1, 0),
            new Vector2Int(0, -1),
            new Vector2Int(0, 1)
        };

        private static readonly Vector3[] Dir3D =
        {
            new Vector3(-1f, 0f, 0f),
            new Vector3(1f, 0f, 0f),
            new Vector3(0f, 0f, -1f),
            new Vector3(0f, 0f, 1f)
        };

        private static readonly Vector3[] Right3D =
        {
            new Vector3(0f, 0f, 1f),
            new Vector3(0f, 0f, -1f),
            new Vector3(-1f, 0f, 0f),
            new Vector3(1f, 0f, 0f)
        };

        private sealed class Vehicle
        {
            public Vector2Int Cell;
            public int InDir;
            public int OutDir;
            public float T;
            public float Speed;
            public VehicleTemplate Template;
        }

        private readonly CitySimulation _city;
        private readonly float _cellSize;
        private readonly System.Random _rng = new System.Random(1234);
        private readonly List<Vehicle> _vehicles = new List<Vehicle>(MaxVehicles);
        private readonly List<Vector2Int> _candidates = new List<Vector2Int>(256);
        private readonly HashSet<Vector2Int> _occupied = new HashSet<Vector2Int>();
        private readonly int[] _dirScratch = new int[8];

        private readonly GameObject _go;
        private readonly Mesh _mesh;
        private readonly Bounds _mapBounds;

        private Vector3[] _renderVertices = Array.Empty<Vector3>();
        private Vector3[] _renderNormals = Array.Empty<Vector3>();
        private Color32[] _renderColors = Array.Empty<Color32>();
        private int[] _renderTriangles = Array.Empty<int>();

        private int _lastRoadVersion = -1;
        private float _recountTimer = RecountInterval;
        private float _spawnTimer;
        private int _targetCount;
        private bool _topologyDirty = true;

        public VehicleSystem(CitySimulation city, float cellSize, Material material, Transform parent)
        {
            _city = city;
            _cellSize = cellSize;

            float span = CitySimulation.GridSize * cellSize;
            _mapBounds = new Bounds(new Vector3(span * 0.5f, 2.5f, span * 0.5f), new Vector3(span, 5f, span));

            _mesh = new Mesh { name = "CityVehicles" };
            _mesh.MarkDynamic();

            _go = new GameObject("Vehicles");
            _go.transform.SetParent(parent, false);
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            MeshRenderer renderer = _go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
        }

        public void Prewarm(float seconds)
        {
            Recount();
            for (int i = 0; i < _targetCount && _vehicles.Count < _targetCount; i++)
            {
                if (!TrySpawn()) break;
            }

            const float step = 0.1f;
            int steps = Mathf.CeilToInt(seconds / step);
            for (int i = 0; i < steps; i++)
                Update(step);
        }

        public void Update(float dt)
        {
            if (dt <= 0f) return;

            int roadVersion = _city.Roads.Version;
            if (roadVersion != _lastRoadVersion)
            {
                _lastRoadVersion = roadVersion;
                ValidateRoadVehicles();
                _recountTimer = RecountInterval;
            }

            _recountTimer += dt;
            if (_recountTimer >= RecountInterval)
            {
                _recountTimer = 0f;
                Recount();
            }

            while (_vehicles.Count > _targetCount)
            {
                _vehicles.RemoveAt(_vehicles.Count - 1);
                _topologyDirty = true;
            }

            if (_vehicles.Count < _targetCount)
            {
                _spawnTimer += dt;
                if (_spawnTimer >= SpawnInterval)
                {
                    _spawnTimer = 0f;
                    TrySpawn();
                }
            }

            for (int i = _vehicles.Count - 1; i >= 0; i--)
            {
                Vehicle v = _vehicles[i];
                ComputeCurve(v.Cell, v.InDir, v.OutDir, out Vector3 p0, out Vector3 p1, out Vector3 p2);
                float length = (Vector3.Distance(p1, p0) + Vector3.Distance(p2, p1) + Vector3.Distance(p2, p0)) * 0.5f;
                v.T += v.Speed * dt / Mathf.Max(length, 0.1f);

                if (v.T >= 1f)
                {
                    v.Cell += Dir2D[v.OutDir];
                    v.InDir = v.OutDir;
                    v.T -= 1f;

                    if (!IsRoad(v.Cell.x, v.Cell.y))
                    {
                        _vehicles.RemoveAt(i);
                        _topologyDirty = true;
                        continue;
                    }

                    v.OutDir = ChooseOutDir(v.Cell.x, v.Cell.y, v.InDir);
                }
            }

            RenderVehicles();
        }

        private void Recount()
        {
            _candidates.Clear();
            int developed = 0;

            for (int x = 0; x < CitySimulation.GridSize; x++)
            {
                for (int z = 0; z < CitySimulation.GridSize; z++)
                {
                    CityCell cell = _city.GetCell(x, z);
                    if (cell.Type == CellType.Road)
                    {
                        if (_city.Roads.GetConnections(x, z) != RoadConnections.None)
                            _candidates.Add(new Vector2Int(x, z));
                    }
                    else if (cell.Type >= CellType.Residential && cell.Type <= CellType.Industrial && cell.Level > 0)
                    {
                        developed++;
                    }
                }
            }

            _targetCount = _candidates.Count < 2
                ? 0
                : Mathf.Clamp(developed / 3 + _candidates.Count / 12, 0, MaxVehicles);
        }

        private bool TrySpawn()
        {
            if (_candidates.Count == 0 || _vehicles.Count >= MaxVehicles) return false;

            _occupied.Clear();
            for (int i = 0; i < _vehicles.Count; i++)
                _occupied.Add(_vehicles[i].Cell);

            int start = _rng.Next(_candidates.Count);
            for (int offset = 0; offset < _candidates.Count; offset++)
            {
                Vector2Int cell = _candidates[(start + offset) % _candidates.Count];
                if (_occupied.Contains(cell)) continue;

                RoadConnections conns = _city.Roads.GetConnections(cell.x, cell.y);
                int count = 0;
                for (int d = 0; d < 4; d++)
                {
                    if (((int)conns & (1 << d)) != 0)
                        _dirScratch[count++] = d;
                }
                if (count == 0) continue;

                int outDir = _dirScratch[_rng.Next(count)];
                VehicleTemplate tpl = VehicleMeshFactory.PickRandom(_rng);
                _vehicles.Add(new Vehicle
                {
                    Cell = cell,
                    InDir = outDir,
                    OutDir = outDir,
                    T = 0.5f,
                    Speed = tpl.Speed,
                    Template = tpl
                });
                _topologyDirty = true;
                return true;
            }

            return false;
        }

        private void ValidateRoadVehicles()
        {
            for (int i = _vehicles.Count - 1; i >= 0; i--)
            {
                Vehicle v = _vehicles[i];
                if (!IsRoad(v.Cell.x, v.Cell.y))
                {
                    _vehicles.RemoveAt(i);
                    _topologyDirty = true;
                    continue;
                }

                RoadConnections conns = _city.Roads.GetConnections(v.Cell.x, v.Cell.y);
                if (((int)conns & (1 << v.OutDir)) == 0)
                    v.OutDir = ChooseOutDir(v.Cell.x, v.Cell.y, v.InDir);
            }
        }

        private int ChooseOutDir(int x, int z, int inDir)
        {
            RoadConnections conns = _city.Roads.GetConnections(x, z);
            int reverse = inDir ^ 1;
            int count = 0;

            for (int d = 0; d < 4; d++)
            {
                if (d == reverse) continue;
                if (((int)conns & (1 << d)) == 0) continue;

                _dirScratch[count++] = d;
                if (d == inDir)
                    _dirScratch[count++] = d; // weight straight x2
            }

            return count == 0 ? reverse : _dirScratch[_rng.Next(count)];
        }

        private void ComputeCurve(Vector2Int cell, int inDir, int outDir, out Vector3 p0, out Vector3 p1, out Vector3 p2)
        {
            float half = _cellSize * 0.5f;
            float offset = 0.17f * _cellSize;
            Vector3 center = new Vector3((cell.x + 0.5f) * _cellSize, 0.05f, (cell.y + 0.5f) * _cellSize);

            p0 = center - Dir3D[inDir] * half + Right3D[inDir] * offset;
            p2 = center + Dir3D[outDir] * half + Right3D[outDir] * offset;

            if (inDir == outDir)
                p1 = (p0 + p2) * 0.5f;
            else if (outDir == (inDir ^ 1))
                p1 = center;
            else
                p1 = center + Right3D[inDir] * offset + Right3D[outDir] * offset;
        }

        private void RenderVehicles()
        {
            int count = _vehicles.Count;
            if (count == 0)
            {
                if (_topologyDirty)
                {
                    _mesh.Clear();
                    _topologyDirty = false;
                }
                return;
            }

            if (_topologyDirty)
            {
                int totalVerts = 0;
                int totalTris = 0;
                for (int i = 0; i < count; i++)
                {
                    VehicleTemplate tpl = _vehicles[i].Template;
                    totalVerts += tpl.Vertices.Length;
                    totalTris += tpl.Triangles.Length;
                }

                if (_renderVertices.Length != totalVerts)
                {
                    _renderVertices = new Vector3[totalVerts];
                    _renderNormals = new Vector3[totalVerts];
                    _renderColors = new Color32[totalVerts];
                }
                if (_renderTriangles.Length != totalTris)
                    _renderTriangles = new int[totalTris];

                int vOffset = 0;
                int tOffset = 0;
                for (int i = 0; i < count; i++)
                {
                    VehicleTemplate tpl = _vehicles[i].Template;
                    Array.Copy(tpl.Colors, 0, _renderColors, vOffset, tpl.Colors.Length);
                    int[] srcTris = tpl.Triangles;
                    for (int t = 0; t < srcTris.Length; t++)
                        _renderTriangles[tOffset + t] = srcTris[t] + vOffset;

                    vOffset += tpl.Vertices.Length;
                    tOffset += srcTris.Length;
                }
            }

            int vertCursor = 0;
            for (int i = 0; i < count; i++)
            {
                Vehicle v = _vehicles[i];
                ComputeCurve(v.Cell, v.InDir, v.OutDir, out Vector3 p0, out Vector3 p1, out Vector3 p2);

                float u = 1f - v.T;
                Vector3 pos = u * u * p0 + 2f * u * v.T * p1 + v.T * v.T * p2;
                Vector3 tangent = 2f * u * (p1 - p0) + 2f * v.T * (p2 - p1);
                if (tangent.sqrMagnitude < 1e-6f) tangent = p2 - p0;
                Quaternion rot = tangent.sqrMagnitude > 1e-6f
                    ? Quaternion.LookRotation(tangent.normalized, Vector3.up)
                    : Quaternion.identity;

                Vector3[] srcV = v.Template.Vertices;
                Vector3[] srcN = v.Template.Normals;
                for (int k = 0; k < srcV.Length; k++)
                {
                    _renderVertices[vertCursor] = rot * srcV[k] + pos;
                    _renderNormals[vertCursor] = rot * srcN[k];
                    vertCursor++;
                }
            }

            if (_topologyDirty)
            {
                _mesh.Clear();
                _mesh.vertices = _renderVertices;
                _mesh.normals = _renderNormals;
                _mesh.colors32 = _renderColors;
                _mesh.triangles = _renderTriangles;
                _mesh.bounds = _mapBounds;
                _topologyDirty = false;
            }
            else
            {
                _mesh.vertices = _renderVertices;
                _mesh.normals = _renderNormals;
                _mesh.bounds = _mapBounds;
            }
        }

        private bool IsRoad(int x, int z) =>
            x >= 0 && z >= 0 && x < CitySimulation.GridSize && z < CitySimulation.GridSize &&
            _city.GetCell(x, z).Type == CellType.Road;

        public void Dispose()
        {
            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
            if (_go != null) UnityEngine.Object.Destroy(_go);
        }
    }
}
