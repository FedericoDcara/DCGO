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
