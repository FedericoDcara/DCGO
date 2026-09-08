using System;
using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// Configurable 4/8/16-player tournament signup lobby, wait hub, and bracket display.
/// Builds a Room Match-like panel at runtime under Opening.canvasRect.
/// </summary>
public class TournamentLobbyManager : MonoBehaviourPunCallbacks
{
    private static readonly WaitForSeconds Wait01 = new WaitForSeconds(0.1f);

    public GameObject Parent { get; private set; }

    bool _endSetUp;
    bool _isReady;
    bool _createFailed;
    bool _oldIsReady;
    bool _dispatching;
    bool _spectateBusy;
    bool _selectingDeck;
    int _playerCount;
    string _tourneyId;

    Text _titleText;
    Text _roomIdText;
    Text _statusText;
    Text _deckText;
    Text _howToText;
    Text _bracketText;
    Text _readyButtonText;
    Text _startButtonText;
    Text _copyIdButtonText;
    Button _readyButton;
    Button _selectDeckButton;
    Button _startButton;
    Button _leaveButton;
    Button _copyIdButton;
    Button _sitOutButton;
    Text _sitOutButtonText;
    Image _sitOutButtonImage;
    Transform _spectateParent;
    bool _sitOut;
    Image _readyButtonImage;
    Image _startButtonImage;
    Transform _playerParent;
    GameObject _howToPanel;
    GameObject _invalidDeckObject;
    GameObject _noDeckObject;
    ScrollRect _bracketScroll;
    Coroutine _copyFeedback;

    public static TournamentLobbyManager EnsureExists()
    {
        var battle = Opening.instance != null ? Opening.instance.battle : null;
        if (battle == null)
        {
            return null;
        }

        if (battle.tournamentLobbyManager != null)
        {
            return battle.tournamentLobbyManager;
        }

        var existing = battle.GetComponent<TournamentLobbyManager>();
        if (existing == null)
        {
            existing = battle.gameObject.AddComponent<TournamentLobbyManager>();
        }

        battle.tournamentLobbyManager = existing;
        return existing;
    }

    public void SetUpLobby(bool createNew)
    {
        ContinuousController.instance.StartCoroutine(SetUpLobbyCoroutine(createNew));
    }

    public void SetUpAfterJoin()
    {
        ContinuousController.instance.StartCoroutine(SetUpLobbyCoroutine(createNew: false));
    }

    IEnumerator SetUpLobbyCoroutine(bool createNew)
    {
        Opening.instance.battle?.selectBattleMode?.HideOverlayDialogs();
        Opening.instance.OffYesNoObjects();
        Opening.instance.deck.trialDraw.Close();
        Opening.instance.deck.deckListPanel.Close();

        var cc = ContinuousController.instance;
        cc.isAI = false;
        cc.isRandomMatch = false;
        cc.isRanked = false;
        cc.isTournament = true;
        cc.isTournamentStarted = false;
        cc.ClearTournamentSpectator();
        _sitOut = false;

        // A previous tournament can leave the director marked "in a match room". Entering a fresh
        // lobby must clear it, or the next player who joins the lobby triggers a battle right here.
        TournamentServices.EnsureExists().Match.ResetDirector();
        if (Opening.instance.battle != null)
        {
            Opening.instance.battle.gameObject.SetActive(true);
        }

        ShowPanel();

        yield return cc.StartCoroutine(Opening.instance.LoadingObject.StartLoading("Now Loading"));

        _endSetUp = false;
        _isReady = false;
        _dispatching = false;
        _spectateBusy = false;
        _sitOut = false;

        if (!PhotonNetwork.InRoom)
        {
            if (createNew)
            {
                yield return CreateLobbyCoroutine();
            }
        }

        if (!PhotonNetwork.InRoom)
        {
            yield return cc.StartCoroutine(Opening.instance.LoadingObject.EndLoading());
            Off();
            Opening.instance.battle.selectBattleMode.SetUpSelectBattleMode();
            yield break;
        }

        _tourneyId = TournamentKeys.DisplayRoomId(PhotonNetwork.CurrentRoom.Name);
        SyncBanlistFromRoom();
        SyncPlayerCountFromRoom();
        cc.TournamentState ??= TournamentState.CreateNew(_tourneyId, cc.useBanlist, TournamentKeys.ActivePlayerCount);
        cc.TournamentState.tourneyId = _tourneyId;
        cc.TournamentState.useBanlist = cc.useBanlist;
        cc.TournamentState.playerCount = TournamentKeys.ActivePlayerCount;

        TournamentState.EnsureLocalPlayerId();
        yield return cc.StartCoroutine(PhotonUtility.SetPlayerName());

        if (cc.LastBattleDeckData != null &&
            cc.DeckDatas != null &&
            cc.DeckDatas.Contains(cc.LastBattleDeckData) &&
            cc.LastBattleDeckData.IsValidDeckData())
        {
            cc.BattleDeckData = cc.LastBattleDeckData;
        }
        else
        {
            cc.BattleDeckData = FirstValidDeckData();
        }

        if (cc.BattleDeckData != null)
        {
            yield return cc.StartCoroutine(PhotonUtility.SignUpBattleDeckData());
        }

        SetReadyProperty(false);
        _sitOut = TournamentKeys.IsSitOut(PhotonNetwork.LocalPlayer);
        if (PhotonNetwork.IsMasterClient)
        {
            WriteSitOutProperty(_sitOut);
        }

        RefreshAllUi();

        yield return cc.StartCoroutine(Opening.instance.LoadingObject.EndLoading());
        _endSetUp = true;
    }

    IEnumerator CreateLobbyCoroutine()
    {
        _createFailed = false;

        if (!PhotonNetwork.IsConnected)
        {
            yield return ContinuousController.instance.StartCoroutine(PhotonUtility.ConnectToMasterServerCoroutine());
        }

        yield return new WaitWhile(() => !PhotonNetwork.IsConnectedAndReady);

        if (!PhotonNetwork.InLobby)
        {
            PhotonNetwork.JoinLobby();
        }

        yield return new WaitWhile(() => !PhotonNetwork.InLobby);

        for (int attempt = 0; attempt < 8; attempt++)
        {
            _createFailed = false;
            string id = StringUtils.GeneratePassword_Num(5);
            bool banlist = ContinuousController.instance.useBanlist;
            string roomName = TournamentKeys.LobbyRoomName(id, banlist);

            var options = new RoomOptions
            {
                IsVisible = true,
                IsOpen = true,
                PublishUserId = true,
                MaxPlayers = (byte)TournamentKeys.RoomCapacityForBracket(TournamentKeys.ActivePlayerCount),
                CustomRoomProperties = new Hashtable
                {
                    { "RoomCreator", PhotonNetwork.NickName },
                    { TournamentKeys.UseBanlistProperty, banlist },
                    { TournamentKeys.ModeProperty, TournamentKeys.ModeTournament },
                    { TournamentKeys.TourneyIdProperty, id },
                    { TournamentKeys.RoomKindProperty, TournamentKeys.RoomKindLobby },
                    { TournamentKeys.StartedProperty, false },
                    { TournamentKeys.PlayerCountProperty, TournamentKeys.ActivePlayerCount },
                },
                CustomRoomPropertiesForLobby = new[]
                {
                    "RoomCreator",
                    TournamentKeys.UseBanlistProperty,
                    TournamentKeys.ModeProperty,
                    TournamentKeys.TourneyIdProperty,
                    TournamentKeys.PlayerCountProperty,
                },
            };
            BattleReconnectService.ApplyBattleTtl(options);

            PhotonNetwork.CreateRoom(roomName, options, null);

            while (!_createFailed && !PhotonNetwork.InRoom)
            {
                yield return null;
            }

            if (!_createFailed && PhotonNetwork.InRoom)
            {
                _tourneyId = id;
                yield break;
            }
        }
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        _createFailed = true;
    }

    public void Off()
    {
        if (Parent != null)
        {
            Parent.SetActive(false);
        }

        _endSetUp = false;
        _isReady = false;
        _dispatching = false;
        _spectateBusy = false;
    }

    public void HideLobbyUi()
    {
        if (Parent != null)
        {
            Parent.SetActive(false);
        }
    }

    public void CloseLobby()
    {
        ContinuousController.instance.StartCoroutine(CloseLobbyCoroutine());
    }

    public IEnumerator CloseLobbyCoroutine()
    {
        Opening.instance.battle.selectBattleDeck.OnCloseSelectBattleDeckAction = null;
        Opening.instance.battle.selectBattleDeck.Off();
        yield return ContinuousController.instance.StartCoroutine(Opening.instance.LoadingObject.StartLoading("Now Loading"));

        Off();
        TournamentServices.EnsureExists().Match.ResetDirector();
        ContinuousController.instance.ClearTournament();

        if (PhotonNetwork.InRoom)
        {
            PhotonUtility.LeaveRoomImmediate();
        }

        yield return new WaitWhile(() => PhotonNetwork.InRoom);

        if (PhotonNetwork.IsConnected)
        {
            yield return ContinuousController.instance.StartCoroutine(PhotonUtility.DisconnectCoroutine());
        }

        Opening.instance.battle.selectBattleMode.SetUpSelectBattleMode();
        yield return Wait01;
        yield return ContinuousController.instance.StartCoroutine(Opening.instance.LoadingObject.EndLoading());
    }

    public void OnClickReady()
    {
        if (!_endSetUp || ContinuousController.instance.isTournamentStarted)
        {
            return;
        }

        _isReady = !_isReady;
        SetReadyProperty(_isReady);
        RefreshPlayerList();
        RefreshButtons();

        if (_isReady)
        {
            Opening.instance.PlayDecisionSE();
        }
        else
        {
            Opening.instance.PlayCancelSE();
        }
    }

    public void OnClickSelectDeck()
    {
        if (!_endSetUp || ContinuousController.instance.isTournamentStarted)
        {
            return;
        }

        int defaultIndex = 0;
        if (ContinuousController.instance.BattleDeckData != null &&
            ContinuousController.instance.DeckDatas != null)
        {
            defaultIndex = ContinuousController.instance.DeckDatas.IndexOf(ContinuousController.instance.BattleDeckData);
        }

        _selectingDeck = true;
        _oldIsReady = _isReady;
        _isReady = false;
        SetReadyProperty(false);
        RefreshPlayerList();

        Opening.instance.battle.selectBattleDeck.SetUpSelectBattleDeck(
            () =>
            {
                Opening.instance.battle.selectBattleDeck.OnCloseSelectBattleDeckAction = null;
                ContinuousController.instance.StartCoroutine(EndSelectDeckCoroutine(confirmed: true));
            },
            defaultIndex);

        Opening.instance.battle.selectBattleDeck.OnCloseSelectBattleDeckAction =
            () => ContinuousController.instance.StartCoroutine(EndSelectDeckCoroutine(confirmed: false));
    }

    IEnumerator EndSelectDeckCoroutine(bool confirmed)
    {
        if (confirmed)
        {
            yield return ContinuousController.instance.StartCoroutine(
                Opening.instance.battle.selectBattleDeck.OnClickSelectButton_RoomMatchCoroutine());
        }

        yield return new WaitWhile(() => Opening.instance.battle.selectBattleDeck.gameObject.activeSelf);
        yield return Wait01;

        _selectingDeck = false;

        if (ContinuousController.instance.BattleDeckData != null)
        {
            yield return ContinuousController.instance.StartCoroutine(PhotonUtility.SignUpBattleDeckData());
        }

        _isReady = _oldIsReady && CanReady();
        SetReadyProperty(_isReady);
        RefreshAllUi();
    }

    public void OnClickStartTournament()
    {
        if (!PhotonNetwork.IsMasterClient || ContinuousController.instance.isTournamentStarted)
        {
            return;
        }

        if (!CanStartTournament())
        {
            int present = CountCompetitorPlayers();
            int needMax = TournamentKeys.ActivePlayerCount;
            Opening.instance.SetUpActiveYesNoObject(
                new List<UnityAction> { null },
                new List<string> { "OK" },
                LocalizeUtility.GetLocalizedString(
                    EngMessage: $"Need at least {TournamentKeys.MinPlayersToStart} ready competitors (max {needMax}). Empty seats become byes.\nCurrently: {present} competitors.",
                    JpnMessage: $"準備完了の競技者が{TournamentKeys.MinPlayersToStart}人以上必要です（最大{needMax}人）。空き枠はBYEになります。\n現在: 競技者{present}人"),
                false);
            return;
        }

        Opening.instance.PlayDecisionSE();
        ContinuousController.instance.StartCoroutine(StartTournamentCoroutine());
    }

    IEnumerator StartTournamentCoroutine()
    {
        var snapshot = SnapshotLobbyPlayers();
        if (snapshot.Count < TournamentKeys.MinPlayersToStart ||
            snapshot.Count > TournamentKeys.ActivePlayerCount)
        {
            Opening.instance.SetUpActiveYesNoObject(
                new List<UnityAction> { null },
                new List<string> { "OK" },
                LocalizeUtility.GetLocalizedString(
                    EngMessage: $"Cannot start: need {TournamentKeys.MinPlayersToStart}–{TournamentKeys.ActivePlayerCount} competitors with valid decks.",
                    JpnMessage: $"開始できません。競技者{TournamentKeys.MinPlayersToStart}〜{TournamentKeys.ActivePlayerCount}人で有効なデッキが必要です。"),
                false);
            yield break;
        }

        var state = ContinuousController.instance.TournamentState ??
                    TournamentState.CreateNew(_tourneyId, ContinuousController.instance.useBanlist, TournamentKeys.ActivePlayerCount);
        state.tourneyId = _tourneyId;
        state.useBanlist = ContinuousController.instance.useBanlist;
        state.playerCount = TournamentKeys.ActivePlayerCount;

        string adminId = null;
        string adminNick = null;
        if (PhotonNetwork.InRoom)
        {
            foreach (var p in PhotonNetwork.PlayerList)
            {
                if (!TournamentKeys.IsSitOut(p))
                {
                    continue;
                }

                adminId = TournamentState.ReadPlayerId(p);
                adminNick = TournamentState.ReadNickName(p);
                break;
            }
        }

        state.adminUserId = adminId;
        state.adminNickName = adminNick;

        try
        {
            state.SeedFromLobby(snapshot, unchecked((int)System.DateTime.UtcNow.Ticks));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Tournament] Seed failed: {e.Message}");
            Opening.instance.SetUpActiveYesNoObject(
                new List<UnityAction> { null },
                new List<string> { "OK" },
                LocalizeUtility.GetLocalizedString(
                    EngMessage: $"Could not build bracket: {e.Message}",
                    JpnMessage: $"トーナメント表を作成できませんでした: {e.Message}"),
                false);
            yield break;
        }

        ContinuousController.instance.TournamentState = state;
        ContinuousController.instance.isTournamentStarted = true;

        WriteLockedDeckProperty();

        var hash = PhotonNetwork.CurrentRoom.CustomProperties;
        hash[TournamentKeys.StateProperty] = state.ToRoomJson();
        hash[TournamentKeys.StartedProperty] = true;
        if (!string.IsNullOrEmpty(adminId))
        {
            hash[TournamentKeys.AdminUserIdProperty] = adminId;
        }

        PhotonNetwork.CurrentRoom.SetCustomProperties(hash);
        PhotonNetwork.CurrentRoom.IsOpen = false;
        PhotonNetwork.CurrentRoom.IsVisible = false;

        yield return Wait01;
        yield return DispatchToQuarterfinals();
    }

    IEnumerator DispatchToQuarterfinals()
    {
        if (_dispatching)
        {
            yield break;
        }

        _dispatching = true;
        _endSetUp = false;

        try
        {
            var state = ContinuousController.instance.TournamentState;
            string localId = TournamentState.EnsureLocalPlayerId();
            if (state != null)
            {
                state.ResolveOpeningByes();
                ContinuousController.instance.TournamentState = state;
            }

            var match = state != null ? state.FindActiveMatchFor(localId) : null;
            if (!TournamentKeys.IsReadyTwoPlayerMatch(match))
            {
                yield return TournamentServices.EnsureExists().Match.JoinWaitHubCoroutine();
            }
            else
            {
                yield return TournamentServices.EnsureExists().Match.JoinMatchRoomCoroutine(match.round, match.matchIndex);
            }
        }
        finally
        {
            _dispatching = false;
        }
    }

    public void ShowWaitHub()
    {
        if (Opening.instance != null && Opening.instance.battle != null)
        {
            Opening.instance.battle.gameObject.SetActive(true);
        }

        ShowPanel();
        _endSetUp = true;
        // Idle on the wait hub — never leave match-dispatch lock stuck (blocks Spectate).
        _dispatching = false;
        ContinuousController.instance.isTournament = true;
        ContinuousController.instance.isTournamentStarted = true;

        if (PhotonNetwork.InRoom)
        {
            MergeRoomState();
        }

        RefreshAllUi();
    }

    public void ShowMatchWaiting(int round, int matchIndex)
    {
        if (Opening.instance != null && Opening.instance.battle != null)
        {
            Opening.instance.battle.gameObject.SetActive(true);
        }

        ShowPanel();
        _endSetUp = false;
        RefreshAllUi();

        string roundName = TournamentKeys.RoundDisplayName(round);
        _statusText.text = LocalizeUtility.GetLocalizedString(
            EngMessage: $"Waiting for opponent — {roundName} match {matchIndex + 1}",
            JpnMessage: $"対戦相手を待っています — {roundName} 試合{matchIndex + 1}");
        _readyButton.gameObject.SetActive(false);
        _selectDeckButton.gameObject.SetActive(false);
        _startButton.gameObject.SetActive(false);
        if (_sitOutButton != null)
        {
            _sitOutButton.gameObject.SetActive(false);
        }
    }

    /// <summary>Status while joined as a spectator (mid-game wait or pre-battle).</summary>
    public void ShowSpectateWaiting(int round, int matchIndex, string statusEng, string statusJpn)
    {
        if (Opening.instance != null && Opening.instance.battle != null)
        {
            Opening.instance.battle.gameObject.SetActive(true);
        }

        ShowPanel();
        _endSetUp = false;
        EnsureUi();
        if (_roomIdText != null)
        {
            string id = _tourneyId ?? (PhotonNetwork.InRoom ? TournamentKeys.DisplayRoomId(PhotonNetwork.CurrentRoom.Name) : "");
            _roomIdText.text = string.IsNullOrEmpty(id) ? "" : id;
        }

        RefreshPlayerList();
        if (_bracketScroll != null)
        {
            _bracketScroll.gameObject.SetActive(true);
        }

        var state = ContinuousController.instance != null ? ContinuousController.instance.TournamentState : null;
        if (_bracketText != null && state != null)
        {
            _bracketText.text = state.FormatBracket();
        }

        string roundName = TournamentKeys.RoundDisplayName(round);
        _statusText.text = LocalizeUtility.GetLocalizedString(
            EngMessage: $"Spectating {roundName} M{matchIndex + 1} — {statusEng}",
            JpnMessage: $"観戦中 {roundName} 試合{matchIndex + 1} — {statusJpn}");

        _readyButton.gameObject.SetActive(false);
        _selectDeckButton.gameObject.SetActive(false);
        _startButton.gameObject.SetActive(false);
        if (_sitOutButton != null)
        {
            _sitOutButton.gameObject.SetActive(false);
        }

        if (_deckText != null)
        {
            _deckText.text = LocalizeUtility.GetLocalizedString(
                EngMessage: "Spectating (read-only)",
                JpnMessage: "観戦中（操作不可）");
        }

        if (_noDeckObject != null) _noDeckObject.SetActive(false);
        if (_invalidDeckObject != null) _invalidDeckObject.SetActive(false);

        // Replace spectate buttons so re-clicks don't look broken.
        if (_spectateParent != null)
        {
            for (int i = _spectateParent.childCount - 1; i >= 0; i--)
            {
                Destroy(_spectateParent.GetChild(i).gameObject);
            }

            _spectateParent.gameObject.SetActive(true);
            var font = ResolveFont();
            var note = new GameObject("SpectateBusyNote", typeof(RectTransform), typeof(LayoutElement));
            note.transform.SetParent(_spectateParent, false);
            note.GetComponent<LayoutElement>().preferredHeight = 48f;
            var noteText = note.AddComponent<Text>();
            noteText.font = font;
            noteText.fontSize = 16;
            noteText.color = new Color(0.85f, 0.95f, 1f, 0.95f);
            noteText.alignment = TextAnchor.MiddleLeft;
            noteText.horizontalOverflow = HorizontalWrapMode.Wrap;
            noteText.text = LocalizeUtility.GetLocalizedString(
                EngMessage: "Already spectating — use Leave Spectate (top-right) to cancel.",
                JpnMessage: "観戦待機中 — 右上の「観戦終了」でキャンセルできます。");
        }
    }

    public void ShowSpectateJoinFailed(string reason)
    {
        ShowWaitHub();
        string detail = string.IsNullOrEmpty(reason) ? "unknown error" : reason;
        Opening.instance?.SetUpActiveYesNoObject(
            new List<UnityAction> { null },
            new List<string> { "OK" },
            LocalizeUtility.GetLocalizedString(
                EngMessage: $"Could not spectate that match ({detail}).\nTry again when both players are in the duel room.",
                JpnMessage: $"観戦できませんでした（{detail}）。\n両プレイヤーが試合ルームに入ってから再試行してください。"),
            false);
    }

    Dictionary<string, TournamentPlayerSlot> SnapshotLobbyPlayersAsMap()
    {
        var map = new Dictionary<string, TournamentPlayerSlot>();
        foreach (var slot in SnapshotLobbyPlayers())
        {
            map[slot.userId] = slot;
        }

        return map;
    }

    List<TournamentPlayerSlot> SnapshotLobbyPlayers()
    {
        var list = new List<TournamentPlayerSlot>();
        if (!PhotonNetwork.InRoom)
        {
            return list;
        }

        foreach (var p in PhotonNetwork.PlayerList)
        {
            if (TournamentKeys.IsSitOut(p))
            {
                continue;
            }

            string id = TournamentState.ReadPlayerId(p);
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            string code = TournamentState.ReadDeckCode(p);
            string deckName = "";
            if (!string.IsNullOrEmpty(code))
            {
                try
                {
                    deckName = new DeckData(code).DeckName;
                }
                catch
                {
                    deckName = "";
                }
            }

            list.Add(new TournamentPlayerSlot
            {
                userId = id,
                nickName = TournamentState.ReadNickName(p),
                lockedDeckCode = code,
                lockedDeckName = deckName,
            });
        }

        return list;
    }

    void WriteLockedDeckProperty()
    {
        string localId = TournamentState.EnsureLocalPlayerId();
        var state = ContinuousController.instance.TournamentState;
        string code = state != null ? state.LockedDeckCode(localId) : null;
        if (string.IsNullOrEmpty(code) && ContinuousController.instance.BattleDeckData != null)
        {
            code = ContinuousController.instance.BattleDeckData.GetThisDeckCode();
        }

        var hash = PhotonNetwork.LocalPlayer.CustomProperties ?? new Hashtable();
        hash[TournamentKeys.PlayerIdProperty] = localId;
        if (!string.IsNullOrEmpty(code))
        {
            hash[TournamentKeys.LockedDeckProperty] = code;
            hash[ContinuousController.DeckDataPropertyKey] = code;
            ContinuousController.instance.BattleDeckData = new DeckData(code);
        }

        PhotonNetwork.LocalPlayer.SetCustomProperties(hash);
    }

    void SyncBanlistFromRoom()
    {
        if (!PhotonNetwork.InRoom)
        {
            return;
        }

        var hash = PhotonNetwork.CurrentRoom.CustomProperties;
        if (hash != null &&
            hash.TryGetValue(TournamentKeys.UseBanlistProperty, out object banObj) &&
            banObj is bool ban)
        {
            ContinuousController.instance.useBanlist = ban;
        }
    }

    void SyncPlayerCountFromRoom()
    {
        if (!PhotonNetwork.InRoom)
        {
            return;
        }

        var hash = PhotonNetwork.CurrentRoom.CustomProperties;
        if (hash.TryGetValue(TournamentKeys.PlayerCountProperty, out object countObj))
        {
            int size = TournamentKeys.NormalizePlayerCount(System.Convert.ToInt32(countObj));
            TournamentKeys.ActivePlayerCount = size;
        }
        else if (ContinuousController.instance.TournamentState != null &&
                 ContinuousController.instance.TournamentState.playerCount > 0)
        {
            TournamentKeys.ActivePlayerCount = ContinuousController.instance.TournamentState.playerCount;
        }
    }

    void MergeRoomState()
    {
        if (!PhotonNetwork.InRoom)
        {
            return;
        }

        SyncPlayerCountFromRoom();

        var hash = PhotonNetwork.CurrentRoom.CustomProperties;
        if (!hash.TryGetValue(TournamentKeys.StateProperty, out object jsonObj) || !(jsonObj is string json))
        {
            return;
        }

        var incoming = TournamentState.FromJson(json);
        if (incoming == null)
        {
            return;
        }

        var cc = ContinuousController.instance;
        if (cc.TournamentState == null)
        {
            cc.TournamentState = incoming;
        }
        else
        {
            cc.TournamentState.MergeFrom(incoming);
        }

        if (cc.TournamentState.playerCount > 0)
        {
            TournamentKeys.ActivePlayerCount = cc.TournamentState.playerCount;
        }

        cc.TournamentState.ApplyLocalDeckSnapshot(SnapshotLobbyPlayersAsMap());
        cc.isTournamentStarted = cc.isTournamentStarted || incoming.started;
        if (incoming.started)
        {
            WriteLockedDeckProperty();
        }
    }

    bool CanStartTournament()
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient)
        {
            return false;
        }

        int competitors = CountCompetitorPlayers();
        if (competitors < TournamentKeys.MinPlayersToStart || competitors > TournamentKeys.ActivePlayerCount)
        {
            return false;
        }

        return AllPresentPlayersReadyWithDecks();
    }

    int CountCompetitorPlayers()
    {
        if (!PhotonNetwork.InRoom)
        {
            return 0;
        }

        int n = 0;
        foreach (var p in PhotonNetwork.PlayerList)
        {
            if (!TournamentKeys.IsSitOut(p))
            {
                n++;
            }
        }

        return n;
    }

    bool AllPresentPlayersReadyWithDecks()
    {
        if (!PhotonNetwork.InRoom)
        {
            return false;
        }

        string readyKey = TournamentKeys.ReadyKey(PhotonNetwork.CurrentRoom.Name);
        int competitors = 0;
        foreach (var p in PhotonNetwork.PlayerList)
        {
            if (TournamentKeys.IsSitOut(p))
            {
                continue;
            }

            competitors++;
            if (!p.CustomProperties.TryGetValue(readyKey, out object readyObj) || !(readyObj is bool ready) || !ready)
            {
                return false;
            }

            string code = TournamentState.ReadDeckCode(p);
            if (string.IsNullOrEmpty(code))
            {
                return false;
            }

            try
            {
                if (!new DeckData(code).IsValidDeckData())
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        return competitors >= TournamentKeys.MinPlayersToStart;
    }

    bool CanReady()
    {
        if (!_endSetUp || ContinuousController.instance.isTournamentStarted || _selectingDeck || _sitOut)
        {
            return false;
        }

        if (Opening.instance.battle.selectBattleDeck.gameObject.activeSelf)
        {
            return false;
        }

        return ContinuousController.instance.BattleDeckData != null &&
               ContinuousController.instance.BattleDeckData.IsValidDeckData();
    }

    public void OnClickSitOut()
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient || ContinuousController.instance.isTournamentStarted)
        {
            return;
        }

        Opening.instance.PlayDecisionSE();
        _sitOut = !_sitOut;
        if (_sitOut)
        {
            SetReadyProperty(false);
        }

        WriteSitOutProperty(_sitOut);
        RefreshAllUi();
    }

    void WriteSitOutProperty(bool sitOut)
    {
        if (!PhotonNetwork.InRoom)
        {
            return;
        }

        var hash = PhotonNetwork.LocalPlayer.CustomProperties ?? new Hashtable();
        hash[TournamentKeys.SitOutProperty] = sitOut;
        hash[TournamentKeys.PlayerIdProperty] = TournamentState.EnsureLocalPlayerId();
        PhotonNetwork.LocalPlayer.SetCustomProperties(hash);
        _sitOut = sitOut;
    }

    void SetReadyProperty(bool ready)
    {
        if (!PhotonNetwork.InRoom)
        {
            return;
        }

        if (_sitOut)
        {
            ready = false;
        }

        string key = TournamentKeys.ReadyKey(PhotonNetwork.CurrentRoom.Name);
        var hash = PhotonNetwork.LocalPlayer.CustomProperties ?? new Hashtable();
        hash[key] = ready;
        hash[TournamentKeys.PlayerIdProperty] = TournamentState.EnsureLocalPlayerId();
        hash[TournamentKeys.SitOutProperty] = _sitOut;
        PhotonNetwork.LocalPlayer.SetCustomProperties(hash);
        _isReady = ready;
    }

    DeckData FirstValidDeckData()
    {
        if (ContinuousController.instance.DeckDatas == null)
        {
            return null;
        }

        foreach (var deck in ContinuousController.instance.DeckDatas)
        {
            if (deck != null && deck.IsValidDeckData())
            {
                return deck;
            }
        }

        return null;
    }

    public override void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer)
    {
        if (_endSetUp)
        {
            RefreshAllUi();
        }
    }

    public override void OnPlayerLeftRoom(Photon.Realtime.Player otherPlayer)
    {
        if (_endSetUp)
        {
            RefreshAllUi();
        }
    }

    public override void OnPlayerPropertiesUpdate(Photon.Realtime.Player targetPlayer, Hashtable changedProps)
    {
        if (_endSetUp)
        {
            RefreshAllUi();
        }
    }

    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        bool startedNow = propertiesThatChanged != null &&
                          propertiesThatChanged.ContainsKey(TournamentKeys.StartedProperty);

        MergeRoomState();

        if (startedNow && ContinuousController.instance.isTournamentStarted && !_dispatching &&
            PhotonNetwork.InRoom &&
            PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(TournamentKeys.RoomKindProperty, out object kind) &&
            kind is string kindStr && kindStr == TournamentKeys.RoomKindLobby)
        {
            ContinuousController.instance.StartCoroutine(DispatchToQuarterfinals());
            return;
        }

        if (TryDispatchReadyMatchFromWaitHub())
        {
            return;
        }

        if (_endSetUp)
        {
            RefreshAllUi();
        }
    }

    public bool TryDispatchReadyMatchFromWaitHub()
    {
        if (_dispatching || GManager.instance != null || !PhotonNetwork.InRoom)
        {
            return false;
        }

        var director = TournamentServices.EnsureExists().Match;
        if (director != null && director.RoutingAfterSeries)
        {
            return false;
        }

        if (!PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(TournamentKeys.RoomKindProperty, out object kind) ||
            !(kind is string kindStr) || kindStr != TournamentKeys.RoomKindWaitHub)
        {
            return false;
        }

        var state = ContinuousController.instance != null ? ContinuousController.instance.TournamentState : null;
        var match = state != null ? state.FindActiveMatchFor(TournamentState.EnsureLocalPlayerId()) : null;
        if (!TournamentKeys.IsReadyTwoPlayerMatch(match))
        {
            return false;
        }

        ContinuousController.instance.StartCoroutine(DispatchToQuarterfinals());
        return true;
    }

    void Update()
    {
        if (!_endSetUp || Parent == null || !Parent.activeSelf || GManager.instance != null)
        {
            return;
        }

        RefreshButtons();
        TryDispatchReadyMatchFromWaitHub();
        if (PhotonNetwork.InRoom && _playerCount != PhotonNetwork.CurrentRoom.PlayerCount)
        {
            RefreshPlayerList();
        }
    }

    void ShowPanel()
    {
        EnsureUi();
        if (Parent == null)
        {
            return;
        }

        Parent.SetActive(true);
        Parent.transform.SetAsLastSibling();
        Opening.instance.battle?.selectBattleMode?.HideOverlayDialogs();
        var modeWindow = Opening.instance.battle?.selectBattleMode?.selectBattleModeWindow;
        if (modeWindow != null)
        {
            modeWindow.Off();
        }
    }

    void RefreshAllUi()
    {
        EnsureUi();
        if (_roomIdText != null)
        {
            string id = _tourneyId ?? (PhotonNetwork.InRoom ? TournamentKeys.DisplayRoomId(PhotonNetwork.CurrentRoom.Name) : "");
            _roomIdText.text = string.IsNullOrEmpty(id) ? "" : id;
        }

        RefreshPlayerList();
        RefreshDeckLabel();
        RefreshButtons();
        RefreshBracketAndStatus();
    }

    void RefreshPlayerList()
    {
        if (_playerParent == null)
        {
            return;
        }

        for (int i = _playerParent.childCount - 1; i >= 0; i--)
        {
            Destroy(_playerParent.GetChild(i).gameObject);
        }

        _playerCount = PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.PlayerCount : 0;
        var state = ContinuousController.instance.TournamentState;
        bool started = ContinuousController.instance.isTournamentStarted;

        if (started && state != null && state.players != null)
        {
            string localId = TournamentState.EnsureLocalPlayerId();
            if (!string.IsNullOrEmpty(state.adminUserId))
            {
                string adminName = string.IsNullOrEmpty(state.adminNickName) ? state.adminUserId : state.adminNickName;
                if (state.adminUserId == localId)
                {
                    adminName += " (You)";
                }

                SpawnPlayerRow(
                    adminName,
                    LocalizeUtility.GetLocalizedString(EngMessage: "Admin", JpnMessage: "管理者"),
                    new Color32(120, 170, 255, 255),
                    state.adminUserId == localId);
            }

            foreach (var slot in state.players)
            {
                if (slot == null)
                {
                    continue;
                }

                if (TournamentKeys.IsBye(slot.userId))
                {
                    SpawnPlayerRow(
                        "BYE",
                        LocalizeUtility.GetLocalizedString(EngMessage: "Bye", JpnMessage: "不戦勝枠"),
                        new Color32(140, 150, 160, 255),
                        highlight: false,
                        empty: true);
                    continue;
                }

                string name = slot.userId == localId ? $"{slot.nickName} (You)" : slot.nickName;
                string status;
                Color color;
                if (!string.IsNullOrEmpty(state.championUserId) && slot.userId == state.championUserId)
                {
                    status = LocalizeUtility.GetLocalizedString(EngMessage: "Champion", JpnMessage: "優勝");
                    color = new Color32(255, 215, 0, 255);
                }
                else if (slot.eliminated)
                {
                    status = LocalizeUtility.GetLocalizedString(EngMessage: "Eliminated", JpnMessage: "敗退");
                    color = new Color32(220, 80, 80, 255);
                }
                else
                {
                    status = LocalizeUtility.GetLocalizedString(EngMessage: "Playing", JpnMessage: "出場中");
                    color = new Color32(80, 210, 90, 255);
                }

                SpawnPlayerRow(name, status, color, slot.userId == localId);
            }

            return;
        }

        if (!PhotonNetwork.InRoom)
        {
            for (int i = 0; i < TournamentKeys.ActivePlayerCount; i++)
            {
                SpawnEmptySlot(i + 1);
            }

            return;
        }

        string readyKey = TournamentKeys.ReadyKey(PhotonNetwork.CurrentRoom.Name);
        int slotIndex = 0;
        foreach (var p in PhotonNetwork.PlayerList)
        {
            string name = TournamentState.ReadNickName(p);
            if (p.IsLocal)
            {
                name += " (You)";
            }

            if (TournamentKeys.IsSitOut(p))
            {
                SpawnPlayerRow(
                    name,
                    LocalizeUtility.GetLocalizedString(EngMessage: "Admin (sit out)", JpnMessage: "管理者（不参加）"),
                    new Color32(120, 170, 255, 255),
                    p.IsLocal);
                continue;
            }

            bool ready = p.CustomProperties.TryGetValue(readyKey, out object v) && v is bool b && b;
            SpawnPlayerRow(
                name,
                ready
                    ? LocalizeUtility.GetLocalizedString(EngMessage: "Ready", JpnMessage: "準備完了")
                    : LocalizeUtility.GetLocalizedString(EngMessage: "Not ready", JpnMessage: "未準備"),
                ready ? new Color32(80, 210, 90, 255) : new Color32(230, 90, 90, 255),
                p.IsLocal);
            slotIndex++;
        }

        for (int i = slotIndex; i < TournamentKeys.ActivePlayerCount; i++)
        {
            SpawnEmptySlot(i + 1);
        }
    }

    void SpawnEmptySlot(int number)
    {
        SpawnPlayerRow(
            LocalizeUtility.GetLocalizedString(EngMessage: $"Open slot {number}", JpnMessage: $"空き {number}"),
            LocalizeUtility.GetLocalizedString(EngMessage: "Waiting", JpnMessage: "待機中"),
            new Color32(160, 170, 180, 255),
            highlight: false,
            empty: true);
    }

    void SpawnPlayerRow(string name, string status, Color statusColor, bool highlight, bool empty = false)
    {
        var go = new GameObject("PlayerRow", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(_playerParent, false);
        var le = go.GetComponent<LayoutElement>();
        le.minHeight = 46f;
        le.preferredHeight = 46f;
        var bg = go.GetComponent<Image>();
        bg.color = highlight
            ? new Color(0.18f, 0.36f, 0.62f, 0.55f)
            : empty
                ? new Color(1f, 1f, 1f, 0.04f)
                : new Color(1f, 1f, 1f, 0.08f);

        var nameText = MakeChildText(go.transform, "Name", 20, TextAnchor.MiddleLeft, new Vector2(0.02f, 0f), new Vector2(0.62f, 1f));
        nameText.text = name;
        nameText.color = empty ? new Color(1f, 1f, 1f, 0.45f) : Color.white;

        var statusText = MakeChildText(go.transform, "Status", 18, TextAnchor.MiddleRight, new Vector2(0.62f, 0f), new Vector2(0.98f, 1f));
        statusText.text = status;
        statusText.color = statusColor;
        statusText.fontStyle = FontStyle.Bold;
    }

    void RefreshDeckLabel()
    {
        var deck = ContinuousController.instance.BattleDeckData;
        bool started = ContinuousController.instance.isTournamentStarted;
        if (started)
        {
            string localId = TournamentState.EnsureLocalPlayerId();
            var state = ContinuousController.instance.TournamentState;
            if (state != null && state.IsAdmin(localId))
            {
                _deckText.text = LocalizeUtility.GetLocalizedString(
                    EngMessage: "Admin — not playing",
                    JpnMessage: "管理者 — 不参加");
                if (_noDeckObject != null) _noDeckObject.SetActive(false);
                if (_invalidDeckObject != null) _invalidDeckObject.SetActive(false);
                return;
            }

            var slot = state != null ? state.GetPlayer(localId) : null;
            string lockedName = slot != null && !string.IsNullOrEmpty(slot.lockedDeckName)
                ? slot.lockedDeckName
                : (deck != null ? deck.DeckName : "—");
            _deckText.text = LocalizeUtility.GetLocalizedString(
                EngMessage: $"Locked: {lockedName}",
                JpnMessage: $"ロック: {lockedName}");
            if (_noDeckObject != null) _noDeckObject.SetActive(false);
            if (_invalidDeckObject != null) _invalidDeckObject.SetActive(false);
            return;
        }

        if (_sitOut)
        {
            _deckText.text = LocalizeUtility.GetLocalizedString(
                EngMessage: "Sitting out as admin",
                JpnMessage: "管理者として不参加");
            if (_noDeckObject != null) _noDeckObject.SetActive(false);
            if (_invalidDeckObject != null) _invalidDeckObject.SetActive(false);
            return;
        }

        if (deck == null)
        {
            _deckText.text = LocalizeUtility.GetLocalizedString(EngMessage: "No deck selected", JpnMessage: "デッキ未選択");
            if (_noDeckObject != null) _noDeckObject.SetActive(true);
            if (_invalidDeckObject != null) _invalidDeckObject.SetActive(false);
            return;
        }

        _deckText.text = deck.DeckName;
        bool valid = deck.IsValidDeckData();
        if (_noDeckObject != null) _noDeckObject.SetActive(deck.AllDeckCards().Count == 0);
        if (_invalidDeckObject != null) _invalidDeckObject.SetActive(!valid);
    }

    void RefreshButtons()
    {
        bool started = ContinuousController.instance != null && ContinuousController.instance.isTournamentStarted;
        bool inWaitHub = PhotonNetwork.InRoom &&
                         PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(TournamentKeys.RoomKindProperty, out object kind) &&
                         kind is string ks && ks == TournamentKeys.RoomKindWaitHub;

        bool showSitOut = !started && PhotonNetwork.IsMasterClient;
        if (_sitOutButton != null)
        {
            _sitOutButton.gameObject.SetActive(showSitOut);
            if (showSitOut)
            {
                _sitOutButton.interactable = _endSetUp;
                if (_sitOutButtonText != null)
                {
                    _sitOutButtonText.text = _sitOut
                        ? LocalizeUtility.GetLocalizedString(EngMessage: "Join bracket", JpnMessage: "出場する")
                        : LocalizeUtility.GetLocalizedString(EngMessage: "Sit out (admin)", JpnMessage: "不参加（管理者）");
                }

                if (_sitOutButtonImage != null)
                {
                    _sitOutButtonImage.color = _sitOut
                        ? new Color(0.25f, 0.45f, 0.75f, 1f)
                        : new Color(0.35f, 0.38f, 0.42f, 1f);
                }
            }
        }

        _selectDeckButton.gameObject.SetActive(!started && !_sitOut);
        _selectDeckButton.interactable = !started && !_sitOut && _endSetUp;

        if (started)
        {
            _readyButton.gameObject.SetActive(false);
            _startButton.gameObject.SetActive(false);
        }
        else
        {
            _readyButton.gameObject.SetActive(!_sitOut);
            bool canReady = CanReady();
            _readyButton.interactable = canReady;
            _readyButtonText.text = _isReady
                ? LocalizeUtility.GetLocalizedString(EngMessage: "Cancel Ready", JpnMessage: "準備解除")
                : LocalizeUtility.GetLocalizedString(EngMessage: "Ready", JpnMessage: "準備完了");
            if (_readyButtonImage != null)
            {
                _readyButtonImage.color = !canReady
                    ? new Color(0.35f, 0.38f, 0.42f, 1f)
                    : _isReady
                        ? new Color(0.72f, 0.42f, 0.12f, 1f)
                        : new Color(0.16f, 0.58f, 0.30f, 1f);
            }

            bool isMaster = PhotonNetwork.IsMasterClient;
            _startButton.gameObject.SetActive(isMaster);
            bool canStart = isMaster && CanStartTournament();
            int competitors = CountCompetitorPlayers();
            int need = TournamentKeys.ActivePlayerCount;
            int byeSeats = Mathf.Max(0, need - competitors);
            _startButton.interactable = canStart;
            _startButtonText.text = canStart
                ? LocalizeUtility.GetLocalizedString(
                    EngMessage: byeSeats > 0
                        ? $"Start ({byeSeats} byes)"
                        : "Start Tournament",
                    JpnMessage: byeSeats > 0
                        ? $"開始（BYE {byeSeats}）"
                        : "トーナメント開始")
                : LocalizeUtility.GetLocalizedString(
                    EngMessage: $"Start (need {TournamentKeys.MinPlayersToStart}+ ready)",
                    JpnMessage: $"開始（{TournamentKeys.MinPlayersToStart}人以上準備完了）");
            if (_startButtonImage != null)
            {
                _startButtonImage.color = canStart
                    ? new Color(0.78f, 0.58f, 0.12f, 1f)
                    : new Color(0.35f, 0.38f, 0.42f, 1f);
            }
        }

        _leaveButton.gameObject.SetActive(true);
        _copyIdButton.gameObject.SetActive(!started || inWaitHub);

        if (_titleText != null)
        {
            _titleText.text = started
                ? LocalizeUtility.GetLocalizedString(EngMessage: "Tournament", JpnMessage: "トーナメント")
                : LocalizeUtility.GetLocalizedString(EngMessage: "Tournament Lobby", JpnMessage: "トーナメントロビー");
        }
    }

    void RefreshBracketAndStatus()
    {
        var state = ContinuousController.instance.TournamentState;
        bool started = ContinuousController.instance.isTournamentStarted;
        if (_howToPanel != null)
        {
            _howToPanel.SetActive(!started);
            if (!started && _howToText != null)
            {
                int size = TournamentKeys.ActivePlayerCount;
                _howToText.text = LocalizeUtility.GetLocalizedString(
                    EngMessage: $"How to play\n\n1. Share the Room ID (up to {size} players + optional admin).\n2. Choose your deck.\n3. Tap Ready.\n4. Host can Sit out as admin, or start with {TournamentKeys.MinPlayersToStart}+ ready — empty seats become byes.\n\nBracket size: {size}\nDecks lock when the tournament starts.\nMatches are Best of 3.\nWaiting players can Spectate live matches.",
                    JpnMessage: $"遊び方\n\n1. ルームIDを共有（最大{size}人＋管理者任意）\n2. デッキを選ぶ\n3. 準備完了を押す\n4. ホストは管理者不参加も可。{TournamentKeys.MinPlayersToStart}人以上で開始 — 空き枠はBYE\n\n枠: {size}人\n開始後はデッキ変更できません。\n試合は3本先取です。\n待機中は観戦できます。");
            }
        }

        if (_bracketScroll != null)
        {
            _bracketScroll.gameObject.SetActive(started);
        }

        RefreshSpectateList();

        if (started && state != null)
        {
            _bracketText.text = state.FormatBracket();
            if (!string.IsNullOrEmpty(state.championUserId))
            {
                _statusText.text = LocalizeUtility.GetLocalizedString(
                    EngMessage: $"Champion: {state.DisplayName(state.championUserId)}",
                    JpnMessage: $"優勝: {state.DisplayName(state.championUserId)}");
            }
            else
            {
                _statusText.text = LocalizeUtility.GetLocalizedString(
                    EngMessage: "Tournament in progress — Spectate a match or watch the bracket (leave spectate before your match)",
                    JpnMessage: "トーナメント進行中 — 観戦するか表を確認（自分の試合前に観戦を終了）");
            }

            return;
        }

        int competitors = CountCompetitorPlayers();
        int readyCount = CountReadyPlayers();
        int need = TournamentKeys.ActivePlayerCount;
        int byeSeats = Mathf.Max(0, need - competitors);
        bool allPresentReady = competitors >= TournamentKeys.MinPlayersToStart && readyCount >= competitors && competitors > 0;
        if (competitors < TournamentKeys.MinPlayersToStart)
        {
            _statusText.text = LocalizeUtility.GetLocalizedString(
                EngMessage: $"{competitors}/{need} competitors — need {TournamentKeys.MinPlayersToStart}+ to start (empty → bye)",
                JpnMessage: $"競技者{competitors}/{need} — 開始には{TournamentKeys.MinPlayersToStart}人以上（空き→BYE）");
        }
        else if (!allPresentReady)
        {
            _statusText.text = LocalizeUtility.GetLocalizedString(
                EngMessage: $"{readyCount}/{competitors} ready ({byeSeats} byes if start now)",
                JpnMessage: $"{readyCount}/{competitors}人準備完了（今開始ならBYE {byeSeats}）");
        }
        else
        {
            _statusText.text = LocalizeUtility.GetLocalizedString(
                EngMessage: byeSeats > 0
                    ? $"{competitors} ready — host can start ({byeSeats} byes)"
                    : $"{competitors}/{need} ready — host can start",
                JpnMessage: byeSeats > 0
                    ? $"{competitors}人準備完了 — ホストが開始可（BYE {byeSeats}）"
                    : $"{competitors}/{need}人準備完了 — ホストが開始できます");
        }
    }

    void RefreshSpectateList()
    {
        if (_spectateParent == null)
        {
            return;
        }

        for (int i = _spectateParent.childCount - 1; i >= 0; i--)
        {
            Destroy(_spectateParent.GetChild(i).gameObject);
        }

        var state = ContinuousController.instance != null ? ContinuousController.instance.TournamentState : null;
        bool started = ContinuousController.instance != null && ContinuousController.instance.isTournamentStarted;
        bool inWaitHub = PhotonNetwork.InRoom &&
                         PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(TournamentKeys.RoomKindProperty, out object kind) &&
                         kind is string ks && ks == TournamentKeys.RoomKindWaitHub;

        _spectateParent.gameObject.SetActive(started && inWaitHub && state != null);
        if (!started || !inWaitHub || state == null)
        {
            return;
        }

        string localId = TournamentState.EnsureLocalPlayerId();
        var ownMatch = state.FindActiveMatchFor(localId);
        var live = state.ListSpectatableMatches();
        var font = ResolveFont();

        var header = new GameObject("SpectateHeader", typeof(RectTransform), typeof(LayoutElement));
        header.transform.SetParent(_spectateParent, false);
        header.GetComponent<LayoutElement>().preferredHeight = 28f;
        var headerText = header.AddComponent<Text>();
        headerText.font = font;
        headerText.fontSize = 18;
        headerText.fontStyle = FontStyle.Bold;
        headerText.color = Color.white;
        headerText.alignment = TextAnchor.MiddleLeft;
        headerText.text = LocalizeUtility.GetLocalizedString(EngMessage: "Spectate", JpnMessage: "観戦");

        if (live.Count == 0)
        {
            var empty = new GameObject("NoMatches", typeof(RectTransform), typeof(LayoutElement));
            empty.transform.SetParent(_spectateParent, false);
            empty.GetComponent<LayoutElement>().preferredHeight = 28f;
            var emptyText = empty.AddComponent<Text>();
            emptyText.font = font;
            emptyText.fontSize = 16;
            emptyText.color = new Color(1f, 1f, 1f, 0.55f);
            emptyText.alignment = TextAnchor.MiddleLeft;
            emptyText.text = LocalizeUtility.GetLocalizedString(
                EngMessage: "No live matches right now",
                JpnMessage: "観戦できる試合はありません");
            return;
        }

        foreach (var match in live)
        {
            if (ownMatch != null &&
                match.round == ownMatch.round &&
                match.matchIndex == ownMatch.matchIndex &&
                TournamentKeys.IsReadyTwoPlayerMatch(ownMatch))
            {
                // Own ready match — dispatch as competitor, do not offer spectate.
                continue;
            }

            string a = state.DisplayName(match.userIdA);
            string b = state.DisplayName(match.userIdB);
            string roundName = TournamentKeys.RoundDisplayNameFor(state.ResolvedPlayerCount, match.round);
            int gameNumber = match.gameIndex + 1;
            string label = $"{roundName} M{match.matchIndex + 1}: {a} vs {b}  (Game {gameNumber}/3  {match.seriesWinsA}-{match.seriesWinsB})";

            int round = match.round;
            int matchIndex = match.matchIndex;
            string buttonLabel = LocalizeUtility.GetLocalizedString(
                EngMessage: $"Spectate — {label}",
                JpnMessage: $"観戦 — {label}");

            var btn = MakeButton(
                _spectateParent,
                $"Spectate_{round}_{matchIndex}",
                font,
                buttonLabel,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                Vector2.zero,
                new Vector2(0f, 40f),
                () => OnClickSpectate(round, matchIndex),
                new Color(0.22f, 0.42f, 0.62f, 1f));
            var le = btn.gameObject.GetComponent<LayoutElement>();
            if (le == null)
            {
                le = btn.gameObject.AddComponent<LayoutElement>();
            }

            le.minHeight = 40f;
            le.preferredHeight = 40f;
            var btnRt = btn.GetComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(0f, 1f);
            btnRt.anchorMax = new Vector2(1f, 1f);
            btnRt.pivot = new Vector2(0.5f, 1f);
            btnRt.sizeDelta = new Vector2(0f, 40f);
            var btnText = btn.GetComponentInChildren<Text>();
            if (btnText != null)
            {
                btnText.fontSize = 15;
            }
        }
    }

    void OnClickSpectate(int round, int matchIndex)
    {
        // Already waiting on / in a spectate session — don't spam warnings.
        if (_spectateBusy)
        {
            return;
        }

        if (GManager.instance != null)
        {
            return;
        }

        // Own ready match must be played, not spectated.
        var state = ContinuousController.instance != null ? ContinuousController.instance.TournamentState : null;
        string localId = TournamentState.EnsureLocalPlayerId();
        var own = state != null ? state.FindActiveMatchFor(localId) : null;
        if (own != null &&
            TournamentKeys.IsReadyTwoPlayerMatch(own) &&
            own.round == round &&
            own.matchIndex == matchIndex)
        {
            return;
        }

        Debug.Log($"[Tournament] Spectate clicked round={round} match={matchIndex}");
        Opening.instance.PlayDecisionSE();
        ContinuousController.instance.StartCoroutine(SpectateMatchCoroutine(round, matchIndex));
    }

    IEnumerator SpectateMatchCoroutine(int round, int matchIndex)
    {
        if (_spectateBusy)
        {
            yield break;
        }

        _spectateBusy = true;
        _endSetUp = false;
        // Don't let a stale match-dispatch lock block returning to the hub later.
        _dispatching = false;

        try
        {
            yield return TournamentServices.EnsureExists().Match.JoinMatchAsSpectatorCoroutine(round, matchIndex);
        }
        finally
        {
            _spectateBusy = false;
            _dispatching = false;
        }
    }

    int CountReadyPlayers()
    {
        if (!PhotonNetwork.InRoom)
        {
            return 0;
        }

        string readyKey = TournamentKeys.ReadyKey(PhotonNetwork.CurrentRoom.Name);
        int count = 0;
        foreach (var p in PhotonNetwork.PlayerList)
        {
            if (TournamentKeys.IsSitOut(p))
            {
                continue;
            }

            if (p.CustomProperties.TryGetValue(readyKey, out object v) && v is bool ready && ready)
            {
                count++;
            }
        }

        return count;
    }

    public void OnClickCopyRoomId()
    {
        if (string.IsNullOrEmpty(_tourneyId) && PhotonNetwork.InRoom)
        {
            _tourneyId = TournamentKeys.DisplayRoomId(PhotonNetwork.CurrentRoom.Name);
        }

        if (string.IsNullOrEmpty(_tourneyId))
        {
            return;
        }

        GUIUtility.systemCopyBuffer = _tourneyId;
        if (_copyFeedback != null)
        {
            StopCoroutine(_copyFeedback);
        }

        _copyFeedback = StartCoroutine(CopyIdFeedback());
    }

    IEnumerator CopyIdFeedback()
    {
        if (_copyIdButtonText != null)
        {
            _copyIdButtonText.text = LocalizeUtility.GetLocalizedString(EngMessage: "Copied!", JpnMessage: "コピーした!");
        }

        yield return new WaitForSeconds(1.2f);
        if (_copyIdButtonText != null)
        {
            _copyIdButtonText.text = LocalizeUtility.GetLocalizedString(EngMessage: "Copy ID", JpnMessage: "IDコピー");
        }

        _copyFeedback = null;
    }

    Font ResolveFont()
    {
        var rm = Opening.instance != null ? Opening.instance.battle?.roomManager : null;
        if (rm != null && rm.RoomIDText != null && rm.RoomIDText.font != null)
        {
            return rm.RoomIDText.font;
        }

        var arial = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return arial != null ? arial : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    void EnsureUi()
    {
        if (Parent != null && _howToPanel != null && _spectateParent != null && _sitOutButton != null)
        {
            return;
        }

        if (Parent != null)
        {
            Destroy(Parent);
            Parent = null;
            _howToPanel = null;
            _spectateParent = null;
            _sitOutButton = null;
        }

        var canvas = Opening.instance != null ? Opening.instance.canvasRect : null;
        if (canvas == null)
        {
            return;
        }

        Font font = ResolveFont();

        Parent = new GameObject("TournamentLobbyPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        Parent.transform.SetParent(canvas, false);
        Stretch(Parent.GetComponent<RectTransform>());
        var dim = Parent.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.72f);
        dim.raycastTarget = true;

        var card = CreatePanel(Parent.transform, "Card", new Color(0.07f, 0.11f, 0.18f, 0.98f));
        var cardRt = card.GetComponent<RectTransform>();
        cardRt.anchorMin = new Vector2(0.5f, 0.5f);
        cardRt.anchorMax = new Vector2(0.5f, 0.5f);
        cardRt.sizeDelta = new Vector2(1180f, 720f);
        cardRt.anchoredPosition = Vector2.zero;

        var header = CreatePanel(card.transform, "Header", new Color(0.10f, 0.18f, 0.30f, 1f));
        var headerRt = header.GetComponent<RectTransform>();
        headerRt.anchorMin = new Vector2(0f, 1f);
        headerRt.anchorMax = new Vector2(1f, 1f);
        headerRt.pivot = new Vector2(0.5f, 1f);
        headerRt.sizeDelta = new Vector2(0f, 88f);
        headerRt.anchoredPosition = Vector2.zero;

        _titleText = MakeChildText(header.transform, "Title", 30, TextAnchor.MiddleLeft, new Vector2(0.02f, 0.45f), new Vector2(0.42f, 0.95f));
        _titleText.text = "Tournament Lobby";
        _titleText.fontStyle = FontStyle.Bold;

        _statusText = MakeChildText(header.transform, "Status", 18, TextAnchor.MiddleLeft, new Vector2(0.02f, 0.05f), new Vector2(0.62f, 0.48f));
        _statusText.color = new Color(0.85f, 0.90f, 1f, 0.95f);

        var idLabel = MakeChildText(header.transform, "IdLabel", 16, TextAnchor.MiddleRight, new Vector2(0.42f, 0.52f), new Vector2(0.62f, 0.92f));
        idLabel.text = LocalizeUtility.GetLocalizedString(EngMessage: "Room ID", JpnMessage: "ルームID");
        idLabel.color = new Color(1f, 1f, 1f, 0.7f);

        _roomIdText = MakeChildText(header.transform, "RoomId", 28, TextAnchor.MiddleRight, new Vector2(0.42f, 0.08f), new Vector2(0.62f, 0.58f));
        _roomIdText.fontStyle = FontStyle.Bold;

        _copyIdButton = MakeButton(header.transform, "CopyId", font,
            LocalizeUtility.GetLocalizedString(EngMessage: "Copy ID", JpnMessage: "IDコピー"),
            new Vector2(0.64f, 0.22f), new Vector2(0.80f, 0.78f), Vector2.zero, Vector2.zero,
            OnClickCopyRoomId, new Color(0.18f, 0.45f, 0.78f, 1f));
        _copyIdButtonText = _copyIdButton.GetComponentInChildren<Text>();

        _leaveButton = MakeButton(header.transform, "Leave", font,
            LocalizeUtility.GetLocalizedString(EngMessage: "Leave", JpnMessage: "退出"),
            new Vector2(0.82f, 0.22f), new Vector2(0.98f, 0.78f), Vector2.zero, Vector2.zero,
            CloseLobby, new Color(0.55f, 0.22f, 0.22f, 1f));

        var body = new GameObject("Body", typeof(RectTransform));
        body.transform.SetParent(card.transform, false);
        var bodyRt = body.GetComponent<RectTransform>();
        bodyRt.anchorMin = new Vector2(0f, 0.16f);
        bodyRt.anchorMax = new Vector2(1f, 1f);
        bodyRt.offsetMin = new Vector2(18f, 8f);
        bodyRt.offsetMax = new Vector2(-18f, -100f);

        var listGo = CreatePanel(body.transform, "PlayerList", new Color(0f, 0f, 0f, 0.28f));
        var listRt = listGo.GetComponent<RectTransform>();
        listRt.anchorMin = new Vector2(0f, 0f);
        listRt.anchorMax = new Vector2(0.48f, 1f);
        listRt.offsetMin = Vector2.zero;
        listRt.offsetMax = new Vector2(-8f, 0f);

        var listTitle = MakeChildText(listGo.transform, "ListTitle", 18, TextAnchor.MiddleLeft, new Vector2(0.04f, 0.92f), new Vector2(0.96f, 1f));
        listTitle.text = LocalizeUtility.GetLocalizedString(EngMessage: "Players", JpnMessage: "プレイヤー");
        listTitle.fontStyle = FontStyle.Bold;

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(listGo.transform, false);
        var viewportRt = viewport.GetComponent<RectTransform>();
        viewportRt.anchorMin = new Vector2(0f, 0f);
        viewportRt.anchorMax = new Vector2(1f, 0.92f);
        viewportRt.offsetMin = new Vector2(8f, 8f);
        viewportRt.offsetMax = new Vector2(-8f, -4f);

        var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        var contentRt = content.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = new Vector2(0f, 0f);
        var vlg = content.GetComponent<VerticalLayoutGroup>();
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.spacing = 6f;
        vlg.padding = new RectOffset(4, 4, 4, 4);
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = listGo.AddComponent<ScrollRect>();
        scroll.content = contentRt;
        scroll.viewport = viewportRt;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        _playerParent = content.transform;

        var right = CreatePanel(body.transform, "Right", new Color(0f, 0f, 0f, 0.28f));
        var rightRt = right.GetComponent<RectTransform>();
        rightRt.anchorMin = new Vector2(0.52f, 0f);
        rightRt.anchorMax = new Vector2(1f, 1f);
        rightRt.offsetMin = new Vector2(8f, 0f);
        rightRt.offsetMax = Vector2.zero;

        _howToPanel = new GameObject("HowTo", typeof(RectTransform));
        _howToPanel.transform.SetParent(right.transform, false);
        Stretch(_howToPanel.GetComponent<RectTransform>());
        _howToText = MakeChildText(_howToPanel.transform, "HowToText", 22, TextAnchor.UpperLeft, new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.95f));
        _howToText.horizontalOverflow = HorizontalWrapMode.Wrap;
        _howToText.verticalOverflow = VerticalWrapMode.Overflow;
        _howToText.lineSpacing = 1.15f;
        int size = TournamentKeys.ActivePlayerCount;
        _howToText.text = LocalizeUtility.GetLocalizedString(
            EngMessage: $"How to play\n\n1. Share the Room ID (up to {size} players).\n2. Choose your deck.\n3. Tap Ready.\n4. Host can start with {TournamentKeys.MinPlayersToStart}+ ready — empty seats become byes.\n\nBracket size: {size}\nDecks lock when the tournament starts.\nMatches are Best of 3.",
            JpnMessage: $"遊び方\n\n1. ルームIDを共有（最大{size}人）\n2. デッキを選ぶ\n3. 準備完了を押す\n4. ホストは{TournamentKeys.MinPlayersToStart}人以上で開始可 — 空き枠はBYE\n\n枠: {size}人\n開始後はデッキ変更できません。\n試合は3本先取です。");

        var bracketGo = CreatePanel(right.transform, "Bracket", new Color(0f, 0f, 0f, 0f));
        Stretch(bracketGo.GetComponent<RectTransform>());
        var bracketTitle = MakeChildText(bracketGo.transform, "BracketTitle", 18, TextAnchor.MiddleLeft, new Vector2(0.04f, 0.92f), new Vector2(0.96f, 1f));
        bracketTitle.text = LocalizeUtility.GetLocalizedString(EngMessage: "Bracket", JpnMessage: "トーナメント表");
        bracketTitle.fontStyle = FontStyle.Bold;

        var bracketViewport = new GameObject("BracketViewport", typeof(RectTransform), typeof(RectMask2D));
        bracketViewport.transform.SetParent(bracketGo.transform, false);
        var bViewportRt = bracketViewport.GetComponent<RectTransform>();
        bViewportRt.anchorMin = new Vector2(0f, 0.38f);
        bViewportRt.anchorMax = new Vector2(1f, 0.92f);
        bViewportRt.offsetMin = new Vector2(10f, 4f);
        bViewportRt.offsetMax = new Vector2(-10f, -4f);

        var bracketContent = new GameObject("BracketContent", typeof(RectTransform));
        bracketContent.transform.SetParent(bracketViewport.transform, false);
        var bRt = bracketContent.GetComponent<RectTransform>();
        bRt.anchorMin = new Vector2(0f, 1f);
        bRt.anchorMax = new Vector2(1f, 1f);
        bRt.pivot = new Vector2(0.5f, 1f);
        bRt.anchoredPosition = Vector2.zero;
        bRt.sizeDelta = new Vector2(0f, 560f);
        _bracketText = bracketContent.AddComponent<Text>();
        _bracketText.font = font;
        _bracketText.fontSize = 20;
        _bracketText.color = Color.white;
        _bracketText.alignment = TextAnchor.UpperLeft;
        _bracketText.horizontalOverflow = HorizontalWrapMode.Wrap;
        _bracketText.verticalOverflow = VerticalWrapMode.Overflow;
        _bracketScroll = bracketGo.AddComponent<ScrollRect>();
        _bracketScroll.content = bRt;
        _bracketScroll.viewport = bViewportRt;
        _bracketScroll.horizontal = false;
        bracketGo.SetActive(false);

        var spectateViewport = new GameObject("SpectateViewport", typeof(RectTransform), typeof(RectMask2D));
        spectateViewport.transform.SetParent(bracketGo.transform, false);
        var sViewportRt = spectateViewport.GetComponent<RectTransform>();
        sViewportRt.anchorMin = new Vector2(0f, 0f);
        sViewportRt.anchorMax = new Vector2(1f, 0.36f);
        sViewportRt.offsetMin = new Vector2(10f, 8f);
        sViewportRt.offsetMax = new Vector2(-10f, -2f);

        var spectateContent = new GameObject("SpectateContent", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        spectateContent.transform.SetParent(spectateViewport.transform, false);
        var sRt = spectateContent.GetComponent<RectTransform>();
        sRt.anchorMin = new Vector2(0f, 1f);
        sRt.anchorMax = new Vector2(1f, 1f);
        sRt.pivot = new Vector2(0.5f, 1f);
        sRt.anchoredPosition = Vector2.zero;
        sRt.sizeDelta = new Vector2(0f, 0f);
        var sVlg = spectateContent.GetComponent<VerticalLayoutGroup>();
        sVlg.childForceExpandHeight = false;
        sVlg.childForceExpandWidth = true;
        sVlg.childControlHeight = true;
        sVlg.childControlWidth = true;
        sVlg.spacing = 4f;
        sVlg.padding = new RectOffset(2, 2, 2, 2);
        spectateContent.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _spectateParent = spectateContent.transform;

        var footer = CreatePanel(card.transform, "Footer", new Color(0.09f, 0.14f, 0.22f, 1f));
        var footerRt = footer.GetComponent<RectTransform>();
        footerRt.anchorMin = new Vector2(0f, 0f);
        footerRt.anchorMax = new Vector2(1f, 0f);
        footerRt.pivot = new Vector2(0.5f, 0f);
        footerRt.sizeDelta = new Vector2(0f, 110f);
        footerRt.anchoredPosition = Vector2.zero;

        var deckLabel = MakeChildText(footer.transform, "DeckCaption", 16, TextAnchor.MiddleLeft, new Vector2(0.03f, 0.58f), new Vector2(0.34f, 0.92f));
        deckLabel.text = LocalizeUtility.GetLocalizedString(EngMessage: "Your deck", JpnMessage: "使用デッキ");
        deckLabel.color = new Color(1f, 1f, 1f, 0.65f);

        _deckText = MakeChildText(footer.transform, "DeckName", 22, TextAnchor.MiddleLeft, new Vector2(0.03f, 0.18f), new Vector2(0.34f, 0.62f));
        _deckText.fontStyle = FontStyle.Bold;

        _noDeckObject = MakeChildText(footer.transform, "NoDeck", 16, TextAnchor.MiddleLeft, new Vector2(0.03f, 0.02f), new Vector2(0.34f, 0.22f)).gameObject;
        _noDeckObject.GetComponent<Text>().text = LocalizeUtility.GetLocalizedString(EngMessage: "Select a valid deck", JpnMessage: "有効なデッキを選んでください");
        _noDeckObject.GetComponent<Text>().color = new Color(1f, 0.55f, 0.45f);
        _noDeckObject.SetActive(false);

        _invalidDeckObject = MakeChildText(footer.transform, "InvalidDeck", 16, TextAnchor.MiddleLeft, new Vector2(0.03f, 0.02f), new Vector2(0.34f, 0.22f)).gameObject;
        _invalidDeckObject.GetComponent<Text>().text = LocalizeUtility.GetLocalizedString(EngMessage: "This deck is invalid", JpnMessage: "このデッキは無効です");
        _invalidDeckObject.GetComponent<Text>().color = new Color(1f, 0.45f, 0.45f);
        _invalidDeckObject.SetActive(false);

        _selectDeckButton = MakeButton(footer.transform, "SelectDeck", font,
            LocalizeUtility.GetLocalizedString(EngMessage: "Change Deck", JpnMessage: "デッキ変更"),
            new Vector2(0.36f, 0.55f), new Vector2(0.54f, 0.92f), Vector2.zero, Vector2.zero,
            OnClickSelectDeck, new Color(0.20f, 0.42f, 0.78f, 1f));

        _sitOutButton = MakeButton(footer.transform, "SitOut", font,
            LocalizeUtility.GetLocalizedString(EngMessage: "Sit out (admin)", JpnMessage: "不参加（管理者）"),
            new Vector2(0.36f, 0.08f), new Vector2(0.54f, 0.48f), Vector2.zero, Vector2.zero,
            OnClickSitOut, new Color(0.35f, 0.38f, 0.42f, 1f));
        _sitOutButtonText = _sitOutButton.GetComponentInChildren<Text>();
        _sitOutButtonImage = _sitOutButton.GetComponent<Image>();
        _sitOutButton.gameObject.SetActive(false);

        _readyButton = MakeButton(footer.transform, "Ready", font,
            LocalizeUtility.GetLocalizedString(EngMessage: "Ready", JpnMessage: "準備完了"),
            new Vector2(0.56f, 0.18f), new Vector2(0.76f, 0.82f), Vector2.zero, Vector2.zero,
            OnClickReady, new Color(0.16f, 0.58f, 0.30f, 1f));
        _readyButtonText = _readyButton.GetComponentInChildren<Text>();
        _readyButtonImage = _readyButton.GetComponent<Image>();

        _startButton = MakeButton(footer.transform, "Start", font,
            LocalizeUtility.GetLocalizedString(EngMessage: "Start Tournament", JpnMessage: "トーナメント開始"),
            new Vector2(0.78f, 0.18f), new Vector2(0.98f, 0.82f), Vector2.zero, Vector2.zero,
            OnClickStartTournament, new Color(0.78f, 0.58f, 0.12f, 1f));
        _startButtonText = _startButton.GetComponentInChildren<Text>();
        _startButtonImage = _startButton.GetComponent<Image>();

        Parent.SetActive(false);
    }

    static GameObject CreatePanel(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = color;
        return go;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    Text MakeChildText(Transform parent, string name, int size, TextAnchor align, Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var text = go.AddComponent<Text>();
        text.font = ResolveFont();
        text.fontSize = size;
        text.alignment = align;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    static Button MakeButton(Transform parent, string name, Font font, string label,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size, UnityAction onClick, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var image = go.GetComponent<Image>();
        image.color = color;
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);

        var textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        Stretch(textGo.GetComponent<RectTransform>());
        var text = textGo.AddComponent<Text>();
        text.font = font;
        text.fontSize = 20;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.text = label;
        text.raycastTarget = false;
        return button;
    }
}
