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
      };

      CBUFFER_START(UnityPerMaterial)
      float4 _AtlasGrid;
      float4 _Tint;
      float4 _Atlas_TexelSize;
      float _TextureScale;
      float _TriplanarSharpness;
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

        return OUT;
      }

      half4 frag(Varyings IN) : SV_Target
      {
        float materialId = DecodeMaterialId(IN.color);
        float tileId = SampleDensityTileId(materialId);
        float renderCategory = SampleRenderCategory(materialId);

        half4 albedo = SampleTriplanar(
            tileId,
            IN.positionWS,
            normalize(IN.normalWS)
        );

        if (renderCategory == 1.0 && albedo.a < 0.5)
        {
          discard;
        }

        float3 lit = ApplyLighting(albedo.rgb * _Tint.rgb, IN.normalWS);
        return half4(lit, 1.0);
      }

      ENDHLSL
    }
  }

  FallBack "Universal Render Pipeline/Lit"
}