using NUnit.Framework;
using UnityEngine;

namespace TheTower.EditorTools.MeshMaskPainter.Tests
{
    public sealed class MeshLayerMaskRaycasterTests
    {
        [Test]
        public void RaycasterOnlyAcceptsSelectedSubmeshWithoutSceneCollider()
        {
            GameObject gameObject = new GameObject("Layer Mask Raycast Test");
            Mesh mesh = CreateTwoSubmeshMesh();
            var firstMaterial = new Material(Shader.Find("HDRP/LayeredLit"));
            var secondMaterial = new Material(Shader.Find("HDRP/LayeredLit"));
            try
            {
                MeshFilter filter = gameObject.AddComponent<MeshFilter>();
                MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
                filter.sharedMesh = mesh;
                renderer.sharedMaterials = new[] { firstMaterial, secondMaterial };
                var target = new MeshLayerMaskTarget(gameObject, filter, renderer, mesh, secondMaterial, 1);
                var raycaster = new MeshLayerMaskRaycaster();
                raycaster.Build(target);

                bool firstHit = raycaster.Raycast(new Ray(new Vector3(-1f, 0f, 1f), Vector3.back), out _);
                bool secondHit = raycaster.Raycast(new Ray(new Vector3(1f, 0f, 1f), Vector3.back), out _);
                raycaster.Dispose();

                Assert.That(gameObject.GetComponent<MeshCollider>(), Is.Null);
                Assert.That(firstHit, Is.False);
                Assert.That(secondHit, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(firstMaterial);
                Object.DestroyImmediate(secondMaterial);
            }
        }

        private static Mesh CreateTwoSubmeshMesh()
        {
            var mesh = new Mesh { subMeshCount = 2 };
            mesh.vertices = new[]
            {
                new Vector3(-1.5f, -0.5f, 0f), new Vector3(-0.5f, -0.5f, 0f), new Vector3(-1f, 0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f), new Vector3(1.5f, -0.5f, 0f), new Vector3(1f, 0.5f, 0f)
            };
            mesh.normals = new[]
            {
                Vector3.forward, Vector3.forward, Vector3.forward,
                Vector3.forward, Vector3.forward, Vector3.forward
            };
            mesh.uv = new[]
            {
                Vector2.zero, Vector2.right, Vector2.up,
                Vector2.zero, Vector2.right, Vector2.up
            };
            mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
            mesh.SetTriangles(new[] { 3, 4, 5 }, 1);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
