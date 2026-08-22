#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

public static class CellTowerConfigAutoAssignment
{
    public const string DefaultConfigPath =
        "Assets/ART/Environment/Props/CellTower/CellTowerConfig.asset";

    private const string GeometryPath =
        "Assets/ART/Environment/Props/CellTower/Geometry/";

    private static readonly string[] AntennaIds =
    {
        "01", "03", "04", "06", "10", "11", "13", "15"
    };

    [MenuItem("Tools/The Tower/Cell Tower/Create or Update Default Config")]
    public static void CreateOrUpdateDefaultConfig()
    {
        CellTowerConfig config = AssetDatabase.LoadAssetAtPath<CellTowerConfig>(DefaultConfigPath);
        bool created = config == null;
        if (created)
        {
            config = ScriptableObject.CreateInstance<CellTowerConfig>();
            AssetDatabase.CreateAsset(config, DefaultConfigPath);
        }

        AssignMeshes(config, created);
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeObject = config;
        Debug.Log($"Cell tower config {(created ? "created" : "updated")}: {DefaultConfigPath}", config);
    }

    public static void AssignMeshes(CellTowerConfig config, bool resetCalibration = false)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        Undo.RecordObject(config, "Auto-Assign Cell Tower Meshes");
        var serializedConfig = new SerializedObject(config);

        SetModule(
            serializedConfig.FindProperty("centralPole"),
            "SM_Post_01",
            CellTowerConfig.LengthAxis.Z,
            true,
            resetCalibration);
        SetModule(
            serializedConfig.FindProperty("radialSupport"),
            "SM_Support_01",
            CellTowerConfig.LengthAxis.X,
            true,
            resetCalibration);
        SetModule(
            serializedConfig.FindProperty("verticalHorizontalSupport"),
            "SM_Support_02",
            CellTowerConfig.LengthAxis.X,
            true,
            resetCalibration);
        SetModule(
            serializedConfig.FindProperty("diagonalSupport"),
            "SM_Support_03",
            CellTowerConfig.LengthAxis.X,
            true,
            resetCalibration);
        SetModule(
            serializedConfig.FindProperty("ladder"),
            "SM_Ladder",
            CellTowerConfig.LengthAxis.Z,
            true,
            resetCalibration);

        SetWeightedModules(
            serializedConfig.FindProperty("threeSidedPlatforms"),
            new[] { "SM_Platform_03_A", "SM_Platform_03_B", "SM_Platform_03_C" },
            new[] { 2f, 1f, 1f },
            resetCalibration,
            new Vector3(-90f, 0f, 0f));
        SetWeightedModules(
            serializedConfig.FindProperty("fourSidedPlatforms"),
            new[] { "SM_Platform_04_A", "SM_Platform_04_B" },
            new[] { 1f, 1f },
            resetCalibration,
            new Vector3(-90f, 0f, 0f));
        SetWeightedModules(
            serializedConfig.FindProperty("fiveSidedPlatforms"),
            new[] { "SM_Platform_05_A", "SM_Platform_05_B" },
            new[] { 1f, 1f },
            resetCalibration,
            new Vector3(-90f, 0f, 0f));

        SerializedProperty antennaPairs = serializedConfig.FindProperty("antennaPairs");
        antennaPairs.arraySize = AntennaIds.Length;
        for (int index = 0; index < AntennaIds.Length; index++)
        {
            string id = AntennaIds[index];
            SerializedProperty pair = antennaPairs.GetArrayElementAtIndex(index);
            pair.FindPropertyRelative("id").stringValue = id;
            pair.FindPropertyRelative("weight").floatValue = 1f;
            SetModule(
                pair.FindPropertyRelative("antenna"),
                $"SM_Antenna_{id}",
                CellTowerConfig.LengthAxis.Z,
                false,
                resetCalibration);
            SetModule(
                pair.FindPropertyRelative("frame"),
                $"SM_Antenna_{id}_Frame",
                CellTowerConfig.LengthAxis.Z,
                false,
                resetCalibration);
        }

        serializedConfig.ApplyModifiedProperties();
        EditorUtility.SetDirty(config);
    }

    private static void SetWeightedModules(
        SerializedProperty array,
        string[] modelNames,
        float[] weights,
        bool resetCalibration,
        Vector3 rotationOffset)
    {
        array.arraySize = modelNames.Length;
        for (int index = 0; index < modelNames.Length; index++)
        {
            SerializedProperty weightedModule = array.GetArrayElementAtIndex(index);
            weightedModule.FindPropertyRelative("weight").floatValue = weights[index];
            SetModule(
                weightedModule.FindPropertyRelative("module"),
                modelNames[index],
                CellTowerConfig.LengthAxis.X,
                true,
                resetCalibration,
                rotationOffset);
        }
    }

    private static void SetModule(
        SerializedProperty module,
        string modelName,
        CellTowerConfig.LengthAxis axis,
        bool centerOnPlacement,
        bool resetCalibration,
        Vector3 rotationOffset = default)
    {
        SerializedProperty prefab = module.FindPropertyRelative("prefab");
        bool initializeCalibration = resetCalibration || prefab.objectReferenceValue == null;
        prefab.objectReferenceValue = LoadModel(modelName);
        if (!initializeCalibration)
            return;

        module.FindPropertyRelative("lengthAxis").enumValueIndex = (int)axis;
        module.FindPropertyRelative("referenceLength").floatValue = 0f;
        module.FindPropertyRelative("positionOffset").vector3Value = Vector3.zero;
        module.FindPropertyRelative("rotationOffset").vector3Value = rotationOffset;
        module.FindPropertyRelative("scaleMultiplier").vector3Value = Vector3.one;
        module.FindPropertyRelative("centerOnPlacement").boolValue = centerOnPlacement;
    }

    private static GameObject LoadModel(string modelName)
    {
        string path = $"{GeometryPath}{modelName}.FBX";
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (model == null)
            throw new InvalidOperationException($"Cell tower model was not found: {path}");

        return model;
    }
}
#endif
