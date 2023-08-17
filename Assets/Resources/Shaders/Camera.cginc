#ifndef CAMERA_H
#define CAMERA_H

// for unity_CameraToWorld
#include "UnityCG.cginc"
#include "Utils.cginc"

RWTexture2D<float4> _Radiance;
RWTexture2D<float4> _Albedo;
RWTexture2D<float4> _Positions;
uint2 _OutputExtent;

float4x4 _CameraInverseProjection;

RayDesc GetCameraRay(uint2 id) {
    RayDesc ray;
    ray.TMin = 0;
    ray.TMax = POS_INFINITY;
    ray.Origin    = mul(unity_CameraToWorld, float4(0, 0, 0, 1)).xyz;
    ray.Direction = mul(_CameraInverseProjection, float4((float2(id + 0.5) / float2(_OutputExtent))*2-1, 0, 1)).xyz;
    ray.Direction = normalize(mul(unity_CameraToWorld, float4(ray.Direction, 0)).xyz);
    return ray;
}

#endif