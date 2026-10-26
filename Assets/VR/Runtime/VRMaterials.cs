#if DCGO_VR
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// Unlit materials for the VR room. Uses Resources/DCGOVR_Unlit (a URP Unlit
// material created by DCGO > VR > Setup VR) so the shader is guaranteed to be
// in builds; falls back to Shader.Find in the editor.
public static class VRMaterials
{
    static Material _base;

    static Material Base
    {
        get
        {
            if (_base == null)
            {
                _base = Resources.Load<Material>("DCGOVR_Unlit");

                if (_base == null)
                {
                    Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

                    if (shader == null)
                    {
                        shader = Shader.Find("Unlit/Texture");
                    }

                    _base = new Material(shader);
                    Debug.LogWarning("[DCGO VR] Resources/DCGOVR_Unlit material missing; run DCGO > VR > Setup VR.");
                }
            }

            return _base;
        }
    }

    public static Material Unlit(Color color)
    {
        Material material = new Material(Base);
        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        return material;
    }

    /// <summary>See-through unlit material (alpha from the colour), e.g. the duel desk backdrop.</summary>
    public static Material Transparent(Color color)
    {
        Material material = Unlit(color);
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return material;
    }

    public static Material Screen(Texture texture)
    {
        Material material = new Material(Base);
        material.SetColor("_BaseColor", Color.white);
        material.SetColor("_Color", Color.white);
        material.SetTexture("_BaseMap", texture);
        material.SetTexture("_MainTex", texture);
        return material;
    }
}
#endif
