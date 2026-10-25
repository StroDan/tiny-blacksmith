using UnityEngine;

// Grindstone minigame (touch & pull). Hold to press the blade to the stone, the pointer
// moves along the bar - slowly, sharpening isn't a quick motion - release to lock the result.
// There's no limit on touches, but every new touch is harder: the good zones shrink noticeably,
// the perfect zone barely shrinks at all, the pointer speeds up a little, and the good-perfect-good
// cluster sits at a new, random spot between the middle and the far end of the bar every time.
//   Perfect: +25 Sharpness, Good: +12-16, Bad: -8-12 (gains scaled by the weapon type's sharpGain - daggers take an edge more readily than spears).
//   The sword only gets maxTouches touches, and sharpening never changes Balance (that comes from the handle).
//   Every touch also wears 3/5/9 Durability away (perfect/good/bad), scaled by the metal's wearScale.
//   (All values are stat points.)
public class Grindstone : MonoBehaviour
{
    public int maxTouches = 4;
    public float wheelRadius = 0.45f;    // the blade rests on the top edge of the standing wheel
    public float baseSpeed = 0.30f;      // bar lengths per second on the first touch
    public float speedGrowth = 1.08f;    // per extra touch: only a little faster each time
    public float perfectShrink = 0.97f;  // per extra touch: the perfect window is almost untouched
    public float goodShrink = 0.78f;     // per extra touch: the good windows shrink a lot

    // Sharpness per touch (balance pass): a perfect touch is valuable, but a keen edge takes several of them.
    public const float PerfectSharpness = 25f;
    public static readonly Vector2Int GoodSharpness = new Vector2Int(12, 16);
    public static readonly Vector2Int BadSharpness = new Vector2Int(8, 12);

    public readonly TimingBar bar = new TimingBar();

    public int Touches { get; private set; }

    public void Begin()
    {
        Touches = 0;
    }

    // Sets up the bar for the next touch based on how many touches were already made.
    public void PrepareTouch(ForgeMaterialInfo material)
    {
        float s = material.windowScale;
        float basePerfect = 0.05f * s;
        float baseGood = 0.15f * s;

        float perfect = Mathf.Max(0.01f, basePerfect * Mathf.Pow(perfectShrink, Touches));
        float good = Mathf.Max(0.015f, baseGood * Mathf.Pow(goodShrink, Touches));
        float cluster = good * 2f + perfect;

        // The good-perfect-good cluster can start anywhere from the middle of the bar up to wherever
        // still leaves it enough room to fit before the end - a new draw every touch.
        float latestStart = Mathf.Max(0.5f, 1f - cluster);
        float start = Random.Range(0.5f, latestStart);

        bar.SetZones(start, good, perfect, good, 1f - start - cluster);
        bar.Reset(baseSpeed * material.speedScale * Mathf.Pow(speedGrowth, Touches));
    }

    // Applies the locked touch to the item and returns a short description for the player.
    // Every touch also grinds a little metal away (Durability), a clean one less than a botched one, and
    // hard metals less than soft ones - so "one more touch" is always a real trade, not a free roll.
    public string ApplyTouch(WorkpieceData item, Rating rating, ForgeMaterialInfo material)
    {
        Touches++;
        float baseWear = rating == Rating.Perfect ? 3f : rating == Rating.Good ? 5f : 9f;
        int wear = Mathf.Max(1, Mathf.RoundToInt(baseWear * material.wearScale));
        item.durability = Mathf.Max(0f, item.durability - wear);
        string worn = $"   Durability -{wear}";

        // No edge to grind (a hammer in for repair): the stone just dresses its striking face true again.
        var weapon = item.Weapon;
        if (!weapon.hasEdge)
            return (rating == Rating.Perfect ? "PERFECT!  The face is dressed clean and true" : rating == Rating.Good ? "Good.  The face is smoother" : "Bad!  You gouged the face") + worn;

        switch (rating)
        {
            case Rating.Perfect:
            {
                int gain = Mathf.RoundToInt(PerfectSharpness * weapon.sharpGain);
                item.sharpness = Mathf.Min(100f, item.sharpness + gain);
                return $"PERFECT!  Sharpness +{gain}" + worn;
            }
            case Rating.Good:
            {
                int gain = Mathf.RoundToInt(Random.Range(GoodSharpness.x, GoodSharpness.y + 1) * weapon.sharpGain);
                item.sharpness = Mathf.Min(100f, item.sharpness + gain);
                return $"Good.  Sharpness +{gain}" + worn;
            }
            default:
            {
                int loss = Random.Range(BadSharpness.x, BadSharpness.y + 1);
                item.sharpness = Mathf.Max(0f, item.sharpness - loss);
                return $"Bad!  Sharpness -{loss}" + worn;
            }
        }
    }
}
