using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace XCity
{
    public partial class XCityBootstrap
    {
        private const int MapResolution = 192;
        private GameObject _worldMapPanel;
        private Texture2D _worldMapTexture;
        private Color32[] _worldMapTerrain;
        private Color32[] _worldMapPixels;

        private void SetupWorldMap(Transform toolbar, Font font)
        {
            Button open = CreateHudButton(toolbar, font, "Bản đồ", "OpenWorldMap");
            open.onClick.AddListener(OpenWorldMap);

            var frame = CreatePanel(_safeAreaRoot, "WorldMap", Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, new Color(0.035f, 0.06f, 0.09f, 0.82f));
            _worldMapPanel = frame;
            var body = CreatePanel(frame.transform, "MapPanel", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-235, -244), new Vector2(235, 244), new Color(0.08f, 0.12f, 0.16f, 1f));

            Button close = CreateHudButton(body.transform, font, "Đóng", "CloseWorldMap");
            var closeRect = (RectTransform)close.transform;
            closeRect.anchorMin = closeRect.anchorMax = new Vector2(1, 1);
            closeRect.offsetMin = new Vector2(-88, -47);
            closeRect.offsetMax = new Vector2(-12, -10);
            close.onClick.AddListener(() => frame.SetActive(false));

            var title = new GameObject("MapTitle", typeof(RectTransform));
            title.transform.SetParent(body.transform, false);
            var titleRect = (RectTransform)title.transform;
            titleRect.anchorMin = new Vector2(0, 1);
            titleRect.anchorMax = Vector2.one;
            titleRect.offsetMin = new Vector2(18, -48);
            titleRect.offsetMax = new Vector2(-100, -10);
            Text label = title.AddComponent<Text>();
            label.font = font;
            label.fontSize = 19;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleLeft;
            label.raycastTarget = false;
            label.text = $"Thế giới {GridSize} × {GridSize} ô · Chạm để đến";

            var picture = new GameObject("WorldMapImage", typeof(RectTransform));
            picture.transform.SetParent(body.transform, false);
            var rect = (RectTransform)picture.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(-208, -219);
            rect.offsetMax = new Vector2(208, 197);
            var image = picture.AddComponent<RawImage>();
            _worldMapTexture = new Texture2D(MapResolution, MapResolution, TextureFormat.RGBA32, false)
            {
                name = "XCity terrain map",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            image.texture = _worldMapTexture;
            var trigger = picture.AddComponent<EventTrigger>();
            var select = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            select.callback.AddListener(eventData =>
            {
                if (!(eventData is PointerEventData pointer) ||
                    !RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, pointer.position, null, out Vector2 position))
                    return;
                float u = Mathf.InverseLerp(rect.rect.xMin, rect.rect.xMax, position.x);
                float v = Mathf.InverseLerp(rect.rect.yMin, rect.rect.yMax, position.y);
                float span = GridSize * CellSize;
                _camTarget = new Vector3(u * span, 0f, v * span);
                UpdateCameraTransform();
                frame.SetActive(false);
            });
            trigger.triggers.Add(select);
            frame.SetActive(false);
        }

        private void OpenWorldMap()
        {
            CancelGesture();
            _catalog.SetActive(false);
            RefreshWorldMap();
            _worldMapPanel.SetActive(true);
        }

        private void RefreshWorldMap()
        {
            if (_worldMapTerrain == null)
            {
                _worldMapTerrain = new Color32[MapResolution * MapResolution];
                _worldMapPixels = new Color32[_worldMapTerrain.Length];
                var water = new Color32(59, 103, 129, 255);
                var sand = new Color32(167, 153, 104, 255);
                var grass = new Color32(121, 157, 107, 255);
                var trees = new Color32(67, 113, 84, 255);
                for (int py = 0; py < MapResolution; py++)
                {
                    int z = py * GridSize / MapResolution;
                    for (int px = 0; px < MapResolution; px++)
                    {
                        int x = px * GridSize / MapResolution;
                        float level = WorldTerrain.LandHeight(x, z);
                        _worldMapTerrain[py * MapResolution + px] = level < 0f ? water : level < 0.075f
                            ? sand : Color32.Lerp(grass, trees, WorldTerrain.Vegetation(x, z));
                    }
                }
            }

            System.Array.Copy(_worldMapTerrain, _worldMapPixels, _worldMapTerrain.Length);
            var road = new Color32(96, 100, 98, 255);
            var build = new Color32(238, 193, 115, 255);
            foreach (int index in _city.OccupiedIndices)
            {
                int x = index / GridSize;
                int z = index % GridSize;
                int px = x * MapResolution / GridSize;
                int py = z * MapResolution / GridSize;
                int pixel = py * MapResolution + px;
                if (_city.GetCell(x, z).Type != CellType.Road) _worldMapPixels[pixel] = build;
                else if (!_worldMapPixels[pixel].Equals(build)) _worldMapPixels[pixel] = road;
            }
            float span = GridSize * CellSize;
            int markerX = Mathf.Clamp(Mathf.FloorToInt(_camTarget.x / span * MapResolution), 0, MapResolution - 1);
            int markerZ = Mathf.Clamp(Mathf.FloorToInt(_camTarget.z / span * MapResolution), 0, MapResolution - 1);
            var marker = new Color32(249, 250, 236, 255);
            for (int dz = -2; dz <= 2; dz++)
                for (int dx = -2; dx <= 2; dx++)
                    if ((dx == 0 || dz == 0) && markerX + dx >= 0 && markerX + dx < MapResolution &&
                        markerZ + dz >= 0 && markerZ + dz < MapResolution)
                        _worldMapPixels[(markerZ + dz) * MapResolution + markerX + dx] = marker;

            _worldMapTexture.SetPixels32(_worldMapPixels);
            _worldMapTexture.Apply(false);
        }
    }
}
