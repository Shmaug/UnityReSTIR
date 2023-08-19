using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Mathematics;

struct AccumulationData {
    public RenderTexture _AccumulationTexture;
    public RenderTexture _PositionsTexture;
    public Matrix4x4 _WorldToClip;
    public ComputeBuffer _Reservoirs;
};

enum DebugCounterType {
    RAYS,
    SHADOW_RAYS,

    SHIFT_ATTEMPTS,
    SHIFT_SUCCESSES,

    NUM_DEBUG_COUNTERS
};

public class PTRenderPipeline : RenderPipeline {
    PTRenderPipelineAsset _Asset;

    RayTracingShader _PathTracerShader;
    ComputeShader _CopyReservoirsShader;
    ComputeShader _AccumulateShader;
    Material _DefaultMaterial;
    Material _BlitMaterial;
    
    Dictionary<Material, MaterialPropertyBlock> _StandardMaterialMap = new Dictionary<Material, MaterialPropertyBlock>();
    Dictionary<Camera, AccumulationData> _AccumulationData = new Dictionary<Camera, AccumulationData>();

    RayTracingAccelerationStructure _AccelerationStructure = null;
    LightManager _LightManager;

    ComputeBuffer[] _PathReservoirsBuffers = new ComputeBuffer[2];
    ComputeBuffer _DebugCounterBuffer = null;
    int _FrameIndex = 0;

    public PTRenderPipeline(PTRenderPipelineAsset asset) {
        _Asset = asset;
        _PathTracerShader = Resources.Load<RayTracingShader>("PathTrace");
        _CopyReservoirsShader = Resources.Load<ComputeShader>("CopyReservoirs");
        _AccumulateShader = Resources.Load<ComputeShader>("Accumulate");
        _DefaultMaterial = new Material(Resources.Load<Shader>("Opaque"));
        _BlitMaterial = new Material(Resources.Load<Shader>("BlitResult"));
        _LightManager = new LightManager();
    }
    protected override void Dispose(bool disposing) {
        Object.DestroyImmediate(_DefaultMaterial);
        Object.DestroyImmediate(_BlitMaterial);
        _StandardMaterialMap.Clear();
        foreach (AccumulationData d in _AccumulationData.Values) {
            d._AccumulationTexture.Release();
            d._PositionsTexture.Release();
            d._Reservoirs?.Dispose();
        }
        _AccumulationData.Clear();
        _AccelerationStructure?.Dispose();
        _LightManager.Release();
        foreach (ComputeBuffer cb in _PathReservoirsBuffers)
            cb?.Dispose();
        _DebugCounterBuffer?.Dispose();
    }

    bool BuildAccelerationStructure(CommandBuffer cmd) {
        _AccelerationStructure ??= new RayTracingAccelerationStructure();
        _AccelerationStructure.ClearInstances();
        uint instanceCount = 0;
        foreach (MeshRenderer renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)) {
            MeshFilter mf = renderer.GetComponent<MeshFilter>();
            if (!mf || !mf.sharedMesh) continue;

            for (uint i = 0; i < mf.sharedMesh.subMeshCount; i++) {
                Material m = renderer.sharedMaterials[math.min(i, renderer.sharedMaterials.Length-1)];
                if (m == null) continue;
            
                bool isOpaque = m.renderQueue < (int)RenderQueue.AlphaTest;

                if (m.shader.name == "Standard") {
                    RayTracingMeshInstanceConfig cfg = new RayTracingMeshInstanceConfig(mf.sharedMesh, i, _DefaultMaterial);
                    cfg.accelerationStructureBuildFlags = renderer.rayTracingAccelerationStructureBuildFlags;
                    cfg.accelerationStructureBuildFlagsOverride = renderer.rayTracingAccelerationStructureBuildFlagsOverride;
                    cfg.mask = 0xFF;
                    cfg.enableTriangleCulling = false;
                    cfg.subMeshFlags = isOpaque ? RayTracingSubMeshFlags.Enabled|RayTracingSubMeshFlags.ClosestHitOnly : RayTracingSubMeshFlags.Enabled;
                    cfg.lightProbeUsage = renderer.lightProbeUsage;
                    cfg.renderingLayerMask = renderer.renderingLayerMask;
                    cfg.motionVectorMode = renderer.motionVectorGenerationMode;
                    cfg.layer = renderer.gameObject.layer;
                    if (!_StandardMaterialMap.TryGetValue(m, out cfg.materialProperties)) {
                        cfg.materialProperties = new MaterialPropertyBlock();
                        foreach (string id in m.GetPropertyNames(MaterialPropertyType.Texture)) {
                            var t = m.GetTexture(id);
                            if (t != null)
                                cfg.materialProperties.SetTexture(id, t);
                        }
                        foreach (string id in m.GetPropertyNames(MaterialPropertyType.Matrix)) {
                            cfg.materialProperties.SetMatrix(id, m.GetMatrix(id));
                        }
                        foreach (string id in m.GetPropertyNames(MaterialPropertyType.Float)) {
                            cfg.materialProperties.SetFloat(id, m.GetFloat(id));
                        }
                        foreach (string id in m.GetPropertyNames(MaterialPropertyType.Int)) {
                            cfg.materialProperties.SetInteger(id, m.GetInteger(id));
                        }
                        foreach (string id in m.GetPropertyNames(MaterialPropertyType.Vector)) {
                            cfg.materialProperties.SetVector(id, m.GetVector(id));
                        }
                        _StandardMaterialMap.Add(m, cfg.materialProperties);
                    }
                    _AccelerationStructure.AddInstance(cfg, renderer.localToWorldMatrix, null, instanceCount++);
                } else {
                    RayTracingSubMeshFlags[] flags = Enumerable.Repeat(RayTracingSubMeshFlags.Disabled, mf.sharedMesh.subMeshCount).ToArray();
                    flags[i] = isOpaque ? RayTracingSubMeshFlags.Enabled|RayTracingSubMeshFlags.ClosestHitOnly : RayTracingSubMeshFlags.Enabled;
                    _AccelerationStructure.AddInstance(renderer, flags, false, false, 0xFF, instanceCount++);
                }
            }
        }

        if (instanceCount == 0) return false;

        cmd.BuildRayTracingAccelerationStructure(_AccelerationStructure);

        return true;
    }

    RenderTextureDescriptor GetDescriptor(int w, int h, RenderTextureFormat format) {
        RenderTextureDescriptor desc = new RenderTextureDescriptor(w, h, format, 0, 1, RenderTextureReadWrite.Linear);
        desc.enableRandomWrite = true;
        return desc;
    }
    
    public void RenderCamera(ScriptableRenderContext context, CommandBuffer cmd, Camera camera) {
        int w = camera.pixelWidth;
        int h = camera.pixelHeight;

        int renderTarget = Shader.PropertyToID("_OutputImage");
        int albedo       = Shader.PropertyToID("_Albedo");
        int positions    = Shader.PropertyToID("_Positions");
        cmd.GetTemporaryRT(renderTarget, GetDescriptor(w, h, RenderTextureFormat.ARGBHalf));
        cmd.GetTemporaryRT(albedo,       GetDescriptor(w, h, RenderTextureFormat.ARGBHalf));
        cmd.GetTemporaryRT(positions,    GetDescriptor(w, h, RenderTextureFormat.ARGBFloat));

        if (_PathReservoirsBuffers[0] == null || _PathReservoirsBuffers[0].count < w*h)
            for (int i = 0; i < 2; i++)
                _PathReservoirsBuffers[i] = new ComputeBuffer(w*h, 80);

        int currentReservoirBuffer = 0;

        cmd.SetRayTracingShaderPass(_PathTracerShader, "PathTrace");

        // sample canonical paths
        {
            _LightManager.SetShaderParams(cmd, _PathTracerShader);

            cmd.SetRayTracingTextureParam(_PathTracerShader, "_Radiance", renderTarget);
            cmd.SetRayTracingTextureParam(_PathTracerShader, "_Albedo", albedo);
            cmd.SetRayTracingTextureParam(_PathTracerShader, "_Positions", positions);
            cmd.SetRayTracingIntParams(_PathTracerShader, "_OutputExtent", w, h);
            cmd.SetRayTracingIntParam(_PathTracerShader, "_MaxBounces", (int)_Asset._MaxBounces);
            
            cmd.SetRayTracingMatrixParam(_PathTracerShader, "_CameraToWorld",           camera.cameraToWorldMatrix);
            cmd.SetRayTracingMatrixParam(_PathTracerShader, "_CameraInverseProjection", camera.nonJitteredProjectionMatrix.inverse);

            cmd.SetRayTracingBufferParam(_PathTracerShader, "_PathReservoirsIn" , _PathReservoirsBuffers[currentReservoirBuffer^1]);
            cmd.SetRayTracingBufferParam(_PathTracerShader, "_PathReservoirsOut", _PathReservoirsBuffers[currentReservoirBuffer]);


            cmd.DispatchRays(_PathTracerShader, "TracePaths", (uint)w, (uint)h, 1);
        }

        bool hasHistory = _AccumulationData.TryGetValue(camera, out AccumulationData accumData) &&
            accumData._AccumulationTexture && accumData._AccumulationTexture.width == w && accumData._AccumulationTexture.height == h;
        if (!hasHistory) {            
            if (accumData._AccumulationTexture) {
                Object.DestroyImmediate(accumData._AccumulationTexture);
                Object.DestroyImmediate(accumData._PositionsTexture);
            }
            accumData._AccumulationTexture = new RenderTexture(GetDescriptor(w, h, RenderTextureFormat.ARGBHalf));
            accumData._PositionsTexture    = new RenderTexture(GetDescriptor(w, h, RenderTextureFormat.ARGBFloat));
            accumData._AccumulationTexture.Create();
            accumData._PositionsTexture.Create();

            if (accumData._Reservoirs != null)
                accumData._Reservoirs.Dispose();
            accumData._Reservoirs = new ComputeBuffer(w*h, _PathReservoirsBuffers[0].stride);

            accumData._WorldToClip = camera.nonJitteredProjectionMatrix * camera.worldToCameraMatrix;

            if (_AccumulationData.ContainsKey(camera))
                _AccumulationData.Remove(camera);
            _AccumulationData.Add(camera, accumData);
        }

        cmd.SetRayTracingFloatParam(_PathTracerShader, "_ReuseX",  _Asset._ReuseX);
        cmd.SetRayTracingFloatParam(_PathTracerShader, "_MCap", _Asset._MCap);

        // temporal reuse
        if (_Asset._TemporalReuse && hasHistory) {
            cmd.SetRayTracingBufferParam (_PathTracerShader, "_PrevReservoirs",  accumData._Reservoirs);
            cmd.SetRayTracingTextureParam(_PathTracerShader, "_PrevPositions",   accumData._PositionsTexture);
            cmd.SetRayTracingMatrixParam (_PathTracerShader, "_PrevWorldToClip", accumData._WorldToClip);
            cmd.SetRayTracingBufferParam(_PathTracerShader, "_PathReservoirsIn",  _PathReservoirsBuffers[currentReservoirBuffer]);
            cmd.SetRayTracingBufferParam(_PathTracerShader, "_PathReservoirsOut", _PathReservoirsBuffers[currentReservoirBuffer^1]);
            cmd.DispatchRays(_PathTracerShader, "TemporalReuse", (uint)w, (uint)h, 1);
            currentReservoirBuffer ^= 1;
        }

        // spatial reuse
        if (_Asset._SpatialReusePasses > 0) {
            _LightManager.SetShaderParams(cmd, _PathTracerShader);
            cmd.SetRayTracingBufferParam(_PathTracerShader, "_DebugCounters", _DebugCounterBuffer);
            cmd.SetRayTracingIntParam(_PathTracerShader, "_SpatialReuseSamples", (int)_Asset._SpatialReuseSamples);
            cmd.SetRayTracingFloatParam(_PathTracerShader, "_SpatialReuseRadius",  _Asset._SpatialReuseRadius);
            for (int i = 0; i < _Asset._SpatialReusePasses; i++) {
                cmd.SetRayTracingBufferParam(_PathTracerShader, "_PathReservoirsIn",  _PathReservoirsBuffers[currentReservoirBuffer]);
                cmd.SetRayTracingBufferParam(_PathTracerShader, "_PathReservoirsOut", _PathReservoirsBuffers[currentReservoirBuffer^1]);
                currentReservoirBuffer ^= 1;
                cmd.SetRayTracingIntParam(_PathTracerShader, "_SpatialReuseIteration", i);
                cmd.DispatchRays(_PathTracerShader, "SpatialReuse", (uint)w, (uint)h, 1);
            }
        }
        
        // copy final reservoirs for future reuse
        if (_Asset._TemporalReuse) {
            int copyReservoirKernel = _CopyReservoirsShader.FindKernel("CopyReservoirs");
            cmd.SetComputeBufferParam(_CopyReservoirsShader, copyReservoirKernel, "_Src", _PathReservoirsBuffers[currentReservoirBuffer]);
            cmd.SetComputeBufferParam(_CopyReservoirsShader, copyReservoirKernel, "_Dst", accumData._Reservoirs);
            cmd.SetComputeIntParams(_CopyReservoirsShader, "_OutputExtent", w*h, 1);
            _CopyReservoirsShader.GetKernelThreadGroupSizes(copyReservoirKernel, out uint kw, out uint _, out _);
            cmd.DispatchCompute(_CopyReservoirsShader, copyReservoirKernel, (w*h + (int)kw-1)/(int)kw, 1, 1);
        }

        // output radiance from final reservoir
        {
            int otuputRadianceKernel = _CopyReservoirsShader.FindKernel("OutputRadiance");
            cmd.SetComputeTextureParam(_CopyReservoirsShader, otuputRadianceKernel, "_Radiance", renderTarget);
            cmd.SetComputeBufferParam(_CopyReservoirsShader, otuputRadianceKernel, "_Src", _PathReservoirsBuffers[currentReservoirBuffer]);
            cmd.SetComputeIntParams(_CopyReservoirsShader, "_OutputExtent", w, h);
            _CopyReservoirsShader.GetKernelThreadGroupSizes(otuputRadianceKernel, out uint kw, out uint kh, out _);
            cmd.DispatchCompute(_CopyReservoirsShader, otuputRadianceKernel, (w + (int)kw-1)/(int)kw, (h + (int)kh-1)/(int)kh, 1);
        }

        // Accumulate/denoise
        {
            int accum = Shader.PropertyToID("_Accumulated");
            cmd.GetTemporaryRT(accum, GetDescriptor(w, h, RenderTextureFormat.ARGBHalf));
            
            int accumKernel = _AccumulateShader.FindKernel("Accumulate");

            cmd.SetComputeTextureParam(_AccumulateShader, accumKernel, "_Radiance",             renderTarget);
            cmd.SetComputeTextureParam(_AccumulateShader, accumKernel, "_Albedo",               albedo);
            cmd.SetComputeTextureParam(_AccumulateShader, accumKernel, "_Positions",            positions);
            cmd.SetComputeTextureParam(_AccumulateShader, accumKernel, "_AccumulatedColor",     accum);
            cmd.SetComputeTextureParam(_AccumulateShader, accumKernel, "_PrevAccumulatedColor", accumData._AccumulationTexture);
            cmd.SetComputeTextureParam(_AccumulateShader, accumKernel, "_PrevPositions",        accumData._PositionsTexture);
            cmd.SetComputeBufferParam (_AccumulateShader, accumKernel, "_DebugCounters", _DebugCounterBuffer);
            cmd.SetComputeIntParams   (_AccumulateShader, "_OutputExtent", w, h);
            cmd.SetComputeMatrixParam (_AccumulateShader, "_PrevWorldToClip", accumData._WorldToClip);
            cmd.SetComputeIntParams   (_AccumulateShader, "_Clear", hasHistory ? 0 : 1);
            cmd.SetComputeIntParams   (_AccumulateShader, "_MaxSamples", (int)_Asset._TargetSampleCount);
            cmd.SetComputeFloatParam  (_AccumulateShader, "_DepthReuseCutoff", _Asset._DepthReuseCutoff);
            cmd.SetComputeFloatParam  (_AccumulateShader, "_NormalReuseCutoff", Mathf.Cos(_Asset._NormalReuseCutoff*Mathf.Deg2Rad));
            
            _AccumulateShader.GetKernelThreadGroupSizes(accumKernel, out uint kw, out uint kh, out _);
            cmd.DispatchCompute(_AccumulateShader, accumKernel, (w + (int)kw-1)/(int)kw, (h + (int)kh-1)/(int)kh, 1);
                    
            cmd.CopyTexture(accum, accumData._AccumulationTexture);
            cmd.CopyTexture(positions, accumData._PositionsTexture);

            cmd.ReleaseTemporaryRT(accum);
        }

        accumData._WorldToClip = camera.nonJitteredProjectionMatrix * camera.worldToCameraMatrix;
        _AccumulationData[camera] = accumData;
        
        cmd.SetGlobalTexture("_PositionsTex", positions);
        cmd.Blit(renderTarget, BuiltinRenderTextureType.CameraTarget, _BlitMaterial);

        cmd.ReleaseTemporaryRT(albedo);
        cmd.ReleaseTemporaryRT(positions);
        cmd.ReleaseTemporaryRT(renderTarget);
    }

    float lastCounterPrint = 0;
    protected override void Render(ScriptableRenderContext context, List<Camera> cameras) {
        #if UNITY_EDITOR
        bool focused = UnityEditor.EditorApplication.isFocused;
        #else
        bool focused = Application.isFocused;
        #endif
        if (!focused) {
            context.Submit();
            return;
        }

        CommandBuffer cmd = new CommandBuffer();
        
        foreach (Camera camera in cameras) {
            cmd.SetViewMatrix(camera.worldToCameraMatrix);
            cmd.SetProjectionMatrix(camera.projectionMatrix);
            cmd.DrawRendererList(context.CreateSkyboxRendererList(camera, camera.projectionMatrix, camera.worldToCameraMatrix));
        }
        
        if (BuildAccelerationStructure(cmd)) {
            _LightManager.BuildLightBuffer(cmd);

            cmd.SetRayTracingAccelerationStructure(_PathTracerShader, "_AccelerationStructure", _AccelerationStructure);
            cmd.SetRayTracingIntParam(_PathTracerShader, "_Seed", _FrameIndex++);

            if (_DebugCounterBuffer == null)
                _DebugCounterBuffer = new ComputeBuffer(16, 4);
            cmd.SetBufferData(_DebugCounterBuffer, Enumerable.Repeat(0, _DebugCounterBuffer.count).ToArray());
            cmd.SetRayTracingBufferParam(_PathTracerShader, "_DebugCounters", _DebugCounterBuffer);
            foreach (Camera camera in cameras)
                RenderCamera(context, cmd, camera);
        }

        context.ExecuteCommandBuffer(cmd);
        cmd.Release();

        #if UNITY_EDITOR
        float pixels = 0;
        foreach (Camera camera in cameras) {
            if (camera.cameraType == CameraType.SceneView && UnityEditor.Handles.ShouldRenderGizmos()) {
                context.DrawGizmos(camera, GizmoSubset.PreImageEffects);
                context.DrawGizmos(camera, GizmoSubset.PostImageEffects);
                pixels += camera.pixelWidth*camera.pixelHeight;
            }
        }
        if (_Asset._DebugCounters && Time.time - lastCounterPrint > 1) {
            int[] counters = new int[_DebugCounterBuffer.count];
            _DebugCounterBuffer.GetData(counters);
            _Asset._DebugCounterText = "";
            _Asset._DebugCounterText += "Rays/pixel:   " + (counters[(int)DebugCounterType.RAYS          ]/pixels).ToString().PadRight(12);
            _Asset._DebugCounterText += "(" + (int)(0.5f + 100f*counters[(int)DebugCounterType.SHADOW_RAYS    ]/counters[(int)DebugCounterType.RAYS          ]) + "% shadow)\n";
            _Asset._DebugCounterText += "Shifts/pixel: " + (counters[(int)DebugCounterType.SHIFT_ATTEMPTS]/pixels).ToString().PadRight(12);
            _Asset._DebugCounterText += "(" + (int)(0.5f + 100f*counters[(int)DebugCounterType.SHIFT_SUCCESSES]/counters[(int)DebugCounterType.SHIFT_ATTEMPTS]) + "% success)\n";
            lastCounterPrint = Time.time;
        }
        #endif
    
        context.Submit();
    }
    
    protected override void Render(ScriptableRenderContext context, Camera[] cameras) {
        Render(context, cameras.ToList());
    }
}
