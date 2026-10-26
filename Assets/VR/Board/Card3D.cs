#if DCGO_VR
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// A physical card on the VR table: two quads (front art / card back) and a
// thin collider. Board3D tells it where it should be every frame; the card
// glides there, which gives draw / play / digivolve / trash movement for free.
public class Card3D : MonoBehaviour
{
    public const float Width = 0.09f;
    public const float Height = 0.126f;
    const float Thickness = 0.0015f;
    const float FollowRate = 14f;
    const float FastFollowRate = 45f;

    static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
    static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");
    static readonly int MainTex = Shader.PropertyToID("_MainTex");
    static readonly int MainTexST = Shader.PropertyToID("_MainTex_ST");
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    public CardSource Card { get; private set; }
    public bool Held { get; set; }
    public bool Hovered { get; set; }
    public bool IsFaceUp { get; private set; }

    Renderer _front;
    Renderer _back;
    Renderer _glow;
    Color _glowColor;
    MaterialPropertyBlock _block;
    Vector3 _targetPosition;
    Quaternion _targetRotation;
    bool _placed;
    bool _fast;
    float _targetScale = 1f;
    Vector3 _hoverOffset = new Vector3(0f, 0.012f, 0f);
    bool _frontLoaded;
    bool _eggBack;
    Sprite _backSprite;

    public static Card3D Create(Transform parent, CardSource card, Material material)
    {
        GameObject root = new GameObject("Card3D " + card.CardID) { layer = VRRig.Layer };
        root.transform.SetParent(parent, false);

        Card3D card3D = root.AddComponent<Card3D>();
        card3D.Card = card;
        card3D._block = new MaterialPropertyBlock();
        card3D._front = CreateFace(root.transform, "Front", Quaternion.Euler(90f, 0f, 0f), Thickness * 0.5f, material);
        card3D._back = CreateFace(root.transform, "Back", Quaternion.Euler(-90f, 180f, 0f), -Thickness * 0.5f, material);

        // Glow frame (selectable = blue, selected = orange), shown behind the card.
        card3D._glow = CreateFace(root.transform, "Glow", Quaternion.Euler(90f, 0f, 0f), -Thickness, material);
        card3D._glow.transform.localScale = new Vector3(Width + 0.014f, Height + 0.014f, 1f);
        card3D._glow.gameObject.SetActive(false);

        BoxCollider collider = root.AddComponent<BoxCollider>();
        collider.size = new Vector3(Width, Thickness * 4f, Height);

        card3D.ApplyBack();
        return card3D;
    }

    static Renderer CreateFace(Transform parent, string name, Quaternion rotation, float yOffset, Material material)
    {
        GameObject face = GameObject.CreatePrimitive(PrimitiveType.Quad);
        face.name = name;
        face.layer = VRRig.Layer;
        Destroy(face.GetComponent<Collider>());
        face.transform.SetParent(parent, false);
        face.transform.localRotation = rotation;
        face.transform.localPosition = new Vector3(0f, yOffset, 0f);
        face.transform.localScale = new Vector3(Width, Height, 1f);
        Renderer renderer = face.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }

    /// <summary>Where the card should be (in its parent's space), and whether its face shows.</summary>
    public void SetTarget(Vector3 localPosition, Quaternion localRotation, bool faceUp, bool eggBack, bool fastFollow = false, float scale = 1f, Vector3? hoverOffset = null)
    {
        _fast = fastFollow;
        _targetScale = scale;
        _hoverOffset = hoverOffset ?? new Vector3(0f, 0.012f, 0f);
        _targetPosition = localPosition;
        _targetRotation = faceUp ? localRotation : localRotation * Quaternion.Euler(0f, 0f, 180f);

        if (eggBack != _eggBack || _backSprite == null)
        {
            _eggBack = eggBack;
            ApplyBack();
        }

        if (faceUp && !_frontLoaded)
        {
            LoadFront();
        }

        IsFaceUp = faceUp;

        if (!_placed)
        {
            transform.localPosition = _targetPosition;
            transform.localRotation = _targetRotation;
            _placed = true;
        }
    }

    void Update()
    {
        if (Held)
        {
            return;
        }

        float t = 1f - Mathf.Exp(-(_fast ? FastFollowRate : FollowRate) * Time.deltaTime);
        Vector3 target = _targetPosition + (Hovered ? _hoverOffset : Vector3.zero);
        transform.localPosition = Vector3.Lerp(transform.localPosition, target, t);
        transform.localRotation = Quaternion.Slerp(transform.localRotation, _targetRotation, t);
        float scale = Mathf.Lerp(transform.localScale.x, _targetScale * (Hovered && _targetScale < 1f ? 1.15f : 1f), t);
        transform.localScale = new Vector3(scale, scale, scale);
    }

    /// <summary>Copies this card's loaded front image onto another renderer (e.g. the disk security
    /// hologram). False while the front has not loaded.</summary>
    public bool CopyFrontTo(Renderer target)
    {
        if (target == null || _front == null || !_front.HasPropertyBlock())
        {
            return false;
        }

        _front.GetPropertyBlock(_block);
        Texture texture = _block.GetTexture(BaseMap);

        if (texture == null)
        {
            return false;
        }

        MaterialPropertyBlock copy = new MaterialPropertyBlock();
        copy.SetTexture(BaseMap, texture);
        copy.SetTexture(MainTex, texture);
        copy.SetVector(BaseMapST, _block.GetVector(BaseMapST));
        copy.SetVector(MainTexST, _block.GetVector(MainTexST));
        target.SetPropertyBlock(copy);
        return true;
    }

    bool _hidden;

    /// <summary>Stops drawing the card (and its laser collider) while it keeps following its
    /// target, e.g. cards lying on the field when the field hologram is turned off.</summary>
    public void SetHidden(bool hidden)
    {
        if (_hidden == hidden)
        {
            return;
        }

        _hidden = hidden;
        _front.enabled = !hidden;
        _back.enabled = !hidden;
        _glow.enabled = !hidden;
        Collider collider = GetComponent<Collider>();

        if (collider != null)
        {
            collider.enabled = !hidden;
        }
    }

    /// <summary>Shows a coloured frame around the card (null hides it).</summary>
    public void SetGlow(Color? color)
    {
        bool on = color.HasValue;

        if (_glow.gameObject.activeSelf != on)
        {
            _glow.gameObject.SetActive(on);
        }

        if (on && color.Value != _glowColor)
        {
            _glowColor = color.Value;
            SetTint(_glow, _glowColor);
        }
    }

    public void Tint(Color color)
    {
        SetTint(_front, color);
        SetTint(_back, color);
    }

    void SetTint(Renderer renderer, Color color)
    {
        renderer.GetPropertyBlock(_block);
        _block.SetColor(BaseColor, color);
        renderer.SetPropertyBlock(_block);
    }

    async void LoadFront()
    {
        _frontLoaded = true;
        Sprite sprite = null;

        try
        {
            sprite = Card.CardSprite != null ? Card.CardSprite : await Card.GetCardSprite();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[DCGO VR] Card image load failed for " + Card.CardID + ": " + e.Message);
        }

        if (this == null || sprite == null)
        {
            _frontLoaded = sprite != null;
            return;
        }

        ApplySprite(_front, sprite);
    }

    void ApplyBack()
    {
        ContinuousController controller = ContinuousController.instance;

        if (controller == null)
        {
            return;
        }

        _backSprite = _eggBack && controller.ReverseCard_Digitama != null ? controller.ReverseCard_Digitama : controller.ReverseCard;

        if (_backSprite != null)
        {
            ApplySprite(_back, _backSprite);
        }
    }

    void ApplySprite(Renderer renderer, Sprite sprite)
    {
        Texture2D texture = sprite.texture;
        Rect rect = sprite.textureRect;
        Vector4 st = new Vector4(rect.width / texture.width, rect.height / texture.height, rect.x / texture.width, rect.y / texture.height);

        renderer.GetPropertyBlock(_block);
        _block.SetTexture(BaseMap, texture);
        _block.SetTexture(MainTex, texture);
        _block.SetVector(BaseMapST, st);
        _block.SetVector(MainTexST, st);
        renderer.SetPropertyBlock(_block);
    }

    /// <summary>The card art as a sprite (for the big inspect preview), or null if not loaded.</summary>
    public Sprite FrontSprite => _frontLoaded ? Card.CardSprite : null;
}
#endif
