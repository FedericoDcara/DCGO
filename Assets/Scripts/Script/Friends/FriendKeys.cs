/// <summary>
/// Photon property keys and constants for friend list / direct duel.
/// </summary>
public static class FriendKeys
{
    public const string ModeProperty = "Mode";
    public const string ModeFriend = "friend";

    public const string TargetUserIdProperty = "FriendTargetUserId";
    public const string ChallengerUserIdProperty = "FriendChallengerUserId";
    public const string ChallengerNameProperty = "FriendChallengerName";
    public const string WinsToTakeProperty = "FriendWinsToTake";
    public const string UseBanlistProperty = "UseBanlist";

    public const string SeriesWinsAProperty = "FriendSeriesWinsA";
    public const string SeriesWinsBProperty = "FriendSeriesWinsB";
    public const string GameIndexProperty = "FriendGameIndex";
    public const string LastLoserProperty = "FriendLastLoser";
    public const string OnResultProperty = "FriendOnResult";
    public const string UserIdAProperty = "FriendUserIdA";
    public const string UserIdBProperty = "FriendUserIdB";

    public const string RoomNamePrefix = "fd-";

    public const int MaxFriends = 50;
    public const float FindFriendsPollSeconds = 5f;
    public const float InviteTimeoutSeconds = 60f;

    public const string LocalFriendsPrefsKey = "DCGO_FriendList";
    public const string LastOpponentIdPrefsKey = "DCGO_LastOpponentId";
    public const string LastOpponentNamePrefsKey = "DCGO_LastOpponentName";

    /// <summary>Lobby custom properties visible to home-presence clients.</summary>
    public static readonly string[] LobbyProperties =
    {
        ModeProperty,
        TargetUserIdProperty,
        ChallengerUserIdProperty,
        ChallengerNameProperty,
        WinsToTakeProperty,
        UseBanlistProperty,
        "RoomCreator",
    };
}
