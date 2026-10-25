using UnityEngine;

// Particle effects, all built in code (no assets). Particles are tiny faceted gems (LowPoly.Gem) to match the art style.
//   Furnace:    embers + a little smoke while the fire burns (stronger while the metal is being heated)
//   Anvil:      a burst of sparks on every hammer hit
//   Barrel:     steam rising from the water while the hot metal is dipped in (plus a burst on the splash)
//   Grindstone: lots of sparks flying off the wheel while the blade is pressed to it
public class ForgeEffects : MonoBehaviour
{
    private ParticleSystem fireSparks, pitSmoke, chimneySmoke, hammerSparks, steam, grindSparks;
    private float fireLevel = 0.3f, fireTarget = 0.3f;
    private float steamLevel;
    private bool grinding;
    private bool visible = true;

    // furnace: the Forge wrapper. pitLocal (the coal bed) and chimneyLocal (top of the chimney) are in its local
    // frame - measured from the model's geometry by ForgeGameManager.MeasureFurnace. The furnace emitters are
    // parented to it, so the fire stays in the firebox however the furnace is moved or turned; their particles
    // simulate in world space so embers already in the air drift naturally instead of being dragged along.
    public static ForgeEffects Create(Shader shader, Transform furnace, Bounds pitLocal, Vector3 chimneyLocal,
        Vector3 waterSurface, Vector3 grindContact, Vector3 wheelTangent)
    {
        var fx = new GameObject("Effects").AddComponent<ForgeEffects>();
        fx.Build(shader, furnace, pitLocal, chimneyLocal, waterSurface, grindContact, wheelTangent);
        return fx;
    }

    private void Build(Shader shader, Transform furnace, Bounds pitLocal, Vector3 chimneyLocal,
        Vector3 waterSurface, Vector3 grindContact, Vector3 wheelTangent)
    {
        Mesh gem = LowPoly.Gem().mesh;
        Material glowMat = MakeMaterial(shader, false);
        Material smokeMat = MakeMaterial(shader, true);
        Quaternion up = Quaternion.Euler(-90f, 0f, 0f); // shapes emit along local +Z, so this points them straight up

        // Emitters that belong to the furnace: turned with it, so their box shape's X/Y map onto the furnace's
        // own X/Z - i.e. the footprint of the coal bed - and parented to it.
        Quaternion furnaceUp = furnace.rotation * up;
        Vector3 pitTop = furnace.TransformPoint(new Vector3(pitLocal.center.x, pitLocal.max.y, pitLocal.center.z));
        Vector3 pitArea = new Vector3(pitLocal.size.x * 0.8f, pitLocal.size.z * 0.8f, 0.02f);

        // Embers drifting up out of the coal bed.
        fireSparks = Make("FireSparks", pitTop, furnaceUp, gem, glowMat, 0.6f, 1.6f, 0.3f, 1.0f, 0.025f, 0.055f, -0.05f,
            new Color(1f, 0.55f, 0.1f), new Color(1f, 0.85f, 0.3f));
        Shape(fireSparks, ParticleSystemShapeType.Box, pitArea, 0f);
        ShrinkOverLifetime(fireSparks);

        // Black coal smoke: a thin wisp above the coals, and a thicker plume from the chimney.
        pitSmoke = Make("PitSmoke", pitTop + Vector3.up * 0.15f, furnaceUp, gem, smokeMat, 1.2f, 2.0f, 0.3f, 0.5f, 0.08f, 0.16f, -0.02f,
            new Color(0.05f, 0.05f, 0.05f, 0.4f), new Color(0.12f, 0.11f, 0.10f, 0.32f));
        Shape(pitSmoke, ParticleSystemShapeType.Box, pitArea * 0.85f, 0f);
        GrowAndFade(pitSmoke, 0.6f, 1.8f);

        chimneySmoke = Make("ChimneySmoke", furnace.TransformPoint(chimneyLocal), furnaceUp, gem, smokeMat, 2.5f, 3.5f, 0.5f, 0.8f, 0.15f, 0.28f, -0.03f,
            new Color(0.03f, 0.03f, 0.03f, 0.45f), new Color(0.10f, 0.09f, 0.08f, 0.35f));
        Shape(chimneySmoke, ParticleSystemShapeType.Circle, Vector3.one, 0.14f);
        GrowAndFade(chimneySmoke, 0.6f, 2.2f);

        foreach (var ps in new[] { fireSparks, pitSmoke, chimneySmoke })
            ps.transform.SetParent(furnace, true);

        // Sparks flying off the hammer blow: burst only, they fall back down.
        hammerSparks = Make("HammerSparks", Vector3.zero, up, gem, glowMat, 0.3f, 0.75f, 1.2f, 3.2f, 0.03f, 0.06f, 1.4f,
            new Color(1f, 0.6f, 0.1f), new Color(1f, 0.95f, 0.5f));
        Shape(hammerSparks, ParticleSystemShapeType.Hemisphere, Vector3.one, 0.04f);
        ShrinkOverLifetime(hammerSparks);

        // Steam from the water in the barrel.
        // Thin, quick wisps (a heavy clump of opaque puffs read as a pile of snowballs on the rim).
        steam = Make("Steam", waterSurface, up, gem, smokeMat, 0.9f, 1.6f, 0.7f, 1.3f, 0.06f, 0.12f, -0.08f,
            new Color(0.95f, 0.97f, 1f, 0.28f), new Color(0.85f, 0.9f, 0.95f, 0.18f));
        Shape(steam, ParticleSystemShapeType.Circle, Vector3.one, 0.22f);
        GrowAndFade(steam, 0.8f, 2.6f);

        // Grinding: many sparks flung forward along the wheel's tangent and a bit up.
        Vector3 dir = (wheelTangent + Vector3.up * 0.25f).normalized;
        grindSparks = Make("GrindSparks", grindContact, Quaternion.LookRotation(dir), gem, glowMat, 0.25f, 0.6f, 1.5f, 3.8f, 0.022f, 0.045f, 1.6f,
            new Color(1f, 0.8f, 0.25f), new Color(1f, 0.98f, 0.7f));
        Shape(grindSparks, ParticleSystemShapeType.Cone, Vector3.one, 0.02f, 18f);
        ShrinkOverLifetime(grindSparks);
    }

    // ---- control (called by ForgeGameManager) --------------------------------------------------------

    public void SetFire(float level) => fireTarget = level;
    public void SetSteam(float level) => steamLevel = level;
    public void SetGrind(bool on) => grinding = on;

    public void HammerHit(Vector3 position)
    {
        if (!visible) return;
        hammerSparks.transform.position = position;
        hammerSparks.Emit(Random.Range(22, 34));
    }

    public void SteamBurst()
    {
        if (visible) steam.Emit(Random.Range(9, 14));
    }

    // Hides everything (result screen, where the workshop is not shown).
    public void SetVisible(bool value)
    {
        visible = value;
        if (value) return;
        foreach (var ps in new[] { fireSparks, pitSmoke, chimneySmoke, hammerSparks, steam, grindSparks })
            ps.Clear();
    }

    private void Update()
    {
        fireLevel = Mathf.MoveTowards(fireLevel, fireTarget, Time.deltaTime * 2f);
        // The furnace keeps a small idle ember/smoke rate even when not actively stoked, but all of it
        // (idle rate included) must drop to zero once hidden - otherwise embers keep spawning behind the
        // result screen's black fade, right after Clear() emptied the ones already there.
        float f = visible ? fireLevel : 0f;
        Rate(fireSparks, visible ? 3f + 40f * f : 0f);
        Rate(pitSmoke, visible ? 1.5f + 5f * f : 0f);
        Rate(chimneySmoke, visible ? 1.5f + 3f * f : 0f);
        Rate(steam, visible ? 26f * steamLevel : 0f);
        Rate(grindSparks, visible && grinding ? 160f : 0f);
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private static void Rate(ParticleSystem ps, float rate)
    {
        var e = ps.emission;
        e.rateOverTime = rate;
    }

    private ParticleSystem Make(string name, Vector3 pos, Quaternion rot, Mesh mesh, Material mat,
        float lifeMin, float lifeMax, float speedMin, float speedMax, float sizeMin, float sizeMax, float gravity, Color c0, Color c1)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform);
        go.transform.SetPositionAndRotation(pos, rot);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        main.startColor = new ParticleSystem.MinMaxGradient(c0, c1);
        main.gravityModifier = gravity;
        main.maxParticles = 400;

        var e = ps.emission;
        e.rateOverTime = 0f;

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Mesh;
        r.mesh = mesh;
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        ps.Play();
        return ps;
    }

    private static void Shape(ParticleSystem ps, ParticleSystemShapeType type, Vector3 scale, float radius, float angle = 25f)
    {
        var s = ps.shape;
        s.enabled = true;
        s.shapeType = type;
        s.scale = scale;
        s.radius = radius;
        s.angle = angle;
    }

    private static void ShrinkOverLifetime(ParticleSystem ps)
    {
        var s = ps.sizeOverLifetime;
        s.enabled = true;
        s.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
    }

    // Puffs grow while they rise and fade out (start colour alpha is the peak).
    private static void GrowAndFade(ParticleSystem ps, float from, float to)
    {
        var s = ps.sizeOverLifetime;
        s.enabled = true;
        s.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, from, 1f, to));

        var c = ps.colorOverLifetime;
        c.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
        c.color = g;
    }

    // URP particle material; the vertex colour of every particle tints it. Smoke uses alpha blending.
    private static Material MakeMaterial(Shader fallback, bool transparent)
    {
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? fallback;
        var mat = new Material(shader);
        mat.color = Color.white;
        if (transparent && mat.HasProperty("_Surface"))
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        return mat;
    }
}
