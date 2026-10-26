#if DCGO_VR
using TMPro;
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// Wrist console on the left forearm for the Duel Disk view (an original design,
// not a replica of any show's prop).
//   Console (on the forearm): memory, phase, zone counts, END / SKIP button.
//   Blade (sticking out to the side of the arm) with one card zone, PLACE CARD:
//     lay any card from your hand on it. If it can only do one thing it does it;
//     otherwise a menu above the disk lists every option (play, digivolve onto X,
//     use option, activate effect...).
//   HATCH / MOVE button on the blade during your breeding phase.
public class VRDuelDisk : MonoBehaviour
{
    public static readonly Color ZoneColor = new Color(0.05f, 0.16f, 0.22f);

    public Transform PlayZone { get; private set; }
    public Renderer PlayMarker { get; private set; }

    /// <summary>Slot on the inner (right) side of the disk where hand cards are loaded to trash them.</summary>
    public Transform TrashSlot { get; private set; }
    public Renderer TrashSlotMarker { get; private set; }
    public static readonly Color TrashSlotColor = new Color(0.12f, 0.06f, 0.05f);

    Board3D _board;
    VRButton3D _breedButton;
    VRLabel _memory;
    VRLabel _phase;
    VRLabel _counts;
    VRLabel _info;               // what the game is waiting for / doing right now (+ the last instruction)
    string _lastMessage = "";
    float _lastMessageUntil;
    float _nextInfo;
    VRButton3D _endButton;
    int _lastMemory = int.MinValue;
    int _lastCountsKey = -1;
    string _lastCounts;

    public static VRDuelDisk Create(Transform leftHand, Board3D board)
    {
        GameObject root = new GameObject("DuelDisk") { layer = VRRig.Layer };
        root.transform.SetParent(leftHand, false);
        // The controller pose is at the front of the controller; the forearm is behind and below it.
        root.transform.localPosition = new Vector3(0f, -0.035f, -0.14f);

        VRDuelDisk disk = root.AddComponent<VRDuelDisk>();
        disk._board = board;
        disk.Build();
        return disk;
    }

    void Build()
    {
        Piece("Body", new Vector3(0f, 0f, 0f), new Vector3(0.08f, 0.02f, 0.17f), new Color(0.16f, 0.18f, 0.22f));
        Piece("Screen", new Vector3(0f, 0.0105f, -0.005f), new Vector3(0.064f, 0.002f, 0.13f), new Color(0.02f, 0.07f, 0.11f));
        Piece("GlowL", new Vector3(-0.041f, 0.004f, 0f), new Vector3(0.003f, 0.006f, 0.15f), new Color(0.25f, 0.85f, 1f));
        Piece("GlowR", new Vector3(0.041f, 0.004f, 0f), new Vector3(0.003f, 0.006f, 0.15f), new Color(0.25f, 0.85f, 1f));
        Piece("GlowFront", new Vector3(0f, 0.004f, 0.086f), new Vector3(0.07f, 0.006f, 0.003f), new Color(0.3f, 1f, 0.55f));

        _memory = Text(new Vector3(0f, 0.0125f, 0.04f), new Vector2(0.062f, 0.02f), new Color(1f, 0.85f, 0.25f));
        _phase = Text(new Vector3(0f, 0.0125f, 0.012f), new Vector2(0.062f, 0.026f), new Color(0.8f, 0.95f, 1f));
        _phase.enableWordWrapping = true;
        _counts = Text(new Vector3(0f, 0.0125f, -0.022f), new Vector2(0.062f, 0.016f), new Color(0.6f, 0.85f, 1f));
        // Info screen: stands up at the far end of the disk (next to the security hologram) and turns
        // to face you, much bigger than the console text. Hidden when there is nothing to say.
        _info = VRLabel.Create(transform, InfoAnchor, Quaternion.identity, new Vector2(0.15f, 0.05f), "", new Color(1f, 0.88f, 0.5f), new Color(0.03f, 0.06f, 0.11f));
        _info.enableWordWrapping = true;
        _info.alignment = TextAlignmentOptions.Center;
        _info.gameObject.SetActive(false);

        _endButton = VRButton3D.Create(transform, new Vector3(0f, 0.01f, -0.072f), "END", () => _board.PressEndTurn());
        _endButton.transform.localScale = Vector3.one * 0.45f;

        BuildBlade();
        BuildTrashSlot();
    }

    // ---------------------------------------------------------------- trash tray

    // A tray on the inner side of the forearm (toward your body), easy to reach with the right hand.
    // It is retracted inside the disk and slides out when an effect asks you to discard.
    // Load cards into it, then push it closed with your hand to confirm.
    const float TrayClosedX = 0.0f;
    const float TrayOpenX = 0.085f;
    const float TrayHalfWidth = 0.045f;
    const float TrayZ = -0.02f;
    const float PushConfirmT = 0.3f;

    Transform _tray;
    VRLabel _trayLabel;
    Transform _trayEdge;
    float _trayT;            // 0 = retracted, 1 = fully out
    bool _trayWanted;
    bool _handOutside;
    bool _pushing;

    /// <summary>Hand that can push the tray closed (your right hand).</summary>
    public Transform PushHand { get; set; }

    /// <summary>False while a card is being carried to the tray (loading is not pushing).</summary>
    public bool PushEnabled { get; set; } = true;

    /// <summary>Forget the hand's approach (after a card was loaded the hand must come from outside again).</summary>
    public void ResetPush()
    {
        _handOutside = false;
        _pushing = false;
    }

    /// <summary>Raised once when the open tray is pushed closed by hand.</summary>
    public event System.Action TrayPushed;

    public bool TrayOpen => _trayT > 0.95f;

    void BuildTrashSlot()
    {
        GameObject tray = new GameObject("TrashTray") { layer = VRRig.Layer };
        tray.transform.SetParent(transform, false);
        _tray = tray.transform;

        PieceIn(_tray, "TrayBase", new Vector3(0f, -0.002f, TrayZ), new Vector3(TrayHalfWidth * 2f, 0.008f, 0.15f), new Color(0.14f, 0.16f, 0.20f));
        PieceIn(_tray, "TrayEdge", new Vector3(TrayHalfWidth, 0.004f, TrayZ), new Vector3(0.006f, 0.012f, 0.15f), new Color(1f, 0.55f, 0.2f));
        _trayEdge = _tray.Find("TrayEdge");

        TrashSlot = new GameObject("TrashSlot") { layer = VRRig.Layer }.transform;
        TrashSlot.SetParent(_tray, false);
        TrashSlot.localPosition = new Vector3(0.003f, 0.0035f, TrayZ);
        TrashSlotMarker = ZoneMarker(TrashSlot, 0.075f, 0.125f);
        SetMarkerColor(TrashSlotMarker, TrashSlotColor);

        _trayLabel = VRLabel.Create(_tray, new Vector3(0.003f, 0.0055f, TrayZ - 0.08f), Quaternion.Euler(90f, 0f, 0f), new Vector2(0.085f, 0.016f), "SELECT - PUSH TO CONFIRM", new Color(1f, 0.7f, 0.45f));

        _tray.localPosition = new Vector3(TrayClosedX, 0f, 0f);
        tray.SetActive(false);
    }

    void PieceIn(Transform parent, string name, Vector3 position, Vector3 scale, Color color)
    {
        GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
        piece.name = name;
        piece.layer = VRRig.Layer;
        Destroy(piece.GetComponent<Collider>());
        piece.transform.SetParent(parent, false);
        piece.transform.localPosition = position;
        piece.transform.localScale = scale;
        piece.GetComponent<Renderer>().sharedMaterial = VRMaterials.Unlit(color);
    }

    /// <summary>Tray caption, e.g. "TRASH" or "DECK TOP"; trash is orange, the rest blue.</summary>
    public void SetTrayPurpose(string purpose, bool isTrash)
    {
        if (_trayLabel == null)
        {
            return;
        }

        _trayLabel.text = purpose + " - PUSH TO CONFIRM";
        _trayLabel.color = isTrash ? new Color(1f, 0.7f, 0.45f) : new Color(0.55f, 0.85f, 1f);

        if (_trayEdge != null)
        {
            Color edge = isTrash ? new Color(1f, 0.55f, 0.2f) : new Color(0.25f, 0.75f, 1f);
            Renderer renderer = _trayEdge.GetComponent<Renderer>();
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", edge);
            block.SetColor("_Color", edge);
            renderer.SetPropertyBlock(block);
        }
    }

    /// <summary>Open the tray (hand-selection prompt) or retract it.</summary>
    public void SetTrayWanted(bool wanted)
    {
        _trayWanted = wanted;
    }

    void UpdateTray()
    {
        if (_tray == null)
        {
            return;
        }

        float target = _trayWanted ? 1f : 0f;

        // Pushing: the right hand comes from outside, touches the outer edge and pushes toward the arm.
        if (_trayWanted && PushEnabled && PushHand != null && PushHand.localPosition != Vector3.zero)
        {
            Vector3 local = transform.InverseTransformPoint(PushHand.position);
            float edgeX = _tray.localPosition.x + TrayHalfWidth;
            bool alongTray = Mathf.Abs(local.z - TrayZ) < 0.09f && Mathf.Abs(local.y) < 0.05f;

            if (!alongTray)
            {
                // Hand above / away from the tray (e.g. loading a card): never a push.
                _handOutside = false;
                _pushing = false;
            }
            else if (local.x > edgeX + 0.02f)
            {
                // Hand level with the tray, beyond its outer edge: ready to push.
                _handOutside = true;
                _pushing = false;
            }
            else if (_handOutside || _pushing)
            {
                _pushing = true;
                _handOutside = false;
                // The tray follows the hand inward.
                float x = Mathf.Clamp(local.x - TrayHalfWidth, TrayClosedX, TrayOpenX);
                target = Mathf.InverseLerp(TrayClosedX, TrayOpenX, x);
                _trayT = Mathf.Min(_trayT, target);

                if (_trayT < PushConfirmT)
                {
                    _pushing = false;
                    _handOutside = false;
                    VRLaserPointer.Haptic(UnityEngine.XR.XRNode.RightHand, 0.8f, 0.1f);
                    TrayPushed?.Invoke();
                }
            }
        }
        else
        {
            _pushing = false;
        }

        if (!_pushing)
        {
            _trayT = Mathf.MoveTowards(_trayT, target, Time.deltaTime * 4f);
        }

        _tray.localPosition = new Vector3(Mathf.Lerp(TrayClosedX, TrayOpenX, _trayT), 0f, 0f);

        bool visible = _trayT > 0.02f;

        if (_tray.gameObject.activeSelf != visible)
        {
            _tray.gameObject.SetActive(visible);
        }
    }

    static void SetMarkerColor(Renderer renderer, Color color)
    {
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);
        block.SetColor("_BaseColor", color);
        block.SetColor("_Color", color);
        renderer.SetPropertyBlock(block);
    }

    // The blade sticks out from the outside of the left forearm (-X), level with the arm,
    // with one card zone: lay any card from your hand on it.
    void BuildBlade()
    {
        Color glow = new Color(0.25f, 0.85f, 1f);

        Piece("Blade", new Vector3(-0.15f, -0.002f, -0.02f), new Vector3(0.22f, 0.008f, 0.19f), new Color(0.14f, 0.16f, 0.20f));
        Piece("BladeEdge", new Vector3(-0.15f, 0.003f, 0.074f), new Vector3(0.22f, 0.004f, 0.004f), glow);
        Piece("BladeTip", new Vector3(-0.26f, 0.003f, -0.02f), new Vector3(0.004f, 0.004f, 0.19f), glow);

        PlayZone = Zone("PlayZone", new Vector3(-0.13f, 0.0035f, -0.025f));
        PlayMarker = ZoneMarker(PlayZone, 0.10f, 0.135f);
        Label(PlayZone, "PLACE CARD");

        VRButton3D trash = VRButton3D.Create(transform, new Vector3(-0.235f, 0.004f, -0.06f), "TRASH", () => _board.ToggleYourTrash());
        trash.transform.localScale = Vector3.one * 0.4f;

        VRButton3D menu = VRButton3D.Create(transform, new Vector3(-0.235f, 0.004f, -0.008f), "MENU", () =>
        {
            if (VRSession.Instance != null && VRSession.Instance.Settings != null)
            {
                VRSession.Instance.Settings.Toggle();
            }
        });
        menu.transform.localScale = Vector3.one * 0.4f;

        _breedButton = VRButton3D.Create(transform, new Vector3(-0.235f, 0.004f, 0.045f), "HATCH", () => GameActions.Breeding(true));
        _breedButton.transform.localScale = Vector3.one * 0.4f;
        _breedButton.gameObject.SetActive(false);
    }

    Transform Zone(string name, Vector3 position)
    {
        GameObject go = new GameObject(name) { layer = VRRig.Layer };
        go.transform.SetParent(transform, false);
        go.transform.localPosition = position;
        return go.transform;
    }

    Renderer ZoneMarker(Transform zone, float width, float height)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Marker";
        quad.layer = VRRig.Layer;
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(zone, false);
        quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        quad.transform.localScale = new Vector3(width, height, 1f);
        Renderer renderer = quad.GetComponent<Renderer>();
        renderer.sharedMaterial = VRMaterials.Unlit(Color.white);
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", ZoneColor);
        block.SetColor("_Color", ZoneColor);
        renderer.SetPropertyBlock(block);
        return renderer;
    }

    void Label(Transform zone, string text)
    {
        VRLabel label = Text(zone.localPosition + new Vector3(0f, 0.002f, -0.08f), new Vector2(0.1f, 0.018f), new Color(0.6f, 0.9f, 1f));
        label.text = text;
    }

    void Piece(string name, Vector3 position, Vector3 scale, Color color)
    {
        GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
        piece.name = name;
        piece.layer = VRRig.Layer;
        Destroy(piece.GetComponent<Collider>());
        piece.transform.SetParent(transform, false);
        piece.transform.localPosition = position;
        piece.transform.localScale = scale;
        piece.GetComponent<Renderer>().sharedMaterial = VRMaterials.Unlit(color);
    }

    VRLabel Text(Vector3 position, Vector2 size, Color color)
    {
        return VRLabel.Create(transform, position, Quaternion.Euler(90f, 0f, 0f), size, "", color);
    }

    void Update()
    {
        UpdateTray();
        GameContext context = BoardObserver.Context;

        if (context == null || context.You == null)
        {
            Set(_memory, "");
            Set(_phase, "No duel");
            Set(_counts, "");
            Set(_info, "");
            _info.gameObject.SetActive(false);
            _endButton.Interactable = false;
            return;
        }

        // Same convention as the table gauge: positive = you have memory.
        int memory = context.You.PlayerID == 0 ? context.Memory : -context.Memory;

        if (memory != _lastMemory)
        {
            _lastMemory = memory;
            _memory.text = "MEMORY " + (memory > 0 ? "+" : "") + memory;
            _memory.color = memory >= 0 ? new Color(0.35f, 0.75f, 1f) : new Color(1f, 0.4f, 0.35f);
        }

        Player you = context.You;
        int countsKey = you.SecurityCards.Count * 1000000 + you.LibraryCards.Count * 1000 + you.TrashCards.Count;

        if (countsKey != _lastCountsKey)
        {
            _lastCountsKey = countsKey;
            _counts.text = $"SEC {you.SecurityCards.Count}  DECK {you.LibraryCards.Count}  TRASH {you.TrashCards.Count}";
        }

        Set(_phase, _board.PhaseText);
        UpdateInfo(context);

        LegalActionSet set = LegalActions.Current;
        bool breeding = set.IsYourBreedingPhase && (set.CanHatch || set.CanMoveFromBreeding);

        if (_breedButton.gameObject.activeSelf != breeding)
        {
            _breedButton.gameObject.SetActive(breeding);
        }

        if (breeding)
        {
            _breedButton.SetLabel(set.CanHatch ? "HATCH" : "MOVE");
        }
        _endButton.SetLabel(_board.EndButtonLabel == "Skip" ? "SKIP" : "END");
        _endButton.Interactable = _board.EndButtonEnabled;
    }

    // ---------------------------------------------------------------- status line

    const float LastMessageSeconds = 15f;
    static readonly Vector3 InfoAnchor = new Vector3(0.02f, 0.1f, 0.13f); // disk space: high above the far end, clear of the controller model

    void LateUpdate()
    {
        // Keep the info screen upright and facing the eyes whatever the arm does.
        if (_info == null || !_info.gameObject.activeSelf || _board == null || _board.Head == null)
        {
            return;
        }

        Vector3 position = transform.TransformPoint(InfoAnchor);
        Vector3 away = position - _board.Head.position;
        _info.transform.position = position;

        if (away.sqrMagnitude > 1e-6f)
        {
            _info.transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }
    }
    const int MaxInfoChars = 90;

    // Effect prompts / popups can close or be hidden quickly: the console always says what the
    // game is waiting for (its instruction text), which card effect is resolving, or that the
    // opponent is playing, and keeps the last instruction for a while after it disappears.
    void UpdateInfo(GameContext context)
    {
        if (Time.unscaledTime < _nextInfo)
        {
            return;
        }

        _nextInfo = Time.unscaledTime + 0.2f;
        TurnStateMachine tsm = GManager.instance != null ? GManager.instance.turnStateMachine : null;
        string message = Clean(VRPromptBridge.Message);
        string now = "";
        string flash = _board.FlashMessage;

        if (!string.IsNullOrEmpty(flash))
        {
            now = "<b>" + Clean(flash) + "</b>"; // short feedback ("Cancelled", "can't attack"...)
        }
        else if (message.Length > 0)
        {
            now = "<b>WAITING:</b> " + message;
            _lastMessage = message;
            _lastMessageUntil = Time.unscaledTime + LastMessageSeconds;
        }
        else if (tsm != null && tsm.isSecurityCehck)
        {
            now = "<b>SECURITY CHECK</b>";
        }
        else if (tsm != null && tsm.isExecuting)
        {
            string card = ExecutingCard(context);
            now = "<b>RESOLVING</b>" + (card != null ? ": " + card : " effects...");
        }
        else if (context.TurnPlayer != null && !context.TurnPlayer.isYou)
        {
            now = "Opponent is playing...";
        }

        string text = now;

        if (_lastMessage.Length > 0 && Time.unscaledTime < _lastMessageUntil && _lastMessage != message)
        {
            text += (text.Length > 0 ? "\n" : "") + "<color=#8FB8D8><size=80%>Last: " + _lastMessage + "</size></color>";
        }

        bool show = text.Length > 0;

        if (_info.gameObject.activeSelf != show)
        {
            _info.gameObject.SetActive(show);
        }

        Set(_info, text);
    }

    static string ExecutingCard(GameContext context)
    {
        try
        {
            foreach (Player player in new[] { context.You, context.Opponent })
            {
                if (player == null || player.ExecutingCards == null)
                {
                    continue;
                }

                for (int i = player.ExecutingCards.Count - 1; i >= 0; i--)
                {
                    CardSource card = player.ExecutingCards[i];

                    if (card != null)
                    {
                        return card.BaseENGCardNameFromEntity + (player == context.Opponent ? " (opp)" : "");
                    }
                }
            }
        }
        catch (System.Exception)
        {
            // board mid-update
        }

        return null;
    }

    static string Clean(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return "";
        }

        message = message.Replace("\r", " ").Replace("\n", " ").Trim();

        while (message.Contains("  "))
        {
            message = message.Replace("  ", " ");
        }

        return message.Length > MaxInfoChars ? message.Substring(0, MaxInfoChars - 3) + "..." : message;
    }

    static void Set(VRLabel text, string value)
    {
        if (text.text != value)
        {
            text.text = value;
        }
    }
}
#endif
