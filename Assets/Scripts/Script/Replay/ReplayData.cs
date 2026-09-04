using System;
using System.Collections.Generic;

[Serializable]
public class ReplayData
{
    public const int CurrentVersion = 2;

    public int version = CurrentVersion;
    public string id;
    public string timestampIso;
    public string mode;
    public string player0Name;
    public string player1Name;
    public string player0DeckCode;
    public string player1DeckCode;
    /// <summary>Post-shuffle library order as CEntity CardIndex values (authoritative card identity for CardIndex mapping).</summary>
    public int[] player0LibraryEntityIndices;
    public int[] player0DigitamaEntityIndices;
    public int[] player1LibraryEntityIndices;
    public int[] player1DigitamaEntityIndices;
    /// <summary>Legacy numeric seed — may lose precision via JSON. Prefer <see cref="randomSeedText"/>.</summary>
    public long randomSeed;
    /// <summary>Authoritative seed as decimal string (avoids JSON float precision loss).</summary>
    public string randomSeedText;
    public int firstPlayerId;
    public int viewerPlayerId;
    /// <summary>True if live match consumed GameRandom.Range(0,2) when deciding first player.</summary>
    public bool rolledFirstPlayer;
    public int winnerPlayerId = -1;
    public bool surrendered;
    public bool disconnect;
    public int finalTurnCount;
    public List<ReplayEventDto> events = new List<ReplayEventDto>();

    public bool IsValid()
    {
        return version >= 1 && version <= CurrentVersion
               && !string.IsNullOrEmpty(id)
               && !string.IsNullOrEmpty(player0DeckCode)
               && !string.IsNullOrEmpty(player1DeckCode)
               && events != null;
    }

    public long GetRandomSeed()
    {
        if (!string.IsNullOrEmpty(randomSeedText) &&
            long.TryParse(randomSeedText, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out long parsed))
        {
            return parsed;
        }

        return randomSeed;
    }

    public void SetRandomSeed(long seed)
    {
        randomSeed = seed;
        randomSeedText = seed.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public bool HasInitialLibrarySnapshot()
    {
        return player0LibraryEntityIndices != null && player0LibraryEntityIndices.Length > 0
               && player1LibraryEntityIndices != null && player1LibraryEntityIndices.Length > 0;
    }

    public List<int> GetTurnMarkers()
    {
        var turns = new List<int>();
        if (events == null)
        {
            return turns;
        }

        for (int i = 0; i < events.Count; i++)
        {
            var e = events[i];
            if (e != null && e.EventType == ReplayEventType.TurnMarker)
            {
                turns.Add(e.turnCount);
            }
        }

        return turns;
    }
}

[Serializable]
public class MatchHistoryIndex
{
    public List<MatchHistoryEntry> entries = new List<MatchHistoryEntry>();
}

[Serializable]
public class MatchHistoryEntry
{
    public string id;
    public string timestampIso;
    public string mode;
    public string player0Name;
    public string player1Name;
    public int viewerPlayerId;
    public int winnerPlayerId;
    public bool surrendered;
    public int finalTurnCount;
    public string fileName;

    public static MatchHistoryEntry FromReplay(ReplayData data, string fileName)
    {
        return new MatchHistoryEntry
        {
            id = data.id,
            timestampIso = data.timestampIso,
            mode = data.mode,
            player0Name = data.player0Name,
            player1Name = data.player1Name,
            viewerPlayerId = data.viewerPlayerId,
            winnerPlayerId = data.winnerPlayerId,
            surrendered = data.surrendered,
            finalTurnCount = data.finalTurnCount,
            fileName = fileName,
        };
    }

    public string OpponentDisplayName()
    {
        if (viewerPlayerId == 0)
        {
            return string.IsNullOrEmpty(player1Name) ? "Opponent" : player1Name;
        }

        return string.IsNullOrEmpty(player0Name) ? "Opponent" : player0Name;
    }

    public string ResultLabel()
    {
        if (winnerPlayerId < 0)
        {
            return "Draw";
        }

        if (winnerPlayerId == viewerPlayerId)
        {
            return "Win";
        }

        return "Lose";
    }
}
