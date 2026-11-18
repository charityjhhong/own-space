using NUnit.Framework;
using OwnSpace.Interaction;
using OwnSpace.UI;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    // Device (build 754b485): three recenters at launch; the panel re-seated in the event against the old head and stayed low.
    public class ReseatTests
    {
        [Test]
        public void ABurstOfRecenters_SettlesOnce_AfterTheLast()
        {
            int left = 0, fired = 0;
            left = HandRig.SettleFrames;                 // recenter 1
            if (HandRig.StepSettle(ref left)) fired++;   // frame 1
            left = HandRig.SettleFrames;                 // recenter 2 restarts it
            if (HandRig.StepSettle(ref left)) fired++;
            if (HandRig.StepSettle(ref left)) fired++;   // 2 frames after the last: settled
            if (HandRig.StepSettle(ref left)) fired++;
            Assert.AreEqual(1, fired);
        }

        [Test]
        public void ThePanel_ReseatsUnlessPlacedAndStillInView()
        {
            PanelMetrics m = PanelMetrics.Default;
            var head = new Vector3(0f, 1.2f, 0f);
            var inView = new Vector3(0f, 1f, 0.6f);
            var sunk = new Vector3(0f, -0.2f, 0.4f); // the old origin: far below the view
            Assert.IsTrue(ToolPanel.ReseatsAfterOriginChange(false, head, Vector3.forward, inView, m), "not placed: always back on its seat");
            Assert.IsFalse(ToolPanel.ReseatsAfterOriginChange(true, head, Vector3.forward, inView, m), "placed and in view: stays");
            Assert.IsTrue(ToolPanel.ReseatsAfterOriginChange(true, head, Vector3.forward, sunk, m), "placed but out of view after the move: back");
            Assert.IsTrue(PanelHost.FloorMoved(float.NaN, 0.46f), "the first floor reading");
            Assert.IsTrue(PanelHost.FloorMoved(0.01f, 0.46f));
            Assert.IsFalse(PanelHost.FloorMoved(0.46f, 0.47f));
        }
    }
}
