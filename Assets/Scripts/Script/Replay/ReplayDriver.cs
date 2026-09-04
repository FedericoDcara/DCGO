using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Feeds recorded match events into player queues during replay playback.
/// Injects at most one pending item per queue; never waits for MainPhase
/// consumption (that caused deadlocks while Active/Draw/Breeding still ran).
/// </summary>
public class ReplayDriver : MonoBehaviour
{
    public static ReplayDriver Instance { get; private set; }

    ReplayData _data;
    int _cursor;
    bool _running;
    bool _paused;
    float _speed = 1f;
    int _currentTurn;
    int _seekTargetTurn;
    bool _seeking;
    float _savedSpeed = 1f;

    public bool IsPaused => _paused;
    public float Speed => _speed;
    public int CurrentTurn => _currentTurn;
    public int EventIndex => _cursor;
    public int EventCount => _data != null && _data.events != null ? _data.events.Count : 0;
    public ReplayData Data => _data;
    public bool IsSeeking => _seeking;

    public static readonly float[] SpeedSteps = { 1f, 2f, 4f };

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        Time.timeScale = 1f;
    }

    void Start()
    {
        var cc = ContinuousController.instance;
        if (cc == null || !cc.isReplay || cc.ActiveReplay == null)
        {
            Debug.LogWarning("[Replay] ReplayDriver started without ActiveReplay.");
            enabled = false;
            return;
        }

        _data = cc.ActiveReplay;
        _cursor = 0;
        _currentTurn = 0;
        _seekTargetTurn = cc.ReplaySeekTurn;
        _seeking = _seekTargetTurn > 0;
        if (_seeking)
        {
            _savedSpeed = 1f;
            SetSpeedInternal(8f);
        }
        else
        {
            SetSpeedInternal(1f);
        }

        _running = true;
        DisableLiveInputs();
        StartCoroutine(FeedCoroutine());
        StartCoroutine(SeekWatchCoroutine());
    }

    static void DisableLiveInputs()
    {
        if (GManager.instance == null)
        {
            return;
        }

        if (GManager.instance.nextPhaseButton != null)
        {
            GManager.instance.nextPhaseButton.gameObject.SetActive(false);
        }

        if (GManager.instance.sideBar != null)
        {
            GManager.instance.sideBar.OffSideBar(false);
        }
    }

    IEnumerator SeekWatchCoroutine()
    {
        while (_running)
        {
            if (GManager.instance != null &&
                GManager.instance.turnStateMachine != null &&
                GManager.instance.turnStateMachine.DoneStartGame)
            {
                int actualTurn = GManager.instance.turnStateMachine.TurnCount;
                if (actualTurn > 0)
                {
                    _currentTurn = actualTurn;
                }

                if (_seeking && actualTurn >= _seekTargetTurn && _seekTargetTurn > 0)
                {
                    FinishSeek();
                }
            }

            yield return null;
        }
    }

    IEnumerator FeedCoroutine()
    {
        yield return new WaitUntil(() =>
            GManager.instance != null &&
            GManager.instance.turnStateMachine != null &&
            GManager.instance.turnStateMachine.gameContext != null);

        DisableLiveInputs();

        while (_running && _data != null && _cursor < _data.events.Count)
        {
            yield return WaitWhilePaused();

            if (IsEndGame())
            {
                yield break;
            }

            var evt = _data.events[_cursor];
            if (evt == null)
            {
                _cursor++;
                continue;
            }

            switch (evt.EventType)
            {
                case ReplayEventType.TurnMarker:
                    // Markers are for the timeline index only; seek completion uses live TurnCount.
                    _cursor++;
                    break;

                case ReplayEventType.MainPhaseAction:
                    yield return InjectMainPhase(evt);
                    _cursor++;
                    break;

                case ReplayEventType.SelectionValue:
                case ReplayEventType.SelectionCard:
                case ReplayEventType.SelectionPermanent:
                    yield return InjectSelection(evt);
                    _cursor++;
                    break;

                case ReplayEventType.Surrender:
                    yield return InjectSurrender(evt);
                    _cursor++;
                    break;

                default:
                    _cursor++;
                    break;
            }
        }
    }

    IEnumerator WaitWhilePaused()
    {
        while (_paused && !_seeking)
        {
            if (IsEndGame())
            {
                yield break;
            }

            yield return null;
        }
    }

    static bool IsEndGame()
    {
        return GManager.instance != null &&
               GManager.instance.turnStateMachine != null &&
               GManager.instance.turnStateMachine.endGame;
    }

    IEnumerator InjectMainPhase(ReplayEventDto evt)
    {
        Player player = ResolvePlayer(evt.playerId);
        if (player == null)
        {
            yield break;
        }

        float waited = 0f;
        while (true)
        {
            yield return WaitWhilePaused();
            if (IsEndGame())
            {
                yield break;
            }

            if (!player.HasMainPhaseAction())
            {
                break;
            }

            waited += Time.unscaledDeltaTime;
            if (waited > 20f)
            {
                Debug.LogError($"[Replay] Stall: waiting for main-phase queue to clear player={evt.playerId} cursor={_cursor}. " +
                               "Likely RNG desync (bad seed) or a missing selection earlier.");
                waited = 0f;
            }

            yield return null;
        }

        var action = GamePacketFactory.Create(evt.packetId, evt.GetPayloadBytes()) as MainPhaseAction;
        if (action == null)
        {
            Debug.LogWarning($"[Replay] Failed to recreate main phase action packetId={evt.packetId}");
            yield break;
        }

        player.QueueMainPhaseAction(action);
    }

    IEnumerator InjectSelection(ReplayEventDto evt)
    {
        Player player = ResolvePlayer(evt.playerId);
        if (player == null)
        {
            yield break;
        }

        float waited = 0f;
        while (true)
        {
            yield return WaitWhilePaused();
            if (IsEndGame())
            {
                yield break;
            }

            if (!player.HasPlayerSelection())
            {
                break;
            }

            waited += Time.unscaledDeltaTime;
            if (waited > 20f)
            {
                Debug.LogError($"[Replay] Stall: waiting for selection queue to clear player={evt.playerId} type={evt.EventType} cursor={_cursor}.");
                waited = 0f;
            }

            yield return null;
        }

        var selection = evt.ToSelection();
        if (selection == null)
        {
            Debug.LogWarning($"[Replay] Failed to recreate selection type={evt.EventType}");
            yield break;
        }

        player.QueuePlayerSelection(selection);
    }

    IEnumerator InjectSurrender(ReplayEventDto evt)
    {
        yield return new WaitUntil(() =>
            GManager.instance != null &&
            GManager.instance.turnStateMachine != null &&
            (GManager.instance.turnStateMachine.DoneStartGame || IsEndGame()));

        if (IsEndGame())
        {
            yield break;
        }

        GManager.instance.turnStateMachine.Surrender(evt.playerId);
    }

    static Player ResolvePlayer(int playerId)
    {
        if (GManager.instance == null)
        {
            return null;
        }

        return GManager.instance.GetPlayerFromID(playerId);
    }

    public void TogglePause()
    {
        if (_seeking)
        {
            return;
        }

        SetPaused(!_paused);
    }

    public void SetPaused(bool paused)
    {
        _paused = paused;
        ApplyTimeScale();
    }

    public void CycleSpeed()
    {
        if (_seeking)
        {
            return;
        }

        int idx = 0;
        for (int i = 0; i < SpeedSteps.Length; i++)
        {
            if (Mathf.Approximately(_speed, SpeedSteps[i]))
            {
                idx = i;
                break;
            }
        }

        idx = (idx + 1) % SpeedSteps.Length;
        SetSpeed(SpeedSteps[idx]);
    }

    public void SetSpeed(float speed)
    {
        if (_seeking)
        {
            _savedSpeed = speed;
            return;
        }

        SetSpeedInternal(speed);
    }

    void SetSpeedInternal(float speed)
    {
        _speed = Mathf.Max(0.25f, speed);
        ApplyTimeScale();
    }

    void ApplyTimeScale()
    {
        if (_paused && !_seeking)
        {
            Time.timeScale = 0f;
        }
        else
        {
            Time.timeScale = _speed;
        }
    }

    void FinishSeek()
    {
        _seeking = false;
        if (ContinuousController.instance != null)
        {
            ContinuousController.instance.ReplaySeekTurn = 0;
        }

        SetSpeedInternal(_savedSpeed > 0f ? _savedSpeed : 1f);
        SetPaused(true);
        Debug.Log($"[Replay] Seek finished at turn {_currentTurn}");
    }

    public void CancelSeekForExit()
    {
        _seeking = false;
        _seekTargetTurn = 0;
        _paused = false;
        _running = false;
        Time.timeScale = 1f;
    }

    /// <summary>
    /// Forward seek = fast-forward in place (no scene reload).
    /// Backward seek = reload BattleScene only (board state cannot rewind).
    /// </summary>
    public void SeekToTurn(int turn)
    {
        if (_data == null || turn < 1)
        {
            return;
        }

        if (_seeking)
        {
            // Retarget an in-progress forward seek.
            if (turn > _currentTurn)
            {
                _seekTargetTurn = turn;
            }

            return;
        }

        if (turn == _currentTurn)
        {
            return;
        }

        // Forward: no scene reload — just run hot until the turn marker.
        if (turn > _currentTurn)
        {
            _seekTargetTurn = turn;
            _seeking = true;
            _savedSpeed = _speed > 0f ? _speed : 1f;
            SetPaused(false);
            SetSpeedInternal(8f);
            if (ContinuousController.instance != null)
            {
                ContinuousController.instance.ReplaySeekTurn = turn;
            }

            Debug.Log($"[Replay] Fast-forward seek to turn {turn}");
            return;
        }

        // Backward: must rebuild state from the start of the match.
        var cc = ContinuousController.instance;
        if (cc == null)
        {
            return;
        }

        Time.timeScale = 1f;
        cc.StartCoroutine(cc.ReloadReplayBattleCoroutine(_data, turn));
    }

    public List<int> GetTurnList()
    {
        return _data != null ? _data.GetTurnMarkers() : new List<int>();
    }
}
