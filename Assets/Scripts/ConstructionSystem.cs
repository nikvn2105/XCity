using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace XCity
{
    internal sealed class ConstructionSystem : IDisposable
    {
        private const float BuildDuration = 3.2f;

        private static readonly Color32 DirtSlab = new Color32(158, 148, 132, 255);
        private static readonly Color32 ConcreteCore = new Color32(175, 178, 182, 255);
        private static readonly Color32 ConcreteSlab = new Color32(198, 200, 202, 255);
        private static readonly Color32 RebarSteel = new Color32(82, 86, 92, 255);
        private static readonly Color32 ScaffoldMetal = new Color32(190, 178, 154, 255);
        private static readonly Color32 SafetyNet = new Color32(58, 148, 152, 255);
        private static readonly Color32 CraneYellow = new Color32(236, 178, 36, 255);
        private static readonly Color32 CraneCab = new Color32(242, 242, 238, 255);
        private static readonly Color32 BrickRed = new Color32(182, 78, 58, 255);
        private static readonly Color32 TimberTan = new Color32(168, 124, 76, 255);
        private static readonly Color32 BarrierOrange = new Color32(235, 112, 36, 255);
        private static readonly Color32 WorkerVest = new Color32(238, 132, 34, 255);
        private static readonly Color32 WorkerPants = new Color32(48, 76, 126, 255);
        private static readonly Color32 WorkerSkin = new Color32(228, 184, 148, 255);
        private static readonly Color32 HardHatYellow = new Color32(246, 206, 44, 255);

        private sealed class Site
        {
            public int X;
            public int Z;
            public float Progress;
            public CityCell TargetCell;
            public int QuarterTurns;
            public float Phase;
        }

        private readonly CitySimulation _city;
        private readonly BuildingChunkRenderer _buildingRenderer;
        private readonly float _cellSize;
        private readonly CellType[,] _knownTypes = new CellType[CitySimulation.GridSize, CitySimulation.GridSize];
        private readonly int[,] _knownLevels = new int[CitySimulation.GridSize, CitySimulation.GridSize];
        private readonly bool[,] _activeMap = new bool[CitySimulation.GridSize, CitySimulation.GridSize];
        private readonly List<Site> _sites = new List<Site>(32);
        private readonly MeshBuilder _builder = new MeshBuilder();

        private readonly GameObject _go;
        private readonly Mesh _mesh;
        private float _animTime;
        private bool _meshDirty;

        public ConstructionSystem(
            CitySimulation city,
            BuildingChunkRenderer buildingRenderer,
            float cellSize,
            Material material,
            Transform parent)
        {
            _city = city;
            _buildingRenderer = buildingRenderer;
            _cellSize = cellSize;

            for (int x = 0; x < CitySimulation.GridSize; x++)
            {
                for (int z = 0; z < CitySimulation.GridSize; z++)
                {
                    CityCell cell = _city.GetCell(x, z);
                    _knownTypes[x, z] = cell.Type;
                    _knownLevels[x, z] = cell.Level;
                }
            }

            _mesh = new Mesh { name = "ConstructionSites" };
            _mesh.MarkDynamic();

            _go = new GameObject("ConstructionSites");
            _go.transform.SetParent(parent, false);
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            MeshRenderer renderer = _go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;

            _buildingRenderer.SetConstructionFilter(IsUnderConstruction);
            _city.CellChanged += OnCellChanged;
        }

        private bool IsUnderConstruction(int x, int z) => _activeMap[x, z];

        private void OnCellChanged(int x, int z)
        {
            CellType prevType = _knownTypes[x, z];
            int prevLevel = _knownLevels[x, z];
            CityCell cell = _city.GetCell(x, z);
            _knownTypes[x, z] = cell.Type;
            _knownLevels[x, z] = cell.Level;

            bool built = (IsZone(cell.Type) && cell.Level > 0 && (cell.Type != prevType || cell.Level > prevLevel)) ||
                         (cell.Type == CellType.PowerPlant && prevType != CellType.PowerPlant);

            if (built)
            {
                StartOrResetSite(x, z, cell, 0f);
                return;
            }

            if (_activeMap[x, z])
            {
                RemoveSiteAt(x, z);
                _buildingRenderer.RefreshCell(x, z);
            }
        }

        private void StartOrResetSite(int x, int z, CityCell cell, float initialProgress)
        {
            Site existing = null;
            for (int i = 0; i < _sites.Count; i++)
            {
                if (_sites[i].X == x && _sites[i].Z == z)
                {
                    existing = _sites[i];
                    break;
                }
            }

            if (existing == null)
            {
                existing = new Site { X = x, Z = z };
                _sites.Add(existing);
            }

            existing.Progress = initialProgress;
            existing.TargetCell = cell;
            existing.QuarterTurns = BuildingChunkRenderer.PickQuarterTurns(_city, x, z);
            existing.Phase = (BuildingMeshFactory.Hash(x, z, 31) & 255u) / 255f * 6.283f;

            _activeMap[x, z] = true;
            _meshDirty = true;
            _buildingRenderer.RefreshCell(x, z);
        }

        private void RemoveSiteAt(int x, int z)
        {
            _activeMap[x, z] = false;
            for (int i = _sites.Count - 1; i >= 0; i--)
            {
                if (_sites[i].X == x && _sites[i].Z == z)
                {
                    _sites.RemoveAt(i);
                    break;
                }
            }
            _meshDirty = true;
        }

        public void SeedPreviewSites()
        {
            var anchors = new (int X, int Z, float Progress)[]
            {
                (7, 7, 0.28f),
                (20, 8, 0.52f),
                (8, 22, 0.72f),
                (14, 10, 0.40f)
            };

            for (int i = 0; i < anchors.Length; i++)
            {
                var anchor = anchors[i];
                if (TryFindDevelopedNear(anchor.X, anchor.Z, out int sx, out int sz, out CityCell cell))
                    StartOrResetSite(sx, sz, cell, anchor.Progress);
            }
        }

        private bool TryFindDevelopedNear(int ax, int az, out int fx, out int fz, out CityCell found)
        {
            for (int r = 0; r <= 3; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dz = -r; dz <= r; dz++)
                    {
                        int x = ax + dx;
                        int z = az + dz;
                        if (x < 0 || z < 0 || x >= CitySimulation.GridSize || z >= CitySimulation.GridSize) continue;
                        if (_activeMap[x, z]) continue;
                        CityCell c = _city.GetCell(x, z);
                        if ((IsZone(c.Type) && c.Level > 0) || c.Type == CellType.PowerPlant)
                        {
                            fx = x;
                            fz = z;
                            found = c;
                            return true;
                        }
                    }
                }
            }

            fx = 0;
            fz = 0;
            found = default;
            return false;
        }

        public void Update(float dt)
        {
            if (dt > 0f) _animTime += dt;

            if (_sites.Count == 0)
            {
                if (_meshDirty)
                {
                    _mesh.Clear();
                    _meshDirty = false;
                }
                return;
            }

            if (dt > 0f)
            {
                for (int i = _sites.Count - 1; i >= 0; i--)
                {
                    Site s = _sites[i];
                    s.Progress += dt / BuildDuration;
                    if (s.Progress >= 1f)
                    {
                        _activeMap[s.X, s.Z] = false;
                        _sites.RemoveAt(i);
                        _buildingRenderer.RefreshCell(s.X, s.Z);
                    }
                }
            }

            if (_sites.Count == 0)
            {
                _mesh.Clear();
                _meshDirty = false;
                return;
            }

            _builder.Clear();
            for (int i = 0; i < _sites.Count; i++)
                BuildSiteMesh(_sites[i]);

            _builder.ApplyTo(_mesh);
            _meshDirty = true;
        }

        private void BuildSiteMesh(Site s)
        {
            Vector3 origin = new Vector3(
                (s.X + 0.5f) * _cellSize,
                CityChunkRenderer.BuildingBase,
                (s.Z + 0.5f) * _cellSize);
            _builder.SetFrame(origin, s.QuarterTurns);

            const float baseY = 0.04f;
            _builder.AddBox(new Vector3(0f, 0.02f, 0f), new Vector3(3.5f, 0.04f, 3.5f), DirtSlab, DirtSlab);

            float targetH = Mathf.Max(2.2f, s.TargetCell.Height);
            float t = Mathf.Clamp01(s.Progress);
            float curH = Mathf.Max(0.45f, targetH * Mathf.SmoothStep(0.12f, 0.96f, t));

            // Concrete core and corner columns with rebar tips
            _builder.AddBox(new Vector3(0f, baseY + curH * 0.5f, 0f), new Vector3(1.5f, curH, 1.5f), ConcreteCore, ConcreteSlab);

            float colH = curH + 0.35f;
            float colY = baseY + colH * 0.5f;
            for (int cx = -1; cx <= 1; cx += 2)
            {
                for (int cz = -1; cz <= 1; cz += 2)
                {
                    float px = cx * 1.05f;
                    float pz = cz * 1.05f;
                    _builder.AddBox(new Vector3(px, colY, pz), new Vector3(0.16f, colH, 0.16f), ConcreteCore, ConcreteSlab);
                    _builder.AddBox(new Vector3(px, baseY + colH + 0.12f, pz), new Vector3(0.05f, 0.24f, 0.05f), RebarSteel, RebarSteel);
                }
            }

            // Horizontal floor slabs every 0.85 units
            for (float sy = 0.85f; sy <= curH; sy += 0.85f)
                _builder.AddBox(new Vector3(0f, baseY + sy, 0f), new Vector3(2.3f, 0.08f, 2.3f), ConcreteSlab, ConcreteSlab);

            // Perimeter scaffolding & safety netting
            float scaffH = t < 0.88f ? curH + 0.25f : Mathf.Max(0.25f, curH * (1f - (t - 0.88f) * 5f));
            for (int sx = -1; sx <= 1; sx++)
            {
                for (int sz = -1; sz <= 1; sz++)
                {
                    if (sx == 0 && sz == 0) continue;
                    _builder.AddBox(new Vector3(sx * 1.30f, baseY + scaffH * 0.5f, sz * 1.30f),
                        new Vector3(0.06f, scaffH, 0.06f), ScaffoldMetal, ScaffoldMetal);
                }
            }
            for (float ly = 0.85f; ly <= scaffH; ly += 0.85f)
            {
                _builder.AddBox(new Vector3(0f, baseY + ly, 1.30f), new Vector3(2.64f, 0.05f, 0.05f), ScaffoldMetal, ScaffoldMetal);
                _builder.AddBox(new Vector3(0f, baseY + ly, -1.30f), new Vector3(2.64f, 0.05f, 0.05f), ScaffoldMetal, ScaffoldMetal);
                _builder.AddBox(new Vector3(1.30f, baseY + ly, 0f), new Vector3(0.05f, 0.05f, 2.64f), ScaffoldMetal, ScaffoldMetal);
                _builder.AddBox(new Vector3(-1.30f, baseY + ly, 0f), new Vector3(0.05f, 0.05f, 2.64f), ScaffoldMetal, ScaffoldMetal);
            }

            if (scaffH > 0.6f)
            {
                float netH = scaffH * 0.65f;
                _builder.AddBox(new Vector3(0f, baseY + netH * 0.5f + 0.15f, 1.31f),
                    new Vector3(2.45f, netH, 0.03f), SafetyNet, SafetyNet);
                _builder.AddBox(new Vector3(1.31f, baseY + netH * 0.5f + 0.15f, 0f),
                    new Vector3(0.03f, netH, 2.45f), SafetyNet, SafetyNet);
            }

            // Front-corner construction props (bricks, timber, mixer, barrier)
            _builder.AddBox(new Vector3(1.15f, baseY + 0.15f, 1.48f), new Vector3(0.55f, 0.30f, 0.38f), BrickRed, BrickRed);
            _builder.AddBox(new Vector3(0.45f, baseY + 0.10f, 1.50f), new Vector3(0.65f, 0.20f, 0.32f), TimberTan, TimberTan);
            _builder.AddBox(new Vector3(-0.95f, baseY + 0.24f, 1.45f), new Vector3(0.42f, 0.48f, 0.42f), CraneYellow, RebarSteel);
            _builder.AddBox(new Vector3(-0.25f, baseY + 0.16f, 1.62f), new Vector3(0.80f, 0.32f, 0.10f), BarrierOrange, CraneCab);

            // Animated Tower Crane at back-left corner
            float mastH = targetH + 1.15f;
            Vector3 mastBase = new Vector3(-1.35f, baseY, -1.35f);
            _builder.AddBox(new Vector3(mastBase.x, baseY + mastH * 0.5f, mastBase.z),
                new Vector3(0.22f, mastH, 0.22f), CraneYellow, CraneYellow);
            _builder.AddBox(new Vector3(mastBase.x + 0.16f, baseY + mastH - 0.12f, mastBase.z),
                new Vector3(0.28f, 0.32f, 0.28f), CraneCab, CraneYellow);

            float jibYaw = s.Phase * Mathf.Rad2Deg + Mathf.Sin(_animTime * 1.35f + s.Phase) * 50f;
            Vector3 jibPivot = new Vector3(mastBase.x, baseY + mastH + 0.10f, mastBase.z);
            Quaternion jibRot = Quaternion.Euler(0f, jibYaw, 0f);

            // Main jib boom (+Z in jib space) and counterweight arm (-Z)
            AddRotatedBox(jibPivot, jibRot, new Vector3(0f, 0f, 1.05f), new Vector3(0.16f, 0.16f, 2.40f), CraneYellow, CraneYellow);
            AddRotatedBox(jibPivot, jibRot, new Vector3(0f, 0.22f, 0f), new Vector3(0.14f, 0.34f, 0.14f), CraneYellow, CraneYellow);
            AddRotatedBox(jibPivot, jibRot, new Vector3(0f, 0f, -0.45f), new Vector3(0.16f, 0.14f, 0.75f), CraneYellow, CraneYellow);
            AddRotatedBox(jibPivot, jibRot, new Vector3(0f, -0.06f, -0.72f), new Vector3(0.32f, 0.26f, 0.34f), ConcreteCore, RebarSteel);

            // Sliding trolley & oscillating hook/load
            float trolleyDist = 0.70f + (0.5f + 0.5f * Mathf.Sin(_animTime * 0.9f + s.Phase)) * 1.30f;
            float cableLen = 0.45f + (0.5f + 0.5f * Mathf.Cos(_animTime * 1.6f + s.Phase)) * 0.65f;
            AddRotatedBox(jibPivot, jibRot, new Vector3(0f, -0.10f, trolleyDist), new Vector3(0.22f, 0.10f, 0.24f), RebarSteel, RebarSteel);
            AddRotatedBox(jibPivot, jibRot, new Vector3(0f, -0.15f - cableLen * 0.5f, trolleyDist), new Vector3(0.03f, cableLen, 0.03f), RebarSteel, RebarSteel);

            Vector3 hookPivot = jibPivot + jibRot * new Vector3(0f, -0.15f - cableLen, trolleyDist);
            float loadSwing = Mathf.Sin(_animTime * 2.4f + s.Phase) * 8f;
            Quaternion loadRot = jibRot * Quaternion.Euler(0f, 0f, loadSwing);
            AddRotatedBox(hookPivot, loadRot, new Vector3(0f, -0.10f, 0f), new Vector3(0.55f, 0.12f, 0.16f), TimberTan, RebarSteel);

            // Two animated on-site construction workers
            AddAnimatedWorker(new Vector3(0.55f, baseY, 0.95f), 25f, 0);
            AddAnimatedWorker(new Vector3(-0.45f, baseY + Mathf.Min(curH, 0.85f), 0.70f), -40f, 1);
        }

        private void AddAnimatedWorker(Vector3 pos, float facingYaw, int workerIndex)
        {
            Quaternion rootRot = Quaternion.Euler(0f, facingYaw, 0f);
            float hammer = Mathf.Sin(_animTime * 7f + workerIndex * 2f);
            float torsoPitch = 8f + hammer * 6f;
            Quaternion torsoRot = rootRot * Quaternion.Euler(torsoPitch, 0f, 0f);
            Quaternion armRot = rootRot * Quaternion.Euler(-45f + hammer * 35f, 0f, 0f);

            // Legs
            AddRotatedBox(pos, rootRot, new Vector3(-0.045f, 0.11f, 0f), new Vector3(0.07f, 0.22f, 0.08f), WorkerPants, WorkerPants);
            AddRotatedBox(pos, rootRot, new Vector3(0.045f, 0.11f, 0f), new Vector3(0.07f, 0.22f, 0.08f), WorkerPants, WorkerPants);
            // Torso + vest
            Vector3 hip = pos + Vector3.up * 0.22f;
            AddRotatedBox(hip, torsoRot, new Vector3(0f, 0.11f, 0f), new Vector3(0.19f, 0.22f, 0.11f), WorkerVest, WorkerVest);
            // Head + hard hat
            Vector3 neck = hip + torsoRot * new Vector3(0f, 0.22f, 0f);
            AddRotatedBox(neck, torsoRot, new Vector3(0f, 0.06f, 0f), new Vector3(0.13f, 0.12f, 0.13f), WorkerSkin, WorkerSkin);
            AddRotatedBox(neck, torsoRot, new Vector3(0f, 0.13f, 0f), new Vector3(0.16f, 0.06f, 0.16f), HardHatYellow, HardHatYellow);
            // Hammering right arm
            Vector3 rShoulder = hip + torsoRot * new Vector3(0.13f, 0.19f, 0f);
            AddRotatedBox(rShoulder, armRot, new Vector3(0f, -0.09f, 0f), new Vector3(0.06f, 0.18f, 0.06f), WorkerSkin, WorkerVest);
        }

        private void AddRotatedBox(Vector3 pivot, Quaternion rot, Vector3 localCenter, Vector3 size, Color32 side, Color32 top)
        {
            float hx = size.x * 0.5f;
            float hy = size.y * 0.5f;
            float hz = size.z * 0.5f;

            Vector3 c000 = pivot + rot * (localCenter + new Vector3(-hx, -hy, -hz));
            Vector3 c100 = pivot + rot * (localCenter + new Vector3(hx, -hy, -hz));
            Vector3 c101 = pivot + rot * (localCenter + new Vector3(hx, -hy, hz));
            Vector3 c001 = pivot + rot * (localCenter + new Vector3(-hx, -hy, hz));
            Vector3 c010 = pivot + rot * (localCenter + new Vector3(-hx, hy, -hz));
            Vector3 c110 = pivot + rot * (localCenter + new Vector3(hx, hy, -hz));
            Vector3 c111 = pivot + rot * (localCenter + new Vector3(hx, hy, hz));
            Vector3 c011 = pivot + rot * (localCenter + new Vector3(-hx, hy, hz));

            _builder.AddQuad(c001, c101, c111, c011, rot * Vector3.forward, side);
            _builder.AddQuad(c100, c000, c010, c110, rot * Vector3.back, side);
            _builder.AddQuad(c101, c100, c110, c111, rot * Vector3.right, side);
            _builder.AddQuad(c000, c001, c011, c010, rot * Vector3.left, side);
            _builder.AddQuad(c011, c111, c110, c010, rot * Vector3.up, top);
        }

        private static bool IsZone(CellType type) =>
            type >= CellType.Residential && type <= CellType.Industrial;

        public void Dispose()
        {
            _city.CellChanged -= OnCellChanged;
            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
            if (_go != null) UnityEngine.Object.Destroy(_go);
        }
    }
}
