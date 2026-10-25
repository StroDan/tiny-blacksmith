using UnityEngine;
using UnityEngine.UI;

// A button that is hit-tested manually against the mouse position (no EventSystem
// needed - the project uses the new Input System only, and ForgeGameManager already
// polls Mouse.current for everything else).
public class UIButton
{
    public GameObject gameObject;
    public RectTransform rect;
    public Image background;
    public Text label;
    public Color normal;
    public Color hover;

    public bool Contains(Vector2 screenPos)
    {
        return gameObject.activeInHierarchy &&
               RectTransformUtility.RectangleContainsScreenPoint(rect, screenPos, null);
    }

    public void Show(bool visible) => gameObject.SetActive(visible);

    public void SetHover(bool isHover) => background.color = isHover ? hover : normal;
}

// One shelf slot in the storage panel: a wood-toned square with a colour icon. Empty slots have a fully
// transparent icon and a null tooltip, so Contains() (used for hover) naturally ignores them.
public class StorageSlot
{
    public GameObject gameObject;
    public RectTransform rect;
    public Image icon;
    public string tooltip;

    public bool Contains(Vector2 screenPos) =>
        !string.IsNullOrEmpty(tooltip) && RectTransformUtility.RectangleContainsScreenPoint(rect, screenPos, null);
}

// The five-zone bar drawn near the top of the screen.
public class TimingBarView
{
    private static readonly Color BadColor = new Color(0.78f, 0.22f, 0.20f);
    private static readonly Color GoodColor = new Color(0.36f, 0.76f, 0.30f);
    private static readonly Color PerfectColor = new Color(0.40f, 0.95f, 1.00f);

    private readonly GameObject root;
    private readonly RectTransform[] segments = new RectTransform[5];
    private readonly RectTransform pointer;

    public TimingBarView(GameObject root, RectTransform[] segments, RectTransform pointer)
    {
        this.root = root;
        this.segments = segments;
        this.pointer = pointer;
    }

    public void Show(bool visible) => root.SetActive(visible);

    public void SetZones(float[] widths)
    {
        float total = 0f;
        for (int i = 0; i < 5; i++) total += widths[i];

        float x = 0f;
        for (int i = 0; i < 5; i++)
        {
            float w = widths[i] / total;
            segments[i].anchorMin = new Vector2(x, 0f);
            segments[i].anchorMax = new Vector2(x + w, 1f);
            segments[i].offsetMin = Vector2.zero;
            segments[i].offsetMax = Vector2.zero;
            x += w;

            var image = segments[i].GetComponent<Image>();
            image.color = i == 2 ? PerfectColor : (i == 1 || i == 3) ? GoodColor : BadColor;
        }
    }

    public void SetPointer(float t)
    {
        t = Mathf.Clamp01(t);
        pointer.anchorMin = new Vector2(t, 0f);
        pointer.anchorMax = new Vector2(t, 1f);
        pointer.offsetMin = new Vector2(-4f, -10f);
        pointer.offsetMax = new Vector2(4f, 10f);
    }
}

// The furnace's two vertical bars (see HeatGame): the left one shows the bouncing fire and the player's
// RMB-controlled catcher rectangle, the right one is a simple fill bar for heating progress. Built once by
// ForgeUI.Create() and driven every frame from ForgeGameManager.RunFurnace via SetFire/SetCatcher/SetProgress.
public class HeatBarView
{
    private readonly GameObject root;
    private readonly RectTransform fireMarker;
    private readonly RectTransform catcherRect;
    private readonly Image catcherImage;
    private readonly RectTransform progressFill;
    private readonly float barHeight;

    private static readonly Color CaughtColor = new Color(0.42f, 0.92f, 0.36f, 0.60f);
    private static readonly Color MissedColor = new Color(0.55f, 0.58f, 0.62f, 0.30f);

    public HeatBarView(GameObject root, RectTransform fireMarker, RectTransform catcherRect, Image catcherImage, RectTransform progressFill, float barHeight)
    {
        this.root = root;
        this.fireMarker = fireMarker;
        this.catcherRect = catcherRect;
        this.catcherImage = catcherImage;
        this.progressFill = progressFill;
        this.barHeight = barHeight;
    }

    public void Show(bool visible) => root.SetActive(visible);

    public void SetFire(float normalizedY) => fireMarker.anchoredPosition = new Vector2(0f, normalizedY * barHeight);

    public void SetCatcher(float normalizedY, float halfHeight, bool caught)
    {
        catcherRect.anchoredPosition = new Vector2(0f, normalizedY * barHeight);
        catcherRect.sizeDelta = new Vector2(catcherRect.sizeDelta.x, halfHeight * 2f * barHeight);
        catcherImage.color = caught ? CaughtColor : MissedColor;
    }

    public void SetProgress(float t)
    {
        Vector2 max = progressFill.anchorMax;
        max.y = Mathf.Lerp(0.03f, 0.97f, Mathf.Clamp01(t));
        progressFill.anchorMax = max;
    }
}

// Builds every piece of runtime UI in code so the scene doesn't have to be rebuilt
// or hand-wired when the layout changes.
public class ForgeUI
{
    public Text instructions;
    public Text message;
    public Text stats;
    public Text resultTitle;
    public Text resultStats;
    public TimingBarView bar;
    public HeatBarView heatBar;
    public UIButton finishButton;
    public UIButton discardButton;
    public UIButton addButton;
    public Text orderPaper;
    public Text dialogueText;
    public UIButton acceptButton;
    public UIButton declineButton;
    public StorageSlot[] materialSlots;
    public StorageSlot[] swordSlots;
    public Text storageInfo;       // write it through SetStorageInfo (keeps the card sized to the text)
    private RectTransform storageInfoRect;
    public Text hud;           // day / season / gold / reputation, top-right, always on
    public Text ambient;       // small bottom line for things happening out on the meadow
    public UIButton askButton;      // "Ask for details" in the order dialogue
    public UIButton continueButton; // for customers who only came to talk (stories, the Collector)
    public UIButton[] markCells;    // 3x3 maker's mark editor, index = MarkBit(row, col)
    public UIButton markDoneButton;
    public Image[] markStamp;       // the mark shown small on the result screen
    public Text markStampLabel;
    public Text prompt;             // "[E] Open Journal" / "[E] End the Day" while looking at an Interactable

    // The Blacksmith Journal: an open book - sword identity and stats on the left page, its story on the right.
    public Text journalLeft;
    public Text journalRight;
    public Text journalCounter;
    public Image journalBlade;      // a little sword drawn on the left page, tinted to the blade's metal
    public GameObject journalSword;
    // One little drawing per weapon type on the journal's left page (the sword's is journalSword), and the
    // images in each that are the metal - tinted to what that weapon was forged from.
    private readonly System.Collections.Generic.Dictionary<WeaponType, GameObject> journalIcons = new System.Collections.Generic.Dictionary<WeaponType, GameObject>();
    private readonly System.Collections.Generic.Dictionary<WeaponType, System.Collections.Generic.List<Image>> journalIconMetal = new System.Collections.Generic.Dictionary<WeaponType, System.Collections.Generic.List<Image>>();
    // The Material Order Book (by the storage rack): one row per unlocked metal with an "Order 1" button.
    public Text ledgerTitle;
    public Text[] ledgerRows;
    public UIButton[] ledgerOrder;
    public Text ledgerFooter;
    public Text ledgerStatus;
    public UIButton ledgerClose;
    private GameObject ledgerPanel;
    private RectTransform ledgerCover, ledgerCard;
    // "Return to the main menu?" (Esc in the Main Room).
    public Text confirmText;
    public UIButton confirmYes;
    public UIButton confirmNo;
    private GameObject confirmPanel;
    public UIButton journalPrev;
    public UIButton journalNext;
    public UIButton journalClose;
    private GameObject journalPanel;

    private GameObject resultPanel;
    private GameObject orderPanel;
    private GameObject dialoguePanel;
    private GameObject storagePanel;
    private Image fade;
    private GameObject markPanel;
    private GameObject markStampRoot;

    public static ForgeUI Create()
    {
        var ui = new ForgeUI();
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var canvasObj = new GameObject("ForgeUI");
        var canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5;
        var scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 0.5f;
        Transform root = canvasObj.transform;

        // Fade overlay first so it sits behind every text/button.
        var fadeObj = new GameObject("Fade");
        fadeObj.transform.SetParent(root, false);
        ui.fade = fadeObj.AddComponent<Image>();
        ui.fade.color = new Color(0f, 0f, 0f, 0f);
        ui.fade.raycastTarget = false;
        var fadeRect = ui.fade.rectTransform;
        fadeRect.anchorMin = Vector2.zero;
        fadeRect.anchorMax = Vector2.one;
        fadeRect.offsetMin = Vector2.zero;
        fadeRect.offsetMax = Vector2.zero;

        ui.instructions = MakeText("Instructions", root, font, 24, TextAnchor.UpperCenter,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(880f, 110f));

        // Day / season / gold / reputation, tucked into the top-right corner beside the instructions.
        ui.hud = MakeText("Hud", root, font, 20, TextAnchor.UpperRight,
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -14f), new Vector2(180f, 100f));

        ui.ambient = MakeText("Ambient", root, font, 22, TextAnchor.LowerCenter,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(1000f, 60f));
        ui.ambient.fontStyle = FontStyle.Italic;

        ui.prompt = MakeText("Prompt", root, font, 26, TextAnchor.LowerCenter,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 84f), new Vector2(700f, 40f));
        ui.prompt.fontStyle = FontStyle.Bold;

        ui.bar = MakeBar(root);
        ui.heatBar = MakeHeatBars(root, font);

        ui.message = MakeText("Message", root, font, 44, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(1100f, 120f));
        ui.message.fontStyle = FontStyle.Bold;

        ui.stats = MakeText("Stats", root, font, 20, TextAnchor.LowerLeft,
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20f, 20f), new Vector2(640f, 150f));

        ui.finishButton = MakeButton("FinishButton", root, font, "Finish (Enter)", 24,
            new Vector2(1f, 0f), new Vector2(-20f, 20f), new Vector2(260f, 60f), new Color(0.25f, 0.45f, 0.25f));
        ui.finishButton.Show(false);

        // Result screen (hidden until the sword is finished).
        ui.resultPanel = new GameObject("ResultPanel");
        ui.resultPanel.transform.SetParent(root, false);
        var panelRect = ui.resultPanel.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        ui.resultTitle = MakeText("ResultTitle", ui.resultPanel.transform, font, 42, TextAnchor.UpperCenter,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(900f, 70f));
        ui.resultTitle.fontStyle = FontStyle.Bold;

        // Stat list, one line per stat from top to bottom, on the left so the spinning sword stays clear in the middle.
        ui.resultStats = MakeText("ResultStats", ui.resultPanel.transform, font, 34, TextAnchor.MiddleLeft,
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(70f, 0f), new Vector2(420f, 320f));
        ui.resultStats.lineSpacing = 1.25f;

        ui.discardButton = MakeButton("DiscardButton", ui.resultPanel.transform, font, "Scrap Crate", 28,
            new Vector2(0.5f, 0f), new Vector2(-150f, 60f), new Vector2(260f, 64f), new Color(0.55f, 0.2f, 0.2f));
        ui.addButton = MakeButton("AddButton", ui.resultPanel.transform, font, "Hang on the Rack", 28,
            new Vector2(0.5f, 0f), new Vector2(150f, 60f), new Vector2(260f, 64f), new Color(0.2f, 0.5f, 0.3f));

        // The maker's mark, stamped small on the right of the result screen once it has been designed.
        ui.markStampRoot = new GameObject("MarkStamp");
        ui.markStampRoot.transform.SetParent(ui.resultPanel.transform, false);
        var stampRect = ui.markStampRoot.AddComponent<RectTransform>();
        stampRect.anchorMin = stampRect.anchorMax = stampRect.pivot = new Vector2(1f, 0.5f);
        stampRect.anchoredPosition = new Vector2(-90f, 0f);
        stampRect.sizeDelta = new Vector2(96f, 96f);
        var stampBg = ui.markStampRoot.AddComponent<Image>();
        stampBg.color = new Color(0.20f, 0.14f, 0.09f, 0.88f);
        stampBg.raycastTarget = false;
        ui.markStamp = new Image[9];
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 3; col++)
            {
                var dot = new GameObject("Dot").AddComponent<Image>();
                dot.transform.SetParent(ui.markStampRoot.transform, false);
                dot.raycastTarget = false;
                var r = dot.rectTransform;
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
                r.sizeDelta = new Vector2(22f, 22f);
                r.anchoredPosition = new Vector2((col - 1) * 28f, (1 - row) * 28f);
                ui.markStamp[MarkBit(row, col)] = dot;
            }
        ui.markStampLabel = MakeText("MarkStampLabel", ui.markStampRoot.transform, font, 16, TextAnchor.UpperCenter,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, -30f), new Vector2(200f, 24f));

        // Maker's mark editor: shown once, on the very first result screen (see ForgeGameManager.DesignMark).
        ui.markPanel = new GameObject("MarkPanel");
        ui.markPanel.transform.SetParent(ui.resultPanel.transform, false);
        var markBg = ui.markPanel.AddComponent<Image>();
        markBg.color = new Color(0.85f, 0.80f, 0.64f, 0.96f);
        markBg.raycastTarget = false;
        var markRect = markBg.rectTransform;
        markRect.anchorMin = markRect.anchorMax = markRect.pivot = new Vector2(1f, 0.5f);
        markRect.anchoredPosition = new Vector2(-30f, 10f);
        markRect.sizeDelta = new Vector2(320f, 420f);
        var markTitle = MakeText("MarkTitle", ui.markPanel.transform, font, 18, TextAnchor.UpperCenter,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(300f, 80f));
        markTitle.color = new Color(0.15f, 0.10f, 0.05f);
        markTitle.GetComponent<Outline>().enabled = false;
        markTitle.text = "Design your MAKER'S MARK\nClick the squares to draw it.\nIt goes on every blade you forge.";
        ui.markCells = new UIButton[9];
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 3; col++)
            {
                var cell = MakeButton("MarkCell", ui.markPanel.transform, font, "", 10,
                    new Vector2(0.5f, 0.5f), new Vector2((col - 1) * 78f, (1 - row) * 78f - 60f), new Vector2(70f, 70f),
                    new Color(0.55f, 0.47f, 0.36f));
                ui.markCells[MarkBit(row, col)] = cell;
            }
        ui.markDoneButton = MakeButton("MarkDoneButton", ui.markPanel.transform, font, "Stamp it!", 24,
            new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(200f, 50f), new Color(0.2f, 0.5f, 0.3f));
        ui.markPanel.SetActive(false);

        ui.resultPanel.SetActive(false);

        // Order paper: a little parchment note that stays up on the left for the whole trip to the forge
        // once a customer's order is accepted.
        ui.orderPanel = new GameObject("OrderPanel");
        ui.orderPanel.transform.SetParent(root, false);
        var orderBg = ui.orderPanel.AddComponent<Image>();
        orderBg.color = new Color(0.85f, 0.80f, 0.64f, 0.94f);
        orderBg.raycastTarget = false;
        var orderRect = orderBg.rectTransform;
        orderRect.anchorMin = new Vector2(0f, 1f);
        orderRect.anchorMax = new Vector2(0f, 1f);
        orderRect.pivot = new Vector2(0f, 1f);
        orderRect.anchoredPosition = new Vector2(20f, -210f); // below the instructions and the timing bar (which ends ~195 down)
        orderRect.sizeDelta = new Vector2(270f, 240f);

        ui.orderPaper = MakeText("OrderPaperText", ui.orderPanel.transform, font, 17, TextAnchor.UpperLeft,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -12f), new Vector2(242f, 216f));
        ui.orderPaper.color = new Color(0.15f, 0.10f, 0.05f);
        ui.orderPaper.GetComponent<Outline>().enabled = false; // a dark outline reads badly on light parchment
        ui.orderPanel.SetActive(false);

        // Dialogue box: appears at the bottom of the screen while a customer is talking.
        ui.dialoguePanel = new GameObject("DialoguePanel");
        ui.dialoguePanel.transform.SetParent(root, false);
        var dialogueBg = ui.dialoguePanel.AddComponent<Image>();
        dialogueBg.color = new Color(0.05f, 0.05f, 0.05f, 0.78f);
        dialogueBg.raycastTarget = false;
        var dialogueRect = dialogueBg.rectTransform;
        dialogueRect.anchorMin = new Vector2(0.5f, 0f);
        dialogueRect.anchorMax = new Vector2(0.5f, 0f);
        dialogueRect.pivot = new Vector2(0.5f, 0f);
        dialogueRect.anchoredPosition = new Vector2(0f, 40f);
        dialogueRect.sizeDelta = new Vector2(940f, 250f); // room for a customer who talks (NpcStories keeps them under ~360 characters)

        ui.dialogueText = MakeText("DialogueText", ui.dialoguePanel.transform, font, 22, TextAnchor.UpperCenter,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(890f, 170f));
        // A long speech (someone sent by a friend, a special commission) shrinks a little rather than running off the box.
        ui.dialogueText.verticalOverflow = VerticalWrapMode.Truncate; // (Best Fit only shrinks text that is kept inside its box)
        ui.dialogueText.resizeTextForBestFit = true;
        ui.dialogueText.resizeTextMinSize = 16;
        ui.dialogueText.resizeTextMaxSize = 22;

        ui.acceptButton = MakeButton("AcceptButton", ui.dialoguePanel.transform, font, "Accept Order", 24,
            new Vector2(0.5f, 0f), new Vector2(250f, 14f), new Vector2(220f, 52f), new Color(0.2f, 0.5f, 0.3f));
        ui.askButton = MakeButton("AskButton", ui.dialoguePanel.transform, font, "Ask for details", 22,
            new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(220f, 52f), new Color(0.35f, 0.33f, 0.45f));
        ui.declineButton = MakeButton("DeclineButton", ui.dialoguePanel.transform, font, "Decline", 24,
            new Vector2(0.5f, 0f), new Vector2(-250f, 14f), new Vector2(220f, 52f), new Color(0.55f, 0.2f, 0.2f));
        ui.continueButton = MakeButton("ContinueButton", ui.dialoguePanel.transform, font, "Continue", 24,
            new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(220f, 52f), new Color(0.2f, 0.5f, 0.3f));
        ui.dialoguePanel.SetActive(false);

        // Storage: the rack, pegs and bins are real 3D geometry (LowPoly.StorageRack / MaterialBin /
        // DisplaySword, see ForgeGameManager), so this full-screen container only holds invisible hover
        // hit-boxes that ForgeGameManager lines up over the 3D rack every frame (PlaceSlot), plus one compact
        // parchment info card in the top-right corner, below the HUD.
        ui.storagePanel = new GameObject("StoragePanel");
        ui.storagePanel.transform.SetParent(root, false);
        var storageRect = ui.storagePanel.AddComponent<RectTransform>();
        storageRect.anchorMin = Vector2.zero;
        storageRect.anchorMax = Vector2.one;
        storageRect.offsetMin = Vector2.zero;
        storageRect.offsetMax = Vector2.zero;

        ui.materialSlots = new StorageSlot[ForgeMaterials.All.Length];
        for (int i = 0; i < ui.materialSlots.Length; i++)
            ui.materialSlots[i] = MakeSlot(ui.storagePanel.transform);

        ui.swordSlots = new StorageSlot[8];
        for (int i = 0; i < ui.swordSlots.Length; i++)
            ui.swordSlots[i] = MakeSlot(ui.storagePanel.transform);

        // The info card: a narrow parchment whose height follows its text (SetStorageInfo). The text is a
        // child of the card, anchored to its top-left with a fixed margin, so it can never drift outside it.
        var infoBg = new GameObject("StorageInfoBg").AddComponent<Image>();
        infoBg.transform.SetParent(ui.storagePanel.transform, false);
        infoBg.color = new Color(0.85f, 0.80f, 0.64f, 0.94f);
        infoBg.raycastTarget = false;
        ui.storageInfoRect = infoBg.rectTransform;
        ui.storageInfoRect.anchorMin = ui.storageInfoRect.anchorMax = ui.storageInfoRect.pivot = new Vector2(1f, 1f);
        ui.storageInfoRect.anchoredPosition = new Vector2(-16f, -214f); // just below the HUD (up to 8 lines)
        ui.storageInfoRect.sizeDelta = new Vector2(StorageInfoWidth, 100f);

        ui.storageInfo = MakeText("StorageInfoText", infoBg.transform, font, 16, TextAnchor.UpperLeft,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(StorageInfoMargin, -StorageInfoMargin),
            new Vector2(StorageInfoWidth - 2f * StorageInfoMargin, 80f));
        ui.storageInfo.color = new Color(0.15f, 0.10f, 0.05f);
        ui.storageInfo.GetComponent<Outline>().enabled = false;
        ui.storageInfo.lineSpacing = 1.05f;
        ui.SetStorageInfo("<b>STORAGE</b>");

        ui.storagePanel.SetActive(false);

        MakeJournal(ui, root, font);
        MakeConfirm(ui, root, font);
        MakeLedger(ui, root, font);

        ui.bar.Show(false);
        return ui;
    }

    private static readonly Color Ink = new Color(0.15f, 0.10f, 0.05f);

    // The weapon drawings on the left page sit in their own band between the title and the text (the taller ones -
    // the sickle's crescent, the hammer's head - used to reach up into the title).
    private const float JournalIconY = 188f;

    private static void MakeJournal(ForgeUI ui, Transform root, Font font)
    {
        ui.journalPanel = new GameObject("JournalPanel");
        ui.journalPanel.transform.SetParent(root, false);
        var dim = ui.journalPanel.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.55f);
        dim.raycastTarget = false;
        var dimRect = dim.rectTransform;
        dimRect.anchorMin = Vector2.zero;
        dimRect.anchorMax = Vector2.one;
        dimRect.offsetMin = Vector2.zero;
        dimRect.offsetMax = Vector2.zero;

        // Leather cover, a little bigger than the two pages on it.
        Image cover = MakeImage("JournalCover", ui.journalPanel.transform, new Color(0.36f, 0.15f, 0.09f), new Vector2(0f, 20f), new Vector2(1060f, 620f));
        MakeImage("JournalPageLeft", cover.transform, new Color(0.93f, 0.88f, 0.73f), new Vector2(-258f, 0f), new Vector2(500f, 580f));
        MakeImage("JournalPageRight", cover.transform, new Color(0.95f, 0.90f, 0.75f), new Vector2(258f, 0f), new Vector2(500f, 580f));
        MakeImage("JournalGutter", cover.transform, new Color(0.62f, 0.55f, 0.42f), Vector2.zero, new Vector2(16f, 580f));

        var title = MakeText("JournalTitle", cover.transform, font, 26, TextAnchor.UpperCenter,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-258f, -36f), new Vector2(460f, 40f));
        title.text = "BLACKSMITH JOURNAL";
        title.fontStyle = FontStyle.Bold;
        Parchment(title);

        // A small sword drawn across the left page, tinted to the metal of the blade being read about.
        ui.journalSword = new GameObject("JournalSword");
        ui.journalSword.transform.SetParent(cover.transform, false);
        var swordRect = ui.journalSword.AddComponent<RectTransform>();
        swordRect.anchoredPosition = new Vector2(-258f, JournalIconY);
        swordRect.sizeDelta = new Vector2(300f, 30f);
        ui.journalBlade = MakeImage("Blade", ui.journalSword.transform, Color.gray, new Vector2(40f, 0f), new Vector2(200f, 12f));
        MakeImage("Guard", ui.journalSword.transform, new Color(0.35f, 0.33f, 0.32f), new Vector2(-64f, 0f), new Vector2(8f, 34f));
        MakeImage("Grip", ui.journalSword.transform, new Color(0.34f, 0.19f, 0.10f), new Vector2(-92f, 0f), new Vector2(48f, 10f));
        MakeImage("Pommel", ui.journalSword.transform, new Color(0.78f, 0.62f, 0.20f), new Vector2(-120f, 0f), new Vector2(14f, 14f));
        ui.journalIcons[WeaponType.Sword] = ui.journalSword;
        ui.journalIconMetal[WeaponType.Sword] = new System.Collections.Generic.List<Image> { ui.journalBlade };
        MakeJournalIcons(ui, cover.transform);

        ui.journalLeft = MakeText("JournalLeft", cover.transform, font, 20, TextAnchor.UpperLeft,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-258f, -146f), new Vector2(440f, 430f));
        Parchment(ui.journalLeft);
        ui.journalLeft.lineSpacing = 1.1f;
        ui.journalRight = MakeText("JournalRight", cover.transform, font, 18, TextAnchor.UpperLeft,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(258f, -36f), new Vector2(440f, 520f));
        Parchment(ui.journalRight);
        ui.journalCounter = MakeText("JournalCounter", cover.transform, font, 18, TextAnchor.LowerCenter,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(258f, 22f), new Vector2(300f, 26f));
        Parchment(ui.journalCounter);

        Color wood = new Color(0.40f, 0.26f, 0.14f);
        ui.journalPrev = MakeButton("JournalPrev", ui.journalPanel.transform, font, "< Previous  (A)", 22,
            new Vector2(0.5f, 0f), new Vector2(-300f, 18f), new Vector2(230f, 50f), wood);
        ui.journalClose = MakeButton("JournalClose", ui.journalPanel.transform, font, "Close  (E)", 22,
            new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(200f, 50f), new Color(0.45f, 0.18f, 0.14f));
        ui.journalNext = MakeButton("JournalNext", ui.journalPanel.transform, font, "Next  (D) >", 22,
            new Vector2(0.5f, 0f), new Vector2(300f, 18f), new Vector2(230f, 50f), wood);

        ui.journalPanel.SetActive(false);
    }

    // Little flat drawings of the other weapons, in the same spot and style as the journal's sword.
    private static void MakeJournalIcons(ForgeUI ui, Transform cover)
    {
        Color steel = new Color(0.35f, 0.33f, 0.32f), wood = new Color(0.45f, 0.30f, 0.16f);
        Color leather = new Color(0.34f, 0.19f, 0.10f), gold = new Color(0.78f, 0.62f, 0.20f);

        Transform Icon(WeaponType type)
        {
            var go = new GameObject("Journal" + type);
            go.transform.SetParent(cover, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(-258f, JournalIconY);
            rect.sizeDelta = new Vector2(300f, 30f);
            ui.journalIcons[type] = go;
            ui.journalIconMetal[type] = new System.Collections.Generic.List<Image>();
            go.SetActive(false);
            return go.transform;
        }
        Image Metal(WeaponType type, Transform t, Vector2 pos, Vector2 size, float angle = 0f)
        {
            var img = MakeImage("Metal", t, Color.gray, pos, size);
            img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
            ui.journalIconMetal[type].Add(img);
            return img;
        }

        var dagger = Icon(WeaponType.Dagger);
        Metal(WeaponType.Dagger, dagger, new Vector2(10f, 0f), new Vector2(110f, 12f));
        Metal(WeaponType.Dagger, dagger, new Vector2(68f, 0f), new Vector2(10f, 10f), 45f); // the point
        MakeImage("Guard", dagger, steel, new Vector2(-48f, 0f), new Vector2(7f, 26f));
        MakeImage("Grip", dagger, leather, new Vector2(-68f, 0f), new Vector2(34f, 9f));
        MakeImage("Pommel", dagger, gold, new Vector2(-89f, 0f), new Vector2(11f, 11f));

        var sickle = Icon(WeaponType.Sickle);
        MakeImage("Handle", sickle, wood, new Vector2(-92f, -6f), new Vector2(76f, 11f));
        MakeImage("Ferrule", sickle, gold, new Vector2(-52f, -6f), new Vector2(8f, 15f));
        for (int i = 0; i < 8; i++) // the crescent: short strips round a circle, thinning toward the tip
        {
            float a = Mathf.Lerp(190f, 395f, (i + 0.5f) / 8f) * Mathf.Deg2Rad;
            Vector2 p = new Vector2(2f, 8f) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 36f;
            Metal(WeaponType.Sickle, sickle, p, new Vector2(20f, Mathf.Lerp(11f, 4f, i / 7f)), a * Mathf.Rad2Deg + 90f);
        }

        var spear = Icon(WeaponType.Spear);
        MakeImage("Shaft", spear, wood, new Vector2(-40f, 0f), new Vector2(210f, 7f));
        MakeImage("Butt", spear, steel, new Vector2(-148f, 0f), new Vector2(10f, 9f));
        Metal(WeaponType.Spear, spear, new Vector2(78f, 0f), new Vector2(26f, 10f));
        Metal(WeaponType.Spear, spear, new Vector2(100f, 0f), new Vector2(22f, 22f), 45f); // leaf head

        var hammer = Icon(WeaponType.Hammer);
        MakeImage("Handle", hammer, wood, new Vector2(-24f, 0f), new Vector2(170f, 9f));
        MakeImage("Wrap", hammer, leather, new Vector2(-88f, 0f), new Vector2(42f, 12f));
        Metal(WeaponType.Hammer, hammer, new Vector2(70f, 0f), new Vector2(26f, 56f));
        Metal(WeaponType.Hammer, hammer, new Vector2(70f, 30f), new Vector2(32f, 10f)); // striking face
    }

    // "Return to the main menu?" - a small parchment card with two buttons over a dimmed screen.
    private static void MakeConfirm(ForgeUI ui, Transform root, Font font)
    {
        ui.confirmPanel = new GameObject("ConfirmPanel");
        ui.confirmPanel.transform.SetParent(root, false);
        var dim = ui.confirmPanel.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.5f);
        dim.raycastTarget = false;
        dim.rectTransform.anchorMin = Vector2.zero;
        dim.rectTransform.anchorMax = Vector2.one;
        dim.rectTransform.offsetMin = dim.rectTransform.offsetMax = Vector2.zero;
        var card = MakeImage("ConfirmCard", ui.confirmPanel.transform, new Color(0.93f, 0.88f, 0.73f), new Vector2(0f, 20f), new Vector2(560f, 230f));
        ui.confirmText = MakeText("ConfirmText", card.transform, font, 28, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(500f, 110f));
        Parchment(ui.confirmText);
        ui.confirmYes = MakeButton("ConfirmYes", ui.confirmPanel.transform, font, "Main Menu", 24,
            new Vector2(0.5f, 0.5f), new Vector2(-120f, -70f), new Vector2(200f, 54f), new Color(0.30f, 0.45f, 0.24f));
        ui.confirmNo = MakeButton("ConfirmNo", ui.confirmPanel.transform, font, "Stay", 24,
            new Vector2(0.5f, 0.5f), new Vector2(120f, -70f), new Vector2(200f, 54f), new Color(0.40f, 0.26f, 0.14f));
        ui.confirmPanel.SetActive(false);
    }

    public void ShowConfirm(bool visible) => confirmPanel.SetActive(visible);

    // The Material Order Book: a ledger page over the storage view - one row per metal (only unlocked ones are shown),
    // each with its own "Order 1" button, then tomorrow's delivery and the purse at the bottom.
    public const int LedgerRowHeight = 64;
    private static void MakeLedger(ForgeUI ui, Transform root, Font font)
    {
        ui.ledgerPanel = new GameObject("LedgerPanel");
        ui.ledgerPanel.transform.SetParent(root, false);
        var dim = ui.ledgerPanel.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.45f);
        dim.raycastTarget = false;
        dim.rectTransform.anchorMin = Vector2.zero;
        dim.rectTransform.anchorMax = Vector2.one;
        dim.rectTransform.offsetMin = dim.rectTransform.offsetMax = Vector2.zero;

        var cover = MakeImage("LedgerCover", ui.ledgerPanel.transform, new Color(0.20f, 0.30f, 0.24f), new Vector2(0f, 10f), new Vector2(640f, 470f));
        var card = MakeImage("LedgerPage", cover.transform, new Color(0.94f, 0.89f, 0.74f), Vector2.zero, new Vector2(616f, 446f));
        ui.ledgerCover = cover.rectTransform;
        ui.ledgerCard = card.rectTransform;
        ui.ledgerTitle = MakeText("LedgerTitle", card.transform, font, 26, TextAnchor.UpperCenter,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(580f, 64f));
        ui.ledgerTitle.fontStyle = FontStyle.Bold;
        Parchment(ui.ledgerTitle);
        var rule = MakeImage("LedgerRule", card.transform, new Color(0.55f, 0.45f, 0.32f), Vector2.zero, new Vector2(560f, 2f)).rectTransform;
        rule.anchorMin = rule.anchorMax = rule.pivot = new Vector2(0.5f, 1f); // stays under the title whatever the page height
        rule.anchoredPosition = new Vector2(0f, -82f);

        int n = ForgeMaterials.All.Length;
        ui.ledgerRows = new Text[n];
        ui.ledgerOrder = new UIButton[n];
        for (int i = 0; i < n; i++)
        {
            float top = -92f - i * LedgerRowHeight;
            ui.ledgerRows[i] = MakeText("LedgerRow" + i, card.transform, font, 18, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(34f, top), new Vector2(400f, 56f));
            Parchment(ui.ledgerRows[i]);
            ui.ledgerRows[i].supportRichText = true;
            ui.ledgerOrder[i] = MakeButton("LedgerOrder" + i, card.transform, font, "", 19,
                new Vector2(0.5f, 1f), new Vector2(200f, top - 48f), new Vector2(160f, 44f), new Color(0.40f, 0.28f, 0.14f));
        }
        ui.ledgerFooter = MakeText("LedgerFooter", card.transform, font, 18, TextAnchor.UpperLeft,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(34f, -92f - n * LedgerRowHeight - 8f), new Vector2(548f, 60f));
        Parchment(ui.ledgerFooter);
        ui.ledgerFooter.supportRichText = true;
        ui.ledgerStatus = MakeText("LedgerStatus", card.transform, font, 17, TextAnchor.UpperCenter,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 64f), new Vector2(560f, 26f));
        Parchment(ui.ledgerStatus);
        ui.ledgerStatus.fontStyle = FontStyle.Italic;
        ui.ledgerClose = MakeButton("LedgerClose", card.transform, font, "Close  (E)", 21,
            new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(180f, 44f), new Color(0.30f, 0.45f, 0.24f));
        ui.ledgerPanel.SetActive(false);
    }

    public void ShowLedger(bool visible) => ledgerPanel.SetActive(visible);

    // Fits the page to what's on it: the rows in use, then the footer right under the last one, then the status
    // line and Close at the bottom - no empty gap while only one or two metals are known.
    public void LayoutLedger(int rows)
    {
        float footerTop = 92f + rows * LedgerRowHeight + 6f;
        ledgerFooter.rectTransform.anchoredPosition = new Vector2(34f, -footerTop);
        float footerHeight = Mathf.Max(44f, ledgerFooter.preferredHeight);
        float height = footerTop + footerHeight + 104f;
        ledgerCard.sizeDelta = new Vector2(ledgerCard.sizeDelta.x, height);
        ledgerCover.sizeDelta = new Vector2(ledgerCover.sizeDelta.x, height + 24f);
    }
    public bool LedgerOpen => ledgerPanel.activeSelf;

    private static void Parchment(Text t)
    {
        t.color = Ink;
        t.GetComponent<Outline>().enabled = false; // a dark outline reads badly on light parchment
    }

    internal static Image MakeImage(string name, Transform parent, Color color, Vector2 pos, Vector2 size)
    {
        var img = new GameObject(name).AddComponent<Image>();
        img.transform.SetParent(parent, false);
        img.color = color;
        img.raycastTarget = false;
        var r = img.rectTransform;
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = pos;
        r.sizeDelta = size;
        return img;
    }

    public void ShowJournal(bool visible) => journalPanel.SetActive(visible);
    public bool JournalOpen => journalPanel.activeSelf;

    // bladeColor null = no weapon on this page (the empty journal); otherwise the drawing of that kind of weapon,
    // its metal tinted to bladeColor.
    public void SetJournalPage(string left, string right, string counter, Color? bladeColor, bool canBrowse, WeaponType type = WeaponType.Sword)
    {
        journalLeft.text = left;
        journalRight.text = right;
        journalCounter.text = counter;
        foreach (var icon in journalIcons) icon.Value.SetActive(bladeColor.HasValue && icon.Key == type);
        if (bladeColor.HasValue && journalIconMetal.TryGetValue(type, out var metal))
            foreach (var img in metal) img.color = bladeColor.Value;
        journalPrev.Show(canBrowse);
        journalNext.Show(canBrowse);
    }

    public void SetFade(float alpha)
    {
        fade.color = new Color(0f, 0f, 0f, alpha);
    }

    public void ShowResult(bool visible) => resultPanel.SetActive(visible);
    public void ShowOrder(bool visible) => orderPanel.SetActive(visible);

    // Writes the order note and shrinks the parchment to fit it (the notes range from 3 short lines to a quote).
    public void SetOrderPaper(string text)
    {
        orderPaper.text = text;
        var rect = (RectTransform)orderPanel.transform;
        rect.sizeDelta = new Vector2(rect.sizeDelta.x, Mathf.Min(orderPaper.preferredHeight + 26f, 300f));
    }
    public void ShowDialogue(bool visible) => dialoguePanel.SetActive(visible);
    public bool DialogueOpen => dialoguePanel != null && dialoguePanel.activeInHierarchy;
    public void ShowStorage(bool visible) => storagePanel.SetActive(visible);
    public void ShowMarkEditor(bool visible) => markPanel.SetActive(visible);
    public bool MarkEditorOpen => markPanel.activeInHierarchy;

    // Bit index for a cell of the 3x3 maker's mark, laid out so a binary literal reads like the grid:
    // 0b111_010_010 = top row full, then the middle column (see Workshop.MarkName).
    public static int MarkBit(int row, int col) => (2 - row) * 3 + (2 - col);

    private static readonly Color MarkOn = new Color(0.15f, 0.10f, 0.05f);
    private static readonly Color MarkOff = new Color(0.55f, 0.47f, 0.36f);

    public void SetMarkCells(int mark)
    {
        for (int i = 0; i < 9; i++)
        {
            bool on = (mark & (1 << i)) != 0;
            markCells[i].normal = on ? MarkOn : MarkOff;
            markCells[i].hover = Color.Lerp(markCells[i].normal, Color.white, 0.3f);
            markCells[i].background.color = markCells[i].normal;
        }
    }

    public void ShowMarkStamp(int mark, string name)
    {
        markStampRoot.SetActive(mark != 0);
        for (int i = 0; i < 9; i++)
            markStamp[i].color = (mark & (1 << i)) != 0 ? new Color(0.95f, 0.78f, 0.35f) : new Color(1f, 1f, 1f, 0.06f);
        markStampLabel.text = $"the {name} mark";
    }

    // An invisible hover/tooltip hit-box, laid over one of the 3D rack pegs or material bins every frame by
    // PlaceSlot (the 3D objects carry the visuals - the transparent icon Image is only kept so
    // Contains()/tooltip callers don't need to change).
    private static StorageSlot MakeSlot(Transform parent)
    {
        var go = new GameObject("Slot");
        go.transform.SetParent(parent, false);
        var bg = go.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0f);
        bg.raycastTarget = false;
        var rect = bg.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(60f, 60f);

        var iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(go.transform, false);
        var icon = iconObj.AddComponent<Image>();
        icon.raycastTarget = false;
        icon.color = new Color(1f, 1f, 1f, 0f);
        var iconRect = icon.rectTransform;
        iconRect.anchorMin = new Vector2(0.15f, 0.15f);
        iconRect.anchorMax = new Vector2(0.85f, 0.85f);
        iconRect.offsetMin = Vector2.zero;
        iconRect.offsetMax = Vector2.zero;

        return new StorageSlot { gameObject = go, rect = rect, icon = icon, tooltip = null };
    }

    // Covers exactly `screenRect` (screen pixels - e.g. a 3D object's projected bounds) with the slot's hit-box.
    // Converting through the storage panel keeps it right at any resolution, aspect ratio or UI scale - the old
    // hand-calibrated 1280x720 pixel positions drifted off the rack everywhere else.
    public void PlaceSlot(StorageSlot slot, Rect screenRect)
    {
        var panel = (RectTransform)storagePanel.transform;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(panel, screenRect.min, null, out Vector2 min);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(panel, screenRect.max, null, out Vector2 max);
        slot.rect.anchoredPosition = (min + max) * 0.5f;
        slot.rect.sizeDelta = max - min;
    }

    private const float StorageInfoWidth = 236f;
    private const float StorageInfoMargin = 12f;

    // Writes the storage info card and fits the parchment to it, so it's only ever as big as its text.
    // The info card sits just under the HUD, however many lines the HUD has right now (it grows with the metals known).
    public void KeepStorageInfoUnderHud() => storageInfoRect.anchoredPosition = new Vector2(-16f, -Mathf.Max(112f, hud.preferredHeight + 30f));

    public void SetStorageInfo(string text)
    {
        if (storageInfo.text == text) return;
        storageInfo.text = text;
        float h = storageInfo.preferredHeight;
        storageInfo.rectTransform.sizeDelta = new Vector2(StorageInfoWidth - 2f * StorageInfoMargin, h);
        storageInfoRect.sizeDelta = new Vector2(StorageInfoWidth, h + 2f * StorageInfoMargin);
    }

    private static TimingBarView MakeBar(Transform parent)
    {
        var rootObj = new GameObject("TimingBar");
        rootObj.transform.SetParent(parent, false);
        var rootRect = rootObj.AddComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0.5f, 1f);
        rootRect.anchorMax = new Vector2(0.5f, 1f);
        rootRect.pivot = new Vector2(0.5f, 1f);
        rootRect.anchoredPosition = new Vector2(0f, -150f);
        rootRect.sizeDelta = new Vector2(760f, 40f);

        var back = new GameObject("Back").AddComponent<Image>();
        back.transform.SetParent(rootObj.transform, false);
        back.color = new Color(0f, 0f, 0f, 0.6f);
        back.raycastTarget = false;
        var backRect = back.rectTransform;
        backRect.anchorMin = Vector2.zero;
        backRect.anchorMax = Vector2.one;
        backRect.offsetMin = new Vector2(-4f, -4f);
        backRect.offsetMax = new Vector2(4f, 4f);

        var segments = new RectTransform[5];
        for (int i = 0; i < 5; i++)
        {
            var img = new GameObject("Zone" + i).AddComponent<Image>();
            img.transform.SetParent(rootObj.transform, false);
            img.raycastTarget = false;
            segments[i] = img.rectTransform;
        }

        var pointerImg = new GameObject("Pointer").AddComponent<Image>();
        pointerImg.transform.SetParent(rootObj.transform, false);
        pointerImg.color = Color.white;
        pointerImg.raycastTarget = false;

        var view = new TimingBarView(rootObj, segments, pointerImg.rectTransform);
        view.SetZones(TimingBar.StandardWidths);
        view.SetPointer(0f);
        return view;
    }

    // Two vertical bars anchored to the right edge of the screen for the furnace's heat-tracking minigame
    // (see HeatGame): FIRE (the bouncing fire marker + the player's RMB-controlled catcher rectangle) on
    // the left, HEAT (a simple bottom-up fill bar for heating progress) on the right.
    private static HeatBarView MakeHeatBars(Transform parent, Font font)
    {
        const float barWidth = 42f, barHeight = 320f, barGap = 30f;
        Color frameColor = new Color(0.20f, 0.14f, 0.09f, 0.88f); // wood/leather tone, matches the rest of the UI

        var root = new GameObject("HeatBars");
        root.transform.SetParent(parent, false);
        var rootRect = root.AddComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(1f, 0.5f);
        rootRect.anchorMax = new Vector2(1f, 0.5f);
        rootRect.pivot = new Vector2(1f, 0.5f);
        rootRect.anchoredPosition = new Vector2(-30f, 0f);
        rootRect.sizeDelta = new Vector2(barWidth * 2f + barGap, barHeight);

        // Fire/control bar (left of the pair).
        var fireBar = new GameObject("FireBar");
        fireBar.transform.SetParent(root.transform, false);
        var fireBarRect = fireBar.AddComponent<RectTransform>();
        fireBarRect.anchorMin = new Vector2(0f, 0.5f);
        fireBarRect.anchorMax = new Vector2(0f, 0.5f);
        fireBarRect.pivot = new Vector2(0f, 0.5f);
        fireBarRect.anchoredPosition = Vector2.zero;
        fireBarRect.sizeDelta = new Vector2(barWidth, barHeight);
        var fireBack = fireBar.AddComponent<Image>();
        fireBack.color = frameColor;
        fireBack.raycastTarget = false;

        var catcherImage = new GameObject("Catcher").AddComponent<Image>();
        catcherImage.transform.SetParent(fireBar.transform, false);
        catcherImage.raycastTarget = false;
        var catcherRect = catcherImage.rectTransform;
        catcherRect.anchorMin = new Vector2(0f, 0f);
        catcherRect.anchorMax = new Vector2(1f, 0f);
        catcherRect.pivot = new Vector2(0.5f, 0.5f);
        catcherRect.sizeDelta = new Vector2(-4f, 60f);

        var fireImg = new GameObject("Fire").AddComponent<Image>();
        fireImg.transform.SetParent(fireBar.transform, false);
        fireImg.color = new Color(1f, 0.55f, 0.15f); // matches ForgeGameManager.HotGlow
        fireImg.raycastTarget = false;
        var fireRect = fireImg.rectTransform;
        fireRect.anchorMin = new Vector2(0f, 0f);
        fireRect.anchorMax = new Vector2(1f, 0f);
        fireRect.pivot = new Vector2(0.5f, 0.5f);
        fireRect.sizeDelta = new Vector2(-16f, 14f);

        // Heating progress bar (right of the pair) - a plain bottom-up fill, same anchor-stretch technique
        // TimingBarView.SetZones already uses for its horizontal zones.
        var progBar = new GameObject("ProgressBar");
        progBar.transform.SetParent(root.transform, false);
        var progBarRect = progBar.AddComponent<RectTransform>();
        progBarRect.anchorMin = new Vector2(1f, 0.5f);
        progBarRect.anchorMax = new Vector2(1f, 0.5f);
        progBarRect.pivot = new Vector2(1f, 0.5f);
        progBarRect.anchoredPosition = Vector2.zero;
        progBarRect.sizeDelta = new Vector2(barWidth, barHeight);
        var progBack = progBar.AddComponent<Image>();
        progBack.color = frameColor;
        progBack.raycastTarget = false;

        var fillImg = new GameObject("Fill").AddComponent<Image>();
        fillImg.transform.SetParent(progBar.transform, false);
        fillImg.color = new Color(0.85f, 0.35f, 0.12f);
        fillImg.raycastTarget = false;
        var fillRect = fillImg.rectTransform;
        fillRect.anchorMin = new Vector2(0.08f, 0.03f);
        fillRect.anchorMax = new Vector2(0.92f, 0.03f); // anchorMax.y is moved by HeatBarView.SetProgress each frame
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        MakeText("FireBarLabel", fireBar.transform, font, 13, TextAnchor.UpperCenter,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 18f), new Vector2(100f, 18f)).text = "FIRE";
        MakeText("ProgressBarLabel", progBar.transform, font, 13, TextAnchor.UpperCenter,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 18f), new Vector2(100f, 18f)).text = "HEAT";

        root.SetActive(false);
        return new HeatBarView(root, fireRect, catcherRect, catcherImage, fillRect, barHeight);
    }

    internal static Text MakeText(string name, Transform parent, Font font, int size, TextAnchor alignment,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = anchorMin; // anchor to the matching corner/edge so positions read as offsets from it
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = sizeDelta;

        var text = obj.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.text = "";

        var outline = obj.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        return text;
    }

    internal static UIButton MakeButton(string name, Transform parent, Font font, string label, int fontSize,
        Vector2 anchor, Vector2 anchoredPos, Vector2 size, Color color)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(anchor.x == 0.5f ? 0.5f : anchor.x, 0f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;

        var image = obj.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;

        var textObj = new GameObject("Label");
        textObj.transform.SetParent(obj.transform, false);
        var textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        var text = textObj.AddComponent<Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        text.text = label;

        return new UIButton
        {
            gameObject = obj,
            rect = rect,
            background = image,
            label = text,
            normal = color,
            hover = Color.Lerp(color, Color.white, 0.3f)
        };
    }
}
