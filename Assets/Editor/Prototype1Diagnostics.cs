using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// One-off headless diagnostic: opens Prototype1, enters Play mode, waits a
// few frames, logs the state of the key runtime objects, then exits.
// Run via: Unity.exe -batchmode -projectPath <path> -executeMethod Prototype1Diagnostics.RunTest
// (do NOT also pass -quit - this script calls EditorApplication.Exit itself
// once the diagnostic is done.)
public static class Prototype1Diagnostics
{
    private static int frame;

    [MenuItem("Tiny Blacksmith/Run Headless Diagnostic")]
    public static void RunTest()
    {
        frame = 0;
        EditorSceneManager.OpenScene("Assets/Scenes/Prototype1.unity");
        EditorApplication.isPlaying = true;
        EditorApplication.update += Tick;
    }

    private const int FramesToWait = 200; // a few seconds, to catch anything that only shows up after Update() has run a while

    private static void Tick()
    {
        if (!EditorApplication.isPlaying)
            return; // still waiting for play mode to actually start

        frame++;
        if (frame < FramesToWait)
            return;

        EditorApplication.update -= Tick;

        LogState();

        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += () => EditorApplication.Exit(0);
    }

    private static void LogState()
    {
        var manager = Object.FindAnyObjectByType<ForgeGameManager>();
        if (manager == null)
        {
            Debug.LogError("[DIAG] No ForgeGameManager found in scene!");
            return;
        }

        Debug.Log($"[DIAG] CurrentStage={manager.CurrentStage}");
        Debug.Log($"[DIAG] inventory={manager.inventory.Count}");

        var cam = Camera.main;
        Debug.Log($"[DIAG] Camera.main={(cam != null ? cam.name : "NULL")} tag={(cam != null ? cam.tag : "n/a")}");

        var mouse = UnityEngine.InputSystem.Mouse.current;
        Debug.Log($"[DIAG] Mouse.current={(mouse != null ? "present" : "NULL (no mouse device registered in this headless run)")}");

        CaptureScreenshot(cam);
    }

    private static void CaptureScreenshot(Camera cam)
    {
        if (cam == null)
        {
            Debug.LogError("[DIAG] Can't screenshot: no camera.");
            return;
        }

        int width = 1280, height = 720;
        var rt = new RenderTexture(width, height, 24);
        var prevTarget = cam.targetTexture;
        var prevActive = RenderTexture.active;

        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;

        var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();

        cam.targetTexture = prevTarget;
        RenderTexture.active = prevActive;
        rt.Release();

        string path = "Logs/diag_screenshot.png";
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Debug.Log($"[DIAG] Screenshot saved to {path}");
    }
}
