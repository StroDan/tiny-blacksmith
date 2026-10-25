using System.Collections.Generic;
using UnityEngine;

// The meadow in front of the tent, as a window onto the world: the season (every 4 days) recolours the
// trees, grass and sunlight, and each day rolls its own weather (rain, or snow in winter). (The people walking
// about the city - past customers wearing weapons you made among them - are CityLife.)
// Built in code at runtime by ForgeGameManager, like ForgeEffects.
public class MeadowLife : MonoBehaviour
{

    private struct Tinted { public Material mat; public Color baseColor; }

    // The season and today's weather tint the sunlight; DayCycle owns the sun itself and applies these.
    public Color SunTint { get; private set; } = Color.white;
    public float SunScale { get; private set; } = 1f;
    public bool Overcast => Weather != "clear";
    private readonly List<Tinted> leaves = new List<Tinted>();
    private readonly List<Tinted> grass = new List<Tinted>();
    private Shader shader;


    public string Weather { get; private set; } = "clear";


    public static MeadowLife Create(Shader litShader)
    {
        var life = new GameObject("MeadowLife").AddComponent<MeadowLife>();
        life.Build(litShader);
        return life;
    }

    private void Build(Shader litShader)
    {
        shader = litShader;

        // Trees (LowPoly.Tree: bark + two leaf paints), including any the player copied by hand in the Editor.
        foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (r.gameObject.name.StartsWith("Tree") && IsLibraryPlant(r.sharedMaterials))
            {
                // Environment-library trees, bushes and ferns: exactly their TB_Leaf* materials (bark, birch marks and
                // flowers keep their colour).
                var shared = r.sharedMaterials;
                var mats = r.materials; // per-renderer instances - never touch the shared library materials
                for (int i = 0; i < mats.Length; i++)
                    if (shared[i] != null && shared[i].name.StartsWith("TB_Leaf")) leaves.Add(new Tinted { mat = mats[i], baseColor = mats[i].color });
            }
            else if (r.gameObject.name.StartsWith("Tree") && r.sharedMaterials.Length == 3)
            {
                var mats = r.materials; // per-renderer instances - never touch the saved scene materials
                for (int i = 1; i < mats.Length; i++) leaves.Add(new Tinted { mat = mats[i], baseColor = mats[i].color });
            }
            else if (r.gameObject.name == "MeadowGround")
            {
                var mat = r.material;
                grass.Add(new Tinted { mat = mat, baseColor = mat.color });
            }
        }

        BuildPrecipitation();
    }

    private static bool IsLibraryPlant(Material[] mats)
    {
        foreach (var m in mats) if (m != null && m.name.StartsWith("TB_Leaf")) return true;
        return false;
    }

    // Rain / snow all round the seat - the player turns a full circle, so the weather has to be there in every
    // direction (it used to be one box out over the meadow, so turning left or right showed dry sky). Four
    // boxes frame the workshop canopy: nothing falls under the roof, and particles fall straight down in world
    // space, so none drift in under it either. Each box's emission rate is scaled by its area, so the density is
    // the same everywhere; beyond ~15 m a raindrop is too small to see, so the frame stops there.
    private const float PrecipitationReach = 15f;
    private const float RainPerSquareMetre = 4f;   // per second
    private const float SnowPerSquareMetre = 0.6f; // per second (flakes live much longer)
    private readonly List<ParticleSystem> precipitation = new List<ParticleSystem>();
    private readonly List<ParticleSystemRenderer> precipitationRenderers = new List<ParticleSystemRenderer>();
    private readonly List<float> precipitationAreas = new List<float>();

    private void BuildPrecipitation()
    {
        var cam = Camera.main;
        Vector3 seat = cam != null ? cam.transform.position : new Vector3(0f, 1.7f, -3.5f);
        float xMin = seat.x - PrecipitationReach, xMax = seat.x + PrecipitationReach;
        float zMin = seat.z - PrecipitationReach, zMax = seat.z + PrecipitationReach;
        // The roof to keep dry: the canopy's footprint, a little generous.
        var canopy = GameObject.Find("TentCanopy");
        Bounds roof = canopy != null && canopy.TryGetComponent(out Renderer cr) ? cr.bounds : new Bounds(seat, new Vector3(8.5f, 5f, 13.5f));
        float rx0 = roof.min.x - 0.2f, rx1 = roof.max.x + 0.2f, rz0 = roof.min.z - 0.2f, rz1 = roof.max.z + 0.2f;

        var mat = PrecipitationMaterial();
        AddPrecipitation(mat, xMin, rx0, zMin, zMax); // west strip, full depth
        AddPrecipitation(mat, rx1, xMax, zMin, zMax); // east strip, full depth
        AddPrecipitation(mat, rx0, rx1, rz1, zMax);   // north, between them
        AddPrecipitation(mat, rx0, rx1, zMin, rz0);   // south, between them
    }

    private void AddPrecipitation(Material mat, float x0, float x1, float z0, float z1)
    {
        if (x1 - x0 < 0.5f || z1 - z0 < 0.5f) return;
        var go = new GameObject("Precipitation");
        go.transform.SetParent(transform);
        // Emits along local +Z = straight down; the box is as tall as the fall so the sky fills at once.
        go.transform.SetPositionAndRotation(new Vector3((x0 + x1) * 0.5f, 3.6f, (z0 + z1) * 0.5f), Quaternion.Euler(90f, 0f, 0f));
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        float area = (x1 - x0) * (z1 - z0);
        main.maxParticles = Mathf.CeilToInt(area * Mathf.Max(RainPerSquareMetre * 1.2f, SnowPerSquareMetre * 10f));

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(x1 - x0, z1 - z0, 7.2f); // local Y is world Z once turned to face down

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        precipitation.Add(ps);
        precipitationRenderers.Add(r);
        precipitationAreas.Add(area);
        ps.Play();
    }

    private Material PrecipitationMaterial()
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? shader);
        if (mat.HasProperty("_Surface"))
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        return mat;
    }

    // Recolours the meadow for the season and rolls today's weather.
    public void ApplyDay(int day, string season, string forceWeather = null)
    {
        Color leafTint, grassTint, sunTint;
        float sunScale;
        switch (season)
        {
            case "Summer": leafTint = new Color(0.80f, 0.95f, 0.70f); grassTint = new Color(1.05f, 1.05f, 0.80f); sunTint = new Color(1.00f, 0.95f, 0.85f); sunScale = 1.10f; break;
            case "Autumn": leafTint = new Color(3.30f, 0.95f, 0.40f); grassTint = new Color(1.35f, 1.00f, 0.75f); sunTint = new Color(1.00f, 0.82f, 0.62f); sunScale = 0.90f; break;
            case "Winter": leafTint = new Color(3.60f, 2.00f, 4.20f); grassTint = new Color(2.80f, 2.00f, 4.20f); sunTint = new Color(0.80f, 0.88f, 1.00f); sunScale = 0.75f; break;
            default:       leafTint = Color.white; grassTint = Color.white; sunTint = Color.white; sunScale = 1f; break;
        }

        float roll = Random.value;
        Weather = season == "Winter" ? (roll < 0.5f ? "snow" : "clear")
                : season == "Autumn" ? (roll < 0.45f ? "rain" : "clear")
                : (roll < 0.25f ? "rain" : "clear");
        if (day == 1) Weather = "clear"; // the very first morning is always a nice one
        if (forceWeather != null) Weather = forceWeather;

        bool wet = Weather != "clear";
        foreach (var t in leaves) t.mat.color = Clamp(t.baseColor * leafTint);
        foreach (var t in grass) t.mat.color = Clamp(t.baseColor * grassTint * (Weather == "rain" ? 0.85f : 1f));
        SunTint = sunTint;
        SunScale = sunScale * (wet ? 0.7f : 1f);
        SetPrecipitation(Weather);
    }

    private void SetPrecipitation(string weather)
    {
        for (int i = 0; i < precipitation.Count; i++)
        {
            var ps = precipitation[i];
            var r = precipitationRenderers[i];
            float area = precipitationAreas[i];
            ps.Clear();
            var main = ps.main;
            var emission = ps.emission;
            var noise = ps.noise;
            if (weather == "rain")
            {
                main.startLifetime = 1.1f;
                main.startSpeed = new ParticleSystem.MinMaxCurve(7f, 9f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.03f);
                main.startColor = new Color(0.75f, 0.82f, 0.95f, 0.55f);
                main.gravityModifier = 0f;
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.lengthScale = 1f;
                r.velocityScale = 0.05f;
                noise.enabled = false;
                emission.rateOverTime = area * RainPerSquareMetre;
            }
            else if (weather == "snow")
            {
                main.startLifetime = 9f;
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.0f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
                main.startColor = new Color(1f, 1f, 1f, 0.9f);
                main.gravityModifier = 0f;
                r.renderMode = ParticleSystemRenderMode.Billboard;
                emission.rateOverTime = area * SnowPerSquareMetre;
                noise.enabled = true;
                noise.strength = 0.4f;
                noise.frequency = 0.3f;
            }
            else
            {
                emission.rateOverTime = 0f;
            }
        }
    }

    private static Color Clamp(Color c) => new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), c.a);
}
