#if DCGO_VR
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.XR;
using Unity.Profiling;

// === DCGO-CUSTOM:vr ===
// Performance monitor for the VR build.
//  - Every 10 s writes one line to the log: average fps, 1% low, worst frame,
//    frames over budget, managed memory and GC count, so a play session can be
//    reviewed afterwards from Player.log / Editor.log.
//  - With "Performance stats" on (VR settings), a small readout floats above the
//    left controller.
//  - Applies the "Render quality" setting (headset eye-texture scale).
public class VRPerf : MonoBehaviour
{
    const float Window = 10f;

    VRRig _rig;
    VRLabel _label;
    readonly float[] _frames = new float[2048];
    int _count;
    float _windowStart;
    int _gcAtStart;
    float _displayNext;
    float _displayFps;
    float _displayWorst;

    // Engine counters (valid in development builds / the editor).
    ProfilerRecorder _gcAlloc;
    ProfilerRecorder _mainThread;
    ProfilerRecorder _renderThread;
    double _allocSum;
    double _mainSum;
    double _mainMax;
    double _renderSum;
    int _samples;
    int _notWornFrames;

    void OnEnable()
    {
        _gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
        _mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
        _renderThread = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Render Thread", 15);
    }

    void OnDisable()
    {
        _gcAlloc.Dispose();
        _mainThread.Dispose();
        _renderThread.Dispose();
    }

    void SampleCounters()
    {
        _samples++;

        if (_gcAlloc.Valid)
        {
            _allocSum += _gcAlloc.LastValue;
        }

        if (_mainThread.Valid)
        {
            double ms = _mainThread.LastValue * 1e-6;
            _mainSum += ms;
            _mainMax = System.Math.Max(_mainMax, ms);
        }

        if (_renderThread.Valid)
        {
            _renderSum += _renderThread.LastValue * 1e-6;
        }

        InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);

        if (head.isValid && head.TryGetFeatureValue(CommonUsages.userPresence, out bool worn) && !worn)
        {
            _notWornFrames++;
        }
    }

    public static VRPerf Create(Transform parent, VRRig rig)
    {
        GameObject go = new GameObject("VRPerf") { layer = VRRig.Layer };
        go.transform.SetParent(parent, false);
        VRPerf perf = go.AddComponent<VRPerf>();
        perf._rig = rig;
        perf._label = VRLabel.Create(rig.LeftHand, new Vector3(0f, 0.09f, 0.02f), Quaternion.Euler(20f, 0f, 0f), new Vector2(0.16f, 0.045f), "", new Color(0.6f, 1f, 0.7f), new Color(0f, 0f, 0f));
        perf._label.enableWordWrapping = true;
        perf._label.gameObject.SetActive(false);
        perf._windowStart = Time.unscaledTime;
        perf._gcAtStart = System.GC.CollectionCount(0);
        return perf;
    }

    public static void ApplyRenderScale()
    {
        float scale = Mathf.Clamp(VRSettings.RenderScale, 50, 200) / 100f;
        // The headset compositor paces frames; desktop VSync only caps the editor / mirror at 60.
        QualitySettings.vSyncCount = 0;

        try
        {
            XRSettings.eyeTextureResolutionScale = scale;
            Debug.Log($"[DCGO VR] Render quality {scale:P0} (eye texture {XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight})");
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning("[DCGO VR] Render scale not applied: " + exception.Message);
        }
    }

    static float TargetFps()
    {
        float refresh = XRDevice.refreshRate;
        return refresh > 1f ? refresh : 72f;
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        if (_count < _frames.Length)
        {
            _frames[_count++] = dt;
        }

        SampleCounters();

        // Small live readout (smoothed, 2 updates per second).
        bool show = VRSettings.PerfStats;

        if (_label.gameObject.activeSelf != show)
        {
            _label.gameObject.SetActive(show);
        }

        _displayFps = Mathf.Lerp(_displayFps, 1f / Mathf.Max(dt, 1e-4f), 0.05f);
        _displayWorst = Mathf.Max(_displayWorst * 0.98f, dt * 1000f);

        if (show && Time.unscaledTime >= _displayNext)
        {
            _displayNext = Time.unscaledTime + 0.5f;
            long mono = Profiler.GetMonoUsedSizeLong() / (1024 * 1024);
            _label.text = $"{_displayFps:0} fps / {TargetFps():0}   worst {_displayWorst:0.0} ms\nmono {mono} MB   gc {System.GC.CollectionCount(0) - _gcAtStart}";
        }

        if (Time.unscaledTime - _windowStart >= Window)
        {
            LogWindow();
        }
    }

    // Also kept in "<game folder>/Logs/vr-perf.log" (project Logs in the editor), easy to find after a session.
    static void AppendToFile(string line)
    {
        try
        {
            string folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Logs"));
            System.IO.Directory.CreateDirectory(folder);
            System.IO.File.AppendAllText(System.IO.Path.Combine(folder, "vr-perf.log"), System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + line + "\n");
        }
        catch
        {
            // Logging must never disturb the game.
        }
    }

    void LogWindow()
    {
        int n = _count;

        if (n > 10)
        {
            float total = 0f;
            float worst = 0f;
            int over = 0;
            float budget = 1f / TargetFps() * 1.05f;
            float[] sorted = new float[n];

            for (int i = 0; i < n; i++)
            {
                float f = _frames[i];
                total += f;
                worst = Mathf.Max(worst, f);
                over += f > budget ? 1 : 0;
                sorted[i] = f;
            }

            System.Array.Sort(sorted);
            float onePercentLow = sorted[Mathf.Clamp(Mathf.FloorToInt(n * 0.99f), 0, n - 1)];
            StringBuilder line = new StringBuilder();
            line.Append($"[DCGO VR] Perf: {n / total:0.0} fps avg (target {TargetFps():0}), 1% low {1f / onePercentLow:0.0} fps, worst {worst * 1000f:0.0} ms, ");
            line.Append($"{over * 100f / n:0}% frames over budget, mono {Profiler.GetMonoUsedSizeLong() / (1024 * 1024)} MB, ");
            line.Append($"gc +{System.GC.CollectionCount(0) - _gcAtStart}, ");
            line.Append($"scene: {(GManager.instance != null ? "duel" : "menu")}, render {XRSettings.eyeTextureResolutionScale:P0}");

            if (_samples > 0)
            {
                if (_gcAlloc.Valid)
                {
                    line.Append($", alloc {_allocSum / _samples / 1024.0:0.0} KB/frame");
                }

                if (_mainThread.Valid)
                {
                    line.Append($", main {_mainSum / _samples:0.0} ms (max {_mainMax:0.0})");
                }

                if (_renderThread.Valid)
                {
                    line.Append($", render thread {_renderSum / _samples:0.0} ms");
                }

                line.Append(_notWornFrames > _samples / 2 ? ", HEADSET NOT WORN" : "");
                line.Append(Application.isFocused ? "" : ", window unfocused");
            }
            Debug.Log(line.ToString());
            AppendToFile(line.ToString());
        }

        _count = 0;
        _samples = 0;
        _allocSum = _mainSum = _mainMax = _renderSum = 0;
        _notWornFrames = 0;
        _windowStart = Time.unscaledTime;
        _gcAtStart = System.GC.CollectionCount(0);
    }
}
#endif
