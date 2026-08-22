using System;
using System.Collections;
using System.Collections.Generic;
using Hashtable = ExitGames.Client.Photon.Hashtable;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// FindFriends presence, challenge room create/join, and invite popup while on home lobby.
/// </summary>
public class FriendDuelService : MonoBehaviourPunCallbacks
{
    public class PresenceInfo
    {
        public bool isOnline;
        public bool isInRoom;
        public string roomName;
    }

    readonly Dictionary<string, PresenceInfo> _presence =
        new Dictionary<string, PresenceInfo>(StringComparer.OrdinalIgnoreCase);

    readonly HashSet<string> _declinedRooms =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    Coroutine _findFriendsLoop;
    Coroutine _inviteTimeout;
    bool _listeningInvites;
    bool _pendingInviteShown;
    string _pendingInviteRoom;
    bool _createFailed;

    public event Action PresenceChanged;
    public event Action InviteHandled;

    public bool IsChallenging { get; private set; }

    public PresenceInfo GetPresence(string playFabId)
    {
        if (string.IsNullOrEmpty(playFabId))
        {
            return null;
        }

        return _presence.TryGetValue(playFabId, out var info) ? info : null;
    }

    public bool CanChallenge(string playFabId)
    {
        var info = GetPresence(playFabId);
        return info != null && info.isOnline && !info.isInRoom;
    }

    public void SetInviteListening(bool enabled)
    {
        _listeningInvites = enabled;
        if (!enabled)
        {
            _pendingInviteShown = false;
            _pendingInviteRoom = null;
        }
    }

    public void StartPresencePolling()
    {
        StopPresencePolling();
        _findFriendsLoop = StartCoroutine(FindFriendsLoop());
    }

    public void StopPresencePolling()
    {
        if (_findFriendsLoop != null)
        {
            StopCoroutine(_findFriendsLoop);
            _findFriendsLoop = null;
        }
    }

    IEnumerator FindFriendsLoop()
    {
        while (true)
        {
            RequestFindFriends();
            yield return new WaitForSecondsRealtime(FriendKeys.FindFriendsPollSeconds);
        }
    }

    public void RequestFindFriends()
    {
        var friends = FriendServices.EnsureExists().List;
        string[] ids = friends.FriendUserIds();
        if (ids == null || ids.Length == 0)
        {
            return;
        }

        if (!PhotonNetwork.IsConnectedAndReady)
        {
            return;
        }

        PhotonNetwork.FindFriends(ids);
    }

    public override void OnFriendListUpdate(List<FriendInfo> friendList)
    {
        if (friendList == null)
        {
            return;
        }

        foreach (var f in friendList)
        {
            if (f == null || string.IsNullOrEmpty(f.UserId))
            {
                continue;
            }

            _presence[f.UserId] = new PresenceInfo
            {
                isOnline = f.IsOnline,
                isInRoom = f.IsInRoom,
                roomName = f.Room,
            };
        }

        PresenceChanged?.Invoke();
    }

    public override void OnRoomListUpdate(List<RoomInfo> roomList)
    {
        if (!_listeningInvites || roomList == null || PhotonNetwork.InRoom)
        {
            return;
        }

        if (ContinuousController.IsBattleSceneLoaded())
        {
            return;
        }

        string localId = FriendListService.LocalPlayFabId();
        if (string.IsNullOrEmpty(localId))
        {
            return;
        }

        for (int i = 0; i < roomList.Count; i++)
        {
            var room = roomList[i];
            if (room == null || !room.IsOpen || room.RemovedFromList || room.PlayerCount <= 0)
            {
                continue;
            }

            if (!room.CustomProperties.TryGetValue(FriendKeys.ModeProperty, out object modeObj) ||
                !(modeObj is string mode) ||
                mode != FriendKeys.ModeFriend)
            {
                continue;
            }

            if (!room.CustomProperties.TryGetValue(FriendKeys.TargetUserIdProperty, out object targetObj) ||
                !(targetObj is string target) ||
                !string.Equals(target, localId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (_declinedRooms.Contains(room.Name) || _pendingInviteShown)
            {
                continue;
            }

            ShowInvitePopup(room);
            break;
        }
    }

    void ShowInvitePopup(RoomInfo room)
    {
        _pendingInviteShown = true;
        _pendingInviteRoom = room.Name;

        string challengerName = "Friend";
        if (room.CustomProperties.TryGetValue(FriendKeys.ChallengerNameProperty, out object n) && n is string ns &&
            !string.IsNullOrEmpty(ns))
        {
            challengerName = ns;
        }

        int winsToTake = 1;
        if (room.CustomProperties.TryGetValue(FriendKeys.WinsToTakeProperty, out object w))
        {
            winsToTake = System.Convert.ToInt32(w);
        }

        string format = winsToTake >= 2 ? "Best of 3" : "Best of 1";
        string info = LocalizeUtility.GetLocalizedString(
            EngMessage: $"{challengerName} challenges you to a {format} duel.",
            JpnMessage: $"{challengerName}から{format}のデュエル挑戦です。");

        var commands = new List<UnityAction>
        {
            () => StartCoroutine(AcceptInviteCoroutine(room.Name, winsToTake)),
            () => DeclineInvite(room.Name),
        };
        var texts = new List<string>
        {
            LocalizeUtility.GetLocalizedString(EngMessage: "Accept", JpnMessage: "受ける"),
            LocalizeUtility.GetLocalizedString(EngMessage: "Decline", JpnMessage: "断る"),
        };

        YesNoObject window = ResolveInviteWindow();
        if (window == null)
        {
            Debug.LogWarning("[Friends] No YesNoObject for invite popup");
            _pendingInviteShown = false;
            return;
        }

        window.CloseOnButtonClicked = true;
        window.SetUpYesNoObject(commands, texts, info, true);
    }

    YesNoObject ResolveInviteWindow()
    {
        if (Opening.instance == null)
        {
            return null;
        }

        // Prefer battle mode select window if available; otherwise any Opening YesNo.
        var battle = Opening.instance.battle;
        if (battle != null && battle.selectBattleMode != null)
        {
            var field = typeof(SelectBattleMode).GetField(
                "selectRoomMatchWindow",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                var yn = field.GetValue(battle.selectBattleMode) as YesNoObject;
                if (yn != null)
                {
                    return yn;
                }
            }
        }

        var yesNos = Opening.instance.GetComponentsInChildren<YesNoObject>(true);
        return yesNos != null && yesNos.Length > 0 ? yesNos[0] : null;
    }

    void DeclineInvite(string roomName)
    {
        if (!string.IsNullOrEmpty(roomName))
        {
            _declinedRooms.Add(roomName);
        }

        _pendingInviteShown = false;
        _pendingInviteRoom = null;
        InviteHandled?.Invoke();
    }

    IEnumerator AcceptInviteCoroutine(string roomName, int winsToTake)
    {
        _pendingInviteShown = false;
        InviteHandled?.Invoke();

        if (string.IsNullOrEmpty(roomName) || !PhotonNetwork.InLobby)
        {
            yield break;
        }

        OnlinePlayerCountService.EnsureExists().SetMatchmakingOwnsConnection(true);

        SetInviteListening(false);
        FriendServices.EnsureExists().Duel.StopPresencePolling();

        if (Opening.instance != null)
        {
            Opening.instance.OffModeButtons();
            Opening.instance.home?.OffHome();
            FriendListPanel.HideIfOpen();
        }

        ContinuousController.instance.isAI = false;
        ContinuousController.instance.isRandomMatch = false;
        ContinuousController.instance.isRanked = false;
        ContinuousController.instance.isTournament = false;
        ContinuousController.instance.isFriendDuel = true;
        ContinuousController.instance.FriendWinsToTake = winsToTake;

        PhotonNetwork.JoinRoom(roomName);
        yield return new WaitUntil(() => PhotonNetwork.InRoom || !PhotonNetwork.IsConnected);

        if (!PhotonNetwork.InRoom)
        {
            ContinuousController.instance.ClearFriendDuel();
            OnlinePlayerCountService.EnsureExists().SetMatchmakingOwnsConnection(false);
            Opening.instance?.home?.SetUpHome();
            yield break;
        }

        RememberChallengerFromRoom();
        FriendServices.EnsureExists().Director.ResetDirector();
        FriendServices.EnsureExists().Director.BeginSeriesFromRoom();

        if (Opening.instance?.battle?.roomManager != null)
        {
            Opening.instance.battle.roomManager.SetUpRoom();
        }
    }

    void RememberChallengerFromRoom()
    {
        if (!PhotonNetwork.InRoom)
        {
            return;
        }

        var hash = PhotonNetwork.CurrentRoom.CustomProperties;
        string id = null;
        string name = null;
        if (hash.TryGetValue(FriendKeys.ChallengerUserIdProperty, out object idObj) && idObj is string s)
        {
            id = s;
        }

        if (hash.TryGetValue(FriendKeys.ChallengerNameProperty, out object nObj) && nObj is string ns)
        {
            name = ns;
        }

        if (!string.IsNullOrEmpty(id))
        {
            var list = FriendServices.EnsureExists().List;
            if (!list.Contains(id))
            {
                FriendServices.EnsureExists().StartCoroutine(list.AddFriendById(id, name, null));
            }
        }
    }

    public void ChallengeFriend(string targetPlayFabId, string targetDisplayName, int winsToTake)
    {
        if (IsChallenging)
        {
            return;
        }

        StartCoroutine(ChallengeFriendCoroutine(targetPlayFabId, targetDisplayName, winsToTake));
    }

    IEnumerator ChallengeFriendCoroutine(string targetPlayFabId, string targetDisplayName, int winsToTake)
    {
        IsChallenging = true;
        _createFailed = false;

        yield return FriendServices.EnsureExists().List.EnsureLoggedIn();

        // Ensure PlayFabId is on local player props for opponent last-opponent stash
        if (RankedServices.Instance != null)
        {
            yield return ContinuousController.instance.StartCoroutine(
                PhotonUtility.SetRankedPlayerProperties());
        }

        OnlinePlayerCountService.EnsureExists().SetMatchmakingOwnsConnection(true);

        if (!PhotonNetwork.IsConnectedAndReady)
        {
            yield return ContinuousController.instance.StartCoroutine(
                PhotonUtility.ConnectToMasterServerCoroutine(matchmakingOwnsConnection: true));
        }

        if (!PhotonNetwork.InLobby)
        {
            PhotonNetwork.JoinLobby();
            yield return new WaitWhile(() => !PhotonNetwork.InLobby && PhotonNetwork.IsConnected);
        }

        if (!PhotonNetwork.InLobby)
        {
            IsChallenging = false;
            OnlinePlayerCountService.EnsureExists().SetMatchmakingOwnsConnection(false);
            yield break;
        }

        string localId = FriendListService.LocalPlayFabId();
        string localName = ContinuousController.instance.PlayerName;

        ContinuousController.instance.isAI = false;
        ContinuousController.instance.isRandomMatch = false;
        ContinuousController.instance.isRanked = false;
        ContinuousController.instance.isTournament = false;
        ContinuousController.instance.isFriendDuel = true;
        ContinuousController.instance.FriendWinsToTake = winsToTake;

        SetInviteListening(false);
        StopPresencePolling();
        FriendListPanel.HideIfOpen();
        Opening.instance?.OffModeButtons();
        Opening.instance?.home?.OffHome();

        string shortId = string.IsNullOrEmpty(localId) ? "x" : localId;
        if (shortId.Length > 8)
        {
            shortId = shortId.Substring(0, 8);
        }

        string roomName = FriendKeys.RoomNamePrefix + shortId + "-" +
                          UnityEngine.Random.Range(1000, 9999);

        var roomOptions = new RoomOptions
        {
            IsVisible = true,
            IsOpen = true,
            PublishUserId = true,
            MaxPlayers = 2,
            CustomRoomProperties = new Hashtable
            {
                { FriendKeys.ModeProperty, FriendKeys.ModeFriend },
                { FriendKeys.TargetUserIdProperty, targetPlayFabId },
                { FriendKeys.ChallengerUserIdProperty, localId ?? "" },
                { FriendKeys.ChallengerNameProperty, localName ?? "Player" },
                { FriendKeys.WinsToTakeProperty, winsToTake },
                { FriendKeys.UseBanlistProperty, ContinuousController.instance.useBanlist },
                { FriendKeys.SeriesWinsAProperty, 0 },
                { FriendKeys.SeriesWinsBProperty, 0 },
                { FriendKeys.GameIndexProperty, 0 },
                { FriendKeys.UserIdAProperty, localId ?? "" },
                { FriendKeys.UserIdBProperty, targetPlayFabId ?? "" },
                { "RoomCreator", PhotonNetwork.NickName },
            },
            CustomRoomPropertiesForLobby = FriendKeys.LobbyProperties,
        };
        BattleReconnectService.ApplyBattleTtl(roomOptions);

        PhotonNetwork.CreateRoom(roomName, roomOptions, null);

        float waited = 0f;
        while (!PhotonNetwork.InRoom && !_createFailed && waited < 15f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        if (!PhotonNetwork.InRoom)
        {
            CancelChallenge();
            yield break;
        }

        FriendServices.EnsureExists().Director.ResetDirector();
        FriendServices.EnsureExists().Director.BeginSeriesFromRoom();

        if (Opening.instance?.battle?.roomManager != null)
        {
            Opening.instance.battle.roomManager.SetUpRoom();
        }

        if (_inviteTimeout != null)
        {
            StopCoroutine(_inviteTimeout);
        }

        _inviteTimeout = StartCoroutine(InviteTimeoutCoroutine(targetDisplayName));
        IsChallenging = false;
    }

    IEnumerator InviteTimeoutCoroutine(string targetDisplayName)
    {
        float t = 0f;
        while (t < FriendKeys.InviteTimeoutSeconds)
        {
            if (!PhotonNetwork.InRoom)
            {
                yield break;
            }

            if (PhotonNetwork.CurrentRoom != null && PhotonNetwork.CurrentRoom.PlayerCount >= 2)
            {
                yield break;
            }

            t += Time.unscaledDeltaTime;
            yield return null;
        }

        Debug.Log($"[Friends] Invite timed out for {targetDisplayName}");
        CancelChallenge();
        if (Opening.instance?.battle?.roomManager != null)
        {
            Opening.instance.battle.roomManager.Off();
        }

        Opening.instance?.home?.SetUpHome();
        FriendListPanel.ShowFromHome();
    }

    public void CancelChallenge()
    {
        if (_inviteTimeout != null)
        {
            StopCoroutine(_inviteTimeout);
            _inviteTimeout = null;
        }

        IsChallenging = false;
        ContinuousController.instance?.ClearFriendDuel();

        if (PhotonNetwork.InRoom)
        {
            PhotonNetwork.LeaveRoom();
        }

        OnlinePlayerCountService.EnsureExists().SetMatchmakingOwnsConnection(false);
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        _createFailed = true;
        Debug.LogWarning($"[Friends] CreateRoom failed: {returnCode} {message}");
    }

    public override void OnJoinedRoom()
    {
        if (_inviteTimeout != null && PhotonNetwork.CurrentRoom != null &&
            PhotonNetwork.CurrentRoom.PlayerCount >= 2)
        {
            StopCoroutine(_inviteTimeout);
            _inviteTimeout = null;
        }
    }

    public override void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer)
    {
        if (_inviteTimeout != null && PhotonNetwork.CurrentRoom != null &&
            PhotonNetwork.CurrentRoom.PlayerCount >= 2)
        {
            StopCoroutine(_inviteTimeout);
            _inviteTimeout = null;
        }
    }
}
