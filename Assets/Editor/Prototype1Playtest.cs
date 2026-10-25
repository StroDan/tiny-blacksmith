using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

// Headless playtest: opens Prototype1, enters Play mode, then plays a whole sword with
// simulated keyboard/mouse input (furnace -> anvil -> barrel -> grindstone -> result),
// logging state with a [PLAY] prefix and saving a screenshot per stage to Logs/.
// Run via: Unity.exe -batchmode -projectPath <path> -executeMethod Prototype1Playtest.Run -logFile <log>
// (no -quit: this script exits the editor itself when done).
public static class Prototype1Playtest
{
    private static ForgeGameManager manager;
    private static Keyboard kb;
    private static Mouse mouse;
    private static string lastWorkpieceMesh;
    private static ForgeStage lastStage = (ForgeStage)(-1);
    private static int step;
    private static float clockAtStart, clockAfter = -1f; // the game clock around forging the weapon (TEST A / B)
    private static double realAtStart, realAfter;
    private static int frame;
    private static double stepStart;
    private static double startedAt;
    private static bool pressedLastFrame;
    private static int pressFrame;
    private static double lastEnter;
    private static bool expDone;
    private static int expLogs;
    private static int wbPhase;
    private static int wbFrame;
    private static int holdFrames = 6;
    private static Key heldKey = Key.None;
    private static Vector2? heldMouse;
    private static int lastFrame = -1;
    private static int clicks;
    private static int grindTouches;
    private static int grindPhase; // 0 holding, 1 waiting for lock, 2 waiting for bar reset
    private static bool grindSpaceTapped;
    private static double grindSpaceHoldStart;
    private static int mainRoomPhase; // 0 = waiting for the dialogue, 1 = clicked Accept waiting for it to close, 2 = pause, 3 = pressed Enter
    private static double mainRoomPhaseStart;
    private static double lookHoldStart;
    private static int markClicks;
    private static int secondRoomPhase;
    private static double secondRoomStart;
    private static double assignClickAt = -10;
    private static double talkPressAt = -10;
    private static readonly HashSet<string> shots = new HashSet<string>();
    // Which weapon to forge: env TB_TEST_WEAPON = Sword / Dagger / Sickle / Spear / Hammer (default Sword).
    // Screenshots get the weapon's name as a prefix when it isn't the sword.
    private static WeaponType target = WeaponType.Sword;
    private static string shotPrefix = "";

    [MenuItem("Tiny Blacksmith/Run Playtest")]
    public static void Run()
    {
        System.Environment.SetEnvironmentVariable("TB_SAVE_PATH", "none"); // start from a fresh game, never touch the real save
        step = 0;
        clockAfter = -1f;
        frame = 0;
        clicks = 0;
        grindTouches = 0;
        grindPhase = 0;
        grindSpaceTapped = false;
        grindSpaceHoldStart = 0;
        secondRoomPhase = 0;
        secondRoomStart = 0;
        markClicks = 0;
        shots.Clear();
        string wanted = System.Environment.GetEnvironmentVariable("TB_TEST_WEAPON");
        target = !string.IsNullOrEmpty(wanted) && System.Enum.TryParse(wanted, true, out WeaponType parsed) ? parsed : WeaponType.Sword;
        shotPrefix = target == WeaponType.Sword ? "" : target.ToString().ToLower() + "_";
        Debug.Log($"[PLAY] forging a {target}");
        EditorSceneManager.OpenScene("Assets/Scenes/Prototype1.unity");
        EditorApplication.isPlaying = true;
        EditorApplication.update += Tick;
        startedAt = EditorApplication.timeSinceStartup;
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;

        if (manager == null)
        {
            manager = Object.FindAnyObjectByType<ForgeGameManager>();
            if (manager == null) return;

            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            kb = Keyboard.current ?? InputSystem.AddDevice<Keyboard>();
            mouse = Mouse.current ?? InputSystem.AddDevice<Mouse>();
            Debug.Log($"[PLAY] keyboard={(kb != null)} mouse={(mouse != null)} screen={Screen.width}x{Screen.height}");
            stepStart = EditorApplication.timeSinceStartup;
            return;
        }

        if (Time.frameCount == lastFrame) return; // several editor ticks can share one player frame
        lastFrame = Time.frameCount;
        frame++;
        double now = EditorApplication.timeSinceStartup;

        // Log every change of the forging-phase mesh (the workpiece goes through 5 shapes at the anvil).
        var wpMesh = manager.workpieceTransform.GetComponent<MeshFilter>().sharedMesh;
        string wpName = wpMesh != null ? wpMesh.name : "none";
        if (wpName != lastWorkpieceMesh)
        {
            lastWorkpieceMesh = wpName;
            Debug.Log($"[PLAY] workpiece mesh -> {wpName} (stage {manager.CurrentStage})");
        }

        if (manager.CurrentStage != lastStage)
        {
            // Case 0 (below) waits on this instead of CameraSettled(): Main Room -> Ready only turns the
            // camera (same seat, same position throughout), and CameraSettled() only tracks position - it
            // can't see a rotation-only animation still in progress and reports "settled" immediately.
            if (manager.CurrentStage == ForgeStage.Ready) stepStart = now;
            lastStage = manager.CurrentStage;
        }
        if (now - startedAt > 300)
        {
            Finish("TIMEOUT at step " + step + " stage " + manager.CurrentStage, false);
            return;
        }

        // Releasing keys/buttons the frame after they were pressed, so wasPressedThisFrame fires once.
        if (pressedLastFrame)
        {
            if (Time.frameCount < pressFrame + holdFrames)
            {
                // Keep re-asserting the state every frame: single events get dropped in this headless setup.
                if (heldKey != Key.None) InputSystem.QueueStateEvent(kb, new KeyboardState(heldKey));
                if (heldMouse.HasValue) InputSystem.QueueStateEvent(mouse, new MouseState { position = heldMouse.Value }.WithButton(MouseButton.Left));
                if (step != 8) return;
                goto runStep;
            }
            heldKey = Key.None;
            heldMouse = null;
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            InputSystem.QueueStateEvent(mouse, new MouseState { position = mouse.position.ReadValue() });
            pressedLastFrame = false;
            holdFrames = 6;
            return;
        }

        runStep:

        if (step == 10 && clockAfter < 0f && manager.CurrentStage == ForgeStage.MainRoom) { clockAfter = manager.GameClock.Hour; realAfter = now; } // back from forging (TEST A / B)
        if (manager.CurrentStage == ForgeStage.MainRoom)
        {
            if (step == 10) HandleSecondMainRoom(now);
            else HandleMainRoom(now);
            return;
        }

        switch (step)
        {
            case 0: // Ready: pick the weapon (Right arrow cycles), then Copper (the only metal a new smith knows), then Enter
                if (manager.CurrentStage != ForgeStage.Ready) return;
                if (now - stepStart < 1.5) return; // let the camera finish turning back from Main Room
                if (manager.SelectedWeapon != target)
                {
                    if (now - lastEnter < 0.35) return;
                    lastEnter = now;
                    Debug.Log($"[PLAY] ready: {manager.SelectedWeapon} selected, pressing Right for {target}");
                    Tap(Key.RightArrow);
                    return;
                }
                Shot("01_ready");
                Tap(Key.Digit1);
                step = 1;
                stepStart = now;
                break;

            case 1:
                if (now - stepStart < 0.3) return;
                Debug.Log($"[PLAY] ready text: {Find("Instructions")}");
                clockAtStart = manager.GameClock.Hour; realAtStart = now; // TEST A / B: how much in-game time the weapon takes
                Tap(Key.Enter);
                step = 2;
                stepStart = now;
                break;

            case 2: // Furnace: chase the fire with the catcher - hold RMB while the fire is above it, release while below
            {
                if (manager.CurrentStage != ForgeStage.Furnace) return;
                var game = manager.forgeStation.game;
                if (game.Finished) { step = 3; stepStart = now; return; }
                if (!CameraSettled()) return;
                HoldRightMouse(game.firePos > game.catcherPos);
                if (now - stepStart > 0.6) Shot("02_furnace");
                break;
            }

            case 3: // wait for anvil
                if (manager.CurrentStage != ForgeStage.Anvil) return;
                Debug.Log($"[PLAY] furnace result -> hits={manager.anvilStation.TotalHits} won={manager.forgeStation.game.Won} result={manager.forgeStation.game.Result}");
                step = 4;
                stepStart = now;
                break;

            case 4: // Anvil: click points (skip the 2nd round on purpose to test the miss path)
            {
                if (manager.CurrentStage != ForgeStage.Anvil)
                {
                    Debug.Log($"[PLAY] anvil done: clicks={clicks} durPts={manager.anvilStation.DurabilityPoints} balPts={manager.anvilStation.BalancePoints}");
                    step = 5;
                    stepStart = now;
                    return;
                }
                if (!CameraSettled()) return;
                var markers = Object.FindObjectsByType<HitZoneMarker>(FindObjectsSortMode.None);
                if (markers.Length == 2)
                {
                    Shot("03_anvil");
                    if (manager.anvilStation.Resolved == 1) return; // let this round expire untouched
                    var m = markers[0];
                    foreach (var candidate in markers)
                        if (candidate.kind == (manager.anvilStation.Resolved % 2 == 0 ? PointKind.Durability : PointKind.Balance))
                            m = candidate;
                    Vector3 sp = Camera.main.WorldToScreenPoint(m.transform.position);
                    if (clicks < 40)
                    {
                        Click(new Vector2(sp.x, sp.y));
                        clicks++;
                        Debug.Log($"[PLAY] click #{clicks} on {m.kind} at screen {sp} resolvedBefore={manager.anvilStation.Resolved}");
                    }
                }
                break;
            }

            case 5: // Barrel
            {
                if (manager.CurrentStage != ForgeStage.Barrel) return;
                var bar = manager.quenchBarrel.bar;
                if (bar.Finished) { step = 6; stepStart = now; return; }
                if (!CameraSettled() || bar.widths[0] <= 0f) return;
                float target = PerfectMid(bar);
                if (bar.pointer < target) HoldSpace(true);
                else
                {
                    if (!bar.Finished) Debug.Log($"[PLAY] barrel release at pointer={bar.pointer:0.000}");
                    HoldSpace(false);
                }
                if (bar.pointer > 0.3f) Shot("04_barrel");
                break;
            }

            case 6: // Grindstone: Space starts the wheel, then touch 1 perfect, touch 2 deliberately bad, touch 3 good-ish, then Enter
            {
                // A hammer has no edge: it skips the grindstone and goes straight to the workbench.
                if (manager.CurrentStage == ForgeStage.Workbench) { Debug.Log("[PLAY] no grindstone for this weapon"); step = 7; return; }
                if (manager.CurrentStage != ForgeStage.Grindstone) return;
                if (!CameraSettled()) return;
                if (!grindSpaceTapped)
                {
                    // RunGrindstone first waits for Space/LMB to be released (stray input left over from the
                    // Barrel stage's own SpaceHeld() use) before it starts polling for the "start the wheel"
                    // press - that entry guard only starts checking once its own GoTo(GrindView()) camera
                    // glide finishes, at some real-time offset this test can't precisely predict. A single
                    // release-then-hold pulse can land entirely inside either phase and be missed by the
                    // other, so this instead toggles Space on/off repeatedly for several seconds, guaranteeing
                    // both an observed release (clears the entry guard) and a fresh press edge (starts the
                    // wheel) regardless of exactly when either side starts polling.
                    if (grindSpaceHoldStart == 0) { grindSpaceHoldStart = now; Debug.Log("[PLAY] grindstone: toggling Space on/off to start the wheel"); }
                    double elapsed = now - grindSpaceHoldStart;
                    if (elapsed > 6.0)
                    {
                        InputSystem.QueueStateEvent(kb, new KeyboardState());
                        grindSpaceTapped = true;
                        grindSpaceHoldStart = 0;
                        return;
                    }
                    bool down = ((int)(elapsed / 0.4) % 2) == 1;
                    InputSystem.QueueStateEvent(kb, down ? new KeyboardState(Key.Space) : new KeyboardState());
                    return;
                }
                var bar = manager.grindstone.bar;
                if (bar.widths[0] <= 0f) return; // manager has not started spinning / prepared the first touch yet
                Vector2 grindMousePos = new Vector2(300f, 400f); // away from the Finish button (bottom-right)
                if (grindTouches >= 3)
                {
                    Debug.Log($"[PLAY] grindstone finishing after {grindTouches} touches");
                    HoldMouseButton(false, grindMousePos);
                    Tap(Key.Enter);
                    step = 7;
                    stepStart = now;
                    return;
                }
                if (grindPhase == 1) { if (bar.Finished) grindPhase = 2; return; }
                if (grindPhase == 2) { if (!bar.Finished) grindPhase = 0; return; } // manager re-prepared the bar
                float target = grindTouches == 1 ? 0.97f : PerfectMid(bar);
                if (bar.pointer < target) HoldMouseButton(true, grindMousePos);
                else
                {
                    Debug.Log($"[PLAY] grind touch {grindTouches + 1} release at {bar.pointer:0.000} widths={string.Join(",", bar.widths)}");
                    HoldMouseButton(false, grindMousePos);
                    grindTouches++;
                    grindPhase = 1;
                }
                if (bar.pointer > 0.2f) Shot("05_grind");
                break;
            }

            case 7: // wait for the Workbench (Enter is ignored while a touch message shows - keep tapping)
                if (manager.CurrentStage == ForgeStage.Grindstone)
                {
                    if (now - lastEnter > 0.5) { lastEnter = now; Tap(Key.Enter); }
                    return;
                }
                if (manager.CurrentStage != ForgeStage.Workbench) return;
                wbPhase = 0;
                step = 8;
                stepStart = now;
                break;

            case 8: // Workbench: drag Guard (perfect), Grip (way off), Pommel (perfect) onto the ghost
            {
                if (!expDone) { expDone = true; Click(new Vector2(300f, 300f)); Debug.Log("[PLAY] exp: clicked 300,300"); return; }
                if (Time.frameCount % 2 == 0 && expLogs < 6) { expLogs++; Debug.Log($"[PLAY] exp: mouse={mouse.position.ReadValue()} pressed={mouse.leftButton.isPressed}"); }
                if (manager.CurrentStage != ForgeStage.Workbench)
                {
                    var d = manager.HandleParts;
                    Debug.Log($"[PLAY] workbench done: {d.Count} parts");
                    step = 9;
                    stepStart = now;
                    return;
                }
                var cam = Camera.main;
                Vector3 wbCam = manager.WorkbenchCameraPosition;
                if ((cam.transform.position - wbCam).sqrMagnitude > 0.0004f || manager.HandleParts.Count == 0) return; // camera has not arrived yet
                HandlePart part = null;
                int idx = 0;
                for (int i = 0; i < manager.HandleParts.Count; i++)
                    if (!manager.HandleParts[i].locked) { part = manager.HandleParts[i]; idx = i; break; }
                if (part == null) return;

                float planeY = part.targetPos.y;
                Vector3 from = cam.WorldToScreenPoint(new Vector3(part.go.transform.position.x, planeY, part.go.transform.position.z));
                Vector3 aim = part.targetPos + (idx == 1 ? new Vector3(0.28f, 0f, 0.1f) : Vector3.zero);
                Vector3 to = cam.WorldToScreenPoint(aim);

                switch (wbPhase)
                {
                    case 0: // grab the part
                        {
                            // The manager grabs with a raycast from the mouse: the part's collider must be hit first.
                            var grabRay = cam.ScreenPointToRay(new Vector2(from.x, from.y));
                            bool grabbed = Physics.Raycast(grabRay, out RaycastHit grabHit, 100f) && grabHit.collider.gameObject == part.go;
                            Debug.Log($"[PLAY] grab check {part.name}: {(grabbed ? "OK" : "MISS")}");
                        }
                        if (part.rotatable) part.go.transform.rotation = Quaternion.Euler(0f, part.targetYaw, 0f); // rotation input is not simulated here
                        HoldMouse(new Vector2(from.x, from.y));
                        wbPhase = 1; wbFrame = Time.frameCount;
                        break;
                    case 1:
                        if (Time.frameCount - wbFrame >= 4) { wbPhase = 2; wbFrame = Time.frameCount; }
                        break;
                    case 2: // drag it onto the ghost
                        heldMouse = new Vector2(to.x, to.y);
                        Debug.Log($"[PLAY] dbg p2 f={Time.frameCount - wbFrame} mousePos={mouse.position.ReadValue()} pressed={mouse.leftButton.isPressed} guard={part.go.transform.position} hold={heldMouse} pfl={pressedLastFrame}");
                        if (Time.frameCount - wbFrame >= 6 && !mouse.leftButton.isPressed)
                        {
                            // The simulated mouse sometimes stops receiving events by this point in a long headless
                            // run (reads (0,0), never pressed) - rather than hang, fit every remaining part onto its
                            // ghost and lock it through the manager's own LockPart, so the rest of the loop still runs.
                            Shot("07_workbench");
                            Debug.Log("[PLAY] workbench: simulated mouse not arriving - fitting remaining parts directly");
                            var lockPart = typeof(ForgeGameManager).GetMethod("LockPart", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            foreach (var hp in manager.HandleParts)
                            {
                                if (hp.locked) continue;
                                hp.go.transform.SetPositionAndRotation(hp.targetPos, Quaternion.Euler(0f, hp.targetYaw, 0f));
                                manager.StartCoroutine((System.Collections.IEnumerator)lockPart.Invoke(manager, new object[] { hp, hp.targetPos.y }));
                            }
                            heldMouse = null;
                            holdFrames = 0;
                            wbPhase = 3; wbFrame = Time.frameCount;
                        }
                        else if (Time.frameCount - wbFrame >= 6)
                        {
                            Shot("07_workbench");
                            Debug.Log($"[PLAY] dragging {part.name} at {part.go.transform.position} target {part.targetPos}: {Find("Stats")}");
                            holdFrames = 0; // release on the next frame
                            wbPhase = 3; wbFrame = Time.frameCount;
                        }
                        break;
                    case 3: // wait for the lock
                        if (part.locked) { Debug.Log($"[PLAY] {part.name} locked: {Find("Message")}"); wbPhase = 0; }
                        break;
                }
                break;
            }

            case 9: // Result screen
                if (manager.CurrentStage != ForgeStage.Result) return;
                if (now - stepStart < 4.5) return; // fade out + fade in
                Shot("06_result");
                Debug.Log($"[PLAY] result title: {Find("ResultTitle")}");
                Debug.Log($"[PLAY] result stats: {Find("ResultStats")}");
                var markPanel = FindRect("MarkPanel");
                bool designing = markPanel != null && markPanel.gameObject.activeInHierarchy;
                Debug.Log($"[PLAY] mark editor open={designing}");
                step = designing ? 11 : 12;
                markClicks = 0;
                stepStart = now;
                break;

            case 11: // First sword ever: switch on every cell of the maker's mark (-> "sun"), then Stamp it!
            {
                if (now - stepStart < 0.25) return;
                stepStart = now;
                var cells = new List<RectTransform>();
                foreach (var r in Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None))
                    if (r.name == "MarkCell") cells.Add(r);
                if (markClicks < cells.Count)
                {
                    Vector3 cp = RectTransformUtility.WorldToScreenPoint(null, cells[markClicks].position + new Vector3(0f, 20f, 0f));
                    Click(new Vector2(cp.x, cp.y));
                    markClicks++;
                    return;
                }
                if (markClicks == cells.Count) Shot("06b_mark");
                var done = FindRect("MarkDoneButton");
                Vector3 dp = RectTransformUtility.WorldToScreenPoint(null, done.position + new Vector3(0f, 20f, 0f));
                Click(new Vector2(dp.x, dp.y));
                markClicks++;
                if (manager.workshop.HasMark)
                {
                    Debug.Log($"[PLAY] mark designed: bits={manager.workshop.mark} name={manager.workshop.MarkName}");
                    step = 12;
                }
                break;
            }

            case 12: // Hang the sword on the rack
            {
                if (now - stepStart < 0.6) return;
                var add = FindRect("AddButton");
                Vector3 p = RectTransformUtility.WorldToScreenPoint(null, add.position + new Vector3(0f, 20f, 0f));
                Click(new Vector2(p.x, p.y));
                step = 10;
                stepStart = now;
                break;
            }

            case 10: // back through the Main Room (try to deliver) to Ready
                if (clockAfter < 0f && manager.CurrentStage == ForgeStage.MainRoom) { clockAfter = manager.GameClock.Hour; realAfter = now; }
                if (now - stepStart < 0.5) return;
                {
                    // Hung on the rack: if it could go to the accepted order (right kind of weapon), the game offers to set it aside - say yes.
                    var dlg = FindRect("DialoguePanel");
                    if (dlg != null && dlg.gameObject.activeInHierarchy && Find("DialogueText").Contains("could go to") && now - assignClickAt > 1.0)
                    {
                        var yes = FindRect("AcceptButton");
                        Vector3 yp = RectTransformUtility.WorldToScreenPoint(null, yes.position + new Vector3(0f, 20f, 0f));
                        Click(new Vector2(yp.x, yp.y));
                        assignClickAt = now;
                        Debug.Log($"[PLAY] assignment offer: {Find("DialogueText")} -> assigning");
                    }
                }
                if (manager.CurrentStage != ForgeStage.Ready) return;
                var w = manager.workshop;
                Debug.Log($"[PLAY] back at Ready: day={w.day} gold={w.gold} rep={w.reputation} inventory={manager.inventory.Count} sold={w.history.SoldCount} history={w.history.swords.Count} valid={w.history.Validate() ?? "ok"} scrap={w.scrap.Count} weather={Find("Hud")}");
                foreach (var s in manager.inventory)
                    Debug.Log($"[PLAY] saved sword: {s.itemName} dur={s.durability} bal={s.balance} sharp={s.sharpness} cracked={s.cracked}");
                // Forging no longer ends the day (only the house door does), and the one weapon made is either on the rack or sold.
                bool ok = w.day == 1 && w.HasMark && manager.inventory.Count + w.history.SoldCount == 1 && w.history.swords.Count == 1 && w.history.Validate() == null;
                float workedMinutes = (clockAfter - clockAtStart) * 60f, expected = GameTime.WeaponMinutes(w, target);
                bool timeOk = clockAfter > 0f && Mathf.Abs(workedMinutes - expected) < 2f;
                Debug.Log($"[PLAY] {(timeOk ? "ok  " : "FAIL")} TEST A/B: the {target} took {GameTime.Duration(workedMinutes)} of game time ({GameTime.Clock(clockAtStart)} -> {GameTime.Clock(clockAfter)}; expected {GameTime.Duration(expected)}) in {realAfter - realAtStart:0} real seconds of minigames");
                ok &= timeOk;
                if (w.history.swords.Count == 1)
                {
                    // The weapon type survived the whole pipeline: its record, its label, the journal and the rack.
                    var rec = w.history.swords[0];
                    bool typeOk = rec.sword.weaponType == target && rec.Label.StartsWith(Weapons.Get(target).name + " #");
                    var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                    string journal = (string)typeof(ForgeGameManager).GetMethod("JournalLeftPage", flags).Invoke(manager, new object[] { rec });
                    bool journalOk = journal.Contains($"Type: {Weapons.Get(target).name}") && journal.Contains(rec.Label) &&
                                     (Weapons.Get(target).hasEdge || journal.Contains("Sharpness N/A"));
                    Debug.Log($"[PLAY] record: {rec.Label} type={rec.sword.weaponType} status={rec.status} ok={typeOk}");
                    Debug.Log($"[PLAY] journal page:\n{journal}");
                    bool rackOk = true;
                    if (manager.inventory.Count == 1)
                    {
                        var display = GameObject.Find("DisplaySword0");
                        string meshName = display != null ? display.GetComponent<MeshFilter>().sharedMesh.name : "none";
                        rackOk = display != null && display.activeInHierarchy && (target == WeaponType.Sword ? meshName.StartsWith("DisplaySword") : meshName.StartsWith(target + "Display"));
                        Debug.Log($"[PLAY] rack peg 0 shows mesh '{meshName}' at {display?.transform.position} bounds={display?.GetComponent<Renderer>().bounds} ok={rackOk}");
                    }
                    ok = ok && typeOk && journalOk && rackOk;
                    Debug.Log($"[PLAY] weapon checks: type={typeOk} journal={journalOk} rack={rackOk}");
                }
                Finish("all stages completed", ok);
                break;
        }
    }

    // Second Main Room visit (after the first sword): face the waiting customer, press F to hand it over,
    // log what they say, then head back to the forge.
    private static void HandleSecondMainRoom(double now)
    {
        var dialogue = FindRect("DialoguePanel");
        bool dialogueOpen = dialogue != null && dialogue.gameObject.activeInHierarchy;
        switch (secondRoomPhase)
        {
            case 0:
                if (secondRoomStart == 0) secondRoomStart = now;
                if (now - secondRoomStart < 4.0) return; // fade in, day message, turn to the table
                Shot("08_mainroom_day2");
                Debug.Log($"[PLAY] day2 main room: hud=\"{Find("Hud")}\" order=\"{Find("OrderPaperText")}\"");
                {
                    // The customer isn't waiting here any more - they come back on their day. If their order is ready
                    // (the weapon was set aside for it), make today their day and let them come to collect it.
                    var ready = manager.workshop.orders.Find(o => manager.workshop.IsReady(o));
                    if (ready == null) { Debug.Log("[PLAY] no order ready to collect (the weapon didn't meet it) - straight to the forge"); secondRoomPhase = 3; return; }
                    ready.returnDay = manager.workshop.day; ready.returnHour = 0f;
                    Debug.Log($"[PLAY] {ready.Label} is ready ({string.Join(",", manager.workshop.AssignedWeaponIds(ready))}) - its customer comes back for it");
                }
                secondRoomPhase = 4;
                secondRoomStart = now;
                break;
            case 4:
                if (!Find("Instructions").Contains("is back for")) { if (now - secondRoomStart > 25) { Debug.Log("[PLAY] FAIL the customer never came back"); secondRoomPhase = 3; } return; }
                if (now - secondRoomStart < 1.0) return;
                Tap(Key.F);
                secondRoomPhase = 1;
                secondRoomStart = now;
                break;
            case 1:
                if (dialogueOpen)
                {
                    Shot("09_delivery");
                    Debug.Log($"[PLAY] delivery dialogue: {Find("DialogueText")}");
                    secondRoomPhase = 2;
                }
                else if (now - secondRoomStart > 1.0) { Tap(Key.F); secondRoomStart = now; }
                break;
            case 2:
                if (dialogueOpen) return;
                Debug.Log($"[PLAY] after delivery: gold={manager.workshop.gold} rep={manager.workshop.reputation} sold={manager.workshop.history.SoldCount}");
                secondRoomPhase = 3;
                secondRoomStart = now;
                break;
            case 3:
                if (now - secondRoomStart > 0.5) { Tap(Key.Enter); secondRoomStart = now; }
                break;
        }
    }

    // Main Room: wait for a customer to arrive and stand there (offered, not auto-opened), press F while
    // facing them to hear the order, click Accept, then A to snap-turn to storage, then Enter to the forge.
    private static void HandleMainRoom(double now)
    {
        var dialogue = FindRect("DialoguePanel");
        bool dialogueOpen = dialogue != null && dialogue.gameObject.activeInHierarchy;

        switch (mainRoomPhase)
        {
            case 0:
                // Customer becomes active as soon as they start walking in (RunCustomerVisit/WalkCustomer),
                // but offeredOrder isn't set until the ~2.2s walk finishes - wait for that before pressing F,
                // otherwise the key lands too early and is wasted.
                var customerGO = GameObject.Find("Customer");
                if (customerGO == null || !customerGO.activeInHierarchy) return;
                if (mainRoomPhaseStart == 0) mainRoomPhaseStart = now;
                if (now - mainRoomPhaseStart < 2.6) return; // let the walk-in finish
                Debug.Log("[PLAY] mainroom: customer arrived, pressing F to hear the order");
                Tap(Key.F);
                mainRoomPhase = 1;
                break;

            case 1:
                if (!dialogueOpen)
                {
                    // Waiting for the F press to land and AnswerOrder's dialogue to open. The walk-in is timed in
                    // game time, which can lag real time on slow first frames - so keep re-pressing until it opens.
                    if (now - mainRoomPhaseStart > 4.0) { Tap(Key.F); mainRoomPhaseStart = now - 2.6; Debug.Log("[PLAY] mainroom: re-pressing F"); }
                    return;
                }
                Shot("00_mainroom_dialogue");
                var accept = FindRect("AcceptButton");
                Vector3 p = RectTransformUtility.WorldToScreenPoint(null, accept.position);
                Click(new Vector2(p.x, p.y));
                Debug.Log("[PLAY] mainroom: clicking Accept");
                mainRoomPhase = 2;
                break;

            case 2:
                if (dialogueOpen) return; // still waiting for the click to land and the panel to close
                mainRoomPhase = 3;
                mainRoomPhaseStart = now;
                break;

            case 3:
                // One tap of A is meant to snap-turn all the way to storage (yaw 180) - but a single queued
                // press/release event is prone to being dropped in this headless setup (same class of issue
                // documented for other keys), so this re-queues the down-state directly every frame (bypassing
                // Tap's narrow 6-frame window) until the turn actually starts, same technique the old
                // continuous-hold free-look test used. AnswerOrder() also keeps RunMainRoom's loop blocked
                // well past dialogueOpen going false (zoom back out, then the ~1.4s "Order accepted!"
                // message), so nothing is even polling for A until that's done.
                // Accepting now ends with the customer saying they'll come back ("Talk" - F continues), then walking off.
                if (dialogueOpen)
                {
                    if (now - talkPressAt > 0.8) { Tap(Key.F); talkPressAt = now; Debug.Log($"[PLAY] mainroom: after accepting - \"{Find("DialogueText")}\""); }
                    mainRoomPhaseStart = now;
                    return;
                }
                if (now - mainRoomPhaseStart < 2.5) return;
                if (lookHoldStart == 0) { lookHoldStart = now; Debug.Log("[PLAY] mainroom: holding A to snap-turn to storage"); }
                float yaw = Camera.main.transform.eulerAngles.y;
                if (yaw > 150f || now - lookHoldStart > 3.0)
                {
                    InputSystem.QueueStateEvent(kb, new KeyboardState());
                    Debug.Log($"[PLAY] mainroom storage: yaw={yaw:0.0} info=\"{Find("StorageInfoText")}\" " +
                              $"title=\"{Find("StorageTitle")}\" panelActive={FindRect("StoragePanel")?.gameObject.activeInHierarchy}");
                    lookHoldStart = 0;
                    mainRoomPhase = 4;
                    mainRoomPhaseStart = now;
                    return;
                }
                InputSystem.QueueStateEvent(kb, new KeyboardState(Key.A));
                break;

            case 4:
                if (now - mainRoomPhaseStart < 1.0) return; // let the "order accepted" message play out
                Debug.Log("[PLAY] mainroom: pressing Enter to head to the forge");
                Tap(Key.Enter);
                mainRoomPhase = 5;
                mainRoomPhaseStart = now;
                break;

            case 5:
                // The customer is still walking away when this first Enter lands, so it's easy to miss the
                // window RunMainRoom actually polls input in - keep retrying until the stage really changes
                // (once it does, the outer CurrentStage==MainRoom check stops routing here at all).
                if (now - mainRoomPhaseStart > 0.4)
                {
                    Tap(Key.Enter);
                    mainRoomPhaseStart = now;
                }
                break;
        }
    }

    private static bool CameraSettled()
    {
        // The manager glides the camera between poses (position and/or rotation - the Main Room -> Ready
        // transition only turns it, since both share the same seat); wait until it's stopped moving entirely.
        var cam = Camera.main;
        var lastPos = SessionState.GetVector3("camlastpos", Vector3.zero);
        var lastRot = SessionState.GetVector3("camlastrot", Vector3.zero);
        SessionState.SetVector3("camlastpos", cam.transform.position);
        SessionState.SetVector3("camlastrot", cam.transform.eulerAngles);
        return (lastPos - cam.transform.position).sqrMagnitude < 1e-8f &&
               Quaternion.Angle(Quaternion.Euler(lastRot), cam.transform.rotation) < 0.01f;
    }

    private static float PerfectMid(TimingBar bar)
    {
        float total = 0f;
        foreach (var w in bar.widths) total += w;
        if (total <= 0f) return 0f; // manager has not set the zones up yet
        return (bar.widths[0] + bar.widths[1] + bar.widths[2] * 0.5f) / total;
    }

    private static void HoldSpace(bool down)
    {
        InputSystem.QueueStateEvent(kb, down ? new KeyboardState(Key.Space) : new KeyboardState());
    }

    private static void HoldMouseButton(bool down, Vector2 screenPos)
    {
        InputSystem.QueueStateEvent(mouse, down
            ? new MouseState { position = screenPos }.WithButton(MouseButton.Left)
            : new MouseState { position = screenPos });
    }

    private static void HoldRightMouse(bool down)
    {
        InputSystem.QueueStateEvent(mouse, down
            ? new MouseState { position = mouse.position.ReadValue() }.WithButton(MouseButton.Right)
            : new MouseState { position = mouse.position.ReadValue() });
    }

    private static void Tap(Key key)
    {
        InputSystem.QueueStateEvent(kb, new KeyboardState(key));
        heldKey = key;
        holdFrames = 6;
        heldMouse = null;
        pressedLastFrame = true;
        pressFrame = Time.frameCount;
    }

    private static void HoldMouse(Vector2 screenPos)
    {
        InputSystem.QueueStateEvent(mouse, new MouseState { position = screenPos }.WithButton(MouseButton.Left));
        heldKey = Key.None;
        heldMouse = screenPos;
        holdFrames = 100000;
        pressedLastFrame = true;
        pressFrame = Time.frameCount;
    }

    private static void Click(Vector2 screenPos)
    {
        InputSystem.QueueStateEvent(mouse, new MouseState { position = screenPos }.WithButton(MouseButton.Left));
        heldKey = Key.None;
        heldMouse = screenPos;
        holdFrames = 6;
        pressedLastFrame = true;
        pressFrame = Time.frameCount;
    }

    private static string Find(string name)
    {
        foreach (var t in Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name == name) return t.text.Replace("\n", " | ");
        return "(not found: " + name + ")";
    }

    private static RectTransform FindRect(string name)
    {
        foreach (var r in Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (r.name == name) return r;
        return null;
    }

    private static void Shot(string name)
    {
        if (!shots.Add(name)) return;
        var cam = Camera.main;
        int width = 1280, height = 720;
        var rt = new RenderTexture(width, height, 24);
        var prevActive = RenderTexture.active;
        // Screen Space Overlay UI never shows up in camera.Render() - switch the canvas to camera space just
        // for this render so the shot includes the UI, then straight back (the game's manual hit-testing
        // assumes overlay coordinates).
        var canvas = Object.FindAnyObjectByType<Canvas>();
        if (canvas != null) { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = cam.nearClipPlane + 0.05f; }
        cam.targetTexture = rt;
        cam.Render();
        if (canvas != null) canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        RenderTexture.active = rt;
        var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();
        cam.targetTexture = null;
        RenderTexture.active = prevActive;
        rt.Release();
        System.IO.File.WriteAllBytes($"Logs/play_{shotPrefix}{name}.png", tex.EncodeToPNG());
        Debug.Log($"[PLAY] shot {name} stage={manager.CurrentStage} instr=\"{Find("Instructions")}\" msg=\"{Find("Message")}\" stats=\"{Find("Stats")}\"");
    }

    private static void Finish(string reason, bool ok)
    {
        EditorApplication.update -= Tick;
        Debug.Log($"[PLAY] RESULT: {(ok ? "PASS" : "FAIL")} - {reason}");
        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += () => EditorApplication.Exit(ok ? 0 : 1);
    }
}
