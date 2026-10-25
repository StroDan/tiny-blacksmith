using System.Collections.Generic;
using UnityEngine;

// Small movement over the rooftops: smoke from the chimneys nearest the smithy (VillageBuilder writes every chimney
// top as a "Chimneys" marker) - thicker when the cooking fires are lit in the morning and the evening, a thin wisp by
// day, darker and lower at night. Only the closest dozen or so smoke, so it costs next to nothing.
// Built at runtime by ForgeGameManager (like CityLife); the main menu uses ChimneySmoke for its own village.
public class CityAmbience : MonoBehaviour
{
    public const int MaxChimneys = 14;
    public const float Range = 90f;

    private readonly List<(ParticleSystem ps, float share)> smokes = new List<(ParticleSystem, float)>();
    private DayCycle clock;
    private MeadowLife weather;
    private float refresh;

    public int SmokingChimneys => smokes.Count;

    public static CityAmbience Create(DayCycle clock, MeadowLife weather, Vector3 seat)
    {
        var amb = new GameObject("CityAmbience").AddComponent<CityAmbience>();
        amb.clock = clock;
        amb.weather = weather;
        var markers = GameObject.Find("Chimneys");
        if (markers == null) return amb;
        var tops = new List<Vector3>();
        foreach (Transform t in markers.transform)
            if ((t.position - seat).sqrMagnitude < Range * Range) tops.Add(t.position);
        tops.Sort((a, b) => (a - seat).sqrMagnitude.CompareTo((b - seat).sqrMagnitude));
        var mat = SmokeMaterial();
        for (int i = 0; i < tops.Count && i < MaxChimneys; i++)
        {
            var ps = ChimneySmoke(tops[i], mat);
            ps.transform.SetParent(amb.transform, true);
            var main = ps.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.6f); // seen from the seat, 20-90 m off: fuller puffs than the menu's close-ups
            amb.smokes.Add((ps, Random.Range(0.35f, 1.25f))); // some hearths burn higher than others
        }
        return amb;
    }

    private void Update()
    {
        refresh -= Time.deltaTime;
        if (refresh > 0f || clock == null) return;
        refresh = 1f;
        float h = clock.Hour;
        // Cooking fires: morning and evening; a low fire through the day; embers banked at night.
        float rate = h >= 6f && h < 9.5f ? 3.4f : h >= 17f && h < 21.5f ? 3.0f : h >= 5f && h < 21.5f ? 1.5f : 0.8f;
        if (weather != null && weather.Weather == "rain") rate *= 0.7f;
        Color c = Color.Lerp(new Color(0.52f, 0.50f, 0.49f, 0.62f), new Color(0.20f, 0.20f, 0.24f, 0.5f), clock.Night01);
        foreach (var (ps, share) in smokes)
        {
            var e = ps.emission;
            e.rateOverTime = rate * share;
            var main = ps.main;
            main.startColor = c;
        }
    }

    // One chimney's plume: faceted puffs (like the forge's smoke) rising, growing and fading, bent by a light breeze.
    private static Mesh gem;

    public static ParticleSystem ChimneySmoke(Vector3 at, Material mat = null)
    {
        var go = new GameObject("ChimneySmoke");
        go.transform.SetPositionAndRotation(at, Quaternion.Euler(-90f, 0f, 0f));
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
        main.startColor = new Color(0.55f, 0.53f, 0.52f, 0.35f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = -0.02f;
        main.maxParticles = 60;
        var emission = ps.emission;
        emission.rateOverTime = 3f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.12f;
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.4f));
        var color = ps.colorOverLifetime;
        color.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        color.color = g;
        var velocity = ps.velocityOverLifetime; // a gentle breeze bends the plume
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(0.25f);
        velocity.y = new ParticleSystem.MinMaxCurve(0f);
        velocity.z = new ParticleSystem.MinMaxCurve(0.1f);
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = mat != null ? mat : SmokeMaterial();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        if (gem == null) gem = LowPoly.Gem().mesh; // faceted puffs, like the forge's own smoke (one mesh for them all)
        renderer.mesh = gem;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        ps.Play();
        return ps;
    }

    // URP Particles/Unlit, alpha blended (Resources/ParticlesUnlit* keep the shader in builds).
    public static Material SmokeMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        var mat = new Material(shader != null ? shader : Shader.Find("Sprites/Default"));
        mat.SetFloat("_Surface", 1f); // transparent
        mat.SetFloat("_Blend", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = 3000;
        return mat;
    }
}
