using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The main menu's look and its controls. Like the rest of the game's UI there are no texture files and no
// EventSystem: the art (wood-and-brass plates, the left-side shade, diamonds, the divider) is painted into small
// textures at start-up and 9-sliced, and MainMenu hit-tests the controls against the mouse itself.
public static class MenuArt
{
    public static readonly Color Parchment = new Color(0.97f, 0.90f, 0.74f);
    public static readonly Color Gold = new Color(0.95f, 0.78f, 0.44f);
    public static readonly Color WarmText = new Color(0.93f, 0.81f, 0.60f);
    public static readonly Color DimText = new Color(0.78f, 0.69f, 0.55f);
    public static readonly Color Ink = new Color(0.07f, 0.045f, 0.03f);

    private static Font font;
    private static Sprite plank, primaryPlank, card, diamond, track;
    private static Texture2D shade, rule;

    // A book-style serif reads far more "old city" than Arial; Windows always has one of these. Anywhere else the
    // built-in font stands in.
    public static Font Font
    {
        get
        {
            if (font != null) return font;
            var installed = new HashSet<string>(Font.GetOSInstalledFontNames());
            foreach (var name in new[] { "Palatino Linotype", "Book Antiqua", "Georgia", "Cambria" })
                if (installed.Contains(name)) return font = Font.CreateDynamicFontFromOSFont(name, 32);
            return font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
    }

    // The ordinary button: dark walnut, thin brass rim.
    public static Sprite Plank => plank != null ? plank : plank = Plate("MenuPlank",
        new Color(0.33f, 0.21f, 0.12f), new Color(0.19f, 0.11f, 0.06f), new Color(0.90f, 0.72f, 0.40f), new Color(0.52f, 0.36f, 0.17f), 1f, 2, 3);
    // Continue: a warmer, redder wood (the forge's colour) and a brighter rim.
    public static Sprite PrimaryPlank => primaryPlank != null ? primaryPlank : primaryPlank = Plate("MenuPlankPrimary",
        new Color(0.50f, 0.25f, 0.12f), new Color(0.29f, 0.12f, 0.06f), new Color(1.00f, 0.84f, 0.50f), new Color(0.66f, 0.45f, 0.20f), 1f, 2, 7);
    // Cards behind the save slot, the settings list and the confirmation: see-through dark with a faint rim.
    public static Sprite Card => card != null ? card : card = Plate("MenuCard",
        new Color(0.13f, 0.09f, 0.06f), new Color(0.07f, 0.05f, 0.03f), new Color(0.80f, 0.62f, 0.34f, 0.9f), new Color(0.45f, 0.31f, 0.15f, 0.9f), 0.95f, 1, 11);
    public static Sprite Track => track != null ? track : track = Plate("MenuTrack",
        new Color(0.08f, 0.06f, 0.04f), new Color(0.12f, 0.08f, 0.05f), new Color(0.62f, 0.47f, 0.25f), new Color(0.40f, 0.28f, 0.14f), 1f, 1, 13, 4, 6);
    public static Sprite Diamond => diamond != null ? diamond : diamond = MakeDiamond();

    // A 9-sliced plate. Outline, brass rim (brighter along the top - light from above), a dark groove, then the face:
    // a vertical wood gradient with faint grain that only varies with height, so slicing stretches it into long
    // boards instead of smearing it, and a soft bevel along the rim. Chamfered corners, low-poly style.
    private static Sprite Plate(string name, Color faceTop, Color faceBottom, Color rimTop, Color rimBottom,
        float faceAlpha, int rim, int seed, int chamfer = 5, int border = 14)
    {
        const int W = 64, H = 64;
        var px = new Color[W * H];
        for (int y = 0; y < H; y++)
        {
            float v = y / (float)(H - 1);
            float grain = (Mathf.PerlinNoise(seed * 3.17f, y * 0.33f) - 0.5f) * 0.16f + Mathf.Sin(y * 2.3f + seed) * 0.018f;
            for (int x = 0; x < W; x++)
            {
                float dx = Mathf.Min(x, W - 1 - x) + 0.5f, dy = Mathf.Min(y, H - 1 - y) + 0.5f;
                float d = Mathf.Min(Mathf.Min(dx, dy), (dx + dy - chamfer) * 0.7071f);
                if (d <= 0f) { px[y * W + x] = Color.clear; continue; }
                Color c;
                if (d < 1.1f) c = new Color(Ink.r, Ink.g, Ink.b, 0.9f);
                else if (d < 1.1f + rim)
                {
                    c = Color.Lerp(rimBottom, rimTop, v);
                    if (y > H - 4) c = Color.Lerp(c, Color.white, 0.18f);
                }
                else if (d < 2.1f + rim) c = new Color(Ink.r * 1.4f, Ink.g * 1.4f, Ink.b * 1.4f, 0.95f);
                else
                {
                    c = Color.Lerp(faceBottom, faceTop, v);
                    float inner = d - (2.1f + rim);
                    float bevel = inner < 3f ? Mathf.Lerp(-0.22f, 0.20f, v) * (1f - inner / 3f) : 0f;
                    float k = 1f + grain + bevel;
                    c = new Color(c.r * k, c.g * k, c.b * k, faceAlpha);
                }
                c.a *= Mathf.Clamp01(d);
                px[y * W + x] = c;
            }
        }
        var tex = NewTexture(name, W, H, px);
        return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
            new Vector4(border, border, border, border));
    }

    private static Sprite MakeDiamond()
    {
        const int S = 32;
        var px = new Color[S * S];
        float c = (S - 1) / 2f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = 15f - (Mathf.Abs(x - c) + Mathf.Abs(y - c)); // inside distance, px
                if (d <= 0f) { px[y * S + x] = Color.clear; continue; }
                Color col = d < 2f ? new Color(Ink.r, Ink.g, Ink.b, 0.85f)
                    : Color.Lerp(new Color(0.60f, 0.42f, 0.18f), new Color(1f, 0.88f, 0.58f), y / (float)S);
                col.a *= Mathf.Clamp01(d);
                px[y * S + x] = col;
            }
        return Sprite.Create(NewTexture("MenuDiamond", S, S, px), new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
    }

    // The shade down the left: darkest at the screen edge, holding for the first third, then easing out to nothing,
    // so the city shows through it and past it.
    public static Texture2D Shade
    {
        get
        {
            if (shade != null) return shade;
            const int W = 256;
            var px = new Color[W];
            for (int x = 0; x < W; x++)
            {
                float t = x / (float)(W - 1);
                float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.28f, 1f, t));
                float a = 0.93f * Mathf.Pow(fade, 1.25f) * (1f - 0.15f * t); // high: the project blends in linear space, which reads lighter
                px[x] = new Color(0.045f, 0.03f, 0.02f, a);
            }
            return shade = NewTexture("MenuShade", W, 1, px);
        }
    }

    // A thin gold rule that fades out at both ends (the divider under the subtitle).
    public static Texture2D Rule
    {
        get
        {
            if (rule != null) return rule;
            const int W = 256;
            var px = new Color[W * 3];
            for (int x = 0; x < W; x++)
            {
                float t = x / (float)(W - 1);
                float a = Mathf.SmoothStep(0f, 1f, Mathf.Min(t, 1f - t) * 3f);
                for (int y = 0; y < 3; y++) px[y * W + x] = y == 1 ? new Color(Gold.r, Gold.g, Gold.b, a * 0.9f) : new Color(0f, 0f, 0f, a * 0.35f);
            }
            return rule = NewTexture("MenuRule", W, 3, px);
        }
    }

    private static Texture2D NewTexture(string name, int w, int h, Color[] px)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    // ---- building blocks -------------------------------------------------------------------------------

    public static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    // Centred on (x, -yTop - h/2) from the top-centre of its parent - the menu column lays out top to bottom.
    public static void Place(RectTransform r, float x, float yTop, float w, float h)
    {
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 1f);
        r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = new Vector2(x, -yTop - h / 2f);
        r.sizeDelta = new Vector2(w, h);
    }

    public static void Fill(RectTransform r)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
    }

    public static Text Label(string name, Transform parent, int size, TextAnchor align, Color color, FontStyle style = FontStyle.Normal)
    {
        var t = Rect(name, parent).gameObject.AddComponent<Text>();
        t.font = Font;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = align;
        t.color = color;
        t.raycastTarget = false;
        t.supportRichText = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    // A thin dark edge and a soft drop shadow - enough to lift text off the city without a heavy black border.
    public static void Soften(Graphic g, float strength = 1f)
    {
        var o = g.gameObject.AddComponent<Outline>();
        o.effectColor = new Color(0.10f, 0.05f, 0.02f, 0.55f * strength);
        o.effectDistance = new Vector2(1f, -1f);
        var s = g.gameObject.AddComponent<Shadow>();
        s.effectColor = new Color(0f, 0f, 0f, 0.42f * strength);
        s.effectDistance = new Vector2(1.5f, -3f);
    }

    public static Image Image(string name, Transform parent, Sprite sprite, Color color, bool sliced = true)
    {
        var img = Rect(name, parent).gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.type = sliced ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }
}

// Vertical colour gradient over a Text (or any graphic), multiplied with its own colour. Must be added before
// any Outline/Shadow on the same object, so it tints the letters and not their shadows.
public class TextGradient : BaseMeshEffect
{
    public Color top = Color.white;
    public Color bottom = Color.white;

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || vh.currentVertCount == 0) return;
        var v = new UIVertex();
        float min = float.MaxValue, max = float.MinValue;
        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref v, i);
            min = Mathf.Min(min, v.position.y);
            max = Mathf.Max(max, v.position.y);
        }
        float span = Mathf.Max(0.001f, max - min);
        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref v, i);
            v.color = (Color32)((Color)v.color * Color.Lerp(bottom, top, (v.position.y - min) / span));
            vh.SetUIVertex(v, i);
        }
    }
}

// Anything on a menu screen the pointer or the keyboard can reach. "focused" is the one highlight: the mouse sets
// it by hovering, the arrow keys by stepping.
public abstract class MenuControl
{
    public RectTransform rect;
    public bool focused;
    public bool interactable = true;

    public string Name => rect.gameObject.name;
    public bool Visible => rect.gameObject.activeInHierarchy;
    public virtual bool Contains(Vector2 screen) => Visible && RectTransformUtility.RectangleContainsScreenPoint(rect, screen, null);
    public Vector2 ScreenCenter => RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));

    public virtual void Activate() { }           // click / Enter
    public virtual void Adjust(int direction) { } // Left / Right
    public virtual void PointerDown(Vector2 screen) => Activate();
    public virtual void Drag(Vector2 screen) { }
    public virtual bool Drags => false;
    public abstract void Tick(float dt);
}

// A wood-and-brass menu button: brightens and grows a touch (3%) on hover/focus, dips on click.
public class MenuButton : MenuControl
{
    public Image plate;
    public Text label;
    public System.Action onClick;
    private Image leftGem, rightGem;
    private float hoverT, pressT;
    private readonly bool primary;

    public MenuButton(string name, Transform parent, string text, int fontSize, bool primary = false, bool gems = true)
    {
        this.primary = primary;
        rect = MenuArt.Rect(name, parent);
        plate = rect.gameObject.AddComponent<Image>();
        plate.sprite = primary ? MenuArt.PrimaryPlank : MenuArt.Plank;
        plate.type = Image.Type.Sliced;
        plate.raycastTarget = false;
        var drop = rect.gameObject.AddComponent<Shadow>(); // the plate sits slightly above the scene
        drop.effectColor = new Color(0f, 0f, 0f, 0.35f);
        drop.effectDistance = new Vector2(0f, -3f);

        label = MenuArt.Label("Label", rect, fontSize, TextAnchor.MiddleCenter, MenuArt.Parchment, FontStyle.Bold);
        MenuArt.Fill(label.rectTransform);
        label.rectTransform.offsetMin = new Vector2(0f, 2f); // optical centre: caps sit a little low otherwise
        label.text = text;
        var o = label.gameObject.AddComponent<Shadow>();
        o.effectColor = new Color(0f, 0f, 0f, 0.6f);
        o.effectDistance = new Vector2(1f, -2f);

        if (gems)
        {
            leftGem = Gem("GemL", new Vector2(0f, 0.5f), 20f);
            rightGem = Gem("GemR", new Vector2(1f, 0.5f), -20f);
        }
        Tick(1f);
    }

    private Image Gem(string name, Vector2 anchor, float x)
    {
        var g = MenuArt.Image(name, rect, MenuArt.Diamond, Color.white, false);
        g.rectTransform.anchorMin = g.rectTransform.anchorMax = anchor;
        g.rectTransform.anchoredPosition = new Vector2(x, 0f);
        g.rectTransform.sizeDelta = new Vector2(11f, 11f);
        return g;
    }

    public override void Activate()
    {
        if (!interactable) return;
        pressT = 1f;
        onClick?.Invoke();
    }

    public override void Tick(float dt)
    {
        hoverT = Mathf.MoveTowards(hoverT, interactable && focused ? 1f : 0f, dt * 7f);
        pressT = Mathf.MoveTowards(pressT, 0f, dt * 5f);
        float h = Mathf.SmoothStep(0f, 1f, hoverT);
        float s = 1f + 0.03f * h - 0.035f * pressT;
        rect.localScale = new Vector3(s, s, 1f);
        float b = interactable ? Mathf.Lerp(primary ? 0.9f : 0.84f, 1f, Mathf.Max(h, pressT)) : 0.55f;
        plate.color = new Color(b, b, b, interactable ? 1f : 0.8f);
        label.color = !interactable ? new Color(0.62f, 0.56f, 0.47f)
            : Color.Lerp(primary ? new Color(1f, 0.93f, 0.78f) : MenuArt.Parchment, new Color(1f, 0.86f, 0.52f), h);
        if (leftGem != null)
        {
            var gem = new Color(1f, 1f, 1f, interactable ? Mathf.Lerp(0.55f, 1f, h) : 0.25f);
            leftGem.color = rightGem.color = gem;
            float g = 11f + 2f * h;
            leftGem.rectTransform.sizeDelta = rightGem.rectTransform.sizeDelta = new Vector2(g, g);
        }
    }
}

// Shared by the settings rows: the label on the left and a faint highlight band behind the row when focused.
public abstract class SettingRow : MenuControl
{
    protected Image highlight;
    protected Text label;
    protected float hoverT;

    protected void BuildRow(string name, Transform parent, string text)
    {
        rect = MenuArt.Rect(name, parent);
        highlight = MenuArt.Image("Highlight", rect, MenuArt.Card, new Color(1f, 0.85f, 0.5f, 0f));
        MenuArt.Fill(highlight.rectTransform);
        label = MenuArt.Label("Label", rect, 21, TextAnchor.MiddleLeft, MenuArt.Parchment);
        label.rectTransform.anchorMin = new Vector2(0f, 0f);
        label.rectTransform.anchorMax = new Vector2(0f, 1f);
        label.rectTransform.pivot = new Vector2(0f, 0.5f);
        label.rectTransform.anchoredPosition = new Vector2(18f, 0f);
        label.rectTransform.sizeDelta = new Vector2(170f, 0f);
        label.text = text;
        MenuArt.Soften(label, 0.6f);
    }

    public override void Tick(float dt)
    {
        hoverT = Mathf.MoveTowards(hoverT, focused ? 1f : 0f, dt * 7f);
        highlight.color = new Color(1f, 0.85f, 0.5f, 0.22f * hoverT);
        label.color = Color.Lerp(MenuArt.Parchment, new Color(1f, 0.86f, 0.52f), hoverT);
    }

    // A control area on the right of the row, `width` wide, `right` in from the edge.
    protected RectTransform RightArea(string name, float right, float width, float height)
    {
        var r = MenuArt.Rect(name, rect);
        r.anchorMin = r.anchorMax = new Vector2(1f, 0.5f);
        r.pivot = new Vector2(1f, 0.5f);
        r.anchoredPosition = new Vector2(-right, 0f);
        r.sizeDelta = new Vector2(width, height);
        return r;
    }
}

// Volume: a brass-rimmed groove, a gold fill, a diamond knob and the percentage. Click or drag anywhere along it;
// Left/Right step 5%.
public class SliderRow : SettingRow
{
    public float Value { get; private set; }
    public System.Action<float> onChange;
    private RectTransform track, fill, knob, hit;
    private Text valueText;

    public SliderRow(string name, Transform parent, string text, float value)
    {
        BuildRow(name, parent, text);
        hit = RightArea("SliderHit", 70f, 196f, 40f);
        var groove = MenuArt.Image("Track", hit, MenuArt.Track, Color.white);
        track = groove.rectTransform;
        track.anchorMin = new Vector2(0f, 0.5f);
        track.anchorMax = new Vector2(1f, 0.5f);
        track.sizeDelta = new Vector2(0f, 12f);
        var f = MenuArt.Image("Fill", track, null, new Color(0.93f, 0.70f, 0.34f, 0.9f), false);
        fill = f.rectTransform;
        fill.anchorMin = new Vector2(0f, 0f);
        fill.pivot = new Vector2(0f, 0.5f);
        fill.offsetMin = new Vector2(3f, 3f);
        fill.offsetMax = new Vector2(0f, -3f);
        var k = MenuArt.Image("Knob", track, MenuArt.Diamond, Color.white, false);
        knob = k.rectTransform;
        knob.sizeDelta = new Vector2(24f, 24f);
        valueText = MenuArt.Label("Value", rect, 19, TextAnchor.MiddleRight, MenuArt.WarmText);
        var vr = valueText.rectTransform;
        vr.anchorMin = vr.anchorMax = vr.pivot = new Vector2(1f, 0.5f);
        vr.anchoredPosition = new Vector2(-16f, 0f);
        vr.sizeDelta = new Vector2(50f, 30f);
        Set(value, false);
    }

    public void Set(float v, bool notify)
    {
        v = Mathf.Clamp01(v);
        bool changed = !Mathf.Approximately(v, Value);
        Value = v;
        fill.anchorMax = new Vector2(v, 1f);
        fill.offsetMax = new Vector2(v >= 0.99f ? -3f : 0f, -3f);
        knob.anchorMin = knob.anchorMax = new Vector2(v, 0.5f);
        knob.anchoredPosition = Vector2.zero;
        valueText.text = Mathf.RoundToInt(v * 100f) + "%";
        if (notify && changed) onChange?.Invoke(v);
    }

    public override bool Drags => true;
    public override void Adjust(int direction) => Set(Mathf.Round((Value + 0.05f * direction) * 20f) / 20f, true);
    public override void PointerDown(Vector2 screen) { if (RectTransformUtility.RectangleContainsScreenPoint(hit, screen, null)) Drag(screen); }

    public override void Drag(Vector2 screen)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(track, screen, null, out Vector2 local);
        Set(Mathf.InverseLerp(track.rect.xMin, track.rect.xMax, local.x), true);
    }

    public override void Tick(float dt)
    {
        base.Tick(dt);
        float s = 1f + 0.15f * hoverT;
        knob.localScale = new Vector3(s, s, 1f);
    }
}

// On/off: a small brass-rimmed box with a diamond in it when on, and the word beside it.
public class ToggleRow : SettingRow
{
    public bool On { get; private set; }
    public System.Action<bool> onChange;
    private Image check;
    private Text state;

    public ToggleRow(string name, Transform parent, string text, bool on)
    {
        BuildRow(name, parent, text);
        var box = MenuArt.Image("Box", RightArea("ToggleBox", 18f, 30f, 30f), MenuArt.Track, Color.white);
        MenuArt.Fill(box.rectTransform);
        check = MenuArt.Image("Check", box.rectTransform, MenuArt.Diamond, Color.white, false);
        check.rectTransform.sizeDelta = new Vector2(18f, 18f);
        state = MenuArt.Label("State", rect, 19, TextAnchor.MiddleRight, MenuArt.WarmText);
        var sr = state.rectTransform;
        sr.anchorMin = sr.anchorMax = sr.pivot = new Vector2(1f, 0.5f);
        sr.anchoredPosition = new Vector2(-60f, 0f);
        sr.sizeDelta = new Vector2(80f, 30f);
        Set(on, false);
    }

    public void Set(bool on, bool notify)
    {
        On = on;
        check.enabled = on;
        state.text = on ? "On" : "Off";
        if (notify) onChange?.Invoke(on);
    }

    public override void Activate() => Set(!On, true);
    public override void Adjust(int direction) => Activate();
}

// A choice stepped through with ‹ › arrows (the resolution).
public class ChoiceRow : SettingRow
{
    public int Index { get; private set; }
    public System.Action<int> onChange;
    private readonly List<string> options;
    private Text value;
    private MenuButton left, right;

    public ChoiceRow(string name, Transform parent, string text, List<string> options, int index)
    {
        this.options = options;
        BuildRow(name, parent, text);
        var area = RightArea("Choice", 14f, 226f, 36f);
        left = Arrow("Prev", area, "‹", new Vector2(0f, 0.5f), 18f);
        right = Arrow("Next", area, "›", new Vector2(1f, 0.5f), -18f);
        value = MenuArt.Label("Value", area, 20, TextAnchor.MiddleCenter, MenuArt.WarmText);
        MenuArt.Fill(value.rectTransform);
        SetIndex(index, false);
    }

    private MenuButton Arrow(string name, RectTransform parent, string glyph, Vector2 anchor, float x)
    {
        var b = new MenuButton(name, parent, glyph, 26, false, false);
        b.rect.anchorMin = b.rect.anchorMax = anchor;
        b.rect.anchoredPosition = new Vector2(x, 0f);
        b.rect.sizeDelta = new Vector2(36f, 34f);
        b.label.rectTransform.offsetMin = new Vector2(0f, 4f);
        return b;
    }

    public void SetIndex(int i, bool notify)
    {
        Index = Mathf.Clamp(i, 0, options.Count - 1);
        value.text = options[Index];
        left.interactable = Index > 0;
        right.interactable = Index < options.Count - 1;
        if (notify) onChange?.Invoke(Index);
    }

    public override void Adjust(int direction)
    {
        int next = Mathf.Clamp(Index + direction, 0, options.Count - 1);
        if (next == Index) return;
        (direction < 0 ? left : right).Activate();
        SetIndex(next, true);
    }

    public override void Activate() => Adjust(Index < options.Count - 1 ? 1 : -1);

    public override void PointerDown(Vector2 screen)
    {
        if (left.Contains(screen)) Adjust(-1);
        else if (right.Contains(screen)) Adjust(1);
    }

    public override void Tick(float dt)
    {
        base.Tick(dt);
        left.focused = focused && left.interactable;
        right.focused = focused && right.interactable;
        left.Tick(dt);
        right.Tick(dt);
    }
}
