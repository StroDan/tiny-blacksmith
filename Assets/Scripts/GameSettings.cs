using System.Collections.Generic;
using UnityEngine;

// Player options that outlive the game: three volumes, fullscreen and the resolution. Stored in PlayerPrefs (the
// registry on Windows) and applied once at start-up, before the first scene loads, so they hold whichever scene
// the game starts in. The main menu's Settings screen is the only thing that changes them.
//   Master - AudioListener.volume, so it scales every sound in the game.
//   SFX    - GameAudio's sounds, loops and city ambience, and the menu's button clicks.
//   Music  - the music tracks, if any are present (Resources/Audio/music_menu and music_game - none ship yet,
//            so the setting is stored and ready for them).
//   Fullscreen / resolution - only applied in a build; in the Editor the Game view decides its own size.
public static class GameSettings
{
    private const string KeyMaster = "tb.volume.master";
    private const string KeyMusic = "tb.volume.music";
    private const string KeySfx = "tb.volume.sfx";
    private const string KeyFullscreen = "tb.screen.fullscreen";
    private const string KeyWidth = "tb.screen.width";
    private const string KeyHeight = "tb.screen.height";

    public const float DefaultMaster = 1f, DefaultMusic = 0.7f, DefaultSfx = 0.9f;

    public static float MasterVolume { get; private set; } = DefaultMaster;
    public static float MusicVolume { get; private set; } = DefaultMusic;
    public static float SfxVolume { get; private set; } = DefaultSfx;

    // Raised after any change, so live audio sources can follow a slider as it moves.
    public static event System.Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Load()
    {
        MasterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(KeyMaster, DefaultMaster));
        MusicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(KeyMusic, DefaultMusic));
        SfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(KeySfx, DefaultSfx));
        AudioListener.volume = MasterVolume;

        // Unity's player remembers its own window too; this only steps in if the stored choice is still valid on
        // this monitor (a resolution the display no longer offers is ignored rather than forced).
        if (!Application.isEditor && PlayerPrefs.HasKey(KeyFullscreen))
        {
            bool full = PlayerPrefs.GetInt(KeyFullscreen) == 1;
            var res = new Vector2Int(PlayerPrefs.GetInt(KeyWidth, Screen.width), PlayerPrefs.GetInt(KeyHeight, Screen.height));
            if (!Resolutions().Contains(res)) res = CurrentResolution;
            if (full != Fullscreen || res != CurrentResolution) Screen.SetResolution(res.x, res.y, full ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
        }
    }

    public static void SetMasterVolume(float v) { MasterVolume = Mathf.Clamp01(v); AudioListener.volume = MasterVolume; Store(KeyMaster, MasterVolume); }
    public static void SetMusicVolume(float v) { MusicVolume = Mathf.Clamp01(v); Store(KeyMusic, MusicVolume); }
    public static void SetSfxVolume(float v) { SfxVolume = Mathf.Clamp01(v); Store(KeySfx, SfxVolume); }

    private static void Store(string key, float v)
    {
        PlayerPrefs.SetFloat(key, v);
        Changed?.Invoke();
    }

    // Writes PlayerPrefs to disk (the menu calls it when a slider is let go / Settings is closed, not every frame).
    public static void Flush() => PlayerPrefs.Save();

    // ---- screen -------------------------------------------------------------------------------------

    // Can this build change its window? (Not in the Editor, where the Game view owns the size.)
    public static bool ScreenChangesApply => !Application.isEditor;

    public static bool Fullscreen => Screen.fullScreenMode != FullScreenMode.Windowed;
    public static Vector2Int CurrentResolution => new Vector2Int(Screen.width, Screen.height);

    public static void SetFullscreen(bool on)
    {
        PlayerPrefs.SetInt(KeyFullscreen, on ? 1 : 0);
        var res = CurrentResolution;
        PlayerPrefs.SetInt(KeyWidth, res.x);
        PlayerPrefs.SetInt(KeyHeight, res.y);
        PlayerPrefs.Save();
        if (ScreenChangesApply) Screen.SetResolution(res.x, res.y, on ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
        Changed?.Invoke();
    }

    public static void SetResolution(Vector2Int res)
    {
        PlayerPrefs.SetInt(KeyFullscreen, Fullscreen ? 1 : 0);
        PlayerPrefs.SetInt(KeyWidth, res.x);
        PlayerPrefs.SetInt(KeyHeight, res.y);
        PlayerPrefs.Save();
        if (ScreenChangesApply) Screen.SetResolution(res.x, res.y, Screen.fullScreenMode);
        Changed?.Invoke();
    }

    // The display's resolutions (each size once, whatever its refresh rates), smallest first, never anything
    // smaller than the menu was laid out for, and always including the size the game runs at right now.
    public static List<Vector2Int> Resolutions()
    {
        var list = new List<Vector2Int>();
        foreach (var r in Screen.resolutions)
        {
            var v = new Vector2Int(r.width, r.height);
            if (v.x >= 1024 && v.y >= 576 && !list.Contains(v)) list.Add(v);
        }
        var current = CurrentResolution;
        if (!list.Contains(current)) list.Add(current);
        list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        return list;
    }
}
