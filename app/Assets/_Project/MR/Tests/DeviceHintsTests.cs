using NUnit.Framework;
using OwnSpace.Interaction;

namespace OwnSpace.MR.Tests
{
    // Device day #17/#18: the one-line hints.
    public class DeviceHintsTests
    {
        [Test]
        public void ControllerNote_OnlyWhenControllersAreTheInputAndNoHandIsTracked()
        {
            Assert.IsTrue(HandRig.ControllersOnly(true, false));
            Assert.IsFalse(HandRig.ControllersOnly(true, true), "hands are tracked: they are in use");
            Assert.IsFalse(HandRig.ControllersOnly(false, false), "neither: nothing to say (hands may be off view)");
            StringAssert.Contains("hands", GazeReticle.ControllerNote);
        }

        [Test]
        public void PlaceHint_SaysWhereToPinch()
        {
            StringAssert.Contains("Pinch the floor", PanelHost.PlaceHint);
            StringAssert.Contains("outside", PanelHost.PlaceHint);
        }
    }
}
