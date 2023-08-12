#include "ShadingData.cginc"
// Upgrade NOTE: excluded shader from OpenGL ES 2.0 because it uses non-square matrices
#pragma exclude_renderers gles
#include "Visibility.cginc"
#include "Random.cginc"
#include "Sampling.cginc"

// from Light.cs
enum LightType {
    //
    // Summary:
    //     The light is a cone-shaped spot light.
    Spot = 0,
    //
    // Summary:
    //     The light is a directional light.
    Directional = 1,
    //
    // Summary:
    //     The light is a point light.
    Point = 2,
    Area = 3,
    //
    // Summary:
    //     The light is a rectangle-shaped area light.
    Rectangle = 3,
    //
    // Summary:
    //     The light is a disc-shaped area light.
    Disc = 4,
    //
    // Summary:
    //     The light is a pyramid-shaped spot light.
    Pyramid = 5,
    //
    // Summary:
    //     The light is a box-shaped spot light.
    Box = 6,
    //
    // Summary:
    //     The light is a tube-shaped area light.
    Tube = 7
};

struct Light {
    float4x4 _LightToWorld;
    float3 _Color;
    uint _Type;
    float _Angle;
    float _Radius;
    float pad0;
    float pad1;
};

StructuredBuffer<Light> _Lights;
uint _LightCount;

struct LightSampleRecord {
    float3 _Radiance;
    float _PdfA;
    float3 _Brdf;
    float _G;
};
LightSampleRecord SampleLight(ShadingData sd, float3 dirIn, uint4 rnd) {
    Light l = _Lights[rnd.z % _LightCount];

    LightSampleRecord r;
    r._Radiance = l._Color;
    r._PdfA = 1 / (float)_LightCount;
    r._Brdf = 0;
    r._G = 0;
    
    float3 lightFwd = normalize(float3(l._LightToWorld[0][2], l._LightToWorld[1][2], l._LightToWorld[2][2]));

    float3 toLight = 0;
    float dist = POS_INFINITY;

    switch (l._Type) {
    default:
    case LightType::Directional:
        r._G = 1;
        toLight = -lightFwd;
        if (l._Angle > 0) {
            toLight = mul(SampleUniformCone(UINT_TO_FLOAT_01(rnd.xy), l._Angle), MakeOrthonormal(toLight));
            //r._PdfA *= UniformConePdfW(l._Angle);
        }
        break;
    case LightType::Point:
    case LightType::Spot: {
        float3 lightPos =  float3(l._LightToWorld[0][3], l._LightToWorld[1][3], l._LightToWorld[2][3]);
        toLight = lightPos - sd._Position;
        dist = length(toLight);
        toLight /= dist;
        r._G = 1 / (dist*dist);
        if (l._Type == LightType::Spot) {
            float cosAngle = max(0, dot(-lightFwd, toLight));
            float attenuation = cosAngle > l._Angle ? 1 : 0;
            r._Radiance *= attenuation;
            if (attenuation <= 0) {
                return r;
            }
        }
        break;
    }
    case Rectangle:
        break;
    case Disc:
        break;
    }

    r._Brdf = sd.Brdf(dirIn, toLight);

    if (any(r._Brdf > 0)) {
        if (Occluded(MakeRay(OffsetRayOrigin(sd._Position, sd.GeometryNormal(), toLight), toLight, 0, dist))) {
            r._Radiance = 0;
        }
    }

    return r;
}