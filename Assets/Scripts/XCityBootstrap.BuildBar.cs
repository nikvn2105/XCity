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
            public readonly string Icon;
            public readonly List<(Button Button, Image Ring, Text Title, Text Details, Text Glyph, string Icon, string Stats, string LockedStats)> Cards =
                new List<(Button, Image, Text, Text, Text, string, string, string)>();
            public BuildOption Pick;
            public Button Button;
            public Button Tab;
            public Text PickLabel;
            public Image Underline;
            public Image Knob;
            public Text KnobGlyph;
            public RectTransform Content;

            public BuildCategory(string name, string icon, Color swatch, bool isMap, BuildOption[] options)
            {
                Name = name;
                Icon = icon;
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

        private static BuildCategory Category(string name, string icon, Color swatch, params BuildOption[] options) =>
            new BuildCategory(name, icon, swatch, false, options);

        private const float BuildSlotWidth = 84f;
        private const float BuildSlotHeight = 66f;
        private const float BuildCardWidth = 176f;
        private const float BuildCardHeight = 164f;
        private const float BuildPickerHeight = BuildCardHeight + 56f;
        private RectTransform _buildPickerRect;
        private Text _buildPickerTitle;

        private static string OptionIcon(BuildOption option)
        {
            if (option.WorldMap) return UiIcon.Map;
            switch (option.Overlay)
            {
                case CityOverlayMode.Power: return UiIcon.Bolt;
                case CityOverlayMode.Services: return UiIcon.Shield;
                case CityOverlayMode.Education: return UiIcon.School;
                case CityOverlayMode.Environment: return UiIcon.Park;
                case CityOverlayMode.Happiness: return UiIcon.Smile;
                case CityOverlayMode.LandValue: return UiIcon.Map;
                case CityOverlayMode.Garbage: return UiIcon.Recycling;
            }
            switch (option.Tool)
            {
                case BuildTool.Pan: return option.Name == UiText.View ? UiIcon.Pointer : UiIcon.Close;
                case BuildTool.Road:
                case BuildTool.Bridge: return UiIcon.Road;
                case BuildTool.PowerPlant: return UiIcon.Bolt;
                case BuildTool.Bulldoze: return UiIcon.Delete;
            }
            return option.Model switch
            {
                BuildingModel.Villa => UiIcon.Cottage,
                BuildingModel.Apartment => UiIcon.Apartment,
                BuildingModel.RowHouse => UiIcon.Home,
                BuildingModel.Office => UiIcon.Domain,
                BuildingModel.OfficeTower or BuildingModel.ShoppingMall => UiIcon.Business,
                BuildingModel.Market or BuildingModel.HighStreet => UiIcon.Store,
                BuildingModel.Workshop => UiIcon.Construction,
                BuildingModel.Warehouse => UiIcon.Warehouse,
                BuildingModel.Factory => UiIcon.Factory,
                BuildingModel.PolicePost or BuildingModel.PoliceStation => UiIcon.Police,
                BuildingModel.Clinic or BuildingModel.Hospital => UiIcon.Hospital,
                BuildingModel.FireStationSmall or BuildingModel.FireStation => UiIcon.Fire,
                BuildingModel.SmallPark or BuildingModel.Plaza or BuildingModel.LargePark => UiIcon.Park,
                BuildingModel.Harbor => UiIcon.Boat,
                BuildingModel.WaterPump => UiIcon.Water,
                BuildingModel.Landfill => UiIcon.Delete,
                BuildingModel.SewagePlant or BuildingModel.Incinerator => UiIcon.Recycling,
                _ => UiIcon.School
            };
        }

        private void SetupBuildBar()
        {
            _buildCategories = new[]
            {
                Category(UiText.View, UiIcon.Pointer, UiTheme.TextSoft, new BuildOption(UiText.View, BuildTool.Pan)),
                Category(UiText.Road, UiIcon.Road, UiTheme.RoadGray, new BuildOption(UiText.Road, BuildTool.Road),
                    new BuildOption(UiText.Bridge, BuildTool.Bridge)),
                Category(UiText.Residential, UiIcon.Home, UiTheme.Residential,
                    ModelOption(BuildTool.Residential, BuildingModel.RowHouse),
                    ModelOption(BuildTool.Residential, BuildingModel.Villa),
                    ModelOption(BuildTool.Residential, BuildingModel.Apartment)),
                Category(UiText.Commercial, UiIcon.Store, UiTheme.Commercial,
                    ModelOption(BuildTool.Commercial, BuildingModel.Market),
                    ModelOption(BuildTool.Commercial, BuildingModel.HighStreet),
                    ModelOption(BuildTool.Commercial, BuildingModel.ShoppingMall),
                    ModelOption(BuildTool.Commercial, BuildingModel.Office),
                    ModelOption(BuildTool.Commercial, BuildingModel.OfficeTower)),
                Category(UiText.Industrial, UiIcon.Factory, UiTheme.Industrial,
                    ModelOption(BuildTool.Industrial, BuildingModel.Workshop),
                    ModelOption(BuildTool.Industrial, BuildingModel.Warehouse),
                    ModelOption(BuildTool.Industrial, BuildingModel.Factory)),
                Category(UiText.Education, UiIcon.School, UiTheme.Education,
                    ModelOption(BuildTool.School, BuildingModel.Preschool),
                    ModelOption(BuildTool.School, BuildingModel.PrimarySchool),
                    ModelOption(BuildTool.School, BuildingModel.HighSchool),
                    ModelOption(BuildTool.School, BuildingModel.University)),
                Category(UiText.Services, UiIcon.Shield, UiTheme.Services,
                    ModelOption(BuildTool.CivicService, BuildingModel.PolicePost),
                    ModelOption(BuildTool.CivicService, BuildingModel.Clinic),
                    ModelOption(BuildTool.CivicService, BuildingModel.FireStationSmall),
                    ModelOption(BuildTool.CivicService, BuildingModel.PoliceStation),
                    ModelOption(BuildTool.CivicService, BuildingModel.Hospital),
                    ModelOption(BuildTool.CivicService, BuildingModel.FireStation),
                    ModelOption(BuildTool.CivicService, BuildingModel.Harbor)),
                Category(UiText.Parks, UiIcon.Park, UiTheme.Residential,
                    ModelOption(BuildTool.CivicService, BuildingModel.SmallPark),
                    ModelOption(BuildTool.CivicService, BuildingModel.Plaza),
                    ModelOption(BuildTool.CivicService, BuildingModel.LargePark)),
                Category(UiText.Utilities, UiIcon.Bolt, UiTheme.Utilities, new BuildOption(UiText.PowerPlant, BuildTool.PowerPlant),
                    ModelOption(BuildTool.PowerPlant, BuildingModel.GasPlant),
                    ModelOption(BuildTool.CivicService, BuildingModel.WaterPump),
                    ModelOption(BuildTool.CivicService, BuildingModel.SewagePlant),
                    ModelOption(BuildTool.CivicService, BuildingModel.Landfill),
                    ModelOption(BuildTool.CivicService, BuildingModel.Incinerator)),
                new BuildCategory(UiText.Map, UiIcon.Layers, UiTheme.MapTeal, true, new[]
                {
                    new BuildOption(UiText.Power, BuildTool.Pan, overlay: CityOverlayMode.Power),
                    new BuildOption(UiText.Services, BuildTool.Pan, overlay: CityOverlayMode.Services),
                    new BuildOption(UiText.Education, BuildTool.Pan, overlay: CityOverlayMode.Education),
                    new BuildOption(UiText.Environment, BuildTool.Pan, overlay: CityOverlayMode.Environment),
                    new BuildOption(UiText.Happiness, BuildTool.Pan, overlay: CityOverlayMode.Happiness),
                    new BuildOption(UiText.LandValue, BuildTool.Pan, overlay: CityOverlayMode.LandValue),
                    new BuildOption(UiText.Garbage, BuildTool.Pan, overlay: CityOverlayMode.Garbage),
                    new BuildOption(UiText.WorldMap, BuildTool.Pan, worldMap: true),
                    new BuildOption(UiText.OverlayOff, BuildTool.Pan)
                }),
                Category(UiText.Bulldoze, UiIcon.Delete, UiTheme.Demolish, new BuildOption(UiText.Bulldoze, BuildTool.Bulldoze))
            };

            // A floating dock centred on the bottom edge; BuildBarTop stays the height it reserves.
            var bar = CreatePanel(_safeAreaRoot, "BuildBar", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                Vector2.zero, Vector2.zero, UiTheme.Panel);
            var barRect = (RectTransform)bar.transform;
            barRect.pivot = new Vector2(0.5f, 0f);
            barRect.anchoredPosition = new Vector2(0f, 12f);
            barRect.sizeDelta = new Vector2(0f, BuildSlotHeight + 12f);
            UiTheme.Float(bar.GetComponent<Image>(), 18f);
            var row = bar.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(6, 6, 6, 6);
            row.spacing = 4;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = true;
            bar.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            var picker = CreatePanel(_safeAreaRoot, "BuildPicker", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                Vector2.zero, Vector2.zero, new Color(UiTheme.Panel.r, UiTheme.Panel.g, UiTheme.Panel.b, 0.92f));
            _buildPickerRect = (RectTransform)picker.transform;
            _buildPickerRect.pivot = new Vector2(0.5f, 0f);
            _buildPickerRect.anchoredPosition = new Vector2(0f, BuildBarTop);
            UiTheme.Float(picker.GetComponent<Image>(), 16f);
            _buildPickerTitle = BuildLabel(picker.transform, "", UiTheme.SmallSize - 1, new Vector2(0, 1), Vector2.one,
                new Vector2(16, -34), new Vector2(-16, -8), TextAnchor.MiddleLeft);
            _buildPickerTitle.supportRichText = true;
            _buildPicker = picker.AddComponent<ScrollRect>();
            _buildPicker.horizontal = true;
            _buildPicker.vertical = false;
            _buildPicker.movementType = ScrollRect.MovementType.Clamped;
            var viewport = BuildRect(picker.transform, "Viewport", Vector2.zero, Vector2.one, new Vector2(4, 4), new Vector2(-4, -38));
            viewport.gameObject.AddComponent<RectMask2D>();
            _buildPicker.viewport = viewport;

            foreach (BuildCategory category in _buildCategories)
            {
                if (category.Options[0].Tool == BuildTool.Bulldoze) CreateDivider(bar.transform);
                var slot = BuildRect(bar.transform, "Category", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                var slotSize = slot.gameObject.AddComponent<LayoutElement>();
                slotSize.minWidth = slotSize.preferredWidth = BuildSlotWidth;
                slotSize.minHeight = BuildSlotHeight;
                bool hasPicker = category.Options.Length > 1;
                category.Button = BuildButton(slot, "Choose", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                UiTheme.Round(category.Button.image, 12f, outline: false);
                category.Underline = BuildDecoration(category.Button.transform, "Underline", Vector2.zero, new Vector2(1, 0),
                    new Vector2(16, 2), new Vector2(-16, 5), category.Swatch);
                UiTheme.Round(category.Underline, 2f, outline: false);
                Text glyph = BuildLabel(category.Button.transform, category.Icon, 26,
                    new Vector2(0, hasPicker ? 0.46f : 0.38f), Vector2.one, Vector2.zero, new Vector2(0, -2));
                glyph.font = UiTheme.IconFont;
                glyph.resizeTextForBestFit = false;
                glyph.color = category.Swatch;
                Text name = BuildLabel(category.Button.transform, category.Name, UiTheme.TinySize - 1,
                    new Vector2(0, hasPicker ? 0.22f : 0f), new Vector2(1, hasPicker ? 0.48f : 0.40f), new Vector2(2, 0), new Vector2(-2, 0));
                name.fontStyle = FontStyle.Bold;
                if (hasPicker)
                {
                    category.PickLabel = BuildLabel(category.Button.transform, "", UiTheme.TinySize - 2,
                        Vector2.zero, new Vector2(1, 0.24f), new Vector2(3, 3), new Vector2(-3, 0));
                    category.PickLabel.color = UiTheme.TextMuted;

                    // Wide invisible hit area above the slot; the visible knob sits at its bottom.
                    category.Tab = BuildButton(slot, "PickerTab", new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                        new Vector2(-30, -6), new Vector2(30, 26));
                    category.Tab.image.color = Color.clear;
                    category.Knob = BuildDecoration(category.Tab.transform, "Knob", new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                        new Vector2(-18, 0), new Vector2(18, 20), UiTheme.Panel);
                    UiTheme.Round(category.Knob, 9f);
                    category.KnobGlyph = BuildLabel(category.Knob.transform, UiIcon.ExpandLess, 18, Vector2.zero, Vector2.one,
                        Vector2.zero, Vector2.zero);
                    category.KnobGlyph.font = UiTheme.IconFont;
                    category.KnobGlyph.resizeTextForBestFit = false;
                    category.KnobGlyph.color = UiTheme.TextSoft;
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
            row.padding = new RectOffset(8, 8, 4, 6);
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = true;
            category.Content.gameObject.AddComponent<ContentSizeFitter>().horizontalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            foreach (BuildOption option in category.Options)
            {
                Button card = BuildButton(category.Content, "Card", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                var size = card.gameObject.AddComponent<LayoutElement>();
                size.minWidth = size.preferredWidth = BuildCardWidth;
                size.minHeight = UiTheme.MinTouch;
                UiTheme.Round(card.image, 12f);
                Image ring = card.transform.Find("Outline").GetComponent<Image>();

                var thumb = BuildDecoration(card.transform, "Thumb", new Vector2(0, 1), Vector2.one,
                    new Vector2(4, -56), new Vector2(-4, -4), new Color(category.Swatch.r, category.Swatch.g, category.Swatch.b, 0.14f));
                UiTheme.Round(thumb, 9f, outline: false);
                string icon = OptionIcon(option);
                Text glyph = BuildLabel(thumb.transform, icon, 32, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                glyph.font = UiTheme.IconFont;
                glyph.resizeTextForBestFit = false;

                Text title = BuildLabel(card.transform, option.Name, UiTheme.SmallSize - 1, new Vector2(0, 1), Vector2.one,
                    new Vector2(10, -80), new Vector2(-10, -60), TextAnchor.MiddleLeft);
                title.fontStyle = FontStyle.Bold;
                string stats = BuildOptionStats(option, category.IsMap);
                Text details = BuildLabel(card.transform, stats, UiTheme.TinySize - 1, Vector2.zero, Vector2.one,
                    new Vector2(10, 6), new Vector2(-10, -82), TextAnchor.UpperLeft);
                string lockedStats = option.UnlockPopulation == 0 ? stats :
                    UiText.UnlockAt(option.UnlockPopulation) + "\n" + stats;
                category.Cards.Add((card, ring, title, details, glyph, icon, stats, lockedStats));
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
                return UiText.Price(option.Model == BuildingModel.Default ? CitySimulation.PowerPlantCost : BuildingCatalog.Get(option.Model).Cost, false) +
                    "\n" + UiText.Upkeep(CitySimulation.PowerUpkeep(option.Model)) + "\n" + UiText.PowerSupply(CitySimulation.PowerCapacity(option.Model));

            if (GarbageCatalog.IsFacility(option.Model))
            {
                var facility = GarbageCatalog.Get(option.Model);
                return UiText.Price(facility.Cost, false) + "\n" + UiText.Upkeep(facility.Upkeep) + "\n" +
                    UiText.Radius(facility.Radius) + "\n" + UiText.GarbageCapacity(facility.Capacity);
            }

            var item = BuildingCatalog.Get(option.Model);
            var park = ParkCatalog.Get(option.Model);
            int radius = ParkCatalog.IsPark(option.Model) ? park.Radius : ServiceCatalog.Get(option.Model).Radius;
            if (item.Type == CellType.Residential || item.Type == CellType.Commercial)
            {
                Density density = BuildingCatalog.DensityOf(option.Model);
                return UiText.Price(item.Cost, true) + "\n" +
                    UiText.DensityLine((int)density, BuildingCatalog.MinLandValue(density));
            }
            return UiText.Price(item.Cost, !BuildingCatalog.IsPublicService(item.Type)) + "\n" +
                UiText.Upkeep(BuildingCatalog.ServiceUpkeep(option.Model)) +
                (radius > 0 ? "\n" + UiText.Radius(radius) : "") +
                (item.Seats > 0 ? "\n" + UiText.Seats(item.Seats) : "") +
                (park.Bonus > 0 ? "\n" + UiText.ParkHappiness(park.Bonus) : "");
        }

        private void ToggleBuildPicker(BuildCategory category)
        {
            CancelGesture();
            bool wasOpen = _openBuildCategory == category;
            CloseBuildPicker();
            if (wasOpen) return;
            _openBuildCategory = category;
            float maxWidth = _safeAreaRoot.rect.width - 32f;
            _buildPickerRect.sizeDelta = new Vector2(
                Mathf.Min(maxWidth, category.Options.Length * (BuildCardWidth + 8f) + 16f), BuildPickerHeight);
            string hex = ColorUtility.ToHtmlStringRGB(category.Swatch);
            _buildPickerTitle.text = $"<color=#{hex}><b>{category.Name}</b></color>";
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
            category.Knob.color = UiTheme.Panel;
            category.KnobGlyph.text = UiIcon.ExpandLess;
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
            ClearRoadAnchor();
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
                    ShowToast(UiText.Unlocked(string.Join(", ", names)), UiTheme.Active, UiIcon.LockOpen);
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
            Color tint = category.Swatch;
            category.Button.image.color = selected ? new Color(tint.r, tint.g, tint.b, 0.16f) : Color.clear;
            category.Underline.enabled = selected;
            if (category.PickLabel != null) category.PickLabel.text = available ? pick.ShortName : UiText.Locked;
            if (_openBuildCategory != category) return;
            category.Knob.color = new Color(tint.r * 0.35f, tint.g * 0.35f, tint.b * 0.35f, 0.95f);
            category.KnobGlyph.text = UiIcon.ExpandMore;
            for (int i = 0; i < category.Cards.Count; i++)
            {
                var card = category.Cards[i];
                BuildOption option = category.Options[i];
                bool unlocked = option.UnlockPopulation <= _city.PeakPopulation;
                card.Button.interactable = unlocked;
                bool chosen = pick == option;
                if (category.IsMap && !option.WorldMap && option.Overlay == CityOverlayMode.None)
                    chosen = _overlays.Mode == CityOverlayMode.None;
                else if (category.IsMap)
                    chosen = _overlays.Mode != CityOverlayMode.None && chosen;
                chosen &= unlocked;
                card.Button.image.color = chosen ? new Color(tint.r, tint.g, tint.b, 0.10f) : UiTheme.Button;
                card.Ring.color = chosen ? tint : UiTheme.Outline;
                card.Title.color = unlocked ? UiTheme.Text : UiTheme.TextMuted;
                card.Glyph.text = unlocked ? card.Icon : UiIcon.Lock;
                card.Glyph.color = unlocked ? tint : UiTheme.TextMuted;
                card.Details.color = unlocked ? UiTheme.TextSoft : UiTheme.TextMuted;
                card.Details.text = unlocked ? card.Stats : card.LockedStats;
            }
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
            text.resizeTextMinSize = Mathf.Min(UiTheme.TinySize, fontSize - 3);
            text.resizeTextMaxSize = fontSize;
            return text;
        }
    }
}
