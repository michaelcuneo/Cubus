Shader "Cubus/DensityBiomeURP"
{
  Properties
  {
    _Tint("Tint", Color) = (1, 1, 1, 1)
    _TextureScale("Global Texture Scale", Float) = 0.25
    _TriplanarSharpness("Triplanar Sharpness", Float) = 4.0
    _MaterialBlendWidth("Material Blend Width", Range(0.01, 1)) = 0.35
    _MaterialBlendNoiseScale("Material Blend Noise Scale", Float) = 0.14
    _MaterialBlendNoiseStrength("Material Blend Noise Strength", Range(0, 1)) = 0.45
    _AlbedoBrightness("Albedo Brightness", Range(0.25, 3)) = 1.35
    _NormalStrength("Normal Strength", Range(0, 3)) = 1.25
    _Smoothness("Smoothness", Range(0, 1)) = 0.04
    _SpecularStrength("Specular Strength", Range(0, 2)) = 0.06
    _FresnelStrength("Fresnel Rim", Range(0, 2)) = 0.03
    _AmbientStrength("Ambient Strength", Range(0, 2)) = 0.85
    _DirectLightStrength("Direct Sun Strength", Range(0, 3)) = 1.35
    _ShadowStrength("Shadow Strength", Range(0, 1)) = 0.45
    _UseSceneFog("Use Scene Fog", Range(0, 1)) = 0
    _DebugMode("Debug Mode: 0 Final, 1 Albedo, 2 Mesh Normal, 3 Material Normal, 4 Packed Mask, 5 Rough/Smooth/AO, 6 Weights", Range(0, 6)) = 0
  }

  SubShader
  {
    Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
    LOD 100
    Pass
    {
      Name "ForwardLit"
      Tags { "LightMode" = "UniversalForward" }
      Cull Back
      ZWrite On
      ZTest LEqual
      Blend One Zero

      HLSLPROGRAM
      #pragma vertex vert
      #pragma fragment frag
      #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
      #pragma multi_compile_fragment _ _SHADOWS_SOFT
      #pragma multi_compile_fog

      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

      TEXTURE2D_ARRAY(_TerrainAlbedoArray); SAMPLER(sampler_TerrainAlbedoArray);
      TEXTURE2D_ARRAY(_TerrainNormalArray); SAMPLER(sampler_TerrainNormalArray);
      TEXTURE2D_ARRAY(_TerrainMaskArray); SAMPLER(sampler_TerrainMaskArray);
      TEXTURE2D(_TerrainMaterialSliceLookup); SAMPLER(sampler_TerrainMaterialSliceLookup);

      struct Attributes
      {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
        float4 color : COLOR;
        float2 uv0 : TEXCOORD0;
        float2 uv1 : TEXCOORD1;
      };

      struct Varyings
      {
        float4 positionCS : SV_POSITION;
        float3 positionWS : TEXCOORD0;
        float3 normalWS : TEXCOORD1;
        float4 splatWeights : TEXCOORD2;
        float2 materialIds01 : TEXCOORD3;
        float2 materialIds23 : TEXCOORD4;
        float fogCoord : TEXCOORD5;
      };

      CBUFFER_START(UnityPerMaterial)
      float4 _Tint;
      float _TextureScale;
      float _TriplanarSharpness;
      float _MaterialBlendWidth;
      float _MaterialBlendNoiseScale;
      float _MaterialBlendNoiseStrength;
      float _AlbedoBrightness;
      float _NormalStrength;
      float _Smoothness;
      float _SpecularStrength;
      float _FresnelStrength;
      float _AmbientStrength;
      float _DirectLightStrength;
      float _ShadowStrength;
      float _UseSceneFog;
      float _DebugMode;
      int _TerrainMaterialCount;
      float4 _TerrainMaterialParams[128];
      CBUFFER_END

      float MaterialIdToSlice(float materialId)
      {
        float id = clamp(round(materialId), 1.0, 65535.0);
        float x = fmod(id, 256.0);
        float y = floor(id / 256.0);
        float2 uv = (float2(x, y) + 0.5) / 256.0;
        half4 encoded = SAMPLE_TEXTURE2D_LOD(_TerrainMaterialSliceLookup, sampler_TerrainMaterialSliceLookup, uv, 0.0);
        float encodedSlice = round(encoded.r * 255.0) + round(encoded.g * 255.0) * 256.0;
        if (encodedSlice <= 0.5) return 0.0;
        float count = max(1.0, (float)_TerrainMaterialCount);
        return clamp(encodedSlice - 1.0, 0.0, count - 1.0);
      }

      float4 GetTerrainMaterialParams(float slice)
      {
        int index = clamp((int)round(slice), 0, 127);
        return _TerrainMaterialParams[index];
      }

      half4 SampleTerrainAlbedoArray(float slice, float2 uv, float2 gradX, float2 gradY) { return SAMPLE_TEXTURE2D_ARRAY_GRAD(_TerrainAlbedoArray, sampler_TerrainAlbedoArray, uv, slice, gradX, gradY); }
      half4 SampleTerrainNormalArray(float slice, float2 uv, float2 gradX, float2 gradY) { return SAMPLE_TEXTURE2D_ARRAY_GRAD(_TerrainNormalArray, sampler_TerrainNormalArray, uv, slice, gradX, gradY); }
      half4 SampleTerrainMaskArray(float slice, float2 uv, float2 gradX, float2 gradY) { return SAMPLE_TEXTURE2D_ARRAY_GRAD(_TerrainMaskArray, sampler_TerrainMaskArray, uv, slice, gradX, gradY); }

      float3 TriplanarWeights(float3 normalWS)
      {
        float3 n = abs(normalize(normalWS));
        n = pow(n, max(0.001, _TriplanarSharpness));
        return n / max(n.x + n.y + n.z, 0.0001);
      }

      float3 UnpackTerrainNormal(half4 sampleValue)
      {
        float2 xy = sampleValue.rg * 2.0 - 1.0;
        xy = clamp(xy, -0.999, 0.999);
        float z = sqrt(saturate(1.0 - dot(xy, xy)));
        return normalize(float3(xy, z));
      }

      float3 AxisNormalX(float3 tangentNormal, float3 normalWS)
      {
        float s = normalWS.x < 0.0 ? -1.0 : 1.0;
        return normalize(float3(tangentNormal.z * s, tangentNormal.y, tangentNormal.x * s));
      }

      float3 AxisNormalY(float3 tangentNormal, float3 normalWS)
      {
        float s = normalWS.y < 0.0 ? -1.0 : 1.0;
        return normalize(float3(tangentNormal.x, tangentNormal.z * s, tangentNormal.y * s));
      }

      float3 AxisNormalZ(float3 tangentNormal, float3 normalWS)
      {
        float s = normalWS.z < 0.0 ? -1.0 : 1.0;
        return normalize(float3(tangentNormal.x * s, tangentNormal.y, tangentNormal.z * s));
      }

      void BuildTriplanarCoordinates(float3 positionWS, float3 normalWS, float tiling, out float3 weights, out float2 uvX, out float2 uvY, out float2 uvZ)
      {
        weights = TriplanarWeights(normalWS);
        float scale = max(0.0001, _TextureScale) * max(0.0001, tiling);
        uvX = positionWS.zy * scale;
        uvY = positionWS.xz * scale;
        uvZ = positionWS.xy * scale;
      }

      half4 SampleTriplanarAlbedo(float materialId, float3 positionWS, float3 normalWS)
      {
        float slice = MaterialIdToSlice(materialId);
        float4 materialParams = GetTerrainMaterialParams(slice);
        float3 weights; float2 uvX; float2 uvY; float2 uvZ;
        BuildTriplanarCoordinates(positionWS, normalWS, materialParams.x, weights, uvX, uvY, uvZ);
        return SampleTerrainAlbedoArray(slice, uvX, ddx(uvX), ddy(uvX)) * weights.x + SampleTerrainAlbedoArray(slice, uvY, ddx(uvY), ddy(uvY)) * weights.y + SampleTerrainAlbedoArray(slice, uvZ, ddx(uvZ), ddy(uvZ)) * weights.z;
      }

      half4 SampleTriplanarMask(float materialId, float3 positionWS, float3 normalWS)
      {
        float slice = MaterialIdToSlice(materialId);
        float4 materialParams = GetTerrainMaterialParams(slice);
        float3 weights; float2 uvX; float2 uvY; float2 uvZ;
        BuildTriplanarCoordinates(positionWS, normalWS, materialParams.x, weights, uvX, uvY, uvZ);
        return SampleTerrainMaskArray(slice, uvX, ddx(uvX), ddy(uvX)) * weights.x + SampleTerrainMaskArray(slice, uvY, ddx(uvY), ddy(uvY)) * weights.y + SampleTerrainMaskArray(slice, uvZ, ddx(uvZ), ddy(uvZ)) * weights.z;
      }

      float3 SampleTriplanarNormal(float materialId, float3 positionWS, float3 normalWS)
      {
        float slice = MaterialIdToSlice(materialId);
        float4 materialParams = GetTerrainMaterialParams(slice);
        float3 weights; float2 uvX; float2 uvY; float2 uvZ;
        BuildTriplanarCoordinates(positionWS, normalWS, materialParams.x, weights, uvX, uvY, uvZ);
        float3 tangentX = UnpackTerrainNormal(SampleTerrainNormalArray(slice, uvX, ddx(uvX), ddy(uvX)));
        float3 tangentY = UnpackTerrainNormal(SampleTerrainNormalArray(slice, uvY, ddx(uvY), ddy(uvY)));
        float3 tangentZ = UnpackTerrainNormal(SampleTerrainNormalArray(slice, uvZ, ddx(uvZ), ddy(uvZ)));
        float3 detailNormal = normalize(AxisNormalX(tangentX, normalWS) * weights.x + AxisNormalY(tangentY, normalWS) * weights.y + AxisNormalZ(tangentZ, normalWS) * weights.z);
        return normalize(lerp(normalize(normalWS), detailNormal, saturate(saturate(materialParams.y) * _NormalStrength)));
      }

      float GetMaterialRoughnessFallback(float materialId)
      {
        return saturate(GetTerrainMaterialParams(MaterialIdToSlice(materialId)).z);
      }

      float Hash31(float3 p)
      {
        p = frac(p * 0.1031);
        p += dot(p, p.yzx + 33.33);
        return frac((p.x + p.y) * p.z);
      }

      float ValueNoise(float3 p)
      {
        float3 i = floor(p);
        float3 f = frac(p);
        f = f * f * (3.0 - 2.0 * f);
        float n000 = Hash31(i + float3(0, 0, 0));
        float n100 = Hash31(i + float3(1, 0, 0));
        float n010 = Hash31(i + float3(0, 1, 0));
        float n110 = Hash31(i + float3(1, 1, 0));
        float n001 = Hash31(i + float3(0, 0, 1));
        float n101 = Hash31(i + float3(1, 0, 1));
        float n011 = Hash31(i + float3(0, 1, 1));
        float n111 = Hash31(i + float3(1, 1, 1));
        float nx00 = lerp(n000, n100, f.x);
        float nx10 = lerp(n010, n110, f.x);
        float nx01 = lerp(n001, n101, f.x);
        float nx11 = lerp(n011, n111, f.x);
        return lerp(lerp(nx00, nx10, f.y), lerp(nx01, nx11, f.y), f.z);
      }

      float4 NormalizeSplatWeights(float4 weights)
      {
        weights = max(weights, 0.0);
        float total = weights.x + weights.y + weights.z + weights.w;
        if (total <= 0.0001) return float4(1.0, 0.0, 0.0, 0.0);
        return weights / total;
      }

      float4 BreakupSplatWeights(float4 weights, float4 activeMask, float3 positionWS)
      {
        weights = NormalizeSplatWeights(weights * activeMask);
        float noiseScale = max(0.0001, _MaterialBlendNoiseScale);
        float n1 = ValueNoise(positionWS * noiseScale);
        float n2 = ValueNoise(positionWS * noiseScale * 2.37 + 19.17);
        float n3 = ValueNoise(positionWS * noiseScale * 4.71 - 7.93);
        float3 noise = float3(n1, n2, n3) - 0.5;
        float transitionAmount = 1.0 - saturate(max(max(weights.x, weights.y), max(weights.z, weights.w)));
        float strength = _MaterialBlendNoiseStrength * _MaterialBlendWidth * transitionAmount;
        weights.x += noise.x * strength;
        weights.y += noise.y * strength;
        weights.z += noise.z * strength;
        weights.w -= (noise.x + noise.y + noise.z) * 0.3333333 * strength;
        return NormalizeSplatWeights(max(weights * activeMask, 0.0));
      }

      float3 ApplyLighting(float3 albedoRgb, float3 positionWS, float3 normalWS, float roughness)
      {
        float3 n = normalize(normalWS);
        float3 viewDir = SafeNormalize(_WorldSpaceCameraPos - positionWS);
        float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
        Light mainLight = GetMainLight(shadowCoord);
        float atten = lerp(1.0, mainLight.shadowAttenuation * mainLight.distanceAttenuation, saturate(_ShadowStrength));
        float ndl = saturate(dot(n, mainLight.direction));
        float3 direct = mainLight.color * saturate(ndl * 0.85 + 0.15) * atten * _DirectLightStrength;
        float3 skyAmbient = max(SampleSH(n), 0.20.xxx) * _AmbientStrength;
        float terrainRoughness = saturate(max(roughness, 0.55));
        float smoothness = saturate(_Smoothness) * saturate(1.0 - terrainRoughness) * 0.35;
        float3 halfVec = SafeNormalize(mainLight.direction + viewDir);
        float specTerm = pow(saturate(dot(n, halfVec)), exp2(lerp(2.0, 8.0, smoothness))) * _SpecularStrength * 0.35 * ndl * atten;
        float3 specular = mainLight.color * specTerm;
        float3 rim = skyAmbient * pow(1.0 - saturate(dot(n, viewDir)), 5.0) * _FresnelStrength * 0.35;
        return albedoRgb * (max(skyAmbient, 0.18.xxx) + direct) + specular + rim;
      }

      float3 DebugModeColor(float debugMode, float3 baseColor, float3 meshNormalWS, float3 materialNormalWS, half4 packedMask, float roughness, float smoothness, float ambientOcclusion, float4 weights)
      {
        int mode = (int)round(debugMode);
        if (mode == 1) return baseColor;
        if (mode == 2) return normalize(meshNormalWS) * 0.5 + 0.5;
        if (mode == 3) return normalize(materialNormalWS) * 0.5 + 0.5;
        if (mode == 4) return float3(saturate(packedMask.r), saturate(packedMask.g), saturate(packedMask.a));
        if (mode == 5) return float3(saturate(roughness), saturate(smoothness), saturate(ambientOcclusion));
        if (mode == 6) return saturate(weights.rgb + weights.www * 0.3333333);
        return baseColor;
      }

      Varyings vert(Attributes IN)
      {
        Varyings OUT;
        VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
        VertexNormalInputs nrm = GetVertexNormalInputs(IN.normalOS);
        OUT.positionCS = pos.positionCS;
        OUT.positionWS = pos.positionWS;
        OUT.normalWS = nrm.normalWS;
        OUT.splatWeights = IN.color;
        OUT.materialIds01 = IN.uv0;
        OUT.materialIds23 = IN.uv1;
        OUT.fogCoord = ComputeFogFactor(pos.positionCS.z);
        return OUT;
      }

      half4 frag(Varyings IN) : SV_Target
      {
        float oldId = round(IN.splatWeights.r * 255.0) + round(IN.splatWeights.g * 255.0) * 256.0;
        bool oldLayout = IN.splatWeights.a > 0.99 && oldId > 0.5;
        float material0 = oldLayout ? oldId : round(IN.materialIds01.x);
        float material1 = oldLayout ? 0.0 : round(IN.materialIds01.y);
        float material2 = oldLayout ? 0.0 : round(IN.materialIds23.x);
        float material3 = oldLayout ? 0.0 : round(IN.materialIds23.y);
        float4 activeMask = float4(material0 > 0.5 ? 1.0 : 0.0, material1 > 0.5 ? 1.0 : 0.0, material2 > 0.5 ? 1.0 : 0.0, material3 > 0.5 ? 1.0 : 0.0);
        material0 = max(1.0, material0);
        material1 = max(1.0, material1);
        material2 = max(1.0, material2);
        material3 = max(1.0, material3);
        float4 weights = oldLayout ? float4(1.0, 0.0, 0.0, 0.0) : BreakupSplatWeights(IN.splatWeights, activeMask, IN.positionWS);
        float3 meshNormalWS = normalize(IN.normalWS);
        half4 albedo0 = SampleTriplanarAlbedo(material0, IN.positionWS, meshNormalWS);
        half4 albedo1 = SampleTriplanarAlbedo(material1, IN.positionWS, meshNormalWS);
        half4 albedo2 = SampleTriplanarAlbedo(material2, IN.positionWS, meshNormalWS);
        half4 albedo3 = SampleTriplanarAlbedo(material3, IN.positionWS, meshNormalWS);
        half4 mask0 = SampleTriplanarMask(material0, IN.positionWS, meshNormalWS);
        half4 mask1 = SampleTriplanarMask(material1, IN.positionWS, meshNormalWS);
        half4 mask2 = SampleTriplanarMask(material2, IN.positionWS, meshNormalWS);
        half4 mask3 = SampleTriplanarMask(material3, IN.positionWS, meshNormalWS);
        float3 normal0 = SampleTriplanarNormal(material0, IN.positionWS, meshNormalWS);
        float3 normal1 = SampleTriplanarNormal(material1, IN.positionWS, meshNormalWS);
        float3 normal2 = SampleTriplanarNormal(material2, IN.positionWS, meshNormalWS);
        float3 normal3 = SampleTriplanarNormal(material3, IN.positionWS, meshNormalWS);
        half4 albedo = albedo0 * weights.x + albedo1 * weights.y + albedo2 * weights.z + albedo3 * weights.w;
        half4 mask = mask0 * weights.x + mask1 * weights.y + mask2 * weights.z + mask3 * weights.w;
        float3 materialNormalWS = normalize(normal0 * weights.x + normal1 * weights.y + normal2 * weights.z + normal3 * weights.w);
        float roughnessFallback = GetMaterialRoughnessFallback(material0) * weights.x + GetMaterialRoughnessFallback(material1) * weights.y + GetMaterialRoughnessFallback(material2) * weights.z + GetMaterialRoughnessFallback(material3) * weights.w;
        float packedSmoothness = saturate(mask.a);
        float packedRoughness = 1.0 - packedSmoothness;
        float maskSignal = max(max(mask.r, mask.g), mask.a);
        float roughness = saturate(max(lerp(roughnessFallback, packedRoughness, saturate((maskSignal - 0.05) * 4.0)), 0.55));
        float ambientOcclusion = lerp(1.0, saturate(mask.g), saturate((mask.g - 0.05) * 4.0));
        float3 baseColor = saturate(albedo.rgb * _Tint.rgb * _AlbedoBrightness);
        if (_DebugMode >= 0.5) return half4(DebugModeColor(_DebugMode, baseColor, meshNormalWS, materialNormalWS, mask, roughness, packedSmoothness, ambientOcclusion, weights), 1.0);
        float3 lit = ApplyLighting(baseColor, IN.positionWS, materialNormalWS, roughness);
        lit *= lerp(1.0, ambientOcclusion, 0.35);
        if (_UseSceneFog > 0.5) lit = MixFog(lit, IN.fogCoord);
        return half4(lit, 1.0);
      }
      ENDHLSL
    }
  }
  FallBack "Universal Render Pipeline/Lit"
}
