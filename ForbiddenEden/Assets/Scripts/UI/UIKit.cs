using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ForbiddenEden.UI
{
    /// <summary>Colours of the 天空港 look: deep night-blue glass panels, sky accent, warm gold highlights.</summary>
    public static class Theme
    {
        public static readonly Color Backdrop = new Color(0.03f, 0.05f, 0.10f, 0.55f);
        public static readonly Color Panel = new Color(0.07f, 0.10f, 0.19f, 0.90f);
        public static readonly Color PanelSoft = new Color(0.10f, 0.15f, 0.27f, 0.82f);
        public static readonly Color PanelLight = new Color(0.16f, 0.22f, 0.38f, 0.92f);
        public static readonly Color Line = new Color(0.45f, 0.62f, 0.90f, 0.35f);
        public static readonly Color Accent = new Color(0.42f, 0.74f, 1.00f, 1f);
        public static readonly Color AccentDark = new Color(0.18f, 0.36f, 0.62f, 1f);
        public static readonly Color Gold = new Color(0.98f, 0.81f, 0.47f, 1f);
        public static readonly Color Text = new Color(0.93f, 0.95f, 1.00f, 1f);
        public static readonly Color TextDim = new Color(0.66f, 0.73f, 0.86f, 1f);
        public static readonly Color TextMuted = new Color(0.48f, 0.54f, 0.66f, 1f);
        public static readonly Color Danger = new Color(1.00f, 0.47f, 0.47f, 1f);
        public static readonly Color Ok = new Color(0.50f, 0.90f, 0.66f, 1f);
        public static readonly Color Warn = new Color(1.00f, 0.78f, 0.40f, 1f);

        /// <summary>A name-tag colour from the server's speaker_hue (0–360).</summary>
        public static Color FromHue(int hue, float s = 0.55f, float v = 0.95f) =>
            Color.HSVToRGB(((hue % 360) + 360) % 360 / 360f, s, v);
    }

    public enum ButtonStyle { Normal, Primary, Ghost, Danger, Menu }

    /// <summary>Builders for a uGUI interface made entirely from code.</summary>
    public static class UIKit
    {
        private static Font _font;
        private static Sprite _rounded;
        private static Sprite _roundedSmall;
        private static Sprite _circle;
        private static Sprite _vignette;

        /// <summary>Scales every label; set from the 字級 setting.</summary>
        public static float FontScale = 1f;

        public static Font Font
        {
            get
            {
                if (_font == null)
                {
                    _font = Font.CreateDynamicFontFromOSFont(
                        new[] { "Microsoft JhengHei UI", "Microsoft JhengHei", "Microsoft YaHei UI", "Microsoft YaHei", "PingFang TC", "Noto Sans CJK TC", "Arial Unicode MS", "Arial" }, 32);
                }
                return _font;
            }
        }

        public static Sprite Rounded => _rounded != null ? _rounded : (_rounded = MakeRounded(64, 18));
        public static Sprite RoundedSmall => _roundedSmall != null ? _roundedSmall : (_roundedSmall = MakeRounded(32, 8));
        public static Sprite Circle => _circle != null ? _circle : (_circle = MakeCircle(128));
        public static Sprite Vignette => _vignette != null ? _vignette : (_vignette = MakeVignette(256));

        // ---- layout ------------------------------------------------------------------

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        /// <summary>Anchors in normalized parent space (0..1, origin bottom-left) with zero offsets.</summary>
        public static RectTransform Place(this RectTransform rt, float xMin, float yMin, float xMax, float yMax)
        {
            rt.anchorMin = new Vector2(xMin, yMin);
            rt.anchorMax = new Vector2(xMax, yMax);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>Same as <see cref="Place"/> but takes top-left based fractions, which matches the wireframes.</summary>
        public static RectTransform PlaceTL(this RectTransform rt, float left, float top, float width, float height) =>
            rt.Place(left, 1f - top - height, left + width, 1f - top);

        public static RectTransform Fill(this RectTransform rt) => rt.Place(0, 0, 1, 1);

        public static RectTransform Inset(this RectTransform rt, float l, float t, float r, float b)
        {
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(-r, -t);
            return rt;
        }

        public static T Pref<T>(this T c, float height = -1, float width = -1, float flexW = -1, float flexH = -1) where T : Component
        {
            var le = c.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
            if (height >= 0)
            {
                le.preferredHeight = height;
                le.minHeight = height;
                if (flexH < 0) le.flexibleHeight = 0; // a fixed row must not soak up spare space
            }
            if (width >= 0)
            {
                le.preferredWidth = width;
                le.minWidth = width;
                if (flexW < 0) le.flexibleWidth = 0;
            }
            if (flexW >= 0) le.flexibleWidth = flexW;
            if (flexH >= 0) le.flexibleHeight = flexH;
            return c;
        }

        public static VerticalLayoutGroup VBox(RectTransform rt, float spacing = 8, int pad = 0, TextAnchor align = TextAnchor.UpperLeft, bool expandW = true)
        {
            var g = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            g.spacing = spacing;
            g.padding = new RectOffset(pad, pad, pad, pad);
            g.childAlignment = align;
            g.childControlWidth = true;
            g.childControlHeight = true;
            g.childForceExpandWidth = expandW;
            g.childForceExpandHeight = false;
            return g;
        }

        public static HorizontalLayoutGroup HBox(RectTransform rt, float spacing = 8, int pad = 0, TextAnchor align = TextAnchor.MiddleLeft, bool expandW = false, bool expandH = true)
        {
            var g = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.spacing = spacing;
            g.padding = new RectOffset(pad, pad, pad, pad);
            g.childAlignment = align;
            g.childControlWidth = true;
            g.childControlHeight = true;
            g.childForceExpandWidth = expandW;
            g.childForceExpandHeight = expandH;
            return g;
        }

        /// <summary>Drops characters outside the BMP (emoji), which the OS dynamic font cannot draw.</summary>
        public static string NoEmoji(string s)
        {
            if (string.IsNullOrEmpty(s))
                return s;
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsHighSurrogate(c))
                {
                    i++;
                    continue;
                }
                if (c == '️' || c == '‍')
                    continue;
                sb.Append(c);
            }
            return sb.ToString();
        }

        public static GridLayoutGroup Grid(RectTransform rt, Vector2 cell, Vector2 spacing, int columns)
        {
            var g = rt.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = cell;
            g.spacing = spacing;
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = columns;
            return g;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }

        // ---- widgets -----------------------------------------------------------------

        public static Image Panel(Transform parent, string name, Color color, Sprite sprite = null, bool raycast = true)
        {
            RectTransform rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite == null ? Rounded : sprite;
            img.type = Image.Type.Sliced;
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        public static Image Block(Transform parent, string name, Color color, bool raycast = false)
        {
            RectTransform rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        public static Text Label(Transform parent, string text, int size = 22, Color? color = null, TextAnchor align = TextAnchor.UpperLeft, FontStyle style = FontStyle.Normal)
        {
            RectTransform rt = Rect(parent, "Label");
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = NoEmoji(text);
            t.fontSize = Mathf.RoundToInt(size * FontScale);
            t.color = color ?? Theme.Text;
            t.alignment = align;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.lineSpacing = 1.15f;
            return t;
        }

        /// <summary>A label that sizes its own height inside a layout group.</summary>
        public static Text FlowLabel(Transform parent, string text, int size = 22, Color? color = null, TextAnchor align = TextAnchor.UpperLeft, FontStyle style = FontStyle.Normal)
        {
            Text t = Label(parent, text, size, color, align, style);
            if (parent.GetComponent<LayoutGroup>() == null)
            {
                // Free-standing: span the parent's width from the top and grow downward.
                RectTransform rt = t.rectTransform;
                rt.anchorMin = new Vector2(0, 1);
                rt.anchorMax = new Vector2(1, 1);
                rt.pivot = new Vector2(0.5f, 1);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                var fitter = t.gameObject.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            return t;
        }

        public static Button Button(Transform parent, string label, UnityAction onClick, ButtonStyle style = ButtonStyle.Normal, int size = 22)
        {
            Color bg;
            Color fg = Theme.Text;
            switch (style)
            {
                case ButtonStyle.Primary: bg = new Color(0.24f, 0.50f, 0.86f, 0.96f); break;
                case ButtonStyle.Ghost: bg = new Color(1f, 1f, 1f, 0.06f); fg = Theme.TextDim; break;
                case ButtonStyle.Danger: bg = new Color(0.55f, 0.18f, 0.22f, 0.92f); break;
                case ButtonStyle.Menu: bg = new Color(0.06f, 0.09f, 0.18f, 0.72f); break;
                default: bg = Theme.PanelLight; break;
            }
            Image img = Panel(parent, "Button:" + label, bg);
            var button = img.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            ColorBlock cb = button.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.25f, 1.25f, 1.35f, 1f);
            cb.pressedColor = new Color(0.8f, 0.85f, 0.95f, 1f);
            cb.selectedColor = new Color(1.12f, 1.12f, 1.2f, 1f);
            cb.disabledColor = new Color(0.55f, 0.55f, 0.6f, 0.55f);
            cb.colorMultiplier = 1.4f;
            cb.fadeDuration = 0.08f;
            button.colors = cb;
            if (onClick != null)
                button.onClick.AddListener(onClick);

            Text t = Label(img.transform, label, size, fg, style == ButtonStyle.Menu ? TextAnchor.MiddleRight : TextAnchor.MiddleCenter);
            t.rectTransform.Fill().Inset(14, 4, style == ButtonStyle.Menu ? 26 : 14, 4);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            return button;
        }

        public static Text ButtonText(this Button b) => b.GetComponentInChildren<Text>();

        public static InputField Input(Transform parent, string placeholder, bool multiline = false, int size = 22)
        {
            Image bg = Panel(parent, "Input", new Color(0.02f, 0.04f, 0.09f, 0.85f), RoundedSmall);
            var input = bg.gameObject.AddComponent<InputField>();
            Text text = Label(bg.transform, string.Empty, size, Theme.Text, multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft);
            text.rectTransform.Fill().Inset(14, 6, 14, 6);
            text.supportRichText = false;
            Text ph = Label(bg.transform, placeholder, size, Theme.TextMuted, multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft, FontStyle.Italic);
            ph.rectTransform.Fill().Inset(14, 6, 14, 6);
            input.textComponent = text;
            input.placeholder = ph;
            input.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
            input.caretColor = Theme.Accent;
            input.selectionColor = new Color(0.42f, 0.74f, 1f, 0.35f);
            return input;
        }

        public static Slider Slider(Transform parent, float min, float max, float value, UnityAction<float> onChange, bool whole = false)
        {
            RectTransform root = Rect(parent, "Slider");
            var slider = root.gameObject.AddComponent<Slider>();
            Image track = Panel(root, "Track", new Color(1, 1, 1, 0.10f), RoundedSmall, true);
            track.rectTransform.Place(0, 0.38f, 1, 0.62f);
            RectTransform fillArea = Rect(root, "FillArea").Place(0, 0.38f, 1, 0.62f);
            Image fill = Panel(fillArea, "Fill", Theme.AccentDark, RoundedSmall, false);
            fill.rectTransform.Fill();
            // The slider stretches the handle across the handle area's height, so that area is a
            // zero-height line through the middle and the handle keeps its own 24×24 size.
            RectTransform handleArea = Rect(root, "HandleArea").Place(0, 0.5f, 1, 0.5f).Inset(12, 0, 12, 0);
            Image handle = Panel(handleArea, "Handle", Theme.Accent, Circle);
            handle.type = Image.Type.Simple;
            handle.rectTransform.sizeDelta = new Vector2(24, 24);
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = whole;
            slider.value = value;
            if (onChange != null)
                slider.onValueChanged.AddListener(onChange);
            return slider;
        }

        public static Toggle Toggle(Transform parent, string label, bool value, UnityAction<bool> onChange)
        {
            RectTransform root = Rect(parent, "Toggle:" + label);
            var toggle = root.gameObject.AddComponent<Toggle>();
            Image box = Panel(root, "Box", new Color(1, 1, 1, 0.10f), RoundedSmall);
            box.rectTransform.anchorMin = new Vector2(0, 0.5f);
            box.rectTransform.anchorMax = new Vector2(0, 0.5f);
            box.rectTransform.sizeDelta = new Vector2(28, 28);
            box.rectTransform.anchoredPosition = new Vector2(14, 0);
            Image check = Panel(box.transform, "Check", Theme.Accent, RoundedSmall, false);
            check.rectTransform.Fill().Inset(5, 5, 5, 5);
            Text t = Label(root, label, 22, Theme.Text, TextAnchor.MiddleLeft);
            t.rectTransform.Fill().Inset(40, 0, 0, 0);
            toggle.targetGraphic = box;
            toggle.graphic = check;
            toggle.isOn = value;
            if (onChange != null)
                toggle.onValueChanged.AddListener(onChange);
            return toggle;
        }

        /// <summary>A vertical scroll list; children go into <paramref name="content"/>.</summary>
        public static ScrollRect ScrollList(Transform parent, out RectTransform content, float spacing = 8, int pad = 8)
        {
            RectTransform root = Rect(parent, "Scroll");
            var scroll = root.gameObject.AddComponent<ScrollRect>();
            RectTransform viewport = Rect(root, "Viewport").Fill();
            var mask = viewport.gameObject.AddComponent<RectMask2D>();
            mask.padding = Vector4.zero;
            Image hit = viewport.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            content = Rect(viewport, "Content");
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            VBox(content, spacing, pad);
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30;
            return scroll;
        }

        /// <summary>Small numbered badge, the blue circle of the wireframes.</summary>
        public static Text Badge(Transform parent, string text, Color color, float size = 34)
        {
            Image c = Panel(parent, "Badge", color, Circle, false);
            c.rectTransform.sizeDelta = new Vector2(size, size);
            Text t = Label(c.transform, text, Mathf.RoundToInt(size * 0.5f), Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            t.rectTransform.Fill();
            return t;
        }

        /// <summary>Section title with a short accent bar, as in the wireframe panels.</summary>
        public static Text Header(Transform parent, string text, int size = 24)
        {
            RectTransform row = Rect(parent, "Header");
            row.Pref(size + 14);
            Image bar = Block(row, "Bar", Theme.Accent);
            bar.rectTransform.Place(0, 0.18f, 0, 0.82f);
            bar.rectTransform.sizeDelta = new Vector2(4, 0);
            bar.rectTransform.anchoredPosition = new Vector2(2, 0);
            Text t = Label(row, text, size, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            t.rectTransform.Fill().Inset(14, 0, 0, 0);
            return t;
        }

        public static Image Divider(Transform parent)
        {
            Image line = Block(parent, "Divider", Theme.Line);
            line.Pref(1);
            return line;
        }

        // ---- procedural sprites --------------------------------------------------------

        private static Sprite MakeRounded(int size, int radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(0, Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius)));
                    float dy = Mathf.Max(0, Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius)));
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(radius - d + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect,
                new Vector4(radius, radius, radius, radius));
        }

        private static Sprite MakeCircle(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float r = size / 2f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    float a = Mathf.Clamp01(r - d);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        private static Sprite MakeVignette(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size * 2f - 1f;
                    float ny = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Sqrt(nx * nx * 0.8f + ny * ny);
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.35f, d));
                    px[y * size + x] = new Color32(0, 0, 0, (byte)(a * 255));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }
    }
}
