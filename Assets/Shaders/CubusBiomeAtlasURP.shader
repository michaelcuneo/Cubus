Shader "Cubus/BiomeAtlasURP"
{
  Properties
  {
    _Atlas("Biome Texture Atlas", 2D) = "white" {}
    _TopLookup("Top Tile Lookup", 2D) = "black" {}
    _SideLookup("Side Tile Lookup", 2D) = "black" {}
    _BottomLookup("Bottom Tile Lookup", 2D) = "black" {}
    _PropsLookup("Props Lookup", 2D) = "black" {}
    _AtlasGrid("Atlas Grid (X Columns, Y Rows)", Vector) = (4, 4, 0, 0)
    _Tint("Tint", Color) = (1, 1, 1, 1)
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

      TEXTURE2D(_TopLookup);
      SAMPLER(sampler_TopLookup);

      TEXTURE2D(_SideLookup);
      SAMPLER(sampler_SideLookup);

      TEXTURE2D(_BottomLookup);
      SAMPLER(sampler_BottomLookup);

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
        float2 uv : TEXCOORD2;
        float4 color : TEXCOORD3;
      };

      CBUFFER_START(UnityPerMaterial)
      float4 _AtlasGrid;
      float4 _Tint;
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
        return DecodeU16(color.rg);
      }

      float SampleLookupTileId(
          TEXTURE2D_PARAM(lookupTex, lookupSampler),
          float materialId)
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

        if (n.y > 0.5)
        {
          return SampleLookupTileId(
              TEXTURE2D_ARGS(_TopLookup, sampler_TopLookup),
              materialId
          );
        }

        if (n.y < -0.5)
        {
          return SampleLookupTileId(
              TEXTURE2D_ARGS(_BottomLookup, sampler_BottomLookup),
              materialId
          );
        }

        return SampleLookupTileId(
            TEXTURE2D_ARGS(_SideLookup, sampler_SideLookup),
            materialId
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
        OUT.uv = IN.uv;
        OUT.color = IN.color;

        return OUT;
      }

      half4 frag(Varyings IN) : SV_Target
      {
        float materialId = max(1.0, DecodeMaterialId(IN.color));
        float tileId = SelectTileId(normalize(IN.normalWS), materialId);
        float renderCategory = SampleRenderCategory(materialId);

        float columns = max(1.0, floor(_AtlasGrid.x + 0.5));
        float rows = max(1.0, floor(_AtlasGrid.y + 0.5));
        float totalTiles = max(1.0, columns * rows);

        float tile = fmod(tileId - 1.0, totalTiles);
        float sourceRowTopToBottom = floor(tile / columns);
        float row = rows - 1.0 - sourceRowTopToBottom;
        float col = tile - (sourceRowTopToBottom * columns);

        float2 atlasGrid = float2(columns, rows);
        float2 unwrappedTileUv = IN.uv;
        float2 tileUv = frac(unwrappedTileUv);

        float2 atlasUv = (tileUv + float2(col, row)) / atlasGrid;

        float2 atlasDx = ddx(unwrappedTileUv) / atlasGrid;
        float2 atlasDy = ddy(unwrappedTileUv) / atlasGrid;

        half4 albedo = SAMPLE_TEXTURE2D_GRAD(
            _Atlas,
            sampler_Atlas,
            atlasUv,
            atlasDx,
            atlasDy
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