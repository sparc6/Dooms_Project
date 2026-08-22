using UnityEngine;

namespace TheTower.EditorTools.MeshMaskPainter
{
    internal static class MeshLayerMaskUtility
    {
        internal const string LayerMaskProperty = "_LayerMaskMap";
        internal const string LayerCountProperty = "_LayerCount";
        internal const string UvBlendMaskProperty = "_UVBlendMask";

        internal static Color InitialLayerZeroColor => new Color(0f, 0f, 0f, 1f);

        internal static int GetLayerCount(Material material)
        {
            if (!material || !material.HasProperty(LayerCountProperty))
                return 0;

            return Mathf.Clamp(Mathf.RoundToInt(material.GetFloat(LayerCountProperty)), 0, 4);
        }

        internal static Color BlendExclusive(Color encoded, int targetLayer, float amount, int layerCount)
        {
            layerCount = Mathf.Clamp(layerCount, 2, 4);
            targetLayer = Mathf.Clamp(targetLayer, 0, layerCount - 1);
            amount = Mathf.Clamp01(amount);

            Vector4 weights = DecodeWeights(encoded);
            for (int index = layerCount; index < 4; index++)
                weights[index] = 0f;

            float sum = 0f;
            for (int index = 0; index < layerCount; index++)
            {
                weights[index] = Mathf.Max(0f, weights[index]);
                sum += weights[index];
            }

            if (sum <= 0.00001f)
            {
                weights = new Vector4(1f, 0f, 0f, 0f);
            }
            else
            {
                weights /= sum;
            }

            for (int index = 0; index < layerCount; index++)
            {
                float target = index == targetLayer ? 1f : 0f;
                weights[index] = Mathf.Lerp(weights[index], target, amount);
            }

            return EncodeWeights(weights);
        }

        internal static Vector4 DecodeWeights(Color encoded)
        {
            return new Vector4(encoded.a, encoded.r, encoded.g, encoded.b);
        }

        internal static Color EncodeWeights(Vector4 weights)
        {
            return new Color(weights.y, weights.z, weights.w, weights.x);
        }

        internal static bool IsIdentityScaleOffset(Material material)
        {
            if (!material || !material.HasProperty(LayerMaskProperty))
                return false;

            Vector2 scale = material.GetTextureScale(LayerMaskProperty);
            Vector2 offset = material.GetTextureOffset(LayerMaskProperty);
            return Approximately(scale, Vector2.one) && Approximately(offset, Vector2.zero);
        }

        private static bool Approximately(Vector2 left, Vector2 right)
        {
            return Mathf.Approximately(left.x, right.x) && Mathf.Approximately(left.y, right.y);
        }
    }
}
