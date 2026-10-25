using UnityEngine;

// The one skeleton every townsperson shares - humans, elves and dwarves alike - so a single set of animation clips
// (Assets/Resources/Npc, built by NpcAnimationBuilder) drives all of them. Only the bones' rest POSITIONS differ by
// race (NpcProportions); every rest rotation is identity, and the clips only ever rotate bones (plus a small vertical
// bob on "Bob"), so the same clip fits a tall elf and a stout dwarf.
//
// Model space: feet at y = 0, facing +Z, the character's right hand toward +X.
// Rotation conventions the clips follow: +X on a leg or arm swings it BACK (negative = forward), +X on a lower leg
// bends the knee, -X on a lower arm bends the elbow (forearm forward), +X on the spine/chest leans forward, +Z on the
// right upper arm raises it out to the side (-Z for the left), Y turns (head, chest, hips).
public enum NpcBone
{
    Bob, Hips, Spine, Chest, Neck, Head,
    UpperArmR, LowerArmR, HandR, UpperArmL, LowerArmL, HandL,
    UpperLegR, LowerLegR, FootR, UpperLegL, LowerLegL, FootL,
}

public static class NpcSkeleton
{
    public const int Count = 18;

    public static readonly string[] Names =
    {
        "Bob", "Hips", "Spine", "Chest", "Neck", "Head",
        "UpperArm.R", "LowerArm.R", "Hand.R", "UpperArm.L", "LowerArm.L", "Hand.L",
        "UpperLeg.R", "LowerLeg.R", "Foot.R", "UpperLeg.L", "LowerLeg.L", "Foot.L",
    };

    // Parent of each bone (-1 = the rig root, which carries the Animator).
    public static readonly int[] Parent =
    {
        -1, 0, 1, 2, 3, 4,
        3, 6, 7, 3, 9, 10,
        1, 12, 13, 1, 15, 16,
    };

    // "Bob/Hips/Spine/Chest/UpperArm.R" - the path an animation curve uses for this bone.
    public static string Path(NpcBone bone)
    {
        int i = (int)bone;
        string p = Names[i];
        for (int k = Parent[i]; k >= 0; k = Parent[k]) p = Names[k] + "/" + p;
        return p;
    }

    // ---- walking: how far one full cycle (two steps) carries the body, so playback speed can match movement ----
    // The clips solve the legs by IK (NpcAnimationBuilder.Leg): during the stance the ankle slides back along the ground
    // at a constant rate from L*sin(amp) in front of the hip to the same distance behind it, and the hips ride exactly
    // as high as that leg allows - so the planted foot moves back relative to the body by this much per cycle (two
    // steps). Playing the clip at speed / StridePerCycle cycles a second keeps it still on the ground (no gliding).
    public const float WalkCycleSeconds = 1f;
    public const float StanceDip = 0.006f;   // mid-stance the leg shortens by this fraction of its length (soft knee)
    public static float WalkAmplitude(Race race) => race == Race.Dwarf ? 27f : race == Race.Elf ? 22f : 24f;
    public const float CarryAmplitude = 18f;
    public const float PatrolAmplitude = 21f;

    public static float StridePerCycle(NpcProportions p, float amplitudeDegrees)
    {
        return 4f * (p.hipY - p.ankleY) * Mathf.Sin(amplitudeDegrees * Mathf.Deg2Rad);
    }
}

// Where each race's bones sit (metres, model space) and how big its body parts are. Heights: human ~1.74,
// elf ~1.86 (taller, slimmer, longer limbs), dwarf ~1.34 (short legs, broad chest, big head and hands).
public class NpcProportions
{
    public float hipY, kneeY, ankleY, legX, thighW, shinW, footLen, footH;
    public float waistY, chestY, shoulderY, torsoW, torsoD;
    public float armX, upperArm, lowerArm, armW, hand;
    public float neckY, headH, headW, headD;

    public float LegLength => hipY;
    public float Height => neckY + 0.04f + headH;

    public static readonly NpcProportions Human = new NpcProportions
    {
        hipY = 0.82f, kneeY = 0.45f, ankleY = 0.11f, legX = 0.098f, thighW = 0.17f, shinW = 0.15f, footLen = 0.26f, footH = 0.11f,
        waistY = 0.88f, chestY = 1.12f, shoulderY = 1.32f, torsoW = 0.42f, torsoD = 0.25f,
        armX = 0.29f, upperArm = 0.28f, lowerArm = 0.26f, armW = 0.135f, hand = 0.11f,
        neckY = 1.33f, headH = 0.37f, headW = 0.34f, headD = 0.33f,
    };

    public static readonly NpcProportions Elf = new NpcProportions
    {
        hipY = 0.93f, kneeY = 0.50f, ankleY = 0.11f, legX = 0.085f, thighW = 0.145f, shinW = 0.128f, footLen = 0.25f, footH = 0.1f,
        waistY = 0.99f, chestY = 1.22f, shoulderY = 1.42f, torsoW = 0.36f, torsoD = 0.21f,
        armX = 0.25f, upperArm = 0.31f, lowerArm = 0.29f, armW = 0.115f, hand = 0.1f,
        neckY = 1.44f, headH = 0.36f, headW = 0.30f, headD = 0.31f,
    };

    public static readonly NpcProportions Dwarf = new NpcProportions
    {
        hipY = 0.47f, kneeY = 0.26f, ankleY = 0.1f, legX = 0.13f, thighW = 0.21f, shinW = 0.195f, footLen = 0.28f, footH = 0.12f,
        waistY = 0.52f, chestY = 0.74f, shoulderY = 0.95f, torsoW = 0.62f, torsoD = 0.38f,
        armX = 0.385f, upperArm = 0.21f, lowerArm = 0.2f, armW = 0.18f, hand = 0.13f,
        neckY = 0.96f, headH = 0.36f, headW = 0.37f, headD = 0.35f,
    };

    public static NpcProportions For(Race race) => race == Race.Elf ? Elf : race == Race.Dwarf ? Dwarf : Human;

    // Each bone's rest position in model space.
    public Vector3 RestPosition(NpcBone b)
    {
        float armTop = shoulderY - 0.03f;
        switch (b)
        {
            case NpcBone.Bob: return Vector3.zero;
            case NpcBone.Hips: return new Vector3(0f, hipY, 0f);
            case NpcBone.Spine: return new Vector3(0f, waistY, 0f);
            case NpcBone.Chest: return new Vector3(0f, chestY, 0f);
            case NpcBone.Neck: return new Vector3(0f, neckY, 0f);
            case NpcBone.Head: return new Vector3(0f, neckY + 0.04f, 0f);
            case NpcBone.UpperArmR: return new Vector3(armX, armTop, 0f);
            case NpcBone.LowerArmR: return new Vector3(armX, armTop - upperArm, 0f);
            case NpcBone.HandR: return new Vector3(armX, armTop - upperArm - lowerArm, 0f);
            case NpcBone.UpperArmL: return new Vector3(-armX, armTop, 0f);
            case NpcBone.LowerArmL: return new Vector3(-armX, armTop - upperArm, 0f);
            case NpcBone.HandL: return new Vector3(-armX, armTop - upperArm - lowerArm, 0f);
            case NpcBone.UpperLegR: return new Vector3(legX, hipY, 0f);
            case NpcBone.LowerLegR: return new Vector3(legX, kneeY, 0f);
            case NpcBone.FootR: return new Vector3(legX, ankleY, 0f);
            case NpcBone.UpperLegL: return new Vector3(-legX, hipY, 0f);
            case NpcBone.LowerLegL: return new Vector3(-legX, kneeY, 0f);
            case NpcBone.FootL: return new Vector3(-legX, ankleY, 0f);
        }
        return Vector3.zero;
    }
}
