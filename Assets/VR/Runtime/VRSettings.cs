#if DCGO_VR
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// Player preferences for the VR room, stored in PlayerPrefs (per machine).
public static class VRSettings
{
    public static bool HideScreenInDuel { get => Get("HideScreen", false); set => Set("HideScreen", value); }
    public static bool Haptics { get => Get("Haptics", true); set => Set("Haptics", value); }
    public static bool HandFloating { get => Get("HandFloating", false); set => Set("HandFloating", value); }
    public static bool CardPreview { get => Get("Preview", true); set => Set("Preview", value); }
    public static bool Models { get => Get("Models", true); set => Set("Models", value); }
    /// <summary>Duel Disk view: draw the field hologram (cards lying on the board). Off = arena + disk only.</summary>
    public static bool HoloBoard { get => Get("HoloBoard", false); set => Set("HoloBoard", value); }

    /// <summary>3D Digimon size multiplier.</summary>
    public static float ModelScale
    {
        get => PlayerPrefs.GetFloat("DCGO_VR_ModelScale", 1f);
        set => PlayerPrefs.SetFloat("DCGO_VR_ModelScale", value);
    }

    /// <summary>Distance from the seat to the middle of the duel arena (metres).</summary>
    public static float ArenaDistance
    {
        get => PlayerPrefs.GetFloat("DCGO_VR_ArenaDistance", 2.4f);
        set => PlayerPrefs.SetFloat("DCGO_VR_ArenaDistance", value);
    }

    /// <summary>Overview: look down on the arena from above (default). Off: the arena is at ground level.</summary>
    public static bool ArenaOverview { get => Get("ArenaOverview2", true); set => Set("ArenaOverview2", value); } // new key: everyone starts in overview

    /// <summary>Memory gauge fixed at the top of the view in duels.</summary>
    public static bool MemoryHud { get => Get("MemoryHud", true); set => Set("MemoryHud", value); }

    /// <summary>Opponent shown as a 3D person behind their field.</summary>
    public static bool OpponentAvatar { get => Get("OpponentAvatar", true); set => Set("OpponentAvatar", value); }

    /// <summary>Security check shown as a side-view battle scene in front of you.</summary>
    public static bool SecurityBattle { get => Get("SecurityBattle", true); set => Set("SecurityBattle", value); }

    /// <summary>Character model used as your avatar (HUD webcam): a model key from _DSTSModels ("npc" models).</summary>
    public static string MyAvatar
    {
        get => PlayerPrefs.GetString("DCGO_VR_MyAvatar", "inori");
        set => PlayerPrefs.SetString("DCGO_VR_MyAvatar", value);
    }

    /// <summary>Webcam portraits of both players beside the memory gauge.</summary>
    public static bool AvatarCams { get => Get("AvatarCams", true); set => Set("AvatarCams", value); }

    public static bool PerfStats { get => Get("PerfStats", false); set => Set("PerfStats", value); }
    /// <summary>Full Debug.Log output with stack traces (off = faster: only warnings / errors, no stack traces).</summary>
    public static bool VerboseLogs { get => Get("VerboseLogs", false); set => Set("VerboseLogs", value); }

    /// <summary>Headset render resolution in percent (eye texture scale).</summary>
    public static int RenderScale
    {
        get => PlayerPrefs.GetInt("DCGO_VR_RenderScale", 100);
        set => PlayerPrefs.SetInt("DCGO_VR_RenderScale", value);
    }

    static bool Get(string key, bool fallback)
    {
        try
        {
            return PlayerPrefs.GetInt("DCGO_VR_" + key, fallback ? 1 : 0) != 0;
        }
        catch
        {
            return fallback;
        }
    }

    static void Set(string key, bool value)
    {
        try
        {
            PlayerPrefs.SetInt("DCGO_VR_" + key, value ? 1 : 0);
        }
        catch
        {
            // Not critical.
        }
    }
}
#endif
