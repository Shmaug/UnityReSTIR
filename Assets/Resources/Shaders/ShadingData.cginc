#include "Utils.cginc"

struct ShadingData {
    // The data is packed as a series of 8 bit quantities:
    // { baseColor.r, baseColor.g   , baseColor.b , specular   }
    // { emission.r , emission.g    , emission.b  , sheen      }
	// { metallic   , roughness     , anisotropic , subsurface }
	// { clearcoat  , clearcoatGloss, transmission, eta        }
    uint4 _PackedData;
    float _EmissionScale;
	float3 _Position;
	uint _PackedGeometryNormal;
	uint _PackedShadingNormal;
	uint _PackedTangent;
	uint pad;

	float3 BaseColor() {
        return float3(
			BF_GET(_PackedData[0],  0, 8) / float(255),
			BF_GET(_PackedData[0],  8, 8) / float(255),
			BF_GET(_PackedData[0], 16, 8) / float(255) );
    }
    float3 Emission() {
        return _EmissionScale * float3(
            BF_GET(_PackedData[1],  0, 8) / float(255),
            BF_GET(_PackedData[1],  8, 8) / float(255),
            BF_GET(_PackedData[1], 16, 8) / float(255));
    }
    float Specular()        { return BF_GET(_PackedData[0], 24, 8) / float(255); }
    float Sheen()           { return BF_GET(_PackedData[1], 24, 8) / float(255); }
	float Metallic()        { return BF_GET(_PackedData[2],  0, 8) / float(255); }
	float Roughness()       { return BF_GET(_PackedData[2],  8, 8) / float(255); }
	float Anisotropic()     { return BF_GET(_PackedData[2], 16, 8) / float(255); }
	float Subsurface()      { return BF_GET(_PackedData[2], 24, 8) / float(255); }
	float Clearcoat()       { return BF_GET(_PackedData[3],  0, 8) / float(255); }
	float ClearcoatGloss()  { return BF_GET(_PackedData[3],  8, 8) / float(255); }
	float Transmission()    { return BF_GET(_PackedData[3], 16, 8) / float(255); }
	float Eta()             { return BF_GET(_PackedData[3], 24, 8) / float(255); }
	float3 GeometryNormal() { return UnpackNormal(_PackedGeometryNormal); }
	float3 ShadingNormal()  { return UnpackNormal(_PackedShadingNormal); }
	float3 Tangent()        { return UnpackNormal(_PackedTangent); }

    float SpecularTint() { return 0.5; }
    float SheenTint()    { return 0.5; }


    void BaseColor(const float3 newValue) {
		BF_SET(_PackedData[0], (uint)floor(saturate(newValue[0])*255 + 0.5),  0, 8);
		BF_SET(_PackedData[0], (uint)floor(saturate(newValue[1])*255 + 0.5),  8, 8);
		BF_SET(_PackedData[0], (uint)floor(saturate(newValue[2])*255 + 0.5), 16, 8);
    }
    void Emission(float3 newValue) {
        _EmissionScale = max(max(newValue.r, newValue.g), newValue.b);
        newValue /= _EmissionScale;
		BF_SET(_PackedData[1], (uint)floor(saturate(newValue[0])*255 + 0.5),  0, 8);
		BF_SET(_PackedData[1], (uint)floor(saturate(newValue[1])*255 + 0.5),  8, 8);
        BF_SET(_PackedData[1], (uint)floor(saturate(newValue[2])*255 + 0.5), 16, 8);
    }
	void Specular      (const float newValue) { BF_SET(_PackedData[0], (uint)floor(saturate(newValue)*255 + 0.5), 24, 8); }
	void Sheen         (const float newValue) { BF_SET(_PackedData[1], (uint)floor(saturate(newValue)*255 + 0.5), 24, 8); }
	void Metallic      (const float newValue) { BF_SET(_PackedData[2], (uint)floor(saturate(newValue)*255 + 0.5),  0, 8); }
	void Roughness     (const float newValue) { BF_SET(_PackedData[2], (uint)floor(saturate(newValue)*255 + 0.5),  8, 8); }
	void Anisotropic   (const float newValue) { BF_SET(_PackedData[2], (uint)floor(saturate(newValue)*255 + 0.5), 16, 8); }
	void Subsurface    (const float newValue) { BF_SET(_PackedData[2], (uint)floor(saturate(newValue)*255 + 0.5), 24, 8); }
	void Clearcoat     (const float newValue) { BF_SET(_PackedData[3], (uint)floor(saturate(newValue)*255 + 0.5),  0, 8); }
	void ClearcoatGloss(const float newValue) { BF_SET(_PackedData[3], (uint)floor(saturate(newValue)*255 + 0.5),  8, 8); }
	void Transmission  (const float newValue) { BF_SET(_PackedData[3], (uint)floor(saturate(newValue)*255 + 0.5), 16, 8); }
	void Eta           (const float newValue) { BF_SET(_PackedData[3], (uint)floor(saturate(newValue)*255 + 0.5), 24, 8); }
	void GeometryNormal(const float3 newValue) { _PackedGeometryNormal = PackNormal(newValue); }
	void ShadingNormal (const float3 newValue) { _PackedShadingNormal  = PackNormal(newValue); }
	void Tangent       (const float3 newValue) { _PackedTangent        = PackNormal(newValue); }
	
	float3 ToWorld(float3 v) {
		float3 n = ShadingNormal();
		float3 t = Tangent();
		return v.x * t + v.y * cross(n,t) + v.z * n;
	}
	float3 ToLocal(const float3 v) {
		float3 n = ShadingNormal();
		float3 t = Tangent();
		return float3(dot(v, t), dot(v, cross(n, t)), dot(v, n));
    }


	float3 Brdf(float3 dirIn, float3 dirOut) {
		float3 n = ShadingNormal();
		float cosOut = dot(dirOut, n);
		if (cosOut * dot(dirIn, n) < 0)
			return 0;
		return (BaseColor() / M_PI) * abs(cosOut);
	}
};