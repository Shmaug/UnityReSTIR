using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Mathematics;

struct AccumulationData {
    public RenderTexture _AccumulationTexture;
    public RenderTexture _VisibilityTexture;
    public Matrix4x4 _CameraToWorld;
    public Matrix4x4 _Projection;
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
        foreach (AccumulationData tex in _AccumulationTextures.Values) {
            tex._AccumulationTexture.Release();
            tex._VisibilityTexture.Release();
        }
        _AccumulationTextures.Clear();
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

    public void RenderCamera(CommandBuffer cmd, Camera camera, RenderTargetIdentifier renderTarget, int w, int h) {
        bool hasHistory = true;
        if (!_AccumulationTextures.TryGetValue(camera, out AccumulationData accum) ||
            !accum._AccumulationTexture ||
            accum._AccumulationTexture.width < w ||
            accum._AccumulationTexture.height < h) {
            if (accum._AccumulationTexture) {
                Object.DestroyImmediate(accum._AccumulationTexture);
                _AccumulationTextures.Remove(camera);
            }
            {
                RenderTextureDescriptor desc = new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGBHalf, 0, 1, RenderTextureReadWrite.Linear);
                desc.enableRandomWrite = true;
                accum._AccumulationTexture = new RenderTexture(desc);
            }
            {
                RenderTextureDescriptor desc = new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGBInt, 0, 1, RenderTextureReadWrite.Linear);
                desc.enableRandomWrite = true;
                accum._VisibilityTexture = new RenderTexture(desc);
            }
            _AccumulationTextures.Add(camera, accum);
            accum._AccumulationTexture.Create();
            accum._CameraToWorld = camera.transform.localToWorldMatrix;
            accum._Projection    = camera.projectionMatrix;
            hasHistory = false;
        }
        
        int albedo = Shader.PropertyToID("_Albedo");
        int vis = Shader.PropertyToID("_Visibility");
        int prevUV = Shader.PropertyToID("_PrevUV");
        {
            RenderTextureDescriptor desc = new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGBHalf, 0, 1, RenderTextureReadWrite.Linear);
            desc.enableRandomWrite = true;
            cmd.GetTemporaryRT(albedo, desc);
        }
        {
            RenderTextureDescriptor desc = new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGBInt, 0, 1, RenderTextureReadWrite.Linear);
            desc.enableRandomWrite = true;
            cmd.GetTemporaryRT(vis, desc);
        }
        {
            RenderTextureDescriptor desc = new RenderTextureDescriptor(w, h, RenderTextureFormat.RGFloat, 0, 1, RenderTextureReadWrite.Linear);
            desc.enableRandomWrite = true;
            cmd.GetTemporaryRT(prevUV, desc);
        }

        // Render
        {
            if (_PathStateBuffer == null || _PathStateBuffer.count < w*h)
                _PathStateBuffer = new ComputeBuffer(w*h, 3*Marshal.SizeOf(typeof(float4)));

            cmd.SetRayTracingBufferParam(_PathTracerShader, "_State", _PathStateBuffer);
            
            cmd.SetRayTracingTextureParam(_PathTracerShader, "_OutputImage", renderTarget);
            cmd.SetRayTracingTextureParam(_PathTracerShader, "_OutputAlbedo", albedo);
            cmd.SetRayTracingTextureParam(_PathTracerShader, "_OutputVisibility", vis);
            cmd.SetRayTracingTextureParam(_PathTracerShader, "_OutputPrevUVs", prevUV);
            cmd.SetRayTracingIntParams(_PathTracerShader, "_OutputExtent", new int[]{ w, h });

            cmd.SetRayTracingMatrixParam(_PathTracerShader, "_CameraToWorld",           camera.cameraToWorldMatrix);
            cmd.SetRayTracingMatrixParam(_PathTracerShader, "_WorldToCamera",           camera.worldToCameraMatrix);
            cmd.SetRayTracingMatrixParam(_PathTracerShader, "_CameraInverseProjection", camera.projectionMatrix.inverse);

            cmd.SetRayTracingMatrixParam(_PathTracerShader, "_PrevWorldToClip", accum._Projection * accum._CameraToWorld.inverse);

            cmd.DispatchRays(_PathTracerShader, "RenderFirstBounce", (uint)w, (uint)h, 1);
            for (int i = 1; i < _Asset._MaxDepth; i++)
                cmd.DispatchRays(_PathTracerShader, "RenderNextBounce" , (uint)w, (uint)h, 1);
        }

        // Accumulate/denoise
        {

            int accumKernel = _AccumulateShader.FindKernel("Accumulate");
            cmd.SetComputeTextureParam(_AccumulateShader, accumKernel, "_OutputImage", renderTarget);
            cmd.SetComputeTextureParam(_AccumulateShader, accumKernel, "_AlbedoImage", albedo);
            cmd.SetComputeTextureParam(_AccumulateShader, accumKernel, "_VisibilityImage", vis);
            cmd.SetComputeTextureParam(_AccumulateShader, accumKernel, "_PrevUVs", prevUV);
            cmd.SetComputeTextureParam(_AccumulateShader, accumKernel, "_AccumulationImage", accum._AccumulationTexture);
            cmd.SetComputeTextureParam(_AccumulateShader, accumKernel, "_PrevVisibilityImage", accum._VisibilityTexture);
            cmd.SetComputeIntParams(_AccumulateShader, "_OutputExtent", new int[]{ w, h });

            bool clear = false;
            if (_Asset._TargetSampleCount == 0) {
                bool4x4 b = (float4x4)camera.transform.localToWorldMatrix != (float4x4)accum._CameraToWorld;
                clear = math.any(b[0]) || math.any(b[1]) || math.any(b[2]) || math.any(b[3]);
            }
            cmd.SetComputeIntParams(_AccumulateShader, "_Clear", (clear || !hasHistory) ? 1 : 0);
            cmd.SetComputeIntParams(_AccumulateShader, "_MaxSamples", (int)_Asset._TargetSampleCount);
            
            _AccumulateShader.GetKernelThreadGroupSizes(accumKernel, out uint kw, out uint kh, out _);
            cmd.DispatchCompute(_AccumulateShader, accumKernel, (w + (int)kw-1)/(int)kw, (h + (int)kh-1)/(int)kh, 1);
            
            accum._CameraToWorld = camera.transform.localToWorldMatrix;
        }

        cmd.CopyTexture(vis, accum._VisibilityTexture);

        cmd.ReleaseTemporaryRT(albedo);
        cmd.ReleaseTemporaryRT(vis);
        cmd.ReleaseTemporaryRT(prevUV);

        _AccumulationTextures[camera] = accum;
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
                {
                    RenderTextureDescriptor desc = new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGBHalf, 0, 1, RenderTextureReadWrite.Linear);
                    desc.enableRandomWrite = true;
                    cmd.GetTemporaryRT(outputRT, desc);
                }

                RenderCamera(cmd, camera, outputRT, w, h);

                cmd.Blit(outputRT, BuiltinRenderTextureType.CameraTarget);
                cmd.ReleaseTemporaryRT(outputRT);

            }
        }

        context.ExecuteCommandBuffer(cmd);

        #if UNITY_EDITOR
        foreach (Camera camera in cameras) {
            if (camera.cameraType == CameraType.SceneView && UnityEditor.Handles.ShouldRenderGizmos()) {
                context.DrawUIOverlay(camera);
                context.DrawWireOverlay(camera);
                context.DrawGizmos(camera, GizmoSubset.PreImageEffects);
                context.DrawGizmos(camera, GizmoSubset.PostImageEffects);
            }
        }
        #endif

        context.Submit();
    }
}
