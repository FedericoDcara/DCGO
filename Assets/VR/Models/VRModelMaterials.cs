#if DCGO_VR
using GLTFast;
using GLTFast.Logging;
using GLTFast.Materials;
using GLTFast.Schema;
using UnityEngine;
using Material = UnityEngine.Material; // GLTFast.Schema has its own Material

// === DCGO-CUSTOM:vr ===
// Materials for the 3D models (glTFast material generator). glTFast's own shader graphs
// are only compiled on demand in the editor: a build leaves their variants out, and the
// models came out pink. These materials use URP Unlit through the VR's Resources materials
// (opaque / alpha clip / transparent), which are always in the build. The flat, unshaded
// look keeps the models' own colours, close to the original anime style.
public sealed class VRModelMaterials : IMaterialGenerator
{
    public static readonly VRModelMaterials Instance = new VRModelMaterials();

    static Material _opaque;
    static Material _clip;
    static Material _transparent;
    Material _default;
    static int _logged;

    public Material GetDefaultMaterial(bool pointsSupport = false)
    {
        if (_default == null)
        {
            _default = new Material(Base(MaterialBase.AlphaMode.Opaque));
            _default.name = "glTF default";
        }

        return _default;
    }

    public Material GenerateMaterial(MaterialBase gltfMaterial, IGltfReadable gltf, bool pointsSupport = false)
    {
        MaterialBase.AlphaMode mode = gltfMaterial.GetAlphaMode();
        Material material = new Material(Base(mode)) { name = gltfMaterial.name };
        PbrMetallicRoughnessBase pbr = gltfMaterial.PbrMetallicRoughness;
        Color color = pbr != null ? pbr.BaseColor : Color.white;
        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);

        TextureInfoBase info = pbr != null ? pbr.BaseColorTexture : null;
        Texture2D found = null;

        if (info != null && info.index >= 0 && info.index < gltf.TextureCount)
        {
            Texture2D texture = gltf.GetTexture(info.index);
            found = texture;

            if (texture != null)
            {
                material.SetTexture("_BaseMap", texture);
                material.SetTexture("_MainTex", texture);

                if (gltf.IsTextureYFlipped(info.index))
                {
                    material.SetTextureScale("_BaseMap", new Vector2(1f, -1f));
                    material.SetTextureOffset("_BaseMap", new Vector2(0f, 1f));
                }
            }
        }

        // Diagnostics (first materials only): what the model's texture looks like when it arrives.
        if (_logged < 12)
        {
            _logged++;
            Debug.Log($"[DCGO VR] Model material '{gltfMaterial.name}': mode {mode}, texture index {(info != null ? info.index : -1)} of {gltf.TextureCount}, "
                + (found != null ? $"{found.name} {found.width}x{found.height} {found.format} readable {found.isReadable}" : "NO TEXTURE")
                + $", base {material.shader.name}");
        }

        if (mode == MaterialBase.AlphaMode.Mask)
        {
            material.SetFloat("_Cutoff", gltfMaterial.alphaCutoff);
        }

        if (gltfMaterial.doubleSided)
        {
            material.SetFloat("_Cull", 0f);
        }

        return material;
    }

    public void SetLogger(ICodeLogger logger)
    {
    }

    static Material Base(MaterialBase.AlphaMode mode)
    {
        switch (mode)
        {
            case MaterialBase.AlphaMode.Mask:
                return _clip != null ? _clip : _clip = Load("DCGOVR_UnlitClip");
            case MaterialBase.AlphaMode.Blend:
                return _transparent != null ? _transparent : _transparent = Load("DCGOVR_UnlitTransparent");
            default:
                return _opaque != null ? _opaque : _opaque = Load("DCGOVR_Unlit");
        }
    }

    static Material Load(string name)
    {
        Material material = Resources.Load<Material>(name);

        if (material == null)
        {
            Debug.LogWarning("[DCGO VR] Missing Resources/" + name + " - model materials fall back to opaque unlit.");
            material = Resources.Load<Material>("DCGOVR_Unlit");
        }

        return material;
    }
}
#endif
