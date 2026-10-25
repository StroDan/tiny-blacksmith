using System;
using System.Collections.Generic;
using UnityEngine;

// The living world's memory for one save (owned by Workshop, saved by SaveSystem): its seed, what the smith has already
// seen, what's been asked for lately, how each folk thinks of the smith, the city's events past and to come, word of mouth
// on its way, and the one offer standing at the table right now. Everything the WorldDirector decides that must NOT
// change on a reload is written here (or into the records it creates) the moment it's decided.
[Serializable]
public class WorldState
{
    public int worldSeed;                       // this save's own sequence (never shown; 0 = not set up yet)

    // ---- progression windows (rolled once from the seed at the start of a game) ----
    public int[] raceGuaranteeDay = new int[3]; // by this day each race has shown up (0 = no guarantee: humans)
    public int[] raceFirstSeen = new int[3];    // the day each race first came to the table (0 = not yet)
    public int[] weaponIntroDay = new int[5];   // from this day each weapon type can be asked for
    public int[] weaponFirstAsked = new int[5]; // the day it was first asked for (0 = not yet)
    public int firstEventTraderDay;             // the Event Trader's first visit is guaranteed by this day

    // ---- recent history (for the repetition penalty) ----
    public List<int> recentWeapons = new List<int>();
    public List<int> recentRaces = new List<int>();
    public List<int> recentTrades = new List<int>();
    public List<int> recentStats = new List<int>();
    public int ordersGenerated;

    // ---- the smith's name among each folk (-1..1) and among the watch ----
    public float[] raceStanding = new float[3];
    public int guardFavour;                     // excellent weapons delivered to the watch/soldiery

    // ---- city events ----
    public List<CityEventRecord> events = new List<CityEventRecord>(); // past, running and announced ones (by start day)
    public int eventsPlannedThrough;            // the last day PlanDay ran for

    // ---- word of mouth ----
    public List<Referral> referrals = new List<Referral>();

    // ---- special commissions ----
    public int lastSpecialDay = -100;
    public int specialsOffered;
    public int specialKindsDone;                // bit per SpecialKind already met (the first few are all different)

    // ---- the Event Trader, metals ----
    public int traderVisits;                    // times the Event Trader has come (0 = the first visit is still ahead)
    public int lastTraderVisitDay;
    public int lastChallengeWeaponPlusOne;      // the weapon her last challenge asked for, +1
    public int[] metalUnlockDay = new int[3];   // the day each metal was learned (0 = not yet / unknown)

    // ---- rolls taken today (the salt that keeps each visit's rolls fixed across reloads) ----
    public int visitRolls;

    // ---- the customer at the table ----
    public bool hasStandingOffer;
    public CustomerOrder standingOffer;         // an offer nobody has answered yet - it waits for the smith after a reload

    // ---- debug overrides (never saved into a real decision: they only steer the next roll) ----
    [NonSerialized] public Race? forceNextRace;
    [NonSerialized] public bool forceSpecial;

    public CityEventRecord EventOn(int day) => events.Find(e => e.startDay <= day && day <= e.endDay);
    public CityEventType TodayEvent(int day) { var e = EventOn(day); return e != null ? e.type : CityEventType.None; }
    public CityEventRecord LastEventBefore(int day) { CityEventRecord best = null; foreach (var e in events) if (e.endDay < day && (best == null || e.endDay > best.endDay)) best = e; return best; }
    public CityEventRecord Upcoming(int day) => events.Find(e => e.startDay > day);

    public void AfterLoad()
    {
        if (raceGuaranteeDay == null || raceGuaranteeDay.Length < 3) raceGuaranteeDay = new int[3];
        if (raceFirstSeen == null || raceFirstSeen.Length < 3) raceFirstSeen = new int[3];
        if (weaponIntroDay == null || weaponIntroDay.Length < 5) weaponIntroDay = new int[5];
        if (weaponFirstAsked == null || weaponFirstAsked.Length < 5) weaponFirstAsked = new int[5];
        if (raceStanding == null || raceStanding.Length < 3) raceStanding = new float[3];
        if (metalUnlockDay == null || metalUnlockDay.Length < ForgeMaterials.All.Length) metalUnlockDay = new int[ForgeMaterials.All.Length];
        recentWeapons ??= new List<int>(); recentRaces ??= new List<int>(); recentTrades ??= new List<int>(); recentStats ??= new List<int>();
        events ??= new List<CityEventRecord>();
        referrals ??= new List<Referral>();
        if (!hasStandingOffer || standingOffer == null || string.IsNullOrEmpty(standingOffer.customerName)) { hasStandingOffer = false; standingOffer = null; }
        else standingOffer.AfterLoad();
    }
}

[Serializable]
public class CityEventRecord
{
    public CityEventType type;
    public int startDay, endDay;
    public int announcedDay;   // the day it was heralded (0 = came as a surprise)
    public CityEventInfo Info => CityEvents.Get(type);
}

// Somebody talked about the smith: a satisfied customer sends a friend (positive), or a let-down one grumbles (negative).
[Serializable]
public class Referral
{
    public int day;            // from this day the new customer may come
    public int fromCustomerId;
    public int swordId;        // the weapon they talk about
    public bool positive;
    public Race race;          // who's likely to come (the referrer's folk, mostly)
    public WeaponType weapon;  // what they'll likely want - similar, not identical
    public bool used;
}

// Seeded randomness that doesn't change when a game is reloaded: every roll is derived from the save's seed plus WHAT
// is being decided (a stream name) and WHEN (day, and a salt such as the visit number). Two saves diverge; one save
// replays the same way however often it's loaded.
public static class WorldRandom
{
    public static int Hash(int seed, string stream, int day, int salt = 0)
    {
        unchecked
        {
            int h = (int)2166136261;
            void Mix(int v) { h = (h ^ v) * 16777619; h ^= h >> 13; }
            Mix(seed); Mix(day); Mix(salt);
            foreach (char c in stream ?? "") Mix(c);
            return h & 0x7fffffff;
        }
    }

    public static System.Random For(int seed, string stream, int day, int salt = 0) => new System.Random(Hash(seed, stream, day, salt));

    // For older code that draws from UnityEngine.Random (order generation, NPC building): seeds it for the duration of a
    // using-block and puts the previous state back afterwards, so nothing else's randomness shifts.
    public static Scope Seeded(int seed, string stream, int day, int salt = 0) => new Scope(Hash(seed, stream, day, salt));

    public readonly struct Scope : IDisposable
    {
        private readonly UnityEngine.Random.State saved;
        public Scope(int state) { saved = UnityEngine.Random.state; UnityEngine.Random.InitState(state); }
        public void Dispose() => UnityEngine.Random.state = saved;
    }

    public static float Range(this System.Random r, float min, float max) => min + (float)r.NextDouble() * (max - min);
    public static int RangeInclusive(this System.Random r, Vector2Int range) => r.Next(range.x, range.y + 1);
    public static bool Chance(this System.Random r, float p) => r.NextDouble() < p;

    // A weighted pick; returns -1 when every weight is zero.
    public static int Pick(this System.Random r, IList<float> weights)
    {
        float total = 0f;
        foreach (var w in weights) total += Mathf.Max(0f, w);
        if (total <= 0f) return -1;
        double roll = r.NextDouble() * total;
        for (int i = 0; i < weights.Count; i++)
        {
            roll -= Mathf.Max(0f, weights[i]);
            if (roll <= 0 && weights[i] > 0f) return i;
        }
        for (int i = weights.Count - 1; i >= 0; i--) if (weights[i] > 0f) return i;
        return -1;
    }
}
