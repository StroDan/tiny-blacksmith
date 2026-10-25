using System.IO;
using UnityEditor;
using UnityEngine;

// Development-only tools for the living world (never in the player's UI): Tiny Blacksmith > World.
//   - Print World State: seed, stage, windows, events past/coming, referrals, weapon return days, specials.
//   - Simulate Next 30 Days: plays a COPY of the current game (Play mode) or the save file (Edit mode) 30 days on with
//     the bot smith and writes the timeline to Logs/world_forecast.txt - the real game is never touched.
//   - Force Event / Race / Event Trader / Special: steer the next rolls of a game running in Play mode.
public static class WorldDebugMenu
{
    private const string Root = "Tiny Blacksmith/World/";

    private static Workshop Live()
    {
        var m = Application.isPlaying ? Object.FindAnyObjectByType<ForgeGameManager>() : null;
        return m != null ? m.workshop : null;
    }

    private static Workshop LiveOrSave()
    {
        var live = Live();
        if (live != null) return live;
        var w = new Workshop();
        if (!SaveSystem.TryLoad(w, out _)) { Debug.LogWarning("[WORLD] No game running and no save file to read."); return null; }
        return w;
    }

    [MenuItem(Root + "Print World State")]
    public static void Print()
    {
        var w = LiveOrSave();
        if (w != null) Debug.Log("[WORLD]\n" + WorldDirector.Describe(w));
    }

    [MenuItem(Root + "Simulate Next 30 Days (copy)")]
    public static void Forecast()
    {
        var src = LiveOrSave();
        if (src == null) return;
        // A deep copy through the save format, so the game itself is never touched.
        string tmp = Path.Combine(Path.GetTempPath(), "tb_forecast.json");
        string old = System.Environment.GetEnvironmentVariable("TB_SAVE_PATH");
        System.Environment.SetEnvironmentVariable("TB_SAVE_PATH", tmp);
        try
        {
            SaveSystem.Save(src);
            var copy = new Workshop();
            if (!SaveSystem.TryLoad(copy, out _)) { Debug.LogWarning("[WORLD] Couldn't copy the game."); return; }
            var run = WorldSimTest.PlayFrom("Forecast", copy, copy.day + 30);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/world_forecast.txt", WorldDirector.Describe(copy) + "\n" + string.Join("\n", run.timeline));
            Debug.Log($"[WORLD] Forecast written to Logs/world_forecast.txt ({run.timeline.Count} lines, {run.delivered} deliveries, {run.events} events).");
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
            System.Environment.SetEnvironmentVariable("TB_SAVE_PATH", old);
        }
    }

    private static void Force(CityEventType type)
    {
        var w = Live();
        if (w == null) { Debug.LogWarning("[WORLD] Enter Play mode first."); return; }
        WorldDirector.ForceEvent(w, type);
        Object.FindAnyObjectByType<ForgeGameManager>().DebugRefreshWorld();
        Debug.Log($"[WORLD] Today is now: {CityEvents.Get(type).name}");
    }

    [MenuItem(Root + "Force Event/None")] public static void ForceNone() => Force(CityEventType.None);
    [MenuItem(Root + "Force Event/Harvest Season")] public static void ForceHarvest() => Force(CityEventType.HarvestSeason);
    [MenuItem(Root + "Force Event/Bandits on the Roads")] public static void ForceBandits() => Force(CityEventType.BanditsOnRoads);
    [MenuItem(Root + "Force Event/Royal Tournament")] public static void ForceTournament() => Force(CityEventType.RoyalTournament);
    [MenuItem(Root + "Force Event/Dwarven Caravan")] public static void ForceCaravan() => Force(CityEventType.DwarvenCaravan);
    [MenuItem(Root + "Force Event/Elven Delegation")] public static void ForceDelegation() => Force(CityEventType.ElvenDelegation);
    [MenuItem(Root + "Force Event/Festival")] public static void ForceFestival() => Force(CityEventType.Festival);
    [MenuItem(Root + "Force Event/Heavy Rain")] public static void ForceRain() => Force(CityEventType.HeavyRain);
    [MenuItem(Root + "Force Event/Market Day")] public static void ForceMarket() => Force(CityEventType.MarketDay);

    private static void NextRace(Race r)
    {
        var w = Live();
        if (w == null) { Debug.LogWarning("[WORLD] Enter Play mode first."); return; }
        w.world.forceNextRace = r;
        Debug.Log($"[WORLD] The next new face will be: {r}");
    }

    [MenuItem(Root + "Force Next Race/Human")] public static void RaceHuman() => NextRace(Race.Human);
    [MenuItem(Root + "Force Next Race/Elf")] public static void RaceElf() => NextRace(Race.Elf);
    [MenuItem(Root + "Force Next Race/Dwarf")] public static void RaceDwarf() => NextRace(Race.Dwarf);

    [MenuItem(Root + "Force Event Trader Today")]
    public static void ForceTrader()
    {
        var w = Live();
        if (w == null) { Debug.LogWarning("[WORLD] Enter Play mode first."); return; }
        w.nextEventDay = w.day;
        w.world.firstEventTraderDay = w.day;
        w.world.lastSpecialDay = -100;
        Debug.Log(w.NextLockedMetal == null ? "[WORLD] Every metal is already known - she has nothing to offer." : "[WORLD] The Event Trader will come by during customer hours today.");
    }

    [MenuItem(Root + "Force Special Commission Next")]
    public static void ForceSpecial()
    {
        var w = Live();
        if (w == null) { Debug.LogWarning("[WORLD] Enter Play mode first."); return; }
        w.world.forceSpecial = true;
        Debug.Log("[WORLD] The next customer with new work will be a special commission.");
    }

    [MenuItem(Root + "Create World Settings Asset")]
    public static void CreateSettings()
    {
        const string path = "Assets/Resources/WorldSettings.asset";
        var existing = AssetDatabase.LoadAssetAtPath<WorldSettings>(path);
        if (existing != null) { Selection.activeObject = existing; return; }
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var s = ScriptableObject.CreateInstance<WorldSettings>();
        AssetDatabase.CreateAsset(s, path);
        AssetDatabase.SaveAssets();
        Selection.activeObject = s;
        Debug.Log($"[WORLD] Created {path} - it now overrides the code defaults in WorldSettings.cs.");
    }
}
