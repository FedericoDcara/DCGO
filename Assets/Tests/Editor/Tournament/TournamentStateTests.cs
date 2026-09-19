using System;
using System.Collections.Generic;
using NUnit.Framework;

public class TournamentStateTests
{
    [Test]
    public void CreateNew_BuildsEmptyFourPlayerBracket()
    {
        var state = TournamentState.CreateNew("tourney", false, 4);

        Assert.AreEqual(4, state.ResolvedPlayerCount);
        Assert.AreEqual(3, state.matches.Length);
        Assert.IsNotNull(state.GetMatch(0, 0));
        Assert.IsNotNull(state.GetMatch(0, 1));
        Assert.IsNotNull(state.GetMatch(1, 0));
        Assert.IsFalse(state.started);
        Assert.IsFalse(state.finished);
    }

    [Test]
    public void SeedFromLobby_FourPlayers_OpensTwoLiveSemifinals()
    {
        var state = SeededState(4, 4, seed: 11);

        Assert.IsTrue(state.started);
        var live = state.ListSpectatableMatches();
        Assert.AreEqual(2, live.Count);
        Assert.IsTrue(TournamentKeys.IsReadyTwoPlayerMatch(state.GetMatch(0, 0)));
        Assert.IsTrue(TournamentKeys.IsReadyTwoPlayerMatch(state.GetMatch(0, 1)));
        Assert.IsNull(state.GetMatch(1, 0).userIdA);
        Assert.IsNull(state.GetMatch(1, 0).userIdB);
    }

    [Test]
    public void SeedFromLobby_SameSeed_IsDeterministic()
    {
        var a = SeededState(4, 4, seed: 42);
        var b = SeededState(4, 4, seed: 42);

        Assert.AreEqual(a.GetMatch(0, 0).userIdA, b.GetMatch(0, 0).userIdA);
        Assert.AreEqual(a.GetMatch(0, 0).userIdB, b.GetMatch(0, 0).userIdB);
        Assert.AreEqual(a.GetMatch(0, 1).userIdA, b.GetMatch(0, 1).userIdA);
        Assert.AreEqual(a.GetMatch(0, 1).userIdB, b.GetMatch(0, 1).userIdB);
    }

    [Test]
    public void SeedFromLobby_ThrowsWithoutTwoPlayers()
    {
        var state = TournamentState.CreateNew("tourney", false, 4);
        Assert.Throws<InvalidOperationException>(() =>
            state.SeedFromLobby(new List<TournamentPlayerSlot> { Player("p0") }, 1));
    }

    [Test]
    public void OneGameWin_CompletesMatchAndEliminatesLoser()
    {
        var state = SeededState(4, 4, seed: 3);
        var match = state.GetMatch(0, 0);
        string winner = match.userIdA;
        string loser = match.userIdB;

        state.ApplyGameResult(0, 0, winner, seriesComplete: false);

        Assert.IsTrue(match.complete);
        Assert.AreEqual(winner, match.winnerUserId);
        Assert.AreEqual(loser, match.lastGameLoserUserId);
        Assert.AreEqual(1, match.seriesWinsA);
        Assert.AreEqual(0, match.seriesWinsB);
        Assert.AreEqual(1, match.gameIndex);
        Assert.IsTrue(state.GetPlayer(loser).eliminated);
        Assert.IsFalse(state.GetPlayer(winner).eliminated);
        Assert.IsNull(state.FindActiveMatchFor(loser));
        Assert.AreEqual(winner, state.GetMatch(1, 0).userIdA);
    }

    [Test]
    public void WinnerAdvancesToCorrectFinalSeat()
    {
        var state = SeededState(4, 4, seed: 8);
        string winner0 = state.GetMatch(0, 0).userIdA;
        string winner1 = state.GetMatch(0, 1).userIdB;

        state.ApplyGameResult(0, 0, winner0, true);
        state.ApplyGameResult(0, 1, winner1, true);

        var finals = state.GetMatch(1, 0);
        Assert.AreEqual(winner0, finals.userIdA);
        Assert.AreEqual(winner1, finals.userIdB);
        Assert.IsFalse(finals.complete);
        Assert.IsFalse(state.finished);
    }

    [Test]
    public void FinalWin_SetsChampionAndFinishesTournament()
    {
        var state = PlayFourPlayerToFinals(out string left, out string right);
        state.ApplyGameResult(1, 0, left, true);

        Assert.IsTrue(state.finished);
        Assert.AreEqual(left, state.championUserId);
        Assert.IsTrue(state.GetPlayer(right).eliminated);
        Assert.That(state.FormatBracket(), Does.Contain("Champion:"));
    }

    [Test]
    public void ThreePlayersInFourBracket_ByeAdvancesWithoutADuel()
    {
        var state = SeededState(4, 3, seed: 5);

        int completedByes = 0;
        int live = 0;
        for (int i = 0; i < 2; i++)
        {
            var match = state.GetMatch(0, i);
            if (match.complete &&
                (TournamentKeys.IsBye(match.userIdA) || TournamentKeys.IsBye(match.userIdB)))
            {
                completedByes++;
                Assert.IsFalse(TournamentKeys.IsBye(match.winnerUserId));
            }
            else if (TournamentKeys.IsReadyTwoPlayerMatch(match))
            {
                live++;
            }
        }

        Assert.AreEqual(1, completedByes);
        Assert.AreEqual(1, live);
        Assert.AreEqual(1, state.ListSpectatableMatches().Count);

        var finals = state.GetMatch(1, 0);
        Assert.IsFalse(string.IsNullOrEmpty(finals.userIdA) && string.IsNullOrEmpty(finals.userIdB));
    }

    [Test]
    public void FindActiveMatchFor_ReturnsCurrentRoundOnly()
    {
        var state = SeededState(4, 4, seed: 2);
        var first = state.GetMatch(0, 0);
        Assert.AreSame(first, state.FindActiveMatchFor(first.userIdA));

        state.ApplyGameResult(0, 0, first.userIdA, true);
        Assert.IsNull(state.FindActiveMatchFor(first.userIdB));
        Assert.AreSame(state.GetMatch(1, 0), state.FindActiveMatchFor(first.userIdA));
    }

    [Test]
    public void ToRoomJson_StripsLockedDeckCodes()
    {
        var state = SeededState(4, 4, seed: 1);
        state.players[0].lockedDeckCode = "SECRET-DECK";

        var restored = TournamentState.FromJson(state.ToRoomJson());

        Assert.IsNotNull(restored);
        Assert.IsTrue(string.IsNullOrEmpty(restored.players[0].lockedDeckCode));
        Assert.AreEqual(state.tourneyId, restored.tourneyId);
    }

    [Test]
    public void MergeFrom_KeepsHigherSeriesProgress()
    {
        var local = SeededState(4, 4, seed: 9);
        var incoming = local.Clone();
        incoming.GetMatch(0, 0).seriesWinsA = 1;
        incoming.GetMatch(0, 0).complete = true;
        incoming.GetMatch(0, 0).winnerUserId = incoming.GetMatch(0, 0).userIdA;

        local.MergeFrom(incoming);

        Assert.AreEqual(1, local.GetMatch(0, 0).seriesWinsA);
        Assert.IsTrue(local.GetMatch(0, 0).complete);
    }

    [Test]
    public void IsAdmin_MatchesSitOutHost()
    {
        var state = TournamentState.CreateNew("tourney", false, 4);
        state.adminUserId = "host";

        Assert.IsTrue(state.IsAdmin("host"));
        Assert.IsFalse(state.IsAdmin("player"));
    }

    static TournamentState SeededState(int bracketSize, int lobbyCount, int seed)
    {
        var state = TournamentState.CreateNew("tourney", false, bracketSize);
        var lobby = new List<TournamentPlayerSlot>();
        for (int i = 0; i < lobbyCount; i++)
        {
            lobby.Add(Player("p" + i));
        }

        state.SeedFromLobby(lobby, seed);
        return state;
    }

    static TournamentState PlayFourPlayerToFinals(out string left, out string right)
    {
        var state = SeededState(4, 4, seed: 17);
        left = state.GetMatch(0, 0).userIdA;
        right = state.GetMatch(0, 1).userIdA;
        state.ApplyGameResult(0, 0, left, true);
        state.ApplyGameResult(0, 1, right, true);
        return state;
    }

    static TournamentPlayerSlot Player(string userId)
    {
        return new TournamentPlayerSlot
        {
            userId = userId,
            nickName = userId.ToUpperInvariant(),
            lockedDeckCode = "deck-" + userId,
            lockedDeckName = "Deck " + userId,
        };
    }
}
