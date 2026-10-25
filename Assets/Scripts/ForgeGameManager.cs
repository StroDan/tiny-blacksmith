using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Runs the whole game loop:
//   Main Room (talk to a customer, accept or decline their order) ->
//   Ready (pick metal) -> Furnace -> Anvil -> Water Barrel -> Grindstone -> Workbench -> Result screen -> back to Main Room
// The camera zooms to every workplace; between workplaces it returns to its starting pose first.
//
// Controls:
//   Main Room:  A/D snap-turn one step around the seat and glide there automatically (forge on the left,
//               meadow + work table ahead, storage on the right, the house behind); a waiting customer isn't
//               forced on you - face them and press F to hear their order out (click Accept/Decline). Accepted
//               orders go in the order book (Workshop.orders) and the customer LEAVES; clicking a weapon in storage
//               sets it aside for an order (reserved); the customer comes back on their day and F hands over
//               exactly the weapon(s) reserved for their order; E uses what you're looking at (the journal on the
//               table, or the house door to end the day); Enter heads to the forge
//   Ready:      (R repairs a customer's sword waiting on the rack, straight to the grindstone)
//   Ready:      1/2/3 pick Copper/Iron/Steel, Enter to start
//   Furnace:    hold Space, release at the perfect moment
//   Anvil:      left-click the red (Durability) or yellow (Balance) point before it shrinks away
//   Barrel:     hold Space, release at the perfect moment
//   Grindstone: Space starts the wheel spinning (once); once spinning, hold left mouse to press the blade
//               to it, release to lock in; Enter / Finish button to stop
//   Workbench:  drag Guard/Grip/Pommel onto the ghost outline (mouse wheel or Q/E rotates the grabbed part), release to lock
//   Result:     click Discard / Add to Inventory
//
// Uses the new Input System only (Keyboard.current / Mouse.current) and a manual Physics.Raycast
// for clicks - this project's Active Input Handling is "Input System Package (New)", so the old
// Input class throws and OnMouseDown isn't reliably delivered.
//
// Life between swords (see Workshop, CustomerOrder, SwordStories, MeadowLife):
//   - Metal costs gold (paid at Ready); customers pay on delivery, scaled by metal, reputation and tips.
//   - A day lasts until the smith goes home: E at the house door ends it (EndDay). Seasons change every
//     4 days and each day rolls its own weather. Other systems can react to a new day via DayEnded.
//   - Customers describe their job, not numbers - after the apprentice days the order paper only carries
//     their own words unless you "Ask for details" (costs the tip and some pay). They come back to ask
//     until their patience (plus a little grace) runs out, then give up on the order.
//   - Every sword hung on the rack gets a permanent ID and a record in BlacksmithHistory (the journal on
//     the table). Selling one moves it off the rack to its new owner - the rack is derived from those
//     records, never edited directly. Sold swords come back as stories a couple of days later, past
//     customers stroll across the meadow wearing them, and recurring customers come back with the same
//     sword worn down for a repair (Ready -> R -> grindstone) or order a new one shaped by the last.
//   - Failed or unwanted blades go into the scrap crate (Result screen, or click one on the rack); the
//     Collector of Cursed Blades drops by now and then to buy the lot - cracked ones for extra.
//   - The first finished sword asks you to design a 3x3 maker's mark, stamped on everything after.
//   - Everything above is saved to disk (SaveSystem) at every change that matters, and loaded on start.
//
// Main Room simplification: the design doc describes free walking once an order is accepted ("they can
// move wherever they want"). There's no first-person movement/pathing system in this prototype, so instead
// the player just presses Enter when ready and the camera glides to the forge the same way it already does
// between every other stage - see RunMainRoom.
public enum ForgeStage { MainRoom, Ready, Furnace, Anvil, Barrel, Grindstone, Workbench, Result }

public class ForgeGameManager : MonoBehaviour
{
    public ForgeStation forgeStation;
    public AnvilStation anvilStation;
    public QuenchBarrel quenchBarrel;
    public Grindstone grindstone;
    public WorkbenchStation workbenchStation;
    public StorageDisplay storageDisplay;

    public Transform workpieceTransform;
    public Renderer workpieceRenderer;

    public ForgeStage CurrentStage { get; private set; } = ForgeStage.MainRoom;
    // THE clock (GameTime): day, time, work durations, customer hours. DayCycle only shows it (sky, lights).
    public GameTime GameClock => gameTime;
    private GameTime gameTime;
    private static TimeSettings TimeCfg => TimeSettings.Current;
    public DayCycle Clock => dayCycle;
    private DayCycle dayCycle;
    // Customers still bring new work: during customer hours, while today's arrivals aren't all used up
    // (GameTime.Arrivals - 3 or 4 a day, spread out) and the order book isn't full (Workshop.MaxActiveOrders).
    public bool OrdersOpen => workshop.orders.Count < Workshop.MaxActiveOrders && (gameTime == null || (gameTime.IsCustomerHours && gameTime.NewCustomersLeftToday > 0));
    private bool eveningAnnounced, nightAnnounced;
    // The rack: only swords physically at the smithy (see BlacksmithHistory - derived from the sword records).
    public IReadOnlyList<WorkpieceData> inventory => workshop.history.Storage;
    public readonly Workshop workshop = new Workshop();
    private MeadowLife meadow;
    private CityLife city;
    public CityLife City => city;

    // Raised once a new day has begun (argument = the new day number), after the day's own bookkeeping
    // (sword wear, weather, season) is done - hook future day-based systems here.
    public event System.Action<int> DayEnded;
    public bool IsEndingDay { get; private set; }

    // ---- main room: seated camera turn (A/D), customer, current order --------------------------------
    private const float ForgeYaw = 0f;     // facing +Z: the actual forge/anvil/workbench cluster, on the player's left
    private const float CenterYaw = 90f;   // facing +X: the work table and the meadow beyond, where customers walk in from
    private const float StorageYaw = 180f; // facing -Z: the storage rack, on the player's right
    private const float HouseYaw = 270f;   // facing -X: the smith's house, behind the seat

    private float mainRoomYaw = CenterYaw;
    // Edge-tracked by hand (see LookInput). E starts "held" like Esc: a key already down when the scene starts (the
    // menu, a reload) must be let go before it counts.
    private bool lookAHeld, lookDHeld, useHeld = true;
    private float defaultFov;
    private Transform customer;
    private GameObject customerSword;       // the sword on the visiting customer's belt, when they own one of yours
    private Material customerSwordBlade;
    private Vector3 customerSpawn;
    private Vector3 customerStand;
    private CustomerOrder offeredOrder; // a customer is standing there with an order, not yet accepted or declined
    // Accepted orders are in workshop.orders - their customers are NOT here: they go about their lives and come back on
    // order.returnDay. visitOrder is the order whose customer is at the table right now (to collect, or to ask).
    private CustomerOrder visitOrder;
    private float visitSince;          // real time they walked up (they don't wait forever if nobody sees to them)
    private const float VisitPatienceSeconds = 45f;
    private bool AtTable => offeredOrder != null || visitOrder != null;
    private SwordRecord repairTarget; // set at Ready when the smith picks a customer's sword to repair instead of forging
    private int announcedDay = 1;     // last day the "Day N" greeting was shown for
    private int journalIndex = -1;    // which journal page was open last (-1 = start at the newest)

    private static readonly Color HotGlow = new Color(1f, 0.55f, 0.15f);

    private WorkpieceData item = new WorkpieceData();
    private ForgeMaterialInfo material = ForgeMaterials.All[Workshop.StartingMetal]; // the one metal a new smith knows (RunReady re-checks unlocks + stock)
    private WeaponType weaponType = WeaponType.Sword; // chosen at Ready (defaults to what the waiting customer asked for)
    public WeaponType SelectedWeapon => weaponType;
    private WeaponInfo Weapon => item.Weapon;
    private ForgeUI ui;
    private Camera cam;
    private Pose homePose;

    private MeshFilter workpieceFilter;
    private int shownPhase = -1; // which of the 5 forging-phase meshes the workpiece currently shows
    private WeaponType shownType;  // ...and of which weapon
    private float punch;
    private float displayScale = 1f;
    private float fadeAlpha;

    private readonly List<Renderer> stationRenderers = new List<Renderer>();
    private HitZoneMarker durabilityMarker;
    private HitZoneMarker balanceMarker;
    private Transform hammer;

    // The grindstone's "Wheel" child (stone/hub/axle/crank, see LowPoly.GrindstoneWheel) spins around its
    // own local Z once the player starts it in RunGrindstone - independent of the static stand.
    private Transform grindWheel;
    private bool grindstoneSpinning;
    private float grindWheelSpeed;
    private const float GrindWheelSpinSpeed = 540f; // degrees/second once fully spun up

    // One real 3D sword per storage rack peg (see LowPoly.DisplaySword), shown/hidden and tinted by
    // UpdateStorageUI to mirror the same "most recently forged first" slots the old icon grid used.
    private GameObject[] swordDisplays;
    private Material[] swordDisplayBladeMats;

    private readonly List<HandlePart> handleParts = new List<HandlePart>();
    private bool handleFollow;
    private bool handleBroken; // a part was dropped away from the blade: the sword is ruined
    private float handleCenterZ;
    private GameAudio sound;
    private ForgeEffects fx;

    public IReadOnlyList<HandlePart> HandleParts => handleParts;

    private void Start()
    {
        cam = Camera.main;
        if (forgeStation == null) forgeStation = FindAnyObjectByType<ForgeStation>();
        if (anvilStation == null) anvilStation = FindAnyObjectByType<AnvilStation>();
        if (quenchBarrel == null) quenchBarrel = FindAnyObjectByType<QuenchBarrel>();
        if (grindstone == null) grindstone = FindAnyObjectByType<Grindstone>();
        if (workbenchStation == null) workbenchStation = FindAnyObjectByType<WorkbenchStation>();
        if (workpieceTransform == null) workpieceTransform = GameObject.Find("Workpiece")?.transform;
        if (workpieceRenderer == null && workpieceTransform != null)
            workpieceRenderer = workpieceTransform.GetComponent<Renderer>();

        workpieceFilter = workpieceTransform != null ? workpieceTransform.GetComponent<MeshFilter>() : null;

        if (cam == null || forgeStation == null || anvilStation == null || quenchBarrel == null ||
            grindstone == null || workbenchStation == null || workpieceTransform == null || workpieceRenderer == null ||
            workpieceFilter == null || storageDisplay == null)
        {
            Debug.LogError("ForgeGameManager: missing scene objects - rebuild the scene via Tiny Blacksmith > Build Prototype 1 Scene.");
            enabled = false;
            return;
        }

        // Markers from an older build of the scene aren't used any more.
        GameObject.Find("HammerMarker")?.SetActive(false);
        GameObject.Find("GrindstoneMarker")?.SetActive(false);

        // The workpiece meshes are authored at real size, and the piece must not block the mouse rays.
        workpieceTransform.localScale = Vector3.one;
        var workpieceCollider = workpieceTransform.GetComponent<Collider>();
        if (workpieceCollider != null) Destroy(workpieceCollider);
        homePose = new Pose(cam.transform.position, cam.transform.rotation);
        defaultFov = cam.fieldOfView;

        // The game opens looking at the meadow, not straight at the forge (homePose.rotation, captured above,
        // is the forge-facing pose every other stage's camera glides back to).
        cam.transform.rotation = Quaternion.Euler(homePose.rotation.eulerAngles.x, CenterYaw, 0f);

        // Customers come down the side lane by the workshop and step round to the table: this spot is just outside
        // the seat's view when it faces the table (about 54 degrees off-axis, the view is 45 each side), so nobody ever
        // pops into or out of existence in front of the player.
        customerSpawn = new Vector3(5.5f, 0f, -11f);
        customerStand = new Vector3(2.6f, 0f, -3.5f);

        foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (r.transform == workpieceTransform || r.transform.IsChildOf(workpieceTransform)) continue;
            if (!r.gameObject.activeInHierarchy) continue;
            stationRenderers.Add(r);
        }

        durabilityMarker = CreateMarker("DurabilityPoint", PointKind.Durability, new Color(0.92f, 0.15f, 0.15f));
        balanceMarker = CreateMarker("BalancePoint", PointKind.Balance, new Color(0.98f, 0.85f, 0.10f));
        hammer = CreateHammer();
        grindWheel = grindstone.transform.Find("Wheel");
        customer = CreateCustomer();
        CreateSwordDisplays();
        CreateSupplyObjects();
        sound = gameObject.AddComponent<GameAudio>();
        MeasureFurnace();
        MeasureGrindWheel();
        fx = ForgeEffects.Create(workpieceRenderer.sharedMaterial.shader, forgeStation.transform, forgePitLocal, forgeChimneyLocal,
            BarrelPos + Vector3.up * 0.4f, GrindWheelCenter + Vector3.up * grindstone.wheelRadius, grindstone.transform.forward);

        // Main menu: New Game starts from nothing (and replaces the old save as soon as it's set up, below);
        // Load Game - or pressing Play on this scene directly in the Editor - picks up the save, if there is one.
        bool newGame = GameSession.Mode == StartMode.NewGame;
        if (!newGame && SaveSystem.TryLoad(workshop, out _))
        {
            ReconcileRepairs();
            announcedDay = workshop.day;
            Debug.Log($"ForgeGameManager: loaded day {workshop.day}, {workshop.history.swords.Count} sword record(s), {inventory.Count} on the rack.");
        }

        // The living world: this save's seed and windows (a new game gets its own), today's city event and weather -
        // the same on every reload of the day. An offer left standing at the table is still there.
        WorldDirector.EnsureWorld(workshop);
        WorldDirector.PlanDay(workshop);
        if (workshop.world.hasStandingOffer && workshop.world.standingOffer != null) { offeredOrder = workshop.world.standingOffer; offerToRestore = true; }
        meadow = MeadowLife.Create(workpieceRenderer.sharedMaterial.shader);
        meadow.ApplyDay(workshop.day, workshop.Season, WorldDirector.Weather(workshop));
        // Time of day: the clock (saved with the game), and the forge's warm light right over the coals.
        Vector3 fire = forgeStation.transform.TransformPoint(new Vector3(forgePitLocal.center.x, forgePitLocal.max.y + 0.45f, forgePitLocal.center.z));
        gameTime = GameTime.Create(workshop); // resumes the saved day and time exactly
        dayCycle = DayCycle.Create(gameTime, meadow, fire, workpieceRenderer.sharedMaterial.shader);
        eveningAnnounced = gameTime.Hour >= TimeCfg.eveningStart;
        nightAnnounced = gameTime.Hour >= TimeCfg.nightStart;
        // The townsfolk out in the city (past customers wearing your weapons among them) - they carry on whatever the smith is doing.
        city = CityLife.Create(workpieceRenderer.sharedMaterial.shader, dayCycle, meadow);
        city.pickPasserby = PickPasserby;
        city.pickResident = PickResident;
        CityAmbience.Create(dayCycle, meadow, homePose.position); // chimney smoke over the nearest roofs
        decor = CityEventDecor.Create(workpieceRenderer.sharedMaterial.shader, homePose.position);
        ApplyCityEvent();

        foreach (var i in FindObjectsByType<Interactable>(FindObjectsSortMode.None))
            if (i.kind == InteractionKind.EndDay) houseDoor = i;
        ui = ForgeUI.Create();
        if (newGame) SaveGame(); // "Existing save data will be overwritten" - the fresh game is the save from now on
        GameSession.Mode = StartMode.Continue; // a scene reload without the menu (tests, Editor) keeps the save
        RefreshOrderPaper();
        ShowOrderPaper();
        StartCoroutine(MainLoop());
    }

    private void OnApplicationQuit() => SaveGame();

    private void SaveGame()
    {
        string problem = workshop.history.Validate();
        if (problem != null) Debug.LogError("Sword history is inconsistent: " + problem); // should never happen - ownership only changes in BlacksmithHistory
        string orderProblem = workshop.ValidateOrders();
        if (orderProblem != null) Debug.LogError("Order book is inconsistent: " + orderProblem); // should never happen - reservations only change in Workshop
        // The customer standing at the table with an unanswered offer is part of the save: quitting and reloading brings
        // back the same person with the same job, never a new roll.
        workshop.world.hasStandingOffer = offeredOrder != null;
        workshop.world.standingOffer = offeredOrder;
        SaveSystem.Save(workshop);
    }

    // A customer's sword can only be in for repair while its owner is waiting on the repair job. If the save
    // ever lost track of that job (older save, edited file), the sword goes home with its owner rather than
    // being stuck on the rack forever - it's never the smith's to sell.
    private void ReconcileRepairs()
    {
        foreach (var r in workshop.history.swords.ToArray())
        {
            if (r.status != SwordStatus.NeedsRepair) continue;
            bool jobOpen = workshop.orders.Exists(o => o.IsRepair && o.repairSwordId == r.id);
            if (!jobOpen) workshop.history.ReturnToOwner(r, workshop.day, repaired: false);
        }
    }

    private void Update()
    {
        if (ui == null) return;

        punch = Mathf.MoveTowards(punch, 0f, Time.deltaTime * 6f);
        CustomerLife();

        workpieceTransform.localScale = new Vector3(1f, 1f - 0.15f * punch, 1f) * displayScale;

        if (handleFollow)
        {
            // The handle rides on the blade (position, spin and display scale) once it has been assembled.
            foreach (var p in handleParts)
            {
                p.go.transform.SetPositionAndRotation(
                    workpieceTransform.position + workpieceTransform.rotation * (p.localPos * displayScale),
                    workpieceTransform.rotation * p.localRot);
                p.go.transform.localScale = p.baseScale * displayScale;
            }
        }

        if (grindWheel != null)
        {
            float targetSpeed = grindstoneSpinning ? GrindWheelSpinSpeed : 0f;
            grindWheelSpeed = Mathf.MoveTowards(grindWheelSpeed, targetSpeed, Time.deltaTime * 900f);
            if (grindWheelSpeed > 0f) grindWheel.Rotate(grindWheelSpeed * Time.deltaTime, 0f, 0f, Space.Self);
        }

        UpdateSupplyObjects();
        Interactable focus = mainRoomPolling && CurrentStage == ForgeStage.MainRoom ? Interactable.Focused(cam, PointerPos) : null;
        ui.prompt.text = focus != null ? $"[E] {focus.prompt}" : "";

        // The metal's ceiling (ForgeMaterialInfo.maxQuality) holds through every stage: no station can push a stat past it.
        if (CurrentStage != ForgeStage.MainRoom && CurrentStage != ForgeStage.Ready) item.CapToMaterial();
        ui.stats.text = GetStats();
        ui.hud.text = CurrentStage == ForgeStage.Result
            ? ""
            : $"Day {workshop.day} - {workshop.Season}\n{gameTime.ClockText}  {gameTime.Phase}{WorkNote}\n{(meadow.Weather == "clear" ? "clear skies" : meadow.Weather)}{EventHud}\n" +
              $"{workshop.gold} gold\n{workshop.StarsText}\n{OreText}";
        // Passive time only while the smith is simply idle in the workshop - never in the journal, a dialogue, the
        // ledger, the storage menus or while the day ends. Work moves the day on (GameTime.Spend).
        gameTime.PassiveAllowed = mainRoomPolling && CurrentStage == ForgeStage.MainRoom && !IsEndingDay && !ui.DialogueOpen && !ui.JournalOpen;
        if (houseDoor != null) houseDoor.prompt = gameTime.CanEndDayFreely ? "End Day" : "Go to bed early";
        UpdateAmbient();
        sound.SetAmbience(dayCycle.Hour, meadow.Weather == "rain"); // river, townsfolk, wind - the city around the forge

        var mouse = Mouse.current;
        if (mouse != null)
        {
            Vector2 pos = mouse.position.ReadValue();
            ui.finishButton.SetHover(ui.finishButton.Contains(pos));
            ui.discardButton.SetHover(ui.discardButton.Contains(pos));
            ui.addButton.SetHover(ui.addButton.Contains(pos));
            ui.acceptButton.SetHover(ui.acceptButton.Contains(pos));
            ui.declineButton.SetHover(ui.declineButton.Contains(pos));
            ui.askButton.SetHover(ui.askButton.Contains(pos));
            ui.continueButton.SetHover(ui.continueButton.Contains(pos));
            ui.markDoneButton.SetHover(ui.markDoneButton.Contains(pos));
            foreach (var cell in ui.markCells) cell.SetHover(cell.Contains(pos));
            ui.journalPrev.SetHover(ui.journalPrev.Contains(pos));
            ui.journalNext.SetHover(ui.journalNext.Contains(pos));
            ui.journalClose.SetHover(ui.journalClose.Contains(pos));
            ui.confirmYes.SetHover(ui.confirmYes.Contains(pos));
            ui.confirmNo.SetHover(ui.confirmNo.Contains(pos));
            foreach (var b in ui.ledgerOrder) b.SetHover(b.Contains(pos));
            ui.ledgerClose.SetHover(ui.ledgerClose.Contains(pos));
        }
    }

    // Under the clock for a few seconds after a piece of work: where the time went ("+45m anvil work").
    private string WorkNote =>
        gameTime.LastWork != null && Time.unscaledTime - gameTime.LastWorkRealTime < 6f
            ? $"\n+{GameTime.Duration(gameTime.LastWorkMinutes)} {gameTime.LastWork}" : "";

    // The house door (its prompt says whether ending the day now is the usual thing, or an early night).
    private Interactable houseDoor;

    // Work done: the clock moves on - the one way a day is used up (see GameTime). Saved at once.
    private void Work(float minutes, string what)
    {
        if (minutes <= 0f) return;
        gameTime.Spend(minutes, what);
        SaveGame();
    }

    private float StageTime(WorkStage stage) => GameTime.StageMinutes(workshop, item.weaponType, stage);

    // A little line at the bottom while a past customer is crossing the meadow wearing one of your swords.
    private void UpdateAmbient()
    {
        SwordRecord passing = city != null ? city.PasserbyWithYourSword : null;
        bool lookingOut = CurrentStage == ForgeStage.MainRoom;
        if (!lookingOut || ui.dialogueText.gameObject.activeInHierarchy || ui.JournalOpen)
        {
            ui.ambient.text = "";
            return;
        }
        if (passing == null)
        {
            // Now and then a glimpse of what's going on in the city today (a few seconds out of every half minute).
            var ev = WorldDirector.Today(workshop);
            float t = Time.unscaledTime % 34f;
            ui.ambient.text = ev.ambient != null && ev.ambient.Length > 0 && t > 26f ? ev.ambient[(int)(Time.unscaledTime / 34f) % ev.ambient.Length] : "";
            return;
        }
        string sword = passing.legendName != null ? $"'{passing.legendName}' ({passing.Label})" : $"{passing.Label}";
        ui.ambient.text = $"Look - {passing.order.Title} is walking past, wearing {sword} - the {passing.sword.Weapon.noun} you forged on day {passing.createdDay}!";
    }

    // ---- the living world: today's city event on the HUD, in the streets and in the view ----------------------------

    private CityEventDecor decor;
    private bool offerToRestore; // a loaded game had a customer standing at the table with an offer: they walk back up

    private string EventHud
    {
        get
        {
            var world = workshop.world;
            var today = world.EventOn(workshop.day);
            var coming = world.Upcoming(workshop.day);
            string s = today != null ? $"\n{today.Info.name}" : "";
            if (coming != null && coming.announcedDay > 0)
                s += $"\n{coming.Info.name} {(coming.startDay - workshop.day == 1 ? "tomorrow" : $"in {coming.startDay - workshop.day} days")}";
            return s;
        }
    }

    // Everything that shows the day is different: how busy the streets are and who's in them, and the decorations.
    private void ApplyCityEvent()
    {
        var ev = WorldDirector.Today(workshop);
        city?.SetMood(ev.crowd, ev.guards, ev.merchants, ev.nobles, ev.knights);
        decor?.Show(ev);
    }

    // Development tools only (Tiny Blacksmith > World menu): re-show today's event and weather after forcing one.
    public void DebugRefreshWorld()
    {
        meadow.ApplyDay(workshop.day, workshop.Season, WorldDirector.Weather(workshop));
        ApplyCityEvent();
        SaveGame();
    }

    // A customer who was standing at the table with an unanswered offer when the game was saved comes back to it.
    private IEnumerator RestoreOffer()
    {
        offerToRestore = false;
        if (offeredOrder == null || customer.gameObject.activeSelf) yield break;
        var who = workshop.history.Customer(offeredOrder.customerId);
        if (who != null) { DressAs(who); ShowCustomerSword(workshop.history.CarriedBy(who)); }
        else { DressCustomer(NpcAppearance.ForOrder(offeredOrder, 7919 * offeredOrder.customerId + 17)); ShowCustomerSword(null); }
        yield return WalkCustomer(customerSpawn, customerStand, 2.2f);
        customer.rotation = FacingSeat;
    }

    // Who strolls across the meadow next: often someone who owns one of your swords right now (never the
    // same person who's standing at the table), otherwise a stranger.
    private SwordRecord PickPasserby()
    {
        int atTable = customer != null && customer.gameObject.activeSelf ? CustomerIdAtTable() : 0;
        var candidates = workshop.history.swords.FindAll(r => r.IsOut && r.order != null && (atTable == 0 || r.ownerId != atTable));
        if (candidates.Count == 0 || Random.value < 0.4f) return null;
        return candidates[Random.Range(0, candidates.Count)];
    }

    // A customer whose order is in the book, out living their life while the smith works on it (not the one at the
    // table, nor anyone due at the table today).
    private CustomerRecord PickResident()
    {
        var candidates = workshop.orders.FindAll(o => o.IsActive && o.customerId != CustomerIdAtTable() && o.returnDay > workshop.day);
        if (candidates.Count == 0) return null;
        return workshop.history.Customer(candidates[Random.Range(0, candidates.Count)].customerId);
    }

    private int CustomerIdAtTable() => visitOrder != null ? visitOrder.customerId : offeredOrder != null ? offeredOrder.customerId : visitingCustomerId;
    private int visitingCustomerId; // someone who only came to talk (story, repair request)

    private IEnumerator MainLoop()
    {
        while (true)
        {
            yield return RunMainRoom();
            yield return RunReady();
            if (readyCancelled) continue; // back to the table without starting anything
            if (repairTarget != null)
            {
                // A repair skips the forging: the owner's own sword goes straight onto the grindstone.
                yield return RunGrindstone();
                yield return FinishRepairWork();
                continue;
            }
            yield return RunFurnace();
            yield return RunAnvil();
            yield return RunBarrel();
            // A weapon without an edge (the hammer) has nothing to sharpen - it goes straight to the workbench.
            if (item.Weapon.hasEdge) yield return RunGrindstone();
            else yield return Say($"{item.Weapon.name}s need no edge - on to the workbench for a handle.", 1.6f);
            yield return RunWorkbench();
            yield return RunResult();
        }
    }

    // ---------------------------------------------------------------- main room

    // Seated at the table between the forge and the storage nook. A/D turns the view; customers walk in
    // from the meadow every so often with an order to accept/decline, or (once one is pending) to collect
    // if a matching sword is waiting in storage. Enter at any time heads to the forge.
    private IEnumerator RunMainRoom()
    {
        CurrentStage = ForgeStage.MainRoom;
        ui.bar.Show(false);
        ui.finishButton.Show(false);
        ui.message.text = "";
        workpieceRenderer.enabled = false; // no stale sword/ingot floating on the anvil while chatting
        ClearHandle(); // ...nor the last sword's hilt, left hanging where the result screen showed it

        // RunResult() ends by fading to black and leaves it there for whichever stage runs next to clear -
        // that's normally us now (Main Room sits between Result and Ready in the loop).
        if (fadeAlpha > 0f) yield return Fade(0f);

        // The forge stages move the camera freely; pick the seat's yaw up from wherever it actually points
        // now so the turn below starts from there instead of snapping to the last Main Room angle first.
        mainRoomYaw = Mathf.Repeat(cam.transform.eulerAngles.y, 360f);
        yield return TurnTo(CenterYaw);
        if (workshop.day != announcedDay) yield return AnnounceDay();
        if (offerToRestore) yield return RestoreOffer();

        // Visitors come at their time on the clock (GameTime), one at a time: someone already due walks up shortly after
        // the smith is back at the table, the next only a little while after the last one left.
        float idleTimer = 0f;
        float nextVisit = Random.Range(1.5f, 3f);

        while (true)
        {
            bool facingCustomer = Facing(CenterYaw, 20f);
            ui.instructions.text = visitOrder != null
                ? (facingCustomer
                    ? (workshop.IsReady(visitOrder)
                        ? $"{visitOrder.customerName} is back for {visitOrder.Label}.\nPress F to hand over {(visitOrder.quantity > 1 ? "the weapons" : "the weapon")} set aside for it."
                        : $"{visitOrder.customerName} is back to ask about {visitOrder.Label}.\nPress F to talk to them.")
                    : $"{visitOrder.customerName} is at the table about {visitOrder.Label} (look ahead, A/D, then F).")
                : offeredOrder != null
                    ? (facingCustomer
                        ? "A customer is waiting with an order.\nPress F to hear it out, or just carry on - ENTER for the forge, A/D to look elsewhere."
                        : "A customer is waiting by the table with an order (look ahead, A/D, then F).\nENTER to head to the forge.")
                    : !gameTime.CanStartRepair
                        ? "It's late - too late to start any more work tonight.\nEnd the day at your house (look behind you, E at the door)."
                        : !gameTime.CanStartWeapon
                            ? $"Too late to begin another weapon (after {GameTime.Clock(TimeCfg.newWeaponCutoff)}) - a repair still fits.\nOr end the day at your house (E at the door)."
                            : !gameTime.IsCustomerHours && gameTime.Hour >= TimeCfg.customerEnd
                                ? $"Customer hours are over. Finish your work, order materials for tomorrow,\nor end the day at your house (E at the door).  ENTER for the forge."
                                : gameTime.NewCustomersLeftToday == 0 || workshop.orders.Count >= Workshop.MaxActiveOrders
                                    ? "No more new customers expected today.\nENTER for the forge, or plan tomorrow (storage, order book, journal)."
                                    : "A / D to look around.\nPress ENTER to head to the forge whenever you like.  (Esc: main menu)";
            if (LookingAtStorage)
                ui.instructions.text = workshop.CrateTotal > 0
                    ? "STORAGE - your delivery has arrived: E (or click the crate) unloads it into the bins.\nPoint at the order book for Material Orders, at a weapon for its details."
                    : "STORAGE - E: Material Orders (paid now, delivered tomorrow morning).\nClick a weapon to set it aside for an order (or scrap it).  ENTER for the forge.";
            else if (Facing(HouseYaw, 20f))
                ui.instructions.text = gameTime.CanEndDayFreely
                    ? "Home. E at the door ends the day (tomorrow's delivery comes in the morning).\nA / D to look around, ENTER for the forge."
                    : $"Home. It's only {gameTime.ClockText} - the working day runs until {GameTime.Clock(TimeCfg.endDayAvailable)}.\nE at the door for an early night anyway.";

            // The day moving on: a word when evening comes, and when night falls.
            if (!eveningAnnounced && gameTime.Hour >= TimeCfg.eveningStart && !gameTime.IsNight)
            {
                eveningAnnounced = true;
                yield return Say("Evening. Customer hours are over - finish your work, order materials, or call it a day at the house.", 2.6f);
            }
            if (!nightAnnounced && gameTime.Hour >= TimeCfg.nightStart)
            {
                nightAnnounced = eveningAnnounced = true;
                yield return Say("Night falls over the city. End the day at your house when you're ready.", 2.6f);
            }

            if (LookInput(out float turnYaw)) yield return TurnTo(turnYaw);
            UpdateStorageUI();
            var clickedWeapon = StorageClick();
            if (clickedWeapon != null) { yield return WeaponActions(clickedWeapon); yield return null; continue; }

            // E: whatever interactable the player is looking at (journal on the table, the house door).
            Interactable focus = Interactable.Focused(cam, PointerPos); // the prompt itself is drawn in Update (see mainRoomPolling)
            bool clickedSupply = focus != null && LookingAtStorage && ClickedThisFrame() && (focus == ledgerUse || focus == crateUse) &&
                                 focus.ScreenRect(cam).Contains(PointerPos.Value);
            if ((UsePressed() || clickedSupply) && focus != null)
            {
                if (focus.kind == InteractionKind.OpenJournal) yield return OpenJournal();
                else if (focus.kind == InteractionKind.MaterialOrders) yield return OpenLedger();
                else if (focus.kind == InteractionKind.OpenDelivery) yield return OpenDelivery();
                else if (focus.kind == InteractionKind.EndDay) { yield return EndDay(); idleTimer = 0f; }
                yield return null;
                continue;
            }

            if (visitOrder != null)
            {
                // Back about an order they placed: F to see to them. Nobody waits at the table forever - left
                // alone, they go and come back another day (the order stays in the book).
                if (facingCustomer && GivePressed()) yield return CollectVisit();
                else if (Time.realtimeSinceStartup - visitSince > VisitPatienceSeconds) yield return VisitLeaves("I'll come back another day.");
            }
            else if (offeredOrder != null)
            {
                // Customer is standing there with an offer the player hasn't answered yet - they're free to
                // ignore it entirely (keep looking around, head to the forge) until they come back and press F.
                if (facingCustomer && GivePressed())
                {
                    yield return AnswerOrder();
                }
            }
            else
            {
                idleTimer += Time.deltaTime;
                if (idleTimer >= nextVisit && SomeoneToVisit(idleTimer))
                {
                    yield return RunCustomerVisit(idleTimer);
                    idleTimer = 0f;
                    nextVisit = Random.Range(TimeCfg.visitGapSeconds.x, TimeCfg.visitGapSeconds.y);
                }
            }

            // Esc: back to the main menu (after a confirmation) - the game is saved first, nothing is reset.
            if (MenuPressed())
            {
                yield return ReturnToMenuPrompt();
                yield return null;
                continue;
            }

            if (EnterPressed())
            {
                // Late in the day: a weapon can't be begun any more (a repair still can, for a while).
                if (!gameTime.CanStartRepair || (!gameTime.CanStartWeapon && SwordAwaitingRepair() == null))
                {
                    yield return Say(gameTime.CanStartRepair
                        ? $"It's too late to begin another weapon ({gameTime.ClockText}). Plan tomorrow, or end the day at the house."
                        : $"It's too late for any more work tonight ({gameTime.ClockText}). End the day at the house.", 2.4f);
                    continue;
                }
                ui.ShowStorage(false);
                workpieceRenderer.enabled = true;
                yield break;
            }
            mainRoomPolling = true;
            yield return null;
            mainRoomPolling = false;
        }
    }

    // True only while the Main Room loop is idling between frames - i.e. not inside a turn, a dialogue, the
    // journal or the end of the day. The E prompt is only shown then, so it can never linger over those.
    private bool mainRoomPolling;

    private bool UsePressed()
    {
        var kb = Keyboard.current;
        bool now = kb != null && kb.eKey.isPressed;
        bool pressed = now && !useHeld;
        useHeld = now;
        return pressed;
    }

    private bool escHeld = true; // edge-tracked like UsePressed; starts "held" so nothing carries over from the menu

    private bool MenuPressed()
    {
        var kb = Keyboard.current;
        bool now = kb != null && kb.escapeKey.isPressed;
        bool pressed = now && !escHeld;
        escHeld = now;
        return pressed;
    }

    // "Return to the main menu?" - saves first, so the menu's Load Game picks up exactly where this left off.
    private IEnumerator ReturnToMenuPrompt()
    {
        ui.ShowStorage(false);
        ui.confirmText.text = "Return to the main menu?\nYour progress is saved.";
        ui.ShowConfirm(true);
        bool? answer = null;
        yield return null;
        while (answer == null)
        {
            var kb = Keyboard.current;
            bool escNow = kb != null && kb.escapeKey.isPressed;
            if (escNow && !escHeld) answer = false;
            escHeld = escNow;
            if (ClickedThisFrame() && Mouse.current != null)
            {
                Vector2 pos = Mouse.current.position.ReadValue();
                if (ui.confirmYes.Contains(pos)) answer = true;
                else if (ui.confirmNo.Contains(pos)) answer = false;
            }
            yield return null;
        }
        ui.ShowConfirm(false);
        if (answer == true) yield return GoToMainMenu();
    }

    // Public so the menu test can drive it without simulated clicks.
    public IEnumerator GoToMainMenu()
    {
        SaveGame();
        yield return Fade(1f);
        UnityEngine.SceneManagement.SceneManager.LoadScene(GameSession.MenuScene);
    }

    private IEnumerator AnnounceDay()
    {
        announcedDay = workshop.day;
        bool newSeason = (workshop.day - 1) % 4 == 0;
        string weather = meadow.Weather == "clear" ? "clear skies" : meadow.Weather;
        yield return Say(newSeason ? $"Day {workshop.day}: {workshop.Season} has arrived!" : $"Day {workshop.day} - {weather} over the meadow", 1.6f);
        // What's different today: a city event, one being cried for the coming days, a new kind of work in demand.
        foreach (var line in WorldDirector.MorningLines(workshop)) yield return Say(line, 3.2f);
    }

    private bool Facing(float yaw, float tolerance) => Mathf.Abs(Mathf.DeltaAngle(mainRoomYaw, yaw)) < tolerance;
    private bool LookingAtStorage => Facing(StorageYaw, 60f);

    // ---------------------------------------------------------------- end of the day (the house door)

    // Fade out, move the calendar on (sword wear, weather, season), fade back in. Runs inline in the Main
    // Room loop, so nothing else - turning, other interactions, customer visits - can happen until it's
    // done, and IsEndingDay guards against it ever being started twice.
    private IEnumerator EndDay()
    {
        if (IsEndingDay) yield break;
        // Before the evening it's the smith's own choice to stop early - asked, never forced (and never the other way:
        // the day doesn't end by itself).
        if (!gameTime.CanEndDayFreely)
        {
            bool sure = false;
            yield return Choose($"It's only {gameTime.ClockText} - the working day usually runs until {GameTime.Clock(TimeCfg.endDayAvailable)}.\n" +
                                "Go to bed early and start tomorrow fresh?", new[] { "Go to bed", "Keep working" }, i => sure = i == 0);
            if (!sure) yield break;
        }
        IsEndingDay = true;
        ui.prompt.text = "";
        ui.instructions.text = "";
        ui.ShowStorage(false);
        sound.Whoosh();
        yield return Fade(1f, 0.8f);

        // Nobody stays at the table overnight: an offer nobody answered is gone, someone who came to collect comes back tomorrow.
        offeredOrder = null;
        if (visitOrder != null) { visitOrder.returnDay = workshop.day + 1; visitOrder.returnHour = GameTime.ReturnHourFor(visitOrder, workshop.day + 1); visitOrder = null; }
        customer.gameObject.SetActive(false);

        // The day's summary on the dark screen - a moment to take stock, then the night passes.
        string arriving = Workshop.IngotList(workshop.ordered);
        ui.message.text = DaySummary(arriving);
        yield return WaitForKeyOrSeconds(9f);

        // The smith sleeps through the night: wake at dawn, the sky brightens into the working morning on its own.
        AdvanceDay();
        ui.message.text = $"Day {workshop.day}";
        yield return new WaitForSeconds(1.2f);
        ui.message.text = "";
        yield return Fade(0f, 0.8f);
        IsEndingDay = false;
        useHeld = lookAHeld = lookDHeld = true; // nothing pressed during the fade carries over

        yield return AnnounceDay();
        if (creditDelivery) yield return Say("The supplier left two ingots on credit by your storage - \"pay me back in custom\".", 2.6f);
        else if (arriving != null) yield return Say($"Your delivery has arrived by the storage: {arriving}.", 2.2f);
    }

    // "DAY 4 COMPLETE" and what it brought - compact, a few lines on the dark screen.
    private string DaySummary(string arriving)
    {
        var t = workshop.today;
        int earned = workshop.gold - t.goldAtStart + t.materialSpend;
        float rep = workshop.reputation - t.repAtStart;
        var lines = new List<string> { $"DAY {workshop.day} COMPLETE", "" };
        var work = new List<string>();
        if (t.forged > 0) work.Add($"{t.forged} weapon{(t.forged == 1 ? "" : "s")} forged");
        if (t.repairs > 0) work.Add($"{t.repairs} repair{(t.repairs == 1 ? "" : "s")}");
        if (t.ordersCompleted > 0) work.Add($"{t.ordersCompleted} order{(t.ordersCompleted == 1 ? "" : "s")} completed");
        lines.Add(work.Count > 0 ? string.Join("  ·  ", work) : "A quiet day at the forge.");
        lines.Add($"Gold  +{earned}" + (t.materialSpend > 0 ? $"   -{t.materialSpend} materials" : ""));
        if (Mathf.Abs(rep) >= 0.5f) lines.Add($"Reputation  {(rep > 0 ? "+" : "")}{rep:0}");
        if (arriving != null) lines.Add($"Arriving tomorrow:  {arriving}");
        int open = workshop.orders.Count;
        if (open > 0)
        {
            var dueSoon = workshop.orders.FindAll(o => o.deadlineDay <= workshop.day + 1);
            lines.Add($"Active orders: {open}" + (dueSoon.Count > 0 ? $"  ({dueSoon.Count} due tomorrow)" : ""));
        }
        // A glimpse of tomorrow - something to look forward to.
        var teasers = WorldDirector.Teasers(workshop);
        if (teasers.Count > 0) { lines.Add(""); lines.AddRange(teasers); }
        // The title at the message's own size, the rest smaller - a few quiet lines, not a spreadsheet.
        return lines[0] + "\n<size=26>" + string.Join("\n", lines.GetRange(1, lines.Count - 1)) + "</size>";
    }

    private IEnumerator WaitForKeyOrSeconds(float seconds)
    {
        float end = Time.time + seconds;
        yield return new WaitForSeconds(1.5f); // at least a moment to read it
        while (Time.time < end)
        {
            var kb = Keyboard.current;
            if ((kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame)) || ClickedThisFrame()) break;
            yield return null;
        }
    }

    private bool creditDelivery; // this morning's crate holds the supplier's two ingots on credit (see Workshop.ReceiveDelivery)

    // Everything that happens when a new day begins. The single place the day number changes.
    // In order: the new day and its morning (GameTime), yesterday's material orders, the weapons out in the world
    // (returns, stories, wear), the weather and season, the order book, the townsfolk, and anything hooked on DayEnded.
    // The supplier's stock and today's customer arrivals follow from the new day number (Workshop.Supply,
    // GameTime.Arrivals); the Event Trader from workshop.nextEventDay.
    private void AdvanceDay()
    {
        gameTime.BeginNewDay();
        workshop.ordersToday = 0; // a fresh day, fresh customers
        creditDelivery = workshop.ReceiveDelivery(); // yesterday's orders are in the crate by the storage
        eveningAnnounced = nightAnnounced = false;
        workshop.history.ProcessNewDay(workshop.day);
        WorldDirector.BeginDay(workshop); // the city's events, the weapons out there living through them, notable works
        meadow.ApplyDay(workshop.day, workshop.Season, WorldDirector.Weather(workshop));
        ApplyCityEvent();
        RefreshOrderPaper();
        city?.ResetForNewDay(); // the screen is black: everyone's home for the night
        DayEnded?.Invoke(workshop.day);
        SaveGame();
    }

    // ---------------------------------------------------------------- the supplier: Material Order Book + delivery crate

    // The ledger on its stand in front of the rack's left post, and the crate the morning's delivery comes in (on the
    // floor right of the rack, by the storage crates) - placed so neither ever covers a peg or a bin.
    private GameObject ledgerObject, crateObject;
    private Interactable ledgerUse, crateUse;
    private static readonly Vector3 LedgerRackLocal = new Vector3(1.38f, 0f, 1.05f);
    private static readonly Vector3 CrateRackLocal = new Vector3(-2.2f, 0f, 0.2f); // beside the rack, in front of the storage crates

    private Vector2? PointerPos => Mouse.current != null ? Mouse.current.position.ReadValue() : (Vector2?)null;

    private void CreateSupplyObjects()
    {
        Shader shader = workpieceRenderer.sharedMaterial.shader;
        Transform rack = storageDisplay.transform;
        GameObject Make(string name, LowPolyModel model, Vector3 local, float yaw, InteractionKind kind, string prompt, out Interactable use)
        {
            var go = new GameObject(name);
            go.transform.SetParent(rack.parent != null ? rack.parent : rack, true);
            Vector3 at = rack.TransformPoint(local);
            at.y = rack.position.y;
            Vector3 away = at - homePose.position; away.y = 0f; // the front (-Z) faces the seat
            go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(away) * Quaternion.Euler(0f, yaw, 0f));
            model.Attach(go, shader);
            use = go.AddComponent<Interactable>();
            use.kind = kind;
            use.prompt = prompt;
            use.maxViewAngle = 60f; // they sit near the edge of the storage view - only live while facing it (UpdateSupplyObjects)
            return go;
        }
        ledgerObject = Make("MaterialOrderBook", LowPoly.SupplyLedger(), LedgerRackLocal, 0f, InteractionKind.MaterialOrders, "Material Orders", out ledgerUse);
        crateObject = Make("DeliveryCrate", LowPoly.DeliveryCrate(), CrateRackLocal, -9f, InteractionKind.OpenDelivery, "Open Delivery", out crateUse);
        UpdateSupplyObjects();
    }

    // The crate is there while it holds anything; both only answer E while the seat faces the storage. With nothing
    // pointed at, E goes to the crate while it can unload something, otherwise to the order book.
    private void UpdateSupplyObjects()
    {
        if (crateObject == null) return;
        bool delivery = workshop.CrateTotal > 0;
        if (crateObject.activeSelf != delivery && !crateLeaving) crateObject.SetActive(delivery);
        bool facing = CurrentStage == ForgeStage.MainRoom && Facing(StorageYaw, 30f);
        ledgerUse.enabled = facing;
        crateUse.enabled = facing && delivery && !crateLeaving;
        crateUse.priority = workshop.CrateCanUnload ? 1 : -1;
    }

    private string SupplierClosedText => $"Supplier orders are closed for today (until {GameTime.Clock(TimeCfg.supplierCutoff)}). Order again tomorrow.";

    private IEnumerator OpenLedger()
    {
        sound.Pickup();
        ui.ShowStorage(false);
        string status = gameTime.SupplierOpen ? "" : SupplierClosedText;
        RefreshLedger(status);
        ui.ShowLedger(true);
        yield return null; // don't let the E that opened it close it on the same frame

        bool closeHeld = true;
        var digitHeld = new bool[ForgeMaterials.All.Length];
        for (int i = 0; i < digitHeld.Length; i++) digitHeld[i] = true;
        while (true)
        {
            var kb = Keyboard.current;
            bool closeNow = kb != null && (kb.eKey.isPressed || kb.escapeKey.isPressed);
            bool close = closeNow && !closeHeld;
            closeHeld = closeNow;
            int buy = -1;
            for (int i = 0; i < digitHeld.Length; i++)
            {
                bool now = kb != null && i < 9 && kb[(Key)((int)Key.Digit1 + i)].isPressed;
                if (now && !digitHeld[i]) buy = i;
                digitHeld[i] = now;
            }
            if (ClickedThisFrame() && Mouse.current != null)
            {
                Vector2 pos = Mouse.current.position.ReadValue();
                for (int i = 0; i < ui.ledgerOrder.Length; i++) if (ui.ledgerOrder[i].Contains(pos)) buy = i;
                if (ui.ledgerClose.Contains(pos)) close = true;
            }
            if (close) break;
            if (buy >= 0 && buy < ForgeMaterials.All.Length && workshop.IsUnlocked(ForgeMaterials.All[buy]))
            {
                var m = ForgeMaterials.All[buy];
                string why = gameTime.SupplierOpen ? workshop.OrderIngot(buy) : SupplierClosedText;
                if (why == null)
                {
                    sound.Coin();
                    status = $"One {m.name.ToLower()} ingot ordered - {workshop.ordered[buy]} arriving tomorrow morning.";
                    SaveGame();
                }
                else
                {
                    sound.Clack(true);
                    status = why;
                }
                RefreshLedger(status);
            }
            yield return null;
        }

        ui.ShowLedger(false);
        // Keys used in the ledger must be let go before they count out here (the E that closed it mustn't re-open it).
        useHeld = lookAHeld = lookDHeld = escHeld = true;
        yield return null;
    }

    // One row per metal the smith knows (unlocks come in order, so they're always the first rows); locked metals
    // aren't listed at all.
    private void RefreshLedger(string status)
    {
        var supply = workshop.Supply;
        ui.ledgerTitle.text = $"MATERIAL ORDERS\n<size=16>Day {workshop.day}  -  paid now, delivered to your storage tomorrow morning</size>";
        for (int i = 0; i < ui.ledgerRows.Length; i++)
        {
            var m = ForgeMaterials.All[i];
            bool known = workshop.IsUnlocked(m);
            ui.ledgerRows[i].gameObject.SetActive(known);
            ui.ledgerOrder[i].Show(known);
            if (!known) continue;
            string ordered = workshop.ordered[i] > 0 ? $"   ·   ordered {workshop.ordered[i]}" : "";
            ui.ledgerRows[i].text = $"<b>{m.name}</b>   ×{supply[i]} available  -  {m.price} gold each\n" +
                                    $"<size=16>In your bin {workshop.stock[i]}/{Workshop.BinCapacity}{ordered}</size>";
            bool can = supply[i] > 0 && workshop.gold >= m.price;
            var b = ui.ledgerOrder[i];
            b.label.text = supply[i] <= 0 ? "Sold out" : $"Order 1   [{i + 1}]";
            b.normal = can ? new Color(0.40f, 0.28f, 0.14f) : new Color(0.52f, 0.47f, 0.40f);
            b.hover = Color.Lerp(b.normal, Color.white, 0.3f);
            b.background.color = b.normal;
        }
        string coming = Workshop.IngotList(workshop.ordered) ?? "nothing ordered yet";
        bool overflow = false;
        for (int i = 0; i < workshop.ordered.Length; i++)
            if (workshop.ordered[i] > 0 && workshop.stock[i] + workshop.crate[i] + workshop.ordered[i] > Workshop.BinCapacity) overflow = true;
        ui.ledgerFooter.text = $"<b>Tomorrow's delivery:</b> {coming}{(overflow ? "  <i>(more than the bins hold - the rest waits in the crate)</i>" : "")}\n" +
                               (workshop.CrateTotal > 0 ? $"<b>Still in the crate:</b> {Workshop.IngotList(workshop.crate)}\n" : "") +
                               $"<b>Purse:</b> {workshop.gold} gold";
        ui.ledgerStatus.text = status;
        int rows = 0;
        foreach (var m in ForgeMaterials.All) if (workshop.IsUnlocked(m)) rows = m.index + 1;
        ui.LayoutLedger(rows);
    }

    // Unloads the crate into the bins - whatever doesn't fit stays in it for later, nothing is ever thrown away.
    private IEnumerator OpenDelivery()
    {
        var moved = workshop.CollectCrate();
        string got = Workshop.IngotList(moved);
        string left = Workshop.IngotList(workshop.crate);
        if (got != null)
        {
            sound.Pickup();
            SaveGame();
        }
        if (left == null) StartCoroutine(CrateLeaves());
        if (got == null) yield return Say($"The bins are full - it stays in the crate until there's room ({left}).", 2.2f);
        else if (left != null) yield return Say($"Into storage: {got}.   No room for {left} - it stays in the crate.", 2.6f);
        else yield return Say($"Into storage: {got}.", 1.6f);
    }

    // The emptied crate is carried off: it sinks and shrinks away rather than blinking out.
    private bool crateLeaving;
    private IEnumerator CrateLeaves()
    {
        crateLeaving = true;
        Transform t = crateObject.transform;
        Vector3 pos = t.position;
        for (float k = 0f; k < 1f; k += Time.deltaTime / 0.45f)
        {
            float s = 1f - Mathf.SmoothStep(0f, 1f, k);
            t.localScale = new Vector3(s, s, s);
            t.position = pos + Vector3.down * (1f - s) * 0.1f;
            yield return null;
        }
        crateObject.SetActive(false);
        t.localScale = Vector3.one;
        t.position = pos;
        crateLeaving = false;
    }

    // ---------------------------------------------------------------- the blacksmith journal

    // Browses every sword the smith has finished, newest first. Runs inline in the Main Room loop, so the
    // camera, storage, customers and other interactions are all paused while it's open - and it always
    // closes its own panel on the way out.
    private IEnumerator OpenJournal()
    {
        // Page 0 is the Chronicle (notable works, people, metals, the city's events); then one page per weapon.
        int count = workshop.history.swords.Count + 1;
        int index = journalIndex >= 0 && journalIndex < count ? journalIndex : count - 1;
        sound.Pickup();
        ShowJournalPage(index);
        ui.ShowJournal(true);
        yield return null; // don't let the E that opened it close it on the same frame

        bool prevHeld = true, nextHeld = true, closeHeld = true;
        while (true)
        {
            var kb = Keyboard.current;
            bool prevNow = kb != null && (kb.aKey.isPressed || kb.leftArrowKey.isPressed);
            bool nextNow = kb != null && (kb.dKey.isPressed || kb.rightArrowKey.isPressed);
            bool closeNow = kb != null && (kb.eKey.isPressed || kb.escapeKey.isPressed);
            bool prev = prevNow && !prevHeld, next = nextNow && !nextHeld, close = closeNow && !closeHeld;
            prevHeld = prevNow; nextHeld = nextNow; closeHeld = closeNow;

            if (ClickedThisFrame() && Mouse.current != null)
            {
                Vector2 pos = Mouse.current.position.ReadValue();
                if (ui.journalPrev.Contains(pos)) prev = true;
                else if (ui.journalNext.Contains(pos)) next = true;
                else if (ui.journalClose.Contains(pos)) close = true;
            }
            if (close) break;
            if (count > 1 && (prev || next))
            {
                // Newest is the last record; "Previous" goes back in time, wrapping round at either end.
                index = (index + (next ? 1 : -1) + count) % count;
                sound.Clack(true);
                ShowJournalPage(index);
            }
            yield return null;
        }

        journalIndex = index;
        ui.ShowJournal(false);
        // Keys used inside the journal must be released before they count again out here: the E that closed
        // it mustn't re-open it, and A/D used to turn pages mustn't turn the seat.
        useHeld = lookAHeld = lookDHeld = escHeld = true;
        yield return null;
    }

    private void ShowJournalPage(int index)
    {
        var h = workshop.history;
        int pages = h.swords.Count + 1;
        if (index <= 0 || index > h.swords.Count)
        {
            WorldDirector.ChroniclePages(workshop, out string left, out string right);
            if (h.swords.Count == 0) left += "\n\nNo completed weapons yet - your first creation will get a page of its own.";
            ui.SetJournalPage(left, right, $"Chronicle  ·  page 1 of {pages}", null, pages > 1);
            return;
        }
        var r = h.swords[index - 1];
        ui.SetJournalPage(JournalLeftPage(r), JournalRightPage(r), $"Page {index + 1} of {pages}",
            MaterialColorFor(r.sword.materialName), pages > 1, r.sword.weaponType);
    }

    private string JournalLeftPage(SwordRecord r)
    {
        var s = r.sword;
        var owner = workshop.history.Owner(r);
        string status;
        switch (r.status)
        {
            case SwordStatus.InStorage:
                var reserved = workshop.FindOrder(r.reservedForOrder);
                status = reserved != null ? $"Reserved for {reserved.Label} ({(reserved.IsChallenge ? "Orla" : reserved.customerName)}) - on your rack" : "In storage - on your rack";
                break;
            case SwordStatus.OwnedByCustomer: status = $"Owned by {owner?.Title ?? "a customer"}"; break;
            case SwordStatus.NeedsRepair: status = r.RepairedAwaitingPickup ? "Repaired - waiting for its owner" : "In for repair - on your rack"; break;
            case SwordStatus.Destroyed: status = "Destroyed"; break;
            default: status = "Scrapped"; break;
        }
        var w = s.Weapon;
        string legend = (r.notable ? $"★ NOTABLE - {r.notableReason}\n" : "") + (r.legendName != null ? $"Called '{r.legendName}'\n" : "");
        string condition = r.status == SwordStatus.OwnedByCustomer || r.status == SwordStatus.NeedsRepair ? $"\nCondition: {r.ConditionText}" : "";
        string now = Mathf.Approximately(s.durability, r.originalDurability) && Mathf.Approximately(s.balance, r.originalBalance) &&
                     Mathf.Approximately(s.sharpness, r.originalSharpness)
            ? ""
            : $"\nNow:  {s.durability:0}  /  {s.balance:0}  /  {w.SharpnessText(s.sharpness)}";
        return $"{(r.notable ? "★ " : "")}{r.Label}\nType: {w.name}     Material: {s.materialName}{(s.cracked ? " (cracked)" : "")}\n{legend}Forged on day {r.createdDay}  -  Grade {s.Grade}\n\n" +
               $"Status: {status}{condition}\n" +
               (owner != null && r.status != SwordStatus.OwnedByCustomer ? $"Owner: {owner.Title}\n" : "") +
               $"\nAs forged:\nDurability {r.originalDurability:0}\nBalance {r.originalBalance:0}\nSharpness {w.SharpnessText(r.originalSharpness)}{now}\n\n" +
               $"Repairs: {r.repairCount}    Times returned: {r.timesReturned}";
    }

    private string JournalRightPage(SwordRecord r)
    {
        // The order in brief (what they asked for, not the whole conversation), who has it now, and what happened to it.
        var reservedFor = r.status == SwordStatus.InStorage ? workshop.FindOrder(r.reservedForOrder) : null;
        string order = r.order != null ? $"ORIGINAL ORDER{(r.order.id > 0 ? "  -  " + r.order.Label : "")}\n\"{r.order.paperQuote ?? r.order.requestText}\"\n\n"
                     : reservedFor != null ? $"RESERVED FOR {reservedFor.Label.ToUpper()}  -  {(reservedFor.IsChallenge ? "Orla" : reservedFor.customerName)}\n\"{reservedFor.paperQuote}\"\n\n"
                     : "ORIGINAL ORDER\nNone - forged for the rack.\n\n";
        var owner = workshop.history.Owner(r);
        string who = owner != null && owner.archetypeIndex != CustomerArchetype.MerchantIndex ? NpcStories.OwnerBlock(owner, workshop.history) + "\n\n" : "";
        var lines = new List<string>();
        foreach (var e in workshop.history.EventsFor(r))
            lines.Add($"Day {e.day}: {EventText(e)}");
        // The newest events matter most - keep the page from overflowing on a well-travelled sword.
        int keep = who.Length > 0 ? 7 : 9;
        if (lines.Count > keep) lines.RemoveRange(0, lines.Count - keep);
        return order + who + "ITS STORY\n" + string.Join("\n", lines);
    }

    private string EventText(HistoryEvent e)
    {
        var c = workshop.history.Customer(e.customerId);
        string who = c != null ? c.name : "someone";
        switch (e.type)
        {
            case HistoryEventType.Forged: return e.note;
            case HistoryEventType.WeaponPurchased: return e.note != null && e.note.StartsWith("Order #") ? $"Delivered to {c?.name ?? who} ({e.note})" : $"Sold to {c?.Title ?? who}";
            case HistoryEventType.WeaponUsed: return $"{who}: {e.note}";
            case HistoryEventType.WeaponDamaged: return e.note;
            case HistoryEventType.WeaponReturnedForRepair: return $"{who} brought it back for repair";
            case HistoryEventType.WeaponRepaired: return e.note;
            case HistoryEventType.WeaponReturnedToOwner: return $"Handed back to {who}";
            case HistoryEventType.RepairAbandoned: return $"{who} took it back unrepaired";
            case HistoryEventType.NewOrderRequested: return $"{who} ordered a new one because of it";
            case HistoryEventType.WeaponDestroyed: return $"Destroyed - {e.note}";
            case HistoryEventType.WeaponScrapped: return e.note;
        }
        return e.note;
    }

    // Someone walks up to the table. Only called while nobody is already there - once a customer with an
    // order arrives (offered or accepted), they camp at the table instead of cycling on a timer, and the
    // player isn't forced to deal with them right away (see RunMainRoom's offeredOrder branch and AnswerOrder,
    // triggered by F once they face the table). Who turns up depends on what's been going on:
    //   - the Collector, once the scrap crate has something in it;
    //   - a recurring customer whose sword (the one you made) has worn down, asking for a repair;
    //   - a past customer whose sword has had time to make a story;
    //   - now and then a special commission (someone important), or someone sent by word of mouth;
    //   - a recurring customer wanting a new sword, shaped by how their last one did;
    //   - otherwise a stranger with a brand new order.
    // The choice, and everyone new, comes from WorldDirector (seeded, weighted by the day and the city's events).
    // Visitors who only came to talk say their piece and leave; orders and accepted repairs camp at the table.
    private IEnumerator RunCustomerVisit(float idleSeconds = 0f)
    {
        ui.ShowStorage(false);
        yield return TurnTo(CenterYaw);

        var h = workshop.history;
        int day = workshop.day;
        SwordRecord story = h.StoryDue(day);
        // Repairs and new orders count toward the day's arrivals; stories and the Collector don't. Anyone coming back
        // with a weapon only does so on the day they were (secretly) due back - see BlacksmithHistory.RollReturn.
        bool ordersOpen = NewWorkDue(idleSeconds); // one of today's arrival times has come
        SwordRecord worn = ordersOpen ? h.swords.Find(r => r.ReturnDue(day) && r.storyTold && r.condition < BlacksmithHistory.RepairThreshold && RegularReady(h.Owner(r))) : null;
        CustomerRecord regular = ordersOpen ? h.customers.Find(WantsNewSword) : null;
        CustomerRecord acquaintance = ordersOpen ? PickAcquaintance() : null;
        var due = DueOrder();
        // Who actually comes is the living world's call (WorldDirector): seeded per save and visit, weighted by what's
        // going on, with its guarantees (a new folk, a special commission, word of mouth) - see ChooseVisit.
        var kind = WorldDirector.ChooseVisit(workshop, new VisitContext
        {
            orderDue = due != null, eventTraderDue = EventTraderDue, collectorDue = CollectorDue, newWorkDue = ordersOpen,
            hasWorn = worn != null, hasStory = story != null, hasRegular = regular != null, hasAcquaintance = acquaintance != null,
        });

        switch (kind)
        {
            case VisitKind.CollectOrder:
            {
                // Someone whose order is in the book, on their day to come by: to collect it, or to ask how it's going.
                var who = h.Customer(due.customerId);
                visitingCustomerId = 0;
                if (who != null) { DressAs(who); ShowCustomerSword(due.IsRepair ? null : h.CarriedBy(who)); NpcStories.Seen(who, day); }
                else { DressCustomer(NpcAppearance.ForOrder(due, 7919 * due.customerId + 17)); ShowCustomerSword(null); }
                due.returnDay = day + 1; // once a day at most, whatever happens now
                due.returnHour = GameTime.ReturnHourFor(due, day + 1);
                yield return WalkCustomer(customerSpawn, customerStand, 2.2f);
                customer.rotation = FacingSeat;
                visitOrder = due;
                visitSince = Time.realtimeSinceStartup;
                yield break;
            }
            case VisitKind.EventTrader:
                yield return EventTraderVisit();
                yield break;
            case VisitKind.Collector:
                DressCustomer(new Color(0.36f, 0.16f, 0.42f), new Color(0.78f, 0.78f, 0.80f), true, Race.Human);
                ShowCustomerSword(null);
                yield return WalkCustomer(customerSpawn, customerStand, 3.0f); // he's in no hurry
                yield return CollectorVisit();
                break;
            case VisitKind.Repair:
            {
                gameTime.ArrivalUsed(); // one of today's customers with work
                var owner = h.Owner(worn);
                visitingCustomerId = owner.id;
                DressAs(owner);
                ShowCustomerSword(worn); // arrives wearing it - the player should recognise it
                yield return WalkCustomer(customerSpawn, customerStand, 2.2f);
                yield return RepairVisit(worn);
                visitingCustomerId = 0; // whether or not they left it with you, they go - and come back for it on their day
                break;
            }
            case VisitKind.Story:
                visitingCustomerId = story.ownerId;
                DressCustomer(NpcAppearance.ForOrder(story.order, 7919 * story.ownerId + 17));
                ShowCustomerSword(story);
                yield return WalkCustomer(customerSpawn, customerStand, 2.2f);
                yield return StoryVisit(story);
                visitingCustomerId = 0;
                break;
            case VisitKind.Nobody:
                yield break; // nobody new today (the story-teller didn't feel like it after all)
            default:
            {
                // Someone with new work: made here, synchronously, inside a seeded scope - the same person with the same
                // job on every reload of this moment - and saved at once as the offer standing at the table.
                gameTime.ArrivalUsed();
                CustomerRecord who;
                string headline = null;
                using (WorldRandom.Seeded(workshop.world.worldSeed, "gen", day, WorldDirector.VisitSalt(workshop) * 7 + (int)kind))
                {
                    switch (kind)
                    {
                        case VisitKind.Special:
                            offeredOrder = WorldDirector.Special(workshop, out who, out _);
                            headline = "A SPECIAL COMMISSION - someone important is walking up to your table!";
                            break;
                        case VisitKind.Referral:
                            offeredOrder = WorldDirector.Referred(workshop, WorldDirector.ReferralDue(workshop), out who);
                            break;
                        case VisitKind.Regular:
                        {
                            who = regular;
                            var previous = h.Find(regular.followUpForSwordId) ?? h.CarriedBy(regular);
                            offeredOrder = CustomerOrder.FollowUp(regular, previous, day, workshop.unlockedMetals);
                            PersonalizeFollowUp(offeredOrder, regular); // in their own manner, with their news since the last visit
                            WorldDirector.FinishFollowUp(workshop, offeredOrder, regular);
                            break;
                        }
                        case VisitKind.Acquaintance:
                            // Someone the smith has met before - bought something once, or was turned away - back with a new job.
                            who = acquaintance;
                            offeredOrder = WorldDirector.NewOrder(workshop, acquaintance);
                            break;
                        default:
                            // A new face: a person with a life of their own from now on (NpcStories), remembered from this visit on.
                            offeredOrder = WorldDirector.Stranger(workshop, out who);
                            break;
                    }
                }
                SaveGame(); // the person and their offer are part of the save from this moment
                DressAs(who);
                ShowCustomerSword(h.CarriedBy(who));
                if (headline != null) { sound.Fanfare("S"); yield return Say(headline, 2.2f); }
                yield return WalkCustomer(customerSpawn, customerStand, 2.2f);
                customer.rotation = FacingSeat; // facing the player
                yield break;
            }
        }

        yield return WalkCustomer(customerStand, customerSpawn, 1.8f);
        customer.gameObject.SetActive(false);
    }

    // "Ore: Copper 4  Iron 1" - the ingots in store, for the HUD.
    private string OreText
    {
        get
        {
            var parts = new List<string>();
            foreach (var m in ForgeMaterials.All) if (workshop.IsUnlocked(m)) parts.Add($"{m.name}\u00A0{workshop.stock[m.index]}");
            return "Ore: " + string.Join("  ", parts) + (workshop.CrateTotal > 0 ? "\nDelivery waiting!" : "");
        }
    }

    private bool CollectorDue => workshop.scrap.Count >= 2 && workshop.day - workshop.lastCollectorDay >= 3;

    // Would anybody come to the table right now? Nobody at night; once the day's orders are taken, only a
    // visitor with a story or the Collector - so the camera isn't swung round to an empty table.
    // By the clock: customers with new work only in customer hours and when one of today's arrival times has come
    // (GameTime.NewCustomerDue); people collecting their orders until a little later in the evening; stories, the
    // Collector and the Event Trader in customer hours. Nobody at night.
    private bool SomeoneToVisit(float idleSeconds = 0f)
    {
        if (DueOrder() != null) return true;
        if (!gameTime.IsCustomerHours) return false;
        return NewWorkDue(idleSeconds) || workshop.history.StoryDue(workshop.day) != null || CollectorDue || EventTraderDue;
    }

    private bool NewWorkDue(float idleSeconds) => OrdersOpen && gameTime.NewCustomerDue(idleSeconds);

    // An order whose customer comes by now: on their day, from about their hour on (ready ones first). Collecting isn't
    // new work, so the day's customer arrivals don't limit them - but they only come until returnVisitsEnd.
    private CustomerOrder DueOrder()
    {
        if (!gameTime.ReturnVisitHours) return null;
        CustomerOrder best = null;
        foreach (var o in workshop.orders)
        {
            if (!o.IsActive || o.IsChallenge || workshop.day < o.returnDay) continue;
            if (workshop.day == o.returnDay && gameTime.Hour < o.returnHour) continue; // later today
            if (best == null || (workshop.IsReady(o) && !workshop.IsReady(best))) best = o;
        }
        return best;
    }

    // Someone met before who isn't a regular (regulars have their own reasons to come - WantsNewSword, repairs):
    // a past buyer or someone once turned away, not seen for a few days, not tied up in anything at the smithy.
    private CustomerRecord PickAcquaintance()
    {
        var h = workshop.history;
        int day = workshop.day;
        var pool = h.customers.FindAll(c => !c.recurring && NpcStories.IsGenerated(c.archetypeIndex) && c.profile != null && c.profile.IsSet &&
                                            day >= c.nextVisitDay && day - c.profile.lastSeenDay >= 3 && c.id != CustomerIdAtTable() &&
                                            !h.swords.Exists(r => r.ownerId == c.id && r.status == SwordStatus.NeedsRepair) &&
                                            !HasOpenOrder(c));
        return pool.Count > 0 ? pool[Random.Range(0, pool.Count)] : null;
    }

    // A regular's follow-up order (CustomerOrder.FollowUp), told in their own manner: greeting, news since the last
    // visit, what they insist on - and it waits as long as they do.
    private void PersonalizeFollowUp(CustomerOrder order, CustomerRecord c)
    {
        NpcStories.EnsureProfile(c, workshop.history);
        string news = NpcStories.MemoryLine(c, workshop.history, workshop.day, aboutWeapon: false);
        order.requestText = $"{NpcStories.Greeting(c)} {(news != null ? news + " " : "")}{order.requestText}";
        order.titleOverride = c.Title;
        order.patienceDays = NpcStories.PatienceOf(c);
        NpcStories.Seen(c, workshop.day);
    }

    // Someone whose order is still in the book (they come back for THAT, on its day - see DueOrder).
    private bool HasOpenOrder(CustomerRecord c) => c != null && workshop.orders.Exists(o => o.customerId == c.id && o.IsActive);

    // Recurring customers keep coming back; one-off customers never do (their story ends with the sale).
    private bool RegularReady(CustomerRecord c) => c != null && c.recurring && workshop.day >= c.nextVisitDay;

    // A regular wants another sword once the last one broke, or once it's told its story and a few days
    // have passed - but not while their current one is worn out (that's a repair first) or in the shop.
    private bool WantsNewSword(CustomerRecord c)
    {
        if (!RegularReady(c)) return false;
        var h = workshop.history;
        if (h.swords.Exists(r => r.ownerId == c.id && r.status == SwordStatus.NeedsRepair)) return false;
        if (HasOpenOrder(c)) return false; // one job at a time: never a second new order while the first is still in the book
        if (c.followUpForSwordId != 0) return true;
        var carried = h.CarriedBy(c);
        return carried != null && carried.storyTold && carried.condition >= BlacksmithHistory.RepairThreshold && workshop.day - c.lastOrderDay >= 4;
    }


    // Shows a line of dialogue with a single Continue button and waits for it.
    private IEnumerator Talk(string text)
    {
        customer.rotation = FacingSeat;
        yield return ZoomTo(defaultFov - 12f);
        ui.dialogueText.text = text;
        ShowDialogueButtons(accept: false, ask: false, decline: false, cont: true);
        ui.ShowDialogue(true);
        yield return null;
        while (!(ClickedThisFrame() && Mouse.current != null && ui.continueButton.Contains(Mouse.current.position.ReadValue())) && !GivePressed())
            yield return null;
        ui.ShowDialogue(false);
        yield return ZoomTo(defaultFov);
    }

    // Two-button question (Accept/Decline with custom labels). F doesn't answer it - it has to be a choice.
    private IEnumerator Ask(string text, string yes, string no, System.Action<bool> answer)
    {
        customer.rotation = FacingSeat;
        yield return ZoomTo(defaultFov - 12f);
        ui.dialogueText.text = text;
        string oldYes = ui.acceptButton.label.text, oldNo = ui.declineButton.label.text;
        ui.acceptButton.label.text = yes;
        ui.declineButton.label.text = no;
        ShowDialogueButtons(accept: true, ask: false, decline: true, cont: false);
        ui.ShowDialogue(true);
        bool result;
        while (true)
        {
            if (ClickedThisFrame() && Mouse.current != null)
            {
                Vector2 pos = Mouse.current.position.ReadValue();
                if (ui.acceptButton.Contains(pos)) { result = true; break; }
                if (ui.declineButton.Contains(pos)) { result = false; break; }
            }
            yield return null;
        }
        ui.ShowDialogue(false);
        ui.acceptButton.label.text = oldYes;
        ui.declineButton.label.text = oldNo;
        yield return ZoomTo(defaultFov);
        answer(result);
    }

    private void ShowDialogueButtons(bool accept, bool ask, bool decline, bool cont)
    {
        ui.acceptButton.Show(accept);
        ui.askButton.Show(ask);
        ui.declineButton.Show(decline);
        ui.continueButton.Show(cont);
    }

    // A past customer comes back to tell you how their sword did.
    private IEnumerator StoryVisit(SwordRecord sold)
    {
        var outcome = SwordStories.Tell(sold, workshop);
        var teller = workshop.history.Owner(sold);
        if (teller != null) NpcStories.Seen(teller, workshop.day);
        sold.storyTold = true;
        workshop.history.LogCameBack(sold, workshop.day);
        workshop.history.LogUse(sold, workshop.day, sold.legendName != null ? $"won glory with it - now it's called '{sold.legendName}'" : "put it to good use");
        if (outcome.destroyed) workshop.history.Destroy(sold, workshop.day, "snapped clean in two");
        else workshop.history.ScheduleNextReturn(sold, workshop.day); // maybe again one day (a regular, for a repair)
        SaveGame();

        ShowReturnCard(sold);
        string reward = outcome.tip > 0 ? $"\n\n(They leave you {outcome.tip} gold as thanks.)" : "";
        yield return Talk($"\"I still have the {sold.sword.Weapon.noun} you made me...\" " + outcome.text + reward);
        HideReturnCard();
        if (outcome.destroyed) ShowCustomerSword(null); // nothing left on the belt to walk off with
        workshop.gold += outcome.tip;
        workshop.AddReputation(outcome.reputation);
        if (outcome.tip > 0) sound.Coin();
        SaveGame();
        string rep = outcome.reputation >= 0f ? "Your reputation grows." : "Word gets around... your reputation dips.";
        yield return Say(rep, 1.4f);
    }

    // A recurring customer is back with the very sword you made them, worn down from use, asking for a repair.
    // Taking it in puts it back on your rack (still theirs - it can't be sold or scrapped) until it's repaired
    // and handed back; turning them down leaves it with them, and they'll try again another day.
    private IEnumerator RepairVisit(SwordRecord worn)
    {
        var h = workshop.history;
        var owner = h.Owner(worn);
        int fee = workshop.RepairFee(worn);
        string request = $"{NpcStories.Greeting(owner)} {SwordStories.RepairRequest(worn)}".Trim();
        NpcStories.Seen(owner, workshop.day);
        bool accepted = false;
        workshop.history.LogCameBack(worn, workshop.day);
        ShowReturnCard(worn);
        yield return Ask($"{owner.Title} is back - with {worn.Label}, the {worn.sword.itemName.ToLower()} you forged on day {worn.createdDay}.\n" +
                         $"\"{request}\"\n(The repair pays {fee} gold.)",
            "Take it in", "Not now", a => accepted = a);
        HideReturnCard();

        if (accepted && inventory.Count >= Workshop.RackSize)
        {
            accepted = false;
            yield return Say("No free peg on the rack for it - clear one first.", 1.6f);
        }
        if (!accepted)
        {
            owner.nextVisitDay = workshop.day + 2;
            workshop.AddReputation(Workshop.RepRepairRefused);
            SaveGame();
            yield return Say($"{owner.name} will make do for now. (They're not pleased - reputation dips.)", 1.6f);
            yield break;
        }

        h.TakeInForRepair(worn, workshop.day);
        workshop.ordersToday++;
        var job = CustomerOrder.RepairJob(owner, worn, fee, request);
        job.patienceDays = NpcStories.PatienceOf(owner);
        workshop.Accept(job, workshop.day); // its weapon - their own - is reserved for it
        customerBody.Give();     // hands it over the counter
        Work(TimeCfg.repairTakeInMinutes, "looking the weapon over");
        ShowCustomerSword(null); // handed over the counter
        RefreshOrderPaper();
        ShowOrderPaper();
        sound.Pickup();
        SaveGame();
        yield return Talk($"\"Look after it. I'll be back for it in a day or two.\"\n({job.Label}: {worn.Label} is on your rack. At the forge, press R to repair it.)");
    }

    // ---------------------------------------------------------------- traders

    // The Event Trader: on her own schedule (every few days) while there's still a metal to unlock; with a
    // challenge running, she looks in once a day from ChallengeCheckDelay days after it was accepted.
    private bool EventTraderDue
    {
        get
        {
            if (!gameTime.IsCustomerHours) return false;
            if (workshop.challenge != null)
                return workshop.day >= workshop.challengeDay + Workshop.ChallengeCheckDelay && workshop.challengeCheckedDay != workshop.day;
            return WorldDirector.EventTraderMayCome(workshop); // her first visit falls in a window, not on a fixed day
        }
    }

    private IEnumerator EventTraderVisit()
    {
        var shop = workshop;
        var h = shop.history;
        int day = shop.day;
        DressCustomer(new Color(0.20f, 0.42f, 0.44f), new Color(0.55f, 0.25f, 0.12f), true, Race.Human);
        ShowCustomerSword(null);
        yield return WalkCustomer(customerSpawn, customerStand, 2.6f);
        gameTime.Spend(TimeCfg.traderMinutes, "with the Event Trader");
        WorldDirector.TraderVisited(workshop);

        if (shop.challenge == null)
        {
            var metal = shop.NextLockedMetal;
            var challenge = WorldDirector.Challenge(shop, metal); // a weapon the smith knows, needs within reach of their metals
            bool yes = false;
            yield return Ask($"{challenge.requestText}\n(She needs {challenge.NeedSummary()}.)", "Accept the challenge", "Not this time", a => yes = a);
            if (yes)
            {
                var orla = h.customers.Find(x => x.archetypeIndex == CustomerArchetype.MerchantIndex) ?? h.AddCustomer(challenge, recurring: false);
                challenge.customerId = orla.id; // Orla is on record like anyone else - the same Orla every time
                shop.challenge = challenge;
                shop.Accept(challenge, day); // an id like any order, so a weapon can be set aside for it
                challenge.deadlineDay = day + Workshop.ChallengeDays;
                shop.challengeDay = day;
                shop.challengeCheckedDay = day;
                RefreshOrderPaper();
                ShowOrderPaper();
                SaveGame();
                yield return Say($"Challenge accepted ({challenge.Label}). Orla looks in from day {day + Workshop.ChallengeCheckDelay} - set the weapon aside for her in storage.", 2.6f);
            }
            else
            {
                shop.nextEventDay = WorldDirector.NextTraderDay(shop);
                SaveGame();
                yield return Say("\"Another time, then.\" Orla moves on.", 1.6f);
            }
        }
        else
        {
            var c = shop.challenge;
            shop.challengeCheckedDay = day;
            // Only what the smith has set aside for her challenge - never just the best or newest blade on the rack.
            var assigned = shop.AssignedTo(c);
            var record = assigned.Count > 0 ? assigned[0] : null;
            // She judges what was set aside for her only now - short of her standard, she hands it back.
            if (record != null && !c.IsFulfilled(record.sword))
            {
                shop.Unassign(record, day);
                SaveGame();
                yield return Talk($"{record.Label}? No. {Shortfall(c, record.sword, refuse: true)} That's not the work I'm looking for.\n" +
                                  $"(It's back on your rack, unassigned. She looks in again tomorrow - her last day is day {shop.challengeDay + Workshop.ChallengeDays}.)");
                record = null;
                RefreshOrderPaper();
                yield return WalkCustomer(customerStand, customerSpawn, 1.8f);
                customer.gameObject.SetActive(false);
                yield break;
            }
            if (record != null)
            {
                var metal = ForgeMaterials.Find(c.unlocksMaterial);
                var buyer = h.Customer(c.customerId) ?? h.AddCustomer(c, recurring: false);
                h.Sell(record, buyer, c, day);
                c.status = OrderStatus.Completed;
                record.returnDay = -1; // it goes into her collection for good
                shop.Unlock(metal);
                WorldDirector.NoteUnlock(shop, metal);
                shop.AddIngots(metal.index, 2);
                shop.AddToSupply(metal);
                shop.AddReputation(Workshop.RepChallenge);
                shop.challenge = null;
                shop.nextEventDay = WorldDirector.NextTraderDay(shop);
                sound.Fanfare("S");
                SaveGame();
                yield return Talk($"Now THAT is {c.Weapon.WithArticle}! {record.Label} is exactly what I hoped for.\n" +
                                  $"{metal.name.ToUpper()} UNLOCKED. Here are two ingots to start with - and your supplier will list {metal.name.ToLower()} in the order book from now on.");
                yield return Say($"NEW METAL UNLOCKED: {metal.name.ToUpper()}", 2.2f);
            }
            else if (day >= shop.challengeDay + Workshop.ChallengeDays)
            {
                shop.Close(c, day, OrderStatus.Failed); // lifts any reservation
                shop.challenge = null;
                shop.nextEventDay = WorldDirector.NextTraderDay(shop);
                SaveGame();
                yield return Talk($"Still no {c.Weapon.noun} that meets my standard? A pity. Perhaps when you've grown - I'll pass this way again.");
            }
            else
            {
                SaveGame();
                yield return Talk($"Nothing has been set aside for me yet ({c.Label}: {c.NeedSummary()}). I'll look in again tomorrow - my last day is day {shop.challengeDay + Workshop.ChallengeDays}.");
            }
            RefreshOrderPaper();
            ShowOrderPaper();
        }
        yield return WalkCustomer(customerStand, customerSpawn, 1.8f);
        customer.gameObject.SetActive(false);
    }

    // A weapon coming back: its whole record on the note by the table, so the smith recognises their own work.
    private void ShowReturnCard(SwordRecord r)
    {
        var s = r.sword;
        var w = s.Weapon;
        var owner = workshop.history.Owner(r);
        ui.SetOrderPaper(
            $"{r.Label.ToUpper()} - COMES BACK\n{s.materialName} {w.name}\nCreated: day {r.createdDay}\n" +
            $"Sold to: {owner?.name ?? "a customer"} (day {r.dayDelivered})\nReturned: day {workshop.day}\n" +
            $"As forged: Dur {r.originalDurability:0}  Bal {r.originalBalance:0}  Sharp {w.SharpnessText(r.originalSharpness)}\n" +
            $"Condition: {r.ConditionText}");
        ui.ShowOrder(true);
    }

    private void HideReturnCard()
    {
        RefreshOrderPaper();
        ShowOrderPaper();
    }

    // The Collector of Cursed Blades buys everything in the scrap crate. Cracked blades fetch extra.
    private IEnumerator CollectorVisit()
    {
        int count = workshop.scrap.Count, cracked = workshop.CrackedScrap, pay = workshop.ScrapValue;
        string crackedNote = cracked > 0 ? $" And {cracked} of them CRACKED! Oh, how delightful." : "";
        yield return Talk(
            "Ahh... the Collector of Cursed Blades, at your service. I smell failure in that crate of yours - " +
            $"{count} unloved blade(s).{crackedNote} I'll take the lot for {pay} gold. Every broken sword has a story, you know.");
        workshop.gold += pay;
        workshop.scrap.Clear();
        workshop.lastCollectorDay = workshop.day;
        gameTime.Spend(TimeCfg.traderMinutes, "with the Collector");
        sound.Coin();
        SaveGame();
        yield return Say($"+{pay} gold for the scrap crate", 1.4f);
    }

    // ---------------------------------------------------------------- customers coming back for their orders

    // F with a customer back about their order. Ready: they get exactly the weapon(s) reserved for it (Workshop.Deliver)
    // and pay. Not ready: they ask, hear "not yet" and go until another day - the order stays in the book. Once their
    // patience (plus a little grace) has run out, they give up instead.
    private IEnumerator CollectVisit()
    {
        var o = visitOrder;
        var h = workshop.history;
        var who = h.Customer(o.customerId);
        if (workshop.IsReady(o))
        {
            yield return o.IsRepair ? HandBackRepair(o, who) : HandOver(o, who);
            yield break;
        }
        if (GameTime.IsOverdue(o, workshop.day, gameTime.Hour))
        {
            yield return GiveUp(o, who);
            yield break;
        }
        int next = Mathf.Min(workshop.day + Random.Range(1, 3), o.deadlineDay + 1);
        o.returnDay = next;
        o.returnHour = GameTime.ReturnHourFor(o, next);
        string when = DayWord(next);
        string lastChance = workshop.day >= o.deadlineDay ? " This is the last time I'll ask, mind." : "";
        string status;
        if (o.IsRepair)
            status = $"{o.Label}: at the forge, press R to repair {h.Find(o.repairSwordId)?.Label ?? "their weapon"}.";
        else
        {
            int have = workshop.AssignedTo(o).Count;
            string nouns = o.quantity > 1 ? PluralNoun(o.Weapon) : o.Weapon.noun;
            status = have == 0
                ? $"{o.Label}: nothing is set aside for it yet - click a finished {o.Weapon.noun} that meets it in storage to assign it."
                : $"{o.Label}: {have} of {o.quantity} {nouns} set aside so far.";
        }
        yield return Talk($"\"{NpcStories.Greeting(who)} Is my order ready?\"\nNot yet.\n\"Then I'll come back {when}.{lastChance}\"\n({status})");
        SaveGame();
        yield return VisitLeaves();
    }

    // The order's weapon(s) - exactly the ones set aside for it - are handed over, and only NOW does the customer judge
    // them against what they (secretly) needed (Workshop.Judge): happy, grudging (less pay, a small knock to your name),
    // or not having it at all - then it goes back on the rack unassigned and the order stays open for another try.
    private IEnumerator HandOver(CustomerOrder o, CustomerRecord who)
    {
        var h = workshop.history;
        int day = workshop.day;
        var weapons = workshop.AssignedTo(o);
        var refused = weapons.FindAll(r => Workshop.Judge(o, r.sword) == Workshop.Verdict.Refused);
        if (refused.Count > 0)
        {
            foreach (var r in refused) workshop.Unassign(r, day);
            workshop.AddReputation(Workshop.RepRefusedWeapon);
            WorldDirector.OnWeaponRefused(workshop, o);
            o.returnDay = Mathf.Min(day + Random.Range(1, 3), o.deadlineDay + 1);
            o.returnHour = GameTime.ReturnHourFor(o, o.returnDay);
            string when = DayWord(o.returnDay);
            string labels = string.Join(", ", refused.ConvertAll(r => r.Label));
            RefreshOrderPaper();
            SaveGame();
            yield return Talk($"\"{Shortfall(o, refused[0].sword, refuse: true)} I'll come back {when} - have something proper for me.\"\n" +
                              $"({labels} won't do for {o.Label}: back on your rack, unassigned. Your reputation dips.)");
            yield return VisitLeaves();
            yield break;
        }

        customerBody.Receive();  // takes what was set aside for them
        Work(TimeCfg.handOverMinutes, "handing the order over");
        int pay = 0, tip = 0;
        float rep = 0f;
        SwordRecord grudging = null;
        foreach (var r in weapons)
        {
            pay += workshop.Payment(o, r.sword, out int t);
            tip += t;
            rep += Workshop.SatisfactionRep(o, r.sword);
            if (grudging == null && Workshop.Judge(o, r.sword) == Workshop.Verdict.Grudging) grudging = r;
        }
        rep /= Mathf.Max(1, weapons.Count);
        if (rep > 0f) rep *= WorldDirector.RepMultiplier(o); // a special commission done well makes a name
        var worst = Workshop.Verdict.Excellent;
        foreach (var r in weapons) { var v = Workshop.Judge(o, r.sword); if (v > worst) worst = v; }
        bool firstSale = h.SoldCount == 0;
        string labelsAll = string.Join(", ", weapons.ConvertAll(r => r.Label));
        string words = grudging != null
            ? $"\"{Shortfall(o, grudging.sword, refuse: false)} I'll take it - but it's not what I asked for. {pay} gold, and that's generous.\""
            : $"\"This is exactly what I needed! Here's {pay} gold.{(tip > 0 ? $" And a little extra ({tip} gold) - it's even better than I hoped!" : "")}\"";
        yield return ZoomTo(defaultFov - 12f);
        ShowDialogueButtons(accept: false, ask: false, decline: false, cont: false);
        ui.dialogueText.text = $"{words}\n\n({o.Label} complete: {labelsAll} {(weapons.Count > 1 ? "are" : "is")} theirs now. They needed {o.NeedSummary()}.)";
        ui.ShowDialogue(true);
        yield return new WaitForSeconds(3.2f);
        ui.ShowDialogue(false);
        yield return ZoomTo(defaultFov);

        if (who == null) who = h.AddCustomer(o, recurring: false);
        // Whether they become a regular is decided at their first purchase (never after a disappointment).
        if (who.purchases == 0 && !who.recurring && grudging == null && who.archetypeIndex != CustomerArchetype.MerchantIndex)
            who.recurring = h.ShouldBecomeRecurring(Random.value);
        var delivered = workshop.Deliver(o, who, day);
        WorldDirector.OnDelivered(workshop, o, delivered, who, worst, firstSale); // word of mouth, standing, notable works
        workshop.gold += pay + tip;
        workshop.AddReputation(rep); // judged on what THEY asked for: a little for "it'll do", more for beating it, a loss for settling
        sound.Coin();
        visitOrder = null;
        RefreshOrderPaper();
        ShowOrderPaper();
        SaveGame();
        ShowCustomerSword(delivered[0]); // off they go, wearing it
        yield return WalkCustomer(customerStand, customerSpawn, 1.8f);
        customer.gameObject.SetActive(false);
    }

    // What the customer says is wrong with a weapon, in their own words - never a number. The stat furthest below
    // what they needed (or a crack) is the one they notice.
    private static string Shortfall(CustomerOrder o, WorkpieceData s, bool refuse)
    {
        if (s.cracked) return "There's a crack running right through this one! I'm not paying for that.";
        float d = o.minDurability > 0f ? s.durability - o.minDurability : float.MaxValue;
        float b = o.minBalance > 0f ? s.balance - o.minBalance : float.MaxValue;
        float e = o.minSharpness > 0f ? s.sharpness - o.minSharpness : float.MaxValue;
        if (d <= b && d <= e)
            return refuse ? "It flexes like a willow branch - it'd snap the first time I needed it." : "It's a little flimsier than I'd hoped.";
        if (b <= e)
            return refuse ? "It pulls to one side the moment I lift it - I can't work with that." : "It doesn't sit quite as evenly in the hand as I wanted.";
        return refuse ? "I could sit on this edge. It's nowhere near sharp enough." : "The edge isn't quite as keen as I asked for.";
    }

    // A repair order is ready: the very same weapon (same id) goes back to its owner.
    private IEnumerator HandBackRepair(CustomerOrder o, CustomerRecord who)
    {
        var h = workshop.history;
        var r = h.Find(o.repairSwordId);
        int pay = o.reward;
        string feel = !r.sword.Weapon.hasEdge ? "Rings true again - lovely!" : r.sword.sharpness >= 80f ? "Sharper than the day I bought it!" : r.sword.sharpness < 50f ? "Hm, still not the keenest edge... but it'll do." : "That'll do nicely.";
        customerBody.Receive(); // their own weapon back, mended
        Work(TimeCfg.handOverMinutes, "handing the repair back");
        yield return ZoomTo(defaultFov - 12f);
        ShowDialogueButtons(accept: false, ask: false, decline: false, cont: false);
        ui.dialogueText.text = $"\"Good as new! {feel} Here's {pay} gold.\"\n\n({o.Label} complete: {r.Label} goes home with {who?.name ?? "its owner"} - repaired {r.repairCount} time(s) now.)";
        ui.ShowDialogue(true);
        yield return new WaitForSeconds(3.2f);
        ui.ShowDialogue(false);
        yield return ZoomTo(defaultFov);

        h.ReturnToOwner(r, workshop.day, repaired: true);
        workshop.Close(o, workshop.day, OrderStatus.Completed);
        workshop.gold += pay;
        workshop.AddReputation(1.5f);
        sound.Coin();
        visitOrder = null;
        RefreshOrderPaper();
        ShowOrderPaper();
        SaveGame();
        ShowCustomerSword(r); // back on their belt
        yield return WalkCustomer(customerStand, customerSpawn, 1.8f);
        customer.gameObject.SetActive(false);
    }

    // Past their patience with nothing to collect: they give up on the order - the one real consequence of taking on
    // a job you can't do. Anything already set aside for it goes back to being the smith's; a repair customer takes
    // their own weapon home unrepaired.
    private IEnumerator GiveUp(CustomerOrder o, CustomerRecord who)
    {
        var h = workshop.history;
        if (o.IsRepair)
        {
            var r = h.Find(o.repairSwordId);
            yield return Talk($"I've waited long enough, smith. I'll take my {o.Weapon.noun} back as it is.");
            if (r != null && r.status == SwordStatus.NeedsRepair) { h.ReturnToOwner(r, workshop.day, repaired: false); ShowCustomerSword(r); }
            workshop.AddReputation(-6f);
        }
        else
        {
            yield return Talk($"I've waited long enough, smith. I'll find someone else who keeps their word.\n({o.Label} is cancelled.)");
            workshop.AddReputation(Workshop.RepWalkedOff);
        }
        WorldDirector.OnWalkedOff(workshop, o); // their folk hear about it
        workshop.Close(o, workshop.day, OrderStatus.Failed);
        RefreshOrderPaper();
        ShowOrderPaper();
        SaveGame();
        yield return Say("The customer leaves angry. Your reputation suffers.", 1.4f);
        yield return VisitLeaves();
    }

    // Whoever came about an order walks off (the order, if still open, keeps its returnDay).
    private IEnumerator VisitLeaves(string line = null)
    {
        visitOrder = null;
        if (line != null) StartCoroutine(Say(line, 1.4f));
        yield return WalkCustomer(customerStand, customerSpawn, 1.8f);
        customer.gameObject.SetActive(false);
    }

    private static string PluralNoun(WeaponInfo w) => w.noun.EndsWith("s") ? w.noun : w.noun + "s";

    // The player faced the waiting customer and pressed F to hear their order out.
    // "tomorrow", "on day 7"
    private string DayWord(int day) => day == workshop.day ? "later today" : day == workshop.day + 1 ? "tomorrow" : $"on day {day}";

    // What the offer asks of the smith's time: a rush wants it tomorrow evening; a contract, several weapons in a few days.
    private string DueHint(CustomerOrder o)
    {
        if (o.rush) return $" - RUSH: needed by tomorrow {GameTime.Clock(TimeCfg.deadlineHour)}";
        if (o.quantity > 1)
        {
            int days = Mathf.Max(2, Mathf.CeilToInt(o.quantity / Mathf.Max(0.5f, TimeCfg.contractWeaponsPerDay)));
            return $" - {o.quantity} weapons within {days} days (each ~{GameTime.Duration(GameTime.WeaponMinutes(workshop, o.weaponType))} of work)";
        }
        return "";
    }

    private IEnumerator AnswerOrder()
    {
        yield return ZoomTo(defaultFov - 12f);

        ui.dialogueText.text = offeredOrder.requestText + $"\n\n(Pays about {EstimatedPay(offeredOrder)} gold{DueHint(offeredOrder)}.)";
        bool canAsk = !offeredOrder.ShowNumbers(workshop.day);
        ShowDialogueButtons(accept: true, ask: canAsk, decline: true, cont: false);
        ui.ShowDialogue(true);

        bool accepted = false;
        while (true)
        {
            if (ClickedThisFrame() && Mouse.current != null)
            {
                Vector2 pos = Mouse.current.position.ReadValue();
                if (ui.acceptButton.Contains(pos)) { accepted = true; break; }
                if (ui.declineButton.Contains(pos)) { accepted = false; break; }
                if (ui.askButton.Contains(pos))
                {
                    // Spelling it out costs the tip and a slice of the pay - reading people is the smith's job.
                    offeredOrder.detailsAsked = true;
                    ui.askButton.Show(false);
                    ui.dialogueText.text = $"\"Fine, fine - I need {offeredOrder.NeedSummary()}.\"\n" +
                                           $"(Asking costs you the tip: about {EstimatedPay(offeredOrder)} gold now.)";
                }
            }
            yield return null;
        }
        ui.ShowDialogue(false);
        yield return ZoomTo(defaultFov);

        var regular = workshop.history.Customer(offeredOrder.customerId);
        if (accepted)
        {
            // Into the order book (its own id, a day to come back) - and the customer goes about their business.
            var order = offeredOrder;
            offeredOrder = null;
            if (regular == null) regular = workshop.history.AddCustomer(order, recurring: false); // every order has its customer on record
            workshop.Accept(order, workshop.day);
            workshop.ordersToday++;
            Work(TimeCfg.orderTalkMinutes, "talking the order over");
            workshop.history.LogNewOrder(regular, workshop.day, order.previousSwordId, $"{order.Label}: {order.paperQuote}");
            RefreshOrderPaper();
            ShowOrderPaper();
            SaveGame();
            string when = $"{DayWord(order.returnDay)}, around {GameTime.Clock(order.returnHour)}";
            string what = order.quantity > 1 ? $"{order.quantity} {PluralNoun(order.Weapon)}" : order.Weapon.WithArticle;
            yield return Talk($"\"Good. I'll come back for it {when}.\"\n({order.Label} is in your order book - due {GameTime.DueText(order)}. Forge {what} that meets it, then click it in storage to set it aside for them.)");
            yield return WalkCustomer(customerStand, customerSpawn, 1.8f);
            customer.gameObject.SetActive(false);
        }
        else
        {
            if (regular != null)
            {
                // They remember being turned away (and bring it up when they come back another day).
                regular.nextVisitDay = workshop.day + (regular.recurring ? 2 : Random.Range(3, 7));
                regular.profile.declinedDay = workshop.day;
                workshop.history.LogPerson(workshop.day, HistoryEventType.OrderDeclined, regular, $"Turned away: {offeredOrder.Weapon.WithArticle}");
            }
            offeredOrder = null;
            // They came to the smith for a service and were turned away - word gets around (among their own folk most).
            WorldDirector.OnDeclined(workshop, offeredOrder);
            workshop.AddReputation(Workshop.RepRefused);
            Work(TimeCfg.declineTalkMinutes, "talking");
            yield return Say("The customer walks off, unimpressed. Turning people away hurts your reputation.", 1.8f);
            yield return WalkCustomer(customerStand, customerSpawn, 1.8f);
            customer.gameObject.SetActive(false);
        }
    }

    // The order book pinned by the table: every open order in two short lines - who and what, then which weapon is
    // set aside for it and when they're back. Numbers only while the smith is an apprentice or asked for them.
    private void RefreshOrderPaper()
    {
        var all = new List<CustomerOrder>(workshop.orders);
        if (workshop.challenge != null) all.Add(workshop.challenge);
        if (all.Count == 0) return;
        var lines = new List<string> { $"ACTIVE ORDERS ({all.Count})" };
        foreach (var o in all) lines.Add(OrderLine(o));
        ui.SetOrderPaper(string.Join("\n", lines));
    }

    private string OrderLine(CustomerOrder o)
    {
        var h = workshop.history;
        int day = workshop.day;
        string who = o.IsChallenge ? "Orla (challenge)" : o.customerName;
        string what = o.IsRepair ? $"repair {h.Find(o.repairSwordId)?.Label}" : o.quantity > 1 ? $"{o.quantity} {PluralNoun(o.Weapon)}" : o.Weapon.name;
        if (!o.IsRepair && o.ShowNumbers(day))
        {
            var needs = new List<string>();
            if (o.minDurability > 0f) needs.Add($"Dur {o.minDurability:0}+");
            if (o.minBalance > 0f) needs.Add($"Bal {o.minBalance:0}+");
            if (o.minSharpness > 0f) needs.Add($"Sharp {o.minSharpness:0}+");
            if (o.requiredMaterial != null) needs.Add(o.requiredMaterial);
            what += $" ({string.Join(" ", needs)})";
        }
        else if (!o.IsRepair && o.requiredMaterial != null) what += $" ({o.requiredMaterial})";
        string weapons;
        if (o.IsRepair) weapons = workshop.IsReady(o) ? "repaired - READY" : "needs R at the forge";
        else
        {
            var assigned = workshop.AssignedTo(o);
            weapons = o.quantity == 1
                ? (assigned.Count == 1 ? $"{assigned[0].Label} set aside - READY" : "no weapon assigned")
                : $"{assigned.Count}/{o.quantity} assigned{(assigned.Count >= o.quantity ? " - READY" : "")}";
        }
        string back = o.returnDay < day || (o.returnDay == day && (o.returnHour <= 0f || o.returnHour <= workshop.hour)) ? "back today" : o.returnDay == day ? $"back ~{GameTime.Clock(o.returnHour)}" : $"back day {o.returnDay}";
        string due = o.deadlineDay == day ? $"DUE {GameTime.Clock(o.DeadlineHour)}" : $"due day {o.deadlineDay}";
        string when = o.IsChallenge ? $"until day {o.deadlineDay}" : $"{(o.rush ? "RUSH, " : "")}{due}, {back}";
        return $"#{o.id:0000} {who} - {what}\n     {weapons}, {when}";
    }

    private void ShowOrderPaper() => ui.ShowOrder(workshop.orders.Count > 0 || workshop.challenge != null);

    // The next order still waiting for a weapon (oldest first) - what the Ready screen suggests forging.
    private CustomerOrder NextOrderToForge()
    {
        foreach (var o in workshop.orders)
            if (o.IsActive && !o.IsRepair && workshop.AssignedTo(o).Count < o.quantity) return o;
        var c = workshop.challenge;
        return c != null && workshop.AssignedTo(c).Count < c.quantity ? c : null;
    }

    // Base pay for the order in the metal it needs (or iron), before any tip - just a rough promise.
    private int EstimatedPay(CustomerOrder order)
    {
        return Mathf.RoundToInt(workshop.BasePay(order, order.requiredMaterial ?? "Iron")) * Mathf.Max(1, order.quantity);
    }

    // Re-dresses the one customer model for whoever is visiting.
    private void DressCustomer(Color outfit, Color hair, bool robe, Race race)
    {
        int seed = Mathf.RoundToInt(outfit.r * 997f + outfit.g * 131f + outfit.b * 17f + hair.r * 7f);
        DressCustomer(robe ? NpcAppearance.Robed(race, outfit, hair, seed) : NpcAppearance.Generate(race, CityRole.Townsfolk, seed, outfit, hair));
    }

    private void DressCustomer(NpcAppearance look)
    {
        bool was = customer.gameObject.activeSelf;
        customer.gameObject.SetActive(true); // (the animator only takes orders while active)
        customerBody.Dress(look);
        customer.gameObject.SetActive(was);
    }

    private void DressAs(CustomerRecord c) => DressCustomer(NpcAppearance.ForPerson(c)); // as they look in the city too

    private NpcBody customerBody;
    private bool customerWalking;
    private float customerFidget; // when a visitor left standing at the table next shifts about
    private float customerArrivedAt;

    // The visitor at the table: gesturing while the talk is on, otherwise standing about - and, left waiting a
    // while, getting impatient.
    private void CustomerLife()
    {
        if (customerBody == null || !customer.gameObject.activeInHierarchy || customerWalking) return;
        string now = customerBody.State;
        if (now == "Give_Item" || now == "Receive_Item") return; // let a hand-over finish
        bool talking = ui.DialogueOpen;
        if (talking) { if (!now.StartsWith("Talk")) customerBody.Talk(); return; }
        if (now.StartsWith("Talk")) customerBody.Idle();
        customerFidget -= Time.deltaTime;
        if (customerFidget > 0f) return;
        customerFidget = Random.Range(6f, 12f);
        bool waitingLong = (visitOrder != null || offeredOrder != null) && Time.time - customerArrivedAt > 18f;
        if (waitingLong && Random.value < 0.6f) customerBody.Impatient();
    }
    private WeaponType? customerWeaponType; // which display model the belt object currently carries

    // The weapon the customer carries: shown only while they really own one of yours (null = none), as that
    // very kind of weapon, in the metal it was forged from - so a returning customer arrives visibly carrying
    // *that* weapon (worn to suit their build, see Weapons.WearOn).
    private void ShowCustomerSword(SwordRecord r)
    {
        bool show = r != null && r.status == SwordStatus.OwnedByCustomer;
        customerSword.SetActive(show);
        if (!show) return;
        var type = r.sword.weaponType;
        if (customerWeaponType != type)
        {
            Weapons.DisplayModel(type).Attach(customerSword, workpieceRenderer.sharedMaterial.shader);
            customerSwordBlade = customerSword.GetComponent<Renderer>().sharedMaterials[0];
            customerWeaponType = type;
        }
        Weapons.WearOn(customerSword.transform, type, customerBody.Proportions);
        customerSwordBlade.color = MaterialColorFor(r.sword.materialName);
    }

    // Standing at the table, facing the smith's seat.
    private Quaternion FacingSeat => Quaternion.LookRotation(new Vector3(homePose.position.x - customerStand.x, 0f, homePose.position.z - customerStand.z));

    // The durations were tuned for the old straight 6.4 m walk-in; they scale with the actual distance so the pace stays the same.
    private IEnumerator WalkCustomer(Vector3 from, Vector3 to, float duration)
    {
        float distance = Vector3.Distance(from, to);
        duration *= distance / 6.4f;
        customer.gameObject.SetActive(true);
        customer.position = from;
        Vector3 dir = to - from;
        if (dir.sqrMagnitude > 0.001f) customer.rotation = Quaternion.LookRotation(dir);
        // An even pace (the body's walk cycle is matched to it, so the feet stay planted), easing only over the
        // first and last steps.
        customerWalking = true;
        const float ease = 0.12f;
        float peak = distance / Mathf.Max(0.01f, duration * (1f - ease));
        for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
        {
            float k = t < ease ? t / ease : t > 1f - ease ? (1f - t) / ease : 1f;
            float s = t < ease ? 0.5f * t * t / ease : t > 1f - ease ? 1f - ease - 0.5f * (1f - t) * (1f - t) / ease : t - 0.5f * ease;
            customer.position = Vector3.Lerp(from, to, s / (1f - ease));
            customerBody.Walk(Mathf.Max(0.35f, peak * k));
            yield return null;
        }
        customer.position = to;
        customerWalking = false;
        customerBody.Idle();
        customerArrivedAt = Time.time;
        customerFidget = Random.Range(6f, 12f);
        if (to == customerStand) customer.rotation = FacingSeat;
    }

    // A/D each take one step round the seat's four waypoints (forge - table/meadow - storage - house, and
    // round again) and glide there automatically - a single tap is enough, there's no need to hold the key.
    private static readonly float[] LookYaws = { ForgeYaw, CenterYaw, StorageYaw, HouseYaw };

    private int CurrentLookIndex()
    {
        int best = 0;
        for (int i = 1; i < LookYaws.Length; i++)
            if (Mathf.Abs(Mathf.DeltaAngle(mainRoomYaw, LookYaws[i])) < Mathf.Abs(Mathf.DeltaAngle(mainRoomYaw, LookYaws[best]))) best = i;
        return best;
    }

    // Returns true (and the yaw to turn to) on the frame A or D goes down. Deliberately NOT a coroutine: the
    // Main Room loop used to `yield return HandleLookInput()` every frame, and even a nested coroutine that
    // ends at once costs a frame - so everything checked after it (Enter, F, rack clicks) ran a frame late,
    // when .wasPressedThisFrame was already false again, and about half those presses were silently lost.
    // Now the loop only yields when there's actually a turn to animate.
    private bool LookInput(out float targetYaw)
    {
        targetYaw = mainRoomYaw;
        var kb = Keyboard.current;
        if (kb == null) return false;
        // Rising edge tracked by hand off .isPressed.
        bool aNow = kb.aKey.isPressed;
        bool dNow = kb.dKey.isPressed;
        bool a = aNow && !lookAHeld;
        bool d = dNow && !lookDHeld;
        lookAHeld = aNow;
        lookDHeld = dNow;
        if (!a && !d) return false;

        int index = CurrentLookIndex();
        targetYaw = LookYaws[(index + (d ? 1 : -1) + LookYaws.Length) % LookYaws.Length];
        return true;
    }

    // Turns the seated camera the short way round to targetYaw (0..360).
    private IEnumerator TurnTo(float targetYaw, float duration = 0.6f)
    {
        float start = mainRoomYaw;
        float delta = Mathf.DeltaAngle(start, targetYaw);
        if (Mathf.Abs(delta) < 0.01f) { mainRoomYaw = targetYaw; yield break; }
        for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
        {
            mainRoomYaw = Mathf.Repeat(start + delta * Mathf.SmoothStep(0f, 1f, t), 360f);
            cam.transform.SetPositionAndRotation(homePose.position, Quaternion.Euler(homePose.rotation.eulerAngles.x, mainRoomYaw, 0f));
            yield return null;
        }
        mainRoomYaw = targetYaw;
        cam.transform.SetPositionAndRotation(homePose.position, Quaternion.Euler(homePose.rotation.eulerAngles.x, mainRoomYaw, 0f));
    }

    private IEnumerator ZoomTo(float targetFov, float duration = 0.5f)
    {
        float start = cam.fieldOfView;
        if (Mathf.Approximately(start, targetFov)) yield break;
        for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
        {
            cam.fieldOfView = Mathf.Lerp(start, targetFov, t);
            yield return null;
        }
        cam.fieldOfView = targetFov;
    }

    // Shows the shelf UI once the player has turned far enough toward the storage side, and keeps its
    // slots (3 fixed materials + every sword currently in storage) and hover tooltip up to date.
    private void UpdateStorageUI()
    {
        // The 3D swords on the pegs always mirror the rack, whether or not the player is looking at it.
        for (int i = 0; i < swordDisplays.Length; i++)
        {
            bool occupied = i < inventory.Count;
            swordDisplays[i].SetActive(occupied);
            if (!occupied) continue;
            var shown = inventory[inventory.Count - 1 - i];
            HangDisplay(i, shown.weaponType);
            swordDisplayBladeMats[i].color = MaterialColorFor(shown.materialName);
        }

        // Only metals the smith has unlocked are in the store at all (workshop.IsUnlocked - the same single source the
        // order book, the Ready screen and forging read): a locked metal's bin simply isn't on the shelf.
        for (int i = 0; i < ForgeMaterials.All.Length && i < storageDisplay.materialBins.Length; i++)
        {
            var binT = storageDisplay.materialBins[i];
            if (binT == null) continue;
            bool known = workshop.IsUnlocked(ForgeMaterials.All[i]);
            if (binT.gameObject.activeSelf != known) binT.gameObject.SetActive(known);
        }

        bool showStorage = LookingAtStorage;
        ui.ShowStorage(showStorage);
        if (!showStorage) return;

        // The bins and rack pegs are real 3D geometry (LowPoly.MaterialBin / DisplaySword) - the slots are just
        // invisible hover/tooltip hit-boxes laid over their on-screen footprint every frame (ForgeUI.PlaceSlot).
        for (int i = 0; i < ForgeMaterials.All.Length && i < ui.materialSlots.Length; i++)
        {
            var m = ForgeMaterials.All[i];
            if (!workshop.IsUnlocked(m)) { ui.materialSlots[i].tooltip = ""; continue; } // not on the shelf - no hover
            ui.materialSlots[i].tooltip =
                $"<b>{m.name}</b>  -  {workshop.stock[i]}/{Workshop.BinCapacity} ingots in the bin\n{m.price} gold an ingot from the order book\n{MaterialDifficultyText(m)}\n" +
                $"<i>{m.character}</i>  (max D{m.durCap:0} B{m.balCap:0} S{m.sharpCap:0})\n" +
                $"Sells for x{m.valueScale:0.0}\nWears {(m.wearScale > 1f ? "fast" : m.wearScale < 1f ? "slowly" : "normally")} on the grindstone";
            if (i < storageDisplay.materialBins.Length && storageDisplay.materialBins[i] != null)
            {
                var bin = storageDisplay.materialBins[i].GetComponent<Renderer>();
                if (bin != null) ui.PlaceSlot(ui.materialSlots[i], ScreenRect(bin.bounds));
            }
        }

        for (int i = 0; i < ui.swordSlots.Length; i++)
        {
            // Hover area: the hanging weapon's own on-screen footprint (weapons differ in size and pivot); an empty
            // peg keeps the old sword-shaped area (it has no tooltip, so it never shows anything anyway).
            if (i < storageDisplay.swordPegs.Length)
            {
                if (i < inventory.Count)
                {
                    Bounds wb = swordDisplays[i].GetComponent<Renderer>().bounds;
                    wb.Expand(new Vector3(0.08f, 0.04f, 0.04f));
                    ui.PlaceSlot(ui.swordSlots[i], ScreenRect(wb));
                }
                else
                {
                    Transform peg = storageDisplay.swordPegs[i];
                    Vector3 middle = RackGuardPoint(peg) + peg.up * (LowPoly.DisplaySwordGuardY - LowPoly.DisplaySwordLength * 0.5f);
                    ui.PlaceSlot(ui.swordSlots[i], ScreenRect(new Bounds(middle, new Vector3(0.3f, LowPoly.DisplaySwordLength + 0.05f, 0.2f))));
                }
            }
            if (i < inventory.Count)
            {
                WorkpieceData s = inventory[inventory.Count - 1 - i]; // most recently hung up first
                var w = s.Weapon;
                var r = workshop.history.Find(s);
                string title = r != null ? $"<b>{(r.notable ? "★ " : "")}{r.Label}</b>  -  Grade {s.Grade}" : $"<b>{s.itemName}</b>  -  Grade {s.Grade}";
                var reservedFor = r != null && r.status == SwordStatus.InStorage ? workshop.FindOrder(r.reservedForOrder) : null;
                string whose = r != null && r.status == SwordStatus.NeedsRepair
                    ? $"{workshop.history.Owner(r)?.name ?? "A customer"}'s - in for repair{(r.RepairedAwaitingPickup ? " (done)" : "")}"
                    : reservedFor != null ? $"RESERVED for {reservedFor.Label} - {(reservedFor.IsChallenge ? "Orla" : reservedFor.customerName)}"
                    : "Click to set it aside for an order, or scrap it";
                ui.swordSlots[i].tooltip =
                    $"{title}\nType: {w.name}   Material: {s.materialName}{(s.cracked ? "  -  CRACKED" : "")}\n" +
                    $"Durability  {s.durability:0}\nBalance  {s.balance:0}\nSharpness  {w.SharpnessText(s.sharpness)}\n<i>{whose}</i>";
            }
            else
            {
                ui.swordSlots[i].tooltip = null;
            }
        }

        Vector2 mousePos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        string hover = null;
        foreach (var s in ui.materialSlots) if (s.Contains(mousePos)) hover = s.tooltip;
        foreach (var s in ui.swordSlots) if (s.Contains(mousePos)) hover = s.tooltip;
        if (hover == null && crateObject.activeInHierarchy && crateUse.ScreenRect(cam).Contains(mousePos))
            hover = $"<b>DELIVERY</b>\n{Workshop.IngotList(workshop.crate, "\n")}\n<i>{(workshop.CrateCanUnload ? "E or click to unload it" : "The bins are full for this - use some first")}</i>";
        else if (hover == null && ledgerObject.activeInHierarchy && ledgerUse.ScreenRect(cam).Contains(mousePos))
            hover = $"<b>MATERIAL ORDER BOOK</b>\nToday's supply: {Workshop.IngotList(workshop.Supply) ?? "sold out"}\n" +
                    $"Tomorrow's delivery: {Workshop.IngotList(workshop.ordered) ?? "nothing yet"}\n<i>E or click to order</i>";
        ui.KeepStorageInfoUnderHud();
        ui.SetStorageInfo(hover ??
            $"<b>STORAGE</b>\nRack: {inventory.Count}/{Workshop.RackSize} weapons\n" +
            $"Scrap crate: {workshop.scrap.Count}{(workshop.CrackedScrap > 0 ? $" ({workshop.CrackedScrap} cracked)" : "")}" +
            (workshop.scrap.Count > 0 ? $"\nThe Collector would pay {workshop.ScrapValue} gold" : "") +
            "\n<i>Hover an item for details</i>");
    }

    // The on-screen rectangle (pixels) a world-space box covers from the current camera.
    private Rect ScreenRect(Bounds b)
    {
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3((i & 1) != 0 ? b.max.x : b.min.x, (i & 2) != 0 ? b.max.y : b.min.y, (i & 4) != 0 ? b.max.z : b.min.z);
            Vector3 p = cam.WorldToScreenPoint(corner);
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    // ---------------------------------------------------------------- setting weapons aside for orders

    // A choice in the dialogue box: up to three labelled buttons (the first option on the right). E or Esc picks
    // nothing (-1).
    private IEnumerator Choose(string text, string[] options, System.Action<int> picked)
    {
        var buttons = new[] { ui.acceptButton, ui.askButton, ui.declineButton };
        var oldLabels = new string[buttons.Length];
        for (int i = 0; i < buttons.Length; i++)
        {
            oldLabels[i] = buttons[i].label.text;
            buttons[i].Show(i < options.Length);
            if (i < options.Length) buttons[i].label.text = options[i];
        }
        ui.continueButton.Show(false);
        ui.dialogueText.text = text;
        ui.ShowDialogue(true);
        yield return null;
        int result = -1;
        bool closeHeld = true;
        while (true)
        {
            var kb = Keyboard.current;
            bool closeNow = kb != null && (kb.eKey.isPressed || kb.escapeKey.isPressed);
            if (closeNow && !closeHeld) break;
            closeHeld = closeNow;
            if (ClickedThisFrame() && Mouse.current != null)
            {
                Vector2 pos = Mouse.current.position.ReadValue();
                for (int i = 0; i < options.Length && i < buttons.Length; i++)
                    if (buttons[i].Contains(pos)) result = i;
                if (result >= 0) break;
            }
            yield return null;
        }
        ui.ShowDialogue(false);
        for (int i = 0; i < buttons.Length; i++) buttons[i].label.text = oldLabels[i];
        useHeld = escHeld = lookAHeld = lookDHeld = true; // nothing pressed here carries over
        picked(result);
    }

    // "Order #0003 - Borin: a hammer" - who and what (and, withWords, what they said they need) - never the numbers
    // they didn't spell out, and never whether a weapon passes them.
    private string OrderBrief(CustomerOrder o, bool withWords = false)
    {
        string who = o.IsChallenge ? "Orla's challenge" : o.customerName;
        string what = o.quantity > 1 ? $"{o.quantity} {PluralNoun(o.Weapon)} ({workshop.AssignedTo(o).Count} set aside)" : o.Weapon.WithArticle;
        string said = withWords ? Clip(o.IsChallenge ? o.NeedSummary() : o.paperQuote, 90) : null; // their words, never the numbers (Orla states hers openly)
        return $"{o.Label} - {who}: {what}" + (said != null ? $" - \"{said}\"" : "");
    }

    private static string Clip(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max - 3).TrimEnd() + "...";

    // Open orders this weapon could go to (its kind, and a metal they named - nothing about its stats), oldest first.
    private List<CustomerOrder> OrdersFor(SwordRecord r, int max)
    {
        var fits = workshop.AssignableOrders.FindAll(o => workshop.CanAssign(o, r) == null);
        fits.Sort((a, b) => a.id.CompareTo(b.id));
        if (fits.Count > max) fits.RemoveRange(max, fits.Count - max);
        return fits;
    }

    // Straight after a new weapon is hung on the rack: if it meets an open order, offer to set it aside for it.
    // Never automatic - the smith decides which weapon belongs to which order.
    private IEnumerator OfferAssignment(SwordRecord r)
    {
        var fits = OrdersFor(r, 2);
        if (fits.Count == 0) yield break;
        var lines = new List<string> { $"{r.Label} could go to:" };
        var options = new List<string>();
        foreach (var o in fits) { lines.Add(OrderBrief(o, withWords: true)); options.Add(o.Label.Replace("Order ", "")); }
        lines.Add("Is it what they asked for? That's your call - they judge it when they collect.");
        options.Add("Keep it");
        int pick = -1;
        yield return Choose(string.Join("\n", lines), options.ToArray(), i => pick = i);
        if (pick >= 0 && pick < fits.Count) yield return AssignNow(fits[pick], r);
    }

    private IEnumerator AssignNow(CustomerOrder o, SwordRecord r)
    {
        string why = workshop.Assign(o, r, workshop.day);
        if (why != null) { yield return Say(why, 1.8f); yield break; }
        sound.Clack(true);
        RefreshOrderPaper();
        ShowOrderPaper();
        SaveGame();
        yield return Say($"{r.Label} is reserved for {o.Label} ({(o.IsChallenge ? "Orla" : o.customerName)})" +
                         (workshop.IsReady(o) ? " - ready for them to collect." : "."), 1.8f);
    }

    // Which weapon on the rack was clicked (null = none) - never one under the order book or the crate.
    private SwordRecord StorageClick()
    {
        if (!LookingAtStorage || !ClickedThisFrame() || Mouse.current == null) return null;
        Vector2 pos = Mouse.current.position.ReadValue();
        if (ledgerObject.activeInHierarchy && ledgerUse.ScreenRect(cam).Contains(pos)) return null;
        if (crateObject.activeInHierarchy && crateUse.ScreenRect(cam).Contains(pos)) return null;
        for (int i = 0; i < ui.swordSlots.Length && i < inventory.Count; i++)
            if (ui.swordSlots[i].Contains(pos)) return workshop.history.Find(inventory[inventory.Count - 1 - i]);
        return null;
    }

    // A weapon on the rack, clicked: set it aside for an order it meets, lift its reservation, or scrap it. A
    // reserved weapon can't be scrapped or given to anyone else until it's unassigned; a customer's weapon in for
    // repair is only shown.
    private IEnumerator WeaponActions(SwordRecord r)
    {
        if (r == null) yield break;
        sound.Pickup();
        var h = workshop.history;
        var s = r.sword;
        string head = $"{r.Label} - {s.itemName}, grade {s.Grade}";
        if (r.status == SwordStatus.NeedsRepair)
        {
            var job = workshop.FindOrder(r.reservedForOrder);
            yield return Choose($"{head}\n{h.Owner(r)?.name ?? "A customer"}'s own weapon, in for repair{(job != null ? $" ({job.Label})" : "")}.\nAt the forge, press R to work it on the grindstone.",
                new[] { "Close" }, _ => { });
            yield break;
        }
        if (r.IsReserved)
        {
            var o = workshop.FindOrder(r.reservedForOrder);
            int pick = -1;
            yield return Choose($"{head}\nRESERVED for {(o != null ? OrderBrief(o) : $"Order #{r.reservedForOrder:0000}")}.\nIt can't be scrapped or given to anyone else unless you unassign it first.",
                new[] { "Keep it", "Unassign" }, i => pick = i);
            if (pick == 1)
            {
                workshop.Unassign(r, workshop.day);
                RefreshOrderPaper();
                SaveGame();
                sound.Clack(false);
                yield return Say($"{r.Label} is no longer reserved for {o?.Label ?? "its order"}.", 1.6f);
            }
            yield break;
        }
        var fits = OrdersFor(r, 2);
        var lines = new List<string> { head };
        var options = new List<string>();
        if (fits.Count == 0) lines.Add(workshop.AssignableOrders.Count > 0 ? $"No open order is for {s.Weapon.WithArticle} in {s.materialName.ToLower()}." : "No open order is waiting for a weapon.");
        foreach (var o in fits) { lines.Add($"Could go to {OrderBrief(o, withWords: true)}"); options.Add(o.Label.Replace("Order ", "")); }
        options.Add("Scrap it");
        lines.Add("E: leave it as it is.");
        int choice = -1;
        yield return Choose(string.Join("\n", lines), options.ToArray(), i => choice = i);
        if (choice < 0) yield break;
        if (choice < fits.Count) { yield return AssignNow(fits[choice], r); yield break; }
        // Scrap: into the crate for the Collector. It stays in the journal, marked Scrapped.
        h.Scrap(r, workshop.day);
        workshop.scrap.Add(s);
        SaveGame();
        sound.Clack(false);
        yield return Say($"{r.Label} tossed in the scrap crate", 1.0f);
    }

    // Looks up a stored sword's forged metal by name to tint its rack display blade - falls back to a
    // neutral steel tone if the name doesn't match (shouldn't happen; materialName is always set from
    // ForgeMaterials.All[x].name in RunReady).
    private static Color MaterialColorFor(string materialName)
    {
        foreach (var m in ForgeMaterials.All)
            if (m.name == materialName) return m.color;
        return new Color(0.72f, 0.78f, 0.85f);
    }

    private static string MaterialDifficultyText(ForgeMaterialInfo m)
    {
        if (m.windowScale >= 1f) return "Easy to work";
        if (m.windowScale >= 0.8f) return "Moderate to work";
        return "Hard to work";
    }

    // ---------------------------------------------------------------- stages

    private IEnumerator RunReady()
    {
        CurrentStage = ForgeStage.Ready;
        readyCancelled = false;
        ClearHandle();
        sound.StopLoops();
        repairTarget = null;
        // Start on a metal the smith can actually use right now: the one the order demands if it's in store,
        // otherwise the last one if there's still some, otherwise any unlocked metal with ingots left.
        var next = NextOrderToForge();
        var demanded = next != null && next.requiredMaterial != null ? ForgeMaterials.Find(next.requiredMaterial) : null;
        if (demanded != null && workshop.IsUnlocked(demanded) && workshop.stock[demanded.index] > 0) material = demanded;
        else if (!workshop.IsUnlocked(material) || workshop.stock[material.index] == 0)
        {
            material = ForgeMaterials.All[Workshop.StartingMetal];
            foreach (var m in ForgeMaterials.All)
                if (workshop.IsUnlocked(m) && workshop.stock[m.index] > 0) { material = m; break; }
        }
        // Whatever the oldest order still needing a weapon asks for is the natural default - the smith can still pick anything.
        if (next != null) weaponType = next.weaponType;
        item = NewItem();
        displayScale = 1f;
        ApplyMaterialVisual();
        SetPhase(0);
        PlaceWorkpiece(AnvilWorkpiecePos(0), Quaternion.Euler(0f, 90f, 0f));

        ui.bar.Show(false);
        ui.finishButton.Show(false);
        ui.message.text = "";

        if (fadeAlpha > 0f) yield return Fade(0f);
        yield return MoveCamera(homePose);

        ShowReadyText();
        while (true)
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.digit1Key.wasPressedThisFrame) ChooseMaterial(0);
                if (kb.digit2Key.wasPressedThisFrame) ChooseMaterial(1);
                if (kb.digit3Key.wasPressedThisFrame) ChooseMaterial(2);
                if (kb.rightArrowKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame) ChooseWeapon(1);
                if (kb.leftArrowKey.wasPressedThisFrame) ChooseWeapon(-1);
                if (kb.rKey.wasPressedThisFrame)
                {
                    var job = SwordAwaitingRepair();
                    if (job == null) StartCoroutine(Say("No customer's weapon is waiting for a repair.", 1.4f));
                    else if (!gameTime.CanStartRepair) StartCoroutine(Say($"It's too late to start a repair tonight ({gameTime.ClockText}).", 1.6f));
                    else { StartRepair(job); yield break; }
                }
            }
            if (MenuPressed() || (kb != null && kb.backspaceKey.wasPressedThisFrame))
            {
                readyCancelled = true; // nothing taken, nothing spent
                yield break;
            }
            if (EnterPressed() && !gameTime.CanStartWeapon)
            {
                StartCoroutine(Say($"It's too late to begin another weapon ({gameTime.ClockText}) - it would run past midnight. Esc to go back.", 2f));
                yield return null;
                continue;
            }
            if (EnterPressed())
            {
                // Metal comes out of the store (ordered from the supplier) - one ingot per weapon.
                if (workshop.IsUnlocked(material) && workshop.stock[material.index] > 0)
                {
                    workshop.stock[material.index]--;
                    sound.Pickup();
                    SaveGame();
                    break;
                }
                var starter = ForgeMaterials.All[Workshop.StartingMetal];
                if (workshop.TotalStock == 0 && workshop.gold < starter.price && workshop.history.SmithOwnedCount == 0 && freeIngotDay != workshop.day)
                {
                    // Flat broke, nothing in store and nothing to sell: never a dead end - an old friend spots you an ingot.
                    freeIngotDay = workshop.day;
                    ChooseMaterial(starter.index);
                    yield return Say($"Broke! Old Tom from the mine spots you a {starter.name.ToLower()} ingot. Free.", 2.0f);
                    break;
                }
                StartCoroutine(Say(workshop.TotalStock == 0
                    ? "Your ore store is empty. Order more in the Material Order Book by the storage - it arrives the next morning."
                    : $"No {material.name.ToLower()} left in your store - pick another metal, or order more in the Material Order Book (it arrives the next morning).", 2.0f));
            }
            yield return null;
        }
    }

    private bool readyCancelled; // Esc at Ready: back to the table, nothing started

    // A customer's sword on the rack that still needs its repair work.
    private SwordRecord SwordAwaitingRepair() => workshop.history.swords.Find(r => r.AwaitingRepairWork);

    // Repairing works the customer's own sword - the very same object, same ID - on the grindstone. No metal
    // to buy and no new blade: MainLoop goes straight to RunGrindstone and then FinishRepairWork.
    private ForgeMaterialInfo materialBeforeRepair;

    private void StartRepair(SwordRecord r)
    {
        repairTarget = r;
        materialBeforeRepair = material;
        material = ForgeMaterials.Find(r.sword.materialName);
        item = r.sword;
        sound.Pickup();
        SetPhase(LowPoly.PhaseCount - 1); // a finished weapon (of its own type), not an ingot
        FitFinishedHilt();                // ...with its handle still on it
        ApplyMaterialVisual();
    }

    private IEnumerator FinishRepairWork()
    {
        var r = repairTarget;
        repairTarget = null;
        material = materialBeforeRepair ?? material;
        item = NewItem();
        yield return MoveCamera(homePose);

        if (grindstone.Touches == 0)
        {
            yield return Say($"You never touched the stone - {r.Label} is still worn.", 1.8f);
            yield break;
        }
        workshop.history.FinishRepair(r, workshop.day);
        var repairOrder = workshop.FindOrder(r.reservedForOrder);
        workshop.RefreshStatus(repairOrder); // ready for its owner to collect
        RefreshOrderPaper();
        var kind = repairOrder != null ? repairOrder.repairKind : GameTime.RepairKindFor(r);
        Work(GameTime.RepairMinutes(workshop, kind), GameTime.RepairName(kind)); // one job, however it was split at the stone
        workshop.today.repairs++;
        SaveGame();
        sound.Fanfare(r.sword.Grade);
        yield return Say($"{r.Label} repaired!  Sharpness {r.sword.Weapon.SharpnessText(r.sword.sharpness)}  Durability {r.sword.durability:0}\n{(repairOrder != null ? $"{repairOrder.Label} is ready - its owner collects it when they come by." : "")}", 2.4f);
    }

    private int freeIngotDay = -1;

    private void ChooseMaterial(int index)
    {
        var m = ForgeMaterials.All[index];
        if (!workshop.IsUnlocked(m))
        {
            StartCoroutine(Say($"You don't know how to work {m.name.ToLower()} yet - an Event Trader challenge unlocks it.", 1.8f));
            return;
        }
        sound.Pickup();
        material = m;
        item.materialName = material.name;
        item.itemName = $"{material.name} {item.Weapon.name}";
        ApplyMaterialVisual();
        ShowReadyText();
    }

    // A fresh piece of metal for the chosen weapon and metal.
    private WorkpieceData NewItem()
    {
        var w = Weapons.Get(weaponType);
        return new WorkpieceData { materialName = material.name, weaponType = weaponType, itemName = $"{material.name} {w.name}", weight = w.weight };
    }

    // Left/Right (or Tab) at Ready: what to forge. The ingot looks the same for every weapon - the shape comes
    // at the anvil.
    private void ChooseWeapon(int step)
    {
        sound.Pickup();
        int n = Weapons.All.Length;
        weaponType = (WeaponType)((((int)weaponType + step) % n + n) % n);
        item = NewItem();
        ApplyMaterialVisual();
        ShowReadyText();
    }

    private void ShowReadyText()
    {
        var m = ForgeMaterials.All;
        var w = Weapons.Get(weaponType);
        var next = NextOrderToForge();
        string wanted = next != null
            ? (next.weaponType == weaponType ? $"  (for {next.Label} - {next.customerName})" : $"  ({next.Label} wants {next.Weapon.WithArticle})")
            : "";
        var metals = new List<string>();
        for (int i = 0; i < m.Length; i++)
            if (workshop.IsUnlocked(m[i])) metals.Add($"{i + 1} {m[i].name} x{workshop.stock[i]}"); // unknown metals aren't offered at all
        // How long it'll take - the planning question ("is there time for a sword today, or only a repair?").
        float minutes = GameTime.WeaponMinutes(workshop, weaponType);
        string estimate = gameTime.CanStartWeapon
            ? $"~{GameTime.Duration(minutes)} of work, done about {GameTime.Clock(Mathf.Min(gameTime.Hour + minutes / 60f, TimeCfg.latestHour))}"
            : "too late to begin another weapon today";
        string repair = "";
        if (SwordAwaitingRepair() is SwordRecord job)
        {
            var kind = workshop.FindOrder(job.reservedForOrder)?.repairKind ?? GameTime.RepairKindFor(job);
            repair = $"   Or R to repair {job.Label} for {workshop.history.Owner(job)?.name ?? "its owner"} (~{GameTime.Duration(GameTime.RepairMinutes(workshop, kind))}).";
        }
        ui.instructions.text =
            $"Forge:  < {w.name.ToUpper()} >  (LEFT/RIGHT)  -  {w.strengths}{wanted}\n" +
            $"Metal:  {string.Join("    ", metals)}   -  {material.name}: {material.character} (Dur {material.durCap:0} / Bal {material.balCap:0} / Sharp {material.sharpCap:0} max)\n" +
            $"ENTER: take an ingot to the furnace ({estimate}).{repair}   Esc: back";
    }

    private IEnumerator RunFurnace()
    {
        CurrentStage = ForgeStage.Furnace;
        ui.instructions.text =
            "Hold the RIGHT MOUSE BUTTON to raise the rectangle, release to let it fall.\n" +
            "Keep the fire inside it to fill the HEAT bar before it empties.";
        // The ingot rests directly on top of the coal bed.
        PlaceWorkpiece(new Vector3(ForgeVisualPos.x, ForgeTop + LowPoly.PhaseHalfThickness(0), ForgeVisualPos.z), Quaternion.Euler(0f, 90f, 0f));

        yield return GoTo(FurnaceView());

        forgeStation.Begin(material);
        HeatGame game = forgeStation.game;
        ui.heatBar.SetFire(game.firePos);
        ui.heatBar.SetCatcher(game.catcherPos, game.catcherHalfHeight, false);
        ui.heatBar.SetProgress(game.progress);
        ui.heatBar.Show(true);

        while (RightMouseHeld()) yield return null; // don't let a stray held button carry over from an earlier stage

        while (!game.Tick(RightMouseHeld(), Time.deltaTime))
        {
            ui.heatBar.SetFire(game.firePos);
            ui.heatBar.SetCatcher(game.catcherPos, game.catcherHalfHeight, game.Caught);
            ui.heatBar.SetProgress(game.progress);
            SetWorkpieceColor(Color.Lerp(material.color, HotGlow, game.progress));
            sound.SetFire(game.Caught ? 0.55f + 0.45f * game.progress : 0.15f);
            fx.SetFire(game.Caught ? 0.6f + 0.4f * game.progress : 0.3f);
            yield return null;
        }
        ui.heatBar.SetFire(game.firePos);
        ui.heatBar.SetCatcher(game.catcherPos, game.catcherHalfHeight, game.Caught);
        ui.heatBar.SetProgress(game.progress);

        Rating rating = game.Result;
        sound.SetFire(0f);
        fx.SetFire(0.3f);
        sound.PlayRating(rating);
        item.hits = forgeStation.HitsFor(rating);
        Work(StageTime(WorkStage.Furnace), "at the furnace");
        yield return Say($"{RatingLabel(rating)}  {item.hits} hits on the anvil", 1.4f);
        ui.heatBar.Show(false);
    }

    private IEnumerator RunAnvil()
    {
        CurrentStage = ForgeStage.Anvil;
        ui.instructions.text =
            "Click the points on the item before they vanish.\nRED = +1 Durability, YELLOW = +1 Balance. Each round you can only take ONE of the two.";
        SetWorkpieceColor(Color.Lerp(material.color, HotGlow, 0.6f));
        SetPhase(0);
        PlaceWorkpiece(AnvilWorkpiecePos(0), Quaternion.Euler(0f, 90f, 0f));

        yield return GoTo(AnvilView());

        anvilStation.Begin(item.hits);
        item.shapeProgress = 0f;

        while (anvilStation.HitsLeft > 0)
        {
            PickPointPair(out Vector3 durPos, out Vector3 balPos);
            durabilityMarker.Activate(durPos, anvilStation.pointLifetime);
            balanceMarker.Activate(balPos, anvilStation.pointLifetime);
            bool missed = false;

            while (true)
            {
                if (ClickedThisFrame() && TryRaycastFromMouse(out RaycastHit hit))
                {
                    var marker = hit.collider.GetComponent<HitZoneMarker>();
                    if (marker != null)
                    {
                        anvilStation.RecordHit(marker.kind);
                        StartCoroutine(HammerSwing(marker.transform.position));
                        break;
                    }
                }

                if (!durabilityMarker.gameObject.activeSelf && !balanceMarker.gameObject.activeSelf)
                {
                    anvilStation.RecordMiss(); // both faded away - that hit is spent
                    missed = true;
                    break;
                }
                yield return null;
            }

            durabilityMarker.Deactivate();
            balanceMarker.Deactivate();

            item.shapeProgress = anvilStation.ShapeProgress;
            // Each weapon takes the hammer differently: a hammer head soaks up Durability, a dagger Balance (a sword: 10 and 10).
            item.durability = Mathf.Min(100f, anvilStation.DurabilityPoints * Weapon.durPerPoint);
            item.balance = Mathf.Min(100f, anvilStation.BalancePoints * Weapon.balPerPoint);

            // A hit changes the shape when the hammer lands (HammerSwing); a spent round without a hit still moves the work on.
            if (missed) SetPhase(LowPoly.PhaseFor(item.shapeProgress));

            yield return new WaitForSeconds(anvilStation.roundGap);
        }
        SetPhase(LowPoly.PhaseFor(item.shapeProgress));
        Work(StageTime(WorkStage.Anvil), "anvil work");

        yield return Say($"Shaped!  Durability {item.durability:0}   Balance {item.balance:0}", 1.4f);
    }

    private IEnumerator RunBarrel()
    {
        CurrentStage = ForgeStage.Barrel;
        ui.instructions.text =
            "Hold the SPACEBAR to lower the metal into the water and release at the perfect timing.\nPerfect = +10 Durability, Good = +3, Bad = -12 Durability - and the blade may CRACK.";

        // Blade is held upright and dipped tip-first (the tip is at local -Z); the barrel pivot is its centre (rim 0.475 above,
        // floor 0.475 below). It goes in almost to the hilt: only a short stub of the bare tang still pokes out of the water.
        const float barrelRim = 0.475f;
        const float exposedTang = 0.09f;
        Vector3 hover = BarrelPos + Vector3.up * 1.2f;
        // A piece with a long bare tang (sword, dagger, sickle) is held by it with a stub left above the water; a
        // hammer head or a spear head has nothing to hold above the waterline, so it goes all the way under.
        float exposed = MetalBackReach > 0.2f ? exposedTang : -0.05f;
        Vector3 dunked = BarrelPos + Vector3.up * (barrelRim + exposed - MetalBackReach);
        PlaceWorkpiece(hover, Quaternion.Euler(-90f, 0f, 0f));

        yield return GoTo(BarrelView());

        quenchBarrel.Begin(material);
        ui.bar.SetZones(quenchBarrel.bar.widths);
        ui.bar.SetPointer(0f);
        ui.bar.Show(true);

        while (SpaceHeld()) yield return null;

        float dip = 0f;
        bool splashed = false;
        while (!quenchBarrel.bar.Tick(SpaceHeld(), Time.deltaTime))
        {
            dip = Mathf.MoveTowards(dip, SpaceHeld() ? 1f : 0f, Time.deltaTime * 4f);
            sound.SetSteam(dip * (0.5f + 0.5f * quenchBarrel.bar.pointer));
            fx.SetSteam(dip * (0.5f + 0.5f * quenchBarrel.bar.pointer));
            if (!splashed && dip > 0.35f) { splashed = true; sound.Splash(); fx.SteamBurst(); }
            workpieceTransform.position = Vector3.Lerp(hover, dunked, Mathf.SmoothStep(0f, 1f, dip));
            SetWorkpieceColor(Color.Lerp(Color.Lerp(material.color, HotGlow, 0.6f), material.color, quenchBarrel.bar.pointer));
            ui.bar.SetPointer(quenchBarrel.bar.pointer);
            yield return null;
        }
        ui.bar.SetPointer(quenchBarrel.bar.pointer);

        Rating rating = quenchBarrel.bar.Result;
        sound.SetSteam(0.3f); // a last wisp of steam while the metal is lifted out
        fx.SetSteam(0.3f);
        sound.PlayRating(rating);
        float before = item.durability;
        bool cracked = quenchBarrel.Apply(item, rating, material);

        // Metal comes back out of the water.
        for (float t = 0f; t < 1f; t += Time.deltaTime * 3f)
        {
            workpieceTransform.position = Vector3.Lerp(workpieceTransform.position, hover, t);
            yield return null;
        }
        workpieceTransform.position = hover;
        sound.SetSteam(0f);
        fx.SetSteam(0f);
        SetWorkpieceColor(material.color);
        Work(StageTime(WorkStage.Quench), "quenching");

        if (cracked)
        {
            sound.Shatter();
            yield return Say($"CRACK! The shock split the {material.name.ToLower()}.  Durability {before:0} -> {item.durability:0}", 2.0f);
        }
        else
        {
            yield return Say($"{RatingLabel(rating)}  Durability {before:0} -> {item.durability:0}", 1.5f);
        }
        ui.bar.Show(false);
    }

    private IEnumerator RunGrindstone()
    {
        CurrentStage = ForgeStage.Grindstone;
        ui.instructions.text = "Press SPACE to start the grinding wheel spinning.\nPress ENTER or FINISH to stop early.";

        // Blade lies along the top edge of the wheel (its local Z axis is the tangent there - the new
        // Grindstone.fbx model spins around local X instead of the old procedural model's Z, so the tangent
        // moved from transform.right to transform.forward accordingly).
        // Lying on the top of the stone (its own half-thickness above the measured wheel top).
        Vector3 restPos = GrindWheelCenter + Vector3.up * (grindstone.wheelRadius + LowPoly.PhaseHalfThickness(item.weaponType, shownPhase) + 0.005f);
        // Held the way the player would hold it: handle end (local +Z: the bare tang, or the fitted hilt on a
        // repair) pointing straight back at the grindstone camera, blade reaching away across the top of the
        // stone. Level (horizontal), so it still lies flat on the wheel. This pose is the grindstone's alone -
        // every stage places the sword itself, so nothing carries over.
        Vector3 toCamera = GrindView().position - restPos;
        toCamera.y = 0f;
        Quaternion grindRot = Quaternion.LookRotation(toCamera.sqrMagnitude > 0.0001f ? toCamera : -grindstone.transform.forward);
        restPos = Centered(restPos, grindRot); // a sickle's crescent sits to one side of its handle line - keep it on the stone
        PlaceWorkpiece(restPos, grindRot);

        yield return GoTo(GrindView());

        ui.finishButton.Show(true);
        grindstone.Begin();
        grindstoneSpinning = false;

        while (SpaceHeld() || (Mouse.current != null && Mouse.current.leftButton.isPressed)) yield return null;

        bool finished = false;

        // Nothing grinds until the wheel is actually spinning - the player starts it whenever they're ready.
        // Edge tracked by hand off SpaceHeld() rather than a .wasPressedThisFrame helper (see LookInput
        // for why: that primitive missed presses for A/D under headless simulation in exactly this project).
        bool spaceWasHeld = false;
        while (!grindstoneSpinning)
        {
            Vector2 mp = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            if (EnterPressed() || (ClickedThisFrame() && ui.finishButton.Contains(mp))) { finished = true; break; }
            bool spaceNow = SpaceHeld();
            if (spaceNow && !spaceWasHeld) grindstoneSpinning = true;
            spaceWasHeld = spaceNow;
            yield return null;
        }
        if (!finished)
            ui.instructions.text =
                "Hold the left mouse button to press the blade to the stone, release at the right moment.\nEvery touch grinds a little Durability away, and each one is riskier than the last.\nUp to 4 touches - know when to stop! Press ENTER or FINISH to stop early.";

        while (!finished)
        {
            grindstone.PrepareTouch(material);
            ui.bar.SetZones(grindstone.bar.widths);
            ui.bar.SetPointer(0f);
            ui.bar.Show(true);

            bool touching = false;
            while (true)
            {
                Vector2 mousePos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;

                if (!touching && (EnterPressed() || (ClickedThisFrame() && ui.finishButton.Contains(mousePos))))
                {
                    finished = true;
                    break;
                }

                // The wheel keeps spinning on its own once started - LMB only presses the blade to it now.
                bool hold = Mouse.current != null && Mouse.current.leftButton.isPressed && !ui.finishButton.Contains(mousePos);
                if (hold) touching = true;

                bool locked = grindstone.bar.Tick(hold, Time.deltaTime);
                sound.SetGrind(hold && !locked ? 1f : 0f, 0.85f + 0.5f * grindstone.bar.pointer);
                fx.SetGrind(hold && !locked);

                // The blade shakes against the stone while pressed.
                Vector3 jitter = hold ? new Vector3(0f, -0.02f, 0f) + Random.insideUnitSphere * 0.006f : Vector3.zero;
                workpieceTransform.position = restPos + jitter;
                ui.bar.SetPointer(grindstone.bar.pointer);

                if (locked) break;
                yield return null;
            }

            sound.SetGrind(0f, 1f);
            fx.SetGrind(false);
            if (finished) break;

            workpieceTransform.position = restPos;
            Rating rating = grindstone.bar.Result;
            sound.PlayRating(rating);
            ui.message.text = grindstone.ApplyTouch(item, rating, material);
            if (grindstone.Touches >= grindstone.maxTouches) finished = true; // out of touches

            // Enter / Finish stay usable while the touch result is on screen.
            for (float t = 0f; t < 1f && !(finished && grindstone.Touches < grindstone.maxTouches); t += Time.deltaTime)
            {
                Vector2 mp = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
                if (EnterPressed() || (ClickedThisFrame() && ui.finishButton.Contains(mp))) finished = true;
                yield return null;
            }
            ui.message.text = "";
        }

        grindstoneSpinning = false;
        ui.finishButton.Show(false);
        ui.bar.Show(false);
        if (repairTarget == null) Work(StageTime(WorkStage.Grind), "at the grindstone"); // a repair counts as one job (FinishRepairWork)
        yield return Say($"Sharpening done after {grindstone.Touches} touch(es)", 1.1f);
    }

    private IEnumerator RunResult()
    {
        CurrentStage = ForgeStage.Result;
        ui.ShowOrder(false); // the note would sit on top of the stat list; it comes back after
        ui.instructions.text = "";

        yield return MoveCamera(homePose);
        yield return Fade(1f);

        // Screen is black: hide the workshop, float the finished sword in front of the camera.
        SetStationsVisible(false);
        SetWorkpieceColor(material.color);
        float resultScale = Weapon.resultScale; // long weapons (a spear) are shown a little smaller so they fit
        displayScale = resultScale;
        float angle = 0f;
        PlaceSwordForDisplay(angle);

        ui.resultTitle.text = $"Your Forged {Weapon.name}  -  Grade {item.Grade}";
        ui.resultStats.text =
            $"{item.itemName}{(item.cracked ? "  (CRACKED)" : "")}\n\nType: {Weapon.name}\nDurability: {item.durability:0}\nBalance: {item.balance:0}\nSharpness: {Weapon.SharpnessText(item.sharpness)}\nWeight: {item.weight:0.0} kg";
        bool rackFull = inventory.Count >= Workshop.RackSize;
        ui.addButton.label.text = rackFull ? "Rack is full" : "Hang on the Rack";
        ui.ShowMarkStamp(workshop.mark, workshop.MarkName);
        ui.ShowResult(true);
        sound.Fanfare(item.Grade);
        yield return Fade(0f, 0.6f);

        // The first sword ever finished: design the mark that goes on every blade from now on.
        if (!workshop.HasMark) yield return DesignMark(() => { angle += Time.deltaTime * 40f; PlaceSwordForDisplay(angle); });

        bool add = false;
        while (true)
        {
            angle += Time.deltaTime * 40f;
            PlaceSwordForDisplay(angle);

            if (ClickedThisFrame() && Mouse.current != null)
            {
                Vector2 pos = Mouse.current.position.ReadValue();
                if (ui.discardButton.Contains(pos)) { add = false; break; }
                if (ui.addButton.Contains(pos))
                {
                    if (!rackFull) { add = true; break; }
                    StartCoroutine(Say("No free pegs - scrap this one, or clear the rack in storage.", 1.6f));
                }
            }
            yield return null;
        }

        ui.ShowResult(false);
        Work(StageTime(WorkStage.Finish), "finishing"); // cleaned up and looked over - the weapon is done
        workshop.today.forged++;
        if (add)
        {
            // Only now does it become a real, finished sword: permanent ID, a journal entry, a peg on the rack.
            var record = workshop.history.RegisterForged(item.Clone(), workshop.day);
            SaveGame();
            sound.Coin();
            yield return Say($"{record.Label} hung on the rack  ({inventory.Count}/{Workshop.RackSize})", 1.0f);
            // Does it meet an open order? The smith decides whether to set it aside for one (never automatic).
            yield return OfferAssignment(record);
        }
        else
        {
            // Into the scrap crate: spin fast and shrink away. Not wasted - the Collector buys these. A blade
            // scrapped straight off the result screen never counted as a finished sword: no ID, no journal
            // entry, never sellable.
            workshop.scrap.Add(item.Clone());
            SaveGame();
            sound.Shatter();
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.5f)
            {
                angle += Time.deltaTime * 700f;
                displayScale = Mathf.Lerp(resultScale, 0f, t);
                PlaceSwordForDisplay(angle);
                yield return null;
            }
            yield return Say($"Tossed in the scrap crate  ({workshop.scrap.Count} there)", 1.0f);
        }

        yield return Fade(1f);
        SetStationsVisible(true);
        ShowOrderPaper();
        displayScale = 1f;
        // (The day doesn't end here any more - only at the house door, see EndDay.)
        RefreshOrderPaper();
    }

    // 3x3 grid of toggle cells; "Stamp it!" is only accepted once at least one cell is on.
    private IEnumerator DesignMark(System.Action spin)
    {
        int mark = 0;
        ui.addButton.Show(false);
        ui.discardButton.Show(false);
        ui.SetMarkCells(mark);
        ui.ShowMarkEditor(true);
        while (true)
        {
            spin();
            if (ClickedThisFrame() && Mouse.current != null)
            {
                Vector2 pos = Mouse.current.position.ReadValue();
                for (int i = 0; i < 9; i++)
                    if (ui.markCells[i].Contains(pos))
                    {
                        mark ^= 1 << i;
                        ui.SetMarkCells(mark);
                        sound.Clack(true);
                    }
                if (ui.markDoneButton.Contains(pos) && mark != 0) break;
            }
            yield return null;
        }
        workshop.mark = mark;
        SaveGame();
        ui.ShowMarkEditor(false);
        ui.addButton.Show(true);
        ui.discardButton.Show(true);
        ui.ShowMarkStamp(workshop.mark, workshop.MarkName);
        sound.Hammer();
        StartCoroutine(Say($"Stamped with your {workshop.MarkName} mark!", 1.4f));
    }

    private IEnumerator RunWorkbench()
    {
        CurrentStage = ForgeStage.Workbench;
        handleBroken = false;
        var w = Weapon;
        ui.instructions.text =
            $"Drag each part ({w.PartNames}) into its transparent outline and match it as closely as possible.\nMouse wheel or Q/E turns the part you are holding. The closer the fit, the better Balance and Durability - release to lock it in.\nDrop a part away from the {w.noun} and it loses ALL its Durability and Balance!";

        float surface = WorkbenchSurface;
        float shoulder = LowPoly.WeaponShoulder(item.weaponType);
        float bladeY = surface + 0.06f; // the handle parts are thicker than the blade, so its centre line sits a bit above the bench
        Vector3 wb = WorkbenchPos;

        // The weapon lies on the bench with the tip to the left and the bare tang pointing right. The sword keeps
        // its tuned spot; the others are centred by their full length with the handle on (a spear is long).
        Quaternion frame = workbenchStation.Frame;
        float bladeU = -0.25f;
        if (item.weaponType != WeaponType.Sword)
        {
            float tip = -LowPoly.WeaponPhase(item.weaponType, LowPoly.PhaseCount - 1).mesh.bounds.min.z;
            float end = shoulder + w.hilt[w.hilt.Length - 1].atX + 0.05f;
            bladeU = -(end - tip) * 0.5f - 0.05f;
        }
        PlaceWorkpiece(Centered(BenchPoint(wb, bladeU, 0.1f, bladeY), frame * Quaternion.Euler(0f, 90f, 0f)), frame * Quaternion.Euler(0f, 90f, 0f));
        BuildHandleParts(bladeU + shoulder, bladeY, wb);

        yield return GoTo(WorkbenchView(), 1.2f); // swings right and up over the bench

        ui.bar.Show(false);

        HandlePart dragging = null;
        Vector3 grabOffset = Vector3.zero;
        while (handleParts.Exists(p => !p.locked))
        {
            var mouse = Mouse.current;
            var kb = Keyboard.current;

            if (dragging == null)
            {
                if (ClickedThisFrame() && TryRaycastFromMouse(out RaycastHit hit))
                {
                    var part = handleParts.Find(p => !p.locked && p.go == hit.collider.gameObject);
                    if (part != null)
                    {
                        dragging = part;
                        sound.Pickup();
                        grabOffset = TryMousePlanePoint(bladeY, out Vector3 grabPoint)
                            ? part.go.transform.position - grabPoint
                            : Vector3.zero;
                        grabOffset.y = 0f;
                    }
                }
            }
            else if (mouse != null && mouse.leftButton.isPressed)
            {
                if (TryMousePlanePoint(bladeY, out Vector3 point))
                {
                    Vector3 pos = point + grabOffset;
                    pos.y = bladeY + 0.08f; // lifted while carried
                    dragging.go.transform.position = pos;
                }

                if (dragging.rotatable)
                {
                    float turn = 0f;
                    float wheel = mouse.scroll.ReadValue().y;
                    if (wheel != 0f) turn += Mathf.Sign(wheel) * workbenchStation.wheelStep;
                    if (kb != null && kb.qKey.isPressed) turn -= workbenchStation.keyTurnSpeed * Time.deltaTime;
                    if (kb != null && kb.eKey.isPressed) turn += workbenchStation.keyTurnSpeed * Time.deltaTime;
                    if (turn != 0f) dragging.go.transform.Rotate(0f, turn, 0f, Space.World);

                    // Close to straight (the piece looks the same turned 180 degrees): click it exactly into line.
                    float off = Mathf.DeltaAngle(dragging.targetYaw, dragging.go.transform.eulerAngles.y);
                    float nearest = Mathf.Abs(off) > 90f ? off - 180f * Mathf.Sign(off) : off;
                    if (Mathf.Abs(nearest) <= workbenchStation.straightenSnap)
                        dragging.go.transform.rotation = Quaternion.Euler(0f, dragging.targetYaw + (Mathf.Abs(off) > 90f ? 180f : 0f), 0f);
                }

                float error = MatchError(dragging, out float side);
                ShowGhostFeedback(dragging, error);
            }
            else
            {
                HandlePart released = dragging;
                dragging = null;
                yield return LockPart(released, bladeY);
            }

            yield return null;
        }

        AttachHandle();
        ui.bar.Show(false);
        Work(StageTime(WorkStage.Handle), "fitting the handle");
        yield return Say($"Handle assembled!  Balance {item.balance:0}   Durability {item.durability:0}", 1.5f);
    }

    // Guard, Grip and Pommel: solid pieces staged in front of the blade, each with a ghost at its place on the tang.
    // A point on the bench in its own frame: u along the sword (screen right), v across it (screen up).
    private Vector3 BenchPoint(Vector3 wb, float u, float v, float y)
    {
        Vector3 p = wb + workbenchStation.Frame * new Vector3(u, 0f, v);
        p.y = y;
        return p;
    }

    // The handle, piece by piece: each weapon type's own parts and how far past its shoulder each one sits
    // (WeaponInfo.hilt - the sword's Guard/Grip/Pommel, a spear's Shaft and Butt Cap...). Shared by the
    // workbench (where the player fits them) and FitFinishedHilt (a finished weapon brought in for repair).
    private HiltPart[] HiltParts => item.Weapon.hilt;

    private Vector3 HandleTarget(float atX, float y)
    {
        Vector3 p = workpieceTransform.position + workpieceTransform.rotation * new Vector3(0f, 0f, LowPoly.WeaponShoulder(item.weaponType) + atX);
        p.y = y;
        return p;
    }

    private void BuildHandleParts(float tangStartU, float y, Vector3 wb)
    {
        // Low-poly parts; the meshes are authored at real size (grip along X, guard along Z).
        var hilt = HiltParts;
        float[] stageX = hilt.Length >= 3
            ? new[] { -0.35f, 0.1f, 0.5f } // all on the planks - further left the guard ended up lying on the vise
            : new[] { -0.3f, 0.35f };
        Shader shader = workpieceRenderer.sharedMaterial.shader;

        for (int i = 0; i < hilt.Length; i++)
        {
            var s = hilt[i];
            var model = s.model();
            var part = new HandlePart
            {
                name = s.name,
                rotatable = s.rotatable,
                // On the weapon's own handle line (its local +Z, from the shoulder) - measured from where the metal
                // actually lies, not from the bench frame: a sickle is shifted sideways so its crescent sits on the
                // planks, and its handle has to follow the tang, not the bench centre line.
                targetPos = HandleTarget(s.atX, y),
                targetYaw = workbenchStation.transform.eulerAngles.y,
                baseScale = Vector3.one,
            };

            part.go = new GameObject("Handle" + s.name);
            model.Attach(part.go, shader);
            var box = part.go.AddComponent<BoxCollider>(); // grab area
            box.center = model.mesh.bounds.center;
            box.size = model.mesh.bounds.size + Vector3.one * 0.03f;
            float yaw = workbenchStation.transform.eulerAngles.y + (s.rotatable ? Random.Range(35f, 80f) * (Random.value < 0.5f ? -1f : 1f) : 0f);
            part.go.transform.SetPositionAndRotation(BenchPoint(wb, stageX[i], -0.55f, y), Quaternion.Euler(0f, yaw, 0f));

            part.ghost = new GameObject("Ghost" + s.name);
            part.ghost.AddComponent<MeshFilter>().sharedMesh = model.singleMesh;
            part.ghostMaterial = MakeGhostMaterial();
            part.ghost.AddComponent<MeshRenderer>().sharedMaterial = part.ghostMaterial;
            part.ghost.transform.SetPositionAndRotation(part.targetPos, workbenchStation.Frame);
            SetGhost(part, Color.white, 0.22f);

            handleParts.Add(part);
        }
    }

    // A finished sword (a customer's, in for repair) keeps its hilt: the three pieces are put straight onto the
    // tang exactly where a perfect fit at the workbench would leave them, and ride along with the blade through
    // the handle-follow in Update - the same mechanism the workbench uses once a handle is assembled.
    private void FitFinishedHilt()
    {
        ClearHandle();
        Shader shader = workpieceRenderer.sharedMaterial.shader;
        float shoulder = LowPoly.WeaponShoulder(item.weaponType);
        float farthest = 0f;
        foreach (var s in HiltParts)
        {
            var part = new HandlePart
            {
                name = s.name,
                rotatable = s.rotatable,
                locked = true,
                baseScale = Vector3.one,
                // Weapon space: the tang runs along +Z from the shoulder; the pieces are authored across it.
                localPos = new Vector3(0f, 0f, shoulder + s.atX),
                localRot = Quaternion.Euler(0f, -90f, 0f),
            };
            part.go = new GameObject("Handle" + s.name);
            s.model().Attach(part.go, shader);
            handleParts.Add(part);
            farthest = Mathf.Max(farthest, part.localPos.z + 0.06f);
        }
        handleCenterZ = SpinCenter(farthest);
        handleFollow = true;
    }

    // Where the finished weapon's middle is along its handle line (its handle's far end at `farthest`), so the
    // result screen spins it around its middle. The sword keeps its tuned blade-half reference.
    private float SpinCenter(float farthest)
    {
        float tip = item.weaponType == WeaponType.Sword
            ? LowPoly.BladeHalfLength
            : -LowPoly.WeaponPhase(item.weaponType, LowPoly.PhaseCount - 1).mesh.bounds.min.z;
        return (farthest - tip) * 0.5f;
    }

    // How far the piece is from its ghost. side is -1/+1 (left/right of the ghost) for the bar pointer.
    private float MatchError(HandlePart p, out float side)
    {
        Vector3 d = p.go.transform.position - p.targetPos;
        d.y = 0f;
        float angle = 0f, angleSigned = 0f;
        if (p.rotatable)
        {
            angleSigned = Mathf.DeltaAngle(p.targetYaw, p.go.transform.eulerAngles.y);
            float a = Mathf.Abs(angleSigned);
            angle = Mathf.Min(a, 180f - a); // guard and grip look the same turned 180 degrees
        }
        float across = (Quaternion.Inverse(workbenchStation.Frame) * d).x; // left/right of the ghost as the player sees it
        side = Mathf.Abs(across) > 0.005f ? Mathf.Sign(across) : (angleSigned != 0f ? Mathf.Sign(angleSigned) : 1f);
        return workbenchStation.Error(d.magnitude, angle);
    }

    private float PositionOffset(HandlePart p)
    {
        Vector3 d = p.go.transform.position - p.targetPos;
        d.y = 0f;
        return d.magnitude;
    }

    // The ghost fills in and glows as the piece gets closer: white -> green (good) -> cyan (perfect); red when it is too far to attach.
    private void ShowGhostFeedback(HandlePart p, float error)
    {
        if (PositionOffset(p) > workbenchStation.attachRadius)
        {
            SetGhost(p, new Color(0.95f, 0.25f, 0.2f), 0.45f);
            return;
        }
        Rating rating = workbenchStation.RatingFor(error);
        Color c = rating == Rating.Perfect ? new Color(0.35f, 0.95f, 1f) : rating == Rating.Good ? new Color(0.45f, 0.9f, 0.4f) : Color.white;
        SetGhost(p, c, Mathf.Lerp(0.8f, 0.2f, Mathf.Clamp01(error)));
    }

    private static void SetGhost(HandlePart p, Color color, float alpha)
    {
        color.a = alpha;
        p.ghostMaterial.color = color;
    }

    private IEnumerator LockPart(HandlePart p, float y)
    {
        float error = MatchError(p, out _);
        bool attached = PositionOffset(p) <= workbenchStation.attachRadius;
        Rating rating = attached ? workbenchStation.RatingFor(error) : Rating.Bad;
        string effect;
        if (!attached)
        {
            // Not fitted onto the tang at all - the sword is ruined.
            handleBroken = true;
            item.durability = 0f;
            item.balance = 0f;
            effect = "NOT ATTACHED - Durability and Balance lost!";
        }
        else if (handleBroken)
        {
            effect = "no effect, the sword is already ruined";
        }
        else
        {
            effect = workbenchStation.Apply(item, rating, error);
        }
        p.locked = true;
        Destroy(p.go.GetComponent<Collider>());
        sound.PlayRating(rating);
        sound.Clack(rating != Rating.Bad);

        Color flash = rating == Rating.Perfect ? new Color(0.35f, 0.95f, 1f) : rating == Rating.Good ? new Color(0.45f, 0.9f, 0.4f) : new Color(0.95f, 0.25f, 0.2f);
        SetGhost(p, flash, 0.85f);
        StartCoroutine(Say(attached ? $"{RatingLabel(rating)}  {p.name}: {effect}" : $"{p.name}: {effect}", 1.8f));

        // Settle onto the tang: a perfect fit clicks fully into place, a good one most of the way, a bad one stays where it was dropped.
        float pull = rating == Rating.Perfect ? 1f : rating == Rating.Good ? 0.6f : 0f;
        Vector3 startPos = p.go.transform.position;
        Quaternion startRot = p.go.transform.rotation;
        Vector3 endPos = Vector3.Lerp(startPos, p.targetPos, pull);
        endPos.y = y;
        Quaternion endRot = Quaternion.Slerp(startRot, Quaternion.Euler(0f, p.targetYaw, 0f), pull);

        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.2f)
        {
            float s = Mathf.SmoothStep(0f, 1f, t);
            p.go.transform.SetPositionAndRotation(Vector3.Lerp(startPos, endPos, s), Quaternion.Slerp(startRot, endRot, s));
            p.go.transform.localScale = p.baseScale * (1f + 0.25f * Mathf.Sin(t * Mathf.PI));
            yield return null;
        }
        p.go.transform.SetPositionAndRotation(endPos, endRot);
        p.go.transform.localScale = p.baseScale;

        StartCoroutine(FadeGhost(p, flash));
    }

    private IEnumerator FadeGhost(HandlePart p, Color color)
    {
        yield return new WaitForSeconds(0.25f);
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.4f)
        {
            if (p.ghost == null) yield break;
            SetGhost(p, color, Mathf.Lerp(0.85f, 0f, t));
            yield return null;
        }
        if (p.ghost != null) p.ghost.SetActive(false);
    }

    // Remember where every piece sits relative to the blade so the finished handle follows the sword afterwards.
    private void AttachHandle()
    {
        float farthest = 0f;
        Quaternion inv = Quaternion.Inverse(workpieceTransform.rotation);
        foreach (var p in handleParts)
        {
            p.localPos = inv * (p.go.transform.position - workpieceTransform.position);
            p.localRot = inv * p.go.transform.rotation;
            farthest = Mathf.Max(farthest, p.localPos.z + 0.06f);
        }
        handleCenterZ = SpinCenter(farthest);
        handleFollow = true;
    }

    private void ClearHandle()
    {
        handleFollow = false;
        foreach (var p in handleParts)
        {
            if (p.go != null) Destroy(p.go);
            if (p.ghost != null) Destroy(p.ghost);
        }
        handleParts.Clear();
    }

    private bool TryMousePlanePoint(float y, out Vector3 point)
    {
        point = Vector3.zero;
        var mouse = Mouse.current;
        if (mouse == null || cam == null) return false;
        var plane = new Plane(Vector3.up, new Vector3(0f, y, 0f));
        Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
        if (!plane.Raycast(ray, out float enter)) return false;
        point = ray.GetPoint(enter);
        return true;
    }

    // URP Lit set up as a see-through material for the ghost outlines (alpha comes from the colour).
    private Material MakeGhostMaterial()
    {
        var mat = new Material(workpieceRenderer.sharedMaterial.shader);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.SetShaderPassEnabled("ShadowCaster", false);
        return mat;
    }

    // ---------------------------------------------------------------- helpers

    private Vector3 ForgePos => forgeStation.transform.position;
    private Vector3 AnvilPos => anvilStation.transform.position;
    private Vector3 BarrelPos => quenchBarrel.transform.position;
    private Vector3 GrindPos => grindstone.transform.position;
    // The grindstone's pivot is the imported model's root, which sits below the wheel's middle - the blade and
    // the sparks belong on top of the actual stone, so they're anchored to the measured disc (see Start).
    private Vector3 grindWheelLocal;
    private Vector3 GrindWheelCenter => grindstone.transform.TransformPoint(grindWheelLocal);

    private void MeasureGrindWheel()
    {
        grindWheelLocal = Vector3.zero;
        Transform wheel = grindWheel != null ? grindWheel : grindstone.transform;
        Transform disc = wheel.Find("Cylinder"); // the stone itself in Grindstone.fbx (see Prototype1SceneBuilder.CreateGrindstoneModel)
        var r = (disc != null ? disc : wheel).GetComponentInChildren<Renderer>();
        if (r != null) grindWheelLocal = grindstone.transform.InverseTransformPoint(r.bounds.center);
    }
    private Vector3 WorkbenchPos => workbenchStation.transform.position;
    private float WorkbenchSurface => workbenchStation.GetComponent<Renderer>().bounds.max.y;

    // The anvil wrapper holds its imported model as a CHILD (see Prototype1SceneBuilder.CreateAnvilModel),
    // and that model's face is centred on its own pivot - so the anvil anchors follow the child's live X/Z,
    // which also picks up any hand-drag of the model in the Editor. (The furnace can't use this: its FBX pivot
    // is at a corner of the model - see MeasureFurnace.) Falls back to the wrapper's own position if the
    // named child isn't found (e.g. the procedural fallback model, which has no such child).
    private static Vector3 VisualCenter(Transform wrapper, string childName)
    {
        var child = wrapper.Find(childName);
        return child != null ? new Vector3(child.position.x, wrapper.position.y, child.position.z) : wrapper.position;
    }
    // The child's yaw only (no pitch/roll expected for a floor prop) - used to keep offsets tuned against
    // it (the furnace camera's framing) pointed the right way after it's turned to a new angle by hand.
    private static Quaternion VisualYaw(Transform wrapper, string childName)
    {
        var child = wrapper.Find(childName);
        return Quaternion.Euler(0f, (child != null ? child : wrapper).eulerAngles.y, 0f);
    }
    // The furnace FBX's own root pivot sits at a CORNER of the model (not under the fire), so a pivot-based
    // anchor put the embers, the chimney smoke and even the ingot on the furnace's side wall. Everything that
    // has to line up with the fire is anchored to the model's actual geometry instead, measured once in the
    // Forge wrapper's local frame (MeasureFurnace): the "Coal" meshes are the fire pit, the top of the model
    // is the chimney. TransformPoint puts them back in the world, so they follow the furnace however it's
    // moved or turned - and a hand-dragged model child is picked up the next time the game starts.
    private Bounds forgePitLocal = new Bounds(new Vector3(0f, 0.35f, 0f), new Vector3(0.6f, 0.7f, 0.6f));
    private Vector3 forgeChimneyLocal = new Vector3(0f, 2.4f, 0f);

    private void MeasureFurnace() => MeasureFurnaceModel(forgeStation.transform, ref forgePitLocal, ref forgeChimneyLocal);

    // Static so the main menu's background can light the very same furnace (ForgeEffects) without a game running.
    // Leaves pitLocal/chimneyLocal alone if the wrapper has no model at all.
    public static void MeasureFurnaceModel(Transform w, ref Bounds forgePitLocal, ref Vector3 forgeChimneyLocal)
    {
        bool anyCoal = false, any = false;
        Bounds pit = default, all = default;
        var parts = new List<(Renderer r, Bounds b)>();
        foreach (var r in w.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer) continue;
            Bounds local = LocalBounds(w, r.bounds);
            parts.Add((r, local));
            if (!any) { all = local; any = true; } else all.Encapsulate(local);
            bool coal = false;
            foreach (var m in r.sharedMaterials) if (m != null && m.name.StartsWith("Coal")) coal = true;
            if (!coal) continue;
            if (!anyCoal) { pit = local; anyCoal = true; } else pit.Encapsulate(local);
        }
        if (!any) return; // no model at all - keep the defaults
        forgePitLocal = anyCoal ? pit : new Bounds(new Vector3(all.center.x, all.min.y + 0.35f, all.center.z), new Vector3(all.size.x * 0.5f, 0.7f, all.size.z * 0.5f));

        // The chimney: whatever reaches the top 15% of the model's height.
        bool anyTop = false;
        Bounds top = default;
        float cut = all.min.y + all.size.y * 0.85f;
        foreach (var p in parts)
        {
            if (p.b.max.y < cut) continue;
            if (!anyTop) { top = p.b; anyTop = true; } else top.Encapsulate(p.b);
        }
        forgeChimneyLocal = anyTop ? new Vector3(top.center.x, all.max.y, top.center.z) : new Vector3(all.center.x, all.max.y, all.center.z);
    }

    // A world-space AABB re-expressed as an AABB in `space`'s local frame (all 8 corners transformed).
    private static Bounds LocalBounds(Transform space, Bounds world)
    {
        Vector3 min = world.min, max = world.max;
        Bounds b = new Bounds(space.InverseTransformPoint(min), Vector3.zero);
        for (int i = 1; i < 8; i++)
            b.Encapsulate(space.InverseTransformPoint(new Vector3((i & 1) != 0 ? max.x : min.x, (i & 2) != 0 ? max.y : min.y, (i & 4) != 0 ? max.z : min.z)));
        return b;
    }

    // Centre of the fire pit on the floor plane (y = the wrapper's floor), and the top of the coal bed.
    private Vector3 ForgeVisualPos
    {
        get
        {
            Vector3 p = forgeStation.transform.TransformPoint(forgePitLocal.center);
            return new Vector3(p.x, forgeStation.transform.position.y, p.z);
        }
    }
    private Quaternion ForgeVisualRot => VisualYaw(forgeStation.transform, "FurnaceModel");
    private Vector3 AnvilVisualPos => VisualCenter(anvilStation.transform, "AnvilModel");

    // right/up/forward offset in the model's OWN frame (as originally tuned, when the wrapper and its
    // child model were still aligned) rotated back into world space via its current actual yaw - so
    // embers/camera framing keep the same relative placement no matter how the model gets turned by hand.
    private static Vector3 LocalOffset(Quaternion rot, float right, float up, float forward)
        => rot * new Vector3(right, 0f, forward) + Vector3.up * up;

    private Pose FurnaceView() => LookPose(ForgeVisualPos + LocalOffset(ForgeVisualRot, -3.4f, 2.3f, -0.8f), new Vector3(ForgeVisualPos.x, ForgeTop - 0.3f, ForgeVisualPos.z));
    // The metal rests on top of the coal bed (see MeasureFurnace).
    private float ForgeTop => forgeStation.transform.TransformPoint(new Vector3(0f, forgePitLocal.max.y, 0f)).y;
    // AnvilStation.transform (AnvilPos) is the floor point under the model (see Prototype1SceneBuilder.
    // CreateAnvilModel) - the flat face plate sits about this high above that floor, centred on the pivot.
    private const float AnvilFaceHeight = 0.93f;
    // (The piece lies along world X on the anvil, Euler(0, 90, 0), centred across the face - see Centered.)
    private Vector3 AnvilWorkpiecePos(int phase)
        => Centered(new Vector3(AnvilVisualPos.x, AnvilPos.y + AnvilFaceHeight + LowPoly.PhaseHalfThickness(item.weaponType, phase) + 0.005f, AnvilVisualPos.z),
            Quaternion.Euler(0f, 90f, 0f), phase);

    // Every weapon's handle line is its local Z axis, but not every one is symmetric about it: a sickle's
    // crescent sweeps off to +X. Shifting it back sideways by its own mesh centre keeps the metal on the anvil
    // face / on the stone. Zero for the sword and the other straight weapons, so they sit exactly as before.
    private Vector3 Centered(Vector3 position, Quaternion rotation, int phase = -1)
    {
        float cx = LowPoly.WeaponPhase(item.weaponType, phase < 0 ? Mathf.Max(0, shownPhase) : phase).mesh.bounds.center.x;
        return position - rotation * new Vector3(cx, 0f, 0f);
    }

    // How far the bare metal reaches back along +Z (toward the handle) from the piece's origin - what still
    // pokes out of the water when it's dipped. The sword keeps its tuned constant.
    private float MetalBackReach => item.weaponType == WeaponType.Sword
        ? LowPoly.TangTipOffset
        : LowPoly.WeaponPhase(item.weaponType, LowPoly.PhaseCount - 1).mesh.bounds.max.z;
    private Pose AnvilView() => LookPose(AnvilVisualPos + new Vector3(0f, 2.9f, -0.8f), AnvilVisualPos + new Vector3(0f, 1.05f, 0f));
    private Pose BarrelView() => LookPose(BarrelPos + new Vector3(0.6f, 1.4f, -2.6f), BarrelPos + new Vector3(0f, 0.5f, 0f));
    // Seen from the front, the wheel face was originally turned 45 degrees towards the middle of the room -
    // grindstone.transform.rotation is read live (no wrapper/child split for this one, unlike furnace/anvil),
    // so the camera keeps the same relative framing however the stand gets turned by hand.
    private Pose GrindView() => LookPose(GrindPos + LocalOffset(grindstone.transform.rotation, 1.2021f, 1.3f, -2.4749f), GrindPos + new Vector3(0f, 0.45f, 0f));
    // Straight down over the bench, turned with it so the sword reads left-to-right on screen.
    public Vector3 WorkbenchCameraPosition => new Vector3(WorkbenchPos.x, WorkbenchSurface + 1.5f, WorkbenchPos.z);
    private Pose WorkbenchView() => new Pose(WorkbenchCameraPosition, Quaternion.LookRotation(Vector3.down, workbenchStation.Frame * Vector3.forward));

    private static Pose LookPose(Vector3 position, Vector3 lookAt)
        => new Pose(position, Quaternion.LookRotation(lookAt - position));

    // Camera returns to its starting pose first, then swings to the workplace.
    private IEnumerator GoTo(Pose station, float duration = 0.8f)
    {
        yield return MoveCamera(homePose);
        yield return MoveCamera(station, duration);
    }

    private IEnumerator MoveCamera(Pose target, float duration = 0.8f)
    {
        Transform ct = cam.transform;
        Vector3 p0 = ct.position;
        Quaternion r0 = ct.rotation;
        if ((p0 - target.position).sqrMagnitude < 0.0001f && Quaternion.Angle(r0, target.rotation) < 0.1f)
            yield break;

        sound.Whoosh();
        for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
        {
            float s = Mathf.SmoothStep(0f, 1f, t);
            ct.position = Vector3.Lerp(p0, target.position, s);
            ct.rotation = Quaternion.Slerp(r0, target.rotation, s);
            yield return null;
        }
        ct.position = target.position;
        ct.rotation = target.rotation;
    }

    private IEnumerator Fade(float target, float duration = 0.5f)
    {
        float from = fadeAlpha;
        for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
        {
            fadeAlpha = Mathf.Lerp(from, target, t);
            ui.SetFade(fadeAlpha);
            yield return null;
        }
        fadeAlpha = target;
        ui.SetFade(fadeAlpha);
    }

    private IEnumerator Say(string text, float seconds)
    {
        ui.message.text = text;
        yield return new WaitForSeconds(seconds);
        ui.message.text = "";
    }

    private IEnumerator HammerSwing(Vector3 target)
    {
        hammer.gameObject.SetActive(true);
        Vector3 high = target + new Vector3(0.2f, 0.55f, 0f);
        Vector3 low = target + new Vector3(0f, 0.1f, 0f);
        Quaternion raised = Quaternion.Euler(0f, 0f, 65f);
        Quaternion struck = Quaternion.Euler(0f, 0f, 8f);

        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.12f)
        {
            hammer.position = Vector3.Lerp(high, low, t);
            hammer.rotation = Quaternion.Slerp(raised, struck, t);
            yield return null;
        }
        punch = 1f;
        SetPhase(LowPoly.PhaseFor(item.shapeProgress)); // the metal takes its next shape when the hammer lands
        sound.Hammer();
        fx.HammerHit(workpieceTransform.position + Vector3.up * (LowPoly.PhaseHalfThickness(item.weaponType, shownPhase) + 0.02f));
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.18f)
        {
            hammer.position = Vector3.Lerp(low, high, t);
            hammer.rotation = Quaternion.Slerp(struck, raised, t);
            yield return null;
        }
        hammer.gameObject.SetActive(false);
    }

    // A pair of points on the item itself: one red, one yellow, kept apart so they're both clickable.
    // The spawn area follows the piece: small on the ingot, wider and longer as it turns into a sword.
    private void PickPointPair(out Vector3 durPos, out Vector3 balPos)
    {
        int phase = Mathf.Max(0, shownPhase);
        Vector3 center = workpieceTransform.position;
        center.y += LowPoly.PhaseHalfThickness(item.weaponType, phase) + 0.07f; // hovering just above the metal
        float halfX = LowPoly.PhaseHalfLength(phase);  // the piece lies along world X on the anvil
        float halfZ = LowPoly.PhaseHalfWidth(phase);
        if (item.weaponType != WeaponType.Sword && phase > 0)
        {
            // Other weapons: the area of the actual shape (a hammer head is short and wide, a sickle curls to one side).
            Bounds b = workpieceFilter.sharedMesh.bounds;
            Vector3 c = workpieceTransform.TransformPoint(new Vector3(b.center.x, 0f, b.center.z));
            center.x = c.x;
            center.z = c.z;
            halfX = Mathf.Max(0.08f, b.extents.z * 0.8f);
            halfZ = Mathf.Max(0.05f, b.extents.x * 0.8f);
        }
        float minApart = Mathf.Lerp(0.2f, 0.4f, phase / (float)(LowPoly.PhaseCount - 1)); // the markers are 0.22 wide

        durPos = center;
        balPos = center;
        for (int i = 0; i < 20; i++)
        {
            durPos = center + new Vector3(Random.Range(-halfX, halfX), 0f, Random.Range(-halfZ, halfZ));
            balPos = center + new Vector3(Random.Range(-halfX, halfX), 0f, Random.Range(-halfZ, halfZ));
            if ((durPos - balPos).sqrMagnitude > minApart * minApart) return;
        }
        durPos = center + new Vector3(-halfX * 0.6f, 0f, 0f);
        balPos = center + new Vector3(halfX * 0.6f, 0f, 0f);
    }

    private void PlaceWorkpiece(Vector3 position, Quaternion rotation)
    {
        workpieceTransform.SetPositionAndRotation(position, rotation);
    }

    private void PlaceSwordForDisplay(float angle)
    {
        Vector3 pos = cam.transform.position + cam.transform.forward * 2.2f;
        // The maker's-mark editor fills the right of the screen - keep the spinning hilt out from under it.
        if (ui.MarkEditorOpen) pos -= cam.transform.right * 0.35f;
        Quaternion rot = Quaternion.Euler(12f, angle, 0f);
        // With the handle on, spin the whole sword around its middle instead of around the blade centre.
        if (handleFollow) pos -= rot * (Vector3.forward * handleCenterZ * displayScale);
        workpieceTransform.SetPositionAndRotation(pos, rot);
    }

    // Shows the given forging phase (0 = raw ingot ... 4 = finished sword blade) and keeps it lying on the anvil.
    private void SetPhase(int phase)
    {
        if (phase == shownPhase && item.weaponType == shownType) return;
        shownPhase = phase;
        shownType = item.weaponType;
        workpieceFilter.sharedMesh = LowPoly.WeaponPhase(item.weaponType, phase).mesh;
        if (CurrentStage == ForgeStage.Anvil)
        {
            Vector3 p = AnvilWorkpiecePos(phase);
            workpieceTransform.position = p;
        }
    }

    private void ApplyMaterialVisual() => SetWorkpieceColor(material.color);

    private void SetWorkpieceColor(Color color) => workpieceRenderer.material.color = color;

    private void SetStationsVisible(bool visible)
    {
        foreach (var r in stationRenderers)
            if (r != null) r.enabled = visible;
        fx.SetVisible(visible); // no embers or smoke floating in the black result screen
        city?.SetVisible(visible); // ...nor townsfolk hanging in the sky behind the finished weapon
    }

    private static string RatingLabel(Rating rating)
    {
        switch (rating)
        {
            case Rating.Perfect: return "PERFECT!";
            case Rating.Good: return "Good.";
            default: return "Bad timing!";
        }
    }

    private static bool SpaceHeld() => Keyboard.current != null && Keyboard.current.spaceKey.isPressed;
    private static bool RightMouseHeld() => Mouse.current != null && Mouse.current.rightButton.isPressed;

    private static bool EnterPressed()
    {
        var kb = Keyboard.current;
        return kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame);
    }

    private static bool ClickedThisFrame() => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;

    private static bool GivePressed() => Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame;

    // Raycasts from the mouse cursor through Camera.main into the 3D scene.
    private bool TryRaycastFromMouse(out RaycastHit hit)
    {
        hit = default;
        var mouse = Mouse.current;
        if (mouse == null || cam == null) return false;

        Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
        return Physics.Raycast(ray, out hit, 100f);
    }

    private HitZoneMarker CreateMarker(string name, PointKind kind, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.localScale = Vector3.one * 0.22f;
        go.GetComponent<SphereCollider>().radius = 0.8f; // generous click area
        go.GetComponent<MeshFilter>().sharedMesh = LowPoly.Gem().mesh; // faceted gem instead of the smooth sphere
        go.GetComponent<Renderer>().material = MakeMaterial(color);
        var marker = go.AddComponent<HitZoneMarker>();
        marker.kind = kind;
        go.SetActive(false);
        return marker;
    }

    private Transform CreateHammer()
    {
        // Visual only (no collider) - must never block the mouse ray that picks the points.
        var root = new GameObject("Hammer");
        LowPoly.Hammer().Attach(root, workpieceRenderer.sharedMaterial.shader);

        root.SetActive(false);
        return root.transform;
    }

    // The one customer model, reused for every visit (walked in and back out by RunCustomerVisit).
    private Transform CreateCustomer()
    {
        var go = new GameObject("Customer");
        // The same animated body as the townsfolk (NpcBody), re-dressed for whoever visits.
        customerBody = NpcBody.Create(go.transform, NpcAppearance.Generate(Race.Human, CityRole.Townsfolk, 1));
        customerBody.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // close to the camera, the one who matters

        // The sword at their hip (same belt spot as the city's passers-by), shown by ShowCustomerSword.
        customerSword = new GameObject("CustomerSword");
        LowPoly.DisplaySword().Attach(customerSword, workpieceRenderer.sharedMaterial.shader);
        customerSwordBlade = customerSword.GetComponent<Renderer>().sharedMaterials[0];
        customerWeaponType = WeaponType.Sword;
        customerSword.transform.SetParent(customerBody.BobBone, false); // rides the body's step
        Weapons.WearOn(customerSword.transform, WeaponType.Sword, customerBody.Proportions);
        customerSword.SetActive(false);

        go.SetActive(false);
        return go.transform;
    }

    // Where a hanging sword's guard rests: on top of its peg's forked prongs (see LowPoly.StorageRack).
    private static Vector3 RackGuardPoint(Transform peg) => peg.position + peg.up * LowPoly.RackGuardRestY;

    // One real 3D sword per rack peg (see LowPoly.DisplaySword), all sharing one mesh but each with its own
    // material instance (Attach() always makes fresh ones) so its blade can be tinted per slot in
    // UpdateStorageUI. Hidden until UpdateStorageUI turns one on for an occupied slot. Only the rack turns the
    // model upside down - the same mesh is worn tip-down at the hip by customers with their own rotation.
    private void CreateSwordDisplays()
    {
        int count = storageDisplay.swordPegs.Length;
        swordDisplays = new GameObject[count];
        swordDisplayBladeMats = new Material[count];
        displayTypes = new WeaponType?[count];
        displayLean = new float[count];
        var rng = new System.Random(7); // fixed seed: a stable handmade lean, not a different one every run

        for (int i = 0; i < count; i++)
        {
            swordDisplays[i] = new GameObject("DisplaySword" + i);
            displayLean[i] = ((float)rng.NextDouble() - 0.5f) * 5f;
            HangDisplay(i, WeaponType.Sword);
            swordDisplays[i].SetActive(false);
        }
    }

    private float RackTop => storageDisplay.TryGetComponent(out Renderer r) ? r.bounds.max.y : storageDisplay.transform.position.y + 2f;

    private WeaponType?[] displayTypes; // which weapon model each peg's display object carries right now
    private float[] displayLean;

    // Hangs slot i's display as the given kind of weapon, swapping its model only when the kind changes.
    // Upside down on the peg, swinging a touch: a sword rests on its guard (hilt above the peg, blade below);
    // everything else hangs by a loop at the very end of its handle. Weapons don't share one size or pivot, so a
    // long one (a spear) is shrunk just enough to clear the shelf under its row - the sword is never touched.
    private void HangDisplay(int i, WeaponType type)
    {
        var go = swordDisplays[i];
        if (displayTypes[i] != type)
        {
            Weapons.DisplayModel(type).Attach(go, workpieceRenderer.sharedMaterial.shader);
            swordDisplayBladeMats[i] = go.GetComponent<Renderer>().sharedMaterials[0]; // submesh 0 = the metal paint
            displayTypes[i] = type;
        }
        Transform peg = storageDisplay.swordPegs[i];
        Weapons.RackPose(type, out bool tipDown, out Vector3 rest);
        Weapons.RackExtent(type, out float below, out float above);
        Quaternion rot = peg.rotation * Quaternion.Euler(0f, 0f, (tipDown ? 180f : 0f) + displayLean[i]);
        Vector3 hang = RackGuardPoint(peg);
        float roomBelow = hang.y - storageDisplay.transform.position.y - 0.36f;   // down to just above the bin shelf
        float roomAbove = RackTop - hang.y - 0.03f;                             // up to just under the rack's top beam
        float scale = type == WeaponType.Sword ? 1f : Mathf.Min(1f, roomBelow / below, roomAbove / above);
        go.transform.localScale = Vector3.one * scale;
        go.transform.SetPositionAndRotation(hang - rot * (rest * scale), rot);
    }

    // Reuses the workpiece's shader so this works with whichever render pipeline the project uses (URP here).
    private Material MakeMaterial(Color color)
    {
        var mat = new Material(workpieceRenderer.sharedMaterial.shader);
        mat.color = color;
        return mat;
    }

    private string GetStats()
    {
        // Main Room: the order paper and storage card cover this. Result: the result screen lists the same stats.
        if (CurrentStage == ForgeStage.MainRoom || CurrentStage == ForgeStage.Result) return "";

        string text = $"{Weapon.name}  -  Metal: {material.name}\n" +
                      $"Durability: {item.durability:0}    Balance: {item.balance:0}    Sharpness: {Weapon.SharpnessText(item.sharpness)}";

        if (CurrentStage == ForgeStage.Anvil)
            text += $"\nHits left: {anvilStation.HitsLeft}";
        else if (CurrentStage == ForgeStage.Grindstone)
            text += $"\nTouches: {grindstone.Touches}/{grindstone.maxTouches}";
        else if (CurrentStage == ForgeStage.Workbench)
            text += $"\nParts fitted: {handleParts.FindAll(p => p.locked).Count}/{handleParts.Count}";

        return text + $"\nInventory: {inventory.Count}";
    }
}
