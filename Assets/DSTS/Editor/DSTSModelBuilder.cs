using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DCGO.DSTS.EditorTools
{
    public class DSTSModelMeta
    {
        public string kind;      // "digimon" | "npcs"
        public string slug;
        public string name;
        public string jpName;
        public string chrId;
        public string category;  // digimon | npc | boss
    }

    /// <summary>Reads _DSTSModels/manifest.csv written by Download-DSTSModels.ps1.</summary>
    public static class DSTSManifest
    {
        public static string DownloadRoot => Path.Combine(DSTSModelBuilder.ProjectRoot, "_DSTSModels");
        public static string DefaultSourceRoot => Path.Combine(DownloadRoot, "original");

        public static Dictionary<string, DSTSModelMeta> Load(string manifestPath = null)
        {
            var result = new Dictionary<string, DSTSModelMeta>(StringComparer.OrdinalIgnoreCase);
            manifestPath = manifestPath ?? Path.Combine(DownloadRoot, "manifest.csv");
            if (!File.Exists(manifestPath)) return result;

            var lines = File.ReadAllLines(manifestPath, Encoding.UTF8);
            if (lines.Length == 0) return result;
            var header = ParseCsvLine(lines[0].TrimStart('﻿'));
            int Col(string n) => header.FindIndex(h => string.Equals(h, n, StringComparison.OrdinalIgnoreCase));
            int cKind = Col("kind"), cSlug = Col("slug"), cName = Col("name"), cJp = Col("jp_name"), cChr = Col("chr_id"), cCat = Col("category");
            string Get(List<string> row, int c) => c >= 0 && c < row.Count ? row[c] : null;

            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var row = ParseCsvLine(lines[i]);
                var meta = new DSTSModelMeta
                {
                    kind = Get(row, cKind), slug = Get(row, cSlug), name = Get(row, cName),
                    jpName = Get(row, cJp), chrId = Get(row, cChr), category = Get(row, cCat),
                };
                if (string.IsNullOrEmpty(meta.kind) || string.IsNullOrEmpty(meta.slug)) continue;
                result[meta.kind + "/" + meta.slug] = meta;
            }
            return result;
        }

        static List<string> ParseCsvLine(string line)
        {
            var cells = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else quoted = false;
                    }
                    else sb.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { cells.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            cells.Add(sb.ToString());
            return cells;
        }
    }

    /// <summary>
    /// Turns an imported DSTS .glb (glTFast) into a ready-to-use prefab:
    ///  * animation clips copied out as friendly-named .anim files with proper loop flags
    ///  * an AnimatorController (non-looping states return to Idle automatically)
    ///  * a DSTSModel component for playing animations by name or code
    /// </summary>
    public static class DSTSModelBuilder
    {
        public const string Root = "Assets/DSTS";
        public const string ModelsRoot = Root + "/Models";
        public const string PrefabsRoot = Root + "/Prefabs";
        const float CrossFade = 0.15f;

        public static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        public static string FullPath(string assetPath) => Path.Combine(ProjectRoot, assetPath);
        public static string GlbAssetPath(string kind, string slug) => $"{ModelsRoot}/{kind}/{slug}/{slug}.glb";
        public static string PrefabPath(string kind, string slug) => $"{PrefabsRoot}/{kind}/{slug}.prefab";

        // ------------------------------------------------------------------ menus

        [MenuItem("Assets/DCGO DSTS/Build Prefab From GLB", true)]
        static bool ValidateBuildSelected()
            => Selection.objects.Any(o => AssetDatabase.GetAssetPath(o).EndsWith(".glb", StringComparison.OrdinalIgnoreCase));

        [MenuItem("Assets/DCGO DSTS/Build Prefab From GLB")]
        static void BuildSelected()
        {
            var paths = Selection.objects.Select(o => AssetDatabase.GetAssetPath(o))
                .Where(p => p.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
            BuildMany(paths);
        }

        [MenuItem("DCGO/DSTS/Rebuild All Imported Prefabs")]
        static void RebuildAll()
        {
            string full = FullPath(ModelsRoot);
            if (!Directory.Exists(full)) { Debug.LogWarning("[DSTS] Nothing imported yet: " + ModelsRoot); return; }
            var paths = Directory.GetFiles(full, "*.glb", SearchOption.AllDirectories)
                .Select(f => "Assets" + f.Substring(Application.dataPath.Length).Replace('\\', '/'))
                .ToList();
            BuildMany(paths);
        }

        // ------------------------------------------------------------------ import + build

        /// <summary>Copies .glb files into the project, lets glTFast import them, then builds prefabs.</summary>
        public static void ImportFromSource(IList<KeyValuePair<string, DSTSModelMeta>> filesAndMeta)
        {
            var assetPaths = new List<string>();
            try
            {
                for (int i = 0; i < filesAndMeta.Count; i++)
                {
                    var meta = filesAndMeta[i].Value;
                    EditorUtility.DisplayProgressBar("DSTS", $"Copying {meta.slug}", (float)i / filesAndMeta.Count);
                    string dst = GlbAssetPath(meta.kind, meta.slug);
                    Directory.CreateDirectory(Path.GetDirectoryName(FullPath(dst)));
                    File.Copy(filesAndMeta[i].Key, FullPath(dst), true);
                    assetPaths.Add(dst);
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            EditorUtility.DisplayProgressBar("DSTS", "Importing GLB files (glTFast)...", 0.5f);
            try { AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport); }
            finally { EditorUtility.ClearProgressBar(); }

            BuildMany(assetPaths);
        }

        public static void BuildMany(IList<string> glbAssetPaths)
        {
            var manifest = DSTSManifest.Load();
            int ok = 0;
            try
            {
                for (int i = 0; i < glbAssetPaths.Count; i++)
                {
                    string p = glbAssetPaths[i];
                    if (EditorUtility.DisplayCancelableProgressBar("DSTS", $"Building prefab {Path.GetFileNameWithoutExtension(p)} ({i + 1}/{glbAssetPaths.Count})", (float)i / glbAssetPaths.Count))
                        break;
                    try
                    {
                        BuildPrefab(p, MetaFor(p, manifest));
                        ok++;
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[DSTS] Failed to build {p}: {e.Message}\n{e}");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            Debug.Log($"[DSTS] Built {ok}/{glbAssetPaths.Count} prefab(s) into {PrefabsRoot}");
        }

        static DSTSModelMeta MetaFor(string glbAssetPath, Dictionary<string, DSTSModelMeta> manifest)
        {
            string slug = Path.GetFileNameWithoutExtension(glbAssetPath);
            string kind = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(glbAssetPath)));
            if (kind != "digimon" && kind != "npcs") kind = "other";
            if (manifest.TryGetValue(kind + "/" + slug, out var m)) return m;
            return new DSTSModelMeta { kind = kind, slug = slug, name = slug };
        }

        public static GameObject BuildPrefab(string glbAssetPath, DSTSModelMeta meta)
        {
            var json = DSTSJson.ReadGlbJson(FullPath(glbAssetPath));
            var src = AssetDatabase.LoadMainAssetAtPath(glbAssetPath) as GameObject;
            if (src == null)
                throw new Exception("GLB did not import as a GameObject. Is the glTFast package (com.unity.cloud.gltfast) installed?");

            string folder = Path.GetDirectoryName(glbAssetPath).Replace('\\', '/');
            string slug = meta.slug;

            // -------- sub-assets from glTFast
            var subAssets = AssetDatabase.LoadAllAssetsAtPath(glbAssetPath);
            var clips = subAssets.OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal))
                .OrderBy(c => c.name, StringComparer.Ordinal)
                .ToList();

            var rootExtras = DSTSJson.Obj(json, "extras");
            float fieldGuideScale = (float)DSTSJson.Num(DSTSJson.Obj(rootExtras, "dsts"), "field_guide_scale", 1.0);

            // -------- animation clips -> friendly .anim assets
            string animDir = folder + "/Animations";
            EnsureFolder(animDir);
            var built = new List<KeyValuePair<AnimationClip, DSTSClipInfo>>();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var clip in clips)
            {
                if (DSTSAnimNames.IsBindPose(clip.name)) continue;
                string code = DSTSAnimNames.CodeOf(clip.name);
                string friendly = SafeName(DSTSAnimNames.Friendly(code));
                string unique = friendly;
                for (int n = 2; !usedNames.Add(unique); n++) unique = $"{friendly} {n}";
                bool loop = DSTSAnimNames.IsLoop(code);

                var copy = Object.Instantiate(clip);
                copy.name = unique;
                var settings = AnimationUtility.GetAnimationClipSettings(copy);
                settings.loopTime = loop;
                AnimationUtility.SetAnimationClipSettings(copy, settings);

                string clipPath = $"{animDir}/{unique}.anim";
                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                AnimationClip saved;
                if (existing != null)
                {
                    EditorUtility.CopySerialized(copy, existing);
                    existing.name = unique;
                    EditorUtility.SetDirty(existing);
                    Object.DestroyImmediate(copy);
                    saved = existing;
                }
                else
                {
                    AssetDatabase.CreateAsset(copy, clipPath);
                    saved = copy;
                }
                built.Add(new KeyValuePair<AnimationClip, DSTSClipInfo>(saved, new DSTSClipInfo
                {
                    friendlyName = unique, code = code, loop = loop, length = clip.length,
                }));
            }

            // -------- animator controller
            string ctrlPath = $"{folder}/{slug}.controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath) != null) AssetDatabase.DeleteAsset(ctrlPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            var sm = ctrl.layers[0].stateMachine;
            var states = new Dictionary<string, AnimatorState>();
            for (int k = 0; k < built.Count; k++)
            {
                var st = sm.AddState(built[k].Value.friendlyName, new Vector3(300 + (k % 4) * 260, k / 4 * 60, 0));
                st.motion = built[k].Key;
                states[built[k].Value.friendlyName] = st;
            }

            string defaultState = DSTSAnimNames.DefaultStatePreference.FirstOrDefault(states.ContainsKey);
            if (defaultState == null)
            {
                var firstLoop = built.FirstOrDefault(b => b.Value.loop);
                defaultState = firstLoop.Value != null ? firstLoop.Value.friendlyName
                             : built.Count > 0 ? built[0].Value.friendlyName : null;
            }
            if (defaultState != null)
            {
                var defSt = states[defaultState];
                sm.defaultState = defSt;
                foreach (var b in built)
                {
                    if (b.Value.loop || b.Value.friendlyName == defaultState) continue;
                    var tr = states[b.Value.friendlyName].AddTransition(defSt);
                    float len = Mathf.Max(b.Key.length, 0.01f);
                    tr.hasExitTime = true;
                    tr.exitTime = Mathf.Clamp01(1f - CrossFade / len);
                    tr.hasFixedDuration = true;
                    tr.duration = CrossFade;
                }
            }
            EditorUtility.SetDirty(ctrl);

            // -------- prefab
            string prefabPath = PrefabPath(meta.kind, slug);
            EnsureFolder(Path.GetDirectoryName(prefabPath).Replace('\\', '/'));
            var inst = (GameObject)Object.Instantiate(src);
            inst.name = slug;
            try
            {
                var legacy = inst.GetComponent<Animation>();
                if (legacy != null) Object.DestroyImmediate(legacy);
                var animator = inst.GetComponent<Animator>();
                if (animator == null) animator = inst.AddComponent<Animator>();
                animator.runtimeAnimatorController = ctrl;
                animator.applyRootMotion = false;

                var model = inst.GetComponent<DSTSModel>();
                if (model == null) model = inst.AddComponent<DSTSModel>();
                model.slug = slug;
                model.displayName = string.IsNullOrEmpty(meta.name) ? slug : meta.name;
                model.chrId = meta.chrId;
                model.category = meta.category;
                model.fieldGuideScale = fieldGuideScale;
                model.defaultState = defaultState ?? "";
                model.clips = built.Select(b => b.Value).ToList();

                var prefab = PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(inst);
            }
        }

        // ------------------------------------------------------------------ utils

        public static void EnsureFolder(string assetFolder)
        {
            assetFolder = assetFolder.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(assetFolder)) return;
            string parent = Path.GetDirectoryName(assetFolder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(assetFolder));
        }

        static string SafeName(string s)
        {
            var bad = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(s.Length);
            foreach (char c in s) sb.Append(Array.IndexOf(bad, c) >= 0 || c == '.' ? '_' : c);
            return sb.ToString().Trim();
        }
    }
}
