using UnityEditor;
using UnityEditor.SceneManagement;

// The Editor's Play button always plays the scene that's open - not scene 0 from Build Settings - so with the
// workshop open you'd skip the main menu. This makes Play start from MainMenu no matter which scene is open,
// like the real game. Toggle it off with Tiny Blacksmith > Play From Main Menu to test the workshop directly.
// Never applies in -batchmode (the headless tests open and play their own scenes).
[InitializeOnLoad]
public static class PlayFromMainMenu
{
    private const string MenuScene = "Assets/Scenes/MainMenu.unity";
    private const string MenuItem = "Tiny Blacksmith/Play From Main Menu";
    private const string PrefKey = "TinyBlacksmith.PlayFromMainMenu";

    static PlayFromMainMenu() => EditorApplication.delayCall += Apply;

    private static bool Enabled
    {
        get => EditorPrefs.GetBool(PrefKey, true);
        set => EditorPrefs.SetBool(PrefKey, value);
    }

    private static void Apply()
    {
        if (UnityEngine.Application.isBatchMode) { EditorSceneManager.playModeStartScene = null; return; }
        EditorSceneManager.playModeStartScene = Enabled ? AssetDatabase.LoadAssetAtPath<SceneAsset>(MenuScene) : null;
    }

    [MenuItem(MenuItem)]
    private static void Toggle()
    {
        Enabled = !Enabled;
        Apply();
    }

    [MenuItem(MenuItem, true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(MenuItem, Enabled);
        return true;
    }
}
