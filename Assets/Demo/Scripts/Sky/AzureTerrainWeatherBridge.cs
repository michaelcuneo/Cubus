using UnityEngine;
using UnityEngine.AzureSky;

namespace Assets.Demo.Scripts.Sky
{
    [AddComponentMenu("Cubus/Sky/Azure Terrain Weather Bridge")]
    public sealed class AzureTerrainWeatherBridge : MonoBehaviour
    {
        private static readonly int RainIntensityId = Shader.PropertyToID("_CubusWeatherRainIntensity");
        private static readonly int WetnessId = Shader.PropertyToID("_CubusWeatherWetness");
        private static readonly int PuddleStrengthId = Shader.PropertyToID("_CubusWeatherPuddleStrength");
        private static readonly int GroundDarkeningId = Shader.PropertyToID("_CubusWeatherGroundDarkening");
        private static readonly int SpecularBoostId = Shader.PropertyToID("_CubusWeatherSpecularBoost");
        private static readonly int CloudShadowStrengthId = Shader.PropertyToID("_CubusWeatherCloudShadowStrength");
        private static readonly int CloudShadowScaleId = Shader.PropertyToID("_CubusWeatherCloudShadowScale");
        private static readonly int CloudShadowSpeedId = Shader.PropertyToID("_CubusWeatherCloudShadowSpeed");
        private static readonly int CloudShadowContrastId = Shader.PropertyToID("_CubusWeatherCloudShadowContrast");

        [SerializeField] private AzureCoreSystem azureCore;
        [SerializeField] private bool autoFindAzureCore = true;
        [SerializeField] private bool logBinding;

        [Header("Ground Response")]
        [Range(0f, 1f)][SerializeField] private float wetnessFromRain = 1.0f;
        [Range(0f, 1f)][SerializeField] private float wetnessFromFog = 0.18f;
        [Range(0f, 1f)][SerializeField] private float wetnessFromHumidity = 0.35f;
        [SerializeField] private float wettingRate = 0.85f;
        [SerializeField] private float dryingRate = 0.08f;
        [Range(0f, 1f)][SerializeField] private float puddleStrength = 0.75f;
        [Range(0f, 1f)][SerializeField] private float groundDarkening = 0.72f;
        [Range(0f, 2f)][SerializeField] private float specularBoost = 1.0f;

        [Header("Cloud Shadows")]
        [Range(0f, 1f)][SerializeField] private float clearDayCloudShadow = 0.06f;
        [Range(0f, 1f)][SerializeField] private float overcastCloudShadow = 0.18f;
        [Range(0f, 1f)][SerializeField] private float stormCloudShadow = 0.38f;
        [SerializeField] private float cloudShadowScale = 0.0018f;
        [SerializeField] private float cloudShadowSpeed = 0.018f;
        [Range(0f, 1f)][SerializeField] private float cloudShadowContrast = 0.68f;

        private float wetness;
        private bool loggedBinding;

        private AzureWeatherSystem Weather => azureCore != null ? azureCore.weatherSystem : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureRuntimeBridge()
        {
            if (FindAnyObjectByType<AzureTerrainWeatherBridge>() != null)
            {
                return;
            }

            GameObject bridge = new("Azure Terrain Weather Bridge");
            bridge.AddComponent<AzureTerrainWeatherBridge>();
        }

        private void Reset()
        {
            azureCore = GetComponent<AzureCoreSystem>();
        }

        private void Awake()
        {
            ResolveAzureCore();
        }

        private void OnDisable()
        {
            PublishWeather(0f, 0f, 0f);
        }

        private void LateUpdate()
        {
            ResolveAzureCore();

            AzureWeatherSystem weather = Weather;
            if (weather == null || weather.weatherPropertyGroupList == null)
            {
                PublishWeather(0f, Mathf.MoveTowards(wetness, 0f, dryingRate * Time.deltaTime), 0f);
                return;
            }

            ExtractWeatherSignals(weather, out float rain, out float fog, out float humidity, out float cloud);

            float targetWetness = Mathf.Clamp01(
                rain * wetnessFromRain +
                fog * wetnessFromFog +
                humidity * wetnessFromHumidity);

            float rate = targetWetness > wetness ? wettingRate : dryingRate;
            wetness = Mathf.MoveTowards(wetness, targetWetness, Mathf.Max(0.001f, rate) * Time.deltaTime);

            float storm = Mathf.Max(rain, Mathf.Clamp01((rain + cloud) * 0.5f));
            float cloudShadow = Mathf.Clamp01(
                clearDayCloudShadow +
                cloud * overcastCloudShadow +
                storm * stormCloudShadow);

            PublishWeather(rain, wetness, cloudShadow);
        }

        private void ResolveAzureCore()
        {
            if (azureCore != null || !autoFindAzureCore)
            {
                return;
            }

            azureCore = GetComponent<AzureCoreSystem>();
            if (azureCore == null)
            {
                azureCore = FindAnyObjectByType<AzureCoreSystem>();
            }

            if (logBinding && azureCore != null && !loggedBinding)
            {
                Debug.Log($"[AzureTerrainWeather] Bound to {azureCore.name}.", this);
                loggedBinding = true;
            }
        }

        private void ExtractWeatherSignals(AzureWeatherSystem weather, out float rain, out float fog, out float humidity, out float cloud)
        {
            rain = 0f;
            fog = 0f;
            humidity = 0f;
            cloud = 0f;

            ApplyPresetNameSignals(weather.currentWeatherPreset, 1.0f - weather.weatherTransitionProgress, ref rain, ref fog, ref cloud);
            ApplyPresetNameSignals(weather.targetWeatherPreset, weather.weatherTransitionProgress, ref rain, ref fog, ref cloud);

            for (int groupIndex = 0; groupIndex < weather.weatherPropertyGroupList.Count; groupIndex++)
            {
                AzureWeatherPropertyGroup group = weather.weatherPropertyGroupList[groupIndex];
                if (group == null || !group.isEnabled || group.weatherPropertyList == null)
                {
                    continue;
                }

                for (int propertyIndex = 0; propertyIndex < group.weatherPropertyList.Count; propertyIndex++)
                {
                    AzureWeatherProperty property = group.weatherPropertyList[propertyIndex];
                    if (property == null)
                    {
                        continue;
                    }

                    if (property.propertyType != AzureWeatherPropertyType.Float && property.propertyType != AzureWeatherPropertyType.Curve)
                    {
                        continue;
                    }

                    string propertyName = BuildSignalName(group, property);
                    float normalized = NormalizeProperty(property.floatOutput, property.minValue, property.maxValue);

                    if (Contains(propertyName, "rain", "precip", "storm"))
                    {
                        rain = Mathf.Max(rain, normalized);
                    }

                    if (Contains(propertyName, "fog", "mist", "haze"))
                    {
                        fog = Mathf.Max(fog, normalized);
                    }

                    if (Contains(propertyName, "cloud", "overcast"))
                    {
                        cloud = Mathf.Max(cloud, normalized);
                    }

                    if (Contains(propertyName, "wet", "humid", "humidity"))
                    {
                        humidity = Mathf.Max(humidity, normalized);
                    }
                }
            }
        }

        private static void ApplyPresetNameSignals(AzureWeatherPreset preset, float weight, ref float rain, ref float fog, ref float cloud)
        {
            if (preset == null || weight <= 0f)
            {
                return;
            }

            string presetName = preset.name != null ? preset.name.ToLowerInvariant() : string.Empty;
            float signal = Mathf.Clamp01(weight);

            if (Contains(presetName, "fog", "mist", "haze"))
            {
                fog = Mathf.Max(fog, signal);
                cloud = Mathf.Max(cloud, signal * 0.35f);
            }

            if (Contains(presetName, "rain", "storm", "thunder", "precip"))
            {
                rain = Mathf.Max(rain, signal);
                cloud = Mathf.Max(cloud, signal * 0.8f);
            }

            if (Contains(presetName, "cloud", "overcast"))
            {
                cloud = Mathf.Max(cloud, signal);
            }
        }

        private void PublishWeather(float rain, float currentWetness, float cloudShadow)
        {
            Shader.SetGlobalFloat(RainIntensityId, Mathf.Clamp01(rain));
            Shader.SetGlobalFloat(WetnessId, Mathf.Clamp01(currentWetness));
            Shader.SetGlobalFloat(PuddleStrengthId, puddleStrength);
            Shader.SetGlobalFloat(GroundDarkeningId, groundDarkening);
            Shader.SetGlobalFloat(SpecularBoostId, specularBoost);
            Shader.SetGlobalFloat(CloudShadowStrengthId, Mathf.Clamp01(cloudShadow));
            Shader.SetGlobalFloat(CloudShadowScaleId, Mathf.Max(0.00001f, cloudShadowScale));
            Shader.SetGlobalFloat(CloudShadowSpeedId, cloudShadowSpeed);
            Shader.SetGlobalFloat(CloudShadowContrastId, cloudShadowContrast);
        }

        private static float NormalizeProperty(float value, float min, float max)
        {
            if (Mathf.Abs(max - min) <= 0.0001f)
            {
                return Mathf.Clamp01(value);
            }

            return Mathf.Clamp01(Mathf.InverseLerp(min, max, value));
        }

        private static bool Contains(string haystack, params string[] needles)
        {
            for (int i = 0; i < needles.Length; i++)
            {
                if (haystack.Contains(needles[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildSignalName(AzureWeatherPropertyGroup group, AzureWeatherProperty property)
        {
            string groupName = group != null && group.name != null ? group.name : string.Empty;
            string propertyName = property != null && property.name != null ? property.name : string.Empty;
            string targetName = property != null && property.targetPropertyName != null ? property.targetPropertyName : string.Empty;
            return $"{groupName} {propertyName} {targetName}".ToLowerInvariant();
        }
    }
}
