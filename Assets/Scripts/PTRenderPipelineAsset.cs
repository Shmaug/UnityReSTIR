using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Rendering/PTRenderPipelineAsset")]
public class PTRenderPipelineAsset : RenderPipelineAsset {
    public uint _MaxDepth = 1;
    public uint _TargetSampleCount = 0;

    protected override RenderPipeline CreatePipeline() {
        return new PTRenderPipeline(this);
    }
}
