#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace UnityEssentials
{
    public class TimeOfDayEditor : MonoBehaviour
    {
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

        public void OnDrawGizmosSelected()
        {
            var timeOfDay = TimeOfDay.Instance;
            if (timeOfDay.CameraElevation > 1000)
                Handles.Label(transform.position, "o");
            else
            {
                if (timeOfDay.SunLight != null)
                {
                    var sunLight = timeOfDay.SunLight;
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawRay(sunLight.transform.position, -sunLight.transform.forward * 2.3f);
                    Gizmos.DrawSphere(sunLight.transform.position - sunLight.transform.forward * 2.3f, 0.2f);

                    Handles.Label(sunLight.transform.position - sunLight.transform.forward * 2.3f + Vector3.up * 0.35f, "Sun");
                }

                if (timeOfDay.MoonLight != null)
                {
                    var moonLight = timeOfDay.MoonLight;
                    Gizmos.color = Color.white;
                    Gizmos.DrawRay(moonLight.transform.position, -moonLight.transform.forward * 2f);
                    Gizmos.DrawSphere(moonLight.transform.position - moonLight.transform.forward * 2f, 0.1f);

                    Handles.Label(moonLight.transform.position - moonLight.transform.forward * 2f + Vector3.up * 0.25f, "Moon");
                }

                if(timeOfDay.GalacticUp != null)
                {
                    var galacticUp = timeOfDay.GalacticUp;
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawRay(transform.position, galacticUp * 2f);
                    Gizmos.DrawSphere(transform.position + galacticUp * 2f, 0.1f);

                    Handles.Label(transform.position + galacticUp * 2f + Vector3.up * 0.25f, "Galactic Up");
                }
            }
        }
    }
}
#endif