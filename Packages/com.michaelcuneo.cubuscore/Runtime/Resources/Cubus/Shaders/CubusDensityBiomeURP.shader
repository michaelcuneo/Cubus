Shader "Cubus/DensityBiomeURP"
{
  Properties
  {
    _Tint("Tint", Color) = (1, 1, 1, 1)

    _TextureScale("Global Texture Scale", Float) = 0.25
    _TriplanarSharpness("Triplanar Sharpness", Float) = 4.0
    _DistanceTextureStart("Distance Texture Blend Start", Float) = 80
    _DistanceTextureEnd("Distance Texture Blend End", Float) = 240
    _DistanceTextureScaleMultiplier("Distance Texture Scale Multiplier", Range(0.02, 1)) = 0.18
    _DistanceNormalFade("Distance Normal Fade", Range(0, 1)) = 0.85
    _DistanceTextureWarpScale("Distance Texture Warp Scale", Float) = 0.012
    _DistanceTextureWarpStrength("Distance Texture Warp Strength", Range(0, 64)) = 22
    _DistanceTextureVariationStrength("Distance Texture Variation Strength", Range(0, 0.35)) = 0.12

    _MaterialBlendWidth("Material Blend Width", Range(0.01, 1)) = 0.35
    _MaterialBlendNoiseScale("Material Blend Noise Scale", Float) = 0.14
    _MaterialBlendNoiseStrength("Material Blend Noise Strength", Range(0, 1)) = 0.45
    _TerrainTextureDetileStrength("Terrain Texture Detile Strength", Range(0, 1)) = 0.42
    _TerrainTextureNoiseScale("Terrain Texture Noise Scale", Float) = 0.23
    _TerrainTextureNoiseStrength("Terrain Texture Noise Strength", Range(0, 0.35)) = 0.025
    _DensityTerrainNormalMapStrength("Density Terrain Normal Map Strength", Range(0, 1)) = 1
    _DensityTerrainLightingNormalMapStrength("Density Terrain Lighting Normal Map Strength", Range(0, 1)) = 1
    _DensityTerrainShadowStrength("Density Terrain Shadow Strength", Range(0, 1)) = 1
    _DensityDebugView("Density Debug View", Float) = 0

    _Smoothness("Smoothness", Range(0, 1)) = 0.12
    _SpecularStrength("Specular Strength", Range(0, 2)) = 0.25
    _FresnelStrength("Fresnel Rim", Range(0, 2)) = 0.15
    _AmbientStrength("Ambient Strength", Range(0, 1)) = 0.28
    _DirectLightStrength("Direct Sun Strength", Range(0, 2)) = 1.15
    _UseSceneFog("Use Scene Fog", Range(0, 1)) = 0
  }

  SubShader
  {
    Tags
    {
      "RenderType" = "Opaque"
      "RenderPipeline" = "UniversalPipeline"
      "Queue" = "Geometry"
    }

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
      #include "Packages/com.michaelcuneo.cubuscore/Runtime/Resources/Cubus/Shaders/CubusAzureWeather.hlsl"

      TEXTURE2D_ARRAY(_TerrainAlbedoArray);
      SAMPLER(sampler_TerrainAlbedoArray);

      TEXTURE2D_ARRAY(_TerrainNormalArray);
      SAMPLER(sampler_TerrainNormalArray);

      TEXTURE2D_ARRAY(_TerrainMaskArray);
      SAMPLER(sampler_TerrainMaskArray);

      TEXTURE2D(_TerrainMaterialSliceLookup);
      SAMPLER(sampler_TerrainMaterialSliceLookup);

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
      float _DistanceTextureStart;
      float _DistanceTextureEnd;
      float _DistanceTextureScaleMultiplier;
      float _DistanceNormalFade;
      float _DistanceTextureWarpScale;
      float _DistanceTextureWarpStrength;
      float _DistanceTextureVariationStrength;
      float _MaterialBlendWidth;
      float _MaterialBlendNoiseScale;
      float _MaterialBlendNoiseStrength;
      float _TerrainTextureDetileStrength;
      float _TerrainTextureNoiseScale;
      float _TerrainTextureNoiseStrength;
      float _DensityTerrainNormalMapStrength;
      float _DensityTerrainLightingNormalMapStrength;
      float _DensityTerrainShadowStrength;
      float _DensityDebugView;
      float _Smoothness;
      float _SpecularStrength;
      float _FresnelStrength;
      float _AmbientStrength;
      float _DirectLightStrength;
      float _UseSceneFog;
      int _TerrainMaterialCount;
      float4 _TerrainMaterialParams[128];
      CBUFFER_END

      float MaterialIdToSlice(float materialId)
      {
        float id = clamp(round(materialId), 1.0, 65535.0);
        float x = fmod(id, 256.0);
        float y = floor(id / 256.0);
        float2 uv = (float2(x, y) + 0.5) / 256.0;
        half4 encoded = SAMPLE_TEXTURE2D_LOD(
          _TerrainMaterialSliceLookup,
          sampler_TerrainMaterialSliceLookup,
          uv,
          0.0);

        float encodedSlice = round(encoded.r * 255.0) + round(encoded.g * 255.0) * 256.0;

        if (encodedSlice <= 0.5)
        {
          return 0.0;
        }

        float count = max(1.0, (float)_TerrainMaterialCount);
        return clamp(encodedSlice - 1.0, 0.0, count - 1.0);
      }

      float4 GetTerrainMaterialParams(float slice)
      {
        int index = clamp((int)round(slice), 0, 127);
        return _TerrainMaterialParams[index];
      }

      half4 SampleTerrainAlbedoArray(
        float slice,
        float2 uv,
        float2 gradX,
        float2 gradY)
      {
        return SAMPLE_TEXTURE2D_ARRAY_GRAD(
          _TerrainAlbedoArray,
          sampler_TerrainAlbedoArray,
          uv,
          slice,
          gradX,
          gradY);
      }

      half4 SampleTerrainMaskArray(
        float slice,
        float2 uv,
        float2 gradX,
        float2 gradY)
      {
        return SAMPLE_TEXTURE2D_ARRAY_GRAD(
          _TerrainMaskArray,
          sampler_TerrainMaskArray,
          uv,
          slice,
          gradX,
          gradY);
      }

      half4 SampleTerrainNormalArray(
        float slice,
        float2 uv,
        float2 gradX,
        float2 gradY)
      {
        return SAMPLE_TEXTURE2D_ARRAY_GRAD(
          _TerrainNormalArray,
          sampler_TerrainNormalArray,
          uv,
          slice,
          gradX,
          gradY);
      }

      float3 TriplanarWeights(float3 normalWS)
      {
        float3 n = abs(normalize(normalWS));
        float sharpness = max(0.001, _TriplanarSharpness);
        n = pow(n, sharpness);
        return n / max(n.x + n.y + n.z, 0.0001);
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

        float nxy0 = lerp(nx00, nx10, f.y);
        float nxy1 = lerp(nx01, nx11, f.y);

        return lerp(nxy0, nxy1, f.z);
      }

      half4 SampleTriplanarAlbedo(
        float materialId,
        float3 positionWS,
        float3 normalWS,
        float scaleMultiplier)
      {
        float slice = MaterialIdToSlice(materialId);
        float4 materialParams = GetTerrainMaterialParams(slice);

        float tiling = max(0.0001, materialParams.x);
        float3 n = TriplanarWeights(normalWS);
        float scale = max(0.0001, _TextureScale) * tiling * max(0.0001, scaleMultiplier);

        float2 uvX = positionWS.zy * scale;
        float2 uvY = positionWS.xz * scale;
        float2 uvZ = positionWS.xy * scale;

        half4 sampleX = SampleTerrainAlbedoArray(slice, uvX, ddx(uvX), ddy(uvX));
        half4 sampleY = SampleTerrainAlbedoArray(slice, uvY, ddx(uvY), ddy(uvY));
        half4 sampleZ = SampleTerrainAlbedoArray(slice, uvZ, ddx(uvZ), ddy(uvZ));

        float detileStrength = saturate(_TerrainTextureDetileStrength);
        if (detileStrength > 0.0001)
        {
          float materialSeed = materialId * 23.731;
          float detileBlend = saturate(ValueNoise(positionWS * 0.173 + materialSeed) * 0.65 + 0.18) * detileStrength;

          float2 offsetX = float2(0.37, 0.61) + materialSeed * 0.013;
          float2 offsetY = float2(0.53, 0.29) + materialSeed * 0.017;
          float2 offsetZ = float2(0.19, 0.47) + materialSeed * 0.019;

          half4 detileX = SampleTerrainAlbedoArray(slice, uvX * 1.618 + offsetX, ddx(uvX) * 1.618, ddy(uvX) * 1.618);
          half4 detileY = SampleTerrainAlbedoArray(slice, uvY * 1.618 + offsetY, ddx(uvY) * 1.618, ddy(uvY) * 1.618);
          half4 detileZ = SampleTerrainAlbedoArray(slice, uvZ * 1.618 + offsetZ, ddx(uvZ) * 1.618, ddy(uvZ) * 1.618);

          sampleX = lerp(sampleX, detileX, detileBlend);
          sampleY = lerp(sampleY, detileY, detileBlend);
          sampleZ = lerp(sampleZ, detileZ, detileBlend);
        }

        return sampleX * n.x + sampleY * n.y + sampleZ * n.z;
      }

      half4 SampleTriplanarAlbedo(
        float materialId,
        float3 positionWS,
        float3 normalWS)
      {
        return SampleTriplanarAlbedo(materialId, positionWS, normalWS, 1.0);
      }

      float DistanceTextureBlend(float3 positionWS)
      {
        float startDistance = max(0.0, _DistanceTextureStart);
        float endDistance = max(startDistance + 0.001, _DistanceTextureEnd);
        float viewDistance = distance(_WorldSpaceCameraPos, positionWS);
        float t = saturate((viewDistance - startDistance) / (endDistance - startDistance));
        return t * t * (3.0 - 2.0 * t);
      }

      half4 SampleTriplanarMask(
        float materialId,
        float3 positionWS,
        float3 normalWS)
      {
        float slice = MaterialIdToSlice(materialId);
        float4 materialParams = GetTerrainMaterialParams(slice);

        float tiling = max(0.0001, materialParams.x);
        float3 n = TriplanarWeights(normalWS);
        float scale = max(0.0001, _TextureScale) * tiling;

        float2 uvX = positionWS.zy * scale;
        float2 uvY = positionWS.xz * scale;
        float2 uvZ = positionWS.xy * scale;

        half4 sampleX = SampleTerrainMaskArray(slice, uvX, ddx(uvX), ddy(uvX));
        half4 sampleY = SampleTerrainMaskArray(slice, uvY, ddx(uvY), ddy(uvY));
        half4 sampleZ = SampleTerrainMaskArray(slice, uvZ, ddx(uvZ), ddy(uvZ));

        return sampleX * n.x + sampleY * n.y + sampleZ * n.z;
      }

      float3 UnpackTerrainNormal(half4 packedNormal, float normalStrength)
      {
        float3 tangentNormal = packedNormal.xyz * 2.0 - 1.0;
        tangentNormal.xy *= normalStrength;
        tangentNormal.z = max(0.001, tangentNormal.z);
        return normalize(tangentNormal);
      }

      float3 BlendTriplanarNormal(
        float3 normalX,
        float3 normalY,
        float3 normalZ,
        float3 weights,
        float3 baseNormalWS)
      {
        float3 signNormal = sign(baseNormalWS);
        signNormal.x = signNormal.x == 0.0 ? 1.0 : signNormal.x;
        signNormal.y = signNormal.y == 0.0 ? 1.0 : signNormal.y;
        signNormal.z = signNormal.z == 0.0 ? 1.0 : signNormal.z;

        float3 worldX = normalize(float3(normalX.z * signNormal.x, normalX.y, normalX.x));
        float3 worldY = normalize(float3(normalY.x, normalY.z * signNormal.y, normalY.y));
        float3 worldZ = normalize(float3(normalZ.x, normalZ.y, normalZ.z * signNormal.z));

        return normalize(worldX * weights.x + worldY * weights.y + worldZ * weights.z);
      }

      float3 SampleTriplanarNormal(
        float materialId,
        float3 positionWS,
        float3 normalWS,
        float distanceBlend)
      {
        float slice = MaterialIdToSlice(materialId);
        float4 materialParams = GetTerrainMaterialParams(slice);

        float tiling = max(0.0001, materialParams.x);
        float normalStrength =
          max(0.0, materialParams.y) *
          saturate(_DensityTerrainNormalMapStrength) *
          max(0.0, 1.0 - distanceBlend * saturate(_DistanceNormalFade));
        float3 n = TriplanarWeights(normalWS);
        float scale = max(0.0001, _TextureScale) * tiling;

        float2 uvX = positionWS.zy * scale;
        float2 uvY = positionWS.xz * scale;
        float2 uvZ = positionWS.xy * scale;

        float3 normalX = UnpackTerrainNormal(SampleTerrainNormalArray(slice, uvX, ddx(uvX), ddy(uvX)), normalStrength);
        float3 normalY = UnpackTerrainNormal(SampleTerrainNormalArray(slice, uvY, ddx(uvY), ddy(uvY)), normalStrength);
        float3 normalZ = UnpackTerrainNormal(SampleTerrainNormalArray(slice, uvZ, ddx(uvZ), ddy(uvZ)), normalStrength);

        return BlendTriplanarNormal(normalX, normalY, normalZ, n, normalWS);
      }

      float GetMaterialRoughnessFallback(float materialId)
      {
        float slice = MaterialIdToSlice(materialId);
        return saturate(GetTerrainMaterialParams(slice).z);
      }

      half4 SampleDistanceTriplanarAlbedo(
        float materialId,
        float3 positionWS,
        float3 normalWS,
        float scaleMultiplier,
        float distanceBlend)
      {
        float warpScale = max(0.0001, _DistanceTextureWarpScale);
        float materialSeed = materialId * 17.371;
        float3 noisePosition = positionWS * warpScale + materialSeed;

        float3 warp = float3(
          ValueNoise(noisePosition + float3(7.13, 19.37, 2.41)),
          ValueNoise(noisePosition + float3(31.67, 5.29, 43.11)),
          ValueNoise(noisePosition + float3(13.83, 47.53, 23.79))) * 2.0 - 1.0;

        float3 warpedPosition = positionWS + warp * _DistanceTextureWarpStrength * distanceBlend;

        half4 primary = SampleTriplanarAlbedo(materialId, warpedPosition, normalWS, scaleMultiplier);
        half4 secondary = SampleTriplanarAlbedo(
          materialId,
          warpedPosition + float3(41.3, -17.7, 29.1),
          normalWS,
          scaleMultiplier * 0.57);

        float secondaryBlend = ValueNoise(noisePosition * 0.43 + 61.17) * 0.45 * distanceBlend;
        half4 albedo = lerp(primary, secondary, secondaryBlend);

        float variation =
          (ValueNoise(noisePosition * 0.31 + 97.41) - 0.5) *
          _DistanceTextureVariationStrength *
          distanceBlend;

        albedo.rgb *= max(0.0, 1.0 + variation);
        return albedo;
      }

      half4 BlendDistanceTriplanarAlbedo(
        float materialId,
        float3 positionWS,
        float3 normalWS,
        float scaleMultiplier,
        float distanceBlend)
      {
        half4 closeAlbedo = SampleTriplanarAlbedo(materialId, positionWS, normalWS);

        if (distanceBlend <= 0.0001)
        {
          return closeAlbedo;
        }

        half4 farAlbedo = SampleDistanceTriplanarAlbedo(
          materialId,
          positionWS,
          normalWS,
          scaleMultiplier,
          distanceBlend);

        return lerp(closeAlbedo, farAlbedo, distanceBlend);
      }

      float4 NormalizeSplatWeights(float4 weights)
      {
        weights = max(weights, 0.0);

        float total = weights.x + weights.y + weights.z + weights.w;

        if (total <= 0.0001)
        {
          return float4(1.0, 0.0, 0.0, 0.0);
        }

        return weights / total;
      }

      float4 BreakupSplatWeights(
        float4 weights,
        float4 activeMask,
        float3 positionWS)
      {
        weights = NormalizeSplatWeights(weights * activeMask);

        float noiseScale = max(0.0001, _MaterialBlendNoiseScale);

        float n1 = ValueNoise(positionWS * noiseScale);
        float n2 = ValueNoise(positionWS * noiseScale * 2.37 + 19.17);
        float n3 = ValueNoise(positionWS * noiseScale * 4.71 - 7.93);

        float3 noise = float3(n1, n2, n3) - 0.5;

        float transitionAmount =
          1.0 - saturate(max(max(weights.x, weights.y), max(weights.z, weights.w)));

        float strength =
          _MaterialBlendNoiseStrength *
          _MaterialBlendWidth *
          transitionAmount;

        weights.x += noise.x * strength;
        weights.y += noise.y * strength;
        weights.z += noise.z * strength;
        weights.w -= (noise.x + noise.y + noise.z) * 0.3333333 * strength;

        weights = max(weights * activeMask, 0.0);

        return NormalizeSplatWeights(weights);
      }

      float TerrainTextureNoise(float3 positionWS)
      {
        float scale = max(0.0001, _TerrainTextureNoiseScale);
        float3 noisePosition = positionWS * scale;

        float broad = ValueNoise(noisePosition * float3(1.0, 0.71, 1.37));
        float detail = ValueNoise(noisePosition * float3(2.73, 1.91, 3.41) + 37.19);

        return (broad * 0.42 + detail * 0.58) - 0.5;
      }

      float3 MaterialDebugColor(float materialId)
      {
        float seed = max(1.0, round(materialId));
        return float3(
          Hash31(float3(seed, 13.17, 7.31)),
          Hash31(float3(seed, 29.43, 41.11)),
          Hash31(float3(seed, 53.71, 19.83)));
      }

      float3 ApplyLighting(float3 albedoRgb, float3 positionWS, float3 normalWS, float roughness, float shadowStrength)
      {
        float3 n = normalize(normalWS);
        float3 viewDir = SafeNormalize(_WorldSpaceCameraPos - positionWS);

        float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
        Light mainLight = GetMainLight(shadowCoord);

        float atten = lerp(1.0, mainLight.shadowAttenuation, saturate(shadowStrength));
        float ndl = saturate(dot(n, mainLight.direction));
        float cloudShadow = CubusCloudShadow(positionWS, n);
        float3 direct = CubusAzureDirectLightColor(mainLight.color) * ndl * atten * _DirectLightStrength * cloudShadow * CubusAzureLightFlashMultiplier();

        // Real sky ambient (spherical harmonics) so the terrain reacts to the scene
        // and sky lighting instead of a flat constant. A small floor keeps shaded
        // faces readable.
        float3 skyAmbient = SampleSH(n) * _AmbientStrength;
        float3 floorAmbient = 0.06.xxx;
        float3 ambient = CubusAzureAmbientLight(max(skyAmbient, floorAmbient));

        // Terrain is matte: drive the highlight from the low uniform smoothness (the
        // material mask roughness is often unauthored/zero, which would otherwise make
        // everything look like wet plastic). Mask roughness can only make it more matte.
        float roughSurface = saturate(roughness);
        float smoothness = saturate(_Smoothness) * saturate(1.0 - roughSurface);
        float3 halfVec = SafeNormalize(mainLight.direction + viewDir);
        float ndh = saturate(dot(n, halfVec));
        float shininess = exp2(lerp(3.0, 11.0, smoothness));
        float specTerm = pow(ndh, shininess) * _SpecularStrength * smoothness * ndl * atten * cloudShadow;
        specTerm *= CubusWeatherSpecularMultiplier(positionWS, n);
        float3 specular = CubusAzureDirectLightColor(mainLight.color) * specTerm * CubusAzureLightFlashMultiplier();

        float fresnel = pow(1.0 - saturate(dot(n, viewDir)), 5.0) * _FresnelStrength * smoothness;
        float3 rim = skyAmbient * fresnel;

        return albedoRgb * (ambient + direct) + specular + rim;
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
        float material0 = round(IN.materialIds01.x);
        float material1 = round(IN.materialIds01.y);
        float material2 = round(IN.materialIds23.x);
        float material3 = round(IN.materialIds23.y);

        float4 activeMask = float4(
          material0 > 0.5 ? 1.0 : 0.0,
          material1 > 0.5 ? 1.0 : 0.0,
          material2 > 0.5 ? 1.0 : 0.0,
          material3 > 0.5 ? 1.0 : 0.0);

          float4 rawWeights = NormalizeSplatWeights(IN.splatWeights * activeMask);

        material0 = max(1.0, material0);
        material1 = max(1.0, material1);
        material2 = max(1.0, material2);
        material3 = max(1.0, material3);

        float4 weights = BreakupSplatWeights(
          IN.splatWeights,
          activeMask,
          IN.positionWS);

        float3 geometryNormalWS = normalize(IN.normalWS);

        float distanceBlend = DistanceTextureBlend(IN.positionWS);
        float farScaleMultiplier = saturate(_DistanceTextureScaleMultiplier);

        half4 albedo0 = BlendDistanceTriplanarAlbedo(material0, IN.positionWS, geometryNormalWS, farScaleMultiplier, distanceBlend);
        half4 albedo1 = BlendDistanceTriplanarAlbedo(material1, IN.positionWS, geometryNormalWS, farScaleMultiplier, distanceBlend);
        half4 albedo2 = BlendDistanceTriplanarAlbedo(material2, IN.positionWS, geometryNormalWS, farScaleMultiplier, distanceBlend);
        half4 albedo3 = BlendDistanceTriplanarAlbedo(material3, IN.positionWS, geometryNormalWS, farScaleMultiplier, distanceBlend);

        half4 mask0 = SampleTriplanarMask(material0, IN.positionWS, geometryNormalWS);
        half4 mask1 = SampleTriplanarMask(material1, IN.positionWS, geometryNormalWS);
        half4 mask2 = SampleTriplanarMask(material2, IN.positionWS, geometryNormalWS);
        half4 mask3 = SampleTriplanarMask(material3, IN.positionWS, geometryNormalWS);

        float3 normal0 = SampleTriplanarNormal(material0, IN.positionWS, geometryNormalWS, distanceBlend);
        float3 normal1 = SampleTriplanarNormal(material1, IN.positionWS, geometryNormalWS, distanceBlend);
        float3 normal2 = SampleTriplanarNormal(material2, IN.positionWS, geometryNormalWS, distanceBlend);
        float3 normal3 = SampleTriplanarNormal(material3, IN.positionWS, geometryNormalWS, distanceBlend);

        half4 albedo =
          albedo0 * weights.x +
          albedo1 * weights.y +
          albedo2 * weights.z +
          albedo3 * weights.w;

        float terrainTextureNoise = TerrainTextureNoise(IN.positionWS) * _TerrainTextureNoiseStrength;
        albedo.rgb *= max(0.0, 1.0 + terrainTextureNoise);

        half4 mask =
          mask0 * weights.x +
          mask1 * weights.y +
          mask2 * weights.z +
          mask3 * weights.w;

        float3 normalWS = normalize(
          normal0 * weights.x +
          normal1 * weights.y +
          normal2 * weights.z +
          normal3 * weights.w);

        float3 lightingNormalWS = normalize(lerp(
          geometryNormalWS,
          normalWS,
          saturate(_DensityTerrainLightingNormalMapStrength)));

        // Mask convention:
        // R = AO
        // G = Roughness
        // B = Metallic
        // A = Height
        //
        // AO (mask.r) is intentionally NOT multiplied into the diffuse albedo: the
        // terrain mask array is frequently unauthored (all zero), which would multiply
        // the whole surface to black regardless of lighting. The block shader ignores
        // AO for the same reason; when authored it belongs on the ambient term only.
        float fallbackRoughness =
          GetMaterialRoughnessFallback(material0) * weights.x +
          GetMaterialRoughnessFallback(material1) * weights.y +
          GetMaterialRoughnessFallback(material2) * weights.z +
          GetMaterialRoughnessFallback(material3) * weights.w;
        float roughness = max(saturate(mask.g), saturate(fallbackRoughness));
        float3 shadedAlbedo = albedo.rgb * _Tint.rgb;
        CubusApplyGroundWeather(shadedAlbedo, roughness, IN.positionWS, lightingNormalWS);

        int debugView = (int)round(_DensityDebugView);

        if (debugView == 1)
        {
          return half4(rawWeights.rgb, 1.0);
        }

        if (debugView == 2)
        {
          return half4(weights.rgb, 1.0);
        }

        if (debugView == 3)
        {
          float3 materialDebug =
            MaterialDebugColor(material0) * rawWeights.x +
            MaterialDebugColor(material1) * rawWeights.y +
            MaterialDebugColor(material2) * rawWeights.z +
            MaterialDebugColor(material3) * rawWeights.w;

          return half4(saturate(materialDebug), 1.0);
        }

        if (debugView == 4)
        {
          return half4(saturate(albedo.rgb * _Tint.rgb), 1.0);
        }

        if (debugView == 5)
        {
          return half4(geometryNormalWS * 0.5 + 0.5, 1.0);
        }

        if (debugView == 6)
        {
          return half4(normalWS * 0.5 + 0.5, 1.0);
        }

        if (debugView == 7)
        {
          return half4(roughness.xxx, 1.0);
        }

        if (debugView == 8)
        {
          float3 litGeometry = ApplyLighting(
            shadedAlbedo,
            IN.positionWS,
            geometryNormalWS,
            roughness,
            _DensityTerrainShadowStrength);

          return half4(litGeometry, 1.0);
        }

        if (debugView == 9)
        {
          float3 litTextureNormal = ApplyLighting(
            shadedAlbedo,
            IN.positionWS,
            normalWS,
            roughness,
            _DensityTerrainShadowStrength);

          return half4(litTextureNormal, 1.0);
        }

        if (debugView == 10)
        {
          float3 litNoShadows = ApplyLighting(
            shadedAlbedo,
            IN.positionWS,
            lightingNormalWS,
            roughness,
            0.0);

          return half4(litNoShadows, 1.0);
        }

        if (debugView == 11)
        {
          float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
          Light mainLight = GetMainLight(shadowCoord);
          return half4(mainLight.shadowAttenuation.xxx, 1.0);
        }

        if (debugView == 12)
        {
          float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
          Light mainLight = GetMainLight(shadowCoord);
          float ndl = saturate(dot(lightingNormalWS, mainLight.direction));
          return half4(ndl.xxx, 1.0);
        }

        if (debugView == 13)
        {
          float3 litGeometryNoShadows = ApplyLighting(
            shadedAlbedo,
            IN.positionWS,
            geometryNormalWS,
            roughness,
            0.0);

          return half4(litGeometryNoShadows, 1.0);
        }

        float3 lit = ApplyLighting(
          shadedAlbedo,
          IN.positionWS,
          lightingNormalWS,
          roughness,
          _DensityTerrainShadowStrength);

        if (_Azure_GlobalFogDistance > 0.0)
        {
          lit = CubusApplyAzureFog(lit, IN.positionWS);
        }
        else if (_UseSceneFog > 0.5)
        {
          lit = MixFog(lit, IN.fogCoord);
        }

        return half4(lit, 1.0);
      }

      ENDHLSL
    }

    Pass
    {
      Name "ShadowCaster"
      Tags { "LightMode" = "ShadowCaster" }
      ZWrite On
      ZTest LEqual
      Cull Back
      ColorMask 0

      HLSLPROGRAM
      #pragma vertex ShadowVert
      #pragma fragment ShadowFrag
      #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

      float3 _LightDirection;
      float3 _LightPosition;

      struct ShadowAttributes
      {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
      };

      struct ShadowVaryings
      {
        float4 positionCS : SV_POSITION;
      };

      float4 GetShadowClipPosition(float3 positionWS, float3 normalWS)
      {
      #if _CASTING_PUNCTUAL_LIGHT_SHADOW
        float3 lightDirectionWS = normalize(_LightPosition - positionWS);
      #else
        float3 lightDirectionWS = _LightDirection;
      #endif
        float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
      #if UNITY_REVERSED_Z
        positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
      #else
        positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
      #endif
        return positionCS;
      }

      ShadowVaryings ShadowVert(ShadowAttributes IN)
      {
        ShadowVaryings OUT;
        float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
        float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
        OUT.positionCS = GetShadowClipPosition(positionWS, normalWS);
        return OUT;
      }

      half4 ShadowFrag(ShadowVaryings IN) : SV_Target
      {
        return 0;
      }
      ENDHLSL
    }

    Pass
    {
      Name "DepthOnly"
      Tags { "LightMode" = "DepthOnly" }
      ZWrite On
      ColorMask R
      Cull Back

      HLSLPROGRAM
      #pragma vertex DepthVert
      #pragma fragment DepthFrag
      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

      struct DepthAttributes
      {
        float4 positionOS : POSITION;
      };

      struct DepthVaryings
      {
        float4 positionCS : SV_POSITION;
      };

      DepthVaryings DepthVert(DepthAttributes IN)
      {
        DepthVaryings OUT;
        OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
        return OUT;
      }

      half DepthFrag(DepthVaryings IN) : SV_Target
      {
        return 0;
      }
      ENDHLSL
    }
  }

  FallBack "Universal Render Pipeline/Lit"
}
