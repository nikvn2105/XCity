using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace XCity
{
    internal sealed class WatercraftSystem : IDisposable
    {
        private const int MaxVessels = 10;
        private const float WaterY = BridgeProfile.WaterSurface;

        private const byte PartHull = 0;
        private const byte PartWake = 1;
        private const byte PartStrobe = 2;

        private enum VesselKind
        {
            CargoShip,
            Ferry,
            Tugboat,
            CoastGuard,
            Yacht
        }

        private sealed class VesselTemplate
        {
            public Vector3[] Vertices;
            public Vector3[] Normals;
            public Color32[] Colors;
            public int[] Triangles;
            public byte[] PartIds;
            public VesselKind Kind;
            public string DisplayName;
            public float Speed;
            public float Radius;
            public Color32 StrobeColor;
        }

        private struct ChannelGate
        {
            public Vector3 Center;
            public RoadConnections BridgeAxis;
        }

        private sealed class Vessel
        {
            public Vector3 Start;
            public Vector3 End;
            public Vector3 Position;
            public float Progress;
            public float Distance;
            public float PauseTimer;
            public float Yaw;
            public float CurrentSpeed;
            public float Phase;
            public bool ChannelRunner;
            public VesselTemplate Template;
        }

        private static readonly Color32 WakeFoam = new Color32(232, 246, 252, 255);
        private static readonly Color32 GlassDark = new Color32(36, 54, 72, 0);
        private static readonly Color32 NavGlowWhite = new Color32(246, 250, 255, 0);
        private static readonly Color32 NavGlowRed = new Color32(255, 62, 54, 0);
        private static readonly Color32 StrobeDim = new Color32(42, 42, 48, 255);

        private static VesselTemplate[] _templates;

        private readonly CitySimulation _city;
        private readonly float _cellSize;
        private readonly System.Random _rng = new System.Random(4242);
        private readonly List<Vessel> _vessels = new List<Vessel>(MaxVessels);
        private readonly List<Vector3> _openWater = new List<Vector3>(256);
        private readonly List<Vector3> _harborBerths = new List<Vector3>(8);
        private readonly List<ChannelGate> _channelGates = new List<ChannelGate>(8);

        private readonly GameObject _go;
        private readonly Mesh _mesh;
        private readonly Bounds _mapBounds;

        private Vector3[] _renderVertices = Array.Empty<Vector3>();
        private Vector3[] _renderNormals = Array.Empty<Vector3>();
        private Color32[] _renderColors = Array.Empty<Color32>();
        private int[] _renderTriangles = Array.Empty<int>();

        private WorldView _view;
        private bool _hasView;
        private bool _waterDirty = true;
        private bool _topologyDirty = true;
        private int _lastRoadVersion = -1;
        private int _targetCount;
        private int _lastStrobeStep = -1;
        private float _time;

        public int ActiveCount => _vessels.Count;

        public WatercraftSystem(CitySimulation city, float cellSize, Material material, Transform parent)
        {
            _city = city;
            _cellSize = cellSize;

            float span = CitySimulation.GridSize * cellSize;
            _mapBounds = new Bounds(new Vector3(span * 0.5f, 0f, span * 0.5f), new Vector3(span, 12f, span));

            _mesh = new Mesh { name = "CityWatercraft" };
            _mesh.MarkDynamic();

            _go = new GameObject("Watercraft");
            _go.transform.SetParent(parent, false);
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            MeshRenderer renderer = _go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;

            _city.CellChanged += OnCellChanged;
        }

        private void OnCellChanged(int x, int z)
        {
            if (!_hasView || (x >= _view.MinX - 10 && x <= _view.MaxX + 10 && z >= _view.MinZ - 10 && z <= _view.MaxZ + 10))
                _waterDirty = true;
        }

        public void SetView(WorldView view)
        {
            if (_hasView && _view.SameAnchor(view)) return;
            _hasView = true;
            _view = view;
            _waterDirty = true;
            TrimOutOfView();
        }

        public void SetVisible(bool visible)
        {
            if (_go.activeSelf != visible) _go.SetActive(visible);
        }

        public void Prewarm(float seconds)
        {
            if (!_hasView) SetView(new WorldView(CitySimulation.LegacyOffset + 16, CitySimulation.LegacyOffset + 16));
            RebuildWaterGraph();
            SyncFleet();

            const float step = 0.1f;
            int steps = Mathf.CeilToInt(seconds / step);
            for (int i = 0; i < steps; i++)
                Update(step);
        }

        public bool TryPickVessel(Ray ray, out int index, out float nearest)
        {
            index = -1;
            nearest = float.PositiveInfinity;
            for (int i = 0; i < _vessels.Count; i++)
            {
                Vessel v = _vessels[i];
                float r = v.Template.Radius;
                var bounds = new Bounds(v.Position + Vector3.up * 0.8f, new Vector3(r * 2.2f, 2.2f, r * 2.2f));
                if (bounds.IntersectRay(ray, out float dist) && dist < nearest)
                {
                    nearest = dist;
                    index = i;
                }
            }
            return index >= 0;
        }

        public bool TryGetVesselFocus(int index, out Vector3 worldPos, out string displayName)
        {
            if (index >= 0 && index < _vessels.Count)
            {
                Vessel v = _vessels[index];
                worldPos = new Vector3(v.Position.x, WaterY + 0.6f, v.Position.z);
                displayName = v.Template.DisplayName;
                return true;
            }
            worldPos = default;
            displayName = null;
            return false;
        }

        public void Update(float dt)
        {
            if (dt <= 0f) return;
            _time += dt;

            int roadVersion = _city.Roads.Version;
            if (roadVersion != _lastRoadVersion)
            {
                _lastRoadVersion = roadVersion;
                _waterDirty = true;
            }

            if (_waterDirty)
                RebuildWaterGraph();

            SyncFleet();
            if (_vessels.Count == 0)
            {
                if (_topologyDirty)
                {
                    _mesh.Clear();
                    _topologyDirty = false;
                }
                return;
            }

            for (int i = 0; i < _vessels.Count; i++)
                UpdateVessel(_vessels[i], dt);

            RenderFleet();
        }

        private void UpdateVessel(Vessel v, float dt)
        {
            if (v.PauseTimer > 0f)
            {
                v.PauseTimer -= dt;
                v.CurrentSpeed = Mathf.MoveTowards(v.CurrentSpeed, 0f, 3.5f * dt);
                if (v.PauseTimer <= 0f)
                    AssignNextLeg(v);
                return;
            }

            float targetSpeed = v.Template.Speed;
            v.CurrentSpeed = Mathf.MoveTowards(v.CurrentSpeed, targetSpeed, 2.5f * dt);

            float step = v.CurrentSpeed * dt / Mathf.Max(4f, v.Distance);
            v.Progress = Mathf.Clamp01(v.Progress + step);
            float t = v.Progress * v.Progress * (3f - 2f * v.Progress);
            v.Position = Vector3.Lerp(v.Start, v.End, t);

            Vector3 dir = v.End - v.Start;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
            {
                float targetYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                v.Yaw = Mathf.MoveTowardsAngle(v.Yaw, targetYaw, 85f * dt);
            }

            if (v.Progress >= 1f)
            {
                v.Position = v.End;
                v.PauseTimer = v.ChannelRunner ? 1.2f : 2.5f + (float)_rng.NextDouble() * 2.5f;
            }
        }

        private void TrimOutOfView()
        {
            int minX = Mathf.Max(0, _view.MinX - 12);
            int maxX = Mathf.Min(CitySimulation.GridSize - 1, _view.MaxX + 12);
            int minZ = Mathf.Max(0, _view.MinZ - 12);
            int maxZ = Mathf.Min(CitySimulation.GridSize - 1, _view.MaxZ + 12);
            for (int i = _vessels.Count - 1; i >= 0; i--)
            {
                Vector3 pos = _vessels[i].Position;
                int tx = Mathf.FloorToInt(pos.x / _cellSize);
                int tz = Mathf.FloorToInt(pos.z / _cellSize);
                if (tx < minX || tx > maxX || tz < minZ || tz > maxZ)
                {
                    _vessels.RemoveAt(i);
                    _topologyDirty = true;
                }
            }
        }

        private void RebuildWaterGraph()
        {
            _waterDirty = false;
            _openWater.Clear();
            _harborBerths.Clear();
            _channelGates.Clear();

            int minX = Mathf.Max(0, (_hasView ? _view.MinX : 0) - 10);
            int maxX = Mathf.Min(CitySimulation.GridSize - 1, (_hasView ? _view.MaxX : CitySimulation.GridSize - 1) + 10);
            int minZ = Mathf.Max(0, (_hasView ? _view.MinZ : 0) - 10);
            int maxZ = Mathf.Min(CitySimulation.GridSize - 1, (_hasView ? _view.MaxZ : CitySimulation.GridSize - 1) + 10);

            int waterCount = 0;
            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    CityCell cell = _city.GetCell(x, z);
                    if (cell.Model == BuildingModel.Harbor &&
                        CitySimulation.TryHarborFacing(x, z, out int turn, out _, out _))
                    {
                        int dx = turn == 1 ? 1 : turn == 3 ? -1 : 0;
                        int dz = turn == 0 ? 1 : turn == 2 ? -1 : 0;
                        int bx = x + dx * 2;
                        int bz = z + dz * 2;
                        if (IsNavigableWater(bx, bz))
                            _harborBerths.Add(CellWaterCenter(bx, bz));
                    }

                    if (!IsNavigableWater(x, z)) continue;
                    waterCount++;

                    if (_city.TryGetBridgeProfile(x, z, out BridgeProfile bridge) &&
                        bridge.HasNavigationOpening &&
                        Mathf.Abs(bridge.Index + 0.5f - bridge.Count * 0.5f) <= 0.55f)
                    {
                        _channelGates.Add(new ChannelGate
                        {
                            Center = CellWaterCenter(x, z),
                            BridgeAxis = bridge.Axis
                        });
                    }

                    if ((x % 2 == 0) && (z % 2 == 0) && IsClearFromCoast(x, z))
                        _openWater.Add(CellWaterCenter(x, z));
                }
            }

            _targetCount = (waterCount < 24 || (_openWater.Count < 4 && _channelGates.Count == 0))
                ? 0
                : Mathf.Clamp(2 + waterCount / 140 + _harborBerths.Count * 2 + _channelGates.Count * 2, 2, MaxVessels);
        }

        private void SyncFleet()
        {
            while (_vessels.Count > _targetCount)
            {
                _vessels.RemoveAt(_vessels.Count - 1);
                _topologyDirty = true;
            }

            if (_vessels.Count >= _targetCount || _targetCount == 0) return;

            EnsureTemplates();
            while (_vessels.Count < _targetCount)
            {
                int idx = _vessels.Count;
                VesselTemplate tpl = _templates[idx % _templates.Length];
                var v = new Vessel
                {
                    Phase = (float)_rng.NextDouble() * 6.283f,
                    Template = tpl,
                    CurrentSpeed = tpl.Speed * 0.7f
                };

                if (_channelGates.Count > 0 && idx < Mathf.Min(2, _channelGates.Count * 2) &&
                    TryBuildChannelLeg(_channelGates[idx % _channelGates.Count], idx % 2 == 0, out Vector3 s, out Vector3 e))
                {
                    v.ChannelRunner = true;
                    v.Start = s;
                    v.End = e;
                    v.Progress = 0.22f + 0.35f * (idx % 2);
                }
                else
                {
                    v.ChannelRunner = false;
                    AssignFreshOpenLeg(v);
                    v.Progress = (float)_rng.NextDouble() * 0.6f;
                }

                v.Distance = Mathf.Max(4f, Vector3.Distance(v.Start, v.End));
                v.Position = Vector3.Lerp(v.Start, v.End, v.Progress);
                Vector3 dir = v.End - v.Start;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f)
                    v.Yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

                _vessels.Add(v);
                _topologyDirty = true;
            }
        }

        private void AssignNextLeg(Vessel v)
        {
            if (v.ChannelRunner)
            {
                Vector3 tmp = v.Start;
                v.Start = v.End;
                v.End = tmp;
                v.Progress = 0f;
                v.Distance = Mathf.Max(4f, Vector3.Distance(v.Start, v.End));
                return;
            }

            if (_harborBerths.Count > 0 && _rng.Next(100) < 35)
            {
                Vector3 berth = _harborBerths[_rng.Next(_harborBerths.Count)];
                if (IsPathNavigable(v.Position, berth))
                {
                    v.Start = v.Position;
                    v.End = berth;
                    v.Progress = 0f;
                    v.Distance = Mathf.Max(4f, Vector3.Distance(v.Start, v.End));
                    return;
                }
            }

            if (_openWater.Count > 0)
            {
                for (int attempt = 0; attempt < 16; attempt++)
                {
                    Vector3 candidate = _openWater[_rng.Next(_openWater.Count)];
                    float dist = Vector3.Distance(v.Position, candidate);
                    if (dist < 10f || dist > 64f) continue;
                    if (!IsPathNavigable(v.Position, candidate)) continue;
                    v.Start = v.Position;
                    v.End = candidate;
                    v.Progress = 0f;
                    v.Distance = dist;
                    return;
                }
            }

            Vector3 swap = v.Start;
            v.Start = v.End;
            v.End = swap;
            v.Progress = 0f;
            v.Distance = Mathf.Max(4f, Vector3.Distance(v.Start, v.End));
        }

        private void AssignFreshOpenLeg(Vessel v)
        {
            if (_openWater.Count >= 2)
            {
                for (int attempt = 0; attempt < 24; attempt++)
                {
                    Vector3 a = _openWater[_rng.Next(_openWater.Count)];
                    Vector3 b = _openWater[_rng.Next(_openWater.Count)];
                    float dist = Vector3.Distance(a, b);
                    if (dist < 12f || dist > 64f) continue;
                    if (!IsPathNavigable(a, b)) continue;
                    v.Start = a;
                    v.End = b;
                    return;
                }
            }

            if (_harborBerths.Count > 0)
            {
                Vector3 berth = _harborBerths[0];
                v.Start = berth;
                v.End = berth;
                return;
            }

            Vector3 origin = _openWater.Count > 0 ? _openWater[0] : Vector3.zero;
            v.Start = origin;
            v.End = origin;
        }

        private bool TryBuildChannelLeg(ChannelGate gate, bool forward, out Vector3 start, out Vector3 end)
        {
            Vector3 axisDir = gate.BridgeAxis == CitySimulation.BridgeAlongZ ? Vector3.right : Vector3.forward;
            if (!forward) axisDir = -axisDir;

            Vector3 reachA = gate.Center;
            Vector3 reachB = gate.Center;
            for (int step = 1; step <= 6; step++)
            {
                Vector3 probeA = gate.Center - axisDir * (step * _cellSize);
                if (IsWorldPointNavigable(probeA) && IsClearFromCoastPoint(probeA)) reachA = probeA;
                else break;
            }
            for (int step = 1; step <= 6; step++)
            {
                Vector3 probeB = gate.Center + axisDir * (step * _cellSize);
                if (IsWorldPointNavigable(probeB) && IsClearFromCoastPoint(probeB)) reachB = probeB;
                else break;
            }

            start = reachA;
            end = reachB;
            return Vector3.Distance(start, end) >= _cellSize * 3f;
        }

        private bool IsPathNavigable(Vector3 a, Vector3 b)
        {
            float dist = Vector3.Distance(a, b);
            int samples = Mathf.Max(3, Mathf.CeilToInt(dist / (_cellSize * 0.6f)));
            for (int i = 1; i < samples; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, i / (float)samples);
                if (!IsWorldPointNavigable(p)) return false;
            }
            return true;
        }

        private bool IsWorldPointNavigable(Vector3 p)
        {
            int x = Mathf.FloorToInt(p.x / _cellSize);
            int z = Mathf.FloorToInt(p.z / _cellSize);
            return IsNavigableWater(x, z);
        }

        private bool IsNavigableWater(int x, int z)
        {
            if (x < 0 || z < 0 || x >= CitySimulation.GridSize || z >= CitySimulation.GridSize) return false;
            if (WorldTerrain.IsLand(x, z)) return false;
            if (_city.TryGetBridgeProfile(x, z, out BridgeProfile bp))
                return bp.HasNavigationOpening;
            return true;
        }

        private bool IsClearFromCoastPoint(Vector3 p)
        {
            int x = Mathf.FloorToInt(p.x / _cellSize);
            int z = Mathf.FloorToInt(p.z / _cellSize);
            return IsClearFromCoast(x, z);
        }

        private bool IsClearFromCoast(int x, int z)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    int nx = x + dx;
                    int nz = z + dz;
                    if (nx < 0 || nz < 0 || nx >= CitySimulation.GridSize || nz >= CitySimulation.GridSize)
                        return false;
                    if (WorldTerrain.IsLand(nx, nz)) return false;
                    if (_city.TryGetBridgeProfile(nx, nz, out BridgeProfile bp) && !bp.HasNavigationOpening)
                        return false;
                }
            }
            return true;
        }

        private Vector3 CellWaterCenter(int x, int z) =>
            new Vector3((x + 0.5f) * _cellSize, WaterY, (z + 0.5f) * _cellSize);

        private void RenderFleet()
        {
            int count = _vessels.Count;
            if (_topologyDirty)
            {
                int totalVerts = 0;
                int totalTris = 0;
                for (int i = 0; i < count; i++)
                {
                    VesselTemplate tpl = _vessels[i].Template;
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
                    VesselTemplate tpl = _vessels[i].Template;
                    Array.Copy(tpl.Colors, 0, _renderColors, vOffset, tpl.Colors.Length);
                    int[] srcTris = tpl.Triangles;
                    for (int t = 0; t < srcTris.Length; t++)
                        _renderTriangles[tOffset + t] = srcTris[t] + vOffset;

                    vOffset += tpl.Vertices.Length;
                    tOffset += srcTris.Length;
                }
                _lastStrobeStep = -1;
            }

            int strobeStep = (int)(_time * 4f) & 1;
            bool updateColors = _topologyDirty || strobeStep != _lastStrobeStep;
            _lastStrobeStep = strobeStep;

            int vertCursor = 0;
            for (int i = 0; i < count; i++)
            {
                Vessel v = _vessels[i];
                VesselTemplate tpl = v.Template;

                float heave = Mathf.Sin(_time * 2.1f + v.Phase) * 0.025f;
                float roll = Mathf.Sin(_time * 1.7f + v.Phase) * 2.1f;
                float pitch = -Mathf.Clamp(v.CurrentSpeed * 0.25f, 0f, 1.4f) + Mathf.Sin(_time * 2.5f + v.Phase) * 0.7f;

                Vector3 basePos = new Vector3(v.Position.x, WaterY + heave, v.Position.z);
                Quaternion shipRot = Quaternion.Euler(pitch, v.Yaw, roll);
                Quaternion wakeRot = Quaternion.Euler(0f, v.Yaw, 0f);
                float wakeScale = 0.25f + 0.78f * Mathf.Clamp01(v.CurrentSpeed / Mathf.Max(1f, tpl.Speed));
                float wakeWidthPulse = 1f + Mathf.Sin(_time * 5.2f + v.Phase) * 0.08f;

                Vector3[] srcV = tpl.Vertices;
                Vector3[] srcN = tpl.Normals;
                byte[] partIds = tpl.PartIds;

                for (int k = 0; k < srcV.Length; k++)
                {
                    byte partId = partIds[k];
                    if (partId == PartWake)
                    {
                        Vector3 p = srcV[k];
                        p.x *= wakeWidthPulse;
                        p.z *= wakeScale;
                        _renderVertices[vertCursor] = new Vector3(v.Position.x, WaterY + 0.02f, v.Position.z) + wakeRot * p;
                        _renderNormals[vertCursor] = Vector3.up;
                    }
                    else
                    {
                        _renderVertices[vertCursor] = basePos + shipRot * srcV[k];
                        _renderNormals[vertCursor] = shipRot * srcN[k];
                        if (updateColors && partId == PartStrobe)
                            _renderColors[vertCursor] = strobeStep == 0 ? tpl.StrobeColor : StrobeDim;
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

        private static void EnsureTemplates()
        {
            if (_templates != null) return;
            _templates = new[]
            {
                BuildCargoShip(),
                BuildFerry(),
                BuildCoastGuard(),
                BuildTugboat(),
                BuildYacht()
            };
        }

        private static VesselTemplate BuildCargoShip()
        {
            var b = new MeshBuilder();
            b.SetFrame(Vector3.zero, 0);
            var parts = new List<byte>(256);

            void Mark(int start, byte part)
            {
                while (parts.Count < b.VertexCount) parts.Add(part);
                for (int i = start; i < b.VertexCount; i++) parts[i] = part;
            }

            Color32 hullRed = new Color32(158, 48, 42, 255);
            Color32 hullNavy = new Color32(42, 54, 72, 255);
            Color32 deck = new Color32(106, 114, 120, 255);
            Color32 bridgeWhite = new Color32(238, 240, 242, 255);
            Color32[] crates =
            {
                new Color32(52, 138, 196, 255),
                new Color32(204, 86, 42, 255),
                new Color32(42, 134, 84, 255)
            };

            AddHullShape(b, 1.10f, 3.30f, 0.90f, 0.44f, hullRed, hullNavy, deck);

            // Stacked ISO containers on forward cargo deck
            for (int row = 0; row < 2; row++)
            {
                float cz = 0.05f + row * 0.96f;
                for (int col = -1; col <= 1; col += 2)
                {
                    Color32 c1 = crates[(row + (col < 0 ? 0 : 1)) % crates.Length];
                    Color32 c2 = crates[(row + 2) % crates.Length];
                    b.AddBox(new Vector3(col * 0.24f, 0.56f, cz), new Vector3(0.42f, 0.34f, 0.86f), c1, c1);
                    b.AddBox(new Vector3(col * 0.24f, 0.90f, cz), new Vector3(0.42f, 0.34f, 0.86f), c2, c2);
                }
            }

            // Stern superstructure bridge + smokestack
            Vector3 bridge = new Vector3(0f, 0.84f, -1.16f);
            Vector3 bridgeSize = new Vector3(0.92f, 0.80f, 0.72f);
            b.AddBox(bridge, bridgeSize, bridgeWhite, deck);
            b.AddFacadeRect(bridge, bridgeSize, 0, -0.36f, 0.36f, 0.96f, 1.18f, GlassDark);
            b.AddFacadeRect(bridge, bridgeSize, 2, -0.26f, 0.26f, 0.96f, 1.18f, GlassDark);
            b.AddFacadeRect(bridge, bridgeSize, 3, -0.26f, 0.26f, 0.96f, 1.18f, GlassDark);
            b.AddBox(new Vector3(0f, 1.38f, -1.10f), new Vector3(1.12f, 0.16f, 0.36f), bridgeWhite, bridgeWhite);
            b.AddCylinder(new Vector3(0f, 1.24f, -1.36f), 0.13f, 0.11f, 0.44f, 6, crates[1], true, hullNavy);
            b.AddBox(new Vector3(0f, 1.72f, -1.08f), new Vector3(0.05f, 0.54f, 0.05f), bridgeWhite, NavGlowWhite);
            Mark(0, PartHull);

            int s = b.VertexCount;
            b.AddBox(new Vector3(0f, 2.02f, -1.08f), new Vector3(0.09f, 0.09f, 0.09f), NavGlowWhite, NavGlowWhite);
            Mark(s, PartStrobe);

            AddWakeFoam(b, parts, -1.70f, 0.48f, 2.4f, Mark);
            return Bake(b, parts, VesselKind.CargoShip, "Tàu hàng Container", 3.6f, 2.2f, NavGlowWhite);
        }

        private static VesselTemplate BuildFerry()
        {
            var b = new MeshBuilder();
            b.SetFrame(Vector3.zero, 0);
            var parts = new List<byte>(220);

            void Mark(int start, byte part)
            {
                while (parts.Count < b.VertexCount) parts.Add(part);
                for (int i = start; i < b.VertexCount; i++) parts[i] = part;
            }

            Color32 hullTeal = new Color32(46, 114, 126, 255);
            Color32 white = new Color32(242, 244, 246, 255);
            Color32 deck = new Color32(166, 150, 122, 255);

            AddHullShape(b, 1.00f, 2.75f, 0.80f, 0.40f, hullTeal, hullTeal, deck);

            Vector3 lower = new Vector3(0f, 0.64f, -0.18f);
            Vector3 lowerSize = new Vector3(0.92f, 0.48f, 2.04f);
            b.AddBox(lower, lowerSize, white, deck);
            b.AddFacadeRect(lower, lowerSize, 2, -0.84f, 0.84f, 0.52f, 0.78f, GlassDark);
            b.AddFacadeRect(lower, lowerSize, 3, -0.84f, 0.84f, 0.52f, 0.78f, GlassDark);

            Vector3 wheelhouse = new Vector3(0f, 1.06f, 0.22f);
            Vector3 wheelSize = new Vector3(0.76f, 0.36f, 0.96f);
            b.AddBox(wheelhouse, wheelSize, white, hullTeal);
            b.AddFacadeRect(wheelhouse, wheelSize, 0, -0.32f, 0.32f, 0.96f, 1.18f, GlassDark);
            b.AddFacadeRect(wheelhouse, wheelSize, 2, -0.34f, 0.34f, 0.96f, 1.18f, GlassDark);
            b.AddFacadeRect(wheelhouse, wheelSize, 3, -0.34f, 0.34f, 0.96f, 1.18f, GlassDark);
            Mark(0, PartHull);

            int s = b.VertexCount;
            b.AddBox(new Vector3(0f, 1.38f, 0.22f), new Vector3(0.08f, 0.08f, 0.08f), NavGlowWhite, NavGlowWhite);
            Mark(s, PartStrobe);

            AddWakeFoam(b, parts, -1.40f, 0.44f, 2.1f, Mark);
            return Bake(b, parts, VesselKind.Ferry, "Phà chở khách", 4.2f, 1.8f, NavGlowWhite);
        }

        private static VesselTemplate BuildCoastGuard()
        {
            var b = new MeshBuilder();
            b.SetFrame(Vector3.zero, 0);
            var parts = new List<byte>(180);

            void Mark(int start, byte part)
            {
                while (parts.Count < b.VertexCount) parts.Add(part);
                for (int i = start; i < b.VertexCount; i++) parts[i] = part;
            }

            Color32 white = new Color32(242, 245, 248, 255);
            Color32 navy = new Color32(38, 64, 102, 255);
            Color32 rescueOrange = new Color32(236, 98, 36, 255);

            AddHullShape(b, 0.74f, 1.65f, 0.65f, 0.34f, navy, white, new Color32(158, 166, 174, 255));
            b.AddBox(new Vector3(0f, 0.24f, 0.46f), new Vector3(0.76f, 0.14f, 0.56f), rescueOrange, rescueOrange);

            Vector3 cabin = new Vector3(0f, 0.54f, -0.18f);
            Vector3 cabinSize = new Vector3(0.56f, 0.40f, 0.84f);
            b.AddBox(cabin, cabinSize, white, navy);
            b.AddFacadeRect(cabin, cabinSize, 0, -0.22f, 0.22f, 0.44f, 0.66f, GlassDark);
            b.AddFacadeRect(cabin, cabinSize, 2, -0.30f, 0.30f, 0.44f, 0.66f, GlassDark);
            b.AddFacadeRect(cabin, cabinSize, 3, -0.30f, 0.30f, 0.44f, 0.66f, GlassDark);
            b.AddBox(new Vector3(0f, 0.92f, -0.32f), new Vector3(0.05f, 0.36f, 0.05f), white, white);
            Mark(0, PartHull);

            int s = b.VertexCount;
            b.AddBox(new Vector3(0f, 1.12f, -0.32f), new Vector3(0.09f, 0.09f, 0.09f), new Color32(46, 138, 255, 0), new Color32(46, 138, 255, 0));
            Mark(s, PartStrobe);

            AddWakeFoam(b, parts, -0.88f, 0.34f, 1.8f, Mark);
            return Bake(b, parts, VesselKind.CoastGuard, "Tàu tuần duyên", 5.4f, 1.2f, new Color32(46, 138, 255, 0));
        }

        private static VesselTemplate BuildTugboat()
        {
            var b = new MeshBuilder();
            b.SetFrame(Vector3.zero, 0);
            var parts = new List<byte>(160);

            void Mark(int start, byte part)
            {
                while (parts.Count < b.VertexCount) parts.Add(part);
                for (int i = start; i < b.VertexCount; i++) parts[i] = part;
            }

            Color32 tugRed = new Color32(192, 54, 44, 255);
            Color32 charcoal = new Color32(42, 46, 54, 255);
            Color32 cabinYellow = new Color32(236, 184, 54, 255);

            AddHullShape(b, 0.80f, 1.40f, 0.56f, 0.34f, tugRed, charcoal, new Color32(118, 112, 98, 255));
            Vector3 cabin = new Vector3(0f, 0.58f, 0.06f);
            Vector3 cabinSize = new Vector3(0.56f, 0.46f, 0.66f);
            b.AddBox(cabin, cabinSize, cabinYellow, charcoal);
            b.AddFacadeRect(cabin, cabinSize, 0, -0.22f, 0.22f, 0.50f, 0.74f, GlassDark);
            b.AddFacadeRect(cabin, cabinSize, 2, -0.22f, 0.22f, 0.50f, 0.74f, GlassDark);
            b.AddFacadeRect(cabin, cabinSize, 3, -0.22f, 0.22f, 0.50f, 0.74f, GlassDark);
            b.AddCylinder(new Vector3(-0.14f, 0.78f, -0.34f), 0.06f, 0.05f, 0.34f, 6, charcoal, true, tugRed);
            b.AddCylinder(new Vector3(0.14f, 0.78f, -0.34f), 0.06f, 0.05f, 0.34f, 6, charcoal, true, tugRed);
            Mark(0, PartHull);

            int s = b.VertexCount;
            b.AddBox(new Vector3(0f, 1.02f, 0.06f), new Vector3(0.08f, 0.07f, 0.08f), new Color32(255, 182, 42, 0), new Color32(255, 182, 42, 0));
            Mark(s, PartStrobe);

            AddWakeFoam(b, parts, -0.74f, 0.36f, 1.5f, Mark);
            return Bake(b, parts, VesselKind.Tugboat, "Tàu kéo cảng", 3.8f, 1.1f, new Color32(255, 182, 42, 0));
        }

        private static VesselTemplate BuildYacht()
        {
            var b = new MeshBuilder();
            b.SetFrame(Vector3.zero, 0);
            var parts = new List<byte>(140);

            void Mark(int start, byte part)
            {
                while (parts.Count < b.VertexCount) parts.Add(part);
                for (int i = start; i < b.VertexCount; i++) parts[i] = part;
            }

            Color32 white = new Color32(246, 248, 250, 255);
            Color32 stripe = new Color32(48, 108, 168, 255);
            Color32 teak = new Color32(174, 146, 104, 255);

            AddHullShape(b, 0.66f, 1.38f, 0.58f, 0.28f, stripe, white, teak);
            Vector3 salon = new Vector3(0f, 0.44f, -0.14f);
            Vector3 salonSize = new Vector3(0.50f, 0.30f, 0.78f);
            b.AddBox(salon, salonSize, white, white);
            b.AddFacadeRect(salon, salonSize, 0, -0.20f, 0.20f, 0.36f, 0.54f, GlassDark);
            b.AddFacadeRect(salon, salonSize, 2, -0.28f, 0.28f, 0.36f, 0.54f, GlassDark);
            b.AddFacadeRect(salon, salonSize, 3, -0.28f, 0.28f, 0.36f, 0.54f, GlassDark);
            Mark(0, PartHull);

            int s = b.VertexCount;
            b.AddBox(new Vector3(0f, 0.68f, -0.22f), new Vector3(0.07f, 0.06f, 0.07f), NavGlowRed, NavGlowRed);
            Mark(s, PartStrobe);

            AddWakeFoam(b, parts, -0.72f, 0.30f, 1.6f, Mark);
            return Bake(b, parts, VesselKind.Yacht, "Du thuyền", 4.8f, 1.0f, NavGlowRed);
        }

        private static void AddHullShape(MeshBuilder b, float width, float sternLen, float bowLen, float freeboard,
            Color32 lowerHull, Color32 upperHull, Color32 deckColor)
        {
            const float keel = -0.16f;
            float zStern = -sternLen * 0.5f;
            float zShoulder = sternLen * 0.5f;
            float zBow = zShoulder + bowLen;
            float hw = width * 0.5f;

            b.AddBox(new Vector3(0f, (keel + freeboard) * 0.5f, 0f), new Vector3(width, freeboard - keel, sternLen),
                lowerHull, deckColor);
            b.AddBox(new Vector3(0f, freeboard - 0.06f, 0f), new Vector3(width + 0.02f, 0.12f, sternLen + 0.02f),
                upperHull, deckColor);

            var deckL = new Vector3(-hw, freeboard, zShoulder);
            var deckR = new Vector3(hw, freeboard, zShoulder);
            var bowTop = new Vector3(0f, freeboard + 0.05f, zBow);
            var drop = Vector3.up * (freeboard - keel);

            b.AddTriangle(deckL, deckR, bowTop, Vector3.up, deckColor);
            b.AddQuad(deckL, bowTop, bowTop - drop, deckL - drop, Vector3.left, lowerHull);
            b.AddQuad(bowTop, deckR, deckR - drop, bowTop - drop, Vector3.right, lowerHull);
        }

        private static void AddWakeFoam(MeshBuilder b, List<byte> parts, float sternZ, float halfBeam, float length, Action<int, byte> mark)
        {
            int start = b.VertexCount;
            float innerTail = halfBeam * 0.65f;
            float outerTail = halfBeam * 1.65f;
            float endZ = sternZ - length;

            b.AddQuad(
                new Vector3(-halfBeam * 0.85f, 0f, sternZ),
                new Vector3(-halfBeam * 0.45f, 0f, sternZ),
                new Vector3(-innerTail, 0f, endZ),
                new Vector3(-outerTail, 0f, endZ),
                Vector3.up, WakeFoam);

            b.AddQuad(
                new Vector3(halfBeam * 0.45f, 0f, sternZ),
                new Vector3(halfBeam * 0.85f, 0f, sternZ),
                new Vector3(outerTail, 0f, endZ),
                new Vector3(innerTail, 0f, endZ),
                Vector3.up, WakeFoam);

            mark(start, PartWake);
        }

        private static VesselTemplate Bake(MeshBuilder b, List<byte> parts, VesselKind kind, string name,
            float speed, float radius, Color32 strobeColor)
        {
            var mesh = new Mesh();
            b.ApplyTo(mesh);
            Vector3[] verts = mesh.vertices;
            while (parts.Count < verts.Length) parts.Add(PartHull);

            var tpl = new VesselTemplate
            {
                Vertices = verts,
                Normals = mesh.normals,
                Colors = mesh.colors32,
                Triangles = mesh.triangles,
                PartIds = parts.ToArray(),
                Kind = kind,
                DisplayName = name,
                Speed = speed,
                Radius = radius,
                StrobeColor = strobeColor
            };
            UnityEngine.Object.Destroy(mesh);
            return tpl;
        }

        public void Dispose()
        {
            _city.CellChanged -= OnCellChanged;
            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
            if (_go != null) UnityEngine.Object.Destroy(_go);
        }
    }
}
