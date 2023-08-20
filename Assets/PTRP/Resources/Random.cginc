#ifndef RANDOM_H
#define RANDOM_H

#include "Utils.cginc"

// xxhash (https://github.com/Cyan4973/xxHash)
//   From https://www.shadertoy.com/view/Xt3cDn
uint xxhash32(const uint p) {
	const uint PRIME32_2 = 2246822519U, PRIME32_3 = 3266489917U;
	const uint PRIME32_4 = 668265263U, PRIME32_5 = 374761393U;
	uint h32 = p + PRIME32_5;
	h32 = PRIME32_4*((h32 << 17) | (h32 >> (32 - 17)));
    h32 = PRIME32_2*(h32^(h32 >> 15));
    h32 = PRIME32_3*(h32^(h32 >> 13));
    return h32^(h32 >> 16);
}


// PCG (https://www.pcg-random.org/)
uint pcg(uint v) {
	uint state = v * 747796405u + 2891336453u;
	uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
	return (word >> 22u) ^ word;
}

uint4 pcg4d(uint4 v) {
	v = v * 1664525u + 1013904223u;
	v.x += v.y * v.w;
	v.y += v.z * v.x;
	v.z += v.x * v.y;
	v.w += v.y * v.z;
	v = v ^ (v >> 16u);
	v.x += v.y * v.w;
	v.y += v.z * v.x;
	v.z += v.x * v.y;
	v.w += v.y * v.z;
	return v;
}

#define UINT_TO_FLOAT_01(u) (asfloat(0x3f800000 | ((u) >> 9)) - 1)

struct RandomSampler {
	uint2 _State;
        
    void SkipNext(const uint n = 1) {
		uint idx = BF_GET(_State[0], 0, 16);
        idx += n;
		BF_SET(_State[0], idx, 0, 16);
    }

    uint4 Next() {
        SkipNext();
        return pcg4d(uint4(
			BF_GET(_State[0],  0, 16), 
			BF_GET(_State[0], 16, 16), 
			BF_GET(_State[1],  0, 16), 
			BF_GET(_State[1], 16, 16) ) );
    }
    float4 NextFloat() {
        return UINT_TO_FLOAT_01(Next());
    }
};

RandomSampler MakeRandomSampler(uint seed, uint2 index, uint offset = 0) {
    RandomSampler s;
	s._State = 0;
	BF_SET(s._State[0], offset, 0, 16);
	BF_SET(s._State[0], seed, 16, 16);
	BF_SET(s._State[1], index.x,  0, 16);
	BF_SET(s._State[1], index.y, 16, 16);
	s._State = s.Next().xy;
    return s;
}


#endif