#ifndef PATH_RESERVOIR_H
#define PATH_RESERVOIR_H

#include "Camera.cginc"
#include "Lights.cginc"

struct ReconnectionVertex {
    float3 _Radiance;
    float _G;
    float3 _Position;
    uint _PackedDirIn;
    
	float3 DirIn() { return UnpackNormal(_PackedDirIn); }
	void DirIn(float3 newValue) { _PackedDirIn = PackNormal(newValue); }
};

struct PathSample {
    float3 _Radiance;
    uint pad;
    RandomSampler _RngSeed;
    float _PdfW;
    uint _Bounces;
    #ifdef RECONNECTION
    ReconnectionVertex _ReconnectionVertex;
    #endif
};
struct PathReservoir {
    PathSample _Sample;
    float _W;
    float _M;
    uint pad0;
    uint pad1;

    void PrepareMerge(float misWeight = 1, float jacobian = 1) {
        _W *= Luminance(_Sample._Radiance) * misWeight * jacobian;
    }
    // note: PrepareMerge must be called on both reservoirs prior to calling Merge
    bool Merge(float rnd, PathReservoir r) {
        _M += r._M;
        
        if (r._W <= 0 || isnan(r._W))
            return false;

        _W += r._W;
        if (rnd*_W < r._W) {
            _Sample = r._Sample;
            return true;
        }
        
        return false;
    }
    void FinalizeMerge() {
        float p = Luminance(_Sample._Radiance);
        if (p > 0)
            _W /= p;
    }
};
PathReservoir MakeNullReservoir() {
    PathReservoir r = {
        0, 0, 0, 0,
        0, 0, 0, 0,
        0, 0, 0, 0,
        0, 0, 0, 0,
        0, 0, 0, 0 };
    return r;
}
PathReservoir MakeReservoir(float3 radiance, float pdfW, RandomSampler seed, uint bounces) {
    PathReservoir r;
    r._Sample._Radiance = radiance;
    r._Sample._RngSeed = seed;
    r._Sample._PdfW = pdfW;
    r._Sample._Bounces = bounces;
    #ifdef RECONNECTION
    //r._Sample._ReconnectionVertex = rcv;
    #endif
    r._M = 1;
    r._W = pdfW > 0 ? 1/pdfW : 0;
    return r;
}

uint _MaxBounces;

void SampleNextVertex(inout ShadingData sd, inout float3 dirIn, inout RandomSampler rng, inout float3 throughput, inout float pdfW) {
    float3 dirOut = SampleCosHemisphere(rng.NextFloat().xy);
    pdfW *= CosHemispherePdfW(dirOut.z);

    if (pdfW <= 0) {
        pdfW = 0;
        throughput = 0;
        return;
    }

    dirOut = sd.ToWorld(dirOut);
    throughput *= sd.Brdf(-dirIn, dirOut);

    if (!any(throughput > 0)) {
        pdfW = 0;
        return;
    }

    sd = TraceRay(MakeRay(OffsetRayOrigin(sd._Position, sd.GeometryNormal(), dirOut), dirOut));
    dirIn = dirOut;
}

PathReservoir SampleRadiance(ShadingData sd, float3 dirIn, inout RandomSampler rng) {
    PathReservoir r = MakeNullReservoir();
    r._Sample._RngSeed = rng;

    float3 throughput = 1;
    float pdfW = 1;
    for (uint bounces = 0; bounces < _MaxBounces && all(isfinite(sd._Position));) {
        #ifdef SAMPLE_LIGHTS
        const LightSampleRecord lr = SampleLight(sd, -dirIn, rng.Next());
        if (any(lr._Radiance > 0)) {
            PathReservoir s = MakeReservoir(throughput * lr._Radiance, pdfW * lr._PdfW, r._Sample._RngSeed, bounces + 1);
            s.PrepareMerge();
            r.Merge(rng.NextFloat().x, s);
        } else
            rng.SkipNext();
        #endif

        SampleNextVertex(sd, dirIn, rng, throughput, pdfW);
        if (!any(throughput > 0))
            break;

        bounces++;

        float3 emission = sd.Emission();
        if (any(emission > 0)) {
            PathReservoir s = MakeReservoir(throughput * emission, pdfW, r._Sample._RngSeed, bounces);
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
    float3 cameraPos = mul(unity_CameraToWorld, float4(0, 0, 0, 1)).xyz;
    float3 dirIn = normalize(to.xyz - cameraPos);

    jacobian = 0;
    PathSample r = MakeNullReservoir()._Sample;
    ShadingData sd = TraceRay(MakeRay(cameraPos, dirIn));
    if (all(isfinite(sd._Position))) {
        RandomSampler rng = from._RngSeed;
        float3 throughput = 1;
        float pdfW = 1;
        for (uint bounces = 0; bounces <= from._Bounces && all(isfinite(sd._Position));) {
            #ifdef SAMPLE_LIGHTS
            if (bounces + 1 == from._Bounces) {
                const LightSampleRecord lr = SampleLight(sd, -dirIn, rng.Next());
                rng.SkipNext();
                if (any(lr._Radiance > 0)) {
                    r = MakeReservoir(throughput * lr._Radiance, pdfW * lr._PdfW, from._RngSeed, bounces + 1)._Sample;
                    break;
                }
            } else
                rng.SkipNext(2);
            #endif
            

            // sample direction

            SampleNextVertex(sd, dirIn, rng, throughput, pdfW);
            if (!any(throughput > 0))
                break;

            bounces++;

            if (bounces == from._Bounces) {
                float3 emission = sd.Emission();
                if (any(emission > 0)) {
                    r = MakeReservoir(throughput * emission, pdfW, from._RngSeed, bounces)._Sample;
                    break;
                }
            } else
                rng.SkipNext();
        }
    }

    if (any(r._Radiance > 0) && from._Bounces == r._Bounces && r._PdfW > 0)
        jacobian = from._PdfW / r._PdfW;
        
    if (jacobian > 0)
	    IncrementCounter(DEBUG_COUNTER_SHIFT_SUCCESSES);

    return r;
}

#endif