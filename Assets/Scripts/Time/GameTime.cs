using UnityEngine;

// THE game clock - the one authority on what day and time it is. Everything else reads it: the sky and lights
// (DayCycle), the townsfolk (CityLife), the ambience (GameAudio), customers and order deadlines (ForgeGameManager),
// the supplier's hours, the end of the day. Nothing else keeps a timer of its own.
//
// Time moves because the smith WORKS: every finished piece of work (a furnace heat, the anvil, the quench, the
// handle, the grindstone, the final finish, a repair, talking an order through) adds its in-game minutes when it is
// done (Spend) - however fast or slow the player was at the minigame. Besides that a very slow passive clock runs,
// but only while the smith is idle in the workshop looking around (PassiveAllowed, set by ForgeGameManager): never in
// the journal, the ledger, a dialogue, the storage menus or the main menu, so reading never costs the day.
//
// The state itself (day, hour, visits today, today's tally) is saved data and lives in Workshop; this is the only
// code that changes it. VisualHour eases towards Hour, so a jump of the clock after work turns into a smooth change of
// light rather than a snap.
public class GameTime : MonoBehaviour
{
    public static GameTime Instance { get; private set; }
    public static TimeSettings Settings => TimeSettings.Current;

    private Workshop shop;
    public int Day => shop.day;
    public float Hour => shop.hour;
    public float VisualHour { get; private set; }
    public bool PassiveAllowed { get; set; }

    // The last piece of work, for the little "+25 min at the furnace" note under the clock.
    public string LastWork { get; private set; }
    public float LastWorkMinutes { get; private set; }
    public float LastWorkRealTime { get; private set; } = -100f;

    private float visualSpeed; // > 0 while the morning glide sets its own pace
    private double passiveCarry;                     // idle time not yet added to the clock (hours)
    private const double PassiveStep = 1.0 / 360.0;  // added ten game seconds at a time

    public static GameTime Create(Workshop shop)
    {
        var t = new GameObject("GameTime").AddComponent<GameTime>();
        t.shop = shop;
        if (shop.hour <= 0f) { shop.hour = Settings.dayStart; shop.StartDay(); } // a brand-new game: day 1 begins now
        t.VisualHour = shop.hour;
        Instance = t;
        return t;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    // ---- changing the time (the only places it happens) -------------------------------------------------------

    // Work done: the clock moves on by its minutes (never past the latest hour). Returns the minutes that passed.
    public float Spend(float minutes, string what)
    {
        float before = shop.hour;
        shop.hour = Mathf.Min(Settings.latestHour, shop.hour + Mathf.Max(0f, minutes) / 60f);
        float spent = (shop.hour - before) * 60f;
        shop.today.minutesWorked += spent;
        LastWork = what;
        LastWorkMinutes = minutes;
        LastWorkRealTime = Time.unscaledTime;
        return spent;
    }

    // Straight to a time (loading, tests, debugging) - the sky follows at once.
    public void SetHour(float hour)
    {
        shop.hour = Mathf.Clamp(hour, 0f, Settings.latestHour);
        VisualHour = shop.hour;
        visualSpeed = 0f;
    }

    // The night passes: a new day number, the working morning - and the sky brightening into it from dawn.
    public void BeginNewDay()
    {
        shop.day++;
        shop.hour = Settings.dayStart;
        shop.visitsToday = 0;
        shop.StartDay();
        VisualHour = Settings.dawnStart;
        visualSpeed = (Settings.dayStart - Settings.dawnStart) / Mathf.Max(0.5f, Settings.morningGlideSeconds);
    }

    private void Update()
    {
        var s = Settings;
        float dt = Time.deltaTime;
        // Collected on the side and added in whole steps: a single frame's worth is far below what a float hour
        // near 8.0 can even represent, and would simply be lost.
        if (PassiveAllowed && shop.hour < s.latestHour)
        {
            passiveCarry += dt * s.passiveMinutesPerRealMinute / 3600.0;
            if (passiveCarry >= PassiveStep)
            {
                shop.hour = Mathf.Min(s.latestHour, (float)(shop.hour + passiveCarry));
                passiveCarry = 0.0;
            }
        }
        float gap = shop.hour - VisualHour;
        float speed = visualSpeed > 0f ? visualSpeed : Mathf.Max(s.visualCatchUpHoursPerSecond * Mathf.Clamp01(Mathf.Abs(gap) * 2f), 0.02f);
        VisualHour = Mathf.MoveTowards(VisualHour, shop.hour, speed * dt);
        if (Mathf.Abs(shop.hour - VisualHour) < 1e-4f) visualSpeed = 0f;
    }

    // ---- reading it ------------------------------------------------------------------------------------------

    public bool IsNight => Hour >= Settings.nightStart || Hour < Settings.dawnStart;
    public bool IsEvening => Hour >= Settings.eveningStart && Hour < Settings.nightStart;
    public bool IsCustomerHours => Hour >= Settings.customerStart && Hour < Settings.customerEnd;
    public bool ReturnVisitHours => Hour >= Settings.customerStart && Hour < Settings.returnVisitsEnd;
    public bool CanEndDayFreely => Hour >= Settings.endDayAvailable;
    public bool SupplierOpen => Hour < Settings.supplierCutoff;
    public bool CanStartWeapon => Hour < Settings.newWeaponCutoff;
    public bool CanStartRepair => Hour < Settings.repairCutoff;
    public string Phase => PhaseAt(VisualHour);
    public string ClockText => Clock(Hour);

    public static string PhaseAt(float h)
    {
        var s = Settings;
        if (h < s.dawnStart || h >= s.nightStart) return "Night";
        if (h < s.dayStart) return "Dawn";
        if (h < 12f) return "Morning";
        if (h < s.eveningStart) return "Afternoon";
        return "Evening";
    }

    // "14:35" (to the nearest five minutes - it's a workshop, not a stopwatch).
    public static string Clock(float hour)
    {
        int minutes = Mathf.FloorToInt(hour * 60f + 0.01f) / 5 * 5;
        return $"{minutes / 60 % 24:00}:{minutes % 60:00}";
    }

    // "2h 30m", "45m", "1h"
    public static string Duration(float minutes)
    {
        int m = Mathf.RoundToInt(minutes / 5f) * 5;
        if (m < 60) return $"{m}m";
        return m % 60 == 0 ? $"{m / 60}h" : $"{m / 60}h {m % 60:00}m";
    }

    // ---- how long work takes (settings x the workshop's upgrades) ---------------------------------------------

    public static float StageMinutes(Workshop shop, WeaponType type, WorkStage stage)
    {
        var s = Settings;
        float minutes = s.WorkFor(type).Stage(stage);
        int level = stage == WorkStage.Furnace ? shop.furnaceLevel : stage == WorkStage.Grind ? shop.grindstoneLevel : shop.toolsLevel;
        return minutes * s.UpgradeScale(level);
    }

    public static float WeaponMinutes(Workshop shop, WeaponType type)
    {
        float total = 0f;
        foreach (WorkStage st in System.Enum.GetValues(typeof(WorkStage)))
        {
            if (st == WorkStage.Grind && !Weapons.Get(type).hasEdge) continue;
            total += StageMinutes(shop, type, st);
        }
        return total;
    }

    public static float RepairMinutes(Workshop shop, RepairKind kind)
    {
        var s = Settings;
        float m = kind == RepairKind.Sharpening ? s.repairSharpen : kind == RepairKind.Handle ? s.repairHandle
                : kind == RepairKind.BentBlade ? s.repairBentBlade : kind == RepairKind.Moderate ? s.repairModerate : s.repairMajor;
        return shop == null ? m : m * s.UpgradeScale(kind == RepairKind.Sharpening ? shop.grindstoneLevel : shop.toolsLevel);
    }

    // What a worn weapon needs, from what happened to it: a dull edge is a sharpening, a dented edgeless head and
    // loose handle a handle job, a bent blade takes longer, and the more worn it is the bigger the job.
    public static RepairKind RepairKindFor(SwordRecord r)
    {
        var s = r.sword;
        if (r.condition < 12f) return RepairKind.Major;
        if (!s.Weapon.hasEdge) return RepairKind.Handle;
        if (s.durability < SwordStories.Low) return RepairKind.BentBlade;
        if (r.condition >= 30f) return RepairKind.Sharpening;
        return RepairKind.Moderate;
    }

    public static string RepairName(RepairKind k) =>
        k == RepairKind.Sharpening ? "sharpening" : k == RepairKind.Handle ? "handle and head repair" : k == RepairKind.BentBlade ? "straightening a bent blade"
        : k == RepairKind.Moderate ? "a proper repair" : "a major repair";

    // ---- customers through the day ------------------------------------------------------------------------------
    // Each day has its own few arrival times for customers with new work, spread over the customer hours (one per
    // slot, randomly placed in it), the same for a given save and day - so they aren't stored, only how many came.

    // delta: today's city event (a festival brings one more, heavy rain one fewer - WorldDirector.ArrivalsDelta).
    public static float[] Arrivals(int day, int seed, int delta = 0)
    {
        var s = Settings;
        var rng = new System.Random(unchecked(day * 7919 + seed * 104729 + 13));
        int count = Mathf.Max(1, rng.Next(s.newCustomersMin, s.newCustomersMax + 1) + delta);
        float start = s.firstArrivalHour, end = s.customerEnd - 0.25f;
        float slot = (end - start) / Mathf.Max(1, count);
        var times = new float[count];
        for (int i = 0; i < count; i++)
            times[i] = start + slot * i + (float)rng.NextDouble() * slot * (i == 0 ? 0.35f : 0.6f);
        return times;
    }

    public float[] ArrivalsToday => Arrivals(shop.day, shop.timeSeed, WorldDirector.ArrivalsDelta(shop));

    // The next customer with new work, if their time has come (or nearly, if the smith has been idle a while with
    // nobody around). Never outside customer hours.
    public bool NewCustomerDue(float idleSeconds)
    {
        if (!IsCustomerHours) return false;
        var times = ArrivalsToday;
        if (shop.visitsToday >= times.Length) return false;
        float next = times[shop.visitsToday];
        return Hour >= next || (idleSeconds >= Settings.earlyArrivalIdleSeconds && next - Hour <= Settings.earlyArrivalHours);
    }

    public int NewCustomersLeftToday => Mathf.Max(0, ArrivalsToday.Length - shop.visitsToday);

    public void ArrivalUsed() => shop.visitsToday++;

    // ---- order deadlines --------------------------------------------------------------------------------------

    public static bool IsOverdue(CustomerOrder o, int day, float hour) =>
        day > o.deadlineDay || (day == o.deadlineDay && hour >= o.DeadlineHour);

    // When a customer comes back on a given day: sometime in the working day - and on their last day, by the deadline.
    public static float ReturnHourFor(CustomerOrder o, int onDay)
    {
        var s = Settings;
        // On the due day itself they come towards the deadline - the smith gets the whole day for the work.
        if (onDay >= o.deadlineDay) return o.DeadlineHour - Random.Range(0.5f, 1.5f);
        return Random.Range(s.returnVisitHours.x, s.returnVisitHours.y);
    }

    public static string DueText(CustomerOrder o) => $"Day {o.deadlineDay}, {Clock(o.DeadlineHour)}";
}
