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

        lit = MixFog(lit, IN.fogCoord);

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

    Pass
    {
      Name "DepthNormals"
      Tags { "LightMode" = "DepthNormals" }

      ZWrite On
      Cull Off

      HLSLPROGRAM
      #pragma vertex DepthNormalsVert
      #pragma fragment DepthNormalsFrag

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

      struct DepthNormalsAttributes
      {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
        float2 uv : TEXCOORD0;
        float2 wind : TEXCOORD1;
      };

      struct DepthNormalsVaryings
      {
        float4 positionCS : SV_POSITION;
        float3 normalWS : TEXCOORD0;
        float2 uv : TEXCOORD1;
      };

      DepthNormalsVaryings DepthNormalsVert(DepthNormalsAttributes IN)
      {
        DepthNormalsVaryings OUT;
        float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
        positionWS = ApplyWind(positionWS, IN.wind.x, IN.wind.y);
        OUT.positionCS = TransformWorldToHClip(positionWS);
        OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
        OUT.uv = TRANSFORM_TEX(IN.uv, _FoliageAtlas);
        return OUT;
      }

      half4 DepthNormalsFrag(DepthNormalsVaryings IN) : SV_Target
      {
        half a = SAMPLE_TEXTURE2D(_FoliageAtlas, sampler_FoliageAtlas, IN.uv).a;
        clip(a - _Cutoff);
        return half4(normalize(IN.normalWS), 0.0);
      }

      ENDHLSL
    }
  }

  FallBack "Universal Render Pipeline/Lit"
}
