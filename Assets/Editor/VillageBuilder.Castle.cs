using System.Collections.Generic;
using UnityEngine;

// The castle on its hill (part of VillageBuilder, rebuilt with the "City" root) - the main-menu art's hero landmark:
// a big green hill out past the city wall, a rocky brow toward the city with the castle standing near its edge, a
// road switching back up the city-facing slope on walled terraces to a lower gatehouse on the south-west, a bailey
// wall with red-roofed towers round the plateau's flanks and back (left open toward the city, so from the seat nothing
// stands between the avenue and the castle's own gate), and trees scattered down the slopes.
public static partial class VillageBuilder
{
    private const float CastlePlateauR = 43f;
    private const float CastleShelf = 8f; // the drop under the castle's walled base on the city face
    private static readonly Vector2 CastleRoadStart = new Vector2(116f, AvenueZ);
    // The road up (x, z): zig-zags across the west face, then round to the gatehouse on the south-west of the plateau.
    private static readonly Vector2[] CastleRoad =
    {
        new Vector2(116f, -3.5f), new Vector2(123f, -24f), new Vector2(130f, 22f), new Vector2(135f, -20f),
        new Vector2(139f, 16f), new Vector2(141f, -24f), new Vector2(145.5f, -27.5f),
    };

    // Where the hill's foot is, by direction from the castle: close on the city side (west), further out behind.
    private static float CastleFootR(float theta) => 69f - 11f * Mathf.Cos(theta - Mathf.PI);

    // Ground height on the castle hill (0 off it).
    private static float CastleHillHeight(float x, float z)
    {
        float dx = x - CastleCenter.x, dz = z - CastleCenter.z;
        float r = Mathf.Sqrt(dx * dx + dz * dz);
        if (r <= CastlePlateauR) return CastleBase;
        float foot = CastleFootR(Mathf.Atan2(dz, dx));
        float t = (r - CastlePlateauR) / (foot - CastlePlateauR);
        if (t >= 1f) return 0f;
        float noise = (Mathf.PerlinNoise(x * 0.06f + 11.3f, z * 0.06f + 4.1f) - 0.5f) * 5f;
        float h = CastleBase * (0.5f + 0.5f * Mathf.Cos(Mathf.PI * t)) + noise * Mathf.Sin(Mathf.PI * t);
        // On the city face the brow drops sheer to a shelf CastleShelf below the plateau - the face the castle's walled
        // base stands against (fading out before the road's hairpins either side).
        float off = Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(dz, dx) * Mathf.Rad2Deg, 180f));
        float arc = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(15f, 23f, off));
        return arc > 0f ? Mathf.Min(h, CastleBase - CastleShelf * arc) : h;
    }

    private static void BuildCastle(Transform city)
    {
        var b = new MeshBuilder();
        var p = new CityPal(b);
        int grass = b.Paint(C(0.38f, 0.54f, 0.27f), 0.05f), grassDark = b.Paint(C(0.31f, 0.46f, 0.23f), 0.05f), grassLight = b.Paint(C(0.46f, 0.60f, 0.30f), 0.05f);
        int rock = b.Paint(C(0.55f, 0.53f, 0.49f), 0.08f), rockDark = b.Paint(C(0.43f, 0.42f, 0.40f), 0.08f);
        Vector3 c = CastleCenter;

        // ---- the hill: a polar grid, flat plateau, eased slopes; steep facets show rock ----
        const int S = 72;
        var radii = new List<float> { 0f, 14f, 28f, CastlePlateauR };
        const int slopeRings = 10;
        var rows = new List<Vector3[]>();
        var rng = new System.Random(8);
        foreach (float r0 in radii) rows.Add(Ring(r0, 0));
        for (int k = 1; k <= slopeRings + 1; k++) rows.Add(Ring(-1f, k));
        Vector3[] Ring(float r0, int k)
        {
            var ring = new Vector3[S];
            for (int i = 0; i < S; i++)
            {
                float th = i * Mathf.PI * 2f / S;
                float foot = CastleFootR(th);
                float r = r0 >= 0f ? r0 : k <= slopeRings ? Mathf.Lerp(CastlePlateauR, foot, k / (float)slopeRings) : foot + 8f;
                if (r0 < 0f && k < slopeRings) r += (float)(rng.NextDouble() - 0.5) * 1.6f; // uneven facets
                var at = new Vector3(c.x + Mathf.Cos(th) * r, 0f, c.z + Mathf.Sin(th) * r);
                at.y = k > slopeRings ? -0.4f : CastleHillHeight(at.x, at.z) + 0.02f;
                ring[i] = at;
            }
            return ring;
        }
        for (int j = 0; j + 1 < rows.Count; j++)
            for (int i = 0; i < S; i++)
            {
                int i2 = (i + 1) % S;
                Vector3 a = rows[j][i], bb = rows[j][i2], cc = rows[j + 1][i2], d = rows[j + 1][i];
                foreach (var (t0, t1, t2) in new[] { (a, bb, cc), (a, cc, d) })
                {
                    Vector3 n = Vector3.Cross(t1 - t0, t2 - t0);
                    if (n.sqrMagnitude < 1e-8f) continue;
                    n.Normalize();
                    Vector3 mid = (t0 + t1 + t2) / 3f;
                    int paint;
                    // green slopes, like the menu art's hill; bare rock only on the steepest facets and in scattered patches
                    float patch = Mathf.PerlinNoise(mid.x * 0.09f + 3f, mid.z * 0.09f + 9f);
                    bool bare = n.y < 0.4f || (n.y < 0.62f && patch > 0.64f);
                    if (bare) paint = patch > 0.5f ? rock : rockDark;
                    else if (mid.y > CastleBase - 0.5f) paint = grass;
                    else paint = Mathf.PerlinNoise(mid.x * 0.08f, mid.z * 0.08f) > 0.55f ? grassDark : mid.y < 5f ? grassLight : grass;
                    b.Triangle(t0, t1, t2, paint);
                }
            }
        // A rocky brow under the castle on the city side (the castle stands on its rock).
        for (int i = 0; i < 14; i++)
        {
            float th = Mathf.Lerp(148f, 212f, i / 13f) * Mathf.Deg2Rad;
            float r = CastlePlateauR + 1.5f + (float)rng.NextDouble() * 4f;
            var at = new Vector3(c.x + Mathf.Cos(th) * r, 0f, c.z + Mathf.Sin(th) * r);
            if (DistanceToRoad(at) < 3.5f) continue;
            at.y = CastleHillHeight(at.x, at.z) - 0.6f;
            b.Ball(at, 2.2f + (float)rng.NextDouble() * 2.2f, rng.NextDouble() < 0.5 ? rock : rockDark, 0.7f);
        }

        // ---- the road: cobbles on a stone-faced terrace cut into the slope ----
        for (int s = 0; s + 1 < CastleRoad.Length; s++)
        {
            Vector3 a = new Vector3(CastleRoad[s].x, 0f, CastleRoad[s].y), e = new Vector3(CastleRoad[s + 1].x, 0f, CastleRoad[s + 1].y);
            int steps = Mathf.CeilToInt(Vector3.Distance(a, e) / 2.2f);
            Vector3 dir = (e - a).normalized, side = new Vector3(dir.z, 0f, -dir.x);
            for (int k = 0; k < steps; k++)
            {
                Vector3 p0 = Vector3.Lerp(a, e, k / (float)steps), p1 = Vector3.Lerp(a, e, (k + 1f) / steps);
                float h0 = RoadBed(p0, side), h1 = RoadBed(p1, side);
                float lo = Mathf.Min(LowSide(p0, side), LowSide(p1, side));
                p0.y = h0; p1.y = h1;
                var rot = Quaternion.LookRotation(p1 - p0);
                b.Box((p0 + p1) * 0.5f + Vector3.up * 0.12f, new Vector3(3.2f, 0.24f, Vector3.Distance(p0, p1) + 0.35f), p.cobble, rot);
                float fillH = Mathf.Min(h0, h1) - lo + 0.3f;
                if (fillH > 0.3f)
                {
                    // an earth terrace under the road, faced with a low stone kerb on its downhill edge
                    Vector3 m = (p0 + p1) * 0.5f;
                    var flat = Quaternion.LookRotation(new Vector3(p1.x - p0.x, 0f, p1.z - p0.z));
                    float len = Vector3.Distance(p0, p1) + 0.4f;
                    float down = CastleHillHeight(m.x + side.x * 1.7f, m.z + side.z * 1.7f) < CastleHillHeight(m.x - side.x * 1.7f, m.z - side.z * 1.7f) ? 1f : -1f;
                    b.Box(new Vector3(m.x, lo - 0.3f + fillH * 0.5f, m.z), new Vector3(3.4f, fillH, len), grassDark, flat);
                    b.Box(new Vector3(m.x, lo - 0.3f + (fillH + 0.5f) * 0.5f, m.z) + side * (down * 1.7f), new Vector3(0.4f, fillH + 0.5f, len), p.stone, flat);
                }
            }
        }

        // Everything of the castle proper in the castle model's own warm cream stone (TB_CastleStone*), not the city's grey.
        p.stone = b.Paint(C(0.81f, 0.73f, 0.59f), 0.06f);
        p.stoneLight = b.Paint(C(0.90f, 0.84f, 0.70f), 0.06f);
        p.stoneDark = b.Paint(C(0.63f, 0.56f, 0.46f), 0.06f);
        Vector3 top = c + Vector3.up * CastleBase;
        // ---- the lower gatehouse where the road reaches the plateau ----
        Vector3 gateAt = new Vector3(CastleRoad[CastleRoad.Length - 1].x, CastleBase, CastleRoad[CastleRoad.Length - 1].y);
        Vector3 inward = (new Vector3(c.x, CastleBase, c.z) - gateAt); inward.y = 0f; inward.Normalize();
        Vector3 across = new Vector3(inward.z, 0f, -inward.x);
        int red = p.roof[0], slate = p.roof[2];
        foreach (float s in new[] { -1f, 1f })
            RoundTower(b, p.stoneLight, red, gateAt + across * (s * 3.4f) - Vector3.up * 4f, 2.3f, 13f, 5f);
        b.Box(gateAt + Vector3.up * 3.6f, new Vector3(4.6f, 7.2f, 3.4f), p.stoneLight, Quaternion.LookRotation(across));
        b.Box(gateAt + Vector3.up * 1.6f - inward * 1.72f, new Vector3(2.6f, 3.2f, 0.06f), p.dark, Quaternion.LookRotation(inward));
        b.Box(gateAt + Vector3.up * 5.4f - inward * 1.74f, new Vector3(1.1f, 1.6f, 0.05f), p.banner, Quaternion.LookRotation(inward));

        // ---- the bailey wall round the plateau's flanks and back, red-roofed towers along it ----
        const float WallR = CastlePlateauR - 1f, sink = 4f;
        float from = 226f, to = 505f; // degrees, running round through south, east and north (the city side stays open)
        int segs = 10;
        Vector3 Wp(float deg)
        {
            float th = deg * Mathf.Deg2Rad;
            return new Vector3(c.x + Mathf.Cos(th) * WallR, CastleBase - sink, c.z + Mathf.Sin(th) * WallR);
        }
        for (int k = 0; k < segs; k++)
            WallRun(b, p, Wp(Mathf.Lerp(from, to, k / (float)segs)), Wp(Mathf.Lerp(from, to, (k + 1f) / segs)), sink + 4.5f, 2.2f);
        for (int k = 0; k <= segs; k += 2)
            RoundTower(b, p.stoneLight, k % 4 == 0 ? red : slate, Wp(Mathf.Lerp(from, to, k / (float)segs)), 2.6f, sink + 8.5f, 5f);

        // ---- the castle's walled base on the city face: a tall battered retaining wall round the plateau's brow, a
        // parapet along its top and a drum tower at each end (the menu art's castle stands on a stone terrace, not on
        // bare grass). It stops short of the road's hairpins either side (they reach the brow at ~153 and ~209 deg).
        {
            const float BaseR = CastlePlateauR + 0.6f, BaseDrop = CastleShelf + 2f;
            const float a0 = 161f, a1 = 199f;
            const int baseSegs = 8;
            Vector3 Bp(float deg, float y)
            {
                float th = deg * Mathf.Deg2Rad;
                return new Vector3(c.x + Mathf.Cos(th) * BaseR, y, c.z + Mathf.Sin(th) * BaseR);
            }
            for (int k = 0; k < baseSegs; k++)
            {
                Vector3 a = Bp(Mathf.Lerp(a0, a1, k / (float)baseSegs), CastleBase - BaseDrop);
                Vector3 e = Bp(Mathf.Lerp(a0, a1, (k + 1f) / baseSegs), CastleBase - BaseDrop);
                Vector3 d = e - a;
                var rot = Quaternion.LookRotation(d.normalized);
                Vector3 outward = new Vector3((a.x + e.x) * 0.5f - c.x, 0f, (a.z + e.z) * 0.5f - c.z).normalized;
                // battered: a wider footing course under the wall face
                b.Box((a + e) * 0.5f + Vector3.up * (BaseDrop * 0.5f + 0.5f), new Vector3(2.4f, BaseDrop + 1f, d.magnitude + 0.2f), p.stone, rot);
                b.Box((a + e) * 0.5f + Vector3.up * 1.6f + outward * 0.8f, new Vector3(1.8f, 3.2f, d.magnitude + 0.25f), p.stoneDark, rot);
                b.Box((a + e) * 0.5f + Vector3.up * (BaseDrop + 0.9f) + outward * 0.15f, new Vector3(2.8f, 0.35f, d.magnitude + 0.3f), p.stoneLight, rot);
                if (k == baseSegs / 2 - 1 || k == baseSegs / 2) continue; // no merlons in front of the gate: from the seat they'd cut into it
                int m = Mathf.Max(2, Mathf.FloorToInt(d.magnitude / 1.7f));
                for (int i = 0; i < m; i++)
                    b.Box(a + d * ((i + 0.5f) / m) + Vector3.up * (BaseDrop + 1.55f) + outward * 1.0f, new Vector3(0.55f, 1.0f, 0.85f), p.stoneLight, rot);
            }
            foreach (float deg in new[] { a0, a1 })
                RoundTower(b, p.stoneLight, p.roof[2], Bp(deg, CastleBase - BaseDrop - 1f), 2.8f, BaseDrop + 6f, 4.5f);
        }

        var hill = Place(city, "Castle", b.Build("Castle"));

        // ---- trees down the slopes (library pines and broadleaves) ----
        if (useLibrary)
        {
            var trees = new GameObject("CastleHillTrees").transform;
            trees.SetParent(hill.transform, false);
            string[] kinds = { "Pine_Tall", "Pine_Medium", "Tree_Broadleaf_Green", "Pine_Tall", "Tree_Broadleaf_Orange", "Pine_Medium", "Tree_Broadleaf_Lime" };
            var trng = new System.Random(19);
            int placed = 0;
            for (int attempt = 0; attempt < 400 && placed < 56; attempt++)
            {
                float th = (float)trng.NextDouble() * Mathf.PI * 2f;
                float foot = CastleFootR(th);
                float r = Mathf.Lerp(CastlePlateauR + 3f, foot - 1f, (float)trng.NextDouble());
                var at = new Vector3(c.x + Mathf.Cos(th) * r, 0f, c.z + Mathf.Sin(th) * r);
                if (DistanceToRoad(at) < 4f || at.x < WallX + 12f) continue;
                // thinner on the city face, so the rock and the road still read
                if (Mathf.Cos(th) < -0.6f && trng.NextDouble() < 0.6) continue;
                at.y = CastleHillHeight(at.x, at.z) - 0.15f;
                var t = Spawn(kinds[placed % kinds.Length], trees, at, (float)trng.NextDouble() * 360f, Vector3.one * (1.1f + (float)trng.NextDouble() * 0.6f));
                if (t != null) { t.name = "Tree_Castle" + placed; placed++; }
            }
        }

        // ---- the castle itself ----
        if (PF("Landmark_Castle") != null)
        {
            // Gate toward the city (-X), 11 m back from the plateau's brow, full size (every tower still on the plateau).
            var castle = Spawn("Landmark_Castle", hill.transform, Vector3.zero, 180f, Vector3.one);
            var info = castle.GetComponent<EnvironmentAsset>();
            castle.transform.position = top + Vector3.left * (32f - info.walls.max.x);
            return;
        }
        ProceduralCastle(city, top);
    }

    // Road bed height at a point: the higher of the ground under its two edges (the road is cut into the slope).
    private static float RoadBed(Vector3 p, Vector3 side) =>
        Mathf.Max(CastleHillHeight(p.x + side.x * 1.6f, p.z + side.z * 1.6f), CastleHillHeight(p.x - side.x * 1.6f, p.z - side.z * 1.6f), CastleHillHeight(p.x, p.z)) + 0.05f;

    private static float LowSide(Vector3 p, Vector3 side) =>
        Mathf.Min(CastleHillHeight(p.x + side.x * 1.7f, p.z + side.z * 1.7f), CastleHillHeight(p.x - side.x * 1.7f, p.z - side.z * 1.7f));

    private static float DistanceToRoad(Vector3 p)
    {
        float best = float.MaxValue;
        var q = new Vector2(p.x, p.z);
        for (int i = 0; i + 1 < CastleRoad.Length; i++)
        {
            Vector2 a = CastleRoad[i], e = CastleRoad[i + 1], ab = e - a;
            float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude);
            best = Mathf.Min(best, Vector2.Distance(q, a + ab * t));
        }
        return best;
    }

    // The old procedural castle (when the environment library isn't built): octagonal curtain wall, round towers,
    // a gatehouse to the west, the keep and its main tower.
    private static void ProceduralCastle(Transform city, Vector3 top)
    {
        var b = new MeshBuilder();
        var p = new CityPal(b);
        int cs = b.Paint(C(0.62f, 0.60f, 0.56f), 0.08f), csDark = b.Paint(C(0.48f, 0.46f, 0.44f), 0.08f);
        int croof = b.Paint(C(0.20f, 0.26f, 0.46f), 0.12f), flag = b.Paint(C(0.72f, 0.16f, 0.14f), 0.1f);
        const float R = 22f, WH = 11f;
        var corners = new Vector3[8];
        for (int i = 0; i < 8; i++) { float a = (22.5f + i * 45f) * Mathf.Deg2Rad; corners[i] = top + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * R; }
        for (int i = 0; i < 8; i++)
        {
            Vector3 a = corners[i], e = corners[(i + 1) % 8], d = e - a;
            var rot = Quaternion.LookRotation(d.normalized);
            b.Box((a + e) * 0.5f + Vector3.up * WH * 0.5f, new Vector3(2.4f, WH, d.magnitude), cs, rot);
            int merlons = Mathf.FloorToInt(d.magnitude / 1.8f);
            for (int k = 0; k < merlons; k++)
                b.Box(a + d * ((k + 0.5f) / merlons) + Vector3.up * (WH + 0.55f) + rot * new Vector3(-1f, 0f, 0f), new Vector3(0.5f, 1.1f, 0.9f), cs, rot);
        }
        for (int i = 0; i < 8; i++)
        {
            RoundTower(b, cs, croof, corners[i], 3.3f, 18f, 7f);
            b.Box(corners[i] + Vector3.up * 26.5f, new Vector3(0.1f, 3f, 0.1f), p.iron);
            b.Box(corners[i] + new Vector3(0f, 27.4f, 0.55f), new Vector3(0.05f, 0.8f, 1.1f), flag);
        }
        Vector3 gate = top + Vector3.left * (R * Mathf.Cos(22.5f * Mathf.Deg2Rad) + 0.5f);
        foreach (float s in new[] { -1f, 1f }) RoundTower(b, cs, croof, gate + Vector3.forward * s * 4.2f, 2.6f, 15f, 5f);
        b.Box(gate + Vector3.up * 3.2f + Vector3.left * 1.25f, new Vector3(0.1f, 6.4f, 4.4f), p.dark);
        b.Box(gate + Vector3.up * 12f, new Vector3(3f, 4f, 7f), cs);
        Vector3 keep = top + Vector3.right * 3f;
        const float KH = 20f;
        b.Box(keep + Vector3.up * KH * 0.5f, new Vector3(15f, KH, 15f), cs);
        b.Box(keep + Vector3.up * (KH + 0.2f), new Vector3(15.6f, 0.4f, 15.6f), csDark);
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
                RoundTower(b, cs, croof, keep + new Vector3(sx * 7.5f, 0f, sz * 7.5f), 1.7f, KH + 5f, 4f);
        RoundTower(b, cs, croof, keep + new Vector3(4f, 0f, 3f), 4f, KH + 9f, 8f);
        Place(city, "CastleKeep", b.Build("CastleKeep"));
    }
}
