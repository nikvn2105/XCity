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
        private Button _catalogButton;
        private GameObject _catalog;
        private GameObject _catalogOptions;
        private Image _catalogTab;
        private BuildingModel _activeBuildingModel;
        private readonly Dictionary<BuildTool, Image> _toolButtons = new Dictionary<BuildTool, Image>();

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
        private readonly List<Vector2Int> _stroke = new List<Vector2Int>();
        private readonly HashSet<Vector2Int> _strokeCells = new HashSet<Vector2Int>();
        private readonly List<Transform> _roadMarkers = new List<Transform>();
        private Material _roadPreviewMat;
        private int _roadStrokeCost;
        private Vector2Int? _bridgeStart;
        private Vector2Int _bridgeEnd;
        private BridgeQuote _bridgeQuote;
        private Vector2Int _lastStrokeSample;

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
            _save = new CitySave(Application.persistentDataPath);
            _previewOutput = GetPreviewOutputArg();
            _city = _previewOutput != null ? XCityPreviewCity.Create() : _save.Load();
            _nextAutosave = Time.unscaledTime + 15f;
            CreateMaterials();
            SetupEnvironment();
            LoadDisplaySettings();
            SetupCamera();
            SetupHUD();
            RestoreFpsCounter();
            if (_previewOutput != null) StartCoroutine(CapturePreviewShots());
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
                RenderSettings.skybox = new Material(skyShader) { hideFlags = HideFlags.DontSave };
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

            float center = GridSize * CellSize * 0.5f;
            _camTarget = new Vector3(center, 0f, center);
            foreach (int index in _city.OccupiedIndices)
            {
                _camTarget = new Vector3((index / GridSize + 0.5f) * CellSize, 0f, (index % GridSize + 0.5f) * CellSize);
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
            _roadPreviewMat = MakeMat(_roadMat.shader, Color.green);

            Font font = Resources.Load<Font>("NotoSans-Regular");

            var topBar = CreatePanel(_safeAreaRoot, "TopBar", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -64), new Vector2(0, 0), new Color(0.08f, 0.10f, 0.14f, 0.85f));
            var statusObj = new GameObject("StatusText");
            statusObj.transform.SetParent(topBar.transform, false);
            var statusRect = statusObj.AddComponent<RectTransform>();
            statusRect.anchorMin = Vector2.zero;
            statusRect.anchorMax = Vector2.one;
            statusRect.offsetMin = new Vector2(20, 0);
            statusRect.offsetMax = new Vector2(-360, 0);
            _hudStatusText = statusObj.AddComponent<Text>();
            _hudStatusText.font = font;
            _hudStatusText.fontSize = 21;
            _hudStatusText.color = Color.white;
            _hudStatusText.alignment = TextAnchor.MiddleLeft;
            _hudStatusText.resizeTextForBestFit = true;
            _hudStatusText.resizeTextMinSize = 13;
            _hudStatusText.resizeTextMaxSize = 21;

            _undoButton = CreateHudButton(topBar.transform, font, "Hoàn tác", "UndoBuild");
            var undoRect = (RectTransform)_undoButton.transform;
            undoRect.anchorMin = new Vector2(1, 0);
            undoRect.anchorMax = Vector2.one;
            undoRect.offsetMin = new Vector2(-172, 8);
            undoRect.offsetMax = new Vector2(-12, -8);
            _undoButton.onClick.AddListener(UndoBuild);

            _loanButton = CreateHudButton(topBar.transform, font, $"Vay ${CitySimulation.LoanAmount:N0}", "TakeLoan");
            var loanRect = (RectTransform)_loanButton.transform;
            loanRect.anchorMin = new Vector2(1, 0);
            loanRect.anchorMax = Vector2.one;
            loanRect.offsetMin = new Vector2(-344, 8);
            loanRect.offsetMax = new Vector2(-180, -8);
            _loanButton.onClick.AddListener(() =>
            {
                CancelGesture();
                _cityDirty |= _city.TryTakeLoan();
                RefreshHUD();
            });

            var bottomBar = CreatePanel(_safeAreaRoot, "BottomBar", new Vector2(0.1f, 0), new Vector2(0.9f, 0), new Vector2(0, 14), new Vector2(0, 88), new Color(0.08f, 0.10f, 0.14f, 0.88f));
            var layout = bottomBar.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10;
            layout.padding = new RectOffset(12, 12, 10, 10);
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            AddToolButton(bottomBar.transform, font, "Xem", BuildTool.Pan);
            SetupWorldMap(bottomBar.transform, font);
            AddToolButton(bottomBar.transform, font, "Đường", BuildTool.Road);
            AddToolButton(bottomBar.transform, font, "Cầu", BuildTool.Bridge);
            _catalogButton = CreateHudButton(bottomBar.transform, font, "Xây", "OpenCatalog");
            _catalogButton.onClick.AddListener(ToggleCatalog);
            SetupBuildCatalog(font);
            AddToolButton(bottomBar.transform, font, "Nhà máy điện", BuildTool.PowerPlant);
            AddToolButton(bottomBar.transform, font, "Phá bỏ", BuildTool.Bulldoze);
            SetupUXToolbar(font);
            SetupInfoView(font);

            SelectTool(BuildTool.Road);
            RefreshHUD();
        }

        private readonly List<(BuildingModel Model, Button Button, Text Label)> _catalogButtons =
            new List<(BuildingModel, Button, Text)>();
        private int _catalogUnlockTier = -1;
        private string _unlockBanner;
        private float _unlockBannerUntil;

        private void SetupBuildCatalog(Font font)
        {
            _catalogButtons.Clear();
            _unlockBanner = null;
            _unlockBannerUntil = 0f;

            _catalog = new GameObject("BuildCatalog", typeof(RectTransform));
            var root = (RectTransform)_catalog.transform;
            root.SetParent(_safeAreaRoot, false);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;

            Transform tabs = CreateCatalogRow(root, "Categories", 94, 146);
            AddCatalogCategory(root, tabs, font, "Nhà ở", BuildTool.Residential,
                BuildingModel.RowHouse, BuildingModel.Villa, BuildingModel.Apartment);
            AddCatalogCategory(root, tabs, font, "Trường học", BuildTool.School,
                BuildingModel.Preschool, BuildingModel.PrimarySchool, BuildingModel.HighSchool, BuildingModel.University);
            AddCatalogCategory(root, tabs, font, "Công nghiệp", BuildTool.Industrial,
                BuildingModel.Workshop, BuildingModel.Warehouse, BuildingModel.Factory);
            AddCatalogCategory(root, tabs, font, "Văn phòng", BuildTool.Commercial,
                BuildingModel.Office, BuildingModel.OfficeTower);
            AddCatalogCategory(root, tabs, font, "Thương mại", BuildTool.Commercial,
                BuildingModel.Market, BuildingModel.HighStreet, BuildingModel.ShoppingMall);
            AddCatalogCategory(root, tabs, font, "Dịch vụ", BuildTool.CivicService,
                BuildingModel.PoliceStation, BuildingModel.Hospital, BuildingModel.FireStation, BuildingModel.Harbor);
            AddCatalogCategory(root, tabs, font, "Tiện ích", BuildTool.CivicService,
                BuildingModel.WaterPump, BuildingModel.SewagePlant);

            _catalogUnlockTier = CurrentUnlockTier(_city.PeakPopulation);
            RefreshCatalogUnlocks();
            _catalog.SetActive(false);
        }

        private static Transform CreateCatalogRow(Transform parent, string name, float bottom, float top)
        {
            var row = CreatePanel(parent, name, new Vector2(0.1f, 0), new Vector2(0.9f, 0),
                new Vector2(0, bottom), new Vector2(0, top), new Color(0.08f, 0.10f, 0.14f, 0.94f));
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 7, 7);
            layout.spacing = 8;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            return row.transform;
        }

        private void AddCatalogCategory(Transform root, Transform tabs, Font font, string label, BuildTool tool,
            params BuildingModel[] models)
        {
            Button tab = CreateHudButton(tabs, font, label, label);
            Transform options = CreateCatalogRow(root, label, 152, 226);
            foreach (BuildingModel model in models)
            {
                var item = BuildingCatalog.Get(model);
                string price = item.Seats > 0 ? $"${item.Cost:N0} · {item.Seats:N0} chỗ" : $"${item.Cost:N0}/ô";
                Button button = CreateHudButton(options, font, $"{item.Name}\n{price}", model.ToString());
                _catalogButtons.Add((model, button, button.GetComponentInChildren<Text>()));
                button.onClick.AddListener(() =>
                {
                    if (BuildingCatalog.UnlockPopulation(model) > _city.PeakPopulation) return;
                    _activeBuildingModel = model;
                    SelectTool(tool);
                });
            }

            options.gameObject.SetActive(false);
            tab.onClick.AddListener(() => ShowCatalogCategory(options.gameObject, tab.image));
            if (_catalogOptions == null) ShowCatalogCategory(options.gameObject, tab.image);
        }

        private static int CurrentUnlockTier(int peakPopulation) =>
            peakPopulation >= 5000 ? 5000 : peakPopulation >= 2000 ? 2000 : peakPopulation >= 500 ? 500 : 0;

        private static string FormatMilestone(int population) =>
            population >= 1000 ? $"{population / 1000}.{population % 1000:D3}" : population.ToString();

        private void RefreshCatalogUnlocks()
        {
            for (int i = 0; i < _catalogButtons.Count; i++)
            {
                var entry = _catalogButtons[i];
                int required = BuildingCatalog.UnlockPopulation(entry.Model);
                bool unlocked = _city.PeakPopulation >= required;
                var item = BuildingCatalog.Get(entry.Model);

                entry.Button.interactable = unlocked;
                entry.Button.image.color = unlocked
                    ? new Color(0.20f, 0.24f, 0.30f, 1f)
                    : new Color(0.13f, 0.15f, 0.19f, 0.65f);

                if (entry.Label != null)
                {
                    entry.Label.color = unlocked ? Color.white : new Color(0.62f, 0.66f, 0.72f, 0.75f);
                    if (unlocked)
                    {
                        string price = item.Seats > 0 ? $"${item.Cost:N0} · {item.Seats:N0} chỗ" : $"${item.Cost:N0}/ô";
                        entry.Label.text = $"{item.Name}\n{price}";
                    }
                    else
                    {
                        entry.Label.text = $"{item.Name}\nMở ở {FormatMilestone(required)} dân";
                    }
                }
            }
        }

        private void ShowCatalogCategory(GameObject options, Image tab)
        {
            if (_catalogOptions != null) _catalogOptions.SetActive(false);
            if (_catalogTab != null) _catalogTab.color = new Color(0.20f, 0.24f, 0.30f);
            _catalogOptions = options;
            _catalogTab = tab;
            _catalogOptions.SetActive(true);
            _catalogTab.color = new Color(0.19f, 0.52f, 0.96f);
        }

        private void ToggleCatalog()
        {
            CancelGesture();
            _catalog.SetActive(!_catalog.activeSelf);
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

        private void AddToolButton(Transform parent, Font font, string label, BuildTool tool)
        {
            Button button = CreateHudButton(parent, font, label, "Btn_" + tool);
            button.onClick.AddListener(() =>
            {
                _activeBuildingModel = BuildingModel.Default;
                SelectTool(tool);
            });
            _toolButtons[tool] = button.image;
        }

        private static Button CreateHudButton(Transform parent, Font font, string label, string name)
        {
            var btnObj = new GameObject(name);
            btnObj.transform.SetParent(parent, false);
            var img = btnObj.AddComponent<Image>();
            img.color = new Color(0.20f, 0.24f, 0.30f, 1f);
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
            txt.fontSize = 20;
            txt.text = label;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;

            return btn;
        }

        private void SelectTool(BuildTool tool)
        {
            CancelGesture();
            if (tool != BuildTool.Pan) ClearFollow();
            if (_activeBuildingModel != BuildingModel.Default &&
                BuildingCatalog.UnlockPopulation(_activeBuildingModel) > _city.PeakPopulation)
            {
                _activeBuildingModel = BuildingModel.Default;
                if (tool == BuildTool.School || tool == BuildTool.CivicService)
                    tool = BuildTool.Road;
            }
            _activeTool = tool;
            _catalog.SetActive(false);
            bool catalogTool = _activeBuildingModel != BuildingModel.Default;
            _catalogButton.image.color = catalogTool
                ? new Color(0.19f, 0.52f, 0.96f)
                : new Color(0.20f, 0.24f, 0.30f);
            _catalogButton.GetComponentInChildren<Text>().text = catalogTool
                ? BuildingCatalog.Get(_activeBuildingModel).Name
                : "Xây";
            _inspectedCell = null;
            RefreshHUD();
            foreach (var kv in _toolButtons)
            {
                kv.Value.color = kv.Key == tool
                    ? new Color(0.19f, 0.52f, 0.96f, 1f)
                    : new Color(0.20f, 0.24f, 0.30f, 1f);
            }
        }

        private void Update()
        {
            UpdateSafeArea();
            RunSimulationTick();
            HandleInput();
            UpdateFpsCounter();
            if (_cityDirty && Time.unscaledTime >= _nextAutosave)
                SaveCity(background: true);
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
                {
                    Vector2 current = second.position - first.position;
                    Vector2 previous = current - second.deltaPosition + first.deltaPosition;
                    if (current.sqrMagnitude > 1f && previous.sqrMagnitude > 1f)
                    {
                        _camDistance = Mathf.Clamp(_camDistance * previous.magnitude / current.magnitude, MinCamDistance, MaxCamDistance);
                        _camYaw -= Vector2.SignedAngle(previous, current);
                        UpdateCameraTransform();
                    }
                }

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
            if (_catalog.activeSelf)
            {
                _catalog.SetActive(false);
                return;
            }
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
            else if (_activeTool != BuildTool.Pan && !IsSinglePlacement && TryGetCell(position, out var cell))
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
                Vector3 right = Vector3.ProjectOnPlane(_cam.transform.right, Vector3.up).normalized;
                Vector3 forward = Vector3.ProjectOnPlane(_cam.transform.forward, Vector3.up).normalized;
                float panSpeed = _camDistance * 1.6f / Screen.height;
                _camTarget -= (right * delta.x + forward * delta.y) * panSpeed;
                float maxBound = GridSize * CellSize;
                _camTarget.x = Mathf.Clamp(_camTarget.x, 0f, maxBound);
                _camTarget.z = Mathf.Clamp(_camTarget.z, 0f, maxBound);
                UpdateCameraTransform();
            }
            else if (!IsSinglePlacement && TryGetCell(position, out var cell))
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
                        if (_city.TryBuildBridge(start.x, start.y, _bridgeEnd.x, _bridgeEnd.y))
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
                        CellType placement = _activeTool == BuildTool.PowerPlant
                            ? CellType.PowerPlant : BuildingCatalog.Get(_activeBuildingModel).Type;
                        if (_city.TrySetCell(cell.x, cell.y, placement, _activeBuildingModel))
                        {
                            _cityDirty = true;
                            _audio?.PlayBuild();
                        }
                    }
                    RefreshHUD();
                }
                else
                {
                    AddStrokeSample(cell);
                    if (_activeTool != BuildTool.Road || _roadStrokeCost <= _city.Money)
                        ApplyBuildStroke();
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
            if (_activeTool == BuildTool.Road || _activeTool == BuildTool.Bridge)
                HideStreetPreview();
            _stroke.Clear();
            _strokeCells.Clear();
            _roadStrokeCost = 0;
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
            if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float planeDistance))
                return false;

            Vector3 ground = ray.GetPoint(planeDistance);
            Vector3 roof = ray.GetPoint(Mathf.Max(0f, planeDistance - 9f / Mathf.Max(0.001f, -ray.direction.y)));
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
                        CityChunkRenderer.BuildingBase + height * 0.5f, (z + 0.5f) * CellSize);
                    var bounds = new Bounds(center, new Vector3(width, height, width));
                    if (!bounds.IntersectRay(ray, out float distance) || distance >= closest) continue;
                    closest = distance;
                    point = new Vector2Int(x, z);
                }
            }

            if (TryPickBridge(ray, planeDistance, out var bridge, out float bridgeDistance) &&
                bridgeDistance < closest)
            {
                point = bridge;
                return true;
            }
            return !float.IsPositiveInfinity(closest) || TryGetCell(position, out point);
        }

        private bool TryGetCell(Vector2 position, out Vector2Int cell)
        {
            cell = default;
            Ray ray = _cam.ScreenPointToRay(position);
            Plane ground = new Plane(Vector3.up, Vector3.zero);
            if (!ground.Raycast(ray, out float distance)) return false;
            if (_activeTool == BuildTool.Bulldoze && TryPickBridge(ray, distance, out cell, out _))
                return true;
            Vector3 hit = ray.GetPoint(distance);
            cell = new Vector2Int(Mathf.FloorToInt(hit.x / CellSize), Mathf.FloorToInt(hit.z / CellSize));
            if (cell.x < 0 || cell.y < 0 || cell.x >= GridSize || cell.y >= GridSize) return false;
            return _activeTool == BuildTool.Bridge || CitySimulation.IsBuildable(cell.x, cell.y) ||
                (_activeTool == BuildTool.Bulldoze || _activeTool == BuildTool.Pan) &&
                _city.GetCell(cell.x, cell.y).IsBridge;
        }

        private bool TryPickBridge(Ray ray, float planeDistance, out Vector2Int tile, out float nearest)
        {
            tile = default;
            nearest = float.PositiveInfinity;
            Vector3 low = ray.GetPoint(planeDistance);
            Vector3 high = ray.GetPoint(Mathf.Max(0f, planeDistance -
                (BridgeProfile.MaxRise + 0.40f) / Mathf.Max(0.001f, -ray.direction.y)));
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
            RefreshHUD();
        }

        private void AddStrokeCell(Vector2Int cell)
        {
            if (!CitySimulation.IsBuildable(cell.x, cell.y) &&
                (_activeTool != BuildTool.Bulldoze || !_city.GetCell(cell.x, cell.y).IsBridge)) return;
            if (!_strokeCells.Add(cell)) return;

            int index = _stroke.Count;
            _stroke.Add(cell);
            if (_activeTool != BuildTool.Road) return;
            if (_city.GetCell(cell.x, cell.y).Type != CellType.Road) _roadStrokeCost += CitySimulation.RoadCost;
            ShowStreetPreview(index, cell);
        }

        private void UpdateBridgePreview(Vector2Int pointer)
        {
            if (!_bridgeStart.HasValue) return;
            Vector2Int start = _bridgeStart.Value;
            Vector2Int delta = pointer - start;
            bool alongX = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y);
            _bridgeEnd = alongX ? new Vector2Int(pointer.x, start.y) : new Vector2Int(start.x, pointer.y);
            HideStreetPreview();
            _stroke.Clear();
            _bridgeQuote = _city.QuoteBridge(start.x, start.y, _bridgeEnd.x, _bridgeEnd.y, out _roadStrokeCost);
            int span = alongX ? delta.x : delta.y;
            int direction = System.Math.Sign(span);
            int previews = Mathf.Min(Mathf.Abs(span) + 1, CitySimulation.MaxBridgeWaterTiles + 2);
            var step = alongX ? new Vector2Int(direction, 0) : new Vector2Int(0, direction);
            for (int i = 0; i < previews; i++)
            {
                Vector2Int tile = start + step * i;
                _stroke.Add(tile);
                float rise = i > 0 && i < Mathf.Abs(span)
                    ? BridgeProfile.Rise(i - 0.5f, Mathf.Abs(span) - 1) : 0f;
                ShowStreetPreview(i, tile, rise);
            }
            RefreshHUD();
        }

        private void HideStreetPreview()
        {
            for (int i = 0; i < _stroke.Count; i++)
                _roadMarkers[i].gameObject.SetActive(false);
        }

        private void ShowStreetPreview(int index, Vector2Int cell, float rise = 0f)
        {
            if (index == _roadMarkers.Count)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                marker.name = "RoadPreview";
                marker.GetComponent<Collider>().enabled = false;
                Destroy(marker.GetComponent<Collider>());
                var renderer = marker.GetComponent<Renderer>();
                renderer.sharedMaterial = _roadPreviewMat;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                marker.transform.localScale = new Vector3(CellSize * 0.9f, 0.06f, CellSize * 0.9f);
                _roadMarkers.Add(marker.transform);
            }

            Transform preview = _roadMarkers[index];
            preview.position = new Vector3((cell.x + 0.5f) * CellSize, 0.15f + rise, (cell.y + 0.5f) * CellSize);
            preview.gameObject.SetActive(true);
        }

        private void ApplyBuildStroke()
        {
            CellType type = _activeTool switch
            {
                BuildTool.Road => CellType.Road,
                BuildTool.Residential => CellType.Residential,
                BuildTool.Commercial => CellType.Commercial,
                BuildTool.Industrial => CellType.Industrial,
                _ => CellType.Empty
            };
            if (_city.TryApplyBuild(GetStrokeCells(), type, _activeBuildingModel))
            {
                _cityDirty = true;
                if (_activeTool == BuildTool.Bulldoze) _audio?.PlayBulldoze();
                else _audio?.PlayBuild();
            }
        }

        private IEnumerable<(int X, int Z)> GetStrokeCells()
        {
            foreach (Vector2Int point in _stroke)
                yield return (point.x, point.y);
        }

        private void UndoBuild()
        {
            CancelGesture();
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
        private void SaveCity(bool background = false)
        {
            if (_previewOutput != null) return;
            _nextAutosave = Time.unscaledTime + 15f;
            if (!_cityDirty) return;
            if (background ? _save.TryWriteInBackground(_city) : _save.Write(_city)) _cityDirty = false;
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
            bool visible = _chunkScheduler.ShowEffects;
            float simDt = Time.deltaTime * _simSpeed;
            _construction.Update(simDt, visible, LocalView);
            _buildingRenderer.RebuildDirtyChunks(1);
            _dayNight?.Update(simDt);

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
            Destroy(_buildingMaterial);
            if (_inspectionMaterial != null) Destroy(_inspectionMaterial);
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
                float grade = raised ? profile.HeightAt(profile.Axis == CitySimulation.BridgeAlongX ? u : v) : 0f;
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
                CellType.Road => cell.IsBridge ? "Cầu đường bộ" : "Đường",
                CellType.Residential => "Nhà ở",
                CellType.Commercial => "Thương mại",
                CellType.Industrial => "Công nghiệp",
                CellType.PowerPlant => "Nhà máy điện",
                _ => "Ô trống"
            };
            if (type == CellType.Residential && cell.Level > 0)
                label += $" · Cấp {cell.Level} · {BuildingCatalog.Residents(cell)} dân · Hạnh phúc {_city.GetHappiness(point.x, point.y)}%" +
                    $" · Dịch vụ: {DescribeCoverage(_city.GetCoverage(point.x, point.y))}" +
                    (_city.HasWater(point.x, point.y) ? "" : " · Thiếu nước") +
                    (_city.HasSewage(point.x, point.y) ? "" : " · Thiếu thoát nước") +
                    (_city.GetTrafficLoad(point.x, point.y) > 100 ? " · Kẹt xe" : "");
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
                        ? $" · Cấp {CitySimulation.PowerPlantCapacity} ô" : " · Cần đường liền kề")
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
            _roadPreviewMat.color = _bridgeQuote == BridgeQuote.Ready ? Color.green : Color.red;
            return _bridgeQuote switch
            {
                BridgeQuote.Ready => $"Chi phí cầu: ${_roadStrokeCost:N0} · Thả để xây",
                BridgeQuote.InsufficientFunds => $"Thiếu ngân sách · Cầu: ${_roadStrokeCost:N0}",
                BridgeQuote.TooLong => $"Cầu tối đa {CitySimulation.MaxBridgeWaterTiles} ô nước",
                BridgeQuote.SpanObstructed => "Cần một nhịp nước trống giữa hai đầu cầu",
                _ => "Kéo tiếp đến đất ở bờ đối diện"
            };
        }

        private GameObject _bankruptPanel;

        private void ShowBankruptcy()
        {
            if (_bankruptPanel != null) return;
            CancelGesture();
            Font font = Resources.Load<Font>("NotoSans-Regular");
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
            Button restart = CreateHudButton(_bankruptPanel.transform, font, "Xây thành phố mới", "NewCity");
            var buttonRect = (RectTransform)restart.transform;
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0.35f);
            buttonRect.sizeDelta = new Vector2(320, 72);
            restart.onClick.AddListener(StartNewCity);
        }

        // Saving a fresh city first moves the bankrupt one to the .bak file, then the scene reload rebuilds everything.
        private void StartNewCity()
        {
            if (!_save.Write(new CitySimulation())) return;
            _cityDirty = false;
            SceneManager.sceneLoaded += RestartAfterLoad;
            Destroy(gameObject);
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private static void RestartAfterLoad(Scene scene, LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= RestartAfterLoad;
            AutoStart();
        }

        private static string Signed(int value) => value.ToString("+0;-0;0");

        private void RefreshHUD()
        {
            int unlockTier = CurrentUnlockTier(_city.PeakPopulation);
            if (unlockTier != _catalogUnlockTier)
            {
                if (_catalogUnlockTier >= 0 && unlockTier > _catalogUnlockTier)
                {
                    var unlockedNames = new List<string>();
                    for (int i = 0; i < _catalogButtons.Count; i++)
                    {
                        BuildingModel model = _catalogButtons[i].Model;
                        int required = BuildingCatalog.UnlockPopulation(model);
                        if (required > _catalogUnlockTier && required <= unlockTier)
                            unlockedNames.Add(BuildingCatalog.Get(model).Name);
                    }
                    if (unlockedNames.Count > 0)
                    {
                        _unlockBanner = "Đã mở khóa: " + string.Join(", ", unlockedNames);
                        _unlockBannerUntil = Time.unscaledTime + 6f;
                    }
                }
                _catalogUnlockTier = unlockTier;
                RefreshCatalogUnlocks();
            }

            if (_undoButton != null) _undoButton.interactable = _city.CanUndoBuild;
            if (_loanButton != null) _loanButton.interactable = _city.CanTakeLoan;
            if (_hudStatusText != null)
            {
                if (_activeTool != BuildTool.Pan && _inspectionOutline != null)
                    _inspectionOutline.enabled = false;

                string hint = _activeTool switch
                {
                    BuildTool.Pan => GetInspectionHint(),
                    BuildTool.Road => "Kéo để vẽ đường · Thả để đặt",
                    BuildTool.Bridge => GetBridgeHint(),
                    BuildTool.PowerPlant => $"Chạm đặt nhà máy · {CitySimulation.PowerPlantCost:N0}",
                    BuildTool.Bulldoze => "Kéo qua các ô cần phá bỏ",
                    BuildTool.School or BuildTool.CivicService => "Chạm để đặt công trình",
                    _ => "Chạm hoặc kéo để phân khu"
                };
                if (_activeBuildingModel != BuildingModel.Default)
                {
                    var item = BuildingCatalog.Get(_activeBuildingModel);
                    hint = _city.Money < item.Cost
                        ? $"Thiếu ngân sách · {item.Name} · ${item.Cost:N0}"
                        : $"{hint} · {item.Name} · ${item.Cost:N0}";
                }
                if (_activeTool == BuildTool.Road && _stroke.Count > 0)
                {
                    bool affordable = _roadStrokeCost <= _city.Money;
                    _roadPreviewMat.color = affordable ? Color.green : Color.red;
                    hint = affordable ? $"Chi phí: ${_roadStrokeCost:N0}" : $"Thiếu ngân sách · Chi phí: ${_roadStrokeCost:N0}";
                }
                if (BuildingCatalog.NeedsShore(_activeBuildingModel) && _city.Money >= BuildingCatalog.Get(_activeBuildingModel).Cost)
                    hint = _activeBuildingModel == BuildingModel.Harbor
                        ? "Chạm vào ô đất sát mặt nước để đặt cảng"
                        : "Chạm vào ô đất sát mặt nước, cạnh đường có điện";
                int unpowered = _stroke.Count == 0 ? _city.UnpoweredZones : 0;
                if (unpowered > 0) hint = $"Thiếu điện: {unpowered} ô  |  {hint}";
                string uxPrefix = GetUXStatusPrefix();
                if (uxPrefix != null) hint = $"{uxPrefix}  |  {hint}";
                if (_unlockBanner != null)
                {
                    if (Time.unscaledTime < _unlockBannerUntil)
                        hint = $"{_unlockBanner}  |  {hint}";
                    else
                        _unlockBanner = null;
                }
                if (_simSpeed <= 0f) hint = $"Đang tạm dừng  |  {hint}";
                if (_city.TicksInDebt > 0 && !_city.IsBankrupt)
                    hint = $"Cảnh báo phá sản: còn {CitySimulation.BankruptcyTicks - _city.TicksInDebt} lượt · Vay hoặc cắt giảm chi phí  |  {hint}";
                if (_city.IsBankrupt) ShowBankruptcy();
                string debt = _city.LoanBalance > 0 ? $" · Nợ ${_city.LoanBalance:N0}" : "";
                string text = $"${_city.Money:N0} ({_city.LastNetIncome:+#,##0;-#,##0;0}){debt}  |  Dân: {_city.Population:N0}" +
                    $"  |  Việc làm: {_city.Jobs:N0}  |  Nhu cầu  Ở {Signed(_city.ResidentialDemand)}" +
                    $" · TM {Signed(_city.CommercialDemand)} · CN {Signed(_city.IndustrialDemand)}\n{hint}";
                if (text == _lastHudText) return;
                _lastHudText = text;
                _hudStatusText.text = text;
            }
        }
    }
}
