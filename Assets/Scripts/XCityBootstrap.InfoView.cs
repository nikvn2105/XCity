using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace XCity
{
    public partial class XCityBootstrap
    {
        private const float MinCamDistance = 7f;
        private const float MaxCamDistance = 2800f;

        private const float InfoPanelWidth = 270f;
        private RectTransform _infoPanel;
        private Text _infoBody;
        private Text _infoTitle;
        private Coroutine _infoSlide;
        private bool _infoOpen;
        private Vector2Int? _infoCell;

        private void UpdateAtmosphereRanges()
        {
            RenderSettings.fogStartDistance = _camDistance <= 55f ? 120f : Mathf.Max(120f, _camDistance * 1.48f);
            RenderSettings.fogEndDistance = _camDistance <= 55f ? 265f : Mathf.Max(265f, _camDistance * 2.50f);
            if (_cam == null) return;
            _cam.nearClipPlane = _camDistance <= 55f ? 0.5f : Mathf.Max(0.5f, _camDistance * 0.015f);
            _cam.farClipPlane = Mathf.Max(320f, _camDistance * 1.85f + 220f);
        }

        private void SetupInfoView()
        {
            Transform ocean = transform.Find("Ocean");
            if (ocean != null)
                ocean.localScale = new Vector3(GridSize * CellSize + 16000f, 0.2f, GridSize * CellSize + 16000f);

            var clip = BuildRect(_safeAreaRoot, "InfoClip", Vector2.zero, Vector2.one,
                new Vector2(0, BuildBarTop), new Vector2(0, -114));
            clip.gameObject.AddComponent<RectMask2D>();
            var panel = CreatePanel(clip, "InfoPanel", new Vector2(1, 0), Vector2.one,
                new Vector2(-InfoPanelWidth, 0), Vector2.zero, UiTheme.PanelSolid);
            _infoPanel = (RectTransform)panel.transform;
            _infoPanel.anchoredPosition += new Vector2(InfoPanelWidth, 0);

            Button close = BuildButton(_infoPanel, "CloseInfo", Vector2.one, Vector2.one,
                new Vector2(-UiTheme.MinTouch - 8, -UiTheme.MinTouch - 8), new Vector2(-8, -8));
            BuildLabel(close.transform, UiText.Close, UiTheme.TinySize, Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero);
            close.onClick.AddListener(CloseInfoView);

            _infoTitle = BuildLabel(_infoPanel, UiText.InfoTitle, UiTheme.BodySize, new Vector2(0, 1), Vector2.one,
                new Vector2(12, -UiTheme.MinTouch - 8), new Vector2(-UiTheme.MinTouch - 16, -8),
                TextAnchor.MiddleLeft);

            var scrollArea = BuildDecoration(_infoPanel, "InfoScroll", Vector2.zero, Vector2.one,
                new Vector2(10, 8), new Vector2(-10, -UiTheme.MinTouch - 14), UiTheme.PanelSolid);
            scrollArea.raycastTarget = true;
            var viewport = scrollArea.rectTransform;
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            _infoBody = BuildLabel(viewport, "", UiTheme.SmallSize, new Vector2(0, 1), Vector2.one,
                new Vector2(3, 0), new Vector2(-3, 0), TextAnchor.UpperLeft);
            _infoBody.rectTransform.pivot = new Vector2(0.5f, 1f);
            _infoBody.resizeTextForBestFit = false;
            _infoBody.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = _infoBody.rectTransform;

            _infoPanel.gameObject.SetActive(false);
            RefreshInfoViewHUD();
        }

        private void CloseInfoView()
        {
            _inspectedCell = null;
            if (_cityRenderer.InfoViewEnabled) SetBuildBarOverlay(CityOverlayMode.None);
            else RefreshHUD();
        }

        private void RefreshInfoViewHUD()
        {
            if (_infoPanel == null) return;
            bool inspecting = _activeTool == BuildTool.Pan && _inspectedCell.HasValue;
            bool showLegend = _cityRenderer != null && _cityRenderer.InfoViewEnabled;
            bool show = inspecting || showLegend;
            if (show)
            {
                string title = inspecting ? UiText.InfoTitle : UiText.Happiness;
                Vector2Int? cell = inspecting ? _inspectedCell : null;
                if (_infoTitle.text != title || _infoCell != cell)
                    _infoBody.rectTransform.anchoredPosition = Vector2.zero;
                _infoCell = cell;
                _infoTitle.text = title;
                string body = inspecting ? GetInspectionHint().Replace(" · ", "\n") : UiText.OverlayLegend;
                if (inspecting && showLegend) body += "\n\n" + UiText.OverlayLegend;
                _infoBody.text = body;
            }

            if (show == _infoOpen) return;
            _infoOpen = show;
            if (_infoSlide != null) StopCoroutine(_infoSlide);
            if (show) _infoPanel.gameObject.SetActive(true);
            _infoSlide = StartCoroutine(SlideInfoView(show));
        }

        private IEnumerator SlideInfoView(bool open)
        {
            float shownX = -InfoPanelWidth * _infoPanel.pivot.x;
            float target = open ? shownX : shownX + InfoPanelWidth;
            while (_infoPanel.anchoredPosition.x != target)
            {
                Vector2 position = _infoPanel.anchoredPosition;
                position.x = Mathf.MoveTowards(position.x, target, 1600f * Time.unscaledDeltaTime);
                _infoPanel.anchoredPosition = position;
                yield return null;
            }
            if (!open) _infoPanel.gameObject.SetActive(false);
            _infoSlide = null;
        }
    }
}
