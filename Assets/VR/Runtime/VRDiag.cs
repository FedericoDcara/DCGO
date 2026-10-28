#if DCGO_VR
using System.Collections.Concurrent;
using System.IO;
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// Copies every "[DCGO VR]" log line, plus all errors and exceptions, to
// "<game folder>/Logs/vr-diag.log" (project Logs in the editor), so a play session can
// be checked afterwards without the Unity console. Written in batches, off the hot path.
public static class VRDiag
{
    static readonly ConcurrentQueue<string> _pending = new ConcurrentQueue<string>();
    static string _path;
    static bool _installed;
    static float _nextFlush;

    /// <summary>Quiet mode (VRPerformance): ordinary Debug.Log lines are dropped before they
    /// reach the console (each one costs a stack trace in the editor), but "[DCGO VR]" lines
    /// still go to vr-diag.log. Replaces filterLogType = Warning, which also hid them from here.</summary>
    public static void SetQuiet(bool quiet)
    {
        Install();
        Debug.unityLogger.filterLogType = LogType.Log;

        if (quiet && !(Debug.unityLogger.logHandler is QuietHandler))
        {
            Debug.unityLogger.logHandler = new QuietHandler(Debug.unityLogger.logHandler);
        }
        else if (!quiet && Debug.unityLogger.logHandler is QuietHandler handler)
        {
            Debug.unityLogger.logHandler = handler.Inner;
        }
    }

    sealed class QuietHandler : ILogHandler
    {
        public readonly ILogHandler Inner;

        public QuietHandler(ILogHandler inner) => Inner = inner;

        public void LogFormat(LogType logType, Object context, string format, params object[] args)
        {
            if (logType == LogType.Log)
            {
                // Debug.Log(x) arrives as ("{0}", x): only look at our own lines, cheaply.
                string message = args != null && args.Length == 1 && format == "{0}" ? args[0] as string : null;

                if (message != null && message.Contains("[DCGO VR]") && !message.Contains("Perf:"))
                {
                    _pending.Enqueue(System.DateTime.Now.ToString("HH:mm:ss ") + message);
                }

                return; // not shown in the console
            }

            Inner.LogFormat(logType, context, format, args);
        }

        public void LogException(System.Exception exception, Object context) => Inner.LogException(exception, context);
    }

    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;

        try
        {
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs"));
            Directory.CreateDirectory(folder);
            _path = Path.Combine(folder, "vr-diag.log");
            File.AppendAllText(_path, "\n==== " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " session start ====\n");
        }
        catch
        {
            _path = null;
        }

        Application.logMessageReceivedThreaded += OnLog;
    }

    static void OnLog(string message, string stackTrace, LogType type)
    {
        bool error = type == LogType.Exception || type == LogType.Error || type == LogType.Assert;

        if (!error && (message == null || !message.Contains("[DCGO VR]")) || (message != null && message.Contains("Perf:")))
        {
            return;
        }

        string line = System.DateTime.Now.ToString("HH:mm:ss ") + (error ? type.ToString().ToUpper() + " " : "") + message;

        if (error && !string.IsNullOrEmpty(stackTrace))
        {
            line += "\n    " + stackTrace.Replace("\n", "\n    ").TrimEnd();
        }

        _pending.Enqueue(line);
    }

    /// <summary>Writes queued lines (called about once a second from the VR session).</summary>
    public static void Flush(bool force = false)
    {
        if (_path == null || (!force && Time.unscaledTime < _nextFlush) || _pending.IsEmpty)
        {
            return;
        }

        _nextFlush = Time.unscaledTime + 1f;
        System.Text.StringBuilder text = new System.Text.StringBuilder();
        int count = 0;

        while (count < 500 && _pending.TryDequeue(out string line))
        {
            text.Append(line).Append('\n');
            count++;
        }

        try
        {
            File.AppendAllText(_path, text.ToString());
        }
        catch
        {
            // Not critical.
        }
    }
}
#endif
