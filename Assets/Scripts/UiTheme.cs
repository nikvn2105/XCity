using UnityEngine;
using UnityEngine.UI;

namespace XCity
{
    // One dark, translucent look for every HUD element. New UI uses these names, never raw colors.
    public static class UiTheme
    {
        // Surfaces: translucent so the city stays visible behind the HUD.
        public static readonly Color Panel = new Color(0.05f, 0.07f, 0.10f, 0.80f);
        public static readonly Color PanelSolid = new Color(0.06f, 0.08f, 0.12f, 0.96f);
        public static readonly Color Inset = new Color(0.08f, 0.10f, 0.14f, 1f);

        // Controls.
        public static readonly Color Button = new Color(1f, 1f, 1f, 0.07f);
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

        public static readonly Color Outline = new Color(1f, 1f, 1f, 0.10f);
        public static readonly Color Divider = new Color(1f, 1f, 1f, 0.08f);
        public static readonly Color Positive = new Color(0.37f, 0.88f, 0.60f);
        public static readonly Color Gold = new Color(0.96f, 0.77f, 0.32f);
        public static readonly Color Sky = new Color(0.50f, 0.85f, 1f);

        private static Font _font;
        public static Font Font => _font != null ? _font : _font = Resources.Load<Font>("NotoSans-Regular");

        // Material Icons subset (Assets/Resources/XCityIcons.ttf); glyphs are listed in UiIcon.
        private static Font _iconFont;
        public static Font IconFont => _iconFont != null ? _iconFont : _iconFont = Resources.Load<Font>("XCityIcons");

        // Rounded corners: one baked 9-slice sprite, scaled per use with pixelsPerUnitMultiplier.
        private const int SpriteSize = 64;
        private const float BakedRadius = 16f;
        private static Sprite _fill;
        private static Sprite _ring;

        private static Sprite Fill => _fill != null ? _fill : _fill = BakeRounded(false);
        private static Sprite Ring => _ring != null ? _ring : _ring = BakeRounded(true);

        // Gives an Image rounded corners and, optionally, a thin light outline. Colors set later still apply.
        public static void Round(Image image, float radius = 14f, bool outline = true)
        {
            image.sprite = Fill;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = BakedRadius / Mathf.Max(1f, radius);
            if (!outline) return;
            var ring = new GameObject("Outline", typeof(RectTransform));
            var rect = (RectTransform)ring.transform;
            rect.SetParent(image.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            ring.AddComponent<LayoutElement>().ignoreLayout = true;
            var ringImage = ring.AddComponent<Image>();
            ringImage.sprite = Ring;
            ringImage.type = Image.Type.Sliced;
            ringImage.pixelsPerUnitMultiplier = image.pixelsPerUnitMultiplier;
            ringImage.color = Outline;
            ringImage.raycastTarget = false;
        }

        // A floating panel: rounded, outlined, with a soft drop shadow under it.
        public static void Float(Image image, float radius = 14f)
        {
            Round(image, radius);
            var shadow = image.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.30f);
            shadow.effectDistance = new Vector2(0f, -3f);
        }

        private static Sprite BakeRounded(bool ringOnly)
        {
            var texture = new Texture2D(SpriteSize, SpriteSize, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[SpriteSize * SpriteSize];
            float half = SpriteSize * 0.5f;
            for (int y = 0; y < SpriteSize; y++)
            for (int x = 0; x < SpriteSize; x++)
            {
                // Signed distance to a rounded square filling the texture.
                float qx = Mathf.Abs(x + 0.5f - half) - (half - BakedRadius);
                float qy = Mathf.Abs(y + 0.5f - half) - (half - BakedRadius);
                float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
                float d = outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - BakedRadius;
                float cover = Mathf.Clamp01(0.5f - d);
                if (ringOnly) cover -= Mathf.Clamp01(0.5f - (d + 1.2f));
                pixels[y * SpriteSize + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(cover) * 255f));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            float border = BakedRadius + 4f;
            return Sprite.Create(texture, new Rect(0, 0, SpriteSize, SpriteSize), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }
    }
}
