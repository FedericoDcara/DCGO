#if DCGO_VR
using System.Collections.Generic;
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// Option cards on the field (delay options that stay, and options being used right now)
// shown in the arena: yours lie flat past the right end of your row, readable from above
// like the cards under your Digimon; the opponent's stand upright beside their avatar,
// on her right side, facing you. Delay options are laser targets (their effects work
// from the arena like any other card).
public class VRArenaOptions : MonoBehaviour
{
    const float CardW = 0.5f;
    const float CardH = 0.7f;
    const float TheirScale = 1.6f;   // far away: drawn bigger

    sealed class Shown
    {
        public Transform Root;
        public Renderer Face;
        public bool Yours;
        public bool Seen;
    }

    /// <summary>Options shown per side (the Duel Ring puts them in the back part of the Battle Area).</summary>
    public static int YourCount;
    public static int TheirCount;

    Transform _arena;
    readonly Dictionary<CardSource, Shown> _shown = new Dictionary<CardSource, Shown>();
    readonly List<CardSource> _gone = new List<CardSource>();
    readonly List<KeyValuePair<CardSource, Permanent>> _yours = new List<KeyValuePair<CardSource, Permanent>>();
    readonly List<KeyValuePair<CardSource, Permanent>> _theirs = new List<KeyValuePair<CardSource, Permanent>>();
    static Material _faceMaterial;
    static Material _yourFrame;
    static Material _theirFrame;

    public static VRArenaOptions Create(Transform parent, Transform arena)
    {
        GameObject go = new GameObject("ArenaOptions") { layer = VRRig.Layer };
        go.transform.SetParent(parent, false);
        VRArenaOptions options = go.AddComponent<VRArenaOptions>();
        options._arena = arena;
        return options;
    }

    void LateUpdate()
    {
        GameContext context = BoardObserver.Context;
        bool show = VRSettings.Models && _arena != null && context != null && context.You != null && context.Opponent != null;

        foreach (Shown shown in _shown.Values)
        {
            shown.Seen = false;
        }

        if (!show)
        {
            YourCount = 0;
            TheirCount = 0;
        }

        if (show)
        {
            Collect(context.You, _yours);
            Collect(context.Opponent, _theirs);
            YourCount = _yours.Count;
            TheirCount = _theirs.Count;

            if (VRFieldModels.RingLayout)
            {
                // Duel Ring: lying in the back part of each Battle Area, after the Tamers.
                for (int side = 0; side < 2; side++)
                {
                    bool yours = side == 0;
                    List<KeyValuePair<CardSource, Permanent>> list = yours ? _yours : _theirs;
                    int tamers = yours ? VRFieldModels.YourTamers : VRFieldModels.TheirTamers;

                    for (int i = 0; i < list.Count; i++)
                    {
                        Shown shown = Get(list[i].Key, list[i].Value, yours);
                        shown.Root.SetParent(_arena, false);
                        shown.Root.localPosition = VRFieldModels.RingBackSlot(yours, tamers + i, tamers + list.Count) + new Vector3(0f, 0.015f, 0f);
                        shown.Root.localRotation = Quaternion.Euler(90f, 0f, 0f); // flat, bottom edge towards you
                        shown.Root.localScale = Vector3.one * (yours ? 1f : 1.3f);
                    }
                }

                _yours.Clear();
                _theirs.Clear(); // placed: the Classic placement below has nothing left to do
            }

            for (int i = 0; i < _yours.Count; i++)
            {
                Shown shown = Get(_yours[i].Key, _yours[i].Value, true);
                shown.Root.SetParent(_arena, false);
                shown.Root.localPosition = VRFieldModels.YourOptionSpot(i);
                shown.Root.localRotation = Quaternion.Euler(90f, 0f, 0f); // flat, bottom edge towards you
                shown.Root.localScale = Vector3.one;
            }

            Transform avatar = VROpponentAvatar.Root;

            for (int i = 0; i < _theirs.Count; i++)
            {
                Shown shown = Get(_theirs[i].Key, _theirs[i].Value, false);

                if (avatar != null)
                {
                    // Upright beside her, facing you.
                    shown.Root.SetParent(avatar, false);
                    shown.Root.localPosition = VROpponentAvatar.OptionSpot(i);
                    shown.Root.localRotation = Quaternion.identity;
                    shown.Root.localScale = Vector3.one * TheirScale;
                }
                else
                {
                    // No avatar: lie flat past the left end of their row.
                    shown.Root.SetParent(_arena, false);
                    shown.Root.localPosition = VRFieldModels.PreviewSpot(false) + new Vector3(-4f - i * 0.9f, 0.012f, 0f);
                    shown.Root.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    shown.Root.localScale = Vector3.one * 1.3f;
                }
            }
        }

        // Remove cards that left the field / finished resolving.
        _gone.Clear();

        foreach (KeyValuePair<CardSource, Shown> pair in _shown)
        {
            if (!pair.Value.Seen)
            {
                _gone.Add(pair.Key);
            }
        }

        foreach (CardSource card in _gone)
        {
            if (_shown[card].Root != null)
            {
                Destroy(_shown[card].Root.gameObject);
            }

            _shown.Remove(card);
        }
    }

    // Options on the field (delay options) plus options being used now.
    static void Collect(Player player, List<KeyValuePair<CardSource, Permanent>> list)
    {
        list.Clear();

        foreach (Permanent permanent in player.GetFieldPermanents())
        {
            CardSource top = permanent != null ? permanent.TopCard : null;

            if (top != null && !top.IsFlipped && IsOption(top))
            {
                list.Add(new KeyValuePair<CardSource, Permanent>(top, permanent));
            }
        }

        foreach (CardSource card in player.ExecutingCards)
        {
            if (card != null && IsOption(card) && !Contains(list, card))
            {
                list.Add(new KeyValuePair<CardSource, Permanent>(card, null));
            }
        }
    }

    static bool Contains(List<KeyValuePair<CardSource, Permanent>> list, CardSource card)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Key == card)
            {
                return true;
            }
        }

        return false;
    }

    static bool IsOption(CardSource card)
    {
        try
        {
            return card.IsOption;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    Shown Get(CardSource card, Permanent permanent, bool yours)
    {
        if (!_shown.TryGetValue(card, out Shown shown) || shown.Root == null || shown.Yours != yours)
        {
            if (shown != null && shown.Root != null)
            {
                Destroy(shown.Root.gameObject);
            }

            shown = Build(card, permanent, yours);
            _shown[card] = shown;
        }

        shown.Seen = true;
        return shown;
    }

    Shown Build(CardSource card, Permanent permanent, bool yours)
    {
        Shown shown = new Shown { Yours = yours };
        shown.Root = new GameObject("Option " + card.BaseENGCardNameFromEntity) { layer = VRRig.Layer }.transform;

        // Coloured frame behind the card (blue yours, red theirs).
        GameObject frame = GameObject.CreatePrimitive(PrimitiveType.Quad);
        frame.layer = VRRig.Layer;
        Destroy(frame.GetComponent<Collider>());
        frame.transform.SetParent(shown.Root, false);
        frame.transform.localPosition = new Vector3(0f, 0f, 0.004f);
        frame.transform.localScale = new Vector3(CardW + 0.05f, CardH + 0.05f, 1f);
        frame.GetComponent<Renderer>().sharedMaterial = yours
            ? (_yourFrame != null ? _yourFrame : _yourFrame = VRMaterials.Unlit(new Color(0.25f, 0.8f, 1f) * 0.8f))
            : (_theirFrame != null ? _theirFrame : _theirFrame = VRMaterials.Unlit(new Color(1f, 0.35f, 0.3f) * 0.8f));

        GameObject face = GameObject.CreatePrimitive(PrimitiveType.Quad);
        face.layer = VRRig.Layer;
        Destroy(face.GetComponent<Collider>());
        face.transform.SetParent(shown.Root, false);
        face.transform.localScale = new Vector3(CardW, CardH, 1f);
        shown.Face = face.GetComponent<Renderer>();
        shown.Face.sharedMaterial = _faceMaterial != null ? _faceMaterial : _faceMaterial = VRMaterials.Unlit(Color.white);
        LoadArt(card, shown.Face);

        // Delay options on the field can be pointed at (their effects / the stack viewer).
        if (permanent != null)
        {
            BoxCollider hit = shown.Root.gameObject.AddComponent<BoxCollider>();
            hit.isTrigger = true;
            hit.size = new Vector3(CardW + 0.05f, CardH + 0.05f, 0.05f);
            shown.Root.gameObject.AddComponent<VRArenaTarget>().Permanent = permanent;
        }

        return shown;
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
            Debug.LogWarning("[DCGO VR] Option card art failed: " + exception.Message);
        }
    }

    void OnDestroy()
    {
        foreach (Shown shown in _shown.Values)
        {
            if (shown.Root != null)
            {
                Destroy(shown.Root.gameObject);
            }
        }
    }
}
#endif
