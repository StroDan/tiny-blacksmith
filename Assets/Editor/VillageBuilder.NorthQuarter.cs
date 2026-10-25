using System.Collections.Generic;
using UnityEngine;

// The river quarter north of the town bridge (part of VillageBuilder, rebuilt with the "City" root) - so the river
// runs on between houses past the town bridge instead of out into empty grass, like the menu art's river city:
//  - a cobbled river street up the west bank from the town bridge, a row of town houses facing the water along it
//  - the north road moved a little east (the river is wider here) with a row of houses facing the water across it
//  - a timber trestle bridge at NorthBridgeZ tying the two banks together (the sixth crossing), with its street
//    running on between the rows either side
//  - quay walls (QuayHere reaches NorthQuarterEnd), moored boats and a landing stage
// Also: the landing stage + boats on the east quay in the seat's own view of the river (SeatLanding).
public static partial class VillageBuilder
{
    private const float NorthBridgeZ = 104f, NorthQuarterEnd = 138f;

    // The north road out of town, from the city street at the stone bridge (BuildCityStreets and the crowd's graph).
    private static readonly Vector2[] NorthRoad =
    {
        new Vector2(24f, 41.5f), new Vector2(24.5f, 60f), new Vector2(26f, 78f), new Vector2(30.5f, 96f), new Vector2(32.5f, 112f),
        new Vector2(34f, 130f), new Vector2(38f, 170f), new Vector2(46f, 230f), new Vector2(52f, 300f), new Vector2(60f, 420f),
    };
    // The west bank's river street, on from the town bridge's west end.
    private static readonly Vector2[] WestRiverStreet =
    {
        new Vector2(7.4f, TownBridgeZ), new Vector2(8.8f, 80f), new Vector2(10.2f, 96f), new Vector2(10.6f, NorthBridgeZ),
        new Vector2(10.8f, 112f), new Vector2(11.6f, 134f),
    };

    private static void BuildNorthQuarter(Transform root)
    {
        var q = new GameObject("NorthQuarter").transform;
        q.SetParent(root, false);

        // ---- the timber bridge ----
        StreamAt(NorthBridgeZ, out float bx, out float bw);
        float half = bw * 0.5f + 2.2f;
        var bb = new MeshBuilder();
        var bp = new CityPal(bb);
        var bvp = new Pal(bb);
        TimberBridge(bb, bp, bvp, new Vector3(bx, 0f, NorthBridgeZ), half, bw * 0.5f, 3.4f, TimberLift, TimberArch);
        bridges.Add((new Vector3(bx, 0f, NorthBridgeZ), half, 1.7f, TimberLift, TimberArch));
        obstacles.Add(Obstacle.Segment(new Vector3(bx - half, 0f, NorthBridgeZ), new Vector3(bx + half, 0f, NorthBridgeZ), 2.4f));
        Place(q, "NorthBridge", bb.Build("NorthBridge"));

        // ---- streets: the west river street, the bridge street across both rows ----
        var sb = new MeshBuilder();
        var sp = new CityPal(sb);
        for (int i = 0; i + 1 < WestRiverStreet.Length; i++) Street(sb, sp, WestRiverStreet[i], WestRiverStreet[i + 1], 3.0f);
        Street(sb, sp, new Vector2(-1f, NorthBridgeZ), new Vector2(bx - half + 0.2f, NorthBridgeZ), 3.0f);
        Street(sb, sp, new Vector2(bx + half - 0.2f, NorthBridgeZ), new Vector2(44f, NorthBridgeZ), 3.0f);
        for (int i = 0; i + 1 < WestRiverStreet.Length; i++)
            obstacles.Add(Obstacle.Segment(new Vector3(WestRiverStreet[i].x, 0f, WestRiverStreet[i].y), new Vector3(WestRiverStreet[i + 1].x, 0f, WestRiverStreet[i + 1].y), 1.6f));
        Place(q, "NorthStreets", sb.Build("NorthStreets"));

        // ---- rows of houses facing the water (split at the bridge street) ----
        int n = 0;
        var rng = new System.Random(9091);
        var rows = new[]
        {
            (name: "WestRiverRowA", from: new Vector3(5.4f, 0f, 67f), to: new Vector3(7.6f, 0f, 99.2f)),
            (name: "WestRiverRowB", from: new Vector3(8.0f, 0f, 108.8f), to: new Vector3(9.2f, 0f, 135f)),
            (name: "EastRiverRowA", from: new Vector3(30.2f, 0f, 81f), to: new Vector3(33.4f, 0f, 99.2f)),
            (name: "EastRiverRowB", from: new Vector3(35.0f, 0f, 108.8f), to: new Vector3(38.0f, 0f, 135f)),
        };
        foreach (var r in rows)
        {
            Vector3 along = (r.to - r.from).normalized;
            bool west = r.from.x < bx;
            // fronts to the river: east for the west bank, west for the east bank
            Vector3 facing = west ? new Vector3(along.z, 0f, -along.x) : new Vector3(-along.z, 0f, along.x);
            if (useLibrary)
            {
                var parent = new GameObject(r.name).transform;
                parent.SetParent(q, false);
                n += FrontagePrefabs(parent, rng, r.from, r.to, facing, 6f, 2, TownPool, null).count;
                continue;
            }
            var b = new MeshBuilder();
            var p = new CityPal(b);
            n += Frontage(b, p, rng, r.from, r.to, facing, 9f, 2, 3);
            Place(q, r.name, b.Build("NorthRow"));
        }

        // ---- on the water: boats at a landing stage on each bank, lanterns along the west street ----
        var mb = new MeshBuilder();
        var mp = new Pal(mb);
        StreamAt(88f, out float wx, out float ww);
        Landing(mb, mp, 84f, 92f, false);
        Boat(mb, mp, new Vector3(wx - ww * 0.5f + 1.6f, 0f, 88.5f), 176f);
        Boat(mb, mp, new Vector3(wx - ww * 0.5f + 2.9f, 0f, 86.2f), 186f);
        StreamAt(120f, out float ex, out float ew);
        Landing(mb, mp, 116f, 124f, true);
        Boat(mb, mp, new Vector3(ex + ew * 0.5f - 1.6f, 0f, 121f), 4f);
        Crate(new Put(mb, new Vector3(ex + ew * 0.5f + 1.4f, 0f, 117.5f), 14f), mp, 0.6f);
        Barrel(new Put(mb, new Vector3(ex + ew * 0.5f + 1.3f, 0f, 118.6f), 0f), mp);
        for (int i = 1; i + 1 < WestRiverStreet.Length; i++)
            if (i != 3) // not on the bridge corner
                LanternPost(new Put(mb, new Vector3(WestRiverStreet[i].x + 1.8f, 0f, WestRiverStreet[i].y + 2f), 180f), mp);
        Place(q, "NorthRiverProps", mb.Build("NorthRiverProps"));

        // ---- trees on the bridge corners (clear of the street and the rows) ----
        if (useLibrary)
        {
            int t = 0;
            var trng = new System.Random(515);
            foreach (var at in new[] { new Vector3(3.2f, 0f, NorthBridgeZ + 3.1f), new Vector3(3.6f, 0f, NorthBridgeZ - 3.2f), new Vector3(38.6f, 0f, NorthBridgeZ - 3.0f), new Vector3(39.2f, 0f, NorthBridgeZ + 3.2f) })
            {
                var tree = Spawn(TreePrefabs[(t * 2 + 3) % TreePrefabs.Length], q, at, (float)trng.NextDouble() * 360f, Vector3.one * (0.9f + (float)trng.NextDouble() * 0.2f));
                if (tree != null) { tree.name = "Tree_North" + t++; obstacles.Add(Obstacle.Circle(at, 1.4f)); }
            }
        }
        Debug.Log($"[City] north river quarter: {n} buildings, timber bridge at z {NorthBridgeZ} ({half * 2f:0.0} m)");
    }

    // A plank landing stage along the water's edge of a quay between z0 and z1 (east = the east bank), on piles.
    private static void Landing(MeshBuilder b, Pal p, float z0, float z1, bool east)
    {
        const float w = 0.95f;
        int steps = Mathf.Max(2, Mathf.CeilToInt((z1 - z0) / 1.6f));
        for (int k = 0; k < steps; k++)
        {
            float za = Mathf.Lerp(z0, z1, k / (float)steps), zb = Mathf.Lerp(z0, z1, (k + 1f) / steps);
            StreamAt(za, out float xa, out float wa);
            StreamAt(zb, out float xb, out float wb2);
            float s = east ? 1f : -1f;
            Vector3 ea = new Vector3(xa + s * (wa * 0.5f - w * 0.5f), 0f, za), eb = new Vector3(xb + s * (wb2 * 0.5f - w * 0.5f), 0f, zb);
            var rot = Quaternion.LookRotation(eb - ea);
            b.Box((ea + eb) * 0.5f + Vector3.up * 0.34f, new Vector3(w, 0.07f, Vector3.Distance(ea, eb) + 0.04f), k % 2 == 0 ? p.woodLight : p.wood, rot);
            b.Box(ea + Vector3.up * 0.1f - new Vector3(s * (w * 0.5f - 0.08f), 0f, 0f), new Vector3(0.15f, 0.62f, 0.15f), p.woodDark);
        }
        // mooring posts at the ends
        foreach (float z in new[] { z0, z1 })
        {
            StreamAt(z, out float x, out float ww);
            b.Box(new Vector3(x + (east ? 1f : -1f) * (ww * 0.5f - w + 0.08f), 0.3f, z), new Vector3(0.17f, 0.75f, 0.17f), p.woodDark);
        }
    }

    // ---- the timber bridge ----------------------------------------------------------------------------------------

    private const float TimberLift = 0.22f, TimberArch = 0.9f; // deck = lift + arch * (1 - (x/half)^2): DeckHeightAt's profile

    // A timber trestle bridge along X: a plank deck on stringers, pile bents across the water with cross braces, a rail
    // either side, stone abutments on the banks and a lantern at each end.
    private static void TimberBridge(MeshBuilder b, CityPal p, Pal vp, Vector3 c, float half, float waterHalf, float width, float lift, float arch)
    {
        float Deck(float x) => lift + arch * (1f - (x / half) * (x / half));
        const int n = 22;
        for (int i = 0; i < n; i++)
        {
            float x0 = -half + i * 2f * half / n, x1 = x0 + 2f * half / n, xm = (x0 + x1) * 0.5f;
            var rot = Quaternion.Euler(0f, 0f, Mathf.Atan2(Deck(x1) - Deck(x0), x1 - x0) * Mathf.Rad2Deg);
            float dm = Deck(xm);
            b.Box(c + new Vector3(xm, dm - 0.06f, 0f), new Vector3(x1 - x0 + 0.03f, 0.12f, width), i % 2 == 0 ? vp.woodLight : vp.wood, rot);
            foreach (float s in new[] { -1f, 0f, 1f }) // stringers
                b.Box(c + new Vector3(xm, dm - 0.24f, s * (width * 0.5f - 0.25f)), new Vector3(x1 - x0 + 0.05f, 0.26f, 0.22f), vp.woodDark, rot);
            foreach (float s in new[] { -1f, 1f }) // hand rail and mid rail
            {
                b.Box(c + new Vector3(xm, dm + 0.95f, s * (width * 0.5f - 0.06f)), new Vector3(x1 - x0 + 0.04f, 0.1f, 0.12f), vp.woodLight, rot);
                b.Box(c + new Vector3(xm, dm + 0.5f, s * (width * 0.5f - 0.06f)), new Vector3(x1 - x0 + 0.04f, 0.07f, 0.07f), vp.woodDark, rot);
            }
        }
        // rail posts
        for (float x = -half + 0.4f; x <= half - 0.35f; x += 1.55f)
            foreach (float s in new[] { -1f, 1f })
                b.Box(c + new Vector3(x, Deck(x) + 0.5f, s * (width * 0.5f - 0.06f)), new Vector3(0.14f, 1.0f, 0.14f), vp.woodDark);
        // pile bents in the water
        int bents = Mathf.Max(2, Mathf.RoundToInt(waterHalf * 2f / 3.2f));
        for (int k = 1; k < bents; k++)
        {
            float x = -waterHalf + k * 2f * waterHalf / bents;
            float top = Deck(x) - 0.37f;
            foreach (float s in new[] { -1f, 0f, 1f })
                b.Box(c + new Vector3(x, (top - 0.4f) * 0.5f, s * (width * 0.5f - 0.2f)), new Vector3(0.26f, top + 0.4f, 0.26f), vp.woodDark);
            b.Box(c + new Vector3(x, top - 0.08f, 0f), new Vector3(0.3f, 0.18f, width + 0.3f), vp.wood); // cap beam
            float brace = Mathf.Atan2(top - 0.25f, width * 0.5f) * Mathf.Rad2Deg;
            foreach (float s in new[] { -1f, 1f })
                b.Box(c + new Vector3(x, top * 0.5f + 0.1f, s * width * 0.25f), new Vector3(0.1f, 0.12f, Mathf.Sqrt(width * width * 0.25f + (top - 0.25f) * (top - 0.25f))), vp.wood, Quaternion.Euler(s * brace, 0f, 0f));
            // cutwater logs upstream/downstream, so the bent reads as standing in a current
            foreach (float s in new[] { -1f, 1f })
                b.Box(c + new Vector3(x, 0.1f, s * (width * 0.5f + 0.25f)), new Vector3(0.22f, 0.35f, 0.5f), vp.woodDark, Quaternion.Euler(0f, 45f, 0f));
        }
        // stone abutments where the deck meets the banks
        foreach (float s in new[] { -1f, 1f })
        {
            float xa = s * (waterHalf + (half - waterHalf) * 0.45f);
            b.Box(c + new Vector3(xa, (Deck(xa) - 0.3f) * 0.5f, 0f), new Vector3(half - waterHalf + 0.4f, Deck(xa) - 0.1f, width + 0.5f), p.stone);
            foreach (float zs in new[] { -1f, 1f })
                b.Box(c + new Vector3(s * (half - 0.2f), 0.45f, zs * (width * 0.5f + 0.1f)), new Vector3(0.55f, 0.9f, 0.5f), p.stoneDark);
        }
        foreach (float s in new[] { -1f, 1f })
            LanternPost(new Put(b, c + new Vector3(s * (half - 0.7f), 0f, s * (width * 0.5f + 0.55f)), s > 0f ? 180f : 0f), vp);
    }

    // ---- the crowd -----------------------------------------------------------------------------------------------

    private static void AddNorthQuarterNav(NavBuilder nb)
    {
        StreamAt(NorthBridgeZ, out float bx, out float bw);
        float half = bw * 0.5f + 2.2f;
        nb.Street(0.9f, 9f, WestRiverStreet);
        nb.Street(0.8f, 9f, new Vector2(-1f, NorthBridgeZ), new Vector2(10.6f, NorthBridgeZ), new Vector2(bx - half + 0.3f, NorthBridgeZ), new Vector2(bx, NorthBridgeZ),
                  new Vector2(bx + half - 0.3f, NorthBridgeZ), new Vector2(31.5f, NorthBridgeZ), new Vector2(44f, NorthBridgeZ));
        var road = nb.Nearest(new Vector3(31.5f, 0f, NorthBridgeZ), 6f);
        if (road != null) NavBuilder.Link(nb.Node(new Vector2(31.5f, NorthBridgeZ)), road);
        // the town bridge's west end joins the river street
        StreamAt(TownBridgeZ, out float tx, out float tw);
        NavBuilder.Link(nb.Node(new Vector2(7.4f, TownBridgeZ)), nb.Node(WestRiverStreet[0]));
        // someone watching the boats from each landing stage
        StreamAt(88f, out float wx, out float ww);
        var wNear = nb.Nearest(new Vector3(8f, 0f, 88f), 6f);
        if (wNear != null) Spot(nb, new Vector3(wx - ww * 0.5f + 0.2f, 0f, 90.5f), "river", Vector3.right, wNear);
        StreamAt(120f, out float ex, out float ew);
        var eNear = nb.Nearest(new Vector3(33f, 0f, 120f), 7f);
        if (eNear != null) Spot(nb, new Vector3(ex + ew * 0.5f + 0.9f, 0f, 123.5f), "river", Vector3.left, eNear);
    }
}
