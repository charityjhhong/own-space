using NUnit.Framework;

namespace OwnSpace.MR.Tests
{
    // Device, e3f28ec: launched by adb with the headset on the desk (head 0.75 m up); the panel seated there.
    public class PanelWakeTests
    {
        [Test]
        public void OnlyAWornTrackedHead_SeatsThePanel()
        {
            Assert.IsFalse(PanelHost.HeadValid(tracked: true, worn: false, aboveFloor: 0.75f, hasFloor: true), "a headset on the desk");
            Assert.IsFalse(PanelHost.HeadValid(false, true, 1.2f, true), "untracked");
            Assert.IsFalse(PanelHost.HeadValid(true, true, 0.3f, true), "worn but on the floor: not a head pose to seat from");
            Assert.IsTrue(PanelHost.HeadValid(true, true, 1.15f, true), "seated at the desk");
            Assert.IsTrue(PanelHost.HeadValid(true, true, 0.7f, true), "reclined on a bed");
            Assert.IsTrue(PanelHost.HeadValid(true, true, 0f, false), "no floor yet: worn and tracked is enough");
        }

        // Device, bece6e8: launched with the headset on the desk; the zone confirmed while system UI had the input and the
        // head was invalid, and the panel never opened, not on the input coming back nor after three settled recenters.
        [Test]
        public void InvalidHeadAtStart_ThenValid_ThePanelOpens()
        {
            bool desk = PanelHost.HeadValid(tracked: true, worn: false, aboveFloor: 0.75f, hasFloor: true);
            bool worn = PanelHost.HeadValid(true, true, 1.2f, true);
            Assert.IsTrue(PanelHost.OpenWaits(systemHasInput: true, headReady: desk), "at launch: waits");
            Assert.IsTrue(PanelHost.OpenWaits(false, desk), "the input back, still on the desk: waits");
            Assert.IsTrue(PanelHost.OpenWaits(true, worn), "worn but system UI on top: waits");
            Assert.IsFalse(PanelHost.OpenWaits(false, worn), "worn, with the input: opens");
        }
    }
}
