using System.Runtime.InteropServices;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Mathematics;

public class LightManager {
    ComputeBuffer _LightBuffer = null;

    public void Release() {
        _LightBuffer?.Release();
    }

    [StructLayout(LayoutKind.Sequential)]
    struct GpuLight {
        public float4x4 _LightToWorld;
        public float3 _Color;
        public uint _Type;
        public float _Range;
        public float _InnerSpotAngle; // also _ShadowAngle for directional lights
        public float _OuterSpotAngle;
        public float pad;
    };

    public void BuildLightBuffer(CommandBuffer cmd, RayTracingShader shader) {
        List<GpuLight> lights = new List<GpuLight>();
        foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) {
            GpuLight l = new GpuLight();
            l._LightToWorld = light.transform.localToWorldMatrix;
            l._Color = (Vector3)(Vector4)light.color * light.intensity;
            lights.Add(l);
        }
        if (lights.Count == 0) return;

        if (_LightBuffer == null || _LightBuffer.count < lights.Count) {
            _LightBuffer = new ComputeBuffer(lights.Count, Marshal.SizeOf(typeof(GpuLight)));
        }

        cmd.SetBufferData(_LightBuffer, lights.ToArray());
        
        cmd.SetRayTracingBufferParam(shader, "_Lights", _LightBuffer);
        cmd.SetRayTracingIntParam(shader, "_LightCount", lights.Count);
    }
}