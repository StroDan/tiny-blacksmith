using UnityEngine;

public enum PointKind { Durability, Balance }

// Anvil minigame bookkeeping (hit on time + choose your buff). Each "hit" granted by
// the furnace is one round: a red (Durability) and a yellow (Balance) point spawn
// together, the player can only take one of them, and both vanish after pointLifetime.
// A round where nothing was clicked is simply used up.
public class AnvilStation : MonoBehaviour
{
    public float pointLifetime = 1.0f;
    public float roundGap = 0.15f;

    public int TotalHits { get; private set; }
    public int Resolved { get; private set; }
    public int DurabilityPoints { get; private set; }
    public int BalancePoints { get; private set; }

    public int HitsLeft => TotalHits - Resolved;
    public float ShapeProgress => TotalHits == 0 ? 0f : (float)Resolved / TotalHits;

    public void Begin(int hits)
    {
        TotalHits = hits;
        Resolved = 0;
        DurabilityPoints = 0;
        BalancePoints = 0;
    }

    public void RecordHit(PointKind kind)
    {
        Resolved++;
        if (kind == PointKind.Durability) DurabilityPoints++;
        else BalancePoints++;
    }

    public void RecordMiss()
    {
        Resolved++;
    }
}
