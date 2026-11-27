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
        // A relaxed pace: one full day and night every 10 minutes at 1x speed.
        private const float CycleDurationSeconds = 600f;

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
            else if (mode == LightingMode.Sunset) _timeOfDay = 0.737f;
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

            // Sky gradient, ambient, fog and water palette matched so fog equals horizon in every direction.
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

            Color dayFog = new Color(0.71f, 0.84f, 0.90f);
            Color sunsetFog = new Color(0.88f, 0.58f, 0.42f);
            Color nightFog = new Color(0.06f, 0.10f, 0.18f);
            Color horizon = Color.Lerp(nightFog, Color.Lerp(dayFog, sunsetFog, sunsetBand * 0.82f), dayFrac);
            RenderSettings.fogColor = horizon;
            if (_cam != null) _cam.backgroundColor = horizon;

            Color dayZenith = new Color(0.24f, 0.50f, 0.82f);
            Color sunsetZenith = new Color(0.22f, 0.27f, 0.52f);
            Color nightZenith = new Color(0.02f, 0.04f, 0.09f);
            Color zenith = Color.Lerp(nightZenith, Color.Lerp(dayZenith, sunsetZenith, sunsetBand * 0.80f), dayFrac);

            Color sunTint = Color.Lerp(new Color(1.00f, 0.92f, 0.78f), new Color(1.00f, 0.62f, 0.32f), sunsetBand);
            sunTint.a = dayFrac * (0.12f + sunsetBand * 0.48f);
            Vector3 sunDir = _sun != null ? -_sun.transform.forward : new Vector3(0.3f, 0.6f, 0.7f);

            Shader.SetGlobalColor("_XCitySkyHorizon", horizon);
            Shader.SetGlobalColor("_XCitySkyZenith", zenith);
            Shader.SetGlobalColor("_XCitySkyGlow", sunTint);
            Shader.SetGlobalVector("_XCitySunDir", new Vector4(sunDir.x, sunDir.y, sunDir.z, 0f));

            if (_waterMat != null)
            {
                Color dayWater = new Color(0.14f, 0.34f, 0.48f);
                Color sunsetWater = new Color(0.24f, 0.29f, 0.40f);
                Color nightWater = new Color(0.05f, 0.12f, 0.22f);
                _waterMat.color = Color.Lerp(nightWater, Color.Lerp(dayWater, sunsetWater, sunsetBand * 0.55f), dayFrac);
            }

            Color dayShallow = new Color(0.20f, 0.50f, 0.54f);
            Color sunsetShallow = new Color(0.36f, 0.40f, 0.46f);
            Color nightShallow = new Color(0.08f, 0.18f, 0.28f);
            Shader.SetGlobalColor("_XCityWaterShallow", Color.Lerp(nightShallow, Color.Lerp(dayShallow, sunsetShallow, sunsetBand * 0.55f), dayFrac));

            Shader.SetGlobalFloat("_XCityNight", NightFactor);
        }
    }
}
