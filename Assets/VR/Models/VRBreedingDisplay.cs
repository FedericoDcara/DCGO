#if DCGO_VR
using System.Collections.Generic;
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// Breeding area Digimon in the arena (purely visual, reads the board only):
//  - Opponent: their breeding Digimon stands beside their podium on your right of it (her
//    option cards are on the left), on a small red platform, idling and facing you, with its
//    card standing upright behind it. Without the avatar it stands past the right end of their mat.
//  - You: a small hologram of your breeding Digimon floats on a projector disc just past the
//    tip of your duel disk blade (left of the disk). It follows your arm, stays upright and
//    turns to face you. Shown while the duel disk is visible.
// Cards without a 3D model show only the card (opponent) / nothing (your hologram).
public class VRBreedingDisplay : MonoBehaviour
{
    const float CardW = 0.5f;
    const float CardH = 0.7f;
    const float TheirCardScale = 1.6f;         // same size as the opponent's option cards
    const float TheirModelHeight = 1.1f;
    const float TheirLift = 0.3f;              // platform height above the podium top (clear of their field)
    const float TheirOut = 0.45f;              // extra distance out to the side of the podium
    const float HoloHeight = 0.17f;
    static readonly Vector3 DiskAnchor = new Vector3(-0.33f, 0.006f, -0.02f); // disk space, past the blade tip

    static readonly Color YourColor = new Color(0.25f, 0.8f, 1f);
    static readonly Color TheirColor = new Color(1f, 0.35f, 0.3f);

    sealed class Shown
    {
        public Permanent Permanent;
        public CardSource Top;
        public string Path;
        public Transform Root;
        public Transform Stand;     // the model stands here
        public Transform Model;
        public VRArenaTarget Target;
        public int Version;
    }

    /// <summary>Your breeding hologram (null when none): a drop zone for digivolving onto it (Board3D).</summary>
    public static Transform YourAnchor { get; private set; }
    /// <summary>The projector disc under your hologram (lit while it is a drop zone).</summary>
    public static Renderer YourDisc { get; private set; }
    public static Color YourDiscColor => YourColor * 0.9f;

    Transform _arena;
    readonly Shown _yours = new Shown();
    readonly Shown _theirs = new Shown();
    readonly Dictionary<Material, Material> _holo = new Dictionary<Material, Material>();

    static Material _faceMaterial;
    static Material _theirFrame;
    static Material _theirPad;
    static Material _yourPad;
    static Material _holoBase;
    static bool _holoMissing;

    public static VRBreedingDisplay Create(Transform parent, Transform arena)
    {
        GameObject go = new GameObject("BreedingDisplay") { layer = VRRig.Layer };
        go.transform.SetParent(parent, false);
        VRBreedingDisplay display = go.AddComponent<VRBreedingDisplay>();
        display._arena = arena;
        return display;
    }

    void LateUpdate()
    {
        GameContext context = BoardObserver.Context;
        bool show = VRSettings.Models && _arena != null && context != null && context.You != null && context.Opponent != null;

        VRSession session = VRSession.Instance;
        VRDuelDisk disk = session != null ? session.DuelDisk : null;
        bool diskOn = disk != null && disk.isActiveAndEnabled;

        Sync(_theirs, show ? Breeding(context.Opponent) : null, false);
        Sync(_yours, show && diskOn ? Breeding(context.You) : null, true);

        if (_theirs.Root != null)
        {
            PlaceTheirs();
        }

        if (_yours.Root != null && diskOn)
        {
            PlaceYours(disk, session.Rig != null && session.Rig.Camera != null ? session.Rig.Camera.transform : null);
        }
    }

    static Permanent Breeding(Player player)
    {
        try
        {
            foreach (Permanent permanent in player.GetBreedingAreaPermanents())
            {
                if (permanent != null && permanent.TopCard != null && !permanent.TopCard.IsFlipped)
                {
                    return permanent;
                }
            }
        }
        catch (System.Exception)
        {
            // board mid-update: try again next frame
        }

        return null;
    }

    // (Re)build when the breeding Digimon appears, digivolves (new top card) or leaves.
    void Sync(Shown shown, Permanent permanent, bool yours)
    {
        CardSource top = permanent != null ? permanent.TopCard : null;
        shown.Permanent = permanent;

        if (shown.Target != null)
        {
            shown.Target.Permanent = permanent;
        }

        if (top == shown.Top && (top == null || shown.Root != null))
        {
            return;
        }

        Clear(shown, yours);
        shown.Top = top;

        if (top != null)
        {
            Build(shown, permanent, yours);
        }
    }

    void Build(Shown shown, Permanent permanent, bool yours)
    {
        string name = shown.Top.BaseENGCardNameFromEntity;
        shown.Root = new GameObject((yours ? "BreedingHologram " : "OpponentBreeding ") + name) { layer = VRRig.Layer }.transform;
        shown.Root.SetParent(yours ? transform : _arena, false);

        shown.Stand = new GameObject("Stand") { layer = VRRig.Layer }.transform;
        shown.Stand.SetParent(shown.Root, false);

        if (yours)
        {
            // Projector disc under the hologram.
            YourDisc = Disc(shown.Root, Vector3.zero, 0.075f, _yourPad != null ? _yourPad : _yourPad = VRMaterials.Unlit(YourColor * 0.9f));
            YourAnchor = shown.Root;

            // Pointable: the laser on the hologram shows the big card preview (A / X: its stack),
            // like any Digimon in the arena.
            BoxCollider hit = shown.Root.gameObject.AddComponent<BoxCollider>();
            hit.isTrigger = true;
            hit.center = new Vector3(0f, HoloHeight * 0.55f, 0f);
            hit.size = new Vector3(0.12f, HoloHeight * 1.2f, 0.12f);
            shown.Target = shown.Root.gameObject.AddComponent<VRArenaTarget>();
            shown.Target.Permanent = permanent;
        }
        else
        {
            Disc(shown.Root, Vector3.zero, 0.75f, _theirPad != null ? _theirPad : _theirPad = VRMaterials.Unlit(TheirColor * 0.55f));
            shown.Stand.localPosition = new Vector3(0f, 0.02f, -0.12f);
            shown.Stand.localRotation = Quaternion.Euler(0f, 180f, 0f); // models face +Z: turn to face you

            // The card standing upright behind the figure, facing you.
            Transform card = new GameObject("Card") { layer = VRRig.Layer }.transform;
            card.SetParent(shown.Root, false);
            card.localPosition = new Vector3(0f, 0.04f + CardH * TheirCardScale * 0.5f, 0.38f);
            card.localScale = Vector3.one * TheirCardScale;
            Quad(card, new Vector3(0f, 0f, 0.004f), new Vector2(CardW + 0.05f, CardH + 0.05f), _theirFrame != null ? _theirFrame : _theirFrame = VRMaterials.Unlit(TheirColor * 0.8f));
            Renderer face = Quad(card, Vector3.zero, new Vector2(CardW, CardH), _faceMaterial != null ? _faceMaterial : _faceMaterial = VRMaterials.Unlit(Color.white));
            LoadArt(shown.Top, face);

            VRLabel.Create(card, new Vector3(0f, CardH * 0.5f + 0.09f, 0f), Quaternion.identity, new Vector2(0.5f, 0.09f), "BREEDING", new Color(1f, 0.8f, 0.75f), new Color(0.14f, 0.04f, 0.05f));

            // Pointable like the card on the desk (stack viewer, effects).
            BoxCollider hit = card.gameObject.AddComponent<BoxCollider>();
            hit.isTrigger = true;
            hit.size = new Vector3(CardW + 0.05f, CardH + 0.05f, 0.05f);
            shown.Target = card.gameObject.AddComponent<VRArenaTarget>();
            shown.Target.Permanent = permanent;
        }

        shown.Path = VRModelLibrary.Resolve(shown.Top);

        if (shown.Path != null)
        {
            Load(shown, yours);
        }
        else if (yours)
        {
            HoloCard(shown); // no 3D model for this card (e.g. Pinamon): project the card instead
        }
    }

    static Material _holoFrame;

    // Small projected card standing on the disc, facing you (+Z of the root points at your head).
    void HoloCard(Shown shown)
    {
        if (shown.Stand == null)
        {
            return;
        }

        const float w = 0.07f;
        const float h = w * CardH / CardW;
        Transform card = new GameObject("HoloCard") { layer = VRRig.Layer }.transform;
        card.SetParent(shown.Stand, false);
        card.localPosition = new Vector3(0f, h * 0.5f + 0.008f, 0f);
        card.localRotation = Quaternion.Euler(0f, 180f, 0f); // quads face -Z
        Quad(card, new Vector3(0f, 0f, 0.001f), new Vector2(w + 0.008f, h + 0.008f),
            _holoFrame != null ? _holoFrame : _holoFrame = VRMaterials.Transparent(new Color(0.3f, 0.85f, 1f, 0.6f)));
        Renderer face = Quad(card, Vector3.zero, new Vector2(w, h), _faceMaterial != null ? _faceMaterial : _faceMaterial = VRMaterials.Unlit(Color.white));
        LoadArt(shown.Top, face);
    }

    async void Load(Shown shown, bool yours)
    {
        int version = ++shown.Version;
        string path = shown.Path;
        Transform holder = await VRFieldModels.LoadPreview(path, shown.Stand);

        if (this == null || shown.Version != version || shown.Stand == null)
        {
            VRFieldModels.ReleasePreview(path, holder);
            return;
        }

        if (holder == null)
        {
            if (yours)
            {
                HoloCard(shown); // model failed to load: project the card instead
            }

            return;
        }

        shown.Model = holder;
        float tune = VRModelTuning.Get(VRModelTuning.KeyOf(path));
        VRFieldModels.Normalize(holder, (yours ? HoloHeight : TheirModelHeight) * tune, out _);

        if (yours)
        {
            MakeHologram(holder);
        }

        PlayIdle(holder.GetComponentInChildren<Animation>(true));
    }

    void Clear(Shown shown, bool yours)
    {
        shown.Version++;

        if (shown.Model != null)
        {
            VRFieldModels.ReleasePreview(shown.Path, shown.Model);
        }

        if (shown.Root != null)
        {
            Destroy(shown.Root.gameObject);
        }

        shown.Root = null;
        shown.Stand = null;
        shown.Model = null;
        shown.Target = null;
        shown.Top = null;
        shown.Path = null;

        if (yours)
        {
            ClearHologramMaterials();
            YourAnchor = null;
            YourDisc = null;
        }
    }

    void PlaceTheirs()
    {
        Transform avatar = VROpponentAvatar.Root;
        Transform root = _theirs.Root;

        if (avatar != null && avatar.gameObject.activeInHierarchy)
        {
            // Mirror of her option cards (they stand on her right = your left).
            if (root.parent != avatar)
            {
                root.SetParent(avatar, false);
            }

            Vector3 option = VROpponentAvatar.OptionSpot(0);
            // Raised to about her head height and a bit further out, so it stands above their field.
            root.localPosition = new Vector3(-option.x + TheirOut, option.y + TheirLift, option.z);
        }
        else
        {
            if (root.parent != _arena)
            {
                root.SetParent(_arena, false);
            }

            root.localPosition = VRFieldModels.PreviewSpot(false) + new Vector3(4.2f, 1.2f, 0.4f);
        }

        root.localRotation = Quaternion.identity;
        root.localScale = Vector3.one;
    }

    void PlaceYours(VRDuelDisk disk, Transform head)
    {
        Vector3 position = disk.transform.TransformPoint(DiskAnchor);
        Vector3 toHead = head != null ? head.position - position : Vector3.back;
        toHead.y = 0f;
        Quaternion face = toHead.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(toHead.normalized, Vector3.up) : Quaternion.identity;

        _yours.Root.SetPositionAndRotation(position, face); // upright whatever the arm does
        _yours.Root.localScale = Vector3.one;
        _yours.Stand.localPosition = new Vector3(0f, 0.012f + Mathf.Sin(Time.unscaledTime * 2f) * 0.004f, 0f); // gentle hover
    }

    // ---------------------------------------------------------------- hologram

    static Material HoloBase
    {
        get
        {
            if (_holoBase == null && !_holoMissing)
            {
                _holoBase = Resources.Load<Material>("DSTS/DSTS_HologramBase");
                _holoMissing = _holoBase == null;

                if (_holoMissing)
                {
                    Debug.LogWarning("[DCGO VR] Resources/DSTS/DSTS_HologramBase missing: breeding hologram uses normal materials.");
                }
            }

            return _holoBase;
        }
    }

    void MakeHologram(Transform holder)
    {
        Material holoBase = HoloBase;

        if (holoBase == null)
        {
            return;
        }

        foreach (Renderer renderer in holder.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled)
            {
                continue; // outline shells / eye shine stay hidden
            }

            Material[] materials = renderer.sharedMaterials;

            for (int i = 0; i < materials.Length; i++)
            {
                Material source = materials[i];

                if (source == null)
                {
                    continue;
                }

                if (!_holo.TryGetValue(source, out Material holo))
                {
                    holo = new Material(holoBase) { name = source.name + " (hologram)" };
                    Texture texture = source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap")
                                    : source.HasProperty("baseColorTexture") ? source.GetTexture("baseColorTexture") : null;

                    if (texture != null)
                    {
                        holo.SetTexture("_BaseMap", texture);
                    }

                    _holo[source] = holo;
                }

                materials[i] = holo;
            }

            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }

    void ClearHologramMaterials()
    {
        foreach (Material material in _holo.Values)
        {
            if (material != null)
            {
                Destroy(material);
            }
        }

        _holo.Clear();
    }

    // ---------------------------------------------------------------- helpers

    // Body idle (bn01 battle idle, else fn01 field idle) on loop.
    static void PlayIdle(Animation anim)
    {
        if (anim == null)
        {
            return;
        }

        anim.playAutomatically = false;
        anim.cullingType = AnimationCullingType.BasedOnRenderers;
        string idle = null;

        foreach (AnimationState state in anim)
        {
            if (state.name.EndsWith("_bn01") || state.name.Contains("_fn01"))
            {
                idle = state.name;
                break;
            }
        }

        if (idle != null)
        {
            anim[idle].wrapMode = WrapMode.Loop;
            anim.Play(idle);
        }
    }

    static Renderer Disc(Transform parent, Vector3 position, float diameter, Material material)
    {
        GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.layer = VRRig.Layer;
        Destroy(disc.GetComponent<Collider>());
        disc.transform.SetParent(parent, false);
        disc.transform.localPosition = position;
        disc.transform.localScale = new Vector3(diameter, 0.002f, diameter);
        Renderer renderer = disc.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return renderer;
    }

    static Renderer Quad(Transform parent, Vector3 position, Vector2 size, Material material)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.layer = VRRig.Layer;
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(parent, false);
        quad.transform.localPosition = position;
        quad.transform.localScale = new Vector3(size.x, size.y, 1f);
        Renderer renderer = quad.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return renderer;
    }

    static async void LoadArt(CardSource card, Renderer target)
    {
        try
        {
            Sprite sprite = card.CardSprite != null ? card.CardSprite : await card.GetCardSprite();

            if (sprite == null || target == null)
            {
                return;
            }

            Texture2D texture = sprite.texture;
            Rect rect = sprite.textureRect;
            Vector4 st = new Vector4(rect.width / texture.width, rect.height / texture.height, rect.x / texture.width, rect.y / texture.height);
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            block.SetTexture("_BaseMap", texture);
            block.SetTexture("_MainTex", texture);
            block.SetVector("_BaseMap_ST", st);
            block.SetVector("_MainTex_ST", st);
            target.SetPropertyBlock(block);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning("[DCGO VR] Breeding card art failed: " + exception.Message);
        }
    }

    void OnDestroy()
    {
        Clear(_yours, true);
        Clear(_theirs, false);
    }
}
#endif
