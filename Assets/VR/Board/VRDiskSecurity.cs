#if DCGO_VR
using System.Collections.Generic;
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// Your security as a small hologram floating over the duel disk: one glowing blue shield per
// security card (a face-up / revealed card shows its art instead), with "SECURITY n" under the
// row. Stays upright and turns to face you whatever your arm does. Shown while the disk is visible.
public class VRDiskSecurity : MonoBehaviour
{
    const float ShieldW = 0.026f;
    const float ShieldH = 0.032f;
    const float CardW = 0.026f;
    const float CardH = 0.036f;
    const float Spacing = 0.031f;
    const int PerRow = 5;
    static readonly Vector3 Anchor = new Vector3(-0.16f, 0.03f, 0.11f); // disk space: top-left, over the far edge of the blade (clear of the console text)

    static readonly Color HoloColor = new Color(0.35f, 0.85f, 1f);

    Transform _row;
    VRLabel _caption;
    readonly List<Renderer> _slots = new List<Renderer>();
    readonly List<CardSource> _slotCards = new List<CardSource>();
    readonly List<bool> _hasArt = new List<bool>();
    readonly HashSet<CardSource> _reported = new HashSet<CardSource>();
    readonly Dictionary<CardSource, int> _reloads = new Dictionary<CardSource, int>();
    readonly Dictionary<CardSource, float> _nextReload = new Dictionary<CardSource, float>();

    static readonly System.Reflection.FieldInfo EntityField =
        typeof(CardSource).GetField("_cEntity_Base", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    float _nextArtTry;
    MaterialPropertyBlock _block;   // created on first use: Unity forbids creating it in a field initializer
    int _key = int.MinValue;

    static Material _shieldMaterial;
    static Material _cardMaterial;

    public static VRDiskSecurity Create(Transform parent)
    {
        GameObject go = new GameObject("DiskSecurity") { layer = VRRig.Layer };
        go.transform.SetParent(parent, false);
        return go.AddComponent<VRDiskSecurity>();
    }

    void LateUpdate()
    {
        VRSession session = VRSession.Instance;
        VRDuelDisk disk = session != null ? session.DuelDisk : null;
        GameContext context = BoardObserver.Context;
        bool show = disk != null && disk.isActiveAndEnabled && context != null && context.You != null;

        if (!show)
        {
            if (_row != null && _row.gameObject.activeSelf)
            {
                _row.gameObject.SetActive(false);
            }

            return;
        }

        if (_row == null)
        {
            Build();
        }

        if (!_row.gameObject.activeSelf)
        {
            _row.gameObject.SetActive(true);
        }

        Transform head = session.Rig != null && session.Rig.Camera != null ? session.Rig.Camera.transform : null;
        Vector3 position = disk.transform.TransformPoint(Anchor);
        position.y += Mathf.Sin(Time.unscaledTime * 1.7f) * 0.002f; // gentle hover
        Vector3 away = head != null ? position - head.position : disk.transform.forward;

        if (away.sqrMagnitude > 1e-6f)
        {
            // Quads and labels face -Z: point +Z away from the eyes, keep it upright.
            _row.SetPositionAndRotation(position, Quaternion.LookRotation(away.normalized, Vector3.up));
        }

        UpdateShields(context.You.SecurityCards);
        RetryArt(session.Board);
    }

    // Face-up security cards whose art did not load directly: copy it from the board's own card
    // object (Card3D loads the front of every face-up card, even while the field is hidden).
    void RetryArt(Board3D board)
    {
        if (board == null || Time.unscaledTime < _nextArtTry)
        {
            return;
        }

        _nextArtTry = Time.unscaledTime + 0.3f;

        for (int i = 0; i < _slots.Count; i++)
        {
            if (_hasArt[i] || _slotCards[i] == null || !_slots[i].gameObject.activeSelf)
            {
                continue;
            }

            Sprite sprite = null;

            try
            {
                sprite = _slotCards[i].CardSprite; // cached by the game once it has loaded the image
            }
            catch (System.Exception)
            {
                sprite = null;
            }

            if (Usable(sprite))
            {
                ApplySprite(_slots[i], sprite);
                _hasArt[i] = true;
                continue;
            }

            Card3D card = board.FindCard(_slotCards[i]);

            if (card != null && card.CopyFrontTo(_slots[i]))
            {
                _hasArt[i] = true;
                continue;
            }

            TryReload(_slotCards[i]);
        }
    }

    void Build()
    {
        _row = new GameObject("SecurityHologram") { layer = VRRig.Layer }.transform;
        _row.SetParent(transform, false);
        _caption = VRLabel.Create(_row, new Vector3(0f, -0.009f, 0f), Quaternion.identity, new Vector2(0.09f, 0.014f), "SECURITY", HoloColor);
    }

    void UpdateShields(List<CardSource> security)
    {
        // Rebuild only when the security changes (count, cards or which are face up).
        int key = security.Count;

        foreach (CardSource card in security)
        {
            key = key * 31 + (card != null ? card.GetHashCode() : 0) * (card != null && card.IsFaceUp ? 2 : 1);
        }

        if (key == _key)
        {
            return;
        }

        _key = key;
        int n = security.Count;
        _caption.text = "SECURITY " + n;

        while (_slots.Count < n)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Shield";
            quad.layer = VRRig.Layer;
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(_row, false);
            Renderer renderer = quad.GetComponent<Renderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _slots.Add(renderer);
            _slotCards.Add(null);
            _hasArt.Add(false);
        }

        int rows = Mathf.Max(1, Mathf.CeilToInt(n / (float)PerRow));

        for (int i = 0; i < _slots.Count; i++)
        {
            Renderer slot = _slots[i];
            bool used = i < n;
            slot.gameObject.SetActive(used);

            if (!used)
            {
                _slotCards[i] = null;
                _hasArt[i] = false;
                continue;
            }

            CardSource card = security[i];
            bool faceUp = card != null && card.IsFaceUp;
            int row = i / PerRow;
            int inRow = Mathf.Min(PerRow, n - row * PerRow);
            int column = i % PerRow;
            float x = (column - (inRow - 1) * 0.5f) * Spacing;
            float y = (rows - 1 - row) * (CardH + 0.005f) + CardH * 0.5f;
            slot.transform.localPosition = new Vector3(x, y, 0f);
            slot.transform.localScale = faceUp ? new Vector3(CardW, CardH, 1f) : new Vector3(ShieldW, ShieldH, 1f);

            if (faceUp)
            {
                slot.sharedMaterial = _cardMaterial != null ? _cardMaterial : _cardMaterial = VRMaterials.Unlit(Color.white);

                if (_slotCards[i] != card)
                {
                    _slotCards[i] = card;
                    _hasArt[i] = false;
                    slot.SetPropertyBlock(null);
                    LoadArt(card, slot, i);
                }
            }
            else
            {
                _slotCards[i] = null;
                _hasArt[i] = false;
                slot.SetPropertyBlock(null);
                slot.sharedMaterial = ShieldMaterial();
            }
        }
    }

    // The game marks a card's image as "load started" before loading; if that load failed, the
    // card never gets an image again (GetCardSprite returns null). Clear the flag and load again
    // (up to 3 times, 2 s apart); report the card and its image file once if it keeps failing.
    async void TryReload(CardSource card)
    {
        if (!_nextReload.TryGetValue(card, out float next))
        {
            _nextReload[card] = Time.unscaledTime + 2f; // give the normal load a moment first
            return;
        }

        _reloads.TryGetValue(card, out int tries);

        if (Time.unscaledTime < next || tries >= 3)
        {
            return;
        }

        _reloads[card] = tries + 1;
        _nextReload[card] = Time.unscaledTime + 2f;
        CEntity_Base entity = EntityField != null ? EntityField.GetValue(card) as CEntity_Base : null;
        string reason;

        if (entity == null)
        {
            reason = "the card has no card data";
        }
        else
        {
            try
            {
                if (entity.CardSprite != null && entity.CardSprite.texture == null)
                {
                    entity.CardSprite = null; // stale: its texture was unloaded
                }

                entity.HasLoadStarted = false;
                Sprite sprite = await entity.GetCardSprite();

                if (Usable(sprite))
                {
                    return; // RetryArt picks it up from CardSprite
                }

                reason = $"image file '{entity.CardSpriteName}' did not load";
            }
            catch (System.Exception exception)
            {
                reason = $"image file '{entity.CardSpriteName}': {exception.GetType().Name}: {exception.Message}";
            }
        }

        if (tries + 1 >= 3 && _reported.Add(card))
        {
            Debug.LogWarning("[DCGO VR] Disk security: face-up card has no image after 3 tries - " + reason);
        }
    }

    async void LoadArt(CardSource card, Renderer target, int index)
    {
        Sprite sprite = null;

        try
        {
            sprite = Usable(card.CardSprite) ? card.CardSprite : await card.GetCardSprite();
        }
        catch (System.Exception exception)
        {
            // RetryArt keeps trying (cached sprite / the board's card); report the cause once per card.
            if (_reported.Add(card))
            {
                string id = "?";

                try
                {
                    id = card.CardID;
                }
                catch (System.Exception)
                {
                    id = "(no card data)";
                }

                Debug.LogWarning($"[DCGO VR] Disk security: image for face-up card {id} not loaded yet ({exception.GetType().Name}: {exception.Message}); retrying from the board card.\n{exception}");
            }
        }

        if (!Usable(sprite) || this == null || target == null || index >= _slotCards.Count || _slotCards[index] != card)
        {
            return;
        }

        ApplySprite(target, sprite);
        _hasArt[index] = true;
    }

    // A cached sprite can outlive its texture (the game unloads card images): treat that as "no image".
    static bool Usable(Sprite sprite) => sprite != null && sprite.texture != null;

    void ApplySprite(Renderer target, Sprite sprite)
    {
        if (!Usable(sprite) || target == null)
        {
            return;
        }

        if (_block == null)
        {
            _block = new MaterialPropertyBlock();
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

    // Hologram shield drawn once into a texture: see-through blue body, bright cyan rim and
    // emblem, faint horizontal scan lines.
    static Material ShieldMaterial()
    {
        if (_shieldMaterial != null)
        {
            return _shieldMaterial;
        }

        const int w = 96;
        const int h = 118;
        Texture2D texture = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        Color32[] pixels = new Color32[w * h];
        Color body = new Color(0.1f, 0.45f, 0.85f, 0.55f);
        Color rim = new Color(0.6f, 0.95f, 1f, 0.95f);
        Color emblem = new Color(0.75f, 1f, 1f, 0.95f);

        for (int py = 0; py < h; py++)
        {
            for (int px = 0; px < w; px++)
            {
                float u = (px + 0.5f) / w * 2f - 1f;      // -1..1
                float v = (py + 0.5f) / h;                // 0 bottom .. 1 top
                float half = HalfWidth(v);
                float inner = HalfWidth(Mathf.Clamp01((v - 0.07f) / 0.86f)) - 0.14f;
                Color c = Color.clear;

                if (Mathf.Abs(u) <= half && v <= 0.97f)
                {
                    bool inside = Mathf.Abs(u) <= inner && v > 0.07f && v < 0.9f;
                    c = inside ? body : rim;
                    float diamond = Mathf.Abs(u) / 0.32f + Mathf.Abs(v - 0.55f) / 0.24f;

                    if (inside && diamond < 1f)
                    {
                        c = diamond < 0.7f ? emblem : Color.Lerp(emblem, body, 0.5f);
                    }

                    if (inside && py % 4 == 0)
                    {
                        c.a *= 0.6f; // scan lines
                    }
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
}
#endif
