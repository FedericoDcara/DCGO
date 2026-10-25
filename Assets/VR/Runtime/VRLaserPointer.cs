#if DCGO_VR
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR;

// === DCGO-CUSTOM:vr ===
// Laser pointer that impersonates the mouse.
//   Trigger          = left click / drag   (play cards, attack, buttons)
//   A / X            = right click         (card detail, same as the mouse)
//   Thumbstick up/dn = scroll wheel
//   Pull the other hand's trigger to switch which hand holds the laser.
// The pointer position is the texel the laser hits on the game screen, so all
// existing UI code (EventSystem, Draggable, drop areas, target arrows) works
// unchanged through PointerInput and the EventSystem's BaseInput override.
[DefaultExecutionOrder(-29000)]
public class VRLaserPointer : MonoBehaviour, PointerInput.IProvider
{
    const float MaxDistance = 6f;
    const float ScrollSpeed = 6f;
    const float SmoothingRate = 30f;

    VRRig _rig;
    VRGameScreen _screen;
    VRBaseInput _baseInput;

    XRNode _activeHand = XRNode.RightHand;
    bool _otherTriggerPrev;
    bool _suppressTriggerUntilRelease;

    LineRenderer _line;
    Transform _cursor;
    Material _lineMaterial;

    bool _onScreen;
    Vector2 _pixel;
    Vector2 _scroll;
    readonly bool[] _current = new bool[2];
    readonly bool[] _previous = new bool[2];
    int _lastTickFrame = -1;
    float _nextModuleScan;

    public XRNode ActiveHand => _activeHand;
    public Ray CurrentRay { get; private set; }
    public bool HasHit { get; private set; }
    public RaycastHit CurrentHit { get; private set; }

    /// <summary>Set while a 3D card is held so the laser can't also click the screen.</summary>
    public bool BlockScreenInput { get; set; }

    /// <summary>Hides the laser beam and cursor (e.g. while your hand is touching or holding a card).</summary>
    public bool Hidden { get; set; }

    bool _triggerRawPrevious;
    bool _primaryRawPrevious;

    public void Init(VRRig rig, VRGameScreen screen)
    {
        _rig = rig;
        _screen = screen;
        _baseInput = gameObject.AddComponent<VRBaseInput>();
        _pixel = new Vector2(VRGameScreen.Width * 0.5f, VRGameScreen.Height * 0.5f);
        BuildVisuals();
    }

    void BuildVisuals()
    {
        GameObject lineGo = new GameObject("Laser") { layer = VRRig.Layer };
        lineGo.transform.SetParent(transform, false);
        _line = lineGo.AddComponent<LineRenderer>();
        _line.positionCount = 2;
        _line.useWorldSpace = true;
        _line.startWidth = 0.004f;
        _line.endWidth = 0.002f;
        _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _line.receiveShadows = false;
        _lineMaterial = VRMaterials.Unlit(Color.white);
        _line.sharedMaterial = _lineMaterial;

        GameObject cursor = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        cursor.name = "LaserCursor";
        cursor.layer = VRRig.Layer;
        Destroy(cursor.GetComponent<Collider>());
        cursor.transform.SetParent(transform, false);
        cursor.transform.localScale = Vector3.one * 0.012f;
        cursor.GetComponent<Renderer>().sharedMaterial = VRMaterials.Unlit(new Color(1f, 0.85f, 0.2f));
        _cursor = cursor.transform;
    }

    void Update()
    {
        Tick();
        AttachToInputModules();
    }

    void Tick()
    {
        if (_lastTickFrame == Time.frameCount || _rig == null)
        {
            return;
        }

        _lastTickFrame = Time.frameCount;
        _previous[0] = _current[0];
        _previous[1] = _current[1];

        XRNode otherHand = _activeHand == XRNode.RightHand ? XRNode.LeftHand : XRNode.RightHand;
        VRRig.HandInput active = _rig.Hand(_activeHand);
        VRRig.HandInput other = _rig.Hand(otherHand);
        active.Tick();
        other.Tick();

        // Switch hands when the other trigger is pulled while nothing is held.
        bool otherTrigger = other.Trigger;

        if (otherTrigger && !_otherTriggerPrev && !_current[0] && !_current[1] && !BlockScreenInput)
        {
            _activeHand = otherHand;
            active = other;
            _suppressTriggerUntilRelease = true; // the switching pull must not also click
            Haptic(_activeHand, 0.2f, 0.03f);
        }

        _otherTriggerPrev = otherTrigger;

        // Raycast against the game screen.
        Ray ray = _rig.PointerRay(_activeHand);
        bool hitAny = Physics.Raycast(ray, out RaycastHit hit, MaxDistance, 1 << VRRig.Layer, QueryTriggerInteraction.Collide);
        bool hitScreen = hitAny && hit.collider == _rig.ScreenCollider;
        CurrentRay = ray;
        HasHit = hitAny;
        CurrentHit = hit;

        if (hitScreen)
        {
            Vector2 target = new Vector2(hit.textureCoord.x * VRGameScreen.Width, hit.textureCoord.y * VRGameScreen.Height);
            float t = _onScreen ? 1f - Mathf.Exp(-SmoothingRate * Time.unscaledDeltaTime) : 1f;
            _pixel = Vector2.Lerp(_pixel, target, t);
        }

        _onScreen = hitScreen;

        // Buttons only start a press while on the screen, but a press that is
        // already held keeps going (so drags can leave and re-enter the screen).
        if (!active.Trigger)
        {
            _suppressTriggerUntilRelease = false;
        }

        bool trigger = active.Trigger && !_suppressTriggerUntilRelease;
        bool primary = active.Primary;
        bool triggerDown = trigger && !_triggerRawPrevious;
        bool primaryDown = primary && !_primaryRawPrevious;
        _triggerRawPrevious = trigger;
        _primaryRawPrevious = primary;

        // A press only starts if it begins on the screen (not on a 3D card or button).
        _current[0] = trigger && (_current[0] || (triggerDown && _onScreen && !BlockScreenInput));
        _current[1] = primary && (_current[1] || (primaryDown && _onScreen && !BlockScreenInput));

        if ((_current[0] && !_previous[0]) || (_current[1] && !_previous[1]))
        {
            Haptic(_activeHand, 0.35f, 0.02f);
            LogClick(_current[0] ? 0 : 1);
        }

        float stickY = active.SecondaryHeld ? 0f : active.Stick.y;
        _scroll = Mathf.Abs(stickY) > 0.2f && _onScreen ? new Vector2(0f, stickY * ScrollSpeed) : Vector2.zero;

        UpdateVisuals(ray, hitAny, hitScreen, hit);
    }

    void UpdateVisuals(Ray ray, bool hitAny, bool hitScreen, RaycastHit hit)
    {
        Vector3 end = hitAny ? hit.point : ray.origin + ray.direction * 1.5f;
        _line.SetPosition(0, ray.origin);
        _line.SetPosition(1, end);

        Color color = BlockScreenInput ? new Color(1f, 0.85f, 0.2f)
            : !hitAny ? new Color(1f, 1f, 1f, 0.35f)
            : _current[0] ? new Color(0.3f, 1f, 0.5f)
            : new Color(0.4f, 0.8f, 1f);
        _lineMaterial.SetColor("_BaseColor", color);
        _lineMaterial.SetColor("_Color", color);

        _line.enabled = !Hidden;
        _cursor.gameObject.SetActive(hitAny && !Hidden);

        if (hitAny)
        {
            _cursor.position = hit.point + hit.normal * 0.004f;
        }
    }

    void AttachToInputModules()
    {
        if (Time.unscaledTime < _nextModuleScan)
        {
            return;
        }

        _nextModuleScan = Time.unscaledTime + 2f;

        foreach (StandaloneInputModule standalone in FindObjectsOfType<StandaloneInputModule>())
        {
            if (!(standalone is VRUIInputModule) && standalone.enabled)
            {
                VRUIInputModule.Replace(standalone);
                Debug.Log("[DCGO VR] Swapped StandaloneInputModule for VRUIInputModule on " + standalone.gameObject.name);
            }
        }

        foreach (BaseInputModule module in FindObjectsOfType<BaseInputModule>())
        {
            if (module.inputOverride != _baseInput)
            {
                module.inputOverride = _baseInput;
            }
        }
    }

    // Development aid: says what the UI raycast finds under the laser on each press.
    readonly System.Collections.Generic.List<RaycastResult> _hits = new System.Collections.Generic.List<RaycastResult>();

    void LogClick(int button)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        EventSystem eventSystem = EventSystem.current;
        string top = "(no EventSystem)";

        if (eventSystem != null)
        {
            _hits.Clear();
            eventSystem.RaycastAll(new PointerEventData(eventSystem) { position = _pixel }, _hits);
            top = _hits.Count > 0 ? $"{_hits[0].gameObject.name} (+{_hits.Count - 1} more)" : "nothing";
            top += $", module: {(eventSystem.currentInputModule != null ? eventSystem.currentInputModule.GetType().Name : "none")}, focused: {eventSystem.isFocused}";
        }

        Debug.Log($"[DCGO VR] {(button == 0 ? "Click" : "Right-click")} at ({_pixel.x:F0},{_pixel.y:F0}) on screen: {_onScreen}, UI hit: {top}");
#endif
    }

    public static void Haptic(XRNode node, float amplitude, float duration)
    {
        if (!VRSettings.Haptics)
        {
            return;
        }

        UnityEngine.XR.InputDevice device = InputDevices.GetDeviceAtXRNode(node);

        if (device.isValid)
        {
            device.SendHapticImpulse(0, amplitude, duration);
        }
    }

    // ---------------------------------------------------------------- PointerInput.IProvider

    public Vector3 MousePosition
    {
        get
        {
            Tick();
            return new Vector3(_pixel.x, _pixel.y, 0f);
        }
    }

    public bool GetMouseButton(int button)
    {
        Tick();
        return button >= 0 && button < 2 && _current[button];
    }

    public bool GetMouseButtonDown(int button)
    {
        Tick();
        return button >= 0 && button < 2 && _current[button] && !_previous[button];
    }

    public bool GetMouseButtonUp(int button)
    {
        Tick();
        return button >= 0 && button < 2 && !_current[button] && _previous[button];
    }

    public Vector2 MouseScrollDelta
    {
        get
        {
            Tick();
            return _scroll;
        }
    }

    public int ScreenWidth => VRGameScreen.Width;
    public int ScreenHeight => VRGameScreen.Height;
}

// Feeds the laser into Unity's EventSystem (hover, click, drag, scroll) in place of the mouse.
public class VRBaseInput : BaseInput
{
    public override bool mousePresent => true;
    public override Vector2 mousePosition => PointerInput.mousePosition;
    public override Vector2 mouseScrollDelta => PointerInput.mouseScrollDelta;
    public override bool GetMouseButton(int button) => PointerInput.GetMouseButton(button);
    public override bool GetMouseButtonDown(int button) => PointerInput.GetMouseButtonDown(button);
    public override bool GetMouseButtonUp(int button) => PointerInput.GetMouseButtonUp(button);
    public override bool touchSupported => false;
    public override int touchCount => 0;
}
#endif
