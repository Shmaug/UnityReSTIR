#include "ShadingData.cginc"
#include "Visibility.cginc"

enum LightType {
    Directional,
    Point,
    Cone,
};

struct Light {
    float4x4 _LightToWorld;
    float3 _Color;
    uint _Type;
    float _Range;
    float _InnerSpotAngle; // also _ShadowAngle for directional lights
    float _OuterSpotAngle;
    float pad;
};

StructuredBuffer<Light> _Lights;
uint _LightCount;

Light GetLight(uint rng) {
    return _Lights[rng % _LightCount];
}

float3 SampleRadiance(ShadingData sd, Light l, float3 dirIn) {
    float3 toLight = normalize(mul(l._LightToWorld, float4(0,0,-1,0)).xyz);

    float G = 1;

    if (Occluded(MakeRay(OffsetRayOrigin(sd._Position, sd.GeometryNormal(), toLight), toLight)))
        return 0;

    return l._Color * sd.Brdf(dirIn, toLight) * G;
}