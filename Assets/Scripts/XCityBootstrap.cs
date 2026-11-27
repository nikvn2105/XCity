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
        Bulldoze
    }

    public enum CellType
    {
        Empty,
        Road,
        Residential,
        Commercial,
        Industrial
    }

    public struct CityCell
    {
        public CellType Type;
        public int Level;
        public GameObject Visual;
        public GameObject Building;
    }

    public class XCityBootstrap : MonoBehaviour
    {
        private const int GridSize = 32;
        private const float CellSize = 4f;
        private const int RoadCost = 100;

        private CityCell[,] _grid;
        private BuildTool _activeTool = BuildTool.Road;
        private Camera _cam;
        private Vector3 _camTarget;
        private float _camDistance = 42f;
        private float _camPitch = 48f;
        private float _camYaw = 45f;

        private int _money = 50000;
        private int _population = 0;
        private float _simTimer = 0f;
        private float _simSpeed = 1f;

        private Text _hudStatusText;
        private readonly Dictionary<BuildTool, Image> _toolButtons = new Dictionary<BuildTool, Image>();

        private Material _groundMat;
        private Material _roadMat;
        private Material _resMat;
        private Material _comMat;
        private Material _indMat;
        private Material _bldgResMat;
        private Material _bldgComMat;
        private Material _bldgIndMat;

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
            Application.targetFrameRate = 60;
            _grid = new CityCell[GridSize, GridSize];
            CreateMaterials();
            SetupEnvironment();
            SetupCamera();
            SetupHUD();
        }

        private void CreateMaterials()
        {
            Shader shader = Resources.Load<Shader>("XCityWorld");
            _groundMat = MakeMat(shader, new Color(0.36f, 0.58f, 0.32f));
            _roadMat = MakeMat(shader, new Color(0.18f, 0.20f, 0.23f));
            _resMat = MakeMat(shader, new Color(0.24f, 0.72f, 0.36f));
            _comMat = MakeMat(shader, new Color(0.20f, 0.52f, 0.88f));
            _indMat = MakeMat(shader, new Color(0.88f, 0.68f, 0.20f));
            _bldgResMat = MakeMat(shader, new Color(0.88f, 0.84f, 0.76f));
            _bldgComMat = MakeMat(shader, new Color(0.62f, 0.78f, 0.92f));
            _bldgIndMat = MakeMat(shader, new Color(0.65f, 0.56f, 0.45f));
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
            light.intensity = 1.15f;
            light.color = new Color(1f, 0.96f, 0.88f);
            light.shadows = LightShadows.Soft;
            lightObj.transform.rotation = Quaternion.Euler(50f, -35f, 0f);

            float worldSpan = GridSize * CellSize;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.position = new Vector3(worldSpan * 0.5f, -0.5f, worldSpan * 0.5f);
            ground.transform.localScale = new Vector3(worldSpan, 1f, worldSpan);
            ground.GetComponent<Renderer>().sharedMaterial = _groundMat;
        }

        private void SetupCamera()
        {
            var camObj = new GameObject("MainCamera");
            camObj.tag = "MainCamera";
            _cam = camObj.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.60f, 0.78f, 0.94f);
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
            statusRect.offsetMax = new Vector2(-20, 0);
            _hudStatusText = statusObj.AddComponent<Text>();
            _hudStatusText.font = font;
            _hudStatusText.fontSize = 24;
            _hudStatusText.color = Color.white;
            _hudStatusText.alignment = TextAnchor.MiddleLeft;

            var bottomBar = CreatePanel(_safeAreaRoot, "BottomBar", new Vector2(0.1f, 0), new Vector2(0.9f, 0), new Vector2(0, 14), new Vector2(0, 88), new Color(0.08f, 0.10f, 0.14f, 0.88f));
            var layout = bottomBar.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10;
            layout.padding = new RectOffset(12, 12, 10, 10);
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            AddToolButton(bottomBar.transform, font, "Xem", BuildTool.Pan);
            AddToolButton(bottomBar.transform, font, "Đường", BuildTool.Road);
            AddToolButton(bottomBar.transform, font, "Nhà ở (R)", BuildTool.Residential);
            AddToolButton(bottomBar.transform, font, "Thương mại (C)", BuildTool.Commercial);
            AddToolButton(bottomBar.transform, font, "Công nghiệp (I)", BuildTool.Industrial);
            AddToolButton(bottomBar.transform, font, "Phá bỏ", BuildTool.Bulldoze);

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

        private void AddToolButton(Transform parent, Font font, string label, BuildTool tool)
        {
            var btnObj = new GameObject("Btn_" + tool);
            btnObj.transform.SetParent(parent, false);
            var img = btnObj.AddComponent<Image>();
            img.color = new Color(0.20f, 0.24f, 0.30f, 1f);
            var btn = btnObj.AddComponent<Button>();
            btn.onClick.AddListener(() => SelectTool(tool));

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

            _toolButtons[tool] = img;
        }

        private void SelectTool(BuildTool tool)
        {
            CancelGesture();
            _activeTool = tool;
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
            HandleInput();
            RunSimulationTick();
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

        private void BeginGesture(Vector2 position)
        {
            if (IsPointerOverUI(position)) return;
            _isDragging = true;
            _lastPointerPos = position;
            if (_activeTool != BuildTool.Pan && TryGetCell(position, out var cell))
                AddStrokeSample(cell);
        }

        private void ContinueGesture(Vector2 position)
        {
            if (!_isDragging || IsPointerOverUI(position)) return;

            if (_activeTool == BuildTool.Pan)
            {
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
            else if (TryGetCell(position, out var cell))
            {
                AddStrokeSample(cell);
            }
            _lastPointerPos = position;
        }

        private void EndGesture(Vector2 position)
        {
            if (_isDragging && _activeTool != BuildTool.Pan &&
                !IsPointerOverUI(position) && TryGetCell(position, out var cell))
            {
                AddStrokeSample(cell);
                if (_activeTool != BuildTool.Road || _roadStrokeCost <= _money)
                    foreach (Vector2Int point in _stroke)
                        ApplyCellTool(point.x, point.y);
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
            if (_grid[cell.x, cell.y].Type != CellType.Road) _roadStrokeCost += RoadCost;
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

        private void ApplyCellTool(int gx, int gz)
        {
            ref CityCell cell = ref _grid[gx, gz];

            if (_activeTool == BuildTool.Bulldoze)
            {
                if (cell.Type == CellType.Empty) return;
                if (cell.Visual != null) Destroy(cell.Visual);
                if (cell.Building != null) Destroy(cell.Building);
                cell = default;
                _money -= 10;
                RefreshHUD();
                return;
            }

            CellType targetType = _activeTool switch
            {
                BuildTool.Road => CellType.Road,
                BuildTool.Residential => CellType.Residential,
                BuildTool.Commercial => CellType.Commercial,
                BuildTool.Industrial => CellType.Industrial,
                _ => CellType.Empty
            };

            if (targetType == CellType.Empty || cell.Type == targetType) return;

            int cost = targetType == CellType.Road ? RoadCost : 50;
            if (_money < cost) return;

            if (cell.Visual != null) Destroy(cell.Visual);
            if (cell.Building != null) Destroy(cell.Building);

            cell.Type = targetType;
            cell.Level = 0;
            _money -= cost;

            Vector3 worldPos = new Vector3((gx + 0.5f) * CellSize, 0.05f, (gz + 0.5f) * CellSize);
            var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tile.name = $"Tile_{gx}_{gz}_{targetType}";
            tile.transform.position = worldPos;
            tile.transform.localScale = new Vector3(CellSize * 0.96f, 0.1f, CellSize * 0.96f);
            Destroy(tile.GetComponent<Collider>());

            tile.GetComponent<Renderer>().sharedMaterial = targetType switch
            {
                CellType.Road => _roadMat,
                CellType.Residential => _resMat,
                CellType.Commercial => _comMat,
                CellType.Industrial => _indMat,
                _ => _indMat
            };

            cell.Visual = tile;
            RefreshHUD();
        }

        private void RunSimulationTick()
        {
            _simTimer += Time.deltaTime * _simSpeed;
            if (_simTimer < 1.5f) return;
            _simTimer = 0f;

            int pop = 0;
            int taxIncome = 0;
            int upkeep = 0;

            for (int x = 0; x < GridSize; x++)
            {
                for (int z = 0; z < GridSize; z++)
                {
                    ref CityCell cell = ref _grid[x, z];
                    if (cell.Type == CellType.Road)
                    {
                        upkeep += 2;
                        continue;
                    }

                    if (cell.Type == CellType.Residential || cell.Type == CellType.Commercial || cell.Type == CellType.Industrial)
                    {
                        if (cell.Building == null && HasAdjacentRoad(x, z) && Random.value < 0.35f)
                        {
                            SpawnBuilding(x, z, ref cell);
                        }

                        if (cell.Building != null)
                        {
                            if (cell.Type == CellType.Residential) pop += 12 * cell.Level;
                            taxIncome += 15 * cell.Level;
                        }
                    }
                }
            }

            _population = pop;
            _money += (taxIncome - upkeep);
            RefreshHUD();
        }

        private bool HasAdjacentRoad(int x, int z)
        {
            int[] dx = { -1, 1, 0, 0 };
            int[] dz = { 0, 0, -1, 1 };
            for (int i = 0; i < 4; i++)
            {
                int nx = x + dx[i];
                int nz = z + dz[i];
                if (nx >= 0 && nx < GridSize && nz >= 0 && nz < GridSize && _grid[nx, nz].Type == CellType.Road)
                    return true;
            }
            return false;
        }

        private void SpawnBuilding(int x, int z, ref CityCell cell)
        {
            cell.Level = 1;
            float height = cell.Type switch
            {
                CellType.Residential => Random.Range(2.4f, 4.5f),
                CellType.Commercial => Random.Range(4.0f, 7.5f),
                _ => Random.Range(2.2f, 3.8f)
            };

            var bldg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bldg.name = $"Building_{x}_{z}";
            bldg.transform.position = new Vector3((x + 0.5f) * CellSize, height * 0.5f + 0.1f, (z + 0.5f) * CellSize);
            bldg.transform.localScale = new Vector3(CellSize * 0.72f, height, CellSize * 0.72f);
            Destroy(bldg.GetComponent<Collider>());

            bldg.GetComponent<Renderer>().sharedMaterial = cell.Type switch
            {
                CellType.Residential => _bldgResMat,
                CellType.Commercial => _bldgComMat,
                _ => _bldgIndMat
            };

            cell.Building = bldg;
        }

        private void RefreshHUD()
        {
            if (_hudStatusText != null)
            {
                string hint = "Kéo để vẽ đường · Thả để đặt";
                if (_activeTool == BuildTool.Road && _stroke.Count > 0)
                {
                    bool affordable = _roadStrokeCost <= _money;
                    _roadPreviewMat.color = affordable ? Color.green : Color.red;
                    hint = affordable ? $"Chi phí: ${_roadStrokeCost:N0}" : $"Thiếu ngân sách · Chi phí: ${_roadStrokeCost:N0}";
                }
                _hudStatusText.text = $"XCity  |  Ngân sách: ${_money:N0}  |  Dân số: {_population:N0}  |  {hint}";
            }
        }
    }
}
