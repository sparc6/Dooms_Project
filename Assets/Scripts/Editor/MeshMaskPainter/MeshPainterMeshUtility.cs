using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace TheTower.EditorTools.MeshMaskPainter
{
    internal static class MeshPainterMeshUtility
    {
        internal static Mesh CreateReadableCopy(Mesh source, string name = null)
        {
            if (!source)
                throw new ArgumentNullException(nameof(source));

            var result = new Mesh
            {
                name = string.IsNullOrWhiteSpace(name) ? source.name : name,
                hideFlags = HideFlags.HideAndDontSave
            };
            CopyMeshData(source, result);
            return result;
        }

        internal static void CopyMeshData(Mesh source, Mesh destination)
        {
            if (!source)
                throw new ArgumentNullException(nameof(source));
            if (!destination)
                throw new ArgumentNullException(nameof(destination));
            if (ReferenceEquals(source, destination))
                return;

            Mesh.MeshDataArray readOnlyData = Mesh.AcquireReadOnlyMeshData(source);
            Mesh.MeshDataArray writableData = Mesh.AllocateWritableMeshData(1);
            bool applied = false;
            try
            {
                Mesh.MeshData sourceData = readOnlyData[0];
                Mesh.MeshData destinationData = writableData[0];
                VertexAttributeDescriptor[] attributes = source.GetVertexAttributes();
                destinationData.SetVertexBufferParams(source.vertexCount, attributes);

                for (int stream = 0; stream < source.vertexBufferCount; stream++)
                {
                    NativeArray<byte> sourceBytes = sourceData.GetVertexData<byte>(stream);
                    NativeArray<byte> destinationBytes = destinationData.GetVertexData<byte>(stream);
                    NativeArray<byte>.Copy(sourceBytes, destinationBytes, sourceBytes.Length);
                }

                int indexStride = source.indexFormat == IndexFormat.UInt16 ? sizeof(ushort) : sizeof(uint);
                NativeArray<byte> sourceIndices = sourceData.GetIndexData<byte>();
                destinationData.SetIndexBufferParams(sourceIndices.Length / indexStride, source.indexFormat);
                NativeArray<byte> destinationIndices = destinationData.GetIndexData<byte>();
                NativeArray<byte>.Copy(sourceIndices, destinationIndices, sourceIndices.Length);

                destinationData.subMeshCount = source.subMeshCount;
                for (int submesh = 0; submesh < source.subMeshCount; submesh++)
                {
                    destinationData.SetSubMesh(
                        submesh,
                        source.GetSubMesh(submesh),
                        MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                }

                Mesh.ApplyAndDisposeWritableMeshData(
                    writableData,
                    destination,
                    MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                applied = true;
                destination.bounds = source.bounds;
            }
            finally
            {
                readOnlyData.Dispose();
                if (!applied)
                    writableData.Dispose();
            }
        }

        internal static Mesh CreatePersistentCopy(Mesh source, string name = null)
        {
            Mesh result = CreateReadableCopy(source, name);
            result.hideFlags = HideFlags.None;
            return result;
        }
    }
}
