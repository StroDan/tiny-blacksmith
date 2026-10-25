using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Everything that has to survive closing the game - plain data only, never scene objects. The rack isn't
// saved on its own: it's rebuilt from the sword records' status on load (BlacksmithHistory.RebuildStorage),
// so a save can't disagree with itself about who owns which sword.
[System.Serializable]
public class SaveData
{
    public int version = 1;
    public int gold;
    public int day;
    public float reputation;
    public int mark;
    public int lastCollectorDay;
    public float hour;       // 0 in saves from before the clock existed -> morning
    public int ordersToday;
    public int visitsToday;  // customers with new work already come by today
    public int timeSeed;     // the save's own pattern of arrival times (0 in older saves -> a new one)
    public DayStats today;   // today's tally so far (null in older saves)
    public int furnaceLevel, grindstoneLevel, toolsLevel; // workshop upgrades (shorter work)
    public List<string> unlockedMetals;  // null/empty in saves from before material progression -> all unlocked
    public int[] stock;
    public int traderDay;    // unused since the Material Order Book replaced the evening trader (old saves still carry it)
    public int[] supply;     // the supplier's stock left today (null in older saves -> rolled fresh)
    public int supplyDay;
    public int[] ordered;    // paid for, arriving with tomorrow's delivery
    public int[] crate;      // delivered, still in the crate by the storage
    public int nextEventDay;
    public bool hasChallenge;
    public CustomerOrder challenge;
    public int challengeDay;
    public int challengeCheckedDay;
    public List<WorkpieceData> scrap = new List<WorkpieceData>();
    public BlacksmithHistory history = new BlacksmithHistory();
    // The order book: every accepted order still open (its weapons are the swords whose reservedForOrder points at it).
    public List<CustomerOrder> orders = new List<CustomerOrder>();
    public int nextOrderId;
    // Saves from before the order book had one order at a time, with the customer waiting at the table - migrated on load.
    public bool hasPendingOrder;
    public CustomerOrder pendingOrder;
    // The living world: seed, city events, word of mouth, first sightings, the offer standing at the table (null in older
    // saves -> WorldDirector.EnsureWorld gives the game one, keeping everything it already had).
    public WorldState world;
}

// One JSON file in Application.persistentDataPath. Set the TB_SAVE_PATH environment variable to point it
// somewhere else (the headless tests do, so they never touch a real save), or "none" to disable saving.
public static class SaveSystem
{
    public const string FileName = "tinyblacksmith_save.json";

    public static string PathOrNull
    {
        get
        {
            string over = System.Environment.GetEnvironmentVariable("TB_SAVE_PATH");
            if (over == "none") return null;
            return string.IsNullOrEmpty(over) ? Path.Combine(Application.persistentDataPath, FileName) : over;
        }
    }

    // (The second argument is only there for old callers - accepted orders are saved from shop.orders.)
    public static void Save(Workshop shop, CustomerOrder unused = null)
    {
        string path = PathOrNull;
        if (path == null) return;
        var data = new SaveData
        {
            gold = shop.gold,
            day = shop.day,
            reputation = shop.reputation,
            mark = shop.mark,
            lastCollectorDay = shop.lastCollectorDay,
            hour = shop.hour,
            ordersToday = shop.ordersToday,
            visitsToday = shop.visitsToday,
            timeSeed = shop.timeSeed,
            today = shop.today,
            furnaceLevel = shop.furnaceLevel,
            grindstoneLevel = shop.grindstoneLevel,
            toolsLevel = shop.toolsLevel,
            unlockedMetals = new List<string>(shop.unlockedMetals),
            stock = (int[])shop.stock.Clone(),
            supply = (int[])shop.supply.Clone(),
            supplyDay = shop.supplyDay,
            ordered = (int[])shop.ordered.Clone(),
            crate = (int[])shop.crate.Clone(),
            nextEventDay = shop.nextEventDay,
            hasChallenge = shop.challenge != null,
            challenge = shop.challenge,
            challengeDay = shop.challengeDay,
            challengeCheckedDay = shop.challengeCheckedDay,
            scrap = new List<WorkpieceData>(shop.scrap),
            history = shop.history,
            orders = new List<CustomerOrder>(shop.orders),
            nextOrderId = shop.nextOrderId,
            world = shop.world,
        };
        try
        {
            // Write to a temp file first so a crash mid-write can't leave a half-written save behind.
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(data, true));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"SaveSystem: couldn't save to {path}: {e.Message}");
        }
    }

    // Loads into `shop` and returns the accepted order the customer is still waiting on (or null).
    // Returns false (and leaves `shop` untouched) if there's no usable save.
    public static bool TryLoad(Workshop shop, out CustomerOrder pendingOrder)
    {
        pendingOrder = null;
        string path = PathOrNull;
        if (path == null || !File.Exists(path)) return false;
        SaveData data;
        try
        {
            data = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"SaveSystem: couldn't read {path}: {e.Message} - starting a new game.");
            return false;
        }
        if (data == null || data.history == null) return false;

        data.history.AfterLoad();
        string problem = data.history.Validate();
        if (problem != null)
        {
            Debug.LogWarning($"SaveSystem: save at {path} is inconsistent ({problem}) - starting a new game.");
            return false;
        }

        shop.gold = data.gold;
        shop.day = Mathf.Max(1, data.day);
        shop.reputation = data.reputation;
        shop.mark = data.mark;
        shop.lastCollectorDay = data.lastCollectorDay;
        var time = TimeSettings.Current;
        shop.hour = data.hour > 0f ? Mathf.Clamp(data.hour, 0.01f, time.latestHour) : time.dayStart; // exactly where it was saved
        shop.ordersToday = Mathf.Max(0, data.ordersToday);
        shop.visitsToday = Mathf.Max(0, data.visitsToday);
        if (data.timeSeed != 0) shop.timeSeed = data.timeSeed;
        shop.today = data.today ?? new DayStats { goldAtStart = data.gold, repAtStart = data.reputation };
        shop.furnaceLevel = Mathf.Max(0, data.furnaceLevel);
        shop.grindstoneLevel = Mathf.Max(0, data.grindstoneLevel);
        shop.toolsLevel = Mathf.Max(0, data.toolsLevel);
        shop.unlockedMetals.Clear();
        for (int i = 0; i < shop.stock.Length; i++) shop.stock[i] = 0;
        if (data.unlockedMetals == null || data.unlockedMetals.Count == 0)
        {
            // A game saved before materials had to be unlocked: keep everything it could already use.
            foreach (var m in ForgeMaterials.All) { shop.unlockedMetals.Add(m.name); shop.stock[m.index] = 3; }
        }
        else
        {
            shop.unlockedMetals.AddRange(data.unlockedMetals);
            if (data.stock != null) for (int i = 0; i < shop.stock.Length && i < data.stock.Length; i++) shop.stock[i] = Mathf.Max(0, data.stock[i]);
        }
        CopyCounts(data.supply, shop.supply);
        shop.supplyDay = data.supply != null ? data.supplyDay : 0;
        CopyCounts(data.ordered, shop.ordered);
        CopyCounts(data.crate, shop.crate);
        shop.nextEventDay = data.nextEventDay > 0 ? data.nextEventDay : Workshop.FirstEventDay;
        shop.challenge = data.hasChallenge && data.challenge != null ? data.challenge : null;
        shop.challenge?.AfterLoad();
        shop.challengeDay = data.challengeDay;
        shop.challengeCheckedDay = data.challengeCheckedDay;
        shop.scrap.Clear();
        if (data.scrap != null) shop.scrap.AddRange(data.scrap);
        shop.history = data.history;
        shop.world = data.world ?? new WorldState();
        shop.world.AfterLoad();

        shop.orders.Clear();
        shop.nextOrderId = Mathf.Max(1, data.nextOrderId);
        if (data.orders != null)
            foreach (var o in data.orders)
            {
                if (o == null) continue;
                o.AfterLoad();
                if (o.IsActive) shop.orders.Add(o);
            }
        // The id counter can never fall behind anything already numbered.
        foreach (var o in shop.orders) shop.nextOrderId = Mathf.Max(shop.nextOrderId, o.id + 1);
        foreach (var r in shop.history.swords)
        {
            shop.nextOrderId = Mathf.Max(shop.nextOrderId, r.reservedForOrder + 1);
            if (r.order != null) shop.nextOrderId = Mathf.Max(shop.nextOrderId, r.order.id + 1);
        }
        if (shop.challenge != null)
        {
            if (shop.challenge.id <= 0) { shop.challenge.id = shop.nextOrderId++; shop.challenge.status = OrderStatus.WaitingForWeapon; }
            else shop.nextOrderId = Mathf.Max(shop.nextOrderId, shop.challenge.id + 1);
        }

        // An old save's single order (the customer used to wait at the table): into the book, with an id and a return
        // day - its customer goes home and comes back. Its weapon was never chosen, so nothing is reserved yet (the
        // smith assigns one). A repair job only makes sense while its weapon is really still in for repair.
        if (data.hasPendingOrder && data.pendingOrder != null && data.pendingOrder.id <= 0)
        {
            var old = data.pendingOrder;
            old.AfterLoad();
            var repairSword = old.IsRepair ? shop.history.Find(old.repairSwordId) : null;
            if (!old.IsRepair || (repairSword != null && repairSword.status == SwordStatus.NeedsRepair))
            {
                if (old.customerId <= 0 || shop.history.Customer(old.customerId) == null) shop.history.AddCustomer(old, recurring: false);
                int deadline = old.deadlineDay;
                shop.Accept(old, shop.day);
                if (deadline > 0) old.deadlineDay = Mathf.Max(deadline, shop.day + 1);
                old.returnDay = shop.day + 1;
            }
        }
        foreach (var o in shop.orders) shop.RefreshStatus(o);
        WorldDirector.EnsureWorld(shop);
        string orderProblem = shop.ValidateOrders();
        if (orderProblem != null) Debug.LogWarning($"SaveSystem: order book inconsistent after load ({orderProblem})");
        return true;
    }

    private static void CopyCounts(int[] from, int[] to)
    {
        for (int i = 0; i < to.Length; i++) to[i] = from != null && i < from.Length ? Mathf.Max(0, from[i]) : 0;
    }

    // Is there a save file to load? (The main menu asks before offering Load Game / warning on New Game.)
    public static bool HasSave()
    {
        string path = PathOrNull;
        return path != null && File.Exists(path);
    }

    public static void Delete()
    {
        string path = PathOrNull;
        if (path != null && File.Exists(path)) File.Delete(path);
    }
}
