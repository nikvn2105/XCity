using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace XCity
{
    public partial class XCityBootstrap
    {
        private const float MinCamDistance = 7f;
        private const float MaxCamDistance = 2800f;

        private const float InfoPanelWidth = 290f;
        private RectTransform _infoPanel;
        private Text _infoBody;
        private Text _infoTitle;
        private Text _infoSubtitle;
        private Image _infoBadge;
        private Text _infoGlyph;
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

            // Clip keeps a 16-unit margin from the right edge; the panel slides in from behind it.
            var clip = BuildRect(_safeAreaRoot, "InfoClip", Vector2.zero, Vector2.one,
                new Vector2(0, BuildBarTop + 6), new Vector2(-16, -78));
            clip.gameObject.AddComponent<RectMask2D>();
            var panel = CreatePanel(clip, "InfoPanel", new Vector2(1, 0), Vector2.one,
                new Vector2(-InfoPanelWidth, 0), Vector2.zero,
                new Color(UiTheme.Panel.r, UiTheme.Panel.g, UiTheme.Panel.b, 0.92f));
            UiTheme.Round(panel.GetComponent<Image>(), 16f);
            _infoPanel = (RectTransform)panel.transform;
            _infoPanel.anchoredPosition += new Vector2(InfoPanelWidth, 0);

            _infoBadge = BuildDecoration(_infoPanel, "Badge", new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(14, -54), new Vector2(54, -14), UiTheme.Button);
            UiTheme.Round(_infoBadge, 10f, outline: false);
            _infoGlyph = BuildLabel(_infoBadge.transform, "", 24, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            _infoGlyph.font = UiTheme.IconFont;
            _infoGlyph.resizeTextForBestFit = false;

            _infoTitle = BuildLabel(_infoPanel, UiText.InfoTitle, UiTheme.SmallSize + 1, new Vector2(0, 1), Vector2.one,
                new Vector2(64, -36), new Vector2(-58, -12), TextAnchor.MiddleLeft);
            _infoTitle.fontStyle = FontStyle.Bold;
            _infoSubtitle = BuildLabel(_infoPanel, "", UiTheme.TinySize - 1, new Vector2(0, 1), Vector2.one,
                new Vector2(64, -56), new Vector2(-58, -36), TextAnchor.MiddleLeft);
            _infoSubtitle.color = UiTheme.TextMuted;

            Button close = CreateIconButton(_infoPanel, UiIcon.Close, "CloseInfo", 20);
            var closeRect = (RectTransform)close.transform;
            closeRect.anchorMin = closeRect.anchorMax = Vector2.one;
            closeRect.offsetMin = new Vector2(-UiTheme.MinTouch - 10, -UiTheme.MinTouch - 10);
            closeRect.offsetMax = new Vector2(-10, -10);
            close.onClick.AddListener(CloseInfoView);

            var divider = BuildDecoration(_infoPanel, "Divider", new Vector2(0, 1), Vector2.one,
                new Vector2(14, -67), new Vector2(-14, -66), UiTheme.Divider);
            divider.raycastTarget = false;

            var scrollArea = BuildDecoration(_infoPanel, "InfoScroll", Vector2.zero, Vector2.one,
                new Vector2(14, 12), new Vector2(-14, -74), Color.clear);
            scrollArea.raycastTarget = true;
            var viewport = scrollArea.rectTransform;
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            _infoBody = BuildLabel(viewport, "", UiTheme.TinySize + 1, new Vector2(0, 1), Vector2.one,
                Vector2.zero, Vector2.zero, TextAnchor.UpperLeft);
            _infoBody.rectTransform.pivot = new Vector2(0.5f, 1f);
            _infoBody.resizeTextForBestFit = false;
            _infoBody.lineSpacing = 1.25f;
            _infoBody.color = UiTheme.TextSoft;
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
                Vector2Int? cell = inspecting ? _inspectedCell : null;
                // The inspection hint is "Name · fact · fact …": the name heads the panel, each fact gets a line.
                string[] parts = inspecting ? GetInspectionHint().Split(" · ") : new[] { UiText.Happiness };
                string title = parts[0];
                if (_infoTitle.text != title || _infoCell != cell)
                    _infoBody.rectTransform.anchoredPosition = Vector2.zero;
                _infoCell = cell;
                _infoTitle.text = title;

                CityCell inspected = inspecting ? _city.GetCell(cell.Value.x, cell.Value.y) : default;
                (string icon, Color accent, string kind) = inspecting ? DescribeCell(inspected) : (UiIcon.Smile, UiTheme.MapTeal, "");
                _infoGlyph.text = icon;
                _infoGlyph.color = accent;
                _infoBadge.color = new Color(accent.r, accent.g, accent.b, 0.16f);
                _infoSubtitle.text = kind;

                var body = new System.Text.StringBuilder();
                for (int i = 1; i < parts.Length; i++)
                    body.Append(FactLine(parts[i])).Append('\n');
                if (!inspecting || showLegend)
                {
                    if (body.Length > 0) body.Append('\n');
                    body.Append(UiText.OverlayLegend);
                }
                _infoBody.text = body.ToString().TrimEnd('\n');
            }

            if (show == _infoOpen) return;
            _infoOpen = show;
            if (_infoSlide != null) StopCoroutine(_infoSlide);
            if (show) _infoPanel.gameObject.SetActive(true);
            _infoSlide = StartCoroutine(SlideInfoView(show));
        }

        private static (string Icon, Color Accent, string Kind) DescribeCell(CityCell cell)
        {
            string icon = cell.Model != BuildingModel.Default
                ? OptionIcon(new BuildOption("", BuildTool.CivicService, cell.Model))
                : cell.Type switch
                {
                    CellType.Road => UiIcon.Road,
                    CellType.Residential => UiIcon.Home,
                    CellType.Commercial => UiIcon.Store,
                    CellType.Industrial => UiIcon.Factory,
                    CellType.PowerPlant => UiIcon.Bolt,
                    _ => UiIcon.Info
                };
            return cell.Type switch
            {
                CellType.Road => (icon, UiTheme.RoadGray, UiText.Road),
                CellType.Residential => (icon, UiTheme.Residential, UiText.Residential),
                CellType.Commercial => (icon, UiTheme.Commercial, UiText.Commercial),
                CellType.Industrial => (icon, UiTheme.Industrial, UiText.Industrial),
                CellType.School => (icon, UiTheme.Education, UiText.Education),
                CellType.CivicService => (icon, UiTheme.Services, UiText.Services),
                CellType.PowerPlant => (icon, UiTheme.Utilities, UiText.Utilities),
                _ => (icon, UiTheme.TextSoft, "")
            };
        }

        // Problems read red, healthy states green, everything else stays neutral.
        private static string FactLine(string fact)
        {
            if (fact.StartsWith("Thiếu") || fact.StartsWith("Cần") || fact.StartsWith("Mất") ||
                fact.StartsWith("Kẹt") || fact.StartsWith("Chưa"))
                return $"<color=#ff9a8a>• {fact}</color>";
            if (fact.StartsWith("Đang hoạt động") || fact.StartsWith("Đã phát triển"))
                return $"<color=#7fe0a0>• {fact}</color>";
            return "• " + fact;
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
