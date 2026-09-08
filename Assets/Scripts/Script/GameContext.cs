using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using System.Linq;

//Class to manage the overall game situation
[System.Serializable]
public class GameContext
{
    #region constructor
    public GameContext(Player _You, Player _Opponent)
    {
        You = _You;
        Opponent = _Opponent;

        if (PhotonNetwork.IsConnected)
        {
            SetPlayerID();
        }

        Memory = 0;
    }
    #endregion

    #region メモリー
    public int Memory { get; set; } = 0;
    #endregion

    #region List of cards in scene
    public List<CardSource> ActiveCardList
    {
        get; set;
    } = new List<CardSource>();
    #endregion

    #region Player
    public Player You;
    public Player Opponent;

    public List<Player> Players
    {
        get
        {
            List<Player> players = new List<Player>();

            players.Add(PlayerFromID(0));
            players.Add(PlayerFromID(1));

            return players;
        }
    }

    public List<Player> Players_ForTurnPlayer
    {
        get
        {
            List<Player> players = new List<Player>();

            players.Add(TurnPlayer);
            players.Add(NonTurnPlayer);

            return players;
        }
    }

    public List<Player> Players_ForNonTurnPlayer
    {
        get
        {
            List<Player> players = new List<Player>();

            players.Add(NonTurnPlayer);
            players.Add(TurnPlayer);

            return players;
        }
    }

    public Player TurnPlayer;

    public Player NonTurnPlayer
    {
        get
        {
            Player _player = null;

            foreach (Player player in Players)
            {
                if (player != TurnPlayer)
                {
                    _player = player;
                    break;
                }
            }

            return _player;
        }
    }

    public Player FirstPlayer;

    #endregion

    #region Permanents
    public List<Permanent> PermanentsForTurnPlayer
    {
        get
        {
            return Players_ForTurnPlayer.Map(player => player.GetFieldPermanents()).Flat();
        }
    }
    #endregion

    #region Phase of the turn
    public enum phase
    {
        Active,
        Draw,
        Breeding,
        Main,
        End,
        None,
    }

    public phase TurnPhase;
    #endregion

    #region Player ID Assignment
    public void SetPlayerID()
    {
        var cc = ContinuousController.instance;
        if (cc != null && cc.isTournamentSpectator && cc.TournamentState != null)
        {
            AssignSpectatorPlayerIds(cc);
            return;
        }

        if (PhotonNetwork.IsMasterClient)
        {
            You.PlayerID = 0;
            Opponent.PlayerID = 1;
        }

        else
        {
            You.PlayerID = 1;
            Opponent.PlayerID = 0;
        }
    }

    void AssignSpectatorPlayerIds(ContinuousController cc)
    {
        // Seat 0 = match-room master competitor, seat 1 = the other competitor.
        // POV defaults to userIdA; map that bracket side onto the Photon seats.
        var state = cc.TournamentState;
        string viewerId = cc.TournamentSpectateViewerUserId;
        var match = FindSpectateMatch(state, viewerId);
        if (match == null)
        {
            You.PlayerID = 0;
            Opponent.PlayerID = 1;
            return;
        }

        if (string.IsNullOrEmpty(viewerId))
        {
            viewerId = match.userIdA;
            cc.TournamentSpectateViewerUserId = viewerId;
        }

        int viewerSeat = ResolveCompetitorSeat(viewerId);
        if (viewerSeat < 0)
        {
            // Fallback: A → seat of master if we cannot resolve yet.
            viewerSeat = 0;
        }

        You.PlayerID = viewerSeat;
        Opponent.PlayerID = 1 - viewerSeat;
    }

    static TournamentMatchSlot FindSpectateMatch(TournamentState state, string viewerId)
    {
        if (state == null)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(viewerId))
        {
            var byViewer = state.FindActiveMatchFor(viewerId);
            if (byViewer != null)
            {
                return byViewer;
            }
        }

        var live = state.ListSpectatableMatches();
        return live.Count > 0 ? live[0] : null;
    }

    static int ResolveCompetitorSeat(string userId)
    {
        if (string.IsNullOrEmpty(userId) || !PhotonNetwork.InRoom)
        {
            return -1;
        }

        var master = TournamentKeys.FindCompetitorMaster();
        if (master != null && TournamentState.ReadPlayerId(master) == userId)
        {
            return 0;
        }

        foreach (var player in PhotonNetwork.PlayerList)
        {
            if (!TournamentKeys.IsCompetitor(player))
            {
                continue;
            }

            if (master != null && player.ActorNumber == master.ActorNumber)
            {
                continue;
            }

            if (TournamentState.ReadPlayerId(player) == userId)
            {
                return 1;
            }
        }

        return -1;
    }
    #endregion

    #region Returns the player corresponding to the player ID
    public Player PlayerFromID(int playerID)
    {
        if (You.PlayerID == playerID)
        {
            return You;
        }

        else if (Opponent.PlayerID == playerID)
        {
            return Opponent;
        }

        return null;
    }
    #endregion

    public bool DoSwitchTurnPlayer { get; set; } = true;

    #region turn-player switching
    public void SwitchTurnPlayer()
    {
        if (DoSwitchTurnPlayer)
        {
            foreach (Player player in Players)
            {
                if (player != TurnPlayer)
                {
                    TurnPlayer = player;
                    break;
                }
            }
        }

        DoSwitchTurnPlayer = true;
    }
    #endregion

    public bool IsSecurityLooking { get; set; } = false;
}

