using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace XCity
{
    internal sealed class HelicopterSystem : IDisposable
    {
        private const int MaxHelicopters = 5;
        private const float StrobeFreq = 5f;
        private const float MainRotorDegPerSec = 980f;
        private const float TailRotorDegPerSec = 1480f;

        private const byte PartFuselage = 0;
        private const byte PartMainRotor = 1;
        private const byte PartTailRotor = 2;
        private const byte PartStrobeL = 3;
        private const byte PartStrobeR = 4;
        private const int PartCount = 5;

        private enum HeliRole
        {
            FireRescue,
            MedEvac,
            Police,
            Executive
        }

        private sealed class HeliTemplate
        {
            public Vector3[] Vertices;
            public Vector3[] Normals;
            public Color32[] Colors;
            public int[] Triangles;
            public byte[] PartIds;
            public Vector3[] PartPivots;
            public HeliRole Role;
            public float CruiseSpeed;
            public Color32 StrobeLeftColor;
            public Color32 StrobeRightColor;
        }

        private struct Waypoint
        {
            public Vector3 HoverPos;
            public HeliRole PreferredRole;
            public bool IsHelipad;
        }

        private sealed class Helicopter
        {
            public Vector3 Position;
            public Vector3 Target;
            public Vector3 Start;
            public float FlightProgress;
            public float FlightLength;
            public float HoverTimer;
            public float CruiseHeight;
            public float Yaw;
            public float Pitch;
            public float Roll;
            public float MainRotorAngle;
            public float TailRotorAngle;
            public float Phase;
            public bool IsHovering;
            public HeliTemplate Template;
        }

        private static readonly Color32 RotorDark = new Color32(42, 46, 52, 255);
        private static readonly Color32 RotorTipYellow = new Color32(246, 206, 44, 255);
        private static readonly Color32 GlassTinted = new Color32(38, 56, 76, 255);
        private static readonly Color32 SkidSteel = new Color32(62, 68, 76, 255);
        private static readonly Color32 Chrome = new Color32(196, 204, 214, 255);
        private static readonly Color32 StrobeDim = new Color32(42, 34, 42, 255);
        private static readonly Color32 StrobeRed = new Color32(255, 48, 48, 255);
        private static readonly Color32 StrobeBlue = new Color32(45, 138, 255, 255);
        private static readonly Color32 StrobeWhite = new Color32(246, 250, 255, 255);

        private static HeliTemplate[] _templates;

        private readonly CitySimulation _city;
        private readonly float _cellSize;
        private readonly System.Random _rng = new System.Random(7890);
        private readonly List<Helicopter> _helicopters = new List<Helicopter>(MaxHelicopters);
        private readonly List<Waypoint> _waypoints = new List<Waypoint>(64);

        private readonly GameObject _go;
        private readonly Mesh _mesh;
        private readonly Bounds _mapBounds;

        private Vector3[] _renderVertices = Array.Empty<Vector3>();
        private Vector3[] _renderNormals = Array.Empty<Vector3>();
        private Color32[] _renderColors = Array.Empty<Color32>();
        private int[] _renderTriangles = Array.Empty<int>();

        private WorldView _view;
        private bool _hasView;
        private bool _waypointsDirty = true;
        private bool _topologyDirty = true;
        private int _targetCount;
        private int _lastStrobeStep = -1;
        private float _time;

        public HelicopterSystem(CitySimulation city, float cellSize, Material material, Transform parent)
        {
            _city = city;
            _cellSize = cellSize;

            float span = CitySimulation.GridSize * cellSize;
            _mapBounds = new Bounds(new Vector3(span * 0.5f, 12f, span * 0.5f), new Vector3(span, 30f, span));

            _mesh = new Mesh { name = "CityHelicopters" };
            _mesh.MarkDynamic();

            _go = new GameObject("Helicopters");
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
            if (!_hasView || _view.Contains(x, z))
                _waypointsDirty = true;
        }

        public void SetView(WorldView view)
        {
            if (_hasView && _view.SameAnchor(view)) return;
            _hasView = true;
            _view = view;
            _waypointsDirty = true;
        }

        public void SetVisible(bool visible)
        {
            if (_go.activeSelf != visible) _go.SetActive(visible);
        }

        public void Prewarm(float seconds)
        {
            if (!_hasView) SetView(new WorldView(CitySimulation.LegacyOffset + 16, CitySimulation.LegacyOffset + 16));
            RebuildWaypoints();
            SyncPopulation();

            const float step = 0.1f;
            int steps = Mathf.CeilToInt(seconds / step);
            for (int i = 0; i < steps; i++)
                Update(step);
        }

        public void Update(float dt)
        {
            if (dt <= 0f) return;
            _time += dt;

            if (_waypointsDirty)
                RebuildWaypoints();

            SyncPopulation();
            if (_helicopters.Count == 0)
            {
                if (_topologyDirty)
                {
                    _mesh.Clear();
                    _topologyDirty = false;
                }
                return;
            }

            for (int i = 0; i < _helicopters.Count; i++)
                UpdateHelicopter(_helicopters[i], dt);

            RenderHelicopters();
        }

        private void UpdateHelicopter(Helicopter h, float dt)
        {
            h.MainRotorAngle = Mathf.Repeat(h.MainRotorAngle + MainRotorDegPerSec * dt, 360f);
            h.TailRotorAngle = Mathf.Repeat(h.TailRotorAngle + TailRotorDegPerSec * dt, 360f);

            if (h.IsHovering)
            {
                h.HoverTimer -= dt;
                h.Pitch = Mathf.MoveTowards(h.Pitch, 0f, 18f * dt);
                h.Roll = Mathf.MoveTowards(h.Roll, 0f, 22f * dt);
                h.Yaw = Mathf.Repeat(h.Yaw + 10f * dt, 360f);
                if (h.HoverTimer <= 0f)
                    PickNextLeg(h);
                return;
            }

            float step = h.Template.CruiseSpeed * dt / Mathf.Max(4f, h.FlightLength);
            h.FlightProgress = Mathf.Clamp01(h.FlightProgress + step);

            float smoothT = h.FlightProgress * h.FlightProgress * (3f - 2f * h.FlightProgress);
            Vector3 horiz = Vector3.Lerp(h.Start, h.Target, smoothT);
            float climbBell = Mathf.Sin(h.FlightProgress * Mathf.PI);
            float baseY = Mathf.Lerp(h.Start.y, h.Target.y, smoothT);
            float y = baseY + Mathf.Max(0f, h.CruiseHeight - baseY) * climbBell;

            Vector3 prevPos = h.Position;
            h.Position = new Vector3(horiz.x, y, horiz.z);

            Vector3 delta = h.Target - h.Start;
            delta.y = 0f;
            float speedFrac = Mathf.Sin(h.FlightProgress * Mathf.PI);
            if (delta.sqrMagnitude > 0.04f)
            {
                float targetYaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                float yawDelta = Mathf.DeltaAngle(h.Yaw, targetYaw);
                h.Yaw = Mathf.MoveTowardsAngle(h.Yaw, targetYaw, 110f * dt);
                float targetRoll = Mathf.Clamp(-yawDelta * 0.45f, -16f, 16f);
                h.Roll = Mathf.MoveTowards(h.Roll, targetRoll, 45f * dt);
            }

            float targetPitch = h.FlightProgress > 0.78f ? -4.5f : 9.0f * speedFrac;
            h.Pitch = Mathf.MoveTowards(h.Pitch, targetPitch, 22f * dt);

            if (h.FlightProgress >= 1f)
            {
                h.Position = h.Target;
                h.IsHovering = true;
                h.HoverTimer = 3.5f + (float)_rng.NextDouble() * 3.0f;
            }
        }

        private void RebuildWaypoints()
        {
            _waypointsDirty = false;
            _waypoints.Clear();
            int developed = 0;

            int minX = _hasView ? _view.MinX : 0;
            int maxX = _hasView ? _view.MaxX : CitySimulation.GridSize - 1;
            int minZ = _hasView ? _view.MinZ : 0;
            int maxZ = _hasView ? _view.MaxZ : CitySimulation.GridSize - 1;

            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    CityCell cell = _city.GetCell(x, z);
                    if (cell.Type == CellType.Empty || cell.Type == CellType.Road || cell.Height <= 0f) continue;
                    developed++;

                    Vector3 worldCenter = new Vector3((x + 0.5f) * _cellSize, 0f, (z + 0.5f) * _cellSize);
                    switch (cell.Model)
                    {
                        case BuildingModel.Hospital:
                            _waypoints.Add(new Waypoint
                            {
                                HoverPos = worldCenter + Vector3.up * (cell.Height + 2.0f),
                                PreferredRole = HeliRole.MedEvac,
                                IsHelipad = true
                            });
                            break;
                        case BuildingModel.FireStation:
                            _waypoints.Add(new Waypoint
                            {
                                HoverPos = worldCenter + Vector3.up * (cell.Height + 3.2f),
                                PreferredRole = HeliRole.FireRescue
                            });
                            break;
                        case BuildingModel.PoliceStation:
                            _waypoints.Add(new Waypoint
                            {
                                HoverPos = worldCenter + Vector3.up * (cell.Height + 3.4f),
                                PreferredRole = HeliRole.Police
                            });
                            break;
                        case BuildingModel.OfficeTower:
                        case BuildingModel.ShoppingMall:
                        case BuildingModel.University:
                            _waypoints.Add(new Waypoint
                            {
                                HoverPos = worldCenter + Vector3.up * (cell.Height + 2.6f),
                                PreferredRole = HeliRole.Executive
                            });
                            break;
                        default:
                            if (cell.Type == CellType.PowerPlant)
                            {
                                _waypoints.Add(new Waypoint
                                {
                                    HoverPos = worldCenter + Vector3.up * 8.6f,
                                    PreferredRole = HeliRole.FireRescue
                                });
                            }
                            else if (cell.Type == CellType.Industrial && ((x + z) % 5 == 0))
                            {
                                _waypoints.Add(new Waypoint
                                {
                                    HoverPos = worldCenter + Vector3.up * (cell.Height + 4.4f),
                                    PreferredRole = HeliRole.FireRescue
                                });
                            }
                            else if ((x % 4 == 1) && (z % 4 == 1))
                            {
                                HeliRole role = cell.Type == CellType.Commercial ? HeliRole.Executive : HeliRole.Police;
                                _waypoints.Add(new Waypoint
                                {
                                    HoverPos = worldCenter + Vector3.up * (cell.Height + 4.2f),
                                    PreferredRole = role
                                });
                            }
                            break;
                    }
                }
            }

            _targetCount = (developed < 6 || _waypoints.Count < 2)
                ? 0
                : Mathf.Clamp(1 + developed / 30, 1, MaxHelicopters);
        }

        private void SyncPopulation()
        {
            while (_helicopters.Count > _targetCount)
            {
                _helicopters.RemoveAt(_helicopters.Count - 1);
                _topologyDirty = true;
            }

            if (_helicopters.Count >= _targetCount || _waypoints.Count == 0) return;

            EnsureTemplates();
            HeliRole[] spawnOrder =
            {
                HeliRole.MedEvac,
                HeliRole.FireRescue,
                HeliRole.Police,
                HeliRole.Executive,
                HeliRole.FireRescue
            };

            while (_helicopters.Count < _targetCount)
            {
                int idx = _helicopters.Count;
                HeliRole role = spawnOrder[idx % spawnOrder.Length];
                HeliTemplate tpl = _templates[(int)role];
                Waypoint startWp = PickWaypoint(role, null);

                var heli = new Helicopter
                {
                    Position = startWp.HoverPos,
                    Start = startWp.HoverPos,
                    Target = startWp.HoverPos,
                    CruiseHeight = 10.6f + idx * 0.7f,
                    Yaw = (float)_rng.NextDouble() * 360f,
                    Phase = (float)_rng.NextDouble() * 6.283f,
                    IsHovering = idx == 0 && startWp.IsHelipad,
                    HoverTimer = 2.0f + idx * 0.8f,
                    Template = tpl
                };
                if (!heli.IsHovering)
                    PickNextLeg(heli);
                _helicopters.Add(heli);
                _topologyDirty = true;
            }
        }

        private Waypoint PickWaypoint(HeliRole role, Vector3? awayFrom)
        {
            int bestIdx = -1;
            int rollCount = 0;

            for (int i = 0; i < _waypoints.Count; i++)
            {
                Waypoint wp = _waypoints[i];
                if (awayFrom.HasValue && (wp.HoverPos - awayFrom.Value).sqrMagnitude < 16f * 16f)
                    continue;
                if (wp.PreferredRole != role) continue;

                rollCount++;
                if (_rng.Next(rollCount) == 0)
                    bestIdx = i;
            }

            if (bestIdx >= 0) return _waypoints[bestIdx];

            for (int attempt = 0; attempt < 8; attempt++)
            {
                int i = _rng.Next(_waypoints.Count);
                if (!awayFrom.HasValue || (_waypoints[i].HoverPos - awayFrom.Value).sqrMagnitude > 14f * 14f)
                    return _waypoints[i];
            }

            return _waypoints[_rng.Next(_waypoints.Count)];
        }

        private void PickNextLeg(Helicopter h)
        {
            Waypoint next = PickWaypoint(h.Template.Role, h.Position);
            h.Start = h.Position;
            h.Target = next.HoverPos;
            Vector3 flat = h.Target - h.Start;
            flat.y = 0f;
            h.FlightLength = Mathf.Max(6f, flat.magnitude);
            h.FlightProgress = 0f;
            h.IsHovering = false;
        }

        private void RenderHelicopters()
        {
            int count = _helicopters.Count;
            if (_topologyDirty)
            {
                int totalVerts = 0;
                int totalTris = 0;
                for (int i = 0; i < count; i++)
                {
                    HeliTemplate tpl = _helicopters[i].Template;
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
                    HeliTemplate tpl = _helicopters[i].Template;
                    Array.Copy(tpl.Colors, 0, _renderColors, vOffset, tpl.Colors.Length);
                    int[] srcTris = tpl.Triangles;
                    for (int t = 0; t < srcTris.Length; t++)
                        _renderTriangles[tOffset + t] = srcTris[t] + vOffset;

                    vOffset += tpl.Vertices.Length;
                    tOffset += srcTris.Length;
                }
                _lastStrobeStep = -1;
            }

            int strobeStep = (int)(_time * StrobeFreq) & 1;
            bool updateColors = _topologyDirty || strobeStep != _lastStrobeStep;
            _lastStrobeStep = strobeStep;

            int vertCursor = 0;
            for (int i = 0; i < count; i++)
            {
                Helicopter h = _helicopters[i];
                HeliTemplate tpl = h.Template;

                float hoverBob = h.IsHovering ? Mathf.Sin(_time * 2.5f + h.Phase) * 0.09f : Mathf.Sin(_time * 4.0f + h.Phase) * 0.03f;
                Vector3 basePos = h.Position + new Vector3(0f, hoverBob, 0f);
                Quaternion bodyRot = Quaternion.Euler(h.Pitch, h.Yaw, h.Roll);
                Quaternion mainRotorRot = bodyRot * Quaternion.Euler(0f, h.MainRotorAngle, 0f);
                Quaternion tailRotorRot = bodyRot * Quaternion.Euler(h.TailRotorAngle, 0f, 0f);

                Vector3 mainPivot = tpl.PartPivots[PartMainRotor];
                Vector3 tailPivot = tpl.PartPivots[PartTailRotor];
                Vector3 worldMainPivot = basePos + bodyRot * mainPivot;
                Vector3 worldTailPivot = basePos + bodyRot * tailPivot;

                Vector3[] srcV = tpl.Vertices;
                Vector3[] srcN = tpl.Normals;
                byte[] partIds = tpl.PartIds;

                for (int k = 0; k < srcV.Length; k++)
                {
                    byte partId = partIds[k];
                    if (partId == PartMainRotor)
                    {
                        _renderVertices[vertCursor] = worldMainPivot + mainRotorRot * (srcV[k] - mainPivot);
                        _renderNormals[vertCursor] = mainRotorRot * srcN[k];
                    }
                    else if (partId == PartTailRotor)
                    {
                        _renderVertices[vertCursor] = worldTailPivot + tailRotorRot * (srcV[k] - tailPivot);
                        _renderNormals[vertCursor] = tailRotorRot * srcN[k];
                    }
                    else
                    {
                        _renderVertices[vertCursor] = basePos + bodyRot * srcV[k];
                        _renderNormals[vertCursor] = bodyRot * srcN[k];

                        if (updateColors)
                        {
                            if (partId == PartStrobeL)
                                _renderColors[vertCursor] = strobeStep == 0 ? tpl.StrobeLeftColor : StrobeDim;
                            else if (partId == PartStrobeR)
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

        private static void EnsureTemplates()
        {
            if (_templates != null) return;
            _templates = new[]
            {
                BuildTemplate(HeliRole.FireRescue,
                    new Color32(212, 38, 34, 255),
                    new Color32(246, 202, 38, 255),
                    new Color32(236, 118, 32, 255),
                    StrobeRed, StrobeWhite, 8.2f),
                BuildTemplate(HeliRole.MedEvac,
                    new Color32(244, 246, 248, 255),
                    new Color32(210, 44, 44, 255),
                    new Color32(56, 136, 134, 255),
                    StrobeRed, StrobeWhite, 8.6f),
                BuildTemplate(HeliRole.Police,
                    new Color32(36, 56, 88, 255),
                    new Color32(240, 242, 246, 255),
                    new Color32(58, 128, 212, 255),
                    StrobeBlue, StrobeRed, 8.8f),
                BuildTemplate(HeliRole.Executive,
                    new Color32(52, 126, 188, 255),
                    new Color32(210, 216, 224, 255),
                    new Color32(44, 52, 62, 255),
                    StrobeWhite, StrobeRed, 9.0f)
            };
        }

        private static HeliTemplate BuildTemplate(
            HeliRole role,
            Color32 primary,
            Color32 secondary,
            Color32 accent,
            Color32 strobeL,
            Color32 strobeR,
            float speed)
        {
            var b = new MeshBuilder();
            b.SetFrame(Vector3.zero, 0);
            var parts = new List<byte>(256);
            var pivots = new Vector3[PartCount];

            void MarkPart(int start, byte partId)
            {
                int end = b.VertexCount;
                while (parts.Count < end) parts.Add(partId);
                for (int i = start; i < end; i++) parts[i] = partId;
            }

            // Landing skids
            b.AddBox(new Vector3(-0.36f, -0.36f, 0.14f), new Vector3(0.06f, 0.05f, 1.48f), SkidSteel, SkidSteel);
            b.AddBox(new Vector3(0.36f, -0.36f, 0.14f), new Vector3(0.06f, 0.05f, 1.48f), SkidSteel, SkidSteel);
            b.AddBox(new Vector3(-0.32f, -0.22f, 0.44f), new Vector3(0.05f, 0.28f, 0.05f), SkidSteel, SkidSteel);
            b.AddBox(new Vector3(0.32f, -0.22f, 0.44f), new Vector3(0.05f, 0.28f, 0.05f), SkidSteel, SkidSteel);
            b.AddBox(new Vector3(-0.32f, -0.22f, -0.22f), new Vector3(0.05f, 0.28f, 0.05f), SkidSteel, SkidSteel);
            b.AddBox(new Vector3(0.32f, -0.22f, -0.22f), new Vector3(0.05f, 0.28f, 0.05f), SkidSteel, SkidSteel);

            // Main cabin & bubble nose
            Vector3 cabinCenter = new Vector3(0f, 0.14f, 0.14f);
            Vector3 cabinSize = new Vector3(0.68f, 0.56f, 1.18f);
            b.AddBox(cabinCenter, cabinSize, primary, secondary);
            b.AddBox(new Vector3(0f, 0.04f, 0.84f), new Vector3(0.56f, 0.34f, 0.34f), primary, GlassTinted);

            // Side windows, windshield & role markings
            b.AddFacadeRect(cabinCenter, cabinSize, 0, -0.28f, 0.28f, 0.16f, 0.38f, GlassTinted);
            b.AddFacadeRect(cabinCenter, cabinSize, 2, 0.08f, 0.52f, 0.14f, 0.38f, GlassTinted);
            b.AddFacadeRect(cabinCenter, cabinSize, 3, 0.08f, 0.52f, 0.14f, 0.38f, GlassTinted);
            b.AddFacadeRect(cabinCenter, cabinSize, 2, -0.52f, 0.48f, -0.02f, 0.08f, accent);
            b.AddFacadeRect(cabinCenter, cabinSize, 3, -0.52f, 0.48f, -0.02f, 0.08f, accent);

            // Role-specific attachments (belly water tank / red cross / nose FLIR turret)
            if (role == HeliRole.FireRescue)
            {
                b.AddBox(new Vector3(0f, -0.20f, 0.08f), new Vector3(0.48f, 0.18f, 0.86f), accent, accent);
            }
            else if (role == HeliRole.MedEvac)
            {
                Color32 redCross = new Color32(215, 42, 42, 255);
                b.AddFacadeRect(cabinCenter, cabinSize, 2, -0.38f, -0.06f, 0.16f, 0.24f, redCross);
                b.AddFacadeRect(cabinCenter, cabinSize, 2, -0.26f, -0.18f, 0.06f, 0.34f, redCross);
                b.AddFacadeRect(cabinCenter, cabinSize, 3, -0.38f, -0.06f, 0.16f, 0.24f, redCross);
                b.AddFacadeRect(cabinCenter, cabinSize, 3, -0.26f, -0.18f, 0.06f, 0.34f, redCross);
            }
            else if (role == HeliRole.Police)
            {
                b.AddBox(new Vector3(0f, -0.16f, 0.88f), new Vector3(0.20f, 0.18f, 0.20f), RotorDark, Chrome);
            }

            // Twin turbine cowling & rotor mast
            b.AddBox(new Vector3(0f, 0.48f, 0.02f), new Vector3(0.42f, 0.16f, 0.78f), secondary, accent);
            b.AddCylinder(new Vector3(0f, 0.56f, 0.08f), 0.08f, 0.07f, 0.14f, 6, Chrome, true, RotorDark);

            // Tail boom, horizontal stabilizer & vertical tail fin
            b.AddBox(new Vector3(0f, 0.18f, -0.96f), new Vector3(0.16f, 0.18f, 1.12f), primary, secondary);
            b.AddBox(new Vector3(0f, 0.18f, -1.24f), new Vector3(0.62f, 0.05f, 0.18f), secondary, accent);
            b.AddBox(new Vector3(0f, 0.38f, -1.46f), new Vector3(0.10f, 0.44f, 0.26f), secondary, secondary);
            MarkPart(0, PartFuselage);

            // Navigation / anti-collision strobes
            int sL = b.VertexCount;
            b.AddBox(new Vector3(-0.36f, 0.16f, 0.04f), new Vector3(0.08f, 0.08f, 0.10f), strobeL, strobeL);
            MarkPart(sL, PartStrobeL);

            int sR = b.VertexCount;
            b.AddBox(new Vector3(0.0f, 0.62f, -1.46f), new Vector3(0.10f, 0.08f, 0.10f), strobeR, strobeR);
            MarkPart(sR, PartStrobeR);

            // 4-blade main rotor with yellow warning tips
            Vector3 mainPivot = new Vector3(0f, 0.72f, 0.08f);
            pivots[PartMainRotor] = mainPivot;
            int mainStart = b.VertexCount;
            b.AddBox(mainPivot, new Vector3(0.16f, 0.07f, 0.16f), Chrome, RotorDark);
            b.AddBox(mainPivot, new Vector3(2.58f, 0.03f, 0.11f), RotorDark, RotorDark);
            b.AddBox(mainPivot, new Vector3(0.11f, 0.03f, 2.58f), RotorDark, RotorDark);
            b.AddBox(mainPivot + new Vector3(-1.38f, 0f, 0f), new Vector3(0.22f, 0.035f, 0.115f), RotorTipYellow, RotorTipYellow);
            b.AddBox(mainPivot + new Vector3(1.38f, 0f, 0f), new Vector3(0.22f, 0.035f, 0.115f), RotorTipYellow, RotorTipYellow);
            b.AddBox(mainPivot + new Vector3(0f, 0f, -1.38f), new Vector3(0.115f, 0.035f, 0.22f), RotorTipYellow, RotorTipYellow);
            b.AddBox(mainPivot + new Vector3(0f, 0f, 1.38f), new Vector3(0.115f, 0.035f, 0.22f), RotorTipYellow, RotorTipYellow);
            MarkPart(mainStart, PartMainRotor);

            // Tail rotor spinning on +X plane
            Vector3 tailPivot = new Vector3(0.11f, 0.44f, -1.46f);
            pivots[PartTailRotor] = tailPivot;
            int tailStart = b.VertexCount;
            b.AddBox(tailPivot, new Vector3(0.06f, 0.08f, 0.08f), Chrome, Chrome);
            b.AddBox(tailPivot, new Vector3(0.035f, 0.56f, 0.07f), RotorDark, RotorDark);
            b.AddBox(tailPivot, new Vector3(0.035f, 0.07f, 0.56f), RotorDark, RotorDark);
            b.AddBox(tailPivot + new Vector3(0f, 0.28f, 0f), new Vector3(0.04f, 0.09f, 0.075f), RotorTipYellow, RotorTipYellow);
            b.AddBox(tailPivot + new Vector3(0f, -0.28f, 0f), new Vector3(0.04f, 0.09f, 0.075f), RotorTipYellow, RotorTipYellow);
            MarkPart(tailStart, PartTailRotor);

            var mesh = new Mesh();
            b.ApplyTo(mesh);
            Vector3[] verts = mesh.vertices;
            while (parts.Count < verts.Length) parts.Add(PartFuselage);

            var tpl = new HeliTemplate
            {
                Vertices = verts,
                Normals = mesh.normals,
                Colors = mesh.colors32,
                Triangles = mesh.triangles,
                PartIds = parts.ToArray(),
                PartPivots = pivots,
                Role = role,
                CruiseSpeed = speed,
                StrobeLeftColor = strobeL,
                StrobeRightColor = strobeR
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
