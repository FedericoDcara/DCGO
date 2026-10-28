#if DCGO_VR
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// The 3D tabletop. Every frame it reads the engine's zones (deck, egg deck,
// hand, security, trash, lost, executing, field stacks, memory) and tells
// each Card3D where to be. It never changes game state itself: grabbing and
// dropping cards turns into GameActions calls (same packets as the flat UI).
//
// Layout (table space, +Z away from you; the opponent's half is the same
// layout rotated 180 degrees):
//   left column : security, breeding area, Digi-Egg deck
//   centre      : 12 battle slots (2 rows of 6), memory gauge on the centre line
//   right column: deck, trash, lost
//   your hand   : a fan floating at the near edge, tilted toward you
public class Board3D : MonoBehaviour
{
    // ---------------------------------------------------------------- layout constants (player space, metres)

    const float SlotSpacingX = 0.115f;
    const float RowNearCenterZ = -0.13f;
    const float RowNearPlayerZ = -0.33f;
    const float SideColumnX = 0.58f;
    const float FarColumnX = 0.70f;
    const float PileStep = 0.0012f;
    const float StackStep = 0.0016f;
    const float SourceOffsetZ = 0.017f;
    const float MemoryStep = 0.055f;

    static readonly Vector3 SecurityPos = new Vector3(-SideColumnX, 0f, RowNearCenterZ);
    static readonly Vector3 BreedingPos = new Vector3(-SideColumnX, 0f, RowNearPlayerZ);
    static readonly Vector3 EggDeckPos = new Vector3(-SideColumnX, 0f, -0.53f);
    static readonly Vector3 DeckPos = new Vector3(SideColumnX, 0f, RowNearCenterZ);
    static readonly Vector3 TrashPos = new Vector3(SideColumnX, 0f, RowNearPlayerZ);
    static readonly Vector3 LostPos = new Vector3(FarColumnX, 0f, -0.53f);
    static readonly Vector3 ExecutingPos = new Vector3(0f, 0.10f, -0.50f);
    static readonly Vector3 OpponentHandPos = new Vector3(0f, 0.01f, -0.66f);
    static readonly Vector3 YourHandCenter = new Vector3(0f, 0.20f, -0.64f);
    static readonly Quaternion YourHandRotation = Quaternion.Euler(-60f, 0f, 0f);
    static readonly Vector3 EndButtonPos = new Vector3(0.72f, 0f, -0.66f);
    static readonly Vector3 PreviewPos = new Vector3(-0.70f, 0.30f, -0.55f);

    static readonly Color SlotColor = new Color(0.10f, 0.12f, 0.18f, 0.55f);
    static readonly Color PlayColor = new Color(0.20f, 0.85f, 0.45f);
    static readonly Color DigivolveColor = new Color(1.00f, 0.75f, 0.15f);
    static readonly Color AttackColor = new Color(0.95f, 0.25f, 0.25f);
    static readonly Color BreedingColor = new Color(0.35f, 0.65f, 1.00f);

    // ---------------------------------------------------------------- state

    public Transform Root => transform;
    public bool Visible => gameObject.activeSelf;

    Material _material;
    Material _markerMaterial;
    readonly Dictionary<CardSource, Card3D> _cards = new Dictionary<CardSource, Card3D>();
    readonly HashSet<CardSource> _seen = new HashSet<CardSource>();
    readonly List<CardSource> _toRemove = new List<CardSource>();

    // Slot markers: [0] = you, [1] = opponent; index = FrameID.
    readonly List<Renderer>[] _slotMarkers = { new List<Renderer>(), new List<Renderer>() };
    readonly Dictionary<string, Renderer> _pileMarkers = new Dictionary<string, Renderer>();
    readonly Dictionary<string, VRLabel> _labels = new Dictionary<string, VRLabel>();
    readonly List<Renderer> _memoryTiles = new List<Renderer>();
    Transform _memoryToken;
    Renderer _preview;
    MaterialPropertyBlock _block;
    VRButton3D _endButton;
    VRLabel _status;
    VRLabel _hud;
    string _hudText;

    readonly List<DropTarget> _targets = new List<DropTarget>();
    readonly Player[] _players = new Player[2];
    readonly Dictionary<string, int> _labelCounts = new Dictionary<string, int>();

    /// <summary>Your hand is held in this controller (fanned like real cards) when HandHeld is on.</summary>
    public Transform HoldingHand { get; set; }
    public bool HandHeld { get; set; } = true;

    /// <summary>0 = tight fan (like holding real cards), 1 = wide fan where every card is clearly separated.</summary>
    public float HandSpread
    {
        get => _handSpread;
        set => _handSpread = Mathf.Clamp01(value);
    }

    float _handSpread = 0.65f;

    /// <summary>The player's head (to open the hand panel in front of them).</summary>
    public Transform Head { get; set; }

    /// <summary>SAO-style floating hand panel: every card in a readable grid in front of you.</summary>
    public bool PanelOpen { get; private set; }

    const float PanelCardScale = 0.75f;
    const float PanelGapX = 0.012f;
    const float PanelGapY = 0.016f;
    Vector3 _panelPosition;
    Quaternion _panelRotation;
    Transform _panelFrame;
    Transform _panelAccent;
    VRLabel _panelTitle;
    int _panelCols = -1;
    int _panelRows = -1;
    float _panelOpenTime;
    float _heldDistance;
    string _flash;

    // Cards shown somewhere else for one frame (e.g. revealed cards on the prompt panel).
    struct CardOverride
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public float Scale;
        public Vector3 Hover;
    }

    readonly Dictionary<CardSource, CardOverride> _overrides = new Dictionary<CardSource, CardOverride>();

    /// <summary>Places a card at a board-space pose this frame, overriding its zone position.</summary>
    public void OverrideCard(CardSource card, Vector3 position, Quaternion rotation, float scale, Vector3 hover)
    {
        _overrides[card] = new CardOverride { Position = position, Rotation = rotation, Scale = scale, Hover = hover };
    }
    GameObject _holoBackdrop;
    readonly HashSet<CardSource> _boardOnly = new HashSet<CardSource>(); // cards lying on the field this frame
    bool _fieldHidden;

    /// <summary>Text and state of the End Turn button, mirrored on the wrist disk.</summary>
    public string PhaseText { get; private set; } = "";
    public string EndButtonLabel { get; private set; } = "End Turn";
    public bool EndButtonEnabled { get; private set; }
    float _flashUntil;
    Card3D _hovered;
    float _hoverTime;

    class DropTarget
    {
        public Vector3 Position;   // board space (used when Anchor is null)
        public Transform Anchor;   // a zone that moves with you (wrist disk); world-space distance
        public float Radius;
        public Func<bool> Action;
        public Renderer Marker;
        public Color Color;
        public Color BaseColor;
    }

    /// <summary>The wrist disk (Duel Disk view). Cards from your hand can be laid on its zones.</summary>
    public VRDuelDisk Disk { get; set; }
    bool _duel;

    // A card dropped on the DIGIVOLVE zone with several possible targets waits on the
    // disk until the player points at the Digimon to digivolve onto.
    Card3D _pendingCard;
    readonly List<LegalPlay> _pendingPlays = new List<LegalPlay>();
    public bool HasPendingChoice => _pendingCard != null;

    // ---------------------------------------------------------------- build

    public static Board3D Create(Transform tableTop)
    {
        GameObject go = new GameObject("Board3D") { layer = VRRig.Layer };
        go.transform.SetParent(tableTop, false);
        Board3D board = go.AddComponent<Board3D>();
        board.Build();
        return board;
    }

    void Build()
    {
        _block = new MaterialPropertyBlock();
        // Cards are opaque (stacks must sort by depth); only the zone markers are
        // see-through, so the desk's empty zones let you look through to the arena.
        _material = VRMaterials.Unlit(Color.white);
        _markerMaterial = VRMaterials.Transparent(Color.white);

        for (int side = 0; side < 2; side++)
        {
            Quaternion rotation = SideRotation(side == 0);

            string prefix = side == 0 ? "you." : "opp.";
            AddPile(prefix + "security", rotation * SecurityPos, "Security");
            AddPile(prefix + "eggdeck", rotation * EggDeckPos, "Eggs");
            AddPile(prefix + "deck", rotation * DeckPos, "Deck");
            AddPile(prefix + "trash", rotation * TrashPos, "Trash");
        }

        // Memory gauge along the centre line; your side is +X (blue), opponent's is -X (red).
        for (int v = -10; v <= 10; v++)
        {
            Color color = v > 0 ? new Color(0.15f, 0.35f, 0.85f) : v < 0 ? new Color(0.75f, 0.18f, 0.18f) : new Color(0.85f, 0.85f, 0.85f);
            _memoryTiles.Add(Marker(new Vector3(v * MemoryStep, 0f, 0f), MemoryStep * 0.85f, 0.05f, color));
            Label("mem" + v, new Vector3(v * MemoryStep, 0.004f, 0f), Mathf.Abs(v).ToString(), new Vector2(0.042f, 0.03f), Quaternion.Euler(90f, 0f, 0f)).plateColor = color;
        }

        GameObject token = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        token.name = "MemoryToken";
        token.layer = VRRig.Layer;
        Destroy(token.GetComponent<Collider>());
        token.transform.SetParent(transform, false);
        token.transform.localScale = new Vector3(0.04f, 0.006f, 0.04f);
        token.GetComponent<Renderer>().sharedMaterial = VRMaterials.Unlit(new Color(1f, 0.85f, 0.2f));
        _memoryToken = token.transform;

        _endButton = VRButton3D.Create(transform, EndButtonPos, "End Turn", OnEndButton);
        _status = Label("status", EndButtonPos + new Vector3(-0.1f, 0.09f, 0.02f), "", new Vector2(0.42f, 0.09f), Quaternion.Euler(30f, 0f, 0f));
        _status.enableWordWrapping = true;

        // Big status bar along the far edge of the field: always readable from your seat.
        _hud = Label("hud", new Vector3(0f, 0.003f, 0.88f), "", new Vector2(1.85f, 0.09f), Quaternion.Euler(90f, 0f, 0f));
        _hud.color = new Color(0.55f, 0.95f, 1f);

        GameObject preview = GameObject.CreatePrimitive(PrimitiveType.Quad);
        preview.name = "InspectPreview";
        preview.layer = VRRig.Layer;
        Destroy(preview.GetComponent<Collider>());
        preview.transform.SetParent(transform, false);
        preview.transform.localPosition = PreviewPos;
        preview.transform.localRotation = Quaternion.Euler(15f, -25f, 0f);
        preview.transform.localScale = new Vector3(0.25f, 0.35f, 1f);
        _preview = preview.GetComponent<Renderer>();
        _preview.sharedMaterial = VRMaterials.Unlit(Color.white);
        preview.SetActive(false);

        BuildPanel();
        _viewer = new PileViewer(this, c => VRMaterials.Unlit(c));
        _badges = new StatusBadges(transform);
        _models = new VRFieldModels(Arena != null ? Arena : transform, _badges);
    }

    VRFieldModels _models;

    /// <summary>Floor area in front of the seat where the 3D Digimon / Tamers stand (set by VRSession).</summary>
    public static Transform Arena { get; set; }

    /// <summary>Rebuilds the 3D Digimon (e.g. after changing their size setting).</summary>
    public void RefreshModels() => _models?.Clear();

    public VRFieldModels FieldModels => _models;

    StatusBadges _badges;

    PileViewer _viewer;

    public bool ViewerOpen => _viewer != null && _viewer.IsOpen;

    public void CloseViewer() => _viewer?.Close();

    /// <summary>True while an opened field stack is longer than the panel can show.</summary>
    public bool StackScrollable => _viewer != null && _viewer.StackScrollable;

    public void ScrollStack(float rows) => _viewer?.ScrollStack(rows);

    /// <summary>Opens (or closes) the viewer for the trash / lost zone this card lies in. Returns false if it is not in one.</summary>
    public bool ToggleViewerFor(Card3D card)
    {
        GameContext context = BoardObserver.Context;

        if (context == null || card == null || _viewer == null)
        {
            return false;
        }

        foreach (Player player in new[] { context.You, context.Opponent })
        {
            BoardZone zone = player.TrashCards.Contains(card.Card) ? BoardZone.Trash
                : player.LostCards.Contains(card.Card) ? BoardZone.Lost
                : BoardZone.None;

            if (zone == BoardZone.None)
            {
                continue;
            }

            if (_viewer.IsShowing(player, zone))
            {
                // Clicking a card inside the open viewer just inspects it.
                return true;
            }

            _viewer.Open(player, zone, Head);
            Debug.Log($"[DCGO VR] Viewing {(player.isYou ? "your" : "opponent's")} {zone}");
            return true;
        }

        return false;
    }

    // ---------------------------------------------------------------- trash slot (prompts)

    const float TrashSlotRadius = 0.09f;

    public bool HasTrashSlot => _duel && Disk != null && Disk.isActiveAndEnabled && Disk.TrashSlot != null;

    /// <summary>Starts carrying a hand card toward the disk's trash slot (prompt "trash from hand").</summary>
    public void BeginTrashHold(Card3D card)
    {
        card.Held = true;
        Disk.PushEnabled = false;
        Flash("Place it in the tray, then push the tray closed");
        SetColor(Disk.TrashSlotMarker, new Color(0.6f, 0.3f, 0.1f));
    }

    /// <summary>Brightens the slot while the carried card is over it.</summary>
    public void UpdateTrashHold(Card3D card)
    {
        bool over = Vector3.Distance(card.transform.position, Disk.TrashSlot.position) < TrashSlotRadius;
        SetColor(Disk.TrashSlotMarker, over ? new Color(1f, 0.55f, 0.15f) : new Color(0.6f, 0.3f, 0.1f));
    }

    /// <summary>Releases the carried card: if it is in the slot, it is chosen for the prompt. Returns true if chosen.</summary>
    public bool EndTrashHold(Card3D card)
    {
        bool inSlot = Vector3.Distance(card.transform.position, Disk.TrashSlot.position) < TrashSlotRadius;
        card.Held = false;
        Disk.PushEnabled = true;
        Disk.ResetPush();
        SetColor(Disk.TrashSlotMarker, VRDuelDisk.TrashSlotColor);
        bool chosen = inSlot && VRPromptBridge.TryClick(card.Card);
        Debug.Log($"[DCGO VR] Trash slot: {LegalActions.Name(card.Card)} {(chosen ? "loaded (chosen)" : inSlot ? "loaded but the prompt refused it" : "released outside the slot")}");
        return chosen;
    }

    /// <summary>Keeps the slot lit while a trash-from-hand prompt is waiting.</summary>
    void UpdateTrashSlotHint()
    {
        if (!HasTrashSlot)
        {
            return;
        }

        if (_trashHolding)
        {
            Disk.SetTrayWanted(true);
            return;
        }

        bool waiting = VRPromptBridge.IsDiscardPrompt;
        Disk.SetTrayWanted(waiting);

        if (waiting)
        {
            string purpose = VRPromptBridge.HandSelectionPurpose(out bool isTrash);

            if (purpose != _trayPurpose)
            {
                _trayPurpose = purpose;
                Disk.SetTrayPurpose(purpose, isTrash);
            }
        }

        SetColor(Disk.TrashSlotMarker, waiting ? new Color(0.25f, 0.2f, 0.12f) : VRDuelDisk.TrashSlotColor);

        if (!_trayHooked)
        {
            _trayHooked = true;
            Disk.TrayPushed += OnTrayPushed;
        }
    }

    bool _trashHolding;
    bool _trayHooked;
    string _trayPurpose;

    // Cards confirmed with the tray stay in it until the game actually moves them
    // (otherwise they would briefly jump back into the hand first).
    readonly Dictionary<CardSource, float> _committed = new Dictionary<CardSource, float>();
    const float CommitTimeout = 4f;

    // Pushing the tray closed confirms the selection (End Selection).
    void OnTrayPushed()
    {
        if (_loaded.Count == 0)
        {
            Flash("Place the cards in the tray first, then push it");
            return;
        }

        List<CardSource> confirmed = new List<CardSource>(_loaded);

        if (VRPromptBridge.PressConfirm())
        {
            foreach (CardSource card in confirmed)
            {
                _committed[card] = Time.unscaledTime + CommitTimeout;
            }
        }
        else
        {
            Flash("Press End Selection on the wrist panel");
        }
    }

    bool IsCommitted(CardSource card)
    {
        if (!_committed.TryGetValue(card, out float until))
        {
            return false;
        }

        if (Time.unscaledTime > until)
        {
            _committed.Remove(card);
            return false;
        }

        return true;
    }
    public bool TrashHolding { get => _trashHolding; set => _trashHolding = value; }

    /// <summary>A/X on a Digimon / Tamer: shows every card of its stack in the viewer (or closes it).</summary>
    public bool ToggleStackViewer(Card3D card)
    {
        GameContext context = BoardObserver.Context;

        if (context == null || card == null || _viewer == null)
        {
            return false;
        }

        foreach (Player player in new[] { context.You, context.Opponent })
        {
            foreach (Permanent permanent in player.GetFieldPermanents())
            {
                if (!permanent.cardSources.Contains(card.Card))
                {
                    continue;
                }

                if (_viewer.IsShowingStack(permanent))
                {
                    _viewer.Close();
                }
                else
                {
                    _viewer.OpenStack(permanent, Head);
                    Debug.Log("[DCGO VR] Viewing stack of " + LegalActions.Name(permanent.TopCard));
                }

                return true;
            }
        }

        // A card already shown in the stack viewer: A/X closes it.
        if (_viewer.Stack != null)
        {
            _viewer.Close();
            return true;
        }

        return false;
    }

    readonly List<CardSource> _stackOrder = new List<CardSource>();

    /// <summary>Wrist disk TRASH button: toggles the viewer on your own trash.</summary>
    public void ToggleYourTrash()
    {
        GameContext context = BoardObserver.Context;

        if (context == null || context.You == null || _viewer == null)
        {
            return;
        }

        if (_viewer.IsShowing(context.You, BoardZone.Trash))
        {
            _viewer.Close();
        }
        else
        {
            _viewer.Open(context.You, BoardZone.Trash, Head);
        }
    }

    void BuildPanel()
    {
        GameObject frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
        frame.name = "HandPanel";
        frame.layer = VRRig.Layer;
        Destroy(frame.GetComponent<Collider>());
        frame.transform.SetParent(transform, false);
        frame.GetComponent<Renderer>().sharedMaterial = VRMaterials.Unlit(new Color(0.86f, 0.90f, 0.95f));
        _panelFrame = frame.transform;

        GameObject accent = GameObject.CreatePrimitive(PrimitiveType.Cube);
        accent.name = "HandPanelAccent";
        accent.layer = VRRig.Layer;
        Destroy(accent.GetComponent<Collider>());
        accent.transform.SetParent(transform, false);
        accent.GetComponent<Renderer>().sharedMaterial = VRMaterials.Unlit(new Color(0.95f, 0.65f, 0.15f));
        _panelAccent = accent.transform;

        _panelTitle = VRLabel.Create(transform, Vector3.zero, Quaternion.identity, new Vector2(0.3f, 0.035f), "HAND", new Color(0.15f, 0.18f, 0.25f), new Color(0.86f, 0.90f, 0.95f));
        _panelTitle.alignment = TextAlignmentOptions.Left;
        GameObject title = _panelTitle.gameObject;

        frame.SetActive(false);
        accent.SetActive(false);
        title.SetActive(false);
    }

    public void TogglePanel()
    {
        if (PanelOpen)
        {
            ClosePanel();
        }
        else
        {
            OpenPanel();
        }
    }

    /// <summary>Opens the hand panel about half a metre in front of the head, facing it.</summary>
    public void OpenPanel()
    {
        if (Head == null)
        {
            return;
        }

        Vector3 forward = Head.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 1e-4f)
        {
            forward = Head.up;
            forward.y = 0f;
        }

        forward.Normalize();
        Vector3 worldPosition = Head.position + forward * 0.5f + Vector3.down * 0.12f;
        Quaternion worldRotation = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(12f, 0f, 0f);

        _panelPosition = transform.InverseTransformPoint(worldPosition);
        _panelRotation = Quaternion.Inverse(transform.rotation) * worldRotation;
        _panelCols = _panelRows = -1;
        _panelOpenTime = Time.unscaledTime;
        PanelOpen = true;
        SetPanelVisible(true);
    }

    public void ClosePanel()
    {
        PanelOpen = false;
        SetPanelVisible(false);
    }

    void SetPanelVisible(bool visible)
    {
        _panelFrame.gameObject.SetActive(visible);
        _panelAccent.gameObject.SetActive(visible);
        _panelTitle.gameObject.SetActive(visible);
    }

    void AddPile(string key, Vector3 position, string title)
    {
        _pileMarkers[key] = Marker(position, Card3D.Width + 0.012f, Card3D.Height + 0.012f, SlotColor);
        bool yours = key.StartsWith("you.");
        Vector3 labelPos = position + (yours ? new Vector3(0f, 0.002f, -0.085f) : new Vector3(0f, 0.002f, 0.085f));
        Label(key, labelPos, title, new Vector2(0.17f, 0.04f), Quaternion.Euler(90f, 0f, 0f));
    }

    Renderer Marker(Vector3 position, float width, float height, Color color)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Marker";
        quad.layer = VRRig.Layer;
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(transform, false);
        quad.transform.localPosition = position + new Vector3(0f, 0.0005f, 0f);
        quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        quad.transform.localScale = new Vector3(width, height, 1f);
        Renderer renderer = quad.GetComponent<Renderer>();
        renderer.sharedMaterial = _markerMaterial;
        SetColor(renderer, color);
        return renderer;
    }

    VRLabel Label(string key, Vector3 position, string text, Vector2 size, Quaternion rotation)
    {
        VRLabel label = VRLabel.Create(transform, position, rotation, size, text, new Color(0.9f, 0.92f, 1f));
        _labels[key] = label;
        return label;
    }

    void SetColor(Renderer renderer, Color color)
    {
        renderer.GetPropertyBlock(_block);
        _block.SetColor("_BaseColor", color);
        _block.SetColor("_Color", color);
        renderer.SetPropertyBlock(_block);
    }

    public void SetVisible(bool visible) => gameObject.SetActive(visible);

    public void PressEndTurn() => OnEndButton();

    /// <summary>True while the board is shown as the Duel Disk hologram.</summary>
    public bool DuelStyle => _duel;

    /// <summary>
    /// Hologram look for the Duel Disk view: a dark field with a glowing cyan frame and
    /// centre line, and the big card preview moved to the left of the field.
    /// </summary>
    public void SetDuelStyle(bool duel)
    {
        _duel = duel;
        CancelPending();
        _viewer?.Close();

        if (duel && _holoBackdrop == null)
        {
            _holoBackdrop = new GameObject("HoloBackdrop") { layer = VRRig.Layer };
            _holoBackdrop.transform.SetParent(transform, false);
            Color glow = new Color(0.25f, 0.85f, 1f);
            HoloPiece(new Vector3(0f, -0.004f, 0f), new Vector3(1.9f, 0.002f, 1.62f), new Color(0.02f, 0.05f, 0.09f, 0.3f));
            HoloPiece(new Vector3(0f, 0f, 0.81f), new Vector3(1.92f, 0.006f, 0.012f), glow);
            HoloPiece(new Vector3(0f, 0f, -0.81f), new Vector3(1.92f, 0.006f, 0.012f), glow);
            HoloPiece(new Vector3(0.95f, 0f, 0f), new Vector3(0.012f, 0.006f, 1.62f), glow);
            HoloPiece(new Vector3(-0.95f, 0f, 0f), new Vector3(0.012f, 0.006f, 1.62f), glow);
            HoloPiece(new Vector3(0f, -0.001f, 0.045f), new Vector3(1.2f, 0.003f, 0.004f), glow * 0.7f);
            HoloPiece(new Vector3(0f, -0.001f, -0.045f), new Vector3(1.2f, 0.003f, 0.004f), glow * 0.7f);
        }

        if (_holoBackdrop != null)
        {
            _holoBackdrop.SetActive(duel);
        }

        // Card preview: beside the hologram in Duel Disk view, beside the table otherwise.
        if (duel)
        {
            _preview.transform.localPosition = new Vector3(-1.22f, 0.02f, -0.15f);
            _preview.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _preview.transform.localScale = new Vector3(0.36f, 0.50f, 1f);
        }
        else
        {
            _preview.transform.localPosition = PreviewPos;
            _preview.transform.localRotation = Quaternion.Euler(15f, -25f, 0f);
            _preview.transform.localScale = new Vector3(0.25f, 0.35f, 1f);
        }
    }

    void HoloPiece(Vector3 position, Vector3 scale, Color color)
    {
        GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
        piece.layer = VRRig.Layer;
        Destroy(piece.GetComponent<Collider>());
        piece.transform.SetParent(_holoBackdrop.transform, false);
        piece.transform.localPosition = position;
        piece.transform.localScale = scale;
        piece.GetComponent<Renderer>().sharedMaterial = color.a < 1f ? VRMaterials.Transparent(color) : VRMaterials.Unlit(color);
    }

    // ---------------------------------------------------------------- layout helpers

    static Quaternion SideRotation(bool yours) => yours ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);

    /// <summary>
    /// Player-space position of a field frame. The last frame is the breeding area;
    /// the battle frames are laid out in two rows (front row first).
    /// </summary>
    static Vector3 SlotPosition(int frameId, int frameCount)
    {
        if (frameId >= frameCount - 1)
        {
            return BreedingPos;
        }

        int battleSlots = Mathf.Max(1, frameCount - 1);
        int perRow = Mathf.CeilToInt(battleSlots / 2f);
        int col = frameId % perRow;
        int row = frameId / perRow;
        return new Vector3((col - (perRow - 1) * 0.5f) * SlotSpacingX, 0f, row == 0 ? RowNearCenterZ : RowNearPlayerZ);
    }

    // Slot markers are created once the real number of field frames is known.
    void EnsureSlotMarkers(Player player, int side)
    {
        int count = player.fieldCardFrames.Count;

        if (_slotMarkers[side].Count == count)
        {
            return;
        }

        foreach (Renderer marker in _slotMarkers[side])
        {
            if (marker != null)
            {
                Destroy(marker.gameObject);
            }
        }

        _slotMarkers[side].Clear();
        Quaternion rotation = SideRotation(side == 0);

        for (int frame = 0; frame < count; frame++)
        {
            Color color = frame == count - 1 ? new Color(0.12f, 0.16f, 0.26f) : SlotColor;
            _slotMarkers[side].Add(Marker(rotation * SlotPosition(frame, count), Card3D.Width + 0.012f, Card3D.Height + 0.012f, color));
        }
    }

    Vector3 SlotWorldLocal(Player player, int frameId) =>
        SideRotation(player.isYou) * SlotPosition(frameId, player.fieldCardFrames.Count);

    // ---------------------------------------------------------------- per-frame sync

    void LateUpdate()
    {
        GameContext context = BoardObserver.Context;

        if (context == null || context.You == null || context.Opponent == null)
        {
            HideAll();
            return;
        }

        _seen.Clear();
        _boardOnly.Clear();

        _players[0] = context.You;
        _players[1] = context.Opponent;

        EnsureSlotMarkers(context.You, 0);
        EnsureSlotMarkers(context.Opponent, 1);

        // Close the stack viewer if that Digimon left the field.
        if (_viewer.Stack != null && !context.You.GetFieldPermanents().Contains(_viewer.Stack) && !context.Opponent.GetFieldPermanents().Contains(_viewer.Stack))
        {
            _viewer.Close();
        }

        _badges.BeginFrame();
        _models.BeginFrame();

        foreach (Player player in _players)
        {
            bool yours = player.isYou;
            Quaternion side = SideRotation(yours);

            PlacePile(player.LibraryCards, side * DeckPos, side, false, false);
            PlacePile(player.DigitamaLibraryCards, side * EggDeckPos, side, false, true);
            PlacePile(player.SecurityCards, side * SecurityPos, side, false, false, honorFaceUp: true); // face-up security shows (and loads) its art
            if (_viewer.IsShowing(player, BoardZone.Trash))
            {
                _viewer.Layout(player.TrashCards, Get, "TRASH");
            }
            else
            {
                PlacePile(player.TrashCards, side * TrashPos, side, true, false);
            }

            if (_viewer.IsShowing(player, BoardZone.Lost))
            {
                _viewer.Layout(player.LostCards, Get, "LOST ZONE");
            }
            else
            {
                PlacePile(player.LostCards, side * LostPos, side, true, false);
            }
            PlaceExecuting(player.ExecutingCards, side);

            if (yours)
            {
                PlaceYourHand(player.HandCards);
            }
            else
            {
                PlaceOpponentHand(player.HandCards, side);

                foreach (CardSource card in player.HandCards)
                {
                    if (card != null)
                    {
                        _boardOnly.Add(card);
                    }
                }
            }

            foreach (Permanent permanent in player.GetFieldPermanents())
            {
                PlacePermanent(player, permanent, side);
            }

            string prefix = yours ? "you." : "opp.";
            SetCount(prefix + "deck", "Deck ", player.LibraryCards.Count);
            SetCount(prefix + "security", "Security ", player.SecurityCards.Count);
            SetCount(prefix + "trash", "Trash ", player.TrashCards.Count);
            SetCount(prefix + "eggdeck", "Eggs ", player.DigitamaLibraryCards.Count);
        }

        _badges.EndFrame();
        _models.EndFrame();

        foreach (KeyValuePair<CardSource, CardOverride> pair in _overrides)
        {
            CardOverride o = pair.Value;
            _boardOnly.Remove(pair.Key); // shown in a prompt panel: keep it visible
            Get(pair.Key).SetTarget(o.Position, o.Rotation, true, pair.Key.IsDigiEgg, fastFollow: false, scale: o.Scale, hoverOffset: o.Hover);
        }

        _overrides.Clear();
        UpdateGlows(context);
        ApplyFieldVisibility();

        // Cards that left every zone (tokens deleted, etc.)
        _toRemove.Clear();

        foreach (KeyValuePair<CardSource, Card3D> pair in _cards)
        {
            if (!_seen.Contains(pair.Key))
            {
                _toRemove.Add(pair.Key);
            }
        }

        foreach (CardSource card in _toRemove)
        {
            if (_cards[card] != null)
            {
                Destroy(_cards[card].gameObject);
            }

            _cards.Remove(card);
        }

        UpdateMemory(context);
        UpdateStatus(context);
        UpdatePreview();
        UpdatePending();
        UpdateTrashSlotHint();
    }

    // Mirror the game's own selection outlines (blue = can be chosen, orange = chosen)
    // onto the 3D cards, for the field, your hand and the prompt panel's cards.
    void UpdateGlows(GameContext context)
    {
        foreach (KeyValuePair<CardSource, Card3D> pair in _cards)
        {
            if (pair.Value != null)
            {
                pair.Value.SetGlow(ChoiceGlow(pair.Key) ?? VRPromptBridge.OutlineColor(pair.Key, context));
            }
        }
    }

    /// <summary>Duel Disk view with the field hologram off (VR settings): the board keeps running
    /// (arena models, hand, disk, prompts, viewers) but the cards lying on the field, its frame,
    /// markers, labels, badges and End Turn button are not drawn.</summary>
    bool HideField => _duel && !VRSettings.HoloBoard;

    void ApplyFieldVisibility()
    {
        bool hide = HideField;

        foreach (KeyValuePair<CardSource, Card3D> pair in _cards)
        {
            if (pair.Value != null)
            {
                pair.Value.SetHidden(hide && !pair.Value.Held && _boardOnly.Contains(pair.Key));
            }
        }

        if (_badges != null)
        {
            _badges.Visible = !hide;
        }

        // Only force things off while hidden (markers are rebuilt when frames change); restore once.
        if (!hide && !_fieldHidden)
        {
            return;
        }

        bool show = !hide;
        _fieldHidden = hide;

        if (_holoBackdrop != null)
        {
            _holoBackdrop.SetActive(_duel && show);
        }

        foreach (List<Renderer> side in _slotMarkers)
        {
            foreach (Renderer marker in side)
            {
                if (marker != null && marker.enabled != show)
                {
                    marker.enabled = show;
                }
            }
        }

        foreach (Renderer marker in _pileMarkers.Values)
        {
            if (marker != null && marker.enabled != show)
            {
                marker.enabled = show;
            }
        }

        foreach (Renderer tile in _memoryTiles)
        {
            if (tile != null && tile.enabled != show)
            {
                tile.enabled = show;
            }
        }

        foreach (VRLabel label in _labels.Values)
        {
            if (label != null && label.gameObject.activeSelf != show)
            {
                label.gameObject.SetActive(show);
            }
        }

        if (_memoryToken != null)
        {
            foreach (Renderer renderer in _memoryToken.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = show;
            }
        }

        if (_endButton != null && _endButton.gameObject.activeSelf != show)
        {
            _endButton.gameObject.SetActive(show);
        }
    }

    void HideAll()
    {
        _viewer?.Close();
        _badges?.Clear();
        _models?.Clear();
        foreach (Card3D card in _cards.Values)
        {
            if (card != null)
            {
                Destroy(card.gameObject);
            }
        }

        _cards.Clear();
        _memoryToken.gameObject.SetActive(false);
        _status.text = "";
    }

    Card3D Get(CardSource card)
    {
        _seen.Add(card);

        if (!_cards.TryGetValue(card, out Card3D card3D) || card3D == null)
        {
            card3D = Card3D.Create(transform, card, _material);
            _cards[card] = card3D;
        }

        return card3D;
    }

    void PlacePile(List<CardSource> cards, Vector3 basePos, Quaternion side, bool faceUp, bool egg, bool honorFaceUp = false)
    {
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] == null)
            {
                continue;
            }

            Card3D card = Get(cards[i]);
            _boardOnly.Add(cards[i]);
            card.SetTarget(basePos + new Vector3(0f, 0.001f + i * PileStep, 0f), side, faceUp || (honorFaceUp && cards[i].IsFaceUp), egg);
        }
    }

    void PlaceExecuting(List<CardSource> cards, Quaternion side)
    {
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] == null)
            {
                continue;
            }

            Vector3 offset = new Vector3((i - (cards.Count - 1) * 0.5f) * 0.1f, 0f, 0f);
            Get(cards[i]).SetTarget(side * (ExecutingPos + offset), side * Quaternion.Euler(-35f, 0f, 0f), true, cards[i].IsDigiEgg);
        }
    }

    readonly List<CardSource> _handShown = new List<CardSource>();
    readonly List<CardSource> _loaded = new List<CardSource>();

    /// <summary>
    /// Cards you hold (hand, panel, tray) keep their real size whatever the board's
    /// scale: the board can be shrunk or grown as a desk without the hand following.
    /// </summary>
    float HandScale => 1f / Mathf.Max(0.05f, transform.lossyScale.x);

    void PlaceYourHand(List<CardSource> allCards)
    {
        // Cards loaded into the disk's trash slot (chosen for a discard, not confirmed yet)
        // lie in the slot; the rest stay in your hand.
        _handShown.Clear();
        _loaded.Clear();

        // Forget committed cards that have left the hand (the game moved them).
        if (_committed.Count > 0)
        {
            _toRemove.Clear();

            foreach (CardSource card in _committed.Keys)
            {
                if (!allCards.Contains(card))
                {
                    _toRemove.Add(card);
                }
            }

            foreach (CardSource card in _toRemove)
            {
                _committed.Remove(card);
            }

            _toRemove.Clear();
        }

        foreach (CardSource card in allCards)
        {
            if (HasTrashSlot && (VRPromptBridge.IsLoadedForTrash(card) || IsCommitted(card)))
            {
                _loaded.Add(card);
            }
            else
            {
                _handShown.Add(card);
            }
        }

        for (int i = 0; i < _loaded.Count; i++)
        {
            Transform slot = Disk.TrashSlot;
            Vector3 world = slot.position + slot.up * (0.004f + i * 0.002f) + slot.forward * (i * 0.006f);
            Card3D card = Get(_loaded[i]);

            if (!card.Held)
            {
                card.SetTarget(transform.InverseTransformPoint(world), Quaternion.Inverse(transform.rotation) * slot.rotation, true, _loaded[i].IsDigiEgg, fastFollow: true, scale: HandScale);
            }
        }

        List<CardSource> cards = _handShown;

        if (PanelOpen)
        {
            PlaceHandInPanel(cards);
            return;
        }

        if (HandHeld && HoldingHand != null && HoldingHand.localPosition != Vector3.zero)
        {
            PlaceHandInController(cards);
            return;
        }

        int n = cards.Count;
        float spacing = n <= 1 ? 0f : Mathf.Min(0.08f, 0.85f / (n - 1));
        Vector3 normal = YourHandRotation * Vector3.up;

        for (int i = 0; i < n; i++)
        {
            if (cards[i] == null)
            {
                continue;
            }

            float t = i - (n - 1) * 0.5f;
            Vector3 position = YourHandCenter + new Vector3(t * spacing, -Mathf.Abs(t) * 0.004f, 0f) + normal * (i * 0.0015f);
            Quaternion rotation = YourHandRotation * Quaternion.Euler(0f, t * 1.5f, 0f);
            Get(cards[i]).SetTarget(position, rotation, true, cards[i].IsDigiEgg);
        }
    }

    // Fan of cards held in the controller like a real hand of cards: they rise from a
    // point just above the controller, face back toward you and tilt up to your eyes.
    void PlaceHandInController(List<CardSource> cards)
    {
        int n = cards.Count;

        // HandSpread blends from a tight real-card fan to a wide arc. The visible
        // gap between neighbouring cards is set directly (arc length), so the fan
        // never clumps however many cards you hold; the arc is capped at 170 deg.
        float radius = Mathf.Lerp(0.09f, 0.20f, _handSpread);
        float gap = Mathf.Lerp(0.018f, 0.075f, _handSpread);
        float spread = n <= 1 ? 0f : Mathf.Min(gap / radius * Mathf.Rad2Deg, 170f / (n - 1));
        Vector3 pivot = new Vector3(0f, Mathf.Lerp(0.02f, -0.04f, _handSpread), 0.06f);
        Quaternion tilt = Quaternion.Euler(20f, 0f, 0f);
        Quaternion faceUser = Quaternion.Euler(-90f, 0f, 0f);

        Quaternion toBoard = Quaternion.Inverse(transform.rotation) * HoldingHand.rotation;

        for (int i = 0; i < n; i++)
        {
            if (cards[i] == null)
            {
                continue;
            }

            float angle = -(i - (n - 1) * 0.5f) * spread;
            Quaternion inPlane = Quaternion.AngleAxis(angle, Vector3.forward);
            Vector3 handLocal = pivot + tilt * (inPlane * new Vector3(0f, radius, 0f)) + tilt * new Vector3(0f, 0f, -i * 0.0018f);
            Quaternion handRotation = tilt * inPlane * faceUser;

            Vector3 boardLocal = transform.InverseTransformPoint(HoldingHand.TransformPoint(handLocal));
            Get(cards[i]).SetTarget(boardLocal, toBoard * handRotation, true, cards[i].IsDigiEgg, fastFollow: true, scale: HandScale);
        }
    }

    // Grid of cards on the floating panel. Columns grow with the hand size so a
    // 25-card hand becomes a 7 x 4 grid instead of an unreadable fan.
    void PlaceHandInPanel(List<CardSource> cards)
    {
        int n = Mathf.Max(1, cards.Count);
        int cols = n <= 6 ? n : Mathf.Min(9, Mathf.CeilToInt(Mathf.Sqrt(n * 1.8f)));
        int rows = Mathf.CeilToInt(n / (float)cols);
        float hs = HandScale;
        float cellW = (Card3D.Width * PanelCardScale + PanelGapX) * hs;
        float cellH = (Card3D.Height * PanelCardScale + PanelGapY) * hs;

        if (cols != _panelCols || rows != _panelRows)
        {
            _panelCols = cols;
            _panelRows = rows;
        }

        // Frame + accent bar + title, with a quick SAO-style "unfold" when opened.
        float unfold = Mathf.SmoothStep(0f, 1f, (Time.unscaledTime - _panelOpenTime) / 0.18f);
        float frameW = cols * cellW + 0.03f * hs;
        float frameH = rows * cellH + 0.06f * hs;
        Vector3 frameCenter = _panelPosition + _panelRotation * new Vector3(0f, 0.015f, 0.006f);
        _panelFrame.localPosition = frameCenter;
        _panelFrame.localRotation = _panelRotation;
        _panelFrame.localScale = new Vector3(frameW, frameH * unfold, 0.004f);
        _panelAccent.localPosition = _panelPosition + _panelRotation * new Vector3(0f, 0.015f + frameH * 0.5f * unfold, 0.004f);
        _panelAccent.localRotation = _panelRotation;
        _panelAccent.localScale = new Vector3(frameW, 0.006f * hs, 0.006f);
        _panelTitle.transform.localPosition = _panelPosition + _panelRotation * new Vector3(0.02f * hs, 0.015f + frameH * 0.5f * unfold - 0.022f * hs, -0.002f);
        _panelTitle.transform.localRotation = _panelRotation;
        _panelTitle.Size = new Vector2(Mathf.Round((frameW - 0.04f * hs) * 50f) / 50f, Mathf.Round(0.035f * hs * 200f) / 200f);
        string title = "HAND  " + cards.Count;

        if (_panelTitle.text != title)
        {
            _panelTitle.text = title;
        }

        Quaternion faceUser = _panelRotation * Quaternion.Euler(-90f, 0f, 0f);
        Vector3 hover = _panelRotation * new Vector3(0f, 0f, -0.03f);

        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] == null)
            {
                continue;
            }

            int col = i % cols;
            int row = i / cols;
            // The last row is centred when it is not full.
            int inRow = row == rows - 1 ? cards.Count - row * cols : cols;
            float x = (col - (inRow - 1) * 0.5f) * cellW;
            float y = ((rows - 1) * 0.5f - row) * cellH * unfold;
            Vector3 position = _panelPosition + _panelRotation * new Vector3(x, y, 0f);
            Get(cards[i]).SetTarget(position, faceUser, true, cards[i].IsDigiEgg, fastFollow: false, scale: PanelCardScale * hs, hoverOffset: hover * hs);
        }
    }

    void PlaceOpponentHand(List<CardSource> cards, Quaternion side)
    {
        int n = cards.Count;
        float spacing = n <= 1 ? 0f : Mathf.Min(0.05f, 0.6f / (n - 1));

        for (int i = 0; i < n; i++)
        {
            if (cards[i] == null)
            {
                continue;
            }

            Vector3 position = OpponentHandPos + new Vector3((i - (n - 1) * 0.5f) * spacing, i * PileStep, 0f);
            Get(cards[i]).SetTarget(side * position, side, false, false);
        }
    }

    void PlacePermanent(Player player, Permanent permanent, Quaternion side)
    {
        if (permanent.PermanentFrame == null || permanent.cardSources.Count == 0)
        {
            return;
        }

        if (_viewer.IsShowingStack(permanent))
        {
            // Stack viewer: detail panel (stack effects left, applied effects + big card right).
            _viewer.LayoutStack(permanent, Get);
            _badges.Hide(permanent);
            _models.Hide(permanent);
            return;
        }

        Vector3 slot = SlotWorldLocal(player, permanent.PermanentFrame.FrameID);
        // Only the top card turns sideways when the Digimon is suspended; the cards under it stay upright.
        Quaternion rotation = side;
        Quaternion topRotation = side * (permanent.IsSuspended ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity);
        CardSource top = permanent.TopCard;
        List<CardSource> sources = permanent.cardSources;
        int n = sources.Count;
        int sourceIndex = 0;
        int linkIndex = 0;

        for (int i = 0; i < n; i++)
        {
            CardSource card = sources[i];

            if (card == null)
            {
                continue;
            }

            Vector3 offset;
            float height;

            if (card == top)
            {
                offset = Vector3.zero;
                height = 0.002f + n * StackStep;
            }
            else if (permanent.LinkedCards.Contains(card))
            {
                linkIndex++;
                offset = new Vector3(linkIndex * 0.03f, 0f, 0f);
                height = 0.002f + (n - i) * StackStep * 0.5f;
            }
            else
            {
                sourceIndex++;
                offset = new Vector3(0f, 0f, -Mathf.Min(sourceIndex, 4) * SourceOffsetZ);
                height = 0.002f + (n - sourceIndex) * StackStep * 0.5f;
            }

            Vector3 position = slot + rotation * offset + new Vector3(0f, height, 0f);
            _boardOnly.Add(card);
            Get(card).SetTarget(position, card == top ? topRotation : rotation, VRCompat.CanSee(card), card.IsDigiEgg);
        }

        // DP / BLOCKER / CAN'T ATTACK / CAN'T SUSPEND badges on the top card.
        _badges.Place(permanent, slot + new Vector3(0f, 0.002f + n * StackStep, 0f));

        // 3D Digimon standing on the card (player's own model folder, optional).
        _models.Place(permanent, slot, player.isYou, permanent.IsSuspended);
    }

    Renderer SlotMarker(int side, int frameId) =>
        frameId >= 0 && frameId < _slotMarkers[side].Count ? _slotMarkers[side][frameId] : null;

    // Only rebuilds the label string when the number changes (avoids per-frame garbage).
    void SetCount(string key, string title, int count)
    {
        if (!_labelCounts.TryGetValue(key, out int previous) || previous != count)
        {
            _labelCounts[key] = count;
            SetLabel(key, title + count);
        }
    }

    void SetLabel(string key, string text)
    {
        if (_labels.TryGetValue(key, out VRLabel label) && label.text != text)
        {
            label.text = text;
        }
    }

    void UpdateMemory(GameContext context)
    {
        // gameContext.Memory is stored from player 0's point of view (see MemoryObject.AssignMemoryTab):
        // positive = player 0 has memory. Verified in a headset against the flat gauge.
        int yours = context.You.PlayerID == 0 ? context.Memory : -context.Memory;
        yours = Mathf.Clamp(yours, -10, 10);
        _memoryToken.gameObject.SetActive(true);
        Vector3 target = new Vector3(yours * MemoryStep, 0.006f, 0f);
        _memoryToken.localPosition = Vector3.Lerp(_memoryToken.localPosition, target, 1f - Mathf.Exp(-10f * Time.deltaTime));
    }

    int _statusKey = int.MinValue;

    void UpdateStatus(GameContext context)
    {
        LegalActionSet set = LegalActions.Current;

        // Rebuild the status / HUD strings only when something they show changed
        // (string building every frame was a steady source of garbage).
        bool flashing = !string.IsNullOrEmpty(_flash) && Time.unscaledTime < _flashUntil;
        int key = 17;
        key = key * 31 + (set.IsYourBreedingPhase ? 1 : 0);
        key = key * 31 + (set.IsYourMainPhase ? 1 : 0);
        key = key * 31 + (context.TurnPlayer == null ? 0 : context.TurnPlayer.isYou ? 1 : 2);
        key = key * 31 + (int)context.TurnPhase;
        key = key * 31 + context.Memory;
        key = key * 31 + context.Opponent.HandCards.Count;
        key = key * 31 + (flashing ? _flash.GetHashCode() : 0);

        if (key == _statusKey)
        {
            return;
        }

        _statusKey = key;
        string text;
        bool canPress;

        if (set.IsYourBreedingPhase)
        {
            text = "Breeding phase\nGrab the egg / Digimon, or press Skip";
            _endButton.SetLabel("Skip");
            canPress = true;
        }
        else if (set.IsYourMainPhase)
        {
            text = "Your main phase";
            _endButton.SetLabel("End Turn");
            canPress = true;
        }
        else if (context.TurnPlayer != null && context.TurnPlayer.isYou)
        {
            text = "Resolving...\n(answer prompts on the screen)";
            _endButton.SetLabel("End Turn");
            canPress = false;
        }
        else
        {
            text = "Opponent's turn";
            _endButton.SetLabel("End Turn");
            canPress = false;
        }

        // Lead with the real phase (the disk console and HUD show this line first).
        string phaseName = context.TurnPhase == GameContext.phase.Active ? "Unsuspend" : context.TurnPhase.ToString();
        string turn = context.TurnPlayer != null && context.TurnPlayer.isYou ? "Your" : "Opp.";

        if (context.TurnPhase != GameContext.phase.None)
        {
            text = $"{turn} {phaseName} phase\n" + text.Replace("Breeding phase\n", "").Replace("Your main phase", "Play, digivolve or attack").Replace("Opponent's turn", "Opponent is playing");
        }

        PhaseText = text;
        UpdateHud(context, text);
        EndButtonLabel = set.IsYourBreedingPhase ? "Skip" : "End Turn";
        EndButtonEnabled = canPress;

        if (!string.IsNullOrEmpty(_flash) && Time.unscaledTime < _flashUntil)
        {
            text = _flash;
            PhaseText = _flash;
        }

        if (_status.text != text)
        {
            _status.text = text;
        }

        _endButton.Interactable = canPress;
    }

    void UpdateHud(GameContext context, string phase)
    {
        int memory = context.You.PlayerID == 0 ? context.Memory : -context.Memory;
        string text = $"{phase.Split('\n')[0].ToUpperInvariant()}   |   YOUR MEMORY {(memory > 0 ? "+" : "")}{memory}   |   OPPONENT HAND {context.Opponent.HandCards.Count}";

        if (!string.IsNullOrEmpty(_flash) && Time.unscaledTime < _flashUntil)
        {
            text = _flash.ToUpperInvariant();
        }

        if (text != _hudText)
        {
            _hudText = text;
            _hud.text = text;
        }
    }

    /// <summary>Shows a short message on the status line (e.g. why a card can't be picked up).</summary>
    public void Flash(string message)
    {
        _flash = message;
        _flashUntil = Time.unscaledTime + 2.5f;
    }

    void OnEndButton()
    {
        LegalActionSet set = LegalActions.Current;

        if (set.IsYourBreedingPhase)
        {
            GameActions.Breeding(false);
        }
        else if (set.CanPass)
        {
            GameActions.Pass();
        }
    }

    // ---------------------------------------------------------------- hover / inspect

    public void SetHover(Card3D card)
    {
        if (_hovered == card)
        {
            _hoverTime += Time.deltaTime;
            return;
        }

        if (_hovered != null)
        {
            _hovered.Hovered = false;
        }

        _hovered = card;
        _hoverTime = 0f;

        if (_hovered != null)
        {
            _hovered.Hovered = true;
        }
    }

    void UpdatePreview()
    {
        Sprite sprite = VRSettings.CardPreview && _hovered != null && _hovered.IsFaceUp && _hoverTime > 0.2f ? _hovered.FrontSprite : null;

        if (sprite == null)
        {
            if (_preview.gameObject.activeSelf)
            {
                _preview.gameObject.SetActive(false);
            }

            return;
        }

        Texture2D texture = sprite.texture;
        Rect rect = sprite.textureRect;
        _preview.GetPropertyBlock(_block);
        _block.SetTexture("_BaseMap", texture);
        _block.SetTexture("_MainTex", texture);
        Vector4 st = new Vector4(rect.width / texture.width, rect.height / texture.height, rect.x / texture.width, rect.y / texture.height);
        _block.SetVector("_BaseMap_ST", st);
        _block.SetVector("_MainTex_ST", st);
        _block.SetColor("_BaseColor", Color.white);
        _preview.SetPropertyBlock(_block);

        bool wasHidden = !_preview.gameObject.activeSelf;
        _preview.gameObject.SetActive(true);
        PlacePreviewNearGaze(wasHidden);
    }

    // The zoomed card floats close to you, just above your line of sight to the card
    // you are pointing at, facing you - big enough to read the effect text.
    const float PreviewDistance = 0.45f;
    const float PreviewWidth = 0.2f;

    void PlacePreviewNearGaze(bool snap)
    {
        if (Head == null || _hovered == null)
        {
            return;
        }

        Vector3 toCard = _hovered.transform.position - Head.position;

        if (toCard.sqrMagnitude < 1e-4f)
        {
            return;
        }

        Vector3 target;
        VRArenaTarget arena = VRArenaTarget.Hovered;

        if (arena != null)
        {
            // Pointing into the arena (the desk card is hidden / far): zoom at eye height, towards what
            // you point at, a little to the left so it does not cover the figure.
            Vector3 flat = arena.transform.position - Head.position;
            flat.y = 0f;

            if (flat.sqrMagnitude < 1e-4f)
            {
                flat = Head.forward;
                flat.y = 0f;
            }

            flat = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, flat);
            target = Head.position + flat * PreviewDistance - right * 0.22f + Vector3.down * 0.03f;
        }
        else
        {
            // Cards already close to your face (hand fan, panels): show the zoom a bit further up.
            float lift = toCard.magnitude < 0.7f ? 0.2f : 0.14f;
            target = Head.position + toCard.normalized * PreviewDistance + Vector3.up * lift;
        }

        Quaternion rotation = Quaternion.LookRotation(target - Head.position, Vector3.up);

        Transform t = _preview.transform;
        float follow = snap ? 1f : 1f - Mathf.Exp(-12f * Time.deltaTime);
        t.position = Vector3.Lerp(t.position, target, follow);
        t.rotation = Quaternion.Slerp(t.rotation, rotation, follow);

        // Keep the real size whatever the board's scale (hologram) is.
        Vector3 parentScale = transform.lossyScale;
        float s = Mathf.Max(0.01f, parentScale.x);
        t.localScale = new Vector3(PreviewWidth / s, PreviewWidth * Card3D.Height / Card3D.Width / s, 1f);
    }

    // ---------------------------------------------------------------- grabbing

    /// <summary>
    /// Called when the player grabs a card. Works out what that card may legally do,
    /// lights up those drop zones and returns true; returns false (with a reason) if the
    /// card has nothing legal to do right now.
    /// </summary>
    public bool BeginGrab(Card3D card, out string reason)
    {
        ClearTargets();

        if (_pendingCard != null && _pendingCard != card)
        {
            CancelPending();
        }

        reason = null;
        GameContext context = BoardObserver.Context;
        LegalActionSet set = LegalActions.Current;

        if (context == null || set == LegalActionSet.None)
        {
            reason = "Not your move right now";
            return false;
        }

        Player you = context.You;
        CardSource source = card.Card;

        if (you.HandCards.Contains(source))
        {
            if (_duel && Disk != null && Disk.isActiveAndEnabled)
            {
                AddDiskTargets(source, set);
            }

            foreach (LegalPlay play in set.PlaysFor(source))
            {
                LegalPlay captured = play;

                switch (play.Kind)
                {
                    case LegalPlayKind.PlayToField:
                        foreach (FieldCardFrame frame in you.fieldCardFrames)
                        {
                            if (frame.IsBattleAreaFrame() && frame.IsEmptyFrame())
                            {
                                AddTarget(SlotWorldLocal(you, frame.FrameID), 0.07f, () => GameActions.Play(captured), SlotMarker(0, frame.FrameID), PlayColor);
                            }
                        }

                        break;

                    case LegalPlayKind.Digivolve:
                        AddTarget(SlotWorldLocal(you, play.Frame.FrameID), 0.075f, () => GameActions.Play(captured), SlotMarker(0, play.Frame.FrameID), DigivolveColor);

                        // Onto your breeding Digimon: its hologram beside the disk is a drop zone too.
                        if (!play.Frame.IsBattleAreaFrame() && VRBreedingDisplay.YourAnchor != null)
                        {
                            AddWorldTarget(VRBreedingDisplay.YourAnchor, 0.13f, () => GameActions.Play(captured), VRBreedingDisplay.YourDisc, DigivolveColor, VRBreedingDisplay.YourDiscColor);
                        }

                        break;

                    case LegalPlayKind.Option:
                        AddTarget(new Vector3(0f, 0f, -0.23f), 0.32f, () => GameActions.Play(captured), null, PlayColor);
                        break;

                    default:
                        // DNA / Burst / App Fusion still go through the flat screen for now.
                        reason = play.Kind + ": use the screen for this one";
                        break;
                }
            }
        }
        else if (set.IsYourBreedingPhase)
        {
            bool isEgg = you.DigitamaLibraryCards.Contains(source);
            bool isBreeding = you.GetBreedingAreaPermanents().Exists(p => p.cardSources.Contains(source));

            if (isEgg && set.CanHatch)
            {
                AddTarget(SlotWorldLocal(you, you.fieldCardFrames.Count - 1), 0.08f, () => GameActions.Breeding(true), SlotMarker(0, you.fieldCardFrames.Count - 1), BreedingColor);
            }
            else if (isBreeding && set.CanMoveFromBreeding)
            {
                foreach (FieldCardFrame frame in you.fieldCardFrames)
                {
                    if (frame.IsBattleAreaFrame() && frame.IsEmptyFrame())
                    {
                        AddTarget(SlotWorldLocal(you, frame.FrameID), 0.07f, () => GameActions.Breeding(true), SlotMarker(0, frame.FrameID), BreedingColor);
                    }
                }
            }
        }
        else
        {
            Permanent attacker = you.GetFieldPermanents().Find(p => p.TopCard == source);

            if (attacker != null)
            {
                foreach (LegalAttack attack in set.AttacksFor(attacker))
                {
                    LegalAttack captured = attack;

                    if (attack.Target == null)
                    {
                        AddTarget(SideRotation(false) * SecurityPos, 0.09f, () => GameActions.Attack(captured), _pileMarkers["opp.security"], AttackColor);
                    }
                    else if (attack.Target.PermanentFrame != null)
                    {
                        int frameId = attack.Target.PermanentFrame.FrameID;
                        AddTarget(SlotWorldLocal(context.Opponent, frameId), 0.075f, () => GameActions.Attack(captured), SlotMarker(1, frameId), AttackColor);
                    }
                }
            }
        }

        if (_targets.Count == 0)
        {
            reason = reason ?? "That card can't do anything right now";
            Debug.Log($"[DCGO VR] Can't pick up {LegalActions.Name(source)}: {reason}");
            return false;
        }

        card.Held = true;
        _heldDistance = -1f;
        Debug.Log($"[DCGO VR] Picked up {LegalActions.Name(source)}: {_targets.Count} drop zone(s) lit");
        return true;
    }

    void AddTarget(Vector3 position, float radius, Func<bool> action, Renderer marker, Color color)
    {
        _targets.Add(new DropTarget { Position = position, Radius = radius, Action = action, Marker = marker, Color = color, BaseColor = SlotColor });

        if (marker != null)
        {
            SetColor(marker, color * 0.6f);
        }
    }

    void AddWorldTarget(Transform anchor, float radius, Func<bool> action, Renderer marker, Color color, Color baseColor)
    {
        _targets.Add(new DropTarget { Anchor = anchor, Radius = radius, Action = action, Marker = marker, Color = color, BaseColor = baseColor });

        if (marker != null)
        {
            SetColor(marker, color * 0.6f);
        }
    }

    // ---------------------------------------------------------------- duel disk: card menu

    class MenuOption
    {
        public string Label;
        public bool Enabled;
        public Func<bool> Action;
    }

    readonly List<MenuOption> _menuOptions = new List<MenuOption>();
    readonly List<VRButton3D> _menuButtons = new List<VRButton3D>();
    Transform _menuRoot;

    // One zone on the disk. Dropping a card there either does its only possible move,
    // or opens a menu listing every move the card has right now.
    void AddDiskTargets(CardSource source, LegalActionSet set)
    {
        if (!set.PlaysFor(source).GetEnumerator().MoveNext() && !set.Skills.Exists(s => s.HandCard == source))
        {
            return;
        }

        AddWorldTarget(Disk.PlayZone, 0.09f, () => OpenCardMenu(source), Disk.PlayMarker, PlayColor, VRDuelDisk.ZoneColor);
    }

    bool OpenCardMenu(CardSource source)
    {
        LegalActionSet set = LegalActions.Current;
        _menuOptions.Clear();
        _pendingPlays.Clear();

        foreach (LegalPlay play in set.PlaysFor(source))
        {
            LegalPlay captured = play;

            switch (play.Kind)
            {
                case LegalPlayKind.PlayToField:
                    _menuOptions.Add(new MenuOption { Label = "Play to field", Enabled = true, Action = () => GameActions.Play(captured) });
                    break;

                case LegalPlayKind.Option:
                    _menuOptions.Add(new MenuOption { Label = "Use option", Enabled = true, Action = () => GameActions.Play(captured) });
                    break;

                case LegalPlayKind.Digivolve:
                    _pendingPlays.Add(play);
                    _menuOptions.Add(new MenuOption { Label = "Digivolve onto " + ShortName(play.TargetPermanent.TopCard) + (play.Frame.IsBattleAreaFrame() ? "" : " (breeding)"), Enabled = true, Action = () => GameActions.Play(captured) });
                    break;

                case LegalPlayKind.Jogress:
                    _menuOptions.Add(new MenuOption { Label = "DNA onto " + ShortName(play.TargetPermanent.TopCard), Enabled = true, Action = () => BeginJogressChoice(source, captured) });
                    break;

                case LegalPlayKind.Burst:
                    _menuOptions.Add(new MenuOption { Label = "Burst onto " + ShortName(play.TargetPermanent.TopCard), Enabled = true, Action = () => BeginBurstChoice(source, captured) });
                    break;

                case LegalPlayKind.AppFusion:
                    _menuOptions.Add(new MenuOption { Label = "App Fusion onto " + ShortName(play.TargetPermanent.TopCard), Enabled = true, Action = () => BeginAppFusionChoice(source, captured) });
                    break;
            }
        }

        foreach (LegalSkill skill in set.Skills)
        {
            if (skill.HandCard == source)
            {
                LegalSkill captured = skill;
                _menuOptions.Add(new MenuOption { Label = "Activate: " + skill.Effect.EffectName, Enabled = skill.CanUse, Action = () => GameActions.UseSkill(captured) });
            }
        }

        int enabled = _menuOptions.FindAll(o => o.Enabled).Count;

        if (enabled == 0)
        {
            Flash("Nothing to do with that card here - use the screen");
            return false;
        }

        if (enabled == 1)
        {
            // Only one thing it can do: just do it.
            _pendingPlays.Clear();
            return _menuOptions.Find(o => o.Enabled).Action();
        }

        if (!_cards.TryGetValue(source, out Card3D card))
        {
            return false;
        }

        _pendingCard = card;
        _menuOptions.Add(new MenuOption { Label = "Cancel", Enabled = true, Action = () => true });
        BuildMenu();
        Flash("Choose what to do with " + ShortName(source));
        return true;
    }

    static string ShortName(CardSource card) => card == null ? "?" : card.BaseENGCardNameFromEntity;

    void BuildMenu()
    {
        ClearMenu();

        if (_menuRoot == null)
        {
            GameObject root = new GameObject("CardMenu") { layer = VRRig.Layer };
            _menuRoot = root.transform;
        }

        _menuRoot.SetParent(Disk.transform, false);
        _menuRoot.localPosition = Disk.PlayZone.localPosition + new Vector3(0f, 0.16f, 0.02f);
        _menuRoot.gameObject.SetActive(true);

        const float width = 0.24f;
        const float height = 0.034f;
        const float step = 0.04f;
        float top = (_menuOptions.Count - 1) * step * 0.5f;

        for (int i = 0; i < _menuOptions.Count; i++)
        {
            MenuOption option = _menuOptions[i];
            VRButton3D button = VRButton3D.CreatePlate(_menuRoot, new Vector3(0f, top - i * step, 0f), new Vector2(width, height), option.Label, () => ChooseMenu(option));
            button.Interactable = option.Enabled;
            _menuButtons.Add(button);
        }
    }

    void ChooseMenu(MenuOption option)
    {
        bool cancelled = option.Label == "Cancel";
        Debug.Log("[DCGO VR] Card menu: " + option.Label);
        CancelPending();

        if (!cancelled)
        {
            option.Action();
        }
    }

    void ClearMenu()
    {
        foreach (VRButton3D button in _menuButtons)
        {
            if (button != null)
            {
                Destroy(button.gameObject);
            }
        }

        _menuButtons.Clear();

        if (_menuRoot != null)
        {
            _menuRoot.gameObject.SetActive(false);
        }
    }

    void LightPending()
    {
        foreach (LegalPlay play in _pendingPlays)
        {
            Renderer marker = play.Frame != null ? SlotMarker(0, play.Frame.FrameID) : null;

            if (marker != null)
            {
                SetColor(marker, DigivolveColor);
            }
        }

        if (Disk != null)
        {
            SetColor(Disk.PlayMarker, PlayColor);
        }
    }

    /// <summary>While the menu is open, the laser can also pick a glowing Digimon on the hologram to digivolve onto it.</summary>
    public void ResolvePending(Card3D clicked)
    {
        if (_pendingCard == null || clicked == null)
        {
            return;
        }

        foreach (LegalPlay play in _pendingPlays)
        {
            if (play.TargetPermanent != null && play.TargetPermanent.cardSources.Contains(clicked.Card))
            {
                LegalPlay chosen = play;
                CancelPending();
                GameActions.Play(chosen);
                return;
            }
        }
    }

    public void CancelPending()
    {
        if (_pendingCard != null)
        {
            _pendingCard.Held = false;
        }

        foreach (LegalPlay play in _pendingPlays)
        {
            Renderer marker = play.Frame != null ? SlotMarker(0, play.Frame.FrameID) : null;

            if (marker != null)
            {
                SetColor(marker, SlotColor);
            }
        }

        if (Disk != null)
        {
            SetColor(Disk.PlayMarker, VRDuelDisk.ZoneColor);
        }

        ClearMenu();
        _pendingCard = null;
        _pendingPlays.Clear();
        _permanentMenu = false;
    }

    void UpdatePending()
    {
        UpdateTargetChoice();

        if (_pendingCard == null)
        {
            if (_permanentMenu)
            {
                if (!LegalActions.Current.IsYourMainPhase)
                {
                    CancelPending();
                }
                else
                {
                    FaceMenu();
                }
            }

            return;
        }

        // The game moved on (turn ended, card left the hand...): close the menu.
        if (_pendingCard.Card == null || !BoardObserver.Context.You.HandCards.Contains(_pendingCard.Card) || !LegalActions.Current.IsYourMainPhase)
        {
            CancelPending();
            return;
        }

        if (Disk != null)
        {
            _pendingCard.transform.position = Disk.PlayZone.position + Disk.PlayZone.up * 0.004f;
            _pendingCard.transform.rotation = Disk.PlayZone.rotation;
        }

        FaceMenu();
        LightPending();
    }

    void FaceMenu()
    {
        if (_menuRoot != null && _menuRoot.gameObject.activeSelf && Head != null)
        {
            // Menu always faces you, whatever your arm is doing.
            Vector3 away = _menuRoot.position - Head.position;

            if (away.sqrMagnitude > 1e-4f)
            {
                _menuRoot.rotation = Quaternion.LookRotation(away, Vector3.up);
            }
        }
    }

    // ---------------------------------------------------------------- field card menu ([Main] effects / attack)

    bool _permanentMenu;

    /// <summary>
    /// Your Digimon / Tamer with a usable [Main] effect: opens a menu on the disk with
    /// "Attack" (if it can) and its effects. Returns false if it has no effect to offer
    /// (then the card is simply grabbed to attack, as before).
    /// </summary>
    /// <param name="withAttack">From the arena (no dragging there): also open the menu when
    /// the only thing it can do is attack.</param>
    public bool TryOpenPermanentMenu(Card3D card, bool withAttack = false)
    {
        GameContext context = BoardObserver.Context;
        LegalActionSet set = LegalActions.Current;

        if (!_duel || Disk == null || context == null || !set.IsYourMainPhase)
        {
            return false;
        }

        Permanent permanent = context.You.GetFieldPermanents().Find(p => p.TopCard == card.Card);

        if (permanent == null)
        {
            return false;
        }

        List<LegalSkill> skills = set.Skills.FindAll(s => s.Permanent == permanent);
        List<LegalAttack> attacks = new List<LegalAttack>(set.AttacksFor(permanent));

        if (skills.Count == 0 && (!withAttack || attacks.Count == 0))
        {
            return false;
        }

        CancelPending();
        _menuOptions.Clear();

        if (attacks.Count > 0)
        {
            _menuOptions.Add(new MenuOption { Label = "Attack", Enabled = true, Action = () => BeginAttackChoice(attacks) });
        }

        foreach (LegalSkill skill in skills)
        {
            LegalSkill captured = skill;
            _menuOptions.Add(new MenuOption { Label = "Activate: " + skill.Effect.EffectName, Enabled = skill.CanUse, Action = () => GameActions.UseSkill(captured) });
        }

        _menuOptions.Add(new MenuOption { Label = "Cancel", Enabled = true, Action = () => true });
        _permanentMenu = true;
        BuildMenu();
        Flash("Choose what " + ShortName(card.Card) + " does");
        return true;
    }

    // ---------------------------------------------------------------- pointing at a target (attack, DNA partner)

    class TargetOption
    {
        public Permanent Permanent;
        public CardSource Card;      // a specific card (e.g. a linked card for App Fusion)
        public bool Security;
        public Func<bool> Action;
    }

    /// <summary>The desk card for a card (null if it is not on the desk).</summary>
    public Card3D FindCard(CardSource card) => card != null && _cards.TryGetValue(card, out Card3D card3D) ? card3D : null;

    /// <summary>The desk card that stands for a figure in the arena: the permanent's top card,
    /// or for the opponent in person one of their security cards (= attack the player).</summary>
    public Card3D CardForArena(VRArenaTarget target)
    {
        GameContext context = BoardObserver.Context;

        if (target == null || context == null)
        {
            return null;
        }

        if (target.OpponentPlayer)
        {
            if (context.Opponent == null)
            {
                return null;
            }

            foreach (CardSource security in context.Opponent.SecurityCards)
            {
                Card3D card = FindCard(security);

                if (card != null)
                {
                    return card;
                }
            }

            return null;
        }

        return target.Permanent != null ? FindCard(target.Permanent.TopCard) : null;
    }

    /// <summary>Glow of an arena figure: target of the current pointing choice (attack...),
    /// else the outline the game shows for it (selectable by an effect).</summary>
    public Color? ArenaGlow(Permanent permanent)
    {
        if (permanent == null)
        {
            return null;
        }

        foreach (TargetOption option in _choice)
        {
            if (option.Permanent == permanent || (option.Card != null && permanent.cardSources.Contains(option.Card)))
            {
                return _choiceColor;
            }
        }

        return permanent.TopCard != null ? VRPromptBridge.OutlineColor(permanent.TopCard, BoardObserver.Context) : null;
    }

    /// <summary>True while the opponent player (their security) can be picked as the attack target.</summary>
    public bool OpponentIsTarget
    {
        get
        {
            foreach (TargetOption option in _choice)
            {
                if (option.Security)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Glow for cards that are targets of the current pointing choice (App Fusion linked cards).</summary>
    public Color? ChoiceGlow(CardSource card)
    {
        foreach (TargetOption option in _choice)
        {
            if (option.Card == card)
            {
                return _choiceColor;
            }
        }

        return null;
    }

    // Burst: pick the Tamer that returns to the hand.
    bool BeginBurstChoice(CardSource card, LegalPlay play)
    {
        BurstDigivolutionCondition condition = card.burstDigivolutionCondition;
        _choice.Clear();

        if (condition != null)
        {
            foreach (Permanent tamer in BoardObserver.Context.You.GetBattleAreaPermanents())
            {
                if (tamer != play.TargetPermanent && condition.tamerCondition(tamer) && !tamer.CannotReturnToHand(null))
                {
                    Permanent captured = tamer;
                    _choice.Add(new TargetOption { Permanent = tamer, Action = () => GameActions.PlayBurst(play, captured) });
                }
            }
        }

        return FinishChoice("No Tamer can be returned for this Burst", "Point at the Tamer to return for Burst + Trigger");
    }

    // App Fusion: pick which linked card of the target is used.
    bool BeginAppFusionChoice(CardSource card, LegalPlay play)
    {
        AppFusionCondition condition = card.appFusionCondition;
        _choice.Clear();

        if (condition != null)
        {
            foreach (CardSource linked in play.TargetPermanent.LinkedCards)
            {
                if (condition.linkedCondition(play.TargetPermanent, linked))
                {
                    CardSource captured = linked;
                    _choice.Add(new TargetOption { Card = linked, Action = () => GameActions.PlayAppFusion(play, captured) });
                }
            }
        }

        return FinishChoice("No linked card can be used for this App Fusion", "Point at the linked card to fuse + Trigger");
    }

    bool FinishChoice(string none, string prompt)
    {
        if (_choice.Count == 0)
        {
            Flash(none);
            return false;
        }

        if (_choice.Count == 1)
        {
            Func<bool> only = _choice[0].Action;
            _choice.Clear();
            return only();
        }

        _choiceColor = DigivolveColor;
        Flash(prompt);
        return true;
    }

    readonly List<TargetOption> _choice = new List<TargetOption>();
    Color _choiceColor;

    /// <summary>True while waiting for the player to point at a target (laser + trigger).</summary>
    public bool HasTargetChoice => _choice.Count > 0;

    bool BeginAttackChoice(List<LegalAttack> attacks)
    {
        _choice.Clear();

        foreach (LegalAttack attack in attacks)
        {
            LegalAttack captured = attack;
            _choice.Add(new TargetOption { Permanent = attack.Target, Security = attack.Target == null, Action = () => GameActions.Attack(captured) });
        }

        _choiceColor = AttackColor;
        Flash("Point at the target (or the security) + Trigger");
        return true;
    }

    bool BeginJogressChoice(CardSource card, LegalPlay play)
    {
        GameContext context = BoardObserver.Context;
        _choice.Clear();

        foreach (Permanent partner in context.You.GetBattleAreaPermanents())
        {
            if (partner == play.TargetPermanent)
            {
                continue;
            }

            if (card.CanJogressFromTargetPermanents(new List<Permanent> { play.TargetPermanent, partner }, true))
            {
                Permanent capturedPartner = partner;
                _choice.Add(new TargetOption { Permanent = partner, Action = () => GameActions.PlayJogress(play, play.TargetPermanent, capturedPartner) });
            }
        }

        if (_choice.Count == 0)
        {
            Flash("No second Digimon can be used for this DNA");
            return false;
        }

        if (_choice.Count == 1)
        {
            Func<bool> only = _choice[0].Action;
            _choice.Clear();
            return only();
        }

        _choiceColor = DigivolveColor;
        Flash("Point at the second Digimon for DNA + Trigger");
        return true;
    }

    /// <summary>The player pointed (laser + trigger): run the matching target, or cancel.</summary>
    /// <summary>Attack-target choice: pick "the player / her security" directly (laser on her shields or
    /// on her). False when attacking the player is not one of the options.</summary>
    public bool ResolveSecurityTarget()
    {
        foreach (TargetOption option in _choice)
        {
            if (option.Security)
            {
                TargetOption chosen = option;
                ClearTargetChoice();
                chosen.Action();
                return true;
            }
        }

        return false;
    }

    /// <summary>The short message currently flashed (e.g. "Cancelled"), or null.</summary>
    public string FlashMessage => !string.IsNullOrEmpty(_flash) && Time.unscaledTime < _flashUntil ? _flash : null;

    public void ResolveTargetChoice(Card3D pointed)
    {
        GameContext context = BoardObserver.Context;
        TargetOption chosen = null;

        if (pointed != null && context != null)
        {
            foreach (TargetOption option in _choice)
            {
                if (option.Card != null)
                {
                    if (option.Card == pointed.Card)
                    {
                        chosen = option;
                        break;
                    }

                    continue;
                }

                if (option.Permanent != null && option.Permanent.cardSources.Contains(pointed.Card))
                {
                    chosen = option;
                    break;
                }

                if (option.Security && context.Opponent.SecurityCards.Contains(pointed.Card))
                {
                    chosen = option;
                    break;
                }
            }
        }

        ClearTargetChoice();

        if (chosen != null)
        {
            chosen.Action();
        }
        else
        {
            Flash("Cancelled");
        }
    }

    void UpdateTargetChoice()
    {
        if (_choice.Count == 0)
        {
            return;
        }

        if (!LegalActions.Current.IsYourMainPhase)
        {
            ClearTargetChoice();
            return;
        }

        foreach (TargetOption option in _choice)
        {
            Renderer marker = ChoiceMarker(option);

            if (marker != null)
            {
                SetColor(marker, _choiceColor);
            }
        }
    }

    Renderer ChoiceMarker(TargetOption option)
    {
        if (option.Security)
        {
            return _pileMarkers["opp.security"];
        }

        if (option.Card != null || option.Permanent == null || option.Permanent.PermanentFrame == null || option.Permanent.TopCard == null)
        {
            return null;
        }

        return SlotMarker(option.Permanent.TopCard.Owner.isYou ? 0 : 1, option.Permanent.PermanentFrame.FrameID);
    }

    void ClearTargetChoice()
    {
        foreach (TargetOption option in _choice)
        {
            Renderer marker = ChoiceMarker(option);

            if (marker != null)
            {
                SetColor(marker, SlotColor);
            }
        }

        _choice.Clear();
    }

    /// <summary>Moves a held card along the table under the laser and brightens the zone it is over.</summary>
    public void MoveHeld(Card3D card, Ray worldRay)
    {
        Vector3 origin = transform.InverseTransformPoint(worldRay.origin);
        Vector3 direction = transform.InverseTransformDirection(worldRay.direction);
        const float holdHeight = 0.05f;
        float follow = 1f - Mathf.Exp(-25f * Time.deltaTime);
        float distance = Mathf.Abs(direction.y) > 1e-4f ? (holdHeight - origin.y) / direction.y : -1f;
        Vector3 local;

        if (distance > 0f && distance < 3f)
        {
            // Laser points at the table: the card slides along the table under it.
            local = origin + direction * distance;
            local.x = Mathf.Clamp(local.x, -0.85f, 0.85f);
            local.z = Mathf.Clamp(local.z, -0.78f, 0.78f);
            card.transform.localRotation = Quaternion.Slerp(card.transform.localRotation, Quaternion.identity, 1f - Mathf.Exp(-15f * Time.deltaTime));
        }
        else
        {
            // Laser points into the air (e.g. at the hand panel): the card floats on the laser
            // at the distance it was picked up, so you always see it following you.
            if (_heldDistance <= 0f)
            {
                _heldDistance = Mathf.Clamp(Vector3.Distance(origin, card.transform.localPosition), 0.15f, 1.5f);
            }

            local = origin + direction * _heldDistance;
        }

        card.transform.localPosition = Vector3.Lerp(card.transform.localPosition, local, follow);
        HighlightTargetUnder(card);
    }

    void HighlightTargetUnder(Card3D card)
    {
        DropTarget over = FindTarget(card);

        foreach (DropTarget target in _targets)
        {
            if (target.Marker != null)
            {
                SetColor(target.Marker, target == over ? target.Color : target.Color * 0.6f);
            }
        }
    }

    /// <summary>Moves a card that is physically held: it keeps the pose it had relative to the hand when grabbed.</summary>
    public void MoveHeldAttached(Card3D card, Vector3 worldPosition, Quaternion worldRotation)
    {
        card.transform.SetPositionAndRotation(worldPosition, worldRotation);
        HighlightTargetUnder(card);
    }

    /// <summary>Moves a card held by touch with the controller; the zone under it (straight down) is highlighted.</summary>
    public void MoveHeldDirect(Card3D card, Transform hand)
    {
        float t = 1f - Mathf.Exp(-40f * Time.deltaTime);

        if (_duel)
        {
            // Held like a real card: flat, in front of the fingers, top edge pointing forward.
            Vector3 forward = hand.forward;
            forward.y = 0f;
            Quaternion rotation = forward.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(forward.normalized, Vector3.up) : card.transform.rotation;
            card.transform.position = Vector3.Lerp(card.transform.position, hand.position + hand.forward * 0.05f, t);
            card.transform.rotation = Quaternion.Slerp(card.transform.rotation, rotation, t);
        }
        else
        {
            Vector3 local = transform.InverseTransformPoint(hand.position + hand.forward * 0.04f);
            local.y = Mathf.Max(local.y, 0.02f);
            card.transform.localPosition = Vector3.Lerp(card.transform.localPosition, local, t);
            card.transform.localRotation = Quaternion.Slerp(card.transform.localRotation, Quaternion.identity, t);
        }

        HighlightTargetUnder(card);
    }

    /// <summary>Drops the held card: performs the action of the zone under it, if any. Returns true if an action was sent.</summary>
    public bool Drop(Card3D card)
    {
        Vector3 at = card.transform.localPosition;
        DropTarget target = FindTarget(card);
        bool sent = target != null && target.Action();
        Debug.Log(target == null
            ? $"[DCGO VR] Dropped {LegalActions.Name(card.Card)} at ({at.x:F2},{at.y:F2},{at.z:F2}) - not over a lit zone, card returns"
            : $"[DCGO VR] Dropped {LegalActions.Name(card.Card)} on a lit zone - move {(sent ? (_pendingCard == card ? "waiting for a digivolve target" : "sent") : "refused (see Action rejected)")}");
        card.Held = _pendingCard == card;
        ClearTargets();

        if (_pendingCard != null)
        {
            LightPending();
        }

        return sent;
    }

    DropTarget FindTarget(Card3D card)
    {
        DropTarget best = null;
        float bestDistance = float.MaxValue;
        Vector3 local = transform.InverseTransformPoint(card.transform.position);
        Vector3 world = card.transform.position;

        foreach (DropTarget target in _targets)
        {
            float distance;

            if (target.Anchor != null)
            {
                distance = Vector3.Distance(world, target.Anchor.position);
            }
            else
            {
                // On the hologram, a card held far from the field plane (e.g. in your hand)
                // must not count as being over a slot just because it lines up with it.
                if (_duel && Mathf.Abs(local.y) > 0.2f)
                {
                    continue;
                }

                distance = new Vector2(local.x - target.Position.x, local.z - target.Position.z).magnitude;
            }

            if (distance <= target.Radius && distance < bestDistance)
            {
                best = target;
                bestDistance = distance;
            }
        }

        return best;
    }

    void ClearTargets()
    {
        foreach (DropTarget target in _targets)
        {
            if (target.Marker != null)
            {
                SetColor(target.Marker, target.BaseColor);
            }
        }

        _targets.Clear();
    }
}
#endif
