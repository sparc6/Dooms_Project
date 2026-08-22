using NUnit.Framework;
using UnityEngine;

namespace TheTower.EditorTools.MeshMaskPainter.Tests
{
    public sealed class MeshLayerMaskValidationTests
    {
        private GameObject _gameObject;
        private Mesh _mesh;
        private Material _material;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject("Layer Mask Validation Test");
            MeshFilter filter = _gameObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = _gameObject.AddComponent<MeshRenderer>();
            _mesh = CreateQuadMesh();
            filter.sharedMesh = _mesh;
            _material = new Material(Shader.Find("HDRP/LayeredLit"));
            _material.SetFloat(MeshLayerMaskUtility.LayerCountProperty, 2f);
            _material.SetFloat(MeshLayerMaskUtility.UvBlendMaskProperty, 0f);
            _material.SetTextureScale(MeshLayerMaskUtility.LayerMaskProperty, Vector2.one);
            _material.SetTextureOffset(MeshLayerMaskUtility.LayerMaskProperty, Vector2.zero);
            renderer.sharedMaterial = _material;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_gameObject);
            Object.DestroyImmediate(_mesh);
            Object.DestroyImmediate(_material);
        }

        [Test]
        public void CompatibleTargetPassesValidation()
        {
            bool valid = MeshLayerMaskValidation.TryCreateTarget(_gameObject, 0, out MeshLayerMaskTarget target, out string message);

            Assert.That(valid, Is.True, message);
            Assert.That(target.Mesh, Is.SameAs(_mesh));
            Assert.That(target.Material, Is.SameAs(_material));
        }

        [Test]
        public void NonUv0BlendMappingIsRejected()
        {
            _material.SetFloat(MeshLayerMaskUtility.UvBlendMaskProperty, 4f);

            bool valid = MeshLayerMaskValidation.TryCreateTarget(_gameObject, 0, out _, out string message);

            Assert.That(valid, Is.False);
            Assert.That(message, Does.Contain("UV0"));
        }

        [Test]
        public void NonIdentityMaskTransformIsRejected()
        {
            _material.SetTextureScale(MeshLayerMaskUtility.LayerMaskProperty, new Vector2(2f, 1f));

            bool valid = MeshLayerMaskValidation.TryCreateTarget(_gameObject, 0, out _, out string message);

            Assert.That(valid, Is.False);
            Assert.That(message, Does.Contain("Tiling"));
        }

        [Test]
        public void MeshWithoutUv0IsRejected()
        {
            _mesh.uv = null;

            bool valid = MeshLayerMaskValidation.TryCreateTarget(_gameObject, 0, out _, out string message);

            Assert.That(valid, Is.False);
            Assert.That(message, Does.Contain("UV0"));
        }

        [Test]
        public void GeometryTargetAllowsSculptWithoutUvOrLayeredLitMaterial()
        {
            _mesh.uv = null;
            Object.DestroyImmediate(_material);
            _material = new Material(Shader.Find("HDRP/Lit"));
            _gameObject.GetComponent<MeshRenderer>().sharedMaterial = _material;

            bool valid = MeshLayerMaskValidation.TryCreateGeometryTarget(
                _gameObject,
                0,
                out MeshLayerMaskTarget target,
                out string message);

            Assert.That(valid, Is.True, message);
            Assert.That(target.Mesh, Is.SameAs(_mesh));
        }

        internal static Mesh CreateQuadMesh()
        {
            var mesh = new Mesh { name = "Layer Mask Test Quad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f)
            };
            mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
