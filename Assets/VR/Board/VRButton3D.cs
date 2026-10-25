#if DCGO_VR
using System;
using TMPro;
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// A pressable button: either a round push button lying flat (End Turn, Hatch)
// or a rectangular plate facing the player (menu entries). Pressed with the
// laser + trigger or by touching it with a controller (see VRGrabber).
public class VRButton3D : MonoBehaviour
{
    static readonly Color RoundIdle = new Color(0.75f, 0.15f, 0.15f);
    static readonly Color RoundHover = new Color(1f, 0.35f, 0.3f);
    static readonly Color PlateIdle = new Color(0.10f, 0.20f, 0.30f);
    static readonly Color PlateHover = new Color(0.20f, 0.55f, 0.85f);
    static readonly Color Disabled = new Color(0.22f, 0.22f, 0.25f);

    Action _onPress;
    Renderer _cap;
    VRLabel _label;
    MaterialPropertyBlock _block;
    bool _hovered;
    bool _plate;
    bool _cardStyle;
    float _pressedUntil;

    public bool Interactable { get; set; } = true;

    /// <summary>Round push button lying flat (cap facing up).</summary>
    public static VRButton3D Create(Transform parent, Vector3 localPosition, string label, Action onPress)
    {
        VRButton3D button = CreateRoot(parent, localPosition, label, onPress);

        GameObject cap = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cap.name = "Cap";
        cap.layer = VRRig.Layer;
        cap.transform.SetParent(button.transform, false);
        cap.transform.localScale = new Vector3(0.09f, 0.012f, 0.09f);
        cap.transform.localPosition = new Vector3(0f, 0.012f, 0f);
        button._cap = cap.GetComponent<Renderer>();
        button._cap.sharedMaterial = VRMaterials.Unlit(RoundIdle);

        button._label = VRLabel.Create(button.transform, new Vector3(0f, 0.0245f, 0f), Quaternion.Euler(90f, 0f, 0f), new Vector2(0.075f, 0.026f), label, Color.white, RoundIdle);
        return button;
    }

    /// <summary>Rectangular plate whose front (-Z) faces the player; used for menus.</summary>
    public static VRButton3D CreatePlate(Transform parent, Vector3 localPosition, Vector2 size, string label, Action onPress)
    {
        VRButton3D button = CreateRoot(parent, localPosition, label, onPress);
        button._plate = true;

        GameObject cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cap.name = "Plate";
        cap.layer = VRRig.Layer;
        cap.transform.SetParent(button.transform, false);
        cap.transform.localScale = new Vector3(size.x, size.y, 0.008f);
        button._cap = cap.GetComponent<Renderer>();
        button._cap.sharedMaterial = VRMaterials.Unlit(PlateIdle);

        button._label = VRLabel.Create(button.transform, new Vector3(0f, 0f, -0.0045f), Quaternion.identity, new Vector2(size.x - 0.006f, size.y - 0.006f), label, Color.white, PlateIdle);
        return button;
    }

    /// <summary>A card picture you can press (choices of a prompt panel). Its front (-Z) faces the player.</summary>
    public static VRButton3D CreateCard(Transform parent, Vector3 localPosition, Vector2 size, Sprite sprite, Action onPress)
    {
        VRButton3D button = CreateRoot(parent, localPosition, "card", onPress);
        button._plate = true;
        button._cardStyle = true;

        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Picture";
        quad.layer = VRRig.Layer;
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(button.transform, false);
        quad.transform.localScale = new Vector3(size.x, size.y, 1f);
        button._cap = quad.GetComponent<Renderer>();
        button._cap.sharedMaterial = VRMaterials.Unlit(Color.white);

        BoxCollider collider = button.gameObject.AddComponent<BoxCollider>();
        collider.size = new Vector3(size.x, size.y, 0.01f);

        button.SetSprite(sprite);
        return button;
    }

    public void SetSprite(Sprite sprite)
    {
        if (sprite == null || _cap == null)
        {
            return;
        }

        Texture2D texture = sprite.texture;
        Rect rect = sprite.textureRect;
        Vector4 st = new Vector4(rect.width / texture.width, rect.height / texture.height, rect.x / texture.width, rect.y / texture.height);
        _cap.GetPropertyBlock(_block);
        _block.SetTexture("_BaseMap", texture);
        _block.SetTexture("_MainTex", texture);
        _block.SetVector("_BaseMap_ST", st);
        _block.SetVector("_MainTex_ST", st);
        _cap.SetPropertyBlock(_block);
    }

    static VRButton3D CreateRoot(Transform parent, Vector3 localPosition, string label, Action onPress)
    {
        GameObject root = new GameObject("Button " + label) { layer = VRRig.Layer };
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;
        VRButton3D button = root.AddComponent<VRButton3D>();
        button._onPress = onPress;
        button._block = new MaterialPropertyBlock();
        return button;
    }

    public void SetLabel(string text)
    {
        if (_label != null && _label.text != text)
        {
            _label.text = text;
        }
    }

    public void SetHovered(bool hovered) => _hovered = hovered;

    public void Press()
    {
        if (!Interactable)
        {
            return;
        }

        _pressedUntil = Time.unscaledTime + 0.15f;
        _onPress?.Invoke();
    }

    void Update()
    {
        bool pressed = Time.unscaledTime < _pressedUntil;

        if (_cardStyle)
        {
            // Cards keep their picture; hovering brightens and pops them toward you.
            Color tint = !Interactable ? new Color(0.5f, 0.5f, 0.55f) : _hovered ? new Color(1.15f, 1.15f, 1.05f) : Color.white;
            _cap.GetPropertyBlock(_block);
            _block.SetColor("_BaseColor", tint);
            _block.SetColor("_Color", tint);
            _cap.SetPropertyBlock(_block);
            _cap.transform.localPosition = new Vector3(0f, 0f, _hovered ? -0.015f : 0f) + (pressed ? new Vector3(0f, 0f, 0.004f) : Vector3.zero);
            _hovered = false;
            return;
        }

        _cap.transform.localPosition = _plate
            ? new Vector3(0f, 0f, pressed ? 0.004f : 0f)
            : new Vector3(0f, pressed ? 0.005f : 0.012f, 0f);
        _label.transform.localPosition = _plate
            ? new Vector3(0f, 0f, pressed ? -0.0005f : -0.0045f)
            : new Vector3(0f, pressed ? 0.0175f : 0.0245f, 0f);

        Color idle = _plate ? PlateIdle : RoundIdle;
        Color hover = _plate ? PlateHover : RoundHover;
        Color color = !Interactable ? Disabled : _hovered ? hover : idle;
        _cap.GetPropertyBlock(_block);
        _block.SetColor("_BaseColor", color);
        _block.SetColor("_Color", color);
        _cap.SetPropertyBlock(_block);
        _label.color = Interactable ? Color.white : new Color(0.6f, 0.6f, 0.65f);
        _label.plateColor = color;
        _hovered = false; // re-set every frame by the grabber while hovered
    }
}
#endif
