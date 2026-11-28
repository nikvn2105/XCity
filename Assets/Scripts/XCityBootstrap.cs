using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace XCity
{
    public enum BuildTool
    {
        Pan,
        Road,
        Residential,
        Commercial,
        Industrial,
        Bulldoze,
        PowerPlant,
        School,
        CivicService,
        Bridge
    }

    public partial class XCityBootstrap : MonoBehaviour
    {
        private const int GridSize = CitySimulation.GridSize;
        private const float CellSize = 4f;
        private const float TickInterval = 1.5f;

        private CitySimulation _city;
        private CitySave _save;
        private bool _cityDirty;
        private float _nextAutosave;
        private CityChunkRenderer _cityRenderer;
        private BuildingChunkRenderer _buildingRenderer;
        private ChunkRenderScheduler _chunkScheduler;
        private ConstructionSystem _construction;
        private IndustrialSmokeSystem _industrialSmoke;
        private VehicleSystem _vehicles;
        private HelicopterSystem _helicopters;
        private WatercraftSystem _watercraft;
        private CitizenSystem _citizens;
        private DayNightSystem _dayNight;
        private Material _skyMat;
        private Light _sunLight;
        private BuildTool _activeTool = BuildTool.Road;
        private Camera _cam;
        private Vector3 _camTarget;
        private float _camDistance = 42f;
        private float _camPitch = 48f;
        private float _camYaw = 45f;

        private float _simTimer;

        private Text _hudStatusText;
        private Button _undoButton;
        private Button _loanButton;
        private BuildingModel _activeBuildingModel;

        private Material _roadMat;
        private Material _waterMat;
        private Material _vertexColorMat;
        private Material _buildingMaterial;

        private Vector2 _gestureStart;
        private bool _panMoved;
        private Vector2Int? _inspectedCell;
        private LineRenderer _inspectionOutline;
        private Material _inspectionMaterial;
        private Vector2 _lastPointerPos;
        private bool _isDragging;
        private bool _waitForTouchRelease;
        private int _activeFingerId = -1;
        private int _pinchFirstId = -1;
        private int _pinchSecondId = -1;
        private RectTransform _safeAreaRoot;
        private Rect _lastSafeArea;
        private Vector2Int _lastScreenSize;
        private GraphicRaycaster _hudRaycaster;
        private PointerEventData _hudPointer;
        private readonly List<RaycastResult> _hudHits = new List<RaycastResult>();
        private const int PreviewPoolCapacity = 256;
        private readonly List<Vector2Int> _stroke = new List<Vector2Int>(PreviewPoolCapacity);
        private readonly List<Vector2Int> _roadTrace = new List<Vector2Int>(PreviewPoolCapacity);
        private readonly Dictionary<Vector2Int, RoadConnections> _strokeRoadExits =
            new Dictionary<Vector2Int, RoadConnections>(PreviewPoolCapacity);
        private readonly HashSet<Vector2Int> _strokeCells = new HashSet<Vector2Int>(PreviewPoolCapacity);
        private readonly List<MeshRenderer> _roadMarkers = new List<MeshRenderer>(PreviewPoolCapacity);
        private readonly HashSet<int> _quotedBridges = new HashSet<int>(PreviewPoolCapacity);
        private Material _roadPreviewMat;
        private Material _invalidPreviewMat;
        private Material _bulldozePreviewMat;
        private int _strokeCost;
        private int _strokeBudget;
        private bool _strokeOverBudget;
        private Text _strokeCostLabel;
        private readonly Text[] _strokeCostDigits = new Text[13];
        private static readonly string[] CostSymbols = { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "," };
        private int _shownStrokeCost = -1;
        private const string StrokeCostPrefix = UiText.StrokeCost;
        private const string OverBudgetPrefix = UiText.StrokeOverBudget;
        private float _strokeCostPrefixWidth;
        private float _overBudgetPrefixWidth;
        private bool _shownStrokeOverBudget;
        private Vector2Int? _bridgeStart;
        private Vector2Int _bridgeEnd;
        private BridgeQuote _bridgeQuote;
        private Vector2Int _lastStrokeSample;
        private float _slopeWarningUntil;
        private const float ShoreWarningDuration = 3f;
        private float _shoreWarningUntil;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoStart()
        {
            if (FindObjectOfType<XCityBootstrap>() != null) return;
            var host = new GameObject("XCityBootstrap");
            DontDestroyOnLoad(host);
            host.AddComponent<XCityBootstrap>();
        }

        private void Awake()
        {
            _previewOutput = GetPreviewOutputArg();
            _city = OpenStartupCity();
            CrashLog.SetMap(_city.Map);
            _nextAutosave = Time.unscaledTime + 15f;
            CreateMaterials();
            SetupEnvironment();
            LoadDisplaySettings();
            SetupCamera();
            SetupHUD();
            if (_city.DroppedCellsOnLoad > 0)
                ShowToast(string.Format(UiText.DroppedOnLoad, _city.DroppedCellsOnLoad), UiTheme.Warning, UiIcon.Info);
            RestoreFpsCounter();
            if (_previewOutput == null && _activeSaveSlot == 0) ShowMainMenu(intro: true);
            if (_previewOutput != null) StartCoroutine(CapturePreviewShots());
            else if (CrashLog.TakeStartupNotice()) ShowCrashLogToast(UiText.CrashLogPreviousSession, UiTheme.Warning);
        }

        private void CreateMaterials()
        {
            Shader worldShader = Resources.Load<Shader>("XCityWorld");
            Shader waterShader = Resources.Load<Shader>("XCityWater");
            _waterMat = MakeMat(waterShader != null ? waterShader : worldShader, new Color(0.14f, 0.34f, 0.48f));
            _roadMat = MakeMat(worldShader, new Color(0.18f, 0.20f, 0.23f));
        }

        private static Material MakeMat(Shader shader, Color color)
        {
            var m = new Material(shader);
            m.color = color;
            return m;
        }

        private void SetupEnvironment()
        {
            var lightObj = new GameObject("SunLight");
            _sunLight = lightObj.AddComponent<Light>();
            _sunLight.type = LightType.Directional;
            _sunLight.intensity = 0.96f;
            _sunLight.color = new Color(1f, 0.96f, 0.88f);
            _sunLight.shadows = LightShadows.Soft;
            _sunLight.shadowStrength = 0.64f;
            lightObj.transform.SetParent(transform);
            lightObj.transform.rotation = Quaternion.Euler(56f, -34f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.65f, 0.73f, 0.81f);
            RenderSettings.ambientEquatorColor = new Color(0.48f, 0.55f, 0.50f);
            RenderSettings.ambientGroundColor = new Color(0.31f, 0.34f, 0.32f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.69f, 0.82f, 0.86f);
            UpdateAtmosphereRanges();

            float worldSpan = GridSize * CellSize;
            AddLandscapeBase("Ocean", worldSpan, worldSpan + 800f, -1.05f, 0.2f, _waterMat);
            Shader vc = Resources.Load<Shader>("XCityVertexColor");
            if (vc == null)
            {
                Debug.LogError("Could not load XCityVertexColor shader; falling back to XCityWorld.");
                vc = Resources.Load<Shader>("XCityWorld");
            }
            _vertexColorMat = new Material(vc);
            _cityRenderer = new CityChunkRenderer(_city, CellSize, _vertexColorMat, transform);
            _buildingMaterial = new Material(vc);
            _buildingMaterial.EnableKeyword("XCITY_ARCHITECTURE");
            _buildingRenderer = new BuildingChunkRenderer(_city, CellSize, _buildingMaterial, _vertexColorMat, transform);
            _chunkScheduler = new ChunkRenderScheduler(_cityRenderer, _buildingRenderer, CellSize);
            _construction = new ConstructionSystem(_city, _buildingRenderer, CellSize, _vertexColorMat, transform);
            _industrialSmoke = new IndustrialSmokeSystem(_city, _construction, CellSize, _vertexColorMat, transform);
            _vehicles = new VehicleSystem(_city, CellSize, _vertexColorMat, transform);
            _helicopters = new HelicopterSystem(_city, CellSize, _vertexColorMat, transform);
            _watercraft = new WatercraftSystem(_city, CellSize, _vertexColorMat, transform);
            _citizens = new CitizenSystem(_city, CellSize, _vertexColorMat, transform);
            SetupUXSystems();
        }

        private void AddLandscapeBase(string name, float worldSpan, float size, float top, float depth, Material material)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(transform, false);
            block.transform.localPosition = new Vector3(worldSpan * 0.5f, top - depth * 0.5f, worldSpan * 0.5f);
            block.transform.localScale = new Vector3(size, depth, size);
            var collider = block.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            var renderer = block.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void SetupCamera()
        {
            var camObj = new GameObject("MainCamera");
            camObj.tag = "MainCamera";
            _cam = camObj.AddComponent<Camera>();
            Shader skyShader = Resources.Load<Shader>("XCitySky");
            if (skyShader != null)
            {
                _skyMat = new Material(skyShader) { hideFlags = HideFlags.DontSave };
                RenderSettings.skybox = _skyMat;
                _cam.clearFlags = CameraClearFlags.Skybox;
            }
            else
            {
                _cam.clearFlags = CameraClearFlags.SolidColor;
            }
            _cam.backgroundColor = RenderSettings.fogColor;
            _cam.nearClipPlane = 0.5f;
            _cam.farClipPlane = 320f;
            camObj.AddComponent<AudioListener>();

            // An empty city starts over its map's guaranteed buildable start area.
            _camTarget = new Vector3((WorldTerrain.Start.X + 0.5f) * CellSize, WorldTerrain.SurfaceY(WorldTerrain.Start.X, WorldTerrain.Start.Z), (WorldTerrain.Start.Z + 0.5f) * CellSize);
            foreach (int index in _city.OccupiedIndices)
            {
                _camTarget = new Vector3((index / GridSize + 0.5f) * CellSize, WorldTerrain.SurfaceY(index / GridSize, index % GridSize), (index % GridSize + 0.5f) * CellSize);
                break;
            }
            _dayNight = new DayNightSystem(_sunLight, _cam, _waterMat);
            UpdateCameraTransform();
        }

        private void UpdateCameraTransform()
        {
            Quaternion rot = Quaternion.Euler(_camPitch, _camYaw, 0f);
            Vector3 offset = rot * new Vector3(0f, 0f, -_camDistance);
            _cam.transform.position = _camTarget + offset;
            _cam.transform.LookAt(_camTarget);
            UpdateAtmosphereRanges();
        }

        private void SetupHUD()
        {
            if (FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }

            var canvasObj = new GameObject("HUDCanvas");
            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            _hudRaycaster = canvasObj.AddComponent<GraphicRaycaster>();
            _hudPointer = new PointerEventData(EventSystem.current);
            _safeAreaRoot = new GameObject("SafeArea", typeof(RectTransform)).GetComponent<RectTransform>();
            _safeAreaRoot.SetParent(canvas.transform, false);
            UpdateSafeArea();
            _roadPreviewMat = MakeMat(_roadMat.shader, new Color(0.30f, 0.94f, 0.42f));
            _invalidPreviewMat = MakeMat(_roadMat.shader, new Color(1f, 0.16f, 0.18f));
            _bulldozePreviewMat = MakeMat(_roadMat.shader, new Color(1f, 0.68f, 0.12f));
            for (int i = 0; i < PreviewPoolCapacity; i++) CreateStreetMarker();

            Font font = UiTheme.Font;

            var topBar = new GameObject("TopBar", typeof(RectTransform)).GetComponent<RectTransform>();
            topBar.SetParent(_safeAreaRoot, false);
            topBar.anchorMin = new Vector2(0, 1);
            topBar.anchorMax = Vector2.one;
            topBar.offsetMin = new Vector2(16, -12 - TopBarHeight);
            topBar.offsetMax = new Vector2(-16, -12);
            SetupTopBar(topBar, font);
            // Build and map in a corner, so every screenshot says what it runs.
            var buildInfo = CreateLabel(_safeAreaRoot, font, 13, UiTheme.TextMuted, TextAnchor.LowerRight);
            var infoRect = buildInfo.rectTransform;
            infoRect.anchorMin = infoRect.anchorMax = infoRect.pivot = new Vector2(1f, 0f);
            infoRect.anchoredPosition = new Vector2(-8f, 4f);
            var build = Resources.Load<TextAsset>("XCityBuild");
            buildInfo.text = $"{(build != null ? build.text.Trim() : "dev")} · " +
                $"{UiText.MapStyleName((int)WorldTerrain.Current.Style)} #{WorldTerrain.Current.Seed}";
            // A small pill under the top bar. While a fire burns, tapping it flies the camera there.
            var hintPill = CreatePanel(_safeAreaRoot, "HintPill", new Vector2(0.32f, 1), new Vector2(0.68f, 1),
                new Vector2(0, -110), new Vector2(0, -78), UiTheme.Panel);
            UiTheme.Round(hintPill.GetComponent<Image>(), 16f);
            hintPill.AddComponent<Button>().onClick.AddListener(FocusActiveFire);
            var statusObj = new GameObject("StatusText");
            statusObj.transform.SetParent(hintPill.transform, false);
            var statusRect = statusObj.AddComponent<RectTransform>();
            statusRect.anchorMin = Vector2.zero;
            statusRect.anchorMax = Vector2.one;
            statusRect.offsetMin = new Vector2(14, 0);
            statusRect.offsetMax = new Vector2(-14, 0);
            _hudStatusText = statusObj.AddComponent<Text>();
            _hudStatusText.font = font;
            _hudStatusText.fontSize = UiTheme.TinySize + 1;
            _hudStatusText.color = UiTheme.TextSoft;
            _hudStatusText.alignment = TextAnchor.MiddleCenter;
            _hudStatusText.raycastTarget = false;
            _hudStatusText.resizeTextForBestFit = true;
            _hudStatusText.resizeTextMinSize = UiTheme.TinySize;
            _hudStatusText.resizeTextMaxSize = UiTheme.TinySize + 1;
            SetupStrokeCost(statusRect, font);


            SetupBuildBar();
            SetupWorldMap();
            SetupUXToolbar(_topButtons);
            SetupRotateButtons();
            SetupInfoView();
            SetupTutorial();

            SelectTool(BuildTool.Road);
            RefreshHUD();
        }


        private static GameObject CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            var img = go.AddComponent<Image>();
            img.color = color;
            return go;
        }

        private static Button CreateHudButton(Transform parent, Font font, string label, string name)
        {
            var btnObj = new GameObject(name);
            btnObj.transform.SetParent(parent, false);
            var img = btnObj.AddComponent<Image>();
            img.color = UiTheme.Button;
            UiTheme.Round(img, 12f, outline: false);
            var btn = btnObj.AddComponent<Button>();
            btn.targetGraphic = img;

            var txtObj = new GameObject("Label");
            txtObj.transform.SetParent(btnObj.transform, false);
            var tr = txtObj.AddComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = Vector2.zero;
            tr.offsetMax = Vector2.zero;
            var txt = txtObj.AddComponent<Text>();
            txt.font = font;
            txt.fontSize = UiTheme.BodySize;
            txt.text = label;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;

            return btn;
        }

        private void Update()
        {
            UpdateSafeArea();
            if (SettingsOpen) RefreshCrashLogCount();
            if (MainMenuOpen)
            {
                OrbitMenuCamera();
                UpdateToasts();
                UpdateFpsCounter();
                return;
            }
            RunSimulationTick();
            HandleInput();
            UpdateCameraInertia();
            UpdateTopBar();
            UpdateFpsCounter();
            if (_shoreWarningUntil > 0f && Time.unscaledTime >= _shoreWarningUntil)
            {
                _shoreWarningUntil = 0f;
                RefreshHUD();
            }
            UpdateTutorial();
            if (_activeSaveSlot != 0 && Time.unscaledTime >= _nextAutosave) SaveCity(background: true);
        }

        private void UpdateSafeArea()
        {
            var size = new Vector2Int(Screen.width, Screen.height);
            Rect area = Screen.safeArea;
            if (size == _lastScreenSize && area == _lastSafeArea) return;

            _lastScreenSize = size;
            _lastSafeArea = area;
            _safeAreaRoot.anchorMin = new Vector2(area.xMin / size.x, area.yMin / size.y);
            _safeAreaRoot.anchorMax = new Vector2(area.xMax / size.x, area.yMax / size.y);
            _safeAreaRoot.offsetMin = Vector2.zero;
            _safeAreaRoot.offsetMax = Vector2.zero;
        }

        private void HandleInput()
        {
            if (_worldMapPanel.activeSelf || _bankruptPanel != null || SettingsOpen) return;
            if (Input.touchCount > 0)
            {
                HandleTouch();
                return;
            }

            if (_waitForTouchRelease || _activeFingerId >= 0)
            {
                CancelGesture();
                _waitForTouchRelease = false;
                _pinchFirstId = -1;
                _pinchSecondId = -1;
                return;
            }

            if (Input.GetMouseButtonDown(0)) BeginGesture(Input.mousePosition);
            else if (Input.GetMouseButtonUp(0)) EndGesture(Input.mousePosition);
            else if (Input.GetMouseButton(0)) ContinueGesture(Input.mousePosition);

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                float step = Mathf.Max(3f, _camDistance * 0.12f);
                _camDistance = Mathf.Clamp(_camDistance - scroll * step, MinCamDistance, MaxCamDistance);
                UpdateCameraTransform();
            }
        }

        private void HandleTouch()
        {
            if (Input.touchCount >= 2)
            {
                CancelGesture();
                _waitForTouchRelease = true;
                Touch first = Input.GetTouch(0);
                Touch second = Input.GetTouch(1);
                if (Input.touchCount != 2 || first.phase == TouchPhase.Ended ||
                    second.phase == TouchPhase.Ended || first.phase == TouchPhase.Canceled ||
                    second.phase == TouchPhase.Canceled || IsPointerOverUI(first.position) ||
                    IsPointerOverUI(second.position))
                {
                    _pinchFirstId = -1;
                    _pinchSecondId = -1;
                    return;
                }

                if (first.fingerId == _pinchFirstId && second.fingerId == _pinchSecondId)
                    ContinueTwoFingerGesture(first, second);
                else
                    BeginTwoFingerGesture(first, second);

                _pinchFirstId = first.fingerId;
                _pinchSecondId = second.fingerId;
                return;
            }

            if (_waitForTouchRelease) return;
            Touch touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Began)
            {
                _activeFingerId = touch.fingerId;
                BeginGesture(touch.position);
            }
            else if (touch.fingerId == _activeFingerId)
            {
                if (touch.phase == TouchPhase.Canceled) CancelGesture();
                else if (touch.phase == TouchPhase.Ended) EndGesture(touch.position);
                else if (touch.phase == TouchPhase.Moved) ContinueGesture(touch.position);
            }
        }

        private bool IsSinglePlacement =>
            _activeTool == BuildTool.PowerPlant || _activeTool == BuildTool.School || _activeTool == BuildTool.CivicService;

        private void BeginGesture(Vector2 position)
        {
            if (IsPointerOverUI(position)) return;
            StopCameraInertia(); // a new touch catches the gliding camera
            CloseBuildPicker();
            _isDragging = true;
            _gestureStart = position;
            _panMoved = false;
            _lastPointerPos = position;
            if (_activeTool == BuildTool.Bridge && TryGetCell(position, out var bank))
            {
                if (CitySimulation.IsBuildable(bank.x, bank.y))
                {
                    _bridgeStart = bank;
                    UpdateBridgePreview(bank);
                }
            }
            else if (_activeTool != BuildTool.Pan && TryGetCell(position, out var cell))
                AddStrokeSample(cell);
        }

        private void ContinueGesture(Vector2 position)
        {
            if (!_isDragging || IsPointerOverUI(position)) return;

            if (_activeTool == BuildTool.Pan)
            {
                if (!_panMoved && !IsPanMovement(position)) return;
                _panMoved = true;
                ClearFollow();
                Vector2 delta = position - _lastPointerPos;
                PanCamera(delta);
                TrackPanVelocity(delta);
                UpdateCameraTransform();
            }
            else if (IsSinglePlacement && IsPanMovement(position))
            {
                CancelGesture();
            }
            else if (TryGetCell(position, out var cell))
            {
                if (_activeTool == BuildTool.Bridge) UpdateBridgePreview(cell);
                else AddStrokeSample(cell);
            }
            _lastPointerPos = position;
        }

        private bool IsPanMovement(Vector2 position)
        {
            float threshold = 8f * Screen.height / 720f;
            return (position - _gestureStart).sqrMagnitude > threshold * threshold;
        }

        private void EndGesture(Vector2 position)
        {
            if (_isDragging && _activeTool == BuildTool.Pan && !_panMoved &&
                !IsPanMovement(position) && !IsPointerOverUI(position))
            {
                if (!TryBeginFollowFromPointer(position))
                {
                    ClearFollow();
                    _inspectedCell = TryInspectCell(position, out var inspected) ? inspected : (Vector2Int?)null;
                }
                RefreshHUD();
            }

            if (_isDragging && _activeTool != BuildTool.Pan &&
                !IsPointerOverUI(position) && TryGetCell(position, out var cell))
            {
                if (_activeTool == BuildTool.Bridge)
                {
                    UpdateBridgePreview(cell);
                    if (_bridgeStart.HasValue && _bridgeQuote == BridgeQuote.Ready)
                    {
                        Vector2Int start = _bridgeStart.Value;
                        if (_city.TryBuildBridge(start.x, start.y, _bridgeEnd.x, _bridgeEnd.y, _activeRoadType))
                        {
                            _cityDirty = true;
                            _audio?.PlayBridge();
                        }
                    }
                }
                else if (IsSinglePlacement)
                {
                    if (!IsPanMovement(position))
                    {
                        if (_city.TrySetCell(cell.x, cell.y, StrokeType, _activeBuildingModel))
                        {
                            _shoreWarningUntil = 0f;
                            _cityDirty = true;
                            _audio?.PlayBuild();
                        }
                        else if (WorldTerrain.IsSlope(cell.x, cell.y))
                            _slopeWarningUntil = Time.unscaledTime + ShoreWarningDuration;
                        else if (BuildingCatalog.NeedsShore(_activeBuildingModel))
                        {
                            _shoreWarningUntil = _city.CanPlaceWaterfront(cell.x, cell.y, _activeBuildingModel)
                                ? 0f : Time.unscaledTime + ShoreWarningDuration;
                        }
                    }
                    RefreshHUD();
                }
                else if (_activeTool == BuildTool.Road && !IsPanMovement(position))
                {
                    HandleRoadTap(cell);
                }
                else
                {
                    AddStrokeSample(cell);
                    ApplyBuildStroke();
                    ClearRoadAnchor();
                }
            }
            CancelGesture();
        }

        private void CancelGesture()
        {
            _isDragging = false;
            _activeFingerId = -1;
            _bridgeStart = null;
            if (_stroke.Count == 0) return;
            HideStreetPreview();
            _stroke.Clear();
            _strokeCells.Clear();
            _roadTrace.Clear();
            _strokeRoadExits.Clear();
            _quotedBridges.Clear();
            _strokeCost = 0;
            _strokeOverBudget = false;
            _strokeCostLabel.gameObject.SetActive(false);
            _shownStrokeCost = -1;
            RefreshHUD();
        }

        private bool IsPointerOverUI(Vector2 position)
        {
            _hudPointer.position = position;
            _hudHits.Clear();
            _hudRaycaster.Raycast(_hudPointer, _hudHits);
            return _hudHits.Count > 0;
        }

        private bool TryInspectCell(Vector2 position, out Vector2Int point)
        {
            Ray ray = _cam.ScreenPointToRay(position);
            float closest = float.PositiveInfinity;
            point = default;
            float width = CellSize * CityChunkRenderer.BuildingWidthRatio;
            if (!new Plane(Vector3.up, new Vector3(0f, BridgeProfile.WaterSurface, 0f)).Raycast(ray, out float planeDistance))
                return false;

            Vector3 ground = ray.GetPoint(planeDistance);
            Vector3 roof = ray.GetPoint(Mathf.Max(0f, planeDistance - (9f + WorldTerrain.MaxSurfaceY) / Mathf.Max(0.001f, -ray.direction.y)));
            int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(ground.x, roof.x) / CellSize) - 1, 0, GridSize - 1);
            int maxX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(ground.x, roof.x) / CellSize) + 1, 0, GridSize - 1);
            int minZ = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(ground.z, roof.z) / CellSize) - 1, 0, GridSize - 1);
            int maxZ = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(ground.z, roof.z) / CellSize) + 1, 0, GridSize - 1);
            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    float height = _city.GetCell(x, z).Height;
                    if (height <= 0f) continue;
                    var center = new Vector3((x + 0.5f) * CellSize,
                        WorldTerrain.SurfaceY(x, z) + CityChunkRenderer.BuildingBase + height * 0.5f, (z + 0.5f) * CellSize);
                    var bounds = new Bounds(center, new Vector3(width, height, width));
                    if (!bounds.IntersectRay(ray, out float distance) || distance >= closest) continue;
                    closest = distance;
                    point = new Vector2Int(x, z);
                }
            }

            bool hitSurface = TryPickTerrain(ray, planeDistance, out var surface, out float surfaceDistance);
            if (TryPickBridge(ray, planeDistance, out var bridge, out float bridgeDistance) &&
                bridgeDistance < Mathf.Min(closest, surfaceDistance))
            {
                point = bridge;
                return true;
            }
            if (closest <= surfaceDistance && !float.IsPositiveInfinity(closest)) return true;
            point = surface;
            return hitSurface && WorldTerrain.IsLand(point.x, point.y);
        }

        private bool TryGetCell(Vector2 position, out Vector2Int cell)
        {
            cell = default;
            Ray ray = _cam.ScreenPointToRay(position);
            Plane water = new Plane(Vector3.up, new Vector3(0f, BridgeProfile.WaterSurface, 0f));
            if (!water.Raycast(ray, out float distance)) return false;
            bool found = TryPickTerrain(ray, distance, out cell, out float surfaceDistance);
            if (_activeTool == BuildTool.Bulldoze && TryPickBridge(ray, distance, out var bridge, out float bridgeDistance) &&
                bridgeDistance < surfaceDistance)
            {
                cell = bridge;
                return true;
            }
            return found && (_activeTool != BuildTool.Pan || WorldTerrain.IsLand(cell.x, cell.y));
        }

        private bool TryPickTerrain(Ray ray, float waterDistance, out Vector2Int cell, out float nearest)
        {
            var selected = default(Vector2Int);
            float closest = float.PositiveInfinity;
            float vertical = ray.direction.y;
            int topTier = WorldTerrain.Current.Style == MapStyle.Classic ? 0 : 3;
            for (int tier = 0; tier <= topTier; tier++)
            {
                float y = tier * WorldTerrain.TerraceStep;
                TestPlane(y, tier, true);
                TestPlane(y + BridgeProfile.WaterSurface, tier, false);
            }

            if (topTier > 0)
            {
                Vector3 low = ray.GetPoint(waterDistance);
                Vector3 high = ray.GetPoint(Mathf.Max(0f, waterDistance +
                    (WorldTerrain.MaxSurfaceY + CityChunkRenderer.RoadY - BridgeProfile.WaterSurface) / vertical));
                int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(low.x, high.x) / CellSize), 0, GridSize - 1);
                int maxX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(low.x, high.x) / CellSize), 0, GridSize - 1);
                int minZ = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(low.z, high.z) / CellSize), 0, GridSize - 1);
                int maxZ = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(low.z, high.z) / CellSize), 0, GridSize - 1);
                for (int x = minX; x <= maxX; x++)
                for (int z = minZ; z <= maxZ; z++)
                {
                    if (!WorldTerrain.IsLand(x, z)) continue;
                    TerrainPatch surface = WorldTerrain.GetSurface(x, z);
                    // At irregular boundaries a patch can be flat at an intermediate height.
                    // Only nominal tier surfaces were already checked by TestPlane.
                    if (!surface.IsSlope && surface.Sample(0.5f, 0.5f) == WorldTerrain.Tier(x, z) * WorldTerrain.TerraceStep)
                        continue;
                    CityCell tile = _city.GetCell(x, z);
                    bool road = tile.Type == CellType.Road;
                    int subdivisions = !surface.IsSlope ? 1 : road ? 4 : 2;
                    for (int ix = 0; ix < subdivisions; ix++)
                    for (int iz = 0; iz < subdivisions; iz++)
                    {
                        float x0 = Cut(ix), x1 = Cut(ix + 1), z0 = Cut(iz), z1 = Cut(iz + 1);
                        Vector3 a = Point(x0, z0), b = Point(x0, z1), c = Point(x1, z1), d = Point(x1, z0);
                        TestTriangle(a, b, c);
                        TestTriangle(a, c, d);
                    }

                    float Cut(int index) => subdivisions == 4 ? CityChunkRenderer.RoadBoundary(index, tile.RoadType) : index / (float)subdivisions;
                    Vector3 Point(float u, float v) => new Vector3((x + u) * CellSize,
                        surface.Sample(u, v) + (road ? CityChunkRenderer.RoadY : 0.007f), (z + v) * CellSize);

                    void TestTriangle(Vector3 a, Vector3 b, Vector3 c)
                    {
                        Vector3 normal = Vector3.Cross(b - a, c - a);
                        float denom = Vector3.Dot(normal, ray.direction);
                        if (Mathf.Abs(denom) < 0.0001f) return;
                        float t = Vector3.Dot(normal, a - ray.origin) / denom;
                        if (t < 0f || t >= closest) return;
                        Vector3 hit = ray.GetPoint(t);
                        if (Vector3.Dot(Vector3.Cross(b - a, hit - a), normal) < 0f ||
                            Vector3.Dot(Vector3.Cross(c - b, hit - b), normal) < 0f ||
                            Vector3.Dot(Vector3.Cross(a - c, hit - c), normal) < 0f) return;
                        closest = t;
                        selected = new Vector2Int(x, z);
                    }
                }
            }

            cell = selected;
            nearest = closest;
            return !float.IsPositiveInfinity(nearest);

            void TestPlane(float y, int tier, bool land)
            {
                float distance = (y - ray.origin.y) / vertical;
                if (distance < 0f || distance >= closest) return;
                Vector3 hit = ray.GetPoint(distance);
                int x = Mathf.FloorToInt(hit.x / CellSize), z = Mathf.FloorToInt(hit.z / CellSize);
                if ((uint)x >= GridSize || (uint)z >= GridSize || WorldTerrain.Tier(x, z) != tier ||
                    WorldTerrain.IsLand(x, z) != land ||
                    (land && (WorldTerrain.IsSlope(x, z) || WorldTerrain.SurfaceY(x, z) != y))) return;
                closest = distance;
                selected = new Vector2Int(x, z);
            }
        }

        private bool TryPickBridge(Ray ray, float planeDistance, out Vector2Int tile, out float nearest)
        {
            tile = default;
            nearest = float.PositiveInfinity;
            Vector3 low = ray.GetPoint(planeDistance);
            Vector3 high = ray.GetPoint(Mathf.Max(0f, planeDistance -
                (WorldTerrain.MaxSurfaceY + BridgeProfile.MaxRise + 0.40f) / Mathf.Max(0.001f, -ray.direction.y)));
            int x0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(low.x, high.x) / CellSize) - 1, 0, GridSize - 1);
            int x1 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(low.x, high.x) / CellSize) + 1, 0, GridSize - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(low.z, high.z) / CellSize) - 1, 0, GridSize - 1);
            int z1 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(low.z, high.z) / CellSize) + 1, 0, GridSize - 1);
            for (int x = x0; x <= x1; x++)
            {
                for (int z = z0; z <= z1; z++)
                {
                    if (!_city.TryGetBridgeProfile(x, z, out BridgeProfile bridge)) continue;
                    bool alongX = bridge.Axis == CitySimulation.BridgeAlongX;
                    var size = new Vector3(alongX ? CellSize : CellSize * 0.94f, 0f,
                        alongX ? CellSize * 0.94f : CellSize);
                    float lower = Mathf.Min(bridge.Start, bridge.End) - BridgeProfile.DeckThickness;
                    float upper = Mathf.Max(bridge.Middle, Mathf.Max(bridge.Start, bridge.End)) + 0.30f;
                    size.y = upper - lower;
                    var bounds = new Bounds(new Vector3((x + 0.5f) * CellSize, (lower + upper) * 0.5f,
                        (z + 0.5f) * CellSize), size);
                    if (!bounds.IntersectRay(ray, out float distance) || distance >= nearest) continue;
                    nearest = distance;
                    tile = new Vector2Int(x, z);
                }
            }
            return !float.IsPositiveInfinity(nearest);
        }

        private void AddStrokeSample(Vector2Int destination)
        {
            if (_stroke.Count > 0 && destination == _lastStrokeSample) return;
            if (IsSinglePlacement && _stroke.Count > 0)
            {
                HideStreetPreview();
                _stroke.Clear();
                _strokeCells.Clear();
                _strokeCost = 0;
                _strokeOverBudget = false;
            }
            bool firstSample = _stroke.Count == 0 && !_strokeCostLabel.gameObject.activeSelf;
            if (_stroke.Count == 0) _strokeBudget = _city.Money;
            bool wasOverBudget = _strokeOverBudget;
            bool changedSlope = BlocksBuildingOnSlope &&
                (_stroke.Count == 0 || WorldTerrain.IsSlope(destination.x, destination.y) !=
                    WorldTerrain.IsSlope(_lastStrokeSample.x, _lastStrokeSample.y));
            Vector2Int cursor = _stroke.Count == 0 ? destination : _lastStrokeSample;
            int stepsX = Mathf.Abs(destination.x - cursor.x);
            int stepsY = Mathf.Abs(destination.y - cursor.y);
            int directionX = System.Math.Sign(destination.x - cursor.x);
            int directionY = System.Math.Sign(destination.y - cursor.y);
            int movedX = 0;
            int movedY = 0;
            AddStrokeCell(cursor);

            while (cursor != destination)
            {
                if (movedY == stepsY || (movedX < stepsX && (2 * movedX + 1) * stepsY <= (2 * movedY + 1) * stepsX))
                {
                    cursor.x += directionX;
                    movedX++;
                }
                else
                {
                    cursor.y += directionY;
                    movedY++;
                }
                AddStrokeCell(cursor);
            }
            _lastStrokeSample = destination;
            if (_activeTool == BuildTool.Road && (_activeRoadType == RoadType.OneWay || (!wasOverBudget && _strokeOverBudget)))
                RefreshStrokePreview();
            if (firstSample || changedSlope) RefreshHUD();
            else UpdateStrokeCost();
        }

        private bool BlocksBuildingOnSlope =>
            _activeTool != BuildTool.Pan && _activeTool != BuildTool.Road &&
            _activeTool != BuildTool.Bridge && _activeTool != BuildTool.Bulldoze;

        private CellType StrokeType => _activeTool switch
        {
            BuildTool.Road => CellType.Road,
            BuildTool.Residential => CellType.Residential,
            BuildTool.Commercial => CellType.Commercial,
            BuildTool.Industrial => CellType.Industrial,
            BuildTool.PowerPlant => CellType.PowerPlant,
            BuildTool.School or BuildTool.CivicService => BuildingCatalog.Get(_activeBuildingModel).Type,
            _ => CellType.Empty
        };

        private void AddStrokeCell(Vector2Int cell)
        {
            if (_activeTool == BuildTool.Road && _activeRoadType == RoadType.OneWay)
            {
                if (_roadTrace.Count == 0) _roadTrace.Add(cell);
                else
                {
                    Vector2Int previous = _roadTrace[_roadTrace.Count - 1];
                    if (previous != cell)
                    {
                        RoadConnections exit = RoadCatalog.Direction(cell.x - previous.x, cell.y - previous.y);
                        _strokeRoadExits[previous] = _strokeRoadExits[cell] = exit;
                        _roadTrace.Add(cell);
                    }
                }
            }
            if (!_strokeCells.Add(cell)) return;

            int index = _stroke.Count;
            _stroke.Add(cell);
            Material material = _activeTool == BuildTool.Road && _activeRoadType == RoadType.OneWay
                ? _invalidPreviewMat : QuoteStrokeMarker(cell);
            ShowStreetPreview(index, cell, material, PreviewRise(cell));
        }

        private Material QuoteStrokeMarker(Vector2Int cell)
        {
            _strokeRoadExits.TryGetValue(cell, out RoadConnections exit);
            if (!_city.TryQuoteCell(cell.x, cell.y, StrokeType, _activeBuildingModel, out int cost, out int bridge, _strokeBudget,
                _activeRoadType, exit))
                return _invalidPreviewMat;

            if (_activeTool == BuildTool.Bulldoze)
            {
                if (bridge < 0 || _quotedBridges.Add(bridge))
                {
                    _strokeCost += cost;
                    _strokeBudget -= cost;
                }
                return _bulldozePreviewMat;
            }

            _strokeCost += cost;
            if (_activeTool == BuildTool.Road)
            {
                _strokeOverBudget = _strokeCost > _city.Money;
                return _strokeOverBudget ? _invalidPreviewMat : _roadPreviewMat;
            }

            // The simulation applies eligible cells in stroke order and skips cells it cannot afford.
            if (cost <= _strokeBudget)
            {
                _strokeBudget -= cost;
                return _roadPreviewMat;
            }
            _strokeOverBudget = true;
            return _invalidPreviewMat;
        }

        private void RefreshStrokePreview()
        {
            _strokeBudget = _city.Money;
            _strokeCost = 0;
            _strokeOverBudget = false;
            _quotedBridges.Clear();
            for (int i = 0; i < _stroke.Count; i++)
                _roadMarkers[i].sharedMaterial = QuoteStrokeMarker(_stroke[i]);
            if (_activeTool == BuildTool.Road && _strokeOverBudget)
                for (int i = 0; i < _stroke.Count; i++)
                    _roadMarkers[i].sharedMaterial = _invalidPreviewMat;
        }

        private float PreviewRise(Vector2Int cell)
        {
            if (_activeTool == BuildTool.Bulldoze && _city.TryGetBridgeProfile(cell.x, cell.y, out BridgeProfile bridge))
                return Mathf.Max(bridge.Middle, Mathf.Max(bridge.Start, bridge.End));
            if (_city.TryGetBridgeProfile(cell.x, cell.y, out BridgeProfile deck)) return deck.Middle;
            if (!WorldTerrain.IsLand(cell.x, cell.y))
                return WorldTerrain.SurfaceY(cell.x, cell.y) + BridgeProfile.WaterSurface;
            return WorldTerrain.SurfaceY(cell.x, cell.y);
        }

        private void UpdateBridgePreview(Vector2Int pointer)
        {
            if (!_bridgeStart.HasValue) return;
            Vector2Int start = _bridgeStart.Value;
            Vector2Int delta = pointer - start;
            bool alongX = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y);
            var end = alongX ? new Vector2Int(pointer.x, start.y) : new Vector2Int(start.x, pointer.y);
            BridgeQuote quote = _city.QuoteBridge(start.x, start.y, end.x, end.y, out int cost, _activeRoadType);
            bool sameQuote = _stroke.Count > 0 && quote == _bridgeQuote;
            if (sameQuote && end == _bridgeEnd && cost == _strokeCost) return;
            bool hintChanged = !sameQuote && !(HasBridgePrice &&
                (quote == BridgeQuote.Ready || quote == BridgeQuote.InsufficientFunds));
            _bridgeEnd = end;
            _bridgeQuote = quote;
            _strokeCost = cost;
            _strokeOverBudget = quote == BridgeQuote.InsufficientFunds;
            HideStreetPreview();
            _stroke.Clear();
            int span = alongX ? delta.x : delta.y;
            int direction = System.Math.Sign(span);
            int previews = Mathf.Min(Mathf.Abs(span) + 1, CitySimulation.MaxBridgeWaterTiles + 2);
            var step = alongX ? new Vector2Int(direction, 0) : new Vector2Int(0, direction);
            for (int i = 0; i < previews; i++)
            {
                Vector2Int tile = start + step * i;
                _stroke.Add(tile);
                float rise = i > 0 && i < Mathf.Abs(span)
                    ? BridgeProfile.TerrainDeck(i - 0.5f, Mathf.Abs(span) - 1,
                        CitySimulation.BridgeBankY(start.x, start.y, step.x, step.y),
                        CitySimulation.BridgeBankY(end.x, end.y, -step.x, -step.y))
                    : WorldTerrain.SurfaceY(tile.x, tile.y);
                ShowStreetPreview(i, tile, quote == BridgeQuote.Ready ? _roadPreviewMat : _invalidPreviewMat, rise);
            }
            if (hintChanged) RefreshHUD();
            else if (HasBridgePrice) UpdateStrokeCost();
        }

        private bool HasBridgePrice => _bridgeStart.HasValue &&
            (_bridgeQuote == BridgeQuote.Ready || _bridgeQuote == BridgeQuote.InsufficientFunds);

        private void HideStreetPreview()
        {
            for (int i = 0; i < _stroke.Count; i++)
                _roadMarkers[i].gameObject.SetActive(false);
        }

        private void CreateStreetMarker()
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "StreetPreview";
            marker.transform.SetParent(transform, false);
            marker.GetComponent<Collider>().enabled = false;
            Destroy(marker.GetComponent<Collider>());
            var renderer = marker.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _roadPreviewMat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            marker.transform.localScale = new Vector3(CellSize * 0.98f, 0.06f, CellSize * 0.98f);
            marker.SetActive(false);
            _roadMarkers.Add(renderer);
        }

        private void ShowStreetPreview(int index, Vector2Int cell, Material material, float rise = 0f)
        {
            if (index == _roadMarkers.Count) CreateStreetMarker();
            MeshRenderer preview = _roadMarkers[index];
            if (preview.sharedMaterial != material) preview.sharedMaterial = material;
            TerrainPatch surface = WorldTerrain.GetSurface(cell.x, cell.y);
            bool onGrade = surface.IsSlope;
            preview.transform.rotation = TerrainRotation(surface);
            float markerWidth = CellSize * (onGrade ? 0.40f : 0.98f);
            preview.transform.localScale = new Vector3(markerWidth, 0.06f, markerWidth);
            preview.transform.position = new Vector3((cell.x + 0.5f) * CellSize, 0.15f + rise, (cell.y + 0.5f) * CellSize);
            preview.gameObject.SetActive(true);
        }

        private static Quaternion TerrainRotation(TerrainPatch surface) => surface.IsSlope
            ? Quaternion.FromToRotation(Vector3.up, new Vector3(-surface.DeltaX(0.5f), CellSize, -surface.DeltaZ(0.5f)))
            : Quaternion.identity;

        private bool ApplyBuildStroke()
        {
            if (_activeTool == BuildTool.Road && _strokeOverBudget) return false;
            CellType type = StrokeType;
            bool applied = type == CellType.Road
                ? _city.TryBuildRoad(GetStrokeCells(), _activeRoadType)
                : _city.TryApplyBuild(GetStrokeCells(), type, _activeBuildingModel);
            if (!applied) return false;
            _cityDirty = true;
            if (_activeTool == BuildTool.Bulldoze) _audio?.PlayBulldoze();
            else _audio?.PlayBuild();
            return true;
        }

        private IEnumerable<(int X, int Z)> GetStrokeCells()
        {
            var path = _activeTool == BuildTool.Road && _activeRoadType == RoadType.OneWay ? _roadTrace : _stroke;
            foreach (Vector2Int point in path)
                yield return (point.x, point.y);
        }

        private void UndoBuild()
        {
            CancelGesture();
            ClearRoadAnchor();
            if (_city.TryUndoBuild())
            {
                _cityDirty = true;
                _audio?.PlayUndo();
            }
            RefreshHUD();
        }

        private void RunSimulationTick()
        {
            if (_previewOutput != null || _simSpeed <= 0f) return;
            _simTimer += Time.deltaTime * _simSpeed;
            if (_simTimer < TickInterval) return;
            _simTimer -= TickInterval;
            _city.Tick();
            _cityDirty = true;
            RefreshHUD();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                CancelGesture();
                SaveCity();
            }
        }

        private void OnApplicationQuit() => SaveCity();

        // Autosaves run in the background; pausing or quitting saves synchronously so iOS cannot suspend mid-write.
        private bool SaveCity(bool background = false, bool force = false)
        {
            if (_previewOutput != null || _activeSaveSlot == 0) return true;
            _nextAutosave = Time.unscaledTime + 15f;
            _cityDirty |= background ? _save.HasFailedWrite(_activeSaveSlot) : !_save.WaitForWrite(_activeSaveSlot);
            if (!_cityDirty && !force) return true;
            bool saved = background
                ? _save.TryWriteInBackground(_activeSaveSlot, _activeCityName, _city)
                : _save.Write(_activeSaveSlot, _activeCityName, _city);
            _cityDirty = !saved;
            return saved;
        }

        private WorldView LocalView => new WorldView(Mathf.FloorToInt(_camTarget.x / CellSize), Mathf.FloorToInt(_camTarget.z / CellSize));

        private void RefreshSceneDetail(int budget)
        {
            _chunkScheduler.Refresh(_cam, budget);
            bool visible = _chunkScheduler.ShowEffects;
            _construction.SetVisible(visible);
            _industrialSmoke.SetVisible(visible);
            _vehicles.SetVisible(visible);
            _helicopters.SetVisible(visible);
            _watercraft.SetVisible(visible);
            _citizens.SetVisible(visible);
            _emergencies?.SetVisible(visible);
            if (!visible) return;
            var view = LocalView;
            _industrialSmoke.SetView(view);
            _vehicles.SetView(view);
            _helicopters.SetView(view);
            _watercraft.SetView(view);
            _citizens.SetView(view);
            UpdateUXInView(view);
        }

        private void LateUpdate()
        {
            RefreshSceneDetail(2);
            _cityRenderer.RebuildDirtyChunks(1);
            if (MainMenuOpen)
            {
                _buildingRenderer.RebuildDirtyChunks(1);
                _dayNight?.SyncClock(GameDays);
                return;
            }
            bool visible = _chunkScheduler.ShowEffects;
            float simDt = Time.deltaTime * _simSpeed;
            _construction.Update(simDt, visible, LocalView);
            _buildingRenderer.RebuildDirtyChunks(1);
            _dayNight?.SyncClock(GameDays);

            if (!visible)
            {
                LateUpdateUX(0f);
                return;
            }
            Shader.SetGlobalFloat("_XCityTime", Time.time);
            _industrialSmoke.Update(simDt);
            _vehicles.Update(simDt);
            _helicopters.Update(simDt);
            _watercraft.Update(simDt);
            _citizens.Update(simDt);
            LateUpdateUX(simDt);
        }

        private void OnDestroy()
        {
            foreach (Mesh guide in _roadGuideMeshes)
                if (guide != null) Destroy(guide);
            _cityRenderer?.Dispose();
            _construction?.Dispose();
            _industrialSmoke?.Dispose();
            _buildingRenderer?.Dispose();
            _vehicles?.Dispose();
            _helicopters?.Dispose();
            _watercraft?.Dispose();
            _citizens?.Dispose();
            DisposeUXSystems();
            Destroy(_vertexColorMat);
            Destroy(_waterMat);
            Destroy(_worldMapTexture);
            Destroy(_roadMat);
            Destroy(_roadPreviewMat);
            Destroy(_invalidPreviewMat);
            Destroy(_bulldozePreviewMat);
            Destroy(_buildingMaterial);
            if (_inspectionMaterial != null) Destroy(_inspectionMaterial);
            // DontSave survives the "new city" scene reload, so it must be freed by hand.
            if (_skyMat != null) Destroy(_skyMat);
        }

        private string GetInspectionHint()
        {
            if (!_inspectedCell.HasValue)
            {
                if (_inspectionOutline != null) _inspectionOutline.enabled = false;
                return "Chạm vào ô để xem trạng thái";
            }

            Vector2Int point = _inspectedCell.Value;
            CityCell cell = _city.GetCell(point.x, point.y);
            CellType type = cell.Type;
            ZoneStatus status = _city.GetZoneStatus(point.x, point.y);
            if (_inspectionOutline == null)
            {
                var outline = new GameObject("InspectedCell");
                outline.transform.SetParent(transform, false);
                _inspectionOutline = outline.AddComponent<LineRenderer>();
                _inspectionMaterial = MakeMat(_roadMat.shader, Color.white);
                _inspectionOutline.sharedMaterial = _inspectionMaterial;
                _inspectionOutline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _inspectionOutline.loop = true;
                _inspectionOutline.positionCount = 4;
                _inspectionOutline.startWidth = 0.16f;
                _inspectionOutline.endWidth = 0.16f;
            }

            float x = point.x * CellSize;
            float z = point.y * CellSize;
            const float inset = 0.12f;
            bool raised = _city.TryGetBridgeProfile(point.x, point.y, out BridgeProfile profile);
            float near = inset / CellSize;
            float far = 1f - near;
            SetCorner(0, near, near);
            SetCorner(1, far, near);
            SetCorner(2, far, far);
            SetCorner(3, near, far);

            void SetCorner(int index, float u, float v)
            {
                float grade = raised ? profile.HeightAt(profile.Axis == CitySimulation.BridgeAlongX ? u : v)
                    : cell.Type == CellType.Road
                        ? WorldTerrain.GetSurface(point.x, point.y).Sample(u, v) + CityChunkRenderer.RoadY
                        : WorldTerrain.GetSurface(point.x, point.y).Sample(u, v);
                _inspectionOutline.SetPosition(index, new Vector3(x + u * CellSize, grade + 0.18f, z + v * CellSize));
            }
            _inspectionOutline.enabled = true;
            _inspectionMaterial.color = status switch
            {
                ZoneStatus.NeedsRoad or ZoneStatus.DevelopedWithoutRoad => new Color(1f, 0.38f, 0.20f),
                ZoneStatus.NeedsPower or ZoneStatus.DevelopedWithoutPower => new Color(0.77f, 0.48f, 1f),
                ZoneStatus.WaitingForDevelopment => new Color(1f, 0.82f, 0.25f),
                ZoneStatus.Developed => new Color(0.25f, 1f, 0.48f),
                _ => Color.white
            };

            string label = cell.Model != BuildingModel.Default ? BuildingCatalog.Get(cell.Model).Name : type switch
            {
                CellType.Road => cell.IsBridge ? UiText.Bridge : UiText.RoadName(cell.RoadType),
                CellType.Residential => "Nhà ở",
                CellType.Commercial => "Thương mại",
                CellType.Industrial => "Công nghiệp",
                CellType.PowerPlant => "Nhà máy điện",
                _ => "Ô trống"
            };
            if (type == CellType.Residential && cell.Level > 0)
                label += $" · Cấp {cell.Level} · {BuildingCatalog.Residents(cell)} dân · Hạnh phúc {_city.GetHappiness(point.x, point.y)}%" +
                    $" · Dịch vụ: {DescribeCoverage(_city.GetCoverage(point.x, point.y))}" +
                    (!_city.HasWater(point.x, point.y) ? " · Thiếu nước" : _city.HasDirtyWater(point.x, point.y) ? " · Thiếu nước sạch" : "") +
                    (_city.HasSewage(point.x, point.y) ? "" : " · Thiếu thoát nước") +
                    (_city.GetTrafficLoad(point.x, point.y) > 100 ? " · Kẹt xe" : "");
            if (cell.Model == BuildingModel.WaterPump && _city.PumpOnPollutedWater(point.x, point.y))
                label += " · Mất nước sạch: nguồn bị ô nhiễm";
            if (type == CellType.Residential || type == CellType.Commercial)
            {
                label += " · " + UiText.LandValueFact(_city.GetLandValue(point.x, point.y));
                if (_city.LandValueBlocksGrowth(point.x, point.y))
                    label += " · " + UiText.NeedsLandValue(BuildingCatalog.MinLandValue(BuildingCatalog.DensityOf(cell.Model)));
            }
            if (type == CellType.Road)
                label += $" · Lưu lượng {_city.GetTrafficLoad(point.x, point.y)}%";
            if (cell.Model == BuildingModel.WaterPump || cell.Model == BuildingModel.SewagePlant)
                label += $" · Cấp {CitySimulation.UtilityCapacity} ô qua đường";
            var service = ServiceCatalog.Get(cell.Model);
            if (service.Radius > 0)
                label += $" · Bán kính {service.Radius} ô";
            if (type == CellType.School)
                label += $" · Học sinh {_city.GetSchoolStudents(point.x, point.y):N0}/{BuildingCatalog.Get(cell.Model).Seats:N0} chỗ" +
                    $" · Phí: ${BuildingCatalog.ServiceUpkeep(cell.Model):N0}";
            if (type == CellType.CivicService)
                label += $" · Phí duy trì: ${BuildingCatalog.ServiceUpkeep(cell.Model):N0}";
            if (GarbageCatalog.IsFacility(cell.Model))
            {
                var (used, capacity) = _city.GarbageFacilityAt(point.x, point.y);
                label += " · " + UiText.GarbageCollection(used, capacity);
            }
            else if (_city.ProducesGarbage(point.x, point.y) && !_city.HasGarbageService(point.x, point.y))
                label += " · " + UiText.MissingGarbage;
            return status switch
            {
                ZoneStatus.NeedsRoad => label + " · Cần đường liền kề",
                ZoneStatus.WaitingForDevelopment => label + (ZoneDemand(type) > 0
                    ? " · Đang chờ phát triển" : " · Chưa có nhu cầu"),
                ZoneStatus.DevelopedWithoutRoad => label + " · Mất kết nối đường",
                ZoneStatus.NeedsPower or ZoneStatus.DevelopedWithoutPower => label + " · Thiếu điện",
                ZoneStatus.Developed => label + (BuildingCatalog.IsPublicService(type) ? " · Đang hoạt động" : " · Đã phát triển"),
                _ => type == CellType.PowerPlant
                    ? label + (_city.Roads.HasRoadAccess(point.x, point.y)
                        ? $" · Cấp {CitySimulation.PowerCapacity(cell.Model)} ô" : " · Cần đường liền kề")
                    : label
            };
        }

        private string _lastHudText;

        private int ZoneDemand(CellType type) => type switch
        {
            CellType.Residential => _city.ResidentialDemand,
            CellType.Commercial => _city.CommercialDemand,
            CellType.Industrial => _city.IndustrialDemand,
            _ => 1
        };

        private static string DescribeCoverage(ServiceCoverage coverage)
        {
            if (coverage == ServiceCoverage.None) return "chưa có";
            var parts = new List<string>(4);
            if ((coverage & ServiceCoverage.Education) != 0) parts.Add("giáo dục");
            if ((coverage & ServiceCoverage.Health) != 0) parts.Add("y tế");
            if ((coverage & ServiceCoverage.Police) != 0) parts.Add("an ninh");
            if ((coverage & ServiceCoverage.Fire) != 0) parts.Add("cứu hỏa");
            return string.Join(", ", parts);
        }

        private string GetBridgeHint()
        {
            if (!_bridgeStart.HasValue) return "Kéo thẳng từ bờ này sang bờ kia để xây cầu";
            return _bridgeQuote switch
            {
                BridgeQuote.Ready => "Thả để xây cầu",
                BridgeQuote.InsufficientFunds => "Thiếu ngân sách",
                BridgeQuote.TooLong => $"Cầu tối đa {CitySimulation.MaxBridgeWaterTiles} ô nước",
                BridgeQuote.SpanObstructed => "Cần một nhịp nước trống giữa hai đầu cầu",
                _ => "Kéo tiếp đến đất ở bờ đối diện"
            };
        }

        private string GetShoreHint()
        {
            bool harbor = _activeBuildingModel == BuildingModel.Harbor;
            if (Time.unscaledTime < _shoreWarningUntil)
                return harbor ? "Cảng cần một ô biển liền kề" : "Không giáp nước — chọn ô sát bờ";
            return harbor ? "Đặt sát mép nước (một cạnh giáp biển)" : "Đặt sát mép nước, cạnh đường có điện";
        }

        private GameObject _bankruptPanel;

        private void ShowBankruptcy()
        {
            if (_bankruptPanel != null) return;
            CancelGesture();
            Font font = UiTheme.Font;
            _bankruptPanel = CreatePanel(_safeAreaRoot, "Bankruptcy", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Color(0.05f, 0.06f, 0.09f, 0.92f));
            var message = new GameObject("Message", typeof(RectTransform));
            message.transform.SetParent(_bankruptPanel.transform, false);
            var rect = (RectTransform)message.transform;
            rect.anchorMin = new Vector2(0.15f, 0.45f);
            rect.anchorMax = new Vector2(0.85f, 0.80f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var text = message.AddComponent<Text>();
            text.font = font;
            text.fontSize = 30;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.text = $"Thành phố đã phá sản\n<size=22>Ngân sách âm suốt {CitySimulation.BankruptcyTicks} lượt liên tiếp. " +
                $"Dân số cuối cùng: {_city.Population:N0}</size>";
            Button restart = CreateHudButton(_bankruptPanel.transform, font, UiText.NewCity, "NewCity");
            var buttonRect = (RectTransform)restart.transform;
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0.35f);
            buttonRect.sizeDelta = new Vector2(320, 72);
            restart.onClick.AddListener(OpenNewCityFromBankruptcy);
        }

        private static void RestartAfterLoad(Scene scene, LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= RestartAfterLoad;
            AutoStart();
        }


        private void SetupStrokeCost(RectTransform parent, Font font)
        {
            _strokeCostLabel = CreateStrokeCostText(parent, font, "StrokeCost", 0f, 400f);
            _strokeCostLabel.text = StrokeCostPrefix;
            _strokeCostPrefixWidth = _strokeCostLabel.preferredWidth;
            _strokeCostLabel.text = OverBudgetPrefix;
            _overBudgetPrefixWidth = _strokeCostLabel.preferredWidth;
            for (int i = 0; i < _strokeCostDigits.Length; i++)
                _strokeCostDigits[i] = CreateStrokeCostText(_strokeCostLabel.rectTransform, font, "CostDigit", 0f, 12f);
            _strokeCostLabel.gameObject.SetActive(false);
        }

        private static Text CreateStrokeCostText(RectTransform parent, Font font, string name, float x, float width)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)obj.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(x, -11f);
            rect.sizeDelta = new Vector2(width, 22f);
            var label = obj.AddComponent<Text>();
            label.font = font;
            label.fontSize = 16;
            label.alignment = TextAnchor.MiddleLeft;
            label.color = Color.white;
            label.raycastTarget = false;
            return label;
        }

        private void UpdateStrokeCost()
        {
            int cost = _strokeCost;
            bool layoutChanged = _shownStrokeCost < 0 || _shownStrokeOverBudget != _strokeOverBudget;
            if (!layoutChanged && _shownStrokeCost == cost) return;

            if (layoutChanged)
                _strokeCostLabel.text = _strokeOverBudget ? OverBudgetPrefix : StrokeCostPrefix;
            _strokeCostLabel.gameObject.SetActive(true);
            _shownStrokeCost = cost;
            _shownStrokeOverBudget = _strokeOverBudget;

            int length = 0;
            int remaining = cost;
            do
            {
                if (length == 3 || length == 7 || length == 11) length++;
                length++;
                remaining /= 10;
            } while (remaining > 0);

            // Reuse immutable one-character labels because uGUI Text needs a string for each update.
            float start = _strokeOverBudget ? _overBudgetPrefixWidth : _strokeCostPrefixWidth;
            int digits = 0;
            for (int i = length - 1; i >= 0; i--)
            {
                bool separator = digits == 3;
                Text digit = _strokeCostDigits[i];
                digit.rectTransform.anchoredPosition = new Vector2(start + i * 10f, -11f);
                digit.text = CostSymbols[separator ? 10 : cost % 10];
                if (separator) digits = 0;
                else
                {
                    digits++;
                    cost /= 10;
                }
            }
            for (int i = 0; i < _strokeCostDigits.Length; i++)
                if (_strokeCostDigits[i].enabled != (i < length)) _strokeCostDigits[i].enabled = i < length;
        }

        private void RefreshHUD()
        {
            RefreshBuildBar();
            RefreshInfoViewHUD();

            if (_undoButton != null) _undoButton.interactable = _city.CanUndoBuild;
            if (_loanButton != null) _loanButton.interactable = _city.CanTakeLoan;
            RefreshTopBar();
            RefreshBudgetPanel();
            UpdateTutorialProgress();
            if (_hudStatusText != null)
            {
                if (_activeTool != BuildTool.Pan && _inspectionOutline != null)
                    _inspectionOutline.enabled = false;

                string hint = _activeTool switch
                {
                    BuildTool.Pan => GetInspectionHint(),
                    BuildTool.Road => RoadHint,
                    BuildTool.Bridge => GetBridgeHint(),
                    // A gas plant gets its name and price appended below, like other catalog buildings.
                    BuildTool.PowerPlant => _activeBuildingModel == BuildingModel.Default
                        ? $"Chạm đặt nhà máy · {CitySimulation.PowerPlantCost:N0}" : "Chạm đặt nhà máy",
                    BuildTool.Bulldoze => "Kéo qua các ô cần phá bỏ",
                    BuildTool.School or BuildTool.CivicService => BuildingCatalog.NeedsShore(_activeBuildingModel)
                        ? GetShoreHint() : "Chạm để đặt công trình",
                    _ => "Chạm hoặc kéo để phân khu"
                };
                if (_activeBuildingModel != BuildingModel.Default)
                {
                    var item = BuildingCatalog.Get(_activeBuildingModel);
                    hint = _city.Money < item.Cost && Time.unscaledTime >= _shoreWarningUntil
                        ? $"Thiếu ngân sách · {item.Name} · ${item.Cost:N0}"
                        : $"{hint} · {item.Name} · ${item.Cost:N0}";
                }
                bool isBridge = _activeTool == BuildTool.Bridge;
                bool showStrokeCost = isBridge ? HasBridgePrice : _stroke.Count > 0;
                if (showStrokeCost)
                {
                    if (!isBridge) RefreshStrokePreview();
                    UpdateStrokeCost();
                }
                else if (_strokeCostLabel.gameObject.activeSelf)
                {
                    _strokeCostLabel.gameObject.SetActive(false);
                    _shownStrokeCost = -1;
                }
                if (BlocksBuildingOnSlope && (Time.unscaledTime < _slopeWarningUntil ||
                    (_stroke.Count > 0 && WorldTerrain.IsSlope(_lastStrokeSample.x, _lastStrokeSample.y))))
                    hint = UiText.SlopeRoadOnly;
                int unpowered = _stroke.Count == 0 ? _city.UnpoweredZones : 0;
                if (unpowered > 0) hint = $"Thiếu điện: {unpowered} ô  |  {hint}";
                string uxPrefix = GetUXStatusPrefix();
                if (uxPrefix != null) hint = $"{uxPrefix}  |  {hint}";
                if (_simSpeed <= 0f) hint = $"Đang tạm dừng  |  {hint}";
                if (_city.TicksInDebt > 0 && !_city.IsBankrupt)
                    hint = $"Cảnh báo phá sản: còn {CitySimulation.BankruptcyTicks - _city.TicksInDebt} lượt · Vay hoặc cắt giảm chi phí  |  {hint}";
                if (_city.IsBankrupt) ShowBankruptcy();
                string text = showStrokeCost ? "\u00a0" : hint;
                if (text == _lastHudText) return;
                _lastHudText = text;
                _hudStatusText.text = text;
            }
        }
    }
}
