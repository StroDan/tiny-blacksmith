using UnityEngine;

// Every number the game's clock runs on, in one place: the shape of a day, how long each piece of work takes, when
// customers come, what upgrades do. GameTime reads this and nothing else hardcodes a time.
//
// The asset lives at Assets/Resources/TimeSettings.asset (Tiny Blacksmith > Create Time Settings Asset makes it from
// the defaults below); without it the defaults are used as they are. Hours are clock hours (14.5 = 14:30); work
// durations are in-game minutes.
[CreateAssetMenu(menuName = "Tiny Blacksmith/Time Settings", fileName = "TimeSettings")]
public class TimeSettings : ScriptableObject
{
    [Header("The shape of a day (clock hours)")]
    [Tooltip("The sky starts to lighten - where the fade after End Day wakes the smith before the morning brightens.")]
    public float dawnStart = 6f;
    [Tooltip("A new day's work starts here.")]
    public float dayStart = 8f;
    public float customerStart = 8f;
    [Tooltip("No new customers with new work after this (people already at the table finish their business).")]
    public float customerEnd = 17.5f;
    [Tooltip("Customers coming back for their orders still turn up until this - collecting is an evening job too.")]
    public float returnVisitsEnd = 19.5f;
    public float eveningStart = 18f;
    public float nightStart = 21f;
    [Tooltip("From here the house door simply ends the day (before it, it asks first).")]
    public float endDayAvailable = 18f;
    [Tooltip("The supplier takes material orders until this hour.")]
    public float supplierCutoff = 20f;
    [Tooltip("No NEW weapon can be started after this (a weapon already begun is always finished).")]
    public float newWeaponCutoff = 20.5f;
    [Tooltip("Repairs are shorter - they can still be begun until this hour.")]
    public float repairCutoff = 22f;
    [Tooltip("The clock never goes past this: work that would run later is simply finished by then.")]
    public float latestHour = 23.75f;

    [Header("Passive time (only while the smith is idle in the workshop, never in menus, the journal or dialogue)")]
    [Tooltip("In-game minutes per real minute of just looking around. Work is what moves the day on.")]
    public float passiveMinutesPerRealMinute = 4f;
    [Tooltip("How fast the sky and the city catch up with a jump of the clock (clock hours per real second at most).")]
    public float visualCatchUpHoursPerSecond = 0.9f;
    [Tooltip("Real seconds the sky takes to brighten from dawn into the working morning after End Day.")]
    public float morningGlideSeconds = 8f;

    [Header("Customers")]
    public int newCustomersMin = 3;
    public int newCustomersMax = 4;
    [Tooltip("Arrivals are spread over the customer hours, one per slot, each randomly placed in its slot.")]
    public float firstArrivalHour = 8.25f;
    [Tooltip("If the smith has been idle this long (real seconds) with nobody around, the next customer may come early...")]
    public float earlyArrivalIdleSeconds = 40f;
    [Tooltip("...by at most this many clock hours.")]
    public float earlyArrivalHours = 1f;
    [Tooltip("Real seconds between one visitor leaving and the next walking up, even when several are already due.")]
    public Vector2 visitGapSeconds = new Vector2(10f, 22f);
    [Tooltip("When an order's customer comes back on their day (clock hours).")]
    public Vector2 returnVisitHours = new Vector2(9f, 16.5f);
    [Tooltip("Orders are due by this hour on their deadline day.")]
    public float deadlineHour = 18f;
    [Range(0f, 1f)] public float rushOrderChance = 0.12f;
    public float rushPayMultiplier = 1.35f;
    [Tooltip("A bulk contract asks for this many weapons...")]
    public Vector2Int contractSize = new Vector2Int(3, 5);
    [Tooltip("...and allows about this many of them per working day (other work goes on too).")]
    public float contractWeaponsPerDay = 2f;

    [Header("Talking takes a little time too (in-game minutes)")]
    public float orderTalkMinutes = 15f;   // hearing an order out and agreeing on it
    public float declineTalkMinutes = 5f;
    public float handOverMinutes = 10f;    // a customer collecting their order
    public float repairTakeInMinutes = 10f;
    public float traderMinutes = 10f;      // the Collector, the Event Trader

    [Header("Forging, per weapon (in-game minutes per stage)")]
    public WeaponWork[] weapons =
    {
        //                      furnace anvil quench handle grind finish           total
        new WeaponWork(WeaponType.Dagger, 16, 30, 14, 17, 18,  5), // 100 = 1h40
        new WeaponWork(WeaponType.Sickle, 19, 36, 15, 18, 22,  5), // 115 = 1h55
        new WeaponWork(WeaponType.Hammer, 27, 55, 20, 25,  0,  8), // 135 = 2h15 (no edge to grind: a heavy head instead)
        new WeaponWork(WeaponType.Sword,  24, 44, 20, 25, 28,  9), // 150 = 2h30
        new WeaponWork(WeaponType.Spear,  23, 42, 18, 33, 26,  8), // 150 = 2h30 (the shaft takes fitting)
    };

    [Header("Repairs (in-game minutes)")]
    public float repairSharpen = 25f;
    public float repairHandle = 40f;
    public float repairBentBlade = 50f;
    public float repairModerate = 60f;
    public float repairMajor = 80f;

    [Header("Workshop upgrades: work-time multiplier per level (level 0 = the starting workshop)")]
    [Tooltip("Diminishing returns on purpose: upgrades help, they never bring back dozens of weapons a day.")]
    public float[] upgradeTimeScale = { 1f, 0.95f, 0.9f, 0.87f };

    public float UpgradeScale(int level) => upgradeTimeScale == null || upgradeTimeScale.Length == 0
        ? 1f : upgradeTimeScale[Mathf.Clamp(level, 0, upgradeTimeScale.Length - 1)];

    public WeaponWork WorkFor(WeaponType type)
    {
        foreach (var w in weapons) if (w.weapon == type) return w;
        return weapons.Length > 0 ? weapons[0] : new WeaponWork(type, 20, 40, 15, 20, 25, 5);
    }

    // ---- the one instance everything reads ----------------------------------------------------------------
    private static TimeSettings current;
    public static TimeSettings Current
    {
        get
        {
            if (current == null) current = Resources.Load<TimeSettings>("TimeSettings");
            if (current == null) { current = CreateInstance<TimeSettings>(); current.name = "TimeSettings (defaults)"; }
            return current;
        }
    }
}

public enum WorkStage { Furnace, Anvil, Quench, Handle, Grind, Finish }

public enum RepairKind { Sharpening, Handle, BentBlade, Moderate, Major }

[System.Serializable]
public class WeaponWork
{
    public WeaponType weapon;
    public float furnace, anvil, quench, handle, grind, finish;

    public WeaponWork(WeaponType weapon, float furnace, float anvil, float quench, float handle, float grind, float finish)
    {
        this.weapon = weapon; this.furnace = furnace; this.anvil = anvil; this.quench = quench;
        this.handle = handle; this.grind = grind; this.finish = finish;
    }

    public float Stage(WorkStage s)
    {
        switch (s)
        {
            case WorkStage.Furnace: return furnace;
            case WorkStage.Anvil: return anvil;
            case WorkStage.Quench: return quench;
            case WorkStage.Handle: return handle;
            case WorkStage.Grind: return grind;
            default: return finish;
        }
    }

    public float Total => furnace + anvil + quench + handle + grind + finish;
}
