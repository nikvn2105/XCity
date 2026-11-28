using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace XCity
{
    internal sealed class IndustrialSmokeSystem : IDisposable
    {
        private const int PuffsPerEmitter = 5;
        private const int PuffVertexCount = 14;
        private const int PuffTriangleCount = 72; // 24 tris * 3

        private static readonly Vector3[] UnitVerts;
        private static readonly Vector3[] UnitNormals;
        private static readonly int[] UnitTris;

        private static readonly Color32 SteamTop = new Color32(244, 248, 252, 255);
        private static readonly Color32 SteamBottom = new Color32(214, 222, 232, 255);
        private static readonly Color32 SmokeTop = new Color32(150, 154, 160, 255);
        private static readonly Color32 SmokeBottom = new Color32(116, 120, 128, 255);

        static IndustrialSmokeSystem()
        {
            UnitVerts = new Vector3[PuffVertexCount];
            UnitNormals = new Vector3[PuffVertexCount];

            UnitVerts[0] = new Vector3(0f, 1f, 0f);
            UnitNormals[0] = Vector3.up;

            for (int i = 0; i < 6; i++)
            {
                float a = i * (Mathf.PI * 2f / 6f);
                float c = Mathf.Cos(a) * 0.85f;
                float s = Mathf.Sin(a) * 0.85f;
                Vector3 up = new Vector3(c, 0.35f, s);
                Vector3 dn = new Vector3(c, -0.35f, s);
                UnitVerts[1 + i] = up;
                UnitNormals[1 + i] = up.normalized;
                UnitVerts[7 + i] = dn;
                UnitNormals[7 + i] = dn.normalized;
            }

            UnitVerts[13] = new Vector3(0f, -1f, 0f);
            UnitNormals[13] = Vector3.down;

            UnitTris = new int[PuffTriangleCount];
            int t = 0;
            for (int i = 0; i < 6; i++)
            {
                int next = (i + 1) % 6;
                // Top cap
                UnitTris[t++] = 0;
                UnitTris[t++] = 1 + next;
                UnitTris[t++] = 1 + i;
                // Middle belt
                UnitTris[t++] = 7 + i;
                UnitTris[t++] = 1 + i;
                UnitTris[t++] = 1 + next;

                UnitTris[t++] = 7 + i;
                UnitTris[t++] = 1 + next;
                UnitTris[t++] = 7 + next;
                // Bottom cap
                UnitTris[t++] = 13;
                UnitTris[t++] = 7 + i;
                UnitTris[t++] = 7 + next;
            }
        }

        private struct Emitter
        {
            public Vector3 Origin;
            public float Phase;
            public bool IsSteam;
        }

        private readonly CitySimulation _city;
        private readonly ConstructionSystem _construction;
        private readonly float _cellSize;
        private readonly List<Emitter> _emitters = new List<Emitter>(32);

        private readonly GameObject _go;
        private readonly Mesh _mesh;
        private readonly Bounds _mapBounds;

        private Vector3[] _renderVertices = Array.Empty<Vector3>();
        private Vector3[] _renderNormals = Array.Empty<Vector3>();
        private Color32[] _renderColors = Array.Empty<Color32>();
        private int[] _renderTriangles = Array.Empty<int>();

        private bool _emittersDirty = true;
        private bool _topologyDirty = true;
        private int _lastConstructionCount = -1;
        private float _time;

        public IndustrialSmokeSystem(
            CitySimulation city,
            ConstructionSystem construction,
            float cellSize,
            Material material,
            Transform parent)
        {
            _city = city;
            _construction = construction;
            _cellSize = cellSize;

            float span = CitySimulation.GridSize * cellSize;
            _mapBounds = new Bounds(new Vector3(span * 0.5f, 4f, span * 0.5f), new Vector3(span, 12f, span));

            _mesh = new Mesh { name = "IndustrialPlumes" };
            _mesh.MarkDynamic();

            _go = new GameObject("IndustrialPlumes");
            _go.transform.SetParent(parent, false);
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            MeshRenderer renderer = _go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            _city.CellChanged += OnCellChanged;
        }

        private void OnCellChanged(int x, int z) => _emittersDirty = true;

        public void Prewarm(float seconds) => Update(seconds);

        private WorldView _view;

        public void SetView(WorldView view)
        {
            if (_view.SameAnchor(view)) return;
            _view = view;
            _emittersDirty = true;
        }

        public void SetVisible(bool visible)
        {
            if (_go.activeSelf != visible) _go.SetActive(visible);
        }

        public void Update(float dt)
        {
            if (dt > 0f) _time += dt;

            int activeSites = _construction != null ? _construction.ActiveSiteCount : 0;
            if (activeSites != _lastConstructionCount)
            {
                _lastConstructionCount = activeSites;
                _emittersDirty = true;
            }

            if (_emittersDirty)
            {
                RebuildEmitters();
                _emittersDirty = false;
            }

            int totalPuffs = _emitters.Count * PuffsPerEmitter;
            if (totalPuffs == 0)
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
                int totalVerts = totalPuffs * PuffVertexCount;
                int totalTris = totalPuffs * PuffTriangleCount;
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
                for (int e = 0; e < _emitters.Count; e++)
                {
                    bool steam = _emitters[e].IsSteam;
                    Color32 topC = steam ? SteamTop : SmokeTop;
                    Color32 botC = steam ? SteamBottom : SmokeBottom;
                    for (int p = 0; p < PuffsPerEmitter; p++)
                    {
                        Array.Copy(UnitNormals, 0, _renderNormals, vOffset, PuffVertexCount);
                        _renderColors[vOffset] = topC;
                        for (int k = 1; k <= 6; k++) _renderColors[vOffset + k] = topC;
                        for (int k = 7; k <= 12; k++) _renderColors[vOffset + k] = botC;
                        _renderColors[vOffset + 13] = botC;

                        for (int k = 0; k < PuffTriangleCount; k++)
                            _renderTriangles[tOffset + k] = UnitTris[k] + vOffset;

                        vOffset += PuffVertexCount;
                        tOffset += PuffTriangleCount;
                    }
                }
            }

            int vertCursor = 0;
            for (int e = 0; e < _emitters.Count; e++)
            {
                Emitter em = _emitters[e];
                float rate = em.IsSteam ? 0.34f : 0.42f;
                float riseMax = em.IsSteam ? 2.5f : 1.95f;
                float driftMax = em.IsSteam ? 0.85f : 0.95f;
                float radiusMax = em.IsSteam ? 0.92f : 0.36f;

                for (int p = 0; p < PuffsPerEmitter; p++)
                {
                    float age = Mathf.Repeat(_time * rate + em.Phase + p * 0.2f, 1f);
                    float y = age * riseMax;
                    float dx = age * age * driftMax + Mathf.Sin(_time * 1.8f + em.Phase + p) * 0.06f;
                    float dz = age * age * 0.55f;
                    float grow = Mathf.Sin(Mathf.Pow(age, 0.65f) * Mathf.PI);
                    float radius = radiusMax * grow;
                    float squashY = radius * 0.68f;
                    Vector3 center = new Vector3(em.Origin.x + dx, em.Origin.y + y, em.Origin.z + dz);

                    for (int k = 0; k < PuffVertexCount; k++)
                    {
                        Vector3 uv = UnitVerts[k];
                        _renderVertices[vertCursor++] = new Vector3(
                            center.x + uv.x * radius,
                            center.y + uv.y * squashY,
                            center.z + uv.z * radius);
                    }
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
                _mesh.bounds = _mapBounds;
            }
        }

        private void RebuildEmitters()
        {
            _emitters.Clear();
            for (int x = _view.MinX; x <= _view.MaxX; x++)
            {
                for (int z = _view.MinZ; z <= _view.MaxZ; z++)
                {
                    if (_construction != null && _construction.IsSiteActive(x, z)) continue;
                    CityCell cell = _city.GetCell(x, z);
                    if (cell.Level <= 0 || (cell.Model == BuildingModel.Incinerator &&
                        _city.GetZoneStatus(x, z) != ZoneStatus.Developed)) continue;

                    int quarterTurns = BuildingChunkRenderer.PickQuarterTurns(_city, x, z);
                    if (IndustrialMeshFactory.TryGetPlumeEmitter(x, z, cell, quarterTurns, _cellSize,
                            out Vector3 worldPos, out bool isSteam))
                    {
                        _emitters.Add(new Emitter
                        {
                            Origin = worldPos,
                            Phase = (BuildingMeshFactory.Hash(x, z, 881) & 255u) / 255f,
                            IsSteam = isSteam
                        });
                    }
                }
            }
            _topologyDirty = true;
        }

        public void Dispose()
        {
            _city.CellChanged -= OnCellChanged;
            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
            if (_go != null) UnityEngine.Object.Destroy(_go);
        }
    }
}
