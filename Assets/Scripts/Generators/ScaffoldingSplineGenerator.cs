using UnityEngine;
using UnityEngine.Splines;

#if UNITY_EDITOR
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

[ExecuteAlways]
public sealed class ScaffoldingSplineGenerator : MonoBehaviour
{
    private const string GeneratedRootName = "Generated Scaffolding";

    [Header("Source")]
    [SerializeField] private SplineContainer splineContainer;
    [SerializeField] private ScaffoldingConfig scaffoldingConfig;

    [Header("Layout")]
    [SerializeField, Min(0)] private int maximumFloors = 1;
    [SerializeField] private AnimationCurve heightAlongSpline = AnimationCurve.Constant(0f, 1f, 1f);

    [Header("Generation")]
    [SerializeField] private bool autoRebuildInEditMode = true;
    [SerializeField, Min(0f)] private float rebuildDelay = 0.2f;
    [SerializeField, HideInInspector] private Transform generatedRoot;

#if UNITY_EDITOR
    [System.NonSerialized] private bool rebuildQueued;
    [System.NonSerialized] private double scheduledRebuildTime;
    [System.NonSerialized] private Matrix4x4 lastSplineMatrix;
    [System.NonSerialized] private bool hasLastSplineMatrix;

    private void Reset()
    {
        splineContainer = GetComponent<SplineContainer>();
    }

    private void OnEnable()
    {
        Spline.Changed += OnSplineChanged;
        ScaffoldingConfig.Changed += OnScaffoldingConfigChanged;
        CaptureSplineMatrix();
        QueueAutoRebuild();
    }

    private void OnDisable()
    {
        Spline.Changed -= OnSplineChanged;
        ScaffoldingConfig.Changed -= OnScaffoldingConfigChanged;
        CancelQueuedRebuild();
    }

    private void OnValidate()
    {
        if (splineContainer == null)
            splineContainer = GetComponent<SplineContainer>();

        QueueAutoRebuild();
    }

    private void Update()
    {
        if (Application.isPlaying || !autoRebuildInEditMode || splineContainer == null)
            return;

        Matrix4x4 currentMatrix = splineContainer.transform.localToWorldMatrix;
        if (!hasLastSplineMatrix || currentMatrix != lastSplineMatrix)
        {
            lastSplineMatrix = currentMatrix;
            hasLastSplineMatrix = true;
            QueueRebuild();
        }
    }

    private void OnSplineChanged(Spline changedSpline, int knotIndex, SplineModification modification)
    {
        if (!ContainsSpline(changedSpline))
            return;

        QueueAutoRebuild();
    }

    private void OnScaffoldingConfigChanged(ScaffoldingConfig changedConfig)
    {
        if (changedConfig == scaffoldingConfig)
            QueueAutoRebuild();
    }

    private bool ContainsSpline(Spline spline)
    {
        if (splineContainer == null)
            return false;

        for (int index = 0; index < splineContainer.Splines.Count; index++)
        {
            if (ReferenceEquals(splineContainer.Splines[index], spline))
                return true;
        }

        return false;
    }

    private void QueueAutoRebuild()
    {
        if (!Application.isPlaying && autoRebuildInEditMode)
            QueueRebuild();
        else
            CancelQueuedRebuild();
    }

    private void QueueRebuild()
    {
        if (!isActiveAndEnabled || EditorUtility.IsPersistent(this))
            return;

        scheduledRebuildTime = EditorApplication.timeSinceStartup + Mathf.Max(0f, rebuildDelay);
        if (rebuildQueued)
            return;

        rebuildQueued = true;
        EditorApplication.update += ProcessQueuedRebuild;
    }

    private void ProcessQueuedRebuild()
    {
        if (EditorApplication.timeSinceStartup < scheduledRebuildTime)
            return;

        if (this == null || Application.isPlaying || !isActiveAndEnabled || !autoRebuildInEditMode)
        {
            CancelQueuedRebuild();
            return;
        }

        Rebuild();
    }

    [ContextMenu("Generate / Rebuild")]
    public void Rebuild()
    {
        if (Application.isPlaying || EditorUtility.IsPersistent(this))
            return;

        CancelQueuedRebuild();

        if (!TryGetValidationMessage(out _))
            return;

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Rebuild Scaffolding");

        Transform root = GetOrCreateGeneratedRoot();
        ClearChildren(root);

        for (int splineIndex = 0; splineIndex < splineContainer.Splines.Count; splineIndex++)
        {
            Spline spline = splineContainer.Splines[splineIndex];
            if (spline == null || spline.Count < 2)
                continue;

            using (var worldSpline = new NativeSpline(
                spline,
                splineContainer.transform.localToWorldMatrix,
                Allocator.Temp))
            {
                float splineLength = worldSpline.GetLength();
                if (splineLength > Mathf.Epsilon && maximumFloors > 0)
                    GenerateSections(worldSpline, splineLength, root);
            }
        }

        CaptureSplineMatrix();
        EditorUtility.SetDirty(this);
        if (gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(gameObject.scene);

        Undo.CollapseUndoOperations(undoGroup);
    }

    [ContextMenu("Clear Generated")]
    public void ClearGenerated()
    {
        if (Application.isPlaying || EditorUtility.IsPersistent(this))
            return;

        CancelQueuedRebuild();

        Transform root = FindGeneratedRoot();
        if (root == null)
            return;

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Clear Scaffolding");
        ClearChildren(root);
        EditorUtility.SetDirty(this);
        if (gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
        Undo.CollapseUndoOperations(undoGroup);
    }

    public bool TryGetValidationMessage(out string message)
    {
        if (splineContainer == null)
        {
            message = "Assign a Spline Container.";
            return false;
        }

        if (splineContainer.Splines.Count == 0)
        {
            message = "The Spline Container does not contain any splines.";
            return false;
        }

        bool hasValidSpline = false;
        for (int index = 0; index < splineContainer.Splines.Count; index++)
        {
            Spline spline = splineContainer.Splines[index];
            if (spline != null && spline.Count >= 2)
            {
                hasValidSpline = true;
                break;
            }
        }

        if (!hasValidSpline)
        {
            message = "At least one spline must contain two or more knots.";
            return false;
        }

        if (scaffoldingConfig == null)
        {
            message = "Assign a Scaffolding Config.";
            return false;
        }

        if (scaffoldingConfig.SectionPrefab == null)
        {
            message = "Assign a section prefab in the Scaffolding Config.";
            return false;
        }

        if (!PrefabUtility.IsPartOfPrefabAsset(scaffoldingConfig.SectionPrefab))
        {
            message = "Section Prefab must reference a prefab asset, not a scene object.";
            return false;
        }

        if (scaffoldingConfig.SectionLength <= 0f || scaffoldingConfig.FloorHeight <= 0f)
        {
            message = "Section Length and Floor Height in the Scaffolding Config must be greater than zero.";
            return false;
        }

        if (maximumFloors < 0)
        {
            message = "Maximum Floors cannot be negative.";
            return false;
        }

        if (rebuildDelay < 0f)
        {
            message = "Rebuild Delay cannot be negative.";
            return false;
        }

        if (heightAlongSpline == null)
        {
            message = "Assign a Height Along Spline curve.";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private void GenerateSections(NativeSpline worldSpline, float splineLength, Transform root)
    {
        int columnCount = Mathf.Max(1, Mathf.CeilToInt(splineLength / scaffoldingConfig.SectionLength));
        Quaternion lastRotation = transform.rotation;
        bool hasValidRotation = false;

        for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            float normalizedDistance = (columnIndex + 0.5f) / columnCount;
            float distance = normalizedDistance * splineLength;
            float splineT = SplineUtility.GetNormalizedInterpolation(
                worldSpline,
                distance,
                PathIndexUnit.Distance);
            Vector3 basePosition = (Vector3)worldSpline.EvaluatePosition(splineT);
            Vector3 tangent = (Vector3)worldSpline.EvaluateTangent(splineT);

            if (TryGetYawRotation(tangent, out Quaternion rotation))
            {
                lastRotation = rotation;
                hasValidRotation = true;
            }
            else if (!hasValidRotation)
            {
                lastRotation = Quaternion.identity;
            }

            float normalizedHeight = Mathf.Clamp01(heightAlongSpline.Evaluate(normalizedDistance));
            int floorCount = Mathf.RoundToInt(normalizedHeight * maximumFloors);

            for (int floorIndex = 0; floorIndex < floorCount; floorIndex++)
            {
                Object instanceObject = PrefabUtility.InstantiatePrefab(scaffoldingConfig.SectionPrefab, root);
                if (!(instanceObject is GameObject instance))
                    continue;

                Undo.RegisterCreatedObjectUndo(instance, "Create Scaffolding Section");
                Vector3 position = basePosition + Vector3.up * (floorIndex * scaffoldingConfig.FloorHeight);
                instance.transform.SetPositionAndRotation(position, lastRotation);
            }
        }
    }

    private bool TryGetYawRotation(Vector3 tangent, out Quaternion rotation)
    {
        Vector3 horizontalDirection = Vector3.ProjectOnPlane(tangent, Vector3.up);
        if (horizontalDirection.sqrMagnitude < 0.000001f)
        {
            rotation = Quaternion.identity;
            return false;
        }

        horizontalDirection.Normalize();
        rotation = Quaternion.LookRotation(horizontalDirection, Vector3.up);
        if (scaffoldingConfig.ForwardAxis == ScaffoldingConfig.SectionForwardAxis.X)
            rotation *= Quaternion.Euler(0f, -90f, 0f);

        return true;
    }

    private Transform GetOrCreateGeneratedRoot()
    {
        Transform root = FindGeneratedRoot();
        if (root != null)
            return root;

        var rootObject = new GameObject(GeneratedRootName);
        Undo.RegisterCreatedObjectUndo(rootObject, "Create Scaffolding Root");
        Undo.SetTransformParent(rootObject.transform, transform, "Parent Scaffolding Root");
        rootObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        rootObject.transform.localScale = Vector3.one;

        Undo.RecordObject(this, "Assign Scaffolding Root");
        generatedRoot = rootObject.transform;
        return generatedRoot;
    }

    private Transform FindGeneratedRoot()
    {
        if (generatedRoot != null && generatedRoot.parent == transform)
            return generatedRoot;

        return null;
    }

    private static void ClearChildren(Transform root)
    {
        for (int index = root.childCount - 1; index >= 0; index--)
            Undo.DestroyObjectImmediate(root.GetChild(index).gameObject);
    }

    private void CaptureSplineMatrix()
    {
        if (splineContainer == null)
        {
            hasLastSplineMatrix = false;
            return;
        }

        lastSplineMatrix = splineContainer.transform.localToWorldMatrix;
        hasLastSplineMatrix = true;
    }

    private void CancelQueuedRebuild()
    {
        EditorApplication.update -= ProcessQueuedRebuild;
        rebuildQueued = false;
    }
#endif
}
