using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace XCity
{
    internal sealed class CitizenSystem : IDisposable
    {
        private const int MaxWalkCells = 600;
        private const int MaxCitizens = 90;
        private const float RecountInterval = 1.0f;
        private const float SpawnInterval = 0.25f;
        private const float CurbY = 0.106f;

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

        private struct CandidateCell
        {
            public Vector2Int Cell;
            public CitizenGroup Group;
        }

        private sealed class Citizen
        {
            public Vector2Int Cell;
            public int InDir;
            public int OutDir;
            public int SideSign;
            public float T;
            public float Phase;
            public float SeedPhase;
            public float PauseTimer;
            public float PauseYaw;
            public CitizenTemplate Template;
            public List<int> Route; // directions to the destination; null while strolling
            public int RouteStep;
        }

        private readonly CitySimulation _city;
        private readonly float _cellSize;
        private readonly System.Random _rng = new System.Random(5678);
        private readonly List<Citizen> _citizens = new List<Citizen>(MaxCitizens);
        private readonly List<CandidateCell> _candidates = new List<CandidateCell>(256);
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
        private int _targetCount;
        private bool _topologyDirty = true;

        public CitizenSystem(CitySimulation city, float cellSize, Material material, Transform parent)
        {
            _city = city;
            _cellSize = cellSize;

            float span = CitySimulation.GridSize * cellSize;
            _mapBounds = new Bounds(new Vector3(span * 0.5f, (3f + BridgeProfile.MaxRise + WorldTerrain.MaxSurfaceY) * 0.5f, span * 0.5f), new Vector3(span, 3f + BridgeProfile.MaxRise + WorldTerrain.MaxSurfaceY, span));

            _mesh = new Mesh { name = "CityCitizens" };
            _mesh.MarkDynamic();

            _go = new GameObject("Citizens");
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
            for (int i = 0; i < _targetCount && _citizens.Count < _targetCount; i++)
            {
                if (!TrySpawn()) break;
            }

            const float step = 0.15f;
            int steps = Mathf.CeilToInt(seconds / step);
            for (int i = 0; i < steps; i++)
                Update(step);
        }

        private WorldView _view;

        public void SetView(WorldView view)
        {
            if (_view.SameAnchor(view)) return;
            _view = view;
            _recountTimer = RecountInterval;
            for (int i = _citizens.Count - 1; i >= 0; i--)
            {
                var cell = _citizens[i].Cell;
                if (view.Contains(cell.x, cell.y)) continue;
                _citizens.RemoveAt(i);
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
                ValidateCitizens();
                _recountTimer = RecountInterval;
            }

            _recountTimer += dt;
            if (_recountTimer >= RecountInterval)
            {
                _recountTimer = 0f;
                Recount();
            }

            while (_citizens.Count > _targetCount)
            {
                _citizens.RemoveAt(_citizens.Count - 1);
                _topologyDirty = true;
            }

            if (_citizens.Count < _targetCount)
            {
                _spawnTimer += dt;
                if (_spawnTimer >= SpawnInterval)
                {
                    _spawnTimer = 0f;
                    TrySpawn();
                }
            }

            for (int i = _citizens.Count - 1; i >= 0; i--)
            {
                Citizen c = _citizens[i];
                if (c.PauseTimer > 0f)
                {
                    c.PauseTimer -= dt;
                    continue;
                }

                c.Phase += dt * c.Template.StrideFreq;
                ComputeSidewalkCurve(c.Cell, c.InDir, c.OutDir, c.SideSign, out Vector3 p0, out Vector3 p1, out Vector3 p2);
                float length = (Vector3.Distance(p1, p0) + Vector3.Distance(p2, p1) + Vector3.Distance(p2, p0)) * 0.5f;
                c.T += c.Template.WalkSpeed * dt / Mathf.Max(length, 0.1f);

                if (c.T >= 1f)
                {
                    c.Cell += Dir2D[c.OutDir];
                    c.InDir = c.OutDir;
                    c.T -= 1f;

                    if (!_view.Contains(c.Cell.x, c.Cell.y) || !IsRoad(c.Cell.x, c.Cell.y))
                    {
                        _citizens.RemoveAt(i);
                        _topologyDirty = true;
                        continue;
                    }

                    if (c.Route != null && ++c.RouteStep >= c.Route.Count)
                    {
                        // Arrived: the walker goes inside and frees a slot for a new trip.
                        _citizens.RemoveAt(i);
                        _topologyDirty = true;
                        continue;
                    }
                    c.OutDir = c.Route != null ? c.Route[c.RouteStep] : ChooseOutDir(c.Cell.x, c.Cell.y, c.InDir);
                    if (_rng.Next(100) < 12 && TryPickAdjacentBuildingYaw(c.Cell.x, c.Cell.y, out float yaw))
                    {
                        c.PauseTimer = 2.0f + (float)_rng.NextDouble() * 1.8f;
                        c.PauseYaw = yaw;
                    }
                }
            }

            RenderCitizens();
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
                            _candidates.Add(new CandidateCell { Cell = new Vector2Int(x, z), Group = DetectGroup(x, z) });
                    }
                    else if (cell.Type >= CellType.Residential && cell.Type <= CellType.Industrial && cell.Level > 0)
                    {
                        developed++;
                    }
                }
            }

            _targetCount = _candidates.Count < 2
                ? 0
                : Mathf.Clamp(developed * 2 + _candidates.Count / 4, 0, MaxCitizens);
        }

        private CitizenGroup DetectGroup(int x, int z)
        {
            for (int d = 0; d < 4; d++)
            {
                int nx = x + Dir2D[d].x;
                int nz = z + Dir2D[d].y;
                if (nx < 0 || nz < 0 || nx >= CitySimulation.GridSize || nz >= CitySimulation.GridSize) continue;
                CityCell c = _city.GetCell(nx, nz);
                if (c.Type == CellType.Commercial) return CitizenGroup.Commercial;
                if (c.Type == CellType.Industrial || c.Type == CellType.PowerPlant) return CitizenGroup.Industrial;
                if (c.Type == CellType.Residential) return CitizenGroup.Residential;
            }
            return CitizenGroup.Civic;
        }

        private bool TrySpawn()
        {
            if (_candidates.Count == 0 || _citizens.Count >= MaxCitizens) return false;

            CandidateCell candidate = _candidates[_rng.Next(_candidates.Count)];
            RoadConnections conns = _city.Roads.GetConnections(candidate.Cell.x, candidate.Cell.y);
            int count = 0;
            for (int d = 0; d < 4; d++)
            {
                if (((int)conns & (1 << d)) != 0)
                    _dirScratch[count++] = d;
            }
            if (count == 0) return false;

            List<int> route = PlanWalk(candidate.Cell);
            int outDir = route != null ? route[0] : _dirScratch[_rng.Next(count)];
            CitizenTemplate tpl = CitizenMeshFactory.PickForGroup(_rng, candidate.Group);
            _citizens.Add(new Citizen
            {
                Route = route,
                Cell = candidate.Cell,
                InDir = outDir,
                OutDir = outDir,
                SideSign = _rng.Next(2) == 0 ? 1 : -1,
                T = (float)_rng.NextDouble(),
                Phase = (float)_rng.NextDouble() * 6.283f,
                SeedPhase = (float)_rng.NextDouble() * 6.283f,
                Template = tpl
            });
            _topologyDirty = true;
            return true;
        }

        // Walk to nearby shops, workplaces or services; walks are short, so the search stops early.
        private List<int> PlanWalk(Vector2Int origin)
        {
            Vector2Int goal = origin;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                CandidateCell cand = _candidates[_rng.Next(_candidates.Count)];
                if (cand.Cell == origin) continue;
                goal = cand.Cell;
                if (cand.Group != CitizenGroup.Residential) break;
            }
            var route = new List<int>();
            return goal != origin && _city.Roads.TryFindPath(origin.x, origin.y, goal.x, goal.y, route, MaxWalkCells) &&
                route.Count > 0 ? route : null;
        }

        private void ValidateCitizens()
        {
            for (int i = _citizens.Count - 1; i >= 0; i--)
            {
                Citizen c = _citizens[i];
                if (!_view.Contains(c.Cell.x, c.Cell.y) || !IsRoad(c.Cell.x, c.Cell.y))
                {
                    _citizens.RemoveAt(i);
                    _topologyDirty = true;
                    continue;
                }

                RoadConnections conns = _city.Roads.GetConnections(c.Cell.x, c.Cell.y);
                c.Route = null; // the road network changed, so the planned walk may be cut; stroll instead
                if (((int)conns & (1 << c.OutDir)) == 0)
                    c.OutDir = ChooseOutDir(c.Cell.x, c.Cell.y, c.InDir);
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

        private bool TryPickAdjacentBuildingYaw(int x, int z, out float yaw)
        {
            for (int d = 0; d < 4; d++)
            {
                int nx = x + Dir2D[d].x;
                int nz = z + Dir2D[d].y;
                if (nx < 0 || nz < 0 || nx >= CitySimulation.GridSize || nz >= CitySimulation.GridSize) continue;
                CityCell c = _city.GetCell(nx, nz);
                if ((c.Type >= CellType.Residential && c.Type <= CellType.Industrial && c.Level > 0) ||
                    c.Type == CellType.PowerPlant)
                {
                    yaw = Mathf.Atan2(Dir3D[d].x, Dir3D[d].z) * Mathf.Rad2Deg;
                    return true;
                }
            }
            yaw = 0f;
            return false;
        }

        private void ComputeSidewalkCurve(Vector2Int cell, int inDir, int outDir, int sideSign, out Vector3 p0, out Vector3 p1, out Vector3 p2)
        {
            float half = _cellSize * 0.5f;
            float offset = sideSign * 0.39f * _cellSize;
            Vector3 center = new Vector3((cell.x + 0.5f) * _cellSize, CurbY, (cell.y + 0.5f) * _cellSize);

            Vector3 sideIn = Right3D[inDir] * offset;
            Vector3 sideOut = Right3D[outDir] * offset;
            p0 = center - Dir3D[inDir] * half + sideIn;
            p2 = center + Dir3D[outDir] * half + sideOut;

            if (inDir == outDir)
                p1 = (p0 + p2) * 0.5f;
            else if (outDir == (inDir ^ 1))
                p1 = center + sideIn * 0.4f;
            else
                p1 = center + sideIn + sideOut;
        }

        private void RenderCitizens()
        {
            int count = _citizens.Count;
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
                    CitizenTemplate tpl = _citizens[i].Template;
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
                    CitizenTemplate tpl = _citizens[i].Template;
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
                Citizen c = _citizens[i];
                CitizenTemplate tpl = c.Template;
                ComputeSidewalkCurve(c.Cell, c.InDir, c.OutDir, c.SideSign, out Vector3 p0, out Vector3 p1, out Vector3 p2);

                float u = 1f - c.T;
                Vector3 pos = u * u * p0 + 2f * u * c.T * p1 + c.T * c.T * p2;
                Vector3 tangent = 2f * u * (p1 - p0) + 2f * c.T * (p2 - p1);
                if (tangent.sqrMagnitude < 1e-6f) tangent = p2 - p0;
                CityChunkRenderer.ApplyRoadGrade(_city, c.Cell, _cellSize, ref pos, ref tangent);

                Quaternion rootRot = c.PauseTimer > 0f
                    ? Quaternion.Euler(0f, c.PauseYaw, 0f)
                    : (tangent.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(tangent.normalized, Vector3.up) : Quaternion.identity);

                float bobY;
                float torsoPitch = tpl.BaseTorsoPitch;
                float headYaw = 0f;
                float headPitch = 0f;
                float leftLegDeg = 0f;
                float rightLegDeg = 0f;
                float leftArmDeg = 0f;
                float rightArmDeg = 0f;

                if (c.PauseTimer > 0f)
                {
                    bobY = Mathf.Sin(_animTime * 2.6f + c.SeedPhase) * 0.006f;
                    headYaw = Mathf.Sin(_animTime * 2.0f + c.SeedPhase) * 20f;
                    rightArmDeg = tpl.UsesCane
                        ? -10f
                        : -35f + Mathf.Sin(_animTime * 5.5f + c.SeedPhase) * 20f;
                }
                else
                {
                    float stride = Mathf.Sin(c.Phase);
                    bobY = Mathf.Abs(Mathf.Cos(c.Phase)) * 0.022f;
                    headPitch = Mathf.Abs(stride) * 2.5f;
                    headYaw = Mathf.Sin(c.Phase * 0.5f) * 4f;
                    leftLegDeg = stride * tpl.LegSwingDeg;
                    rightLegDeg = -stride * tpl.LegSwingDeg;
                    leftArmDeg = -stride * tpl.ArmSwingDeg;
                    rightArmDeg = tpl.UsesCane
                        ? -14f + stride * 18f
                        : stride * tpl.ArmSwingDeg * tpl.RightArmSwingScale;
                }

                Vector3 bob = new Vector3(0f, bobY, 0f);
                Quaternion torsoRot = rootRot * Quaternion.Euler(torsoPitch, 0f, 0f);
                Quaternion headRot = torsoRot * Quaternion.Euler(headPitch, headYaw, 0f);
                Quaternion leftArmRot = torsoRot * Quaternion.Euler(leftArmDeg, 0f, 0f);
                Quaternion rightArmRot = torsoRot * Quaternion.Euler(rightArmDeg, 0f, 0f);
                Quaternion leftLegRot = rootRot * Quaternion.Euler(leftLegDeg, 0f, 0f);
                Quaternion rightLegRot = rootRot * Quaternion.Euler(rightLegDeg, 0f, 0f);

                Vector3[] pivots = tpl.Pivots;
                Vector3 hipPivot = pivots[CitizenMeshFactory.PartTorso];
                Vector3 neckOffset = torsoRot * (pivots[CitizenMeshFactory.PartHead] - hipPivot);
                Vector3 lShoulderOffset = torsoRot * (pivots[CitizenMeshFactory.PartLeftArm] - hipPivot);
                Vector3 rShoulderOffset = torsoRot * (pivots[CitizenMeshFactory.PartRightArm] - hipPivot);

                Vector3 torsoWorldPivot = pos + bob + rootRot * hipPivot;
                Vector3 headWorldPivot = torsoWorldPivot + neckOffset;
                Vector3 lArmWorldPivot = torsoWorldPivot + lShoulderOffset;
                Vector3 rArmWorldPivot = torsoWorldPivot + rShoulderOffset;
                Vector3 lLegWorldPivot = pos + bob + rootRot * pivots[CitizenMeshFactory.PartLeftLeg];
                Vector3 rLegWorldPivot = pos + bob + rootRot * pivots[CitizenMeshFactory.PartRightLeg];

                Vector3[] srcV = tpl.Vertices;
                Vector3[] srcN = tpl.Normals;
                byte[] partIds = tpl.PartIds;

                for (int k = 0; k < srcV.Length; k++)
                {
                    byte partId = partIds[k];
                    switch (partId)
                    {
                        case CitizenMeshFactory.PartTorso:
                            _renderVertices[vertCursor] = torsoWorldPivot + torsoRot * (srcV[k] - hipPivot);
                            _renderNormals[vertCursor] = torsoRot * srcN[k];
                            break;
                        case CitizenMeshFactory.PartHead:
                            _renderVertices[vertCursor] = headWorldPivot + headRot * (srcV[k] - pivots[CitizenMeshFactory.PartHead]);
                            _renderNormals[vertCursor] = headRot * srcN[k];
                            break;
                        case CitizenMeshFactory.PartLeftArm:
                            _renderVertices[vertCursor] = lArmWorldPivot + leftArmRot * (srcV[k] - pivots[CitizenMeshFactory.PartLeftArm]);
                            _renderNormals[vertCursor] = leftArmRot * srcN[k];
                            break;
                        case CitizenMeshFactory.PartRightArm:
                            _renderVertices[vertCursor] = rArmWorldPivot + rightArmRot * (srcV[k] - pivots[CitizenMeshFactory.PartRightArm]);
                            _renderNormals[vertCursor] = rightArmRot * srcN[k];
                            break;
                        case CitizenMeshFactory.PartLeftLeg:
                            _renderVertices[vertCursor] = lLegWorldPivot + leftLegRot * (srcV[k] - pivots[CitizenMeshFactory.PartLeftLeg]);
                            _renderNormals[vertCursor] = leftLegRot * srcN[k];
                            break;
                        case CitizenMeshFactory.PartRightLeg:
                            _renderVertices[vertCursor] = rLegWorldPivot + rightLegRot * (srcV[k] - pivots[CitizenMeshFactory.PartRightLeg]);
                            _renderNormals[vertCursor] = rightLegRot * srcN[k];
                            break;
                        default: // GroundShadow
                            _renderVertices[vertCursor] = pos + rootRot * srcV[k];
                            _renderNormals[vertCursor] = srcN[k];
                            break;
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
