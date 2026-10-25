using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Composition audit, rendered in Play mode (Logs/comp_*.png; never touches the real save):
//   env TB_COMP = "seat" (default) - the seat camera all the way round (8 directions) at midday, plus two top-down maps
//                 "evening"        - the same seat views at 19:30
//                 "menu"           - the main menu's title camera
// Run: Unity.exe -batchmode -projectPath <p> -executeMethod CompositionAudit.Run   (no -quit)
public static class CompositionAudit
{
    private static double started;
    private static bool running;
    private static string mode;
    private static MonoBehaviour host;

    public static void Run()
    {
        System.Environment.SetEnvironmentVariable("TB_SAVE_PATH", "none");
        mode = System.Environment.GetEnvironmentVariable("TB_COMP") ?? "seat";
        running = false;
        host = null;
        EditorSceneManager.OpenScene(mode == "menu" ? "Assets/Scenes/MainMenu.unity" : "Assets/Scenes/Prototype1.unity");
        EditorApplication.isPlaying = true;
        EditorApplication.update += Tick;
        started = EditorApplication.timeSinceStartup;
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        if (EditorApplication.timeSinceStartup - started > 240) { Done("TIMEOUT"); return; }
        if (running || EditorApplication.timeSinceStartup - started < 4) return;
        host = mode == "menu" ? (MonoBehaviour)Object.FindAnyObjectByType<MainMenu>() : Object.FindAnyObjectByType<ForgeGameManager>();
        if (host == null) return;
        running = true;
        if (host is ForgeGameManager gm) gm.StopAllCoroutines(); // we pose the camera ourselves
        host.StartCoroutine(mode == "menu" ? Menu() : Seat(mode == "evening" ? 19.5f : 12f));
    }

    private static IEnumerator Menu()
    {
        for (int i = 0; i < 30; i++) yield return null;
        var ui = GameObject.Find("MainMenuUI") ?? GameObject.Find("MenuUI");
        Shot("comp_menu", Camera.main, true);
        if (ui != null) ui.SetActive(false);
        yield return null;
        Shot("comp_menu_noui", Camera.main, false);
        Done("done");
    }

    private static IEnumerator Seat(float hour)
    {
        var gm = (ForgeGameManager)host;
        var cam = Camera.main;
        var clock = gm.Clock;
        clock.Hour = hour;
        Vector3 seat = cam.transform.position;
        // Let the townsfolk come out first.
        Time.timeScale = 6f;
        double until = EditorApplication.timeSinceStartup + 12;
        while (EditorApplication.timeSinceStartup < until) { clock.Hour = hour; yield return null; }
        Time.timeScale = 1f;
        string tag = hour < 15f ? "noon" : "eve";
        foreach (float yaw in new[] { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f })
        {
            cam.transform.SetPositionAndRotation(seat, Quaternion.Euler(12f, yaw, 0f));
            yield return null;
            clock.Hour = hour;
            yield return null;
            Shot($"comp_{tag}_yaw{yaw:000}", cam, false);
        }
        // Close-ups for checking construction (not player views): the watermill and its wheel, the tannery bank.
        foreach (var (name, from, to) in new[]
        {
            ("mill", new Vector3(7f, 3.2f, 12f), new Vector3(16f, 1.8f, 22f)),
            ("tannery", new Vector3(1f, 3.5f, -31f), new Vector3(9f, 0.8f, -42f)),
            ("stonebridge", new Vector3(4f, 3f, 32f), new Vector3(13f, 0.6f, 41.5f)),
            // The landmarks (sheets 01-04): the plaza from the avenue, the church, the clock house, the castle on its hill.
            ("plaza", new Vector3(49f, 4f, -3.5f), new Vector3(63f, 2f, -3.5f)),
            ("fountain", new Vector3(57f, 2.2f, -8.5f), new Vector3(63f, 1.8f, -3.5f)),
            ("church", new Vector3(55f, 3f, -8f), new Vector3(63f, 9f, 16f)),
            ("clockhouse", new Vector3(56f, 3f, 3f), new Vector3(63f, 9f, -20f)),
            ("castle", new Vector3(92f, 16f, -3.5f), new Vector3(178f, 38f, -3.5f)),
            // The riverfront pass: the new bridges, the market quay, the castle hill's road side, the backdrop.
            ("southbridge", new Vector3(11.8f, 5f, -11f), new Vector3(11.5f, 0.5f, -27f)),
            ("townbridge", new Vector3(14f, 8f, 45f), new Vector3(16f, 1f, 64f)),
            ("marketquay", new Vector3(7f, 5f, 21f), new Vector3(19.5f, 1f, 33f)),
            ("castlehill", new Vector3(104f, 34f, -70f), new Vector3(170f, 18f, -3.5f)),
            ("mountains", new Vector3(20f, 30f, -3.5f), new Vector3(600f, 90f, 120f)),
        })
        {
            cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(to - from));
            yield return null;
            Shot($"comp_{tag}_close_{name}", cam, false);
        }
        cam.transform.position = seat;
        if (hour < 15f)
        {
            // Top-down maps: the neighbourhood, and the whole world.
            var top = new GameObject("TopCam").AddComponent<Camera>();
            top.orthographic = true;
            top.nearClipPlane = 1f; top.farClipPlane = 600f;
            top.transform.SetPositionAndRotation(new Vector3(10f, 300f, -3f), Quaternion.Euler(90f, 0f, 0f));
            top.orthographicSize = 70f;
            yield return null;
            Shot("comp_map_near", top, false, 1024, 1024);
            top.transform.position = new Vector3(20f, 300f, -3f);
            top.orthographicSize = 230f;
            yield return null;
            Shot("comp_map_world", top, false, 1024, 1024);
        }
        Done("done");
    }

    private static void Shot(string name, Camera cam, bool withUi, int w = 1280, int h = 720)
    {
        Canvas canvas = null;
        RenderMode old = RenderMode.ScreenSpaceOverlay;
        if (withUi)
        {
            canvas = Object.FindAnyObjectByType<Canvas>();
            if (canvas != null) { old = canvas.renderMode; canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = 0.5f; Canvas.ForceUpdateCanvases(); }
        }
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.DefaultHDR);
        cam.targetTexture = rt;
        cam.Render();
        var ldr = new RenderTexture(w, h, 0);
        Graphics.Blit(rt, ldr);
        RenderTexture.active = ldr;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        cam.targetTexture = null;
        RenderTexture.active = null;
        if (canvas != null) canvas.renderMode = old;
        Directory.CreateDirectory("Logs");
        File.WriteAllBytes($"Logs/{name}.png", tex.EncodeToPNG());
    }

    private static void Done(string reason)
    {
        EditorApplication.update -= Tick;
        Time.timeScale = 1f;
        Debug.Log($"[COMP] RESULT: {reason}");
        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += () => EditorApplication.Exit(0);
    }
}
