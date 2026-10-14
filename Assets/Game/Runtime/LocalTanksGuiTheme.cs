using UnityEngine;

namespace LocalTanks
{
    /// <summary>
    /// Shared IMGUI bridge for the existing laboratory and diagnostic windows. New player-facing UI uses
    /// UI Toolkit, but these older tools still need the same restrained palette, readable typography and
    /// control sizes while they are migrated.
    /// </summary>
    public static class LocalTanksGuiTheme
    {
        public const float Margin = 12f;
        public const float PanelPadding = 16f;
        public const float ControlHeight = 30f;
        public const float Gap = 8f;

        private static readonly Color PanelColor = new Color32(12, 15, 13, 247);
        private static readonly Color PanelQuietColor = new Color32(12, 15, 13, 224);
        private static readonly Color BorderColor = new Color32(205, 210, 200, 88);
        private static readonly Color TextColor = new Color32(226, 229, 221, 255);
        private static readonly Color CaptionColor = new Color32(174, 180, 171, 255);
        private static readonly Color AccentColor = new Color32(174, 190, 169, 255);
        private static readonly Color WarningColor = new Color32(220, 154, 126, 255);

        private static GUIStyle title;
        private static GUIStyle sectionTitle;
        private static GUIStyle body;
        private static GUIStyle caption;
        private static GUIStyle warning;
        private static GUIStyle button;
        private static GUIStyle primaryButton;
        private static GUIStyle textArea;
        private static GUIStyle slider;
        private static GUIStyle sliderThumb;

        public static GUIStyle Title => title ??= LabelStyle(20, FontStyle.Bold, TextColor, true);
        public static GUIStyle SectionTitle => sectionTitle ??= LabelStyle(14, FontStyle.Bold, AccentColor, true);
        public static GUIStyle Body => body ??= LabelStyle(14, FontStyle.Normal, TextColor, true);
        public static GUIStyle Caption => caption ??= LabelStyle(12, FontStyle.Normal, CaptionColor, true);
        public static GUIStyle Warning => warning ??= LabelStyle(13, FontStyle.Normal, WarningColor, true);

        public static GUIStyle TextArea
        {
            get
            {
                if (textArea != null) return textArea;
                textArea = new GUIStyle(GUI.skin.textArea)
                {
                    fontSize = 14,
                    wordWrap = true,
                    padding = new RectOffset(10, 10, 8, 8),
                    normal = { textColor = TextColor, background = Solid(new Color32(25, 30, 26, 255)) },
                    focused = { textColor = TextColor, background = Solid(new Color32(31, 38, 32, 255)) }
                };
                return textArea;
            }
        }

        public static void DrawPanel(Rect rect, bool solid = true)
        {
            DrawRect(rect, solid ? PanelColor : PanelQuietColor);
            DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), BorderColor);
            DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), BorderColor);
            DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), BorderColor);
            DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), BorderColor);
        }

        public static bool Button(Rect rect, string text, bool primary = false)
        {
            GUIStyle style = primary ? PrimaryButtonStyle : ButtonStyle;
            return GUI.Button(rect, text, style);
        }

        public static bool Toggle(Rect rect, bool value, string text)
        {
            string marker = value ? "✓" : "—";
            return Button(rect, $"{marker}  {text}") ? !value : value;
        }

        public static float HorizontalSlider(Rect rect, float value, float minimum, float maximum)
        {
            EnsureSliderStyles();
            return GUI.HorizontalSlider(rect, value, minimum, maximum, slider, sliderThumb);
        }

        public static Rect ClampToScreen(Rect rect, float margin = Margin)
        {
            float width = Mathf.Min(rect.width, Mathf.Max(1f, Screen.width - margin * 2f));
            float height = Mathf.Min(rect.height, Mathf.Max(1f, Screen.height - margin * 2f));
            float x = Mathf.Clamp(rect.x, margin, Mathf.Max(margin, Screen.width - width - margin));
            float y = Mathf.Clamp(rect.y, margin, Mathf.Max(margin, Screen.height - height - margin));
            return new Rect(x, y, width, height);
        }

        public static Rect Union(Rect first, Rect second)
        {
            float xMin = Mathf.Min(first.xMin, second.xMin);
            float yMin = Mathf.Min(first.yMin, second.yMin);
            float xMax = Mathf.Max(first.xMax, second.xMax);
            float yMax = Mathf.Max(first.yMax, second.yMax);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static GUIStyle ButtonStyle
        {
            get
            {
                EnsureButtonStyles();
                return button;
            }
        }

        private static GUIStyle PrimaryButtonStyle
        {
            get
            {
                EnsureButtonStyles();
                return primaryButton;
            }
        }

        private static void EnsureButtonStyles()
        {
            if (button != null) return;
            button = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = ControlHeight,
                padding = new RectOffset(12, 12, 5, 5),
                normal = { textColor = TextColor, background = Solid(new Color32(49, 56, 50, 255)) },
                hover = { textColor = Color.white, background = Solid(new Color32(65, 74, 66, 255)) },
                active = { textColor = Color.white, background = Solid(new Color32(38, 44, 39, 255)) },
                focused = { textColor = Color.white, background = Solid(new Color32(65, 78, 67, 255)) }
            };
            primaryButton = new GUIStyle(button)
            {
                normal = { textColor = TextColor, background = Solid(new Color32(65, 78, 67, 255)) },
                hover = { textColor = Color.white, background = Solid(new Color32(79, 94, 81, 255)) },
                active = { textColor = Color.white, background = Solid(new Color32(48, 59, 50, 255)) }
            };
        }

        private static void EnsureSliderStyles()
        {
            if (slider != null) return;
            slider = new GUIStyle(GUI.skin.horizontalSlider)
            {
                fixedHeight = 6f,
                normal = { background = Solid(new Color32(35, 41, 36, 255)) }
            };
            sliderThumb = new GUIStyle(GUI.skin.horizontalSliderThumb)
            {
                fixedWidth = 12f,
                fixedHeight = 20f,
                normal = { background = Solid(new Color32(212, 218, 207, 255)) },
                hover = { background = Solid(new Color32(235, 239, 230, 255)) },
                active = { background = Solid(new Color32(151, 171, 145, 255)) }
            };
        }

        private static GUIStyle LabelStyle(int size, FontStyle fontStyle, Color color, bool wordWrap)
        {
            return new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = fontStyle,
                wordWrap = wordWrap,
                clipping = TextClipping.Clip,
                normal = { textColor = color }
            };
        }

        private static Texture2D Solid(Color color)
        {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };
            texture.SetPixel(0, 0, color);
            texture.Apply(false, true);
            return texture;
        }

        private static void DrawRect(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
