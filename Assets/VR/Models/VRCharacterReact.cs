#if DCGO_VR
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// Character reactions (Mirei, your webcam avatar): plays one of the character's own
// clips once, then goes back to its idle loop. Character files name their clips after
// the model id (e.g. Mirei = npc012a): "_e551" is her "hit / shocked" reaction. Most
// characters number their reactions differently, so a per-character choice can be set in
// _DSTSModels/reactions.csv ("model,clip suffix", e.g. "inori,_e003"); otherwise the
// first existing clip of the default list is used.
public static class VRCharacterReact
{
    /// <summary>Clip suffixes tried in order for "my security was broken".</summary>
    public static readonly string[] HitClips = { "_e551", "_e550", "_e003", "_e004", "_e002", "_bd01" };

    static Dictionary<string, string> _overrides;

    /// <summary>The hit reaction clips for a model: its own choice from reactions.csv first.</summary>
    public static string[] HitClipsFor(string modelKey)
    {
        LoadOverrides();

        if (modelKey != null && _overrides.TryGetValue(modelKey, out string clip))
        {
            string[] list = new string[HitClips.Length + 1];
            list[0] = clip;
            HitClips.CopyTo(list, 1);
            return list;
        }

        return HitClips;
    }

    static void LoadOverrides()
    {
        if (_overrides != null)
        {
            return;
        }

        _overrides = new Dictionary<string, string>();

        try
        {
            string root = VRModelLibrary.Root;
            string path = root != null ? Path.Combine(root, "reactions.csv") : null;

            if (path == null)
            {
                return;
            }

            if (!File.Exists(path))
            {
                // Starter file the player can edit: one line per character.
                File.WriteAllText(path, "# model,clip suffix played when that character's security is broken\nmirei,_e551\n");
            }

            foreach (string line in File.ReadAllLines(path))
            {
                string trimmed = line.Trim();

                if (trimmed.Length == 0 || trimmed.StartsWith("#"))
                {
                    continue;
                }

                string[] parts = trimmed.Split(',');

                if (parts.Length >= 2 && parts[1].Trim().Length > 0)
                {
                    string suffix = parts[1].Trim();
                    _overrides[parts[0].Trim().ToLower(CultureInfo.InvariantCulture)] = suffix.StartsWith("_") ? suffix : "_" + suffix;
                }
            }
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning("[DCGO VR] Could not read reactions.csv: " + exception.Message);
        }
    }

    /// <summary>Plays the first clip found from <paramref name="suffixes"/>, then returns to <paramref name="idle"/>.</summary>
    public static bool Play(Animation anim, string idle, string[] suffixes)
    {
        if (anim == null)
        {
            return false;
        }

        foreach (string suffix in suffixes)
        {
            AnimationClip clip = VRFieldModels.Clip(anim, suffix);

            if (clip == null)
            {
                continue;
            }

            anim[clip.name].wrapMode = WrapMode.Once;
            anim.CrossFade(clip.name, 0.15f);

            if (idle != null && idle != clip.name)
            {
                anim.CrossFadeQueued(idle, 0.25f, QueueMode.CompleteOthers);
            }

            return true;
        }

        return false;
    }

    /// <summary>The character's idle loop (standing "_fn01", or Digimon "_bn01").</summary>
    public static string FindIdle(Animation anim)
    {
        if (anim == null)
        {
            return null;
        }

        foreach (AnimationState state in anim)
        {
            if (state.name.Contains("_fn01") || state.name.Contains("_bn01"))
            {
                return state.name;
            }
        }

        return null;
    }
}
#endif
