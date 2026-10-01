using System.Collections;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Fast-forwards a mid-game spectator from a MatchRecorder snapshot, then keeps
/// applying live recorder appends. Photon battle RPCs are never the source of
/// truth — mixing them with the stream desyncs optional-effect WaitUntils.
/// </summary>
public class SpectatorCatchUpDriver : MonoBehaviour
{
    public static SpectatorCatchUpDriver Instance { get; private set; }

    ReplayData _data;
    int _cursor;
    bool _running;
    bool _caughtUp;
    bool _cancelled;
    const float CatchUpTimeScale = 8f;
    const int LiveSpeedLag = 3;

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
        if (cc == null || !cc.isSpectatorCatchUp || cc.ActiveCatchUpReplay == null)
        {
            Debug.LogWarning("[Tournament] SpectatorCatchUpDriver started without catch-up data.");
            enabled = false;
            return;
        }

        _data = cc.ActiveCatchUpReplay;
        _cursor = 0;
        _running = true;
        SpectatorInputGate.StreamOnly = true;
        SpectatorInputGate.Injecting = false;

        bool hasEvents = _data.events != null && _data.events.Count > 0;
        if (hasEvents)
        {
            Time.timeScale = CatchUpTimeScale;
        }

        DisableLiveInputs();
        StartCoroutine(FeedCoroutine());
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

    IEnumerator FeedCoroutine()
    {
        yield return new WaitUntil(() =>
            GManager.instance != null &&
            GManager.instance.turnStateMachine != null &&
            GManager.instance.turnStateMachine.gameContext != null);

        DisableLiveInputs();
        PhotonNetwork.IsMessageQueueRunning = true;

        float idle = 0f;
        while (_running && !IsEndGame())
        {
            SpectatorInputGate.StreamOnly = true;

            if (HasPendingEvent())
            {
                idle = 0f;
                yield return InjectNext();
                continue;
            }

            Time.timeScale = 1f;
            idle += Time.unscaledDeltaTime;

            if (!_caughtUp &&
                GManager.instance.turnStateMachine.DoneStartGame &&
                !HasPendingInjections())
            {
                _caughtUp = true;
                Debug.Log($"[Tournament] Spectator caught up at event {_cursor} — following recorder stream");
            }

            if (idle > 20f)
            {
                Debug.Log($"[Tournament] Spectator waiting for stream event {_cursor}");
                idle = 0f;
            }

            yield return null;
        }

        Time.timeScale = 1f;
    }

    bool HasPendingEvent()
    {
        return _data != null && _data.events != null && _cursor < _data.events.Count;
    }

    static bool HasPendingInjections()
    {
        if (GManager.instance == null)
        {
            return false;
        }

        for (int id = 0; id <= 1; id++)
        {
            var player = GManager.instance.GetPlayerFromID(id);
            if (player != null && (player.HasMainPhaseAction() || player.HasPlayerSelection()))
            {
                return true;
            }
        }

        return false;
    }

    IEnumerator InjectNext()
    {
        Time.timeScale = _data.events.Count - _cursor > LiveSpeedLag ? CatchUpTimeScale : 1f;

        var evt = _data.events[_cursor];
        if (evt != null)
        {
            switch (evt.EventType)
            {
                case ReplayEventType.TurnMarker:
                    break;

                case ReplayEventType.MainPhaseAction:
                    yield return InjectMainPhase(evt);
                    break;

                case ReplayEventType.SelectionValue:
                case ReplayEventType.SelectionCard:
                case ReplayEventType.SelectionPermanent:
                    yield return InjectSelection(evt);
                    break;

                case ReplayEventType.Surrender:
                    yield return InjectSurrender(evt);
                    break;
            }
        }

        _cursor++;
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

        ReplayDriver.DiscardOrphanSelections(player);
        yield return WaitForEmptyQueue(() => player.HasMainPhaseAction(), "main-phase", evt.playerId);
        if (IsEndGame() || !_running)
        {
            yield break;
        }

        var action = GamePacketFactory.Create(evt.packetId, evt.GetPayloadBytes()) as MainPhaseAction;
        if (action == null)
        {
            Debug.LogWarning($"[Tournament] Catch-up failed to recreate main phase packetId={evt.packetId}");
            yield break;
        }

        Inject(() => player.QueueMainPhaseAction(action));
    }

    IEnumerator InjectSelection(ReplayEventDto evt)
    {
        Player player = ResolvePlayer(evt.playerId);
        if (player == null)
        {
            yield break;
        }

        yield return WaitForEmptyQueue(() => player.HasPlayerSelection(), "selection", evt.playerId);
        if (IsEndGame() || !_running)
        {
            yield break;
        }

        var selection = evt.ToSelection();
        if (selection == null)
        {
            Debug.LogWarning($"[Tournament] Catch-up failed to recreate selection type={evt.EventType}");
            yield break;
        }

        Inject(() => player.QueuePlayerSelection(selection));
    }

    IEnumerator InjectSurrender(ReplayEventDto evt)
    {
        yield return new WaitUntil(() =>
            GManager.instance != null &&
            GManager.instance.turnStateMachine != null &&
            (GManager.instance.turnStateMachine.DoneStartGame || IsEndGame() || !_running));

        if (IsEndGame() || !_running)
        {
            yield break;
        }

        Inject(() => GManager.instance.turnStateMachine.Surrender(evt.playerId));
    }

    IEnumerator WaitForEmptyQueue(System.Func<bool> hasPending, string label, int playerId)
    {
        float waited = 0f;
        while (_running && !IsEndGame() && hasPending())
        {
            yield return null;

            if (label == "selection")
            {
                ReplayDriver.DiscardOrphanSelections(ResolvePlayer(playerId));
            }

            if (!hasPending())
            {
                break;
            }

            waited += Time.unscaledDeltaTime;
            if (waited > 20f)
            {
                Debug.LogError(
                    $"[Tournament] Catch-up stall waiting for {label} queue player={playerId} cursor={_cursor}");
                waited = 0f;
            }
        }
    }

    static void Inject(System.Action queue)
    {
        SpectatorInputGate.Injecting = true;
        try
        {
            queue();
        }
        finally
        {
            SpectatorInputGate.Injecting = false;
        }
    }

    static Player ResolvePlayer(int playerId)
    {
        if (GManager.instance == null)
        {
            return null;
        }

        return GManager.instance.GetPlayerFromID(playerId);
    }

    public void Cancel()
    {
        _cancelled = true;
        _running = false;
        Time.timeScale = 1f;
    }
}
