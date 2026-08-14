using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BattleMode : MonoBehaviour
{
    [Header("Battle Button")]
    public OpeningButton BattleButton;

    [Header("Battle Mode Selection")]
    public SelectBattleMode selectBattleMode;

    [Header("Battle Deck Selection")]
    public SelectBattleDeck selectBattleDeck;

    [Header("RandomMatch")]
    public LobbyManager_RandomMatch lobbyManager_RandomMatch;

    [Header("RankedMatch")]
    public LobbyManager_RankedMatch lobbyManager_RankedMatch;

    [Header("Room Screen")]
    public RoomManager roomManager;

    [Header("Tournament")]
    public TournamentLobbyManager tournamentLobbyManager;

    bool first = false;

    public void OffBattle()
    {
        roomManager.Off();

        tournamentLobbyManager?.Off();

        selectBattleDeck.Off();

        selectBattleMode.OffSelectBattleMode();

        lobbyManager_RandomMatch.OffLobby();

        if (lobbyManager_RankedMatch != null)
        {
            lobbyManager_RankedMatch.OffLobby();
        }

        if (!first)
        {
            BattleButton.OnExit();
            first = true;
        }
    }

    public void SetUpBattleMode()
    {
        selectBattleMode.SetUpSelectBattleMode();
        Opening.instance.optionPanel.CloseOptionPanel();
    }
}
