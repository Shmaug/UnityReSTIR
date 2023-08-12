using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Mathematics;

struct AccumulationData {
    public RenderTexture _AccumulationTexture;
    public float4x4 _CameraToWorld;
};

public class PTRenderPipeline : RenderPipeline {
    RayTracingShader _PathTracerShader;
    ComputeShader _AccumulateShader;

    Material _DefaultMaterial = null;
    Dictionary<Material, MaterialPropertyBlock> _StandardMaterialMap = new Dictionary<Material, MaterialPropertyBlock>();

    RayTracingAccelerationStructure _AccelerationStructure = null;
    LightManager _LightManager = null;
    ComputeBuffer _PathStateBuffer;

    Dictionary<Camera, AccumulationData> _AccumulationTextures = new Dictionary<Camera, AccumulationData>();

    int _FrameIndex = 0;

    PTRenderPipelineAsset _Asset;

    public PTRenderPipeline(PTRenderPipelineAsset asset) {
        _Asset = asset;
        _PathTracerShader = Resources.Load<RayTracingShader>("Shaders/PathTrace");
        _AccumulateShader = Resources.Load<ComputeShader>("Shaders/Accumulate");
        _DefaultMaterial = new Material(Resources.Load<Shader>("Shaders/Opaque"));
        _LightManager = new LightManager();
    }
    protected override void Dispose(bool disposing) {
        _LightManager.Release();
        _AccelerationStructure?.Dispose();
        _PathStateBuffer?.Dispose();
        Object.DestroyImmediate(_DefaultMaterial);
        _StandardMaterialMap.Clear();
        foreach (AccumulationData tex in _AccumulationTextures.Values)
            tex._AccumulationTexture.Release();
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


    protected override void Render(ScriptableRenderContext context, Camera[] cameras) {
        CommandBuffer cmd = new CommandBuffer();
        cmd.ClearRenderTarget(true, true, Color.black);
        
        if (BuildAccelerationStructure(cmd)) {            
            cmd.SetRayTracingAccelerationStructure(_PathTracerShader, "_AccelerationStructure", _AccelerationStructure);
            
            _LightManager.BuildLightBuffer(cmd, _PathTracerShader);

            int outputRT = Shader.PropertyToID("_OutputImage");
        
            cmd.SetRayTracingShaderPass(_PathTracerShader, "PathTrace");
            cmd.SetRayTracingIntParam(_PathTracerShader, "_Seed", _FrameIndex++);

            foreach (Camera camera in cameras) {
                int w = camera.pixelWidth;
                int h = camera.pixelHeight;
                RenderTextureDescriptor desc = new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGBFloat, 0, 1, RenderTextureReadWrite.Linear);
                desc.enableRandomWrite = true;
                cmd.GetTemporaryRT(outputRT, desc);

                // Render
                {
                    if (_PathStateBuffer == null || _PathStateBuffer.count < w*h)
                        _PathStateBuffer = new ComputeBuffer(w*h, 3*Marshal.SizeOf(typeof(float4)));

                    cmd.SetRayTracingBufferParam(_PathTracerShader, "_State", _PathStateBuffer);
                    
                    cmd.SetRayTracingTextureParam(_PathTracerShader, "_OutputImage", outputRT);
                    cmd.SetRayTracingIntParams(_PathTracerShader, "_OutputExtent", new int[]{ w, h });

                    cmd.SetRayTracingMatrixParam(_PathTracerShader, "_CameraToWorldMatrix",           camera.cameraToWorldMatrix);
                    cmd.SetRayTracingMatrixParam(_PathTracerShader, "_WorldToCameraMatrix",           camera.worldToCameraMatrix);
                    cmd.SetRayTracingMatrixParam(_PathTracerShader, "_CameraInverseProjectionMatrix", camera.projectionMatrix.inverse);

                    cmd.DispatchRays(_PathTracerShader, "RenderFirstBounce", (uint)w, (uint)h, 1);
                    for (int i = 1; i < _Asset._MaxDepth; i++)
                        cmd.DispatchRays(_PathTracerShader, "RenderNextBounce" , (uint)w, (uint)h, 1);
                }

                // Accumulate/denoise
                {
                    if (!_AccumulationTextures.TryGetValue(camera, out AccumulationData accum) || accum._AccumulationTexture.width < w || accum._AccumulationTexture.height < h) {
                        if (accum._AccumulationTexture) {
                            Object.DestroyImmediate(accum._AccumulationTexture);
                            _AccumulationTextures.Remove(camera);
                        }
                        accum._AccumulationTexture = new RenderTexture(desc);
                        _AccumulationTextures.Add(camera, accum);
                        accum._AccumulationTexture.Create();
                        accum._CameraToWorld = Matrix4x4.identity;
                    }

                    int accumKernel = _AccumulateShader.FindKernel("Accumulate");
                    cmd.SetComputeTextureParam(_AccumulateShader, accumKernel, "_AccumulationImage", accum._AccumulationTexture);
                    cmd.SetComputeTextureParam(_AccumulateShader, accumKernel, "_OutputImage", outputRT);
                    cmd.SetComputeIntParams(_AccumulateShader, "_OutputExtent", new int[]{ w, h });

                    bool4x4 b = (float4x4)camera.transform.localToWorldMatrix != accum._CameraToWorld;
                    cmd.SetComputeIntParams(_AccumulateShader, "_Clear", math.any(b[0]) || math.any(b[1]) || math.any(b[2]) || math.any(b[3]) ? 1 : 0);
                    
                    _AccumulateShader.GetKernelThreadGroupSizes(accumKernel, out uint kw, out uint kh, out _);
                    cmd.DispatchCompute(_AccumulateShader, accumKernel, (w + (int)kw-1)/(int)kw, (h + (int)kh-1)/(int)kh, 1);
                    
                    accum._CameraToWorld = camera.transform.localToWorldMatrix;
                    _AccumulationTextures[camera] = accum;
                }

                cmd.Blit(outputRT, BuiltinRenderTextureType.CameraTarget);

                cmd.ReleaseTemporaryRT(outputRT);
            }
        }

        context.ExecuteCommandBuffer(cmd);
        context.Submit();
    }
}
