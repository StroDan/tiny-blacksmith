using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

// Headless Play-mode test of the home/journal/repair loop with simulated input:
//   a recurring customer returns with the sword you made -> "Take it in" -> it hangs on your rack (still
//   theirs) -> Enter, R at the forge -> grindstone touch -> back -> F hands the same sword back ->
//   the journal opens/browses/closes with E and A/D -> turning still works -> the house door ends the day
//   exactly once (spamming E during the fade) -> turning works again.
// Logs [HOME] lines, screenshots to Logs/home_*.png. Never touches the real save (TB_SAVE_PATH=none).
// Run via: Unity.exe -batchmode -projectPath <path> -executeMethod HomePlaytest.Run -logFile <log>  (no -quit)
public static class HomePlaytest
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
    private static bool mouseDown;
    private static int mouseUntil;
    private static int failures;
    private static SwordRecord marcusSword;

    [MenuItem("Tiny Blacksmith/Run Home Playtest")]
    public static void Run()
    {
        System.Environment.SetEnvironmentVariable("TB_SAVE_PATH", "none");
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
        if (EditorApplication.timeSinceStartup - startedAt > 240) { Finish("TIMEOUT"); return; }
        if (manager == null)
        {
            manager = Object.FindAnyObjectByType<ForgeGameManager>();
            if (manager == null) return;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            kb = Keyboard.current ?? InputSystem.AddDevice<Keyboard>();
            mouse = Mouse.current ?? InputSystem.AddDevice<Mouse>();
            Seed();
            stack.Push(Script());
            return;
        }
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;

        // Same pattern as Prototype1Playtest: re-assert a held state every frame while it lasts (single events
        // get dropped headless), then send exactly one release.
        bool keysActive = Time.frameCount < keysUntil;
        if (keysActive) InputSystem.QueueStateEvent(kb, new KeyboardState(keysDown.ToArray()));
        else if (keysWereActive) InputSystem.QueueStateEvent(kb, new KeyboardState());
        keysWereActive = keysActive;
        bool mouseActive = mouseDown && Time.frameCount < mouseUntil;
        if (mouseActive) InputSystem.QueueStateEvent(mouse, new MouseState { position = mousePos }.WithButton(MouseButton.Left));
        else if (mouseDown) { InputSystem.QueueStateEvent(mouse, new MouseState { position = mousePos }); mouseDown = false; }

        try
        {
            if (!Step()) Finish("script finished");
        }
        catch (System.Exception e)
        {
            Debug.LogError("[HOME] exception: " + e);
            failures++;
            Finish("exception");
        }
    }

    // Before the first customer shows up: a recurring customer (Marcus) already owns a well-worn sword you
    // made, so the first visitor is (most likely - it's an 80% roll) him, back for a repair.
    private static void Seed()
    {
        var h = manager.workshop.history;
        var order = CustomerOrder.Generate(1, null, Race.Human);
        order.weaponType = WeaponType.Sword; // Marcus owns a sword
        order.customerName = "Marcus";
        order.requestText = "I need a sword that won't fail me on the road.";
        marcusSword = h.RegisterForged(new WorkpieceData { materialName = "Steel", itemName = "Steel Sword", durability = 91, balance = 73, sharpness = 64 }, 1);
        h.Sell(marcusSword, h.AddCustomer(order, recurring: true), order, 1);
        marcusSword.condition = 30f;
        var customer = h.Owner(marcusSword);
        customer.nextVisitDay = 1;
        marcusSword.storyTold = true; // he already told how it went on an earlier visit...
        marcusSword.returnDay = 1;    // ...and his next return day (normally rolled once, in advance) is today
        Debug.Log($"[HOME] seeded {marcusSword.Label} owned by {customer.Title}, condition {marcusSword.condition}");
    }

    // Customers with new work come at their time on the clock (GameTime.Arrivals): move the clock to the next one.
    private static void CustomerDueNow()
    {
        var t = manager.GameClock; var a = t.ArrivalsToday; int i = Mathf.Min(manager.workshop.visitsToday, a.Length - 1);
        if (manager.workshop.visitsToday < a.Length && t.Hour < a[i]) t.SetHour(a[i]);
    }

    private static IEnumerator Script()
    {
        var h = manager.workshop.history;
        yield return Wait(0.5);

        // ---- 1. Marcus comes back with the same sword ---------------------------------------------
        // His visit is an 80% roll, so turn away any stranger who turns up first until he does.
        double t = Now();
        string talk = "";
        bool repairVisit = false;
        for (int attempt = 0; attempt < 5 && !repairVisit; attempt++)
        {
            CustomerDueNow();
            t = Now();
            while (!DialogueOpen() && !StrangerWaiting() && Now() - t < 15) yield return null;
            if (!DialogueOpen())
            {
                var mc = h.Owner(marcusSword);
                Debug.Log($"[HOME] a stranger with an order came first ({attempt}) - declining. Marcus: status={marcusSword.status} cond={marcusSword.condition} recurring={mc?.recurring} nextVisit={mc?.nextVisitDay} day={manager.workshop.day} offered={Text("Instructions")}");
                yield return PressUntil(Key.F, DialogueOpen, 6);
                yield return Frames(5);
                if (Text("DialogueText").Contains(marcusSword.Label)) { attempt--; continue; } // it was Marcus after all - handle him above
                ClickButton("DeclineButton");
                t = Now();
                while ((DialogueOpen() || GameObject.Find("Customer") != null) && Now() - t < 10) yield return null;
                continue;
            }
            talk = Text("DialogueText");
            Debug.Log($"[HOME] visitor says: {talk}");
            repairVisit = talk.Contains(marcusSword.Label);
        }
        if (repairVisit)
        {
            Check(GameObject.Find("CustomerSword") != null, "Marcus arrives wearing his sword");
            Shot("home_01_repair_visit");
            yield return Frames(5); // let the canvas settle back into overlay layout before reading button positions
            ClickButton("AcceptButton");
            t = Now();
            while (marcusSword.status != SwordStatus.NeedsRepair && Now() - t < 5) yield return null;
            Check(marcusSword.status == SwordStatus.NeedsRepair && Contains(manager.inventory, marcusSword.sword), "took it in: NeedsRepair and on the rack");
            Check(GameObject.Find("CustomerSword") == null, "Marcus no longer carries it");
            // "Look after it - I'll be back for it": he leaves; the repair order waits in the book, holding his sword.
            t = Now();
            while (!DialogueOpen() && Now() - t < 5) yield return null;
            yield return PressUntil(Key.F, () => !DialogueOpen(), 6);
            t = Now();
            while (GameObject.Find("Customer") != null && Now() - t < 8) yield return null;
            var job = manager.workshop.orders.Find(o => o.IsRepair && o.repairSwordId == marcusSword.id);
            Check(GameObject.Find("Customer") == null, "Marcus leaves after handing it over (nobody camps at the table)");
            Check(job != null && marcusSword.reservedForOrder == job.id && job.customerId == marcusSword.ownerId, $"a repair order ({job?.Label}) holds exactly his sword for him");
            yield return Wait(1.0);

            // ---- 2. Repair it at the forge: Enter, R, grindstone ------------------------------------
            yield return PressUntil(Key.Enter, () => manager.CurrentStage == ForgeStage.Ready, 6);
            yield return Wait(1.5);
            Debug.Log($"[HOME] ready text: {Text("Instructions")}");
            yield return PressUntil(Key.R, () => manager.CurrentStage == ForgeStage.Grindstone, 6);
            yield return Wait(2.0);
            float sharpBefore = marcusSword.sword.sharpness;
            // Start the wheel (toggle Space until it spins), then one touch with the left mouse.
            var spinning = typeof(ForgeGameManager).GetField("grindstoneSpinning", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            t = Now();
            while (!(bool)spinning.GetValue(manager) && Now() - t < 8) { Hold(Key.Space, 10); yield return Frames(20); }
            yield return Wait(0.5);
            Shot("home_02_repair_grindstone");
            HoldMouse(new Vector2(640f, 360f), 60);
            yield return Frames(90);
            yield return Wait(1.0);
            Debug.Log($"[HOME] after touch: {Text("Message")} touches={manager.grindstone.Touches}");
            yield return PressUntil(Key.Enter, () => manager.CurrentStage == ForgeStage.MainRoom, 10);
            Check(marcusSword.RepairedAwaitingPickup && marcusSword.repairCount == 1 && marcusSword.id == 1, $"repaired: same {marcusSword.Label}, repairCount 1 (sharpness {sharpBefore:0} -> {marcusSword.sword.sharpness:0})");
            Check(h.swords.Count == 1, "no new sword record was created by the repair");

            // ---- 3. He comes back on his day; hand it back with F -----------------------------------------
            Check(job != null && job.status == OrderStatus.WeaponReady, $"the repair order is ready ({job?.status})");
            if (job != null) { job.returnDay = manager.workshop.day; job.returnHour = 0f; } // his day to come by is today
            t = Now();
            while (!Text("Instructions").Contains(job?.Label ?? "#") && Now() - t < 20) yield return null;
            yield return Wait(1.0);
            Check(Text("Instructions").Contains("is back for"), $"Marcus is back for his order: '{Text("Instructions")}'");
            yield return PressUntil(Key.F, DialogueOpen, 6);
            Debug.Log($"[HOME] hand-back: {Text("DialogueText")}");
            Shot("home_03_hand_back");
            t = Now();
            while (marcusSword.status != SwordStatus.OwnedByCustomer && Now() - t < 8) yield return null;
            Check(marcusSword.status == SwordStatus.OwnedByCustomer && !Contains(manager.inventory, marcusSword.sword), "back with Marcus and off the rack");
            Check(h.Validate() == null, "history consistent after the repair round trip");
        }
        else
        {
            Debug.Log("[HOME] first visitor wasn't Marcus (random roll) - skipping the repair part");
        }

        // Let the next customer walk up and settle at the table, so they can't interrupt the turning below.
        t = Now();
        while (!Text("Instructions").Contains("A customer is waiting") && Now() - t < 15) yield return null;
        yield return Wait(4.0);

        // ---- 4. The journal ---------------------------------------------------------------------------
        Shot("home_04_table");
        Check(Text("Prompt") == "[E] Open Journal", $"facing the table shows the journal prompt ('{Text("Prompt")}')");
        yield return PressUntil(Key.E, () => Panel("JournalPanel"), 6);
        yield return Wait(0.5);
        Shot("home_05_journal");
        Debug.Log($"[HOME] journal left: {Text("JournalLeft")}");
        Debug.Log($"[HOME] journal right: {Text("JournalRight")}");
        Check(Text("JournalLeft").Contains("Sword #0001") && Text("JournalLeft").Contains("Marcus"), "journal shows Sword #0001 and its owner");
        if (repairVisit) Check(Text("JournalLeft").Contains("Repairs: 1"), "journal shows the repair");
        float yawInJournal = Camera.main.transform.eulerAngles.y;
        Hold(Key.D, 6);
        yield return Wait(0.8);
        Check(Mathf.Approximately(Camera.main.transform.eulerAngles.y, yawInJournal), "camera doesn't turn while the journal is open");
        yield return PressUntil(Key.E, () => !Panel("JournalPanel"), 6);
        yield return Wait(0.6);
        Check(!Panel("JournalPanel") && Text("Prompt") == "[E] Open Journal", "journal closed, prompt back");

        // ---- 5. Turn right to storage, then on to the house -----------------------------------------
        manager.GameClock.SetHour(18.5f); // evening: the door simply ends the day (before 18:00 it asks first)
        yield return PressUntil(Key.D, () => Yaw() > 170f && Yaw() < 190f, 6);
        yield return Wait(0.8);
        Check(Panel("StoragePanel"), "storage view after D from the table");
        yield return PressUntil(Key.D, () => Yaw() > 260f && Yaw() < 280f, 6);
        yield return Wait(0.8);
        Shot("home_06_house");
        Check(!Panel("StoragePanel") && Text("Prompt") == "[E] End Day", $"facing the house: no storage UI, End Day prompt ('{Text("Prompt")}')");

        // ---- 6. End the day - spam E through the fade, it must advance exactly one day ---------------
        int dayBefore = manager.workshop.day;
        int dayEvents = 0;
        manager.DayEnded += _ => dayEvents++;
        Hold(Key.E, 6);
        yield return Frames(10);
        Check(manager.IsEndingDay, "End Day started");
        for (int i = 0; i < 12; i++) { Hold(Key.E, 4); yield return Frames(8); } // spam
        // Nobody catches the smith at the door any more (the ore trader became the Material Order Book): straight to sleep.
        Check(!Panel("DialoguePanel"), "no trader stops the smith at the door");
        t = Now();
        while (manager.IsEndingDay && Now() - t < 25) yield return null;
        yield return Wait(2.5); // day greeting
        Check(manager.workshop.day == dayBefore + 1 && dayEvents == 1, $"day {dayBefore} -> {manager.workshop.day}, DayEnded raised {dayEvents} time(s)");
        Check(FadeAlpha() < 0.01f, "screen faded back in");
        Debug.Log($"[HOME] hud after end day: {Text("Hud")}");

        // ---- 7. Controls work again: D wraps round to the forge ----------------------------------------
        float yawBefore = Yaw();
        bool turnedToForge = false;
        yield return PressUntil(Key.D, () => turnedToForge = turnedToForge || Yaw() < 10f || Yaw() > 350f, 6);
        yield return Wait(1.0);
        yield return Wait(0.8);
        Shot("home_07_forge");
        Check(turnedToForge, $"D from the house turns to the forge (yaw {yawBefore:0} -> ~0)");
        Check(h.Validate() == null, "history consistent at the end");
    }

    // ---- helpers --------------------------------------------------------------------------------------

    // Runs the test script up to its next plain `yield return null`, stepping into nested enumerators
    // (Wait, Frames, PressUntil) - false once the whole script is done.
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
    private static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

    // Taps a key, and keeps re-tapping it until the condition holds (the game may not be polling yet).
    private static IEnumerator PressUntil(Key key, System.Func<bool> done, double timeout)
    {
        double t = Now();
        while (!done() && Now() - t < timeout)
        {
            Hold(key, 6);
            double tapAt = Now();
            while (!done() && Now() - tapAt < 0.7) yield return null; // real time, not frames: headless runs at thousands of fps
        }
        if (!done()) { failures++; Debug.Log($"[HOME] FAIL pressing {key} never got there"); }
    }

    private static void Hold(Key key, int frames)
    {
        keysDown.Clear();
        keysDown.Add(key);
        keysUntil = Time.frameCount + frames;
    }

    private static void HoldMouse(Vector2 pos, int frames)
    {
        mousePos = pos;
        mouseDown = true;
        mouseUntil = Time.frameCount + frames;
    }

    private static void ClickButton(string name)
    {
        var r = FindRect(name);
        Vector3 p = RectTransformUtility.WorldToScreenPoint(null, r.position + new Vector3(0f, 20f, 0f));
        HoldMouse(new Vector2(p.x, p.y), 6);
        Debug.Log($"[HOME] click {name} at {p} rectpos={r.position} screen={Screen.width}x{Screen.height} f={Time.frameCount}");
    }

    private static bool StrangerWaiting() => typeof(ForgeGameManager).GetField("offeredOrder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(manager) != null;
    private static bool DialogueOpen() { var d = FindRect("DialoguePanel"); return d != null && d.gameObject.activeInHierarchy; }
    private static bool Panel(string name) { var r = FindRect(name); return r != null && r.gameObject.activeInHierarchy; }
    private static bool Contains(IReadOnlyList<WorkpieceData> list, WorkpieceData w) { foreach (var x in list) if (ReferenceEquals(x, w)) return true; return false; }

    private static float FadeAlpha()
    {
        var f = FindRect("Fade");
        return f != null ? f.GetComponent<Image>().color.a : -1f;
    }

    private static void Check(bool ok, string what)
    {
        if (!ok) failures++;
        Debug.Log($"[HOME] {(ok ? "ok  " : "FAIL")} {what}");
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
        System.IO.File.WriteAllBytes($"Logs/{name}.png", tex.EncodeToPNG());
        Debug.Log($"[HOME] shot {name} yaw={Yaw():0} prompt=\"{Text("Prompt")}\" instr=\"{Text("Instructions")}\"");
    }

    private static void Finish(string reason)
    {
        EditorApplication.update -= Tick;
        Debug.Log($"[HOME] RESULT: {(failures == 0 && reason == "script finished" ? "PASS" : "FAIL")} - {reason}, {failures} failure(s)");
        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += () => EditorApplication.Exit(failures == 0 ? 0 : 1);
    }
}
