#ifndef SAMPLING_H
#define SAMPLING_H

#include "Utils.cginc"

float2 SampleUniformTriangle(float2 rnd) {
    const float a = sqrt(rnd.x);
    return float2(1 - a, a * rnd.y);
}
float3 SampleUniformSphere(float2 rnd) {
	const float z = rnd.y * 2 - 1;
    const float r = sqrt(max(1 - z * z, 0));
    const float phi = 2 * M_PI * rnd.x;
	return float3(r * cos(phi), r * sin(phi), z);
}

float2 SampleConcentricDisc(float2 rnd) {
	// from pbrtv3, sampling.cpp line 113

    // Map uniform random numbers to $[-1,1]^2$
    const float2 uOffset = 2 * float2(rnd.x,rnd.y) - 1;

    // Handle degeneracy at the origin
    if (uOffset.x == 0 && uOffset.y == 0) return 0;

    // Apply concentric mapping to point
    float theta, r;
    if (abs(uOffset.x) > abs(uOffset.y)) {
        r = uOffset.x;
        theta = M_PI/4 * (uOffset.y / uOffset.x);
    } else {
        r = uOffset.y;
        theta = M_PI/2 - M_PI/4 * (uOffset.x / uOffset.y);
    }
    return r * float2(cos(theta), sin(theta));
}
float ConcentricDiscPdfA() {
    return 1.0 / M_PI;
}

float3 SampleCosHemisphere(float2 rnd) {
    const float2 xy = SampleConcentricDisc(rnd);
	return float3(xy, sqrt(max(0, 1 - dot(xy, xy))));
}
float CosHemispherePdfW(const float cosTheta) {
	return max(cosTheta, 0.f) / M_PI;
}

float3 SampleUniformCone(float2 rnd, float cosConeAngle) {
    float cosElevation = (1 - rnd.x) + rnd.x * cosConeAngle;
    float sinElevation = sqrt(max(0, 1 - cosElevation * cosElevation));
    float azimuth = rnd.y * 2 * M_PI;
    return float3(
        cos(azimuth)*sinElevation,
        sin(azimuth)*sinElevation,
        cosElevation);
}
float UniformConePdfW(float cosConeAngle) {
    return 1 / (2 * M_PI * (1 - cosConeAngle));
}

#endif