using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace XCity
{
    public enum CityOverlayMode
    {
        None,
        Power,
        Services,
        Education,
        Environment
    }

    internal sealed class CityOverlaySystem : IDisposable
    {
        private const float OverlayY = 0.135f;

        private readonly CitySimulation _city;
        private readonly float _cellSize;
        private readonly MeshBuilder _builder = new MeshBuilder();
        private readonly List<Vector2Int> _serviceSites = new List<Vector2Int>(32);
        private readonly List<Vector2Int> _eduSites = new List<Vector2Int>(32);
        private readonly List<Vector2Int> _polluters = new List<Vector2Int>(64);

        private readonly GameObject _go;
        private readonly Mesh _mesh;

        private WorldView _view;
        private bool _dirty = true;
        private long _lastTick = -1;

        public CityOverlayMode Mode { get; private set; } = CityOverlayMode.None;

        public CityOverlaySystem(CitySimulation city, float cellSize, Material material, Transform parent)
        {
            _city = city;
            _cellSize = cellSize;

            _mesh = new Mesh { name = "CityOverlay" };
            _mesh.MarkDynamic();

            _go = new GameObject("CityOverlay");
            _go.transform.SetParent(parent, false);
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            MeshRenderer renderer = _go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _go.SetActive(false);

            _city.CellChanged += OnCellChanged;
        }

        private void OnCellChanged(int x, int z) => _dirty = true;

        public void SetMode(CityOverlayMode mode)
        {
            if (Mode == mode) return;
            Mode = mode;
            _dirty = true;
            _go.SetActive(mode != CityOverlayMode.None);
            if (mode == CityOverlayMode.None)
                _mesh.Clear();
        }

        public void CycleMode()
        {
            CityOverlayMode next = Mode switch
            {
                CityOverlayMode.None => CityOverlayMode.Power,
                CityOverlayMode.Power => CityOverlayMode.Services,
                CityOverlayMode.Services => CityOverlayMode.Education,
                CityOverlayMode.Education => CityOverlayMode.Environment,
                _ => CityOverlayMode.None
            };
            SetMode(next);
        }

        public string GetModeLabel() => Mode switch
        {
            CityOverlayMode.Power => "Điện",
            CityOverlayMode.Services => "Dịch vụ",
            CityOverlayMode.Education => "Giáo dục",
            CityOverlayMode.Environment => "Môi trường",
            _ => "Tắt"
        };

        public void SetView(WorldView view)
        {
            if (_view.SameAnchor(view)) return;
            _view = view;
            _dirty = true;
        }

        public void Update()
        {
            if (Mode == CityOverlayMode.None) return;
            if (_lastTick != _city.TickCount)
            {
                _lastTick = _city.TickCount;
                _dirty = true;
            }
            if (!_dirty) return;
            Rebuild();
        }

        private void Rebuild()
        {
            _dirty = false;
            _builder.Clear();
            _serviceSites.Clear();
            _eduSites.Clear();
            _polluters.Clear();

            int scanMinX = Mathf.Max(0, _view.MinX - 18);
            int scanMaxX = Mathf.Min(CitySimulation.GridSize - 1, _view.MaxX + 18);
            int scanMinZ = Mathf.Max(0, _view.MinZ - 18);
            int scanMaxZ = Mathf.Min(CitySimulation.GridSize - 1, _view.MaxZ + 18);

            if (Mode == CityOverlayMode.Services || Mode == CityOverlayMode.Education || Mode == CityOverlayMode.Environment)
            {
                foreach (int idx in _city.OccupiedIndices)
                {
                    int x = idx / CitySimulation.GridSize;
                    int z = idx % CitySimulation.GridSize;
                    if (x < scanMinX || x > scanMaxX || z < scanMinZ || z > scanMaxZ) continue;
                    CityCell cell = _city.GetCell(x, z);
                    if (cell.Model == BuildingModel.FireStation || cell.Model == BuildingModel.Hospital || cell.Model == BuildingModel.PoliceStation)
                        _serviceSites.Add(new Vector2Int(x, z));
                    else if (cell.Type == CellType.School)
                        _eduSites.Add(new Vector2Int(x, z));
                    else if (cell.Type == CellType.Industrial || cell.Type == CellType.PowerPlant)
                        _polluters.Add(new Vector2Int(x, z));
                }
            }

            float half = _cellSize * 0.45f;
            for (int x = _view.MinX; x <= _view.MaxX; x++)
            {
                for (int z = _view.MinZ; z <= _view.MaxZ; z++)
                {
                    if (!WorldTerrain.IsLand(x, z)) continue;
                    CityCell cell = _city.GetCell(x, z);
                    if (cell.Type == CellType.Road) continue;

                    if (!TryPickOverlayColor(x, z, cell, out Color32 color)) continue;

                    Vector3 center = new Vector3((x + 0.5f) * _cellSize, 0f, (z + 0.5f) * _cellSize);
                    float y = cell.Height > 0f ? CityChunkRenderer.BuildingBase + cell.Height + 0.14f : OverlayY;
                    _builder.SetFrame(center, 0);
                    _builder.AddQuad(
                        new Vector3(-half, y, -half),
                        new Vector3(-half, y, half),
                        new Vector3(half, y, half),
                        new Vector3(half, y, -half),
                        Vector3.up, color);
                }
            }

            _builder.ApplyTo(_mesh);
        }

        private bool TryPickOverlayColor(int x, int z, CityCell cell, out Color32 color)
        {
            color = default;
            switch (Mode)
            {
                case CityOverlayMode.Power:
                    if (cell.Type == CellType.PowerPlant)
                    {
                        color = new Color32(52, 214, 245, 0);
                        return true;
                    }
                    ZoneStatus status = _city.GetZoneStatus(x, z);
                    if (status == ZoneStatus.NotZoned) return false;
                    color = status switch
                    {
                        ZoneStatus.Developed or ZoneStatus.WaitingForDevelopment => new Color32(58, 206, 108, 255),
                        ZoneStatus.NeedsPower or ZoneStatus.DevelopedWithoutPower => new Color32(236, 56, 86, 0),
                        _ => new Color32(240, 146, 44, 255)
                    };
                    return true;

                case CityOverlayMode.Services:
                    if (cell.Model == BuildingModel.FireStation || cell.Model == BuildingModel.Hospital || cell.Model == BuildingModel.PoliceStation)
                    {
                        color = new Color32(58, 174, 252, 0);
                        return true;
                    }
                    if (cell.Type == CellType.Empty && !_city.Roads.HasRoadAccess(x, z)) return false;
                    int cov = CountWithin(_serviceSites, x, z, 16);
                    if (cov >= 2) color = new Color32(52, 198, 122, 255);
                    else if (cov == 1) color = new Color32(158, 208, 86, 255);
                    else if (cell.Height > 0f) color = new Color32(232, 82, 58, 255);
                    else return false;
                    return true;

                case CityOverlayMode.Education:
                    if (cell.Type == CellType.School)
                    {
                        color = new Color32(66, 176, 248, 0);
                        return true;
                    }
                    if (cell.Type != CellType.Residential) return false;
                    int schools = CountWithin(_eduSites, x, z, 18);
                    color = schools > 0 ? new Color32(72, 208, 122, 255) : new Color32(236, 138, 48, 255);
                    return true;

                case CityOverlayMode.Environment:
                    if (cell.Type == CellType.Industrial || cell.Type == CellType.PowerPlant)
                    {
                        color = new Color32(182, 94, 42, 255);
                        return true;
                    }
                    int dirty = CountWithin(_polluters, x, z, 8);
                    if (dirty > 0)
                    {
                        color = dirty >= 3 ? new Color32(216, 114, 48, 255) : new Color32(226, 186, 68, 255);
                        return true;
                    }
                    if (cell.Height > 0f || _city.Roads.HasRoadAccess(x, z))
                    {
                        color = new Color32(76, 194, 114, 255);
                        return true;
                    }
                    return false;
            }
            return false;
        }

        private static int CountWithin(List<Vector2Int> sites, int x, int z, int radius)
        {
            int rSq = radius * radius;
            int hits = 0;
            for (int i = 0; i < sites.Count; i++)
            {
                int dx = sites[i].x - x;
                int dz = sites[i].y - z;
                if (dx * dx + dz * dz <= rSq) hits++;
            }
            return hits;
        }

        public void Dispose()
        {
            _city.CellChanged -= OnCellChanged;
            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
            if (_go != null) UnityEngine.Object.Destroy(_go);
        }
    }
}
