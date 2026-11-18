using NUnit.Framework;
using OwnSpace.Interaction;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    // The human (build 754b485): "the ray shows up weird again": hands typing on a laptop shot two long rays.
    public class RayGateTests
    {
        static readonly Vector3 Head = new Vector3(0f, 1.2f, 0f);

        [Test]
        public void HandsTypingOnALaptop_DoNotAim()
        {
            var hand = new Vector3(0.15f, 0.72f, 0.35f); // 48 cm below the eyes, over the desk
            Vector3 palmDown = HandRig.PalmNormal(new Vector3(0.15f, 0.72f, 0.3f), new Vector3(0.13f, 0.72f, 0.39f), new Vector3(0.18f, 0.72f, 0.38f), left: false);
            Assert.Less(palmDown.y, -0.9f, "right hand, palm flat on the keys: the normal points down");
            Assert.IsFalse(HandRig.AimPose(new Vector3(-0.6f, 0.5f, 0.6f), hand, Head, palmDown, true, out string why), "up and left from a resting hand");
            Assert.AreEqual("hand at rest, palm down", why);
            Assert.IsFalse(HandRig.AimPose(new Vector3(0f, -1f, 0.1f), hand, Head, palmDown, true, out why));
            Assert.AreEqual("pointing straight down", why);
            Vector3 leftPalmDown = HandRig.PalmNormal(new Vector3(-0.15f, 0.72f, 0.3f), new Vector3(-0.13f, 0.72f, 0.39f), new Vector3(-0.18f, 0.72f, 0.38f), left: true);
            Assert.Less(leftPalmDown.y, -0.9f, "left hand, palm down: the same");
        }

        [Test]
        public void ARaisedHand_PointingOut_Aims_EvenDownAtTheFloor()
        {
            var hand = new Vector3(0.2f, 0.95f, 0.3f); // chest height
            Vector3 palmOut = new Vector3(0f, -0.3f, 1f).normalized;
            Assert.IsTrue(HandRig.AimPose(new Vector3(0f, 0.1f, 1f), hand, Head, palmOut, true, out _), "at the panel");
            Assert.IsTrue(HandRig.AimPose(new Vector3(0.1f, -0.6f, 0.8f), hand, Head, new Vector3(0f, -0.7f, 0.7f), true, out _), "down at a zone corner on the floor");
            Assert.IsFalse(HandRig.AimPose(new Vector3(-0.2f, 0.3f, -1f), hand, Head, palmOut, true, out string why), "back at the user");
            Assert.AreEqual("pointing at the body", why);
            Assert.IsTrue(HandRig.AimPose(Vector3.forward, hand, Head, palmOut, false, out why), "a dim room: a raised hand still aims");
            var low = new Vector3(0.15f, 0.72f, 0.35f);
            Assert.IsFalse(HandRig.AimPose(Vector3.forward, low, Head, palmOut, false, out why), "low confidence and low: resting");
            Assert.AreEqual("low confidence, hand low", why);
        }

        [Test]
        public void TheRay_ShowsAfterAShortHold_AndHidesAtOnce()
        {
            float t = 0f;
            t = HandRig.StepAim(t, true, 0.1f);
            Assert.Less(t, HandRig.AimHold, "0.1 s: not yet (no flicker)");
            t = HandRig.StepAim(t, true, 0.1f);
            Assert.GreaterOrEqual(t, HandRig.AimHold, "0.2 s: shown");
            Assert.AreEqual(0f, HandRig.StepAim(t, false, 0.016f), "back at rest: hidden at once");
        }

        // Log 2dc475d: aiming at the floor from the chair (low hand, palm down, ray steeply down) was gated as resting.
        [Test]
        public void AFloorAction_LetsALowHandAimDownAtTheFloor()
        {
            var hand = new Vector3(0.15f, 0.72f, 0.35f);
            var down = new Vector3(0.1f, -0.95f, 0.3f);
            Vector3 palmDown = new Vector3(0f, -1f, 0.1f);
            Assert.IsFalse(HandRig.AimPose(down, hand, Head, palmDown, true, out _), "nothing armed: resting");
            Assert.IsTrue(HandRig.AimPose(down, hand, Head, palmDown, true, out string why, floorAction: true), "placing, drawing a box, zone edit: it aims");
            Assert.AreEqual("aiming (floor action)", why);
            Assert.IsFalse(HandRig.AimPose(new Vector3(0f, 0.3f, -1f), hand, Head, palmDown, true, out _, floorAction: true), "still never at the body");
        }

        [Test]
        public void Resting_IsLowAndPalmDown_JustAboveADeskOrBed()
        {
            var hand = new Vector3(0.15f, 0.72f, 0.35f);
            var outUp = new Vector3(0f, 0.2f, 1f);
            Vector3 palmDown = new Vector3(0f, -1f, 0.1f);
            Assert.IsFalse(HandRig.AimPose(outUp, hand, Head, palmDown, true, out _, overRestSurface: true), "over the desk: resting");
            Assert.IsTrue(HandRig.AimPose(outUp, hand, Head, palmDown, true, out _, overRestSurface: false), "low and palm down but over no desk: aims");
            Assert.IsTrue(PanelHost.OnRestSurface(0.8f, 0.72f), "8 cm over the desk top");
            Assert.IsFalse(PanelHost.OnRestSurface(1.05f, 0.72f), "33 cm over it: raised");
        }
    }
}
