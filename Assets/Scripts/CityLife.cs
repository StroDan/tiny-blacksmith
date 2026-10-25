using System.Collections.Generic;
using UnityEngine;

// Who a townsperson is - mostly how they look and where they like to go (no gameplay of their own).
public enum CityRole { Townsfolk, Merchant, Guard, Craftsman, Traveller, Worker, Knight, Noble }

// The townsfolk: a small crowd that lives on the city's street graph (CityNavNode, built by VillageBuilder) while the
// smith works. Each walker comes out of a door (or in along a road from out of town), walks to somewhere with a purpose
// - the market, the fountain, a shop, the tavern, the smithy's front, a bench, the riverbank, a bridge rail, the
// watermill - does what one does there (browses, chats, sits, watches the water or the wheel), and then goes on
// somewhere else or home. Stall keepers mind the market stalls through the day; guards walk the streets in pairs and
// stand at the gates, day and night. Now and then people go about in twos and threes (friends, a merchant and a
// customer, workers, a knight and a companion). Roles (townsfolk, merchants, guards, craftsmen, travellers, workers,
// knights) change their clothes, what they carry and where they like to go.
// How many are out, and where they go, follows DayCycle: people leave home and the workers start in the morning, the
// streets are busiest by day, taverns fill and people head home in the evening, and at night the city is quiet but
// not dead - guards, a few tavern-goers, the odd traveller. Rain and snow keep people in.
//
// Nobody pops in or out where the player could see it: walkers only appear or disappear at a spot the seat's camera
// can't see (outside its view, hidden behind a building - CityOccluders - or very far away). A walker who reaches a
// door in plain view waits on the doorstep until the player looks away, or goes on somewhere else.
//
// Built at runtime by ForgeGameManager (like MeadowLife / DayCycle); runs on its own every frame, whatever the smith
// is doing. The walkers are pooled, never destroyed: each is one NpcBody (a skinned low-poly human, elf or dwarf on the
// shared rig) re-dressed as someone new every time it comes out; this Update moves them along the streets and tells
// the body what to do (walk at the matching pace, carry, patrol, chat, browse, sit down and stand up).
public class CityLife : MonoBehaviour
{
    public const int MaxWalkers = 26;
    private const float FarAway = 95f;       // beyond this a walker is a speck in the haze - fine to come and go
    private const float DoorPopDistance = 55f; // going in through a door is natural past this even in view
    private const float BenchSeatTop = 0.485f; // VillageBuilder.Bench
    private const float StandUpSeconds = 0.9f; // the Stand_Up clip
    private const float GlideSpeed = 1.1f;     // a last short step into place

    // Who is walking past wearing one of your weapons (null otherwise) - the Main Room shows a line about it.
    public System.Func<SwordRecord> pickPasserby;
    // Someone the smith knows who is out in the city right now (a customer whose order is in the book, going about
    // their day) - they walk the streets in their own clothes. Null = nobody.
    public System.Func<CustomerRecord> pickResident;
    public readonly Dictionary<int, int> peopleSeen = new Dictionary<int, int>(); // customer id -> times they came out
    private float residentCooldown = 20f;

    private DayCycle clock;
    private MeadowLife weather;
    private Shader shader;
    private CityOccluders occluders;
    private readonly List<CityNavNode> nodes = new List<CityNavNode>();
    private readonly List<CityNavNode> origins = new List<CityNavNode>();   // doors + edges
    private readonly List<CityNavNode> spots = new List<CityNavNode>();
    private readonly List<CityNavNode> stalls = new List<CityNavNode>();
    private readonly List<CityNavNode> guardOrigins = new List<CityNavNode>(); // where the watch comes on duty from

    private enum State { Walk, Linger, Sit, Leave }

    private class Walker
    {
        public GameObject go;
        public Transform t;
        public NpcBody body;
        public GameObject prop, prop2, sword;
        public WeaponType? swordType;
        public Material swordBlade;
        public Race race;
        public CityRole role;
        public bool active, keeper, traveller;
        public SwordRecord carries;
        public CustomerRecord person;  // a known customer (their own look), or null for an anonymous townsperson
        public State state;
        public readonly List<CityNavNode> route = new List<CityNavNode>();
        public int next;
        public Vector3 segFrom, segTo;
        public float segT, segLen, speed, side, timer, phase, recheck, groundY, delay;
        public CityNavNode at;
        public Quaternion facing;
        public Walker leader;          // walking with someone (a pair or a small group) - follows their lead
        public bool enterAfter;        // lingering outside a tavern door, going in afterwards
        public float seatSide;
        public bool hasProp, hasProp2; // carrying something (put down while sitting)
        public Vector3 glideTo;        // a last short step while lingering or sitting down (never a jump)
        public bool gliding;
        public string propKind, prop2Kind;
        public float rising, riseFromY; // standing up from a bench: back onto their feet before walking off
        public float animTimer;         // when to look again for someone to chat with
    }

    private readonly List<Walker> walkers = new List<Walker>();
    private float spawnTimer, passerbyCooldown = 40f;
    private readonly Plane[] planes = new Plane[6];
    private int planesFrame = -1;
    private Camera cam;

    // Stats for tests / the audit.
    public int ActiveCount { get; private set; }
    public int Spawned { get; private set; }
    public int Removed { get; private set; }
    public int RemovedInView { get; private set; } // must stay 0 near the seat - see Seen()
    public int GroupsFormed { get; private set; }
    public IEnumerable<Vector3> WalkerPositions { get { foreach (var w in walkers) if (w.active) yield return w.t.position; } }
    public int NodeCount => nodes.Count;
    public readonly Dictionary<string, int> visits = new Dictionary<string, int>(); // place -> arrivals
    public readonly Dictionary<string, int> activities = new Dictionary<string, int>(); // "sit", "tavern", "river", ... -> times begun
    public readonly Dictionary<CityRole, int> rolesSeen = new Dictionary<CityRole, int>();
    public int Sitting { get { int n = 0; foreach (var w in walkers) if (w.active && w.state == State.Sit) n++; return n; } }
    public int GuardsOut { get { int n = 0; foreach (var w in walkers) if (w.active && w.role == CityRole.Guard) n++; return n; } }

    public static CityLife Create(Shader shader, DayCycle clock, MeadowLife weather)
    {
        var life = new GameObject("CityLife").AddComponent<CityLife>();
        life.shader = shader;
        life.clock = clock;
        life.weather = weather;
        life.Collect();
        return life;
    }

    private void Collect()
    {
        nodes.AddRange(FindObjectsByType<CityNavNode>(FindObjectsSortMode.None));
        occluders = FindAnyObjectByType<CityOccluders>();
        foreach (var n in nodes)
        {
            if (n.links.Count == 0) continue;
            if (n.kind == CityNodeKind.Door || n.kind == CityNodeKind.Edge) origins.Add(n);
            if (n.kind == CityNodeKind.Spot) spots.Add(n);
            if (n.kind == CityNodeKind.Spot && n.place == "market") stalls.Add(n);
            if ((n.kind == CityNodeKind.Edge && (n.place == "gate" || n.place == "city")) || (n.kind == CityNodeKind.Door && n.place == "hall")) guardOrigins.Add(n);
        }
        for (int i = 0; i < MaxWalkers; i++) walkers.Add(NewWalker(i));
        spawnTimer = 0.5f;
    }

    private Walker NewWalker(int i)
    {
        var go = new GameObject("Townsfolk" + i);
        go.transform.SetParent(transform, false);
        var w = new Walker { go = go, t = go.transform };
        go.SetActive(false);
        return w;
    }

    // ---- the day's rhythm -----------------------------------------------------------------------------------

    private float Hour => clock != null ? clock.Hour : 12f;

    // How many people are out and about now (the watch comes on top - see KeepWatch).
    private int Desired()
    {
        float h = Hour, d;
        // (GameTime's hour, as shown - it eases after the clock, so a jump of work time empties or fills the streets
        // gradually, never all at once.) Waking 6-8, busy from 8, busiest 12-17, home and to the taverns 17-20, quiet after.
        if (h < 5.5f) d = 2f;
        else if (h < 8f) d = Mathf.Lerp(2f, 10f, (h - 5.5f) / 2.5f);
        else if (h < 10f) d = Mathf.Lerp(10f, 15f, (h - 8f) / 2f);
        else if (h < 12f) d = Mathf.Lerp(15f, 17f, (h - 10f) / 2f);
        else if (h < 17f) d = 17f;
        else if (h < 20f) d = Mathf.Lerp(17f, 7f, (h - 17f) / 3f);
        else if (h < 22f) d = Mathf.Lerp(7f, 4f, (h - 20f) / 2f);
        else d = 3.5f;
        string wx = weather != null ? weather.Weather : "clear";
        if (wx == "rain") d *= 0.55f; else if (wx == "snow") d *= 0.7f;
        d *= crowdScale; // a festival fills the streets, bandit news empties them
        return Mathf.Clamp(Mathf.RoundToInt(d), 1, MaxWalkers - 4);
    }

    // Morning, day, evening, night - judged per decision with a little personal offset (see Decide), so people don't all
    // switch from shopping to the tavern on the same stroke of the clock.
    private float jitter;
    private float PhaseHour => Hour + jitter;
    private int Phase => PhaseHour < 5f || PhaseHour >= 21f ? 3 : PhaseHour < 11f ? 0 : PhaseHour < 17f ? 1 : 2;
    private void Decide() => jitter = Random.Range(-0.8f, 0.8f);

    private float OriginWeight(CityNavNode n)
    {
        switch (Phase)
        {
            case 0: return W(n, home: 3f, shop: 0.8f, tavern: 0.3f, workshop: 1.2f, hall: 0.3f, church: 0.2f, edge: 1.2f);
            case 1: return W(n, home: 1.2f, shop: 1.2f, tavern: 0.5f, workshop: 0.8f, hall: 0.5f, church: 0.3f, edge: 1.5f);
            case 2: return W(n, home: 0.6f, shop: 1.5f, tavern: 0.3f, workshop: 1f, hall: 0.6f, church: 0.4f, edge: 0.6f);
            default: return W(n, home: 0.3f, shop: 0.1f, tavern: 2f, workshop: 0.1f, hall: 0f, church: 0f, edge: 0.5f);
        }
    }

    // Where someone goes next: by the time of day, then nudged by who they are.
    private float DestinationWeight(CityNavNode n, CityRole role)
    {
        float w;
        switch (Phase)
        {
            case 0: w = W(n, home: 0.6f, shop: 2.5f, tavern: 0.2f, workshop: 1.5f, hall: 1f, church: 1.2f, edge: 0.8f, market: 3f, plaza: 2f, well: 1.2f, smithy: 0.8f, bench: 0.5f, river: 0.4f, bridge: 0.4f, mill: 1.0f); break;
            case 1: w = W(n, home: 1f, shop: 2.5f, tavern: 0.8f, workshop: 1f, hall: 0.8f, church: 0.5f, edge: 1f, market: 3f, plaza: 2.5f, well: 1f, smithy: 1f, bench: 1.3f, river: 1.0f, bridge: 0.9f, mill: 0.5f); break;
            case 2: w = W(n, home: 3f, shop: 0.3f, tavern: 3.5f, workshop: 0.2f, hall: 0.2f, church: 0.6f, edge: 0.6f, market: 0.5f, plaza: 1f, well: 0.5f, smithy: 0.3f, bench: 1.0f, river: 0.8f, bridge: 0.8f, mill: 0.2f); break;
            default: w = W(n, home: 3f, shop: 0f, tavern: 2f, workshop: 0f, hall: 0f, church: 0f, edge: 0.3f, market: 0f, plaza: 0.2f, well: 0f, smithy: 0f, bench: 0.15f, river: 0.2f, bridge: 0.3f, mill: 0f); break;
        }
        string p = n.place;
        switch (role)
        {
            case CityRole.Worker:
            case CityRole.Craftsman:
                if (p == "mill") w *= 2.5f; else if (p == "workshop" || p == "barn") w *= 2f; else if (p == "bench" || p == "plaza") w *= 0.5f;
                break;
            case CityRole.Merchant:
                if (p == "market" || p == "shop") w *= 2f; else if (p == "mill") w *= 1.5f;
                break;
            case CityRole.Traveller:
                if (n.kind == CityNodeKind.Edge || p == "well") w *= 2f; else if (p == "tavern") w *= 2.5f; else if (p == "home") w *= 0.1f;
                break;
            case CityRole.Noble:
                if (p == "plaza" || p == "church" || p == "hall" || p == "market" || p == "bridge") w *= 2f; else if (p == "mill" || p == "workshop" || p == "barn" || p == "tavern") w *= 0.2f;
                break;
            case CityRole.Knight:
                if (p == "plaza" || p == "hall" || p == "church" || p == "smithy") w *= 2.5f; else if (p == "home" || p == "mill" || p == "bench") w *= 0.2f;
                break;
        }
        return w;
    }

    // Where the watch walks: the gates, the squares, the bridges, past the smithy.
    private static float PatrolWeight(CityNavNode n)
    {
        switch (n.place)
        {
            case "gate": return 3f;
            case "plaza": return 2f;
            case "well": return 1.5f;
            case "bridge": return 1f;
            case "smithy": return 0.8f;
            case "market": return 0.3f;
        }
        return 0f;
    }

    // A door's or place's weight by what it is (doors in the city are many - each one counts for less than a spot).
    private static float W(CityNavNode n, float home, float shop, float tavern, float workshop, float hall, float church, float edge,
                           float market = 0f, float plaza = 0f, float well = 0f, float smithy = 0f,
                           float bench = 0f, float river = 0f, float bridge = 0f, float mill = 0f)
    {
        if (n.kind == CityNodeKind.Edge) return edge * 3f;
        if (n.kind == CityNodeKind.Door)
        {
            float perDoor = n.place == "home" ? home * 0.12f : n.place == "shop" ? shop * 0.2f : n.place == "tavern" ? tavern * 1.2f
                : n.place == "workshop" || n.place == "barn" ? workshop * 0.5f : n.place == "hall" ? hall * 2f : n.place == "church" ? church * 2f : 0.05f;
            return perDoor;
        }
        switch (n.place)
        {
            case "market": return market * 0.5f; // several stalls - together about as popular as the plaza
            case "plaza": return plaza * 2f;
            case "well": return well * 2f;
            case "smithy": return smithy * 2f;
            case "bench": return bench * 0.8f;  // ten or so of them
            case "river": return river * 1.2f;
            case "bridge": return bridge * 1.2f;
            case "mill": return mill * 1.2f;
            case "gate": return 0f;              // the watch's posts
        }
        return 0.2f;
    }

    private CityNavNode Pick(List<CityNavNode> from, System.Func<CityNavNode, float> weight, System.Func<CityNavNode, bool> ok)
    {
        float total = 0f;
        foreach (var n in from) if (ok(n)) total += Mathf.Max(0f, weight(n));
        if (total <= 0f) return null;
        float r = Random.value * total;
        foreach (var n in from)
        {
            if (!ok(n)) continue;
            r -= Mathf.Max(0f, weight(n));
            if (r <= 0f) return n;
        }
        return null;
    }

    // ---- seeing -------------------------------------------------------------------------------------------

    // Could the player see someone standing here right now? (In the camera's view, not too far, and not hidden
    // behind a building - both head and feet have to be blocked for that.)
    private bool Seen(Vector3 p)
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return false;
        Vector3 eye = cam.transform.position;
        if ((p - eye).sqrMagnitude > FarAway * FarAway) return false;
        if (planesFrame != Time.frameCount) { GeometryUtility.CalculateFrustumPlanes(cam, planes); planesFrame = Time.frameCount; }
        if (!GeometryUtility.TestPlanesAABB(planes, new Bounds(p + Vector3.up * 0.9f, new Vector3(1.4f, 2f, 1.4f)))) return false;
        if (occluders == null) return true;
        return !(occluders.Blocked(eye, p + Vector3.up * 1.65f) && occluders.Blocked(eye, p + Vector3.up * 0.25f));
    }

    public bool IsSeen(Vector3 p) => Seen(p);

    // The result screen shows the finished weapon against the sky with the world hidden - the crowd goes too (they'd
    // otherwise float there, the only thing left of the city). They keep walking meanwhile.
    private bool hidden;
    public void SetVisible(bool visible)
    {
        hidden = !visible;
        foreach (var w in walkers) ShowRenderers(w);
    }

    private void ShowRenderers(Walker w)
    {
        foreach (var r in w.go.GetComponentsInChildren<Renderer>(true)) r.enabled = !hidden;
    }

    // ---- update ---------------------------------------------------------------------------------------------

    private void Update()
    {
        if (nodes.Count == 0) return;
        float dt = Time.deltaTime;
        passerbyCooldown -= dt;
        residentCooldown -= dt;
        spawnTimer -= dt;
        int active = 0, guards = 0;
        foreach (var w in walkers) if (w.active) { active++; if (w.role == CityRole.Guard) guards++; }
        ActiveCount = active;
        if (spawnTimer <= 0f)
        {
            spawnTimer = Random.Range(0.8f, 2.2f);
            if (active - guards < Desired()) TrySpawn();
            KeepStalls();
            KeepWatch(guards);
        }
        foreach (var w in walkers) if (w.active) Step(w, dt);
    }

    private void Step(Walker w, float dt)
    {
        if (w.rising > 0f)
        {
            // Up off the bench (the body plays Stand_Up) - back down onto the ground they sat above.
            w.rising -= dt;
            float up = Mathf.SmoothStep(0f, 1f, 1f - Mathf.Clamp01(w.rising / StandUpSeconds));
            Vector3 at = w.t.position; at.y = Mathf.Lerp(w.riseFromY, w.groundY, up);
            w.t.position = at;
            if (w.rising > 0f) return;
        }
        if (w.delay > 0f) { w.delay -= dt; return; } // a companion setting off a moment after the one they're with
        if (w.gliding && w.state != State.Walk)
        {
            // A last short step into place: walked (not slid), unless it's settling onto a seat.
            w.t.position = Vector3.MoveTowards(w.t.position, w.glideTo, dt * GlideSpeed);
            if (w.state != State.Sit) w.body.Walk(GlideSpeed, w.propKind == "crate");
            if ((w.t.position - w.glideTo).sqrMagnitude < 0.0001f) { w.gliding = false; if (w.state != State.Sit) Settle(w); }
        }
        switch (w.state)
        {
            case State.Walk:
            {
                w.segT += w.speed * dt;
                if (w.segT >= w.segLen)
                {
                    w.next++;
                    if (w.next >= w.route.Count) { Arrive(w); return; }
                    BeginSegment(w);
                }
                float k = w.segLen > 0.01f ? Mathf.Clamp01(w.segT / w.segLen) : 1f;
                Vector3 pos = Vector3.Lerp(w.segFrom, w.segTo, k);
                w.groundY = pos.y; // follows the nodes' heights - up over a bridge deck and down again
                w.t.position = pos; // the step's rise and fall is the body's own (the clip's hips ride the planted leg)
                w.body.Walk(w.speed, w.propKind == "crate", w.role == CityRole.Guard);
                Vector3 dir = w.segTo - w.segFrom; dir.y = 0f;
                if (dir.sqrMagnitude > 0.001f) w.t.rotation = Quaternion.Slerp(w.t.rotation, Quaternion.LookRotation(dir), dt * 7f);
                break;
            }
            case State.Linger:
            {
                w.timer -= dt;
                w.phase += dt;
                // Chatting: face whoever else is lingering close by, with a little shifting of weight.
                Quaternion face = w.facing;
                if (!FacesSomething(w.at))
                    foreach (var o in walkers)
                        if (o != w && o.active && (o.state == State.Linger || o.state == State.Sit) && (o.t.position - w.t.position).sqrMagnitude < 2.4f * 2.4f)
                        {
                            Vector3 d = o.t.position - w.t.position; d.y = 0f;
                            if (d.sqrMagnitude > 0.01f) face = Quaternion.LookRotation(d);
                            break;
                        }
                w.t.rotation = Quaternion.Slerp(w.t.rotation, face * Quaternion.Euler(0f, Mathf.Sin(w.phase * 0.9f) * 6f, 0f), dt * 3f);
                w.animTimer -= dt;
                if (w.animTimer <= 0f && !w.gliding) Settle(w);
                if (FollowingSomeone(w)) break; // they go when the one they're with goes (see GoOn)
                if (w.keeper ? !KeeperHours : w.timer <= 0f)
                {
                    if (w.enterAfter) { w.enterAfter = false; w.state = State.Leave; w.timer = Random.Range(10f, 20f); w.recheck = 0f; }
                    else GoOn(w);
                }
                break;
            }
            case State.Sit:
            {
                w.timer -= dt;
                if (FollowingSomeone(w)) break;
                if (w.timer <= 0f) GoOn(w); // (GoOn stands them up first)
                break;
            }
            case State.Leave:
            {
                // At a door or at the edge of town: go in / away once nobody's looking.
                w.recheck -= dt;
                w.timer -= dt;
                if (w.recheck > 0f) break;
                w.recheck = 0.4f;
                Vector3 p = w.t.position;
                bool farDoor = w.at.kind == CityNodeKind.Door && cam != null && (p - cam.transform.position).sqrMagnitude > DoorPopDistance * DoorPopDistance;
                if (!Seen(p) || farDoor) { Remove(w); break; }
                if (w.timer <= 0f) Retarget(w, leave: false); // been waiting a while in plain view - do something else
                break;
            }
        }
    }

    // Is this walker with someone who's still out and about? (If their companion has gone, they carry on alone.)
    private static bool FollowingSomeone(Walker w)
    {
        if (w.leader == null) return false;
        if (w.leader.active && w.leader.leader == null) return true;
        w.leader = null;
        return false;
    }

    // Places where you look at something (the water, the wheel, out of the gate) rather than at whoever's nearby.
    private static bool FacesSomething(CityNavNode n) =>
        n != null && (n.place == "river" || n.place == "bridge" || n.place == "mill" || n.place == "gate" || n.place == "bench");

    private void BeginSegment(Walker w)
    {
        var a = w.route[w.next - 1];
        var b = w.route[w.next];
        w.segFrom = w.t.position; w.segFrom.y = w.groundY;
        Vector3 dir = b.Position - a.Position; dir.y = 0f;
        Vector3 perp = dir.sqrMagnitude > 0.001f ? new Vector3(dir.z, 0f, -dir.x).normalized : Vector3.zero;
        float spread = b.kind == CityNodeKind.Door ? 0f : b.spread;
        w.segTo = b.Position + perp * (w.side * Mathf.Max(spread, b.kind == CityNodeKind.Spot && w.leader != null ? 0.45f : 0f));
        w.segLen = Vector3.Distance(w.segFrom, w.segTo);
        w.segT = 0f;
    }

    private void Arrive(Walker w)
    {
        Decide();
        w.at = w.route[w.route.Count - 1];
        visits[w.at.place ?? ""] = visits.TryGetValue(w.at.place ?? "", out int c) ? c + 1 : 1;
        if (w.at.kind == CityNodeKind.Spot)
        {
            if (w.at.place == "bench" && SitDown(w)) return;
            w.state = State.Linger;
            w.timer = w.keeper ? 0f : w.at.place == "gate" ? Random.Range(25f, 50f) : Random.Range(8f, 32f);
            Vector3 look = w.at.links.Count > 0 ? w.at.links[0].Position - w.t.position : w.t.forward;
            if (w.keeper) look = w.t.position - (w.at.links.Count > 0 ? w.at.links[0].Position : w.t.position - w.t.forward); // back to the stall, facing the square
            if (w.at.place == "smithy") look = new Vector3(2f, 0f, -3.5f) - w.t.position; // having a look at the smith's wares
            if (FacesSomething(w.at)) look = w.at.transform.forward; // the water, the wheel, the road out
            look.y = 0f;
            w.facing = look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : w.t.rotation;
            Count(w.at.place == "market" ? "browse" : w.at.place == "plaza" || w.at.place == "well" ? "chat" : w.at.place);
            if (!w.keeper) StepAside(w);
            Settle(w);
        }
        else
        {
            // Evenings: a drink outside the tavern first - people stand about by its door before going in.
            if (w.at.kind == CityNodeKind.Door && w.at.place == "tavern" && Phase >= 2 && w.role != CityRole.Guard && Random.value < 0.7f)
            {
                Vector3 outward = w.at.links.Count > 0 ? w.at.links[0].Position - w.at.Position : -w.t.forward; outward.y = 0f;
                outward = outward.sqrMagnitude > 0.01f ? outward.normalized : Vector3.forward;
                Vector3 along = new Vector3(outward.z, 0f, -outward.x);
                Glide(w, w.at.Position + outward * 0.9f + along * (w.side * 1.4f));
                w.state = State.Linger;
                w.enterAfter = true;
                w.timer = Random.Range(12f, 40f);
                w.facing = Quaternion.LookRotation(outward);
                Count("tavern");
                return;
            }
            w.state = State.Leave;
            w.timer = Random.Range(12f, 25f);
            w.recheck = 0f;
            Vector3 d = w.at.links.Count > 0 ? w.at.Position - w.at.links[0].Position : w.t.forward; d.y = 0f;
            if (d.sqrMagnitude > 0.01f) w.t.rotation = Quaternion.LookRotation(d);
            Settle(w);
        }
    }

    private static void Glide(Walker w, Vector3 to) { w.glideTo = to; w.gliding = true; }

    // Someone's already standing right here: take a step to the side rather than stand inside them.
    private void StepAside(Walker w)
    {
        const float Room = 0.75f;
        Vector3 me = w.t.position;
        for (int tries = 0; tries < 4; tries++)
        {
            Walker near = null;
            foreach (var o in walkers)
            {
                if (o == w || !o.active || o.state == State.Walk) continue;
                Vector3 there = o.gliding ? o.glideTo : o.t.position;
                Vector3 d = there - me; d.y = 0f;
                if (d.sqrMagnitude < Room * Room) { near = o; break; }
            }
            if (near == null) break;
            Vector3 away = me - (near.gliding ? near.glideTo : near.t.position); away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = w.t.right;
            me += away.normalized * Room + Quaternion.Euler(0f, 90f, 0f) * away.normalized * 0.2f * tries;
        }
        if ((me - w.t.position).sqrMagnitude > 0.0025f) Glide(w, me);
    }

    // What the body does while they're here: chat with whoever's close, look over the wares, or just stand about
    // (NpcBody fills that with its own idle life - shifting weight, looking around).
    private void Settle(Walker w)
    {
        w.animTimer = Random.Range(4f, 9f);
        var b = w.body;
        if (w.state == State.Sit) { b.Sit(); return; }
        b.Carrying = w.propKind == "crate";
        string now = b.State;
        bool standing = now == "Idle_01" || now == "Idle_02" || now == "Carry_Idle" || now == "Look_Around" || now == "Turn_In_Place";
        if (w.state == State.Linger && !b.Carrying)
        {
            bool someone = false;
            foreach (var o in walkers)
                if (o != w && o.active && (o.state == State.Linger || o.state == State.Sit) && (o.t.position - w.t.position).sqrMagnitude < 2.4f * 2.4f) { someone = true; break; }
            string place = w.at != null ? w.at.place : null;
            if (someone && !FacesSomething(w.at) && Random.value < 0.75f) { b.Talk(); return; }
            if (!w.keeper && (place == "market" || place == "smithy") && Random.value < 0.6f) { b.Inspect(); return; }
            if (w.enterAfter && Random.value < 0.25f) { b.Impatient(); return; }
        }
        if (!standing) b.Idle();
    }

    private void Count(string activity)
    {
        if (string.IsNullOrEmpty(activity)) return;
        activities[activity] = activities.TryGetValue(activity, out int n) ? n + 1 : 1;
    }

    // Two to a bench: the second sits beside the first. A full bench (or no seated look for them) - they just stand.
    private bool SitDown(Walker w)
    {
        float taken = 0f; int count = 0;
        foreach (var o in walkers) if (o != w && o.active && o.state == State.Sit && o.at == w.at) { taken = o.seatSide; count++; }
        if (count >= 2 || w.body == null) return false;
        w.seatSide = count == 0 ? (Random.value < 0.5f ? -0.34f : 0.34f) : -taken;
        Transform n = w.at.transform;
        Vector3 seat = n.position - n.forward * 0.6f + n.right * w.seatSide;
        // The body's hips come to rest on the seat: its thighs (folded forward by the Sit clip) lie on the top.
        var prop = w.body.Proportions;
        seat.y = n.position.y + BenchSeatTop + prop.thighW * 0.5f - prop.hipY;
        w.t.rotation = n.rotation;
        w.body.Sit();
        Glide(w, seat); // settling down and back onto the seat
        if (w.prop != null) w.prop.SetActive(false);
        if (w.prop2 != null) w.prop2.SetActive(false);
        w.state = State.Sit;
        w.timer = Random.Range(20f, 55f);
        Count("sit");
        return true;
    }

    private void StandUp(Walker w)
    {
        Transform n = w.at.transform;
        w.gliding = false;
        w.groundY = n.position.y;               // up on their feet where they sat (Step lifts them over StandUpSeconds)
        w.riseFromY = w.t.position.y;
        w.rising = StandUpSeconds;
        w.body.StandUp();
        if (w.prop != null && w.hasProp) w.prop.SetActive(true);
        if (w.prop2 != null && w.hasProp2) w.prop2.SetActive(true);
    }

    // Done here: somewhere else, or home - and whoever came along goes too.
    private void GoOn(Walker w)
    {
        Decide();
        if (w.state == State.Sit) StandUp(w);
        bool leave;
        if (w.role == CityRole.Guard) leave = Random.value < (Phase == 3 ? 0.08f : 0.15f); // the watch keeps walking its rounds
        else
        {
            int active = 0;
            foreach (var o in walkers) if (o.active && o.role != CityRole.Guard) active++;
            leave = w.keeper || Random.value < (active > Desired() ? 0.9f : 0.5f);
        }
        w.keeper = false;
        Retarget(w, leave);
        foreach (var f in walkers)
        {
            if (!f.active || f.leader != w || f == w) continue;
            if (f.state == State.Sit) StandUp(f);
            if (w.state != State.Walk) { f.leader = null; GoOn(f); continue; }
            f.route.Clear();
            f.route.AddRange(w.route);
            f.route[0] = f.at ?? w.route[0];
            f.enterAfter = false;
            StartWalking(f);
            f.delay = Random.Range(0.3f, 0.9f);
        }
    }

    private void Retarget(Walker w, bool leave)
    {
        Decide();
        var from = w.at;
        CityNavNode dest;
        if (w.role == CityRole.Guard && !leave)
            dest = Pick(spots, PatrolWeight, n => n != from && !Crowded(n));
        else
            dest = leave
                ? Pick(origins, n => DestinationWeight(n, w.role), n => n != from && (w.role != CityRole.Guard || guardOrigins.Contains(n)))
                : Pick(spots, n => DestinationWeight(n, w.role), n => n != from && !Crowded(n));
        if (dest == null) dest = Pick(origins, n => 1f, n => n != from);
        if (dest == null || !Route(from, dest, w.route)) { w.state = State.Linger; w.timer = 5f; return; }
        StartWalking(w);
    }

    private bool Crowded(CityNavNode n)
    {
        int here = 0;
        foreach (var o in walkers)
            if (o.active && o.leader == null && o.route.Count > 0 && o.route[o.route.Count - 1] == n && o.state != State.Leave) here++;
        int cap = n.place == "plaza" ? 5 : n.place == "market" ? 2 : n.place == "bridge" || n.place == "gate" || n.place == "bench" ? 1 : 3;
        return here >= cap;
    }

    private void StartWalking(Walker w)
    {
        w.state = State.Walk;
        w.next = 1;
        if (w.route.Count < 2) { Arrive(w); return; }
        BeginSegment(w);
    }

    // ---- appearing and leaving --------------------------------------------------------------------------------

    private Walker Free()
    {
        foreach (var o in walkers) if (!o.active) return o;
        return null;
    }

    private void TrySpawn()
    {
        Decide();
        Walker w = Free();
        if (w == null) return;

        // Someone who bought a weapon from you, out and about with it - they come in along a road or through the gate.
        SwordRecord carrying = null;
        if (passerbyCooldown <= 0f && pickPasserby != null && Random.value < 0.35f && Phase <= 2)
        {
            carrying = pickPasserby();
            passerbyCooldown = Random.Range(45f, 90f);
        }

        CustomerRecord person = null;
        if (carrying == null && residentCooldown <= 0f && pickResident != null && Phase <= 2 && Random.value < 0.4f)
        {
            person = pickResident();
            foreach (var o in walkers) if (o.active && person != null && o.person == person) person = null; // they're already out
            residentCooldown = Random.Range(30f, 70f);
        }

        var origin = Pick(origins, OriginWeight, n => (carrying == null || n.kind == CityNodeKind.Edge) && !Seen(n.Position) && !Occupied(n.Position));
        if (origin == null) return;
        CityRole role = carrying != null || person != null ? CityRole.Townsfolk : PickRole(origin);
        var dest = Pick(spots, n => DestinationWeight(n, role), n => !Crowded(n));
        if (carrying != null) dest = Pick(spots, n => n.place == "well" || n.place == "smithy" || (n.place == "market" && n.Position.x < 30f) ? 1f : 0f, n => true) ?? dest;
        if (dest == null) dest = Pick(origins, n => DestinationWeight(n, role), n => n != origin);
        if (dest == null || !Route(origin, dest, w.route)) return;

        Dress(w, carrying, origin, role, person);
        w.t.position = origin.Position;
        w.groundY = origin.Position.y;
        w.at = origin;
        StartWalking(w);

        // Now and then people go about together: friends, a merchant with a customer, workers, a knight with a companion.
        if (carrying == null && person == null)
        {
            float together = role == CityRole.Knight ? 0.6f : role == CityRole.Worker ? 0.35f : Phase == 2 ? 0.35f : Phase == 3 ? 0.15f : 0.25f;
            if (Random.value < together)
            {
                int extra = (dest.place == "market" || dest.place == "plaza" || dest.kind == CityNodeKind.Door && dest.place == "tavern") && Random.value < 0.35f ? 2 : 1;
                for (int i = 0; i < extra; i++)
                {
                    CityRole with = role == CityRole.Knight ? (Random.value < 0.5f ? CityRole.Knight : CityRole.Townsfolk)
                                  : role == CityRole.Merchant ? CityRole.Townsfolk : role == CityRole.Worker ? CityRole.Worker : PickRole(origin);
                    if (with == CityRole.Guard) with = CityRole.Townsfolk;
                    Follow(w, origin, with, 0.7f + i * 0.6f);
                }
                GroupsFormed++;
            }
        }
    }

    // A companion for `leader`: same way, a step behind and beside them.
    private Walker Follow(Walker leader, CityNavNode origin, CityRole role, float delay)
    {
        var f = Free();
        if (f == null) return null;
        f.route.Clear();
        f.route.AddRange(leader.route);
        Dress(f, null, origin, role);
        f.leader = leader;
        f.speed = leader.speed;
        f.side = Mathf.Clamp(leader.side + (leader.side > 0f ? -0.8f : 0.8f), -0.9f, 0.9f);
        f.t.position = origin.Position;
        f.groundY = origin.Position.y;
        f.at = origin;
        StartWalking(f);
        f.delay = delay;
        return f;
    }

    // Who comes out of this door / along this road.
    private CityRole PickRole(CityNavNode origin)
    {
        // Today's event brings its own people (knights for a tournament, traders for market day) on top of the usual mix.
        float e = Random.value;
        if (e < knightShare) return CityRole.Knight;
        e -= knightShare;
        if (e < nobleShare) return CityRole.Noble;
        e -= nobleShare;
        if (e < merchantShare) return CityRole.Merchant;
        float r = Random.value;
        if (origin.kind == CityNodeKind.Edge)
        {
            if (origin.place == "gate" && r < 0.12f) return CityRole.Knight;
            if (r < 0.5f) return CityRole.Traveller;
            if (r < 0.72f) return CityRole.Merchant;
            if (r < 0.85f) return CityRole.Worker;
            return CityRole.Townsfolk;
        }
        switch (origin.place)
        {
            case "workshop":
            case "barn": return r < 0.55f ? CityRole.Craftsman : r < 0.85f ? CityRole.Worker : CityRole.Townsfolk;
            case "shop": return r < 0.5f ? CityRole.Merchant : CityRole.Townsfolk;
            case "hall": return r < 0.3f ? CityRole.Knight : r < 0.5f ? CityRole.Noble : r < 0.75f ? CityRole.Merchant : CityRole.Townsfolk;
            case "church": return r < 0.25f ? CityRole.Noble : CityRole.Townsfolk;
            case "tavern": return r < 0.3f ? CityRole.Traveller : CityRole.Townsfolk;
        }
        if (Phase == 0 && r < 0.35f) return CityRole.Worker; // off to work
        return r < 0.1f ? CityRole.Craftsman : r < 0.18f ? CityRole.Merchant : r < 0.22f && Phase != 0 ? CityRole.Noble : CityRole.Townsfolk;
    }

    // The market stalls have someone minding them through the day.
    private bool KeeperHours => Hour >= 7.2f && Hour < 17.5f && (weather == null || weather.Weather != "rain");

    private void KeepStalls()
    {
        if (!KeeperHours) return;
        foreach (var stall in stalls)
        {
            bool kept = false;
            foreach (var o in walkers) if (o.active && o.keeper && o.route.Count > 0 && o.route[o.route.Count - 1] == stall) { kept = true; break; }
            if (kept) continue;
            Walker w = Free();
            if (w == null) return;
            // From the nearest shop/workshop door out of sight.
            CityNavNode best = null; float bd = float.MaxValue;
            foreach (var n in origins)
            {
                if (n.kind != CityNodeKind.Door || (n.place != "shop" && n.place != "workshop" && n.place != "home")) continue;
                float d = (n.Position - stall.Position).sqrMagnitude;
                if (d < bd && d < 40f * 40f && !Seen(n.Position)) { bd = d; best = n; }
            }
            if (best == null || !Route(best, stall, w.route)) continue;
            Dress(w, null, best, CityRole.Merchant);
            w.keeper = true;
            w.t.position = best.Position;
            w.groundY = best.Position.y;
            w.at = best;
            StartWalking(w);
            return; // one a tick is plenty
        }
    }

    // The city watch: a pair on its rounds by day, two pairs at night (when they're most of who's about).
    private void KeepWatch(int guards)
    {
        int wanted = (Phase == 3 ? 4 : 2) + Mathf.RoundToInt(guardShare * 8f); // more patrols when the city is uneasy
        if (guards >= wanted || guardOrigins.Count == 0) return;
        Walker w = Free();
        if (w == null) return;
        var origin = Pick(guardOrigins, n => 1f, n => !Seen(n.Position) && !Occupied(n.Position));
        if (origin == null) return;
        var dest = Pick(spots, PatrolWeight, n => !Crowded(n));
        if (dest == null || !Route(origin, dest, w.route)) return;
        Dress(w, null, origin, CityRole.Guard);
        w.t.position = origin.Position;
        w.groundY = origin.Position.y;
        w.at = origin;
        w.speed = 1.0f;
        StartWalking(w);
        if (Follow(w, origin, CityRole.Guard, 0.6f) != null) GroupsFormed++;
    }

    private bool Occupied(Vector3 p)
    {
        foreach (var o in walkers) if (o.active && o.delay <= 0f && (o.t.position - p).sqrMagnitude < 2.5f * 2.5f) return true;
        return false;
    }

    private void Remove(Walker w)
    {
        if (cam != null && Seen(w.t.position) && (w.t.position - cam.transform.position).magnitude < DoorPopDistance) RemovedInView++;
        w.active = false;
        w.keeper = false;
        w.carries = null;
        w.person = null;
        w.leader = null;
        w.enterAfter = false;
        w.go.SetActive(false);
        Removed++;
    }

    // A new day (the screen is black while it turns over): everyone's home and the morning starts afresh.
    // ---- the city's mood (a city event - WorldDirector / CityEvents) -------------------------------------------------
    private float crowdScale = 1f, guardShare, merchantShare, nobleShare, knightShare;

    public void SetMood(float crowd, float guards, float merchants, float nobles, float knights)
    {
        crowdScale = Mathf.Clamp(crowd, 0.2f, 2f);
        guardShare = Mathf.Clamp01(guards);
        merchantShare = Mathf.Clamp01(merchants);
        nobleShare = Mathf.Clamp01(nobles);
        knightShare = Mathf.Clamp01(knights);
    }

    public float CrowdScale => crowdScale;

    public void ResetForNewDay()
    {
        foreach (var w in walkers)
            if (w.active) { w.active = false; w.keeper = false; w.carries = null; w.person = null; w.leader = null; w.enterAfter = false; w.go.SetActive(false); }
    }

    // ---- looks ----------------------------------------------------------------------------------------------

    private static readonly Color Steel = new Color(0.52f, 0.54f, 0.60f);
    private readonly Dictionary<string, (Mesh mesh, Material[] mats)> props = new Dictionary<string, (Mesh, Material[])>();
    private const int LooksPerKind = 10; // anonymous faces per race and role - varied, but a bounded set of meshes

    private void Dress(Walker w, SwordRecord carrying, CityNavNode origin, CityRole role, CustomerRecord person = null)
    {
        w.carries = carrying;
        w.person = person;
        if (person != null) peopleSeen[person.id] = peopleSeen.TryGetValue(person.id, out int times) ? times + 1 : 1;
        w.role = role;
        rolesSeen[role] = rolesSeen.TryGetValue(role, out int seen) ? seen + 1 : 1;
        if (person != null) w.race = person.race;
        else if (carrying != null) w.race = carrying.order.race;
        else if (role == CityRole.Knight) w.race = Random.value < 0.85f ? Race.Human : Race.Dwarf;
        else if (role == CityRole.Guard) w.race = Random.value < 0.8f ? Race.Human : Race.Dwarf;
        else if (role == CityRole.Craftsman && Random.value < 0.4f) w.race = Race.Dwarf;
        else if ((role == CityRole.Traveller || role == CityRole.Noble) && Random.value < 0.35f) w.race = Race.Elf;
        else w.race = RaceInfo.Pick();

        // Who they look like: a known customer exactly as at the smith's table; a buyer of one of your weapons as they
        // were when they ordered it; anyone else one of a handful of generated faces for their race and trade.
        NpcAppearance look = person != null ? NpcAppearance.ForPerson(person)
            : carrying != null ? NpcAppearance.ForOrder(carrying.order, 7919 * carrying.ownerId + 17)
            : NpcAppearance.Generate(w.race, role, 1000 * (int)role + 97 * (int)w.race + Random.Range(0, LooksPerKind));
        w.go.SetActive(true); // (the animator only takes orders while active)
        if (w.body == null) w.body = NpcBody.Create(w.t, look);
        else w.body.Dress(look);

        // What they carry, by who they are.
        string prop = null, prop2 = null;
        if (carrying == null && person == null)
        {
            float r = Random.value;
            switch (role)
            {
                case CityRole.Guard: prop = "spear"; break;
                case CityRole.Traveller: prop = "pack"; prop2 = r < 0.6f ? "staff" : null; break;
                case CityRole.Worker: prop = r < 0.3f ? "sack" : r < 0.55f ? "crate" : r < 0.7f ? "plank" : r < 0.85f ? "bucket" : null; break;
                case CityRole.Craftsman: prop = r < 0.3f ? "crate" : r < 0.5f ? "plank" : r < 0.75f ? "tools" : null; break;
                case CityRole.Merchant: prop = r < 0.4f ? "basket" : r < 0.6f ? "sack" : null; break;
                case CityRole.Townsfolk: prop = r < (w.race == Race.Dwarf ? 0.3f : 0.18f) ? (Random.value < 0.6f ? "basket" : "sack") : null; break;
            }
        }
        SetProp(w, ref w.prop, prop, "Carried");
        SetProp(w, ref w.prop2, prop2, "Carried2");
        w.propKind = prop;
        w.prop2Kind = prop2;
        w.hasProp = prop != null;
        w.hasProp2 = prop2 != null;
        w.body.Carrying = prop == "crate";

        if (w.sword != null) w.sword.SetActive(false);
        WeaponType? worn = carrying != null ? carrying.sword.weaponType : role == CityRole.Knight ? WeaponType.Sword : (WeaponType?)null;
        if (worn.HasValue)
        {
            if (w.sword == null)
            {
                w.sword = new GameObject("Weapon");
                w.sword.AddComponent<MeshFilter>(); w.sword.AddComponent<MeshRenderer>();
            }
            w.sword.transform.SetParent(w.body.BobBone, false); // rides the body's step
            var type = worn.Value;
            if (w.swordType != type)
            {
                Weapons.DisplayModel(type).Attach(w.sword, shader);
                w.swordBlade = w.sword.GetComponent<Renderer>().sharedMaterials[0];
                w.swordType = type;
            }
            Weapons.WearOn(w.sword.transform, type, w.body.Proportions);
            w.swordBlade.color = carrying != null ? ForgeMaterials.Find(carrying.sword.materialName).color : Steel;
            w.sword.SetActive(true);
        }

        w.traveller = origin.kind == CityNodeKind.Edge;
        float pace = role == CityRole.Knight || role == CityRole.Noble ? 0.92f : role == CityRole.Guard ? 1.0f : role == CityRole.Worker ? 1.15f : 1f;
        w.speed = (w.race == Race.Dwarf ? 1.05f : w.race == Race.Elf ? 1.35f : 1.2f) * pace * Random.Range(0.88f, 1.12f);
        w.side = Random.Range(-0.85f, 0.85f);
        w.phase = Random.value * 6f;
        w.keeper = false;
        w.leader = null;
        w.enterAfter = false;
        w.gliding = false;
        w.rising = 0f;
        w.delay = 0f;
        w.active = true;
        w.body.Idle();
        ShowRenderers(w);
        Spawned++;
    }

    private void SetProp(Walker w, ref GameObject holder, string kind, string name)
    {
        if (kind == null) { if (holder != null) holder.SetActive(false); return; }
        if (!props.TryGetValue(kind, out var model))
        {
            var b = new MeshBuilder();
            int wood = b.Paint(new Color(0.50f, 0.34f, 0.19f), 0.08f), dark = b.Paint(new Color(0.34f, 0.22f, 0.12f), 0.08f);
            int sack = b.Paint(new Color(0.62f, 0.52f, 0.34f), 0.05f), fruit = b.Paint(new Color(0.78f, 0.20f, 0.14f), 0.3f);
            int iron = b.Paint(new Color(0.55f, 0.56f, 0.60f), 0.45f), water = b.Paint(new Color(0.30f, 0.45f, 0.60f), 0.6f);
            switch (kind)
            {
                case "crate": b.Box(Vector3.zero, new Vector3(0.44f, 0.34f, 0.34f), wood); b.Box(Vector3.zero, new Vector3(0.46f, 0.06f, 0.36f), dark); break;
                case "sack": b.Ball(Vector3.zero, 0.24f, sack, 1.25f); b.Box(Vector3.up * 0.3f, new Vector3(0.08f, 0.1f, 0.08f), dark); break;
                case "basket":
                    b.Frustum(Vector3.zero, 0.3f, 0.3f, 0.38f, 0.38f, 0.2f, wood); b.Ball(Vector3.up * 0.1f, 0.13f, fruit, 0.6f);
                    b.Box(new Vector3(0f, 0.24f, 0f), new Vector3(0.03f, 0.22f, 0.03f), dark); break; // the handle
                case "spear": b.Prism(Vector3.up * 1.0f, 6, 0.025f, 0.022f, 2.0f, dark); b.Frustum(Vector3.up * 2.08f, 0.07f, 0.02f, 0.005f, 0.005f, 0.2f, iron); break;
                case "staff": b.Prism(Vector3.up * 0.8f, 6, 0.03f, 0.026f, 1.6f, wood); b.Ball(Vector3.up * 1.6f, 0.05f, dark); break;
                case "pack": b.Box(Vector3.zero, new Vector3(0.36f, 0.44f, 0.2f), sack); b.Box(Vector3.up * 0.25f, new Vector3(0.38f, 0.12f, 0.22f), dark); break;
                case "bucket":
                    b.Frustum(Vector3.zero, 0.2f, 0.2f, 0.25f, 0.25f, 0.24f, wood); b.Box(Vector3.up * 0.1f, new Vector3(0.2f, 0.02f, 0.2f), water);
                    b.Box(new Vector3(0f, 0.2f, 0f), new Vector3(0.02f, 0.16f, 0.02f), iron); break; // the bail
                case "tools": b.Prism(Vector3.zero, 6, 0.022f, 0.022f, 0.7f, wood, Quaternion.Euler(0f, 0f, 90f)); b.Box(new Vector3(0.36f, 0f, 0f), new Vector3(0.08f, 0.2f, 0.08f), iron); break;
                default: b.Box(Vector3.zero, new Vector3(0.12f, 0.08f, 2.2f), wood); break;
            }
            var m = b.Build("Carried" + kind);
            model = (m.mesh, m.CreateMaterials(shader));
            props[kind] = model;
        }
        if (holder == null)
        {
            holder = new GameObject(name);
            holder.AddComponent<MeshFilter>(); holder.AddComponent<MeshRenderer>();
        }
        holder.GetComponent<MeshFilter>().sharedMesh = model.mesh;
        holder.GetComponent<MeshRenderer>().sharedMaterials = model.mats;
        // Loads on the chest, shoulder or back ride that bone (they move with the body's step and sway). Things carried
        // in a hand (basket, bucket) or planted on the ground (spear, staff) follow the hand but hang or stand upright
        // - see LateUpdate.
        var p = w.body.Proportions;
        float front = p.torsoD * 0.5f, shoulder = p.shoulderY - p.chestY;
        var t = holder.transform;
        t.SetParent(InHand(kind) ? w.t : w.body.Chest, false);
        switch (kind)
        {
            case "crate": t.localPosition = new Vector3(0f, -0.1f, front + 0.2f); t.localRotation = Quaternion.identity; break;
            case "sack": t.localPosition = new Vector3(p.torsoW * 0.3f, shoulder + 0.14f, -0.1f); t.localRotation = Quaternion.Euler(0f, 0f, -20f); break;
            case "pack": t.localPosition = new Vector3(0f, shoulder - 0.2f, -(front + 0.1f)); t.localRotation = Quaternion.identity; break;
            case "tools": t.localPosition = new Vector3(p.torsoW * 0.3f, shoulder + 0.08f, -0.02f); t.localRotation = Quaternion.Euler(0f, 90f, -12f); break;
            case "plank": t.localPosition = new Vector3(p.torsoW * 0.35f, shoulder + 0.08f, 0.1f); t.localRotation = Quaternion.Euler(0f, 8f, 0f); break;
            default: t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity; break;
        }
        holder.SetActive(true);
    }

    private static bool InHand(string kind) => kind == "basket" || kind == "bucket" || kind == "spear" || kind == "staff";

    // Hand-held things follow the hand after the animation has posed it: a basket or bucket hangs from it, a spear or
    // staff stands on the ground beside the step with the hand around its shaft.
    private void LateUpdate()
    {
        foreach (var w in walkers)
        {
            if (!w.active || w.body == null) continue;
            if (w.prop != null && w.prop.activeSelf && InHand(w.propKind)) HoldInHand(w, w.prop.transform, w.propKind);
            if (w.prop2 != null && w.prop2.activeSelf && InHand(w.prop2Kind)) HoldInHand(w, w.prop2.transform, w.prop2Kind);
        }
    }

    private static void HoldInHand(Walker w, Transform t, string kind)
    {
        var p = w.body.Proportions;
        Transform hand = kind == "staff" ? w.body.HandL : w.body.HandR;
        Vector3 grip = hand.position - Vector3.up * (p.hand * 0.5f);
        Quaternion yaw = Quaternion.Euler(0f, w.t.eulerAngles.y, 0f);
        if (kind == "spear" || kind == "staff")
        {
            Vector3 foot = grip; foot.y = w.t.position.y + 0.02f;
            t.SetPositionAndRotation(foot, yaw * Quaternion.Euler(kind == "staff" ? 6f : 3f, 0f, 0f));
        }
        else
            t.SetPositionAndRotation(grip - Vector3.up * (kind == "basket" ? 0.35f : 0.28f), yaw);
    }

    // Who's walking past in view wearing one of your weapons.
    public SwordRecord PasserbyWithYourSword
    {
        get
        {
            foreach (var w in walkers)
                if (w.active && w.carries != null && w.carries.IsOut && cam != null &&
                    (w.t.position - cam.transform.position).sqrMagnitude < 45f * 45f && Seen(w.t.position))
                    return w.carries;
            return null;
        }
    }

    // ---- routes ---------------------------------------------------------------------------------------------

    private readonly Dictionary<CityNavNode, float> cost = new Dictionary<CityNavNode, float>();
    private readonly Dictionary<CityNavNode, CityNavNode> cameFrom = new Dictionary<CityNavNode, CityNavNode>();
    private readonly List<CityNavNode> open = new List<CityNavNode>();

    // Shortest way along the streets (A*), written into `route` (from and to included).
    private bool Route(CityNavNode from, CityNavNode to, List<CityNavNode> route)
    {
        route.Clear();
        if (from == null || to == null) return false;
        cost.Clear(); cameFrom.Clear(); open.Clear();
        cost[from] = 0f;
        open.Add(from);
        int guard = 0;
        while (open.Count > 0 && guard++ < 4000)
        {
            int bi = 0; float bf = float.MaxValue;
            for (int i = 0; i < open.Count; i++)
            {
                float f = cost[open[i]] + Vector3.Distance(open[i].Position, to.Position);
                if (f < bf) { bf = f; bi = i; }
            }
            var cur = open[bi];
            open.RemoveAt(bi);
            if (cur == to) break;
            foreach (var nb in cur.links)
            {
                if (nb == null) continue;
                // Doors, far edges and places are ends of a journey, never shortcuts through it.
                if (nb != to && (nb.kind == CityNodeKind.Door || nb.kind == CityNodeKind.Edge || (nb.kind == CityNodeKind.Spot && nb.links.Count <= 1))) continue;
                float c = cost[cur] + Vector3.Distance(cur.Position, nb.Position);
                if (!cost.TryGetValue(nb, out float old) || c < old)
                {
                    cost[nb] = c;
                    cameFrom[nb] = cur;
                    if (!open.Contains(nb)) open.Add(nb);
                }
            }
        }
        if (!cameFrom.ContainsKey(to) && from != to) return false;
        for (var n = to; n != null; n = cameFrom.TryGetValue(n, out var p) ? p : null)
        {
            route.Add(n);
            if (n == from) break;
        }
        route.Reverse();
        return route.Count >= 2 && route[0] == from;
    }
}
