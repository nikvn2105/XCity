using UnityEngine;

namespace XCity
{
    public enum LightingMode
    {
        AutoCycle,
        Day,
        Sunset,
        Night
    }

    internal sealed class DayNightSystem
    {
        private const float CycleDurationSeconds = 120f;

        private readonly Light _sun;
        private readonly Camera _cam;
        private readonly Material _waterMat;
        private float _timeOfDay = 0.43f; // 10:19 AM morning start

        public LightingMode Mode { get; private set; } = LightingMode.AutoCycle;
        public float NightFactor { get; private set; }

        public DayNightSystem(Light sun, Camera cam, Material waterMat)
        {
            _sun = sun;
            _cam = cam;
            _waterMat = waterMat;
            ApplyAtmosphere();
        }

        public void SetMode(LightingMode mode)
        {
            Mode = mode;
            if (mode == LightingMode.Day) _timeOfDay = 0.44f;
            else if (mode == LightingMode.Sunset) _timeOfDay = 0.765f;
            else if (mode == LightingMode.Night) _timeOfDay = 0.92f;
            ApplyAtmosphere();
        }

        public void CycleMode()
        {
            LightingMode next = Mode switch
            {
                LightingMode.AutoCycle => LightingMode.Day,
                LightingMode.Day => LightingMode.Sunset,
                LightingMode.Sunset => LightingMode.Night,
                _ => LightingMode.AutoCycle
            };
            SetMode(next);
        }

        public string GetModeLabel() => Mode switch
        {
            LightingMode.AutoCycle => $"Tự động {FormatClock()}",
            LightingMode.Day => "Ngày",
            LightingMode.Sunset => "Hoàng hôn",
            _ => "Đêm"
        };

        public string FormatClock()
        {
            int totalMinutes = Mathf.FloorToInt(Mathf.Repeat(_timeOfDay, 1f) * 24f * 60f);
            int hh = (totalMinutes / 60) % 24;
            int mm = (totalMinutes % 60 / 10) * 10;
            return $"{hh:00}:{mm:00}";
        }

        public void Update(float dt)
        {
            if (Mode == LightingMode.AutoCycle && dt > 0f)
                _timeOfDay = Mathf.Repeat(_timeOfDay + dt / CycleDurationSeconds, 1f);
            ApplyAtmosphere();
        }

        private void ApplyAtmosphere()
        {
            float sunElev = -Mathf.Cos(_timeOfDay * Mathf.PI * 2f); // +1 noon, -1 midnight
            float dayFrac = Mathf.InverseLerp(-0.08f, 0.34f, sunElev);
            float sunsetBand = Mathf.Exp(-Mathf.Pow((sunElev - 0.08f) / 0.18f, 2f));
            NightFactor = 1f - dayFrac;

            // Sun / Moon directional light angle & color
            float pitchDeg = sunElev >= -0.05f
                ? Mathf.Lerp(12f, 64f, Mathf.Clamp01(sunElev))
                : Mathf.Lerp(26f, 52f, Mathf.Clamp01(-sunElev));
            float yawDeg = -34f + (_timeOfDay - 0.5f) * 110f;
            if (_sun != null)
            {
                _sun.transform.rotation = Quaternion.Euler(pitchDeg, yawDeg, 0f);
                Color noonLight = new Color(1.00f, 0.96f, 0.88f);
                Color sunsetLight = new Color(1.00f, 0.58f, 0.32f);
                Color moonLight = new Color(0.42f, 0.54f, 0.82f);
                Color dayTint = Color.Lerp(noonLight, sunsetLight, sunsetBand * 0.85f);
                _sun.color = Color.Lerp(moonLight, dayTint, dayFrac);
                _sun.intensity = Mathf.Lerp(0.34f, 0.98f, dayFrac) + sunsetBand * 0.08f;
                _sun.shadowStrength = Mathf.Lerp(0.42f, 0.64f, dayFrac);
            }

            // Sky, ambient, fog, ocean palette matched cleanly to classic bright day at NightFactor == 0
            Color daySky = new Color(0.65f, 0.73f, 0.81f);
            Color sunsetSky = new Color(0.76f, 0.50f, 0.46f);
            Color nightSky = new Color(0.11f, 0.16f, 0.28f);
            RenderSettings.ambientSkyColor = Color.Lerp(nightSky, Color.Lerp(daySky, sunsetSky, sunsetBand * 0.7f), dayFrac);

            Color dayEq = new Color(0.48f, 0.55f, 0.50f);
            Color sunsetEq = new Color(0.56f, 0.39f, 0.36f);
            Color nightEq = new Color(0.08f, 0.12f, 0.21f);
            RenderSettings.ambientEquatorColor = Color.Lerp(nightEq, Color.Lerp(dayEq, sunsetEq, sunsetBand * 0.65f), dayFrac);

            Color dayGr = new Color(0.31f, 0.34f, 0.32f);
            Color nightGr = new Color(0.05f, 0.07f, 0.12f);
            RenderSettings.ambientGroundColor = Color.Lerp(nightGr, dayGr, dayFrac);

            Color dayFog = new Color(0.69f, 0.82f, 0.86f);
            Color sunsetFog = new Color(0.84f, 0.58f, 0.44f);
            Color nightFog = new Color(0.06f, 0.09f, 0.17f);
            Color fog = Color.Lerp(nightFog, Color.Lerp(dayFog, sunsetFog, sunsetBand * 0.78f), dayFrac);
            RenderSettings.fogColor = fog;
            if (_cam != null) _cam.backgroundColor = fog;

            if (_waterMat != null)
            {
                Color dayWater = new Color(0.37f, 0.58f, 0.64f);
                Color sunsetWater = new Color(0.40f, 0.44f, 0.52f);
                Color nightWater = new Color(0.07f, 0.14f, 0.24f);
                _waterMat.color = Color.Lerp(nightWater, Color.Lerp(dayWater, sunsetWater, sunsetBand * 0.55f), dayFrac);
            }

            Shader.SetGlobalFloat("_XCityNight", NightFactor);
        }
    }
}
