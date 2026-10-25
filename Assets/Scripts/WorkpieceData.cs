using UnityEngine;

public class ForgeMaterialInfo
{
    public string name;
    public Color color;
    public float windowScale; // >1 = wider good/perfect windows (easier)
    public float speedScale;  // >1 = faster pointer (harder)
    public int price;         // gold per ingot at the evening trader
    public float valueScale;  // customers pay this much more for a blade of this metal
    public float wearScale;   // durability each grindstone touch grinds away (hard metals wear less)
    public float crackChance; // chance a botched quench leaves a hairline crack
    public float maxQuality;  // no stat can go past this in this metal - soft metals simply can't hold a master's work
    public int index;         // position in ForgeMaterials.All (stock and unlocks are stored by it)
    // Its character: how far each stat can go in this metal (each <= maxQuality). Copper bends before it wears out
    // (low Durability ceiling) while holding a fine balance and edge; iron is even; steel reaches everything, at a price.
    public float durCap, balCap, sharpCap;
    public string character;  // one line for the Ready screen
    public float Cap(int stat) => stat == 0 ? durCap : stat == 1 ? balCap : sharpCap; // NpcStories.Stat* order
}

// "Small window and temperature window depends on material" - three metals, unlocked one by one (Event Trader).
// Each harder metal is a real trade: tighter timing windows, a faster pointer, a higher crack chance and a much
// higher price - in exchange for a higher quality ceiling (maxQuality), better pay and less grindstone wear.
// Copper is forgiving but tops out below a master's standard; only Steel can reach 100 - and each metal has its own
// strengths per stat (durCap/balCap/sharpCap), so the best metal for a job depends on what the job needs.
public static class ForgeMaterials
{
    public static readonly ForgeMaterialInfo[] All =
    {
        new ForgeMaterialInfo { name = "Copper", color = new Color(0.72f, 0.45f, 0.20f), windowScale = 1.1f,  speedScale = 0.95f,
                                price = 12, valueScale = 1.0f, wearScale = 1.4f, crackChance = 0.15f, maxQuality = 78f, index = 0,
                                durCap = 70f, balCap = 78f, sharpCap = 78f, character = "forgiving, but soft - it won't hold great Durability" },
        new ForgeMaterialInfo { name = "Iron",   color = new Color(0.55f, 0.55f, 0.58f), windowScale = 0.85f, speedScale = 1.05f,
                                price = 30, valueScale = 1.35f, wearScale = 1.0f, crackChance = 0.30f, maxQuality = 90f, index = 1,
                                durCap = 90f, balCap = 90f, sharpCap = 90f, character = "the honest all-rounder" },
        new ForgeMaterialInfo { name = "Steel",  color = new Color(0.72f, 0.78f, 0.85f), windowScale = 0.65f, speedScale = 1.2f,
                                price = 60, valueScale = 1.8f, wearScale = 0.6f, crackChance = 0.45f, maxQuality = 100f, index = 2,
                                durCap = 100f, balCap = 100f, sharpCap = 100f, character = "the best there is - but hard to work and quick to crack" },
    };

    public static ForgeMaterialInfo Find(string name)
    {
        foreach (var m in All)
            if (m.name == name) return m;
        return All[1];
    }
}

// Plain data holder for the item currently being forged.
// Not a MonoBehaviour on purpose: it's just numbers that the station scripts read and write.
[System.Serializable]
public class WorkpieceData
{
    // Permanent ID once the finished sword is hung on the rack (see BlacksmithHistory.RegisterForged);
    // 0 = still being forged, or a failed blade that went straight into the scrap crate.
    public int id;

    public string itemName = "Iron Sword";
    public string materialName = "Iron";

    // Sword for anything forged (or saved) before weapon types existed - JsonUtility leaves missing fields at 0.
    public WeaponType weaponType;
    public WeaponInfo Weapon => Weapons.Get(weaponType);

    // 0..1, how much of the anvil shaping work is done (drives the visual only)
    public float shapeProgress;

    // Anvil hits granted by the Furnace minigame (5..10)
    public int hits;

    // All 0..100, shown as percentages
    public float durability;
    public float balance;
    public float sharpness;

    public float weight = 1.2f;

    public bool cracked; // a botched quench left a hairline crack - the Collector loves these, nobody else does

    public WorkpieceData Clone() => (WorkpieceData)MemberwiseClone();

    // The metal's ceiling: no stat can be worked past it (see ForgeMaterialInfo.maxQuality).
    public void CapToMaterial()
    {
        var m = ForgeMaterials.Find(materialName);
        durability = Mathf.Min(durability, m.durCap);
        balance = Mathf.Min(balance, m.balCap);
        sharpness = Mathf.Min(sharpness, m.sharpCap);
    }

    // Weighted by what this kind of weapon is about (a sword counts all three equally - see WeaponInfo.Grade).
    public string Grade => Weapon.Grade(this);
}
