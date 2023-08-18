#ifndef UTILS_H
#define UTILS_H

// https://gist.github.com/Jeff-Russ/c9b471158fa7427280e6707d9b11d7d2

/* Bit Manipulation Macros
A good article: http://www.coranac.com/documents/working-with-bits-and-bitfields/
x    is a variable that will be modified.
y    will not.
pos  is a unsigned int (usually 0 through 7) representing a single bit position where the
     right-most bit is bit 0. So 00000010 is pos 1 since the second bit is high.
bm   (bit mask) is used to specify multiple bits by having each set ON.
bf   (bit field) is similar (it is in fact used as a bit mask) but it is used to specify a
     range of neighboring bit by having them set ON.
*/
/* shifts left the '1' over pos times to create a single HIGH bit at location pos. */
#define BIT(pos) ( 1u << (pos) )

/* Set single bit at pos to '1' by generating a mask
in the proper bit location and ORing x with the mask. */
#define SET_BIT(x, pos) ( (x) |= (BIT(pos)) )
#define SET_BITS(x, bm) ( (x) |= (bm) ) // same but for multiple bits

/* Set single bit at pos to '0' by generating a mask
in the proper bit location and ORing x with the mask. */
#define UNSET_BIT(x, pos) ( (x) &= ~(BIT(pos)) )
#define UNSET_BITS(x, bm) ( (x) &= (~(bm)) ) // same but for multiple bits

/* Set single bit at pos to opposite of what is currently is by generating a mask
in the proper bit location and ORing x with the mask. */
#define FLIP_BIT(x, pos) ( (x) ^= (BIT(pos)) )
#define FLIP_BITS(x, bm) ( (x) ^= (bm) ) // same but for multiple bits

/* Return '1' if the bit value at position pos within y is '1' and '0' if it's 0 by
ANDing x with a bit mask where the bit in pos's position is '1' and '0' elsewhere and
comparing it to all 0's.  Returns '1' in least significant bit position if the value
of the bit is '1', '0' if it was '0'. */
#define CHECK_BIT(y, pos) ( ( 0u == ( (y)&(BIT(pos)) ) ) ? 0u : 1u )
#define CHECK_BITS_ANY(y, bm) ( ( (y) & (bm) ) ? 0u : 1u )
// warning: evaluates bm twice:
#define CHECK_BITS_ALL(y, bm) ( ( (bm) == ((y)&(bm)) ) ? 0u : 1u )

// These are three preparatory macros used by the following two:
#define SET_LSBITS(len) ( (1u << (len)) - 1u ) // the first len bits are '1' and the rest are '0'
#define BF_MASK(start, len) ( SET_LSBITS(len) << (start) ) // same but with offset
#define BF_PREP(y, start, len) ( ((y)&SET_LSBITS(len)) << (start) ) // Prepare a bitmask

/* Extract a bitfield of length len starting at bit start from y. */
#define BF_GET(y, start, len) ( ((y) >> (start)) & SET_LSBITS(len) )

/* Insert a new bitfield value bf into x. */
#define BF_SET(x, bf, start, len) ( x = ((x) &~ BF_MASK(start, len)) | BF_PREP(bf, start, len) )

////////////////////////////////////////////////////////////////////////////////////////////////


#include "D3DX_DXGIFormatConvert.cginc"

#define M_PI (3.1415926535897932)
#define M_1_PI (1/M_PI)

#define POS_INFINITY asfloat(0x7F800000)
#define NEG_INFINITY asfloat(0xFF800000)

float sqr(float x) { return x*x; }

float2 PackNormal2(const float3 v) {
	// Project the sphere onto the octahedron, and then onto the xy plane
	const float2 p = v.xy * (1 / (abs(v.x) + abs(v.y) + abs(v.z)));
	// Reflect the folds of the lower hemisphere over the diagonals
	return (v.z <= 0) ? ((1 - abs(p.yx)) * lerp(-1, 1, float2(p >= 0))) : p;
}
float3 UnpackNormal2(const float2 p) {
	float3 v = float3(p, 1 - dot(1, abs(p)));
	if (v.z < 0) v.xy = (1 - abs(v.yx)) * lerp(-1, 1, float2(v.xy >= 0));
	return normalize(v);
}

uint PackNormal(const float3 v) {
	return D3DX_FLOAT2_to_R16G16_SNORM(PackNormal2(v));
}
float3 UnpackNormal(const uint p) {
	return UnpackNormal2(D3DX_R16G16_SNORM_to_FLOAT2(p));
}

float3x3 MakeOrthonormal(float3 N) {
    float3x3 r;
	if (N[0] != N[1] || N[0] != N[2])
		r[0] = float3(N[2] - N[1], N[0] - N[2], N[1] - N[0]);  // (1,1,1) x N
	else
		r[0] = float3(N[2] - N[1], N[0] + N[2], -N[1] - N[0]);  // (-1,1,1) x N
	r[0] = normalize(r[0]);
	r[1] = cross(N, r[0]);
    r[2] = N;
    return r;
}

float Luminance(const float3 color) { return dot(color, float3(0.2126, 0.7152, 0.0722)); }

// stores 4 floats between 0 and 1
struct PackedUnorm4 {
	uint _Bits;
    float Get(uint index) { return BF_GET(_Bits, index*8, 8) / float(255); }
	void Set(uint index, float newValue) { BF_SET(_Bits, (uint)floor(saturate(newValue)*255 + 0.5), index*8, 8); }
};
// stores 4 floats between 0 and 1
struct PackedUnorm16 {
	uint4 _Bits;
    float Get(uint index) { return BF_GET(_Bits[index/4], (index%4)*8, 8) / float(255); }
	void Set(uint index, float newValue) { BF_SET(_Bits[index/4], (uint)floor(saturate(newValue)*255 + 0.5), (index%4)*8, 8); }
};

#endif