#if DCGO_VR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// === DCGO-CUSTOM:vr ===
// Redirects the flat game's rendering into a RenderTexture that VRRig shows on
// the table. Scans every scene (and periodically, for objects spawned later):
//  - base game cameras render into the texture instead of the window
//  - Screen Space Overlay canvases become Screen Space Camera canvases on the
//    game camera (overlay canvases cannot render into a texture)
// The game keeps working in "screen pixels"; those pixels are now texels.
public class VRGameScreen : MonoBehaviour
{
    public const int Width = 1920;
    public const int Height = 1080;

    /// <summary>Canvases converted from overlay get pushed above all existing camera canvases.</summary>
    const int OverlaySortingBoost = 1000;

    const float ScanInterval = 2f;

    public RenderTexture Texture { get; private set; }

    Camera _xrCamera;
    float _nextScan;
    bool _paused;
    readonly List<Camera> _pausedCameras = new List<Camera>();

    /// <summary>
    /// Stops drawing the flat game into the texture while nobody can see it (VR menu
    /// page shown, side screen hidden in a duel). The game itself keeps running:
    /// only its cameras stop rendering, UI logic and input are untouched.
    /// </summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            if (_paused == value)
            {
                return;
            }

            _paused = value;

            if (_paused)
            {
                PauseCameras();
            }
            else
            {
                foreach (Camera cam in _pausedCameras)
                {
                    if (cam != null)
                    {
                        cam.enabled = true;
                    }
                }

                _pausedCameras.Clear();
                Scan();
            }

            Debug.Log("[DCGO VR] Game screen rendering " + (_paused ? "paused (not visible)" : "resumed"));
        }
    }

    void PauseCameras()
    {
        foreach (Camera cam in Camera.allCameras)
        {
            if (cam.targetTexture == Texture && cam != _xrCamera)
            {
                cam.enabled = false;
                _pausedCameras.Add(cam);
            }
        }
    }
    readonly HashSet<int> _boostedCanvases = new HashSet<int>();

    void Awake()
    {
        RenderTextureDescriptor desc = new RenderTextureDescriptor(Width, Height, RenderTextureFormat.ARGB32, 24)
        {
            msaaSamples = 2,
            useMipMap = true,
            autoGenerateMips = true,
            sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear,
        };

        Texture = new RenderTexture(desc)
        {
            name = "DCGO_VR_GameScreen",
            filterMode = FilterMode.Trilinear,
            anisoLevel = 8,
            wrapMode = TextureWrapMode.Clamp,
        };
        Texture.Create();
    }

    public void Init(Camera xrCamera)
    {
        _xrCamera = xrCamera;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Scan();
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;

        if (Texture != null)
        {
            Texture.Release();
        }
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Scan();
    }

    void LateUpdate()
    {
        if (Time.unscaledTime >= _nextScan)
        {
            _nextScan = Time.unscaledTime + ScanInterval;
            Scan();
        }
    }

    public void Scan()
    {
        if (_xrCamera == null)
        {
            return;
        }

        foreach (Camera cam in Camera.allCameras)
        {
            RetargetCamera(cam);
        }

        Camera uiCamera = FindUICamera();

        if (_paused)
        {
            // New cameras (scene loads) still get their canvases converted, then pause too.
            ConvertCanvases(uiCamera);
            PauseCameras();
            return;
        }

        ConvertCanvases(uiCamera);
    }

    void ConvertCanvases(Camera uiCamera)
    {

        if (uiCamera == null)
        {
            return;
        }

        foreach (Canvas canvas in FindObjectsOfType<Canvas>())
        {
            if (!canvas.isRootCanvas || canvas.gameObject.layer == VRRig.Layer)
            {
                continue;
            }

            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = uiCamera;
                canvas.planeDistance = uiCamera.nearClipPlane + 0.01f;

                if (_boostedCanvases.Add(canvas.GetInstanceID()))
                {
                    canvas.sortingOrder += OverlaySortingBoost;
                }
            }
            else if (canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == null)
            {
                // A camera canvas without a camera behaves like an overlay.
                canvas.worldCamera = uiCamera;
            }
        }
    }

    void RetargetCamera(Camera cam)
    {
        if (cam == _xrCamera || cam.gameObject.layer == VRRig.Layer)
        {
            return;
        }

        // URP overlay cameras draw into their base camera's target.
        if (cam.TryGetComponent(out UniversalAdditionalCameraData data) && data.renderType == CameraRenderType.Overlay)
        {
            return;
        }

        // Cameras that already render into their own texture (card renders etc.) are left alone.
        if (cam.targetTexture != null && cam.targetTexture != Texture)
        {
            return;
        }

        if (cam.targetTexture != Texture)
        {
            cam.targetTexture = Texture;
        }

        cam.stereoTargetEye = StereoTargetEyeMask.None;
        cam.cullingMask &= ~(1 << VRRig.Layer);
    }

    /// <summary>The camera the game itself uses for UI maths, so converted canvases stay consistent with Draggable etc.</summary>
    Camera FindUICamera()
    {
        if (GManager.instance != null && GManager.instance.camara != null && GManager.instance.camara.isActiveAndEnabled)
        {
            return GManager.instance.camara;
        }

        if (Opening.instance != null && Opening.instance.MainCamera != null && Opening.instance.MainCamera.isActiveAndEnabled)
        {
            return Opening.instance.MainCamera;
        }

        Camera best = null;

        foreach (Camera cam in Camera.allCameras)
        {
            if (cam.targetTexture == Texture && (best == null || cam.depth > best.depth))
            {
                best = cam;
            }
        }

        return best;
    }
}
#endif
