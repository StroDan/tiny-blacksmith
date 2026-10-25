using UnityEngine;

// Furnace minigame: hold the RIGHT mouse button to raise a catcher rectangle (release to let it fall) and
// keep the independently, continuously bouncing fire inside it. The metal's heating progress climbs while
// the fire is caught and drains while it isn't; reaching 1 wins, reaching 0 loses - either way forging
// continues (same as the old bar's worst case never aborted the attempt), just with fewer anvil hits.
public class ForgeStation : MonoBehaviour
{
    // Anvil rounds granted by the heat game. (Renamed from maxHits/minHits in the balance pass, so the values
    // saved in the scene don't override these.) More rounds than before, but every one now has to be split
    // between Durability and Balance - a great furnace buys you options, not a finished weapon.
    public int perfectHits = 15;
    public int badHits = 7;

    public readonly HeatGame game = new HeatGame();

    public void Begin(ForgeMaterialInfo material) => game.Begin(material);

    public int HitsFor(Rating rating)
    {
        switch (rating)
        {
            case Rating.Perfect: return perfectHits;
            case Rating.Good: return Random.Range(11, 13); // 11 or 12
            default: return badHits;
        }
    }
}

// Plain state/logic class (no MonoBehaviour) - same spirit as TimingBar: ForgeGameManager drives it every
// frame with Tick() and reads its fields for both gameplay and the two vertical bars in ForgeUI.
public class HeatGame
{
    // Positions are normalised 0..1 within the bar (0 = bottom, 1 = top).
    public float firePos;
    public float catcherPos;
    public float progress;
    public float catcherHalfHeight = 0.11f;
    public bool Caught { get; private set; }

    public bool Finished { get; private set; }
    public bool Won { get; private set; }
    public Rating Result { get; private set; }

    private float fireSpeed;
    private float fireDir;
    private float speedScale;
    private float catcherVel;
    private float caughtTime;
    private float totalTime;

    // The fire moves for a while, freezes in place for a second or two, then carries on - so its motion
    // reads as "drifts, pauses, drifts again" rather than one continuous bounce.
    private bool firePaused;
    private float fireStateTimer;

    private const float RiseSpeed = 0.95f;    // catcher's target speed (bar-lengths/sec) while RMB is held
    private const float FallSpeed = 1.15f;    // ...and while released (a little faster - it just drops)
    private const float CatcherAccel = 6f;    // how fast the catcher's actual speed ramps to that target, so it eases rather than teleporting
    private const float ProgressGain = 0.40f; // per second while the fire is caught
    private const float ProgressLoss = 0.16f; // per second while it isn't - a gradual drain, not a snap back to zero
    private const float StartProgress = 0.20f; // a little buffer so a slow start isn't an instant loss

    public void Begin(ForgeMaterialInfo material)
    {
        speedScale = material.speedScale;
        firePos = Random.Range(0.35f, 0.65f);
        fireDir = Random.value < 0.5f ? -1f : 1f;
        fireSpeed = RandomFireSpeed();
        firePaused = false;
        fireStateTimer = RandomMoveDuration();
        catcherPos = 0.5f;
        catcherVel = 0f;
        catcherHalfHeight = 0.12f * material.windowScale; // easier metals (windowScale > 1) get a more forgiving catcher
        progress = StartProgress;
        caughtTime = 0f;
        totalTime = 0f;
        Caught = false;
        Finished = false;
        Won = false;
    }

    // A little randomised on every bounce so the fire's rhythm isn't perfectly predictable, but still fair
    // in the moment (speed only changes when it's at an extreme, never mid-flight).
    private float RandomFireSpeed() => Random.Range(0.5f, 0.85f) * speedScale;
    private static float RandomMoveDuration() => Random.Range(0.9f, 2.4f);
    private static float RandomPauseDuration() => Random.Range(1f, 2f);

    // Returns true on the frame the game is won or lost.
    public bool Tick(bool held, float dt)
    {
        if (Finished) return false;

        fireStateTimer -= dt;
        if (firePaused)
        {
            if (fireStateTimer <= 0f) { firePaused = false; fireStateTimer = RandomMoveDuration(); }
        }
        else
        {
            firePos += fireDir * fireSpeed * dt;
            if (firePos >= 1f) { firePos = 1f; fireDir = -1f; fireSpeed = RandomFireSpeed(); }
            else if (firePos <= 0f) { firePos = 0f; fireDir = 1f; fireSpeed = RandomFireSpeed(); }
            if (fireStateTimer <= 0f) { firePaused = true; fireStateTimer = RandomPauseDuration(); }
        }

        float targetVel = held ? RiseSpeed : -FallSpeed;
        catcherVel = Mathf.MoveTowards(catcherVel, targetVel, CatcherAccel * dt);
        catcherPos = Mathf.Clamp(catcherPos + catcherVel * dt, catcherHalfHeight, 1f - catcherHalfHeight);

        Caught = Mathf.Abs(firePos - catcherPos) <= catcherHalfHeight;
        progress = Mathf.Clamp01(progress + (Caught ? ProgressGain : -ProgressLoss) * dt);

        totalTime += dt;
        if (Caught) caughtTime += dt;

        if (progress >= 1f) return Lock(true);
        if (progress <= 0f) return Lock(false);
        return false;
    }

    private bool Lock(bool won)
    {
        Finished = true;
        Won = won;
        // How steady the tracking was overall decides Perfect vs. Good on a win; losing is always Bad -
        // reuses the same three-tier Rating/HitsFor the rest of the game already understands.
        float quality = totalTime > 0f ? caughtTime / totalTime : 0f;
        Result = !won ? Rating.Bad : (quality >= 0.70f ? Rating.Perfect : Rating.Good);
        return true;
    }
}
