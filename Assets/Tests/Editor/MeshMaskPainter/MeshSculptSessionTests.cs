using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TheTower.EditorTools.MeshMaskPainter.Tests
{
    public sealed class MeshSculptSessionTests
    {
        private const string TemporaryMeshPath = "Assets/Tests/Editor/MeshMaskPainter/__TemporarySculptedMesh.asset";
        private GameObject _gameObject;
        private Mesh _mesh;
        private Material _material;
        private MeshLayerMaskTarget _target;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject("Mesh Sculpt Session Test");
            MeshFilter filter = _gameObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = _gameObject.AddComponent<MeshRenderer>();
            _mesh = MeshLayerMaskValidationTests.CreateQuadMesh();
            filter.sharedMesh = _mesh;
            _material = new Material(Shader.Find("HDRP/LayeredLit"));
            _material.SetFloat(MeshLayerMaskUtility.LayerCountProperty, 2f);
            _material.SetFloat(MeshLayerMaskUtility.UvBlendMaskProperty, 0f);
            renderer.sharedMaterial = _material;
            Assert.That(MeshLayerMaskValidation.TryCreateGeometryTarget(_gameObject, 0, out _target, out string message), Is.True, message);
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(TemporaryMeshPath);
            Object.DestroyImmediate(_gameObject);
            if (_mesh)
                Object.DestroyImmediate(_mesh);
            Object.DestroyImmediate(_material);
        }

        [Test]
        public void RaiseAndLowerMoveAlongVertexNormals()
        {
            using var session = new MeshSculptSession(_target);
            RaycastHit hit = Raycast(_target, new Vector3(0f, 0f, 2f), Vector3.back);

            session.BeginStroke();
            Assert.That(session.Stamp(hit, MeshSculptTool.Raise, 2f, 1f, 1f), Is.EqualTo(4));
            session.EndStroke();
            foreach (Vector3 vertex in session.WorkingMesh.vertices)
                Assert.That(vertex.z, Is.EqualTo(0.2f).Within(0.0001f));

            hit = RaycastActive(_target, session, new Vector3(0f, 0f, 2f), Vector3.back);
            session.BeginStroke();
            session.Stamp(hit, MeshSculptTool.Lower, 2f, 1f, 1f);
            session.EndStroke();
            foreach (Vector3 vertex in session.WorkingMesh.vertices)
                Assert.That(vertex.z, Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void TangentsAreUpdatedBeforeStrokeEnds()
        {
            _mesh.RecalculateTangents();
            Vector4[] sourceTangents = _mesh.tangents;
            using var session = new MeshSculptSession(_target);
            RaycastHit hit = Raycast(_target, new Vector3(0.45f, 0.45f, 2f), Vector3.back);

            session.BeginStroke();
            Assert.That(session.BeginGrab(hit, 0.15f, 1f), Is.GreaterThan(0));
            session.ApplyGrab(new Vector3(0f, 0f, 0.5f), 1f);

            Vector4[] liveTangents = session.WorkingMesh.tangents;
            Assert.That(liveTangents.Length, Is.EqualTo(sourceTangents.Length));
            Assert.That(
                Vector3.Dot((Vector3)liveTangents[2], (Vector3)sourceTangents[2]),
                Is.LessThan(0.9999f),
                "Tangents должны соответствовать деформированной поверхности ещё до завершения stroke.");

            session.EndStroke();
        }

        [Test]
        public void GrabUsesFixedFalloffAndWorldDelta()
        {
            using var session = new MeshSculptSession(_target);
            RaycastHit hit = Raycast(_target, new Vector3(0f, 0f, 2f), Vector3.back);
            Vector3[] original = session.WorkingMesh.vertices;

            session.BeginStroke();
            Assert.That(session.BeginGrab(hit, 2f, 1f), Is.EqualTo(4));
            session.ApplyGrab(new Vector3(0.4f, 0.2f, 0f), 1f);
            session.EndStroke();

            Vector3[] moved = session.WorkingMesh.vertices;
            for (int index = 0; index < moved.Length; index++)
                Assert.That(Vector3.Distance(moved[index], original[index] + new Vector3(0.4f, 0.2f, 0f)), Is.LessThan(0.0001f));
        }

        [Test]
        public void UndoRedoRestoresSculptStroke()
        {
            using var session = new MeshSculptSession(_target);
            RaycastHit hit = Raycast(_target, new Vector3(0f, 0f, 2f), Vector3.back);

            session.BeginStroke();
            session.Stamp(hit, MeshSculptTool.Raise, 2f, 1f, 1f);
            session.EndStroke();
            Assert.That(session.IsDirty, Is.True);

            Assert.That(session.Undo(), Is.True);
            Assert.That(session.WorkingMesh.vertices[0].z, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(session.IsDirty, Is.False);
            Assert.That(session.Redo(), Is.True);
            Assert.That(session.WorkingMesh.vertices[0].z, Is.GreaterThan(0f));
        }

        [Test]
        public void SmoothMovesInteriorButProtectsOpenBoundary()
        {
            ReplaceMesh(CreateRaisedGrid());
            using var session = new MeshSculptSession(_target);
            RaycastHit hit = Raycast(_target, new Vector3(0f, 0f, 3f), Vector3.back);
            Vector3[] before = session.WorkingMesh.vertices;

            session.BeginStroke();
            session.Stamp(hit, MeshSculptTool.Smooth, 3f, 1f, 1f);
            session.EndStroke();

            Vector3[] after = session.WorkingMesh.vertices;
            Assert.That(after[4].z, Is.LessThan(before[4].z), "Центральная вершина должна сгладиться.");
            Assert.That(after[0], Is.EqualTo(before[0]), "Открытая граница должна оставаться неподвижной.");
            Assert.That(after[8], Is.EqualTo(before[8]), "Открытая граница должна оставаться неподвижной.");
        }

        [Test]
        public void GeodesicBrushDoesNotReachDisconnectedBackSurface()
        {
            ReplaceMesh(CreateTwoLayerMesh());
            using var session = new MeshSculptSession(_target);
            RaycastHit hit = Raycast(_target, new Vector3(0f, 0f, 2f), Vector3.back);

            session.BeginStroke();
            session.Stamp(hit, MeshSculptTool.Raise, 0.2f, 1f, 1f);
            session.EndStroke();

            Vector3[] vertices = session.WorkingMesh.vertices;
            for (int index = 0; index < 4; index++)
                Assert.That(vertices[index].z, Is.EqualTo(0f).Within(0.0001f), "Задняя несвязная поверхность не должна изменяться.");
            for (int index = 4; index < 8; index++)
                Assert.That(vertices[index].z, Is.GreaterThan(0.05f), "Передняя поверхность должна подняться.");
        }

        [Test]
        public void CoincidentUvSeamVerticesMoveTogether()
        {
            ReplaceMesh(CreateUvSeamMesh());
            using var session = new MeshSculptSession(_target);
            RaycastHit hit = Raycast(_target, new Vector3(0.49f, 0.49f, 2f), Vector3.back);

            session.BeginStroke();
            session.Stamp(hit, MeshSculptTool.Raise, 0.1f, 1f, 1f);
            session.EndStroke();

            Vector3[] vertices = session.WorkingMesh.vertices;
            Assert.That(vertices[2].z, Is.GreaterThan(0f));
            Assert.That(vertices[4].z, Is.EqualTo(vertices[2].z).Within(0.0001f));
        }

        [Test]
        public void SaveCreatesAssetAssignsFilterAndLinkedCollider()
        {
            MeshCollider collider = _gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = _mesh;
            Assert.That(MeshLayerMaskValidation.TryCreateGeometryTarget(_gameObject, 0, out MeshLayerMaskTarget target, out _), Is.True);

            using var session = new MeshSculptSession(target);
            RaycastHit hit = Raycast(target, new Vector3(0f, 0f, 2f), Vector3.back);
            session.BeginStroke();
            session.Stamp(hit, MeshSculptTool.Raise, 2f, 1f, 1f);
            session.EndStroke();

            Mesh saved = session.SaveMeshAssetAndAssign(TemporaryMeshPath);

            Assert.That(saved, Is.Not.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<Mesh>(TemporaryMeshPath), Is.SameAs(saved));
            Assert.That(_gameObject.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(saved));
            Assert.That(collider.sharedMesh, Is.SameAs(saved));
            Assert.That(session.IsDirty, Is.False);
            Assert.That(saved.vertices[0].z, Is.GreaterThan(0f), "Сохранённый asset должен содержать Sculpt-деформацию.");
            Assert.That(_mesh.vertices[0].z, Is.EqualTo(0f).Within(0.0001f), "Исходный меш не должен изменяться.");
        }

        [Test]
        public void SaveOverwritesExistingAssetVertexBuffersWithoutChangingAssetIdentity()
        {
            using var session = new MeshSculptSession(_target);
            RaycastHit hit = Raycast(_target, new Vector3(0f, 0f, 2f), Vector3.back);
            session.BeginStroke();
            session.Stamp(hit, MeshSculptTool.Raise, 2f, 1f, 1f);
            session.EndStroke();
            Mesh firstSave = session.SaveMeshAssetAndAssign(TemporaryMeshPath);
            float firstHeight = firstSave.vertices[0].z;

            hit = RaycastActive(_target, session, new Vector3(0f, 0f, 2f), Vector3.back);
            session.BeginStroke();
            session.Stamp(hit, MeshSculptTool.Raise, 2f, 1f, 1f);
            session.EndStroke();
            float workingHeight = session.WorkingMesh.vertices[0].z;
            Mesh secondSave = session.SaveMeshAssetAndAssign(TemporaryMeshPath);

            Assert.That(secondSave, Is.SameAs(firstSave), "Повторный Save должен сохранять GUID и объект Mesh asset.");
            Assert.That(secondSave.vertices[0].z, Is.EqualTo(workingHeight).Within(0.0001f));
            Assert.That(secondSave.vertices[0].z, Is.GreaterThan(firstHeight));
        }

        [Test]
        public void SaveDoesNotReplaceIndependentColliderMesh()
        {
            Mesh independentColliderMesh = Object.Instantiate(_mesh);
            MeshCollider collider = _gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = independentColliderMesh;
            Assert.That(MeshLayerMaskValidation.TryCreateGeometryTarget(_gameObject, 0, out MeshLayerMaskTarget target, out _), Is.True);

            using (var session = new MeshSculptSession(target))
            {
                RaycastHit hit = Raycast(target, new Vector3(0f, 0f, 2f), Vector3.back);
                session.BeginStroke();
                session.Stamp(hit, MeshSculptTool.Raise, 2f, 1f, 1f);
                session.EndStroke();
                session.SaveMeshAssetAndAssign(TemporaryMeshPath);

                Assert.That(collider.sharedMesh, Is.SameAs(independentColliderMesh));
                Assert.That(session.ColliderSyncSkipped, Is.True);
            }

            Object.DestroyImmediate(independentColliderMesh);
        }

        [Test]
        public void ReadOnlySourceClonePreservesGeometryStreams()
        {
            var source = MeshLayerMaskValidationTests.CreateQuadMesh();
            source.colors = new[] { Color.red, Color.green, Color.blue, Color.white };
            int[] indices = source.triangles;
            Vector2[] uv = source.uv;
            source.UploadMeshData(true);

            Mesh copy = MeshPainterMeshUtility.CreateReadableCopy(source);
            try
            {
                Assert.That(copy.isReadable, Is.True);
                Assert.That(copy.triangles, Is.EqualTo(indices));
                Assert.That(copy.uv, Is.EqualTo(uv));
                Assert.That(copy.colors.Length, Is.EqualTo(4));
                Assert.That(copy.subMeshCount, Is.EqualTo(source.subMeshCount));
            }
            finally
            {
                Object.DestroyImmediate(copy);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void CombinedHistoryUndoesPaintAndSculptInChronologicalOrder()
        {
            Shader paintShader = Shader.Find("Hidden/TheTower/MeshLayerMaskPainter");
            Assert.That(paintShader, Is.Not.Null);
            using var paint = new MeshLayerMaskPainterSession(_target, paintShader);
            using var sculpt = new MeshSculptSession(_target);
            var history = new MeshPainterCombinedHistory();

            paint.InitializeNew(32);
            history.PrepareNewStroke(paint, sculpt);
            paint.BeginStroke();
            paint.Stamp(Vector3.zero, Vector3.forward, new Vector2(0.5f, 0.5f), 1, 0.4f, 1f, 1f, 85f, 1f);
            Assert.That(paint.EndStroke(), Is.True);
            history.RegisterCompleted(MeshPainterHistoryKind.Paint, paint, sculpt);

            RaycastHit hit = Raycast(_target, new Vector3(0f, 0f, 2f), Vector3.back);
            history.PrepareNewStroke(paint, sculpt);
            sculpt.BeginStroke();
            sculpt.Stamp(hit, MeshSculptTool.Raise, 2f, 1f, 1f);
            Assert.That(sculpt.EndStroke(), Is.True);
            history.RegisterCompleted(MeshPainterHistoryKind.Sculpt, paint, sculpt);

            Assert.That(history.Undo(paint, sculpt), Is.True);
            Assert.That(sculpt.WorkingMesh.vertices[0].z, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(ReadCenter(paint.CurrentTexture).r, Is.GreaterThan(0.9f));

            Assert.That(history.Undo(paint, sculpt), Is.True);
            Assert.That(ReadCenter(paint.CurrentTexture).a, Is.GreaterThan(0.9f));
            Assert.That(history.Redo(paint, sculpt), Is.True);
            Assert.That(ReadCenter(paint.CurrentTexture).r, Is.GreaterThan(0.9f));
        }

        private static RaycastHit Raycast(MeshLayerMaskTarget target, Vector3 origin, Vector3 direction)
        {
            using var raycaster = new DisposableRaycaster(target);
            Assert.That(raycaster.Value.RaycastAll(new Ray(origin, direction), out RaycastHit hit), Is.True);
            return hit;
        }

        private void ReplaceMesh(Mesh replacement)
        {
            Object.DestroyImmediate(_mesh);
            _mesh = replacement;
            _gameObject.GetComponent<MeshFilter>().sharedMesh = replacement;
            Assert.That(MeshLayerMaskValidation.TryCreateGeometryTarget(_gameObject, 0, out _target, out string message), Is.True, message);
        }

        private static Mesh CreateRaisedGrid()
        {
            var mesh = new Mesh { name = "Raised Grid" };
            mesh.vertices = new[]
            {
                new Vector3(-1f, -1f, 0f), new Vector3(0f, -1f, 0f), new Vector3(1f, -1f, 0f),
                new Vector3(-1f, 0f, 0f), new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 0f),
                new Vector3(-1f, 1f, 0f), new Vector3(0f, 1f, 0f), new Vector3(1f, 1f, 0f)
            };
            mesh.triangles = new[]
            {
                0, 1, 4, 0, 4, 3,
                1, 2, 5, 1, 5, 4,
                3, 4, 7, 3, 7, 6,
                4, 5, 8, 4, 8, 7
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateTwoLayerMesh()
        {
            var mesh = new Mesh { name = "Disconnected Layers" };
            mesh.vertices = new[]
            {
                new Vector3(-0.05f, -0.05f, 0f), new Vector3(0.05f, -0.05f, 0f),
                new Vector3(0.05f, 0.05f, 0f), new Vector3(-0.05f, 0.05f, 0f),
                new Vector3(-0.05f, -0.05f, 0.05f), new Vector3(0.05f, -0.05f, 0.05f),
                new Vector3(0.05f, 0.05f, 0.05f), new Vector3(-0.05f, 0.05f, 0.05f)
            };
            mesh.normals = new[]
            {
                Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
                Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateUvSeamMesh()
        {
            var mesh = new Mesh { name = "UV Seam Mesh" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f)
            };
            mesh.normals = new[]
            {
                Vector3.forward, Vector3.forward, Vector3.forward,
                Vector3.forward, Vector3.forward, Vector3.forward
            };
            mesh.uv = new[]
            {
                Vector2.zero, Vector2.right, Vector2.one,
                Vector2.zero, Vector2.one, Vector2.up
            };
            mesh.triangles = new[] { 0, 1, 2, 3, 4, 5 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Color ReadCenter(RenderTexture source)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = source;
            var texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
            texture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            texture.Apply();
            Color color = texture.GetPixel(source.width / 2, source.height / 2);
            Object.DestroyImmediate(texture);
            RenderTexture.active = previous;
            return color;
        }

        private static RaycastHit RaycastActive(MeshLayerMaskTarget target, MeshSculptSession session, Vector3 origin, Vector3 direction)
        {
            using var raycaster = new DisposableRaycaster(target);
            raycaster.Value.UpdateMesh(session.WorkingMesh);
            Assert.That(raycaster.Value.RaycastAll(new Ray(origin, direction), out RaycastHit hit), Is.True);
            return hit;
        }

        private sealed class DisposableRaycaster : System.IDisposable
        {
            internal DisposableRaycaster(MeshLayerMaskTarget target)
            {
                Value = new MeshLayerMaskRaycaster();
                Value.Build(target);
            }

            internal MeshLayerMaskRaycaster Value { get; }
            public void Dispose() => Value.Dispose();
        }
    }
}
