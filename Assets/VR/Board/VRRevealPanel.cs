#if DCGO_VR
using System.Collections.Generic;
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// Shows in VR what the flat game shows in its "shown cards" panels (Effects.ShowCardEffect
// and ShowCardEffect2): revealed cards, searched cards, the card the opponent picked,
// discarded cards... A floating panel in front of you with the panel's title and the very
// same card faces the flat screen shows (face down stays face down, so nothing hidden is
// revealed), numbered when there are several, outlined like on the flat screen.
// Read-only: it only looks at the flat panels.
public class VRRevealPanel : MonoBehaviour
{
    const float CardW = 0.15f;
    const float CardH = 0.21f;
    const float Gap = 0.012f;
    const int PerRowWorld = 7;
    const int PerRowHud = 4;
    const float HudScale = 0.48f;    // the panel shrunk onto the HUD (it is only 0.7 m away)
    int _perRow = PerRowWorld;
    const float Distance = 1.25f;

    sealed class Panel
    {
        public Transform Root;
        public Transform Frame;
        public VRLabel Title;
        public readonly List<Renderer> Faces = new List<Renderer>();
        public readonly List<Renderer> Outlines = new List<Renderer>();
        public readonly List<VRLabel> Numbers = new List<VRLabel>();
        public readonly List<Sprite> Sprites = new List<Sprite>();
        public bool Open;
        public int Rows;
        public float Width;
        public float Height;
    }

    Transform _head;
    readonly Panel[] _panels = { new Panel(), new Panel() };
    MaterialPropertyBlock _block; // created in Create(): Unity forbids it in a field initializer
    readonly List<HandCard> _cards = new List<HandCard>();
    static Material _faceMaterial;
    static Material _outlineMaterial;
    readonly Dictionary<Color, Material> _outlineColors = new Dictionary<Color, Material>();
    Vector3 _anchor;
    Quaternion _anchorRotation;
    bool _anchored;

    public static VRRevealPanel Create(Transform parent, Transform head)
    {
        GameObject go = new GameObject("RevealPanel") { layer = VRRig.Layer };
        go.transform.SetParent(parent, false);
        VRRevealPanel panel = go.AddComponent<VRRevealPanel>();
        panel._head = head;
        panel._block = new MaterialPropertyBlock();
        return panel;
    }

    void LateUpdate()
    {
        Effects effects = GManager.instance != null ? GManager.instance.GetComponent<Effects>() : null;

        bool any = false;

        for (int i = 0; i < 2; i++)
        {
            Transform parent = effects == null ? null : i == 0 ? effects.ShowCardParent : effects.ShowCardParent2;
            TMPro.TextMeshProUGUI title = effects == null ? null : i == 0 ? effects.ShowCardTitleText : effects.ShowCardTitleText2;
            bool open = parent != null && parent.gameObject.activeInHierarchy && parent.childCount > 0;
            Mirror(_panels[i], open ? parent : null, title);
            any |= _panels[i].Open;
        }

        if (!any)
        {
            _anchored = false;
            return;
        }

        // On the HUD, under the opponent's webcam (right of the memory gauge), close to the
        // centre of your view so the cards are readable. Without the HUD: once in front of you, above the field.
        VRMemoryHud hud = VRMemoryHud.Instance;
        bool onHud = hud != null && hud.Showing;
        _perRow = onHud ? PerRowHud : PerRowWorld;

        if (onHud)
        {
            float top = -0.1f; // under the opponent webcam, its name strip and room for the panel title

            for (int i = 0; i < 2; i++)
            {
                Panel panel = _panels[i];

                if (!panel.Open)
                {
                    continue;
                }

                if (panel.Root.parent != hud.transform)
                {
                    panel.Root.SetParent(hud.transform, false);
                }

                float w = panel.Width * HudScale;
                float h = panel.Height * HudScale;
                // Centred under the opponent webcam (x 0.304), never reaching into the gauge's column;
                // turned to face your eye.
                float x = Mathf.Max(0.304f, 0.12f + w * 0.5f);
                float y = top - h * 0.5f + 0.02f * HudScale;
                panel.Root.localPosition = new Vector3(x, y, 0f);
                panel.Root.localRotation = Quaternion.Euler(0f, Mathf.Atan2(x, 0.7f) * Mathf.Rad2Deg, 0f);
                panel.Root.localScale = Vector3.one * HudScale;
                top -= h + 0.012f;
            }

            return;
        }

        if (!_anchored && _head != null)
        {
            Vector3 forward = _head.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
            _anchor = _head.position + forward * Distance + Vector3.up * 0.02f;
            _anchorRotation = Quaternion.LookRotation(forward, Vector3.up);
            _anchored = true;
        }

        // Panel 1 on top, panel 2 under it when both are shown.
        float y0 = 0f;

        for (int i = 0; i < 2; i++)
        {
            Panel panel = _panels[i];

            if (!panel.Open)
            {
                continue;
            }

            if (panel.Root.parent != transform)
            {
                panel.Root.SetParent(transform, false);
            }

            panel.Root.localScale = Vector3.one;
            panel.Root.SetPositionAndRotation(_anchor + _anchorRotation * new Vector3(0f, y0 - panel.Height * 0.5f + 0.15f, 0f), _anchorRotation);
            y0 -= panel.Height + 0.03f;
        }
    }

    void Mirror(Panel panel, Transform parent, TMPro.TextMeshProUGUI title)
    {
        if (parent == null)
        {
            if (panel.Open)
            {
                Debug.Log("[DCGO VR] Shown cards closed");
                panel.Open = false;
                panel.Root.gameObject.SetActive(false);
            }

            return;
        }

        _cards.Clear();

        for (int i = 0; i < parent.childCount; i++)
        {
            HandCard card = parent.GetChild(i).GetComponent<HandCard>();

            if (card != null && card.gameObject.activeSelf)
            {
                _cards.Add(card);
            }
        }

        if (_cards.Count == 0)
        {
            Mirror(panel, null, null);
            return;
        }

        if (panel.Root == null)
        {
            Build(panel);
        }

        if (!panel.Open)
        {
            Debug.Log($"[DCGO VR] Shown cards: '{(title != null ? title.text : "")}' x{_cards.Count}");
            panel.Open = true;
            panel.Root.gameObject.SetActive(true);
        }

        int n = _cards.Count;
        int columns = Mathf.Min(_perRow, n);
        panel.Rows = Mathf.CeilToInt(n / (float)_perRow);
        float width = Mathf.Max(columns * (CardW + Gap) + 0.04f, 0.5f);
        float height = panel.Rows * (CardH + Gap) + 0.06f;
        panel.Width = width;
        panel.Height = height + 0.06f; // + title
        panel.Frame.localScale = new Vector3(width, height, 0.004f);
        panel.Frame.localPosition = new Vector3(0f, 0f, 0.004f);

        string text = title != null ? title.text : "";
        panel.Title.text = string.IsNullOrEmpty(text) ? "Cards" : text;
        panel.Title.transform.localPosition = new Vector3(0f, height * 0.5f + 0.03f, -0.001f);

        while (panel.Faces.Count < n)
        {
            AddSlot(panel);
        }

        for (int i = 0; i < panel.Faces.Count; i++)
        {
            bool used = i < n;
            panel.Faces[i].gameObject.SetActive(used);
            panel.Outlines[i].gameObject.SetActive(false);
            panel.Numbers[i].gameObject.SetActive(false);

            if (!used)
            {
                continue;
            }

            int row = i / _perRow;
            int inRow = Mathf.Min(_perRow, n - row * _perRow);
            int column = i % _perRow;
            Vector3 position = new Vector3((column - (inRow - 1) * 0.5f) * (CardW + Gap), ((panel.Rows - 1) * 0.5f - row) * (CardH + Gap), 0f);
            panel.Faces[i].transform.localPosition = position;
            panel.Outlines[i].transform.localPosition = position + new Vector3(0f, 0f, 0.002f);

            // The exact picture the flat screen shows (front, or the card back when face down).
            HandCard card = _cards[i];
            Sprite sprite = card.CardImage != null ? card.CardImage.sprite : null;

            if (sprite != panel.Sprites[i])
            {
                panel.Sprites[i] = sprite;
                SetSprite(panel.Faces[i], sprite);
            }

            // Outline as on the flat screen (selected / selectable).
            if (card.Outline_Select != null && card.Outline_Select.activeInHierarchy)
            {
                Color color = card.Outline_SelectImages.Count > 0 && card.Outline_SelectImages[0] != null ? card.Outline_SelectImages[0].color : VRPromptBridge.Selected;
                panel.Outlines[i].sharedMaterial = OutlineMaterial(color);
                panel.Outlines[i].gameObject.SetActive(true);
            }

            bool faceUp = card.cardSource != null && sprite != null && card.cardSource.IsFaceUp;

            if (n >= 2 && faceUp)
            {
                panel.Numbers[i].text = "<b>" + (i + 1) + "</b>";
                panel.Numbers[i].transform.localPosition = position + new Vector3(-CardW * 0.5f + 0.018f, CardH * 0.5f - 0.018f, -0.002f);
                panel.Numbers[i].gameObject.SetActive(true);
            }
        }
    }

    void Build(Panel panel)
    {
        panel.Root = new GameObject("ShownCards") { layer = VRRig.Layer }.transform;
        panel.Root.SetParent(transform, false);

        GameObject frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
        frame.layer = VRRig.Layer;
        Destroy(frame.GetComponent<Collider>());
        frame.transform.SetParent(panel.Root, false);
        frame.GetComponent<Renderer>().sharedMaterial = VRMaterials.Unlit(new Color(0.04f, 0.06f, 0.1f));
        panel.Frame = frame.transform;

        panel.Title = VRLabel.Create(panel.Root, Vector3.zero, Quaternion.identity, new Vector2(0.6f, 0.05f), "", new Color(1f, 0.85f, 0.3f), new Color(0.06f, 0.08f, 0.14f));
    }

    void AddSlot(Panel panel)
    {
        GameObject outline = GameObject.CreatePrimitive(PrimitiveType.Quad);
        outline.layer = VRRig.Layer;
        Destroy(outline.GetComponent<Collider>());
        outline.transform.SetParent(panel.Root, false);
        outline.transform.localScale = new Vector3(CardW + 0.014f, CardH + 0.014f, 1f);
        outline.GetComponent<Renderer>().sharedMaterial = _outlineMaterial != null ? _outlineMaterial : _outlineMaterial = VRMaterials.Unlit(VRPromptBridge.Selected);
        panel.Outlines.Add(outline.GetComponent<Renderer>());

        GameObject face = GameObject.CreatePrimitive(PrimitiveType.Quad);
        face.layer = VRRig.Layer;
        Destroy(face.GetComponent<Collider>());
        face.transform.SetParent(panel.Root, false);
        face.transform.localScale = new Vector3(CardW, CardH, 1f);
        Renderer renderer = face.GetComponent<Renderer>();
        renderer.sharedMaterial = _faceMaterial != null ? _faceMaterial : _faceMaterial = VRMaterials.Unlit(Color.white);
        panel.Faces.Add(renderer);
        panel.Sprites.Add(null);

        panel.Numbers.Add(VRLabel.Create(panel.Root, Vector3.zero, Quaternion.identity, new Vector2(0.03f, 0.03f), "", Color.white, new Color(0.1f, 0.3f, 0.6f)));
    }

    Material OutlineMaterial(Color color)
    {
        color.a = 1f;

        if (!_outlineColors.TryGetValue(color, out Material material))
        {
            material = VRMaterials.Unlit(color);
            _outlineColors[color] = material;
        }

        return material;
    }

    void SetSprite(Renderer target, Sprite sprite)
    {
        if (sprite == null)
        {
            target.SetPropertyBlock(null);
            return;
        }

        Texture2D texture = sprite.texture;
        Rect rect = sprite.textureRect;
        Vector4 st = new Vector4(rect.width / texture.width, rect.height / texture.height, rect.x / texture.width, rect.y / texture.height);
        _block.Clear();
        _block.SetTexture("_BaseMap", texture);
        _block.SetTexture("_MainTex", texture);
        _block.SetVector("_BaseMap_ST", st);
        _block.SetVector("_MainTex_ST", st);
        target.SetPropertyBlock(_block);
    }
}
#endif
