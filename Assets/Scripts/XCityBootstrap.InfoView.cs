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
        private const float InfoActionMargin = 12f;
        private const float InfoDemolitionSeconds = 3f;
        private const float InfoFocusDistance = 40f;
        private const float InfoFocusSeconds = 0.38f;
        private static readonly Color InfoDemolitionTint = Color.Lerp(UiTheme.Button, UiTheme.Demolish, 0.42f);
        private RectTransform _infoPanel;
        private Text _infoBody;
        private Text _infoTitle;
        private Text _infoSubtitle;
        private Image _infoBadge;
        private Text _infoGlyph;
        private Coroutine _infoSlide;
        private bool _infoOpen;
        private Vector2Int? _infoCell;
        private RectTransform _infoViewport;
        private GameObject _infoActions;
        private Button _infoDemolish;
        private Text _infoDemolishLabel;
        private Text _infoDemolishIcon;
        private Coroutine _infoFocus;
        private Coroutine _infoDemolitionTimeout;
        private (Vector2Int Cell, CellType Type, BuildingModel Model, int Cost, int BridgeGroup)? _infoDemolition;
        private float _infoDemolitionUntil;

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
            var viewport = _infoViewport = scrollArea.rectTransform;
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
            SetupInfoActions();

            _infoPanel.gameObject.SetActive(false);
            RefreshInfoViewHUD();
        }

        private void SetupInfoActions()
        {
            var row = BuildRect(_infoPanel, "InfoActions", Vector2.zero, new Vector2(1, 0),
                new Vector2(InfoActionMargin, InfoActionMargin),
                new Vector2(-InfoActionMargin, InfoActionMargin + UiTheme.MinTouch));
            _infoActions = row.gameObject;
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.spacing = 8f;

            var focus = AddInfoAction(row, "FocusCell", UiIcon.Eye, UiText.FocusInspectedCell, UiTheme.Button,
                out _, out _);
            var focusSize = focus.gameObject.GetComponent<LayoutElement>();
            focusSize.preferredWidth = 96f;
            focus.onClick.AddListener(FocusInspectedCell);

            _infoDemolish = AddInfoAction(row, "DemolishCell", UiIcon.Delete, UiText.Bulldoze, InfoDemolitionTint,
                out _infoDemolishLabel, out _infoDemolishIcon);
            _infoDemolish.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;
            _infoDemolish.onClick.AddListener(DemolishInspectedCell);
        }

        private static Button AddInfoAction(Transform parent, string name, string glyph, string text, Color color,
            out Text label, out Text icon)
        {
            var button = BuildButton(parent, name, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            button.image.color = color;
            UiTheme.Round(button.image, 10f, outline: false);
            var size = button.gameObject.AddComponent<LayoutElement>();
            size.minWidth = size.minHeight = UiTheme.MinTouch;
            icon = BuildLabel(button.transform, glyph, UiTheme.SmallSize + 1, Vector2.zero, new Vector2(0, 1),
                new Vector2(4, 0), new Vector2(25, 0));
            icon.font = UiTheme.IconFont;
            icon.resizeTextForBestFit = false;
            label = BuildLabel(button.transform, text, UiTheme.TinySize, Vector2.zero, Vector2.one,
                new Vector2(26, 2), new Vector2(-4, -2));
            label.resizeTextForBestFit = false;
            return button;
        }

        private static bool IsInfoCellInBounds(Vector2Int point) =>
            point.x >= 0 && point.y >= 0 && point.x < GridSize && point.y < GridSize;

        private bool TryGetInfoActionCell(out Vector2Int point)
        {
            point = _inspectedCell.GetValueOrDefault();
            return _activeTool == BuildTool.Pan && _inspectedCell.HasValue && IsInfoCellInBounds(point) &&
                _city.GetCell(point.x, point.y).Type != CellType.Empty;
        }

        private void RefreshInfoActions()
        {
            bool show = TryGetInfoActionCell(out Vector2Int point);
            // A bridge over water is itself a demolishable object. Bare water has no action.
            bool demolish = show && _city.TryQuoteCell(point.x, point.y, CellType.Empty, BuildingModel.Default,
                out _, out _);
            if (_infoDemolition.HasValue && (!demolish || _infoDemolition.Value.Cell != point ||
                Time.unscaledTime >= _infoDemolitionUntil))
                ResetInfoDemolition();

            _infoActions.SetActive(show);
            _infoDemolish.gameObject.SetActive(demolish);
            _infoViewport.offsetMin = new Vector2(14, show ? InfoActionMargin + UiTheme.MinTouch + 8f : 12f);
        }

        private void FocusInspectedCell()
        {
            if (!TryGetInfoActionCell(out Vector2Int point)) return;
            ResetInfoDemolition();
            if (_infoFocus != null) StopCoroutine(_infoFocus);
            Vector3 start = _camTarget;
            FocusCamera(start);
            _infoFocus = StartCoroutine(FlyToInspectedCell(point, start, _camDistance));
        }

        private IEnumerator FlyToInspectedCell(Vector2Int cell, Vector3 start, float startDistance)
        {
            var target = new Vector3((cell.x + 0.5f) * CellSize, WorldTerrain.SurfaceY(cell.x, cell.y), (cell.y + 0.5f) * CellSize);
            Vector3 expectedTarget = _camTarget;
            float expectedDistance = _camDistance;
            float startTime = Time.unscaledTime;
            // Give touch input a chance to cancel the flight before applying each camera step.
            while (true)
            {
                yield return null;
                if (_activeTool != BuildTool.Pan || _inspectedCell != cell || _isDragging ||
                    _camTarget != expectedTarget || !Mathf.Approximately(_camDistance, expectedDistance))
                    break;

                float progress = Mathf.Clamp01((Time.unscaledTime - startTime) / InfoFocusSeconds);
                float eased = Mathf.SmoothStep(0f, 1f, progress);
                if (progress >= 1f)
                {
                    FocusCamera(target);
                    _camDistance = InfoFocusDistance;
                    UpdateCameraTransform();
                    break;
                }
                _camTarget = expectedTarget = Vector3.Lerp(start, target, eased);
                _camDistance = expectedDistance = Mathf.Lerp(startDistance, InfoFocusDistance, eased);
                UpdateCameraTransform();
            }
            _infoFocus = null;
        }

        private void DemolishInspectedCell()
        {
            if (!TryGetInfoActionCell(out Vector2Int point) ||
                !_city.TryQuoteCell(point.x, point.y, CellType.Empty, BuildingModel.Default,
                    out int cost, out int bridgeGroup))
            {
                ResetInfoDemolition();
                RefreshInfoActions();
                return;
            }

            CityCell cell = _city.GetCell(point.x, point.y);
            var quote = (point, cell.Type, cell.Model, cost, bridgeGroup);
            if (_infoDemolition != quote || Time.unscaledTime >= _infoDemolitionUntil)
            {
                ResetInfoDemolition();
                _infoDemolition = quote;
                _infoDemolitionUntil = Time.unscaledTime + InfoDemolitionSeconds;
                _infoDemolish.image.color = UiTheme.Danger;
                _infoDemolishIcon.enabled = false;
                _infoDemolishLabel.rectTransform.offsetMin = new Vector2(4, 2);
                _infoDemolishLabel.text = UiText.ConfirmInspectedDemolition(cost);
                _infoDemolitionTimeout = StartCoroutine(ExpireInfoDemolition());
                return;
            }

            ResetInfoDemolition();
            if (!_city.TrySetCell(point.x, point.y, CellType.Empty)) return;
            _cityDirty = true;
            _audio?.PlayBulldoze();
            CloseInfoView();
        }

        private IEnumerator ExpireInfoDemolition()
        {
            yield return new WaitForSecondsRealtime(InfoDemolitionSeconds);
            _infoDemolitionTimeout = null;
            ResetInfoDemolition();
        }

        private void ResetInfoDemolition()
        {
            if (!_infoDemolition.HasValue) return;
            _infoDemolition = null;
            if (_infoDemolitionTimeout != null)
            {
                StopCoroutine(_infoDemolitionTimeout);
                _infoDemolitionTimeout = null;
            }
            _infoDemolish.image.color = InfoDemolitionTint;
            _infoDemolishIcon.enabled = true;
            _infoDemolishLabel.rectTransform.offsetMin = new Vector2(26, 2);
            _infoDemolishLabel.text = UiText.Bulldoze;
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
            RefreshInfoActions();
            bool inspecting = _activeTool == BuildTool.Pan && _inspectedCell.HasValue &&
                IsInfoCellInBounds(_inspectedCell.Value);
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
            string icon = cell.Type != CellType.PowerPlant && cell.Model != BuildingModel.Default
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
