using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

// Edit-mode regression tests for the order book: CUSTOMER -> ORDER (id) -> WEAPON(S) (ids), never "whatever was
// forged last". Covers: B the latest-weapon trap, C save/load, D reserved weapons, E the same customer, F a
// multi-weapon contract, repair orders, the Event Trader's challenge, an old save's single order, and that the old
// newest-match lookup is gone from the game. Logs [ORDER] lines and a final RESULT line.
// Run: Unity.exe -batchmode -quit -projectPath <p> -executeMethod OrderSystemTest.Run -logFile <log>
public static class OrderSystemTest
{
    private static int failures;

    private static void Check(bool ok, string what)
    {
        if (!ok) failures++;
        Debug.Log($"[ORDER] {(ok ? "ok  " : "FAIL")} {what}");
    }

    public static void Run()
    {
        failures = 0;
        string savePath = Path.Combine(Path.GetTempPath(), "tb_order_test_save.json");
        string oldEnv = System.Environment.GetEnvironmentVariable("TB_SAVE_PATH");
        System.Environment.SetEnvironmentVariable("TB_SAVE_PATH", savePath);
        try
        {
            Random.InitState(1234);
            LatestWeaponTrap();
            SaveLoad();
            Reserved();
            HiddenJudgement();
            Contract();
            Repair();
            Challenge();
            OldSave();
            NoNewestMatchLogic();
        }
        catch (System.Exception e) { failures++; Debug.LogError("[ORDER] exception: " + e); }
        finally
        {
            if (File.Exists(savePath)) File.Delete(savePath);
            System.Environment.SetEnvironmentVariable("TB_SAVE_PATH", oldEnv);
        }
        Debug.Log($"[ORDER] RESULT: {(failures == 0 ? "PASS" : $"FAIL ({failures})")}");
    }

    private static readonly string[] AllMetals = { "Copper", "Iron", "Steel" };

    private static Workshop NewShop()
    {
        var w = new Workshop();
        foreach (var m in ForgeMaterials.All) w.Unlock(m);
        return w;
    }

    // A person walks in and orders a weapon of this type; the smith accepts.
    private static CustomerOrder Order(Workshop w, WeaponType type, out CustomerRecord who)
    {
        who = NpcStories.Meet(w.history, w.day);
        var o = NpcStories.OrderFor(who, w.history, w.day, AllMetals, 60f);
        o.weaponType = type;
        o.quantity = 1;
        o.requiredMaterial = null;
        if (!o.Weapon.hasEdge) o.minSharpness = 0f; // (the type was changed after the order was made)
        w.Accept(o, w.day);
        return o;
    }

    private static SwordRecord Forge(Workshop w, WeaponType type, float stat = 95f)
        => w.history.RegisterForged(new WorkpieceData { weaponType = type, materialName = "Steel", itemName = "Steel " + Weapons.Get(type).name, durability = stat, balance = stat, sharpness = Weapons.Get(type).hasEdge ? stat : 0 }, w.day);

    // TEST B: order 1, weapon 1 assigned; weapons 2 and 3 forged later (better, newer) - the customer gets weapon 1.
    private static void LatestWeaponTrap()
    {
        var w = NewShop();
        var o1 = Order(w, WeaponType.Sword, out var a);
        Check(o1.id == 1 && o1.Label == "Order #0001" && o1.status == OrderStatus.WaitingForWeapon && o1.customerId == a.id && w.orders.Contains(o1),
              $"accepted: {o1.Label}, customer {a.NpcId}, waiting for a weapon, in the book");
        Check(o1.returnDay > w.day, $"the customer comes back later (day {o1.returnDay}), not now");
        var s1 = Forge(w, WeaponType.Sword, 90f);
        Check(w.Assign(o1, s1, w.day) == null && s1.reservedForOrder == o1.id && o1.status == OrderStatus.WeaponReady, $"{s1.Label} assigned to {o1.Label} -> ready");
        w.day = 2;
        var o2 = Order(w, WeaponType.Sword, out var b);
        var s2 = Forge(w, WeaponType.Sword, 99f);                // for customer 2 - better than s1
        Check(w.Assign(o2, s2, w.day) == null, $"{s2.Label} assigned to {o2.Label}");
        w.day = 3;
        var s3 = Forge(w, WeaponType.Sword, 100f);               // for the rack - the newest and best of all
        Check(!s3.IsReserved && s3.status == SwordStatus.InStorage, $"{s3.Label} forged for the rack, unreserved");
        var got = w.Deliver(o1, a, w.day);
        Check(got.Count == 1 && got[0] == s1, $"customer 1 receives {string.Join(",", got.Select(r => r.Label))} - exactly {s1.Label}, not {s2.Label} or {s3.Label}");
        Check(s1.status == SwordStatus.OwnedByCustomer && s1.ownerId == a.id && !s1.IsReserved && s1.order == o1 && o1.status == OrderStatus.Completed && !w.orders.Contains(o1),
              "delivered: owned by customer 1, reservation lifted, order completed and out of the book");
        Check(s2.status == SwordStatus.InStorage && s2.reservedForOrder == o2.id && s3.status == SwordStatus.InStorage && !s3.IsReserved, $"{s2.Label} still waits for {o2.Label}; {s3.Label} still on the rack");
        var events = w.history.EventsFor(s1).Select(e => e.type).ToList();
        Check(events.SequenceEqual(new[] { HistoryEventType.Forged, HistoryEventType.WeaponReserved, HistoryEventType.WeaponPurchased }),
              $"{s1.Label}'s history: forged -> reserved for {o1.Label} -> delivered ({string.Join(", ", events)})");
        Check(w.ValidateOrders() == null && w.history.Validate() == null, "order book and history consistent");
    }

    // TEST C (logic): the assignment survives a save and load, and delivers the same weapon afterwards.
    private static void SaveLoad()
    {
        var w = NewShop();
        var o = Order(w, WeaponType.Dagger, out var who);
        var d1 = Forge(w, WeaponType.Dagger, 90f);
        w.Assign(o, d1, w.day);
        Forge(w, WeaponType.Dagger, 100f); // a newer, better dagger
        string story = who.profile.story;
        SaveSystem.Save(w);
        var l = new Workshop();
        Check(SaveSystem.TryLoad(l, out _), "saved and loaded");
        var lo = l.FindOrder(o.id);
        Check(lo != null && lo.customerId == who.id && lo.status == OrderStatus.WeaponReady && lo.returnDay == o.returnDay && lo.deadlineDay == o.deadlineDay,
              $"{o.Label} is still in the book after loading: customer {who.NpcId}, ready, back day {lo?.returnDay}");
        Check(l.AssignedWeaponIds(lo).SequenceEqual(new[] { d1.id }), $"...with exactly {d1.Label} assigned");
        var lwho = l.history.Customer(who.id);
        Check(lwho != null && lwho.name == who.name && lwho.profile.story == story, $"...for the same person ({lwho?.Title})");
        var got = l.Deliver(lo, lwho, l.day);
        Check(got.Count == 1 && got[0].id == d1.id, $"after loading, delivery hands over {got[0].Label} (not the newer dagger)");
        Check(l.nextOrderId > o.id, "new orders after loading never reuse an id");
    }

    // TEST D: a reserved weapon can't go to anyone else, be scrapped or be assigned twice - until it's unassigned.
    private static void Reserved()
    {
        var w = NewShop();
        var o5 = Order(w, WeaponType.Spear, out _);
        var o6 = Order(w, WeaponType.Spear, out var c6);
        var s10 = Forge(w, WeaponType.Spear);
        w.Assign(o5, s10, w.day);
        string why = w.CanAssign(o6, s10);
        Check(why != null && why.Contains("reserved"), $"can't assign it to another order: \"{why}\"");
        bool sold = true;
        try { w.history.Sell(s10, c6, o6, w.day); } catch (System.InvalidOperationException) { sold = false; }
        Check(!sold && s10.reservedForOrder == o5.id, "can't sell it to another customer");
        bool scrapped = true;
        try { w.history.Scrap(s10, w.day); } catch (System.InvalidOperationException) { scrapped = false; }
        Check(!scrapped && s10.status == SwordStatus.InStorage, "can't scrap it");
        Check(!w.IsReady(o6) && w.IsReady(o5), "only its own order counts it");
        w.Unassign(s10, w.day);
        Check(!s10.IsReserved && o5.status == OrderStatus.WaitingForWeapon, "unassigned explicitly: free again, its order waits again");
        Check(w.Assign(o6, s10, w.day) == null && s10.reservedForOrder == o6.id, "...and now it can go to the other order");
        var bad = Forge(w, WeaponType.Spear, 10f);
        Check(w.CanAssign(o5, bad) == null, "a weak weapon of the right kind CAN still be assigned (the hidden numbers never filter the choice)");
        var wrongType = Forge(w, WeaponType.Hammer);
        Check(w.CanAssign(o5, wrongType) != null, "nor one of the wrong type");
        Check(w.ValidateOrders() == null, "consistent");
    }

    // The customer's numbers stay hidden: every weapon of the right kind (and a named metal) can be set aside; only
    // when they collect is it judged - pleased, grudging (less pay, a small loss of name) or refused.
    private static void HiddenJudgement()
    {
        var w = NewShop();
        var o = Order(w, WeaponType.Sword, out _);
        o.minDurability = 80f; o.minBalance = 65f; o.minSharpness = 0f; o.detailsAsked = false;
        var great = Forge(w, WeaponType.Sword, 99f);
        var slightlyShort = w.history.RegisterForged(new WorkpieceData { weaponType = WeaponType.Sword, materialName = "Steel", itemName = "Steel Sword", durability = 72, balance = 70, sharpness = 50 }, 1);
        var farShort = w.history.RegisterForged(new WorkpieceData { weaponType = WeaponType.Sword, materialName = "Steel", itemName = "Steel Sword", durability = 40, balance = 40, sharpness = 40 }, 1);
        var cracked = w.history.RegisterForged(new WorkpieceData { weaponType = WeaponType.Sword, materialName = "Steel", itemName = "Steel Sword", durability = 99, balance = 99, sharpness = 99, cracked = true }, 1);
        Check(w.CanAssign(o, great) == null && w.CanAssign(o, slightlyShort) == null && w.CanAssign(o, farShort) == null && w.CanAssign(o, cracked) == null,
              "great, slightly short, far short and even cracked swords are all offered for the order - nothing reveals which pass");
        Check(Workshop.Judge(o, great.sword) == Workshop.Verdict.Excellent, "judged on collection: the great one pleases");
        Check(Workshop.Judge(o, slightlyShort.sword) == Workshop.Verdict.Grudging, "...one a little short on durability is taken grudgingly");
        Check(Workshop.Judge(o, farShort.sword) == Workshop.Verdict.Refused && Workshop.Judge(o, cracked.sword) == Workshop.Verdict.Refused, "...one far short, or cracked, is refused");
        int full = w.Payment(o, great.sword, out int tip), less = w.Payment(o, slightlyShort.sword, out int lessTip), none = w.Payment(o, farShort.sword, out _);
        Check(less == Mathf.RoundToInt(w.BasePay(o, "Steel") * Workshop.GrudgingPay) && lessTip == 0 && none == 0 && full > less && tip > 0,
              $"pay follows the verdict: {full} + {tip} tip, {less} grudging, {none} refused");
        Check(Workshop.SatisfactionRep(o, great.sword) > 0f && Workshop.SatisfactionRep(o, slightlyShort.sword) < 0f, "reputation: gained for the great one, lost for settling");
        // A metal they named is a hard requirement (it's in their words, not hidden) - the wrong metal isn't offered.
        o.requiredMaterial = "Iron";
        Check(w.CanAssign(o, great) != null, "a sword in the wrong metal isn't offered when they named the metal");
    }

    // TEST F: a contract for three - exact ids, ready only when all three are set aside.
    private static void Contract()
    {
        var w = NewShop();
        var o = Order(w, WeaponType.Sword, out var knight);
        o.quantity = 3;
        w.RefreshStatus(o);
        var before = Forge(w, WeaponType.Sword);           // forged before - not for this contract
        var a = Forge(w, WeaponType.Sword); var b = Forge(w, WeaponType.Sword);
        w.Assign(o, a, w.day); w.Assign(o, b, w.day);
        Check(!w.IsReady(o) && o.status == OrderStatus.WaitingForWeapon, "2 of 3 set aside: not ready");
        var c = Forge(w, WeaponType.Sword);
        var after = Forge(w, WeaponType.Sword);            // forged later - not for this contract either
        w.Assign(o, c, w.day);
        Check(w.IsReady(o) && o.status == OrderStatus.WeaponReady, "3 of 3: ready");
        Check(w.CanAssign(o, after) != null, "a fourth can't be added");
        var got = w.Deliver(o, knight, w.day);
        Check(got.Select(r => r.id).SequenceEqual(new[] { a.id, b.id, c.id }), $"delivers exactly {string.Join(", ", got.Select(r => r.Label))}");
        Check(before.status == SwordStatus.InStorage && after.status == SwordStatus.InStorage, "the swords forged before and after stay on the rack");
    }

    // Repair order: points at the customer's own weapon; after the repair it's the very same weapon (same id).
    private static void Repair()
    {
        var w = NewShop();
        var o = Order(w, WeaponType.Hammer, out var marcus);
        var s12 = Forge(w, WeaponType.Hammer);
        Check(w.Assign(o, s12, w.day) == null, $"{s12.Label} assigned to {marcus.name}'s {o.Label}");
        w.Deliver(o, marcus, w.day);
        int records = w.history.swords.Count;
        s12.condition = 30f;
        w.history.TakeInForRepair(s12, 5);
        var job = CustomerOrder.RepairJob(marcus, s12, 20, "Fix it, please.");
        w.Accept(job, 5);
        Check(job.IsRepair && s12.reservedForOrder == job.id && job.customerId == marcus.id && !w.IsReady(job), $"repair {job.Label} holds {s12.Label} (not yet repaired)");
        Check(w.CanAssign(job, Forge(w, WeaponType.Hammer)) != null, "a repair order takes no other weapon");
        records = w.history.swords.Count;
        w.history.FinishRepair(s12, 5);
        w.RefreshStatus(job);
        Check(w.IsReady(job) && job.status == OrderStatus.WeaponReady, "repaired: ready to collect");
        w.history.ReturnToOwner(s12, 6, repaired: true);
        w.Close(job, 6, OrderStatus.Completed);
        Check(s12.status == SwordStatus.OwnedByCustomer && s12.ownerId == marcus.id && !s12.IsReserved && s12.repairCount == 1 && w.history.swords.Count == records,
              $"{s12.Label} is back with {marcus.name}, the same weapon (repaired once, no new record)");
        Check(w.ValidateOrders() == null && w.history.Validate() == null, "consistent");
    }

    // The Event Trader takes only the weapon set aside for her challenge, never the best one lying around.
    private static void Challenge()
    {
        var w = NewShop();
        var c = CustomerOrder.Challenge(WeaponType.Dagger, 90f, ForgeMaterials.All[1]);
        w.challenge = c;
        w.Accept(c, 1);
        Check(c.id > 0 && !w.orders.Contains(c) && w.FindOrder(c.id) == c, $"the challenge is {c.Label} (kept apart from the book)");
        var great = Forge(w, WeaponType.Dagger, 100f);
        Check(w.AssignedTo(c).Count == 0, "a perfect dagger on the rack isn't hers until it's set aside for her");
        Check(w.Assign(c, great, 1) == null && w.AssignedTo(c)[0] == great, "set aside: hers");
        SaveSystem.Save(w);
        var l = new Workshop(); SaveSystem.TryLoad(l, out _);
        Check(l.challenge != null && l.challenge.id == c.id && l.AssignedWeaponIds(l.challenge).SequenceEqual(new[] { great.id }), "the challenge's reservation survives a save/load");
    }

    // A save from before the order book: the one order the customer was waiting on becomes Order #n in the book.
    private static void OldSave()
    {
        var w = NewShop();
        var who = NpcStories.Meet(w.history, 1);
        var old = NpcStories.OrderFor(who, w.history, 1, AllMetals, 60f);
        old.deadlineDay = 4;
        var data = new SaveData { gold = 50, day = 2, history = w.history, hasPendingOrder = true, pendingOrder = old, unlockedMetals = new List<string>(AllMetals), stock = new int[3] };
        File.WriteAllText(SaveSystem.PathOrNull, JsonUtility.ToJson(data));
        var l = new Workshop();
        Check(SaveSystem.TryLoad(l, out _), "an old save loads");
        var o = l.orders.Count == 1 ? l.orders[0] : null;
        Check(o != null && o.id > 0 && o.customerId == who.id && o.IsActive && l.AssignedTo(o).Count == 0 && o.returnDay == 3,
              $"its waiting order is now {o?.Label} in the book, for {who.name}, nothing assigned yet, customer back tomorrow");
        Check(l.ValidateOrders() == null, "consistent");
    }

    // The old "newest matching weapon on the rack" lookup and the camp-at-the-table order are gone from the game.
    private static void NoNewestMatchLogic()
    {
        var t = typeof(ForgeGameManager);
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
        Check(t.GetMethod("FindMatchingSword", flags) == null && t.GetMethod("NewestOwnSword", flags) == null && t.GetMethod("TryGiveSword", flags) == null,
              "FindMatchingSword / NewestOwnSword / TryGiveSword no longer exist");
        Check(t.GetField("pendingOrder", flags) == null && t.GetField("customerWaiting", flags) == null, "no single pending order, no customer camping at the table");
        string src = File.ReadAllText("Assets/Scripts/ForgeGameManager.cs");
        Check(!src.Contains("inventory.Count - 1; i >= 0; i--)\n            if (workshop.history.IsSmithsOwn(inventory[i]) && order.IsFulfilled"), "no newest-first scan of the rack for an order anywhere");
    }
}
