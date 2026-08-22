using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace TheTower.EditorTools.MeshMaskPainter
{
    internal enum MeshSculptTool
    {
        Raise,
        Lower,
        Smooth,
        Grab
    }

    internal sealed class MeshSculptSession : IDisposable
    {
        private readonly MeshLayerMaskTarget _target;
        private readonly Mesh _sourceMesh;
        private readonly Mesh _workingMesh;
        private readonly MeshCollider _sceneCollider;
        private readonly bool _sceneColliderWasLinked;
        private readonly List<MeshHistoryState> _history = new List<MeshHistoryState>();

        private Mesh _committedMesh;
        private MeshSculptTopology _topology;
        private Vector3[] _vertices;
        private int _historyIndex = -1;
        private long _nextHistoryId = 1;
        private long _savedHistoryId = -1;
        private bool _strokeActive;
        private bool _strokeChanged;
        private List<SculptInfluence> _grabInfluences;
        private Vector3[] _grabBaseVertices;
        private string _assetPath;

        internal MeshSculptSession(MeshLayerMaskTarget target)
        {
            _target = target;
            _sourceMesh = target.Mesh;
            _committedMesh = target.Mesh;
            _workingMesh = MeshPainterMeshUtility.CreateReadableCopy(target.Mesh, target.Mesh.name + " (Sculpt Preview)");
            _workingMesh.MarkDynamic();
            _vertices = _workingMesh.vertices;
            EnsureNormals();
            _topology = new MeshSculptTopology(_workingMesh, _vertices);

            _sceneCollider = target.GameObject.GetComponent<MeshCollider>();
            _sceneColliderWasLinked = _sceneCollider && _sceneCollider.sharedMesh == target.Mesh;

            string sourcePath = AssetDatabase.GetAssetPath(target.Mesh);
            if (!string.IsNullOrEmpty(sourcePath) &&
                string.Equals(Path.GetExtension(sourcePath), ".asset", StringComparison.OrdinalIgnoreCase) &&
                !AssetDatabase.IsSubAsset(target.Mesh))
            {
                _assetPath = sourcePath;
            }

            ResetHistory(markSaved: true);
        }

        internal Mesh WorkingMesh => _workingMesh;
        internal Mesh ActiveMesh => _target.Filter ? _target.Filter.sharedMesh : null;
        internal string SourceMeshName => _sourceMesh ? _sourceMesh.name : "SculptedMesh";
        internal string SourceAssetPath => AssetDatabase.GetAssetPath(_sourceMesh);
        internal string AssetPath => _assetPath;
        internal bool IsDirty => CurrentHistoryId != _savedHistoryId;
        internal bool CanUndo => _historyIndex > 0;
        internal bool CanRedo => _historyIndex >= 0 && _historyIndex < _history.Count - 1;
        internal long HistoryBytes => (long)_history.Count * _vertices.Length * sizeof(float) * 3L;
        internal bool ColliderSyncSkipped { get; private set; }

        internal void BeginStroke()
        {
            EnsurePreviewAssigned();
            _strokeActive = true;
            _strokeChanged = false;
            _grabInfluences = null;
            _grabBaseVertices = null;
        }

        internal int Stamp(RaycastHit hit, MeshSculptTool tool, float radius, float hardness, float strength)
        {
            if (!_strokeActive || tool == MeshSculptTool.Grab)
                return 0;

            List<SculptInfluence> influences = CalculateInfluences(hit, radius, hardness);
            if (influences.Count == 0)
                return 0;

            Vector3[] next = (Vector3[])_vertices.Clone();
            Matrix4x4 localToWorld = _target.Renderer.localToWorldMatrix;
            Matrix4x4 worldToLocal = localToWorld.inverse;
            Vector3[] nodePositions = GetNodeWorldPositions(localToWorld);
            Vector3[] nodeNormals = GetNodeWorldNormals(localToWorld);
            float safeStrength = Mathf.Clamp01(strength);

            foreach (SculptInfluence influence in influences)
            {
                MeshSculptNode node = _topology.Nodes[influence.NodeIndex];
                Vector3 worldDelta;
                switch (tool)
                {
                    case MeshSculptTool.Raise:
                    case MeshSculptTool.Lower:
                    {
                        float sign = tool == MeshSculptTool.Raise ? 1f : -1f;
                        worldDelta = nodeNormals[influence.NodeIndex] *
                                     (Mathf.Max(0.0001f, radius) * safeStrength * 0.1f * influence.Weight * sign);
                        break;
                    }
                    case MeshSculptTool.Smooth:
                    {
                        if (node.IsBoundary || node.Neighbors.Count == 0)
                            continue;

                        Vector3 average = Vector3.zero;
                        foreach (int neighbor in node.Neighbors)
                            average += nodePositions[neighbor];
                        average /= node.Neighbors.Count;
                        Vector3 delta = average - nodePositions[influence.NodeIndex];
                        Vector3 normal = nodeNormals[influence.NodeIndex];
                        Vector3 tangential = Vector3.ProjectOnPlane(delta, normal);
                        Vector3 normalCorrection = Vector3.Project(delta, normal) * 0.5f;
                        worldDelta = (tangential + normalCorrection) * (safeStrength * influence.Weight);
                        break;
                    }
                    default:
                        continue;
                }

                Vector3 localDelta = worldToLocal.MultiplyVector(worldDelta);
                foreach (int vertexIndex in node.VertexIndices)
                    next[vertexIndex] = _vertices[vertexIndex] + localDelta;
            }

            ApplyVertices(next, recalculateTangents: true);
            _strokeChanged = true;
            return CountVertices(influences);
        }

        internal int BeginGrab(RaycastHit hit, float radius, float hardness)
        {
            if (!_strokeActive)
                return 0;

            _grabInfluences = CalculateInfluences(hit, radius, hardness);
            _grabBaseVertices = (Vector3[])_vertices.Clone();
            return CountVertices(_grabInfluences);
        }

        internal int ApplyGrab(Vector3 worldDelta, float strength)
        {
            if (!_strokeActive || _grabInfluences == null || _grabBaseVertices == null)
                return 0;

            Matrix4x4 worldToLocal = _target.Renderer.localToWorldMatrix.inverse;
            Vector3 localDelta = worldToLocal.MultiplyVector(worldDelta * Mathf.Clamp01(strength));
            Vector3[] next = (Vector3[])_grabBaseVertices.Clone();
            foreach (SculptInfluence influence in _grabInfluences)
            {
                MeshSculptNode node = _topology.Nodes[influence.NodeIndex];
                foreach (int vertexIndex in node.VertexIndices)
                    next[vertexIndex] = _grabBaseVertices[vertexIndex] + localDelta * influence.Weight;
            }

            ApplyVertices(next, recalculateTangents: true);
            _strokeChanged = worldDelta.sqrMagnitude > 0.0000000001f;
            return CountVertices(_grabInfluences);
        }

        internal Vector3[] GetAffectedWorldPositions(RaycastHit hit, float radius, float hardness, out int vertexCount)
        {
            List<SculptInfluence> influences = CalculateInfluences(hit, radius, hardness);
            vertexCount = CountVertices(influences);
            Matrix4x4 localToWorld = _target.Renderer.localToWorldMatrix;
            Vector3[] result = new Vector3[influences.Count];
            for (int index = 0; index < influences.Count; index++)
            {
                MeshSculptNode node = _topology.Nodes[influences[index].NodeIndex];
                result[index] = localToWorld.MultiplyPoint3x4(_vertices[node.VertexIndices[0]]);
            }
            return result;
        }

        internal bool EndStroke()
        {
            if (!_strokeActive)
                return false;

            _strokeActive = false;
            _grabInfluences = null;
            _grabBaseVertices = null;
            if (!_strokeChanged)
                return false;

            RemoveRedoStates();
            _history.Add(CreateHistoryState());
            _historyIndex = _history.Count - 1;
            return true;
        }

        internal bool Undo()
        {
            if (!CanUndo)
                return false;
            EnsurePreviewAssigned();
            _historyIndex--;
            ApplyVertices(_history[_historyIndex].Vertices, recalculateTangents: true);
            return true;
        }

        internal bool Redo()
        {
            if (!CanRedo)
                return false;
            EnsurePreviewAssigned();
            _historyIndex++;
            ApplyVertices(_history[_historyIndex].Vertices, recalculateTangents: true);
            return true;
        }

        internal void ClearRedoStates()
        {
            RemoveRedoStates();
        }

        internal void DropOldestUndoCommand()
        {
            if (_history.Count <= 1 || _historyIndex <= 0)
                return;

            long removedId = _history[0].Id;
            _history.RemoveAt(0);
            _historyIndex--;
            if (_savedHistoryId == removedId)
                _savedHistoryId = -1;
        }

        internal Mesh SaveMeshAssetAndAssign(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                throw new ArgumentException("Меш должен быть сохранён внутри Assets.", nameof(assetPath));

            assetPath = Path.ChangeExtension(assetPath.Replace('\\', '/'), ".asset");
            Mesh savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (savedMesh)
            {
                MeshPainterMeshUtility.CopyMeshData(_workingMesh, savedMesh);
                savedMesh.name = Path.GetFileNameWithoutExtension(assetPath);
                savedMesh.hideFlags = HideFlags.None;
                EditorUtility.SetDirty(savedMesh);
            }
            else
            {
                if (AssetDatabase.LoadMainAssetAtPath(assetPath))
                    throw new InvalidOperationException($"Путь '{assetPath}' уже занят ассетом другого типа.");

                savedMesh = MeshPainterMeshUtility.CreatePersistentCopy(_workingMesh, Path.GetFileNameWithoutExtension(assetPath));
                AssetDatabase.CreateAsset(savedMesh, assetPath);
            }

            AssetDatabase.SaveAssets();

            Mesh previousCommitted = _committedMesh;
            UnityEditor.Undo.RecordObject(_target.Filter, "Assign Sculpted Mesh");
            _target.Filter.sharedMesh = savedMesh;
            PrefabUtility.RecordPrefabInstancePropertyModifications(_target.Filter);
            EditorUtility.SetDirty(_target.Filter);

            ColliderSyncSkipped = false;
            if (_sceneCollider)
            {
                if (_sceneColliderWasLinked &&
                    (_sceneCollider.sharedMesh == previousCommitted || _sceneCollider.sharedMesh == _sourceMesh))
                {
                    UnityEditor.Undo.RecordObject(_sceneCollider, "Assign Sculpted Collider Mesh");
                    _sceneCollider.sharedMesh = savedMesh;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(_sceneCollider);
                    EditorUtility.SetDirty(_sceneCollider);
                }
                else
                {
                    ColliderSyncSkipped = true;
                }
            }

            if (_target.GameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(_target.GameObject.scene);

            _committedMesh = savedMesh;
            _assetPath = assetPath;
            _savedHistoryId = CurrentHistoryId;
            return savedMesh;
        }

        public void Dispose()
        {
            if (_target.Filter && _target.Filter.sharedMesh == _workingMesh)
                _target.Filter.sharedMesh = _committedMesh;

            if (_workingMesh)
                UnityEngine.Object.DestroyImmediate(_workingMesh);
            _history.Clear();
            _vertices = Array.Empty<Vector3>();
        }

        private long CurrentHistoryId => _historyIndex >= 0 && _historyIndex < _history.Count
            ? _history[_historyIndex].Id
            : -1;

        private void EnsurePreviewAssigned()
        {
            if (_target.Filter && _target.Filter.sharedMesh != _workingMesh)
                _target.Filter.sharedMesh = _workingMesh;
        }

        private void EnsureNormals()
        {
            if (!_workingMesh.HasVertexAttribute(VertexAttribute.Normal) || _workingMesh.normals.Length != _workingMesh.vertexCount)
                _workingMesh.RecalculateNormals();
        }

        private void ApplyVertices(Vector3[] positions, bool recalculateTangents)
        {
            _vertices = (Vector3[])positions.Clone();
            _workingMesh.vertices = _vertices;
            _workingMesh.RecalculateBounds();
            _workingMesh.RecalculateNormals();
            if (recalculateTangents)
                RecalculateTangentsIfPresent();
            SceneView.RepaintAll();
        }

        private void RecalculateTangentsIfPresent()
        {
            if (_workingMesh.HasVertexAttribute(VertexAttribute.Tangent) &&
                _workingMesh.HasVertexAttribute(VertexAttribute.TexCoord0))
            {
                _workingMesh.RecalculateTangents();
            }
        }

        private List<SculptInfluence> CalculateInfluences(RaycastHit hit, float radius, float hardness)
        {
            float safeRadius = Mathf.Max(0.0001f, radius);
            if (!_topology.TryGetTriangleNodes(hit.triangleIndex, out int nodeA, out int nodeB, out int nodeC))
                return new List<SculptInfluence>();

            Matrix4x4 localToWorld = _target.Renderer.localToWorldMatrix;
            Vector3[] nodePositions = GetNodeWorldPositions(localToWorld);
            float[] distances = new float[_topology.Nodes.Count];
            for (int index = 0; index < distances.Length; index++)
                distances[index] = float.PositiveInfinity;

            var heap = new SculptMinHeap();
            Seed(nodeA);
            Seed(nodeB);
            Seed(nodeC);

            while (heap.Count > 0)
            {
                heap.Pop(out int nodeIndex, out float distance);
                if (distance > distances[nodeIndex] || distance > safeRadius)
                    continue;

                foreach (int neighbor in _topology.Nodes[nodeIndex].Neighbors)
                {
                    float nextDistance = distance + Vector3.Distance(nodePositions[nodeIndex], nodePositions[neighbor]);
                    if (nextDistance >= distances[neighbor] || nextDistance > safeRadius)
                        continue;
                    distances[neighbor] = nextDistance;
                    heap.Push(neighbor, nextDistance);
                }
            }

            var result = new List<SculptInfluence>();
            float innerRadius = safeRadius * Mathf.Clamp01(hardness);
            for (int nodeIndex = 0; nodeIndex < distances.Length; nodeIndex++)
            {
                float distance = distances[nodeIndex];
                if (distance > safeRadius)
                    continue;

                float weight = 1f;
                if (distance > innerRadius && innerRadius < safeRadius)
                {
                    float t = Mathf.Clamp01((distance - innerRadius) / (safeRadius - innerRadius));
                    weight = 1f - t * t * (3f - 2f * t);
                }
                result.Add(new SculptInfluence(nodeIndex, weight));
            }
            return result;

            void Seed(int nodeIndex)
            {
                float distance = Vector3.Distance(hit.point, nodePositions[nodeIndex]);
                if (distance >= distances[nodeIndex] || distance > safeRadius)
                    return;
                distances[nodeIndex] = distance;
                heap.Push(nodeIndex, distance);
            }
        }

        private Vector3[] GetNodeWorldPositions(Matrix4x4 localToWorld)
        {
            var result = new Vector3[_topology.Nodes.Count];
            for (int nodeIndex = 0; nodeIndex < result.Length; nodeIndex++)
            {
                MeshSculptNode node = _topology.Nodes[nodeIndex];
                Vector3 position = Vector3.zero;
                foreach (int vertexIndex in node.VertexIndices)
                    position += localToWorld.MultiplyPoint3x4(_vertices[vertexIndex]);
                result[nodeIndex] = position / node.VertexIndices.Count;
            }
            return result;
        }

        private Vector3[] GetNodeWorldNormals(Matrix4x4 localToWorld)
        {
            Vector3[] normals = _workingMesh.normals;
            Matrix4x4 normalMatrix = localToWorld.inverse.transpose;
            var result = new Vector3[_topology.Nodes.Count];
            for (int nodeIndex = 0; nodeIndex < result.Length; nodeIndex++)
            {
                Vector3 normal = Vector3.zero;
                foreach (int vertexIndex in _topology.Nodes[nodeIndex].VertexIndices)
                    normal += normalMatrix.MultiplyVector(normals[vertexIndex]).normalized;
                result[nodeIndex] = normal.sqrMagnitude > 0.000001f ? normal.normalized : Vector3.up;
            }
            return result;
        }

        private void ResetHistory(bool markSaved)
        {
            _history.Clear();
            MeshHistoryState initial = CreateHistoryState();
            _history.Add(initial);
            _historyIndex = 0;
            if (markSaved)
                _savedHistoryId = initial.Id;
        }

        private MeshHistoryState CreateHistoryState()
        {
            return new MeshHistoryState(_nextHistoryId++, (Vector3[])_vertices.Clone());
        }

        private void RemoveRedoStates()
        {
            for (int index = _history.Count - 1; index > _historyIndex; index--)
                _history.RemoveAt(index);
        }

        private int CountVertices(List<SculptInfluence> influences)
        {
            if (influences == null)
                return 0;
            int count = 0;
            foreach (SculptInfluence influence in influences)
                count += _topology.Nodes[influence.NodeIndex].VertexIndices.Count;
            return count;
        }

        private readonly struct MeshHistoryState
        {
            internal MeshHistoryState(long id, Vector3[] vertices)
            {
                Id = id;
                Vertices = vertices;
            }

            internal long Id { get; }
            internal Vector3[] Vertices { get; }
        }

        private readonly struct SculptInfluence
        {
            internal SculptInfluence(int nodeIndex, float weight)
            {
                NodeIndex = nodeIndex;
                Weight = weight;
            }

            internal int NodeIndex { get; }
            internal float Weight { get; }
        }

        private sealed class MeshSculptTopology
        {
            private readonly List<Vector3Int> _triangles = new List<Vector3Int>();

            internal MeshSculptTopology(Mesh mesh, Vector3[] vertices)
            {
                float epsilon = Mathf.Max(0.000001f, mesh.bounds.size.magnitude * 0.000001f);
                var nodesByPosition = new Dictionary<PositionKey, int>();
                int[] vertexToNode = new int[vertices.Length];
                Nodes = new List<MeshSculptNode>();

                for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
                {
                    var key = new PositionKey(vertices[vertexIndex], epsilon);
                    if (!nodesByPosition.TryGetValue(key, out int nodeIndex))
                    {
                        nodeIndex = Nodes.Count;
                        nodesByPosition.Add(key, nodeIndex);
                        Nodes.Add(new MeshSculptNode());
                    }
                    Nodes[nodeIndex].VertexIndices.Add(vertexIndex);
                    vertexToNode[vertexIndex] = nodeIndex;
                }

                var edgeUseCounts = new Dictionary<EdgeKey, int>();
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    if (mesh.GetTopology(submesh) != MeshTopology.Triangles)
                        continue;
                    int[] indices = mesh.GetIndices(submesh);
                    for (int index = 0; index + 2 < indices.Length; index += 3)
                    {
                        int a = vertexToNode[indices[index]];
                        int b = vertexToNode[indices[index + 1]];
                        int c = vertexToNode[indices[index + 2]];
                        _triangles.Add(new Vector3Int(a, b, c));
                        Connect(a, b);
                        Connect(b, c);
                        Connect(c, a);
                        CountEdge(a, b);
                        CountEdge(b, c);
                        CountEdge(c, a);
                    }
                }

                foreach (KeyValuePair<EdgeKey, int> pair in edgeUseCounts)
                {
                    if (pair.Value == 1)
                    {
                        Nodes[pair.Key.A].IsBoundary = true;
                        Nodes[pair.Key.B].IsBoundary = true;
                    }
                }

                void Connect(int a, int b)
                {
                    if (a == b)
                        return;
                    Nodes[a].Neighbors.Add(b);
                    Nodes[b].Neighbors.Add(a);
                }

                void CountEdge(int a, int b)
                {
                    if (a == b)
                        return;
                    var key = new EdgeKey(a, b);
                    edgeUseCounts.TryGetValue(key, out int count);
                    edgeUseCounts[key] = count + 1;
                }
            }

            internal List<MeshSculptNode> Nodes { get; }

            internal bool TryGetTriangleNodes(int triangleIndex, out int a, out int b, out int c)
            {
                if (triangleIndex < 0 || triangleIndex >= _triangles.Count)
                {
                    a = b = c = -1;
                    return false;
                }
                Vector3Int triangle = _triangles[triangleIndex];
                a = triangle.x;
                b = triangle.y;
                c = triangle.z;
                return true;
            }
        }

        private sealed class MeshSculptNode
        {
            internal readonly List<int> VertexIndices = new List<int>();
            internal readonly HashSet<int> Neighbors = new HashSet<int>();
            internal bool IsBoundary;
        }

        private readonly struct PositionKey : IEquatable<PositionKey>
        {
            private readonly long _x;
            private readonly long _y;
            private readonly long _z;

            internal PositionKey(Vector3 value, float epsilon)
            {
                _x = (long)Math.Round(value.x / epsilon);
                _y = (long)Math.Round(value.y / epsilon);
                _z = (long)Math.Round(value.z / epsilon);
            }

            public bool Equals(PositionKey other) => _x == other._x && _y == other._y && _z == other._z;
            public override bool Equals(object obj) => obj is PositionKey other && Equals(other);
            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _x.GetHashCode();
                    hash = (hash * 397) ^ _y.GetHashCode();
                    return (hash * 397) ^ _z.GetHashCode();
                }
            }
        }

        private readonly struct EdgeKey : IEquatable<EdgeKey>
        {
            internal EdgeKey(int a, int b)
            {
                A = Mathf.Min(a, b);
                B = Mathf.Max(a, b);
            }

            internal int A { get; }
            internal int B { get; }
            public bool Equals(EdgeKey other) => A == other.A && B == other.B;
            public override bool Equals(object obj) => obj is EdgeKey other && Equals(other);
            public override int GetHashCode() => (A * 397) ^ B;
        }

        private sealed class SculptMinHeap
        {
            private readonly List<HeapItem> _items = new List<HeapItem>();
            internal int Count => _items.Count;

            internal void Push(int node, float distance)
            {
                _items.Add(new HeapItem(node, distance));
                int index = _items.Count - 1;
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (_items[parent].Distance <= distance)
                        break;
                    _items[index] = _items[parent];
                    index = parent;
                }
                _items[index] = new HeapItem(node, distance);
            }

            internal void Pop(out int node, out float distance)
            {
                HeapItem root = _items[0];
                HeapItem tail = _items[_items.Count - 1];
                _items.RemoveAt(_items.Count - 1);
                if (_items.Count > 0)
                {
                    int index = 0;
                    while (true)
                    {
                        int left = index * 2 + 1;
                        if (left >= _items.Count)
                            break;
                        int right = left + 1;
                        int child = right < _items.Count && _items[right].Distance < _items[left].Distance ? right : left;
                        if (_items[child].Distance >= tail.Distance)
                            break;
                        _items[index] = _items[child];
                        index = child;
                    }
                    _items[index] = tail;
                }
                node = root.Node;
                distance = root.Distance;
            }

            private readonly struct HeapItem
            {
                internal HeapItem(int node, float distance)
                {
                    Node = node;
                    Distance = distance;
                }
                internal int Node { get; }
                internal float Distance { get; }
            }
        }
    }
}
