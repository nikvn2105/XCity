using UnityEngine;
using UnityEngine.UI;

namespace XCity
{
    // First-city tutorial as a quest card (top left): one step at a time, the build-bar category it
    // needs pulses gold, and each finished step shows a toast. Skippable; never shown again once done.
    public partial class XCityBootstrap
    {
        private const string TutorialDoneKey = "xcity.tutorial";
        private const int TutorialSteps = 5;

        private bool _tutorialActive;
        private int _tutorialStep;
        private GameObject _questCard;
        private Text _questStepText;
        private Text _questTitle;
        private Text _questBody;
        private readonly Image[] _questDots = new Image[TutorialSteps];
        private Image _spotlight;
        private BuildCategory _spotlightCategory;

        private bool HasAnyOccupiedCell()
        {
            foreach (int _ in _city.OccupiedIndices) return true;
            return false;
        }

        private void SetupTutorial()
        {
            if (_previewOutput != null || PlayerPrefs.GetInt(TutorialDoneKey, 0) != 0 ||
                _city.TickCount >= 5 || HasAnyOccupiedCell())
                return;

            _tutorialActive = true;
            _tutorialStep = 0;

            _questCard = CreatePanel(_safeAreaRoot, "QuestCard", new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(16, -190), new Vector2(336, -78), UiTheme.Panel);
            UiTheme.Float(_questCard.GetComponent<Image>());

            _questStepText = BuildLabel(_questCard.transform, "", UiTheme.TinySize - 1, new Vector2(0, 1), Vector2.one,
                new Vector2(14, -28), new Vector2(-56, -10), TextAnchor.MiddleLeft);
            _questStepText.color = UiTheme.Gold;
            _questStepText.fontStyle = FontStyle.Bold;
            _questTitle = BuildLabel(_questCard.transform, "", UiTheme.SmallSize + 1, new Vector2(0, 1), Vector2.one,
                new Vector2(14, -52), new Vector2(-56, -28), TextAnchor.MiddleLeft);
            _questTitle.fontStyle = FontStyle.Bold;
            _questBody = BuildLabel(_questCard.transform, "", UiTheme.TinySize, Vector2.zero, Vector2.one,
                new Vector2(14, 22), new Vector2(-14, -54), TextAnchor.UpperLeft);
            _questBody.color = UiTheme.TextSoft;

            for (int i = 0; i < TutorialSteps; i++)
            {
                _questDots[i] = BuildDecoration(_questCard.transform, "Dot", Vector2.zero, Vector2.zero,
                    new Vector2(14 + i * 22, 10), new Vector2(30 + i * 22, 14), UiTheme.Divider);
                UiTheme.Round(_questDots[i], 2f, outline: false);
            }

            Button skip = CreateIconButton(_questCard.transform, UiIcon.Close, "SkipTutorial", 20);
            var skipRect = (RectTransform)skip.transform;
            skipRect.anchorMin = skipRect.anchorMax = Vector2.one;
            skipRect.offsetMin = new Vector2(-UiTheme.MinTouch - 6, -UiTheme.MinTouch - 6);
            skipRect.offsetMax = new Vector2(-6, -6);
            skip.onClick.AddListener(SkipTutorial);

            ShowQuestStep();
        }

        private void SkipTutorial()
        {
            CancelGesture();
            FinishTutorial();
            RefreshHUD();
        }

        private void FinishTutorial()
        {
            _tutorialActive = false;
            PlayerPrefs.SetInt(TutorialDoneKey, 1);
            PlayerPrefs.Save();
            if (_questCard != null) _questCard.SetActive(false);
            SetSpotlight(null);
        }

        // Pulses the spotlight on the category the current step needs.
        private void UpdateTutorial()
        {
            if (!_tutorialActive) return;
            if (_spotlightCategory == null) ShowQuestStep(); // the build bar may not have existed yet
            if (_spotlight != null)
            {
                Color gold = UiTheme.Gold;
                gold.a = 0.45f + 0.55f * Mathf.PingPong(Time.unscaledTime * 1.6f, 1f);
                _spotlight.color = gold;
            }
        }

        // Called from RefreshHUD: advances past every step the city already satisfies.
        private void UpdateTutorialProgress()
        {
            if (!_tutorialActive) return;
            bool hasRoad = false, hasPoweredPlant = false, hasResidential = false, hasBusiness = false, hasWaterPump = false;
            foreach (int index in _city.OccupiedIndices)
            {
                int x = index / GridSize;
                int z = index % GridSize;
                CityCell cell = _city.GetCell(x, z);
                if (cell.Type == CellType.Road) hasRoad = true;
                else if (cell.Type == CellType.PowerPlant && _city.Roads.HasRoadAccess(x, z)) hasPoweredPlant = true;
                else if (cell.Type == CellType.Residential) hasResidential = true;
                else if (cell.Type == CellType.Commercial || cell.Type == CellType.Industrial) hasBusiness = true;
                if (cell.Model == BuildingModel.WaterPump) hasWaterPump = true;
            }

            int before = _tutorialStep;
            while (_tutorialStep < TutorialSteps)
            {
                bool completed = _tutorialStep switch
                {
                    0 => hasRoad,
                    1 => hasPoweredPlant,
                    2 => hasResidential,
                    3 => hasBusiness,
                    _ => hasWaterPump
                };
                if (!completed) break;
                _tutorialStep++;
            }
            if (_tutorialStep == before) return;

            if (_tutorialStep >= TutorialSteps)
            {
                ShowToast(UiText.TutorialDone, UiTheme.Positive, UiIcon.Check);
                FinishTutorial();
                return;
            }
            ShowToast(UiText.QuestDone(UiText.QuestTitles[before]), UiTheme.Positive, UiIcon.Check);
            ShowQuestStep();
        }

        private void ShowQuestStep()
        {
            if (_questCard == null) return;
            _questStepText.text = UiText.QuestStep(_tutorialStep + 1, TutorialSteps);
            _questTitle.text = UiText.QuestTitles[_tutorialStep];
            _questBody.text = UiText.QuestBodies[_tutorialStep];
            for (int i = 0; i < TutorialSteps; i++)
                _questDots[i].color = i < _tutorialStep ? UiTheme.Positive : i == _tutorialStep ? UiTheme.Gold : UiTheme.Divider;

            string category = _tutorialStep switch
            {
                0 => UiText.Road,
                1 or 4 => UiText.Utilities,
                2 => UiText.Residential,
                _ => UiText.Commercial
            };
            BuildCategory target = null;
            if (_buildCategories != null)
                foreach (BuildCategory c in _buildCategories)
                    if (c.Name == category) target = c;
            SetSpotlight(target);
        }

        private void SetSpotlight(BuildCategory category)
        {
            if (_spotlight != null) Destroy(_spotlight.transform.parent.gameObject);
            _spotlight = null;
            _spotlightCategory = category;
            if (category == null) return;
            var holder = BuildDecoration(category.Button.transform, "Spotlight", Vector2.zero, Vector2.one,
                new Vector2(-3, -3), new Vector2(3, 3), Color.clear);
            UiTheme.Round(holder, 14f);
            _spotlight = holder.transform.Find("Outline").GetComponent<Image>();
        }
    }
}
