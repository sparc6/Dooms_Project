using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TheTower.EditorTools.MeshMaskPainter.Tests
{
    public sealed class MeshLayerMaskPainterSessionTests
    {
        private const string TemporaryAssetPath = "Assets/Tests/Editor/MeshMaskPainter/__TemporaryLayerMask.png";
        private GameObject _gameObject;
        private Mesh _mesh;
        private Material _material;
        private MeshLayerMaskTarget _target;
        private Shader _paintShader;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject("Layer Mask Session Test");
            MeshFilter filter = _gameObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = _gameObject.AddComponent<MeshRenderer>();
            _mesh = MeshLayerMaskValidationTests.CreateQuadMesh();
            filter.sharedMesh = _mesh;
            _material = new Material(Shader.Find("HDRP/LayeredLit"));
            _material.SetFloat(MeshLayerMaskUtility.LayerCountProperty, 2f);
            _material.SetFloat(MeshLayerMaskUtility.UvBlendMaskProperty, 0f);
            renderer.sharedMaterial = _material;
            Assert.That(MeshLayerMaskValidation.TryCreateTarget(_gameObject, 0, out _target, out string message), Is.True, message);
            _paintShader = Shader.Find("Hidden/TheTower/MeshLayerMaskPainter");
            Assert.That(_paintShader, Is.Not.Null);
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(TemporaryAssetPath);
            Object.DestroyImmediate(_gameObject);
            Object.DestroyImmediate(_mesh);
            Object.DestroyImmediate(_material);
        }

        [Test]
        public void GpuStrokeChangesCenterButNotCorner()
        {
            using var session = new MeshLayerMaskPainterSession(_target, _paintShader);
            session.InitializeNew(32);

            session.BeginStroke();
            session.Stamp(Vector3.zero, Vector3.forward, new Vector2(0.5f, 0.5f), 1, 0.2f, 1f, 1f, 85f, 1f);
            session.EndStroke();

            Texture2D result = ReadBack(session.CurrentTexture);
            Color center = result.GetPixel(16, 16);
            Color corner = result.GetPixel(2, 2);
            Object.DestroyImmediate(result);

            Assert.That(center.r, Is.GreaterThan(0.9f));
            Assert.That(center.a, Is.LessThan(0.1f));
            Assert.That(corner.a, Is.GreaterThan(0.9f));
        }

        [Test]
        public void GpuStrokeWritesToExpectedNonSymmetricUvOnDirect3D()
        {
            using var session = new MeshLayerMaskPainterSession(_target, _paintShader);
            session.InitializeNew(64);

            session.BeginStroke();
            session.Stamp(
                new Vector3(-0.25f, 0.25f, 0f),
                Vector3.forward,
                new Vector2(0.25f, 0.75f),
                1,
                0.08f,
                1f,
                1f,
                85f,
                1f);
            session.EndStroke();

            Texture2D result = ReadBack(session.CurrentTexture);
            Color expected = result.GetPixel(16, 48);
            Color verticallyMirrored = result.GetPixel(16, 16);
            Object.DestroyImmediate(result);

            Assert.That(expected.r, Is.GreaterThan(0.9f));
            Assert.That(verticallyMirrored.a, Is.GreaterThan(0.9f));
        }

        [Test]
        public void OppositeNormalDoesNotPaint()
        {
            using var session = new MeshLayerMaskPainterSession(_target, _paintShader);
            session.InitializeNew(32);

            session.BeginStroke();
            session.Stamp(Vector3.zero, Vector3.back, new Vector2(0.5f, 0.5f), 1, 0.4f, 1f, 1f, 85f, 1f);
            session.EndStroke();

            Texture2D result = ReadBack(session.CurrentTexture);
            Color center = result.GetPixel(16, 16);
            Object.DestroyImmediate(result);

            Assert.That(center.a, Is.GreaterThan(0.9f));
            Assert.That(center.r, Is.LessThan(0.1f));
        }

        [Test]
        public void RelaxedAnglePaintsSurfaceBeyondLegacySixtyDegreeLimit()
        {
            using var session = new MeshLayerMaskPainterSession(_target, _paintShader);
            session.InitializeNew(32);
            Vector3 steepBrushNormal = Quaternion.AngleAxis(75f, Vector3.up) * Vector3.forward;

            session.BeginStroke();
            session.Stamp(Vector3.zero, steepBrushNormal, new Vector2(0.5f, 0.5f), 1, 0.4f, 1f, 1f, 85f, 1f);
            session.EndStroke();

            Assert.That(ReadCenter(session.CurrentTexture).r, Is.GreaterThan(0.9f));
        }

        [Test]
        public void ConfigurableDepthReachesSurfaceBeyondLegacyQuarterRadiusLimit()
        {
            using var session = new MeshLayerMaskPainterSession(_target, _paintShader);
            session.InitializeNew(32);

            session.BeginStroke();
            session.Stamp(new Vector3(0f, 0f, 0.6f), Vector3.forward, new Vector2(0.5f, 0.5f), 1, 1f, 1f, 1f, 85f, 1f);
            session.EndStroke();

            Assert.That(ReadCenter(session.CurrentTexture).r, Is.GreaterThan(0.9f));
        }

        [Test]
        public void PaintsUvTileOutsideZeroToOneRange()
        {
            _mesh.uv = new[]
            {
                new Vector2(5f, 7f),
                new Vector2(6f, 7f),
                new Vector2(6f, 8f),
                new Vector2(5f, 8f)
            };
            using var session = new MeshLayerMaskPainterSession(_target, _paintShader);
            session.InitializeNew(32);

            session.BeginStroke();
            session.Stamp(Vector3.zero, Vector3.forward, new Vector2(5.5f, 7.5f), 1, 0.3f, 1f, 1f, 85f, 1f);
            session.EndStroke();

            Assert.That(ReadCenter(session.CurrentTexture).r, Is.GreaterThan(0.9f));
        }

        [Test]
        public void NonBrushTriangleWithOverlappingUvDoesNotEraseStroke()
        {
            ReplaceWithOverlappingQuadMesh(_mesh);
            using var session = new MeshLayerMaskPainterSession(_target, _paintShader);
            session.InitializeNew(32);

            session.BeginStroke();
            session.Stamp(Vector3.zero, Vector3.forward, new Vector2(0.5f, 0.5f), 1, 0.3f, 1f, 1f, 85f, 1f);
            session.EndStroke();

            Assert.That(ReadCenter(session.CurrentTexture).r, Is.GreaterThan(0.9f));
        }

        [Test]
        public void UndoRedoRestoresCompletedStrokeStates()
        {
            using var session = new MeshLayerMaskPainterSession(_target, _paintShader);
            session.InitializeNew(32);
            session.BeginStroke();
            session.Stamp(Vector3.zero, Vector3.forward, new Vector2(0.5f, 0.5f), 1, 0.3f, 1f, 1f, 85f, 1f);
            session.EndStroke();

            Assert.That(session.CanUndo, Is.True);
            Assert.That(session.Undo(), Is.True);
            Assert.That(ReadCenter(session.CurrentTexture).a, Is.GreaterThan(0.9f));
            Assert.That(session.Redo(), Is.True);
            Assert.That(ReadCenter(session.CurrentTexture).r, Is.GreaterThan(0.9f));
        }

        [Test]
        public void DisposeRestoresExistingMaterialPropertyBlock()
        {
            var originalTexture = new Texture2D(1, 1);
            var block = new MaterialPropertyBlock();
            block.SetTexture(MeshLayerMaskUtility.LayerMaskProperty, originalTexture);
            _target.Renderer.SetPropertyBlock(block, 0);

            var session = new MeshLayerMaskPainterSession(_target, _paintShader);
            session.InitializeNew(32);
            session.Dispose();

            var restored = new MaterialPropertyBlock();
            _target.Renderer.GetPropertyBlock(restored, 0);
            Assert.That(restored.GetTexture(MeshLayerMaskUtility.LayerMaskProperty), Is.SameAs(originalTexture));
            Object.DestroyImmediate(originalTexture);
        }

        [Test]
        public void SaveCreatesLinearPngAndAssignsItToMaterial()
        {
            using var session = new MeshLayerMaskPainterSession(_target, _paintShader);
            session.InitializeNew(32);

            Texture2D saved = session.SavePngAndAssign(TemporaryAssetPath);
            var importer = AssetImporter.GetAtPath(TemporaryAssetPath) as TextureImporter;

            Assert.That(saved, Is.Not.Null);
            Assert.That(File.Exists(Path.GetFullPath(TemporaryAssetPath)), Is.True);
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.sRGBTexture, Is.False);
            Assert.That(importer.mipmapEnabled, Is.True);
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Repeat));
            Assert.That(_material.GetTexture(MeshLayerMaskUtility.LayerMaskProperty), Is.SameAs(saved));
            Assert.That(session.IsDirty, Is.False);
        }

        [Test]
        public void SavedPngContainsPaintedWorkingPixels()
        {
            using var session = new MeshLayerMaskPainterSession(_target, _paintShader);
            session.InitializeNew(32);
            session.BeginStroke();
            session.Stamp(Vector3.zero, Vector3.forward, new Vector2(0.5f, 0.5f), 1, 0.3f, 1f, 1f, 85f, 1f);
            session.EndStroke();

            Texture2D savePreview = session.CreateReadableSaveTexture();
            Color workingCenter = ReadCenter(session.CurrentTexture);
            Color paddedCenter = savePreview.GetPixel(savePreview.width / 2, savePreview.height / 2);
            Object.DestroyImmediate(savePreview);
            Assert.That(paddedCenter.r, Is.EqualTo(workingCenter.r).Within(0.02f),
                $"GPU-padding изменил цвет: working={workingCenter}, padded={paddedCenter}");

            session.SavePngAndAssign(TemporaryAssetPath);

            byte[] pngBytes = File.ReadAllBytes(Path.GetFullPath(TemporaryAssetPath));
            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                Assert.That(decoded.LoadImage(pngBytes, false), Is.True);
                Color center = decoded.GetPixel(decoded.width / 2, decoded.height / 2);
                Color corner = decoded.GetPixel(2, 2);
                Assert.That(center.r, Is.GreaterThan(0.9f), "PNG должна содержать окрашенный Layer 1.");
                Assert.That(center.a, Is.LessThan(0.1f));
                Assert.That(corner.a, Is.GreaterThan(0.9f), "Нетронутая область должна оставаться Layer 0.");
            }
            finally
            {
                Object.DestroyImmediate(decoded);
            }
        }

        private static Color ReadCenter(RenderTexture source)
        {
            Texture2D texture = ReadBack(source);
            Color color = texture.GetPixel(source.width / 2, source.height / 2);
            Object.DestroyImmediate(texture);
            return color;
        }

        private static Texture2D ReadBack(RenderTexture source)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = source;
            var texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
            texture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            return texture;
        }

        private static void ReplaceWithOverlappingQuadMesh(Mesh mesh)
        {
            mesh.Clear();
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(-0.5f, -0.5f, 10f),
                new Vector3(0.5f, -0.5f, 10f),
                new Vector3(0.5f, 0.5f, 10f),
                new Vector3(-0.5f, 0.5f, 10f)
            };
            mesh.normals = new[]
            {
                Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
                Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward
            };
            mesh.uv = new[]
            {
                Vector2.zero, Vector2.right, Vector2.one, Vector2.up,
                Vector2.zero, Vector2.right, Vector2.one, Vector2.up
            };
            mesh.triangles = new[]
            {
                0, 1, 2, 0, 2, 3,
                4, 5, 6, 4, 6, 7
            };
            mesh.RecalculateBounds();
        }
    }
}
