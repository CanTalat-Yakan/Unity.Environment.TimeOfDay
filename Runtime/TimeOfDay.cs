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
    public class TimeOfDay : MonoBehaviour
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
        [HideInInspector] public Light MoonLight;
        [HideInInspector] public Volume SkyVolume;
        [HideInInspector] public Volume NightVolume;
        [HideInInspector] public Material SkyMaterial;

        public DateTime DateTime { get; private set; }

        public bool IsDay { get; private set; }
        public bool IsNight => !IsDay;
        public float DayWeight { get; private set; }
        public float NightWeight => 1 - DayWeight;

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

#if UNITY_EDITOR
        [MenuItem("GameObject/Essentials/Time of Day", false, priority = 100)]
        private static void InstantiateAdvancedSpotLight(MenuCommand menuCommand)
        {
            var prefab = ResourceLoaderEditor.InstantiatePrefab("UnityEssentials_Prefab_TimeOfDay", "Time of Day");
            if (prefab != null)
            {
                var timeOfDay = prefab.GetComponent<TimeOfDay>();
                timeOfDay.SunLight = prefab.transform.Find("Directional Sun Light")?.GetComponent<Light>();
                timeOfDay.MoonLight = prefab.transform.Find("Directional Moon Light")?.GetComponent<Light>();
                timeOfDay.SkyVolume = prefab.transform.Find("Physical Based Sky Volume")?.GetComponent<Volume>();
                timeOfDay.NightVolume = prefab.transform.Find("Night Color Adjustment Volume")?.GetComponent<Volume>();
                if (timeOfDay.SkyVolume.profile.TryGet<PhysicallyBasedSky>(out var skyOverride))
                    timeOfDay.SkyMaterial = skyOverride.material.value;
            }

            GameObjectUtility.SetParentAndAlign(prefab, menuCommand.context as GameObject);
            Undo.RegisterCreatedObjectUndo(prefab, "Create Time of Day");
            Selection.activeObject = prefab;
        }
#endif

        void Update()
        {
            GetCurrentTimeUTC();
            UpdateCelestialTargets();
        }

        private void GetCurrentTimeUTC() =>
            DateTime = new DateTime(Date.x, Date.y, Date.z, 0, 0, 0, DateTimeKind.Utc).AddHours(TimeInHours - UTCOffset);

        private void UpdateCelestialTargets()
        {
            var sunDirection = CelestialBodiesCalculator.GetSunDirection(DateTime, Latitude, Longitude).ToVector3();
            var moonDirection = CelestialBodiesCalculator.GetMoonDirection(DateTime, Latitude, Longitude).ToVector3();

            if (SunLight != null && MoonLight != null)
            {
                var sunRotation = Quaternion.LookRotation(-sunDirection, Vector3.up);
                SunLight.transform.rotation = Quaternion.Lerp(SunLight.transform.rotation, sunRotation, Time.deltaTime);
                SunLight.transform.rotation = sunRotation;

                SkyMaterial?.SetMatrix(s_skyPropertyID, GetSkyRotation());

                var moonRotation = Quaternion.LookRotation(-moonDirection, Vector3.up);
                MoonLight.transform.rotation = Quaternion.Lerp(MoonLight.transform.rotation, moonRotation, Time.deltaTime);
            }

            SunProperties = CelestialBodiesCalculator.GetSunProperties(DateTime, Latitude, Longitude);
            MoonProperties = CelestialBodiesCalculator.GetMoonProperties(DateTime, Latitude, Longitude);

            CelestialLightingController.UpdateLightProperties(SunLight, MoonLight, SunProperties, MoonProperties);

            if (IsNight && CelestialLightingController.IsSunLightAboveHorizon)
                DayEvents?.Invoke();
            else if (IsDay && !CelestialLightingController.IsSunLightAboveHorizon)
                NightEvents?.Invoke();

            IsDay = CelestialLightingController.IsSunLightAboveHorizon;

            const float nauticalTwilight = 0.1f;
            DayWeight = Mathf.Clamp01(Vector3.Dot(-SunLight.transform.forward, Vector3.up).Remap(0, nauticalTwilight, 0, 1));

            if (NightVolume != null)
                NightVolume.weight = NightWeight;
        }

        private const string SkyPropertyName = "_Rotation";
        private static readonly int s_skyPropertyID = Shader.PropertyToID(SkyPropertyName);
        private Matrix4x4 GetSkyRotation()
        {
            var offsetVector = new Vector3(90, 0, 0);
            var offsetRotation = Quaternion.AngleAxis(90f, offsetVector.normalized);
            var debugMatrix = Matrix4x4.Rotate(offsetRotation);

            var rotationMatrix = Matrix4x4.TRS(Vector3.zero, SunLight.transform.rotation, Vector3.one);
            return rotationMatrix * debugMatrix;
        }

#if UNITY_EDITOR
        public void OnDrawGizmosSelected()
        {
            if (SunLight != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawRay(SunLight.transform.position, -SunLight.transform.forward * 2.3f);
                Gizmos.DrawSphere(SunLight.transform.position - SunLight.transform.forward * 2.3f, 0.2f);

                Handles.Label(SunLight.transform.position - SunLight.transform.forward * 2.3f + Vector3.up * 0.5f, "Sun");
            }

            if (MoonLight != null)
            {
                Gizmos.color = Color.white;
                Gizmos.DrawRay(MoonLight.transform.position, -MoonLight.transform.forward * 2f);
                Gizmos.DrawSphere(MoonLight.transform.position - MoonLight.transform.forward * 2f, 0.1f);

                Handles.Label(MoonLight.transform.position - MoonLight.transform.forward * 2f + Vector3.up * 0.5f, "Moon");
            }
        }
#endif
    }
}
