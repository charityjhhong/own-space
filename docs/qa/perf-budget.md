# Performance budget: how to measure

Targets come from the team plan, "Quality bar > Performance budget" (Quest 3 / 3S, worst scene). This page says how to measure each one and holds the results table. Anything not confirmed from official Meta or Unity docs is marked **verify**. Everything on a device is `needs-headset`.

## Setup once

- Device in developer mode, USB cable, `adb devices` shows it (Meta Quest Developer Hub, MQDH, installs adb on Mac; **verify** the path or use `brew install android-platform-tools`).
- **OVR Metrics Tool** on the headset (from the Horizon Store or MQDH). **verify** the current install route.
- Development build with the overlay: `OWNSPACE_DEVELOPMENT=1 OWNSPACE_VERSION=0.<phase>.<n> OWNSPACE_VERSION_CODE=<n> .github/scripts/build-android.sh`, then `adb install -r app/Builds/<apk>`. Development builds are never uploaded.
- Enable Player Settings > **Frame Timing Stats** (owner `xr`) so the in-app overlay shows CPU/GPU ms.
- Measure in the **worst scene**: most furniture placed, all three zones filled, evening mood (heaviest lighting), a real room scanned.

## Per item

| Item | Target | Quest | XR Simulator / Editor |
| --- | --- | --- | --- |
| Frame rate | 72 Hz steady | OVR Metrics Tool overlay on, plus its CSV recording for 5 minutes; read FPS and stale/dropped frames. Cross-check with the in-app `DiagnosticsRoot` overlay (FPS row). | Not meaningful: the Editor frame rate depends on the Mac. Only check nothing regressed against the last run. |
| Frame time | CPU and GPU each under 13.9 ms | Unity Profiler attached (below), read CPU and GPU in the Timeline; the overlay shows averages every 0.5 s (red when over). OVR Metrics also shows CPU/GPU utilisation and level. | Editor Profiler gives CPU time only; GPU number is not representative. |
| Draw calls | 150 or fewer per scene | Unity Profiler > Rendering module (Batches, SetPass calls) on device. RenderDoc for Meta Quest is the capture tool (**verify**). | Frame Debugger in the Editor (Window > Analysis > Frame Debugger) lists draw calls; a good proxy. |
| Per-frame GC allocation | 0 B | Profiler: CPU Usage > Hierarchy, sort by *GC Alloc* during play; overlay row `GC` shows the largest allocation in a frame (red above 0). Use Deep Profile only to locate a source. | Same in the Editor Profiler. Editor-only allocations (inspector, editor loop) do not count: look at the player-loop rows. |
| Cold start | Under 5 s from icon to first interaction | Force-stop, then `adb shell am start -W -n com.charityjhhong.ownspace/com.unity3d.player.UnityPlayerGameActivity` prints `TotalTime` (to first frame; the activity is `UnityPlayerGameActivity` per `AndroidManifest.xml`). Then stopwatch to first working interaction. Average of 3 runs. | Not applicable. |
| Long session | 30 min, no thermal warning, crash or memory growth | OVR Metrics recording (CPU/GPU level, temperature) plus `adb logcat -s Unity ActivityManager`; before and after: `adb shell dumpsys meminfo com.charityjhhong.ownspace` and compare Native and Graphics heap. Note any thermal toast. | Not applicable. |

## Static scene budget (batch-mode smoke)

`.github/scripts/perf-smoke.sh` opens `Main.unity`, adds every outside-zone catalog prefab of one style (a full outside layout, for each style in turn) and fails if the worst style breaks a limit. It is a static estimate on the Mac, no GPU: it catches runaway assets before device day and does not replace the measurements below. Runtime-built objects (outside shell, finishes, lights) are not in the scene until Play and are **not counted**, so the device number is higher.

| Metric | Limit (starting value) | How it is counted |
| --- | --- | --- |
| Draw calls | 150 (team plan) | One per submesh of every active renderer, before batching: an upper bound |
| Triangles | 750,000 (**verify**: lock after the first device run) | Sum of triangle index counts |
| Materials | 60 (**verify**) | Unique materials, a proxy for SetPass calls with the SRP Batcher |
| Texture memory | 256 MB (**verify**) | Unique textures at editor size, an upper bound for ASTC on Quest |

Change the limits in `PerfSmoke.cs` and in this table together, in one PR.

## Unity Profiler over USB from the Mac

1. Build with `OWNSPACE_DEVELOPMENT=1` (the script sets Development and *Autoconnect Profiler*, so the app connects to the Editor Profiler on start when both are on the same network or USB; if it does not, attach manually below).
2. Install and start the app on the headset (cable connected).
3. In the Editor: Window > Analysis > Profiler > the target dropdown > pick the Quest (listed as `Android Player (<serial>)`). If it is missing: `adb forward tcp:34999 localabstract:Unity-com.charityjhhong.ownspace` then choose *Direct Connection* (**verify** the port and socket name for Unity 6).
4. Record 60 s in the worst scene; save the capture to `docs/qa/captures/` (large files go through Git LFS; **verify** `.gitattributes` first).
5. Remember the profiler itself costs frame time; confirm the final frame-rate number with OVR Metrics without the Profiler attached.

## In the XR Simulator

Use it to catch regressions in logic and allocations, not to prove frame time. The Meta XR Simulator renders on the Mac GPU, so its timing says nothing about Quest. A passing simulator run never counts as acceptance (`AGENTS.md`).

## Results

Fill one row per measurement. Build = commit short hash. Pass = within target.

| Date | Build | Device | Scene | FPS (min / avg) | CPU ms | GPU ms | Draw calls | GC B/frame | Cold start s | 30-min result | Pass | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| | | | | | | | | | | | | |
