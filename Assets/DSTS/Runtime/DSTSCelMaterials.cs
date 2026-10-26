using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DCGO.DSTS
{
    /// <summary>
    /// Builds DSTS cel / outline materials at runtime from a DSTS .glb's JSON (incl. the
    /// "extras" written by dsts-extractor). One material per glTF material index; null where
    /// the original material should be kept.
    /// Shaders come from Resources/DSTS/*Base materials so they are always in builds.
    /// </summary>
    public static class DSTSCelMaterials
    {
        public const float DefaultOutlineWidth = 0.0022f;

        /// <summary>Multiplier for the outline width of newly built materials (1 = viewer look).</summary>
        public static float OutlineWidthScale = 1f;

        static Material _toonBase, _outlineBase;

        public static Material ToonBase => _toonBase != null ? _toonBase : (_toonBase = LoadBase("DSTS/DSTS_ToonBase", "DCGO/DSTS/Toon"));
        public static Material OutlineBase => _outlineBase != null ? _outlineBase : (_outlineBase = LoadBase("DSTS/DSTS_OutlineBase", "DCGO/DSTS/Outline"));

        static Material LoadBase(string resource, string shaderName)
        {
            var m = Resources.Load<Material>(resource);
            if (m != null) return m;
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[DSTS] Missing Resources/{resource} and shader '{shaderName}'. Cel shading disabled.");
                return null;
            }
            return new Material(shader);
        }

        public static bool IsOutline(Material m)
        {
            var b = OutlineBase;
            return m != null && b != null && m.shader == b.shader;
        }

        public static bool IsCel(Material m)
        {
            if (m == null) return false;
            var t = ToonBase;
            return (t != null && m.shader == t.shader) || IsOutline(m);
        }

        class Info
        {
            public string name;
            public string role;
            public int baseTex = -1;
            public int uvSet;
            public int normalTex = -1;
            public string baseImage;
            public Color baseColor = Color.white;
            public Color tint = Color.black;
            public bool hasTint;
            public bool doubleSided;
            public string alphaMode = "OPAQUE";
            public bool alphaTest;
            public float cutoff = 0.5f;
        }

        /// <param name="json">GLB JSON chunk (DSTSGlbJson.ReadGlbJson).</param>
        /// <param name="textureByIndex">glTF texture index -> loaded texture (e.g. GltfImport.GetTexture).</param>
        public static Material[] Build(Dictionary<string, object> json, Func<int, Texture2D> textureByIndex)
        {
            var images = DSTSGlbJson.Arr(json, "images") ?? new List<object>();
            var textures = DSTSGlbJson.Arr(json, "textures") ?? new List<object>();
            var materials = DSTSGlbJson.Arr(json, "materials") ?? new List<object>();

            // image name -> first texture index that uses it
            var texOfImage = new Dictionary<string, int>();
            for (int ti = 0; ti < textures.Count; ti++)
            {
                int si = (int)DSTSGlbJson.Num(textures[ti], "source", -1);
                if (si < 0 || si >= images.Count) continue;
                string n = DSTSGlbJson.Str(images[si], "name");
                if (n != null && !texOfImage.ContainsKey(n)) texOfImage[n] = ti;
            }

            Texture2D ByIndex(int ti) => ti >= 0 && ti < textures.Count ? textureByIndex(ti) : null;
            Texture2D ByImage(string n) => n != null && texOfImage.TryGetValue(n, out int ti) ? ByIndex(ti) : null;

            var rootExtras = DSTSGlbJson.Obj(json, "extras");
            var clut = DSTSGlbJson.Obj(DSTSGlbJson.Obj(rootExtras, "dsts_env"), "clut");
            Texture2D ramp = ByImage(DSTSGlbJson.Str(clut, "key"));
            Texture2D fill = ByImage(DSTSGlbJson.Str(clut, "fill"));
            if (fill == null) fill = ramp;

            var result = new Material[materials.Count];
            for (int i = 0; i < materials.Count; i++)
            {
                var g = Parse(i, materials[i]);
                Texture2D baseTex = g.baseTex >= 0 ? ByIndex(g.baseTex) : ByImage(g.baseImage);
                result[i] = Create(g, baseTex, ByIndex(g.normalTex), ramp, fill);
            }
            return result;
        }

        static Info Parse(int index, object m)
        {
            var extras = DSTSGlbJson.Obj(m, "extras");
            var dsts = DSTSGlbJson.Obj(extras, "dsts");
            var render = DSTSGlbJson.Obj(extras, "dsts_render");
            var pbr = DSTSGlbJson.Obj(m, "pbrMetallicRoughness");

            var g = new Info { name = DSTSGlbJson.Str(m, "name") ?? $"material_{index}" };
            g.role = DSTSGlbJson.Str(render, "role") ?? (g.name == "outline" ? "outline_inflation" : "chr_cel");

            var bct = DSTSGlbJson.Obj(pbr, "baseColorTexture");
            if (bct != null)
            {
                g.baseTex = (int)DSTSGlbJson.Num(bct, "index", -1);
                g.uvSet = Mathf.Clamp((int)DSTSGlbJson.Num(bct, "texCoord", 0), 0, 2);
            }
            g.baseImage = DSTSGlbJson.Str(DSTSGlbJson.Obj(dsts, "texture_slots"), "DiffuseColor");

            var nt = DSTSGlbJson.Obj(m, "normalTexture");
            if (nt != null) g.normalTex = (int)DSTSGlbJson.Num(nt, "index", -1);

            var bcf = DSTSGlbJson.Arr(pbr, "baseColorFactor");
            if (bcf != null && bcf.Count >= 4) g.baseColor = new Color(F(bcf[0]), F(bcf[1]), F(bcf[2]), F(bcf[3]));

            var u25 = DSTSGlbJson.Arr(DSTSGlbJson.Obj(dsts, "uniforms"), "25");
            if (u25 != null && u25.Count >= 3) { g.tint = new Color(F(u25[0]), F(u25[1]), F(u25[2]), 1); g.hasTint = true; }

            g.doubleSided = DSTSGlbJson.Bool(m, "doubleSided") || DSTSGlbJson.Str(render, "cull") == "none";
            string gltfAlpha = DSTSGlbJson.Str(m, "alphaMode") ?? "OPAQUE";
            g.alphaMode = DSTSGlbJson.Str(render, "alpha_mode") ?? (render == null ? gltfAlpha : "OPAQUE");
            g.alphaTest = DSTSGlbJson.Bool(render, "alpha_test") || DSTSGlbJson.Has(render, "mask_cutoff")
                       || gltfAlpha == "MASK" || g.role == "mask_alpha";
            g.cutoff = (float)DSTSGlbJson.Num(render, "mask_cutoff", DSTSGlbJson.Num(m, "alphaCutoff", 0.5));
            return g;
        }

        static float F(object o) => o is double d ? (float)d : 0f;

        enum Surface { Opaque, Cutout, Transparent, Additive }

        static Material Create(Info g, Texture2D baseTex, Texture2D normal, Texture2D ramp, Texture2D fill)
        {
            if (g.role == "outline_inflation")
            {
                if (OutlineBase == null) return null;
                var o = new Material(OutlineBase) { name = g.name + " (DSTS outline)" };
                var c = g.hasTint ? g.tint : Color.black;
                c.a = 1;
                o.SetColor("_OutlineColor", c);
                o.SetFloat("_OutlineWidth", DefaultOutlineWidth * OutlineWidthScale);
                o.SetFloat("_NormalSign", -1f);
                o.SetFloat("_Cull", (float)CullMode.Front);
                return o;
            }

            if (ToonBase == null) return null;
            var m = new Material(ToonBase) { name = g.name + " (DSTS cel)" };
            m.SetTexture("_BaseMap", baseTex);
            m.SetFloat("_UVSet", g.uvSet);
            m.SetColor("_BaseColor", g.baseColor);
            m.SetTexture("_BumpMap", normal);
            SetKeyword(m, "_NORMALMAP", normal != null);
            m.SetTexture("_RampMap", ramp);
            m.SetTexture("_FillRampMap", fill);
            m.SetFloat("_UseRamp", ramp != null ? 1f : 0f);
            m.SetFloat("_Cull", g.doubleSided ? (float)CullMode.Off : (float)CullMode.Back);
            m.SetFloat("_Cutoff", g.cutoff);

            bool eyeShine = g.role == "eye_shine";
            m.SetFloat("_Unlit", eyeShine ? 1f : 0f);
            Surface s = eyeShine ? Surface.Additive
                      : g.alphaTest ? Surface.Cutout
                      : string.Equals(g.alphaMode, "BLEND", StringComparison.OrdinalIgnoreCase) ? Surface.Transparent
                      : Surface.Opaque;
            ApplySurface(m, s);
            return m;
        }

        static void ApplySurface(Material m, Surface s)
        {
            switch (s)
            {
                case Surface.Opaque:
                    SetBlend(m, BlendMode.One, BlendMode.Zero, true);
                    m.SetOverrideTag("RenderType", "Opaque");
                    m.renderQueue = -1;
                    break;
                case Surface.Cutout:
                    SetBlend(m, BlendMode.One, BlendMode.Zero, true);
                    m.SetOverrideTag("RenderType", "TransparentCutout");
                    m.renderQueue = (int)RenderQueue.AlphaTest;
                    break;
                case Surface.Transparent:
                    SetBlend(m, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, false);
                    m.SetOverrideTag("RenderType", "Transparent");
                    m.renderQueue = (int)RenderQueue.Transparent;
                    break;
                case Surface.Additive:
                    SetBlend(m, BlendMode.SrcAlpha, BlendMode.One, false);
                    m.SetOverrideTag("RenderType", "Transparent");
                    m.renderQueue = (int)RenderQueue.Transparent + 10;
                    break;
            }
            SetKeyword(m, "_ALPHATEST_ON", s == Surface.Cutout);
            bool solid = s == Surface.Opaque || s == Surface.Cutout;
            m.SetShaderPassEnabled("ShadowCaster", solid);
            m.SetShaderPassEnabled("DepthOnly", solid);
        }

        static void SetBlend(Material m, BlendMode src, BlendMode dst, bool zWrite)
        {
            m.SetFloat("_SrcBlend", (float)src);
            m.SetFloat("_DstBlend", (float)dst);
            m.SetFloat("_ZWrite", zWrite ? 1f : 0f);
        }

        static void SetKeyword(Material m, string kw, bool on)
        {
            if (on) m.EnableKeyword(kw); else m.DisableKeyword(kw);
        }
    }
}
