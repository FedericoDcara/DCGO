using NUnit.Framework;

public class TournamentKeysTests
{
    [Test]
    public void SingleElimination_RequiresOneWin()
    {
        Assert.AreEqual(1, TournamentKeys.WinsToTakeSeries);
    }

    [TestCase(4)]
    [TestCase(8)]
    [TestCase(16)]
    public void NormalizePlayerCount_KeepsAllowedSizes(int size)
    {
        Assert.AreEqual(size, TournamentKeys.NormalizePlayerCount(size));
        Assert.IsTrue(TournamentKeys.IsAllowedPlayerCount(size));
    }

    [TestCase(0)]
    [TestCase(3)]
    [TestCase(5)]
    [TestCase(7)]
    public void NormalizePlayerCount_FallsBackToDefault(int size)
    {
        Assert.AreEqual(TournamentKeys.DefaultPlayerCount, TournamentKeys.NormalizePlayerCount(size));
    }

    [Test]
    public void FinalRound_MatchesBracketDepth()
    {
        Assert.AreEqual(1, TournamentKeys.FinalRoundFor(4));
        Assert.AreEqual(2, TournamentKeys.FinalRoundFor(8));
        Assert.AreEqual(3, TournamentKeys.FinalRoundFor(16));
    }

    [Test]
    public void MatchesInRound_HalvesEachRound()
    {
        Assert.AreEqual(4, TournamentKeys.MatchesInRoundFor(8, 0));
        Assert.AreEqual(2, TournamentKeys.MatchesInRoundFor(8, 1));
        Assert.AreEqual(1, TournamentKeys.MatchesInRoundFor(8, 2));
        Assert.AreEqual(2, TournamentKeys.MatchesInRoundFor(4, 0));
        Assert.AreEqual(1, TournamentKeys.MatchesInRoundFor(4, 1));
    }

    [Test]
    public void RoundDisplayName_UsesFinalsAndSemifinals()
    {
        Assert.AreEqual("Semifinals", TournamentKeys.RoundDisplayNameFor(4, 0));
        Assert.AreEqual("Finals", TournamentKeys.RoundDisplayNameFor(4, 1));
        Assert.AreEqual("Quarterfinals", TournamentKeys.RoundDisplayNameFor(8, 0));
        Assert.AreEqual("Semifinals", TournamentKeys.RoundDisplayNameFor(8, 1));
        Assert.AreEqual("Finals", TournamentKeys.RoundDisplayNameFor(8, 2));
    }

    [Test]
    public void RoomNames_AreStablePerMatch()
    {
        Assert.AreEqual("09118-T-R0-M1-True", TournamentKeys.MatchRoomName("09118", 0, 1, true));
        Assert.AreEqual("09118-T-W-Hub", TournamentKeys.WaitHubRoomName("09118", true));
        Assert.That(TournamentKeys.LobbyRoomName("09118"), Does.Contain("09118"));
    }

    [Test]
    public void Capacities_LeaveOneSeatForAdminOrSpectator()
    {
        Assert.AreEqual(5, TournamentKeys.RoomCapacityForBracket(4));
        Assert.AreEqual(5, TournamentKeys.MatchRoomMaxPlayers(4));
        Assert.AreEqual(9, TournamentKeys.MatchRoomMaxPlayers(8));
    }

    [Test]
    public void IsReadyTwoPlayerMatch_RequiresTwoLivePlayers()
    {
        Assert.IsFalse(TournamentKeys.IsReadyTwoPlayerMatch(null));
        Assert.IsFalse(TournamentKeys.IsReadyTwoPlayerMatch(new TournamentMatchSlot
        {
            userIdA = "a",
            userIdB = TournamentKeys.ByeUserId,
        }));
        Assert.IsFalse(TournamentKeys.IsReadyTwoPlayerMatch(new TournamentMatchSlot
        {
            userIdA = "a",
            userIdB = "b",
            complete = true,
        }));
        Assert.IsTrue(TournamentKeys.IsReadyTwoPlayerMatch(new TournamentMatchSlot
        {
            userIdA = "a",
            userIdB = "b",
        }));
    }

    [Test]
    public void IsBye_OnlyMatchesPlaceholder()
    {
        Assert.IsTrue(TournamentKeys.IsBye(TournamentKeys.ByeUserId));
        Assert.IsFalse(TournamentKeys.IsBye("player-1"));
        Assert.IsFalse(TournamentKeys.IsBye(null));
    }
}
