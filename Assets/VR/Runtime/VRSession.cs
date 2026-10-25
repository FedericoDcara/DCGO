#if DCGO_VR
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR.Management;

// === DCGO-CUSTOM:vr ===
// Phase 0 entry point. Starts OpenXR manually (so the same build still runs flat
// when no headset is connected), then builds the VR room:
//   VRGameScreen   - renders the existing flat game into a texture
//   VRRig          - headset camera, controllers, table and the screen surface
//   VRLaserPointer - turns the controller laser into mouse input for the game UI
// Nothing here touches game rules or networking: the game itself still thinks
// it is being played with a mouse, so cross-play with flat clients is unchanged.
[DefaultExecutionOrder(-30000)]
public class VRSession : MonoBehaviour
{
    public static VRSession Instance { get; private set; }

    /// <summary>True once a headset is running and the VR room is built.</summary>
    public static bool IsActive => Instance != null && Instance._active;

    public VRRig Rig { get; private set; }
    public VRGameScreen GameScreen { get; private set; }
    public VRLaserPointer Pointer { get; private set; }
    public Board3D Board { get; private set; }
    public VRDuelDisk DuelDisk { get; private set; }
    public VRPromptPanel Prompt { get; private set; }
    public VRSettingsPanel Settings { get; private set; }

    void Update()
    {
        if (!_active || Rig == null)
        {
            return;
        }

        // Optionally hide the side screen during a duel (everything is in VR); always show it in menus.
        bool inDuel = BoardObserver.Context != null && BoardObserver.Context.You != null;
        Rig.SetScreenVisible(!(VRSettings.HideScreenInDuel && inDuel && Rig.DuelMode));
    }

    // Moves the 3D board between the hologram (Duel Disk view) and the table.
    void ApplyView()
    {
        Board.SetVisible(Rig.BoardMode);
        Board.transform.SetParent(Rig.DuelMode ? Rig.HoloAnchor : Rig.TableTop, false);
        Board.transform.localPosition = Vector3.zero;
        Board.transform.localRotation = Quaternion.identity;
        Board.transform.localScale = Vector3.one;
        Board.SetDuelStyle(Rig.DuelMode);
        DuelDisk.gameObject.SetActive(Rig.DuelMode);
    }
    public VRGrabber Grabber { get; private set; }

    bool _active;
    bool _weStartedXR;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null)
        {
            return;
        }

        // "-novr" on the command line forces the flat game even in a VR build.
        foreach (string arg in Environment.GetCommandLineArgs())
        {
            if (string.Equals(arg, "-novr", StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log("[DCGO VR] -novr passed, staying in flat mode.");
                return;
            }
        }

        GameObject go = new GameObject("DCGO_VR");
        DontDestroyOnLoad(go);
        go.AddComponent<VRSession>();
    }

    void Awake()
    {
        Instance = this;
    }

    IEnumerator Start()
    {
        XRGeneralSettings generalSettings = XRGeneralSettings.Instance;

        if (generalSettings == null || generalSettings.Manager == null)
        {
            Debug.LogWarning("[DCGO VR] XR Plug-in Management is not configured. Run DCGO > VR > Setup VR in the editor.");
            Destroy(gameObject);
            yield break;
        }

        XRManagerSettings manager = generalSettings.Manager;

        if (manager.activeLoader == null)
        {
            yield return manager.InitializeLoader();

            if (manager.activeLoader == null)
            {
                Debug.LogWarning("[DCGO VR] No headset / OpenXR runtime found. Running in flat mode.");
                Destroy(gameObject);
                yield break;
            }

            manager.StartSubsystems();
            _weStartedXR = true;
        }

        // Give the display subsystem a frame to come up before we build cameras.
        yield return null;

        GameScreen = gameObject.AddComponent<VRGameScreen>();
        Rig = VRRig.Create(transform, GameScreen.Texture);
        GameScreen.Init(Rig.Camera);

        Pointer = gameObject.AddComponent<VRLaserPointer>();
        Pointer.Init(Rig, GameScreen);
        PointerInput.Provider = Pointer;

        Board = Board3D.Create(Rig.TableTop);
        Board.HoldingHand = Rig.LeftHand;
        Board.Head = Rig.Camera.transform;
        DuelDisk = VRDuelDisk.Create(Rig.LeftHand, Board);
        Board.Disk = DuelDisk;
        DuelDisk.PushHand = Rig.RightHand;
        Prompt = VRPromptPanel.Create(Board);
        Board.HandHeld = !VRSettings.HandFloating;
        Settings = VRSettingsPanel.Create(transform, Rig, Board);
        Rig.ViewChanged += ApplyView;
        ApplyView();

        Grabber = gameObject.AddComponent<VRGrabber>();
        Grabber.Init(Rig, Pointer, Board);

        _active = true;
        Debug.Log("[DCGO VR] VR session started with loader: " + manager.activeLoader.name);
        StartCoroutine(LogControllers());
    }

    // Development aid: lists every controller and its button names once they connect,
    // so input bindings can be checked against what the runtime really reports.
    IEnumerator LogControllers()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        yield return new WaitForSecondsRealtime(5f);

        foreach (UnityEngine.InputSystem.InputDevice device in UnityEngine.InputSystem.InputSystem.devices)
        {
            if (!(device is UnityEngine.InputSystem.XR.XRController))
            {
                continue;
            }

            System.Text.StringBuilder controls = new System.Text.StringBuilder();

            foreach (UnityEngine.InputSystem.InputControl control in device.allControls)
            {
                if (control is UnityEngine.InputSystem.Controls.ButtonControl)
                {
                    controls.Append(control.name).Append(' ');
                }
            }

            Debug.Log($"[DCGO VR] Controller: {device.layout} usages [{string.Join(",", device.usages)}] buttons: {controls}");
        }
#else
        yield break;
#endif
    }

    void OnApplicationQuit()
    {
        StopXR();
    }

    void OnDestroy()
    {
        if (Pointer != null && ReferenceEquals(PointerInput.Provider, Pointer))
        {
            PointerInput.Provider = null;
        }

        StopXR();

        if (Instance == this)
        {
            Instance = null;
        }
    }

    void StopXR()
    {
        if (!_weStartedXR)
        {
            return;
        }

        _weStartedXR = false;
        XRGeneralSettings generalSettings = XRGeneralSettings.Instance;

        if (generalSettings != null && generalSettings.Manager != null && generalSettings.Manager.activeLoader != null)
        {
            generalSettings.Manager.StopSubsystems();
            generalSettings.Manager.DeinitializeLoader();
        }
    }
}
#endif
