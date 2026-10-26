#if DCGO_VR
using System.Collections.Generic;
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// Memory gauge fixed at the top of your view during a duel (head-locked, follows
// your look with a light smoothing so it never swims). Opponent side red on the
// left, yours blue on the right, a gold token on the current value. Read-only:
// it only looks at GameContext.Memory / TurnPlayer.
public class VRMemoryHud : MonoBehaviour
{
    const float Distance = 0.7f;      // in front of the eyes
    const float Up = 0.215f;          // above the line of sight (~17 degrees)
    const float Tile = 0.022f;
    const float Gap = 0.002f;

    static readonly Color Theirs = new Color(0.62f, 0.16f, 0.16f);
    static readonly Color Yours = new Color(0.12f, 0.36f, 0.72f);
    static readonly Color Zero = new Color(0.3f, 0.32f, 0.36f);

    /// <summary>The live HUD (null before the VR session builds it).</summary>
    public static VRMemoryHud Instance { get; private set; }

    /// <summary>Where the phase strip docks (under the gauge) while the HUD is shown.</summary>
    public Transform PhaseAnchor { get; private set; }

    public bool Showing => _visible;

    Transform _head;
    Transform _token;
    VRLabel _caption;
    readonly List<Renderer> _tiles = new List<Renderer>();
    int _shown = int.MinValue;
    string _captionText;

    public static VRMemoryHud Create(Transform parent, Transform head)
    {
        GameObject go = new GameObject("MemoryHud") { layer = VRRig.Layer };
        go.transform.SetParent(parent, false);
        VRMemoryHud hud = go.AddComponent<VRMemoryHud>();
        hud._head = head;
        Instance = hud;
        hud.Build();
        hud.SetVisible(false);
        return hud;
    }

    void Build()
    {
        float step = Tile + Gap;

        // Backing plate.
        Quad("Plate", new Vector3(0f, -0.026f, 0.002f), new Vector2(21 * step + 0.012f, 0.076f), new Color(0.04f, 0.06f, 0.09f));

        for (int v = -10; v <= 10; v++)
        {
            Color color = v < 0 ? Theirs : v > 0 ? Yours : Zero;
            Vector3 position = new Vector3(v * step, 0f, 0f);
            _tiles.Add(Quad("Tile", position, new Vector2(Tile, Tile), color));
            VRLabel.Create(transform, position + new Vector3(0f, 0f, -0.001f), Quaternion.identity, new Vector2(Tile * 0.9f, Tile * 0.7f), Mathf.Abs(v).ToString(), Color.white, color);
        }

        GameObject token = GameObject.CreatePrimitive(PrimitiveType.Quad);
        token.name = "Token";
        token.layer = VRRig.Layer;
        Destroy(token.GetComponent<Collider>());
        token.transform.SetParent(transform, false);
        token.transform.localScale = new Vector3(Tile + 0.006f, Tile + 0.006f, 1f);
        token.GetComponent<Renderer>().sharedMaterial = VRMaterials.Unlit(new Color(1f, 0.82f, 0.2f));
        _token = token.transform;
        _token.localPosition = new Vector3(0f, 0f, 0.0005f); // just behind the tile: a gold frame around it

        _caption = VRLabel.Create(transform, new Vector3(0f, -0.024f, -0.001f), Quaternion.identity, new Vector2(0.2f, 0.016f), "", new Color(1f, 0.85f, 0.3f), new Color(0.04f, 0.06f, 0.09f));

        // Phase strip (VRPhaseBar) docks here, under the memory line.
        PhaseAnchor = new GameObject("PhaseAnchor") { layer = VRRig.Layer }.transform;
        PhaseAnchor.SetParent(transform, false);
        PhaseAnchor.localPosition = new Vector3(0f, -0.047f, -0.001f);
    }

    Renderer Quad(string name, Vector3 position, Vector2 size, Color color)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        quad.layer = VRRig.Layer;
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(transform, false);
        quad.transform.localPosition = position;
        quad.transform.localScale = new Vector3(size.x, size.y, 1f);
        Renderer renderer = quad.GetComponent<Renderer>();
        renderer.sharedMaterial = VRMaterials.Unlit(color);
        return renderer;
    }

    bool _visible = true;

    void SetVisible(bool visible)
    {
        if (_visible == visible)
        {
            return;
        }

        _visible = visible;

        foreach (Transform child in transform)
        {
            child.gameObject.SetActive(visible);
        }
    }

    void LateUpdate()
    {
        GameContext context = BoardObserver.Context;
        bool active = VRSettings.MemoryHud && _head != null && context != null && context.You != null;
        SetVisible(active);

        if (!active)
        {
            return;
        }

        // Head-locked: always at the top of the view, upright, facing the eyes.
        Vector3 target = _head.position + _head.rotation * new Vector3(0f, Up, Distance);
        Quaternion rotation = Quaternion.LookRotation(target - _head.position, _head.up);
        float follow = 1f - Mathf.Exp(-25f * Time.unscaledDeltaTime);
        bool far = (transform.position - target).sqrMagnitude > 0.25f;
        transform.position = far ? target : Vector3.Lerp(transform.position, target, follow);
        transform.rotation = far ? rotation : Quaternion.Slerp(transform.rotation, rotation, follow);

        // Same convention as the table gauge: positive = you have memory.
        int memory = Mathf.Clamp(context.You.PlayerID == 0 ? context.Memory : -context.Memory, -10, 10);
        float step = Tile + Gap;
        Vector3 tokenTarget = new Vector3(memory * step, 0f, 0.0005f);
        _token.localPosition = _shown == int.MinValue ? tokenTarget : Vector3.Lerp(_token.localPosition, tokenTarget, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));

        string caption = (memory > 0 ? "YOU HAVE " + memory + " MEMORY" : memory < 0 ? "OPPONENT HAS " + (-memory) + " MEMORY" : "MEMORY 0");

        if (memory != _shown || caption != _captionText)
        {
            _shown = memory;
            _captionText = caption;
            _caption.text = caption;
        }
    }
}
#endif
