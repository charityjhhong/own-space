using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.XR;

namespace OwnSpace.MR
{
    /// <summary>
    /// Development builds, once, <see cref="AfterSeconds"/> s after start: what holds memory. The headset reported
    /// "GPU=1750 MiB" for the app 15 s after launch (build dd3521d) while every texture in the project adds up to about
    /// 50 MiB, so the question is which render targets, meshes or system buffers it is. One-time; it allocates (lists,
    /// strings), which is why it never runs per frame or in a release build.
    /// </summary>
    public static class MemoryReport
    {
        public const float AfterSeconds = 20f;
        const string LogTag = "[OwnSpace.MR]";

        /// <summary>The total and the <paramref name="top"/> largest entries of one kind, as one line (pure).</summary>
        public static string Summarise(string kind, List<KeyValuePair<string, long>> items, int top)
        {
            long total = 0;
            for (int i = 0; i < items.Count; i++) total += items[i].Value;
            items.Sort((a, b) => b.Value.CompareTo(a.Value));
            var text = new StringBuilder($"{kind}: {items.Count}, {Mb(total)} MiB");
            for (int i = 0; i < items.Count && i < top; i++) text.Append(i == 0 ? "; largest: " : ", ").Append($"{items[i].Key} {Mb(items[i].Value)}");
            return text.ToString();
        }

        public static string Mb(long bytes) => (bytes / 1048576f).ToString("F1");

        /// <summary>
        /// What the eye textures are really allocated at: Unity's eye-texture scale, the URP render scale, the display's
        /// render-target scale and the first render pass's target size and MSAA (XRSettings.eyeTextureWidth reads 0 with
        /// OpenXR). Meant to read 1.2 everywhere (EyeResolutionSetup).
        /// </summary>
        public static string EyeTargets()
        {
            var text = new StringBuilder($"Eye targets: eye-texture scale {XRSettings.eyeTextureResolutionScale:F2}");
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)
                text.Append($", URP render scale {urp.renderScale:F2}");
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            for (int i = 0; i < displays.Count; i++)
            {
                XRDisplaySubsystem display = displays[i];
                if (!display.running) continue;
                text.Append($", display scale {display.scaleOfAllRenderTargets:F2}");
                if (display.GetRenderPassCount() > 0)
                {
                    display.GetRenderPass(0, out XRDisplaySubsystem.XRRenderPass pass);
                    RenderTextureDescriptor d = pass.renderTargetDesc;
                    text.Append($", target {d.width} x {d.height} x {d.volumeDepth}, MSAA {d.msaaSamples}, {d.graphicsFormat}");
                }
            }
            return text.Append('.').ToString();
        }

        public static void Log()
        {
            Debug.Log($"{LogTag} {EyeTargets()}");
            Debug.Log($"{LogTag} Memory: graphics driver {Mb(Profiler.GetAllocatedMemoryForGraphicsDriver())} MiB, Unity allocated " +
                      $"{Mb(Profiler.GetTotalAllocatedMemoryLong())} / reserved {Mb(Profiler.GetTotalReservedMemoryLong())} MiB, managed {Mb(Profiler.GetMonoUsedSizeLong())} MiB; " +
                      $"eye buffer {XRSettings.eyeTextureWidth} x {XRSettings.eyeTextureHeight}, scale {XRSettings.eyeTextureResolutionScale:F2}, " +
                      $"viewport {XRSettings.renderViewportScale:F2}, MSAA {QualitySettings.antiAliasing}.");
            Debug.Log($"{LogTag} Memory, {Kind<RenderTexture>("render textures", t => $"{t.name} {t.width}x{t.height}x{t.volumeDepth} aa{t.antiAliasing} {t.graphicsFormat}")}.");
            Debug.Log($"{LogTag} Memory, {Kind<Texture2D>("textures", t => $"{t.name} {t.width}x{t.height} {t.format}")}.");
            Debug.Log($"{LogTag} Memory, {Kind<Texture2DArray>("texture arrays", t => $"{t.name} {t.width}x{t.height}x{t.depth}")}.");
            Debug.Log($"{LogTag} Memory, {Kind<Cubemap>("cubemaps", t => $"{t.name} {t.width}")}.");
            Debug.Log($"{LogTag} Memory, {Kind<Mesh>("meshes", m => $"{m.name} {m.vertexCount}v{(m.isReadable ? " readable" : "")}")}.");
        }

        static string Kind<T>(string kind, System.Func<T, string> name) where T : Object
        {
            var items = new List<KeyValuePair<string, long>>();
            foreach (T o in Resources.FindObjectsOfTypeAll<T>())
                if (o != null) items.Add(new KeyValuePair<string, long>(name(o), Profiler.GetRuntimeMemorySizeLong(o)));
            return Summarise(kind, items, 8);
        }
    }
}
