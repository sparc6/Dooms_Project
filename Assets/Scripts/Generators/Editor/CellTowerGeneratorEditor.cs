#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CellTowerGenerator))]
public sealed class CellTowerGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();
        serializedObject.ApplyModifiedProperties();

        var generator = (CellTowerGenerator)target;
        EditorGUILayout.Space();

        if (Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "Cell tower generation is disabled in Play Mode.",
                MessageType.Info);
        }
        else if (!generator.TryGetValidationMessage(out string validationMessage))
        {
            EditorGUILayout.HelpBox(validationMessage, MessageType.Warning);
        }
        else if (generator.SkipRandomSection)
        {
            EditorGUILayout.HelpBox(
                $"Level {generator.EffectiveSkippedLevel} will omit its platform and antennas.",
                MessageType.Info);
        }

        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Generate / Rebuild"))
                generator.Generate();
            if (GUILayout.Button("Randomize & Generate"))
                generator.RandomizeAndGenerate();
            if (GUILayout.Button("Clear"))
                generator.ClearGenerated();
            EditorGUILayout.EndHorizontal();
        }
    }
}
#endif
