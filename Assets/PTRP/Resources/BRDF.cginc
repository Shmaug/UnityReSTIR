#ifndef BRDF_H
#define BRDF_H

#include "ShadingData.cginc"
#include "Sampling.cginc"

float3 EvalBrdf(ShadingData sd, float3 dirIn, float3 dirOut) {
    float3 n = sd.ShadingNormal();
    float cosOut = dot(dirOut, n);
    if (sign(cosOut * dot(dirIn, n)) < 0)
        return 0;
        
    return (sd.BaseColor() / M_PI) * abs(cosOut);
}

float3 SampleBrdf(ShadingData sd, float4 rnd, float3 dirIn, out float pdfW) {
    float3 localDirOut = SampleCosHemisphere(rnd.xy);
    pdfW = CosHemispherePdfW(localDirOut.z);

    if (dot(dirIn, sd.ShadingNormal()) < 0)
        localDirOut = -localDirOut;
    return sd.ToWorld(localDirOut);
}

#endif