#if DCGO_VR
using UnityEngine;
using UnityEngine.Rendering.Universal;

// === DCGO-CUSTOM:vr ===
// VR-only performance profile. The flat game keeps its own settings; nothing here edits assets.
//  - No frame cap: the original game sets Application.targetFrameRate = 60 (ContinuousController),
//    which held the headset at ~59 fps. Desktop VSync off (the headset paces frames).
//  - Quiet logging: Debug.Log messages are dropped and logs/warnings skip stack traces
//    (each editor log line captured a full stack trace). Warnings and errors still show.
//    VRSettings.VerboseLogs = true brings everything back for debugging.
//  - Lightweight cameras: no HDR, no depth / opaque copy textures, no shadow pass, no
//    post-processing on the VR eye camera and the small helper cameras. MSAA stays (clean edges).
public static class VRPerformance
{
    public static void Apply(Camera eyeCamera)
    {
        Enforce();

        if (!VRSettings.VerboseLogs)
        {
            VRDiag.SetQuiet(true); // console: warnings, errors, exceptions only; "[DCGO VR]" lines still reach vr-diag.log
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
        }

        ConfigureCamera(eyeCamera);
    }

    /// <summary>Settings panel switch: full logs with stack traces (for debugging) or quiet (faster).</summary>
    public static void SetVerboseLogs(bool verbose)
    {
        VRSettings.VerboseLogs = verbose;
        VRDiag.SetQuiet(!verbose);
        Application.SetStackTraceLogType(LogType.Log, verbose ? StackTraceLogType.ScriptOnly : StackTraceLogType.None);
        Application.SetStackTraceLogType(LogType.Warning, verbose ? StackTraceLogType.ScriptOnly : StackTraceLogType.None);
    }

    /// <summary>Called every frame: the game can set the frame cap again when it loads a scene.</summary>
    public static void Enforce()
    {
        if (Application.targetFrameRate != -1)
        {
            Application.targetFrameRate = -1;
        }

        if (QualitySettings.vSyncCount != 0)
        {
            QualitySettings.vSyncCount = 0;
        }
    }

    /// <summary>Turns off everything a VR / helper camera does not need (per camera, no asset changes).</summary>
    public static void ConfigureCamera(Camera camera, bool keepMsaa = true)
    {
        if (camera == null)
        {
            return;
        }

        camera.allowHDR = false;
        camera.allowMSAA = keepMsaa;
        UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();

        if (data == null)
        {
            return;
        }

        data.renderPostProcessing = false;
        data.antialiasing = AntialiasingMode.None;
        data.requiresDepthOption = CameraOverrideOption.Off;
        data.requiresColorOption = CameraOverrideOption.Off;
        data.renderShadows = false;
        data.stopNaN = false;
        data.dithering = false;
    }
}
#endif
