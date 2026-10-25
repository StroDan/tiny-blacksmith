using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

// Edit-mode checks for the progression pass: material unlocks and the starting store, the Material Order Book
// (daily supply, next-day delivery crate, storage-full leftovers), the Event Trader's challenge, reputation rules, and random customer returns (rolled once at the sale,
// saved, never re-rolled). No scene, no Play mode. Logs [PROG] lines and a final RESULT line.
// Run via: Unity.exe -batchmode -quit -projectPath <path> -executeMethod ProgressionTest.Run -logFile <log>
public static class ProgressionTest
{
    private static int failures;

    private static void Check(bool ok, string what)
    {
        if (!ok) failures++;
        Debug.Log($"[PROG] {(ok ? "ok  " : "FAIL")} {what}");
    }

    public static void Run()
    {
        failures = 0;
        Random.InitState(99);
        string savePath = Path.Combine(Path.GetTempPath(), "tb_progression_test_save.json");
        string oldEnv = System.Environment.GetEnvironmentVariable("TB_SAVE_PATH");
        System.Environment.SetEnvironmentVariable("TB_SAVE_PATH", savePath);
        try
        {
            Materials();
            Supplier();
            Delivery();
            EventTrader();
            Reputation();
            Returns();
            SaveRoundTrip();
        }
        catch (System.Exception e) { failures++; Debug.LogError("[PROG] exception: " + e); }
        finally
        {
            if (File.Exists(savePath)) File.Delete(savePath);
            System.Environment.SetEnvironmentVariable("TB_SAVE_PATH", oldEnv);
        }
        Debug.Log($"[PROG] RESULT: {(failures == 0 ? "PASS" : $"FAIL ({failures})")}");
    }

    private static void Materials()
    {
        var shop = new Workshop();
        Check(shop.unlockedMetals.Count == 1, $"a new game knows exactly one metal ({string.Join(",", shop.unlockedMetals)})");
        Check(shop.TotalStock == Workshop.StartingOre && Workshop.StartingOre <= 8, $"the starting store is limited ({shop.TotalStock} ingots)");
        var copper = ForgeMaterials.All[0]; var iron = ForgeMaterials.All[1]; var steel = ForgeMaterials.All[2];
        Check(copper.price < iron.price && iron.price < steel.price, $"rarer metals cost more ({copper.price}/{iron.price}/{steel.price})");
        Check(copper.windowScale > iron.windowScale && iron.windowScale > steel.windowScale && steel.speedScale > copper.speedScale,
              "harder metals have tighter, faster timing windows");
        Check(copper.maxQuality < iron.maxQuality && iron.maxQuality < steel.maxQuality, "harder metals have a higher quality ceiling");
        // Orders never ask for a locked metal, nor for more than the known metals can reach.
        bool ok = true;
        for (int i = 0; i < 500; i++)
        {
            var o = CustomerOrder.Generate(1 + i % 12, null, RaceInfo.Pick(), shop.unlockedMetals, 100f);
            if (o.requiredMaterial != null && !shop.unlockedMetals.Contains(o.requiredMaterial)) ok = false;
            if (o.HighestNeed > copper.maxQuality) ok = false;
        }
        Check(ok, "copper-only orders never demand a locked metal or an unreachable quality");
    }

    private static void Supplier()
    {
        var shop = new Workshop();
        bool onlyUnlocked = true, limited = true;
        for (int i = 0; i < 200; i++)
        {
            var offer = shop.RollSupply();
            for (int m = 1; m < offer.Length; m++) if (offer[m] != 0) onlyUnlocked = false;
            if (offer[0] < 1 || offer[0] > 8) limited = false;
        }
        Check(onlyUnlocked, "the order book lists no locked metal");
        Check(limited, "the supplier has a limited amount each day");
        shop.Unlock(ForgeMaterials.All[1]);
        var offers = Enumerable.Range(0, 200).Select(_ => shop.RollSupply()).ToList();
        Check(offers.All(o => o[1] >= 1 && o[1] <= 3 && o[2] == 0), "after unlocking iron the supplier carries iron (a few), still no steel");
        Check(offers.Select(o => o[0]).Distinct().Count() > 1, "the daily stock varies from day to day");
        var today = shop.Supply;
        Check(ReferenceEquals(today, shop.Supply), "one supply roll per day (looking again doesn't re-roll it)");
    }

    // Day 1: order copper -> paid at once, nothing in the store yet. Day 2: it's in the crate; opening it fills the bin.
    // Storage nearly full: only what fits moves, the rest stays in the crate (and in the save).
    private static void Delivery()
    {
        var shop = new Workshop();
        int gold0 = shop.gold, stock0 = shop.stock[0], supply0 = shop.Supply[0];
        string why = shop.OrderIngot(0) ?? shop.OrderIngot(0);
        Check(why == null && shop.gold == gold0 - 2 * ForgeMaterials.All[0].price, $"ordering two copper ingots is paid at once ({gold0} -> {shop.gold} gold)");
        Check(shop.stock[0] == stock0 && shop.ordered[0] == 2 && shop.Supply[0] == supply0 - 2, "...but nothing arrives in the store yet (2 pending, today's supply down by 2)");
        Check(shop.OrderIngot(1) != null && shop.ordered[1] == 0, "a locked metal can't be ordered");
        Check(new Workshop { gold = 5 }.OrderIngot(0) != null, "can't order without the gold");
        while (shop.Supply[0] > 0) { shop.gold = 999; shop.OrderIngot(0); }
        Check(shop.OrderIngot(0) != null, "the day's supply runs out");

        // The pending order survives a save.
        SaveSystem.Save(shop, null);
        var loaded = new Workshop();
        Check(SaveSystem.TryLoad(loaded, out _) && loaded.ordered[0] == shop.ordered[0] && loaded.Supply[0] == shop.supply[0],
              $"the pending delivery ({loaded.ordered[0]}) and today's remaining supply are saved");

        // The next morning.
        int pending = shop.ordered[0];
        shop.day++;
        shop.ReceiveDelivery();
        Check(shop.crate[0] == pending && shop.OrderedTotal == 0 && shop.stock[0] == stock0, $"day 2: the crate holds the delivery ({shop.crate[0]}), the store is unchanged until it's opened");
        Check(shop.Supply[0] > 0, "day 2: fresh supply in the order book");

        // Storage nearly full: the bin has room for 2, the crate holds 5 -> 2 move in, 3 stay.
        shop.stock[0] = Workshop.BinCapacity - 2;
        shop.crate[0] = 5;
        var moved = shop.CollectCrate();
        Check(moved[0] == 2 && shop.stock[0] == Workshop.BinCapacity && shop.crate[0] == 3, $"storage full: {moved[0]} moved in, {shop.crate[0]} stay in the crate (nothing lost)");
        Check(!shop.CrateCanUnload, "...and the crate can't unload more until there's room");
        SaveSystem.Save(shop, null);
        loaded = new Workshop();
        Check(SaveSystem.TryLoad(loaded, out _) && loaded.crate[0] == 3 && loaded.stock[0] == Workshop.BinCapacity, "the half-emptied crate is saved");
        shop.stock[0] -= 4; // four weapons forged
        shop.CollectCrate();
        Check(shop.CrateTotal == 0 && shop.stock[0] == Workshop.BinCapacity - 1, "with room again the rest comes out and the crate is empty");

        // Nothing to work, nothing coming, no gold: the supplier leaves two ingots on credit.
        var broke = new Workshop { gold = 0 };
        broke.stock[0] = 0;
        Check(broke.ReceiveDelivery() && broke.crate[0] == 2, "a broke smith with an empty store gets two ingots on credit");
        var fine = new Workshop();
        Check(!fine.ReceiveDelivery() && fine.CrateTotal == 0, "...nobody else does");

        // An unlock goes straight into today's supply; the gift ingots overflow into the crate when the bin is full.
        var s3 = new Workshop();
        s3.Unlock(ForgeMaterials.All[1]);
        s3.AddToSupply(ForgeMaterials.All[1]);
        Check(s3.Supply[1] > 0, "a metal unlocked today can be ordered today");
        s3.stock[1] = Workshop.BinCapacity - 1;
        s3.AddIngots(1, 2);
        Check(s3.stock[1] == Workshop.BinCapacity && s3.crate[1] == 1, "gift ingots that don't fit wait in the crate");
    }

    private static WorkpieceData Weapon(WeaponType type, string metal, float d, float b, float s) =>
        new WorkpieceData { weaponType = type, materialName = metal, durability = d, balance = b, sharpness = s };

    private static void EventTrader()
    {
        var shop = new Workshop();
        var next = shop.NextLockedMetal;
        Check(next == ForgeMaterials.All[1], "the first Event Trader offers the next metal (iron)");
        var c = CustomerOrder.Challenge(WeaponType.Dagger, shop.BestQualityCap, next);
        Check(c.unlocksMaterial == next.name && c.IsChallenge, $"the challenge rewards {c.unlocksMaterial}: {c.NeedSummary()}");
        Check(c.HighestNeed >= shop.BestQualityCap - 10f && c.HighestNeed <= shop.BestQualityCap, "it asks for near the best the smith's metal can do");
        var ordinary = Weapon(WeaponType.Dagger, "Copper", 60, 60, 60);
        var master = Weapon(WeaponType.Dagger, "Copper", 60, 78, 78);
        Check(!c.IsFulfilled(ordinary), "an ordinary dagger doesn't win it");
        Check(c.IsFulfilled(master), "an exceptional dagger does");
        Check(!c.IsFulfilled(Weapon(WeaponType.Sword, "Copper", 78, 78, 78)), "the wrong kind of weapon doesn't count");
        Check(!shop.IsUnlocked(next), "accepting/holding a challenge unlocks nothing by itself");
        var hammer = CustomerOrder.Challenge(WeaponType.Hammer, shop.BestQualityCap, next);
        Check(hammer.minDurability > 0f && hammer.minSharpness <= 0f, $"a hammer challenge is about durability ({hammer.NeedSummary()})");
        Check(Workshop.FirstEventDay >= 2 && Workshop.EventTraderGap.x >= 2, "the Event Trader doesn't come every day");
    }

    private static void Reputation()
    {
        var shop = new Workshop();
        float start = shop.reputation;
        var o = CustomerOrder.Generate(3, null, Race.Human, shop.unlockedMetals, shop.reputation);
        o.tier = 1; o.minBalance = 50f; o.minDurability = 0f; o.minSharpness = 0f;
        float good = Workshop.SatisfactionRep(o, Weapon(o.weaponType, "Copper", 20, 58, 20));
        float great = Workshop.SatisfactionRep(o, Weapon(o.weaponType, "Copper", 20, 70, 20));
        Check(great > good && good > 0f && great <= 5f, $"a delivery earns a little; beating the order by far earns more ({good:0.0} / {great:0.0})");
        Check(-Workshop.RepRefused >= 1.5f * good, $"refusing costs clearly more than one delivery earns ({Workshop.RepRefused} vs +{good:0.0})");
        Check(-Workshop.RepRefused < 15f, "...but one refusal doesn't wreck a name");

        // What they ASKED for matters, not raw totals: wants Balance, gets a lopsided-but-right weapon vs a big-but-wrong one.
        o.minDurability = 40f;
        var right = Weapon(o.weaponType, "Copper", 70, 92, 0);
        var wrong = Weapon(o.weaponType, "Copper", 95, 45, 95); // higher total (235 vs 162), but short on the Balance they asked for
        Check(o.IsFulfilled(right) && !o.IsFulfilled(wrong), "a weapon that meets the asked stats satisfies; higher totals in the wrong stats don't");

        int orders = 0;
        while (shop.reputation < 80f && orders < 500) { shop.AddReputation(great); orders++; }
        Check(orders >= 20, $"even excellent work takes a while to reach 4 stars ({orders} excellent orders from {start:0})");
        shop.reputation = 0f; shop.AddReputation(good);
        Check(shop.reputation > 0f, "reputation can always be rebuilt from zero");
    }

    private static void Returns()
    {
        var shop = new Workshop();
        var h = shop.history;
        var byRace = new Dictionary<Race, List<int>>();
        int never = 0, total = 0, early = 0;
        var rng = new System.Random(3);
        foreach (Race race in System.Enum.GetValues(typeof(Race)))
        {
            byRace[race] = new List<int>();
            for (int i = 0; i < 200; i++)
            {
                var o = CustomerOrder.Generate(1, null, race);
                var r = h.RegisterForged(Weapon((WeaponType)(i % 5), "Copper", 60, 60, 60), 1);
                var buyer = h.AddCustomer(o, recurring: false);
                h.Sell(r, buyer, o, 1);
                total++;
                if (r.returnDay < 0) never++;
                else { byRace[race].Add(r.returnDay - 1); if (r.returnDay - 1 <= 3) early++; }
            }
        }
        var all = byRace.Values.SelectMany(x => x).ToList();
        Check(never > total * 0.2f && never < total * 0.7f, $"some sold weapons never come back ({never}/{total})");
        Check(all.Distinct().Count() >= 6, $"return days are spread out ({all.Min()}-{all.Max()} days after the sale, {all.Distinct().Count()} different)");
        Check(early < all.Count * 0.3f, $"most returns are not within 2-3 days ({early}/{all.Count})");
        string avg(Race r) => byRace[r].Count > 0 ? byRace[r].Average().ToString("0.0") : "-";
        Check(byRace[Race.Elf].Average() > byRace[Race.Dwarf].Average() + 1f,
              $"races differ a little (average days until the return: human {avg(Race.Human)}, elf {avg(Race.Elf)}, dwarf {avg(Race.Dwarf)})");

        // The day is fixed at the sale: days passing don't move it.
        var watched = h.swords.Where(r => r.status == SwordStatus.OwnedByCustomer && r.returnDay > 0).Take(50).ToList();
        var before = watched.Select(r => r.returnDay).ToList();
        for (int day = 2; day <= 20; day++) h.ProcessNewDay(day);
        bool stable = true;
        for (int i = 0; i < watched.Count; i++)
            if (watched[i].status == SwordStatus.OwnedByCustomer && watched[i].returnDay != before[i]) stable = false;
        Check(stable, "a return day isn't re-rolled as the days go by");

        // Every weapon type keeps its own history for the return card.
        var dagger = h.swords.First(r => r.sword.weaponType == WeaponType.Dagger);
        Check(dagger.Label.StartsWith("Dagger #") && dagger.createdDay == 1 && dagger.dayDelivered == 1 && h.Owner(dagger) != null,
              $"returned-weapon history: {dagger.Label}, made day {dagger.createdDay}, sold day {dagger.dayDelivered} to {h.Owner(dagger)?.name}, " +
              $"as forged {dagger.originalDurability:0}/{dagger.originalBalance:0}/{dagger.originalSharpness:0}");
    }

    private static void SaveRoundTrip()
    {
        var shop = new Workshop();
        shop.Unlock(ForgeMaterials.All[1]);
        shop.stock[1] = 3;
        shop.nextEventDay = 9;
        shop.challenge = CustomerOrder.Challenge(WeaponType.Spear, shop.BestQualityCap, ForgeMaterials.All[2]);
        shop.challengeDay = 6;
        var o = CustomerOrder.Generate(1, null, Race.Elf);
        var r = shop.history.RegisterForged(Weapon(WeaponType.Hammer, "Iron", 80, 50, 0), 2);
        shop.history.Sell(r, shop.history.AddCustomer(o, false), o, 2);
        r.returnDay = 11;
        SaveSystem.Save(shop, null);

        var loaded = new Workshop();
        bool ok = SaveSystem.TryLoad(loaded, out _);
        var lr = loaded.history.Find(r.id);
        Check(ok && loaded.unlockedMetals.SequenceEqual(shop.unlockedMetals) && loaded.stock[1] == 3, "unlocked metals and the store survive a save");
        Check(loaded.challenge != null && loaded.challenge.unlocksMaterial == "Steel" && loaded.challengeDay == 6 && loaded.nextEventDay == 9,
              "the Event Trader's challenge survives a save");
        Check(lr != null && lr.returnDay == 11, $"the return day survives a save ({lr?.returnDay})");
    }
}
