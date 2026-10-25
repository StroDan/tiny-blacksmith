using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Editor-only tool that builds the Prototype #1 scene from scratch so you
// don't have to wire everything up by hand. Run it from the menu:
// Tiny Blacksmith > Build Prototype 1 Scene
//
// Everything it creates (the placeholder blocks, the Canvas, the wiring on
// GameManager) is fully editable afterwards in the normal Inspector - this
// just saves the repetitive setup work.
public static class Prototype1SceneBuilder
{
    [MenuItem("Tiny Blacksmith/Build Prototype 1 Scene")]
    public static void BuildPrototype1Scene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateLightAndCamera();

        GameObject managerObj = new GameObject("GameManager");
        var manager = managerObj.AddComponent<ForgeGameManager>();

        // Every workplace is a generated low-poly model (see LowPoly.cs), pivot conventions are documented there.
        CreateModelObject("Floor", Vector3.zero, LowPoly.Floor());

        // Main room: the player's tent, sat facing the meadow (+X) with the forge (+Z, built below) to their
        // right and a small storage nook (-Z) to their left - see ForgeGameManager.RunMainRoom for the camera.
        CreateModelObject("TentCanopy", new Vector3(0f, 0f, -3.5f), LowPoly.TentCanopy(4f, 4f, 2.6f, 3.6f));
        CreateModelObject("MeadowGround", new Vector3(11f, 0f, -3.5f), LowPoly.Ground(new Vector2(12f, 14f), new Color(0.30f, 0.42f, 0.19f)));
        var storageDisplay = CreateStorageArea(new Vector3(0f, 0f, -7f));
        CreateHomeArea();
        var treeRng = new System.Random(1);
        for (int i = 0; i < 7; i++)
        {
            float tx = 7f + (float)treeRng.NextDouble() * 8f;
            float tz = -8f + (float)treeRng.NextDouble() * 9f;
            GameObject tree = CreateModelObject("Tree" + i, new Vector3(tx, 0f, tz), LowPoly.Tree());
            tree.transform.rotation = Quaternion.Euler(0f, (float)treeRng.NextDouble() * 360f, 0f);
            tree.transform.localScale = Vector3.one * (0.85f + (float)treeRng.NextDouble() * 0.4f);
        }

        // Real modelled furnace (Assets/Models/furnace.fbx), floor-anchored: forge.transform.position is the
        // floor point directly under it (see ForgeGameManager's ForgeTop for the coal-bed height above that).
        GameObject forge = CreateFurnaceModel(new Vector3(0f, 0f, 2.6f), 2.4f);
        var forgeStation = forge.AddComponent<ForgeStation>();

        GameObject anvil = CreateAnvilModel(new Vector3(0f, 0f, 0f), 1.0f);
        var anvilStation = anvil.AddComponent<AnvilStation>();

        // Barrel stands left of the anvil and the grindstone right of it (both a bit further from the camera); the big forge is centred behind the anvil. Barrel is scaled to 0.95 tall and centred at y=0.475, so it stands on the floor with its rim at y=0.95.
        GameObject quench = CreateBarrel(new Vector3(-1.5f, 0.475f, 0.8f), 0.95f);
        var quenchBarrel = quench.AddComponent<QuenchBarrel>();

        // Standing wheel (radius 0.45) turned 45 degrees so it faces the diagonal; the blade rests on its top edge.
        // The model's wheel axis is local Z, so transform.right (used by ForgeGameManager) is the tangent at the top.
        GameObject grindstone = CreateGrindstoneModel(new Vector3(1.6f, 0.5f, 2.0f));
        grindstone.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
        var grindstoneComp = grindstone.AddComponent<Grindstone>();

        // Workbench for fitting the handle: at the right edge of the main camera view (only a slice of it is visible; the camera swings right and up to it when the handle is made), its long side running away from the camera. The grindstone stands right of the forge, a bit closer to the camera.
        GameObject workbench = CreateModelObject("Workbench", new Vector3(3.3f, 0.4f, 0.6f), LowPoly.Workbench());
        var workbenchStation = workbench.AddComponent<WorkbenchStation>();

        // The workpiece starts as the raw ingot; ForgeGameManager swaps the mesh through the 5 forging phases.
        var ingot = LowPoly.SwordPhase(0);
        SaveMesh(ingot, "SwordPhase0");
        GameObject workpiece = new GameObject("Workpiece");
        workpiece.transform.position = new Vector3(0f, 1.05f, 0f);
        workpiece.AddComponent<MeshFilter>().sharedMesh = ingot.mesh;
        workpiece.AddComponent<MeshRenderer>();
        ApplyColor(workpiece, Color.gray, 0.55f);
        var workpieceRenderer = workpiece.GetComponent<Renderer>();

        manager.forgeStation = forgeStation;
        manager.anvilStation = anvilStation;
        manager.quenchBarrel = quenchBarrel;
        manager.grindstone = grindstoneComp;
        manager.workbenchStation = workbenchStation;
        manager.workpieceTransform = workpiece.transform;
        manager.workpieceRenderer = workpieceRenderer;
        manager.storageDisplay = storageDisplay;

        EditorSceneManager.MarkSceneDirty(scene);
        System.IO.Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Prototype1.unity");

        Debug.Log("Prototype1 scene built and saved to Assets/Scenes/Prototype1.unity. Add it to Build Settings manually if you need it in a build.");
    }

    private static void CreateLightAndCamera()
    {
        GameObject lightObj = new GameObject("Directional Light");
        var light = lightObj.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.shadows = LightShadows.Soft; // grounds the low-poly models on the floor
        lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // Tagged MainCamera: ForgeGameManager looks this up via Camera.main to
        // raycast mouse clicks against the anvil points.
        GameObject camObj = new GameObject("Main Camera");
        camObj.tag = "MainCamera";
        var cam = camObj.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.05f, 0.07f);
        camObj.transform.position = new Vector3(0f, 1.7f, -3.5f);
        camObj.transform.rotation = Quaternion.Euler(12f, 0f, 0f);
        camObj.AddComponent<AudioListener>();
    }

    // Generated model on an empty object (no collider: nothing here may block the mouse rays).
    private static GameObject CreateModelObject(string name, Vector3 pos, LowPolyModel model)
    {
        SaveMesh(model, name);
        var go = new GameObject(name);
        go.transform.position = pos;
        model.Attach(go, Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        return go;
    }

    // The wooden weapon rack + its three material bins, close enough to the camera's storage-facing view
    // (ForgeGameManager.StorageYaw) to read as the visual centrepiece rather than a distant prop. Peg/bin
    // anchors mirror LowPoly.StorageRack's own layout numbers - keep both in sync if the rack changes.
    private static StorageDisplay CreateStorageArea(Vector3 basePos)
    {
        CreateModelObject("StorageGround", basePos + new Vector3(0f, 0f, 0.4f), LowPoly.Ground(new Vector2(6.5f, 5.5f), new Color(0.34f, 0.29f, 0.23f)));

        GameObject rack = CreateModelObject("StorageRack", basePos, LowPoly.StorageRack());
        var display = rack.AddComponent<StorageDisplay>();
        display.swordPegs = new Transform[8];
        display.materialBins = new Transform[3];

        for (int row = 0; row < 2; row++)
        {
            float y = row == 0 ? LowPoly.RackUpperRailY : LowPoly.RackLowerRailY;
            for (int col = 0; col < 4; col++)
            {
                int i = row * 4 + col;
                float x = LowPoly.RackPegX(row, col);
                var peg = new GameObject("SwordPeg" + i).transform;
                peg.SetParent(rack.transform, false);
                peg.localPosition = new Vector3(x, y, 0.16f);
                display.swordPegs[i] = peg;
            }
        }

        float[] binX = { -1.15f, 0f, 1.15f };
        for (int i = 0; i < 3; i++)
        {
            Vector3 binPos = basePos + new Vector3(binX[i], 0.34f, 0.45f);
            GameObject bin = CreateModelObject("MaterialBin" + i, binPos, LowPoly.MaterialBin(ForgeMaterials.All[i].color, i));
            bin.transform.SetParent(rack.transform, true); // keep world position, follow future hand-drags of the rack
            display.materialBins[i] = bin.transform;
        }

        return display;
    }

    // The smith's life around the seat (camera at (0, 1.7, -3.5)): the house directly behind it (-X, under
    // the back edge of the canopy) with its door as the End-the-Day interactable, and the personal work table
    // straight ahead (+X, between the seat and where customers stand at x = 2.6) with the journal on it.
    // Neither has a collider, so neither can block the mouse raycasts the forging stations use.
    public static readonly Vector3 HousePosition = new Vector3(-8.4f, 0f, -3.5f);
    public static readonly Vector3 JournalTablePosition = new Vector3(1.7f, 0f, -3.5f);

    internal static void CreateHomeArea()
    {
        var house = new GameObject("PlayerHouse");
        house.transform.position = HousePosition;
        CreateModelObject("HouseStructure", HousePosition, LowPoly.HouseStructure()).transform.SetParent(house.transform, true);
        CreateModelObject("HouseDecor", HousePosition, LowPoly.HouseDecor()).transform.SetParent(house.transform, true);
        var door = CreateModelObject("HouseDoor",
            HousePosition + new Vector3(LowPoly.HouseDepth * 0.5f + 0.04f, LowPoly.HouseFoundation + LowPoly.HouseDoorHeight * 0.5f, LowPoly.HouseDoorZ),
            LowPoly.HouseDoor());
        door.transform.SetParent(house.transform, true);
        var doorUse = door.AddComponent<Interactable>();
        doorUse.kind = InteractionKind.EndDay;
        doorUse.prompt = "End the Day";

        var table = CreateModelObject("JournalTable", JournalTablePosition, LowPoly.JournalTable());
        var journal = CreateModelObject("Journal", JournalTablePosition + new Vector3(-0.1f, LowPoly.JournalTableTop, 0.02f), LowPoly.JournalBook());
        journal.transform.rotation = Quaternion.Euler(0f, -6f, 0f);
        journal.transform.SetParent(table.transform, true);
        var journalUse = journal.AddComponent<Interactable>();
        journalUse.kind = InteractionKind.OpenJournal;
        journalUse.prompt = "Open Journal";
    }

    [MenuItem("Tiny Blacksmith/Delete Save Game")]
    public static void DeleteSaveGame()
    {
        SaveSystem.Delete();
        Debug.Log("Tiny Blacksmith save deleted - the next Play starts a new game.");
    }

    // Stores the generated mesh as an asset so the scene references it instead of embedding it.
    private static void SaveMesh(LowPolyModel model, string name)
    {
        System.IO.Directory.CreateDirectory("Assets/Models/Generated");
        AssetDatabase.CreateAsset(model.mesh, "Assets/Models/Generated/" + name + ".asset");
    }

    // Grindstone model exported from the artist's Blender file (Assets/Models/Grindstone.fbx). Its raw
    // child names: Cube/Cube.001-004 are the wooden stand frame and legs; Cylinder.001/.002 are the fixed
    // bearing brackets the axle rests in; Cylinder (the big disc), Cylinder.003 (the axle rod) and
    // Cylinder.004/.005 (the two end caps) are the part that actually spins - confirmed by measuring each
    // piece's world-space bounds: those four are all thin/elongated along the same world axis, everything
    // else isn't. Reparented under a "Wheel" child (same two-piece wrapper/child split the procedural model
    // used), which the disc's own baked-in local rotation already lines up correctly with Wheel's local X
    // (verified empirically - Wheel itself needs NO corrective rotation at all; ForgeGameManager spins it
    // with grindWheel.Rotate(speed,0,0,Space.Self), local X rather than the old procedural model's Z, and
    // the two "transform.right = tangent at the top" usages there became transform.forward accordingly).
    // Wheel's own pivot is also recentred onto the disc's actual geometric centre (its raw FBX position is
    // offset from the piece group's origin) - every wheel piece's local position is shifted to compensate,
    // so nothing visibly moves but Wheel now rotates around the right point instead of making the disc
    // orbit. (Both fixes came from an earlier version of this code that span the disc around the wrong
    // point on the wrong-ish axis and visibly tumbled - confirmed by watching its world bounds change
    // shape/position through the rotation instead of staying put, which a correct spin never does. Some
    // per-step AABB size wobble is still normal even now - the disc is a low-poly faceted cylinder, not a
    // smooth one, so its axis-aligned bounding box necessarily changes a little as the facets rotate
    // through it even during a perfectly correct spin; only the CENTRE position is the reliable signal.)
    // Falls back to the procedural stand+wheel if the model isn't imported yet.
    internal static GameObject CreateGrindstoneModel(Vector3 pos)
    {
        const float wheelRadius = 0.45f; // must match Grindstone.wheelRadius's default - see ForgeGameManager
        const string path = "Assets/Models/Grindstone.fbx";
        AssetDatabase.ImportAsset(path);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (model == null)
        {
            Debug.LogWarning("Grindstone model not found at " + path + " - using the procedural stand+wheel instead.");
            var fallback = CreateModelObject("Grindstone", pos, LowPoly.GrindstoneStand());
            var wheelModel = LowPoly.GrindstoneWheel();
            SaveMesh(wheelModel, "GrindstoneWheel");
            var fallbackWheel = new GameObject("Wheel");
            fallbackWheel.transform.SetParent(fallback.transform, false);
            wheelModel.Attach(fallbackWheel, Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            return fallback;
        }

        var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
        go.name = "Grindstone";
        // Unlike the anvil/barrel/furnace (which only wrap the whole imported prefab instance and never
        // touch its internal structure), this one needs to split the prefab's own children between two
        // different parents - Unity refuses to reparent a child out of a connected prefab instance, so it
        // has to be unpacked into a plain hierarchy first.
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        foreach (var cam in go.GetComponentsInChildren<Camera>()) Object.DestroyImmediate(cam.gameObject);
        foreach (var lt in go.GetComponentsInChildren<Light>()) Object.DestroyImmediate(lt.gameObject);
        foreach (var col in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);

        var wheel = new GameObject("Wheel");
        wheel.transform.SetParent(go.transform, false); // stays at identity - no corrective rotation needed (see comment above)

        var wheelPieceNames = new HashSet<string> { "Cylinder", "Cylinder.003", "Cylinder.004", "Cylinder.005" };
        var children = new List<Transform>();
        foreach (Transform child in go.transform) if (child != wheel.transform) children.Add(child);
        foreach (var child in children)
        {
            // Wheel and go share the same (identity) rotation right now, so this reparent is a pure,
            // lossless structural move for the wheel pieces - stand pieces are left as go's direct children.
            if (wheelPieceNames.Contains(child.name)) child.SetParent(wheel.transform, true);

            Color color;
            float smoothness;
            switch (child.name)
            {
                case "Cylinder": color = new Color(0.58f, 0.55f, 0.50f); smoothness = 0.05f; break;         // stone
                case "Cylinder.003": color = new Color(0.20f, 0.21f, 0.24f); smoothness = 0.5f; break;      // axle (iron)
                case "Cylinder.004": case "Cylinder.005": color = new Color(0.66f, 0.63f, 0.58f); smoothness = 0.05f; break; // end caps
                case "Cylinder.001": case "Cylinder.002": color = new Color(0.30f, 0.28f, 0.27f); smoothness = 0.1f; break;  // bearing brackets
                default: color = new Color(0.43f, 0.28f, 0.14f); smoothness = 0.1f; break;                  // wood stand/legs
            }
            var r = child.GetComponent<Renderer>();
            if (r != null) ApplyColor(r.gameObject, color, smoothness);
        }

        // The disc's own geometric centre isn't at Wheel's pivot (its raw FBX position is offset from the
        // group's origin) - spinning around the wrong point makes it visibly orbit rather than spin in
        // place. Recentre Wheel's pivot on the disc's actual centre and shift every wheel piece's local
        // position to compensate, so nothing visibly moves but Wheel now rotates around the right point.
        Transform discTransform = wheel.transform.Find("Cylinder");
        Vector3 discCenter = discTransform.GetComponent<Renderer>().bounds.center; // wheel is still at identity, so world == go-local here
        wheel.transform.position = discCenter;
        foreach (Transform piece in wheel.transform) piece.localPosition -= discCenter;

        Bounds wheelBounds = CombinedBounds(wheel.gameObject);
        // The disc's diameter is the average of the two larger dimensions of its (low-poly, so not quite
        // perfectly equal) round cross-section - whichever axis is thinnest is the spin axis (local X).
        float[] dims = { wheelBounds.size.x, wheelBounds.size.y, wheelBounds.size.z };
        System.Array.Sort(dims);
        float currentDiameter = (dims[1] + dims[2]) * 0.5f;
        go.transform.localScale *= (wheelRadius * 2f) / currentDiameter;

        wheelBounds = CombinedBounds(wheel.gameObject);
        go.transform.position += pos - wheelBounds.center;
        return go;
    }

    private static GameObject CreateCylinder(string name, Vector3 pos, Vector3 scale, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.position = pos;
        go.transform.localScale = scale;
        ApplyColor(go, color);
        return go;
    }

    // Anvil model exported from the artist's modelling scene (Assets/Models/Anvil Final.fbx). Like the
    // furnace, its own pivot isn't at a useful point, so it's floor-anchored the same way: wrapped in an
    // empty parent placed at `floorCenter` (X/Z centred, floor at Y), which is what AnvilStation.transform
    // (read as ForgeGameManager.AnvilPos) ends up being. The model's long axis (horn to heel) is its own
    // local Z, so it's turned 90 degrees to lie along world X, matching where the workpiece sits
    // (ForgeGameManager.AnvilWorkpiecePos - see the comment there for the measured face height).
    // Falls back to the procedural anvil if the model isn't imported yet.
    internal static GameObject CreateAnvilModel(Vector3 floorCenter, float targetHeight)
    {
        const string path = "Assets/Models/Anvil Final.fbx";
        AssetDatabase.ImportAsset(path);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (model == null)
        {
            Debug.LogWarning("Anvil model not found at " + path + " - using the procedural anvil instead.");
            return CreateModelObject("Anvil", new Vector3(floorCenter.x, floorCenter.y + 0.5f, floorCenter.z), LowPoly.Anvil());
        }

        var inner = (GameObject)PrefabUtility.InstantiatePrefab(model);
        inner.name = "AnvilModel";

        foreach (var cam in inner.GetComponentsInChildren<Camera>()) Object.DestroyImmediate(cam.gameObject);
        foreach (var lt in inner.GetComponentsInChildren<Light>()) Object.DestroyImmediate(lt.gameObject);
        foreach (var col in inner.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);

        // One uniform iron color across the whole anvil (body, horn, face and stump alike), matching the top.
        foreach (var r in inner.GetComponentsInChildren<Renderer>())
            ApplyColor(r.gameObject, new Color(0.30f, 0.31f, 0.34f), 0.3f);

        Bounds bounds = CombinedBounds(inner);
        inner.transform.localScale *= targetHeight / bounds.size.y;
        bounds = CombinedBounds(inner);

        var go = new GameObject("Anvil");
        go.transform.position = floorCenter;
        inner.transform.SetParent(go.transform, true);
        inner.transform.position += floorCenter - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        go.transform.rotation = Quaternion.Euler(0f, 90f, 0f); // horn (local +Z) now points world +X
        return go;
    }

    // Barrel model exported from the artist's Blender file (Assets/Models/Barrel 1.fbx). It is scaled to
    // targetHeight and centred on `center` whatever units the importer used. Falls back to a plain
    // cylinder if the model isn't imported yet. Colors/roughness match the Blender materials.
    internal static GameObject CreateBarrel(Vector3 center, float targetHeight)
    {
        const string path = "Assets/Models/Barrel 1.fbx";
        AssetDatabase.ImportAsset(path);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (model == null)
        {
            Debug.LogWarning("Barrel model not found at " + path + " - using a placeholder cylinder.");
            return CreateCylinder("QuenchBarrel", center, new Vector3(0.8f, 0.4f, 0.8f), new Color(0.15f, 0.35f, 0.55f));
        }

        var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
        go.name = "QuenchBarrel";

        foreach (var cam in go.GetComponentsInChildren<Camera>()) Object.DestroyImmediate(cam.gameObject);
        foreach (var lt in go.GetComponentsInChildren<Light>()) Object.DestroyImmediate(lt.gameObject);
        foreach (var col in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);

        // Barrel 1.fbx has just two meshes (no separate water surface like the old model): the wood body
        // and a combined ring of metal hoops.
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            string n = r.gameObject.name;
            if (n == "Cylinder") ApplyColor(r.gameObject, new Color(0.420f, 0.208f, 0.122f), 0.35f);  // wood body
            else ApplyColor(r.gameObject, new Color(0.212f, 0.180f, 0.086f), 0.4f);                    // hoops
        }

        Bounds bounds = CombinedBounds(go);
        go.transform.localScale *= targetHeight / bounds.size.y;
        bounds = CombinedBounds(go);
        go.transform.position += center - bounds.center;
        return go;
    }

    // Furnace model exported from the artist's modelling scene (Assets/Models/furnace.fbx). Unlike the barrel,
    // it isn't built around a sensible pivot (its own hierarchy root sits at a corner of the whole prop, not
    // its centre), so instead of moving the imported hierarchy itself, it's wrapped in an empty parent placed
    // exactly at `floorCenter` (X/Z centred, floor at Y) - that parent is what ForgeGameManager reads as
    // forgeStation.transform.position, regardless of where the artist's own pivot happened to land.
    // Falls back to the procedural furnace if the model isn't imported yet.
    private static GameObject CreateFurnaceModel(Vector3 floorCenter, float targetHeight)
    {
        const string path = "Assets/Models/furnace.fbx";
        AssetDatabase.ImportAsset(path);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (model == null)
        {
            Debug.LogWarning("Furnace model not found at " + path + " - using the procedural furnace instead.");
            return CreateModelObject("Forge", floorCenter + Vector3.up * 0.7f, LowPoly.Furnace());
        }

        var inner = (GameObject)PrefabUtility.InstantiatePrefab(model);
        inner.name = "FurnaceModel";

        // Strip the modelling scene's own camera/light and any colliders baked into the import - nothing
        // here should block the mouse raycasts the other stations rely on.
        foreach (var cam in inner.GetComponentsInChildren<Camera>()) Object.DestroyImmediate(cam.gameObject);
        foreach (var lt in inner.GetComponentsInChildren<Light>()) Object.DestroyImmediate(lt.gameObject);
        foreach (var col in inner.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);

        Bounds bounds = CombinedBounds(inner);
        inner.transform.localScale *= targetHeight / bounds.size.y;
        bounds = CombinedBounds(inner);

        var go = new GameObject("Forge");
        go.transform.position = floorCenter;
        inner.transform.SetParent(go.transform, true); // keep its current world pose while reparenting
        inner.transform.position += floorCenter - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        // The model's open coal bed faces away from where it was exported facing, so it first got turned 180
        // degrees to face the player/anvil (-Z) - then another 90 degrees toward the furnace camera's left
        // (-X), on request, so the open side now faces that way instead of straight at the camera.
        go.transform.rotation = Quaternion.Euler(0f, 270f, 0f);
        return go;
    }

    private static Bounds CombinedBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        return b;
    }

    private static void ApplyColor(GameObject go, Color color, float smoothness = 0.5f)
    {
        var renderer = go.GetComponent<Renderer>();
        // URP project: the Built-in "Standard" shader renders as pink here, so use URP/Lit.
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var material = new Material(shader);
        material.color = color;
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        renderer.material = material;
    }
}
