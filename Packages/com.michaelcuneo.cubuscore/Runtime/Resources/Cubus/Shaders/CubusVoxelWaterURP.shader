Shader "Cubus/Voxel Water URP"
{
    Properties
    {
        _BaseColor("Shallow Color", Color) = (0.055, 0.32, 0.48, 0.72)
        _DeepColor("Deep Color", Color) = (0.015, 0.08, 0.16, 0.92)
        _Smoothness("Smoothness", Range(0, 1)) = 0.88
        _WaveScale("Wave Scale", Float) = 0.12
        _WaveSpeed("Wave Speed", Float) = 0.35
        _WaveStrength("Wave Strength", Range(0, 1)) = 0.18
        _FresnelPower("Fresnel Power", Range(0.5, 8)) = 4.0
        _DepthFade("Depth Fade", Range(0.01, 20)) = 3.5
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
                half _Smoothness;
                float _WaveScale;
                float _WaveSpeed;
                half _WaveStrength;
                half _FresnelPower;
                half _DepthFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.positionWS.xz * _WaveScale;
                float t = _Time.y * _WaveSpeed;

                float waveA = sin(p.x + t) * cos(p.y * 0.73 - t * 1.17);
                float waveB = sin((p.x + p.y) * 0.61 - t * 0.83);

                half3 normalWS = normalize(input.normalWS + half3(
                    waveA * _WaveStrength,
                    0.0h,
                    waveB * _WaveStrength));

                half3 viewDirWS = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half fresnel = pow(1.0h - saturate(dot(normalWS, viewDirWS)), _FresnelPower);

                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half3 diffuse = mainLight.color * (0.28h + ndotl * 0.32h);

                half3 halfDir = SafeNormalize(mainLight.direction + viewDirWS);
                half specular = pow(saturate(dot(normalWS, halfDir)), lerp(16.0h, 128.0h, _Smoothness));

                half depthTint = saturate(abs(input.positionWS.y) / max(0.01h, _DepthFade));
                half3 color = lerp(_BaseColor.rgb, _DeepColor.rgb, depthTint * 0.25h);
                color *= diffuse + 0.45h;
                color += mainLight.color * specular * 0.55h;
                color = lerp(color, color + 0.18h, fresnel);
                color = MixFog(color, input.fogFactor);

                half alpha = saturate(lerp(_BaseColor.a, _DeepColor.a, depthTint * 0.2h) + fresnel * 0.12h);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
