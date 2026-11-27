using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace XCity
{
    internal sealed class CityEmergencySystem : IDisposable
    {
        private const float InitialCooldown = 26f;
        private const float RepeatInterval = 42f;
        private const float BaseFireDuration = 24f;

        private static readonly Color32 FlameOrange = new Color32(255, 118, 28, 3);
        private static readonly Color32 FlameGold = new Color32(255, 206, 48, 3);
        private static readonly Color32 SootSmoke = new Color32(54, 58, 66, 255);
        private static readonly Color32 WaterSpray = new Color32(156, 224, 255, 255);

        private readonly CitySimulation _city;
        private readonly float _cellSize;
        private readonly System.Random _rng = new System.Random(9001);
        private readonly List<Vector2Int> _burnables = new List<Vector2Int>(128);
        private readonly MeshBuilder _builder = new MeshBuilder();

        private readonly GameObject _go;
        private readonly Mesh _mesh;

        private WorldView _view;
        private bool _hasView;
        private bool _previewMode;
        private float _cooldown = InitialCooldown;
        private float _remaining;
        private float _time;
        private float _responderPulse;
        private bool _hasFireStation;

        public bool HasActiveFire { get; private set; }
        public Vector2Int FireCell { get; private set; }
        public Vector3 FireWorldPos { get; private set; }
        public float FireHeight { get; private set; }
        public event Action OnFireStarted;

        public string StatusBanner => !HasActiveFire
            ? null
            : _responderPulse > 0f
                ? "🔥 Đang dập lửa · Xe & trực thăng cứu hỏa đã tiếp cận!"
                : _hasFireStation
                    ? "🔥 Có đám cháy · Đang điều lực lượng cứu hỏa"
                    : "🔥 Có đám cháy · Hãy xây thêm Trạm cứu hỏa";

        public CityEmergencySystem(CitySimulation city, float cellSize, Material material, Transform parent)
        {
            _city = city;
            _cellSize = cellSize;

            _mesh = new Mesh { name = "CityEmergencies" };
            _mesh.MarkDynamic();

            _go = new GameObject("Emergencies");
            _go.transform.SetParent(parent, false);
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            MeshRenderer renderer = _go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        public void SetPreviewMode(bool preview) => _previewMode = preview;

        public void SetView(WorldView view) => _view = view;

        public void SetVisible(bool visible)
        {
            if (_go.activeSelf != visible) _go.SetActive(visible);
        }

        public void NotifyResponderNear(Vector3 pos)
        {
            if (!HasActiveFire) return;
            float dx = pos.x - FireWorldPos.x;
            float dz = pos.z - FireWorldPos.z;
            if (dx * dx + dz * dz <= 14f * 14f)
                _responderPulse = 0.6f;
        }

        public void Update(float dt)
        {
            if (dt <= 0f || _previewMode) return;
            _time += dt;
            if (_responderPulse > 0f) _responderPulse = Mathf.Max(0f, _responderPulse - dt);

            if (!HasActiveFire)
            {
                _cooldown -= dt;
                if (_cooldown <= 0f)
                {
                    _cooldown = RepeatInterval;
                    TryStartFire();
                }
                return;
            }

            CityCell current = _city.GetCell(FireCell.x, FireCell.y);
            if (current.Type == CellType.Empty || current.Height <= 0f)
            {
                Extinguish();
                return;
            }

            float suppressRate = 1f + (_hasFireStation ? 0.9f : 0f) + (_responderPulse > 0f ? 1.8f : 0f);
            _remaining -= dt * suppressRate;
            if (_remaining <= 0f)
            {
                Extinguish();
                return;
            }

            RebuildFireMesh();
        }

        private void TryStartFire()
        {
            _burnables.Clear();
            bool fireStationNearby = false;

            for (int x = _view.MinX; x <= _view.MaxX; x++)
            {
                for (int z = _view.MinZ; z <= _view.MaxZ; z++)
                {
                    CityCell cell = _city.GetCell(x, z);
                    if (cell.Height <= 0f || cell.Type == CellType.Empty || cell.Type == CellType.Road) continue;
                    if (cell.Model == BuildingModel.FireStation)
                    {
                        fireStationNearby = true;
                        continue;
                    }
                    if (!_city.Roads.HasRoadAccess(x, z)) continue;

                    _burnables.Add(new Vector2Int(x, z));
                    if (cell.Type == CellType.Industrial || cell.Type == CellType.PowerPlant)
                        _burnables.Add(new Vector2Int(x, z));
                }
            }

            if (_burnables.Count < 14) return;

            Vector2Int chosen = _burnables[_rng.Next(_burnables.Count)];
            CityCell chosenCell = _city.GetCell(chosen.x, chosen.y);
            FireCell = chosen;
            FireHeight = chosenCell.Height;
            FireWorldPos = new Vector3((chosen.x + 0.5f) * _cellSize, CityChunkRenderer.BuildingBase, (chosen.y + 0.5f) * _cellSize);
            _hasFireStation = fireStationNearby;
            _remaining = BaseFireDuration;
            HasActiveFire = true;
            _hasView = true;
            RebuildFireMesh();
            OnFireStarted?.Invoke();
        }

        private void Extinguish()
        {
            HasActiveFire = false;
            _responderPulse = 0f;
            _mesh.Clear();
        }

        private void RebuildFireMesh()
        {
            _builder.Clear();
            _builder.SetFrame(FireWorldPos, 0);

            float intensity = Mathf.Clamp01(_remaining / BaseFireDuration + 0.25f);
            float topY = FireHeight * 0.72f;

            // 7 flickering flame cones around roof / upper windows
            for (int i = 0; i < 7; i++)
            {
                float angle = i * 0.90f;
                float r = (i == 0) ? 0f : 0.62f + (i % 2) * 0.28f;
                float fx = Mathf.Cos(angle) * r;
                float fz = Mathf.Sin(angle) * r;
                float pulse = 0.72f + 0.38f * Mathf.Sin(_time * (8f + i * 1.3f) + i * 1.7f);
                float flameH = (0.85f + (i % 3) * 0.35f) * pulse * intensity;
                float baseR = 0.34f * (0.8f + 0.25f * intensity);
                Color32 col = (i & 1) == 0 ? FlameOrange : FlameGold;
                _builder.AddCylinder(new Vector3(fx, topY, fz), baseR, 0.03f, flameH, 5, col, col, true, FlameGold);
            }

            // 4 dark soot smoke puffs drifting upward
            for (int s = 0; s < 4; s++)
            {
                float age = Mathf.Repeat(_time * 0.48f + s * 0.25f, 1f);
                float sy = topY + 0.8f + age * 4.8f;
                float sx = Mathf.Sin(_time * 1.5f + s) * 0.28f + age * 0.9f;
                float sz = Mathf.Cos(_time * 1.2f + s) * 0.22f + age * 0.4f;
                float sr = (0.34f + age * 0.52f) * intensity;
                _builder.AddBox(new Vector3(sx, sy, sz), new Vector3(sr, sr * 0.85f, sr), SootSmoke, SootSmoke);
            }

            // Animated water spray jets when fire units are on scene
            if (_responderPulse > 0f)
            {
                for (int j = 0; j < 5; j++)
                {
                    float t = Mathf.Repeat(_time * 2.2f + j * 0.2f, 1f);
                    float y = Mathf.Lerp(1.1f, topY + 0.4f, Mathf.Sin(t * Mathf.PI * 0.75f));
                    float x = Mathf.Lerp(2.4f, 0.1f, t);
                    float z = Mathf.Lerp(2.1f, -0.1f, t);
                    _builder.AddBox(new Vector3(x, y, z), new Vector3(0.20f, 0.20f, 0.24f), WaterSpray, WaterSpray);
                }
            }

            _builder.ApplyTo(_mesh);
            _ = _hasView;
        }

        public void Dispose()
        {
            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
            if (_go != null) UnityEngine.Object.Destroy(_go);
        }
    }
}
