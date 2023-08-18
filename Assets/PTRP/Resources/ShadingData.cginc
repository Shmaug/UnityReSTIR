#ifndef SHADING_DATA_H
#define SHADING_DATA_H

#include "Utils.cginc"

struct ShadingData {
    PackedUnorm16 _PackedData;
	float3 _Position;
    float _EmissionScale;
	uint _PackedGeometryNormal;
	uint _PackedShadingNormal;
	uint _PackedTangent;
	PackedUnorm4 _PackedData2;

	float3 BaseColor() { return float3(_PackedData.Get(0), _PackedData.Get(1), _PackedData.Get(2) ); }
    float3 Emission() { return _EmissionScale * float3(_PackedData.Get(3), _PackedData.Get(4), _PackedData.Get(5)); }
    float Specular()        { return _PackedData.Get( 6); }
    float Sheen()           { return _PackedData.Get( 7); }
	float Metallic()        { return _PackedData.Get( 8); }
	float Roughness()       { return _PackedData.Get( 9); }
	float Anisotropic()     { return _PackedData.Get(10); }
	float Subsurface()      { return _PackedData.Get(11); }
	float Clearcoat()       { return _PackedData.Get(12); }
	float ClearcoatGloss()  { return _PackedData.Get(13); }
	float Transmission()    { return _PackedData.Get(14); }
	float Eta()             { return _PackedData.Get(15)*2; }
    float SpecularTint()    { return _PackedData2.Get(0); }
    float SheenTint()       { return _PackedData2.Get(1); }
	float3 GeometryNormal() { return UnpackNormal(_PackedGeometryNormal); }
	float3 ShadingNormal()  { return UnpackNormal(_PackedShadingNormal); }
	float3 Tangent()        { return UnpackNormal(_PackedTangent); }

    void BaseColor(const float3 newValue) {
		_PackedData.Set(0, newValue[0]);
		_PackedData.Set(1, newValue[1]);
		_PackedData.Set(2, newValue[2]);
    }
    void Emission(float3 newValue) {
        _EmissionScale = max(max(newValue.r, newValue.g), newValue.b);
        newValue /= _EmissionScale;
		_PackedData.Set(3, newValue[0]);
		_PackedData.Set(4, newValue[1]);
        _PackedData.Set(5, newValue[2]);
    }
	void Specular      (float newValue) { _PackedData.Set( 6, newValue); }
	void Sheen         (float newValue) { _PackedData.Set( 7, newValue); }
	void Metallic      (float newValue) { _PackedData.Set( 8, newValue); }
	void Roughness     (float newValue) { _PackedData.Set( 9, newValue); }
	void Anisotropic   (float newValue) { _PackedData.Set(10, newValue); }
	void Subsurface    (float newValue) { _PackedData.Set(11, newValue); }
	void Clearcoat     (float newValue) { _PackedData.Set(12, newValue); }
	void ClearcoatGloss(float newValue) { _PackedData.Set(13, newValue); }
	void Transmission  (float newValue) { _PackedData.Set(14, newValue); }
	void Eta           (float newValue) { _PackedData.Set(15, newValue/2); }
    void SpecularTint  (float newValue) { _PackedData2.Set(0, newValue); }
    void SheenTint     (float newValue) { _PackedData2.Set(1, newValue); }
	void GeometryNormal(float3 newValue) { _PackedGeometryNormal = PackNormal(newValue); }
	void ShadingNormal (float3 newValue) { _PackedShadingNormal  = PackNormal(newValue); }
	void Tangent       (float3 newValue) { _PackedTangent        = PackNormal(newValue); }
	
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

	bool IsDiffuse() {
		return true;//Metallic() < 0.99 || Roughness() > 0.01 || any(Emission() > 0);
	}
};

#endif