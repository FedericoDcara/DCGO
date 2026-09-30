using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds a 64-bit Windows standalone player.
/// Batchmode: -executeMethod WindowsStandaloneBuild.BuildPlayer
/// </summary>
public static class WindowsStandaloneBuild
{
    const string DefaultOutputDir = "Builds/Windows";
    const string ExeName = "DCGO.exe";

    [MenuItem("DCGO/Build/Windows 64-bit Player")]
    public static void BuildPlayerMenu()
    {
        string message = BuildPlayerInternal();
        if (string.IsNullOrEmpty(message))
            EditorUtility.DisplayDialog("Windows Build", $"Player written to {Path.Combine(DefaultOutputDir, ExeName)}", "OK");
        else
            EditorUtility.DisplayDialog("Windows Build Failed", message, "OK");
    }

    public static void BuildPlayer()
    {
        string error = BuildPlayerInternal();
        if (!string.IsNullOrEmpty(error))
        {
            Debug.LogError(error);
            EditorApplication.Exit(1);
            return;
        }

        EditorApplication.Exit(0);
    }

    static string BuildPlayerInternal()
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
            return "Windows Standalone build support is not installed for this Unity editor.";

        string outputDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", DefaultOutputDir));
        Directory.CreateDirectory(outputDir);
        string exePath = Path.Combine(outputDir, ExeName);

        string[] scenes = GetEnabledScenes();
        if (scenes.Length == 0)
            return "No enabled scenes in Build Settings.";

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = exePath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.CompressWithLz4HC
        };

        // Mono: no Visual Studio C++ toolchain is installed, so IL2CPP cannot compile a Windows player.
        var previousBackend = PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone);
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);

        UnityEditor.Build.Reporting.BuildReport report;
        try
        {
            Debug.Log($"[WindowsStandaloneBuild] Starting Windows 64-bit Mono build ÔåÆ {exePath}");
            report = BuildPipeline.BuildPlayer(options);
        }
        finally
        {
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, previousBackend);
        }

        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            return "Build failed: " + report.summary.result + " (" + report.summary.totalErrors + " errors).";
        }

        CopyPcRuntimeTextures(outputDir);
        CopyPcRuntimeMatchMusic(outputDir);

        Debug.Log($"[WindowsStandaloneBuild] Success: {exePath} ({report.summary.totalSize} bytes)");
        return null;
    }

    /// <summary>
    /// Windows players load playmats/backgrounds/card backs from Builds/Assets/Textures
    /// (sibling of Builds/Windows), not from DCGO_Data/StreamingAssets.
    /// </summary>
    static void CopyPcRuntimeTextures(string windowsOutputDir)
    {
        string source = Path.Combine(Application.streamingAssetsPath, "Textures");
        if (!Directory.Exists(source))
            return;

        string dest = Path.GetFullPath(Path.Combine(windowsOutputDir, "..", "Assets", "Textures"));
        Directory.CreateDirectory(dest);
        CopyDirectorySkippingMeta(source, dest);
        Debug.Log($"[WindowsStandaloneBuild] Copied runtime textures to {dest}");
    }

    /// <summary>
    /// Custom reactive music drop-in next to DCGO.exe:
    /// Windows/Assets/Audio/MatchMusic (not the parent Builds/Assets folder).
    /// </summary>
    static void CopyPcRuntimeMatchMusic(string windowsOutputDir)
    {
        string source = Path.Combine(Application.streamingAssetsPath, "Audio", "MatchMusic");
        if (!Directory.Exists(source))
            return;

        string dest = Path.GetFullPath(Path.Combine(windowsOutputDir, "Assets", "Audio", "MatchMusic"));
        Directory.CreateDirectory(dest);
        CopyDirectorySkippingMeta(source, dest);
        Debug.Log($"[WindowsStandaloneBuild] Copied MatchMusic drop-in to {dest}");
    }

    static void CopyDirectorySkippingMeta(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            if (file.EndsWith(".meta", System.StringComparison.OrdinalIgnoreCase))
                continue;
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
        }

        foreach (string dir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectorySkippingMeta(dir, Path.Combine(destDir, Path.GetFileName(dir)));
        }
    }

    static string[] GetEnabledScenes()
    {
        var list = new System.Collections.Generic.List<string>();
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            if (scene.enabled && !string.IsNullOrEmpty(scene.path))
                list.Add(scene.path);
        }
        return list.ToArray();
    }
}
