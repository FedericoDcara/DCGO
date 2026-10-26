using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DCGO.DSTS.EditorTools
{
    /// <summary>DCGO > DSTS > Import Models... : pick downloaded models and turn them into prefabs.</summary>
    public class DSTSImportWindow : EditorWindow
    {
        const string PrefKey = "DCGO.DSTS.SourceRoot";

        class Entry
        {
            public DSTSModelMeta meta;
            public string file;
            public bool imported;
            public bool selected;
        }

        readonly List<Entry> entries = new List<Entry>();
        string sourceRoot;
        string search = "";
        bool showDigimon = true, showNpcs = true, hideImported;
        Vector2 scroll;

        [MenuItem("DCGO/DSTS/Import Models...")]
        public static void Open() => GetWindow<DSTSImportWindow>("DSTS Models").Show();

        void OnEnable()
        {
            sourceRoot = EditorPrefs.GetString(PrefKey, DSTSManifest.DefaultSourceRoot);
            Scan();
        }

        void Scan()
        {
            entries.Clear();
            string parent = Path.GetDirectoryName(sourceRoot.TrimEnd('/', '\\')) ?? sourceRoot;
            var manifest = DSTSManifest.Load(Path.Combine(parent, "manifest.csv"));
            foreach (var kind in new[] { "digimon", "npcs" })
            {
                string dir = Path.Combine(sourceRoot, kind);
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.GetFiles(dir, "*.glb").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    string slug = Path.GetFileNameWithoutExtension(f);
                    if (!manifest.TryGetValue(kind + "/" + slug, out var meta))
                        meta = new DSTSModelMeta { kind = kind, slug = slug, name = slug };
                    entries.Add(new Entry
                    {
                        meta = meta,
                        file = f,
                        imported = File.Exists(DSTSModelBuilder.FullPath(DSTSModelBuilder.PrefabPath(kind, slug))),
                    });
                }
            }
        }

        IEnumerable<Entry> Filtered()
        {
            string q = search.Trim();
            return entries.Where(e =>
                (e.meta.kind == "digimon" ? showDigimon : showNpcs) &&
                (!hideImported || !e.imported) &&
                (q.Length == 0 ||
                 Contains(e.meta.name, q) || Contains(e.meta.slug, q) ||
                 Contains(e.meta.jpName, q) || Contains(e.meta.category, q)));
        }

        static bool Contains(string s, string q) => s != null && s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Copies the selected .glb files into Assets/DSTS/Models, imports them with glTFast, and builds a prefab for each in Assets/DSTS/Prefabs " +
                "(toon + outline materials, friendly-named animations, Animator that returns to Idle).",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                sourceRoot = EditorGUILayout.TextField("Download folder", sourceRoot);
                if (GUILayout.Button("Browse", GUILayout.Width(70)))
                {
                    string picked = EditorUtility.OpenFolderPanel("Folder containing digimon/ and npcs/", sourceRoot, "");
                    if (!string.IsNullOrEmpty(picked)) sourceRoot = picked;
                }
                if (EditorGUI.EndChangeCheck()) { EditorPrefs.SetString(PrefKey, sourceRoot); Scan(); }
                if (GUILayout.Button("Rescan", GUILayout.Width(70))) Scan();
            }

            if (entries.Count == 0)
            {
                EditorGUILayout.HelpBox($"No .glb files found in\n{sourceRoot}\\digimon or \\npcs.\nRun _DSTSModels/Download-DSTSModels.ps1 first.", MessageType.Warning);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                showDigimon = GUILayout.Toggle(showDigimon, "Digimon", "Button", GUILayout.Width(80));
                showNpcs = GUILayout.Toggle(showNpcs, "NPCs / Bosses", "Button", GUILayout.Width(110));
                hideImported = GUILayout.Toggle(hideImported, "Hide imported", "Button", GUILayout.Width(110));
                GUILayout.FlexibleSpace();
            }
            search = EditorGUILayout.TextField("Search", search);

            var shown = Filtered().ToList();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button($"Select shown ({shown.Count})")) shown.ForEach(e => e.selected = true);
                if (GUILayout.Button("Clear selection")) entries.ForEach(e => e.selected = false);
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var e in shown)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    e.selected = EditorGUILayout.ToggleLeft(e.meta.name ?? e.meta.slug, e.selected, GUILayout.Width(240));
                    GUILayout.Label(e.meta.slug, EditorStyles.miniLabel, GUILayout.Width(190));
                    GUILayout.Label(string.IsNullOrEmpty(e.meta.category) ? e.meta.kind : e.meta.category, EditorStyles.miniLabel, GUILayout.Width(60));
                    GUILayout.Label(e.imported ? "imported" : "", EditorStyles.miniBoldLabel);
                }
            }
            EditorGUILayout.EndScrollView();

            int count = entries.Count(e => e.selected);
            using (new EditorGUI.DisabledScope(count == 0))
            {
                if (GUILayout.Button($"Import / rebuild {count} selected", GUILayout.Height(30)))
                {
                    var items = entries.Where(e => e.selected)
                        .Select(e => new KeyValuePair<string, DSTSModelMeta>(e.file, e.meta)).ToList();
                    DSTSModelBuilder.ImportFromSource(items);
                    Scan();
                    GUIUtility.ExitGUI();
                }
            }
        }
    }
}
