#ifndef PATHGEN_H
#define PATHGEN_H

#include "PathReservoir.cginc"
#include "Camera.cginc"
#include "Lights.cginc"

uint _MaxBounces;

// samples the BRDF, modifies throughput and pdfW, traces ray, updates sd and dirIn
void SampleNextVertex(
    inout ShadingData sd,
    inout float3 dirIn,
    inout RandomSampler rng,
    inout float3 throughput,
    inout float pdfW,
    inout uint bounces) {

    float dirPdfW;
    float3 dirOut = SampleBrdf(sd, rng.NextFloat(), -dirIn, dirPdfW);

    if (dirPdfW <= 0) {
        throughput = 0;
        pdfW = 0;
        return;
    }

    pdfW *= dirPdfW;
    throughput *= EvalBrdf(sd, -dirIn, dirOut);

    if (!any(throughput > 0)) {
        throughput = 0;
        pdfW = 0;
        return;
    }

    sd = TraceRay(MakeRay(OffsetRayOrigin(sd._Position, sd.GeometryNormal(), dirOut), dirOut));
    dirIn = dirOut;
    bounces++;
}

PathReservoir SampleRadiance(ShadingData sd, float3 dirIn, inout RandomSampler rng) {
    PathReservoir r = MakeReservoir();
    r._Sample._RngSeed = rng;

    float3 throughput = 1;
    float pdfW = 1;

    bool rcvFound = false;
    float3 rcvPos = 0;
    float rcvG = 0;
    uint rcvPrefixBounces = 0;
    float3 throughputAtRcv = 0;
    float pdfAtRcv = 0;
    
    for (uint bounces = 0; bounces < _MaxBounces && all(isfinite(sd._Position));) {
        #ifdef SAMPLE_LIGHTS
        const LightSampleRecord lr = SampleLight(sd, -dirIn, rng.Next());
        if (any(lr._Radiance > 0)) {
            PathReservoir s = MakeReservoir(MakeSample(throughput * lr._Radiance, pdfW * lr._PdfW, bounces + 1, r._Sample._RngSeed, MakeReconnectionVertex()));
            s.PrepareMerge();
            r.Merge(rng.NextFloat().x, s);
        } else
            rng.SkipNext();
        #endif

        bool prevDiffuse = sd.IsDiffuse();
        float3 prevPos = sd._Position;
        float prevPdfW = pdfW;

        SampleNextVertex(sd, dirIn, rng, throughput, pdfW, bounces);
        if (!any(throughput > 0))
            break;

        #ifdef RECONNECTION
        if (!rcvFound && prevDiffuse && sd.IsDiffuse()) {
            // store first available reconnection vertex
            rcvFound = true;
            rcvPos = sd._Position;
            float dx = prevPos - sd._Position;
            rcvG = abs(dot(sd.ShadingNormal(), dirOut)) / dot(dx, dx);
            rcvPrefixBounces = bounces;
            pdfAtRcv = prevPdfW;
        }
        #endif

        float3 emission = sd.Emission();
        if (any(emission > 0)) {
            PathReservoir s = MakeReservoir(MakeSample(throughput * emission, pdfW, bounces, r._Sample._RngSeed, MakeReconnectionVertex()));
            s.PrepareMerge();
            r.Merge(rng.NextFloat().x, s);
        } else
            rng.SkipNext();
    }
    r.FinalizeMerge();
    r._M = 1;
    return r;
}

PathSample ShiftTo(PathSample from, float4 to, out float jacobian) {
	IncrementCounter(DEBUG_COUNTER_SHIFT_ATTEMPTS);
    
    jacobian = 0;

    #ifdef RECONNECTION
    bool hasRcv = any(from._ReconnectionVertex.Radiance() > 0);
    #else
    bool hasRcv = false;
    #endif

    PathSample r = MakeReservoir()._Sample;
    
    float3 cameraPos = mul(unity_CameraToWorld, float4(0, 0, 0, 1)).xyz;
    float3 dirIn = normalize(to.xyz - cameraPos);
    ShadingData sd = TraceRay(MakeRay(cameraPos, dirIn));
    if (all(isfinite(sd._Position))) {
        RandomSampler rng = from._RngSeed;
        float3 throughput = 1;
        float pdfW = 1;
        for (uint bounces = 0; bounces <= from._Bounces && all(isfinite(sd._Position));) {
            #ifdef RECONNECTION
            // reconnect to base path
            if (hasRcv) {
                if (bounces + 1 == from._ReconnectionVertex.PrefixBounces()) {
                    // TODO: connect to from._ReconnectionVertex, call MakeSample
                    break;
                } else if (bounces + 1 > from._ReconnectionVertex.PrefixBounces()) {
                    break;
                }
            }
            #endif

            #ifdef SAMPLE_LIGHTS
            if (!hasRcv && bounces + 1 == from._Bounces) {
                const LightSampleRecord lr = SampleLight(sd, -dirIn, rng.Next());
                rng.SkipNext();
                if (any(lr._Radiance > 0)) {
                    r = MakeSample(throughput * lr._Radiance, pdfW * lr._PdfW, bounces + 1, from._RngSeed, MakeReconnectionVertex());
                    break;
                }
            } else
                rng.SkipNext(2);
            #endif
            
            // sample direction

            SampleNextVertex(sd, dirIn, rng, throughput, pdfW, bounces);
            if (!any(throughput > 0))
                break;

            if (!hasRcv && bounces == from._Bounces) {
                float3 emission = sd.Emission();
                if (any(emission > 0)) {
                    r = MakeSample(throughput * emission, pdfW, bounces, from._RngSeed, MakeReconnectionVertex());
                    break;
                }
            } else
                rng.SkipNext();
        }
    }

    if (any(r._Radiance > 0) && r._PdfW > 0)
        jacobian = from._PdfW / r._PdfW;
        
    if (jacobian > 0)
	    IncrementCounter(DEBUG_COUNTER_SHIFT_SUCCESSES);

    return r;
}

#endif