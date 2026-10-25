using System;
using UnityEngine;

// What's going on in the city on a given day. An event is a set of NUDGES (never rules): who tends to come to the
// smithy, what they tend to ask for, what the supplier has, how busy the streets are and what the view looks like.
// The enum is saved by number - only ever append.
public enum CityEventType { None, HarvestSeason, BanditsOnRoads, RoyalTournament, DwarvenCaravan, ElvenDelegation, Festival, HeavyRain, MarketDay }

[Flags]
public enum EventDecor { None = 0, Bunting = 1, Lanterns = 2, Stalls = 4, Carts = 8, Banners = 16, HaySheaves = 32, ElvenBanners = 64, Wagons = 128 }

public class CityEventInfo
{
    public CityEventType type;
    public string name;
    public string herald;      // said the day before when it's announced ahead ("Heralds cry a Royal Tournament for tomorrow!")
    public string morning;     // the day's greeting while it's on
    public string journal;     // the Chronicle line ("The Royal Tournament drew knights from three kingdoms.")
    public int minDay = 2;
    public float minRep;
    public float weight = 1f;
    public int cooldown = 6;   // days after it ends before it can come again
    public Vector2Int length = new Vector2Int(1, 2);
    public int announceAhead;  // 0 = a surprise on the morning itself
    public string[] seasons;   // null = any season
    public bool rare;          // special occasions: half weight, never two rare ones in a row

    // Who comes and what they want (multipliers on the director's weights; missing = 1).
    public float[] raceMul = { 1f, 1f, 1f };
    public int[] favouredTrades = new int[0];   // CustomerArchetype indices
    public float tradeMul = 2.5f;
    public float[] weaponMul = { 1f, 1f, 1f, 1f, 1f };
    public int statFocus = -1;                  // NpcStories.Stat*, -1 none
    public string focusLine;                    // what a customer says about it when the event adds that stat to their job
    public float tierUp;                        // chance an order is one step more demanding
    public float payMul = 1f;
    public int arrivalsDelta;                   // customers with new work today (+/-)
    public float specialMul = 1f;               // special commissions more (or less) likely

    // The city itself.
    public float crowd = 1f;                    // how many people are out
    public float guards, merchants, nobles, knights; // extra share of those roles in the crowd (0..1)
    public string weather;                      // forced weather, null = the day's own
    public float[] supplyMul = { 1f, 1f, 1f };  // the supplier's stock per metal
    public EventDecor decor;
    public Color decorTint = new Color(0.8f, 0.2f, 0.2f);
    public string[] ambient;                    // little lines under the view now and then

    // Owners of your weapons caught up in it (trades that take part, and what it did with the weapon).
    public int[] takesPart = new int[0];
    public string[] useLines;                   // {w} weapon noun, {n} owner
    public float wear;                          // extra wear on a weapon used in it
}

public static class CityEvents
{
    // CustomerArchetype indices (see CustomerOrder.cs; appended, never reordered).
    private const int Fencer = 0, Hunter = 1, Cook = 2, Mercenary = 3, BladeDancer = 4, Knight = 5, Farmer = 6, Ranger = 7,
                      GroveKeeper = 8, Miner = 9, Shieldwarden = 10, Guard = 12, Scout = 13, Stoneworker = 14, Merchant = 15, Boatman = 16;

    public static readonly CityEventInfo[] All =
    {
        new CityEventInfo { type = CityEventType.None, name = "An ordinary day" },
        new CityEventInfo
        {
            type = CityEventType.HarvestSeason, name = "Harvest Season", minDay = 2, weight = 1.2f, cooldown = 7, length = new Vector2Int(2, 3),
            seasons = new[] { "Summer", "Autumn" },
            morning = "Harvest has begun - carts of grain roll in from the farms.",
            journal = "The harvest came in; farmers queued for sickles.",
            favouredTrades = new[] { Farmer, GroveKeeper, Cook }, weaponMul = new[] { 0.8f, 0.8f, 4f, 0.8f, 0.9f }, statFocus = NpcStories.StatSharpness,
            focusLine = "It's harvest - the edge has to last a whole field.",
            crowd = 1.15f, merchants = 0.1f, decor = EventDecor.HaySheaves | EventDecor.Carts, decorTint = new Color(0.85f, 0.65f, 0.2f),
            ambient = new[] { "Farmhands haul sheaves past the smithy.", "The smell of cut hay drifts over from the fields." },
            takesPart = new[] { Farmer, GroveKeeper }, useLines = new[] { "{n} brought the harvest in with it - row after row.", "Cut a whole field with it before sundown." }, wear = 8f,
        },
        new CityEventInfo
        {
            type = CityEventType.BanditsOnRoads, name = "Bandits on the Roads", minDay = 3, weight = 1f, cooldown = 8, length = new Vector2Int(2, 3),
            morning = "Word is out: bandits on the roads. The watch is doubled.",
            journal = "Bandits troubled the roads; the watch wanted steel that wouldn't break.",
            raceMul = new[] { 1f, 0.8f, 1.2f },
            favouredTrades = new[] { Guard, Mercenary, Hunter, Scout, Shieldwarden }, weaponMul = new[] { 1.8f, 0.8f, 0.3f, 1.8f, 1f },
            statFocus = NpcStories.StatDurability, focusLine = "With bandits on the roads, it has to survive a real fight.",
            crowd = 0.8f, guards = 0.25f, supplyMul = new[] { 0.8f, 0.7f, 0.7f }, decor = EventDecor.None,
            ambient = new[] { "A patrol of the watch marches past, spears high.", "Travellers hurry in through the gate before dark." },
            takesPart = new[] { Guard, Mercenary, Hunter, Scout, Shieldwarden, Boatman },
            useLines = new[] { "{n} drove off a band of robbers on the south road with it.", "Held the bridge against bandits - it took some hard knocks." }, wear = 18f,
        },
        new CityEventInfo
        {
            type = CityEventType.RoyalTournament, name = "Royal Tournament", minDay = 6, minRep = 45f, weight = 0.8f, cooldown = 10, length = new Vector2Int(2, 3),
            announceAhead = 2, rare = true,
            herald = "Heralds cry through the streets: a ROYAL TOURNAMENT begins {when}!",
            morning = "The Royal Tournament is on - banners on every tower, knights in the streets.",
            journal = "The Royal Tournament drew knights and duellists from far and wide.",
            favouredTrades = new[] { Knight, Fencer, BladeDancer, Shieldwarden }, tradeMul = 3f, weaponMul = new[] { 2f, 1.2f, 0.4f, 1.3f, 1f },
            tierUp = 0.45f, payMul = 1.25f, specialMul = 2.5f,
            crowd = 1.3f, nobles = 0.15f, knights = 0.2f, guards = 0.1f, decor = EventDecor.Banners | EventDecor.Bunting, decorTint = new Color(0.2f, 0.3f, 0.75f),
            ambient = new[] { "Trumpets sound from the castle lists.", "A knight rides by in full plate, pennant fluttering." },
            takesPart = new[] { Knight, Fencer, BladeDancer, Shieldwarden, Mercenary },
            useLines = new[] { "{n} fought in the Royal Tournament with it.", "Carried into the lists at the Royal Tournament - the crowd roared." }, wear = 14f,
        },
        new CityEventInfo
        {
            type = CityEventType.DwarvenCaravan, name = "Dwarven Caravan", minDay = 4, weight = 1f, cooldown = 9, length = new Vector2Int(2, 2),
            announceAhead = 1,
            herald = "A dwarven caravan has been sighted in the pass - it reaches the city {when}.",
            morning = "The Dwarven Caravan is in town - wagons of ore and stubborn bargaining.",
            journal = "A dwarven caravan came down from the mountains, heavy with ore.",
            raceMul = new[] { 0.7f, 0.5f, 3.5f }, favouredTrades = new[] { Miner, Shieldwarden, Stoneworker }, weaponMul = new[] { 0.8f, 0.6f, 0.5f, 1.1f, 3f },
            focusLine = "Caravan work is hard on tools - it must be tough.", statFocus = NpcStories.StatDurability, supplyMul = new[] { 1f, 1.8f, 1.8f },
            crowd = 1.1f, merchants = 0.2f, decor = EventDecor.Wagons | EventDecor.Stalls | EventDecor.Banners, decorTint = new Color(0.55f, 0.28f, 0.12f),
            ambient = new[] { "Dwarven wagons rumble past, piled with ore.", "Somewhere a dwarf is haggling very loudly." },
            takesPart = new[] { Miner, Shieldwarden, Stoneworker }, useLines = new[] { "{n} worked the caravan's ore with it all day.", "Traded blows with a caravan rival over a bet - the {w} held." }, wear = 10f,
        },
        new CityEventInfo
        {
            type = CityEventType.ElvenDelegation, name = "Elven Delegation", minDay = 4, weight = 0.9f, cooldown = 10, length = new Vector2Int(2, 2),
            announceAhead = 1,
            herald = "An elven delegation is expected at the castle {when}.",
            morning = "An Elven Delegation visits the castle - green-and-gold banners along the avenue.",
            journal = "An elven delegation came to the castle; fine blades were in demand.",
            raceMul = new[] { 0.7f, 3.5f, 0.5f }, favouredTrades = new[] { Ranger, Scout, GroveKeeper, Fencer }, weaponMul = new[] { 1f, 3f, 1.4f, 1f, 0.3f },
            statFocus = NpcStories.StatBalance, tierUp = 0.2f, focusLine = "With the elves watching, it must sit perfectly in the hand.",
            crowd = 1.05f, nobles = 0.15f, decor = EventDecor.ElvenBanners | EventDecor.Lanterns, decorTint = new Color(0.2f, 0.55f, 0.3f),
            ambient = new[] { "Tall elven guests stroll by, admiring the city walls.", "Someone is playing a harp near the fountain." },
            takesPart = new[] { Ranger, Scout, Fencer }, useLines = new[] { "{n} drilled with the delegation's escort using it.", "Showed it off to the elven envoys - they approved." }, wear = 6f,
        },
        new CityEventInfo
        {
            type = CityEventType.Festival, name = "Festival", minDay = 3, weight = 0.9f, cooldown = 9, length = new Vector2Int(1, 2),
            announceAhead = 1,
            herald = "Tomorrow the city celebrates its Founding Festival!",
            morning = "Festival day! Lanterns and bunting everywhere, music in every street.",
            journal = "The city held its Founding Festival - music, lanterns and odd customers.",
            raceMul = new[] { 1f, 1.3f, 1.3f }, favouredTrades = new[] { BladeDancer, Merchant, Cook, Boatman }, weaponMul = new[] { 1f, 1.4f, 1.2f, 1f, 1f },
            arrivalsDelta = 1, specialMul = 1.5f,
            crowd = 1.45f, merchants = 0.15f, nobles = 0.1f, decor = EventDecor.Bunting | EventDecor.Lanterns | EventDecor.Stalls, decorTint = new Color(0.85f, 0.25f, 0.35f),
            ambient = new[] { "Music and laughter drift over from the square.", "Children run past with paper lanterns." },
            takesPart = new[] { BladeDancer, Fencer, Merchant }, useLines = new[] { "{n} danced with it at the Festival - everyone asked who made it.", "Won a festival contest with it." }, wear = 4f,
        },
        new CityEventInfo
        {
            type = CityEventType.HeavyRain, name = "Heavy Rain", minDay = 2, weight = 1f, cooldown = 5, length = new Vector2Int(1, 2),
            seasons = new[] { "Spring", "Summer", "Autumn" },
            morning = "Heavy rain drums on the roof. The streets are almost empty.",
            journal = "Days of heavy rain; the river rose and few came out.",
            favouredTrades = new[] { Boatman, Guard }, arrivalsDelta = -1, weather = "rain", crowd = 0.55f,
            supplyMul = new[] { 0.8f, 0.8f, 0.8f },
            ambient = new[] { "Rain streams off the eaves.", "Someone splashes past under a sack for a hood." },
            takesPart = new[] { Boatman }, useLines = new[] { "{n} cut a boat loose in the flood with it." }, wear = 6f,
        },
        new CityEventInfo
        {
            type = CityEventType.MarketDay, name = "Market Day", minDay = 2, weight = 1.3f, cooldown = 4, length = new Vector2Int(1, 1),
            morning = "Market day - stalls and carts crowd the lanes.",
            journal = "A busy market day; traders from the villages filled the lanes.",
            favouredTrades = new[] { Merchant, Cook, Farmer, Boatman }, weaponMul = new[] { 1f, 1.3f, 1.2f, 1f, 1f },
            supplyMul = new[] { 1.5f, 1.4f, 1.3f }, crowd = 1.3f, merchants = 0.3f, decor = EventDecor.Stalls | EventDecor.Carts | EventDecor.Bunting, decorTint = new Color(0.9f, 0.75f, 0.35f),
            ambient = new[] { "A trader shouts the price of onions.", "A cart piled with cabbages creaks past." },
        },
    };

    public static CityEventInfo Get(CityEventType t)
    {
        int i = (int)t;
        return i >= 0 && i < All.Length ? All[i] : All[0];
    }
}
