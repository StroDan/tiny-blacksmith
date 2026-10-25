using System;
using System.Collections.Generic;
using UnityEngine;

// The people of the city, as persons rather than order generators.
//
// Every customer who walks up to the table becomes a CustomerRecord (BlacksmithHistory) the first time they come -
// "NPC_0007" - and that record carries an NpcProfile: who they are (surname, age, trade and rank in it, wealth,
// two personality traits, the district they live in), a short background written once from authored fragments
// (how they came to their trade, something that happened to them, what they want, a trouble of theirs), what they
// like in a weapon (a weapon and a stat, which follow from that past), who they know among the smith's other
// customers, and what has happened to them since (life events). Nothing is ever regenerated: the story is composed
// once from a saved seed and saved as text, so a loaded game has exactly the same people.
//
// Their orders come from all of that (OrderFor): the trade decides the job, the past decides the stat they insist
// on, the purse decides how demanding and how well paid it is, the temperament decides how long they'll wait, and
// they speak in their own words - including what they remember of the smith (the weapon they bought, the repair,
// being turned away, a cousin who bought a dagger here, a promotion since).
//
// Everything is local and deterministic (authored text + weighted choices from a seeded System.Random) - no
// external services. Background townsfolk walking the streets (CityLife) stay light: a role and a race.
public enum NpcRelation { None, Cousin, Friend, Comrade }

public enum NpcLifeKind { Promoted, Injured, JoinedOrder, TookToTheRoad, BecameWealthier, LostEverything, WonTournament, ReturnedFromWar, OpenedShop, ChangedTrade, Married, BecameParent }

[Serializable]
public class NpcLifeEvent
{
    public int day;
    public NpcLifeKind kind;
    public string said;  // how they tell the smith about it ("I've been made sergeant of the watch!")
    public string note;  // how the journal puts it ("Made sergeant of the watch")
}

[Serializable]
public class NpcProfile
{
    public int seed;             // everything below was drawn from this (kept for reference / tests)
    public string surname;
    public int ageGroup;         // 0 young, 1 grown, 2 old
    public int rank;             // 0..2 within their trade (grows with Promoted)
    public int wealth;           // 0 poor, 1 modest, 2 comfortable, 3 wealthy
    public int trait1, trait2;   // NpcStories.Traits
    public int district;         // NpcStories.Districts
    public int background, pastEvent, goal, problem; // fragment indices (their trade's, and the shared problems)
    public int preferredStat;    // 0 Durability, 1 Balance, 2 Sharpness - what they insist on (their past taught them)
    public WeaponType preferredWeapon;
    public bool unusual;         // a trade uncommon among their folk
    public int years;            // in their trade
    public int relatedTo;        // another customer's id (0 = nobody the smith knows)
    public NpcRelation relation;
    public string story;         // the composed background, written once
    public List<NpcLifeEvent> lifeEvents = new List<NpcLifeEvent>();
    public int firstMetDay, lastSeenDay, timesMet, declinedDay, lastLifeEventDay;

    public bool IsSet => !string.IsNullOrEmpty(story);
    // Two people are "the same story" if they share trade, background, past, temperament and goal.
    public string Signature(int archetype) => $"{archetype}/{background}/{pastEvent}/{Mathf.Min(trait1, trait2)}-{Mathf.Max(trait1, trait2)}/{goal}";
}

public static class NpcStories
{
    public const int StatDurability = 0, StatBalance = 1, StatSharpness = 2;
    public static readonly string[] StatNames = { "Durability", "Balance", "Sharpness" };

    // ---- temperaments ---------------------------------------------------------------------------------------
    public class Trait
    {
        public string name;
        public string[] greet;   // how they open (first line of any visit)
        public string close;     // how they finish (as the second trait), may be null
        public int patience;     // days of waiting, +/- the usual
        public string opposite;  // never paired with this one
    }

    public static readonly Trait[] Traits =
    {
        new Trait { name = "serious", greet = new[] { "I've come on serious business, smith.", "Smith. A word, if you please." }, close = "I'd rather not have to come back about it.", opposite = "cheerful" },
        new Trait { name = "loyal", greet = new[] { "They say you're a smith who stands by your work.", "Good day to you, smith." }, close = "Do this right and I'll never go to another smith." },
        new Trait { name = "impatient", greet = new[] { "Smith! I haven't got all day, so I'll be quick.", "Right - let's not waste time." }, close = "And don't keep me waiting.", patience = -1, opposite = "calm" },
        new Trait { name = "calm", greet = new[] { "Good day. No hurry - I'll explain.", "Peace, smith. Let me tell you what I need." }, close = "Take the time it needs.", patience = 1, opposite = "impatient" },
        new Trait { name = "precise", greet = new[] { "I'll be exact about what I need.", "Let me be precise, smith." }, close = "I will check it closely, you understand." },
        new Trait { name = "demanding", greet = new[] { "I expect the best, and I'm told you can manage it.", "I've been let down by smiths before." }, close = "Anything less than your best and I'll know." },
        new Trait { name = "cheerful", greet = new[] { "Hello, hello! What a smithy - look at those sparks!", "Good morrow, smith! Lovely day for it." }, close = "Oh, I can't wait to see it!", opposite = "serious" },
        new Trait { name = "gruff", greet = new[] { "Hmph. You the smith?", "Smith." }, close = "That's all.", opposite = "talkative" },
        new Trait { name = "shy", greet = new[] { "Um... sorry to bother you. Are you the smith?", "Er - hello. I hope I'm not in the way." }, close = "Only if it's no trouble...", patience = 1, opposite = "proud" },
        new Trait { name = "proud", greet = new[] { "You may have heard of me.", "I don't visit just any smithy, you know." }, close = "It will be seen with me - make it worthy.", opposite = "shy" },
        new Trait { name = "talkative", greet = new[] { "Oh, you'd not believe the week I've had!", "Smith! Have I got a tale for you - but first, business." }, close = "Anyway, where was I? Ah - that's all!", opposite = "gruff" },
        new Trait { name = "frugal", greet = new[] { "Morning. I'll not waste your time or my coin.", "Smith. I've counted my coins twice." }, close = "And I'll not pay a copper more than it's worth." },
        new Trait { name = "superstitious", greet = new[] { "Is today a lucky day for forging? I hope so.", "I touched the lucky stone at the bridge on my way here." }, close = "Forge it on a good day, mind - a crack is bad luck." },
        new Trait { name = "kind", greet = new[] { "Good day to you - I hope business is kind to you.", "Hello! I brought you no trouble, I promise." }, close = "Thank you, truly." },
        new Trait { name = "blunt", greet = new[] { "I'll say it straight.", "No sense dancing around it." }, close = "That's what I want. Simple." },
        new Trait { name = "curious", greet = new[] { "So this is where the sparks come from! Anyway -", "Is that the anvil everyone talks about? Right, right -" }, close = "Would you let me watch you forge it some day?" },
    };

    public static readonly string[] Districts =
    {
        "the Old Town", "the Workshop District", "the river quarter", "the market streets", "the castle quarter", "the farms outside the walls", "the road",
    };
    private const int OldTown = 0, Workshops = 1, River = 2, Market = 3, Castle = 4, Farms = 5, Road = 6;

    // A trouble of theirs - shared by every trade (the ones that don't fit a trade are skipped by Fits).
    private static readonly (string text, int[] notFor)[] Problems =
    {
        ("has a bad knee from a fall years ago", null),
        ("owes money to a moneylender in the market streets", null),
        ("is afraid of deep water", new[] { 16 }),
        ("keeps losing things - gloves, keys, once a whole mule", null),
        ("has a rival who never misses a chance to mock them", null),
        ("looks after an ageing parent", null),
        ("sleeps badly before anything important", null),
        ("hasn't been home to their family in far too long", null),
        ("was cheated by a smith once and has never forgotten it", null),
        ("is saving every coin for something they won't talk about", null),
        ("has a temper they're trying hard to keep", null),
        ("still limps a little from an old wound", new[] { 4 }),
    };

    // ---- trades ---------------------------------------------------------------------------------------------
    // One per CustomerArchetype (same index). Fragment tokens: {n} first name, {y} years, {d} their district,
    // {w} the weapon noun. story = the journal's third person; said = what they tell the smith.
    private class Frag
    {
        public string story, said;
        public int stat = -1;
        public Frag(string story, string said, int stat = -1) { this.story = story; this.said = said; this.stat = stat; }
    }

    private class Trade
    {
        public string[] ranks;
        public int minWealth, maxWealth;
        public int[] districts;
        public bool military, restricted; // restricted: only the races the archetype lists (lore-bound trades)
        public Frag[] backgrounds, past, goals;
    }

    private static readonly Trade[] Trades =
    {
        // 0 fencer
        new Trade
        {
            ranks = new[] { "fencing student", "duellist", "fencing master" }, minWealth = 1, maxWealth = 3, districts = new[] { OldTown, Castle, Market },
            backgrounds = new[]
            {
                new Frag("{n} learned the blade in a cramped hall above a bakery in {d}.", "I learned to fence above a bakery - I still smell bread when I duel."),
                new Frag("{n} teaches rich merchants' children to fence, and has broken a practice blade over every one of them.", "I teach merchants' children to fence. They break everything I give them."),
                new Frag("{n} fought a duel over an insult at sixteen and has fenced ever since.", "Someone insulted my family when I was sixteen. I've been fencing ever since."),
            },
            past = new[]
            {
                new Frag("Last season {n} lost a bout because a heavy blade was a heartbeat too slow.", "I lost my last bout by a heartbeat - the blade was too heavy in the wrist.", StatBalance),
                new Frag("{n}'s old practice blade bent in a bout and never ran true again.", "My old blade bent in a bout and never ran straight again.", StatDurability),
                new Frag("{n} was cut by a rival with a keener edge, and swore never to be out-edged again.", "A rival's edge opened my sleeve and my arm with it. I won't be out-edged again.", StatSharpness),
            },
            goals = new[]
            {
                new Frag("wants to win the city tournament before the year is out", "I mean to win the city tournament this year."),
                new Frag("hopes to open a fencing school of their own", "One day I'll have a fencing school of my own."),
                new Frag("dreams of a place in the Queen's guard", "I want a place in the Queen's guard."),
            },
        },
        // 1 hunter
        new Trade
        {
            ranks = new[] { "trapper", "hunter", "master huntsman" }, minWealth = 0, maxWealth = 2, districts = new[] { Farms, Road, Workshops },
            backgrounds = new[]
            {
                new Frag("{n} has hunted the hills above the city for {y} years.", "I've hunted these hills since before the new wall went up."),
                new Frag("{n} sells furs and game to the tavern kitchens in town.", "Half the stews in this city are my venison, you know."),
                new Frag("{n} was taught to track by a grandmother who could follow a hare over bare rock.", "My grandmother could track a hare over bare rock. I'm still learning."),
            },
            past = new[]
            {
                new Frag("A boar snapped {n}'s last spear clean in two last winter.", "A boar snapped my last spear like kindling. Never again.", StatDurability),
                new Frag("{n} lost a stag because a dull blade took three strokes instead of one.", "I lost a stag last month - my blade was so dull it took three strokes.", StatSharpness),
                new Frag("{n} slipped on a scree slope because a clumsy weapon threw them off balance.", "My old one was so clumsy it threw me off a scree slope.", StatBalance),
            },
            goals = new[]
            {
                new Frag("wants to bring down the old boar of the north woods", "There's an old boar in the north woods. This season he's mine."),
                new Frag("is saving to buy a cottage at the edge of the forest", "I'm saving for a cottage by the forest edge."),
                new Frag("wants to become the Duke's huntsman", "I mean to be the Duke's huntsman before I'm grey."),
            },
        },
        // 2 cook
        new Trade
        {
            ranks = new[] { "kitchen hand", "butcher", "head cook" }, minWealth = 0, maxWealth = 2, districts = new[] { Market, OldTown, River },
            backgrounds = new[]
            {
                new Frag("{n} works the hottest kitchen in the city, at the Laughing Goose on the market streets.", "I cook at the Laughing Goose - hottest kitchen in the city."),
                new Frag("{n} started out gutting fish on the river docks at twelve.", "I've been gutting fish since I was twelve. I know a good edge."),
                new Frag("{n} runs a butcher's stall that has been in the family for three generations.", "My grandfather cut meat at the same stall I do."),
            },
            past = new[]
            {
                new Frag("{n}'s best knife went dull halfway through a wedding feast.", "My best knife went dull halfway through a wedding feast. The shame of it!", StatSharpness),
                new Frag("{n} snapped a cleaver on a beef bone in front of a customer.", "I snapped a cleaver on a bone in front of a customer. They still laugh.", StatDurability),
                new Frag("{n} nicked a finger with a blade that twisted in the hand.", "My last blade twisted in my hand and took a bit of finger with it.", StatBalance),
            },
            goals = new[]
            {
                new Frag("wants to cook for the Baron's table", "One day I'll cook for the Baron himself."),
                new Frag("dreams of opening an inn of their own", "I dream of my own inn, with my name over the door."),
                new Frag("is determined to win the harvest pie contest", "This year I will win the harvest pie contest."),
            },
        },
        // 3 mercenary
        new Trade
        {
            ranks = new[] { "caravan guard", "sellsword", "mercenary captain" }, minWealth = 0, maxWealth = 2, districts = new[] { Road, River, OldTown }, military = true,
            backgrounds = new[]
            {
                new Frag("{n} has sold their sword to merchants and lords for {y} years.", "I've fought for whoever pays for {y} years now."),
                new Frag("{n} deserted a company that burned villages, and has worked alone since.", "I left a company that burned villages. I work alone now."),
                new Frag("{n} guards the salt caravans between the coast and the city.", "I ride with the salt caravans. Long roads, short tempers."),
            },
            past = new[]
            {
                new Frag("{n}'s blade shattered in the middle of an ambush.", "My blade shattered in the middle of an ambush. I was lucky to walk away.", StatDurability),
                new Frag("{n} fought a whole skirmish with an edge like a spoon.", "I fought a whole skirmish with an edge like a spoon. Never again.", StatSharpness),
                new Frag("{n} was nearly killed when a badly weighted weapon was too slow to parry.", "A badly weighted blade was too slow to parry. I still have the scar.", StatBalance),
            },
            goals = new[]
            {
                new Frag("wants to earn enough to buy a farm and never fight again", "One more good year and I buy a farm. Then no more fighting."),
                new Frag("wants to lead a company of their own", "I'll have my own company before long."),
                new Frag("is looking for the man who betrayed their old captain", "I'm looking for the man who sold out my old captain."),
            },
        },
        // 4 blade dancer
        new Trade
        {
            ranks = new[] { "street dancer", "blade dancer", "celebrated blade dancer" }, minWealth = 0, maxWealth = 3, districts = new[] { Market, OldTown, Castle },
            backgrounds = new[]
            {
                new Frag("{n} dances for coins on the fountain steps in the plaza.", "I dance for coins by the fountain. The pigeons are my harshest critics."),
                new Frag("{n} travelled with a troupe of players until it broke up in the spring.", "My troupe broke up this spring. Now I dance on my own."),
                new Frag("{n} was a tumbler before taking up the blade dance.", "I was a tumbler before I ever held a blade."),
            },
            past = new[]
            {
                new Frag("{n} dropped a blade mid-spin in front of a festival crowd.", "I dropped my blade mid-spin at the festival. The crowd gasped - then laughed.", StatBalance),
                new Frag("{n}'s ribbon cut failed at the Midsummer fair because the edge was too dull.", "My blade couldn't cut the ribbon at Midsummer. Too dull. I wanted to vanish.", StatSharpness),
                new Frag("{n}'s performance blade bent against a stage post.", "I clipped a stage post and my blade bent like a reed.", StatDurability),
            },
            goals = new[]
            {
                new Frag("wants to dance before the Duchess", "I will dance before the Duchess. Just you wait."),
                new Frag("wants to start a troupe of their own", "I'm gathering dancers for a troupe of my own."),
                new Frag("hopes to be remembered in a song", "I want to end up in a song. A good one."),
            },
        },
        // 5 knight
        new Trade
        {
            ranks = new[] { "squire", "knight", "knight-captain" }, minWealth = 1, maxWealth = 3, districts = new[] { Castle, OldTown }, military = true,
            backgrounds = new[]
            {
                new Frag("{n} is the third child of a minor noble house and must earn their own name.", "I'm the third child of my house. Nobody gives me anything - I earn it."),
                new Frag("{n} was a stable hand until the day they pulled a drowning lord out of the river.", "I was mucking out stables until the day I pulled a lord out of a river."),
                new Frag("{n} serves in the castle garrison under old Sir Aldric.", "I serve under old Sir Aldric at the castle."),
            },
            past = new[]
            {
                new Frag("{n}'s sword broke on a shield in the spring joust.", "My sword broke on a shield at the spring joust. In front of everyone.", StatDurability),
                new Frag("{n} lost a duel of honour because their blade wavered.", "I lost a duel of honour because my blade wavered. It won't happen twice.", StatBalance),
                new Frag("{n}'s ceremonial blade was so blunt the squires joked about it.", "Even the squires joked my blade couldn't cut butter.", StatSharpness),
            },
            goals = new[]
            {
                new Frag("wants to become the King's champion", "I will be the King's champion one day."),
                new Frag("hopes to restore their family's fallen name", "I mean to restore my family's name."),
                new Frag("wants to lead the castle garrison", "One day the castle garrison will answer to me."),
            },
        },
        // 6 farmer
        new Trade
        {
            ranks = new[] { "farmhand", "farmer", "landholder" }, minWealth = 0, maxWealth = 2, districts = new[] { Farms },
            backgrounds = new[]
            {
                new Frag("{n} farms barley on the slopes outside the south-west gate.", "I farm barley out past the south-west gate."),
                new Frag("{n} inherited a stony field and a stubborn mule from an uncle.", "My uncle left me a stony field and a mule that hates me."),
                new Frag("{n} works the Miller's fields in exchange for a share of the flour.", "I work the Miller's fields for a share of the flour."),
            },
            past = new[]
            {
                new Frag("{n}'s old blade went dull before half the harvest was in.", "My old one went dull before half the harvest was in.", StatSharpness),
                new Frag("{n} cracked a blade on a hidden stone in the field.", "Hit a stone in the field and the blade cracked right through.", StatDurability),
                new Frag("{n} strained their back swinging a badly balanced tool all season.", "My back's still sore from a badly balanced tool all season.", StatBalance),
            },
            goals = new[]
            {
                new Frag("wants to buy the field next to theirs", "I want the field next to mine. Then we'll see."),
                new Frag("hopes to bring in the biggest harvest in the valley", "This year, the biggest harvest in the valley."),
                new Frag("wants their little brothers and sisters to go to the town school", "My little brothers and sisters will go to the town school, whatever it takes."),
            },
        },
        // 7 ranger
        new Trade
        {
            ranks = new[] { "trail warden", "ranger", "ranger of the high glades" }, minWealth = 0, maxWealth = 2, districts = new[] { Road, Farms },
            backgrounds = new[]
            {
                new Frag("{n} has walked the forest paths for {y} years.", "I have walked these paths for {y} years."),
                new Frag("{n} watches the old border glades where the forest meets the farms.", "I watch the glades where the forest meets your farms."),
                new Frag("{n} left the deep woods to see how the city folk live.", "I left the deep woods to see how your people live. It is... loud."),
            },
            past = new[]
            {
                new Frag("{n}'s knife was too heavy to draw quickly when a wolf came out of the dark.", "My old knife was too slow to draw when a wolf came out of the dark.", StatBalance),
                new Frag("{n}'s blade grew dull on the trail and failed them at a bad moment.", "My blade grew dull on the trail and failed me when it mattered.", StatSharpness),
                new Frag("{n} served among heavy infantry in a human war, and learned to trust sturdy steel.", "I stood in a shield wall in one of your wars. I learned to trust sturdy steel.", StatDurability),
            },
            goals = new[]
            {
                new Frag("hunts something in the deep wood that no one else believes in", "There is something in the deep wood. I will find it."),
                new Frag("wants to map every path between the city and the mountains", "I will map every path from here to the mountains."),
                new Frag("hopes to make peace between the woodcutters and the grove", "I want peace between your woodcutters and our grove."),
            },
        },
        // 8 grove-keeper (elves only)
        new Trade
        {
            ranks = new[] { "grove apprentice", "grove-keeper", "elder grove-keeper" }, minWealth = 0, maxWealth = 1, districts = new[] { Road, Farms }, restricted = true,
            backgrounds = new[]
            {
                new Frag("{n} tends a grove of moonflowers a day's walk from the city.", "I tend the moonflower grove, a day's walk from here."),
                new Frag("{n} was sent to learn the city's gardens and stayed for the bread.", "I came to learn your gardens. I stayed for your bread."),
                new Frag("{n} keeps the Elder Oak, and has done for {y} years.", "I keep the Elder Oak. It is older than your castle."),
            },
            past = new[]
            {
                new Frag("A ragged cut from a dull blade once made one of {n}'s trees sicken.", "A ragged cut once made one of my trees sicken. It took a year to heal.", StatSharpness),
                new Frag("{n}'s old pruning blade snapped on a frozen branch.", "My old blade snapped on a frozen branch last winter.", StatDurability),
                new Frag("{n} slipped from a ladder with a clumsy blade in hand.", "I fell from a ladder with a clumsy blade in my hand.", StatBalance),
            },
            goals = new[]
            {
                new Frag("wants to plant a grove inside the city walls", "I want to plant a grove inside your walls."),
                new Frag("is waiting for the Starbloom to flower again", "I am waiting for the Starbloom to flower again."),
                new Frag("hopes to teach city children the old songs of the trees", "I would teach your children the old tree songs."),
            },
        },
        // 9 miner
        new Trade
        {
            ranks = new[] { "tunnel digger", "miner", "foreman of the deep seams" }, minWealth = 0, maxWealth = 2, districts = new[] { Workshops, Road },
            backgrounds = new[]
            {
                new Frag("{n} has dug the copper seams in the northern hills for {y} years.", "I've dug the northern copper seams for {y} years."),
                new Frag("{n} comes from a long line of miners and has never once seen the sea.", "My whole family's dug rock. None of us has ever seen the sea."),
                new Frag("{n} works the new silver tunnel the Guild opened last spring.", "I'm in the new silver tunnel the Guild opened last spring."),
            },
            past = new[]
            {
                new Frag("{n}'s last hammer lasted twelve years before the head finally split.", "My last hammer lasted twelve years. Let's see if your work can do the same.", StatDurability),
                new Frag("A clumsy hammer once bounced off the rock and broke {n}'s nose.", "A clumsy hammer bounced off the rock and broke my nose. See?", StatBalance),
                new Frag("{n} was trapped for a day behind a rockfall and trusts only the toughest tools since.", "I spent a day trapped behind a rockfall. I trust only tough tools now.", StatDurability),
            },
            goals = new[]
            {
                new Frag("wants to find the lost vein their grandfather talked about", "Grandfather swore there's a lost vein up there. I'll find it."),
                new Frag("wants to become foreman before the season ends", "I'll be foreman before the season's out."),
                new Frag("is saving to bring the family down from the mountain", "I'm saving to bring my family down from the mountain."),
            },
        },
        // 10 shieldwarden
        new Trade
        {
            ranks = new[] { "gate sentry", "shieldwarden", "captain of the gate" }, minWealth = 1, maxWealth = 2, districts = new[] { OldTown, Castle }, military = true,
            backgrounds = new[]
            {
                new Frag("{n} stood watch at the mountain gate for {y} years before coming to the city.", "I stood at the mountain gate for {y} years before I came here."),
                new Frag("{n} guards the dwarven merchants' hall in {d}.", "I guard the dwarven merchants' hall. Dull work, mostly."),
                new Frag("{n} came down the mountain to train the city watch in shieldwork.", "I came down to teach your watch how to hold a shield."),
            },
            past = new[]
            {
                new Frag("{n}'s hammer cracked against a troll's hide.", "My hammer cracked on a troll's hide. The troll laughed. I didn't.", StatDurability),
                new Frag("{n} was knocked flat because a weapon pulled them off their stance.", "My old weapon pulled me off my stance and I went down like a sack.", StatBalance),
                new Frag("{n} held a bridge for an hour and swore every weapon they carry will be the toughest they can find.", "I held a bridge for an hour once. Since then - only the toughest.", StatDurability),
            },
            goals = new[]
            {
                new Frag("wants to see the Ironbridge rebuilt", "I want to see the Ironbridge rebuilt before I die."),
                new Frag("hopes to be named captain of the gate", "They'll make me captain of the gate. Soon."),
                new Frag("wants to prove that dwarves and city folk can stand one line together", "Dwarf and city folk in one shield line - that's what I want to see."),
            },
        },
        // 11 rare-metal trader (the Event Trader - never generated)
        new Trade
        {
            ranks = new[] { "rare-metal trader", "rare-metal trader", "rare-metal trader" }, minWealth = 3, maxWealth = 3, districts = new[] { Road }, restricted = true,
            backgrounds = new[] { new Frag("{n} trades rare metals between the mountain holds and the capital.", "I trade in rare metals.") },
            past = new[] { new Frag("{n} only deals with smiths who have proven themselves.", "I only deal with smiths who prove themselves.", StatBalance) },
            goals = new[] { new Frag("is looking for a smith worthy of her metals", "I am looking for a smith worthy of my metals.") },
        },
        // 12 city guard
        new Trade
        {
            ranks = new[] { "recruit of the watch", "gate guard", "sergeant of the watch" }, minWealth = 0, maxWealth = 2, districts = new[] { OldTown, River, Castle, Market }, military = true,
            backgrounds = new[]
            {
                new Frag("{n} has served at the eastern gate for {y} years.", "I've stood at the eastern gate for {y} years."),
                new Frag("{n} joined the watch after thieves robbed their parents' shop in {d}.", "Thieves robbed my parents' shop. That's why I joined the watch."),
                new Frag("{n} walks the night patrol along the river docks.", "I walk the night patrol down by the docks."),
            },
            past = new[]
            {
                new Frag("{n}'s issued sword broke during a fight with smugglers, and they've distrusted light weapons since.", "The blade they issued me snapped in a fight with smugglers. I don't care if the new one looks pretty. Make it strong.", StatDurability),
                new Frag("{n} lost a thief in the alleys because a clumsy weapon slowed them down.", "A thief got away from me because my weapon was clumsy in the alleys.", StatBalance),
                new Frag("{n} was mocked by the whole barracks for a blade that couldn't cut rope.", "The whole barracks laughed - my blade couldn't even cut a rope.", StatSharpness),
            },
            goals = new[]
            {
                new Frag("wants to make sergeant before the winter", "I'll make sergeant before winter."),
                new Frag("is hunting the smuggler known as the Grey Eel", "I'm after the smuggler they call the Grey Eel."),
                new Frag("hopes to be taken into a knightly order one day", "One day a knightly order will take me in. You'll see."),
            },
        },
        // 13 scout
        new Trade
        {
            ranks = new[] { "courier", "scout", "pathfinder" }, minWealth = 0, maxWealth = 2, districts = new[] { Road, Farms, River },
            backgrounds = new[]
            {
                new Frag("{n} carries messages between the settlements around the city.", "I spend more time on the road than at home."),
                new Frag("{n} rides ahead of the merchant convoys looking for trouble.", "I ride ahead of the convoys, looking for trouble before it finds them."),
                new Frag("{n} knows every ford on the river between here and the coast.", "I know every ford from here to the coast."),
            },
            past = new[]
            {
                new Frag("{n} spends most days travelling and prefers a weapon light in the hand.", "I need something light in my hand and quick to draw.", StatBalance),
                new Frag("{n}'s blade went dull on a long ride and was useless in an ambush.", "My blade went dull on a long ride - useless when the ambush came.", StatSharpness),
                new Frag("{n} had a blade snap in a river crossing and nearly drowned after it.", "My last blade snapped in a river crossing, and I nearly went with it.", StatDurability),
            },
            goals = new[]
            {
                new Frag("wants to carry the Duke's own letters", "I want to carry the Duke's own letters."),
                new Frag("is trying to find a sibling who went missing on the northern road", "My sibling went missing on the northern road. I keep looking."),
                new Frag("wants to cross the Grey Marches, which no courier has done in a decade", "Nobody's crossed the Grey Marches in ten years. I will."),
            },
        },
        // 14 stoneworker
        new Trade
        {
            ranks = new[] { "quarry hand", "stoneworker", "master mason" }, minWealth = 0, maxWealth = 2, districts = new[] { Workshops, Castle },
            backgrounds = new[]
            {
                new Frag("{n} dresses stone for the new bridge by the watermill.", "I dress stone for the new bridge by the mill."),
                new Frag("{n} has worked the castle quarry for {y} years.", "I've worked the castle quarry for {y} years."),
                new Frag("{n} carves the gargoyles on the church roof and swears one of them winks.", "I carve the church gargoyles. One of them winks, I swear it."),
            },
            past = new[]
            {
                new Frag("{n}'s hammer head flew off mid-swing last spring.", "My hammer head flew off mid-swing and nearly took a pigeon with it.", StatDurability),
                new Frag("A badly balanced hammer ruined a whole day of {n}'s carving.", "A badly balanced hammer ruined a whole day's carving.", StatBalance),
                new Frag("{n} trusts only tools that outlast the stone.", "I want tools that outlast the stone.", StatDurability),
            },
            goals = new[]
            {
                new Frag("wants their mark on a stone in the castle wall", "I want my mark on a stone in the castle wall."),
                new Frag("hopes to be made a master of the masons' guild", "The masons' guild will make me master. Soon."),
                new Frag("dreams of building a bridge that lasts a thousand years", "I'll build a bridge that stands a thousand years."),
            },
        },
        // 15 merchant
        new Trade
        {
            ranks = new[] { "pedlar", "merchant", "guild merchant" }, minWealth = 0, maxWealth = 3, districts = new[] { Market, River },
            backgrounds = new[]
            {
                new Frag("{n} sells spices and dyes from a stall in {d}.", "I sell spices and dyes - you can smell me coming."),
                new Frag("{n} started out with a single pack of needles and dreams of a warehouse on the river.", "I started with one pack of needles. One day, a warehouse."),
                new Frag("{n} buys wool from the farms and sells cloth in the capital.", "I buy wool from the farms and sell cloth in the capital."),
            },
            past = new[]
            {
                new Frag("{n} was robbed on the road and couldn't draw their weapon in time.", "I was robbed on the road - couldn't draw my weapon in time.", StatBalance),
                new Frag("{n} was sold a pretty blade that snapped the first time it was used.", "I bought a pretty blade once. It snapped the first time I used it.", StatDurability),
                new Frag("{n} lost face at a banquet when their dress blade couldn't even cut a ribbon.", "My dress blade couldn't cut a ribbon at the banquet. The shame.", StatSharpness),
            },
            goals = new[]
            {
                new Frag("wants a seat on the merchants' guild council", "I want a seat on the guild council."),
                new Frag("wants to open a shop on the plaza", "I'll have a shop on the plaza by next year."),
                new Frag("hopes to trade as far as the southern cities", "I want to trade as far as the southern cities."),
            },
        },
        // 16 boatman
        new Trade
        {
            ranks = new[] { "ferry hand", "boatman", "river captain" }, minWealth = 0, maxWealth = 2, districts = new[] { River },
            backgrounds = new[]
            {
                new Frag("{n} has run the ferry below the watermill for {y} years.", "I've run the ferry below the mill for {y} years."),
                new Frag("{n} grew up on a barge and has never slept more than a night on land.", "I grew up on a barge. Land makes me dizzy."),
                new Frag("{n} carries grain from the farms to the city on a flat-bottomed boat.", "I bring the grain down to the city on my boat."),
            },
            past = new[]
            {
                new Frag("{n}'s knife rusted through and snapped while cutting a snagged rope.", "My knife rusted through and snapped on a snagged rope.", StatDurability),
                new Frag("{n} couldn't cut a tangled line in time and lost half a cargo.", "Couldn't cut a tangled line in time - lost half a cargo.", StatSharpness),
                new Frag("{n} dropped a clumsy blade overboard in a squall.", "Dropped my old blade overboard in a squall. Clumsy thing.", StatBalance),
            },
            goals = new[]
            {
                new Frag("wants to own a proper river barge", "I want a proper barge of my own."),
                new Frag("hopes to run cargo all the way to the sea", "One day I'll take cargo all the way to the sea."),
                new Frag("wants to clear the river of the pirates who took a friend's boat", "Pirates took a friend's boat. I want them off my river."),
            },
        },
    };

    private static Trade TradeFor(int archetype) => archetype >= 0 && archetype < Trades.Length ? Trades[archetype] : Trades[0];
    public static bool IsGenerated(int archetype) => archetype != CustomerArchetype.MerchantIndex && archetype < Trades.Length;

    public static string RankTitle(CustomerRecord c)
    {
        var t = TradeFor(c.archetypeIndex);
        return t.ranks[Mathf.Clamp(c.profile.rank, 0, t.ranks.Length - 1)];
    }

    // "gate guard", "elven scout", "dwarven stoneworker"
    public static string Describe(CustomerRecord c)
    {
        string adj = RaceInfo.Get(c.race).adjective;
        string rank = RankTitle(c);
        return adj.Length > 0 ? $"{adj} {rank}" : rank;
    }

    public static string FullName(CustomerRecord c) => string.IsNullOrEmpty(c.profile.surname) ? c.name : $"{c.name} {c.profile.surname}";

    // ---- surnames -------------------------------------------------------------------------------------------
    private static readonly string[] HumanSurnames =
    {
        "Vance", "Ashford", "Miller", "Thorne", "Hale", "Brightwater", "Cobb", "Farrow", "Graves", "Holt", "Marsh", "Penrose",
        "Quill", "Redding", "Sallow", "Tanner", "Wick", "Yarrow", "Blackwood", "Dunmore", "Fenwick", "Kettle", "Lark", "Oakes",
    };
    private static readonly string[] ElfHouses =
    {
        "Moonwhisper", "Silverbough", "Dawnleaf", "Starfall", "Mistvale", "Ashenglade", "Brightwing", "Nightbloom", "Thornsong", "Willowmere",
        "Frostpetal", "Sunreed", "Hollowmoon", "Greenhallow",
    };
    private static readonly string[] DwarfClans =
    {
        "Stonehand", "Ironbeard", "Deepdelver", "Coalbrow", "Anvilback", "Granitejaw", "Copperkettle", "Flintfist", "Hammerfall", "Oreheart",
        "Blackforge", "Rockbottom", "Silvervein", "Stoutshield",
    };
    private static readonly string[] ExtraHumanNames = { "Garrick", "Wren", "Elsa", "Matthias", "Odile", "Corin", "Nadia", "Bram", "Kasia", "Leopold", "Sabine", "Tomas" };
    private static readonly string[] ExtraElfNames = { "Lethiel", "Aerin", "Saelis", "Vaelor", "Irime", "Nerion", "Talathel", "Ysolde" };
    private static readonly string[] ExtraDwarfNames = { "Brokk", "Ingrid", "Harald", "Sigrun", "Tordek", "Ragna", "Balin", "Helka" };

    private static string[] FirstNames(Race race)
    {
        var r = RaceInfo.Get(race);
        var extra = race == Race.Elf ? ExtraElfNames : race == Race.Dwarf ? ExtraDwarfNames : ExtraHumanNames;
        var all = new string[r.names.Length + extra.Length];
        r.names.CopyTo(all, 0);
        extra.CopyTo(all, r.names.Length);
        return all;
    }

    private static string[] Surnames(Race race) => race == Race.Elf ? ElfHouses : race == Race.Dwarf ? DwarfClans : HumanSurnames;

    // ---- making a person ------------------------------------------------------------------------------------

    // Someone new walks up to the table: a new NPC, told apart from everyone the smith already knows, sometimes a
    // cousin, friend or comrade of one of them. Recorded straight away (their first visit is part of their history).
    // opts (WorldDirector): who the living world sends today - their folk, the trades that are about, how established.
    public static CustomerRecord Meet(BlacksmithHistory h, int day, MeetOptions opts = null)
    {
        int seed = UnityEngine.Random.Range(1, int.MaxValue / 2);
        CustomerRecord c = null;
        for (int attempt = 0; attempt < 40; attempt++)
        {
            c = Build(h, seed + attempt * 7919, null, null, null, opts);
            if (!IsRepeat(h, c)) break;
        }
        c.id = h.nextCustomerId++;
        c.profile.firstMetDay = day;
        h.customers.Add(c);
        h.LogPerson(day, HistoryEventType.FirstMet, c, $"First came to the smithy: {Describe(c)} {Home(c.profile.district)}");
        return c;
    }

    // An existing record without a profile (a save from before persons, a test, the Event Trader): give it one that
    // keeps its name, race and trade. Seeded by its id and name, so it's the same whenever it's done.
    public static void EnsureProfile(CustomerRecord c, BlacksmithHistory h)
    {
        if (c.profile == null) c.profile = new NpcProfile();
        if (c.profile.IsSet) return;
        int seed = c.id * 7919 + StableHash(c.name);
        var built = Build(h, seed, c.race, c.archetypeIndex, c.name);
        var keep = c.profile;
        c.profile = built.profile;
        c.profile.firstMetDay = keep.firstMetDay > 0 ? keep.firstMetDay : Mathf.Max(1, c.lastOrderDay);
        c.profile.timesMet = Mathf.Max(keep.timesMet, c.lastOrderDay > 0 ? 1 : 0);
        c.profile.lastSeenDay = keep.lastSeenDay > 0 ? keep.lastSeenDay : c.lastOrderDay;
        c.profile.relatedTo = 0; // no invented relations for people who already exist
        c.profile.relation = NpcRelation.None;
        c.profile.story = ComposeStory(c, h, new System.Random(seed));
    }

    private static int StableHash(string s)
    {
        unchecked
        {
            int hsh = 23;
            foreach (char ch in s ?? "") hsh = hsh * 31 + ch;
            return hsh & 0x7fffffff;
        }
    }

    private static bool IsRepeat(BlacksmithHistory h, CustomerRecord c)
    {
        string sig = c.profile.Signature(c.archetypeIndex), full = FullName(c);
        foreach (var o in h.customers)
        {
            if (o.profile == null || !o.profile.IsSet) continue;
            if (o.profile.Signature(o.archetypeIndex) == sig || FullName(o) == full) return true;
            // Same trade with the same past AND the same background reads as the same person, whatever the temperament.
            if (o.archetypeIndex == c.archetypeIndex && o.profile.background == c.profile.background && o.profile.pastEvent == c.profile.pastEvent) return true;
        }
        return false;
    }

    private static CustomerRecord Build(BlacksmithHistory h, int seed, Race? forceRace, int? forceArchetype, string forceName, MeetOptions opts = null)
    {
        var rng = new System.Random(seed);
        var c = new CustomerRecord { profile = new NpcProfile { seed = seed } };
        var p = c.profile;
        if (opts != null)
        {
            if (!forceRace.HasValue) forceRace = opts.race;
            if (!forceArchetype.HasValue) forceArchetype = opts.archetype;
        }

        // Someone the smith already knows, now and then: a cousin shares their family name and folk.
        CustomerRecord relative = null;
        if (opts != null && opts.relatedTo > 0 && h != null && h.Customer(opts.relatedTo) is CustomerRecord sent && sent.profile != null && sent.profile.IsSet)
        {
            // Word of mouth: sent by someone the smith knows.
            relative = sent;
            p.relatedTo = sent.id;
            p.relation = opts.relation != NpcRelation.None ? opts.relation : NpcRelation.Friend;
        }
        else if (forceName == null && h != null && rng.NextDouble() < 0.25)
        {
            var known = h.customers.FindAll(o => o.profile != null && o.profile.IsSet && IsGenerated(o.archetypeIndex) && o.profile.relatedTo == 0);
            if (known.Count > 0)
            {
                // People who bought something are talked about more.
                var buyers = known.FindAll(o => o.purchases > 0);
                var list = buyers.Count > 0 && rng.NextDouble() < 0.75 ? buyers : known;
                relative = list[rng.Next(list.Count)];
                p.relatedTo = relative.id;
                p.relation = (NpcRelation)(1 + rng.Next(3));
            }
        }

        // Folk and trade: race tendencies, never rules - now and then someone took up a trade unusual among their folk.
        if (forceRace.HasValue) c.race = forceRace.Value;
        else if (relative != null && p.relation == NpcRelation.Cousin) c.race = relative.race;
        else c.race = PickRace(rng);
        if (forceArchetype.HasValue) c.archetypeIndex = forceArchetype.Value;
        else if (relative != null && p.relation == NpcRelation.Comrade) c.archetypeIndex = relative.archetypeIndex;
        else c.archetypeIndex = PickTrade(rng, c.race, out p.unusual, opts?.tradeWeight);
        if (relative != null && p.relation == NpcRelation.Comrade && !CustomerArchetype.All[c.archetypeIndex].TakenUpBy(c.race))
        {
            if (TradeFor(c.archetypeIndex).restricted) { p.relation = NpcRelation.Friend; c.archetypeIndex = PickTrade(rng, c.race, out p.unusual, opts?.tradeWeight); }
            else p.unusual = true;
        }
        var trade = TradeFor(c.archetypeIndex);
        var arch = CustomerArchetype.All[c.archetypeIndex];

        // Standing in their trade, and the age and purse that go with it. A smith with a name draws the established.
        double rr = Mathf.Clamp01((float)rng.NextDouble() + (opts != null ? opts.rankShift : 0f));
        p.rank = rr < 0.45 ? 0 : rr < 0.85 ? 1 : 2;
        if (opts != null && opts.rank >= 0) p.rank = opts.rank;
        p.ageGroup = p.rank == 0 ? (rng.NextDouble() < 0.75 ? 0 : 1) : p.rank == 1 ? (rng.NextDouble() < 0.7 ? 1 : rng.NextDouble() < 0.5 ? 0 : 2) : (rng.NextDouble() < 0.6 ? 1 : 2);
        if (trade.military && p.rank == 0 && p.ageGroup == 2) p.ageGroup = 1; // no grey-haired recruits
        p.wealth = Mathf.Clamp(trade.minWealth + rng.Next(trade.maxWealth - trade.minWealth + 1) + (p.rank == 2 ? 1 : 0) - (p.rank == 0 && rng.NextDouble() < 0.4 ? 1 : 0),
                               trade.minWealth, trade.maxWealth);
        if (opts != null && opts.minWealth > p.wealth) p.wealth = Mathf.Min(3, opts.minWealth);
        int baseYears = p.rank == 0 ? 1 + rng.Next(3) : p.rank == 1 ? 4 + rng.Next(8) : 11 + rng.Next(15);
        p.years = c.race == Race.Elf ? baseYears * 3 : baseYears;

        // Temperament: two traits that aren't each other's opposite.
        p.trait1 = rng.Next(Traits.Length);
        do { p.trait2 = rng.Next(Traits.Length); } while (p.trait2 == p.trait1 || Traits[p.trait2].name == Traits[p.trait1].opposite || Traits[p.trait1].name == Traits[p.trait2].opposite);

        p.district = trade.districts[rng.Next(trade.districts.Length)];
        p.background = rng.Next(trade.backgrounds.Length);
        p.pastEvent = rng.Next(trade.past.Length);
        p.goal = rng.Next(trade.goals.Length);
        do { p.problem = rng.Next(Problems.Length); } while (Problems[p.problem].notFor != null && Array.IndexOf(Problems[p.problem].notFor, c.archetypeIndex) >= 0);

        // What they want in a weapon follows from what happened to them - and the job's own needs.
        int stat = trade.past[p.pastEvent].stat;
        p.preferredStat = stat >= 0 ? stat : arch.durability ? StatDurability : arch.balance ? StatBalance : StatSharpness;
        p.preferredWeapon = PickWeapon(rng, arch, c.race);
        if (p.preferredStat == StatSharpness && !Weapons.Get(p.preferredWeapon).hasEdge) p.preferredStat = StatDurability; // a hammer has no edge to care about

        // Name: a cousin shares the family name; nobody shares a full name with someone the smith knows.
        var firsts = FirstNames(c.race);
        var taken = new HashSet<string>();
        if (h != null) foreach (var o in h.customers) taken.Add(o.name);
        if (forceName != null) c.name = forceName;
        else
        {
            var free = new List<string>();
            foreach (var n in firsts) if (!taken.Contains(n)) free.Add(n);
            c.name = free.Count > 0 ? free[rng.Next(free.Count)] : firsts[rng.Next(firsts.Length)];
        }
        var surnames = Surnames(c.race);
        p.surname = relative != null && p.relation == NpcRelation.Cousin && relative.race == c.race ? relative.profile.surname : surnames[rng.Next(surnames.Length)];
        var raceInfo = RaceInfo.Get(c.race);
        c.hair = raceInfo.hair[rng.Next(raceInfo.hair.Length)];
        c.profile.story = ComposeStory(c, h, rng);
        return c;
    }

    private static Race PickRace(System.Random rng)
    {
        float total = 0f;
        foreach (var r in RaceInfo.All) total += r.weight;
        double roll = rng.NextDouble() * total;
        foreach (var r in RaceInfo.All) { roll -= r.weight; if (roll <= 0) return r.race; }
        return Race.Human;
    }

    // Their folk's trades (their own special ones a little more often); about one in eight took up something else.
    // weight (WorldDirector): how much the living world favours each trade today (events, reputation, repetition) -
    // 0 shuts a trade out. Null = every trade alike (tests, older callers).
    private static int PickTrade(System.Random rng, Race race, out bool unusual, Func<int, float> weight = null)
    {
        var pool = new List<int>();
        var odd = new List<int>();
        var poolW = new List<float>();
        var oddW = new List<float>();
        for (int i = 0; i < Trades.Length && i < CustomerArchetype.All.Length; i++)
        {
            if (!IsGenerated(i)) continue;
            var a = CustomerArchetype.All[i];
            float w = weight != null ? Mathf.Max(0f, weight(i)) : 1f;
            if (a.TakenUpBy(race)) { pool.Add(i); poolW.Add(w * (a.races.Length == 1 && race != Race.Human ? 2f : 1f)); }
            else if (!TradeFor(i).restricted) { odd.Add(i); oddW.Add(w); }
        }
        unusual = odd.Count > 0 && rng.NextDouble() < 0.12;
        var from = unusual ? odd : pool;
        var fromW = unusual ? oddW : poolW;
        int pick = rng.Pick(fromW);
        if (pick < 0 && unusual) { unusual = false; from = pool; pick = rng.Pick(poolW); }
        return pick >= 0 ? from[pick] : from[rng.Next(from.Count)];
    }

    private static WeaponType PickWeapon(System.Random rng, CustomerArchetype a, Race race)
    {
        var bias = RaceInfo.Get(race).weaponBias;
        float total = 0f;
        foreach (var w in a.weapons) total += bias[(int)w];
        double roll = rng.NextDouble() * total;
        foreach (var w in a.weapons) { roll -= bias[(int)w]; if (roll <= 0) return w; }
        return a.weapons[a.weapons.Length - 1];
    }

    private static string Fill(string s, CustomerRecord c, WeaponType? weapon = null)
    {
        var p = c.profile;
        s = s.Replace("{n}", c.name).Replace("{y} years", p.years == 1 ? "a year" : $"{p.years} years").Replace("{y}", p.years.ToString()).Replace("{d}", Districts[Mathf.Clamp(p.district, 0, Districts.Length - 1)]);
        if (weapon.HasValue) s = s.Replace("{w}", Weapons.Get(weapon.Value).noun);
        return s;
    }

    private static readonly string[] Ages = { "young", "", "grey-haired" };
    private static readonly string[] Purses = { "Money is always tight.", "", "They live comfortably.", "They are wealthy, and it shows." };

    // The background, in the journal's voice - written once.
    private static string ComposeStory(CustomerRecord c, BlacksmithHistory h, System.Random rng)
    {
        var p = c.profile;
        var t = TradeFor(c.archetypeIndex);
        var raceInfo = RaceInfo.Get(c.race);
        var parts = new List<string>();
        string age = Ages[Mathf.Clamp(p.ageGroup, 0, 2)];
        string who = $"{(age.Length > 0 ? age + " " : "")}{Describe(c)}";
        parts.Add($"{FullName(c)} is {Article(who)} {who} {Home(p.district)}.");
        if (p.unusual) parts.Add($"Few {(c.race == Race.Elf ? "elves" : c.race == Race.Dwarf ? "dwarves" : "humans")} take up that trade - {c.name} did anyway.");
        parts.Add(Fill(t.backgrounds[p.background].story, c));
        parts.Add(Fill(t.past[p.pastEvent].story, c));
        parts.Add($"{c.name} {Fill(t.goals[p.goal].story, c)}, and {Problems[p.problem].text}.");
        if (Purses[p.wealth].Length > 0) parts.Add(Purses[p.wealth]);
        parts.Add($"Temperament: {Traits[p.trait1].name}, {Traits[p.trait2].name}. Prefers {Weapons.Get(p.preferredWeapon).WithArticle} with good {StatNames[p.preferredStat].ToLower()}.");
        var rel = h != null && p.relatedTo != 0 ? h.Customer(p.relatedTo) : null;
        if (rel != null) parts.Add($"{RelationWord(p.relation, rel, true)} of {FullName(rel)}.");
        return string.Join(" ", parts);
    }

    // "from the river quarter" / "who lives on the road"
    public static string Home(int district) => district == Road ? "who lives on the road" : $"from {Districts[Mathf.Clamp(district, 0, Districts.Length - 1)]}";

    private static string RelationWord(NpcRelation r, CustomerRecord other, bool capital)
    {
        string w = r == NpcRelation.Cousin ? "cousin" : r == NpcRelation.Friend ? "friend" : $"comrade ({CustomerArchetype.All[other.archetypeIndex].title})";
        return capital ? char.ToUpper(w[0]) + w.Substring(1) : w;
    }

    private static string Article(string word) => word.Length > 0 && "aeiou".IndexOf(char.ToLower(word[0])) >= 0 ? "an" : "a";

    // ---- what they ask for ----------------------------------------------------------------------------------

    private static readonly float[] TierValues = { 25f, 50f, 70f, 85f };
    private const int MaxSpeech = 360; // characters that fit the dialogue box with the pay line under them
    public static float ContractChance = 0.12f; // (a field, so tests can force one)
    private static readonly float[] PayByWealth = { 0.8f, 1.0f, 1.15f, 1.35f };
    private static readonly string[] WealthLines =
    {
        "I can't pay much, mind - it doesn't have to be fancy.", "", "I'll pay fair for good work.", "Money is no object - only the best.",
    };
    public static readonly string[] StatDemand =
    {
        "Whatever else, it has to be tough.", "Above all it has to sit right in the hand.", "And I want a real edge on it.",
    };

    // A new order from this person: their trade's job, their own reasons and insistence, what they can pay, how long
    // they'll wait - and what they remember of the smith. Marks the visit in their profile.
    // bias (WorldDirector): which weapons are about today and how much each is wanted (introduced types, city events,
    // repetition), a stat the city is worried about, a push toward harder jobs, a pay swing. Null = the old behaviour.
    public static CustomerOrder OrderFor(CustomerRecord c, BlacksmithHistory h, int day, ICollection<string> unlocked, float reputation, OrderBias bias = null)
    {
        EnsureProfile(c, h);
        var p = c.profile;
        var arch = CustomerArchetype.All[c.archetypeIndex];
        var order = new CustomerOrder
        {
            customerName = c.name,
            archetypeIndex = c.archetypeIndex,
            race = c.race,
            hair = c.hair,
            customerId = c.id,
            titleOverride = Title(c),
        };
        // Mostly the weapon they like; now and then another their trade uses.
        if (bias == null || bias.weaponWeight == null)
            order.weaponType = UnityEngine.Random.value < 0.75f ? p.preferredWeapon : arch.weapons[UnityEngine.Random.Range(0, arch.weapons.Length)];
        else
        {
            // Their favourite counts three times over the trade's others - then what's about today (and what the smith
            // has been asked for lately) tips the scales.
            var options = new List<WeaponType> { p.preferredWeapon };
            var weights = new List<float> { 3f * bias.weaponWeight(p.preferredWeapon) };
            foreach (var w in arch.weapons) { options.Add(w); weights.Add(bias.weaponWeight(w)); }
            float roll = UnityEngine.Random.value, total = 0f;
            foreach (var x in weights) total += x;
            order.weaponType = p.preferredWeapon;
            if (total > 0f)
                for (int i = 0; i < options.Count; i++) { roll -= weights[i] / total; if (roll <= 0f && weights[i] > 0f) { order.weaponType = options[i]; break; } }
        }
        if (!order.Weapon.hasEdge && p.preferredStat == StatSharpness && (bias == null || bias.weaponWeight == null || bias.weaponWeight(p.preferredWeapon) > 0f))
            order.weaponType = p.preferredWeapon;

        // How demanding: the smith's name and metals limit it, their standing pushes it up, their purse caps it.
        int byRep = reputation < CustomerOrder.RepForTier2 ? 2 : reputation < CustomerOrder.RepForTier3 ? 3 : TierValues.Length;
        int maxTier = Mathf.Min(Mathf.Min(day <= 2 ? 2 : TierValues.Length, byRep), MaxTierFor(unlocked) + 1);
        int tier = UnityEngine.Random.Range(0, maxTier);
        if (p.rank > tier && UnityEngine.Random.value < 0.8f) tier = Mathf.Min(p.rank, maxTier - 1); // a master of their trade brings a master's job
        if (reputation < CustomerOrder.RepForTier2 && tier > 0 && UnityEngine.Random.value < 0.4f) tier = 0;
        if (bias != null && bias.tierUp > 0f && tier < maxTier - 1 && UnityEngine.Random.value < bias.tierUp) tier++; // a big occasion raises the stakes
        int purseCap = p.wealth == 0 ? 1 : p.wealth == 1 ? 2 : 3;
        order.tier = Mathf.Clamp(tier, 0, purseCap);
        // Now and then a contract: the watch, a knightly order or the gate company wants several weapons at once - modest
        // ones, all to the same standard (the order holds each weapon's id; it's ready when all are set aside).
        bool contract = (c.archetypeIndex == 12 || c.archetypeIndex == 5 || c.archetypeIndex == 10) && p.wealth >= 2 && UnityEngine.Random.value < ContractChance;
        var time = TimeSettings.Current;
        if (contract) { order.quantity = UnityEngine.Random.Range(time.contractSize.x, time.contractSize.y + 1); order.tier = Mathf.Min(order.tier, 1); }
        // Now and then someone in a hurry: wanted by tomorrow evening, and paid for it. Never a contract, never the poor.
        order.rush = !contract && p.wealth >= 1 && UnityEngine.Random.value < time.rushOrderChance;
        float need = TierValues[order.tier];
        bool edge = order.Weapon.hasEdge;
        order.minDurability = arch.durability ? need : 0f;
        order.minBalance = arch.balance ? need : 0f;
        order.minSharpness = arch.sharpness && edge ? need : 0f;
        if (order.minDurability <= 0f && order.minBalance <= 0f && order.minSharpness <= 0f) order.minDurability = need;
        // What their past taught them to insist on - always part of the job, whatever the trade leans on.
        if (p.preferredStat == StatDurability) order.minDurability = Mathf.Max(order.minDurability, need - (arch.durability ? 0f : 10f));
        if (p.preferredStat == StatBalance) order.minBalance = Mathf.Max(order.minBalance, need - (arch.balance ? 0f : 10f));
        if (p.preferredStat == StatSharpness && edge) order.minSharpness = Mathf.Max(order.minSharpness, need - (arch.sharpness ? 0f : 10f));
        // What the whole city worries about today (bandits: toughness; the elves' visit: balance) creeps into the job.
        bool focusAdded = false;
        if (bias != null && bias.statFocus >= 0 && order.tier >= 1 && UnityEngine.Random.value < 0.5f)
        {
            if (bias.statFocus == StatDurability && order.minDurability <= 0f) { order.minDurability = need - 10f; focusAdded = true; }
            else if (bias.statFocus == StatBalance && order.minBalance <= 0f) { order.minBalance = need - 10f; focusAdded = true; }
            else if (bias.statFocus == StatSharpness && edge && order.minSharpness <= 0f) { order.minSharpness = need - 10f; focusAdded = true; }
        }
        DiscountMultiStat(order);

        // A wealthy customer with a serious job may insist on the best metal the smith can work (never a poor one).
        bool Has(string m) => unlocked == null || unlocked.Contains(m);
        if (p.wealth >= 3 && order.tier >= 1 && UnityEngine.Random.value < 0.6f)
        {
            string metal = Has("Steel") ? "Steel" : Has("Iron") ? "Iron" : null;
            if (metal != null && ForgeMaterials.Find(metal).maxQuality >= order.HighestNeed + 5f) order.requiredMaterial = metal;
        }
        int statCount = (order.minDurability > 0f ? 1 : 0) + (order.minBalance > 0f ? 1 : 0) + (order.minSharpness > 0f ? 1 : 0);
        order.reward = Mathf.RoundToInt((CustomerOrder.BaseFee + need * CustomerOrder.FeePerPoint * statCount * (order.requiredMaterial != null ? 1.2f : 1f)) *
                                        PayByWealth[Mathf.Clamp(p.wealth, 0, 3)]);
        if (order.rush) order.reward = Mathf.RoundToInt(order.reward * time.rushPayMultiplier);
        if (bias != null && bias.payMul > 0f && !Mathf.Approximately(bias.payMul, 1f)) order.reward = Mathf.RoundToInt(order.reward * bias.payMul);
        order.patienceDays = PatienceOf(c);

        // Their words: greeting, who they are (or what they remember), why, the job, what they insist on, the purse.
        var words = new List<string>();
        var t = TradeFor(c.archetypeIndex);
        words.Add(Pick(Traits[p.trait1].greet, p.timesMet));
        bool first = p.timesMet == 0;
        if (first)
        {
            string desc = Describe(c);
            words.Add($"I'm {FullName(c)}, {Article(desc)} {desc}.");
            string relLine = RelationLine(c, h);
            if (relLine != null) words.Add(relLine);
        }
        else
        {
            words.Add($"It's {c.name} again.");
            string memory = MemoryLine(c, h, day, aboutWeapon: true);
            if (memory != null) words.Add(memory);
        }
        // Why: a different part of their story each visit (what happened to them, what they're after, where they come from).
        Frag reason = (p.timesMet % 3) == 0 ? t.past[p.pastEvent] : (p.timesMet % 3) == 1 ? t.goals[p.goal] : t.backgrounds[p.background];
        string reasonSaid = Fill(reason.said, c, order.weaponType);
        words.Add(reasonSaid);
        string job = arch.Line(arch.tierLines[order.tier], order.weaponType);
        if (!first && p.rank > 0 && order.tier >= 2 && UnityEngine.Random.value < 0.5f) job = $"This time I need something better than before. {job}";
        words.Add(job);
        if (order.quantity > 1)
            words.Add($"It's for {(c.archetypeIndex == 12 ? "the watch" : c.archetypeIndex == 5 ? "our order's squires" : "the gate company")} - I need {order.quantity} of them, every one to that standard.");
        if (order.rush) words.Add("And I need it by tomorrow evening - I'll pay extra for the hurry.");
        // What they insist on - only if it's part of this job (a hammer has no edge to insist on).
        string demand = p.preferredStat == StatSharpness && !order.Weapon.hasEdge ? null : StatDemand[p.preferredStat];
        words.Add(demand);
        if (focusAdded && bias.focusLine != null) words.Add(bias.focusLine);
        if (order.requiredMaterial != null) words.Add($"And it must be {order.requiredMaterial.ToLower()}.");
        // The purse and a closing remark only while it still fits the dialogue box (the essentials always do).
        string purse = WealthLines[p.wealth].Length > 0 && UnityEngine.Random.value < 0.7f ? WealthLines[p.wealth] : null;
        string close = Traits[p.trait2].close;
        words.RemoveAll(string.IsNullOrEmpty);
        int length = string.Join(" ", words).Length;
        // Someone back again with a lot to tell (news, the weapon you made them) skips the old reasons.
        if (!first && length > MaxSpeech) { words.Remove(reasonSaid); length = string.Join(" ", words).Length; }
        if (purse != null && length + purse.Length < MaxSpeech) { words.Add(purse); length += purse.Length + 1; }
        if (close != null && length + close.Length < MaxSpeech) words.Add(close);
        order.requestText = string.Join(" ", words);
        order.paperQuote = demand != null ? $"{job} {demand}" : job;
        if (focusAdded && bias.focusLine != null) order.paperQuote += " " + bias.focusLine;

        Seen(c, day);
        return order;
    }

    // Someone the smith knows turns up again (any visit): counted, and life events told since are "heard".
    public static void Seen(CustomerRecord c, int day)
    {
        c.profile.timesMet++;
        c.profile.lastSeenDay = day;
    }

    private static string Pick(string[] lines, int turn) => lines == null || lines.Length == 0 ? "" : lines[Mathf.Abs(turn) % lines.Length];

    private static int MaxTierFor(ICollection<string> unlocked)
    {
        float cap = 0f;
        foreach (var m in ForgeMaterials.All)
            if (unlocked == null || unlocked.Contains(m.name)) cap = Mathf.Max(cap, m.maxQuality);
        int best = 0;
        for (int t = 0; t < TierValues.Length; t++) if (TierValues[t] <= cap - 6f) best = t;
        return best;
    }

    private static void DiscountMultiStat(CustomerOrder o)
    {
        int count = (o.minDurability > 0f ? 1 : 0) + (o.minBalance > 0f ? 1 : 0) + (o.minSharpness > 0f ? 1 : 0);
        if (count < 2) return;
        float cut = CustomerOrder.MultiStatDiscount * (count - 1);
        if (o.minDurability > 0f) o.minDurability = Mathf.Max(20f, o.minDurability - cut);
        if (o.minBalance > 0f) o.minBalance = Mathf.Max(20f, o.minBalance - cut);
        if (o.minSharpness > 0f) o.minSharpness = Mathf.Max(20f, o.minSharpness - cut);
    }

    // How many days they'll wait for an order: their temperament, two to four.
    public static int PatienceOf(CustomerRecord c)
        => Mathf.Clamp(CustomerOrder.PatienceDays + Traits[c.profile.trait1].patience + Traits[c.profile.trait2].patience, 2, 4);

    public static string Title(CustomerRecord c) => $"{FullName(c)}, {Describe(c)}";

    // A greeting in their own manner (for visits that aren't new orders: repairs, stories).
    public static string Greeting(CustomerRecord c)
    {
        if (c == null || c.profile == null || !c.profile.IsSet) return "";
        return Pick(Traits[c.profile.trait1].greet, c.profile.timesMet);
    }

    // ---- remembering ----------------------------------------------------------------------------------------

    // What they bring up when they come back: news of their own since the last visit, the weapon of yours they
    // carry (or lost), a repair you did, the day you turned them away. Null if there's nothing worth saying.
    public static string MemoryLine(CustomerRecord c, BlacksmithHistory h, int day, bool aboutWeapon)
    {
        var p = c.profile;
        var parts = new List<string>();
        var news = p.lifeEvents.FindLast(e => e.day > p.lastSeenDay);
        if (news != null) parts.Add($"Since we last spoke - {news.said}");
        if (aboutWeapon)
        {
            var mine = h.swords.FindLast(r => r.ownerId == c.id);
            if (mine != null)
            {
                if (mine.status == SwordStatus.OwnedByCustomer)
                    parts.Add(mine.repairCount > 0
                        ? $"You put {mine.Label} right for me once already - it's still at my side."
                        : mine.condition >= 60f ? $"{mine.Label}, the {mine.sword.Weapon.noun} you made me, has served me well." : $"{mine.Label} is getting worn, but it's still with me.");
                else if (mine.status == SwordStatus.Destroyed)
                    parts.Add($"{mine.Label} finally broke on me - it had a good life.");
            }
            else if (p.declinedDay > 0 && p.declinedDay >= p.firstMetDay)
                parts.Add($"You turned me away on day {p.declinedDay}. I hope you've room for me today.");
        }
        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    // A new face who knows someone the smith knows.
    public static string RelationLine(CustomerRecord c, BlacksmithHistory h)
    {
        var p = c.profile;
        if (p.relatedTo == 0) return null;
        var other = h.Customer(p.relatedTo);
        if (other == null) return null;
        var bought = h.swords.FindLast(r => r.ownerId == other.id);
        string who = RelationWho(p, other);
        return bought != null
            ? $"{who} bought {bought.Label} from you. Wouldn't stop talking about it."
            : $"{who} told me to come to you.";
    }

    private static string RelationWho(NpcProfile p, CustomerRecord other)
        => p.relation == NpcRelation.Cousin ? $"My cousin {other.name}" : p.relation == NpcRelation.Friend ? $"My friend {other.name}" : $"{other.name} - we're both in the same line of work -";

    // Word of mouth (WorldDirector.Referred): what the one who sent them said about a particular weapon of yours -
    // praise that brought them here, or a grumble that has them on their guard.
    public static string ReferralLine(CustomerRecord c, BlacksmithHistory h, SwordRecord sword, bool positive)
    {
        var other = h.Customer(c.profile.relatedTo);
        if (other == null) return null;
        string who = RelationWho(c.profile, other);
        string what = sword != null ? $"{sword.Label}, the {sword.sword.Weapon.noun} you made" : "what you made";
        return positive
            ? $"{who} showed me {what}, and hasn't stopped talking about it - so here I am. I want something like it, but my own."
            : $"{who} had {what}, and it didn't turn out as promised. I hope you'll do better by me.";
    }

    // ---- time passing for them too ----------------------------------------------------------------------------

    private static readonly string[] Orders = { "the Silver Lance", "the Ember Shield", "the Grey Oak", "the Vigil" };
    private static readonly string[] Shops = { "a stall on the plaza", "a little shop by the river", "a place of my own in the market streets" };

    // Once a day: people the smith knows now and then have something happen to them. Small things, mostly -
    // only one event per person in a while, and never for someone they've barely met.
    public const int MaxLifeEvents = 4; // small lives, not sagas

    public static void LiveADay(BlacksmithHistory h, int day)
    {
        foreach (var c in h.customers)
        {
            var p = c.profile;
            if (p == null || !p.IsSet || !IsGenerated(c.archetypeIndex) || p.timesMet == 0) continue;
            if (p.lifeEvents.Count >= MaxLifeEvents || day - Mathf.Max(p.lastLifeEventDay, p.firstMetDay) < 9) continue;
            if (UnityEngine.Random.value > 0.06f) continue;
            var e = LifeEvent(c, h, day);
            if (e == null) continue;
            p.lifeEvents.Add(e);
            p.lastLifeEventDay = day;
            h.LogPerson(day, HistoryEventType.LifeEvent, c, e.note);
        }
    }

    public static NpcLifeEvent LifeEvent(CustomerRecord c, BlacksmithHistory h, int day)
    {
        var p = c.profile;
        var t = TradeFor(c.archetypeIndex);
        int a = c.archetypeIndex;
        bool fighter = t.military || a == 0 || a == 3 || a == 7;
        bool owns = h.swords.Exists(r => r.ownerId == c.id && (r.status == SwordStatus.OwnedByCustomer || r.status == SwordStatus.NeedsRepair));
        var options = new List<NpcLifeKind>();
        if (p.rank < 2) { options.Add(NpcLifeKind.Promoted); options.Add(NpcLifeKind.Promoted); }
        if (fighter || a == 1 || a == 9 || a == 14) options.Add(NpcLifeKind.Injured);
        if (a == 12 || a == 5 || a == 10) options.Add(NpcLifeKind.JoinedOrder);
        if (a == 15 || a == 13 || a == 1 || a == 4) options.Add(NpcLifeKind.TookToTheRoad);
        if (p.wealth < 3 && (a == 15 || a == 2 || a == 16 || a == 6 || a == 14)) options.Add(NpcLifeKind.BecameWealthier);
        if (!owns) options.Add(NpcLifeKind.LostEverything); // never contradicts a weapon of yours they still have
        if (a == 0 || a == 5 || a == 4) options.Add(NpcLifeKind.WonTournament);
        if (a == 3 || a == 5 || a == 10 || a == 12) options.Add(NpcLifeKind.ReturnedFromWar);
        if (p.wealth < 3 && (a == 2 || a == 15 || a == 6 || a == 16)) options.Add(NpcLifeKind.OpenedShop);
        if (ChangedTradeTo(a) >= 0 && UnityEngine.Random.value < 0.4f) options.Add(NpcLifeKind.ChangedTrade);
        options.Add(NpcLifeKind.Married);
        if (p.ageGroup >= 1) options.Add(NpcLifeKind.BecameParent);
        // Once-in-a-life things happen once (and a trade changes at most once).
        options.RemoveAll(k => (k == NpcLifeKind.Married || k == NpcLifeKind.BecameParent || k == NpcLifeKind.ChangedTrade || k == NpcLifeKind.JoinedOrder ||
                                k == NpcLifeKind.OpenedShop || k == NpcLifeKind.LostEverything) && p.lifeEvents.Exists(x => x.kind == k));
        if (options.Count == 0) return null;
        var kind = options[UnityEngine.Random.Range(0, options.Count)];

        var e = new NpcLifeEvent { day = day, kind = kind };
        switch (kind)
        {
            case NpcLifeKind.Promoted:
                p.rank++;
                if (p.wealth < t.maxWealth && UnityEngine.Random.value < 0.5f) p.wealth++;
                e.said = $"they've made me {RankTitle(c)}!";
                e.note = $"Became {RankTitle(c)}";
                break;
            case NpcLifeKind.Injured:
                p.preferredStat = StatBalance; // a weaker arm wants something that handles easily
                e.said = "I took a bad wound and I'm still healing. I need things lighter in the hand now.";
                e.note = "Badly injured - now wants weapons that handle easily";
                break;
            case NpcLifeKind.JoinedOrder:
            {
                string order = Orders[UnityEngine.Random.Range(0, Orders.Length)];
                if (a == 12) { c.archetypeIndex = 5; p.rank = 0; } // a guard taken into a knightly order starts as a squire
                e.said = $"I've sworn the oath of the Order of {order}!";
                e.note = $"Joined the Order of {order}" + (a == 12 ? " (now a squire)" : "");
                break;
            }
            case NpcLifeKind.TookToTheRoad:
                p.district = Road;
                p.preferredStat = StatBalance;
                e.said = "I've taken to the road - more travelling than living, these days.";
                e.note = "Took to the road";
                break;
            case NpcLifeKind.BecameWealthier:
                p.wealth++;
                e.said = "business has been very kind to me lately.";
                e.note = "Came into money";
                break;
            case NpcLifeKind.LostEverything:
                if (p.wealth > 0) p.wealth--;
                e.said = "I lost all my gear crossing the river. Everything, gone.";
                e.note = "Lost all their gear in the river";
                break;
            case NpcLifeKind.WonTournament:
                if (p.rank < 2) p.rank++;
                e.said = "I won the spring tournament! Can you believe it?";
                e.note = "Won the spring tournament";
                break;
            case NpcLifeKind.ReturnedFromWar:
                p.preferredStat = StatDurability;
                e.said = "I'm back from the border war. I trust only things that don't break now.";
                e.note = "Came back from the border war";
                break;
            case NpcLifeKind.OpenedShop:
            {
                string shop = Shops[UnityEngine.Random.Range(0, Shops.Length)];
                p.wealth = Mathf.Min(3, p.wealth + 1);
                p.district = Market;
                e.said = $"I've opened {shop}!";
                e.note = "Opened a shop of their own";
                break;
            }
            case NpcLifeKind.ChangedTrade:
            {
                string before = RankTitle(c);
                c.archetypeIndex = ChangedTradeTo(a);
                p.rank = 0;
                var arch = CustomerArchetype.All[c.archetypeIndex];
                if (Array.IndexOf(arch.weapons, p.preferredWeapon) < 0) p.preferredWeapon = arch.weapons[0];
                e.said = $"I've given up being {Article(before)} {before} - I'm {Article(RankTitle(c))} {RankTitle(c)} now.";
                e.note = $"Gave up being {Article(before)} {before} to become {Article(RankTitle(c))} {RankTitle(c)}";
                break;
            }
            case NpcLifeKind.Married:
                e.said = "I got married! The whole street came.";
                e.note = "Got married";
                break;
            default:
                e.said = "we've had a baby - I've not slept in a week.";
                e.note = "Became a parent";
                break;
        }
        if (!Weapons.Get(p.preferredWeapon).hasEdge && p.preferredStat == StatSharpness) p.preferredStat = StatDurability;
        return e;
    }

    // Trades people plausibly move between.
    private static int ChangedTradeTo(int a)
    {
        switch (a)
        {
            case 3: return 12;  // mercenary -> city guard
            case 1: return 13;  // hunter -> scout
            case 2: return 15;  // cook -> merchant
            case 6: return 16;  // farmer -> boatman
            case 16: return 15; // boatman -> merchant
            case 9: return 14;  // miner -> stoneworker
        }
        return -1;
    }

    // ---- the journal ------------------------------------------------------------------------------------------

    // A few lines about who owns a weapon: who they are, their story in a sentence, and their latest news.
    public static string OwnerBlock(CustomerRecord c, BlacksmithHistory h)
    {
        EnsureProfile(c, h);
        var p = c.profile;
        var lines = new List<string> { $"OWNER  -  NPC_{c.id:0000}", $"{FullName(c)}, {Describe(c)}", $"{Cap(Traits[p.trait1].name)}, {Traits[p.trait2].name}  -  {Cap(p.district == Road ? "on the road" : Districts[p.district])}" };
        var t = TradeFor(c.archetypeIndex);
        lines.Add(Fill(t.past[p.pastEvent].story, c));
        var rel = p.relatedTo != 0 ? h.Customer(p.relatedTo) : null;
        if (rel != null) lines.Add($"{RelationWord(p.relation, rel, true)} of {FullName(rel)}");
        var news = p.lifeEvents.Count > 0 ? p.lifeEvents[p.lifeEvents.Count - 1] : null;
        if (news != null) lines.Add($"Latest: day {news.day} - {news.note}");
        return string.Join("\n", lines);
    }

    private static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);
}
