using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Play-mode test of the main menu and the scene flow, against a throwaway save file (TB_SAVE_PATH points at the
// temp folder - the real save is never touched) and with the player's settings backed up and restored. Buttons are
// pressed through the menu's own hit-testing (MainMenu.ClickAt clicks the centre of the control on screen).
//   1. no save: Continue hidden; Load Game page says "No save data found." and its Load can't start anything
//   2. Settings: keyboard focus + Left changes Master, a click on the SFX slider sets it, Fullscreen toggles,
//      the values survive GameSettings.Load() (PlayerPrefs), Back returns to the main page
//   3. New Game (no save yet, no question) -> fresh workshop; play a little, back to the menu
//   4. with a save: Continue + "Day 3 · Spring · 77 Gold · 1 Weapon Forged"; Load Game page shows it; Load restores it
//   5. Continue restores it too      6. New Game asks; Cancel (and Esc) keep the save byte for byte
//   7. New Game -> Yes -> fresh workshop, and the save file is the fresh game
//   8. an unreadable save: no Continue, the Load page says so, New Game still asks before replacing it
// Screenshots (UI included) at 1920x1080, 2560x1440, 3440x1440 and 1280x1024 go to Logs/menu_*.png.
// Run: Unity.exe -batchmode -projectPath <p> -executeMethod MainMenuPlaytest.Run   (no -quit; logs [MENU] lines)
public static class MainMenuPlaytest
{
    private static int step;
    private static double stepStart;
    private static bool ok = true;
    private static string savePath;
    private static string oldEnv;
    private static readonly Dictionary<string, object> savedPrefs = new Dictionary<string, object>();
    private static readonly string[] FloatPrefs = { "tb.volume.master", "tb.volume.music", "tb.volume.sfx" };
    private static readonly string[] IntPrefs = { "tb.screen.fullscreen", "tb.screen.width", "tb.screen.height" };

    public static void Run()
    {
        savePath = Path.Combine(Path.GetTempPath(), "tb_menu_test_save.json");
        if (File.Exists(savePath)) File.Delete(savePath);
        oldEnv = System.Environment.GetEnvironmentVariable("TB_SAVE_PATH");
        System.Environment.SetEnvironmentVariable("TB_SAVE_PATH", savePath);
        savedPrefs.Clear();
        foreach (var k in FloatPrefs) if (PlayerPrefs.HasKey(k)) savedPrefs[k] = PlayerPrefs.GetFloat(k);
        foreach (var k in IntPrefs) if (PlayerPrefs.HasKey(k)) savedPrefs[k] = PlayerPrefs.GetInt(k);
        step = 0;
        ok = true;
        GameSession.Mode = StartMode.Continue;
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
        EditorApplication.isPlaying = true;
        EditorApplication.update += Tick;
        stepStart = EditorApplication.timeSinceStartup;
    }

    private static void Check(bool condition, string what)
    {
        Debug.Log($"[MENU] {(condition ? "ok  " : "FAIL")} {what}");
        ok &= condition;
    }

    private static double Now => EditorApplication.timeSinceStartup;
    private static bool Waited(double seconds) => Now - stepStart >= seconds;
    private static void Next() { step++; stepStart = Now; }
    private static string Scene => SceneManager.GetActiveScene().name;
    private static MainMenu Menu => Object.FindAnyObjectByType<MainMenu>();
    private static ForgeGameManager Game => Object.FindAnyObjectByType<ForgeGameManager>();
    private static bool MenuReady => Scene == "MainMenu" && Menu != null && Waited(2.5);

    private static void Click(string name) => Check(Menu.ClickAt(name), $"click {name}");

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        if (Now - stepStart > 60) { Finish($"TIMEOUT at step {step} (scene {Scene})"); return; }
        if (ShotsPending()) return;

        switch (step)
        {
            case 0: // the menu is up, no save
                if (Menu == null || !Waited(4)) return;
                Check(Scene == "MainMenu" && Found("NewGameButton") && Found("LoadGameButton") && Found("SettingsButton") && Found("QuitButton"),
                    "the game opens on the main menu with New Game / Load Game / Settings / Quit");
                Check(!Menu.ContinueVisible && !Menu.HasValidSave, "no save: Continue is hidden");
                Check(Text("VersionLabel") == "Tiny Blacksmith Demo · v0.1.0", $"version label '{Text("VersionLabel")}'");
                Menu.MoveFocus(1);
                Check(Menu.FocusedName == "NewGameButton", $"keyboard Down focuses the first button ({Menu.FocusedName})");
                Menu.MoveFocus(-1);
                Check(Menu.FocusedName == "QuitButton", $"...and Up wraps round to Quit ({Menu.FocusedName})");
                Menu.Pointer(new Vector2(-50f, -50f), false, false, true);
                QueueShots("menu_01_nosave");
                Next();
                break;

            case 1: // Load Game without a save
                Click("LoadGameButton");
                Check(Menu.CurrentPage == MainMenu.Page.Load && Menu.LoadPageText == "No save data found." && !Menu.LoadButtonVisible,
                    $"Load Game with no save shows '{Menu.LoadPageText}' and no Load button");
                Menu.OnLoadSave(); // even forced, nothing starts
                QueueShot("menu_02_load_nosave", 1920, 1080);
                Next();
                break;
            case 2:
                if (!Waited(1)) return;
                Check(Scene == "MainMenu" && !Menu.Busy && Menu.MessageText == "No save data found.", $"a forced load with no save stays put ('{Menu.MessageText}')");
                Click("LoadBackButton");
                Check(Menu.CurrentPage == MainMenu.Page.Main, "Back returns to the main page");
                Click("SettingsButton");
                Check(Menu.CurrentPage == MainMenu.Page.Settings, "Settings opens in place (same scene)");
                Next();
                break;

            case 3: // Settings
                if (!Waited(0.5)) return;
                {
                    float master = GameSettings.MasterVolume;
                    Menu.MoveFocus(1);
                    Check(Menu.FocusedName == "MasterVolume", $"keyboard focus lands on Master Volume ({Menu.FocusedName})");
                    Menu.AdjustFocus(-1);
                    Check(Mathf.Abs(GameSettings.MasterVolume - (Mathf.Round((master - 0.05f) * 20f) / 20f)) < 0.001f && Mathf.Approximately(AudioListener.volume, GameSettings.MasterVolume),
                        $"Left lowers Master {master:0.00} -> {GameSettings.MasterVolume:0.00} (AudioListener {AudioListener.volume:0.00})");
                    Click("SfxVolume"); // the centre of the row is on the slider's left part
                    Check(GameSettings.SfxVolume > 0.05f && GameSettings.SfxVolume < 0.5f, $"clicking the SFX slider sets it ({GameSettings.SfxVolume:0.00})");
                    Menu.MoveFocus(-1);
                    Check(Menu.FocusedName == "MusicVolume", $"Up steps back through the rows ({Menu.FocusedName})");
                    Menu.AdjustFocus(1);
                    bool full = GameSettings.Fullscreen;
                    Click("Fullscreen");
                    Check(PlayerPrefs.GetInt("tb.screen.fullscreen", -1) == (full ? 0 : 1), $"Fullscreen toggle stored ({PlayerPrefs.GetInt("tb.screen.fullscreen", -1)})");
                    Debug.Log($"[MENU] resolutions offered: {string.Join(", ", GameSettings.Resolutions())}");
                    float m = GameSettings.MasterVolume, mu = GameSettings.MusicVolume, s = GameSettings.SfxVolume;
                    GameSettings.Load(); // as on the next launch
                    Check(Mathf.Approximately(m, GameSettings.MasterVolume) && Mathf.Approximately(mu, GameSettings.MusicVolume) && Mathf.Approximately(s, GameSettings.SfxVolume),
                        $"volumes persist through a reload ({GameSettings.MasterVolume:0.00}/{GameSettings.MusicVolume:0.00}/{GameSettings.SfxVolume:0.00})");
                    Menu.Pointer(new Vector2(-50f, -50f), false, false, true);
                    QueueShots("menu_03_settings");
                }
                Next();
                break;
            case 4:
                Menu.OnBack(); // Esc
                Check(Menu.CurrentPage == MainMenu.Page.Main, "Esc/Back from Settings returns to the main page");
                Click("NewGameButton"); // no save yet: no question asked
                Next();
                break;

            case 5: // fresh workshop
                if (Scene != "Prototype1" || Game == null || !Waited(2)) return;
                {
                    var w = Game.workshop;
                    Check(w.day == 1 && w.gold == Workshop.StartingGold && w.TotalStock == Workshop.StartingOre && w.unlockedMetals.Count == 1 && w.history.swords.Count == 0 && Game.inventory.Count == 0 && w.scrap.Count == 0,
                        $"New Game starts fresh (day {w.day}, {w.gold} gold, {w.history.swords.Count} records)");
                    Check(File.Exists(savePath), "the new game is saved straight away");
                    w.gold = 77;
                    w.day = 3;
                    w.history.RegisterForged(new WorkpieceData { weaponType = WeaponType.Dagger, materialName = "Iron", itemName = "Iron Dagger", durability = 55, balance = 90, sharpness = 80 }, 2);
                    Game.StartCoroutine(Game.GoToMainMenu());
                }
                Next();
                break;

            case 6: // back at the menu - Continue and its line
                if (!MenuReady) return;
                Check(Menu.ContinueVisible && Menu.SaveInfoText == "Day 3 · Spring · 77 Gold · 1 Weapon Forged", $"Continue shows the save ('{Menu.SaveInfoText}')");
                Menu.MoveFocus(1);
                Check(Menu.FocusedName == "ContinueButton", $"with a save, Continue is the first stop ({Menu.FocusedName})");
                Menu.Pointer(new Vector2(-50f, -50f), false, false, true);
                QueueShots("menu_04_withsave");
                Next();
                break;
            case 7:
                Click("LoadGameButton");
                Check(Menu.CurrentPage == MainMenu.Page.Load && Menu.LoadButtonVisible && Menu.LoadPageText.Contains("Day 3") && Menu.LoadPageText.Contains("77 Gold"),
                    $"the Load Game page shows the save:\n{Menu.LoadPageText}");
                QueueShot("menu_05_load", 1920, 1080);
                Next();
                break;
            case 8:
                Click("LoadSaveButton");
                Next();
                break;

            case 9: // loaded exactly
                if (Scene != "Prototype1" || Game == null || !Waited(2)) return;
                {
                    var w = Game.workshop;
                    var r = w.history.Find(1);
                    Check(w.day == 3 && w.gold == 77 && r != null && r.Label == "Dagger #0001" && Game.inventory.Count == 1,
                        $"Load restores the save (day {w.day}, {w.gold} gold, {r?.Label})");
                    Game.StartCoroutine(Game.GoToMainMenu());
                }
                Next();
                break;
            case 10:
                if (!MenuReady) return;
                Click("ContinueButton");
                Next();
                break;
            case 11:
                if (Scene != "Prototype1" || Game == null || !Waited(2)) return;
                Check(Game.workshop.day == 3 && Game.workshop.gold == 77 && Game.inventory.Count == 1, $"Continue restores the save (day {Game.workshop.day}, {Game.workshop.gold} gold)");
                Game.StartCoroutine(Game.GoToMainMenu());
                Next();
                break;

            case 12: // New Game over a save asks first; Cancel / Esc leave everything alone
                if (!MenuReady) return;
                {
                    string before = File.ReadAllText(savePath);
                    Click("NewGameButton");
                    Check(Menu.ConfirmOpen && Scene == "MainMenu", "New Game with a save asks for confirmation");
                    Check(!Menu.ClickAt("SettingsButton"), "the main buttons can't be pressed behind the question");
                    QueueShots("menu_06_confirm");
                    stepStart = Now;
                    Next();
                    confirmBefore = before;
                }
                break;
            case 13:
                Click("ConfirmCancel");
                Check(!Menu.ConfirmOpen && Scene == "MainMenu" && File.ReadAllText(savePath) == confirmBefore && Menu.ContinueVisible, "Cancel keeps the save untouched and stays on the menu");
                Menu.OnNewGame();
                Menu.OnBack(); // Esc
                Check(!Menu.ConfirmOpen && File.ReadAllText(savePath) == confirmBefore, "Esc closes the question too");
                Click("NewGameButton");
                Click("ConfirmYes");
                Next();
                break;

            case 14: // confirmed: fresh again, and the save is the fresh game
                if (Scene != "Prototype1" || Game == null || !Waited(2)) return;
                {
                    var w = Game.workshop;
                    Check(w.day == 1 && w.gold == Workshop.StartingGold && w.history.swords.Count == 0 && Game.inventory.Count == 0,
                        $"confirmed New Game resets everything (day {w.day}, {w.gold} gold, {w.history.swords.Count} records)");
                    var probe = new Workshop();
                    Check(SaveSystem.TryLoad(probe, out _) && probe.day == 1 && probe.history.swords.Count == 0, "...and the old save was overwritten by the fresh one");
                    Game.StartCoroutine(Game.GoToMainMenu());
                }
                Next();
                break;

            case 15: // an unreadable save file
                if (!MenuReady) return;
                Check(Menu.SaveInfoText == $"Day 1 · Spring · {Workshop.StartingGold} Gold · 0 Weapons Forged", $"fresh save line ('{Menu.SaveInfoText}')");
                File.WriteAllText(savePath, "{ this is not a save");
                Click("LoadGameButton");
                Check(Menu.LoadPageText == "The save file couldn't be read." && !Menu.LoadButtonVisible, $"a broken save: '{Menu.LoadPageText}', no Load button");
                Click("LoadBackButton");
                Check(!Menu.ContinueVisible, "...and no Continue");
                Click("NewGameButton");
                Check(Menu.ConfirmOpen, "...New Game still asks before replacing the file");
                Click("ConfirmCancel");
                Finish("done");
                break;
        }
    }

    private static string confirmBefore;

    private static bool Found(string name) => GameObject.Find(name) != null;

    private static string Text(string name)
    {
        var go = GameObject.Find(name);
        return go != null && go.TryGetComponent(out Text t) ? t.text : "";
    }

    // ---- screenshots with the (overlay) UI composited in, at any size -------------------------------------
    // The canvas is switched to camera space and the scaler's factor set by hand for the shot's height (what the
    // CanvasScaler would do at that screen size), then after a few frames of layout the camera renders it.

    private struct ShotJob { public string name; public int w, h; }
    private static readonly Queue<ShotJob> shots = new Queue<ShotJob>();
    private static int shotFrame = -1;
    private static double shotStart;
    private static RenderTexture shotRT;

    private static void QueueShot(string name, int w, int h) => shots.Enqueue(new ShotJob { name = name, w = w, h = h });

    private static void QueueShots(string name)
    {
        QueueShot(name + "_1920x1080", 1920, 1080);
        QueueShot(name + "_2560x1440", 2560, 1440);
        QueueShot(name + "_3440x1440", 3440, 1440);
        QueueShot(name + "_1280x1024", 1280, 1024);
    }

    private static bool ShotsPending()
    {
        if (shots.Count == 0) return false;
        var job = shots.Peek();
        var cam = Camera.main;
        var canvas = GameObject.Find("MainMenuUI").GetComponent<Canvas>();
        var scaler = canvas.GetComponent<CanvasScaler>();
        if (shotFrame < 0)
        {
            shotRT = new RenderTexture(job.w, job.h, 24);
            cam.targetTexture = shotRT;
            scaler.enabled = false;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            canvas.scaleFactor = job.h / 1080f;
            shotFrame = Time.frameCount;
            shotStart = Now;
            return true;
        }
        if (Time.frameCount < shotFrame + 4 || Now - shotStart < 0.6) return true; // layout, and the UI fades (real seconds)
        canvas.scaleFactor = job.h / 1080f;
        Canvas.ForceUpdateCanvases();
        Menu.Relayout();
        Canvas.ForceUpdateCanvases();
        var rect = ((RectTransform)canvas.transform).rect;
        Debug.Log($"[MENU] shot {job.name}: canvas {rect.width:0}x{rect.height:0} units");
        cam.Render();
        RenderTexture.active = shotRT;
        var tex = new Texture2D(job.w, job.h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, job.w, job.h), 0, 0);
        tex.Apply();
        cam.targetTexture = null;
        RenderTexture.active = null;
        shotRT.Release();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        scaler.enabled = true;
        Directory.CreateDirectory("Logs");
        File.WriteAllBytes($"Logs/{job.name}.png", tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        shots.Dequeue();
        shotFrame = -1;
        stepStart = Now;
        return true;
    }

    private static void Finish(string reason)
    {
        EditorApplication.update -= Tick;
        Debug.Log($"[MENU] RESULT: {(ok && reason == "done" ? "PASS" : "FAIL")} - {reason}");
        System.Environment.SetEnvironmentVariable("TB_SAVE_PATH", oldEnv);
        if (File.Exists(savePath)) File.Delete(savePath);
        foreach (var k in FloatPrefs) { if (savedPrefs.TryGetValue(k, out var v)) PlayerPrefs.SetFloat(k, (float)v); else PlayerPrefs.DeleteKey(k); }
        foreach (var k in IntPrefs) { if (savedPrefs.TryGetValue(k, out var v)) PlayerPrefs.SetInt(k, (int)v); else PlayerPrefs.DeleteKey(k); }
        PlayerPrefs.Save();
        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += () => EditorApplication.Exit(0);
    }
}
