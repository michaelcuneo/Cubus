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
        return SampleLookupTileId(
            TEXTURE2D_ARGS(_TopLookup, sampler_TopLookup),
            materialId
        );
      }

      // ---- Azure[Sky] atmospheric fog ----------------------------------------
      // These are GLOBAL shader uniforms published by the Azure[Sky] Dynamic
      // Skybox weather system (set via Shader.SetGlobal*). We read them directly
      // so the terrain shares the exact same atmospheric scattering / fog as the
      // sky, without taking a hard #include dependency on the Azure plugin. When
      // Azure is not running these globals are zero and we fall back to Unity's
      // MixFog (see frag). Declared outside UnityPerMaterial because they are
      // engine globals, not per-material properties.
      #define AZURE_PI 3.1415926535f
      #define AZURE_PI316 0.0596831f
      #define AZURE_PI14 0.07957747f

      float3   _Azure_SunDirection;
      float3   _Azure_MoonDirection;
      float4x4 _Azure_UpDirectionMatrix;

      float  _Azure_MieDistance;
      float  _Azure_Kr;
      float  _Azure_Km;
      float3 _Azure_Rayleigh;
      float3 _Azure_Mie;
      float3 _Azure_MieG;
      float  _Azure_Scattering;
      float  _Azure_SkyLuminance;
      float  _Azure_Exposure;
      float4 _Azure_RayleighColor;
      float4 _Azure_MieColor;

      float _Azure_GlobalFogDistance;
      float _Azure_GlobalFogSmooth;
      float _Azure_GlobalFogDensity;
      float _Azure_HeightFogDistance;
      float _Azure_HeightFogSmooth;
      float _Azure_HeightFogDensity;
      float _Azure_HeightFogStartAltitude;
      float _Azure_HeightFogEndAltitude;
      float _Azure_FogBluishIntensity;
      float _Azure_HeightFogScatterMultiplier;

      // Ported from Azure's AzureFogCore.cginc ComputeFogScatteringColor.
      // Returns rgb = atmospheric scattering colour, a = total fog amount.
      float4 AzureComputeFogScattering(float3 worldPos)
      {
        float dist = distance(_WorldSpaceCameraPos, worldPos);
        float depth = dist * _ProjectionParams.w;
        float mieDepth = saturate(lerp(dist * (_ProjectionParams.z / 10000.0f), dist * (_ProjectionParams.z / 1000.0f), _Azure_MieDistance));

        // Global fog
        float globalFog = smoothstep(-_Azure_GlobalFogSmooth, 1.25, dist / _Azure_GlobalFogDistance) * _Azure_GlobalFogDensity;

        // Height fog
        float heightFogDistance = smoothstep(-_Azure_HeightFogSmooth, 1.25f, dist / _Azure_HeightFogDistance);
        float3 worldSpaceDirection = mul((float3x3)_Azure_UpDirectionMatrix, worldPos.xyz);
        float heightFog = saturate((worldSpaceDirection.y - _Azure_HeightFogStartAltitude) / (_Azure_HeightFogEndAltitude + _Azure_HeightFogStartAltitude));
        heightFog = 1.0f - heightFog;
        heightFog *= heightFog;
        heightFog *= heightFogDistance;
        heightFog *= _Azure_HeightFogDensity;

        // Total fog
        float totalFog = saturate(globalFog + heightFog);

        // Directions
        float3 viewDir = (_WorldSpaceCameraPos - worldPos) * -1.0f;
        viewDir = normalize(mul((float3x3)_Azure_UpDirectionMatrix, viewDir.xyz));
        float sunCosTheta = dot(viewDir, _Azure_SunDirection);
        float moonCosTheta = dot(viewDir, _Azure_MoonDirection);
        float skyCosTheta = dot(viewDir, float3(0.0f, -1.0f, 0.0f));
        float r = length(float3(0.0, 50.0, 0.0));
        float sunRise = saturate(dot(float3(0.0, 500.0, 0.0), _Azure_SunDirection) / r);
        float moonRise = saturate(dot(float3(0.0, 500.0, 0.0), _Azure_MoonDirection) / r);
        float sunDot = dot(float3(0.0f, 1.0f, 0.0f), _Azure_SunDirection);
        float moonDot = dot(float3(0.0f, 1.0f, 0.0f), _Azure_MoonDirection);

        // Optical depth
        float zenith1 = acos(saturate(dot(float3(0.0f, 1.0f, 0.0f), viewDir) * mieDepth));
        float zenith2 = acos(saturate(max(0.0f, viewDir.y) + (1.0f - depth) * _Azure_FogBluishIntensity));
        float zenith = lerp(zenith1, zenith2, saturate(lerp(moonDot, sunDot, sunRise)));
        float z = cos(zenith) + 0.15f * pow(93.885f - ((zenith * 180.0f) / AZURE_PI), -1.253f);
        float SR = _Azure_Kr / z;
        float SM = _Azure_Km / z;

        // Extinction
        float3 fex = exp(-(_Azure_Rayleigh * SR + _Azure_Mie * SM));

        // Default sky (no sun or moon up)
        float3 Esun = 1.0f - fex;
        float  rayPhase = 2.0f + 0.5f * pow(skyCosTheta, 2.0f);
        float3 BrTheta = AZURE_PI316 * _Azure_Rayleigh * rayPhase * _Azure_RayleighColor.rgb;
        float3 BrmTheta = BrTheta / (_Azure_Rayleigh + _Azure_Mie);
        float3 defaultDayLight = BrmTheta * Esun * _Azure_Scattering * _Azure_SkyLuminance * (1.0f - fex);
        defaultDayLight *= 1.0f - sunRise;
        defaultDayLight *= 1.0f - moonRise;

        // Sun inScattering
        Esun = lerp(fex, (1.0f - fex), sunDot);
        rayPhase = 2.0f + 0.5f * pow(sunCosTheta, 2.0f);
        float  miePhase = _Azure_MieG.x / pow(_Azure_MieG.y - _Azure_MieG.z * sunCosTheta, 1.5f);
        BrTheta = AZURE_PI316 * _Azure_Rayleigh * rayPhase * _Azure_RayleighColor.rgb;
        float3 BmTheta = AZURE_PI14 * _Azure_Mie * miePhase * _Azure_MieColor.rgb * mieDepth;
        BrmTheta = (BrTheta + BmTheta) / (_Azure_Rayleigh + _Azure_Mie);
        float3 sunInScatter = BrmTheta * Esun * _Azure_Scattering * (1.0f - fex);
        sunInScatter *= sunRise;

        // Moon inScattering
        Esun = 1.0f - fex;
        rayPhase = 2.0f + 0.5f * pow(moonCosTheta, 2.0f);
        miePhase = _Azure_MieG.x / pow(_Azure_MieG.y - _Azure_MieG.z * moonCosTheta, 1.5f);
        BrTheta = AZURE_PI316 * _Azure_Rayleigh * rayPhase * _Azure_RayleighColor.rgb;
        BmTheta = AZURE_PI14 * _Azure_Mie * miePhase * _Azure_MieColor.rgb * mieDepth;
        BrmTheta = (BrTheta + BmTheta) / (_Azure_Rayleigh + _Azure_Mie);
        float3 moonInScatter = BrmTheta * Esun * _Azure_Scattering * 0.1f * (1.0f - fex);
        moonInScatter *= moonRise;
        moonInScatter *= 1.0f - sunRise;

        // Output
        float3 outputColor = defaultDayLight + sunInScatter + moonInScatter;
        outputColor += heightFog * _Azure_HeightFogScatterMultiplier;

        // Tonemapping
        outputColor = saturate(1.0f - exp(-_Azure_Exposure * outputColor));

        #ifndef UNITY_COLORSPACE_GAMMA
        outputColor = pow(outputColor, 2.2f);
        #endif

        return float4(outputColor, totalFog);
      }

      // Opaque fog blend (fragment alpha == 1).
      float3 AzureApplyFog(float3 color, float3 worldPos)
      {
        float4 fogData = AzureComputeFogScattering(worldPos);
        return lerp(color, fogData.rgb, fogData.a);
      }

      float3 ApplyLighting(float3 albedoRgb, float3 positionWS, float3 normalWS)
      {
        float3 n = normalize(normalWS);

        // Receive the main directional light (the Azure sun/moon) with shadows.
        float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
        Light mainLight = GetMainLight(shadowCoord);

        float ndl = saturate(dot(n, mainLight.direction));
        float3 direct = mainLight.color * (ndl * mainLight.shadowAttenuation);

        // Dynamic environment ambient driven by Azure's sky (spherical harmonics)
        // instead of a flat constant, so terrain colour tracks the time of day.
        float3 ambient = SampleSH(n);

        return albedoRgb * (ambient + direct);
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

        // Inset the in-tile UV by half a texel so bilinear filtering never
        // samples across the tile boundary into a neighbouring atlas tile.
        // Without this, every voxel face draws a thin seam of the adjacent
        // tile's colour around its edges.
        float2 tilePixelSize = max(_Atlas_TexelSize.zw / atlasGrid, 1.0);
        float2 halfTexelInTile = 0.5 / tilePixelSize;
        tileUv = clamp(tileUv, halfTexelInTile, 1.0 - halfTexelInTile);

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

        float3 lit = ApplyLighting(albedo.rgb * _Tint.rgb, IN.positionWS, IN.normalWS);

        // Prefer Azure's atmospheric scattering so the horizon matches the sky
        // exactly. _Azure_GlobalFogDistance is only > 0 when the Azure weather
        // system is live; otherwise fall back to URP's standard fog.
        if (_Azure_GlobalFogDistance > 0.0)
        {
          lit = AzureApplyFog(lit, IN.positionWS);
        }
        else
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

      CBUFFER_START(UnityPerMaterial)
      float4 _AtlasGrid;
      float4 _Tint;
      float4 _Atlas_TexelSize;
      CBUFFER_END

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

      CBUFFER_START(UnityPerMaterial)
      float4 _AtlasGrid;
      float4 _Tint;
      float4 _Atlas_TexelSize;
      CBUFFER_END

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