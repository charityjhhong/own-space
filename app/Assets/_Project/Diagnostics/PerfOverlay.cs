using System;
using UnityEngine;


namespace OwnSpace.Diagnostics
{
    /// <summary>
    /// World-space performance readout: FPS, CPU and GPU frame time, and GC bytes allocated per frame.
    /// Sits in the upper-left of the view (about 1.2 m away) so it stays off the tool panel.
    /// Visible only in the Editor and development builds, on by default; in release builds the object destroys itself.
    /// Toggle it with <see cref="Toggle"/> (e.g. from a debug gesture or the Editor menu).
    /// Allocates nothing after Awake. CPU/GPU are the app's own frame times from the Meta runtime
    /// (OVRPlugin perf metrics) when the device reports them; otherwise the row is labelled FRM (Unity frame time,
    /// which is capped by the 72 Hz vsync and says nothing about headroom) and GPU shows N/A.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PerfOverlay : MonoBehaviour
    {
        const float RefreshSeconds = 0.5f;
        const double BudgetMs = 13.9;      // 72 Hz
        const float MinFps = 70f;

#pragma warning disable CS0414 // serialized fields are only read in Editor/development builds
        [SerializeField] MeshFilter textMesh;
        [SerializeField] MeshFilter alertMesh;
        [SerializeField] GameObject visual;
        [SerializeField] bool visibleAtStart = true;
        [SerializeField, Tooltip("Metres in front of the eyes")] float distance = 1.2f;
        [SerializeField, Tooltip("Degrees above the gaze direction (negative = below)")] float pitchUp = 18f;
        [SerializeField, Tooltip("Degrees to the left of the gaze direction (negative = right)")] float yawLeft = 22f;
        [SerializeField] float pixelSize = 0.002f;
#pragma warning restore CS0414

        readonly char[] _line = new char[24];
        readonly FrameTiming[] _timing = new FrameTiming[1];
        bool _ovrCpu, _ovrGpu;
        QuadBatch _text, _alert;
        Mesh _textM, _alertM;
        Transform _cam;
        float _camSearchAt;
        bool _snap;

        float _elapsed;
        int _frames;
        double _cpuSum, _gpuSum;
        int _timingFrames, _gpuFrames;
        long _lastAlloc, _maxAlloc;
        int _lastCollections, _collections;

        /// <summary>The live overlay in a development build or the Editor; null in release builds.</summary>
        public static PerfOverlay Instance { get; private set; }

        public bool Visible => visual != null && visual.activeSelf;
        public void SetVisible(bool value) { if (visual != null) visual.SetActive(value); }
        public void Toggle() => SetVisible(!Visible);

        void Awake()
        {
#if !(UNITY_EDITOR || DEVELOPMENT_BUILD)
            Destroy(gameObject);   // off in release builds
            return;
#else
            Instance = this;
            _ovrCpu = OVRPlugin.IsPerfMetricsSupported(OVRPlugin.PerfMetrics.App_CpuTime_Float);
            _ovrGpu = OVRPlugin.IsPerfMetricsSupported(OVRPlugin.PerfMetrics.App_GpuTime_Float);
            _text = new QuadBatch(400);
            _alert = new QuadBatch(400);
            _textM = new Mesh { name = "PerfText" };
            _alertM = new Mesh { name = "PerfAlert" };
            _textM.MarkDynamic();
            _alertM.MarkDynamic();
            textMesh.sharedMesh = _textM;
            alertMesh.sharedMesh = _alertM;
            SetVisible(visibleAtStart);
            _lastAlloc = GC.GetAllocatedBytesForCurrentThread();
            _lastCollections = GC.CollectionCount(0);
#endif
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_textM != null) Destroy(_textM);
            if (_alertM != null) Destroy(_alertM);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        void LateUpdate()
        {
            Sample();
            Follow();
        }

        void Sample()
        {
            long alloc = GC.GetAllocatedBytesForCurrentThread();
            long delta = alloc - _lastAlloc;
            _lastAlloc = alloc;
            if (delta > _maxAlloc) _maxAlloc = delta;
            int collections = GC.CollectionCount(0);
            _collections += collections - _lastCollections;
            _lastCollections = collections;

            if (_ovrCpu)
            {
                float? c = OVRPlugin.GetPerfMetricsFloat(OVRPlugin.PerfMetrics.App_CpuTime_Float);
                float? g = _ovrGpu ? OVRPlugin.GetPerfMetricsFloat(OVRPlugin.PerfMetrics.App_GpuTime_Float) : null;
                if (c.HasValue)
                {
                    _cpuSum += ToMs(c.Value);
                    _timingFrames++;
                    if (g.HasValue) { _gpuSum += ToMs(g.Value); _gpuFrames++; }
                }
            }
            else
            {
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, _timing) > 0 && _timing[0].cpuFrameTime > 0.0)
                {
                    _cpuSum += _timing[0].cpuFrameTime;
                    _timingFrames++;
                }
            }

            _frames++;
            _elapsed += Time.unscaledDeltaTime;
            if (_elapsed < RefreshSeconds) return;

            Refresh();
            _elapsed = 0f; _frames = 0; _cpuSum = _gpuSum = 0.0; _timingFrames = _gpuFrames = 0;
            _maxAlloc = 0; _collections = 0;
        }

        void Refresh()
        {
            _text.Clear();
            _alert.Clear();
            float fps = _frames / _elapsed;
            bool hasTiming = _timingFrames > 0;
            double cpu = hasTiming ? _cpuSum / _timingFrames : 0.0;
            bool hasGpu = _gpuFrames > 0;
            double gpu = hasGpu ? _gpuSum / _gpuFrames : 0.0;

            float y = 0f, line = 6f * pixelSize;
            int n = PixelText.WriteLabel(_line, 0, "FPS ");
            n = PixelText.WriteInt(_line, n, (long)(fps + 0.5f));
            Row(n, y, fps < MinFps);

            y -= line;
            n = PixelText.WriteLabel(_line, 0, _ovrCpu ? "CPU " : "FRM ");
            n = hasTiming ? PixelText.WriteTenths(_line, n, cpu) : PixelText.WriteLabel(_line, n, "N/A");
            if (hasTiming) n = PixelText.WriteLabel(_line, n, " MS");
            Row(n, y, hasTiming && cpu > BudgetMs);

            y -= line;
            n = PixelText.WriteLabel(_line, 0, "GPU ");
            n = hasGpu ? PixelText.WriteTenths(_line, n, gpu) : PixelText.WriteLabel(_line, n, "N/A");
            if (hasGpu) n = PixelText.WriteLabel(_line, n, " MS");
            Row(n, y, hasGpu && gpu > BudgetMs);

            y -= line;
            n = PixelText.WriteLabel(_line, 0, "GC ");
            n = PixelText.WriteInt(_line, n, _maxAlloc);
            n = PixelText.WriteLabel(_line, n, " B");
            Row(n, y, _maxAlloc > 0 || _collections > 0);

            _text.Upload(_textM);
            _alert.Upload(_alertM);
        }

        // The runtime reports seconds (about 0.007 for 7 ms); a value above 1 can only already be milliseconds.
        static double ToMs(float v) => v > 1f ? v : v * 1000.0;

        void Row(int length, float y, bool alert)
        {
            PixelText.Draw(alert ? _alert : _text, _line, length, 0f, y, pixelSize);
        }

        void Follow()
        {
            if (_cam == null)
            {
                if (Time.unscaledTime < _camSearchAt) return;
                _camSearchAt = Time.unscaledTime + 1f;
                Camera main = Camera.main;
                if (main == null) return;
                _cam = main.transform;
                _snap = true;
            }
            // Upper-left of the view, clear of the tool panel in front and below.
            Quaternion offset = Quaternion.AngleAxis(-yawLeft, _cam.up) * Quaternion.AngleAxis(-pitchUp, _cam.right);
            Vector3 gaze = offset * _cam.forward;
            Vector3 target = _cam.position + gaze * distance;
            float k = _snap ? 1f : 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime);
            _snap = false;
            Transform t = transform;
            t.position = Vector3.Lerp(t.position, target, k);
            Vector3 away = t.position - _cam.position;
            if (away.sqrMagnitude > 1e-6f) t.rotation = Quaternion.LookRotation(away, Vector3.up);
        }
#endif
    }
}
