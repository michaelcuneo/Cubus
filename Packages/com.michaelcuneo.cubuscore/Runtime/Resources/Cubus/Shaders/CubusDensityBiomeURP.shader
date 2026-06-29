Shader "Cubus/DensityBiomeURP"
{
  Properties
  {
    _Atlas("Biome Texture Atlas", 2D) = "white" {}
    _DensityLookup("Density Tile Lookup", 2D) = "black" {}
    _TopLookup("Fallback Tile Lookup", 2D) = "black" {}
    _PropsLookup("Props Lookup", 2D) = "black" {}
    _AtlasGrid("Atlas Grid (X Columns, Y Rows)", Vector) = (4, 4, 0, 0)
    _Tint("Tint", Color) = (1, 1, 1, 1)
    _TextureScale("Texture Scale", Float) = 0.25
    _TriplanarSharpness("Triplanar Sharpness", Float) = 4.0
    _MaterialBlendWidth("Material Blend Width", Range(0.01, 1)) = 0.35
    _MaterialBlendNoiseScale("Material Blend Noise Scale", Float) = 0.14
    _MaterialBlendNoiseStrength("Material Blend Noise Strength", Range(0, 1)) = 0.45
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

      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

      TEXTURE2D(_Atlas);
      SAMPLER(sampler_Atlas);

      TEXTURE2D(_DensityLookup);
      SAMPLER(sampler_DensityLookup);

      TEXTURE2D(_TopLookup);
      SAMPLER(sampler_TopLookup);

      TEXTURE2D(_PropsLookup);
      SAMPLER(sampler_PropsLookup);

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
        float4 color : TEXCOORD2;
        float2 materialBlend : TEXCOORD3;
      };

      CBUFFER_START(UnityPerMaterial)
      float4 _AtlasGrid;
      float4 _Tint;
      float4 _Atlas_TexelSize;
      float _TextureScale;
      float _TriplanarSharpness;
      float _MaterialBlendWidth;
      float _MaterialBlendNoiseScale;
      float _MaterialBlendNoiseStrength;
      CBUFFER_END

      float DecodeU16(float2 rg)
      {
        float low = round(saturate(rg.x) * 255.0);
        float high = round(saturate(rg.y) * 255.0);
        return low + (high * 256.0);
      }

      float LinearToSrgbChannel(float value)
      {
        value = saturate(value);
        return value <= 0.0031308
          ? value * 12.92
          : (1.055 * pow(value, 1.0 / 2.4)) - 0.055;
      }

      float DecodeLookupU16(float2 rg)
      {
        float low = round(LinearToSrgbChannel(rg.x) * 255.0);
        float high = round(LinearToSrgbChannel(rg.y) * 255.0);
        return low + (high * 256.0);
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

      float SampleRenderCategory(float materialId)
      {
        float2 uv = MaterialLookupUv(materialId);
        float4 encoded = SAMPLE_TEXTURE2D(_PropsLookup, sampler_PropsLookup, uv);
        return DecodeLookupByte(encoded.r);
      }

      float SampleDensityTileId(float materialId)
      {
        float2 uv = MaterialLookupUv(materialId);

        float4 encoded = SAMPLE_TEXTURE2D(_DensityLookup, sampler_DensityLookup, uv);
        float tileId = DecodeLookupU16(encoded.rg);

        // Fallback for early wiring where _DensityLookup is not bound.
        if (tileId < 1.0)
        {
          encoded = SAMPLE_TEXTURE2D(_TopLookup, sampler_TopLookup, uv);
          tileId = DecodeLookupU16(encoded.rg);
        }

        return max(1.0, tileId);
      }

      void AtlasTileInfo(float tileId, out float2 atlasGrid, out float2 tileOffset)
      {
        float columns = max(1.0, floor(_AtlasGrid.x + 0.5));
        float rows = max(1.0, floor(_AtlasGrid.y + 0.5));
        float totalTiles = max(1.0, columns * rows);

        float tile = fmod(tileId - 1.0, totalTiles);
        float sourceRowTopToBottom = floor(tile / columns);
        float row = rows - 1.0 - sourceRowTopToBottom;
        float col = tile - (sourceRowTopToBottom * columns);

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

      half4 SampleAtlasTile(float tileId, float2 localTileUv, float2 gradX, float2 gradY)
      {
        float2 atlasGrid;
        float2 tileOffset;
        AtlasTileInfo(tileId, atlasGrid, tileOffset);

        float2 atlasUv = AtlasUv(localTileUv, atlasGrid, tileOffset);
        float2 atlasDx = gradX / atlasGrid;
        float2 atlasDy = gradY / atlasGrid;

        return SAMPLE_TEXTURE2D_GRAD(
            _Atlas,
            sampler_Atlas,
            atlasUv,
            atlasDx,
            atlasDy
        );
      }

      half4 SampleTriplanar(float tileId, float3 positionWS, float3 normalWS)
      {
        float3 n = abs(normalize(normalWS));
        float sharpness = max(0.001, _TriplanarSharpness);

        n = pow(n, sharpness);
        n /= max(n.x + n.y + n.z, 0.0001);

        float scale = max(0.0001, _TextureScale);

        float2 uvX = positionWS.zy * scale;
        float2 uvY = positionWS.xz * scale;
        float2 uvZ = positionWS.xy * scale;

        half4 sampleX = SampleAtlasTile(tileId, uvX, ddx(uvX), ddy(uvX));
        half4 sampleY = SampleAtlasTile(tileId, uvY, ddx(uvY), ddy(uvY));
        half4 sampleZ = SampleAtlasTile(tileId, uvZ, ddx(uvZ), ddy(uvZ));

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

      float TerrainBlendMask(float rawBlend, float3 positionWS)
      {
        rawBlend = saturate(rawBlend);

        if (rawBlend <= 0.0001 || rawBlend >= 0.9999)
        {
          return rawBlend;
        }

        float noiseScale = max(0.0001, _MaterialBlendNoiseScale);

        float n1 = ValueNoise(positionWS * noiseScale);
        float n2 = ValueNoise(positionWS * noiseScale * 2.37 + 19.17);

        float noise = (n1 * 0.65 + n2 * 0.35) - 0.5;

        float blendWidth = max(0.001, _MaterialBlendWidth);
        float noisyBlend = rawBlend + noise * _MaterialBlendNoiseStrength;

        return smoothstep(
          0.5 - blendWidth,
          0.5 + blendWidth,
          noisyBlend
        );
      }

      float3 ApplyLighting(float3 albedoRgb, float3 normalWS)
      {
        float3 n = normalize(normalWS);
        Light mainLight = GetMainLight();

        float ndl = saturate(dot(n, mainLight.direction));
        float litFactor = 0.35 + ndl * 0.65;

        return albedoRgb * litFactor * mainLight.color;
      }

      Varyings vert(Attributes IN)
      {
        Varyings OUT;

        VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
        VertexNormalInputs nrm = GetVertexNormalInputs(IN.normalOS);

        OUT.positionCS = pos.positionCS;
        OUT.positionWS = pos.positionWS;
        OUT.normalWS = nrm.normalWS;
        OUT.color = IN.color;
        OUT.materialBlend = IN.uv;

        return OUT;
      }

      half4 frag(Varyings IN) : SV_Target
      {
        float primaryMaterialId = DecodeMaterialId(IN.color);
        float secondaryMaterialId = max(1.0, round(IN.materialBlend.x));
        float rawBlendWeight = saturate(IN.materialBlend.y);

        if (abs(secondaryMaterialId - primaryMaterialId) < 0.5)
        {
          rawBlendWeight = 0.0;
        }

        float blendWeight = TerrainBlendMask(rawBlendWeight, IN.positionWS);

        float primaryTileId = SampleDensityTileId(primaryMaterialId);
        float secondaryTileId = SampleDensityTileId(secondaryMaterialId);
        float renderCategory = SampleRenderCategory(primaryMaterialId);
        float3 normalWS = normalize(IN.normalWS);

        half4 primaryAlbedo = SampleTriplanar(
            primaryTileId,
            IN.positionWS,
            normalWS
        );

        half4 secondaryAlbedo = SampleTriplanar(
            secondaryTileId,
            IN.positionWS,
            normalWS
        );

        half4 albedo = lerp(primaryAlbedo, secondaryAlbedo, blendWeight);

        if (renderCategory == 1.0 && albedo.a < 0.5)
        {
          discard;
        }

        float3 lit = ApplyLighting(albedo.rgb * _Tint.rgb, normalWS);
        return half4(lit, 1.0);
      }

      ENDHLSL
    }
  }

  FallBack "Universal Render Pipeline/Lit"
}
