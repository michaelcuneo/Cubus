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
      float _MaterialBlendWidth;
      float _MaterialBlendNoiseScale;
      float _MaterialBlendNoiseStrength;
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

      float3 TriplanarWeights(float3 normalWS)
      {
        float3 n = abs(normalize(normalWS));
        float sharpness = max(0.001, _TriplanarSharpness);
        n = pow(n, sharpness);
        return n / max(n.x + n.y + n.z, 0.0001);
      }

      half4 SampleTriplanarAlbedo(
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

        half4 sampleX = SampleTerrainAlbedoArray(slice, uvX, ddx(uvX), ddy(uvX));
        half4 sampleY = SampleTerrainAlbedoArray(slice, uvY, ddx(uvY), ddy(uvY));
        half4 sampleZ = SampleTerrainAlbedoArray(slice, uvZ, ddx(uvZ), ddy(uvZ));

        return sampleX * n.x + sampleY * n.y + sampleZ * n.z;
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

      float3 ApplyLighting(float3 albedoRgb, float3 positionWS, float3 normalWS, float roughness)
      {
        float3 n = normalize(normalWS);
        float3 viewDir = SafeNormalize(_WorldSpaceCameraPos - positionWS);

        float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
        Light mainLight = GetMainLight(shadowCoord);

        float atten = mainLight.shadowAttenuation * mainLight.distanceAttenuation;
        float ndl = saturate(dot(n, mainLight.direction));
        float3 direct = mainLight.color * ndl * atten * _DirectLightStrength;

        // Real sky ambient (spherical harmonics) so the terrain reacts to the scene
        // and sky lighting instead of a flat constant. A small floor keeps shaded
        // faces readable.
        float3 skyAmbient = SampleSH(n) * _AmbientStrength;
        float3 floorAmbient = 0.06.xxx;
        float3 ambient = max(skyAmbient, floorAmbient);

        // Terrain is matte: drive the highlight from the low uniform smoothness (the
        // material mask roughness is often unauthored/zero, which would otherwise make
        // everything look like wet plastic). Mask roughness can only make it more matte.
        float smoothness = saturate(_Smoothness) * saturate(1.0 - roughness);
        float3 halfVec = SafeNormalize(mainLight.direction + viewDir);
        float ndh = saturate(dot(n, halfVec));
        float shininess = exp2(lerp(3.0, 11.0, smoothness));
        float specTerm = pow(ndh, shininess) * _SpecularStrength * ndl * atten;
        float3 specular = mainLight.color * specTerm;

        float fresnel = pow(1.0 - saturate(dot(n, viewDir)), 5.0) * _FresnelStrength;
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

        material0 = max(1.0, material0);
        material1 = max(1.0, material1);
        material2 = max(1.0, material2);
        material3 = max(1.0, material3);

        float4 weights = BreakupSplatWeights(
          IN.splatWeights,
          activeMask,
          IN.positionWS);

        float3 normalWS = normalize(IN.normalWS);

        half4 albedo0 = SampleTriplanarAlbedo(material0, IN.positionWS, normalWS);
        half4 albedo1 = SampleTriplanarAlbedo(material1, IN.positionWS, normalWS);
        half4 albedo2 = SampleTriplanarAlbedo(material2, IN.positionWS, normalWS);
        half4 albedo3 = SampleTriplanarAlbedo(material3, IN.positionWS, normalWS);

        half4 mask0 = SampleTriplanarMask(material0, IN.positionWS, normalWS);
        half4 mask1 = SampleTriplanarMask(material1, IN.positionWS, normalWS);
        half4 mask2 = SampleTriplanarMask(material2, IN.positionWS, normalWS);
        half4 mask3 = SampleTriplanarMask(material3, IN.positionWS, normalWS);

        half4 albedo =
          albedo0 * weights.x +
          albedo1 * weights.y +
          albedo2 * weights.z +
          albedo3 * weights.w;

        half4 mask =
          mask0 * weights.x +
          mask1 * weights.y +
          mask2 * weights.z +
          mask3 * weights.w;

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
        float roughness = saturate(mask.g);

        float3 lit = ApplyLighting(
          albedo.rgb * _Tint.rgb,
          IN.positionWS,
          normalWS,
          roughness);

        if (_UseSceneFog > 0.5)
        {
          lit = MixFog(lit, IN.fogCoord);
        }

        return half4(lit, 1.0);
      }

      ENDHLSL
    }
  }

  FallBack "Universal Render Pipeline/Lit"
}
