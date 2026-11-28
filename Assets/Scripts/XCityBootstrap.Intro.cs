using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace XCity
{
    // Opening: black screen with the logo, then the city fades in while the camera circles it and the
    // menu slides in from the left. A tap anywhere skips to the end. The camera keeps circling while
    // the menu stays open.
    public partial class XCityBootstrap
    {
        private const float IntroLogoIn = 0.6f;
        private const float IntroHoldUntil = 1.5f;
        private const float IntroRevealUntil = 2.3f;
        private const float IntroMenuUntil = 2.9f;
        private const float MenuOrbitDegreesPerSecond = 4f;

        private GameObject _introOverlay;
        private bool _skipIntro;
        private RectTransform _menuWindow;
        private CanvasGroup _menuWindowGroup;

        // Menu and intro share the complete title artwork. The OS icon stays a separate asset.
        private static GameObject CreateTitleArtwork(Transform parent, float height, float fallbackIconSize, int fallbackTextSize)
        {
            var texture = Resources.Load<Texture2D>("XCityTitleArtwork");
            if (texture == null) return CreateLogo(parent, fallbackIconSize, fallbackTextSize);

            var frame = new GameObject("TitleArtwork", typeof(RectTransform));
            frame.transform.SetParent(parent, false);
            var layout = frame.AddComponent<LayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;

            var graphic = new GameObject("Artwork", typeof(RectTransform));
            graphic.transform.SetParent(frame.transform, false);
            var image = graphic.AddComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;
            var fit = graphic.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = (float)texture.width / texture.height;
            return frame;
        }

        // The emblem (rounded) beside a bold "XCITY" wordmark.
        private static GameObject CreateLogo(Transform parent, float iconSize, int textSize)
        {
            var row = new GameObject("Logo", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = iconSize * 0.25f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            row.AddComponent<LayoutElement>().minHeight = iconSize;

            var frame = new GameObject("Emblem", typeof(RectTransform));
            frame.transform.SetParent(row.transform, false);
            var size = frame.AddComponent<LayoutElement>();
            size.minWidth = size.preferredWidth = size.minHeight = size.preferredHeight = iconSize;
            var mask = frame.AddComponent<Image>();
            UiTheme.Round(mask, iconSize * 0.22f, outline: false);
            frame.AddComponent<Mask>().showMaskGraphic = false;
            var art = new GameObject("Art", typeof(RectTransform));
            var artRect = (RectTransform)art.transform;
            artRect.SetParent(frame.transform, false);
            artRect.anchorMin = Vector2.zero;
            artRect.anchorMax = Vector2.one;
            artRect.offsetMin = artRect.offsetMax = Vector2.zero;
            var image = art.AddComponent<RawImage>();
            image.texture = Resources.Load<Texture2D>("XCityLogo");
            image.raycastTarget = false;

            var word = new GameObject("Wordmark", typeof(RectTransform));
            word.transform.SetParent(row.transform, false);
            var text = word.AddComponent<Text>();
            text.font = UiTheme.Font;
            text.fontSize = textSize;
            text.fontStyle = FontStyle.Bold;
            text.color = UiTheme.Text;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.raycastTarget = false;
            text.text = UiText.Wordmark;
            return row;
        }

        // Puts the camera over the middle of the built-up area for the menu orbit.
        private void FrameMenuCamera()
        {
            long sumX = 0, sumZ = 0;
            int count = 0;
            foreach (int index in _city.OccupiedIndices)
            {
                sumX += index / GridSize;
                sumZ += index % GridSize;
                count++;
            }
            float center = GridSize * CellSize * 0.5f;
            _camTarget = count == 0
                ? new Vector3(center, 0f, center)
                : new Vector3((sumX / (float)count + 0.5f) * CellSize, 0f, (sumZ / (float)count + 0.5f) * CellSize);
            _camDistance = Mathf.Clamp(Mathf.Sqrt(count) * CellSize * 0.9f, 60f, 260f);
            _camPitch = 34f;
            UpdateCameraTransform();
        }

        private void OrbitMenuCamera()
        {
            _camYaw += MenuOrbitDegreesPerSecond * Time.unscaledDeltaTime;
            UpdateCameraTransform();
        }

        private void PlayIntro()
        {
            _introOverlay = CreatePanel(_safeAreaRoot.parent, "Intro", Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, Color.black);
            _introOverlay.AddComponent<Button>().onClick.AddListener(() => _skipIntro = true);
            var logoGroup = new GameObject("IntroLogo", typeof(RectTransform), typeof(CanvasGroup));
            var logoRect = (RectTransform)logoGroup.transform;
            logoRect.SetParent(_introOverlay.transform, false);
            logoRect.anchorMin = logoRect.anchorMax = new Vector2(0.5f, 0.5f);
            bool hasArtwork = Resources.Load<Texture2D>("XCityTitleArtwork") != null;
            float height = hasArtwork ? 420f : 160f;
            logoRect.sizeDelta = new Vector2(600f, height);
            var logoRow = (RectTransform)CreateTitleArtwork(logoRect, height, 140f, 84).transform;
            logoRow.anchorMin = Vector2.zero;
            logoRow.anchorMax = Vector2.one;
            logoRow.offsetMin = logoRow.offsetMax = Vector2.zero;
            StartCoroutine(IntroRoutine(_introOverlay.GetComponent<Image>(), logoGroup.GetComponent<CanvasGroup>(), logoRect));
        }

        private IEnumerator IntroRoutine(Image black, CanvasGroup logo, RectTransform logoRect)
        {
            float start = Time.unscaledTime;
            Vector2 windowHome = _menuWindow.anchoredPosition;
            while (true)
            {
                float t = _skipIntro ? IntroMenuUntil : Time.unscaledTime - start;
                float reveal = Smooth(IntroHoldUntil, IntroRevealUntil, t);
                logo.alpha = Smooth(0f, IntroLogoIn, t) * (1f - reveal);
                logoRect.localScale = Vector3.one * (1f + 0.12f * reveal);
                black.color = new Color(0f, 0f, 0f, 1f - reveal);
                float menu = Smooth(IntroRevealUntil - 0.3f, IntroMenuUntil, t);
                _menuWindowGroup.alpha = menu;
                _menuWindow.anchoredPosition = windowHome + new Vector2(-60f * (1f - menu), 0f);
                if (t >= IntroMenuUntil) break;
                yield return null;
            }
            Destroy(_introOverlay);
            _introOverlay = null;
        }

        private static float Smooth(float from, float to, float t) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, t));
    }
}
