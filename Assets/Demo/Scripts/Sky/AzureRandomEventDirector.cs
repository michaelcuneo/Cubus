using UnityEngine;
using UnityEngine.AzureSky;
using UnityEngine.Events;

namespace Assets.Demo.Scripts.Sky
{
    /// <summary>
    /// Drop-in random weather director for Azure[Sky].
    ///
    /// SETUP (that's the whole thing):
    ///   1. Add this component to ANY GameObject in the scene (the Azure Core System object is ideal).
    ///   2. Press Play.
    ///
    /// It auto-finds the Azure Core System, subscribes itself to the time events (minute/hour/day/
    /// month/year) and, on a sensible cadence, generates fresh randomized weather by cloning your
    /// current weather preset and nudging every property within the min/max range Azure already
    /// declares for it. Nothing to wire by hand, no presets to author.
    ///
    /// Each randomized "mood" biases the right properties using their names (anything containing
    /// fog/cloud/rain/wind goes up in storms and down when calm; light/sun/intensity dims in storms).
    /// Properties Azure doesn't recognise are still safely randomized within their own bounds.
    /// </summary>
    [AddComponentMenu("Cubus/Sky/Azure Random Event Director")]
    public sealed class AzureRandomEventDirector : MonoBehaviour
    {
        public enum WeatherMood { Calm, Overcast, Storm, Ominous, Ethereal }

        [Header("References (auto-found if empty)")]
        [SerializeField] private AzureCoreSystem azureCore;
        [Tooltip("Preset used as the 'decent values' baseline that gets cloned and randomized. " +
                 "If empty, the weather preset active at startup is captured automatically.")]
        [SerializeField] private AzureWeatherPreset templatePreset;

        [Header("Behaviour")]
        [Tooltip("Subscribe to Azure time events automatically. Leave on for zero-wiring operation.")]
        [SerializeField] private bool autoSubscribe = true;
        [Tooltip("Seconds a weather transition takes to blend in.")]
        [SerializeField] private float transitionTime = 18f;
        [Tooltip("Generate a brand-new weather mood at the start of every in-game day.")]
        [SerializeField] private bool newWeatherEachDay = true;

        [Header("Probabilities (0 = never, 1 = always)")]
        [Range(0f, 1f)][SerializeField] private float hourlyChangeChance = 0.25f;
        [Range(0f, 1f)][SerializeField] private float rareNightEventChance = 0.15f;
        [Range(0f, 1f)][SerializeField] private float grandYearEventChance = 0.5f;
        [Range(0f, 1f)][SerializeField] private float stormThunderChancePerMinute = 0.03f;

        [Header("Randomness")]
        [Tooltip("How far each value wanders from the baseline preset toward a random in-range value. " +
                 "0 = keep the baseline, 1 = fully random within the property's min/max.")]
        [Range(0f, 1f)][SerializeField] private float randomness = 0.45f;
        [Tooltip("Hue / saturation / value jitter applied to colour properties.")]
        [Range(0f, 0.5f)][SerializeField] private float colorJitter = 0.08f;
        [SerializeField] private bool randomizeColors = true;
        [Tooltip("Use property names (fog, cloud, light, etc.) to bias values per mood.")]
        [SerializeField] private bool useKeywordBias = true;

        [Header("Thunder")]
        [Tooltip("Number of entries in Weather System -> Thunder Settings. A random one is picked.")]
        [SerializeField] private int thunderVariantCount = 1;

        [Header("Season Pacing (Day Length in hours)")]
        [SerializeField] private float summerDayLength = 60f;
        [SerializeField] private float normalDayLength = 120f;
        [SerializeField] private float winterDayLength = 240f;

        [Header("Optional Art Hooks (particles / audio / VFX)")]
        public UnityEvent onBloodMoon = new UnityEvent();
        public UnityEvent onAurora = new UnityEvent();
        public UnityEvent onMeteorShower = new UnityEvent();
        public UnityEvent onEclipse = new UnityEvent();
        public UnityEvent onEerieFog = new UnityEvent();
        public UnityEvent onLightningStrike = new UnityEvent();
        public UnityEvent onCalmRestored = new UnityEvent();

        [Header("Debug")]
        [SerializeField] private bool logEvents = true;

        // Runtime state ------------------------------------------------------
        private AzureWeatherPreset m_template;          // captured-once baseline
        private readonly AzureWeatherPreset[] m_pool = new AzureWeatherPreset[2];
        private int m_activeSlot = -1;
        private bool m_poolBuilt;
        private bool m_isStormy;

        private AzureTimeSystem Time => azureCore != null ? azureCore.timeSystem : null;
        private AzureWeatherSystem Weather => azureCore != null ? azureCore.weatherSystem : null;

        // Lifecycle ----------------------------------------------------------

        private void Reset() => azureCore = GetComponent<AzureCoreSystem>();

        private void Awake()
        {
            if (azureCore == null) azureCore = GetComponent<AzureCoreSystem>();
            if (azureCore == null) azureCore = FindAnyObjectByType<AzureCoreSystem>();
        }

        private void OnEnable()
        {
            if (!autoSubscribe) return;
            AzureNotificationCenter.OnMinuteChanged += HandleMinute;
            AzureNotificationCenter.OnHourChanged += HandleHour;
            AzureNotificationCenter.OnDayChanged += HandleDay;
            AzureNotificationCenter.OnMonthChanged += HandleMonth;
            AzureNotificationCenter.OnYearChanged += HandleYear;
        }

        private void OnDisable()
        {
            AzureNotificationCenter.OnMinuteChanged -= HandleMinute;
            AzureNotificationCenter.OnHourChanged -= HandleHour;
            AzureNotificationCenter.OnDayChanged -= HandleDay;
            AzureNotificationCenter.OnMonthChanged -= HandleMonth;
            AzureNotificationCenter.OnYearChanged -= HandleYear;
        }

        // Azure event handlers ----------------------------------------------

        private void HandleMinute(AzureTimeSystem t)
        {
            if (m_isStormy && Random.value <= stormThunderChancePerMinute)
                StrikeThunder();
        }

        private void HandleHour(AzureTimeSystem t)
        {
            if (Random.value <= hourlyChangeChance)
                NewRandomWeather();
        }

        private void HandleDay(AzureTimeSystem t)
        {
            if (newWeatherEachDay)
                GenerateWeather(RandomMood());

            if (Random.value <= rareNightEventChance)
            {
                switch (Random.Range(0, 4))
                {
                    case 0: TriggerBloodMoon(); break;
                    case 1: TriggerAurora(); break;
                    case 2: TriggerMeteorShower(); break;
                    default: TriggerEerieFog(); break;
                }
            }
        }

        private void HandleMonth(AzureTimeSystem t) => ShiftSeason();

        private void HandleYear(AzureTimeSystem t)
        {
            if (Random.value > grandYearEventChance) return;
            switch (Random.Range(0, 3))
            {
                case 0: TriggerEclipse(); break;
                case 1: TriggerAurora(); break;
                default: TriggerMeteorShower(); break;
            }
        }

        // Public, parameter-less API (also wireable into Custom Events) -------

        public void NewRandomWeather() => GenerateWeather(RandomMood());
        public void SetCalmWeather() => GenerateWeather(WeatherMood.Calm);
        public void SetOvercastWeather() => GenerateWeather(WeatherMood.Overcast);
        public void SetStormWeather() => GenerateWeather(WeatherMood.Storm);

        public void TriggerBloodMoon()
        {
            GenerateWeather(WeatherMood.Ominous);
            Log("BLOOD MOON rises.");
            onBloodMoon.Invoke();
        }

        public void TriggerAurora()
        {
            GenerateWeather(WeatherMood.Ethereal);
            Log("Aurora ignites the sky.");
            onAurora.Invoke();
        }

        public void TriggerMeteorShower()
        {
            GenerateWeather(WeatherMood.Calm);
            Log("Meteor shower!");
            onMeteorShower.Invoke();
        }

        public void TriggerEclipse()
        {
            GenerateWeather(WeatherMood.Ominous);
            Log("The sun goes dark - eclipse.");
            onEclipse.Invoke();
        }

        public void TriggerEerieFog()
        {
            GenerateWeather(WeatherMood.Ominous);
            Log("An eerie fog creeps in.");
            onEerieFog.Invoke();
        }

        public void StrikeThunder()
        {
            if (Weather == null) return;
            Weather.InstantiateThunderPrefab(Random.Range(0, Mathf.Max(1, thunderVariantCount)));
            onLightningStrike.Invoke();
        }

        /// <summary>Shifts day pacing per month: short bright summers, long brooding winters.</summary>
        public void ShiftSeason()
        {
            if (Time == null) return;
            int m = Time.month;
            float target = (m >= 6 && m <= 8) ? summerDayLength
                         : (m == 12 || m <= 2) ? winterDayLength
                         : normalDayLength;
            Time.dayLength = target;
            Log($"Season shift -> month {m}, day length {target}.");
        }

        // Time helpers -------------------------------------------------------

        public void WarpTime(float targetHour)
        {
            if (Time == null) return;
            Time.StartTimelineTransition(Mathf.Repeat(targetHour, 24f), Mathf.Max(0.01f, transitionTime));
        }

        public void SkipToDawn() => WarpTime(6f);
        public void SkipToNoon() => WarpTime(12f);
        public void SkipToDusk() => WarpTime(18f);
        public void SkipToMidnight() => WarpTime(0f);

        // Weather generation -------------------------------------------------

        private WeatherMood RandomMood()
        {
            float r = Random.value;
            if (r < 0.45f) return WeatherMood.Calm;
            if (r < 0.70f) return WeatherMood.Overcast;
            if (r < 0.88f) return WeatherMood.Storm;
            if (r < 0.94f) return WeatherMood.Ominous;
            return WeatherMood.Ethereal;
        }

        private void GenerateWeather(WeatherMood mood)
        {
            if (Weather == null) return;
            if (Weather.isWeatherChanging) return;
            if (!EnsurePool()) return;

            int dstSlot = m_activeSlot == 0 ? 1 : 0;
            AzureWeatherPreset dst = m_pool[dstSlot];

            RandomizePreset(dst, mood);
            Weather.SetGlobalWeather(dst, transitionTime);

            m_activeSlot = dstSlot;
            m_isStormy = mood == WeatherMood.Storm || mood == WeatherMood.Ominous;
            if (mood == WeatherMood.Calm) onCalmRestored.Invoke();
            Log($"New weather generated: {mood}.");
        }

        /// <summary>Captures the baseline template and builds two reusable runtime preset clones.</summary>
        private bool EnsurePool()
        {
            if (m_poolBuilt) return true;

            if (m_template == null)
                m_template = templatePreset != null ? templatePreset : Weather.currentWeatherPreset;
            if (m_template == null)
            {
                Log("No template weather preset available yet - skipping generation.");
                return false;
            }

            Transform parent = azureCore != null ? azureCore.transform : transform;
            for (int i = 0; i < 2; i++)
            {
                GameObject clone = Instantiate(m_template.gameObject, parent);
                clone.name = $"[RuntimeRandomWeather {i}]";
                clone.SetActive(false); // pure data holder; never needs to be active
                m_pool[i] = clone.GetComponent<AzureWeatherPreset>();
            }
            m_poolBuilt = true;
            return m_pool[0] != null && m_pool[1] != null;
        }

        private void RandomizePreset(AzureWeatherPreset dst, WeatherMood mood)
        {
            var groups = Weather.weatherPropertyGroupList;
            if (groups == null) return;

            for (int i = 0; i < groups.Count; i++)
            {
                if (i >= dst.propertyGroupDataList.Count || i >= m_template.propertyGroupDataList.Count)
                    continue;

                var props = groups[i].weatherPropertyList;
                var dstData = dst.propertyGroupDataList[i].propertyDataList;
                var baseData = m_template.propertyGroupDataList[i].propertyDataList;

                for (int j = 0; j < props.Count; j++)
                {
                    if (j >= dstData.Count || j >= baseData.Count) continue;
                    AzureWeatherProperty owner = props[j];

                    switch (owner.propertyType)
                    {
                        case AzureWeatherPropertyType.Float:
                        case AzureWeatherPropertyType.Curve:
                            {
                                float min = owner.minValue, max = owner.maxValue;
                                float baseVal = baseData[j].floatData;
                                float rnd = Random.Range(min, max);
                                rnd = BiasFloat(owner.name, mood, rnd, min, max);
                                dstData[j].floatData = Mathf.Lerp(baseVal, rnd, randomness);
                                break;
                            }

                        case AzureWeatherPropertyType.Color:
                        case AzureWeatherPropertyType.Gradient:
                            {
                                if (!randomizeColors) break;
                                dstData[j].colorData = JitterColor(owner.name, mood, baseData[j].colorData);
                                break;
                            }
                            // Direction / Position left at the cloned baseline (never randomized).
                    }
                }
            }
        }

        // Mood biasing -------------------------------------------------------

        private float BiasFloat(string name, WeatherMood mood, float value, float min, float max)
        {
            if (!useKeywordBias) return value;
            string n = name.ToLowerInvariant();

            // Atmospherics that should swell in bad weather and fade in calm weather.
            if (Contains(n, "fog", "cloud", "rain", "wet", "humid", "wind", "storm", "mist", "overcast"))
            {
                switch (mood)
                {
                    case WeatherMood.Storm:
                    case WeatherMood.Ominous: return TowardEdge(value, min, max, 0.85f, 0.6f);
                    case WeatherMood.Overcast: return TowardEdge(value, min, max, 0.6f, 0.4f);
                    case WeatherMood.Ethereal: return TowardEdge(value, min, max, 0.35f, 0.35f);
                    default: return TowardEdge(value, min, max, 0.12f, 0.5f); // Calm
                }
            }

            // Light / brightness that should dim under heavy skies.
            if (Contains(n, "light", "intensity", "exposure", "bright", "sun"))
            {
                switch (mood)
                {
                    case WeatherMood.Storm:
                    case WeatherMood.Ominous: return TowardEdge(value, min, max, 0.3f, 0.5f);
                    case WeatherMood.Ethereal: return TowardEdge(value, min, max, 0.7f, 0.3f);
                    default: return value;
                }
            }

            return value;
        }

        private Color JitterColor(string name, WeatherMood mood, Color baseColor)
        {
            Color.RGBToHSV(baseColor, out float h, out float s, out float v);
            h = Mathf.Repeat(h + Random.Range(-colorJitter, colorJitter), 1f);
            s = Mathf.Clamp01(s + Random.Range(-colorJitter, colorJitter));
            v *= Random.Range(1f - colorJitter, 1f + colorJitter); // preserve HDR intensity

            Color result = Color.HSVToRGB(h, s, v, true);
            result.a = baseColor.a;

            if (!useKeywordBias) return result;
            string n = name.ToLowerInvariant();
            bool tintable = Contains(n, "fog", "cloud", "sky", "ambient", "light", "horizon");
            if (!tintable) return result;

            switch (mood)
            {
                case WeatherMood.Ominous:
                    return TintToward(result, new Color(0.65f, 0.22f, 0.18f), 0.3f); // crimson
                case WeatherMood.Storm:
                    return TintToward(result, new Color(0.35f, 0.37f, 0.4f), 0.3f);  // slate grey
                case WeatherMood.Ethereal:
                    return TintToward(result, new Color(0.25f, 0.7f, 0.55f), 0.25f); // teal-green
                default:
                    return result;
            }
        }

        // Small math helpers -------------------------------------------------

        private static float TowardEdge(float value, float min, float max, float edge01, float strength)
            => Mathf.Lerp(value, Mathf.Lerp(min, max, edge01), strength);

        private static Color TintToward(Color c, Color target, float t)
        {
            float a = c.a;
            Color r = Color.Lerp(c, target, t);
            r.a = a;
            return r;
        }

        private static bool Contains(string haystack, params string[] needles)
        {
            for (int i = 0; i < needles.Length; i++)
                if (haystack.Contains(needles[i])) return true;
            return false;
        }

        private void Log(string msg)
        {
            if (logEvents) Debug.Log($"[AzureEvents] {msg}");
        }
    }
}
