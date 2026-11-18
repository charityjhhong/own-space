# Diagnostics

Owner: `build`. Performance overlays and diagnostics.

## DiagnosticsRoot (performance overlay)

`DiagnosticsRoot.prefab` shows FPS, CPU and GPU frame time (ms) and the largest GC allocation in a frame, updated twice a second. Red rows are over budget (under 70 FPS, over 13.9 ms, any GC allocation).

- Editor and development builds only; in a release build the object destroys itself. Make a development build with `OWNSPACE_DEVELOPMENT=1 .github/scripts/build-android.sh`.
- Sits in the upper-left of the view (about 1.2 m away, small) so it stays off the tool panel. It is on at start in the Editor and development builds, and absent in release builds.
- Toggle in Play mode with *OwnSpace > Diagnostics > Toggle perf overlay*; on a headset call `PerfOverlay.Instance.Toggle()` from a debug gesture (`PerfOverlay.Instance` is null in release builds).
- Allocates nothing after `Awake`: it draws a 3x5 pixel font as quads, no TextMeshPro or strings.
- CPU/GPU show `N/A` until **Frame Timing Stats** is on in Player Settings (`xr` owns that file).
- `PerfOverlay.Toggle()` / `SetVisible(bool)` are the hooks for a debug gesture.
- Regenerate the prefab with the menu *OwnSpace > Diagnostics > Create DiagnosticsRoot prefab*.
