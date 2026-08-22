using UnityEngine;

namespace CORE.Editor.Tools
{
    internal static class PrefabPainterDragRotation
    {
        private const float DirectionEpsilon = 0.0001f;

        public static bool TryCalculateYaw(
            Vector2 startMousePosition,
            Vector2 currentMousePosition,
            Vector3 surfacePoint,
            Vector3 surfaceNormal,
            Ray pointerRay,
            float minimumDragPixels,
            out float yaw,
            out Vector3 directionPoint)
        {
            yaw = 0f;
            directionPoint = surfacePoint;

            float minimumDistance = Mathf.Max(0f, minimumDragPixels);
            if ((currentMousePosition - startMousePosition).sqrMagnitude < minimumDistance * minimumDistance)
            {
                return false;
            }

            Vector3 up = surfaceNormal.sqrMagnitude > DirectionEpsilon
                ? surfaceNormal.normalized
                : Vector3.up;
            Plane surfacePlane = new Plane(up, surfacePoint);
            if (!surfacePlane.Raycast(pointerRay, out float rayDistance))
            {
                return false;
            }

            directionPoint = pointerRay.GetPoint(rayDistance);
            Vector3 direction = Vector3.ProjectOnPlane(directionPoint - surfacePoint, up);
            if (direction.sqrMagnitude <= DirectionEpsilon)
            {
                directionPoint = surfacePoint;
                return false;
            }

            direction.Normalize();
            Vector3 referenceForward = Vector3.ProjectOnPlane(Vector3.forward, up).normalized;
            if (referenceForward.sqrMagnitude <= DirectionEpsilon)
            {
                referenceForward = Vector3.ProjectOnPlane(Vector3.right, up).normalized;
            }

            yaw = Vector3.SignedAngle(referenceForward, direction, up);
            directionPoint = surfacePoint + direction * Vector3.Distance(surfacePoint, directionPoint);
            return true;
        }

        public static float ResolveFloorYaw(float randomYaw, float? directionalYaw, bool hasNearbyWall)
        {
            return !hasNearbyWall && directionalYaw.HasValue ? directionalYaw.Value : randomYaw;
        }
    }
}
