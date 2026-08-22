using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

[Serializable]
public class MonochromeBackgroundCustomPass : CustomPass
{
    [NonSerialized]
    private ShaderTagId[] depthShaderTags;

    [SerializeField, Tooltip("Layers whose visible opaque pixels keep their original color.")]
    private LayerMask preservedLayers;

    [SerializeField, Tooltip("Material created from an HDRP Fullscreen Shader Graph.")]
    private Material compositingMaterial;

    protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
    {
        // ShaderTagId calls Shader.TagToID internally, which Unity does not allow
        // while a CustomPassVolume is being deserialized. Setup runs later, just
        // before the first execution of this pass.
        depthShaderTags = new[]
        {
            new ShaderTagId("DepthForwardOnly"),
            new ShaderTagId("DepthOnly"),
        };
    }

    protected override void Execute(CustomPassContext ctx)
    {
        if (compositingMaterial == null)
            return;

        RTHandle customColorBuffer = ctx.customColorBuffer.Value;
        RTHandle customDepthBuffer = ctx.customDepthBuffer.Value;

        // The fullscreen shader cannot read and write the camera color buffer at
        // the same time, so keep the post-processed source color in HDRP's custom
        // color buffer.
        CustomPassUtils.Copy(ctx, ctx.cameraColorBuffer, customColorBuffer);

        // Build a visibility mask from the original depth passes. Using the
        // renderers' own passes preserves alpha-clipped silhouettes.
        CoreUtils.SetRenderTarget(ctx.cmd, customDepthBuffer, ClearFlag.Depth);
        CustomPassUtils.DrawRenderers(
            ctx,
            depthShaderTags,
            preservedLayers,
            RenderQueueType.AllOpaque);

        // The graph samples Custom Color and Custom Depth, then writes the
        // composited result back to the camera color buffer.
        CoreUtils.SetRenderTarget(ctx.cmd, ctx.cameraColorBuffer, ClearFlag.None);
        CoreUtils.DrawFullScreen(
            ctx.cmd,
            compositingMaterial,
            ctx.propertyBlock,
            shaderPassId: 0);
    }

    public override IEnumerable<Material> RegisterMaterialForInspector()
    {
        yield return compositingMaterial;
    }

    protected override void Cleanup()
    {
        depthShaderTags = null;
    }
}
