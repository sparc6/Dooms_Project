using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Splines;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Генератор заборов вдоль Unity Spline (только для редактора).
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(SplineContainer))]
public sealed class FenceSplineGenerator : MonoBehaviour
{
    private readonly struct TileRuntimeData
    {
        public TileRuntimeData(GameObject prefab, float length, Vector3 localStartAnchor, Vector3 localEndAnchor)
        {
            Prefab = prefab;
            Length = length;
            LocalStartAnchor = localStartAnchor;
            LocalEndAnchor = localEndAnchor;
        }

        public GameObject Prefab { get; }
        public float Length { get; }
        public Vector3 LocalStartAnchor { get; }
        public Vector3 LocalEndAnchor { get; }
    }

    [Header("Config")]
    [Tooltip("Конфиг генерации забора.")]
    [SerializeField] private FenceSplineGeneratorConfig _config;

    [Header("References")]
    [Tooltip("SplineContainer, по которому строится забор.")]
    [FormerlySerializedAs("m_SplineContainer")]
    [SerializeField] private SplineContainer _splineContainer;

    [Header("Editor")]
    [Tooltip("Автоматически перегенерировать в редакторе при изменении сплайна.")]
    [SerializeField] private bool _autoRebuildOnSplineChange = true;

    [Tooltip("Обходить точки сплайна в обратном порядке, чтобы быстро развернуть внешнюю и внутреннюю сторону забора.")]
    [SerializeField] private bool _reverseSplineTraversal;

    [Tooltip("Минимальный интервал автоперегенерации в секундах.")]
    [Min(0.01f)]
    [SerializeField] private float _autoRebuildIntervalSeconds = 0.15f;

    [SerializeField, HideInInspector] private Transform _generatedRoot;
    [NonSerialized] private bool _autoRebuildRequested;
    [NonSerialized] private double _nextAllowedAutoRebuildTime;

    private const float MinSplineLength = 0.001f;
    private const float MinProgressT = 0.00001f;
    private const float MinAdvanceChord = 0.02f;

    private void OnEnable()
    {
        if (ShouldSkipAnyGenerationLogic())
            return;

        EnsureReferences();
        EnsureGeneratedRoot();
        _autoRebuildRequested = false;
        _nextAllowedAutoRebuildTime = 0d;
        RegisterSplineCallbacks();
    }

    private void OnDisable()
    {
        UnregisterSplineCallbacks();
    }

    private void OnValidate()
    {
        EnsureReferences();
        EnsureGeneratedRoot();
        RequestAutoRebuild();
    }

    private void Update()
    {
        if (!_autoRebuildOnSplineChange || ShouldSkipAnyGenerationLogic())
            return;

#if UNITY_EDITOR
        ProcessAutoRebuild();
#endif
    }

    [ContextMenu("Rebuild Fence")]
    public void Rebuild()
    {
        if (ShouldSkipAnyGenerationLogic())
            return;

        EnsureReferences();
        EnsureGeneratedRoot();
        ClearGenerated();

        if (_config == null || !_config.Enabled)
            return;

        if (_splineContainer == null)
        {
            Debug.LogWarning($"{nameof(FenceSplineGenerator)}: SplineContainer не назначен.", this);
            return;
        }

        var validTiles = CollectValidTiles(_config.Tiles, _config.TileLengthAxis, _config.TileOrientationAxis);
        if (validTiles.Count == 0)
        {
            Debug.LogWarning($"{nameof(FenceSplineGenerator)}: Нет валидных тайлов (Prefab + автоматически рассчитанная длина > 0).", this);
            return;
        }

        validTiles.Sort((a, b) => b.Length.CompareTo(a.Length));

        var splines = _splineContainer.Splines;
        if (splines == null || splines.Count == 0)
            return;

        for (var splineIndex = 0; splineIndex < splines.Count; splineIndex++)
        {
            GenerateForSpline(splines[splineIndex], splineIndex, validTiles);
        }
    }

    [ContextMenu("Clear Fence")]
    public void ClearGenerated()
    {
        if (_generatedRoot == null)
            return;

        var immediate = ShouldDestroyImmediate();
        for (var i = _generatedRoot.childCount - 1; i >= 0; i--)
        {
            var child = _generatedRoot.GetChild(i);
            if (child == null)
                continue;

#if UNITY_EDITOR
            if (immediate)
                DestroyImmediate(child.gameObject);
            else
                Destroy(child.gameObject);
#else
            Destroy(child.gameObject);
#endif
        }
    }

    private void GenerateForSpline(Spline spline, int splineIndex, List<TileRuntimeData> sortedTiles)
    {
        if (spline == null || spline.Count < 2)
            return;

        var splineLength = spline.GetLength();
        if (splineLength <= MinSplineLength)
            return;

        var trimStart = Mathf.Clamp(_config.TrimStartPercent, 0f, 100f) * 0.01f;
        var trimEnd = Mathf.Clamp(_config.TrimEndPercent, 0f, 100f) * 0.01f;
        if (trimEnd < trimStart)
        {
            var temp = trimStart;
            trimStart = trimEnd;
            trimEnd = temp;
        }

        var rangeStartDistance = splineLength * trimStart;
        var rangeEndDistance = splineLength * trimEnd;
        if (rangeEndDistance - rangeStartDistance <= MinSplineLength)
            return;

        var splineRoot = EnsureSplineRoot(splineIndex);
        if (splineRoot == null)
            return;

        if (IsLinearSpline(spline))
        {
            var linearRangeStartDistance = _reverseSplineTraversal ? splineLength - rangeEndDistance : rangeStartDistance;
            var linearRangeEndDistance = _reverseSplineTraversal ? splineLength - rangeStartDistance : rangeEndDistance;
            GenerateForLinearSpline(
                spline,
                sortedTiles,
                splineRoot,
                linearRangeStartDistance,
                linearRangeEndDistance);
            return;
        }

        var traversalSign = _reverseSplineTraversal ? -1f : 1f;
        var traversalStartDistance = _reverseSplineTraversal ? rangeEndDistance : rangeStartDistance;
        var traversalEndDistance = _reverseSplineTraversal ? rangeStartDistance : rangeEndDistance;

        spline.GetPointAtLinearDistance(0f, traversalStartDistance, out var currentT);
        var currentDistance = traversalStartDistance;
        var minTileLength = GetMinTileLength(sortedTiles);
        var probeStep = Mathf.Max(0.05f, _config.CurvatureProbeStep);
        var hasPlacedAnyTile = false;
        var lastPlacedTileEndPos = Vector3.zero;
        var lastPlacedTileForward = Vector3.forward;
        spline.GetPointAtLinearDistance(0f, traversalEndDistance, out var endT);
        SplineUtility.Evaluate(spline, endT, out var endLocalPos, out var _, out var _);
        var rangeEndPos = (Vector3)endLocalPos;

        while (GetRemainingTraversalDistance(currentDistance, traversalEndDistance, traversalSign) > MinSplineLength)
        {
            var remainingArc = GetRemainingTraversalDistance(currentDistance, traversalEndDistance, traversalSign);
            if (remainingArc < MinSplineLength)
                break;

            SplineUtility.Evaluate(spline, currentT, out var currentLocalPos, out var _, out var _);
            var currentPosForRemaining = (Vector3)currentLocalPos;
            var remainingChord = Vector3.Distance(currentPosForRemaining, rangeEndPos);
            if (remainingChord < minTileLength + Mathf.Max(0f, _config.TailSkipLength))
                break;

            var curvature = EstimateCurvatureDegPerMeter(spline, currentT, probeStep);
            var tileData = ChooseTileByCurvature(sortedTiles, curvature, remainingChord, _config.LowCurvatureDegPerMeter, _config.HighCurvatureDegPerMeter);
            if (!tileData.HasValue)
                break;

            var tile = tileData.Value;
            if (!TryGetNextTByChordLength(spline, currentT, tile.Length, remainingArc * traversalSign, out var nextT, out var consumedArc))
                break;

            SplineUtility.Evaluate(spline, currentT, out var localStartPos, out var _, out var _);
            SplineUtility.Evaluate(spline, nextT, out var localEndPos, out var _, out var _);

            var startPos = (Vector3)localStartPos;
            var endPos = (Vector3)localEndPos;

            var instance = InstantiatePrefabInstance(tile.Prefab, splineRoot);
            if (instance != null)
            {
                if (!TryBuildTransformFromAnchors(
                        tile.LocalStartAnchor,
                        tile.LocalEndAnchor,
                        startPos,
                        endPos,
                        Vector3.up,
                        out var localPosition,
                        out var localRotation))
                {
                    DestroyImmediateSafe(instance);
                    currentDistance += consumedArc;
                    currentT = nextT;
                    continue;
                }

                instance.name = tile.Prefab.name;
                instance.transform.localPosition = localPosition + GetAlignmentOffset(endPos - startPos);
                instance.transform.localRotation = localRotation;

                hasPlacedAnyTile = true;
                lastPlacedTileEndPos = endPos;
                var chordForward = endPos - startPos;
                if (chordForward.sqrMagnitude > 0.000001f)
                    lastPlacedTileForward = chordForward.normalized;
            }

            var desiredAdvanceChord = Mathf.Max(MinAdvanceChord, tile.Length + _config.TileGap);
            if (!TryGetNextTByChordLength(spline, currentT, desiredAdvanceChord, remainingArc * traversalSign, out var advanceT, out var advanceArc))
                break;

            currentDistance += advanceArc;
            currentT = advanceT;
        }

        if (_config.SpawnFinalPost && _config.FinalPostPrefab != null)
        {
            if (hasPlacedAnyTile)
            {
                SpawnFinalPostAtWorldPoint(lastPlacedTileEndPos, lastPlacedTileForward, splineRoot);
            }
            else
            {
                SpawnFinalPost(spline, traversalEndDistance, traversalSign, splineRoot);
            }
        }
    }

    private void GenerateForLinearSpline(
        Spline spline,
        IReadOnlyList<TileRuntimeData> sortedTiles,
        Transform splineRoot,
        float rangeStartDistance,
        float rangeEndDistance)
    {
        if (spline == null || sortedTiles == null || sortedTiles.Count == 0 || splineRoot == null)
            return;

        var totalLength = spline.GetLength();
        if (totalLength <= MinSplineLength)
            return;

        var points = BuildLinearSplinePoints(spline);
        if (points.Count < 2)
            return;

        if (_reverseSplineTraversal)
            points.Reverse();

        var minTileLength = GetMinTileLength(sortedTiles);
        var tailSkip = Mathf.Max(0f, _config.TailSkipLength);
        var hasPlacedAnyTile = false;
        var lastPlacedTileEndPos = Vector3.zero;
        var lastPlacedTileForward = Vector3.forward;
        var shouldSpawnLinearPosts = _config.SpawnPostsAtLinearPoints && _config.FinalPostPrefab != null;

        var traversed = 0f;
        for (var i = 0; i < points.Count - 1; i++)
        {
            var segmentStart = points[i];
            var segmentEnd = points[i + 1];
            var segmentLength = Vector3.Distance(segmentStart, segmentEnd);
            if (segmentLength <= MinSplineLength)
            {
                traversed += segmentLength;
                continue;
            }

            var segmentGlobalStart = traversed;
            var segmentGlobalEnd = traversed + segmentLength;
            traversed = segmentGlobalEnd;

            var startInSegment = Mathf.Max(rangeStartDistance, segmentGlobalStart);
            var endInSegment = Mathf.Min(rangeEndDistance, segmentGlobalEnd);
            var availableInSegment = endInSegment - startInSegment;
            if (availableInSegment <= MinSplineLength)
                continue;

            var segmentDir = (segmentEnd - segmentStart) / segmentLength;
            if (shouldSpawnLinearPosts && i > 0 && startInSegment <= segmentGlobalStart + MinSplineLength)
                SpawnFinalPostAtWorldPoint(segmentStart, segmentDir, splineRoot);

            var localDistance = startInSegment - segmentGlobalStart;
            var currentPos = segmentStart + segmentDir * localDistance;
            var segmentStartOffset = startInSegment - segmentGlobalStart;

            while (true)
            {
                var consumedInsideSegment = localDistance - segmentStartOffset;
                var remainingInSegment = availableInSegment - consumedInsideSegment;
                if (remainingInSegment <= MinSplineLength)
                    break;

                var currentGlobalDistance = startInSegment + consumedInsideSegment;
                var remainingToRangeEnd = rangeEndDistance - currentGlobalDistance;
                if (remainingToRangeEnd < minTileLength + tailSkip)
                    break;

                var tileData = ChooseTileByCurvature(
                    sortedTiles,
                    0f,
                    remainingInSegment,
                    _config.LowCurvatureDegPerMeter,
                    _config.HighCurvatureDegPerMeter);
                if (!tileData.HasValue)
                    break;

                var tile = tileData.Value;
                if (tile.Length > remainingInSegment + 0.0001f)
                    break;

                var nextPos = currentPos + segmentDir * tile.Length;
                var instance = InstantiatePrefabInstance(tile.Prefab, splineRoot);
                if (instance != null)
                {
                    if (!TryBuildTransformFromAnchors(
                            tile.LocalStartAnchor,
                            tile.LocalEndAnchor,
                            currentPos,
                            nextPos,
                            Vector3.up,
                            out var localPosition,
                            out var localRotation))
                    {
                        DestroyImmediateSafe(instance);
                        currentPos = nextPos;
                        localDistance += tile.Length;
                        continue;
                    }

                    instance.name = tile.Prefab.name;
                    instance.transform.localPosition = localPosition + GetAlignmentOffset(segmentDir);
                    instance.transform.localRotation = localRotation;

                    hasPlacedAnyTile = true;
                    lastPlacedTileEndPos = nextPos;
                    lastPlacedTileForward = segmentDir;
                }

                var advance = GetLinearTileAdvance(tile.Length);
                currentPos += segmentDir * advance;
                localDistance += advance;
            }
        }

        if (_config.SpawnFinalPost && _config.FinalPostPrefab != null)
        {
            if (hasPlacedAnyTile)
                SpawnFinalPostAtWorldPoint(lastPlacedTileEndPos, lastPlacedTileForward, splineRoot);
            else
                SpawnFinalPost(spline, _reverseSplineTraversal ? totalLength - rangeEndDistance : rangeEndDistance, _reverseSplineTraversal ? -1f : 1f, splineRoot);
        }
    }

    private void SpawnFinalPost(Spline spline, float distance, float traversalSign, Transform parent)
    {
        if (spline == null || parent == null)
            return;

        spline.GetPointAtLinearDistance(0f, distance, out var endT);
        SplineUtility.Evaluate(spline, endT, out var localPos, out var localTangent, out var _);

        var forward = (Vector3)localTangent;
        if (traversalSign < 0f)
            forward = -forward;

        var safeForward = forward.sqrMagnitude > 0.000001f ? forward.normalized : Vector3.forward;

        var rotation = Quaternion.identity;
        if (_config.AlignFinalPostToSpline)
            rotation = Quaternion.LookRotation(safeForward, Vector3.up);

        var instance = InstantiatePrefabInstance(_config.FinalPostPrefab, parent);
        if (instance == null)
            return;

        instance.name = _config.FinalPostPrefab.name;
        instance.transform.localPosition = (Vector3)localPos + GetAlignmentOffset(safeForward);
        instance.transform.localRotation = rotation * _config.FinalPostPrefab.transform.localRotation;
    }

    private void SpawnFinalPostAtWorldPoint(Vector3 worldPoint, Vector3 forward, Transform parent)
    {
        if (parent == null || _config == null || _config.FinalPostPrefab == null)
            return;

        var safeForward = forward.sqrMagnitude > 0.000001f ? forward.normalized : Vector3.forward;

        var rotation = Quaternion.identity;
        if (_config.AlignFinalPostToSpline)
            rotation = Quaternion.LookRotation(safeForward, Vector3.up);

        var instance = InstantiatePrefabInstance(_config.FinalPostPrefab, parent);
        if (instance == null)
            return;

        instance.name = _config.FinalPostPrefab.name;
        instance.transform.localPosition = worldPoint + GetAlignmentOffset(safeForward);
        instance.transform.localRotation = rotation * _config.FinalPostPrefab.transform.localRotation;
    }

    private Vector3 GetAlignmentOffset(Vector3 forward)
    {
        var horizontalForward = Vector3.ProjectOnPlane(forward, Vector3.up);
        var perpendicular = horizontalForward.sqrMagnitude > 0.000001f
            ? Vector3.Cross(Vector3.up, horizontalForward.normalized)
            : Vector3.right;

        return Vector3.up * _config.VerticalOffset + perpendicular * _config.PerpendicularOffset;
    }

    private float GetLinearTileAdvance(float tileLength)
    {
        var overlap = Mathf.Max(0f, _config.LinearTileOverlap);
        var requestedAdvance = tileLength + _config.TileGap;
        var maxSafeAdvance = tileLength - overlap;
        var clampedAdvance = Mathf.Min(requestedAdvance, maxSafeAdvance);
        return Mathf.Max(MinAdvanceChord, clampedAdvance);
    }

    private static List<TileRuntimeData> CollectValidTiles(
        IReadOnlyList<GameObject> source,
        FenceTileLengthAxis lengthAxis,
        FenceTileOrientationAxis orientationAxis)
    {
        var result = new List<TileRuntimeData>();
        if (source == null)
            return result;

        for (var i = 0; i < source.Count; i++)
        {
            var prefab = source[i];
            if (prefab == null)
                continue;

            if (!TryGetPrefabChordAnchors(prefab, lengthAxis, orientationAxis, out var localStartAnchor, out var localEndAnchor, out var length))
                continue;

            result.Add(new TileRuntimeData(prefab, length, localStartAnchor, localEndAnchor));
        }

        return result;
    }

    private static bool TryGetPrefabChordAnchors(
        GameObject prefab,
        FenceTileLengthAxis axis,
        FenceTileOrientationAxis orientationAxis,
        out Vector3 localStartAnchor,
        out Vector3 localEndAnchor,
        out float length)
    {
        localStartAnchor = Vector3.zero;
        localEndAnchor = Vector3.zero;
        length = 0f;

        if (prefab == null)
            return false;

        if (!TryGetPrefabLocalBounds(prefab, out var min, out var max))
            return false;

        var centerX = (min.x + max.x) * 0.5f;
        var centerZ = (min.z + max.z) * 0.5f;
        var baseY = min.y;
        var orientationAbsAxis = GetAbsoluteOrientationAxis(orientationAxis);
        switch (orientationAbsAxis)
        {
            case FenceTileLengthAxis.X:
                localStartAnchor = new Vector3(min.x, baseY, centerZ);
                localEndAnchor = new Vector3(max.x, baseY, centerZ);
                break;
            case FenceTileLengthAxis.Y:
                localStartAnchor = new Vector3(centerX, min.y, centerZ);
                localEndAnchor = new Vector3(centerX, max.y, centerZ);
                break;
            case FenceTileLengthAxis.Z:
                localStartAnchor = new Vector3(centerX, baseY, min.z);
                localEndAnchor = new Vector3(centerX, baseY, max.z);
                break;
            default:
                localStartAnchor = new Vector3(min.x, baseY, centerZ);
                localEndAnchor = new Vector3(max.x, baseY, centerZ);
                break;
        }

        ApplyOrientationAxis(ref localStartAnchor, ref localEndAnchor, orientationAxis);
        length = GetAxisExtent(min, max, axis);
        return length > 0.0001f;
    }

    private static void ApplyOrientationAxis(
        ref Vector3 localStartAnchor,
        ref Vector3 localEndAnchor,
        FenceTileOrientationAxis orientationAxis)
    {
        var invert = orientationAxis is
            FenceTileOrientationAxis.NegativeX or
            FenceTileOrientationAxis.NegativeY or
            FenceTileOrientationAxis.NegativeZ;

        if (!invert)
            return;

        var temp = localStartAnchor;
        localStartAnchor = localEndAnchor;
        localEndAnchor = temp;
    }

    private static FenceTileLengthAxis GetAbsoluteOrientationAxis(FenceTileOrientationAxis orientationAxis)
    {
        return orientationAxis switch
        {
            FenceTileOrientationAxis.PositiveX => FenceTileLengthAxis.X,
            FenceTileOrientationAxis.NegativeX => FenceTileLengthAxis.X,
            FenceTileOrientationAxis.PositiveY => FenceTileLengthAxis.Y,
            FenceTileOrientationAxis.NegativeY => FenceTileLengthAxis.Y,
            FenceTileOrientationAxis.PositiveZ => FenceTileLengthAxis.Z,
            FenceTileOrientationAxis.NegativeZ => FenceTileLengthAxis.Z,
            _ => FenceTileLengthAxis.X
        };
    }

    private static float GetAxisExtent(Vector3 min, Vector3 max, FenceTileLengthAxis axis)
    {
        return axis switch
        {
            FenceTileLengthAxis.X => Mathf.Abs(max.x - min.x),
            FenceTileLengthAxis.Y => Mathf.Abs(max.y - min.y),
            FenceTileLengthAxis.Z => Mathf.Abs(max.z - min.z),
            _ => Mathf.Abs(max.x - min.x)
        };
    }

    private static bool TryGetPrefabLocalBounds(GameObject prefab, out Vector3 min, out Vector3 max)
    {
        min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        if (prefab == null)
            return false;

        var renderers = prefab.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return false;

        var rootWorldToLocal = prefab.transform.worldToLocalMatrix;
        var hasBounds = false;

        for (var i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null)
                continue;

            var localBounds = renderer.localBounds;
            var localToRoot = rootWorldToLocal * renderer.localToWorldMatrix;
            var corners = GetBoundsCorners(localBounds);

            for (var c = 0; c < corners.Length; c++)
            {
                var p = localToRoot.MultiplyPoint3x4(corners[c]);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            hasBounds = true;
        }

        return hasBounds;
    }

    private static Vector3[] GetBoundsCorners(Bounds bounds)
    {
        var min = bounds.min;
        var max = bounds.max;
        return new[]
        {
            new Vector3(min.x, min.y, min.z),
            new Vector3(max.x, min.y, min.z),
            new Vector3(min.x, max.y, min.z),
            new Vector3(max.x, max.y, min.z),
            new Vector3(min.x, min.y, max.z),
            new Vector3(max.x, min.y, max.z),
            new Vector3(min.x, max.y, max.z),
            new Vector3(max.x, max.y, max.z)
        };
    }

    private static float GetMinTileLength(IReadOnlyList<TileRuntimeData> tiles)
    {
        var min = float.MaxValue;
        for (var i = 0; i < tiles.Count; i++)
        {
            var len = tiles[i].Length;
            if (len < min)
                min = len;
        }

        return min == float.MaxValue ? 0f : min;
    }

    private static TileRuntimeData? ChooseTileByCurvature(
        IReadOnlyList<TileRuntimeData> sortedTiles,
        float curvatureDegPerMeter,
        float remainingChordDistance,
        float lowCurvatureDegPerMeter,
        float highCurvatureDegPerMeter)
    {
        if (sortedTiles == null || sortedTiles.Count == 0)
            return null;

        var maxLen = sortedTiles[0].Length;
        var minLen = sortedTiles[sortedTiles.Count - 1].Length;

        var low = Mathf.Max(0f, lowCurvatureDegPerMeter);
        var high = Mathf.Max(low + 0.0001f, highCurvatureDegPerMeter);
        var curvature01 = Mathf.InverseLerp(low, high, curvatureDegPerMeter);
        var targetLen = Mathf.Lerp(maxLen, minLen, curvature01);

        TileRuntimeData? best = null;
        var bestScore = float.MaxValue;
        for (var i = 0; i < sortedTiles.Count; i++)
        {
            var tile = sortedTiles[i];
            if (tile.Length > remainingChordDistance + 0.0001f)
                continue;

            var score = Mathf.Abs(tile.Length - targetLen);
            if (score < bestScore)
            {
                bestScore = score;
                best = tile;
            }
        }

        if (best != null)
            return best;

        return sortedTiles[sortedTiles.Count - 1];
    }

    private static bool TryGetNextTByChordLength(
        Spline spline,
        float startT,
        float targetChordLength,
        float signedMaxArcStep,
        out float nextT,
        out float consumedArc)
    {
        nextT = startT;
        consumedArc = 0f;

        if (spline == null || targetChordLength <= 0.0001f || Mathf.Abs(signedMaxArcStep) <= 0.0001f)
            return false;

        SplineUtility.Evaluate(spline, startT, out var localStartPos, out var _, out var _);
        var startPos = (Vector3)localStartPos;

        var lowArc = 0f;
        var highArc = signedMaxArcStep;

        spline.GetPointAtLinearDistance(startT, highArc, out var highT);
        if (Mathf.Abs(highT - startT) <= MinProgressT)
            return false;

        SplineUtility.Evaluate(spline, highT, out var localHighPos, out var _, out var _);
        var maxChord = Vector3.Distance(startPos, (Vector3)localHighPos);
        if (maxChord <= 0.0001f)
            return false;

        if (maxChord <= targetChordLength + 0.0001f)
        {
            nextT = highT;
            consumedArc = highArc;
            return true;
        }

        for (var i = 0; i < 14; i++)
        {
            var midArc = (lowArc + highArc) * 0.5f;
            spline.GetPointAtLinearDistance(startT, midArc, out var midT);
            SplineUtility.Evaluate(spline, midT, out var localMidPos, out var _, out var _);
            var chord = Vector3.Distance(startPos, (Vector3)localMidPos);

            if (chord < targetChordLength)
                lowArc = midArc;
            else
                highArc = midArc;
        }

        consumedArc = highArc;
        spline.GetPointAtLinearDistance(startT, consumedArc, out nextT);
        return Mathf.Abs(nextT - startT) > MinProgressT;
    }

    private static float GetRemainingTraversalDistance(float currentDistance, float endDistance, float traversalSign)
    {
        return traversalSign >= 0f ? endDistance - currentDistance : currentDistance - endDistance;
    }

    private static bool IsLinearSpline(Spline spline)
    {
        if (spline == null || spline.Count < 2)
            return false;

        for (var i = 0; i < spline.Count; i++)
        {
            if (spline.GetTangentMode(i) != TangentMode.Linear)
                return false;
        }

        return true;
    }

    private static List<Vector3> BuildLinearSplinePoints(Spline spline)
    {
        var points = new List<Vector3>();
        if (spline == null || spline.Count == 0)
            return points;

        for (var i = 0; i < spline.Count; i++)
        {
            var knot = spline[i];
            points.Add((Vector3)knot.Position);
        }

        if (spline.Closed && points.Count > 1)
            points.Add(points[0]);

        return points;
    }

    private static float EstimateCurvatureDegPerMeter(Spline spline, float centerT, float stepDistance)
    {
        if (spline == null)
            return 0f;

        SplineUtility.Evaluate(spline, centerT, out var _, out var centerTangent, out var _);
        var center = NormalizeOrFallback(centerTangent, new float3(0f, 0f, 1f));

        spline.GetPointAtLinearDistance(centerT, stepDistance, out var forwardT);
        spline.GetPointAtLinearDistance(centerT, -stepDistance, out var backwardT);

        SplineUtility.Evaluate(spline, forwardT, out var _, out var forwardTangent, out var _);
        SplineUtility.Evaluate(spline, backwardT, out var _, out var backwardTangent, out var _);

        var fwd = NormalizeOrFallback(forwardTangent, center);
        var back = NormalizeOrFallback(backwardTangent, center);

        var angleForward = math.degrees(math.acos(math.clamp(math.dot(center, fwd), -1f, 1f)));
        var angleBackward = math.degrees(math.acos(math.clamp(math.dot(center, back), -1f, 1f)));

        var distance = Mathf.Max(0.001f, stepDistance);
        return (angleForward + angleBackward) / (distance * 2f);
    }

    private static float3 NormalizeOrFallback(float3 value, float3 fallback)
    {
        if (math.lengthsq(value) <= 0.0000001f)
            return math.normalize(fallback);

        return math.normalize(value);
    }

    private static bool TryBuildTransformFromAnchors(
        Vector3 localStartAnchor,
        Vector3 localEndAnchor,
        Vector3 worldStartPoint,
        Vector3 worldEndPoint,
        Vector3 preferredUp,
        out Vector3 worldPosition,
        out Quaternion worldRotation)
    {
        worldPosition = Vector3.zero;
        worldRotation = Quaternion.identity;

        var localDir = localEndAnchor - localStartAnchor;
        var worldDir = worldEndPoint - worldStartPoint;
        if (localDir.sqrMagnitude <= 0.000001f || worldDir.sqrMagnitude <= 0.000001f)
            return false;

        var worldDirNormalized = worldDir.normalized;
        var initialRotation = Quaternion.FromToRotation(localDir.normalized, worldDirNormalized);
        var rotatedUp = initialRotation * Vector3.up;

        var desiredUp = preferredUp.sqrMagnitude > 0.000001f ? preferredUp.normalized : Vector3.up;
        var rotatedUpProjected = Vector3.ProjectOnPlane(rotatedUp, worldDirNormalized);
        var desiredUpProjected = Vector3.ProjectOnPlane(desiredUp, worldDirNormalized);

        if (rotatedUpProjected.sqrMagnitude > 0.000001f && desiredUpProjected.sqrMagnitude > 0.000001f)
        {
            var twistAngle = Vector3.SignedAngle(rotatedUpProjected, desiredUpProjected, worldDirNormalized);
            worldRotation = Quaternion.AngleAxis(twistAngle, worldDirNormalized) * initialRotation;
        }
        else
        {
            worldRotation = initialRotation;
        }

        worldPosition = worldStartPoint - worldRotation * localStartAnchor;
        return true;
    }

    private static void DestroyImmediateSafe(GameObject gameObject)
    {
        if (gameObject == null)
            return;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            DestroyImmediate(gameObject);
            return;
        }
#endif
        Destroy(gameObject);
    }

    private static GameObject InstantiatePrefabInstance(GameObject prefab, Transform parent)
    {
        if (prefab == null)
            return null;

#if UNITY_EDITOR
        if (!Application.isPlaying)
            return PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
#endif

        return Instantiate(prefab, parent);
    }

    private void RequestAutoRebuild()
    {
        if (!_autoRebuildOnSplineChange || ShouldSkipAnyGenerationLogic())
            return;

        _autoRebuildRequested = true;
    }

    private void ProcessAutoRebuild()
    {
#if UNITY_EDITOR
        if (!_autoRebuildRequested)
            return;

        var now = EditorApplication.timeSinceStartup;
        if (now < _nextAllowedAutoRebuildTime)
            return;

        _autoRebuildRequested = false;
        _nextAllowedAutoRebuildTime = now + Mathf.Max(0.01f, _autoRebuildIntervalSeconds);
        Rebuild();
#endif
    }

    private bool ShouldSkipAnyGenerationLogic()
    {
        if (Application.isPlaying)
            return true;

        return !Application.isEditor;
    }

    private void EnsureReferences()
    {
        if (_splineContainer == null)
            _splineContainer = GetComponent<SplineContainer>();
    }

    private void RegisterSplineCallbacks()
    {
        SplineContainer.SplineAdded += OnSplineContainerAdded;
        SplineContainer.SplineRemoved += OnSplineContainerRemoved;
        SplineContainer.SplineReordered += OnSplineContainerReordered;
        Spline.Changed += OnSplineChanged;
    }

    private void UnregisterSplineCallbacks()
    {
        SplineContainer.SplineAdded -= OnSplineContainerAdded;
        SplineContainer.SplineRemoved -= OnSplineContainerRemoved;
        SplineContainer.SplineReordered -= OnSplineContainerReordered;
        Spline.Changed -= OnSplineChanged;
    }

    private void OnSplineContainerAdded(SplineContainer container, int index)
    {
        if (container != _splineContainer)
            return;

        RequestAutoRebuild();
    }

    private void OnSplineContainerRemoved(SplineContainer container, int index)
    {
        if (container != _splineContainer)
            return;

        RequestAutoRebuild();
    }

    private void OnSplineContainerReordered(SplineContainer container, int previousIndex, int newIndex)
    {
        if (container != _splineContainer)
            return;

        RequestAutoRebuild();
    }

    private void OnSplineChanged(Spline spline, int knotIndex, SplineModification modification)
    {
        if (_splineContainer == null)
            return;

        var splines = _splineContainer.Splines;
        if (splines == null)
            return;

        for (var i = 0; i < splines.Count; i++)
        {
            if (splines[i] != spline)
                continue;

            RequestAutoRebuild();
            break;
        }
    }

    private void EnsureGeneratedRoot()
    {
        if (_generatedRoot != null)
            return;

        var existing = transform.Find("FenceSplineGenerated");
        if (existing != null)
        {
            _generatedRoot = existing;
            return;
        }

        var go = new GameObject("FenceSplineGenerated");
        go.transform.SetParent(transform, false);
        _generatedRoot = go.transform;
    }

    private Transform EnsureSplineRoot(int splineIndex)
    {
        if (_generatedRoot == null)
            return null;

        var rootName = $"Spline_{splineIndex:D2}";
        var existing = _generatedRoot.Find(rootName);
        if (existing != null)
            return existing;

        var go = new GameObject(rootName);
        go.transform.SetParent(_generatedRoot, false);
        return go.transform;
    }

    private static bool ShouldDestroyImmediate()
    {
#if UNITY_EDITOR
        return !Application.isPlaying;
#else
        return false;
#endif
    }
}
