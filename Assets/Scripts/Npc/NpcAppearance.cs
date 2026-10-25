using UnityEngine;

// What one townsperson looks like: a race, a set of interchangeable parts (hair, beard, headwear, outfit, extras)
// and their colours. NpcBody builds the skinned mesh from it; identical looks share one mesh (Key).
// Generate() picks a believable combination for a race and a role from a seed - the same seed always gives the
// same person (a customer on record looks the same at the smith's table and out in the city).
public enum NpcHair { Bald, Short, Swept, Long, Tied, LongStraight, LongBraid, Fringe }
public enum NpcBeard { None, Full, Short, Mustache, Goatee, Big, Forked, Braided }
public enum NpcHeadwear { None, Hood, Hat, Cap, Helmet, Circlet }
public enum NpcOutfit { Vest, Tunic, Coat, Apron, Tabard, Armor, Robe }

public class NpcAppearance
{
    public Race race;
    public CityRole role;
    public NpcHair hair;
    public NpcBeard beard;
    public NpcHeadwear headwear;
    public NpcOutfit outfit;
    public bool cape, pouch, bracers, trim, gloves;
    public Color skin, hairColor, beardColor, main, shirt, trousers, boots, belt, accent, extra;

    // Everything that changes the mesh, as a string: equal keys share one mesh.
    public string Key => $"{race}/{hair}/{beard}/{headwear}/{outfit}/{(cape ? 1 : 0)}{(pouch ? 1 : 0)}{(bracers ? 1 : 0)}{(trim ? 1 : 0)}{(gloves ? 1 : 0)}/" +
                         $"{Hex(skin)}{Hex(hairColor)}{Hex(beardColor)}{Hex(main)}{Hex(shirt)}{Hex(trousers)}{Hex(boots)}{Hex(belt)}{Hex(accent)}{Hex(extra)}";

    private static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

    // ---- colours ----------------------------------------------------------------------------------------------
    private static Color C(float r, float g, float b) => new Color(r, g, b);
    private static readonly Color[] Skins = { C(0.93f, 0.72f, 0.55f), C(0.86f, 0.64f, 0.47f), C(0.74f, 0.52f, 0.37f), C(0.56f, 0.38f, 0.26f), C(0.96f, 0.80f, 0.65f) };
    private static readonly Color[] ElfSkins = { C(0.97f, 0.84f, 0.70f), C(0.93f, 0.78f, 0.64f), C(0.86f, 0.70f, 0.56f) };
    private static readonly Color[] DwarfSkins = { C(0.93f, 0.70f, 0.52f), C(0.88f, 0.64f, 0.48f), C(0.78f, 0.55f, 0.40f) };
    private static readonly Color[] Earthy = { C(0.47f, 0.28f, 0.16f), C(0.36f, 0.42f, 0.24f), C(0.30f, 0.38f, 0.52f), C(0.56f, 0.24f, 0.18f), C(0.62f, 0.50f, 0.28f), C(0.40f, 0.34f, 0.40f), C(0.26f, 0.36f, 0.34f) };
    private static readonly Color[] Rich = { C(0.44f, 0.16f, 0.40f), C(0.14f, 0.30f, 0.46f), C(0.52f, 0.12f, 0.16f), C(0.16f, 0.36f, 0.24f), C(0.20f, 0.20f, 0.28f) };
    private static readonly Color[] ElfGreens = { C(0.24f, 0.44f, 0.24f), C(0.18f, 0.36f, 0.34f), C(0.30f, 0.30f, 0.50f), C(0.44f, 0.20f, 0.26f), C(0.36f, 0.46f, 0.22f) };
    private static readonly Color[] Shirts = { C(0.90f, 0.86f, 0.74f), C(0.84f, 0.80f, 0.68f), C(0.78f, 0.74f, 0.62f), C(0.70f, 0.66f, 0.58f) };
    private static readonly Color[] DwarfShirts = { C(0.28f, 0.40f, 0.62f), C(0.52f, 0.20f, 0.16f), C(0.30f, 0.44f, 0.28f), C(0.48f, 0.42f, 0.30f) };
    private static readonly Color[] Trousers = { C(0.33f, 0.24f, 0.17f), C(0.24f, 0.22f, 0.22f), C(0.30f, 0.33f, 0.26f), C(0.28f, 0.28f, 0.34f) };
    private static readonly Color[] Leather = { C(0.40f, 0.24f, 0.13f), C(0.33f, 0.20f, 0.11f), C(0.46f, 0.30f, 0.16f) };
    private static readonly Color Gold = C(0.86f, 0.64f, 0.18f), Silver = C(0.74f, 0.76f, 0.80f), Steel = C(0.56f, 0.58f, 0.63f), DarkBelt = C(0.20f, 0.13f, 0.08f);
    private static readonly Color[] HumanHair = { C(0.36f, 0.22f, 0.12f), C(0.20f, 0.14f, 0.09f), C(0.72f, 0.54f, 0.28f), C(0.50f, 0.24f, 0.12f), C(0.60f, 0.60f, 0.62f), C(0.10f, 0.08f, 0.07f) };
    private static readonly Color[] ElfHair = { C(0.93f, 0.84f, 0.56f), C(0.88f, 0.88f, 0.90f), C(0.16f, 0.13f, 0.15f), C(0.70f, 0.40f, 0.22f), C(0.84f, 0.72f, 0.44f) };
    private static readonly Color[] DwarfHair = { C(0.70f, 0.30f, 0.12f), C(0.46f, 0.24f, 0.12f), C(0.18f, 0.12f, 0.08f), C(0.66f, 0.64f, 0.62f), C(0.84f, 0.52f, 0.20f) };

    private static T Pick<T>(System.Random r, T[] a) => a[r.Next(a.Length)];

    // ---- a person ---------------------------------------------------------------------------------------------

    public static NpcAppearance Generate(Race race, CityRole role, int seed, Color? mainColor = null, Color? hairColor = null)
    {
        var r = new System.Random(seed);
        var a = new NpcAppearance { race = race, role = role };
        a.trousers = Pick(r, Trousers);
        a.boots = Pick(r, Leather);
        a.belt = DarkBelt;
        a.accent = Gold;
        a.shirt = Pick(r, Shirts);
        a.extra = Pick(r, Leather);

        switch (race)
        {
            case Race.Elf:
                a.skin = Pick(r, ElfSkins);
                a.hairColor = Pick(r, ElfHair);
                a.hair = r.NextDouble() < 0.5 ? NpcHair.LongStraight : r.NextDouble() < 0.6 ? NpcHair.LongBraid : NpcHair.Swept;
                a.beard = NpcBeard.None;
                a.outfit = NpcOutfit.Coat;
                a.main = Pick(r, ElfGreens);
                a.trousers = C(0.18f, 0.17f, 0.19f);
                a.trim = true;
                a.accent = r.NextDouble() < 0.6 ? Gold : Silver;
                a.bracers = true;
                a.extra = Pick(r, Leather);
                if (r.NextDouble() < 0.15) a.headwear = NpcHeadwear.Circlet;
                break;
            case Race.Dwarf:
                a.skin = Pick(r, DwarfSkins);
                a.hairColor = Pick(r, DwarfHair);
                a.hair = r.NextDouble() < 0.55 ? NpcHair.Bald : NpcHair.Tied;
                a.beard = r.NextDouble() < 0.5 ? NpcBeard.Big : r.NextDouble() < 0.5 ? NpcBeard.Forked : NpcBeard.Braided;
                a.outfit = NpcOutfit.Apron;
                a.shirt = Pick(r, DwarfShirts);
                a.main = Pick(r, Leather);
                a.trousers = C(0.28f, 0.32f, 0.26f);
                a.bracers = true;
                a.pouch = r.NextDouble() < 0.4;
                break;
            default:
                a.skin = Pick(r, Skins);
                a.hairColor = Pick(r, HumanHair);
                double h = r.NextDouble();
                a.hair = h < 0.38 ? NpcHair.Short : h < 0.55 ? NpcHair.Swept : h < 0.68 ? NpcHair.Tied : h < 0.8 ? NpcHair.Long : h < 0.9 ? NpcHair.Fringe : NpcHair.Bald;
                double b = r.NextDouble();
                a.beard = b < 0.42 ? NpcBeard.None : b < 0.66 ? NpcBeard.Full : b < 0.8 ? NpcBeard.Short : b < 0.92 ? NpcBeard.Mustache : NpcBeard.Goatee;
                a.outfit = r.NextDouble() < 0.6 ? NpcOutfit.Vest : NpcOutfit.Tunic;
                a.main = Pick(r, Earthy);
                break;
        }
        a.beardColor = a.hairColor;

        // What they do shows in what they wear.
        switch (role)
        {
            case CityRole.Guard:
                a.outfit = NpcOutfit.Tabard;
                a.main = C(0.60f, 0.13f, 0.12f);
                a.shirt = Steel;
                a.accent = Gold;
                a.headwear = NpcHeadwear.Helmet;
                a.gloves = true;
                a.trousers = C(0.22f, 0.21f, 0.22f);
                break;
            case CityRole.Knight:
                a.outfit = NpcOutfit.Armor;
                a.main = Pick(r, Rich);
                a.shirt = Steel;
                a.accent = Gold;
                a.cape = r.NextDouble() < 0.6;
                a.extra = Pick(r, Rich);
                a.headwear = r.NextDouble() < 0.5 ? NpcHeadwear.Helmet : NpcHeadwear.None;
                a.gloves = true;
                break;
            case CityRole.Merchant:
                a.main = Pick(r, Rich);
                a.outfit = race == Race.Dwarf ? NpcOutfit.Vest : NpcOutfit.Coat;
                a.trim = true;
                a.pouch = true;
                if (r.NextDouble() < 0.55) a.headwear = NpcHeadwear.Hat;
                a.extra = Pick(r, Rich);
                break;
            case CityRole.Noble:
                a.main = Pick(r, Rich);
                a.outfit = NpcOutfit.Coat;
                a.trim = true;
                a.cape = true;
                a.extra = Pick(r, Rich);
                if (race != Race.Elf && r.NextDouble() < 0.35) a.headwear = NpcHeadwear.Hat;
                break;
            case CityRole.Craftsman:
                a.outfit = NpcOutfit.Apron;
                if (race == Race.Human) a.main = Pick(r, Leather);
                a.bracers = race != Race.Elf || a.bracers;
                break;
            case CityRole.Worker:
                if (race != Race.Dwarf) a.outfit = NpcOutfit.Tunic;
                a.main = Pick(r, Earthy);
                if (r.NextDouble() < 0.35 && a.headwear == NpcHeadwear.None) a.headwear = NpcHeadwear.Cap;
                break;
            case CityRole.Traveller:
                if (race == Race.Human) a.outfit = NpcOutfit.Tunic;
                a.cape = true;
                a.extra = Pick(r, new[] { C(0.30f, 0.34f, 0.24f), C(0.38f, 0.28f, 0.20f), C(0.26f, 0.28f, 0.34f) });
                if (r.NextDouble() < 0.55) a.headwear = NpcHeadwear.Hood;
                a.pouch = true;
                break;
        }
        if (mainColor.HasValue) a.main = mainColor.Value;
        if (hairColor.HasValue) { a.hairColor = hairColor.Value; a.beardColor = hairColor.Value; }
        if (a.headwear == NpcHeadwear.Helmet || a.headwear == NpcHeadwear.Hood) a.hair = a.hair == NpcHair.Bald ? NpcHair.Bald : NpcHair.Short;
        return a;
    }

    // A robed figure (the Collector of Cursed Blades, Orla the rare-metal trader).
    public static NpcAppearance Robed(Race race, Color robe, Color hair, int seed)
    {
        var a = Generate(race, CityRole.Merchant, seed, robe, hair);
        a.outfit = NpcOutfit.Robe;
        a.headwear = NpcHeadwear.None;
        a.cape = false;
        a.trim = true;
        return a;
    }

    // A customer on record: their trade decides what they wear, their id decides the rest - always the same person.
    public static NpcAppearance ForPerson(CustomerRecord c)
    {
        var a = c.Archetype;
        return Generate(c.race, RoleFor(a.title), 7919 * c.id + 17, a.outfit, c.hair);
    }

    // Someone at the table who isn't on record yet (an order's customer, a stranger from an old save).
    public static NpcAppearance ForOrder(CustomerOrder o, int seed)
        => Generate(o.race, RoleFor(o.archetype.title), seed, o.archetype.outfit, o.hair);

    public static CityRole RoleFor(string trade)
    {
        switch (trade)
        {
            case "city guard": case "shieldwarden": return CityRole.Guard;
            case "knight": return CityRole.Knight;
            case "merchant": case "rare-metal trader": return CityRole.Merchant;
            case "miner": case "stoneworker": case "cook": return CityRole.Craftsman;
            case "farmer": case "boatman": return CityRole.Worker;
            case "hunter": case "ranger": case "scout": case "mercenary": return CityRole.Traveller;
            case "fencer": case "blade dancer": return CityRole.Noble;
        }
        return CityRole.Townsfolk;
    }
}
