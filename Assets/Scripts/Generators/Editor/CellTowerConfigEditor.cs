#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CellTowerConfig))]
public sealed class CellTowerConfigEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Generation", EditorStyles.boldLabel);
        SerializedProperty generatedLayer = serializedObject.FindProperty("generatedLayer");
        generatedLayer.intValue = EditorGUILayout.LayerField(
            new GUIContent(
                "Generated Layer",
                "Applied recursively to the generated root, groups, prefab roots and mesh children."),
            generatedLayer.intValue);

        DrawPropertiesExcluding(serializedObject, "m_Script", "generatedLayer");
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space();
        if (GUILayout.Button("Auto-Assign Meshes"))
        {
            CellTowerConfigAutoAssignment.AssignMeshes((CellTowerConfig)target);
            AssetDatabase.SaveAssets();
        }
    }
}
#endif
