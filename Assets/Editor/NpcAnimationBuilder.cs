using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Builds the townsfolk's animation set on the shared skeleton (NpcSkeleton) as real Mecanim assets:
//   Assets/Resources/Npc/Clips/*.anim        - one clip per motion
//   Assets/Resources/Npc/NpcAnimator.controller - one state per clip (NpcBody cross-fades between them)
// Each clip is a pose written as a smooth function of time and sampled at 30 fps, so loops close seamlessly and the
// motion never pops. Clips only rotate bones (plus a small vertical bob on "Bob"), so the same clip fits every race.
//
// Walking: the thighs swing LINEARLY through the stance (the planted foot slides back at a constant rate relative
// to the hips) and the knee only bends in the swing - so NpcBody can set the playback speed from the real movement
// speed (NpcSkeleton.StridePerCycle) and the planted foot stays put on the ground.
// Menu: Tiny Blacksmith > Build NPC Animations; headless: -executeMethod NpcAnimationBuilder.Build -quit
public static class NpcAnimationBuilder
{
    private const string Folder = "Assets/Resources/Npc";
    private const string ClipFolder = Folder + "/Clips";
    public const string ControllerPath = Folder + "/NpcAnimator.controller";
    private const float Fps = 30f;

    // One sampled pose: a local euler rotation per bone, and the bob height.
    private class Pose
    {
        public readonly Vector3[] r = new Vector3[NpcSkeleton.Count];
        public float bob;
        public Vector3 this[NpcBone b] { get => r[(int)b]; set => r[(int)b] = value; }
    }

    private struct ClipDef
    {
        public string name;
        public float length;
        public bool loop;
        public bool walk; // plays at "Pace" (movement-matched) instead of "Tempo"
        public System.Action<float, Pose> pose; // u = 0..1 through the clip
    }

    [MenuItem("Tiny Blacksmith/Build NPC Animations")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Resources", "Npc");
        if (!AssetDatabase.IsValidFolder(ClipFolder)) AssetDatabase.CreateFolder(Folder, "Clips");

        var defs = Definitions();
        var clips = new Dictionary<string, AnimationClip>();
        foreach (var d in defs) clips[d.name] = WriteClip(d);

        if (File.Exists(ControllerPath)) AssetDatabase.DeleteAsset(ControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter(new AnimatorControllerParameter { name = "Pace", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
        controller.AddParameter(new AnimatorControllerParameter { name = "Tempo", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
        var sm = controller.layers[0].stateMachine;
        foreach (var d in defs)
        {
            var st = sm.AddState(d.name, new Vector3(250f + (d.walk ? 0f : 300f), 60f * sm.states.Length, 0f));
            st.motion = clips[d.name];
            st.writeDefaultValues = true;
            st.speedParameterActive = true;
            st.speedParameter = d.walk ? "Pace" : "Tempo";
            if (d.name == "Idle_01") sm.defaultState = st;
        }
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[NpcAnim] built {defs.Count} clips and {ControllerPath}");
    }

    private static AnimationClip WriteClip(ClipDef d)
    {
        int frames = Mathf.Max(2, Mathf.RoundToInt(d.length * Fps));
        var poses = new Pose[frames + 1];
        for (int f = 0; f <= frames; f++)
        {
            poses[f] = new Pose();
            float u = f / (float)frames;
            d.pose(d.loop ? Mathf.Repeat(u, 1f) : u, poses[f]);
        }
        if (d.loop) { d.pose(0f, poses[frames] = new Pose()); } // a loop ends exactly where it began

        var clip = new AnimationClip { name = d.name, frameRate = Fps };
        for (int b = 0; b < NpcSkeleton.Count; b++)
        {
            bool used = false;
            foreach (var ps in poses) if (ps.r[b].sqrMagnitude > 1e-6f) { used = true; break; }
            if (!used) continue;
            string path = NpcSkeleton.Path((NpcBone)b);
            for (int axis = 0; axis < 3; axis++)
            {
                var keys = new Keyframe[poses.Length];
                for (int f = 0; f < poses.Length; f++) keys[f] = new Keyframe(f / Fps * (d.length * Fps / frames), poses[f].r[b][axis]);
                var curve = Linear(keys);
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "localEulerAnglesRaw." + "xyz"[axis]), curve);
            }
        }
        bool bobs = false;
        foreach (var ps in poses) if (Mathf.Abs(ps.bob) > 1e-5f) { bobs = true; break; }
        if (bobs)
        {
            string path = NpcSkeleton.Path(NpcBone.Bob);
            var ys = new Keyframe[poses.Length];
            for (int f = 0; f < poses.Length; f++) ys[f] = new Keyframe(f / Fps * (d.length * Fps / frames), poses[f].bob);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalPosition.y"), Linear(ys));
            var zero = AnimationCurve.Constant(0f, d.length, 0f);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalPosition.x"), zero);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalPosition.z"), zero);
        }
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = d.loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        string file = $"{ClipFolder}/{d.name}.anim";
        if (File.Exists(file)) AssetDatabase.DeleteAsset(file);
        AssetDatabase.CreateAsset(clip, file);
        return clip;
    }

    // Dense samples joined by straight segments (30 per second - smooth to the eye, exact where it matters).
    private static AnimationCurve Linear(Keyframe[] keys)
    {
        for (int i = 0; i < keys.Length; i++)
        {
            float inT = i > 0 ? (keys[i].value - keys[i - 1].value) / Mathf.Max(1e-5f, keys[i].time - keys[i - 1].time) : 0f;
            float outT = i < keys.Length - 1 ? (keys[i + 1].value - keys[i].value) / Mathf.Max(1e-5f, keys[i + 1].time - keys[i].time) : inT;
            if (i == 0) inT = outT;
            keys[i].inTangent = inT;
            keys[i].outTangent = outT;
        }
        return new AnimationCurve(keys);
    }

    // ---- the motions ---------------------------------------------------------------------------------------------

    private static float S(float x) => Mathf.Sin(2f * Mathf.PI * x);
    private static float Co(float x) => Mathf.Cos(2f * Mathf.PI * x);
    private static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
    private static float Seg(float u, float a, float b) => Smooth((u - a) / Mathf.Max(1e-4f, b - a)); // 0 before a, 1 after b
    private static Vector3 V(float x, float y = 0f, float z = 0f) => new Vector3(x, y, z);

    private class WalkStyle
    {
        public float amp, knee, arm, armOut, elbow, bob, sway, yaw, lean, headNod;
    }

    // One leg through the cycle: stance (0..0.5) the thigh sweeps linearly from forward to back with the knee nearly
    // straight; swing (0.5..1) it comes forward again with the knee lifted. Sole kept level while planted.
    // Where the ankle of a planted leg sits relative to the hip at stance progress s (0 = heel strike, 1 = toe off):
    // it slides straight back at a constant rate, and the hip-to-ankle distance dips slightly mid-stance (soft knee).
    private static float StanceX(NpcProportions prop, float amp, float s) => (prop.hipY - prop.ankleY) * Mathf.Sin(amp * Mathf.Deg2Rad) * (1f - 2f * s);
    private static float StanceReach(NpcProportions prop, float s) => (prop.hipY - prop.ankleY) * (1f - NpcSkeleton.StanceDip * Mathf.Sin(Mathf.PI * s));

    // How far the hips must come down so the planted leg still reaches the ground: exact, so the foot neither floats
    // nor sinks - which gives the natural rise and fall of a step.
    private static float StanceDrop(NpcProportions prop, float amp, float s)
    {
        float x = StanceX(prop, amp, s), d = StanceReach(prop, s);
        return Mathf.Sqrt(Mathf.Max(0f, d * d - x * x)) - (prop.hipY - prop.ankleY);
    }

    // One leg through the cycle by 2-bone IK. Stance (0..0.5): the ankle stays on the ground moving back at a constant
    // rate (the body passes over a still foot). Swing (0..1 of the second half): the ankle follows a Hermite curve that
    // leaves and lands with the stance's own backward speed (no skid at toe-off or heel strike) while lifted clear of
    // the ground by `lift` metres at most - it touches down only at the very end.
    private static void Leg(Pose p, NpcBone thigh, NpcBone shin, NpcBone foot, float phase, float amp, float lift, float bob, NpcProportions prop)
    {
        phase = Mathf.Repeat(phase, 1f);
        float t = prop.hipY - prop.kneeY, sh = prop.kneeY - prop.ankleY, L = t + sh;
        float hipH = L + bob;               // hip height above the ankle's ground level
        bool stance = phase < 0.5f;
        float x, y;                          // ankle relative to the hip: forward, downward
        if (stance)
        {
            float s = phase / 0.5f;
            x = StanceX(prop, amp, s);
            y = hipH;
        }
        else
        {
            float s = (phase - 0.5f) / 0.5f;
            float x0 = L * Mathf.Sin(amp * Mathf.Deg2Rad);
            x = x0 * (-8f * s * s * s + 12f * s * s - 2f * s - 1f);
            y = hipH - lift * Mathf.Sin(Mathf.PI * s);
        }
        float d = Mathf.Min(Mathf.Sqrt(x * x + y * y), L - 1e-4f);
        float aim = Mathf.Atan2(x, y);                                           // forward of straight down
        float hipAngle = Mathf.Acos(Mathf.Clamp((t * t + d * d - sh * sh) / (2f * t * d), -1f, 1f));
        float kneeInner = Mathf.Acos(Mathf.Clamp((t * t + sh * sh - d * d) / (2f * t * sh), -1f, 1f));
        float fwd = (aim + hipAngle) * Mathf.Rad2Deg;                            // knee points forward
        float knee = (Mathf.PI - kneeInner) * Mathf.Rad2Deg;
        p[thigh] = V(-fwd);
        p[shin] = V(knee);
        // Planted: sole kept level. Swinging: the toe hangs a little, then the foot levels again for the landing.
        float level = fwd - knee;
        p[foot] = V(stance ? level : level * (1f - 0.5f * Mathf.Sin(Mathf.PI * (phase - 0.5f) / 0.5f)));
    }

    private static void Walk(float u, Pose p, WalkStyle st, NpcProportions prop)
    {
        // The hips ride on whichever leg is planted: exactly low enough for it to touch the ground (no floating feet).
        float ph = Mathf.Repeat(u, 1f), stanceU = ph < 0.5f ? ph / 0.5f : (ph - 0.5f) / 0.5f;
        p.bob = StanceDrop(prop, st.amp, stanceU);
        float lift = (prop.hipY - prop.ankleY) * 0.075f * st.knee / 50f;
        Leg(p, NpcBone.UpperLegR, NpcBone.LowerLegR, NpcBone.FootR, u, st.amp, lift, p.bob, prop);
        Leg(p, NpcBone.UpperLegL, NpcBone.LowerLegL, NpcBone.FootL, u + 0.5f, st.amp, lift, p.bob, prop);
        float c = Co(u);
        // Arms swing against the legs (right leg forward at u = 0 -> left arm forward), elbows bend on the forward swing.
        p[NpcBone.UpperArmL] = V(-st.arm * c, 0f, -st.armOut);
        p[NpcBone.UpperArmR] = V(st.arm * c, 0f, st.armOut);
        p[NpcBone.LowerArmL] = V(-(st.elbow + st.elbow * Mathf.Max(0f, c)));
        p[NpcBone.LowerArmR] = V(-(st.elbow + st.elbow * Mathf.Max(0f, -c)));
        // The pelvis stays square (turning it would swing the planted foot across the ground); the twist and sway of
        // the walk live in the spine instead.
        p[NpcBone.Hips] = V(0f);
        p[NpcBone.Spine] = V(st.lean, st.yaw * c, st.sway * 0.6f * S(u));
        p[NpcBone.Chest] = V(0f, -st.yaw * 1.3f * c, 0f);
        p[NpcBone.Head] = V(-st.lean * 0.6f + st.headNod * Mathf.Cos(4f * Mathf.PI * u), st.yaw * 0.3f * c, 0f);
    }

    private static void Breathe(float u, Pose p, float amount = 1f)
    {
        p[NpcBone.Chest] += V(1.4f * amount * S(u));
        p[NpcBone.Spine] += V(0.6f * amount * S(u));
        p[NpcBone.Head] += V(1.2f * amount * S(u + 0.3f));
        p.bob += 0.004f * amount * S(u);
    }

    private static void ArmsRelaxed(float u, Pose p)
    {
        p[NpcBone.UpperArmR] = V(2f * S(u + 0.2f), 0f, 4f);
        p[NpcBone.UpperArmL] = V(2f * S(u + 0.2f), 0f, -4f);
        p[NpcBone.LowerArmR] = V(-9f - 2f * S(u));
        p[NpcBone.LowerArmL] = V(-9f - 2f * S(u));
    }

    private static void CarryArms(float bounce, Pose p)
    {
        p[NpcBone.UpperArmR] = V(-52f + bounce, 0f, -4f);
        p[NpcBone.UpperArmL] = V(-52f + bounce, 0f, 4f);
        p[NpcBone.LowerArmR] = V(-62f - bounce);
        p[NpcBone.LowerArmL] = V(-62f - bounce);
    }

    private static void SitPose(Pose p)
    {
        foreach (var (thigh, shin, foot) in new[] { (NpcBone.UpperLegR, NpcBone.LowerLegR, NpcBone.FootR), (NpcBone.UpperLegL, NpcBone.LowerLegL, NpcBone.FootL) })
        {
            p[thigh] = V(-86f);
            p[shin] = V(84f);
            p[foot] = V(2f);
        }
        p[NpcBone.UpperArmR] = V(-30f, 0f, 3f);
        p[NpcBone.UpperArmL] = V(-30f, 0f, -3f);
        p[NpcBone.LowerArmR] = V(-50f);
        p[NpcBone.LowerArmL] = V(-50f);
        p[NpcBone.Spine] = V(4f);
    }

    private static Pose Lerp(Pose a, Pose b, float t)
    {
        var o = new Pose();
        for (int i = 0; i < NpcSkeleton.Count; i++) o.r[i] = Vector3.Lerp(a.r[i], b.r[i], t);
        o.bob = Mathf.Lerp(a.bob, b.bob, t);
        return o;
    }

    private static void Copy(Pose from, Pose to) { for (int i = 0; i < NpcSkeleton.Count; i++) to.r[i] = from.r[i]; to.bob = from.bob; }

    private static List<ClipDef> Definitions()
    {
        var human = new WalkStyle { amp = NpcSkeleton.WalkAmplitude(Race.Human), knee = 50f, arm = 20f, armOut = 4f, elbow = 9f, bob = 0.025f, sway = 3f, yaw = 5f, lean = 3f, headNod = 1f };
        var elf = new WalkStyle { amp = NpcSkeleton.WalkAmplitude(Race.Elf), knee = 44f, arm = 13f, armOut = 3f, elbow = 7f, bob = 0.014f, sway = 1.8f, yaw = 3.5f, lean = 0.5f, headNod = 0.5f };
        var dwarf = new WalkStyle { amp = NpcSkeleton.WalkAmplitude(Race.Dwarf), knee = 46f, arm = 26f, armOut = 9f, elbow = 12f, bob = 0.034f, sway = 6f, yaw = 7f, lean = 5f, headNod = 1.5f };
        var carry = new WalkStyle { amp = NpcSkeleton.CarryAmplitude, knee = 40f, bob = 0.02f, sway = 2.5f, yaw = 2f, lean = -2f, headNod = 0.6f };
        var patrol = new WalkStyle { amp = NpcSkeleton.PatrolAmplitude, knee = 44f, arm = 14f, armOut = 4f, elbow = 8f, bob = 0.018f, sway = 1.5f, yaw = 3f, lean = 0f, headNod = 0.4f };

        var list = new List<ClipDef>
        {
            new ClipDef { name = "Idle_01", length = 3f, loop = true, pose = (u, p) => { ArmsRelaxed(u, p); Breathe(u, p); } },
            new ClipDef { name = "Idle_02", length = 4f, loop = true, pose = (u, p) =>
            {
                // Weight shifting from foot to foot, glancing about.
                ArmsRelaxed(u, p);
                float w = S(u);
                p[NpcBone.Hips] = V(0f, 0f, 3f * w);
                p[NpcBone.Spine] = V(0f, 0f, -2f * w);
                p[NpcBone.UpperLegR] = V(-3f * (0.5f + 0.5f * w));
                p[NpcBone.LowerLegR] = V(7f * (0.5f + 0.5f * w));
                p[NpcBone.FootR] = V(-4f * (0.5f + 0.5f * w));
                p[NpcBone.UpperLegL] = V(-3f * (0.5f - 0.5f * w));
                p[NpcBone.LowerLegL] = V(7f * (0.5f - 0.5f * w));
                p[NpcBone.FootL] = V(-4f * (0.5f - 0.5f * w));
                p.bob = -0.01f * Mathf.Abs(w);
                p[NpcBone.Head] = V(0f, 14f * S(u + 0.1f), 2f * S(2f * u));
                Breathe(u, p, 0.8f);
            } },
            new ClipDef { name = "Carry_Idle", length = 3f, loop = true, pose = (u, p) => { CarryArms(1.5f * S(u), p); Breathe(u, p); p[NpcBone.Spine] += V(-2f); } },
            new ClipDef { name = "Talk_Gesture_01", length = 3f, loop = true, pose = (u, p) =>
            {
                ArmsRelaxed(u, p);
                // An open hand, making a point twice; nodding along.
                p[NpcBone.UpperArmR] = V(-24f - 12f * S(2f * u), 0f, 10f);
                p[NpcBone.LowerArmR] = V(-72f - 18f * S(2f * u + 0.25f));
                p[NpcBone.HandR] = V(0f, 20f * S(2f * u), -10f);
                p[NpcBone.UpperArmL] = V(-6f, 0f, -5f);
                p[NpcBone.LowerArmL] = V(-18f);
                p[NpcBone.Chest] = V(0f, 6f * S(u), 0f);
                p[NpcBone.Head] = V(3f * S(3f * u), 5f * S(u + 0.2f), 0f);
                Breathe(u, p);
            } },
            new ClipDef { name = "Talk_Gesture_02", length = 3f, loop = true, pose = (u, p) =>
            {
                // Both hands out - explaining, then a shrug.
                float shrug = Mathf.Max(0f, S(u));
                p[NpcBone.UpperArmR] = V(-26f, 0f, 16f + 14f * shrug);
                p[NpcBone.UpperArmL] = V(-26f, 0f, -16f - 14f * shrug);
                p[NpcBone.LowerArmR] = V(-58f - 12f * S(2f * u));
                p[NpcBone.LowerArmL] = V(-58f - 12f * S(2f * u + 0.5f));
                p[NpcBone.HandR] = V(0f, 0f, -25f * shrug);
                p[NpcBone.HandL] = V(0f, 0f, 25f * shrug);
                p[NpcBone.Neck] = V(4f * shrug);
                p[NpcBone.Head] = V(0f, 0f, 5f * S(u));
                Breathe(u, p);
            } },
            new ClipDef { name = "Turn_In_Place", length = 0.8f, loop = false, pose = (u, p) =>
            {
                ArmsRelaxed(u, p);
                float l = Mathf.Sin(Mathf.PI * Mathf.Clamp01(u / 0.5f)), r = Mathf.Sin(Mathf.PI * Mathf.Clamp01((u - 0.5f) / 0.5f));
                p[NpcBone.UpperLegL] = V(-18f * l); p[NpcBone.LowerLegL] = V(34f * l); p[NpcBone.FootL] = V(-16f * l);
                p[NpcBone.UpperLegR] = V(-18f * r); p[NpcBone.LowerLegR] = V(34f * r); p[NpcBone.FootR] = V(-16f * r);
                p.bob = -0.01f * (l + r);
            } },
            new ClipDef { name = "Look_Around", length = 4f, loop = false, pose = (u, p) =>
            {
                ArmsRelaxed(u, p);
                float yaw = -45f * Seg(u, 0.08f, 0.28f) + 90f * Seg(u, 0.42f, 0.62f) - 45f * Seg(u, 0.78f, 0.95f);
                p[NpcBone.Head] = V(-3f * Seg(u, 0.08f, 0.2f) + 3f * Seg(u, 0.8f, 0.95f), yaw, 0f);
                p[NpcBone.Chest] = V(0f, yaw * 0.3f, 0f);
                Breathe(u, p);
            } },
            new ClipDef { name = "Receive_Item", length = 1.4f, loop = false, pose = (u, p) =>
            {
                // Both hands out to take it, then drawn in to the chest - and back down at the end.
                float reach = Seg(u, 0f, 0.3f), draw = Seg(u, 0.55f, 0.8f), down = Seg(u, 0.85f, 1f);
                var a = new Pose(); ArmsRelaxed(u, a);
                var b = new Pose();
                b[NpcBone.UpperArmR] = V(-60f, 0f, -6f); b[NpcBone.UpperArmL] = V(-60f, 0f, 6f);
                b[NpcBone.LowerArmR] = V(-25f); b[NpcBone.LowerArmL] = V(-25f);
                var c = new Pose();
                c[NpcBone.UpperArmR] = V(-28f, 0f, -8f); c[NpcBone.UpperArmL] = V(-28f, 0f, 8f);
                c[NpcBone.LowerArmR] = V(-88f); c[NpcBone.LowerArmL] = V(-88f);
                Copy(Lerp(Lerp(Lerp(a, b, reach), c, draw), a, down), p);
                p[NpcBone.Spine] = V(6f * reach * (1f - draw));
                p[NpcBone.Head] = V(8f * reach * (1f - down));
            } },
            new ClipDef { name = "Give_Item", length = 1.4f, loop = false, pose = (u, p) =>
            {
                ArmsRelaxed(u, p);
                float reach = Seg(u, 0.05f, 0.4f) - Seg(u, 0.65f, 1f);
                p[NpcBone.UpperArmR] = V(-76f * reach + 2f, 0f, 4f - 6f * reach);
                p[NpcBone.LowerArmR] = V(-9f - 4f * reach);
                p[NpcBone.HandR] = V(0f, 0f, -12f * reach);
                p[NpcBone.Spine] = V(6f * reach);
                p[NpcBone.Head] = V(6f * reach);
            } },
            new ClipDef { name = "Sit", length = 3f, loop = true, pose = (u, p) => { SitPose(p); Breathe(u, p); p[NpcBone.Head] += V(0f, 8f * S(u + 0.2f), 0f); } },
            new ClipDef { name = "Stand_Up", length = 0.9f, loop = false, pose = (u, p) =>
            {
                var sit = new Pose(); SitPose(sit);
                var stand = new Pose(); ArmsRelaxed(0f, stand);
                Copy(Lerp(sit, stand, Smooth(u)), p);
                p[NpcBone.Spine] += V(18f * Mathf.Sin(Mathf.PI * u)); // lean forward to rise
            } },
            new ClipDef { name = "Inspect_Item", length = 3f, loop = true, pose = (u, p) =>
            {
                // Holding something up to look it over, turning it in the hands.
                p[NpcBone.UpperArmR] = V(-38f, 0f, -6f); p[NpcBone.UpperArmL] = V(-38f, 0f, 6f);
                p[NpcBone.LowerArmR] = V(-86f); p[NpcBone.LowerArmL] = V(-86f);
                p[NpcBone.HandR] = V(0f, 15f * S(u), 0f); p[NpcBone.HandL] = V(0f, 15f * S(u), 0f);
                p[NpcBone.Head] = V(18f + 2f * S(2f * u), 6f * S(u), 0f);
                p[NpcBone.Spine] = V(5f);
                Breathe(u, p, 0.7f);
            } },
            new ClipDef { name = "Waiting_Impatient", length = 2f, loop = true, pose = (u, p) =>
            {
                // Hands behind the back, tapping a foot, glancing about.
                p[NpcBone.UpperArmR] = V(18f, 0f, 6f); p[NpcBone.UpperArmL] = V(18f, 0f, -6f);
                p[NpcBone.LowerArmR] = V(-38f); p[NpcBone.LowerArmL] = V(-38f);
                float tap = Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * 3f * u));
                p[NpcBone.FootR] = V(-16f * tap);
                p[NpcBone.LowerLegR] = V(4f * tap);
                p[NpcBone.Hips] = V(0f, 0f, 2f);
                p[NpcBone.Head] = V(-2f, 10f * S(u), 0f);
                Breathe(u, p, 1.2f);
            } },
        };

        // Walking, carrying and patrolling - one clip per race, since how far the hips drop onto the planted leg
        // depends on the leg's length (the swing, arms and rhythm are each race's own too).
        foreach (Race race in new[] { Race.Human, Race.Elf, Race.Dwarf })
        {
            var prop = NpcProportions.For(race);
            var style = race == Race.Elf ? elf : race == Race.Dwarf ? dwarf : human;
            string r = race.ToString();
            list.Add(new ClipDef { name = "Walk_" + r, length = NpcSkeleton.WalkCycleSeconds, loop = true, walk = true, pose = (u, p) => Walk(u, p, style, prop) });
            list.Add(new ClipDef { name = "Carry_Item_Walk_" + r, length = NpcSkeleton.WalkCycleSeconds, loop = true, walk = true, pose = (u, p) =>
            {
                Walk(u, p, carry, prop);
                CarryArms(3f * Mathf.Cos(4f * Mathf.PI * u), p);
            } });
            list.Add(new ClipDef { name = "Guard_Patrol_" + r, length = NpcSkeleton.WalkCycleSeconds, loop = true, walk = true, pose = (u, p) =>
            {
                Walk(u, p, patrol, prop);
                // The right hand holds the spear upright and steady; the left swings.
                p[NpcBone.UpperArmR] = V(-8f, 0f, 6f);
                p[NpcBone.LowerArmR] = V(-78f);
                p[NpcBone.Chest] += V(-2f);
            } });
        }
        return list;
    }
}
