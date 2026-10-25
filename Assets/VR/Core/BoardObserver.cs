using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// Watches the engine's state and raises events when something changes, so a
// 3D board can animate it without hooks inside the rules code.
// It polls once per frame (cheap: a few dozen list reads) and diffs against
// the previous snapshot. It also produces a fingerprint of the whole board
// which is logged at every turn change: compare the logs of a VR client and a
// desktop client in the same match to catch desyncs.
public enum BoardZone
{
    None,
    Deck,
    EggDeck,
    Hand,
    Security,
    Trash,
    Lost,
    Executing,
    Field,
}

public struct CardLocation : IEquatable<CardLocation>
{
    public Player Owner;
    public BoardZone Zone;
    /// <summary>For Field: the permanent's frame id; otherwise -1.</summary>
    public int FrameID;
    /// <summary>Position inside the zone (stack position for Field, 0 = bottom).</summary>
    public int Index;

    public bool Equals(CardLocation other) => Owner == other.Owner && Zone == other.Zone && FrameID == other.FrameID && Index == other.Index;
    public override bool Equals(object obj) => obj is CardLocation other && Equals(other);
    public override int GetHashCode() => ((int)Zone * 397) ^ FrameID ^ (Index << 8);
    public override string ToString() => Zone == BoardZone.Field ? $"{Zone}[{FrameID}]#{Index}" : $"{Zone}#{Index}";
}

public class BoardObserver : MonoBehaviour
{
    public static BoardObserver Instance { get; private set; }

    /// <summary>A card changed zone (or moved to a different field slot). Index-only changes inside a zone are not reported.</summary>
    public event Action<CardSource, CardLocation, CardLocation> CardMoved;
    public event Action<Permanent, bool> SuspendedChanged;
    public event Action<int, int> MemoryChanged;
    public event Action<GameContext.phase, Player> PhaseChanged;

    readonly Dictionary<CardSource, CardLocation> _previous = new Dictionary<CardSource, CardLocation>();
    readonly Dictionary<CardSource, CardLocation> _current = new Dictionary<CardSource, CardLocation>();
    readonly Dictionary<Permanent, bool> _suspended = new Dictionary<Permanent, bool>();
    int _memory = int.MinValue;
    GameContext.phase _phase = GameContext.phase.None;
    Player _turnPlayer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null)
        {
            return;
        }

        GameObject go = new GameObject("DCGO_BoardObserver");
        DontDestroyOnLoad(go);
        go.AddComponent<BoardObserver>();
    }

    void Awake()
    {
        Instance = this;
    }

    public static GameContext Context =>
        GManager.instance != null && GManager.instance.turnStateMachine != null ? GManager.instance.turnStateMachine.gameContext : null;

    public bool TryGetLocation(CardSource card, out CardLocation location) => _current.TryGetValue(card, out location);

    void LateUpdate()
    {
        GameContext context = Context;

        if (context == null || context.Players == null)
        {
            if (_previous.Count > 0)
            {
                _previous.Clear();
                _current.Clear();
                _suspended.Clear();
                _memory = int.MinValue;
                _phase = GameContext.phase.None;
                _turnPlayer = null;
            }

            return;
        }

        Snapshot(context, _current);

        foreach (KeyValuePair<CardSource, CardLocation> pair in _current)
        {
            if (!_previous.TryGetValue(pair.Key, out CardLocation before))
            {
                LegalActions.Invalidate();
                CardMoved?.Invoke(pair.Key, default, pair.Value);
            }
            else if (before.Owner != pair.Value.Owner || before.Zone != pair.Value.Zone || before.FrameID != pair.Value.FrameID || before.Index != pair.Value.Index)
            {
                LegalActions.Invalidate();

                if (before.Owner != pair.Value.Owner || before.Zone != pair.Value.Zone || before.FrameID != pair.Value.FrameID)
                {
                    CardMoved?.Invoke(pair.Key, before, pair.Value);
                }
            }
        }

        _previous.Clear();

        foreach (KeyValuePair<CardSource, CardLocation> pair in _current)
        {
            _previous[pair.Key] = pair.Value;
        }

        foreach (Player player in context.Players)
        {
            foreach (Permanent permanent in player.GetFieldPermanents())
            {
                bool suspended = permanent.IsSuspended;

                if (!_suspended.TryGetValue(permanent, out bool was))
                {
                    _suspended[permanent] = suspended; // first time seen: no event
                }
                else if (was != suspended)
                {
                    LegalActions.Invalidate();
                    _suspended[permanent] = suspended;
                    SuspendedChanged?.Invoke(permanent, suspended);
                }
            }
        }

        if (context.Memory != _memory)
        {
            int old = _memory;
            _memory = context.Memory;

            if (old != int.MinValue)
            {
                MemoryChanged?.Invoke(old, _memory);
            }
        }

        if (context.TurnPhase != _phase || context.TurnPlayer != _turnPlayer)
        {
            bool turnChanged = context.TurnPlayer != _turnPlayer && _turnPlayer != null;
            _phase = context.TurnPhase;
            _turnPlayer = context.TurnPlayer;
            LegalActions.Invalidate();
            PhaseChanged?.Invoke(_phase, _turnPlayer);

            if (turnChanged)
            {
                Debug.Log("[DCGO VR] Board fingerprint at turn change: " + Fingerprint(context));
            }
        }
    }

    static void Snapshot(GameContext context, Dictionary<CardSource, CardLocation> into)
    {
        into.Clear();

        foreach (Player player in context.Players)
        {
            AddZone(into, player, BoardZone.Deck, player.LibraryCards);
            AddZone(into, player, BoardZone.EggDeck, player.DigitamaLibraryCards);
            AddZone(into, player, BoardZone.Hand, player.HandCards);
            AddZone(into, player, BoardZone.Security, player.SecurityCards);
            AddZone(into, player, BoardZone.Trash, player.TrashCards);
            AddZone(into, player, BoardZone.Lost, player.LostCards);
            AddZone(into, player, BoardZone.Executing, player.ExecutingCards);

            foreach (Permanent permanent in player.GetFieldPermanents())
            {
                int frameId = permanent.PermanentFrame != null ? permanent.PermanentFrame.FrameID : -1;

                for (int i = 0; i < permanent.cardSources.Count; i++)
                {
                    CardSource card = permanent.cardSources[i];

                    if (card != null)
                    {
                        into[card] = new CardLocation { Owner = player, Zone = BoardZone.Field, FrameID = frameId, Index = i };
                    }
                }
            }
        }
    }

    static void AddZone(Dictionary<CardSource, CardLocation> into, Player owner, BoardZone zone, List<CardSource> cards)
    {
        if (cards == null)
        {
            return;
        }

        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] != null)
            {
                into[cards[i]] = new CardLocation { Owner = owner, Zone = zone, FrameID = -1, Index = i };
            }
        }
    }

    /// <summary>
    /// Order-sensitive hash of every zone for both players (card indices, field stacks,
    /// suspended state, memory, phase). Identical on every client while in sync.
    /// Players are ordered by PlayerID so both clients hash in the same order.
    /// </summary>
    public static string Fingerprint(GameContext context)
    {
        if (context == null || context.Players == null)
        {
            return "-";
        }

        StringBuilder sb = new StringBuilder();
        List<Player> players = new List<Player>(context.Players);
        players.Sort((a, b) => a.PlayerID.CompareTo(b.PlayerID));

        foreach (Player p in players)
        {
            sb.Append('P').Append(p.PlayerID);
            Append(sb, 'D', p.LibraryCards);
            Append(sb, 'E', p.DigitamaLibraryCards);
            Append(sb, 'H', p.HandCards);
            Append(sb, 'S', p.SecurityCards);
            Append(sb, 'T', p.TrashCards);
            Append(sb, 'L', p.LostCards);

            foreach (Permanent permanent in p.GetFieldPermanents())
            {
                sb.Append('F').Append(permanent.PermanentFrame != null ? permanent.PermanentFrame.FrameID : -1).Append(permanent.IsSuspended ? 's' : 'u');
                Append(sb, ':', permanent.cardSources);
            }
        }

        sb.Append('M').Append(context.Memory).Append('T').Append(context.TurnPlayer != null ? context.TurnPlayer.PlayerID : -1);

        // FNV-1a, stable across platforms (string.GetHashCode is not).
        uint hash = 2166136261;

        foreach (char c in sb.ToString())
        {
            hash = (hash ^ c) * 16777619;
        }

        return hash.ToString("X8") + $" (cards D/H/S/T: {Count(players, x => x.LibraryCards)}/{Count(players, x => x.HandCards)}/{Count(players, x => x.SecurityCards)}/{Count(players, x => x.TrashCards)}, mem {context.Memory})";
    }

    static void Append(StringBuilder sb, char tag, List<CardSource> cards)
    {
        sb.Append(tag);

        if (cards == null)
        {
            return;
        }

        foreach (CardSource card in cards)
        {
            sb.Append(card != null ? card.CardIndex : -1).Append(',');
        }
    }

    static string Count(List<Player> players, Func<Player, List<CardSource>> zone)
    {
        StringBuilder sb = new StringBuilder();

        for (int i = 0; i < players.Count; i++)
        {
            if (i > 0)
            {
                sb.Append('-');
            }

            List<CardSource> cards = zone(players[i]);
            sb.Append(cards != null ? cards.Count : 0);
        }

        return sb.ToString();
    }
}
