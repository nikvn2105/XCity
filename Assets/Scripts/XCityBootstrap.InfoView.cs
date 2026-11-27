using UnityEngine;
using UnityEngine.UI;

namespace XCity
{
    public partial class XCityBootstrap
    {
        private const float MinCamDistance = 7f;
        private const float MaxCamDistance = 2800f;

        private GameObject _infoLegend;

        private void UpdateAtmosphereRanges()
        {
            RenderSettings.fogStartDistance = _camDistance <= 55f ? 120f : Mathf.Max(120f, _camDistance * 1.48f);
            RenderSettings.fogEndDistance = _camDistance <= 55f ? 265f : Mathf.Max(265f, _camDistance * 2.50f);
            if (_cam == null) return;
            _cam.nearClipPlane = _camDistance <= 55f ? 0.5f : Mathf.Max(0.5f, _camDistance * 0.015f);
            _cam.farClipPlane = Mathf.Max(320f, _camDistance * 1.85f + 220f);
        }

        private void SetupInfoView(Font font)
        {
            Transform ocean = transform.Find("Ocean");
            if (ocean != null)
                ocean.localScale = new Vector3(GridSize * CellSize + 16000f, 0.2f, GridSize * CellSize + 16000f);

            // Sits just below the utility toolbar (which spans -110..-68) so neither hides the other.
            _infoLegend = CreatePanel(_safeAreaRoot, "ServiceLegend",
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(12, -148), new Vector2(-12, -114),
                new Color(0.08f, 0.10f, 0.14f, 0.88f));

            var txtObj = new GameObject("LegendText");
            txtObj.transform.SetParent(_infoLegend.transform, false);
            var tr = txtObj.AddComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(12, 2);
            tr.offsetMax = new Vector2(-12, -2);
            var txt = txtObj.AddComponent<Text>();
            txt.font = font;
            txt.fontSize = 15;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleLeft;
            txt.resizeTextForBestFit = true;
            txt.resizeTextMinSize = 11;
            txt.resizeTextMaxSize = 16;
            txt.text = "Hạnh phúc: Đỏ (10) -> Vàng (55) -> Xanh (100)  |  Phủ dịch vụ: Lam (Giáo dục) · Ngọc (Y tế) · Chàm (Công an) · Cam (Cứu hỏa)  |  Khác: Xám";

            RefreshInfoViewHUD();
        }

        private void RefreshInfoViewHUD()
        {
            bool active = _cityRenderer != null && _cityRenderer.InfoViewEnabled;
            if (_infoLegend != null)
                _infoLegend.SetActive(active);
        }
    }
}
