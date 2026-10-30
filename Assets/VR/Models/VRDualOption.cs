#if DCGO_VR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using S = VRMenuStyle;

// === DCGO-CUSTOM:vr ===
// Dual cards (Digimon + Option on one card) used for their Option side: the card's Digimon
// appears in the middle of the arena out of a light pillar and a magic circle, performs its
// special move (_bs01, else _bs02 / _ba02 / _ba01) and a banner above it shows the name of the
// Option side ("dualEffect"). Then it fades away. Several in a row are played one after another.
// Purely visual and read-only: it watches each player's ExecutingCards (where the game puts an
// Option while UseOptionClass resolves it) and never touches the rules or the network.
public class VRDualOption : MonoBehaviour
{
    const float Rise = 0.45f;
    const float MaxMove = 4.2f;
    const float Fade = 0.45f;

    static readonly Color YourColor = new Color(0.25f, 0.8f, 1f);
    static readonly Color TheirColor = new Color(1f, 0.35f, 0.3f);
    static FieldInfo _entityField;
    static FieldInfo _dualField;

    sealed class Show
    {
        public CardSource Card;
        public bool Yours;
        public string Name;
        public string Path;
        public Transform Root;
        public Transform Body;
        public Transform Model;
        public Transform Circle;
        public Transform Pillar;
        public Transform Banner;
        public Animation Anim;
        public float Start = -1f;
        public float MoveEnd;
        public bool Loading;
    }

    Transform _arena;
    readonly HashSet<CardSource> _seen = new HashSet<CardSource>();
    readonly List<CardSource> _stillThere = new List<CardSource>();
    readonly Queue<Show> _queue = new Queue<Show>();
    Show _current;
    Light _light;
    static Material _pillarMaterial;
    static Material _circleMaterial;

    public static VRDualOption Create(Transform parent, Transform arena)
    {
        GameObject go = new GameObject("DualOptionShow") { layer = VRRig.Layer };
        go.transform.SetParent(parent, false);
        VRDualOption show = go.AddComponent<VRDualOption>();
        show._arena = arena;
        return show;
    }

    void LateUpdate()
    {
        GameContext context = BoardObserver.Context;

        if (!VRSettings.Models || _arena == null || context == null || context.You == null || context.Opponent == null)
        {
            if (_current != null)
            {
                Finish(_current);
                _current = null;
            }

            _queue.Clear();
            _seen.Clear();
            return;
        }

        Watch(context.You, true);
        Watch(context.Opponent, false);

        if (_current == null && _queue.Count > 0)
        {
            _current = _queue.Dequeue();
            Begin(_current);
        }

        if (_current != null && !Animate(_current))
        {
            Finish(_current);
            _current = null;
        }
    }

    // A dual card entering ExecutingCards = its Option side is being used.
    void Watch(Player player, bool yours)
    {
        _stillThere.Clear();

        foreach (CardSource card in player.ExecutingCards)
        {
            if (card == null || !IsDual(card))
            {
                continue;
            }

            _stillThere.Add(card);

            if (_seen.Add(card))
            {
                string name = OptionName(card);
                Debug.Log($"[DCGO VR] Dual card option: {card.BaseENGCardNameFromEntity} -> {name}");
                _queue.Enqueue(new Show { Card = card, Yours = yours, Name = name, Path = VRModelLibrary.Resolve(card) });
            }
        }

        // Forget cards of this player that left (so the same card can be shown again next use).
        _seen.RemoveWhere(card => card != null && card.Owner == player && !_stillThere.Contains(card));
    }

    static bool IsDual(CardSource card)
    {
        try
        {
            return card.IsDualCard;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // The Option side's name (CEntity_Base.dualEffect); falls back to the card's extra base name.
    static string OptionName(CardSource card)
    {
        try
        {
            _entityField ??= typeof(CardSource).GetField("_cEntity_Base", BindingFlags.Instance | BindingFlags.NonPublic);
            object entity = _entityField?.GetValue(card);

            if (entity != null)
            {
                _dualField ??= entity.GetType().GetField("dualEffect", BindingFlags.Instance | BindingFlags.Public);
                string dual = _dualField?.GetValue(entity) as string;

                if (!string.IsNullOrEmpty(dual))
                {
                    return dual;
                }
            }

            List<string> names = card.BaseCardNames;
            return names != null && names.Count > 1 ? names[1] : card.BaseENGCardNameFromEntity;
        }
        catch (Exception)
        {
            return card.BaseENGCardNameFromEntity;
        }
    }

    // ---------------------------------------------------------------- the show

    async void Begin(Show show)
    {
        EnsureLight();
        show.Root = new GameObject("DualOption " + show.Name) { layer = VRRig.Layer }.transform;
        show.Root.SetParent(_arena, false);
        // Centre of the arena, between the two rows; theirs a little nearer their side.
        Vector3 mine = VRFieldModels.PreviewSpot(true);
        Vector3 theirs = VRFieldModels.PreviewSpot(false);
        Vector3 spot = Vector3.Lerp(mine, theirs, show.Yours ? 0.42f : 0.58f);
        spot.x = 0f;
        show.Root.localPosition = spot;
        show.Root.localRotation = Quaternion.Euler(0f, show.Yours ? 0f : 180f, 0f);

        float boost = show.Yours ? 1.1f : VRFieldModels.TheirBoost;
        Color side = show.Yours ? YourColor : TheirColor;
        show.Circle = Circle(show.Root, side, 1.6f * boost);
        show.Pillar = Pillar(show.Root, side, boost);

        show.Body = new GameObject("Body") { layer = VRRig.Layer }.transform;
        show.Body.SetParent(show.Root, false);
        show.Body.localScale = Vector3.one * 0.001f;
        show.Banner = Banner(show, boost);
        show.Start = Time.unscaledTime;
        show.MoveEnd = show.Start + Rise + 2.4f;

        if (show.Path == null)
        {
            return; // no model for this card: circle, pillar and the name
        }

        show.Loading = true;
        Transform holder = null;

        try
        {
            holder = await VRFieldModels.LoadPreview(show.Path, show.Body);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[DCGO VR] Dual option model failed: " + exception.Message);
        }

        show.Loading = false;

        if (holder == null || show.Root == null)
        {
            if (holder != null)
            {
                VRFieldModels.ReleasePreview(show.Path, holder);
            }

            return;
        }

        show.Model = holder;
        int level = 0;

        try
        {
            level = show.Card.Level;
        }
        catch (Exception)
        {
            level = 5;
        }

        VRFieldModels.Normalize(holder, VRFieldModels.TargetHeight(level) * 1.15f * boost * VRModelTuning.Get(VRModelTuning.KeyOf(show.Path)), out _);
        show.Anim = holder.GetComponentInChildren<Animation>(true);

        // Restart the timing from when the model is actually there.
        show.Start = Time.unscaledTime;
        show.MoveEnd = show.Start + Rise + 2.4f;

        if (show.Anim != null)
        {
            show.Anim.playAutomatically = false;
            show.Anim.cullingType = AnimationCullingType.AlwaysAnimate;
            AnimationClip special = VRFieldModels.Clip(show.Anim, "_bs01") ?? VRFieldModels.Clip(show.Anim, "_bs02")
                ?? VRFieldModels.Clip(show.Anim, "_ba02") ?? VRFieldModels.Clip(show.Anim, "_ba01");
            AnimationClip idle = VRFieldModels.Clip(show.Anim, "_bn01");

            if (idle != null)
            {
                show.Anim[idle.name].wrapMode = WrapMode.Loop;
                show.Anim.Play(idle.name);
            }

            if (special != null)
            {
                show.Anim[special.name].wrapMode = WrapMode.Once;
                show.Anim.CrossFade(special.name, 0.15f);

                if (idle != null)
                {
                    show.Anim.CrossFadeQueued(idle.name, 0.25f);
                }

                show.MoveEnd = show.Start + Rise + Mathf.Min(MaxMove, special.length);
            }
        }
    }

    // Returns false when the show is over.
    bool Animate(Show show)
    {
        if (show.Root == null)
        {
            return false;
        }

        float now = Time.unscaledTime;

        if (show.Loading && now - show.Start < 6f)
        {
            Spin(show, now);
            return true; // circle spins while the model loads
        }

        float t = now - show.Start;
        float end = Mathf.Max(show.MoveEnd, show.Start + Rise + 2.4f) + 0.4f;
        float rise = Mathf.SmoothStep(0f, 1f, t / Rise);
        float fade = Mathf.Clamp01((now - end) / Fade);
        float size = rise * (1f - fade);

        // The figure grows up out of the circle; at the end everything shrinks away.
        show.Body.localScale = Vector3.one * Mathf.Max(0.001f, size);
        show.Body.localPosition = Vector3.down * (1f - rise) * 0.3f;

        if (show.Banner != null)
        {
            float b = Mathf.SmoothStep(0f, 1f, (t - 0.15f) / 0.35f) * (1f - fade);
            show.Banner.localScale = new Vector3(Mathf.Max(0.001f, b), Mathf.Max(0.001f, b), 1f);
        }

        if (show.Pillar != null)
        {
            // A bright burst at the start that thins out.
            float p = Mathf.Clamp01(1f - t / 0.9f);
            show.Pillar.localScale = new Vector3(0.4f + p * 1.6f, show.Pillar.localScale.y, 0.4f + p * 1.6f);
            show.Pillar.gameObject.SetActive(p > 0.01f);
        }

        Spin(show, now);
        show.Circle.localScale = Vector3.one * Mathf.Max(0.001f, (0.3f + 0.7f * Mathf.SmoothStep(0f, 1f, t / 0.3f)) * (1f - fade));
        return fade < 1f;
    }

    static void Spin(Show show, float now)
    {
        if (show.Circle != null)
        {
            show.Circle.localRotation = Quaternion.Euler(90f, now * 60f, 0f);
        }
    }

    void Finish(Show show)
    {
        if (show.Model != null)
        {
            VRFieldModels.ReleasePreview(show.Path, show.Model);
            show.Model = null;
        }

        if (show.Root != null)
        {
            Destroy(show.Root.gameObject);
        }
    }

    void OnDestroy()
    {
        if (_current != null)
        {
            Finish(_current);
        }
    }

    // ---------------------------------------------------------------- pieces

    // The Option side's name on a plate above the figure, always facing you.
    Transform Banner(Show show, float boost)
    {
        float height = (show.Path != null ? VRFieldModels.TargetHeight(5) * 1.15f : 1.2f) * boost + 0.45f * boost;
        Transform banner = new GameObject("Banner") { layer = VRRig.Layer }.transform;
        banner.SetParent(show.Root, false);
        banner.localPosition = new Vector3(0f, height, 0f);
        // Arena space faces away from you; undo the figure's turn so the plate always faces you.
        banner.localRotation = Quaternion.Inverse(show.Root.localRotation);
        Transform plate = new GameObject("Plate") { layer = VRRig.Layer }.transform;
        plate.SetParent(banner, false);
        plate.localScale = Vector3.one * (1.9f * boost);
        Color trim = show.Yours ? S.Accent : TheirColor;
        const float w = 0.62f;
        const float h = 0.16f;
        S.Shape(plate, "Halo", S.RoundedRect(w + 0.05f, h + 0.05f, 0.04f), S.Glass(S.Halo), new Vector3(0f, 0f, 0.012f));
        S.Card(plate, "Body", new Vector3(0f, 0f, 0.006f), new Vector2(w, h), 0.03f, S.Header, trim, 0.006f);
        S.Card(plate, "Tag", new Vector3(0f, h * 0.5f, 0.003f), new Vector2(0.16f, 0.04f), 0.02f, trim);
        S.Text(plate, new Vector3(0f, h * 0.5f, -0.001f), new Vector2(0.13f, 0.026f), "OPTION", Color.white, trim);
        S.Text(plate, new Vector3(0f, -0.012f, -0.001f), new Vector2(w - 0.06f, 0.07f), string.IsNullOrEmpty(show.Name) ? "OPTION" : show.Name, S.Ink, S.Header);
        banner.localScale = new Vector3(0.001f, 0.001f, 1f);
        return banner;
    }

    static Transform Circle(Transform parent, Color color, float size)
    {
        _circleMaterial ??= CircleMaterial();
        Transform circle = new GameObject("MagicCircle") { layer = VRRig.Layer }.transform;
        circle.SetParent(parent, false);
        circle.localPosition = Vector3.up * 0.02f;
        MeshRenderer renderer = S.Shape(circle, "Ring", S.Disc(size), _circleMaterial, Vector3.zero);
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", new Color(color.r, color.g, color.b, 0.9f));
        block.SetColor("_Color", new Color(color.r, color.g, color.b, 0.9f));
        renderer.SetPropertyBlock(block);
        circle.localRotation = Quaternion.Euler(90f, 0f, 0f);
        return circle;
    }

    static Transform Pillar(Transform parent, Color color, float boost)
    {
        _pillarMaterial ??= VRMaterials.Transparent(new Color(1f, 1f, 1f, 0.45f));
        GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pillar.name = "Pillar";
        pillar.layer = VRRig.Layer;
        Destroy(pillar.GetComponent<Collider>());
        pillar.transform.SetParent(parent, false);
        pillar.transform.localScale = new Vector3(2f, 3f * boost, 2f);
        pillar.transform.localPosition = Vector3.up * 3f * boost;
        Renderer renderer = pillar.GetComponent<Renderer>();
        renderer.sharedMaterial = _pillarMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        Color c = Color.Lerp(color, Color.white, 0.5f);
        block.SetColor("_BaseColor", new Color(c.r, c.g, c.b, 0.45f));
        block.SetColor("_Color", new Color(c.r, c.g, c.b, 0.45f));
        renderer.SetPropertyBlock(block);
        return pillar.transform;
    }

    // A ring-and-runes texture (made in code) on a see-through material.
    static Material CircleMaterial()
    {
        const int size = 128;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "DCGOVR_MagicCircle" };

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f - size * 0.5f, y + 0.5f - size * 0.5f) / (size * 0.5f);
                float r = p.magnitude;
                float a = Mathf.Atan2(p.y, p.x);
                float ring = Band(r, 0.95f, 0.025f) + Band(r, 0.78f, 0.018f) + Band(r, 0.42f, 0.02f);
                // Runes between the outer rings, a hexagram inside.
                float runes = r > 0.8f && r < 0.93f && Mathf.Repeat(a * 24f / (2f * Mathf.PI), 1f) < 0.45f && Mathf.Repeat(r * 30f, 1f) < 0.6f ? 0.8f : 0f;
                float star = 0f;

                for (int k = 0; k < 6; k++)
                {
                    float angle = k * Mathf.PI / 3f;
                    Vector2 n = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    star = Mathf.Max(star, r < 0.78f ? Band(Vector2.Dot(p, n), 0.39f, 0.012f) : 0f);
                }

                float alpha = Mathf.Clamp01(ring + runes + star) * (r < 1f ? 1f : 0f);
                alpha = Mathf.Max(alpha, r < 0.95f ? 0.08f * (1f - r) : 0f); // faint fill
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply(true, true);
        Material material = VRMaterials.Transparent(Color.white);
        material.SetTexture("_BaseMap", texture);
        material.SetTexture("_MainTex", texture);
        return material;
    }

    static float Band(float value, float centre, float width) => Mathf.Clamp01(1f - Mathf.Abs(value - centre) / width);

    void EnsureLight()
    {
        if (_light != null)
        {
            return;
        }

        // Same soft light as the arena figures get (their toon shader uses the main light).
        GameObject go = new GameObject("DCGO_VR_DualOptionLight") { layer = VRRig.Layer };
        go.transform.SetParent(_arena, false);
        go.transform.localRotation = Quaternion.Euler(45f, 160f, 0f);
        _light = go.AddComponent<Light>();
        _light.type = LightType.Directional;
        _light.intensity = 1.15f;
        _light.color = new Color(1f, 0.97f, 0.92f);
        _light.cullingMask = 1 << VRRig.Layer;
        _light.shadows = LightShadows.None;
    }
}
#endif
