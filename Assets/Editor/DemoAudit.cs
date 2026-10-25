using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Visual audit from the real player camera (Play mode, never touches the real save): every seated view at
// several times of day, rain from every direction, and a close-up of the rack holding one of each weapon.
// Screenshots -> Logs/audit_*.png. Run: Unity.exe -batchmode -projectPath <p> -executeMethod DemoAudit.Run (no -quit)
// Optional env TB_AUDIT = "views" / "rain" / "rack" to run just one part.
public static class DemoAudit
{
    private static ForgeGameManager manager;
    private static double started;
    private static bool running;

    public static void Run()
    {
        System.Environment.SetEnvironmentVariable("TB_SAVE_PATH", "none");
        manager = null;
        running = false;
        EditorSceneManager.OpenScene("Assets/Scenes/Prototype1.unity");
        EditorApplication.isPlaying = true;
        EditorApplication.update += Tick;
        started = EditorApplication.timeSinceStartup;
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        if (EditorApplication.timeSinceStartup - started > 240) { Done("TIMEOUT"); return; }
        if (manager == null) manager = Object.FindAnyObjectByType<ForgeGameManager>();
        if (manager == null || running || EditorApplication.timeSinceStartup - started < 3) return;
        running = true;
        manager.StopAllCoroutines(); // freeze the game loop; we pose the camera ourselves
        manager.StartCoroutine(Audit());
    }

    private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    private static object Call(string method, params object[] args) => typeof(ForgeGameManager).GetMethod(method, Flags).Invoke(manager, args);
    private static T Field<T>(string name) => (T)typeof(ForgeGameManager).GetField(name, Flags).GetValue(manager);

    private static IEnumerator Audit()
    {
        string part = System.Environment.GetEnvironmentVariable("TB_AUDIT") ?? "";
        var cam = Camera.main;
        var ui = Field<ForgeUI>("ui");
        var meadow = Object.FindAnyObjectByType<MeadowLife>();
        var clock = manager.Clock;
        float pitch = 12f;
        Vector3 seat = cam.transform.position;
        ui.ShowOrder(false);
        ui.instructions.text = "";

        // One of each weapon on the rack, so every view and the close-up shows them.
        var h = manager.workshop.history;
        string[] metals = { "Steel", "Iron", "Copper", "Iron", "Steel" };
        int n = 0;
        foreach (WeaponType type in System.Enum.GetValues(typeof(WeaponType)))
        {
            var w = Weapons.Get(type);
            h.RegisterForged(new WorkpieceData { weaponType = type, materialName = metals[n], itemName = $"{metals[n]} {w.name}", durability = 60, balance = 70, sharpness = w.hasEdge ? 60 : 0 }, 1);
            n++;
        }
        typeof(ForgeGameManager).GetField("mainRoomYaw", Flags).SetValue(manager, 180f);
        for (int f = 0; f < 3; f++) { Call("UpdateStorageUI"); yield return null; }
        ui.ShowStorage(false);

        if (part == "" || part == "views")
        {
            float[] hours = { 5.8f, 7.5f, 12f, 18.2f, 19.7f, 22.5f };
            foreach (float hr in hours)
            {
                clock.Hour = hr;
                foreach (float yaw in new[] { 0f, 90f, 180f, 270f })
                {
                    cam.transform.SetPositionAndRotation(seat, Quaternion.Euler(pitch, yaw, 0f));
                    yield return null;
                    clock.Hour = hr;
                    yield return null;
                    Shot($"audit_{hr:00.0}h_yaw{yaw:0}");
                }
                Debug.Log($"[AUDIT] {hr}h: {clock.Phase} sun={RenderSettings.sun?.intensity} ambient={RenderSettings.ambientSkyColor} night={clock.Night01:0.00}");
            }
        }

        if (part == "" || part == "rain")
        {
            clock.Hour = 13f;
            meadow.ApplyDay(2, "Spring", "rain");
            for (int f = 0; f < 90; f++) yield return null; // let the rain fill the air
            foreach (float yaw in new[] { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f })
            {
                cam.transform.SetPositionAndRotation(seat, Quaternion.Euler(pitch, yaw, 0f));
                clock.Hour = 13f;
                yield return null;
                Shot($"audit_rain_yaw{yaw:0}");
            }
            int alive = 0;
            foreach (var ps in meadow.GetComponentsInChildren<ParticleSystem>()) alive += ps.particleCount;
            Debug.Log($"[AUDIT] rain particles alive: {alive}");
            meadow.ApplyDay(2, "Spring", "clear");
        }

        if (part == "" || part == "rack")
        {
            clock.Hour = 12f;
            var rack = manager.storageDisplay.transform;
            float fov = cam.fieldOfView;
            cam.fieldOfView = 34f;
            cam.transform.SetPositionAndRotation(rack.position + new Vector3(0f, 1.25f, 2.6f), Quaternion.Euler(4f, 180f, 0f));
            yield return null;
            Shot("audit_rack_closeup");
            cam.transform.SetPositionAndRotation(rack.position + new Vector3(2.2f, 1.2f, 1.6f), Quaternion.Euler(6f, 234f, 0f));
            yield return null;
            Shot("audit_rack_side");
            cam.fieldOfView = fov;
            var displays = Field<GameObject[]>("swordDisplays");
            for (int i = 0; i < 5; i++)
            {
                var r = displays[i].GetComponent<Renderer>();
                Debug.Log($"[AUDIT] rack slot {i}: {r.GetComponent<MeshFilter>().sharedMesh.name} bounds {r.bounds.min:F2}..{r.bounds.max:F2} scale {displays[i].transform.localScale.x:F2}");
            }
        }
        Done("done");
    }

    private static void Shot(string name)
    {
        var cam = Camera.main;
        var canvas = GameObject.Find("ForgeUI").GetComponent<Canvas>();
        var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.DefaultHDR);
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 0.5f;
        Canvas.ForceUpdateCanvases();
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
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        Directory.CreateDirectory("Logs");
        File.WriteAllBytes($"Logs/{name}.png", tex.EncodeToPNG());
    }

    private static void Done(string reason)
    {
        EditorApplication.update -= Tick;
        Debug.Log($"[AUDIT] RESULT: {reason}");
        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += () => EditorApplication.Exit(0);
    }
}
