using System;

public enum ReplayEventType : byte
{
    TurnMarker = 0,
    MainPhaseAction = 1,
    SelectionValue = 2,
    SelectionCard = 3,
    SelectionPermanent = 4,
    Surrender = 5,
}

[Serializable]
public class ReplayEventDto
{
    public byte type;
    public int playerId;
    public int turnCount;
    public byte packetId;
    public string payloadBase64;
    public int value;
    public int[] intPayload;
    public bool[] boolPayload;

    public ReplayEventType EventType => (ReplayEventType)type;

    public static ReplayEventDto TurnMarker(int turnCount, int turnPlayerId)
    {
        return new ReplayEventDto
        {
            type = (byte)ReplayEventType.TurnMarker,
            turnCount = turnCount,
            playerId = turnPlayerId,
        };
    }

    public static ReplayEventDto MainPhase(int playerId, byte packetId, byte[] bytes)
    {
        return new ReplayEventDto
        {
            type = (byte)ReplayEventType.MainPhaseAction,
            playerId = playerId,
            packetId = packetId,
            payloadBase64 = bytes != null && bytes.Length > 0 ? Convert.ToBase64String(bytes) : "",
        };
    }

    public static ReplayEventDto FromSelection(int playerId, IPlayerSelection selection)
    {
        if (selection is ValueSelection valueSelection)
        {
            return new ReplayEventDto
            {
                type = (byte)ReplayEventType.SelectionValue,
                playerId = playerId,
                value = valueSelection.ValueAsInt(),
            };
        }

        if (selection is CardSelection cardSelection)
        {
            return new ReplayEventDto
            {
                type = (byte)ReplayEventType.SelectionCard,
                playerId = playerId,
                intPayload = cardSelection.CardIDList != null
                    ? (int[])cardSelection.CardIDList.Clone()
                    : null,
            };
        }

        if (selection is PermanentSelection permanentSelection)
        {
            return new ReplayEventDto
            {
                type = (byte)ReplayEventType.SelectionPermanent,
                playerId = playerId,
                intPayload = permanentSelection.PermanentIDList != null
                    ? (int[])permanentSelection.PermanentIDList.Clone()
                    : null,
                boolPayload = permanentSelection.IsTurnPlayerList != null
                    ? (bool[])permanentSelection.IsTurnPlayerList.Clone()
                    : null,
            };
        }

        return null;
    }

    public static ReplayEventDto Surrender(int playerId)
    {
        return new ReplayEventDto
        {
            type = (byte)ReplayEventType.Surrender,
            playerId = playerId,
        };
    }

    public byte[] GetPayloadBytes()
    {
        if (string.IsNullOrEmpty(payloadBase64))
        {
            return Array.Empty<byte>();
        }

        return Convert.FromBase64String(payloadBase64);
    }

    public IPlayerSelection ToSelection()
    {
        switch (EventType)
        {
            case ReplayEventType.SelectionValue:
                return new ValueSelection(value);
            case ReplayEventType.SelectionCard:
                return new CardSelection(intPayload);
            case ReplayEventType.SelectionPermanent:
                return new PermanentSelection(boolPayload, intPayload);
            default:
                return null;
        }
    }
}
