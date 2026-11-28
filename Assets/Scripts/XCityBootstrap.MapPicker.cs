using UnityEngine;
using UnityEngine.UI;

namespace XCity
{
    // Map choice inside the "new city" dialog: style buttons, seed with a random button, and a small
    // preview of the generated coastline with the start area marked.
    public partial class XCityBootstrap
    {
        private const int PreviewSize = 128;
        private static readonly MapStyle[] PickerStyles = { MapStyle.Continent, MapStyle.Islands, MapStyle.Classic };

        private MapStyle _newMapStyle = MapStyle.Continent;
        private int _newMapSeed;
        private RawImage _mapPreview;
        private Texture2D _mapPreviewTexture;
        private Text _mapSeedText;
        private readonly Button[] _mapStyleButtons = new Button[3];

        private MapSpec NewMapSpec => new MapSpec(_newMapStyle, _newMapSeed);

        private void AddMapPicker(Transform dialog)
        {
            if (_newMapSeed == 0) _newMapSeed = Random.Range(1, 1000000);
            GameObject row = AddMenuGroup(dialog, "MapPicker", horizontal: true);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            row.AddComponent<LayoutElement>().minHeight = 150f;

            var frame = new GameObject("Preview", typeof(RectTransform));
            frame.transform.SetParent(row.transform, false);
            var frameSize = frame.AddComponent<LayoutElement>();
            frameSize.minWidth = frameSize.preferredWidth = 150f;
            _mapPreview = frame.AddComponent<RawImage>();
            _mapPreview.raycastTarget = false;

            var controls = new GameObject("Controls", typeof(RectTransform));
            controls.transform.SetParent(row.transform, false);
            controls.AddComponent<LayoutElement>().flexibleWidth = 1f;
            AddMenuColumn(controls.transform, 0, 8);
            AddMenuLabel(controls.transform, UiText.MapLabel, UiTheme.SmallSize, 22f, UiTheme.TextMuted);

            GameObject styles = AddMenuGroup(controls.transform, "Styles", horizontal: true);
            styles.AddComponent<LayoutElement>().minHeight = UiTheme.MinTouch;
            for (int i = 0; i < PickerStyles.Length; i++)
            {
                MapStyle style = PickerStyles[i];
                _mapStyleButtons[i] = AddMenuButton(styles.transform, UiText.MapStyleName((int)style), UiIcon.Map, () =>
                {
                    _newMapStyle = style;
                    RefreshMapPicker();
                }, UiTheme.Button);
            }

            GameObject seedRow = AddMenuGroup(controls.transform, "Seed", horizontal: true);
            seedRow.AddComponent<LayoutElement>().minHeight = UiTheme.MinTouch;
            _mapSeedText = AddMenuLabel(seedRow.transform, "", UiTheme.SmallSize, UiTheme.MinTouch, UiTheme.TextSoft);
            AddMenuButton(seedRow.transform, UiText.RandomMap, UiIcon.Recycling, () =>
            {
                _newMapSeed = Random.Range(1, 1000000);
                RefreshMapPicker();
            }, UiTheme.Button);

            RefreshMapPicker();
        }

        private void RefreshMapPicker()
        {
            for (int i = 0; i < PickerStyles.Length; i++)
                _mapStyleButtons[i].image.color = PickerStyles[i] == _newMapStyle ? UiTheme.Active : UiTheme.Button;
            _mapSeedText.text = _newMapStyle == MapStyle.Classic ? UiText.ClassicMapNote : UiText.MapSeed(_newMapSeed);

            float[] height = MapGenerator.Generate(NewMapSpec, out var start, out var water, out _);
            if (_mapPreviewTexture == null)
                _mapPreviewTexture = new Texture2D(PreviewSize, PreviewSize, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            int scale = CitySimulation.GridSize / PreviewSize;
            var pixels = new Color32[PreviewSize * PreviewSize];
            var sea = new Color32(24, 64, 90, 255);
            var fresh = new Color32(69, 155, 192, 255);
            var low = new Color32(79, 138, 75, 255);
            var high = new Color32(143, 179, 106, 255);
            var mark = new Color32(245, 196, 81, 255);
            int half = MapGenerator.StartArea / 2;
            for (int px = 0; px < PreviewSize; px++)
            for (int pz = 0; pz < PreviewSize; pz++)
            {
                int x = px * scale, z = pz * scale;
                int index = x * CitySimulation.GridSize + z;
                float h = height[index];
                bool startEdge = (Mathf.Abs(x - start.X) == half || Mathf.Abs(z - start.Z) == half) &&
                    Mathf.Abs(x - start.X) <= half && Mathf.Abs(z - start.Z) <= half;
                bool hasFreshWater = false;
                for (int dx = 0; dx < scale && !hasFreshWater; dx++)
                for (int dz = 0; dz < scale && !hasFreshWater; dz++)
                    hasFreshWater = water[index + dx * CitySimulation.GridSize + dz] is WaterKind.River or WaterKind.Lake;
                pixels[pz * PreviewSize + px] = startEdge ? mark : hasFreshWater ? fresh : h < 0f ? sea
                    : Color32.Lerp(low, high, Mathf.Clamp01(h * 1.4f));
            }
            _mapPreviewTexture.SetPixels32(pixels);
            _mapPreviewTexture.Apply(false);
            _mapPreview.texture = _mapPreviewTexture;
        }
    }
}
