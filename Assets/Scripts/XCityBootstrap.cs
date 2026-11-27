using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
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
        School
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
        private ConstructionSystem _construction;
        private VehicleSystem _vehicles;
        private CitizenSystem _citizens;
        private BuildTool _activeTool = BuildTool.Road;
        private Camera _cam;
        private Vector3 _camTarget;
        private float _camDistance = 42f;
        private float _camPitch = 48f;
        private float _camYaw = 45f;

        private float _simTimer;

        private Text _hudStatusText;
        private Button _undoButton;
        private Button _catalogButton;
        private GameObject _catalog;
        private GameObject _catalogOptions;
        private Image _catalogTab;
        private BuildingModel _activeBuildingModel;
        private readonly Dictionary<BuildTool, Image> _toolButtons = new Dictionary<BuildTool, Image>();

        private Material _groundMat;
        private Material _roadMat;
        private Material _shoreMat;
        private Material _waterMat;
        private Mesh _waterMarks;
        private Material _vertexColorMat;

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
            Application.targetFrameRate = 30;
            _save = new CitySave(Application.persistentDataPath);
            _previewOutput = GetPreviewOutputArg();
            _city = _previewOutput != null ? XCityPreviewCity.Create() : _save.Load();
            _nextAutosave = Time.unscaledTime + 15f;
            CreateMaterials();
            SetupEnvironment();
            SetupCamera();
            SetupHUD();
            if (_previewOutput != null) StartCoroutine(CapturePreviewShots());
        }

        private void CreateMaterials()
        {
            Shader shader = Resources.Load<Shader>("XCityWorld");
            _groundMat = MakeMat(shader, new Color(0.31f, 0.45f, 0.32f));
            _shoreMat = MakeMat(shader, new Color(0.52f, 0.58f, 0.43f));
            _waterMat = MakeMat(shader, new Color(0.37f, 0.58f, 0.64f));
            _roadMat = MakeMat(shader, new Color(0.18f, 0.20f, 0.23f));
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
            var light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.96f;
            light.color = new Color(1f, 0.96f, 0.88f);
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.64f;
            lightObj.transform.SetParent(transform);
            lightObj.transform.rotation = Quaternion.Euler(56f, -34f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.65f, 0.73f, 0.81f);
            RenderSettings.ambientEquatorColor = new Color(0.48f, 0.55f, 0.50f);
            RenderSettings.ambientGroundColor = new Color(0.31f, 0.34f, 0.32f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.69f, 0.82f, 0.86f);
            RenderSettings.fogStartDistance = 120f;
            RenderSettings.fogEndDistance = 265f;

            float worldSpan = GridSize * CellSize;
            AddLandscapeBase("City island", worldSpan, worldSpan, 0f, 2.5f, _groundMat);
            AddLandscapeBase("Shore", worldSpan, worldSpan + 1.7f, -0.58f, 1.8f, _shoreMat);
            AddLandscapeBase("Water", worldSpan, 800f, -1.05f, 0.2f, _waterMat);
            Shader vc = Resources.Load<Shader>("XCityVertexColor");
            if (vc == null)
            {
                Debug.LogError("Could not load XCityVertexColor shader; falling back to XCityWorld.");
                vc = Resources.Load<Shader>("XCityWorld");
            }
            _vertexColorMat = new Material(vc);
            AddWaterMarks(worldSpan);
            _cityRenderer = new CityChunkRenderer(_city, CellSize, _vertexColorMat, transform);
            _buildingRenderer = new BuildingChunkRenderer(_city, CellSize, _vertexColorMat, transform);
            _construction = new ConstructionSystem(_city, _buildingRenderer, CellSize, _vertexColorMat, transform);
            _vehicles = new VehicleSystem(_city, CellSize, _vertexColorMat, transform);
            _citizens = new CitizenSystem(_city, CellSize, _vertexColorMat, transform);
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

        private void AddWaterMarks(float worldSpan)
        {
            var builder = new MeshBuilder();
            var center = new Vector3(worldSpan * 0.5f, -1.035f, worldSpan * 0.5f);
            for (int side = 0; side < 4; side++)
            {
                builder.SetFrame(center, side);
                for (int i = 0; i < 48; i++)
                {
                    uint seed = BuildingMeshFactory.Hash(side, i, 480);
                    float x = (seed & 255u) / 255f * (worldSpan + 28f) - (worldSpan * 0.5f + 14f);
                    float z = worldSpan * 0.5f + 1.4f + (seed >> 8 & 255u) / 255f * 23f;
                    float width = 0.65f + (seed >> 16 & 255u) / 255f * 2.1f;
                    var tint = new Color32(109, 163, 170, 255);
                    builder.AddQuad(new Vector3(x, 0, z), new Vector3(x + 0.18f, 0, z + 0.10f),
                        new Vector3(x + width, 0, z + 0.08f), new Vector3(x + width - 0.19f, 0, z),
                        Vector3.up, tint);
                }
            }

            _waterMarks = new Mesh { name = "Shoreline ripples" };
            builder.ApplyTo(_waterMarks);
            var waterDetails = new GameObject("Shoreline ripples");
            waterDetails.transform.SetParent(transform, false);
            waterDetails.AddComponent<MeshFilter>().sharedMesh = _waterMarks;
            var renderer = waterDetails.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _vertexColorMat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void SetupCamera()
        {
            var camObj = new GameObject("MainCamera");
            camObj.tag = "MainCamera";
            _cam = camObj.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = RenderSettings.fogColor;
            _cam.nearClipPlane = 0.5f;
            _cam.farClipPlane = 500f;
            camObj.AddComponent<AudioListener>();

            float center = GridSize * CellSize * 0.5f;
            _camTarget = new Vector3(center, 0f, center);
            UpdateCameraTransform();
        }

        private void UpdateCameraTransform()
        {
            Quaternion rot = Quaternion.Euler(_camPitch, _camYaw, 0f);
            Vector3 offset = rot * new Vector3(0f, 0f, -_camDistance);
            _cam.transform.position = _camTarget + offset;
            _cam.transform.LookAt(_camTarget);
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
            statusRect.offsetMax = new Vector2(-188, 0);
            _hudStatusText = statusObj.AddComponent<Text>();
            _hudStatusText.font = font;
            _hudStatusText.fontSize = 21;
            _hudStatusText.color = Color.white;
            _hudStatusText.alignment = TextAnchor.MiddleLeft;

            _undoButton = CreateHudButton(topBar.transform, font, "Hoàn tác", "UndoBuild");
            var undoRect = (RectTransform)_undoButton.transform;
            undoRect.anchorMin = new Vector2(1, 0);
            undoRect.anchorMax = Vector2.one;
            undoRect.offsetMin = new Vector2(-172, 8);
            undoRect.offsetMax = new Vector2(-12, -8);
            _undoButton.onClick.AddListener(UndoBuild);

            var bottomBar = CreatePanel(_safeAreaRoot, "BottomBar", new Vector2(0.1f, 0), new Vector2(0.9f, 0), new Vector2(0, 14), new Vector2(0, 88), new Color(0.08f, 0.10f, 0.14f, 0.88f));
            var layout = bottomBar.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10;
            layout.padding = new RectOffset(12, 12, 10, 10);
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            AddToolButton(bottomBar.transform, font, "Xem", BuildTool.Pan);
            AddToolButton(bottomBar.transform, font, "Đường", BuildTool.Road);
            _catalogButton = CreateHudButton(bottomBar.transform, font, "Xây", "OpenCatalog");
            _catalogButton.onClick.AddListener(ToggleCatalog);
            SetupBuildCatalog(font);
            AddToolButton(bottomBar.transform, font, "Nhà máy điện", BuildTool.PowerPlant);
            AddToolButton(bottomBar.transform, font, "Phá bỏ", BuildTool.Bulldoze);

            SelectTool(BuildTool.Road);
            RefreshHUD();
        }

        private void SetupBuildCatalog(Font font)
        {
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
                BuildingModel.Market, BuildingModel.ShoppingMall);
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
                button.onClick.AddListener(() =>
                {
                    _activeBuildingModel = model;
                    SelectTool(tool);
                });
            }

            options.gameObject.SetActive(false);
            tab.onClick.AddListener(() => ShowCatalogCategory(options.gameObject, tab.image));
            if (_catalogOptions == null) ShowCatalogCategory(options.gameObject, tab.image);
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
            if (_cityDirty && Time.unscaledTime >= _nextAutosave)
                SaveCity();
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
                _camDistance = Mathf.Clamp(_camDistance - scroll * 3f, 14f, 110f);
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
                        _camDistance = Mathf.Clamp(_camDistance * previous.magnitude / current.magnitude, 14f, 110f);
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

        private bool IsSinglePlacement => _activeTool == BuildTool.PowerPlant || _activeTool == BuildTool.School;

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
            if (_activeTool != BuildTool.Pan && !IsSinglePlacement && TryGetCell(position, out var cell))
                AddStrokeSample(cell);
        }

        private void ContinueGesture(Vector2 position)
        {
            if (!_isDragging || IsPointerOverUI(position)) return;

            if (_activeTool == BuildTool.Pan)
            {
                if (!_panMoved && !IsPanMovement(position)) return;
                _panMoved = true;
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
                AddStrokeSample(cell);
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
                _inspectedCell = TryInspectCell(position, out var inspected) ? inspected : (Vector2Int?)null;
                RefreshHUD();
            }

            if (_isDragging && _activeTool != BuildTool.Pan &&
                !IsPointerOverUI(position) && TryGetCell(position, out var cell))
            {
                if (IsSinglePlacement)
                {
                    if (!IsPanMovement(position))
                    {
                        CellType placement = _activeTool == BuildTool.School ? CellType.School : CellType.PowerPlant;
                        _cityDirty |= _city.TrySetCell(cell.x, cell.y, placement, _activeBuildingModel);
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
            if (_stroke.Count == 0) return;
            if (_activeTool == BuildTool.Road)
                for (int i = 0; i < _stroke.Count; i++)
                    _roadMarkers[i].gameObject.SetActive(false);
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
            for (int x = 0; x < GridSize; x++)
            {
                for (int z = 0; z < GridSize; z++)
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

            return !float.IsPositiveInfinity(closest) || TryGetCell(position, out point);
        }

        private bool TryGetCell(Vector2 position, out Vector2Int cell)
        {
            cell = default;
            Ray ray = _cam.ScreenPointToRay(position);
            Plane ground = new Plane(Vector3.up, Vector3.zero);
            if (!ground.Raycast(ray, out float distance)) return false;
            Vector3 hit = ray.GetPoint(distance);
            cell = new Vector2Int(Mathf.FloorToInt(hit.x / CellSize), Mathf.FloorToInt(hit.z / CellSize));
            return cell.x >= 0 && cell.x < GridSize && cell.y >= 0 && cell.y < GridSize;
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
            if (!_strokeCells.Add(cell)) return;

            int index = _stroke.Count;
            _stroke.Add(cell);
            if (_activeTool != BuildTool.Road) return;
            if (_city.GetCell(cell.x, cell.y).Type != CellType.Road) _roadStrokeCost += CitySimulation.RoadCost;
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
            preview.position = new Vector3((cell.x + 0.5f) * CellSize, 0.15f, (cell.y + 0.5f) * CellSize);
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
            _cityDirty |= _city.TryApplyBuild(GetStrokeCells(), type, _activeBuildingModel);
        }

        private IEnumerable<(int X, int Z)> GetStrokeCells()
        {
            foreach (Vector2Int point in _stroke)
                yield return (point.x, point.y);
        }

        private void UndoBuild()
        {
            CancelGesture();
            _cityDirty |= _city.TryUndoBuild();
            RefreshHUD();
        }

        private void RunSimulationTick()
        {
            if (_previewOutput != null) return;
            _simTimer += Time.deltaTime;
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

        private void SaveCity()
        {
            if (_previewOutput != null) return;
            _nextAutosave = Time.unscaledTime + 15f;
            if (_cityDirty && _save.Write(_city)) _cityDirty = false;
        }

        private void LateUpdate()
        {
            Shader.SetGlobalFloat("_XCityTime", Time.time);
            _cityRenderer.RebuildDirtyChunks();
            _construction.Update(Time.deltaTime);
            _buildingRenderer.RebuildDirtyChunks();
            _vehicles.Update(Time.deltaTime);
            _citizens.Update(Time.deltaTime);
        }

        private void OnDestroy()
        {
            _cityRenderer?.Dispose();
            _construction?.Dispose();
            _buildingRenderer?.Dispose();
            _vehicles?.Dispose();
            _citizens?.Dispose();
            Destroy(_vertexColorMat);
            Destroy(_groundMat);
            Destroy(_shoreMat);
            Destroy(_waterMat);
            Destroy(_waterMarks);
            Destroy(_roadMat);
            Destroy(_roadPreviewMat);
            if (_vertexColorMat != null) Destroy(_vertexColorMat);
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
            const float height = 0.18f;
            _inspectionOutline.SetPosition(0, new Vector3(x + inset, height, z + inset));
            _inspectionOutline.SetPosition(1, new Vector3(x + CellSize - inset, height, z + inset));
            _inspectionOutline.SetPosition(2, new Vector3(x + CellSize - inset, height, z + CellSize - inset));
            _inspectionOutline.SetPosition(3, new Vector3(x + inset, height, z + CellSize - inset));
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
                CellType.Road => "Đường",
                CellType.Residential => "Nhà ở",
                CellType.Commercial => "Thương mại",
                CellType.Industrial => "Công nghiệp",
                CellType.PowerPlant => "Nhà máy điện",
                _ => "Ô trống"
            };
            if (type == CellType.Residential && cell.Level > 0)
                label += $" · {BuildingCatalog.Residents(cell)} dân";
            if (type == CellType.School)
                label += $" · {BuildingCatalog.Get(cell.Model).Seats:N0} chỗ · Phí: ${BuildingCatalog.SchoolUpkeep(cell.Model):N0}";
            return status switch
            {
                ZoneStatus.NeedsRoad => label + " · Cần đường liền kề",
                ZoneStatus.WaitingForDevelopment => label + " · Đang chờ phát triển",
                ZoneStatus.DevelopedWithoutRoad => label + " · Mất kết nối đường",
                ZoneStatus.NeedsPower or ZoneStatus.DevelopedWithoutPower => label + " · Thiếu điện",
                ZoneStatus.Developed => label + (type == CellType.School ? " · Đang hoạt động" : " · Đã phát triển"),
                _ => type == CellType.PowerPlant
                    ? label + (_city.Roads.HasRoadAccess(point.x, point.y)
                        ? $" · Cấp {CitySimulation.PowerPlantCapacity} ô" : " · Cần đường liền kề")
                    : label
            };
        }

        private string _lastHudHint;
        private int _lastHudMoney;
        private int _lastHudPopulation;
        private int _lastHudTax;
        private int _lastHudNet;

        private void RefreshHUD()
        {
            if (_undoButton != null) _undoButton.interactable = _city.CanUndoBuild;
            if (_hudStatusText != null)
            {
                if (_activeTool != BuildTool.Pan && _inspectionOutline != null)
                    _inspectionOutline.enabled = false;

                string hint = _activeTool switch
                {
                    BuildTool.Pan => GetInspectionHint(),
                    BuildTool.Road => "Kéo để vẽ đường · Thả để đặt",
                    BuildTool.PowerPlant => $"Chạm đặt nhà máy · {CitySimulation.PowerPlantCost:N0}",
                    BuildTool.Bulldoze => "Kéo qua các ô cần phá bỏ",
                    BuildTool.School => "Chạm để đặt trường học",
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
                int unpowered = _stroke.Count == 0 ? _city.UnpoweredZones : 0;
                if (unpowered > 0) hint = $"Thiếu điện: {unpowered} ô  |  {hint}";
                if (_lastHudHint == hint && _lastHudMoney == _city.Money && _lastHudPopulation == _city.Population &&
                    _lastHudTax == _city.LastTaxIncome && _lastHudNet == _city.LastNetIncome)
                    return;
                _lastHudHint = hint;
                _lastHudMoney = _city.Money;
                _lastHudPopulation = _city.Population;
                _lastHudTax = _city.LastTaxIncome;
                _lastHudNet = _city.LastNetIncome;
                _hudStatusText.text = $"XCity  |  Ngân sách: ${_city.Money:N0}  |  Dân số: {_city.Population:N0}  |  Thuế: {_city.LastTaxIncome:N0}  |  Ròng: {_city.LastNetIncome:+#,##0;-#,##0;0}\n{hint}";
            }
        }
    }
}
