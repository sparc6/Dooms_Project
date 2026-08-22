using CORE.Editor.Tools;
using NUnit.Framework;
using UnityEngine;

namespace TheTower.EditorTools.PrefabPainter.Tests
{
    public sealed class PrefabPainterDragRotationTests
    {
        private const float DragThreshold = 5f;

        [Test]
        public void TryCalculateYaw_ForwardDrag_ReturnsZeroYaw()
        {
            bool succeeded = TryCalculateOnHorizontalPlane(
                new Vector3(0f, 10f, 2f),
                out float yaw,
                out _);

            Assert.That(succeeded, Is.True);
            Assert.That(Mathf.DeltaAngle(0f, yaw), Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void TryCalculateYaw_RightDrag_ReturnsPositiveQuarterTurn()
        {
            bool succeeded = TryCalculateOnHorizontalPlane(
                new Vector3(2f, 10f, 0f),
                out float yaw,
                out _);

            Assert.That(succeeded, Is.True);
            Assert.That(Mathf.DeltaAngle(90f, yaw), Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void TryCalculateYaw_Slope_ProducesDirectionTangentToSurface()
        {
            Vector3 normal = new Vector3(0f, 1f, -1f).normalized;
            Vector3 expectedDirection = Vector3.right;
            Ray ray = new Ray(expectedDirection + normal * 10f, -normal);

            bool succeeded = PrefabPainterDragRotation.TryCalculateYaw(
                Vector2.zero,
                new Vector2(10f, 0f),
                Vector3.zero,
                normal,
                ray,
                DragThreshold,
                out float yaw,
                out Vector3 directionPoint);

            Vector3 referenceForward = Vector3.ProjectOnPlane(Vector3.forward, normal).normalized;
            Vector3 resolvedDirection = Quaternion.AngleAxis(yaw, normal) * referenceForward;

            Assert.That(succeeded, Is.True);
            Assert.That(Vector3.Dot(directionPoint.normalized, normal), Is.EqualTo(0f).Within(0.001f));
            Assert.That(Vector3.Angle(expectedDirection, resolvedDirection), Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void TryCalculateYaw_ShortDrag_ReturnsFallbackSignal()
        {
            bool succeeded = PrefabPainterDragRotation.TryCalculateYaw(
                Vector2.zero,
                new Vector2(4.99f, 0f),
                Vector3.zero,
                Vector3.up,
                new Ray(new Vector3(2f, 10f, 0f), Vector3.down),
                DragThreshold,
                out _,
                out _);

            Assert.That(succeeded, Is.False);
        }

        [Test]
        public void TryCalculateYaw_DegenerateWorldDirection_ReturnsFallbackSignal()
        {
            bool succeeded = PrefabPainterDragRotation.TryCalculateYaw(
                Vector2.zero,
                new Vector2(10f, 0f),
                Vector3.zero,
                Vector3.up,
                new Ray(new Vector3(0f, 10f, 0f), Vector3.down),
                DragThreshold,
                out _,
                out _);

            Assert.That(succeeded, Is.False);
        }

        [Test]
        public void ResolveFloorYaw_NearbyWall_PreservesRandomYaw()
        {
            float yaw = PrefabPainterDragRotation.ResolveFloorYaw(37f, 90f, true);

            Assert.That(yaw, Is.EqualTo(37f));
        }

        [Test]
        public void ResolveFloorYaw_FreeFloor_UsesDirectionalYaw()
        {
            float yaw = PrefabPainterDragRotation.ResolveFloorYaw(37f, 90f, false);

            Assert.That(yaw, Is.EqualTo(90f));
        }

        private static bool TryCalculateOnHorizontalPlane(
            Vector3 rayOrigin,
            out float yaw,
            out Vector3 directionPoint)
        {
            return PrefabPainterDragRotation.TryCalculateYaw(
                Vector2.zero,
                new Vector2(10f, 0f),
                Vector3.zero,
                Vector3.up,
                new Ray(rayOrigin, Vector3.down),
                DragThreshold,
                out yaw,
                out directionPoint);
        }
    }
}
