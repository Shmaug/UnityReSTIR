using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Rendering/PTRenderPipelineAsset")]
public class PTRenderPipelineAsset : RenderPipelineAsset {
    [Header("Path tracing")]
    public uint _MaxBounces = 2;

    [Header("ReSTIR")]
    public bool _TemporalReuse = false;
    public uint _SpatialReusePasses = 2;
    public uint _SpatialReuseSamples = 1;
    public float _SpatialReuseRadius = 64;
    public float _MCap = 30;

    [Header("Accumulation")]
    public uint _TargetSampleCount = 0;
    public float _DepthReuseCutoff = 0.01f;
    public float _NormalReuseCutoff = 3;
    
    [Header("Debug")]
    public bool _DebugCounters = false;
    public string _DebugCounterText;
    public float _ReuseX = 0;

    public bool _PauseRendering = false;

    protected override RenderPipeline CreatePipeline() {
        return new PTRenderPipeline(this);
    }
}
