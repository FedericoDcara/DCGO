#if DCGO_VR
using System;
using System.Collections.Generic;
using DCGO.DSTS;
using GLTFast;
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// Gives runtime-loaded DSTS models (glTFast) the web viewer's look: swaps glTFast's
// default PBR materials for the DSTS cel shader (game CLUT ramp, shadow floor, rim)
// and turns the game's outline shell back on with the DSTS outline shader.
// Materials are built once per loaded file (GltfImport) and shared by all its instances;
// call Release before the GltfImport is disposed.
public static class VRCelShading
{
    /// <summary>Turn off to get glTFast's plain materials (outline shells are hidden then).</summary>
    public static bool Enabled = false; // off: the cel shader made the VR app unplayable (FPS)

    sealed class Set
    {
        public readonly Dictionary<Material, Material> Map = new Dictionary<Material, Material>();
        public readonly List<Material> Owned = new List<Material>();
    }

    static readonly Dictionary<GltfImport, Set> _sets = new Dictionary<GltfImport, Set>();

    /// <summary>Replace the materials of a freshly instantiated model.</summary>
    public static void Apply(GltfImport import, string path, Transform holder)
    {
        if (import == null || holder == null)
        {
            return;
        }

        if (!Enabled)
        {
            ApplyPlain(import, path, holder); // cheap toon (or glTFast PBR) + eye fixes
            return;
        }

        if (!_sets.TryGetValue(import, out Set set))
        {
            set = Build(import, path);
            _sets[import] = set; // null = not a DSTS file / failed: don't retry
        }

        if (set == null || set.Map.Count == 0)
        {
            return;
        }

        foreach (Renderer renderer in holder.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            bool changed = false;

            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] != null && set.Map.TryGetValue(materials[i], out Material cel))
                {
                    materials[i] = cel;
                    changed = true;
                }
            }

            if (changed)
            {
                renderer.sharedMaterials = materials;
            }
        }
    }

    static Set Build(GltfImport import, string path)
    {
        try
        {
            Dictionary<string, object> json = DSTSGlbJson.ReadGlbJson(path);
            Material[] cel = DSTSCelMaterials.Build(json,
                index => index >= 0 && index < import.TextureCount ? import.GetTexture(index) : null);

            Set set = new Set();
            int count = Mathf.Min(cel.Length, import.MaterialCount);

            for (int i = 0; i < count; i++)
            {
                Material source = import.GetMaterial(i);

                if (source != null && cel[i] != null && !set.Map.ContainsKey(source))
                {
                    set.Map[source] = cel[i];
                }
            }

            foreach (Material material in cel)
            {
                if (material != null)
                {
                    set.Owned.Add(material);
                }
            }

            return set;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[DCGO VR] Cel shading skipped for {System.IO.Path.GetFileName(path)}: {exception.Message}");
            return null;
        }
    }

    /// <summary>When cel shading is off: true = cheap Quest toon material (Resources/DSTS/DSTS_MobileToonBase),
    /// false = keep glTFast's PBR materials (heavier, washed-out look).</summary>
    public static bool Lite = true;

    sealed class PlainSet
    {
        public readonly HashSet<Material> Hide = new HashSet<Material>();
        public readonly Dictionary<Material, Material> Map = new Dictionary<Material, Material>();
        public readonly List<Material> Owned = new List<Material>();
    }

    static readonly Dictionary<GltfImport, PlainSet> _plain = new Dictionary<GltfImport, PlainSet>();
    static Material _liteBase;
    static bool _liteBaseMissing;

    static Material LiteBase
    {
        get
        {
            if (_liteBase == null && !_liteBaseMissing)
            {
                _liteBase = Resources.Load<Material>("DSTS/DSTS_MobileToonBase");
                _liteBaseMissing = _liteBase == null;

                if (_liteBase != null)
                {
                    // A shader that did not make it into the build draws pink: fall back to the
                    // (always included) URP Unlit materials from VRModelMaterials instead.
                    Debug.Log($"[DCGO VR] Toon material: {_liteBase.shader.name}, supported {_liteBase.shader.isSupported}");

                    if (!_liteBase.shader.isSupported)
                    {
                        _liteBase = null;
                        _liteBaseMissing = true;
                    }
                }

                if (_liteBaseMissing)
                {
                    Debug.LogWarning("[DCGO VR] Resources/DSTS/DSTS_MobileToonBase missing: keeping glTFast materials.");
                }
            }

            return _liteBase;
        }
    }

    /// <summary>Non-cel path. Hides the game's eye-shine layers (flagged OPAQUE in the .glb, they paint
    /// over the irises), honours "screen door" alpha (eye white has a hole for the iris) and, with
    /// <see cref="Lite"/>, swaps glTFast PBR for the cheap DSTS_MobileToon material.</summary>
    static void ApplyPlain(GltfImport import, string path, Transform holder)
    {
        if (!_plain.TryGetValue(import, out PlainSet set))
        {
            set = BuildPlain(import, path);
            _plain[import] = set;
        }

        foreach (Renderer renderer in holder.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            bool changed = false;
            bool allHidden = materials.Length > 0;

            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];

                if (material != null && set.Hide.Contains(material))
                {
                    continue;
                }

                allHidden = false;

                if (material != null && set.Map.TryGetValue(material, out Material lite))
                {
                    materials[i] = lite;
                    changed = true;
                }
            }

            if (allHidden)
            {
                renderer.enabled = false;
                continue;
            }

            if (changed)
            {
                renderer.sharedMaterials = materials;
            }
        }
    }

    static PlainSet BuildPlain(GltfImport import, string path)
    {
        PlainSet set = new PlainSet();

        try
        {
            List<object> materials = DSTSGlbJson.Arr(DSTSGlbJson.ReadGlbJson(path), "materials");
            int count = materials == null ? 0 : Mathf.Min(materials.Count, import.MaterialCount);
            Material liteBase = Lite ? LiteBase : null;

            for (int i = 0; i < count; i++)
            {
                Material source = import.GetMaterial(i);

                if (source == null || set.Map.ContainsKey(source) || set.Hide.Contains(source))
                {
                    continue;
                }

                object gltf = materials[i];
                object render = DSTSGlbJson.Obj(DSTSGlbJson.Obj(gltf, "extras"), "dsts_render");
                string role = DSTSGlbJson.Str(render, "role");

                if (role == "eye_shine")
                {
                    set.Hide.Add(source);
                    continue;
                }

                if (role == "outline_inflation")
                {
                    continue; // outline shells are hidden by VRFieldModels.Normalize
                }

                string gltfAlpha = DSTSGlbJson.Str(gltf, "alphaMode");
                bool blend = gltfAlpha == "BLEND" || DSTSGlbJson.Str(render, "alpha_mode") == "BLEND";
                bool clip = !blend && (DSTSGlbJson.Bool(render, "screen_door") || gltfAlpha == "MASK" || role == "mask_alpha");
                float cutoff = (float)DSTSGlbJson.Num(render, "mask_cutoff", DSTSGlbJson.Num(gltf, "alphaCutoff", 0.5));

                if (liteBase == null || blend)
                {
                    if (clip)
                    {
                        MakeCutout(source, cutoff); // keep glTFast material, just honour the alpha
                    }

                    continue;
                }

                Material lite = new Material(liteBase) { name = source.name + " (DSTS lite)" };

                // Source is either a glTFast shader-graph material ("baseColorTexture") or a
                // VRModelMaterials URP Unlit one ("_BaseMap"), which is what loads now.
                if (source.HasProperty("baseColorTexture"))
                {
                    lite.SetTexture("_BaseMap", source.GetTexture("baseColorTexture"));
                }
                else if (source.HasProperty("_BaseMap"))
                {
                    lite.SetTexture("_BaseMap", source.GetTexture("_BaseMap"));
                    lite.SetTextureScale("_BaseMap", source.GetTextureScale("_BaseMap"));
                    lite.SetTextureOffset("_BaseMap", source.GetTextureOffset("_BaseMap"));
                }

                if (source.HasProperty("baseColorTexture_ST"))
                {
                    Vector4 st = source.GetVector("baseColorTexture_ST");

                    if (st.x != 0f || st.y != 0f)
                    {
                        lite.SetTextureScale("_BaseMap", new Vector2(st.x, st.y));
                        lite.SetTextureOffset("_BaseMap", new Vector2(st.z, st.w));
                    }
                }

                if (source.HasProperty("baseColorFactor"))
                {
                    lite.SetColor("_BaseColor", source.GetColor("baseColorFactor"));
                }
                else if (source.HasProperty("_BaseColor"))
                {
                    lite.SetColor("_BaseColor", source.GetColor("_BaseColor"));
                }

                bool doubleSided = DSTSGlbJson.Bool(gltf, "doubleSided") || DSTSGlbJson.Str(render, "cull") == "none";
                lite.SetFloat("_Cull", doubleSided ? 0f : 2f);

                if (clip)
                {
                    lite.EnableKeyword("_ALPHATEST_ON");
                    lite.SetFloat("_Cutoff", cutoff);
                    lite.SetOverrideTag("RenderType", "TransparentCutout");
                    lite.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                }

                set.Map[source] = lite;
                set.Owned.Add(lite);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[DCGO VR] Material fix-up skipped for {System.IO.Path.GetFileName(path)}: {exception.Message}");
        }

        return set;
    }

    /// <summary>The game "screen-door" discards pixels by texture alpha (e.g. the eye white has a hole
    /// where the iris shows through) but the .glb marks those materials OPAQUE. Switch the glTFast
    /// material to alpha clip, the same way glTFast sets up alphaMode MASK on URP.</summary>
    static void MakeCutout(Material material, float cutoff)
    {
        if (material.IsKeywordEnabled("_ALPHATEST_ON") || material.renderQueue > (int)UnityEngine.Rendering.RenderQueue.GeometryLast)
        {
            return; // already MASK, or a transparent (BLEND) material
        }

        material.SetFloat("alphaCutoff", cutoff);
        material.SetFloat("_Cutoff", cutoff); // URP Unlit (VRModelMaterials)
        material.SetFloat("_AlphaClip", 1f);
        material.EnableKeyword("_ALPHATEST_ON");
        material.SetOverrideTag("RenderType", "TransparentCutout");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
    }

    /// <summary>Destroys the cel materials of a file. Call right before GltfImport.Dispose().</summary>
    public static void Release(GltfImport import)
    {
        if (import != null && _plain.TryGetValue(import, out PlainSet plain))
        {
            _plain.Remove(import);

            foreach (Material material in plain.Owned)
            {
                if (material != null)
                {
                    UnityEngine.Object.Destroy(material);
                }
            }
        }

        if (import == null || !_sets.TryGetValue(import, out Set set))
        {
            return;
        }

        _sets.Remove(import);

        if (set == null)
        {
            return;
        }

        foreach (Material material in set.Owned)
        {
            if (material != null)
            {
                UnityEngine.Object.Destroy(material);
            }
        }
    }

    /// <summary>True for the DSTS outline material (its shell should stay visible).</summary>
    public static bool IsCelOutline(Material material) => Enabled && DSTSCelMaterials.IsOutline(material);
}
#endif
