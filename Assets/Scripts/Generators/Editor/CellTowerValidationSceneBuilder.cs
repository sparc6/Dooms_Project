using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CellTowerValidationSceneBuilder
{
    public const string ScenePath =
        "Assets/ART/Environment/Props/CellTower/CellTowerValidation.unity";

    private static readonly int[] Seeds = { 1103, 2207, 3313, 4421, 5527, 6637 };
    private static readonly int[] LevelCounts = { 15, 16, 17, 18, 19, 20 };
    private static readonly int[] SideCounts = { 3, 4, 5, 3, 4, 5 };

    [MenuItem("Tools/The Tower/Cell Tower/Create Validation Scene")]
    public static void CreateOrUpdateValidationScene()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CellTowerConfig config = AssetDatabase.LoadAssetAtPath<CellTowerConfig>(
            CellTowerConfigAutoAssignment.DefaultConfigPath);
        if (config == null)
        {
            CellTowerConfigAutoAssignment.CreateOrUpdateDefaultConfig();
            config = AssetDatabase.LoadAssetAtPath<CellTowerConfig>(
                CellTowerConfigAutoAssignment.DefaultConfigPath);
        }

        string validationMessage = "Could not create the default cell tower config.";
        if (config == null || !config.TryValidate(out validationMessage))
        {
            throw new InvalidOperationException(validationMessage);
        }

        var validationRoot = new GameObject("Cell Tower Validation");

        for (int index = 0; index < Seeds.Length; index++)
        {
            var towerObject = new GameObject($"Cell Tower {index + 1:00}");
            towerObject.transform.SetParent(validationRoot.transform, false);
            towerObject.transform.localPosition = new Vector3((index - 2.5f) * 10f, 0f, 0f);

            CellTowerGenerator generator = towerObject.AddComponent<CellTowerGenerator>();
            var serializedGenerator = new SerializedObject(generator);
            serializedGenerator.Update();
            serializedGenerator.FindProperty("config").objectReferenceValue = config;
            serializedGenerator.FindProperty("seed").intValue = Seeds[index];
            serializedGenerator.FindProperty("levelCount").intValue = LevelCounts[index];
            serializedGenerator.FindProperty("startingLevel").intValue = LevelCounts[index] - 8;
            serializedGenerator.FindProperty("ladderStartingLevel").intValue = 0;
            serializedGenerator.FindProperty("sideCount").intValue = SideCounts[index];
            serializedGenerator.FindProperty("radius").floatValue = 2.2f;
            serializedGenerator.FindProperty("levelHeight").floatValue = 2.5f;
            serializedGenerator.FindProperty("skipRandomSection").boolValue = (index & 1) != 0;
            serializedGenerator.FindProperty("antennaDensity").floatValue = 0.65f;
            serializedGenerator.ApplyModifiedPropertiesWithoutUndo();

            if (generator.Config != config)
                throw new InvalidOperationException($"Could not assign the config to tower {index + 1}.");

            generator.Generate();
            if (towerObject.transform.Find("Generated Cell Tower") == null)
                throw new InvalidOperationException($"Tower {index + 1} was not generated.");
        }

        CreateCamera(validationRoot.transform);
        CreateLight(validationRoot.transform);

        if (!EditorSceneManager.SaveScene(scene, ScenePath))
            throw new InvalidOperationException($"Could not save validation scene at {ScenePath}.");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"Cell tower validation scene created: {ScenePath}");
    }

    private static void CreateCamera(Transform parent)
    {
        var cameraObject = new GameObject("Validation Camera");
        cameraObject.transform.SetParent(parent, false);
        cameraObject.transform.position = new Vector3(0f, 26f, -72f);
        cameraObject.transform.rotation = Quaternion.LookRotation(
            new Vector3(0f, 24f, 0f) - cameraObject.transform.position,
            Vector3.up);

        Camera camera = cameraObject.AddComponent<Camera>();
        camera.fieldOfView = 48f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 250f;
        cameraObject.tag = "MainCamera";
    }

    private static void CreateLight(Transform parent)
    {
        var lightObject = new GameObject("Validation Sun");
        lightObject.transform.SetParent(parent, false);
        lightObject.transform.rotation = Quaternion.Euler(42f, -28f, 0f);

        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
    }
}
