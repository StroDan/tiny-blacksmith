using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// The Blender-built environment library (sources: BlenderSource/, built by BlenderSource/scripts/build.py).
//   Assets/TinyBlacksmith/Environment/Models     FBX per asset + assets_<category>.json metadata (written by Blender)
//   Assets/TinyBlacksmith/Environment/Materials  one shared URP/Lit material per palette entry (palette.json)
//   Assets/TinyBlacksmith/Environment/Prefabs    one prefab per asset, front turned to +X (VillageBuilder's frame)
// Menu: Tiny Blacksmith > Environment > Build Asset Library (headless: EnvironmentLibrary.BuildHeadless, with -quit).
// Re-run it after rebuilding the models in Blender; VillageBuilder then places the prefabs.
public static class EnvironmentLibrary
{
    public const string Root = "Assets/TinyBlacksmith/Environment";
    public const string ModelDir = Root + "/Models";
    public const string MaterialDir = Root + "/Materials";
    public const string PrefabDir = Root + "/Prefabs";

    [System.Serializable] private class PaletteEntry { public string name; public float[] color; public float smoothness; public float emission; }
    [System.Serializable] private class Palette { public PaletteEntry[] materials; }

    [System.Serializable]
    private class Item
    {
        public string name, kind, folder, style;
        public float[] min, max, walls, door, chimneys, lanterns, glow, lights, wheel;
        public float ridge;
        public int floors, tris, lod1_tris;
    }
    [System.Serializable] private class ItemList { public Item[] assets; }

    [MenuItem("Tiny Blacksmith/Environment/Build Asset Library")]
    public static void Build()
    {
        EnsureFolder(MaterialDir);
        EnsureFolder(PrefabDir);
        int mats = BuildMaterials();
        var items = LoadItems();
        int reimported = RemapModels(items);
        int prefabs = 0;
        foreach (var it in items) if (BuildPrefab(it)) prefabs++;
        AssetDatabase.SaveAssets();
        Debug.Log($"[EnvLib] {mats} materials, {reimported} models remapped, {prefabs}/{items.Count} prefabs");
    }

    public static void BuildHeadless() => Build();

    // Library, then the village and city that place it (headless: EnvironmentLibrary.BuildAllHeadless, with -quit).
    [MenuItem("Tiny Blacksmith/Environment/Rebuild Library and Village")]
    public static void BuildAll()
    {
        Build();
        VillageBuilder.BuildInOpenScene();
    }

    public static void BuildAllHeadless()
    {
        Build();
        VillageBuilder.BuildHeadless();
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    // ---- materials ---------------------------------------------------------------------------------------------

    private static int BuildMaterials()
    {
        string json = File.ReadAllText(MaterialDir + "/palette.json");
        var pal = JsonUtility.FromJson<Palette>(json);
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        int n = 0;
        foreach (var e in pal.materials)
        {
            string path = $"{MaterialDir}/{e.name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = m == null;
            if (created) m = new Material(lit);
            // Same meaning as a LowPoly paint: an sRGB colour, a smoothness, emission = colour x multiplier. The
            // window glass keeps DayCycle's window signature, so each house's windows light up on their own evening.
            var c = new Color(e.color[0], e.color[1], e.color[2]);
            m.color = c;
            m.SetFloat("_Smoothness", e.smoothness);
            m.SetFloat("_Metallic", 0f);
            if (e.emission > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c * e.emission);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            else
            {
                m.DisableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
            }
            m.enableInstancing = true;
            if (created) AssetDatabase.CreateAsset(m, path);
            else EditorUtility.SetDirty(m);
            n++;
        }
        return n;
    }

    public static Material LibraryMaterial(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{MaterialDir}/{name}.mat");

    // ---- models ------------------------------------------------------------------------------------------------

    private static List<Item> LoadItems()
    {
        var list = new List<Item>();
        foreach (var file in Directory.GetFiles(ModelDir, "assets_*.json"))
            list.AddRange(JsonUtility.FromJson<ItemList>(File.ReadAllText(file)).assets);
        return list;
    }

    private static string ModelPath(Item it) => $"{ModelDir}/{it.folder}/{it.name}.fbx";

    // Every FBX material slot (named after a palette entry in Blender) -> the shared library material.
    private static int RemapModels(List<Item> items)
    {
        int n = 0;
        foreach (var it in items)
        {
            var importer = AssetImporter.GetAtPath(ModelPath(it)) as ModelImporter;
            if (importer == null) { Debug.LogWarning($"[EnvLib] missing model {ModelPath(it)}"); continue; }
            EnvironmentModelImporter.Configure(importer);
            bool changed = false;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(ModelPath(it)))
            {
                if (!(o is Material src)) continue;
                var lib = LibraryMaterial(src.name);
                if (lib == null) continue;
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), src.name), lib);
                changed = true;
            }
            // Materials already remapped don't show up as sub-assets any more - remap by the known palette too.
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialDir }))
            {
                var lib = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), lib.name);
                if (!importer.GetExternalObjectMap().TryGetValue(id, out var cur) || cur != lib) { importer.AddRemap(id, lib); changed = true; }
            }
            if (changed) { importer.SaveAndReimport(); n++; }
        }
        return n;
    }

    // Blender (x, y, z) -> the prefab's frame. The FBX import maps Blender to Unity as (-x, z, -y); the model is then
    // turned 90 degrees about Y so the Blender front (-Y) faces +X.
    private static Vector3 B2P(float x, float y, float z) => new Vector3(-y, z, x);
    private static Vector3 B2P(float[] a, int i = 0) => B2P(a[i], a[i + 1], a[i + 2]);

    private static List<Vector3> Points(float[] flat)
    {
        var list = new List<Vector3>();
        if (flat == null) return list;
        for (int i = 0; i + 2 < flat.Length; i += 3) list.Add(B2P(flat, i));
        return list;
    }

    private static Bounds BoxFrom(Vector3 a, Vector3 b)
    {
        var bb = new Bounds(a, Vector3.zero);
        bb.Encapsulate(b);
        return bb;
    }

    private static bool BuildPrefab(Item it)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(it));
        if (model == null) return false;
        var root = new GameObject(it.name);
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
        inst.name = "Model";
        inst.transform.SetParent(root.transform, false);
        inst.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

        var info = root.AddComponent<EnvironmentAsset>();
        info.kind = it.kind;
        info.style = it.style;
        info.ridge = it.ridge;
        info.floors = it.floors;
        info.extent = BoxFrom(B2P(it.min), B2P(it.max));
        info.walls = it.walls != null && it.walls.Length == 4
            ? BoxFrom(B2P(it.walls[0], it.walls[2], 0f), B2P(it.walls[1], it.walls[3], it.ridge))
            : info.extent;
        if (it.door != null && it.door.Length == 3) info.door = B2P(it.door);
        info.chimneys = Points(it.chimneys);
        info.lanterns = Points(it.lanterns);
        info.lanterns.AddRange(Points(it.lights));
        info.glows = Points(it.glow);
        if (it.wheel != null && it.wheel.Length == 3) info.wheelHub = B2P(it.wheel);

        // Check the axis conversion against the real imported mesh once per model (a wrong guess would put every
        // door on the wrong side of every house).
        var rs = root.GetComponentsInChildren<Renderer>();
        if (rs.Length > 0)
        {
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            if (Vector3.Distance(b.center, info.extent.center) > 0.25f || Vector3.Distance(b.size, info.extent.size) > 0.25f)
                Debug.LogWarning($"[EnvLib] {it.name}: imported bounds {b.center}/{b.size} vs metadata {info.extent.center}/{info.extent.size}");
        }

        // LODs: the full model near the seat, the plain one beyond ~50 m; never culled (the skyline must stay).
        var lod = inst.GetComponentInChildren<LODGroup>();
        if (lod != null)
        {
            var lods = lod.GetLODs();
            if (lods.Length >= 2)
            {
                lods[0].screenRelativeTransitionHeight = 0.16f;
                lods[lods.Length - 1].screenRelativeTransitionHeight = 0f;
                lod.SetLODs(lods);
                lod.RecalculateBounds();
            }
        }

        // The water wheel turns (VillageBuilder sets the direction for the river it stands in).
        var wheel = FindDeep(inst.transform, "Watermill_Wheel");
        if (wheel != null)
        {
            var spin = wheel.gameObject.AddComponent<Spinner>();
            spin.axis = Vector3.right;
            spin.degreesPerSecond = 16f;
        }

        // Simple colliders on the Ignore Raycast layer: there for physics, invisible to the stations' mouse raycasts
        // (Physics.DefaultRaycastLayers leaves that layer out) - the game never relied on building colliders.
        bool solid = it.kind == "building" || (it.kind == "prop" && info.extent.size.y > 0.6f) || it.kind == "rock";
        if (solid)
        {
            var box = root.AddComponent<BoxCollider>();
            var cb = it.kind == "building" ? info.walls : info.extent;
            box.center = cb.center;
            box.size = cb.size;
        }
        root.layer = 2;

        var flags = StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (wheel != null && (t == wheel || t.IsChildOf(wheel))) continue;
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
        }
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            r.shadowCastingMode = it.kind == "ground" ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            r.receiveShadows = true;
        }

        EnsureFolder($"{PrefabDir}/{it.folder}");
        PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{it.folder}/{it.name}.prefab");
        Object.DestroyImmediate(root);
        return true;
    }

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

    // ---- lookup for the builders ------------------------------------------------------------------------------

    private static Dictionary<string, GameObject> cache;

    public static void ClearCache() => cache = null;

    public static GameObject Prefab(string name)
    {
        if (cache == null)
        {
            cache = new Dictionary<string, GameObject>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir }))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (go != null) cache[go.name] = go;
            }
        }
        return cache.TryGetValue(name, out var p) ? p : null;
    }
}

// Import settings for the environment FBX files: metres, imported flat normals, no animation/cameras/lights, and
// materials remapped to the shared library (EnvironmentLibrary.RemapModels) instead of one copy per model.
public class EnvironmentModelImporter : AssetPostprocessor
{
    private void OnPreprocessModel()
    {
        if (!assetPath.StartsWith(EnvironmentLibrary.ModelDir)) return;
        Configure((ModelImporter)assetImporter);
    }

    public static void Configure(ModelImporter mi)
    {
        mi.globalScale = 1f;
        mi.useFileScale = true;
        mi.bakeAxisConversion = false;
        mi.importAnimation = false;
        mi.animationType = ModelImporterAnimationType.None;
        mi.importCameras = false;
        mi.importLights = false;
        mi.importBlendShapes = false;
        mi.importVisibility = false;
        mi.isReadable = false;
        mi.meshCompression = ModelImporterMeshCompression.Off;
        mi.importNormals = ModelImporterNormals.Import;
        mi.importTangents = ModelImporterTangents.None;
        mi.addCollider = false;
        mi.generateSecondaryUV = false;
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
        mi.materialName = ModelImporterMaterialName.BasedOnMaterialName;
    }
}
