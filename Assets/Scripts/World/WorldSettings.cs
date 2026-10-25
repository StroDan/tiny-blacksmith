using UnityEngine;

// Every number the living world runs on (WorldDirector): who comes to the smithy and how often, city events, returns,
// referrals, special commissions, repetition penalties and the guarantees that keep pacing safe. Nothing in the director
// hardcodes these.
//
// Optional asset: Assets/Resources/WorldSettings.asset (Tiny Blacksmith > World > Create World Settings Asset). Without
// it the code defaults below are used - and NOTE that once the asset exists, it overrides any later change of a default
// here (edit the asset, or delete it to go back to the defaults).
[CreateAssetMenu(menuName = "Tiny Blacksmith/World Settings", fileName = "WorldSettings")]
public class WorldSettings : ScriptableObject
{
    [Header("Progression windows (content pools open by stage, never on one exact day)")]
    [Tooltip("Elves and dwarves can't turn up before this day (the first day is humans and simple work).")]
    public int otherRacesFromDay = 2;
    [Tooltip("Each other race is guaranteed to have shown up by a day in this range (rolled per save, so the order they appear in varies).")]
    public Vector2Int raceGuaranteeDays = new Vector2Int(3, 5);
    [Tooltip("Weapon types that aren't in the starting pool open on a day in this range (rolled per save, per weapon).")]
    public Vector2Int weaponIntroDays = new Vector2Int(2, 5);
    [Tooltip("The Event Trader's first visit lands in this day window (and needs a completed order first, unless the window's end has come).")]
    public Vector2Int firstEventTraderDays = new Vector2Int(3, 5);
    [Tooltip("Reputation from which the medium pool (guards, merchants, captains...) opens up.")]
    public float mediumRep = 45f;
    [Tooltip("Reputation from which elite customers (knights, masters, castle folk) come.")]
    public float highRep = 65f;

    [Header("Repetition (avoid monotony, never forbid)")]
    [Tooltip("Weight multiplier per recent repeat of the same weapon / race / trade (0.55 = each repeat almost halves it).")]
    [Range(0.1f, 1f)] public float repetitionPenalty = 0.55f;
    [Tooltip("How many recent orders the repetition penalty remembers.")]
    public int recentMemory = 4;
    [Tooltip("A weapon not asked for in this many orders gets a small novelty bonus.")]
    public int noveltyAfter = 5;
    public float noveltyBonus = 1.5f;

    [Header("Race standing (the smith's name among each folk)")]
    public float standingDeclined = -0.08f;
    public float standingWalkedOff = -0.1f;
    public float standingExcellent = 0.07f;
    public float standingGood = 0.04f;
    public float standingGrudging = -0.03f;
    public float standingRefusedWeapon = -0.06f;
    [Tooltip("Standing -1..1 scales how often that folk comes: weight x (1 + standing x this).")]
    public float standingInfluence = 0.6f;

    [Header("City events")]
    [Tooltip("Chance a new event starts on a day with none running (after the cooldown between events).")]
    [Range(0f, 1f)] public float eventChance = 0.38f;
    [Tooltip("Quiet days between the end of one event and the start of the next.")]
    public int eventGapDays = 1;
    [Tooltip("The first city event is guaranteed by this day.")]
    public int firstEventGuaranteeDay = 4;
    [Tooltip("Events can't start before this day.")]
    public int firstEventDay = 2;

    [Header("Returning weapons")]
    [Tooltip("The very first weapon sold always comes back (the player should see that payoff early), within these days.")]
    public Vector2Int firstReturnDays = new Vector2Int(3, 6);

    [Header("Word of mouth")]
    [Range(0f, 1f)] public float referralChanceExcellent = 0.45f;
    [Range(0f, 1f)] public float referralChanceGood = 0.2f;
    [Range(0f, 1f)] public float badWordChance = 0.5f;
    [Tooltip("A referred customer turns up this many days later.")]
    public Vector2Int referralDelay = new Vector2Int(2, 5);
    public int maxPendingReferrals = 3;

    [Header("Special commissions (rare, memorable)")]
    public int specialFromDay = 6;
    public float specialMinRep = 45f;
    [Range(0f, 1f)] public float specialChance = 0.16f;
    [Tooltip("Days after a special commission before another can come.")]
    public int specialCooldown = 5;
    [Tooltip("If none has come yet (and reputation allows), one is guaranteed by this day.")]
    public int specialGuaranteeDay = 11;
    public float specialPayMultiplier = 1.8f;
    public float specialRepMultiplier = 1.6f;

    [Header("Orders")]
    [Tooltip("Needs never go closer than this to the best reachable value of that stat in the metals the smith can work.")]
    public float feasibilityMargin = 6f;
    [Tooltip("In a two-stat order the less important stat is asked this much lower (the customer's priorities).")]
    public float secondaryStatDrop = 14f;
    [Tooltip("Chance a modest customer wants a plain all-round weapon (every stat a little, nothing extreme, low pay).")]
    [Range(0f, 1f)] public float allRoundOrderChance = 0.18f;

    [Header("Notable weapons")]
    [Tooltip("A weapon whose every relevant stat is at least this is notable from the day it's forged.")]
    public float notableQuality = 88f;
    public int notableRepairs = 2;

    [Header("Economy")]
    [Tooltip("How much a city event can swing the supplier's daily stock (multiplier range it clamps to).")]
    public Vector2 supplySwing = new Vector2(0.5f, 2f);

    private static WorldSettings current;

    public static WorldSettings Current
    {
        get
        {
            if (current == null) current = Resources.Load<WorldSettings>("WorldSettings");
            if (current == null) { current = CreateInstance<WorldSettings>(); current.name = "WorldSettings (defaults)"; }
            return current;
        }
    }
}
