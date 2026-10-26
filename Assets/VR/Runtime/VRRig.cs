#if DCGO_VR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

// === DCGO-CUSTOM:vr ===
// Headset camera, controllers and the play space (floor, table, game screen).
// Everything lives on layer 31 so the game's cameras never see it and the
// headset camera never sees the game's own world objects.
//
// Comfort controls (either hand):
//   B / Y tap            recenter (puts you back in your seat, facing the table)
//   B / Y hold + stick   raise / lower the table
//   (Duel Disk view only: arena + wrist disk. View switching was removed.)
//   B / Y hold + stick      in Duel Disk view: raise / lower the hologram
//   B / Y long press (1.5s) in Duel Disk view: reset the hologram's position / turn / size
//   Both grips in empty space (Duel Disk view): grab the world - move, turn and
//                           resize the hologram field (remembered between sessions)
//   (the LEFT thumbstick click opens the hand panel, see VRGrabber)
public class VRRig : MonoBehaviour
{
    public const int Layer = 31;

    static readonly Vector3 SeatPosition = new Vector3(0f, 0f, -1.05f);
    static readonly float[] TiltPresets = { 25f, 50f, 75f };

    const float ScreenWidthMeters = 1.4f;
    const float MonitorWidthMeters = 1.3f;
    const float TableDepth = 1.55f;
    const float DefaultTableHeight = 0.74f;
    const float SeatedEyeHeightAboveTable = 0.45f;

    public Camera Camera { get; private set; }
    public Transform TrackingSpace { get; private set; }
    public Transform LeftHand { get; private set; }
    public Transform RightHand { get; private set; }
    public Collider ScreenCollider { get; private set; }

    /// <summary>The table-top surface (y = 0 is the playing surface, +Z away from you).</summary>
    public Transform TableTop => _tableRoot;

    /// <summary>Mode 0 = Duel Disk (hologram field), 1 = 3D board on the table, 2..4 = flat game lying on the table.</summary>
    public bool BoardMode => _mode <= 1;
    public bool DuelMode => _mode == 0;

    /// <summary>Parent of the 3D board in Duel Disk view: a tilted, scaled hologram in front of you.</summary>
    public Transform HoloAnchor { get; private set; }

    public event System.Action ViewChanged;

    /// <summary>
    /// True outside a duel (title, deck editor, lobby): the game screen becomes a
    /// big cinema screen straight ahead and the table / hologram are hidden.
    /// </summary>
    public bool MenuLayout { get; private set; }

    /// <summary>Screen transform (for things docked to it, like the keyboard).</summary>
    public Transform Screen => _screen;

    /// <summary>
    /// The duel arena on the floor in front of the seat (rig space, y = floor):
    /// 3D Digimon and Tamers stand here, yours near you, the opponent's far away.
    /// </summary>
    public Transform ArenaAnchor { get; private set; }

    /// <summary>Where the native VR menu stands (rig space): ahead of the seat at arm's-length reading distance.</summary>
    public Transform MenuAnchor { get; private set; }

    public void SetMenuLayout(bool menu)
    {
        if (MenuLayout == menu)
        {
            return;
        }

        MenuLayout = menu;
        ApplyTableLayout();
        ViewChanged?.Invoke();
        Debug.Log("[DCGO VR] Layout: " + (menu ? "menu screen" : "duel view"));
    }

    // Menu cinema screen (rig space, relative to the seat and eye height).
    const float MenuScreenWidth = 2.1f;
    const float MenuScreenDistance = 1.75f;
    const float MenuScreenBelowEye = 0.12f;
    float _menuHeightOffset;
    float _menuDistanceOffset;

    /// <summary>Only the Duel Disk view is used: switching views is disabled (always mode 0).
    /// The Table / flat-screen layouts are kept in code but can no longer be reached.</summary>
    public void CycleView()
    {
        _mode = 0;
        ApplyTableLayout();
        ViewChanged?.Invoke();
    }

    /// <summary>Shows or hides the flat game screen (side screen / table screen).</summary>
    public void SetScreenVisible(bool visible)
    {
        if (_screen != null && _screen.gameObject.activeSelf != visible)
        {
            _screen.gameObject.SetActive(visible);
        }
    }

    // Hologram placement (rig space, relative to the seat and the eye height measured at recenter).
    // "Duel desk": the board sits low and close, tilted like a desk, so you can
    // look over it at the arena stage right behind it (one view, no head turning).
    const float HoloDistance = 1.2f;
    const float HoloBelowEye = 0.80f;
    const float HoloTilt = 22f;
    const float HoloScale = 0.85f;

    // Arena stage behind the desk: a little lower than the eyes, its front just
    // above the desk's far edge as seen from the seat.
    const float StageBehindDesk = 3.1f;   // seat-to-stage-centre minus HoloDistance
    const float StageBelowEye = 0.95f;
    const float OverviewDrop = 4.2f;       // stage depth below the eyes in overview
    const float OverviewBack = 0.8f;       // arena pulled towards you in overview
    const float SideScreenWidth = 1.0f;

    GameObject _floor;
    GameObject _platform;

    // Overview hides the room floor (the arena lies far below) and shows a small
    // glowing platform under the seat so you still feel grounded.
    void SetOverviewRoom(bool overview)
    {
        if (_floor != null && _floor.activeSelf == overview)
        {
            _floor.SetActive(!overview);
        }

        if (_platform == null && overview)
        {
            _platform = new GameObject("SeatPlatform") { layer = Layer };
            _platform.transform.SetParent(transform, false);
            _platform.transform.localPosition = new Vector3(SeatPosition.x, 0f, SeatPosition.z);

            GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            SetupProp(disc, _platform.transform, new Color(0.07f, 0.09f, 0.13f), keepCollider: false);
            disc.transform.localScale = new Vector3(0.8f, 0.02f, 0.8f); // just under your feet: the arena below stays in view
            disc.transform.localPosition = new Vector3(0f, -0.02f, 0f);

            GameObject rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            SetupProp(rim, _platform.transform, new Color(0.25f, 0.85f, 1f), keepCollider: false);
            rim.transform.localScale = new Vector3(0.86f, 0.01f, 0.86f);
            rim.transform.localPosition = new Vector3(0f, -0.035f, 0f);
        }

        if (_platform != null && _platform.activeSelf != overview)
        {
            _platform.SetActive(overview);
        }
    }

    /// <summary>Moves the duel arena to the distance chosen in the VR settings.</summary>
    public void ApplyArenaDistance()
    {
        if (MenuLayout)
        {
            if (_arenaPreview)
            {
                PlaceArena();
            }

            return;
        }

        if (DuelMode)
        {
            ApplyDuelLayout();
            return;
        }

        ArenaAnchor.localPosition = new Vector3(SeatPosition.x, 0f, SeatPosition.z + VRSettings.ArenaDistance);
        ArenaAnchor.localRotation = Quaternion.identity;
    }
    float _eyeHeight = 1.2f;
    float _holoHeightOffset;

    // Player's own placement of the hologram (grab the world), saved in PlayerPrefs.
    Vector3 _holoUserOffset;
    float _holoUserYaw;
    float _holoUserScale = 1f;
    Vector3 _grabStartOffset;
    float _grabStartYaw;
    float _grabStartScale;
    const string HoloPrefKey = "DCGO_VR_Holo2"; // new key: the desk layout starts from its defaults

    Transform _tableRoot;
    Transform _screen;
    int _mode;
    float _tableHeight = DefaultTableHeight;
    bool _floorTracking;
    bool _recenteredOnce;
    float _heightCheckUntil = 10f;   // keep checking the tracking origin this long after start / after an origin change
    int _validHeadFrames;

    readonly Dictionary<XRNode, HandInput> _hands = new Dictionary<XRNode, HandInput>();

    // ---------------------------------------------------------------- build

    public static VRRig Create(Transform parent, Texture gameScreen)
    {
        GameObject root = new GameObject("VRRig") { layer = Layer };
        root.transform.SetParent(parent, false);
        VRRig rig = root.AddComponent<VRRig>();
        rig.Build(gameScreen);
        return rig;
    }

    void Build(Texture gameScreen)
    {
        _floorTracking = TrySetFloorTracking();
        WatchTrackingOrigin();

        TrackingSpace = NewChild("TrackingSpace", transform);

        Transform head = NewChild("Head", TrackingSpace);
        Camera = head.gameObject.AddComponent<Camera>();
        Camera.cullingMask = 1 << Layer;
        Camera.clearFlags = CameraClearFlags.SolidColor;
        Camera.backgroundColor = new Color(0.05f, 0.06f, 0.09f);
        Camera.nearClipPlane = 0.02f;
        Camera.farClipPlane = 100f;
        Camera.stereoTargetEye = StereoTargetEyeMask.Both;
        Camera.depth = 100;
        AddPoseDriver(head, "<XRHMD>/centerEyePosition", "<XRHMD>/centerEyeRotation");

        LeftHand = BuildHand("LeftHand", "{LeftHand}");
        RightHand = BuildHand("RightHand", "{RightHand}");
        _hands[XRNode.LeftHand] = new HandInput("{LeftHand}");
        _hands[XRNode.RightHand] = new HandInput("{RightHand}");

        HoloAnchor = NewChild("HoloAnchor", transform);
        MenuAnchor = NewChild("MenuAnchor", transform);
        ArenaAnchor = NewChild("ArenaAnchor", transform);
        ArenaAnchor.localPosition = new Vector3(SeatPosition.x, 0f, SeatPosition.z + VRSettings.ArenaDistance); // placed by the layout
        LoadHoloPlacement();
        BuildRoom(gameScreen);

        if (!_floorTracking)
        {
            // Device-relative tracking: put the eyes at a seated height.
            TrackingSpace.localPosition = new Vector3(0f, _tableHeight + SeatedEyeHeightAboveTable, 0f);
        }
    }

    Transform BuildHand(string name, string handUsage)
    {
        Transform hand = NewChild(name, TrackingSpace);
        AddPoseDriver(hand, "<XRController>" + handUsage + "/pointerPosition", "<XRController>" + handUsage + "/pointerRotation");

        // Small visible controller stand-in.
        GameObject model = GameObject.CreatePrimitive(PrimitiveType.Cube);
        model.name = "ControllerModel";
        model.layer = Layer;
        Destroy(model.GetComponent<Collider>());
        model.transform.SetParent(hand, false);
        model.transform.localScale = new Vector3(0.03f, 0.03f, 0.1f);
        model.transform.localPosition = new Vector3(0f, 0f, -0.05f);
        model.GetComponent<Renderer>().sharedMaterial = VRMaterials.Unlit(new Color(0.2f, 0.22f, 0.26f));
        return hand;
    }

    void BuildRoom(Texture gameScreen)
    {
        // Floor
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        _floor = floor;
        SetupProp(floor, transform, new Color(0.11f, 0.12f, 0.15f), keepCollider: false);
        floor.transform.localScale = new Vector3(3f, 1f, 3f);

        _tableRoot = NewChild("Table", transform);

        // Table top + pedestal (positions are relative to the table top surface).
        GameObject top = GameObject.CreatePrimitive(PrimitiveType.Cube);
        top.name = "TableTop";
        SetupProp(top, _tableRoot, new Color(0.30f, 0.20f, 0.13f), keepCollider: false);
        top.transform.localPosition = new Vector3(0f, -0.025f, 0f);
        top.transform.localScale = new Vector3(1.75f, 0.05f, TableDepth);

        GameObject pedestal = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pedestal.name = "Pedestal";
        SetupProp(pedestal, _tableRoot, new Color(0.18f, 0.12f, 0.08f), keepCollider: false);
        pedestal.transform.localScale = new Vector3(0.35f, 1f, 0.35f);

        // Game screen (a quad; MeshCollider gives texture coordinates for the laser).
        GameObject screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
        screen.name = "GameScreen";
        screen.layer = Layer;
        screen.transform.SetParent(_tableRoot, false);
        screen.transform.localScale = new Vector3(ScreenWidthMeters, ScreenWidthMeters * VRGameScreen.Height / VRGameScreen.Width, 1f);
        screen.GetComponent<Renderer>().sharedMaterial = VRMaterials.Screen(gameScreen);
        ScreenCollider = screen.GetComponent<Collider>();
        _screen = screen.transform;

        GameObject frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
        frame.name = "ScreenFrame";
        SetupProp(frame, _screen, new Color(0.03f, 0.03f, 0.04f), keepCollider: false);
        frame.transform.localPosition = new Vector3(0f, 0f, 0.012f);
        frame.transform.localScale = new Vector3(1.03f, 1.05f, 0.02f);

        ApplyTableLayout();
    }

    void ApplyTableLayout()
    {
        SetOverviewRoom(false); // the duel layout turns it back on when needed

        _tableRoot.localPosition = new Vector3(0f, _tableHeight, 0f);

        Transform pedestal = _tableRoot.Find("Pedestal");
        pedestal.localPosition = new Vector3(0f, -_tableHeight * 0.5f, 0f);
        pedestal.localScale = new Vector3(0.35f, _tableHeight, 0.35f);

        bool duel = DuelMode;
        pedestal.gameObject.SetActive(!duel && !MenuLayout);
        _tableRoot.Find("TableTop").gameObject.SetActive(!duel && !MenuLayout);

        if (MenuLayout)
        {
            ApplyMenuLayout();

            if (_arenaPreview)
            {
                PlaceArena();
            }

            return;
        }

        if (duel)
        {
            ApplyDuelLayout();
            return;
        }

        if (_screen.parent != _tableRoot)
        {
            _screen.SetParent(_tableRoot, false);
        }

        if (BoardMode)
        {
            // Upright monitor behind the opponent's side, tilted back a little.
            float monitorHeight = MonitorWidthMeters * VRGameScreen.Height / VRGameScreen.Width;
            _screen.localScale = new Vector3(MonitorWidthMeters, monitorHeight, 1f);
            _screen.localRotation = Quaternion.Euler(10f, 0f, 0f);
            _screen.localPosition = new Vector3(0f, 0.12f + monitorHeight * 0.5f, TableDepth * 0.5f + 0.15f);
            return;
        }

        _screen.localScale = new Vector3(ScreenWidthMeters, ScreenWidthMeters * VRGameScreen.Height / VRGameScreen.Width, 1f);

        // Quad faces -Z; rotating 90 deg on X lays it face-up with its top edge away from the player.
        float tilt = TiltPresets[_mode - 2];
        float halfHeight = _screen.localScale.y * 0.5f;
        _screen.localRotation = Quaternion.Euler(90f - tilt, 0f, 0f);
        // Keep the near (bottom) edge resting just above the table, a little in from the edge.
        Vector3 bottomEdgeOffset = _screen.localRotation * new Vector3(0f, -halfHeight, 0f);
        // More upright screens move back so they stay at a comfortable reading distance.
        float pushBack = Mathf.InverseLerp(TiltPresets[0], TiltPresets[TiltPresets.Length - 1], tilt) * 0.45f;
        Vector3 bottomEdge = new Vector3(0f, 0.015f, -0.62f + pushBack);
        _screen.localPosition = bottomEdge - bottomEdgeOffset;
    }

    // Duel Disk view: the field stands up in front of you as a tilted hologram;
    // the flat game screen (effect prompts) floats to your right, facing you.
    void ApplyDuelLayout()
    {
        float centerY = _eyeHeight - HoloBelowEye + _holoHeightOffset;
        Vector3 seat = new Vector3(SeatPosition.x, centerY, SeatPosition.z);
        Quaternion yaw = Quaternion.Euler(0f, _holoUserYaw, 0f);
        HoloAnchor.localPosition = seat + yaw * new Vector3(0f, 0f, HoloDistance) + _holoUserOffset;
        HoloAnchor.localRotation = yaw * Quaternion.Euler(-HoloTilt, 0f, 0f);
        HoloAnchor.localScale = Vector3.one * HoloScale * _holoUserScale;

        PlaceArena();

        if (_screen.parent != transform)
        {
            _screen.SetParent(transform, false);
        }

        float height = SideScreenWidth * VRGameScreen.Height / VRGameScreen.Width;
        Vector3 eye = new Vector3(SeatPosition.x, _eyeHeight, SeatPosition.z);
        Vector3 position = new Vector3(SeatPosition.x + 1.45f, _eyeHeight + 0.1f + _holoHeightOffset, SeatPosition.z + 1.05f);
        _screen.localScale = new Vector3(SideScreenWidth, height, 1f);
        _screen.localPosition = position;
        _screen.localRotation = Quaternion.LookRotation(position - eye, Vector3.up);
    }

    bool _arenaPreview;

    /// <summary>Model studio: puts the arena exactly where a duel (Duel Disk view) has it, also in menus.</summary>
    public void SetArenaPreview(bool on)
    {
        _arenaPreview = on;
        ApplyTableLayout();
    }

    // The arena stage follows the desk (moving / turning the desk moves the stage too).
    void PlaceArena()
    {
        float centerY = _eyeHeight - HoloBelowEye + _holoHeightOffset;
        Vector3 seat = new Vector3(SeatPosition.x, centerY, SeatPosition.z);
        Quaternion yaw = Quaternion.Euler(0f, _holoUserYaw, 0f);
        float extra = VRSettings.ArenaDistance - 2.4f;
        Vector3 stage = seat + yaw * new Vector3(0f, 0f, HoloDistance + StageBehindDesk + extra) + _holoUserOffset;
        stage.y = _eyeHeight - StageBelowEye + _holoHeightOffset;

        // Overview (default): you look down on the whole arena from above, like a
        // commander's balcony; the room floor is hidden and a small platform holds you.
        bool overview = VRSettings.ArenaOverview;

        if (overview)
        {
            stage += yaw * new Vector3(0f, 0f, -OverviewBack);
            stage.y = _eyeHeight - OverviewDrop + _holoHeightOffset;
        }

        ArenaAnchor.localPosition = stage;
        ArenaAnchor.localRotation = yaw;
        SetOverviewRoom(overview);
    }

    // Menus: a large screen straight ahead, slightly below the eyes and tilted
    // to face them. B/Y + stick up/down moves it up/down, B/Y + stick left/right
    // pushes it nearer/further.
    void ApplyMenuLayout()
    {
        if (_screen.parent != transform)
        {
            _screen.SetParent(transform, false);
        }

        float height = MenuScreenWidth * VRGameScreen.Height / VRGameScreen.Width;
        Vector3 eye = new Vector3(SeatPosition.x, _eyeHeight, SeatPosition.z);
        float distance = Mathf.Clamp(MenuScreenDistance + _menuDistanceOffset, 0.9f, 3.5f);
        Vector3 position = eye + new Vector3(0f, -MenuScreenBelowEye + _menuHeightOffset, distance);
        _screen.localScale = new Vector3(MenuScreenWidth, height, 1f);
        _screen.localPosition = position;
        _screen.localRotation = Quaternion.LookRotation(position - eye, Vector3.up);

        Vector3 menu = eye + new Vector3(0f, -0.14f + _menuHeightOffset, Mathf.Clamp(1.15f + _menuDistanceOffset * 0.5f, 0.7f, 2.2f));
        MenuAnchor.localPosition = menu;
        MenuAnchor.localRotation = Quaternion.LookRotation(menu - eye, Vector3.up);
    }

    // ---------------------------------------------------------------- grab the world (hologram placement)

    public void BeginHoloGrab()
    {
        _grabStartOffset = _holoUserOffset;
        _grabStartYaw = _holoUserYaw;
        _grabStartScale = _holoUserScale;
    }

    /// <summary>Applies a two-hand grab: move (world space), turn (degrees around up) and resize (ratio) since the grab began.</summary>
    public void UpdateHoloGrab(Vector3 worldMove, float yawDelta, float scaleRatio)
    {
        _holoUserOffset = _grabStartOffset + transform.InverseTransformVector(worldMove);
        _holoUserYaw = _grabStartYaw + yawDelta;
        _holoUserScale = Mathf.Clamp(_grabStartScale * scaleRatio, 0.4f, 2.5f);
        ApplyTableLayout();
    }

    public void EndHoloGrab() => SaveHoloPlacement();

    public void ResetHoloPlacement()
    {
        _holoUserOffset = Vector3.zero;
        _holoUserYaw = 0f;
        _holoUserScale = 1f;
        _holoHeightOffset = 0f;
        SaveHoloPlacement();
        ApplyTableLayout();
    }

    void SaveHoloPlacement()
    {
        try
        {
            PlayerPrefs.SetString(HoloPrefKey, string.Join(";",
                _holoUserOffset.x.ToString(System.Globalization.CultureInfo.InvariantCulture),
                _holoUserOffset.y.ToString(System.Globalization.CultureInfo.InvariantCulture),
                _holoUserOffset.z.ToString(System.Globalization.CultureInfo.InvariantCulture),
                _holoUserYaw.ToString(System.Globalization.CultureInfo.InvariantCulture),
                _holoUserScale.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        catch
        {
            // Not critical.
        }
    }

    void LoadHoloPlacement()
    {
        try
        {
            string[] parts = PlayerPrefs.GetString(HoloPrefKey, "").Split(';');

            if (parts.Length == 5)
            {
                System.Globalization.CultureInfo c = System.Globalization.CultureInfo.InvariantCulture;
                _holoUserOffset = new Vector3(float.Parse(parts[0], c), float.Parse(parts[1], c), float.Parse(parts[2], c));
                _holoUserYaw = float.Parse(parts[3], c);
                _holoUserScale = Mathf.Clamp(float.Parse(parts[4], c), 0.4f, 2.5f);
            }
        }
        catch
        {
            // Bad or missing preference: default placement.
        }
    }

    static void SetupProp(GameObject go, Transform parent, Color color, bool keepCollider)
    {
        go.layer = Layer;
        go.transform.SetParent(parent, false);

        if (!keepCollider && go.TryGetComponent(out Collider col))
        {
            Destroy(col);
        }

        go.GetComponent<Renderer>().sharedMaterial = VRMaterials.Unlit(color);
    }

    static Transform NewChild(string name, Transform parent)
    {
        GameObject go = new GameObject(name) { layer = Layer };
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static void AddPoseDriver(Transform target, string positionPath, string rotationPath)
    {
        InputAction position = new InputAction(name: target.name + "Position", type: InputActionType.Value, binding: positionPath, expectedControlType: "Vector3");
        InputAction rotation = new InputAction(name: target.name + "Rotation", type: InputActionType.Value, binding: rotationPath, expectedControlType: "Quaternion");
        position.Enable();
        rotation.Enable();

        UnityEngine.InputSystem.XR.TrackedPoseDriver driver = target.gameObject.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
        driver.trackingType = UnityEngine.InputSystem.XR.TrackedPoseDriver.TrackingType.RotationAndPosition;
        driver.updateType = UnityEngine.InputSystem.XR.TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        driver.positionInput = new InputActionProperty(position);
        driver.rotationInput = new InputActionProperty(rotation);
    }

    // The headset can change its origin after start (guardian ready, Quest "reset view"...):
    // re-check the height for a few seconds whenever it does.
    void WatchTrackingOrigin()
    {
        _heightCheckUntil = Time.unscaledTime + 10f;
        List<XRInputSubsystem> subsystems = new List<XRInputSubsystem>();
        SubsystemManager.GetInstances(subsystems);

        foreach (XRInputSubsystem subsystem in subsystems)
        {
            subsystem.trackingOriginUpdated += _ => _heightCheckUntil = Time.unscaledTime + 5f;
        }
    }

    // Floor origin: the head is reported at standing / seated height (> 0.45 m) and needs no offset.
    // Device origin (or floor not ready yet): the head is near 0, so lift the tracking space to a
    // seated eye height. Returns true when the offset changed.
    bool FixTrackingHeight()
    {
        float headY = Camera.transform.localPosition.y;
        bool deviceOrigin = headY < 0.45f;
        float wantY = deviceOrigin ? _tableHeight + SeatedEyeHeightAboveTable : 0f;

        if (Mathf.Abs(TrackingSpace.localPosition.y - wantY) < 0.01f)
        {
            return false;
        }

        Vector3 position = TrackingSpace.localPosition;
        position.y = wantY;
        TrackingSpace.localPosition = position;
        _floorTracking = !deviceOrigin;
        Debug.Log($"[DCGO VR] Tracking height fixed: head {headY:F2} m in tracking space -> {(deviceOrigin ? "device origin, seated offset" : "floor origin")}");
        return true;
    }

    static bool TrySetFloorTracking()
    {
        List<XRInputSubsystem> subsystems = new List<XRInputSubsystem>();
        SubsystemManager.GetInstances(subsystems);
        bool floor = false;

        foreach (XRInputSubsystem subsystem in subsystems)
        {
            if (subsystem.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor))
            {
                floor = true;
            }
        }

        return floor;
    }

    // ---------------------------------------------------------------- runtime

    public HandInput Hand(XRNode node) => _hands[node];

    public Transform HandTransform(XRNode node) => node == XRNode.LeftHand ? LeftHand : RightHand;

    public Ray PointerRay(XRNode node)
    {
        Transform hand = HandTransform(node);
        return new Ray(hand.position, hand.forward);
    }

    void Update()
    {
        // Recenter automatically once the headset reports a pose.
        if (!_recenteredOnce)
        {
            if (Camera.transform.localPosition != Vector3.zero || Camera.transform.localRotation != Quaternion.identity)
            {
                _validHeadFrames++;
            }

            if (_validHeadFrames > 10)
            {
                FixTrackingHeight();
                Recenter();
                _recenteredOnce = true;
            }
        }
        else if (Time.unscaledTime < _heightCheckUntil && FixTrackingHeight())
        {
            Recenter(); // re-measure the eye height and re-place the hologram / arena
        }

        foreach (KeyValuePair<XRNode, HandInput> pair in _hands)
        {
            pair.Value.Tick();
            HandleComfortInput(pair.Value, pair.Key);
        }
    }

    void HandleComfortInput(HandInput hand, XRNode node)
    {
        if (hand.SecondaryHeld && MenuLayout && Mathf.Abs(hand.Stick.x) > 0.25f && Mathf.Abs(hand.Stick.x) > Mathf.Abs(hand.Stick.y))
        {
            hand.SecondaryUsedForAdjust = true;
            _menuDistanceOffset = Mathf.Clamp(_menuDistanceOffset + hand.Stick.x * 0.6f * Time.unscaledDeltaTime, -0.85f, 1.75f);
            ApplyTableLayout();
        }
        else if (hand.SecondaryHeld)
        {
            float y = hand.Stick.y;

            if (Mathf.Abs(y) > 0.25f)
            {
                hand.SecondaryUsedForAdjust = true;

                if (MenuLayout)
                {
                    _menuHeightOffset = Mathf.Clamp(_menuHeightOffset + y * 0.5f * Time.unscaledDeltaTime, -0.8f, 0.8f);
                }
                else if (DuelMode)
                {
                    _holoHeightOffset = Mathf.Clamp(_holoHeightOffset + y * 0.5f * Time.unscaledDeltaTime, -0.8f, 0.8f);
                }
                else
                {
                    _tableHeight = Mathf.Clamp(_tableHeight + y * 0.5f * Time.unscaledDeltaTime, 0.35f, 1.3f);
                }

                ApplyTableLayout();
            }
        }

        if (hand.SecondaryHeld)
        {
            hand.SecondaryHeldTime += Time.unscaledDeltaTime;

            if (DuelMode && !hand.SecondaryUsedForAdjust && hand.SecondaryHeldTime > 1.5f)
            {
                hand.SecondaryUsedForAdjust = true;
                ResetHoloPlacement();
                VRLaserPointer.Haptic(node, 0.6f, 0.1f);
                Debug.Log("[DCGO VR] Hologram placement reset");
            }
        }

        if (hand.SecondaryReleasedThisFrame)
        {
            if (!hand.SecondaryUsedForAdjust)
            {
                Recenter();
            }

            hand.SecondaryUsedForAdjust = false;
            hand.SecondaryHeldTime = 0f;
        }

    }

    /// <summary>Puts the player's head back in the seat, facing the table.</summary>
    public void Recenter()
    {
        Transform head = Camera.transform;
        float yaw = head.localRotation.eulerAngles.y;
        Quaternion counterYaw = Quaternion.Euler(0f, -yaw, 0f);

        Vector3 headFlat = head.localPosition;
        headFlat.y = 0f;

        TrackingSpace.localRotation = counterYaw;
        Vector3 offset = SeatPosition - counterYaw * headFlat;
        offset.y = TrackingSpace.localPosition.y;
        TrackingSpace.localPosition = offset;

        // Remember the eye height (seated or standing) so the hologram sits just below it.
        float eye = transform.InverseTransformPoint(head.position).y;

        if (eye > 0.5f && eye < 2.3f)
        {
            _eyeHeight = eye;
        }

        ApplyTableLayout();
    }

    // ---------------------------------------------------------------- input

    /// <summary>Button and stick state for one controller, read through the Input System.</summary>
    public class HandInput
    {
        readonly InputAction _trigger;
        readonly InputAction _primary;
        readonly InputAction _secondary;
        readonly InputAction _stick;
        readonly InputAction _stickClick;
        readonly InputAction _grip;
        readonly InputAction _menu;
        bool _menuPrev;
        bool _primaryPrev;

        bool _secondaryPrev;
        bool _stickClickPrev;
        int _lastFrame = -1;

        public bool SecondaryUsedForAdjust { get; set; }
        public float SecondaryHeldTime { get; set; }
        public bool SecondaryHeld { get; private set; }
        public bool SecondaryReleasedThisFrame { get; private set; }
        public bool StickClickPressedThisFrame { get; private set; }
        public bool MenuPressedThisFrame { get; private set; }
        public bool PrimaryPressedThisFrame { get; private set; }

        public HandInput(string handUsage)
        {
            string device = "<XRController>" + handUsage;
            _trigger = Button(device + "/triggerPressed");
            _primary = Button(device + "/primaryButton");
            _secondary = Button(device + "/secondaryButton");
            _stickClick = Button(device + "/thumbstickClicked");
            _grip = Button(device + "/gripPressed");
            _menu = Button(device + "/menu");
            _stick = new InputAction(type: InputActionType.Value, binding: device + "/thumbstick", expectedControlType: "Vector2");
            _stick.Enable();
        }

        static InputAction Button(string path)
        {
            InputAction action = new InputAction(type: InputActionType.Button, binding: path);
            action.Enable();
            return action;
        }

        public bool Trigger => _trigger.IsPressed();
        public bool Primary => _primary.IsPressed();
        public bool Grip => _grip.IsPressed();
        public Vector2 Stick => _stick.ReadValue<Vector2>();

        // Edge states are computed once per frame so every reader sees the same values.
        public void Tick()
        {
            if (_lastFrame == Time.frameCount)
            {
                return;
            }

            _lastFrame = Time.frameCount;

            bool secondary = _secondary.IsPressed();
            SecondaryHeld = secondary;
            SecondaryReleasedThisFrame = _secondaryPrev && !secondary;
            _secondaryPrev = secondary;

            bool primary = _primary.IsPressed();
            PrimaryPressedThisFrame = primary && !_primaryPrev;
            _primaryPrev = primary;

            bool menu = _menu.IsPressed();
            MenuPressedThisFrame = menu && !_menuPrev;
            _menuPrev = menu;

            bool stickClick = _stickClick.IsPressed();
            StickClickPressedThisFrame = stickClick && !_stickClickPrev;
            _stickClickPrev = stickClick;
        }
    }

    void LateUpdate()
    {
        // Ensure Tick runs every frame even if no other script polled the hand.
        foreach (HandInput hand in _hands.Values)
        {
            hand.Tick();
        }
    }
}
#endif
