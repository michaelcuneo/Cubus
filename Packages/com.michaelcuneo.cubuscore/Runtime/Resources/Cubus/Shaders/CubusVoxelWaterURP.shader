Shader "Cubus/Voxel Water URP"
{
    Properties
    {
        _BaseColor("Shallow Color", Color) = (0.10, 0.50, 0.58, 0.38)
        _DeepColor("Deep Color", Color) = (0.015, 0.14, 0.20, 0.58)
        _FoamColor("Shore Foam Color", Color) = (0.78, 0.92, 0.86, 0.55)
        _Smoothness("Smoothness", Range(0, 1)) = 0.72
        _WaveScale("Wave Scale", Float) = 0.18
        _WaveSpeed("Wave Speed", Float) = 0.42
        _WaveStrength("Wave Strength", Range(0, 1)) = 0.11
        _WaveHeight("Wave Height", Range(0, 0.25)) = 0.035
        _FresnelPower("Fresnel Power", Range(0.5, 8)) = 4.0
        _DepthFade("Depth Fade", Range(0.01, 20)) = 1.0
        _ShoreFoamStrength("Shore Foam Strength", Range(0, 1)) = 0.48
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _DeepColor;
                half4 _FoamColor;
                half _Smoothness;
                float _WaveScale;
                float _WaveSpeed;
                half _WaveStrength;
                half _WaveHeight;
                half _FresnelPower;
                half _DepthFade;
                half _ShoreFoamStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 waterData : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half2 waterData : TEXCOORD3;
                half fogFactor : TEXCOORD4;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                half depth = saturate(input.waterData.x);
                half shore = saturate(input.waterData.y);
                float2 p = input.positionOS.xz * _WaveScale;
                float t = _Time.y * _WaveSpeed;
                float wave = (sin(p.x + t) + sin((p.x + p.y) * 0.61 - t * 0.83)) * 0.5;
                float waveMask = depth * (1.0h - shore * 0.85h);
                float3 positionOS = input.positionOS.xyz;
                positionOS.y += wave * _WaveHeight * waveMask;

                VertexPositionInputs positionInputs = GetVertexPositionInputs(positionOS);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.uv = input.uv;
                output.waterData = half2(depth, shore);
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.positionWS.xz * _WaveScale;
                float t = _Time.y * _WaveSpeed;
                half depth = saturate(input.waterData.x * _DepthFade);
                half shore = saturate(input.waterData.y);

                float waveA = sin(p.x + t) * cos(p.y * 0.73 - t * 1.17);
                float waveB = sin((p.x + p.y) * 0.61 - t * 0.83);
                float ripple = sin((p.x * 2.7 + p.y * 1.9) - t * 2.15) * sin(p.y * 3.1 + t * 1.37);

                half3 normalWS = normalize(input.normalWS + half3(
                    (waveA + ripple * 0.22) * _WaveStrength,
                    0.0h,
                    (waveB - ripple * 0.18) * _WaveStrength));

                half3 viewDirWS = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half fresnel = pow(1.0h - saturate(dot(normalWS, viewDirWS)), _FresnelPower);

                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half3 diffuse = mainLight.color * (0.22h + ndotl * 0.28h);

                half3 halfDir = SafeNormalize(mainLight.direction + viewDirWS);
                half specular = pow(saturate(dot(normalWS, halfDir)), lerp(16.0h, 128.0h, _Smoothness));

                half foamNoise = saturate(ripple * 0.5h + 0.5h);
                half foam = shore * _ShoreFoamStrength * smoothstep(0.32h, 0.82h, foamNoise);
                half3 color = lerp(_BaseColor.rgb, _DeepColor.rgb, depth);
                color = lerp(color, _FoamColor.rgb, foam * _FoamColor.a);
                color *= diffuse + 0.52h;
                color += mainLight.color * specular * lerp(0.22h, 0.48h, depth) * (1.0h - shore * 0.55h);
                half surfaceSheen = saturate(fresnel * 0.65h + specular * 0.22h + shore * 0.08h);
                color = lerp(color, color + half3(0.10h, 0.16h, 0.17h), surfaceSheen * (1.0h - foam * 0.5h));

                color = MixFog(color, input.fogFactor);

                half alpha = saturate(lerp(_BaseColor.a, _DeepColor.a, depth) + fresnel * 0.07h + foam * 0.12h);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
