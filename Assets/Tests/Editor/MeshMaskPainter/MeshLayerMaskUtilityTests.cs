using NUnit.Framework;
using UnityEngine;

namespace TheTower.EditorTools.MeshMaskPainter.Tests
{
    public sealed class MeshLayerMaskUtilityTests
    {
        [Test]
        public void InitialColorSelectsLayerZero()
        {
            Vector4 weights = MeshLayerMaskUtility.DecodeWeights(MeshLayerMaskUtility.InitialLayerZeroColor);

            Assert.That(weights, Is.EqualTo(new Vector4(1f, 0f, 0f, 0f)));
        }

        [TestCase(0, 0f, 0f, 0f, 1f)]
        [TestCase(1, 1f, 0f, 0f, 0f)]
        [TestCase(2, 0f, 1f, 0f, 0f)]
        [TestCase(3, 0f, 0f, 1f, 0f)]
        public void FullStrengthUsesHdrpLayerChannelOrder(int layer, float r, float g, float b, float a)
        {
            Color result = MeshLayerMaskUtility.BlendExclusive(
                MeshLayerMaskUtility.InitialLayerZeroColor,
                layer,
                1f,
                4);

            Assert.That(result.r, Is.EqualTo(r).Within(0.0001f));
            Assert.That(result.g, Is.EqualTo(g).Within(0.0001f));
            Assert.That(result.b, Is.EqualTo(b).Within(0.0001f));
            Assert.That(result.a, Is.EqualTo(a).Within(0.0001f));
        }

        [Test]
        public void PartialStrokeNormalizesAndPreservesUnitWeight()
        {
            Color result = MeshLayerMaskUtility.BlendExclusive(
                new Color(0.2f, 0.2f, 0f, 0.2f),
                targetLayer: 1,
                amount: 0.5f,
                layerCount: 3);
            Vector4 weights = MeshLayerMaskUtility.DecodeWeights(result);

            Assert.That(weights.x + weights.y + weights.z, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(weights.y, Is.GreaterThan(weights.x));
            Assert.That(weights.w, Is.Zero.Within(0.0001f));
        }

        [Test]
        public void InactiveLayersAreClearedOnPaintedPixel()
        {
            Color result = MeshLayerMaskUtility.BlendExclusive(Color.white, 1, 0.5f, 2);

            Assert.That(result.g, Is.Zero.Within(0.0001f));
            Assert.That(result.b, Is.Zero.Within(0.0001f));
        }
    }
}
