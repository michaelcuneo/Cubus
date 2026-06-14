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
    Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
    LOD 100

    Pass
    {
      Name "ForwardLit"
      Tags { "LightMode" = "UniversalForward" }
      Blend SrcAlpha OneMinusSrcAlpha
      ZWrite On

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

      float DecodeU16(float2 rg)
      {
        float low = round(saturate(rg.x) * 255.0);
        float high = round(saturate(rg.y) * 255.0);
        return low + (high * 256.0);
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

      float SelectTileId(float3 normalWS, float materialId)
      {
        return max(1.0, round(materialId));
      }

      float3 ApplyLighting(float3 albedoRgb, float3 normalWS)
      {
        float3 n = normalize(normalWS);
        Light mainLight = GetMainLight();
        float ndl = saturate(dot(n, mainLight.direction));
        float litFactor = 0.2 + ndl * 0.8;
        float3 color = albedoRgb * litFactor * mainLight.color;

        return color;
      }

      CBUFFER_START(UnityPerMaterial)
      float4 _AtlasGrid;
      float4 _Tint;
      CBUFFER_END

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

        float columns = max(1.0, floor(_AtlasGrid.x + 0.5));
        float rows = max(1.0, floor(_AtlasGrid.y + 0.5));
        float totalTiles = max(1.0, columns * rows);

        float tile = fmod(tileId - 1.0, totalTiles);
        float row = floor(tile / columns);
        float col = tile - (row * columns);

        float2 tileUv = frac(IN.uv);
        float2 atlasUv = (tileUv + float2(col, row)) / float2(columns, rows);

        half4 albedo = SAMPLE_TEXTURE2D(_Atlas, sampler_Atlas, atlasUv);
        float3 lit = ApplyLighting(albedo.rgb * _Tint.rgb, IN.normalWS);
        return half4(lit, albedo.a * _Tint.a);
      }
      ENDHLSL
    }
  }

  FallBack "Universal Render Pipeline/Lit"
}
