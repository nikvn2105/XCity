using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace XCity
{
    public partial class XCityBootstrap
    {
        private const int CrashLogVisibleLines = 50;
        // Keep the maximum uGUI glyph count within a single mesh, even for huge exception messages.
        private const int CrashLogVisibleColumns = 240;
        private GameObject _crashLogPanel;
        private Text _crashLogCount;
        private Text _crashLogBody;
        private ScrollRect _crashLogScroll;
        private Button _crashLogCopy, _crashLogClear;
        private int _shownCrashLogCount = -1;

        private void BuildCrashLogRow(Transform row)
        {
            _crashLogCount = CreateLabel(row, UiTheme.Font, UiTheme.SmallSize, UiTheme.TextSoft, TextAnchor.MiddleLeft);
            _crashLogCount.horizontalOverflow = HorizontalWrapMode.Wrap;
            var labelSize = _crashLogCount.gameObject.AddComponent<LayoutElement>();
            labelSize.minWidth = 140f;
            labelSize.flexibleWidth = 1f;

            AddCrashLogButton(row, UiText.View, "ViewCrashLog", OpenCrashLog);
            _crashLogCopy = AddCrashLogButton(row, UiText.CrashLogCopy, "CopyCrashLog", CopyCrashLog);
            _crashLogClear = AddCrashLogButton(row, UiText.DeleteCity, "ClearCrashLog", ClearCrashLog);
            RefreshCrashLogCount();
        }

        private Button AddCrashLogButton(Transform parent, string caption, string name, Action onClick)
        {
            Button button = CreateHudButton(parent, UiTheme.Font, caption, name);
            var size = button.gameObject.AddComponent<LayoutElement>();
            size.minWidth = size.minHeight = UiTheme.MinTouch;
            size.preferredWidth = 96f;
            Text label = button.GetComponentInChildren<Text>();
            label.fontSize = UiTheme.SmallSize;
            label.color = UiTheme.Text;
            label.raycastTarget = false;
            button.onClick.AddListener(() => onClick());
            return button;
        }

        private void RefreshCrashLogCount()
        {
            if (_crashLogCount == null) return;
            int count = CrashLog.Count;
            if (_shownCrashLogCount == count) return;
            _shownCrashLogCount = count;
            _crashLogCount.text = UiText.CrashLogCount(count);
            _crashLogCopy.interactable = _crashLogClear.interactable = count > 0;
        }

        private void OpenCrashLog()
        {
            if (_crashLogPanel == null) BuildCrashLogPanel();
            _crashLogPanel.transform.SetAsLastSibling();
            _crashLogPanel.SetActive(true);
            _crashLogBody.text = !CrashLog.TryRead(out string log, CrashLogVisibleLines)
                ? UiText.CrashLogReadFailed : string.IsNullOrEmpty(log) ? UiText.CrashLogEmpty : FormatCrashLogPreview(log);
            Canvas.ForceUpdateCanvases();
            _crashLogScroll.StopMovement();
            _crashLogScroll.verticalNormalizedPosition = 1f;
        }

        private static string FormatCrashLogPreview(string log)
        {
            using var reader = new StringReader(log);
            var result = new StringBuilder();
            for (string line = reader.ReadLine(); line != null; line = reader.ReadLine())
            {
                if (line.Length <= CrashLogVisibleColumns) result.AppendLine(line);
                else
                {
                    int end = CrashLogVisibleColumns;
                    if (char.IsHighSurrogate(line[end - 1])) end--;
                    result.Append(line, 0, end).AppendLine("…");
                }
            }
            return result.ToString();
        }

        private void BuildCrashLogPanel()
        {
            _crashLogPanel = CreatePanel(_settingsPanel.transform, "CrashLog", Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, UiTheme.PanelSolid);
            _crashLogPanel.AddComponent<LayoutElement>().ignoreLayout = true;
            UiTheme.Float(_crashLogPanel.GetComponent<Image>());
            Transform panel = _crashLogPanel.transform;

            Text title = CreateLabel(panel, UiTheme.Font, UiTheme.TitleSize, UiTheme.Text, TextAnchor.MiddleLeft);
            title.text = UiText.CrashLogTitle;
            Place(title.rectTransform, new Vector2(0f, 1f), Vector2.one,
                new Vector2(16f, -46f), new Vector2(-126f, -10f));

            Text subtitle = CreateLabel(panel, UiTheme.Font, UiTheme.SmallSize, UiTheme.TextSoft, TextAnchor.MiddleLeft);
            subtitle.text = UiText.CrashLogRecent;
            Place(subtitle.rectTransform, new Vector2(0f, 1f), Vector2.one,
                new Vector2(16f, -68f), new Vector2(-126f, -44f));

            Button close = AddCrashLogButton(panel, UiText.Close, "CloseCrashLog", () => _crashLogPanel.SetActive(false));
            Place((RectTransform)close.transform, Vector2.one, Vector2.one,
                new Vector2(-114f, -12f - UiTheme.MinTouch), new Vector2(-12f, -12f));

            GameObject viewport = CreatePanel(panel, "LogScroll", Vector2.zero, Vector2.one,
                new Vector2(14f, 14f), new Vector2(-14f, -74f), UiTheme.Inset);
            UiTheme.Round(viewport.GetComponent<Image>(), outline: false);
            viewport.AddComponent<RectMask2D>();
            _crashLogScroll = viewport.AddComponent<ScrollRect>();
            _crashLogScroll.viewport = (RectTransform)viewport.transform;
            _crashLogScroll.horizontal = false;
            _crashLogScroll.movementType = ScrollRect.MovementType.Clamped;

            _crashLogBody = CreateLabel(viewport.transform, UiTheme.Font, UiTheme.TinySize, UiTheme.TextSoft, TextAnchor.UpperLeft);
            _crashLogBody.supportRichText = false;
            _crashLogBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            _crashLogBody.verticalOverflow = VerticalWrapMode.Overflow;
            _crashLogBody.rectTransform.pivot = new Vector2(0.5f, 1f);
            Place(_crashLogBody.rectTransform, new Vector2(0f, 1f), Vector2.one,
                new Vector2(10f, 0f), new Vector2(-10f, 0f));
            _crashLogBody.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _crashLogScroll.content = _crashLogBody.rectTransform;

            static void Place(RectTransform rect, Vector2 min, Vector2 max, Vector2 lower, Vector2 upper)
            {
                rect.anchorMin = min;
                rect.anchorMax = max;
                rect.offsetMin = lower;
                rect.offsetMax = upper;
            }
        }

        private void CopyCrashLog()
        {
            try
            {
                if (CrashLog.TryRead(out string log) && !string.IsNullOrEmpty(log))
                {
                    GUIUtility.systemCopyBuffer = log;
                    ShowCrashLogToast(UiText.CrashLogCopied, UiTheme.Positive);
                    return;
                }
            }
            catch (Exception)
            {
                // Clipboard access can depend on the platform.
            }
            ShowCrashLogToast(UiText.CrashLogCopyFailed, UiTheme.Warning);
        }

        private void ClearCrashLog()
        {
            if (!CrashLog.TryClear()) ShowCrashLogToast(UiText.CrashLogClearFailed, UiTheme.Warning);
            RefreshCrashLogCount();
        }

        private void ShowCrashLogToast(string text, Color accent)
        {
            if (_toastRoot == null) return;
            _toastRoot.SetAsLastSibling();
            ShowToast(text, accent, UiIcon.Info);
        }
    }
}
