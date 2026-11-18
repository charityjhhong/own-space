using System;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// Development builds only: every <see cref="Seconds"/> s, the average CPU main-thread, CPU render-thread and GPU frame
    /// times from Unity's FrameTimingManager (Frame Timing Stats is on in Player settings), with what the app is showing
    /// (visit, sheet, occlusion, pieces). Device feedback on 1b913d6: 65 FPS after "You're in Dorm" and 70 with Settings
    /// open, but the overlay's GPU reads 0.0, so the cost could not be placed. One line per period; no per-frame allocation.
    /// </summary>
    public sealed class FrameTimeLog : MonoBehaviour
    {
        public const float Seconds = 5f;
        const int Samples = 30;

        readonly FrameTiming[] _timings = new FrameTiming[Samples];
        float _next;

        /// <summary>What is on screen, built once per period.</summary>
        public Func<string> Context { get; set; }

        bool _memoryLogged;

        void Update()
        {
            // Once, when the room and the first design are up: what holds memory (MemoryReport).
            if (!_memoryLogged && Time.unscaledTime >= MemoryReport.AfterSeconds) { _memoryLogged = true; MemoryReport.Log(); }
            FrameTimingManager.CaptureFrameTimings();
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Seconds;
            uint n = FrameTimingManager.GetLatestTimings(Samples, _timings);
            if (n == 0) { Debug.Log("[OwnSpace.MR] Frame time: no timings (Frame Timing Stats off, or not supported here)."); return; }
            double main = 0, render = 0, gpu = 0, frame = 0;
            for (int i = 0; i < n; i++)
            {
                main += _timings[i].cpuMainThreadFrameTime;
                render += _timings[i].cpuRenderThreadFrameTime;
                gpu += _timings[i].gpuFrameTime;
                frame += _timings[i].cpuFrameTime;
            }
            Debug.Log($"[OwnSpace.MR] Frame time over {n}: frame {frame / n:F1} ms, CPU main {main / n:F1}, render {render / n:F1}, GPU {gpu / n:F1}; {Context?.Invoke()}");
        }
    }
}
