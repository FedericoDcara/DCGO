using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Switches battle BGM among three intensity tracks based on the local player's security count.
/// Full (5+) / Half (2-4) / Critical (0-1). Opt-in via ContinuousController.useReactiveMatchMusic.
/// At match start, one random group of 3 tracks is selected from catalog.json.
/// </summary>
public class MatchMusicController : MonoBehaviour
{
    public enum Intensity
    {
        Full = 0,
        Half = 1,
        Critical = 2
    }

    public static MatchMusicController instance { get; private set; }

    // Cached for the last selected group (rematches with the same group skip re-decode).
    static string s_cachedGroupId;
    static AudioClip s_fullClip;
    static AudioClip s_halfClip;
    static AudioClip s_criticalClip;

    StreamingAssetsUtility.MatchMusicGroupJson _activeGroup;
    AudioClip _fullClip;
    AudioClip _halfClip;
    AudioClip _criticalClip;
    Intensity? _currentIntensity;
    bool _reacting;
    bool _startedPlayback;
    bool _backgroundLoadStarted;
    Coroutine _loadRoutine;
    Coroutine _ensureRoutine;

    public static MatchMusicController EnsureExists()
    {
        if (instance != null)
            return instance;

        var go = new GameObject("MatchMusicController");
        instance = go.AddComponent<MatchMusicController>();
        DontDestroyOnLoad(go);
        return instance;
    }

    void OnEnable()
    {
        GManager.OnSecurityStackChanged += OnSecurityStackChanged;
    }

    void OnDisable()
    {
        GManager.OnSecurityStackChanged -= OnSecurityStackChanged;
    }

    void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    /// <summary>
    /// Begin reactive music for this match. Picks a random group, loads current track first.
    /// </summary>
    public void StartForMatch()
    {
        _reacting = true;
        _startedPlayback = false;
        _currentIntensity = null;
        _backgroundLoadStarted = false;

        if (_loadRoutine != null)
            StopCoroutine(_loadRoutine);
        if (_ensureRoutine != null)
            StopCoroutine(_ensureRoutine);

        _loadRoutine = StartCoroutine(LoadAndStartCoroutine());
    }

    /// <summary>Stop reacting; leave the current BGM clip playing.</summary>
    public void StopReacting()
    {
        _reacting = false;
        _currentIntensity = null;
    }

    /// <summary>Enable mid-match: start reacting from the current security count.</summary>
    public void ResumeReacting()
    {
        _reacting = true;
        if (_loadRoutine != null)
            StopCoroutine(_loadRoutine);
        if (_ensureRoutine != null)
            StopCoroutine(_ensureRoutine);
        _loadRoutine = StartCoroutine(LoadAndStartCoroutine());
    }

    void ClearInstanceClips()
    {
        _fullClip = null;
        _halfClip = null;
        _criticalClip = null;
    }

    void BindFromCacheIfSameGroup(string groupId)
    {
        if (!string.IsNullOrEmpty(s_cachedGroupId) && s_cachedGroupId == groupId)
        {
            _fullClip = s_fullClip;
            _halfClip = s_halfClip;
            _criticalClip = s_criticalClip;

            if (_criticalClip != null && (_criticalClip == _halfClip || _criticalClip == _fullClip))
            {
                _criticalClip = null;
                s_criticalClip = null;
            }
            return;
        }

        ClearInstanceClips();
    }

    void PushToCache(string groupId)
    {
        s_cachedGroupId = groupId;
        s_fullClip = _fullClip;
        s_halfClip = _halfClip;
        s_criticalClip = _criticalClip;
    }

    IEnumerator LoadAndStartCoroutine()
    {
        var catalogTask = StreamingAssetsUtility.ReadMatchMusicCatalog();
        while (!catalogTask.IsCompleted)
            yield return null;

        StreamingAssetsUtility.MatchMusicCatalogJson catalog =
            catalogTask.Status == TaskStatus.RanToCompletion
                ? catalogTask.Result
                : new StreamingAssetsUtility.MatchMusicCatalogJson();

        List<StreamingAssetsUtility.MatchMusicGroupJson> groups = catalog.GetGroups();
        _activeGroup = groups.Count == 1
            ? groups[0]
            : groups[UnityEngine.Random.Range(0, groups.Count)];

        string groupId = _activeGroup != null ? _activeGroup.name : "default";
        Debug.Log($"[MatchMusic] Selected group '{groupId}' ({groups.Count} available)");

        BindFromCacheIfSameGroup(groupId);

        Intensity needed = ResolveIntensity();

        if (ExactClipFor(needed) != null)
        {
            if (_reacting)
                ApplyIntensity(needed, force: true);

            if (!AllClipsReady())
                yield return StartCoroutine(LoadRemainingClipsCoroutine(needed));

            _loadRoutine = null;
            yield break;
        }

        yield return LoadSingleIntensityCoroutine(needed);

        if (_reacting && ExactClipFor(needed) != null)
            ApplyIntensity(needed, force: true);

        yield return LoadRemainingClipsCoroutine(needed);

        _loadRoutine = null;
    }

    IEnumerator LoadSingleIntensityCoroutine(Intensity intensity)
    {
        if (ExactClipFor(intensity) != null)
            yield break;

        if (_activeGroup == null)
        {
            var catalogTask = StreamingAssetsUtility.ReadMatchMusicCatalog();
            while (!catalogTask.IsCompleted)
                yield return null;

            StreamingAssetsUtility.MatchMusicCatalogJson catalog =
                catalogTask.Status == TaskStatus.RanToCompletion
                    ? catalogTask.Result
                    : new StreamingAssetsUtility.MatchMusicCatalogJson();
            _activeGroup = catalog.PickRandomGroup();
        }

        string fileName = FileNameFor(intensity, _activeGroup);
        var loadTask = StreamingAssetsUtility.LoadMatchMusicClip(fileName);
        while (!loadTask.IsCompleted)
            yield return null;

        AudioClip clip = loadTask.Status == TaskStatus.RanToCompletion ? loadTask.Result : null;
        if (clip == null)
            clip = FallbackInspectorClip(intensity);

        if (clip != null)
            AssignClip(intensity, clip);

        string groupId = _activeGroup != null ? _activeGroup.name : "default";
        PushToCache(groupId);
        yield return null;
    }

    IEnumerator LoadRemainingClipsCoroutine(Intensity alreadyLoaded)
    {
        if (_backgroundLoadStarted)
            yield break;
        _backgroundLoadStarted = true;

        yield return null;
        yield return null;

        Intensity[] order = { Intensity.Full, Intensity.Half, Intensity.Critical };
        foreach (Intensity intensity in order)
        {
            if (intensity == alreadyLoaded)
                continue;
            if (ExactClipFor(intensity) != null)
                continue;

            yield return LoadSingleIntensityCoroutine(intensity);

            if (_reacting && _currentIntensity == intensity && ExactClipFor(intensity) != null)
                ApplyIntensity(intensity, force: true);

            yield return null;
            yield return null;
        }

        ApplyInspectorFallbacks();
        string groupId = _activeGroup != null ? _activeGroup.name : "default";
        PushToCache(groupId);

        if (_reacting)
        {
            Intensity now = ResolveIntensity();
            if (ExactClipFor(now) != null)
                ApplyIntensity(now, force: true);
        }
    }

    static string FileNameFor(Intensity intensity, StreamingAssetsUtility.MatchMusicGroupJson group)
    {
        if (group == null)
            group = new StreamingAssetsUtility.MatchMusicGroupJson { name = "pack1" };

        switch (intensity)
        {
            case Intensity.Half:
                return group.HalfRelativePath;
            case Intensity.Critical:
                return group.CriticalRelativePath;
            default:
                return group.FullRelativePath;
        }
    }

    void AssignClip(Intensity intensity, AudioClip clip)
    {
        switch (intensity)
        {
            case Intensity.Half:
                _halfClip = clip;
                break;
            case Intensity.Critical:
                _criticalClip = clip;
                break;
            default:
                _fullClip = clip;
                break;
        }
    }

    AudioClip FallbackInspectorClip(Intensity intensity)
    {
        if (GManager.instance == null || GManager.instance.bgms == null)
            return null;

        int index = (int)intensity;
        if (GManager.instance.bgms.Count > index)
            return GManager.instance.bgms[index];
        return null;
    }

    void ApplyInspectorFallbacks()
    {
        if (GManager.instance == null || GManager.instance.bgms == null)
            return;

        if (_fullClip == null && GManager.instance.bgms.Count > 0)
            _fullClip = GManager.instance.bgms[0];
        if (_halfClip == null && GManager.instance.bgms.Count > 1)
            _halfClip = GManager.instance.bgms[1];
        if (_criticalClip == null && GManager.instance.bgms.Count > 2)
            _criticalClip = GManager.instance.bgms[2];
    }

    bool AllClipsReady()
    {
        return _fullClip != null && _halfClip != null && _criticalClip != null;
    }

    void OnSecurityStackChanged(Player player)
    {
        if (!_reacting || player == null || !player.isYou)
            return;

        Intensity intensity = ResolveIntensity(player);

        if (ExactClipFor(intensity) == null)
        {
            if (_ensureRoutine != null)
                StopCoroutine(_ensureRoutine);
            _ensureRoutine = StartCoroutine(EnsureIntensityThenApplyCoroutine(intensity));
            return;
        }

        ApplyIntensity(intensity, force: false);
    }

    IEnumerator EnsureIntensityThenApplyCoroutine(Intensity intensity)
    {
        yield return LoadSingleIntensityCoroutine(intensity);
        if (_reacting && ExactClipFor(intensity) != null)
            ApplyIntensity(intensity, force: true);
        _ensureRoutine = null;
    }

    Intensity ResolveIntensity(Player player = null)
    {
        Player you = player;
        if (you == null && GManager.instance != null)
            you = GManager.instance.You;

        bool setupDone = GManager.instance?.turnStateMachine != null
            && GManager.instance.turnStateMachine.DoneStartGame;

        if (!setupDone)
            return Intensity.Full;

        int count = you?.SecurityCards != null ? you.SecurityCards.Count : 0;
        if (count >= 5)
            return Intensity.Full;
        if (count >= 2)
            return Intensity.Half;
        return Intensity.Critical;
    }

    void ApplyIntensity(Intensity intensity, bool force)
    {
        if (!_reacting)
            return;

        AudioClip clip = ExactClipFor(intensity);
        if (clip == null)
            return;

        if (!force
            && _currentIntensity.HasValue
            && _currentIntensity.Value == intensity)
        {
            BGMObject currentBgm = GManager.instance != null ? GManager.instance.BattleBGM : null;
            if (currentBgm != null && currentBgm._audio != null && currentBgm._audio.clip == clip)
                return;
        }

        BGMObject bgm = GManager.instance != null ? GManager.instance.BattleBGM : null;
        if (bgm == null)
            return;

        Intensity? previous = _currentIntensity;
        _currentIntensity = intensity;

        if (!_startedPlayback || !bgm.isPlaying || previous == null)
        {
            if (bgm.isPlaying && bgm._audio != null && bgm._audio.clip != null && bgm._audio.clip != clip)
            {
                bgm.CrossfadeToClip(clip, 0.6f);
                _startedPlayback = true;
                return;
            }

            bgm.StartPlayBGM(clip);
            _startedPlayback = true;
            return;
        }

        if (bgm._audio != null && bgm._audio.clip == clip)
            return;

        bgm.CrossfadeToClip(clip, 0.6f);
        _startedPlayback = true;
    }

    AudioClip ExactClipFor(Intensity intensity)
    {
        switch (intensity)
        {
            case Intensity.Half:
                return _halfClip;
            case Intensity.Critical:
                return _criticalClip;
            default:
                return _fullClip;
        }
    }
}
