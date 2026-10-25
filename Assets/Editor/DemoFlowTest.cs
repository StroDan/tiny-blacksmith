using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Play-mode checks of the game clock (GameTime) in the real scene (throwaway save in the temp folder, never the real one):
//   - a new game starts on day 1 at the start of the working day, with a small clock in the HUD
//   - idling passes only a little time; the journal (TEST C) none at all; work moves the clock by its minutes
//   - TEST H: the sky follows a jump of the clock smoothly; morning / day / evening / night get darker in turn
//   - customer hours, spread-out arrivals, the new-weapon cutoff, the supplier cutoff, End Day availability
//   - TEST G: save at day 3 15:45 and load it back exactly; TEST F: materials ordered, End Day (with its summary),
//     the crate by the storage the next morning at the start of the day; a customer comes on the new day
// Run: Unity.exe -batchmode -projectPath <p> -executeMethod DemoFlowTest.Run   (no -quit; logs [FLOW] lines)
public static class DemoFlowTest
{
    private static ForgeGameManager manager;
    private static double started;
    private static bool running;
    private static bool ok = true;
    private static string savePath;

    public static void Run()
    {
        savePath = Path.Combine(Path.GetTempPath(), "tb_flow_test_save.json");
        if (File.Exists(savePath)) File.Delete(savePath);
        System.Environment.SetEnvironmentVariable("TB_SAVE_PATH", savePath);
        manager = null;
        running = false;
        ok = true;
        EditorSceneManager.OpenScene("Assets/Scenes/Prototype1.unity");
        EditorApplication.isPlaying = true;
        EditorApplication.update += Tick;
        started = EditorApplication.timeSinceStartup;
    }

    private static void Check(bool condition, string what)
    {
        Debug.Log($"[FLOW] {(condition ? "ok  " : "FAIL")} {what}");
        ok &= condition;
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        if (EditorApplication.timeSinceStartup - started > 400) { Done("TIMEOUT"); return; }
        if (manager == null) manager = Object.FindAnyObjectByType<ForgeGameManager>();
        if (manager == null || running || EditorApplication.timeSinceStartup - started < 2) return;
        running = true;
        new GameObject("FlowTestRunner").AddComponent<Runner>().StartCoroutine(Flow());
    }

    private class Runner : MonoBehaviour { }

    private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    private static T Field<T>(string name) => (T)typeof(ForgeGameManager).GetField(name, Flags).GetValue(manager);
    private static void SetField(string name, object value) => typeof(ForgeGameManager).GetField(name, Flags).SetValue(manager, value);
    private static IEnumerator Wait(float seconds) { float end = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < end) yield return null; }
    private static bool CustomerAtTable => Field<CustomerOrder>("offeredOrder") != null || GameObject.Find("Customer") != null;

    private static string Text(string name) => GameObject.Find(name)?.GetComponent<UnityEngine.UI.Text>()?.text ?? "";
    private static object Call(string method, params object[] args) => typeof(ForgeGameManager).GetMethod(method, Flags).Invoke(manager, args);

    private static void ClearTable()
    {
        SetField("offeredOrder", null);
        SetField("visitOrder", null);
        GameObject.Find("Customer")?.SetActive(false);
    }

    private static IEnumerator Flow()
    {
        var w = manager.workshop;
        var t = manager.GameClock;
        var s = TimeSettings.Current;
        var clock = manager.Clock;

        // ---- a new game: day 1, the working morning, a small clock in the corner ------------------------------------
        Check(t != null && w.day == 1 && Mathf.Abs(t.Hour - s.dayStart) < 0.1f, $"a new game starts on day 1 at {GameTime.Clock(s.dayStart)} ({t?.ClockText})");
        yield return Wait(1f);
        string hud = Text("Hud");
        Check(hud.Contains("Day 1") && hud.Contains(t.ClockText), $"the HUD shows a small clock ({hud.Split('\n')[0]} | {hud.Split('\n')[1]})");
        Check(clock.Hour == t.VisualHour && clock.Phase == t.Phase, "the sky reads the one clock (DayCycle keeps no time of its own)");

        // ---- passive time is slow, and only while idle ----------------------------------------------------------------
        yield return Wait(2f);
        ClearTable();
        w.visitsToday = 99; // nobody new for now - an empty table
        float h0 = t.Hour;
        yield return Wait(15f);
        float idleMinutes = (t.Hour - h0) * 60f;
        Check(idleMinutes > 0.3f && idleMinutes < 2f, $"idling in the workshop for 15 real seconds passes only {idleMinutes:0.0} game minutes");

        // ---- TEST C: the journal - several minutes of reading cost nothing -----------------------------------------------
        var ui = Field<ForgeUI>("ui");
        var journal = manager.StartCoroutine((IEnumerator)Call("OpenJournal"));
        yield return null;
        h0 = t.Hour;
        Time.timeScale = 10f;
        yield return Wait(18f); // three minutes of game-real time
        Time.timeScale = 1f;
        float readMinutes = (t.Hour - h0) * 60f;
        Check(ui.JournalOpen && readMinutes < 0.2f, $"TEST C: three minutes in the journal cost {readMinutes:0.00} game minutes");
        manager.StopCoroutine(journal);
        ui.ShowJournal(false);

        // ---- work is what moves the day on ----------------------------------------------------------------------------
        h0 = t.Hour;
        Call("Work", 150f, "test work");
        yield return null;
        Check(Mathf.Abs((t.Hour - h0) * 60f - 150f) < 0.1f && Text("Hud").Contains("2h 30m"), $"work moves the clock by exactly its minutes, and says so ({GameTime.Clock(h0)} -> {t.ClockText})");

        // ---- TEST H: the light follows smoothly (the sky eases after a jump of the clock) -----------------------------
        t.SetHour(15.5f);
        yield return Wait(1f);
        Call("Work", 240f, "a long job");
        float maxStep = 0f, lastVisual = t.VisualHour;
        Color lastSky = RenderSettings.ambientSkyColor;
        float colourStep = 0f;
        var sunAt = new System.Collections.Generic.List<string>();
        double end = EditorApplication.timeSinceStartup + 8.0;
        while (EditorApplication.timeSinceStartup < end)
        {
            yield return null;
            maxStep = Mathf.Max(maxStep, t.VisualHour - lastVisual);
            lastVisual = t.VisualHour;
            Color sky = RenderSettings.ambientSkyColor;
            colourStep = Mathf.Max(colourStep, Mathf.Abs(sky.r - lastSky.r) + Mathf.Abs(sky.g - lastSky.g) + Mathf.Abs(sky.b - lastSky.b));
            lastSky = sky;
        }
        Check(Mathf.Abs(t.VisualHour - t.Hour) < 0.01f && maxStep < 0.1f && colourStep < 0.05f,
              $"TEST H: 15:30 -> 19:30 in one go: the sky follows in small steps (largest {maxStep * 60f:0.0} min / colour {colourStep:0.000} per frame)");
        float Night(float hr) { t.SetHour(hr); return clock.Night01; }
        yield return null;
        float n7 = Night(7f); yield return null; n7 = clock.Night01;
        float n12 = Night(12f); yield return null; n12 = clock.Night01;
        float n19 = Night(19.5f); yield return null; n19 = clock.Night01;
        float n22 = Night(22f); yield return null; n22 = clock.Night01;
        Check(n12 <= n7 + 0.01f && n19 > n12 && n22 > n19 && n22 > 0.95f,
              $"...morning, day, evening and night: darkness {n7:0.00} / {n12:0.00} / {n19:0.00} / {n22:0.00}");

        // ---- customer hours and the late cutoffs ------------------------------------------------------------------------
        w.visitsToday = 0;
        t.SetHour(17.6f);
        Check(!manager.OrdersOpen && !t.NewCustomerDue(999f), "after customer hours no new customers come");
        t.SetHour(10f);
        var arrivals = t.ArrivalsToday;
        bool spread = arrivals.Length >= s.newCustomersMin && arrivals.Length <= s.newCustomersMax;
        for (int i = 1; i < arrivals.Length; i++) spread &= arrivals[i] - arrivals[i - 1] > 0.75f;
        Check(spread, $"today's customers are spread over the day ({string.Join(", ", System.Array.ConvertAll(arrivals, GameTime.Clock))})");
        t.SetHour(20.6f);
        yield return Wait(0.5f);
        Check(!t.CanStartWeapon && t.CanStartRepair && t.CanEndDayFreely, "after the cutoff no new weapon can be begun (a repair still can), and End Day is on offer");
        t.SetHour(20.1f);
        Check(!t.SupplierOpen, $"the supplier's orders close at {GameTime.Clock(s.supplierCutoff)}");

        // ---- TEST G: save at day 3, 15:45 - load - day 3, 15:45 ---------------------------------------------------------
        w.day = 3;
        t.SetHour(15.75f);
        w.visitsToday = 2;
        Call("SaveGame");
        var probe = new Workshop();
        bool read = SaveSystem.TryLoad(probe, out _);
        Check(read && probe.day == 3 && Mathf.Abs(probe.hour - 15.75f) < 0.001f && probe.visitsToday == 2 && probe.timeSeed == w.timeSeed,
              $"TEST G: saved at day 3 {GameTime.Clock(15.75f)}, loaded as day {probe.day} {GameTime.Clock(probe.hour)} (customers so far {probe.visitsToday})");

        // ---- TEST F: order materials, end the day, the crate is there in the morning ------------------------------------
        w.nextEventDay = 99; // (the Event Trader would otherwise be the first visitor of day 4)
        w.gold += 100;
        string why = w.OrderIngot(0) ?? w.OrderIngot(0);
        int ordered = w.ordered[0];
        w.today.forged = 2;
        t.SetHour(19f);
        ClearTable();
        manager.StartCoroutine((IEnumerator)Call("EndDay"));
        string summary = "";
        double until = EditorApplication.timeSinceStartup + 8.0;
        while (EditorApplication.timeSinceStartup < until && !summary.Contains("COMPLETE")) { summary = Text("Message"); yield return null; }
        Check(summary.Contains("DAY 3 COMPLETE") && summary.Contains("2 weapons forged") && summary.Contains("Copper ×"),
              $"End Day shows a compact summary:\n{summary}");
        until = EditorApplication.timeSinceStartup + 20.0;
        while (EditorApplication.timeSinceStartup < until && w.day == 3) yield return null;
        Check(why == null && ordered == 2 && w.day == 4 && w.crate[0] >= 2 && w.ordered[0] == 0,
              $"TEST F: 2 copper ordered on day 3 are in the crate by the storage on day 4 (crate {w.crate[0]})");
        Check(Mathf.Abs(t.Hour - s.dayStart) < 0.01f && t.VisualHour < s.dayStart - 0.5f && w.visitsToday == 0,
              $"...the new day starts at {GameTime.Clock(s.dayStart)} with the sky still at dawn ({GameTime.Clock(t.VisualHour)})");
        until = EditorApplication.timeSinceStartup + 15.0;
        while (EditorApplication.timeSinceStartup < until && t.VisualHour < t.Hour - 0.01f) yield return null;
        Check(t.VisualHour >= t.Hour - 0.01f, "...and it brightens into the working morning");

        // ---- a customer comes on the new day (early, if the smith is just waiting about) -------------------------------
        until = EditorApplication.timeSinceStartup + 75.0;
        while (Field<CustomerOrder>("offeredOrder") == null && Field<CustomerOrder>("visitOrder") == null && EditorApplication.timeSinceStartup < until) yield return null;
        Check(Field<CustomerOrder>("offeredOrder") != null || Field<CustomerOrder>("visitOrder") != null,
              $"a customer comes on the new day ({t.ClockText}, first arrival due {GameTime.Clock(t.ArrivalsToday[0])})");
        Done("done");
    }

    private static void Done(string reason)
    {
        EditorApplication.update -= Tick;
        Debug.Log($"[FLOW] RESULT: {(ok && reason == "done" ? "PASS" : "FAIL")} - {reason}");
        if (File.Exists(savePath)) File.Delete(savePath);
        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += () => EditorApplication.Exit(0);
    }
}
