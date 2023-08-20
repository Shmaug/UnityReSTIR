#ifndef LIGHT_H
#define LIGHT_H

#include "BRDF.cginc"
#include "Visibility.cginc"
#include "Random.cginc"

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
    float _PdfW;
    float3 _ToLight;
    float3 _Brdf;
};
LightSampleRecord SampleLight(ShadingData sd, float3 dirIn, uint4 rnd) {
    LightSampleRecord r = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
    if (_LightCount == 0) {
        return r;
    }
    Light l = _Lights[rnd.z % _LightCount];

    r._Radiance = l._Color;
    r._PdfW = 1 / (float)_LightCount;
    
    float3 lightFwd = normalize(float3(l._LightToWorld[0][2], l._LightToWorld[1][2], l._LightToWorld[2][2]));

    r._ToLight = 0;
    float dist = POS_INFINITY;

    switch (l._Type) {
    default:
    case LightType::Directional:
        r._ToLight = -lightFwd;
        if (l._Angle > 0) {
            r._ToLight = mul(SampleUniformCone(UINT_TO_FLOAT_01(rnd.xy), l._Angle), MakeOrthonormal(r._ToLight));
            //r._PdfW *= UniformConePdfW(l._Angle);
        }
        break;
    case LightType::Point:
    case LightType::Spot: {
        float3 lightPos =  float3(l._LightToWorld[0][3], l._LightToWorld[1][3], l._LightToWorld[2][3]);
        r._ToLight = lightPos - sd._Position;
        dist = length(r._ToLight);
        r._ToLight /= dist;

        r._PdfW *= dist*dist;

        if (l._Type == LightType::Spot) {
            float cosAngle = max(0, dot(-lightFwd, r._ToLight));
            if (cosAngle < l._Angle)
                r._Radiance = 0;
        }
        break;
    }
    }

    if (!any(r._Radiance > 0))
        return r;

    r._Brdf = EvalBrdf(sd, dirIn, r._ToLight);
    r._Radiance *= r._Brdf;

    if (!any(r._Radiance > 0))
        return r;

    if (Occluded(MakeRay(OffsetRayOrigin(sd._Position, sd.GeometryNormal(), r._ToLight), r._ToLight, 0, dist)))
        r._Radiance = 0;

    return r;
}

#endif