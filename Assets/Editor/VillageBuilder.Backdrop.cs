using System.Collections.Generic;
using UnityEngine;

// The backdrop all round the city (part of VillageBuilder, rebuilt with the "City" root) - the main-menu art's layered
// mountains, instead of the old ring of single cone hills:
//   Foothills   ~235-390 m  rolling green ridges with pine forests (they rise out of the south-west farmland's slope)
//   Mountains   ~430-680 m  rocky peaks, green lower slopes, snow above ~125 m
//   High range  ~760-1080 m tall, pale blue-grey (they sit deep in the haze), heavy snow
// Each range is one flat-shaded ridge mesh: a polar grid whose crest line is a chain of overlapping peaks (so the
// silhouette is a saw of summits and cols, not a row of pyramids), tallest behind the castle and the menu's view. The
// river keeps a valley open through the two nearer ranges, so from the seat it runs off between the hills and on
// toward the high range rather than ending anywhere. DayCycle's daytime haze (140-1400 m) fades each range a little
// more than the one before it.
public static partial class VillageBuilder
{
    private static readonly Vector3 RangeOrigin = new Vector3(4f, 0f, -1f);

    private class RangeSpec
    {
        public string name;
        public float rIn, rOut, hMin, hMax, wMin, wMax, valleyFloor = 1f, snowLine = 1e6f, treeLine = 1e6f;
        public float peakPow = 1f;      // < 1: rounded (hills), 1: straight-sided massifs
        public float snowFrac = 2f;     // snow above this fraction of the local crest height (and above snowLine)
        public float spurs;             // depth of the ridges and gullies running down the faces (0 = smooth)
        public int sectors, rings, peaks, seed;
        public bool onFarmland;
        public Color grass, grassDark, rock, rockDark, snow;
    }

    private static void BuildMountains(Transform city)
    {
        var root = new GameObject("Mountains").transform;
        root.SetParent(city, false);
        var specs = new[]
        {
            // Rounded green hills, forested - the menu art's rolling foreground ridges.
            new RangeSpec { name = "Foothills", rIn = 235f, rOut = 390f, hMin = 26f, hMax = 62f, wMin = 10f, wMax = 24f, sectors = 360, rings = 10, peaks = 22, seed = 101,
                valleyFloor = 0.1f, onFarmland = true, peakPow = 0.55f, spurs = 0.08f,
                grass = C(0.38f, 0.54f, 0.27f), grassDark = C(0.29f, 0.45f, 0.23f), rock = C(0.50f, 0.49f, 0.45f), rockDark = C(0.42f, 0.41f, 0.39f), snow = Color.white },
            // Broad massifs with shoulders and sub-summits, ridged faces, a ragged snow cap over the top third.
            new RangeSpec { name = "MountainRange", rIn = 430f, rOut = 680f, hMin = 100f, hMax = 205f, wMin = 9f, wMax = 19f, sectors = 300, rings = 12, peaks = 15, seed = 202,
                valleyFloor = 0.22f, snowLine = 105f, treeLine = 62f, snowFrac = 0.7f, spurs = 0.2f,
                grass = C(0.33f, 0.47f, 0.30f), grassDark = C(0.27f, 0.41f, 0.27f), rock = C(0.47f, 0.50f, 0.60f), rockDark = C(0.37f, 0.40f, 0.52f), snow = C(0.96f, 0.97f, 1.0f) },
            // The far wall: tall, pale lavender-blue in the haze, mostly snow.
            new RangeSpec { name = "HighRange", rIn = 760f, rOut = 1080f, hMin = 190f, hMax = 340f, wMin = 10f, wMax = 21f, sectors = 280, rings = 11, peaks = 13, seed = 303,
                snowLine = 150f, treeLine = 95f, snowFrac = 0.64f, spurs = 0.18f,
                grass = C(0.42f, 0.52f, 0.52f), grassDark = C(0.38f, 0.48f, 0.50f), rock = C(0.58f, 0.63f, 0.78f), rockDark = C(0.49f, 0.54f, 0.71f), snow = C(0.94f, 0.96f, 1.0f) },
        };
        int tris = 0;
        foreach (var s in specs)
        {
            var crest = Crest(s);
            var b = new MeshBuilder();
            int grass = b.Paint(s.grass, 0.04f), grassDark = b.Paint(s.grassDark, 0.04f), rock = b.Paint(s.rock, 0.06f), rockDark = b.Paint(s.rockDark, 0.06f), snow = b.Paint(s.snow, 0.15f);
            var rng = new System.Random(s.seed + 7);
            var grid = new Vector3[s.rings + 1, s.sectors];
            for (int j = 0; j <= s.rings; j++)
                for (int i = 0; i < s.sectors; i++)
                {
                    float u = j / (float)s.rings;
                    float yaw = i * 360f / s.sectors;
                    if (j > 0 && j < s.rings) { yaw += (float)(rng.NextDouble() - 0.5) * 0.5f * 360f / s.sectors; u += (float)(rng.NextDouble() - 0.5) * 0.5f / s.rings; }
                    grid[j, i] = RangePoint(s, crest, yaw, u, j == 0 || j == s.rings);
                }
            for (int j = 0; j < s.rings; j++)
                for (int i = 0; i < s.sectors; i++)
                {
                    int i2 = (i + 1) % s.sectors;
                    // yaw runs clockwise seen from above (0 = +Z, 90 = +X): outer-then-inner winding keeps the faces up
                    Vector3 a = grid[j, i], bb = grid[j, i2], c = grid[j + 1, i2], d = grid[j + 1, i];
                    foreach (var (t0, t1, t2) in new[] { (a, c, bb), (a, d, c) })
                    {
                        Vector3 n = Vector3.Cross(t1 - t0, t2 - t0);
                        if (n.sqrMagnitude < 1e-6f) continue;
                        n.Normalize();
                        float y = (t0.y + t1.y + t2.y) / 3f;
                        Vector3 mid = (t0 + t1 + t2) / 3f;
                        // snow over the top of each peak (a fraction of its own height, so small summits get caps too),
                        // with a ragged edge that runs further down the gullies
                        float local = CrestAt(crest, Mathf.Atan2(mid.x - RangeOrigin.x, mid.z - RangeOrigin.z) * Mathf.Rad2Deg);
                        float snowAt = Mathf.Max(s.snowLine, local * (s.snowFrac + 0.14f * (Mathf.PerlinNoise(mid.x * 0.035f + s.seed, mid.z * 0.035f) - 0.5f)));
                        int paint;
                        if (y > snowAt + 25f || (y > snowAt && n.y > 0.35f)) paint = snow;
                        else if (y > s.treeLine || n.y < (y > s.treeLine * 0.6f ? 0.72f : 0.5f)) paint = ((i + j) & 1) == 0 ? rock : rockDark;
                        else paint = Mathf.PerlinNoise(t0.x * 0.01f + s.seed, t0.z * 0.01f) > 0.5f ? grassDark : grass;
                        b.Triangle(t0, t1, t2, paint);
                        tris++;
                    }
                }
            var go = Place(root, s.name, b.Build(s.name));
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // far past the shadow distance anyway

            // Pine forests: dense on the foothills, a fringe on the mountains' lower slopes.
            if (s.name == "HighRange") continue;
            var fb = new MeshBuilder();
            int pineA = fb.Paint(C(0.17f, 0.33f, 0.20f), 0.05f), pineB = fb.Paint(C(0.22f, 0.40f, 0.23f), 0.05f);
            var frng = new System.Random(s.seed + 13);
            int want = s.name == "Foothills" ? 2300 : 800, placed = 0;
            float scale = s.name == "Foothills" ? 1f : 1.9f;
            for (int attempt = 0; attempt < want * 6 && placed < want; attempt++)
            {
                float yaw = (float)frng.NextDouble() * 360f, u = Mathf.Lerp(0.06f, 0.8f, (float)frng.NextDouble());
                var at = RangePoint(s, crest, yaw, u, false);
                if (Mathf.PerlinNoise(at.x * 0.018f + 50f, at.z * 0.018f + 50f) < 0.42f) continue;   // in clumps, with clearings
                if (at.y > s.treeLine * 0.9f || at.y > 150f) continue;
                // not on steep ground (compare with a point a few metres further out)
                var nxt = RangePoint(s, crest, yaw, u + 6f / (s.rOut - s.rIn), false);
                if (Mathf.Abs(nxt.y - at.y) > 6f * 0.9f) continue;
                float h = (7f + (float)frng.NextDouble() * 6f) * scale;
                at.y -= 1.2f * scale; // sunk a little: the grid's facets and the smooth height differ by a metre or so
                int paint = frng.NextDouble() < 0.5 ? pineA : pineB;
                fb.Prism(at + Vector3.up * (h * 0.36f), 6, h * 0.24f, 0f, h * 0.72f, paint);
                fb.Prism(at + Vector3.up * (h * 0.68f), 6, h * 0.17f, 0f, h * 0.64f, paint);
                placed++;
            }
            var forest = Place(root, s.name + "Forest", fb.Build(s.name + "Forest"));
            foreach (var r in forest.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tris += placed * 24;
        }
        // The ranges reach ~1.1 km: every camera looking at them must see that far.
        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (cam.farClipPlane < 2600f) cam.farClipPlane = 2600f;
        Debug.Log($"[City] mountains: 3 ranges + forests, ~{tris} triangles");
    }

    // A point on a range at a compass yaw (degrees, 0 = +Z) and a fraction u across its depth (0 = near foot, 1 = far foot).
    private static Vector3 RangePoint(RangeSpec s, float[] crest, float yaw, float u, bool foot)
    {
        u = Mathf.Clamp01(u);
        float r = Mathf.Lerp(s.rIn, s.rOut, u);
        float a = yaw * Mathf.Deg2Rad;
        var p = RangeOrigin + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * r;
        float ground = s.onFarmland ? FarmHeight(p.x, p.z) : 0f;
        if (foot) { p.y = ground - 1.2f; return p; }
        // the crest sits a little nearer or further along the range, so ridges don't run as perfect circles
        float uc = 0.42f + 0.14f * Mathf.Sin(a * 3f + s.seed);
        float prof = u < uc ? Mathf.Pow(Mathf.Sin(Mathf.PI * 0.5f * u / uc), 1.15f) : Mathf.Pow(Mathf.Cos(Mathf.PI * 0.5f * (u - uc) / (1f - uc)), 0.9f);
        float c = CrestAt(crest, yaw);
        float rough = 1f + 0.22f * (Mathf.PerlinNoise(yaw * 0.21f + s.seed, u * 4.3f) - 0.5f);
        // Spurs and gullies down the faces: a ridged pattern round the range (wavering a little with depth) that is
        // strongest mid-slope and fades out at the crest and the foot - faces read as ribs of rock, not cone skins.
        if (s.spurs > 0f)
        {
            float wob = (Mathf.PerlinNoise(yaw * 0.05f + s.seed, u * 2.1f) - 0.5f) * 2.2f;
            float rib = Mathf.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad * 55f + wob));
            float mid = Mathf.Sin(Mathf.PI * Mathf.Clamp01(Mathf.Abs(u - uc) / Mathf.Max(uc, 1f - uc) * 1.4f));
            rough *= 1f - s.spurs * (1f - rib) * mid;
        }
        p.y = Mathf.Max(ground + c * prof * rough, ground) - 0.8f * (1f - prof);
        return p;
    }

    // Crest height round the range, one value per degree: a base ridge plus a chain of overlapping peaks, taller toward
    // the castle and the menu's view (east / north-east), cut down into a valley where the river runs through.
    private static float[] Crest(RangeSpec s)
    {
        var rng = new System.Random(s.seed);
        var crest = new float[360];
        var peaks = new List<(float yaw, float h, float w)>();
        // Main summits spread evenly round the range (jittered), each a massif: the summit plus a shoulder peak on one
        // side and a lower sub-summit on the other, so no peak stands alone as a pyramid.
        for (int k = 0; k < s.peaks; k++)
        {
            float yaw = (k + 0.5f + ((float)rng.NextDouble() - 0.5f) * 0.7f) * 360f / s.peaks;
            float h = Mathf.Lerp(s.hMin, s.hMax, (float)rng.NextDouble());
            float w = Mathf.Lerp(s.wMin, s.wMax, (float)rng.NextDouble());
            float side = rng.NextDouble() < 0.5 ? -1f : 1f;
            peaks.Add((yaw, h, w));
            peaks.Add((yaw + side * w * (0.35f + 0.2f * (float)rng.NextDouble()), h * (0.72f + 0.12f * (float)rng.NextDouble()), w * 0.6f));
            peaks.Add((yaw - side * w * (0.45f + 0.2f * (float)rng.NextDouble()), h * (0.5f + 0.15f * (float)rng.NextDouble()), w * 0.55f));
        }
        for (int k = 0; k < s.peaks; k++) // small summits in between
            peaks.Add(((float)rng.NextDouble() * 360f, Mathf.Lerp(s.hMin, s.hMax, (float)rng.NextDouble()) * 0.45f, Mathf.Lerp(s.wMin, s.wMax, (float)rng.NextDouble()) * 0.5f));
        float riverN = RiverYaw(Mathf.Lerp(s.rIn, s.rOut, 0.45f), true), riverS = RiverYaw(Mathf.Lerp(s.rIn, s.rOut, 0.45f), false);
        for (int d = 0; d < 360; d++)
        {
            float best = 0f;
            foreach (var pk in peaks)
            {
                float t = 1f - Mathf.Abs(Mathf.DeltaAngle(d, pk.yaw)) / pk.w;
                if (t > 0f) best = Mathf.Max(best, pk.h * Mathf.Pow(t, s.peakPow));
            }
            float baseRidge = s.hMin * 0.5f * (0.6f + 0.8f * Mathf.PerlinNoise(d * 0.045f + s.seed, 0.5f));
            float emphasis = 0.8f + 0.35f * Mathf.Max(0f, Mathf.Cos((d - 75f) * Mathf.Deg2Rad));
            float valley = 1f;
            if (s.valleyFloor < 1f)
            {
                float off = Mathf.Min(Mathf.Abs(Mathf.DeltaAngle(d, riverN)), Mathf.Abs(Mathf.DeltaAngle(d, riverS)));
                valley = Mathf.Lerp(s.valleyFloor, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(3f, 16f, off)));
            }
            crest[d] = (baseRidge + best) * emphasis * valley;
        }
        return crest;
    }

    private static float CrestAt(float[] crest, float yaw)
    {
        float y = Mathf.Repeat(yaw, 360f);
        int a = Mathf.FloorToInt(y) % 360, b = (a + 1) % 360;
        return Mathf.Lerp(crest[a], crest[b], y - Mathf.Floor(y));
    }

    // The compass yaw (from RangeOrigin) at which the river crosses a given distance, north or south.
    private static float RiverYaw(float dist, bool north)
    {
        Vector3 best = north ? new Vector3(40f, 0f, 600f) : new Vector3(40f, 0f, -600f);
        float bestErr = float.MaxValue;
        foreach (var p in stream)
        {
            if ((p.z > 0f) != north) continue;
            float err = Mathf.Abs(Vector3.Distance(new Vector3(p.x, 0f, p.z), RangeOrigin) - dist);
            if (err < bestErr) { bestErr = err; best = p; }
        }
        // past the river's own end it runs straight on the way it was going
        return Mathf.Repeat(Mathf.Atan2(best.x - RangeOrigin.x, best.z - RangeOrigin.z) * Mathf.Rad2Deg, 360f);
    }
}
