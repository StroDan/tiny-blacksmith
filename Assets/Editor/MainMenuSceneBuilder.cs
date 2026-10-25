using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Builds Assets/Scenes/MainMenu.unity (menu: Tiny Blacksmith > Build Main Menu Scene).
// The background is the real game world: the workshop scene (village, trees, the smith's forge - everything,
// including anything hand-placed there) is copied, the game itself is taken out (ForgeGameManager and its
// workpiece), and the copy is dressed for a title screen: golden-hour sun, a ring of low-poly mountains and
// hills, a few clouds, a warm light in the forge and a camera looking across the smithy toward the village.
// MainMenu (runtime) adds the moving parts - forge fire and smoke, chimney smoke, drifting clouds.
// Re-run it after changing the workshop scene to refresh the menu's copy. It also makes MainMenu the first
// scene in Build Settings, so the game starts there.
public static class MainMenuSceneBuilder
{
    private const string MenuPath = "Assets/Scenes/MainMenu.unity";
    private const string GamePath = "Assets/Scenes/Prototype1.unity";
    private const string MeshDir = "Assets/Models/Generated/Menu";

    // Where the title camera stands: south-west of the smithy, a little above head height, looking over the
    // forge toward the northern houses, the stream and the mountains beyond.
    // (Re-framed for the city: from just above and behind the smithy, the forge sits left of centre - clear of the
    // menu panel on the left - the city runs away beyond it, and the castle stands upper right, the mountains far behind.)
    // (Raised 3.5 m for the riverfront pass so the river and its quay show over the smithy's roofs, like the menu art.)
    public static Vector3 CameraPosition = new Vector3(-17f, 13f, -10.5f);
    public static Vector3 CameraTarget = new Vector3(-17f + 60f * 0.8954f, 13f - 60f * 0.0872f, -10.5f + 60f * 0.4367f); // yaw 64, pitch 5 down
    public const float CameraFov = 52f;

    [MenuItem("Tiny Blacksmith/Build Main Menu Scene")]
    public static void BuildFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Build();
    }

    // Unity.exe -batchmode -projectPath <p> -executeMethod MainMenuSceneBuilder.Build -quit
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.CreateFolder("Assets/Models/Generated", "Menu");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MenuPath) != null) AssetDatabase.DeleteAsset(MenuPath);
        if (!AssetDatabase.CopyAsset(GamePath, MenuPath)) { Debug.LogError("[Menu] couldn't copy the workshop scene"); return; }
        var scene = EditorSceneManager.OpenScene(MenuPath);

        // The game itself stays in the workshop scene.
        foreach (var m in Object.FindObjectsByType<ForgeGameManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Object.DestroyImmediate(m.gameObject);
        var workpiece = GameObject.Find("Workpiece");
        if (workpiece != null) Object.DestroyImmediate(workpiece);

        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        var cam = Camera.main;
        cam.transform.SetPositionAndRotation(CameraPosition, Quaternion.LookRotation(CameraTarget - CameraPosition));
        cam.fieldOfView = CameraFov;
        cam.farClipPlane = 2600f; // the high range stands ~1.1 km out
        // Aerial haze (as the game's daytime haze): the castle stays crisp, each mountain range beyond goes paler and bluer.
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.70f, 0.72f, 0.86f);
        RenderSettings.fogStartDistance = 150f;
        RenderSettings.fogEndDistance = 1450f;

        // Golden hour: a low, warm sun from behind the camera's left shoulder.
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (l.type != LightType.Directional) continue;
            float yaw = cam.transform.eulerAngles.y + 28f;
            l.transform.rotation = Quaternion.Euler(24f, yaw, 0f);
            l.color = new Color(1f, 0.80f, 0.58f);
            l.intensity = Mathf.Max(l.intensity, 1.1f);
        }

        var root = new GameObject("MenuBackdrop").transform;
        // (The mountains come with the copied game scene now - City/Mountains, VillageBuilder.Backdrop.cs.)
        // (The old ring of menu hills at ~90 m now stood inside the city; the game scene's own countryside ring, copied
        // in with everything else, sits between the castle and the mountains instead.)
        var clouds = new GameObject("Clouds").transform;
        clouds.SetParent(root, false);
        var rng = new System.Random(4);
        for (int i = 0; i < 9; i++)
        {
            var cloud = Place(clouds, "Cloud" + i, Cloud(rng), lit);
            float a = (float)(rng.NextDouble() * 360.0) * Mathf.Deg2Rad;
            float r = 70f + (float)rng.NextDouble() * 90f;
            cloud.transform.position = new Vector3(Mathf.Cos(a) * r, 34f + (float)rng.NextDouble() * 18f, Mathf.Sin(a) * r);
            cloud.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            foreach (var mr in cloud.GetComponentsInChildren<MeshRenderer>()) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // A warm light glowing out of the firebox (MainMenu flickers it and puts it right over the coals).
        var glow = new GameObject("ForgeGlow").AddComponent<Light>();
        glow.transform.SetParent(root, false);
        var forge = Object.FindAnyObjectByType<ForgeStation>();
        glow.transform.position = (forge != null ? forge.transform.position : Vector3.zero) + Vector3.up * 1.0f;
        glow.type = LightType.Point;
        glow.color = new Color(1f, 0.55f, 0.2f);
        glow.range = 6f;
        glow.intensity = 4f;
        glow.shadows = LightShadows.None;

        var menu = new GameObject("MainMenu").AddComponent<MainMenu>();
        menu.clouds = clouds;
        menu.forgeGlow = glow;

        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        // The game starts at the menu; the workshop is loaded from it.
        var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(MenuPath, true), new EditorBuildSettingsScene(GamePath, true) };
        foreach (var s in EditorBuildSettings.scenes)
            if (s.path != MenuPath && s.path != GamePath) scenes.Add(s);
        EditorBuildSettings.scenes = scenes.ToArray();
        Debug.Log("[Menu] MainMenu scene built; build order: " + string.Join(", ", scenes.ConvertAll(s => s.path)));
    }

    private static GameObject Place(Transform parent, string name, LowPolyModel model, Shader lit)
    {
        string path = $"{MeshDir}/{name}.asset";
        if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(model.mesh, path);
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        model.Attach(go, lit);
        return go;
    }

    // A cloud: a few flattened white puffs.
    private static LowPolyModel Cloud(System.Random rng)
    {
        var b = new MeshBuilder();
        int white = b.Paint(new Color(0.98f, 0.97f, 0.95f), 0.0f, 0.45f); // a little self-lit, so clouds stay white when back-lit
        int shade = b.Paint(new Color(0.86f, 0.87f, 0.92f), 0.0f, 0.35f);
        int puffs = 3 + rng.Next(3);
        for (int i = 0; i < puffs; i++)
        {
            var at = new Vector3((i - puffs * 0.5f) * 3.2f + (float)rng.NextDouble(), (float)rng.NextDouble() * 1.2f, (float)(rng.NextDouble() - 0.5) * 3f);
            b.Ball(at, 2.6f + (float)rng.NextDouble() * 1.8f, i % 2 == 0 ? white : shade, 0.55f);
        }
        return b.Build("Cloud");
    }
}
