using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

// === DCGO-CUSTOM:vr ===
// DCGO > VR menu.
//   Setup VR   : OpenXR loader for PC + Android (Quest), controller profiles,
//                Meta Quest feature, DCGO_VR define, VR materials.
//                XR is NOT started on launch; VRSession starts it only when a
//                headset is present, so a VR build still runs flat without one.
//   Disable VR : removes the DCGO_VR define (back to the normal flat build).
public static class VRSetup
{
    const string Define = "DCGO_VR";
    const string MaterialPath = "Assets/VR/Resources/DCGOVR_Unlit.mat";
    const string OpenXRLoaderType = "UnityEngine.XR.OpenXR.OpenXRLoader";

    static readonly BuildTargetGroup[] Targets = { BuildTargetGroup.Standalone, BuildTargetGroup.Android };

    static readonly HashSet<string> WantedFeatures = new HashSet<string>
    {
        "OculusTouchControllerProfile",
        "MetaQuestTouchProControllerProfile",
        "ValveIndexControllerProfile",
        "HTCViveControllerProfile",
        "KHRSimpleControllerProfile",
        "MetaQuestFeature",
    };

    [MenuItem("DCGO/VR/Setup VR (OpenXR, Quest + PC VR)")]
    public static void Setup()
    {
        StringBuilder log = new StringBuilder();
        CreateMaterial(log);
        EnsureBothInputHandlers(log);

        XRGeneralSettingsPerBuildTarget perTarget = GetOrCreatePerTargetSettings(log);
        bool missing = false;

        foreach (BuildTargetGroup group in Targets)
        {
            if (perTarget != null)
            {
                EnsureTargetSettings(perTarget, group);
            }

            XRGeneralSettings settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);

            if (settings == null || settings.Manager == null)
            {
                log.AppendLine($"[{group}] XR settings not found. Open Project Settings > XR Plug-in Management, select the {group} tab once, then run Setup VR again.");
                missing = true;
                continue;
            }

            settings.InitManagerOnStart = false;

            bool hasOpenXR = settings.Manager.activeLoaders.Any(l => l != null && l.GetType().FullName == OpenXRLoaderType);

            if (!hasOpenXR)
            {
                bool assigned = XRPackageMetadataStore.AssignLoader(settings.Manager, OpenXRLoaderType, group);
                log.AppendLine($"[{group}] OpenXR loader " + (assigned ? "assigned." : "could NOT be assigned (enable OpenXR in XR Plug-in Management)."));
            }
            else
            {
                log.AppendLine($"[{group}] OpenXR loader already assigned.");
            }

            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(settings.Manager);

            EnableFeatures(group, log);
            AddDefine(group);
            log.AppendLine($"[{group}] {Define} define added, Initialize XR on Startup = off.");
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[DCGO VR] Setup VR\n" + log);
        EditorUtility.DisplayDialog("DCGO VR", log + (missing ? "\nSome targets still need setup (see above)." : "\nDone. Press Play with a headset connected (Quest Link / SteamVR)."), "OK");
    }

    // The game reads the legacy Input API and VR tracking uses the Input System,
    // so Player Settings > Active Input Handling must be "Both" (value 2).
    static void EnsureBothInputHandlers(StringBuilder log)
    {
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");

        if (assets == null || assets.Length == 0)
        {
            log.AppendLine("Active Input Handling: could not read Player Settings, set it to Both by hand.");
            return;
        }

        SerializedObject playerSettings = new SerializedObject(assets[0]);
        SerializedProperty handler = playerSettings.FindProperty("activeInputHandler");

        if (handler == null)
        {
            log.AppendLine("Active Input Handling: setting not found, set it to Both by hand.");
            return;
        }

        if (handler.intValue == 2)
        {
            log.AppendLine("Active Input Handling already Both.");
            return;
        }

        handler.intValue = 2;
        playerSettings.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
        log.AppendLine("Active Input Handling set to Both. RESTART the Unity editor for it to take effect.");
    }

    [MenuItem("DCGO/VR/Disable VR (flat build)")]
    public static void Disable()
    {
        foreach (BuildTargetGroup group in Targets)
        {
            List<string> defines = GetDefines(group);

            if (defines.Remove(Define))
            {
                PlayerSettings.SetScriptingDefineSymbolsForGroup(group, string.Join(";", defines));
            }
        }

        Debug.Log("[DCGO VR] DCGO_VR define removed. Builds are flat again (OpenXR stays installed but is never started).");
    }

    static void CreateMaterial(StringBuilder log)
    {
        if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath) != null)
        {
            return;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
        {
            log.AppendLine("URP Unlit shader not found; VR materials not created.");
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
        AssetDatabase.CreateAsset(new Material(shader) { name = "DCGOVR_Unlit" }, MaterialPath);
        log.AppendLine("Created " + MaterialPath);
    }

    static XRGeneralSettingsPerBuildTarget GetOrCreatePerTargetSettings(StringBuilder log)
    {
        EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget);

        if (perTarget != null)
        {
            return perTarget;
        }

        Directory.CreateDirectory("Assets/XR");
        perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
        AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
        EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
        log.AppendLine("Created Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
        return perTarget;
    }

    // These helpers are public in newer XR Management versions; call them by
    // reflection so the menu compiles against any 4.x version.
    static void EnsureTargetSettings(XRGeneralSettingsPerBuildTarget perTarget, BuildTargetGroup group)
    {
        Invoke(perTarget, "HasSettingsForBuildTarget", "CreateDefaultSettingsForBuildTarget", group);
        Invoke(perTarget, "HasManagerSettingsForBuildTarget", "CreateDefaultManagerSettingsForBuildTarget", group);
    }

    static void Invoke(object target, string hasMethod, string createMethod, BuildTargetGroup group)
    {
        Type type = target.GetType();
        MethodInfo has = type.GetMethod(hasMethod, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo create = type.GetMethod(createMethod, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        if (has == null || create == null)
        {
            return;
        }

        try
        {
            if (!(bool)has.Invoke(target, new object[] { group }))
            {
                create.Invoke(target, new object[] { group });
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DCGO VR] {createMethod}({group}) failed: {e.InnerException?.Message ?? e.Message}");
        }
    }

    static void EnableFeatures(BuildTargetGroup group, StringBuilder log)
    {
        OpenXRSettings openXR = OpenXRSettings.GetSettingsForBuildTargetGroup(group);

        if (openXR == null)
        {
            log.AppendLine($"[{group}] OpenXR settings not found yet.");
            return;
        }

        List<string> enabled = new List<string>();

        foreach (var feature in openXR.GetFeatures())
        {
            if (feature != null && WantedFeatures.Contains(feature.GetType().Name) && !feature.enabled)
            {
                feature.enabled = true;
                EditorUtility.SetDirty(feature);
                enabled.Add(feature.GetType().Name);
            }
        }

        EditorUtility.SetDirty(openXR);
        log.AppendLine($"[{group}] OpenXR features enabled: " + (enabled.Count > 0 ? string.Join(", ", enabled) : "(already on)"));
    }

    static List<string> GetDefines(BuildTargetGroup group) =>
        PlayerSettings.GetScriptingDefineSymbolsForGroup(group)
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(d => d.Trim())
            .ToList();

    static void AddDefine(BuildTargetGroup group)
    {
        List<string> defines = GetDefines(group);

        if (!defines.Contains(Define))
        {
            defines.Add(Define);
            PlayerSettings.SetScriptingDefineSymbolsForGroup(group, string.Join(";", defines));
        }
    }
}
