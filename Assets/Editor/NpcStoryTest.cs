using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

// Edit-mode checks for persistent NPCs (NpcStories): 50 generated people are all different (no repeated stories,
// names or opening lines), make sense (lore-bound trades, purses, preferences that follow from their past), their
// orders follow from who they are, and nothing about them changes across a save/load. Then a few of them live
// through several weeks with the smith: a sale, a repair, a follow-up, being turned away, a cousin, life events -
// and they remember it when they come back.
// Every generated person (story + first order) is written to Logs/npc_sample.txt for reading.
// Run: Unity.exe -batchmode -quit -projectPath <p> -executeMethod NpcStoryTest.Run -logFile <log>
public static class NpcStoryTest
{
    private static int failures;

    private static void Check(bool ok, string what)
    {
        if (!ok) failures++;
        Debug.Log($"[NPC] {(ok ? "ok  " : "FAIL")} {what}");
    }

    public static void Run()
    {
        failures = 0;
        string savePath = Path.Combine(Path.GetTempPath(), "tb_npc_test_save.json");
        string oldEnv = System.Environment.GetEnvironmentVariable("TB_SAVE_PATH");
        System.Environment.SetEnvironmentVariable("TB_SAVE_PATH", savePath);
        try
        {
            Random.InitState(4242);
            Population();
            SaveLoad();
            Lives();
        }
        catch (System.Exception e) { failures++; Debug.LogError("[NPC] exception: " + e); }
        finally
        {
            if (File.Exists(savePath)) File.Delete(savePath);
            System.Environment.SetEnvironmentVariable("TB_SAVE_PATH", oldEnv);
        }
        Debug.Log($"[NPC] RESULT: {(failures == 0 ? "PASS" : $"FAIL ({failures})")}");
    }

    private static readonly string[] AllMetals = { "Copper", "Iron", "Steel" };

    private static Workshop shop;

    private static void Population()
    {
        shop = new Workshop();
        foreach (var m in ForgeMaterials.All) shop.Unlock(m);
        var h = shop.history;
        var people = new List<CustomerRecord>();
        var firstOrders = new List<CustomerOrder>();
        var dump = new List<string>();
        for (int i = 0; i < 50; i++)
        {
            var c = NpcStories.Meet(h, 1 + i / 5);
            var o = NpcStories.OrderFor(c, h, 5 + i / 5, AllMetals, 70f);
            people.Add(c);
            firstOrders.Add(o);
            // Some of them buy something, so later people can be their cousins and friends.
            if (i % 3 == 0)
            {
                var w = new WorkpieceData { weaponType = o.weaponType, materialName = "Iron", itemName = "Iron " + o.Weapon.name, durability = 80, balance = 80, sharpness = o.Weapon.hasEdge ? 80 : 0 };
                h.Sell(h.RegisterForged(w, 1), c, o, 1);
            }
            dump.Add($"{c.NpcId}  {c.Title}\n  {c.profile.story}\n  wealth {c.profile.wealth}, rank {c.profile.rank}, patience {o.Patience}d, tier {o.tier}, pays {o.reward}\n" +
                     $"  ORDER: {o.Weapon.name} - {o.NeedSummary()}{(o.requiredMaterial != null ? " in " + o.requiredMaterial : "")}\n  SAYS: \"{o.requestText}\"\n");
        }
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/npc_sample.txt", string.Join("\n", dump));
        Debug.Log($"[NPC] wrote {people.Count} people to Logs/npc_sample.txt");

        Check(people.Select(c => c.id).Distinct().Count() == 50 && people.All(c => c.NpcId.StartsWith("NPC_")), "50 people, 50 ids (NPC_0001...)");
        Check(people.Select(c => NpcStories.FullName(c)).Distinct().Count() == 50, "no two share a full name");
        Check(people.Select(c => c.profile.Signature(c.archetypeIndex)).Distinct().Count() == 50, "no two share trade + background + past + temperament + goal");
        Check(people.Select(c => c.profile.story).Distinct().Count() == 50, "no repeated biography");
        Check(firstOrders.Select(o => o.requestText).Distinct().Count() == 50, "no repeated opening words");
        int longest = firstOrders.Max(o => o.requestText.Length);
        Check(longest <= 420, $"everyone's words fit the dialogue box (longest {longest} characters)");
        string all = string.Join("\n", dump);
        Check(!all.Contains(" 1 years") && !all.Contains("in the road") && !all.Contains("from the road") && !all.Contains("{"),
              "no grammar slips or unfilled blanks (\"1 years\", \"in the road\", {tokens})");
        int trades = people.Select(c => c.archetypeIndex).Distinct().Count();
        Check(trades >= 12, $"many different trades ({trades})");
        Check(people.Select(c => c.race).Distinct().Count() == 3, "all three folk turn up");
        Check(people.Any(c => c.profile.unusual), $"some took up a trade unusual among their folk ({people.Count(c => c.profile.unusual)})");
        Check(people.Any(c => c.profile.relatedTo != 0), $"some are cousins/friends/comrades of earlier customers ({people.Count(c => c.profile.relatedTo != 0)})");

        // Nothing nonsensical.
        bool lore = true, unusualRight = true, weapons = true, purses = true, ages = true, cousins = true, trader = true;
        foreach (var c in people)
        {
            var a = c.Archetype;
            if (c.archetypeIndex == CustomerArchetype.MerchantIndex) trader = false;
            if (a.title == "grove-keeper" && c.race != Race.Elf) lore = false;
            if (c.profile.unusual == a.TakenUpBy(c.race) && c.profile.relation != NpcRelation.Comrade) unusualRight = false;
            if (System.Array.IndexOf(a.weapons, c.profile.preferredWeapon) < 0) weapons = false;
            if (c.profile.preferredStat == NpcStories.StatSharpness && !Weapons.Get(c.profile.preferredWeapon).hasEdge) weapons = false;
            if (c.profile.wealth < 0 || c.profile.wealth > 3) purses = false;
            if ((a.title == "knight") && c.profile.wealth == 0) purses = false;
            if (c.profile.rank == 2 && c.profile.ageGroup == 0) ages = false;
            if (c.profile.relation == NpcRelation.Cousin)
            {
                var other = h.Customer(c.profile.relatedTo);
                if (other == null || other.race != c.race || other.profile.surname != c.profile.surname) cousins = false;
            }
        }
        Check(trader, "nobody is generated as the Event Trader");
        Check(lore, "lore-bound trades only for their folk (grove-keepers are elves)");
        Check(unusualRight, "'unusual' marks exactly the trades not common among their folk");
        Check(weapons, "every favourite weapon is one their trade uses, and nobody wants an edge on a hammer");
        Check(purses, "purses make sense (no penniless knights)");
        Check(ages, "no young masters of their trade");
        Check(cousins, "cousins share their family name and folk");

        // Orders follow from the person.
        int likesOwn = 0, statKept = 0, statPossible = 0;
        bool poorModest = true, wealthMetal = true;
        for (int i = 0; i < people.Count; i++)
        {
            var c = people[i];
            var o = firstOrders[i];
            if (o.weaponType == c.profile.preferredWeapon) likesOwn++;
            int s = c.profile.preferredStat;
            bool edge = o.Weapon.hasEdge;
            if (s != NpcStories.StatSharpness || edge)
            {
                statPossible++;
                if ((s == 0 && o.minDurability > 0f) || (s == 1 && o.minBalance > 0f) || (s == 2 && o.minSharpness > 0f)) statKept++;
            }
            if (c.profile.wealth == 0 && (o.tier > 1 || o.requiredMaterial != null)) poorModest = false;
            if (o.requiredMaterial != null && c.profile.wealth < 3) wealthMetal = false;
            if (o.titleOverride != c.Title || o.customerId != c.id) poorModest = false;
        }
        Check(likesOwn >= 30, $"most ask for their favourite weapon ({likesOwn}/50)");
        Check(statKept == statPossible, $"everyone insists on the stat their past taught them ({statKept}/{statPossible})");
        Check(poorModest, "poor customers ask for modest work and never insist on a metal");
        Check(wealthMetal, "only the wealthy insist on a particular metal");
        var patience = firstOrders.Select(o => o.Patience).Distinct().ToList();
        Check(patience.Count >= 2, $"temperament changes how long people wait ({string.Join("/", patience.OrderBy(p => p))} days)");
    }

    private static void SaveLoad()
    {
        var before = shop.history.customers.ToDictionary(c => c.id, c => (JsonUtility.ToJson(c.profile), c.Title, c.name, c.race, c.archetypeIndex));
        SaveSystem.Save(shop, null);
        var loaded = new Workshop();
        bool read = SaveSystem.TryLoad(loaded, out _);
        bool same = read && loaded.history.customers.Count == before.Count && loaded.history.customers.All(c =>
            before.TryGetValue(c.id, out var b) && b.Item1 == JsonUtility.ToJson(c.profile) && b.Title == c.Title && b.name == c.name && b.race == c.race && b.archetypeIndex == c.archetypeIndex);
        Check(same, $"after save and load every one of {before.Count} people is exactly the same person");
        // Saving and loading again changes nothing either (stories are never regenerated).
        SaveSystem.Save(loaded, null);
        var again = new Workshop();
        SaveSystem.TryLoad(again, out _);
        Check(again.history.customers.All(c => JsonUtility.ToJson(c.profile) == JsonUtility.ToJson(loaded.history.Customer(c.id).profile)), "...and again after a second save/load");

        // A save from before profiles: its customers get one, keep their name/race/trade, and get the same one every load.
        var legacy = new Workshop();
        var order = CustomerOrder.Generate(3, null, Race.Dwarf);
        var old = legacy.history.AddCustomer(order, recurring: true);
        old.profile = new NpcProfile(); // as if saved without it
        SaveSystem.Save(legacy, null);
        var l1 = new Workshop(); SaveSystem.TryLoad(l1, out _);
        var l2 = new Workshop(); SaveSystem.TryLoad(l2, out _);
        var a1 = l1.history.Customer(old.id); var a2 = l2.history.Customer(old.id);
        Check(a1.profile.IsSet && a1.name == order.customerName && a1.race == Race.Dwarf && a1.archetypeIndex == order.archetypeIndex,
              $"an old save's customer gets a profile that keeps who they were ({a1.Title})");
        Check(a1.profile.story == a2.profile.story, "...the same one on every load");
    }

    private static void Lives()
    {
        var w = new Workshop();
        foreach (var m in ForgeMaterials.All) w.Unlock(m);
        var h = w.history;
        Random.InitState(77);

        // Day 2: Marcus-to-be orders; day 4 the smith delivers.
        var a = NpcStories.Meet(h, 2);
        string identity = $"{NpcStories.FullName(a)}|{a.race}|{a.profile.story}|{a.profile.trait1}{a.profile.trait2}";
        var o1 = NpcStories.OrderFor(a, h, 2, AllMetals, 60f);
        var s1 = h.RegisterForged(new WorkpieceData { weaponType = o1.weaponType, materialName = "Iron", itemName = "Iron " + o1.Weapon.name, durability = 85, balance = 85, sharpness = o1.Weapon.hasEdge ? 85 : 0 }, 3);
        h.Sell(s1, a, o1, 4);
        a.recurring = true;
        Check(h.CarriedBy(a) == s1 && h.Owner(s1) == a && a.purchases == 1, $"{s1.Label} belongs to {a.Title} and they know it");

        // Weeks pass; the weapon wears; they bring it in on day 11, the smith repairs it.
        for (int d = 5; d <= 11; d++) h.ProcessNewDay(d);
        if (s1.status == SwordStatus.OwnedByCustomer)
        {
            h.TakeInForRepair(s1, 11);
            h.FinishRepair(s1, 11);
            h.ReturnToOwner(s1, 11, repaired: true);
        }
        Check(s1.repairCount == 1 && h.Owner(s1) == a, $"{s1.Label} repaired on day 11 and back with {a.name}");

        // A life event, then they come back for another weapon on day 23.
        var news = NpcStories.LifeEvent(a, h, 18);
        a.profile.lifeEvents.Add(news);
        a.profile.lastLifeEventDay = 18;
        for (int d = 12; d <= 23; d++) h.ProcessNewDay(d);
        var o2 = NpcStories.OrderFor(a, h, 23, AllMetals, 70f);
        Debug.Log($"[NPC] day 23, {a.Title}: \"{o2.requestText}\"");
        Check(o2.requestText.Length <= 420, $"...and it still fits the dialogue box ({o2.requestText.Length} characters)");
        Check(o2.requestText.Contains("again") && o2.requestText.Contains(s1.Label), $"they remember the weapon you made them ({s1.Label})");
        Check(o2.requestText.Contains("Since we last spoke"), $"they tell you their news ({news.note})");
        Check(o2.customerId == a.id && o2.titleOverride == a.Title, "the new order is theirs, under their name");
        Check($"{NpcStories.FullName(a)}|{a.race}|{a.profile.story}|{a.profile.trait1}{a.profile.trait2}" == identity, "the same person all along (name, folk, story, temperament)");
        bool fits = o2.weaponType == a.profile.preferredWeapon || System.Array.IndexOf(a.Archetype.weapons, o2.weaponType) >= 0;
        Check(fits, $"their next order fits their story ({o2.Weapon.name} for {NpcStories.Describe(a)})");
        string block = NpcStories.OwnerBlock(a, h);
        Check(block.Contains(a.NpcId) && block.Contains(NpcStories.FullName(a)) && block.Contains("Latest"), $"the journal knows the owner:\n{block}");

        // Someone turned away remembers it.
        var b = NpcStories.Meet(h, 5);
        NpcStories.OrderFor(b, h, 5, AllMetals, 60f);
        b.profile.declinedDay = 5;
        var ob = NpcStories.OrderFor(b, h, 12, AllMetals, 60f);
        Check(ob.requestText.Contains("turned me away on day 5"), "someone you turned away says so when they come back");

        // A cousin of someone who bought from you mentions it.
        CustomerRecord relative = null;
        for (int i = 0; i < 60 && relative == null; i++)
        {
            var c = NpcStories.Meet(h, 24);
            if (c.profile.relatedTo == a.id) relative = c;
        }
        if (relative != null)
        {
            string line = NpcStories.RelationLine(relative, h);
            Check(line != null && line.Contains(a.name) && line.Contains(s1.Label), $"{NpcStories.Title(relative)}: \"{line}\"");
        }
        else Check(false, "a relative of a past buyer turns up among 60 new faces");

        // Several regulars across many days: they keep who they are while their lives move on.
        var regulars = new List<CustomerRecord>();
        for (int i = 0; i < 6; i++) { var c = NpcStories.Meet(h, 1); NpcStories.OrderFor(c, h, 1, AllMetals, 60f); regulars.Add(c); }
        var names = regulars.Select(c => NpcStories.FullName(c) + c.race + c.profile.surname).ToList();
        for (int d = 25; d <= 80; d++) h.ProcessNewDay(d);
        int events = regulars.Sum(c => c.profile.lifeEvents.Count);
        Check(events > 0, $"their lives move on over eight weeks ({events} life events among 6 people: {string.Join("; ", regulars.SelectMany(c => c.profile.lifeEvents.Select(e => $"{c.name}: {e.note}")).Take(6))})");
        Check(regulars.Select(c => NpcStories.FullName(c) + c.race + c.profile.surname).SequenceEqual(names), "...without becoming different people");
        Check(h.events.Exists(e => e.type == HistoryEventType.LifeEvent && e.customerId != 0), "life events are in the history");
        Check(h.Validate() == null, "history consistent");
    }
}
