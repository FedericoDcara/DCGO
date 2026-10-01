using System.Collections;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// Runs a 2-player tournament match room: single-elimination game, routing after the match.
/// Also supports read-only spectators joining the same Photon room.
/// </summary>
public class TournamentMatchDirector : MonoBehaviourPunCallbacks
{
    private static readonly WaitForSeconds Wait01 = new WaitForSeconds(0.1f);
    private static readonly WaitForSeconds Wait1 = new WaitForSeconds(1f);

    public bool InMatchRoom { get; private set; }
    public bool ShouldReloadNextGame { get; private set; }
    public bool RoutingAfterSeries { get; private set; }
    public bool IsSpectating { get; private set; }

    int _round;
    int _matchIndex;
    bool _startingBattle;
    bool _joinFailed;
    bool _createFailed;
    bool _joinOrCreatePending;
    bool _autoAdvancingResult;
    short _lastJoinFailCode;
    string _lastJoinFailMessage;
    string _pendingRoomName;
    Text _seriesOverlay;
    GameObject _leaveSpectateButton;
    Coroutine _autoAdvanceFromResult;

    public void ResetDirector()
    {
        InMatchRoom = false;
        ShouldReloadNextGame = false;
        RoutingAfterSeries = false;
        IsSpectating = false;
        _startingBattle = false;
        CancelAutoAdvanceFromResult();

        _round = 0;
        _matchIndex = 0;
        _pendingRoomName = null;
        Bo3FirstPlayerChoice.Hide();
        DestroyOverlay();
        DestroyLeaveSpectateButton();
        ContinuousController.instance?.ClearTournamentSpectator();
    }

    public IEnumerator JoinMatchRoomCoroutine(int round, int matchIndex)
    {
        var cc = ContinuousController.instance;
        var state = cc != null ? cc.TournamentState : null;
        if (state == null)
        {
            yield break;
        }

        ResetDirector();
        _round = round;
        _matchIndex = matchIndex;
        InMatchRoom = true;
        IsSpectating = false;
        if (cc != null)
        {
            cc.ClearTournamentSpectator();
        }

        var match = state.GetMatch(round, matchIndex);
        if (!TournamentKeys.IsReadyTwoPlayerMatch(match))
        {
            state.ResolveOpeningByes();
            cc.TournamentState = state;
            InMatchRoom = false;
            var next = state.FindActiveMatchFor(TournamentState.EnsureLocalPlayerId());
            int guard = 0;
            while (IsAssignedByeMatch(next) && guard++ < 32)
            {
                state.ResolveOpeningByes();
                next = state.FindActiveMatchFor(TournamentState.EnsureLocalPlayerId());
            }

            if (TournamentKeys.IsReadyTwoPlayerMatch(next))
            {
                yield return JoinMatchRoomCoroutine(next.round, next.matchIndex);
            }
            else
            {
                yield return JoinWaitHubCoroutine();
            }

            yield break;
        }

        string roomName = TournamentKeys.MatchRoomName(state.tourneyId, round, matchIndex, state.useBanlist);
        _pendingRoomName = roomName;

        var options = new RoomOptions
        {
            IsVisible = false,
            IsOpen = true,
            PublishUserId = true,
            MaxPlayers = (byte)TournamentKeys.MatchRoomMaxPlayers(state.ResolvedPlayerCount),
            EmptyRoomTtl = 120000,
            CustomRoomProperties = new Hashtable
            {
                { TournamentKeys.ModeProperty, TournamentKeys.ModeTournament },
                { TournamentKeys.TourneyIdProperty, state.tourneyId },
                { TournamentKeys.UseBanlistProperty, state.useBanlist },
                { TournamentKeys.RoomKindProperty, TournamentKeys.RoomKindMatch },
                { TournamentKeys.RoundProperty, round },
                { TournamentKeys.MatchIndexProperty, matchIndex },
                { TournamentKeys.UserIdAProperty, match != null ? match.userIdA ?? "" : "" },
                { TournamentKeys.UserIdBProperty, match != null ? match.userIdB ?? "" : "" },
                { TournamentKeys.SeriesWinsAProperty, match != null ? match.seriesWinsA : 0 },
                { TournamentKeys.SeriesWinsBProperty, match != null ? match.seriesWinsB : 0 },
                { TournamentKeys.GameIndexProperty, match != null ? match.gameIndex : 0 },
                { TournamentKeys.PlayerCountProperty, state.ResolvedPlayerCount },
                { "RoomCreator", PhotonNetwork.NickName },
            },
            CustomRoomPropertiesForLobby = new[]
            {
                TournamentKeys.ModeProperty,
                TournamentKeys.TourneyIdProperty,
            },
        };
        BattleReconnectService.ApplyBattleTtl(options);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.Name == roomName)
            {
                break;
            }

            yield return JoinOrCreateNamedRoom(roomName, options);
            if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.Name == roomName)
            {
                break;
            }

            yield return Wait01;
        }

        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom.Name != roomName)
        {
            Debug.LogWarning($"[Tournament] Failed to join match room {roomName} (now in {PhotonNetwork.CurrentRoom?.Name}) — falling back to wait hub");
            InMatchRoom = false;
            yield return JoinWaitHubCoroutine();
            yield break;
        }

        // Competitors may raise MaxPlayers if room was created by an older client.
        if (PhotonNetwork.IsMasterClient)
        {
            byte cap = (byte)TournamentKeys.MatchRoomMaxPlayers(state.ResolvedPlayerCount);
            PhotonNetwork.CurrentRoom.MaxPlayers = cap;
            PhotonNetwork.CurrentRoom.IsOpen = true;
            PhotonNetwork.CurrentRoom.IsVisible = false;
        }

        WriteMatchRole(TournamentKeys.RoleCompetitor);
        SyncMatchPropsFromRoom();
        EnsureLockedDeckProperty();
        Opening.instance?.battle?.tournamentLobbyManager?.ShowMatchWaiting(_round, _matchIndex);

        float wait = 0f;
        const float assignedOpponentTimeout = 45f * 60f;
        int maxActiveSeen = TournamentKeys.CountActiveCompetitors();
        while (PhotonNetwork.InRoom && TournamentKeys.CountActiveCompetitors() < 2)
        {
            int active = TournamentKeys.CountActiveCompetitors();
            if (active > maxActiveSeen)
            {
                maxActiveSeen = active;
            }

            var waitingMatch = state.GetMatch(_round, _matchIndex);
            bool opponentSeatEmpty = waitingMatch != null &&
                (string.IsNullOrEmpty(waitingMatch.userIdA) || string.IsNullOrEmpty(waitingMatch.userIdB));
            // Bye winners sit in the next match room until the feeder series finishes.
            // Never forfeit that wait — a Bo3 can last far longer than a few minutes.
            if (TournamentKeys.HasInactiveCompetitor())
            {
                wait += Time.unscaledDeltaTime;
                yield return null;
                continue;
            }

            if (!opponentSeatEmpty && maxActiveSeen >= 2)
            {
                break;
            }

            if (!opponentSeatEmpty && wait >= assignedOpponentTimeout)
            {
                break;
            }

            wait += Time.unscaledDeltaTime;
            yield return null;
        }

        if (!PhotonNetwork.InRoom)
        {
            InMatchRoom = false;
            yield break;
        }

        if (TournamentKeys.CountActiveCompetitors() < 2)
        {
            InMatchRoom = false;
            yield return JoinWaitHubCoroutine();
            yield break;
        }

        yield return StartBattleCoroutine(isRematch: match != null && match.gameIndex > 0);
    }

    /// <summary>Join an existing match room as a read-only observer (does not create the room).</summary>
    public IEnumerator JoinMatchAsSpectatorCoroutine(int round, int matchIndex)
    {
        var cc = ContinuousController.instance;
        var state = cc != null ? cc.TournamentState : null;
        if (state == null)
        {
            yield break;
        }

        var match = state.GetMatch(round, matchIndex);
        if (!TournamentKeys.IsReadyTwoPlayerMatch(match))
        {
            Debug.LogWarning("[Tournament] Spectate aborted — match not ready");
            yield return JoinWaitHubCoroutine();
            yield break;
        }

        ResetDirector();
        _round = round;
        _matchIndex = matchIndex;
        InMatchRoom = true;
        IsSpectating = true;
        cc.isTournamentSpectator = true;
        cc.TournamentSpectateViewerUserId = match.userIdA;

        string roomName = TournamentKeys.MatchRoomName(state.tourneyId, round, matchIndex, state.useBanlist);
        _pendingRoomName = roomName;

        Debug.Log($"[Tournament] Spectate joining room {roomName}");
        Opening.instance?.battle?.tournamentLobbyManager?.ShowSpectateWaiting(
            _round,
            _matchIndex,
            "connecting to match…",
            "試合ルームに接続中…");

        // Competitors may still be leaving the wait hub — retry JoinRoom for a while.
        // Keep the message queue running until we are actually inside the match room
        // (paused queue can delay join callbacks).
        PhotonNetwork.IsMessageQueueRunning = true;
        const float joinRetrySeconds = 60f;
        float joinWait = 0f;
        _lastJoinFailCode = 0;
        _lastJoinFailMessage = null;
        while (joinWait < joinRetrySeconds)
        {
            yield return JoinNamedRoomOnly(roomName);
            if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.Name == roomName)
            {
                break;
            }

            string failHint = DescribeJoinFail(_lastJoinFailCode, _lastJoinFailMessage);
            Opening.instance?.battle?.tournamentLobbyManager?.ShowSpectateWaiting(
                _round,
                _matchIndex,
                $"waiting for match room… ({failHint})",
                $"試合ルーム待機中… ({failHint})");
            Debug.LogWarning(
                $"[Tournament] Spectate join retry room={roomName} code={_lastJoinFailCode} msg={_lastJoinFailMessage}");
            joinWait += 2f;
            yield return new WaitForSecondsRealtime(2f);
        }

        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom.Name != roomName)
        {
            string failHint = DescribeJoinFail(_lastJoinFailCode, _lastJoinFailMessage);
            Debug.LogWarning(
                $"[Tournament] Spectate join failed for {roomName} " +
                $"(inRoom={PhotonNetwork.InRoom} now={PhotonNetwork.CurrentRoom?.Name} {failHint})");
            IsSpectating = false;
            cc.ClearTournamentSpectator();
            InMatchRoom = false;
            PhotonNetwork.IsMessageQueueRunning = true;
            yield return JoinWaitHubCoroutine();
            Opening.instance?.battle?.tournamentLobbyManager?.ShowSpectateJoinFailed(failHint);
            yield break;
        }

        WriteMatchRole(TournamentKeys.RoleSpectator);
        TournamentKeys.EnsureMasterIsCompetitor();
        if (PhotonNetwork.IsMasterClient)
        {
            // Should be rare — transfer should have moved master to a competitor.
            PhotonNetwork.CurrentRoom.IsOpen = true;
            byte cap = (byte)TournamentKeys.MatchRoomMaxPlayers(state.ResolvedPlayerCount);
            PhotonNetwork.CurrentRoom.MaxPlayers = cap;
        }

        SyncMatchPropsFromRoom();
        ApplySpectatorDeckFromPov(match);
        if (cc.BattleDeckData == null || !cc.BattleDeckData.IsValidDeckData())
        {
            foreach (var p in PhotonNetwork.PlayerList)
            {
                if (!TournamentKeys.IsCompetitor(p))
                {
                    continue;
                }

                string code = TournamentState.ReadDeckCode(p);
                if (!string.IsNullOrEmpty(code))
                {
                    try
                    {
                        cc.BattleDeckData = new DeckData(code);
                        break;
                    }
                    catch
                    {
                        // try next
                    }
                }
            }
        }

        ShowLeaveSpectateButton();
        SpectatorCatchUpTransfer.EnsureExists();

        Opening.instance?.battle?.tournamentLobbyManager?.ShowSpectateWaiting(
            _round,
            _matchIndex,
            "waiting for the duel to start…",
            "試合開始を待っています…");

        // The board is rebuilt from the fighters' recorder stream, so we can only attach once
        // one of them has a snapshot to send. That covers game 1 and every Bo3 rematch alike.
        float nextStreamRetryAt = 0f;
        while (PhotonNetwork.InRoom && IsSpectating)
        {
            SyncMatchPropsFromRoom();
            state = cc.TournamentState;
            match = state != null ? state.GetMatch(_round, _matchIndex) : null;
            if (match == null || match.complete || (state != null && state.finished))
            {
                Debug.Log("[Tournament] Spectate ending — match complete or missing");
                break;
            }

            TournamentKeys.EnsureMasterIsCompetitor();

            if (TournamentKeys.CountActiveCompetitors() < 1)
            {
                // Both fighters left.
                break;
            }

            bool canAttach = CompetitorsReportingBattle() &&
                             GManager.instance == null &&
                             !ContinuousController.IsBattleSceneLoaded();

            if (canAttach && Time.unscaledTime >= nextStreamRetryAt)
            {
                nextStreamRetryAt = Time.unscaledTime + 2f;
                Opening.instance?.battle?.tournamentLobbyManager?.ShowSpectateWaiting(
                    _round,
                    _matchIndex,
                    "catching up to the duel…",
                    "試合に追いついています…");

                yield return TrySpectatorCatchUpStartBattle(match);
                if (GManager.instance != null || ContinuousController.IsBattleSceneLoaded())
                {
                    // In battle — EndBattle / LeaveSpectate will route afterward.
                    yield break;
                }
            }

            yield return null;
        }

        DestroyLeaveSpectateButton();
        IsSpectating = false;
        cc.ClearTournamentSpectator();
        InMatchRoom = false;
        yield return JoinWaitHubCoroutine();
    }

    IEnumerator TrySpectatorCatchUpStartBattle(TournamentMatchSlot match)
    {
        var cc = ContinuousController.instance;
        if (cc == null)
        {
            yield break;
        }

        // RaiseEvents only dispatch while the queue runs. Pause it after the snapshot
        // so BattleScene PhotonViews exist before buffered OwnershipUpdates apply.
        PhotonNetwork.IsMessageQueueRunning = true;
        ReplayData catchUpData = null;
        yield return SpectatorCatchUpTransfer.RequestCatchUpCoroutine(
            data => catchUpData = data,
            _ => { });

        if (catchUpData == null || !catchUpData.HasInitialLibrarySnapshot())
        {
            cc.ClearCatchUp();
            yield break;
        }

        cc.isSpectatorCatchUp = true;
        cc.ActiveCatchUpReplay = catchUpData;
        PhotonNetwork.IsMessageQueueRunning = false;

        Opening.instance?.battle?.tournamentLobbyManager?.ShowSpectateWaiting(
            _round,
            _matchIndex,
            "catching up to live game…",
            "ライブ試合に追いついています…");

        yield return StartBattleCoroutine(isRematch: IsCurrentMatchRematch(match));
        if (GManager.instance == null && !ContinuousController.IsBattleSceneLoaded())
        {
            Debug.LogWarning("[Tournament] Catch-up StartBattle failed");
            PhotonNetwork.IsMessageQueueRunning = true;
            SpectatorCatchUpTransfer.StopStream();
            cc.ClearCatchUp();
        }
    }

    static bool IsCurrentMatchRematch(TournamentMatchSlot match)
    {
        if (PhotonNetwork.InRoom &&
            PhotonNetwork.CurrentRoom.CustomProperties != null &&
            PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(
                TournamentKeys.GameIndexProperty, out object gObj))
        {
            try
            {
                if (System.Convert.ToInt32(gObj) > 0)
                {
                    return true;
                }
            }
            catch
            {
                // fall through
            }
        }

        return match != null && match.gameIndex > 0;
    }

    void SetSpectateWaitingStatus(string message)
    {
        Opening.instance?.battle?.tournamentLobbyManager?.ShowSpectateWaiting(
            _round,
            _matchIndex,
            message ?? "…",
            message ?? "…");
        if (!string.IsNullOrEmpty(message))
        {
            Debug.Log($"[Tournament] {message}");
        }
    }

    static bool CompetitorsReportingBattle()
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.PlayerList == null)
        {
            return false;
        }

        foreach (var p in PhotonNetwork.PlayerList)
        {
            if (!TournamentKeys.IsCompetitor(p))
            {
                continue;
            }

            if (p.CustomProperties != null &&
                p.CustomProperties.TryGetValue("isBattle", out object value) &&
                value is bool inBattle &&
                inBattle)
            {
                return true;
            }
        }

        return false;
    }

    void ApplySpectatorDeckFromPov(TournamentMatchSlot match)
    {
        var cc = ContinuousController.instance;
        if (cc == null || match == null)
        {
            return;
        }

        string viewerId = cc.TournamentSpectateViewerUserId;
        if (string.IsNullOrEmpty(viewerId))
        {
            viewerId = match.userIdA;
            cc.TournamentSpectateViewerUserId = viewerId;
        }

        string code = null;
        foreach (var p in PhotonNetwork.PlayerList)
        {
            if (TournamentState.ReadPlayerId(p) == viewerId)
            {
                code = TournamentState.ReadDeckCode(p);
                break;
            }
        }

        if (string.IsNullOrEmpty(code) && cc.TournamentState != null)
        {
            code = cc.TournamentState.LockedDeckCode(viewerId);
        }

        if (!string.IsNullOrEmpty(code))
        {
            cc.BattleDeckData = new DeckData(code);
        }
    }

    static void WriteMatchRole(string role)
    {
        if (!PhotonNetwork.InRoom)
        {
            return;
        }

        var hash = PhotonNetwork.LocalPlayer.CustomProperties ?? new Hashtable();
        hash[TournamentKeys.RoleProperty] = role;
        hash[TournamentKeys.PlayerIdProperty] = TournamentState.EnsureLocalPlayerId();
        PhotonNetwork.LocalPlayer.SetCustomProperties(hash);
    }

    public void OnClickLeaveSpectate()
    {
        if (!IsSpectating)
        {
            return;
        }

        Opening.instance?.PlayDecisionSE();
        ContinuousController.instance?.StartCoroutine(LeaveSpectateCoroutine());
    }

    IEnumerator LeaveSpectateCoroutine()
    {
        DestroyLeaveSpectateButton();
        SpectatorCatchUpDriver.Instance?.Cancel();
        SpectatorCatchUpTransfer.StopStream();
        Time.timeScale = 1f;
        ContinuousController.instance?.ClearCatchUp();
        PhotonNetwork.IsMessageQueueRunning = true;
        if (GManager.instance != null)
        {
            // Tear down battle without writing tournament results.
            ShouldReloadNextGame = false;
            RoutingAfterSeries = true;
            GManager.instance.ReturnToTitle();
            yield break;
        }

        IsSpectating = false;
        ContinuousController.instance?.ClearTournamentSpectator();
        InMatchRoom = false;
        yield return JoinWaitHubCoroutine();
    }

    public IEnumerator JoinWaitHubCoroutine()
    {
        InMatchRoom = false;
        ShouldReloadNextGame = false;
        IsSpectating = false;
        DestroyLeaveSpectateButton();
        ContinuousController.instance?.ClearTournamentSpectator();
        var cc = ContinuousController.instance;
        var state = cc != null ? cc.TournamentState : null;
        if (state == null)
        {
            yield break;
        }

        string roomName = TournamentKeys.WaitHubRoomName(state.tourneyId, state.useBanlist);
        _pendingRoomName = roomName;

        var options = new RoomOptions
        {
            IsVisible = false,
            IsOpen = true,
            PublishUserId = true,
            MaxPlayers = (byte)TournamentKeys.RoomCapacityForBracket(
                state.ResolvedPlayerCount),
            EmptyRoomTtl = 300000,
            CustomRoomProperties = new Hashtable
            {
                { TournamentKeys.ModeProperty, TournamentKeys.ModeTournament },
                { TournamentKeys.TourneyIdProperty, state.tourneyId },
                { TournamentKeys.UseBanlistProperty, state.useBanlist },
                { TournamentKeys.RoomKindProperty, TournamentKeys.RoomKindWaitHub },
                { TournamentKeys.PlayerCountProperty, state.ResolvedPlayerCount },
                { TournamentKeys.StateProperty, state.ToRoomJson() },
                { TournamentKeys.StartedProperty, true },
                { "RoomCreator", PhotonNetwork.NickName },
            },
            CustomRoomPropertiesForLobby = new[]
            {
                TournamentKeys.ModeProperty,
                TournamentKeys.TourneyIdProperty,
            },
        };
        BattleReconnectService.ApplyBattleTtl(options);

        yield return JoinOrCreateNamedRoom(roomName, options);
        if (!PhotonNetwork.InRoom)
        {
            Debug.LogWarning("[Tournament] Failed to join wait hub");
            Opening.instance?.battle?.tournamentLobbyManager?.ShowWaitHub();
            yield break;
        }

        if (PhotonNetwork.IsMasterClient)
        {
            byte capacity = (byte)TournamentKeys.RoomCapacityForBracket(state.ResolvedPlayerCount);
            if (PhotonNetwork.CurrentRoom.MaxPlayers < capacity)
            {
                PhotonNetwork.CurrentRoom.MaxPlayers = capacity;
            }
        }

        var lobby = Opening.instance?.battle?.tournamentLobbyManager;
        lobby?.ShowWaitHub();
        // Publish first so the bye / waiting player merges the filled final.
        // Do not dispatch from here: EndBattle may still be tearing down, and
        // starting the next match inside that coroutine soft-locks the winner.
        PublishStateToCurrentRoom();
    }

    public void NotifyGameEnded(bool? localWon, bool disconnect, bool draw)
    {
        ShouldReloadNextGame = false;

        var cc = ContinuousController.instance;
        if (cc != null && (cc.isTournamentSpectator || IsSpectating))
        {
            NotifySpectatorGameEnded();
            return;
        }

        var state = cc != null ? cc.TournamentState : null;
        if (state == null)
        {
            Debug.LogWarning("[Tournament] NotifyGameEnded: missing TournamentState");
            return;
        }

        SyncMatchPropsFromRoom();

        var match = state.GetMatch(_round, _matchIndex);
        if (match == null || match.complete)
        {
            match = state.FindActiveMatchFor(TournamentState.EnsureLocalPlayerId());
        }

        if (match != null)
        {
            _round = match.round;
            _matchIndex = match.matchIndex;
        }

        string localId = TournamentState.EnsureLocalPlayerId();
        string winnerId = null;

        if (PhotonNetwork.InRoom && TournamentKeys.CountActiveCompetitors() < 2)
        {
            // Empty match: surrender / disconnect must not start another ghost game.
            ShouldReloadNextGame = false;
            if (match != null && !match.complete && localWon == true)
            {
                state.CompleteMatch(match, localId);
                cc.TournamentState = state;
            }

            Debug.LogWarning("[Tournament] Game ended with fewer than 2 players — no rematch");
            return;
        }

        if (draw)
        {
            // Replay the same game index; series score unchanged.
            ShouldReloadNextGame = match != null && !match.complete;
            Debug.Log($"[Tournament] Draw → rematch game={match?.gameIndex} shouldReload={ShouldReloadNextGame}");
            return;
        }

        if (localWon == true)
        {
            winnerId = localId;
        }
        else if (localWon == false)
        {
            winnerId = OpponentUserId(state, localId, match);
        }
        else if (disconnect)
        {
            if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom && TournamentKeys.CountActiveCompetitors() < 2)
            {
                winnerId = localId;
            }
            else if (!PhotonNetwork.IsConnected)
            {
                winnerId = OpponentUserId(state, localId, match);
            }
        }

        if (match == null)
        {
            Debug.LogWarning("[Tournament] NotifyGameEnded: no active match");
            return;
        }

        if (string.IsNullOrEmpty(winnerId))
        {
            // Prefer staying in the series over wrongly exiting to hub.
            ShouldReloadNextGame = !match.complete;
            Debug.LogWarning($"[Tournament] NotifyGameEnded: unresolved winner, shouldReload={ShouldReloadNextGame}");
            return;
        }

        // Avoid double-counting if room props already reflect this finished game.
        int expectedGameIndex = match.gameIndex;
        bool alreadyApplied =
            PhotonNetwork.InRoom &&
            PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(TournamentKeys.GameIndexProperty, out object gObj) &&
            System.Convert.ToInt32(gObj) > expectedGameIndex;

        bool wouldComplete =
            (match.userIdA == winnerId && match.seriesWinsA + 1 >= TournamentKeys.WinsToTakeSeries) ||
            (match.userIdB == winnerId && match.seriesWinsB + 1 >= TournamentKeys.WinsToTakeSeries);

        if (!alreadyApplied)
        {
            state.ApplyGameResult(_round, _matchIndex, winnerId, wouldComplete);
            cc.TournamentState = state;
        }
        else
        {
            SyncMatchPropsFromRoom();
            if (PhotonNetwork.InRoom &&
                PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(TournamentKeys.StateProperty, out object jsonObj) &&
                jsonObj is string json)
            {
                var incoming = TournamentState.FromJson(json);
                if (incoming != null)
                {
                    state.MergeFrom(incoming);
                    cc.TournamentState = state;
                }
            }

            match = state.GetMatch(_round, _matchIndex);
            wouldComplete = match != null && match.complete;
        }

        if (PhotonNetwork.IsMasterClient && PhotonNetwork.InRoom &&
            !IsSpectating &&
            (ContinuousController.instance == null || !ContinuousController.instance.isTournamentSpectator))
        {
            var updated = state.GetMatch(_round, _matchIndex);
            if (updated != null)
            {
                PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable
                {
                    { TournamentKeys.SeriesWinsAProperty, updated.seriesWinsA },
                    { TournamentKeys.SeriesWinsBProperty, updated.seriesWinsB },
                    { TournamentKeys.GameIndexProperty, updated.gameIndex },
                    { TournamentKeys.LastLoserProperty, updated.lastGameLoserUserId ?? "" },
                    { TournamentKeys.StateProperty, state.ToRoomJson() },
                });
            }
        }

        ShouldReloadNextGame = !wouldComplete && !state.finished;
        Debug.Log(
            $"[Tournament] Game result winner={winnerId} score={match.seriesWinsA}-{match.seriesWinsB} " +
            $"complete={wouldComplete} nextGame={ShouldReloadNextGame}");

        if (wouldComplete && winnerId == localId)
        {
            cc.WinCount++;
            cc.SaveWinCount();
        }
    }

    /// <summary>
    /// After both players see the result, leave automatically so the next
    /// bracket match (or a draw rematch) starts without pressing Return to Title.
    /// </summary>
    public void BeginAutoAdvanceFromResult()
    {
        SetLocalOnResult(true);
        RelabelResultReturnButton();

        if (_autoAdvanceFromResult != null)
        {
            StopCoroutine(_autoAdvanceFromResult);
        }

        _autoAdvanceFromResult = StartCoroutine(AutoAdvanceFromResultCoroutine());
    }

    public void CancelAutoAdvanceFromResult()
    {
        if (_autoAdvanceFromResult != null)
        {
            StopCoroutine(_autoAdvanceFromResult);
            _autoAdvanceFromResult = null;
        }

        _autoAdvancingResult = false;
        Bo3FirstPlayerChoice.Hide();
    }

    IEnumerator AutoAdvanceFromResultCoroutine()
    {
        _autoAdvancingResult = true;
        float start = Time.unscaledTime;

        if (ShouldReloadNextGame)
        {
            yield return WaitForLoserFirstPlayerChoice();
        }

        float shown = Time.unscaledTime - start;
        const float minShowSeconds = 2f;
        while (shown < minShowSeconds)
        {
            shown += Time.unscaledDeltaTime;
            yield return null;
        }

        if (ShouldReloadNextGame && PhotonNetwork.InRoom && TournamentKeys.CountActiveCompetitors() >= 2)
        {
            float waitBoth = 0f;
            const float waitBothTimeout = 8f;
            while (waitBoth < waitBothTimeout && !AllPlayersOnResult())
            {
                if (!PhotonNetwork.InRoom || TournamentKeys.CountActiveCompetitors() < 2)
                {
                    break;
                }

                waitBoth += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        while (ResultObject.HoldAutoLeave)
        {
            if (GManager.instance == null)
            {
                yield break;
            }

            yield return null;
        }

        Bo3FirstPlayerChoice.Hide();
        _autoAdvanceFromResult = null;
        _autoAdvancingResult = false;

        if (ContinuousController.instance == null)
        {
            yield break;
        }

        if (IsSpectating ||
            (ContinuousController.instance != null && ContinuousController.instance.isTournamentSpectator))
        {
            NotifySpectatorGameEnded();
        }

        Debug.Log($"[Tournament] Auto-advance from result rematch={ShouldReloadNextGame}");
        if (GManager.instance != null)
        {
            GManager.instance.ReturnToTitle();
        }
        else
        {
            ContinuousController.instance.EndBattle();
        }
    }

    IEnumerator WaitForLoserFirstPlayerChoice()
    {
        var state = ContinuousController.instance != null ? ContinuousController.instance.TournamentState : null;
        var match = state != null ? state.GetMatch(_round, _matchIndex) : null;
        string localId = TournamentState.EnsureLocalPlayerId();
        if (match == null && state != null)
        {
            match = state.FindActiveMatchFor(localId);
        }

        string loserId = match != null ? match.lastGameLoserUserId : null;
        float waitedLoser = 0f;
        while (string.IsNullOrEmpty(loserId) && waitedLoser < 4f)
        {
            if (PhotonNetwork.InRoom &&
                PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(TournamentKeys.LastLoserProperty, out object loserObj) &&
                loserObj is string roomLoser &&
                !string.IsNullOrEmpty(roomLoser))
            {
                loserId = roomLoser;
                break;
            }

            waitedLoser += Time.unscaledDeltaTime;
            yield return null;
        }

        if (string.IsNullOrEmpty(loserId) || TournamentKeys.IsBye(loserId))
        {
            yield break;
        }

        bool canWrite = !IsSpectating &&
            (ContinuousController.instance == null || !ContinuousController.instance.isTournamentSpectator);
        int gameIndex = match != null ? match.gameIndex : 1;
        if (PhotonNetwork.InRoom &&
            PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(TournamentKeys.GameIndexProperty, out object gObj))
        {
            try
            {
                gameIndex = System.Math.Max(gameIndex, System.Convert.ToInt32(gObj));
            }
            catch
            {
                // keep local gameIndex
            }
        }

        yield return Bo3FirstPlayerChoice.WaitForChoice(
            TournamentKeys.NextFirstUserIdProperty,
            TournamentKeys.NextFirstGameIndexProperty,
            gameIndex,
            localId,
            loserId,
            canWrite);
    }

    static void SetLocalOnResult(bool onResult)
    {
        if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom)
        {
            return;
        }

        var hash = PhotonNetwork.LocalPlayer.CustomProperties ?? new Hashtable();
        hash[TournamentKeys.OnResultProperty] = onResult;
        PhotonNetwork.LocalPlayer.SetCustomProperties(hash);
    }

    static bool AllPlayersOnResult()
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.PlayerList == null)
        {
            return false;
        }

        int competitors = 0;
        for (int i = 0; i < PhotonNetwork.PlayerList.Length; i++)
        {
            var p = PhotonNetwork.PlayerList[i];
            if (p == null || !TournamentKeys.IsCompetitor(p))
            {
                continue;
            }

            competitors++;
            if (!p.CustomProperties.TryGetValue(TournamentKeys.OnResultProperty, out object value) ||
                !(value is bool onResult) ||
                !onResult)
            {
                return false;
            }
        }

        return competitors >= 2;
    }

    /// <summary>Spectators sync rematch vs hub from room bracket state (they do not write results).</summary>
    void NotifySpectatorGameEnded()
    {
        // This game's stream is done; the next game needs a fresh snapshot.
        SpectatorCatchUpTransfer.StopStream();
        SyncMatchPropsFromRoom();
        var state = ContinuousController.instance != null ? ContinuousController.instance.TournamentState : null;
        var match = state != null ? state.GetMatch(_round, _matchIndex) : null;
        if (state != null && state.finished)
        {
            ShouldReloadNextGame = false;
        }
        else if (match == null)
        {
            ShouldReloadNextGame = true;
        }
        else if (match.complete ||
                 match.seriesWinsA >= TournamentKeys.WinsToTakeSeries ||
                 match.seriesWinsB >= TournamentKeys.WinsToTakeSeries)
        {
            ShouldReloadNextGame = false;
        }
        else
        {
            ShouldReloadNextGame = true;
        }

        Debug.Log($"[Tournament] Spectator observed result rematch={ShouldReloadNextGame}");
    }

    static void RelabelResultReturnButton()
    {
        var result = GManager.instance != null ? GManager.instance.resultObject : null;
        if (result == null)
        {
            return;
        }

        var director = TournamentServices.EnsureExists().Match;
        bool rematch = director != null && director.ShouldReloadNextGame;
        string label = rematch ? "Next game starting..." : "Returning...";

        var buttons = result.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null)
            {
                continue;
            }

            bool isReturn = buttons[i].name.IndexOf("Return", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                            buttons[i].name.IndexOf("Title", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isReturn)
            {
                continue;
            }

            buttons[i].interactable = false;

            var texts = buttons[i].GetComponentsInChildren<Text>(true);
            for (int t = 0; t < texts.Length; t++)
            {
                if (texts[t] != null)
                {
                    texts[t].text = label;
                }
            }
        }
    }

    public string FormatSeriesStatusLine()
    {
        var state = ContinuousController.instance != null ? ContinuousController.instance.TournamentState : null;
        var match = state != null ? state.GetMatch(_round, _matchIndex) : null;
        if (match == null)
        {
            return null;
        }

        string localId = TournamentState.EnsureLocalPlayerId();
        if (IsSpectating || (ContinuousController.instance != null && ContinuousController.instance.isTournamentSpectator))
        {
            return "Spectating";
        }

        if (ShouldReloadNextGame)
        {
            return "Draw — rematch starting...";
        }

        if (match.complete)
        {
            return match.winnerUserId == localId ? "Match won" : "Match lost";
        }

        return null;
    }

    public IEnumerator StartNextGameCoroutine()
    {
        ShouldReloadNextGame = false;
        InMatchRoom = true;

        if (!InConfiguredMatchRoom())
        {
            Debug.LogWarning("[Tournament] Rematch is not in a match room — joining it now");
            if (IsSpectating || (ContinuousController.instance != null && ContinuousController.instance.isTournamentSpectator))
            {
                yield return JoinMatchAsSpectatorCoroutine(_round, _matchIndex);
            }
            else
            {
                yield return JoinMatchRoomCoroutine(_round, _matchIndex);
            }

            yield break;
        }

        float wait = 0f;
        float reconnectWait = BattleReconnectService.PlayerTtlMs / 1000f;
        while (PhotonNetwork.InRoom && TournamentKeys.CountActiveCompetitors() < 2 && wait < reconnectWait)
        {
            wait += Time.unscaledDeltaTime;
            yield return null;
        }

        if (!PhotonNetwork.InRoom || TournamentKeys.CountActiveCompetitors() < 2)
        {
            // Opponent never arrived — do not award a phantom series. Meet in the hub.
            if (IsSpectating)
            {
                ContinuousController.instance?.ClearTournamentSpectator();
                IsSpectating = false;
            }

            yield return JoinWaitHubCoroutine();
            yield break;
        }

        var cc = ContinuousController.instance;
        bool spectating = IsSpectating || (cc != null && cc.isTournamentSpectator);
        if (spectating)
        {
            SpectatorCatchUpDriver.Instance?.Cancel();
            SpectatorCatchUpTransfer.StopStream();
            cc?.ClearCatchUp();

            // Surrender used to leave the previous BattleScene loaded. Catch-up then
            // treated that leftover GManager as "already in the next game".
            if (GManager.instance != null || ContinuousController.IsBattleSceneLoaded())
            {
                yield return ClearLeftoverBattleSceneCoroutine();
            }

            var state = cc != null ? cc.TournamentState : null;
            var match = state != null ? state.GetMatch(_round, _matchIndex) : null;

            // The next game needs a fresh stream — the previous one ended with the last game.
            if (CompetitorsReportingBattle())
            {
                yield return TrySpectatorCatchUpStartBattle(match);
                if (GManager.instance != null || ContinuousController.IsBattleSceneLoaded())
                {
                    yield break;
                }
            }

            yield return JoinMatchAsSpectatorCoroutine(_round, _matchIndex);
            yield break;
        }

        yield return StartBattleCoroutine(isRematch: true);
        if (GManager.instance == null &&
            !ContinuousController.IsBattleSceneLoaded() &&
            InConfiguredMatchRoom() &&
            TournamentKeys.CountActiveCompetitors() >= 2)
        {
            Debug.LogWarning("[Tournament] Rematch StartBattle did not load — retrying once");
            yield return StartBattleCoroutine(isRematch: true);
        }
    }

    public IEnumerator RouteAfterSeriesCoroutine()
    {
        RoutingAfterSeries = true;
        ShouldReloadNextGame = false;
        InMatchRoom = false;
        DestroyOverlay();
        DestroyLeaveSpectateButton();

        var cc = ContinuousController.instance;
        if (cc != null)
        {
            cc.ClearTournamentSpectator();
        }

        IsSpectating = false;

        var state = cc != null ? cc.TournamentState : null;
        if (state != null)
        {
            state.ResolveOpeningByes();
            cc.TournamentState = state;
        }

        // Always reconvene in the wait hub so the bye / waiting player receives
        // the updated bracket. Going straight to the next match room left them behind.
        // Leave RoutingAfterSeries true until EndBattle finishes EndLoading so
        // Update cannot start the final during teardown.
        yield return JoinWaitHubCoroutine();
    }

    public void EndRoutingAfterSeries()
    {
        RoutingAfterSeries = false;
    }

    IEnumerator StartBattleCoroutine(bool isRematch)
    {
        if (_startingBattle)
        {
            yield break;
        }

        _startingBattle = true;

        yield return ClearLeftoverBattleSceneCoroutine();
        if (isRematch && (GManager.instance != null || ContinuousController.IsBattleSceneLoaded()))
        {
            float extra = 0f;
            while ((GManager.instance != null || ContinuousController.IsBattleSceneLoaded()) && extra < 8f)
            {
                yield return ClearLeftoverBattleSceneCoroutine();
                extra += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        if (GManager.instance != null || ContinuousController.IsBattleSceneLoaded())
        {
            // A rematch must not abandon the opponent in the match room (1-1 game 3).
            if (isRematch && InConfiguredMatchRoom() && TournamentKeys.CountActiveCompetitors() >= 2)
            {
                Debug.LogWarning("[Tournament] Leftover battle scene on rematch — clearing then continuing");
                yield return ClearLeftoverBattleSceneCoroutine();
                if (GManager.instance != null || ContinuousController.IsBattleSceneLoaded())
                {
                    _startingBattle = false;
                    yield break;
                }
            }
            else if (IsSpectating && !isRematch)
            {
                Debug.LogWarning("[Tournament] Leftover battle scene for spectator — clearing");
                yield return ClearLeftoverBattleSceneCoroutine();
                if (GManager.instance != null || ContinuousController.IsBattleSceneLoaded())
                {
                    _startingBattle = false;
                    PhotonNetwork.IsMessageQueueRunning = true;
                    yield return AbortStartBattleToWaitHubCoroutine();
                    yield break;
                }
            }
            else
            {
                Debug.LogWarning("[Tournament] Leftover battle scene blocked the next match — returning to wait hub");
                _startingBattle = false;
                yield return AbortStartBattleToWaitHubCoroutine();
                yield break;
            }
        }

        if (!InConfiguredMatchRoom() ||
            !PhotonNetwork.InRoom ||
            TournamentKeys.CountActiveCompetitors() < 2)
        {
            Debug.LogWarning($"[Tournament] Refusing to start battle in '{PhotonNetwork.CurrentRoom?.Name}' players={TournamentKeys.CountActiveCompetitors()}");
            _startingBattle = false;
            yield break;
        }

        InMatchRoom = true;
        SpectatorCatchUpTransfer.EnsureExists();
        if (ContinuousController.instance != null)
        {
            ContinuousController.instance.CanSetRandom = false;
            ContinuousController.instance.DoneSetRandom = false;
        }

        Debug.Log($"[Tournament] Start battle rematch={isRematch} room={PhotonNetwork.CurrentRoom?.Name} players={PhotonNetwork.CurrentRoom.PlayerCount}");

        try
        {
            Opening.instance?.battle?.tournamentLobbyManager?.HideLobbyUi();

            TournamentKeys.EnsureMasterIsCompetitor();
            if (PhotonNetwork.IsMasterClient && PhotonNetwork.InRoom)
            {
                byte cap = (byte)TournamentKeys.MatchRoomMaxPlayers(
                    ContinuousController.instance.TournamentState != null
                        ? ContinuousController.instance.TournamentState.ResolvedPlayerCount
                        : TournamentKeys.ActivePlayerCount);
                if (PhotonNetwork.CurrentRoom.MaxPlayers < cap)
                {
                    PhotonNetwork.CurrentRoom.MaxPlayers = cap;
                }

                PhotonNetwork.CurrentRoom.IsOpen = true;
                PhotonNetwork.CurrentRoom.IsVisible = false;

                if (!IsSpectating)
                {
                    ApplyFirstPlayerProperty(isRematch);
                }
            }

            var unloadLoading = Opening.instance != null ? Opening.instance.LoadingObject_Unload : null;
            if (unloadLoading != null)
            {
                yield return ContinuousController.instance.StartCoroutine(
                    unloadLoading.StartLoading("Now Loading"));
            }

            var playerProp = PhotonNetwork.LocalPlayer.CustomProperties ?? new Hashtable();
            playerProp["isBattle"] = true;
            playerProp[TournamentKeys.OnResultProperty] = false;
            playerProp[TournamentKeys.RoleProperty] = IsSpectating
                ? TournamentKeys.RoleSpectator
                : TournamentKeys.RoleCompetitor;

            if (IsSpectating)
            {
                var spectateMatch = ContinuousController.instance.TournamentState != null
                    ? ContinuousController.instance.TournamentState.GetMatch(_round, _matchIndex)
                    : null;
                ApplySpectatorDeckFromPov(spectateMatch);
                if (ContinuousController.instance.BattleDeckData != null)
                {
                    string spectateCode = ContinuousController.instance.BattleDeckData.GetThisDeckCode();
                    playerProp[ContinuousController.DeckDataPropertyKey] = spectateCode;
                    playerProp[TournamentKeys.LockedDeckProperty] = spectateCode;
                }
            }
            else
            {
                EnsureLockedDeckOnHash(playerProp);
                string locked = TournamentState.ReadDeckCode(PhotonNetwork.LocalPlayer);
                if (string.IsNullOrEmpty(locked))
                {
                    string localId = TournamentState.EnsureLocalPlayerId();
                    locked = ContinuousController.instance.TournamentState != null
                        ? ContinuousController.instance.TournamentState.LockedDeckCode(localId)
                        : null;
                }

                if (!string.IsNullOrEmpty(locked))
                {
                    playerProp[ContinuousController.DeckDataPropertyKey] = locked;
                    playerProp[TournamentKeys.LockedDeckProperty] = locked;
                    if (ContinuousController.instance.BattleDeckData == null ||
                        ContinuousController.instance.BattleDeckData.GetThisDeckCode() != locked)
                    {
                        ContinuousController.instance.BattleDeckData = new DeckData(locked);
                    }
                }
            }

            PhotonNetwork.LocalPlayer.SetCustomProperties(playerProp);
            yield return Wait01;

            foreach (Camera camera in Opening.instance.openingCameras)
            {
                camera.gameObject.SetActive(false);
            }

            ContinuousController.instance.StartCoroutine(Opening.instance.OpeningBGM.FadeOut(0.5f));
            yield return Wait1;

            ContinuousController.CleanStalePhotonViews();
            if (!IsSpectating)
            {
                PhotonNetwork.IsMessageQueueRunning = true;
            }

            yield return ClearLeftoverBattleSceneCoroutine();
            if (ContinuousController.IsBattleSceneLoaded() || GManager.instance != null)
            {
                Debug.LogWarning("[Tournament] Battle load aborted — leftover scene after cameras off");
                PhotonNetwork.IsMessageQueueRunning = true;
                RestoreOpeningCameras();
                if (unloadLoading != null)
                {
                    yield return ContinuousController.instance.StartCoroutine(unloadLoading.EndLoading());
                }

                yield return AbortStartBattleToWaitHubCoroutine();
                yield break;
            }

            var load = SceneManager.LoadSceneAsync(ContinuousController.BattleSceneName, LoadSceneMode.Additive);
            while (load != null && !load.isDone)
            {
                yield return null;
            }

            float waitGm = 0f;
            while (GManager.instance == null && waitGm < 20f)
            {
                waitGm += Time.deltaTime;
                yield return null;
            }

            PhotonNetwork.IsMessageQueueRunning = true;

            if (unloadLoading != null)
            {
                yield return ContinuousController.instance.StartCoroutine(unloadLoading.EndLoading());
            }

            StartCoroutine(AttachSeriesOverlayWhenReady());
            if (IsSpectating)
            {
                ShowLeaveSpectateButton();
                StartCoroutine(DisableSpectatorInputsWhenReady());
            }
        }
        finally
        {
            _startingBattle = false;
        }
    }

    IEnumerator DisableSpectatorInputsWhenReady()
    {
        float wait = 0f;
        while (GManager.instance == null && wait < 20f)
        {
            wait += Time.unscaledDeltaTime;
            yield return null;
        }

        if (GManager.instance == null)
        {
            yield break;
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

    IEnumerator ClearLeftoverBattleSceneCoroutine()
    {
        for (int guard = 0; guard < 4 && ContinuousController.IsBattleSceneLoaded(); guard++)
        {
            var unload = SceneManager.UnloadSceneAsync(ContinuousController.BattleSceneName);
            if (unload == null)
            {
                break;
            }

            yield return unload;
        }

        yield return null;
        yield return null;
        ContinuousController.CleanStalePhotonViews();

        float waitGone = 0f;
        while ((ContinuousController.IsBattleSceneLoaded() || GManager.instance != null) && waitGone < 2f)
        {
            waitGone += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    IEnumerator AbortStartBattleToWaitHubCoroutine()
    {
        RestoreOpeningCameras();
        InMatchRoom = false;
        _startingBattle = false;
        yield return JoinWaitHubCoroutine();
    }

    static void RestoreOpeningCameras()
    {
        if (Opening.instance == null)
        {
            return;
        }

        Opening.instance.openingObject.SetActive(true);
        if (Opening.instance.openingCameras == null)
        {
            return;
        }

        foreach (Camera camera in Opening.instance.openingCameras)
        {
            if (camera != null)
            {
                camera.gameObject.SetActive(true);
            }
        }
    }

    IEnumerator AttachSeriesOverlayWhenReady()
    {
        float t = 0f;
        while (GManager.instance == null && t < 15f)
        {
            t += Time.deltaTime;
            yield return null;
        }

        if (GManager.instance == null || GManager.instance.canvas == null)
        {
            yield break;
        }

        DestroyOverlay();
        var go = new GameObject("TournamentSeriesOverlay");
        go.transform.SetParent(GManager.instance.canvas.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -18f);
        rt.sizeDelta = new Vector2(900f, 48f);

        _seriesOverlay = go.AddComponent<Text>();
        _seriesOverlay.font = ResolveFont();
        _seriesOverlay.fontSize = 22;
        _seriesOverlay.alignment = TextAnchor.UpperCenter;
        _seriesOverlay.color = Color.white;
        _seriesOverlay.raycastTarget = false;
        RefreshSeriesOverlay();
    }

    void RefreshSeriesOverlay()
    {
        if (_seriesOverlay == null)
        {
            return;
        }

        var state = ContinuousController.instance != null ? ContinuousController.instance.TournamentState : null;
        var match = state != null ? state.GetMatch(_round, _matchIndex) : null;
        if (match == null)
        {
            _seriesOverlay.text = "Tournament";
            return;
        }

        if (IsSpectating || (ContinuousController.instance != null && ContinuousController.instance.isTournamentSpectator))
        {
            string a = state.DisplayName(match.userIdA);
            string b = state.DisplayName(match.userIdB);
            _seriesOverlay.text = $"Spectating  —  {a} vs {b}";
            return;
        }

        _seriesOverlay.text = "Tournament";
    }

    void ApplyFirstPlayerProperty(bool isRematch)
    {
        var state = ContinuousController.instance.TournamentState;
        var match = state != null ? state.GetMatch(_round, _matchIndex) : null;
        int firstPlayerId = -1;

        string loserId = match != null ? match.lastGameLoserUserId : null;
        if (string.IsNullOrEmpty(loserId) &&
            PhotonNetwork.InRoom &&
            PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(TournamentKeys.LastLoserProperty, out object loserObj) &&
            loserObj is string roomLoser)
        {
            loserId = roomLoser;
        }

        string firstUserId = Bo3FirstPlayerChoice.ReadChosenFirstUserId(
            TournamentKeys.NextFirstUserIdProperty,
            TournamentKeys.NextFirstGameIndexProperty,
            match != null ? match.gameIndex : 0);
        if (string.IsNullOrEmpty(firstUserId))
        {
            firstUserId = loserId;
        }

        if (isRematch && !string.IsNullOrEmpty(firstUserId))
        {
            firstPlayerId = Bo3FirstPlayerChoice.ActorNumberForUserId(firstUserId);
        }

        var hash = new Hashtable
        {
            { DataBase.FirstPlayerKey, firstPlayerId },
        };
        if (!string.IsNullOrEmpty(loserId))
        {
            hash[TournamentKeys.LastLoserProperty] = loserId;
        }

        PhotonNetwork.CurrentRoom.SetCustomProperties(hash);
        Debug.Log($"[Tournament] FirstPlayer actor={firstPlayerId} rematch={isRematch} firstUser={firstUserId} loser={loserId}");
    }

    void AwardSeriesForfeitIfAlone()
    {
        if (IsSpectating || (ContinuousController.instance != null && ContinuousController.instance.isTournamentSpectator))
        {
            return;
        }

        var cc = ContinuousController.instance;
        var state = cc != null ? cc.TournamentState : null;
        if (state == null)
        {
            return;
        }

        var match = state.GetMatch(_round, _matchIndex);
        if (match == null || match.complete)
        {
            return;
        }

        string localId = TournamentState.EnsureLocalPlayerId();
        state.CompleteMatch(match, localId);
        cc.TournamentState = state;
        cc.WinCount++;
        cc.SaveWinCount();
        ShouldReloadNextGame = false;
    }

    void SyncMatchPropsFromRoom()
    {
        if (!PhotonNetwork.InRoom)
        {
            return;
        }

        var hash = PhotonNetwork.CurrentRoom.CustomProperties;
        if (hash.TryGetValue(TournamentKeys.RoundProperty, out object roundObj))
        {
            _round = System.Convert.ToInt32(roundObj);
        }

        if (hash.TryGetValue(TournamentKeys.MatchIndexProperty, out object matchObj))
        {
            _matchIndex = System.Convert.ToInt32(matchObj);
        }

        var state = ContinuousController.instance.TournamentState;
        var match = state != null ? state.GetMatch(_round, _matchIndex) : null;
        if (match == null)
        {
            return;
        }

        if (hash.TryGetValue(TournamentKeys.SeriesWinsAProperty, out object a))
        {
            match.seriesWinsA = System.Convert.ToInt32(a);
        }

        if (hash.TryGetValue(TournamentKeys.SeriesWinsBProperty, out object b))
        {
            match.seriesWinsB = System.Convert.ToInt32(b);
        }

        if (hash.TryGetValue(TournamentKeys.GameIndexProperty, out object g))
        {
            match.gameIndex = System.Convert.ToInt32(g);
        }

        if (hash.TryGetValue(TournamentKeys.LastLoserProperty, out object loserObj) &&
            loserObj is string loser &&
            !string.IsNullOrEmpty(loser))
        {
            match.lastGameLoserUserId = loser;
        }

        if (hash.TryGetValue(TournamentKeys.StateProperty, out object jsonObj) && jsonObj is string json)
        {
            var incoming = TournamentState.FromJson(json);
            if (incoming != null)
            {
                state.MergeFrom(incoming);
            }
        }
    }

    void PublishStateToCurrentRoom()
    {
        if (IsSpectating || (ContinuousController.instance != null && ContinuousController.instance.isTournamentSpectator))
        {
            return;
        }

        var state = ContinuousController.instance != null ? ContinuousController.instance.TournamentState : null;
        if (!PhotonNetwork.InRoom || state == null)
        {
            return;
        }

        PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable
        {
            { TournamentKeys.StateProperty, state.ToRoomJson() },
            { TournamentKeys.StartedProperty, true },
        });
    }

    void EnsureLockedDeckProperty()
    {
        var hash = PhotonNetwork.LocalPlayer.CustomProperties ?? new Hashtable();
        EnsureLockedDeckOnHash(hash);
        PhotonNetwork.LocalPlayer.SetCustomProperties(hash);
    }

    static void EnsureLockedDeckOnHash(Hashtable hash)
    {
        string localId = TournamentState.EnsureLocalPlayerId();
        var state = ContinuousController.instance != null ? ContinuousController.instance.TournamentState : null;
        string code = state != null ? state.LockedDeckCode(localId) : null;
        if (string.IsNullOrEmpty(code) && ContinuousController.instance.BattleDeckData != null)
        {
            code = ContinuousController.instance.BattleDeckData.GetThisDeckCode();
        }

        if (!string.IsNullOrEmpty(code))
        {
            hash[TournamentKeys.LockedDeckProperty] = code;
            hash[ContinuousController.DeckDataPropertyKey] = code;
        }

        hash[TournamentKeys.PlayerIdProperty] = localId;
    }

    static string OpponentUserId(TournamentState state, string localId, TournamentMatchSlot match = null)
    {
        match ??= state.FindActiveMatchFor(localId) ?? state.GetMatch(0, 0);
        if (match == null)
        {
            return null;
        }

        string other = match.userIdA == localId ? match.userIdB : match.userIdA;
        return TournamentKeys.IsBye(other) ? null : other;
    }

    /// <summary>Both sides filled and at least one is BYE — must auto-resolve, never Photon-battle.</summary>
    static bool IsAssignedByeMatch(TournamentMatchSlot match)
    {
        if (match == null || match.complete)
        {
            return false;
        }

        if (string.IsNullOrEmpty(match.userIdA) || string.IsNullOrEmpty(match.userIdB))
        {
            return false;
        }

        return TournamentKeys.IsBye(match.userIdA) || TournamentKeys.IsBye(match.userIdB);
    }

    IEnumerator JoinNamedRoomOnly(string roomName)
    {
        _joinFailed = false;
        if (PhotonNetwork.InRoom)
        {
            if (PhotonNetwork.CurrentRoom.Name == roomName)
            {
                yield break;
            }

            PhotonNetwork.LeaveRoom(false);
            yield return new WaitWhile(() => PhotonNetwork.InRoom);
        }

        if (!PhotonNetwork.IsConnectedAndReady)
        {
            yield return ContinuousController.instance.StartCoroutine(PhotonUtility.ConnectToMasterServerCoroutine());
        }

        yield return new WaitUntil(() => PhotonNetwork.IsConnectedAndReady);

        if (!PhotonNetwork.InLobby)
        {
            PhotonNetwork.JoinLobby();
        }

        yield return new WaitUntil(() => PhotonNetwork.InLobby && PhotonNetwork.IsConnectedAndReady);

        _pendingRoomName = roomName;
        _joinFailed = false;
        PhotonNetwork.JoinRoom(roomName);

        float t = 0f;
        while (!PhotonNetwork.InRoom && !_joinFailed && t < 8f)
        {
            t += Time.deltaTime;
            yield return null;
        }
    }

    IEnumerator JoinOrCreateNamedRoom(string roomName, RoomOptions options)
    {
        _joinFailed = false;
        _createFailed = false;

        if (PhotonNetwork.InRoom)
        {
            PhotonNetwork.LeaveRoom(false);
            yield return new WaitWhile(() => PhotonNetwork.InRoom);
        }

        if (!PhotonNetwork.IsConnectedAndReady)
        {
            yield return ContinuousController.instance.StartCoroutine(PhotonUtility.ConnectToMasterServerCoroutine());
        }

        yield return new WaitUntil(() => PhotonNetwork.IsConnectedAndReady);

        if (!PhotonNetwork.InLobby)
        {
            PhotonNetwork.JoinLobby();
        }

        yield return new WaitUntil(() => PhotonNetwork.InLobby && PhotonNetwork.IsConnectedAndReady);

        _pendingRoomName = roomName;
        _joinOrCreatePending = true;
        _joinFailed = false;
        _createFailed = false;
        PhotonNetwork.JoinOrCreateRoom(roomName, options, TypedLobby.Default);

        float t = 0f;
        // JoinOrCreate fires OnJoinRoomFailed(32758) when the room does not exist yet, then creates it.
        // Do not abort on join-failed while that create is still in flight.
        while (!PhotonNetwork.InRoom && !_createFailed && t < 15f)
        {
            t += Time.deltaTime;
            yield return null;
        }

        _joinOrCreatePending = false;
        if (PhotonNetwork.InRoom)
        {
            yield break;
        }

        _joinFailed = false;
        _createFailed = false;
        PhotonNetwork.CreateRoom(roomName, options, TypedLobby.Default);

        t = 0f;
        while (!PhotonNetwork.InRoom && !_createFailed && t < 10f)
        {
            t += Time.deltaTime;
            yield return null;
        }

        if (!PhotonNetwork.InRoom)
        {
            _joinFailed = false;
            PhotonNetwork.JoinRoom(roomName);
            t = 0f;
            while (!PhotonNetwork.InRoom && !_joinFailed && t < 10f)
            {
                t += Time.deltaTime;
                yield return null;
            }
        }
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        _lastJoinFailCode = returnCode;
        _lastJoinFailMessage = message;
        if (_joinOrCreatePending && (returnCode == 32758 || returnCode == ErrorCode.GameDoesNotExist))
        {
            return;
        }

        _joinFailed = true;
        Debug.LogWarning($"[Tournament] OnJoinRoomFailed code={returnCode} message={message}");
    }

    static string DescribeJoinFail(short code, string message)
    {
        if (code == ErrorCode.GameFull || code == 32765)
        {
            return "room full — fighters must be on latest build";
        }

        if (code == ErrorCode.GameClosed || code == 32764)
        {
            return "room closed";
        }

        if (code == ErrorCode.GameDoesNotExist || code == 32758)
        {
            return "room not created yet";
        }

        if (code == 0)
        {
            return "timeout";
        }

        return string.IsNullOrEmpty(message) ? $"error {code}" : message;
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        _createFailed = true;
    }

    /// <summary>True only inside a room created as a tournament match room (never the lobby / wait hub).</summary>
    static bool InConfiguredMatchRoom()
    {
        if (!PhotonNetwork.InRoom)
        {
            return false;
        }

        var props = PhotonNetwork.CurrentRoom.CustomProperties;
        return props != null &&
               props.TryGetValue(TournamentKeys.RoomKindProperty, out object kind) &&
               kind is string kindStr &&
               kindStr == TournamentKeys.RoomKindMatch;
    }

    public override void OnPlayerLeftRoom(Photon.Realtime.Player otherPlayer)
    {
        // Inactive and full leaves forfeit via GManager.CheckDisconnect.
        // Waiting for an inactive opponent is disabled (BattleReconnectService.WaitForOpponent).
    }

    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        if (!InMatchRoom)
        {
            return;
        }

        SyncMatchPropsFromRoom();
        RefreshSeriesOverlay();
    }

    public override void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer)
    {
        if (!InConfiguredMatchRoom())
        {
            return;
        }

        if (!InMatchRoom || _startingBattle || GManager.instance != null ||
            ContinuousController.IsBattleSceneLoaded())
        {
            return;
        }

        if (CompetitorsReportingBattle())
        {
            return;
        }

        if (IsSpectating)
        {
            // Spectators attach through the recorder stream, never on a blank board.
            return;
        }

        if (PhotonNetwork.InRoom && TournamentKeys.CountActiveCompetitors() >= 2)
        {
            var state = ContinuousController.instance != null ? ContinuousController.instance.TournamentState : null;
            var match = state != null ? state.GetMatch(_round, _matchIndex) : null;
            StartCoroutine(StartBattleCoroutine(isRematch: IsCurrentMatchRematch(match)));
        }
    }

    void DestroyOverlay()
    {
        if (_seriesOverlay != null)
        {
            Destroy(_seriesOverlay.gameObject);
            _seriesOverlay = null;
        }
    }

    void ShowLeaveSpectateButton()
    {
        DestroyLeaveSpectateButton();
        if (!IsSpectating)
        {
            return;
        }

        var canvas = Opening.instance != null ? Opening.instance.canvasRect : null;
        if (canvas == null && GManager.instance != null)
        {
            // Prefer battle canvas if Opening canvas is hidden.
            var canvasComp = GManager.instance.GetComponentInChildren<Canvas>(true);
            if (canvasComp != null)
            {
                canvas = canvasComp.transform as RectTransform;
            }
        }

        if (canvas == null)
        {
            return;
        }

        Font font = ResolveFont();
        var go = new GameObject("LeaveSpectate", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(canvas, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-24f, -24f);
        rt.sizeDelta = new Vector2(200f, 48f);
        var image = go.GetComponent<Image>();
        image.color = new Color(0.55f, 0.22f, 0.22f, 0.92f);
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(OnClickLeaveSpectate);

        var textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;
        var text = textGo.AddComponent<Text>();
        text.font = font;
        text.fontSize = 18;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.text = LocalizeUtility.GetLocalizedString(EngMessage: "Leave Spectate", JpnMessage: "観戦終了");
        text.raycastTarget = false;
        _leaveSpectateButton = go;
    }

    void DestroyLeaveSpectateButton()
    {
        if (_leaveSpectateButton != null)
        {
            Destroy(_leaveSpectateButton);
            _leaveSpectateButton = null;
        }
    }

    static Font ResolveFont()
    {
        var rm = Opening.instance != null ? Opening.instance.battle?.roomManager : null;
        if (rm != null && rm.RoomIDText != null && rm.RoomIDText.font != null)
        {
            return rm.RoomIDText.font;
        }

        var arial = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (arial != null)
        {
            return arial;
        }

        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    void OnDestroy()
    {
        DestroyOverlay();
        DestroyLeaveSpectateButton();
    }
}
