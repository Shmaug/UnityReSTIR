using System.Runtime.InteropServices;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Mathematics;
using System.Collections.ObjectModel;

public class LightManager {
    ComputeBuffer _LightBuffer = null;
    int _LightCount = 0;

    public void Release() {
        _LightBuffer?.Release();
    }

    [StructLayout(LayoutKind.Sequential)]
    struct GpuLight {
        public float4x4 _LightToWorld;
        public float3 _Color;
        public uint _Type;
        public float _Angle;
        public float _Radius;
        public float pad0;
        public float pad1;
    };

    public void BuildLightBuffer(CommandBuffer cmd) {
        List<GpuLight> lights = new List<GpuLight>();
        foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) {
            if (!light.isActiveAndEnabled) continue;
            GpuLight l = new GpuLight();
            l._LightToWorld = light.transform.localToWorldMatrix;
            l._Type = (uint)light.type;
            l._Color = (Vector3)(Vector4)light.color * light.intensity;
            l._Radius = light.shadowRadius;
            l._Angle = Mathf.Cos(Mathf.Deg2Rad*light.spotAngle/2);
            if (light.type == LightType.Directional) {
                if (light.shadows == LightShadows.Soft && light.shadowAngle > 0)
                    l._Angle = Mathf.Max(1e-4f, Mathf.Cos(Mathf.Deg2Rad*light.shadowAngle));
                else
                    l._Angle = 0;
            }
            lights.Add(l);
        }
        if (lights.Count == 0) return;

        if (_LightBuffer == null || _LightBuffer.count < lights.Count) {
            _LightBuffer = new ComputeBuffer(lights.Count, Marshal.SizeOf(typeof(GpuLight)));
        }

        cmd.SetBufferData(_LightBuffer, lights.ToArray());
        _LightCount = lights.Count;
    }
    public void SetShaderParams(CommandBuffer cmd, RayTracingShader shader) {        
        cmd.SetRayTracingBufferParam(shader, "_Lights", _LightBuffer);
        cmd.SetRayTracingIntParam(shader, "_LightCount", _LightCount);
    }
    public void SetShaderParams(CommandBuffer cmd, ComputeShader shader, int kernelIndex) {        
        cmd.SetComputeBufferParam(shader, kernelIndex, "_Lights", _LightBuffer);
        cmd.SetComputeIntParam(shader, "_LightCount", _LightCount);
    }
}