using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace XCity
{
    public partial class XCityBootstrap
    {
        private const string LastSaveSlotKey = "xcity.last-save-slot";
        private const int CityNameLimit = 36;

        private sealed class SaveSession
        {
            public CitySave Save;
            public CitySimulation City;
            public int Slot;
            public string Name;
        }

        private static SaveSession _nextSaveSession;

        private int _activeSaveSlot;
        private string _activeCityName;
        private bool _saveMigrationFailed;
        private int _menuBackgroundSlot;
        private string _menuBackgroundName;
        private GameObject _menuLogo;
        private GameObject _mainMenu;
        private CanvasGroup _menuControls;
        private GameObject _menuActions;
        private GameObject _slotScreen;
        private GameObject _menuDialog;
        private Text _menuTitle;
        private Text _menuNotice;
        private Button _continueButton;
        private bool _creatingCity;
        private readonly SlotCard[] _slotCards = new SlotCard[CitySave.SlotCount];

        private sealed class SlotCard
        {
            public Text Name;
            public Text Summary;
            public Button Choose;
            public Text ChooseLabel;
            public Button Delete;
        }

        private bool MainMenuOpen => _mainMenu != null && _mainMenu.activeSelf;

        private CitySimulation OpenStartupCity()
        {
            if (_previewOutput != null) return XCityPreviewCity.Create();
            SaveSession session = _nextSaveSession;
            _nextSaveSession = null;
            if (session != null)
            {
                _save = session.Save;
                _activeSaveSlot = session.Slot;
                _activeCityName = session.Name;
                return session.City;
            }

            _save = new CitySave(Application.persistentDataPath, JsonUtility.ToJson, JsonUtility.FromJson,
                tick => Mathf.FloorToInt(DayNightSystem.StartTimeOfDay +
                    tick * TickInterval / DayNightSystem.CycleDurationSeconds) + 1,
                message => Debug.LogWarning(message));
            _saveMigrationFailed = !_save.MigrateLegacy(UiText.DefaultCityName(1));
            // The menu shows the last played city behind it, read-only (no slot is active, nothing saves).
            int slot = FindContinueSlot();
            if (slot != 0 && _save.TryLoad(slot, out var city, out var summary))
            {
                _menuBackgroundSlot = slot;
                _menuBackgroundName = SavedSlotName(slot, summary);
                return city;
            }
            return XCityPreviewCity.Create();
        }

        private void ShowMainMenu(bool intro = false)
        {
            if (_mainMenu == null) BuildMainMenu();
            FrameMenuCamera();
            CloseMenuDialog();
            _mainMenu.transform.SetAsLastSibling();
            _mainMenu.SetActive(true);
            _menuControls.interactable = true;
            ShowMenuHome();
            if (_saveMigrationFailed) _menuNotice.text = UiText.MigrationFailed;
            if (intro) PlayIntro();
        }

        private bool ReturnToMainMenu()
        {
            if (!SaveCity(force: true))
            {
                if (SettingsOpen)
                {
                    _settingsPanel.SetActive(false);
                    ShowToast(UiText.SaveFailed, UiTheme.Danger, UiIcon.Warning);
                }
                return false;
            }

            CancelGesture();
            StopCameraInertia();
            CloseBuildPicker();
            if (_settingsPanel != null) _settingsPanel.SetActive(false);
            ShowMainMenu();
            return true;
        }

        private void OpenNewCityFromBankruptcy()
        {
            if (ReturnToMainMenu()) ShowSlots(creating: true);
        }

        private void BuildMainMenu()
        {
            // Light scrim only: the circling city behind the menu stays visible.
            _mainMenu = CreatePanel(_safeAreaRoot, "MainMenu", Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.18f));
            _menuControls = _mainMenu.AddComponent<CanvasGroup>();
            GameObject window = CreatePanel(_mainMenu.transform, "MenuWindow",
                new Vector2(0.08f, 0.10f), new Vector2(0.92f, 0.90f),
                Vector2.zero, Vector2.zero, new Color(UiTheme.Panel.r, UiTheme.Panel.g, UiTheme.Panel.b, 0.88f));
            UiTheme.Float(window.GetComponent<Image>(), 22f);
            _menuWindow = (RectTransform)window.transform;
            _menuWindowGroup = window.AddComponent<CanvasGroup>();
            AddMenuColumn(window.transform, 20, 10);
            _menuLogo = CreateTitleArtwork(window.transform, 156f, 64f, 40);
            _menuTitle = AddMenuLabel(window.transform, UiText.GameTitle, UiTheme.TitleSize, 34f, UiTheme.Text);
            _menuNotice = AddMenuLabel(window.transform, "", UiTheme.SmallSize, 28f, UiTheme.Warning);

            _menuActions = AddMenuGroup(window.transform, "MainActions", horizontal: false);
            var actions = AddMenuColumn(_menuActions.transform, 0, 10);
            actions.childAlignment = TextAnchor.MiddleCenter;
            Flexible(_menuActions);
            _continueButton = AddMenuButton(_menuActions.transform, UiText.ContinueCity, UiIcon.Play,
                ContinueSavedCity, UiTheme.Active);
            AddMenuButton(_menuActions.transform, UiText.NewCity, UiIcon.Construction, () => ShowSlots(true), UiTheme.Button);
            AddMenuButton(_menuActions.transform, UiText.LoadCity, UiIcon.Home, () => ShowSlots(false), UiTheme.Button);
            AddMenuButton(_menuActions.transform, UiText.Settings, UiIcon.Settings, ToggleSettings, UiTheme.Button);

            _slotScreen = AddMenuGroup(window.transform, "Slots", horizontal: false);
            AddMenuColumn(_slotScreen.transform, 0, 10);
            Flexible(_slotScreen);
            GameObject cards = AddMenuGroup(_slotScreen.transform, "SlotCards", horizontal: true);
            Flexible(cards);
            for (int slot = 1; slot <= CitySave.SlotCount; slot++)
                BuildSlotCard(cards.transform, slot);
            AddMenuButton(_slotScreen.transform, UiText.Back, UiIcon.Close, ShowMenuHome, UiTheme.Button);
        }

        private void BuildSlotCard(Transform row, int slot)
        {
            GameObject panel = AddMenuGroup(row, "SlotCard", horizontal: false);
            Image background = panel.AddComponent<Image>();
            background.color = UiTheme.Inset;
            UiTheme.Round(background, 14f);
            AddMenuColumn(panel.transform, 10, 6);
            AddMenuLabel(panel.transform, UiText.SlotLabel(slot), UiTheme.SmallSize, 24f, UiTheme.TextMuted);
            Text name = AddMenuLabel(panel.transform, UiText.DefaultCityName(slot), UiTheme.BodySize, 36f, UiTheme.Text);
            Text summary = AddMenuLabel(panel.transform, "", UiTheme.SmallSize, 84f, UiTheme.TextSoft);
            Flexible(summary.gameObject);
            GameObject buttons = AddMenuGroup(panel.transform, "SlotActions", horizontal: true);
            buttons.AddComponent<LayoutElement>().minHeight = UiTheme.MinTouch;
            Button choose = AddMenuButton(buttons.transform, UiText.LoadCity, UiIcon.Play,
                () => ChooseSaveSlot(slot), UiTheme.Active);
            Button delete = AddMenuButton(buttons.transform, UiText.DeleteCity, UiIcon.Delete,
                () => ConfirmDeleteSlot(slot), UiTheme.Danger);
            _slotCards[slot - 1] = new SlotCard
            {
                Name = name,
                Summary = summary,
                Choose = choose,
                ChooseLabel = choose.GetComponentInChildren<Text>(),
                Delete = delete
            };
        }

        private void ShowMenuHome()
        {
            // Home is a narrow column on the left so the city shows; the slot screen uses the wide window.
            _menuWindow.anchorMin = new Vector2(0.03f, 0.06f);
            _menuWindow.anchorMax = new Vector2(0.37f, 0.94f);
            _menuLogo.SetActive(true);
            _menuTitle.gameObject.SetActive(false);
            _menuTitle.text = UiText.GameTitle;
            _menuNotice.text = "";
            _continueButton.gameObject.SetActive(FindContinueSlot() != 0);
            _menuActions.SetActive(true);
            _slotScreen.SetActive(false);
        }

        private void ShowSlots(bool creating)
        {
            _creatingCity = creating;
            _menuWindow.anchorMin = new Vector2(0.08f, 0.10f);
            _menuWindow.anchorMax = new Vector2(0.92f, 0.90f);
            _menuLogo.SetActive(false);
            _menuTitle.gameObject.SetActive(true);
            _menuTitle.text = creating ? UiText.NewCity : UiText.LoadCity;
            _menuNotice.text = "";
            _menuActions.SetActive(false);
            _slotScreen.SetActive(true);
            RefreshSlotCards();
        }

        private void RefreshSlotCards()
        {
            for (int slot = 1; slot <= CitySave.SlotCount; slot++)
            {
                SlotCard card = _slotCards[slot - 1];
                CitySaveSummary summary = _save.ReadSummary(slot);
                bool occupied = _save.HasSave(slot);
                card.Name.text = SavedSlotName(slot, summary);
                card.Summary.text = summary == null || summary.savedUtcTicks <= 0 ||
                    summary.savedUtcTicks > DateTime.MaxValue.Ticks
                    ? occupied ? UiText.SummaryUnavailable : UiText.EmptySlot
                    : UiText.CitySummary(summary.population, summary.money, summary.day,
                        new DateTime(summary.savedUtcTicks, DateTimeKind.Utc).ToLocalTime());
                card.Delete.gameObject.SetActive(occupied && !_creatingCity);
                card.Choose.interactable = _creatingCity || occupied;
                card.Choose.image.color = card.Choose.interactable ? UiTheme.Active : UiTheme.ButtonLocked;
                card.ChooseLabel.text = !_creatingCity ? UiText.LoadCity : occupied ? UiText.OverwriteCity : UiText.CreateCity;
            }
        }

        private int FindContinueSlot()
        {
            int lastSlot = PlayerPrefs.GetInt(LastSaveSlotKey, 0);
            if (lastSlot >= 1 && lastSlot <= CitySave.SlotCount && _save.HasSave(lastSlot)) return lastSlot;
            int newestSlot = 0;
            long newestTime = -1;
            for (int slot = 1; slot <= CitySave.SlotCount; slot++)
            {
                CitySaveSummary summary = _save.ReadSummary(slot);
                if (summary != null && summary.savedUtcTicks > newestTime)
                {
                    newestSlot = slot;
                    newestTime = summary.savedUtcTicks;
                }
            }
            return newestSlot;
        }

        private static string SavedSlotName(int slot, CitySaveSummary summary) =>
            string.IsNullOrWhiteSpace(summary?.name) ? UiText.DefaultCityName(slot) : summary.name;

        private void ContinueSavedCity()
        {
            int slot = FindContinueSlot();
            if (slot != 0) LoadSavedCity(slot);
        }

        private void ChooseSaveSlot(int slot)
        {
            if (!_creatingCity)
            {
                LoadSavedCity(slot);
                return;
            }

            bool occupied = _save.HasSave(slot);
            string title = occupied
                ? UiText.ConfirmOverwriteCity(SavedSlotName(slot, _save.ReadSummary(slot))) : UiText.CityName;
            Transform dialog = OpenMenuDialog(title, large: true);
            InputField name = AddCityNameField(dialog, slot);
            AddMapPicker(dialog);
            AddDialogActions(dialog, occupied ? UiText.OverwriteCity : UiText.CreateCity,
                occupied ? UiTheme.Danger : UiTheme.Active, () => StartNewCity(slot, name.text));
        }

        private void ConfirmDeleteSlot(int slot)
        {
            Transform dialog = OpenMenuDialog(UiText.ConfirmDeleteCity(SavedSlotName(slot, _save.ReadSummary(slot))));
            AddDialogActions(dialog, UiText.DeleteCity, UiTheme.Danger, () =>
            {
                CloseMenuDialog();
                if (!_save.DeleteConfirmedSlot(slot))
                {
                    _menuNotice.text = UiText.DeleteFailed;
                }
                else
                {
                    if (_activeSaveSlot == slot)
                    {
                        _activeSaveSlot = 0;
                        _cityDirty = false;
                    }
                    if (_menuBackgroundSlot == slot) _menuBackgroundSlot = 0;
                    if (PlayerPrefs.GetInt(LastSaveSlotKey, 0) == slot)
                    {
                        PlayerPrefs.DeleteKey(LastSaveSlotKey);
                        PlayerPrefs.Save();
                    }
                }
                RefreshSlotCards();
            });
        }

        private void LoadSavedCity(int slot)
        {
            // The city behind the menu is already this slot: start playing it without reloading.
            if (_activeSaveSlot == 0 && slot == _menuBackgroundSlot)
            {
                _activeSaveSlot = slot;
                _activeCityName = _menuBackgroundName;
                PlayerPrefs.SetInt(LastSaveSlotKey, slot);
                PlayerPrefs.Save();
            }
            if (_activeSaveSlot == slot)
            {
                _nextAutosave = Time.unscaledTime + 15f;
                _mainMenu.SetActive(false);
                return;
            }
            if (!_save.TryLoad(slot, out var city, out var summary))
            {
                _menuNotice.text = UiText.LoadFailed;
                return;
            }
            EnterSavedCity(slot, SavedSlotName(slot, summary), city);
        }

        private void StartNewCity(int slot, string inputName)
        {
            CloseMenuDialog();
            string name = string.IsNullOrWhiteSpace(inputName) ? UiText.DefaultCityName(slot) : inputName.Trim();
            // Preserve a valid previous primary as the backup when overwriting a slot.
            if (_save.HasSave(slot)) _save.TryLoad(slot, out _, out _);
            // The new city lives on the chosen map; the scene reload that follows builds that terrain.
            WorldTerrain.Use(NewMapSpec);
            var city = new CitySimulation();
            if (!_save.Write(slot, name, city))
            {
                _city.ActivateTerrain();
                _menuNotice.text = UiText.CreateFailed;
                return;
            }
            EnterSavedCity(slot, name, city);
        }

        private void EnterSavedCity(int slot, string name, CitySimulation city)
        {
            PlayerPrefs.SetInt(LastSaveSlotKey, slot);
            PlayerPrefs.Save();
            _nextSaveSession = new SaveSession { Save = _save, City = city, Slot = slot, Name = name };
            _activeSaveSlot = 0;
            _cityDirty = false;
            SceneManager.sceneLoaded += RestartAfterLoad;
            gameObject.SetActive(false);
            Destroy(gameObject);
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private Transform OpenMenuDialog(string title, bool large = false)
        {
            CloseMenuDialog();
            _menuDialog = CreatePanel(_mainMenu.transform, "Confirm", Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, UiTheme.Panel);
            var panel = CreatePanel(_menuDialog.transform, "Dialog",
                large ? new Vector2(0.14f, 0.08f) : new Vector2(0.22f, 0.25f),
                large ? new Vector2(0.86f, 0.92f) : new Vector2(0.78f, 0.75f),
                Vector2.zero, Vector2.zero, UiTheme.PanelSolid);
            UiTheme.Float(panel.GetComponent<Image>(), 18f);
            AddMenuColumn(panel.transform, 18, 12);
            AddMenuLabel(panel.transform, title, UiTheme.BodySize, 60f, UiTheme.Text);
            return panel.transform;
        }

        private void CloseMenuDialog()
        {
            if (_menuDialog == null) return;
            _menuDialog.SetActive(false);
            Destroy(_menuDialog);
            _menuDialog = null;
        }

        private void AddDialogActions(Transform dialog, string confirm, Color color, Action action)
        {
            GameObject buttons = AddMenuGroup(dialog, "DialogActions", horizontal: true);
            buttons.AddComponent<LayoutElement>().minHeight = UiTheme.MinTouch;
            AddMenuButton(buttons.transform, UiText.Cancel, UiIcon.Close, CloseMenuDialog, UiTheme.Button);
            AddMenuButton(buttons.transform, confirm, UiIcon.Check, action, color);
        }

        private static InputField AddCityNameField(Transform parent, int slot)
        {
            var go = new GameObject("CityName", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var layout = go.AddComponent<LayoutElement>();
            layout.minHeight = layout.minWidth = UiTheme.MinTouch;
            var image = go.AddComponent<Image>();
            image.color = UiTheme.Inset;
            UiTheme.Round(image);
            Text label = AddMenuLabel(go.transform, "", UiTheme.BodySize, UiTheme.MinTouch, UiTheme.Text);
            RectTransform rect = label.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(12f, 0);
            rect.offsetMax = new Vector2(-12f, 0);
            label.alignment = TextAnchor.MiddleLeft;
            var input = go.AddComponent<InputField>();
            input.targetGraphic = image;
            input.textComponent = label;
            input.characterLimit = CityNameLimit;
            input.lineType = InputField.LineType.SingleLine;
            input.text = UiText.RandomCityName();
            return input;
        }

        private static Button AddMenuButton(Transform parent, string text, string icon, Action action, Color color)
        {
            Button button = CreateHudButton(parent, UiTheme.Font, text, "MenuButton");
            button.image.color = color;
            button.transition = Selectable.Transition.None;
            var layout = button.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = layout.minWidth = UiTheme.MinTouch;
            Text label = button.GetComponentInChildren<Text>();
            label.fontSize = UiTheme.SmallSize;
            label.color = UiTheme.Text;
            label.raycastTarget = false;
            label.rectTransform.offsetMin = new Vector2(24f, 0);
            label.rectTransform.offsetMax = new Vector2(-4f, 0);
            AddMenuIcon(button.transform, icon);
            button.onClick.AddListener(() => action());
            return button;
        }

        private static void AddMenuIcon(Transform parent, string icon)
        {
            Text label = AddMenuLabel(parent, icon, UiTheme.SmallSize, UiTheme.MinTouch, UiTheme.TextSoft);
            label.font = UiTheme.IconFont;
            RectTransform rect = label.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(0, 1);
            rect.offsetMin = new Vector2(4, 0);
            rect.offsetMax = new Vector2(28, 0);
        }

        private static Text AddMenuLabel(Transform parent, string value, int size, float height, Color color)
        {
            var go = new GameObject("MenuText", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().minHeight = height;
            var label = go.AddComponent<Text>();
            label.font = UiTheme.Font;
            label.fontSize = size;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = color;
            label.raycastTarget = false;
            label.supportRichText = false;
            label.text = value;
            return label;
        }

        private static GameObject AddMenuGroup(Transform parent, string name, bool horizontal)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            if (horizontal)
            {
                var layout = go.AddComponent<HorizontalLayoutGroup>();
                layout.spacing = 8f;
                layout.childControlHeight = layout.childControlWidth = true;
                layout.childForceExpandHeight = layout.childForceExpandWidth = true;
            }
            return go;
        }

        private static VerticalLayoutGroup AddMenuColumn(Transform parent, int padding, int spacing)
        {
            var layout = parent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.spacing = spacing;
            layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return layout;
        }

        private static void Flexible(GameObject go) => go.AddComponent<LayoutElement>().flexibleHeight = 1f;
    }
}
