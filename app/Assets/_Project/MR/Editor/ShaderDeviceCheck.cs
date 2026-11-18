using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace OwnSpace.MR.Editor
{
    /// <summary>
    /// Compiles the project's own shaders for the headset (Android: Vulkan and GLES3) and reports every error. The Editor
    /// on a Mac compiles them for Metal only, where <c>half</c> is <c>float</c> and more is accepted: OwnSpace/RealOcclusion
    /// compiled there and failed on device, so with the pass on the room was drawn with the magenta error shader (device,
    /// build dd3521d). Used by an EditMode test and before every Android build (the build fails).
    /// Run by hand: <c>-executeMethod OwnSpace.MR.Editor.ShaderDeviceCheck.Run</c>.
    /// </summary>
    public sealed class ShaderDeviceCheck : IPreprocessBuildWithReport
    {
        /// <summary>Every shader of ours that draws on the headset.</summary>
        public static readonly string[] Shaders =
        {
            "OwnSpace/RealOcclusion", "OwnSpace/PassthroughHole", "OwnSpace/SeamFade", "OwnSpace/Finish", "OwnSpace/Furniture",
        };

        static readonly ShaderCompilerPlatform[] Platforms = { ShaderCompilerPlatform.Vulkan, ShaderCompilerPlatform.GLES3x };
        // What the player uses on Quest: single-pass multiview with or without instancing, and the plain variant.
        static readonly string[][] KeywordSets =
        {
            new string[0], new[] { "STEREO_MULTIVIEW_ON" }, new[] { "STEREO_MULTIVIEW_ON", "INSTANCING_ON" },
            new[] { "STEREO_MULTIVIEW_ON", "_ADDITIONAL_LIGHTS" }, new[] { "STEREO_MULTIVIEW_ON", "_ADDITIONAL_LIGHTS_VERTEX" },
        };

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android) return;
            List<string> errors = Check();
            if (errors.Count > 0) throw new BuildFailedException("Shaders that do not compile for the headset:\n" + string.Join("\n", errors));
        }

        public static void Run()
        {
            List<string> errors = Check();
            if (errors.Count > 0) throw new System.InvalidOperationException("Shaders that do not compile for the headset:\n" + string.Join("\n", errors));
            Debug.Log($"[OwnSpace.MR.Editor] Shader device check: {Shaders.Length} shaders compile for Vulkan and GLES3.");
        }

        /// <summary>One line per failing shader, pass, stage, platform and keyword set; empty when all compile.</summary>
        public static List<string> Check()
        {
            var errors = new List<string>();
            foreach (string name in Shaders)
            {
                Shader shader = Shader.Find(name);
                if (shader == null) { errors.Add($"{name}: shader not found"); continue; }
                ShaderData data = ShaderUtil.GetShaderData(shader);
                for (int s = 0; s < data.SubshaderCount; s++)
                {
                    ShaderData.Subshader sub = data.GetSubshader(s);
                    for (int p = 0; p < sub.PassCount; p++)
                    {
                        ShaderData.Pass pass = sub.GetPass(p);
                        foreach (ShaderCompilerPlatform platform in Platforms)
                        foreach (string[] keywords in KeywordSets)
                        foreach (ShaderType stage in new[] { ShaderType.Vertex, ShaderType.Fragment })
                        {
                            ShaderData.VariantCompileInfo info = pass.CompileVariant(stage, keywords, platform, BuildTarget.Android);
                            if (info.Success) continue;
                            var text = new StringBuilder($"{name}, pass '{pass.Name}', {stage}, {platform}, [{string.Join(" ", keywords)}]:");
                            foreach (ShaderMessage m in info.Messages)
                                if (m.severity == ShaderCompilerMessageSeverity.Error) text.Append($" {m.message} (line {m.line});");
                            string line = text.ToString();
                            if (!errors.Contains(line)) errors.Add(line);
                        }
                    }
                }
            }
            return errors;
        }
    }
}
