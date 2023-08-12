#include "Utils.cginc"

float2 SampleUniformTriangle(const float u1, const float u2) {
    const float a = sqrt(u1);
    return float2(1 - a, a * u2);
}
float3 SampleUniformSphere(const float u1, const float u2) {
	const float z = u2 * 2 - 1;
    const float r = sqrt(max(1 - z * z, 0));
    const float phi = 2 * M_PI * u1;
	return float3(r * cos(phi), r * sin(phi), z);
}

float2 SampleConcentricDisc(const float u1, const float u2) {
	// from pbrtv3, sampling.cpp line 113

    // Map uniform random numbers to $[-1,1]^2$
    const float2 uOffset = 2 * float2(u1,u2) - 1;

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

float3 SampleCosHemisphere(const float u1, const float u2) {
    const float2 xy = SampleConcentricDisc(u1, u2);
	return float3(xy, sqrt(max(0, 1 - dot(xy,xy))));
}
float CosHemispherePdfW(const float cosTheta) {
	return max(cosTheta, 0.f) / M_PI;
}