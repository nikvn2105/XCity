using UnityEngine;
using UnityEngine.UI;

namespace XCity
{
    public partial class XCityBootstrap
    {
        private enum FollowKind
        {
            None,
            Vehicle,
            Helicopter,
            Vessel
        }

        private CityEmergencySystem _emergencies;
        private CityOverlaySystem _overlays;
        private XCityAudioSystem _audio;

        private float _simSpeed = 1f;
        private float _rciRefreshTimer;
        private RectTransform _rciResFill;
        private RectTransform _rciComFill;
        private RectTransform _rciIndFill;

        private Button _speedButton;
        private Button _lightButton;
        private Button _overlayButton;
        private Button _followButton;
        private Button _audioButton;

        private FollowKind _followKind;
        private int _followIndex = -1;
        private int _followCursor;
        private string _followLabel;

        private void SetupUXSystems()
        {
            _emergencies = new CityEmergencySystem(_city, CellSize, _vertexColorMat, transform);
            _emergencies.SetPreviewMode(_previewOutput != null);
            _vehicles.SetEmergencySystem(_emergencies);
            _helicopters.SetEmergencySystem(_emergencies);
            _overlays = new CityOverlaySystem(_city, CellSize, _vertexColorMat, transform);
            _audio = new XCityAudioSystem(transform, _previewOutput != null);
            _emergencies.OnFireStarted += () =>
            {
                _audio?.PlayAlert();
                RefreshHUD();
            };
        }

        private void SetupUXToolbar(Font font)
        {
            var bar = CreatePanel(
                _safeAreaRoot,
                "UtilityBar",
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(12f, -110f),
                new Vector2(-12f, -68f),
                new Color(0.06f, 0.08f, 0.12f, 0.78f));

            var layout = bar.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8;
            layout.padding = new RectOffset(8, 8, 5, 5);
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            // Compact R-C-I Demand indicator
            var rciBox = CreatePanel(bar.transform, "RCIBars", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Color(0.14f, 0.17f, 0.23f, 1f));
            var rciRow = rciBox.AddComponent<HorizontalLayoutGroup>();
            rciRow.spacing = 4;
            rciRow.padding = new RectOffset(6, 6, 4, 4);
            rciRow.childForceExpandWidth = true;
            rciRow.childForceExpandHeight = true;

            _rciResFill = CreateDemandBar(rciBox.transform, font, "R", new Color(0.29f, 0.83f, 0.46f));
            _rciComFill = CreateDemandBar(rciBox.transform, font, "C", new Color(0.27f, 0.64f, 0.96f));
            _rciIndFill = CreateDemandBar(rciBox.transform, font, "I", new Color(0.94f, 0.68f, 0.20f));

            _speedButton = CreateCompactButton(bar.transform, font, "Tốc độ: 1×", "Btn_Speed");
            _speedButton.onClick.AddListener(CycleSimSpeed);

            _lightButton = CreateCompactButton(bar.transform, font, "Tự động", "Btn_Light");
            _lightButton.onClick.AddListener(CycleLightingMode);

            _overlayButton = CreateCompactButton(bar.transform, font, "Lớp phủ: Tắt", "Btn_Overlay");
            _overlayButton.onClick.AddListener(CycleOverlayMode);

            _followButton = CreateCompactButton(bar.transform, font, "Theo dõi", "Btn_Follow");
            _followButton.onClick.AddListener(CycleFollowTarget);

            _audioButton = CreateCompactButton(bar.transform, font, "Âm thanh: Bật", "Btn_Audio");
            _audioButton.onClick.AddListener(ToggleAudioMute);

            Button settingsButton = CreateCompactButton(bar.transform, font, "Cài đặt", "Btn_Settings");
            settingsButton.onClick.AddListener(ToggleSettings);

            RefreshUXButtons();
            RefreshRciBars();
        }

        private static RectTransform CreateDemandBar(Transform parent, Font font, string code, Color barColor)
        {
            var slot = CreatePanel(parent, "Demand_" + code, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Color(0.08f, 0.10f, 0.14f, 1f));

            var fillGo = CreatePanel(slot.transform, "Fill", new Vector2(0f, 0f), new Vector2(1f, 0.5f),
                new Vector2(2f, 2f), new Vector2(-2f, -2f), barColor);
            var fillRt = fillGo.GetComponent<RectTransform>();

            var txtObj = new GameObject("Code");
            txtObj.transform.SetParent(slot.transform, false);
            var tr = txtObj.AddComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = Vector2.zero;
            tr.offsetMax = Vector2.zero;
            var txt = txtObj.AddComponent<Text>();
            txt.font = font;
            txt.fontSize = 13;
            txt.text = code;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.raycastTarget = false;

            return fillRt;
        }

        private static Button CreateCompactButton(Transform parent, Font font, string label, string name)
        {
            Button btn = CreateHudButton(parent, font, label, name);
            Text txt = btn.GetComponentInChildren<Text>();
            if (txt != null) txt.fontSize = 15;
            return btn;
        }

        private void CycleSimSpeed()
        {
            _simSpeed = _simSpeed > 2f ? 0f : _simSpeed > 0.5f ? 3f : 1f;
            _audio?.PlayBuild();
            RefreshUXButtons();
        }

        private void CycleLightingMode()
        {
            _dayNight?.CycleMode();
            _audio?.PlayBuild();
            RefreshUXButtons();
        }

        private void CycleOverlayMode()
        {
            _overlays?.CycleMode();
            _cityRenderer?.SetInfoView(_overlays != null && _overlays.Mode == CityOverlayMode.Happiness);
            RefreshInfoViewHUD();
            _audio?.PlayBuild();
            RefreshUXButtons();
        }

        private void ToggleAudioMute()
        {
            _audio?.ToggleMute();
            RefreshUXButtons();
        }

        private void RefreshUXButtons()
        {
            if (_speedButton != null)
            {
                string lbl = _simSpeed <= 0f ? "Tạm dừng" : _simSpeed > 2f ? "Tốc độ: 3×" : "Tốc độ: 1×";
                _speedButton.GetComponentInChildren<Text>().text = lbl;
                _speedButton.image.color = _simSpeed <= 0f
                    ? new Color(0.58f, 0.24f, 0.24f, 1f)
                    : _simSpeed > 2f
                        ? new Color(0.19f, 0.52f, 0.96f, 1f)
                        : new Color(0.20f, 0.24f, 0.30f, 1f);
            }

            if (_lightButton != null && _dayNight != null)
            {
                _lightButton.GetComponentInChildren<Text>().text = _dayNight.GetModeLabel();
                _lightButton.image.color = _dayNight.Mode == LightingMode.Night
                    ? new Color(0.22f, 0.30f, 0.58f, 1f)
                    : new Color(0.20f, 0.24f, 0.30f, 1f);
            }

            if (_overlayButton != null && _overlays != null)
            {
                _overlayButton.GetComponentInChildren<Text>().text = "Lớp phủ: " + _overlays.GetModeLabel();
                _overlayButton.image.color = _overlays.Mode != CityOverlayMode.None
                    ? new Color(0.19f, 0.52f, 0.96f, 1f)
                    : new Color(0.20f, 0.24f, 0.30f, 1f);
            }

            if (_followButton != null)
            {
                _followButton.GetComponentInChildren<Text>().text =
                    _followKind != FollowKind.None ? "Đang theo dõi" : "Theo dõi";
                _followButton.image.color = _followKind != FollowKind.None
                    ? new Color(0.19f, 0.52f, 0.96f, 1f)
                    : new Color(0.20f, 0.24f, 0.30f, 1f);
            }

            if (_audioButton != null && _audio != null)
            {
                _audioButton.GetComponentInChildren<Text>().text = _audio.Muted ? "Âm thanh: Tắt" : "Âm thanh: Bật";
            }
        }

        private void RefreshRciBars()
        {
            if (_rciResFill == null) return;
            // Bars show the simulation's own demand (-100..100) as a 0..1 fill.
            float r = (_city.ResidentialDemand + 100) / 200f;
            float c = (_city.CommercialDemand + 100) / 200f;
            float ind = (_city.IndustrialDemand + 100) / 200f;
            _rciResFill.anchorMax = new Vector2(1f, r);
            _rciComFill.anchorMax = new Vector2(1f, c);
            _rciIndFill.anchorMax = new Vector2(1f, ind);
        }

        private bool TryBeginFollowFromPointer(Vector2 position)
        {
            if (_cam == null) return false;
            Ray ray = _cam.ScreenPointToRay(position);
            float bestDist = float.PositiveInfinity;
            FollowKind bestKind = FollowKind.None;
            int bestIndex = -1;

            if (_helicopters != null && _helicopters.TryPickHelicopter(ray, out int hIdx, out float hDist) && hDist < bestDist)
            {
                bestDist = hDist;
                bestKind = FollowKind.Helicopter;
                bestIndex = hIdx;
            }
            if (_watercraft != null && _watercraft.TryPickVessel(ray, out int sIdx, out float sDist) && sDist < bestDist)
            {
                bestDist = sDist;
                bestKind = FollowKind.Vessel;
                bestIndex = sIdx;
            }
            if (_vehicles != null && _vehicles.TryPickVehicle(ray, out int vIdx, out float vDist) && vDist < bestDist)
            {
                bestDist = vDist;
                bestKind = FollowKind.Vehicle;
                bestIndex = vIdx;
            }

            if (bestKind == FollowKind.None) return false;
            SetFollowTarget(bestKind, bestIndex);
            return true;
        }

        private void CycleFollowTarget()
        {
            int helis = _helicopters != null ? _helicopters.ActiveCount : 0;
            int ships = _watercraft != null ? _watercraft.ActiveCount : 0;
            int vehs = _vehicles != null ? _vehicles.ActiveCount : 0;
            int total = helis + ships + vehs;
            if (total == 0)
            {
                ClearFollow();
                return;
            }

            if (_followKind != FollowKind.None && (_followCursor % Mathf.Min(6, total) == Mathf.Min(6, total) - 1))
            {
                ClearFollow();
                _followCursor++;
                return;
            }

            int pick = (_followCursor++) % total;
            if (pick < helis)
                SetFollowTarget(FollowKind.Helicopter, pick);
            else if (pick < helis + ships)
                SetFollowTarget(FollowKind.Vessel, pick - helis);
            else
                SetFollowTarget(FollowKind.Vehicle, pick - helis - ships);
        }

        private void SetFollowTarget(FollowKind kind, int index)
        {
            _followKind = kind;
            _followIndex = index;
            _inspectedCell = null;
            if (_activeTool != BuildTool.Pan) SelectTool(BuildTool.Pan);
            _camDistance = Mathf.Min(_camDistance, 26f);
            UpdateFollowFocusLabel();
            _audio?.PlayBuild();
            RefreshUXButtons();
            RefreshHUD();
        }

        private void ClearFollow()
        {
            if (_followKind == FollowKind.None) return;
            _followKind = FollowKind.None;
            _followIndex = -1;
            _followLabel = null;
            RefreshUXButtons();
            RefreshHUD();
        }

        private void UpdateFollowFocusLabel()
        {
            if (!TryReadFollowFocus(out _, out _followLabel))
            {
                _followKind = FollowKind.None;
                _followIndex = -1;
                _followLabel = null;
            }
        }

        private bool TryReadFollowFocus(out Vector3 pos, out string label)
        {
            switch (_followKind)
            {
                case FollowKind.Helicopter:
                    if (_helicopters != null && _helicopters.TryGetHelicopterFocus(_followIndex, out pos, out label))
                        return true;
                    break;
                case FollowKind.Vessel:
                    if (_watercraft != null && _watercraft.TryGetVesselFocus(_followIndex, out pos, out label))
                        return true;
                    break;
                case FollowKind.Vehicle:
                    if (_vehicles != null && _vehicles.TryGetVehicleFocus(_followIndex, out pos, out label))
                        return true;
                    break;
            }
            pos = default;
            label = null;
            return false;
        }

        private void UpdateUXInView(WorldView view)
        {
            _emergencies?.SetView(view);
            _overlays?.SetView(view);
        }

        private void LateUpdateUX(float simDt)
        {
            _overlays?.Update();
            _emergencies?.Update(simDt);

            if (_dayNight != null && _dayNight.Mode == LightingMode.AutoCycle && _lightButton != null)
                _lightButton.GetComponentInChildren<Text>().text = _dayNight.GetModeLabel();

            _rciRefreshTimer += Time.unscaledDeltaTime;
            if (_rciRefreshTimer >= 1.2f)
            {
                _rciRefreshTimer = 0f;
                RefreshRciBars();
            }

            if (_followKind != FollowKind.None)
            {
                if (TryReadFollowFocus(out Vector3 focus, out string label))
                {
                    _followLabel = label;
                    float alpha = 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime);
                    _camTarget = Vector3.Lerp(_camTarget, focus, alpha);
                    UpdateCameraTransform();
                }
                else
                {
                    ClearFollow();
                }
            }
        }

        private string GetUXStatusPrefix()
        {
            if (_followKind != FollowKind.None && !string.IsNullOrEmpty(_followLabel))
                return $"Theo dõi: {_followLabel} · Kéo để thoát";
            if (_emergencies != null && !string.IsNullOrEmpty(_emergencies.StatusBanner))
                return _emergencies.StatusBanner;
            return null;
        }

        private void DisposeUXSystems()
        {
            _emergencies?.Dispose();
            _overlays?.Dispose();
            _audio?.Dispose();
        }
    }
}
