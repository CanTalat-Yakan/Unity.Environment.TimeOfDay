using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

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

    [ExecuteAlways]
    public class TimeOfDay : Singleton<TimeOfDay>
    {
        [Header("Time Settings")]
        [Date] public Vector3Int Date;
        [Time] public float TimeInHours;

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

        [HideInInspector] public Light SunLight;
        [HideInInspector] public HDAdditionalLightData SunLightData;
        [HideInInspector] public Light MoonLight;
        [HideInInspector] public HDAdditionalLightData MoonLightData;
        [HideInInspector] public Volume SkyVolume;
        [HideInInspector] public Volume NightVolume;
        [HideInInspector] public Material SkyMaterial;

        public DateTime DateTime { get; private set; }

        public bool IsDay { get; private set; }
        public bool IsNight => !IsDay;
        public float DayWeight { get; private set; }
        public float NightWeight => 1 - DayWeight;
        public float SpaceWeight { get; private set; }
        public Vector3 GalacticUp { get; private set; }
        public float CameraHeight { get; private set; }

        [field: SerializeField] public SunProperties SunProperties { get; private set; }
        [field: SerializeField] public MoonProperties MoonProperties { get; private set; }

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
            if (Location == 0)
                return;

            var preset = _locationPreset[(int)Location];
            Latitude = preset.Latitude;
            Longitude = preset.Longitude;
            UTCOffset = preset.UTCOffset;
        }

        [OnValueChanged("Latitude", "Longitude", "UTCOffset")]
        public void OnCustomValueChanged() =>
            Location = PresetLocations.Custom;

        [OnValueChanged("Date")]
        public void OnDateValueChanged() =>
            _staticDateTime = null;

        void Update()
        {
            GetCurrentTimeUTC();
            UpdateCelestialTargets();
        }

        private void GetCurrentTimeUTC() =>
            DateTime = GetTime().AddHours(TimeInHours - UTCOffset);

        private DateTime? _staticDateTime;
        private DateTime GetTime() =>
            _staticDateTime ??= new DateTime(Date.x, Date.y, Date.z, 0, 0, 0, DateTimeKind.Utc);

        private const string SkyPropertyName = "_RotationMatrix";
        private const string EarthPropertyName = "_EarthRotationMatrix";
        private const string SpaceWeightName = "_SpaceWeight";
        private static readonly int s_earthPropertyID = Shader.PropertyToID(EarthPropertyName);
        private static readonly int s_skyPropertyID = Shader.PropertyToID(SkyPropertyName);
        private static readonly int s_spaceWeightPropertyID = Shader.PropertyToID(SpaceWeightName);
        private Quaternion _sunRotation;
        private Quaternion _moonRotation;
        private Quaternion _skyRotation;
        private Quaternion _skyRotationLerped;
        private Vector3 _earthRotationOffset = new Vector3(164.5f, 20.5f, 12.25f);
        private void UpdateCelestialTargets()
        {
            var sunDirection = CelestialBodiesCalculator.GetSunDirection(DateTime, Latitude, Longitude).ToVector3();
            var moonDirection = CelestialBodiesCalculator.GetMoonDirection(DateTime, Latitude, Longitude).ToVector3();
            var galacticUp = CelestialBodiesCalculator.GetGalacticUpDirection(DateTime, Latitude, Longitude).ToVector3();
            var sunStaticDirection = CelestialBodiesCalculator.GetSunDirection(GetTime(), Latitude, Longitude).ToVector3();
            var solarUp = CelestialBodiesCalculator.GetSolarSystemUpDirection(GetTime(), Latitude, Longitude).ToVector3();

            GalacticUp = galacticUp.normalized;
            SpaceWeight = GetSpaceWeight();
            CameraHeight = GetCurrentRenderingCameraHeight();

            if (SunLight != null && MoonLight != null && SkyMaterial != null)
            {
                _sunRotation = Quaternion.LookRotation(-sunDirection, galacticUp);
                SunLight.transform.rotation = Quaternion.Lerp(SunLight.transform.rotation, _sunRotation, Time.deltaTime);

                _moonRotation = Quaternion.LookRotation(-moonDirection, galacticUp);
                MoonLight.transform.rotation = Quaternion.Lerp(MoonLight.transform.rotation, _moonRotation, Time.deltaTime);
                MoonLightData.earthshine = GetMoonEarthshine();

                _skyRotation = CalculateCelestialRotation(sunDirection, galacticUp);
                _skyRotationLerped = Quaternion.Lerp(_skyRotationLerped, _skyRotation, Time.deltaTime);
                SkyMaterial?.SetMatrix(s_skyPropertyID, GetRotationMatrix(_skyRotationLerped));
                SkyMaterial?.SetFloat(s_spaceWeightPropertyID, SpaceWeight);

                var earthSolarRotation = CalculateCelestialRotation(sunStaticDirection, solarUp);
                SkyMaterial?.SetMatrix(s_earthPropertyID, GetRotationMatrix(earthSolarRotation, _earthRotationOffset));

#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    SunLight.transform.rotation = _sunRotation;
                    MoonLight.transform.rotation = _moonRotation;
                    SkyMaterial.SetMatrix(s_skyPropertyID, GetRotationMatrix(_skyRotation));
                }
#endif
            }

            SunProperties = CelestialBodiesCalculator.GetSunProperties(DateTime, Latitude, Longitude);
            MoonProperties = CelestialBodiesCalculator.GetMoonProperties(DateTime, Latitude, Longitude);

            CelestialLightingController.UpdateLightProperties(SunLight, MoonLight, SunProperties, MoonProperties, SpaceWeight);

            if (IsNight && CelestialLightingController.IsSunLightAboveHorizon)
                DayEvents?.Invoke();
            else if (IsDay && !CelestialLightingController.IsSunLightAboveHorizon)
                NightEvents?.Invoke();

            IsDay = CelestialLightingController.IsSunLightAboveHorizon;

            const float nauticalTwilight = 0.1f;
            DayWeight = Mathf.Clamp01(Vector3.Dot(-SunLight.transform.forward, Vector3.up).Remap(0, nauticalTwilight, 0, 1));

            if (NightVolume != null)
                NightVolume.weight = Mathf.Max(NightWeight, SpaceWeight);
        }

        private Quaternion CalculateCelestialRotation(Vector3 direction, Vector3 up) =>
            Quaternion.LookRotation(-direction, up);

        private Matrix4x4 GetRotationMatrix(Quaternion rotation, Vector3? rotationOffset = null)
        {
            rotationOffset ??= Vector3.zero;
            var offsetRotation = Quaternion.Euler(rotationOffset.Value);
            var finalRotation = rotation * offsetRotation;
            return Matrix4x4.Rotate(finalRotation).inverse;
        }

        private float GetCurrentRenderingCameraHeight()
        {
#if UNITY_EDITOR
            // Prefer SceneView camera if available and focused
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null && sceneView.camera != null && sceneView.hasFocus)
                return Mathf.Max(100, sceneView.camera.transform.position.magnitude);
#endif
            // Fallback to main camera
            if (Camera.main != null)
                return Mathf.Max(100, Camera.main.transform.position.magnitude);

            return 0f;
        }

        private float GetSpaceWeight()
        {
            const float outerspaceThreshold = 100_000f;
            return Mathf.Clamp01(CameraHeight / outerspaceThreshold);
        }

        private float GetMoonEarthshine()
        {
            const float minEarthshine = 0.01f;
            return minEarthshine * (1 - SpaceWeight);
        }

#if UNITY_EDITOR
        public void OnDrawGizmosSelected()
        {
            if (CameraHeight > 1000)
                Handles.Label(transform.position, "o");
            else
            {
                if (SunLight != null)
                {
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawRay(SunLight.transform.position, -SunLight.transform.forward * 2.3f);
                    Gizmos.DrawSphere(SunLight.transform.position - SunLight.transform.forward * 2.3f, 0.2f);

                    Handles.Label(SunLight.transform.position - SunLight.transform.forward * 2.3f + Vector3.up * 0.35f, "Sun");
                }

                if (MoonLight != null)
                {
                    Gizmos.color = Color.white;
                    Gizmos.DrawRay(MoonLight.transform.position, -MoonLight.transform.forward * 2f);
                    Gizmos.DrawSphere(MoonLight.transform.position - MoonLight.transform.forward * 2f, 0.1f);

                    Handles.Label(MoonLight.transform.position - MoonLight.transform.forward * 2f + Vector3.up * 0.25f, "Moon");
                }

                if (GalacticUp != null)
                {
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawRay(transform.position, GalacticUp * 2f);
                    Gizmos.DrawSphere(transform.position + GalacticUp * 2f, 0.1f);

                    Handles.Label(transform.position + GalacticUp * 2f + Vector3.up * 0.25f, "Galactic Up");
                }
            }
        }
#endif
    }
}