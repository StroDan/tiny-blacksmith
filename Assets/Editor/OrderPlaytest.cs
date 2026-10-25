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

// Play-mode regression test of the order flow through the real game and UI, with simulated input:
//   A  a customer orders, the smith accepts -> the customer walks away; the order stays in the book
//   -  the smith clicks the weapon in storage and sets it aside for the order (reserved); clicking it again shows
//      it's reserved and offers to unassign it (D)
//   B  two more weapons are forged later (one for another order, one for the rack)
//   C  the game is saved and the whole scene reloaded from the save - the assignments are still there
//   E  on their day the SAME customer (same NPC id and story) comes back, F -> they get exactly weapon 1
//   -  another customer comes back before their order is ready: "not yet" - they leave, the order stays, no duplicate
// Throwaway save in the temp folder. Logs [OPLAY] lines; screenshots Logs/order_*.png.
// Run: Unity.exe -batchmode -projectPath <path> -executeMethod OrderPlaytest.Run -logFile <log>  (no -quit)
public static class OrderPlaytest
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
    private static string savePath;
    private static bool scriptStarted; // once only - the script itself reloads the scene (and re-finds the manager) midway

    public static void Run()
    {
        savePath = Path.Combine(Path.GetTempPath(), "tb_order_playtest_save.json");
        if (File.Exists(savePath)) File.Delete(savePath);
        System.Environment.SetEnvironmentVariable("TB_SAVE_PATH", savePath);
        manager = null;
        stack.Clear();
        failures = 0;
        scriptStarted = false;
        EditorSceneManager.OpenScene("Assets/Scenes/Prototype1.unity");
        EditorApplication.isPlaying = true;
        EditorApplication.update += Tick;
        startedAt = EditorApplication.timeSinceStartup;
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        if (EditorApplication.timeSinceStartup - startedAt > 420) { Finish("TIMEOUT"); return; }
        if (!scriptStarted)
        {
            manager = Object.FindAnyObjectByType<ForgeGameManager>();
            if (manager == null) return;
            scriptStarted = true;
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

        try
        {
            if (!Step()) Finish("script finished");
        }
        catch (System.Exception e)
        {
            Debug.LogError("[OPLAY] exception: " + e);
            failures++;
            Finish("exception");
        }
    }

    private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    private static T Field<T>(string name) => (T)typeof(ForgeGameManager).GetField(name, Flags).GetValue(manager);
    private static object Call(string method, params object[] args) => typeof(ForgeGameManager).GetMethod(method, Flags).Invoke(manager, args);

    // Customers with new work come at their time on the clock (GameTime.Arrivals): move the clock to the next one.
    private static void CustomerDueNow()
    {
        var t = manager.GameClock; var a = t.ArrivalsToday; int i = Mathf.Min(manager.workshop.visitsToday, a.Length - 1);
        if (manager.workshop.visitsToday < a.Length && t.Hour < a[i]) t.SetHour(a[i]);
    }

    private static IEnumerator Script()
    {
        yield return Wait(3.0);
        var w = manager.workshop;

        // ---- A: a customer orders; accepting sends them away ------------------------------------------------
        double t = Now();
        CustomerDueNow();
        while (Field<CustomerOrder>("offeredOrder") == null && Now() - t < 30) yield return null;
        var offer = Field<CustomerOrder>("offeredOrder");
        Check(offer != null, "a customer walks up with an order");
        if (offer == null) yield break;
        int customerId = offer.customerId;
        var person = w.history.Customer(customerId);
        string story = person.profile.story, npc = person.NpcId, title = person.Title;
        yield return Wait(0.5);
        yield return PressUntil(Key.F, () => Panel("AcceptButton"), 6);
        yield return Wait(0.3);
        Shot("order_01_offer");
        ClickButton("AcceptButton");
        t = Now();
        while (!Text("DialogueText").Contains("come back") && Now() - t < 8) yield return null;
        Shot("order_02_leaving_words");
        Check(Text("DialogueText").Contains("come back") && Text("DialogueText").Contains("Order #"), $"on accepting they say they'll come back: '{Text("DialogueText")}'");
        yield return PressUntil(Key.F, () => !Panel("DialoguePanel"), 6);
        w.visitsToday = 99; // nobody new today - keeps the table clear for the test
        t = Now();
        while (GameObject.Find("Customer") != null && Now() - t < 8) yield return null;
        var order = w.orders.Find(o => o.customerId == customerId);
        Check(GameObject.Find("Customer") == null && Field<CustomerOrder>("visitOrder") == null && Field<CustomerOrder>("offeredOrder") == null,
              "TEST A: the customer walked away - nobody stands at the table waiting");
        Check(order != null && order.status == OrderStatus.WaitingForWeapon && order.returnDay > w.day && order.acceptedDay == w.day,
              $"...and {order?.Label} is active in the book (waiting for a weapon, customer back on day {order?.returnDay})");
        yield return Wait(8.0);
        Check(GameObject.Find("Customer") == null, "...and they don't come back to stand there while it's being made");
        Shot("order_03_order_note");
        Check(Text("OrderPaperText").Contains($"#{order.id:0000}") && Text("OrderPaperText").Contains("no weapon assigned"), $"the order note lists it: '{Text("OrderPaperText")}'");

        // ---- forge weapon 1 and set it aside for the order, through the storage UI --------------------------
        var type = order.weaponType;
        string metal = order.requiredMaterial ?? "Copper";
        var w1 = w.history.RegisterForged(new WorkpieceData { weaponType = type, materialName = metal, itemName = $"{metal} {Weapons.Get(type).name}", durability = 96, balance = 96, sharpness = Weapons.Get(type).hasEdge ? 96 : 0 }, w.day);
        // A deliberately poor one of the same kind: it must be offered just the same - the UI never reveals who passes.
        var weak = w.history.RegisterForged(new WorkpieceData { weaponType = type, materialName = metal, itemName = $"{metal} {Weapons.Get(type).name}", durability = 5, balance = 5, sharpness = Weapons.Get(type).hasEdge ? 5 : 0 }, w.day);
        yield return PressUntil(Key.D, () => Mathf.Abs(Mathf.DeltaAngle(Yaw(), 180f)) < 2f, 6);
        yield return Wait(1.0);
        yield return ClickWeapon(weak);
        t = Now();
        while (!Text("DialogueText").Contains("Could go to") && Now() - t < 5) yield return null;
        Shot("order_04a_weak_offered");
        Check(Text("DialogueText").Contains($"Could go to {order.Label}") && !Text("DialogueText").ToLower().Contains("meets") && !Text("DialogueText").Contains("Durability "),
              $"a far-too-weak {weak.Label} is offered for {order.Label} like any other, with no pass/fail hint: '{Text("DialogueText")}'");
        yield return PressUntil(Key.E, () => !Panel("DialoguePanel"), 4);
        Check(!weak.IsReserved, "...and left alone when closed");
        yield return Wait(1.0);
        yield return ClickWeapon(w1);
        t = Now();
        while (!Text("DialogueText").Contains("Could go to") && Now() - t < 5) yield return null;
        Shot("order_04_assign_dialog");
        Check(Text("DialogueText").Contains($"Could go to {order.Label}"), $"clicking {w1.Label} in storage offers {order.Label}: '{Text("DialogueText")}'");
        ClickButton("AcceptButton");
        t = Now();
        while (w1.reservedForOrder != order.id && Now() - t < 5) yield return null;
        Check(w1.reservedForOrder == order.id && order.status == OrderStatus.WeaponReady, $"{w1.Label} is RESERVED for {order.Label} (order ready)");
        yield return Wait(2.5);

        // D in the UI: clicking the reserved weapon says so and offers to unassign it; it can't be scrapped.
        yield return ClickWeapon(w1);
        t = Now();
        while (!Text("DialogueText").Contains("RESERVED") && Now() - t < 5) yield return null;
        Shot("order_05_reserved_dialog");
        Check(Text("DialogueText").Contains($"RESERVED for {order.Label}") && FindRect("AskButton").GetComponentInChildren<Text>().text == "Unassign",
              $"TEST D (UI): a reserved weapon shows its order and offers Unassign, no Scrap: '{Text("DialogueText")}'");
        yield return PressUntil(Key.E, () => !Panel("DialoguePanel"), 4);
        Check(w1.reservedForOrder == order.id && w1.status == SwordStatus.InStorage, "...closing it leaves the reservation alone");

        // ---- B: two more weapons, forged later - one for another customer, one for the rack -------------------
        var other = NpcStories.Meet(w.history, w.day);
        var order2 = NpcStories.OrderFor(other, w.history, w.day, w.unlockedMetals, 60f);
        order2.weaponType = type; order2.quantity = 1; order2.requiredMaterial = null;
        if (!Weapons.Get(type).hasEdge) order2.minSharpness = 0f;
        w.Accept(order2, w.day);
        var w2 = w.history.RegisterForged(new WorkpieceData { weaponType = type, materialName = metal, itemName = $"{metal} {Weapons.Get(type).name}", durability = 99, balance = 99, sharpness = Weapons.Get(type).hasEdge ? 99 : 0 }, w.day);
        Check(w.Assign(order2, w2, w.day) == null, $"{w2.Label} set aside for {order2.Label} ({other.name})");
        var w3 = w.history.RegisterForged(new WorkpieceData { weaponType = type, materialName = metal, itemName = $"{metal} {Weapons.Get(type).name}", durability = 100, balance = 100, sharpness = Weapons.Get(type).hasEdge ? 100 : 0 }, w.day);
        Call("SaveGame");
        Debug.Log($"[OPLAY] weapons: {w1.Label} -> {order.Label}, {w2.Label} -> {order2.Label}, {w3.Label} for the rack (newest, best)");

        // ---- C: reload the whole game from the save ------------------------------------------------------------
        int o1 = order.id, o2 = order2.id, id1 = w1.id, id2 = w2.id, id3 = w3.id, idWeak = weak.id;
        GameSession.Mode = StartMode.LoadGame;
        UnityEngine.SceneManagement.SceneManager.LoadScene(GameSession.WorkshopScene);
        manager = null;
        t = Now();
        while (Now() - t < 10)
        {
            var m = Object.FindAnyObjectByType<ForgeGameManager>();
            if (m != null && m.workshop != w) { manager = m; break; }
            yield return null;
        }
        Check(manager != null, "the game restarted from the save");
        if (manager == null) yield break;
        yield return Wait(3.0);
        w = manager.workshop;
        w.visitsToday = 99;
        order = w.FindOrder(o1);
        order2 = w.FindOrder(o2);
        w1 = w.history.Find(id1); w2 = w.history.Find(id2); w3 = w.history.Find(id3);
        Check(order != null && order2 != null && w.AssignedWeaponIds(order).Count == 1 && w.AssignedWeaponIds(order)[0] == id1 && w.AssignedWeaponIds(order2)[0] == id2 && !w3.IsReserved,
              $"TEST C: after the reload {order?.Label} -> {w1?.Label}, {order2?.Label} -> {w2?.Label}, {w3?.Label} unreserved");
        Check(order != null && order.customerId == customerId && order.status == OrderStatus.WeaponReady, "...same customer, still ready");

        // ---- E + B: on their day, the same customer comes back and gets exactly weapon 1 --------------------------
        order.returnDay = w.day; order.returnHour = 0f;
        t = Now();
        double traced = 0;
        while (Field<CustomerOrder>("visitOrder") != order && Now() - t < 30)
        {
            if (Now() - traced > 2) { traced = Now(); Debug.Log($"[OPLAY] waiting: stage={manager.CurrentStage} hour={manager.Clock.Hour:0.0} day={w.day} return={order.returnDay} visit={Field<CustomerOrder>("visitOrder")?.Label} offer={Field<CustomerOrder>("offeredOrder")?.Label} customer={(GameObject.Find("Customer") != null)} instr='{Text("Instructions")}' dlg={Panel("DialoguePanel")} someone={Call("SomeoneToVisit", 0f)} due={(Call("DueOrder") as CustomerOrder)?.Label} night={manager.Clock.IsNight} polling={Field<bool>("mainRoomPolling")} ending={manager.IsEndingDay} frame={Time.frameCount} fade={Field<float>("fadeAlpha")} yaw={Yaw():0}"); }
            yield return null;
        }
        yield return Wait(1.0);
        var back = w.history.Customer((int)Call("CustomerIdAtTable"));
        Check(back != null && back.id == customerId && back.NpcId == npc && back.profile.story == story && back.Title == title,
              $"TEST E: the customer at the table is the same person ({back?.NpcId} {back?.Title})");
        Shot("order_06_back_for_it");
        yield return PressUntil(Key.F, () => Panel("DialoguePanel"), 6);
        yield return Wait(0.3);
        Shot("order_07_handover");
        Check(Text("DialogueText").Contains(w1.Label) && !Text("DialogueText").Contains(w3.Label), $"the hand-over names {w1.Label}: '{Text("DialogueText")}'");
        t = Now();
        while (w1.status != SwordStatus.OwnedByCustomer && Now() - t < 8) yield return null;
        Check(w1.status == SwordStatus.OwnedByCustomer && w1.ownerId == customerId, $"TEST B: they received {w1.Label}");
        Check(w2.status == SwordStatus.InStorage && w2.reservedForOrder == o2 && w3.status == SwordStatus.InStorage && !w3.IsReserved,
              $"...not {w2.Label} (still reserved for {order2.Label}) and not {w3.Label} (still on the rack)");
        Check(w.FindOrder(o1) == null && order.status == OrderStatus.Completed && w1.order != null && w1.order.id == o1, $"{order.Label} completed; {w1.Label} remembers its order");
        string journal = (string)Call("JournalLeftPage", w1);
        Check(journal.Contains("Owned by") && !journal.Contains("on your rack"), "the journal shows it as owned by the customer, not in storage");
        yield return Wait(3.0);

        // ---- the smith sets the poor weapon aside instead: judged only on collection - refused, the order stays -------
        w.Unassign(w2, w.day);
        weak = w.history.Find(idWeak);
        Check(w.Assign(order2, weak, w.day) == null, $"the poor {weak.Label} can be set aside for {order2.Label}");
        float repBefore = w.reputation;
        order2.returnDay = w.day; order2.returnHour = 0f;
        t = Now();
        while (Field<CustomerOrder>("visitOrder") != order2 && Now() - t < 30) yield return null;
        yield return Wait(1.0);
        yield return PressUntil(Key.F, () => Panel("DialoguePanel"), 6);
        yield return Wait(0.3);
        Shot("order_08a_refused");
        Check(Text("DialogueText").Contains("won't do") && !Text("DialogueText").Contains(" >= "), $"on collection they judge it and refuse it, in words: '{Text("DialogueText")}'");
        yield return PressUntil(Key.F, () => !Panel("DialoguePanel"), 6);
        t = Now();
        while (GameObject.Find("Customer") != null && Now() - t < 8) yield return null;
        Check(weak.status == SwordStatus.InStorage && !weak.IsReserved && w.FindOrder(o2) == order2 && order2.IsActive && w.reputation < repBefore,
              $"...{weak.Label} is back on the rack unassigned, {order2.Label} stays open, reputation dips ({repBefore:0.0} -> {w.reputation:0.0})");
        yield return Wait(2.0);

        // ---- not ready yet: the other customer asks, hears "not yet", and leaves; the order stays -------------------
        int returnBefore = w.day;
        order2.returnDay = w.day; order2.returnHour = 0f;
        t = Now();
        while (Field<CustomerOrder>("visitOrder") != order2 && Now() - t < 30) yield return null;
        yield return Wait(1.0);
        yield return PressUntil(Key.F, () => Panel("DialoguePanel"), 6);
        yield return Wait(0.3);
        Shot("order_08_not_yet");
        Check(Text("DialogueText").Contains("Is my order ready") && Text("DialogueText").Contains("Not yet"), $"not ready: '{Text("DialogueText")}'");
        yield return PressUntil(Key.F, () => !Panel("DialoguePanel"), 6);
        t = Now();
        while (GameObject.Find("Customer") != null && Now() - t < 8) yield return null;
        Check(GameObject.Find("Customer") == null && Field<CustomerOrder>("visitOrder") == null, "...they leave again (nobody left waiting at the table)");
        Check(w.FindOrder(o2) == order2 && order2.IsActive && order2.returnDay > returnBefore && w.orders.Count == 1, $"...{order2.Label} stays active, back on day {order2.returnDay}, no duplicate order");
        Check(w.ValidateOrders() == null && w.history.Validate() == null, "order book and history consistent at the end");
    }

    // ---- harness -------------------------------------------------------------------------------------------

    private static IEnumerator ClickWeapon(SwordRecord r)
    {
        var ui = Field<ForgeUI>("ui");
        var inv = manager.inventory;
        int idx = -1;
        for (int i = 0; i < inv.Count; i++) if (ReferenceEquals(inv[i], r.sword)) idx = i;
        int slot = inv.Count - 1 - idx;
        yield return null;
        var rect = ui.swordSlots[slot].rect;
        Vector3 p = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        mousePos = new Vector2(p.x, p.y);
        mouseDown = true;
        mouseUntil = Time.frameCount + 6;
        yield return Wait(0.5);
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
    private static float Yaw() => Mathf.Repeat(Camera.main.transform.eulerAngles.y, 360f);
    private static IEnumerator Wait(double seconds) { double t = Now(); while (Now() - t < seconds) yield return null; }

    private static IEnumerator PressUntil(Key key, System.Func<bool> done, double timeout)
    {
        double t = Now();
        while (!done() && Now() - t < timeout)
        {
            keysDown.Clear(); keysDown.Add(key); keysUntil = Time.frameCount + 6;
            double tapAt = Now();
            while (!done() && Now() - tapAt < 0.7) yield return null;
        }
        if (!done()) { failures++; Debug.Log($"[OPLAY] FAIL pressing {key} never got there"); }
    }

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
        Debug.Log($"[OPLAY] {(ok ? "ok  " : "FAIL")} {what}");
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
        Debug.Log($"[OPLAY] shot {name}");
    }

    private static void Finish(string reason)
    {
        EditorApplication.update -= Tick;
        Debug.Log($"[OPLAY] RESULT: {(failures == 0 && reason == "script finished" ? "PASS" : "FAIL")} - {reason}, {failures} failure(s)");
        EditorApplication.isPlaying = false;
        if (File.Exists(savePath)) File.Delete(savePath);
        GameSession.Mode = StartMode.Continue;
        EditorApplication.delayCall += () => EditorApplication.Exit(failures == 0 ? 0 : 1);
    }
}
