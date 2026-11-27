using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace XCity
{
    public partial class XCityBootstrap
    {
        private const float BuildBarTop = 112f;

        private sealed class BuildOption
        {
            public readonly string Name;
            public readonly string ShortName;
            public readonly BuildTool Tool;
            public readonly BuildingModel Model;
            public readonly CityOverlayMode Overlay;
            public readonly bool WorldMap;

            public BuildOption(string name, BuildTool tool, BuildingModel model = BuildingModel.Default,
                CityOverlayMode overlay = CityOverlayMode.None, bool worldMap = false)
            {
                Name = name;
                ShortName = UiText.ShortModel(model, name);
                Tool = tool;
                Model = model;
                Overlay = overlay;
                WorldMap = worldMap;
            }

            public int UnlockPopulation => BuildingCatalog.UnlockPopulation(Model);
        }

        private sealed class BuildCategory
        {
            public readonly string Name;
            public readonly Color Swatch;
            public readonly BuildOption[] Options;
            public readonly bool IsMap;
            public readonly List<(Button Button, Outline Outline, Text Title, Text Details, GameObject Lock, Image Swatch, string Stats, string LockedStats)> Cards =
                new List<(Button, Outline, Text, Text, GameObject, Image, string, string)>();
            public BuildOption Pick;
            public Button Button;
            public Button Tab;
            public Text PickLabel;
            public RectTransform Content;

            public BuildCategory(string name, Color swatch, bool isMap, BuildOption[] options)
            {
                Name = name;
                Swatch = swatch;
                IsMap = isMap;
                Options = options;
            }
        }

        private BuildCategory[] _buildCategories;
        private BuildCategory _openBuildCategory;
        private ScrollRect _buildPicker;
        private int _buildUnlockTier = -1;
        private BuildTool _buildBarTool;
        private BuildingModel _buildBarModel;
        private CityOverlayMode _buildBarOverlay;

        private static BuildOption ModelOption(BuildTool tool, BuildingModel model) =>
            new BuildOption(BuildingCatalog.Get(model).Name, tool, model);

        private static BuildCategory Category(string name, Color swatch, params BuildOption[] options) =>
            new BuildCategory(name, swatch, false, options);

        private void SetupBuildBar()
        {
            _buildCategories = new[]
            {
                Category(UiText.View, UiTheme.TextSoft, new BuildOption(UiText.View, BuildTool.Pan)),
                Category(UiText.Road, UiTheme.TextSoft, new BuildOption(UiText.Road, BuildTool.Road),
                    new BuildOption(UiText.Bridge, BuildTool.Bridge)),
                Category(UiText.Residential, UiTheme.Residential,
                    ModelOption(BuildTool.Residential, BuildingModel.RowHouse),
                    ModelOption(BuildTool.Residential, BuildingModel.Villa),
                    ModelOption(BuildTool.Residential, BuildingModel.Apartment)),
                Category(UiText.Commercial, UiTheme.Commercial,
                    ModelOption(BuildTool.Commercial, BuildingModel.Market),
                    ModelOption(BuildTool.Commercial, BuildingModel.HighStreet),
                    ModelOption(BuildTool.Commercial, BuildingModel.ShoppingMall),
                    ModelOption(BuildTool.Commercial, BuildingModel.Office),
                    ModelOption(BuildTool.Commercial, BuildingModel.OfficeTower)),
                Category(UiText.Industrial, UiTheme.Industrial,
                    ModelOption(BuildTool.Industrial, BuildingModel.Workshop),
                    ModelOption(BuildTool.Industrial, BuildingModel.Warehouse),
                    ModelOption(BuildTool.Industrial, BuildingModel.Factory)),
                Category(UiText.Education, UiTheme.Commercial,
                    ModelOption(BuildTool.School, BuildingModel.Preschool),
                    ModelOption(BuildTool.School, BuildingModel.PrimarySchool),
                    ModelOption(BuildTool.School, BuildingModel.HighSchool),
                    ModelOption(BuildTool.School, BuildingModel.University)),
                Category(UiText.Services, UiTheme.Active,
                    ModelOption(BuildTool.CivicService, BuildingModel.PolicePost),
                    ModelOption(BuildTool.CivicService, BuildingModel.Clinic),
                    ModelOption(BuildTool.CivicService, BuildingModel.FireStationSmall),
                    ModelOption(BuildTool.CivicService, BuildingModel.PoliceStation),
                    ModelOption(BuildTool.CivicService, BuildingModel.Hospital),
                    ModelOption(BuildTool.CivicService, BuildingModel.FireStation),
                    ModelOption(BuildTool.CivicService, BuildingModel.Harbor)),
                Category(UiText.Utilities, UiTheme.Warning, new BuildOption(UiText.PowerPlant, BuildTool.PowerPlant),
                    ModelOption(BuildTool.CivicService, BuildingModel.WaterPump),
                    ModelOption(BuildTool.CivicService, BuildingModel.SewagePlant)),
                new BuildCategory(UiText.Map, UiTheme.Commercial, true, new[]
                {
                    new BuildOption(UiText.Power, BuildTool.Pan, overlay: CityOverlayMode.Power),
                    new BuildOption(UiText.Services, BuildTool.Pan, overlay: CityOverlayMode.Services),
                    new BuildOption(UiText.Education, BuildTool.Pan, overlay: CityOverlayMode.Education),
                    new BuildOption(UiText.Environment, BuildTool.Pan, overlay: CityOverlayMode.Environment),
                    new BuildOption(UiText.Happiness, BuildTool.Pan, overlay: CityOverlayMode.Happiness),
                    new BuildOption(UiText.WorldMap, BuildTool.Pan, worldMap: true),
                    new BuildOption(UiText.OverlayOff, BuildTool.Pan)
                }),
                Category(UiText.Bulldoze, UiTheme.Danger, new BuildOption(UiText.Bulldoze, BuildTool.Bulldoze))
            };

            var bar = CreatePanel(_safeAreaRoot, "BuildBar", Vector2.zero, new Vector2(1, 0),
                Vector2.zero, new Vector2(0, BuildBarTop), UiTheme.Panel);
            var row = bar.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(8, 8, 4, 8);
            row.spacing = 4;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = true;

            var picker = CreatePanel(_safeAreaRoot, "BuildPicker", Vector2.zero, new Vector2(1, 0),
                new Vector2(0, BuildBarTop), new Vector2(0, BuildBarTop + 156), UiTheme.PanelSolid);
            _buildPicker = picker.AddComponent<ScrollRect>();
            _buildPicker.horizontal = true;
            _buildPicker.vertical = false;
            _buildPicker.movementType = ScrollRect.MovementType.Clamped;
            var viewport = BuildRect(picker.transform, "Viewport", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            viewport.gameObject.AddComponent<RectMask2D>();
            _buildPicker.viewport = viewport;

            foreach (BuildCategory category in _buildCategories)
            {
                var slot = BuildRect(bar.transform, "Category", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                var minSize = slot.gameObject.AddComponent<LayoutElement>();
                minSize.minWidth = minSize.minHeight = UiTheme.MinTouch;
                bool hasPicker = category.Options.Length > 1;
                category.Button = BuildButton(slot, "Choose", Vector2.zero, Vector2.one,
                    Vector2.zero, new Vector2(0, hasPicker ? -UiTheme.MinTouch : 0));
                BuildLabel(category.Button.transform, category.Name, UiTheme.SmallSize,
                    new Vector2(0, hasPicker ? 0.45f : 0), Vector2.one, Vector2.zero, Vector2.zero);
                if (hasPicker)
                {
                    category.PickLabel = BuildLabel(category.Button.transform, "", UiTheme.TinySize,
                        Vector2.zero, new Vector2(1, 0.45f), Vector2.zero, Vector2.zero);
                    category.Tab = BuildButton(slot, "PickerTab", new Vector2(0, 1), Vector2.one,
                        new Vector2(0, -UiTheme.MinTouch), Vector2.zero);
                    DrawPickerArrow(category.Tab.transform);
                    category.Tab.onClick.AddListener(() => ToggleBuildPicker(category));
                    CreateBuildCards(viewport, category);
                }
                category.Button.onClick.AddListener(() => UseRememberedOption(category));
            }
            picker.SetActive(false);
            RefreshBuildBar();
        }

        private void CreateBuildCards(RectTransform viewport, BuildCategory category)
        {
            category.Content = BuildRect(viewport, "Cards", Vector2.zero, new Vector2(0, 1),
                Vector2.zero, Vector2.zero);
            category.Content.pivot = new Vector2(0, 0.5f);
            var row = category.Content.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 8;
            row.padding = new RectOffset(10, 10, 8, 8);
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = true;
            category.Content.gameObject.AddComponent<ContentSizeFitter>().horizontalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            foreach (BuildOption option in category.Options)
            {
                Button card = BuildButton(category.Content, "Card", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                var size = card.gameObject.AddComponent<LayoutElement>();
                size.minWidth = size.preferredWidth = 235f;
                size.minHeight = UiTheme.MinTouch;

                var outline = card.gameObject.AddComponent<Outline>();
                outline.effectColor = UiTheme.Active;
                outline.effectDistance = new Vector2(2, 2);

                Text title = BuildLabel(card.transform, option.Name, UiTheme.SmallSize, new Vector2(0, 1), Vector2.one,
                    new Vector2(38, -36), new Vector2(-10, -4), TextAnchor.MiddleLeft);
                Image swatch = BuildDecoration(card.transform, "Swatch", new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(10, -29), new Vector2(27, -12), category.Swatch);
                GameObject lockIcon = DrawBuildLock(card.transform);
                string stats = BuildOptionStats(option, category.IsMap);
                Text details = BuildLabel(card.transform, stats, UiTheme.TinySize, Vector2.zero, Vector2.one,
                    new Vector2(10, 8), new Vector2(-10, -39), TextAnchor.UpperLeft);
                string lockedStats = option.UnlockPopulation == 0 ? stats :
                    UiText.UnlockAt(option.UnlockPopulation) + "\n" + stats;
                category.Cards.Add((card, outline, title, details, lockIcon, swatch, stats, lockedStats));
                card.onClick.AddListener(() => PickBuildOption(category, option));
            }

            category.Content.gameObject.SetActive(false);
        }

        private static string BuildOptionStats(BuildOption option, bool isMap)
        {
            if (isMap) return "";
            if (option.Tool == BuildTool.Bridge)
                return UiText.BridgePrice(CitySimulation.BridgeTileCost, CitySimulation.RoadCost) + "\n" +
                    UiText.Upkeep(CitySimulation.RoadUpkeep);
            if (option.Tool == BuildTool.Road)
                return UiText.Price(CitySimulation.RoadCost, true) + "\n" + UiText.Upkeep(CitySimulation.RoadUpkeep);
            if (option.Tool == BuildTool.PowerPlant)
                return UiText.Price(CitySimulation.PowerPlantCost, false) + "\n" + UiText.Upkeep(CitySimulation.PowerPlantUpkeep);

            var item = BuildingCatalog.Get(option.Model);
            int radius = ServiceCatalog.Get(option.Model).Radius;
            return UiText.Price(item.Cost, !BuildingCatalog.IsPublicService(item.Type)) + "\n" +
                UiText.Upkeep(BuildingCatalog.ServiceUpkeep(option.Model)) +
                (radius > 0 ? "\n" + UiText.Radius(radius) : "") +
                (item.Seats > 0 ? "\n" + UiText.Seats(item.Seats) : "");
        }

        private void ToggleBuildPicker(BuildCategory category)
        {
            CancelGesture();
            bool wasOpen = _openBuildCategory == category;
            CloseBuildPicker();
            if (wasOpen) return;
            _openBuildCategory = category;
            category.Content.gameObject.SetActive(true);
            _buildPicker.transform.SetAsLastSibling();
            _buildPicker.content = category.Content;
            _buildPicker.gameObject.SetActive(true);
            _buildPicker.StopMovement();
            category.Content.anchoredPosition = Vector2.zero;
            UpdateBuildCategory(category);
        }

        private void CloseBuildPicker()
        {
            if (_openBuildCategory == null) return;
            var category = _openBuildCategory;
            _openBuildCategory = null;
            _buildPicker.StopMovement();
            _buildPicker.gameObject.SetActive(false);
            category.Content.gameObject.SetActive(false);
            category.Tab.image.color = UiTheme.Button;
        }

        private void UseRememberedOption(BuildCategory category)
        {
            BuildOption option = category.Pick;
            if (option == null) return;
            if (category.IsMap && _overlays.Mode == option.Overlay)
            {
                CloseBuildPicker();
                SetBuildBarOverlay(CityOverlayMode.None);
                return;
            }
            PickBuildOption(category, option);
        }

        private void PickBuildOption(BuildCategory category, BuildOption option)
        {
            if (option.UnlockPopulation > _city.PeakPopulation) return;
            CloseBuildPicker();
            if (option.WorldMap)
            {
                // The world map opens a modal and does not replace the remembered overlay.
                OpenWorldMap();
                return;
            }

            if (category.IsMap)
            {
                if (option.Overlay != CityOverlayMode.None) category.Pick = option;
                SetBuildBarOverlay(option.Overlay);
                return;
            }

            category.Pick = option;
            _activeBuildingModel = option.Model;
            SelectTool(option.Tool);
        }

        private void SetBuildBarOverlay(CityOverlayMode mode)
        {
            CancelGesture();
            _overlays.SetMode(mode);
            _cityRenderer?.SetInfoView(mode == CityOverlayMode.Happiness);
            _audio?.PlayBuild();
            RefreshHUD();
        }

        private void SelectTool(BuildTool tool)
        {
            CancelGesture();
            if (tool != BuildTool.Pan) ClearFollow();
            if (tool == BuildTool.Pan) _activeBuildingModel = BuildingModel.Default;
            else if (BuildingCatalog.UnlockPopulation(_activeBuildingModel) > _city.PeakPopulation)
            {
                _activeBuildingModel = BuildingModel.Default;
                if (tool == BuildTool.School || tool == BuildTool.CivicService) tool = BuildTool.Road;
            }
            _activeTool = tool;
            CloseBuildPicker();
            _inspectedCell = null;
            _shoreWarningUntil = 0f;
            RefreshHUD();
        }

        private void RefreshBuildBar()
        {
            if (_buildCategories == null) return;
            int peak = _city.PeakPopulation;
            int tier = 0;
            foreach (BuildCategory category in _buildCategories)
                foreach (BuildOption option in category.Options)
                    if (option.UnlockPopulation <= peak)
                        tier = Mathf.Max(tier, option.UnlockPopulation);

            CityOverlayMode overlay = _overlays.Mode;
            if (tier == _buildUnlockTier && _activeTool == _buildBarTool &&
                _activeBuildingModel == _buildBarModel && overlay == _buildBarOverlay) return;

            if (_buildUnlockTier >= 0 && tier > _buildUnlockTier)
            {
                var names = new List<string>();
                foreach (BuildCategory category in _buildCategories)
                    foreach (BuildOption option in category.Options)
                        if (option.UnlockPopulation > _buildUnlockTier && option.UnlockPopulation <= tier)
                            names.Add(option.Name);
                if (names.Count > 0)
                {
                    _unlockBanner = UiText.Unlocked(string.Join(", ", names));
                    _unlockBannerUntil = Time.unscaledTime + 6f;
                }
            }
            _buildUnlockTier = tier;
            _buildBarTool = _activeTool;
            _buildBarModel = _activeBuildingModel;
            _buildBarOverlay = overlay;
            foreach (BuildCategory category in _buildCategories)
            {
                if (category.Pick == null || category.Pick.UnlockPopulation > peak)
                    foreach (BuildOption option in category.Options)
                        if (option.UnlockPopulation <= peak)
                        {
                            category.Pick = option;
                            break;
                        }
                UpdateBuildCategory(category);
            }
        }

        private void UpdateBuildCategory(BuildCategory category)
        {
            BuildOption pick = category.Pick;
            bool available = pick != null && pick.UnlockPopulation <= _city.PeakPopulation;
            bool selected = available && (category.IsMap ? _overlays.Mode == pick.Overlay :
                _activeTool == pick.Tool && _activeBuildingModel == pick.Model);
            category.Button.interactable = available;
            category.Button.image.color = selected ? UiTheme.Active : available ? UiTheme.Button : UiTheme.ButtonLocked;
            if (category.PickLabel != null) category.PickLabel.text = available ? pick.ShortName : UiText.Locked;
            if (_openBuildCategory != category) return;
            category.Tab.image.color = UiTheme.Active;
            for (int i = 0; i < category.Cards.Count; i++)
            {
                var card = category.Cards[i];
                BuildOption option = category.Options[i];
                bool unlocked = option.UnlockPopulation <= _city.PeakPopulation;
                card.Button.interactable = unlocked;
                card.Button.image.color = unlocked ? UiTheme.Button : UiTheme.ButtonLocked;
                card.Title.color = unlocked ? UiTheme.Text : UiTheme.TextMuted;
                bool chosen = pick == option;
                if (category.IsMap && !option.WorldMap && option.Overlay == CityOverlayMode.None)
                    chosen = _overlays.Mode == CityOverlayMode.None;
                else if (category.IsMap)
                    chosen = _overlays.Mode != CityOverlayMode.None && chosen;
                card.Outline.enabled = chosen && unlocked;
                card.Lock.SetActive(!unlocked);
                card.Swatch.enabled = unlocked;
                card.Details.color = unlocked ? UiTheme.TextSoft : UiTheme.TextMuted;
                card.Details.text = unlocked ? card.Stats : card.LockedStats;
            }
        }

        private static void DrawPickerArrow(Transform parent)
        {
            var left = BuildDecoration(parent, "ArrowLeft", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-10, -1), new Vector2(1, 2), UiTheme.Text);
            left.rectTransform.localRotation = Quaternion.Euler(0, 0, 42);
            var right = BuildDecoration(parent, "ArrowRight", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-1, -1), new Vector2(10, 2), UiTheme.Text);
            right.rectTransform.localRotation = Quaternion.Euler(0, 0, -42);
        }

        private static GameObject DrawBuildLock(Transform parent)
        {
            var root = BuildRect(parent, "Lock", new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(10, -30), new Vector2(28, -10));
            BuildDecoration(root, "Shackle", Vector2.zero, Vector2.zero, new Vector2(4, 6), new Vector2(14, 19), UiTheme.TextMuted);
            BuildDecoration(root, "Hole", Vector2.zero, Vector2.zero, new Vector2(6, 7), new Vector2(12, 16), UiTheme.Inset);
            BuildDecoration(root, "Body", Vector2.zero, Vector2.zero, new Vector2(1, 1), new Vector2(17, 12), UiTheme.TextMuted);
            return root.gameObject;
        }

        private static RectTransform BuildRect(Transform parent, string name, Vector2 min, Vector2 max,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return rect;
        }

        private static Image BuildDecoration(Transform parent, string name, Vector2 min, Vector2 max,
            Vector2 offsetMin, Vector2 offsetMax, Color color)
        {
            Image image = BuildRect(parent, name, min, max, offsetMin, offsetMax).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Button BuildButton(Transform parent, string name, Vector2 min, Vector2 max,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            GameObject go = BuildRect(parent, name, min, max, offsetMin, offsetMax).gameObject;
            Image background = go.AddComponent<Image>();
            background.color = UiTheme.Button;
            Button button = go.AddComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.None;
            return button;
        }

        private static Text BuildLabel(Transform parent, string value, int fontSize, Vector2 min, Vector2 max,
            Vector2 offsetMin, Vector2 offsetMax, TextAnchor align = TextAnchor.MiddleCenter)
        {
            Text text = BuildRect(parent, "Label", min, max, offsetMin, offsetMax).gameObject.AddComponent<Text>();
            text.font = UiTheme.Font;
            text.fontSize = fontSize;
            text.color = UiTheme.Text;
            text.alignment = align;
            text.text = value;
            text.raycastTarget = false;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = UiTheme.TinySize;
            text.resizeTextMaxSize = fontSize;
            return text;
        }
    }
}
