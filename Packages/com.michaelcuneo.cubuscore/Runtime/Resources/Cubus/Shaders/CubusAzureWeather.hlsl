#ifndef CUBUS_AZURE_WEATHER_INCLUDED
#define CUBUS_AZURE_WEATHER_INCLUDED

#define CUBUS_AZURE_PI 3.1415926535f
#define CUBUS_AZURE_PI316 0.0596831f
#define CUBUS_AZURE_PI14 0.07957747f

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
float4 _Azure_SunColor;
float4 _Azure_MoonColor;
float _Azure_SkyExtinction;

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
float3 _Azure_DynamicCloudDirection;
float _Azure_DynamicCloudDensity;
float4 _Azure_DynamicCloudColor1;
float4 _Azure_DynamicCloudColor2;
float _Azure_ThunderLightningEffect;

float _CubusWeatherRainIntensity;
float _CubusWeatherWetness;
float _CubusWeatherPuddleStrength;
float _CubusWeatherGroundDarkening;
float _CubusWeatherSpecularBoost;
float _CubusWeatherCloudShadowStrength;
float _CubusWeatherCloudShadowScale;
float _CubusWeatherCloudShadowSpeed;
float _CubusWeatherCloudShadowContrast;

float CubusWeatherHash31(float3 p)
{
  p = frac(p * 0.1031);
  p += dot(p, p.yzx + 33.33);
  return frac((p.x + p.y) * p.z);
}

float CubusWeatherNoise(float3 p)
{
  float3 i = floor(p);
  float3 f = frac(p);
  f = f * f * (3.0 - 2.0 * f);

  float n000 = CubusWeatherHash31(i + float3(0, 0, 0));
  float n100 = CubusWeatherHash31(i + float3(1, 0, 0));
  float n010 = CubusWeatherHash31(i + float3(0, 1, 0));
  float n110 = CubusWeatherHash31(i + float3(1, 1, 0));
  float n001 = CubusWeatherHash31(i + float3(0, 0, 1));
  float n101 = CubusWeatherHash31(i + float3(1, 0, 1));
  float n011 = CubusWeatherHash31(i + float3(0, 1, 1));
  float n111 = CubusWeatherHash31(i + float3(1, 1, 1));

  float nx00 = lerp(n000, n100, f.x);
  float nx10 = lerp(n010, n110, f.x);
  float nx01 = lerp(n001, n101, f.x);
  float nx11 = lerp(n011, n111, f.x);

  float nxy0 = lerp(nx00, nx10, f.y);
  float nxy1 = lerp(nx01, nx11, f.y);
  return lerp(nxy0, nxy1, f.z);
}

float CubusWeatherUpMask(float3 normalWS)
{
  return saturate(normalize(normalWS).y * 1.35 - 0.12);
}

float CubusWeatherWetMask(float3 positionWS, float3 normalWS)
{
  float up = CubusWeatherUpMask(normalWS);
  float noise = CubusWeatherNoise(positionWS * 0.037 + float3(11.7, 3.1, 29.4));
  float puddles = smoothstep(0.45, 0.92, up) * smoothstep(0.42, 0.82, noise) * saturate(_CubusWeatherPuddleStrength);
  return saturate(_CubusWeatherWetness) * up * saturate(0.72 + puddles * 0.55);
}

void CubusApplyGroundWeather(inout float3 albedoRgb, inout float roughness, float3 positionWS, float3 normalWS)
{
  float wet = CubusWeatherWetMask(positionWS, normalWS);
  float darken = saturate(_CubusWeatherGroundDarkening) * wet;
  albedoRgb = lerp(albedoRgb, albedoRgb * 0.58, darken);
  roughness = lerp(roughness, roughness * 0.28, wet);
}

float CubusWeatherSpecularMultiplier(float3 positionWS, float3 normalWS)
{
  float wet = CubusWeatherWetMask(positionWS, normalWS);
  return 1.0 + wet * saturate(_CubusWeatherSpecularBoost) * 3.0;
}

float3 CubusAzureSafeColor(float4 colorValue, float3 fallback)
{
  float luma = dot(abs(colorValue.rgb), float3(0.2126, 0.7152, 0.0722));
  return luma > 0.0001 ? colorValue.rgb : fallback;
}

float3 CubusAzureDirectLightColor(float3 mainLightColor)
{
  float sunAbove = saturate(dot(float3(0.0, 1.0, 0.0), _Azure_SunDirection) * 0.5 + 0.5);
  float3 sunColor = CubusAzureSafeColor(_Azure_SunColor, 1.0.xxx);
  float3 moonColor = CubusAzureSafeColor(_Azure_MoonColor, 0.45.xxx);
  float3 celestialColor = lerp(moonColor, sunColor, sunAbove);
  float lightning = saturate(_Azure_ThunderLightningEffect);
  return lerp(mainLightColor, mainLightColor * celestialColor, 0.28) + lightning * 1.35.xxx;
}

float3 CubusAzureAmbientLight(float3 ambient)
{
  float cloudDensity = saturate(_Azure_DynamicCloudDensity);
  float3 cloudColorA = CubusAzureSafeColor(_Azure_DynamicCloudColor1, 0.62.xxx);
  float3 cloudColorB = CubusAzureSafeColor(_Azure_DynamicCloudColor2, cloudColorA);
  float3 cloudColor = lerp(cloudColorA, cloudColorB, 0.35);
  float skyExtinction = saturate(_Azure_SkyExtinction);
  float lightning = saturate(_Azure_ThunderLightningEffect);

  ambient = lerp(ambient, ambient * cloudColor, cloudDensity * 0.22);
  ambient *= lerp(1.0, 0.78, cloudDensity * saturate(_CubusWeatherCloudShadowStrength));
  ambient *= lerp(1.0, 0.82, skyExtinction);
  ambient += lightning * max(cloudColorB, 0.75.xxx) * 0.35;
  return ambient;
}

float CubusAzureLightFlashMultiplier()
{
  return 1.0 + saturate(_Azure_ThunderLightningEffect) * 1.65;
}

float CubusCloudShadow(float3 positionWS, float3 normalWS)
{
  float up = smoothstep(0.1, 0.75, normalize(normalWS).y);
  float2 direction = _Azure_DynamicCloudDirection.xz;
  direction = length(direction) > 0.001 ? normalize(direction) : float2(0.73, 0.68);

  float scale = max(0.00001, _CubusWeatherCloudShadowScale);
  float2 uv = positionWS.xz * scale + direction * (_Time.y * _CubusWeatherCloudShadowSpeed);
  float broad = CubusWeatherNoise(float3(uv, 13.71));
  float detail = CubusWeatherNoise(float3(uv * 2.7 + 41.3, 7.19));
  float cloud = saturate(broad * 0.72 + detail * 0.28);
  float density = saturate(max(_Azure_DynamicCloudDensity, _CubusWeatherCloudShadowStrength));
  float threshold = lerp(0.72, 0.38, density);
  float mask = smoothstep(threshold, threshold + 0.28, cloud);
  float strength = saturate(_CubusWeatherCloudShadowStrength) * saturate(_CubusWeatherCloudShadowContrast) * up;
  return lerp(1.0, 0.58, mask * strength);
}

float4 CubusAzureComputeFogScattering(float3 worldPos)
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
  float z = cos(zenith) + 0.15f * pow(93.885f - ((zenith * 180.0f) / CUBUS_AZURE_PI), -1.253f);
  float SR = _Azure_Kr / z;
  float SM = _Azure_Km / z;

  float3 fex = exp(-(_Azure_Rayleigh * SR + _Azure_Mie * SM));

  float3 Esun = 1.0f - fex;
  float rayPhase = 2.0f + 0.5f * pow(skyCosTheta, 2.0f);
  float3 BrTheta = CUBUS_AZURE_PI316 * _Azure_Rayleigh * rayPhase * _Azure_RayleighColor.rgb;
  float3 BrmTheta = BrTheta / (_Azure_Rayleigh + _Azure_Mie);
  float3 defaultDayLight = BrmTheta * Esun * _Azure_Scattering * _Azure_SkyLuminance * (1.0f - fex);
  defaultDayLight *= 1.0f - sunRise;
  defaultDayLight *= 1.0f - moonRise;

  Esun = lerp(fex, 1.0f - fex, sunDot);
  rayPhase = 2.0f + 0.5f * pow(sunCosTheta, 2.0f);
  float miePhase = _Azure_MieG.x / pow(_Azure_MieG.y - _Azure_MieG.z * sunCosTheta, 1.5f);
  BrTheta = CUBUS_AZURE_PI316 * _Azure_Rayleigh * rayPhase * _Azure_RayleighColor.rgb;
  float3 BmTheta = CUBUS_AZURE_PI14 * _Azure_Mie * miePhase * _Azure_MieColor.rgb * mieDepth;
  BrmTheta = (BrTheta + BmTheta) / (_Azure_Rayleigh + _Azure_Mie);
  float3 sunInScatter = BrmTheta * Esun * _Azure_Scattering * (1.0f - fex);
  sunInScatter *= sunRise;

  Esun = 1.0f - fex;
  rayPhase = 2.0f + 0.5f * pow(moonCosTheta, 2.0f);
  miePhase = _Azure_MieG.x / pow(_Azure_MieG.y - _Azure_MieG.z * moonCosTheta, 1.5f);
  BrTheta = CUBUS_AZURE_PI316 * _Azure_Rayleigh * rayPhase * _Azure_RayleighColor.rgb;
  BmTheta = CUBUS_AZURE_PI14 * _Azure_Mie * miePhase * _Azure_MieColor.rgb * mieDepth;
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

float3 CubusApplyAzureFog(float3 color, float3 worldPos)
{
  float4 fogData = CubusAzureComputeFogScattering(worldPos);
  return lerp(color, fogData.rgb, fogData.a);
}

#endif
