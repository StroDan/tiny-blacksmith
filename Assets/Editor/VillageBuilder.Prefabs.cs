using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Placing the Blender-built environment library (EnvironmentLibrary / Assets/TinyBlacksmith/Environment) instead of
// the old procedural houses, props and trees. Part of VillageBuilder: when the library exists every builder routes
// through here (the procedural code is still there as the fallback when it doesn't).
//
// What each placed prefab still gives the rest of the game, exactly like the old meshes did:
//   - an obstacle rect (tree relocation, tufts), an occluder box (CityLife hides walkers behind it),
//   - its door as a street-graph door node, its chimney tops as smoke markers, lantern flames as light markers.
// Prefab frame (EnvironmentAsset): front toward +X, width along Z, ground at y = 0 - the old House frame.
public static partial class VillageBuilder
{
    private static bool useLibrary;
    private static GameObject PF(string name) => useLibrary ? EnvironmentLibrary.Prefab(name) : null;
    private static EnvironmentAsset Info(string name) => PF(name) != null ? PF(name).GetComponent<EnvironmentAsset>() : null;

    // The shortlists each district draws from (sheets 01, 02 and 03).
    private static readonly string[] TownPool =
    {
        "Town_01_RedPorch", "Town_02_BlueBanner", "Town_03_Thatch", "Town_04_RedBalcony", "Town_05_BlueStair",
        "Town_06_GreenPorch", "Town_07_RedFlared", "Town_08_StoneTower", "Pack_Townhouse",
    };
    private static readonly string[] CottagePool =
    {
        "Cottage_01_BlueSlate", "Cottage_02_Thatch", "Cottage_03_Vines", "Cottage_04_Woodshed", "Cottage_05_Veranda",
        "Cottage_06_Laundry", "Cottage_07_Sunflower", "Cottage_08_BluePorch", "Pack_Cottage",
    };
    private static readonly string[] NeighbourPool =
    {
        "Cottage_01_BlueSlate", "Cottage_02_Thatch", "Cottage_03_Vines", "Cottage_04_Woodshed", "Cottage_05_Veranda",
        "Cottage_06_Laundry", "Cottage_07_Sunflower", "Cottage_08_BluePorch", "Pack_Cottage", "Pack_Workshop",
        "Town_01_RedPorch", "Town_03_Thatch", "Town_07_RedFlared", "Town_06_GreenPorch", "Town_02_BlueBanner",
    };
    private static readonly string[] WorkshopPool = { "Pack_Workshop", "Pack_Warehouse", "Town_03_Thatch", "Cottage_04_Woodshed", "Town_07_RedFlared" };
    private static readonly string[] TreePrefabs = { "Tree_Broadleaf_Green", "Tree_Broadleaf_Green", "Tree_Broadleaf_Lime", "Tree_Broadleaf_Green", "Tree_Birch", "Tree_Broadleaf_Orange", "Tree_Broadleaf_Lime" };

    // ---- spawning ------------------------------------------------------------------------------------------------

    private static GameObject Spawn(string name, Transform parent, Vector3 localPos, float yaw, Vector3? scale = null)
    {
        var prefab = PF(name);
        if (prefab == null) return null;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = scale ?? Vector3.one;
        return go;
    }

    // Props asked for while a MeshBuilder group is being built: spawned under that group's GameObject by Place(), in
    // the same local frame the group's mesh is built in (so they move with the group, like the old merged props).
    private struct PendingProp { public string name; public Vector3 pos; public float yaw; public Vector3 scale; }
    private static readonly List<PendingProp> pendingProps = new List<PendingProp>();

    private static bool PropPrefab(string name, Vector3 pos, float yaw, Vector3 scale)
    {
        if (PF(name) == null) return false;
        pendingProps.Add(new PendingProp { name = name, pos = pos, yaw = yaw, scale = scale });
        return true;
    }

    private static bool PropPrefab(string name, Put u, float yawOffset = 0f, float scale = 1f) =>
        PropPrefab(name, u.Origin, u.yaw + yawOffset, Vector3.one * scale);

    private static void SpawnPending(Transform parent)
    {
        foreach (var p in pendingProps) Spawn(p.name, parent, p.pos, p.yaw, p.scale);
        pendingProps.Clear();
    }

    // Where a lantern post's flame ends up (the LanternLights marker), for a post at pos with the old LanternPost yaw.
    private static Vector3 LanternFlame(Vector3 pos, float yaw)
    {
        var info = Info("LanternPost");
        if (info == null || info.lanterns.Count == 0) return pos + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0.4f, 1.72f, 0f);
        return pos + Quaternion.Euler(0f, yaw + LanternPostYaw, 0f) * info.lanterns[0];
    }

    // Yaw offsets between each old procedural prop's own frame and the prefab's (+X front) frame.
    private const float BenchYaw = 90f, LanternPostYaw = 90f, WellYaw = 90f, FirewoodYaw = 90f, SignYaw = 180f, ToolRackYaw = 90f;

    // ---- buildings -----------------------------------------------------------------------------------------------

    private struct Placed
    {
        public GameObject go;
        public EnvironmentAsset info;
        public Vector3 door;
        public float depth;
    }

    // Puts a building with the middle of its wall front on frontMid, front facing along yaw (VillageBuilder's house
    // convention), and registers everything the old procedural house registered.
    private static Placed PlaceBuilding(string name, Transform parent, Vector3 frontMid, float yaw, string place, float scale = 1f, bool mirror = false)
    {
        var info = Info(name);
        var q = Quaternion.Euler(0f, yaw, 0f);
        var s = new Vector3(scale, scale, mirror ? -scale : scale);
        Vector3 anchor = Vector3.Scale(new Vector3(info.walls.max.x, 0f, info.walls.center.z), s);
        var go = Spawn(name, parent, Vector3.zero, 0f, s);
        go.transform.SetPositionAndRotation(frontMid - q * anchor, q);
        return Register(go, info, place, yaw);
    }

    private static Placed Register(GameObject go, EnvironmentAsset info, string place, float yaw)
    {
        var t = go.transform;
        var p = new Placed { go = go, info = info, door = t.TransformPoint(info.door), depth = info.Depth * Mathf.Abs(t.lossyScale.x) };
        foreach (var c in info.chimneys) chimneyTops.Add(t.TransformPoint(c));
        Vector3 centre = t.TransformPoint(info.extent.center);
        Vector3 half = Vector3.Scale(info.extent.extents, new Vector3(Mathf.Abs(t.lossyScale.x), 1f, Mathf.Abs(t.lossyScale.z)));
        obstacles.Add(Obstacle.Rect(new Vector3(centre.x, 0f, centre.z), yaw, half.x + 0.3f, half.z + 0.3f));
        occluderBoxes.Add(WallBox(go, info));
        if (place != null) cityDoors.Add((new Vector3(p.door.x, 0f, p.door.z), place));
        return p;
    }

    // The occluder: the wall block + roof (awnings, props and verandas don't hide anyone).
    private static Bounds WallBox(GameObject go, EnvironmentAsset info)
    {
        var t = go.transform;
        var w = info.walls;
        var b = new Bounds(t.TransformPoint(w.center), Vector3.zero);
        for (int i = 0; i < 8; i++)
            b.Encapsulate(t.TransformPoint(w.center + Vector3.Scale(w.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
        return b;
    }

    private static string PlaceFor(EnvironmentAsset info, string force)
    {
        if (force != null) return force;
        switch (info.style)
        {
            case "tavern": return "tavern";
            case "workshop": case "warehouse": case "mill": return "workshop";
            default: return "home";
        }
    }

    // How tall a building may stand at this spot: under the castle roofline within 32 degrees of the start view
    // (MaxFloors' rule, in metres of ridge), and the neighbourhood's storey cap near the smithy.
    private static float MaxRidge(Vector3 nearest)
    {
        Vector3 d = nearest - Seat; d.y = 0f;
        float h = Vector3.Angle(Vector3.right, d) > 32f ? 99f : Seat.y + RooflineTan * d.magnitude;
        if (nearCap)
        {
            int cap = NearCap(Vector3.Distance(new Vector3(nearest.x, 0f, nearest.z), SeatGround));
            h = Mathf.Min(h, cap <= 1 ? 7.0f : cap == 2 ? 9.8f : 99f);
        }
        return h;
    }

    // Picks a building for a slot: fits the frontage left, no deeper than allowed, under the height limit (scaled
    // down to 85% at most when nothing fits), preferring enough storeys, and never the same as the last two.
    private static string PickBuilding(System.Random rng, IList<string> pool, float room, float maxDepth, float maxRidge, int minFloors,
                                       List<string> recent, out float scale)
    {
        scale = 1f;
        var fit = new List<string>();
        var roomy = new List<string>();
        string lowest = null;
        foreach (var n in pool)
        {
            var info = Info(n);
            if (info == null || SlotWidth(info) > room || info.Depth > maxDepth) continue;
            roomy.Add(n);
            if (lowest == null || info.ridge < Info(lowest).ridge) lowest = n;
            if (info.ridge <= maxRidge) fit.Add(n);
        }
        if (fit.Count == 0)
        {
            if (lowest == null) return null;
            scale = Mathf.Clamp(maxRidge / Info(lowest).ridge, 0.85f, 1f);
            return lowest;
        }
        var tall = fit.FindAll(n => Info(n).floors >= minFloors);
        var from = tall.Count > 0 ? tall : fit;
        var fresh = from.FindAll(n => !recent.Contains(n));
        if (fresh.Count > 0) from = fresh;
        return from[rng.Next(from.Count)];
    }

    // The width a building takes in a row: its walls and eaves - side awnings, fences and trees may lean into the alley.
    private static float SlotWidth(EnvironmentAsset info) => Mathf.Max(info.walls.size.z + 0.7f, info.extent.size.z - 1.8f);

    // One side of a street lined with library buildings (the prefab version of Frontage). Returns (count, deepest).
    private static (int count, float depth) FrontagePrefabs(Transform parent, System.Random rng, Vector3 from, Vector3 to, Vector3 facing,
                                                            float depth, int minFloors, IList<string> pool, string forcePlace, string first = null)
    {
        Vector3 along = to - from; float len = along.magnitude; along /= len;
        float yaw = Quaternion.LookRotation(facing).eulerAngles.y - 90f;
        var q = Quaternion.Euler(0f, yaw, 0f);
        float zSign = Mathf.Sign(Vector3.Dot(q * Vector3.forward, along));
        var recent = new List<string>();
        int count = 0;
        float deepest = 0f, x = 0f;
        while (len - x > 3.5f)
        {
            Vector3 a = from + along * x;
            float maxR = Mathf.Min(MaxRidge(a), MaxRidge(from + along * Mathf.Min(len, x + 7f)));
            string name;
            float scale = 1f;
            if (first != null && count == 0 && Info(first) != null && SlotWidth(Info(first)) <= len - x) name = first;
            else name = PickBuilding(rng, pool, len - x, depth + 4f, maxR, minFloors, recent, out scale);
            if (name == null) break;
            var info = Info(name);
            bool mirror = name != first && rng.NextDouble() < 0.4;
            // the slot: the wall block plus its eaves, centred on the walls (side awnings may reach into the alley)
            float wc = mirror ? -info.walls.center.z : info.walls.center.z, half = SlotWidth(info) * 0.5f;
            float zMin = wc - half, zMax = wc + half;
            zMin *= scale; zMax *= scale;
            // along the street: the model's leading edge at x; across: its front reach 0.4 m into the street margin
            float rootAlong = zSign > 0 ? x - zMin : x + zMax;
            Vector3 root = from + along * rootAlong - facing * (info.extent.max.x * scale - 0.4f);
            var go = Spawn(name, parent, Vector3.zero, 0f, new Vector3(scale, scale, mirror ? -scale : scale));
            go.transform.SetPositionAndRotation(root, q);
            Register(go, info, PlaceFor(info, forcePlace), yaw);
            deepest = Mathf.Max(deepest, info.Depth * scale - 0.4f);
            recent.Add(name);
            if (recent.Count > 2) recent.RemoveAt(0);
            x += (zMax - zMin) + 0.15f + (float)rng.NextDouble() * 0.45f;   // a narrow alley between neighbours
            count++;
        }
        return (count, deepest);
    }

    // A whole block with library buildings round its edge (the prefab version of Block).
    private static int BlockPrefabs(Transform parent, System.Random rng, float x0, float x1, float z0, float z1, int minF, IList<string> pool, string firstSouth = null)
    {
        float depth = Mathf.Min(8f, Mathf.Min(x1 - x0, z1 - z0) * 0.5f);
        var north = FrontagePrefabs(parent, rng, new Vector3(x0, 0f, z1), new Vector3(x1, 0f, z1), Vector3.forward, depth, minF, pool, null);
        var south = FrontagePrefabs(parent, rng, new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z0), Vector3.back, depth, minF, pool, null, firstSouth);
        int n = north.count + south.count;
        float s0 = z0 + Mathf.Max(south.depth, 1f) + 0.4f, s1 = z1 - Mathf.Max(north.depth, 1f) - 0.4f;
        if (s1 - s0 > 4f)
        {
            float sideDepth = Mathf.Min(depth, (x1 - x0) * 0.5f);
            n += FrontagePrefabs(parent, rng, new Vector3(x0, 0f, s0), new Vector3(x0, 0f, s1), Vector3.left, sideDepth, minF, pool, null).count;
            n += FrontagePrefabs(parent, rng, new Vector3(x1, 0f, s0), new Vector3(x1, 0f, s1), Vector3.right, sideDepth, minF, pool, null).count;
        }
        return n;
    }

    // ---- the smith's own house ------------------------------------------------------------------------------------

    // Once: the blacksmith house (sheet 04) replaces the old house's visuals. The old HouseStructure / HouseDecor /
    // HouseDoor stay in the scene switched off (turn them back on by hand to revert); the End-the-Day Interactable
    // moves onto the new door leaf, standing exactly where the old door stood. Re-runs leave it alone (hand-tuning safe).
    private static void UpgradePlayerHouse()
    {
        var house = GameObject.Find("PlayerHouse");
        var info = Info("BlacksmithHouse");
        if (house == null || info == null) return;
        var existing = house.transform.Find("BlacksmithHouse");
        if (existing == null)
        {
            Transform oldDoor = house.transform.Find("HouseDoor");
            var oldUse = oldDoor != null ? oldDoor.GetComponent<Interactable>() : null;
            var go = Spawn("BlacksmithHouse", house.transform, Vector3.zero, 0f);
            go.name = "BlacksmithHouse";
            var leaf = FindDeep(go.transform, "BlacksmithHouse_Door");
            // front wall on the old front wall, the new door on the old door's line
            float frontX = Prototype1SceneBuilder.HousePosition.x + LowPoly.HouseDepth * 0.5f;
            float doorZ = Prototype1SceneBuilder.HousePosition.z + LowPoly.HouseDoorZ;
            go.transform.position = new Vector3(frontX - info.walls.max.x, 0f, 0f);
            float leafZ = leaf != null ? leaf.position.z : go.transform.TransformPoint(info.door).z;
            go.transform.position += new Vector3(0f, 0f, doorZ - leafZ);
            if (leaf != null)
            {
                var use = leaf.gameObject.AddComponent<Interactable>();
                use.kind = InteractionKind.EndDay;
                use.prompt = oldUse != null ? oldUse.prompt : "End the Day";
                use.maxViewAngle = oldUse != null ? oldUse.maxViewAngle : 32f;
            }
            foreach (var n in new[] { "HouseStructure", "HouseDecor", "HouseDoor" })
            {
                var old = house.transform.Find(n);
                if (old != null) old.gameObject.SetActive(false);
            }
            existing = go.transform;
            Debug.Log("[Village] the smith's house is now the sheet-04 blacksmith house");
        }
        var t = existing;
        Vector3 c = t.TransformPoint(info.extent.center);
        obstacles.Add(Obstacle.Rect(new Vector3(c.x, 0f, c.z), t.eulerAngles.y, info.extent.extents.x + 0.4f, info.extent.extents.z + 0.4f));
        foreach (var ch in info.chimneys) chimneyTops.Add(t.TransformPoint(ch));
        smithHouse = t;
    }

    private static Transform smithHouse;

    private static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t)
        {
            var r = FindDeep(c, name);
            if (r != null) return r;
        }
        return null;
    }

    // ---- trees ----------------------------------------------------------------------------------------------------

    // The scene's own trees (hand-placed ones included) keep exactly where and how big they stand, but wear the
    // sheet-05 broadleaf models instead of the old blocky ones. Stable per tree (picked by position), done once per
    // tree: a tree already wearing a library mesh is left alone.
    private static void UpgradeSceneTrees()
    {
        var meshes = new List<(Mesh mesh, Material[] mats)>();
        foreach (var n in TreePrefabs)
        {
            var p = PF(n);
            var mf = p != null ? p.GetComponentInChildren<MeshFilter>() : null;
            if (mf != null) meshes.Add((mf.sharedMesh, mf.GetComponent<MeshRenderer>().sharedMaterials));
        }
        if (meshes.Count == 0) return;
        var library = new HashSet<Mesh>();
        foreach (var m in meshes) library.Add(m.mesh);
        int changed = 0;
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (!root.name.StartsWith("Tree") || root.name.StartsWith("Tree_")) continue;
            var mf = root.GetComponent<MeshFilter>();
            var mr = root.GetComponent<MeshRenderer>();
            if (mf == null || mr == null || mf.sharedMesh == null || library.Contains(mf.sharedMesh)) continue;
            Vector3 pos = root.transform.position;
            int pick = Mathf.Abs(Mathf.RoundToInt(pos.x * 7.3f) * 31 + Mathf.RoundToInt(pos.z * 3.7f) * 17) % meshes.Count;
            var (mesh, mats) = meshes[pick];
            float oldH = mf.sharedMesh.bounds.size.y, newH = mesh.bounds.size.y;
            mf.sharedMesh = mesh;
            mr.sharedMaterials = mats;
            // Same height as before (and grounded: the library tree's pivot is at its foot like the old one's).
            if (oldH > 0.01f && newH > 0.01f) root.transform.localScale *= oldH / newH;
            changed++;
        }
        Debug.Log($"[Village] {changed} scene trees now wear the sheet-05 tree models");
    }

    // ---- the river ---------------------------------------------------------------------------------------------------

    // The sheet-03 watermill on the east bank: mirrored so its wheel side faces the river (west) with the front to the
    // south (toward the lane and the smithy), the wheel's axle just off the bank so its lower paddles sit in the water.
    private static bool PlaceWatermill(Transform parent, float bankEdge, float wheelZ)
    {
        var info = Info("Pack_Watermill");
        if (info == null) return false;
        const float yaw = 90f;
        var q = Quaternion.Euler(0f, yaw, 0f);
        var mirror = new Vector3(1f, 1f, -1f);
        var go = Spawn("Pack_Watermill", parent, Vector3.zero, 0f, mirror);
        go.name = "Watermill";
        Vector3 hub = Vector3.Scale(info.wheelHub, mirror);
        go.transform.SetPositionAndRotation(new Vector3(bankEdge - 0.15f, 0f, wheelZ) - q * new Vector3(hub.x, 0f, hub.z), q);
        Register(go, info, "workshop", yaw);
        if (info.lanterns.Count > 0) millLantern = go.transform.TransformPoint(info.lanterns[0]);
        // Turn the wheel with the current: its lowest paddles must move downstream (the river runs north, +Z).
        var wheel = FindDeep(go.transform, "Watermill_Wheel");
        var spin = wheel != null ? wheel.GetComponent<Spinner>() : null;
        if (spin != null)
        {
            Vector3 bottom = wheel.position + Vector3.down * 2f;
            Vector3 local = wheel.InverseTransformPoint(bottom);
            var saved = wheel.localRotation;
            wheel.Rotate(spin.axis, 5f, Space.Self);
            float dz = wheel.TransformPoint(local).z - bottom.z;
            wheel.localRotation = saved;
            spin.degreesPerSecond = Mathf.Abs(spin.degreesPerSecond) * (dz > 0f ? 1f : -1f);
        }
        return true;
    }

    // ---- nature and street clutter (sheets 05 and 06), placed where it belongs -----------------------------------------

    private static bool ClearOf(Vector3 p, float margin, bool allowRiverBank)
    {
        foreach (var o in obstacles)
        {
            if (o.isWorkshop || (allowRiverBank && o.isRiver)) continue;
            if (o.Penetration(p, margin, out _) > 0f) return false;
        }
        return true;
    }

    private static void DressWithLibrary(Transform village)
    {
        var root = new GameObject("NatureAndClutter").transform;
        root.SetParent(village, false);
        var rng = new System.Random(505);
        int n = 0;
        GameObject Put(string name, Vector3 at, float yaw, float scale, float margin, bool bank = false)
        {
            if (!ClearOf(at, margin, bank)) return null;
            var go = Spawn(name, root, at, yaw, Vector3.one * scale);
            if (go != null) { obstacles.Add(Obstacle.Circle(at, margin + 0.3f)); n++; }
            return go;
        }
        float R(float a, float b) => a + (b - a) * (float)rng.NextDouble();

        // Riverbanks through town: rocks at the water's edge, grass and ferns up the bank (never on a path or bridge).
        string[] bankRocks = { "Rock_A", "Rock_B", "Rocks_Medium", "Rock_Mossy", "Pebbles_Grey" };
        string[] bankGreen = { "Grass_Clump_A", "Grass_Clump_B", "Tree_Fern_A", "Grass_Clump_Dry", "Tree_Fern_B" };
        for (int i = 3; i < stream.Count - 1; i += 7)
        {
            if (Mathf.Abs(stream[i].z) > 90f || DeckHeightAt(stream[i]) > 0f) continue;
            Vector3 dir = (stream[i + 1] - stream[i]).normalized, side = new Vector3(dir.z, 0f, -dir.x);
            float w = streamWidth[i], s = (i / 7) % 2 == 0 ? 1f : -1f;
            if (Mathf.Abs(stream[i].z - LaneZ) < 4f || Mathf.Abs(stream[i].z - NorthLaneZ) < 4f || Mathf.Abs(stream[i].z - 41.5f) < 5f) continue;
            if (!QuayHere(stream[i].z, s > 0f)) // (a quay wall stands there)
                Put(bankRocks[(i / 7) % bankRocks.Length], stream[i] + side * s * (w * 0.5f + R(0.2f, 0.5f)), R(0f, 360f), R(0.5f, 0.9f), 0.3f, true);
            Put(bankGreen[(i / 7) % bankGreen.Length], stream[i] - side * s * (w * 0.5f + R(0.8f, 1.4f)), R(0f, 360f), R(0.9f, 1.3f), 0.2f, true);
        }

        // The greens round the smithy and at the city's edge: a stump, a fallen log, mushrooms, sticks and clover under
        // the trees - small things you only notice when you look.
        (string, Vector3)[] greens =
        {
            ("Stump", new Vector3(-11.5f, 0f, -11.0f)), ("Mushrooms", new Vector3(-11.0f, 0f, -10.2f)), ("Fallen_Log", new Vector3(-14.5f, 0f, -13.5f)),
            ("Clover_Patch", new Vector3(-10.0f, 0f, 9.5f)), ("Sticks", new Vector3(-12.2f, 0f, 8.8f)), ("Leaves_Scatter", new Vector3(9.0f, 0f, -11.5f)),
            ("Stump", new Vector3(10.2f, 0f, 21.0f)), ("Mushrooms", new Vector3(10.8f, 0f, 20.4f)), ("Rock_Mossy", new Vector3(-18.0f, 0f, -6.0f)),
            ("Flowers_Daisy", new Vector3(-13.0f, 0f, -9.0f)), ("Clover_Patch", new Vector3(7.5f, 0f, -12.5f)), ("Pebbles_Warm", new Vector3(3.0f, 0f, -15.2f)),
            ("Boulder_Large", new Vector3(-28.0f, 0f, -44.0f)), ("Standing_Stones", new Vector3(-40.0f, 0f, -38.0f)), ("DeadTree", new Vector3(-50.0f, 0f, -34.0f)),
        };
        foreach (var (name, at) in greens) Put(name, at, R(0f, 360f), R(0.85f, 1.15f), 0.4f);

        // Pines and a wheat margin along the farm track out of the south-west gate (the city's edge, toward the fields).
        string[] pines = { "Pine_Tall", "Pine_Medium", "Pine_Small", "Pine_Medium" };
        Vector3[] pineSpots = { new Vector3(-30f, 0f, -42f), new Vector3(-42f, 0f, -27f), new Vector3(-18f, 0f, -47f), new Vector3(-52f, 0f, -44f),
                                new Vector3(-9f, 0f, -45f), new Vector3(-47f, 0f, -20f), new Vector3(-56f, 0f, -30f), new Vector3(-26f, 0f, -50f) };
        for (int i = 0; i < pineSpots.Length; i++) Put(pines[i % pines.Length], pineSpots[i], R(0f, 360f), R(0.85f, 1.2f), 1.0f);
        for (int i = 0; i < 12; i++)
        {
            float t = i / 11f;
            Vector3 track = Vector3.Lerp(new Vector3(-14f, 0f, -18f), new Vector3(-44f, 0f, -44f), t);
            Vector3 off = new Vector3(0.75f, 0f, -0.66f) * (i % 2 == 0 ? 2.6f : -2.6f);
            Put(i % 3 == 0 ? "Wheat_Patch_Wide" : "Wheat_Patch", track + off, R(0f, 360f), R(0.9f, 1.2f), 0.2f);
        }

        // Street furniture: a trough on the workshop lane, a sign post where the farm lane leaves the smithy's corner,
        // a wheelbarrow by the farmsteads, a barrel and sacks by the watermill's deck.
        Put("Water_Trough", new Vector3(-14.5f, 0f, 10.2f), 0f, 1f, 0.6f);
        Put("SignPost", new Vector3(7.4f, 0f, -12.0f), 200f, 1f, 0.3f);
        Put("Wheelbarrow", new Vector3(-29.5f, 0f, -24.0f), 130f, 1f, 0.6f);
        Put("Bucket", new Vector3(-13.3f, 0f, 9.4f), 0f, 1f, 0.2f);
        Put("Cart_Wheel", new Vector3(-21.0f, 0f, -12.8f), 15f, 1f, 0.3f);
        Debug.Log($"[Village] dressed the town with {n} library nature/clutter pieces");
    }
}
