using System.Collections.Generic;
using UnityEngine;

// Where the progression is in broad strokes (pools open by stage, never on one fixed day).
public enum GameStage { Early, EarlyMid, Mid, Late }

// Who walks up to the table next (ChooseVisit). Appended only.
public enum VisitKind { Nobody, CollectOrder, EventTrader, Collector, Repair, Story, Special, Referral, Regular, Acquaintance, Stranger }

public enum SpecialKind { GuardCaptain, RoyalMessenger, FamousDwarf, ElvenDuelist, WealthyMerchant }

// How NpcStories.Meet should build today's new face (WorldDirector fills it; null = the old behaviour).
public class MeetOptions
{
    public Race? race;
    public int? archetype;
    public System.Func<int, float> tradeWeight;  // CustomerArchetype index -> weight (0 = not today)
    public float rankShift;                       // + draws the established, - the beginners
    public int rank = -1;                         // forced rank in their trade (-1 = rolled)
    public int minWealth;
    public int relatedTo;                         // sent by this customer (word of mouth)
    public NpcRelation relation;
}

// How NpcStories.OrderFor should shape the job (WorldDirector fills it; null = the old behaviour).
public class OrderBias
{
    public System.Func<WeaponType, float> weaponWeight; // 0 = not asked for yet
    public int statFocus = -1;
    public string focusLine;
    public float tierUp;
    public float payMul = 1f;
}

// What the Main Room knows about who COULD come right now (ChooseVisit decides who does).
public struct VisitContext
{
    public bool orderDue;        // an order's customer is due back
    public bool eventTraderDue;  // the Event Trader's day (see EventTraderMayCome)
    public bool collectorDue;
    public bool newWorkDue;      // one of today's arrivals with new work has come
    public bool hasWorn, hasStory, hasRegular, hasAcquaintance;
}

// THE LIVING WORLD. Same structure for every player (the day loop, the stations, the rules), different content inside
// it for every save: which folk come and when they're first seen, what they ask for, which weapons turn up, city
// events, word of mouth, special commissions, the Event Trader's timing, the supplier's stock, the weather.
//   - Weighted, never uniform: race/trade/weapon weights come from the day, reputation, city event, the smith's standing
//     with each folk, and a repetition penalty on what was asked for lately (never a ban).
//   - Guaranteed: important content has a window and a latest day (other races, the first city event, the Event Trader,
//     the first returning weapon, a special commission) - randomness varies the timing, never removes the content.
//   - Stable: every roll is seeded from the save's WorldSeed + what + when (WorldRandom), and every decision is written
//     into the save as soon as it's made, so a reload replays the same world and can't be used to reroll it.
//   - Feasible: every order is checked against what the smith's known metals can actually reach (MakeFeasible).
// Plain static logic over Workshop (+ its WorldState); the Main Room calls it, and so does WorldSimTest, headlessly.
public static class WorldDirector
{
    private static WorldSettings S => WorldSettings.Current;

    // CustomerArchetype indices (appended, never reordered - see CustomerOrder.cs).
    private const int Fencer = 0, Hunter = 1, Cook = 2, Mercenary = 3, BladeDancer = 4, Knight = 5, Farmer = 6, Ranger = 7, GroveKeeper = 8,
                      Miner = 9, Shieldwarden = 10, Guard = 12, Scout = 13, Stoneworker = 14, Merchant = 15, Boatman = 16;
    private static readonly int[] MediumTrades = { Fencer, Mercenary, Shieldwarden, Guard, Merchant };
    private static readonly int[] SoldierTrades = { Guard, Shieldwarden, Mercenary, Knight };
    private static readonly float[] TierValues = { 25f, 50f, 70f, 85f };
    private const int RecentCap = 10;

    // ======================================================================================== setup

    // A new game: its seed and its own progression windows (which folk show up first, which weapons are asked for
    // early, when the Event Trader first comes).
    public static void NewWorld(Workshop w, int seed)
    {
        var s = S;
        var world = w.world = new WorldState { worldSeed = Mathf.Max(1, seed) };
        var rng = WorldRandom.For(world.worldSeed, "setup", 0);

        // Elves and dwarves: one is guaranteed early in the window, the other by its end - which one comes first varies.
        int early = Mathf.Min(s.raceGuaranteeDays.y, rng.Next(s.raceGuaranteeDays.x, s.raceGuaranteeDays.x + 2));
        bool elfFirst = rng.Chance(0.5f);
        world.raceGuaranteeDay[(int)Race.Elf] = elfFirst ? early : s.raceGuaranteeDays.y;
        world.raceGuaranteeDay[(int)Race.Dwarf] = elfFirst ? s.raceGuaranteeDays.y : early;

        // Weapons: sword and dagger from the start plus one more; the other two open on their own days.
        var rest = new List<WeaponType> { WeaponType.Hammer, WeaponType.Spear, WeaponType.Sickle };
        world.weaponIntroDay[(int)WeaponType.Sword] = 1;
        world.weaponIntroDay[(int)WeaponType.Dagger] = 1;
        var starter = rest[rng.Next(rest.Count)];
        rest.Remove(starter);
        world.weaponIntroDay[(int)starter] = 1;
        int a = rng.RangeInclusive(s.weaponIntroDays), b = rng.RangeInclusive(s.weaponIntroDays);
        if (b == a) b = Mathf.Min(s.weaponIntroDays.y + 1, a + 1);
        world.weaponIntroDay[(int)rest[0]] = Mathf.Min(a, b);
        world.weaponIntroDay[(int)rest[1]] = Mathf.Max(a, b);

        // The Event Trader: may come from a day in the window's start, guaranteed by its end.
        w.nextEventDay = rng.Next(s.firstEventTraderDays.x, s.firstEventTraderDays.x + 2);
        world.firstEventTraderDay = Mathf.Max(w.nextEventDay + 1, s.firstEventTraderDays.y + rng.Next(0, 2));

        world.metalUnlockDay[Workshop.StartingMetal] = 1;
        world.eventsPlannedThrough = 0;
    }

    // Any game without a world yet (a new Workshop, a save from before this existed): give it one. A game already under
    // way keeps everything it already had (all weapons asked for, no races hidden again).
    public static void EnsureWorld(Workshop w)
    {
        if (w.world == null) w.world = new WorldState();
        w.world.AfterLoad();
        if (w.world.worldSeed != 0) return;
        bool underway = w.day > 1 || w.history.customers.Count > 0 || w.history.swords.Count > 0;
        int keepEventDay = w.nextEventDay;
        NewWorld(w, WorldRandom.Hash(w.timeSeed, "world", 0));
        if (!underway) return;
        var world = w.world;
        w.nextEventDay = keepEventDay;
        for (int i = 0; i < world.weaponIntroDay.Length; i++) world.weaponIntroDay[i] = 1;
        world.firstEventTraderDay = Mathf.Max(w.day, keepEventDay);
        foreach (var c in w.history.customers)
            if (c.archetypeIndex != CustomerArchetype.MerchantIndex && (int)c.race < 3 && world.raceFirstSeen[(int)c.race] == 0)
                world.raceFirstSeen[(int)c.race] = Mathf.Max(1, c.profile != null ? c.profile.firstMetDay : 1);
        if (w.history.customers.Exists(c => c.archetypeIndex == CustomerArchetype.MerchantIndex)) world.traderVisits = 1;
        foreach (var m in ForgeMaterials.All) if (w.IsUnlocked(m) && world.metalUnlockDay[m.index] == 0) world.metalUnlockDay[m.index] = 1;
        world.eventsPlannedThrough = w.day - 1;
    }

    // ======================================================================================== the broad stage

    public static GameStage Stage(Workshop w)
    {
        int sold = w.history.SoldCount, metals = w.unlockedMetals.Count;
        if (w.day <= 2 && sold < 3) return GameStage.Early;
        if (metals <= 1 || w.day <= 5) return GameStage.EarlyMid;
        if (w.reputation < S.highRep || metals < ForgeMaterials.All.Length) return GameStage.Mid;
        return GameStage.Late;
    }

    // ======================================================================================== the day

    public static CityEventInfo Today(Workshop w) => CityEvents.Get(w.world != null ? w.world.TodayEvent(w.day) : CityEventType.None);

    // A new morning (after the day number moved on): plan the city's events, reset the day's rolls, and let the weapons
    // out in the world live through whatever is going on.
    public static void BeginDay(Workshop w)
    {
        EnsureWorld(w);
        w.world.visitRolls = 0;
        PlanDay(w);
        LiveThroughEvent(w);
        ScanNotable(w);
    }

    // Decides (once, and saved) whether a city event starts today or is announced for a coming day. Contrast is part of
    // the design: a quiet day or more between events, rare ones rarer, the same one never back-to-back.
    public static void PlanDay(Workshop w)
    {
        EnsureWorld(w);
        var world = w.world;
        int day = w.day;
        if (world.eventsPlannedThrough >= day) return;
        world.eventsPlannedThrough = day;
        var s = S;
        if (world.EventOn(day) != null || world.Upcoming(day) != null) return; // something is on, or already announced
        if (day < s.firstEventDay) return;
        var last = world.LastEventBefore(day);
        if (last != null && day - last.endDay <= s.eventGapDays) return;

        var rng = WorldRandom.For(world.worldSeed, "events", day);
        bool guarantee = world.events.Count == 0 && day >= s.firstEventGuaranteeDay;
        if (!guarantee && !rng.Chance(s.eventChance)) return;

        var weights = new float[CityEvents.All.Length];
        for (int i = 1; i < CityEvents.All.Length; i++) weights[i] = EventWeight(w, CityEvents.All[i], last);
        int pick = rng.Pick(weights);
        if (pick <= 0) return;
        var info = CityEvents.All[pick];
        int length = rng.RangeInclusive(info.length);
        // Big occasions are cried a day or two ahead - something to look forward to ("tomorrow, the tournament!").
        int ahead = guarantee ? 0 : info.announceAhead;
        world.events.Add(new CityEventRecord { type = info.type, startDay = day + ahead, endDay = day + ahead + length - 1, announcedDay = ahead > 0 ? day : 0 });
    }

    private static float EventWeight(Workshop w, CityEventInfo e, CityEventRecord last)
    {
        int day = w.day;
        if (day < e.minDay || w.reputation < e.minRep) return 0f;
        if (e.seasons != null && System.Array.IndexOf(e.seasons, w.Season) < 0) return 0f;
        var prev = w.world.events.FindLast(x => x.type == e.type);
        if (prev != null && day - prev.endDay <= e.cooldown) return 0f;
        if (last != null && last.type == e.type) return 0f;
        float wt = e.weight * (e.rare ? 0.5f : 1f);
        if (e.rare && last != null && last.Info.rare) return 0f;
        if (prev == null) wt *= 1.5f; // something the smith hasn't seen yet is a little more likely
        return wt;
    }

    // What the smith hears this morning: today's event, a coming one being cried in the streets, a new kind of work.
    public static List<string> MorningLines(Workshop w)
    {
        var lines = new List<string>();
        var world = w.world;
        if (world == null) return lines;
        var today = world.EventOn(w.day);
        if (today != null)
            lines.Add(today.startDay == w.day ? $"{today.Info.name.ToUpper()}: {today.Info.morning}" : $"{today.Info.name}, day {w.day - today.startDay + 1}: {today.Info.morning}");
        var coming = world.Upcoming(w.day);
        if (coming != null && coming.announcedDay == w.day && coming.Info.herald != null)
            lines.Add(coming.Info.herald.Replace("{when}", coming.startDay - w.day == 1 ? "tomorrow" : $"in {coming.startDay - w.day} days"));
        for (int i = 0; i < world.weaponIntroDay.Length; i++)
            if (world.weaponIntroDay[i] == w.day && w.day > 1)
                lines.Add($"Word around the city: people have started asking smiths for {Plural(Weapons.Get((WeaponType)i).noun)}.");
        return lines;
    }

    private static string Plural(string noun) => noun.EndsWith("s") ? noun : noun + "s";

    // Tonight's glimpse of tomorrow (the End Day summary): a reason to play one more day, never a spoiler of who or what.
    public static List<string> Teasers(Workshop w)
    {
        var lines = new List<string>();
        var world = w.world;
        if (world == null) return lines;
        int tomorrow = w.day + 1;
        var coming = world.Upcoming(w.day);
        if (coming != null && coming.announcedDay > 0 && coming.startDay == tomorrow) lines.Add($"Tomorrow: the {coming.Info.name}!");
        else if (coming != null && coming.announcedDay > 0) lines.Add($"The {coming.Info.name} is {coming.startDay - w.day} days away.");
        if (world.referrals.Exists(r => !r.used && r.day <= tomorrow && r.positive)) lines.Add("Someone in town has been talking about your work...");
        else if (world.referrals.Exists(r => !r.used && r.day <= tomorrow)) lines.Add("Not everyone in town is singing your praises...");
        if (lines.Count < 2 && w.history.swords.Exists(r => r.IsOut && r.returnDay == tomorrow && !r.storyTold))
            lines.Add("One of your weapons may be coming home.");
        return lines;
    }

    // The day's weather: an event's own, or rolled from the seed (the same on every reload of the day).
    public static string Weather(Workshop w)
    {
        EnsureWorld(w);
        var ev = Today(w);
        if (ev.weather != null) return ev.weather;
        if (w.day == 1) return "clear"; // the very first morning is always a nice one
        var rng = WorldRandom.For(w.world.worldSeed, "weather", w.day);
        double roll = rng.NextDouble();
        string season = w.Season;
        // (Heavy rain is an event of its own - ordinary days are a little drier than they used to be.)
        return season == "Winter" ? (roll < 0.45 ? "snow" : "clear") : season == "Autumn" ? (roll < 0.35 ? "rain" : "clear") : (roll < 0.18 ? "rain" : "clear");
    }

    // Customers with new work today: the usual number, plus or minus what's going on.
    public static int ArrivalsDelta(Workshop w) => w.world == null ? 0 : Today(w).arrivalsDelta;

    // The supplier's stock today: seeded, only known metals, swayed by events (a dwarven caravan brings iron and steel).
    public static int[] RollSupply(Workshop w)
    {
        EnsureWorld(w);
        var rng = WorldRandom.For(w.world.worldSeed, "supply", w.day);
        var ev = Today(w);
        var offer = new int[ForgeMaterials.All.Length];
        int rank = 0;
        foreach (var m in ForgeMaterials.All)
        {
            if (!w.IsUnlocked(m)) continue;
            int n = rank == 0 ? rng.Next(4, 7) : rank == 1 ? rng.Next(2, 4) : rng.Next(1, 3);
            float mul = Mathf.Clamp(m.index < ev.supplyMul.Length ? ev.supplyMul[m.index] : 1f, S.supplySwing.x, S.supplySwing.y);
            offer[m.index] = Mathf.Max(1, Mathf.RoundToInt(n * mul));
            rank++;
        }
        return offer;
    }

    // ======================================================================================== who comes to the table

    // One roll per visit decision; the salt (visitRolls) is saved, so a reload asks the same question again.
    public static VisitKind ChooseVisit(Workshop w, VisitContext ctx)
    {
        EnsureWorld(w);
        var world = w.world;
        if (ctx.orderDue) return VisitKind.CollectOrder;
        if (ctx.eventTraderDue) return VisitKind.EventTrader;
        var rng = WorldRandom.For(world.worldSeed, "visit", w.day, world.visitRolls++);
        if (ctx.collectorDue && rng.Chance(0.7f)) return VisitKind.Collector;
        if (ctx.hasWorn && rng.Chance(0.8f)) return VisitKind.Repair;
        if (ctx.hasStory && rng.Chance(0.75f)) return VisitKind.Story;
        if (!ctx.newWorkDue) return VisitKind.Nobody;
        if (SpecialDue(w)) return VisitKind.Special;
        if (ReferralDue(w) != null && rng.Chance(0.8f)) return VisitKind.Referral;
        bool raceOverdue = OverdueRace(w).HasValue; // a guarantee is due: a new face, so it can actually happen
        if (!raceOverdue && ctx.hasRegular && rng.Chance(0.6f)) return VisitKind.Regular;
        if (!raceOverdue && ctx.hasAcquaintance && rng.Chance(Stage(w) <= GameStage.EarlyMid ? 0.3f : 0.45f)) return VisitKind.Acquaintance;
        return VisitKind.Stranger;
    }

    // The salt for everything generated during the visit just chosen (see WorldRandom.Seeded in the Main Room).
    public static int VisitSalt(Workshop w) => w.world != null ? w.world.visitRolls : 0;

    // ---- race ----------------------------------------------------------------------------------------------------

    // A race whose guaranteed day has come without it showing up (the earliest such), or null.
    public static Race? OverdueRace(Workshop w)
    {
        var world = w.world;
        if (world == null) return null;
        Race? best = null;
        int bestDay = int.MaxValue;
        for (int i = 1; i < 3; i++)
            if (world.raceFirstSeen[i] == 0 && world.raceGuaranteeDay[i] > 0 && w.day >= world.raceGuaranteeDay[i] && world.raceGuaranteeDay[i] < bestDay)
            { best = (Race)i; bestDay = world.raceGuaranteeDay[i]; }
        return best;
    }

    public static float[] RaceWeights(Workshop w)
    {
        var s = S;
        var world = w.world;
        var ev = Today(w);
        var weights = new float[3];
        for (int i = 0; i < 3; i++)
        {
            float wt = RaceInfo.All[i].weight;
            if (i > 0)
            {
                if (w.day < s.otherRacesFromDay) { weights[i] = 0f; continue; }
                // Not seen yet: more likely every day until their guaranteed day.
                if (world.raceFirstSeen[i] == 0) wt *= 1f + 0.5f * (w.day - s.otherRacesFromDay);
            }
            wt *= ev.raceMul[i];
            wt *= Mathf.Clamp(1f + world.raceStanding[i] * s.standingInfluence, 0.3f, 1.8f);
            wt *= Mathf.Pow(s.repetitionPenalty, Repeats(world.recentRaces, i, Mathf.Max(2, s.recentMemory - 1)));
            weights[i] = wt;
        }
        return weights;
    }

    public static Race PickRace(Workshop w, System.Random rng)
    {
        var world = w.world;
        if (world.forceNextRace.HasValue) { var f = world.forceNextRace.Value; world.forceNextRace = null; return f; }
        var overdue = OverdueRace(w);
        if (overdue.HasValue) return overdue.Value;
        int pick = rng.Pick(RaceWeights(w));
        return pick < 0 ? Race.Human : (Race)pick;
    }

    // ---- trade ---------------------------------------------------------------------------------------------------

    public static float TradeWeight(Workshop w, int archetype)
    {
        var s = S;
        var a = CustomerArchetype.All[archetype];
        // A trade whose weapons are all still unheard of can't come yet.
        bool any = false;
        foreach (var t in a.weapons) if (WeaponWeight(w, t) > 0f) { any = true; break; }
        if (!any) return 0f;
        float rep = w.reputation, wt;
        bool medium = System.Array.IndexOf(MediumTrades, archetype) >= 0;
        if (archetype == Knight) wt = w.day <= 1 ? 0f : rep < s.mediumRep ? 0.15f : rep < s.highRep ? 0.8f : 1.6f;
        else if (archetype == BladeDancer) wt = rep < s.mediumRep ? 0.5f : 1.2f;
        else if (medium) wt = w.day <= 1 ? 0.3f : rep < s.mediumRep ? 0.6f : rep < s.highRep ? 1.1f : 1.2f;
        else wt = rep < s.mediumRep ? 1.3f : rep < s.highRep ? 1f : 0.75f; // ordinary folk: the bread and butter of an unknown smith
        var ev = Today(w);
        if (System.Array.IndexOf(ev.favouredTrades, archetype) >= 0) wt *= ev.tradeMul;
        if (System.Array.IndexOf(SoldierTrades, archetype) >= 0) wt *= 1f + 0.2f * Mathf.Min(5, w.world.guardFavour);
        wt *= Mathf.Pow(s.repetitionPenalty, Repeats(w.world.recentTrades, archetype, s.recentMemory));
        return wt;
    }

    // Low reputation brings apprentices and farmhands; a famous smith draws masters.
    public static float RankShift(Workshop w) => w.day <= 1 ? -0.2f : Mathf.Clamp((w.reputation - 45f) / 200f, -0.15f, 0.2f);

    // ---- weapon --------------------------------------------------------------------------------------------------

    public static bool WeaponKnown(Workshop w, WeaponType t)
    {
        var world = w.world;
        if (world == null) return true;
        int i = (int)t;
        return world.weaponIntroDay[i] <= 0 || w.day >= world.weaponIntroDay[i] || Today(w).weaponMul[i] >= 3f; // an event can bring one early
    }

    public static float WeaponWeight(Workshop w, WeaponType t)
    {
        if (!WeaponKnown(w, t)) return 0f;
        var s = S;
        var world = w.world;
        int i = (int)t;
        float wt = Today(w).weaponMul[i];
        wt *= Mathf.Pow(s.repetitionPenalty, Repeats(world.recentWeapons, i, s.recentMemory));
        // A streak is what reads as monotony: the same weapon twice running is fine, a third time is rare, a fourth rarer.
        int streak = Streak(world.recentWeapons, i);
        if (streak >= 2) wt *= streak == 2 ? 0.3f : 0.08f;
        if (world.ordersGenerated >= s.noveltyAfter && Repeats(world.recentWeapons, i, s.noveltyAfter) == 0) wt *= s.noveltyBonus;
        return wt;
    }

    public static OrderBias Bias(Workshop w)
    {
        var ev = Today(w);
        return new OrderBias { weaponWeight = t => WeaponWeight(w, t), statFocus = ev.statFocus, focusLine = ev.focusLine, tierUp = ev.tierUp, payMul = ev.payMul };
    }

    private static int Repeats(List<int> recent, int value, int window)
    {
        int n = 0;
        for (int i = recent.Count - 1, k = 0; i >= 0 && k < window; i--, k++) if (recent[i] == value) n++;
        return n;
    }

    private static int Streak(List<int> recent, int value)
    {
        int n = 0;
        for (int i = recent.Count - 1; i >= 0 && recent[i] == value; i--) n++;
        return n;
    }

    private static void Push(List<int> list, int value)
    {
        list.Add(value);
        while (list.Count > RecentCap) list.RemoveAt(0);
    }

    // ======================================================================================== making people and orders

    public static MeetOptions MeetFor(Workshop w, System.Random rng) =>
        new MeetOptions { race = PickRace(w, rng), tradeWeight = i => TradeWeight(w, i), rankShift = RankShift(w) };

    // A new face with a new order. (Draws from UnityEngine.Random - call inside a WorldRandom.Seeded scope.)
    public static CustomerOrder Stranger(Workshop w, out CustomerRecord who)
    {
        EnsureWorld(w);
        var rng = WorldRandom.For(w.world.worldSeed, "race", w.day, VisitSalt(w));
        who = NpcStories.Meet(w.history, w.day, MeetFor(w, rng));
        SawRace(w, who.race);
        return NewOrder(w, who);
    }

    // Someone known coming back with new work.
    public static CustomerOrder NewOrder(Workshop w, CustomerRecord c)
    {
        EnsureWorld(w);
        var order = NpcStories.OrderFor(c, w.history, w.day, w.unlockedMetals, w.reputation, Bias(w));
        Finish(w, order, c);
        return order;
    }

    // A regular's follow-up (CustomerOrder.FollowUp shapes it from their last weapon): made possible and remembered.
    public static void FinishFollowUp(Workshop w, CustomerOrder order, CustomerRecord c)
    {
        EnsureWorld(w);
        order.duringEvent = w.world.TodayEvent(w.day);
        order.toleranceBonus = Tolerance(c);
        MakeFeasible(w, order);
        Remember(w, order);
    }

    public static void SawRace(Workshop w, Race race)
    {
        int i = (int)race;
        if (i >= 0 && i < 3 && w.world.raceFirstSeen[i] == 0) w.world.raceFirstSeen[i] = w.day;
    }

    // The customer's priorities, temperament and purse turned into the shape of the job - then checked it can be done.
    public static void Finish(Workshop w, CustomerOrder order, CustomerRecord c)
    {
        var s = S;
        var p = c.profile;
        order.duringEvent = w.world.TodayEvent(w.day);
        order.toleranceBonus = Tolerance(c);
        var rng = WorldRandom.For(w.world.worldSeed, "shape", w.day, c.id * 131 + VisitSalt(w));

        // A modest customer now and then wants a plain all-round weapon: a little of everything, nothing extreme, low pay.
        if (p != null && p.wealth <= 1 && order.quantity == 1 && !order.rush && order.tier <= 1 && !order.IsRepair && rng.Chance(s.allRoundOrderChance))
        {
            float baseNeed = order.tier == 0 ? 30f : 40f;
            order.allRound = true;
            order.primaryStatPlusOne = 0;
            order.minDurability = baseNeed;
            order.minBalance = baseNeed;
            order.minSharpness = order.Weapon.hasEdge ? baseNeed - 5f : 0f;
            order.requiredMaterial = null;
            order.reward = Mathf.RoundToInt((CustomerOrder.BaseFee + baseNeed * CustomerOrder.FeePerPoint * 1.7f) * 0.9f);
            string plain = $"Nothing fancy - a sound all-round {order.Weapon.noun}: sturdy enough, even in the hand{(order.Weapon.hasEdge ? ", with a fair edge" : "")}.";
            // In their words: the plain request takes the place of the job line, and they don't insist on any one thing.
            var arch = CustomerArchetype.All[Mathf.Clamp(order.archetypeIndex, 0, CustomerArchetype.All.Length - 1)];
            string job = arch.Line(arch.tierLines[Mathf.Clamp(order.tier, 0, arch.tierLines.Length - 1)], order.weaponType);
            foreach (var demand in NpcStories.StatDemand) order.requestText = order.requestText.Replace(" " + demand, "");
            order.requestText = order.requestText.Contains(job) ? order.requestText.Replace(job, plain) : InsertAfterFirstSentence(order.requestText, plain);
            order.paperQuote = plain;
        }
        else
        {
            // Priorities: in a job with several needs, one matters most (their own insistence), the rest are "should be".
            int count = (order.minDurability > 0f ? 1 : 0) + (order.minBalance > 0f ? 1 : 0) + (order.minSharpness > 0f ? 1 : 0);
            if (count >= 2)
            {
                int primary = p != null && Need(order, p.preferredStat) > 0f ? p.preferredStat : HighestStat(order);
                float top = Mathf.Max(order.minDurability, Mathf.Max(order.minBalance, order.minSharpness));
                float drop = Mathf.Max(0f, s.secondaryStatDrop - CustomerOrder.MultiStatDiscount);
                for (int stat = 0; stat < 3; stat++)
                {
                    float need = Need(order, stat);
                    if (need <= 0f) continue;
                    SetNeed(order, stat, stat == primary ? top + CustomerOrder.MultiStatDiscount * 0.5f : Mathf.Max(20f, need - drop));
                }
                order.primaryStatPlusOne = primary + 1;
            }
        }
        MakeFeasible(w, order);
        Remember(w, order);
    }

    // How far short of what they asked they'll still (grudgingly) take a weapon: perfectionists won't, easy-going folk will.
    public static float Tolerance(CustomerRecord c)
    {
        if (c == null || c.profile == null || !c.profile.IsSet) return 0f;
        float t = 0f;
        foreach (int trait in new[] { c.profile.trait1, c.profile.trait2 })
        {
            string n = NpcStories.Traits[Mathf.Clamp(trait, 0, NpcStories.Traits.Length - 1)].name;
            if (n == "precise" || n == "demanding" || n == "proud") t -= 5f;
            else if (n == "impatient") t -= 2f;
            else if (n == "calm" || n == "kind" || n == "frugal" || n == "cheerful") t += 4f;
        }
        return Mathf.Clamp(t, -9f, 8f);
    }

    // ANTI-FRUSTRATION: no order may ask for a metal the smith doesn't know, a stat no known metal can reach (with a
    // margin), or Sharpness on a weapon without an edge. Returns true if anything had to be lowered.
    public static bool MakeFeasible(Workshop w, CustomerOrder o, float margin = -1f)
    {
        if (margin < 0f) margin = S.feasibilityMargin;
        bool changed = false;
        if (o.requiredMaterial != null && !w.unlockedMetals.Contains(o.requiredMaterial)) { o.requiredMaterial = null; changed = true; }
        var named = o.requiredMaterial != null ? ForgeMaterials.Find(o.requiredMaterial) : null;
        float Cap(int stat) => named != null ? named.Cap(stat) : w.BestCap(stat);
        if (!o.Weapon.hasEdge && o.minSharpness > 0f) { o.minSharpness = 0f; changed = true; }
        for (int stat = 0; stat < 3; stat++)
        {
            float need = Need(o, stat), max = Cap(stat) - margin;
            if (need > max) { SetNeed(o, stat, Mathf.Max(20f, max)); changed = true; }
        }
        if (o.minDurability <= 0f && o.minBalance <= 0f && o.minSharpness <= 0f) { o.minDurability = Mathf.Min(25f, Cap(0) - margin); changed = true; }

        // ...nor a combination the anvil can't supply: Durability and Balance come out of the same handful of anvil points
        // (plus a little from the quench and the handle), and every grindstone touch for Sharpness wears Durability away.
        // Too much at once: the less important needs give way first, then the main one.
        // ...nor an edge sharper than the stone's four touches can give (a few perfect ones allowed for serious work).
        if (o.Weapon.hasEdge && o.minSharpness > SharpReach(o)) { o.minSharpness = Mathf.Max(20f, SharpReach(o)); changed = true; }
        float budget = AnvilBudget(o);
        for (int guard = 0; guard < 60 && Workload(o, named) > budget; guard++)
        {
            int primary = o.PrimaryStat >= 0 && Need(o, o.PrimaryStat) > 0f ? o.PrimaryStat : HighestStat(o);
            int lower = -1;
            for (int stat = 0; stat < 3; stat++)
                if (stat != primary && Need(o, stat) > 20f && (lower < 0 || Need(o, stat) > Need(o, lower))) lower = stat;
            if (lower < 0) lower = Need(o, primary) > 20f ? primary : -1;
            if (lower < 0) break;
            SetNeed(o, lower, Mathf.Max(20f, Need(o, lower) - 2f));
            changed = true;
        }
        return changed;
    }

    public static float SharpReach(CustomerOrder o)
        => 4f * (o.IsChallenge ? 22f : o.tier >= 2 || o.special ? 17f : 14.5f) * Mathf.Max(0.5f, o.Weapon.sharpGain);

    // Anvil points a job needs (see MakeFeasible): Durability past what a good quench gives, plus what grinding the edge
    // will wear away; Balance past what fitting the handle gives.
    public static float Workload(CustomerOrder o, ForgeMaterialInfo metal = null)
    {
        var wi = o.Weapon;
        float wearScale = metal != null ? metal.wearScale : 1f;
        float touches = wi.hasEdge ? Mathf.Clamp(Mathf.Ceil(o.minSharpness / (14f * Mathf.Max(0.5f, wi.sharpGain))), 1f, 4f) : 0f;
        float durPts = Mathf.Max(0f, o.minDurability + touches * 5f * wearScale - 6f) / wi.durPerPoint;
        float balPts = Mathf.Max(0f, o.minBalance - 8f) / wi.balPerPoint;
        return durPts + balPts;
    }

    // What a smith can be expected to manage: a good furnace's worth of anvil points - a little more for the serious
    // jobs of an established smith, a perfect furnace's worth for the Event Trader's challenge.
    public static float AnvilBudget(CustomerOrder o) => o.IsChallenge ? 14.5f : o.tier >= 2 || o.special ? 12.5f : 11f;

    // Is this order possible for this smith right now? (Used by the simulation and the debug tools.)
    public static string WhyImpossible(Workshop w, CustomerOrder o)
    {
        if (o.requiredMaterial != null && !w.unlockedMetals.Contains(o.requiredMaterial)) return $"needs unknown {o.requiredMaterial}";
        if (!o.Weapon.hasEdge && o.minSharpness > 0f) return "sharpness on an edgeless weapon";
        var named = o.requiredMaterial != null ? ForgeMaterials.Find(o.requiredMaterial) : null;
        for (int stat = 0; stat < 3; stat++)
        {
            float cap = named != null ? named.Cap(stat) : w.BestCap(stat);
            if (Need(o, stat) > cap - 2f) return $"{NpcStories.StatNames[stat]} {Need(o, stat):0} beyond reach ({cap:0})";
        }
        if (o.Weapon.hasEdge && o.minSharpness > SharpReach(o) + 0.01f) return $"Sharpness {o.minSharpness:0} beyond four touches ({SharpReach(o):0})";
        if (Workload(o, named) > AnvilBudget(o) + 0.01f) return $"needs {Workload(o, named):0.0} anvil points (budget {AnvilBudget(o):0.0})";
        return null;
    }

    public static void Remember(Workshop w, CustomerOrder o)
    {
        var world = w.world;
        Push(world.recentWeapons, (int)o.weaponType);
        Push(world.recentRaces, (int)o.race);
        Push(world.recentTrades, o.archetypeIndex);
        Push(world.recentStats, o.PrimaryStat >= 0 ? o.PrimaryStat : HighestStat(o));
        int wi = (int)o.weaponType;
        if (world.weaponFirstAsked[wi] == 0) world.weaponFirstAsked[wi] = w.day;
        SawRace(w, o.race);
        world.ordersGenerated++;
    }

    public static float Need(CustomerOrder o, int stat) => stat == 0 ? o.minDurability : stat == 1 ? o.minBalance : o.minSharpness;
    private static void SetNeed(CustomerOrder o, int stat, float v) { if (stat == 0) o.minDurability = v; else if (stat == 1) o.minBalance = v; else o.minSharpness = v; }
    private static int HighestStat(CustomerOrder o) => o.minDurability >= o.minBalance && o.minDurability >= o.minSharpness ? 0 : o.minBalance >= o.minSharpness ? 1 : 2;

    private static string InsertAfterFirstSentence(string text, string insert)
    {
        if (string.IsNullOrEmpty(text)) return insert;
        int cut = -1;
        foreach (var mark in new[] { ". ", "! ", "? " })
        {
            int i = text.IndexOf(mark, System.StringComparison.Ordinal);
            if (i >= 0 && (cut < 0 || i < cut)) cut = i;
        }
        return cut < 0 ? $"{insert} {text}" : $"{text.Substring(0, cut + 2)}{insert} {text.Substring(cut + 2)}";
    }

    // ======================================================================================== word of mouth

    public static Referral ReferralDue(Workshop w)
    {
        if (w.world == null) return null;
        return w.world.referrals.Find(r => !r.used && r.day <= w.day && w.history.Customer(r.fromCustomerId) != null);
    }

    // Someone sent by a customer (or put on guard by their grumbling) - a new face who talks about that weapon.
    public static CustomerOrder Referred(Workshop w, Referral r, out CustomerRecord who)
    {
        var h = w.history;
        var from = h.Customer(r.fromCustomerId);
        var sword = h.Find(r.swordId);
        var rng = WorldRandom.For(w.world.worldSeed, "referral-who", w.day, r.fromCustomerId * 17 + r.swordId);
        var opts = MeetFor(w, rng);
        if (rng.Chance(0.65f)) opts.race = r.race;
        int trade = from.archetypeIndex;
        opts.tradeWeight = i => TradeWeight(w, i) * (i == trade ? 3f : 1f) + (i == trade ? 0.2f : 0f);
        opts.relatedTo = from.id;
        opts.relation = rng.Chance(0.4f) ? NpcRelation.Comrade : NpcRelation.Friend;
        who = NpcStories.Meet(h, w.day, opts);
        SawRace(w, who.race);

        var bias = Bias(w);
        var baseWeight = bias.weaponWeight;
        var similar = r.weapon;
        bias.weaponWeight = t => baseWeight(t) * (t == similar ? 2.5f : 1f); // something like it, not necessarily the same
        var order = NpcStories.OrderFor(who, h, w.day, w.unlockedMetals, w.reputation, bias);
        Finish(w, order, who);
        order.referredBy = from.id;

        // Their introduction names who sent them (NpcStories.RelationLine) - replaced by what that person actually said.
        string line = NpcStories.ReferralLine(who, h, sword, r.positive);
        string rel = NpcStories.RelationLine(who, h);
        if (!r.positive)
        {
            // They've heard what went wrong, and they want exactly that done right.
            if (sword != null)
            {
                int weak = System.Array.IndexOf(NpcStories.StatNames, SwordStories.WeakestStat(sword.sword));
                if (weak >= 0 && Need(order, weak) > 0f) SetNeed(order, weak, Need(order, weak) + 5f);
            }
            order.toleranceBonus -= 3f;
            MakeFeasible(w, order);
        }
        if (line != null)
            order.requestText = rel != null && order.requestText.Contains(rel) ? order.requestText.Replace(rel, line) : InsertAfterFirstSentence(order.requestText, line);
        r.used = true;
        h.LogPerson(w.day, HistoryEventType.WordOfMouth, who, r.positive ? $"Sent to the smithy by {from.name}" : $"Heard {from.name}'s grumbling about the smith");
        if (sword != null) h.LogWeapon(w.day, HistoryEventType.WordOfMouth, sword, from,
            r.positive ? $"{from.name} recommended the smith to {who.name}" : $"{from.name} complained about it to {who.name}");
        return order;
    }

    // ======================================================================================== special commissions

    public static bool SpecialDue(Workshop w)
    {
        var world = w.world;
        if (world == null) return false;
        if (world.forceSpecial) return true;
        var s = S;
        int day = w.day;
        if (day < s.specialFromDay || w.reputation < s.specialMinRep) return false;
        if (day - world.lastSpecialDay <= s.specialCooldown) return false;
        if (w.orders.Exists(o => o.special)) return false;
        if (world.lastTraderVisitDay == day) return false; // one headline a day
        if (world.specialsOffered == 0 && day >= s.specialGuaranteeDay) return true;
        return WorldRandom.For(world.worldSeed, "special", day).Chance(s.specialChance * Today(w).specialMul);
    }

    private static readonly string[] SpecialTitles = { "Captain of the City Guard", "Royal Messenger", "famed champion of the mountain holds", "elven duellist of renown", "wealthy guild master" };

    // A rare, difficult, memorable job - "The KING asked me to forge this weapon." (UnityEngine.Random: seeded scope.)
    public static CustomerOrder Special(Workshop w, out CustomerRecord who, out SpecialKind kind)
    {
        var world = w.world;
        var s = S;
        var ev = Today(w).type;
        var rng = WorldRandom.For(world.worldSeed, "special-kind", w.day);
        var weights = new float[5];
        weights[(int)SpecialKind.GuardCaptain] = 1f + 0.5f * world.guardFavour + (ev == CityEventType.BanditsOnRoads ? 3f : 0f);
        weights[(int)SpecialKind.RoyalMessenger] = w.reputation >= s.highRep ? (ev == CityEventType.RoyalTournament ? 4f : 1f) : 0f;
        weights[(int)SpecialKind.FamousDwarf] = ev == CityEventType.DwarvenCaravan ? 4f : 1f;
        weights[(int)SpecialKind.ElvenDuelist] = ev == CityEventType.ElvenDelegation ? 4f : ev == CityEventType.RoyalTournament ? 1.5f : 1f;
        weights[(int)SpecialKind.WealthyMerchant] = ev == CityEventType.MarketDay || ev == CityEventType.Festival ? 2.5f : 1f;
        for (int i = 0; i < 5; i++) if ((world.specialKindsDone & (1 << i)) != 0) weights[i] *= 0.25f;
        kind = (SpecialKind)Mathf.Max(0, rng.Pick(weights));

        Race race; int arch; WeaponType[] likes; int primary, secondary;
        switch (kind)
        {
            case SpecialKind.GuardCaptain: race = Race.Human; arch = Guard; likes = new[] { WeaponType.Sword, WeaponType.Spear }; primary = 0; secondary = 1; break;
            case SpecialKind.RoyalMessenger: race = Race.Human; arch = Knight; likes = new[] { WeaponType.Sword }; primary = 1; secondary = 2; break;
            case SpecialKind.FamousDwarf: race = Race.Dwarf; arch = Shieldwarden; likes = new[] { WeaponType.Hammer, WeaponType.Spear }; primary = 0; secondary = 1; break;
            case SpecialKind.ElvenDuelist: race = Race.Elf; arch = Fencer; likes = new[] { WeaponType.Dagger, WeaponType.Sword }; primary = 1; secondary = 2; break;
            default: race = Race.Human; arch = Merchant; likes = new[] { WeaponType.Dagger, WeaponType.Sword }; primary = 2; secondary = 1; break;
        }
        who = NpcStories.Meet(w.history, w.day, new MeetOptions { race = race, archetype = arch, rank = 2, minWealth = 3 });
        SawRace(w, who.race);
        var order = NpcStories.OrderFor(who, w.history, w.day, w.unlockedMetals, w.reputation, Bias(w));
        WeaponType weapon = likes[0];
        foreach (var t in likes) if (WeaponKnown(w, t)) { weapon = t; break; }
        if (!WeaponKnown(w, weapon)) weapon = WeaponType.Sword;
        order.weaponType = weapon;
        if (!order.Weapon.hasEdge && secondary == 2) secondary = primary == 0 ? 1 : 0;
        if (!order.Weapon.hasEdge && primary == 2) primary = 1;

        // As demanding as the smith's metals allow - the hardest job they'll have seen.
        int tier = 0;
        for (int t = 0; t < TierValues.Length; t++) if (TierValues[t] <= Mathf.Min(w.BestCap(primary), w.BestCap(secondary)) - s.feasibilityMargin) tier = t;
        order.tier = Mathf.Max(1, tier);
        order.minDurability = order.minBalance = order.minSharpness = 0f;
        float need = TierValues[order.tier];
        SetNeed(order, primary, need);
        SetNeed(order, secondary, need - 16f);
        order.primaryStatPlusOne = primary + 1;
        order.requiredMaterial = w.unlockedMetals.Count > 1 ? BestMetalFor(w, primary) : null;
        order.quantity = 1;
        order.rush = false;
        order.special = true;
        order.patienceDays = 4;
        order.toleranceBonus = -4f; // they expect the best
        order.reward = Mathf.RoundToInt((CustomerOrder.BaseFee + need * CustomerOrder.FeePerPoint * 2f * 1.2f) * 1.35f * s.specialPayMultiplier);
        order.titleOverride = $"{NpcStories.FullName(who)}, {SpecialTitles[(int)kind]}";
        order.duringEvent = world.TodayEvent(w.day);
        string W = order.Weapon.noun;
        string need1 = StatWords(primary), need2 = StatWords(secondary);
        string metal = order.requiredMaterial != null ? $" Forged from {order.requiredMaterial.ToLower()}, nothing less." : "";
        switch (kind)
        {
            case SpecialKind.GuardCaptain:
                order.requestText = $"I'm {NpcStories.FullName(who)}, Captain of the City Guard. My sergeants swear by your work - so I've come myself. I need a {W} for the captain of this city: {need1} above all, and {need2}.{metal} Men's lives will hang on it.";
                break;
            case SpecialKind.RoyalMessenger:
                order.requestText = $"By order of His Majesty the King! I am {NpcStories.FullName(who)}, royal messenger. The King himself has heard of your forge. He requires a {W} fit for the throne room - {need1}, and {need2}.{metal} Do not disappoint the crown.";
                break;
            case SpecialKind.FamousDwarf:
                order.requestText = $"You'll know my name - {NpcStories.FullName(who)}, of the mountain holds. They sing about me under three mountains, and I want a {W} worthy of the songs: {need1} above all, {need2} too.{metal} Make it, and I'll tell every hold who made it.";
                break;
            case SpecialKind.ElvenDuelist:
                order.requestText = $"I am {NpcStories.FullName(who)}. I have not lost a duel in two hundred years, and I do not intend to begin. A {W}, then - {need1}, and {need2}.{metal} I will know at once if it is anything less.";
                break;
            default:
                order.requestText = $"{NpcStories.FullName(who)}, master of the merchants' guild. I collect the finest work in the city, and I'm told yours may qualify. A {W}: {need1}, and {need2}.{metal} Price is no object - quality is.";
                break;
        }
        order.paperQuote = $"SPECIAL COMMISSION for the {SpecialTitles[(int)kind]}: a {W} - {need1} above all, and {need2}.{metal}";
        MakeFeasible(w, order);
        Remember(w, order);
        world.lastSpecialDay = w.day;
        world.specialsOffered++;
        world.specialKindsDone |= 1 << (int)kind;
        world.forceSpecial = false;
        return order;
    }

    private static string StatWords(int stat) => stat == 0 ? "tough enough to never break" : stat == 1 ? "perfectly balanced" : "keen as a razor";

    private static string BestMetalFor(Workshop w, int stat)
    {
        ForgeMaterialInfo best = null;
        foreach (var m in ForgeMaterials.All) if (w.IsUnlocked(m) && (best == null || m.Cap(stat) > best.Cap(stat))) best = m;
        return best?.name;
    }

    public static float RepMultiplier(CustomerOrder o) => o != null && o.special ? S.specialRepMultiplier : 1f;

    // ======================================================================================== consequences

    public static void OnDelivered(Workshop w, CustomerOrder o, List<SwordRecord> weapons, CustomerRecord buyer, Workshop.Verdict worst, bool firstSale)
    {
        EnsureWorld(w);
        var s = S;
        var world = w.world;
        int race = Mathf.Clamp((int)o.race, 0, 2);
        float delta = worst == Workshop.Verdict.Excellent ? s.standingExcellent : worst == Workshop.Verdict.Good ? s.standingGood
                    : worst == Workshop.Verdict.Grudging ? s.standingGrudging : 0.01f;
        world.raceStanding[race] = Mathf.Clamp(world.raceStanding[race] + delta, -1f, 1f);
        if (worst == Workshop.Verdict.Excellent && System.Array.IndexOf(SoldierTrades, o.archetypeIndex) >= 0) world.guardFavour++;

        var rng = WorldRandom.For(world.worldSeed, "delivered", w.day, o.id);
        // The first weapon the smith ever sells always comes back - the player sees that payoff early.
        if (firstSale)
            foreach (var r in weapons) r.returnDay = w.day + rng.RangeInclusive(s.firstReturnDays);

        // Word of mouth: pleased customers send friends; let-down ones grumble.
        int pending = world.referrals.FindAll(x => !x.used).Count;
        if (buyer != null && pending < s.maxPendingReferrals && weapons.Count > 0 && buyer.archetypeIndex != CustomerArchetype.MerchantIndex)
        {
            float chance = worst == Workshop.Verdict.Excellent ? s.referralChanceExcellent : worst == Workshop.Verdict.Good ? s.referralChanceGood
                         : worst == Workshop.Verdict.Grudging ? s.badWordChance : 0f;
            if (o.special) chance = Mathf.Max(chance, 0.6f);
            if (rng.Chance(chance))
                world.referrals.Add(new Referral
                {
                    day = w.day + rng.RangeInclusive(s.referralDelay), fromCustomerId = buyer.id, swordId = weapons[0].id,
                    positive = worst != Workshop.Verdict.Grudging, race = buyer.race, weapon = o.weaponType,
                });
        }

        foreach (var r in weapons)
        {
            if (o.special) MarkNotable(w, r, $"Made for the {SpecialTitles[SpecialIndex(o)]}");
            else CheckQuality(w, r);
        }
    }

    private static int SpecialIndex(CustomerOrder o)
    {
        for (int i = 0; i < SpecialTitles.Length; i++) if (o.titleOverride != null && o.titleOverride.EndsWith(SpecialTitles[i])) return i;
        return 0;
    }

    public static void OnDeclined(Workshop w, CustomerOrder o) => Standing(w, o, S.standingDeclined);
    public static void OnWalkedOff(Workshop w, CustomerOrder o) => Standing(w, o, S.standingWalkedOff);
    public static void OnWeaponRefused(Workshop w, CustomerOrder o) => Standing(w, o, S.standingRefusedWeapon);

    private static void Standing(Workshop w, CustomerOrder o, float delta)
    {
        if (o == null || o.IsChallenge) return;
        EnsureWorld(w);
        int race = Mathf.Clamp((int)o.race, 0, 2);
        w.world.raceStanding[race] = Mathf.Clamp(w.world.raceStanding[race] + delta, -1f, 1f);
    }

    public static void NoteUnlock(Workshop w, ForgeMaterialInfo m)
    {
        EnsureWorld(w);
        if (w.world.metalUnlockDay[m.index] == 0) w.world.metalUnlockDay[m.index] = w.day;
    }

    // ======================================================================================== the Event Trader

    // Whether she comes today: her first visit falls somewhere in a window (sooner for a smith who's delivered work),
    // guaranteed by its end; after that she comes back a few days after each visit - never on a special commission's day.
    public static bool EventTraderMayCome(Workshop w)
    {
        EnsureWorld(w);
        if (w.challenge != null) return true;
        if (w.NextLockedMetal == null || w.day < w.nextEventDay) return false;
        var world = w.world;
        if (world.lastSpecialDay == w.day) return false;
        var rng = WorldRandom.For(world.worldSeed, "trader", w.day);
        if (world.traderVisits == 0)
        {
            if (w.day >= world.firstEventTraderDay) return true;
            if (w.history.SoldCount < 1) return false; // she hears of smiths through their customers
            return rng.Chance(0.55f);
        }
        return w.day >= w.nextEventDay + 2 || rng.Chance(0.6f);
    }

    public static void TraderVisited(Workshop w)
    {
        EnsureWorld(w);
        w.world.traderVisits++;
        w.world.lastTraderVisitDay = w.day;
    }

    public static int NextTraderDay(Workshop w)
        => w.day + WorldRandom.For(w.world.worldSeed, "trader-gap", w.day).RangeInclusive(Workshop.EventTraderGap);

    // What her challenge asks for: a weapon the smith knows (ones they've forged more often a little likelier), not the
    // same as last time - then its needs checked against what the known metals can do, per stat.
    public static CustomerOrder Challenge(Workshop w, ForgeMaterialInfo reward)
    {
        EnsureWorld(w);
        var world = w.world;
        var rng = WorldRandom.For(world.worldSeed, "challenge", w.day);
        var weights = new float[Weapons.All.Length];
        for (int i = 0; i < weights.Length; i++)
        {
            if (!WeaponKnown(w, (WeaponType)i)) continue;
            int forged = w.history.swords.FindAll(r => r.sword.weaponType == (WeaponType)i).Count;
            weights[i] = (1f + 0.5f * Mathf.Min(forged, 4)) * (world.lastChallengeWeaponPlusOne - 1 == i ? 0.2f : 1f);
        }
        int pick = Mathf.Max(0, rng.Pick(weights));
        world.lastChallengeWeaponPlusOne = pick + 1;
        var type = (WeaponType)pick;
        CustomerOrder order;
        using (WorldRandom.Seeded(world.worldSeed, "challenge-stats", w.day)) order = CustomerOrder.Challenge(type, w.BestQualityCap, reward);
        int count = (order.minDurability > 0f ? 1 : 0) + (order.minBalance > 0f ? 1 : 0) + (order.minSharpness > 0f ? 1 : 0);
        MakeFeasible(w, order, count == 1 ? 2f : 8f);
        return order;
    }

    // ======================================================================================== the weapons out in the world

    // Owners whose trade takes part in today's event carry your weapons through it: a line in the weapon's story, some
    // wear - and now and then glory (a tournament) or the end of a weak one (bandits).
    public static void LiveThroughEvent(Workshop w)
    {
        var world = w.world;
        var rec = world.EventOn(w.day);
        if (rec == null) return;
        var info = rec.Info;
        if (info.takesPart.Length == 0 || info.useLines == null || info.useLines.Length == 0) return;
        var h = w.history;
        foreach (var r in h.swords.ToArray())
        {
            if (r.status != SwordStatus.OwnedByCustomer || r.order == null) continue;
            var owner = h.Owner(r);
            if (owner == null || System.Array.IndexOf(info.takesPart, owner.archetypeIndex) < 0) continue;
            var rng = WorldRandom.For(world.worldSeed, "event-use", w.day, r.id);
            if (!rng.Chance(0.45f)) continue;
            r.eventUses++;
            r.condition = Mathf.Max(owner.recurring ? 1f : 25f, r.condition - info.wear);
            string line = info.useLines[rng.Next(info.useLines.Length)].Replace("{n}", owner.name).Replace("{w}", r.sword.Weapon.noun);
            h.LogUse(r, w.day, line);
            string grade = r.sword.Grade;
            if (info.type == CityEventType.RoyalTournament && (grade == "S" || grade == "A") && rng.Chance(0.35f))
            {
                if (r.legendName == null)
                {
                    var names = owner.Archetype.swordNames;
                    if (names != null && names.Length > 0) r.legendName = names[rng.Next(names.Length)];
                }
                h.LogUse(r, w.day, $"Won glory at the Royal Tournament{(r.legendName != null ? $" - the crowd named it '{r.legendName}'" : "")}");
                MarkNotable(w, r, "Won glory at the Royal Tournament");
            }
            else if (info.type == CityEventType.BanditsOnRoads && r.sword.durability < 35f && rng.Chance(0.35f))
            {
                h.Destroy(r, w.day, $"Broke in a fight with bandits on the road");
                continue;
            }
            if (r.eventUses >= 2) MarkNotable(w, r, $"Through {r.eventUses} of the city's great events with its owner");
        }
    }

    // ======================================================================================== notable weapons

    public static void CheckQuality(Workshop w, SwordRecord r)
    {
        var s = r.sword;
        var wi = s.Weapon;
        float q = S.notableQuality;
        bool great = (wi.durWeight <= 0f || s.durability >= q) && (wi.balWeight <= 0f || s.balance >= q) && (!wi.hasEdge || wi.sharpWeight <= 0f || s.sharpness >= q);
        if (great && !s.cracked) MarkNotable(w, r, "Masterwork - exceptional in every way that matters");
    }

    public static void ScanNotable(Workshop w)
    {
        foreach (var r in w.history.swords)
        {
            if (r.notable) continue;
            if (r.repairCount >= S.notableRepairs && r.status != SwordStatus.Scrapped) MarkNotable(w, r, $"Repaired {r.repairCount} times and still going");
            else if (r.legendName != null) MarkNotable(w, r, $"Known far and wide as '{r.legendName}'");
        }
    }

    public static void MarkNotable(Workshop w, SwordRecord r, string reason)
    {
        if (r == null || r.notable) return;
        r.notable = true;
        r.notableReason = reason;
        w.history.LogWeapon(w.day, HistoryEventType.BecameNotable, r, w.history.Owner(r), $"★ {reason}");
    }

    // ======================================================================================== the Chronicle (journal)

    public static void ChroniclePages(Workshop w, out string left, out string right)
    {
        EnsureWorld(w);
        var h = w.history;
        var world = w.world;
        var l = new List<string> { "CHRONICLE OF THE SMITHY", $"Day {w.day} - {w.Season}   ·   {w.StarsText}", "" };
        var notable = h.swords.FindAll(r => r.notable);
        l.Add($"NOTABLE WORKS ({notable.Count})");
        if (notable.Count == 0) l.Add("None yet. Exceptional work, famous owners and long lives earn a weapon its star.");
        for (int i = Mathf.Max(0, notable.Count - 5); i < notable.Count; i++)
        {
            var r = notable[i];
            var owner = h.Owner(r);
            string name = r.legendName != null ? $" '{r.legendName}'" : "";
            l.Add($"★ {r.Label}{name} - {r.sword.materialName}, day {r.createdDay}{(owner != null ? $", {owner.name}" : "")}");
            l.Add($"   {r.notableReason}");
        }
        l.Add("");
        l.Add("METALS");
        var metals = new List<string>();
        foreach (var m in ForgeMaterials.All)
            if (w.IsUnlocked(m)) metals.Add(world.metalUnlockDay[m.index] > 1 ? $"{m.name} (day {world.metalUnlockDay[m.index]})" : m.name);
        l.Add(string.Join(", ", metals) + (w.NextLockedMetal != null ? "   ·   ? still unknown" : ""));
        left = string.Join("\n", l);

        var rt = new List<string> { "PEOPLE OF NOTE" };
        var people = h.customers.FindAll(c => c.archetypeIndex != CustomerArchetype.MerchantIndex && (c.recurring || c.purchases >= 2));
        people.Sort((a, b) => b.purchases.CompareTo(a.purchases));
        if (people.Count == 0) rt.Add("No regulars yet.");
        for (int i = 0; i < people.Count && i < 4; i++)
            rt.Add($"{people[i].Title} - {people[i].purchases} weapon{(people[i].purchases == 1 ? "" : "s")}");
        var folk = new List<string>();
        for (int i = 0; i < 3; i++) if (world.raceFirstSeen[i] > 0) folk.Add($"{RaceInfo.All[i].name}s (day {world.raceFirstSeen[i]})");
        if (folk.Count > 0) rt.Add("Folk met: " + string.Join(", ", folk));
        rt.Add("");
        rt.Add("THE CITY");
        var past = world.events.FindAll(e => e.startDay <= w.day);
        if (past.Count == 0) rt.Add("Nothing out of the ordinary yet.");
        for (int i = Mathf.Max(0, past.Count - 5); i < past.Count; i++)
        {
            var e = past[i];
            string when = e.startDay == e.endDay ? $"Day {e.startDay}" : $"Days {e.startDay}-{e.endDay}";
            rt.Add($"{when}: {(e.endDay >= w.day ? e.Info.morning : e.Info.journal)}");
        }
        var coming = world.Upcoming(w.day);
        if (coming != null && coming.announcedDay > 0) rt.Add($"Coming: {coming.Info.name}, day {coming.startDay}");
        right = string.Join("\n", rt);
    }

    // ======================================================================================== debug

    public static string Describe(Workshop w)
    {
        EnsureWorld(w);
        var world = w.world;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"WorldSeed {world.worldSeed}  day {w.day}  stage {Stage(w)}  rep {w.reputation:0}");
        sb.AppendLine($"Race guarantees: elf by {world.raceGuaranteeDay[1]} (seen {world.raceFirstSeen[1]}), dwarf by {world.raceGuaranteeDay[2]} (seen {world.raceFirstSeen[2]})");
        var intro = new List<string>();
        for (int i = 0; i < 5; i++) intro.Add($"{(WeaponType)i}@{world.weaponIntroDay[i]}");
        sb.AppendLine("Weapons open: " + string.Join(", ", intro));
        sb.AppendLine($"Event Trader: next {w.nextEventDay}, first guaranteed {world.firstEventTraderDay}, visits {world.traderVisits}");
        sb.AppendLine($"Standing H/E/D: {world.raceStanding[0]:0.00}/{world.raceStanding[1]:0.00}/{world.raceStanding[2]:0.00}  guard favour {world.guardFavour}");
        foreach (var e in world.events) sb.AppendLine($"Event {e.Info.name}: days {e.startDay}-{e.endDay}{(e.announcedDay > 0 ? $" (announced day {e.announcedDay})" : "")}");
        foreach (var r in world.referrals) sb.AppendLine($"Referral from #{r.fromCustomerId} ({(r.positive ? "+" : "-")}) day {r.day} {(r.used ? "used" : "pending")}");
        foreach (var r in w.history.swords)
            if (r.status == SwordStatus.OwnedByCustomer && r.returnDay > 0) sb.AppendLine($"{r.Label} returns day {r.returnDay}");
        sb.AppendLine($"Special: offered {world.specialsOffered}, last day {world.lastSpecialDay}");
        return sb.ToString();
    }

    // Debug: start an event today (replacing whatever was planned for today).
    public static void ForceEvent(Workshop w, CityEventType type, int length = 2)
    {
        EnsureWorld(w);
        w.world.events.RemoveAll(e => e.endDay >= w.day);
        if (type != CityEventType.None)
            w.world.events.Add(new CityEventRecord { type = type, startDay = w.day, endDay = w.day + Mathf.Max(1, length) - 1 });
        w.world.eventsPlannedThrough = w.day;
    }
}
