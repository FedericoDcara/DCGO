using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class MatchHistoryStore
{
    public const int MaxEntries = 50;
    public const string FileExtension = ".dcgoreplay";

    static string RootDir => Path.Combine(Application.persistentDataPath, "MatchHistory");
    static string IndexPath => Path.Combine(RootDir, "index.json");

    public static string GetRootDir()
    {
        EnsureDir();
        return RootDir;
    }

    static void EnsureDir()
    {
        if (!Directory.Exists(RootDir))
        {
            Directory.CreateDirectory(RootDir);
        }
    }

    public static MatchHistoryIndex LoadIndex()
    {
        EnsureDir();
        if (!File.Exists(IndexPath))
        {
            return new MatchHistoryIndex();
        }

        try
        {
            string json = File.ReadAllText(IndexPath);
            var index = JsonUtility.FromJson<MatchHistoryIndex>(json);
            if (index == null || index.entries == null)
            {
                return new MatchHistoryIndex();
            }

            return index;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Replay] Failed to load history index: {ex.Message}");
            return new MatchHistoryIndex();
        }
    }

    static void SaveIndex(MatchHistoryIndex index)
    {
        EnsureDir();
        if (index.entries == null)
        {
            index.entries = new List<MatchHistoryEntry>();
        }

        string json = JsonUtility.ToJson(index, true);
        File.WriteAllText(IndexPath, json);
    }

    public static string SaveReplay(ReplayData data)
    {
        if (data == null || !data.IsValid())
        {
            Debug.LogWarning("[Replay] SaveReplay skipped — invalid data.");
            return null;
        }

        if (!string.IsNullOrEmpty(data.randomSeedText) || data.randomSeed != 0)
        {
            // ok
        }
        else
        {
            Debug.LogWarning("[Replay] Saving replay with no RNG seed — file will freeze on playback.");
        }

        EnsureDir();
        string fileName = data.id + FileExtension;
        string path = Path.Combine(RootDir, fileName);
        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(path, json);

        var index = LoadIndex();
        index.entries.RemoveAll(e => e != null && e.id == data.id);
        index.entries.Insert(0, MatchHistoryEntry.FromReplay(data, fileName));

        while (index.entries.Count > MaxEntries)
        {
            var oldest = index.entries[index.entries.Count - 1];
            index.entries.RemoveAt(index.entries.Count - 1);
            if (oldest != null && !string.IsNullOrEmpty(oldest.fileName))
            {
                TryDeleteFile(Path.Combine(RootDir, oldest.fileName));
            }
        }

        SaveIndex(index);
        Debug.Log($"[Replay] Saved {path}");
        return path;
    }

    public static ReplayData LoadReplay(string idOrFileName)
    {
        EnsureDir();
        string fileName = idOrFileName;
        if (!fileName.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase))
        {
            fileName = idOrFileName + FileExtension;
        }

        string path = Path.Combine(RootDir, fileName);
        if (!File.Exists(path))
        {
            var index = LoadIndex();
            var entry = index.entries.Find(e => e != null && (e.id == idOrFileName || e.fileName == idOrFileName));
            if (entry != null)
            {
                path = Path.Combine(RootDir, entry.fileName);
            }
        }

        if (!File.Exists(path))
        {
            Debug.LogWarning($"[Replay] File not found: {path}");
            return null;
        }

        try
        {
            string json = File.ReadAllText(path);
            var data = JsonUtility.FromJson<ReplayData>(json);
            if (data == null || !data.IsValid())
            {
                Debug.LogWarning("[Replay] Invalid replay file.");
                return null;
            }

            if (data.events == null)
            {
                data.events = new List<ReplayEventDto>();
            }

            return data;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Replay] Load failed: {ex.Message}");
            return null;
        }
    }

    public static bool DeleteReplay(string id)
    {
        var index = LoadIndex();
        var entry = index.entries.Find(e => e != null && e.id == id);
        if (entry == null)
        {
            return false;
        }

        index.entries.Remove(entry);
        SaveIndex(index);
        if (!string.IsNullOrEmpty(entry.fileName))
        {
            TryDeleteFile(Path.Combine(RootDir, entry.fileName));
        }

        return true;
    }

    public static string ExportReplay(string id, string destinationPath)
    {
        var data = LoadReplay(id);
        if (data == null)
        {
            return null;
        }

        try
        {
            string dest = destinationPath;
            if (Directory.Exists(dest))
            {
                dest = Path.Combine(dest, data.id + FileExtension);
            }
            else if (!dest.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase))
            {
                dest += FileExtension;
            }

            string dir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(dest, JsonUtility.ToJson(data, true));
            return dest;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Replay] Export failed: {ex.Message}");
            return null;
        }
    }

    public static ReplayData ImportReplay(string sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
        {
            Debug.LogWarning("[Replay] Import path missing.");
            return null;
        }

        try
        {
            string json = File.ReadAllText(sourcePath);
            var data = JsonUtility.FromJson<ReplayData>(json);
            if (data == null || !data.IsValid())
            {
                Debug.LogWarning("[Replay] Import rejected — invalid or unsupported version.");
                return null;
            }

            if (string.IsNullOrEmpty(data.id))
            {
                data.id = Guid.NewGuid().ToString("N");
            }

            SaveReplay(data);
            return data;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Replay] Import failed: {ex.Message}");
            return null;
        }
    }

    static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Replay] Delete file failed: {ex.Message}");
        }
    }
}
