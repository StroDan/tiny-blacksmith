using UnityEngine;

public enum Rating { Bad, Good, Perfect }

// Shared "press and hold" bar logic used by the Furnace, Water Barrel and Grindstone.
// The bar has five zones (bad - good - perfect - good - bad). While the player
// holds the input the pointer moves up (it never goes back down); releasing locks
// the pointer and the zone it landed in decides the Rating.
public class TimingBar
{
    // Furnace / Water Barrel layout from the design doc: 50% bad, 15% good, 5% perfect, 10% good, 20% bad.
    public static readonly float[] StandardWidths = { 0.50f, 0.15f, 0.05f, 0.10f, 0.20f };

    public readonly float[] widths = new float[5];
    public float pointer;
    public float speed = 0.25f; // bar lengths per second while held

    public bool Finished { get; private set; }
    public Rating Result { get; private set; }

    private bool started;

    public void Reset(float newSpeed)
    {
        pointer = 0f;
        speed = newSpeed;
        started = false;
        Finished = false;
    }

    // windowScale widens (>1) or narrows (<1) the good/perfect zones; the bad
    // zones soak up the difference in their original 50:20 proportion.
    public void SetStandardZones(float windowScale)
    {
        widths[1] = StandardWidths[1] * windowScale;
        widths[2] = StandardWidths[2] * windowScale;
        widths[3] = StandardWidths[3] * windowScale;
        float rest = Mathf.Max(0.1f, 1f - widths[1] - widths[2] - widths[3]);
        widths[0] = rest * (0.50f / 0.70f);
        widths[4] = rest * (0.20f / 0.70f);
    }

    public void SetZones(float w0, float w1, float w2, float w3, float w4)
    {
        widths[0] = w0; widths[1] = w1; widths[2] = w2; widths[3] = w3; widths[4] = w4;
    }

    public Rating Evaluate(float t)
    {
        float total = 0f;
        for (int i = 0; i < widths.Length; i++) total += widths[i];

        float edge = 0f;
        for (int i = 0; i < widths.Length; i++)
        {
            edge += widths[i] / total;
            if (t <= edge)
            {
                if (i == 2) return Rating.Perfect;
                if (i == 1 || i == 3) return Rating.Good;
                return Rating.Bad;
            }
        }
        return Rating.Bad;
    }

    // Returns true on the frame the pointer gets locked (released, or ran off the end).
    public bool Tick(bool holding, float deltaTime)
    {
        if (Finished) return false;

        if (holding)
        {
            started = true;
            pointer = Mathf.Min(1f, pointer + speed * deltaTime);
            if (pointer >= 1f) return Lock();
            return false;
        }

        if (started) return Lock();
        return false;
    }

    private bool Lock()
    {
        Finished = true;
        Result = Evaluate(pointer);
        return true;
    }
}
