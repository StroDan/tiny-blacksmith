using System.Collections.Generic;
using UnityEngine;

// A townsperson's body: the shared skeleton (NpcSkeleton) as a transform hierarchy, one skinned mesh built from their
// NpcAppearance, and an Animator playing the shared clip set (Resources/Npc/NpcAnimator, NpcAnimationBuilder).
//
// The look follows the game's blocky low-poly style: square heads with simple faces (eyes, brows, nose, ears), hair
// and beards as chunky blocks, tunics / vests / coats / aprons / tabards with belts, gold buckles, cuffs, bracers and
// boots. Every piece is rigidly skinned to one bone (low-poly segments, like the style reference) so nothing ever
// stretches or collapses at a joint; cuffs, belts and boot tops hide the joins.
//
// Used by the city crowd (CityLife) and the customer at the smith's table (ForgeGameManager). Characters are pooled
// and re-dressed (Dress); meshes are cached by look, and all characters share one material (NpcPalette).
public class NpcBody : MonoBehaviour
{
    public NpcAppearance Look { get; private set; }
    public NpcProportions Proportions { get; private set; }
    public Animator Animator { get; private set; }
    public SkinnedMeshRenderer Skin { get; private set; }
    public Transform Bone(NpcBone b) => bones[(int)b];
    public Transform Chest => bones[(int)NpcBone.Chest];
    public Transform HandR => bones[(int)NpcBone.HandR];
    public Transform HandL => bones[(int)NpcBone.HandL];
    public Transform BobBone => bones[(int)NpcBone.Bob];
    public float Height => Proportions.Height;
    public float SeatDrop => Proportions.hipY - 0.06f; // lowering the rig by this sits the hips on a seat at 0.06 above its feet level

    private readonly Transform[] bones = new Transform[NpcSkeleton.Count];
    private string state = "";
    private float oneShotLeft;
    private string afterOneShot;
    private float idleTimer;
    private float tempo = 1f;

    // ---- making one --------------------------------------------------------------------------------------------

    private static RuntimeAnimatorController controller;

    public static NpcBody Create(Transform parent, NpcAppearance look, string name = "Body")
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var body = go.AddComponent<NpcBody>();
        for (int i = 0; i < NpcSkeleton.Count; i++)
        {
            var t = new GameObject(NpcSkeleton.Names[i]).transform;
            int p = NpcSkeleton.Parent[i];
            t.SetParent(p < 0 ? go.transform : body.bones[p], false);
            body.bones[i] = t;
        }
        var skinGo = new GameObject("Skin");
        skinGo.transform.SetParent(go.transform, false);
        body.Skin = skinGo.AddComponent<SkinnedMeshRenderer>();
        body.Skin.bones = body.bones;
        body.Skin.rootBone = body.bones[(int)NpcBone.Bob];
        body.Skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        body.Skin.quality = SkinQuality.Bone1; // rigid parts: one bone per vertex
        body.Skin.updateWhenOffscreen = false;
        body.Animator = go.AddComponent<Animator>();
        if (controller == null) controller = Resources.Load<RuntimeAnimatorController>("Npc/NpcAnimator");
        body.Animator.runtimeAnimatorController = controller;
        body.Animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms; // off screen: no bone updates
        body.Animator.applyRootMotion = false;                                 // movement is the game's, not the clips'
        body.Dress(look);
        return body;
    }

    // Put a (possibly different) person into this body: bones moved to their race's proportions, their mesh.
    public void Dress(NpcAppearance look)
    {
        Look = look;
        Proportions = NpcProportions.For(look.race);
        for (int i = 0; i < NpcSkeleton.Count; i++)
        {
            int p = NpcSkeleton.Parent[i];
            Vector3 rest = Proportions.RestPosition((NpcBone)i);
            bones[i].localPosition = p < 0 ? rest : rest - Proportions.RestPosition((NpcBone)p);
            bones[i].localRotation = Quaternion.identity;
        }
        Skin.sharedMesh = MeshFor(look);
        Skin.sharedMaterial = NpcPalette.Material;
        Skin.localBounds = new Bounds(new Vector3(0f, Proportions.Height * 0.5f, 0.1f), new Vector3(1.8f, Proportions.Height + 0.6f, 1.8f));
        tempo = (look.race == Race.Dwarf ? 0.92f : look.race == Race.Elf ? 1.06f : 1f) * (0.95f + 0.1f * Mathf.Abs(look.Key.GetHashCode() % 100) / 100f);
        if (Animator != null && Animator.runtimeAnimatorController != null)
        {
            Animator.Rebind();
            Animator.SetFloat(TempoId, tempo);
            state = "";
            Idle();
        }
    }

    // ---- animation ---------------------------------------------------------------------------------------------
    // The controller has one state per clip (no transition web): the body cross-fades between them. Walk-type states
    // play at "Pace" (matched to the actual movement speed - no gliding), everything else at "Tempo" (a race's
    // rhythm: dwarves a touch heavier, elves a touch lighter).

    private static readonly int PaceId = Animator.StringToHash("Pace"), TempoId = Animator.StringToHash("Tempo");

    public bool IsSitting => state == "Sit" || state == "Stand_Up";

    private void Play(string next, float fade = 0.2f)
    {
        if (Animator == null || Animator.runtimeAnimatorController == null || state == next) return;
        state = next;
        Animator.CrossFadeInFixedTime(next, fade);
    }

    // Walking at `speed` metres per second - the cycle plays exactly fast enough for the feet to stay planted.
    public float Pace { get; private set; }

    public void Walk(float speed, bool carrying = false, bool patrol = false)
    {
        float amp = carrying ? NpcSkeleton.CarryAmplitude : patrol ? NpcSkeleton.PatrolAmplitude : NpcSkeleton.WalkAmplitude(Look.race);
        float perSecond = NpcSkeleton.StridePerCycle(Proportions, amp) / NpcSkeleton.WalkCycleSeconds;
        Pace = Mathf.Clamp(speed / Mathf.Max(0.05f, perSecond), 0.3f, 2.5f);
        if (Animator != null) Animator.SetFloat(PaceId, Pace);
        oneShotLeft = 0f;
        Play((carrying ? "Carry_Item_Walk_" : patrol ? "Guard_Patrol_" : "Walk_") + Look.race, 0.15f);
    }

    public bool Carrying { get; set; }

    public void Idle()
    {
        oneShotLeft = 0f;
        idleTimer = Random.Range(5f, 12f);
        Play(Carrying ? "Carry_Idle" : Random.value < 0.5f ? "Idle_01" : "Idle_02", 0.25f);
    }

    public void Talk() { oneShotLeft = 0f; idleTimer = Random.Range(2.5f, 5f); Play(Random.value < 0.5f ? "Talk_Gesture_01" : "Talk_Gesture_02", 0.25f); }
    public void Sit() { oneShotLeft = 0f; Play("Sit", 0.35f); }
    public void Inspect() { oneShotLeft = 0f; idleTimer = Random.Range(4f, 8f); Play("Inspect_Item", 0.3f); }
    public void Impatient() { oneShotLeft = 0f; idleTimer = Random.Range(4f, 8f); Play("Waiting_Impatient", 0.3f); }
    public void StandUp() => Once("Stand_Up", 0.9f, "idle", 0.1f);
    public void Give() => Once("Give_Item", 1.4f, "idle");
    public void Receive() => Once("Receive_Item", 1.4f, "idle");
    public void LookAround() => Once("Look_Around", 4f, "idle");
    public void TurnInPlace() => Once("Turn_In_Place", 0.8f, "idle");

    public string State => state;

    private void Once(string clip, float seconds, string then, float fade = 0.2f)
    {
        state = "";
        Play(clip, fade);
        oneShotLeft = seconds / Mathf.Max(0.1f, tempo);
        afterOneShot = then;
    }

    // Idle life: standing people shift, look around, check what they hold, get impatient, chat on.
    private void Update()
    {
        if (oneShotLeft > 0f)
        {
            oneShotLeft -= Time.deltaTime;
            if (oneShotLeft <= 0f) { state = ""; if (afterOneShot == "talk") Talk(); else Idle(); }
            return;
        }
        bool idle = state == "Idle_01" || state == "Idle_02" || state == "Carry_Idle";
        bool talking = state == "Talk_Gesture_01" || state == "Talk_Gesture_02";
        bool busy = state == "Inspect_Item" || state == "Waiting_Impatient";
        if (!idle && !talking && !busy) return;
        idleTimer -= Time.deltaTime;
        if (idleTimer > 0f) return;
        if (talking) { state = ""; Talk(); return; } // a new gesture now and then
        if (busy) { Idle(); return; }
        if (Carrying) { idleTimer = Random.Range(5f, 10f); return; }
        float r = Random.value;
        if (r < 0.35f) LookAround();
        else { state = ""; Idle(); }
    }

    // ---- the mesh ----------------------------------------------------------------------------------------------

    private static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
    public static int CachedMeshes => meshes.Count;

    public static Mesh MeshFor(NpcAppearance look)
    {
        string key = look.Key;
        if (meshes.TryGetValue(key, out var mesh) && mesh != null) return mesh;
        mesh = new Kit(look).Build("Npc_" + look.race);
        meshes[key] = mesh;
        return mesh;
    }

    // The parts of one character, each on its bone, merged into one rigidly skinned mesh.
    private class Kit
    {
        private readonly NpcAppearance a;
        private readonly NpcProportions p;
        private readonly Dictionary<NpcBone, MeshBuilder> parts = new Dictionary<NpcBone, MeshBuilder>();
        private readonly Dictionary<(NpcBone, Color32), int> paints = new Dictionary<(NpcBone, Color32), int>();

        public Kit(NpcAppearance look) { a = look; p = NpcProportions.For(look.race); }

        private MeshBuilder B(NpcBone bone)
        {
            if (!parts.TryGetValue(bone, out var b)) { b = new MeshBuilder(); parts[bone] = b; }
            return b;
        }

        private int P(NpcBone bone, Color c)
        {
            Color32 k = c;
            if (!paints.TryGetValue((bone, k), out int i)) { i = B(bone).Paint(c, 0.1f); paints[(bone, k)] = i; }
            return i;
        }

        private void Box(NpcBone bone, Vector3 c, Vector3 s, Color col, Quaternion? r = null) => B(bone).Box(c, s, P(bone, col), r);
        private void Taper(NpcBone bone, Vector3 c, float w0, float d0, float w1, float d1, float h, Color col, Quaternion? r = null)
            => B(bone).Frustum(c, w0, d0, w1, d1, h, P(bone, col), r);

        private static void Discard(Object o) { if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o); }

        private static Color Shade(Color c, float k) => new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k));

        public Mesh Build(string name)
        {
            Legs();
            Hips();
            Torso();
            Arms();
            Head();
            Hair();
            Beard();
            Headwear();

            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var weights = new List<BoneWeight>();
            var tris = new List<int>();
            foreach (var kv in parts)
            {
                var model = kv.Value.Build(name + "_" + kv.Key);
                var mv = model.mesh.vertices;
                var mn = model.mesh.normals;
                for (int s = 0; s < model.mesh.subMeshCount; s++)
                {
                    Vector2 uv = NpcPalette.UV(model.paints[s].color);
                    foreach (int t in model.mesh.GetTriangles(s))
                    {
                        tris.Add(verts.Count);
                        verts.Add(mv[t]);
                        normals.Add(mn[t]);
                        uvs.Add(uv);
                        weights.Add(new BoneWeight { boneIndex0 = (int)kv.Key, weight0 = 1f });
                    }
                }
                Discard(model.mesh);
                Discard(model.singleMesh);
            }
            var mesh = new Mesh { name = name };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.boneWeights = weights.ToArray();
            var bind = new Matrix4x4[NpcSkeleton.Count];
            for (int i = 0; i < bind.Length; i++) bind[i] = Matrix4x4.Translate(-p.RestPosition((NpcBone)i));
            mesh.bindposes = bind;
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
            return mesh;
        }

        // ---- legs and boots --------------------------------------------------------------------------------
        private void Legs()
        {
            bool robe = a.outfit == NpcOutfit.Robe;
            float bootTop = p.ankleY + (a.race == Race.Dwarf ? 0.13f : 0.18f);
            Color bootCuff = a.race == Race.Elf ? Shade(a.boots, 1.3f) : Shade(a.boots, 1.18f);
            foreach (float s in new[] { 1f, -1f })
            {
                var thigh = s > 0 ? NpcBone.UpperLegR : NpcBone.UpperLegL;
                var shin = s > 0 ? NpcBone.LowerLegR : NpcBone.LowerLegL;
                var foot = s > 0 ? NpcBone.FootR : NpcBone.FootL;
                float x = s * p.legX;
                Box(thigh, new Vector3(x, (p.hipY + p.kneeY) * 0.5f + 0.02f, 0f), new Vector3(p.thighW, p.hipY - p.kneeY + 0.07f, p.thighW * 1.05f), a.trousers);
                Box(shin, new Vector3(x, (p.kneeY + bootTop) * 0.5f + 0.01f, 0f), new Vector3(p.shinW, p.kneeY - bootTop + 0.05f, p.shinW), a.trousers);
                if (robe) { Box(foot, new Vector3(x, p.ankleY * 0.5f, p.footLen * 0.5f - 0.08f), new Vector3(p.shinW + 0.03f, p.ankleY, p.footLen), a.boots); continue; }
                // Boot: the shaft on the shin (with a turned-down cuff), the foot on the foot bone, a darker sole.
                Box(shin, new Vector3(x, (p.ankleY + bootTop) * 0.5f, 0f), new Vector3(p.shinW + 0.035f, bootTop - p.ankleY + 0.01f, p.shinW + 0.035f), a.boots);
                Box(shin, new Vector3(x, bootTop - 0.02f, 0f), new Vector3(p.shinW + 0.06f, 0.055f, p.shinW + 0.06f), bootCuff);
                float fz = p.footLen * 0.5f - 0.075f;
                Box(foot, new Vector3(x, (p.ankleY + 0.03f) * 0.5f + 0.012f, fz), new Vector3(p.shinW + 0.045f, p.ankleY + 0.03f, p.footLen), a.boots);
                Box(foot, new Vector3(x, 0.012f, fz), new Vector3(p.shinW + 0.055f, 0.024f, p.footLen + 0.01f), Shade(a.boots, 0.6f));
            }
        }

        // ---- the lower body: skirts, hems, belt ------------------------------------------------------------
        private void Hips()
        {
            float w = p.torsoW, d = p.torsoD;
            Box(NpcBone.Hips, new Vector3(0f, p.hipY + 0.03f, 0f), new Vector3(w * 0.96f, 0.16f, d * 0.94f), a.trousers);
            float waist = p.waistY;
            switch (a.outfit)
            {
                case NpcOutfit.Vest:
                case NpcOutfit.Tunic:
                {
                    float hem = a.outfit == NpcOutfit.Vest ? p.hipY - (p.hipY - p.kneeY) * 0.52f : p.hipY - (p.hipY - p.kneeY) * 0.66f;
                    Skirt(waist, hem, w, d, 0.07f, a.main, a.outfit == NpcOutfit.Vest ? Shade(a.main, 0.82f) : Shade(a.main, 0.85f));
                    break;
                }
                case NpcOutfit.Coat:
                {
                    // Long coat to the knee: back and sides, two front panels either side of a lighter centre, trim.
                    float hem = p.kneeY - 0.03f;
                    float h = waist - hem;
                    Taper(NpcBone.Hips, new Vector3(0f, hem + h * 0.5f, -d * 0.12f), w + 0.1f, d * 0.8f + 0.06f, w + 0.02f, d * 0.8f, h, a.main);
                    Color inner = Shade(a.main, 1.22f);
                    Taper(NpcBone.Hips, new Vector3(0f, hem + h * 0.5f + 0.02f, d * 0.36f), w * 0.34f, 0.04f, w * 0.3f, 0.04f, h - 0.04f, inner);
                    foreach (float s in new[] { 1f, -1f })
                    {
                        Taper(NpcBone.Hips, new Vector3(s * (w * 0.33f + 0.02f), hem + h * 0.5f, d * 0.4f), w * 0.36f + 0.04f, 0.05f, w * 0.34f, 0.05f, h, a.main);
                        if (a.trim) Box(NpcBone.Hips, new Vector3(s * (w * 0.16f + 0.005f), hem + h * 0.5f, d * 0.43f + 0.02f), new Vector3(0.03f, h, 0.02f), a.accent);
                    }
                    if (a.trim) Taper(NpcBone.Hips, new Vector3(0f, hem + 0.02f, 0f), w + 0.13f, d + 0.1f, w + 0.12f, d + 0.09f, 0.04f, a.accent);
                    break;
                }
                case NpcOutfit.Apron:
                {
                    // Leather apron wrapping the hips to below the knee, darker pocket, with the shirt tail showing at the sides.
                    float hem = p.kneeY - (a.race == Race.Dwarf ? 0.02f : 0.06f);
                    Skirt(waist, hem, w, d, 0.06f, a.main, Shade(a.main, 0.8f));
                    break;
                }
                case NpcOutfit.Tabard:
                case NpcOutfit.Armor:
                {
                    float hem = p.hipY - (p.hipY - p.kneeY) * 0.55f;
                    Skirt(waist, hem, w, d, 0.05f, a.shirt, Shade(a.shirt, 0.8f)); // mail skirt
                    if (a.outfit == NpcOutfit.Tabard)
                        foreach (float z in new[] { 1f, -1f })
                            Box(NpcBone.Hips, new Vector3(0f, (waist + hem) * 0.5f - 0.05f, z * (d * 0.5f + 0.045f)), new Vector3(w * 0.5f, waist - hem + 0.08f, 0.025f), a.main);
                    break;
                }
                case NpcOutfit.Robe:
                {
                    float hem = 0.06f;
                    Taper(NpcBone.Hips, new Vector3(0f, (waist + hem) * 0.5f, 0f), w + 0.22f, d + 0.18f, w + 0.02f, d + 0.02f, waist - hem, a.main);
                    if (a.trim) Taper(NpcBone.Hips, new Vector3(0f, hem + 0.03f, 0f), w + 0.25f, d + 0.21f, w + 0.24f, d + 0.2f, 0.05f, a.accent);
                    break;
                }
            }
            // Belt with a gold buckle; a pouch on the hip.
            Box(NpcBone.Hips, new Vector3(0f, waist, 0f), new Vector3(w + 0.05f, 0.075f, d + 0.05f), a.belt);
            Box(NpcBone.Hips, new Vector3(0f, waist, d * 0.5f + 0.028f), new Vector3(0.1f, 0.085f, 0.02f), a.accent);
            Box(NpcBone.Hips, new Vector3(0f, waist, d * 0.5f + 0.04f), new Vector3(0.05f, 0.04f, 0.01f), a.belt);
            if (a.pouch)
            {
                Box(NpcBone.Hips, new Vector3(-(w * 0.5f - 0.02f), waist - 0.09f, d * 0.2f), new Vector3(0.08f, 0.12f, 0.1f), Shade(a.extra, 0.9f));
                Box(NpcBone.Hips, new Vector3(-(w * 0.5f - 0.02f), waist - 0.04f, d * 0.2f), new Vector3(0.09f, 0.03f, 0.11f), Shade(a.extra, 0.7f));
            }
        }

        // A flared skirt from the waist to the hem, with a darker band at the bottom.
        private void Skirt(float waist, float hem, float w, float d, float flare, Color c, Color band)
        {
            float h = waist - hem;
            Taper(NpcBone.Hips, new Vector3(0f, hem + h * 0.5f, 0f), w + flare * 2f, d + flare * 1.6f, w + 0.02f, d + 0.02f, h, c);
            Taper(NpcBone.Hips, new Vector3(0f, hem + 0.02f, 0f), w + flare * 2f + 0.02f, d + flare * 1.6f + 0.02f, w + flare * 2f + 0.01f, d + flare * 1.6f + 0.01f, 0.04f, band);
        }

        // ---- the upper body ----------------------------------------------------------------------------------
        private void Torso()
        {
            float w = p.torsoW, d = p.torsoD;
            float mid = p.chestY, top = p.shoulderY + 0.03f, low = p.waistY;
            // The lower half rides on the spine, the upper on the chest (belt and collar hide the seams).
            Color lowerCol = a.outfit == NpcOutfit.Tunic || a.outfit == NpcOutfit.Coat || a.outfit == NpcOutfit.Vest || a.outfit == NpcOutfit.Robe ? a.main : a.shirt;
            Color upperCol = a.outfit == NpcOutfit.Apron || a.outfit == NpcOutfit.Tabard || a.outfit == NpcOutfit.Armor ? a.shirt : a.main;
            Box(NpcBone.Spine, new Vector3(0f, (low + mid) * 0.5f, 0f), new Vector3(w, mid - low + 0.02f, d), lowerCol);
            Taper(NpcBone.Chest, new Vector3(0f, (mid + top) * 0.5f, 0f), w, d, w * 1.02f, d * 0.95f, top - mid, upperCol);
            float front = d * 0.5f + 0.004f;
            switch (a.outfit)
            {
                case NpcOutfit.Vest:
                    // The shirt showing in a V under the vest, with its white collar.
                    Taper(NpcBone.Chest, new Vector3(0f, top - 0.1f, front), 0.012f, 0.012f, 0.16f, 0.012f, 0.18f, a.shirt);
                    Taper(NpcBone.Chest, new Vector3(0f, top - 0.045f, front + 0.006f), 0.03f, 0.012f, 0.14f, 0.012f, 0.06f, Shade(a.shirt, 1.12f));
                    break;
                case NpcOutfit.Tunic:
                    Taper(NpcBone.Chest, new Vector3(0f, top - 0.06f, front), 0.012f, 0.012f, 0.1f, 0.012f, 0.1f, a.shirt);
                    break;
                case NpcOutfit.Coat:
                case NpcOutfit.Robe:
                    // A lighter panel down the front, the shirt at the neck, trim along the V and the edges.
                    Box(NpcBone.Spine, new Vector3(0f, (low + mid) * 0.5f, front), new Vector3(w * 0.3f, mid - low, 0.012f), Shade(a.main, 1.22f));
                    Taper(NpcBone.Chest, new Vector3(0f, (mid + top) * 0.5f, front), w * 0.3f, 0.012f, w * 0.34f, 0.012f, top - mid - 0.04f, Shade(a.main, 1.22f));
                    Taper(NpcBone.Chest, new Vector3(0f, top - 0.07f, front + 0.004f), 0.012f, 0.012f, 0.13f, 0.012f, 0.13f, a.shirt);
                    if (a.trim)
                        foreach (float s in new[] { 1f, -1f })
                        {
                            Box(NpcBone.Chest, new Vector3(s * 0.045f, top - 0.08f, front + 0.008f), new Vector3(0.025f, 0.16f, 0.012f), a.accent, Quaternion.Euler(0f, 0f, s * 22f));
                            Box(NpcBone.Spine, new Vector3(s * w * 0.16f, (low + mid) * 0.5f, front + 0.008f), new Vector3(0.025f, mid - low, 0.012f), a.accent);
                            Box(NpcBone.Chest, new Vector3(s * w * 0.17f, (mid + top) * 0.5f - 0.05f, front + 0.008f), new Vector3(0.025f, top - mid - 0.12f, 0.012f), a.accent);
                        }
                    break;
                case NpcOutfit.Apron:
                {
                    // The bib over the shirt, pockets, straps over the shoulders crossing on the back, brass rivets.
                    float bibTop = top - 0.1f;
                    Box(NpcBone.Spine, new Vector3(0f, (low + mid) * 0.5f, front + 0.01f), new Vector3(w * 0.78f, mid - low + 0.02f, 0.03f), a.main);
                    Box(NpcBone.Chest, new Vector3(0f, (mid + bibTop) * 0.5f, front + 0.01f), new Vector3(w * 0.62f, bibTop - mid, 0.03f), a.main);
                    foreach (float s in new[] { 1f, -1f })
                    {
                        Box(NpcBone.Spine, new Vector3(s * w * 0.2f, low + (mid - low) * 0.45f, front + 0.03f), new Vector3(w * 0.22f, (mid - low) * 0.45f, 0.015f), Shade(a.main, 0.8f));
                        Box(NpcBone.Chest, new Vector3(s * w * 0.25f, bibTop - 0.012f, front + 0.028f), new Vector3(0.035f, 0.035f, 0.012f), a.accent);
                        Box(NpcBone.Chest, new Vector3(s * w * 0.25f, (bibTop + top) * 0.5f + 0.01f, front + 0.005f), new Vector3(0.05f, top - bibTop + 0.03f, 0.02f), Shade(a.main, 0.85f));
                        Box(NpcBone.Chest, new Vector3(s * w * 0.25f, top + 0.01f, 0f), new Vector3(0.05f, 0.02f, d + 0.02f), Shade(a.main, 0.85f));
                        Box(NpcBone.Chest, new Vector3(0f, (mid + top) * 0.5f, -(d * 0.5f + 0.012f)), new Vector3(0.05f, top - mid + 0.1f, 0.02f), Shade(a.main, 0.85f), Quaternion.Euler(0f, 0f, s * 28f));
                    }
                    Box(NpcBone.Spine, new Vector3(0f, (low + mid) * 0.5f, -(d * 0.5f + 0.01f)), new Vector3(w * 0.7f, mid - low, 0.025f), a.main);
                    break;
                }
                case NpcOutfit.Tabard:
                case NpcOutfit.Armor:
                {
                    Color tab = a.main;
                    foreach (float z in new[] { 1f, -1f })
                    {
                        Box(NpcBone.Spine, new Vector3(0f, (low + mid) * 0.5f, z * (d * 0.5f + 0.012f)), new Vector3(w * 0.52f, mid - low + 0.02f, 0.025f), tab);
                        Box(NpcBone.Chest, new Vector3(0f, (mid + top) * 0.5f - 0.02f, z * (d * 0.5f + 0.012f)), new Vector3(w * 0.52f, top - mid - 0.04f, 0.025f), tab);
                    }
                    Box(NpcBone.Chest, new Vector3(0f, mid + 0.02f, front + 0.025f), new Vector3(0.08f, 0.08f, 0.012f), a.accent, Quaternion.Euler(0f, 0f, 45f)); // emblem
                    if (a.outfit == NpcOutfit.Armor)
                        foreach (float s in new[] { 1f, -1f })
                            Box(NpcBone.Chest, new Vector3(s * (w * 0.5f + 0.01f), top - 0.06f, 0f), new Vector3(0.1f, 0.12f, d * 0.85f), Shade(a.shirt, 1.1f));
                    break;
                }
            }
            if (a.cape)
            {
                // A cape hung from the shoulders to the backs of the knees, with a mantle over the shoulders.
                float bottom = p.kneeY + 0.02f;
                Taper(NpcBone.Chest, new Vector3(0f, (top + bottom) * 0.5f, -(d * 0.5f + 0.035f)), w + 0.12f, 0.03f, w + 0.02f, 0.03f, top - bottom, a.extra);
                Box(NpcBone.Chest, new Vector3(0f, top - 0.01f, -0.01f), new Vector3(w + 0.08f, 0.06f, d + 0.05f), Shade(a.extra, 1.12f));
                if (a.trim) Box(NpcBone.Chest, new Vector3(0f, top - 0.02f, d * 0.5f + 0.03f), new Vector3(0.05f, 0.05f, 0.02f), a.accent);
            }
            // The neck.
            Box(NpcBone.Neck, new Vector3(0f, p.neckY + 0.02f, 0f), new Vector3(p.headW * 0.42f, 0.08f, p.headD * 0.42f), a.skin);
        }

        // ---- arms ----------------------------------------------------------------------------------------------
        private void Arms()
        {
            bool sleevesMain = a.outfit == NpcOutfit.Tunic || a.outfit == NpcOutfit.Robe;
            Color sleeve = a.outfit == NpcOutfit.Armor ? a.shirt : sleevesMain ? a.main : a.shirt;
            float armTop = p.shoulderY - 0.03f;
            foreach (float s in new[] { 1f, -1f })
            {
                var upper = s > 0 ? NpcBone.UpperArmR : NpcBone.UpperArmL;
                var lower = s > 0 ? NpcBone.LowerArmR : NpcBone.LowerArmL;
                var hand = s > 0 ? NpcBone.HandR : NpcBone.HandL;
                float x = s * p.armX;
                float elbow = armTop - p.upperArm, wrist = elbow - p.lowerArm;
                Box(upper, new Vector3(x, armTop - p.upperArm * 0.5f + 0.02f, 0f), new Vector3(p.armW + 0.015f, p.upperArm + 0.06f, p.armW + 0.015f), sleeve);
                if (a.outfit == NpcOutfit.Vest || a.outfit == NpcOutfit.Coat)
                    Box(upper, new Vector3(x, armTop + 0.005f, 0f), new Vector3(p.armW + 0.03f, 0.07f, p.armW + 0.03f), a.main); // vest/coat shoulder
                if (a.outfit == NpcOutfit.Armor || a.outfit == NpcOutfit.Tabard)
                    Box(upper, new Vector3(x + s * 0.01f, armTop - 0.02f, 0f), new Vector3(p.armW + 0.05f, 0.1f, p.armW + 0.05f), Shade(a.shirt, 1.12f)); // pauldron
                Box(lower, new Vector3(x, elbow - p.lowerArm * 0.5f + 0.01f, 0f), new Vector3(p.armW, p.lowerArm + 0.03f, p.armW), sleeve);
                if (a.bracers)
                    Box(lower, new Vector3(x, wrist + p.lowerArm * 0.38f, 0f), new Vector3(p.armW + 0.035f, p.lowerArm * 0.62f, p.armW + 0.035f), Shade(a.extra, 0.75f));
                else
                    Box(lower, new Vector3(x, wrist + 0.03f, 0f), new Vector3(p.armW + 0.03f, 0.055f, p.armW + 0.03f), Shade(sleeve, 1.12f)); // cuff
                Color handCol = a.gloves ? (a.outfit == NpcOutfit.Armor ? Shade(a.shirt, 0.9f) : Shade(a.extra, 0.6f)) : a.skin;
                Box(hand, new Vector3(x, wrist - p.hand * 0.5f + 0.005f, 0.005f), new Vector3(p.hand * 0.95f, p.hand * 1.05f, p.hand * 0.9f), handCol);
            }
        }

        // ---- the head and face ---------------------------------------------------------------------------------
        private float HeadBase => p.neckY + 0.04f;

        private void Head()
        {
            float hb = HeadBase, H = p.headH, W = p.headW, D = p.headD;
            var head = NpcBone.Head;
            Box(head, new Vector3(0f, hb + H * 0.5f, 0f), new Vector3(W, H, D), a.skin);
            float fz = D * 0.5f;
            // Ears: small blocks on humans and dwarves; long points swept up and back on elves.
            foreach (float s in new[] { 1f, -1f })
            {
                if (a.race == Race.Elf)
                    Taper(head, new Vector3(s * (W * 0.5f + 0.07f), hb + H * 0.6f, -0.015f), 0.075f, 0.035f, 0.01f, 0.02f, 0.21f, a.skin, Quaternion.Euler(-14f, 0f, -s * 55f));
                else
                    Box(head, new Vector3(s * (W * 0.5f + 0.014f), hb + H * 0.47f, -0.01f), new Vector3(0.03f, a.race == Race.Dwarf ? 0.1f : 0.085f, 0.07f), Shade(a.skin, 0.96f));
            }
            // Eyes, brows, nose.
            Color eye = new Color(0.14f, 0.1f, 0.08f);
            float eyeY = hb + H * (a.race == Race.Dwarf ? 0.55f : 0.54f);
            foreach (float s in new[] { 1f, -1f })
            {
                Box(head, new Vector3(s * W * 0.2f, eyeY, fz + 0.004f), new Vector3(0.035f, a.race == Race.Elf ? 0.042f : 0.05f, 0.012f), eye);
                if (a.race == Race.Dwarf)
                    Box(head, new Vector3(s * W * 0.2f, eyeY + 0.055f, fz + 0.012f), new Vector3(0.11f, 0.045f, 0.03f), a.beardColor, Quaternion.Euler(0f, 0f, s * -10f));
                else
                    Box(head, new Vector3(s * W * 0.2f, eyeY + 0.05f, fz + 0.006f), new Vector3(0.075f, 0.022f, 0.014f), a.race == Race.Elf ? Shade(a.hairColor, 0.8f) : a.hairColor, Quaternion.Euler(0f, 0f, s * (a.race == Race.Elf ? -8f : 0f)));
            }
            Vector3 nose = a.race == Race.Dwarf ? new Vector3(0.075f, 0.08f, 0.055f) : a.race == Race.Elf ? new Vector3(0.035f, 0.06f, 0.028f) : new Vector3(0.045f, 0.065f, 0.035f);
            Box(head, new Vector3(0f, hb + H * 0.43f, fz + nose.z * 0.5f), nose, Shade(a.skin, 0.95f));
            if (a.beard == NpcBeard.None || a.beard == NpcBeard.Mustache)
                Box(head, new Vector3(0f, hb + H * 0.27f, fz + 0.004f), new Vector3(0.07f, 0.014f, 0.01f), Shade(a.skin, 0.6f)); // mouth
        }

        private void Hair()
        {
            float hb = HeadBase, H = p.headH, W = p.headW, D = p.headD;
            var head = NpcBone.Head;
            Color c = a.hairColor;
            float topY = hb + H;
            switch (a.hair)
            {
                case NpcHair.Bald:
                    if (a.race == Race.Dwarf) Box(head, new Vector3(0f, hb + H * 0.36f, -D * 0.5f - 0.012f), new Vector3(W * 0.9f, H * 0.3f, 0.03f), c); // a fringe of hair round the back
                    return;
                case NpcHair.LongStraight:
                case NpcHair.LongBraid:
                {
                    // Elf: a cap of hair, locks falling in front of the ears to the jaw, and long hair down the back.
                    Box(head, new Vector3(0f, topY + 0.02f, -0.01f), new Vector3(W + 0.035f, 0.075f, D + 0.035f), c);
                    Box(head, new Vector3(0f, topY - 0.04f, D * 0.5f + 0.012f), new Vector3(W * 0.9f, 0.07f, 0.03f), c);
                    foreach (float s in new[] { 1f, -1f })
                        Box(head, new Vector3(s * (W * 0.5f + 0.014f), hb + H * 0.52f, 0.03f), new Vector3(0.035f, H * 0.95f, D * 0.6f), c);
                    if (a.hair == NpcHair.LongStraight)
                    {
                        float len = H + 0.34f;
                        Taper(head, new Vector3(0f, topY - len * 0.5f, -D * 0.5f - 0.02f), W * 0.62f, 0.05f, W + 0.03f, 0.05f, len, c);
                        Taper(head, new Vector3(0f, topY - len - 0.03f, -D * 0.5f - 0.02f), 0.01f, 0.045f, W * 0.62f, 0.045f, 0.08f, c); // the point
                    }
                    else
                    {
                        Box(head, new Vector3(0f, hb + H * 0.5f, -D * 0.5f - 0.02f), new Vector3(W + 0.03f, H, 0.04f), c);
                        for (int i = 0; i < 4; i++)
                            Box(head, new Vector3(0f, hb - 0.02f - i * 0.075f, -D * 0.5f - 0.03f), new Vector3(0.07f - i * 0.006f, 0.07f, 0.06f), Shade(c, i % 2 == 0 ? 1f : 0.88f));
                    }
                    return;
                }
            }
            // Human and dwarf styles share a cap, the back and short sideburns.
            Box(head, new Vector3(0f, topY + 0.03f, -0.01f), new Vector3(W + 0.035f, 0.09f, D + 0.035f), c);
            Box(head, new Vector3(0f, hb + H * 0.62f, -D * 0.5f - 0.015f), new Vector3(W + 0.035f, H * 0.8f, 0.04f), c);
            foreach (float s in new[] { 1f, -1f })
                Box(head, new Vector3(s * (W * 0.5f + 0.012f), hb + H * 0.7f, -0.02f), new Vector3(0.03f, H * 0.5f, D * 0.8f), c);
            switch (a.hair)
            {
                case NpcHair.Short:
                    Box(head, new Vector3(0f, topY - 0.02f, D * 0.5f + 0.012f), new Vector3(W * 0.95f, 0.06f, 0.035f), c);
                    break;
                case NpcHair.Swept:
                    Box(head, new Vector3(0f, topY + 0.03f, D * 0.5f - 0.01f), new Vector3(W * 0.95f, 0.1f, 0.08f), Shade(c, 1.06f), Quaternion.Euler(-18f, 0f, 0f));
                    break;
                case NpcHair.Fringe:
                    Box(head, new Vector3(0f, topY - 0.05f, D * 0.5f + 0.012f), new Vector3(W + 0.02f, 0.1f, 0.035f), c);
                    break;
                case NpcHair.Tied:
                    Box(head, new Vector3(0f, topY - 0.02f, D * 0.5f + 0.012f), new Vector3(W * 0.95f, 0.05f, 0.035f), c);
                    Box(head, new Vector3(0f, hb + H * 0.5f, -D * 0.5f - 0.06f), new Vector3(0.08f, 0.08f, 0.07f), c);
                    Box(head, new Vector3(0f, hb + H * 0.3f, -D * 0.5f - 0.055f), new Vector3(0.06f, 0.16f, 0.05f), Shade(c, 0.9f));
                    break;
                case NpcHair.Long:
                    Box(head, new Vector3(0f, topY - 0.02f, D * 0.5f + 0.012f), new Vector3(W * 0.95f, 0.05f, 0.035f), c);
                    Box(head, new Vector3(0f, hb + H * 0.25f, -D * 0.5f - 0.02f), new Vector3(W + 0.04f, H * 0.7f, 0.05f), c);
                    break;
            }
        }

        private void Beard()
        {
            if (a.beard == NpcBeard.None) return;
            float hb = HeadBase, H = p.headH, W = p.headW, D = p.headD;
            var head = NpcBone.Head;
            Color c = a.beardColor;
            float fz = D * 0.5f;
            // Mustache (every beard has one).
            float mY = hb + H * 0.33f;
            Box(head, new Vector3(0f, mY, fz + 0.022f), new Vector3(a.race == Race.Dwarf ? 0.24f : 0.15f, a.race == Race.Dwarf ? 0.055f : 0.04f, 0.04f), Shade(c, 1.05f));
            if (a.race == Race.Dwarf) // a drooping moustache, its ends hanging past the mouth
                foreach (float s in new[] { 1f, -1f })
                    Box(head, new Vector3(s * 0.105f, mY - 0.045f, fz + 0.075f), new Vector3(0.045f, 0.09f, 0.04f), Shade(c, 1.05f));
            if (a.beard == NpcBeard.Mustache) return;
            if (a.beard == NpcBeard.Goatee)
            {
                Box(head, new Vector3(0f, hb + H * 0.1f, fz + 0.01f), new Vector3(0.08f, 0.1f, 0.04f), c);
                return;
            }
            if (a.beard == NpcBeard.Full || a.beard == NpcBeard.Short)
            {
                // Round the jaw from ear to ear, fuller for Full, with the mouth a dark line in it.
                float t = a.beard == NpcBeard.Full ? 0.05f : 0.03f;
                float h = a.beard == NpcBeard.Full ? H * 0.44f : H * 0.34f;
                float bottom = hb + (a.beard == NpcBeard.Full ? -0.03f : 0.02f);
                Box(head, new Vector3(0f, bottom + h * 0.5f, fz + t * 0.5f - 0.005f), new Vector3(W + 0.02f, h, t), c);
                foreach (float s in new[] { 1f, -1f })
                    Box(head, new Vector3(s * (W * 0.5f + 0.008f), bottom + h * 0.5f + 0.04f, 0.03f), new Vector3(0.025f, h + 0.08f, D * 0.7f), c);
                Box(head, new Vector3(0f, hb + H * 0.26f, fz + t + 0.001f), new Vector3(0.06f, 0.015f, 0.008f), Shade(c, 0.55f));
                return;
            }
            // Dwarf beards: big, down onto the chest.
            float topY = hb + H * 0.34f, len = a.beard == NpcBeard.Big ? 0.44f : 0.38f;
            Taper(head, new Vector3(0f, topY - len * 0.5f, fz + 0.045f), 0.06f, 0.09f, W + 0.04f, 0.11f, len, c);
            Box(head, new Vector3(0f, hb + H * 0.18f, fz + 0.02f), new Vector3(W + 0.03f, H * 0.36f, 0.06f), c); // the jaw, ear to ear
            foreach (float s in new[] { 1f, -1f }) // along the jaw, below the ears
                Box(head, new Vector3(s * (W * 0.5f + 0.012f), hb + H * 0.2f, 0.07f), new Vector3(0.035f, H * 0.36f, D * 0.5f), c);
            Box(head, new Vector3(0f, hb + H * 0.26f, fz + 0.095f), new Vector3(0.07f, 0.016f, 0.01f), Shade(c, 0.5f));
            if (a.beard == NpcBeard.Forked)
                foreach (float s in new[] { 1f, -1f })
                    Taper(head, new Vector3(s * 0.07f, topY - len - 0.06f, fz + 0.05f), 0.015f, 0.04f, 0.08f, 0.06f, 0.14f, Shade(c, 0.92f));
            if (a.beard == NpcBeard.Braided)
                for (int i = 0; i < 3; i++)
                    Box(head, new Vector3(0f, topY - len - 0.03f - i * 0.06f, fz + 0.05f), new Vector3(0.06f - i * 0.01f, 0.055f, 0.05f), Shade(c, i % 2 == 0 ? 0.9f : 1f));
            else
                Taper(head, new Vector3(0f, topY - len - 0.04f, fz + 0.05f), 0.01f, 0.04f, 0.12f, 0.06f, 0.1f, Shade(c, 0.92f)); // the point
        }

        private void Headwear()
        {
            float hb = HeadBase, H = p.headH, W = p.headW, D = p.headD;
            var head = NpcBone.Head;
            float topY = hb + H;
            switch (a.headwear)
            {
                case NpcHeadwear.Hood:
                {
                    Color c = a.extra;
                    Box(head, new Vector3(0f, topY + 0.04f, -0.01f), new Vector3(W + 0.08f, 0.07f, D + 0.08f), c);
                    Box(head, new Vector3(0f, hb + H * 0.5f, -D * 0.5f - 0.04f), new Vector3(W + 0.08f, H + 0.1f, 0.05f), c);
                    foreach (float s in new[] { 1f, -1f })
                        Box(head, new Vector3(s * (W * 0.5f + 0.035f), hb + H * 0.5f, 0.0f), new Vector3(0.04f, H + 0.08f, D + 0.06f), c);
                    Box(head, new Vector3(0f, topY + 0.01f, D * 0.5f + 0.03f), new Vector3(W + 0.08f, 0.06f, 0.04f), Shade(c, 0.85f));
                    Taper(head, new Vector3(0f, topY - 0.02f, -D * 0.5f - 0.1f), 0.02f, 0.02f, 0.1f, 0.12f, 0.12f, c, Quaternion.Euler(-70f, 0f, 0f)); // the point
                    Box(NpcBone.Chest, new Vector3(0f, p.shoulderY + 0.01f, 0f), new Vector3(p.torsoW + 0.1f, 0.08f, p.torsoD + 0.08f), c); // cowl
                    break;
                }
                case NpcHeadwear.Hat:
                {
                    Color c = Shade(a.extra, 0.8f);
                    Box(head, new Vector3(0f, topY + 0.03f, 0f), new Vector3(W + 0.2f, 0.03f, D + 0.2f), c);
                    Box(head, new Vector3(0f, topY + 0.1f, 0f), new Vector3(W - 0.02f, 0.13f, D - 0.02f), c);
                    Box(head, new Vector3(0f, topY + 0.06f, 0f), new Vector3(W, 0.035f, D), a.accent);
                    Box(head, new Vector3(W * 0.3f, topY + 0.17f, -0.02f), new Vector3(0.02f, 0.16f, 0.05f), new Color(0.85f, 0.2f, 0.18f), Quaternion.Euler(-25f, 0f, -15f)); // feather
                    break;
                }
                case NpcHeadwear.Cap:
                {
                    Color c = Shade(a.main, 0.85f);
                    Box(head, new Vector3(0f, topY + 0.035f, -0.02f), new Vector3(W + 0.05f, 0.08f, D + 0.03f), c);
                    Box(head, new Vector3(0f, topY + 0.005f, D * 0.5f + 0.04f), new Vector3(W * 0.8f, 0.025f, 0.09f), Shade(c, 0.85f));
                    break;
                }
                case NpcHeadwear.Helmet:
                {
                    Color steel = new Color(0.6f, 0.62f, 0.67f);
                    Box(head, new Vector3(0f, topY + 0.03f, 0f), new Vector3(W + 0.05f, 0.09f, D + 0.05f), steel);
                    Box(head, new Vector3(0f, hb + H * 0.72f, -0.01f), new Vector3(W + 0.06f, H * 0.4f, D + 0.04f), Shade(steel, 0.92f));
                    Box(head, new Vector3(0f, hb + H * 0.72f, D * 0.5f + 0.02f), new Vector3(W + 0.06f, 0.05f, 0.02f), Shade(steel, 1.1f));
                    Box(head, new Vector3(0f, hb + H * 0.52f, D * 0.5f + 0.03f), new Vector3(0.035f, 0.14f, 0.02f), steel); // nasal
                    Box(head, new Vector3(0f, topY + 0.085f, 0f), new Vector3(0.04f, 0.03f, D * 0.7f), Shade(steel, 1.1f));
                    break;
                }
                case NpcHeadwear.Circlet:
                    Box(head, new Vector3(0f, hb + H * 0.82f, 0f), new Vector3(W + 0.045f, 0.025f, D + 0.045f), a.accent);
                    Box(head, new Vector3(0f, hb + H * 0.82f, D * 0.5f + 0.026f), new Vector3(0.035f, 0.035f, 0.012f), new Color(0.3f, 0.6f, 0.9f));
                    break;
            }
        }
    }
}
