using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using static UnityEngine.LightAnchor;

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
        public float CameraElevation { get; private set; }

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

#if UNITY_EDITOR
        [MenuItem("GameObject/Essentials/Time of Day", false, priority = 100)]
        private static void InstantiateAdvancedSpotLight(MenuCommand menuCommand)
        {
            var prefab = ResourceLoaderEditor.InstantiatePrefab("UnityEssentials_Prefab_TimeOfDay", "Time of Day");
            if (prefab != null)
            {
                var timeOfDay = prefab.GetComponent<TimeOfDay>();
                timeOfDay.SunLight = prefab.transform.Find("Directional Sun Light")?.GetComponent<Light>();
                timeOfDay.SunLightData = prefab.transform.Find("Directional Sun Light")?.GetComponent<HDAdditionalLightData>();
                timeOfDay.MoonLight = prefab.transform.Find("Directional Moon Light")?.GetComponent<Light>();
                timeOfDay.MoonLightData = prefab.transform.Find("Directional Moon Light")?.GetComponent<HDAdditionalLightData>();
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
            DateTime = GetTime().AddHours(TimeInHours - UTCOffset);

        private DateTime? _staticDateTime;
        private DateTime GetTime() =>
            _staticDateTime ??= new DateTime(Date.x, Date.y, Date.z, 0, 0, 0, DateTimeKind.Utc);

        private const string SkyPropertyName = "_RotationMatrix";
        private const string EarthPropertyName = "_EarthRotationMatrix";
        private const string StarWeightPropertyName = "_Star_Correction_Weight";
        private const string SpaceWeightName = "_SpaceWeight";
        private static readonly int s_earthPropertyID = Shader.PropertyToID(EarthPropertyName);
        private static readonly int s_skyPropertyID = Shader.PropertyToID(SkyPropertyName);
        private static readonly int s_starWeightPropertyID = Shader.PropertyToID(StarWeightPropertyName);
        private static readonly int s_spaceWeightPropertyID = Shader.PropertyToID(SpaceWeightName);
        private Quaternion _sunRotation;
        private Quaternion _moonRotation;
        private Quaternion _skyRotation;
        private Quaternion _skyRotationLerped;
        private void UpdateCelestialTargets()
        {
            var sunDirection = CelestialBodiesCalculator.GetSunDirection(DateTime, Latitude, Longitude).ToVector3();
            var moonDirection = CelestialBodiesCalculator.GetMoonDirection(DateTime, Latitude, Longitude).ToVector3();
            var galacticUp = CelestialBodiesCalculator.GetGalacticUpDirection(DateTime, Latitude, Longitude).ToVector3();
            var sunStaticDirection = CelestialBodiesCalculator.GetSunDirection(GetTime(), Latitude, Longitude).ToVector3();
            var solarUp = CelestialBodiesCalculator.GetSolarSystemUpDirection(GetTime(), Latitude, Longitude).ToVector3();

            SpaceWeight = GetSpaceWeight();
            CameraElevation = GetCurrentRenderingCameraHeight();

            if (SunLight != null && MoonLight != null && SkyMaterial != null)
            {
                _sunRotation = Quaternion.LookRotation(-sunDirection, galacticUp);
                SunLight.transform.rotation = Quaternion.Lerp(SunLight.transform.rotation, _sunRotation, Time.deltaTime);

                _moonRotation = Quaternion.LookRotation(-moonDirection, galacticUp);
                MoonLight.transform.rotation = Quaternion.Lerp(MoonLight.transform.rotation, _moonRotation, Time.deltaTime);
                MoonLightData.earthshine = GetMoonEarthshine();

                _skyRotation = CalculateSkyRotation(sunDirection, galacticUp);
                _skyRotationLerped = Quaternion.Lerp(_skyRotationLerped, _skyRotation, Time.deltaTime);
                SkyMaterial?.SetMatrix(s_skyPropertyID, GetRotationMatrix(_skyRotationLerped));
                SkyMaterial?.SetFloat(s_starWeightPropertyID, DayWeight);
                SkyMaterial?.SetFloat(s_spaceWeightPropertyID, SpaceWeight);

                var earthRotationOffset = new Vector3(164.5f, 20.5f, 12.25f);
                var earthSolarRotation = CalculateSolarRotation(sunStaticDirection, solarUp);
                SkyMaterial?.SetMatrix(s_earthPropertyID, GetRotationMatrix(earthSolarRotation, earthRotationOffset));

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
            DayWeight = Mathf.Clamp01(Vector3.Dot(-SunLight.transform.forward, Vector3.up).Remap(-0.1f, nauticalTwilight, 0, 1));

            if (NightVolume != null)
                NightVolume.weight = NightWeight;
        }

        private Quaternion CalculateSkyRotation(Vector3 sunDirection, Vector3 galacticUp)
        {
            var finalRotation = Quaternion.LookRotation(-sunDirection, galacticUp);
            return finalRotation;
        }

        private Quaternion CalculateSolarRotation(Vector3 sunDirection, Vector3 solarUp)
        {
            var finalRotation = Quaternion.LookRotation(-sunDirection, solarUp);
            return finalRotation;
        }

        private Quaternion CalculateEarthRotation(float latitude, float longitude)
        {
            var finalRotation = Quaternion.Euler(latitude.Remap(-90, 90, 180, 0), 0f, longitude);
            return finalRotation;
        }

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
                return Mathf.Max(100, sceneView.camera.transform.position.y);
#endif
            // Fallback to main camera
            if (Camera.main != null)
                return Mathf.Max(100, Camera.main.transform.position.y);

            return 0f;
        }

        private float GetSpaceWeight()
        {
            const float outerspaceThreshold = 100_000f;
            return Mathf.Clamp01(CameraElevation / outerspaceThreshold);
        }

        private float GetMoonEarthshine()
        {
            const float minEarthshine = 0.01f;
            return minEarthshine * SpaceWeight;
        }

#if UNITY_EDITOR
        public void OnDrawGizmosSelected()
        {
            if (CameraElevation > 1000)
                Handles.Label(transform.position, "o");
            else
            {
                if (SunLight != null)
                {
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawRay(SunLight.transform.position, -SunLight.transform.forward * 2.3f);
                    Gizmos.DrawSphere(SunLight.transform.position - SunLight.transform.forward * 2.3f, 0.2f);

                    //Handles.Label(SunLight.transform.position - SunLight.transform.forward * 2.3f + Vector3.up * 0.5f, "Sun");
                }
                if (MoonLight != null)
                {
                    Gizmos.color = Color.white;
                    Gizmos.DrawRay(MoonLight.transform.position, -MoonLight.transform.forward * 2f);
                    Gizmos.DrawSphere(MoonLight.transform.position - MoonLight.transform.forward * 2f, 0.1f);

                    //Handles.Label(MoonLight.transform.position - MoonLight.transform.forward * 2f + Vector3.up * 0.5f, "Moon");
                }

                {
                    Gizmos.color = Color.cyan;
                    Vector3 galacticUp = CelestialBodiesCalculator
                        .GetGalacticUpDirection(DateTime, Latitude, Longitude).ToVector3();
                    Gizmos.DrawRay(transform.position, galacticUp * 2f);
                    Gizmos.DrawSphere(transform.position + galacticUp * 2f, 0.1f);
                    //Handles.Label(transform.position + galacticUp * 2f + Vector3.up * 0.5f, "Galactic Up");
                }
            }
        }

        void OnDrawGizmos()
        {
        }
#endif
    }
}
