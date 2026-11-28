using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace XCity
{
    internal sealed class VehicleSystem : IDisposable
    {
        private const float DeadlockSeconds = 4f;
        private const float StoppedSpeed = 0.05f;
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
            public Vehicle BlockedBy;
            public float WaitSeconds;
            public float WheelAngle;
            public float Pitch;
            public float Roll;
            public float TrailerYaw;
            public float Phase;
            public VehicleTemplate Template;
            public List<int> Route; // directions to the destination; null while wandering
            public int RouteStep;
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
        private CityEmergencySystem _emergency;

        public int ActiveCount => _vehicles.Count;

        public void SetEmergencySystem(CityEmergencySystem emergency) => _emergency = emergency;

        public bool TryPickVehicle(Ray ray, out int index, out float nearest)
        {
            index = -1;
            nearest = float.PositiveInfinity;
            for (int i = 0; i < _vehicles.Count; i++)
            {
                Vector3 pos = ComputeWorldPos(_vehicles[i]);
                float r = Mathf.Max(0.8f, _vehicles[i].Template.Length * 0.55f);
                var bounds = new Bounds(pos + Vector3.up * 0.45f, new Vector3(r * 1.4f, 1.3f, r * 1.4f));
                if (bounds.IntersectRay(ray, out float dist) && dist < nearest)
                {
                    nearest = dist;
                    index = i;
                }
            }
            return index >= 0;
        }

        public bool TryGetVehicleFocus(int index, out Vector3 worldPos, out string displayName)
        {
            if (index >= 0 && index < _vehicles.Count)
            {
                worldPos = ComputeWorldPos(_vehicles[index]);
                displayName = _vehicles[index].Template.DisplayName;
                return true;
            }
            worldPos = default;
            displayName = null;
            return false;
        }

        private Vector3 ComputeWorldPos(Vehicle v)
        {
            ComputeCurve(v, out Vector3 p0, out Vector3 p1, out Vector3 p2);
            float u = 1f - v.T;
            Vector3 pos = u * u * p0 + 2f * u * v.T * p1 + v.T * v.T * p2;
            Vector3 tangent = 2f * u * (p1 - p0) + 2f * v.T * (p2 - p1);
            CityChunkRenderer.ApplyRoadGrade(_city, v.Cell, _cellSize, ref pos, ref tangent);
            return pos;
        }

        public VehicleSystem(CitySimulation city, float cellSize, Material material, Transform parent)
        {
            _city = city;
            _cellSize = cellSize;

            float span = CitySimulation.GridSize * cellSize;
            _mapBounds = new Bounds(new Vector3(span * 0.5f, (5f + BridgeProfile.MaxRise + WorldTerrain.MaxSurfaceY) * 0.5f, span * 0.5f), new Vector3(span, 5f + BridgeProfile.MaxRise + WorldTerrain.MaxSurfaceY, span));

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
                RemoveVehicleAt(i);
            }
        }

        public void SetVisible(bool visible)
        {
            if (_go.activeSelf != visible) _go.SetActive(visible);
        }

        private int _renderedTerrainVersion;

        public void Update(float dt)
        {
            bool terrainChanged = _renderedTerrainVersion != _city.TerrainVersion;
            _renderedTerrainVersion = _city.TerrainVersion;
            if (dt <= 0f)
            {
                if (terrainChanged) RenderVehicles();
                return;
            }
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
                RemoveVehicleAt(_vehicles.Count - 1);
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
                bool fireActive = _emergency != null && _emergency.HasActiveFire;
                bool responding = fireActive && v.Template.IsFireTruck;
                float desiredSpeed = v.InDir != v.OutDir ? v.Speed * 0.78f : v.Speed;
                if (responding)
                {
                    int fireDist = Mathf.Abs(v.Cell.x - _emergency.FireCell.x) + Mathf.Abs(v.Cell.y - _emergency.FireCell.y);
                    if (fireDist <= 2)
                    {
                        desiredSpeed *= 0.45f;
                        _emergency.NotifyResponderNear(ComputeWorldPos(v));
                    }
                    else
                    {
                        desiredSpeed *= 1.32f;
                    }
                }

                Vehicle blocker = null;
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
                                LimitSpeed(gap < minGapSame * 0.60f ? 0f : other.CurrentSpeed * 0.82f, other);
                        }
                        else if (VehicleLaneRules.BlocksAcrossCell(v.OutDir, other.InDir, other.OutDir, v.T, other.T))
                        {
                            LimitSpeed(0f, other);
                        }
                    }
                    else if (other.Cell == nextCell)
                    {
                        if (other.InDir == v.OutDir)
                        {
                            float gap = (1f - v.T) + other.T;
                            float minGapNext = minGapSame - 0.04f;
                            if (gap < minGapNext)
                                LimitSpeed(gap < minGapNext * 0.60f ? 0f : other.CurrentSpeed * 0.82f, other);
                        }
                        else if (VehicleLaneRules.BlocksEntry(v.OutDir, other.InDir, other.OutDir, v.T, other.T,
                            (int)_city.Roads.GetConnections(nextCell.x, nextCell.y)))
                        {
                            LimitSpeed(0f, other);
                        }
                    }
                }

                float prevSpeed = v.CurrentSpeed;
                v.CurrentSpeed = Mathf.MoveTowards(v.CurrentSpeed, desiredSpeed, 9f * dt);
                v.BlockedBy = blocker;
                v.WaitSeconds = blocker != null && v.CurrentSpeed <= StoppedSpeed ? v.WaitSeconds + dt : 0f;
                if (v.WaitSeconds >= DeadlockSeconds && IsWaitingInCycle(v))
                {
                    // shortcut: clear one vehicle in a closed wait cycle; normal spawning fills the slot.
                    // Never force a car through a blocker or remove traffic that's merely queued.
                    RemoveVehicleAt(i);
                    continue;
                }
                float accel = (v.CurrentSpeed - prevSpeed) / dt;

                void LimitSpeed(float limit, Vehicle other)
                {
                    if (limit >= desiredSpeed) return;
                    desiredSpeed = limit;
                    blocker = other;
                }

                ComputeCurve(v, out Vector3 p0, out Vector3 p1, out Vector3 p2);
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
                        RemoveVehicleAt(i);
                        continue;
                    }

                    // A fire truck answering a call drops its trip and steers toward the fire instead.
                    if (v.Template.IsFireTruck && _emergency != null && _emergency.HasActiveFire) v.Route = null;
                    if (v.Route != null && ++v.RouteStep >= v.Route.Count)
                    {
                        // Arrived: the car parks at its destination and frees a slot for a new trip.
                        RemoveVehicleAt(i);
                        continue;
                    }
                    v.OutDir = v.Route != null ? v.Route[v.RouteStep] : ChooseOutDir(v.Cell.x, v.Cell.y, v.InDir, v.Template.IsFireTruck);
                    if (v.OutDir < 0) RemoveVehicleAt(i);
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

        private void RemoveVehicleAt(int index)
        {
            Vehicle vehicle = _vehicles[index];
            vehicle.BlockedBy = null;
            vehicle.WaitSeconds = 0f;
            _vehicles.RemoveAt(index);
            _topologyDirty = true;
        }

        private static bool IsWaitingInCycle(Vehicle start)
        {
            Vehicle current = start.BlockedBy;
            for (int i = 0; i < MaxVehicles && current != null && current.WaitSeconds > 0f; i++)
            {
                if (current == start) return true;
                current = current.BlockedBy;
            }
            return false;
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

                RoadConnections conns = _city.Roads.GetVehicleExits(cell.x, cell.y);
                int count = 0;
                for (int d = 0; d < 4; d++)
                {
                    if (((int)conns & (1 << d)) != 0)
                        _dirScratch[count++] = d;
                }
                if (count == 0) continue;

                List<int> route = PlanTrip(cell);
                int outDir = route != null ? route[0] : _dirScratch[_rng.Next(count)];
                VehicleTemplate tpl = VehicleMeshFactory.PickForDistrict(_rng, cand.District);
                _vehicles.Add(new Vehicle
                {
                    Route = route,
                    Cell = cell,
                    InDir = SpawnHeading(cell, outDir),
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

        // Commute from the spawn road to a road beside jobs (shops, industry, services) when one is reachable.
        private List<int> PlanTrip(Vector2Int origin)
        {
            Vector2Int goal = origin;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                RoadCandidate cand = _candidates[_rng.Next(_candidates.Count)];
                if (cand.Cell == origin) continue;
                goal = cand.Cell;
                if (cand.District != VehicleCategory.Residential && cand.District != VehicleCategory.Any) break;
            }
            var route = new List<int>();
            return goal != origin && _city.Roads.TryFindPath(origin.x, origin.y, goal.x, goal.y, route) && route.Count > 0
                ? route : null;
        }

        private void ValidateRoadVehicles()
        {
            for (int i = _vehicles.Count - 1; i >= 0; i--)
            {
                Vehicle v = _vehicles[i];
                if (!_view.Contains(v.Cell.x, v.Cell.y) || !IsRoad(v.Cell.x, v.Cell.y))
                {
                    RemoveVehicleAt(i);
                    continue;
                }

                RoadConnections conns = _city.Roads.GetVehicleExits(v.Cell.x, v.Cell.y);
                v.BlockedBy = null;
                v.WaitSeconds = 0f;
                v.Route = null; // the road network changed, so the planned route may be cut; wander instead
                if (((int)conns & (1 << v.OutDir)) == 0)
                    v.OutDir = ChooseOutDir(v.Cell.x, v.Cell.y, v.InDir);
                if (v.OutDir < 0) RemoveVehicleAt(i);
            }
        }

        private int ChooseOutDir(int x, int z, int inDir, bool seekFire = false)
        {
            RoadConnections conns = _city.Roads.GetVehicleExits(x, z);
            int reverse = inDir ^ 1;
            int count = 0;

            if (seekFire && _emergency != null && _emergency.HasActiveFire)
            {
                Vector2Int target = _emergency.FireCell;
                int bestDir = -1;
                int bestDist = int.MaxValue;
                for (int d = 0; d < 4; d++)
                {
                    if (d == reverse) continue;
                    if (((int)conns & (1 << d)) == 0) continue;
                    Vector2Int next = new Vector2Int(x, z) + Dir2D[d];
                    int dist = Mathf.Abs(next.x - target.x) + Mathf.Abs(next.y - target.y);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestDir = d;
                    }
                }
                if (bestDir >= 0) return bestDir;
            }

            for (int d = 0; d < 4; d++)
            {
                if (d == reverse) continue;
                if (((int)conns & (1 << d)) == 0) continue;

                _dirScratch[count++] = d;
                if (d == inDir)
                    _dirScratch[count++] = d;
            }

            return count != 0 ? _dirScratch[_rng.Next(count)] : ((int)conns & (1 << reverse)) != 0 ? reverse : -1;
        }

        private int SpawnHeading(Vector2Int cell, int exit)
        {
            RoadConnections links = _city.Roads.GetConnections(cell.x, cell.y);
            int degree = 0;
            for (int direction = 0; direction < 4; direction++)
                if (((int)links & (1 << direction)) != 0) degree++;
            return VehicleLaneRules.SpawnHeading(exit, (int)links, degree);
        }

        private void ComputeCurve(Vehicle vehicle, out Vector3 p0, out Vector3 p1, out Vector3 p2)
        {
            Vector2Int cell = vehicle.Cell;
            int inDir = vehicle.InDir, outDir = vehicle.OutDir;
            float half = _cellSize * 0.5f;
            float enterOffset = 0.17f * _cellSize, exitOffset = enterOffset;
            if (_city.GetCell(cell.x, cell.y).RoadType == RoadType.Avenue)
            {
                float avenueOffset = (vehicle.Phase < Mathf.PI ? 0.10f : 0.29f) * _cellSize;
                if (IsAvenue(cell - Dir2D[inDir])) enterOffset = avenueOffset;
                if (IsAvenue(cell + Dir2D[outDir])) exitOffset = avenueOffset;
            }
            Vector3 center = new Vector3((cell.x + 0.5f) * _cellSize, 0.05f, (cell.y + 0.5f) * _cellSize);

            p0 = center - Dir3D[inDir] * half + Right3D[inDir] * enterOffset;
            p2 = center + Dir3D[outDir] * half + Right3D[outDir] * exitOffset;

            if (inDir == outDir)
                p1 = (p0 + p2) * 0.5f;
            else if (outDir == (inDir ^ 1))
                p1 = center;
            else
                p1 = center + Right3D[inDir] * enterOffset + Right3D[outDir] * exitOffset;

            bool IsAvenue(Vector2Int point) => (uint)point.x < CitySimulation.GridSize &&
                (uint)point.y < CitySimulation.GridSize && _city.GetCell(point.x, point.y).RoadType == RoadType.Avenue;
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
                ComputeCurve(v, out Vector3 p0, out Vector3 p1, out Vector3 p2);

                float u = 1f - v.T;
                Vector3 pos = u * u * p0 + 2f * u * v.T * p1 + v.T * v.T * p2;
                Vector3 tangent = 2f * u * (p1 - p0) + 2f * v.T * (p2 - p1);
                if (tangent.sqrMagnitude < 1e-6f) tangent = p2 - p0;
                CityChunkRenderer.ApplyRoadGrade(_city, v.Cell, _cellSize, ref pos, ref tangent);
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
