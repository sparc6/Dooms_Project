using UnityEngine;

namespace TheTower.EditorTools.MeshMaskPainter
{
    internal sealed class MeshLayerMaskRaycaster
    {
        private GameObject _colliderObject;
        private MeshCollider _collider;
        private Transform _sourceTransform;
        private int _firstTriangle;
        private int _triangleCount;
        private int _materialSlot;

        internal void Build(MeshLayerMaskTarget target)
        {
            Dispose();

            _sourceTransform = target.Renderer.transform;
            _colliderObject = new GameObject("Mesh Layer Mask Painter Raycaster")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = 2
            };
            _collider = _colliderObject.AddComponent<MeshCollider>();
            _collider.sharedMesh = target.Mesh;

            _materialSlot = target.MaterialSlot;
            UpdateTriangleRange(target.Mesh);

            SyncTransform();
        }

        internal bool Raycast(Ray ray, out RaycastHit hit)
        {
            return Raycast(ray, restrictToMaterialSlot: true, out hit);
        }

        internal bool RaycastAll(Ray ray, out RaycastHit hit)
        {
            return Raycast(ray, restrictToMaterialSlot: false, out hit);
        }

        internal void UpdateMesh(Mesh mesh)
        {
            if (!_collider || !mesh)
                return;

            _collider.sharedMesh = null;
            _collider.sharedMesh = mesh;
            UpdateTriangleRange(mesh);
        }

        private bool Raycast(Ray ray, bool restrictToMaterialSlot, out RaycastHit hit)
        {
            hit = default;
            if (!_collider || !_sourceTransform)
                return false;

            SyncTransform();
            if (!_collider.Raycast(ray, out hit, float.MaxValue))
                return false;

            return !restrictToMaterialSlot ||
                   hit.triangleIndex >= _firstTriangle && hit.triangleIndex < _firstTriangle + _triangleCount;
        }

        internal void Dispose()
        {
            if (_colliderObject)
                Object.DestroyImmediate(_colliderObject);

            _colliderObject = null;
            _collider = null;
            _sourceTransform = null;
        }

        private void UpdateTriangleRange(Mesh mesh)
        {
            _firstTriangle = 0;
            int safeSlot = Mathf.Clamp(_materialSlot, 0, Mathf.Max(0, mesh.subMeshCount - 1));
            for (int index = 0; index < safeSlot; index++)
            {
                if (mesh.GetTopology(index) == MeshTopology.Triangles)
                    _firstTriangle += (int)mesh.GetIndexCount(index) / 3;
            }
            _triangleCount = mesh.GetTopology(safeSlot) == MeshTopology.Triangles
                ? (int)mesh.GetIndexCount(safeSlot) / 3
                : 0;
        }

        private void SyncTransform()
        {
            Transform destination = _colliderObject.transform;
            destination.SetPositionAndRotation(_sourceTransform.position, _sourceTransform.rotation);
            destination.localScale = _sourceTransform.lossyScale;
        }
    }
}
