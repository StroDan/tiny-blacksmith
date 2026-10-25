using UnityEngine;

// Things for the crowd (CityLife) to do besides walking between doors: benches to sit on, riverbanks to watch the water
// from, bridge rails to lean on, the watermill's yard and wheel, guard posts at the gates. Each is a Spot on the street
// graph whose rotation is the way someone there faces; a few props go with them (benches round the fountain and by the
// riverside path). Part of the same re-runnable build as the rest of the city (VillageBuilder.City.cs calls both).
public static partial class VillageBuilder
{
    // Props that go with the new spots. Called from BuildCity before the street graph is laid out, so the benches built
    // here get their own spots too (every Bench() call is recorded in benchSeats).
    private static void BuildLifeProps(Transform city)
    {
        var b = new MeshBuilder();
        var p = new Pal(b);
        // Round the fountain, on the diagonals - clear of the four ways into it and of the stalls.
        Vector3 fountain = new Vector3(PlazaCx, 0f, -3.5f);
        foreach (var (dx, dz) in new[] { (1f, 1f), (1f, -1f), (-1f, 1f), (-1f, -1f) })
        {
            Vector3 at = fountain + new Vector3(dx, 0f, dz) * 3.9f;
            Vector3 toFountain = fountain - at;
            Bench(new Put(b, at, Mathf.Atan2(toFountain.x, toFountain.z) * Mathf.Rad2Deg), p);
            obstacles.Add(Obstacle.Circle(at, 0.9f));
        }
        // On the west bank's riverside path, looking out over the water (seen behind the storage rack).
        Bench(new Put(b, new Vector3(2.2f, 0f, -21.5f), 90f), p);
        obstacles.Add(Obstacle.Circle(new Vector3(2.2f, 0f, -21.5f), 0.9f));
        // Outside the inn: a bench under its window and a couple of barrels by the door (the square's evening crowd).
        Bench(new Put(b, new Vector3(19.55f, 0f, 1.6f), 90f), p);
        Barrel(new Put(b, new Vector3(19.7f, 0f, 5.05f), 0f), p);
        Barrel(new Put(b, new Vector3(19.75f, 0f, 5.65f), 25f), p);
        obstacles.Add(Obstacle.Circle(new Vector3(19.55f, 0f, 1.6f), 0.9f));
        obstacles.Add(Obstacle.Circle(new Vector3(19.7f, 0f, 5.35f), 0.8f));
        Place(city, "LifeProps", b.Build("LifeProps"));
    }

    // The spots themselves (called at the end of BuildCityNav, before the nodes are lifted onto the bridge decks).
    private static void AddLifeSpots(NavBuilder nb)
    {
        int benches = 0, others = 0;
        // Benches: a spot just in front of the seat on the side the street is, facing away from the bench.
        foreach (var (pos, yaw) in benchSeats)
        {
            Vector3 z = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            var near = nb.Nearest(pos, 9f);
            if (near == null) continue;
            Vector3 front = Vector3.Distance(pos + z, near.Position) <= Vector3.Distance(pos - z, near.Position) ? z : -z;
            Vector3 at = pos + front * 0.6f;
            if (CrossesRiver(at, near.Position)) continue;
            Spot(nb, at, "bench", front, near);
            benches++;
        }

        // Riverbanks: people stop to watch the water (both banks, near the smithy and down by the tannery).
        foreach (var (z, east) in new[] { (-16f, true), (7.5f, true), (19f, false), (-30f, false) })
        {
            StreamAt(z, out float x, out float w);
            float edge = east ? x + w * 0.5f + 0.65f : x - w * 0.5f - 0.65f;
            Vector3 at = new Vector3(edge, 0f, z);
            var near = NearestDry(nb, at, 12f);
            if (near == null) continue;
            Spot(nb, at, "river", east ? Vector3.left : Vector3.right, near);
            others++;
        }

        // Bridges: one person leaning on the rail of each, looking along the river (never in the middle of the deck,
        // where customers cross).
        foreach (var br in bridges)
        {
            float side = br.centre.z > 30f ? -1f : br.centre.z > 0f ? 1f : -1f;
            Vector3 at = br.centre + new Vector3(0.5f, 0f, side * (br.halfWidth - 0.3f));
            var near = nb.Nearest(br.centre + Vector3.right * 1.6f, 4f);
            if (near == null) continue;
            Spot(nb, at, "bridge", new Vector3(0f, 0f, side), near);
            others++;
        }

        // The watermill: by the sacks in its yard, and on the bank watching the wheel turn.
        const float wheelZ = 22.4f;
        StreamAt(wheelZ, out float rx, out float rw);
        float bankEdge = rx + rw * 0.5f, wall = bankEdge + 0.9f;
        Vector3 millCentre = new Vector3(wall + 5.5f * 0.5f, 0f, wheelZ - 0.4f);
        Vector3 yard = millCentre + Vector3.back * (8f * 0.5f + 1.2f);
        foreach (var (at, face) in new[] { (yard + new Vector3(-0.7f, 0f, -0.8f), new Vector3(-1f, 0f, 0.6f)), (yard + new Vector3(1.4f, 0f, -0.9f), new Vector3(0.6f, 0f, 1f)),
                                          (new Vector3(bankEdge + 0.5f, 0f, wheelZ - 4.8f), new Vector3(-0.2f, 0f, 1f)) })
        {
            var near = NearestDry(nb, at, 12f);
            if (near == null) continue;
            Spot(nb, at, "mill", face, near);
            others++;
        }

        // Guard posts either side of the two gates' roads, facing out of town.
        Vector3 roadIn = new Vector3(-36f, 0f, -40f), gate = new Vector3(-46f, 0f, -46f);
        Vector3 outward = (gate - roadIn).normalized, across = new Vector3(outward.z, 0f, -outward.x);
        foreach (float s in new[] { -1f, 1f })
        {
            var near = nb.Nearest(gate, 3f);
            if (near != null) { Spot(nb, gate + across * s * 2.4f - outward * 0.8f, "gate", outward, near); others++; }
        }
        foreach (float s in new[] { -1f, 1f })
        {
            var near = nb.Nearest(new Vector3(WallX + 3f, 0f, -3.5f), 3f);
            if (near != null) { Spot(nb, new Vector3(WallX + 3.5f, 0f, -3.5f + s * 3f), "gate", Vector3.right, near); others++; }
        }
        Debug.Log($"[City] life spots: {benches} benches, {others} riverbank/bridge/mill/gate spots");
    }

    private static CityNavNode Spot(NavBuilder nb, Vector3 at, string place, Vector3 facing, CityNavNode linkTo)
    {
        var spot = nb.Node(new Vector2(at.x, at.z), CityNodeKind.Spot, place, 0f);
        facing.y = 0f;
        if (facing.sqrMagnitude > 0.001f) spot.transform.rotation = Quaternion.LookRotation(facing.normalized);
        NavBuilder.Link(spot, linkTo);
        return spot;
    }

    // The nearest street node that can be reached from `at` without wading through the river.
    private static CityNavNode NearestDry(NavBuilder nb, Vector3 at, float maxDist)
    {
        CityNavNode best = null;
        float bd = maxDist;
        foreach (var n in nb.streets)
        {
            float d = Vector2.Distance(new Vector2(at.x, at.z), new Vector2(n.Position.x, n.Position.z));
            if (d < bd && !CrossesRiver(at, n.Position)) { bd = d; best = n; }
        }
        return best;
    }
}
