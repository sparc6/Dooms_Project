#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ScaffoldingSplineGenerator))]
public sealed class ScaffoldingSplineGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();
        serializedObject.ApplyModifiedProperties();

        var generator = (ScaffoldingSplineGenerator)target;

        EditorGUILayout.Space();
        if (Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "Scaffolding generation is disabled in Play Mode.",
                MessageType.Info);
        }
        else if (!generator.TryGetValidationMessage(out string validationMessage))
        {
            EditorGUILayout.HelpBox(validationMessage, MessageType.Warning);
        }

        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Generate / Rebuild"))
                generator.Rebuild();

            if (GUILayout.Button("Clear"))
                generator.ClearGenerated();
            EditorGUILayout.EndHorizontal();
        }
    }
}
#endif
