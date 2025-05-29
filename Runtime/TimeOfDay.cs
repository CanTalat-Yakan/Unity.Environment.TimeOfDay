using System;
using UnityEngine;
using UnityEngine.Events;

namespace UnityEssentials
{
    public enum PresetLocations
    {
        Custom,
        Greenwich,
        Cologne,
        Dubrovnik,
        Tokyo,
        NewYork,
    }

    [Serializable]
    public struct LocationPreset
    {
        public string Name;
        public float Latitude;
        public float Longitude;
        public int UTCOffset;
    }

    public class TimeOfDay : MonoBehaviour
    {
        [Header("Time Settings")]
        [Date] public Vector3Int Date;
        [Time] public float InitialTimeInHours;
        [Range(0, 24)] public float TimeInHours;
        [OnValueChanged("InitialTimeInHours")] public void OnTimeInHoursValueChanged() => TimeInHours = InitialTimeInHours;

        [Header("Celestial Bodies")]
        public Light SunLight;
        public Light MoonLight;

        [Header("Location Presets")]
        public PresetLocations Location;
        [Tooltip("In Degrees")]
        [Range(-90, 90)] public float Latitude = 50.9375f;
        [Tooltip("In Degrees")]
        [Range(-180, 180)] public float Longitude = 6.9603f;
        [Tooltip("Offset in hours")]
        [Range(-12, 14)] public int UTCOffset = 1;

        [Header("Day Night Cycle Events")]
        public UnityEvent DayEvents;
        public UnityEvent NightEvents;

        public static bool IsDay { get; private set; }
        public static bool IsNight => !IsDay;
        public float DayWeight { get; private set; }

        public SunPhase SunPhase { get; private set; }
        public float SunElevationAngle { get; private set; }
        public float SunAzimuthAngle { get; private set; }

        public MoonPhase MoonPhase { get; private set; }
        public float MoonElevationAngle { get; private set; }
        public float MoonAzimuthAngle { get; private set; }
        public double MoonIllumination { get; private set; }
        public double MoonDistance { get; private set; }
        public DateTime DateTime { get; private set; }

        private LocationPreset[] _locationPreset = new[]
        {
            new LocationPreset { Name = "Empty"},
            new LocationPreset { Name = "Greenwich", Latitude = 51.4934f, Longitude = 0.0098f, UTCOffset = 0 },
            new LocationPreset { Name = "Cologne", Latitude = 50.9375f, Longitude = 6.9603f, UTCOffset = 1 },
            new LocationPreset { Name = "Dubrovnik", Latitude = 42.6507f, Longitude = 18.0944f, UTCOffset = 1 },
            new LocationPreset { Name = "Tokyo", Latitude = 35.6764f, Longitude = 139.6500f, UTCOffset = 9 },
            new LocationPreset { Name = "NewYork", Latitude = 40.7128f, Longitude = -74.0060f, UTCOffset = -5 },
        };

        [OnValueChanged("Location")]
        public void OnLocationValueChanged()
        {
            Debug.Log($"Location changed to: {_locationPreset[(int)Location].Name}");
            var preset = _locationPreset[(int)Location];
            Latitude = preset.Latitude;
            Longitude = preset.Longitude;
            UTCOffset = preset.UTCOffset;
        }

        void Update()
        {
            GetCurrentTimeUTC();
            UpdateCelestialTargets();
        }

        private void GetCurrentTimeUTC() =>
            DateTime = new DateTime(Date.x, Date.y, Date.z, 0, 0, 0, DateTimeKind.Utc).AddHours(TimeInHours - UTCOffset);

        [ContextMenu("Update Celestial Targets")]
        private void UpdateCelestialTargets()
        {
            var sunDirection = CelestialBodiesCalculator.GetSunDirection(DateTime, Latitude, Longitude).ToVector3();
            var moonDirection = CelestialBodiesCalculator.GetMoonDirection(DateTime, Latitude, Longitude).ToVector3();

            if (SunLight != null && MoonLight != null)
            {
                var sunRotation = Quaternion.LookRotation(-sunDirection, Vector3.up);
                SunLight.transform.rotation = Quaternion.Lerp(SunLight.transform.rotation, sunRotation, Time.deltaTime);

                var moonRotation = Quaternion.LookRotation(-moonDirection, Vector3.up);
                MoonLight.transform.rotation = Quaternion.Lerp(MoonLight.transform.rotation, moonRotation, Time.deltaTime);
            }

            var sunProperties = CelestialBodiesCalculator.GetSunProperties(DateTime, Latitude, Longitude);
            var moonProperties = CelestialBodiesCalculator.GetMoonProperties(DateTime, Latitude, Longitude);

            CelestialLightingController.UpdateLightProperties(SunLight, MoonLight, sunProperties, moonProperties);

            if (IsNight && CelestialLightingController.IsSunLightAboveHorizon)
                DayEvents?.Invoke();
            else if (IsDay && !CelestialLightingController.IsSunLightAboveHorizon)
                NightEvents?.Invoke();

            IsDay = CelestialLightingController.IsSunLightAboveHorizon;
            float nauticalTwilight = 0.1f;
            DayWeight = Mathf.Clamp01(Vector3.Dot(-SunLight.transform.forward, Vector3.up).Remap(0, nauticalTwilight, 0, 1));

            //if (DayProfile != null) DayProfile.weight = DayWeight;
            //if (NightProfile != null) NightProfile.weight = 1 - DayWeight;
        }

    }
}
