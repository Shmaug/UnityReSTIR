using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Mathematics;

struct AccumulationData {
    public RenderTexture _AccumulationTexture;
    public RenderTexture _PositionsTexture;
    public Matrix4x4 _WorldToClip;
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
    ComputeShader _AccumulateShader;
    Material _DefaultMaterial;
    
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
        _AccumulateShader = Resources.Load<ComputeShader>("Accumulate");
        _DefaultMaterial = new Material(Resources.Load<Shader>("Opaque"));
        _LightManager = new LightManager();
    }
    protected override void Dispose(bool disposing) {
        Object.DestroyImmediate(_DefaultMaterial);
        _StandardMaterialMap.Clear();
        foreach (AccumulationData tex in _AccumulationData.Values) {
            tex._AccumulationTexture.Release();
            tex._PositionsTexture.Release();
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

    public void RenderCamera(CommandBuffer cmd, Camera camera, RenderTargetIdentifier renderTarget, int w, int h) {
        int albedo    = Shader.PropertyToID("_Albedo");
        int positions = Shader.PropertyToID("_Positions");
        cmd.GetTemporaryRT(albedo,    GetDescriptor(w, h, RenderTextureFormat.ARGBHalf));
        cmd.GetTemporaryRT(positions, GetDescriptor(w, h, RenderTextureFormat.ARGBFloat));

        for (int i = 0; i < 2; i++)
            if (_PathReservoirsBuffers[i] == null || _PathReservoirsBuffers[i].count < w*h)
                _PathReservoirsBuffers[i] = new ComputeBuffer(w*h, 80);

        if (_DebugCounterBuffer == null)
            _DebugCounterBuffer = new ComputeBuffer(16, 4);

        cmd.SetBufferData(_DebugCounterBuffer, Enumerable.Repeat(0, _DebugCounterBuffer.count).ToArray());

        // Render
        {
            cmd.SetRayTracingShaderPass(_PathTracerShader, "PathTrace");
            cmd.SetRayTracingTextureParam(_PathTracerShader, "_Radiance", renderTarget);
            cmd.SetRayTracingTextureParam(_PathTracerShader, "_Albedo", albedo);
            cmd.SetRayTracingTextureParam(_PathTracerShader, "_Positions", positions);
            cmd.SetRayTracingIntParams(_PathTracerShader, "_OutputExtent", new int[]{ w, h });
            cmd.SetRayTracingIntParam(_PathTracerShader, "_MaxBounces", (int)_Asset._MaxBounces);
            
            cmd.SetRayTracingBufferParam(_PathTracerShader, "_PathReservoirsOut", _PathReservoirsBuffers[0]);
            cmd.SetRayTracingBufferParam(_PathTracerShader, "_PathReservoirsIn", _PathReservoirsBuffers[1]);

            cmd.SetRayTracingMatrixParam(_PathTracerShader, "_CameraToWorld",           camera.cameraToWorldMatrix);
            cmd.SetRayTracingMatrixParam(_PathTracerShader, "_CameraInverseProjection", camera.nonJitteredProjectionMatrix.inverse);

            cmd.SetRayTracingBufferParam(_PathTracerShader, "_DebugCounters", _DebugCounterBuffer);

            cmd.DispatchRays(_PathTracerShader, "TracePaths", (uint)w, (uint)h, 1);
        }

        // spatial reuse
        if (_Asset._SpatialReusePasses > 0) {
            cmd.SetRayTracingIntParam(_PathTracerShader, "_SpatialReuseSamples", (int)_Asset._SpatialReuseSamples);
            cmd.SetRayTracingFloatParam(_PathTracerShader, "_ReuseX",  _Asset._ReuseX);
            cmd.SetRayTracingFloatParam(_PathTracerShader, "_SpatialReuseRadius",  _Asset._SpatialReuseRadius);
            cmd.SetRayTracingFloatParam(_PathTracerShader, "_MCap", _Asset._MCap);
            for (int i = 0; i < _Asset._SpatialReusePasses; i++)
            {
                cmd.SetRayTracingBufferParam(_PathTracerShader, "_PathReservoirsOut", _PathReservoirsBuffers[1 - (i%2)]);
                cmd.SetRayTracingBufferParam(_PathTracerShader, "_PathReservoirsIn",  _PathReservoirsBuffers[i%2]);
                cmd.SetRayTracingIntParam(_PathTracerShader, "_SpatialReuseIteration", i);
                cmd.DispatchRays(_PathTracerShader, "SpatialReuse", (uint)w, (uint)h, 1);
            }
        }

        // Accumulate/denoise
        int accumKernel = _AccumulateShader.FindKernel("Accumulate");
        if (_AccumulateShader.IsSupported(accumKernel)) {
            bool hasHistory = true;
            if (!_AccumulationData.TryGetValue(camera, out AccumulationData accumData) || !accumData._AccumulationTexture ||
                accumData._AccumulationTexture.width != w || accumData._AccumulationTexture.height != h) {
                if (accumData._AccumulationTexture) {
                    Object.DestroyImmediate(accumData._AccumulationTexture);
                    Object.DestroyImmediate(accumData._PositionsTexture);
                }
                accumData._AccumulationTexture = new RenderTexture(GetDescriptor(w, h, RenderTextureFormat.ARGBHalf));
                accumData._PositionsTexture    = new RenderTexture(GetDescriptor(w, h, RenderTextureFormat.ARGBFloat));
                accumData._AccumulationTexture.Create();
                accumData._PositionsTexture.Create();
                accumData._WorldToClip = camera.nonJitteredProjectionMatrix * camera.worldToCameraMatrix;
                if (_AccumulationData.ContainsKey(camera))
                    _AccumulationData.Remove(camera);
                _AccumulationData.Add(camera, accumData);
                hasHistory = false;
            }

            int accum = Shader.PropertyToID("_Accumulated");
            cmd.GetTemporaryRT(accum, GetDescriptor(w, h, RenderTextureFormat.ARGBHalf));

            cmd.SetComputeTextureParam(_AccumulateShader, accumKernel, "_Radiance",             renderTarget);
            cmd.SetComputeTextureParam(_AccumulateShader, accumKernel, "_Albedo",               albedo);
            cmd.SetComputeTextureParam(_AccumulateShader, accumKernel, "_Positions",            positions);
            cmd.SetComputeTextureParam(_AccumulateShader, accumKernel, "_AccumulatedColor",     accum);
            cmd.SetComputeTextureParam(_AccumulateShader, accumKernel, "_PrevAccumulatedColor", accumData._AccumulationTexture);
            cmd.SetComputeTextureParam(_AccumulateShader, accumKernel, "_PrevPositions",        accumData._PositionsTexture);
            cmd.SetComputeBufferParam (_AccumulateShader, accumKernel, "_DebugCounters", _DebugCounterBuffer);
            cmd.SetComputeIntParams   (_AccumulateShader, "_OutputExtent", new int[]{ w, h });
            cmd.SetComputeMatrixParam (_AccumulateShader, "_PrevWorldToClip", accumData._WorldToClip);
            cmd.SetComputeIntParams   (_AccumulateShader, "_Clear", hasHistory ? 0 : 1);
            cmd.SetComputeIntParams   (_AccumulateShader, "_MaxSamples", (int)_Asset._TargetSampleCount);
            cmd.SetComputeFloatParam  (_AccumulateShader, "_DepthReuseCutoff", _Asset._DepthReuseCutoff);
            cmd.SetComputeFloatParam  (_AccumulateShader, "_NormalReuseCutoff", Mathf.Cos(_Asset._NormalReuseCutoff*Mathf.Deg2Rad));
            
            _AccumulateShader.GetKernelThreadGroupSizes(accumKernel, out uint kw, out uint kh, out _);
            cmd.DispatchCompute(_AccumulateShader, accumKernel, (w + (int)kw-1)/(int)kw, (h + (int)kh-1)/(int)kh, 1);
                    
            cmd.CopyTexture(accum, accumData._AccumulationTexture);
            cmd.CopyTexture(positions, accumData._PositionsTexture);
            accumData._WorldToClip = camera.nonJitteredProjectionMatrix * camera.worldToCameraMatrix;
            _AccumulationData[camera] = accumData;

            cmd.ReleaseTemporaryRT(accum);
        }
        
        cmd.ReleaseTemporaryRT(albedo);
        cmd.ReleaseTemporaryRT(positions);
    }

    float lastCounterPrint = 0;
    protected override void Render(ScriptableRenderContext context, List<Camera> cameras) {
        CommandBuffer cmd = new CommandBuffer();
        if (_Asset._PauseRendering) {
            cmd.ClearRenderTarget(true, true, Color.magenta);
        } else {
            cmd.ClearRenderTarget(true, true, Color.black);
            
            if (BuildAccelerationStructure(cmd)) {            
                cmd.SetRayTracingAccelerationStructure(_PathTracerShader, "_AccelerationStructure", _AccelerationStructure);
                
                _LightManager.BuildLightBuffer(cmd, _PathTracerShader);

                int outputRT = Shader.PropertyToID("_OutputImage");
            
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
        if (_Asset._DebugCounters && Time.time - lastCounterPrint > 2) {
            int[] counters = new int[_DebugCounterBuffer.count];
            _DebugCounterBuffer.GetData(counters);
            _Asset._DebugCounterText = "";
            for (int i = 0; i < (int)DebugCounterType.NUM_DEBUG_COUNTERS; i++) {
                _Asset._DebugCounterText += ((DebugCounterType)i).ToString().PadRight(20) + counters[i] + "\n";
            }
            lastCounterPrint = Time.time;
        }
        #endif

        context.Submit();
    }
    
    protected override void Render(ScriptableRenderContext context, Camera[] cameras) {
        Render(context, cameras.ToList());
    }
}
