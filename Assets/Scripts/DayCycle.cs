using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// How the time of day LOOKS: DAWN -> MORNING -> DAY -> EVENING -> NIGHT. It keeps no time of its own - it reads
// GameTime (the one clock) and shows GameTime.VisualHour, which eases after the clock, so an hour of work passing
// becomes a smooth change of light, never a snap. Everything visual follows from a small table of key times:
//   sun colour/strength/angle (a cool moon at night), trilight ambient, skybox tint/exposure, fog, shadow strength,
//   the forge's warm light (the main light source after dark), lantern lights, house windows, stars and a moon.
// The season/weather tint from MeadowLife is applied on top. Built in code by ForgeGameManager, like MeadowLife.
public class DayCycle : MonoBehaviour
{
    private GameTime time;

    // The hour as shown (sky, lights, the townsfolk's rhythm). Setting it moves THE clock (tests, debugging).
    public float Hour
    {
        get => time != null ? time.VisualHour : 12f;
        set { if (time != null) time.SetHour(value); }
    }
    public GameTime Clock => time;

    public bool IsNight => time.IsNight;
    public bool IsEvening => time.IsEvening;
    public float Night01 { get; private set; }  // 0 in daylight .. 1 in full night (drives lights)
    public string Phase => time.Phase;
    public string ClockText => time.ClockText;

    private struct Key
    {
        public float hour, sunIntensity, exposure, shadow;
        public Color sun, sky, equator, ground, tint, fog;
    }

    private static Color C(float r, float g, float b) => new Color(r, g, b);

    // The look at key times; everything in between is blended.
    private static readonly Key[] Keys =
    {
        new Key { hour = 0f,    sun = C(0.55f, 0.65f, 1.00f), sunIntensity = 0.22f, sky = C(0.13f, 0.16f, 0.28f), equator = C(0.09f, 0.10f, 0.17f), ground = C(0.04f, 0.04f, 0.06f), tint = C(0.16f, 0.20f, 0.38f), exposure = 0.32f, fog = C(0.06f, 0.08f, 0.14f), shadow = 0.45f },
        new Key { hour = 4.6f,  sun = C(0.55f, 0.65f, 1.00f), sunIntensity = 0.22f, sky = C(0.13f, 0.16f, 0.28f), equator = C(0.09f, 0.10f, 0.17f), ground = C(0.04f, 0.04f, 0.06f), tint = C(0.16f, 0.20f, 0.38f), exposure = 0.32f, fog = C(0.06f, 0.08f, 0.14f), shadow = 0.45f },
        new Key { hour = 6.0f,  sun = C(1.00f, 0.56f, 0.40f), sunIntensity = 0.40f, sky = C(0.40f, 0.36f, 0.48f), equator = C(0.52f, 0.36f, 0.30f), ground = C(0.13f, 0.11f, 0.10f), tint = C(0.64f, 0.44f, 0.46f), exposure = 0.72f, fog = C(0.62f, 0.47f, 0.45f), shadow = 0.6f },
        new Key { hour = 7.5f,  sun = C(1.00f, 0.88f, 0.74f), sunIntensity = 0.95f, sky = C(0.60f, 0.64f, 0.74f), equator = C(0.52f, 0.50f, 0.46f), ground = C(0.22f, 0.20f, 0.17f), tint = C(0.50f, 0.50f, 0.52f), exposure = 0.95f, fog = C(0.70f, 0.74f, 0.80f), shadow = 0.85f },
        new Key { hour = 12f,   sun = C(1.00f, 0.96f, 0.88f), sunIntensity = 1.10f, sky = C(0.66f, 0.72f, 0.82f), equator = C(0.56f, 0.57f, 0.55f), ground = C(0.26f, 0.24f, 0.20f), tint = C(0.50f, 0.50f, 0.50f), exposure = 1.00f, fog = C(0.74f, 0.80f, 0.88f), shadow = 0.9f },
        new Key { hour = 16f,   sun = C(1.00f, 0.92f, 0.80f), sunIntensity = 1.05f, sky = C(0.64f, 0.68f, 0.78f), equator = C(0.58f, 0.56f, 0.52f), ground = C(0.25f, 0.23f, 0.19f), tint = C(0.52f, 0.50f, 0.49f), exposure = 1.00f, fog = C(0.76f, 0.78f, 0.82f), shadow = 0.9f },
        new Key { hour = 18.2f, sun = C(1.00f, 0.62f, 0.36f), sunIntensity = 0.85f, sky = C(0.56f, 0.46f, 0.50f), equator = C(0.66f, 0.45f, 0.31f), ground = C(0.20f, 0.14f, 0.10f), tint = C(0.68f, 0.46f, 0.40f), exposure = 0.85f, fog = C(0.80f, 0.55f, 0.42f), shadow = 0.85f },
        new Key { hour = 19.7f, sun = C(0.95f, 0.42f, 0.30f), sunIntensity = 0.35f, sky = C(0.30f, 0.25f, 0.38f), equator = C(0.36f, 0.23f, 0.21f), ground = C(0.09f, 0.07f, 0.08f), tint = C(0.44f, 0.30f, 0.42f), exposure = 0.52f, fog = C(0.36f, 0.26f, 0.32f), shadow = 0.6f },
        new Key { hour = 21f,   sun = C(0.55f, 0.65f, 1.00f), sunIntensity = 0.22f, sky = C(0.13f, 0.16f, 0.28f), equator = C(0.09f, 0.10f, 0.17f), ground = C(0.04f, 0.04f, 0.06f), tint = C(0.16f, 0.20f, 0.38f), exposure = 0.32f, fog = C(0.06f, 0.08f, 0.14f), shadow = 0.45f },
        new Key { hour = 24f,   sun = C(0.55f, 0.65f, 1.00f), sunIntensity = 0.22f, sky = C(0.13f, 0.16f, 0.28f), equator = C(0.09f, 0.10f, 0.17f), ground = C(0.04f, 0.04f, 0.06f), tint = C(0.16f, 0.20f, 0.38f), exposure = 0.32f, fog = C(0.06f, 0.08f, 0.14f), shadow = 0.45f },
    };

    private Light sun;
    private Quaternion noonRotation;
    private Material sky;
    private MeadowLife meadow;
    private Light forgeLight;
    private readonly List<Light> lanterns = new List<Light>();
    private Renderer stars, moon;
    private Material starMat, moonMat;
    private float skyRotation;
    private float lastGlow = -1f;

    public static DayCycle Create(GameTime time, MeadowLife meadow, Vector3 forgeFire, Shader lit)
    {
        var cycle = new GameObject("DayCycle").AddComponent<DayCycle>();
        cycle.time = time;
        cycle.Build(meadow, forgeFire, lit);
        return cycle;
    }

    private void Build(MeadowLife meadowLife, Vector3 forgeFire, Shader lit)
    {
        meadow = meadowLife;
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional) { sun = l; break; }
        noonRotation = sun != null ? sun.transform.rotation : Quaternion.Euler(50f, 330f, 0f);
        if (sun != null) sun.shadows = LightShadows.Soft;

        // Our own copy of the sky, so tinting it never touches the saved material asset.
        if (RenderSettings.skybox != null)
        {
            sky = new Material(RenderSettings.skybox);
            RenderSettings.skybox = sky;
        }
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;

        // The forge: a warm light over the coals - modest by day, the heart of the workshop at night.
        forgeLight = MakeLight("ForgeLight", forgeFire, new Color(1f, 0.52f, 0.2f), 6.5f);

        // Lanterns round the village (marker points placed by the village builder) and at the smith's own door.
        var markers = GameObject.Find("LanternLights");
        if (markers != null)
            foreach (Transform t in markers.transform)
                lanterns.Add(MakeLight("Lantern", t.position, new Color(1f, 0.66f, 0.34f), 8f));

        CollectGlows();
        BuildNightSky(lit);
        SetupPostProcessing();
        Apply();
    }

    // Everything that already glows (house windows, lantern and candle flames, the coals) glows brighter after
    // dark. Per-renderer material copies (renderer.materials), so no saved material asset is ever changed.
    private readonly List<(Material mat, Color baseEmission)> glows = new List<(Material, Color)>();

    private void CollectGlows()
    {
        foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            bool emissive = false;
            foreach (var m in r.sharedMaterials)
                if (IsGlowing(m)) { emissive = true; break; }
            if (!emissive) continue;
            foreach (var m in r.materials)
            {
                if (!IsGlowing(m)) continue;
                m.EnableKeyword("_EMISSION"); // generated materials saved into the scene lose the keyword (the colour survives)
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                if (IsWindow(m)) windows.Add(NewWindow(m));
                else glows.Add((m, m.GetColor("_EmissionColor")));
            }
        }
    }

    // House windows (the warm window-glass paints of LowPoly.HouseStructure / VillageBuilder houses): dark glass by
    // day; in the evening each window group lights up at its own time, at its own brightness - and a few stay dark
    // or go dark again late at night - so the village looks lived in rather than switched on all at once.
    private class Window
    {
        public Material mat;
        public Color warm, emission;
        public bool home;              // some households are out / asleep early
        public float onHour, offHour, brightness;
        public float level = -1f;
    }

    private readonly List<Window> windows = new List<Window>();
    private static readonly Color DarkGlass = new Color(0.16f, 0.18f, 0.22f);

    private static bool IsWindow(Material m)
    {
        Color c = m.color;
        return Mathf.Abs(c.r - 1f) < 0.02f && c.g > 0.72f && c.g < 0.8f && Mathf.Abs(c.b - (c.g - 0.35f)) < 0.03f;
    }

    private static Window NewWindow(Material m)
    {
        var rng = new System.Random(m.GetHashCode() ^ 0x5eed);
        float R() => (float)rng.NextDouble();
        return new Window
        {
            mat = m,
            warm = m.color,
            emission = m.GetColor("_EmissionColor"),
            home = R() < 0.8f,
            onHour = 17.2f + R() * 2.6f,                   // between late afternoon and after sunset
            offHour = R() < 0.35f ? 22.5f + R() * 1f : 30f, // some go to bed; most stay lit
            brightness = 0.7f + R() * 0.9f,
        };
    }

    private void UpdateWindows()
    {
        foreach (var w in windows)
        {
            if (w.mat == null) continue;
            float lit = 0f;
            if (w.home)
            {
                float on = Mathf.InverseLerp(w.onHour, w.onHour + 0.4f, Hour);
                float off = Hour >= w.offHour ? 1f : 0f;
                float morningOff = Hour < 12f ? 1f - Mathf.InverseLerp(5.4f, 6.8f, Hour) : 1f; // still lit before dawn
                lit = Hour < 12f ? (Hour < 5.4f ? 1f : morningOff) : on * (1f - off);
            }
            if (Mathf.Abs(lit - w.level) < 0.01f) continue;
            w.level = lit;
            w.mat.color = Color.Lerp(DarkGlass, w.warm, lit);
            w.mat.SetColor("_EmissionColor", w.emission * (lit * w.brightness * 2.4f));
        }
    }

    // By its emission colour alone: scene-saved generated materials keep the colour but not the _EMISSION keyword.
    private static bool IsGlowing(Material m) =>
        m != null && m.HasProperty("_EmissionColor") && m.GetColor("_EmissionColor").maxColorComponent > 0.05f;

    private Light MakeLight(string name, Vector3 at, Color color, float range)
    {
        var l = new GameObject(name).AddComponent<Light>();
        l.transform.SetParent(transform);
        l.transform.position = at;
        l.type = LightType.Point;
        l.color = color;
        l.range = range;
        l.shadows = LightShadows.None; // point-light shadows are expensive and not needed for the look
        l.renderMode = LightRenderMode.ForcePixel;
        return l;
    }

    // A dome of little stars and a pale moon, only switched on after dusk. Lit-shader black + emission, so no
    // extra shader has to be included in the build.
    private void BuildNightSky(Shader lit)
    {
        var b = new MeshBuilder();
        int star = b.Paint(Color.black, 0f, 1f);
        var rng = new System.Random(9);
        for (int i = 0; i < 360; i++)
        {
            float az = (float)rng.NextDouble() * Mathf.PI * 2f;
            float el = Mathf.Asin(0.08f + 0.92f * (float)rng.NextDouble()); // above the horizon, thinning upward
            var dir = new Vector3(Mathf.Cos(el) * Mathf.Cos(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Sin(az));
            b.Ball(dir * 260f, 0.7f + (float)rng.NextDouble() * 0.9f, star);
        }
        stars = MakeSkyObject("Stars", b.Build("Stars"), lit, out starMat);

        var mb = new MeshBuilder();
        int moonPaint = mb.Paint(Color.black, 0f, 1f);
        mb.Ball(Vector3.zero, 7f, moonPaint);
        moon = MakeSkyObject("Moon", mb.Build("Moon"), lit, out moonMat);
    }

    private Renderer MakeSkyObject(string name, LowPolyModel model, Shader lit, out Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform);
        model.Attach(go, lit);
        var r = go.GetComponent<Renderer>();
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        mat = r.sharedMaterial;
        mat.EnableKeyword("_EMISSION"); // the paint starts black, so the builder didn't switch emission on
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        r.enabled = false;
        return r;
    }

    // A gentle grade for the whole game: a little bloom so fire, embers and lanterns glow, slightly richer
    // colour, a soft vignette. Values kept low - it should read as "cozy", not "filter".
    private void SetupPostProcessing()
    {
        var cam = Camera.main;
        if (cam == null) return;
        var data = cam.GetUniversalAdditionalCameraData();
        if (data != null) data.renderPostProcessing = true;

        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        var bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(1.0f);
        bloom.intensity.Override(0.55f);
        bloom.scatter.Override(0.6f);
        var tone = profile.Add<Tonemapping>(true);
        tone.mode.Override(TonemappingMode.Neutral);
        var color = profile.Add<ColorAdjustments>(true);
        color.contrast.Override(8f);
        color.saturation.Override(8f);
        var vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(0.2f);
        vignette.smoothness.Override(0.5f);

        var volume = new GameObject("Grade").AddComponent<Volume>();
        volume.transform.SetParent(transform);
        volume.isGlobal = true;
        volume.priority = 10f;
        volume.sharedProfile = profile;
    }

    private void Update()
    {
        skyRotation += Time.deltaTime * 0.25f; // clouds drift
        Apply();
    }

    private void Apply()
    {
        Key k = Sample(Hour);
        Color tint = meadow != null ? meadow.SunTint : Color.white;
        float scale = meadow != null ? meadow.SunScale : 1f;
        bool overcast = meadow != null && meadow.Overcast;
        float dim = overcast ? 0.8f : 1f;

        // How much it is night: 0 through the day, 1 from late dusk to before dawn.
        Night01 = Hour >= 12f ? Mathf.InverseLerp(18.8f, 20.8f, Hour) : 1f - Mathf.InverseLerp(4.8f, 6.6f, Hour);

        if (sun != null)
        {
            sun.color = k.sun * (Night01 > 0.5f ? Color.white : tint);
            sun.intensity = k.sunIntensity * (Night01 > 0.5f ? 1f : scale);
            sun.shadowStrength = k.shadow;
            sun.transform.rotation = Quaternion.Slerp(SunRotation(Hour), MoonRotation, Mathf.SmoothStep(0f, 1f, Night01));
        }

        RenderSettings.ambientSkyColor = k.sky * dim;
        RenderSettings.ambientEquatorColor = k.equator * dim;
        RenderSettings.ambientGroundColor = k.ground * dim;
        RenderSettings.fogColor = Color.Lerp(k.fog, k.fog * 0.8f + C(0.1f, 0.1f, 0.12f), overcast ? 0.5f : 0f);
        // A soft distance haze by day (thicker when it's grey out). It thins away as night falls - the dark does
        // that job then, and the stars and moon (far out) must not disappear into it. Aerial perspective for the
        // layered backdrop: the castle (~180 m) stays crisp, the foothills (~300 m) take a little haze, the mountains
        // (~550 m) more, the high range (~900 m) is pale and blue - each layer lighter than the one in front. On a
        // grey day the ranges vanish into it.
        RenderSettings.fogStartDistance = Mathf.Lerp(overcast ? 60f : 140f, 800f, Night01);
        RenderSettings.fogEndDistance = Mathf.Lerp(overcast ? 520f : 1400f, 2500f, Night01);
        if (sky != null)
        {
            if (sky.HasProperty("_Tint")) sky.SetColor("_Tint", k.tint * (overcast ? 0.85f : 1f));
            if (sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", k.exposure * dim);
            if (sky.HasProperty("_Rotation")) sky.SetFloat("_Rotation", Mathf.Repeat(skyRotation, 360f));
        }

        // The forge breathes; after dark it lights the whole workshop.
        float flicker = 0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 2.3f, 0.5f) + 0.05f * Mathf.Sin(Time.time * 11f);
        if (forgeLight != null) forgeLight.intensity = Mathf.Lerp(0.9f, 3.2f, Night01) * flicker;
        float lanternLevel = Mathf.InverseLerp(0.15f, 0.8f, Night01);
        foreach (var l in lanterns)
        {
            l.enabled = lanternLevel > 0.01f;
            l.intensity = 4.2f * lanternLevel * (0.92f + 0.08f * Mathf.PerlinNoise(Time.time * 3f, l.transform.position.x));
        }

        UpdateWindows();
        float glow = Mathf.Lerp(1f, 2.3f, Night01);
        if (Mathf.Abs(glow - lastGlow) > 0.01f)
        {
            lastGlow = glow;
            foreach (var g in glows) if (g.mat != null) g.mat.SetColor("_EmissionColor", g.baseEmission * glow);
        }

        float starLevel = Mathf.InverseLerp(0.85f, 1f, Night01) * (overcast ? 0.2f : 1f);
        Vector3 eye = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
        stars.enabled = starLevel > 0.01f;
        moon.enabled = starLevel > 0.01f;
        if (stars.enabled)
        {
            stars.transform.position = eye;
            starMat.SetColor("_EmissionColor", Color.white * 1.3f * starLevel);
            moon.transform.position = eye - (MoonRotation * Vector3.forward) * 230f;
            moonMat.SetColor("_EmissionColor", C(0.95f, 0.95f, 1f) * 1.6f * starLevel);
        }
    }

    // The sun climbs from the east in the morning, stands high at midday (the scene's own sun angle) and sets
    // low and long-shadowed in the evening.
    private Quaternion SunRotation(float hour)
    {
        float t = Mathf.InverseLerp(5.8f, 20f, hour);
        Vector3 noon = noonRotation.eulerAngles;
        float elevation = Mathf.Max(4f, Mathf.Sin(t * Mathf.PI) * noon.x);
        float yaw = noon.y + (t - 0.5f) * 120f;
        return Quaternion.Euler(elevation, yaw, 0f);
    }

    private static readonly Quaternion MoonRotation = Quaternion.Euler(16f, 255f, 0f); // moon low over the meadow, in the table view

    private static Key Sample(float hour)
    {
        hour = Mathf.Repeat(hour, 24f);
        for (int i = 0; i < Keys.Length - 1; i++)
        {
            if (hour > Keys[i + 1].hour) continue;
            var a = Keys[i];
            var b = Keys[i + 1];
            float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a.hour, b.hour, hour));
            return new Key
            {
                hour = hour,
                sun = Color.Lerp(a.sun, b.sun, t),
                sunIntensity = Mathf.Lerp(a.sunIntensity, b.sunIntensity, t),
                sky = Color.Lerp(a.sky, b.sky, t),
                equator = Color.Lerp(a.equator, b.equator, t),
                ground = Color.Lerp(a.ground, b.ground, t),
                tint = Color.Lerp(a.tint, b.tint, t),
                exposure = Mathf.Lerp(a.exposure, b.exposure, t),
                fog = Color.Lerp(a.fog, b.fog, t),
                shadow = Mathf.Lerp(a.shadow, b.shadow, t),
            };
        }
        return Keys[Keys.Length - 1];
    }
}
