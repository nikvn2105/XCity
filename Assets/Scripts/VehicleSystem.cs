using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace XCity
{
    internal sealed class VehicleSystem : IDisposable
    {
        private const int MaxVehicles = 76;
        private const float RecountInterval = 1.0f;
        private const float SpawnInterval = 0.20f;
        private static readonly Color32 StrobeDim = new Color32(42, 34, 42, 255);

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

        private struct RoadCandidate
        {
            public Vector2Int Cell;
            public VehicleCategory District;
        }

        private sealed class Vehicle
        {
            public Vector2Int Cell;
            public int InDir;
            public int OutDir;
            public float T;
            public float Speed;
            public float CurrentSpeed;
            public float WheelAngle;
            public float Pitch;
            public float Roll;
            public float TrailerYaw;
            public float Phase;
            public VehicleTemplate Template;
        }

        private readonly CitySimulation _city;
        private readonly float _cellSize;
        private readonly System.Random _rng = new System.Random(1234);
        private readonly List<Vehicle> _vehicles = new List<Vehicle>(MaxVehicles);
        private readonly List<RoadCandidate> _candidates = new List<RoadCandidate>(512);
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
        private float _animTime;
        private int _lastStrobeStep = -1;
        private int _targetCount;
        private bool _topologyDirty = true;
        private bool _hasEmergencyVehicles;
        private WorldView _view;

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
            renderer.shadowCastingMode = ShadowCastingMode.On;
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

        public void SetView(WorldView view)
        {
            if (_view.SameAnchor(view)) return;
            _view = view;
            _recountTimer = RecountInterval;
            for (int i = _vehicles.Count - 1; i >= 0; i--)
            {
                var cell = _vehicles[i].Cell;
                if (view.Contains(cell.x, cell.y)) continue;
                _vehicles.RemoveAt(i);
                _topologyDirty = true;
            }
        }

        public void SetVisible(bool visible)
        {
            if (_go.activeSelf != visible) _go.SetActive(visible);
        }

        public void Update(float dt)
        {
            if (dt <= 0f) return;
            _animTime += dt;

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
                float desiredSpeed = v.InDir != v.OutDir ? v.Speed * 0.78f : v.Speed;

                Vector2Int nextCell = v.Cell + Dir2D[v.OutDir];
                for (int j = 0; j < _vehicles.Count; j++)
                {
                    if (j == i) continue;
                    Vehicle other = _vehicles[j];
                    float minGapSame = Mathf.Clamp((v.Template.Length + other.Template.Length) * 0.5f / _cellSize + 0.14f, 0.42f, 0.96f);
                    if (other.Cell == v.Cell)
                    {
                        if (other.OutDir == v.OutDir && other.T > v.T)
                        {
                            float gap = other.T - v.T;
                            if (gap < minGapSame)
                                desiredSpeed = gap < minGapSame * 0.60f ? 0f : Mathf.Min(desiredSpeed, other.CurrentSpeed * 0.82f);
                        }
                        else if (other.OutDir != v.OutDir && other.InDir != (v.OutDir ^ 1) && v.T < 0.42f && other.T >= 0.25f && other.T <= 0.78f)
                        {
                            desiredSpeed = 0f;
                        }
                    }
                    else if (other.Cell == nextCell)
                    {
                        if (other.InDir == v.OutDir)
                        {
                            float gap = (1f - v.T) + other.T;
                            float minGapNext = minGapSame - 0.04f;
                            if (gap < minGapNext)
                                desiredSpeed = gap < minGapNext * 0.60f ? 0f : Mathf.Min(desiredSpeed, other.CurrentSpeed * 0.82f);
                        }
                        else if (other.InDir != (v.OutDir ^ 1) && (1f - v.T) < 0.38f && other.T >= 0.12f && other.T <= 0.88f)
                        {
                            desiredSpeed = 0f;
                        }
                    }
                }

                float prevSpeed = v.CurrentSpeed;
                v.CurrentSpeed = Mathf.MoveTowards(v.CurrentSpeed, desiredSpeed, 9f * dt);
                float accel = (v.CurrentSpeed - prevSpeed) / dt;

                ComputeCurve(v.Cell, v.InDir, v.OutDir, out Vector3 p0, out Vector3 p1, out Vector3 p2);
                float length = (Vector3.Distance(p1, p0) + Vector3.Distance(p2, p1) + Vector3.Distance(p2, p0)) * 0.5f;
                float distMoved = v.CurrentSpeed * dt;
                v.T += distMoved / Mathf.Max(length, 0.1f);
                v.WheelAngle = Mathf.Repeat(v.WheelAngle + (distMoved / Mathf.Max(0.08f, v.Template.WheelRadius)) * Mathf.Rad2Deg, 360f);

                int turnSign = GetTurnSign(v.InDir, v.OutDir);
                float turnWeight = Mathf.Sin(Mathf.Clamp01(v.T) * Mathf.PI);
                float targetRoll = v.Template.IsTwoWheeler
                    ? -turnSign * 14f * turnWeight
                    : turnSign * 3.0f * turnWeight;
                v.Roll = Mathf.MoveTowards(v.Roll, targetRoll, 45f * dt);

                float targetPitch = Mathf.Clamp(-accel * 0.45f, -3.0f, 3.0f);
                v.Pitch = Mathf.MoveTowards(v.Pitch, targetPitch, 22f * dt);

                float targetTrailerYaw = v.Template.HasTrailer ? -turnSign * 19f * turnWeight : 0f;
                v.TrailerYaw = Mathf.MoveTowards(v.TrailerYaw, targetTrailerYaw, 55f * dt);

                if (v.T >= 1f)
                {
                    v.Cell += Dir2D[v.OutDir];
                    v.InDir = v.OutDir;
                    v.T -= 1f;

                    if (!_view.Contains(v.Cell.x, v.Cell.y) || !IsRoad(v.Cell.x, v.Cell.y))
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

        private static int GetTurnSign(int inDir, int outDir)
        {
            if (inDir == outDir) return 0;
            if (outDir == (inDir ^ 1)) return -1;
            return Vector3.Dot(Right3D[inDir], Dir3D[outDir]) > 0f ? 1 : -1;
        }

        private void Recount()
        {
            _candidates.Clear();
            int developed = 0;

            for (int x = _view.MinX; x <= _view.MaxX; x++)
            {
                for (int z = _view.MinZ; z <= _view.MaxZ; z++)
                {
                    CityCell cell = _city.GetCell(x, z);
                    if (cell.Type == CellType.Road)
                    {
                        if (_city.Roads.GetConnections(x, z) != RoadConnections.None)
                            _candidates.Add(new RoadCandidate
                            {
                                Cell = new Vector2Int(x, z),
                                District = DetectDistrict(x, z)
                            });
                    }
                    else if (cell.Type != CellType.Empty && cell.Height > 0f)
                    {
                        developed++;
                    }
                }
            }

            _targetCount = _candidates.Count < 2
                ? 0
                : Mathf.Clamp(Mathf.Max(3, developed / 2 + _candidates.Count / 5), 0, MaxVehicles);
        }

        private VehicleCategory DetectDistrict(int x, int z)
        {
            VehicleCategory fallback = VehicleCategory.Any;
            for (int dx = -2; dx <= 2; dx++)
            {
                int nx = x + dx;
                if (nx < 0 || nx >= CitySimulation.GridSize) continue;
                for (int dz = -2; dz <= 2; dz++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int nz = z + dz;
                    if (nz < 0 || nz >= CitySimulation.GridSize) continue;
                    CityCell c = _city.GetCell(nx, nz);
                    if (c.Type == CellType.CivicService || c.Type == CellType.School)
                        return VehicleCategory.Civic;
                    if (c.Type == CellType.Industrial || c.Type == CellType.PowerPlant)
                        return VehicleCategory.Industrial;
                    if (c.Type == CellType.Commercial)
                        fallback = VehicleCategory.Commercial;
                    else if (c.Type == CellType.Residential && fallback == VehicleCategory.Any)
                        fallback = VehicleCategory.Residential;
                }
            }
            return fallback;
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
                RoadCandidate cand = _candidates[(start + offset) % _candidates.Count];
                Vector2Int cell = cand.Cell;
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
                VehicleTemplate tpl = VehicleMeshFactory.PickForDistrict(_rng, cand.District);
                _vehicles.Add(new Vehicle
                {
                    Cell = cell,
                    InDir = outDir,
                    OutDir = outDir,
                    T = 0.5f,
                    Speed = tpl.Speed,
                    CurrentSpeed = tpl.Speed,
                    Phase = (float)_rng.NextDouble() * 6.283f,
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
                if (!_view.Contains(v.Cell.x, v.Cell.y) || !IsRoad(v.Cell.x, v.Cell.y))
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
                    _dirScratch[count++] = d;
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
                _hasEmergencyVehicles = false;
                for (int i = 0; i < count; i++)
                {
                    VehicleTemplate tpl = _vehicles[i].Template;
                    totalVerts += tpl.Vertices.Length;
                    totalTris += tpl.Triangles.Length;
                    if (tpl.HasEmergencyLights) _hasEmergencyVehicles = true;
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
                _lastStrobeStep = -1;
            }

            int strobeStep = (int)(_animTime * 6f) & 1;
            bool updateColors = _topologyDirty || (_hasEmergencyVehicles && strobeStep != _lastStrobeStep);
            if (_hasEmergencyVehicles) _lastStrobeStep = strobeStep;

            int vertCursor = 0;
            for (int i = 0; i < count; i++)
            {
                Vehicle v = _vehicles[i];
                VehicleTemplate tpl = v.Template;
                ComputeCurve(v.Cell, v.InDir, v.OutDir, out Vector3 p0, out Vector3 p1, out Vector3 p2);

                float u = 1f - v.T;
                Vector3 pos = u * u * p0 + 2f * u * v.T * p1 + v.T * v.T * p2;
                Vector3 tangent = 2f * u * (p1 - p0) + 2f * v.T * (p2 - p1);
                if (tangent.sqrMagnitude < 1e-6f) tangent = p2 - p0;
                Quaternion pathRot = tangent.sqrMagnitude > 1e-6f
                    ? Quaternion.LookRotation(tangent.normalized, Vector3.up)
                    : Quaternion.identity;

                Quaternion bodyRot = pathRot * Quaternion.Euler(v.Pitch, 0f, v.Roll);
                float bobY = Mathf.Sin(_animTime * 15f + v.Phase) * (0.008f + 0.006f * (v.CurrentSpeed / 6f));
                Vector3 bodyOffset = new Vector3(0f, bobY, 0f);

                int turnSign = GetTurnSign(v.InDir, v.OutDir);
                float steerDeg = turnSign * 22f * Mathf.Sin(Mathf.Clamp01(v.T) * Mathf.PI);
                Quaternion spinRot = Quaternion.Euler(v.WheelAngle, 0f, 0f);
                Quaternion wheelRotFront = pathRot * Quaternion.Euler(0f, steerDeg, 0f) * spinRot;
                Quaternion wheelRotRear = pathRot * spinRot;

                Vector3[] srcV = tpl.Vertices;
                Vector3[] srcN = tpl.Normals;
                byte[] partIds = tpl.PartIds;
                Vector3[] pivots = tpl.PartPivots;
                bool pulseStrobes = updateColors && tpl.HasEmergencyLights;

                Vector3 kingpinLocal = tpl.HasTrailer ? pivots[VehicleMeshFactory.PartTrailer] : Vector3.zero;
                Vector3 worldKingpin = tpl.HasTrailer ? pos + pathRot * kingpinLocal : Vector3.zero;
                Quaternion trailerPathRot = tpl.HasTrailer ? pathRot * Quaternion.Euler(0f, v.TrailerYaw, 0f) : Quaternion.identity;
                Quaternion trailerBodyRot = tpl.HasTrailer ? trailerPathRot * Quaternion.Euler(v.Pitch * 0.4f, 0f, v.Roll * 0.6f) : Quaternion.identity;
                Quaternion trailerWheelRot = tpl.HasTrailer ? trailerPathRot * spinRot : Quaternion.identity;

                for (int k = 0; k < srcV.Length; k++)
                {
                    byte partId = partIds[k];
                    if ((partId >= VehicleMeshFactory.PartWheelFL && partId <= VehicleMeshFactory.PartWheelRR) ||
                        partId == VehicleMeshFactory.PartWheelML || partId == VehicleMeshFactory.PartWheelMR)
                    {
                        Vector3 pivot = pivots[partId];
                        Quaternion wRot = partId <= VehicleMeshFactory.PartWheelFR ? wheelRotFront : wheelRotRear;
                        Vector3 worldPivot = pos + pathRot * pivot;
                        _renderVertices[vertCursor] = worldPivot + wRot * (srcV[k] - pivot);
                        _renderNormals[vertCursor] = wRot * srcN[k];
                    }
                    else if (partId == VehicleMeshFactory.PartTrailer)
                    {
                        _renderVertices[vertCursor] = worldKingpin + bodyOffset + trailerBodyRot * (srcV[k] - kingpinLocal);
                        _renderNormals[vertCursor] = trailerBodyRot * srcN[k];
                    }
                    else if (partId >= VehicleMeshFactory.PartTrailerWheelFL && partId <= VehicleMeshFactory.PartTrailerWheelRR)
                    {
                        Vector3 pivot = pivots[partId];
                        Vector3 worldPivot = worldKingpin + trailerPathRot * (pivot - kingpinLocal);
                        _renderVertices[vertCursor] = worldPivot + trailerWheelRot * (srcV[k] - pivot);
                        _renderNormals[vertCursor] = trailerWheelRot * srcN[k];
                    }
                    else
                    {
                        _renderVertices[vertCursor] = pos + bodyOffset + bodyRot * srcV[k];
                        _renderNormals[vertCursor] = bodyRot * srcN[k];

                        if (pulseStrobes)
                        {
                            if (partId == VehicleMeshFactory.PartStrobeL)
                                _renderColors[vertCursor] = strobeStep == 0 ? tpl.StrobeLeftColor : StrobeDim;
                            else if (partId == VehicleMeshFactory.PartStrobeR)
                                _renderColors[vertCursor] = strobeStep == 1 ? tpl.StrobeRightColor : StrobeDim;
                        }
                    }
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
                if (updateColors) _mesh.colors32 = _renderColors;
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
