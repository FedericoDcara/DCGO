/// <summary>
/// A tournament spectator rebuilds the board from the recorder stream for the whole
/// match, so live battle RPCs must never reach the input queues.
/// </summary>
public static class SpectatorInputGate
{
    /// <summary>True while the local client is a stream-driven spectator.</summary>
    public static bool StreamOnly { get; set; }

    /// <summary>True only while the spectator driver is pushing a streamed input.</summary>
    public static bool Injecting { get; set; }

    public static bool BlocksNetworkInput => StreamOnly && !Injecting;

    public static void Clear()
    {
        StreamOnly = false;
        Injecting = false;
    }
}
