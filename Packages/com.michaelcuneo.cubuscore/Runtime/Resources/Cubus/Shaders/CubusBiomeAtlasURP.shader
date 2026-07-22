Shader "Cubus/BiomeAtlasURP"
{
  Properties
  {
    _Atlas("Biome Texture Atlas", 2D) = "white" {}
    _TopLookup("Top Tile Lookup", 2D) = "black" {}
    _SideLookup("Side Tile Lookup", 2D) = "black" {}
    _BottomLookup("Bottom Tile Lookup", 2D) = "black" {}
    _PropsLookup("Props Lookup", 2D) = "black" {}
    _NormalAtlas("Biome Normal Atlas", 2D) = "bump" {}
    _AtlasGrid("Atlas Grid (X Columns, Y Rows)", Vector) = (4, 4, 0, 0)
    _UseSceneFog("Use Scene Fog", Range(0, 1)) = 0
    _Tint("Tint", Color) = (1, 1, 1, 1)
    _NormalStrength("Normal Strength", Range(0, 2)) = 1.0
    _DetailBumpStrength("Detail Bump From Albedo", Range(0, 4)) = 0.0
    _Smoothness("Smoothness", Range(0, 1)) = 0.12
    _SpecularStrength("Specular Strength", Range(0, 2)) = 0.25
    _FresnelStrength("Fresnel Rim", Range(0, 2)) = 0.15
    _AmbientStrength("Ambient Strength", Range(0, 1)) = 0.28
    _DirectLightStrength("Direct Sun Strength", Range(0, 2)) = 1.15
    _TerrainTextureDetileStrength("Terrain Texture Detile Strength", Range(0, 1)) = 0.42
    _TerrainTextureNoiseScale("Terrain Texture Noise Scale", Float) = 0.23
    _TerrainTextureNoiseStrength("Terrain Texture Noise Strength", Range(0, 0.35)) = 0.025
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

      TEXTURE2D(_Atlas);
      SAMPLER(sampler_Atlas);
      TEXTURE2D(_TopLookup);
      SAMPLER(sampler_TopLookup);
      TEXTURE2D(_SideLookup);
      SAMPLER(sampler_SideLookup);
      TEXTURE2D(_BottomLookup);
      SAMPLER(sampler_BottomLookup);
      TEXTURE2D(_PropsLookup);
      SAMPLER(sampler_PropsLookup);
      TEXTURE2D(_NormalAtlas);
      SAMPLER(sampler_NormalAtlas);

      struct Attributes
      {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
        float4 color : COLOR;
        float2 uv : TEXCOORD0;
      };

      struct Varyings
      {
        float4 positionCS : SV_POSITION;
        float3 positionWS : TEXCOORD0;
        float3 normalWS : TEXCOORD1;
        float2 uv : TEXCOORD2;
        float4 color : TEXCOORD3;
        float fogCoord : TEXCOORD4;
      };

      CBUFFER_START(UnityPerMaterial)
      float4 _AtlasGrid;
      float4 _Tint;
      float4 _Atlas_TexelSize;
      float _NormalStrength;
      float _UseSceneFog;
      float _DetailBumpStrength;
      float _Smoothness;
      float _SpecularStrength;
      float _FresnelStrength;
      float _AmbientStrength;
      float _DirectLightStrength;
      float _TerrainTextureDetileStrength;
      float _TerrainTextureNoiseScale;
      float _TerrainTextureNoiseStrength;
      CBUFFER_END

      float DecodeU16(float2 rg)
      {
        float low = round(saturate(rg.x) * 255.0);
        float high = round(saturate(rg.y) * 255.0);
        return low + high * 256.0;
      }

      float LinearToSrgbChannel(float value)
      {
        value = saturate(value);
        return value <= 0.0031308 ? value * 12.92 : (1.055 * pow(value, 1.0 / 2.4)) - 0.055;
      }

      float DecodeLookupU16(float2 rg)
      {
        float low = round(LinearToSrgbChannel(rg.x) * 255.0);
        float high = round(LinearToSrgbChannel(rg.y) * 255.0);
        return low + high * 256.0;
      }

      float DecodeLookupByte(float value)
      {
        return round(LinearToSrgbChannel(value) * 255.0);
      }

      float2 MaterialLookupUv(float materialId)
      {
        float clampedId = clamp(round(materialId), 0.0, 65535.0);
        float low = fmod(clampedId, 256.0);
        float high = floor(clampedId / 256.0);
        return (float2(low, high) + 0.5) / 256.0;
      }

      float DecodeMaterialId(float4 color)
      {
        return max(1.0, DecodeU16(color.rg));
      }

      float SampleLookupTileId(TEXTURE2D_PARAM(lookupTex, lookupSampler), float materialId)
      {
        float2 uv = MaterialLookupUv(materialId);
        float4 encoded = SAMPLE_TEXTURE2D(lookupTex, lookupSampler, uv);
        return max(1.0, DecodeLookupU16(encoded.rg));
      }

      float SampleRenderCategory(float materialId)
      {
        float2 uv = MaterialLookupUv(materialId);
        float4 encoded = SAMPLE_TEXTURE2D(_PropsLookup, sampler_PropsLookup, uv);
        return DecodeLookupByte(encoded.r);
      }

      float SelectTileId(float3 normalWS, float materialId)
      {
        float3 n = normalize(normalWS);
        if (n.y > 0.45)
        {
          return SampleLookupTileId(TEXTURE2D_ARGS(_TopLookup, sampler_TopLookup), materialId);
        }

        if (n.y < -0.45)
        {
          return SampleLookupTileId(TEXTURE2D_ARGS(_BottomLookup, sampler_BottomLookup), materialId);
        }

        return SampleLookupTileId(TEXTURE2D_ARGS(_SideLookup, sampler_SideLookup), materialId);
      }

      void AtlasTileInfo(float tileId, out float2 atlasGrid, out float2 tileOffset)
      {
        float columns = max(1.0, floor(_AtlasGrid.x + 0.5));
        float rows = max(1.0, floor(_AtlasGrid.y + 0.5));
        float totalTiles = max(1.0, columns * rows);
        float tile = fmod(tileId - 1.0, totalTiles);
        float sourceRowTopToBottom = floor(tile / columns);
        float row = rows - 1.0 - sourceRowTopToBottom;
        float col = tile - sourceRowTopToBottom * columns;
        atlasGrid = float2(columns, rows);
        tileOffset = float2(col, row);
      }

      float2 AtlasUv(float2 localTileUv, float2 atlasGrid, float2 tileOffset)
      {
        float2 tileUv = frac(localTileUv);
        float2 tilePixelSize = max(_Atlas_TexelSize.zw / atlasGrid, 1.0);
        float2 halfTexelInTile = 0.5 / tilePixelSize;
        tileUv = clamp(tileUv, halfTexelInTile, 1.0 - halfTexelInTile);
        return (tileUv + tileOffset) / atlasGrid;
      }

      half3 SampleAtlasRGB(float2 localTileUv, float2 tileOffset, float2 atlasGrid, float2 atlasDx, float2 atlasDy)
      {
        float2 atlasUv = AtlasUv(localTileUv, atlasGrid, tileOffset);
        return SAMPLE_TEXTURE2D_GRAD(_Atlas, sampler_Atlas, atlasUv, atlasDx, atlasDy).rgb;
      }

      float3 PerturbNormal(float3 N, float3 worldPos, float2 uv, float3 tangentNormal)
      {
        float3 dp1 = ddx(worldPos);
        float3 dp2 = ddy(worldPos);
        float2 duv1 = ddx(uv);
        float2 duv2 = ddy(uv);
        float3 dp2perp = cross(dp2, N);
        float3 dp1perp = cross(N, dp1);
        float3 T = dp2perp * duv1.x + dp1perp * duv2.x;
        float3 B = dp2perp * duv1.y + dp1perp * duv2.y;
        float invmax = rsqrt(max(dot(T, T), dot(B, B)));
        float3x3 tbn = float3x3(T * invmax, B * invmax, N);
        return SafeNormalize(mul(tangentNormal, tbn));
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

      float TerrainTextureNoise(float3 positionWS)
      {
        float scale = max(0.0001, _TerrainTextureNoiseScale);
        float3 noisePosition = positionWS * scale;

        float broad = ValueNoise(noisePosition * float3(1.0, 0.71, 1.37));
        float detail = ValueNoise(noisePosition * float3(2.73, 1.91, 3.41) + 37.19);

        return (broad * 0.42 + detail * 0.58) - 0.5;
      }

      float3 ApplyLighting(float3 albedoRgb, float3 positionWS, float3 normalWS)
      {
        float3 n = normalize(normalWS);
        float3 viewDir = SafeNormalize(_WorldSpaceCameraPos - positionWS);
        float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
        Light mainLight = GetMainLight(shadowCoord);

        float atten = mainLight.shadowAttenuation;
        float ndl = saturate(dot(n, mainLight.direction));
        float3 direct = mainLight.color * ndl * atten * _DirectLightStrength;

        // Keep a small sky/base ambient so unlit faces are readable, but do not let
        // it wash out the directional sun contrast like the old block shader did.
        float3 skyAmbient = SampleSH(n) * _AmbientStrength;
        float3 floorAmbient = 0.06.xxx;
        float3 ambient = max(skyAmbient, floorAmbient);

        float3 halfVec = SafeNormalize(mainLight.direction + viewDir);
        float ndh = saturate(dot(n, halfVec));
        float shininess = exp2(lerp(3.0, 11.0, saturate(_Smoothness)));
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
        OUT.uv = IN.uv;
        OUT.color = IN.color;
        OUT.fogCoord = ComputeFogFactor(pos.positionCS.z);
        return OUT;
      }

      half4 frag(Varyings IN) : SV_Target
      {
        float3 normalWS = normalize(IN.normalWS);
        float materialId = DecodeMaterialId(IN.color);
        float tileId = SelectTileId(normalWS, materialId);
        float renderCategory = SampleRenderCategory(materialId);

        float2 atlasGrid;
        float2 tileOffset;
        AtlasTileInfo(tileId, atlasGrid, tileOffset);

        float2 unwrappedTileUv = IN.uv;
        float2 atlasDx = ddx(unwrappedTileUv) / atlasGrid;
        float2 atlasDy = ddy(unwrappedTileUv) / atlasGrid;
        float2 atlasUv = AtlasUv(unwrappedTileUv, atlasGrid, tileOffset);

        half4 albedo = SAMPLE_TEXTURE2D_GRAD(_Atlas, sampler_Atlas, atlasUv, atlasDx, atlasDy);
        if (renderCategory == 1.0 && albedo.a < 0.5)
        {
          discard;
        }

        float detileStrength = saturate(_TerrainTextureDetileStrength);
        if (detileStrength > 0.0001)
        {
          float materialSeed = materialId * 23.731;
          float2 detileTileUv = unwrappedTileUv * 1.618 + float2(0.37, 0.61) + materialSeed * 0.013;
          float2 detileAtlasDx = atlasDx * 1.618;
          float2 detileAtlasDy = atlasDy * 1.618;
          float2 detileAtlasUv = AtlasUv(detileTileUv, atlasGrid, tileOffset);
          half4 detileAlbedo = SAMPLE_TEXTURE2D_GRAD(_Atlas, sampler_Atlas, detileAtlasUv, detileAtlasDx, detileAtlasDy);
          float detileBlend = saturate(ValueNoise(IN.positionWS * 0.173 + materialSeed) * 0.65 + 0.18) * detileStrength;
          albedo.rgb = lerp(albedo.rgb, detileAlbedo.rgb, detileBlend);
        }

        float3 normalAtlasSample = SAMPLE_TEXTURE2D_GRAD(_NormalAtlas, sampler_NormalAtlas, atlasUv, atlasDx, atlasDy).rgb;
        float3 tangentNormal = normalAtlasSample * 2.0 - 1.0;
        tangentNormal.xy *= _NormalStrength;

        if (_DetailBumpStrength > 0.0)
        {
          float2 tilePixelSize = max(_Atlas_TexelSize.zw / atlasGrid, 1.0);
          float2 texelStep = 1.0 / tilePixelSize;
          float hC = Luminance(albedo.rgb);
          float hX = Luminance(SampleAtlasRGB(unwrappedTileUv + float2(texelStep.x, 0.0), tileOffset, atlasGrid, atlasDx, atlasDy));
          float hY = Luminance(SampleAtlasRGB(unwrappedTileUv + float2(0.0, texelStep.y), tileOffset, atlasGrid, atlasDx, atlasDy));
          float2 grad = float2(hX - hC, hY - hC) * _DetailBumpStrength;
          float3 detailTN = normalize(float3(-grad, 1.0));
          tangentNormal = normalize(float3(tangentNormal.xy + detailTN.xy, tangentNormal.z * detailTN.z));
        }
        else
        {
          tangentNormal = normalize(tangentNormal);
        }

        float3 perturbedNormalWS = PerturbNormal(normalWS, IN.positionWS, unwrappedTileUv, tangentNormal);
  float terrainTextureNoise = TerrainTextureNoise(IN.positionWS) * _TerrainTextureNoiseStrength;
  albedo.rgb *= max(0.0, 1.0 + terrainTextureNoise);

        float3 shadedAlbedo = albedo.rgb * _Tint.rgb;

        float3 lit = ApplyLighting(shadedAlbedo, IN.positionWS, perturbedNormalWS);

        if (_UseSceneFog > 0.5)
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

    Pass
    {
      Name "DepthNormals"
      Tags { "LightMode" = "DepthNormals" }
      ZWrite On
      ZTest LEqual
      Cull Back

      HLSLPROGRAM
      #pragma vertex DepthNormalsVert
      #pragma fragment DepthNormalsFrag
      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

      struct DepthNormalsAttributes
      {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
      };

      struct DepthNormalsVaryings
      {
        float4 positionCS : SV_POSITION;
        float3 normalWS : TEXCOORD0;
      };

      DepthNormalsVaryings DepthNormalsVert(DepthNormalsAttributes IN)
      {
        DepthNormalsVaryings OUT;
        VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
        VertexNormalInputs nrm = GetVertexNormalInputs(IN.normalOS);
        OUT.positionCS = pos.positionCS;
        OUT.normalWS = nrm.normalWS;
        return OUT;
      }

      half4 DepthNormalsFrag(DepthNormalsVaryings IN) : SV_Target
      {
        return half4(normalize(IN.normalWS), 0.0);
      }
      ENDHLSL
    }
  }

  FallBack "Universal Render Pipeline/Lit"
}
