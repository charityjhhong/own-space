using NUnit.Framework;
using OwnSpace.Diagnostics.Editor;

namespace OwnSpace.Diagnostics.Tests
{
    public class PerfSmokeTests
    {
        [Test] public void WithinBudgetHasNoVerdict() =>
            Assert.AreEqual("", PerfSmoke.Verdict(new SceneStats { DrawCalls = PerfSmoke.MaxDrawCalls, Triangles = PerfSmoke.MaxTriangles, Materials = PerfSmoke.MaxMaterials, TextureBytes = PerfSmoke.MaxTextureBytes }));

        [Test]
        public void EachBreachIsNamed()
        {
            Assert.AreEqual("draw calls", PerfSmoke.Verdict(new SceneStats { DrawCalls = PerfSmoke.MaxDrawCalls + 1 }));
            Assert.AreEqual("triangles", PerfSmoke.Verdict(new SceneStats { Triangles = PerfSmoke.MaxTriangles + 1 }));
            Assert.AreEqual("materials", PerfSmoke.Verdict(new SceneStats { Materials = PerfSmoke.MaxMaterials + 1 }));
            Assert.AreEqual("texture memory", PerfSmoke.Verdict(new SceneStats { TextureBytes = PerfSmoke.MaxTextureBytes + 1 }));
        }
    }
}
