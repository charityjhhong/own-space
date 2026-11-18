using System.Collections.Generic;
using NUnit.Framework;
using OwnSpace.Contracts;

namespace OwnSpace.MR.Tests
{
    // Virtual extent part B: in an extended room the real walls are nearer than the virtual ones; leaving my zone shows them.
    public class PresenceGuardTests
    {
        static readonly List<Vec2> Zone = new List<Vec2> { new Vec2(0f, 1.6f), new Vec2(2.6f, 1.6f), new Vec2(2.6f, 3f), new Vec2(0f, 3f) };

        [Test]
        public void LeavingMyZone_ShowsTheRealRoom_WithAGapAgainstFlicker()
        {
            Assert.AreEqual(0f, OutsideShell.OutsideBy(Zone, new Vec2(1.3f, 2.3f)), "inside");
            Assert.AreEqual(0.4f, OutsideShell.OutsideBy(Zone, new Vec2(1.3f, 1.2f)), 1e-4f, "40 cm out in front");
            Assert.IsFalse(OutsideShell.StepGuard(false, 0.2f, true), "20 cm out: still the virtual room (leaning out of the chair)");
            Assert.IsTrue(OutsideShell.StepGuard(false, 0.35f, true), "35 cm out: the real room");
            Assert.IsTrue(OutsideShell.StepGuard(true, 0.2f, true), "coming back: not yet at 20 cm (no flicker)");
            Assert.IsFalse(OutsideShell.StepGuard(true, 0.1f, true), "within 15 cm: the virtual room returns");
        }

        [Test]
        public void ARoomThatIsNotExtended_NeverGuards()
        {
            Assert.IsFalse(OutsideShell.StepGuard(false, 2f, false), "the virtual walls are the real walls there");
            Assert.IsFalse(OutsideShell.StepGuard(true, 2f, false));
        }
    }
}
