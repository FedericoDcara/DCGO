#if DCGO_VR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// Finds the 3D model (.glb) for a card from the player's own model folder
// "_DSTSModels" (manifest.csv + original/<kind>/<slug>.glb). The folder is
// never part of the project or the build: it is looked up next to the game
// (or the project), so models stay personal and can be added at any time.
// Matching: Japanese card name first (identical katakana in both), then the
// English name, both normalised (case, width, spaces and punctuation).
public static class VRModelLibrary
{
    const string FolderName = "_DSTSModels";

    static bool _loaded;
    static readonly List<ModelInfo> _all = new List<ModelInfo>();

    public sealed class ModelInfo
    {
        public string Name;
        public string Path;
        public string Category;  // digimon, npc...
        public string Key => VRModelTuning.KeyOf(Path);
    }

    /// <summary>Every model in the folder (playable Digimon first, then by name).</summary>
    public static List<ModelInfo> All
    {
        get
        {
            Load();
            return _all;
        }
    }
    static string _root;
    static readonly Dictionary<string, string> _byName = new Dictionary<string, string>();
    static readonly Dictionary<string, string> _resolved = new Dictionary<string, string>();
    static readonly Dictionary<string, bool> _isCharacter = new Dictionary<string, bool>(); // path -> npc/character model

    public static bool Available
    {
        get
        {
            Load();
            return _byName.Count > 0;
        }
    }

    public static string Root
    {
        get
        {
            Load();
            return _root;
        }
    }

    /// <summary>Path of the model for this card, or null when there is none.</summary>
    public static string Resolve(CardSource card)
    {
        if (card == null)
        {
            return null;
        }

        Load();

        if (_byName.Count == 0)
        {
            return null;
        }

        // Cache by what the card is (names + type), never by CardIndex: that is a per-match
        // instance number, so it pointed new cards at an earlier match's model.
        bool character = IsCharacterCard(card);
        string key = (character ? "c:" : "d:") + card.BaseENGCardNameFromEntity + "|" + card.BaseJPNCardNameFromEntity;

        if (_resolved.TryGetValue(key, out string cached))
        {
            return cached;
        }

        string path = null;

        foreach (string name in Candidates(card))
        {
            // Tamers only take person models, Digimon only Digimon models.
            if (!string.IsNullOrEmpty(name) && _byName.TryGetValue(Normalize(name), out path)
                && _isCharacter.TryGetValue(path, out bool isCharacter) && isCharacter == character)
            {
                break;
            }

            path = null;
        }

        _resolved[key] = path;
        Debug.Log($"[DCGO VR] Model for {card.BaseENGCardNameFromEntity}: {(path != null ? Path.GetFileName(path) : "none")}");
        return path;
    }

    static bool IsCharacterCard(CardSource card)
    {
        try
        {
            return card.IsTamer;
        }
        catch (Exception)
        {
            return false;
        }
    }

    static IEnumerable<string> Candidates(CardSource card)
    {
        string jp = card.BaseJPNCardNameFromEntity;
        string en = card.BaseENGCardNameFromEntity;
        yield return jp;
        yield return en;

        // "Rosemon: Burst Mode" (card) vs "Rosemon BM" (model), "Imperialdramon: Fighter Mode"
        // vs "Imperialdramon FM", "Omnimon X" vs "Omnimon (X Antibody)"... Tried before the
        // bracket strip below, which would fall back to the plain form's model.
        foreach (string variant in ModeVariants(en))
        {
            yield return variant;
        }

        yield return StripBrackets(jp);
        yield return StripBrackets(en);

        // "BlackAgumon" style card names vs "Agumon (Black)" model names.
        if (!string.IsNullOrEmpty(en) && en.StartsWith("Black", StringComparison.OrdinalIgnoreCase) && en.Length > 5)
        {
            yield return en.Substring(5) + " (Black)";
        }
    }

    static IEnumerable<string> ModeVariants(string en)
    {
        if (string.IsNullOrEmpty(en))
        {
            yield break;
        }

        // Split "Base: Suffix", "Base (Suffix)" or "Base - Suffix".
        int cut = en.IndexOfAny(new[] { ':', '(', '（', '：' });
        int dash = en.IndexOf(" - ", StringComparison.Ordinal);

        if (cut < 0 || (dash > 0 && dash < cut))
        {
            cut = dash;
        }

        if (cut > 0)
        {
            string name = en.Substring(0, cut).Trim();
            string suffix = en.Substring(cut + (en[cut] == ' ' ? 3 : 1)).Trim(' ', ')', '）');

            if (suffix.EndsWith("Mode", StringComparison.OrdinalIgnoreCase))
            {
                // "Burst Mode" -> "BM"
                StringBuilder initials = new StringBuilder();

                foreach (string word in suffix.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    initials.Append(char.ToUpperInvariant(word[0]));
                }

                yield return name + " " + initials;
            }

            yield return name + " (" + suffix + ")";
            yield return name + ": " + suffix;
        }

        // "Omnimon X" / "Omnimon X-Antibody" -> "Omnimon (X Antibody)"
        if (en.EndsWith(" X", StringComparison.Ordinal))
        {
            yield return en.Substring(0, en.Length - 2) + " (X Antibody)";
        }
        else if (en.EndsWith("X-Antibody", StringComparison.OrdinalIgnoreCase) || en.EndsWith("X Antibody", StringComparison.OrdinalIgnoreCase))
        {
            yield return en.Substring(0, en.Length - "X-Antibody".Length).Trim(' ', '(', ':') + " (X Antibody)";
        }
    }

    static string StripBrackets(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        int open = name.IndexOfAny(new[] { '(', '（' });
        return open > 0 ? name.Substring(0, open) : name;
    }

    public static string Normalize(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "";
        }

        string folded = name.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        StringBuilder result = new StringBuilder(folded.Length);

        foreach (char c in folded)
        {
            if (char.IsLetterOrDigit(c))
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    static void Load()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        _root = FindRoot();

        if (_root == null)
        {
            Debug.Log("[DCGO VR] No _DSTSModels folder found next to the game - 3D models disabled.");
            return;
        }

        string manifest = Path.Combine(_root, "manifest.csv");

        if (!File.Exists(manifest))
        {
            Debug.LogWarning("[DCGO VR] _DSTSModels has no manifest.csv - 3D models disabled.");
            return;
        }

        int count = 0;
        List<List<string>> rows = new List<List<string>>();

        foreach (string line in File.ReadAllLines(manifest, Encoding.UTF8))
        {
            List<string> columns = SplitCsv(line);

            if (columns.Count >= 7 && columns[0] != "kind")
            {
                rows.Add(columns);
            }
        }

        // Playable Digimon win over boss / NPC variants with the same name.
        rows.Sort((a, b) => (a[5] == "digimon" ? 0 : 1).CompareTo(b[5] == "digimon" ? 0 : 1));

        foreach (List<string> columns in rows)
        {

            string file = columns[6].Replace('\\', '/');

            if (file.StartsWith("o/"))
            {
                file = "original/" + file.Substring(2);
            }

            string path = Path.Combine(_root, file);

            if (!File.Exists(path))
            {
                continue;
            }

            Add(columns[3], path);
            Add(columns[2], path);
            _isCharacter[path] = columns[5] == "npc"; // "boss" entries are Digimon too
            _all.Add(new ModelInfo { Name = columns[2], Path = path, Category = columns[5] });
            count++;
        }

        _all.Sort((a, b) =>
        {
            int c = (a.Category == "digimon" ? 0 : 1).CompareTo(b.Category == "digimon" ? 0 : 1);
            return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        Debug.Log($"[DCGO VR] 3D model library: {count} models in {_root}");
    }

    static void Add(string name, string path)
    {
        string key = Normalize(name);

        if (key.Length > 0 && !_byName.ContainsKey(key))
        {
            _byName[key] = path;
        }
    }

    static string FindRoot()
    {
        // Next to the exe / project, or a few folders up (Builds/WindowsVR -> project root).
        string start = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        DirectoryInfo dir = new DirectoryInfo(start);

        for (int i = 0; i < 4 && dir != null; i++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, FolderName);

            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    static List<string> SplitCsv(string line)
    {
        List<string> result = new List<string>();
        StringBuilder current = new StringBuilder();
        bool quoted = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (c == ',' && !quoted)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else if (c != '﻿')
            {
                current.Append(c);
            }
        }

        result.Add(current.ToString());
        return result;
    }
}
#endif
