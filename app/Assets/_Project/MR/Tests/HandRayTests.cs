using NUnit.Framework;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    // The human: "the ray has to go back to how it was at the very first" (before a6cdf5b / #259).
    // DO NOT CHANGE these numbers without the human asking: they are the ray's look.
    public class HandRayTests
    {
        static readonly Ray Pointer = new Ray(new Vector3(0.2f, 1f, 0.3f), new Vector3(-0.3f, -0.1f, 1f).normalized);

        [Test]
        public void TheRay_IsTheOriginalShortLine_OnThePointerRay()
        {
            GazeReticle.Line(Pointer, 1.5f, out Vector3 start, out Vector3 end);
            Assert.AreEqual(0f, Vector3.Distance(Pointer.GetPoint(0.05f), start), 1e-5f, "starts 5 cm along the pointer ray");
            Assert.AreEqual(0f, Vector3.Distance(Pointer.GetPoint(1f), end), 1e-5f, "a target 1.5 m away: 1 m long at most");
            GazeReticle.Line(Pointer, 0.6f, out _, out end);
            Assert.AreEqual(0f, Vector3.Distance(Pointer.GetPoint(0.48f), end), 1e-5f, "0.6 m away: stops at 80%, short of the target");
            GazeReticle.Line(Pointer, 0.02f, out _, out end);
            Assert.AreEqual(0f, Vector3.Distance(Pointer.GetPoint(0.06f), end), 1e-5f, "never shorter than 6 cm");
        }

        [Test]
        public void TheRay_Is4mmWide_TaperingToNothing()
        {
            Assert.AreEqual(0.004f, GazeReticle.LineWidth, 1e-6f);
            AnimationCurve taper = GazeReticle.LineTaper();
            Assert.AreEqual(1f, taper.Evaluate(0f), 1e-5f, "full width at the hand");
            Assert.AreEqual(0f, taper.Evaluate(1f), 1e-5f, "nothing at its end, toward the dot");
            Assert.AreEqual(0.5f, taper.Evaluate(0.5f), 0.1f);
        }

        [Test]
        public void TheRay_HidesWhileTheHandPokesThePanel()
        {
            Assert.IsTrue(GazeReticle.RayShown(true, true, false));
            Assert.IsFalse(GazeReticle.RayShown(true, true, true));
            Assert.IsFalse(GazeReticle.RayShown(false, true, false));
        }
    }
}
