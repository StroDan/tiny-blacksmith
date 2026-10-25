using UnityEngine;

// One loose handle part (Guard, Grip or Pommel) plus its transparent "ghost" target on the tang.
public class HandlePart
{
    public string name;
    public GameObject go;
    public GameObject ghost;
    public Material ghostMaterial; // shared by all submeshes of the ghost, so one colour change tints the whole outline
    public Vector3 targetPos;
    public float targetYaw;
    public bool rotatable;
    public bool locked;

    public Vector3 baseScale;
    public Vector3 localPos;      // pose relative to the blade, kept so the handle follows it on the result screen
    public Quaternion localRot;
}

// Workbench minigame (assembly drag & snap). The player drags the Guard, Grip and Pommel onto the
// ghost outline of the finished handle; how closely each one matches decides the bonus/penalty.
// Match quality is shown on the same five-zone bar as the other stations (bad - good - perfect - good - bad),
// but the pointer reflects fit instead of timing.
//   Perfect: +Balance and a smaller +Durability.   Good: smaller +Balance only.
//   Bad: -Balance, and -Durability as well when the fit is really far off.
public class WorkbenchStation : MonoBehaviour
{
    // Everything on the bench - the sword, the parts and the top-down camera - is laid out relative to
    // this frame, which follows the GameObject's own rotation directly (hand-rotate it in the Editor to
    // fit the room and every dependent position/camera follows automatically - no separate yaw field to
    // keep in sync).
    public Quaternion Frame => transform.rotation;

    public float wheelStep = 6f;          // degrees per mouse-wheel notch
    public float keyTurnSpeed = 240f;     // degrees per second while Q/E is held
    public float straightenSnap = 3f;     // within this many degrees of straight the part snaps exactly into line (keep below wheelStep)

    // A part dropped further than this from its ghost is not attached to the sword at all:
    // the sword immediately loses ALL its Durability and Balance.
    public float attachRadius = 0.35f;

    public float positionTolerance = 0.15f; // metres of offset that count as "fully off"
    public float angleTolerance = 30f;      // degrees of rotation that count as "fully off"
    public float extremeError = 1.2f;       // error at which a bad fit also hurts Durability

    // Fit rewards (renamed in the balance pass so the old scene values don't override them). A handle can
    // polish a weapon, not make it: three perfect fits add about +24 Balance.
    public float perfectBalanceGain = 8f;
    public float perfectDurabilityGain = 3f;
    public float goodBalanceGainMax = 5f; // good fit right at the perfect edge
    public float goodBalanceGainMin = 2f; // good fit right at the bad edge
    public float badBalanceLoss = 8f;
    public float extremeDurabilityLoss = 4f;

    // bad | good | perfect | good | bad, centred on the middle of the bar (pointer 0.5 = exact match).
    public readonly float[] zoneWidths = { 0.15f, 0.25f, 0.20f, 0.25f, 0.15f };

    private readonly TimingBar rater = new TimingBar();

    private void Awake()
    {
        rater.SetZones(zoneWidths[0], zoneWidths[1], zoneWidths[2], zoneWidths[3], zoneWidths[4]);
    }

    // error 0 = exact match, 1 = as far off as the bar can show.
    public float Error(float positionOffset, float angleOffset)
    {
        float p = positionOffset / positionTolerance;
        float a = angleOffset / angleTolerance;
        return Mathf.Sqrt(p * p + a * a);
    }

    // Where the pointer sits on the bar for a given error and side (left/right of the ghost).
    public float PointerFor(float error, float side)
    {
        return 0.5f + Mathf.Sign(side) * Mathf.Min(error, 1f) * 0.5f;
    }

    public Rating RatingFor(float error)
    {
        rater.SetZones(zoneWidths[0], zoneWidths[1], zoneWidths[2], zoneWidths[3], zoneWidths[4]);
        return rater.Evaluate(PointerFor(error, 1f));
    }

    // Applies the result to the item and returns a short description for the player.
    public string Apply(WorkpieceData item, Rating rating, float error)
    {
        switch (rating)
        {
            case Rating.Perfect:
                item.balance = Mathf.Clamp(item.balance + perfectBalanceGain, 0f, 100f);
                item.durability = Mathf.Clamp(item.durability + perfectDurabilityGain, 0f, 100f);
                return $"Balance +{perfectBalanceGain:0}, Durability +{perfectDurabilityGain:0}";
            case Rating.Good:
            {
                // The closer the fit, the more Balance: scales between goodBalanceGainMax and goodBalanceGainMin.
                float gain = Mathf.Round(Mathf.Lerp(goodBalanceGainMax, goodBalanceGainMin, Mathf.InverseLerp(0.2f, 0.7f, error)));
                item.balance = Mathf.Clamp(item.balance + gain, 0f, 100f);
                return $"Balance +{gain:0}";
            }
            default:
                item.balance = Mathf.Clamp(item.balance - badBalanceLoss, 0f, 100f);
                if (error >= extremeError)
                {
                    item.durability = Mathf.Clamp(item.durability - extremeDurabilityLoss, 0f, 100f);
                    return $"Balance -{badBalanceLoss:0}, Durability -{extremeDurabilityLoss:0}";
                }
                return $"Balance -{badBalanceLoss:0}";
        }
    }
}
