using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace XCity
{
    // Top bar as three floating panels (stats, clock and speed, icon buttons) and toasts that appear
    // in the centre under the hint, separate from the tool hint itself.
    public partial class XCityBootstrap
    {
        private const int MaxToasts = 3;
        private const float ToastSeconds = 5f;
        private const float FocusToastSeconds = 8f;
        private const float ToastFadeSeconds = 0.4f;
        private const float TopBarHeight = 56f;

        private sealed class Toast
        {
            public GameObject Root;
            public CanvasGroup Group;
            public float Until;
        }

        private Text _moneyText;
        private Text _peopleText;
        private Text _clockText;
        private readonly Image[] _speedButtons = new Image[3];
        private static readonly float[] SpeedSteps = { 0f, 1f, 3f };
        private float _clockRefreshTimer;
        private string _shownClock;
        private int _lastTicksInDebt;
        private Transform _topButtons;

        private RectTransform _toastRoot;
        private readonly List<Toast> _toasts = new List<Toast>(MaxToasts);

        // Game time in days since the city was founded; the integer part is the day, the rest the clock.
        private float GameDays => DayNightSystem.StartTimeOfDay +
            (_city.TickCount * TickInterval + _simTimer) / DayNightSystem.CycleDurationSeconds;

        // Builds the top bar inside `bar` (a transparent strip across the top of the safe area).
        private void SetupTopBar(RectTransform bar, Font font)
        {
            var row = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 10;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = true;

            // Stats: money | people | demand.
            Transform stats = CreateTopPanel(bar, "Stats", 14);
            _moneyText = CreateIconStat(stats, UiIcon.Wallet, UiTheme.Gold, 128f, font);
            // Tapping the money opens the budget.
            GameObject moneyStat = _moneyText.transform.parent.gameObject;
            moneyStat.AddComponent<Image>().color = Color.clear;
            moneyStat.AddComponent<Button>().onClick.AddListener(ToggleBudget);
            CreateDivider(stats);
            _peopleText = CreateIconStat(stats, UiIcon.People, UiTheme.Sky, 112f, font);
            CreateDivider(stats);
            var rci = new GameObject("RCIBars", typeof(RectTransform));
            rci.transform.SetParent(stats, false);
            rci.AddComponent<LayoutElement>().preferredWidth = 46f;
            var rciRow = rci.AddComponent<HorizontalLayoutGroup>();
            rciRow.spacing = 5;
            rciRow.padding = new RectOffset(0, 0, 6, 6);
            rciRow.childForceExpandWidth = rciRow.childForceExpandHeight = true;
            _rciResFill = CreateDemandBar(rci.transform, font, "R", UiTheme.Residential);
            _rciComFill = CreateDemandBar(rci.transform, font, "C", UiTheme.Commercial);
            _rciIndFill = CreateDemandBar(rci.transform, font, "I", UiTheme.Industrial);

            // Clock and speed.
            Transform clock = CreateTopPanel(bar, "Clock", 12);
            _clockText = CreateIconStat(clock, UiIcon.Clock, UiTheme.TextSoft, 74f, font);
            var seg = CreatePanel(clock, "Speed", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, UiTheme.Button);
            UiTheme.Round(seg.GetComponent<Image>(), 11f, outline: false);
            var segRow = seg.AddComponent<HorizontalLayoutGroup>();
            segRow.padding = new RectOffset(3, 3, 6, 6);
            segRow.spacing = 2;
            segRow.childControlWidth = segRow.childControlHeight = true;
            segRow.childForceExpandWidth = false;
            segRow.childForceExpandHeight = true;
            string[] labels = { UiIcon.Pause, UiText.Speed1, UiText.Speed3 };
            for (int i = 0; i < SpeedSteps.Length; i++)
            {
                float speed = SpeedSteps[i];
                Button button = i == 0
                    ? CreateIconButton(seg.transform, labels[i], "Speed_" + i, 20)
                    : CreateHudButton(seg.transform, font, labels[i], "Speed_" + i);
                button.GetComponentInChildren<Text>().fontSize = i == 0 ? 20 : UiTheme.SmallSize;
                button.gameObject.AddComponent<LayoutElement>().preferredWidth = 44f;
                button.image.color = Color.clear;
                button.onClick.AddListener(() => SetSimSpeed(speed));
                _speedButtons[i] = button.image;
            }

            var spacer = new GameObject("Spacer", typeof(RectTransform));
            spacer.transform.SetParent(bar, false);
            spacer.AddComponent<LayoutElement>().flexibleWidth = 1f;

            // Icon buttons; SetupUXToolbar adds lighting, follow, audio and settings after a divider.
            _topButtons = CreateTopPanel(bar, "Buttons", 6);
            _loanButton = CreateIconButton(_topButtons, UiIcon.Bank, "TakeLoan");
            _loanButton.onClick.AddListener(() =>
            {
                CancelGesture();
                _cityDirty |= _city.TryTakeLoan();
                RefreshHUD();
            });
            _undoButton = CreateIconButton(_topButtons, UiIcon.Undo, "UndoBuild");
            _undoButton.onClick.AddListener(UndoBuild);
            CreateDivider(_topButtons);

            _toastRoot = new GameObject("Toasts", typeof(RectTransform)).GetComponent<RectTransform>();
            _toastRoot.SetParent(_safeAreaRoot, false);
            // Top centre under the hint pill: the right edge belongs to the building info panel.
            _toastRoot.anchorMin = _toastRoot.anchorMax = _toastRoot.pivot = new Vector2(0.5f, 1f);
            _toastRoot.anchoredPosition = new Vector2(0f, -116f);
            _toastRoot.sizeDelta = new Vector2(440f, 0f);
            var column = _toastRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            column.spacing = 6;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;

            _lastTicksInDebt = _city.TicksInDebt;
        }

        private static Transform CreateTopPanel(Transform bar, string name, int padding)
        {
            var panel = CreatePanel(bar, name, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, UiTheme.Panel);
            UiTheme.Float(panel.GetComponent<Image>());
            var row = panel.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(padding, padding, 6, 6);
            row.spacing = 10;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = true;
            return panel.transform;
        }

        private static void CreateDivider(Transform parent)
        {
            var line = CreatePanel(parent, "Divider", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, UiTheme.Divider);
            var size = line.AddComponent<LayoutElement>();
            size.minWidth = size.preferredWidth = 1f;
            line.GetComponent<Image>().raycastTarget = false;
        }

        // An icon followed by a two-line rich-text stat; returns the stat text.
        private static Text CreateIconStat(Transform parent, string icon, Color iconColor, float textWidth, Font font)
        {
            var group = new GameObject("Stat", typeof(RectTransform));
            group.transform.SetParent(parent, false);
            var row = group.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 8;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = true;

            Text glyph = CreateLabel(group.transform, UiTheme.IconFont, 24, iconColor, TextAnchor.MiddleCenter);
            glyph.text = icon;
            glyph.gameObject.AddComponent<LayoutElement>().preferredWidth = 26f;

            Text text = CreateLabel(group.transform, font, UiTheme.TinySize, UiTheme.TextMuted, TextAnchor.MiddleLeft);
            text.lineSpacing = 0.9f;
            text.gameObject.AddComponent<LayoutElement>().preferredWidth = textWidth;
            return text;
        }

        private static Text CreateLabel(Transform parent, Font font, int size, Color color, TextAnchor align)
        {
            var obj = new GameObject("Label", typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            var text = obj.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = align;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            return text;
        }

        // A 44 pt rounded button showing one icon glyph (its only Text, so callers can swap the icon).
        private static Button CreateIconButton(Transform parent, string icon, string name, int size = 24)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = UiTheme.Button;
            UiTheme.Round(image, 12f, outline: false);
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            var layout = go.AddComponent<LayoutElement>();
            layout.minWidth = layout.preferredWidth = UiTheme.MinTouch;

            Text glyph = CreateLabel(go.transform, UiTheme.IconFont, size, UiTheme.TextSoft, TextAnchor.MiddleCenter);
            var rect = glyph.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            glyph.text = icon;
            return button;
        }

        private void SetSimSpeed(float speed)
        {
            _simSpeed = speed;
            _audio?.PlayBuild();
            RefreshHUD();
        }

        private void RefreshTopBar()
        {
            if (_moneyText == null) return;

            string net = _city.LastNetIncome.ToString("+#,##0;-#,##0;0");
            string netColor = _city.LastNetIncome < 0 ? "#ff7a7a" : "#5fe09a";
            string debt = _city.LoanBalance > 0 ? $" · {UiText.Debt}${_city.LoanBalance:N0}" : "";
            string money = _city.Money < 0 ? $"<color=#ff7a7a>${_city.Money:N0}</color>" : $"${_city.Money:N0}";
            _moneyText.text = $"<size=19><b><color=#ffffff>{money}</color></b></size>\n<color={netColor}>{net}</color>{UiText.PerTick}{debt}";

            string happiness = _city.Population > 0 ? $"{UiText.HappinessLabel}{_city.AverageHappiness}%" : "";
            _peopleText.text = $"<size=19><b><color=#ffffff>{_city.Population:N0}</color></b></size>\n{happiness}";

            for (int i = 0; i < SpeedSteps.Length; i++)
            {
                bool active = Mathf.Approximately(_simSpeed, SpeedSteps[i]);
                _speedButtons[i].color = !active ? Color.clear : i == 0 ? UiTheme.Danger : UiTheme.Active;
            }

            if (_lastTicksInDebt == 0 && _city.TicksInDebt > 0 && !_city.IsBankrupt)
                ShowToast(UiText.DebtWarning, UiTheme.Danger, UiIcon.Warning);
            _lastTicksInDebt = _city.TicksInDebt;

            _shownClock = null;
            RefreshClock();
        }

        private void RefreshClock()
        {
            float days = GameDays;
            int minutes = Mathf.FloorToInt((days - Mathf.Floor(days)) * 24f * 60f);
            string clock = $"<size=16><b><color=#ffffff>{UiText.Day}{Mathf.FloorToInt(days) + 1}</color></b></size>\n{minutes / 60:00}:{minutes % 60 / 10 * 10:00}";
            if (clock == _shownClock) return;
            _shownClock = clock;
            _clockText.text = clock;
        }

        private void UpdateTopBar()
        {
            if (_clockText == null) return;
            _clockRefreshTimer += Time.unscaledDeltaTime;
            if (_clockRefreshTimer >= 0.25f)
            {
                _clockRefreshTimer = 0f;
                RefreshClock();
            }

            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                float left = _toasts[i].Until - Time.unscaledTime;
                if (left <= 0f) DismissToast(_toasts[i]);
                else _toasts[i].Group.alpha = Mathf.Clamp01(left / ToastFadeSeconds);
            }
        }

        // A toast with a focus point flies the camera there when tapped.
        private void ShowToast(string message, Color accent, string icon, Vector3? focus = null)
        {
            if (_toastRoot == null || _previewOutput != null) return;
            if (_toasts.Count >= MaxToasts) DismissToast(_toasts[0]);

            var root = CreatePanel(_toastRoot, "Toast", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                Color.Lerp(UiTheme.PanelSolid, accent, 0.22f));
            UiTheme.Float(root.GetComponent<Image>());
            // The toast column has zero height, so the layout falls back to minHeight: set both.
            var size = root.AddComponent<LayoutElement>();
            size.minHeight = size.preferredHeight = UiTheme.MinTouch + 10f;
            var toast = new Toast
            {
                Root = root,
                Group = root.AddComponent<CanvasGroup>(),
                Until = Time.unscaledTime + (focus.HasValue ? FocusToastSeconds : ToastSeconds)
            };

            var badge = CreatePanel(root.transform, "Icon", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(10f, -17f), new Vector2(44f, 17f), new Color(accent.r, accent.g, accent.b, 0.25f));
            UiTheme.Round(badge.GetComponent<Image>(), 10f, outline: false);
            badge.GetComponent<Image>().raycastTarget = false;
            Text glyph = CreateLabel(badge.transform, UiTheme.IconFont, 22, Color.Lerp(accent, Color.white, 0.35f), TextAnchor.MiddleCenter);
            glyph.rectTransform.anchorMin = Vector2.zero;
            glyph.rectTransform.anchorMax = Vector2.one;
            glyph.rectTransform.offsetMin = glyph.rectTransform.offsetMax = Vector2.zero;
            glyph.text = icon;

            Text text = CreateLabel(root.transform, UiTheme.Font, UiTheme.SmallSize, UiTheme.Text, TextAnchor.MiddleLeft);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = UiTheme.TinySize;
            text.resizeTextMaxSize = UiTheme.SmallSize + 1;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(54f, 4f);
            text.rectTransform.offsetMax = new Vector2(focus.HasValue ? -34f : -12f, -4f);
            text.text = message;

            if (focus.HasValue)
            {
                Text chevron = CreateLabel(root.transform, UiTheme.IconFont, 22, UiTheme.TextSoft, TextAnchor.MiddleCenter);
                chevron.rectTransform.anchorMin = new Vector2(1f, 0f);
                chevron.rectTransform.anchorMax = Vector2.one;
                chevron.rectTransform.offsetMin = new Vector2(-34f, 0f);
                chevron.rectTransform.offsetMax = Vector2.zero;
                chevron.text = UiIcon.ChevronRight;
            }

            var button = root.AddComponent<Button>();
            button.targetGraphic = root.GetComponent<Image>();
            button.onClick.AddListener(() =>
            {
                if (focus.HasValue) FocusCamera(focus.Value);
                DismissToast(toast);
            });

            _toasts.Add(toast);
        }

        private void FocusCamera(Vector3 point)
        {
            CancelGesture();
            ClearFollow();
            StopCameraInertia();
            _camTarget = new Vector3(point.x, 0f, point.z);
            UpdateCameraTransform();
        }

        private void FocusActiveFire()
        {
            if (_emergencies != null && _emergencies.HasActiveFire) FocusCamera(_emergencies.FireWorldPos);
        }

        private void DismissToast(Toast toast)
        {
            if (!_toasts.Remove(toast)) return;
            Destroy(toast.Root);
        }
    }
}
