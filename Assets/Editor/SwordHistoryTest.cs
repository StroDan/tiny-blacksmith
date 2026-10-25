using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Edit-mode checks for sword identity, ownership, the rack and save/load (BlacksmithHistory + SaveSystem).
// No scene, no Play mode. Logs [HIST] lines and a final RESULT line.
// Run via: Unity.exe -batchmode -quit -projectPath <path> -executeMethod SwordHistoryTest.Run -logFile <log>
// or the Tiny Blacksmith > Run Sword History Tests menu item.
public static class SwordHistoryTest
{
    private static int failures;

    [MenuItem("Tiny Blacksmith/Run Sword History Tests")]
    public static void Run()
    {
        failures = 0;
        Random.InitState(7);
        string savePath = Path.Combine(Path.GetTempPath(), "tb_history_test_save.json");
        string oldEnv = System.Environment.GetEnvironmentVariable("TB_SAVE_PATH");
        System.Environment.SetEnvironmentVariable("TB_SAVE_PATH", savePath);
        try
        {
            RunCases(savePath);
        }
        catch (System.Exception e)
        {
            failures++;
            Debug.LogError("[HIST] exception: " + e);
        }
        finally
        {
            if (File.Exists(savePath)) File.Delete(savePath);
            System.Environment.SetEnvironmentVariable("TB_SAVE_PATH", oldEnv);
        }
        Debug.Log($"[HIST] RESULT: {(failures == 0 ? "PASS" : $"FAIL ({failures})")}");
    }

    private static void Check(bool ok, string what)
    {
        if (!ok) failures++;
        Debug.Log($"[HIST] {(ok ? "ok  " : "FAIL")} {what}");
    }

    private static WorkpieceData Sword(string metal, float dur, float bal, float sharp) =>
        new WorkpieceData { materialName = metal, itemName = metal + " Sword", durability = dur, balance = bal, sharpness = sharp };

    private static CustomerOrder Order(string name, float minDur = 0f, float minBal = 0f, float minSharp = 0f)
    {
        var o = CustomerOrder.Generate(1, null, Race.Human);
        o.weaponType = WeaponType.Sword; // these records are all swords
        o.customerName = name;
        o.minDurability = minDur;
        o.minBalance = minBal;
        o.minSharpness = minSharp;
        o.requiredMaterial = null;
        o.requestText = $"{name}: I need a sword that won't fail me on the road.";
        return o;
    }

    private static void Valid(BlacksmithHistory h, string when) { string p = h.Validate(); Check(p == null, $"history consistent {when}{(p != null ? " - " + p : "")}"); }

    private static void RunCases(string savePath)
    {
        var shop = new Workshop();
        var h = shop.history;

        // Case 7: several swords, each its own unique ID, all on the rack.
        var a = h.RegisterForged(Sword("Steel", 91, 73, 64), 4);
        var b = h.RegisterForged(Sword("Iron", 40, 90, 55), 4);
        var c = h.RegisterForged(Sword("Copper", 60, 60, 60), 5);
        Check(a.id == 1 && b.id == 2 && c.id == 3 && a.Label == "Sword #0001", "sequential unique IDs (Sword #0001..3)");
        Check(h.Storage.Count == 3 && a.status == SwordStatus.InStorage, "new swords are InStorage and on the rack");
        bool threw = false;
        try { h.RegisterForged(a.sword, 5); } catch (System.InvalidOperationException) { threw = true; }
        Check(threw && h.swords.Count == 3, "the same sword can't be registered twice");
        Valid(h, "after forging");

        // Case 1 + 11: selling one sword takes exactly that one off the rack, the others stay.
        var marcusOrder = Order("Marcus", minDur: 50f);
        var marcus = h.AddCustomer(marcusOrder, recurring: true);
        h.Sell(a, marcus, marcusOrder, 4);
        Check(!ContainsRef(h.Storage, a.sword), "sold sword left the rack");
        Check(ContainsRef(h.Storage, b.sword) && ContainsRef(h.Storage, c.sword) && h.Storage.Count == 2, "the other swords are still on the rack");
        Check(a.status == SwordStatus.OwnedByCustomer && a.ownerId == marcus.id && h.Owner(a) == marcus, "Marcus owns Sword #0001");
        Check(a.order == marcusOrder && a.order.requestText.Contains("road"), "the original order is remembered");
        Check(h.CarriedBy(marcus) == a, "Marcus carries Sword #0001");
        threw = false;
        try { h.Sell(a, marcus, marcusOrder, 4); } catch (System.InvalidOperationException) { threw = true; }
        Check(threw, "a sword that's already sold can't be sold again");
        threw = false;
        try { h.Scrap(a, 4); } catch (System.InvalidOperationException) { threw = true; }
        Check(threw && a.status == SwordStatus.OwnedByCustomer, "a customer's sword can't be scrapped");
        Valid(h, "after the sale");

        // Case 8: two customers own different swords, never the same one.
        var elenaOrder = Order("Elena", minBal: 50f);
        var elena = h.AddCustomer(elenaOrder, recurring: false);
        h.Sell(b, elena, elenaOrder, 5);
        Check(h.CarriedBy(elena) == b && h.CarriedBy(marcus) == a && a.ownerId != b.ownerId, "Marcus and Elena each own their own sword");

        // Day passing: sword wear follows durability.
        float before = a.condition, beforeB = b.condition;
        h.ProcessNewDay(6);
        Check(a.condition < before && before - a.condition < beforeB - b.condition, $"sturdier sword wears slower (#1 -{before - a.condition:0.0}, #2 -{beforeB - b.condition:0.0})");
        for (int day = 7; day <= 40 && a.condition >= BlacksmithHistory.RepairThreshold; day++) h.ProcessNewDay(day);
        Check(a.condition < BlacksmithHistory.RepairThreshold && h.EventsFor(a).Exists(e => e.type == HistoryEventType.WeaponDamaged), $"worn below the repair threshold with a damage event (condition {a.condition:0})");
        Check(b.condition >= 25f, "a one-off customer's sword never wears out completely");

        // Cases 3 + 4: Marcus returns with the SAME sword; it comes back to the rack once, still his.
        string line = SwordStories.RepairRequest(a);
        Debug.Log("[HIST] repair request: " + line);
        int rackBefore = h.Storage.Count;
        h.TakeInForRepair(a, 12);
        Check(a.id == 1 && a.status == SwordStatus.NeedsRepair && a.ownerId == marcus.id, "returned sword keeps ID #0001 and its owner");
        Check(h.Storage.Count == rackBefore + 1 && CountRef(h.Storage, a.sword) == 1, "it's on the rack exactly once");
        Check(!h.IsSmithsOwn(a.sword) && h.CarriedBy(marcus) == null, "it isn't the smith's to sell, and Marcus isn't carrying it");
        Check(h.swords.Count == 3, "no new record was created for the repair");
        Valid(h, "while in for repair");

        // Case 5 + 9: repaired and returned (twice) - same ID, repair count climbs, back to its owner.
        h.FinishRepair(a, 12);
        h.ReturnToOwner(a, 12, repaired: true);
        Check(a.status == SwordStatus.OwnedByCustomer && !ContainsRef(h.Storage, a.sword) && a.condition == 100f && a.repairCount == 1, "repair #1: back with Marcus, off the rack, fresh edge");
        a.condition = 10f;
        h.TakeInForRepair(a, 15);
        h.FinishRepair(a, 15);
        h.ReturnToOwner(a, 15, repaired: true);
        Check(a.id == 1 && a.repairCount == 2 && a.timesReturned == 2 && h.swords.Count == 3 && h.nextSwordId == 4, "repair #2: still Sword #0001, 2 repairs, no new IDs used");
        Valid(h, "after two repairs");

        // Case 6: a blade scrapped straight off the result screen never enters the history.
        var discarded = Sword("Iron", 5, 5, 5);
        shop.scrap.Add(discarded);
        Check(h.Find(discarded) == null && discarded.id == 0 && h.swords.Count == 3, "result-screen discard gets no ID and no record");

        // Case 10: scrapped (from the rack) and destroyed (in use) swords are gone from everyone.
        h.Scrap(c, 15);
        Check(c.status == SwordStatus.Scrapped && !ContainsRef(h.Storage, c.sword) && c.ownerId == 0, "rack sword scrapped - off the rack, owned by no one");
        h.Destroy(b, 16, "snapped clean in two");
        Check(b.status == SwordStatus.Destroyed && h.CarriedBy(elena) == null && !ContainsRef(h.Storage, b.sword), "destroyed sword no longer available to Elena");
        threw = false;
        try { h.TakeInForRepair(b, 16); } catch (System.InvalidOperationException) { threw = true; }
        Check(threw, "a destroyed sword can't be brought in for repair");

        // Follow-up order shaped by the previous sword (low balance -> insists on balance).
        var weak = Sword("Iron", 90, 30, 70);
        var d = h.RegisterForged(weak, 16);
        h.Sell(d, marcus, Order("Marcus", minDur: 50f), 16);
        var follow = CustomerOrder.FollowUp(marcus, d, 20);
        Debug.Log($"[HIST] follow-up: {follow.requestText} | needs {follow.NeedSummary()}");
        Check(follow.customerId == marcus.id && follow.minBalance >= 50f && follow.requestText.Contains("steadier"), "follow-up asks for what let them down last time");

        // A repair job's order paper and needs.
        var job = CustomerOrder.RepairJob(marcus, a, shop.RepairFee(a), "fix it");
        Check(job.IsRepair && !job.IsFulfilled(a.sword) && job.PaperText(20).Contains("Sword #0001"), "repair job references the same sword and can't be 'fulfilled' by selling");

        // Cases 2 + 12: save, reload into a fresh workshop - ownership, rack, IDs and nulls all survive.
        h.TakeInForRepair(a, 20);
        var pending = CustomerOrder.RepairJob(marcus, a, 12, "fix it");
        var inStorage = h.RegisterForged(Sword("Steel", 70, 70, 70), 20);
        shop.gold = 123;
        shop.day = 20;
        shop.Accept(pending, 20); // into the order book - it reserves Marcus's sword
        SaveSystem.Save(shop);
        Check(File.Exists(savePath), "save file written");

        var loaded = new Workshop();
        bool ok = SaveSystem.TryLoad(loaded, out _);
        var loadedPending = loaded.FindOrder(pending.id);
        var lh = loaded.history;
        Check(ok && loaded.gold == 123 && loaded.day == 20, "save loads (gold, day)");
        Check(lh.swords.Count == h.swords.Count && lh.nextSwordId == h.nextSwordId, "same number of records, ID counter preserved");
        var la = lh.Find(1);
        Check(la != null && la.status == SwordStatus.NeedsRepair && lh.Owner(la)?.name == "Marcus" && la.repairCount == 2, "Sword #0001 still Marcus's, in for repair, 2 repairs");
        Check(lh.Find(2).status == SwordStatus.Destroyed && lh.Find(3).status == SwordStatus.Scrapped && lh.Find(4).IsOut, "destroyed / scrapped / sold statuses survive");
        Check(lh.Storage.Count == 2 && ContainsRef(lh.Storage, la.sword) && ContainsRef(lh.Storage, lh.Find(inStorage.id).sword), "rack rebuilt with exactly the repair job and the unsold sword");
        Check(la.order != null && la.order.requiredMaterial == null && la.legendName == null, "null strings survive JSON (any-metal order, no legend name)");
        Check(lh.Find(inStorage.id).order == null, "an unsold sword has no order after loading");
        Check(loadedPending != null && loadedPending.IsRepair && loadedPending.repairSwordId == 1 && la.reservedForOrder == pending.id, $"the repair order survives ({pending.Label}, still holding Sword #0001)");
        Check(la.sword.id == 1 && lh.Find(la.sword) == la, "loaded sword data is linked to its record");
        Valid(lh, "after loading");
        var next = lh.RegisterForged(Sword("Copper", 50, 50, 50), 21);
        Check(next.id == 6, "IDs continue after loading, never reused");

        // A corrupted save (a sword on the rack twice) is refused rather than loaded.
        string json = File.ReadAllText(savePath);
        var data = JsonUtility.FromJson<SaveData>(json);
        data.history.swords[1].id = 1; // duplicate ID
        File.WriteAllText(savePath, JsonUtility.ToJson(data));
        Check(!SaveSystem.TryLoad(new Workshop(), out _), "an inconsistent save is rejected");

        // Journal/UI text doesn't throw on any status.
        foreach (var r in lh.swords) Debug.Log($"[HIST] journal: {r.Label} {r.status} owner={lh.Owner(r)?.Title ?? "-"} condition={r.ConditionText} events={lh.EventsFor(r).Count}");
    }

    private static bool ContainsRef(IReadOnlyList<WorkpieceData> list, WorkpieceData w) => CountRef(list, w) > 0;
    private static int CountRef(IReadOnlyList<WorkpieceData> list, WorkpieceData w) { int n = 0; foreach (var x in list) if (ReferenceEquals(x, w)) n++; return n; }
}
