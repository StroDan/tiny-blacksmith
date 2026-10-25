using System.Collections.Generic;
using UnityEngine;

// The river as the city's spine (part of VillageBuilder, rebuilt with the "City" root) - the main-menu art's river:
//  - stone quay walls along the town stretch instead of muddy banks (the smithy's own bank stays green and open, so
//    nothing hides the water from the seat), ending in a pier block wherever they stop
//  - two more bridges, each carrying a real street: a stone bridge south of the square (the road and its workshops to
//    the west bank's tannery path) and a three-arched town bridge up north (a new quay street on the west bank to the
//    north road) - five crossings in all, three or four in any one view
//  - buildings facing the water on both banks upstream, a little market on the east quay by the watermill, trees along
//    the south bank and round the plaza.
public static partial class VillageBuilder
{
    private const float SouthBridgeZ = -27f, TownBridgeZ = 64f;
    private const float WestQuayFrontX = 4.6f, EastRowFrontX = 27.8f;
    private static readonly float[] QuayStallZ = { 28.2f, 32.6f, 37.0f };
    private const float QuayStallX = 19.8f;

    private static void BuildRiverfront(Transform city)
    {
        var root = new GameObject("Riverfront").transform;
        root.SetParent(city, false);

        // ---- the south bridge: the road (and the lane into the city) over to the west bank's riverside path ----
        StreamAt(SouthBridgeZ, out float sx, out float sw);
        float sHalf = sw * 0.5f + 1.8f;
        var sb = new MeshBuilder();
        var sp = new CityPal(sb);
        StoneBridge(sb, sp, new Vector3(sx, 0f, SouthBridgeZ), sHalf, 3.0f);
        bridges.Add((new Vector3(sx, 0f, SouthBridgeZ), sHalf, 1.5f, 0.15f, 0.45f));
        obstacles.Add(Obstacle.Segment(new Vector3(sx - sHalf, 0f, SouthBridgeZ), new Vector3(sx + sHalf, 0f, SouthBridgeZ), 2f));
        Place(root, "SouthBridge", sb.Build("SouthBridge"));
        var pb = new MeshBuilder();
        var pp = new Pal(pb);
        Path(pb, pp, Line(new Vector2(0.2f, SouthBridgeZ), new Vector2(sx - sHalf + 0.2f, SouthBridgeZ)), 2.4f, new System.Random(64));
        Place(root, "SouthBridgePath", pb.Build("SouthBridgePath"));

        // ---- the town bridge: three stone arches, lanterns at the crown ----
        StreamAt(TownBridgeZ, out float tx, out float tw);
        float tHalf = tw * 0.5f + 3.0f;
        var tb = new MeshBuilder();
        var tp = new CityPal(tb);
        var tvp = new Pal(tb);
        ArchBridge(tb, tp, tvp, new Vector3(tx, 0f, TownBridgeZ), tHalf, tw * 0.5f, 4.0f, 3, TownLift, TownArch);
        bridges.Add((new Vector3(tx, 0f, TownBridgeZ), tHalf, 2.0f, TownLift, TownArch));
        obstacles.Add(Obstacle.Segment(new Vector3(tx - tHalf, 0f, TownBridgeZ), new Vector3(tx + tHalf, 0f, TownBridgeZ), 2.6f));
        Place(root, "TownBridge", tb.Build("TownBridge"));

        // The west quay street, from the third street's stone bridge up to the town bridge, and on from its east end
        // to the north road. Cobbled like the old town (it's the old town's river front).
        var qb = new MeshBuilder();
        var qp = new CityPal(qb);
        foreach (var (a, c) in WestQuayStreet(tx, tHalf))
            Street(qb, qp, a, c, 3.0f);
        Street(qb, qp, new Vector2(tx + tHalf - 0.2f, TownBridgeZ), new Vector2(24.8f, TownBridgeZ), 3.0f);
        Place(root, "QuayStreets", qb.Build("QuayStreets"));

        // ---- buildings facing the water ----
        var rows = new GameObject("RiverRows").transform;
        rows.SetParent(root, false);
        var rng = new System.Random(2718);
        int n = 0;
        // West bank: a row of tall town houses along the quay street, fronts to the river (the skyline rows behind
        // stop short of it - BuildSkylines).
        var west = (from: new Vector3(WestQuayFrontX, 0f, 44.4f), to: new Vector3(WestQuayFrontX, 0f, 62.2f), facing: Vector3.right, depth: 5.5f);
        // East bank: past the last block, along the north road, looking across the river at them.
        var east = (from: new Vector3(EastRowFrontX, 0f, 50f), to: new Vector3(EastRowFrontX, 0f, 77f), facing: Vector3.left, depth: 6f);
        foreach (var r in new[] { west, east })
        {
            if (useLibrary)
            {
                var parent = new GameObject(r.facing.x > 0f ? "WestQuayRow" : "EastRiverRow").transform;
                parent.SetParent(rows, false);
                int placed = FrontagePrefabs(parent, rng, r.from, r.to, r.facing, r.depth, 2, TownPool, null).count;
                Debug.Log($"[City] {parent.name}: {placed} buildings");
                n += placed;
                continue;
            }
            var b = new MeshBuilder();
            var p = new CityPal(b);
            n += Frontage(b, p, rng, r.from, r.to, r.facing, r.depth + 3f, 2, 3);
            Place(rows, r.facing.x > 0f ? "WestQuayRow" : "EastRiverRow", b.Build("RiverRow"));
        }

        // ---- the market quay on the east bank, between the watermill and the stone bridge ----
        var mb = new MeshBuilder();
        var mp = new Pal(mb);
        foreach (float z in QuayStallZ)
        {
            MarketStall(new Put(mb, new Vector3(QuayStallX, 0f, z), 180f), mp); // counters to the river and the quay walk
            obstacles.Add(Obstacle.Circle(new Vector3(QuayStallX, 0f, z), 1.8f));
        }
        // Stock behind the stalls (the gaps between them stay clear - shoppers walk through there from the road).
        Crate(new Put(mb, new Vector3(QuayStallX + 1.5f, 0f, 28.8f), 12f), mp, 0.6f);
        Crate(new Put(mb, new Vector3(QuayStallX + 1.4f, 0.6f, 28.9f), -8f), mp, 0.5f);
        Barrel(new Put(mb, new Vector3(QuayStallX + 1.5f, 0f, 32.2f), 0f), mp);
        Barrel(new Put(mb, new Vector3(QuayStallX + 1.6f, 0f, 36.4f), 30f), mp);
        Sack(new Put(mb, new Vector3(QuayStallX + 1.3f, 0f, 37.4f), 20f), mp);
        StreamAt(33.8f, out float jx, out float jw);
        Jetty(mb, mp, new Vector3(jx + jw * 0.5f, 0f, 33.8f), -1f);       // a landing stage for the market boats
        Boat(mb, mp, new Vector3(jx + jw * 0.5f - 2.4f, 0f, 31.8f), 188f);
        StreamAt(52f, out float fx, out float fw);
        Boat(mb, mp, new Vector3(fx - fw * 0.5f + 1.1f, 0f, 52f), 172f);  // one tied up on the far quay
        LanternPost(new Put(mb, new Vector3(QuayStallX - 1.6f, 0f, 26.4f), 0f), mp);
        // In the seat's own view across the water: a landing stage on the far quay between the two bridges, a boat at it.
        Landing(mb, mp, 2.2f, 8.6f, true);
        StreamAt(6.2f, out float lx, out float lw);
        Boat(mb, mp, new Vector3(lx + lw * 0.5f - 1.75f, 0f, 5.6f), 176f);
        Place(root, "MarketQuay", mb.Build("MarketQuay"));

        // ---- trees: round the plaza's corners and by the market (spots chosen clear of the stalls, the fountain, the
        // church and clock-house doors and the streets - the plaza's own paving rect would reject any automatic pick) ----
        var trees = new GameObject("RiverTrees").transform;
        trees.SetParent(root, false);
        var trng = new System.Random(77);
        int t = 0;
        if (useLibrary)
            foreach (var at in new[]
            {
                new Vector3(PlazaX.x + 1.3f, 0f, PlazaNorth - 1.1f), new Vector3(PlazaX.y - 1.3f, 0f, PlazaNorth - 1.1f),
                new Vector3(PlazaX.x + 1.3f, 0f, PlazaSouth + 1.1f), new Vector3(PlazaX.y - 1.3f, 0f, PlazaSouth + 1.1f),
                new Vector3(21.4f, 0f, 26.2f), new Vector3(21.7f, 0f, 38.3f),
            })
            {
                var tree = Spawn(TreePrefabs[(t * 3 + 1) % TreePrefabs.Length], trees, at, (float)trng.NextDouble() * 360f, Vector3.one * (0.85f + (float)trng.NextDouble() * 0.25f));
                tree.name = "Tree_River" + t++;
                obstacles.Add(Obstacle.Circle(at, 1.4f));
            }

        BuildNorthQuarter(root); // the river quarter past the town bridge and its timber bridge (VillageBuilder.NorthQuarter.cs)
        BuildQuays(root); // last: its gaps are wherever a bridge now lands
        Debug.Log($"[City] riverfront: {n} buildings facing the water, {t} trees, bridges now {bridges.Count}");
    }

    // The west quay street: off the stone bridge's west end, up the bank, onto the town bridge.
    private static IEnumerable<(Vector2, Vector2)> WestQuayStreet(float tx, float tHalf)
    {
        var pts = new[] { new Vector2(6.3f, 42.6f), new Vector2(6.8f, 53f), new Vector2(7.4f, TownBridgeZ), new Vector2(tx - tHalf + 0.2f, TownBridgeZ) };
        for (int i = 0; i < pts.Length - 1; i++) yield return (pts[i], pts[i + 1]);
    }

    // ---- quay walls -------------------------------------------------------------------------------------------------

    private static bool QuayHere(float z, bool east)
    {
        if (z < -62f || z > NorthQuarterEnd) return false;                    // out in the country: natural banks
        if (!east && z > -18f && z < 18f) return false;           // the smithy's own bank: green, and nothing hides the water from the seat
        if (east && Mathf.Abs(z - 22.4f) < 5.2f) return false;    // the watermill's wheel
        if (!east && z > -53f && z < -35f) return false;          // the tannery's vats and jetty
        foreach (var br in bridges) if (Mathf.Abs(z - br.centre.z) < br.halfWidth + 0.9f) return false; // abutments
        return true;
    }

    private static void BuildQuays(Transform root)
    {
        var b = new MeshBuilder();
        var p = new CityPal(b);
        int runs = 0;
        foreach (float s in new[] { -1f, 1f })
        {
            bool prev = false;
            Vector3 prevEnd = Vector3.zero, prevN = Vector3.right;
            for (int i = 0; i < stream.Count - 1; i++)
            {
                Vector3 a = stream[i], c = stream[i + 1], d = c - a;
                float len = d.magnitude;
                if (len < 1e-3f) continue;
                Vector3 dir = d / len, n = new Vector3(dir.z, 0f, -dir.x) * s;
                float w = (streamWidth[i] + streamWidth[i + 1]) * 0.5f;
                bool here = QuayHere((a.z + c.z) * 0.5f, s > 0f);
                Vector3 edgeA = a + n * (w * 0.5f + 0.2f), edgeC = c + n * (w * 0.5f + 0.2f);
                if (here != prev)
                {
                    // A pier block where a run of wall starts or stops, so it never just ends in a cut.
                    Vector3 at = here ? edgeA : prevEnd;
                    b.Box(at + Vector3.up * 0.32f, new Vector3(0.8f, 0.84f, 0.8f), p.stone, Quaternion.LookRotation(here ? dir : prevN));
                    b.Box(at + Vector3.up * 0.78f, new Vector3(0.9f, 0.1f, 0.9f), p.stoneLight, Quaternion.LookRotation(here ? dir : prevN));
                    if (here) runs++;
                }
                prev = here;
                prevEnd = edgeC;
                prevN = dir;
                if (!here) continue;
                var rot = Quaternion.LookRotation(dir);
                Vector3 mid = (edgeA + edgeC) * 0.5f;
                b.Box(mid + Vector3.up * 0.22f, new Vector3(0.56f, 0.64f, len + 0.3f), p.stone, rot);
                b.Box(mid + Vector3.up * 0.58f + n * 0.03f, new Vector3(0.72f, 0.1f, len + 0.34f), p.stoneLight, rot);
            }
        }
        Place(root, "QuayWalls", b.Build("QuayWalls"));
        Debug.Log($"[City] quay walls: {runs} runs");
    }

    // ---- the town bridge ----------------------------------------------------------------------------------------

    private const float TownLift = 0.12f, TownArch = 1.9f; // deck = lift + arch * (1 - (x/half)^2): DeckHeightAt's profile

    // A stone bridge along X with `spans` arches over the water (half-width waterHalf) and solid abutments on the banks:
    // deck and parapets on top, the spandrel walls filled down to each arch's curve, piers with cutwaters between.
    private static void ArchBridge(MeshBuilder b, CityPal p, Pal vp, Vector3 c, float half, float waterHalf, float width, int spans, float lift, float arch)
    {
        float Deck(float x) => lift + arch * (1f - (x / half) * (x / half));
        float openHalf = waterHalf + 0.3f, span = 2f * openHalf / spans, pierHalf = 0.45f;
        // The underside at x: ground/water under the abutments and piers, the arch's curve elsewhere.
        float Soffit(float x)
        {
            if (Mathf.Abs(x) >= openHalf) return -0.05f;
            float t = (x + openHalf) / span;
            float f = t - Mathf.Floor(t);
            float pf = pierHalf / span;
            bool pier = (f < pf && t > 1f) || (f > 1f - pf && t < spans - 1f);
            if (pier) return -0.05f;
            float lo = t < 1f ? 0f : pf, hi = t > spans - 1f ? 1f : 1f - pf;
            float u = Mathf.InverseLerp(lo, hi, f) * 2f - 1f;
            float spanMid = -openHalf + (Mathf.Floor(t) + 0.5f) * span;
            float rise = Mathf.Min(1.15f, Deck(spanMid) - 0.95f);
            return 0.02f + rise * Mathf.Sqrt(Mathf.Max(0f, 1f - u * u));
        }
        const int n = 28;
        for (int i = 0; i < n; i++)
        {
            float x0 = -half + i * 2f * half / n, x1 = x0 + 2f * half / n, xm = (x0 + x1) * 0.5f;
            float ang = Mathf.Atan2(Deck(x1) - Deck(x0), x1 - x0) * Mathf.Rad2Deg;
            var rot = Quaternion.Euler(0f, 0f, ang);
            float dm = Deck(xm);
            b.Box(c + new Vector3(xm, dm - 0.12f, 0f), new Vector3(x1 - x0 + 0.05f, 0.24f, width), p.cobble, rot);
            b.Box(c + new Vector3(xm, dm - 0.5f, 0f), new Vector3(x1 - x0 + 0.05f, 0.6f, width + 0.3f), p.stone, rot);
            foreach (float s in new[] { -1f, 1f })
            {
                b.Box(c + new Vector3(xm, dm + 0.32f, s * (width * 0.5f + 0.12f)), new Vector3(x1 - x0 + 0.05f, 0.64f, 0.34f), p.stoneLight, rot);
                b.Box(c + new Vector3(xm, dm + 0.68f, s * (width * 0.5f + 0.12f)), new Vector3(x1 - x0 + 0.06f, 0.08f, 0.42f), p.stoneDark, rot);
            }
            // Spandrel: solid stone from the arch's curve up to the underside of the deck.
            float top = dm - 0.8f, bottom = Soffit(xm);
            if (top - bottom > 0.02f)
                b.Box(c + new Vector3(xm, (top + bottom) * 0.5f, 0f), new Vector3(x1 - x0 + 0.02f, top - bottom, width + 0.3f), p.stone);
        }
        // Piers with pointed cutwaters, up and down stream.
        for (int k = 1; k < spans; k++)
        {
            float px = -openHalf + k * span;
            float h = Deck(px) - 0.8f;
            foreach (float s in new[] { -1f, 1f })
                b.Prism(c + new Vector3(px, h * 0.5f - 0.05f, s * (width * 0.5f + 0.15f)), 4, 0.62f, 0.5f, h + 0.1f, p.stoneDark);
        }
        // Abutment ends: squared-off stone blocks where the parapets meet the banks.
        foreach (float s in new[] { -1f, 1f })
            foreach (float zs in new[] { -1f, 1f })
                b.Box(c + new Vector3(s * (half - 0.25f), 0.45f, zs * (width * 0.5f + 0.12f)), new Vector3(0.6f, 0.9f, 0.5f), p.stoneDark);
        // Lanterns at the crown.
        foreach (float zs in new[] { -1f, 1f })
            LanternPost(new Put(b, c + new Vector3(0f, Deck(0f), zs * (width * 0.5f - 0.3f)), zs > 0f ? 180f : 0f), vp);
    }

    // ---- the crowd: new streets over the bridges, market spots on the quay ------------------------------------------

    private static void AddRiverfrontNav(NavBuilder nb)
    {
        Vector2 V(float x, float z) => new Vector2(x, z);
        // South bridge: road -> deck -> the west bank's riverside path.
        StreamAt(SouthBridgeZ, out float sx, out float sw);
        float sHalf = sw * 0.5f + 1.8f;
        nb.Street(0.7f, 9f, V(16.8f, SouthBridgeZ), V(sx + sHalf - 0.3f, SouthBridgeZ), V(sx, SouthBridgeZ), V(sx - sHalf + 0.3f, SouthBridgeZ), V(0.2f, SouthBridgeZ));
        NavBuilder.Link(nb.Node(V(0.2f, SouthBridgeZ)), nb.Node(V(-0.4f, -24f)));
        NavBuilder.Link(nb.Node(V(0.2f, SouthBridgeZ)), nb.Node(V(1.2f, -33f)));
        // The west quay street off the stone bridge, over the town bridge, to the north road.
        StreamAt(41.5f, out float n3x, out float n3w);
        float n3half = n3w * 0.5f + 1.8f;
        StreamAt(TownBridgeZ, out float tx, out float tw);
        float tHalf = tw * 0.5f + 3.0f;
        nb.Street(1.0f, 9f, V(n3x - n3half + 0.3f, 41.5f), V(6.3f, 42.6f), V(6.8f, 53f), V(7.4f, TownBridgeZ), V(tx - tHalf + 0.3f, TownBridgeZ),
                  V(tx, TownBridgeZ), V(tx + tHalf - 0.3f, TownBridgeZ), V(24.8f, TownBridgeZ), V(24.5f, 60f));
        // The market quay: shoppers at the gaps between the stalls, and someone watching the boats from the quay.
        for (int i = 0; i < QuayStallZ.Length - 1; i++)
        {
            float z = (QuayStallZ[i] + QuayStallZ[i + 1]) * 0.5f;
            var near = NearestDry(nb, new Vector3(24f, 0f, z), 6f);
            if (near != null) Spot(nb, new Vector3(QuayStallX - 1.5f, 0f, z), "market", Vector3.right, near);
        }
        StreamAt(39.2f, out float qx, out float qw);
        var bank = NearestDry(nb, new Vector3(24f, 0f, 39.2f), 8f);
        if (bank != null) Spot(nb, new Vector3(qx + qw * 0.5f + 0.9f, 0f, 39.2f), "river", Vector3.left, bank);
        AddNorthQuarterNav(nb);
    }
}
