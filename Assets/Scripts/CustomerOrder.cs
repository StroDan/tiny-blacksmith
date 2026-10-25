using System.Collections.Generic;
using UnityEngine;

// Who a customer is: what they do for a living decides which stats their weapon needs, and how serious the
// job is (the tier) decides how much of it. They never say "Balance >= 70" - they say "I'm entering the city
// fencing tournament", and the smith has to read between the lines (see CustomerOrder.PaperText).
// Each job also lists the weapons it makes sense for and the races who take it up; "{w}" in its lines is
// replaced by the weapon they want ("sword", "dagger"...), so every line reads right for every weapon listed.
// New jobs are only ever appended: orders and customers are saved by index.
public class CustomerArchetype
{
    public string title;
    public bool durability, balance, sharpness; // which stats the job actually leans on
    public string[] tierLines;                  // one request per tier (decent / good / great / almost perfect)
    public string[] swordNames;                 // what they call the weapon after it does something legendary
    public string triumph;                      // {0} = its new name, {w} = weapon - told when the key stat beat the need comfortably
    public string fine;                         // told when the weapon just about did the job
    public Color outfit;
    public bool wantsSteel;                     // sometimes asks for a steel weapon specifically
    public WeaponType[] weapons = { WeaponType.Sword }; // what they might ask for (repeats = more likely)
    public Race[] races = { Race.Human };               // who takes up this job

    public bool TakenUpBy(Race race) => System.Array.IndexOf(races, race) >= 0;

    public static readonly CustomerArchetype[] All =
    {
        new CustomerArchetype
        {
            title = "fencer", balance = true, outfit = new Color(0.20f, 0.35f, 0.62f),
            weapons = new[] { WeaponType.Sword, WeaponType.Sword, WeaponType.Dagger }, races = new[] { Race.Human, Race.Elf },
            tierLines = new[]
            {
                "I've just started fencing lessons. Nothing fancy - the {w} just shouldn't wobble in my hand.",
                "I'm duelling at the village fair next week. The {w} has to feel light and even in my grip.",
                "I'm entering the city fencing tournament. The {w} must dance exactly where I point it.",
                "I fence for the Queen's own guard. Any wobble at all and I'm finished - the {w} must be flawless.",
            },
            swordNames = new[] { "Whisper", "the Needle", "Featherfall" },
            triumph = "I won the whole tournament! The judges said they'd never seen footwork like it. I've named the {w} '{0}'.",
            fine = "I made it through my duels in one piece. Not a glorious day, but a good one.",
        },
        new CustomerArchetype
        {
            title = "hunter", durability = true, outfit = new Color(0.30f, 0.42f, 0.22f),
            weapons = new[] { WeaponType.Spear, WeaponType.Spear, WeaponType.Dagger, WeaponType.Sword }, races = new[] { Race.Human, Race.Elf, Race.Dwarf },
            tierLines = new[]
            {
                "I just need a sturdy {w} for the brambles on my walks.",
                "I hunt boar up in the hills. My {w} needs to survive a few hard knocks.",
                "There's a bear bothering our village. I need a {w} that won't snap against bone.",
                "I'm going after the troll under the old bridge. The {w} has to be all but unbreakable.",
            },
            swordNames = new[] { "Bear-Biter", "Old Stubborn", "the Oak" },
            triumph = "The beast bit down on my {w} and broke its own teeth! The whole village threw me a feast. I call it '{0}' now.",
            fine = "Came home with dinner and all my fingers. Can't ask for more than that.",
        },
        new CustomerArchetype
        {
            title = "cook", sharpness = true, outfit = new Color(0.85f, 0.83f, 0.78f),
            weapons = new[] { WeaponType.Dagger, WeaponType.Dagger, WeaponType.Sword }, races = new[] { Race.Human, Race.Dwarf },
            tierLines = new[]
            {
                "My kitchen knife went missing. A {w} with a bit of an edge will do for now.",
                "I'm a butcher. A good sharp {w} saves me hours every single day.",
                "I carve the roasts at the Baron's feasts. The {w} has to slice clean through in one pass.",
                "I'm to carve the King's birthday roast in front of the whole court. The {w} must cut like light itself.",
            },
            swordNames = new[] { "the Carver", "Silkcut", "Supper" },
            triumph = "One stroke and the roast fell apart like butter! The King himself asked who made my {w}. It's called '{0}' in the kitchens now.",
            fine = "It carves well enough. Nobody complained about dinner, which is praise where I work.",
        },
        new CustomerArchetype
        {
            title = "mercenary", durability = true, sharpness = true, outfit = new Color(0.40f, 0.28f, 0.22f),
            weapons = new[] { WeaponType.Sword, WeaponType.Sword, WeaponType.Spear, WeaponType.Hammer }, races = new[] { Race.Human, Race.Dwarf },
            tierLines = new[]
            {
                "I guard merchant carts. Mostly I just need to look armed - any honest {w} will do.",
                "I take whatever work pays. A {w} for bandit work - it has to hit hard and not break.",
                "We're riding against the raiders at the pass. I need a {w} with a real spine.",
                "The Iron Company is marching to war. I need a {w} that survives everything.",
            },
            swordNames = new[] { "Paycheck", "the Last Word", "Grudge" },
            triumph = "Twenty raiders and not a notch on my {w}! The captain doubled my pay. The lads call it '{0}'.",
            fine = "Got paid, got home. The {w} did its job and so did I.",
            wantsSteel = true,
        },
        new CustomerArchetype
        {
            title = "blade dancer", balance = true, sharpness = true, outfit = new Color(0.72f, 0.30f, 0.55f),
            weapons = new[] { WeaponType.Sword, WeaponType.Dagger, WeaponType.Sickle }, races = new[] { Race.Human, Race.Elf },
            tierLines = new[]
            {
                "I'm learning to dance with a {w}. Something light, with a little edge, for practice.",
                "I dance at the harvest festival. The {w} must spin easily and cut a ribbon mid-air.",
                "I'm performing for the Duchess. The {w} has to twirl like a feather and split falling petals.",
                "The Midsummer Masque - the most famous dance in the land. The {w} must be perfect in the hand, and sharp as a thought.",
            },
            swordNames = new[] { "Ribbon", "Petalsplit", "the Swallow" },
            triumph = "I cut every single petal in the air and the crowd went silent, then roared! I've named my {w} '{0}'.",
            fine = "The dance went well. Nobody lost an eye, which in my line of work is a triumph.",
        },
        new CustomerArchetype
        {
            title = "knight", durability = true, balance = true, outfit = new Color(0.55f, 0.55f, 0.62f),
            weapons = new[] { WeaponType.Sword, WeaponType.Sword, WeaponType.Spear, WeaponType.Hammer }, races = new[] { Race.Human, Race.Dwarf },
            tierLines = new[]
            {
                "I'm only a squire. I need a respectable {w} to carry.",
                "I've just been knighted. I need a proper {w} that won't embarrass me in the lists.",
                "I ride in the grand joust next month. The {w} must be steady in the hand and take a beating.",
                "I'm to be the King's champion. The {w} must be perfectly steady and all but indestructible.",
            },
            swordNames = new[] { "Oathkeeper", "Steadfast", "the Banner" },
            triumph = "I unhorsed the Black Baron himself! Minstrels are already singing about it. They call my {w} '{0}'.",
            fine = "I held my own in the lists. The {w} never let me down.",
            wantsSteel = true,
        },
        // ---- appended with weapon types and races ----
        new CustomerArchetype
        {
            title = "farmer", durability = true, sharpness = true, outfit = new Color(0.62f, 0.52f, 0.30f),
            weapons = new[] { WeaponType.Sickle }, races = new[] { Race.Human },
            tierLines = new[]
            {
                "Just a small {w} for the herb garden, nothing grand.",
                "Harvest's coming. I need a {w} that keeps its edge through a whole field of wheat.",
                "I bring in the barley for three villages. The {w} can't go dull or crack halfway through the season.",
                "The Harvest Queen herself will cut the first sheaf with it. It must be the finest {w} in the land.",
            },
            swordNames = new[] { "Goldreaper", "the Crescent", "Old Faithful" },
            triumph = "Whole field in a single morning! The neighbours came to watch. My {w} is '{0}' now, and I won't lend it to anyone.",
            fine = "The harvest's in and my back only aches a little. Good enough.",
        },
        new CustomerArchetype
        {
            title = "ranger", balance = true, sharpness = true, outfit = new Color(0.24f, 0.45f, 0.30f),
            weapons = new[] { WeaponType.Dagger, WeaponType.Dagger, WeaponType.Spear, WeaponType.Sword }, races = new[] { Race.Elf },
            tierLines = new[]
            {
                "I walk the forest paths. I need a small {w} for the trail - something light.",
                "I guard the border glades. My {w} must be quick in the hand.",
                "Shadows have crept into the deep wood. I need a {w} that answers the moment I move.",
                "I hunt the Night Stag, which no arrow has ever touched. The {w} must be flawless in the hand.",
            },
            swordNames = new[] { "Moonthorn", "Quicksilver", "the Hush" },
            triumph = "The shadows fled the deep wood before dawn. The elders have named my {w} '{0}' and sung of it.",
            fine = "The glades are quiet again. It served me well.",
        },
        new CustomerArchetype
        {
            title = "grove-keeper", sharpness = true, outfit = new Color(0.45f, 0.60f, 0.40f),
            weapons = new[] { WeaponType.Sickle, WeaponType.Sickle, WeaponType.Dagger }, races = new[] { Race.Elf },
            tierLines = new[]
            {
                "I tend the moonflowers. A {w} with a fine edge, please - they bruise so easily.",
                "The silverbark vines need pruning before the solstice. The {w} must cut cleanly.",
                "I harvest mistletoe from the Elder Oak with my {w}. One ragged cut and the tree remembers it for a century.",
                "I am to cut the Starbloom for the Queen of the Wood. The {w} must be keen beyond anything you have made.",
            },
            swordNames = new[] { "Dewcutter", "Starbloom", "the Gentle Edge" },
            triumph = "The Starbloom did not even notice the cut - it bloomed again by nightfall. My {w} is called '{0}' in the grove now.",
            fine = "The vines are pruned and the grove is content.",
        },
        new CustomerArchetype
        {
            title = "miner", durability = true, outfit = new Color(0.42f, 0.36f, 0.30f),
            weapons = new[] { WeaponType.Hammer, WeaponType.Hammer, WeaponType.Hammer, WeaponType.Spear }, races = new[] { Race.Dwarf },
            tierLines = new[]
            {
                "Need a {w} for knocking about in the rubble. Nothing fancy.",
                "I work the copper seams. A {w} that can take a beating all day long.",
                "We've hit granite in the deep tunnels. I need a {w} that won't crack before the rock does.",
                "I'm opening the Deepest Hall, sealed a thousand years. The {w} has to be unbreakable.",
            },
            swordNames = new[] { "Rockbiter", "Old Thunder", "the Knocker" },
            triumph = "Split the granite wall clean in two - found a vein of silver behind it! The whole clan calls my {w} '{0}' now.",
            fine = "Tunnel's dug, nobody got squashed. A fine week underground.",
        },
        new CustomerArchetype
        {
            title = "shieldwarden", durability = true, balance = true, outfit = new Color(0.36f, 0.40f, 0.50f),
            weapons = new[] { WeaponType.Hammer, WeaponType.Hammer, WeaponType.Spear, WeaponType.Sword }, races = new[] { Race.Dwarf },
            tierLines = new[]
            {
                "I stand watch at the mountain gate. I need a sturdy {w}.",
                "Goblins are testing the lower gates. My {w} must hold steady and hit hard.",
                "I'm holding the Ironbridge against raiders. The {w} has to take every blow and stay true.",
                "I'm to guard the King Under the Mountain himself. A {w} fit for that - steady and unbreakable.",
            },
            swordNames = new[] { "Gatekeeper", "the Anvil's Oath", "Stonewall" },
            triumph = "Held the Ironbridge alone for an hour! The King himself shook my hand. My {w} is '{0}' now.",
            fine = "The gate held. So did I, and so did the {w}.",
            wantsSteel = true,
        },
        // The Event Trader (never picked for ordinary orders: nobody "takes up" this job).
        new CustomerArchetype
        {
            title = "rare-metal trader", balance = true, outfit = new Color(0.20f, 0.42f, 0.44f),
            weapons = new[] { WeaponType.Sword }, races = new Race[0],
            tierLines = new[] { "Something exceptional.", "Something exceptional.", "Something exceptional.", "Something exceptional." },
            swordNames = new[] { "the Proof" },
            triumph = "It hangs in my finest collection. They call it '{0}'.",
            fine = "A fine piece. It travels with me.",
        },
        // ---- appended with persistent NPCs (NpcStories): trades of the city around the smithy ----
        new CustomerArchetype
        {
            title = "city guard", durability = true, outfit = new Color(0.58f, 0.14f, 0.14f),
            weapons = new[] { WeaponType.Sword, WeaponType.Sword, WeaponType.Spear, WeaponType.Hammer }, races = new[] { Race.Human, Race.Dwarf },
            tierLines = new[]
            {
                "I stand the night watch. The {w} at least has to look the part.",
                "Smugglers keep slipping past the gate. My {w} has to take a hard knock.",
                "We're clearing the thieves' den down by the docks. The {w} can't fail me in a brawl.",
                "I'm to guard the Duke's procession through the city. The {w} must be all but unbreakable.",
            },
            swordNames = new[] { "Gatewarden", "the Long Watch", "Lockjaw" },
            triumph = "We broke the smugglers' ring - I held the gate on my own for a good while. The watch calls my {w} '{0}' now.",
            fine = "Quiet nights, mostly. The {w} did what a {w} should.",
        },
        new CustomerArchetype
        {
            title = "scout", balance = true, sharpness = true, outfit = new Color(0.34f, 0.46f, 0.36f),
            weapons = new[] { WeaponType.Dagger, WeaponType.Dagger, WeaponType.Sword, WeaponType.Spear }, races = new[] { Race.Elf, Race.Human },
            tierLines = new[]
            {
                "I carry letters between the villages. A light {w} for the road, nothing more.",
                "The border roads aren't safe lately. My {w} must be quick to draw and quick to cut.",
                "I ride ahead of the Duke's column. The {w} has to answer before I've finished thinking.",
                "I'm to cross the Grey Marches alone. The {w} must be light as air and keen as frost.",
            },
            swordNames = new[] { "Longroad", "the Quick Word", "Waystar" },
            triumph = "Three bandits on the marsh road, and I was gone before they'd drawn. They call my {w} '{0}' at every inn from here to the coast.",
            fine = "Every letter delivered, and only the one ambush. The {w} kept me honest.",
        },
        new CustomerArchetype
        {
            title = "stoneworker", durability = true, outfit = new Color(0.50f, 0.46f, 0.40f),
            weapons = new[] { WeaponType.Hammer, WeaponType.Hammer, WeaponType.Hammer, WeaponType.Dagger }, races = new[] { Race.Dwarf, Race.Human },
            tierLines = new[]
            {
                "Just a {w} for dressing a few stones in my yard.",
                "I'm cutting blocks for the new bridge. The {w} has to take blows all day long.",
                "We're rebuilding the castle wall. The {w} can't crack on granite.",
                "I'm carving the King's tomb from black marble. The {w} must never falter.",
            },
            swordNames = new[] { "Old Granite", "the Plumb Line", "Keystone" },
            triumph = "Not a single block cracked on the whole bridge! The masons' guild named my {w} '{0}'.",
            fine = "The wall's up and it's straight. My {w} did its share.",
        },
        new CustomerArchetype
        {
            title = "merchant", balance = true, outfit = new Color(0.42f, 0.18f, 0.44f),
            weapons = new[] { WeaponType.Dagger, WeaponType.Sword, WeaponType.Sword }, races = new[] { Race.Human, Race.Elf, Race.Dwarf },
            tierLines = new[]
            {
                "I travel with my wares. A {w} on my belt keeps the cutpurses honest.",
                "I'm meeting buyers from the capital. The {w} should look fine and sit well at my side.",
                "The guild banquet is next week. I need a {w} worth showing off, balanced like a jeweller's scale.",
                "The Merchant Prince himself will see it. The {w} must be a masterpiece.",
            },
            swordNames = new[] { "Goldtongue", "the Ledger", "Fair Bargain" },
            triumph = "Every buyer at the banquet asked where I got my {w}. I've named it '{0}' - it's been good for business.",
            fine = "It hangs at my side and people take me seriously. Worth every coin.",
            wantsSteel = true,
        },
        new CustomerArchetype
        {
            title = "boatman", durability = true, sharpness = true, outfit = new Color(0.26f, 0.36f, 0.46f),
            weapons = new[] { WeaponType.Dagger, WeaponType.Dagger, WeaponType.Spear, WeaponType.Sword }, races = new[] { Race.Human, Race.Dwarf },
            tierLines = new[]
            {
                "I ferry folk across the river. A {w} for cutting rope and the odd tangle.",
                "River pirates took a barge last month. My {w} has to take spray and knocks and stay sharp.",
                "I'm running cargo down to the coast. The {w} must survive salt, wet and worse.",
                "I'm to pilot the Duke's barge through the Rapids of Ash. The {w} must never fail me.",
            },
            swordNames = new[] { "Riverbite", "the Oarsman's Friend", "Tidecutter" },
            triumph = "Cut us free of a snag in the rapids with one stroke - saved the whole cargo! The river folk call my {w} '{0}'.",
            fine = "Many crossings and no drownings. The {w} earned its keep.",
        },
    };

    // The Event Trader's entry (found by its title: new trades are appended after it).
    public static readonly int MerchantIndex = System.Array.FindIndex(All, a => a.title == "rare-metal trader");

    // A line of theirs with the weapon filled in.
    public string Line(string raw, WeaponType weapon) => raw.Replace("{w}", Weapons.Get(weapon).noun);
}

public enum OrderKind { NewSword, Repair, Challenge }

// Where an accepted order stands. Offered = still only being talked about (never saved as an active order).
public enum OrderStatus { Offered, WaitingForWeapon, WeaponReady, Completed, Failed, Cancelled }

// A customer's request: 1-2 stat thresholds wrapped in a job description, plus everything the Main Room needs
// to track the deal (who they are, what weapon, what it pays, how long they'll wait). Generated fresh for every
// visit - or, for a recurring customer, shaped by the weapon they had before (FollowUp) or a repair job
// (RepairJob). Serializable so the accepted order and every sold weapon's original order survive a save/load.
[System.Serializable]
public class CustomerOrder
{
    public const int PatienceDays = 3;       // how many days they'll wait before walking off
    public const int ApprenticeDays = 3;     // for the first few days the smith's notes spell the numbers out
    public const float AskPayCut = 0.85f;    // asking for exact details costs you the tip and a bit of pay

    private static readonly float[] TierValues = { 25f, 50f, 70f, 85f };
    private static readonly string[] TierWords = { "decent", "good", "great", "near-perfect" };

    public string customerName;
    public int archetypeIndex;
    public int tier;
    public string requestText;
    public string paperQuote;       // what goes on the order note when the numbers aren't shown (null = the tier line)
    public float minDurability;
    public float minBalance;
    public float minSharpness;
    public string requiredMaterial; // null = any metal will do
    public int reward;              // base pay before metal/reputation/tips (the whole fee for a repair)
    public int deadlineDay;
    public bool detailsAsked;
    public Color hair;
    public int customerId;          // BlacksmithHistory customer, 0 = a stranger who hasn't bought anything yet
    public OrderKind kind;
    public int repairSwordId;       // Repair: the weapon they left on the rack
    public int previousSwordId;     // a follow-up order: the weapon that prompted it
    public Race race;               // Human for orders saved before races existed
    public WeaponType weaponType;   // Sword for orders saved before weapon types existed
    // ---- as an accepted order (Workshop.orders): its own id, where it stands, and when its customer comes by.
    // Which weapons belong to it is NOT stored here: each weapon records the order it is reserved for
    // (SwordRecord.reservedForOrder) - one place, so order and weapon can never disagree (Workshop.AssignedTo).
    public int id;                  // Order #0001 - 0 while it's only an offer
    public OrderStatus status;
    public int acceptedDay;
    public float acceptedHour;      // when on acceptedDay it was agreed (GameTime clock hours)
    public int returnDay;           // the next day its customer comes by to collect (or to ask how it's going)...
    public float returnHour;        // ...and at about what time (0 in older saves = any time that day)
    public float deadlineHour;      // due by this time on deadlineDay (0 = the usual end of the working day)
    public bool rush;               // wanted by tomorrow evening - pays extra
    public RepairKind repairKind;   // Repair: what the job is, which sets how long it takes
    public int quantity = 1;        // weapons it needs - a contract (the watch, a knightly order) can ask for several
    // ---- shaped by the living world (WorldDirector) - all default to "as before" in older saves ----
    public float toleranceBonus;    // how much further short they'll still take it (easy-going +, perfectionist -)
    public int primaryStatPlusOne;  // the stat that matters most to them, +1 (0 = no single priority)
    public bool allRound;           // "a sound all-round weapon" - a little of everything, nothing extreme
    public bool special;            // a special commission (rare, memorable, pays and counts double)
    public int referredBy;          // the customer who sent them (0 = came on their own)
    public CityEventType duringEvent; // what was going on in the city when they asked
    public int PrimaryStat => primaryStatPlusOne - 1;

    public float DeadlineHour => deadlineHour > 0f ? deadlineHour : TimeSettings.Current.deadlineHour;

    public string Label => id > 0 ? $"Order #{id:0000}" : "New order";
    public bool IsActive => status == OrderStatus.WaitingForWeapon || status == OrderStatus.WeaponReady;

    public string titleOverride;    // a known person's own title ("Garrick Vance, gate guard") - null = name + trade
    public int patienceDays;        // how many days this person waits (their temperament); 0 = PatienceDays

    public int Patience => patienceDays > 0 ? patienceDays : PatienceDays;

    public CustomerArchetype archetype
    {
        get => CustomerArchetype.All[Mathf.Clamp(archetypeIndex, 0, CustomerArchetype.All.Length - 1)];
        set => archetypeIndex = System.Array.IndexOf(CustomerArchetype.All, value);
    }

    public WeaponInfo Weapon => Weapons.Get(weaponType);
    public RaceInfo RaceInfo => RaceInfo.Get(race);

    // "fencer", "elven ranger", "dwarven miner"
    public static string Describe(Race race, CustomerArchetype a)
    {
        string adj = RaceInfo.Get(race).adjective;
        return adj.Length > 0 ? $"{adj} {a.title}" : a.title;
    }

    public string Title => titleOverride ?? $"{customerName} the {Describe(race, archetype)}";
    public bool IsRepair => kind == OrderKind.Repair;
    public string TierLine => archetype.Line(archetype.tierLines[Mathf.Clamp(tier, 0, 3)], weaponType);

    public bool IsFulfilled(WorkpieceData item) =>
        (kind == OrderKind.NewSword || kind == OrderKind.Challenge) && !item.cracked && item.weaponType == weaponType &&
        (requiredMaterial == null || item.materialName == requiredMaterial) &&
        item.durability >= minDurability && item.balance >= minBalance && item.sharpness >= minSharpness;

    // JsonUtility turns null strings into "" - put them back so "any metal" stays "any metal".
    public void AfterLoad()
    {
        if (string.IsNullOrEmpty(requiredMaterial)) requiredMaterial = null;
        if (string.IsNullOrEmpty(paperQuote)) paperQuote = null;
        if (string.IsNullOrEmpty(titleOverride)) titleOverride = null;
    }

    public static CustomerOrder Generate(int day) => Generate(day, null);

    // takenNames: people the smith already knows - a new stranger never shares a name with them.
    public static CustomerOrder Generate(int day, ICollection<string> takenNames) => Generate(day, takenNames, RaceInfo.Pick());

    // Order pay: a base fee plus the difficulty (need x stats), a bit more for a demanded metal. Balanced against
    // ingot prices (ForgeMaterials) and the daily order limit - see BalanceTest for the numbers.
    public const float BaseFee = 13f, FeePerPoint = 0.32f;
    // Asking for two (or three) things at once: each is asked a little lower, so a demanding order is hard but fair.
    public const float MultiStatDiscount = 8f;
    // Reputation a smith needs before customers bring "great" (tier 2) and "almost perfect" (tier 3) jobs.
    public const float RepForTier2 = 40f, RepForTier3 = 65f;

    // unlocked: the metals the smith can work (null = all). Orders never ask for a locked metal, and never for a
    // quality the smith's best metal can't reach (ForgeMaterialInfo.maxQuality) - so every order is possible.
    public static CustomerOrder Generate(int day, ICollection<string> takenNames, Race race, ICollection<string> unlocked = null, float reputation = 60f)
    {
        var r = RaceInfo.Get(race);
        var order = new CustomerOrder { race = race };
        order.archetype = PickArchetype(race);
        order.weaponType = PickWeapon(order.archetype, r);
        order.customerName = PickName(r, takenNames);
        order.hair = r.hair[Random.Range(0, r.hair.Length)];

        // The first days lean easy so a new smith can actually deliver something.
        // ...and the most demanding customers only seek out a smith with a name (reputation), so an unknown smith
        // builds up on work they can manage instead of drowning in jobs beyond them.
        int byRep = reputation < RepForTier2 ? 2 : reputation < RepForTier3 ? 3 : TierValues.Length;
        int maxTier = Mathf.Min(Mathf.Min(day <= 2 ? 2 : TierValues.Length, byRep), MaxTierFor(unlocked) + 1);
        order.tier = Random.Range(0, maxTier);
        if (reputation < RepForTier2 && order.tier > 0 && Random.value < 0.4f) order.tier = 0; // a smith nobody knows gets mostly simple jobs
        float need = TierValues[order.tier];
        var extras = new List<string>();
        order.SetNeeds(need);
        var weapon = order.Weapon;

        // Race tendencies: the stat their folk care about creeps into orders even when the job doesn't need it,
        // and they say so in their own way.
        if (order.tier >= 1 && r.caresBalance && order.minBalance <= 0f && Random.value < 0.6f) order.minBalance = need - 10f;
        if (order.tier >= 1 && r.caresDurability && order.minDurability <= 0f && Random.value < 0.6f) order.minDurability = need - 10f;
        if (race == Race.Elf && weapon.hasEdge && order.weaponType == WeaponType.Dagger && order.minSharpness <= 0f && Random.value < 0.5f)
            order.minSharpness = need - 10f;
        order.DiscountMultiStat();
        if (r.caresBalance && order.minBalance > 0f) extras.Add(RaceInfo.PickLine(r.balanceLines));
        if (r.caresDurability && order.minDurability > 0f) extras.Add(RaceInfo.PickLine(r.durabilityLines));
        if (race == Race.Elf && order.minSharpness > 0f && Random.value < 0.5f) extras.Add(RaceInfo.PickLine(r.sharpLines));

        bool Has(string metal) => unlocked == null || unlocked.Contains(metal);
        if (order.archetype.wantsSteel && order.tier >= 2 && Has("Steel") && Random.value < 0.5f)
            order.requiredMaterial = "Steel";
        else if (order.tier >= 1 && Random.value < r.strongMetalChance)
        {
            // Dwarves trust hard metal - whichever hard metal the smith can actually work.
            string wanted = order.tier >= 2 && Has("Steel") ? "Steel" : Has("Iron") ? "Iron" : null;
            if (wanted != null && ForgeMaterials.Find(wanted).maxQuality >= order.HighestNeed + 5f) order.requiredMaterial = wanted;
        }

        int statCount = (order.minDurability > 0f ? 1 : 0) + (order.minBalance > 0f ? 1 : 0) + (order.minSharpness > 0f ? 1 : 0);
        order.reward = Mathf.RoundToInt(BaseFee + need * FeePerPoint * statCount * (order.requiredMaterial != null ? 1.2f : 1f));

        string words = order.TierLine;
        foreach (var e in extras) words += " " + e;
        if (order.requiredMaterial != null)
            words += " " + string.Format(RaceInfo.PickLine(r.metalLines), order.requiredMaterial.ToLower());
        order.paperQuote = words;
        string who = $"{ArticleFor(Describe(race, order.archetype))} {Describe(race, order.archetype)}";
        order.requestText = $"{string.Format(RaceInfo.PickLine(r.greetings), order.customerName, who)} {words}";
        return order;
    }

    // The highest tier whose need the best unlocked metal can still comfortably reach.
    private static int MaxTierFor(ICollection<string> unlocked)
    {
        float cap = 0f;
        foreach (var m in ForgeMaterials.All)
            if (unlocked == null || unlocked.Contains(m.name)) cap = Mathf.Max(cap, m.maxQuality);
        int best = 0;
        for (int t = 0; t < TierValues.Length; t++) if (TierValues[t] <= cap - 6f) best = t;
        return best;
    }

    public float HighestNeed => Mathf.Max(minDurability, Mathf.Max(minBalance, minSharpness));

    private void DiscountMultiStat()
    {
        int count = (minDurability > 0f ? 1 : 0) + (minBalance > 0f ? 1 : 0) + (minSharpness > 0f ? 1 : 0);
        if (count < 2) return;
        float cut = MultiStatDiscount * (count - 1);
        if (minDurability > 0f) minDurability = Mathf.Max(20f, minDurability - cut);
        if (minBalance > 0f) minBalance = Mathf.Max(20f, minBalance - cut);
        if (minSharpness > 0f) minSharpness = Mathf.Max(20f, minSharpness - cut);
    }

    // ---- the Event Trader's challenge ------------------------------------------------------------------
    // "Forge me something exceptional" - on the weapon's own strong stats, just under what the smith's best metal
    // can reach. The reward is a new metal (unlocksMaterial), not gold.
    public string unlocksMaterial;

    public bool IsChallenge => kind == OrderKind.Challenge;

    private static readonly string[] ChallengeLines =
    {
        "Show me a sword that could hang in a king's hall.",
        "I need a dagger worthy of a master - light as a breath and sharp as a secret.",
        "The Moon Priestess needs a sickle to cut the silver mistletoe. Nothing crude will do.",
        "A spear for the Warden of the Pass: true in the hand and hard to break.",
        "Forge me a hammer that can survive anything.",
    };

    public static CustomerOrder Challenge(WeaponType type, float bestCap, ForgeMaterialInfo reward)
    {
        var w = Weapons.Get(type);
        var order = new CustomerOrder
        {
            kind = OrderKind.Challenge,
            customerName = "Orla",
            archetypeIndex = CustomerArchetype.MerchantIndex,
            race = Race.Human,
            weaponType = type,
            hair = new Color(0.55f, 0.25f, 0.12f),
            tier = 3,
            unlocksMaterial = reward.name,
            detailsAsked = true, // a challenge is spelled out in full
        };
        // Its two strongest stats (the hammer: Durability alone, asked higher).
        var stats = new List<(string name, float weight)> { ("Durability", w.durWeight), ("Balance", w.balWeight) };
        if (w.hasEdge) stats.Add(("Sharpness", w.sharpWeight));
        stats.Sort((a, b) => b.weight.CompareTo(a.weight));
        if (type == WeaponType.Sword) // a sword: any two
            for (int i = stats.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (stats[i], stats[j]) = (stats[j], stats[i]); }
        int count = type == WeaponType.Hammer ? 1 : 2;
        // Just under what the smith's metals can reach - for THAT stat: metals differ per stat (copper can't hold a great
        // Durability, steel's Balance tops out early), and the metals known are the ones up to bestCap.
        float margin = count == 1 ? 2f : 8f;
        float CapOf(int stat)
        {
            float best = 0f;
            foreach (var m in ForgeMaterials.All) if (m.maxQuality <= bestCap + 0.01f) best = Mathf.Max(best, m.Cap(stat));
            return Mathf.Min(bestCap, best > 0f ? best : bestCap);
        }
        for (int i = 0; i < count; i++)
        {
            if (stats[i].name == "Durability") order.minDurability = CapOf(0) - margin;
            else if (stats[i].name == "Balance") order.minBalance = CapOf(1) - margin;
            else order.minSharpness = CapOf(2) - margin;
        }
        order.paperQuote = ChallengeLines[(int)type];
        order.requestText = $"I'm Orla - I trade in rare metals, and I only deal with smiths who've proven themselves. {order.paperQuote}\n" +
                            $"Hang one on your rack that beats my numbers, and I'll send you {reward.name.ToLower()} from then on.";
        return order;
    }

    // The stats the job leans on, at this tier - minus Sharpness for a weapon that has no edge.
    private void SetNeeds(float need)
    {
        var a = archetype;
        bool edge = Weapon.hasEdge;
        minDurability = a.durability ? need : 0f;
        minBalance = a.balance ? need : 0f;
        minSharpness = a.sharpness && edge ? need : 0f;
        if (minDurability <= 0f && minBalance <= 0f && minSharpness <= 0f) minDurability = need; // an edge-only job for an edgeless weapon
    }

    // Jobs their folk take up; their own race's special jobs a little more often.
    private static CustomerArchetype PickArchetype(Race race)
    {
        var pool = new List<CustomerArchetype>();
        foreach (var a in CustomerArchetype.All)
        {
            if (!a.TakenUpBy(race)) continue;
            pool.Add(a);
            if (a.races.Length == 1 && race != Race.Human) pool.Add(a); // elves and dwarves lean on their own trades
        }
        return pool.Count > 0 ? pool[Random.Range(0, pool.Count)] : CustomerArchetype.All[0];
    }

    // One of the job's weapons, nudged by what their race favours.
    private static WeaponType PickWeapon(CustomerArchetype a, RaceInfo r)
    {
        float total = 0f;
        foreach (var w in a.weapons) total += r.weaponBias[(int)w];
        float roll = Random.value * total;
        foreach (var w in a.weapons)
        {
            roll -= r.weaponBias[(int)w];
            if (roll <= 0f) return w;
        }
        return a.weapons[a.weapons.Length - 1];
    }

    private static string ArticleFor(string word) => "aeiou".IndexOf(char.ToLower(word[0])) >= 0 ? "an" : "a";

    private static string PickName(RaceInfo r, ICollection<string> taken)
    {
        var free = new List<string>();
        foreach (var n in r.names) if (taken == null || !taken.Contains(n)) free.Add(n);
        if (free.Count == 0) return r.names[Random.Range(0, r.names.Length)] + " the Younger"; // everyone's already a regular
        return free[Random.Range(0, free.Count)];
    }

    // A recurring customer bringing their weapon in to be worked back into shape on the grindstone.
    public static CustomerOrder RepairJob(CustomerRecord c, SwordRecord sword, int fee, string request)
    {
        bool edge = sword.sword.Weapon.hasEdge;
        var job = GameTime.RepairKindFor(sword);
        return new CustomerOrder
        {
            kind = OrderKind.Repair,
            repairKind = job,
            customerName = c.name,
            archetypeIndex = c.archetypeIndex,
            race = c.race,
            weaponType = sword.sword.weaponType,
            hair = c.hair,
            customerId = c.id,
            repairSwordId = sword.id,
            reward = fee,
            requestText = request,
            paperQuote = $"Repair {sword.Label} - it's on the rack ({GameTime.RepairName(job)}, ~{GameTime.Duration(GameTime.RepairMinutes(null, job))}).\n{(edge ? "Work the edge back on the grindstone." : "Dress its face on the grindstone.")}",
            detailsAsked = true, // nothing to guess on a repair
        };
    }

    // A recurring customer wants a new weapon, shaped by how the last one did: the same kind of weapon, one
    // step more demanding, and they insist on whatever let them down last time.
    public static CustomerOrder FollowUp(CustomerRecord c, SwordRecord previous, int day, ICollection<string> unlocked = null)
    {
        var order = new CustomerOrder
        {
            customerName = c.name,
            archetypeIndex = c.archetypeIndex,
            race = c.race,
            hair = c.hair,
            customerId = c.id,
            previousSwordId = previous != null ? previous.id : 0,
        };
        var a = order.archetype;
        order.weaponType = previous != null ? previous.sword.weaponType : PickWeapon(a, RaceInfo.Get(c.race));
        int prevTier = previous != null && previous.order != null ? previous.order.tier : 0;
        order.tier = Mathf.Clamp(prevTier + 1, 0, Mathf.Min(day <= 2 ? 1 : TierValues.Length - 1, MaxTierFor(unlocked)));
        float need = TierValues[order.tier];
        order.SetNeeds(need);
        string noun = order.Weapon.noun;

        string ask;
        if (previous != null)
        {
            var s = previous.sword;
            string best = SwordStories.BestStat(s), weak = SwordStories.WeakestStat(s);
            float weakValue = SwordStories.StatValue(s, weak);
            // Whatever let them down last time is now part of the job, however their trade usually leans.
            if (weakValue < need + 10f && weak != best)
            {
                float insist = Mathf.Max(need, Mathf.Min(85f, weakValue + 25f));
                if (weak == "Durability") order.minDurability = Mathf.Max(order.minDurability, insist);
                if (weak == "Balance") order.minBalance = Mathf.Max(order.minBalance, insist);
                if (weak == "Sharpness") order.minSharpness = Mathf.Max(order.minSharpness, insist);
                ask = $"Keep the {best.ToLower()}, but make it much {SwordStories.Comparative(weak)} this time.";
            }
            else
            {
                ask = $"Make it just like the last one - only better. {SwordStories.Comparative(best, true)} still, if you can.";
            }
            string opener = previous.status == SwordStatus.Destroyed
                ? $"{previous.Label} finally gave out on me. {SwordStories.Praise(best, s.Weapon)}"
                : $"{previous.Label} has done right by me. {SwordStories.Praise(best, s.Weapon)}";
            order.requestText = $"{opener} {order.TierLine} {ask}";
        }
        else
        {
            ask = order.TierLine;
            order.requestText = $"Back again, smith! Another {noun}. {ask}";
        }
        order.paperQuote = $"{order.TierLine} {ask}";

        int statCount = (order.minDurability > 0f ? 1 : 0) + (order.minBalance > 0f ? 1 : 0) + (order.minSharpness > 0f ? 1 : 0);
        order.reward = Mathf.RoundToInt(BaseFee + need * FeePerPoint * statCount * 1.1f); // a regular pays a little better
        return order;
    }

    // Numbers are only written down while the smith is still an apprentice, or if they asked for them.
    public bool ShowNumbers(int day) => detailsAsked || day <= ApprenticeDays;

    // The note carried to the forge: who, what weapon, the customer's own words - plus exact numbers only when
    // ShowNumbers.
    public string PaperText(int day)
    {
        var lines = new List<string> { Title.ToUpper() };
        if (IsRepair)
        {
            lines.Add(paperQuote);
        }
        else
        {
            lines.Add($"Wants: {Weapon.WithArticle}");
            if (ShowNumbers(day))
            {
                if (minDurability > 0f) lines.Add($"Durability >= {minDurability:0}");
                if (minBalance > 0f) lines.Add($"Balance >= {minBalance:0}");
                if (minSharpness > 0f) lines.Add($"Sharpness >= {minSharpness:0}");
            }
            else
            {
                lines.Add($"\"{paperQuote ?? TierLine}\"");
            }
        }
        if (requiredMaterial != null) lines.Add($"Metal: {requiredMaterial}");
        if (IsChallenge) lines.Add($"Reward: {unlocksMaterial} unlocked");
        int left = deadlineDay - day;
        lines.Add((rush ? "RUSH - " : "") + (left > 1 ? $"Due day {deadlineDay}, {GameTime.Clock(DeadlineHour)}" : left == 1 ? $"Due tomorrow, {GameTime.Clock(DeadlineHour)}" : left == 0 ? $"Due TODAY by {GameTime.Clock(DeadlineHour)}" : "Overdue!"));
        return string.Join("\n", lines);
    }

    // A short "what they were really asking for" line - shown once the job is done, so the player learns
    // to read the next customer.
    public string NeedSummary()
    {
        if (IsRepair) return $"their own {Weapon.noun} back in shape";
        var parts = new List<string>();
        bool ranked = PrimaryStat >= 0 && (minDurability > 0f ? 1 : 0) + (minBalance > 0f ? 1 : 0) + (minSharpness > 0f ? 1 : 0) > 1;
        string Mark(int stat) => ranked && PrimaryStat == stat ? " (above all)" : "";
        if (minDurability > 0f) parts.Add($"Durability {minDurability:0}+{Mark(0)}");
        if (minBalance > 0f) parts.Add($"Balance {minBalance:0}+{Mark(1)}");
        if (minSharpness > 0f) parts.Add($"Sharpness {minSharpness:0}+{Mark(2)}");
        string kind = special ? "special commission" : allRound ? "all-round" : TierWords[tier];
        return $"{ArticleFor(kind)} {kind} {Weapon.noun}: {string.Join(" + ", parts)}";
    }
}
