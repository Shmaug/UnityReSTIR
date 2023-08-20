#ifndef PATHGEN_H
#define PATHGEN_H

#define SAMPLE_LIGHTS

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
    ReconnectionVertex rcv = MakeReconnectionVertex();
    float3 throughputAtRcv = 0;
    
    for (uint bounces = 0; bounces < _MaxBounces && all(isfinite(sd._Position));) {
        #ifdef SAMPLE_LIGHTS
        const LightSampleRecord lr = SampleLight(sd, -dirIn, rng.Next());
        if (any(lr._Radiance > 0)) {
            #ifdef RECONNECTION
            if (rcvFound) {
                if (rcv.IsLastVertex()) {
                    throughputAtRcv = throughput * lr._Brdf;
                    rcv.DirOut(lr._ToLight);
                    rcv.IsLastVertex(false);
                }
                rcv.Radiance(lr._Radiance * throughput / throughputAtRcv);
            }
            #endif
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
        if (rcvFound && rcv.IsLastVertex()) {
            throughputAtRcv = throughput;
            rcv.DirOut(dirIn);
            rcv.IsLastVertex(false);
        }
        // store first available reconnection vertex
        if (!rcvFound && prevDiffuse && sd.IsDiffuse()) {
            rcvFound = true;
            rcv.Radiance(0);
            rcv.PrefixBounces(bounces);
            rcv.IsLastVertex(true);
            float3 dx = prevPos - sd._Position;
            rcv._G = abs(dot(sd.ShadingNormal(), normalize(dx))) / dot(dx, dx);
            rcv._PrefixPdfW = prevPdfW;
            rcv._Position = sd._Position;
            // throughputAtRcv is assigned in the next iteration
        }
        #endif

        float3 emission = sd.Emission();
        if (any(emission > 0)) {
            #ifdef RECONNECTION
            if (rcvFound) {
                if (rcv.IsLastVertex())
                    rcv.Radiance(emission);
                else
                    rcv.Radiance(emission * throughput / throughputAtRcv);
            }
            #endif
            PathReservoir s = MakeReservoir(MakeSample(throughput * emission, pdfW, bounces, r._Sample._RngSeed, rcv));
            s.PrepareMerge();
            r.Merge(rng.NextFloat().x, s);
        } else
            rng.SkipNext();
    }
    r.FinalizeMerge();
    r._M = 1;
    return r;
}

PathSample ShiftTo(PathSample from, float4 to, float3 cameraPos, out float jacobian) {
	IncrementCounter(DEBUG_COUNTER_SHIFT_ATTEMPTS);
    
    jacobian = 0;

    #ifdef RECONNECTION
    bool hasRcv = any(from._ReconnectionVertex.Radiance() > 0);
    if (hasRcv) IncrementCounter(DEBUG_COUNTER_RECONNECTION_ATTEMPTS);
    #else
    bool hasRcv = false;
    #endif

    PathSample r = MakeReservoir()._Sample;
    
    float3 dirIn = normalize(to.xyz - cameraPos);
    ShadingData sd = TraceRay(MakeRay(cameraPos, dirIn));
    if (!all(isfinite(sd._Position)))
        return r;
    
    RandomSampler rng = from._RngSeed;
    float3 throughput = 1;
    float pdfW = 1;
    for (uint bounces = 0; bounces <= from._Bounces && all(isfinite(sd._Position));) {
        #ifdef RECONNECTION
        // reconnect to base path
        if (hasRcv) {
            if (bounces >= from._ReconnectionVertex.PrefixBounces())
                break;
            if (bounces + 1 == from._ReconnectionVertex.PrefixBounces()) {
                float3 toRcv = from._ReconnectionVertex._Position - sd._Position;
                float dist = length(toRcv);
                toRcv /= dist;

                float3 brdf = EvalBrdf(sd, -dirIn, toRcv);
                if (!any(brdf > 0))
                    break;

                ShadingData rcvSd = TraceRay(MakeRay(OffsetRayOrigin(sd._Position, sd.GeometryNormal(), toRcv), toRcv, 0, dist*1.05));

                if (!all(isfinite(rcvSd._Position.xyz)) || abs(dist - length(rcvSd._Position - sd._Position))/dist > 0.01)
                    break;
                
                float3 rcvBrdf = 1;
                if (!from._ReconnectionVertex.IsLastVertex()) {
                    rcvBrdf = EvalBrdf(rcvSd, -toRcv, from._ReconnectionVertex.DirOut());
                    if (!any(rcvBrdf > 0))
                        break;
                }

                r._ReconnectionVertex = from._ReconnectionVertex;
                r._ReconnectionVertex._G = abs(dot(rcvSd.ShadingNormal(), toRcv)) / (dist*dist);
                r._ReconnectionVertex._PrefixPdfW = pdfW;
                r = MakeSample(throughput * brdf * rcvBrdf * from._ReconnectionVertex.Radiance(), pdfW * from._PdfW / from._ReconnectionVertex._PrefixPdfW, from._Bounces, from._RngSeed, r._ReconnectionVertex);
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
    
    if (any(r._Radiance > 0) && r._PdfW > 0) {
        #ifdef RECONNECTION
        if (hasRcv) {
            jacobian = (from._ReconnectionVertex._PrefixPdfW * from._ReconnectionVertex._G) / (r._ReconnectionVertex._PrefixPdfW * r._ReconnectionVertex._G);
            if (jacobian > 0)
                IncrementCounter(DEBUG_COUNTER_RECONNECTION_SUCCESSES);
        } else
        #endif
            jacobian = from._PdfW / r._PdfW;
    }
        
    if (jacobian > 0)
	    IncrementCounter(DEBUG_COUNTER_SHIFT_SUCCESSES);

    return r;
}

PathSample ShiftTo(PathSample from, float4 to, out float jacobian) {
    float3 cameraPos = mul(unity_CameraToWorld, float4(0, 0, 0, 1)).xyz;
    return ShiftTo(from, to, cameraPos, jacobian);
}

#endif