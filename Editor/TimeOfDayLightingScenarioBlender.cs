#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace UnityEssentials
{
    [Serializable]
    public class BlendProperties
    {
        [ReadOnly] public int LightingScenarioCount;
        [ReadOnly] public string CurrentLightingScenario;
        [ReadOnly] public string NextLightingScenario;
        [ReadOnly] public float CurrentBlendFactor;
    }

    [ExecuteAlways]
    [RequireComponent(typeof(TimeOfDay))]
    public class TimeOfDayLightingScenarioBlender : MonoBehaviour
    {
        [HideInInspector] public TimeOfDay TimeOfDay;
        public APVLightingBaker LightingScenarioBaker;

        [field: Space] [field: SerializeField] public BlendProperties BlendProperties { get; private set; } = new();

        private int _numberOfCellsBlendedPerFrame = 10000;

        private string[] _scenarioNames = null;
        private double[] _scenarioTimes = null;

        // Bake runner state (editor only)
        private bool _isBaking24Hours;

        public void Awake()
        {
            TimeOfDay = GetComponent<TimeOfDay>();

            ProbeReferenceVolumeProvider.AddListener(() =>
                FetchLightingScenarios(out _scenarioNames, out _scenarioTimes,
                    out BlendProperties.LightingScenarioCount));
        }

        public void Update() =>
            UpdateBlend(TimeOfDay.TimeInHours + 0.001f);

        [Button(ButtonLayout.Begin)]
        public void BakeCurrentTimeLightingScenario()
        {
            var name = GetLightingScenarioName(TimeOfDay.DateTime, TimeOfDay.UTCOffset);

            LightingScenarioBaker.BakeLightingScenario(name, async: true);

            FetchLightingScenarios(out _scenarioNames, out _scenarioTimes, out BlendProperties.LightingScenarioCount);
        }

        [Button("Bake 24 Hours")]
        public async void Bake24HourLightingScenario(bool skipOddHours = true)
        {
            for (int hour = 0; hour < 24; hour++)
            {
                if (skipOddHours && (hour % 2 == 1))
                    continue;

                TimeOfDay.TimeInHours = hour;

                // In edit mode, mark dirty so dependent systems update consistently
                if (!Application.isPlaying)
                    EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

                // Let one editor frame pass so TimeOfDay.Update runs and DateTime reflects the new hour
                await WaitForEditorUpdate();

                var scenarioName = GetLightingScenarioName(TimeOfDay.GetCurrentTimeUTC(), TimeOfDay.UTCOffset);

                bool started = LightingScenarioBaker.BakeLightingScenario(scenarioName, async: true);
                if (!started)
                {
                    Debug.LogWarning($"Failed to start baking scenario '{scenarioName}'. Aborting batch bake.");
                    return;
                }

                await WaitForBakeToFinish();
            }

            FetchLightingScenarios(out _scenarioNames, out _scenarioTimes, out BlendProperties.LightingScenarioCount);
            Debug.Log("Bake 24 Hours finished.");
        }

        private static Task WaitForEditorUpdate()
        {
            var tcs = new TaskCompletionSource<bool>();

            void Tick()
            {
                EditorApplication.update -= Tick;
                tcs.TrySetResult(true);
            }

            EditorApplication.update += Tick;
            return tcs.Task;
        }

        private static async Task WaitForBakeToFinish()
        {
            // Bake starts asynchronously; wait until Unity flips Lightmapping.isRunning true
            while (!APVLightingBaker.IsBakingInProgress)
                await WaitForEditorUpdate();

            // Then wait until it's done
            while (APVLightingBaker.IsBakingInProgress)
                await WaitForEditorUpdate();
        }

        private void OnDisable()
        {
            // Ensure we don't keep state in case of recompiles/domain reloads
            _isBaking24Hours = false;
        }

        private void UpdateBlend(double currentTimeInHours)
        {
            if (!ProbeReferenceVolumeProvider.IsInitialized)
                return;

            int count = Mathf.Min(_scenarioNames?.Length ?? 0, _scenarioTimes?.Length ?? 0);
            BlendProperties.LightingScenarioCount = count;

            if (BlendProperties.LightingScenarioCount == 0)
            {
                // No scenarios available
                BlendProperties.CurrentLightingScenario = null;
                BlendProperties.NextLightingScenario = null;
                BlendProperties.CurrentBlendFactor = 0f;
                return;
            }

            if (BlendProperties.LightingScenarioCount == 1)
            {
                // Only one scenario available
                BlendProperties.CurrentLightingScenario = _scenarioNames[0];
                BlendProperties.NextLightingScenario = _scenarioNames[0];
                BlendProperties.CurrentBlendFactor = 0f;
                return;
            }

            double previousScenarioTime = -1;
            double nextScenarioTime = -1;

            // Iterate through scenarios to find the current interval
            for (int i = 0; i < BlendProperties.LightingScenarioCount; i++)
            {
                double currentTime = _scenarioTimes[i];
                double followingTime;

                // If then determine the next scenario, looping back to the first if at the end
                // else last scenario loops to the first scenario
                followingTime = (i < BlendProperties.LightingScenarioCount - 1)
                    ? _scenarioTimes[i + 1]
                    : _scenarioTimes[0];

                // Check if current time falls within the interval
                if (IsTimeWithinInterval(currentTimeInHours, currentTime, followingTime))
                {
                    previousScenarioTime = currentTime;
                    nextScenarioTime = followingTime;
                    break;
                }
            }

            // If no interval was found, default to the last and first scenarios
            if (previousScenarioTime == -1 || nextScenarioTime == -1)
            {
                previousScenarioTime = _scenarioTimes[BlendProperties.LightingScenarioCount - 1];
                nextScenarioTime = _scenarioTimes[0];
            }

            // Calculate blend factor
            double totalTimeDifference = GetTimeDifference(previousScenarioTime, nextScenarioTime);
            if (totalTimeDifference <= 0)
            {
                // Prevent division by zero or negative blending
                BlendProperties.CurrentLightingScenario =
                    _scenarioNames[Array.IndexOf(_scenarioTimes, previousScenarioTime)];
                BlendProperties.NextLightingScenario =
                    _scenarioNames[Array.IndexOf(_scenarioTimes, previousScenarioTime)];
                BlendProperties.CurrentBlendFactor = 0f;
                return;
            }

            double elapsedTime = currentTimeInHours - previousScenarioTime;
            float blendFactor = Mathf.Clamp01((float)(elapsedTime / totalTimeDifference));

            // Update current and next scenarios with blend factor
            BlendProperties.CurrentLightingScenario =
                _scenarioNames[Array.IndexOf(_scenarioTimes, previousScenarioTime)];
            BlendProperties.NextLightingScenario = _scenarioNames[Array.IndexOf(_scenarioTimes, nextScenarioTime)];
            BlendProperties.CurrentBlendFactor = blendFactor;

            ApplyQuality(_numberOfCellsBlendedPerFrame);
            ApplyBlend(BlendProperties.CurrentLightingScenario, BlendProperties.NextLightingScenario, blendFactor);
        }

        private void ApplyQuality(int numberOfCellsBlendedPerFrame)
        {
            if (ProbeReferenceVolumeProvider.Volume != null)
                ProbeReferenceVolumeProvider.Volume.numberOfCellsBlendedPerFrame = numberOfCellsBlendedPerFrame;
        }

        private void ApplyBlend(string currentScenario, string nextScenario, float blendFactor)
        {
            if (!ProbeReferenceVolumeProvider.IsInitialized)
                return;

            if (string.IsNullOrEmpty(currentScenario) || string.IsNullOrEmpty(nextScenario))
                return;

            ApplyLightingScenario(currentScenario);
            BlendLightingScenario(nextScenario, blendFactor);
        }

        private void ApplyLightingScenario(string scenario) =>
            ProbeReferenceVolumeProvider.Volume.lightingScenario = scenario;

        private void BlendLightingScenario(string scenarioToBlend, float blendFactor) =>
            ProbeReferenceVolumeProvider.Volume.BlendLightingScenario(scenarioToBlend, blendFactor);

        private string GetLightingScenarioName(DateTime dateTime, int UTCOffset)
        {
            var UTCdateTime = dateTime.AddHours(UTCOffset);
            string sceneName = SceneManager.GetActiveScene().name;
            string hour = UTCdateTime.Hour.ToString("00");
            string minute = UTCdateTime.Minute.ToString("00");

            return $"{sceneName} {hour}{minute}";
        }

        private bool IsTimeWithinInterval(double currentTime, double startTimeInHours, double endTimeInHours)
        {
            if (endTimeInHours > startTimeInHours)
                return currentTime >= startTimeInHours && currentTime <= endTimeInHours;

            // Handle looping case where end time is less than start time
            return currentTime >= startTimeInHours || currentTime <= endTimeInHours;
        }

        private double GetTimeDifference(double startTimeInHours, double endTimeInHours)
        {
            if (endTimeInHours > startTimeInHours)
                return endTimeInHours - startTimeInHours;

            // Assuming time wraps around at 24 hours for looping
            return (24.0 - startTimeInHours) + endTimeInHours;
        }

        private void FetchLightingScenarios(out string[] _scenarioNames, out double[] _scenarioTimes,
            out int lightingScenarioCount)
        {
            lightingScenarioCount = 0;

            var data = new List<(string, double)>();

            if (ProbeReferenceVolumeProvider.Volume.currentBakingSet != null)
                foreach (var scenarioName in ProbeReferenceVolumeProvider.Volume.currentBakingSet.lightingScenarios)
                    if (TryParseScenarioNameForTime(scenarioName, out double timeInHours))
                        data.Add((scenarioName.ToString(), timeInHours));

            // Sort scenarios by time
            data.Sort((a, b) => a.Item2.CompareTo(b.Item2));

            _scenarioNames = new string[data.Count];
            _scenarioTimes = new double[data.Count];

            for (int i = 0; i < data.Count; i++)
            {
                _scenarioNames[i] = data[i].Item1;
                _scenarioTimes[i] = data[i].Item2;
            }

            lightingScenarioCount = data.Count;
        }

        private bool TryParseScenarioNameForTime(string scenarioName, out double timeInHours)
        {
            timeInHours = 0.0;

            string timePart = scenarioName.Split(' ').Last();
            if (timePart.Length == 4 && int.TryParse(timePart, out int timeValue))
            {
                int hours = timeValue / 100;
                int minutes = timeValue % 100;
                timeInHours = hours + (minutes / 60.0);

                return true;
            }

            return false;
        }
    }
}
#endif