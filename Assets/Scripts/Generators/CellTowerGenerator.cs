using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

[DisallowMultipleComponent]
public sealed class CellTowerGenerator : MonoBehaviour
{
    private const string DefaultConfigPath =
        "Assets/ART/Environment/Props/CellTower/CellTowerConfig.asset";
    private const string GeneratedRootName = "Generated Cell Tower";

    [Header("Source")]
    [SerializeField] private CellTowerConfig config;

    [Header("Tower")]
    [SerializeField] private int seed = 12345;
    [SerializeField, Min(3)] private int levelCount = 18;
    [SerializeField, Min(0)] private int startingLevel = 9;
    [SerializeField, Min(0)] private int ladderStartingLevel;
    [SerializeField, Range(3, 5)] private int sideCount = 4;
    [SerializeField, Min(0.01f)] private float radius = 2.2f;
    [SerializeField, Min(0.01f)] private float levelHeight = 2.5f;
    [SerializeField] private bool skipRandomSection = true;
    [SerializeField, Range(0f, 1f)] private float antennaDensity = 0.65f;

    [Header("Generated")]
    [SerializeField, HideInInspector] private Transform generatedRoot;

    public CellTowerConfig Config => config;
    public int Seed => seed;
    public int LevelCount => levelCount;
    public int StartingLevel => startingLevel;
    public int LadderStartingLevel => ladderStartingLevel;
    public int SideCount => sideCount;
    public float Radius => radius;
    public float LevelHeight => levelHeight;
    public bool SkipRandomSection => skipRandomSection;
    public float AntennaDensity => antennaDensity;
    public int EffectiveSkippedLevel => TryCreateLayoutSettings(out CellTowerLayoutSettings settings)
        ? CellTowerLayout.GetSkippedLevel(settings)
        : -1;

    private void Reset()
    {
#if UNITY_EDITOR
        config = AssetDatabase.LoadAssetAtPath<CellTowerConfig>(DefaultConfigPath);
#endif
        ClampParameters();
    }

    private void OnValidate()
    {
        ClampParameters();
    }

    [ContextMenu("Generate / Rebuild")]
    public void Generate()
    {
#if UNITY_EDITOR
        if (!CanRunEditorOperation())
            return;

        if (!TryGetValidationMessage(out string validationMessage))
        {
            Debug.LogWarning(validationMessage, this);
            return;
        }

        RunGenerationUndoGroup("Generate Cell Tower");
#else
        Debug.LogWarning("Cell tower generation is available only in the Unity Editor.", this);
#endif
    }

    [ContextMenu("Randomize & Generate")]
    public void RandomizeAndGenerate()
    {
#if UNITY_EDITOR
        if (!CanRunEditorOperation())
            return;

        string configMessage = "Assign a CellTowerConfig.";
        if (config == null || !config.TryValidate(out configMessage))
        {
            Debug.LogWarning(configMessage, this);
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Randomize Cell Tower");
        Undo.RecordObject(this, "Randomize Cell Tower Parameters");

        seed = CreateFreshSeed();
        levelCount = CellTowerLayout.RangeInclusive(
            seed,
            100,
            0,
            0,
            config.MinimumLevels,
            config.MaximumLevels);

        int minimumStartingLevel = Mathf.Max(1, levelCount - config.StartingLevelMinimumOffset);
        int maximumStartingLevel = Mathf.Min(
            levelCount - 2,
            levelCount - config.StartingLevelMaximumOffset);
        startingLevel = CellTowerLayout.RangeInclusive(
            seed,
            101,
            0,
            0,
            minimumStartingLevel,
            maximumStartingLevel);
        ladderStartingLevel = 0;
        sideCount = CellTowerLayout.RangeInclusive(seed, 102, 0, 0, 3, 5);
        skipRandomSection = CellTowerLayout.Value01(seed, 103, 0, 0) < 0.5f;

        if (!TryGetValidationMessage(out string validationMessage))
        {
            Undo.RevertAllDownToGroup(undoGroup);
            Debug.LogWarning(validationMessage, this);
            return;
        }

        try
        {
            RebuildGeneratedHierarchy();
            FinishEditorOperation();
            Undo.CollapseUndoOperations(undoGroup);
        }
        catch (Exception exception)
        {
            Undo.RevertAllDownToGroup(undoGroup);
            Debug.LogException(exception, this);
        }
#else
        Debug.LogWarning("Cell tower randomization is available only in the Unity Editor.", this);
#endif
    }

    [ContextMenu("Clear Generated")]
    public void ClearGenerated()
    {
#if UNITY_EDITOR
        if (!CanRunEditorOperation())
            return;

        Transform root = FindGeneratedRoot();
        if (root == null)
            return;

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Clear Cell Tower");
        Undo.RecordObject(this, "Clear Cell Tower Reference");
        generatedRoot = null;
        Undo.DestroyObjectImmediate(root.gameObject);
        FinishEditorOperation();
        Undo.CollapseUndoOperations(undoGroup);
#endif
    }

    public bool TryGetValidationMessage(out string message)
    {
        if (config == null)
        {
            message = "Assign a CellTowerConfig.";
            return false;
        }

        if (!config.TryValidate(out message))
            return false;
        if (levelCount < 3)
        {
            message = "Level Count must be at least three.";
            return false;
        }
        if (startingLevel < 0 || startingLevel >= levelCount)
        {
            message = "Starting Level must be inside the tower level range.";
            return false;
        }
        if (ladderStartingLevel < 0 || ladderStartingLevel >= levelCount - 1)
        {
            message = "Ladder Starting Level must be between zero and Level Count minus two.";
            return false;
        }
        if (sideCount < 3 || sideCount > 5)
        {
            message = "Side Count must be between three and five.";
            return false;
        }
        if (radius <= 0f || levelHeight <= 0f)
        {
            message = "Radius and Level Height must be positive.";
            return false;
        }
        if (antennaDensity < 0f || antennaDensity > 1f)
        {
            message = "Antenna Density must be between zero and one.";
            return false;
        }
        if (skipRandomSection && startingLevel + 2 > levelCount - 3)
        {
            message = "The tower is too short above Starting Level to skip a safe section.";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private bool TryCreateLayoutSettings(out CellTowerLayoutSettings settings)
    {
        if (levelCount < 2
            || startingLevel < 0
            || startingLevel >= levelCount
            || ladderStartingLevel < 0
            || ladderStartingLevel >= levelCount - 1
            || sideCount < 3
            || sideCount > 5
            || radius <= 0f
            || levelHeight <= 0f
            || antennaDensity < 0f
            || antennaDensity > 1f)
        {
            settings = default;
            return false;
        }

        settings = new CellTowerLayoutSettings(
            seed,
            levelCount,
            startingLevel,
            sideCount,
            radius,
            levelHeight,
            skipRandomSection,
            antennaDensity,
            ladderStartingLevel);
        return true;
    }

    private void ClampParameters()
    {
        levelCount = Mathf.Max(3, levelCount);
        startingLevel = Mathf.Clamp(startingLevel, 0, levelCount - 1);
        ladderStartingLevel = Mathf.Clamp(ladderStartingLevel, 0, levelCount - 2);
        sideCount = Mathf.Clamp(sideCount, 3, 5);
        radius = Mathf.Max(0.01f, radius);
        levelHeight = Mathf.Max(0.01f, levelHeight);
        antennaDensity = Mathf.Clamp01(antennaDensity);
    }

#if UNITY_EDITOR
    private void RunGenerationUndoGroup(string operationName)
    {
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(operationName);

        try
        {
            RebuildGeneratedHierarchy();
            FinishEditorOperation();
            Undo.CollapseUndoOperations(undoGroup);
        }
        catch (Exception exception)
        {
            Undo.RevertAllDownToGroup(undoGroup);
            Debug.LogException(exception, this);
        }
    }

    private void RebuildGeneratedHierarchy()
    {
        CellTowerLayoutSettings settings = new CellTowerLayoutSettings(
            seed,
            levelCount,
            startingLevel,
            sideCount,
            radius,
            levelHeight,
            skipRandomSection,
            antennaDensity,
            ladderStartingLevel);
        List<CellTowerPlacement> placements = CellTowerLayout.Build(settings);

        DestroyExistingRoot();
        Transform root = CreateGeneratedRoot();
        Transform structureRoot = CreateGroup(root, "Structure");
        Transform platformsRoot = CreateGroup(root, "Platforms");
        Transform ladderRoot = CreateGroup(root, "Ladder");
        Transform antennasRoot = CreateGroup(root, "Antennas");

        for (int index = 0; index < placements.Count; index++)
        {
            CellTowerPlacement placement = placements[index];
            switch (placement.Kind)
            {
                case CellTowerPlacementKind.CentralPole:
                    PlaceBeam(
                        config.CentralPole,
                        structureRoot,
                        placement,
                        $"Pole_L{placement.Level:00}",
                        placement.LateralScale);
                    break;
                case CellTowerPlacementKind.RadialSupport:
                    PlaceBeam(
                        config.RadialSupport,
                        structureRoot,
                        placement,
                        $"Radial_L{placement.Level:00}_S{placement.Side:00}");
                    break;
                case CellTowerPlacementKind.HorizontalSupport:
                    PlaceBeam(
                        config.VerticalHorizontalSupport,
                        structureRoot,
                        placement,
                        $"Horizontal_L{placement.Level:00}_S{placement.Side:00}");
                    break;
                case CellTowerPlacementKind.VerticalSupport:
                    PlaceBeam(
                        config.VerticalHorizontalSupport,
                        structureRoot,
                        placement,
                        $"Vertical_L{placement.Level:00}_S{placement.Side:00}");
                    break;
                case CellTowerPlacementKind.DiagonalSupport:
                    PlaceBeam(
                        config.DiagonalSupport,
                        structureRoot,
                        placement,
                        $"Diagonal_L{placement.Level:00}_S{placement.Side:00}");
                    break;
                case CellTowerPlacementKind.Platform:
                    CellTowerConfig.WeightedModule platform = config.PickPlatform(
                        sideCount,
                        placement.RandomValue);
                    PlaceModule(
                        platform.Module,
                        platformsRoot,
                        placement.Position,
                        placement.Rotation,
                        placement.UniformScale,
                        $"Platform_L{placement.Level:00}_{platform.Module.Prefab.name}");
                    break;
                case CellTowerPlacementKind.Ladder:
                    PlaceBeam(
                        config.Ladder,
                        ladderRoot,
                        placement,
                        $"Ladder_L{placement.Level:00}");
                    break;
                case CellTowerPlacementKind.Antenna:
                    PlaceAntennaPair(antennasRoot, placement);
                    break;
            }
        }

        SetLayerRecursively(root.gameObject, config.GeneratedLayer);
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        root.layer = layer;
        Transform rootTransform = root.transform;
        for (int index = 0; index < rootTransform.childCount; index++)
            SetLayerRecursively(rootTransform.GetChild(index).gameObject, layer);
    }

    private Transform CreateGeneratedRoot()
    {
        var rootObject = new GameObject(GeneratedRootName);
        Undo.RegisterCreatedObjectUndo(rootObject, "Create Cell Tower Root");
        Undo.SetTransformParent(rootObject.transform, transform, "Parent Cell Tower Root");
        rootObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        rootObject.transform.localScale = Vector3.one;

        CellTowerGeneratedRootMarker marker = Undo.AddComponent<CellTowerGeneratedRootMarker>(rootObject);
        marker.Initialize(this);
        EditorUtility.SetDirty(marker);

        Undo.RecordObject(this, "Assign Cell Tower Root");
        generatedRoot = rootObject.transform;
        return generatedRoot;
    }

    private static Transform CreateGroup(Transform parent, string groupName)
    {
        var groupObject = new GameObject(groupName);
        Undo.RegisterCreatedObjectUndo(groupObject, "Create Cell Tower Group");
        Undo.SetTransformParent(groupObject.transform, parent, "Parent Cell Tower Group");
        groupObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        groupObject.transform.localScale = Vector3.one;
        return groupObject.transform;
    }

    private void PlaceAntennaPair(Transform parent, CellTowerPlacement placement)
    {
        CellTowerConfig.AntennaPair pair = config.PickAntenna(placement.RandomValue);
        if (pair == null)
            return;

        Transform mount = CreateGroup(
            parent,
            $"Antenna_L{placement.Level:00}_S{placement.Side:00}_{pair.Id}");
        PlaceModule(
            pair.Frame,
            mount,
            placement.Position,
            placement.Rotation,
            placement.UniformScale,
            pair.Frame.Prefab.name);
        PlaceModule(
            pair.Antenna,
            mount,
            placement.Position,
            placement.Rotation,
            placement.UniformScale,
            pair.Antenna.Prefab.name);
    }

    private static void PlaceBeam(
        CellTowerConfig.Module module,
        Transform parent,
        CellTowerPlacement placement,
        string objectName,
        float lateralScale = 1f)
    {
        Vector3 direction = placement.End - placement.Start;
        float distance = direction.magnitude;
        if (distance <= Mathf.Epsilon)
            return;

        GameObject instance = InstantiateModule(module, parent, objectName, out Bounds localBounds);
        Quaternion correction = Quaternion.Euler(module.RotationOffset);
        Vector3 correctedAxis = correction * GetAxisVector(module.Axis);
        Quaternion rotation = Quaternion.FromToRotation(correctedAxis, direction / distance) * correction;

        Vector3 scale = module.ScaleMultiplier;
        float detectedLength = GetAxisComponent(localBounds.size, module.Axis);
        float referenceLength = module.ReferenceLength > Mathf.Epsilon
            ? module.ReferenceLength
            : detectedLength;
        if (referenceLength <= Mathf.Epsilon)
            referenceLength = 1f;

        SetAxisComponent(ref scale, module.Axis, GetAxisComponent(scale, module.Axis) * distance / referenceLength);
        MultiplyLateralAxes(ref scale, module.Axis, lateralScale);

        Vector3 position = placement.Position + rotation * module.PositionOffset;
        if (module.CenterOnPlacement)
            position -= rotation * Vector3.Scale(localBounds.center, scale);

        instance.transform.SetLocalPositionAndRotation(position, rotation);
        instance.transform.localScale = scale;
    }

    private static void PlaceModule(
        CellTowerConfig.Module module,
        Transform parent,
        Vector3 position,
        Quaternion rotation,
        float uniformScale,
        string objectName)
    {
        GameObject instance = InstantiateModule(module, parent, objectName, out Bounds localBounds);
        Quaternion finalRotation = rotation * Quaternion.Euler(module.RotationOffset);
        Vector3 finalScale = module.ScaleMultiplier * uniformScale;
        Vector3 finalPosition = position + finalRotation * module.PositionOffset;
        if (module.CenterOnPlacement)
            finalPosition -= finalRotation * Vector3.Scale(localBounds.center, finalScale);

        instance.transform.SetLocalPositionAndRotation(finalPosition, finalRotation);
        instance.transform.localScale = finalScale;
    }

    private static GameObject InstantiateModule(
        CellTowerConfig.Module module,
        Transform parent,
        string objectName,
        out Bounds localBounds)
    {
        UnityEngine.Object instanceObject = PrefabUtility.InstantiatePrefab(module.Prefab, parent);
        if (!(instanceObject is GameObject instance))
            throw new InvalidOperationException($"Could not instantiate {module.Prefab.name}.");

        Undo.RegisterCreatedObjectUndo(instance, "Create Cell Tower Part");
        instance.name = objectName;
        instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        instance.transform.localScale = Vector3.one;
        localBounds = CalculateLocalMeshBounds(instance);
        return instance;
    }

    private static Bounds CalculateLocalMeshBounds(GameObject instance)
    {
        MeshFilter[] filters = instance.GetComponentsInChildren<MeshFilter>(true);
        bool hasBounds = false;
        Bounds combined = new Bounds(Vector3.zero, Vector3.zero);
        Matrix4x4 worldToRoot = instance.transform.worldToLocalMatrix;

        for (int filterIndex = 0; filterIndex < filters.Length; filterIndex++)
        {
            MeshFilter filter = filters[filterIndex];
            if (filter.sharedMesh == null)
                continue;

            Bounds meshBounds = filter.sharedMesh.bounds;
            Matrix4x4 meshToRoot = worldToRoot * filter.transform.localToWorldMatrix;
            Vector3 min = meshBounds.min;
            Vector3 max = meshBounds.max;

            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 localCorner = new Vector3(
                    (corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y,
                    (corner & 4) == 0 ? min.z : max.z);
                Vector3 rootCorner = meshToRoot.MultiplyPoint3x4(localCorner);
                if (!hasBounds)
                {
                    combined = new Bounds(rootCorner, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(rootCorner);
                }
            }
        }

        return hasBounds ? combined : new Bounds(Vector3.zero, Vector3.one);
    }

    private void DestroyExistingRoot()
    {
        Transform root = FindGeneratedRoot();
        if (root == null)
            return;

        Undo.RecordObject(this, "Replace Cell Tower Root");
        generatedRoot = null;
        Undo.DestroyObjectImmediate(root.gameObject);
    }

    private Transform FindGeneratedRoot()
    {
        if (IsOwnedGeneratedRoot(generatedRoot))
            return generatedRoot;

        for (int index = 0; index < transform.childCount; index++)
        {
            Transform child = transform.GetChild(index);
            if (IsOwnedGeneratedRoot(child))
            {
                generatedRoot = child;
                return generatedRoot;
            }
        }

        return null;
    }

    private bool IsOwnedGeneratedRoot(Transform candidate)
    {
        if (candidate == null || candidate.parent != transform)
            return false;

        CellTowerGeneratedRootMarker marker = candidate.GetComponent<CellTowerGeneratedRootMarker>();
        return marker != null && marker.Owner == this;
    }

    private bool CanRunEditorOperation()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning("Cell tower generation is disabled in Play Mode.", this);
            return false;
        }

        if (EditorUtility.IsPersistent(this))
        {
            Debug.LogWarning("Open the prefab or place it in a scene before generating a cell tower.", this);
            return false;
        }

        return true;
    }

    private void FinishEditorOperation()
    {
        EditorUtility.SetDirty(this);
        if (gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }

    private static int CreateFreshSeed()
    {
        return unchecked((int)DateTime.UtcNow.Ticks ^ Environment.TickCount ^ Guid.NewGuid().GetHashCode());
    }

    private static Vector3 GetAxisVector(CellTowerConfig.LengthAxis axis)
    {
        return axis switch
        {
            CellTowerConfig.LengthAxis.X => Vector3.right,
            CellTowerConfig.LengthAxis.Y => Vector3.up,
            CellTowerConfig.LengthAxis.Z => Vector3.forward,
            _ => Vector3.right
        };
    }

    private static float GetAxisComponent(Vector3 vector, CellTowerConfig.LengthAxis axis)
    {
        return axis switch
        {
            CellTowerConfig.LengthAxis.X => vector.x,
            CellTowerConfig.LengthAxis.Y => vector.y,
            CellTowerConfig.LengthAxis.Z => vector.z,
            _ => vector.x
        };
    }

    private static void SetAxisComponent(
        ref Vector3 vector,
        CellTowerConfig.LengthAxis axis,
        float value)
    {
        switch (axis)
        {
            case CellTowerConfig.LengthAxis.X:
                vector.x = value;
                break;
            case CellTowerConfig.LengthAxis.Y:
                vector.y = value;
                break;
            case CellTowerConfig.LengthAxis.Z:
                vector.z = value;
                break;
        }
    }

    private static void MultiplyLateralAxes(
        ref Vector3 vector,
        CellTowerConfig.LengthAxis lengthAxis,
        float multiplier)
    {
        switch (lengthAxis)
        {
            case CellTowerConfig.LengthAxis.X:
                vector.y *= multiplier;
                vector.z *= multiplier;
                break;
            case CellTowerConfig.LengthAxis.Y:
                vector.x *= multiplier;
                vector.z *= multiplier;
                break;
            case CellTowerConfig.LengthAxis.Z:
                vector.x *= multiplier;
                vector.y *= multiplier;
                break;
        }
    }
#endif
}
