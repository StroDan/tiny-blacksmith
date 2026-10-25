using System.Collections.Generic;
using UnityEngine;

// Around the smithy, and beyond the city's south-western edge (part of VillageBuilder, rebuilt with the "City" root).
//
//  - The workshop district: the smithy was a little village sitting in a meadow in the middle of the city. Now the
//    lanes round it are lined with small houses and workshops (one storey near the smithy, two or three further
//    out, then the tall rows beyond), with yards, carts and barrels - the forge keeps its working space (its yard,
//    the lane to the table, the stream bank), but it plainly stands in a city street.
//  - The river doesn't stop in the grass any more: it comes out from under a tannery built across it at the south
//    end, and goes in under a watermill (its wheel turning) at the north end - the way a town's river runs on under
//    the houses.
//  - The south-west is where the city ends: farmsteads and a gatehouse on the road out, then big wheat fields on
//    rising ground with windmills turning on the ridge. From the seat they show on the right of the storage view and
//    the left of the house view - never in front of the forge or the table.
public static partial class VillageBuilder
{
    private static bool nearCap; // Frontage: keep houses low close to the smithy (the neighbourhood only)

    // Nearest-first: one storey within ~20 m of the seat, two by ~30 m, three by ~40 m.
    private static int NearCap(float d) => Mathf.Clamp(Mathf.FloorToInt((d - 8f) / 11f), 1, 4);

    // ---- the workshop district round the smithy ----------------------------------------------------------------

    // Blocks either side of the new streets west and north of the smithy (see BuildCityNav for the streets):
    // north lane z 12.5, N2 z 24, N3 z 41.5; W1 x -22, W2 x -44.
    private static readonly Vector4[] NeighbourhoodBlocks =
    {
        new Vector4(-19.5f, -9f, 14.8f, 22f),   // behind the lane, west of the forge yard's northern houses
        new Vector4(-40f, -24.5f, 14.8f, 22f),
        new Vector4(-60f, -46.5f, 14.8f, 22f),
        new Vector4(-19.5f, 4f, 26f, 39.5f),    // the second row behind the forge (kept back from the river bank)
        new Vector4(-40f, -24.5f, 26f, 39.5f),
        new Vector4(-60f, -46.5f, 26f, 39.5f),
        new Vector4(-40f, -26f, -10f, 10f),     // west of the smith's neighbour
        new Vector4(-60f, -46.5f, -10f, 10f),
    };

    private static int BuildNeighbourhood(Transform city)
    {
        var root = new GameObject("WorkshopDistrict").transform;
        root.SetParent(city, false);
        var rng = new System.Random(4321);
        int n = 0, i = 0;
        nearCap = true;
        foreach (var bk in NeighbourhoodBlocks)
        {
            if (useLibrary)
            {
                // cottages and small town houses; the district's tavern opens onto the second street (N2), behind the forge
                var parent = new GameObject($"District{i}").transform;
                parent.SetParent(root, false);
                n += BlockPrefabs(parent, rng, bk.x, bk.y, bk.z, bk.w, 1, NeighbourPool, i == 3 ? "Pack_Tavern" : null);
                i++;
                continue;
            }
            var b = new MeshBuilder();
            var p = new CityPal(b);
            n += Block(b, p, rng, bk.x, bk.y, bk.z, bk.w, 1, 3);
            Place(root, $"District{i++}", b.Build("District"));
        }
        // A short row of workshops across the stream from the barn, facing the road.
        if (useLibrary)
        {
            var parent = new GameObject("RoadWorkshops").transform;
            parent.SetParent(root, false);
            n += FrontagePrefabs(parent, rng, new Vector3(18.2f, 0f, -20f), new Vector3(18.2f, 0f, -34f), Vector3.left, 6.5f, 1, WorkshopPool, "workshop").count;
        }
        else
        {
            var b = new MeshBuilder();
            var p = new CityPal(b);
            n += Frontage(b, p, rng, new Vector3(18.2f, 0f, -34f), new Vector3(18.2f, 0f, -20f), Vector3.left, 6.5f, 1, 2, "workshop");
            Place(root, "RoadWorkshops", b.Build("RoadWorkshops"));
        }
        nearCap = false;

        // The lanes: packed-earth streets (the old village lanes are dirt too), and the life of a workshop street.
        var lb = new MeshBuilder();
        var lp = new Pal(lb);
        var lrng = new System.Random(9);
        Path(lb, lp, Line(new Vector2(-7f, 12.7f), new Vector2(-60f, 12.5f)), 3.0f, lrng);
        StreamAt(41.5f, out float n3x, out float n3w);
        float n3half = n3w * 0.5f + 1.8f; // the third street crosses the river on a stone bridge (BuildRiverside)
        Path(lb, lp, Line(new Vector2(7.6f, 24f), new Vector2(-62f, 24f)), 3.2f, lrng);
        Path(lb, lp, Smooth(new[] { new Vector2(8.3f, 12.7f), new Vector2(7.6f, 15f), new Vector2(7.6f, 24f) }, 0.6f), 1.8f, lrng); // riverside path
        Path(lb, lp, Line(new Vector2(24f, 41.5f), new Vector2(n3x + n3half - 0.2f, 41.5f)), 3.2f, lrng);
        Path(lb, lp, Line(new Vector2(n3x - n3half + 0.2f, 41.5f), new Vector2(-62f, 41.5f)), 3.2f, lrng);
        Path(lb, lp, Line(new Vector2(-22f, -12f), new Vector2(-22f, 44f)), 3.2f, lrng);
        Path(lb, lp, Line(new Vector2(-43.2f, -12f), new Vector2(-43.2f, 44f)), 3.2f, lrng);
        Path(lb, lp, Line(new Vector2(-43.2f, -12f), new Vector2(-22f, -12f)), 2.6f, lrng);
        foreach (var (a, c, w) in new[]
        {
            (new Vector2(-7f, 12.7f), new Vector2(-60f, 12.5f), 3.0f), (new Vector2(7.6f, 24f), new Vector2(-62f, 24f), 3.2f),
            (new Vector2(24f, 41.5f), new Vector2(n3x + n3half, 41.5f), 3.2f), (new Vector2(n3x - n3half, 41.5f), new Vector2(-62f, 41.5f), 3.2f), (new Vector2(-22f, -12f), new Vector2(-22f, 44f), 3.2f),
            (new Vector2(-43.2f, -12f), new Vector2(-43.2f, 44f), 3.2f), (new Vector2(7.6f, 15f), new Vector2(7.6f, 24f), 1.8f),
        })
            obstacles.Add(Obstacle.Segment(new Vector3(a.x, 0f, a.y), new Vector3(c.x, 0f, c.y), w * 0.5f));
        // Carts, barrels, crates, firewood and benches outside the workshops (never on the lane itself).
        Cart(new Put(lb, new Vector3(-12.6f, 0f, 10.6f), 80f), lp, true);
        Barrel(new Put(lb, new Vector3(-9.8f, 0f, 14.2f), 0f), lp);
        Barrel(new Put(lb, new Vector3(-10.4f, 0f, 14.3f), 40f), lp);
        Crate(new Put(lb, new Vector3(-16.8f, 0f, 14.3f), 10f), lp, 0.55f);
        Crate(new Put(lb, new Vector3(-17.4f, 0f, 14.2f), -12f), lp, 0.5f);
        Firewood(new Put(lb, new Vector3(-26.5f, 0f, 14.3f), 90f), lp, 3, 0.8f);
        Bench(new Put(lb, new Vector3(-30.5f, 0f, 14.2f), 180f), lp);
        Cart(new Put(lb, new Vector3(-20.1f, 0f, 30.5f), 0f), lp, false);
        Barrel(new Put(lb, new Vector3(-19.8f, 0f, 3.6f), 0f), lp);
        Crate(new Put(lb, new Vector3(-20.0f, 0f, -2.2f), 25f), lp, 0.6f);
        Crate(new Put(lb, new Vector3(-19.9f, 0f, -1.4f), -5f), lp, 0.45f);
        Barrel(new Put(lb, new Vector3(3.4f, 0f, 25.6f), 0f), lp);
        Firewood(new Put(lb, new Vector3(-2.5f, 0f, 25.6f), 90f), lp, 2, 0.8f);
        Cart(new Put(lb, new Vector3(16.6f, 0f, -24.5f), 350f), lp, true);
        Barrel(new Put(lb, new Vector3(16.9f, 0f, -29.5f), 0f), lp);
        Barrel(new Put(lb, new Vector3(16.7f, 0f, -30.2f), 20f), lp);
        foreach (var c in new[] { new Vector3(-12.6f, 0f, 10.6f), new Vector3(-20.1f, 0f, 30.5f), new Vector3(16.6f, 0f, -24.5f) })
            obstacles.Add(Obstacle.Circle(c, 1.8f));
        Place(root, "DistrictLanes", lb.Build("DistrictLanes"));
        return n;
    }

    // ---- the river through the city ------------------------------------------------------------------------------
    // (The river itself - water, banks, reeds, the lane bridges - is BuildStreamAndPaths; this is what the town built
    // along it: the watermill on the east bank, the tannery on the west, a stone bridge for the third street, a
    // riverside path, a jetty and a couple of boats.)

    private static void BuildRiverside(Transform city)
    {
        var root = new GameObject("Riverside").transform;
        root.SetParent(city, false);
        var b = new MeshBuilder();
        var p = new CityPal(b);
        var vp = new Pal(b);
        var rng = new System.Random(55);

        // ---- the watermill: an ordinary town building on the east bank, its wheel hung on the river-side wall ----
        const float wheelZ = 22.4f, wheelR = 2.2f;
        StreamAt(wheelZ, out float rx, out float rw);
        float bankEdge = rx + rw * 0.5f;             // where the water meets the east bank
        float wall = bankEdge + 0.9f;                // the mill's river-side (west) wall
        bool libraryMill = PlaceWatermill(root, bankEdge, wheelZ); // the sheet-03 watermill (VillageBuilder.Prefabs.cs)
        var mill = new TownSpec { W = 5.5f, D = 8f, floors = 2, stoneGround = true, timber = true, jetty = false, plaster = 0, roof = 3, roofType = 1, rise = 2.4f, chimney = true, shop = false, place = "workshop" };
        Vector3 millCentre = new Vector3(wall + mill.W * 0.5f, 0f, wheelZ - 0.4f);
        var mq = Quaternion.Euler(0f, 90f, 0f); // front (local +X) to the south, toward the lane and the smithy
        Vector3 hub = new Vector3(bankEdge - 0.1f, wheelR - 0.62f, wheelZ);
        if (!libraryMill)
        {
            TownHouse(new Put(b, millCentre, 90f), p, mill, rng);
            cityDoors.Add((millCentre + mq * new Vector3(mill.D * 0.5f + 0.5f, 0f, mill.doorZ), "workshop"));
            occluderBoxes.Add(new Bounds(millCentre + Vector3.up * mill.Height * 0.5f, new Vector3(mill.W, mill.Height, mill.D)));
            obstacles.Add(Obstacle.Rect(millCentre, 0f, mill.W * 0.5f + 0.4f, mill.D * 0.5f + 0.4f));
            // Machinery: the axle out through the wall, a timber frame carrying its outer bearing (standing in the water).
            b.Prism(new Vector3((hub.x + wall) * 0.5f, hub.y, wheelZ), 8, 0.2f, 0.2f, wall - hub.x + 0.6f, vp.woodDark, Quaternion.Euler(0f, 0f, 90f)); // axle
            foreach (float s in new[] { -1f, 1f })
            {
                b.Box(new Vector3(hub.x - 0.85f, (hub.y + 0.35f) * 0.5f - 0.2f, wheelZ + s * 0.55f), new Vector3(0.24f, hub.y + 0.75f, 0.24f), vp.woodDark);
                b.Box(new Vector3(wall + 0.05f, hub.y + 0.2f, wheelZ + s * 1.2f), new Vector3(0.2f, 0.9f, 0.2f), vp.woodDark);
            }
            b.Box(new Vector3(hub.x - 0.85f, hub.y + 0.25f, wheelZ), new Vector3(0.28f, 0.22f, 1.4f), vp.woodDark);
            // The loading yard at the front: sacks of flour, crates, a cart, a lantern by the door.
            Vector3 yard = millCentre + Vector3.back * (mill.D * 0.5f + 1.2f);
            Sack(new Put(b, yard + new Vector3(-1.9f, 0f, 0.1f), 10f), vp);
            Sack(new Put(b, yard + new Vector3(-1.4f, 0f, -0.2f), -20f), vp);
            Sack(new Put(b, yard + new Vector3(-1.7f, 0.42f, 0f), 40f), vp);
            Crate(new Put(b, yard + new Vector3(1.8f, 0f, 0.2f), 8f), vp, 0.55f);
            Crate(new Put(b, yard + new Vector3(2.4f, 0f, -0.1f), -12f), vp, 0.5f);
            Cart(new Put(b, millCentre + new Vector3(mill.W * 0.5f + 1.3f, 0f, -1.5f), 5f), vp, true);
            LanternPost(new Put(b, yard + new Vector3(0.9f, 0f, -0.6f), 0f), vp);
            millLantern = yard + new Vector3(1.3f, 1.72f, -0.6f);
        }

        // ---- the tannery on the west bank, downstream: vats by the water, a jetty, a boat ----
        const float tanZ = -44f;
        StreamAt(tanZ, out float tx, out float tw);
        float westEdge = tx - tw * 0.5f;
        if (PF("Pack_Warehouse") != null)
        {
            // the sheet-03 warehouse, front to the river, set back behind the vats
            PlaceBuilding("Pack_Warehouse", root, new Vector3(westEdge - 3.8f, 0f, tanZ), 0f, "workshop").go.name = "Tannery";
        }
        else
        {
            var tan = new TownSpec { W = 7f, D = 5f, floors = 2, stoneGround = false, timber = true, jetty = true, plaster = 1, roof = 0, roofType = 0, rise = 2.2f, chimney = true, place = "workshop" };
            Vector3 tanCentre = new Vector3(westEdge - 2.6f - tan.D * 0.5f, 0f, tanZ);
            TownHouse(new Put(b, tanCentre, 0f), p, tan, rng); // front (+X) to the river
            cityDoors.Add((tanCentre + new Vector3(tan.D * 0.5f + 0.5f, 0f, tan.doorZ), "workshop"));
            occluderBoxes.Add(new Bounds(tanCentre + Vector3.up * tan.Height * 0.5f, new Vector3(tan.D, tan.Height, tan.W)));
            obstacles.Add(Obstacle.Rect(tanCentre, 0f, tan.D * 0.5f + 0.4f, tan.W * 0.5f + 0.4f));
        }
        foreach (float dz in new[] { -2.2f, 0.4f })
        {
            var vat = new Vector3(westEdge - 1.3f, 0f, tanZ + dz);
            b.Prism(vat + Vector3.up * 0.35f, 10, 0.72f, 0.66f, 0.7f, vp.wood);
            b.Prism(vat + Vector3.up * 0.66f, 10, 0.6f, 0.6f, 0.04f, p.water);
        }
        for (int k = 0; k < 2; k++) // drying racks with hides
        {
            var at = new Vector3(westEdge - 1.4f, 0f, tanZ + 2.6f + k * 1.7f);
            foreach (float s in new[] { -0.7f, 0.7f }) b.Box(at + new Vector3(0f, 0.9f, s), new Vector3(0.1f, 1.8f, 0.1f), vp.woodDark);
            b.Box(at + new Vector3(0f, 1.78f, 0f), new Vector3(0.08f, 0.08f, 1.6f), vp.woodDark);
            b.Box(at + new Vector3(0f, 1.25f, 0f), new Vector3(0.04f, 1.0f, 1.2f), k == 0 ? vp.sack : vp.woodLight);
        }
        Jetty(b, vp, new Vector3(westEdge, 0f, tanZ + 6.2f), +1f);
        Boat(b, vp, new Vector3(westEdge + 1.6f, 0f, tanZ + 7.6f), 8f);
        // A second boat pulled up by a mooring post on the east bank below the square.
        StreamAt(-10f, out float bx2, out float bw2);
        Boat(b, vp, new Vector3(bx2 + bw2 * 0.5f - 0.8f, 0f, -10f), -12f);
        b.Box(new Vector3(bx2 + bw2 * 0.5f + 0.4f, 0.45f, -10.6f), new Vector3(0.18f, 0.9f, 0.18f), vp.woodDark);
        // Mooring posts here and there along the town stretch.
        foreach (float z in new[] { -22f, 5f, 33f })
        {
            StreamAt(z, out float mx, out float mw);
            b.Box(new Vector3(mx - mw * 0.5f - 0.3f, 0.4f, z), new Vector3(0.16f, 0.8f, 0.16f), vp.woodDark);
        }
        Place(root, "RiversideBuildings", b.Build("RiversideBuildings"));

        // The wheel, turning slowly with the current (its own object so it can spin; bottom paddles in the water).
        if (!libraryMill)
        {
            var wb = new MeshBuilder();
            int wood = wb.Paint(C(0.40f, 0.27f, 0.15f), 0.08f), dark = wb.Paint(C(0.28f, 0.18f, 0.10f), 0.08f);
            wb.Prism(Vector3.zero, 12, 0.32f, 0.32f, 1.2f, dark, Quaternion.Euler(0f, 0f, 90f));
            foreach (float s in new[] { -0.45f, 0.45f }) wb.Prism(new Vector3(s, 0f, 0f), 16, wheelR, wheelR, 0.12f, wood, Quaternion.Euler(0f, 0f, 90f));
            for (int k = 0; k < 12; k++)
            {
                var q = Quaternion.Euler(k * 30f, 0f, 0f);
                wb.Box(q * new Vector3(0f, wheelR * 0.5f, 0f), new Vector3(0.9f, wheelR, 0.1f), dark, q);
                wb.Box(q * new Vector3(0f, wheelR - 0.12f, 0f), new Vector3(1.05f, 0.1f, 0.55f), wood, q);
            }
            var wheel = Place(root, "MillWheel", wb.Build("MillWheel"));
            wheel.transform.position = hub;
            var spin = wheel.AddComponent<Spinner>();
            spin.axis = Vector3.right;
            spin.degreesPerSecond = -16f; // bottom paddles moving downstream (north)
        }

        // The third street's stone bridge.
        StreamAt(41.5f, out float n3x, out float n3w);
        float n3half = n3w * 0.5f + 1.8f;
        var sb = new MeshBuilder();
        var sp = new CityPal(sb);
        StoneBridge(sb, sp, new Vector3(n3x, 0f, 41.5f), n3half, 3.0f);
        bridges.Add((new Vector3(n3x, 0f, 41.5f), n3half, 1.5f, 0.15f, 0.45f));
        Place(root, "StoneBridge", sb.Build("StoneBridge"));

        // A riverside path on the west bank from the farm lane down to the tannery.
        var rb = new MeshBuilder();
        var rp = new Pal(rb);
        var path = Smooth(new[] { new Vector2(0f, -13.9f), new Vector2(-0.4f, -24f), new Vector2(1.2f, -33f), new Vector2(westEdge - 3.5f, tanZ + 5.5f), new Vector2(westEdge - 1.6f, tanZ + 4f) }, 0.6f);
        Path(rb, rp, path, 1.4f, new System.Random(8));
        Place(root, "RiversidePath", rb.Build("RiversidePath"));
    }

    private static Vector3? millLantern;

    // A plank jetty from the bank out over the water (along +X for side = 1).
    private static void Jetty(MeshBuilder b, Pal p, Vector3 bank, float side)
    {
        const float len = 3.2f, w = 1.4f;
        Vector3 mid = bank + new Vector3(side * (len * 0.5f - 0.6f), 0f, 0f);
        b.Box(mid + Vector3.up * 0.28f, new Vector3(len, 0.08f, w), p.woodLight);
        for (int k = 0; k < 3; k++)
            foreach (float s in new[] { -1f, 1f })
                b.Box(mid + new Vector3(side * (-len * 0.5f + 0.2f + k * (len - 0.4f) / 2f), 0.05f, s * (w * 0.5f - 0.1f)), new Vector3(0.14f, 0.6f, 0.14f), p.woodDark);
    }

    // A small rowing boat (bow along +Z before the yaw).
    private static void Boat(MeshBuilder b, Pal p, Vector3 at, float yaw)
    {
        var u = new Put(b, at, yaw);
        u.Frustum(new Vector3(0f, 0.16f, 0f), 0.75f, 2.6f, 1.1f, 3.0f, 0.32f, p.wood);
        u.Box(new Vector3(0f, 0.3f, 0f), new Vector3(0.9f, 0.05f, 2.5f), p.woodDark);
        u.Box(new Vector3(0f, 0.36f, 0.3f), new Vector3(1.0f, 0.05f, 0.25f), p.woodLight);
        u.Box(new Vector3(0.25f, 0.4f, -0.6f), new Vector3(0.06f, 0.05f, 1.6f), p.woodLight, Quaternion.Euler(0f, 12f, 0f)); // an oar
    }

    // A low stone bridge along X: an arched deck, parapets, and the arch showing dark underneath.
    private static void StoneBridge(MeshBuilder b, CityPal p, Vector3 c, float half, float width)
    {
        float Deck(float x) => 0.15f + 0.45f * (1f - (x / half) * (x / half));
        int n = 10;
        for (int i = 0; i < n; i++)
        {
            float x0 = -half + i * 2f * half / n, x1 = x0 + 2f * half / n, xm = (x0 + x1) * 0.5f;
            float ang = Mathf.Atan2(Deck(x1) - Deck(x0), x1 - x0) * Mathf.Rad2Deg;
            var rot = Quaternion.Euler(0f, 0f, ang);
            b.Box(c + new Vector3(xm, Deck(xm) - 0.12f, 0f), new Vector3(x1 - x0 + 0.05f, 0.24f, width), p.cobble, rot);
            b.Box(c + new Vector3(xm, Deck(xm) - 0.5f, 0f), new Vector3(x1 - x0 + 0.05f, 0.6f, width + 0.3f), p.stone, rot);
            foreach (float s in new[] { -1f, 1f })
                b.Box(c + new Vector3(xm, Deck(xm) + 0.3f, s * (width * 0.5f + 0.1f)), new Vector3(x1 - x0 + 0.05f, 0.6f, 0.3f), p.stoneLight, rot);
        }
        b.Box(c + new Vector3(0f, 0.08f, 0f), new Vector3(half * 1.1f, 0.3f, width + 0.2f), p.dark); // shadow under the arch
        foreach (float s in new[] { -1f, 1f })
            b.Box(c + new Vector3(s * (half + 0.3f), 0.25f, 0f), new Vector3(0.9f, 0.5f, width + 0.8f), p.stone);
    }

    // ---- the south-western edge of the city: farmsteads, a gate, fields and windmills ------------------------------

    private static readonly Vector3 SeatGround = new Vector3(0f, 0f, -3.5f);
    private const float FarmStart = 66f;          // fields begin this far from the seat
    private const float FarmYawMin = 192f, FarmYawMax = 262f; // the arc (seen from the seat) they fill

    private static float YawFromSeat(Vector3 p) => Mathf.Repeat(Mathf.Atan2(p.x - SeatGround.x, p.z - SeatGround.z) * Mathf.Rad2Deg, 360f);

    // The country rises gently away from the city in that arc: up to ~26 m by the ridge where the mills stand.
    private static float FarmHeight(float x, float z)
    {
        var p = new Vector3(x, 0f, z);
        float d = Vector3.Distance(p, SeatGround);
        float yaw = YawFromSeat(p);
        float inArc = Mathf.Min(Mathf.InverseLerp(FarmYawMin - 22f, FarmYawMin, yaw), Mathf.InverseLerp(FarmYawMax + 22f, FarmYawMax, yaw));
        inArc = Mathf.SmoothStep(0f, 1f, inArc);
        float rise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FarmStart - 4f, 190f, d)) * 26f;
        float roll = (Mathf.PerlinNoise(x * 0.018f + 3.1f, z * 0.018f + 7.7f) - 0.5f) * 7f * Mathf.InverseLerp(FarmStart, 120f, d);
        return Mathf.Max(0f, (rise + roll) * inArc);
    }

    private static void BuildFarmland(Transform city, Color groundColor)
    {
        var root = new GameObject("Farmland").transform;
        root.SetParent(city, false);

        // The ground: a flat-shaded height grid, laid out on a grid turned 22 degrees so the fields don't line up with
        // the streets. Grass is its own mesh named MeadowGround, so the seasons recolour it with the rest of the meadow.
        var grassB = new MeshBuilder();
        int grass = grassB.Paint(groundColor, 0.05f);
        var fieldB = new MeshBuilder();
        int wheat = fieldB.Paint(C(0.86f, 0.71f, 0.33f), 0.05f), wheatDark = fieldB.Paint(C(0.76f, 0.60f, 0.26f), 0.05f);
        int young = fieldB.Paint(C(0.47f, 0.60f, 0.26f), 0.05f), plough = fieldB.Paint(C(0.46f, 0.34f, 0.22f), 0.05f), stubble = fieldB.Paint(C(0.78f, 0.70f, 0.46f), 0.05f);
        var fp = new Pal(fieldB);

        const float cell = 6f;
        var rot = Quaternion.Euler(0f, 22f, 0f);
        Vector3 origin = new Vector3(-120f, 0f, -120f);
        const int N = 44; // 264 m square, centred on the arc
        Vector3 G(int i, int j)
        {
            var w = origin + rot * new Vector3((i - N / 2) * cell, 0f, (j - N / 2) * cell);
            w.y = FarmHeight(w.x, w.z) + 0.03f;
            return w;
        }
        // Fields: a handful of big blocks of cells (u0, v0, u1, v1, crop), with grass tracks between them.
        var fields = new[]
        {
            (22, 30, 28, 36, 0), (29, 30, 35, 35, 2), (15, 26, 21, 33, 0), (22, 22, 28, 29, 3),
            (29, 23, 36, 29, 0), (13, 18, 21, 25, 1), (22, 14, 29, 21, 0), (30, 15, 36, 22, 4), (9, 27, 14, 34, 2),
        };
        int CropAt(int i, int j)
        {
            foreach (var f in fields) if (i >= f.Item1 && i < f.Item3 && j >= f.Item2 && j < f.Item4) return f.Item5;
            return -1;
        }
        bool Usable(Vector3 w) => FarmHeight(w.x, w.z) > 0.6f && Vector3.Distance(w, SeatGround) > FarmStart + 6f;
        var raised = new HashSet<(int, int)>();
        for (int i = 0; i < N; i++)
            for (int j = 0; j < N; j++)
            {
                Vector3 a = G(i, j), bb = G(i, j + 1), c = G(i + 1, j + 1), d = G(i + 1, j);
                if (FarmHeight(a.x, a.z) <= 0f && FarmHeight(bb.x, bb.z) <= 0f && FarmHeight(c.x, c.z) <= 0f && FarmHeight(d.x, d.z) <= 0f) continue;
                int crop = CropAt(i, j);
                Vector3 mid = (a + c) * 0.5f;
                if (crop < 0 || !Usable(mid))
                {
                    grassB.Triangle(a, bb, c, grass); grassB.Triangle(a, c, d, grass);
                    continue;
                }
                // Wheat stands 0.7 m tall: its top is lifted, and its edge against a track gets a golden wall.
                bool tall = crop == 0 || crop == 2;
                Vector3 up = Vector3.up * (tall ? 0.7f : 0.05f);
                int paint = crop == 0 ? (j % 3 == 0 ? wheatDark : wheat) : crop == 1 ? young : crop == 2 ? wheat : crop == 3 ? plough : stubble;
                fieldB.Triangle(a + up, bb + up, c + up, paint); fieldB.Triangle(a + up, c + up, d + up, paint);
                if (tall) raised.Add((i, j));
            }
        // Golden walls where standing wheat meets a track or another crop (what makes it read as a crop at a distance).
        foreach (var (i, j) in raised)
            foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                if (raised.Contains((i + di, j + dj))) continue;
                Vector3 e0, e1;
                if (di == 1) { e0 = G(i + 1, j); e1 = G(i + 1, j + 1); }
                else if (di == -1) { e0 = G(i, j + 1); e1 = G(i, j); }
                else if (dj == 1) { e0 = G(i + 1, j + 1); e1 = G(i, j + 1); }
                else { e0 = G(i, j); e1 = G(i + 1, j); }
                Vector3 t0 = e0 + Vector3.up * 0.7f, t1 = e1 + Vector3.up * 0.7f;
                fieldB.Triangle(e0, t0, t1, wheatDark); fieldB.Triangle(e0, t1, e1, wheatDark);
            }
        // Stooks and bales in the stubble field, a hay cart on the track.
        var srng = new System.Random(31);
        for (int k = 0; k < 14; k++)
        {
            int i = 30 + srng.Next(6), j = 15 + srng.Next(7);
            Vector3 w = (G(i, j) + G(i + 1, j + 1)) * 0.5f;
            if (!Usable(w)) continue;
            HayBale(new Put(fieldB, w, (float)srng.NextDouble() * 180f), fp);
        }
        var ground = Place(root, "FarmlandGround", grassB.Build("FarmlandGround"));
        ground.name = "MeadowGround"; // so MeadowLife's seasons recolour it with the rest of the meadow
        Place(root, "Fields", fieldB.Build("Fields"));

        // The road out through the gate, along the tracks to the mills, following the ground.
        var rb = new MeshBuilder();
        var rp = new Pal(rb);
        var road = new List<Vector3>();
        foreach (var pt in new[] { new Vector2(-46f, -46f), new Vector2(-62f, -64f), new Vector2(-80f, -86f), new Vector2(-96f, -110f), new Vector2(-104f, -136f) })
            road.Add(new Vector3(pt.x, 0f, pt.y));
        for (int k = 0; k < road.Count - 1; k++)
        {
            Vector3 a = road[k], c = road[k + 1];
            int steps = Mathf.CeilToInt(Vector3.Distance(a, c) / 4f);
            for (int s = 0; s < steps; s++)
            {
                Vector3 p0 = Vector3.Lerp(a, c, s / (float)steps), p1 = Vector3.Lerp(a, c, (s + 1f) / steps);
                p0.y = FarmHeight(p0.x, p0.z) + 0.06f; p1.y = FarmHeight(p1.x, p1.z) + 0.06f;
                rb.Box((p0 + p1) * 0.5f, new Vector3(3f, 0.12f, Vector3.Distance(p0, p1) + 0.4f), rp.dirt, Quaternion.LookRotation(p1 - p0));
            }
        }
        {
            Vector3 cartAt = new Vector3(-86f, 0f, -96f); cartAt.y = FarmHeight(cartAt.x, cartAt.z);
            Cart(new Put(rb, cartAt, 215f), rp, true);
        }
        Place(root, "FarmRoad", rb.Build("FarmRoad"));

        // Windmills on the rise, sails square to the city so they read broadside, turning slowly.
        int m = 0;
        foreach (var (yaw, dist) in new[] { (206f, 150f), (224f, 185f), (240f, 128f) }) // clear of the smith's house in the house view
        {
            float a = yaw * Mathf.Deg2Rad;
            Vector3 at = SeatGround + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * dist;
            at.y = FarmHeight(at.x, at.z) - 0.3f;
            Windmill(root, at, m++);
        }

        // A few hedgerow trees along the tracks, reusing the scene's own trees (named Tree_* so the seasons tint them).
        var sources = new List<GameObject>();
        for (int k = 0; k < 7; k++) { var src = GameObject.Find("Tree" + k); if (src != null && src.GetComponent<MeshFilter>() != null) sources.Add(src); }
        if (sources.Count > 0)
        {
            var trng = new System.Random(71);
            int placed = 0;
            foreach (var (i, j) in new[] { (21, 29), (21, 31), (21, 35), (28, 26), (28, 22), (14, 25), (22, 21), (29, 20), (35, 22), (12, 30), (21, 17), (36, 31) })
            {
                Vector3 w = G(i, j);
                if (!Usable(w)) continue;
                var src = sources[placed % sources.Count];
                var go = new GameObject("Tree_Farm" + placed);
                go.transform.SetParent(root, false);
                go.transform.SetPositionAndRotation(w - Vector3.up * 0.1f, Quaternion.Euler(0f, (float)trng.NextDouble() * 360f, 0f));
                go.transform.localScale = Vector3.one * (1.2f + (float)trng.NextDouble() * 0.5f);
                go.AddComponent<MeshFilter>().sharedMesh = src.GetComponent<MeshFilter>().sharedMesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = src.GetComponent<MeshRenderer>().sharedMaterials;
                placed++;
            }
        }
    }

    // A tower mill: whitewashed stone tower, a wooden cap, four lattice sails on their own spinning object.
    private static void Windmill(Transform root, Vector3 at, int index)
    {
        var b = new MeshBuilder();
        int wash = b.Paint(C(0.88f, 0.85f, 0.78f), 0.06f), stone = b.Paint(C(0.52f, 0.50f, 0.47f), 0.08f);
        int cap = b.Paint(C(0.36f, 0.24f, 0.15f), 0.08f), door = b.Paint(C(0.36f, 0.23f, 0.13f), 0.08f), dark = b.Paint(C(0.10f, 0.09f, 0.08f), 0f);
        const float H = 10f;
        b.Prism(Vector3.up * 0.4f, 8, 3.6f, 3.6f, 0.8f, stone);
        b.Prism(Vector3.up * (H * 0.5f + 0.4f), 8, 3.2f, 2.3f, H, wash);
        b.Prism(Vector3.up * (H + 0.55f), 8, 2.5f, 2.5f, 0.3f, cap);
        b.Frustum(Vector3.up * (H + 2.0f), 5.0f, 5.4f, 0.3f, 3.6f, 2.8f, cap); // a boat-shaped cap
        b.Box(new Vector3(3.15f, 1.2f, 0f), new Vector3(0.2f, 2.0f, 1.0f), door);
        foreach (float y in new[] { 4.5f, 7.5f }) b.Box(new Vector3(2.85f - (y - 4f) * 0.08f, y, 0f), new Vector3(0.1f, 0.8f, 0.55f), dark);
        b.Box(new Vector3(0f, H + 1.6f, 3.0f), new Vector3(0.5f, 0.5f, 1.2f), cap); // the sail shaft's housing (facing +Z)
        var go = Place(root, "Windmill" + index, b.Build("Windmill"));
        // Face the sails (local +Z) toward the seat.
        Vector3 toSeat = SeatGround - at; toSeat.y = 0f;
        go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(toSeat));

        var sb = new MeshBuilder();
        int spar = sb.Paint(C(0.40f, 0.28f, 0.16f), 0.08f), cloth = sb.Paint(C(0.90f, 0.86f, 0.76f), 0.05f);
        sb.Prism(Vector3.zero, 8, 0.35f, 0.35f, 0.6f, spar, Quaternion.Euler(90f, 0f, 0f));
        for (int k = 0; k < 4; k++)
        {
            var q = Quaternion.Euler(0f, 0f, k * 90f + 12f);
            sb.Box(q * new Vector3(0f, 3.8f, 0.1f), new Vector3(0.22f, 7.6f, 0.18f), spar, q);
            sb.Box(q * new Vector3(0.75f, 4.4f, 0.14f), new Vector3(1.3f, 5.6f, 0.05f), cloth, q);
            for (int r = 0; r < 5; r++) sb.Box(q * new Vector3(0.75f, 2.0f + r * 1.2f, 0.18f), new Vector3(1.45f, 0.07f, 0.05f), spar, q);
        }
        var sails = Place(go.transform, "Sails" + index, sb.Build("Sails"));
        sails.transform.localPosition = new Vector3(0f, H + 1.6f, 3.7f);
        sails.transform.localRotation = Quaternion.identity;
        var spin = sails.AddComponent<Spinner>();
        spin.axis = Vector3.forward;
        spin.degreesPerSecond = 9f + index * 1.5f;
    }

    // The city's edge on the road out: a few farmsteads with their yards, and a gatehouse with short lengths of wall.
    private static void BuildCityEdge(Transform city)
    {
        var root = new GameObject("CityEdge").transform;
        root.SetParent(city, false);
        var b = new MeshBuilder();
        var p = new CityPal(b);
        var vp = new Pal(b);
        // Gatehouse across the road at (-46, -46), its wall running a little way each side (square to the road).
        Vector3 gate = new Vector3(-46f, 0f, -46f);
        Vector3 along = new Vector3(1f, 0f, -1f).normalized; // the wall's direction
        foreach (float s in new[] { -1f, 1f })
        {
            RoundTower(b, p.stone, p.roof[1], gate + along * (3.6f * s), 2.4f, 9f, 3.6f);
            WallRun(b, p, gate + along * (5.6f * s), gate + along * (16f * s), 6f, 2f);
            RoundTower(b, p.stone, p.roof[1], gate + along * (16.6f * s), 2f, 7.5f, 3f);
        }
        b.Box(gate + Vector3.up * 6.8f, new Vector3(5.2f, 2.6f, 5.2f), p.stone, Quaternion.LookRotation(along));
        occluderBoxes.Add(new Bounds(gate + Vector3.up * 5f, new Vector3(7f, 10f, 7f)));
        cityDoors.Add((gate + new Vector3(-3f, 0f, -3f), "gate"));
        Place(root, "SouthWestGate", b.Build("SouthWestGate"));

        // Farmsteads between the town and the gate: small, low, with fences, hay and a cart.
        var houses = new[]
        {
            new HouseSpec { name = "Farmhouse1", prefab = "Cottage_04_Woodshed", pos = new Vector3(-24f, 0f, -26f), yaw = 45f, D = 3.8f, W = 5f, H = 2.4f, rise = 1.7f, plaster = new Color(0.87f, 0.82f, 0.70f), roof = new Color(0.64f, 0.50f, 0.28f), doorZ = 0.8f, windows = new[] { -1.2f }, chimneyZ = -1.6f, leanTo = true },
            new HouseSpec { name = "Farmhouse2", prefab = "Cottage_07_Sunflower", pos = new Vector3(-34f, 0f, -16f), yaw = 20f, D = 3.6f, W = 4.4f, H = 2.3f, rise = 1.6f, plaster = new Color(0.82f, 0.77f, 0.66f), roof = new Color(0.44f, 0.30f, 0.20f), doorZ = -0.6f, windows = new[] { 1.0f }, chimneyZ = 1.3f, flowerBox = true },
            new HouseSpec { name = "Farmhouse3", prefab = "Pack_Cottage", pos = new Vector3(-16f, 0f, -38f), yaw = 70f, D = 3.6f, W = 4.6f, H = 2.3f, rise = 1.6f, plaster = new Color(0.85f, 0.80f, 0.72f), roof = new Color(0.62f, 0.30f, 0.20f), doorZ = 0.7f, windows = new[] { -1.1f }, chimneyZ = -1.5f },
        };
        foreach (var s in houses)
        {
            if (PF(s.prefab) != null)
            {
                var q = Quaternion.Euler(0f, s.yaw, 0f);
                PlaceBuilding(s.prefab, root, s.pos + q * new Vector3(s.D * 0.5f, 0f, 0f), s.yaw, "home").go.name = s.name;
                continue;
            }
            var go = Place(root, s.name, House(s));
            go.transform.SetPositionAndRotation(s.pos, Quaternion.Euler(0f, s.yaw, 0f));
            obstacles.Add(Obstacle.Rect(s.pos, s.yaw, s.D * 0.5f + 1f, s.W * 0.5f + 1.8f));
            AddOccluder(go);
            cityDoors.Add((s.pos + Quaternion.Euler(0f, s.yaw, 0f) * new Vector3(s.D * 0.5f + 0.5f, 0f, s.doorZ), "home"));
        }
        var barn = Place(root, "FarmBarn", Barn());
        barn.transform.SetPositionAndRotation(new Vector3(-36f, 0f, -32f), Quaternion.Euler(0f, 40f, 0f));
        obstacles.Add(Obstacle.Rect(new Vector3(-36f, 0f, -32f), 40f, 3.4f, 4.3f));
        AddOccluder(barn);

        var yb = new MeshBuilder();
        var yp = new Pal(yb);
        FenceRun(yb, yp, new Vector3(-28f, 0f, -30f), new Vector3(-20f, 0f, -38f), false);
        FenceRun(yb, yp, new Vector3(-40f, 0f, -24f), new Vector3(-30f, 0f, -22f), false);
        FenceRun(yb, yp, new Vector3(-12f, 0f, -44f), new Vector3(-24f, 0f, -44f), false);
        HayBale(new Put(yb, new Vector3(-31f, 0f, -27f), 30f), yp);
        HayBale(new Put(yb, new Vector3(-32f, 0f, -26.2f), 10f), yp);
        HayPile(new Put(yb, new Vector3(-39f, 0f, -36.5f), 0f), yp);
        Cart(new Put(yb, new Vector3(-27f, 0f, -21.5f), 130f), yp, true);
        Garden(yb, yp, new Vector3(-20f, 0f, -31f));
        Place(root, "FarmYards", yb.Build("FarmYards"));
        obstacles.Add(Obstacle.Circle(new Vector3(-20f, 0f, -31f), 3f));

        // The track from the farm lane out to the gate.
        var tb = new MeshBuilder();
        var tp = new Pal(tb);
        Path(tb, tp, Smooth(new[] { new Vector2(-6f, -14f), new Vector2(-14f, -18f), new Vector2(-24f, -30f), new Vector2(-36f, -40f), new Vector2(-46f, -46f) }, 0.8f), 2.6f, new System.Random(3));
        obstacles.Add(Obstacle.Segment(new Vector3(-6f, 0f, -14f), new Vector3(-46f, 0f, -46f), 1.6f));
        Place(root, "GateTrack", tb.Build("GateTrack"));
    }
}
