using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// DCGO > VR > Log Performance Hotspots (Play Mode)
// While on and in Play Mode, records the Unity Profiler (main thread) and every 10 s writes to the
// Console / Editor.log which scripts allocate the most garbage (GC.Alloc) and use the most CPU
// time per frame, plus a breakdown of every frame slower than 40 ms (stutters).
// Lines start with "[DCGO VR] Hotspots". Leave it off normally: profiling costs editor time.
[InitializeOnLoad]
public static class VRHotspotReport
{
    const string PrefKey = "DCGO.VR.HotspotReport";
    const string MenuPath = "DCGO/VR/Log Performance Hotspots (Play Mode)";
    const double ReportEverySeconds = 10.0;
    const float SpikeMs = 40f;
    const int MaxFramesPerTick = 3;

    static readonly Dictionary<string, double> _alloc = new Dictionary<string, double>();
    static readonly Dictionary<string, double> _selfMs = new Dictionary<string, double>();
    static readonly List<string> _spikes = new List<string>();
    static int _lastFrame = -1;
    static int _frames;
    static double _nextReport;
    static bool _weEnabledProfiler;

    static bool On
    {
        get => EditorPrefs.GetBool(PrefKey, false);
        set => EditorPrefs.SetBool(PrefKey, value);
    }

    static VRHotspotReport()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem(MenuPath)]
    static void Toggle()
    {
        On = !On;
        Debug.Log("[DCGO VR] Hotspot report " + (On ? "ON (enter Play Mode and play a duel)" : "OFF"));

        if (!On)
        {
            StopProfiler();
        }
    }

    [MenuItem(MenuPath, true)]
    static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, On);
        return true;
    }

    static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            ResetCounters();
            _lastFrame = -1;
            _nextReport = EditorApplication.timeSinceStartup + ReportEverySeconds;
        }
        else if (change == PlayModeStateChange.ExitingPlayMode)
        {
            if (On)
            {
                Report();
            }

            StopProfiler();
        }
    }

    static void Tick()
    {
        if (!On || !EditorApplication.isPlaying || EditorApplication.isPaused)
        {
            return;
        }

        try
        {
            if (!ProfilerDriver.enabled)
            {
                ProfilerDriver.profileEditor = false;
                ProfilerDriver.enabled = true;
                _weEnabledProfiler = true;
            }

            int last = ProfilerDriver.lastFrameIndex;
            int first = ProfilerDriver.firstFrameIndex;

            if (last < 0)
            {
                return;
            }

            // Profiler keeps a limited history: if we fell behind, skip ahead.
            int start = Math.Max(first, _lastFrame + 1);
            int end = Math.Min(last, start + MaxFramesPerTick - 1);

            for (int frame = start; frame <= end; frame++)
            {
                Collect(frame);
            }

            _lastFrame = Math.Max(_lastFrame, end);

            if (EditorApplication.timeSinceStartup >= _nextReport)
            {
                Report();
                _nextReport = EditorApplication.timeSinceStartup + ReportEverySeconds;
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[DCGO VR] Hotspot report stopped: " + exception.Message);
            On = false;
            StopProfiler();
        }
    }

    static void Collect(int frame)
    {
        using (HierarchyFrameDataView view = ProfilerDriver.GetHierarchyFrameDataView(
                   frame, 0,
                   HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName | HierarchyFrameDataView.ViewModes.HideEditorOnlySamples,
                   HierarchyFrameDataView.columnSelfTime, false))
        {
            if (view == null || !view.valid)
            {
                return;
            }

            _frames++;
            float frameMs = view.frameTimeMs;
            List<KeyValuePair<string, float>> frameSelf = frameMs >= SpikeMs ? new List<KeyValuePair<string, float>>() : null;

            Walk(view, view.GetRootItemID(), "", "", frameSelf);

            if (frameSelf != null && _spikes.Count < 20)
            {
                IEnumerable<string> top = frameSelf.OrderByDescending(p => p.Value).Take(5).Select(p => $"{p.Value:F1} ms {p.Key}");
                _spikes.Add($"{frameMs:F0} ms frame: " + string.Join(" | ", top));
            }
        }
    }

    static void Walk(HierarchyFrameDataView view, int id, string name, string parent, List<KeyValuePair<string, float>> frameSelf)
    {
        List<int> children = new List<int>();
        view.GetItemChildren(id, children);

        foreach (int child in children)
        {
            string childName = view.GetItemName(child);

            if (childName == "GC.Alloc")
            {
                Add(_alloc, Key(parent, name), view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnGcMemory));
                continue;
            }

            float self = view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnSelfTime);
            string key = Key(name, childName);

            if (self > 0.02f)
            {
                Add(_selfMs, key, self);
                frameSelf?.Add(new KeyValuePair<string, float>(key, self));
            }

            Walk(view, child, childName, name, frameSelf);
        }
    }

    static string Key(string parent, string name)
        => string.IsNullOrEmpty(parent) ? name : parent + " > " + name;

    static void Add(Dictionary<string, double> table, string key, double value)
    {
        table.TryGetValue(key, out double sum);
        table[key] = sum + value;
    }

    static void Report()
    {
        if (_frames == 0)
        {
            return;
        }

        StringBuilder sb = new StringBuilder();
        sb.Append($"[DCGO VR] Hotspots ({_frames} frames)\n  GC alloc per frame:");

        foreach (KeyValuePair<string, double> pair in _alloc.OrderByDescending(p => p.Value).Take(10))
        {
            sb.Append($"\n    {pair.Value / _frames / 1024.0,8:F1} KB  {pair.Key}");
        }

        sb.Append("\n  CPU self time per frame:");

        foreach (KeyValuePair<string, double> pair in _selfMs.OrderByDescending(p => p.Value).Take(12))
        {
            sb.Append($"\n    {pair.Value / _frames,8:F2} ms  {pair.Key}");
        }

        if (_spikes.Count > 0)
        {
            sb.Append($"\n  Slow frames (>{SpikeMs:F0} ms):");

            foreach (string spike in _spikes)
            {
                sb.Append("\n    " + spike);
            }
        }

        Debug.Log(sb.ToString());
        ResetCounters();
    }

    static void ResetCounters()
    {
        _alloc.Clear();
        _selfMs.Clear();
        _spikes.Clear();
        _frames = 0;
    }

    static void StopProfiler()
    {
        if (_weEnabledProfiler)
        {
            ProfilerDriver.enabled = false;
            _weEnabledProfiler = false;
        }
    }
}
