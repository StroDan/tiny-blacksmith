using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Environment pass for the Main Room (menu: Tiny Blacksmith > Build Village Environment).
//  1. Groups the workshop under "BlacksmithWorkshop" (ForgeArea / StorageArea / PersonalTableArea) and moves
//     the forge cluster and the storage rack closer to the fixed seat; enlarges the journal table.
//     Applied only once - if BlacksmithWorkshop already exists the groups are left exactly where they are,
//     so hand-tuning them in the Editor afterwards is safe.
//  2. Builds the village around it (houses, a stream with two bridges, paths, props, bushes, flowers) under
//     "Village", plus the workshop's own clutter under ForgeArea/StorageArea. All of these are deleted and
//     rebuilt on every run - nothing else in the scene is touched, except existing trees that stand where a
//     house/path/stream now goes, which are nudged out of the way.
// World frame (seat at (0, 1.7, -3.5)): +Z = forge (left), +X = table + village street (centre),
// -Z = storage (right), -X = the smith's house (behind). Nothing here has a collider, so none of it can
// block the mouse raycasts the forging stations rely on.
public static partial class VillageBuilder
{
    private const string MeshDir = "Assets/Models/Generated/Village";
    private static readonly Vector3 ForgeAreaOffset = new Vector3(0f, 0f, -1.1f);  // toward the seat
    private static readonly Vector3 StorageAreaOffset = new Vector3(0f, 0f, 0.7f); // toward the seat
    private static readonly Vector3 TablePosition = new Vector3(1.8f, 0f, -3.5f);
    private static readonly Vector3 TableScale = new Vector3(1.28f, 1.08f, 1.28f);   // +28% top, a touch taller
    private static readonly Vector3 JournalScale = new Vector3(0.9f, 0.95f, 0.9f);   // net ~+15%: bigger, not huge

    private static Shader lit;
    private static readonly List<Obstacle> obstacles = new List<Obstacle>();

    [MenuItem("Tiny Blacksmith/Build Village Environment")]
    public static void BuildInOpenScene()
    {
        Build();
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    }

    // Headless: Unity.exe -batchmode -projectPath <p> -executeMethod VillageBuilder.BuildHeadless -quit
    public static void BuildHeadless()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Prototype1.unity");
        Build();
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
    }

    private static void Build()
    {
        lit = Shader.Find("Universal Render Pipeline/Lit");
        if (!AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.CreateFolder("Assets/Models/Generated", "Village");
        obstacles.Clear();
        benchSeats.Clear();
        chimneyTops.Clear();
        pendingProps.Clear();
        smithHouse = null;
        EnvironmentLibrary.ClearCache();
        useLibrary = EnvironmentLibrary.Prefab("Town_01_RedPorch") != null; // the Blender-built library (VillageBuilder.Prefabs.cs)
        if (!useLibrary) Debug.LogWarning("[Village] environment library not built - using the procedural placeholders (Tiny Blacksmith > Environment > Build Asset Library)");

        var areas = GroupWorkshop();
        if (useLibrary)
        {
            UpgradePlayerHouse();
            UpgradeSceneTrees();
        }

        var old = GameObject.Find("Village");
        if (old != null) Object.DestroyImmediate(old);
        var village = new GameObject("Village").transform;

        BuildWorkshopClutter(areas.forge, areas.storage, areas.table);
        BuildStreamAndPaths(village);
        BuildHouses(village);
        BuildProps(village);
        BuildCity(village); // the city beyond the square, the castle, and the street graph for the crowd (VillageBuilder.City.cs)
        RelocateConflictingTrees();
        AddVillageTrees(village);
        BuildPlants(village);
        if (useLibrary) DressWithLibrary(village); // sheet 05/06 nature and street clutter (VillageBuilder.Prefabs.cs)
        Debug.Log("[Village] built.");
    }

    // ---- 1. workshop -----------------------------------------------------------------------------

    private struct Areas { public Transform forge, storage, table; }

    private static Areas GroupWorkshop()
    {
        var root = GameObject.Find("BlacksmithWorkshop");
        if (root != null)
            return new Areas
            {
                forge = root.transform.Find("ForgeArea"),
                storage = root.transform.Find("StorageArea"),
                table = root.transform.Find("PersonalTableArea"),
            };

        root = new GameObject("BlacksmithWorkshop");
        Transform Area(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(root.transform, false);
            return t;
        }
        var a = new Areas { forge = Area("ForgeArea"), storage = Area("StorageArea"), table = Area("PersonalTableArea") };

        // The forge cluster moves as one group; each station keeps its hand-tuned spot/rotation inside it.
        foreach (var c in new Component[]
                 {
                     Object.FindAnyObjectByType<ForgeStation>(), Object.FindAnyObjectByType<AnvilStation>(),
                     Object.FindAnyObjectByType<QuenchBarrel>(), Object.FindAnyObjectByType<Grindstone>(),
                     Object.FindAnyObjectByType<WorkbenchStation>(),
                 })
        {
            if (c != null) c.transform.SetParent(a.forge, true);
            else Debug.LogWarning("[Village] a forge station is missing from the scene");
        }
        a.forge.position = ForgeAreaOffset;
        // The workpiece rests on the anvil between stages (the game re-places it every stage anyway).
        var workpiece = GameObject.Find("Workpiece");
        if (workpiece != null) workpiece.transform.position += ForgeAreaOffset;

        var rack = Object.FindAnyObjectByType<StorageDisplay>();
        if (rack != null) rack.transform.SetParent(a.storage, true);
        a.storage.position = StorageAreaOffset;

        var table = GameObject.Find("JournalTable");
        if (table != null)
        {
            table.transform.SetParent(a.table, true);
            table.transform.position = TablePosition;
            // X and Z scaled equally, so the journal's small yaw can't shear; its local position stays at
            // JournalTableTop, which the parent scale carries to the new top height - it can't float or sink.
            table.transform.localScale = TableScale;
            var journal = table.transform.Find("Journal");
            if (journal != null) journal.localScale = JournalScale;
        }
        return a;
    }

    // Forge-yard and storage-nook clutter. Built in each area's local frame so it moves with the area.
    private static void BuildWorkshopClutter(Transform forgeArea, Transform storageArea, Transform tableArea)
    {
        foreach (var area in new[] { forgeArea, storageArea })
        {
            var oldProps = area.Find(area == forgeArea ? "ForgeYardProps" : "StorageProps");
            if (oldProps != null) Object.DestroyImmediate(oldProps.gameObject);
        }

        // Seat, in ForgeArea's frame (the frame the stations were originally laid out in).
        Vector3 seat = forgeArea.InverseTransformPoint(new Vector3(0f, 0f, -3.5f));
        var b = new MeshBuilder();
        var p = new Pal(b);
        ToolRack(new Put(b, new Vector3(-2.75f, 0f, 2.4f), Facing(new Vector3(-2.75f, 0f, 2.4f), seat)), p);
        Firewood(new Put(b, new Vector3(-3.05f, 0f, 3.7f), 90f), p, 4, 1.0f);
        CoalHeap(new Put(b, new Vector3(-1.45f, 0f, 3.75f), 0f), p);
        Crate(new Put(b, new Vector3(3.1f, 0f, 3.3f), 8f), p, 0.6f);
        Crate(new Put(b, new Vector3(3.78f, 0f, 3.15f), -12f), p, 0.55f);
        Crate(new Put(b, new Vector3(3.35f, 0.6f, 3.25f), 25f), p, 0.5f);
        IronBars(new Put(b, new Vector3(3.35f, 1.1f, 3.25f), 25f), p);
        Sack(new Put(b, new Vector3(2.35f, 0f, 3.75f), 0f), p);
        Sack(new Put(b, new Vector3(2.55f, 0f, 4.2f), 40f), p);
        Bucket(new Put(b, new Vector3(-2.25f, 0f, 1.5f), 0f), p);
        FenceRun(b, p, new Vector3(-3.6f, 0f, 5.35f), new Vector3(-0.6f, 0f, 5.35f), false);
        FenceRun(b, p, new Vector3(0.6f, 0f, 5.35f), new Vector3(3.9f, 0f, 5.35f), false);
        Place(forgeArea, "ForgeYardProps", b.Build("ForgeYardProps"));

        b = new MeshBuilder();
        p = new Pal(b);
        Crate(new Put(b, new Vector3(2.45f, 0f, -7.55f), 10f), p, 0.62f);
        Crate(new Put(b, new Vector3(2.5f, 0.62f, -7.55f), -6f), p, 0.5f);
        Barrel(new Put(b, new Vector3(2.75f, 0f, -8.05f), 0f), p);
        Crate(new Put(b, new Vector3(-2.55f, 0f, -7.55f), -14f), p, 0.58f);
        Barrel(new Put(b, new Vector3(-2.7f, 0f, -8.35f), 0f), p);
        IronBars(new Put(b, new Vector3(-2.55f, 0.58f, -7.55f), -14f), p);
        // A lantern hung from the rack's post, so the rack, the order book and the delivery crate can be seen after dark
        // (its light is one of the LanternLights markers - see BuildProps).
        Vector3 lamp = StorageLanternLocal;
        b.Box(lamp + new Vector3(0f, 0.2f, -0.29f), new Vector3(0.05f, 0.05f, 0.62f), p.woodDark);
        b.Box(lamp + new Vector3(0f, 0.13f, 0f), new Vector3(0.015f, 0.12f, 0.015f), p.iron);
        b.Box(lamp, new Vector3(0.15f, 0.2f, 0.15f), p.iron);
        b.Box(lamp, new Vector3(0.17f, 0.13f, 0.1f), p.flame);
        b.Frustum(lamp + Vector3.up * 0.13f, 0.19f, 0.19f, 0.04f, 0.04f, 0.07f, p.iron);
        Place(storageArea, "StorageProps", b.Build("StorageProps"));

        // A small hammer and a rolled drawing on the (now bigger) personal table, in the table's own frame.
        var table = tableArea != null ? tableArea.Find("JournalTable") : null;
        if (table != null)
        {
            var oldTools = table.Find("TableTools");
            if (oldTools != null) Object.DestroyImmediate(oldTools.gameObject);
            b = new MeshBuilder();
            p = new Pal(b);
            float T = LowPoly.JournalTableTop;
            var h = new Put(b, new Vector3(0.23f, T, 0.2f), 15f);
            h.Prism(new Vector3(0f, 0.02f, 0f), 6, 0.016f, 0.016f, 0.26f, p.wood, AlongZ);
            h.Box(new Vector3(0f, 0.035f, 0.13f), new Vector3(0.1f, 0.05f, 0.05f), p.iron);
            var s = new Put(b, new Vector3(-0.27f, T, 0.42f), -20f);
            s.Prism(new Vector3(0f, 0.03f, 0f), 8, 0.03f, 0.03f, 0.28f, p.paper, AlongX);
            s.Prism(new Vector3(0f, 0.03f, 0f), 8, 0.032f, 0.032f, 0.02f, p.clothRed, AlongX);
            Place(table, "TableTools", b.Build("TableTools"));
        }

        // Keep the village (and relocated trees) off the workshop itself.
        obstacles.Add(Obstacle.Circle(new Vector3(0.5f, 0f, -2.5f), 7.5f, true));
    }

    // ---- 2. stream, bridges, paths ---------------------------------------------------------------

    // The river: in from the far north, a gentle bend through the city between the workshop district and the old
    // town (past the smithy's table, under the lane bridges), and away south with long lazy bends until it's lost in
    // the haze - its ends are ~600 m out, far beyond anything the seat can see (the ground runs on under it: see
    // BuildCountryside). The customer lane (z = -3.5, where customers walk in to x = 2.6) crosses it on the main bridge.
    private static readonly Vector2[] StreamPoints =
    {
        new Vector2(40f, -600f), new Vector2(52f, -420f), new Vector2(46f, -300f), new Vector2(36f, -240f), new Vector2(24f, -196f),
        // (south of the square the course leans ~1 m west: the river widens there and the road runs along its east bank)
        new Vector2(28f, -150f), new Vector2(19f, -112f), new Vector2(14.5f, -84f), new Vector2(12.2f, -66f), new Vector2(11.2f, -54f),
        new Vector2(11.0f, -42f), new Vector2(11.4f, -30f), new Vector2(11.8f, -22f), new Vector2(12.4f, -14f), new Vector2(11.7f, -8f),
        new Vector2(11.2f, -3.5f), new Vector2(11.4f, 1f), new Vector2(12.0f, 7f), new Vector2(11.8f, 14f), new Vector2(12.9f, 22f),
        new Vector2(12.9f, 30f), new Vector2(13.2f, 44f), new Vector2(14.6f, 56f), new Vector2(17.5f, 70f), new Vector2(21f, 92f),
        new Vector2(19f, 120f), new Vector2(27f, 160f), new Vector2(24f, 205f), new Vector2(33f, 250f), new Vector2(38f, 300f),
        new Vector2(45f, 420f), new Vector2(40f, 600f),
    };
    private const float RiverDetail = 140f; // banks, reeds, rocks and tree clearance only this far out (nobody sees further)
    // The old village road along the east bank, from the south road to the footbridge corner (from there the city's
    // street at x = 24 carries on north - the east bank upstream is the watermill's).
    private static readonly Vector2[] RoadPoints =
    {
        new Vector2(16.8f, -27f), new Vector2(17.0f, -18f), new Vector2(16.7f, -11f), new Vector2(16f, 6f), new Vector2(16.3f, 12.5f),
    };
    // Wooden and stone bridges across the river (all run along X): the crowd walks over their decks, not through them.
    private static readonly List<(Vector3 centre, float half, float halfWidth, float lift, float arch)> bridges = new List<(Vector3, float, float, float, float)>();
    public static float DeckHeightAt(Vector3 p)
    {
        foreach (var br in bridges)
        {
            float dx = p.x - br.centre.x;
            if (Mathf.Abs(dx) <= br.half && Mathf.Abs(p.z - br.centre.z) <= br.halfWidth + 0.3f)
                return br.lift + br.arch * (1f - (dx / br.half) * (dx / br.half));
        }
        return 0f;
    }
    private const float LaneZ = -3.5f;
    private const float NorthLaneZ = 12.5f;
    private static List<Vector3> stream;
    private static List<float> streamWidth;

    // ~5 m of water past the smithy (the lanes and bridges there are sized to it), opening out to a proper town river
    // (~8.5 m between its quays) once it's past the square either way, and wider still out in the country - so it still
    // reads as a river, not a thread, where perspective squeezes it.
    private static float StreamWidthAt(int i)
    {
        float z = Mathf.Abs(stream[i].z);
        float town = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(16f, 40f, z)) * 3.2f;
        // past the old town either way it opens out again: a broad river between the north quarter's rows and past the tannery
        float wide = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(45f, 75f, z)) * 3f;
        float country = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(90f, 220f, z)) * 3.5f;
        return 5.2f + 0.6f * Mathf.Sin(i * 0.035f) + town + wide + country;
    }

    // The stream's centre x and width at a given z (the stream only ever runs northward).
    private static void StreamAt(float z, out float x, out float w)
    {
        for (int i = 0; i < stream.Count - 1; i++)
            if (z >= stream[i].z && z <= stream[i + 1].z)
            {
                float t = Mathf.InverseLerp(stream[i].z, stream[i + 1].z, z);
                x = Mathf.Lerp(stream[i].x, stream[i + 1].x, t);
                w = Mathf.Lerp(streamWidth[i], streamWidth[i + 1], t);
                return;
            }
        x = stream[0].x; w = streamWidth[0];
    }

    private static void BuildStreamAndPaths(Transform village)
    {
        bridges.Clear();
        stream = Smooth(StreamPoints, 1.0f);
        streamWidth = new List<float>();
        for (int i = 0; i < stream.Count; i++) streamWidth.Add(StreamWidthAt(i));

        var b = new MeshBuilder();
        var p = new Pal(b);
        var rng = new System.Random(11);
        // Muddy banks, then the water, then a darker deep channel down the middle - each a touch higher so
        // they layer instead of fighting, all well under the ankle height nobody here ever walks at.
        Ribbon(b, stream, i => streamWidth[i] + 1.1f, 0.022f, 0.03f, p.mud);
        Ribbon(b, stream, i => streamWidth[i], 0.034f, 0.03f, p.water);
        Ribbon(b, stream, i => streamWidth[i] * 0.42f, 0.036f, 0.03f, p.waterDeep, 0.15f, 0f);
        // Flow: pale streaks along the current (the menu art's light ripples), thicker in the middle of the channel, so the
        // water reads as moving and as a surface rather than a flat blue strip.
        int ripple = b.Paint(C(0.52f, 0.76f, 0.92f), 0.85f), rippleDim = b.Paint(C(0.42f, 0.67f, 0.87f), 0.85f);
        var frng = new System.Random(23);
        for (int i = 0; i < stream.Count - 1; i++)
        {
            if (Mathf.Abs(stream[i].z) > RiverDetail + 60f) continue;
            Vector3 dir = (stream[i + 1] - stream[i]).normalized, n = new Vector3(dir.z, 0f, -dir.x);
            int count = frng.NextDouble() < 0.5 + streamWidth[i] * 0.04f ? 1 : 0;
            for (int k = 0; k < count; k++)
            {
                float across = (float)(frng.NextDouble() * 2.0 - 1.0);
                across *= Mathf.Abs(across) * 0.4f + 0.05f;
                float len = 0.8f + (float)frng.NextDouble() * 2.2f;
                var at = Vector3.Lerp(stream[i], stream[i + 1], (float)frng.NextDouble()) + n * across * streamWidth[i];
                b.Box(at + Vector3.up * 0.043f, new Vector3(0.06f + (float)frng.NextDouble() * 0.06f, 0.004f, len), frng.NextDouble() < 0.6 ? ripple : rippleDim, Quaternion.LookRotation(dir));
            }
        }
        for (int i = 0; i < stream.Count - 1; i++)
        {
            if (Mathf.Abs(stream[i].z) > RiverDetail) continue;
            Vector3 dir = (stream[i + 1] - stream[i]).normalized, n = new Vector3(dir.z, 0f, -dir.x);
            float w = streamWidth[i];
            if (rng.NextDouble() < 0.55)
            {
                float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                float r = 0.1f + (float)rng.NextDouble() * 0.22f;
                b.Ball(stream[i] + n * side * (w * 0.5f + 0.1f + (float)rng.NextDouble() * 0.35f) + Vector3.up * r * 0.2f, r,
                    rng.NextDouble() < 0.5 ? p.rock : p.rockDark, 0.55f);
            }
            if (i % 5 == 2 && !QuayHere(stream[i].z, (i / 5) % 2 != 0)) // reeds on the natural banks only, not against a quay wall
            {
                float side = (i / 5) % 2 == 0 ? -1f : 1f;
                Reeds(b, p, stream[i] + n * side * (w * 0.5f - 0.05f), rng);
                if (i % 15 == 7) b.Ball(stream[i] - n * side * (w * 0.5f + 1.1f + (float)rng.NextDouble()) + Vector3.up * 0.25f, 0.5f + (float)rng.NextDouble() * 0.3f, p.leaf, 0.7f); // a bush up the bank
            }
            if (i % 17 == 9) b.Ball(stream[i] + n * (float)(rng.NextDouble() - 0.5) * w * 0.5f, 0.16f, p.rock, 0.5f); // a stone mid-stream
        }
        for (int i = 0; i < stream.Count - 1; i++)
            if (Mathf.Abs(stream[i].z) < RiverDetail + 20f)
            {
                var o = Obstacle.Segment(stream[i], stream[i + 1], streamWidth[i] * 0.5f + 1.3f);
                o.isRiver = true;
                obstacles.Add(o);
            }
        Place(village, "Stream", b.Build("Stream"));

        // Bridges where the customer lane and the northern lane cross.
        StreamAt(LaneZ, out float bx, out float bw);
        float bridgeLen = bw + 1.9f;
        var bridge = new MeshBuilder();
        Bridge(new Put(bridge, new Vector3(bx, 0f, LaneZ), 0f), new Pal(bridge), bridgeLen, 1.9f);
        bridges.Add((new Vector3(bx, 0f, LaneZ), bridgeLen * 0.5f, 0.95f, 0.12f, 0.2f));
        Place(village, "Bridge", bridge.Build("Bridge"));
        StreamAt(NorthLaneZ, out float nx, out float nw);
        float footLen = nw + 1.6f;
        var foot = new MeshBuilder();
        Bridge(new Put(foot, new Vector3(nx, 0f, NorthLaneZ), 0f), new Pal(foot), footLen, 1.7f);
        bridges.Add((new Vector3(nx, 0f, NorthLaneZ), footLen * 0.5f, 0.85f, 0.12f, 0.2f));
        Place(village, "Footbridge", foot.Build("Footbridge"));

        // Paths: all packed dirt, one height, so overlaps at junctions just merge.
        b = new MeshBuilder();
        p = new Pal(b);
        rng = new System.Random(5);
        var road = Smooth(RoadPoints, 0.7f);
        Path(b, p, road, 2.4f, rng);
        Ribbon(b, road, i => 0.22f, 0.021f, 0.02f, p.dirtDark, 0.1f, 0.5f);  // wheel ruts
        Ribbon(b, road, i => 0.22f, 0.021f, 0.02f, p.dirtDark, 0.1f, -0.5f);
        Path(b, p, Line(new Vector2(3.0f, LaneZ), new Vector2(bx - bridgeLen * 0.5f + 0.15f, LaneZ)), 1.5f, rng);
        Path(b, p, Line(new Vector2(bx + bridgeLen * 0.5f - 0.15f, LaneZ), new Vector2(16f, LaneZ)), 1.6f, rng);
        // Village square where the lane meets the street.
        b.Prism(new Vector3(17.7f, 0.008f, -3.6f), 14, 3.4f, 3.4f, 0.02f, p.dirt);
        obstacles.Add(Obstacle.Circle(new Vector3(17.7f, 0f, -3.6f), 4.0f));
        // North lane: off the customer lane past the forge yard, west to the northern houses, east over the
        // footbridge to the street.
        Path(b, p, Smooth(new[] { new Vector2(6.3f, -2.9f), new Vector2(6.1f, 3.5f), new Vector2(5.0f, 9.0f), new Vector2(2.5f, NorthLaneZ), new Vector2(-7.0f, NorthLaneZ + 0.3f) }, 0.6f), 1.2f, rng);
        Path(b, p, Line(new Vector2(2.5f, NorthLaneZ), new Vector2(nx - footLen * 0.5f + 0.15f, NorthLaneZ)), 1.2f, rng);
        Path(b, p, Line(new Vector2(nx + footLen * 0.5f - 0.15f, NorthLaneZ), new Vector2(16.4f, NorthLaneZ)), 1.2f, rng);
        // South lane to the farm (barn + the house behind the storage side).
        Path(b, p, Smooth(new[] { new Vector2(6.5f, -4.1f), new Vector2(6.3f, -9f), new Vector2(5.6f, -13.8f), new Vector2(5.5f, -16.6f) }, 0.6f), 1.3f, rng);
        Path(b, p, Smooth(new[] { new Vector2(5.6f, -13.8f), new Vector2(0f, -13.9f), new Vector2(-6.0f, -14.0f), new Vector2(-6.2f, -15.0f) }, 0.6f), 1.1f, rng);
        // West lane from the smith's door to the neighbour, and stepping stones from the seat to the door.
        Path(b, p, Smooth(smithHouse != null
            ? new[] { new Vector2(-5.6f, -1.6f), new Vector2(-5.6f, 2.2f), new Vector2(-6.0f, 6.3f), new Vector2(-9.5f, 6.6f), new Vector2(-13.4f, 5.8f) } // round the forge lean-to
            : new[] { new Vector2(-5.9f, -2.1f), new Vector2(-6.3f, 1.6f), new Vector2(-8.2f, 4.6f), new Vector2(-13.4f, 5.8f) }, 0.6f), 1.0f, rng);
        for (int i = 0; i < 8; i++)
        {
            float t = i / 7f;
            var sp = Vector3.Lerp(new Vector3(-1.9f, 0f, -3.15f), new Vector3(-5.85f, 0f, -2.75f), t) + new Vector3(0f, 0f, (float)(rng.NextDouble() - 0.5) * 0.25f);
            b.Prism(sp + Vector3.up * 0.015f, 7, 0.24f + (float)rng.NextDouble() * 0.06f, 0.22f, 0.05f, i % 2 == 0 ? p.stone : p.stoneDark, Quaternion.Euler(0f, (float)rng.NextDouble() * 60f, 0f));
        }
        Place(village, "Paths", b.Build("VillagePaths"));
    }

    // ---- 3. houses --------------------------------------------------------------------------------

    private class HouseSpec
    {
        public string name;
        public Vector3 pos;
        public float yaw;                 // 180 = front door faces -X (toward the workshop), 90 = faces -Z, 270 = +Z, 0 = +X
        public float D = 3.6f, W = 4.6f;  // depth (local X, front at +D/2) and width (local Z)
        public float H = 2.4f;            // ground-storey wall top
        public float upper;               // > 0: a jettied upper storey this tall
        public float rise = 1.6f;         // roof height above the wall top
        public Color plaster = new Color(0.86f, 0.79f, 0.64f);
        public Color roof = new Color(0.55f, 0.36f, 0.20f);
        public Color shutter = new Color(0.26f, 0.38f, 0.40f);
        public float doorZ;
        public float[] windows = new float[0];
        public float chimneyZ = float.NaN;
        public bool leanTo, innSign, flowerBox;
        public string prefab;             // the library model that stands here (when the library is built)
        public Vector3? door;             // its door, once placed
    }

    private static void BuildHouses(Transform village)
    {
        var list = new List<HouseSpec>
        {
            // The street, across the stream, framing the centre view. The avenue into the city (and the castle on its
            // hill) opens straight ahead between the inn on the corner and the narrow house across from it.
            new HouseSpec { name = "House_Inn", prefab = "Town_03_Thatch", pos = new Vector3(22.3f, 0f, 3.6f), yaw = 180f, D = 4.4f, W = 6.2f, H = 2.5f, upper = 1.9f, rise = 1.8f,
                plaster = new Color(0.90f, 0.82f, 0.66f), roof = new Color(0.56f, 0.26f, 0.18f), doorZ = 0f, windows = new[] { -1.9f, 1.9f }, chimneyZ = 2.1f, innSign = true, flowerBox = true },
            new HouseSpec { name = "House_Corner", prefab = "Cottage_06_Laundry", pos = new Vector3(22.0f, 0f, -8.9f), yaw = 270f, D = 4.6f, W = 3.2f, H = 2.4f, upper = 1.8f, rise = 1.4f,
                plaster = new Color(0.78f, 0.72f, 0.62f), roof = new Color(0.40f, 0.38f, 0.40f), shutter = new Color(0.45f, 0.30f, 0.20f), doorZ = 0.4f, windows = new[] { -0.9f }, chimneyZ = -0.9f },
            new HouseSpec { name = "House_StreetSouth", prefab = "Cottage_08_BluePorch", pos = new Vector3(21.7f, 0f, -13.4f), yaw = 180f, D = 3.6f, W = 4.4f, H = 2.3f, rise = 1.5f,
                plaster = new Color(0.82f, 0.77f, 0.66f), roof = new Color(0.64f, 0.50f, 0.28f), shutter = new Color(0.45f, 0.30f, 0.20f), doorZ = 0.9f, windows = new[] { -1.0f }, chimneyZ = -1.3f, leanTo = true },
            new HouseSpec { name = "House_StreetNorth", prefab = "Town_01_RedPorch", pos = new Vector3(21.9f, 0f, 10.6f), yaw = 180f, D = 3.8f, W = 5.0f, H = 2.4f, rise = 1.7f,
                plaster = new Color(0.85f, 0.80f, 0.72f), roof = new Color(0.36f, 0.40f, 0.46f), doorZ = -0.9f, windows = new[] { 0.9f }, chimneyZ = 1.5f, flowerBox = true },
            // Behind the forge (seen past the yard fence).
            new HouseSpec { name = "House_NorthWest", prefab = "Cottage_03_Vines", pos = new Vector3(-4.8f, 0f, 15.6f), yaw = 90f, D = 3.6f, W = 5.0f, H = 2.3f, upper = 1.7f, rise = 1.6f,
                plaster = new Color(0.88f, 0.84f, 0.74f), roof = new Color(0.50f, 0.30f, 0.18f), doorZ = 0.8f, windows = new[] { -1.2f }, chimneyZ = -1.6f },
            new HouseSpec { name = "House_NorthEast", prefab = "Cottage_02_Thatch", pos = new Vector3(4.6f, 0f, 18.1f), yaw = 90f, D = 3.4f, W = 4.2f, H = 2.2f, rise = 1.5f,
                plaster = new Color(0.80f, 0.75f, 0.63f), roof = new Color(0.62f, 0.49f, 0.27f), shutter = new Color(0.35f, 0.42f, 0.26f), doorZ = -0.6f, windows = new[] { 0.8f }, leanTo = true, flowerBox = true },
            // Behind the storage side, by the farm.
            new HouseSpec { name = "House_South", prefab = "Cottage_05_Veranda", pos = new Vector3(-5.6f, 0f, -17.3f), yaw = 270f, D = 3.6f, W = 4.6f, H = 2.3f, rise = 1.6f,
                plaster = new Color(0.84f, 0.78f, 0.66f), roof = new Color(0.45f, 0.28f, 0.20f), doorZ = -0.6f, windows = new[] { 1.1f }, chimneyZ = 1.4f, flowerBox = true },
            // The smith's neighbour, off to the side of their own house.
            new HouseSpec { name = "House_West", prefab = "Cottage_01_BlueSlate", pos = new Vector3(-15.6f, 0f, 6.3f), yaw = 0f, D = 3.4f, W = 4.4f, H = 2.2f, rise = 1.5f,
                plaster = new Color(0.86f, 0.80f, 0.68f), roof = new Color(0.63f, 0.49f, 0.27f), shutter = new Color(0.45f, 0.30f, 0.20f), doorZ = -0.5f, windows = new[] { 1.0f }, chimneyZ = -1.3f },
        };
        var root = new GameObject("Houses").transform;
        root.SetParent(village, false);
        villageHouses.Clear();
        foreach (var s in list)
        {
            if (PF(s.prefab) != null)
            {
                // the library house keeps the old house's front line and faces the same way
                var q = Quaternion.Euler(0f, s.yaw, 0f);
                var placed = PlaceBuilding(s.prefab, root, s.pos + q * new Vector3(s.D * 0.5f, 0f, 0f), s.yaw, null);
                placed.go.name = s.name;
                s.door = placed.door;
                villageHouses.Add((s, placed.go));
                continue;
            }
            var go = Place(root, s.name, House(s));
            go.transform.SetPositionAndRotation(s.pos, Quaternion.Euler(0f, s.yaw, 0f));
            obstacles.Add(Obstacle.Rect(s.pos, s.yaw, s.D * 0.5f + (s.leanTo ? 0.6f : 0.4f) + 0.6f, s.W * 0.5f + (s.leanTo ? 1.6f : 0.5f) + 0.6f));
            villageHouses.Add((s, go));
        }

        var barn = Place(root, "Barn", Barn());
        var barnPos = new Vector3(5.5f, 0f, -19.3f);
        barn.transform.SetPositionAndRotation(barnPos, Quaternion.Euler(0f, 270f, 0f));
        obstacles.Add(Obstacle.Rect(barnPos, 270f, 2.3f + 1.0f, 3.25f + 1.0f));
    }

    // ---- 4. props ---------------------------------------------------------------------------------

    private static void BuildProps(Transform village)
    {
        var root = new GameObject("Props").transform;
        root.SetParent(village, false);

        // The smith's sign by the lane, and lanterns along it.
        var b = new MeshBuilder();
        var p = new Pal(b);
        BlacksmithSign(new Put(b, new Vector3(5.0f, 0f, -1.75f), 0f), p);
        LanternPost(new Put(b, new Vector3(5.2f, 0f, -4.75f), 0f), p);
        LanternPost(new Put(b, new Vector3(7.5f, 0f, -2.1f), 0f), p);
        LanternPost(new Put(b, new Vector3(14.6f, 0f, -5.4f), 0f), p);
        LanternPost(new Put(b, new Vector3(14.6f, 0f, -1.6f), 0f), p);
        Bench(new Put(b, new Vector3(6.4f, 0f, -5.0f), 0f), p);
        Place(root, "LaneProps", b.Build("LaneProps"));

        // Where real lights go at night (DayCycle turns them on after dusk): each lane lantern's flame, the
        // square's lanterns, and the lantern by the smith's own door (LowPoly.HouseStructure, front + 0.2, 1.78).
        var lights = new GameObject("LanternLights").transform;
        lights.SetParent(village, false);
        Vector3[] flames =
        {
            new Vector3(5.2f, 0f, -4.75f), new Vector3(7.5f, 0f, -2.1f), new Vector3(14.6f, 0f, -5.4f), new Vector3(14.6f, 0f, -1.6f),
        };
        foreach (var f in flames)
            new GameObject("LanternLight").transform.SetParent(lights, false);
        for (int i = 0; i < flames.Length; i++) lights.GetChild(i).position = LanternFlame(flames[i], 0f);
        if (smithHouse != null)
        {
            // the blacksmith house's own lanterns: both beside the door, the corner one under the sign, the forge lean-to
            foreach (var l in Info("BlacksmithHouse").lanterns)
            {
                var m = new GameObject("LanternLight").transform;
                m.SetParent(lights, false);
                m.position = smithHouse.TransformPoint(l);
            }
        }
        else
        {
            var door = new GameObject("LanternLight").transform;
            door.SetParent(lights, false);
            door.position = Prototype1SceneBuilder.HousePosition + new Vector3(LowPoly.HouseDepth * 0.5f + 0.35f, 1.72f, 0.05f);
        }
        var storageArea = GameObject.Find("StorageArea");
        if (storageArea != null)
        {
            var lamp = new GameObject("LanternLight").transform;
            lamp.SetParent(lights, false);
            lamp.position = storageArea.transform.TransformPoint(StorageLanternLocal);
        }
        obstacles.Add(Obstacle.Circle(new Vector3(5.0f, 0f, -1.75f), 0.8f));

        // Village square: well, market stall, barrels, crates, a cart, a bench by the inn.
        b = new MeshBuilder();
        p = new Pal(b);
        Well(new Put(b, new Vector3(18.7f, 0f, 0.5f), 20f), p);
        MarketStall(new Put(b, new Vector3(18.9f, 0f, -8.6f), 180f), p);
        Barrel(new Put(b, new Vector3(19.6f, 0f, -6.75f), 0f), p);
        Barrel(new Put(b, new Vector3(19.35f, 0f, -7.35f), 30f), p);
        Crate(new Put(b, new Vector3(17.9f, 0f, -10.3f), 12f), p, 0.55f);
        Crate(new Put(b, new Vector3(18.4f, 0f, -10.6f), -8f), p, 0.5f);
        Cart(new Put(b, new Vector3(14.1f, 0f, -9.4f), 200f), p, true);
        Bench(new Put(b, new Vector3(19.5f, 0f, -5.3f), 90f), p);
        Place(root, "SquareProps", b.Build("SquareProps"));
        obstacles.Add(Obstacle.Circle(new Vector3(18.9f, 0f, -8.6f), 1.8f));
        obstacles.Add(Obstacle.Circle(new Vector3(14.1f, 0f, -9.4f), 1.6f));

        // Farm: fenced yard in front of the barn, hay, a hay cart.
        b = new MeshBuilder();
        p = new Pal(b);
        FenceRun(b, p, new Vector3(0.8f, 0f, -14.6f), new Vector3(4.8f, 0f, -14.6f), false);
        FenceRun(b, p, new Vector3(6.4f, 0f, -14.6f), new Vector3(8.8f, 0f, -14.6f), false);
        FenceRun(b, p, new Vector3(0.8f, 0f, -14.6f), new Vector3(0.8f, 0f, -20.6f), false);
        FenceRun(b, p, new Vector3(8.8f, 0f, -14.6f), new Vector3(8.8f, 0f, -20.6f), false);
        HayBale(new Put(b, new Vector3(2.4f, 0f, -15.7f), 5f), p);
        HayBale(new Put(b, new Vector3(3.5f, 0f, -15.6f), -8f), p);
        HayBale(new Put(b, new Vector3(2.95f, 0.55f, -15.65f), 3f), p);
        HayPile(new Put(b, new Vector3(8.3f, 0f, -15.7f), 0f), p);
        Cart(new Put(b, new Vector3(8.0f, 0f, -12.4f), 110f), p, false);
        Place(root, "FarmProps", b.Build("FarmProps"));
        obstacles.Add(Obstacle.Rect(new Vector3(5.2f, 0f, -17.6f), 0f, 5.2f, 3.6f));
        obstacles.Add(Obstacle.Circle(new Vector3(8.0f, 0f, -12.4f), 1.6f));

        // The smith's vegetable garden beside the house, and firewood by the neighbour's door.
        b = new MeshBuilder();
        p = new Pal(b);
        var garden = smithHouse != null ? new Vector3(-9.0f, 0f, -7.6f) : new Vector3(-8.8f, 0f, 1.9f); // the forge lean-to stands where it was
        Garden(b, p, garden);
        Firewood(new Put(b, new Vector3(-13.4f, 0f, 3.2f), 0f), p, 3, 0.8f);
        Place(root, "HomeProps", b.Build("HomeProps"));
        obstacles.Add(Obstacle.Rect(garden, 0f, 2.1f, 1.8f));

        // Northern houses: a woodpile, a bench.
        b = new MeshBuilder();
        p = new Pal(b);
        Firewood(new Put(b, new Vector3(-1.9f, 0f, 15.2f), 90f), p, 3, 0.8f);
        Bench(new Put(b, new Vector3(-6.5f, 0f, 13.2f), 0f), p);
        Barrel(new Put(b, new Vector3(2.4f, 0f, 16.5f), 0f), p);
        FenceRun(b, p, new Vector3(-8.0f, 0f, 11.6f), new Vector3(-8.0f, 0f, 17.8f), false);
        Place(root, "NorthProps", b.Build("NorthProps"));
    }

    // ---- 5. vegetation ----------------------------------------------------------------------------

    // Existing trees (hand-placed copies included) that now stand in a house, the stream or a path are
    // pushed just clear of it - nothing is deleted.
    private static void RelocateConflictingTrees()
    {
        int moved = 0, hidden = 0;
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (!root.name.StartsWith("Tree") || root.name.StartsWith("Tree_") || root.GetComponent<MeshFilter>() == null) continue;
            Vector3 pos = root.transform.position, start = pos;
            for (int iter = 0; iter < 60; iter++)
            {
                float worst = 0f; Vector3 push = Vector3.zero;
                foreach (var o in obstacles)
                {
                    float depth = o.Penetration(pos, 0.9f, out Vector3 dir);
                    if (depth > worst) { worst = depth; push = dir; }
                }
                if (worst <= 0f) break;
                pos += push * (worst + 0.15f);
            }
            // Wedged between two obstacles (e.g. the strip between the stream and the street): take the
            // nearest free spot around where it started instead, so re-runs find it clear and leave it be.
            if (Blocked(pos, 0.9f))
            {
                pos = start;
                for (float r = 0.5f; r < 14f && Blocked(pos, 0.9f); r += 0.5f)
                    for (int k = 0; k < 24; k++)
                    {
                        var candidate = start + Quaternion.Euler(0f, k * 15f, 0f) * Vector3.forward * r;
                        if (!Blocked(candidate, 0.9f)) { pos = candidate; break; }
                    }
            }
            if (Blocked(pos, 0.9f))
            {
                // Deep inside the city with no free ground anywhere near: hidden (not deleted - re-activate it by hand
                // in the Editor any time), rather than left standing in the middle of a building or a street.
                if (root.activeSelf) hidden++;
                root.SetActive(false);
                continue;
            }
            if ((pos - start).sqrMagnitude > 0.0001f)
            {
                root.transform.position = new Vector3(pos.x, start.y, pos.z);
                moved++;
            }
        }
        Debug.Log($"[Village] nudged {moved} existing trees out of houses/paths/stream, hid {hidden} with nowhere to go");
    }

    private static bool Blocked(Vector3 p, float margin, bool ignoreWorkshop = false)
    {
        foreach (var o in obstacles) if (!(ignoreWorkshop && o.isWorkshop) && o.Penetration(p, margin, out _) > 0f) return true;
        return false;
    }

    // A few more trees around the houses, reusing the scene's own tree meshes/materials (named Tree_* so
    // MeadowLife's seasons recolour them like every other tree).
    private static void AddVillageTrees(Transform village)
    {
        var sources = new List<GameObject>();
        for (int i = 0; i < 7; i++)
        {
            var src = GameObject.Find("Tree" + i);
            if (src != null && src.GetComponent<MeshFilter>() != null) sources.Add(src);
        }
        if (sources.Count == 0) return;
        var root = new GameObject("VillageTrees").transform;
        root.SetParent(village, false);
        Vector3[] spots =
        {
            new Vector3(25.2f, 0f, -7.4f), new Vector3(25.6f, 0f, 1.0f), new Vector3(24.9f, 0f, 9.4f), new Vector3(24.6f, 0f, -14.8f),
            new Vector3(-8.9f, 0f, 18.2f), new Vector3(0.3f, 0f, 20.2f), new Vector3(8.6f, 0f, 21.2f),
            new Vector3(-10.2f, 0f, -18.8f), new Vector3(1.0f, 0f, -22.6f), new Vector3(-19.0f, 0f, 3.2f),
            new Vector3(-12.6f, 0f, -0.8f), new Vector3(-5.2f, 0f, 6.2f), new Vector3(9.4f, 0f, -9.0f),
        };
        var rng = new System.Random(21);
        int placed = 0;
        if (useLibrary)
        {
            // sheet-05 trees; a spot a bigger library house now covers slides to the nearest free ground within 4 m
            foreach (var spot in spots)
            {
                Vector3 at = spot;
                for (float r = 0.5f; r <= 4f && Blocked(at, 0.9f); r += 0.5f)
                    for (int k = 0; k < 16; k++)
                    {
                        var c = spot + Quaternion.Euler(0f, k * 22.5f, 0f) * Vector3.forward * r;
                        if (!Blocked(c, 0.9f)) { at = c; break; }
                    }
                if (Blocked(at, 0.9f)) continue;
                var tree = Spawn(TreePrefabs[placed % TreePrefabs.Length], root, at, (float)rng.NextDouble() * 360f,
                                 Vector3.one * (0.8f + (float)rng.NextDouble() * 0.3f));
                tree.name = "Tree_Village" + placed;
                obstacles.Add(Obstacle.Circle(at, 1.2f));
                placed++;
            }
            Debug.Log($"[Village] added {placed} trees");
            return;
        }
        foreach (var spot in spots)
        {
            bool blocked = false;
            foreach (var o in obstacles) if (o.Penetration(spot, 0.9f, out _) > 0f) { blocked = true; break; }
            if (blocked) continue;
            var src = sources[placed % sources.Count];
            var go = new GameObject("Tree_Village" + placed);
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(spot, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
            go.transform.localScale = Vector3.one * (0.95f + (float)rng.NextDouble() * 0.35f);
            go.AddComponent<MeshFilter>().sharedMesh = src.GetComponent<MeshFilter>().sharedMesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = src.GetComponent<MeshRenderer>().sharedMaterials;
            placed++;
        }
        Debug.Log($"[Village] added {placed} trees");
    }

    // Bushes + grass tufts in one mesh whose paints are exactly (bark, leaf, leaf) and whose name starts
    // with "Tree", so MeadowLife's seasonal tint treats them like the trees. Flowers are a separate mesh.
    private static void BuildPlants(Transform village)
    {
        var b = new MeshBuilder();
        int stem = b.Paint(new Color(0.34f, 0.22f, 0.11f), 0.1f);
        int leafA = b.Paint(new Color(0.22f, 0.42f, 0.17f), 0.15f);
        int leafB = b.Paint(new Color(0.28f, 0.50f, 0.20f), 0.15f);
        var fb = new MeshBuilder();
        int fStem = fb.Paint(new Color(0.24f, 0.44f, 0.18f), 0.1f);
        int[] petals =
        {
            fb.Paint(new Color(0.88f, 0.30f, 0.30f), 0.2f), fb.Paint(new Color(0.96f, 0.82f, 0.30f), 0.2f),
            fb.Paint(new Color(0.76f, 0.56f, 0.88f), 0.2f), fb.Paint(new Color(0.95f, 0.95f, 0.92f), 0.2f),
        };
        var rng = new System.Random(8);

        Vector3[] bushes =
        {
            // workshop yard edges (outside the stations' views) and the sign
            new Vector3(-4.2f, 0f, 4.6f), new Vector3(4.6f, 0f, 4.3f), new Vector3(3.9f, 0f, -1.7f), new Vector3(5.8f, 0f, -1.3f),
            new Vector3(-3.2f, 0f, -8.9f), new Vector3(3.6f, 0f, -9.2f),
            // lane and bridge ends
            new Vector3(9.3f, 0f, -5.1f), new Vector3(14.2f, 0f, -1.4f), new Vector3(13.9f, 0f, -5.6f), new Vector3(7.8f, 0f, -5.5f),
            // houses
            new Vector3(19.7f, 0f, -0.6f), new Vector3(19.8f, 0f, 7.6f), new Vector3(19.6f, 0f, -13.4f), new Vector3(24.8f, 0f, -3.0f),
            new Vector3(-7.4f, 0f, 13.5f), new Vector3(-2.3f, 0f, 13.6f), new Vector3(6.9f, 0f, 16.4f), new Vector3(2.3f, 0f, 14.4f),
            new Vector3(-8.2f, 0f, -15.0f), new Vector3(-3.0f, 0f, -15.1f), new Vector3(-13.6f, 0f, 8.9f), new Vector3(-13.5f, 0f, 4.0f),
            new Vector3(-6.2f, 0f, -6.6f), new Vector3(-10.9f, 0f, -0.2f), new Vector3(0.3f, 0f, -14.9f), new Vector3(10.3f, 0f, -13.9f),
            new Vector3(-9.6f, 0f, 11.4f), new Vector3(6.5f, 0f, 11.2f),
        };
        string[] bushKinds = { "Tree_Bush_Green", "Tree_Bush_Flowering", "Tree_Bush_Berry", "Tree_Bush_Green", "Tree_Bush_Yellow" };
        int bushIndex = 0;
        foreach (var at in bushes)
        {
            float s = 0.75f + (float)rng.NextDouble() * 0.5f;
            if (useLibrary)
            {
                // sheet-05 bushes; a spot a bigger library building now covers is simply skipped
                if (!Blocked(at, 0f, true)) PropPrefab(bushKinds[bushIndex++ % bushKinds.Length], at, (float)rng.NextDouble() * 360f, Vector3.one * s);
                continue;
            }
            b.Prism(at + Vector3.up * 0.12f, 5, 0.05f, 0.04f, 0.24f, stem);
            b.Ball(at + new Vector3(0f, 0.34f, 0f) * s, 0.42f * s, leafA, 0.78f);
            b.Ball(at + new Vector3(0.3f, 0.26f, 0.12f) * s, 0.30f * s, leafB, 0.8f);
            b.Ball(at + new Vector3(-0.26f, 0.24f, -0.16f) * s, 0.28f * s, rng.NextDouble() < 0.5 ? leafA : leafB, 0.8f);
        }

        // Grass tufts along paths, fences and the stream - scattered, never on a path itself.
        int tufts = 0;
        for (int attempt = 0; attempt < 900 && tufts < 260; attempt++)
        {
            var at = new Vector3(-12f + (float)rng.NextDouble() * 38f, 0f, -20f + (float)rng.NextDouble() * 40f);
            if (!NearButClear(at)) continue;
            if (useLibrary)
            {
                if (tufts % 2 == 0) PropPrefab(tufts % 6 == 0 ? "Grass_Clump_Dry" : tufts % 4 == 0 ? "Grass_Clump_B" : "Grass_Clump_A", at,
                                              (float)rng.NextDouble() * 360f, Vector3.one * (0.8f + (float)rng.NextDouble() * 0.5f));
                tufts++;
                continue;
            }
            for (int k = 0; k < 3; k++)
            {
                float yaw = (float)rng.NextDouble() * 360f, lean = 12f + (float)rng.NextDouble() * 18f;
                b.Frustum(at + new Vector3((k - 1) * 0.05f, 0.13f, 0f), 0.05f, 0.02f, 0.005f, 0.005f, 0.26f + (float)rng.NextDouble() * 0.1f,
                    k == 1 ? leafB : leafA, Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(lean, 0f, 0f));
            }
            tufts++;
        }

        // Flower clumps: by doors, along the lane, round the well and the sign.
        Vector3[] clumps =
        {
            new Vector3(3.9f, 0f, -2.2f), new Vector3(5.8f, 0f, -2.45f), new Vector3(7.4f, 0f, -4.6f), new Vector3(8.2f, 0f, -2.4f),
            new Vector3(17.9f, 0f, 1.7f), new Vector3(19.7f, 0f, -1.4f), new Vector3(19.8f, 0f, 6.6f), new Vector3(19.7f, 0f, 3.7f),
            new Vector3(-3.8f, 0f, 13.7f), new Vector3(5.8f, 0f, 16.5f), new Vector3(-4.6f, 0f, -15.1f), new Vector3(-13.6f, 0f, 7.9f),
            new Vector3(-6.1f, 0f, -1.2f), new Vector3(-6.3f, 0f, -4.4f), new Vector3(1.9f, 0f, 13.6f), new Vector3(-9.5f, 0f, 3.3f),
        };
        string[] flowerKinds = { "Flowers_Daisy", "Flowers_Poppy", "Flowers_Lupine", "Flowers_Bluebell" };
        int clumpIndex = 0;
        foreach (var c in clumps)
        {
            if (useLibrary)
            {
                if (!Blocked(c, 0f, true))
                {
                    PropPrefab(flowerKinds[clumpIndex % flowerKinds.Length], c, clumpIndex * 47f, Vector3.one);
                    PropPrefab(flowerKinds[(clumpIndex + 1) % flowerKinds.Length], c + new Vector3(0.35f, 0f, -0.25f), clumpIndex * 91f, Vector3.one * 0.8f);
                }
                clumpIndex++;
                continue;
            }
            for (int k = 0; k < 7; k++)
            {
                var at = c + new Vector3((float)(rng.NextDouble() - 0.5) * 0.9f, 0f, (float)(rng.NextDouble() - 0.5) * 0.9f);
                float h = 0.14f + (float)rng.NextDouble() * 0.12f;
                fb.Ball(at + Vector3.up * h * 0.5f, 0.07f, fStem, 1.2f);
                fb.Ball(at + Vector3.up * (h + 0.04f), 0.055f, petals[rng.Next(petals.Length)]);
            }
        }

        Place(village, "TreeBushes", b.Build("TreeBushes")); // "Tree" prefix: seasonal tint (see MeadowLife)
        Place(village, "Flowers", fb.Build("VillageFlowers"));
    }

    // Close to a path/stream/house edge (within 1.6 m of an obstacle's edge) but not on it.
    private static bool NearButClear(Vector3 at)
    {
        bool near = false;
        foreach (var o in obstacles)
        {
            if (o.isWorkshop) { if (o.Penetration(at, 0f, out _) > 0f) return false; continue; }
            if (o.Penetration(at, 0.15f, out _) > 0f) return false;
            if (o.Penetration(at, 1.6f, out _) > 0f) near = true;
        }
        return near;
    }

    // ---- scene helpers ----------------------------------------------------------------------------

    private static GameObject Place(Transform parent, string name, LowPolyModel model)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        string path = $"{MeshDir}/{name}.asset";
        if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
        if (model.mesh.vertexCount > 0)   // a group can be all library prefabs now, with no mesh of its own
        {
            AssetDatabase.CreateAsset(model.mesh, path);
            model.Attach(go, lit);
        }
        SpawnPending(go.transform);
        return go;
    }

    private static float Facing(Vector3 from, Vector3 to)
    {
        // Yaw that turns local -Z (the rack's front) toward `to`.
        Vector3 d = from - to; d.y = 0f;
        return Quaternion.LookRotation(d).eulerAngles.y;
    }

    private static List<Vector3> Line(Vector2 a, Vector2 b) => new List<Vector3> { new Vector3(a.x, 0f, a.y), new Vector3(b.x, 0f, b.y) };

    // Catmull-Rom through the points, sampled every `step` metres, on the ground plane.
    private static List<Vector3> Smooth(IList<Vector2> pts, float step)
    {
        var res = new List<Vector3>();
        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector2 p0 = pts[Mathf.Max(i - 1, 0)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Mathf.Min(i + 2, pts.Count - 1)];
            int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(p1, p2) / step));
            for (int k = 0; k < n; k++)
            {
                float t = k / (float)n;
                Vector2 q = 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (3f * p1 - p0 - 3f * p2 + p3) * t * t * t);
                res.Add(new Vector3(q.x, 0f, q.y));
            }
        }
        var last = pts[pts.Count - 1];
        res.Add(new Vector3(last.x, 0f, last.y));
        return res;
    }

    // A flat strip along a polyline: one thin box per segment, slightly overlapping so bends leave no gaps.
    private static void Ribbon(MeshBuilder b, List<Vector3> pts, System.Func<int, float> width, float top, float thick, int paint, float extra = 0.2f, float offset = 0f)
    {
        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector3 d = pts[i + 1] - pts[i];
            float len = d.magnitude;
            if (len < 1e-4f) continue;
            Vector3 dir = d / len, n = new Vector3(dir.z, 0f, -dir.x);
            Vector3 mid = (pts[i] + pts[i + 1]) * 0.5f + n * offset;
            mid.y = top - thick * 0.5f;
            b.Box(mid, new Vector3(width(i), thick, len + extra), paint, Quaternion.LookRotation(dir));
        }
    }

    // A dirt path: a slightly wobbly-width strip, a few pebbles at its edges; registered as an obstacle.
    private static void Path(MeshBuilder b, Pal p, List<Vector3> pts, float width, System.Random rng)
    {
        if (pts.Count == 2)
        {
            // Straight runs get intermediate points so the width can wobble along them.
            var a = pts[0]; var c = pts[1];
            int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, c) / 0.7f));
            pts = new List<Vector3>();
            for (int i = 0; i <= n; i++) pts.Add(Vector3.Lerp(a, c, i / (float)n));
        }
        var widths = new List<float>();
        for (int i = 0; i < pts.Count; i++) widths.Add(width * (0.9f + 0.2f * (float)rng.NextDouble()));
        Ribbon(b, pts, i => widths[i], 0.018f, 0.03f, p.dirt, 0.25f);
        for (int i = 0; i < pts.Count - 1; i++)
        {
            obstacles.Add(Obstacle.Segment(pts[i], pts[i + 1], width * 0.5f + 0.3f));
            if (rng.NextDouble() < 0.35)
            {
                Vector3 dir = (pts[i + 1] - pts[i]).normalized, n = new Vector3(dir.z, 0f, -dir.x);
                float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                b.Ball(pts[i] + n * side * (widths[i] * 0.5f + 0.05f), 0.07f + (float)rng.NextDouble() * 0.06f, p.rock, 0.5f);
            }
        }
    }

    // ---- obstacles (for keeping trees/tufts off paths, houses, the stream and the workshop) -----------

    private class Obstacle
    {
        private int kind; // 0 circle, 1 segment, 2 rect
        private Vector3 a, c;
        private float r, hx, hz;
        private Quaternion rot;
        public bool isWorkshop, isRiver;

        public static Obstacle Circle(Vector3 center, float radius, bool workshop = false) => new Obstacle { kind = 0, a = center, r = radius, isWorkshop = workshop };
        public static Obstacle Segment(Vector3 from, Vector3 to, float radius) => new Obstacle { kind = 1, a = from, c = to, r = radius };
        public static Obstacle Rect(Vector3 center, float yaw, float halfX, float halfZ) => new Obstacle { kind = 2, a = center, hx = halfX, hz = halfZ, rot = Quaternion.Euler(0f, yaw, 0f) };

        // How far a point (with a clearance margin) sits inside this obstacle, and the way out.
        public float Penetration(Vector3 p, float margin, out Vector3 dir)
        {
            p.y = 0f;
            dir = Vector3.right;
            if (kind == 0 || kind == 1)
            {
                Vector3 closest = a;
                if (kind == 1)
                {
                    Vector3 ab = c - a;
                    float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                    closest = a + ab * t;
                }
                closest.y = 0f;
                Vector3 d = p - closest;
                float dist = d.magnitude;
                dir = dist > 1e-3f ? d / dist : (kind == 1 ? new Vector3((c - a).z, 0f, -(c - a).x).normalized : Vector3.right);
                return r + margin - dist;
            }
            Vector3 l = Quaternion.Inverse(rot) * (p - new Vector3(a.x, 0f, a.z));
            float px = hx + margin - Mathf.Abs(l.x), pz = hz + margin - Mathf.Abs(l.z);
            if (px <= 0f || pz <= 0f) return 0f;
            if (px < pz) { dir = rot * new Vector3(Mathf.Sign(l.x == 0f ? 1f : l.x), 0f, 0f); return px; }
            dir = rot * new Vector3(0f, 0f, Mathf.Sign(l.z == 0f ? 1f : l.z));
            return pz;
        }
    }

    // ---- models -----------------------------------------------------------------------------------

    private static readonly Quaternion AlongX = Quaternion.Euler(0f, 0f, -90f);
    private static readonly Quaternion AlongZ = Quaternion.Euler(90f, 0f, 0f);
    private static Color C(float r, float g, float b) => new Color(r, g, b);
    private static Color Shade(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k);

    // One palette per MeshBuilder (paints that end up unused are dropped by MeshBuilder.Build).
    private class Pal
    {
        public int wood, woodDark, woodLight, stone, stoneDark, iron, straw, strawDark, dirt, dirtDark, mud, rock, rockDark,
            water, waterDeep, coal, cream, clothRed, sack, leaf, apple, bread, reed, reedTop, soil, flame, paper, rope, barnRed, barnTrim;

        public Pal(MeshBuilder b)
        {
            wood = b.Paint(C(0.42f, 0.28f, 0.15f), 0.08f);
            woodDark = b.Paint(C(0.30f, 0.19f, 0.10f), 0.08f);
            woodLight = b.Paint(C(0.56f, 0.41f, 0.25f), 0.08f);
            stone = b.Paint(C(0.50f, 0.48f, 0.45f), 0.08f);
            stoneDark = b.Paint(C(0.38f, 0.36f, 0.34f), 0.08f);
            iron = b.Paint(C(0.20f, 0.21f, 0.24f), 0.4f);
            straw = b.Paint(C(0.80f, 0.66f, 0.34f), 0.05f);
            strawDark = b.Paint(C(0.66f, 0.52f, 0.25f), 0.05f);
            dirt = b.Paint(C(0.47f, 0.38f, 0.26f), 0.03f);
            dirtDark = b.Paint(C(0.39f, 0.31f, 0.21f), 0.03f);
            mud = b.Paint(C(0.36f, 0.31f, 0.22f), 0.05f);
            rock = b.Paint(C(0.52f, 0.52f, 0.50f), 0.08f);
            rockDark = b.Paint(C(0.40f, 0.40f, 0.40f), 0.08f);
            water = b.Paint(C(0.20f, 0.46f, 0.66f), 0.8f);      // the menu art's clear mid-blue (was a pale cyan that washed out)
            waterDeep = b.Paint(C(0.13f, 0.34f, 0.56f), 0.8f);
            coal = b.Paint(C(0.09f, 0.09f, 0.10f), 0.2f);
            cream = b.Paint(C(0.91f, 0.86f, 0.72f), 0.1f);
            clothRed = b.Paint(C(0.70f, 0.22f, 0.18f), 0.1f);
            sack = b.Paint(C(0.55f, 0.44f, 0.29f), 0.05f);
            leaf = b.Paint(C(0.26f, 0.46f, 0.20f), 0.1f);
            apple = b.Paint(C(0.78f, 0.16f, 0.12f), 0.3f);
            bread = b.Paint(C(0.76f, 0.55f, 0.30f), 0.1f);
            reed = b.Paint(C(0.36f, 0.50f, 0.22f), 0.1f);
            reedTop = b.Paint(C(0.42f, 0.28f, 0.16f), 0.1f);
            soil = b.Paint(C(0.27f, 0.19f, 0.12f), 0.03f);
            flame = b.Paint(C(1f, 0.72f, 0.30f), 0f, 1.5f);
            paper = b.Paint(C(0.92f, 0.87f, 0.72f), 0.05f);
            rope = b.Paint(C(0.62f, 0.52f, 0.36f), 0.05f);
            barnRed = b.Paint(C(0.52f, 0.24f, 0.17f), 0.06f);
            barnTrim = b.Paint(C(0.86f, 0.80f, 0.68f), 0.06f);
        }
    }

    // Places sub-shapes at a position + yaw inside a shared MeshBuilder.
    private class Put
    {
        private readonly MeshBuilder b;
        private readonly Vector3 o;
        private readonly Quaternion q;
        public readonly float yaw;
        public Vector3 Origin => o;
        public Put(MeshBuilder b, Vector3 origin, float yaw) { this.b = b; o = origin; this.yaw = yaw; q = Quaternion.Euler(0f, yaw, 0f); }
        private Vector3 P(Vector3 l) => o + q * l;
        public Vector3 World(Vector3 local) => P(local);
        private Quaternion R(Quaternion? r) => q * (r ?? Quaternion.identity);
        public void Box(Vector3 c, Vector3 s, int paint, Quaternion? r = null) => b.Box(P(c), s, paint, R(r));
        public void Frustum(Vector3 c, float w0, float d0, float w1, float d1, float h, int paint, Quaternion? r = null) => b.Frustum(P(c), w0, d0, w1, d1, h, paint, R(r));
        public void Prism(Vector3 c, int sides, float r0, float r1, float len, int paint, Quaternion? r = null) => b.Prism(P(c), sides, r0, r1, len, paint, R(r));
        public void Ball(Vector3 c, float radius, int paint, float squash = 1f) => b.Ball(P(c), radius, paint, squash);
    }

    // A timber-framed house in the same style (and local frame) as LowPoly.HouseStructure: front wall
    // at +X, footprint centred on the origin, floor at y = 0.
    private static LowPolyModel House(HouseSpec s)
    {
        var b = new MeshBuilder();
        var p = new Pal(b);
        int plaster = b.Paint(s.plaster, 0.05f);
        int timber = b.Paint(C(0.30f, 0.19f, 0.10f), 0.08f);
        int roof = b.Paint(s.roof, 0.06f);
        int roofDark = b.Paint(Shade(s.roof, 0.8f), 0.06f);
        // Two window-glass paints, alternating window by window: DayCycle lights each group up on its own evening
        // schedule, so a house can have some windows glowing and others dark.
        int glass = b.Paint(C(1f, 0.78f, 0.42f), 0.2f, 0.55f);
        int glassB = b.Paint(C(1f, 0.74f, 0.40f), 0.2f, 0.55f);
        int shutter = b.Paint(s.shutter, 0.1f);
        int door = b.Paint(C(0.42f, 0.27f, 0.14f), 0.08f);
        int shadow = b.Paint(C(0.08f, 0.06f, 0.05f), 0f);

        const float F = 0.3f;
        float D = s.D, W = s.W, H = s.H, front = D * 0.5f;
        b.Box(new Vector3(0f, F * 0.5f, 0f), new Vector3(D + 0.2f, F, W + 0.2f), p.stone);
        b.Box(new Vector3(0f, (F + H) * 0.5f, 0f), new Vector3(D, H - F, W), plaster);
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
                b.Box(new Vector3(sx * (front + 0.01f), (F + H) * 0.5f, sz * (W * 0.5f + 0.01f)), new Vector3(0.16f, H - F, 0.16f), timber);
        b.Box(new Vector3(front + 0.02f, H - 0.06f, 0f), new Vector3(0.12f, 0.14f, W + 0.1f), timber);
        b.Box(new Vector3(-front - 0.02f, H - 0.06f, 0f), new Vector3(0.12f, 0.14f, W + 0.1f), timber);
        foreach (float sz in new[] { -1f, 1f }) b.Box(new Vector3(0f, H - 0.06f, sz * (W * 0.5f + 0.02f)), new Vector3(D + 0.1f, 0.14f, 0.12f), timber);
        // Door: frame posts, lintel, a planked leaf with iron straps, a stone step.
        float dl = s.doorZ - 0.58f, dr = s.doorZ + 0.58f;
        foreach (float z in new[] { dl, dr }) b.Box(new Vector3(front + 0.03f, (F + H) * 0.5f, z), new Vector3(0.12f, H - F, 0.14f), timber);
        b.Box(new Vector3(front + 0.03f, F + 1.97f, s.doorZ), new Vector3(0.14f, 0.14f, 1.3f), timber);
        b.Box(new Vector3(front + 0.005f, F + 0.95f, s.doorZ), new Vector3(0.02f, 1.9f, 1.0f), shadow);
        b.Box(new Vector3(front + 0.04f, F + 0.95f, s.doorZ), new Vector3(0.06f, 1.88f, 0.96f), door);
        foreach (float y in new[] { 0.45f, 1.45f }) b.Box(new Vector3(front + 0.08f, F + y, s.doorZ - 0.1f), new Vector3(0.02f, 0.07f, 0.7f), p.iron);
        b.Box(new Vector3(front + 0.3f, 0.1f, s.doorZ), new Vector3(0.55f, 0.2f, 1.25f), p.stone);
        // A diagonal brace in the widest bare stretch of the front wall.
        float braceZ = s.doorZ > 0f ? -W * 0.5f + 0.5f : W * 0.5f - 0.5f;
        b.Box(new Vector3(front + 0.03f, (F + H) * 0.5f + 0.1f, braceZ), new Vector3(0.08f, (H - F) * 0.95f, 0.1f), timber, Quaternion.Euler(Mathf.Sign(braceZ) * 30f, 0f, 0f));

        for (int wi = 0; wi < s.windows.Length; wi++) Window(new Put(b, new Vector3(front, F + 1.2f, s.windows[wi]), 0f), timber, wi % 2 == 0 ? glass : glassB, shutter, p, s.flowerBox);
        Window(new Put(b, new Vector3(0f, F + 1.2f, W * 0.5f), -90f), timber, glassB, shutter, p, false);
        Window(new Put(b, new Vector3(0f, F + 1.2f, -W * 0.5f), 90f), timber, glass, shutter, p, false);

        float top = H, cx = 0f, RD = D;
        if (s.upper > 0f)
        {
            const float jet = 0.3f; // the upper storey overhangs the street a little
            cx = jet * 0.5f; RD = D + jet;
            b.Box(new Vector3(cx, H + 0.07f, 0f), new Vector3(RD + 0.08f, 0.14f, W + 0.08f), timber);
            float mid = H + 0.14f + s.upper * 0.5f, uf = cx + RD * 0.5f;
            b.Box(new Vector3(cx, mid, 0f), new Vector3(RD, s.upper, W), plaster);
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    b.Box(new Vector3(cx + sx * (RD * 0.5f + 0.01f), mid, sz * (W * 0.5f + 0.01f)), new Vector3(0.14f, s.upper, 0.14f), timber);
            foreach (float sz in new[] { -1f, 1f })
                b.Box(new Vector3(uf + 0.03f, mid, sz * (W * 0.5f - 0.5f)), new Vector3(0.08f, s.upper * 1.1f, 0.1f), timber, Quaternion.Euler(sz * 32f, 0f, 0f));
            top = H + 0.14f + s.upper;
            b.Box(new Vector3(uf + 0.02f, top - 0.06f, 0f), new Vector3(0.12f, 0.12f, W + 0.1f), timber);
            var upperWindows = new List<float>(s.windows) { s.doorZ };
            foreach (float z in upperWindows)
                if (!s.innSign || Mathf.Abs(z - s.doorZ) > 0.1f)
                    Window(new Put(b, new Vector3(uf, mid - 0.9f + 0.1f, z), 0f), timber, z > 0f ? glassB : glass, shutter, p, false);
            if (s.innSign)
            {
                // A painted board over the door: a frothy mug.
                b.Box(new Vector3(uf + 0.04f, mid, s.doorZ), new Vector3(0.05f, 0.62f, 1.25f), p.woodDark);
                b.Box(new Vector3(uf + 0.07f, mid, s.doorZ), new Vector3(0.02f, 0.52f, 1.12f), p.woodLight);
                b.Box(new Vector3(uf + 0.09f, mid - 0.04f, s.doorZ - 0.05f), new Vector3(0.02f, 0.26f, 0.2f), p.straw);
                b.Box(new Vector3(uf + 0.09f, mid + 0.12f, s.doorZ - 0.05f), new Vector3(0.02f, 0.08f, 0.24f), p.cream);
                b.Box(new Vector3(uf + 0.09f, mid - 0.03f, s.doorZ + 0.11f), new Vector3(0.02f, 0.14f, 0.05f), p.straw);
            }
        }

        // Gable roof, ridge along Z (same construction as the smith's house).
        float R = top + s.rise, half = RD * 0.5f, t = 0.14f, overhang = 0.4f;
        b.Frustum(new Vector3(cx, top + s.rise * 0.5f, 0f), RD, W, 0.04f, W, s.rise, plaster);
        float theta = Mathf.Atan2(s.rise, half), slab = Mathf.Sqrt(half * half + s.rise * s.rise) + overhang;
        foreach (float sd in new[] { 1f, -1f })
        {
            Vector3 down = new Vector3(sd * Mathf.Cos(theta), -Mathf.Sin(theta), 0f), normal = new Vector3(sd * Mathf.Sin(theta), Mathf.Cos(theta), 0f);
            Vector3 c = new Vector3(cx + sd * half * 0.5f, (R + top) * 0.5f, 0f) + normal * (t * 0.5f) + down * (overhang * 0.5f);
            var rot = Quaternion.Euler(0f, 0f, -sd * theta * Mathf.Rad2Deg);
            b.Box(c, new Vector3(slab, t, W + 0.6f), roof, rot);
            b.Box(c + down * (slab * 0.5f - 0.12f) + normal * 0.03f, new Vector3(0.24f, t, W + 0.62f), roofDark, rot);
        }
        b.Box(new Vector3(cx, R + 0.1f, 0f), new Vector3(0.22f, 0.16f, W + 0.7f), timber);
        if (!float.IsNaN(s.chimneyZ))
        {
            chimneyTops.Add(s.pos + Quaternion.Euler(0f, s.yaw, 0f) * new Vector3(cx - RD * 0.22f, R + 1.02f, s.chimneyZ));
            b.Box(new Vector3(cx - RD * 0.22f, (top + R + 0.9f) * 0.5f, s.chimneyZ), new Vector3(0.58f, R + 0.9f - top, 0.58f), p.stone);
            b.Box(new Vector3(cx - RD * 0.22f, R + 0.95f, s.chimneyZ), new Vector3(0.72f, 0.12f, 0.72f), p.stoneDark);
        }
        if (s.leanTo)
        {
            // A plank shed on the -Z side with a firewood stack in front of it.
            float lz = -W * 0.5f - 0.75f;
            b.Box(new Vector3(-D * 0.1f, 0.95f, lz), new Vector3(D * 0.7f, 1.9f, 1.4f), p.wood);
            b.Box(new Vector3(-D * 0.1f, 2.0f, lz - 0.05f), new Vector3(D * 0.8f, 0.08f, 1.75f), roofDark, Quaternion.Euler(-14f, 0f, 0f));
            Firewood(new Put(b, new Vector3(D * 0.35f + 0.25f, 0f, lz), 0f), p, 3, 0.7f);
        }
        return b.Build("House");
    }

    // Window on a wall whose outward normal is local +X (origin on the wall surface, at the window centre).
    private static void Window(Put w, int timber, int glass, int shutter, Pal p, bool flowerBox)
    {
        w.Box(new Vector3(0.04f, 0f, 0f), new Vector3(0.08f, 0.86f, 0.86f), timber);
        w.Box(new Vector3(0.07f, 0f, 0f), new Vector3(0.03f, 0.7f, 0.7f), glass);
        w.Box(new Vector3(0.09f, 0f, 0f), new Vector3(0.03f, 0.7f, 0.05f), timber);
        w.Box(new Vector3(0.09f, 0f, 0f), new Vector3(0.03f, 0.05f, 0.7f), timber);
        foreach (float s in new[] { -1f, 1f }) w.Box(new Vector3(0.06f, 0f, s * 0.66f), new Vector3(0.05f, 0.8f, 0.4f), shutter);
        if (!flowerBox) return;
        w.Box(new Vector3(0.13f, -0.5f, 0f), new Vector3(0.2f, 0.15f, 0.88f), p.wood);
        for (int i = 0; i < 5; i++)
        {
            float z = -0.34f + i * 0.17f;
            w.Ball(new Vector3(0.13f, -0.38f, z), 0.07f, p.leaf, 0.8f);
            w.Ball(new Vector3(0.15f, -0.31f, z + 0.03f), 0.045f, i % 2 == 0 ? p.clothRed : p.straw);
        }
    }

    private static LowPolyModel Barn()
    {
        var b = new MeshBuilder();
        var p = new Pal(b);
        int roof = b.Paint(C(0.33f, 0.25f, 0.20f), 0.06f);
        float D = 4.6f, W = 6.5f, H = 2.8f, rise = 2.1f, front = D * 0.5f;
        b.Box(new Vector3(0f, 0.1f, 0f), new Vector3(D + 0.15f, 0.2f, W + 0.15f), p.stone);
        b.Box(new Vector3(0f, 0.2f + (H - 0.2f) * 0.5f, 0f), new Vector3(D, H - 0.2f, W), p.barnRed);
        b.Frustum(new Vector3(0f, H + rise * 0.5f, 0f), D, W, 0.04f, W, rise, p.barnRed);
        // Vertical board battens on the long walls.
        for (float z = -W * 0.5f + 0.4f; z < W * 0.5f - 0.2f; z += 0.8f)
            foreach (float sx in new[] { -1f, 1f })
                if (sx < 0f || Mathf.Abs(z) > 1.35f)
                    b.Box(new Vector3(sx * (front + 0.02f), H * 0.5f + 0.1f, z), new Vector3(0.04f, H - 0.2f, 0.08f), p.woodDark);
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
                b.Box(new Vector3(sx * (front + 0.02f), H * 0.5f, sz * (W * 0.5f + 0.02f)), new Vector3(0.14f, H, 0.14f), p.barnTrim);
        // Big double doors with the classic white X braces, and a hay loft above.
        foreach (float s in new[] { -1f, 1f })
        {
            Vector3 dc = new Vector3(front + 0.05f, 0.2f + 1.15f, s * 0.6f);
            b.Box(dc, new Vector3(0.06f, 2.3f, 1.16f), p.woodDark);
            b.Box(dc + new Vector3(0.04f, 0f, 0f), new Vector3(0.03f, 2.5f, 0.1f), p.barnTrim, Quaternion.Euler(26f, 0f, 0f));
            b.Box(dc + new Vector3(0.04f, 0f, 0f), new Vector3(0.03f, 2.5f, 0.1f), p.barnTrim, Quaternion.Euler(-26f, 0f, 0f));
            b.Box(dc + new Vector3(0.04f, 1.12f, 0f), new Vector3(0.03f, 0.1f, 1.16f), p.barnTrim);
            b.Box(dc + new Vector3(0.04f, -1.12f, 0f), new Vector3(0.03f, 0.1f, 1.16f), p.barnTrim);
        }
        b.Box(new Vector3(front + 0.01f, H + 0.6f, 0f), new Vector3(0.04f, 0.9f, 0.9f), p.coal);
        b.Box(new Vector3(front + 0.04f, H + 0.6f, 0f), new Vector3(0.04f, 1.0f, 1.0f), p.barnTrim);
        b.Box(new Vector3(front + 0.06f, H + 0.6f, 0f), new Vector3(0.03f, 0.9f, 0.9f), p.coal);
        b.Ball(new Vector3(front + 0.08f, H + 0.35f, 0.1f), 0.3f, p.straw, 0.6f);
        float half = D * 0.5f, t = 0.14f, overhang = 0.4f, R = H + rise;
        float theta = Mathf.Atan2(rise, half), slab = Mathf.Sqrt(half * half + rise * rise) + overhang;
        foreach (float sd in new[] { 1f, -1f })
        {
            Vector3 down = new Vector3(sd * Mathf.Cos(theta), -Mathf.Sin(theta), 0f), normal = new Vector3(sd * Mathf.Sin(theta), Mathf.Cos(theta), 0f);
            Vector3 c = new Vector3(sd * half * 0.5f, (R + H) * 0.5f, 0f) + normal * (t * 0.5f) + down * (overhang * 0.5f);
            b.Box(c, new Vector3(slab, t, W + 0.5f), roof, Quaternion.Euler(0f, 0f, -sd * theta * Mathf.Rad2Deg));
        }
        b.Box(new Vector3(0f, R + 0.1f, 0f), new Vector3(0.22f, 0.16f, W + 0.6f), p.woodDark);
        return b.Build("Barn");
    }


    private static void Bridge(Put u, Pal p, float length, float width)
    {
        // Planks across a gentle arch, stringers below, stone abutments, posts and hand rails.
        float half = length * 0.5f;
        float Deck(float x) => 0.12f + 0.2f * (1f - (x / half) * (x / half));
        int n = Mathf.CeilToInt(length / 0.3f);
        for (int i = 0; i < n; i++)
        {
            float x = -half + (i + 0.5f) * length / n;
            u.Box(new Vector3(x, Deck(x) - 0.04f, 0f), new Vector3(length / n - 0.02f, 0.08f, width), i % 3 == 0 ? p.woodLight : p.wood);
        }
        foreach (float s in new[] { -1f, 1f })
        {
            for (int k = 0; k < 4; k++)
            {
                float x0 = -half + k * length / 4f, x1 = x0 + length / 4f, xm = (x0 + x1) * 0.5f;
                float ang = Mathf.Atan2(Deck(x1) - Deck(x0), x1 - x0) * Mathf.Rad2Deg;
                u.Box(new Vector3(xm, Deck(xm) - 0.14f, s * (width * 0.5f - 0.15f)), new Vector3(length / 4f + 0.05f, 0.12f, 0.12f), p.woodDark, Quaternion.Euler(0f, 0f, ang));
                u.Box(new Vector3(xm, Deck(xm) + 0.72f, s * width * 0.5f), new Vector3(length / 4f + 0.05f, 0.07f, 0.08f), p.wood, Quaternion.Euler(0f, 0f, ang));
            }
            for (int k = 0; k <= 4; k++)
            {
                float x = -half + k * length / 4f;
                u.Box(new Vector3(x, Deck(x) + 0.36f, s * width * 0.5f), new Vector3(0.1f, 0.8f, 0.1f), p.woodDark);
            }
            u.Box(new Vector3(s * (half + 0.1f), 0.1f, 0f), new Vector3(0.6f, 0.26f, width + 0.4f), p.stone);
        }
    }

    private static void Reeds(MeshBuilder b, Pal p, Vector3 at, System.Random rng)
    {
        int count = 4 + rng.Next(4);
        for (int k = 0; k < count; k++)
        {
            var o = at + new Vector3((float)(rng.NextDouble() - 0.5) * 0.4f, 0f, (float)(rng.NextDouble() - 0.5) * 0.4f);
            float h = 0.5f + (float)rng.NextDouble() * 0.45f;
            var tilt = Quaternion.Euler((float)(rng.NextDouble() - 0.5) * 16f, 0f, (float)(rng.NextDouble() - 0.5) * 16f);
            b.Frustum(o + tilt * new Vector3(0f, h * 0.5f, 0f), 0.04f, 0.03f, 0.01f, 0.01f, h, p.reed, tilt);
            if (k % 2 == 0) b.Prism(o + tilt * new Vector3(0f, h * 0.82f, 0f), 6, 0.03f, 0.03f, 0.13f, p.reedTop, tilt);
        }
    }

    private static void FenceRun(MeshBuilder b, Pal p, Vector3 from, Vector3 to, bool picket)
    {
        Vector3 d = to - from;
        float len = d.magnitude;
        if (!picket && PF("Fence_Straight") != null && len > 0.5f)
        {
            // library fence panels (2 m each, sheet 06), stretched a little to fit the run exactly
            int n = Mathf.Max(1, Mathf.RoundToInt(len / 2f));
            float yawRun = Quaternion.LookRotation(d / len).eulerAngles.y;
            for (int i = 0; i < n; i++)
                PropPrefab("Fence_Straight", Vector3.Lerp(from, to, (i + 0.5f) / n), yawRun, new Vector3(1f, 1f, len / n / 2f));
            return;
        }
        int posts = Mathf.Max(1, Mathf.RoundToInt(len / 1.3f));
        var rot = Quaternion.LookRotation(d.normalized);
        for (int i = 0; i <= posts; i++)
            b.Prism(Vector3.Lerp(from, to, i / (float)posts) + Vector3.up * 0.47f, 5, 0.065f, 0.055f, 0.94f, p.woodDark);
        foreach (float y in new[] { 0.42f, 0.78f })
            b.Box((from + to) * 0.5f + Vector3.up * y, new Vector3(0.05f, 0.08f, len), p.wood, rot);
        if (!picket) return;
        int pickets = Mathf.RoundToInt(len / 0.22f);
        for (int i = 1; i < pickets; i++)
            b.Box(Vector3.Lerp(from, to, i / (float)pickets) + Vector3.up * 0.38f, new Vector3(0.03f, 0.76f, 0.09f), p.woodLight, rot);
    }

    private static void Crate(Put u, Pal p, float s)
    {
        bool small = s < 0.68f;
        if (PropPrefab(small ? "Crate_Small" : "Crate_Large", u, 0f, s / (small ? 0.55f : 0.8f))) return;
        u.Box(new Vector3(0f, s * 0.5f, 0f), new Vector3(s, s, s), p.wood);
        foreach (float a in new[] { -1f, 1f })
            foreach (float c in new[] { -1f, 1f })
                u.Box(new Vector3(a * s * 0.5f, s * 0.5f, c * s * 0.5f), new Vector3(0.07f, s + 0.02f, 0.07f), p.woodDark);
        u.Box(new Vector3(0f, s * 0.5f, -s * 0.5f), new Vector3(s * 1.2f, 0.06f, 0.03f), p.woodDark, Quaternion.Euler(0f, 0f, 40f));
    }

    private static void Barrel(Put u, Pal p)
    {
        if (PropPrefab("Barrel_Large", u)) return;
        u.Prism(new Vector3(0f, 0.22f, 0f), 10, 0.27f, 0.31f, 0.44f, p.wood);
        u.Prism(new Vector3(0f, 0.66f, 0f), 10, 0.31f, 0.27f, 0.44f, p.wood);
        foreach (float y in new[] { 0.12f, 0.44f, 0.76f }) u.Prism(new Vector3(0f, y, 0f), 10, y == 0.44f ? 0.325f : 0.295f, y == 0.44f ? 0.325f : 0.295f, 0.05f, p.iron);
        u.Prism(new Vector3(0f, 0.885f, 0f), 10, 0.25f, 0.25f, 0.01f, p.woodDark);
    }

    private static void Sack(Put u, Pal p)
    {
        if (PropPrefab("Sacks", u, 0f, 0.8f)) return;
        u.Ball(new Vector3(0f, 0.2f, 0f), 0.3f, p.sack, 0.7f);
        u.Ball(new Vector3(0f, 0.42f, 0f), 0.13f, p.sack, 0.9f);
        u.Prism(new Vector3(0f, 0.5f, 0f), 6, 0.06f, 0.06f, 0.04f, p.rope);
    }

    private static void Bucket(Put u, Pal p)
    {
        if (PropPrefab("Bucket", u)) return;
        u.Prism(new Vector3(0f, 0.16f, 0f), 8, 0.15f, 0.19f, 0.32f, p.wood);
        u.Prism(new Vector3(0f, 0.3f, 0f), 8, 0.195f, 0.195f, 0.03f, p.iron);
        u.Prism(new Vector3(0f, 0.323f, 0f), 8, 0.17f, 0.17f, 0.005f, p.water);
    }

    private static void IronBars(Put u, Pal p)
    {
        if (PropPrefab("Metal_Ingots", u, 0f, 0.7f)) return;
        for (int i = 0; i < 4; i++)
            u.Box(new Vector3(-0.12f + i * 0.08f, 0.2f, 0f), new Vector3(0.05f, 0.5f, 0.05f), p.iron, Quaternion.Euler(0f, 0f, -8f + i * 5f));
    }

    private static void CoalHeap(Put u, Pal p)
    {
        if (PropPrefab("Coal_Bin", u, 90f)) return; // open slanted front toward the seat
        u.Ball(new Vector3(0f, 0.05f, 0f), 0.55f, p.coal, 0.45f);
        u.Ball(new Vector3(0.35f, 0.02f, 0.2f), 0.35f, p.coal, 0.45f);
        u.Ball(new Vector3(-0.3f, 0.02f, -0.15f), 0.32f, p.coal, 0.45f);
        // A shovel stuck in the heap.
        u.Prism(new Vector3(0.15f, 0.55f, 0f), 6, 0.022f, 0.022f, 0.9f, p.wood, Quaternion.Euler(0f, 0f, 20f));
        u.Box(new Vector3(0.02f, 0.2f, 0f), new Vector3(0.2f, 0.26f, 0.03f), p.iron, Quaternion.Euler(0f, 90f, 20f));
    }

    private static void Firewood(Put u, Pal p, int rows, float len)
    {
        if (PropPrefab("Firewood_Pile", u, FirewoodYaw, Mathf.Lerp(0.7f, 1f, (rows - 2) / 2f))) return;
        var rng = new System.Random(rows * 31 + (int)(len * 10));
        for (int row = 0; row < rows; row++)
            for (int i = 0; i < rows + 1 - row; i++)
            {
                float x = -(rows) * 0.1f + (i + row * 0.5f) * 0.2f, y = 0.1f + row * 0.17f;
                float l = len * (0.9f + (float)rng.NextDouble() * 0.2f);
                u.Prism(new Vector3(x, y, 0f), 6, 0.09f, 0.09f, l, p.wood, AlongZ);
                u.Prism(new Vector3(x, y, -l * 0.5f), 6, 0.075f, 0.075f, 0.01f, p.woodLight, AlongZ);
            }
    }

    // Wall-less tool rack: two posts, a beam, hammers and tongs hanging from it. Front faces local -Z.
    private static void ToolRack(Put u, Pal p)
    {
        if (PropPrefab("Tool_Rack", u, ToolRackYaw)) return;
        foreach (float x in new[] { -0.6f, 0.6f }) u.Prism(new Vector3(x, 0.8f, 0f), 6, 0.06f, 0.05f, 1.6f, p.woodDark);
        u.Box(new Vector3(0f, 1.45f, 0f), new Vector3(1.4f, 0.1f, 0.1f), p.wood);
        u.Box(new Vector3(0f, 0.7f, 0f), new Vector3(1.3f, 0.06f, 0.06f), p.wood);
        for (int i = 0; i < 4; i++)
        {
            float x = -0.42f + i * 0.28f;
            u.Box(new Vector3(x, 1.43f, -0.07f), new Vector3(0.02f, 0.06f, 0.06f), p.iron);
            if (i % 2 == 0)
            {
                u.Prism(new Vector3(x, 1.18f, -0.08f), 6, 0.018f, 0.018f, 0.42f, p.wood);
                u.Box(new Vector3(x, 0.95f, -0.08f), new Vector3(0.16f, 0.07f, 0.07f), p.iron);
            }
            else
            {
                foreach (float s in new[] { -1f, 1f })
                    u.Box(new Vector3(x + s * 0.02f, 1.17f, -0.08f), new Vector3(0.015f, 0.48f, 0.02f), p.iron, Quaternion.Euler(0f, 0f, s * 4f));
            }
        }
    }

    private static void BlacksmithSign(Put u, Pal p)
    {
        if (PropPrefab("Sign_Blacksmith", u, SignYaw)) return;
        // Post on the lane's north edge, arm reaching over the lane, board facing -X (toward the seat).
        u.Prism(new Vector3(0f, 1.2f, 0f), 6, 0.08f, 0.07f, 2.4f, p.woodDark);
        u.Box(new Vector3(0f, 2.25f, -0.5f), new Vector3(0.08f, 0.08f, 1.1f), p.woodDark);
        u.Box(new Vector3(0f, 2.05f, -0.9f), new Vector3(0.1f, 0.06f, 0.06f), p.iron);
        foreach (float z in new[] { -0.62f, -1.18f }) u.Box(new Vector3(0f, 2.06f, z), new Vector3(0.015f, 0.34f, 0.015f), p.iron);
        var board = new Vector3(0f, 1.66f, -0.9f);
        u.Box(board, new Vector3(0.06f, 0.5f, 0.78f), p.woodLight);
        u.Box(board, new Vector3(0.07f, 0.56f, 0.06f), p.woodDark);
        // Anvil silhouette, painted on both faces.
        foreach (float s in new[] { -1f, 1f })
        {
            float x = s * 0.035f;
            u.Box(board + new Vector3(x, 0.09f, 0.03f), new Vector3(0.01f, 0.08f, 0.36f), p.iron);
            u.Frustum(board + new Vector3(x, 0.09f, -0.2f), 0.01f, 0.1f, 0.01f, 0.01f, 0.08f, p.iron, Quaternion.Euler(0f, 0f, 0f));
            u.Box(board + new Vector3(x, 0.0f, 0.04f), new Vector3(0.01f, 0.1f, 0.12f), p.iron);
            u.Box(board + new Vector3(x, -0.08f, 0.04f), new Vector3(0.01f, 0.06f, 0.28f), p.iron);
            u.Box(board + new Vector3(x, 0.19f, 0.08f), new Vector3(0.01f, 0.1f, 0.04f), p.iron, Quaternion.Euler(-35f, 0f, 0f)); // hammer handle
        }
    }

    private static void LanternPost(Put u, Pal p)
    {
        if (PropPrefab("LanternPost", u, LanternPostYaw)) return;
        u.Prism(new Vector3(0f, 1.05f, 0f), 6, 0.07f, 0.06f, 2.1f, p.woodDark);
        u.Box(new Vector3(0.22f, 2.0f, 0f), new Vector3(0.5f, 0.06f, 0.06f), p.woodDark);
        u.Box(new Vector3(0.4f, 1.78f, 0f), new Vector3(0.17f, 0.24f, 0.17f), p.iron);
        u.Box(new Vector3(0.4f, 1.78f, 0f), new Vector3(0.19f, 0.16f, 0.12f), p.flame);
        u.Frustum(new Vector3(0.4f, 1.94f, 0f), 0.22f, 0.22f, 0.04f, 0.04f, 0.08f, p.iron);
    }

    // The storage rack's lantern, in StorageArea's frame (the rack's left post is at x 1.63, z -7.0 there).
    private static readonly Vector3 StorageLanternLocal = new Vector3(1.63f, 1.7f, -6.42f);

    // Every bench built this run (seat centre, yaw) - the crowd sits on them (VillageBuilder.Life.cs).
    private static readonly List<(Vector3 pos, float yaw)> benchSeats = new List<(Vector3, float)>();

    // The top of every chimney built this run: CityAmbience lets the nearest ones smoke (written out as "Chimneys" markers).
    private static readonly List<Vector3> chimneyTops = new List<Vector3>();

    private static void Bench(Put u, Pal p)
    {
        benchSeats.Add((u.Origin, u.yaw));
        if (PropPrefab("Bench", u, BenchYaw)) return;
        u.Box(new Vector3(0f, 0.45f, 0f), new Vector3(1.3f, 0.07f, 0.36f), p.wood);
        foreach (float x in new[] { -0.5f, 0.5f }) u.Box(new Vector3(x, 0.21f, 0f), new Vector3(0.08f, 0.42f, 0.3f), p.woodDark);
    }

    private static void Well(Put u, Pal p)
    {
        if (PropPrefab("Stone_Well", u, WellYaw)) return;
        u.Prism(new Vector3(0f, 0.37f, 0f), 10, 0.75f, 0.72f, 0.74f, p.stone);
        u.Prism(new Vector3(0f, 0.76f, 0f), 10, 0.76f, 0.76f, 0.06f, p.stoneDark);
        u.Prism(new Vector3(0f, 0.795f, 0f), 10, 0.56f, 0.56f, 0.01f, p.waterDeep);
        foreach (float x in new[] { -0.66f, 0.66f }) u.Box(new Vector3(x, 1.2f, 0f), new Vector3(0.12f, 1.6f, 0.12f), p.woodDark);
        u.Prism(new Vector3(0f, 1.6f, 0f), 6, 0.06f, 0.06f, 1.35f, p.wood, AlongX);
        u.Prism(new Vector3(0f, 1.3f, 0f), 4, 0.01f, 0.01f, 0.55f, p.rope);
        u.Prism(new Vector3(0f, 0.95f, 0f), 8, 0.11f, 0.13f, 0.18f, p.wood);
        // Little gable roof.
        foreach (float s in new[] { -1f, 1f })
            u.Box(new Vector3(0f, 2.05f, s * 0.32f), new Vector3(1.7f, 0.06f, 0.78f), p.woodDark, Quaternion.Euler(s * 32f, 0f, 0f));
    }

    // Counter faces local +X; the canopy slopes down toward the customer.
    private static void MarketStall(Put u, Pal p)
    {
        if (PropPrefab("Market_Stall", u)) return;
        foreach (float x in new[] { -0.55f, 0.55f })
            foreach (float z in new[] { -1.1f, 1.1f })
                u.Box(new Vector3(x, x > 0f ? 1.05f : 1.2f, z), new Vector3(0.1f, x > 0f ? 2.1f : 2.4f, 0.1f), p.woodDark);
        u.Box(new Vector3(0.35f, 0.42f, 0f), new Vector3(0.55f, 0.84f, 2.2f), p.wood);
        u.Box(new Vector3(0.38f, 0.87f, 0f), new Vector3(0.72f, 0.06f, 2.35f), p.woodLight);
        for (int i = 0; i < 6; i++)
            u.Box(new Vector3(0f, 2.28f, -1.2f + (i + 0.5f) * 0.4f), new Vector3(1.55f, 0.05f, 0.41f), i % 2 == 0 ? p.clothRed : p.cream, Quaternion.Euler(0f, 0f, -11f));
        for (int i = 0; i < 6; i++)
            u.Box(new Vector3(0.77f, 2.02f, -1.2f + (i + 0.5f) * 0.4f), new Vector3(0.03f, 0.2f, 0.4f), i % 2 == 0 ? p.clothRed : p.cream);
        // Produce: apples, cabbages, bread.
        float[] zs = { -0.72f, 0f, 0.72f };
        int[] fill = { p.apple, p.leaf, p.bread };
        for (int k = 0; k < 3; k++)
        {
            u.Box(new Vector3(0.4f, 0.95f, zs[k]), new Vector3(0.5f, 0.1f, 0.6f), p.woodDark);
            for (int i = 0; i < 5; i++)
                u.Ball(new Vector3(0.3f + (i % 2) * 0.18f, 1.04f, zs[k] - 0.2f + i * 0.1f), k == 2 ? 0.09f : 0.075f, fill[k], k == 2 ? 0.6f : 1f);
        }
    }

    private static void Cart(Put u, Pal p, bool sacks)
    {
        if (PropPrefab("Cart", u)) return;
        // Bed along local X, wheels on +/-Z, shafts toward +X.
        u.Box(new Vector3(0f, 0.72f, 0f), new Vector3(1.6f, 0.08f, 1.0f), p.wood);
        foreach (float s in new[] { -1f, 1f })
        {
            u.Box(new Vector3(0f, 0.9f, s * 0.5f), new Vector3(1.6f, 0.3f, 0.05f), p.woodLight);
            u.Prism(new Vector3(0.1f, 0.42f, s * 0.62f), 12, 0.42f, 0.42f, 0.07f, p.woodDark, AlongZ);
            u.Prism(new Vector3(0.1f, 0.42f, s * 0.66f), 8, 0.08f, 0.08f, 0.06f, p.iron, AlongZ);
            u.Box(new Vector3(1.35f, 0.55f, s * 0.35f), new Vector3(1.4f, 0.06f, 0.06f), p.woodDark, Quaternion.Euler(0f, 0f, -14f));
        }
        u.Box(new Vector3(-0.8f, 0.9f, 0f), new Vector3(0.05f, 0.3f, 1.0f), p.woodLight);
        u.Prism(new Vector3(0.1f, 0.42f, 0f), 6, 0.04f, 0.04f, 1.3f, p.iron, AlongZ);
        for (int i = 0; i < 3; i++)
        {
            if (sacks) u.Ball(new Vector3(-0.45f + i * 0.45f, 0.95f, (i % 2) * 0.15f - 0.05f), 0.26f, p.sack, 0.75f);
            else u.Ball(new Vector3(-0.45f + i * 0.45f, 0.95f, 0f), 0.42f, i == 1 ? p.straw : p.strawDark, 0.6f);
        }
    }

    private static void HayBale(Put u, Pal p)
    {
        u.Box(new Vector3(0f, 0.27f, 0f), new Vector3(1.0f, 0.54f, 0.6f), p.straw);
        foreach (float x in new[] { -0.28f, 0.28f }) u.Box(new Vector3(x, 0.27f, 0f), new Vector3(0.04f, 0.56f, 0.62f), p.strawDark);
    }

    private static void HayPile(Put u, Pal p)
    {
        u.Ball(new Vector3(0f, 0.1f, 0f), 0.75f, p.straw, 0.55f);
        u.Ball(new Vector3(0.45f, 0.05f, 0.3f), 0.45f, p.strawDark, 0.5f);
        u.Prism(new Vector3(-0.5f, 0.6f, 0.2f), 6, 0.02f, 0.02f, 1.2f, p.wood, Quaternion.Euler(20f, 0f, 25f)); // pitchfork
    }

    private static void Garden(MeshBuilder b, Pal p, Vector3 c)
    {
        // Two soil beds of cabbages and carrot tops inside a picket fence, gate gap facing the path (+X).
        float hx = 1.5f, hz = 1.2f;
        foreach (float z in new[] { -0.5f, 0.5f })
        {
            b.Box(c + new Vector3(0f, 0.05f, z), new Vector3(2.4f, 0.1f, 0.7f), p.soil);
            for (int i = 0; i < 6; i++)
            {
                var at = c + new Vector3(-1.0f + i * 0.4f, 0.1f, z);
                if (z < 0f) b.Ball(at + Vector3.up * 0.1f, 0.15f, p.leaf, 0.8f);
                else
                {
                    b.Frustum(at + Vector3.up * 0.1f, 0.08f, 0.08f, 0.02f, 0.02f, 0.2f, p.reed);
                    b.Frustum(at + new Vector3(0.1f, 0.1f, 0.08f), 0.07f, 0.07f, 0.02f, 0.02f, 0.18f, p.reed);
                }
            }
        }
        FenceRun(b, p, c + new Vector3(-hx, 0f, -hz), c + new Vector3(hx, 0f, -hz), true);
        FenceRun(b, p, c + new Vector3(-hx, 0f, hz), c + new Vector3(hx, 0f, hz), true);
        FenceRun(b, p, c + new Vector3(-hx, 0f, -hz), c + new Vector3(-hx, 0f, hz), true);
        FenceRun(b, p, c + new Vector3(hx, 0f, -hz), c + new Vector3(hx, 0f, -0.35f), true);
        FenceRun(b, p, c + new Vector3(hx, 0f, 0.35f), c + new Vector3(hx, 0f, hz), true);
    }
}
