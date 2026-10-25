using System.Collections.Generic;
using UnityEngine;

// Procedural low-poly models (flat shaded, one colour per "paint"). Everything is generated in code so
// no external 3D tool is needed. Shapes are added as closed solids; their winding is fixed automatically
// (see Commit), so the shape functions don't have to care about triangle order.
//
// A model is a Mesh with one submesh per paint (plus a one-submesh copy for the see-through ghosts) and the
// list of paints, which Attach() turns into URP Lit materials.
public struct Paint
{
    public Color color;
    public float smoothness;
    public Color emission;
}

public class LowPolyModel
{
    public Mesh mesh;        // one submesh per paint
    public Mesh singleMesh;  // same geometry, one submesh (for single-material use such as the ghosts)
    public Paint[] paints;

    public Material[] CreateMaterials(Shader shader)
    {
        var mats = new Material[paints.Length];
        for (int i = 0; i < mats.Length; i++)
        {
            var m = new Material(shader);
            m.color = paints[i].color;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", paints[i].smoothness);
            if (paints[i].emission.maxColorComponent > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", paints[i].emission);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            mats[i] = m;
        }
        return mats;
    }

    // Puts the model on a GameObject (adds MeshFilter/MeshRenderer if missing).
    public void Attach(GameObject go, Shader shader)
    {
        if (!go.TryGetComponent(out MeshFilter mf)) mf = go.AddComponent<MeshFilter>();
        if (!go.TryGetComponent(out MeshRenderer mr)) mr = go.AddComponent<MeshRenderer>();
        mf.sharedMesh = mesh;
        mr.sharedMaterials = CreateMaterials(shader);
    }
}

public class MeshBuilder
{
    private readonly List<Paint> paints = new List<Paint>();
    private readonly List<Vector3[]> tris = new List<Vector3[]>();
    private readonly List<int> triPaint = new List<int>();
    private List<Vector3[]> part = new List<Vector3[]>();

    public bool IsEmpty => tris.Count == 0 && part.Count == 0;

    public int Paint(Color color, float smoothness = 0.25f, float emit = 0f)
    {
        paints.Add(new Paint { color = color, smoothness = smoothness, emission = emit > 0f ? color * emit : Color.black });
        return paints.Count - 1;
    }

    // ---- solids -------------------------------------------------------------------------------

    public void Box(Vector3 center, Vector3 size, int paint, Quaternion? rot = null)
        => Frustum(center, size.x, size.z, size.x, size.z, size.y, paint, rot);

    // Rectangular frustum along local Y: (w0 x d0) at the bottom, (w1 x d1) at the top.
    public void Frustum(Vector3 center, float w0, float d0, float w1, float d1, float height, int paint, Quaternion? rot = null)
    {
        float h = height * 0.5f;
        Vector3[] Ring(float w, float d, float y) => new[]
        {
            new Vector3(-w * 0.5f, y, -d * 0.5f), new Vector3(w * 0.5f, y, -d * 0.5f),
            new Vector3(w * 0.5f, y, d * 0.5f), new Vector3(-w * 0.5f, y, d * 0.5f),
        };
        Sweep(new[] { Ring(w0, d0, -h), Ring(w1, d1, h) }, true, true, paint, Matrix4x4.TRS(center, rot ?? Quaternion.identity, Vector3.one));
    }

    // Prism / cone along local Y with `sides` faces (radius r0 at the bottom, r1 at the top).
    public void Prism(Vector3 center, int sides, float r0, float r1, float length, int paint, Quaternion? rot = null)
    {
        var bottom = new Vector3[sides];
        var top = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = i * Mathf.PI * 2f / sides;
            bottom[i] = new Vector3(Mathf.Cos(a) * r0, -length * 0.5f, Mathf.Sin(a) * r0);
            top[i] = new Vector3(Mathf.Cos(a) * r1, length * 0.5f, Mathf.Sin(a) * r1);
        }
        Sweep(new[] { bottom, top }, r0 > 0f, r1 > 0f, paint, Matrix4x4.TRS(center, rot ?? Quaternion.identity, Vector3.one));
    }

    // Icosahedron: the classic low-poly "ball". squash scales the Y axis.
    public void Ball(Vector3 center, float radius, int paint, float squash = 1f)
    {
        float t = (1f + Mathf.Sqrt(5f)) / 2f;
        var v = new[]
        {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
        };
        for (int i = 0; i < v.Length; i++) v[i] = v[i].normalized * radius;
        int[] f =
        {
            0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1,
        };
        for (int i = 0; i < f.Length; i += 3) T(v[f[i]], v[f[i + 1]], v[f[i + 2]]);
        Commit(paint, Matrix4x4.TRS(center, Quaternion.identity, new Vector3(1f, squash, 1f)));
    }

    // Blade-like loft: the cross-section profile (unit x/y, CCW) is scaled per station by (halfWidth, halfThick)
    // and placed along Z. startApex/endApex (if not NaN) close that end to a point instead of a flat cap.
    public struct Station
    {
        public float z, hw, ht, groove;
        public Station(float z, float hw, float ht, float groove = 0f) { this.z = z; this.hw = hw; this.ht = ht; this.groove = groove; }
    }

    public void Blade(Station[] stations, int paint, float startApex = float.NaN, float endApex = float.NaN, Quaternion? rot = null)
    {
        var rings = new List<Vector3[]>();
        if (!float.IsNaN(startApex)) rings.Add(Apex(startApex));
        foreach (var s in stations) rings.Add(BladeRing(s));
        if (!float.IsNaN(endApex)) rings.Add(Apex(endApex));
        Sweep(rings.ToArray(), float.IsNaN(startApex), float.IsNaN(endApex), paint, Matrix4x4.TRS(Vector3.zero, rot ?? Quaternion.identity, Vector3.one));
    }

    private const int BladeVerts = 14;
    private static Vector3[] Apex(float z)
    {
        var ring = new Vector3[BladeVerts];
        for (int i = 0; i < ring.Length; i++) ring[i] = new Vector3(0f, 0f, z);
        return ring;
    }

    // Flat blade with bevelled edges; groove 0..1 cuts a fuller into both faces.
    private static Vector3[] BladeRing(Station s)
    {
        float g = 1f - 0.55f * s.groove;
        var p = new[]
        {
            new Vector2(1f, 0f), new Vector2(0.55f, 1f), new Vector2(0.28f, 1f), new Vector2(0.16f, g), new Vector2(-0.16f, g), new Vector2(-0.28f, 1f), new Vector2(-0.55f, 1f),
            new Vector2(-1f, 0f), new Vector2(-0.55f, -1f), new Vector2(-0.28f, -1f), new Vector2(-0.16f, -g), new Vector2(0.16f, -g), new Vector2(0.28f, -1f), new Vector2(0.55f, -1f),
        };
        var ring = new Vector3[p.Length];
        for (int i = 0; i < p.Length; i++) ring[i] = new Vector3(p[i].x * s.hw, p[i].y * s.ht, s.z);
        return ring;
    }

    // ---- internals ----------------------------------------------------------------------------

    // Connects equally sized rings with quads, optionally closing both ends with a fan around the ring centre.
    private void Sweep(Vector3[][] rings, bool capStart, bool capEnd, int paint, Matrix4x4 m)
    {
        for (int i = 0; i + 1 < rings.Length; i++)
        {
            var a = rings[i];
            var b = rings[i + 1];
            for (int j = 0; j < a.Length; j++)
            {
                int k = (j + 1) % a.Length;
                T(a[j], a[k], b[k]);
                T(a[j], b[k], b[j]);
            }
        }
        // The side quads walk the first ring forwards and the last ring backwards, so for a consistently wound
        // surface the start cap has to be wound the opposite way of the end cap.
        if (capStart) Cap(rings[0], true);
        if (capEnd) Cap(rings[rings.Length - 1], false);
        Commit(paint, m);
    }

    private void Cap(Vector3[] ring, bool reverse)
    {
        Vector3 c = Vector3.zero;
        foreach (var p in ring) c += p;
        c /= ring.Length;
        for (int j = 0; j < ring.Length; j++)
        {
            Vector3 a = ring[j], b = ring[(j + 1) % ring.Length];
            if (reverse) T(c, b, a);
            else T(c, a, b);
        }
    }

    // One open-surface triangle (terrain and the like), taken exactly as given: its front face is Cross(b - a, c - a).
    // (Solids go through Commit, which fixes their winding; an open surface has no inside to judge that by.)
    public void Triangle(Vector3 a, Vector3 b, Vector3 c, int paint)
    {
        if (Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-12f) return;
        tris.Add(new[] { a, b, c });
        triPaint.Add(paint);
    }

    private void T(Vector3 a, Vector3 b, Vector3 c)
    {
        if (Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-12f) return; // degenerate (collapsed to a point)
        part.Add(new[] { a, b, c });
    }

    // Moves the collected triangles into the model. A closed solid has positive signed volume when every
    // face points outwards, so if it is negative the whole part is wound the wrong way and gets flipped.
    private void Commit(int paint, Matrix4x4 m)
    {
        float volume = 0f;
        for (int i = 0; i < part.Count; i++)
        {
            var t = part[i];
            for (int k = 0; k < 3; k++) t[k] = m.MultiplyPoint3x4(t[k]);
            volume += Vector3.Dot(t[0], Vector3.Cross(t[1], t[2]));
        }
        // Unity's front face normal is Cross(b - a, c - a), so outward faces give a positive volume; flip when negative.
        bool flip = volume < 0f;
        foreach (var t in part)
        {
            tris.Add(flip ? new[] { t[0], t[2], t[1] } : t);
            triPaint.Add(paint);
        }
        part = new List<Vector3[]>();
    }

    public LowPolyModel Build(string name, float scale = 1f)
    {
        // Only paints that are actually used get a submesh.
        var used = new List<int>();
        for (int i = 0; i < paints.Count; i++) if (triPaint.Contains(i)) used.Add(i);

        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var perSub = new List<int>[used.Count];
        for (int i = 0; i < perSub.Length; i++) perSub[i] = new List<int>();
        var all = new List<int>();

        for (int i = 0; i < tris.Count; i++)
        {
            var t = tris[i];
            Vector3 n = Vector3.Cross(t[1] - t[0], t[2] - t[0]).normalized;
            int start = verts.Count;
            for (int k = 0; k < 3; k++) { verts.Add(t[k] * scale); normals.Add(n); }
            int sub = used.IndexOf(triPaint[i]);
            perSub[sub].AddRange(new[] { start, start + 1, start + 2 });
            all.AddRange(new[] { start, start + 1, start + 2 });
        }

        var mesh = new Mesh { name = name };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.subMeshCount = perSub.Length;
        for (int i = 0; i < perSub.Length; i++) mesh.SetTriangles(perSub[i], i);
        mesh.RecalculateBounds();

        var single = new Mesh { name = name + "_Single" };
        single.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        single.SetVertices(verts);
        single.SetNormals(normals);
        single.SetTriangles(all, 0);
        single.RecalculateBounds();

        var list = new Paint[used.Count];
        for (int i = 0; i < list.Length; i++) list[i] = paints[used[i]];
        return new LowPolyModel { mesh = mesh, singleMesh = single, paints = list };
    }
}

// The actual models of the game.
public static class LowPoly
{
    public const int PhaseCount = 5;
    // The whole sword (all forging phases and the handle parts) is authored at "full size" and shrunk by this factor,
    // so the finished sword with its handle fits into the workbench view.
    public const float SwordScale = 0.7f;
    public const float BladeHalfLength = 0.57f * SwordScale; // final blade: tip at -z, shoulder at +z, bare tang beyond it
    // Reach of the finished blade (phase 4) from its mesh origin, used to line it up with the water in the barrel.
    public const float TipOffset = 0.47f * SwordScale;     // origin to the tip (local -Z)
    public const float TangTipOffset = 0.95f * SwordScale; // origin to the far end of the bare tang (local +Z)

    // Half thickness of each forging phase (the piece is centred on Y, so this is how high its centre sits on a surface).
    private static readonly float[] halfThickness = { 0.12f, 0.07f, 0.055f, 0.047f, 0.042f };
    public static float PhaseHalfThickness(int phase) => halfThickness[Mathf.Clamp(phase, 0, PhaseCount - 1)] * SwordScale;

    // Area of the piece (half length along the anvil, half width across it) where the anvil points may appear: it grows with the piece.
    private static readonly float[] halfLength = { 0.15f, 0.22f, 0.29f, 0.35f, 0.40f };
    private static readonly float[] halfWidth = { 0.07f, 0.08f, 0.08f, 0.07f, 0.06f };
    public static float PhaseHalfLength(int phase) => halfLength[Mathf.Clamp(phase, 0, PhaseCount - 1)];
    public static float PhaseHalfWidth(int phase) => halfWidth[Mathf.Clamp(phase, 0, PhaseCount - 1)];

    // 5 phases: every 1-2 anvil hits (depending on how many hits the furnace granted) the piece moves one step on.
    public static int PhaseFor(float shapeProgress)
        => Mathf.Clamp(Mathf.FloorToInt(shapeProgress * PhaseCount + 0.001f), 0, PhaseCount - 1);

    private static Color C(float r, float g, float b) => new Color(r, g, b);

    private static readonly Quaternion AlongX = Quaternion.Euler(0f, 0f, -90f);
    private static readonly Quaternion AlongZ = Quaternion.Euler(90f, 0f, 0f);

    // ---- forging phases of the workpiece --------------------------------------------------------
    // Local axes: tip towards -Z, the tang (bare metal that the handle goes on) towards +Z, flat side up (+Y).
    private static readonly LowPolyModel[] phaseCache = new LowPolyModel[PhaseCount];

    public static LowPolyModel SwordPhase(int phase)
    {
        phase = Mathf.Clamp(phase, 0, PhaseCount - 1);
        if (phaseCache[phase] != null) return phaseCache[phase];

        var b = new MeshBuilder();
        int metal = b.Paint(Color.white, 0.55f); // recoloured at runtime with the metal / heat colour
        switch (phase)
        {
            case 0: // cast ingot: a trapezoid bar
                b.Frustum(Vector3.zero, 0.26f, 0.62f, 0.20f, 0.50f, 0.24f, metal);
                break;
            case 1: // first blows: flattened, wider and longer, still shapeless
                b.Blade(new[]
                {
                    new MeshBuilder.Station(-0.40f, 0.075f, 0.055f), new MeshBuilder.Station(-0.30f, 0.115f, 0.070f),
                    new MeshBuilder.Station(-0.05f, 0.125f, 0.075f), new MeshBuilder.Station(0.25f, 0.115f, 0.070f),
                    new MeshBuilder.Station(0.40f, 0.085f, 0.055f),
                }, metal);
                break;
            case 2: // drawn out: a blunt point at one end, a narrow stub at the other
                b.Blade(new[]
                {
                    new MeshBuilder.Station(-0.48f, 0.030f, 0.030f), new MeshBuilder.Station(-0.34f, 0.090f, 0.050f),
                    new MeshBuilder.Station(-0.14f, 0.108f, 0.055f), new MeshBuilder.Station(0.32f, 0.108f, 0.055f),
                    new MeshBuilder.Station(0.36f, 0.030f, 0.026f), new MeshBuilder.Station(0.60f, 0.030f, 0.026f),
                }, metal);
                break;
            case 3: // blade shape: real point, shoulders, thin tang
                b.Blade(new[]
                {
                    new MeshBuilder.Station(-0.44f, 0.060f, 0.036f), new MeshBuilder.Station(-0.26f, 0.096f, 0.046f),
                    new MeshBuilder.Station(0.00f, 0.105f, 0.048f), new MeshBuilder.Station(0.53f, 0.105f, 0.048f),
                    new MeshBuilder.Station(0.56f, 0.026f, 0.020f), new MeshBuilder.Station(0.72f, 0.026f, 0.020f),
                }, metal, startApex: -0.56f);
                break;
            default: // finished: sharp tip, fuller down the blade, full-length tang
                b.Blade(new[]
                {
                    new MeshBuilder.Station(-0.47f, 0.045f, 0.028f), new MeshBuilder.Station(-0.35f, 0.085f, 0.040f),
                    new MeshBuilder.Station(-0.26f, 0.099f, 0.042f, 0.6f), new MeshBuilder.Station(-0.10f, 0.100f, 0.042f, 1f),
                    new MeshBuilder.Station(0.45f, 0.100f, 0.042f, 1f), new MeshBuilder.Station(0.51f, 0.100f, 0.042f, 0.5f),
                    new MeshBuilder.Station(0.57f, 0.100f, 0.042f), new MeshBuilder.Station(0.575f, 0.024f, 0.016f),
                    new MeshBuilder.Station(0.95f, 0.024f, 0.016f), // tang: short enough that the guard+grip+pommel fully cover it
                }, metal, startApex: -0.57f);
                break;
        }
        phaseCache[phase] = b.Build("SwordPhase" + phase, SwordScale);
        return phaseCache[phase];
    }

    // ---- handle parts (built along the same axes as the old blocks: grip along X, guard along Z) --------

    public static LowPolyModel Guard() => Guard(1f);

    // k scales the whole piece (a dagger's guard is a smaller copy of the sword's).
    public static LowPolyModel Guard(float k)
    {
        var b = new MeshBuilder();
        int steel = b.Paint(C(0.62f, 0.64f, 0.68f), 0.6f);
        int dark = b.Paint(C(0.36f, 0.38f, 0.42f), 0.5f);
        b.Box(Vector3.zero, new Vector3(0.075f, 0.065f, 0.10f), dark);
        foreach (float s in new[] { -1f, 1f })
        {
            b.Frustum(new Vector3(0f, 0f, s * 0.09f), 0.060f, 0.05f, 0.046f, 0.04f, 0.08f, steel, Quaternion.Euler(s * 90f, 0f, 0f));
            // upturned quillon tip
            b.Frustum(new Vector3(0f, 0.012f, s * 0.155f), 0.046f, 0.04f, 0.022f, 0.025f, 0.05f, steel, Quaternion.Euler(s * 90f, 0f, 0f));
        }
        return b.Build("Guard", SwordScale * k);
    }

    public static LowPolyModel Grip() => Grip(0.30f, 0.030f);

    // A leather-wrapped grip of the given length and radius (the sword's is 0.30 x 0.030).
    public static LowPolyModel Grip(float length, float radius)
    {
        var b = new MeshBuilder();
        int leather = b.Paint(C(0.36f, 0.20f, 0.10f), 0.15f);
        int wrap = b.Paint(C(0.20f, 0.11f, 0.06f), 0.1f);
        float half = length * 0.5f;
        b.Prism(Vector3.zero, 8, radius, radius, length, leather, AlongX);
        b.Prism(new Vector3(-half, 0f, 0f), 8, radius * 1.27f, radius * 1.27f, 0.02f, wrap, AlongX);
        b.Prism(new Vector3(half, 0f, 0f), 8, radius * 1.27f, radius * 1.27f, 0.02f, wrap, AlongX);
        int bands = Mathf.Max(1, Mathf.RoundToInt(length / 0.05f) - 1); // 5 bands 0.05 apart on the sword, as before
        for (int i = 0; i < bands; i++)
            b.Prism(new Vector3(-half + (i + 1) * length / (bands + 1), 0f, 0f), 8, radius * 1.15f, radius * 1.15f, 0.012f, wrap, AlongX);
        return b.Build("Grip", SwordScale);
    }

    public static LowPolyModel Pommel() => Pommel(1f);

    public static LowPolyModel Pommel(float k)
    {
        var b = new MeshBuilder();
        int gold = b.Paint(C(0.88f, 0.70f, 0.22f), 0.6f);
        int dark = b.Paint(C(0.62f, 0.46f, 0.12f), 0.5f);
        b.Ball(Vector3.zero, 0.058f, gold, 0.9f);
        b.Prism(new Vector3(-0.045f, 0f, 0f), 6, 0.034f, 0.034f, 0.02f, dark, AlongX);
        return b.Build("Pommel", SwordScale * k);
    }

    // ---- the other weapon types --------------------------------------------------------------------
    // Same conventions as the sword, so every station can place any of them the same way: each forging phase
    // lies flat (+Y up) centred on Y, head/tip toward -Z, the bare metal the handle goes on toward +Z, authored
    // at full size and shrunk by SwordScale. Phase 0 (the cast ingot) is shared by all of them.
    //   Dagger - a short double-edged blade.  Sickle - a crescent curving round toward +X.
    //   Spear  - a leaf-shaped head on a socket.  Hammer - a head lying across the handle line (face +X, peen -X).
    private static readonly Dictionary<(WeaponType, int), LowPolyModel> weaponPhaseCache = new Dictionary<(WeaponType, int), LowPolyModel>();

    public static LowPolyModel WeaponPhase(WeaponType type, int phase)
    {
        phase = Mathf.Clamp(phase, 0, PhaseCount - 1);
        if (type == WeaponType.Sword || phase == 0) return SwordPhase(phase);
        if (weaponPhaseCache.TryGetValue((type, phase), out var cached) && cached.mesh != null) return cached;

        var b = new MeshBuilder();
        int metal = b.Paint(Color.white, 0.55f); // recoloured at runtime with the metal / heat colour
        switch (type)
        {
            case WeaponType.Dagger: DaggerPhase(b, metal, phase); break;
            case WeaponType.Sickle: SicklePhase(b, metal, phase); break;
            case WeaponType.Spear: SpearPhase(b, metal, phase); break;
            default: HammerPhase(b, metal, phase); break;
        }
        var model = b.Build($"{type}Phase{phase}", SwordScale);
        weaponPhaseCache[(type, phase)] = model;
        return model;
    }

    // Where the handle begins, along +Z from the piece's origin (world units) - the workbench fits the hilt
    // parts from here (WeaponInfo.hilt). The sword's is its blade's shoulder.
    public static float WeaponShoulder(WeaponType type)
    {
        switch (type)
        {
            case WeaponType.Dagger: return 0.12f * SwordScale;
            case WeaponType.Sickle: return 0.10f * SwordScale;
            case WeaponType.Spear: return 0.10f * SwordScale;
            case WeaponType.Hammer: return 0.07f * SwordScale;
            default: return BladeHalfLength;
        }
    }

    // How high a phase's centre sits above whatever it lies on. The sword keeps its hand-tuned table.
    public static float PhaseHalfThickness(WeaponType type, int phase)
        => type == WeaponType.Sword ? PhaseHalfThickness(phase) : WeaponPhase(type, phase).mesh.bounds.extents.y;

    private static MeshBuilder.Station St(float z, float hw, float ht, float groove = 0f) => new MeshBuilder.Station(z, hw, ht, groove);

    private static void DaggerPhase(MeshBuilder b, int metal, int phase)
    {
        switch (phase)
        {
            case 1:
                b.Blade(new[] { St(-0.26f, 0.065f, 0.05f), St(-0.18f, 0.10f, 0.065f), St(0.05f, 0.105f, 0.068f), St(0.20f, 0.09f, 0.06f), St(0.28f, 0.07f, 0.05f) }, metal);
                break;
            case 2:
                b.Blade(new[] { St(-0.32f, 0.03f, 0.03f), St(-0.22f, 0.08f, 0.045f), St(-0.05f, 0.09f, 0.048f), St(0.10f, 0.09f, 0.048f), St(0.14f, 0.028f, 0.024f), St(0.34f, 0.028f, 0.024f) }, metal);
                break;
            case 3:
                b.Blade(new[] { St(-0.28f, 0.05f, 0.032f), St(-0.16f, 0.078f, 0.04f), St(0.10f, 0.08f, 0.04f), St(0.12f, 0.022f, 0.017f), St(0.36f, 0.022f, 0.017f) }, metal, startApex: -0.33f);
                break;
            default:
                b.Blade(new[]
                {
                    St(-0.27f, 0.04f, 0.024f), St(-0.18f, 0.07f, 0.032f), St(-0.10f, 0.078f, 0.034f, 0.6f), St(0.07f, 0.078f, 0.034f, 0.8f),
                    St(0.12f, 0.078f, 0.034f), St(0.125f, 0.02f, 0.014f), St(0.40f, 0.02f, 0.014f),
                }, metal, startApex: -0.32f);
                break;
        }
    }

    private static void SicklePhase(MeshBuilder b, int metal, int phase)
    {
        switch (phase)
        {
            case 1:
                b.Blade(new[] { St(-0.30f, 0.06f, 0.045f), St(-0.20f, 0.085f, 0.055f), St(0.15f, 0.085f, 0.055f), St(0.30f, 0.06f, 0.045f) }, metal);
                break;
            case 2: // drawn out and starting to bend
                Arc(b, metal, 0.40f, 70f, 0.12f, 0.075f, 0.06f, 0.045f, 0.04f, 6);
                b.Box(new Vector3(0f, 0f, 0.22f), new Vector3(0.05f, 0.04f, 0.22f), metal);
                break;
            case 3: // curled round, still blunt
                Arc(b, metal, 0.23f, 165f, 0.0f, 0.07f, 0.04f, 0.035f, 0.03f, 10);
                b.Box(new Vector3(0f, 0f, 0.05f), new Vector3(0.055f, 0.034f, 0.12f), metal);
                b.Box(new Vector3(0f, 0f, 0.22f), new Vector3(0.03f, 0.02f, 0.24f), metal);
                break;
            default: // finished: a thin crescent tapering to a point, a short neck and a tang for the handle
                Arc(b, metal, 0.19f, 205f, -0.02f, 0.07f, 0.012f, 0.03f, 0.016f, 12);
                b.Box(new Vector3(0f, 0f, 0.04f), new Vector3(0.055f, 0.03f, 0.13f), metal);
                b.Box(new Vector3(0f, 0f, 0.21f), new Vector3(0.024f, 0.016f, 0.22f), metal);
                break;
        }
    }

    // A flat curved strip: starts at (0, 0, zStart) heading toward -Z and bends round toward +X, width and
    // thickness tapering from w0/t0 to w1/t1. One overlapping box per segment - plenty for this art style.
    private static void Arc(MeshBuilder b, int paint, float radius, float sweep, float zStart, float w0, float w1, float t0, float t1, int segments)
    {
        var c = new Vector3(radius, 0f, zStart);
        for (int i = 0; i < segments; i++)
        {
            float a0 = (180f + sweep * i / segments) * Mathf.Deg2Rad, a1 = (180f + sweep * (i + 1) / segments) * Mathf.Deg2Rad;
            Vector3 p0 = c + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius;
            Vector3 p1 = c + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius;
            Vector3 d = p1 - p0;
            float t = (i + 0.5f) / segments;
            b.Box((p0 + p1) * 0.5f, new Vector3(Mathf.Lerp(w0, w1, t), Mathf.Lerp(t0, t1, t), d.magnitude * 1.15f), paint, Quaternion.LookRotation(d));
        }
    }

    private static void SpearPhase(MeshBuilder b, int metal, int phase)
    {
        switch (phase)
        {
            case 1:
                b.Blade(new[] { St(-0.30f, 0.06f, 0.05f), St(-0.20f, 0.09f, 0.06f), St(0.20f, 0.09f, 0.06f), St(0.30f, 0.06f, 0.05f) }, metal);
                break;
            case 2:
                b.Blade(new[] { St(-0.34f, 0.03f, 0.03f), St(-0.22f, 0.075f, 0.045f), St(0.0f, 0.075f, 0.045f), St(0.06f, 0.04f, 0.035f), St(0.24f, 0.04f, 0.035f) }, metal);
                break;
            case 3:
                b.Blade(new[] { St(-0.28f, 0.035f, 0.024f), St(-0.14f, 0.068f, 0.03f), St(-0.04f, 0.06f, 0.03f), St(0.05f, 0.03f, 0.025f), St(0.08f, 0.036f, 0.036f), St(0.24f, 0.036f, 0.036f) }, metal, startApex: -0.32f);
                break;
            default: // leaf-shaped head, a narrow neck, and the socket the shaft goes into
                b.Blade(new[]
                {
                    St(-0.26f, 0.03f, 0.018f), St(-0.16f, 0.065f, 0.026f), St(-0.08f, 0.07f, 0.028f, 0.7f), St(0.0f, 0.05f, 0.026f, 0.4f),
                    St(0.06f, 0.024f, 0.02f), St(0.08f, 0.034f, 0.034f), St(0.24f, 0.036f, 0.036f),
                }, metal, startApex: -0.30f);
                break;
        }
    }

    private static void HammerPhase(MeshBuilder b, int metal, int phase)
    {
        switch (phase)
        {
            case 1: // the ingot beaten into a squat block
                b.Box(new Vector3(0f, 0f, 0.02f), new Vector3(0.26f, 0.20f, 0.30f), metal);
                break;
            case 2: // drawn out sideways into a bar across the handle line
                b.Box(Vector3.zero, new Vector3(0.34f, 0.17f, 0.19f), metal);
                break;
            case 3:
                b.Box(Vector3.zero, new Vector3(0.16f, 0.15f, 0.15f), metal);
                b.Prism(new Vector3(0.13f, 0f, 0f), 8, 0.075f, 0.075f, 0.12f, metal, AlongX);
                b.Box(new Vector3(-0.13f, 0f, 0f), new Vector3(0.12f, 0.11f, 0.13f), metal);
                break;
            default: // finished head: round striking face (+X), wedge-shaped peen (-X)
                b.Box(Vector3.zero, new Vector3(0.15f, 0.14f, 0.14f), metal);
                b.Prism(new Vector3(0.12f, 0f, 0f), 8, 0.07f, 0.07f, 0.10f, metal, AlongX);
                b.Prism(new Vector3(0.185f, 0f, 0f), 8, 0.078f, 0.078f, 0.03f, metal, AlongX);
                b.Frustum(new Vector3(-0.14f, 0f, 0f), 0.14f, 0.13f, 0.03f, 0.12f, 0.13f, metal, Quaternion.Euler(0f, 0f, 90f));
                break;
        }
    }

    // Handle parts for the other weapons (authored along X, like Guard/Grip/Pommel).

    // The brass collar where a sickle's blade meets its handle.
    public static LowPolyModel Ferrule()
    {
        var b = new MeshBuilder();
        int brass = b.Paint(C(0.66f, 0.52f, 0.24f), 0.55f);
        int dark = b.Paint(C(0.45f, 0.34f, 0.14f), 0.5f);
        b.Prism(Vector3.zero, 8, 0.038f, 0.038f, 0.045f, brass, AlongX);
        foreach (float s in new[] { -1f, 1f }) b.Prism(new Vector3(s * 0.022f, 0f, 0f), 8, 0.042f, 0.042f, 0.01f, dark, AlongX);
        return b.Build("Ferrule", SwordScale);
    }

    // A plain turned wooden handle (sickle), shaft (spear) or haft (hammer), with a darker ring at the far end.
    public static LowPolyModel WoodHandle(float length, float radius)
    {
        var b = new MeshBuilder();
        int wood = b.Paint(C(0.52f, 0.34f, 0.18f), 0.12f);
        int dark = b.Paint(C(0.34f, 0.21f, 0.10f), 0.1f);
        b.Prism(Vector3.zero, 8, radius, radius * 0.92f, length, wood, AlongX);
        b.Prism(new Vector3(length * 0.5f - 0.015f, 0f, 0f), 8, radius * 1.12f, radius * 1.12f, 0.02f, dark, AlongX);
        return b.Build("WoodHandle", SwordScale);
    }

    // The iron spike capping the end of a spear shaft.
    public static LowPolyModel ButtCap()
    {
        var b = new MeshBuilder();
        int iron = b.Paint(C(0.34f, 0.35f, 0.39f), 0.5f);
        b.Prism(Vector3.zero, 8, 0.037f, 0.037f, 0.035f, iron, AlongX);
        b.Prism(new Vector3(0.05f, 0f, 0f), 8, 0.034f, 0.006f, 0.07f, iron, AlongX);
        return b.Build("ButtCap", SwordScale);
    }

    // ---- tools ------------------------------------------------------------------------------------

    // Hammer swung around Z: handle along +X, the head (struck faces top and bottom) at the origin.
    public static LowPolyModel Hammer()
    {
        var b = new MeshBuilder();
        int steel = b.Paint(C(0.42f, 0.43f, 0.47f), 0.6f);
        int face = b.Paint(C(0.60f, 0.61f, 0.65f), 0.7f);
        int wood = b.Paint(C(0.45f, 0.28f, 0.12f), 0.15f);
        int leather = b.Paint(C(0.22f, 0.13f, 0.07f), 0.1f);
        b.Box(Vector3.zero, new Vector3(0.10f, 0.20f, 0.13f), steel);
        b.Frustum(new Vector3(0f, -0.13f, 0f), 0.13f, 0.16f, 0.10f, 0.13f, 0.06f, face);
        b.Frustum(new Vector3(0f, 0.13f, 0f), 0.10f, 0.13f, 0.13f, 0.16f, 0.06f, face);
        b.Prism(new Vector3(0.30f, 0f, 0f), 6, 0.022f, 0.022f, 0.52f, wood, AlongX);
        b.Prism(new Vector3(0.44f, 0f, 0f), 6, 0.027f, 0.027f, 0.20f, leather, AlongX);
        b.Ball(new Vector3(0.575f, 0f, 0f), 0.032f, wood);
        return b.Build("Hammer");
    }

    // Faceted marker gem, radius 0.5 (scaled by the marker).
    public static LowPolyModel Gem()
    {
        var b = new MeshBuilder();
        int p = b.Paint(Color.white);
        b.Ball(Vector3.zero, 0.5f, p);
        return b.Build("Gem");
    }

    // ---- workstations -------------------------------------------------------------------------------

    private static readonly Color Stone = new Color(0.46f, 0.43f, 0.41f);
    private static readonly Color StoneDark = new Color(0.30f, 0.28f, 0.27f);
    private static readonly Color Iron = new Color(0.20f, 0.21f, 0.24f);

    // Pivot in the middle of a 2.0 x 1.4 x 1.2 block: floor at y = -0.7, the rim of the fire pit at y = +0.7.
    // The metal lies on two grate bars whose top is at y = 0.625.
    public static LowPolyModel Furnace()
    {
        var b = new MeshBuilder();
        int stone = b.Paint(Stone, 0.1f);
        int light = b.Paint(new Color(0.54f, 0.50f, 0.47f), 0.1f);
        int dark = b.Paint(StoneDark, 0.1f);
        int iron = b.Paint(Iron, 0.5f);
        int mouth = b.Paint(new Color(0.05f, 0.03f, 0.03f), 0f);
        int glow = b.Paint(new Color(1f, 0.42f, 0.08f), 0f, 1.6f);
        int embers = b.Paint(new Color(0.75f, 0.14f, 0.04f), 0f, 1.1f);
        int coal = b.Paint(new Color(0.10f, 0.09f, 0.09f), 0.1f);

        b.Box(new Vector3(0f, -0.64f, 0f), new Vector3(2.2f, 0.12f, 1.4f), dark);              // plinth
        b.Box(new Vector3(0f, -0.14f, 0f), new Vector3(2.0f, 0.88f, 1.2f), stone);            // body
        // brick courses on the front and both sides
        foreach (float y in new[] { -0.42f, -0.14f, 0.14f })
        {
            foreach (float x in new[] { -0.73f, 0.73f }) b.Box(new Vector3(x, y, -0.605f), new Vector3(0.52f, 0.03f, 0.012f), dark);
            foreach (float x in new[] { -1.005f, 1.005f }) b.Box(new Vector3(x, y, 0f), new Vector3(0.012f, 0.03f, 1.2f), dark);
        }
        // fire mouth: frame, dark recess, glowing coals inside
        b.Box(new Vector3(0f, -0.14f, -0.61f), new Vector3(0.92f, 0.60f, 0.03f), dark);
        b.Frustum(new Vector3(0f, 0.19f, -0.61f), 0.92f, 0.03f, 0.60f, 0.03f, 0.12f, dark, Quaternion.identity); // crown
        b.Box(new Vector3(0f, -0.14f, -0.62f), new Vector3(0.72f, 0.46f, 0.03f), mouth);
        b.Box(new Vector3(0f, -0.28f, -0.635f), new Vector3(0.56f, 0.16f, 0.03f), glow);
        b.Box(new Vector3(0f, -0.13f, -0.635f), new Vector3(0.40f, 0.10f, 0.03f), embers);

        // fire pit walls (top of the wall = furnace top)
        b.Box(new Vector3(0f, 0.50f, -0.50f), new Vector3(2.0f, 0.40f, 0.20f), light);
        b.Box(new Vector3(0f, 0.50f, 0.50f), new Vector3(2.0f, 0.40f, 0.20f), light);
        b.Box(new Vector3(-0.90f, 0.50f, 0f), new Vector3(0.20f, 0.40f, 0.80f), light);
        b.Box(new Vector3(0.90f, 0.50f, 0f), new Vector3(0.20f, 0.40f, 0.80f), light);
        // coal bed + glowing chunks
        b.Box(new Vector3(0f, 0.425f, 0f), new Vector3(1.6f, 0.25f, 0.8f), coal);
        var chunks = new[]
        {
            new Vector3(-0.55f, 0f, -0.18f), new Vector3(-0.30f, 0f, 0.20f), new Vector3(-0.05f, 0f, -0.22f), new Vector3(0.20f, 0f, 0.12f),
            new Vector3(0.45f, 0f, -0.15f), new Vector3(0.62f, 0f, 0.22f), new Vector3(-0.68f, 0f, 0.15f), new Vector3(0.08f, 0f, 0.28f),
        };
        for (int i = 0; i < chunks.Length; i++)
            b.Ball(chunks[i] + new Vector3(0f, 0.56f, 0f), 0.07f + 0.01f * (i % 3), i % 3 == 0 ? coal : (i % 2 == 0 ? glow : embers), 0.75f);
        // grate bars the metal rests on
        foreach (float x in new[] { -0.13f, 0.13f }) b.Box(new Vector3(x, 0.60f, 0f), new Vector3(0.05f, 0.05f, 0.82f), iron);

        // back wall, hood and chimney
        b.Box(new Vector3(0f, 0.85f, 0.50f), new Vector3(2.0f, 0.30f, 0.20f), stone);
        b.Frustum(new Vector3(0f, 1.25f, 0.50f), 2.0f, 0.20f, 0.70f, 0.20f, 0.50f, stone);
        b.Box(new Vector3(0f, 1.80f, 0.50f), new Vector3(0.70f, 0.60f, 0.20f), light);
        b.Box(new Vector3(0f, 2.14f, 0.50f), new Vector3(0.86f, 0.08f, 0.32f), dark);
        return b.Build("Furnace");
    }

    // Pivot 0.5 above the floor: floor at y = -0.5, top of the face plate at y = +0.4.
    public static LowPolyModel Anvil()
    {
        var b = new MeshBuilder();
        int wood = b.Paint(C(0.42f, 0.27f, 0.14f), 0.1f);
        int woodDark = b.Paint(C(0.32f, 0.20f, 0.10f), 0.1f);
        int iron = b.Paint(C(0.24f, 0.25f, 0.29f), 0.5f);
        int steel = b.Paint(C(0.52f, 0.54f, 0.59f), 0.75f);
        int black = b.Paint(C(0.05f, 0.05f, 0.06f), 0.2f);

        b.Prism(new Vector3(0f, -0.275f, 0f), 8, 0.36f, 0.32f, 0.45f, wood);                    // stump
        b.Prism(new Vector3(0f, -0.43f, 0f), 8, 0.375f, 0.375f, 0.04f, iron);                    // iron bands
        b.Prism(new Vector3(0f, -0.13f, 0f), 8, 0.335f, 0.335f, 0.04f, iron);
        b.Box(new Vector3(-0.1f, 0.005f, 0f), new Vector3(0.78f, 0.12f, 0.44f), iron);          // foot
        b.Frustum(new Vector3(-0.1f, 0.17f, 0f), 0.34f, 0.26f, 0.44f, 0.32f, 0.22f, iron);      // waist
        b.Box(new Vector3(-0.1f, 0.33f, 0f), new Vector3(1.0f, 0.13f, 0.44f), iron);            // body under the face
        b.Box(new Vector3(-0.1f, 0.39f, 0f), new Vector3(1.0f, 0.02f, 0.44f), steel);           // polished face
        b.Box(new Vector3(-0.66f, 0.29f, 0f), new Vector3(0.24f, 0.10f, 0.40f), iron);          // heel step
        b.Frustum(new Vector3(0.66f, 0.335f, 0f), 0.13f, 0.40f, 0.04f, 0.08f, 0.52f, iron, AlongX); // horn
        b.Box(new Vector3(-0.42f, 0.4f, 0f), new Vector3(0.07f, 0.012f, 0.07f), black);         // hardy hole
        return b.Build("Anvil");
    }

    // Pivot 0.4 above the floor: floor at y = -0.4, tabletop at y = +0.4 (nothing sticks out above it).
    public static LowPolyModel Workbench()
    {
        var b = new MeshBuilder();
        int p1 = b.Paint(C(0.48f, 0.32f, 0.17f), 0.1f);
        int p2 = b.Paint(C(0.42f, 0.28f, 0.15f), 0.1f);
        int p3 = b.Paint(C(0.52f, 0.36f, 0.20f), 0.1f);
        int beam = b.Paint(C(0.30f, 0.19f, 0.10f), 0.1f);
        int iron = b.Paint(Iron, 0.5f);
        int knob = b.Paint(C(0.40f, 0.25f, 0.12f), 0.15f);

        b.Box(new Vector3(-0.40f, 0.35f, 0f), new Vector3(0.39f, 0.10f, 1.8f), p1);
        b.Box(new Vector3(0.00f, 0.35f, 0f), new Vector3(0.39f, 0.10f, 1.8f), p2);
        b.Box(new Vector3(0.40f, 0.35f, 0f), new Vector3(0.39f, 0.10f, 1.8f), p3);
        foreach (float z in new[] { -0.6f, 0.6f }) b.Box(new Vector3(0f, 0.26f, z), new Vector3(1.05f, 0.08f, 0.10f), beam);
        foreach (float x in new[] { -0.5f, 0.5f })
        {
            b.Box(new Vector3(x, 0.26f, 0f), new Vector3(0.08f, 0.08f, 1.5f), beam);
            foreach (float z in new[] { -0.8f, 0.8f }) b.Box(new Vector3(x, -0.07f, z), new Vector3(0.12f, 0.66f, 0.12f), beam);
        }
        b.Box(new Vector3(0f, -0.20f, 0f), new Vector3(1.0f, 0.05f, 1.5f), p2);                 // lower shelf
        // vise on the near long side
        b.Box(new Vector3(-0.66f, 0.27f, -0.5f), new Vector3(0.14f, 0.16f, 0.22f), iron);
        b.Box(new Vector3(-0.78f, 0.31f, -0.5f), new Vector3(0.05f, 0.10f, 0.22f), iron);
        b.Prism(new Vector3(-0.86f, 0.25f, -0.5f), 6, 0.018f, 0.018f, 0.24f, iron, AlongX);
        b.Ball(new Vector3(-0.99f, 0.25f, -0.5f), 0.032f, knob);
        return b.Build("Workbench");
    }

    // Standing wheel: axis along local Z, centre at the pivot, radius 0.45 (top edge at y = +0.45), floor at y = -0.5.
    // The scene rotates it around Y; transform.right is then the tangent at the top of the wheel.
    // Split into a static stand and a spinning wheel (see Prototype1SceneBuilder.CreateGrindstoneModel) so
    // the stone/hub/axle/crank assembly can rotate around its own Z axis independently of the wooden legs
    // while grinding - ForgeGameManager spins the "Wheel" child once the player starts it.

    // The 4 legs + 2 cross-bars. Static - never rotates.
    public static LowPolyModel GrindstoneStand()
    {
        var b = new MeshBuilder();
        int wood = b.Paint(C(0.43f, 0.28f, 0.14f), 0.1f);

        foreach (float z in new[] { -0.26f, 0.26f })
        {
            foreach (float s in new[] { -1f, 1f })
            {
                Vector3 top = new Vector3(0f, 0f, z), foot = new Vector3(s * 0.42f, -0.47f, z);
                b.Box((top + foot) * 0.5f, new Vector3(0.07f, (top - foot).magnitude + 0.04f, 0.06f), wood, Quaternion.FromToRotation(Vector3.up, top - foot));
            }
            b.Box(new Vector3(0f, -0.47f, z), new Vector3(1.0f, 0.06f, 0.08f), wood);
        }
        foreach (float s in new[] { -1f, 1f }) b.Box(new Vector3(s * 0.40f, -0.47f, 0f), new Vector3(0.06f, 0.06f, 0.56f), wood);
        return b.Build("GrindstoneStand");
    }

    // The stone/hub/axle/crank assembly - everything that spins - built around the same origin/axis as the
    // stand above, so it can sit at local (0,0,0) on a child "Wheel" object and rotate freely around local Z.
    public static LowPolyModel GrindstoneWheel()
    {
        var b = new MeshBuilder();
        int stone = b.Paint(C(0.58f, 0.55f, 0.50f), 0.05f);
        int face = b.Paint(C(0.66f, 0.63f, 0.58f), 0.05f);
        int hub = b.Paint(C(0.30f, 0.28f, 0.27f), 0.1f);
        int iron = b.Paint(Iron, 0.5f);
        int wood = b.Paint(C(0.43f, 0.28f, 0.14f), 0.1f);

        b.Prism(Vector3.zero, 12, 0.45f, 0.45f, 0.16f, stone, AlongZ);
        b.Prism(new Vector3(0f, 0f, 0.083f), 12, 0.32f, 0.32f, 0.012f, face, AlongZ);
        b.Prism(new Vector3(0f, 0f, -0.083f), 12, 0.32f, 0.32f, 0.012f, face, AlongZ);
        b.Prism(Vector3.zero, 8, 0.11f, 0.11f, 0.24f, hub, AlongZ);
        b.Prism(Vector3.zero, 6, 0.035f, 0.035f, 0.76f, iron, AlongZ);

        // crank - mounted on the axle end, so it turns with the wheel
        b.Box(new Vector3(0.09f, 0.10f, 0.34f), new Vector3(0.05f, 0.26f, 0.035f), iron, Quaternion.Euler(0f, 0f, -35f));
        b.Prism(new Vector3(0.18f, 0.24f, 0.39f), 6, 0.028f, 0.028f, 0.13f, wood, AlongZ);
        return b.Build("GrindstoneWheel");
    }

    // Wooden plank floor (top at y = 0), 14 x 14.
    public static LowPolyModel Floor()
    {
        var b = new MeshBuilder();
        int[] planks =
        {
            b.Paint(C(0.20f, 0.15f, 0.11f), 0.05f), b.Paint(C(0.17f, 0.13f, 0.09f), 0.05f), b.Paint(C(0.23f, 0.17f, 0.12f), 0.05f),
        };
        for (int i = 0; i < 14; i++)
            b.Box(new Vector3(-6.5f + i, -0.05f, 0f), new Vector3(0.98f, 0.10f, 14f), planks[i % 3]);
        return b.Build("Floor");
    }

    // ---- main room (tent, meadow, storage, customers) ----------------------------------------------

    // A flat patch of ground, top just under y = 0 so it tucks under the workshop's plank floor at the seam
    // instead of fighting it for depth. Used for the meadow (grass) and the storage nook (packed dirt).
    public static LowPolyModel Ground(Vector2 size, Color color)
    {
        var b = new MeshBuilder();
        int p = b.Paint(color, 0.05f);
        b.Box(new Vector3(0f, -0.055f, 0f), new Vector3(size.x, 0.1f, size.y), p);
        return b.Build("Ground");
    }

    // Canvas canopy over the player's table plus its 4 corner poles - two of them swing into view at the
    // edges of the screen when the player turns fully toward the forge or the storage side.
    public static LowPolyModel TentCanopy(float width, float depth, float wallHeight, float peakHeight)
    {
        var b = new MeshBuilder();
        int cloth = b.Paint(C(0.58f, 0.44f, 0.23f), 0.15f);
        int trim = b.Paint(C(0.42f, 0.30f, 0.14f), 0.15f);
        int wood = b.Paint(C(0.36f, 0.24f, 0.12f), 0.1f);

        b.Frustum(new Vector3(0f, (wallHeight + peakHeight) * 0.5f, 0f), width, depth, width * 0.12f, depth * 0.12f,
            peakHeight - wallHeight, cloth);
        b.Box(new Vector3(0f, wallHeight, 0f), new Vector3(width + 0.12f, 0.06f, depth + 0.12f), trim); // eaves

        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
                b.Prism(new Vector3(sx * width * 0.48f, wallHeight * 0.5f, sz * depth * 0.48f), 6, 0.05f, 0.045f, wallHeight, wood);

        return b.Build("TentCanopy");
    }

    // A large handmade wooden weapon rack: two splayed posts, two hand-hewn rails carrying forked pegs
    // (2 rows of 4 - matches ForgeGameManager's 8 sword slots, col = i % 4, row = i / 4) and a lower shelf
    // board for the material bins. Everything sticks out along local +Z (the side the pegs/swords face),
    // so it can be dropped in facing the camera with no extra rotation. Peg/bin anchor points are added as
    // plain child transforms by Prototype1SceneBuilder (see StorageDisplay) using these same coordinates -
    // keep the two in sync if the layout here changes.
    // Rack layout constants shared with Prototype1SceneBuilder (which builds matching peg/bin anchor
    // transforms) - keep both in sync if these change.
    public const float RackUpperRailY = 1.55f;
    public const float RackLowerRailY = 0.88f;
    public const float RackPegXStart = -1.05f;
    public const float RackPegColStep = 0.70f;
    // The lower row sits half a column over: swords hang blade-down, and a sword (DisplaySwordLength) is longer
    // than the gap between the rails - staggered, the upper blades hang down between the lower hilts.
    public const float RackLowerRowOffset = 0.35f;
    public static float RackPegX(int row, int col) => RackPegXStart + col * RackPegColStep + (row == 1 ? RackLowerRowOffset : 0f);
    // How high above its peg anchor (the rail centre) a hanging sword's guard rests - on top of the forked prongs.
    public const float RackGuardRestY = 0.13f;

    public static LowPolyModel StorageRack()
    {
        var b = new MeshBuilder();
        int post = b.Paint(C(0.32f, 0.21f, 0.12f), 0.06f);
        int rail = b.Paint(C(0.42f, 0.29f, 0.16f), 0.10f);
        int peg = b.Paint(C(0.28f, 0.18f, 0.10f), 0.06f);
        int band = b.Paint(C(0.23f, 0.22f, 0.21f), 0.35f);

        // Side posts - deliberately not quite vertical or matched, for a handmade look.
        b.Box(new Vector3(-1.68f, 1.00f, 0f), new Vector3(0.17f, 2.05f, 0.17f), post, Quaternion.Euler(0f, 0f, 2.5f));
        b.Box(new Vector3(1.63f, 0.95f, -0.02f), new Vector3(0.16f, 1.95f, 0.16f), post, Quaternion.Euler(0f, 0f, -1.6f));
        // A diagonal brace for extra "built by hand" character.
        b.Box(new Vector3(-1.28f, 0.35f, 0.06f), new Vector3(0.09f, 0.78f, 0.09f), post, Quaternion.Euler(0f, 0f, 32f));

        // Two rails, each slightly off-level.
        b.Box(new Vector3(0f, RackUpperRailY, 0.03f), new Vector3(3.55f, 0.15f, 0.17f), rail, Quaternion.Euler(0f, 0f, 1.1f));
        b.Box(new Vector3(0f, RackLowerRailY, -0.02f), new Vector3(3.42f, 0.15f, 0.17f), rail, Quaternion.Euler(0f, 0f, -0.8f));
        // Lower shelf board for the material bins.
        b.Box(new Vector3(0f, 0.34f, 0.35f), new Vector3(3.7f, 0.08f, 0.5f), rail, Quaternion.Euler(0f, 0f, 0.5f));
        // Rough angled feet under the shelf.
        foreach (float x in new[] { -1.5f, 1.5f })
            b.Box(new Vector3(x, 0.15f, 0.35f), new Vector3(0.13f, 0.32f, 0.42f), post, Quaternion.Euler(4f, 0f, 0f));

        // Iron collar bands where the rails meet the posts.
        foreach (float x in new[] { -1.68f, 1.63f })
            foreach (float y in new[] { RackUpperRailY, RackLowerRailY })
                b.Box(new Vector3(x, y, 0.03f), new Vector3(0.23f, 0.10f, 0.23f), band);

        // Forked pegs cradling each sword slot - two short prongs angled apart.
        for (int row = 0; row < 2; row++)
        {
            float y = row == 0 ? RackUpperRailY : RackLowerRailY;
            for (int col = 0; col < 4; col++)
            {
                float x = RackPegX(row, col);
                b.Prism(new Vector3(x - 0.055f, y + 0.10f, 0.14f), 6, 0.022f, 0.014f, 0.11f, peg, Quaternion.Euler(58f, 0f, 12f));
                b.Prism(new Vector3(x + 0.055f, y + 0.10f, 0.14f), 6, 0.022f, 0.014f, 0.11f, peg, Quaternion.Euler(58f, 0f, -12f));
            }
        }

        return b.Build("StorageRack");
    }

    // A chunky open-topped wooden crate with a mound of a coloured material heaped inside - low-poly stand-in
    // for a specific metal (Copper/Iron/Steel) so the storage rack's bins are recognisable at a glance.
    public static LowPolyModel MaterialBin(Color fill, int seed)
    {
        var b = new MeshBuilder();
        int wood = b.Paint(C(0.44f, 0.30f, 0.16f), 0.10f);
        int rim = b.Paint(C(0.27f, 0.17f, 0.09f), 0.10f);
        int mat = b.Paint(fill, 0.4f);

        float w = 0.50f, d = 0.50f, h = 0.36f, wall = 0.05f;
        b.Box(new Vector3(0f, wall * 0.5f, 0f), new Vector3(w, wall, d), wood);
        b.Box(new Vector3(0f, h * 0.5f, -d * 0.5f + wall * 0.5f), new Vector3(w, h, wall), wood);
        b.Box(new Vector3(0f, h * 0.5f, d * 0.5f - wall * 0.5f), new Vector3(w, h, wall), wood);
        b.Box(new Vector3(-w * 0.5f + wall * 0.5f, h * 0.5f, 0f), new Vector3(wall, h, d - 2f * wall), wood);
        b.Box(new Vector3(w * 0.5f - wall * 0.5f, h * 0.5f, 0f), new Vector3(wall, h, d - 2f * wall), wood);
        b.Box(new Vector3(0f, h - 0.015f, 0f), new Vector3(w + 0.05f, 0.05f, d + 0.05f), rim);

        var rng = new System.Random(seed);
        for (int i = 0; i < 6; i++)
        {
            Vector3 p = new Vector3(
                ((float)rng.NextDouble() - 0.5f) * w * 0.5f,
                h - 0.04f + (float)rng.NextDouble() * 0.09f,
                ((float)rng.NextDouble() - 0.5f) * d * 0.5f);
            b.Ball(p, 0.085f + (float)rng.NextDouble() * 0.03f, mat, 0.7f);
        }

        return b.Build("MaterialBin" + seed);
    }

    // A single static, merged mesh of a finished sword (blade + guard + grip + pommel) for display on the
    // storage rack - separate from the interactive workbench parts, which are dragged loose and can't be
    // merged into one mesh. Built tip-up along local +Y with the pommel at the local origin (whoever shows it
    // turns it: the storage rack hangs it upside down by the guard, customers wear it at the hip); the blade
    // paint (submesh 0) is meant to be recoloured per instance at runtime to match the sword's forged material.
    // The layout numbers below (model units before DisplaySwordScale) are shared with those callers.
    private const float DisplaySwordGuardLocal = 0.40f;
    private const float DisplaySwordTipLocal = DisplaySwordGuardLocal + 0.98f;
    public const float DisplaySwordScale = SwordScale * 0.85f; // a touch smaller than the gameplay sword
    public const float DisplaySwordGuardY = DisplaySwordGuardLocal * DisplaySwordScale; // pommel to guard, world units
    public const float DisplaySwordLength = DisplaySwordTipLocal * DisplaySwordScale;   // pommel to tip, world units

    public static LowPolyModel DisplaySword()
    {
        var b = new MeshBuilder();
        int metal = b.Paint(C(0.75f, 0.78f, 0.82f), 0.55f); // recoloured at runtime per metal
        int steel = b.Paint(C(0.58f, 0.60f, 0.64f), 0.55f);
        int dark = b.Paint(C(0.32f, 0.34f, 0.38f), 0.45f);
        int leather = b.Paint(C(0.34f, 0.19f, 0.10f), 0.15f);
        int wrap = b.Paint(C(0.18f, 0.10f, 0.05f), 0.10f);
        int gold = b.Paint(C(0.78f, 0.62f, 0.20f), 0.55f);

        const float guardY = DisplaySwordGuardLocal;
        // Local blade axis (Station.z, increasing away from the guard) maps straight onto +Y with this
        // rotation, so stations can just be authored as "height above the guard".
        Quaternion up = Quaternion.Euler(-90f, 0f, 0f);
        b.Blade(new[]
        {
            new MeshBuilder.Station(guardY + 0.02f, 0.070f, 0.030f, 0.5f),
            new MeshBuilder.Station(guardY + 0.12f, 0.080f, 0.036f, 1f),
            new MeshBuilder.Station(guardY + 0.74f, 0.080f, 0.036f, 1f),
            new MeshBuilder.Station(guardY + 0.83f, 0.080f, 0.036f, 0.5f),
            new MeshBuilder.Station(guardY + 0.90f, 0.030f, 0.017f),
        }, metal, endApex: DisplaySwordTipLocal, rot: up);

        // Pommel.
        b.Ball(new Vector3(0f, 0.06f, 0f), 0.055f, gold, 0.85f);
        b.Prism(new Vector3(0f, 0.115f, 0f), 6, 0.032f, 0.032f, 0.03f, dark);
        // Grip, with a few wrap bands for texture.
        b.Prism(new Vector3(0f, 0.24f, 0f), 8, 0.030f, 0.030f, 0.26f, leather);
        for (int i = -2; i <= 2; i++)
            b.Prism(new Vector3(0f, 0.12f + (i + 2) * 0.052f, 0f), 8, 0.0335f, 0.0335f, 0.012f, wrap);
        // Crossguard, with small down-turned quillon tips.
        b.Box(new Vector3(0f, guardY, 0f), new Vector3(0.30f, 0.05f, 0.07f), steel);
        foreach (float s in new[] { -1f, 1f })
            b.Frustum(new Vector3(s * 0.16f, guardY - 0.02f, 0f), 0.045f, 0.05f, 0.020f, 0.024f, 0.06f, steel, Quaternion.Euler(0f, 0f, s * 70f));

        return b.Build("DisplaySword", DisplaySwordScale);
    }

    // Simple flat-shaded tree: a trunk and a few squashed foliage balls. Feet at y = 0.
    public static LowPolyModel Tree()
    {
        var b = new MeshBuilder();
        int bark = b.Paint(C(0.34f, 0.22f, 0.11f), 0.1f);
        int leafA = b.Paint(C(0.22f, 0.42f, 0.17f), 0.15f);
        int leafB = b.Paint(C(0.28f, 0.50f, 0.20f), 0.15f);

        b.Prism(new Vector3(0f, 0.9f, 0f), 7, 0.11f, 0.07f, 1.8f, bark);
        b.Ball(new Vector3(0f, 2.0f, 0f), 0.55f, leafA, 0.8f);
        b.Ball(new Vector3(0.35f, 1.75f, 0.15f), 0.42f, leafB, 0.85f);
        b.Ball(new Vector3(-0.32f, 1.7f, -0.22f), 0.40f, leafA, 0.85f);

        return b.Build("Tree");
    }

    // A simple standing customer, feet at y = 0, about 1.7 tall. The default outfit is the original one;
    // ForgeGameManager dresses each visitor by job (CustomerArchetype.outfit) so they read as different people.
    public static LowPolyModel Customer() => Customer(C(0.55f, 0.20f, 0.16f), C(0.25f, 0.17f, 0.10f));

    // robe = a long gown down to the floor instead of trousers (the Collector of Cursed Blades).
    public static LowPolyModel Customer(Color outfit, Color hairColor, bool robe = false)
    {
        var b = new MeshBuilder();
        int shirt = b.Paint(outfit, 0.15f);
        int skin = b.Paint(C(0.85f, 0.68f, 0.55f), 0.1f);
        int hair = b.Paint(hairColor, 0.1f);

        if (robe)
        {
            b.Frustum(new Vector3(0f, 0.45f, 0f), 0.62f, 0.40f, 0.42f, 0.24f, 0.9f, shirt);
        }
        else
        {
            int pants = b.Paint(C(0.30f, 0.28f, 0.34f), 0.1f);
            foreach (float x in new[] { -0.11f, 0.11f })
                b.Prism(new Vector3(x, 0.45f, 0f), 6, 0.09f, 0.08f, 0.9f, pants);
        }
        b.Box(new Vector3(0f, 1.15f, 0f), new Vector3(0.42f, 0.5f, 0.22f), shirt);
        foreach (float x in new[] { -0.27f, 0.27f })
            b.Prism(new Vector3(x, 1.10f, 0f), 6, 0.06f, 0.05f, 0.42f, shirt, Quaternion.Euler(0f, 0f, x > 0f ? -8f : 8f));
        b.Ball(new Vector3(0f, 1.55f, 0f), 0.14f, skin, 0.95f);
        b.Ball(new Vector3(0f, 1.66f, -0.01f), 0.145f, hair, 0.6f);

        return b.Build("Customer");
    }

    // The same customer for any race (they face +Z, feet at y = 0). Humans are the model above, unchanged.
    //   Elf   - taller and slimmer, long hair, pointed ears, a light travelling cloak.
    //   Dwarf - short and broad, a big beard in their hair colour, a round nose, an iron cap and a belt.
    public static LowPolyModel Customer(Color outfit, Color hairColor, bool robe, Race race)
    {
        if (race == Race.Human || robe) return Customer(outfit, hairColor, robe);

        var b = new MeshBuilder();
        int shirt = b.Paint(outfit, 0.15f);
        int skin = b.Paint(race == Race.Elf ? C(0.92f, 0.80f, 0.68f) : C(0.84f, 0.62f, 0.50f), 0.1f);
        int hair = b.Paint(hairColor, 0.1f);
        int pants = b.Paint(C(0.30f, 0.28f, 0.34f), 0.1f);

        if (race == Race.Elf)
        {
            int cloak = b.Paint(Color.Lerp(outfit, C(0.20f, 0.30f, 0.22f), 0.55f), 0.12f);
            foreach (float x in new[] { -0.10f, 0.10f })
                b.Prism(new Vector3(x, 0.49f, 0f), 6, 0.075f, 0.065f, 0.98f, pants);
            b.Frustum(new Vector3(0f, 1.23f, 0f), 0.34f, 0.19f, 0.40f, 0.21f, 0.52f, shirt);
            foreach (float x in new[] { -0.25f, 0.25f })
                b.Prism(new Vector3(x, 1.18f, 0f), 6, 0.05f, 0.042f, 0.46f, shirt, Quaternion.Euler(0f, 0f, x > 0f ? -7f : 7f));
            b.Frustum(new Vector3(0f, 1.0f, -0.13f), 0.52f, 0.05f, 0.36f, 0.04f, 0.98f, cloak); // cloak down the back
            b.Ball(new Vector3(0f, 1.65f, 0f), 0.13f, skin, 1.0f);
            b.Ball(new Vector3(0f, 1.75f, -0.015f), 0.138f, hair, 0.6f);
            b.Box(new Vector3(0f, 1.56f, -0.1f), new Vector3(0.25f, 0.34f, 0.06f), hair); // long hair down the back
            foreach (float s in new[] { -1f, 1f }) // pointed ears, swept up and back
                b.Frustum(new Vector3(s * 0.15f, 1.68f, -0.01f), 0.05f, 0.03f, 0.008f, 0.012f, 0.16f, skin, Quaternion.Euler(-18f, 0f, -s * 58f));
        }
        else
        {
            int iron = b.Paint(C(0.42f, 0.43f, 0.47f), 0.45f);
            int belt = b.Paint(C(0.26f, 0.16f, 0.08f), 0.15f);
            int buckle = b.Paint(C(0.80f, 0.64f, 0.24f), 0.55f);
            foreach (float x in new[] { -0.13f, 0.13f })
                b.Prism(new Vector3(x, 0.28f, 0f), 6, 0.11f, 0.10f, 0.56f, pants);
            b.Box(new Vector3(0f, 0.80f, 0f), new Vector3(0.56f, 0.52f, 0.32f), shirt);
            b.Box(new Vector3(0f, 0.60f, 0f), new Vector3(0.58f, 0.07f, 0.34f), belt);
            b.Box(new Vector3(0f, 0.60f, 0.17f), new Vector3(0.1f, 0.08f, 0.02f), buckle);
            foreach (float x in new[] { -0.35f, 0.35f })
                b.Prism(new Vector3(x, 0.78f, 0f), 6, 0.08f, 0.07f, 0.42f, shirt, Quaternion.Euler(0f, 0f, x > 0f ? -10f : 10f));
            b.Ball(new Vector3(0f, 1.2f, 0f), 0.15f, skin, 0.95f);
            b.Ball(new Vector3(0f, 1.21f, 0.14f), 0.045f, skin); // a proper nose
            b.Frustum(new Vector3(0f, 0.98f, 0.12f), 0.08f, 0.06f, 0.28f, 0.12f, 0.34f, hair); // beard, chin to belly
            b.Ball(new Vector3(0f, 1.28f, -0.01f), 0.16f, iron, 0.62f); // iron cap
            b.Box(new Vector3(0f, 1.25f, 0f), new Vector3(0.34f, 0.03f, 0.34f), iron);
        }
        return b.Build("Customer" + race);
    }

    // Where a customer wears their weapon: at the hip for everyone - lower and wider on a dwarf.
    public static Vector3 CustomerHip(Race race)
        => race == Race.Dwarf ? new Vector3(0.36f, 0.66f, 0.06f) : race == Race.Elf ? new Vector3(0.24f, 1.04f, 0.05f) : new Vector3(0.26f, 1.0f, 0.05f);

    // ---- the smith's home (behind the seat) and personal work table ---------------------------------
    // The house is three separate models on one root (see Prototype1SceneBuilder.CreateHomeArea) so it can
    // grow later without touching the rest: HouseStructure (walls, roof, chimney, window, awning), HouseDoor
    // (the End-the-Day interactable) and HouseDecor (bench, flower box, firewood - the part upgrades would
    // add to or swap out). All three share one local frame: front wall faces +X, footprint centred on the
    // origin, floor at y = 0.
    public const float HouseDepth = 3.6f;   // along X (front wall at +HouseDepth/2)
    public const float HouseWidth = 5.0f;   // along Z
    public const float HouseFoundation = 0.3f;
    public const float HouseDoorZ = 0.8f;
    public const float HouseDoorWidth = 1.0f;
    public const float HouseDoorHeight = 1.95f;
    private const float HouseWallTop = 2.4f;
    private const float HouseRidge = 4.0f;
    private const float HouseWindowZ = -1.3f;

    private static readonly Color Plaster = new Color(0.86f, 0.79f, 0.64f);
    private static readonly Color Timber = new Color(0.30f, 0.19f, 0.10f);
    private static readonly Color Thatch = new Color(0.55f, 0.36f, 0.20f);
    private static readonly Color Canvas = new Color(0.58f, 0.44f, 0.23f); // same cloth as the TentCanopy

    public static LowPolyModel HouseStructure()
    {
        var b = new MeshBuilder();
        int stone = b.Paint(new Color(0.48f, 0.46f, 0.43f), 0.08f);
        int stoneDark = b.Paint(new Color(0.36f, 0.34f, 0.32f), 0.08f);
        int plaster = b.Paint(Plaster, 0.05f);
        int timber = b.Paint(Timber, 0.08f);
        int roof = b.Paint(Thatch, 0.06f);
        int roofDark = b.Paint(new Color(0.44f, 0.28f, 0.16f), 0.06f);
        int glass = b.Paint(new Color(1f, 0.78f, 0.42f), 0.2f, 0.55f); // warm light inside
        int shutter = b.Paint(new Color(0.26f, 0.38f, 0.40f), 0.1f);
        int cloth = b.Paint(Canvas, 0.15f);
        int iron = b.Paint(Iron, 0.45f);
        int flame = b.Paint(new Color(1f, 0.70f, 0.30f), 0f, 1.4f);
        int shadow = b.Paint(new Color(0.08f, 0.06f, 0.05f), 0f);

        float D = HouseDepth, W = HouseWidth, F = HouseFoundation, H = HouseWallTop, R = HouseRidge;
        float front = D * 0.5f;

        // Stone foundation, plastered walls, and the attic gable (a wedge with its ridge along Z).
        b.Box(new Vector3(0f, F * 0.5f, 0f), new Vector3(D + 0.2f, F, W + 0.2f), stone);
        b.Box(new Vector3(0f, F + (H - F) * 0.5f, 0f), new Vector3(D, H - F, W), plaster);
        b.Frustum(new Vector3(0f, H + (R - H) * 0.5f, 0f), D, W, 0.04f, W, R - H, plaster);

        // Timber framing: corner posts, a top beam all round, and posts/rails on the front face.
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
                b.Box(new Vector3(sx * (front + 0.01f), F + (H - F) * 0.5f, sz * (W * 0.5f + 0.01f)), new Vector3(0.16f, H - F, 0.16f), timber);
        b.Box(new Vector3(front + 0.02f, H - 0.06f, 0f), new Vector3(0.12f, 0.14f, W + 0.1f), timber);
        b.Box(new Vector3(-front - 0.02f, H - 0.06f, 0f), new Vector3(0.12f, 0.14f, W + 0.1f), timber);
        foreach (float sz in new[] { -1f, 1f }) b.Box(new Vector3(0f, H - 0.06f, sz * (W * 0.5f + 0.02f)), new Vector3(D + 0.1f, 0.14f, 0.12f), timber);
        float doorL = HouseDoorZ - HouseDoorWidth * 0.5f - 0.08f, doorR = HouseDoorZ + HouseDoorWidth * 0.5f + 0.08f;
        foreach (float z in new[] { doorL, doorR, -0.2f })
            b.Box(new Vector3(front + 0.03f, F + (H - F) * 0.5f, z), new Vector3(0.12f, H - F, 0.14f), timber);
        b.Box(new Vector3(front + 0.03f, F + HouseDoorHeight + 0.07f, HouseDoorZ), new Vector3(0.14f, 0.14f, doorR - doorL), timber); // lintel
        b.Box(new Vector3(front + 0.03f, 1.0f, (-W * 0.5f - 0.2f) * 0.5f), new Vector3(0.1f, 0.1f, W * 0.5f - 0.2f), timber); // sill rail
        b.Box(new Vector3(front + 0.03f, 1.2f, (doorR + W * 0.5f) * 0.5f), new Vector3(0.1f, 0.1f, W * 0.5f - doorR), timber);
        // A diagonal brace beside the door, for that timber-frame look.
        b.Box(new Vector3(front + 0.03f, 1.72f, 2.05f), new Vector3(0.08f, 1.05f, 0.1f), timber, Quaternion.Euler(-38f, 0f, 0f));
        // Dark doorway recess behind the door leaf.
        b.Box(new Vector3(front + 0.005f, F + HouseDoorHeight * 0.5f, HouseDoorZ), new Vector3(0.02f, HouseDoorHeight, HouseDoorWidth), shadow);

        // Roof: two thick sloped slabs overhanging the walls, plus a ridge beam.
        float half = D * 0.5f, rise = R - H, t = 0.14f, overhang = 0.45f;
        float theta = Mathf.Atan2(rise, half);
        float slab = Mathf.Sqrt(half * half + rise * rise) + overhang;
        foreach (float s in new[] { 1f, -1f })
        {
            Vector3 down = new Vector3(s * Mathf.Cos(theta), -Mathf.Sin(theta), 0f);
            Vector3 normal = new Vector3(s * Mathf.Sin(theta), Mathf.Cos(theta), 0f);
            Vector3 c = new Vector3(s * half * 0.5f, (R + H) * 0.5f, 0f) + normal * (t * 0.5f) + down * (overhang * 0.5f);
            b.Box(c, new Vector3(slab, t, W + 0.7f), roof, Quaternion.Euler(0f, 0f, -s * theta * Mathf.Rad2Deg));
            // A darker trim course along the eaves.
            Vector3 eave = c + down * (slab * 0.5f - 0.12f) + normal * 0.03f;
            b.Box(eave, new Vector3(0.24f, t, W + 0.72f), roofDark, Quaternion.Euler(0f, 0f, -s * theta * Mathf.Rad2Deg));
        }
        b.Box(new Vector3(0f, R + 0.1f, 0f), new Vector3(0.22f, 0.16f, W + 0.8f), timber);

        // Stone chimney at the back (the smith keeps a warm hearth too).
        b.Box(new Vector3(-0.75f, (H + R + 1.0f) * 0.5f, 1.55f), new Vector3(0.62f, R + 1.0f - H, 0.62f), stone);
        b.Box(new Vector3(-0.75f, R + 1.05f, 1.55f), new Vector3(0.76f, 0.12f, 0.76f), stoneDark);

        // Front window: frame, warm glass, a cross of mullions, and open shutters.
        float wy = 1.55f;
        b.Box(new Vector3(front + 0.04f, wy, HouseWindowZ), new Vector3(0.08f, 0.92f, 0.92f), timber);
        b.Box(new Vector3(front + 0.07f, wy, HouseWindowZ), new Vector3(0.03f, 0.76f, 0.76f), glass);
        b.Box(new Vector3(front + 0.09f, wy, HouseWindowZ), new Vector3(0.03f, 0.76f, 0.05f), timber);
        b.Box(new Vector3(front + 0.09f, wy, HouseWindowZ), new Vector3(0.03f, 0.05f, 0.76f), timber);
        foreach (float s in new[] { -1f, 1f })
            b.Box(new Vector3(front + 0.06f, wy, HouseWindowZ + s * 0.72f), new Vector3(0.05f, 0.86f, 0.44f), shutter);

        // A canvas awning over the door, the same cloth as the workshop canopy - home and work, one place.
        float awnOut = 1.15f, awnHigh = 2.32f, awnLow = 2.0f, awnW = 1.7f;
        float awnAngle = Mathf.Atan2(awnHigh - awnLow, awnOut) * Mathf.Rad2Deg;
        b.Box(new Vector3(front + awnOut * 0.5f, (awnHigh + awnLow) * 0.5f, HouseDoorZ), new Vector3(awnOut + 0.1f, 0.04f, awnW), cloth, Quaternion.Euler(0f, 0f, -awnAngle));
        foreach (float s in new[] { -1f, 1f })
            b.Prism(new Vector3(front + awnOut - 0.05f, awnLow * 0.5f, HouseDoorZ + s * (awnW * 0.5f - 0.06f)), 6, 0.04f, 0.035f, awnLow, timber);

        // Stone doorstep, and a lantern by the door that's always lit.
        b.Box(new Vector3(front + 0.3f, 0.1f, HouseDoorZ), new Vector3(0.55f, 0.2f, 1.3f), stone);
        b.Box(new Vector3(front + 0.12f, 1.95f, 0.05f), new Vector3(0.18f, 0.04f, 0.04f), iron);
        b.Box(new Vector3(front + 0.2f, 1.78f, 0.05f), new Vector3(0.15f, 0.2f, 0.15f), iron);
        b.Box(new Vector3(front + 0.2f, 1.78f, 0.05f), new Vector3(0.16f, 0.14f, 0.11f), flame);
        return b.Build("HouseStructure");
    }

    // The front door, as its own object so it can be the End-the-Day interactable (and swing open one day).
    // Origin at the centre of the door leaf; it faces +X.
    public static LowPolyModel HouseDoor()
    {
        var b = new MeshBuilder();
        int[] planks = { b.Paint(new Color(0.45f, 0.29f, 0.15f), 0.08f), b.Paint(new Color(0.40f, 0.26f, 0.13f), 0.08f) };
        int iron = b.Paint(Iron, 0.45f);
        float w = HouseDoorWidth, h = HouseDoorHeight;
        const int count = 4;
        for (int i = 0; i < count; i++)
        {
            float z = -w * 0.5f + (i + 0.5f) * w / count;
            b.Box(new Vector3(0f, 0f, z), new Vector3(0.07f, h, w / count - 0.012f), planks[i % 2]);
        }
        foreach (float y in new[] { -h * 0.3f, h * 0.3f })
            b.Box(new Vector3(0.045f, y, -0.1f), new Vector3(0.025f, 0.08f, w * 0.75f), iron); // strap hinges
        b.Ball(new Vector3(0.07f, 0f, w * 0.34f), 0.045f, iron);                                  // handle
        b.Box(new Vector3(0.045f, 0f, w * 0.34f), new Vector3(0.02f, 0.14f, 0.08f), iron);
        return b.Build("HouseDoor");
    }

    // Small lived-in touches around the front: a bench under the window, a flower box, stacked firewood.
    public static LowPolyModel HouseDecor()
    {
        var b = new MeshBuilder();
        int wood = b.Paint(new Color(0.42f, 0.28f, 0.15f), 0.08f);
        int log = b.Paint(new Color(0.46f, 0.32f, 0.18f), 0.08f);
        int logEnd = b.Paint(new Color(0.72f, 0.58f, 0.38f), 0.08f);
        int soil = b.Paint(new Color(0.25f, 0.17f, 0.10f), 0.05f);
        int leaf = b.Paint(new Color(0.26f, 0.46f, 0.20f), 0.1f);
        int[] petals = { b.Paint(new Color(0.85f, 0.30f, 0.30f), 0.2f), b.Paint(new Color(0.95f, 0.80f, 0.30f), 0.2f), b.Paint(new Color(0.75f, 0.55f, 0.85f), 0.2f) };
        float front = HouseDepth * 0.5f;

        // Bench under the window.
        b.Box(new Vector3(front + 0.35f, 0.46f, HouseWindowZ), new Vector3(0.38f, 0.07f, 1.35f), wood);
        foreach (float s in new[] { -1f, 1f })
            b.Box(new Vector3(front + 0.35f, 0.22f, HouseWindowZ + s * 0.55f), new Vector3(0.3f, 0.44f, 0.08f), wood);

        // Flower box on the window sill.
        b.Box(new Vector3(front + 0.16f, 1.02f, HouseWindowZ), new Vector3(0.22f, 0.16f, 0.95f), wood);
        b.Box(new Vector3(front + 0.16f, 1.1f, HouseWindowZ), new Vector3(0.18f, 0.02f, 0.9f), soil);
        for (int i = 0; i < 6; i++)
        {
            float z = HouseWindowZ - 0.38f + i * 0.15f;
            b.Ball(new Vector3(front + 0.16f, 1.16f, z), 0.07f, leaf, 0.8f);
            b.Ball(new Vector3(front + 0.18f, 1.24f, z + 0.03f), 0.045f, petals[i % petals.Length]);
        }

        // Firewood stacked against the side wall, logs lying along X.
        var rng = new System.Random(3);
        for (int row = 0; row < 3; row++)
            for (int i = 0; i < 4 - row; i++)
            {
                float z = -HouseWidth * 0.5f - 0.25f - (i + row * 0.5f) * 0.2f;
                float y = 0.1f + row * 0.17f;
                float len = 0.75f + (float)rng.NextDouble() * 0.15f;
                b.Prism(new Vector3(front - 0.6f, y, z), 6, 0.09f, 0.09f, len, log, AlongX);
                b.Prism(new Vector3(front - 0.6f + len * 0.5f, y, z), 6, 0.075f, 0.075f, 0.01f, logEnd, AlongX);
            }
        return b.Build("HouseDecor");
    }

    // The smith's own little table, between their seat and the meadow: where the journal lives.
    // Origin on the floor under its centre; the tabletop (JournalTableTop high) runs along Z, and the
    // player sits on its -X side.
    public const float JournalTableTop = 0.8f;

    public static LowPolyModel JournalTable()
    {
        var b = new MeshBuilder();
        int top = b.Paint(new Color(0.50f, 0.34f, 0.18f), 0.1f);
        int dark = b.Paint(new Color(0.34f, 0.22f, 0.11f), 0.08f);
        int paper = b.Paint(new Color(0.92f, 0.87f, 0.72f), 0.05f);
        int ink = b.Paint(new Color(0.10f, 0.12f, 0.18f), 0.6f);
        int quill = b.Paint(new Color(0.95f, 0.95f, 0.92f), 0.1f);
        int wax = b.Paint(new Color(0.93f, 0.90f, 0.80f), 0.1f);
        int brass = b.Paint(new Color(0.72f, 0.56f, 0.24f), 0.5f);
        int flame = b.Paint(new Color(1f, 0.72f, 0.28f), 0f, 1.6f);

        float T = JournalTableTop;
        // Planked top with a slightly darker middle board, on four sturdy legs with stretchers.
        b.Box(new Vector3(-0.24f, T - 0.035f, 0f), new Vector3(0.24f, 0.07f, 1.25f), top);
        b.Box(new Vector3(0f, T - 0.035f, 0f), new Vector3(0.24f, 0.07f, 1.25f), dark);
        b.Box(new Vector3(0.24f, T - 0.035f, 0f), new Vector3(0.24f, 0.07f, 1.25f), top);
        foreach (float sx in new[] { -1f, 1f })
        {
            foreach (float sz in new[] { -1f, 1f })
                b.Box(new Vector3(sx * 0.27f, (T - 0.07f) * 0.5f, sz * 0.52f), new Vector3(0.08f, T - 0.07f, 0.08f), dark);
            b.Box(new Vector3(sx * 0.27f, 0.18f, 0f), new Vector3(0.05f, 0.06f, 1.0f), dark);
        }
        b.Box(new Vector3(0f, T - 0.1f, 0f), new Vector3(0.5f, 0.06f, 1.0f), dark); // apron

        // A couple of loose papers, an ink pot with a quill, and a candle in a brass dish.
        b.Box(new Vector3(0.1f, T + 0.003f, -0.33f), new Vector3(0.24f, 0.004f, 0.3f), paper, Quaternion.Euler(0f, 12f, 0f));
        b.Box(new Vector3(0.14f, T + 0.007f, -0.36f), new Vector3(0.22f, 0.004f, 0.28f), paper, Quaternion.Euler(0f, -9f, 0f));
        b.Prism(new Vector3(0.14f, T + 0.04f, 0.42f), 8, 0.05f, 0.035f, 0.08f, ink);
        b.Frustum(new Vector3(0.12f, T + 0.17f, 0.46f), 0.012f, 0.004f, 0.045f, 0.004f, 0.26f, quill, Quaternion.Euler(-15f, 0f, 18f));
        b.Prism(new Vector3(0.16f, T + 0.012f, -0.5f), 8, 0.075f, 0.08f, 0.02f, brass);
        b.Prism(new Vector3(0.16f, T + 0.09f, -0.5f), 7, 0.028f, 0.026f, 0.14f, wax);
        b.Ball(new Vector3(0.16f, T + 0.185f, -0.5f), 0.022f, flame, 1.5f);
        return b.Build("JournalTable");
    }

    // The Blacksmith Journal, lying open: leather cover, two page blocks with a few lines of writing and a
    // little sword sketch, and a red ribbon bookmark. Origin at the bottom centre; the spine runs along X
    // (away from the reader, who sits on the -X side) and the pages open out to +/-Z.
    public static LowPolyModel JournalBook()
    {
        var b = new MeshBuilder();
        int leather = b.Paint(new Color(0.42f, 0.17f, 0.10f), 0.2f);
        int page = b.Paint(new Color(0.95f, 0.90f, 0.76f), 0.05f);
        int ink = b.Paint(new Color(0.22f, 0.16f, 0.12f), 0.05f);
        int ribbon = b.Paint(new Color(0.75f, 0.12f, 0.12f), 0.2f);
        int sketch = b.Paint(new Color(0.45f, 0.45f, 0.48f), 0.1f);

        b.Box(new Vector3(0f, 0.01f, 0f), new Vector3(0.36f, 0.02f, 0.52f), leather);
        foreach (float s in new[] { -1f, 1f })
        {
            b.Box(new Vector3(0f, 0.035f, s * 0.123f), new Vector3(0.33f, 0.03f, 0.235f), page);
            // Lines of writing run across the page, as the reader sees them.
            for (int i = 0; i < 7; i++)
            {
                float x = 0.12f - i * 0.035f;
                float len = (i % 3 == 2 ? 0.12f : 0.18f);
                if (s < 0f && i >= 2 && i <= 4) continue; // room for the sketch on the left page
                b.Box(new Vector3(x, 0.051f, s * 0.123f), new Vector3(0.006f, 0.003f, len), ink);
            }
        }
        // A tiny sword sketched on the left page.
        b.Box(new Vector3(0.02f, 0.052f, -0.123f), new Vector3(0.012f, 0.003f, 0.16f), sketch);
        b.Box(new Vector3(0.02f, 0.052f, -0.06f), new Vector3(0.05f, 0.003f, 0.012f), sketch);
        b.Box(new Vector3(0f, 0.051f, 0f), new Vector3(0.33f, 0.004f, 0.008f), ink); // the gutter
        b.Box(new Vector3(-0.2f, 0.02f, 0.02f), new Vector3(0.08f, 0.006f, 0.025f), ribbon, Quaternion.Euler(0f, 0f, 25f));
        return b.Build("JournalBook");
    }

    // ---- the supplier: the Material Order Book by the storage rack, and the morning's delivery crate -----------

    // A small wooden writing stand with the supplier's ledger lying open on its slanted top. Origin at the floor;
    // the reader stands on the -Z side (the top slopes up away from them).
    public const float LedgerStandHeight = 1.0f;
    public static LowPolyModel SupplyLedger()
    {
        var b = new MeshBuilder();
        int wood = b.Paint(C(0.40f, 0.27f, 0.15f), 0.10f);
        int dark = b.Paint(C(0.27f, 0.17f, 0.09f), 0.08f);
        int cover = b.Paint(C(0.20f, 0.30f, 0.24f), 0.20f);
        int page = b.Paint(C(0.94f, 0.89f, 0.74f), 0.05f);
        int ink = b.Paint(C(0.22f, 0.16f, 0.12f), 0.05f);
        int copper = b.Paint(C(0.78f, 0.45f, 0.24f), 0.4f);
        int feather = b.Paint(C(0.92f, 0.90f, 0.86f), 0.1f);

        // Cross feet, a post, and the slanted top with a lip at the front for the book to rest against.
        b.Box(new Vector3(0f, 0.04f, 0f), new Vector3(0.52f, 0.08f, 0.10f), dark);
        b.Box(new Vector3(0f, 0.04f, 0f), new Vector3(0.10f, 0.08f, 0.44f), dark);
        b.Box(new Vector3(0f, 0.47f, 0f), new Vector3(0.09f, 0.82f, 0.09f), wood, Quaternion.Euler(0f, 0f, 1.5f));
        float tilt = 24f, H = LedgerStandHeight - 0.06f;
        var slope = Quaternion.Euler(-tilt, 0f, 0f);
        b.Box(new Vector3(0f, H, 0f), new Vector3(0.56f, 0.035f, 0.42f), wood, slope);
        b.Box(new Vector3(0f, H - 0.02f, -0.02f), new Vector3(0.16f, 0.07f, 0.24f), dark); // bracket under the top
        Vector3 Top(float x, float z, float up) => new Vector3(0f, H, 0f) + slope * new Vector3(x, 0.018f + up, z);
        b.Box(Top(0f, -0.2f, 0.02f), new Vector3(0.56f, 0.04f, 0.03f), dark, slope); // the lip

        // The ledger, open: a cloth cover, two page blocks, ruled lines and a column of tally marks per page.
        b.Box(Top(0f, 0.01f, 0.008f), new Vector3(0.48f, 0.014f, 0.34f), cover, slope);
        foreach (float s in new[] { -1f, 1f })
        {
            b.Box(Top(s * 0.115f, 0.01f, 0.026f), new Vector3(0.215f, 0.024f, 0.31f), page, slope);
            for (int i = 0; i < 6; i++)
            {
                float z = 0.11f - i * 0.045f;
                b.Box(Top(s * 0.115f - 0.02f, z, 0.04f), new Vector3(0.13f, 0.003f, 0.006f), ink, slope);
                b.Box(Top(s * 0.115f + 0.075f, z, 0.04f), new Vector3(0.025f, 0.003f, 0.006f), i % 2 == 0 ? copper : ink, slope);
            }
            b.Box(Top(s * 0.115f + 0.052f, 0.01f, 0.04f), new Vector3(0.004f, 0.003f, 0.27f), ink, slope); // the price column
        }
        b.Box(Top(0f, 0.01f, 0.04f), new Vector3(0.008f, 0.004f, 0.31f), ink, slope); // the gutter

        // An inkpot with a quill on a little shelf at the side.
        b.Box(new Vector3(0.33f, H - 0.12f, 0.02f), new Vector3(0.14f, 0.025f, 0.14f), wood);
        b.Prism(new Vector3(0.33f, H - 0.08f, 0.02f), 6, 0.035f, 0.028f, 0.06f, ink);
        b.Frustum(new Vector3(0.35f, H + 0.02f, 0.03f), 0.012f, 0.004f, 0.045f, 0.006f, 0.2f, feather, Quaternion.Euler(0f, 0f, -18f));
        return b.Build("SupplyLedger");
    }

    // The supplier's delivery crate: a nailed-shut plank crate with a rope round it, straw poking out under the
    // lid and a parchment tag on the front (-Z). Origin at the bottom centre.
    public const float DeliveryCrateHeight = 0.5f;
    public static LowPolyModel DeliveryCrate()
    {
        var b = new MeshBuilder();
        int wood = b.Paint(C(0.55f, 0.39f, 0.22f), 0.08f);
        int slat = b.Paint(C(0.38f, 0.25f, 0.13f), 0.08f);
        int rope = b.Paint(C(0.72f, 0.62f, 0.42f), 0.05f);
        int straw = b.Paint(C(0.86f, 0.74f, 0.38f), 0.05f);
        int tag = b.Paint(C(0.93f, 0.88f, 0.72f), 0.05f);
        int ink = b.Paint(C(0.25f, 0.16f, 0.10f), 0.05f);
        int iron = b.Paint(C(0.30f, 0.30f, 0.32f), 0.4f);

        float w = 0.66f, d = 0.48f, h = DeliveryCrateHeight;
        b.Box(new Vector3(0f, h * 0.5f - 0.01f, 0f), new Vector3(w, h - 0.02f, d), wood);
        // Corner posts and cross battens on the long faces.
        foreach (float x in new[] { -1f, 1f })
            foreach (float z in new[] { -1f, 1f })
                b.Box(new Vector3(x * (w * 0.5f - 0.025f), h * 0.5f, z * (d * 0.5f - 0.025f)), new Vector3(0.06f, h, 0.06f), slat);
        foreach (float z in new[] { -1f, 1f })
        {
            b.Box(new Vector3(0f, h * 0.5f, z * (d * 0.5f + 0.012f)), new Vector3(w * 1.02f, 0.05f, 0.02f), slat, Quaternion.Euler(0f, 0f, 33f * z));
            b.Box(new Vector3(0f, 0.05f, z * (d * 0.5f + 0.01f)), new Vector3(w, 0.06f, 0.02f), slat);
        }
        // The lid: planks with a gap, straw showing through; a rope tied round the middle.
        for (int i = 0; i < 3; i++)
            b.Box(new Vector3(0f, h + 0.012f, -d * 0.33f + i * d * 0.33f), new Vector3(w + 0.02f, 0.025f, d * 0.29f), i == 1 ? wood : slat);
        b.Box(new Vector3(0.08f, h + 0.01f, -d * 0.165f), new Vector3(0.3f, 0.03f, 0.03f), straw, Quaternion.Euler(0f, 15f, 0f));
        b.Box(new Vector3(-0.12f, h + 0.01f, d * 0.165f), new Vector3(0.24f, 0.03f, 0.03f), straw, Quaternion.Euler(0f, -20f, 0f));
        b.Box(new Vector3(-0.12f, h * 0.5f + 0.01f, 0f), new Vector3(0.035f, h + 0.07f, d + 0.04f), rope);
        b.Box(new Vector3(-0.12f, h + 0.035f, 0f), new Vector3(0.035f, 0.03f, d + 0.04f), rope);
        // The address tag nailed to the front, with a few lines of writing.
        b.Box(new Vector3(0.14f, h * 0.55f, -d * 0.5f - 0.03f), new Vector3(0.2f, 0.13f, 0.01f), tag, Quaternion.Euler(0f, 0f, -4f));
        for (int i = 0; i < 3; i++)
            b.Box(new Vector3(0.14f, h * 0.55f + 0.035f - i * 0.03f, -d * 0.5f - 0.036f), new Vector3(i == 2 ? 0.08f : 0.14f, 0.006f, 0.004f), ink, Quaternion.Euler(0f, 0f, -4f));
        b.Box(new Vector3(0.14f, h * 0.55f + 0.055f, -d * 0.5f - 0.04f), new Vector3(0.015f, 0.015f, 0.01f), iron);
        return b.Build("DeliveryCrate");
    }

    // The same townsperson sitting down (for the crowd's benches): a copy of their standing mesh with the legs folded
    // forward at the hip and everything above lowered onto the seat. Works for every race's Customer model - their
    // trousers are always submesh 3 - and puts the hip at the origin, so the caller lifts it to the seat height.
    public const float SeatedThighHalf = 0.09f; // how far the folded thighs reach below the hip (sit them on the seat top)
    public static Mesh SeatedCustomer(Mesh standing)
    {
        var verts = standing.vertices;
        var legs = new HashSet<int>();
        if (standing.subMeshCount > 3) foreach (int i in standing.GetTriangles(3)) legs.Add(i);
        float hip = 0f;
        foreach (int i in legs) hip = Mathf.Max(hip, verts[i].y);
        if (hip <= 0f) hip = 0.9f;
        float knee = hip * 0.5f;
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 v = verts[i];
            if (!legs.Contains(i)) v.y -= hip;                                       // torso, arms, head: down onto the seat
            else if (v.y >= knee) v = new Vector3(v.x, v.z, hip - v.y);                // thigh: turned forward
            else v = new Vector3(v.x, v.y - knee, v.z + (hip - knee));                 // shin: hangs from the knee
            verts[i] = v;
        }
        var mesh = Object.Instantiate(standing);
        mesh.name = standing.name + "_Seated";
        mesh.vertices = verts;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
