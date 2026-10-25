#if DCGO_VR
using UnityEngine;
using UnityEngine.XR;

// === DCGO-CUSTOM:vr ===
// Card handling, split like real hands vs a pointer:
//
//   GRIP  = your real hand. Grabs only what the controller is actually touching
//           (a card in your fan, in the hand panel, on the disk...). The card stays
//           in your hand exactly as you grabbed it and moves/turns with it. Release
//           it on a lit zone (the disk's PLACE CARD zone, or a slot on the table)
//           to play it; anywhere else and it goes back.
//   TRIGGER = the laser. Clicks the game screen, presses buttons and menu entries,
//           and grabs far things on the field (your Digimon to attack, then point
//           at the target and release). A laser-held card follows the laser.
//
//   Touching a button or menu entry and pressing Trigger or Grip presses it.
//   The laser hides while your hand is touching something.
//
//   Your hand of cards is held in the LEFT controller (fan).
//   X (left) tight/wide fan, left stick left/right fine-tunes the spread.
//   Hand panel (Sword Art Online style): click the LEFT thumbstick (or Menu), or
//   hold Grip + Trigger on the right controller and swipe DOWN; swipe UP closes.
[DefaultExecutionOrder(-28000)]
public class VRGrabber : MonoBehaviour
{
    const float TouchRadius = 0.06f;
    const float ButtonTouchRadius = 0.035f;

    VRRig _rig;
    VRLaserPointer _laser;
    Board3D _board;

    Card3D _held;
    XRNode _heldNode;
    bool _heldByGrip;               // true: physically held (released with grip); false: laser-held (released with trigger)
    bool _trashHold;                // carrying a hand card to the disk's trash slot (prompt)
    Vector3 _holdOffsetPosition;    // card pose relative to the hand at grab time
    Quaternion _holdOffsetRotation;

    // Grab the world: both grips in empty space move / turn / resize the hologram.
    bool _worldGrab;
    Vector3 _worldGrabMid;
    Vector3 _worldGrabSpan;

    // [hand][0 = trigger, 1 = grip]
    readonly bool[,] _previous = new bool[2, 2];
    bool _boardWasHidden;
    static readonly XRNode[] RightThenLeft = { XRNode.RightHand, XRNode.LeftHand };
    static readonly XRNode[] LeftThenRight = { XRNode.LeftHand, XRNode.RightHand };
    readonly Collider[] _overlap = new Collider[24];

    const string SpreadPrefKey = "DCGO_VR_HandSpread";
    const float TightSpread = 0.15f;
    const float WideSpread = 1f;
    float _savedSpread = -1f;

    public void Init(VRRig rig, VRLaserPointer laser, Board3D board)
    {
        _rig = rig;
        _laser = laser;
        _board = board;

        try
        {
            board.HandSpread = PlayerPrefs.GetFloat(SpreadPrefKey, board.HandSpread);
        }
        catch
        {
            // Preferences unavailable: keep the default spread.
        }

        _savedSpread = board.HandSpread;
    }

    struct Buttons
    {
        public bool TriggerDown, TriggerUp, GripDown, GripUp;
    }

    Buttons Read(XRNode node)
    {
        VRRig.HandInput hand = _rig.Hand(node);
        int h = node == XRNode.RightHand ? 0 : 1;
        bool trigger = hand.Trigger;
        bool grip = hand.Grip;
        Buttons b = new Buttons
        {
            TriggerDown = trigger && !_previous[h, 0],
            TriggerUp = !trigger && _previous[h, 0],
            GripDown = grip && !_previous[h, 1],
            GripUp = !grip && _previous[h, 1],
        };
        _previous[h, 0] = trigger;
        _previous[h, 1] = grip;
        return b;
    }

    void Update()
    {
        if (_board == null || _rig == null || _laser == null)
        {
            return;
        }

        VRRig.HandInput left = _rig.Hand(XRNode.LeftHand);
        left.Tick();

        if ((left.MenuPressedThisFrame || left.StickClickPressedThisFrame) && !_board.Visible)
        {
            // Menus (no board): the left menu button opens the VR settings.
            VRSession.Instance?.Settings?.Toggle();
            VRLaserPointer.Haptic(XRNode.LeftHand, 0.3f, 0.04f);
        }
        else if ((left.MenuPressedThisFrame || left.StickClickPressedThisFrame) && _board.Visible && _board.ViewerOpen)
        {
            // Left stick click closes an open trash viewer first.
            _board.CloseViewer();
        }
        else if ((left.MenuPressedThisFrame || left.StickClickPressedThisFrame) && _board.Visible)
        {
            _board.TogglePanel();
            VRLaserPointer.Haptic(XRNode.LeftHand, 0.3f, 0.04f);
            Debug.Log($"[DCGO VR] Hand panel {(_board.PanelOpen ? "opened" : "closed")}");
        }

        if (!_board.Visible)
        {
            // Release anything held once when the board goes away; doing it every
            // frame would also forget the trigger state and re-press buttons.
            if (!_boardWasHidden)
            {
                _boardWasHidden = true;
                CancelHold();
            }

            HandleButtonsOnly();
            return;
        }

        _boardWasHidden = false;

        HandleSpreadInput(left);
        HandleSwipeGesture();
        HandleStackScroll();

        Buttons right = Read(XRNode.RightHand);
        Buttons leftButtons = Read(XRNode.LeftHand);

        if (_worldGrab)
        {
            UpdateWorldGrab();
            return;
        }

        if (_held != null)
        {
            Buttons holder = _heldNode == XRNode.RightHand ? right : leftButtons;
            UpdateHeld(_heldByGrip ? holder.GripUp : holder.TriggerUp);
            return;
        }

        // ---- 1) Real hands: what each controller is touching.
        Card3D hover = null;
        bool touching = false;

        foreach (XRNode node in RightThenLeft)
        {
            Transform hand = _rig.HandTransform(node);
            Buttons b = node == XRNode.RightHand ? right : leftButtons;

            VRButton3D touchedButton = FindTouchedButton(hand);

            if (touchedButton != null)
            {
                touching = true;
                touchedButton.SetHovered(true);

                if (b.TriggerDown || b.GripDown)
                {
                    touchedButton.Press();
                    VRLaserPointer.Haptic(node, 0.5f, 0.05f);
                }

                continue;
            }

            // The left hand can't grab from the fan it is holding.
            bool skipOwnHand = node == XRNode.LeftHand && _board.HandHeld && !_board.PanelOpen;
            Card3D touched = FindTouchedCard(hand, skipOwnHand);

            if (touched == null)
            {
                continue;
            }

            touching = true;
            hover = touched;

            if (_board.HasTrashSlot && VRPromptBridge.IsDiscardPrompt)
            {
                // Discard prompt: hand cards are chosen only by loading them into the
                // disk's trash slot (grip). Gripping a loaded card takes it back out.
                if (b.GripDown && VRPromptBridge.WantsTrashFromHand(touched.Card))
                {
                    StartTrashHold(touched, node);
                    return;
                }

                if (b.GripDown && VRPromptBridge.IsLoadedForTrash(touched.Card))
                {
                    VRPromptBridge.TryClick(touched.Card);
                    VRLaserPointer.Haptic(node, 0.3f, 0.04f);
                    Debug.Log("[DCGO VR] Trash slot: took " + LegalActions.Name(touched.Card) + " back out");
                    return;
                }

                if (b.TriggerDown && touched.Card.Owner != null && touched.Card.Owner.isYou)
                {
                    _board.Flash("Grip the card and place it in the tray on your disk");
                    continue;
                }
            }

            if ((b.GripDown || b.TriggerDown) && VRPromptBridge.TryClick(touched.Card))
            {
                // A prompt is running and the screen would accept this card: choose it.
                VRLaserPointer.Haptic(node, 0.4f, 0.04f);
                return;
            }

            if (b.GripDown)
            {
                TryGrab(touched, node, physically: true);
                return;
            }
        }

        // Both grips held in empty space (nothing touched): grab the world.
        if (!touching && _rig.DuelMode && (right.GripDown || leftButtons.GripDown)
            && _rig.Hand(XRNode.RightHand).Grip && _rig.Hand(XRNode.LeftHand).Grip
            && !_rig.Hand(XRNode.RightHand).Trigger && !_rig.Hand(XRNode.LeftHand).Trigger)
        {
            StartWorldGrab();
            return;
        }

        // ---- 2) The laser (trigger).
        _laser.BlockScreenInput = touching;
        _laser.Hidden = touching;

        Card3D pointed = null;
        VRButton3D button = null;

        if (!touching && _laser.HasHit && _laser.CurrentHit.collider != null)
        {
            pointed = _laser.CurrentHit.collider.GetComponentInParent<Card3D>();
            button = _laser.CurrentHit.collider.GetComponentInParent<VRButton3D>();
        }

        _board.SetHover(hover != null ? hover : pointed);

        if (button != null)
        {
            button.SetHovered(true);
        }

        Buttons laserHand = _laser.ActiveHand == XRNode.RightHand ? right : leftButtons;

        // A / X while pointing at a field card: show its whole stack (inherited effects).
        if (!touching && pointed != null && _rig.Hand(_laser.ActiveHand).PrimaryPressedThisFrame && _board.ToggleStackViewer(pointed))
        {
            VRLaserPointer.Haptic(_laser.ActiveHand, 0.25f, 0.03f);
            return;
        }

        if (!laserHand.TriggerDown || touching)
        {
            return;
        }

        if (button != null)
        {
            button.Press();
            VRLaserPointer.Haptic(_laser.ActiveHand, 0.4f, 0.04f);
        }
        else if (_board.HasTargetChoice)
        {
            // Waiting for an attack target / DNA partner: this press picks it (or cancels).
            _board.ResolveTargetChoice(pointed);
            VRLaserPointer.Haptic(_laser.ActiveHand, 0.5f, 0.05f);
        }
        else if (pointed != null && _board.HasTrashSlot && VRPromptBridge.IsDiscardPrompt
                 && pointed.Card.Owner != null && pointed.Card.Owner.HandCards.Contains(pointed.Card))
        {
            _board.Flash("Grip the card and place it in the tray on your disk");
        }
        else if (pointed != null && VRPromptBridge.TryClick(pointed.Card))
        {
            VRLaserPointer.Haptic(_laser.ActiveHand, 0.4f, 0.04f);
        }
        else if (pointed != null && _board.ToggleViewerFor(pointed))
        {
            // Trash / lost zone: open the viewer (or just inspect a card already in it).
            VRLaserPointer.Haptic(_laser.ActiveHand, 0.25f, 0.03f);
        }
        else if (pointed != null && _board.HasPendingChoice && !pointed.Card.Owner.HandCards.Contains(pointed.Card))
        {
            // Card menu open: pointing at a glowing Digimon picks it as the digivolve target.
            _board.ResolvePending(pointed);
        }
        else if (pointed != null && _board.TryOpenPermanentMenu(pointed))
        {
            // Your Digimon / Tamer with a [Main] effect: menu on the disk (Attack / Activate...).
            VRLaserPointer.Haptic(_laser.ActiveHand, 0.3f, 0.03f);
        }
        else if (pointed != null)
        {
            TryGrab(pointed, _laser.ActiveHand, physically: false);
        }
    }

    // ---------------------------------------------------------------- grab the world

    void StartWorldGrab()
    {
        Vector3 l = _rig.LeftHand.position;
        Vector3 r = _rig.RightHand.position;
        _worldGrabMid = (l + r) * 0.5f;
        _worldGrabSpan = r - l;
        _worldGrabSpan.y = 0f;
        _worldGrab = true;
        _rig.BeginHoloGrab();
        _laser.Hidden = true;
        _laser.BlockScreenInput = true;
        VRLaserPointer.Haptic(XRNode.LeftHand, 0.3f, 0.04f);
        VRLaserPointer.Haptic(XRNode.RightHand, 0.3f, 0.04f);
    }

    void UpdateWorldGrab()
    {
        if (!_rig.Hand(XRNode.LeftHand).Grip || !_rig.Hand(XRNode.RightHand).Grip)
        {
            _worldGrab = false;
            _rig.EndHoloGrab();
            _laser.Hidden = false;
            _laser.BlockScreenInput = false;
            return;
        }

        Vector3 l = _rig.LeftHand.position;
        Vector3 r = _rig.RightHand.position;
        Vector3 mid = (l + r) * 0.5f;
        Vector3 span = r - l;
        span.y = 0f;

        float yaw = _worldGrabSpan.sqrMagnitude > 1e-4f && span.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(_worldGrabSpan, span, Vector3.up) : 0f;
        float ratio = _worldGrabSpan.magnitude > 0.05f ? span.magnitude / _worldGrabSpan.magnitude : 1f;
        // Moving both hands moves the field the same way (like pulling a tablecloth).
        _rig.UpdateHoloGrab((mid - _worldGrabMid) * 1.5f, yaw, ratio);
    }

    // ---------------------------------------------------------------- touching

    VRButton3D FindTouchedButton(Transform hand)
    {
        if (hand == null || hand.localPosition == Vector3.zero)
        {
            return null;
        }

        int count = Physics.OverlapSphereNonAlloc(hand.position, ButtonTouchRadius, _overlap, 1 << VRRig.Layer, QueryTriggerInteraction.Collide);

        for (int i = 0; i < count; i++)
        {
            VRButton3D button = _overlap[i].GetComponentInParent<VRButton3D>();

            if (button != null && button.Interactable)
            {
                return button;
            }
        }

        return null;
    }

    Card3D FindTouchedCard(Transform hand, bool skipOwnHand)
    {
        if (hand == null || hand.localPosition == Vector3.zero)
        {
            return null;
        }

        int count = Physics.OverlapSphereNonAlloc(hand.position, TouchRadius, _overlap, 1 << VRRig.Layer, QueryTriggerInteraction.Collide);
        Card3D best = null;
        float bestDistance = float.MaxValue;
        GameContext context = BoardObserver.Context;

        for (int i = 0; i < count; i++)
        {
            Card3D card = _overlap[i].GetComponentInParent<Card3D>();

            if (card == null || card.Held)
            {
                continue;
            }

            if (skipOwnHand && context != null && context.You != null && context.You.HandCards.Contains(card.Card))
            {
                continue;
            }

            float distance = (_overlap[i].ClosestPoint(hand.position) - hand.position).sqrMagnitude;

            if (distance < bestDistance)
            {
                best = card;
                bestDistance = distance;
            }
        }

        return best;
    }

    // ---------------------------------------------------------------- holding

    void TryGrab(Card3D card, XRNode node, bool physically)
    {
        if (_board.BeginGrab(card, out string reason))
        {
            _held = card;
            _heldNode = node;
            _heldByGrip = physically;
            _laser.BlockScreenInput = true;

            Transform hand = _rig.HandTransform(node);
            _holdOffsetRotation = Quaternion.Inverse(hand.rotation) * card.transform.rotation;
            _holdOffsetPosition = hand.InverseTransformPoint(card.transform.position);

            // Don't keep the card buried inside the controller.
            if (physically && _holdOffsetPosition.magnitude < 0.03f)
            {
                _holdOffsetPosition = new Vector3(0f, 0f, 0.05f);
            }

            VRLaserPointer.Haptic(node, 0.3f, 0.03f);
        }
        else
        {
            _board.Flash(reason);
            VRLaserPointer.Haptic(node, 0.1f, 0.08f);
        }
    }

    void StartTrashHold(Card3D card, XRNode node)
    {
        _held = card;
        _heldNode = node;
        _heldByGrip = true;
        _trashHold = true;
        _board.TrashHolding = true;
        _laser.BlockScreenInput = true;

        Transform hand = _rig.HandTransform(node);
        _holdOffsetRotation = Quaternion.Inverse(hand.rotation) * card.transform.rotation;
        _holdOffsetPosition = hand.InverseTransformPoint(card.transform.position);

        if (_holdOffsetPosition.magnitude < 0.03f)
        {
            _holdOffsetPosition = new Vector3(0f, 0f, 0.05f);
        }

        _board.BeginTrashHold(card);
        VRLaserPointer.Haptic(node, 0.3f, 0.03f);
    }

    void UpdateHeld(bool released)
    {
        _board.SetHover(null);
        _laser.Hidden = _heldByGrip;

        if (_trashHold)
        {
            Transform holder = _rig.HandTransform(_heldNode);
            _held.transform.SetPositionAndRotation(holder.TransformPoint(_holdOffsetPosition), holder.rotation * _holdOffsetRotation);
            _board.UpdateTrashHold(_held);

            if (released)
            {
                bool chosen = _board.EndTrashHold(_held);
                VRLaserPointer.Haptic(_heldNode, chosen ? 0.7f : 0.15f, chosen ? 0.08f : 0.03f);
                _held = null;
                _trashHold = false;
                _board.TrashHolding = false;
                _laser.BlockScreenInput = false;
                _laser.Hidden = false;
            }

            return;
        }

        if (_heldByGrip)
        {
            Transform hand = _rig.HandTransform(_heldNode);
            _board.MoveHeldAttached(_held, hand.TransformPoint(_holdOffsetPosition), hand.rotation * _holdOffsetRotation);
        }
        else
        {
            _board.MoveHeld(_held, _rig.PointerRay(_heldNode));
        }

        if (!released)
        {
            return;
        }

        bool sent = _board.Drop(_held);
        VRLaserPointer.Haptic(_heldNode, sent ? 0.6f : 0.15f, sent ? 0.06f : 0.03f);
        _held = null;
        _laser.BlockScreenInput = false;
        _laser.Hidden = false;
    }

    void CancelHold()
    {
        if (_held != null)
        {
            _held.Held = false;
            _held = null;
        }

        if (_trashHold)
        {
            _trashHold = false;
            _board.TrashHolding = false;
        }

        if (_laser != null)
        {
            _laser.BlockScreenInput = false;
            _laser.Hidden = false;
        }

        for (int h = 0; h < 2; h++)
        {
            _previous[h, 0] = _previous[h, 1] = false;
        }
    }

    void OnDisable() => CancelHold();

    // ---------------------------------------------------------------- fan spread

    // Left controller adjusts the held fan (only when the left hand is not the laser hand,
    // so X keeps meaning right-click and the stick keeps meaning scroll for a left laser).
    // Without the board (menus, flat views) only the 3D buttons work: the VR
    // keyboard, the settings panel. Touch them or point the laser + trigger.
    void HandleButtonsOnly()
    {
        Buttons right = Read(XRNode.RightHand);
        Buttons leftButtons = Read(XRNode.LeftHand);
        bool touching = false;

        foreach (XRNode node in RightThenLeft)
        {
            VRButton3D touched = FindTouchedButton(_rig.HandTransform(node));

            if (touched == null)
            {
                continue;
            }

            touching = true;
            touched.SetHovered(true);
            Buttons b = node == XRNode.RightHand ? right : leftButtons;

            if (b.TriggerDown || b.GripDown)
            {
                touched.Press();
                VRLaserPointer.Haptic(node, 0.5f, 0.05f);
            }
        }

        _laser.BlockScreenInput = touching;
        _laser.Hidden = touching;

        if (touching || !_laser.HasHit || _laser.CurrentHit.collider == null)
        {
            return;
        }

        VRButton3D button = _laser.CurrentHit.collider.GetComponentInParent<VRButton3D>();

        if (button == null)
        {
            return;
        }

        button.SetHovered(true);
        Buttons laserHand = _laser.ActiveHand == XRNode.RightHand ? right : leftButtons;

        if (laserHand.TriggerDown)
        {
            button.Press();
            VRLaserPointer.Haptic(_laser.ActiveHand, 0.4f, 0.04f);
        }
    }

    // Long stacks: either thumbstick up/down scrolls the stack list
    // (unless the hand's B/Y is held, which adjusts the table height).
    void HandleStackScroll()
    {
        if (!_board.StackScrollable)
        {
            return;
        }

        float y = 0f;

        foreach (XRNode node in LeftThenRight)
        {
            VRRig.HandInput hand = _rig.Hand(node);

            if (!hand.SecondaryHeld && Mathf.Abs(hand.Stick.y) > Mathf.Abs(y))
            {
                y = hand.Stick.y;
            }
        }

        if (Mathf.Abs(y) > 0.3f)
        {
            // Stick up = towards the top of the stack.
            _board.ScrollStack(-y * 6f * Time.unscaledDeltaTime);
        }
    }

    void HandleSpreadInput(VRRig.HandInput left)
    {
        if (!_board.HandHeld || _laser.ActiveHand == XRNode.LeftHand || left.SecondaryHeld)
        {
            return;
        }

        if (left.PrimaryPressedThisFrame)
        {
            _board.HandSpread = _board.HandSpread < (TightSpread + WideSpread) * 0.5f ? WideSpread : TightSpread;
            VRLaserPointer.Haptic(XRNode.LeftHand, 0.25f, 0.03f);
        }

        float x = left.Stick.x;

        if (Mathf.Abs(x) > 0.2f)
        {
            _board.HandSpread += x * 0.8f * Time.unscaledDeltaTime;
        }

        if (Mathf.Abs(_board.HandSpread - _savedSpread) > 0.01f && Mathf.Abs(x) <= 0.2f)
        {
            _savedSpread = _board.HandSpread;

            try
            {
                PlayerPrefs.SetFloat(SpreadPrefKey, _savedSpread);
            }
            catch
            {
                // Not critical.
            }
        }
    }

    // ---------------------------------------------------------------- SAO swipe

    // Grip + Trigger held on the right controller, then a quick vertical swipe.
    // Down opens the hand panel, up closes it.
    const float SwipeSpeed = 1.1f;      // m/s
    const float SwipeCooldown = 0.6f;
    Vector3 _lastRightPosition;
    bool _hasLastRight;
    float _nextSwipeTime;

    void HandleSwipeGesture()
    {
        Transform right = _rig.RightHand;

        if (right == null || right.localPosition == Vector3.zero)
        {
            _hasLastRight = false;
            return;
        }

        Vector3 position = right.position;
        Vector3 velocity = _hasLastRight && Time.deltaTime > 0f ? (position - _lastRightPosition) / Time.deltaTime : Vector3.zero;
        _lastRightPosition = position;
        _hasLastRight = true;

        VRRig.HandInput hand = _rig.Hand(XRNode.RightHand);

        if (_held != null || !(hand.Trigger && hand.Grip) || Time.unscaledTime < _nextSwipeTime)
        {
            return;
        }

        float horizontal = new Vector2(velocity.x, velocity.z).magnitude;

        if (Mathf.Abs(velocity.y) < SwipeSpeed || Mathf.Abs(velocity.y) < horizontal * 1.5f)
        {
            return;
        }

        if (velocity.y < 0f && !_board.PanelOpen)
        {
            _board.OpenPanel();
        }
        else if (velocity.y > 0f && _board.PanelOpen)
        {
            _board.ClosePanel();
        }
        else
        {
            return;
        }

        Debug.Log($"[DCGO VR] Swipe {(velocity.y < 0f ? "down" : "up")}: hand panel {(_board.PanelOpen ? "opened" : "closed")}");
        _nextSwipeTime = Time.unscaledTime + SwipeCooldown;
        VRLaserPointer.Haptic(XRNode.RightHand, 0.4f, 0.05f);
    }
}
#endif
