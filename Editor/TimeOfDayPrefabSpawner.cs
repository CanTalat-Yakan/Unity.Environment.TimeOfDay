#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace UnityEssentials
{
    public class TimeOfDayPrefabSpawner
    {
        [MenuItem("GameObject/Essentials/Time of Day", false, priority = 100)]
        private static void InstantiateTimeOfDay(MenuCommand menuCommand)
        {
            var prefab = ResourceLoaderEditor.InstantiatePrefab("UnityEssentials_Prefab_TimeOfDay", "Time of Day");
            if (prefab != null)
            {
                var timeOfDay = prefab.GetComponent<TimeOfDay>();

                timeOfDay.SunLight = prefab.transform.Find("Directional Sun Light").GetComponent<Light>();
                timeOfDay.SunLightData = prefab.transform.Find("Directional Sun Light").GetComponent<HDAdditionalLightData>();

                timeOfDay.MoonLight = prefab.transform.Find("Directional Moon Light").GetComponent<Light>();
                timeOfDay.MoonLightData = prefab.transform.Find("Directional Moon Light").GetComponent<HDAdditionalLightData>();

                timeOfDay.NightVolume = prefab.transform.Find("Night Color Adjustment Volume").GetComponent<Volume>();
                timeOfDay.SkyVolume = prefab.transform.Find("Physical Based Sky Volume").GetComponent<Volume>();
                if (timeOfDay.SkyVolume.profile.TryGet<PhysicallyBasedSky>(out var skyOverride))
                    timeOfDay.SkyMaterial = skyOverride.material.value;
            }

            GameObjectUtility.SetParentAndAlign(prefab, menuCommand.context as GameObject);
            Undo.RegisterCreatedObjectUndo(prefab, "Create Time of Day");
            Selection.activeObject = prefab;
        }
    }
}
#endif