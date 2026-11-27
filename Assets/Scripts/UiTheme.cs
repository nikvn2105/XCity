using UnityEngine;

namespace XCity
{
    // One dark, translucent look for every HUD element. New UI uses these names, never raw colors.
    public static class UiTheme
    {
        // Surfaces: translucent so the city stays visible behind the HUD.
        public static readonly Color Panel = new Color(0.07f, 0.09f, 0.13f, 0.82f);
        public static readonly Color PanelSolid = new Color(0.06f, 0.08f, 0.12f, 0.96f);
        public static readonly Color Inset = new Color(0.08f, 0.10f, 0.14f, 1f);

        // Controls.
        public static readonly Color Button = new Color(0.20f, 0.24f, 0.30f, 1f);
        public static readonly Color ButtonLocked = new Color(0.13f, 0.15f, 0.19f, 0.65f);
        public static readonly Color Active = new Color(0.19f, 0.52f, 0.96f, 1f);
        public static readonly Color Danger = new Color(0.58f, 0.24f, 0.24f, 1f);

        // Text.
        public static readonly Color Text = Color.white;
        public static readonly Color TextMuted = new Color(0.62f, 0.66f, 0.72f, 0.75f);
        public static readonly Color TextSoft = new Color(0.82f, 0.86f, 0.92f);
        public static readonly Color Warning = new Color(1f, 0.82f, 0.25f);

        // Zone demand colors (R, C, I).
        public static readonly Color Residential = new Color(0.29f, 0.83f, 0.46f);
        public static readonly Color Commercial = new Color(0.27f, 0.64f, 0.96f);
        public static readonly Color Industrial = new Color(0.94f, 0.68f, 0.20f);

        // Type scale.
        public const int TitleSize = 22;
        public const int BodySize = 20;
        public const int SmallSize = 15;
        public const int TinySize = 13;

        // Apple's minimum comfortable touch target, in canvas units.
        public const float MinTouch = 44f;

        private static Font _font;
        public static Font Font => _font != null ? _font : _font = Resources.Load<Font>("NotoSans-Regular");
    }
}
