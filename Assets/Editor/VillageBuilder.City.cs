using System.Collections.Generic;
using UnityEngine;

// The city around the smithy (part of VillageBuilder - same menu item, same rebuild-from-scratch rules; everything
// here lives under the "City" root, which is deleted and rebuilt on every run).
//
// Composition, from the fixed seat at (0, 1.7, -3.5) looking along +X (the start view, pitched 12 degrees down):
//   foreground  - the workshop, the table, the stream and the little square where the customer lane meets the road
//   midground   - an avenue running straight on from the square, lined with town houses; a market plaza with a
//                 fountain, a church and a guild hall; blocks of streets either side
//   background  - the city wall and its gate
//   horizon     - the castle on its hill, framed by the avenue, and a ring of low hills all round
// Buildings along the view get taller with distance so the roofline stays under ~8 degrees above the eye, which is
// what keeps the castle (8-17 degrees) visible over the rooftops rather than hidden behind the nearest house.
// The other three seat views (forge, storage, the smith's house) get city skylines beyond the trees.
//
// Also built here: the crowd's street graph ("CityNav" - CityNavNode) and the boxes that hide walkers from the seat
// (CityOccluders on the City root) - see CityLife.
public static partial class VillageBuilder
{
    private static readonly List<(HouseSpec spec, GameObject go)> villageHouses = new List<(HouseSpec, GameObject)>();

    private const float AvenueZ = -3.5f, AvenueHalf = 3f;
    private const float WallX = 104f;
    private static readonly Vector3 Seat = new Vector3(0f, 1.7f, -3.5f);
    private static readonly Vector3 CastleCenter = new Vector3(178f, 0f, -3.5f); // out past the wall, room for a real hill
    private const float CastleBase = 26f;           // the hilltop plateau the castle stands on
    private const float RooflineTan = 0.1405f;      // ~8 degrees: the tallest a building in front of the castle may look

    private static readonly List<Bounds> occluderBoxes = new List<Bounds>();
    private static readonly List<(Vector3 pos, string place)> cityDoors = new List<(Vector3, string)>();

    // x ranges of the blocks either side of the plaza, and z ranges of the rows of blocks north (+) and south (-) of
    // the avenue. Streets run in the gaps.
    private static readonly Vector2[] BlockX = { new Vector2(26f, 43f), new Vector2(48f, 56f), new Vector2(70f, 79f), new Vector2(84f, 100f) };
    private static readonly Vector2[] BlockZ =
    {
        new Vector2(-0.5f, 11f), new Vector2(15.5f, 30f), new Vector2(34.5f, 48f),
        new Vector2(-18f, -6.5f), new Vector2(-36f, -22.5f), new Vector2(-52f, -40.5f),
    };
    private static readonly Vector2 PlazaX = new Vector2(56f, 70f);
    private const float PlazaNorth = 8.5f, PlazaSouth = -15f;
    private static float PlazaCx => (PlazaX.x + PlazaX.y) * 0.5f;
    private const float LandmarkLift = 1.2f; // church + clock house: extra height over their plot scale

    private static void BuildCity(Transform village)
    {
        var old = GameObject.Find("City");
        if (old != null) Object.DestroyImmediate(old);
        var city = new GameObject("City").transform;
        occluderBoxes.Clear();
        cityDoors.Clear();

        BuildCityStreets(city);
        var rng = new System.Random(1234);
        int n = BuildCityBlocks(city, rng);
        BuildPlaza(city, rng);
        BuildCityWall(city);
        BuildCastle(city);
        n += BuildSkylines(city, rng);
        // Round the smithy, the river's ends, and the south-western edge with its farmland (VillageBuilder.Outskirts.cs) -
        // their own random streams, so the city above keeps exactly its layout.
        n += BuildNeighbourhood(city);
        BuildRiverside(city);
        BuildRiverfront(city); // quays, the south and town bridges, buildings facing the water (VillageBuilder.Riverfront.cs)
        BuildCityEdge(city);
        var meadowGround = GameObject.Find("MeadowGround");
        BuildFarmland(city, meadowGround != null ? meadowGround.GetComponent<Renderer>().sharedMaterial.color : new Color(0.30f, 0.42f, 0.19f));
        BuildCountryside(city);
        BuildCityLights(village);
        BuildLifeProps(city); // benches and barrels for the crowd's new spots (VillageBuilder.Life.cs)
        var chimneys = new GameObject("Chimneys").transform; // markers only: CityAmbience smokes the nearest
        chimneys.SetParent(city, false);
        foreach (var top in chimneyTops) { var m = new GameObject("Chimney").transform; m.SetParent(chimneys, false); m.position = top; }

        // Village buildings hide walkers too.
        foreach (var (spec, go) in villageHouses) AddOccluder(go);
        foreach (var name in new[] { "Barn", "PlayerHouse" })
        {
            var go = GameObject.Find(name);
            if (go != null) AddOccluder(go);
        }
        city.gameObject.AddComponent<CityOccluders>().boxes = new List<Bounds>(occluderBoxes);
        BuildCityNav(city);
        Debug.Log($"[City] built {n} town buildings, {occluderBoxes.Count} occluder boxes, {cityDoors.Count} doors");
    }

    private static void AddOccluder(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return;
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        occluderBoxes.Add(b);
    }

    // ---- palette --------------------------------------------------------------------------------------

    private class CityPal
    {
        public int[] plaster, roof, roofDark, glass, awning, shutter;
        public int timber, stone, stoneDark, stoneLight, door, iron, cobble, cobbleDark, flame, sign, dark, water, stained, gold, banner;

        public CityPal(MeshBuilder b)
        {
            plaster = new[]
            {
                b.Paint(C(0.90f, 0.84f, 0.70f), 0.05f), b.Paint(C(0.86f, 0.72f, 0.50f), 0.05f), b.Paint(C(0.86f, 0.73f, 0.67f), 0.05f),
                b.Paint(C(0.80f, 0.79f, 0.76f), 0.05f), b.Paint(C(0.75f, 0.78f, 0.65f), 0.05f), b.Paint(C(0.72f, 0.76f, 0.80f), 0.05f),
                b.Paint(C(0.93f, 0.90f, 0.83f), 0.05f),
            };
            var roofs = new[] { C(0.62f, 0.30f, 0.20f), C(0.48f, 0.22f, 0.17f), C(0.30f, 0.33f, 0.38f), C(0.44f, 0.30f, 0.20f), C(0.26f, 0.31f, 0.42f) };
            roof = new int[roofs.Length];
            roofDark = new int[roofs.Length];
            for (int i = 0; i < roofs.Length; i++) { roof[i] = b.Paint(roofs[i], 0.08f); roofDark[i] = b.Paint(Shade(roofs[i], 0.78f), 0.08f); }
            // Window glass: several warm tints that all read as "house window" to DayCycle (it lights each one on its own
            // evening schedule, so a street never switches on all at once).
            glass = new int[5];
            for (int i = 0; i < glass.Length; i++) { float g = 0.73f + i * 0.015f; glass[i] = b.Paint(new Color(1f, g, g - 0.35f), 0.2f, 0.55f); }
            awning = new[] { b.Paint(C(0.70f, 0.22f, 0.18f), 0.1f), b.Paint(C(0.25f, 0.45f, 0.30f), 0.1f), b.Paint(C(0.22f, 0.35f, 0.60f), 0.1f), b.Paint(C(0.85f, 0.70f, 0.30f), 0.1f) };
            shutter = new[] { b.Paint(C(0.26f, 0.38f, 0.40f), 0.1f), b.Paint(C(0.45f, 0.30f, 0.20f), 0.1f), b.Paint(C(0.35f, 0.42f, 0.26f), 0.1f) };
            timber = b.Paint(C(0.30f, 0.19f, 0.10f), 0.08f);
            stone = b.Paint(C(0.58f, 0.56f, 0.52f), 0.08f);
            stoneDark = b.Paint(C(0.42f, 0.40f, 0.38f), 0.08f);
            stoneLight = b.Paint(C(0.72f, 0.70f, 0.65f), 0.08f);
            door = b.Paint(C(0.40f, 0.25f, 0.13f), 0.08f);
            iron = b.Paint(C(0.20f, 0.21f, 0.24f), 0.4f);
            cobble = b.Paint(C(0.52f, 0.49f, 0.45f), 0.06f);
            cobbleDark = b.Paint(C(0.40f, 0.38f, 0.35f), 0.06f);
            flame = b.Paint(C(1f, 0.72f, 0.30f), 0f, 1.5f);
            sign = b.Paint(C(0.56f, 0.41f, 0.25f), 0.08f);
            dark = b.Paint(C(0.08f, 0.07f, 0.07f), 0f);
            water = b.Paint(C(0.24f, 0.49f, 0.60f), 0.85f);
            stained = b.Paint(C(0.55f, 0.35f, 0.75f), 0.3f, 0.35f);
            gold = b.Paint(C(0.82f, 0.66f, 0.26f), 0.5f);
            banner = b.Paint(C(0.66f, 0.14f, 0.14f), 0.1f);
        }
    }

    // ---- a town house (same local frame as House: front wall at +X, footprint centred, floor at y = 0) ---------

    private class TownSpec
    {
        public float W = 5f, D = 7f;
        public int floors = 2;          // including the ground floor
        public float groundH = 3.0f, floorH = 2.6f, rise = 2.2f;
        public bool stoneGround, timber, jetty, shop, chimney = true;
        public int plaster, roof, roofType, awning, shutter;
        public string place = "home";
        public float doorZ;             // filled in when built
        public float Height => groundH + floorH * (floors - 1) + rise + 0.3f;
    }

    private static int WindowCount(float w) => Mathf.Max(1, Mathf.FloorToInt((w - 0.4f) / 1.6f));

    private static void TownHouse(Put u, CityPal p, TownSpec s, System.Random rng)
    {
        float D = s.D, W = s.W, front = D * 0.5f;
        int wall = p.plaster[s.plaster];
        u.Box(new Vector3(0f, 0.15f, 0f), new Vector3(D + 0.16f, 0.3f, W + 0.16f), p.stoneDark);
        u.Box(new Vector3(0f, s.groundH * 0.5f, 0f), new Vector3(D, s.groundH, W), s.stoneGround ? p.stone : wall);
        if (!s.stoneGround && s.timber)
            foreach (float sz in new[] { -1f, 1f })
                u.Box(new Vector3(front + 0.01f, s.groundH * 0.5f, sz * (W * 0.5f - 0.07f)), new Vector3(0.14f, s.groundH, 0.16f), p.timber);

        // Ground floor: a door in one bay, a shop window (with an awning and a hanging sign) or plain windows in the rest.
        int bays = WindowCount(W);
        int doorBay = rng.Next(bays);
        for (int k = 0; k < bays; k++)
        {
            float z = -W * 0.5f + (k + 0.5f) * W / bays;
            if (k == doorBay)
            {
                s.doorZ = z;
                u.Box(new Vector3(front + 0.01f, 1.1f, z), new Vector3(0.08f, 2.2f, 1.14f), s.stoneGround ? p.stoneLight : p.timber);
                u.Box(new Vector3(front + 0.045f, 1.02f, z), new Vector3(0.05f, 2.0f, 0.9f), p.door);
                u.Box(new Vector3(front + 0.3f, 0.09f, z), new Vector3(0.5f, 0.18f, 1.2f), p.stone);
            }
            else if (s.shop)
            {
                u.Box(new Vector3(front + 0.02f, 1.45f, z), new Vector3(0.08f, 1.5f, 1.45f), p.timber);
                u.Box(new Vector3(front + 0.05f, 1.45f, z), new Vector3(0.05f, 1.25f, 1.2f), p.glass[rng.Next(p.glass.Length)]);
                u.Box(new Vector3(front + 0.12f, 0.66f, z), new Vector3(0.2f, 0.08f, 1.5f), p.timber);
                u.Box(new Vector3(front + 0.55f, 2.5f, z), new Vector3(1.1f, 0.06f, 1.7f), p.awning[s.awning], Quaternion.Euler(0f, 0f, -20f));
            }
            else CityWindow(u, p, new Vector3(front, 1.55f, z), rng, s.stoneGround ? p.stoneLight : p.timber, s.shutter);
        }
        if (s.shop)
        {
            // A painted sign on an iron bracket, hung square to the street.
            float z = Mathf.Clamp(s.doorZ + (s.doorZ > 0f ? -0.9f : 0.9f), -W * 0.5f + 0.4f, W * 0.5f - 0.4f);
            u.Box(new Vector3(front + 0.45f, 3.05f, z), new Vector3(0.9f, 0.05f, 0.05f), p.iron);
            u.Box(new Vector3(front + 0.62f, 2.72f, z), new Vector3(0.62f, 0.5f, 0.05f), p.sign);
            u.Box(new Vector3(front + 0.62f, 2.72f, z), new Vector3(0.4f, 0.3f, 0.07f), p.awning[(s.awning + 1) % p.awning.Length]);
        }

        SideWindows(u, p, 0f, D, W, 1.55f, rng, s.stoneGround ? p.stoneLight : p.timber);
        float y = s.groundH, cx = 0f, fd = D;
        for (int f = 1; f < s.floors; f++)
        {
            if (s.jetty) { cx += 0.11f; fd += 0.22f; }
            float ff = cx + fd * 0.5f;
            u.Box(new Vector3(cx, y + s.floorH * 0.5f, 0f), new Vector3(fd, s.floorH, W), wall);
            if (s.timber)
            {
                u.Box(new Vector3(cx, y + 0.07f, 0f), new Vector3(fd + 0.06f, 0.14f, W + 0.06f), p.timber);
                foreach (float sz in new[] { -1f, 1f })
                {
                    u.Box(new Vector3(ff + 0.01f, y + s.floorH * 0.5f, sz * (W * 0.5f - 0.07f)), new Vector3(0.12f, s.floorH, 0.14f), p.timber);
                    if (W > 3.8f)
                        u.Box(new Vector3(ff + 0.02f, y + s.floorH * 0.5f, sz * (W * 0.5f - 0.42f)), new Vector3(0.07f, s.floorH, 0.09f), p.timber, Quaternion.Euler(sz * 26f, 0f, 0f));
                }
            }
            else u.Box(new Vector3(cx, y + 0.05f, 0f), new Vector3(fd + 0.08f, 0.1f, W + 0.08f), p.stoneLight);
            int n = WindowCount(W - (s.timber && W > 3.8f ? 0.8f : 0f));
            for (int k = 0; k < n; k++)
            {
                float span = W - (s.timber && W > 3.8f ? 1.4f : 0.4f);
                float z = -span * 0.5f + (k + 0.5f) * span / n;
                CityWindow(u, p, new Vector3(ff, y + s.floorH * 0.55f, z), rng, s.timber ? p.timber : p.stoneLight, s.shutter);
            }
            SideWindows(u, p, cx, fd, W, y + s.floorH * 0.55f, rng, s.timber ? p.timber : p.stoneLight);
            y += s.floorH;
        }
        CityRoof(u, p, s, wall, cx, fd, W, y, rng);
    }

    private static void CityWindow(Put u, CityPal p, Vector3 c, System.Random rng, int frame, int shutter)
    {
        u.Box(c + new Vector3(0.03f, 0f, 0f), new Vector3(0.08f, 1.05f, 0.82f), frame);
        u.Box(c + new Vector3(0.06f, 0f, 0f), new Vector3(0.04f, 0.85f, 0.62f), p.glass[rng.Next(p.glass.Length)]);
        u.Box(c + new Vector3(0.085f, 0f, 0f), new Vector3(0.02f, 0.85f, 0.05f), frame);
        u.Box(c + new Vector3(0.1f, -0.55f, 0f), new Vector3(0.16f, 0.07f, 0.92f), frame);
        if (rng.NextDouble() < 0.4)
            foreach (float sz in new[] { -1f, 1f })
                u.Box(c + new Vector3(0.05f, 0f, sz * 0.6f), new Vector3(0.05f, 0.9f, 0.34f), p.shutter[shutter]);
    }

    // Windows in the side walls too: in the middle of a row they're hidden against the neighbours, but the houses at
    // the ends of a row (the ones that face the smithy across the stream) would otherwise show a blank wall - and a
    // dark one at night.
    private static void SideWindows(Put u, CityPal p, float cx, float fd, float W, float y, System.Random rng, int frame)
    {
        int n = Mathf.Max(1, Mathf.FloorToInt((fd - 1.2f) / 2.2f));
        foreach (float sz in new[] { -1f, 1f })
            for (int k = 0; k < n; k++)
            {
                var c = new Vector3(cx - fd * 0.5f + (k + 0.5f) * fd / n, y, sz * W * 0.5f);
                u.Box(c + new Vector3(0f, 0f, sz * 0.03f), new Vector3(0.82f, 1.05f, 0.08f), frame);
                u.Box(c + new Vector3(0f, 0f, sz * 0.06f), new Vector3(0.62f, 0.85f, 0.04f), p.glass[rng.Next(p.glass.Length)]);
                u.Box(c + new Vector3(0f, -0.55f, sz * 0.1f), new Vector3(0.92f, 0.07f, 0.16f), frame);
            }
    }

    private static void CityRoof(Put u, CityPal p, TownSpec s, int wall, float cx, float fd, float W, float top, System.Random rng)
    {
        float rise = s.rise, t = 0.14f, ov = 0.35f;
        int roof = p.roof[s.roof], dark = p.roofDark[s.roof];
        float R = top + rise;
        if (s.roofType == 2)
        {
            // Hipped: sloping on all four sides.
            u.Frustum(new Vector3(cx, top + rise * 0.5f, 0f), fd + 0.6f, W + 0.6f, Mathf.Max(0.12f, fd - W), Mathf.Max(0.12f, W - fd), rise, roof);
            u.Box(new Vector3(cx, top + 0.04f, 0f), new Vector3(fd + 0.62f, 0.08f, W + 0.62f), dark);
        }
        else if (s.roofType == 0)
        {
            // Ridge along the street, the eaves over the pavement.
            float half = fd * 0.5f;
            u.Frustum(new Vector3(cx, top + rise * 0.5f, 0f), fd, W, 0.04f, W, rise, wall);
            float theta = Mathf.Atan2(rise, half), slab = Mathf.Sqrt(half * half + rise * rise) + ov;
            foreach (float sd in new[] { 1f, -1f })
            {
                Vector3 down = new Vector3(sd * Mathf.Cos(theta), -Mathf.Sin(theta), 0f), normal = new Vector3(sd * Mathf.Sin(theta), Mathf.Cos(theta), 0f);
                Vector3 c = new Vector3(cx + sd * half * 0.5f, (R + top) * 0.5f, 0f) + normal * (t * 0.5f) + down * (ov * 0.5f);
                var rot = Quaternion.Euler(0f, 0f, -sd * theta * Mathf.Rad2Deg);
                u.Box(c, new Vector3(slab, t, W + 0.5f), roof, rot);
                u.Box(c + down * (slab * 0.5f - 0.1f) + normal * 0.03f, new Vector3(0.2f, t, W + 0.52f), dark, rot);
            }
            u.Box(new Vector3(cx, R + 0.08f, 0f), new Vector3(0.2f, 0.14f, W + 0.55f), dark);
            if (W >= 5f && rng.NextDouble() < 0.5)
            {
                // A dormer on the street side.
                float dz = (float)(rng.NextDouble() - 0.5) * (W - 2.4f);
                float dx = cx + half * 0.45f, dy = top + rise * 0.42f;
                u.Box(new Vector3(dx, dy, dz), new Vector3(0.9f, 1.0f, 1.1f), wall);
                u.Box(new Vector3(dx + 0.46f, dy - 0.05f, dz), new Vector3(0.04f, 0.6f, 0.5f), p.glass[rng.Next(p.glass.Length)]);
                u.Frustum(new Vector3(dx, dy + 0.75f, dz), 1.1f, 1.3f, 1.1f, 0.05f, 0.5f, dark);
            }
        }
        else
        {
            // Gable end to the street.
            float half = W * 0.5f;
            u.Frustum(new Vector3(cx, top + rise * 0.5f, 0f), fd, W, fd, 0.04f, rise, wall);
            float theta = Mathf.Atan2(rise, half), slab = Mathf.Sqrt(half * half + rise * rise) + ov;
            foreach (float sd in new[] { 1f, -1f })
            {
                Vector3 down = new Vector3(0f, -Mathf.Sin(theta), sd * Mathf.Cos(theta)), normal = new Vector3(0f, Mathf.Cos(theta), sd * Mathf.Sin(theta));
                Vector3 c = new Vector3(cx, (R + top) * 0.5f, sd * half * 0.5f) + normal * (t * 0.5f) + down * (ov * 0.5f);
                var rot = Quaternion.Euler(sd * theta * Mathf.Rad2Deg, 0f, 0f);
                u.Box(c, new Vector3(fd + 0.5f, t, slab), roof, rot);
            }
            u.Box(new Vector3(cx, R + 0.08f, 0f), new Vector3(fd + 0.55f, 0.14f, 0.2f), dark);
            // A little window up in the gable.
            u.Box(new Vector3(cx + fd * 0.5f + 0.04f, top + rise * 0.35f, 0f), new Vector3(0.05f, 0.6f, 0.5f), p.glass[rng.Next(p.glass.Length)]);
        }
        if (s.chimney)
        {
            float cz = (float)(rng.NextDouble() - 0.5) * W * 0.6f;
            chimneyTops.Add(u.World(new Vector3(cx - fd * 0.2f, R + 1.02f, cz)));
            u.Box(new Vector3(cx - fd * 0.2f, R - rise * 0.25f + 0.6f, cz), new Vector3(0.55f, rise * 0.5f + 1.2f, 0.55f), p.stone);
            u.Box(new Vector3(cx - fd * 0.2f, R + 0.95f, cz), new Vector3(0.7f, 0.12f, 0.7f), p.stoneDark);
        }
    }

    // ---- streets -------------------------------------------------------------------------------------

    // Cobbled streets: flat slabs just above the meadow (the village's dirt paths sit at the same height, so the
    // two meet cleanly at the square).
    private static void Street(MeshBuilder b, CityPal p, Vector2 from, Vector2 to, float width)
    {
        Vector3 a = new Vector3(from.x, 0f, from.y), c = new Vector3(to.x, 0f, to.y);
        Vector3 d = c - a;
        var rot = Quaternion.LookRotation(d.normalized);
        b.Box((a + c) * 0.5f + Vector3.up * 0.016f, new Vector3(width, 0.032f, d.magnitude + width * 0.5f), p.cobble, rot);
        foreach (float s in new[] { -1f, 1f }) // gutters
            b.Box((a + c) * 0.5f + rot * new Vector3(s * (width * 0.5f - 0.2f), 0.034f, 0f), new Vector3(0.3f, 0.01f, d.magnitude), p.cobbleDark, rot);
        obstacles.Add(Obstacle.Segment(a, c, width * 0.5f));
    }

    private static void BuildCityStreets(Transform city)
    {
        var b = new MeshBuilder();
        var p = new CityPal(b);
        // The avenue, from the square to the gate and out along the road to the castle.
        Street(b, p, new Vector2(19f, AvenueZ), new Vector2(WallX + 8f, AvenueZ), AvenueHalf * 2f);
        Street(b, p, new Vector2(WallX + 8f, AvenueZ), new Vector2(CastleRoadStart.x, CastleRoadStart.y), 3.5f); // on to the foot of the castle road
        // Cross streets and the parallel streets between the rows of blocks.
        foreach (float x in new[] { 45.5f, 81.5f }) Street(b, p, new Vector2(x, -56f), new Vector2(x, 52f), 4.6f);
        foreach (float z in new[] { 13.25f, -20.25f }) // interrupted by the church and the guild hall
        {
            Street(b, p, new Vector2(24f, z), new Vector2(PlazaX.x, z), 4.2f);
            Street(b, p, new Vector2(PlazaX.y, z), new Vector2(WallX - 3f, z), 4.2f);
        }
        foreach (float z in new[] { 32.25f, -38.25f }) Street(b, p, new Vector2(24f, z), new Vector2(WallX - 3f, z), 4.2f);
        // Plaza paving.
        b.Box(new Vector3(PlazaCx, 0.018f, (PlazaNorth + PlazaSouth) * 0.5f), new Vector3(PlazaX.y - PlazaX.x, 0.036f, PlazaNorth - PlazaSouth), p.cobble);
        obstacles.Add(Obstacle.Rect(new Vector3(PlazaCx, 0f, (PlazaNorth + PlazaSouth) * 0.5f), 0f, (PlazaX.y - PlazaX.x) * 0.5f, (PlazaNorth - PlazaSouth) * 0.5f));
        // The village road runs on out of town both ways (the crowd's travellers come and go along it).
        // South, down the river's east bank and away; north, up the city street past the watermill and out along the river.
        var south = new[] { new Vector2(16.8f, -27f), new Vector2(19.5f, -60f), new Vector2(22f, -80f), new Vector2(30f, -110f), new Vector2(40f, -150f), new Vector2(48f, -200f), new Vector2(56f, -250f), new Vector2(62f, -300f), new Vector2(72f, -420f) };
        for (int i = 0; i < south.Length - 1; i++) Street(b, p, south[i], south[i + 1], 2.6f);
        Street(b, p, new Vector2(24f, 13.25f), new Vector2(24f, 41.5f), 3.2f);
        for (int i = 0; i < NorthRoad.Length - 1; i++) Street(b, p, NorthRoad[i], NorthRoad[i + 1], 2.6f); // (VillageBuilder.NorthQuarter.cs)
        // Lanes from the road into the city's back streets.
        Street(b, p, new Vector2(16.3f, 12.5f), new Vector2(24f, 13.25f), 2.4f);
        Street(b, p, new Vector2(17f, -18f), new Vector2(24f, -20.25f), 2.4f);
        Street(b, p, new Vector2(16.8f, -27f), new Vector2(24f, -38.25f), 2.4f);
        Place(city, "CityStreets", b.Build("CityStreets"));
    }

    // ---- blocks of houses ---------------------------------------------------------------------------------

    // How tall a building may be at this spot: in front of the castle (within ~32 degrees of the view straight ahead)
    // it must stay under the roofline angle, so the castle rises above it; elsewhere it's free.
    private static int MaxFloors(Vector3 nearCorner, float rise)
    {
        Vector3 d = nearCorner - Seat; d.y = 0f;
        float ang = Vector3.Angle(Vector3.right, d);
        if (ang > 32f) return 4;
        float maxH = Seat.y + RooflineTan * d.magnitude;
        for (int f = 4; f >= 1; f--) if (3.0f + 2.6f * (f - 1) + rise + 0.3f <= maxH) return f;
        return 0;
    }

    private static TownSpec RandomTown(System.Random rng, float w, float d)
    {
        var s = new TownSpec
        {
            W = w, D = d,
            plaster = rng.Next(7), roof = rng.Next(5), roofType = rng.NextDouble() < 0.55 ? 0 : rng.NextDouble() < 0.6 ? 1 : 2,
            timber = rng.NextDouble() < 0.6, stoneGround = rng.NextDouble() < 0.4, awning = rng.Next(4), shutter = rng.Next(3),
            rise = 1.8f + (float)rng.NextDouble() * 1.2f,
        };
        s.jetty = s.timber && rng.NextDouble() < 0.6;
        s.shop = rng.NextDouble() < 0.35;
        s.place = s.shop ? "shop" : "home";
        s.chimney = rng.NextDouble() < 0.7;
        return s;
    }

    // Lines one side of a street with buildings whose fronts face it. `from`/`to` run along the street frontage; the
    // buildings stand on the side `outward` points away from.
    private static int Frontage(MeshBuilder b, CityPal p, System.Random rng, Vector3 from, Vector3 to, Vector3 facing, float depth,
                                int minFloors, int maxFloors, string forcePlace = null)
    {
        Vector3 along = (to - from); float len = along.magnitude; along /= len;
        float yaw = Quaternion.LookRotation(facing).eulerAngles.y - 90f; // local +X (the front) -> facing
        int count = 0;
        float x = 0f;
        while (x < len - 2.5f)
        {
            float w = Mathf.Min(4f + (float)rng.NextDouble() * 3.2f, len - x);
            if (len - x - w < 2.5f) w = len - x;
            var s = RandomTown(rng, w, depth - (float)rng.NextDouble() * 0.6f);
            if (forcePlace != null) s.place = forcePlace;
            Vector3 frontMid = from + along * (x + w * 0.5f);
            Vector3 center = frontMid - facing * (s.D * 0.5f);
            Vector3 nearest = Vector3.Distance(from + along * x, Seat) < Vector3.Distance(from + along * (x + w), Seat) ? from + along * x : from + along * (x + w);
            int cap = Mathf.Min(maxFloors, MaxFloors(nearest, s.rise));
            if (nearCap) cap = Mathf.Min(cap, NearCap(Vector3.Distance(new Vector3(nearest.x, 0f, nearest.z), SeatGround)));
            if (cap <= 0) { s.floors = 1; s.rise = 1.4f; s.roofType = 0; }
            else s.floors = Mathf.Clamp(rng.Next(minFloors, maxFloors + 1), 1, cap);
            var q = Quaternion.Euler(0f, yaw, 0f);
            var put = new Put(b, center, yaw);
            TownHouse(put, p, s, rng);
            Vector3 door = center + q * new Vector3(s.D * 0.5f + 0.5f, 0f, s.doorZ);
            cityDoors.Add((door, s.place));
            float h = s.Height;
            Vector3 ext = new Vector3(Mathf.Abs(facing.x) * s.D + Mathf.Abs(along.x) * w, 0f, Mathf.Abs(facing.z) * s.D + Mathf.Abs(along.z) * w);
            occluderBoxes.Add(new Bounds(center + Vector3.up * h * 0.5f, new Vector3(ext.x, h, ext.z)));
            obstacles.Add(Obstacle.Rect(center, yaw, s.D * 0.5f + 0.3f, w * 0.5f + 0.3f));
            x += w;
            count++;
        }
        return count;
    }

    // Fills a block's edges with buildings facing out onto the streets round it (courtyards in the middle).
    private static int Block(MeshBuilder b, CityPal p, System.Random rng, float x0, float x1, float z0, float z1, int minF, int maxF)
    {
        float depth = Mathf.Min(8f, Mathf.Min(x1 - x0, z1 - z0) * 0.5f);
        int n = 0;
        // North and south faces run the full width; west and east faces fit in between them.
        n += Frontage(b, p, rng, new Vector3(x0, 0f, z1), new Vector3(x1, 0f, z1), Vector3.forward, depth, minF, maxF);
        n += Frontage(b, p, rng, new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z0), Vector3.back, depth, minF, maxF);
        if (z1 - z0 - 2f * depth > 3f)
        {
            n += Frontage(b, p, rng, new Vector3(x0, 0f, z0 + depth), new Vector3(x0, 0f, z1 - depth), Vector3.left, depth, minF, maxF);
            n += Frontage(b, p, rng, new Vector3(x1, 0f, z0 + depth), new Vector3(x1, 0f, z1 - depth), Vector3.right, depth, minF, maxF);
        }
        return n;
    }

    private static int BuildCityBlocks(Transform city, System.Random rng)
    {
        var root = new GameObject("Districts").transform;
        root.SetParent(city, false);
        int n = 0, i = 0;
        var xs = new List<Vector2>(BlockX) { PlazaX };
        foreach (var bx in xs)
            foreach (var bz in BlockZ)
            {
                bool plazaCol = bx == PlazaX;
                // The plaza column: only the outermost blocks (the church and the guild hall take the inner ones).
                if (plazaCol && Mathf.Abs(bz.x + bz.y) < 60f) continue;
                var b = new MeshBuilder();
                var p = new CityPal(b);
                float far = Mathf.Max(Mathf.Abs(bz.x), Mathf.Abs(bz.y));
                int minF = far > 30f ? 2 : bx.x > 60f ? 2 : 1;
                int maxF = bx.x > 60f ? 4 : 3;
                if (useLibrary)
                {
                    var parent = new GameObject($"Block{i++}").transform;
                    parent.SetParent(root, false);
                    n += BlockPrefabs(parent, rng, bx.x, bx.y, bz.x, bz.y, minF, TownPool);
                    continue;
                }
                n += Block(b, p, rng, bx.x, bx.y, bz.x, bz.y, minF, maxF);
                Place(root, $"Block{i++}", b.Build("CityBlock"));
            }
        return n;
    }

    // ---- the plaza: fountain, stalls, church, guild hall ---------------------------------------------------------

    private static void BuildPlaza(Transform city, System.Random rng)
    {
        var root = new GameObject("Plaza").transform;
        root.SetParent(city, false);
        var b = new MeshBuilder();
        var p = new CityPal(b);
        var vp = new Pal(b);
        Vector3 f = new Vector3(PlazaCx, 0f, AvenueZ);
        // The fountain (Landmark_Fountain, sheet 04): octagonal basin, lion-spout column, arcaded top - the plaza's centre.
        if (Spawn("Landmark_Fountain", root, f, 0f) != null)
        {
            obstacles.Add(Obstacle.Circle(f, 3.5f));
            occluderBoxes.Add(new Bounds(f + Vector3.up * 0.7f, new Vector3(6.2f, 1.4f, 6.2f)));
            occluderBoxes.Add(new Bounds(f + Vector3.up * 3.2f, new Vector3(1.6f, 4f, 1.6f)));
        }
        else
        {
            b.Prism(f + Vector3.up * 0.35f, 8, 2.5f, 2.5f, 0.7f, p.stoneLight);
            b.Prism(f + Vector3.up * 0.66f, 8, 2.2f, 2.2f, 0.06f, p.water);
            b.Prism(f + Vector3.up * 1.3f, 8, 0.38f, 0.3f, 1.3f, p.stone);
            b.Prism(f + Vector3.up * 2.0f, 8, 0.95f, 0.55f, 0.3f, p.stoneLight);
            b.Prism(f + Vector3.up * 2.12f, 8, 0.8f, 0.8f, 0.04f, p.water);
            b.Ball(f + Vector3.up * 2.45f, 0.28f, p.gold);
            obstacles.Add(Obstacle.Circle(f, 3f));
            occluderBoxes.Add(new Bounds(f + Vector3.up * 0.4f, new Vector3(5f, 0.8f, 5f)));
        }
        // Market stalls round the fountain, facing it.
        foreach (var (pos, yaw) in new[]
        {
            (new Vector3(PlazaCx - 4.5f, 0f, AvenueZ - 8f), 90f), (new Vector3(PlazaCx + 4.5f, 0f, AvenueZ - 8f), 90f),
            (new Vector3(PlazaCx - 4.5f, 0f, AvenueZ + 8.5f), 270f), (new Vector3(PlazaCx + 4.5f, 0f, AvenueZ + 8.5f), 270f),
        })
        {
            MarketStall(new Put(b, pos, yaw), vp);
            Crate(new Put(b, pos + new Vector3(1.4f, 0f, 0.3f), 15f), vp, 0.5f);
            obstacles.Add(Obstacle.Circle(pos, 1.8f));
        }
        // Lanterns round the fountain and along the avenue.
        foreach (var lp in new[] { new Vector3(PlazaCx - 3.6f, 0f, AvenueZ + 3.6f), new Vector3(PlazaCx + 3.6f, 0f, AvenueZ - 3.6f) })
            LanternPost(new Put(b, lp, 0f), vp);
        for (float x = 30f; x < WallX - 4f; x += 12f)
        {
            if (x > PlazaX.x - 2f && x < PlazaX.y + 2f) continue;
            bool north = ((int)(x / 12f)) % 2 == 0;
            LanternPost(new Put(b, new Vector3(x, 0f, AvenueZ + (north ? AvenueHalf - 0.5f : -AvenueHalf + 0.5f)), north ? 180f : 0f), vp);
        }
        Place(root, "PlazaProps", b.Build("PlazaProps"));

        // The church on the north side (its tower is the second landmark, left of the castle), the guild hall opposite.
        b = new MeshBuilder();
        p = new CityPal(b);
        var churchFront = new Vector3(PlazaCx, 0f, PlazaNorth);
        var hallFront = new Vector3(PlazaCx, 0f, PlazaSouth);
        // The landmarks from sheets 02 + 03 face each other across the fountain: the church (its spire left of the castle
        // from the seat) and the clock house (its clock tower right of it). Scaled so each fills its 14 m plot like the
        // buildings they replace; PlaceBuilding registers the occluder, the obstacle and the door for the crowd.
        if (PF("Landmark_Church") != null && PF("Landmark_ClockHouse") != null)
        {
            // 20% taller than their plot-width scale: the spire and the clock tower have to clear the roofline between
            // them and the seat, like the menu art's clock tower rising over the town (Y scale can't shear under a yaw).
            foreach (var pl in new[]
                     {
                         PlaceBuilding("Landmark_Church", root, churchFront, 90f, "church", 0.85f),
                         PlaceBuilding("Landmark_ClockHouse", root, hallFront, 270f, "hall", 0.85f),
                     })
            {
                var t = pl.go.transform;
                var ls = t.localScale;
                var before = new List<Vector3>();
                foreach (var c in pl.info.chimneys) before.Add(t.TransformPoint(c));
                t.localScale = new Vector3(ls.x, ls.y * LandmarkLift, ls.z);
                for (int i = 0; i < before.Count; i++) // the smoke markers rise with the chimneys
                {
                    int k = chimneyTops.IndexOf(before[i]);
                    if (k >= 0) chimneyTops[k] = t.TransformPoint(pl.info.chimneys[i]);
                }
            }
            return;
        }
        Church(new Put(b, churchFront + Vector3.forward * 5.5f, 90f), p); // door (local x 5.56) on the plaza edge
        occluderBoxes.Add(new Bounds(churchFront + new Vector3(0f, 6f, 13f), new Vector3(9.5f, 12f, 16f)));
        occluderBoxes.Add(new Bounds(churchFront + new Vector3(0f, 9f, 2.5f), new Vector3(5.2f, 18f, 5.2f)));
        obstacles.Add(Obstacle.Rect(churchFront + Vector3.forward * 10.5f, 0f, 5.5f, 11f));
        cityDoors.Add((churchFront + Vector3.back * 0.6f, "church"));

        var hall = new TownSpec { W = 13.5f, D = 11f, floors = 3, stoneGround = true, timber = false, plaster = 3, roof = 2, roofType = 2, rise = 3f, chimney = false, place = "hall" };
        var hq = Quaternion.Euler(0f, 270f, 0f);
        var hallCenter = hallFront + Vector3.back * (hall.D * 0.5f);
        TownHouse(new Put(b, hallCenter, 270f), p, hall, rng);
        // A clock turret on the roof.
        var turret = hallCenter + Vector3.up * (hall.Height - 0.3f);
        b.Box(turret + Vector3.up * 1.3f, new Vector3(2.4f, 2.6f, 2.4f), p.stoneLight);
        b.Prism(turret + new Vector3(0f, 1.6f, 1.25f), 12, 0.7f, 0.7f, 0.06f, p.plaster[6], Quaternion.Euler(90f, 0f, 0f));
        b.Box(turret + new Vector3(0f, 1.75f, 1.3f), new Vector3(0.06f, 0.5f, 0.03f), p.iron);
        b.Frustum(turret + Vector3.up * 3.6f, 2.9f, 2.9f, 0.1f, 0.1f, 2f, p.roof[4]);
        cityDoors.Add((hallCenter + hq * new Vector3(hall.D * 0.5f + 0.5f, 0f, hall.doorZ), "hall"));
        occluderBoxes.Add(new Bounds(hallCenter + Vector3.up * hall.Height * 0.5f, new Vector3(hall.W, hall.Height, hall.D)));
        obstacles.Add(Obstacle.Rect(hallCenter, 0f, hall.W * 0.5f + 0.3f, hall.D * 0.5f + 0.3f));
        Place(root, "ChurchAndHall", b.Build("ChurchAndHall"));
    }

    // Local frame: the tower's front (the west door) at +X, nave stretching back along -X.
    private static void Church(Put u, CityPal p)
    {
        const float L = 16f, Wn = 9f, H = 7.5f, rise = 4.2f;
        float nx = -L * 0.5f + 0.5f; // nave centre (tower stands in front, 0.5 .. 5.5)
        u.Box(new Vector3(nx, 0.2f, 0f), new Vector3(L + 0.3f, 0.4f, Wn + 0.3f), p.stoneDark);
        u.Box(new Vector3(nx, H * 0.5f, 0f), new Vector3(L, H, Wn), p.stoneLight);
        for (int i = 0; i < 3; i++)
        {
            float x = nx - L * 0.5f + 2.5f + i * 4.3f;
            foreach (float sz in new[] { -1f, 1f })
            {
                u.Box(new Vector3(x, H * 0.4f, sz * (Wn * 0.5f + 0.35f)), new Vector3(0.9f, H * 0.8f, 0.7f), p.stone);
                u.Box(new Vector3(x + 2.15f, H * 0.55f, sz * (Wn * 0.5f + 0.03f)), new Vector3(1.1f, 3.4f, 0.06f), p.stained);
            }
        }
        // Nave roof, ridge along X.
        float half = Wn * 0.5f, theta = Mathf.Atan2(rise, half), slab = Mathf.Sqrt(half * half + rise * rise) + 0.4f;
        u.Frustum(new Vector3(nx, H + rise * 0.5f, 0f), L, Wn, L, 0.04f, rise, p.stoneLight);
        foreach (float sd in new[] { 1f, -1f })
        {
            Vector3 down = new Vector3(0f, -Mathf.Sin(theta), sd * Mathf.Cos(theta)), normal = new Vector3(0f, Mathf.Cos(theta), sd * Mathf.Sin(theta));
            u.Box(new Vector3(nx, H + rise * 0.5f, sd * half * 0.5f) + normal * 0.08f + down * 0.2f, new Vector3(L + 0.6f, 0.16f, slab), p.roof[2],
                Quaternion.Euler(sd * theta * Mathf.Rad2Deg, 0f, 0f));
        }
        // Tower, belfry and spire.
        const float TH = 13f;
        u.Box(new Vector3(3f, TH * 0.5f, 0f), new Vector3(5f, TH, 5f), p.stone);
        u.Box(new Vector3(3f, TH + 0.15f, 0f), new Vector3(5.4f, 0.3f, 5.4f), p.stoneDark);
        foreach (var dir in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
            u.Box(new Vector3(3f, TH - 1.8f, 0f) + dir * 2.52f, new Vector3(dir.x != 0f ? 0.06f : 1.2f, 1.9f, dir.z != 0f ? 0.06f : 1.2f), p.dark);
        u.Frustum(new Vector3(3f, TH + 0.3f + 3.2f, 0f), 5.2f, 5.2f, 0.12f, 0.12f, 6.4f, p.roof[4]);
        u.Box(new Vector3(3f, TH + 7.1f, 0f), new Vector3(0.08f, 1.2f, 0.08f), p.gold);
        u.Box(new Vector3(3f, TH + 7.3f, 0f), new Vector3(0.08f, 0.08f, 0.6f), p.gold);
        // West door and a rose window.
        u.Box(new Vector3(5.52f, 1.6f, 0f), new Vector3(0.08f, 3.2f, 2.0f), p.stoneDark);
        u.Box(new Vector3(5.56f, 1.5f, 0f), new Vector3(0.06f, 2.9f, 1.6f), p.door);
        u.Prism(new Vector3(5.53f, 7f, 0f), 12, 1.1f, 1.1f, 0.08f, p.stained, Quaternion.Euler(0f, 0f, 90f));
        u.Box(new Vector3(6.0f, 0.12f, 0f), new Vector3(1.0f, 0.24f, 3.2f), p.stone);
    }

    // ---- the city wall and its gate --------------------------------------------------------------------------

    private static void WallRun(MeshBuilder b, CityPal p, Vector3 a, Vector3 c, float h, float thick)
    {
        Vector3 d = c - a; float len = d.magnitude;
        var rot = Quaternion.LookRotation(d / len);
        b.Box((a + c) * 0.5f + Vector3.up * h * 0.5f, new Vector3(thick, h, len), p.stone, rot);
        b.Box((a + c) * 0.5f + Vector3.up * (h + 0.1f), new Vector3(thick + 0.3f, 0.2f, len), p.stoneDark, rot);
        int merlons = Mathf.FloorToInt(len / 1.6f);
        for (int i = 0; i < merlons; i++)
        {
            Vector3 m = a + d * ((i + 0.5f) / merlons);
            b.Box(m + Vector3.up * (h + 0.65f) + rot * new Vector3(-thick * 0.5f + 0.25f, 0f, 0f), new Vector3(0.5f, 0.9f, 0.8f), p.stone, rot);
        }
        var bounds = new Bounds((a + c) * 0.5f + Vector3.up * h * 0.5f, Vector3.zero);
        bounds.Encapsulate(a + rot * new Vector3(thick * 0.5f, 0f, 0f)); bounds.Encapsulate(a - rot * new Vector3(thick * 0.5f, 0f, 0f) + Vector3.up * h);
        bounds.Encapsulate(c + rot * new Vector3(thick * 0.5f, 0f, 0f)); bounds.Encapsulate(c - rot * new Vector3(thick * 0.5f, 0f, 0f) + Vector3.up * h);
        occluderBoxes.Add(bounds);
        obstacles.Add(Obstacle.Segment(a, c, thick * 0.5f + 0.5f));
    }

    private static void RoundTower(MeshBuilder b, int stone, int roof, Vector3 at, float r, float h, float cone)
    {
        b.Prism(at + Vector3.up * h * 0.5f, 10, r, r * 0.95f, h, stone);
        b.Prism(at + Vector3.up * (h + 0.15f), 10, r + 0.3f, r + 0.3f, 0.3f, stone);
        b.Prism(at + Vector3.up * (h + 0.3f + cone * 0.5f), 10, r + 0.55f, 0.08f, cone, roof);
    }

    private static void BuildCityWall(Transform city)
    {
        var b = new MeshBuilder();
        var p = new CityPal(b);
        const float H = 8.5f, T = 2.6f, gateHalf = 4f;
        float north = 50f, south = -56f;
        WallRun(b, p, new Vector3(WallX, 0f, south), new Vector3(WallX, 0f, AvenueZ - gateHalf), H, T);
        WallRun(b, p, new Vector3(WallX, 0f, AvenueZ + gateHalf), new Vector3(WallX, 0f, north), H, T);
        // The wall turns back west at both ends, behind the last blocks.
        WallRun(b, p, new Vector3(WallX, 0f, north), new Vector3(WallX - 34f, 0f, north + 9f), H, T);
        WallRun(b, p, new Vector3(WallX, 0f, south), new Vector3(WallX - 34f, 0f, south - 9f), H, T);
        foreach (float z in new[] { south, -30f, 26f, north })
        {
            RoundTower(b, p.stone, p.roof[2], new Vector3(WallX, 0f, z), 3f, 12f, 4.5f);
            occluderBoxes.Add(new Bounds(new Vector3(WallX, 6f, z), new Vector3(6f, 12f, 6f)));
        }
        RoundTower(b, p.stone, p.roof[2], new Vector3(WallX - 34f, 0f, north + 9f), 2.8f, 11f, 4f);
        RoundTower(b, p.stone, p.roof[2], new Vector3(WallX - 34f, 0f, south - 9f), 2.8f, 11f, 4f);
        // Gatehouse: two towers and the arch between them, gates standing open.
        foreach (float s in new[] { -1f, 1f })
        {
            var t = new Vector3(WallX, 0f, AvenueZ + s * (gateHalf + 1.6f));
            RoundTower(b, p.stone, p.roof[4], t, 3.2f, 14f, 5f);
            occluderBoxes.Add(new Bounds(t + Vector3.up * 7f, new Vector3(6.4f, 14f, 6.4f)));
            b.Box(new Vector3(WallX - 0.2f, 0f, AvenueZ + s * (gateHalf - 0.3f)) + Vector3.up * 3f, new Vector3(T + 0.2f, 6f, 0.6f), p.stoneDark);
        }
        b.Box(new Vector3(WallX, 8.2f, AvenueZ), new Vector3(T + 0.4f, 4.4f, gateHalf * 2f + 1f), p.stone);
        b.Box(new Vector3(WallX - T * 0.5f - 0.03f, 6.6f, AvenueZ), new Vector3(0.06f, 1.2f, gateHalf * 2f - 0.4f), p.dark);
        b.Box(new Vector3(WallX - T * 0.5f - 0.05f, 8.6f, AvenueZ), new Vector3(0.05f, 1.6f, 1.4f), p.banner);
        occluderBoxes.Add(new Bounds(new Vector3(WallX, 8.2f, AvenueZ), new Vector3(T, 4.4f, gateHalf * 2f)));
        Place(city, "CityWall", b.Build("CityWall"));
    }

    // (the castle and its hill: VillageBuilder.Castle.cs)

    // ---- skylines beyond the other three seat views ----------------------------------------------------------

    private static int BuildSkylines(Transform city, System.Random rng)
    {
        var root = new GameObject("Skylines").transform;
        root.SetParent(city, false);
        int n = 0;
        // Behind the forge (+Z), behind the storage rack (-Z), behind the smith's house (-X): the city carries on past
        // the gardens and trees - two staggered rows each, the back row taller, with a tower or two among them.
        var rows = new[]
        {
            // (the river runs through between x 3 and x 24; these stop short of the west quay's row of houses facing
            // the water - BuildRiverfront)
            (from: new Vector3(-50f, 0f, 46f), to: new Vector3(-5f, 0f, 46f), facing: Vector3.back, minF: 2, maxF: 3),
            (from: new Vector3(-44f, 0f, 58f), to: new Vector3(-5f, 0f, 58f), facing: Vector3.back, minF: 3, maxF: 4),
            // South: only behind the barn - the south-west is the city's edge, open to the farmland (BuildCityEdge).
            (from: new Vector3(-12f, 0f, -50f), to: new Vector3(3f, 0f, -50f), facing: Vector3.forward, minF: 2, maxF: 3),
            (from: new Vector3(-12f, 0f, -62f), to: new Vector3(3f, 0f, -62f), facing: Vector3.forward, minF: 3, maxF: 4),
            (from: new Vector3(-64f, 0f, -14f), to: new Vector3(-64f, 0f, 40f), facing: Vector3.right, minF: 2, maxF: 3),
            (from: new Vector3(-76f, 0f, -12f), to: new Vector3(-76f, 0f, 34f), facing: Vector3.right, minF: 3, maxF: 4),
        };
        int i = 0;
        foreach (var r in rows)
        {
            if (useLibrary)
            {
                var parent = new GameObject($"Skyline{i++}").transform;
                parent.SetParent(root, false);
                n += FrontagePrefabs(parent, rng, r.from, r.to, r.facing, 8f, r.minF, TownPool, null).count;
                continue;
            }
            var b = new MeshBuilder();
            var p = new CityPal(b);
            n += Frontage(b, p, rng, r.from, r.to, r.facing, 8f, r.minF, r.maxF);
            Place(root, $"Skyline{i++}", b.Build("Skyline"));
        }
        // A bell tower and a watchtower among the rooftops.
        var tb = new MeshBuilder();
        var tp = new CityPal(tb);
        foreach (var (at, h) in new[] { (new Vector3(-18f, 0f, 70f), 17f), (new Vector3(0f, 0f, -74f), 15f), (new Vector3(-88f, 0f, 12f), 16f) })
        {
            tb.Box(at + Vector3.up * h * 0.5f, new Vector3(4.2f, h, 4.2f), tp.stone);
            tb.Box(at + Vector3.up * (h - 1.5f), new Vector3(4.26f, 1.6f, 1.2f), tp.dark);
            tb.Box(at + Vector3.up * (h - 1.5f), new Vector3(1.2f, 1.6f, 4.26f), tp.dark);
            tb.Frustum(at + Vector3.up * (h + 2.5f), 4.6f, 4.6f, 0.1f, 0.1f, 5f, tp.roof[rng.Next(5)]);
        }
        Place(root, "Towers", tb.Build("SkylineTowers"));
        return n;
    }

    // Forested foothills, then two snow-capped mountain ranges all round (VillageBuilder.Backdrop.cs) - the menu art's
    // layered backdrop, replacing the old ring of cone hills.
    private static void BuildCountryside(Transform city)
    {
        BuildMountains(city);

        // The land runs on under the ranges (and under the river's far reaches), so no edge of the ground shows in a
        // valley. Named MeadowGround so the seasons recolour it with the rest.
        var gb = new MeshBuilder();
        int grass = gb.Paint(GameObject.Find("MeadowGround") is GameObject mg ? mg.GetComponent<Renderer>().sharedMaterial.color : C(0.30f, 0.42f, 0.19f), 0.05f);
        gb.Prism(new Vector3(4f, -0.3f, -1f), 48, 2400f, 2400f, 0.1f, grass);
        var far = Place(city, "FarGround", gb.Build("FarGround"));
        far.name = "MeadowGround";
    }

    // Real lights are few (DayCycle adds a point light at each LanternLights marker): the plaza and the near avenue.
    // Every other lantern and window only glows.
    private static void BuildCityLights(Transform village)
    {
        var markers = village.Find("LanternLights");
        if (markers == null) return;
        foreach (var at in new[]
        {
            new Vector3(PlazaCx - 3.6f, 0f, AvenueZ + 3.6f), new Vector3(PlazaCx + 3.6f, 0f, AvenueZ - 3.6f),
            new Vector3(30f, 0f, AvenueZ + AvenueHalf - 0.5f), new Vector3(42f, 0f, AvenueZ - AvenueHalf + 0.5f),
        })
        {
            var t = new GameObject("LanternLight").transform;
            t.SetParent(markers, false);
            t.position = LanternFlame(at, 0f);
        }
        if (millLantern.HasValue) // a little warm light on the bank at the watermill's door
        {
            var t = new GameObject("LanternLight").transform;
            t.SetParent(markers, false);
            t.position = millLantern.Value;
        }
    }

    // ---- the crowd's street graph ----------------------------------------------------------------------------

    private class NavBuilder
    {
        public readonly Transform root;
        private readonly Dictionary<Vector2Int, CityNavNode> byKey = new Dictionary<Vector2Int, CityNavNode>();
        public readonly List<CityNavNode> streets = new List<CityNavNode>();

        public NavBuilder(Transform root) { this.root = root; }

        public CityNavNode Node(Vector2 at, CityNodeKind kind = CityNodeKind.Street, string place = "", float spread = 1f)
        {
            var key = new Vector2Int(Mathf.RoundToInt(at.x * 4f), Mathf.RoundToInt(at.y * 4f));
            if (byKey.TryGetValue(key, out var n))
            {
                if (kind != CityNodeKind.Street) { n.kind = kind; n.place = place; }
                n.spread = Mathf.Max(n.spread, spread);
                return n;
            }
            var go = new GameObject($"{kind}_{(string.IsNullOrEmpty(place) ? "street" : place)}");
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(at.x, 0f, at.y);
            n = go.AddComponent<CityNavNode>();
            n.kind = kind; n.place = place; n.spread = spread;
            byKey[key] = n;
            if (kind == CityNodeKind.Street || kind == CityNodeKind.Edge) streets.Add(n);
            return n;
        }

        public static void Link(CityNavNode a, CityNavNode b)
        {
            if (a == b) return;
            if (!a.links.Contains(b)) a.links.Add(b);
            if (!b.links.Contains(a)) b.links.Add(a);
        }

        // A street through these points, with a node at least every `step` metres so doors find one close by.
        public void Street(float spread, float step, params Vector2[] pts)
        {
            CityNavNode prev = null;
            for (int i = 0; i < pts.Length; i++)
            {
                if (i > 0)
                {
                    int parts = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(pts[i - 1], pts[i]) / step));
                    for (int k = 1; k < parts; k++)
                    {
                        var mid = Node(Vector2.Lerp(pts[i - 1], pts[i], k / (float)parts), CityNodeKind.Street, "", spread);
                        Link(prev, mid); prev = mid;
                    }
                }
                var n = Node(pts[i], CityNodeKind.Street, "", spread);
                if (prev != null) Link(prev, n);
                prev = n;
            }
        }

        public CityNavNode Nearest(Vector3 p, float maxDist)
        {
            CityNavNode best = null; float bd = maxDist;
            foreach (var n in streets)
            {
                float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(n.Position.x, n.Position.z));
                if (d < bd && !CrossesRiver(p, n.Position)) { bd = d; best = n; } // never a link through the water
            }
            return best;
        }
    }

    // Does the straight line between two points cross the river (anywhere but on a bridge)?
    private static bool CrossesRiver(Vector3 a, Vector3 c)
    {
        Vector2 p = new Vector2(a.x, a.z), q = new Vector2(c.x, c.z);
        if (DeckHeightAt(a) > 0f && DeckHeightAt(c) > 0f) return false;
        for (int i = 0; i < stream.Count - 1; i++)
        {
            if (Mathf.Abs(stream[i].z) > RiverDetail + 30f) continue;
            Vector2 r = new Vector2(stream[i].x, stream[i].z), s = new Vector2(stream[i + 1].x, stream[i + 1].z);
            if (SegmentsCross(p, q, r, s)) return true;
        }
        return false;
    }

    private static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;
        float d1 = Cross(b - a, c - a), d2 = Cross(b - a, d - a), d3 = Cross(d - c, a - c), d4 = Cross(d - c, b - c);
        return ((d1 > 0f) != (d2 > 0f)) && ((d3 > 0f) != (d4 > 0f));
    }

    private static void BuildCityNav(Transform city)
    {
        var root = new GameObject("CityNav").transform;
        root.SetParent(city, false);
        var nb = new NavBuilder(root);
        Vector2 V(float x, float z) => new Vector2(x, z);

        // The village road, running out of town both ways (the far ends are where travellers come and go).
        nb.Street(1.0f, 9f, V(30f, -110f), V(22f, -80f), V(19.5f, -60f), V(16.8f, -27f), V(17f, -18f), V(16.7f, -11f), V(16f, -3.5f), V(16f, 6f), V(16.3f, 12.5f),
                  V(24f, 13.25f), V(24f, 32.25f), V(24f, 41.5f), NorthRoad[1], NorthRoad[2], NorthRoad[3], NorthRoad[4], NorthRoad[5]);
        nb.Node(V(30f, -110f), CityNodeKind.Edge, "road");
        nb.Node(NorthRoad[5], CityNodeKind.Edge, "road");
        // The customer lane over the bridge to the smithy's front, and the lanes past it.
        nb.Street(0.5f, 9f, V(16f, -3.5f), V(13.8f, -3.5f), V(9.6f, -3.5f), V(7.3f, -3.4f));
        nb.Street(0.5f, 9f, V(7.3f, -3.4f), V(6.2f, 0f), V(6.1f, 3.5f), V(5f, 9f), V(2.5f, 12.5f), V(-7f, 12.8f));
        nb.Street(0.4f, 9f, V(2.5f, 12.5f), V(10.4f, 12.5f), V(14.2f, 12.5f), V(16.3f, 12.5f));
        nb.Street(0.5f, 9f, V(7.3f, -3.4f), V(6.4f, -5.5f), V(6.3f, -9f), V(5.6f, -13.8f), V(5.5f, -16.4f));
        nb.Street(0.4f, 9f, V(5.6f, -13.8f), V(0f, -13.9f), V(-6f, -14f));
        // Back lanes from the road into the city.
        nb.Street(0.6f, 9f, V(17f, -18f), V(24f, -20.25f));
        nb.Street(0.6f, 9f, V(16.8f, -27f), V(24f, -38.25f));
        // The avenue to the gate and out onto the castle road (the far end, round the wall, is where the gate's
        // travellers come from - out of sight of the seat behind the wall).
        nb.Street(2.2f, 9f, V(16f, -3.5f), V(19.5f, -3.5f), V(26f, -3.5f), V(45.5f, -3.5f), V(PlazaX.x, -3.5f));
        nb.Street(2.2f, 9f, V(PlazaX.y, -3.5f), V(81.5f, -3.5f), V(WallX + 3f, -3.5f), V(WallX + 9f, -3.5f), V(WallX + 11f, -16f));
        nb.Node(V(WallX + 11f, -16f), CityNodeKind.Edge, "gate");
        // Round the plaza (its middle is the fountain).
        var fountain = nb.Node(V(PlazaCx, -3.5f), CityNodeKind.Spot, "plaza", 3.4f);
        nb.Street(1.5f, 8f, V(PlazaX.x, -3.5f), V(PlazaX.x + 1f, 4.5f), V(PlazaCx, 5.6f), V(PlazaX.y - 1f, 4.5f), V(PlazaX.y, -3.5f),
                  V(PlazaX.y - 1f, -11.5f), V(PlazaCx, -12.5f), V(PlazaX.x + 1f, -11.5f), V(PlazaX.x, -3.5f));
        foreach (var at in new[] { V(PlazaX.x, -3.5f), V(PlazaX.y, -3.5f), V(PlazaCx, 5.6f), V(PlazaCx, -12.5f) }) NavBuilder.Link(nb.Node(at), fountain);
        foreach (var (at, from) in new[] { (V(PlazaCx - 4.5f, -9.6f), V(PlazaX.x + 1f, -11.5f)), (V(PlazaCx + 4.5f, -9.6f), V(PlazaX.y - 1f, -11.5f)),
                                           (V(PlazaCx - 4.5f, 2.6f), V(PlazaX.x + 1f, 4.5f)), (V(PlazaCx + 4.5f, 2.6f), V(PlazaX.y - 1f, 4.5f)) })
            NavBuilder.Link(nb.Node(at, CityNodeKind.Spot, "market", 0.7f), nb.Node(from));
        // Cross streets and the parallel streets (split by the church and the guild hall), with the city running on
        // beyond the last blocks at the cross streets' far ends.
        foreach (float x in new[] { 45.5f, 81.5f })
        {
            nb.Street(1.4f, 9f, V(x, -56f), V(x, -38.25f), V(x, -20.25f), V(x, -3.5f), V(x, 13.25f), V(x, 32.25f), V(x, 52f));
            nb.Node(V(x, -56f), CityNodeKind.Edge, "city");
            nb.Node(V(x, 52f), CityNodeKind.Edge, "city");
        }
        foreach (float z in new[] { 13.25f, -20.25f })
        {
            nb.Street(1.3f, 9f, V(24f, z), V(45.5f, z), V(PlazaX.x - 1f, z));
            nb.Street(1.3f, 9f, V(PlazaX.y + 1f, z), V(81.5f, z), V(WallX - 3f, z));
        }
        foreach (float z in new[] { 32.25f, -38.25f }) nb.Street(1.3f, 9f, V(24f, z), V(45.5f, z), V(81.5f, z), V(WallX - 3f, z));

        // Places on the square and by the smithy.
        NavBuilder.Link(nb.Node(V(17.9f, -0.4f), CityNodeKind.Spot, "well", 0.8f), nb.Node(V(16f, -3.5f)));
        NavBuilder.Link(nb.Node(V(17.5f, -7.4f), CityNodeKind.Spot, "market", 0.6f), nb.Node(V(16f, -3.5f)));
        NavBuilder.Link(nb.Node(V(6.6f, -0.3f), CityNodeKind.Spot, "smithy", 0.4f), nb.Node(V(7.3f, -3.4f))); // by the sign, not in the middle of the view
        NavBuilder.Link(nb.Node(V(5.5f, -16.4f), CityNodeKind.Door, "barn"), nb.Node(V(5.6f, -13.8f)));

        // The workshop district's streets (VillageBuilder.Outskirts.cs), and the track out through the south-west gate.
        var roadNorth = nb.Nearest(new Vector3(20f, 0f, 41.5f), 6f);
        nb.Street(0.7f, 9f, V(-7f, 12.8f), V(-22f, 12.6f), V(-43.2f, 12.5f), V(-60f, 12.5f));
        nb.Street(0.5f, 9f, V(10.4f, 12.5f), V(7.6f, 15f), V(7.6f, 24f)); // the riverside path up to the second street
        nb.Street(0.8f, 9f, V(7.6f, 24f), V(-22f, 24f), V(-43.2f, 24f), V(-62f, 24f));
        StreamAt(41.5f, out float n3x, out float n3w);
        float n3half = n3w * 0.5f + 1.8f;
        nb.Street(0.8f, 9f, V(24f, 41.5f), V(n3x + n3half - 0.3f, 41.5f), V(n3x, 41.5f), V(n3x - n3half + 0.3f, 41.5f), V(-22f, 41.5f), V(-43.2f, 41.5f), V(-62f, 41.5f));
        if (roadNorth != null) NavBuilder.Link(nb.Node(V(24f, 41.5f)), roadNorth);
        nb.Street(0.8f, 9f, V(-22f, -12f), V(-22f, 12.6f), V(-22f, 24f), V(-22f, 41.5f));
        nb.Street(0.8f, 9f, V(-43.2f, -12f), V(-43.2f, 12.5f), V(-43.2f, 24f), V(-43.2f, 41.5f));
        nb.Street(0.6f, 9f, V(-43.2f, -12f), V(-22f, -12f));
        nb.Street(0.6f, 9f, V(-6f, -14f), V(-14f, -18f), V(-24f, -30f), V(-36f, -40f), V(-46f, -46f), V(-62f, -64f));
        nb.Node(V(-62f, -64f), CityNodeKind.Edge, "farm");
        // The west bank's riverside path, from the farm lane down to the tannery (BuildRiverside lays the path).
        StreamAt(-44f, out float tanX, out float tanW);
        float tanEdge = tanX - tanW * 0.5f;
        nb.Street(0.5f, 9f, V(0f, -13.9f), V(-0.4f, -24f), V(1.2f, -33f), V(tanEdge - 3.5f, -38.5f), V(tanEdge - 1.6f, -40f));
        AddRiverfrontNav(nb); // the south and town bridges' streets, the market quay (VillageBuilder.Riverfront.cs)

        // Doors: every town building's, and the village houses' (the inn is the old town's tavern).
        int doors = 0, city2 = 0;
        var placeCount = new Dictionary<string, int>();
        foreach (var (pos, place0) in cityDoors)
        {
            string place = place0;
            // A few of the city's houses are taverns and workshops.
            if (place == "home" && (city2++ % 9) == 4) place = "tavern";
            else if (place == "home" && (city2 % 7) == 3) place = "workshop";
            var near = nb.Nearest(pos, 7f);
            if (near == null) continue;
            NavBuilder.Link(nb.Node(new Vector2(pos.x, pos.z), CityNodeKind.Door, place, 0.3f), near);
            placeCount[place] = placeCount.TryGetValue(place, out int c) ? c + 1 : 1;
            doors++;
        }
        foreach (var (spec, go) in villageHouses)
        {
            Vector3 door = spec.door ?? spec.pos + Quaternion.Euler(0f, spec.yaw, 0f) * new Vector3(spec.D * 0.5f + 0.5f, 0f, spec.doorZ);
            door.y = 0f;
            var near = nb.Nearest(door, 6f);
            if (near == null) continue; // the smith's neighbour at the back keeps to themselves
            NavBuilder.Link(nb.Node(new Vector2(door.x, door.z), CityNodeKind.Door, spec.innSign ? "tavern" : "home", 0.3f), near);
            doors++;
        }
        AddLifeSpots(nb); // benches, riverbanks, bridge rails, the mill, the gates (VillageBuilder.Life.cs)
        // Walkers keep to the decks of the bridges (the arch lifts them a little), never the water.
        foreach (Transform t in root) { var q = t.position; q.y = DeckHeightAt(q); t.position = q; }
        int wet = 0;
        foreach (Transform t in root)
            foreach (var l in t.GetComponent<CityNavNode>().links)
                if (CrossesRiver(t.position, l.Position)) { wet++; Debug.LogWarning($"[City] street link through the river: {t.position} - {l.Position}"); }
        Debug.Log($"[City] street links crossing the river off a bridge: {wet}");
        Debug.Log($"[City] street graph: {root.childCount} nodes, {doors} doors ({string.Join(", ", placeCount)})");
    }
}
