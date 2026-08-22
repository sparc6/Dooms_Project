using UnityEngine;
using UnityEngine.Rendering;

namespace TheTower.EditorTools.MeshMaskPainter
{
    internal readonly struct MeshLayerMaskTarget
    {
        internal MeshLayerMaskTarget(GameObject gameObject, MeshFilter filter, MeshRenderer renderer, Mesh mesh, Material material, int materialSlot)
        {
            GameObject = gameObject;
            Filter = filter;
            Renderer = renderer;
            Mesh = mesh;
            Material = material;
            MaterialSlot = materialSlot;
        }

        internal GameObject GameObject { get; }
        internal MeshFilter Filter { get; }
        internal MeshRenderer Renderer { get; }
        internal Mesh Mesh { get; }
        internal Material Material { get; }
        internal int MaterialSlot { get; }
    }

    internal static class MeshLayerMaskValidation
    {
        internal static bool TryCreateGeometryTarget(GameObject gameObject, int materialSlot, out MeshLayerMaskTarget target, out string message)
        {
            target = default;

            if (!gameObject)
                return Fail("Выберите GameObject с MeshFilter и MeshRenderer.", out message);

            if (gameObject.GetComponent<SkinnedMeshRenderer>())
                return Fail("SkinnedMeshRenderer не поддерживается в этой версии инструмента.", out message);

            MeshFilter filter = gameObject.GetComponent<MeshFilter>();
            MeshRenderer renderer = gameObject.GetComponent<MeshRenderer>();
            if (!filter || !renderer)
                return Fail("Объект должен содержать MeshFilter и MeshRenderer.", out message);

            Mesh mesh = filter.sharedMesh;
            if (!mesh)
                return Fail("В MeshFilter не назначен меш.", out message);

            if (mesh.vertexCount == 0)
                return Fail("Назначенный меш не содержит вершин.", out message);

            Material[] materials = renderer.sharedMaterials;
            int safeSlot = Mathf.Clamp(materialSlot, 0, Mathf.Max(0, Mathf.Min(mesh.subMeshCount, materials.Length) - 1));
            Material material = safeSlot < materials.Length ? materials[safeSlot] : null;

            bool hasTriangleSubmesh = false;
            for (int index = 0; index < mesh.subMeshCount; index++)
            {
                if (mesh.GetTopology(index) == MeshTopology.Triangles && mesh.GetIndexCount(index) >= 3)
                {
                    hasTriangleSubmesh = true;
                    break;
                }
            }

            if (!hasTriangleSubmesh)
                return Fail("Меш должен содержать хотя бы один треугольный submesh.", out message);

            target = new MeshLayerMaskTarget(gameObject, filter, renderer, mesh, material, safeSlot);
            message = string.Empty;
            return true;
        }

        internal static bool TryCreateTarget(GameObject gameObject, int materialSlot, out MeshLayerMaskTarget target, out string message)
        {
            if (!TryCreateGeometryTarget(gameObject, materialSlot, out target, out message))
                return false;

            Mesh mesh = target.Mesh;

            if (!mesh.HasVertexAttribute(VertexAttribute.TexCoord0) ||
                mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord0) < 2)
                return Fail("Меш не содержит полноценный UV0-канал.", out message);

            Material[] materials = target.Renderer.sharedMaterials;
            if (materialSlot < 0 || materialSlot >= materials.Length || materialSlot >= mesh.subMeshCount)
                return Fail("Выбранный material slot не соответствует submesh меша.", out message);

            if (mesh.GetTopology(materialSlot) != MeshTopology.Triangles)
                return Fail("Выбранный submesh должен состоять из треугольников.", out message);

            Material material = materials[materialSlot];
            if (!material)
                return Fail("В выбранном material slot отсутствует материал.", out message);

            if (!material.HasProperty(MeshLayerMaskUtility.LayerMaskProperty) ||
                !material.HasProperty(MeshLayerMaskUtility.LayerCountProperty))
            {
                return Fail("Материал не является совместимым HDRP Layered Lit материалом.", out message);
            }

            int layerCount = MeshLayerMaskUtility.GetLayerCount(material);
            if (layerCount < 2 || layerCount > 4)
                return Fail("Layer Count материала должен быть от 2 до 4.", out message);

            if (material.HasProperty(MeshLayerMaskUtility.UvBlendMaskProperty) &&
                !Mathf.Approximately(material.GetFloat(MeshLayerMaskUtility.UvBlendMaskProperty), 0f))
            {
                return Fail("BlendMask UV Mapping должен быть установлен в UV0.", out message);
            }

            if (!MeshLayerMaskUtility.IsIdentityScaleOffset(material))
                return Fail("Tiling Layer Mask должен быть (1, 1), а Offset — (0, 0).", out message);

            target = new MeshLayerMaskTarget(gameObject, target.Filter, target.Renderer, mesh, material, materialSlot);
            message = string.Empty;
            return true;
        }

        internal static int GetMaximumSlot(GameObject gameObject)
        {
            if (!gameObject)
                return 0;

            MeshFilter filter = gameObject.GetComponent<MeshFilter>();
            MeshRenderer renderer = gameObject.GetComponent<MeshRenderer>();
            if (!filter || !filter.sharedMesh || !renderer)
                return 0;

            return Mathf.Max(0, Mathf.Min(filter.sharedMesh.subMeshCount, renderer.sharedMaterials.Length) - 1);
        }

        private static bool Fail(string error, out string message)
        {
            message = error;
            return false;
        }
    }
}
