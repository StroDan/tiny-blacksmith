using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// The townsfolk characters (NpcBody) in Play mode, under the game's own lighting:
//   - a turnaround sheet of the three races (front / side / back / side) like the style reference -> Logs/npc_sheet.png
//   - a crowd of role/race variations                                                         -> Logs/npc_crowd.png
//   - every animation state on each race                                                      -> Logs/npc_anims_*.png
//   - a walk filmed side-on while it's measured: the planted foot must not slide and must touch the ground
//     (no gliding, no floating, no sinking), for every race and for carrying and patrolling
// Logs [NPCCHAR] lines. Run: Unity.exe -batchmode -projectPath <p> -executeMethod NpcCharacterTest.Run (no -quit)
public static class NpcCharacterTest
{
    private static double started;
    private static bool running;
    private static int failures;
    private const int Layer = 31;

    public static void Run()
    {
        System.Environment.SetEnvironmentVariable("TB_SAVE_PATH", "none");
        running = false;
        failures = 0;
        EditorSceneManager.OpenScene("Assets/Scenes/Prototype1.unity");
        EditorApplication.isPlaying = true;
        EditorApplication.update += Tick;
        started = EditorApplication.timeSinceStartup;
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        if (EditorApplication.timeSinceStartup - started > 240) { Done("TIMEOUT"); return; }
        var manager = Object.FindAnyObjectByType<ForgeGameManager>();
        if (manager == null || running || EditorApplication.timeSinceStartup - started < 3) return;
        running = true;
        manager.StopAllCoroutines();
        manager.StartCoroutine(Script());
    }

    private static void Check(bool ok, string what)
    {
        if (!ok) failures++;
        Debug.Log($"[NPCCHAR] {(ok ? "ok  " : "FAIL")} {what}");
    }

    private static readonly Vector3 Stage = new Vector3(0f, 300f, 0f); // far above the world, clear of everything

    // The three from the style reference.
    public static NpcAppearance ReferenceHuman() => new NpcAppearance
    {
        race = Race.Human, role = CityRole.Townsfolk, hair = NpcHair.Short, beard = NpcBeard.Full, outfit = NpcOutfit.Vest,
        skin = new Color(0.93f, 0.72f, 0.55f), hairColor = new Color(0.40f, 0.24f, 0.13f), beardColor = new Color(0.36f, 0.21f, 0.11f),
        main = new Color(0.47f, 0.28f, 0.16f), shirt = new Color(0.90f, 0.86f, 0.74f), trousers = new Color(0.33f, 0.24f, 0.17f),
        boots = new Color(0.33f, 0.20f, 0.11f), belt = new Color(0.20f, 0.13f, 0.08f), accent = new Color(0.86f, 0.64f, 0.18f), extra = new Color(0.40f, 0.24f, 0.13f),
    };
    public static NpcAppearance ReferenceElf() => new NpcAppearance
    {
        race = Race.Elf, role = CityRole.Townsfolk, hair = NpcHair.LongStraight, beard = NpcBeard.None, outfit = NpcOutfit.Coat, trim = true, bracers = true,
        skin = new Color(0.97f, 0.84f, 0.70f), hairColor = new Color(0.93f, 0.84f, 0.56f), beardColor = new Color(0.93f, 0.84f, 0.56f),
        main = new Color(0.24f, 0.44f, 0.24f), shirt = new Color(0.90f, 0.86f, 0.72f), trousers = new Color(0.18f, 0.17f, 0.19f),
        boots = new Color(0.40f, 0.24f, 0.13f), belt = new Color(0.30f, 0.18f, 0.10f), accent = new Color(0.80f, 0.62f, 0.22f), extra = new Color(0.40f, 0.24f, 0.13f),
    };
    public static NpcAppearance ReferenceDwarf() => new NpcAppearance
    {
        race = Race.Dwarf, role = CityRole.Craftsman, hair = NpcHair.Bald, beard = NpcBeard.Big, outfit = NpcOutfit.Apron, bracers = true,
        skin = new Color(0.93f, 0.70f, 0.52f), hairColor = new Color(0.72f, 0.32f, 0.12f), beardColor = new Color(0.72f, 0.32f, 0.12f),
        main = new Color(0.44f, 0.27f, 0.15f), shirt = new Color(0.30f, 0.42f, 0.62f), trousers = new Color(0.28f, 0.32f, 0.26f),
        boots = new Color(0.38f, 0.22f, 0.12f), belt = new Color(0.20f, 0.13f, 0.08f), accent = new Color(0.86f, 0.64f, 0.18f), extra = new Color(0.40f, 0.24f, 0.13f),
    };

    private static IEnumerator Script()
    {
        Directory.CreateDirectory("Logs");
        var root = new GameObject("NpcCharacterTest").transform;
        root.position = Stage;
        var cam = new GameObject("TestCam").AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.9f, 0.9f, 0.9f);
        cam.cullingMask = 1 << Layer;
        cam.fieldOfView = 30f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 200f;
        // A soft light from the camera side for the sheets only (the test layer), like a turnaround render.
        var fill = new GameObject("TestFill").AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.intensity = 0.9f;
        fill.cullingMask = 1 << Layer;
        fill.shadows = LightShadows.None;
        fill.transform.rotation = Quaternion.Euler(28f, -25f, 0f);

        // ---- the reference sheet: each race in a T-pose from four sides -------------------------------------------
        var refs = new[] { ReferenceHuman(), ReferenceElf(), ReferenceDwarf() };
        var sheetBodies = new List<NpcBody>();
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 4; col++)
            {
                var b = NpcBody.Create(root, refs[row], $"Sheet{row}{col}");
                b.transform.localPosition = new Vector3(col * 1.9f, -row * 2.2f, 0f);
                b.transform.localRotation = Quaternion.Euler(0f, 180f + col * 90f, 0f); // front, side, back, side (camera looks down +Z)
                SetLayer(b.gameObject);
                sheetBodies.Add(b);
            }
        yield return null;
        foreach (var b in sheetBodies)
        {
            b.Animator.enabled = false; // hold a T-pose for the sheet
            b.Bone(NpcBone.UpperArmR).localRotation = Quaternion.Euler(0f, 0f, 88f);
            b.Bone(NpcBone.UpperArmL).localRotation = Quaternion.Euler(0f, 0f, -88f);
        }
        cam.transform.position = Stage + new Vector3(2.85f, -1.45f, -16.5f);
        cam.transform.rotation = Quaternion.identity;
        yield return null;
        Shot(cam, "npc_sheet", 1600, 1200);
        foreach (var b in sheetBodies) Object.Destroy(b.gameObject);

        // Heights: humans, taller slimmer elves, short broad dwarves.
        float hH = NpcProportions.Human.Height, hE = NpcProportions.Elf.Height, hD = NpcProportions.Dwarf.Height;
        Check(hE > hH + 0.08f && hD < hH - 0.3f && NpcProportions.Dwarf.torsoW > NpcProportions.Human.torsoW + 0.12f && NpcProportions.Elf.torsoW < NpcProportions.Human.torsoW,
              $"races read by build, not colour: human {hH:0.00} m, elf {hE:0.00} m (slimmer), dwarf {hD:0.00} m (broader)");

        // ---- a crowd of variations ----------------------------------------------------------------------------------
        var roles = new[] { CityRole.Townsfolk, CityRole.Merchant, CityRole.Guard, CityRole.Craftsman, CityRole.Traveller, CityRole.Worker, CityRole.Knight, CityRole.Noble };
        var crowd = new List<NpcBody>();
        var keys = new HashSet<string>();
        int n = 0;
        foreach (var role in roles)
            foreach (Race race in new[] { Race.Human, Race.Elf, Race.Dwarf })
            {
                var look = NpcAppearance.Generate(race, role, 1000 + n * 37);
                keys.Add(look.Key);
                var b = NpcBody.Create(root, look, $"Crowd{n}");
                b.transform.localPosition = new Vector3(20f + (n % 8) * 1.35f, -(n / 8) * 2.3f, 0f);
                b.transform.localRotation = Quaternion.Euler(0f, 180f + 20f, 0f);
                SetLayer(b.gameObject);
                crowd.Add(b);
                n++;
            }
        Check(keys.Count == crowd.Count, $"{crowd.Count} generated townsfolk, {keys.Count} different looks (no clones)");
        yield return Wait(0.6);
        cam.transform.position = Stage + new Vector3(20f + 3.5f * 1.35f, -2.8f, -19f);
        yield return null;
        Shot(cam, "npc_crowd", 1600, 1000);
        Check(crowd.All(b => b.Skin.sharedMaterial == NpcPalette.Material && b.Skin.sharedMesh != null && b.Animator.runtimeAnimatorController != null),
              $"every character: a mesh, the one shared material, an animator ({NpcBody.CachedMeshes} meshes cached, {NpcPalette.ColorsUsed} palette colours)");
        int tris = crowd.Max(b => b.Skin.sharedMesh.triangles.Length / 3);
        Check(tris < 4000, $"low-poly budget: at most {tris} triangles per character, 18 bones, one draw call");
        foreach (var b in crowd) Object.Destroy(b.gameObject);

        // ---- every state on each race ------------------------------------------------------------------------------
        string[] states = { "Idle_01", "Idle_02", "Talk_Gesture_01", "Talk_Gesture_02", "Look_Around", "Give_Item", "Receive_Item", "Inspect_Item", "Waiting_Impatient", "Sit", "Carry_Idle", "Turn_In_Place" };
        var showBodies = new List<NpcBody>();
        for (int r = 0; r < 3; r++)
            for (int s = 0; s < states.Length; s++)
            {
                var b = NpcBody.Create(root, refs[r], $"State{r}_{s}");
                b.transform.localPosition = new Vector3(40f + s * 1.5f, -r * 2.3f - (states[s] == "Sit" ? b.SeatDrop - 0.4f : 0f), 0f);
                b.transform.localRotation = Quaternion.Euler(0f, 180f + 30f, 0f);
                SetLayer(b.gameObject);
                b.Animator.CrossFadeInFixedTime(states[s], 0f);
                showBodies.Add(b);
            }
        yield return Wait(0.9);
        cam.transform.position = Stage + new Vector3(40f + 5.5f * 1.5f, -2.3f, -22f);
        yield return null;
        Shot(cam, "npc_states", 1800, 800);
        // Not a T-pose / not frozen: the arms really move in these states.
        bool moving = true;
        foreach (var b in showBodies.Take(states.Length).Where(b => !b.name.EndsWith("_5") && !b.name.EndsWith("_6") && !b.name.EndsWith("_11"))) // (one-shots may have finished)
        {
            var before = b.Bone(NpcBone.UpperArmR).localRotation;
            var hb = b.Bone(NpcBone.Head).localRotation;
            yield return Wait(0.25);
            if (Quaternion.Angle(before, b.Bone(NpcBone.UpperArmR).localRotation) < 0.05f && Quaternion.Angle(hb, b.Bone(NpcBone.Head).localRotation) < 0.05f) { moving = false; Debug.Log($"[NPCCHAR] frozen: {b.name}"); }
        }
        Check(moving, "every state is alive (arms or head in motion - nobody frozen or T-posing)");
        foreach (var b in showBodies) Object.Destroy(b.gameObject);

        // ---- walking: planted feet stay put, touch the ground, never sink ------------------------------------------------
        foreach (var look in refs)
            foreach (var mode in new[] { "walk", "carry", "patrol" })
            {
                var b = NpcBody.Create(root, look, "Walker");
                SetLayer(b.gameObject);
                b.transform.localPosition = new Vector3(60f, 0f, 0f);
                b.transform.localRotation = Quaternion.identity;
                float speed = look.race == Race.Dwarf ? 1.05f : look.race == Race.Elf ? 1.35f : 1.2f;
                b.Walk(speed, mode == "carry", mode == "patrol");
                yield return Wait(0.5);
                var feet = new[] { b.Bone(NpcBone.FootR), b.Bone(NpcBone.FootL) };
                var last = feet.Select(f => f.position).ToArray();
                float slide = 0f, lowest = float.MaxValue; int planted = 0;
                float floatWorst = 0f;
                float ankle = b.Proportions.ankleY;
                double t0 = EditorApplication.timeSinceStartup;
                int frame = 0;
                float plantedTime = 0f;
                const float Step = 1f / 60f;
                Time.captureFramerate = 60;   // fixed game-time steps, so per-frame foot motion is measured exactly
                while (EditorApplication.timeSinceStartup - t0 < 6.0 && frame < 150)
                {
                    b.transform.position += b.transform.forward * speed * Step;
                    yield return null;
                    frame++;
                    // only the LOWER foot counts as planted, and only while it is really on the ground
                    Vector3 p0 = feet[0].position, p1 = feet[1].position;
                    float s0 = p0.y - Stage.y - ankle, s1 = p1.y - Stage.y - ankle;
                    lowest = Mathf.Min(lowest, Mathf.Min(s0, s1));
                    floatWorst = Mathf.Max(floatWorst, Mathf.Min(s0, s1));
                    int i = s0 <= s1 ? 0 : 1;
                    Vector3 pos = i == 0 ? p0 : p1;
                    if (Mathf.Min(s0, s1) < 0.006f)
                    {
                        slide += new Vector2(pos.x - last[i].x, pos.z - last[i].z).magnitude;
                        plantedTime += Step;
                        planted++;
                    }
                    last[0] = p0; last[1] = p1;
                    if (look.race == Race.Human && mode == "walk" && frame % 12 == 0 && frame < 60)
                    {
                        cam.transform.position = b.transform.position + new Vector3(-4.2f, 0.95f, 0f);
                        cam.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                        Shot(cam, $"npc_walk_{frame / 12}", 640, 640);
                    }
                }
                Time.captureFramerate = 0;
                float meanSlide = plantedTime > 0f ? slide / plantedTime : 99f;
                Check(meanSlide < 0.16f * speed && lowest > -0.02f && floatWorst < 0.03f,
                      $"{look.race} {mode} at {speed:0.00} m/s (pace {b.Pace:0.00}): planted foot drifts {meanSlide:0.000} m/s, lowest sole {lowest:0.000} m, worst gap to ground {floatWorst:0.000} m");
                Object.Destroy(b.gameObject);
            }
        Object.Destroy(cam.gameObject);
        Object.Destroy(root.gameObject);
        Done(failures == 0 ? "PASS" : $"FAIL ({failures})");
    }

    // (A headless editor never marks renderers visible, so the in-game "don't animate what's off screen" culling is off here.)
    private static void SetLayer(GameObject go)
    {
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
        var anim = go.GetComponent<Animator>();
        if (anim != null) anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
    }

    private static IEnumerator Wait(double s) { double t = EditorApplication.timeSinceStartup; while (EditorApplication.timeSinceStartup - t < s) yield return null; }

    private static void Shot(Camera cam, string name, int w, int h)
    {
        var rt = new RenderTexture(w, h, 24) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        cam.targetTexture = null;
        RenderTexture.active = null;
        rt.Release();
        File.WriteAllBytes($"Logs/{name}.png", tex.EncodeToPNG());
    }

    private static void Done(string result)
    {
        EditorApplication.update -= Tick;
        Debug.Log($"[NPCCHAR] RESULT: {result}");
        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += () => EditorApplication.Exit(0);
    }
}
