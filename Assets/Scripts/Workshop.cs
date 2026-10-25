using System.Collections.Generic;
using UnityEngine;

// Everything the smith builds up between swords: gold, days, reputation, the scrap crate, the maker's
// mark and the history of every sword they made (BlacksmithHistory - which also decides what hangs on the
// rack). Plain data, owned by ForgeGameManager (same non-Mono style as WorkpieceData / TimingBar), saved
// by SaveSystem.
public class Workshop
{
    public const int RackSize = 8;          // pegs on the storage rack
    public const int ScrapPrice = 3;        // what the Collector pays per ordinary failed blade
    public const int CrackedScrapPrice = 9; // ...and per cracked one - those are his favourites

    // ---- balance knobs (economy + progression) -------------------------------------------------
    public static int StartingGold = 30;
    public static int StartingOre = 6;            // ingots of the starting metal in the store at the start
    public static int StartingMetal = 0;          // ForgeMaterials.All index: Copper
    public static Vector2Int EventTraderGap = new Vector2Int(3, 5); // days between Event Trader visits
    public const int FirstEventDay = 3;
    public const int ChallengeCheckDelay = 2;    // the Event Trader comes back for the weapon from this many days on...
    public const int ChallengeDays = 5;          // ...until this many days after the challenge was accepted
    // Reputation: gains shrink as it grows (the last stars take real consistency); losses always hit in full.
    public const float RepSatisfied = 2f, RepGood = 3f, RepExcellent = 3.5f; // by how far the weapon beat the order
    public const float RepRefused = -6f;           // declining a customer's order - about two good orders' worth
    public const float RepWalkedOff = -7f;         // accepted, then kept them waiting until they gave up - worse than an honest no
    public const float RepRepairRefused = -2f;
    public const float RepChallenge = 6f;

    public int gold = StartingGold;
    public int day = 1;
    public float reputation = 40f; // 0..100, shown as 0-5 stars
    public int mark;               // 3x3 maker's mark, bit (row * 3 + col); 0 = not designed yet
    public int lastCollectorDay = -10;
    // ---- time (changed only by GameTime - see there; saved, so a loaded game resumes at the very same moment) ----
    public float hour;                        // clock hours, 14.5 = 14:30 (0 = not started yet: GameTime starts it at dayStart)
    public int visitsToday;                   // customers with new work who have come by today (GameTime.Arrivals)
    public int timeSeed = new System.Random().Next(1, 1000000); // this game's own pattern of arrival times
    public DayStats today = new DayStats();   // what today has brought so far (the End Day summary)
    public int ordersToday;                   // orders taken on (new weapons and repairs) since this day began
    // Workshop upgrades: each shortens its work a little (TimeSettings.upgradeTimeScale, diminishing). 0 = as built.
    public int furnaceLevel, grindstoneLevel, toolsLevel;

    // A new day's tally starts from where the purse and the name stand this morning.
    public void StartDay() => today = new DayStats { goldAtStart = gold, repAtStart = reputation };

    // Materials: which metals the smith can work (unlocked by Event Trader challenges) and the ingots in store.
    public readonly List<string> unlockedMetals = new List<string>();
    public int[] stock = new int[ForgeMaterials.All.Length];
    // The supplier (the Material Order Book by the storage rack): a limited stock each day, paid for when ordered,
    // delivered in a crate by the storage the next morning. What doesn't fit in the bins stays in the crate.
    public const int BinCapacity = 10;        // ingots of each metal the storage bins hold
    public int[] supply = new int[ForgeMaterials.All.Length];  // what the supplier can still send today
    public int supplyDay;                     // the day `supply` was rolled for (0 = not yet)
    public int[] ordered = new int[ForgeMaterials.All.Length]; // paid for today - arrives tomorrow morning
    public int[] crate = new int[ForgeMaterials.All.Length];   // delivered, still waiting in the crate by the storage
    public int nextEventDay = FirstEventDay;  // the earliest day the Event Trader may turn up
    public CustomerOrder challenge;           // the Event Trader's challenge in progress (null = none)
    public int challengeDay;                  // the day it was accepted
    public int challengeCheckedDay;           // the last day the Event Trader came to look

    public Workshop()
    {
        unlockedMetals.Add(ForgeMaterials.All[StartingMetal].name);
        stock[StartingMetal] = StartingOre;
    }

    public bool IsUnlocked(ForgeMaterialInfo m) => unlockedMetals.Contains(m.name);
    public ForgeMaterialInfo NextLockedMetal
    {
        get { foreach (var m in ForgeMaterials.All) if (!IsUnlocked(m)) return m; return null; }
    }
    public float BestQualityCap
    {
        get { float best = 0f; foreach (var m in ForgeMaterials.All) if (IsUnlocked(m)) best = Mathf.Max(best, m.maxQuality); return best; }
    }
    // The best any known metal can do for one stat (0 Durability, 1 Balance, 2 Sharpness) - metals differ per stat.
    public float BestCap(int stat)
    {
        float best = 0f;
        foreach (var m in ForgeMaterials.All) if (IsUnlocked(m)) best = Mathf.Max(best, m.Cap(stat));
        return best;
    }

    // The living world around the smithy (WorldDirector): seed, city events, word of mouth, what's been seen. Saved.
    public WorldState world = new WorldState();

    public int TotalStock { get { int n = 0; for (int i = 0; i < stock.Length; i++) if (IsUnlocked(ForgeMaterials.All[i])) n += stock[i]; return n; } }

    public void Unlock(ForgeMaterialInfo m) { if (!IsUnlocked(m)) unlockedMetals.Add(m.name); }

    // What the supplier can send in a day: only metals the smith has unlocked, a limited number of each - plenty
    // of the cheapest, a few of the next, one or two of the rarest. Varies a little from day to day.
    public int[] RollSupply()
    {
        var offer = new int[ForgeMaterials.All.Length];
        int rank = 0;
        foreach (var m in ForgeMaterials.All)
        {
            if (!IsUnlocked(m)) continue;
            offer[m.index] = rank == 0 ? Random.Range(4, 7) : rank == 1 ? Random.Range(2, 4) : Random.Range(1, 3);
            rank++;
        }
        return offer;
    }

    // Today's supply (rolled the first time anyone looks on a new day).
    public int[] Supply
    {
        get
        {
            // Seeded per save and day (a reload shows the same stock), swayed by the city's events.
            if (supplyDay != day) { supply = world != null && world.worldSeed != 0 ? WorldDirector.RollSupply(this) : RollSupply(); supplyDay = day; }
            return supply;
        }
    }

    // A metal unlocked today goes straight into today's supply too.
    public void AddToSupply(ForgeMaterialInfo m)
    {
        var today = Supply;
        if (today[m.index] == 0) today[m.index] = RollSupply()[m.index];
    }

    public int FreeSpace(int metal) => Mathf.Max(0, BinCapacity - stock[metal]);
    public int OrderedTotal { get { int n = 0; foreach (int c in ordered) n += c; return n; } }
    public int CrateTotal { get { int n = 0; foreach (int c in crate) n += c; return n; } }

    // Pays for one ingot from today's supply; it arrives with tomorrow's delivery. Returns why not, or null.
    public string OrderIngot(int metal)
    {
        var m = ForgeMaterials.All[metal];
        if (!IsUnlocked(m)) return $"You don't know how to work {m.name.ToLower()} yet.";
        if (Supply[metal] <= 0) return $"No more {m.name.ToLower()} to be had today - try again tomorrow.";
        if (gold < m.price) return $"{m.name} is {m.price} gold an ingot - you're short.";
        gold -= m.price;
        today.materialSpend += m.price;
        supply[metal]--;
        ordered[metal]++;
        return null;
    }

    // A new morning: yesterday's orders are in the crate by the storage. A smith with nothing to work, nothing on its
    // way and not enough gold for a single ingot would be stuck for good - the supplier adds two of the cheapest on
    // credit rather than lose a customer. Returns true when that happened.
    public bool ReceiveDelivery()
    {
        for (int i = 0; i < crate.Length; i++) { crate[i] += ordered[i]; ordered[i] = 0; }
        var cheapest = System.Array.Find(ForgeMaterials.All, IsUnlocked);
        if (cheapest != null && TotalStock == 0 && CrateTotal == 0 && gold < cheapest.price)
        {
            crate[cheapest.index] += 2;
            return true;
        }
        return false;
    }

    // Opening the crate: everything that fits goes into the bins; the rest stays in the crate. Returns what moved.
    public int[] CollectCrate()
    {
        var moved = new int[crate.Length];
        for (int i = 0; i < crate.Length; i++)
        {
            moved[i] = Mathf.Min(crate[i], FreeSpace(i));
            stock[i] += moved[i];
            crate[i] -= moved[i];
        }
        return moved;
    }

    // Would opening the crate right now move anything at all?
    public bool CrateCanUnload { get { for (int i = 0; i < crate.Length; i++) if (crate[i] > 0 && FreeSpace(i) > 0) return true; return false; } }

    // Ingots from somewhere else (the Event Trader's gift): into the bins, and whatever doesn't fit into the crate.
    public void AddIngots(int metal, int count)
    {
        int fit = Mathf.Min(count, FreeSpace(metal));
        stock[metal] += fit;
        crate[metal] += count - fit;
    }

    // "Copper ×4, Iron ×2" (only what's there), or null when there's nothing.
    public static string IngotList(int[] counts, string separator = ", ")
    {
        var parts = new List<string>();
        for (int i = 0; i < counts.Length && i < ForgeMaterials.All.Length; i++)
            if (counts[i] > 0) parts.Add($"{ForgeMaterials.All[i].name} ×{counts[i]}");
        return parts.Count == 0 ? null : string.Join(separator, parts);
    }

    // ---- orders ------------------------------------------------------------------------------------------
    // Accepted orders live here until they're done: CUSTOMER (CustomerRecord id) -> ORDER (id, status, days) ->
    // WEAPON(S) (the SwordRecords reserved for it). The customer doesn't wait at the table: they go about their life
    // and come back on order.returnDay. What they get is exactly the weapon(s) reserved for their order - never
    // "whatever matching blade was forged last". The Event Trader's challenge is an order too (same ids and
    // reservations) but stays in `challenge`, with her own schedule.
    public const int MaxActiveOrders = 5;   // customers stop bringing new work while the book is this full
    public const int OrderGraceDays = 2;    // on top of their patience: they come back a few times before giving up
    public int nextOrderId = 1;
    public readonly List<CustomerOrder> orders = new List<CustomerOrder>();

    public CustomerOrder FindOrder(int id)
    {
        if (id <= 0) return null;
        var o = orders.Find(x => x.id == id);
        return o ?? (challenge != null && challenge.id == id ? challenge : null);
    }

    // Orders a weapon can still be set aside for (repairs bring their own weapon).
    public List<CustomerOrder> AssignableOrders
    {
        get
        {
            var list = orders.FindAll(o => o.IsActive && !o.IsRepair && AssignedTo(o).Count < o.quantity);
            if (challenge != null && challenge.id > 0 && AssignedTo(challenge).Count < challenge.quantity) list.Add(challenge);
            return list;
        }
    }

    // The weapons that belong to an order - read from the weapons themselves, oldest id first.
    public List<SwordRecord> AssignedTo(CustomerOrder o)
    {
        if (o == null || o.id <= 0) return new List<SwordRecord>();
        var list = history.swords.FindAll(r => r.reservedForOrder == o.id);
        list.Sort((a, b) => a.id.CompareTo(b.id));
        return list;
    }

    public List<int> AssignedWeaponIds(CustomerOrder o) => AssignedTo(o).ConvertAll(r => r.id);

    public bool IsReady(CustomerOrder o)
    {
        if (o == null) return false;
        if (o.IsRepair) { var r = history.Find(o.repairSwordId); return r != null && r.RepairedAwaitingPickup && r.reservedForOrder == o.id; }
        return AssignedTo(o).Count >= o.quantity;
    }

    public void RefreshStatus(CustomerOrder o)
    {
        if (o != null && o.IsActive) o.status = IsReady(o) ? OrderStatus.WeaponReady : OrderStatus.WaitingForWeapon;
    }

    // The smith says yes: the order gets its id and goes in the book, and its customer goes off until returnDay.
    // A repair order reserves the customer's own weapon (already on the rack, see BlacksmithHistory.TakeInForRepair).
    // Deadlines: a normal order waits as long as its customer's patience (plus a little grace) - generous; a rush order
    // is due tomorrow evening; a contract gets about the days its weapons take at a steady couple a day
    // (TimeSettings.contractWeaponsPerDay) - never all at once.
    public void Accept(CustomerOrder o, int day) => Accept(o, day, hour);

    public void Accept(CustomerOrder o, int day, float atHour)
    {
        var s = TimeSettings.Current;
        o.id = nextOrderId++;
        o.acceptedDay = day;
        o.acceptedHour = atHour;
        if (o.quantity < 1) o.quantity = 1;
        o.deadlineHour = s.deadlineHour;
        if (o.rush) o.deadlineDay = day + 1;
        else if (o.quantity > 1) o.deadlineDay = day + Mathf.Max(2, Mathf.CeilToInt(o.quantity / Mathf.Max(0.5f, s.contractWeaponsPerDay)));
        else o.deadlineDay = day + o.Patience + OrderGraceDays;
        // They come back to collect: a day or two later - a rush or a contract on its due day.
        o.returnDay = o.rush || o.quantity > 1 ? o.deadlineDay : Mathf.Min(day + Random.Range(1, 3), o.deadlineDay);
        o.returnHour = GameTime.ReturnHourFor(o, o.returnDay);
        if (o.IsRepair)
        {
            var r = history.Find(o.repairSwordId);
            if (r != null) { r.reservedForOrder = o.id; history.LogWeapon(day, HistoryEventType.WeaponReserved, r, history.Customer(o.customerId), $"In for repair - {o.Label}"); }
        }
        if (!o.IsChallenge) orders.Add(o);
        o.status = OrderStatus.WaitingForWeapon;
        RefreshStatus(o);
    }

    // Why this weapon can't be set aside for this order (null = it can).
    public string CanAssign(CustomerOrder o, SwordRecord r)
    {
        if (o == null || !o.IsActive && !o.IsChallenge) return "That order isn't open any more.";
        if (r == null) return "There's no such weapon.";
        if (o.IsRepair) return "A repair order already has its weapon.";
        if (r.status != SwordStatus.InStorage) return $"{r.Label} isn't yours to give.";
        if (r.IsReserved) return $"{r.Label} is already reserved for Order #{r.reservedForOrder:0000}.";
        if (AssignedTo(o).Count >= o.quantity) return $"{o.Label} already has all its weapons.";
        if (r.sword.weaponType != o.weaponType) return $"{o.Label} is for {o.Weapon.WithArticle}, not {r.sword.Weapon.WithArticle}.";
        // Only the hard facts filter: the kind of weapon, and a metal they named. Whether its stats are good enough for
        // what they described is the smith's call - judged when the customer takes it (Judge), never revealed here.
        if (o.requiredMaterial != null && r.sword.materialName != o.requiredMaterial) return $"{o.Label} must be {o.requiredMaterial.ToLower()}, and {r.Label} is {r.sword.materialName.ToLower()}.";
        return null;
    }

    public string Assign(CustomerOrder o, SwordRecord r, int day)
    {
        string why = CanAssign(o, r);
        if (why != null) return why;
        r.reservedForOrder = o.id;
        history.LogWeapon(day, HistoryEventType.WeaponReserved, r, history.Customer(o.customerId), $"Reserved for {o.Label} ({o.customerName})");
        RefreshStatus(o);
        return null;
    }

    public void Unassign(SwordRecord r, int day)
    {
        if (r == null || !r.IsReserved || r.status != SwordStatus.InStorage) return;
        var o = FindOrder(r.reservedForOrder);
        r.reservedForOrder = 0;
        history.LogWeapon(day, HistoryEventType.WeaponUnreserved, r, o != null ? history.Customer(o.customerId) : null, $"No longer reserved for {o?.Label ?? "its order"}");
        RefreshStatus(o);
    }

    // The customer collects: exactly the weapons reserved for this order change hands; the order is completed.
    public List<SwordRecord> Deliver(CustomerOrder o, CustomerRecord buyer, int day)
    {
        if (!IsReady(o)) throw new System.InvalidOperationException($"{o.Label} isn't ready to hand over.");
        var weapons = AssignedTo(o);
        foreach (var r in weapons) history.Sell(r, buyer, o, day);
        o.status = OrderStatus.Completed;
        orders.Remove(o);
        return weapons;
    }

    // The order falls through (the customer gave up, or a challenge ran out): its reservations are lifted.
    // (A repair's weapon goes home with its owner - the caller does that, BlacksmithHistory.ReturnToOwner.)
    public void Close(CustomerOrder o, int day, OrderStatus how)
    {
        foreach (var r in AssignedTo(o))
            if (r.status == SwordStatus.InStorage) Unassign(r, day);
        o.status = how;
        if (how == OrderStatus.Completed) today.ordersCompleted++;
        orders.Remove(o);
    }

    // Null if every order and every reserved weapon agree with each other, otherwise the first problem.
    public string ValidateOrders()
    {
        var ids = new HashSet<int>();
        foreach (var o in orders)
        {
            if (o.id <= 0 || !ids.Add(o.id)) return $"duplicate or missing order id {o.id}";
            if (!o.IsActive) return $"{o.Label} is {o.status} but still in the book";
            if (o.customerId <= 0 || history.Customer(o.customerId) == null) return $"{o.Label} has no customer";
            if (AssignedTo(o).Count > o.quantity) return $"{o.Label} has more weapons than it asked for";
            if (o.IsRepair && history.Find(o.repairSwordId)?.reservedForOrder != o.id) return $"{o.Label}'s repair weapon isn't reserved for it";
        }
        foreach (var r in history.swords)
        {
            if (!r.IsReserved) continue;
            var o = FindOrder(r.reservedForOrder);
            if (o == null) return $"{r.Label} is reserved for an order that doesn't exist ({r.reservedForOrder})";
            if (o.IsRepair ? r.status != SwordStatus.NeedsRepair || o.repairSwordId != r.id : r.status != SwordStatus.InStorage)
                return $"{r.Label} ({r.status}) can't be reserved for {o.Label}";
        }
        return null;
    }

    public readonly List<WorkpieceData> scrap = new List<WorkpieceData>();
    public BlacksmithHistory history = new BlacksmithHistory();

    public bool HasMark => mark != 0;
    public int Stars => Mathf.Clamp(Mathf.RoundToInt(reputation / 20f), 0, 5);
    public float PayMultiplier => 0.75f + 0.5f * reputation / 100f; // 0.75x unknown smith .. 1.25x famous one

    public string Season => new[] { "Spring", "Summer", "Autumn", "Winter" }[((day - 1) / 4) % 4];

    public void AddReputation(float amount)
    {
        if (amount > 0f) amount *= Mathf.Lerp(1f, 0.15f, reputation / 100f); // each star is harder than the last
        reputation = Mathf.Clamp(reputation + amount, 0f, 100f);
    }

    // Reputation for a delivered order: judged against what THEY asked for (the required stats), not raw totals -
    // and worth more the harder the job was (beating a simple request by a mile isn't a masterpiece).
    // How the customer takes the weapon they're handed, judged on its real stats against what they (secretly) needed.
    public enum Verdict { Excellent, Good, Satisfied, Grudging, Refused }
    public const float GrudgingShortfall = 10f;   // up to this far short on their weakest need: they take it, unhappily
    public const float GrudgingPay = 0.6f;        // ...and pay this share (no tip)
    public const float RepGrudging = -2f;         // settling for less, and saying so
    public const float RepRefusedWeapon = -3f;    // handed something that won't do at all

    public static Verdict Judge(CustomerOrder order, WorkpieceData weapon)
    {
        if (weapon.cracked || weapon.weaponType != order.weaponType) return Verdict.Refused;
        if (order.requiredMaterial != null && weapon.materialName != order.requiredMaterial) return Verdict.Refused;
        float margin = SwordStories.Margin(order, weapon);
        if (margin >= 15f) return Verdict.Excellent;
        if (margin >= 5f) return Verdict.Good;
        if (margin >= 0f) return Verdict.Satisfied;
        // How far short they'll still (unhappily) take it depends on who they are (CustomerOrder.toleranceBonus).
        return margin >= -Mathf.Max(0f, GrudgingShortfall + order.toleranceBonus) ? Verdict.Grudging : Verdict.Refused;
    }

    public static float SatisfactionRep(CustomerOrder order, WorkpieceData weapon)
    {
        var verdict = Judge(order, weapon);
        if (verdict == Verdict.Grudging) return RepGrudging;
        if (verdict == Verdict.Refused) return RepRefusedWeapon;
        float rep = verdict == Verdict.Excellent ? RepExcellent : verdict == Verdict.Good ? RepGood : RepSatisfied;
        return rep * TierRepScale[Mathf.Clamp(order.tier, 0, TierRepScale.Length - 1)];
    }
    private static readonly float[] TierRepScale = { 0.8f, 1.0f, 1.15f, 1.3f };

    public string StarsText => $"Reputation {Stars}/5";

    public int CrackedScrap => scrap.FindAll(s => s.cracked).Count;
    public int ScrapValue => (scrap.Count - CrackedScrap) * ScrapPrice + CrackedScrap * CrackedScrapPrice;

    // What the customer hands over for a sword that meets their order: the job's base pay, scaled by the
    // metal and the smith's reputation, plus a tip if it beat the order comfortably and they didn't need
    // the numbers spelled out.
    // The promised pay for one weapon of the order in this metal, before judging it (what "Pays about" quotes).
    public float BasePay(CustomerOrder order, string materialName)
    {
        float pay = order.reward * ForgeMaterials.Find(materialName).valueScale * PayMultiplier;
        return order.detailsAsked ? pay * CustomerOrder.AskPayCut : pay;
    }

    public int Payment(CustomerOrder order, WorkpieceData sword, out int tip)
    {
        float pay = BasePay(order, sword.materialName);
        var verdict = Judge(order, sword);
        if (verdict == Verdict.Refused) { tip = 0; return 0; }
        if (verdict == Verdict.Grudging) pay *= GrudgingPay;
        tip = !order.detailsAsked && verdict == Verdict.Excellent ? Mathf.RoundToInt(pay * 0.2f) : 0;
        return Mathf.RoundToInt(pay);
    }

    // What a returning customer pays to have their sword worked back into shape: more for a badly worn
    // blade, more for a precious metal, and more for a famous smith.
    public int RepairFee(SwordRecord r)
    {
        float wornness = Mathf.Clamp(100f - r.condition, 0f, 100f);
        float fee = (BlacksmithHistory.RepairFeeBase + wornness * 0.15f) * ForgeMaterials.Find(r.sword.materialName).valueScale * PayMultiplier;
        return Mathf.Max(1, Mathf.RoundToInt(fee));
    }

    // A few recognisable shapes get a proper name; anything else is described by its dot count.
    public string MarkName
    {
        get
        {
            int count = 0;
            for (int i = 0; i < 9; i++) if ((mark & (1 << i)) != 0) count++;
            switch (mark)
            {
                case 0b010_111_010: return "cross";
                case 0b101_010_101: return "star";
                case 0b111_101_111: return "ring";
                case 0b111_111_111: return "sun";
                case 0b010_101_010: return "diamond";
                case 0b000_010_000: return "single-dot";
                case 0b010_010_010: return "sword";
                case 0b000_111_000: return "horizon";
                case 0b101_000_101: return "four-stones";
                case 0b111_010_010: return "hammer";
            }
            return $"{count}-dot";
        }
    }
}

// What one day has brought - shown when the smith ends it (and saved, so a reload mid-day keeps the tally).
[System.Serializable]
public class DayStats
{
    public int forged, repairs, ordersCompleted;
    public int goldAtStart, materialSpend;
    public float repAtStart, minutesWorked;
}
