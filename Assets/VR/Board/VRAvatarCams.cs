#if DCGO_VR
using System.Collections.Generic;
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// "Webcams" beside the memory gauge at the top of your view: a square on each side
// showing a live close-up of each player's 3D character. The opponent's is the giant
// avatar behind their field (her animations show live); yours is a character model you
// pick in VR settings ("My avatar"), standing off-stage where only its camera sees it.
// Yours on the left (blue frame), the opponent on the right (red frame).
public class VRAvatarCams : MonoBehaviour
{
    const int Resolution = 256;
    const float Size = 0.072f;              // square size on the HUD
    const float MyHeight = 1.62f;
    static readonly Vector3 Backstage = new Vector3(0f, -400f, 0f);   // far from everything

    sealed class Cam
    {
        public Camera Camera;
        public RenderTexture Texture;
        public Transform Portrait;
        public VRLabel Name;
    }

    Cam _theirs;
    Cam _yours;
    Transform _hudParent;

    // My avatar.
    Transform _myRoot;
    Transform _myModel;
    string _myPath;
    string _myKey;
    bool _myLoading;
    Animation _myAnim;
    int _myToken;

    public static VRAvatarCams Create(Transform parent)
    {
        GameObject go = new GameObject("AvatarCams") { layer = VRRig.Layer };
        go.transform.SetParent(parent, false);
        return go.AddComponent<VRAvatarCams>();
    }

    /// <summary>Your avatar choices: the character ("npc") models.</summary>
    public static List<VRModelLibrary.ModelInfo> Choices() => VRModelLibrary.All.FindAll(m => m.Category == "npc");

    /// <summary>Reload your avatar (after choosing another one).</summary>
    public void Reload() => _myKey = null;

    Cam Build(bool yours)
    {
        Cam cam = new Cam();
        cam.Texture = new RenderTexture(Resolution, Resolution, 16, RenderTextureFormat.ARGB32) { name = yours ? "MyCam" : "TheirCam" };
        cam.Texture.Create();

        GameObject camGo = new GameObject(yours ? "MyWebcam" : "TheirWebcam");
        camGo.transform.SetParent(transform, false);
        cam.Camera = camGo.AddComponent<Camera>();
        cam.Camera.targetTexture = cam.Texture;
        VRPerformance.ConfigureCamera(cam.Camera);
        cam.Camera.stereoTargetEye = StereoTargetEyeMask.None;
        cam.Camera.cullingMask = 1 << VRRig.Layer;
        cam.Camera.clearFlags = CameraClearFlags.SolidColor;
        cam.Camera.backgroundColor = yours ? new Color(0.04f, 0.08f, 0.14f) : new Color(0.12f, 0.04f, 0.05f);
        cam.Camera.fieldOfView = 28f;
        cam.Camera.nearClipPlane = 0.05f;
        cam.Camera.farClipPlane = 30f;
        cam.Camera.enabled = false;

        // The square on the HUD: frame + picture + name strip.
        Transform portrait = new GameObject(yours ? "MyPortrait" : "TheirPortrait") { layer = VRRig.Layer }.transform;
        portrait.SetParent(_hudParent, false);
        float x = (0.252f + 0.016f + Size * 0.5f) * (yours ? -1f : 1f); // you on the left, the opponent on the right
        portrait.localPosition = new Vector3(x, -0.022f, 0f);
        Quad(portrait, new Vector3(0f, 0f, 0.001f), Size + 0.006f, yours ? new Color(0.25f, 0.8f, 1f) : new Color(1f, 0.35f, 0.3f), null);
        Quad(portrait, Vector3.zero, Size, Color.white, cam.Texture);
        cam.Name = VRLabel.Create(portrait, new Vector3(0f, -Size * 0.5f - 0.008f, -0.001f), Quaternion.identity, new Vector2(Size + 0.006f, 0.012f), yours ? "YOU" : "OPPONENT", Color.white, yours ? new Color(0.06f, 0.2f, 0.36f) : new Color(0.36f, 0.08f, 0.08f));
        cam.Portrait = portrait;
        return cam;
    }

    static void Quad(Transform parent, Vector3 position, float size, Color color, Texture texture)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.layer = VRRig.Layer;
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(parent, false);
        quad.transform.localPosition = position;
        quad.transform.localScale = new Vector3(size, size, 1f);
        quad.GetComponent<Renderer>().sharedMaterial = texture != null ? VRMaterials.Screen(texture) : VRMaterials.Unlit(color);
    }

    void LateUpdate()
    {
        VRMemoryHud hud = VRMemoryHud.Instance;
        bool show = VRSettings.AvatarCams && hud != null && hud.Showing;

        if (show && _theirs == null)
        {
            _hudParent = hud.transform;
            _theirs = Build(false);
            _yours = Build(true);
        }

        if (_theirs == null)
        {
            return;
        }

        bool theirsLive = show && VROpponentAvatar.Model != null;
        Show(_theirs, show, theirsLive, 0);

        if (theirsLive)
        {
            Aim(_theirs.Camera, VROpponentAvatar.Model, VROpponentAvatar.Height);
        }

        // Your avatar: loaded off-stage, only while the webcam is shown.
        if (show && _myKey != VRSettings.MyAvatar && !_myLoading)
        {
            LoadMine();
        }

        if (_myRoot != null && _myRoot.gameObject.activeSelf != show)
        {
            _myRoot.gameObject.SetActive(show);
        }

        bool mineLive = show && _myModel != null;
        Show(_yours, show, mineLive, PortraitEveryFrames / 2);

        if (mineLive)
        {
            Aim(_yours.Camera, _myModel, MyHeight);
        }
    }

    // Portraits refresh about 15 times a second (not every frame): each refresh is a full extra
    // camera render. The two portraits take turns so they never render in the same frame.
    const int PortraitEveryFrames = 6;

    static void Show(Cam cam, bool portrait, bool live, int phase)
    {
        if (cam.Portrait.gameObject.activeSelf != portrait)
        {
            cam.Portrait.gameObject.SetActive(portrait);
        }

        bool render = live && Time.frameCount % PortraitEveryFrames == phase;

        if (cam.Camera.enabled != render)
        {
            cam.Camera.enabled = render;
        }
    }

    // Close-up from the front: follows the head bone, so turns and gestures show live.
    static void Aim(Camera camera, Transform model, float height)
    {
        Transform head = FindHead(model);
        Vector3 up = model.parent != null ? model.parent.up : Vector3.up;
        Vector3 forward = model.parent != null ? model.parent.forward : Vector3.forward;
        Vector3 face = head != null ? head.position + up * height * 0.02f : model.position + up * height * 0.88f;
        float distance = height * 0.3f;
        camera.transform.position = face + forward * distance + up * height * 0.01f;
        camera.transform.rotation = Quaternion.LookRotation(face - camera.transform.position, up);
        camera.nearClipPlane = Mathf.Max(0.02f, distance * 0.2f);
        camera.farClipPlane = distance * 4f;
    }

    static readonly Dictionary<Transform, Transform> _heads = new Dictionary<Transform, Transform>();

    static Transform FindHead(Transform model)
    {
        if (_heads.TryGetValue(model, out Transform head) && head != null)
        {
            return head;
        }

        head = null;

        foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
        {
            string name = t.name.ToLowerInvariant();

            if (name == "head" || name.EndsWith("_head") || name.EndsWith(" head") || name == "bip01_head" || name.EndsWith("head01"))
            {
                head = t;
                break;
            }
        }

        _heads[model] = head;
        return head;
    }

    async void LoadMine()
    {
        _myLoading = true;
        int token = ++_myToken;
        string key = VRSettings.MyAvatar;
        _myKey = key;

        if (_myModel != null)
        {
            VRFieldModels.ReleasePreview(_myPath, _myModel);
            _myModel = null;
            _myPath = null;
        }

        if (_myRoot == null)
        {
            _myRoot = new GameObject("MyAvatarBackstage") { layer = VRRig.Layer }.transform;
            _myRoot.SetParent(transform, false);
            _myRoot.position = Backstage;
        }

        VRModelLibrary.ModelInfo info = VRModelLibrary.All.Find(m => m.Key == key) ?? Choices().Find(m => true);
        string path = info != null ? info.Path : null;
        Transform holder = path != null ? await VRFieldModels.LoadPreview(path, _myRoot) : null;

        if (token != _myToken || this == null)
        {
            VRFieldModels.ReleasePreview(path, holder);
            return;
        }

        _myLoading = false;

        if (holder == null)
        {
            return;
        }

        _myPath = path;
        _myModel = holder;
        VRFieldModels.Normalize(holder, MyHeight * VRModelTuning.Get(info.Key), out _);
        _myAnim = holder.GetComponentInChildren<Animation>(true);

        if (_myAnim != null)
        {
            _myAnim.playAutomatically = false;
            _myAnim.cullingType = AnimationCullingType.AlwaysAnimate;

            foreach (AnimationState state in _myAnim)
            {
                if (state.name.Contains("_fn01") || state.name.Contains("_bn01"))
                {
                    state.wrapMode = WrapMode.Loop;
                    _myAnim.Play(state.name);
                    break;
                }
            }
        }

        if (_yours != null)
        {
            _yours.Name.text = "YOU - " + info.Name;
        }
    }

    void OnDestroy()
    {
        if (_myModel != null)
        {
            VRFieldModels.ReleasePreview(_myPath, _myModel);
        }

        foreach (Cam cam in new[] { _theirs, _yours })
        {
            if (cam != null && cam.Texture != null)
            {
                cam.Texture.Release();
            }
        }
    }
}
#endif
