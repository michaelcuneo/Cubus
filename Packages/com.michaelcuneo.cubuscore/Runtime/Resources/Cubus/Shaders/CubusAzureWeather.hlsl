#ifndef CUBUS_AZURE_WEATHER_INCLUDED
#define CUBUS_AZURE_WEATHER_INCLUDED

// Compatibility shim retained temporarily so existing Cubus shaders compile
// without depending on Azure Sky shader globals or duplicating Azure's URP
// renderer effects. All functions now preserve normal URP lighting output.

void CubusApplyGroundWeather(
  inout float3 albedoRgb,
  inout float roughness,
  float3 positionWS,
  float3 normalWS)
{
}

float CubusWeatherSpecularMultiplier(float3 positionWS, float3 normalWS)
{
  return 1.0;
}

float3 CubusAzureDirectLightColor(float3 mainLightColor)
{
  return mainLightColor;
}

float3 CubusAzureAmbientLight(float3 ambient)
{
  return ambient;
}

float CubusAzureLightFlashMultiplier()
{
  return 1.0;
}

float CubusCloudShadow(float3 positionWS, float3 normalWS)
{
  return 1.0;
}

float4 CubusAzureComputeFogScattering(float3 worldPos)
{
  return float4(0.0, 0.0, 0.0, 0.0);
}

float3 CubusApplyAzureFog(float3 color, float3 worldPos)
{
  return color;
}

#endif
