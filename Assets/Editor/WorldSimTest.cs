using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

// Plays several whole saves headlessly (no scene) through the REAL living-world code - WorldDirector, NpcStories,
// the order book, Judge/Payment, the Event Trader, supply, BlacksmithHistory - with a bot smith forging through the real
// station code (like BalanceTest), then compares the saves: same structure, different stories.
//   - Each save: its own seed, 15 days (TB_SIM_DAYS), a timeline of who came, what they asked, what happened.
//   - Checks: the guarantees (other folk, first event, the Event Trader, the first return) land in their windows; no
//     impossible order is ever offered; no event repeats back-to-back; nothing monotonous (long runs of one weapon);
//     one save replayed gives the very same timeline (determinism); a save/load round trip keeps every world decision;
//     and the saves really differ from each other.
// Writes Logs/world_sim.txt (read it!) and [SIM] lines. Run:
//   Unity.exe -batchmode -quit -projectPath <p> -executeMethod WorldSimTest.Run -logFile <log>
public static class WorldSimTest
{
    private static int failures;
    private static StringBuilder report;

    private static void Check(bool ok, string what)
    {
        if (!ok) failures++;
        string line = $"[SIM] {(ok ? "ok  " : "FAIL")} {what}";
        Debug.Log(line);
        report?.AppendLine(line);
    }

    private class Skill { public string name; public float perfect, good, anvilCatch; public int maxTouches; }
    private static readonly Skill Careful = new Skill { name = "careful", perfect = 0.5f, good = 0.42f, anvilCatch = 0.94f, maxTouches = 4 };
    private static readonly Skill Average = new Skill { name = "average", perfect = 0.35f, good = 0.5f, anvilCatch = 0.88f, maxTouches = 4 };

    private static ForgeStation furnace;
    private static QuenchBarrel barrel;
    private static Grindstone stone;
    private static WorkbenchStation bench;

    public class SaveRun
    {
        public string name;
        public int seed;
        public readonly List<string> timeline = new List<string>();
        public readonly List<string> offers = new List<string>();       // "d2 Elf ranger Dagger" - the comparable sequence
        public readonly List<WeaponType> weaponsAsked = new List<WeaponType>();
        public int impossible, specials, referrals, badWord, delivered, refused, walkedOff, declined, events, returns, notable;
        public int[] metalDay = new int[3];
        public int firstElf, firstDwarf, firstEvent, firstTrader, firstReturn;
        public float rep;
        public int gold;
        public int maxWeaponRun;
        public Workshop shop;
    }

    public static void Run()
    {
        failures = 0;
        report = new StringBuilder();
        string savePath = Path.Combine(Path.GetTempPath(), "tb_worldsim_save.json");
        string oldEnv = System.Environment.GetEnvironmentVariable("TB_SAVE_PATH");
        System.Environment.SetEnvironmentVariable("TB_SAVE_PATH", savePath);
        var go = new GameObject("WorldSim");
        furnace = go.AddComponent<ForgeStation>();
        barrel = go.AddComponent<QuenchBarrel>();
        stone = go.AddComponent<Grindstone>();
        bench = go.AddComponent<WorkbenchStation>();
        int days = int.TryParse(System.Environment.GetEnvironmentVariable("TB_SIM_DAYS"), out int d) ? d : 15;
        try
        {
            var seeds = new[] { 481932, 7, 99173, 250611, 31337 };
            var runs = new List<SaveRun>();
            for (int i = 0; i < seeds.Length; i++) runs.Add(Play($"Save {(char)('A' + i)}", seeds[i], days));
            foreach (var r in runs) WriteRun(r);
            Compare(runs, days);
            Determinism(seeds[0], days);
            SaveRoundTrip(seeds[1], savePath);
        }
        catch (System.Exception e) { failures++; Debug.LogError("[SIM] exception: " + e); report.AppendLine("EXCEPTION " + e); }
        finally
        {
            Object.DestroyImmediate(go);
            if (File.Exists(savePath)) File.Delete(savePath);
            System.Environment.SetEnvironmentVariable("TB_SAVE_PATH", oldEnv);
        }
        string result = $"[SIM] RESULT: {(failures == 0 ? "PASS" : $"FAIL ({failures})")}";
        report.AppendLine(result);
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/world_sim.txt", report.ToString());
        Debug.Log(result);
    }

    // ================================================================================================ one save

    public static SaveRun Play(string name, int seed, int days, int stopAtDay = -1, System.Action<Workshop> atStop = null)
    {
        Random.InitState(seed);
        var w = new Workshop { timeSeed = seed };
        WorldDirector.NewWorld(w, seed);
        return PlayFrom(name, w, days, stopAtDay, atStop, fresh: true);
    }

    // Plays on from any game (a new one, or a copy of the player's save - see WorldDebugMenu) to lastDay.
    public static SaveRun PlayFrom(string name, Workshop w, int lastDay, int stopAtDay = -1, System.Action<Workshop> atStop = null, bool fresh = false)
    {
        if (furnace == null)
        {
            var go = new GameObject("WorldSim") { hideFlags = HideFlags.HideAndDontSave };
            furnace = go.AddComponent<ForgeStation>();
            barrel = go.AddComponent<QuenchBarrel>();
            stone = go.AddComponent<Grindstone>();
            bench = go.AddComponent<WorkbenchStation>();
        }
        var run = new SaveRun { name = name, seed = w.world.worldSeed };
        run.shop = w;
        var h = w.history;
        run.metalDay[0] = 1;
        int days = lastDay;

        for (int day = fresh ? 1 : w.day + 1; day <= days; day++)
        {
            if (day > 1)
            {
                // AdvanceDay, as the Main Room does it.
                w.day = day;
                w.visitsToday = 0;
                w.ordersToday = 0;
                w.ReceiveDelivery();
                w.CollectCrate();
                h.ProcessNewDay(day);
                WorldDirector.BeginDay(w);
            }
            else WorldDirector.PlanDay(w);
            if (day == stopAtDay) { atStop?.Invoke(w); return run; }

            var ev = w.world.EventOn(day);
            string evText = ev != null ? $"  [{ev.Info.name}{(ev.startDay == day ? " begins" : "")}]" : "";
            var coming = w.world.Upcoming(day);
            if (coming != null && coming.announcedDay == day) evText += $"  (herald: {coming.Info.name} on day {coming.startDay})";
            if (ev != null && ev.startDay == day) { run.events++; if (run.firstEvent == 0) run.firstEvent = day; }
            run.timeline.Add($"--- Day {day} ({w.Season}, {WorldDirector.Weather(w)}) rep {w.reputation:0} gold {w.gold}{evText}");
            foreach (var line in WorldDirector.MorningLines(w)) run.timeline.Add($"    morning: {line}");

            // Customers coming back for their orders (the whole day's worth).
            foreach (var o in w.orders.ToArray()) if (day >= o.returnDay) Collect(run, w, o, day);

            // The morning's work: weapons still owed on open orders (yesterday's ingots have arrived).
            foreach (var o in w.orders.ToArray())
                for (int k = w.AssignedTo(o).Count; k < o.quantity && o.IsActive && !o.IsRepair; k++) ForgeFor(run, w, o, day);

            // The Event Trader, on her days.
            if (WorldDirector.EventTraderMayCome(w) && (w.challenge == null || (day >= w.challengeDay + Workshop.ChallengeCheckDelay && w.challengeCheckedDay != day)))
                Trader(run, w, day);

            // Today's arrivals with new work, plus stories/repairs when they're due.
            int arrivals = GameTime.Arrivals(day, w.timeSeed, WorldDirector.ArrivalsDelta(w)).Length;
            for (int i = 0; i < arrivals + 2; i++)
            {
                bool newWork = w.visitsToday < arrivals && w.orders.Count < Workshop.MaxActiveOrders;
                var story = h.StoryDue(day);
                var worn = newWork ? h.swords.Find(r => r.ReturnDue(day) && r.storyTold && r.condition < BlacksmithHistory.RepairThreshold && RegularReady(w, h.Owner(r))) : null;
                var regular = newWork ? h.customers.Find(c => WantsNew(w, c)) : null;
                var acquaintance = newWork ? Acquaintance(w) : null;
                var kind = WorldDirector.ChooseVisit(w, new VisitContext
                {
                    newWorkDue = newWork, hasStory = story != null, hasWorn = worn != null, hasRegular = regular != null, hasAcquaintance = acquaintance != null,
                });
                if (kind == VisitKind.Nobody) { if (!newWork && story == null) break; continue; }
                if (kind == VisitKind.Story)
                {
                    story.storyTold = true;
                    if (run.firstReturn == 0) run.firstReturn = day;
                    run.returns++;
                    h.LogCameBack(story, day);
                    run.timeline.Add($"    RETURN: {h.Owner(story)?.name} comes back with {story.Label} (forged day {story.createdDay}, {story.ConditionText})");
                    h.ScheduleNextReturn(story, day);
                    continue;
                }
                if (kind == VisitKind.Repair)
                {
                    w.visitsToday++;
                    run.timeline.Add($"    REPAIR: {h.Owner(worn)?.name} brings {worn.Label} back worn ({worn.condition:0})");
                    h.TakeInForRepair(worn, day);
                    h.FinishRepair(worn, day);
                    h.ReturnToOwner(worn, day, repaired: true);
                    continue;
                }
                w.visitsToday++;
                CustomerOrder offer;
                CustomerRecord who;
                using (WorldRandom.Seeded(w.world.worldSeed, "gen", day, WorldDirector.VisitSalt(w) * 7 + (int)kind))
                {
                    switch (kind)
                    {
                        case VisitKind.Special: offer = WorldDirector.Special(w, out who, out _); run.specials++; break;
                        case VisitKind.Referral:
                            var rf = WorldDirector.ReferralDue(w);
                            if (rf.positive) run.referrals++; else run.badWord++;
                            offer = WorldDirector.Referred(w, rf, out who); break;
                        case VisitKind.Regular:
                            who = regular;
                            offer = CustomerOrder.FollowUp(regular, h.Find(regular.followUpForSwordId) ?? h.CarriedBy(regular), day, w.unlockedMetals);
                            WorldDirector.FinishFollowUp(w, offer, regular); break;
                        case VisitKind.Acquaintance: who = acquaintance; offer = WorldDirector.NewOrder(w, acquaintance); break;
                        default: offer = WorldDirector.Stranger(w, out who); break;
                    }
                }
                Offer(run, w, offer, who, kind, day);
            }

            // Evening: order what tomorrow needs (the bot keeps a few ingots of the metals it uses).
            foreach (var m in ForgeMaterials.All)
            {
                if (!w.IsUnlocked(m)) continue;
                int want = m.index == 0 ? 4 : 2;
                while (w.stock[m.index] + w.ordered[m.index] < want && w.gold >= m.price + 10 && w.OrderIngot(m.index) == null) { }
            }
            // Orders nobody came for by their deadline are closed by the next collect (see Collect).
        }
        run.rep = w.reputation;
        run.gold = w.gold;
        run.firstElf = w.world.raceFirstSeen[1];
        run.firstDwarf = w.world.raceFirstSeen[2];
        run.firstTrader = w.world.lastTraderVisitDay > 0 ? FirstTraderDay(run) : 0;
        run.notable = h.swords.Count(r => r.notable);
        int best = 0, cur = 0;
        for (int i = 0; i < run.weaponsAsked.Count; i++) { cur = i > 0 && run.weaponsAsked[i] == run.weaponsAsked[i - 1] ? cur + 1 : 1; best = Mathf.Max(best, cur); }
        run.maxWeaponRun = best;
        string problem = h.Validate() ?? w.ValidateOrders();
        Check(problem == null, $"{name}: history and order book consistent after {days} days{(problem != null ? $" ({problem})" : "")}");
        return run;
    }

    private static int FirstTraderDay(SaveRun run)
    {
        foreach (var l in run.timeline) if (l.Contains("EVENT TRADER")) { int i = run.timeline.IndexOf(l); for (int k = i; k >= 0; k--) if (run.timeline[k].StartsWith("--- Day ")) return int.Parse(run.timeline[k].Substring(8).Split(' ')[0]); }
        return 0;
    }

    private static bool RegularReady(Workshop w, CustomerRecord c) => c != null && c.recurring && w.day >= c.nextVisitDay;

    private static bool WantsNew(Workshop w, CustomerRecord c)
    {
        if (!RegularReady(w, c)) return false;
        var h = w.history;
        if (h.swords.Exists(r => r.ownerId == c.id && r.status == SwordStatus.NeedsRepair)) return false;
        if (w.orders.Exists(o => o.customerId == c.id && o.IsActive)) return false;
        if (c.followUpForSwordId != 0) return true;
        var carried = h.CarriedBy(c);
        return carried != null && carried.storyTold && carried.condition >= BlacksmithHistory.RepairThreshold && w.day - c.lastOrderDay >= 4;
    }

    private static CustomerRecord Acquaintance(Workshop w)
    {
        var h = w.history;
        var pool = h.customers.FindAll(c => !c.recurring && NpcStories.IsGenerated(c.archetypeIndex) && c.profile != null && c.profile.IsSet &&
                                            w.day >= c.nextVisitDay && w.day - c.profile.lastSeenDay >= 3 &&
                                            !h.swords.Exists(r => r.ownerId == c.id && r.status == SwordStatus.NeedsRepair) &&
                                            !w.orders.Exists(o => o.customerId == c.id));
        return pool.Count > 0 ? pool[Random.Range(0, pool.Count)] : null;
    }

    // ---- a customer with new work -----------------------------------------------------------------------------------
    private static void Offer(SaveRun run, Workshop w, CustomerOrder o, CustomerRecord who, VisitKind kind, int day)
    {
        run.weaponsAsked.Add(o.weaponType);
        string why = WorldDirector.WhyImpossible(w, o);
        if (why != null) run.impossible++;
        string tag = o.special ? "SPECIAL " : o.referredBy != 0 ? "REFERRAL " : kind == VisitKind.Regular ? "REGULAR " : kind == VisitKind.Acquaintance ? "AGAIN " : "";
        string desc = $"{tag}{who.race} {NpcStories.Describe(who)} ({NpcStories.Traits[who.profile.trait1].name}/{NpcStories.Traits[who.profile.trait2].name}) wants {o.NeedSummary()}" +
                      $"{(o.requiredMaterial != null ? $" in {o.requiredMaterial}" : "")}{(o.rush ? " RUSH" : "")}{(o.quantity > 1 ? $" x{o.quantity}" : "")} - {o.reward}g";
        run.offers.Add($"d{day} {who.race} {CustomerArchetype.All[o.archetypeIndex].title} {o.weaponType}");
        // The bot's judgement: it takes what it can reach with its metals, and not more than it can handle.
        bool take = why == null && w.orders.Count < Workshop.MaxActiveOrders && o.quantity <= 3 && CanTry(w, o);
        run.timeline.Add($"    {(take ? "ACCEPT" : "decline")}: {desc}{(why != null ? $"  !! IMPOSSIBLE: {why}" : "")}");
        if (take && o.special) run.timeline.Add($"      \"{o.requestText}\"");
        if (take && o.referredBy != 0) run.timeline.Add($"      \"{o.requestText}\"");
        if (!take)
        {
            run.declined++;
            WorldDirector.OnDeclined(w, o);
            w.AddReputation(Workshop.RepRefused);
            who.nextVisitDay = day + 3;
            return;
        }
        var rec = w.history.Customer(o.customerId) ?? w.history.AddCustomer(o, false);
        w.Accept(o, day, 10f);
        w.history.LogNewOrder(rec, day, o.previousSwordId, $"{o.Label}: {o.paperQuote}");
        for (int i = 0; i < o.quantity; i++) ForgeFor(run, w, o, day);
    }

    private static bool CanTry(Workshop w, CustomerOrder o)
    {
        var m = MetalFor(w, o, needStock: false);
        return m != null;
    }

    private static ForgeMaterialInfo MetalFor(Workshop w, CustomerOrder o, bool needStock)
    {
        ForgeMaterialInfo pick = null;
        foreach (var m in ForgeMaterials.All)
        {
            if (!w.IsUnlocked(m) || (needStock && w.stock[m.index] <= 0)) continue;
            if (o.requiredMaterial != null && m.name != o.requiredMaterial) continue;
            bool reach = WorldDirector.Need(o, 0) <= m.durCap - 3f && WorldDirector.Need(o, 1) <= m.balCap - 3f && WorldDirector.Need(o, 2) <= m.sharpCap - 3f;
            if (!reach) continue;
            if (pick == null || m.price < pick.price) pick = m; // the cheapest that can do it
        }
        return pick;
    }

    private static void ForgeFor(SaveRun run, Workshop w, CustomerOrder o, int day)
    {
        var m = MetalFor(w, o, needStock: true);
        if (m == null) { run.timeline.Add($"      (no ingot to forge {o.Label} today)"); return; }
        // Like a player looking at the result screen: a weapon that plainly won't do goes in the scrap crate and they try
        // once more (if there's an ingot for it).
        WorkpieceData best = null;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (attempt > 0 && MetalFor(w, o, needStock: true) != m) break;
            w.stock[m.index]--;
            var item = Forge(o.special ? Careful : Average, o.weaponType, m, WorldDirector.Need(o, 0), WorldDirector.Need(o, 1), WorldDirector.Need(o, 2));
            if (best != null) w.scrap.Add(best);
            best = item;
            if (Workshop.Judge(o, best) <= Workshop.Verdict.Satisfied) break;
        }
        var rec = w.history.RegisterForged(best, day);
        WorldDirector.CheckQuality(w, rec);
        string why = w.Assign(o, rec, day);
        run.timeline.Add($"      forged {rec.Label} ({m.name}) D{best.durability:0}/B{best.balance:0}/S{best.sharpness:0}{(best.cracked ? " CRACKED" : "")} -> {(why == null ? $"set aside for {o.Label}" : why)}");
    }

    private static void Collect(SaveRun run, Workshop w, CustomerOrder o, int day)
    {
        var h = w.history;
        var who = h.Customer(o.customerId);
        if (!w.IsReady(o))
        {
            if (day > o.deadlineDay)
            {
                WorldDirector.OnWalkedOff(w, o);
                w.AddReputation(Workshop.RepWalkedOff);
                w.Close(o, day, OrderStatus.Failed);
                run.walkedOff++;
                run.timeline.Add($"    WALKED OFF: {who?.name} gave up on {o.Label}");
            }
            else { o.returnDay = day + 1; }
            return;
        }
        var weapons = w.AssignedTo(o);
        var refusedW = weapons.FindAll(r => Workshop.Judge(o, r.sword) == Workshop.Verdict.Refused);
        if (refusedW.Count > 0)
        {
            foreach (var r in refusedW) w.Unassign(r, day);
            w.AddReputation(Workshop.RepRefusedWeapon);
            WorldDirector.OnWeaponRefused(w, o);
            run.refused++;
            run.timeline.Add($"    REFUSED: {who?.name} won't take {string.Join(",", refusedW.Select(r => r.Label))} for {o.Label} ({o.NeedSummary()})");
            o.returnDay = day + 1;
            // The bot tries once more if it can.
            ForgeFor(run, w, o, day);
            return;
        }
        int pay = 0, tip = 0; float rep = 0f;
        var worst = Workshop.Verdict.Excellent;
        foreach (var r in weapons) { pay += w.Payment(o, r.sword, out int t); tip += t; rep += Workshop.SatisfactionRep(o, r.sword); var v = Workshop.Judge(o, r.sword); if (v > worst) worst = v; }
        rep /= Mathf.Max(1, weapons.Count);
        if (rep > 0f) rep *= WorldDirector.RepMultiplier(o);
        bool firstSale = h.SoldCount == 0;
        if (who.purchases == 0 && !who.recurring && worst != Workshop.Verdict.Grudging) who.recurring = h.ShouldBecomeRecurring(Random.value);
        var delivered = w.Deliver(o, who, day);
        WorldDirector.OnDelivered(w, o, delivered, who, worst, firstSale);
        w.gold += pay + tip;
        w.AddReputation(rep);
        run.delivered++;
        string returns = string.Join(", ", delivered.Select(r => r.returnDay > 0 ? $"{r.Label} back day {r.returnDay}" : $"{r.Label} never returns"));
        run.timeline.Add($"    DELIVERED {o.Label} to {who.name}: {worst} - {pay + tip}g{(o.special ? " (SPECIAL!)" : "")}  [{returns}]");
    }

    // ---- the Event Trader -------------------------------------------------------------------------------------------
    private static void Trader(SaveRun run, Workshop w, int day)
    {
        WorldDirector.TraderVisited(w);
        var h = w.history;
        if (w.challenge == null)
        {
            var metal = w.NextLockedMetal;
            var c = WorldDirector.Challenge(w, metal);
            string why = WorldDirector.WhyImpossible(w, c);
            if (why != null) run.impossible++;
            var orla = h.customers.Find(x => x.archetypeIndex == CustomerArchetype.MerchantIndex) ?? h.AddCustomer(c, false);
            c.customerId = orla.id;
            w.challenge = c;
            w.Accept(c, day);
            c.deadlineDay = day + Workshop.ChallengeDays;
            w.challengeDay = day;
            w.challengeCheckedDay = day;
            run.timeline.Add($"    EVENT TRADER: challenge for {metal.name} - {c.NeedSummary()}{(why != null ? $"  !! IMPOSSIBLE: {why}" : "")}");
            // The bot has a few tries at it over the coming days (one now).
            ForgeChallenge(run, w, c, day);
            return;
        }
        var ch = w.challenge;
        w.challengeCheckedDay = day;
        var assigned = w.AssignedTo(ch);
        var rec = assigned.Count > 0 ? assigned[0] : null;
        if (rec != null && !ch.IsFulfilled(rec.sword)) { w.Unassign(rec, day); rec = null; }
        if (rec != null)
        {
            var metal = ForgeMaterials.Find(ch.unlocksMaterial);
            h.Sell(rec, h.Customer(ch.customerId), ch, day);
            rec.returnDay = -1;
            w.Unlock(metal);
            WorldDirector.NoteUnlock(w, metal);
            w.AddIngots(metal.index, 2);
            w.AddToSupply(metal);
            w.AddReputation(Workshop.RepChallenge);
            w.challenge = null;
            w.nextEventDay = WorldDirector.NextTraderDay(w);
            run.metalDay[metal.index] = day;
            run.timeline.Add($"    EVENT TRADER: {rec.Label} passes! {metal.name.ToUpper()} UNLOCKED");
        }
        else if (day >= w.challengeDay + Workshop.ChallengeDays)
        {
            w.Close(ch, day, OrderStatus.Failed);
            w.challenge = null;
            w.nextEventDay = WorldDirector.NextTraderDay(w);
            run.timeline.Add("    EVENT TRADER: challenge failed - she'll pass by again");
        }
        else
        {
            run.timeline.Add("    EVENT TRADER: looks in - nothing yet");
            ForgeChallenge(run, w, ch, day);
        }
    }

    private static void ForgeChallenge(SaveRun run, Workshop w, CustomerOrder c, int day)
    {
        if (w.AssignedTo(c).Count > 0) return;
        ForgeMaterialInfo m = null;
        foreach (var x in ForgeMaterials.All) if (w.IsUnlocked(x) && w.stock[x.index] > 0 && (m == null || x.maxQuality > m.maxQuality)) m = x;
        if (m == null) return;
        w.stock[m.index]--;
        var s = Forge(new Skill { name = "trying hard", perfect = 0.55f, good = 0.4f, anvilCatch = 0.95f, maxTouches = 4 }, c.weaponType, m, c.minDurability, c.minBalance, c.minSharpness);
        var rec = w.history.RegisterForged(s, day);
        bool ok = c.IsFulfilled(s);
        if (ok) w.Assign(c, rec, day);
        run.timeline.Add($"      challenge attempt {rec.Label} D{s.durability:0}/B{s.balance:0}/S{s.sharpness:0} {(ok ? "- good enough, set aside" : "- not good enough")}");
    }

    // ---- the bot at the stations (the same steps as BalanceTest, through the real station code) ---------------------
    private static Rating Timing(Skill s, ForgeMaterialInfo m)
    {
        float difficulty = Mathf.Sqrt(m.windowScale / m.speedScale);
        float p = Mathf.Clamp01(s.perfect * difficulty);
        float g = Mathf.Clamp01(s.good + (s.perfect - p) * 0.6f);
        float r = Random.value;
        return r < p ? Rating.Perfect : r < p + g ? Rating.Good : Rating.Bad;
    }

    private static WorkpieceData Forge(Skill s, WeaponType type, ForgeMaterialInfo m, float needDur, float needBal, float needSharp)
    {
        var w = Weapons.Get(type);
        var item = new WorkpieceData { weaponType = type, materialName = m.name };
        int hits = furnace.HitsFor(Timing(s, m));
        float dur = 0f, bal = 0f;
        for (int i = 0; i < hits; i++)
        {
            if (Random.value > s.anvilCatch) continue;
            float wantDur = needDur > 0f ? needDur : (needBal <= 0f && needSharp <= 0f ? 60f * w.durWeight : 0f);
            float wantBal = needBal > 0f ? needBal : (needDur <= 0f && needSharp <= 0f ? 60f * w.balWeight : 20f);
            bool toDur = (wantDur + 12f) - dur >= wantBal - bal;
            if (toDur) dur += w.durPerPoint; else bal += w.balPerPoint;
        }
        item.durability = Mathf.Min(100f, dur);
        item.balance = Mathf.Min(100f, bal);
        item.CapToMaterial();
        barrel.Apply(item, Timing(s, m), m);
        item.CapToMaterial();
        if (w.hasEdge)
        {
            stone.Begin();
            float wantSharp = needSharp > 0f ? needSharp + 5f : 45f;
            while (stone.Touches < s.maxTouches && item.sharpness < wantSharp && stone.Touches < stone.maxTouches)
            {
                var t = Timing(s, m);
                if (stone.Touches >= 2 && t == Rating.Good && Random.value < 0.3f) t = Rating.Bad;
                stone.ApplyTouch(item, t, m);
                item.CapToMaterial();
            }
        }
        foreach (var part in w.hilt)
        {
            var t = Timing(s, m);
            bench.Apply(item, t, t == Rating.Perfect ? 0.1f : t == Rating.Good ? 0.4f : 0.9f);
            item.CapToMaterial();
        }
        return item;
    }

    // ================================================================================================ comparing saves

    private static void WriteRun(SaveRun r)
    {
        report.AppendLine();
        report.AppendLine($"==================== {r.name} (seed {r.seed}) ====================");
        report.AppendLine(WorldDirector.Describe(r.shop));
        foreach (var l in r.timeline) report.AppendLine(l);
        report.AppendLine($"SUMMARY {r.name}: rep {r.rep:0}, gold {r.gold}, delivered {r.delivered}, refused {r.refused}, walked off {r.walkedOff}, declined {r.declined}, " +
                          $"events {r.events} (first day {r.firstEvent}), elves day {r.firstElf}, dwarves day {r.firstDwarf}, trader first day {r.firstTrader}, " +
                          $"iron day {r.metalDay[1]}, steel day {r.metalDay[2]}, specials {r.specials}, referrals +{r.referrals}/-{r.badWord}, returns {r.returns} (first day {r.firstReturn}), " +
                          $"notable {r.notable}, longest same-weapon run {r.maxWeaponRun}, impossible offers {r.impossible}");
    }

    private static void Compare(List<SaveRun> runs, int days)
    {
        var s = WorldSettings.Current;
        report.AppendLine();
        report.AppendLine("==================== COMPARISON ====================");
        foreach (var r in runs)
        {
            Check(r.impossible == 0, $"{r.name}: no impossible order or challenge offered ({r.impossible})");
            Check(r.firstElf > 0 && r.firstElf <= s.raceGuaranteeDays.y + 1, $"{r.name}: elves met by day {s.raceGuaranteeDays.y + 1} (day {r.firstElf})");
            Check(r.firstDwarf > 0 && r.firstDwarf <= s.raceGuaranteeDays.y + 1, $"{r.name}: dwarves met by day {s.raceGuaranteeDays.y + 1} (day {r.firstDwarf})");
            Check(r.firstElf >= s.otherRacesFromDay && r.firstDwarf >= s.otherRacesFromDay, $"{r.name}: day 1 is humans only");
            Check(r.firstEvent > 0 && r.firstEvent <= s.firstEventGuaranteeDay + 2, $"{r.name}: a city event by day {s.firstEventGuaranteeDay + 2} (day {r.firstEvent})");
            Check(r.firstTrader > 0 && r.firstTrader <= s.firstEventTraderDays.y + 1, $"{r.name}: the Event Trader by day {s.firstEventTraderDays.y + 1} (day {r.firstTrader})");
            Check(r.firstReturn > 0 && r.firstReturn <= 10, $"{r.name}: a weapon came back by day 10 (day {r.firstReturn})");
            Check(r.maxWeaponRun <= 4, $"{r.name}: no long run of the same weapon asked for ({r.maxWeaponRun})");
            var evs = r.shop.world.events;
            bool backToBack = false;
            for (int i = 1; i < evs.Count; i++) if (evs[i].type == evs[i - 1].type) backToBack = true;
            Check(!backToBack, $"{r.name}: no city event straight after itself ({string.Join(", ", evs.Select(e => $"{e.type}@{e.startDay}"))})");
            Check(r.events >= 2 && r.events <= days / 2 + 1, $"{r.name}: events are spaced - some, not every day ({r.events} in {days} days)");
            int quiet = 0;
            for (int day = 1; day <= days; day++) if (r.shop.world.EventOn(day) == null) quiet++;
            Check(quiet >= days / 3, $"{r.name}: plenty of ordinary days between events ({quiet}/{days})");
            Check(r.delivered >= days / 2, $"{r.name}: the economy works - {r.delivered} orders delivered");
        }
        // Different stories: the first days' visitors, the event calendars, the first weapon asked for.
        var firstWeek = runs.Select(r => string.Join("|", r.offers.Where(o => int.Parse(o.Substring(1).Split(' ')[0]) <= 5))).ToList();
        Check(firstWeek.Distinct().Count() == runs.Count, "every save's first five days bring a different sequence of customers");
        var calendars = runs.Select(r => string.Join(",", r.shop.world.events.Select(e => $"{e.type}@{e.startDay}"))).ToList();
        Check(calendars.Distinct().Count() >= runs.Count - 1, $"city event calendars differ ({calendars.Distinct().Count()} distinct of {runs.Count})");
        var elfDays = runs.Select(r => r.firstElf).Distinct().Count();
        var dwarfDays = runs.Select(r => r.firstDwarf).Distinct().Count();
        var traderDays = runs.Select(r => r.firstTrader).Distinct().Count();
        Check(elfDays + dwarfDays + traderDays >= 5, $"guaranteed content lands on different days in different saves (elf {elfDays}, dwarf {dwarfDays}, trader {traderDays} distinct)");
        // ...but the same game: similar economies.
        float minRep = runs.Min(r => r.rep), maxRep = runs.Max(r => r.rep);
        Check(maxRep - minRep < 35f, $"reputation ends in a similar range for the same skill ({minRep:0}..{maxRep:0})");
        report.AppendLine("First weapons asked for: " + string.Join("  |  ", runs.Select(r => $"{r.name}: {string.Join(" ", r.weaponsAsked.Take(8))}")));
    }

    // The same seed played twice gives the very same save (nothing depends on anything but the save itself).
    private static void Determinism(int seed, int days)
    {
        var a = Play("determinism A", seed, days);
        var b = Play("determinism B", seed, days);
        bool same = a.timeline.Count == b.timeline.Count && a.timeline.SequenceEqual(b.timeline);
        if (!same)
            for (int i = 0; i < Mathf.Min(a.timeline.Count, b.timeline.Count); i++)
                if (a.timeline[i] != b.timeline[i]) { report.AppendLine($"first difference at line {i}:\n  {a.timeline[i]}\n  {b.timeline[i]}"); break; }
        Check(same, "one seed replays identically (determinism)");
    }

    // Save mid-game and load it back: every world decision survives - events, referrals, windows, the standing offer -
    // and the next visit rolls the same way (no reroll by reloading).
    private static void SaveRoundTrip(int seed, string path)
    {
        Workshop saved = null;
        Play("round trip", seed, 20, stopAtDay: 9, atStop: w => saved = w);
        Check(saved != null, "played to day 9 for the save test");
        if (saved == null) return;
        var offer = new CustomerOrder { customerName = "Test", weaponType = WeaponType.Dagger, minBalance = 40f, reward = 20 };
        saved.world.hasStandingOffer = true;
        saved.world.standingOffer = offer;
        SaveSystem.Save(saved);
        var loaded = new Workshop();
        bool ok = SaveSystem.TryLoad(loaded, out _);
        Check(ok, "the save loads");
        if (!ok) return;
        var a = saved.world; var b = loaded.world;
        Check(a.worldSeed == b.worldSeed, "world seed kept");
        Check(a.events.Count == b.events.Count && a.events.Zip(b.events, (x, y) => x.type == y.type && x.startDay == y.startDay && x.endDay == y.endDay).All(t => t),
              $"city events kept ({a.events.Count})");
        Check(a.referrals.Count == b.referrals.Count, $"referrals kept ({a.referrals.Count})");
        Check(a.raceFirstSeen.SequenceEqual(b.raceFirstSeen) && a.weaponIntroDay.SequenceEqual(b.weaponIntroDay) && a.raceGuaranteeDay.SequenceEqual(b.raceGuaranteeDay), "progression windows kept");
        Check(a.raceStanding.SequenceEqual(b.raceStanding) && a.recentWeapons.SequenceEqual(b.recentWeapons), "standing and recent history kept");
        Check(b.hasStandingOffer && b.standingOffer != null && b.standingOffer.customerName == "Test" && b.standingOffer.weaponType == WeaponType.Dagger, "the offer standing at the table is kept");
        Check(saved.nextEventDay == loaded.nextEventDay, "the Event Trader's day kept");
        var ret1 = saved.history.swords.Where(r => r.returnDay != 0).Select(r => r.returnDay);
        var ret2 = loaded.history.swords.Where(r => r.returnDay != 0).Select(r => r.returnDay);
        Check(ret1.SequenceEqual(ret2), "every weapon's return day kept");
        Check(WorldDirector.Weather(saved) == WorldDirector.Weather(loaded), "today's weather is the same after loading");
        var ctx = new VisitContext { newWorkDue = true };
        int salt = saved.world.visitRolls;
        var k1 = WorldDirector.ChooseVisit(saved, ctx);
        var k2 = WorldDirector.ChooseVisit(loaded, ctx);
        Check(k1 == k2 && salt == loaded.world.visitRolls - 1, $"the next visit rolls the same after loading ({k1} / {k2})");
        var s1 = WorldDirector.RollSupply(saved); var s2 = WorldDirector.RollSupply(loaded);
        Check(s1.SequenceEqual(s2), "the supplier's stock is the same after loading");
    }
}
