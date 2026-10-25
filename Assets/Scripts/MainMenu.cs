using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The title screen (MainMenu scene, built by Tiny Blacksmith > Build Main Menu Scene from the workshop scene
// itself, so the background is the real city at golden hour). The menu sits in a column on the left, over a
// shade that fades out toward the middle; the right two thirds of the screen belong to the city.
//   Continue  - only there when a readable save exists; shows "Day 12 · Spring · 340 Gold · 18 Weapons Forged"
//               from that save underneath, and loads it.
//   New Game  - if any save file exists, asks first ("...will overwrite your current save. Continue?"), then
//               starts the workshop with GameSession.Mode = NewGame (ForgeGameManager skips loading and saves the
//               fresh game). Nothing is deleted here - the old save is only replaced once the new game saves.
//   Load Game - a page with the save slot's details and a Load button, or "No save data found."
//   Settings  - Master / Music / SFX volume, Fullscreen, Resolution (GameSettings, kept in PlayerPrefs), Back.
//   Quit      - closes the game (stops Play mode in the Editor).
// Mouse: hover highlights (a little brighter, 3% bigger, a soft tick), click presses. Keyboard: Up/Down (W/S) move,
// Enter/Space press, Left/Right (A/D) change a setting, Esc goes back. UI is built in code and hit-tested by hand,
// like ForgeUI (the project has no EventSystem - new Input System only); the look is in MenuWidgets.cs.
// The background moves a little, never enough to fight the buttons: the forge fire and its smoke/embers
// (ForgeEffects, the same as in-game), smoke from the city chimneys, drifting clouds and a slow camera sway.
public class MainMenu : MonoBehaviour
{
    public Transform clouds;    // children drift slowly across the sky (see MainMenuSceneBuilder)
    public Light forgeGlow;     // warm light in the firebox, flickered here

    public enum Page { Main, Load, Settings }

    // Layout, in canvas units: the canvas is always 1080 tall (CanvasScaler matches height), so these hold at any
    // resolution; only the shade's width follows the screen's width.
    private const float ColumnX = 96f, ColumnWidth = 500f, StackTop = 344f;
    private const float ButtonWidth = 360f, ButtonHeight = 54f, ButtonGap = 16f;

    private Camera cam;
    private Vector3 camPos;
    private Quaternion camRot;
    private float glowBase;

    private RectTransform canvasRect, shadeRect;
    private float laidOutWidth = -1f;
    private CanvasGroup mainGroup, loadGroup, settingsGroup, confirmGroup;
    private MenuButton continueButton, newButton, loadButton, settingsButton, quitButton;
    private MenuButton loadSaveButton, loadBackButton, settingsBackButton, yesButton, cancelButton;
    private SliderRow masterRow, musicRow, sfxRow;
    private ToggleRow fullscreenRow;
    private ChoiceRow resolutionRow;
    private List<Vector2Int> resolutions;
    private RectTransform settingsParent, loadCard, saveCard;
    private Text saveInfo, message, loadTitle, loadDetails, loadEmpty, settingsNote;
    private Image fade;
    private MenuControl[] mainItems, loadItems, settingsItems, confirmItems;
    private readonly List<MenuControl> allItems = new List<MenuControl>();

    private Page page = Page.Main;
    private float pageT = 1f, confirmT = 1f;
    private MenuControl focus, dragging;
    private Vector2 lastMouse = new Vector2(-1f, -1f);
    private float messageUntil, lastPreview;
    private bool busy;
    private Workshop saved;        // the save as it would load (null = no save, or one that can't be read)
    private bool saveFileExists;   // a save file is there at all (even an unreadable one - New Game still asks)
    private AudioSource uiAudio, musicSource;
    private AudioClip hoverClip, clickClip;

    // For the headless menu test.
    public Page CurrentPage => page;
    public bool ConfirmOpen => confirmGroup != null && confirmGroup.gameObject.activeSelf;
    public bool HasValidSave => saved != null;
    public bool ContinueVisible => continueButton.Visible;
    public string SaveInfoText => saveInfo.gameObject.activeInHierarchy ? saveInfo.text : "";
    public string LoadPageText => loadCard.gameObject.activeInHierarchy ? (loadEmpty.gameObject.activeSelf ? loadEmpty.text : loadDetails.text) : "";
    public bool LoadButtonVisible => loadSaveButton.Visible;
    public string MessageText => message != null ? message.text : "";
    public string FocusedName => focus != null ? focus.Name : "";
    public bool Busy => busy;

    private void Start()
    {
        cam = Camera.main;
        camPos = cam.transform.position;
        camRot = cam.transform.rotation;
        if (forgeGlow != null) glowBase = forgeGlow.intensity;
        BuildUI();
        BuildSounds();
        BuildEffects();
        RefreshSaveInfo();
        ShowPage(Page.Main);
        StartCoroutine(FadeTo(0f, 1.2f));
    }

    private void OnDisable() => GameSettings.Flush();

    // ---- UI ----------------------------------------------------------------------------------------

    private void BuildUI()
    {
        var canvasObj = new GameObject("MainMenuUI");
        var canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;
        canvasRect = (RectTransform)canvasObj.transform;
        Transform root = canvasObj.transform;

        // The shade: darkest at the left edge, easing out toward the middle (width set in Relayout).
        var shade = MenuArt.Rect("MenuShade", root).gameObject.AddComponent<RawImage>();
        shade.texture = MenuArt.Shade;
        shade.raycastTarget = false;
        shadeRect = shade.rectTransform;
        shadeRect.anchorMin = new Vector2(0f, 0f);
        shadeRect.anchorMax = new Vector2(0f, 1f);
        shadeRect.pivot = new Vector2(0f, 0.5f);
        shadeRect.anchoredPosition = Vector2.zero;

        // Everything else lives in one column, anchored to the left edge and the full height.
        var column = MenuArt.Rect("MenuColumn", root);
        column.anchorMin = new Vector2(0f, 0f);
        column.anchorMax = new Vector2(0f, 1f);
        column.pivot = new Vector2(0f, 0.5f);
        column.anchoredPosition = new Vector2(ColumnX, 0f);
        column.sizeDelta = new Vector2(ColumnWidth, 0f);

        BuildTitle(column);
        mainGroup = Group("MainPage", column);
        BuildMainPage(mainGroup.transform);
        loadGroup = Group("LoadPage", column);
        BuildLoadPage(loadGroup.transform);
        settingsGroup = Group("SettingsPage", column);
        BuildSettingsPage(settingsGroup.transform);

        message = MenuArt.Label("MenuMessage", column, 20, TextAnchor.MiddleCenter, MenuArt.Gold, FontStyle.Italic);
        MenuArt.Place(message.rectTransform, 0f, 836f, 480f, 30f);
        MenuArt.Soften(message);

        var version = MenuArt.Label("VersionLabel", root, 16, TextAnchor.LowerLeft, new Color(0.92f, 0.84f, 0.70f, 0.55f));
        var vr = version.rectTransform;
        vr.anchorMin = vr.anchorMax = vr.pivot = Vector2.zero;
        vr.anchoredPosition = new Vector2(28f, 20f);
        vr.sizeDelta = new Vector2(420f, 24f);
        version.text = "Tiny Blacksmith Demo · v" + Application.version;
        MenuArt.Soften(version, 0.6f);

        BuildConfirm(root);

        // Fade overlay last, so it covers everything while the scene fades in / out.
        fade = new GameObject("Fade").AddComponent<Image>();
        fade.transform.SetParent(root, false);
        fade.color = Color.black;
        fade.raycastTarget = false;
        MenuArt.Fill(fade.rectTransform);

        mainItems = new MenuControl[] { continueButton, newButton, loadButton, settingsButton, quitButton };
        loadItems = new MenuControl[] { loadSaveButton, loadBackButton };
        settingsItems = new MenuControl[] { masterRow, musicRow, sfxRow, fullscreenRow, resolutionRow, settingsBackButton };
        confirmItems = new MenuControl[] { yesButton, cancelButton };
        allItems.AddRange(mainItems);
        allItems.AddRange(loadItems);
        allItems.AddRange(settingsItems);
        allItems.AddRange(confirmItems);
        Relayout();
    }

    private static CanvasGroup Group(string name, Transform column)
    {
        var r = MenuArt.Rect(name, column);
        MenuArt.Fill(r);
        return r.gameObject.AddComponent<CanvasGroup>();
    }

    private MenuButton Button(string name, Transform parent, string text, int size, bool primary, System.Action onClick)
    {
        return new MenuButton(name, parent, text, size, primary) { onClick = onClick };
    }

    // TINY over BLACKSMITH, warm parchment-to-gold with a thin outline and a soft shadow, the subtitle, and a gold
    // rule with a diamond to close the block off from the buttons.
    private void BuildTitle(Transform column)
    {
        var tiny = MenuArt.Label("TitleTiny", column, 44, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
        tiny.text = "TINY";
        TitleLook(tiny);
        MenuArt.Place(tiny.rectTransform, 0f, 110f, ColumnWidth, 54f);

        var smith = MenuArt.Label("TitleBlacksmith", column, 70, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
        smith.text = "BLACKSMITH";
        TitleLook(smith);
        MenuArt.Place(smith.rectTransform, 0f, 158f, ColumnWidth, 84f);

        var sub = MenuArt.Label("Subtitle", column, 25, TextAnchor.MiddleCenter, MenuArt.WarmText, FontStyle.Italic);
        sub.text = "a cozy forge in a great old city";
        MenuArt.Soften(sub, 0.8f);
        MenuArt.Place(sub.rectTransform, 0f, 248f, ColumnWidth, 34f);

        var rule = MenuArt.Rect("Divider", column).gameObject.AddComponent<RawImage>();
        rule.texture = MenuArt.Rule;
        rule.raycastTarget = false;
        MenuArt.Place(rule.rectTransform, 0f, 298f, 320f, 3f);
        var gem = MenuArt.Image("DividerGem", rule.rectTransform, MenuArt.Diamond, Color.white, false);
        gem.rectTransform.sizeDelta = new Vector2(14f, 14f);
    }

    private static void TitleLook(Text t)
    {
        var g = t.gameObject.AddComponent<TextGradient>(); // before the outline/shadow, so it tints only the letters
        g.top = new Color(1f, 0.96f, 0.84f);
        g.bottom = new Color(0.94f, 0.74f, 0.42f);
        var o = t.gameObject.AddComponent<Outline>();
        o.effectColor = new Color(0.16f, 0.07f, 0.02f, 0.75f);
        o.effectDistance = new Vector2(1.2f, -1.2f);
        var s = t.gameObject.AddComponent<Shadow>();
        s.effectColor = new Color(0f, 0f, 0f, 0.38f);
        s.effectDistance = new Vector2(2f, -4f);
    }

    private static Text Heading(string name, Transform parent, string text)
    {
        var h = MenuArt.Label(name, parent, 28, TextAnchor.MiddleCenter, MenuArt.Gold, FontStyle.Bold);
        h.text = text;
        MenuArt.Soften(h);
        MenuArt.Place(h.rectTransform, 0f, StackTop, ColumnWidth, 40f);
        return h;
    }

    private void BuildMainPage(Transform parent)
    {
        continueButton = Button("ContinueButton", parent, "CONTINUE", 30, true, OnContinue);
        saveCard = MenuArt.Image("SaveInfoCard", parent, MenuArt.Card, new Color(1f, 1f, 1f, 0.9f)).rectTransform; // a small plaque, so the line reads over bright roofs
        saveInfo = MenuArt.Label("SaveInfo", parent, 19, TextAnchor.MiddleCenter, MenuArt.WarmText, FontStyle.Italic);
        MenuArt.Soften(saveInfo, 0.9f);
        newButton = Button("NewGameButton", parent, "NEW GAME", 25, false, OnNewGame);
        loadButton = Button("LoadGameButton", parent, "LOAD GAME", 25, false, OnLoadGame);
        settingsButton = Button("SettingsButton", parent, "SETTINGS", 25, false, OnSettings);
        quitButton = Button("QuitButton", parent, "QUIT", 25, false, OnQuit);
    }

    // Continue (and its save line) only when there's a save to continue; the rest close up under the title if not.
    private void LayoutMain()
    {
        float y = StackTop;
        bool cont = saved != null;
        continueButton.rect.gameObject.SetActive(cont);
        saveInfo.gameObject.SetActive(cont);
        saveCard.gameObject.SetActive(cont);
        if (cont)
        {
            MenuArt.Place(continueButton.rect, 0f, y, 390f, 64f);
            y += 64f + 7f;
            MenuArt.Place(saveInfo.rectTransform, 0f, y, 480f, 26f);
            MenuArt.Place(saveCard, 0f, y - 3f, 452f, 32f);
            y += 26f + 22f;
        }
        foreach (var b in new[] { newButton, loadButton, settingsButton })
        {
            MenuArt.Place(b.rect, 0f, y, ButtonWidth, ButtonHeight);
            y += ButtonHeight + ButtonGap;
        }
        MenuArt.Place(quitButton.rect, 0f, y + 14f, ButtonWidth, ButtonHeight); // a little apart from the rest
    }

    private void BuildLoadPage(Transform parent)
    {
        Heading("LoadHeading", parent, "LOAD GAME");
        loadCard = MenuArt.Image("SaveSlotCard", parent, MenuArt.Card, Color.white).rectTransform;
        loadTitle = MenuArt.Label("SlotTitle", loadCard, 17, TextAnchor.MiddleCenter, MenuArt.Gold, FontStyle.Bold);
        loadTitle.text = "SLOT 1  ·  YOUR SMITHY";
        MenuArt.Place(loadTitle.rectTransform, 0f, 20f, 400f, 24f);
        loadDetails = MenuArt.Label("SlotDetails", loadCard, 20, TextAnchor.UpperCenter, MenuArt.Parchment);
        loadDetails.lineSpacing = 1.15f;
        MenuArt.Place(loadDetails.rectTransform, 0f, 56f, 420f, 110f);
        MenuArt.Soften(loadDetails, 0.5f);
        loadEmpty = MenuArt.Label("NoSaveText", loadCard, 22, TextAnchor.MiddleCenter, MenuArt.WarmText, FontStyle.Italic);
        MenuArt.Fill(loadEmpty.rectTransform);
        loadSaveButton = Button("LoadSaveButton", loadCard, "LOAD", 25, true, OnLoadSave);
        loadBackButton = Button("LoadBackButton", parent, "BACK", 25, false, OnBack);
    }

    private void RefreshLoadPage()
    {
        bool has = saved != null;
        float cardHeight = has ? 262f : 120f;
        MenuArt.Place(loadCard, 0f, StackTop + 56f, 450f, cardHeight);
        loadTitle.gameObject.SetActive(has);
        loadDetails.gameObject.SetActive(has);
        loadSaveButton.rect.gameObject.SetActive(has);
        loadEmpty.gameObject.SetActive(!has);
        if (has)
        {
            var w = saved;
            int forged = w.history.swords.Count;
            string when = "";
            try { when = File.GetLastWriteTime(SaveSystem.PathOrNull).ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture); } catch { }
            loadDetails.text =
                $"Day {w.day} · {w.Season} · {GameTime.Clock(w.hour)}\n" +
                $"{w.gold} Gold · {forged} {(forged == 1 ? "Weapon" : "Weapons")} Forged · {w.history.SoldCount} Sold\n" +
                $"{w.history.customers.Count} townsfolk met · {w.orders.Count} open {(w.orders.Count == 1 ? "order" : "orders")}" +
                (when.Length > 0 ? $"\nLast saved {when}" : "");
            MenuArt.Place(loadSaveButton.rect, 0f, 186f, 240f, 54f);
        }
        else loadEmpty.text = saveFileExists ? "The save file couldn't be read." : "No save data found.";
        MenuArt.Place(loadBackButton.rect, 0f, StackTop + 56f + cardHeight + 24f, ButtonWidth, ButtonHeight);
    }

    private void BuildSettingsPage(Transform parent)
    {
        Heading("SettingsHeading", parent, "SETTINGS");
        const float rowHeight = 52f, pad = 14f;
        var card = MenuArt.Image("SettingsCard", parent, MenuArt.Card, Color.white).rectTransform;
        float cardHeight = pad * 2f + rowHeight * 5f + 26f; // + the note line at the bottom
        MenuArt.Place(card, 0f, StackTop + 56f, 470f, cardHeight);
        settingsParent = card;

        masterRow = new SliderRow("MasterVolume", card, "Master Volume", GameSettings.MasterVolume) { onChange = GameSettings.SetMasterVolume };
        musicRow = new SliderRow("MusicVolume", card, "Music Volume", GameSettings.MusicVolume) { onChange = GameSettings.SetMusicVolume };
        sfxRow = new SliderRow("SfxVolume", card, "SFX Volume", GameSettings.SfxVolume);
        sfxRow.onChange = v => { GameSettings.SetSfxVolume(v); PreviewSfx(); };
        fullscreenRow = new ToggleRow("Fullscreen", card, "Fullscreen", GameSettings.Fullscreen) { onChange = GameSettings.SetFullscreen };
        resolutions = GameSettings.Resolutions();
        var names = resolutions.ConvertAll(r => $"{r.x} × {r.y}");
        resolutionRow = new ChoiceRow("Resolution", card, "Resolution", names, resolutions.IndexOf(GameSettings.CurrentResolution))
            { onChange = i => GameSettings.SetResolution(resolutions[i]) };

        var rows = new MenuControl[] { masterRow, musicRow, sfxRow, fullscreenRow, resolutionRow };
        for (int i = 0; i < rows.Length; i++)
        {
            var r = rows[i].rect;
            r.anchorMin = new Vector2(0f, 1f);
            r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0f, -pad - i * rowHeight);
            r.sizeDelta = new Vector2(-20f, rowHeight - 4f);
        }

        // In the Editor the Game view owns the window size, so say so rather than seem broken.
        settingsNote = MenuArt.Label("SettingsNote", card, 16, TextAnchor.MiddleCenter, MenuArt.DimText, FontStyle.Italic);
        settingsNote.text = GameSettings.ScreenChangesApply ? "Settings are saved automatically." : "Fullscreen and resolution apply in the built game.";
        MenuArt.Soften(settingsNote, 0.6f);
        MenuArt.Place(settingsNote.rectTransform, 0f, pad + rowHeight * 5f + 2f, 440f, 22f);
        settingsBackButton = Button("SettingsBackButton", parent, "BACK", 25, false, OnBack);
        MenuArt.Place(settingsBackButton.rect, 0f, StackTop + 56f + cardHeight + 24f, ButtonWidth, ButtonHeight);
    }

    // Opening Settings shows what's true right now (Alt+Enter or the OS may have changed the window meanwhile).
    private void RefreshSettings()
    {
        masterRow.Set(GameSettings.MasterVolume, false);
        musicRow.Set(GameSettings.MusicVolume, false);
        sfxRow.Set(GameSettings.SfxVolume, false);
        fullscreenRow.Set(GameSettings.Fullscreen, false);
        int i = resolutions.IndexOf(GameSettings.CurrentResolution);
        if (i >= 0) resolutionRow.SetIndex(i, false);
    }

    private void BuildConfirm(Transform root)
    {
        var r = MenuArt.Rect("NewGameConfirm", root);
        MenuArt.Fill(r);
        confirmGroup = r.gameObject.AddComponent<CanvasGroup>();
        var dim = r.gameObject.AddComponent<Image>();
        dim.color = new Color(0.02f, 0.015f, 0.01f, 0.8f);
        dim.raycastTarget = false;

        var card = MenuArt.Image("ConfirmCard", r, MenuArt.Card, Color.white).rectTransform;
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
        card.anchoredPosition = new Vector2(0f, 20f);
        card.sizeDelta = new Vector2(660f, 284f);
        var title = MenuArt.Label("ConfirmTitle", card, 28, TextAnchor.MiddleCenter, MenuArt.Gold, FontStyle.Bold);
        title.text = "NEW GAME";
        MenuArt.Soften(title);
        MenuArt.Place(title.rectTransform, 0f, 28f, 600f, 38f);
        var text = MenuArt.Label("ConfirmText", card, 23, TextAnchor.MiddleCenter, MenuArt.Parchment);
        text.text = "Starting a new game will overwrite your current save.\nContinue?";
        text.lineSpacing = 1.2f;
        MenuArt.Soften(text, 0.6f);
        MenuArt.Place(text.rectTransform, 0f, 84f, 600f, 76f);
        yesButton = Button("ConfirmYes", card, "YES", 24, false, () => OnConfirmNewGame(true));
        MenuArt.Place(yesButton.rect, -124f, 194f, 210f, 52f);
        cancelButton = Button("ConfirmCancel", card, "CANCEL", 24, false, () => OnConfirmNewGame(false));
        MenuArt.Place(cancelButton.rect, 124f, 194f, 210f, 52f);
        r.gameObject.SetActive(false);
    }

    // The shade covers ~37% of the width (never narrower than the column plus a fade, on 4:3-ish screens).
    public void Relayout()
    {
        float w = canvasRect.rect.width;
        laidOutWidth = w;
        shadeRect.sizeDelta = new Vector2(Mathf.Max(w * 0.37f, 700f), 0f);
    }

    // "Day 12 · Spring · 340 Gold · 18 Weapons Forged" from the save as it would actually load.
    public static string Summary(Workshop w)
    {
        int n = w.history.swords.Count;
        return $"Day {w.day} · {w.Season} · {w.gold} Gold · {n} {(n == 1 ? "Weapon" : "Weapons")} Forged";
    }

    private void RefreshSaveInfo()
    {
        saveFileExists = SaveSystem.HasSave();
        saved = null;
        if (saveFileExists)
        {
            var probe = new Workshop();
            try { if (SaveSystem.TryLoad(probe, out _)) saved = probe; }
            catch (System.Exception e) { Debug.LogWarning($"MainMenu: the save couldn't be read ({e.Message})"); }
        }
        saveInfo.text = saved != null ? Summary(saved) : "";
        LayoutMain();
    }

    // ---- pages ---------------------------------------------------------------------------------------

    private void ShowPage(Page p)
    {
        page = p;
        dragging = null;
        SetFocus(null, false);
        mainGroup.gameObject.SetActive(p == Page.Main);
        loadGroup.gameObject.SetActive(p == Page.Load);
        settingsGroup.gameObject.SetActive(p == Page.Settings);
        pageT = 0f;
    }

    private CanvasGroup PageGroup => page == Page.Load ? loadGroup : page == Page.Settings ? settingsGroup : mainGroup;

    private MenuControl[] Items => ConfirmOpen ? confirmItems : page == Page.Load ? loadItems : page == Page.Settings ? settingsItems : mainItems;

    // ---- actions (public so the headless menu test can press them without simulated clicks) ----------

    public void OnContinue()
    {
        if (busy || saved == null) return;
        StartGame(StartMode.Continue);
    }

    public void OnNewGame()
    {
        if (busy) return;
        if (SaveSystem.HasSave()) OpenConfirm();
        else StartGame(StartMode.NewGame);
    }

    public void OnLoadGame()
    {
        if (busy) return;
        RefreshSaveInfo();
        RefreshLoadPage();
        ShowPage(Page.Load);
    }

    // The Load button on the Load Game page. Never starts a load that can't work.
    public void OnLoadSave()
    {
        if (busy) return;
        if (saved == null || !SaveSystem.HasSave())
        {
            ShowMessage("No save data found.");
            return;
        }
        StartGame(StartMode.LoadGame);
    }

    public void OnSettings()
    {
        if (busy) return;
        RefreshSettings();
        ShowPage(Page.Settings);
    }

    // Back / Esc: closes the question, or returns from Load Game / Settings to the main page.
    public void OnBack()
    {
        if (busy) return;
        if (ConfirmOpen) { OnConfirmNewGame(false); return; }
        if (page == Page.Main) return;
        GameSettings.Flush();
        var from = page;
        ShowPage(Page.Main);
        if (keyboardFocus) SetFocus(from == Page.Load ? loadButton : settingsButton, false); // land where they left
    }

    private void OpenConfirm()
    {
        confirmGroup.gameObject.SetActive(true);
        confirmT = 0f;
        SetFocus(keyboardFocus ? cancelButton : null, false); // the safe answer first
    }

    public void OnConfirmNewGame(bool yes)
    {
        if (!ConfirmOpen) return;
        confirmGroup.gameObject.SetActive(false);
        SetFocus(null, false);
        if (yes) StartGame(StartMode.NewGame);
    }

    public void OnQuit()
    {
        if (busy) return;
        busy = true;
        StartCoroutine(QuitGame());
    }

    private IEnumerator QuitGame()
    {
        GameSettings.Flush();
        yield return FadeTo(1f, 0.4f);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void ShowMessage(string text)
    {
        message.text = text;
        messageUntil = Time.unscaledTime + 2.5f;
    }

    private void StartGame(StartMode mode)
    {
        busy = true;
        GameSettings.Flush();
        GameSession.Mode = mode;
        StartCoroutine(LoadWorkshop());
    }

    private IEnumerator LoadWorkshop()
    {
        yield return FadeTo(1f, 0.6f);
        SceneManager.LoadScene(GameSession.WorkshopScene);
    }

    private IEnumerator FadeTo(float alpha, float duration)
    {
        float start = fade.color.a;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / duration)
        {
            fade.color = new Color(0f, 0f, 0f, Mathf.Lerp(start, alpha, t));
            yield return null;
        }
        fade.color = new Color(0f, 0f, 0f, alpha);
    }

    // ---- input ---------------------------------------------------------------------------------------

    private bool keyboardFocus; // the highlight came from the keyboard (so it survives the mouse sitting still)

    private void SetFocus(MenuControl item, bool sound)
    {
        if (focus == item) return;
        if (focus != null) focus.focused = false;
        focus = item;
        if (item == null) return;
        item.focused = true;
        if (sound) PlayUi(hoverClip, 0.10f);
    }

    // One pointer update: hover follows the mouse when it moves, a press activates what's under it, a held press
    // drags a slider. Public so the test can click real screen positions.
    public void Pointer(Vector2 pos, bool pressed, bool held, bool moved)
    {
        if (busy) return;
        if (dragging != null)
        {
            if (held) dragging.Drag(pos);
            else { dragging = null; GameSettings.Flush(); }
            return;
        }
        MenuControl over = null;
        foreach (var item in Items)
            if (item.interactable && item.Contains(pos)) over = item;
        if (moved && (over != null || !keyboardFocus))
        {
            keyboardFocus = false;
            SetFocus(over, true);
        }
        if (!pressed || over == null) return;
        keyboardFocus = false;
        SetFocus(over, false);
        PlayUi(clickClip, 0.22f);
        if (over.Drags) dragging = over;
        over.PointerDown(pos);
    }

    // Presses whatever is at the centre of the named control, through the same hit-testing as the mouse.
    public bool ClickAt(string name)
    {
        foreach (var item in Items)
        {
            if (item.Name != name || !item.Visible) continue;
            Vector2 c = item.ScreenCenter;
            Pointer(c, false, false, true);
            Pointer(c, true, true, false);
            Pointer(c, false, false, false);
            return true;
        }
        return false;
    }

    public void MoveFocus(int step)
    {
        var list = new List<MenuControl>();
        foreach (var item in Items) if (item.Visible && item.interactable) list.Add(item);
        if (list.Count == 0) return;
        int i = list.IndexOf(focus);
        i = i < 0 ? (step > 0 ? 0 : list.Count - 1) : (i + step + list.Count) % list.Count;
        keyboardFocus = true;
        SetFocus(list[i], true);
    }

    public void SubmitFocus()
    {
        if (focus == null || !focus.Visible) return;
        PlayUi(clickClip, 0.22f);
        focus.Activate();
    }

    public void AdjustFocus(int direction)
    {
        if (focus == null || !focus.Visible) return;
        focus.Adjust(direction);
        GameSettings.Flush();
        PlayUi(hoverClip, 0.12f);
    }

    private void HandleKeys(Keyboard kb)
    {
        if (kb.escapeKey.wasPressedThisFrame) { OnBack(); return; }
        int step = (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame ? 1 : 0) - (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame ? 1 : 0);
        if (step != 0) MoveFocus(step);
        int adjust = (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame ? 1 : 0) - (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame ? 1 : 0);
        if (adjust != 0) AdjustFocus(adjust);
        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) SubmitFocus();
    }

    // ---- sound ---------------------------------------------------------------------------------------

    // Two tiny synthesized sounds: a soft wooden tick for hover, a small brass "tink" for a press. SFX volume.
    private void BuildSounds()
    {
        uiAudio = gameObject.AddComponent<AudioSource>();
        uiAudio.playOnAwake = false;
        uiAudio.spatialBlend = 0f;
        hoverClip = Tone("ui_hover", 0.07f, 1180f, 2360f, 70f, 0.25f);
        clickClip = Tone("ui_click", 0.30f, 740f, 1850f, 20f, 0.45f);

        var music = Resources.Load<AudioClip>("Audio/music_menu");
        if (music != null)
        {
            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.clip = music;
            musicSource.loop = true;
            musicSource.spatialBlend = 0f;
            musicSource.volume = 0.5f * GameSettings.MusicVolume;
            musicSource.Play();
        }
    }

    private static AudioClip Tone(string name, float length, float f1, float f2, float decay, float noise)
    {
        const int rate = 44100;
        var d = new float[(int)(rate * length)];
        var rng = new System.Random(name.GetHashCode());
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)rate;
            float env = Mathf.Exp(-t * decay) * Mathf.Min(1f, t * 3000f);
            float v = 0.7f * Mathf.Sin(6.2831853f * f1 * t) + 0.3f * Mathf.Sin(6.2831853f * f2 * t) * Mathf.Exp(-t * decay);
            d[i] = 0.5f * (v * env + noise * ((float)rng.NextDouble() - 0.5f) * Mathf.Exp(-t * 400f));
        }
        var clip = AudioClip.Create(name, d.Length, 1, rate, false);
        clip.SetData(d, 0);
        return clip;
    }

    private void PlayUi(AudioClip clip, float volume)
    {
        if (uiAudio != null && clip != null) uiAudio.PlayOneShot(clip, volume * GameSettings.SfxVolume);
    }

    // Moving the SFX slider lets you hear the new level (not on every pixel of a drag).
    private void PreviewSfx()
    {
        if (Time.unscaledTime - lastPreview < 0.09f) return;
        lastPreview = Time.unscaledTime;
        PlayUi(hoverClip, 0.14f);
    }

    // ---- per frame -----------------------------------------------------------------------------------

    private void Update()
    {
        float t = Time.time;
        float dt = Time.unscaledDeltaTime;

        // A slow, small sway - the scene breathes, the buttons never move.
        cam.transform.SetPositionAndRotation(
            camPos + new Vector3(Mathf.Sin(t * 0.07f) * 0.2f, Mathf.Sin(t * 0.11f) * 0.05f, Mathf.Cos(t * 0.05f) * 0.15f),
            camRot * Quaternion.Euler(Mathf.Sin(t * 0.09f) * 0.25f, Mathf.Sin(t * 0.06f) * 0.6f, 0f));

        if (forgeGlow != null)
            forgeGlow.intensity = glowBase * (0.8f + 0.2f * Mathf.PerlinNoise(t * 2.7f, 0.37f) + 0.08f * Mathf.Sin(t * 13f));

        if (clouds != null)
            foreach (Transform c in clouds)
            {
                c.position += new Vector3(0.9f, 0f, 0.35f) * Time.deltaTime;
                if (c.position.x > 180f) c.position -= new Vector3(360f, 0f, 140f); // round again, out of sight
            }

        // UI: follow the window's shape, ease pages and the question in, animate the controls.
        if (!Mathf.Approximately(canvasRect.rect.width, laidOutWidth)) Relayout();
        if (pageT < 1f)
        {
            pageT = Mathf.Min(1f, pageT + dt / 0.2f);
            var g = PageGroup;
            g.alpha = Mathf.SmoothStep(0f, 1f, pageT);
            ((RectTransform)g.transform).anchoredPosition = new Vector2(-14f * (1f - g.alpha), 0f);
        }
        if (confirmT < 1f)
        {
            confirmT = Mathf.Min(1f, confirmT + dt / 0.15f);
            confirmGroup.alpha = Mathf.SmoothStep(0f, 1f, confirmT);
        }
        foreach (var item in allItems) item.Tick(dt);
        if (musicSource != null) musicSource.volume = 0.5f * GameSettings.MusicVolume;
        if (message.text.Length > 0 && Time.unscaledTime > messageUntil) message.text = "";

        if (busy) return;
        var mouse = Mouse.current;
        if (mouse != null)
        {
            Vector2 pos = mouse.position.ReadValue();
            bool moved = (pos - lastMouse).sqrMagnitude > 0.5f;
            lastMouse = pos;
            Pointer(pos, mouse.leftButton.wasPressedThisFrame, mouse.leftButton.isPressed, moved);
        }
        var kb = Keyboard.current;
        if (kb != null && !busy) HandleKeys(kb);
    }

    // ---- background life -------------------------------------------------------------------------------

    private void BuildEffects()
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        // The forge: the very same embers and smoke as in the game, burning steadily.
        var furnace = FindAnyObjectByType<ForgeStation>();
        if (furnace != null)
        {
            var pit = new Bounds(new Vector3(0f, 0.35f, 0f), new Vector3(0.6f, 0.7f, 0.6f));
            var chimney = new Vector3(0f, 2.4f, 0f);
            ForgeGameManager.MeasureFurnaceModel(furnace.transform, ref pit, ref chimney);
            Vector3 away = furnace.transform.position + Vector3.down * 50f; // no barrel/grindstone effects here
            var fx = ForgeEffects.Create(lit, furnace.transform, pit, chimney, away, away, Vector3.forward);
            fx.SetFire(0.75f);
            if (forgeGlow != null) forgeGlow.transform.position = furnace.transform.TransformPoint(pit.center) + Vector3.up * 0.4f;
        }

        // A thin wisp of smoke from every chimney in the village (the highest, compact bit of each house).
        var smokeMat = CityAmbience.SmokeMaterial();
        foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            string n = r.gameObject.name;
            if (!(n.StartsWith("House_") || n == "HouseStructure")) continue;
            if (ChimneyTop(r, out Vector3 top)) CityAmbience.ChimneySmoke(top, smokeMat);
        }
        // The environment-library houses mark their chimney tops (City/Chimneys, written by VillageBuilder): the
        // nearest dozen to the title camera smoke, like the game's own CityAmbience.
        var markers = GameObject.Find("Chimneys");
        if (markers != null && Camera.main != null)
        {
            var tops = new List<Vector3>();
            foreach (Transform t in markers.transform) tops.Add(t.position);
            Vector3 eye = Camera.main.transform.position;
            tops.Sort((a, b) => (a - eye).sqrMagnitude.CompareTo((b - eye).sqrMagnitude));
            for (int i = 0; i < Mathf.Min(12, tops.Count); i++) CityAmbience.ChimneySmoke(tops[i], smokeMat);
        }
    }

    // The chimney is the tallest part of a house model (its cap rises above the ridge); a house without one has
    // a long ridge beam as its top instead, which is too wide to be a chimney - no smoke from that one.
    private static bool ChimneyTop(MeshRenderer r, out Vector3 top)
    {
        top = Vector3.zero;
        if (!r.TryGetComponent(out MeshFilter filter)) return false;
        var mesh = filter.sharedMesh;
        if (mesh == null || !mesh.isReadable) return false;
        var verts = mesh.vertices;
        float maxY = float.MinValue;
        foreach (var v in verts) maxY = Mathf.Max(maxY, v.y);
        Bounds b = default;
        bool any = false;
        foreach (var v in verts)
        {
            if (v.y < maxY - 0.02f) continue;
            if (!any) { b = new Bounds(v, Vector3.zero); any = true; } else b.Encapsulate(v);
        }
        if (!any || b.size.x > 1.2f || b.size.z > 1.2f) return false;
        top = r.transform.TransformPoint(b.center) + Vector3.up * 0.1f;
        return true;
    }
}
