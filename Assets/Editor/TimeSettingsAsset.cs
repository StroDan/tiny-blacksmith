using System.IO;
using UnityEditor;
using UnityEngine;

// Makes Assets/Resources/TimeSettings.asset from the code defaults (TimeSettings), so the clock's numbers - the shape
// of the day, work durations, customer hours, upgrades - can be tuned in the Inspector. Never overwrites an existing
// asset. Headless: Unity.exe -batchmode -quit -projectPath <p> -executeMethod TimeSettingsAsset.Create
public static class TimeSettingsAsset
{
    public const string AssetPath = "Assets/Resources/TimeSettings.asset";

    [MenuItem("Tiny Blacksmith/Create Time Settings Asset")]
    public static void Create()
    {
        var existing = AssetDatabase.LoadAssetAtPath<TimeSettings>(AssetPath);
        if (existing != null)
        {
            Debug.Log($"TimeSettingsAsset: {AssetPath} already exists - select it to tune the clock.");
            Selection.activeObject = existing;
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
        var settings = ScriptableObject.CreateInstance<TimeSettings>();
        AssetDatabase.CreateAsset(settings, AssetPath);
        AssetDatabase.SaveAssets();
        Selection.activeObject = settings;
        Debug.Log($"TimeSettingsAsset: created {AssetPath}.");
    }
}
