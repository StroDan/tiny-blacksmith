using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

// Headless Play-mode test of the Material Order Book and the next-day delivery crate, with simulated input:
//   Day 1: only copper in the book -> order (keys and a click) -> gold paid, nothing in the store yet -> saved
//   End the day (no trader at the door) -> Day 2: the crate stands by the storage -> E unloads it into the bins
//   Storage full: only what fits moves, the rest stays in the crate (still there, still saved)
//   The Event Trader comes, her challenge is met -> iron unlocked -> it's in the order book the same day
// Logs [SUPPLY] lines, screenshots to Logs/supply_*.png. Throwaway save in the temp folder, never the real one.
// Run: Unity.exe -batchmode -projectPath <path> -executeMethod SupplyPlaytest.Run -logFile <log>  (no -quit)
public static class SupplyPlaytest
{
    private static ForgeGameManager manager;
    private static Keyboard kb;
    private static Mouse mouse;
    private static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    private static int lastFrame = -1;
    private static double startedAt;
    private static readonly List<Key> keysDown = new List<Key>();
    private static int keysUntil;
    private static bool keysWereActive;
    private static Vector2 mousePos = new Vector2(640f, 360f);
    private static bool mouseDown, mouseMoved;
    private static int mouseUntil;
    private static int failures;
    private static string savePath;

    public static void Run()
    {
        savePath = Path.Combine(Path.GetTempPath(), "tb_supply_test_save.json");
        if (File.Exists(savePath)) File.Delete(savePath);
        System.Environment.SetEnvironmentVariable("TB_SAVE_PATH", savePath);
        manager = null;
        stack.Clear();
        failures = 0;
        EditorSceneManager.OpenScene("Assets/Scenes/Prototype1.unity");
        EditorApplication.isPlaying = true;
        EditorApplication.update += Tick;
        startedAt = EditorApplication.timeSinceStartup;
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        if (EditorApplication.timeSinceStartup - startedAt > 300) { Finish("TIMEOUT"); return; }
        if (manager == null)
        {
            manager = Object.FindAnyObjectByType<ForgeGameManager>();
            if (manager == null) return;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            kb = Keyboard.current ?? InputSystem.AddDevice<Keyboard>();
            mouse = Mouse.current ?? InputSystem.AddDevice<Mouse>();
            stack.Push(Script());
            return;
        }
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;

        bool keysActive = Time.frameCount < keysUntil;
        if (keysActive) InputSystem.QueueStateEvent(kb, new KeyboardState(keysDown.ToArray()));
        else if (keysWereActive) InputSystem.QueueStateEvent(kb, new KeyboardState());
        keysWereActive = keysActive;
        bool mouseActive = mouseDown && Time.frameCount < mouseUntil;
        if (mouseActive) InputSystem.QueueStateEvent(mouse, new MouseState { position = mousePos }.WithButton(MouseButton.Left));
        else if (mouseDown) { InputSystem.QueueStateEvent(mouse, new MouseState { position = mousePos }); mouseDown = false; }
        else if (mouseMoved) InputSystem.QueueStateEvent(mouse, new MouseState { position = mousePos });

        try
        {
            if (!Step()) Finish("script finished");
        }
        catch (System.Exception e)
        {
            Debug.LogError("[SUPPLY] exception: " + e);
            failures++;
            Finish("exception");
        }
    }

    private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    private static T Field<T>(string name) => (T)typeof(ForgeGameManager).GetField(name, Flags).GetValue(manager);
    private static void SetField(string name, object value) => typeof(ForgeGameManager).GetField(name, Flags).SetValue(manager, value);

    private static IEnumerator Script()
    {
        var w = manager.workshop;
        var h = w.history;
        yield return Wait(3.0);
        // Keep the table quiet: no order customers for this test, and the Event Trader not before we want her.
        w.visitsToday = 99;
        w.nextEventDay = 99;
        w.gold = 100;
        yield return Wait(2.0);

        // ---- Day 1: the order book lists copper only ---------------------------------------------------
        yield return PressUntil(Key.D, () => Mathf.Abs(Mathf.DeltaAngle(Yaw(), 180f)) < 2f, 6);
        yield return Wait(0.8);
        Shot("supply_01_storage_day1");
        Check(Text("Prompt") == "[E] Material Orders", $"facing the storage: '{Text("Prompt")}'");
        Check(!GameObject.Find("DeliveryCrate"), "no delivery crate on day 1");
        yield return PressUntil(Key.E, () => Panel("LedgerPanel"), 5);
        yield return Wait(0.3);
        Shot("supply_02_ledger_day1");
        Check(Panel("LedgerRow0") && !Panel("LedgerRow1") && !Panel("LedgerRow2"), "the ledger lists copper only (no locked metals)");
        Check(Text("LedgerRow0").Contains("Copper") && Text("LedgerRow0").Contains("available"), $"copper row: '{Text("LedgerRow0")}'");

        int gold0 = w.gold, stock0 = w.stock[0], supply0 = w.Supply[0];
        int price = ForgeMaterials.All[0].price;
        Hold(Key.Digit1, 6); yield return Wait(0.5);
        Hold(Key.Digit1, 6); yield return Wait(0.5);
        ClickButton("LedgerOrder0"); yield return Wait(0.6);
        Check(w.ordered[0] == 3 && w.gold == gold0 - 3 * price, $"three copper ordered (keys 1,1 + a click): {w.ordered[0]} pending, gold {gold0} -> {w.gold}");
        Check(w.stock[0] == stock0 && w.Supply[0] == supply0 - 3, $"...nothing in the store yet ({w.stock[0]}), supply {supply0} -> {w.Supply[0]}");
        Check(Text("LedgerFooter").Contains("Copper ×3"), $"the footer shows tomorrow's delivery: '{Text("LedgerFooter")}'");
        Shot("supply_03_ledger_ordered");
        yield return PressUntil(Key.E, () => !Panel("LedgerPanel"), 5);
        Check(!Panel("LedgerPanel"), "E closes the ledger");
        var probe = new Workshop();
        Check(SaveSystem.TryLoad(probe, out _) && probe.ordered[0] == 3, $"the pending delivery is saved ({probe.ordered[0]})");

        // ---- End the day ------------------------------------------------------------------------------
        manager.GameClock.SetHour(18.5f); // evening: the door simply ends the day
        yield return PressUntil(Key.D, () => Mathf.Abs(Mathf.DeltaAngle(Yaw(), 270f)) < 2f, 6);
        yield return Wait(0.5);
        int day = w.day;
        yield return PressUntil(Key.E, () => manager.IsEndingDay, 4);
        yield return Wait(0.3);
        Check(!Panel("DialoguePanel"), "nobody stops the smith at the door");
        double t = Now();
        while (manager.IsEndingDay && Now() - t < 20) yield return null;
        w.visitsToday = 99; // still no order customers on day 2
        yield return Wait(4.5); // day greeting + delivery line
        Check(w.day == day + 1, $"day {day} -> {w.day}");
        Check(w.crate[0] == 3 && w.OrderedTotal == 0 && w.stock[0] == stock0, $"day 2: the delivery is in the crate ({w.crate[0]}), not yet in the store ({w.stock[0]})");
        var crate = GameObject.Find("DeliveryCrate");
        Check(crate != null && crate.activeInHierarchy, "the delivery crate stands by the storage");
        Check(Text("Hud").Contains("Delivery waiting"), "the HUD mentions the delivery");

        // ---- Open the crate ------------------------------------------------------------------------------
        yield return PressUntil(Key.A, () => Mathf.Abs(Mathf.DeltaAngle(Yaw(), 180f)) < 2f, 6);
        yield return Wait(0.8);
        Shot("supply_04_storage_crate");
        Check(Text("Prompt") == "[E] Open Delivery", $"with a delivery waiting, E opens it: '{Text("Prompt")}'");
        yield return PressUntil(Key.E, () => w.crate[0] == 0, 4);
        Check(w.stock[0] == stock0 + 3 && w.CrateTotal == 0, $"the crate's copper is in the bin ({stock0} -> {w.stock[0]})");
        yield return Wait(2.5);
        Check(GameObject.Find("DeliveryCrate") == null, "the empty crate is gone");
        Check(Text("Prompt") == "[E] Material Orders", $"...and E is the order book again: '{Text("Prompt")}'");

        // ---- Storage full: only what fits moves ----------------------------------------------------------
        w.stock[0] = Workshop.BinCapacity - 2;
        w.crate[0] = 5;
        yield return Wait(0.5);
        crate = GameObject.Find("DeliveryCrate");
        Check(crate != null, "a crate with 5 copper stands there, the bin has room for 2");
        yield return PressUntil(Key.E, () => w.crate[0] != 5, 4);
        Check(w.stock[0] == Workshop.BinCapacity && w.crate[0] == 3, $"storage full: bin {w.stock[0]}/{Workshop.BinCapacity}, {w.crate[0]} left in the crate");
        yield return Wait(3.0);
        Check(GameObject.Find("DeliveryCrate") != null, "the crate stays while it still holds something");
        probe = new Workshop();
        Check(SaveSystem.TryLoad(probe, out _) && probe.crate[0] == 3 && probe.stock[0] == Workshop.BinCapacity, "the half-emptied crate is saved");
        Check(Text("Prompt") == "[E] Material Orders", $"a crate that can't unload doesn't take E from the book: '{Text("Prompt")}'");
        // Pointing at it still opens it (and shows what's inside).
        var use = crate.GetComponent<Interactable>();
        Rect r = use.ScreenRect(Camera.main);
        MoveMouse(r.center);
        yield return Wait(0.5);
        Check(Text("Prompt") == "[E] Open Delivery", $"pointing at the crate: '{Text("Prompt")}'");
        Check(Text("StorageInfoText").Contains("DELIVERY") && Text("StorageInfoText").Contains("Copper ×3"), $"its card: '{Text("StorageInfoText")}'");
        Shot("supply_05_crate_full_hover");
        MoveMouse(new Vector2(640f, 600f));
        w.stock[0] -= 4; // four weapons forged
        yield return PressUntil(Key.E, () => w.crate[0] == 0, 4);
        Check(w.stock[0] == Workshop.BinCapacity - 1, $"with room again the rest comes out (bin {w.stock[0]})");

        // ---- The Event Trader unlocks iron -----------------------------------------------------------------
        var challenge = CustomerOrder.Challenge(WeaponType.Dagger, w.BestQualityCap, ForgeMaterials.All[1]);
        w.challenge = challenge;
        w.Accept(challenge, w.day); // an order id like any other
        challenge.deadlineDay = w.day + Workshop.ChallengeDays;
        w.challengeDay = w.day - Workshop.ChallengeCheckDelay;
        w.challengeCheckedDay = 0;
        var dagger = h.RegisterForged(new WorkpieceData { weaponType = WeaponType.Dagger, materialName = "Copper", itemName = "Copper Dagger", durability = 100, balance = 100, sharpness = 100 }, w.day);
        Check(w.Assign(challenge, dagger, w.day) == null, $"{dagger.Label} set aside for her challenge ({challenge.Label})"); // she only takes what is set aside for her
        Check(w.Supply[1] == 0, "before the challenge: no iron in the book");
        yield return PressUntil(Key.A, () => Mathf.Abs(Mathf.DeltaAngle(Yaw(), 90f)) < 2f, 6);
        t = Now();
        while (w.challenge != null && Now() - t < 40) yield return null; // she comes on her own
        Check(w.challenge == null && w.IsUnlocked(ForgeMaterials.All[1]), "the Event Trader took the dagger: iron unlocked");
        t = Now();
        while (!Panel("DialoguePanel") && Now() - t < 10) yield return null; // her words come up after the challenge is settled
        t = Now();
        while (Panel("DialoguePanel") && Now() - t < 20) { Hold(Key.F, 4); yield return Wait(0.6); }
        yield return Wait(2.0);
        Check(w.stock[1] + w.crate[1] == 2 && w.Supply[1] > 0, $"her two iron ingots are in store, and iron is in today's supply ({w.Supply[1]})");
        yield return PressUntil(Key.D, () => Mathf.Abs(Mathf.DeltaAngle(Yaw(), 180f)) < 2f, 6);
        yield return Wait(0.8);
        MoveMouse(new Vector2(640f, 600f));
        yield return PressUntil(Key.E, () => Panel("LedgerPanel"), 5);
        yield return Wait(0.3);
        Check(Panel("LedgerRow1") && Text("LedgerRow1").Contains("Iron") && !Panel("LedgerRow2"), $"iron is in the order book now: '{Text("LedgerRow1")}' (steel still hidden)");
        Shot("supply_06_ledger_iron");
        yield return PressUntil(Key.E, () => !Panel("LedgerPanel"), 5);
        Check(h.Validate() == null, "history consistent at the end");
    }

    // ---- harness -------------------------------------------------------------------------------------

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
    private static float Yaw() => Mathf.Repeat(Camera.main.transform.eulerAngles.y, 360f);
    private static IEnumerator Wait(double seconds) { double t = Now(); while (Now() - t < seconds) yield return null; }

    private static IEnumerator PressUntil(Key key, System.Func<bool> done, double timeout)
    {
        double t = Now();
        while (!done() && Now() - t < timeout)
        {
            Hold(key, 6);
            double tapAt = Now();
            while (!done() && Now() - tapAt < 0.7) yield return null;
        }
        if (!done()) { failures++; Debug.Log($"[SUPPLY] FAIL pressing {key} never got there"); }
    }

    private static void Hold(Key key, int frames)
    {
        keysDown.Clear();
        keysDown.Add(key);
        keysUntil = Time.frameCount + frames;
    }

    private static void MoveMouse(Vector2 pos) { mousePos = pos; mouseMoved = true; }

    private static void ClickButton(string name)
    {
        var r = FindRect(name);
        Vector3 p = RectTransformUtility.WorldToScreenPoint(null, r.position + new Vector3(0f, 20f, 0f));
        mousePos = new Vector2(p.x, p.y);
        mouseDown = true;
        mouseUntil = Time.frameCount + 6;
    }

    private static bool Panel(string name) { var r = FindRect(name); return r != null && r.gameObject.activeInHierarchy; }

    private static void Check(bool ok, string what)
    {
        if (!ok) failures++;
        Debug.Log($"[SUPPLY] {(ok ? "ok  " : "FAIL")} {what}");
    }

    private static string Text(string name)
    {
        foreach (var t in Object.FindObjectsByType<Text>(FindObjectsInactive.Include))
            if (t.name == name) return t.text.Replace("\n", " | ");
        return "(not found: " + name + ")";
    }

    private static RectTransform FindRect(string name)
    {
        foreach (var r in Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include))
            if (r.name == name) return r;
        return null;
    }

    private static void Shot(string name)
    {
        var cam = Camera.main;
        var rt = new RenderTexture(1280, 720, 24);
        var canvas = Object.FindAnyObjectByType<Canvas>();
        if (canvas != null) { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = cam.nearClipPlane + 0.05f; }
        cam.targetTexture = rt;
        cam.Render();
        if (canvas != null) { canvas.renderMode = RenderMode.ScreenSpaceOverlay; Canvas.ForceUpdateCanvases(); }
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        tex.Apply();
        cam.targetTexture = null;
        RenderTexture.active = null;
        rt.Release();
        File.WriteAllBytes($"Logs/{name}.png", tex.EncodeToPNG());
        Debug.Log($"[SUPPLY] shot {name} yaw={Yaw():0} prompt=\"{Text("Prompt")}\"");
    }

    private static void Finish(string reason)
    {
        EditorApplication.update -= Tick;
        Debug.Log($"[SUPPLY] RESULT: {(failures == 0 && reason == "script finished" ? "PASS" : "FAIL")} - {reason}, {failures} failure(s)");
        EditorApplication.isPlaying = false;
        if (File.Exists(savePath)) File.Delete(savePath);
        EditorApplication.delayCall += () => EditorApplication.Exit(failures == 0 ? 0 : 1);
    }
}
