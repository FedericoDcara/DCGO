#if DCGO_VR
using TMPro;
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// The project's TMP Settings point to a default font asset that is not in the
// project, so TextMeshPro objects created from code render nothing. All VR text
// goes through here and uses the game's own Play font from Resources.
public static class VRText
{
    static TMP_FontAsset _font;
    static Material _stereoMaterial;

    public static TMP_FontAsset Font
    {
        get
        {
            if (_font == null)
            {
                _font = Resources.Load<TMP_FontAsset>("Fonts & Materials/Play-Bold SDF");

                if (_font == null)
                {
                    _font = Resources.Load<TMP_FontAsset>("Fonts & Materials/Play-Regular SDF");
                }

                if (_font == null)
                {
                    _font = TMP_Settings.defaultFontAsset;
                }

                if (_font == null)
                {
                    Debug.LogWarning("[DCGO VR] No TextMeshPro font found for VR text.");
                }
            }

            return _font;
        }
    }

    /// <summary>
    /// The font's material switched to DCGO/VR Text SDF: the stock TMP shader is not
    /// compiled for single-pass instanced stereo and only draws in the left eye.
    /// </summary>
    static Material StereoMaterial
    {
        get
        {
            if (_stereoMaterial == null && Font != null && Font.material != null)
            {
                Shader shader = Shader.Find("DCGO/VR Text SDF");
                _stereoMaterial = new Material(Font.material) { name = "DCGOVR_Text (stereo)" };

                if (shader != null)
                {
                    _stereoMaterial.shader = shader;
                }
                else
                {
                    Debug.LogWarning("[DCGO VR] DCGO/VR Text SDF shader not found; VR text may only show in one eye.");
                }
            }

            return _stereoMaterial;
        }
    }

    public static TextMeshPro Add(GameObject go)
    {
        // Adding a TextMeshPro turns the Transform into a RectTransform; keep the pose we set.
        Transform t = go.transform;
        Vector3 position = t.localPosition;
        Quaternion rotation = t.localRotation;
        Vector3 scale = t.localScale;

        TextMeshPro text = go.AddComponent<TextMeshPro>();

        if (Font != null)
        {
            text.font = Font;
        }

        if (StereoMaterial != null)
        {
            text.fontSharedMaterial = StereoMaterial;
        }

        // Centre the text on the object's position (no offset from the rect pivot).
        text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        go.transform.localPosition = position;
        go.transform.localRotation = rotation;
        go.transform.localScale = scale;
        return text;
    }
}
#endif
