using System;
using System.Collections;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using UnityEngine;

/// <summary>
/// Mobile share sheet and document picker for .dcgoreplay files.
/// Desktop keeps using a real file path.
/// </summary>
public class ReplayFileBridge : MonoBehaviour
{
    public const string ObjectName = "ReplayFileBridge";

    static ReplayFileBridge _instance;
    Action<string> _onPicked;
    bool _picking;
    Coroutine _poll;

    public static bool UsesSystemShare =>
        !Application.isEditor &&
        (Application.platform == RuntimePlatform.Android ||
         Application.platform == RuntimePlatform.IPhonePlayer);

    public static bool Share(string absolutePath)
    {
        if (!UsesSystemShare || string.IsNullOrEmpty(absolutePath))
        {
            return false;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            StartReplayActivity("share", absolutePath);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Replay] Android share failed: {ex.Message}");
            return false;
        }
#elif UNITY_IOS && !UNITY_EDITOR
        DCGO_ReplayShareFile(absolutePath);
        return true;
#else
        return false;
#endif
    }

    public static void Pick(Action<string> onPicked)
    {
        if (!UsesSystemShare)
        {
            onPicked?.Invoke(null);
            return;
        }

        Ensure();
        if (_instance._picking)
        {
            return;
        }

        _instance._picking = true;
        _instance._onPicked = onPicked;

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var activityClass = new AndroidJavaClass("com.dcgo.replayshare.ReplayFileActivity"))
            {
                activityClass.CallStatic("clearResult");
            }

            StartReplayActivity("pick", null);

            if (_instance._poll != null)
            {
                _instance.StopCoroutine(_instance._poll);
            }

            _instance._poll = _instance.StartCoroutine(_instance.PollAndroidPick());
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Replay] Android import picker failed: {ex.Message}");
            _instance.FinishPick(null);
        }
#elif UNITY_IOS && !UNITY_EDITOR
        DCGO_ReplayPickFile();
#else
        _instance.FinishPick(null);
#endif
    }

    /// <summary>Called from the iOS document picker.</summary>
    public void OnReplayFilePicked(string path)
    {
        FinishPick(path);
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    static void StartReplayActivity(string mode, string path)
    {
        using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
        using (var intent = new AndroidJavaObject("android.content.Intent"))
        {
            intent.Call<AndroidJavaObject>("setClassName", activity, "com.dcgo.replayshare.ReplayFileActivity");
            intent.Call<AndroidJavaObject>("putExtra", "mode", mode);
            if (path != null)
            {
                intent.Call<AndroidJavaObject>("putExtra", "path", path);
            }

            activity.Call("startActivity", intent);
        }
    }
#endif

    static void Ensure()
    {
        if (_instance != null)
        {
            return;
        }

        var existing = GameObject.Find(ObjectName);
        if (existing != null)
        {
            _instance = existing.GetComponent<ReplayFileBridge>();
            if (_instance != null)
            {
                return;
            }
        }

        var go = new GameObject(ObjectName);
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<ReplayFileBridge>();
    }

    IEnumerator PollAndroidPick()
    {
        float waited = 0f;
        while (waited < 180f)
        {
            yield return null;
            waited += Time.unscaledDeltaTime;

#if UNITY_ANDROID && !UNITY_EDITOR
            bool ready = false;
            string path = null;
            try
            {
                using (var activityClass = new AndroidJavaClass("com.dcgo.replayshare.ReplayFileActivity"))
                {
                    ready = activityClass.GetStatic<bool>("hasResult");
                    if (ready)
                    {
                        path = activityClass.GetStatic<string>("pendingResult");
                        activityClass.CallStatic("clearResult");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Replay] Android pick poll failed: {ex.Message}");
                _poll = null;
                FinishPick(null);
                yield break;
            }

            if (ready)
            {
                _poll = null;
                FinishPick(path);
                yield break;
            }
#else
            _poll = null;
            FinishPick(null);
            yield break;
#endif
        }

        _poll = null;
        FinishPick(null);
    }

    void FinishPick(string path)
    {
        _picking = false;
        var callback = _onPicked;
        _onPicked = null;
        callback?.Invoke(path);
    }

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    static extern void DCGO_ReplayShareFile(string path);

    [DllImport("__Internal")]
    static extern void DCGO_ReplayPickFile();
#endif
}
