using UnityEngine;

// What happened to a sword after it left the forge. The story follows straight from its real stats:
// the stat(s) the job needed decide triumph vs. "it did the job", and any stat left badly neglected turns
// into a (funny) mishap - so players learn what each stat actually means from how it plays out.
// Everything here is simple thresholds on the sword's stats - no simulation, just believable consequences.
public static class SwordStories
{
    private const float TriumphMargin = 15f; // key stats beat the order by at least this much
    private const float MishapBelow = 20f;   // any stat this low goes wrong somewhere along the way
    private const float High = 80f;          // "this stat is a strength" in customer remarks
    public const float Low = 50f;            // "this stat is a weakness"

    // How far past the order the sword was on its weakest *required* stat.
    public static float Margin(CustomerOrder order, WorkpieceData s)
    {
        float margin = float.MaxValue;
        if (order.minDurability > 0f) margin = Mathf.Min(margin, s.durability - order.minDurability);
        if (order.minBalance > 0f) margin = Mathf.Min(margin, s.balance - order.minBalance);
        if (order.minSharpness > 0f) margin = Mathf.Min(margin, s.sharpness - order.minSharpness);
        return margin == float.MaxValue ? 0f : margin;
    }

    public struct Outcome
    {
        public string text;
        public int tip;
        public float reputation;
        public bool destroyed; // the sword didn't survive the story
    }

    public static Outcome Tell(SwordRecord sold, Workshop shop)
    {
        var order = sold.order;
        var s = sold.sword;
        var a = order.archetype;
        bool triumph = Margin(order, s) >= TriumphMargin;
        string mishap = Mishap(s);

        var o = new Outcome();
        string opener = $"{order.Title} is back! ";
        if (triumph)
        {
            sold.legendName = a.swordNames[Random.Range(0, a.swordNames.Length)];
            o.text = opener + string.Format(a.Line(a.triumph, s.weaponType), sold.legendName);
            o.tip = 8 + Mathf.RoundToInt(order.reward * 0.3f);
            o.reputation = 3f;
        }
        else
        {
            o.text = opener + a.Line(a.fine, s.weaponType);
            o.reputation = 1f;
        }

        if (mishap != null)
        {
            o.text += " " + mishap;
            o.tip /= 2;
            o.reputation -= 6f;
            o.destroyed = s.durability < MishapBelow; // "snapped clean in two"
        }
        else
        {
            string remark = Remark(s);
            if (remark != null) o.text += " " + remark;
            if (shop.HasMark && (triumph || Random.value < 0.5f))
            {
                o.text += $" Everyone at the tavern asked who made it - I showed them your {shop.MarkName} mark.";
                o.reputation += 1f;
            }
        }
        return o;
    }

    // A stat nobody asked for, but that was so bad it still caused trouble.
    private static string Mishap(WorkpieceData s)
    {
        if (s.durability < MishapBelow)
            return "...Mind you, on the way home it snapped clean in two when I leaned on it. Maybe a little sturdier next time?";
        if (s.balance < MishapBelow)
            return "...Though it kept twisting in my grip and I bonked myself on the nose. Twice.";
        if (s.Weapon.hasEdge && s.sharpness < MishapBelow)
            return "...Although it was so blunt I mostly used it as a club.";
        return null;
    }

    // One line about how the sword feels to use - its strongest or weakest trait, whichever stands out more.
    public static string Remark(WorkpieceData s)
    {
        string best = BestStat(s), weak = WeakestStat(s);
        if (StatValue(s, weak) < Low) return Complaint(weak) + ".";
        if (StatValue(s, best) >= High) return Praise(best, s.Weapon);
        return null;
    }

    // Why a recurring customer is back at the counter with their sword: what still works (ideally the very
    // thing they asked for), and the worn edge that needs the grindstone.
    public static string RepairRequest(SwordRecord r)
    {
        var s = r.sword;
        string noun = s.Weapon.noun;
        string opener = s.durability >= High ? $"That {noun} saved my skin more than once" : $"That {noun} has served me well enough";
        string kept = KeyStatStillGood(r);
        string keptText = kept != null ? $" - the {kept.ToLower()} is still {Adjective(kept)}" : "";
        string edge = !s.Weapon.hasEdge ? "look at the face, it's dented and the handle's gone loose"
            : s.sharpness < Low ? "the edge went dull far too fast" : "look at the edge, it's chipped and dull";
        string extra = s.balance < Low ? " And honestly, it's always felt a little awkward to swing." : "";
        return $"{opener}{keptText}, but {edge}.{extra} Could you work it back on the grindstone?";
    }

    // Logged when an owned sword wears past the repair threshold (shown in the journal).
    public static string DamageNote(SwordRecord r)
    {
        if (!r.sword.Weapon.hasEdge) return r.sword.durability < Low ? "Cracked and dented from hard use" : "Dented from hard use";
        if (r.sword.sharpness < Low) return "The edge has gone dull";
        if (r.sword.durability < Low) return "Nicked and bent from hard use";
        return "Worn from hard use";
    }

    // The stat the original order leaned on that has held up best (null if none of them are any good).
    private static string KeyStatStillGood(SwordRecord r)
    {
        var o = r.order;
        if (o == null) return null;
        string pick = null;
        float best = 60f;
        void Check(string stat, bool asked) { float v = StatValue(r.sword, stat); if (asked && v >= best) { best = v; pick = stat; } }
        Check("Durability", o.minDurability > 0f);
        Check("Balance", o.minBalance > 0f);
        Check("Sharpness", o.minSharpness > 0f);
        return pick;
    }

    // ---- stat helpers ----------------------------------------------------------------------------

    public static float StatValue(WorkpieceData s, string stat)
        => stat == "Durability" ? s.durability : stat == "Balance" ? s.balance : s.sharpness;

    // A weapon without an edge (a hammer) is never judged on Sharpness.
    public static string BestStat(WorkpieceData s)
    {
        float sharp = s.Weapon.hasEdge ? s.sharpness : float.MinValue;
        return s.durability >= s.balance && s.durability >= sharp ? "Durability" : s.balance >= sharp ? "Balance" : "Sharpness";
    }

    public static string WeakestStat(WorkpieceData s)
    {
        float sharp = s.Weapon.hasEdge ? s.sharpness : float.MaxValue;
        return s.durability <= s.balance && s.durability <= sharp ? "Durability" : s.balance <= sharp ? "Balance" : "Sharpness";
    }

    public static string Praise(string stat, WeaponInfo weapon)
    {
        if (weapon != null && !weapon.hasEdge && stat == "Balance") return "It swings true every single time.";
        return Praise(stat);
    }

    public static string Praise(string stat)
    {
        switch (stat)
        {
            case "Durability": return "It took every knock I gave it and never flinched.";
            case "Balance": return "It sits in my hand like it grew there.";
            default: return "That edge cut clean for weeks.";
        }
    }

    private static string Complaint(string stat)
    {
        switch (stat)
        {
            case "Durability": return "Though it's picked up nicks and a bend I don't like";
            case "Balance": return "Though it's always felt a little awkward to swing";
            default: return "Though the edge went dull quicker than I'd hoped";
        }
    }

    private static string Adjective(string stat) => stat == "Durability" ? "solid as a rock" : stat == "Balance" ? "perfect" : "keen";

    public static string Comparative(string stat, bool capital = false)
    {
        string w = stat == "Durability" ? "tougher" : stat == "Balance" ? "steadier" : "sharper";
        return capital ? char.ToUpper(w[0]) + w.Substring(1) : w;
    }
}
