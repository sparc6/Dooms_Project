using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace MLA_SIM
{
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    [AddComponentMenu("MLA_SIM/Environment/Time Of Day Manager")]
    public class TimeOfDayManager : MonoBehaviour
    {
        public static TimeOfDayManager Instance { get; private set; }

        [System.Serializable]
        public class LightBinding
        {
            public Light light;
            public bool enableDuringDay = false;
            public bool enableDuringNight = true;
        }

        [Header("Clock")]
        public bool autoAdvanceTime = true;
        public float simHourPerSecond = 0.01f;
        [Range(0f, 24f)] public float currentHour = 8f;

        [Header("Day / Night Windows")]
        [Range(0f, 24f)] public float sunriseHour = 6f;
        [Range(0f, 24f)] public float sunsetHour = 18f;
        [Min(0.1f)] public float twilightDurationHours = 2f;

        [Header("Sun (Day Light)")]
        public Light sunLight;
        [Range(0f, 360f)] public float sunAzimuth = 135f;
        [Range(5f, 90f)] public float sunMaxElevation = 75f;
        
        [Header("Moon (Night Light)")]
        public Light moonLight;
        [Range(0f, 360f)] public float moonAzimuth = 315f;
        [Range(5f, 90f)] public float moonMaxElevation = 70f;

        [Header("Auxiliary Lights")]
        public System.Collections.Generic.List<LightBinding> auxiliaryLights = new System.Collections.Generic.List<LightBinding>();

        [Header("HDRP Exposure")]
        public Volume hdrpVolume;
        public bool driveHdrpExposure = true;
        [Tooltip("HDRP fixed exposure in EV100 during daytime. Higher values are darker.")]
        public float dayVolumeExposure = 14f;
        [Tooltip("HDRP fixed exposure in EV100 during nighttime. Higher values are darker.")]
        public float nightVolumeExposure = 8f;

        private Exposure _exposure;
        private bool _exposureReady;
        private readonly System.Collections.Generic.Dictionary<Light, float> _authoredLightIntensities = new System.Collections.Generic.Dictionary<Light, float>();

        public float CurrentHour => Mathf.Repeat(currentHour, 24f);
        public string TimeOfDayLabel => FormatTimeOfDay(CurrentHour);
        public bool IsNight => GetDaylightFactor(CurrentHour) <= 0.001f;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                Debug.LogWarning("Multiple TimeOfDayManager instances found. Destroying duplicate.");
                Destroy(gameObject);
                return;
            }

            CacheAuthoredLightIntensities();
            ResolveExposureOverride();
            ApplyImmediate();
        }

        private void OnEnable()
        {
            CacheAuthoredLightIntensities();
            ResolveExposureOverride();
            ApplyImmediate();
        }

        private void Update()
        {
            if (autoAdvanceTime)
            {
                currentHour = Mathf.Repeat(currentHour + simHourPerSecond * Time.deltaTime, 24f);
            }

            ApplyImmediate();
        }

        public void SetCurrentHour(float hour)
        {
            currentHour = Mathf.Repeat(hour, 24f);
            ApplyImmediate();
        }

        public float GetDaylightFactor(float hour)
        {
            float sunriseStart = Mathf.Repeat(sunriseHour - twilightDurationHours * 0.5f, 24f);
            float sunriseEnd = Mathf.Repeat(sunriseHour + twilightDurationHours * 0.5f, 24f);
            float sunsetStart = Mathf.Repeat(sunsetHour - twilightDurationHours * 0.5f, 24f);
            float sunsetEnd = Mathf.Repeat(sunsetHour + twilightDurationHours * 0.5f, 24f);

            if (InWrappedRange(hour, sunriseEnd, sunsetStart))
                return 1f;

            if (InWrappedRange(hour, sunsetEnd, sunriseStart))
                return 0f;

            if (InWrappedRange(hour, sunriseStart, sunriseEnd))
                return Mathf.Clamp01(InverseLerpWrapped(hour, sunriseStart, sunriseEnd));

            if (InWrappedRange(hour, sunsetStart, sunsetEnd))
                return 1f - Mathf.Clamp01(InverseLerpWrapped(hour, sunsetStart, sunsetEnd));

            return 0f;
        }

        public static string FormatTimeOfDay(float hour)
        {
            hour = Mathf.Repeat(hour, 24f);
            if (hour >= 6f && hour < 12f) return "morning";
            if (hour >= 12f && hour < 17f) return "afternoon";
            if (hour >= 17f && hour < 21f) return "evening";
            return "night";
        }

        private void ApplyImmediate()
        {
            float hour = CurrentHour;
            float daylight = Mathf.SmoothStep(0f, 1f, GetDaylightFactor(hour));
            ApplySun(hour, daylight);
            ApplyMoon(hour, daylight);
            ApplyAuxiliaryLights(daylight);
            ApplyExposure(daylight);
        }

        private void ApplyAuxiliaryLights(float daylight)
        {
            if (auxiliaryLights == null || auxiliaryLights.Count == 0)
                return;

            bool isDay = daylight >= 0.5f;
            float dayBlend = daylight * daylight;
            float nightBlend = (1f - daylight) * (1f - daylight);
            for (int i = 0; i < auxiliaryLights.Count; i++)
            {
                LightBinding binding = auxiliaryLights[i];
                if (binding == null || binding.light == null)
                    continue;

                float blend = isDay
                    ? (binding.enableDuringDay ? Mathf.Max(dayBlend, binding.enableDuringNight ? nightBlend : 0f) : (binding.enableDuringNight ? nightBlend : 0f))
                    : (binding.enableDuringNight ? Mathf.Max(nightBlend, binding.enableDuringDay ? dayBlend : 0f) : (binding.enableDuringDay ? dayBlend : 0f));

                ApplyLightBlend(binding.light, blend);
            }
        }

        private void ApplySun(float hour, float daylight)
        {
            if (sunLight == null)
                return;

            float dayPos = DayArcPosition(hour);
            bool aboveHorizon = dayPos > 0f && dayPos < 1f;

            float sunBlend = daylight * daylight;
            bool shouldBeActive = sunBlend > 0.001f;
            if (!aboveHorizon || !shouldBeActive)
            {
                ApplyLightBlend(sunLight, 0f);
                return;
            }

            float elevation = Mathf.Sin(dayPos * Mathf.PI) * sunMaxElevation;
            sunLight.transform.rotation = Quaternion.Euler(elevation, sunAzimuth, 0f);
            ApplyLightBlend(sunLight, sunBlend);
        }

        private void ApplyMoon(float hour, float daylight)
        {
            if (moonLight == null)
                return;

            float nightPos = NightArcPosition(hour);
            bool aboveHorizon = nightPos > 0f && nightPos < 1f;

            float moonBlend = (1f - daylight) * (1f - daylight);
            bool shouldBeActive = moonBlend > 0.001f;
            if (!aboveHorizon || !shouldBeActive)
            {
                ApplyLightBlend(moonLight, 0f);
                return;
            }

            float elevation = Mathf.Sin(nightPos * Mathf.PI) * moonMaxElevation;
            moonLight.transform.rotation = Quaternion.Euler(elevation, moonAzimuth, 0f);
            ApplyLightBlend(moonLight, moonBlend);
        }

        private void ApplyExposure(float daylight)
        {
            if (!driveHdrpExposure)
                return;

            if (!_exposureReady)
                ResolveExposureOverride();

            if (_exposure == null)
                return;

            _exposure.mode.Override(ExposureMode.Fixed);
            _exposure.fixedExposure.Override(Mathf.Lerp(nightVolumeExposure, dayVolumeExposure, daylight));
            _exposure.compensation.Override(0f);
        }

        private float DayArcPosition(float hour)
        {
            float start = sunriseHour - twilightDurationHours * 0.5f;
            float end = sunsetHour + twilightDurationHours * 0.5f;
            return InverseLerpWrapped(hour, start, end);
        }

        private float NightArcPosition(float hour)
        {
            float start = sunsetHour - twilightDurationHours * 0.5f;
            float end = sunriseHour + twilightDurationHours * 0.5f;
            return InverseLerpWrapped(hour, start, end);
        }

        private void ResolveExposureOverride()
        {
            _exposureReady = true;
            _exposure = null;

            if (hdrpVolume == null)
                return;

            var profile = hdrpVolume.profile;
            if (profile == null && hdrpVolume.sharedProfile != null)
            {
                profile = Instantiate(hdrpVolume.sharedProfile);
                hdrpVolume.profile = profile;
            }

            if (profile == null)
                return;

            if (!profile.TryGet(out _exposure) || _exposure == null)
            {
                _exposure = profile.Add<Exposure>(true);
            }
        }

        private void CacheAuthoredLightIntensities()
        {
            CacheAuthoredLightIntensity(sunLight);
            CacheAuthoredLightIntensity(moonLight);

            if (auxiliaryLights == null)
                return;

            for (int i = 0; i < auxiliaryLights.Count; i++)
            {
                LightBinding binding = auxiliaryLights[i];
                if (binding == null)
                    continue;

                CacheAuthoredLightIntensity(binding.light);
            }
        }

        private void CacheAuthoredLightIntensity(Light light)
        {
            if (light == null || _authoredLightIntensities.ContainsKey(light))
                return;

            _authoredLightIntensities[light] = light.intensity;
        }

        private void ApplyLightBlend(Light light, float blend)
        {
            if (light == null)
                return;

            CacheAuthoredLightIntensity(light);

            float clampedBlend = Mathf.Clamp01(blend);
            float baseIntensity = _authoredLightIntensities.TryGetValue(light, out float intensity)
                ? intensity
                : light.intensity;

            SetLightActive(light, clampedBlend > 0.001f);
            light.intensity = baseIntensity * clampedBlend;
        }

        private static void SetLightActive(Light light, bool active)
        {
            if (light == null)
                return;

            if (light.gameObject.activeSelf != active)
                light.gameObject.SetActive(active);

            light.enabled = active;
        }

        private static bool InWrappedRange(float value, float start, float end)
        {
            value = Mathf.Repeat(value, 24f);
            start = Mathf.Repeat(start, 24f);
            end = Mathf.Repeat(end, 24f);

            if (Mathf.Approximately(start, end))
                return true;

            if (start < end)
                return value >= start && value < end;

            return value >= start || value < end;
        }

        private static float InverseLerpWrapped(float value, float start, float end)
        {
            value = Mathf.Repeat(value, 24f);
            start = Mathf.Repeat(start, 24f);
            end = Mathf.Repeat(end, 24f);

            if (end < start)
                end += 24f;
            if (value < start)
                value += 24f;

            float length = Mathf.Max(0.0001f, end - start);
            return (value - start) / length;
        }
    }
}
