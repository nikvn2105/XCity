using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace XCity
{
    public partial class XCityBootstrap
    {
        private static readonly string[] GraphicsLabels = { "Thấp", "Trung bình", "Cao", "Cực cao" };
        private static readonly int[] FrameRates = { 30, 60, 120 };
        private const string GraphicsKey = "xcity.graphics";
        private const string FrameRateKey = "xcity.fps";
        private const string FpsCounterKey = "xcity.fpscounter";
        private const float FpsSampleSeconds = 0.5f;
        private const int DefaultGraphics = 2;
        private const int DefaultFrameRate = 30;

        private int _graphicsLevel = DefaultGraphics;
        private int _frameRate = DefaultFrameRate;
        private GameObject _settingsPanel;
        private readonly Image[] _graphicsButtons = new Image[GraphicsLabels.Length];
        private readonly Image[] _frameRateButtons = new Image[FrameRates.Length];

        private Button _fpsCounterButton;
        private Text _fpsText;
        private float _fpsWindow;
        private int _fpsFrames;
        private float _fpsWorst;

        private bool SettingsOpen => _settingsPanel != null && _settingsPanel.activeSelf;

        private void LoadDisplaySettings()
        {
            if (_previewOutput == null)
            {
                _graphicsLevel = Mathf.Clamp(PlayerPrefs.GetInt(GraphicsKey, DefaultGraphics), 0, GraphicsLabels.Length - 1);
                int saved = PlayerPrefs.GetInt(FrameRateKey, DefaultFrameRate);
                _frameRate = Array.IndexOf(FrameRates, saved) >= 0 ? saved : DefaultFrameRate;
            }
            ApplyDisplaySettings();
        }

        private void ApplyDisplaySettings()
        {
            Application.targetFrameRate = _frameRate;
            QualitySettings.vSyncCount = 0;

            // Retuned after an iPhone 14 Pro Max held 120 FPS at the old Ultra: High and Ultra now spend that headroom.
            var (shadows, resolution, distance, cascades, msaa, renderScale, detail) = _graphicsLevel switch
            {
                0 => (LightShadows.None, ShadowResolution.Low, 0f, 1, 0, 0.6f, 1f),
                1 => (LightShadows.Soft, ShadowResolution.Low, 100f, 2, 0, 0.8f, 1f),
                2 => (LightShadows.Soft, ShadowResolution.High, 220f, 2, 4, 1f, 1.25f),
                _ => (LightShadows.Soft, ShadowResolution.VeryHigh, 400f, 4, 4, 1f, 1.6f)
            };
            if (_chunkScheduler != null) _chunkScheduler.DetailScale = detail;
            if (_sunLight != null) _sunLight.shadows = shadows;
            QualitySettings.shadows = shadows == LightShadows.None ? ShadowQuality.Disable : ShadowQuality.All;
            QualitySettings.shadowResolution = resolution;
            QualitySettings.shadowDistance = distance;
            QualitySettings.shadowCascades = cascades;
            QualitySettings.antiAliasing = msaa;
            Shader.SetGlobalFloat("_XCityQuality", _graphicsLevel);

            // Rendering fewer pixels is the biggest GPU saving on a 2796x1290 panel. Preview captures keep their size.
            if (_previewOutput == null && !Application.isEditor)
            {
                int longSide = Mathf.Max(Display.main.systemWidth, Display.main.systemHeight);
                int shortSide = Mathf.Min(Display.main.systemWidth, Display.main.systemHeight);
                Screen.SetResolution(Mathf.RoundToInt(longSide * renderScale), Mathf.RoundToInt(shortSide * renderScale), true);
            }
        }

        private void SetGraphicsLevel(int level)
        {
            _graphicsLevel = level;
            PlayerPrefs.SetInt(GraphicsKey, level);
            PlayerPrefs.Save();
            ApplyDisplaySettings();
            RefreshSettingsButtons();
        }

        private void SetFrameRate(int fps)
        {
            _frameRate = fps;
            PlayerPrefs.SetInt(FrameRateKey, fps);
            PlayerPrefs.Save();
            ApplyDisplaySettings();
            RefreshSettingsButtons();
        }

        private void ToggleSettings()
        {
            CancelGesture();
            if (_settingsPanel == null) BuildSettingsPanel();
            else _settingsPanel.SetActive(!_settingsPanel.activeSelf);
            RefreshSettingsButtons();
        }

        private void BuildSettingsPanel()
        {
            Font font = Resources.Load<Font>("NotoSans-Regular");
            _settingsPanel = CreatePanel(_safeAreaRoot, "Settings", new Vector2(0.2f, 0.14f), new Vector2(0.8f, 0.86f),
                Vector2.zero, Vector2.zero, new Color(0.06f, 0.08f, 0.12f, 0.96f));
            var layout = _settingsPanel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 14, 14);
            layout.spacing = 10;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            AddSettingsLabel(font, "Đồ họa");
            Transform graphicsRow = AddSettingsRow();
            for (int i = 0; i < GraphicsLabels.Length; i++)
            {
                int level = i;
                Button button = CreateHudButton(graphicsRow, font, GraphicsLabels[i], "Graphics_" + i);
                button.onClick.AddListener(() => SetGraphicsLevel(level));
                _graphicsButtons[i] = button.image;
            }

            AddSettingsLabel(font, "Tốc độ khung hình (FPS) · cao hơn thì mượt hơn nhưng nóng máy và tốn pin hơn");
            Transform fpsRow = AddSettingsRow();
            for (int i = 0; i < FrameRates.Length; i++)
            {
                int fps = FrameRates[i];
                Button button = CreateHudButton(fpsRow, font, fps + " FPS", "Fps_" + fps);
                button.onClick.AddListener(() => SetFrameRate(fps));
                _frameRateButtons[i] = button.image;
            }

            _fpsCounterButton = CreateHudButton(_settingsPanel.transform, font, "", "FpsCounter");
            KeepTall(_fpsCounterButton.transform);
            _fpsCounterButton.onClick.AddListener(() => SetFpsCounter(_fpsText == null || !_fpsText.gameObject.activeSelf));

            Button close = CreateHudButton(_settingsPanel.transform, font, "Đóng", "CloseSettings");
            KeepTall(close.transform);
            close.onClick.AddListener(ToggleSettings);

            void AddSettingsLabel(Font labelFont, string label)
            {
                var go = new GameObject("Label", typeof(RectTransform));
                go.transform.SetParent(_settingsPanel.transform, false);
                var text = go.AddComponent<Text>();
                text.font = labelFont;
                text.fontSize = 18;
                text.color = new Color(0.82f, 0.86f, 0.92f);
                text.alignment = TextAnchor.LowerLeft;
                text.text = label;
            }

            // Unity hides a Text that does not fit its rect, so rows keep enough height for the 20pt button labels.
            void KeepTall(Transform item) => item.gameObject.AddComponent<LayoutElement>().minHeight = 44f;

            Transform AddSettingsRow()
            {
                var row = new GameObject("Row", typeof(RectTransform));
                row.transform.SetParent(_settingsPanel.transform, false);
                var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
                rowLayout.spacing = 8;
                rowLayout.childForceExpandWidth = true;
                rowLayout.childForceExpandHeight = true;
                KeepTall(row.transform);
                return row.transform;
            }
        }

        // Called after SetupHUD: the counter needs the safe-area root.
        private void RestoreFpsCounter()
        {
            if (_previewOutput == null && PlayerPrefs.GetInt(FpsCounterKey, 0) == 1) SetFpsCounter(true);
        }

        private void SetFpsCounter(bool visible)
        {
            if (_fpsText == null)
            {
                var go = new GameObject("FpsCounter", typeof(RectTransform));
                go.transform.SetParent(_safeAreaRoot, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 0f);
                rect.anchoredPosition = new Vector2(12f, 108f);
                rect.sizeDelta = new Vector2(420f, 28f);
                _fpsText = go.AddComponent<Text>();
                _fpsText.font = Resources.Load<Font>("NotoSans-Regular");
                _fpsText.fontSize = 18;
                _fpsText.color = new Color(1f, 0.95f, 0.55f);
                _fpsText.raycastTarget = false;
            }
            _fpsText.gameObject.SetActive(visible);
            _fpsWindow = _fpsFrames = 0;
            _fpsWorst = 0f;
            PlayerPrefs.SetInt(FpsCounterKey, visible ? 1 : 0);
            PlayerPrefs.Save();
            RefreshSettingsButtons();
        }

        // Average FPS hides hitches, so the slowest frame of each half second is shown too.
        private void UpdateFpsCounter()
        {
            if (_fpsText == null || !_fpsText.gameObject.activeSelf) return;
            float dt = Time.unscaledDeltaTime;
            _fpsWindow += dt;
            _fpsFrames++;
            _fpsWorst = Mathf.Max(_fpsWorst, dt);
            if (_fpsWindow < FpsSampleSeconds) return;
            _fpsText.text = $"FPS {_fpsFrames / _fpsWindow:0} · {_fpsWindow * 1000f / _fpsFrames:0.0} ms · chậm nhất {_fpsWorst * 1000f:0} ms";
            _fpsWindow = _fpsFrames = 0;
            _fpsWorst = 0f;
        }

        private void RefreshSettingsButtons()
        {
            var active = new Color(0.19f, 0.52f, 0.96f);
            var idle = new Color(0.20f, 0.24f, 0.30f);
            for (int i = 0; i < _graphicsButtons.Length; i++)
                if (_graphicsButtons[i] != null) _graphicsButtons[i].color = i == _graphicsLevel ? active : idle;
            for (int i = 0; i < _frameRateButtons.Length; i++)
                if (_frameRateButtons[i] != null) _frameRateButtons[i].color = FrameRates[i] == _frameRate ? active : idle;
            if (_fpsCounterButton != null)
            {
                bool on = _fpsText != null && _fpsText.gameObject.activeSelf;
                _fpsCounterButton.GetComponentInChildren<Text>().text = on ? "Hiện FPS: Bật" : "Hiện FPS: Tắt";
                _fpsCounterButton.image.color = on ? active : idle;
            }
        }
    }
}
