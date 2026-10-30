#if DCGO_VR
using System.Collections.Generic;
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// "Digitize" transition from the menu into a duel. When the battle starts loading, a dark
// sphere fades in around you with a tunnel of falling digital code and light rings rushing
// past; while you are inside it the headset camera draws ONLY the tunnel (the duel scene
// loads unseen and nothing else costs frames). When the board is ready (or after a time
// limit) a white flash opens the gate and the arena fades in.
//
// World-locked (the tunnel stays put when you turn your head) and at a steady speed, which
// is easier on the stomach than a head-locked or accelerating warp. VRSettings.Transition
// turns it off.
public class VRTransition : MonoBehaviour
{
    const int TunnelLayer = 29;
    const float FadeIn = 0.15f; // short: the menu island is already gone when the duel starts loading
    const float MinWarp = 2.6f;
    const float MaxWarp = 12f;
    const float Flash = 0.35f;
    const float FadeOut = 0.7f;
    const float TunnelLength = 70f;

    enum Phase { Idle, FadingIn, Warp, Flashing, FadingOut }

    VRRig _rig;
    Transform _root;
    Material _backdrop;
    Material _code;
    Material _rings;
    Material _gate;
    Transform _gateDisc;
    Phase _phase = Phase.Idle;
    float _phaseStart;
    float _warpStart;
    bool _wasMenu = true;
    int _normalMask;

    static readonly Color Deep = new Color(0.02f, 0.05f, 0.10f);
    static readonly Color Cyan = new Color(0.35f, 1f, 0.85f);

    public static VRTransition Create(Transform parent, VRRig rig)
    {
        GameObject go = new GameObject("DuelTransition") { layer = TunnelLayer };
        go.transform.SetParent(parent, false);
        VRTransition transition = go.AddComponent<VRTransition>();
        transition._rig = rig;
        return transition;
    }

    void LateUpdate()
    {
        if (_rig == null)
        {
            return;
        }

        bool menu = _rig.MenuLayout;
        float now = Time.unscaledTime;

        // Menu -> duel: start. (Duel -> menu just cuts back, as before.)
        if (_wasMenu && !menu && _phase == Phase.Idle && VRSettings.Transition && !VRModelStudio.Active)
        {
            Begin(now);
        }

        _wasMenu = menu;

        if (_phase == Phase.Idle)
        {
            return;
        }

        // Back to the menu before the duel got going (cancelled / failed): just end.
        if (menu)
        {
            End();
            return;
        }

        float t = now - _phaseStart;
        bool ready = BoardObserver.Context != null && BoardObserver.Context.You != null;
        Animate(now);

        switch (_phase)
        {
            case Phase.FadingIn:
                SetAlpha(Mathf.Clamp01(t / FadeIn));

                if (t >= FadeIn)
                {
                    // Fully inside: show only the tunnel while the duel loads.
                    _rig.Camera.cullingMask = 1 << TunnelLayer;
                    Next(Phase.Warp, now);
                    _warpStart = now;
                }

                break;

            case Phase.Warp:
                SetAlpha(1f);

                if ((ready && now - _warpStart >= MinWarp) || now - _warpStart >= MaxWarp)
                {
                    Next(Phase.Flashing, now);
                }

                break;

            case Phase.Flashing:
            {
                // The gate at the end of the tunnel opens into white.
                float k = Mathf.Clamp01(t / Flash);
                _backdrop.color = Color.Lerp(Deep, Color.white, k);
                _gateDisc.localScale = Vector3.one * Mathf.Lerp(6f, 60f, k * k);
                SetAlpha(1f);

                if (t >= Flash)
                {
                    _rig.Camera.cullingMask = _normalMask | (1 << TunnelLayer);
                    Next(Phase.FadingOut, now);
                }

                break;
            }

            case Phase.FadingOut:
            {
                float k = Mathf.Clamp01(t / FadeOut);
                _backdrop.color = Color.white;
                SetAlpha(1f - k * k);

                if (t >= FadeOut)
                {
                    End();
                }

                break;
            }
        }
    }

    void Begin(float now)
    {
        if (_root == null)
        {
            Build();
        }

        // Centred on your head, pointing the way you face in the duel (rig forward).
        Transform head = _rig.Camera.transform;
        _root.position = head.position;
        _root.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(_rig.transform.forward, Vector3.up).normalized, Vector3.up);
        _root.gameObject.SetActive(true);
        _backdrop.color = Deep;
        _gateDisc.localScale = Vector3.one * 6f;
        SetAlpha(0f);
        _normalMask = _rig.Camera.cullingMask & ~(1 << TunnelLayer);
        _rig.Camera.cullingMask = _normalMask | (1 << TunnelLayer);
        Next(Phase.FadingIn, now);
        Debug.Log("[DCGO VR] Duel transition started");
    }

    void End()
    {
        if (_phase != Phase.Idle && _rig != null && _rig.Camera != null)
        {
            _rig.Camera.cullingMask = _normalMask != 0 ? _normalMask : 1 << VRRig.Layer;
            Debug.Log("[DCGO VR] Duel transition ended");
        }

        _phase = Phase.Idle;

        if (_root != null)
        {
            _root.gameObject.SetActive(false);
        }
    }

    void OnDisable()
    {
        End();
    }

    void Next(Phase phase, float now)
    {
        _phase = phase;
        _phaseStart = now;
    }

    void SetAlpha(float a)
    {
        Color b = _backdrop.color;
        b.a = a;
        _backdrop.color = b;
        _code.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.85f * a);
        _rings.color = new Color(1f, 1f, 1f, 0.55f * a);
        _gate.color = new Color(0.85f, 1f, 1f, a);
    }

    // Code streams flow towards you along the tunnel; rings rush past a bit faster.
    void Animate(float now)
    {
        _code.SetTextureOffset("_BaseMap", new Vector2(now * 0.02f, -now * 0.55f));
        _rings.SetTextureOffset("_BaseMap", new Vector2(0f, -now * 1.1f));
    }

    // ---------------------------------------------------------------- build

    void Build()
    {
        _root = new GameObject("Tunnel") { layer = TunnelLayer }.transform;
        _root.SetParent(_rig.transform, false);

        _backdrop = Transparent(Deep, null, 3990);
        AddMesh(_root, "Backdrop", SphereMesh(7f), _backdrop);

        Texture2D code = VRMenuWorld.CodeTexture();
        _code = Transparent(Cyan, code, 3991);
        _code.SetTextureScale("_BaseMap", new Vector2(5f, TunnelLength / 9f));
        AddMesh(_root, "Code", TubeMesh(2.6f, -6f, TunnelLength), _code);

        _rings = Transparent(Color.white, RingTexture(), 3992);
        _rings.SetTextureScale("_BaseMap", new Vector2(1f, TunnelLength / 7f));
        AddMesh(_root, "Rings", TubeMesh(2.45f, -6f, TunnelLength), _rings);

        // The gate: a soft bright disc far down the tunnel.
        _gate = Transparent(Color.white, GlowTexture(), 3993);
        _gateDisc = AddMesh(_root, "Gate", QuadMesh(), _gate).transform;
        _gateDisc.localPosition = new Vector3(0f, 0f, TunnelLength - 2f);
        _gateDisc.localScale = Vector3.one * 6f;
        _root.gameObject.SetActive(false);
    }

    static Material Transparent(Color color, Texture texture, int queue)
    {
        Material material = VRMaterials.Transparent(color);

        if (texture != null)
        {
            material.SetTexture("_BaseMap", texture);
            material.SetTexture("_MainTex", texture);
        }

        material.renderQueue = queue;
        return material;
    }

    static Renderer AddMesh(Transform parent, string name, Mesh mesh, Material material)
    {
        GameObject go = new GameObject(name) { layer = TunnelLayer };
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }

    // Both windings everywhere: seen from inside whatever the maths says.
    static void TwoSided(List<int> triangles, int a, int b, int c)
    {
        triangles.AddRange(new[] { a, b, c, a, c, b });
    }

    static Mesh TubeMesh(float radius, float from, float to)
    {
        const int segments = 32;
        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();

        for (int i = 0; i <= segments; i++)
        {
            float u = i / (float)segments;
            float a = u * Mathf.PI * 2f;
            Vector3 ring = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius;
            vertices.Add(ring + Vector3.forward * from);
            vertices.Add(ring + Vector3.forward * to);
            uvs.Add(new Vector2(u, 0f));
            uvs.Add(new Vector2(u, 1f));
        }

        for (int i = 0; i < segments; i++)
        {
            int a = i * 2;
            TwoSided(triangles, a, a + 1, a + 3);
            TwoSided(triangles, a, a + 3, a + 2);
        }

        Mesh mesh = new Mesh { name = "TransitionTube" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    static Mesh SphereMesh(float radius)
    {
        const int lon = 16;
        const int lat = 8;
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();

        for (int y = 0; y <= lat; y++)
        {
            float e = Mathf.Lerp(-90f, 90f, y / (float)lat) * Mathf.Deg2Rad;

            for (int x = 0; x <= lon; x++)
            {
                float a = x / (float)lon * Mathf.PI * 2f;
                vertices.Add(new Vector3(Mathf.Cos(e) * Mathf.Cos(a), Mathf.Sin(e), Mathf.Cos(e) * Mathf.Sin(a)) * radius);
            }
        }

        for (int y = 0; y < lat; y++)
        {
            for (int x = 0; x < lon; x++)
            {
                int i = y * (lon + 1) + x;
                TwoSided(triangles, i, i + 1, i + lon + 2);
                TwoSided(triangles, i, i + lon + 2, i + lon + 1);
            }
        }

        Mesh mesh = new Mesh { name = "TransitionSphere" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    static Mesh QuadMesh()
    {
        Mesh mesh = new Mesh
        {
            name = "TransitionGate",
            vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f) },
            uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) },
        };
        List<int> triangles = new List<int>();
        TwoSided(triangles, 0, 3, 1);
        TwoSided(triangles, 3, 0, 2);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // Thin bright bands across the tunnel (they become rings), with a soft glow around each.
    static Texture2D RingTexture()
    {
        Texture2D texture = new Texture2D(4, 64, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, name = "DCGOVR_TransitionRings" };

        for (int y = 0; y < 64; y++)
        {
            float d = Mathf.Abs(y - 32f) / 32f;
            float a = Mathf.Pow(1f - d, 14f) + 0.15f * Mathf.Pow(1f - d, 3f);

            for (int x = 0; x < 4; x++)
            {
                texture.SetPixel(x, y, new Color(0.75f, 1f, 1f, Mathf.Clamp01(a)));
            }
        }

        texture.Apply(true, true);
        return texture;
    }

    static Texture2D GlowTexture()
    {
        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "DCGOVR_TransitionGate" };

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = new Vector2(x - size * 0.5f + 0.5f, y - size * 0.5f + 0.5f).magnitude / (size * 0.5f);
                float a = Mathf.Clamp01(1f - d);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        }

        texture.Apply(true, true);
        return texture;
    }
}
#endif
