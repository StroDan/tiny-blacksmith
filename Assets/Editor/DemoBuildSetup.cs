using System.IO;
using UnityEditor;
using UnityEngine;

// Demo build readiness (menu: Tiny Blacksmith > Prepare Demo Build; headless: -executeMethod DemoBuildSetup.Run -quit).
//  - The particle effects (forge embers and smoke, sparks, steam, rain/snow, menu chimney smoke) create their
//    materials at runtime with Shader.Find("Universal Render Pipeline/Particles/Unlit"). In a player build a
//    shader only exists if some shipped asset uses it - nothing did, so a build would have lost them. Two
//    material assets in Resources (always shipped), one opaque and one alpha-blended like the effects use,
//    keep the shader and both of its variants in every build.
//  - Checks the scene order: MainMenu first, then the workshop.
public static class DemoBuildSetup
{
    private const string Dir = "Assets/Resources";
    private const string ParticleShader = "Universal Render Pipeline/Particles/Unlit";

    [MenuItem("Tiny Blacksmith/Prepare Demo Build")]
    public static void Run()
    {
        var shader = Shader.Find(ParticleShader);
        if (shader == null) { Debug.LogError($"[DEMO] shader '{ParticleShader}' not found"); return; }
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets", "Resources");

        MakeMaterial("ParticlesUnlitOpaque", shader, false);
        MakeMaterial("ParticlesUnlitTransparent", shader, true);
        AssetDatabase.SaveAssets();

        var scenes = EditorBuildSettings.scenes;
        bool order = scenes.Length >= 2 && scenes[0].path.EndsWith("MainMenu.unity") && scenes[1].path.EndsWith("Prototype1.unity") && scenes[0].enabled && scenes[1].enabled;
        Debug.Log($"[DEMO] particle materials in {Dir}; build scenes: {string.Join(", ", System.Array.ConvertAll(scenes, s => s.path))} ({(order ? "ok" : "WRONG ORDER - run Build Main Menu Scene")})");
    }

    private static void MakeMaterial(string name, Shader shader, bool transparent)
    {
        string path = $"{Dir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = shader;
        mat.color = Color.white;
        if (transparent)
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        EditorUtility.SetDirty(mat);
    }
}
