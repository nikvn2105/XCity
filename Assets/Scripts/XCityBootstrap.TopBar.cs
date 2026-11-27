using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace XCity
{
    // Top bar stats (money, people, R/C/I demand, day and clock, speed) and toasts that appear
    // under it in the centre, separate from the tool hint above the bottom bar.
    public partial class XCityBootstrap
    {
        private const int MaxToasts = 3;
        private const float ToastSeconds = 5f;
        private const float FocusToastSeconds = 8f;
        private const float ToastFadeSeconds = 0.4f;

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

        private RectTransform _toastRoot;
        private readonly List<Toast> _toasts = new List<Toast>(MaxToasts);

        // Game time in days since the city was founded; the integer part is the day, the rest the clock.
        private float GameDays => DayNightSystem.StartTimeOfDay +
            (_city.TickCount * TickInterval + _simTimer) / DayNightSystem.CycleDurationSeconds;

        private void SetupTopBarStats(Transform topBar, Font font)
        {
            var stats = new GameObject("Stats", typeof(RectTransform));
            var rect = (RectTransform)stats.transform;
            rect.SetParent(topBar, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = new Vector2(12f, 6f);
            rect.offsetMax = new Vector2(732f, -6f);
            var row = stats.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 14;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = true;

            _moneyText = CreateStatText(stats.transform, font, 170f);
            _peopleText = CreateStatText(stats.transform, font, 150f);

            var rci = CreatePanel(stats.transform, "RCIBars", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, UiTheme.Inset);
            rci.AddComponent<LayoutElement>().preferredWidth = 84f;
            var rciRow = rci.AddComponent<HorizontalLayoutGroup>();
            rciRow.spacing = 4;
            rciRow.padding = new RectOffset(5, 5, 4, 4);
            rciRow.childForceExpandWidth = true;
            rciRow.childForceExpandHeight = true;
            _rciResFill = CreateDemandBar(rci.transform, font, "R", UiTheme.Residential);
            _rciComFill = CreateDemandBar(rci.transform, font, "C", UiTheme.Commercial);
            _rciIndFill = CreateDemandBar(rci.transform, font, "I", UiTheme.Industrial);

            _clockText = CreateStatText(stats.transform, font, 92f);

            string[] labels = { UiText.Pause, UiText.Speed1, UiText.Speed3 };
            for (int i = 0; i < SpeedSteps.Length; i++)
            {
                float speed = SpeedSteps[i];
                Button button = CreateCompactButton(stats.transform, font, labels[i], "Speed_" + i);
                button.gameObject.AddComponent<LayoutElement>().preferredWidth = 48f;
                button.onClick.AddListener(() => SetSimSpeed(speed));
                _speedButtons[i] = button.image;
            }

            _toastRoot = new GameObject("Toasts", typeof(RectTransform)).GetComponent<RectTransform>();
            _toastRoot.SetParent(_safeAreaRoot, false);
            // Top centre under the hint pill: the right edge belongs to the building info panel.
            _toastRoot.anchorMin = _toastRoot.anchorMax = _toastRoot.pivot = new Vector2(0.5f, 1f);
            _toastRoot.anchoredPosition = new Vector2(0f, -172f);
            _toastRoot.sizeDelta = new Vector2(460f, 0f);
            var column = _toastRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            column.spacing = 6;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;

            _lastTicksInDebt = _city.TicksInDebt;
        }

        private static Text CreateStatText(Transform parent, Font font, float width)
        {
            var obj = new GameObject("Stat", typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            obj.AddComponent<LayoutElement>().preferredWidth = width;
            var text = obj.AddComponent<Text>();
            text.font = font;
            text.fontSize = UiTheme.SmallSize;
            text.color = UiTheme.Text;
            text.alignment = TextAnchor.MiddleLeft;
            text.lineSpacing = 0.9f;
            text.raycastTarget = false;
            return text;
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
            string netColor = _city.LastNetIncome < 0 ? "#ff7a7a" : "#7fe0a0";
            string debt = _city.LoanBalance > 0 ? $" · {UiText.Debt}${_city.LoanBalance:N0}" : "";
            string money = _city.Money < 0 ? $"<color=#ff7a7a>${_city.Money:N0}</color>" : $"${_city.Money:N0}";
            _moneyText.text = $"<size={UiTheme.BodySize}><b>{money}</b></size>\n<color={netColor}>{net}</color>{UiText.PerTick}{debt}";

            string happiness = _city.Population > 0 ? $"{UiText.HappinessLabel}{_city.AverageHappiness}%" : "";
            _peopleText.text = $"<size={UiTheme.BodySize}><b>{UiText.Population}{_city.Population:N0}</b></size>\n{happiness}";

            for (int i = 0; i < SpeedSteps.Length; i++)
            {
                bool active = Mathf.Approximately(_simSpeed, SpeedSteps[i]);
                _speedButtons[i].color = !active ? UiTheme.Button : i == 0 ? UiTheme.Danger : UiTheme.Active;
            }

            if (_lastTicksInDebt == 0 && _city.TicksInDebt > 0 && !_city.IsBankrupt)
                ShowToast(UiText.DebtWarning, UiTheme.Danger);
            _lastTicksInDebt = _city.TicksInDebt;

            _shownClock = null;
            RefreshClock();
        }

        private void RefreshClock()
        {
            float days = GameDays;
            int minutes = Mathf.FloorToInt((days - Mathf.Floor(days)) * 24f * 60f);
            string clock = $"<size={UiTheme.BodySize}><b>{UiText.Day}{Mathf.FloorToInt(days) + 1}</b></size>\n{minutes / 60:00}:{minutes % 60 / 10 * 10:00}";
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
        private void ShowToast(string message, Color accent, Vector3? focus = null)
        {
            if (_toastRoot == null || _previewOutput != null) return;
            if (_toasts.Count >= MaxToasts) DismissToast(_toasts[0]);

            var root = CreatePanel(_toastRoot, "Toast", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, UiTheme.PanelSolid);
            // The toast column has zero height, so the layout falls back to minHeight: set both.
            var size = root.AddComponent<LayoutElement>();
            size.minHeight = size.preferredHeight = UiTheme.MinTouch + 8f;
            var toast = new Toast
            {
                Root = root,
                Group = root.AddComponent<CanvasGroup>(),
                Until = Time.unscaledTime + (focus.HasValue ? FocusToastSeconds : ToastSeconds)
            };

            var strip = CreatePanel(root.transform, "Accent", Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(6f, 0f), accent);
            strip.GetComponent<Image>().raycastTarget = false;

            var label = new GameObject("Label", typeof(RectTransform));
            var labelRect = (RectTransform)label.transform;
            labelRect.SetParent(root.transform, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(16f, 4f);
            labelRect.offsetMax = new Vector2(-10f, -4f);
            var text = label.AddComponent<Text>();
            text.font = UiTheme.Font;
            text.text = message;
            text.color = UiTheme.Text;
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = UiTheme.TinySize;
            text.resizeTextMaxSize = UiTheme.BodySize;

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
