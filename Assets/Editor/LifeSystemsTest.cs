using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Checks for the "life between swords" systems (Workshop, CustomerOrder, SwordStories, MeadowLife).
//   RunLogic  - edit mode, no scene: generates orders, payments, stories and mark names and logs them
//               with a [LIFE] prefix. Run with -quit.
//   RunVisual - Play mode: renders the meadow in every season/weather and with a past customer walking
//               by wearing their sword, to Logs/life_*.png. No -quit (exits itself).
public static class LifeSystemsTest
{
    public static void RunLogic()
    {
        Random.InitState(42);
        var shop = new Workshop();
        bool ok = true;

        for (int i = 0; i < 6; i++)
        {
            var o = CustomerOrder.Generate(4 + i);
            o.deadlineDay = 4 + i + CustomerOrder.PatienceDays;
            Debug.Log($"[LIFE] order: {o.requestText} | reward={o.reward} need={o.NeedSummary()} | paper: {o.PaperText(4 + i).Replace("\n", " / ")}");
        }

        // A sword that clears the order by a mile (triumph) and one with a neglected stat (mishap).
        var order = CustomerOrder.Generate(1, null, Race.Human);
        order.weaponType = WeaponType.Sword; // these checks are about swords (weapon types have their own test)
        var great = new WorkpieceData { materialName = "Steel", durability = 95, balance = 95, sharpness = 95 };
        var sloppy = new WorkpieceData { materialName = "Copper", durability = 90, balance = 90, sharpness = 90 };
        if (order.minDurability == 0f) sloppy.durability = 5; else if (order.minBalance == 0f) sloppy.balance = 5; else sloppy.sharpness = 5;

        int pay = shop.Payment(order, great, out int tip);
        Debug.Log($"[LIFE] payment for great steel sword: {pay} + tip {tip} (base {order.reward})");
        ok &= pay > 0 && tip > 0 && order.IsFulfilled(great);

        order.detailsAsked = true;
        int askedPay = shop.Payment(order, great, out int askedTip);
        Debug.Log($"[LIFE] same, after asking for details: {askedPay} + tip {askedTip}");
        ok &= askedPay < pay && askedTip == 0;

        var cracked = great.Clone();
        cracked.cracked = true;
        ok &= !order.IsFulfilled(cracked);

        shop.mark = 0b111_101_111;
        var t1 = SwordStories.Tell(new SwordRecord { order = order, sword = great, dayDelivered = 1, status = SwordStatus.OwnedByCustomer }, shop);
        Debug.Log($"[LIFE] triumph story: {t1.text} (tip {t1.tip}, rep {t1.reputation:+0;-0})");
        ok &= t1.tip > 0 && t1.reputation > 0 && t1.text.Contains("ring");

        var t2 = SwordStories.Tell(new SwordRecord { order = order, sword = sloppy, dayDelivered = 1, status = SwordStatus.OwnedByCustomer }, shop);
        Debug.Log($"[LIFE] mishap story: {t2.text} (tip {t2.tip}, rep {t2.reputation:+0;-0})");
        ok &= t2.text.Contains("...");

        foreach (int m in new[] { 0b010_111_010, 0b111_111_111, 0b111_010_010, 0b100_000_001 })
        {
            shop.mark = m;
            Debug.Log($"[LIFE] mark {System.Convert.ToString(m, 2).PadLeft(9, '0')} -> {shop.MarkName}");
        }
        ok &= shop.MarkName == "2-dot";

        for (int d = 1; d <= 17; d += 4) { shop.day = d; Debug.Log($"[LIFE] day {d} -> {shop.Season}"); }

        Debug.Log($"[LIFE] RESULT: {(ok ? "PASS" : "FAIL")}");
    }

    // ---- visual ---------------------------------------------------------------------------------

    private static int shotIndex;
    private static int lastSlot;
    private static bool visitShot;
    private static double t0;
    private static MeadowLife life;
    private static SwordRecord storySword;
    private static readonly string[][] Shots =
    {
        new[] { "1", "Spring", "clear" }, new[] { "5", "Summer", "rain" },
        new[] { "9", "Autumn", "clear" }, new[] { "13", "Winter", "snow" },
    };

    public static void RunVisual()
    {
        System.Environment.SetEnvironmentVariable("TB_SAVE_PATH", "none"); // never touch the real save
        shotIndex = 0;
        lastSlot = -1;
        visitShot = false;
        visitPhase = 0;
        fFrames = 0;
        life = null;
        EditorSceneManager.OpenScene("Assets/Scenes/Prototype1.unity");
        EditorApplication.isPlaying = true;
        EditorApplication.update += Tick;
        t0 = EditorApplication.timeSinceStartup;
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        double now = EditorApplication.timeSinceStartup - t0;
        if (life == null)
        {
            life = Object.FindAnyObjectByType<MeadowLife>();
            if (life == null) return;
            // A past customer strolls by right away, wearing their sword.
            var order = CustomerOrder.Generate(1);
            var sold = new SwordRecord { id = 99, order = order, sword = new WorkpieceData { materialName = "Steel" }, legendName = "Whisper", status = SwordStatus.OwnedByCustomer };
            var city = Object.FindAnyObjectByType<ForgeGameManager>().City;
            if (city != null)
            {
                city.pickPasserby = () => sold;
                typeof(CityLife).GetField("passerbyCooldown", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(city, 0f);
            }

            // Make the first visitor someone who only came to talk: two blades in the scrap crate (one
            // cracked) for the Collector, and a sold sword with a story due.
            var shop = Object.FindAnyObjectByType<ForgeGameManager>().workshop;
            shop.scrap.Add(new WorkpieceData { materialName = "Iron" });
            shop.scrap.Add(new WorkpieceData { materialName = "Steel", cracked = true });
            storySword = shop.history.RegisterForged(new WorkpieceData { materialName = "Steel", durability = 90, balance = 90, sharpness = 90 }, 1);
            shop.history.Sell(storySword, shop.history.AddCustomer(order, true), order, 1);
            storySword.dayDelivered = -5;
            shop.day = 5;
            t0 = EditorApplication.timeSinceStartup;
            return;
        }

        if (!visitShot)
        {
            RunVisits(now);
            return;
        }

        // Then apply each season and shoot it a moment later (so rain/snow particles have had time to spawn).
        int slot = (int)((now - visitsDoneAt) / 1.2);
        if (slot < 0 || slot == lastSlot) return;
        lastSlot = slot;
        int index = slot / 2;
        if (index >= Shots.Length)
        {
            Debug.Log($"[LIFE] ambient line: {GameObject.Find("Ambient")?.GetComponent<UnityEngine.UI.Text>()?.text}");
            EditorApplication.update -= Tick;
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
            return;
        }
        var s = Shots[index];
        if (slot % 2 == 0)
        {
            life.ApplyDay(int.Parse(s[0]), s[1], s[2]);
            return;
        }
        Shot($"life_{index}_{s[1]}_{s[2]}");
        shotIndex = index + 1;
    }

    // The talk-only visits, driven directly: the game's own loop is stopped first so its order customer
    // can't grab the same F press. Story visit -> close with F -> Collector visit -> close with F.
    private static int visitPhase;
    private static double visitPhaseAt, visitsDoneAt;
    private static int fFrames;

    private static void RunVisits(double now)
    {
        var manager = Object.FindAnyObjectByType<ForgeGameManager>();
        var shop = manager.workshop;
        var kb = UnityEngine.InputSystem.Keyboard.current ?? UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
        if (fFrames > 0)
        {
            fFrames--;
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, fFrames > 0
                ? new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.F)
                : new UnityEngine.InputSystem.LowLevel.KeyboardState());
            return;
        }
        bool open = GameObject.Find("DialogueText") != null;
        string text = open ? GameObject.Find("DialogueText").GetComponent<UnityEngine.UI.Text>().text : "";
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;

        switch (visitPhase)
        {
            case 0:
                if (now < 3.0) return;
                UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
                UnityEngine.InputSystem.InputSystem.settings.editorInputBehaviorInPlayMode =
                    UnityEngine.InputSystem.InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                manager.StopAllCoroutines();
                GameObject.Find("DialoguePanel")?.SetActive(false);
                manager.StartCoroutine((System.Collections.IEnumerator)typeof(ForgeGameManager)
                    .GetMethod("StoryVisit", flags).Invoke(manager, new object[] { storySword }));
                visitPhase = 1; visitPhaseAt = now;
                break;
            case 1:
            case 4:
                if (!open) { if (now - visitPhaseAt > 8) { Debug.Log("[LIFE] visit dialogue never opened"); visitPhase = 6; } return; }
                if (now - visitPhaseAt < 1.0) return;
                Shot(visitPhase == 1 ? "life_story" : "life_collector");
                Debug.Log($"[LIFE] {(visitPhase == 1 ? "story" : "collector")} dialogue: {text.Replace("\n", " | ")}");
                fFrames = 6; visitPhase++; visitPhaseAt = now;
                break;
            case 2:
            case 5:
                if (open) { if (now - visitPhaseAt > 1.5) { fFrames = 6; visitPhaseAt = now; } return; }
                if (now - visitPhaseAt < 2.0) return; // let the reputation/gold message play
                Debug.Log($"[LIFE] after {(visitPhase == 2 ? "story" : "collector")}: gold={shop.gold} rep={shop.reputation} scrap={shop.scrap.Count} storyTold={storySword.storyTold} legend={storySword.legendName} status={storySword.status}");
                if (visitPhase == 2)
                {
                    manager.StartCoroutine((System.Collections.IEnumerator)typeof(ForgeGameManager)
                        .GetMethod("CollectorVisit", flags).Invoke(manager, null));
                    visitPhase = 4; visitPhaseAt = now;
                }
                else visitPhase = 6;
                break;
            case 6:
                visitShot = true;
                visitsDoneAt = now;
                break;
        }
    }

    private static void Shot(string name)
    {
        var cam = Camera.main;
        var canvas = Object.FindAnyObjectByType<Canvas>();
        if (canvas != null) { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = 0.5f; }
        var rt = new RenderTexture(1280, 720, 24);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        tex.Apply();
        cam.targetTexture = null;
        RenderTexture.active = null;
        rt.Release();
        System.IO.File.WriteAllBytes($"Logs/{name}.png", tex.EncodeToPNG());
        Debug.Log($"[LIFE] shot {name} weather={life.Weather}");
    }
}
