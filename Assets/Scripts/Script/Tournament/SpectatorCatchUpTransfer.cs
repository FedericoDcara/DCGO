using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

/// <summary>
/// Streams a live match to spectators: an initial MatchRecorder snapshot followed by every
/// input the authoritative competitor records afterwards. Spectators never apply live battle
/// RPCs, so this stream is their only source of inputs.
/// </summary>
public class SpectatorCatchUpTransfer : MonoBehaviour, IOnEventCallback
{
    public const byte EventRequest = 171;
    public const byte EventChunk = 172;
    public const byte EventDone = 173;
    public const byte EventUnavailable = 174;
    public const byte EventAppend = 175;
    public const byte EventStreamEnd = 176;
    public const byte EventStreamStop = 177;

    const int ChunkSize = 10 * 1024;
    const float DefaultTimeoutSeconds = 30f;

    [Serializable]
    class EventArrayWrapper
    {
        public ReplayEventDto[] items;
    }

    [Serializable]
    class WirePayload
    {
        public string metaJson;
        public string eventsJson;
    }

    class Subscriber
    {
        public int actorNumber;
        public int requestId;
        public int sessionId;
        public int sentCount;
    }

    static SpectatorCatchUpTransfer _instance;

    readonly List<Subscriber> _subscribers = new List<Subscriber>();

    string[] _chunks;
    int _activeRequestId;
    int _streamMasterActor;
    int _streamSessionId;
    bool _done;
    bool _unavailable;
    string _unavailableReason;
    ReplayData _result;
    bool _streamEnded;

    /// <summary>True once the source stopped sending (game over on the fighter's side).</summary>
    public static bool StreamEnded => _instance == null || _instance._streamEnded;

    /// <summary>Inputs received so far, including live appends.</summary>
    public static int StreamedEventCount =>
        _instance != null && _instance._result != null && _instance._result.events != null
            ? _instance._result.events.Count
            : 0;

    public static SpectatorCatchUpTransfer EnsureExists()
    {
        if (_instance != null)
        {
            return _instance;
        }

        var services = TournamentServices.EnsureExists();
        _instance = services.GetComponent<SpectatorCatchUpTransfer>();
        if (_instance == null)
        {
            _instance = services.gameObject.AddComponent<SpectatorCatchUpTransfer>();
        }

        return _instance;
    }

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(this);
            return;
        }

        _instance = this;
    }

    void OnEnable()
    {
        PhotonNetwork.AddCallbackTarget(this);
    }

    void OnDisable()
    {
        PhotonNetwork.RemoveCallbackTarget(this);
    }

    void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    /// <summary>
    /// Spectator: subscribe to the authoritative competitor's match stream and wait for the
    /// opening snapshot. Requires IsMessageQueueRunning so RaiseEvents dispatch.
    /// </summary>
    public static IEnumerator RequestCatchUpCoroutine(
        Action<ReplayData> onSuccess,
        Action<string> onUnavailable,
        float timeoutSeconds = DefaultTimeoutSeconds)
    {
        var transfer = EnsureExists();
        yield return transfer.RequestCatchUpInternal(onSuccess, onUnavailable, timeoutSeconds);
    }

    /// <summary>Spectator: tell the source to stop streaming (leaving, or game finished).</summary>
    public static void StopStream()
    {
        if (_instance == null)
        {
            return;
        }

        _instance.StopStreamInternal();
    }

    /// <summary>
    /// Master: send any unsent recorder events, then close the stream.
    /// Must run before MatchRecorder drops the in-progress list (surrender + Finalize).
    /// </summary>
    public static void FlushAndEndAll()
    {
        if (_instance == null)
        {
            return;
        }

        _instance.FlushAndEndAllInternal();
    }

    IEnumerator RequestCatchUpInternal(
        Action<ReplayData> onSuccess,
        Action<string> onUnavailable,
        float timeoutSeconds)
    {
        StopStreamInternal();

        var master = TournamentKeys.FindCompetitorMaster();
        if (master == null)
        {
            onUnavailable?.Invoke("no_master");
            yield break;
        }

        _chunks = null;
        _done = false;
        _unavailable = false;
        _unavailableReason = null;
        _result = null;
        _streamEnded = false;
        _streamSessionId = 0;
        _streamMasterActor = master.ActorNumber;
        _activeRequestId = PhotonNetwork.ServerTimestamp;

        SendToActor(EventRequest, new object[] { _activeRequestId }, master.ActorNumber);
        Debug.Log($"[Tournament] Catch-up request sent id={_activeRequestId} to actor={master.ActorNumber}");

        float waited = 0f;
        while (!_done && !_unavailable && !_streamEnded && waited < timeoutSeconds)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        if (_done && _result != null)
        {
            onSuccess?.Invoke(_result);
            yield break;
        }

        string reason = _unavailable
            ? (_unavailableReason ?? "unavailable")
            : _streamEnded ? "source_left" : "timeout";
        Debug.LogWarning($"[Tournament] Catch-up failed: {reason}");
        StopStreamInternal();
        onUnavailable?.Invoke(reason);
    }

    void StopStreamInternal()
    {
        if (_activeRequestId != 0 && _streamMasterActor > 0 && PhotonNetwork.InRoom)
        {
            SendToActor(EventStreamStop, new object[] { _activeRequestId }, _streamMasterActor);
        }

        _activeRequestId = 0;
        _streamMasterActor = 0;
        _streamSessionId = 0;
        _chunks = null;
        _done = false;
        _unavailable = false;
        _result = null;
        _streamEnded = true;
    }

    void Update()
    {
        WatchStreamSource();
        PushToSubscribers();
    }

    /// <summary>Spectator: if the fighter feeding us is gone, no more inputs are coming.</summary>
    void WatchStreamSource()
    {
        if (_activeRequestId == 0 || _streamEnded)
        {
            return;
        }

        if (!PhotonNetwork.InRoom || !ActorInRoom(_streamMasterActor))
        {
            Debug.LogWarning("[Tournament] Spectator stream source left the room");
            _streamEnded = true;
        }
    }

    void PushToSubscribers()
    {
        if (_subscribers.Count == 0)
        {
            return;
        }

        if (!PhotonNetwork.InRoom || !IsAuthoritativeRecorder())
        {
            EndAllSubscriptions();
            return;
        }

        for (int i = _subscribers.Count - 1; i >= 0; i--)
        {
            var sub = _subscribers[i];
            if (!ActorInRoom(sub.actorNumber))
            {
                _subscribers.RemoveAt(i);
                continue;
            }

            if (!MatchRecorder.IsRecording || MatchRecorder.SessionId != sub.sessionId)
            {
                FlushSubscriber(sub);
                SendToActor(EventStreamEnd, new object[] { sub.requestId, sub.sessionId }, sub.actorNumber);
                _subscribers.RemoveAt(i);
                continue;
            }

            int count = MatchRecorder.RecordedEventCount;
            if (count <= sub.sentCount)
            {
                continue;
            }

            var slice = MatchRecorder.ExportEventRange(sub.sentCount);
            if (slice == null || slice.Count == 0)
            {
                continue;
            }

            SendAppendBatches(sub, slice);
            sub.sentCount += slice.Count;
        }
    }

    void FlushAndEndAllInternal()
    {
        if (!PhotonNetwork.InRoom)
        {
            _subscribers.Clear();
            return;
        }

        for (int i = 0; i < _subscribers.Count; i++)
        {
            var sub = _subscribers[i];
            if (!ActorInRoom(sub.actorNumber))
            {
                continue;
            }

            FlushSubscriber(sub);
            SendToActor(EventStreamEnd, new object[] { sub.requestId, sub.sessionId }, sub.actorNumber);
        }

        _subscribers.Clear();
    }

    void FlushSubscriber(Subscriber sub)
    {
        if (sub == null || !MatchRecorder.IsRecording || MatchRecorder.SessionId != sub.sessionId)
        {
            return;
        }

        if (MatchRecorder.RecordedEventCount <= sub.sentCount)
        {
            return;
        }

        var slice = MatchRecorder.ExportEventRange(sub.sentCount);
        if (slice == null || slice.Count == 0)
        {
            return;
        }

        SendAppendBatches(sub, slice);
        sub.sentCount += slice.Count;
    }

    void EndAllSubscriptions()
    {
        for (int i = 0; i < _subscribers.Count; i++)
        {
            var sub = _subscribers[i];
            if (PhotonNetwork.InRoom && ActorInRoom(sub.actorNumber))
            {
                FlushSubscriber(sub);
                SendToActor(EventStreamEnd, new object[] { sub.requestId, sub.sessionId }, sub.actorNumber);
            }
        }

        _subscribers.Clear();
    }

    void SendAppendBatches(Subscriber sub, List<ReplayEventDto> slice)
    {
        int batchStart = sub.sentCount;
        var batch = new List<ReplayEventDto>();
        int batchChars = 0;

        for (int i = 0; i < slice.Count; i++)
        {
            int size = JsonUtility.ToJson(slice[i]).Length;
            if (batch.Count > 0 && batchChars + size > ChunkSize)
            {
                SendAppend(sub, batchStart, batch);
                batchStart += batch.Count;
                batch.Clear();
                batchChars = 0;
            }

            batch.Add(slice[i]);
            batchChars += size;
        }

        if (batch.Count > 0)
        {
            SendAppend(sub, batchStart, batch);
        }
    }

    void SendAppend(Subscriber sub, int startIndex, List<ReplayEventDto> batch)
    {
        string json = SerializeEvents(batch);
        SendToActor(
            EventAppend,
            new object[] { sub.requestId, sub.sessionId, startIndex, json },
            sub.actorNumber);
    }

    public void OnEvent(EventData photonEvent)
    {
        if (photonEvent == null)
        {
            return;
        }

        switch (photonEvent.Code)
        {
            case EventRequest:
                HandleRequest(photonEvent);
                break;
            case EventStreamStop:
                HandleStreamStop(photonEvent);
                break;
            case EventChunk:
                HandleChunk(photonEvent);
                break;
            case EventDone:
                HandleDone(photonEvent);
                break;
            case EventAppend:
                HandleAppend(photonEvent);
                break;
            case EventStreamEnd:
                HandleStreamEnd(photonEvent);
                break;
            case EventUnavailable:
                HandleUnavailable(photonEvent);
                break;
        }
    }

    void HandleRequest(EventData photonEvent)
    {
        if (!IsAuthoritativeRecorder())
        {
            return;
        }

        int sender = photonEvent.Sender;
        if (sender <= 0)
        {
            return;
        }

        int requestId = ReadRequestId(photonEvent.CustomData);
        _subscribers.RemoveAll(s => s.actorNumber == sender);

        int sessionId = MatchRecorder.SessionId;
        int eventCount = MatchRecorder.RecordedEventCount;
        var clone = MatchRecorder.ExportPartialClone();
        if (clone == null)
        {
            Debug.Log("[Tournament] Catch-up unavailable — export not ready");
            SendToActor(EventUnavailable, new object[] { requestId, "no_snapshot" }, sender);
            return;
        }

        string json;
        try
        {
            json = SerializeWire(clone);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Tournament] Catch-up serialize failed: {ex.Message}");
            SendToActor(EventUnavailable, new object[] { requestId, "serialize_failed" }, sender);
            return;
        }

        if (string.IsNullOrEmpty(json))
        {
            SendToActor(EventUnavailable, new object[] { requestId, "empty" }, sender);
            return;
        }

        int total = Math.Max(1, (json.Length + ChunkSize - 1) / ChunkSize);
        Debug.Log($"[Tournament] Catch-up sending {json.Length} chars in {total} chunks to actor {sender}");

        for (int i = 0; i < total; i++)
        {
            int start = i * ChunkSize;
            int len = Math.Min(ChunkSize, json.Length - start);
            SendToActor(
                EventChunk,
                new object[] { requestId, i, total, json.Substring(start, len) },
                sender);
        }

        SendToActor(EventDone, new object[] { requestId, total, sessionId }, sender);

        _subscribers.Add(new Subscriber
        {
            actorNumber = sender,
            requestId = requestId,
            sessionId = sessionId,
            sentCount = clone.events != null ? clone.events.Count : eventCount,
        });
    }

    void HandleStreamStop(EventData photonEvent)
    {
        int sender = photonEvent.Sender;
        if (sender > 0)
        {
            _subscribers.RemoveAll(s => s.actorNumber == sender);
        }
    }

    void HandleChunk(EventData photonEvent)
    {
        if (!(photonEvent.CustomData is object[] arr) || arr.Length < 4)
        {
            return;
        }

        int requestId;
        int index;
        int total;
        string fragment;
        try
        {
            requestId = Convert.ToInt32(arr[0]);
            index = Convert.ToInt32(arr[1]);
            total = Convert.ToInt32(arr[2]);
            fragment = arr[3] as string ?? "";
        }
        catch
        {
            return;
        }

        if (!MatchesActiveRequest(requestId) || total <= 0 || index < 0 || index >= total)
        {
            return;
        }

        if (_chunks == null || _chunks.Length != total)
        {
            _chunks = new string[total];
        }

        _chunks[index] = fragment;
    }

    void HandleDone(EventData photonEvent)
    {
        if (!(photonEvent.CustomData is object[] arr) || arr.Length < 2)
        {
            return;
        }

        int requestId;
        int total;
        int sessionId = 0;
        try
        {
            requestId = Convert.ToInt32(arr[0]);
            total = Convert.ToInt32(arr[1]);
            if (arr.Length >= 3)
            {
                sessionId = Convert.ToInt32(arr[2]);
            }
        }
        catch
        {
            return;
        }

        if (!MatchesActiveRequest(requestId))
        {
            return;
        }

        if (_chunks == null || _chunks.Length != total)
        {
            _unavailable = true;
            _unavailableReason = "chunk_mismatch";
            return;
        }

        var sb = new StringBuilder(total * ChunkSize);
        for (int i = 0; i < total; i++)
        {
            if (_chunks[i] == null)
            {
                _unavailable = true;
                _unavailableReason = "missing_chunk";
                _chunks = null;
                return;
            }

            sb.Append(_chunks[i]);
        }

        _chunks = null;

        try
        {
            var data = DeserializeWire(sb.ToString());
            if (data == null || !data.HasInitialLibrarySnapshot())
            {
                _unavailable = true;
                _unavailableReason = "invalid_payload";
                return;
            }

            _result = data;
            _streamSessionId = sessionId;
            _streamEnded = false;
            _done = true;
            Debug.Log($"[Tournament] Catch-up snapshot received events={data.events.Count} session={sessionId}");
        }
        catch (Exception ex)
        {
            _unavailable = true;
            _unavailableReason = ex.Message;
        }
    }

    void HandleAppend(EventData photonEvent)
    {
        if (!(photonEvent.CustomData is object[] arr) || arr.Length < 4)
        {
            return;
        }

        int requestId;
        int sessionId;
        int startIndex;
        string json;
        try
        {
            requestId = Convert.ToInt32(arr[0]);
            sessionId = Convert.ToInt32(arr[1]);
            startIndex = Convert.ToInt32(arr[2]);
            json = arr[3] as string ?? "";
        }
        catch
        {
            return;
        }

        if (!MatchesActiveRequest(requestId) || _result == null || _result.events == null)
        {
            return;
        }

        if (sessionId != _streamSessionId)
        {
            return;
        }

        var events = DeserializeEvents(json);
        if (events == null || events.Count == 0)
        {
            return;
        }

        if (startIndex > _result.events.Count)
        {
            Debug.LogWarning(
                $"[Tournament] Spectator stream gap: expected {_result.events.Count} got {startIndex} — appending anyway");
            startIndex = _result.events.Count;
        }

        // Reliable + ordered, so an overlap can only be a resend of what we already hold.
        int skip = _result.events.Count - startIndex;
        for (int i = skip; i < events.Count; i++)
        {
            _result.events.Add(events[i]);
        }
    }

    void HandleStreamEnd(EventData photonEvent)
    {
        int requestId = ReadRequestId(photonEvent.CustomData);
        if (!MatchesActiveRequest(requestId))
        {
            return;
        }

        _streamEnded = true;
        Debug.Log("[Tournament] Spectator stream ended by source");
    }

    void HandleUnavailable(EventData photonEvent)
    {
        int requestId = ReadRequestId(photonEvent.CustomData);
        if (!MatchesActiveRequest(requestId))
        {
            return;
        }

        if (photonEvent.CustomData is object[] arr && arr.Length >= 2 && arr[1] is string reason)
        {
            _unavailableReason = reason;
        }

        _unavailable = true;
    }

    static string SerializeEvents(List<ReplayEventDto> events)
    {
        var wrapper = new EventArrayWrapper
        {
            items = events != null ? events.ToArray() : Array.Empty<ReplayEventDto>(),
        };
        return JsonUtility.ToJson(wrapper);
    }

    static List<ReplayEventDto> DeserializeEvents(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        var wrapper = JsonUtility.FromJson<EventArrayWrapper>(json);
        if (wrapper?.items == null)
        {
            return null;
        }

        return new List<ReplayEventDto>(wrapper.items);
    }

    static string SerializeWire(ReplayData data)
    {
        var events = data.events;
        data.events = null;
        string metaJson = JsonUtility.ToJson(data);
        data.events = events;

        var wire = new WirePayload
        {
            metaJson = metaJson,
            eventsJson = SerializeEvents(events),
        };
        return JsonUtility.ToJson(wire);
    }

    static ReplayData DeserializeWire(string json)
    {
        var wire = JsonUtility.FromJson<WirePayload>(json);
        if (wire == null || string.IsNullOrEmpty(wire.metaJson))
        {
            return null;
        }

        var data = JsonUtility.FromJson<ReplayData>(wire.metaJson);
        if (data == null)
        {
            return null;
        }

        data.events = DeserializeEvents(wire.eventsJson) ?? new List<ReplayEventDto>();
        return data;
    }

    bool MatchesActiveRequest(int requestId)
    {
        if (_activeRequestId == 0)
        {
            return false;
        }

        return requestId == 0 || requestId == _activeRequestId;
    }

    static bool ActorInRoom(int actorNumber)
    {
        return PhotonNetwork.InRoom &&
               PhotonNetwork.CurrentRoom.Players != null &&
               PhotonNetwork.CurrentRoom.Players.ContainsKey(actorNumber);
    }

    static int ReadRequestId(object customData)
    {
        if (customData is object[] arr && arr.Length > 0)
        {
            try
            {
                return Convert.ToInt32(arr[0]);
            }
            catch
            {
                return 0;
            }
        }

        return 0;
    }

    static void SendToActor(byte code, object content, int actorNumber)
    {
        var options = new RaiseEventOptions
        {
            TargetActors = new[] { actorNumber },
        };
        var sendOptions = new SendOptions { Reliability = true };
        PhotonNetwork.RaiseEvent(code, content, options, sendOptions);
    }

    static bool IsAuthoritativeRecorder()
    {
        if (!PhotonNetwork.InRoom)
        {
            return false;
        }

        if (TournamentKeys.IsSpectator(PhotonNetwork.LocalPlayer))
        {
            return false;
        }

        var master = TournamentKeys.FindCompetitorMaster();
        return master != null && master.IsLocal;
    }
}
