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
            Shader std = Shader.Find("Standard") ?? Shader.Find("Unlit/Color");
            _groundMat = MakeMat(std, new Color(0.36f, 0.58f, 0.32f));
            _roadMat = MakeMat(std, new Color(0.18f, 0.20f, 0.23f));
            _resMat = MakeMat(std, new Color(0.24f, 0.72f, 0.36f));
            _comMat = MakeMat(std, new Color(0.20f, 0.52f, 0.88f));
            _indMat = MakeMat(std, new Color(0.88f, 0.68f, 0.20f));
            _bldgResMat = MakeMat(std, new Color(0.88f, 0.84f, 0.76f));
            _bldgComMat = MakeMat(std, new Color(0.62f, 0.78f, 0.92f));
            _bldgIndMat = MakeMat(std, new Color(0.65f, 0.56f, 0.45f));
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
            canvasObj.AddComponent<GraphicRaycaster>();

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            var topBar = CreatePanel(canvasObj.transform, "TopBar", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -64), new Vector2(0, 0), new Color(0.08f, 0.10f, 0.14f, 0.85f));
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

            var bottomBar = CreatePanel(canvasObj.transform, "BottomBar", new Vector2(0.1f, 0), new Vector2(0.9f, 0), new Vector2(0, 14), new Vector2(0, 88), new Color(0.08f, 0.10f, 0.14f, 0.88f));
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
            HandleInput();
            RunSimulationTick();
        }

        private void HandleInput()
        {
            if (Input.touchCount == 2)
            {
                Touch t0 = Input.GetTouch(0);
                Touch t1 = Input.GetTouch(1);

                Vector2 prev0 = t0.position - t0.deltaPosition;
                Vector2 prev1 = t1.position - t1.deltaPosition;

                float prevDist = Vector2.Distance(prev0, prev1);
                float currDist = Vector2.Distance(t0.position, t1.position);
                float deltaZoom = (prevDist - currDist) * 0.08f;
                _camDistance = Mathf.Clamp(_camDistance + deltaZoom, 14f, 110f);

                float prevAngle = Mathf.Atan2(prev1.y - prev0.y, prev1.x - prev0.x) * Mathf.Rad2Deg;
                float currAngle = Mathf.Atan2(t1.y - t0.y, t1.x - t0.x) * Mathf.Rad2Deg;
                _camYaw -= Mathf.DeltaAngle(prevAngle, currAngle);

                UpdateCameraTransform();
                return;
            }

            bool pointerDown = Input.GetMouseButtonDown(0);
            bool pointerHeld = Input.GetMouseButton(0);
            bool pointerUp = Input.GetMouseButtonUp(0);

            if (pointerDown && IsPointerOverUI()) return;

            if (pointerDown)
            {
                _isDragging = true;
                _lastPointerPos = Input.mousePosition;
                if (_activeTool != BuildTool.Pan)
                {
                    TryApplyToolAtScreenPos(Input.mousePosition);
                }
            }
            else if (pointerHeld && _isDragging)
            {
                Vector2 curr = Input.mousePosition;
                Vector2 delta = curr - _lastPointerPos;
                _lastPointerPos = curr;

                if (_activeTool == BuildTool.Pan)
                {
                    Vector3 right = Vector3.ProjectOnPlane(_cam.transform.right, Vector3.up).normalized;
                    Vector3 forward = Vector3.ProjectOnPlane(_cam.transform.forward, Vector3.up).normalized;
                    float panSpeed = _camDistance * 0.0022f;
                    _camTarget -= (right * delta.x + forward * delta.y) * panSpeed;
                    float maxBound = GridSize * CellSize;
                    _camTarget.x = Mathf.Clamp(_camTarget.x, 0f, maxBound);
                    _camTarget.z = Mathf.Clamp(_camTarget.z, 0f, maxBound);
                    UpdateCameraTransform();
                }
                else if (!IsPointerOverUI())
                {
                    TryApplyToolAtScreenPos(curr);
                }
            }
            else if (pointerUp)
            {
                _isDragging = false;
            }

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                _camDistance = Mathf.Clamp(_camDistance - scroll * 3f, 14f, 110f);
                UpdateCameraTransform();
            }
        }

        private static bool IsPointerOverUI()
        {
            if (EventSystem.current == null) return false;
            if (Input.touchCount > 0)
                return EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId);
            return EventSystem.current.IsPointerOverGameObject();
        }

        private void TryApplyToolAtScreenPos(Vector2 screenPos)
        {
            Ray ray = _cam.ScreenPointToRay(screenPos);
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
            if (!groundPlane.Raycast(ray, out float enter)) return;

            Vector3 hit = ray.GetPoint(enter);
            int gx = Mathf.FloorToInt(hit.x / CellSize);
            int gz = Mathf.FloorToInt(hit.z / CellSize);
            if (gx < 0 || gx >= GridSize || gz < 0 || gz >= GridSize) return;

            ApplyCellTool(gx, gz);
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

            int cost = targetType == CellType.Road ? 100 : 50;
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
                _hudStatusText.text = $"XCity  |  Ngân sách: ${_money:N0}  |  Dân số: {_population:N0}  |  Chạm để đặt đường & phân khu cạnh đường";
            }
        }
    }
}
