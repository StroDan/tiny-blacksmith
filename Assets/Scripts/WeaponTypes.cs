using System.Collections.Generic;
using UnityEngine;

// The kinds of weapon the smith can forge. Stored on WorkpieceData (and so in every save); the default (0)
// is Sword, so saves from before weapon types existed load as the swords they were.
public enum WeaponType { Sword, Dagger, Sickle, Spear, Hammer }

// One piece of a weapon's handle, fitted at the workbench: what it looks like, where it sits along the
// weapon (atX: world units past the weapon's shoulder, see LowPoly.WeaponShoulder) and whether the player has
// to turn it into line. The models are authored lying along X, like the sword's Guard/Grip/Pommel.
public struct HiltPart
{
    public string name;
    public System.Func<LowPolyModel> model;
    public float atX;
    public bool rotatable;

    public HiltPart(string name, System.Func<LowPolyModel> model, float atX, bool rotatable)
    {
        this.name = name; this.model = model; this.atX = atX; this.rotatable = rotatable;
    }
}

// What makes each weapon type play differently. Everything still runs through the same stations and the same
// three stats - a type only leans on them differently:
//   Sword  - the all-rounder.            Dagger - Balance and Sharpness (light, quick, keen).
//   Spear  - Balance and Durability.     Sickle - Balance and Sharpness (a clean, curved cut).
//   Hammer - Durability above all; it has no edge, so it skips the grindstone and has no Sharpness at all.
public class WeaponInfo
{
    public WeaponType type;
    public string name;          // "Dagger"
    public string noun;          // "dagger"
    public string article;       // "a"
    public bool hasEdge;         // false: no grindstone, Sharpness is "N/A" everywhere
    public float durWeight, balWeight, sharpWeight; // what it's judged on (Grade) - also the order leanings
    public float durPerPoint, balPerPoint;          // anvil: stat points per red / yellow point taken
    public float sharpGain = 1f;                    // grindstone: how much of each touch's Sharpness gain sticks
    public float resultScale = 2.2f;                // result screen: display scale (long weapons shown smaller)
    public float weight = 1.2f;                     // kg, shown on the result screen
    public string strengths;     // one line for the Ready screen
    public HiltPart[] hilt;

    public string WithArticle => $"{article} {noun}";
    public string PartNames => string.Join(", ", System.Array.ConvertAll(hilt, h => h.name));

    // The grade weighs the stats this weapon is actually about (a sword's weights are all 1 - same as before).
    public string Grade(WorkpieceData s)
    {
        float total = durWeight + balWeight + (hasEdge ? sharpWeight : 0f);
        float avg = (s.durability * durWeight + s.balance * balWeight + (hasEdge ? s.sharpness * sharpWeight : 0f)) / total;
        if (avg >= 90f) return "S";
        if (avg >= 75f) return "A";
        if (avg >= 55f) return "B";
        if (avg >= 35f) return "C";
        return "D";
    }

    // "Sharpness 64", or "Sharpness N/A" for a weapon without an edge.
    public string SharpnessText(float value) => hasEdge ? $"{value:0}" : "N/A";
}

public static class Weapons
{
    private const float S = LowPoly.SwordScale;

    public static readonly WeaponInfo[] All =
    {
        new WeaponInfo
        {
            type = WeaponType.Sword, name = "Sword", noun = "sword", article = "a", hasEdge = true,
            durWeight = 1f, balWeight = 1f, sharpWeight = 1f, durPerPoint = 10f, balPerPoint = 10f,
            strengths = "an all-rounder: Durability, Balance and Sharpness all count",
            hilt = new[]
            {
                new HiltPart("Guard", LowPoly.Guard, 0.03f * S, true),
                new HiltPart("Grip", LowPoly.Grip, 0.21f * S, true),
                new HiltPart("Pommel", LowPoly.Pommel, 0.415f * S, false),
            },
        },
        new WeaponInfo
        {
            type = WeaponType.Dagger, name = "Dagger", noun = "dagger", article = "a", hasEdge = true,
            durWeight = 0.6f, balWeight = 1.3f, sharpWeight = 1.2f, durPerPoint = 8f, balPerPoint = 12f, sharpGain = 1.15f,
            resultScale = 2.8f, weight = 0.4f,
            strengths = "light and quick: Balance and Sharpness matter most",
            hilt = new[]
            {
                new HiltPart("Guard", () => LowPoly.Guard(0.65f), 0.02f * S, true),
                new HiltPart("Grip", () => LowPoly.Grip(0.17f, 0.027f), 0.14f * S, true),
                new HiltPart("Pommel", () => LowPoly.Pommel(0.75f), 0.265f * S, false),
            },
        },
        new WeaponInfo
        {
            type = WeaponType.Sickle, name = "Sickle", noun = "sickle", article = "a", hasEdge = true,
            durWeight = 0.7f, balWeight = 1.1f, sharpWeight = 1.2f, durPerPoint = 9f, balPerPoint = 11f, sharpGain = 1.1f,
            resultScale = 2.5f, weight = 0.5f,
            strengths = "a clean curved cut: Balance and Sharpness matter most",
            hilt = new[]
            {
                new HiltPart("Ferrule", LowPoly.Ferrule, 0.02f * S, false),
                new HiltPart("Handle", () => LowPoly.WoodHandle(0.27f, 0.032f), 0.175f * S, true),
            },
        },
        new WeaponInfo
        {
            type = WeaponType.Spear, name = "Spear", noun = "spear", article = "a", hasEdge = true,
            durWeight = 1.1f, balWeight = 1.2f, sharpWeight = 0.7f, durPerPoint = 11f, balPerPoint = 11f, sharpGain = 0.85f,
            resultScale = 1.9f, weight = 1.9f,
            strengths = "long reach: Balance and Durability matter most",
            hilt = new[]
            {
                new HiltPart("Shaft", () => LowPoly.WoodHandle(1.05f, 0.03f), 0.525f * S, true),
                new HiltPart("Butt Cap", LowPoly.ButtCap, 1.075f * S, false),
            },
        },
        new WeaponInfo
        {
            type = WeaponType.Hammer, name = "Hammer", noun = "hammer", article = "a", hasEdge = false,
            durWeight = 1.6f, balWeight = 0.8f, sharpWeight = 0f, durPerPoint = 12f, balPerPoint = 8f, sharpGain = 0f,
            resultScale = 2.5f, weight = 1.6f,
            strengths = "built to take a beating: Durability above all (no edge - it skips the grindstone)",
            hilt = new[]
            {
                new HiltPart("Handle", () => LowPoly.WoodHandle(0.64f, 0.03f), 0.20f * S, true),
                new HiltPart("Grip Wrap", () => LowPoly.Grip(0.2f, 0.036f), 0.42f * S, true),
            },
        },
    };

    public static WeaponInfo Get(WeaponType type)
    {
        int i = (int)type;
        return i >= 0 && i < All.Length ? All[i] : All[0];
    }

    // ---- finished-weapon display models (rack pegs, customers' belts, passers-by) --------------------------

    private static readonly Dictionary<WeaponType, LowPolyModel> displayCache = new Dictionary<WeaponType, LowPolyModel>();

    // A finished weapon as one merged model: handle end at y = 0, head/tip straight up along +Y, the first
    // paint (submesh 0) is the metal - recoloured per metal at runtime. The sword keeps its own hand-made
    // display model (LowPoly.DisplaySword); every other type is its finished forging shape with its hilt parts
    // at their perfectly-fitted spots, merged - so what hangs on the rack is exactly what was made.
    public static LowPolyModel DisplayModel(WeaponType type)
    {
        if (displayCache.TryGetValue(type, out var cached) && cached.mesh != null) return cached;
        LowPolyModel model;
        if (type == WeaponType.Sword)
        {
            model = LowPoly.DisplaySword();
        }
        else
        {
            var info = Get(type);
            float shoulder = LowPoly.WeaponShoulder(type);
            var pieces = new List<(LowPolyModel m, Matrix4x4 at)> { (LowPoly.WeaponPhase(type, LowPoly.PhaseCount - 1), Matrix4x4.identity) };
            foreach (var h in info.hilt)
                pieces.Add((h.model(), Matrix4x4.TRS(new Vector3(0f, 0f, shoulder + h.atX), Quaternion.Euler(0f, -90f, 0f), Vector3.one)));

            // Weapon space (handle toward +Z) -> display space (handle end at the bottom, tip up), shrunk a little
            // like the sword's display model.
            Bounds all = default;
            bool any = false;
            foreach (var p in pieces)
            {
                var b = TransformBounds(p.at, p.m.mesh.bounds);
                if (!any) { all = b; any = true; } else all.Encapsulate(b);
            }
            const float displayScale = LowPoly.DisplaySwordScale / LowPoly.SwordScale;
            Matrix4x4 toDisplay = Matrix4x4.Scale(Vector3.one * displayScale) *
                                  Matrix4x4.TRS(new Vector3(0f, all.max.z, 0f), Quaternion.Euler(90f, 0f, 0f), Vector3.one);
            model = Merge(info.name + "Display", pieces, toDisplay);
            displayTop[type] = all.max.z;
        }
        displayCache[type] = model;
        return model;
    }

    private static readonly Dictionary<WeaponType, float> displayTop = new Dictionary<WeaponType, float>();

    // A point along the weapon's handle line (weapon-space z, see LowPoly) in display-model units.
    private static Vector3 DisplayPoint(WeaponType type, float x, float z)
    {
        DisplayModel(type);
        const float k = LowPoly.DisplaySwordScale / LowPoly.SwordScale;
        return new Vector3(x * k, (displayTop[type] - z) * k, 0f);
    }

    // How a weapon sits on a forked rack peg, the way a smith would actually hang it: `rest` is the point of the
    // display model that sits on the peg's prongs, `tipDown` whether the model hangs upside down (head/tip at the
    // bottom). Purely visual - nothing here touches the weapon's data.
    //   Sword, Dagger - rest on the crossguard: handle up, blade down.
    //   Hammer        - the head rests across the prongs, the handle hangs straight down.
    //   Spear         - the socket collar rests on the prongs: head up, shaft down.
    //   Sickle        - hooked over the peg by the inside of its curve: blade up, handle hanging below.
    public static void RackPose(WeaponType type, out bool tipDown, out Vector3 rest)
    {
        const float S = LowPoly.SwordScale;
        switch (type)
        {
            case WeaponType.Dagger:
                tipDown = true;
                rest = DisplayPoint(type, 0f, LowPoly.WeaponShoulder(type) + Get(type).hilt[0].atX);
                break;
            case WeaponType.Hammer:
                tipDown = false;
                rest = DisplayPoint(type, 0f, 0.03f * S); // just under the middle of the head
                break;
            case WeaponType.Spear:
                tipDown = false;
                rest = DisplayPoint(type, 0f, 0.24f * S); // the bottom of the socket, where the shaft goes in
                break;
            case WeaponType.Sickle:
                tipDown = false;
                // The crescent (LowPoly.SicklePhase): radius 0.19 round a centre at x 0.19, z -0.02; its inside top.
                rest = DisplayPoint(type, 0.19f * S, (-0.02f - 0.19f + 0.025f) * S);
                break;
            default:
                tipDown = true;
                rest = new Vector3(0f, LowPoly.DisplaySwordGuardY, 0f);
                break;
        }
    }

    // How much of the weapon hangs below its peg, and how much stands above it (display units).
    public static void RackExtent(WeaponType type, out float below, out float above)
    {
        RackPose(type, out bool tipDown, out Vector3 rest);
        var b = DisplayModel(type).mesh.bounds;
        below = tipDown ? b.max.y - rest.y : rest.y - b.min.y;
        above = tipDown ? rest.y - b.min.y : b.max.y - rest.y;
    }

    public static float DisplayLength(WeaponType type) => DisplayModel(type).mesh.bounds.max.y;

    // Poses a display-model weapon (child of a customer model) the way that customer would carry it: a spear
    // slung across the back, everything else hanging from the hip, handle up - shrunk only if it would
    // otherwise drag on the ground (a dwarf's sword is a touch smaller than a human's).
    public static void WearOn(Transform weapon, WeaponType type, Race race) => WearOn(weapon, type, LowPoly.CustomerHip(race));

    // On an NpcBody (parent the weapon to its Bob bone): at the belt, by the right hip.
    public static void WearOn(Transform weapon, WeaponType type, NpcProportions body)
        => WearOn(weapon, type, new Vector3(body.torsoW * 0.5f + 0.05f, body.waistY + 0.02f, 0.05f), body.torsoD * 0.5f + 0.05f);

    private static void WearOn(Transform weapon, WeaponType type, Vector3 hip, float back = 0.17f)
    {
        float length = DisplayLength(type);
        if (type == WeaponType.Spear)
        {
            weapon.localScale = Vector3.one * Mathf.Min(1f, (hip.y < 0.8f ? 1.2f : 1.5f) / length);
            weapon.localPosition = new Vector3(0.1f, hip.y * 0.4f, -back);
            weapon.localRotation = Quaternion.Euler(0f, 0f, -14f);
        }
        else
        {
            weapon.localScale = Vector3.one * Mathf.Min(1f, (hip.y - 0.08f) / length);
            weapon.localPosition = hip;
            weapon.localRotation = Quaternion.Euler(-25f, 0f, 180f);
        }
    }

    private static Bounds TransformBounds(Matrix4x4 m, Bounds b)
    {
        Bounds r = new Bounds(m.MultiplyPoint3x4(b.min), Vector3.zero);
        for (int i = 1; i < 8; i++)
            r.Encapsulate(m.MultiplyPoint3x4(new Vector3((i & 1) != 0 ? b.max.x : b.min.x, (i & 2) != 0 ? b.max.y : b.min.y, (i & 4) != 0 ? b.max.z : b.min.z)));
        return r;
    }

    // Glues several models into one: every source submesh stays its own submesh (and keeps its paint), in order.
    private static LowPolyModel Merge(string name, List<(LowPolyModel m, Matrix4x4 at)> pieces, Matrix4x4 post)
    {
        var combine = new List<CombineInstance>();
        var paints = new List<Paint>();
        var single = new List<CombineInstance>();
        foreach (var p in pieces)
        {
            for (int s = 0; s < p.m.mesh.subMeshCount; s++)
                combine.Add(new CombineInstance { mesh = p.m.mesh, subMeshIndex = s, transform = post * p.at });
            paints.AddRange(p.m.paints);
            single.Add(new CombineInstance { mesh = p.m.singleMesh, subMeshIndex = 0, transform = post * p.at });
        }
        var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.CombineMeshes(combine.ToArray(), false, true);
        mesh.RecalculateBounds();
        var singleMesh = new Mesh { name = name + "_Single", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        singleMesh.CombineMeshes(single.ToArray(), true, true);
        return new LowPolyModel { mesh = mesh, singleMesh = singleMesh, paints = paints.ToArray() };
    }
}
