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
