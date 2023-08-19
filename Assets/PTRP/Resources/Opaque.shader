Shader "Path Tracing/Opaque"
{
	Properties
	{
		_Color ("Color", Color) = (1, 1, 1, 1)
		_MainTex ("Albedo (RGB)", 2D) = "white" {}
		
		_Cutoff ("Alpha Cutoff", Float) = 0.5
		[NoScaleOffset]
		[Normal]
		_BumpMap ("Bump Map", 2D) = "bump" {}
		_BumpScale ("Bump Scale", Float) = 1.0
		
		[NoScaleOffset]
		_SpecGlossMap ("SpecGloss Map", 2D) = "white" {}
		[NoScaleOffset]
		_MetallicGlossMap ("MetallicGloss Map", 2D) = "white" {}

		_Metallic ("Metallic", Float) = 1.0
		_Glossiness ("Glossiness", Float) = 1.0
		_GlossMapScale ("GlossMapScale", Float) = 1.0

		[HDR]
		_EmissionColor ("Emission", Color) = (0, 0, 0, 0)
		[NoScaleOffset]
		_EmissionMap ("Emission Map", 2D) = "white" {}
	}
	SubShader
	{
		Tags { "RenderType" = "Opaque" }

		Pass
		{
			Name "PathTrace"

			HLSLPROGRAM

			#pragma raytracing surface_shader

			#include "MaterialInput.cginc"
			
			#include "IntersectionVertex.cginc"
			#include "ShadingData.cginc"

			[shader("closesthit")]
			void ClosestHit(inout ShadingData sd : SV_RayPayload, AttributeData attributeData : SV_IntersectionAttributes) {
				IntersectionVertex v;
				GetCurrentIntersectionVertex(attributeData, v, ObjectToWorld3x4());
				
				sd._Position = v.position;
				sd.GeometryNormal(v.geometryNormal);
				sd.ShadingNormal(v.normal);
				sd.Tangent(v.tangent);
				
				float2 uv = TRANSFORM_TEX(v.texCoords[0], _MainTex);

				sd.BaseColor(_Color * _MainTex.SampleLevel(sampler_MainTex, uv, 0).rgb);
				sd.Emission(_EmissionColor * _EmissionMap.SampleLevel(sampler_EmissionMap, uv, 0).rgb);
				sd.Specular      (0.5);
				sd.SpecularTint  (0.5);
				sd.Sheen         (0.5);
				sd.SheenTint     (0.5);
				sd.Metallic      (0);
				sd.Roughness     (0.5);
				sd.Anisotropic   (0.5);
				sd.Subsurface    (0);
				sd.Clearcoat     (0);
				sd.ClearcoatGloss(0.5);
				sd.Transmission  (0);
				sd.Eta           (1.5);
			}

			ENDHLSL
		}
	}
}
