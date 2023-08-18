#include "UnityRaytracingMeshUtils.cginc"
// Upgrade NOTE: excluded shader from OpenGL ES 2.0 because it uses non-square matrices
#pragma exclude_renderers gles

struct AttributeData {
	float2 barycentrics;
};

#define INTERPOLATE_RAYTRACING_ATTRIBUTE(verts, attrib, bary) (verts[0].attrib + (verts[1].attrib - verts[0].attrib) * bary.x + (verts[2].attrib - verts[0].attrib) * bary.y)

struct IntersectionVertex {
	float3 position;
	float3 geometryNormal;
	float triangleArea;
	float3 normal;
	float3 tangent;
	float4 color;
	float2 texCoords[4];
};
void FetchIntersectionVertex(uint vertexIndex, out IntersectionVertex outVertex) {
	outVertex.position     = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributePosition);
	outVertex.normal       = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributeNormal);
	outVertex.tangent      = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributeTangent);
	outVertex.color        = UnityRayTracingFetchVertexAttribute4(vertexIndex, kVertexAttributeColor);
	outVertex.texCoords[0] = UnityRayTracingFetchVertexAttribute2(vertexIndex, kVertexAttributeTexCoord0);
	outVertex.texCoords[1] = UnityRayTracingFetchVertexAttribute2(vertexIndex, kVertexAttributeTexCoord1);
	outVertex.texCoords[2] = UnityRayTracingFetchVertexAttribute2(vertexIndex, kVertexAttributeTexCoord2);
	outVertex.texCoords[3] = UnityRayTracingFetchVertexAttribute2(vertexIndex, kVertexAttributeTexCoord3);
}
void GetCurrentIntersectionVertex(AttributeData attributeData, out IntersectionVertex outVertex, float3x4 objectToWorld) {
	uint3 triangleIndices = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());

	IntersectionVertex verts[3];
	FetchIntersectionVertex(triangleIndices.x, verts[0]);
	FetchIntersectionVertex(triangleIndices.y, verts[1]);
	FetchIntersectionVertex(triangleIndices.z, verts[2]);

	for (int i = 0; i < 3; i++) verts[i].position = mul(objectToWorld, float4(verts[i].position, 1));
	for (int i = 0; i < 3; i++) verts[i].normal   = mul(objectToWorld, float4(verts[i].normal, 0));
	for (int i = 0; i < 3; i++) verts[i].tangent  = mul(objectToWorld, float4(verts[i].tangent, 0));

	outVertex.geometryNormal = cross(verts[1].position - verts[0].position, verts[2].position - verts[0].position);
	outVertex.triangleArea  = length(outVertex.geometryNormal);
	outVertex.geometryNormal /= outVertex.triangleArea;

	outVertex.position = INTERPOLATE_RAYTRACING_ATTRIBUTE(verts, position, attributeData.barycentrics);
	outVertex.normal   = INTERPOLATE_RAYTRACING_ATTRIBUTE(verts, normal  , attributeData.barycentrics);
	outVertex.tangent  = INTERPOLATE_RAYTRACING_ATTRIBUTE(verts, tangent , attributeData.barycentrics);
	outVertex.color    = INTERPOLATE_RAYTRACING_ATTRIBUTE(verts, color   , attributeData.barycentrics);
	for (int i = 0; i < 4; i++)
		outVertex.texCoords[i] = INTERPOLATE_RAYTRACING_ATTRIBUTE(verts, texCoords[i], attributeData.barycentrics);
	
	outVertex.normal  = normalize(outVertex.normal);
	outVertex.tangent = normalize(outVertex.tangent);
	outVertex.tangent -= outVertex.normal * dot(outVertex.normal, outVertex.tangent);
}
