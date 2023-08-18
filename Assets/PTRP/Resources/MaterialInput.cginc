#include "UnityCG.cginc"

float4              _Color;
float               _Cutoff;

Texture2D<float4>   _MainTex;
SamplerState        sampler_MainTex;
float4              _MainTex_ST;

Texture2D<float4>   _BumpMap;
SamplerState        sampler_BumpMap;
float               _BumpScale;

Texture2D<float4>   _SpecGlossMap;
SamplerState        sampler_SpecGlossMap;
Texture2D<float4>   _MetallicGlossMap;
SamplerState        sampler_MetallicGlossMap;
float                _Metallic;
float               _Glossiness;
float               _GlossMapScale;

float4              _EmissionColor;
Texture2D<float4>   _EmissionMap;
SamplerState        sampler_EmissionMap;