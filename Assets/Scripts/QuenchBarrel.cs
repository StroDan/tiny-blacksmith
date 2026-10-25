using UnityEngine;

// Water barrel minigame (press and hold). The metal hangs over the barrel; while
// Space is held it is lowered in and the pointer rises, releasing takes it out.
//   Perfect -> +10 Durability, Good -> +3, Bad -> -12 and a chance to crack the blade.
public class QuenchBarrel : MonoBehaviour
{
    public float fillTime = 1.8f;
    // (Renamed from perfectBonus/badPenalty in the balance pass so the old scene values don't override them.)
    public float perfectDurability = 10f;
    public float goodDurability = 3f;
    public float badDurability = 12f;

    public readonly TimingBar bar = new TimingBar();

    public void Begin(ForgeMaterialInfo material)
    {
        bar.SetStandardZones(material.windowScale);
        bar.Reset(material.speedScale / fillTime);
    }

    // Returns true if a botched quench cracked the blade (the shock of cold water on badly timed metal) -
    // harder metals are more likely to crack. A cracked blade loses half its Durability and no customer
    // will take it, but the Collector pays extra for exactly these.
    public bool Apply(WorkpieceData item, Rating rating, ForgeMaterialInfo material)
    {
        switch (rating)
        {
            case Rating.Perfect:
                item.durability = Mathf.Clamp(item.durability + perfectDurability, 0f, 100f);
                break;
            case Rating.Good:
                item.durability = Mathf.Clamp(item.durability + goodDurability, 0f, 100f);
                break;
            case Rating.Bad:
                item.durability = Mathf.Clamp(item.durability - badDurability, 0f, 100f);
                if (Random.value < material.crackChance)
                {
                    item.cracked = true;
                    item.durability *= 0.5f;
                    return true;
                }
                break;
        }
        return false;
    }
}
