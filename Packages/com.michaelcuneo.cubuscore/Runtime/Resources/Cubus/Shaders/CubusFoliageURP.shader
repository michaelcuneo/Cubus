Shader "Cubus/FoliageURP"
{
  Properties
  {
    _FoliageAtlas("Foliage Atlas", 2D) = "white" {}
    _Tint("Tint", Color) = (1, 1, 1, 1)
    _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.4

    [Header(Wind)]
    _WindStrength("Wind Strength", Range(0, 1)) = 0.12
    _WindSpeed("Wind Speed", Range(0, 10)) = 1.5
    _WindFrequency("Wind Frequency", Range(0, 2)) = 0.15
    _WindDirX("Wind Direction X", Range(-1, 1)) = 1.0
    _WindDirZ("Wind Direction Z", Range(-1, 1)) = 0.35

    [Header(Lighting)]
    _AmbientBoost("Ambient Boost", Range(0, 2)) = 1.0
  }

  SubShader
  {
    Tags
    {
      "RenderType" = "TransparentCutout"
      "RenderPipeline" = "UniversalPipeline"
      "Queue" = "AlphaTest"
      "IgnoreProjector" = "True"
    }

    LOD 100

    Pass
    {
      Name "ForwardLit"
      Tags { "LightMode" = "UniversalForward" }

      Cull Off
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

      TEXTURE2D(_FoliageAtlas);
      SAMPLER(sampler_FoliageAtlas);

      struct Attributes
      {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
        float4 color : COLOR;
        float2 uv : TEXCOORD0;
        float2 wind : TEXCOORD1; // x = sway height factor, y = per-cluster phase
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
      float4 _Tint;
      float4 _FoliageAtlas_ST;
      float _Cutoff;
      float _WindStrength;
      float _WindSpeed;
      float _WindFrequency;
      float _WindDirX;
      float _WindDirZ;
      float _AmbientBoost;
      CBUFFER_END

      // ---- Azure[Sky] atmospheric fog ----------------------------------------
      // GLOBAL uniforms published by the Azure[Sky] Dynamic Skybox (set via
      // Shader.SetGlobal*). Shared verbatim with Cubus/BiomeAtlasURP so foliage
      // dissolves into the same horizon as the terrain. Zero when Azure is not
      // running -> we fall back to URP MixFog.
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

      float4 AzureComputeFogScattering(float3 worldPos)
      {
        float dist = distance(_WorldSpaceCameraPos, worldPos);
        float depth = dist * _ProjectionParams.w;
        float mieDepth = saturate(lerp(dist * (_ProjectionParams.z / 10000.0f), dist * (_ProjectionParams.z / 1000.0f), _Azure_MieDistance));

        float globalFog = smoothstep(-_Azure_GlobalFogSmooth, 1.25, dist / _Azure_GlobalFogDistance) * _Azure_GlobalFogDensity;

        float heightFogDistance = smoothstep(-_Azure_HeightFogSmooth, 1.25f, dist / _Azure_HeightFogDistance);
        float3 worldSpaceDirection = mul((float3x3)_Azure_UpDirectionMatrix, worldPos.xyz);
        float heightFog = saturate((worldSpaceDirection.y - _Azure_HeightFogStartAltitude) / (_Azure_HeightFogEndAltitude + _Azure_HeightFogStartAltitude));
        heightFog = 1.0f - heightFog;
        heightFog *= heightFog;
        heightFog *= heightFogDistance;
        heightFog *= _Azure_HeightFogDensity;

        float totalFog = saturate(globalFog + heightFog);

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

        float zenith1 = acos(saturate(dot(float3(0.0f, 1.0f, 0.0f), viewDir) * mieDepth));
        float zenith2 = acos(saturate(max(0.0f, viewDir.y) + (1.0f - depth) * _Azure_FogBluishIntensity));
        float zenith = lerp(zenith1, zenith2, saturate(lerp(moonDot, sunDot, sunRise)));
        float z = cos(zenith) + 0.15f * pow(93.885f - ((zenith * 180.0f) / AZURE_PI), -1.253f);
        float SR = _Azure_Kr / z;
        float SM = _Azure_Km / z;

        float3 fex = exp(-(_Azure_Rayleigh * SR + _Azure_Mie * SM));

        float3 Esun = 1.0f - fex;
        float  rayPhase = 2.0f + 0.5f * pow(skyCosTheta, 2.0f);
        float3 BrTheta = AZURE_PI316 * _Azure_Rayleigh * rayPhase * _Azure_RayleighColor.rgb;
        float3 BrmTheta = BrTheta / (_Azure_Rayleigh + _Azure_Mie);
        float3 defaultDayLight = BrmTheta * Esun * _Azure_Scattering * _Azure_SkyLuminance * (1.0f - fex);
        defaultDayLight *= 1.0f - sunRise;
        defaultDayLight *= 1.0f - moonRise;

        Esun = lerp(fex, (1.0f - fex), sunDot);
        rayPhase = 2.0f + 0.5f * pow(sunCosTheta, 2.0f);
        float  miePhase = _Azure_MieG.x / pow(_Azure_MieG.y - _Azure_MieG.z * sunCosTheta, 1.5f);
        BrTheta = AZURE_PI316 * _Azure_Rayleigh * rayPhase * _Azure_RayleighColor.rgb;
        float3 BmTheta = AZURE_PI14 * _Azure_Mie * miePhase * _Azure_MieColor.rgb * mieDepth;
        BrmTheta = (BrTheta + BmTheta) / (_Azure_Rayleigh + _Azure_Mie);
        float3 sunInScatter = BrmTheta * Esun * _Azure_Scattering * (1.0f - fex);
        sunInScatter *= sunRise;

        Esun = 1.0f - fex;
        rayPhase = 2.0f + 0.5f * pow(moonCosTheta, 2.0f);
        miePhase = _Azure_MieG.x / pow(_Azure_MieG.y - _Azure_MieG.z * moonCosTheta, 1.5f);
        BrTheta = AZURE_PI316 * _Azure_Rayleigh * rayPhase * _Azure_RayleighColor.rgb;
        BmTheta = AZURE_PI14 * _Azure_Mie * miePhase * _Azure_MieColor.rgb * mieDepth;
        BrmTheta = (BrTheta + BmTheta) / (_Azure_Rayleigh + _Azure_Mie);
        float3 moonInScatter = BrmTheta * Esun * _Azure_Scattering * 0.1f * (1.0f - fex);
        moonInScatter *= moonRise;
        moonInScatter *= 1.0f - sunRise;

        float3 outputColor = defaultDayLight + sunInScatter + moonInScatter;
        outputColor += heightFog * _Azure_HeightFogScatterMultiplier;
        outputColor = saturate(1.0f - exp(-_Azure_Exposure * outputColor));

        #ifndef UNITY_COLORSPACE_GAMMA
        outputColor = pow(outputColor, 2.2f);
        #endif

        return float4(outputColor, totalFog);
      }

      float3 AzureApplyFog(float3 color, float3 worldPos)
      {
        float4 fogData = AzureComputeFogScattering(worldPos);
        return lerp(color, fogData.rgb, fogData.a);
      }

      // World-space wind displacement. swayHeight is 0 at the rooted base and
      // grows toward the tip (it already bakes per-profile sway strength), so
      // blades bend from the ground up and never tear free of the surface.
      float3 ApplyWind(float3 positionWS, float swayHeight, float phase)
      {
        float t = _Time.y * _WindSpeed;
        float wave = sin(t + phase * 6.2831853 + positionWS.x * _WindFrequency + positionWS.z * _WindFrequency);
        float gust = sin(t * 0.37 + phase * 3.0) * 0.5;
        float2 dir = float2(_WindDirX, _WindDirZ);
        float amp = (wave + gust) * swayHeight * _WindStrength;
        positionWS.x += dir.x * amp;
        positionWS.z += dir.y * amp;
        // Slight vertical bob so motion does not look like a flat slide.
        positionWS.y += abs(amp) * -0.15;
        return positionWS;
      }

      float3 ApplyFoliageLighting(float3 albedo, float3 positionWS, float3 normalWS)
      {
        float3 n = normalize(normalWS);
        float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
        Light mainLight = GetMainLight(shadowCoord);
        float atten = mainLight.shadowAttenuation;

        // Half-lambert wrap keeps foliage soft and readable on both faces.
        float ndl = saturate(dot(n, mainLight.direction)) * 0.5 + 0.5;
        float3 direct = mainLight.color * (ndl * atten);
        float3 ambient = SampleSH(n) * _AmbientBoost;
        return albedo * (ambient + direct);
      }

      Varyings vert(Attributes IN)
      {
        Varyings OUT;

        float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
        positionWS = ApplyWind(positionWS, IN.wind.x, IN.wind.y);

        OUT.positionWS = positionWS;
        OUT.positionCS = TransformWorldToHClip(positionWS);
        OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
        OUT.uv = TRANSFORM_TEX(IN.uv, _FoliageAtlas);
        OUT.color = IN.color;
        OUT.fogCoord = ComputeFogFactor(OUT.positionCS.z);

        return OUT;
      }

      half4 frag(Varyings IN) : SV_Target
      {
        half4 albedo = SAMPLE_TEXTURE2D(_FoliageAtlas, sampler_FoliageAtlas, IN.uv);

        clip(albedo.a - _Cutoff);

        float3 baseColor = albedo.rgb * IN.color.rgb * _Tint.rgb;
        float3 lit = ApplyFoliageLighting(baseColor, IN.positionWS, IN.normalWS);

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
      Cull Off

      HLSLPROGRAM
      #pragma vertex ShadowVert
      #pragma fragment ShadowFrag
      #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

      TEXTURE2D(_FoliageAtlas);
      SAMPLER(sampler_FoliageAtlas);

      CBUFFER_START(UnityPerMaterial)
      float4 _Tint;
      float4 _FoliageAtlas_ST;
      float _Cutoff;
      float _WindStrength;
      float _WindSpeed;
      float _WindFrequency;
      float _WindDirX;
      float _WindDirZ;
      float _AmbientBoost;
      CBUFFER_END

      float3 _LightDirection;
      float3 _LightPosition;

      float3 ApplyWind(float3 positionWS, float swayHeight, float phase)
      {
        float t = _Time.y * _WindSpeed;
        float wave = sin(t + phase * 6.2831853 + positionWS.x * _WindFrequency + positionWS.z * _WindFrequency);
        float gust = sin(t * 0.37 + phase * 3.0) * 0.5;
        float2 dir = float2(_WindDirX, _WindDirZ);
        float amp = (wave + gust) * swayHeight * _WindStrength;
        positionWS.x += dir.x * amp;
        positionWS.z += dir.y * amp;
        positionWS.y += abs(amp) * -0.15;
        return positionWS;
      }

      struct ShadowAttributes
      {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
        float2 uv : TEXCOORD0;
        float2 wind : TEXCOORD1;
      };

      struct ShadowVaryings
      {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
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
        positionWS = ApplyWind(positionWS, IN.wind.x, IN.wind.y);
        float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
        OUT.positionCS = GetShadowClipPosition(positionWS, normalWS);
        OUT.uv = TRANSFORM_TEX(IN.uv, _FoliageAtlas);
        return OUT;
      }

      half4 ShadowFrag(ShadowVaryings IN) : SV_Target
      {
        half a = SAMPLE_TEXTURE2D(_FoliageAtlas, sampler_FoliageAtlas, IN.uv).a;
        clip(a - _Cutoff);
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
      Cull Off

      HLSLPROGRAM
      #pragma vertex DepthVert
      #pragma fragment DepthFrag

      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

      TEXTURE2D(_FoliageAtlas);
      SAMPLER(sampler_FoliageAtlas);

      CBUFFER_START(UnityPerMaterial)
      float4 _Tint;
      float4 _FoliageAtlas_ST;
      float _Cutoff;
      float _WindStrength;
      float _WindSpeed;
      float _WindFrequency;
      float _WindDirX;
      float _WindDirZ;
      float _AmbientBoost;
      CBUFFER_END

      float3 ApplyWind(float3 positionWS, float swayHeight, float phase)
      {
        float t = _Time.y * _WindSpeed;
        float wave = sin(t + phase * 6.2831853 + positionWS.x * _WindFrequency + positionWS.z * _WindFrequency);
        float gust = sin(t * 0.37 + phase * 3.0) * 0.5;
        float2 dir = float2(_WindDirX, _WindDirZ);
        float amp = (wave + gust) * swayHeight * _WindStrength;
        positionWS.x += dir.x * amp;
        positionWS.z += dir.y * amp;
        positionWS.y += abs(amp) * -0.15;
        return positionWS;
      }

      struct DepthAttributes
      {
        float4 positionOS : POSITION;
        float2 uv : TEXCOORD0;
        float2 wind : TEXCOORD1;
      };

      struct DepthVaryings
      {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
      };

      DepthVaryings DepthVert(DepthAttributes IN)
      {
        DepthVaryings OUT;
        float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
        positionWS = ApplyWind(positionWS, IN.wind.x, IN.wind.y);
        OUT.positionCS = TransformWorldToHClip(positionWS);
        OUT.uv = TRANSFORM_TEX(IN.uv, _FoliageAtlas);
        return OUT;
      }

      half DepthFrag(DepthVaryings IN) : SV_Target
      {
        half a = SAMPLE_TEXTURE2D(_FoliageAtlas, sampler_FoliageAtlas, IN.uv).a;
        clip(a - _Cutoff);
        return 0;
      }

      ENDHLSL
    }
  }

  FallBack "Universal Render Pipeline/Lit"
}
