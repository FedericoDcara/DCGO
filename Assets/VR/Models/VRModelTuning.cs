#if DCGO_VR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// Per-model size corrections, tuned in game (VR settings > Tune 3D model sizes).
// Saved next to the models in _DSTSModels/tuning.csv ("file,scale"), so they stay
// with your model folder and are never committed. Falls back to PlayerPrefs.
public static class VRModelTuning
{
    public const float Min = 0.01f;
    public const float Max = 20f;

    static Dictionary<string, float> _scales;

    /// <summary>Size multiplier of the cards lying under the arena figures.</summary>
    public static float CardSize
    {
        get => PlayerPrefs.GetFloat("DCGO_VR_ArenaCardSize", 1f);
        set => PlayerPrefs.SetFloat("DCGO_VR_ArenaCardSize", Mathf.Clamp(value, 0.3f, 3f));
    }

    /// <summary>Key for a model path (its file name).</summary>
    public static string KeyOf(string path) => string.IsNullOrEmpty(path) ? null : Path.GetFileNameWithoutExtension(path);

    public static float Get(string key)
    {
        Load();
        return key != null && _scales.TryGetValue(key, out float scale) ? scale : 1f;
    }

    public static void Set(string key, float scale)
    {
        if (key == null)
        {
            return;
        }

        Load();
        _scales[key] = Mathf.Clamp(scale, Min, Max);
        Save();
    }

    /// <summary>True when this model has been sized by hand (even if kept at x1).</summary>
    public static bool Has(string key)
    {
        Load();
        return key != null && _scales.ContainsKey(key);
    }

    public static int Count
    {
        get
        {
            Load();
            return _scales.Count;
        }
    }

    /// <summary>Back to the automatic size.</summary>
    public static void Clear(string key)
    {
        Load();

        if (key != null && _scales.Remove(key))
        {
            Save();
        }
    }

    static string FilePath
    {
        get
        {
            string root = VRModelLibrary.Root;
            return root != null ? Path.Combine(root, "tuning.csv") : null;
        }
    }

    static void Load()
    {
        if (_scales != null)
        {
            return;
        }

        _scales = new Dictionary<string, float>();

        try
        {
            string path = FilePath;
            string text = path != null && File.Exists(path) ? File.ReadAllText(path) : PlayerPrefs.GetString("DCGO_VR_ModelTuning", "");

            foreach (string line in text.Split('\n'))
            {
                string[] parts = line.Trim().Split(',');

                if (parts.Length == 2 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float scale))
                {
                    _scales[parts[0]] = Mathf.Clamp(scale, Min, Max);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[DCGO VR] Could not read model tuning: " + e.Message);
        }
    }

    static void Save()
    {
        StringBuilder text = new StringBuilder();

        foreach (KeyValuePair<string, float> pair in _scales)
        {
            text.Append(pair.Key).Append(',').Append(pair.Value.ToString("0.###", CultureInfo.InvariantCulture)).Append('\n');
        }

        try
        {
            string path = FilePath;

            if (path != null)
            {
                File.WriteAllText(path, text.ToString());
            }
            else
            {
                PlayerPrefs.SetString("DCGO_VR_ModelTuning", text.ToString());
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[DCGO VR] Could not save model tuning: " + e.Message);
            PlayerPrefs.SetString("DCGO_VR_ModelTuning", text.ToString());
        }
    }
}
#endif
