using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Balance simulation (edit mode, no scene): plays thousands of weapons through the REAL station code (ForgeStation,
// anvil points per WeaponInfo, QuenchBarrel, Grindstone, WorkbenchStation, the metal caps) for three kinds of
// player, then plays whole days of the economy (orders, ingots, the evening trader, the Event Trader, reputation).
//   bad     - mostly misses timings, many anvil points slip away, gives up on the grindstone early (but reads the order)
//   average - decent timings, catches most anvil points, a few grindstone touches
//   skilled - mostly perfect, catches every point, and splits the anvil points toward what's asked for
// Run: Unity.exe -batchmode -projectPath <p> -executeMethod BalanceTest.Run -quit   ([BAL] lines)
public static class BalanceTest
{
    private class Skill
    {
        public string name;
        public float perfect, good;   // chance of each timing result (rest = bad), before the metal's difficulty
        public float anvilCatch;      // chance of catching an anvil point before it vanishes
        public int maxTouches;        // grindstone touches they're willing to try
        public bool plans;            // splits anvil points toward the order's needs (else roughly half and half)
    }

    private static readonly Skill[] Skills =
    {
        new Skill { name = "bad", perfect = 0.10f, good = 0.40f, anvilCatch = 0.60f, maxTouches = 4, plans = true },
        new Skill { name = "average", perfect = 0.30f, good = 0.50f, anvilCatch = 0.85f, maxTouches = 3, plans = true },
        new Skill { name = "skilled", perfect = 0.65f, good = 0.30f, anvilCatch = 0.97f, maxTouches = 4, plans = true },
    };

    private static ForgeStation furnace;
    private static QuenchBarrel barrel;
    private static Grindstone stone;
    private static WorkbenchStation bench;
    private static bool ok = true;

    private static void Check(bool condition, string what)
    {
        Debug.Log($"[BAL] {(condition ? "ok  " : "FAIL")} {what}");
        ok &= condition;
    }

    public static void Run()
    {
        ok = true;
        Random.InitState(1234);
        var go = new GameObject("BalanceSim");
        furnace = go.AddComponent<ForgeStation>();
        barrel = go.AddComponent<QuenchBarrel>();
        stone = go.AddComponent<Grindstone>();
        bench = go.AddComponent<WorkbenchStation>();
        try
        {
            QualityByStage();
            OrdersBySkill();
            Challenges();
            Economy();
        }
        finally { Object.DestroyImmediate(go); }
        Debug.Log($"[BAL] RESULT: {(ok ? "PASS" : "FAIL")}");
    }

    // ---- one weapon --------------------------------------------------------------------------------------

    private static Rating Timing(Skill s, ForgeMaterialInfo m)
    {
        // Harder metals (smaller windows, faster pointers) turn some perfects into goods and some goods into bads.
        float difficulty = Mathf.Sqrt(m.windowScale / m.speedScale); // a player adapts - narrower windows cost less than proportionally
        float p = Mathf.Clamp01(s.perfect * difficulty);
        float g = Mathf.Clamp01(s.good + (s.perfect - p) * 0.6f);
        float r = Random.value;
        return r < p ? Rating.Perfect : r < p + g ? Rating.Good : Rating.Bad;
    }

    // Forges one weapon aiming at the given needs (0 = don't care).
    private static WorkpieceData Forge(Skill s, WeaponType type, ForgeMaterialInfo m, float needDur, float needBal, float needSharp)
    {
        var w = Weapons.Get(type);
        var item = new WorkpieceData { weaponType = type, materialName = m.name };
        int hits = furnace.HitsFor(Timing(s, m));

        // Anvil: each caught point goes to Durability or Balance.
        float dur = 0f, bal = 0f;
        for (int i = 0; i < hits; i++)
        {
            if (Random.value > s.anvilCatch) continue;
            bool toDur;
            if (!s.plans) toDur = i % 2 == 0;
            else
            {
                // Planned: what's still missing (weighted by what the weapon is judged on when nothing is asked).
                float wantDur = needDur > 0f ? needDur : (needBal <= 0f && needSharp <= 0f ? 60f * w.durWeight : 0f);
                float wantBal = needBal > 0f ? needBal : (needDur <= 0f && needSharp <= 0f ? 60f * w.balWeight : 20f);
                toDur = (wantDur + 12f) - dur * 1f >= wantBal - bal; // keep a little Durability back for grinding wear
            }
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
                // Each touch is harder than the last (the good window shrinks).
                var t = Timing(s, m);
                if (stone.Touches >= 2 && t == Rating.Good && Random.value < 0.3f) t = Rating.Bad;
                stone.ApplyTouch(item, t, m);
                item.CapToMaterial();
            }
        }

        foreach (var part in w.hilt)
        {
            var t = Timing(s, m);
            float error = t == Rating.Perfect ? 0.1f : t == Rating.Good ? 0.4f : 0.9f;
            bench.Apply(item, t, error);
            item.CapToMaterial();
        }
        return item;
    }

    private static float Avg(IEnumerable<float> xs) => xs.Any() ? xs.Average() : 0f;

    // Unguided (no order): how good does the weapon get, per skill and metal?
    private static void QualityByStage()
    {
        var gradeOf = new Dictionary<string, Dictionary<string, float>>();
        foreach (var s in Skills)
            foreach (var m in ForgeMaterials.All)
            {
                var items = new List<WorkpieceData>();
                for (int i = 0; i < 1500; i++) items.Add(Forge(s, (WeaponType)(i % 5), m, 0f, 0f, 0f));
                float a = items.Count(x => x.Grade == "A" || x.Grade == "S") / (float)items.Count;
                float sGrade = items.Count(x => x.Grade == "S") / (float)items.Count;
                float perfect = items.Count(x => x.durability >= 95f && x.balance >= 95f) / (float)items.Count;
                Debug.Log($"[BAL] {s.name,-8} {m.name,-6}: dur {Avg(items.Select(x => x.durability)),4:0} bal {Avg(items.Select(x => x.balance)),4:0} sharp {Avg(items.Where(x => x.Weapon.hasEdge).Select(x => x.sharpness)),4:0}" +
                          $"  grade A+ {a * 100,3:0}%  S {sGrade * 100,3:0}%  (95+/95+ {perfect * 100:0}%)");
                if (!gradeOf.ContainsKey(s.name)) gradeOf[s.name] = new Dictionary<string, float>();
                gradeOf[s.name][m.name] = a;
            }
        Check(gradeOf["bad"]["Copper"] < 0.05f, "a bad player almost never makes an A-grade weapon");
        Check(gradeOf["average"]["Iron"] < 0.35f, "an average player makes A-grade weapons only sometimes");
        Check(gradeOf["skilled"]["Steel"] > gradeOf["average"]["Steel"] + 0.2f, "skill clearly shows in the results");

        // One perfect stage can't carry a weapon: a perfect furnace alone, everything else average.
        var avg = Skills[1];
        var lucky = new List<WorkpieceData>();
        for (int i = 0; i < 1500; i++)
        {
            var w = Forge(avg, WeaponType.Sword, ForgeMaterials.All[2], 0f, 0f, 0f);
            lucky.Add(w);
        }
        float luckyA = lucky.Count(x => x.Grade == "A" || x.Grade == "S") / (float)lucky.Count;
        Check(luckyA < 0.4f, $"average play in steel: {luckyA * 100:0}% A-grade - excellence needs strong play across the stages");
    }

    private static float TypicalRep(Skill s) => s.name == "bad" ? 20f : s.name == "average" ? 45f : 70f;

    // Real generated orders, per skill: how often can the player deliver?
    private static void OrdersBySkill()
    {
        foreach (var metals in new[] { new[] { "Copper" }, new[] { "Copper", "Iron" }, new[] { "Copper", "Iron", "Steel" } })
        {
            var line = new List<string>();
            var rate = new Dictionary<string, float>();
            foreach (var s in Skills)
            {
                int done = 0, total = 800;
                for (int i = 0; i < total; i++)
                {
                    var o = CustomerOrder.Generate(4 + i % 6, null, RaceInfo.Pick(), metals, TypicalRep(s)); // the jobs a smith of their standing gets
                    var m = ForgeMaterials.Find(o.requiredMaterial ?? metals[metals.Length - 1]);
                    // Two attempts, like a player who retries a failed piece once.
                    for (int attempt = 0; attempt < 2; attempt++)
                        if (o.IsFulfilled(Forge(s, o.weaponType, m, o.minDurability, o.minBalance, o.minSharpness))) { done++; break; }
                }
                rate[s.name] = done / (float)total;
                line.Add($"{s.name} {rate[s.name] * 100:0}%");
            }
            Debug.Log($"[BAL] orders deliverable with {string.Join("+", metals)}: {string.Join("   ", line)}");
            Check(rate["bad"] >= 0.3f, $"a bad player can still deliver a fair share of orders ({string.Join("+", metals)})");
            Check(rate["skilled"] >= 0.8f, $"a skilled player can deliver nearly every order ({string.Join("+", metals)})");
        }
    }

    // Event Trader challenges, against each unlock step.
    private static void Challenges()
    {
        foreach (var unlocked in new[] { new[] { "Copper" }, new[] { "Copper", "Iron" } })
        {
            float cap = unlocked.Max(n => ForgeMaterials.Find(n).maxQuality);
            var best = ForgeMaterials.Find(unlocked[unlocked.Length - 1]);
            var line = new List<string>();
            var rate = new Dictionary<string, float>();
            foreach (var s in Skills)
            {
                int done = 0, total = 800;
                for (int i = 0; i < total; i++)
                {
                    var c = CustomerOrder.Challenge((WeaponType)(i % 5), cap, ForgeMaterials.All[unlocked.Length]);
                    // Up to three tries across the days she waits.
                    for (int attempt = 0; attempt < 3; attempt++)
                        if (c.IsFulfilled(Forge(s, c.weaponType, best, c.minDurability, c.minBalance, c.minSharpness))) { done++; break; }
                }
                rate[s.name] = done / (float)total;
                line.Add($"{s.name} {rate[s.name] * 100:0}%");
            }
            var sample = CustomerOrder.Challenge(WeaponType.Dagger, cap, ForgeMaterials.All[unlocked.Length]);
            Debug.Log($"[BAL] challenge to unlock {ForgeMaterials.All[unlocked.Length].name} (e.g. {sample.NeedSummary()}): {string.Join("   ", line)} (3 tries)");
            Check(rate["bad"] < 0.25f, "the Event Trader's challenge is not a gift for a bad player");
            Check(rate["skilled"] >= 0.6f, "a skilled player can win it");
        }
    }

    // Whole days: orders (up to the daily limit), ingots from store, the evening trader, the Event Trader.
    private static void Economy()
    {
        const int Runs = 30; // one run is far too noisy to judge by - report the averages (and one run's diary)
        foreach (var s in Skills)
        {
            var golds = new List<float>(); var reps = new List<float>(); var rep4 = new List<float>();
            var unlocks = new List<float>(); var metalCounts = new List<float>();
            float sumDelivered = 0, sumFailed = 0, sumRefused = 0;
            for (int run = 0; run < Runs; run++)
            {
            var shop = new Workshop();
            var log = new List<string>();
            int unlockDay = 0, refused = 0, delivered = 0, failed = 0;
            for (int day = 1; day <= 12; day++)
            {
                shop.day = day;
                shop.ordersToday = 0;
                if (day > 1) { shop.ReceiveDelivery(); shop.CollectCrate(); } // yesterday's orders, unloaded first thing
                // Event Trader: a challenge now and then; a skilled smith tries it.
                if (shop.challenge == null && day >= shop.nextEventDay && shop.NextLockedMetal != null)
                {
                    shop.challenge = CustomerOrder.Challenge((WeaponType)Random.Range(0, 5), shop.BestQualityCap, shop.NextLockedMetal);
                    shop.challengeDay = day;
                }
                if (shop.challenge != null && day >= shop.challengeDay + Workshop.ChallengeCheckDelay)
                {
                    var c = shop.challenge;
                    var metal = BestInStore(shop);
                    if (metal != null)
                    {
                        shop.stock[metal.index]--;
                        if (c.IsFulfilled(Forge(s, c.weaponType, metal, c.minDurability, c.minBalance, c.minSharpness)))
                        {
                            var unlock = ForgeMaterials.Find(c.unlocksMaterial);
                            shop.Unlock(unlock);
                            shop.stock[unlock.index] += 2;
                            shop.AddReputation(Workshop.RepChallenge);
                            if (unlockDay == 0) unlockDay = day;
                            log.Add($"d{day}: {unlock.name} unlocked");
                            shop.challenge = null;
                            shop.nextEventDay = day + Random.Range(Workshop.EventTraderGap.x, Workshop.EventTraderGap.y + 1);
                        }
                    }
                    if (shop.challenge != null && day >= shop.challengeDay + Workshop.ChallengeDays)
                    {
                        shop.challenge = null;
                        shop.nextEventDay = day + Random.Range(Workshop.EventTraderGap.x, Workshop.EventTraderGap.y + 1);
                    }
                }

                for (int n = 0; n < 4; n++)
                {
                    var o = CustomerOrder.Generate(day, null, RaceInfo.Pick(), shop.unlockedMetals, shop.reputation);
                    var metal = o.requiredMaterial != null ? ForgeMaterials.Find(o.requiredMaterial) : BestInStore(shop, o.HighestNeed);
                    if (metal == null || shop.stock[metal.index] == 0)
                    {
                        shop.AddReputation(Workshop.RepRefused); refused++; continue; // no ore - has to turn them away
                    }
                    shop.ordersToday++;
                    bool done = false;
                    for (int attempt = 0; attempt < 3 && !done && shop.stock[metal.index] > 0; attempt++)
                    {
                        shop.stock[metal.index]--;
                        var w = Forge(s, o.weaponType, metal, o.minDurability, o.minBalance, o.minSharpness);
                        if (o.IsFulfilled(w))
                        {
                            int pay = shop.Payment(o, w, out int tip);
                            shop.gold += pay + tip;
                            shop.AddReputation(Workshop.SatisfactionRep(o, w));
                            done = true;
                            delivered++;
                        }
                    }
                    if (!done) { shop.AddReputation(Workshop.RepWalkedOff); failed++; } // they wait, then leave angry
                }

                // The Material Order Book: keep ~7 ingots in store or on the way, the best metal the purse allows, saving a little.
                int cheapest = System.Array.FindIndex(ForgeMaterials.All, shop.IsUnlocked);
                for (int i = ForgeMaterials.All.Length - 1; i >= 0; i--)
                    while (shop.IsUnlocked(ForgeMaterials.All[i]) && shop.Supply[i] > 0 && shop.TotalStock + shop.OrderedTotal < 7 &&
                           shop.gold >= ForgeMaterials.All[i].price + (i == cheapest ? 0 : 20))
                        shop.OrderIngot(i);

                log.Add($"d{day}: {shop.gold}g rep {shop.reputation:0} ore {string.Join("/", shop.stock)}");
                if (day == 4) rep4.Add(shop.reputation);
            }
            if (run == 0) Debug.Log($"[BAL] economy {s.name} (one run): delivered {delivered}, failed {failed}, turned away {refused} | " + string.Join(" | ", log));
            golds.Add(shop.gold); reps.Add(shop.reputation); unlocks.Add(unlockDay); metalCounts.Add(shop.unlockedMetals.Count);
            sumDelivered += delivered; sumFailed += failed; sumRefused += refused;
            }

            float gold = Avg(golds), rep = Avg(reps), r4 = Avg(rep4), metals = Avg(metalCounts);
            float unlockedBy9 = unlocks.Count(d => d > 0 && d <= 9) / (float)Runs;
            float avgDelivered = sumDelivered / Runs, avgFailed = sumFailed / Runs, avgRefused = sumRefused / Runs;
            Debug.Log($"[BAL] economy {s.name} (avg of {Runs}): day 12 gold {gold:0} (min {golds.Min():0}), rep day4 {r4:0} -> day12 {rep:0}, " +
                      $"metals {metals:0.0}, first unlock by day 9 in {unlockedBy9 * 100:0}% | delivered {avgDelivered:0.0} failed {avgFailed:0.0} turned away {avgRefused:0.0}");
            if (s.name == "skilled")
            {
                Check(unlockedBy9 >= 0.7f, $"a skilled smith usually unlocks a new metal within the first days ({unlockedBy9 * 100:0}%)");
                Check(r4 < 70f, $"reputation isn't skyrocketing in the first days ({r4:0} on day 4)");
                Check(rep >= 65f && rep < 95f, $"a skilled smith's reputation grows, but not to the top in 12 days ({rep:0})");
                Check(gold > 150, $"a skilled smith makes money ({gold:0} gold)");
            }
            if (s.name == "average")
            {
                Check(gold > 40 && avgDelivered > avgFailed * 2f, $"an average smith gets by ({gold:0} gold, {avgDelivered:0} delivered / {avgFailed:0} failed)");
                Check(rep >= 25f, $"an average smith keeps a fair name ({rep:0})");
            }
            if (s.name == "bad") Check(avgDelivered >= 5 && golds.Min() >= 0, $"a bad smith is never stuck and still delivers some work ({avgDelivered:0.0} in 12 days)");
        }
    }

    // The metal to use for an order: the cheapest in store whose ceiling comfortably clears what's asked.
    private static ForgeMaterialInfo BestInStore(Workshop shop, float need = 200f)
    {
        ForgeMaterialInfo pick = null;
        foreach (var m in ForgeMaterials.All)
        {
            if (!shop.IsUnlocked(m) || shop.stock[m.index] == 0) continue;
            if (pick == null) pick = m;
            if (m.maxQuality >= need + 6f) return m;
            pick = m;
        }
        return pick;
    }
}
