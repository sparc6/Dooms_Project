using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CORE.Editor.Tools
{
    internal readonly struct PrefabPainterPlacementFeedback
    {
        public PrefabPainterPlacementFeedback(
            bool hasSurfaceHit,
            bool isValid,
            bool isWallPlacement,
            bool hasNearbyWall,
            Vector3 surfacePoint,
            Vector3 surfaceNormal,
            Vector3 anchorPosition,
            bool hasFloor,
            Vector3 floorPoint,
            float floorHeight,
            string message)
        {
            HasSurfaceHit = hasSurfaceHit;
            IsValid = isValid;
            IsWallPlacement = isWallPlacement;
            HasNearbyWall = hasNearbyWall;
            SurfacePoint = surfacePoint;
            SurfaceNormal = surfaceNormal;
            AnchorPosition = anchorPosition;
            HasFloor = hasFloor;
            FloorPoint = floorPoint;
            FloorHeight = floorHeight;
            Message = message;
        }

        public bool HasSurfaceHit { get; }
        public bool IsValid { get; }
        public bool IsWallPlacement { get; }
        public bool HasNearbyWall { get; }
        public Vector3 SurfacePoint { get; }
        public Vector3 SurfaceNormal { get; }
        public Vector3 AnchorPosition { get; }
        public bool HasFloor { get; }
        public Vector3 FloorPoint { get; }
        public float FloorHeight { get; }
        public string Message { get; }
    }

    internal sealed class PrefabPainterPlacementController : IDisposable
    {
        private const int WallProbeRayCount = 32;

        private readonly List<ColliderState> _colliderStates = new List<ColliderState>();

        private GameObject _previewInstance;
        private GameObject _previewPrefab;
        private Transform _previewRoot;
        private Bounds _localBounds;
        private HideFlags _originalHideFlags;

        public bool HasPreview => _previewInstance != null;

        public static PrefabPainterPlacementFeedback EvaluateSurface(
            PrefabPainterEntry entry,
            PrefabPainterConfig config,
            Transform root,
            Vector2 mousePosition)
        {
            if (entry == null || config == null || root == null)
            {
                return CreateInvalidFeedback("Не назначены prefab, конфиг или Root.");
            }

            if (!IsEntryValid(entry, out string invalidReason))
            {
                return CreateInvalidFeedback(invalidReason);
            }

            if (!TryGetSurfaceHit(mousePosition, config, root, out RaycastHit surfaceHit))
            {
                return CreateInvalidFeedback("Под курсором нет поверхности из выбранной маски.");
            }

            bool isAllowed = entry.WallOnly
                ? IsWallSurface(surfaceHit.normal, config)
                : IsFloorSurface(surfaceHit.normal, config);
            string message = isAllowed
                ? string.Empty
                : entry.WallOnly
                    ? "Выбранный prefab можно размещать только на стенах."
                    : "Выбранная поверхность слишком крутая для напольного prefab-а.";

            return new PrefabPainterPlacementFeedback(
                true,
                isAllowed,
                entry.WallOnly,
                false,
                surfaceHit.point,
                surfaceHit.normal,
                surfaceHit.point,
                false,
                default,
                0f,
                message);
        }

        public static bool IsEntryValid(PrefabPainterEntry entry, out string reason)
        {
            if (entry == null || entry.Prefab == null)
            {
                reason = "Не назначен prefab-ассет.";
                return false;
            }

            GameObject prefab = entry.Prefab;
            if (!EditorUtility.IsPersistent(prefab) || !PrefabUtility.IsPartOfPrefabAsset(prefab))
            {
                reason = "Нужен prefab-ассет из Project, а не объект сцены.";
                return false;
            }

            if (!prefab.activeSelf)
            {
                reason = "Корневой объект prefab-ассета выключен.";
                return false;
            }

            bool hasRenderer = prefab.GetComponentInChildren<Renderer>(true) != null;
            bool hasCollider = prefab.GetComponentInChildren<Collider>(true) != null;
            if (!hasRenderer && !hasCollider)
            {
                reason = "Для расчёта прилипания нужен Renderer или Collider.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public bool UpdatePreview(
            PrefabPainterEntry entry,
            PrefabPainterConfig config,
            Transform root,
            Vector2 mousePosition,
            float randomYaw,
            float? directionalYaw,
            bool disableNearbyWallSnap,
            out PrefabPainterPlacementFeedback feedback)
        {
            if (entry == null || config == null || root == null)
            {
                CancelPreview();
                feedback = CreateInvalidFeedback("Не назначены prefab, конфиг или Root.");
                return false;
            }

            if (!IsEntryValid(entry, out string invalidReason))
            {
                CancelPreview();
                feedback = CreateInvalidFeedback(invalidReason);
                return false;
            }

            if (!TryGetSurfaceHit(mousePosition, config, root, out RaycastHit surfaceHit))
            {
                CancelPreview();
                feedback = CreateInvalidFeedback("Под курсором нет поверхности из выбранной маски.");
                return false;
            }

            bool surfaceAllowed = entry.WallOnly
                ? IsWallSurface(surfaceHit.normal, config)
                : IsFloorSurface(surfaceHit.normal, config);

            if (!surfaceAllowed)
            {
                CancelPreview();
                string message = entry.WallOnly
                    ? "Выбранный prefab можно размещать только на стенах."
                    : "Выбранная поверхность слишком крутая для напольного prefab-а.";

                feedback = new PrefabPainterPlacementFeedback(
                    true,
                    false,
                    entry.WallOnly,
                    false,
                    surfaceHit.point,
                    surfaceHit.normal,
                    surfaceHit.point,
                    false,
                    default,
                    0f,
                    message);
                return false;
            }

            if (!EnsurePreview(entry.Prefab, root, out invalidReason))
            {
                feedback = new PrefabPainterPlacementFeedback(
                    true,
                    false,
                    entry.WallOnly,
                    false,
                    surfaceHit.point,
                    surfaceHit.normal,
                    surfaceHit.point,
                    false,
                    default,
                    0f,
                    invalidReason);
                return false;
            }

            bool placementSucceeded;
            bool hasNearbyWall = false;
            if (entry.WallOnly)
            {
                placementSucceeded = PlaceOnWall(entry, config, surfaceHit);
            }
            else
            {
                placementSucceeded = PlaceOnFloor(
                    entry,
                    config,
                    root,
                    surfaceHit,
                    randomYaw,
                    directionalYaw,
                    disableNearbyWallSnap,
                    out hasNearbyWall);
            }

            if (!placementSucceeded)
            {
                CancelPreview();
                feedback = new PrefabPainterPlacementFeedback(
                    true,
                    false,
                    entry.WallOnly,
                    hasNearbyWall,
                    surfaceHit.point,
                    surfaceHit.normal,
                    surfaceHit.point,
                    false,
                    default,
                    0f,
                    "После прилипания под объектом не найден допустимый пол.");
                return false;
            }

            _previewInstance.transform.position += _previewInstance.transform.TransformVector(entry.LocalOffset);

            bool hasFloor = false;
            Vector3 floorPoint = default;
            float floorHeight = 0f;
            if (entry.WallOnly)
            {
                hasFloor = TryMeasureHeightFromFloor(
                    _previewInstance.transform.position,
                    surfaceHit.normal,
                    config,
                    root,
                    out floorPoint,
                    out floorHeight);
            }

            feedback = new PrefabPainterPlacementFeedback(
                true,
                true,
                entry.WallOnly,
                hasNearbyWall,
                surfaceHit.point,
                surfaceHit.normal,
                _previewInstance.transform.position,
                hasFloor,
                floorPoint,
                floorHeight,
                hasFloor || !entry.WallOnly ? string.Empty : "Пол под объектом не найден.");
            return true;
        }

        public GameObject CommitPreview()
        {
            if (_previewInstance == null)
            {
                return null;
            }

            RestorePreviewColliders();

            GameObject committedInstance = _previewInstance;
            committedInstance.hideFlags = _originalHideFlags;
            committedInstance.name = _previewPrefab != null ? _previewPrefab.name : committedInstance.name;

            Undo.RegisterCreatedObjectUndo(committedInstance, "Разместить prefab");
            EditorUtility.SetDirty(committedInstance);

            if (_previewRoot != null && _previewRoot.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(_previewRoot.gameObject.scene);
            }

            ClearPreviewReferences();
            return committedInstance;
        }

        public void CancelPreview()
        {
            if (_previewInstance != null)
            {
                UnityEngine.Object.DestroyImmediate(_previewInstance);
            }

            ClearPreviewReferences();
        }

        public void DrawPreviewBounds(Color color)
        {
            if (_previewInstance == null)
            {
                return;
            }

            Matrix4x4 previousMatrix = Handles.matrix;
            Color previousColor = Handles.color;

            Handles.matrix = _previewInstance.transform.localToWorldMatrix;
            Handles.color = color;
            Handles.DrawWireCube(_localBounds.center, _localBounds.size);

            Handles.matrix = previousMatrix;
            Handles.color = previousColor;
        }

        public void Dispose()
        {
            CancelPreview();
        }

        private bool EnsurePreview(GameObject prefab, Transform root, out string reason)
        {
            if (_previewInstance != null && _previewPrefab == prefab && _previewRoot == root)
            {
                reason = string.Empty;
                return true;
            }

            CancelPreview();

            _previewInstance = PrefabUtility.InstantiatePrefab(prefab, root) as GameObject;
            if (_previewInstance == null)
            {
                reason = $"Не удалось создать preview для '{prefab.name}'.";
                return false;
            }

            _previewPrefab = prefab;
            _previewRoot = root;
            _originalHideFlags = _previewInstance.hideFlags;
            _previewInstance.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor;
            _previewInstance.name = $"{prefab.name} (Preview)";

            if (!TryCalculateLocalBounds(_previewInstance.transform, out _localBounds))
            {
                reason = $"У '{prefab.name}' не удалось рассчитать Renderer/Collider bounds.";
                CancelPreview();
                return false;
            }

            DisablePreviewColliders();
            reason = string.Empty;
            return true;
        }

        private bool PlaceOnFloor(
            PrefabPainterEntry entry,
            PrefabPainterConfig config,
            Transform root,
            RaycastHit floorHit,
            float randomYaw,
            float? directionalYaw,
            bool disableNearbyWallSnap,
            out bool hasNearbyWall)
        {
            RaycastHit wallHit = default;
            hasNearbyWall = !disableNearbyWallSnap &&
                            TryFindNearbyWall(floorHit.point, config, root, out wallHit);

            Vector3 wallDirection = hasNearbyWall
                ? -Vector3.ProjectOnPlane(wallHit.normal, Vector3.up).normalized
                : Vector3.zero;

            float resolvedFloorYaw = PrefabPainterDragRotation.ResolveFloorYaw(
                randomYaw,
                directionalYaw,
                hasNearbyWall);
            Quaternion rotation = hasNearbyWall
                ? BuildAttachmentRotation(entry.AttachmentSide, wallDirection, floorHit.normal)
                : BuildRandomFloorRotation(floorHit.normal, resolvedFloorYaw);
            rotation *= Quaternion.Euler(entry.StartRotationEuler);

            Transform previewTransform = _previewInstance.transform;
            previewTransform.SetPositionAndRotation(floorHit.point, rotation);
            AlignBottomToFloor(floorHit.point, floorHit.normal, config.SurfaceOffset);

            if (!hasNearbyWall)
            {
                return true;
            }

            Vector3 horizontalWallNormal = Vector3.ProjectOnPlane(wallHit.normal, Vector3.up).normalized;
            SnapAttachmentToWall(wallHit.point, horizontalWallNormal, config.SurfaceOffset);

            if (!TryFindFloorBelow(
                    previewTransform.position,
                    floorHit.point.y,
                    config,
                    root,
                    out RaycastHit snappedFloorHit))
            {
                return false;
            }

            wallDirection = Vector3.ProjectOnPlane(-horizontalWallNormal, snappedFloorHit.normal).normalized;
            if (wallDirection.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            rotation = BuildAttachmentRotation(entry.AttachmentSide, wallDirection, snappedFloorHit.normal);
            rotation *= Quaternion.Euler(entry.StartRotationEuler);
            previewTransform.rotation = rotation;
            AlignBottomToFloor(snappedFloorHit.point, snappedFloorHit.normal, config.SurfaceOffset);
            SnapAttachmentToWall(wallHit.point, horizontalWallNormal, config.SurfaceOffset);
            return true;
        }

        private bool PlaceOnWall(
            PrefabPainterEntry entry,
            PrefabPainterConfig config,
            RaycastHit wallHit)
        {
            Vector3 wallNormal = wallHit.normal.normalized;
            Vector3 intoWall = -wallNormal;
            Vector3 upright = Vector3.ProjectOnPlane(Vector3.up, intoWall).normalized;
            if (upright.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            Quaternion rotation = BuildAttachmentRotation(entry.AttachmentSide, intoWall, upright);
            rotation *= Quaternion.Euler(entry.StartRotationEuler);
            _previewInstance.transform.SetPositionAndRotation(wallHit.point, rotation);
            SnapAttachmentToWall(wallHit.point, wallNormal, config.SurfaceOffset);
            return true;
        }

        private void AlignBottomToFloor(Vector3 floorPoint, Vector3 floorNormal, float surfaceOffset)
        {
            Transform previewTransform = _previewInstance.transform;
            Vector3 normalizedFloorNormal = floorNormal.normalized;
            Vector3 worldBottom = GetWorldBoundsSupportPoint(normalizedFloorNormal, false);
            Vector3 targetPoint = floorPoint + normalizedFloorNormal * surfaceOffset;
            float correction = Vector3.Dot(targetPoint - worldBottom, normalizedFloorNormal);
            previewTransform.position += normalizedFloorNormal * correction;
        }

        private void SnapAttachmentToWall(
            Vector3 wallPoint,
            Vector3 wallNormal,
            float surfaceOffset)
        {
            Transform previewTransform = _previewInstance.transform;
            Vector3 normalizedWallNormal = wallNormal.normalized;
            Vector3 worldSupport = GetWorldBoundsSupportPoint(normalizedWallNormal, false);
            Vector3 targetPoint = wallPoint + normalizedWallNormal * surfaceOffset;
            float correction = Vector3.Dot(targetPoint - worldSupport, normalizedWallNormal);
            previewTransform.position += normalizedWallNormal * correction;
        }

        private bool TryFindNearbyWall(
            Vector3 floorPoint,
            PrefabPainterConfig config,
            Transform root,
            out RaycastHit closestWallHit)
        {
            closestWallHit = default;
            Vector3 probeOrigin = floorPoint + Vector3.up * config.NearbyWallProbeHeight;
            float closestDistance = float.PositiveInfinity;
            bool found = false;

            for (int i = 0; i < WallProbeRayCount; i++)
            {
                float angle = 360f * i / WallProbeRayCount;
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;
                RaycastHit[] hits = Physics.RaycastAll(
                    probeOrigin,
                    direction,
                    config.NearbyWallDistance,
                    config.SurfaceMask,
                    QueryTriggerInteraction.Ignore);

                for (int hitIndex = 0; hitIndex < hits.Length; hitIndex++)
                {
                    RaycastHit candidateHit = hits[hitIndex];
                    if (candidateHit.collider == null ||
                        !IsWallSurface(candidateHit.normal, config) ||
                        candidateHit.distance >= closestDistance)
                    {
                        continue;
                    }

                    closestDistance = candidateHit.distance;
                    closestWallHit = candidateHit;
                    found = true;
                }
            }

            return found;
        }

        private bool TryMeasureHeightFromFloor(
            Vector3 anchorPosition,
            Vector3 wallNormal,
            PrefabPainterConfig config,
            Transform root,
            out Vector3 floorPoint,
            out float height)
        {
            float outwardOffset = Mathf.Max(0.02f, config.SurfaceOffset + 0.01f);
            Vector3 origin = anchorPosition + wallNormal.normalized * outwardOffset + Vector3.up * 0.01f;
            if (!TryGetClosestFloorHit(
                    new Ray(origin, Vector3.down),
                    config.FloorProbeDistance + 0.01f,
                    config,
                    root,
                    out RaycastHit floorHit))
            {
                floorPoint = default;
                height = 0f;
                return false;
            }

            floorPoint = new Vector3(anchorPosition.x, floorHit.point.y, anchorPosition.z);
            height = Mathf.Max(0f, anchorPosition.y - floorHit.point.y);
            return true;
        }

        private bool TryFindFloorBelow(
            Vector3 position,
            float referenceFloorHeight,
            PrefabPainterConfig config,
            Transform root,
            out RaycastHit floorHit)
        {
            float startPadding = Mathf.Max(1f, config.NearbyWallProbeHeight + 0.5f);
            Vector3 origin = new Vector3(position.x, referenceFloorHeight + startPadding, position.z);
            return TryGetClosestFloorHit(
                new Ray(origin, Vector3.down),
                config.FloorProbeDistance + startPadding,
                config,
                root,
                out floorHit);
        }

        private static bool TryGetSurfaceHit(
            Vector2 mousePosition,
            PrefabPainterConfig config,
            Transform root,
            out RaycastHit closestHit)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
            RaycastHit[] hits = Physics.RaycastAll(
                ray,
                Mathf.Infinity,
                config.SurfaceMask,
                QueryTriggerInteraction.Ignore);

            return TrySelectClosestHit(hits, root, null, out closestHit);
        }

        private static bool TryGetClosestFloorHit(
            Ray ray,
            float distance,
            PrefabPainterConfig config,
            Transform root,
            out RaycastHit floorHit)
        {
            RaycastHit[] hits = Physics.RaycastAll(
                ray,
                distance,
                config.SurfaceMask,
                QueryTriggerInteraction.Ignore);

            return TrySelectClosestHit(hits, root, hit => IsFloorSurface(hit.normal, config), out floorHit);
        }

        private static bool TrySelectClosestHit(
            IReadOnlyList<RaycastHit> hits,
            Transform root,
            Predicate<RaycastHit> filter,
            out RaycastHit closestHit)
        {
            closestHit = default;
            float closestDistance = float.PositiveInfinity;
            bool found = false;

            for (int i = 0; i < hits.Count; i++)
            {
                RaycastHit candidate = hits[i];
                if (candidate.collider == null ||
                    (filter != null && !filter(candidate)) ||
                    candidate.distance >= closestDistance)
                {
                    continue;
                }

                closestDistance = candidate.distance;
                closestHit = candidate;
                found = true;
            }

            return found;
        }

        private static bool IsFloorSurface(Vector3 normal, PrefabPainterConfig config)
        {
            return Vector3.Angle(normal, Vector3.up) <= config.MaxFloorSlopeAngle;
        }

        private static bool IsWallSurface(Vector3 normal, PrefabPainterConfig config)
        {
            Vector3 safeNormal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
            float maximumUpDot = Mathf.Sin(config.WallDeviationAngle * Mathf.Deg2Rad);
            return Mathf.Abs(Vector3.Dot(safeNormal, Vector3.up)) <= maximumUpDot;
        }

        private static Quaternion BuildRandomFloorRotation(Vector3 floorNormal, float yaw)
        {
            Vector3 up = floorNormal.sqrMagnitude > 0.0001f ? floorNormal.normalized : Vector3.up;
            Vector3 forward = Vector3.ProjectOnPlane(Vector3.forward, up).normalized;
            if (forward.sqrMagnitude <= 0.0001f)
            {
                forward = Vector3.ProjectOnPlane(Vector3.right, up).normalized;
            }

            Quaternion baseRotation = Quaternion.LookRotation(forward, up);
            return Quaternion.AngleAxis(yaw, up) * baseRotation;
        }

        private static Quaternion BuildAttachmentRotation(
            PrefabPainterAttachmentSide attachmentSide,
            Vector3 worldAttachmentDirection,
            Vector3 worldUp)
        {
            Vector3 up = worldUp.sqrMagnitude > 0.0001f ? worldUp.normalized : Vector3.up;
            Vector3 attachmentDirection = Vector3.ProjectOnPlane(worldAttachmentDirection, up).normalized;
            if (attachmentDirection.sqrMagnitude <= 0.0001f)
            {
                attachmentDirection = Vector3.ProjectOnPlane(Vector3.forward, up).normalized;
            }

            Vector3 localAttachmentDirection = GetLocalAttachmentDirection(attachmentSide);
            Vector3 localUprightDirection = attachmentSide == PrefabPainterAttachmentSide.Up ||
                                             attachmentSide == PrefabPainterAttachmentSide.Down
                ? Vector3.forward
                : Vector3.up;

            Quaternion attachmentRotation = Quaternion.FromToRotation(
                localAttachmentDirection,
                attachmentDirection);
            Vector3 rotatedUpright = Vector3.ProjectOnPlane(
                attachmentRotation * localUprightDirection,
                attachmentDirection).normalized;

            if (rotatedUpright.sqrMagnitude <= 0.0001f)
            {
                return attachmentRotation;
            }

            float uprightCorrection = Vector3.SignedAngle(rotatedUpright, up, attachmentDirection);
            return Quaternion.AngleAxis(uprightCorrection, attachmentDirection) * attachmentRotation;
        }

        private Vector3 GetWorldBoundsSupportPoint(Vector3 direction, bool maximum)
        {
            Transform previewTransform = _previewInstance.transform;
            Vector3 min = _localBounds.min;
            Vector3 max = _localBounds.max;
            Vector3 supportPoint = previewTransform.position;
            float supportProjection = maximum ? float.NegativeInfinity : float.PositiveInfinity;

            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int z = 0; z < 2; z++)
                    {
                        Vector3 localCorner = new Vector3(
                            x == 0 ? min.x : max.x,
                            y == 0 ? min.y : max.y,
                            z == 0 ? min.z : max.z);
                        Vector3 worldCorner = previewTransform.TransformPoint(localCorner);
                        float projection = Vector3.Dot(worldCorner, direction);
                        bool isBetterSupport = maximum
                            ? projection > supportProjection
                            : projection < supportProjection;
                        if (!isBetterSupport)
                        {
                            continue;
                        }

                        supportProjection = projection;
                        supportPoint = worldCorner;
                    }
                }
            }

            return supportPoint;
        }

        private static Vector3 GetLocalAttachmentDirection(PrefabPainterAttachmentSide attachmentSide)
        {
            switch (attachmentSide)
            {
                case PrefabPainterAttachmentSide.Forward:
                    return Vector3.forward;

                case PrefabPainterAttachmentSide.Left:
                    return Vector3.left;

                case PrefabPainterAttachmentSide.Right:
                    return Vector3.right;

                case PrefabPainterAttachmentSide.Up:
                    return Vector3.up;

                case PrefabPainterAttachmentSide.Down:
                    return Vector3.down;

                default:
                    return Vector3.back;
            }
        }

        private static bool TryCalculateLocalBounds(Transform root, out Bounds bounds)
        {
            bool hasBounds = false;
            bounds = default;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                Matrix4x4 toRoot = root.worldToLocalMatrix * renderer.localToWorldMatrix;
                EncapsulateTransformedBounds(renderer.localBounds, toRoot, ref bounds, ref hasBounds);
            }

            if (hasBounds)
            {
                return true;
            }

            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null)
                {
                    continue;
                }

                EncapsulateWorldBounds(collider.bounds, root.worldToLocalMatrix, ref bounds, ref hasBounds);
            }

            return hasBounds;
        }

        private static void EncapsulateTransformedBounds(
            Bounds sourceBounds,
            Matrix4x4 matrix,
            ref Bounds targetBounds,
            ref bool hasBounds)
        {
            Vector3 min = sourceBounds.min;
            Vector3 max = sourceBounds.max;
            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int z = 0; z < 2; z++)
                    {
                        Vector3 corner = new Vector3(
                            x == 0 ? min.x : max.x,
                            y == 0 ? min.y : max.y,
                            z == 0 ? min.z : max.z);
                        EncapsulatePoint(matrix.MultiplyPoint3x4(corner), ref targetBounds, ref hasBounds);
                    }
                }
            }
        }

        private static void EncapsulateWorldBounds(
            Bounds worldBounds,
            Matrix4x4 worldToLocal,
            ref Bounds targetBounds,
            ref bool hasBounds)
        {
            EncapsulateTransformedBounds(worldBounds, worldToLocal, ref targetBounds, ref hasBounds);
        }

        private static void EncapsulatePoint(Vector3 point, ref Bounds bounds, ref bool hasBounds)
        {
            if (!hasBounds)
            {
                bounds = new Bounds(point, Vector3.zero);
                hasBounds = true;
                return;
            }

            bounds.Encapsulate(point);
        }

        private void DisablePreviewColliders()
        {
            _colliderStates.Clear();
            Collider[] colliders = _previewInstance.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null)
                {
                    continue;
                }

                _colliderStates.Add(new ColliderState(collider, collider.enabled));
                collider.enabled = false;
            }
        }

        private void RestorePreviewColliders()
        {
            for (int i = 0; i < _colliderStates.Count; i++)
            {
                ColliderState state = _colliderStates[i];
                if (state.Collider != null)
                {
                    state.Collider.enabled = state.Enabled;
                }
            }

            _colliderStates.Clear();
        }

        private void ClearPreviewReferences()
        {
            _previewInstance = null;
            _previewPrefab = null;
            _previewRoot = null;
            _localBounds = default;
            _colliderStates.Clear();
            _originalHideFlags = HideFlags.None;
        }

        private static PrefabPainterPlacementFeedback CreateInvalidFeedback(string message)
        {
            return new PrefabPainterPlacementFeedback(
                false,
                false,
                false,
                false,
                default,
                Vector3.up,
                default,
                false,
                default,
                0f,
                message);
        }

        private readonly struct ColliderState
        {
            public ColliderState(Collider collider, bool enabled)
            {
                Collider = collider;
                Enabled = enabled;
            }

            public Collider Collider { get; }
            public bool Enabled { get; }
        }
    }
}
