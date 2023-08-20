#ifndef PATH_RESERVOIR_H
#define PATH_RESERVOIR_H

#define RECONNECTION

#include "Random.cginc"

// 32 bytes
struct ReconnectionVertex {
    uint2 _PackedRadiance;
    float _G;
    float _PrefixPdfW;
    float3 _Position;
    uint _PackedDirOut;
    
	float3 Radiance() {
        return float3(
            f16tof32(BF_GET(_PackedRadiance[0],  0, 16)),
            f16tof32(BF_GET(_PackedRadiance[0], 16, 16)),
            f16tof32(BF_GET(_PackedRadiance[1],  0, 16)));
    }
	void Radiance(float3 newValue) {
        BF_SET(_PackedRadiance[0], f32tof16(newValue.r),  0, 16);
        BF_SET(_PackedRadiance[0], f32tof16(newValue.g), 16, 16);
        BF_SET(_PackedRadiance[1], f32tof16(newValue.b),  0, 16);
    }
    
	uint PrefixBounces() { return BF_GET(_PackedRadiance[1], 16, 8); }
	void PrefixBounces(uint newValue) { BF_SET(_PackedRadiance[1], newValue, 16, 8); }
    
	bool IsLastVertex() { return BF_GET(_PackedRadiance[1], 31, 1); }
	void IsLastVertex(bool newValue) { BF_SET(_PackedRadiance[1], newValue, 31, 1); }
    
	float3 DirOut() { return UnpackNormal(_PackedDirOut); }
	void DirOut(float3 newValue) { _PackedDirOut = PackNormal(newValue); }
};
ReconnectionVertex MakeReconnectionVertex() {
    ReconnectionVertex r = { 0, 0, 0, 0, 0, 0, 0, 0 };
    return r;
}

// 32 bytes (64 if RECONNECTION)
struct PathSample {
    float3 _Radiance;
    float _PdfW;
    RandomSampler _RngSeed;
    uint _Bounces;
    uint pad;
    #ifdef RECONNECTION
    ReconnectionVertex _ReconnectionVertex;
    #endif
};
PathSample MakeSample(float3 radiance, float pdfW, uint bounces, RandomSampler seed, ReconnectionVertex rcv) {
    PathSample r;
    r._Radiance = radiance;
    r._PdfW = pdfW;
    r._RngSeed = seed;
    r._Bounces = bounces;
    #ifdef RECONNECTION
    r._ReconnectionVertex = rcv;
    #endif
    return r;
}
// 64 bytes (128 if RECONNECTION)
struct PathReservoir {
    PathSample _Sample;
    float _W;
    float _M;
    uint pad0;
    uint pad1;
    uint4 pad2;
    #ifdef RECONNECTION
    uint4 pad3[2]; // pad to 128 bytes
    #endif

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
PathReservoir MakeReservoir() {
    PathReservoir r = {
        0, 0, 0, 0,
        0, 0, 0, 0,
        0, 0, 0, 0,
        0, 0, 0, 0,
        #ifdef RECONNECTION
        0, 0, 0, 0,
        0, 0, 0, 0,
        0, 0, 0, 0,
        0, 0, 0, 0,
        #endif
    };
    return r;
}
PathReservoir MakeReservoir(PathSample s) {
    PathReservoir r;
    r._Sample = s;
    r._W = s._PdfW > 0 ? 1/s._PdfW : 0;
    r._M = 1;
    return r;
}


#endif