using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Play-mode test of the living world's promises through the real game (throwaway save in the temp folder):
//   A  a new game gets its own world seed; a customer walks up with an offer
//   B  quitting (saving) and loading brings back THE SAME person with THE SAME offer to the table - no new roll, no
//      extra customer used up - and the day's event and weather are the same after loading
//   C  the journal opens on the Chronicle (notable works starred, the city's events), the HUD names today's event
//   D  a new day plans its events once (saved), and weather/supply come from the seed
// Logs [WFLOW] lines; screenshots Logs/wflow_*.png. Run: Unity.exe -batchmode -projectPath <p> -executeMethod WorldFlowTest.Run (no -quit)
public static class WorldFlowTest
{
    private static ForgeGameManager manager;
    private static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    private static int lastFrame = -1, failures;
    private static double startedAt;
    private static string savePath;
    private static bool started;

    public static void Run()
    {
        savePath = Path.Combine(Path.GetTempPath(), "tb_worldflow_save.json");
        if (File.Exists(savePath)) File.Delete(savePath);
        System.Environment.SetEnvironmentVariable("TB_SAVE_PATH", savePath);
        GameSession.Mode = StartMode.NewGame;
        manager = null;
        stack.Clear();
        failures = 0;
        started = false;
        EditorSceneManager.OpenScene("Assets/Scenes/Prototype1.unity");
        EditorApplication.isPlaying = true;
        EditorApplication.update += Tick;
        startedAt = EditorApplication.timeSinceStartup;
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        if (EditorApplication.timeSinceStartup - startedAt > 300) { Finish("TIMEOUT"); return; }
        if (!started)
        {
            manager = Object.FindAnyObjectByType<ForgeGameManager>();
            if (manager == null) return;
            started = true;
            stack.Push(Script());
            return;
        }
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        try { if (!Step()) Finish("script finished"); }
        catch (System.Exception e) { Debug.LogError("[WFLOW] exception: " + e); failures++; Finish("exception"); }
    }

    private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    private static T Field<T>(string name) => (T)typeof(ForgeGameManager).GetField(name, Flags).GetValue(manager);
    private static object Call(string method, params object[] args) => typeof(ForgeGameManager).GetMethod(method, Flags).Invoke(manager, args);

    private static IEnumerator Script()
    {
        yield return Wait(3.0);
        var w = manager.workshop;
        Check(w.world != null && w.world.worldSeed != 0, $"TEST A: the new game has its own world seed ({w.world?.worldSeed})");
        int seed = w.world.worldSeed;

        // A: someone with new work walks up (move the clock to their arrival time).
        var t = manager.GameClock;
        var arrivals = t.ArrivalsToday;
        if (w.visitsToday < arrivals.Length && t.Hour < arrivals[w.visitsToday]) t.SetHour(arrivals[w.visitsToday]);
        double t0 = Now();
        while (Field<CustomerOrder>("offeredOrder") == null && Now() - t0 < 40) yield return null;
        var offer = Field<CustomerOrder>("offeredOrder");
        Check(offer != null, "a customer walks up with an order");
        if (offer == null) yield break;
        yield return Wait(2.5); // they reach the table
        string who = offer.customerName, words = offer.requestText;
        int customerId = offer.customerId, visits = w.visitsToday, customers = w.history.customers.Count;
        var weapon = offer.weaponType;
        Shot("wflow_01_offer");

        // Today's event (forced, so there is one to check) and weather.
        WorldDirector.ForceEvent(w, CityEventType.MarketDay, 2);
        manager.DebugRefreshWorld();
        string weather = WorldDirector.Weather(w);
        yield return Wait(0.5);
        Check(Field<ForgeUI>("ui").hud.text.Contains("Market Day"), "TEST C: the HUD names today's event");
        Call("SaveGame");
        string json = File.ReadAllText(savePath);
        Check(json.Contains("\"hasStandingOffer\": true") && json.Contains(who), "the save holds the offer standing at the table");

        // B: quit and load.
        GameSession.Mode = StartMode.LoadGame;
        UnityEngine.SceneManagement.SceneManager.LoadScene(GameSession.WorkshopScene);
        var old = w;
        manager = null;
        t0 = Now();
        while (Now() - t0 < 15)
        {
            var m = Object.FindAnyObjectByType<ForgeGameManager>();
            if (m != null && m.workshop != old) { manager = m; break; }
            yield return null;
        }
        Check(manager != null, "the game restarted from the save");
        if (manager == null) yield break;
        w = manager.workshop;
        t0 = Now();
        while ((Field<CustomerOrder>("offeredOrder") == null || GameObject.Find("Customer") == null) && Now() - t0 < 20) yield return null;
        yield return Wait(3.0);
        var again = Field<CustomerOrder>("offeredOrder");
        Check(again != null && again.customerName == who && again.requestText == words && again.customerId == customerId && again.weaponType == weapon,
              $"TEST B: the same customer is back at the table with the same offer ({again?.customerName} / {who})");
        Check(GameObject.Find("Customer") != null && GameObject.Find("Customer").activeInHierarchy, "...standing there to be answered");
        Check(w.visitsToday == visits && w.history.customers.Count == customers, $"...and no new customer was rolled ({w.visitsToday} visits, {w.history.customers.Count} people)");
        Check(w.world.worldSeed == seed, "the world seed survived the reload");
        Check(w.world.TodayEvent(w.day) == CityEventType.MarketDay && WorldDirector.Weather(w) == weather, $"today's event and weather survived the reload ({w.world.TodayEvent(w.day)}, {WorldDirector.Weather(w)})");
        var decor = Object.FindAnyObjectByType<CityEventDecor>();
        Check(decor != null && decor.ShownType == CityEventType.MarketDay, "...and the city is dressed for it again");
        Shot("wflow_02_offer_after_reload");

        // C: the Chronicle, with a notable weapon.
        var rec = w.history.RegisterForged(new WorkpieceData { weaponType = WeaponType.Sword, materialName = "Copper", itemName = "Copper Sword", durability = 70, balance = 78, sharpness = 78 }, w.day);
        WorldDirector.CheckQuality(w, rec);
        WorldDirector.MarkNotable(w, rec, "Test: the finest blade in the city");
        var ui = Field<ForgeUI>("ui");
        ui.ShowJournal(true);
        Call("ShowJournalPage", 0);
        yield return null;
        string left = TextOf("JournalLeft"), right = TextOf("JournalRight");
        Check(left.Contains("CHRONICLE") && left.Contains("★") && left.Contains(rec.Label), $"the Chronicle lists the notable weapon: '{left.Substring(0, Mathf.Min(160, left.Length))}'");
        Check(right.Contains("THE CITY") && right.Contains("Market day"), $"...and the city's events: '{right}'");
        Shot("wflow_03_chronicle");
        Call("ShowJournalPage", 1);
        yield return null;
        Check(TextOf("JournalLeft").Contains("★ NOTABLE"), "the weapon's own page is starred");
        Shot("wflow_04_notable_page");
        ui.ShowJournal(false);

        // D: a new day - planned once, from the seed.
        int day = w.day;
        Call("AdvanceDay");
        Check(w.day == day + 1 && w.world.eventsPlannedThrough == w.day && w.world.visitRolls == 0, $"TEST D: day {w.day} is planned (events through day {w.world.eventsPlannedThrough})");
        var planned = new List<CityEventRecord>(w.world.events).Count;
        WorldDirector.PlanDay(w);
        Check(w.world.events.Count == planned, "planning the same day twice changes nothing");
        var supply = (int[])w.Supply.Clone();
        Check(System.Linq.Enumerable.SequenceEqual(supply, WorldDirector.RollSupply(w)), "the supplier's stock comes from the seed");
        Check(w.ValidateOrders() == null && w.history.Validate() == null, "order book and history consistent");
    }

    private static string TextOf(string name)
    {
        foreach (var x in Object.FindObjectsByType<Text>(FindObjectsInactive.Include)) if (x.name == name) return x.text;
        return "(not found: " + name + ")";
    }

    private static bool Step()
    {
        while (stack.Count > 0)
        {
            var top = stack.Peek();
            if (!top.MoveNext()) { stack.Pop(); continue; }
            if (top.Current is IEnumerator nested) { stack.Push(nested); continue; }
            return true;
        }
        return false;
    }

    private static double Now() => EditorApplication.timeSinceStartup;
    private static IEnumerator Wait(double seconds) { double t = Now(); while (Now() - t < seconds) yield return null; }

    private static void Check(bool ok, string what)
    {
        if (!ok) failures++;
        Debug.Log($"[WFLOW] {(ok ? "ok  " : "FAIL")} {what}");
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

    private static void Finish(string reason)
    {
        EditorApplication.update -= Tick;
        Debug.Log($"[WFLOW] RESULT: {(failures == 0 && reason == "script finished" ? "PASS" : "FAIL")} - {reason}, {failures} failure(s)");
        EditorApplication.isPlaying = false;
        if (File.Exists(savePath)) File.Delete(savePath);
        GameSession.Mode = StartMode.Continue;
        EditorApplication.delayCall += () => EditorApplication.Exit(failures == 0 ? 0 : 1);
    }
}
