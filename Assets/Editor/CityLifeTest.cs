using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Play-mode check of the city and its crowd, from the real seat camera (never touches the real save):
//   1. composition - the four seat views at midday once the streets have filled (Logs/city_*.png)
//   2. a whole day at 12x speed, looking round the four views the way a player does: how many people are out each
//      hour, where they go, and - the important one - that nobody vanishes while the seat can see them
//   3. rain keeps people in
// Run: Unity.exe -batchmode -projectPath <p> -executeMethod CityLifeTest.Run   (no -quit; [CITY] lines, RESULT at the end)
public static class CityLifeTest
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
        if (EditorApplication.timeSinceStartup - started > 900) { Done("TIMEOUT"); return; }
        if (manager == null) manager = Object.FindAnyObjectByType<ForgeGameManager>();
        if (manager == null || running || EditorApplication.timeSinceStartup - started < 3) return;
        running = true;
        manager.StopAllCoroutines(); // freeze the game loop (customers, turning) - the clock, weather and crowd run on
        manager.StartCoroutine(Script());
    }

    private static void Check(bool ok, string what)
    {
        if (!ok) failures++;
        Debug.Log($"[CITY] {(ok ? "ok  " : "FAIL")} {what}");
    }

    private static IEnumerator RealSeconds(float s)
    {
        double end = EditorApplication.timeSinceStartup + s;
        while (EditorApplication.timeSinceStartup < end) yield return null;
    }

    private static IEnumerator Script()
    {
        var cam = Camera.main;
        var city = manager.City;
        var clock = manager.Clock;
        Vector3 seat = cam.transform.position;
        const float pitch = 12f;
        void Look(float yaw) => cam.transform.SetPositionAndRotation(seat, Quaternion.Euler(pitch, yaw, 0f));

        // ---- 0. the store follows the unlocks (one source of truth: Workshop.IsUnlocked) -------------------------
        {
            const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var gmType = typeof(ForgeGameManager);
            var shop = manager.workshop;
            var bins = manager.storageDisplay.materialBins;
            var ui = (ForgeUI)gmType.GetField("ui", F).GetValue(manager);
            void Refresh() { gmType.GetField("mainRoomYaw", F).SetValue(manager, 180f); gmType.GetMethod("UpdateStorageUI", F).Invoke(manager, null); }
            string Ready() { gmType.GetMethod("ShowReadyText", F).Invoke(manager, null); return ui.instructions.text; }
            Look(180f);
            Refresh();
            Check(bins[0].gameObject.activeSelf && !bins[1].gameObject.activeSelf && !bins[2].gameObject.activeSelf,
                  "a new game's store shows copper only (no iron or steel bins, not even empty ones)");
            Check(ui.materialSlots[1].tooltip == "" && ui.materialSlots[2].tooltip == "" && ui.materialSlots[0].tooltip.Contains("Copper"),
                  "locked metals have no hover card either");
            string before = Ready();
            Check(before.Contains("Copper") && !before.Contains("Iron") && !before.Contains("Steel"), $"the forge offers copper only ('{before.Split('\n')[1]}')");
            shop.Unlock(ForgeMaterials.All[1]);
            shop.stock[1] += 2;
            Refresh();
            yield return null;
            Shot("city_storage_iron_unlocked");
            Check(bins[1].gameObject.activeSelf && !bins[2].gameObject.activeSelf && ui.materialSlots[1].tooltip.Contains("2/10 ingots"),
                  "iron unlocked: its bin appears in the store, with the 2 ingots in its card");
            string after = Ready();
            Check(after.Contains("Iron x2") && !after.Contains("Steel"), $"...the forge offers it ('{after.Split('\n')[1]}')");
            bool sells = true; for (int i = 0; i < 20; i++) sells &= shop.RollSupply()[1] > 0;
            Check(sells, "...and the Material Order Book lists it");
        }

        Check(city != null && city.NodeCount > 100, $"the crowd found the city's street graph ({city?.NodeCount} nodes)");
        if (city == null) { Done("no city"); yield break; }
        Check(GameObject.Find("Castle") != null, "the castle is in the scene");

        // ---- 1. composition at midday --------------------------------------------------------------------------
        clock.Hour = 10.5f;
        Look(90f);
        Time.timeScale = 4f;
        yield return RealSeconds(25f);
        Time.timeScale = 1f;
        clock.Hour = 12f;
        foreach (float yaw in new[] { 90f, 0f, 180f, 270f })
        {
            Look(yaw);
            yield return null;
            yield return null;
            Shot($"city_12h_yaw{yaw:0}");
        }
        Debug.Log($"[CITY] midday: {city.ActiveCount} out and about");

        // ---- 1b. the townsfolk up close: the NpcBody characters walking, sitting, chatting, carrying ------------
        {
            // (headless, no renderer ever counts as visible - so let the animators run regardless, as on screen)
            foreach (var body in city.GetComponentsInChildren<NpcBody>()) body.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            yield return RealSeconds(4f);
            var bodies = city.GetComponentsInChildren<NpcBody>().Where(b => b.gameObject.activeInHierarchy).ToList();
            var picked = new List<NpcBody>();
            foreach (string want in new[] { "Walk_", "Sit", "Talk", "Carry_Item_Walk_", "Guard_Patrol_", "Inspect", "Idle" })
            {
                var b = bodies.FirstOrDefault(x => !picked.Contains(x) && x.State.StartsWith(want));
                if (b != null) picked.Add(b);
            }
            var close = new GameObject("CloseCam").AddComponent<Camera>();
            close.CopyFrom(cam);
            close.fieldOfView = 40f;
            var rt = new RenderTexture(640, 640, 24);
            for (int i = 0; i < picked.Count; i++)
            {
                var b = picked[i];
                Transform t = b.transform;
                Vector3 target = t.position + Vector3.up * (b.Height * 0.55f);
                Vector3 from = target + t.forward * 3.2f + t.right * 1.3f + Vector3.up * 0.3f;
                close.transform.SetPositionAndRotation(from, Quaternion.LookRotation(target - from));
                close.targetTexture = rt;
                close.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(640, 640, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 640, 640), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes($"Logs/city_npc_{i}.png", tex.EncodeToPNG());
                var props = string.Join(",", t.parent.GetComponentsInChildren<MeshRenderer>().Where(r => r.gameObject.activeInHierarchy).Select(r => r.name));
                Debug.Log($"[CITY] close-up {i}: {b.Look.race} {b.Look.role} ({b.Look.outfit}) - {b.State}, pace {b.Pace:0.00}, holding [{props}]");
            }
            close.targetTexture = null;
            Object.Destroy(close.gameObject);
            Check(picked.Count >= 4, $"the city's people are animated characters doing different things ({string.Join(", ", picked.Select(p => p.State))})");
        }

        // ---- 2. a whole day ------------------------------------------------------------------------------------
        city.ResetForNewDay();
        clock.Hour = 5.2f;
        yield return null; // let the crowd count settle after the reset
        Time.timeScale = 12f;
        var walkers = city.GetComponentsInChildren<Transform>(true).Where(t => t.parent == city.transform).ToList();
        var wasActive = new Dictionary<Transform, bool>();
        var wasSeen = new Dictionary<Transform, bool>();
        var lastPos = new Dictionary<Transform, Vector3>();
        int pops = 0, removedBefore = city.RemovedInView;
        var perHour = new SortedDictionary<int, (int min, int max)>();
        float[] yaws = { 90f, 0f, 90f, 180f, 90f, 270f };
        int yawIndex = 0;
        double nextTurn = 0;
        var shotsAt = new List<float> { 7.5f, 13f, 18.5f, 20.2f, 22.6f };
        int visitsBefore = city.visits.Values.Sum();
        var eveningVisits = new Dictionary<string, int>();
        bool rainTested = false;
        while (clock.Hour < 23.3f)
        {
            clock.Hour += Time.deltaTime / 150f; // the day passes (the game itself only moves time for work - see GameTime)
            if (EditorApplication.timeSinceStartup >= nextTurn)
            {
                Look(yaws[yawIndex++ % yaws.Length]);
                nextTurn = EditorApplication.timeSinceStartup + 4.0;
                wasActive.Clear(); // an instant snap: "seen last frame" from the old view means nothing now (a real turn is gradual)
            }
            // Anyone who vanished this frame while in view (seen last frame, near enough to notice)?
            foreach (var w in walkers)
            {
                bool act = w.gameObject.activeSelf;
                if (wasActive.TryGetValue(w, out bool before) && before && !act && wasSeen[w] && Vector3.Distance(lastPos[w], cam.transform.position) < 55f)
                {
                    pops++;
                    Debug.Log($"[CITY] POP: {w.name} vanished in view at {lastPos[w]} (hour {clock.Hour:0.0})");
                }
                wasActive[w] = act;
                wasSeen[w] = act && city.IsSeen(w.position);
                lastPos[w] = w.position;
            }
            int h = Mathf.FloorToInt(clock.Hour);
            perHour[h] = perHour.TryGetValue(h, out var mm) ? (Mathf.Min(mm.min, city.ActiveCount), Mathf.Max(mm.max, city.ActiveCount)) : (city.ActiveCount, city.ActiveCount);
            if (shotsAt.Count > 0 && clock.Hour >= shotsAt[0])
            {
                float keep = cam.transform.eulerAngles.y;
                Look(90f);
                yield return null;
                Shot($"city_{shotsAt[0]:00.0}h_yaw90");
                Look(keep);
                shotsAt.RemoveAt(0);
            }
            if (clock.Hour >= 17.5f && eveningVisits.Count == 0) foreach (var kv in city.visits) eveningVisits[kv.Key] = kv.Value;
            // Rain at mid-afternoon for an hour: fewer people should stay out.
            if (!rainTested && clock.Hour >= 14f)
            {
                rainTested = true;
                int dry = city.ActiveCount;
                var meadow = Object.FindAnyObjectByType<MeadowLife>();
                meadow.ApplyDay(manager.workshop.day, manager.workshop.Season, "rain");
                double until = EditorApplication.timeSinceStartup + 12.0;
                while (EditorApplication.timeSinceStartup < until) yield return null;
                Debug.Log($"[CITY] rain: {dry} out when it started, {city.ActiveCount} an hour later");
                Check(city.ActiveCount < Mathf.Max(dry, 8), $"rain keeps people in ({dry} -> {city.ActiveCount})");
                meadow.ApplyDay(manager.workshop.day, manager.workshop.Season, "clear");
            }
            yield return null;
        }
        Time.timeScale = 1f;

        foreach (var kv in perHour) Debug.Log($"[CITY] {kv.Key:00}:00  out and about {kv.Value.min}-{kv.Value.max}");
        Debug.Log($"[CITY] visits: {string.Join(", ", city.visits.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}"))}");
        Debug.Log($"[CITY] spawned {city.Spawned}, gone {city.Removed}");
        int Peak(int from, int to) => perHour.Where(k => k.Key >= from && k.Key < to).Select(k => k.Value.max).DefaultIfEmpty(0).Max();
        int day = Peak(10, 14), night = Peak(22, 24), dawn = Peak(5, 6);
        Check(day >= 9, $"the streets are busy by day (up to {day} out at 10-14h)");
        Check(night <= 9 && night < day, $"the city is quiet at night, but not dead (up to {night} out after 22h, the watch included)");
        Check(dawn < day, $"it wakes gradually at dawn ({dawn} at 5h)");
        Check(city.visits.Count >= 7, $"people go to many kinds of places ({city.visits.Count}: {string.Join(", ", city.visits.Keys)})");
        int tavernEvening = (city.visits.TryGetValue("tavern", out int tv) ? tv : 0) - (eveningVisits.TryGetValue("tavern", out int te) ? te : 0);
        Check(tavernEvening > 0, $"the taverns fill up in the evening ({tavernEvening} went in after 17:30)");
        Check(pops == 0 && city.RemovedInView == removedBefore, $"nobody vanished in view (watched {city.Removed} leave; {pops} in view)");
        Check(city.Spawned > 40 && city.Removed > 20, "people keep coming and going all day");
        Debug.Log($"[CITY] activities: {string.Join(", ", city.activities.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}"))}");
        Debug.Log($"[CITY] roles: {string.Join(", ", city.rolesSeen.Select(k => $"{k.Key} {k.Value}"))}; groups formed {city.GroupsFormed}; guards out at the end {city.GuardsOut}");
        int Act(string a) => city.activities.TryGetValue(a, out int n) ? n : 0;
        Check(Act("sit") > 0, $"people sit on the benches ({Act("sit")})");
        Check(Act("river") + Act("bridge") > 0, $"people stop by the river and on the bridges ({Act("river")} + {Act("bridge")})");
        Check(Act("mill") > 0, $"people visit the watermill ({Act("mill")})");
        Check(Act("tavern") > 0, $"people stand about outside the taverns in the evening ({Act("tavern")})");
        Check(Act("browse") > 0 && Act("chat") > 0, $"people browse the stalls and chat on the squares ({Act("browse")} browsing, {Act("chat")} chatting)");
        Check(city.GroupsFormed >= 5, $"people go about in twos and threes ({city.GroupsFormed} groups)");
        Check(city.rolesSeen.Count >= 6, $"many kinds of people ({city.rolesSeen.Count} roles)");
        Check(city.GuardsOut >= 2, $"the watch is out at night ({city.GuardsOut} guards)");
        var amb = Object.FindAnyObjectByType<CityAmbience>();
        Check(amb != null && amb.SmokingChimneys >= 6, $"smoke rises from the nearest chimneys ({amb?.SmokingChimneys})");
        Done(failures == 0 ? "PASS" : $"FAIL ({failures})");
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
        Time.timeScale = 1f;
        Debug.Log($"[CITY] RESULT: {reason}");
        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += () => EditorApplication.Exit(0);
    }
}
