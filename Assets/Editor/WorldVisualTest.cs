using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// The city events as the player SEES them (Play mode, never touches the real save): every event forced in turn, the
// seat's four views by day (and the lantern-lit ones at night), plus a top-down map of where the decorations landed -
// to catch props in walls, on the river, floating, or across a path. Also logs crowd size and the HUD/morning lines.
// Screenshots -> Logs/event_*.png. Run: Unity.exe -batchmode -projectPath <p> -executeMethod WorldVisualTest.Run (no -quit)
// Optional env TB_EVENTS = comma list of CityEventType names to limit it.
public static class WorldVisualTest
{
    private static ForgeGameManager manager;
    private static double started;
    private static bool running;
    private static int failures;

    public static void Run()
    {
        System.Environment.SetEnvironmentVariable("TB_SAVE_PATH", "none");
        manager = null;
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
        if (EditorApplication.timeSinceStartup - started > 420) { Done("TIMEOUT"); return; }
        if (manager == null) manager = Object.FindAnyObjectByType<ForgeGameManager>();
        if (manager == null || running || EditorApplication.timeSinceStartup - started < 3) return;
        running = true;
        manager.StopAllCoroutines();
        manager.StartCoroutine(Audit());
    }

    private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    private static T Field<T>(string name) => (T)typeof(ForgeGameManager).GetField(name, Flags).GetValue(manager);

    private static void Check(bool ok, string what)
    {
        if (!ok) failures++;
        Debug.Log($"[EVT] {(ok ? "ok  " : "FAIL")} {what}");
    }

    private static IEnumerator Audit()
    {
        var cam = Camera.main;
        var ui = Field<ForgeUI>("ui");
        ui.ShowOrder(false);
        ui.instructions.text = "";
        var clock = manager.Clock;
        Vector3 seat = cam.transform.position;
        var decor = Object.FindAnyObjectByType<CityEventDecor>();
        Check(decor != null, "the event decoration system is in the scene");
        foreach (var r in manager.City.GetComponentsInChildren<Animator>(true)) r.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        string only = System.Environment.GetEnvironmentVariable("TB_EVENTS");
        var w = manager.workshop;
        foreach (var info in CityEvents.All)
        {
            if (info.type == CityEventType.None) continue;
            if (!string.IsNullOrEmpty(only) && !only.Contains(info.type.ToString())) continue;
            WorldDirector.ForceEvent(w, info.type);
            manager.DebugRefreshWorld();
            clock.Hour = 13f;
            for (int f = 0; f < 240; f++) { clock.Hour = 13f; yield return null; } // let the crowd adjust
            string lines = string.Join(" / ", WorldDirector.MorningLines(w));
            Debug.Log($"[EVT] {info.name}: props {decor.PropCount} ({decor.RejectStats}), walkers {manager.City.ActiveCount}, guards {manager.City.GuardsOut}, weather {WorldDirector.Weather(w)} - \"{lines}\"");
            if (info.decor != EventDecor.None) Check(decor.PropCount >= 4, $"{info.name}: its decorations are up ({decor.PropCount} props)");
            Check(ui.hud.text.Contains(info.name), $"{info.name}: named on the HUD");
            foreach (float yaw in new[] { 90f, 0f, 180f, 270f })
            {
                cam.transform.SetPositionAndRotation(seat, Quaternion.Euler(12f, yaw, 0f));
                clock.Hour = 13f;
                yield return null;
                Shot($"event_{info.type}_yaw{yaw:0}", cam);
            }
            if ((info.decor & EventDecor.Lanterns) != 0)
            {
                clock.Hour = 21.5f;
                for (int f = 0; f < 30; f++) { clock.Hour = 21.5f; yield return null; }
                cam.transform.SetPositionAndRotation(seat, Quaternion.Euler(10f, 90f, 0f));
                yield return null;
                Shot($"event_{info.type}_night", cam);
            }
            if (info.decor != EventDecor.None) MapShot($"event_{info.type}_map", seat);
            if (info.type == CityEventType.Festival)
                Debug.Log($"[EVT] blockers in the table view: {decor.Blockers(90f, 45f)}\nseat {seat}\n{decor.ReasonMap(new Vector2(2f, -14f), new Vector2(32f, 8f), 1f, 0.3f)}");
        }
        // Back to an ordinary day: nothing left standing.
        WorldDirector.ForceEvent(w, CityEventType.None);
        manager.DebugRefreshWorld();
        yield return null;
        Check(decor.PropCount == 0 && decor.ShownType == CityEventType.None, "an ordinary day takes the decorations down");
        Done(failures == 0 ? "PASS" : $"FAIL ({failures})");
    }

    private static void MapShot(string name, Vector3 seat)
    {
        var go = new GameObject("MapCam");
        var c = go.AddComponent<Camera>();
        c.orthographic = true;
        c.orthographicSize = 42f;
        c.transform.SetPositionAndRotation(seat + new Vector3(22f, 120f, 0f), Quaternion.Euler(90f, 0f, 0f));
        c.farClipPlane = 400f;
        Shot(name, c);
        Object.DestroyImmediate(go);
    }

    private static void Shot(string name, Camera cam)
    {
        var canvas = GameObject.Find("ForgeUI").GetComponent<Canvas>();
        var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.DefaultHDR);
        bool main = cam == Camera.main;
        if (main)
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 0.5f;
            Canvas.ForceUpdateCanvases();
        }
        cam.targetTexture = rt;
        cam.Render();
        var ldr = new RenderTexture(1280, 720, 0);
        Graphics.Blit(rt, ldr);
        RenderTexture.active = ldr;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        tex.Apply();
        cam.targetTexture = null;
        RenderTexture.active = null;
        if (main) canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        Directory.CreateDirectory("Logs");
        File.WriteAllBytes($"Logs/{name}.png", tex.EncodeToPNG());
    }

    private static void Done(string reason)
    {
        EditorApplication.update -= Tick;
        Debug.Log($"[EVT] RESULT: {reason}");
        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += () => EditorApplication.Exit(0);
    }
}
