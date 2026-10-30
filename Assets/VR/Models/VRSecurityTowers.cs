#if DCGO_VR
using System.Collections.Generic;
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// Duel Ring layout: each player's security as a tower of floating shields in the arena
// (yours blue in your right corner, the opponent's red in their far corner). One shield per
// card, face-up security cards show the card instead, a gentle bob, and a count plate on top.
public class VRSecurityTowers : MonoBehaviour
{
    const float Gap = 0.42f;            // vertical distance between shields
    const float Base = 0.45f;           // first shield above the pad
    const float ShieldW = 0.55f;
    const float ShieldH = 0.67f;

    sealed class Tower
    {
        public Transform Root;
        public VRLabel Label;
        public readonly List<Renderer> Slots = new List<Renderer>();
        public readonly List<CardSource> Cards = new List<CardSource>();
        public int Key = int.MinValue;
    }

    Transform _arena;
    readonly Tower[] _towers = { new Tower(), new Tower() };
    static Material _cardMaterial;

    public static VRSecurityTowers Create(Transform parent, Transform arena)
    {
        GameObject go = new GameObject("SecurityTowers") { layer = VRRig.Layer };
        go.transform.SetParent(parent, false);
        VRSecurityTowers towers = go.AddComponent<VRSecurityTowers>();
        towers._arena = arena;
        return towers;
    }

    void LateUpdate()
    {
        GameContext context = BoardObserver.Context;
        bool show = VRSettings.Models && VRFieldModels.RingLayout && _arena != null && context != null && context.You != null && context.Opponent != null;

        for (int side = 0; side < 2; side++)
        {
            bool yours = side == 0;
            Tower tower = _towers[side];

            if (!show)
            {
                if (tower.Root != null && tower.Root.gameObject.activeSelf)
                {
                    tower.Root.gameObject.SetActive(false);
                }

                continue;
            }

            if (tower.Root == null)
            {
                Build(tower, yours);
            }

            if (!tower.Root.gameObject.activeSelf)
            {
                tower.Root.gameObject.SetActive(true);
            }

            tower.Root.localPosition = VRFieldModels.RingTowerSpot(yours);
            Player player = yours ? context.You : context.Opponent;
            Refresh(tower, player.SecurityCards, yours);

            // Gentle floating bob, each shield a little out of phase.
            float t = Time.unscaledTime;

            for (int i = 0; i < tower.Slots.Count; i++)
            {
                Transform slot = tower.Slots[i].transform;
                Vector3 p = slot.localPosition;
                p.y = Base + i * Gap + Mathf.Sin(t * 1.6f + i * 0.7f) * 0.03f;
                slot.localPosition = p;
            }
        }
    }

    void Build(Tower tower, bool yours)
    {
        tower.Root = new GameObject(yours ? "YourSecurityTower" : "TheirSecurityTower") { layer = VRRig.Layer }.transform;
        tower.Root.SetParent(_arena, false);
        tower.Label = VRLabel.Create(tower.Root, Vector3.zero, Quaternion.identity, new Vector2(1.1f, 0.24f), "", Color.white,
            yours ? new Color(0.06f, 0.2f, 0.36f) : new Color(0.36f, 0.08f, 0.08f));

        if (!yours)
        {
            // Their tower is a laser target too: pointing at it = attacking the player.
            BoxCollider hit = tower.Root.gameObject.AddComponent<BoxCollider>();
            hit.isTrigger = true;
            hit.center = new Vector3(0f, 1.4f, 0f);
            hit.size = new Vector3(1f, 2.8f, 0.8f);
            tower.Root.gameObject.AddComponent<VRArenaTarget>().OpponentPlayer = true;
        }
    }

    void Refresh(Tower tower, List<CardSource> security, bool yours)
    {
        int key = security.Count;

        foreach (CardSource card in security)
        {
            key = key * 31 + (card != null ? card.GetHashCode() : 0) * (card != null && card.IsFaceUp ? 2 : 1);
        }

        if (key == tower.Key)
        {
            return;
        }

        tower.Key = key;
        int n = security.Count;

        while (tower.Slots.Count < n)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Shield";
            quad.layer = VRRig.Layer;
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(tower.Root, false);
            tower.Slots.Add(quad.GetComponent<Renderer>());
            tower.Cards.Add(null);
        }

        for (int i = 0; i < tower.Slots.Count; i++)
        {
            Renderer slot = tower.Slots[i];
            bool used = i < n;
            slot.gameObject.SetActive(used);

            if (!used)
            {
                tower.Cards[i] = null;
                continue;
            }

            // Bottom of the stack = top of the security (the next card to be checked is on top).
            CardSource card = security[n - 1 - i];
            bool faceUp = card != null && card.IsFaceUp;
            slot.transform.localPosition = new Vector3(0f, Base + i * Gap, 0f);
            slot.transform.localScale = faceUp ? new Vector3(ShieldW, ShieldW * 1.4f, 1f) : new Vector3(ShieldW, ShieldH, 1f);

            if (faceUp)
            {
                slot.sharedMaterial = _cardMaterial != null ? _cardMaterial : _cardMaterial = VRMaterials.Unlit(Color.white);

                if (tower.Cards[i] != card)
                {
                    tower.Cards[i] = card;
                    LoadArt(card, slot);
                }
            }
            else
            {
                tower.Cards[i] = null;
                slot.SetPropertyBlock(null);
                slot.sharedMaterial = VROpponentAvatar.ShieldMaterial(blue: yours);
            }
        }

        tower.Label.transform.localPosition = new Vector3(0f, Base + n * Gap + 0.2f, 0f);
        tower.Label.text = "<b>SECURITY " + n + "</b>";
    }

    static async void LoadArt(CardSource card, Renderer target)
    {
        try
        {
            Sprite sprite = await card.GetCardSprite();

            if (sprite == null || target == null)
            {
                return;
            }

            Texture2D texture = sprite.texture;
            Rect rect = sprite.textureRect;
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            block.SetTexture("_BaseMap", texture);
            block.SetTexture("_MainTex", texture);
            Vector4 st = new Vector4(rect.width / texture.width, rect.height / texture.height, rect.x / texture.width, rect.y / texture.height);
            block.SetVector("_BaseMap_ST", st);
            block.SetVector("_MainTex_ST", st);
            target.SetPropertyBlock(block);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning("[DCGO VR] Security tower card art failed: " + exception.Message);
        }
    }
}
#endif
