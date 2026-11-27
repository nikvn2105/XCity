using UnityEngine;
using UnityEngine.UI;

namespace XCity
{
    public partial class XCityBootstrap
    {
        private const string TutorialDoneKey = "xcity.tutorial";
        private const float TutorialDoneBannerSeconds = 4f;

        private bool _tutorialActive;
        private int _tutorialStep;
        private float _tutorialDoneBannerUntil;
        private Button _skipTutorialButton;
        private RectTransform _statusTextRect;

        private bool HasAnyOccupiedCell()
        {
            foreach (int _ in _city.OccupiedIndices) return true;
            return false;
        }

        private void SetupTutorial(Transform topBar, RectTransform statusRect, Font font)
        {
            _statusTextRect = statusRect;
            if (_previewOutput != null || PlayerPrefs.GetInt(TutorialDoneKey, 0) != 0 ||
                _city.TickCount >= 5 || HasAnyOccupiedCell())
                return;

            _tutorialActive = true;
            _tutorialStep = 0;
            _tutorialDoneBannerUntil = 0f;

            _skipTutorialButton = CreateHudButton(topBar, font, "Bỏ qua", "SkipTutorial");
            var txt = _skipTutorialButton.GetComponentInChildren<Text>();
            if (txt != null) txt.fontSize = UiTheme.SmallSize;
            _skipTutorialButton.image.color = new Color(0.22f, 0.27f, 0.35f, 1f);

            var skipRect = (RectTransform)_skipTutorialButton.transform;
            skipRect.anchorMin = new Vector2(1f, 0f);
            skipRect.anchorMax = Vector2.one;
            skipRect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            skipRect.offsetMin = new Vector2(-500f, 6f);
            skipRect.offsetMax = new Vector2(-400f, -6f);
            _skipTutorialButton.onClick.AddListener(SkipTutorial);

        }

        private void SkipTutorial()
        {
            CancelGesture();
            FinishTutorial(showBanner: false);
            RefreshHUD();
        }

        private void FinishTutorial(bool showBanner)
        {
            _tutorialActive = false;
            PlayerPrefs.SetInt(TutorialDoneKey, 1);
            PlayerPrefs.Save();
            if (_skipTutorialButton != null)
                _skipTutorialButton.gameObject.SetActive(false);
            _tutorialDoneBannerUntil = showBanner ? Time.unscaledTime + TutorialDoneBannerSeconds : 0f;
        }

        private void UpdateTutorial()
        {
            if (_tutorialDoneBannerUntil > 0f && Time.unscaledTime >= _tutorialDoneBannerUntil)
            {
                _tutorialDoneBannerUntil = 0f;
                RefreshHUD();
            }
        }

        private string GetTutorialStatusPrefix()
        {
            if (_tutorialActive)
            {
                bool hasRoad = false;
                bool hasPoweredPlant = false;
                bool hasResidential = false;
                bool hasBusiness = false;
                bool hasWaterPump = false;

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

                while (_tutorialStep < 5)
                {
                    bool completed = _tutorialStep switch
                    {
                        0 => hasRoad,
                        1 => hasPoweredPlant,
                        2 => hasResidential,
                        3 => hasBusiness,
                        4 => hasWaterPump,
                        _ => false
                    };
                    if (!completed) break;
                    _tutorialStep++;
                }

                if (_tutorialStep < 5)
                {
                    return _tutorialStep switch
                    {
                        0 => "<color=#FFE066>Hướng dẫn (1/5): Vẽ đường</color>",
                        1 => "<color=#FFE066>Hướng dẫn (2/5): Đặt nhà máy điện cạnh đường</color>",
                        2 => "<color=#FFE066>Hướng dẫn (3/5): Phân khu dân cư (Xây > Nhà ở)</color>",
                        3 => "<color=#FFE066>Hướng dẫn (4/5): Phân khu thương mại/công nghiệp (Xây > Thương mại, Công nghiệp)</color>",
                        _ => "<color=#FFE066>Hướng dẫn (5/5): Đặt trạm bơm nước sát bờ (Xây > Tiện ích)</color>"
                    };
                }

                FinishTutorial(showBanner: true);
            }

            return Time.unscaledTime < _tutorialDoneBannerUntil
                ? "<color=#8CE99A>Đã hoàn thành hướng dẫn cơ bản</color>"
                : null;
        }
    }
}
