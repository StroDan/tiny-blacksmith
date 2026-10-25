using System.Collections.Generic;
using UnityEngine;

// The city dressed for the day's event, so the player can LOOK at the world and see that today is different (they never
// walk into it). Low-poly props built in code (one merged mesh per event, a handful of materials): bunting strung
// across the lanes, lanterns, market stalls, carts, royal or elven banners, harvest sheaves, dwarven wagons.
//
// Placement is found, not hand-typed: points are sampled all round the seat (favouring 9-30 m and the four views, the
// table's most), and a prop only goes where nothing is already standing - never inside a building (CityOccluders), on
// the river or a bridge (the Stream/Bridge meshes), on another prop, or across a line people walk (the crowd's street
// graph and the customer's lane - so walkers never pass through a cart). Bunting is strung ACROSS a walking line,
// stalls and carts stand beside one. Seeded by the event type: an event always looks the same.
public class CityEventDecor : MonoBehaviour
{
    private Shader shader;
    private Vector3 seat;
    private GameObject shown;
    private CityEventType shownType = (CityEventType)(-1);
    private readonly Dictionary<CityEventType, (GameObject go, int props)> cache = new Dictionary<CityEventType, (GameObject, int)>();

    // What's already there (gathered once).
    private readonly List<CityNavNode> nodes = new List<CityNavNode>();
    private readonly List<(Vector3 a, Vector3 b, float half)> walkLines = new List<(Vector3, Vector3, float)>();
    private readonly List<Bounds> solids = new List<Bounds>();
    private readonly List<string> solidNames = new List<string>();
    private readonly List<Vector3[]> water = new List<Vector3[]>();                       // stream/bridge triangles
    private readonly Dictionary<long, List<int>> waterGrid = new Dictionary<long, List<int>>(); // 3 m cells -> triangles
    private CityOccluders occluders;
    private readonly List<(Vector3 p, float r)> placed = new List<(Vector3, float)>();
    private List<Vector3> candidates;

    public int PropCount { get; private set; }  // (tests)
    public CityEventType ShownType => shownType;

    public const float Near = 7f, Far = 55f;    // how close/far from the seat decorations may stand (tall bunting poles: PoleNear)
    private const float PoleNear = 6f;   // bunting may hang this close - lower the closer it is (see Bunting), to stay in view
    private const float Cell = 3f;

    public static CityEventDecor Create(Shader shader, Vector3 seat)
    {
        var go = new GameObject("CityEventDecor");
        var d = go.AddComponent<CityEventDecor>();
        d.shader = shader;
        d.seat = new Vector3(seat.x, 0f, seat.z);
        d.Gather();
        return d;
    }

    public void Show(CityEventInfo ev)
    {
        var type = ev != null ? ev.type : CityEventType.None;
        if (type == shownType) return;
        if (shown != null) shown.SetActive(false);
        shown = null;
        shownType = type;
        PropCount = 0;
        if (ev == null || ev.decor == EventDecor.None) return;
        if (!cache.TryGetValue(type, out var built))
        {
            var go = Build(ev);
            built = (go, PropCount);
            cache[type] = built;
        }
        shown = built.go;
        PropCount = built.props;
        if (shown != null) shown.SetActive(true);
    }

    // ---------------------------------------------------------------------------------------- what's already there

    private void Gather()
    {
        nodes.AddRange(FindObjectsByType<CityNavNode>(FindObjectsSortMode.None));
        nodes.Sort((a, b) => string.CompareOrdinal(a.name, b.name)); // a stable order: the same layout every run
        foreach (var n in nodes)
            foreach (var l in n.links)
                if (l != null && string.CompareOrdinal(n.name, l.name) < 0)
                    walkLines.Add((n.Position, l.Position, Mathf.Max(n.spread, l.spread) * 0.5f + 0.3f));
        // The customer's own way in to the table, and the lane to the bridge (not on the crowd's graph).
        walkLines.Add((new Vector3(5.5f, 0f, -11f), new Vector3(2.6f, 0f, -3.5f), 1.3f));
        walkLines.Add((new Vector3(3f, 0f, -3.5f), new Vector3(17f, 0f, -3.5f), 1.1f));
        occluders = FindAnyObjectByType<CityOccluders>();

        // Solid things near the seat that aren't buildings: props, fences, trees, stalls, the stations themselves.
        foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer) continue;
            var b = r.bounds;
            if (Flat(b.center, seat) > Far + 40f) continue;
            string n = r.name;
            if (n.Contains("Stream") || n.Contains("Bridge"))
            {
                if (r.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null && mf.sharedMesh.isReadable) AddWater(mf);
                else { solids.Add(b); solidNames.Add(r.name); }
                continue;
            }
            // A library building: its wall block (its whole renderer bounds reach over porches, awnings and roof overhangs,
            // and every LOD has one) - the city's occluder boxes cover buildings too.
            var env = r.GetComponentInParent<EnvironmentAsset>();
            if (env != null && env.kind == "building")
            {
                if (buildings.Add(env))
                {
                    var wb = WorldBounds(env.transform, env.walls, 0.4f);
                    solids.Add(wb); solidNames.Add(env.name);
                    walls.Add(WorldBounds(env.transform, env.walls, 0f));
                }
                continue;
            }
            bool readable = r.TryGetComponent(out MeshFilter pmf) && pmf.sharedMesh != null && pmf.sharedMesh.isReadable;
            if (b.size.y < 0.35f)                               // flat: ground, fields - and paths and streets, never built on
            {
                if (readable && (n.Contains("Path") || n.Contains("Street") || n.Contains("Road") || n.Contains("Plaza") || n.Contains("Square"))) PaveCells(pmf);
                continue;
            }
            if (n.Contains("Ground") || n.Contains("Terrain") || n.Contains("Field") || n.Contains("Hill") || n.Contains("Grass")) continue;
            if (b.min.y > 2.6f) continue;                       // up in the air (canopies, roofs)
            if (r.GetComponentInParent<CityLife>() != null || r.GetComponentInParent<NpcBody>() != null) continue; // people move
            // A merged mesh of many things (the square's well and stall, the village's bushes and flowers) would block its
            // whole bounding box: mark only where its low geometry actually stands.
            if ((b.size.x > 5f || b.size.z > 5f) && readable) Occupy(pmf);
            else if (b.size.x > 40f || b.size.z > 40f) continue; // far scenery we can't look inside (buildings are occluders)
            else { solids.Add(b); solidNames.Add(r.name); }
        }
    }

    private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

    private readonly HashSet<EnvironmentAsset> buildings = new HashSet<EnvironmentAsset>();
    private readonly List<Bounds> walls = new List<Bounds>(); // library buildings' wall blocks (façade bunting hangs off them)

    private static Bounds WorldBounds(Transform t, Bounds local, float margin)
    {
        var b = new Bounds(t.TransformPoint(local.center), Vector3.zero);
        for (int i = 0; i < 8; i++)
            b.Encapsulate(t.TransformPoint(local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
        b.Expand(new Vector3(margin * 2f, 0f, margin * 2f));
        return b;
    }

    private const float OccCell = 0.5f;
    private readonly HashSet<long> occupied = new HashSet<long>();

    private void Occupy(MeshFilter mf)
    {
        var m = mf.sharedMesh;
        var v = m.vertices;
        var t = m.triangles;
        var tr = mf.transform;
        void Mark(Vector3 w)
        {
            if (w.y < 0.05f || w.y > 2.6f || Flat(w, seat) > Far + 10f) return;
            occupied.Add(Key(Mathf.FloorToInt(w.x / OccCell), Mathf.FloorToInt(w.z / OccCell)));
        }
        for (int i = 0; i < t.Length; i += 3)
        {
            Vector3 a = tr.TransformPoint(v[t[i]]), b = tr.TransformPoint(v[t[i + 1]]), c = tr.TransformPoint(v[t[i + 2]]);
            Mark(a); Mark(b); Mark(c); Mark((a + b + c) / 3f); Mark((a + b) * 0.5f); Mark((b + c) * 0.5f); Mark((c + a) * 0.5f);
        }
    }

    // Paths and streets: every half-metre cell a path's surface covers.
    private readonly HashSet<long> paved = new HashSet<long>();

    private void PaveCells(MeshFilter mf)
    {
        var m = mf.sharedMesh;
        var v = m.vertices;
        var t = m.triangles;
        var tr = mf.transform;
        for (int i = 0; i < t.Length; i += 3)
        {
            Vector3 a = tr.TransformPoint(v[t[i]]), b = tr.TransformPoint(v[t[i + 1]]), c = tr.TransformPoint(v[t[i + 2]]);
            if (Flat(a, seat) > Far + 10f) continue;
            float edge = Mathf.Max(Flat(a, b), Mathf.Max(Flat(b, c), Flat(c, a)));
            int steps = Mathf.Clamp(Mathf.CeilToInt(edge / 0.35f), 1, 200);
            for (int u = 0; u <= steps; u++)
                for (int w = 0; w <= steps - u; w++)
                {
                    Vector3 p = a + (b - a) * (u / (float)steps) + (c - a) * (w / (float)steps);
                    paved.Add(Key(Mathf.FloorToInt(p.x / OccCell), Mathf.FloorToInt(p.z / OccCell)));
                }
        }
    }

    private bool OnPath(Vector3 p, float r)
    {
        int x0 = Mathf.FloorToInt((p.x - r) / OccCell), x1 = Mathf.FloorToInt((p.x + r) / OccCell);
        int z0 = Mathf.FloorToInt((p.z - r) / OccCell), z1 = Mathf.FloorToInt((p.z + r) / OccCell);
        for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
                if (paved.Contains(Key(x, z))) return true;
        return false;
    }

    private bool Occupied(Vector3 p, float r)
    {
        int x0 = Mathf.FloorToInt((p.x - r) / OccCell), x1 = Mathf.FloorToInt((p.x + r) / OccCell);
        int z0 = Mathf.FloorToInt((p.z - r) / OccCell), z1 = Mathf.FloorToInt((p.z + r) / OccCell);
        for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
                if (occupied.Contains(Key(x, z))) return true;
        return false;
    }

    private void AddWater(MeshFilter mf)
    {
        var m = mf.sharedMesh;
        var v = m.vertices;
        var t = m.triangles;
        var tr = mf.transform;
        for (int i = 0; i < t.Length; i += 3)
        {
            Vector3 a = tr.TransformPoint(v[t[i]]), b = tr.TransformPoint(v[t[i + 1]]), c = tr.TransformPoint(v[t[i + 2]]);
            if (Flat(a, seat) > Far + 30f) continue;
            int idx = water.Count;
            water.Add(new[] { a, b, c });
            int x0 = Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) / Cell), x1 = Mathf.FloorToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) / Cell);
            int z0 = Mathf.FloorToInt(Mathf.Min(a.z, Mathf.Min(b.z, c.z)) / Cell), z1 = Mathf.FloorToInt(Mathf.Max(a.z, Mathf.Max(b.z, c.z)) / Cell);
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    long k = Key(x, z);
                    if (!waterGrid.TryGetValue(k, out var list)) waterGrid[k] = list = new List<int>();
                    list.Add(idx);
                }
        }
    }

    private static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
    private static float DistXZ(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

    private bool InWater(Vector3 p, float r)
    {
        int x0 = Mathf.FloorToInt((p.x - r) / Cell), x1 = Mathf.FloorToInt((p.x + r) / Cell);
        int z0 = Mathf.FloorToInt((p.z - r) / Cell), z1 = Mathf.FloorToInt((p.z + r) / Cell);
        for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
            {
                if (!waterGrid.TryGetValue(Key(x, z), out var list)) continue;
                foreach (int i in list)
                {
                    var t = water[i];
                    // The footprint's centre and four edge points: none may be over water (or on a bridge).
                    if (PointInTri(p, t[0], t[1], t[2]) || PointInTri(p + new Vector3(r, 0f, 0f), t[0], t[1], t[2]) || PointInTri(p - new Vector3(r, 0f, 0f), t[0], t[1], t[2]) ||
                        PointInTri(p + new Vector3(0f, 0f, r), t[0], t[1], t[2]) || PointInTri(p - new Vector3(0f, 0f, r), t[0], t[1], t[2])) return true;
                }
            }
        return false;
    }

    private static bool PointInTri(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        float d1 = Sign(p, a, b), d2 = Sign(p, b, c), d3 = Sign(p, c, a);
        bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }
    private static float Sign(Vector3 p1, Vector3 p2, Vector3 p3) => (p1.x - p3.x) * (p2.z - p3.z) - (p2.x - p3.x) * (p1.z - p3.z);

    private static float SegDist(Vector3 p, Vector3 a, Vector3 b, out Vector3 foot)
    {
        Vector2 P = new Vector2(p.x, p.z), A = new Vector2(a.x, a.z), B = new Vector2(b.x, b.z);
        Vector2 ab = B - A;
        float t = ab.sqrMagnitude < 1e-4f ? 0f : Mathf.Clamp01(Vector2.Dot(P - A, ab) / ab.sqrMagnitude);
        Vector2 f = A + ab * t;
        foot = new Vector3(f.x, 0f, f.y);
        return Vector2.Distance(P, f);
    }

    // The walking line nearest to p: how far past its edge p stands, the nearest point on it, and its direction.
    private float NearestWalk(Vector3 p, out Vector3 foot, out Vector3 dir)
    {
        float best = float.MaxValue;
        foot = p; dir = Vector3.forward;
        foreach (var (a, b, half) in walkLines)
        {
            float d = SegDist(p, a, b, out var f) - half;
            if (d < best) { best = d; foot = f; dir = new Vector3(b.x - a.x, 0f, b.z - a.z).normalized; }
        }
        return best;
    }

    // May a prop of this footprint radius stand here? (height: how tall it is, for the sight line from the seat)
    public bool Free(Vector3 p, float r, float height = 2f, float near = Near)
    {
        float d = Flat(p, seat);
        if (d < near || d > Far) return Reject(0);
        foreach (var (a, b, half) in walkLines) if (SegDist(p, a, b, out _) < half + r) return Reject(1);
        if (OnPath(p, r)) return Reject(1);
        foreach (var s in solids)
        {
            var e = s;
            e.Expand(new Vector3(r * 2f, 0f, r * 2f));
            if (p.x > e.min.x && p.x < e.max.x && p.z > e.min.z && p.z < e.max.z) return Reject(2);
        }
        if (Occupied(p, r + 0.35f)) return Reject(2);
        if (occluders != null)
            foreach (var bx in occluders.boxes)
            {
                var e = bx;
                e.Expand(new Vector3(r * 2f + 0.4f, 0f, r * 2f + 0.4f));
                if (p.x > e.min.x && p.x < e.max.x && p.z > e.min.z && p.z < e.max.z) return Reject(3);
            }
        foreach (var (q, qr) in placed) if (DistXZ(p, q) < r + qr + 0.4f) return Reject(4);
        if (InWater(p, r + 0.3f)) return Reject(5);
        // Worth putting there only if the seat can see it.
        if (occluders != null && occluders.Blocked(seat + Vector3.up * 1.2f, p + Vector3.up * height)) return Reject(6);
        return true;
    }

    // Why placements were turned down (range, walking line, prop, building, other decoration, water, hidden) - tests.
    public readonly int[] rejected = new int[7];
    private bool Reject(int why) { rejected[why]++; return false; }
    public string RejectStats => $"range {rejected[0]}, path {rejected[1]}, prop {rejected[2]}, building {rejected[3]}, decor {rejected[4]}, water {rejected[5]}, hidden {rejected[6]}; " +
                                 $"candidates {candidates?.Count ?? 0}, solids {solids.Count}, walk lines {walkLines.Count}, water tris {water.Count}";

    // (Tests) what stands in the way in one view cone: the names of the solids that turned spots down, most first.
    public string Blockers(float yaw, float halfAngle)
    {
        var count = new Dictionary<string, int>();
        void Add(string k) { count.TryGetValue(k, out int c); count[k] = c + 1; }
        foreach (var p in candidates ?? new List<Vector3>())
        {
            float y = Mathf.Atan2(p.x - seat.x, p.z - seat.z) * Mathf.Rad2Deg;
            if (Mathf.Abs(Mathf.DeltaAngle(y, yaw)) > halfAngle || Flat(p, seat) > 30f) continue;
            for (int i = 0; i < solids.Count; i++)
            {
                var e = solids[i];
                e.Expand(new Vector3(1f, 0f, 1f));
                if (p.x > e.min.x && p.x < e.max.x && p.z > e.min.z && p.z < e.max.z) Add(solidNames[i]);
            }
            if (Occupied(p, 0.5f)) Add("(merged props)");
            if (InWater(p, 0.5f)) Add("(water)");
            foreach (var (a, b, half) in walkLines) if (SegDist(p, a, b, out _) < half + 0.5f) { Add("(walk line)"); break; }
        }
        var list = new List<KeyValuePair<string, int>>(count);
        list.Sort((a, b) => b.Value.CompareTo(a.Value));
        return string.Join(", ", list.GetRange(0, Mathf.Min(16, list.Count)).ConvertAll(kv => $"{kv.Key} {kv.Value}"));
    }

    // (Tests) a character map of what each spot on a grid is: . free, - walking line, p prop, b building, w water,
    // h hidden from the seat, r out of range, d another decoration.
    public string ReasonMap(Vector2 from, Vector2 to, float step, float r)
    {
        var sb = new System.Text.StringBuilder();
        for (float z = to.y; z >= from.y; z -= step)
        {
            sb.Append($"{z,6:0.0} ");
            for (float x = from.x; x <= to.x; x += step)
            {
                var before = (int[])rejected.Clone();
                bool ok = Free(new Vector3(x, 0f, z), r);
                int why = -1;
                for (int i = 0; i < rejected.Length; i++) if (rejected[i] != before[i]) why = i;
                sb.Append(ok ? '.' : "r-pbdwh"[Mathf.Max(0, why)]);
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private void Take(Vector3 p, float r) { placed.Add((p, r)); PropCount++; }

    // Points all round the seat, best first: 9-30 m out, and in one of the four views (the table's first).
    private void Sample()
    {
        candidates = new List<Vector3>();
        var rng = new System.Random(4242);
        for (float d = PoleNear; d <= Far; d += 1.2f)
        {
            int n = Mathf.Max(8, Mathf.RoundToInt(2f * Mathf.PI * d / 1.5f));
            for (int i = 0; i < n; i++)
            {
                float a = (i + (float)rng.NextDouble() * 0.6f) / n * Mathf.PI * 2f;
                var p = seat + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * (d + (float)rng.NextDouble() * 0.8f);
                if (occluders != null && occluders.Blocked(seat + Vector3.up * 1.2f, p + Vector3.up * 2f)) continue;
                candidates.Add(p);
            }
        }
        candidates.Sort((x, y) => Score(x).CompareTo(Score(y)));
    }

    private float Score(Vector3 p)
    {
        float d = Flat(p, seat);
        float yaw = Mathf.Atan2(p.x - seat.x, p.z - seat.z) * Mathf.Rad2Deg; // 0 = +Z (the forge view), 90 = +X (the table)
        float view = Mathf.Min(Mathf.Min(Mathf.Abs(Mathf.DeltaAngle(yaw, 90f)), Mathf.Abs(Mathf.DeltaAngle(yaw, 0f)) * 1.5f),
                               Mathf.Min(Mathf.Abs(Mathf.DeltaAngle(yaw, 180f)) * 1.5f, Mathf.Abs(Mathf.DeltaAngle(yaw, 270f)) * 2.5f));
        float dist = d < 9f ? (9f - d) * 1.5f : d > 30f ? (d - 30f) * 0.8f : 0f;
        return dist + view * 0.12f;
    }

    // ---------------------------------------------------------------------------------------- building a dressing

    private MeshBuilder mb;
    private int wood, dark, cloth, cloth2, gold, glow, straw, strawDark, canvas, iron, ore, green;

    private GameObject Build(CityEventInfo ev)
    {
        placed.Clear();
        PropCount = 0;
        if (candidates == null) Sample();
        var rng = new System.Random(1000 + (int)ev.type * 7919);
        mb = new MeshBuilder();
        Color tint = ev.decorTint;
        wood = mb.Paint(new Color(0.42f, 0.29f, 0.18f), 0.2f);
        dark = mb.Paint(new Color(0.22f, 0.16f, 0.11f), 0.2f);
        cloth = mb.Paint(tint, 0.15f);
        cloth2 = mb.Paint(Color.Lerp(tint, new Color(0.95f, 0.92f, 0.82f), 0.75f), 0.15f);
        gold = mb.Paint(new Color(0.92f, 0.74f, 0.25f), 0.45f);
        glow = mb.Paint(new Color(1f, 0.72f, 0.32f), 0.3f, 2.2f);
        straw = mb.Paint(new Color(0.86f, 0.70f, 0.32f), 0.1f);
        strawDark = mb.Paint(new Color(0.66f, 0.50f, 0.22f), 0.1f);
        canvas = mb.Paint(new Color(0.86f, 0.82f, 0.70f), 0.1f);
        iron = mb.Paint(new Color(0.30f, 0.31f, 0.34f), 0.35f);
        ore = mb.Paint(new Color(0.40f, 0.36f, 0.33f), 0.15f);
        green = mb.Paint(new Color(0.18f, 0.45f, 0.26f), 0.15f);

        var d = ev.decor;
        if ((d & EventDecor.Bunting) != 0) { FacadeBunting(10, (d & EventDecor.Lanterns) != 0); Bunting(rng, 7, (d & EventDecor.Lanterns) != 0); }
        else if ((d & EventDecor.Lanterns) != 0) LanternPosts(8);
        if ((d & EventDecor.Banners) != 0) Banners(8, false); // in the event's colours (royal blue, dwarven clan brown...)
        if ((d & EventDecor.ElvenBanners) != 0) Banners(7, true);
        if ((d & EventDecor.Stalls) != 0) Stalls(rng, 4);
        if ((d & EventDecor.Carts) != 0) Carts(rng, 3, false);
        if ((d & EventDecor.Wagons) != 0) Carts(rng, 3, true);
        if ((d & EventDecor.HaySheaves) != 0) Sheaves(rng, 10);

        if (PropCount == 0) { mb = null; return null; }
        var go = new GameObject($"Decor_{ev.type}");
        go.transform.SetParent(transform, false);
        mb.Build(go.name).Attach(go, shader);
        mb = null;
        return go;
    }

    // ---- bunting (and lanterns on it): strung ACROSS a walking line between two poles ---------------------------------
    private void Bunting(System.Random rng, int max, bool lanterns)
    {
        int made = 0;
        foreach (var p0 in candidates)
        {
            if (made >= max) break;
            float edge = NearestWalk(p0, out var foot, out var dir);
            if (edge > 2.5f || edge < 0f) continue; // beside a street, not in it and not far from any
            if (!Free(p0, 0.25f, 4.5f, PoleNear)) continue;
            // The other pole across the street (mirrored over it), a few steps along so the string runs at a slant.
            Vector3 across = new Vector3(foot.x - p0.x, 0f, foot.z - p0.z);
            float along = (rng.NextDouble() < 0.5 ? -1f : 1f) * (1.5f + (float)rng.NextDouble() * 2.5f);
            Vector3 p1 = foot + across + dir * along;
            placed.Add((p0, 0.25f)); // held while its partner is checked
            bool pair = Free(p1, 0.25f, 4.5f, PoleNear) && Flat(p0, p1) >= 3f && Flat(p0, p1) <= 14f;
            placed.RemoveAt(placed.Count - 1);
            if (!pair) continue;
            Take(p0, 0.25f); Take(p1, 0.25f);
            // High enough to walk under, low enough to stay inside the seated view (its top edge is about 15 degrees up),
            // and the further away, the bigger the pennants - so a string down the avenue still reads from the seat.
            float dist = Mathf.Min(Flat(p0, seat), Flat(p1, seat));
            float H = Mathf.Clamp(1.3f + dist * 0.27f, 2.9f, 4.4f);
            float size = Mathf.Clamp(dist / 14f, 1f, 2.2f);
            Pole(p0, H); Pole(p1, H);
            Vector3 top0 = p0 + Vector3.up * (H - 0.1f), top1 = p1 + Vector3.up * (H - 0.1f);
            float len = Vector3.Distance(top0, top1);
            int flags = Mathf.Max(4, Mathf.RoundToInt(len / (0.62f * size)));
            Vector3 prev = top0;
            var rot = Quaternion.LookRotation((top1 - top0).normalized, Vector3.up) * Quaternion.Euler(0f, 90f, 0f);
            for (int i = 1; i <= flags; i++)
            {
                float u = i / (float)flags;
                Vector3 q = Vector3.Lerp(top0, top1, u) + Vector3.down * (Mathf.Sin(u * Mathf.PI) * len * 0.07f);
                Segment(prev, q, 0.025f * size, dark);
                if (i < flags)
                {
                    // A pennant hanging from the line, alternating colours; now and then a lantern between them.
                    mb.Frustum(q + Vector3.down * 0.24f * size, 0.02f, 0.02f, 0.36f * size, 0.025f, 0.44f * size, i % 2 == 0 ? cloth : cloth2, rot);
                    if (lanterns && i % 3 == 1) Lantern(q + Vector3.down * 0.34f * size, 0.12f * size);
                }
                prev = q;
            }
            made++;
        }
    }

    // ---- bunting strung from house to house across a street (hooked to the walls under the eaves) ------------------------
    private void FacadeBunting(int max, bool lanterns)
    {
        // Points along the streets, best-seen first.
        var samples = new List<(Vector3 p, Vector3 dir)>();
        foreach (var (a, b, _) in walkLines)
        {
            float len = Flat(a, b);
            if (len < 0.5f) continue;
            Vector3 dir = new Vector3(b.x - a.x, 0f, b.z - a.z).normalized;
            for (float t = 0f; t <= len; t += 3f)
            {
                var p = Vector3.Lerp(a, b, t / len); p.y = 0f;
                float d = Flat(p, seat);
                if (d >= 12f && d <= Far) samples.Add((p, dir));
            }
        }
        samples.Sort((x, y) => Score(x.p).CompareTo(Score(y.p)));
        var strung = new List<Vector3>();
        int made = 0;
        foreach (var (p, dir) in samples)
        {
            if (made >= max) break;
            bool near = false;
            foreach (var q in strung) if (Flat(p, q) < 7f) { near = true; break; }
            if (near) continue;
            Vector3 right = new Vector3(dir.z, 0f, -dir.x);
            if (!WallHit(p, right, out var hitA, out float topA) || !WallHit(p, -right, out var hitB, out float topB)) continue;
            float span = Flat(hitA, hitB);
            if (span < 4f || span > 16f) continue;
            float h = Mathf.Clamp(Mathf.Min(topA, topB) - 0.7f, 3.4f, 5.2f);
            Vector3 a = new Vector3(hitA.x, h, hitA.z), b = new Vector3(hitB.x, h, hitB.z);
            if (occluders != null && occluders.Blocked(seat + Vector3.up * 1.2f, (a + b) * 0.5f + Vector3.down * 0.4f)) continue;
            strung.Add(p);
            PropCount++;
            float size = Mathf.Clamp(Flat(p, seat) / 14f, 1f, 2.2f);
            mb.Box(a, new Vector3(0.08f, 0.08f, 0.08f) * size, dark); // the hooks
            mb.Box(b, new Vector3(0.08f, 0.08f, 0.08f) * size, dark);
            String(a, b, size, lanterns, span * 0.08f);
            made++;
        }
    }

    // The nearest wall of a library building from a street point, sideways: where the ray meets it, and its top.
    private bool WallHit(Vector3 p, Vector3 side, out Vector3 hit, out float top)
    {
        hit = default; top = 0f;
        float best = 9f;
        var ray = new Ray(new Vector3(p.x, 3.5f, p.z), side);
        foreach (var w in walls)
            if (w.IntersectRay(ray, out float dist) && dist > 0.5f && dist < best && w.max.y > 3.8f)
            { best = dist; hit = ray.GetPoint(dist) - side * 0.05f; top = w.max.y; }
        return best < 9f;
    }

    // A sagging string of alternating pennants (and now and then a lantern) between two points.
    private void String(Vector3 from, Vector3 to, float size, bool lanterns, float sag)
    {
        float len = Vector3.Distance(from, to);
        int flags = Mathf.Max(4, Mathf.RoundToInt(len / (0.62f * size)));
        var rot = Quaternion.LookRotation((to - from).normalized, Vector3.up) * Quaternion.Euler(0f, 90f, 0f);
        Vector3 prev = from;
        for (int i = 1; i <= flags; i++)
        {
            float u = i / (float)flags;
            Vector3 q = Vector3.Lerp(from, to, u) + Vector3.down * (Mathf.Sin(u * Mathf.PI) * sag);
            Segment(prev, q, 0.025f * size, dark);
            if (i < flags)
            {
                mb.Frustum(q + Vector3.down * 0.24f * size, 0.02f, 0.02f, 0.36f * size, 0.025f, 0.44f * size, i % 2 == 0 ? cloth : cloth2, rot);
                if (lanterns && i % 3 == 1) Lantern(q + Vector3.down * 0.34f * size, 0.12f * size);
            }
            prev = q;
        }
    }

    // Beside a walking line (within `within` of its edge), and free for a prop of radius r.
    private IEnumerable<(Vector3 p, Vector3 foot, Vector3 dir)> Roadside(float r, float height, float within)
    {
        foreach (var p in candidates)
        {
            float edge = NearestWalk(p, out var foot, out var dir);
            if (edge < r || edge > r + within) continue;
            if (!Free(p, r, height)) continue;
            yield return (p, foot, dir);
        }
    }

    // Roadside spots first; once those run out, open ground (facing the nearest street, or the seat).
    private IEnumerable<(Vector3 p, Vector3 foot, Vector3 dir)> Places(float r, float height, float within)
    {
        foreach (var x in Roadside(r, height, within)) yield return x;
        foreach (var p in candidates)
        {
            if (!Free(p, r, height)) continue;
            float edge = NearestWalk(p, out var foot, out var dir);
            if (edge > 12f) { foot = seat; dir = new Vector3(seat.z - p.z, 0f, p.x - seat.x).normalized; }
            yield return (p, foot, dir);
        }
    }

    private void LanternPosts(int max)
    {
        int made = 0;
        foreach (var (p, foot, _) in Roadside(0.3f, 3f, 1.5f))
        {
            if (made >= max) break;
            Take(p, 1.5f); // (spaced out along the lanes)
            Pole(p, 2.6f);
            Vector3 toward = new Vector3(foot.x - p.x, 0f, foot.z - p.z).normalized;
            Vector3 tip = p + Vector3.up * 2.45f + toward * 0.45f;
            Segment(p + Vector3.up * 2.45f, tip, 0.04f, dark);
            Lantern(tip + Vector3.down * 0.25f, 0.15f);
            made++;
        }
    }

    // ---- banners: tall poles with long hanging cloth lining the lanes ------------------------------------------------
    private void Banners(int max, bool elven)
    {
        int made = 0;
        foreach (var (p, _, _) in Roadside(0.35f, 5.2f, 1.5f))
        {
            if (made >= max) break;
            Take(p, 2.5f); // (spaced out, never two side by side)
            float h = elven ? 4.6f : 5.2f;
            Pole(p, h, elven ? wood : dark);
            var face = Quaternion.LookRotation(new Vector3(seat.x - p.x, 0f, seat.z - p.z).normalized, Vector3.up); // show the seat its face
            Vector3 bar = p + Vector3.up * (h - 0.25f);
            mb.Box(bar, new Vector3(1.1f, 0.06f, 0.06f), elven ? gold : dark, face);
            float bh = elven ? 2.0f : 2.4f;
            mb.Box(bar + Vector3.down * (bh * 0.5f + 0.05f) + face * Vector3.forward * 0.06f, new Vector3(0.95f, bh, 0.03f), elven ? green : cloth, face);
            // Emblem: a gold diamond, and a pointed hem.
            mb.Box(bar + Vector3.down * (bh * 0.45f) + face * Vector3.forward * 0.085f, new Vector3(0.34f, 0.34f, 0.02f), gold, face * Quaternion.Euler(0f, 0f, 45f));
            mb.Frustum(bar + Vector3.down * (bh + 0.2f) + face * Vector3.forward * 0.06f, 0.02f, 0.03f, 0.95f, 0.03f, 0.3f, elven ? green : cloth, face);
            if (!elven) mb.Ball(p + Vector3.up * (h + 0.1f), 0.12f, gold);
            else mb.Frustum(p + Vector3.up * (h + 0.2f), 0.16f, 0.03f, 0.01f, 0.03f, 0.4f, gold, face);
            made++;
        }
    }

    // ---- market stalls: posts, counter, striped canopy, goods - facing the lane ---------------------------------------
    private void Stalls(System.Random rng, int max)
    {
        int made = 0;
        foreach (var (p, foot, _) in Places(1.3f, 2.4f, 2.5f))
        {
            if (made >= max) break;
            Take(p, 1.3f);
            var face = Quaternion.LookRotation(new Vector3(foot.x - p.x, 0f, foot.z - p.z).normalized, Vector3.up);
            Vector3 F(float x, float y, float z) => p + face * new Vector3(x, y, z);
            foreach (var c in new[] { new Vector2(-0.9f, -0.5f), new Vector2(0.9f, -0.5f), new Vector2(-0.9f, 0.5f), new Vector2(0.9f, 0.5f) })
                mb.Box(F(c.x, c.y > 0 ? 1.05f : 1.15f, c.y), new Vector3(0.08f, c.y > 0 ? 2.1f : 2.3f, 0.08f), wood, face);
            mb.Box(F(0f, 0.85f, 0.35f), new Vector3(2f, 0.1f, 0.6f), wood, face);   // counter
            mb.Box(F(0f, 0.42f, 0.35f), new Vector3(1.9f, 0.8f, 0.05f), dark, face); // its front
            for (int i = 0; i < 5; i++) // striped canopy, sloping to the front
                mb.Box(F(-0.8f + i * 0.4f, 2.22f, 0f), new Vector3(0.4f, 0.06f, 1.4f), i % 2 == 0 ? cloth : cloth2, face * Quaternion.Euler(-12f, 0f, 0f));
            for (int i = 0; i < 5; i++) // goods
            {
                int paint = i % 3 == 0 ? straw : i % 3 == 1 ? green : cloth;
                mb.Ball(F(-0.7f + i * 0.35f, 0.99f, 0.3f + (float)rng.NextDouble() * 0.1f), 0.1f + (float)rng.NextDouble() * 0.04f, paint);
            }
            made++;
        }
    }

    // ---- carts (harvest / market) and covered dwarven wagons with ore - parked along the lane ---------------------------
    private void Carts(System.Random rng, int max, bool wagon)
    {
        int made = 0;
        float r = wagon ? 1.5f : 1.1f;
        foreach (var (p, _, dir) in Places(r, 2f, 2f))
        {
            if (made >= max) break;
            Take(p, r + 1f);
            var rot = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0f, (float)rng.NextDouble() * 16f - 8f + (rng.NextDouble() < 0.5 ? 180f : 0f), 0f);
            Vector3 F(float x, float y, float z) => p + rot * new Vector3(x, y, z);
            float L = wagon ? 2.3f : 1.6f, W = wagon ? 1.15f : 0.95f;
            mb.Box(F(0f, 0.62f, 0f), new Vector3(W, 0.08f, L), wood, rot);                 // bed
            mb.Box(F(-W * 0.5f, 0.82f, 0f), new Vector3(0.06f, 0.36f, L), wood, rot);      // sides
            mb.Box(F(W * 0.5f, 0.82f, 0f), new Vector3(0.06f, 0.36f, L), wood, rot);
            mb.Box(F(0f, 0.82f, -L * 0.5f), new Vector3(W, 0.36f, 0.06f), wood, rot);
            mb.Box(F(0f, 0.82f, L * 0.5f), new Vector3(W, 0.36f, 0.06f), wood, rot);
            var wheelRot = rot * Quaternion.Euler(0f, 0f, 90f);
            foreach (float z in new[] { -L * 0.28f, L * 0.28f })
                foreach (float x in new[] { -W * 0.5f - 0.08f, W * 0.5f + 0.08f })
                    mb.Prism(F(x, 0.38f, z), 8, 0.38f, 0.38f, 0.08f, dark, wheelRot);
            mb.Box(F(0f, 0.55f, L * 0.5f + 0.55f), new Vector3(0.07f, 0.07f, 1.1f), wood, rot); // shaft
            if (wagon)
            {
                // A canvas cover, and ore heaped at the open back.
                mb.Prism(F(0f, 1.05f, -0.15f), 6, 0.62f, 0.62f, L * 0.75f, canvas, rot * Quaternion.Euler(90f, 0f, 0f));
                for (int i = 0; i < 6; i++)
                    mb.Ball(F(-0.3f + (i % 3) * 0.3f, 0.8f + (i / 3) * 0.12f, L * 0.36f), 0.14f + (float)rng.NextDouble() * 0.05f, i % 2 == 0 ? ore : iron);
            }
            else
            {
                // Load: sacks and hay.
                for (int i = 0; i < 4; i++)
                    mb.Ball(F(-0.22f + (i % 2) * 0.44f, 0.85f + (i / 2) * 0.12f, -0.35f + (i / 2) * 0.55f), 0.24f, i % 2 == 0 ? straw : canvas, 0.8f);
            }
            made++;
        }
    }

    // ---- harvest: sheaves stood in little clusters (on open grass too), a bale or two ---------------------------------
    private void Sheaves(System.Random rng, int max)
    {
        int made = 0;
        foreach (var p in candidates)
        {
            if (made >= max) break;
            if (!Free(p, 0.9f, 1.2f)) continue;
            Take(p, 1.8f);
            int n = 3 + rng.Next(3);
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n + (float)rng.NextDouble() * 0.4f;
                Vector3 q = p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.35f;
                var lean = Quaternion.Euler(Mathf.Cos(a) * -8f, 0f, Mathf.Sin(a) * 8f);
                mb.Prism(q + Vector3.up * 0.5f, 6, 0.26f, 0.1f, 1.0f, i % 2 == 0 ? straw : strawDark, lean);
                mb.Prism(q + Vector3.up * 0.55f, 6, 0.14f, 0.14f, 0.07f, dark, lean); // the tie
            }
            if (rng.NextDouble() < 0.5) mb.Box(p + new Vector3(0.75f, 0.3f, 0.2f), new Vector3(0.8f, 0.6f, 0.55f), straw, Quaternion.Euler(0f, rng.Next(0, 90), 0f));
            made++;
        }
    }

    // ---- bits -----------------------------------------------------------------------------------------------------
    private void Pole(Vector3 foot, float height, int paint = -1)
    {
        mb.Prism(foot + Vector3.up * (height * 0.5f), 6, 0.07f, 0.055f, height, paint < 0 ? wood : paint);
        mb.Prism(foot + Vector3.up * 0.08f, 6, 0.16f, 0.12f, 0.16f, dark); // a stone foot
    }

    private void Segment(Vector3 a, Vector3 b, float thick, int paint)
    {
        Vector3 d = b - a;
        if (d.sqrMagnitude < 1e-4f) return;
        mb.Box((a + b) * 0.5f, new Vector3(thick, thick, d.magnitude), paint, Quaternion.LookRotation(d.normalized, Vector3.up));
    }

    private void Lantern(Vector3 centre, float size)
    {
        mb.Box(centre + Vector3.up * (size * 0.95f), new Vector3(size * 1.3f, size * 0.25f, size * 1.3f), dark);
        mb.Box(centre, new Vector3(size, size * 1.5f, size), glow);
        mb.Box(centre - Vector3.up * (size * 0.85f), new Vector3(size * 1.1f, size * 0.2f, size * 1.1f), dark);
    }
}
