using System;
using UnityEngine;

/// <summary>
/// Buffers match inputs during a live game and finalizes a ReplayData at EndGame.
/// </summary>
public static class MatchRecorder
{
    static ReplayData _current;
    static bool _recording;
    static bool _finalized;
    static long _pendingSeed;
    static bool _hasSeed;
    /// <summary>Survives BeginMatch so a SetRandom that finishes slightly early is not lost.</summary>
    static long _stashedSeed;
    static bool _hasStashedSeed;

    public static bool IsRecording => _recording && !_finalized && _current != null;

    /// <summary>Changes every game so a spectator stream can tell recordings apart.</summary>
    public static int SessionId { get; private set; }

    /// <summary>Inputs recorded so far in this game (the spectator stream index).</summary>
    public static int RecordedEventCount =>
        IsRecording && _current.events != null ? _current.events.Count : 0;

    public static void BeginMatch()
    {
        if (IsReplaySession())
        {
            _recording = false;
            _current = null;
            _finalized = false;
            return;
        }

        SessionId++;
        _current = new ReplayData
        {
            version = ReplayData.CurrentVersion,
            id = Guid.NewGuid().ToString("N"),
            timestampIso = DateTime.UtcNow.ToString("o"),
            mode = ResolveMode(),
            events = new System.Collections.Generic.List<ReplayEventDto>(),
        };
        _recording = true;
        _finalized = false;
        _hasSeed = false;
        _pendingSeed = 0;

        if (_hasStashedSeed)
        {
            ApplySeedToCurrent(_stashedSeed);
        }

        Debug.Log($"[Replay] Recording started id={_current.id} mode={_current.mode} seedStashed={_hasStashedSeed}");
    }

    public static void SetSeed(long seed)
    {
        _stashedSeed = seed;
        _hasStashedSeed = true;

        if (!IsRecording)
        {
            return;
        }

        ApplySeedToCurrent(seed);
    }

    static void ApplySeedToCurrent(long seed)
    {
        _pendingSeed = seed;
        _hasSeed = true;
        if (_current != null)
        {
            _current.SetRandomSeed(seed);
        }
    }

    public static void SetRolledFirstPlayer(bool rolled)
    {
        if (!IsRecording)
        {
            return;
        }

        _current.rolledFirstPlayer = rolled;
    }

    public static void SetPlayerNames(string player0Name, string player1Name)
    {
        if (!IsRecording)
        {
            return;
        }

        _current.player0Name = player0Name ?? "";
        _current.player1Name = player1Name ?? "";
    }

    public static void SetDeckCodes(string player0DeckCode, string player1DeckCode)
    {
        if (!IsRecording)
        {
            return;
        }

        _current.player0DeckCode = player0DeckCode ?? "";
        _current.player1DeckCode = player1DeckCode ?? "";
    }

    public static void SetInitialLibraries(
        int[] player0Library,
        int[] player0Digitama,
        int[] player1Library,
        int[] player1Digitama)
    {
        if (!IsRecording)
        {
            return;
        }

        _current.player0LibraryEntityIndices = player0Library;
        _current.player0DigitamaEntityIndices = player0Digitama;
        _current.player1LibraryEntityIndices = player1Library;
        _current.player1DigitamaEntityIndices = player1Digitama;
    }

    public static void SetFirstPlayer(int firstPlayerId)
    {
        if (!IsRecording)
        {
            return;
        }

        _current.firstPlayerId = firstPlayerId;
    }

    public static void SetViewerPlayerId(int viewerPlayerId)
    {
        if (!IsRecording)
        {
            return;
        }

        _current.viewerPlayerId = viewerPlayerId;
    }

    public static void RecordTurnMarker(int turnCount, int turnPlayerId)
    {
        if (!IsRecording)
        {
            return;
        }

        _current.events.Add(ReplayEventDto.TurnMarker(turnCount, turnPlayerId));
    }

    public static void RecordMainPhaseAction(int playerId, MainPhaseAction action)
    {
        if (!IsRecording || action == null)
        {
            return;
        }

        try
        {
            byte packetId = GamePacketFactory.GetId(action.GetType());
            byte[] bytes = action.Serialize();
            _current.events.Add(ReplayEventDto.MainPhase(playerId, packetId, bytes));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Replay] Failed to record main phase action: {ex.Message}");
        }
    }

    public static void RecordPlayerSelection(int playerId, IPlayerSelection selection)
    {
        if (!IsRecording || selection == null)
        {
            return;
        }

        var dto = ReplayEventDto.FromSelection(playerId, selection);
        if (dto != null)
        {
            _current.events.Add(dto);
        }
    }

    public static void RecordSurrender(int playerId)
    {
        if (!IsRecording)
        {
            return;
        }

        _current.events.Add(ReplayEventDto.Surrender(playerId));
    }

    public static ReplayData Finalize(int winnerPlayerId, bool surrendered, bool disconnect, int finalTurnCount)
    {
        if (!_recording || _current == null || _finalized)
        {
            return null;
        }

        // Push the last inputs (especially surrender) before recording stops.
        // Otherwise spectators never see the game end and freeze on the old board.
        SpectatorCatchUpTransfer.FlushAndEndAll();

        _finalized = true;
        _recording = false;

        if (_hasSeed)
        {
            _current.SetRandomSeed(_pendingSeed);
        }
        else if (_hasStashedSeed)
        {
            _current.SetRandomSeed(_stashedSeed);
            Debug.LogWarning($"[Replay] Finalize recovered stashed seed {_stashedSeed} (SetSeed missed during recording).");
        }
        else
        {
            Debug.LogError("[Replay] Finalize with no RNG seed — replay will desync/freeze. Discard this recording.");
        }

        _current.winnerPlayerId = winnerPlayerId;
        _current.surrendered = surrendered;
        _current.disconnect = disconnect;
        _current.finalTurnCount = finalTurnCount;

        if (string.IsNullOrEmpty(_current.player0DeckCode) || string.IsNullOrEmpty(_current.player1DeckCode))
        {
            Debug.LogWarning("[Replay] Incomplete deck codes — replay not saved.");
            _current = null;
            return null;
        }

        var data = _current;
        _current = null;
        Debug.Log($"[Replay] Finalized id={data.id} events={data.events.Count} turns={finalTurnCount}");
        return data;
    }

    public static void Cancel()
    {
        _recording = false;
        _finalized = false;
        _current = null;
        _hasSeed = false;
        // Keep _stashedSeed — next BeginMatch / battle may still need it.
    }

    /// <summary>
    /// Deep-copies the in-progress recording for mid-game spectator catch-up.
    /// Does not stop recording. Returns null until seed + post-shuffle libraries exist.
    /// </summary>
    public static ReplayData ExportPartialClone()
    {
        if (!IsRecording || _current == null)
        {
            return null;
        }

        if (!_hasSeed && !_hasStashedSeed)
        {
            return null;
        }

        if (!_current.HasInitialLibrarySnapshot())
        {
            return null;
        }

        if (string.IsNullOrEmpty(_current.player0DeckCode) ||
            string.IsNullOrEmpty(_current.player1DeckCode))
        {
            return null;
        }

        if (_hasSeed)
        {
            _current.SetRandomSeed(_pendingSeed);
        }
        else if (_hasStashedSeed)
        {
            _current.SetRandomSeed(_stashedSeed);
        }

        if (_current.GetRandomSeed() == 0 && string.IsNullOrEmpty(_current.randomSeedText))
        {
            return null;
        }

        try
        {
            string json = JsonUtility.ToJson(_current);
            var clone = JsonUtility.FromJson<ReplayData>(json);
            if (clone == null || !clone.HasInitialLibrarySnapshot())
            {
                return null;
            }

            // JsonUtility can drop List<> on some Unity versions — always rebuild events.
            clone.events = ExportEventRange(0) ?? new System.Collections.Generic.List<ReplayEventDto>();
            return clone;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Replay] ExportPartialClone failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Copies recorded inputs from <paramref name="startIndex"/> onward so the spectator
    /// stream can keep feeding a watcher after the initial snapshot.
    /// </summary>
    public static System.Collections.Generic.List<ReplayEventDto> ExportEventRange(int startIndex)
    {
        if (!IsRecording || _current.events == null || startIndex < 0)
        {
            return null;
        }

        var slice = new System.Collections.Generic.List<ReplayEventDto>();
        for (int i = startIndex; i < _current.events.Count; i++)
        {
            var src = _current.events[i];
            if (src == null)
            {
                continue;
            }

            try
            {
                var evtClone = JsonUtility.FromJson<ReplayEventDto>(JsonUtility.ToJson(src));
                if (evtClone != null)
                {
                    slice.Add(evtClone);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Replay] Failed to clone event {i}: {ex.Message}");
            }
        }

        return slice;
    }

    static bool IsReplaySession()
    {
        return ContinuousController.instance != null &&
               (ContinuousController.instance.isReplay ||
                ContinuousController.instance.isTournamentSpectator ||
                ContinuousController.instance.isSpectatorCatchUp);
    }

    static string ResolveMode()
    {
        var cc = ContinuousController.instance;
        if (cc == null)
        {
            return "unknown";
        }

        if (cc.isReplay)
        {
            return "replay";
        }

        if (cc.isTournament)
        {
            return "tournament";
        }

        if (cc.isFriendDuel)
        {
            return "friend";
        }

        if (cc.isRanked)
        {
            return "ranked";
        }

        if (cc.isAI)
        {
            return "ai";
        }

        if (cc.isRandomMatch)
        {
            return "random";
        }

        return "room";
    }
}
