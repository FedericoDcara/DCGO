using UnityEditor;
using UnityEngine;

namespace DCGO.DSTS.EditorTools
{
    /// <summary>Adds "play animation" buttons to the DSTSModel inspector (Play Mode).</summary>
    [CustomEditor(typeof(DSTSModel))]
    public class DSTSModelEditor : UnityEditor.Editor
    {
        bool showButtons = true;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var model = (DSTSModel)target;

            EditorGUILayout.Space();
            showButtons = EditorGUILayout.Foldout(showButtons, $"Play animation ({model.clips.Count} clips)", true);
            if (!showButtons) return;

            if (!Application.isPlaying)
                EditorGUILayout.HelpBox("Enter Play Mode to test animations.", MessageType.None);

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                const int perRow = 3;
                for (int i = 0; i < model.clips.Count; i += perRow)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        for (int j = i; j < Mathf.Min(i + perRow, model.clips.Count); j++)
                        {
                            var c = model.clips[j];
                            if (GUILayout.Button(new GUIContent(c.friendlyName, $"{c.code}  {c.length:0.00}s{(c.loop ? "  loop" : "")}")))
                                model.Play(c.friendlyName);
                        }
                    }
                }
            }
        }
    }
}
