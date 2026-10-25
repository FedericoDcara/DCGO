using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

// === DCGO-CUSTOM:vr ===
// DCGO > VR > Build PC VR ...
// Builds the Windows player with the VR mode (DCGO_VR) into Builds/WindowsVR:
//   DCGO.exe               starts in VR when a headset is connected (Air Link,
//                          Quest Link, SteamVR), otherwise flat
//   Play flat (no VR).bat  forces the flat game (-novr)
//   Assets/                the card images / decks / audio folder the game
//                          reads next to the exe (copied from Builds/Assets)
// The development variant adds the profiler connection and dev logs.
public static class VRBuild
{
    const string Define = "DCGO_VR";

    static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

    [MenuItem("DCGO/VR/Build PC VR (Windows)")]
    public static void BuildRelease() => Build(false);

    [MenuItem("DCGO/VR/Build PC VR (Development + Profiler)")]
    public static void BuildDevelopment() => Build(true);

    static void Build(bool development)
    {
        string folder = Path.Combine(ProjectRoot, "Builds", development ? "WindowsVR-Dev" : "WindowsVR");
        string exe = Path.Combine(folder, "DCGO.exe");

        EnsureDefine(BuildTargetGroup.Standalone);

        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

        if (scenes.Length == 0)
        {
            EditorUtility.DisplayDialog("DCGO VR", "No scenes are enabled in File > Build Settings.", "OK");
            return;
        }

        BuildOptions options = BuildOptions.None;

        if (development)
        {
            options |= BuildOptions.Development | BuildOptions.ConnectWithProfiler | BuildOptions.AllowDebugging;
        }

        Debug.Log($"[DCGO VR] Build: starting {(development ? "development" : "release")} PC VR build -> {exe} ({scenes.Length} scenes)");
        Stopwatch watch = Stopwatch.StartNew();

        // Mono, like the existing flat Windows build: IL2CPP needs the Visual Studio
        // C++ build tools, which are not installed here. The project setting is restored after.
        ScriptingImplementation previousBackend = PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone);
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);

        // Incremental GC spreads garbage collection over several frames instead of
        // one long pause: fewer hitches in the headset. Restored after the build.
        bool previousIncremental = PlayerSettings.gcIncremental;
        PlayerSettings.gcIncremental = true;
        BuildReport report;

        try
        {
            report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = exe,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = options,
            });
        }
        finally
        {
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, previousBackend);
            PlayerSettings.gcIncremental = previousIncremental;
        }

        BuildSummary summary = report.summary;
        string result = $"{summary.result} in {watch.Elapsed.TotalMinutes:0.0} min, {summary.totalSize / (1024f * 1024f):0} MB, {summary.totalErrors} errors, {summary.totalWarnings} warnings";
        Debug.Log("[DCGO VR] Build: " + result);

        if (summary.result != BuildResult.Succeeded)
        {
            foreach (BuildStep step in report.steps)
            {
                foreach (BuildStepMessage message in step.messages)
                {
                    if (message.type == LogType.Error || message.type == LogType.Exception)
                    {
                        Debug.Log("[DCGO VR] Build error: " + message.content);
                    }
                }
            }

            EditorUtility.DisplayDialog("DCGO VR build", "Build failed: " + result + "\nSee the Console for the errors.", "OK");
            return;
        }

        int copied = CopyGameData(folder);
        File.WriteAllText(Path.Combine(folder, "Play flat (no VR).bat"), "@echo off\r\nstart \"\" \"%~dp0DCGO.exe\" -novr\r\n");
        File.WriteAllText(Path.Combine(folder, "README-VR.txt"),
            "DCGO VR\r\n\r\n" +
            "1. Connect the headset first (Air Link / Quest Link in the Meta app, or SteamVR).\r\n" +
            "2. Start DCGO.exe. Without a headset it starts flat.\r\n" +
            "3. 'Play flat (no VR).bat' always starts the normal flat game.\r\n\r\n" +
            "Logs (incl. [DCGO VR] Perf lines): %USERPROFILE%\\AppData\\LocalLow\\<company>\\<product>\\Player.log\r\n");

        Debug.Log($"[DCGO VR] Build: copied {copied} game data files next to the exe. Done: {folder}");
        EditorUtility.RevealInFinder(exe);
    }

    static void EnsureDefine(BuildTargetGroup group)
    {
        string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);

        if (!defines.Split(';').Contains(Define))
        {
            PlayerSettings.SetScriptingDefineSymbolsForGroup(group, string.IsNullOrEmpty(defines) ? Define : defines + ";" + Define);
            Debug.Log("[DCGO VR] Build: added DCGO_VR define for " + group);
        }
    }

    // The game reads Textures / Decks / Audio from "<exe folder>/Assets" (see
    // StreamingAssetsUtility). Copy what the flat build folders have (missing files only).
    static int CopyGameData(string folder)
    {
        string target = Path.Combine(folder, "Assets");
        int copied = 0;

        foreach (string source in new[] { Path.Combine(ProjectRoot, "Builds", "Assets"), Path.Combine(ProjectRoot, "Builds", "Windows", "Assets") })
        {
            if (!Directory.Exists(source))
            {
                continue;
            }

            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(source.Length).TrimStart('\\', '/');
                string destination = Path.Combine(target, relative);

                if (File.Exists(destination))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(file, destination);
                copied++;
            }
        }

        return copied;
    }
}
