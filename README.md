# Unity Essentials

This module is part of the Unity Essentials ecosystem and follows the same lightweight, editor-first approach.
Unity Essentials is a lightweight, modular set of editor utilities and helpers that streamline Unity development. It focuses on clean, dependency-free tools that work well together.

All utilities are under the `UnityEssentials` namespace.

```csharp
using UnityEssentials;
```

## Installation

Install the Unity Essentials entry package via Unity's Package Manager, then install modules from the Tools menu.

- Add the entry package (via Git URL)
    - Window → Package Manager
    - "+" → "Add package from git URL…"
    - Paste: `https://github.com/CanTalat-Yakan/UnityEssentials.git`

- Install or update Unity Essentials packages
    - Tools → Install & Update UnityEssentials
    - Install all or select individual modules; run again anytime to update

---

# Time of Day

> Quick overview: HDRP time-of-day controller that orients sun/moon lights from date, time, and location; drives HDRP light units (Lux), sky material rotation matrices, and night/space blending. Includes an APV lighting scenario blender for seamless GI transitions.

A lightweight, ExecuteAlways controller that computes sun and moon directions from date/time and geolocation, aims your directional lights, and sets realistic HDRP intensity/color. It also rotates a sky material (starfield/spacebox) using matrices, blends night/space volumes, and exposes day/night events and weights for downstream effects. An optional editor helper blends Adaptive Probe Volume (APV) lighting scenarios over time.

![screenshot](Documentation/Screenshot.png)

## Features
- Date, time, and location
  - Inspector fields: `Date` (Y/M/D), `TimeInHours` (0–24), presets for Greenwich/Cologne/Dubrovnik/Tokyo/NewYork
  - Custom latitude/longitude (degrees) and UTC offset
- Sun and moon control (HDRP)
  - Orients two directional lights toward computed sun/moon directions (smoothed in Play, instant in Edit)
  - Drives `HDAdditionalLightData` in Lux via Celestial lighting model (intensity and color temperature/phase)
  - Optional SRP Lens Flare intensity scaled by sun altitude
  - Flags: `IsDay`, `IsNight`, `DayWeight`, `NightWeight`, and `SpaceWeight`
  - UnityEvents: `DayEvents` and `NightEvents` on transitions
- Sky material rotation
  - Sets matrices on a material: `_SkyRotationMatrix`, `_EarthRotationMatrix`, plus `_SpaceWeight`
  - Uses galactic/solar up vectors for celestial rotation; smooth interpolation
- Night/space blending
  - `NightVolume.weight = max(NightWeight, SpaceWeight)`; twilight thresholds baked-in
  - `SpaceWeight` from camera distance (via `CameraProvider.Distance`)
- Prefab included
  - `Resources/UnityEssentials_Prefab_TimeOfDay.prefab` for quick drop-in
- APV lighting scenario blender (Editor)
  - `TimeOfDayLightingScenarioBlender` bakes and blends APV lighting scenarios based on current time
  - Parses scenario names as `"<SceneName> HHmm"` and blends between nearest times
  - Quality knob: cells blended per frame; integrates with `ProbeReferenceVolumeProvider` and `APVLightingBaker`

## Requirements
- Unity 6000.0+
- HDRP
  - Uses `UnityEngine.Rendering.HighDefinition.HDAdditionalLightData`
  - Optional SRP Lens Flare (`LensFlareComponentSRP`) on the sun light
  - Adaptive Probe Volume (APV) for lighting scenarios (optional, for the blender)
- A sky material/shader that consumes `_SkyRotationMatrix`, `_EarthRotationMatrix`, and `_SpaceWeight`
- Dependencies recommended
  - Celestial Bodies Calculator module (for sun/moon/galactic vectors and properties)
  - Camera Provider module (for `CameraProvider.Distance` used by `SpaceWeight`)

## Usage

1) Add the prefab
- Drag `Resources/UnityEssentials_Prefab_TimeOfDay.prefab` into your scene
- Or add `TimeOfDay` to an empty GameObject

2) Wire references (if not pre-wired)
- Assign `SunLight` and `MoonLight` (Directional, each with `HDAdditionalLightData`)
- Assign `SkyMaterial` (your sky/space material that reads the matrices listed above)
- Assign `NightVolume` (a Volume with nighttime overrides)
- Optionally assign a `SkyVolume` if your setup needs it

3) Configure time and location
- Choose a preset or set `Latitude`, `Longitude`, and `UTCOffset`
- Set `Date` (Y/M/D) and `TimeInHours` (local clock)
- `CloudCoverage` [0..1] influences sun color temperature and moon earthshine

4) Play and iterate
- In Play Mode, rotations and weights are smoothed; in Edit Mode, orientation updates immediately
- Hook `DayEvents`/`NightEvents` to trigger ambient, audio, or gameplay changes

### Optional: APV lighting scenario blending (HDRP)
1) Add `TimeOfDayLightingScenarioBlender` to the same GameObject as `TimeOfDay`
2) Ensure APV is enabled and a `ProbeReferenceVolume` is initialized
3) Name lighting scenarios as `"<SceneName> HHmm"` (e.g., `Forest 0630`, `Forest 1200`, `Forest 1900`)
4) Use "Bake Current Time Lighting Scenario" to bake one at the current time
5) The blender will detect scenarios, pick the current/next pair, and blend based on `TimeInHours`
- Quality: `numberOfCellsBlendedPerFrame` controls blend throughput

### Sky material integration
- Your shader/graph should sample matrices set on the material:
  - `_SkyRotationMatrix` and `_EarthRotationMatrix` (Matrix4x4)
  - `_SpaceWeight` (float)
- The controller updates these every frame for celestial rotation and earth/solar alignment

## Notes and Limitations
- Pipeline: Built-in/URP are not supported out of the box (uses HDRP APIs); you can adapt by removing HDRP-specific calls
- Time base: Internally computes a UTC `DateTime` as `Date + (TimeInHours - UTCOffset)`
- Coordinates: Celestial helpers assume +Y up; rotations use `Quaternion.LookRotation(-dir, up)`
- SpaceWeight: Requires `CameraProvider.Distance`; if absent, it defaults to 0 unless you adapt the code
- APV: The blender requires APV with a valid baking set and lighting scenarios to be present in the scene

## Files in This Package
- `Runtime/TimeOfDay.cs` – Core controller (sun/moon orientation, HDRP light driving, sky matrices, day/night/space weights, events)
- `Runtime/UnityEssentials.TimeOfDay.asmdef` – Runtime assembly definition
- `Editor/TimeOfDayLightingScenarioBlender.cs` – APV lighting scenario bake and blend helper (Editor only)
- `Resources/UnityEssentials_Prefab_TimeOfDay.prefab` – Drop-in prefab
- `package.json` – Package manifest metadata

## Tags
unity, hdrp, time-of-day, sun, moon, sky, stars, space, lux, color-temperature, lens-flare, apv, lighting-scenarios, environment
