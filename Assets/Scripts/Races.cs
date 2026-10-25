using UnityEngine;

// The folk who come to the smithy. A race is a set of *tendencies*, never rules: it shifts which jobs a
// customer tends to have, which weapon they tend to want, which stat they care about and how they talk -
// an elf can still walk in wanting a sword, a dwarf a spear. Stored as an int on orders and customer
// records (default 0 = Human, so saves from before races existed load as humans).
public enum Race { Human, Elf, Dwarf }

public class RaceInfo
{
    public Race race;
    public string name;        // "Elf"
    public string adjective;   // "elven" - "" for humans (a human fencer is just "the fencer")
    public float weight;       // how often they turn up
    public string[] names;
    public Color[] hair;
    public float[] weaponBias = { 1f, 1f, 1f, 1f, 1f }; // multiplier per WeaponType on an archetype's weapon choice
    public bool caresBalance, caresDurability;          // the stat they add to an order even when the job doesn't need it
    public float strongMetalChance;                     // chance a serious order insists on a hard metal
    // Coming back with a weapon they bought: the chance they ever do, and after how many days (rolled once, at the
    // sale). Elves are patient and long-lived, dwarves use their things hard.
    public float returnChance = 0.6f;
    public Vector2Int returnDays = new Vector2Int(3, 10);
    public string[] greetings;                          // {0} = name, {1} = "a fencer" / "an elven ranger"
    public string[] balanceLines, durabilityLines, sharpLines; // how they phrase the stat they insist on
    public string[] metalLines;                         // {0} = metal

    public static readonly RaceInfo[] All =
    {
        new RaceInfo
        {
            race = Race.Human, name = "Human", adjective = "", weight = 0.5f,
            names = new[] { "Mira", "Oskar", "Bohdan", "Yara", "Tobias", "Lesya", "Ansel", "Greta", "Ivo", "Marta", "Pip", "Roderick", "Zoryana", "Hugo", "Ilka", "Taras" },
            hair = new[]
            {
                new Color(0.25f, 0.17f, 0.10f), new Color(0.12f, 0.10f, 0.08f), new Color(0.78f, 0.62f, 0.32f),
                new Color(0.55f, 0.25f, 0.12f), new Color(0.70f, 0.70f, 0.72f),
            },
            strongMetalChance = 0f,
            greetings = new[] { "I'm {0}, {1}.", "Morning, smith. {0} - {1}.", "Hello there! I'm {0}, {1}." },
            balanceLines = new[] { "It has to sit evenly in the hand." },
            durabilityLines = new[] { "And it has to last - I can't afford a new one every month." },
            sharpLines = new[] { "Give it a proper edge, too." },
            metalLines = new[] { "And it must be {0} - I have a reputation to keep." },
        },
        new RaceInfo
        {
            race = Race.Elf, name = "Elf", adjective = "elven", weight = 0.25f, returnChance = 0.5f, returnDays = new Vector2Int(5, 14),
            names = new[] { "Aelar", "Sylwen", "Thalion", "Elowen", "Lirael", "Faelan", "Nimriel", "Caladrel", "Ithilen", "Mirelle" },
            hair = new[]
            {
                new Color(0.92f, 0.86f, 0.62f), new Color(0.85f, 0.85f, 0.88f), new Color(0.10f, 0.09f, 0.12f),
                new Color(0.62f, 0.34f, 0.18f),
            },
            weaponBias = new[] { 0.8f, 3.0f, 1.4f, 1.0f, 0.3f },
            caresBalance = true, strongMetalChance = 0f,
            greetings = new[]
            {
                "Well met, smith. I am {0}, {1}.",
                "Greetings. They call me {0} - {1}, from the old woods.",
                "The trees said you work with care. I am {0}, {1}.",
            },
            balanceLines = new[]
            {
                "It must feel light, and perfectly balanced.",
                "Balance above all - it should move like a leaf on the wind.",
                "I want excellent balance. Nothing heavy, nothing crooked.",
            },
            durabilityLines = new[] { "It should not break at the first hard touch." },
            sharpLines = new[] { "Make sure the edge is sharp.", "The edge should part a falling hair." },
            metalLines = new[] { "Forge it from {0}, if you would." },
        },
        new RaceInfo
        {
            race = Race.Dwarf, name = "Dwarf", adjective = "dwarven", weight = 0.25f, returnChance = 0.7f, returnDays = new Vector2Int(3, 9),
            names = new[] { "Borin", "Dagna", "Thrain", "Hilda", "Gorrim", "Brunhild", "Durgin", "Magda", "Kili", "Orsa" },
            hair = new[]
            {
                new Color(0.62f, 0.24f, 0.10f), new Color(0.30f, 0.18f, 0.10f), new Color(0.12f, 0.10f, 0.08f),
                new Color(0.66f, 0.64f, 0.62f),
            },
            weaponBias = new[] { 1.0f, 0.4f, 0.4f, 1.2f, 3.0f },
            caresDurability = true, strongMetalChance = 0.5f,
            greetings = new[]
            {
                "Oi, smith! {0}, {1}.",
                "Name's {0}. {1}, from under the mountain.",
                "{0}, {1}. Heard you swing a decent hammer.",
            },
            balanceLines = new[] { "Mind it swings true, mind." },
            durabilityLines = new[]
            {
                "It has to take a beating.",
                "I care more about toughness than looks.",
                "If it bends, I'm bringing it back - and I'm bringing my cousins.",
            },
            sharpLines = new[] { "And a decent edge wouldn't hurt." },
            metalLines = new[] { "Good solid {0}, none of that soft copper.", "Make it {0}. Proper metal." },
        },
    };

    public static RaceInfo Get(Race race)
    {
        int i = (int)race;
        return i >= 0 && i < All.Length ? All[i] : All[0];
    }

    public static Race Pick()
    {
        float total = 0f;
        foreach (var r in All) total += r.weight;
        float roll = Random.value * total;
        foreach (var r in All)
        {
            roll -= r.weight;
            if (roll <= 0f) return r.race;
        }
        return Race.Human;
    }

    public static string PickLine(string[] lines) => lines == null || lines.Length == 0 ? "" : lines[Random.Range(0, lines.Length)];
}
