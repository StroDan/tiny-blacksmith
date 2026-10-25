using System;
using System.Collections.Generic;
using UnityEngine;

// The smith's memory of every sword they ever finished and every customer who walked off with one.
//
// STORAGE vs HISTORY: the rack (Storage) only ever holds swords that are physically at the smithy right now -
// ones the smith owns (InStorage) and customers' swords left in for repair (NeedsRepair). Every other record is
// history only. Storage is never edited directly: it is derived from the records' status, and every status
// change goes through one of the ownership methods below, which update the record and the rack together.
// That makes "sold but still on the rack" / "on the rack twice" impossible by construction.
//
// Plain serializable data (no scene references), saved as JSON by SaveSystem.
public enum SwordStatus
{
    InStorage,       // on the rack, belongs to the smith
    OwnedByCustomer, // out in the world with its owner
    NeedsRepair,     // the owner left it at the smithy to be repaired - on the rack, but NOT the smith's to sell
    Destroyed,       // broke while its owner was using it
    Scrapped,        // the smith tossed it in the scrap crate
}

public enum HistoryEventType
{
    Forged,
    WeaponPurchased,
    WeaponUsed,
    WeaponDamaged,
    WeaponReturnedForRepair,
    WeaponRepaired,
    WeaponReturnedToOwner,
    RepairAbandoned,
    NewOrderRequested,
    WeaponDestroyed,
    WeaponScrapped,
    CameBack,         // its owner came back to the smithy with it (a story, a repair)
    // About a person, not a weapon (swordId 0) - appended, the enum is saved by number:
    FirstMet,         // they walked up to the table for the first time
    OrderDeclined,    // the smith turned their order down
    LifeEvent,        // something happened in their own life (NpcStories.LiveADay)
    WeaponReserved,   // set aside on the rack for an order (Workshop.Assign)
    WeaponUnreserved, // the reservation was lifted (unassigned by the smith, or the order fell through)
    WordOfMouth,      // they talked about the smith - sent someone, or grumbled (WorldDirector referrals)
    BecameNotable,    // the weapon earned a place among the smith's notable works (WorldDirector.MarkNotable)
}

[Serializable]
public class HistoryEvent
{
    public int day;
    public HistoryEventType type;
    public int swordId;    // 0 = not about a particular sword
    public int customerId; // 0 = not about a particular customer
    public string note;
}

[Serializable]
public class SwordRecord
{
    public int id;
    public WorkpieceData sword; // current stats - the very object that hangs in Storage while it's at the smithy
    public float originalDurability, originalBalance, originalSharpness;
    public int createdDay;
    public SwordStatus status;
    public int ownerId;           // CustomerRecord.id, 0 = the smith
    public CustomerOrder order;   // what its buyer originally asked for (null until sold)
    public int dayDelivered;
    // The day its owner comes back with it (rolled once when it changes hands, then fixed): -1 = never,
    // 0 = not rolled (older saves - fixed up in AfterLoad).
    public int returnDay;
    public bool storyTold;
    public string legendName;     // set if the sword did something legendary (see SwordStories)
    public float condition = 100f; // how worn the blade is from use, 100 = fresh from the forge / repair
    public int repairCount;
    public int timesReturned;
    public int rackOrder;         // position on the rack while at the smithy (higher = hung up more recently)
    // The order this weapon is set aside for (CustomerOrder.id, 0 = none). THE one link between orders and weapons:
    // an order's weapons are the ones pointing at it (Workshop.AssignedTo). A reserved weapon of the smith's stays on
    // the rack (InStorage) but can't be sold to anyone else, scrapped or given to another order; a repair order's
    // weapon (NeedsRepair) points at its repair order the same way.
    public int reservedForOrder;
    public bool IsReserved => reservedForOrder > 0;
    // A notable weapon (the journal stars it): exceptional work, an important owner, a long and eventful life.
    public bool notable;
    public string notableReason;  // why, in a few words (the first reason it earned)
    public int eventUses;         // city events it's been through with its owner

    // "Sword #0001", "Dagger #0012" - the weapon type is part of its name everywhere it's mentioned.
    public string Label => $"{(sword != null ? sword.Weapon.name : "Sword")} #{id:0000}";
    public bool AtSmithy => status == SwordStatus.InStorage || status == SwordStatus.NeedsRepair;
    public bool IsOut => status == SwordStatus.OwnedByCustomer;
    public bool AwaitingRepairWork => status == SwordStatus.NeedsRepair && condition < 100f;
    public bool RepairedAwaitingPickup => status == SwordStatus.NeedsRepair && condition >= 100f;
    public bool ReturnDue(int day) => status == SwordStatus.OwnedByCustomer && returnDay > 0 && day >= returnDay;
    public bool StoryDue(int day) => ReturnDue(day) && !storyTold;

    public string ConditionText =>
        condition >= 90f ? "Like new" : condition >= 60f ? "Well used" : condition >= 30f ? "Worn and dull" : condition > 0f ? "Badly damaged" : "Broken";
}

[Serializable]
public class CustomerRecord
{
    public int id;
    public string name;
    public int archetypeIndex;
    public Race race;          // Human for customers saved before races existed
    public Color hair;
    public bool recurring;     // recurring customers come back: repairs, follow-up orders. Others buy once and move on.
    public int nextVisitDay;   // won't come back before this day (so one customer doesn't hog the table)
    public int lastOrderDay;
    public int followUpForSwordId; // a destroyed/worn-out sword they still want to replace (0 = none)
    public int purchases;          // weapons they've bought from the smith
    // Who they are as a person - background, temperament, purse, preferences, relations, life events (NpcStories).
    public NpcProfile profile = new NpcProfile();

    public CustomerArchetype Archetype => CustomerArchetype.All[Mathf.Clamp(archetypeIndex, 0, CustomerArchetype.All.Length - 1)];
    public string NpcId => $"NPC_{id:0000}";
    public string Title => profile != null && profile.IsSet ? NpcStories.Title(this) : $"{name} the {CustomerOrder.Describe(race, Archetype)}";
}

[Serializable]
public class BlacksmithHistory
{
    public const float RepairThreshold = 45f;   // a recurring customer brings the sword in once it's this worn
    public const int MaxRecurringCustomers = 4; // only a few customers get a continuing story
    public const int RepairFeeBase = 6;

    public int nextSwordId = 1;
    public int nextCustomerId = 1;
    public int nextRackOrder = 1;
    public List<SwordRecord> swords = new List<SwordRecord>();
    public List<CustomerRecord> customers = new List<CustomerRecord>();
    public List<HistoryEvent> events = new List<HistoryEvent>();

    // What physically hangs on the rack, oldest first (derived from the records - never serialized).
    [NonSerialized] private List<WorkpieceData> storage;
    public IReadOnlyList<WorkpieceData> Storage { get { if (storage == null) RebuildStorage(); return storage; } }

    // ---- lookups -------------------------------------------------------------------------------

    public SwordRecord Find(int swordId) => swordId <= 0 ? null : swords.Find(r => r.id == swordId);
    public SwordRecord Find(WorkpieceData sword) => sword == null ? null : swords.Find(r => r.sword == sword);
    public CustomerRecord Customer(int customerId) => customerId <= 0 ? null : customers.Find(c => c.id == customerId);
    public CustomerRecord Owner(SwordRecord r) => r == null ? null : Customer(r.ownerId);

    // Swords on the rack the smith is actually allowed to sell or scrap (not someone else's repair job).
    public bool IsSmithsOwn(WorkpieceData sword) { var r = Find(sword); return r != null && r.status == SwordStatus.InStorage; }
    public int SmithOwnedCount { get { int n = 0; foreach (var r in swords) if (r.status == SwordStatus.InStorage) n++; return n; } }
    public int SoldCount { get { int n = 0; foreach (var r in swords) if (r.ownerId != 0) n++; return n; } }

    public SwordRecord StoryDue(int day) => swords.Find(r => r.StoryDue(day));

    public List<HistoryEvent> EventsFor(SwordRecord r) => events.FindAll(e => e.swordId == r.id);
    public List<HistoryEvent> EventsFor(CustomerRecord c) => events.FindAll(e => e.customerId == c.id);

    // The sword a customer currently carries (if any).
    public SwordRecord CarriedBy(CustomerRecord c) => c == null ? null : swords.FindLast(r => r.ownerId == c.id && r.status == SwordStatus.OwnedByCustomer);

    // ---- ownership: the ONLY places a sword's status/owner (and therefore the rack) changes ------

    // A finished sword hung on the rack from the result screen: it gets its permanent ID here.
    public SwordRecord RegisterForged(WorkpieceData sword, int day)
    {
        if (sword == null) throw new ArgumentNullException(nameof(sword));
        if (Find(sword) != null) throw new InvalidOperationException("Sword is already registered.");
        var r = new SwordRecord
        {
            id = nextSwordId++,
            sword = sword,
            originalDurability = sword.durability,
            originalBalance = sword.balance,
            originalSharpness = sword.sharpness,
            createdDay = day,
            status = SwordStatus.InStorage,
            rackOrder = nextRackOrder++,
        };
        sword.id = r.id;
        swords.Add(r);
        Log(day, HistoryEventType.Forged, r, null, $"Forged from {sword.materialName.ToLower()}");
        RebuildStorage();
        return r;
    }

    // A new customer becomes a record the first time they buy something (or a recurring one orders again).
    public CustomerRecord AddCustomer(CustomerOrder order, bool recurring)
    {
        var c = new CustomerRecord
        {
            id = nextCustomerId++,
            name = order.customerName,
            archetypeIndex = order.archetypeIndex,
            race = order.race,
            hair = order.hair,
            recurring = recurring,
        };
        customers.Add(c);
        order.customerId = c.id;
        NpcStories.EnsureProfile(c, this);
        return c;
    }

    public int RecurringCount { get { int n = 0; foreach (var c in customers) if (c.recurring) n++; return n; } }

    // The very first buyer always gets a continuing story (so the player sees the payoff early), then about
    // every second one, up to a handful.
    public bool ShouldBecomeRecurring(float roll) => RecurringCount == 0 || (RecurringCount < MaxRecurringCustomers && roll < 0.5f);

    public void Sell(SwordRecord r, CustomerRecord buyer, CustomerOrder order, int day)
    {
        Require(r, SwordStatus.InStorage, "sell");
        if (buyer == null) throw new ArgumentNullException(nameof(buyer));
        if (r.reservedForOrder != 0 && r.reservedForOrder != order.id)
            throw new InvalidOperationException($"{r.Label} is reserved for Order #{r.reservedForOrder:0000} - it can't go to anyone else.");
        r.reservedForOrder = 0; // delivered: the reservation is fulfilled
        r.status = SwordStatus.OwnedByCustomer;
        r.ownerId = buyer.id;
        r.order = order;
        r.dayDelivered = day;
        r.condition = 100f;
        r.storyTold = false;
        order.customerId = buyer.id;
        buyer.lastOrderDay = day;
        buyer.purchases++;
        buyer.nextVisitDay = day + 2;
        buyer.followUpForSwordId = 0;
        r.returnDay = RollReturn(buyer, day, first: true);
        Log(day, HistoryEventType.WeaponPurchased, r, buyer, order.id > 0 ? order.Label : order.requestText);
        RebuildStorage();
    }

    // ---- customers coming back ------------------------------------------------------------------
    // Whether - and when - a weapon's owner comes back to the smithy with it is a surprise: decided once (when it
    // changes hands, or after their last visit) from their race's habits and then left alone. A regular (recurring
    // customer) is more likely to come back than someone passing through; nothing about it is recalculated daily.
    public const float ReturnChanceRegular = 0.85f;
    public const float LaterReturnChance = 0.6f;         // a regular coming back AGAIN (for a repair) after their story
    public static Vector2Int LaterReturnDays = new Vector2Int(4, 10);

    public static int RollReturn(CustomerRecord owner, int day, bool first)
    {
        var race = RaceInfo.Get(owner != null ? owner.race : Race.Human);
        bool regular = owner != null && owner.recurring;
        float chance = first ? (regular ? ReturnChanceRegular : race.returnChance) : (regular ? LaterReturnChance : 0f);
        if (UnityEngine.Random.value >= chance) return -1;
        var range = first ? race.returnDays : LaterReturnDays;
        return day + UnityEngine.Random.Range(range.x, range.y + 1);
    }

    // Their visit is over: roll the next one (or never).
    public void ScheduleNextReturn(SwordRecord r, int day) => r.returnDay = RollReturn(Owner(r), day, first: false);

    public void LogCameBack(SwordRecord r, int day)
        => Log(day, HistoryEventType.CameBack, r, Owner(r), $"{Owner(r)?.name ?? "Its owner"} came back with it");

    public void Scrap(SwordRecord r, int day)
    {
        Require(r, SwordStatus.InStorage, "scrap");
        if (r.IsReserved) throw new InvalidOperationException($"{r.Label} is reserved for Order #{r.reservedForOrder:0000} - unassign it first.");
        r.status = SwordStatus.Scrapped;
        Log(day, HistoryEventType.WeaponScrapped, r, null, "Tossed in the scrap crate");
        RebuildStorage();
    }

    // The owner hands the sword over the counter to be repaired: it's back at the smithy (on the rack), still theirs.
    public void TakeInForRepair(SwordRecord r, int day)
    {
        Require(r, SwordStatus.OwnedByCustomer, "take in for repair");
        r.status = SwordStatus.NeedsRepair;
        r.timesReturned++;
        r.rackOrder = nextRackOrder++;
        Log(day, HistoryEventType.WeaponReturnedForRepair, r, Owner(r), $"Brought back {r.ConditionText.ToLower()}");
        RebuildStorage();
    }

    // The grindstone work is done - same sword, same ID, one more repair on its record.
    public void FinishRepair(SwordRecord r, int day)
    {
        Require(r, SwordStatus.NeedsRepair, "repair");
        r.condition = 100f;
        r.repairCount++;
        Log(day, HistoryEventType.WeaponRepaired, r, Owner(r), $"Repaired (repair #{r.repairCount})");
    }

    // Back over the counter to its owner - repaired or not (repaired=false: they gave up waiting and took it back).
    public void ReturnToOwner(SwordRecord r, int day, bool repaired)
    {
        Require(r, SwordStatus.NeedsRepair, "return");
        r.status = SwordStatus.OwnedByCustomer;
        r.reservedForOrder = 0; // the repair order is over, one way or the other
        var owner = Owner(r);
        if (owner != null) owner.nextVisitDay = day + 2;
        ScheduleNextReturn(r, day);
        Log(day, repaired ? HistoryEventType.WeaponReturnedToOwner : HistoryEventType.RepairAbandoned, r, owner,
            repaired ? "Handed back to its owner" : "Taken back unrepaired");
        RebuildStorage();
    }

    public void Destroy(SwordRecord r, int day, string note)
    {
        Require(r, SwordStatus.OwnedByCustomer, "destroy");
        r.status = SwordStatus.Destroyed;
        r.condition = 0f;
        var owner = Owner(r);
        if (owner != null) owner.followUpForSwordId = r.id; // they'll want a replacement
        Log(day, HistoryEventType.WeaponDestroyed, r, owner, note);
    }

    public void LogUse(SwordRecord r, int day, string note) => Log(day, HistoryEventType.WeaponUsed, r, Owner(r), note);

    public void LogNewOrder(CustomerRecord c, int day, int previousSwordId, string note)
    {
        c.lastOrderDay = day;
        c.followUpForSwordId = 0;
        events.Add(new HistoryEvent { day = day, type = HistoryEventType.NewOrderRequested, swordId = previousSwordId, customerId = c.id, note = note });
    }

    // ---- the passing of days ---------------------------------------------------------------------

    // Swords out in the world wear down a little every day - sturdy blades slowly, flimsy ones fast, and a
    // poor edge goes dull on top of that. A recurring customer's sword can wear all the way out (they come
    // in for a repair long before that if the smith lets them); a one-off customer's just gets well used.
    public static float WearPerDay(WorkpieceData s)
        => Mathf.Lerp(16f, 5f, Mathf.Clamp01(s.durability / 100f)) + (s.Weapon.hasEdge && s.sharpness < 50f ? 4f : 0f);

    public void ProcessNewDay(int day)
    {
        foreach (var r in swords)
        {
            if (r.status != SwordStatus.OwnedByCustomer || r.dayDelivered >= day) continue;
            var owner = Owner(r);
            bool recurring = owner != null && owner.recurring;
            float before = r.condition;
            r.condition = Mathf.Max(recurring ? 0f : 25f, r.condition - WearPerDay(r.sword));

            if (before >= RepairThreshold && r.condition < RepairThreshold)
                Log(day, HistoryEventType.WeaponDamaged, r, owner, SwordStories.DamageNote(r));
        }
        // Someone due back with a weapon that's still in good shape has no reason to come in yet: a later day.
        foreach (var r in swords)
            if (r.ReturnDue(day) && r.storyTold && r.condition >= RepairThreshold) r.returnDay = day + UnityEngine.Random.Range(2, 6);
        // A recurring customer who never got their sword repaired eventually wears it out completely.
        foreach (var r in swords.ToArray())
            if (r.status == SwordStatus.OwnedByCustomer && r.condition <= 0f)
                Destroy(r, day, "Worn out completely - it finally gave way");
        // And the people who own them have lives of their own.
        NpcStories.LiveADay(this, day);
    }

    // ---- consistency -------------------------------------------------------------------------------

    public void RebuildStorage()
    {
        if (storage == null) storage = new List<WorkpieceData>();
        storage.Clear();
        var atSmithy = swords.FindAll(r => r.AtSmithy);
        atSmithy.Sort((a, b) => a.rackOrder.CompareTo(b.rackOrder));
        foreach (var r in atSmithy) storage.Add(r.sword);
    }

    // JsonUtility writes null strings as "" and can't leave nested objects null - put those back.
    public void AfterLoad()
    {
        if (swords == null) swords = new List<SwordRecord>();
        if (customers == null) customers = new List<CustomerRecord>();
        if (events == null) events = new List<HistoryEvent>();
        foreach (var r in swords)
        {
            if (string.IsNullOrEmpty(r.legendName)) r.legendName = null;
            if (string.IsNullOrEmpty(r.notableReason)) r.notableReason = null;
            if (r.order != null && string.IsNullOrEmpty(r.order.customerName)) r.order = null;
            r.order?.AfterLoad();
            if (r.sword == null) r.sword = new WorkpieceData();
            if (r.returnDay == 0 && r.status == SwordStatus.OwnedByCustomer) r.returnDay = r.storyTold ? -1 : r.dayDelivered + 3; // older saves
            r.sword.id = r.id;
        }
        // Counters can never fall behind the data (protects against hand-edited or older saves).
        foreach (var r in swords) { nextSwordId = Mathf.Max(nextSwordId, r.id + 1); nextRackOrder = Mathf.Max(nextRackOrder, r.rackOrder + 1); }
        foreach (var c in customers) nextCustomerId = Mathf.Max(nextCustomerId, c.id + 1);
        foreach (var c in customers)
        {
            if (c.purchases == 0) c.purchases = swords.FindAll(r => r.ownerId == c.id).Count; // saves from before the count existed
            if (c.profile != null && c.profile.lifeEvents == null) c.profile.lifeEvents = new List<NpcLifeEvent>();
            NpcStories.EnsureProfile(c, this); // only fills in people saved before profiles existed - a saved story is never rewritten
        }
        RebuildStorage();
    }

    // Returns null if everything agrees, otherwise a description of the first problem found.
    public string Validate()
    {
        var rack = new HashSet<WorkpieceData>(Storage);
        if (rack.Count != Storage.Count) return "a sword hangs on the rack twice";
        var ids = new HashSet<int>();
        var objects = new HashSet<WorkpieceData>();
        foreach (var r in swords)
        {
            if (r.id <= 0 || !ids.Add(r.id)) return $"duplicate or invalid sword id {r.id}";
            if (r.sword == null || !objects.Add(r.sword)) return $"{r.Label} shares its sword object with another record";
            if (r.sword.id != r.id) return $"{r.Label} sword data carries id {r.sword.id}";
            bool owned = r.status == SwordStatus.OwnedByCustomer || r.status == SwordStatus.NeedsRepair;
            if (owned && Customer(r.ownerId) == null) return $"{r.Label} is {r.status} but has no owner";
            if (r.status == SwordStatus.InStorage && r.ownerId != 0) return $"{r.Label} is on the rack but still owned by customer {r.ownerId}";
            if (r.IsReserved && r.status != SwordStatus.InStorage && r.status != SwordStatus.NeedsRepair) return $"{r.Label} is {r.status} but still reserved for order {r.reservedForOrder}";
            bool onRack = rack.Contains(r.sword);
            if (onRack != r.AtSmithy) return $"{r.Label} is {r.status} but {(onRack ? "is" : "isn't")} on the rack";
        }
        return null;
    }

    private static void Require(SwordRecord r, SwordStatus expected, string action)
    {
        if (r == null) throw new ArgumentNullException(nameof(r));
        if (r.status != expected)
            throw new InvalidOperationException($"Can't {action} {r.Label}: it is {r.status}, expected {expected}.");
    }

    // Something about a person rather than a weapon (first visit, a declined order, their own life events).
    public void LogPerson(int day, HistoryEventType type, CustomerRecord c, string note) => Log(day, type, null, c, note);
    public void LogWeapon(int day, HistoryEventType type, SwordRecord r, CustomerRecord c, string note) => Log(day, type, r, c, note);

    private void Log(int day, HistoryEventType type, SwordRecord r, CustomerRecord c, string note)
        => events.Add(new HistoryEvent { day = day, type = type, swordId = r != null ? r.id : 0, customerId = c != null ? c.id : 0, note = note });
}
