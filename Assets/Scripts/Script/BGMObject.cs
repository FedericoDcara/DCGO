using System.Collections;
using UnityEngine;
using DG.Tweening;

[RequireComponent(typeof(AudioSource))]
public class BGMObject : MonoBehaviour
{
    public AudioSource _audio { get; set; }
    public bool isPlaying { get; set; } = false;
    bool isFading { get; set; } = false;

    // === DCGO-CUSTOM:matchmusic begin ===
    AudioSource _crossfadeAudio;
    Coroutine _crossfadeRoutine;
    // === DCGO-CUSTOM:matchmusic end ===

    private void Start()
    {

    }

    public void StopPlayBGM()
    {
        _audio = GetComponent<AudioSource>();

        // === DCGO-CUSTOM:matchmusic begin ===
        StopCrossfadeRoutine();
        if (_crossfadeAudio != null)
        {
            _crossfadeAudio.Stop();
            _crossfadeAudio.clip = null;
        }
        // === DCGO-CUSTOM:matchmusic end ===

        // === DCGO-CUSTOM:android begin ===
        _audio.Stop();
        // === DCGO-CUSTOM:android end ===

        _audio.clip = null;

        isPlaying = false;
    }

    public void StartPlayBGM(AudioClip clip)
    {
        _audio = GetComponent<AudioSource>();

        // === DCGO-CUSTOM:matchmusic begin ===
        StopCrossfadeRoutine();
        if (_crossfadeAudio != null && _crossfadeAudio.isPlaying)
        {
            _crossfadeAudio.Stop();
            _crossfadeAudio.clip = null;
        }
        // === DCGO-CUSTOM:matchmusic end ===

        if (clip != null)
        {
            _audio.clip = clip;
        }

        // === DCGO-CUSTOM:android begin ===
        // Lower number = higher priority; keep BGM above SE pool (priority ~200).
        _audio.priority = 32;
        // === DCGO-CUSTOM:android end ===

        if (ContinuousController.instance != null)
        {
            ContinuousController.instance.ChangeBGMVolume(_audio);
        }

        _audio.Play();

        isPlaying = true;
    }

    private void Update()
    {
        if (ContinuousController.instance != null)
        {
            if (_audio != null && isPlaying && !isFading)
            {
                ContinuousController.instance.ChangeBGMVolume(_audio);
            }
        }
    }

    public IEnumerator FadeOut(float duration)
    {
        _audio = GetComponent<AudioSource>();

        // === DCGO-CUSTOM:matchmusic begin ===
        StopCrossfadeRoutine();
        if (_crossfadeAudio != null && _crossfadeAudio.isPlaying)
        {
            _crossfadeAudio.DOKill();
            _crossfadeAudio.Stop();
            _crossfadeAudio.clip = null;
        }
        // === DCGO-CUSTOM:matchmusic end ===

        bool end = false;
        isFading = true;

        var sequence = DOTween.Sequence();

        sequence
            .Append(DOTween.To(() => _audio.volume, (value) => _audio.volume = value, 0, duration))
            .AppendCallback(() => end = true);

        sequence.Play();

        yield return new WaitWhile(() => !end);

        // === DCGO-CUSTOM:android begin ===
        _audio.Stop();
        // === DCGO-CUSTOM:android end ===
        isPlaying = false;
        isFading = false;
    }

    // === DCGO-CUSTOM:matchmusic begin ===
    /// <summary>
    /// Crossfades to a new clip on a secondary AudioSource, then swaps to the primary.
    /// No-ops if the same clip is already playing on the active source.
    /// </summary>
    public IEnumerator CrossfadeTo(AudioClip clip, float duration)
    {
        if (clip == null)
            yield break;

        _audio = GetComponent<AudioSource>();
        EnsureCrossfadeSource();

        AudioSource active = (_crossfadeAudio != null && _crossfadeAudio.isPlaying && !_audio.isPlaying)
            ? _crossfadeAudio
            : _audio;

        if (active.isPlaying && active.clip == clip)
            yield break;

        StopCrossfadeRoutine();
        _crossfadeRoutine = ContinuousController.instance != null
            ? ContinuousController.instance.StartCoroutine(CrossfadeRoutine(clip, duration))
            : StartCoroutine(CrossfadeRoutine(clip, duration));

        // Wait until this object's fade flag clears (routine sets isFading).
        yield return new WaitWhile(() => isFading);
    }

    /// <summary>Fire-and-forget crossfade (preferred for reactive music switches).</summary>
    public void CrossfadeToClip(AudioClip clip, float duration = 0.6f)
    {
        if (clip == null)
            return;

        _audio = GetComponent<AudioSource>();
        EnsureCrossfadeSource();

        AudioSource active = (_crossfadeAudio != null && _crossfadeAudio.isPlaying && !_audio.isPlaying)
            ? _crossfadeAudio
            : _audio;

        if (active.isPlaying && active.clip == clip)
            return;

        StopCrossfadeRoutine();

        if (ContinuousController.instance != null)
            _crossfadeRoutine = ContinuousController.instance.StartCoroutine(CrossfadeRoutine(clip, duration));
        else
            _crossfadeRoutine = StartCoroutine(CrossfadeRoutine(clip, duration));
    }

    IEnumerator CrossfadeRoutine(AudioClip clip, float duration)
    {
        _audio = GetComponent<AudioSource>();
        EnsureCrossfadeSource();

        isFading = true;
        isPlaying = true;

        float targetVolume = 0.08f;
        if (ContinuousController.instance != null)
        {
            // Match ChangeBGMVolume scaling without writing yet.
            targetVolume = ContinuousController.instance.BGMVolume * 0.25f * 0.8f;
        }

        AudioSource fadeOut = _audio.isPlaying ? _audio : (_crossfadeAudio.isPlaying ? _crossfadeAudio : null);
        AudioSource fadeIn = fadeOut == _audio ? _crossfadeAudio : _audio;

        // If nothing is playing, just start on primary.
        if (fadeOut == null || fadeOut.clip == null)
        {
            _audio.clip = clip;
            _audio.priority = 32;
            _audio.loop = true;
            if (ContinuousController.instance != null)
                ContinuousController.instance.ChangeBGMVolume(_audio);
            _audio.Play();
            if (_crossfadeAudio != null)
            {
                _crossfadeAudio.Stop();
                _crossfadeAudio.clip = null;
            }
            isFading = false;
            _crossfadeRoutine = null;
            yield break;
        }

        fadeIn.clip = clip;
        fadeIn.priority = 32;
        fadeIn.loop = true;
        fadeIn.volume = 0f;
        fadeIn.Play();

        float outStart = fadeOut.volume;
        float elapsed = 0f;
        float dur = Mathf.Max(0.01f, duration);

        while (elapsed < dur)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / dur);
            fadeOut.volume = Mathf.Lerp(outStart, 0f, t);
            fadeIn.volume = Mathf.Lerp(0f, targetVolume, t);
            yield return null;
        }

        fadeOut.Stop();
        fadeOut.clip = null;
        fadeOut.volume = 0f;

        // Keep primary as the "active" source for Update volume apply.
        if (fadeIn != _audio)
        {
            _audio.clip = fadeIn.clip;
            _audio.time = fadeIn.time;
            _audio.priority = 32;
            _audio.loop = true;
            _audio.volume = targetVolume;
            _audio.Play();
            // Sync playhead as closely as possible.
            try { _audio.time = fadeIn.time; } catch { /* ignore invalid time */ }
            fadeIn.Stop();
            fadeIn.clip = null;
            fadeIn.volume = 0f;
        }
        else if (ContinuousController.instance != null)
        {
            ContinuousController.instance.ChangeBGMVolume(_audio);
        }

        isFading = false;
        isPlaying = true;
        _crossfadeRoutine = null;
    }

    void EnsureCrossfadeSource()
    {
        if (_crossfadeAudio != null)
            return;

        _crossfadeAudio = gameObject.AddComponent<AudioSource>();
        _crossfadeAudio.playOnAwake = false;
        _crossfadeAudio.loop = true;
        _crossfadeAudio.priority = 32;
        _crossfadeAudio.spatialBlend = 0f;
    }

    void StopCrossfadeRoutine()
    {
        if (_crossfadeRoutine != null)
        {
            if (ContinuousController.instance != null)
                ContinuousController.instance.StopCoroutine(_crossfadeRoutine);
            else
                StopCoroutine(_crossfadeRoutine);
            _crossfadeRoutine = null;
        }
        isFading = false;
    }
    // === DCGO-CUSTOM:matchmusic end ===
}
