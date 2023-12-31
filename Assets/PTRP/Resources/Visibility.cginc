#ifndef VISIBILITY_H
#define VISIBILITY_H

#include "Utils.cginc"
#include "Counters.cginc"

RaytracingAccelerationStructure _AccelerationStructure;

RayDesc MakeRay(float3 origin, float3 direction, float tmin = 0, float tmax = POS_INFINITY) {
	RayDesc ray;
	ray.Origin = origin;
	ray.TMin = tmin;
	ray.Direction = direction;
	ray.TMax = tmax;
	return ray;
}

float3 OffsetRayOrigin(float3 pos, float3 n, float3 dir) {
    if (dot(n, dir) < 0) n = -n;
    
	// This function should be used to compute a modified ray start position for
	// rays leaving from a surface. This is from "A Fast and Robust Method for Avoiding
	// Self-Intersection" see https://research.nvidia.com/publication/2019-03_A-Fast-and
    float int_scale = 256.0;
    int3 of_i = int3(int_scale * n);

    float origin = 1.0 / 32.0;
    float float_scale = 1.0 / 65536.0;
    return float3(abs(pos.x) < origin ? pos.x + float_scale * n.x : asfloat(asint(pos.x) + ((pos.x < 0.0) ? -of_i.x : of_i.x)),
                  abs(pos.y) < origin ? pos.y + float_scale * n.y : asfloat(asint(pos.y) + ((pos.y < 0.0) ? -of_i.y : of_i.y)),
                  abs(pos.z) < origin ? pos.z + float_scale * n.z : asfloat(asint(pos.z) + ((pos.z < 0.0) ? -of_i.z : of_i.z)));
}

ShadingData TraceRay(RayDesc ray, uint flags = RAY_FLAG_NONE) {
	IncrementCounter(DEBUG_COUNTER_RAYS);
    ShadingData sd;
    TraceRay(_AccelerationStructure, flags, 0xFF, 0, 1, 0, ray, sd);
    return sd;
}

bool Occluded(RayDesc ray) {
	IncrementCounter(DEBUG_COUNTER_SHADOW_RAYS);
    ShadingData sd = TraceRay(ray, RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH);
    return all(isfinite(sd._Position));
}

// intersects a plane centered at the origin
float RayPlane(float3 origin, float3 dir, float3 normal) {
	const float denom = dot(normal, dir);
	if (abs(denom) > 0)
		return -dot(origin, normal) / denom;
	else
		return POS_INFINITY;
}

#endif