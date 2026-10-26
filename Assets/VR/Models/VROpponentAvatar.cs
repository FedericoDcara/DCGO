#if DCGO_VR
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// Your opponent in person: a big NPC model (Mirei Mikagura, from _DSTSModels) behind
// a podium beyond the opponent's field, seen from the waist up, facing you with her
// eyes at your eye level. Over her head floats her security: one shield per card,
// or the card itself when it lies face up in the security.
// Size can be fine-tuned in the model studio like any other model ("mirei").
public class VROpponentAvatar : MonoBehaviour
{
    const string ModelKey = "mirei";
    const float AvatarHeight = 5.8f;       // a giant: reads well from the far end of the arena
    const float EyeRatio = 0.93f;          // eye height / body height
    const float WaistRatio = 0.56f;        // podium top (hides the legs)
    const float PodiumWidth = 3.0f;
    const float PodiumDepth = 1.1f;

    /// <summary>The avatar's root (arena space, faces you) while shown; null otherwise.</summary>
    public static Transform Root { get; private set; }

    /// <summary>The loaded model (for the HUD webcam) and its height; null while not shown.</summary>
    public static Transform Model { get; private set; }
    public static float Height { get; private set; }

    /// <summary>Where her option cards stand: beside the podium on her right (your left).</summary>
    public static Vector3 OptionSpot(int index) => new Vector3(-(PodiumWidth * 0.5f + 0.75f + index * 1.15f), AvatarHeight * 0.55f, -0.4f);

    Transform _arena;
    Transform _head;
    Transform _root;
    Transform _model;
    string _path;
    bool _loading;
    bool _failed;
    float _height = AvatarHeight;
    Transform _figure;
    readonly System.Collections.Generic.List<Renderer> _rim = new System.Collections.Generic.List<Renderer>();
    Material _rimIdle;
    Material _rimHover;
    Material _rimTarget;
    Material _rimTargetDim;
    VRLabel _caption;
    Transform _shieldRow;
    Transform _handRow;          // her hand: small card backs fanned in front of her chest
    VRLabel _handCaption;
    readonly System.Collections.Generic.List<Renderer> _handSlots = new System.Collections.Generic.List<Renderer>();
    int _handCount = -1;
    MaterialPropertyBlock _backBlock;
    bool _handBackApplied;
    static Material _handFallback;   // plain card back until the game's back image is available
    BoxCollider _shieldHit;      // the shields are a laser target too: attack the player over her Digimon
    Renderer _shieldGlow;        // plate behind the shields: gold pulse when attackable, white when pointed at
    readonly System.Collections.Generic.List<Renderer> _slots = new System.Collections.Generic.List<Renderer>();
    readonly System.Collections.Generic.List<CardSource> _slotCards = new System.Collections.Generic.List<CardSource>();
    int _shown = -1;
    int _securityKey = int.MinValue;
    static Material _shieldMaterial;
    static Material _cardMaterial;
    MaterialPropertyBlock _block;

    public static VROpponentAvatar Create(Transform parent, Transform arena, Transform head)
    {
        GameObject go = new GameObject("OpponentAvatar") { layer = VRRig.Layer };
        go.transform.SetParent(parent, false);
        VROpponentAvatar avatar = go.AddComponent<VROpponentAvatar>();
        avatar._arena = arena;
        avatar._head = head;
        return avatar;
    }

    void Build()
    {
        _root = new GameObject("OpponentAvatarRoot") { layer = VRRig.Layer }.transform;
        _root.SetParent(_arena, false);

        // She stands just behind the podium, facing you (-Z in arena space is towards you).
        _figure = new GameObject("Figure") { layer = VRRig.Layer }.transform;
        _figure.SetParent(_root, false);
        _figure.localPosition = new Vector3(0f, 0f, 0.25f);
        _figure.localRotation = Quaternion.Euler(0f, 180f, 0f);

        // Podium: dark block up to her waist, red frame on the front, a top ledge.
        float top = AvatarHeight * WaistRatio;
        Box(_root, new Vector3(0f, top * 0.5f, -PodiumDepth * 0.5f), new Vector3(PodiumWidth, top, PodiumDepth), new Color(0.07f, 0.05f, 0.07f));
        Box(_root, new Vector3(0f, top + 0.03f, -PodiumDepth * 0.5f - 0.05f), new Vector3(PodiumWidth + 0.16f, 0.06f, PodiumDepth + 0.2f), new Color(0.16f, 0.07f, 0.08f));
        float front = -PodiumDepth - 0.012f;
        _rim.Add(Box(_root, new Vector3(0f, top - 0.04f, front), new Vector3(PodiumWidth, 0.07f, 0.02f), new Color(1f, 0.3f, 0.25f)));
        _rim.Add(Box(_root, new Vector3(0f, 0.035f, front), new Vector3(PodiumWidth, 0.07f, 0.02f), new Color(1f, 0.3f, 0.25f)));
        _rim.Add(Box(_root, new Vector3(-PodiumWidth * 0.5f + 0.035f, top * 0.5f, front), new Vector3(0.07f, top, 0.02f), new Color(1f, 0.3f, 0.25f)));
        _rim.Add(Box(_root, new Vector3(PodiumWidth * 0.5f - 0.035f, top * 0.5f, front), new Vector3(0.07f, top, 0.02f), new Color(1f, 0.3f, 0.25f)));
        _rimIdle = _rim[0].sharedMaterial;
        _rimHover = VRMaterials.Unlit(Color.white);
        _rimTarget = VRMaterials.Unlit(new Color(1f, 0.85f, 0.2f));
        _rimTargetDim = VRMaterials.Unlit(new Color(0.8f, 0.45f, 0.1f));

        // Laser target: podium + her upper body = attack the player.
        BoxCollider hit = _root.gameObject.AddComponent<BoxCollider>();
        hit.isTrigger = true;
        hit.center = new Vector3(0f, AvatarHeight * 0.5f, -PodiumDepth * 0.4f);
        hit.size = new Vector3(PodiumWidth, AvatarHeight, PodiumDepth + 0.6f);
        _root.gameObject.AddComponent<VRArenaTarget>().OpponentPlayer = true;

        // Security count floating over her head, facing you (placed in LateUpdate).
        _shieldRow = new GameObject("SecurityShields") { layer = VRRig.Layer }.transform;
        _shieldRow.SetParent(_root, false);
        _shieldHit = _shieldRow.gameObject.AddComponent<BoxCollider>();
        _shieldHit.isTrigger = true;
        _shieldHit.size = new Vector3(0.8f, 0.8f, 0.3f);
        _shieldHit.center = new Vector3(0f, 0.35f, 0f);
        VRArenaTarget shieldTarget = _shieldRow.gameObject.AddComponent<VRArenaTarget>();
        shieldTarget.OpponentPlayer = true;
        shieldTarget.Priority = true;
        GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
        glow.name = "ShieldGlow";
        glow.layer = VRRig.Layer;
        Destroy(glow.GetComponent<Collider>());
        glow.transform.SetParent(_shieldRow, false);
        _shieldGlow = glow.GetComponent<Renderer>();
        _shieldGlow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        glow.SetActive(false);
        _block = new MaterialPropertyBlock();
        _caption = VRLabel.Create(_root, Vector3.zero, Quaternion.identity, new Vector2(1.3f, 0.24f), "SECURITY", new Color(1f, 0.75f, 0.7f), new Color(0.12f, 0.04f, 0.05f));

        // Her hand (card backs) held in front of her chest, with the count under it.
        _handRow = new GameObject("OpponentHand") { layer = VRRig.Layer }.transform;
        _handRow.SetParent(_root, false);
        _handCaption = VRLabel.Create(_handRow, new Vector3(0f, -HandCardH * 0.5f - 0.2f, 0f), Quaternion.identity, new Vector2(0.9f, 0.18f), "HAND", new Color(1f, 0.75f, 0.7f), new Color(0.12f, 0.04f, 0.05f));
    }

    static Renderer Box(Transform parent, Vector3 position, Vector3 size, Color color)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.layer = VRRig.Layer;
        Destroy(box.GetComponent<Collider>());
        box.transform.SetParent(parent, false);
        box.transform.localPosition = position;
        box.transform.localScale = size;
        Renderer renderer = box.GetComponent<Renderer>();
        renderer.sharedMaterial = VRMaterials.Unlit(color);
        return renderer;
    }

    static void Disc(Transform parent, Vector3 position, float diameter, float thickness, Color color)
    {
        GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.layer = VRRig.Layer;
        Destroy(disc.GetComponent<Collider>());
        disc.transform.SetParent(parent, false);
        disc.transform.localPosition = position;
        disc.transform.localScale = new Vector3(diameter, thickness * 0.5f, diameter);
        disc.GetComponent<Renderer>().sharedMaterial = VRMaterials.Unlit(color);
    }

    async void Load()
    {
        _loading = true;
        _path = null;

        foreach (VRModelLibrary.ModelInfo info in VRModelLibrary.All)
        {
            if (info.Key == ModelKey)
            {
                _path = info.Path;
                break;
            }
        }

        if (_path == null)
        {
            _failed = true;
            Debug.Log("[DCGO VR] Opponent avatar: no '" + ModelKey + "' model in _DSTSModels.");
            return;
        }

        Transform holder = await VRFieldModels.LoadPreview(_path, _figure);

        if (this == null || _root == null)
        {
            VRFieldModels.ReleasePreview(_path, holder);
            return;
        }

        if (holder == null)
        {
            _failed = true;
            return;
        }

        _model = holder;
        _height = AvatarHeight * VRModelTuning.Get(ModelKey);
        // Normalize caps very wide models by the arena width rule; she is a person, height decides.
        VRFieldModels.Normalize(holder, _height, out _);
        float grow = Mathf.Max(1f, _height / Mathf.Max(0.01f, MeasureHeight(holder)));
        holder.localScale *= grow;
        holder.localPosition *= grow; // keep the feet-centring offset in proportion
        PlayIdle(holder.GetComponentInChildren<Animation>(true));
        _loading = false;
    }

    // NPC files hold body clips (fn = standing idle) plus face/mouth clips: loop the body idle.
    static void PlayIdle(Animation anim)
    {
        if (anim == null)
        {
            return;
        }

        anim.playAutomatically = false;
        anim.cullingType = AnimationCullingType.AlwaysAnimate;
        string idle = null;

        foreach (AnimationState state in anim)
        {
            string name = state.name;

            if (name.Contains("_fn01") || name.Contains("_bn01"))
            {
                idle = name;
                break;
            }
        }

        if (idle != null)
        {
            anim[idle].wrapMode = WrapMode.Loop;
            anim.Play(idle);
        }
    }

    void LateUpdate()
    {
        GameContext context = BoardObserver.Context;
        bool show = VRSettings.OpponentAvatar && VRSettings.Models && _arena != null && _head != null && context != null && context.Opponent != null;

        if (show && _root == null)
        {
            Build();
        }

        if (_root != null && _root.gameObject.activeSelf != show)
        {
            _root.gameObject.SetActive(show);
        }

        Root = show ? _root : null;
        Model = show ? _model : null;
        Height = _height;

        if (!show)
        {
            return;
        }

        if (_model == null && !_loading && !_failed)
        {
            Load();
        }

        // Behind the opponent's field, eyes level with yours (never sunk below their mat).
        Vector3 eye = _arena.InverseTransformPoint(_head.position);
        Vector3 spot = VRFieldModels.OpponentAvatarSpot;
        spot.y = Mathf.Max(VRFieldModels.PodiumFloor - _height * WaistRatio + 0.6f, eye.y - _height * EyeRatio);
        _root.localPosition = _shown >= 0 ? Vector3.Lerp(_root.localPosition, spot, 1f - Mathf.Exp(-3f * Time.unscaledDeltaTime)) : spot;

        // Podium frame: white when pointed at, pulsing gold when she can be attacked.
        bool hovered = VRArenaTarget.Hovered != null && VRArenaTarget.Hovered.OpponentPlayer;
        Board3D board = VRSession.Instance != null ? VRSession.Instance.Board : null;
        bool target = board != null && board.OpponentIsTarget;
        Material rim = hovered ? _rimHover : target ? (Mathf.Sin(Time.unscaledTime * 6f) > 0f ? _rimTarget : _rimTargetDim) : _rimIdle;

        foreach (Renderer renderer in _rim)
        {
            if (renderer.sharedMaterial != rim)
            {
                renderer.sharedMaterial = rim;
            }
        }

        bool shieldGlow = hovered || target;

        if (_shieldGlow.gameObject.activeSelf != shieldGlow)
        {
            _shieldGlow.gameObject.SetActive(shieldGlow);
        }

        if (shieldGlow && _shieldGlow.sharedMaterial != rim)
        {
            _shieldGlow.sharedMaterial = rim;
        }

        // Above her head: the label just over it, the shields on top.
        _caption.transform.localPosition = new Vector3(0f, _height + 0.3f, 0.25f);
        _shieldRow.localPosition = new Vector3(0f, _height + 0.48f, 0.25f);
        _shown = context.Opponent.SecurityCards.Count;
        UpdateShields(context.Opponent.SecurityCards);

        // Upper chest, in front of her body (she stands at z 0.25; -Z is towards you). Her field was
        // lowered (VRFieldModels.TheirElevation) so her Digimon stay below it.
        _handRow.localPosition = new Vector3(0f, _height * 0.75f, 0.25f - 0.85f);
        UpdateHand(context.Opponent.HandCards.Count);
    }

    // ---------------------------------------------------------------- opponent hand

    const float HandCardW = 0.3f;
    const float HandCardH = 0.42f;
    const float HandMaxWidth = 2.2f;

    void UpdateHand(int n)
    {
        MaterialPropertyBlock back = BackBlock();

        // The game's card back may become available later: switch the cards over once it does.
        if (back != null && !_handBackApplied)
        {
            _handBackApplied = true;

            foreach (Renderer slot in _handSlots)
            {
                slot.sharedMaterial = _cardMaterial != null ? _cardMaterial : _cardMaterial = VRMaterials.Unlit(Color.white);
                slot.SetPropertyBlock(back);
            }
        }

        if (n == _handCount)
        {
            return;
        }

        _handCount = n;
        _handCaption.text = "HAND " + n;

        while (_handSlots.Count < n)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "HandCard";
            quad.layer = VRRig.Layer;
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(_handRow, false);
            Renderer renderer = quad.GetComponent<Renderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            if (back != null)
            {
                renderer.sharedMaterial = _cardMaterial != null ? _cardMaterial : _cardMaterial = VRMaterials.Unlit(Color.white);
                renderer.SetPropertyBlock(back);
            }
            else
            {
                renderer.sharedMaterial = _handFallback != null ? _handFallback : _handFallback = VRMaterials.Unlit(new Color(0.12f, 0.16f, 0.32f));
            }

            _handSlots.Add(renderer);
        }

        // A gentle fan; cards squeeze together when she holds many.
        float spread = Mathf.Min(0.2f, HandMaxWidth / Mathf.Max(1, n));

        for (int i = 0; i < _handSlots.Count; i++)
        {
            Renderer slot = _handSlots[i];
            bool used = i < n;
            slot.gameObject.SetActive(used);

            if (!used)
            {
                continue;
            }

            float c = i - (n - 1) * 0.5f;
            slot.transform.localPosition = new Vector3(c * spread, -Mathf.Abs(c) * spread * 0.12f, -i * 0.003f);
            slot.transform.localRotation = Quaternion.Euler(0f, 0f, c * Mathf.Min(5f, 40f / Mathf.Max(1, n))); // seen from you: right cards lean right
            slot.transform.localScale = new Vector3(HandCardW, HandCardH, 1f);
        }
    }

    // The game's card back (same as the cards on the board).
    MaterialPropertyBlock BackBlock()
    {
        if (_backBlock != null)
        {
            return _backBlock;
        }

        ContinuousController controller = ContinuousController.instance;
        Sprite sprite = controller != null ? controller.ReverseCard : null;

        if (sprite == null)
        {
            return null;
        }

        Texture2D texture = sprite.texture;
        Rect rect = sprite.textureRect;
        Vector4 st = new Vector4(rect.width / texture.width, rect.height / texture.height, rect.x / texture.width, rect.y / texture.height);
        _backBlock = new MaterialPropertyBlock();
        _backBlock.SetTexture("_BaseMap", texture);
        _backBlock.SetTexture("_MainTex", texture);
        _backBlock.SetVector("_BaseMap_ST", st);
        _backBlock.SetVector("_MainTex_ST", st);
        return _backBlock;
    }

    // ---------------------------------------------------------------- security shields

    const float ShieldW = 0.46f;
    const float ShieldH = 0.56f;
    const float CardW = 0.46f;
    const float CardH = 0.64f;
    const float Spacing = 0.54f;
    const int PerRow = 8;

    void UpdateShields(System.Collections.Generic.List<CardSource> security)
    {
        // Rebuild only when the security changes (count, cards or which are face up).
        int key = security.Count;

        foreach (CardSource card in security)
        {
            key = key * 31 + (card != null ? card.GetHashCode() : 0) * (card != null && card.IsFaceUp ? 2 : 1);
        }

        if (key == _securityKey)
        {
            return;
        }

        _securityKey = key;
        int n = security.Count;

        while (_slots.Count < n)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Security";
            quad.layer = VRRig.Layer;
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(_shieldRow, false);
            _slots.Add(quad.GetComponent<Renderer>());
            _slotCards.Add(null);
        }

        int rows = Mathf.Max(1, Mathf.CeilToInt(n / (float)PerRow));

        // Laser box + glow plate around the whole row (a bit generous so it is easy to hit).
        float rowWidth = Mathf.Max(0.8f, Mathf.Min(PerRow, Mathf.Max(1, n)) * Spacing + 0.16f);
        float rowHeight = rows * (CardH + 0.08f) + 0.12f;
        Vector3 rowCenter = new Vector3(0f, rowHeight * 0.5f - 0.06f, 0f);
        _shieldHit.center = rowCenter;
        _shieldHit.size = new Vector3(rowWidth, rowHeight, 0.3f);
        _shieldGlow.transform.localPosition = rowCenter + new Vector3(0f, 0f, 0.02f); // just behind the shields
        _shieldGlow.transform.localScale = new Vector3(rowWidth, rowHeight, 1f);

        for (int i = 0; i < _slots.Count; i++)
        {
            Renderer slot = _slots[i];
            bool used = i < n;
            slot.gameObject.SetActive(used);

            if (!used)
            {
                _slotCards[i] = null;
                continue;
            }

            CardSource card = security[i];
            bool faceUp = card != null && card.IsFaceUp;
            int row = i / PerRow;
            int inRow = Mathf.Min(PerRow, n - row * PerRow);
            int column = i % PerRow;
            float x = (column - (inRow - 1) * 0.5f) * Spacing;
            float y = (rows - 1 - row) * (CardH + 0.08f) + CardH * 0.5f;
            slot.transform.localPosition = new Vector3(x, y, 0f);
            slot.transform.localScale = faceUp ? new Vector3(CardW, CardH, 1f) : new Vector3(ShieldW, ShieldH, 1f);

            if (faceUp)
            {
                slot.sharedMaterial = _cardMaterial != null ? _cardMaterial : _cardMaterial = VRMaterials.Unlit(Color.white);

                if (_slotCards[i] != card)
                {
                    _slotCards[i] = card;
                    LoadArt(card, slot);
                }
            }
            else
            {
                _slotCards[i] = null;
                slot.SetPropertyBlock(null);
                slot.sharedMaterial = ShieldMaterial();
            }
        }
    }

    async void LoadArt(CardSource card, Renderer target)
    {
        try
        {
            Sprite sprite = card.CardSprite != null ? card.CardSprite : await card.GetCardSprite();

            if (sprite == null || target == null || !target.gameObject.activeSelf)
            {
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
        catch (System.Exception exception)
        {
            Debug.LogWarning("[DCGO VR] Security card art failed: " + exception.Message);
        }
    }

    // A heater shield drawn once into a texture: red body, light rim, a gold diamond emblem.
    static Material ShieldMaterial()
    {
        if (_shieldMaterial != null)
        {
            return _shieldMaterial;
        }

        const int w = 128;
        const int h = 156;
        Texture2D texture = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        Color32[] pixels = new Color32[w * h];
        Color body = new Color(0.72f, 0.12f, 0.12f);
        Color rim = new Color(1f, 0.82f, 0.75f);
        Color gold = new Color(1f, 0.82f, 0.25f);

        for (int py = 0; py < h; py++)
        {
            for (int px = 0; px < w; px++)
            {
                float u = (px + 0.5f) / w * 2f - 1f;      // -1..1
                float v = (py + 0.5f) / h;                // 0 bottom .. 1 top
                float half = HalfWidth(v);
                float inner = HalfWidth(Mathf.Clamp01((v - 0.06f) / 0.88f)) - 0.12f;
                Color c = Color.clear;

                if (Mathf.Abs(u) <= half && v <= 0.97f)
                {
                    bool inside = Mathf.Abs(u) <= inner && v > 0.06f && v < 0.91f;
                    c = inside ? Color.Lerp(body, body * 0.7f, 1f - v) : rim;
                    float diamond = Mathf.Abs(u) / 0.32f + Mathf.Abs(v - 0.55f) / 0.24f;

                    if (inside && diamond < 1f)
                    {
                        c = diamond < 0.7f ? gold : Color.Lerp(gold, body, 0.5f);
                    }

                    c.a = 1f;
                }

                pixels[py * w + px] = c;
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(true, true);
        _shieldMaterial = VRMaterials.Transparent(Color.white);
        _shieldMaterial.SetTexture("_BaseMap", texture);
        _shieldMaterial.SetTexture("_MainTex", texture);
        return _shieldMaterial;
    }

    // Shield outline: straight sides at the top, curving to a point at the bottom.
    static float HalfWidth(float v) => v >= 0.45f ? 0.95f : 0.95f * Mathf.Sqrt(Mathf.Clamp01(v / 0.45f));

    static float MeasureHeight(Transform model)
    {
        float top = float.MinValue;
        float bottom = float.MaxValue;

        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
        {
            if (renderer.enabled)
            {
                top = Mathf.Max(top, renderer.bounds.max.y);
                bottom = Mathf.Min(bottom, renderer.bounds.min.y);
            }
        }

        float scale = Mathf.Max(1e-4f, model.parent != null ? model.parent.lossyScale.y : 1f);
        return top > bottom ? (top - bottom) / scale : 0f;
    }

    void OnDestroy()
    {
        if (_model != null)
        {
            VRFieldModels.ReleasePreview(_path, _model);
        }

        if (_root != null)
        {
            Destroy(_root.gameObject);
        }

    }
}
#endif
