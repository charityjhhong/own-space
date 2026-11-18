using NUnit.Framework;
using OwnSpace.Interaction;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    // Device, 2694d7e: the ray sat well below the hand, aligned only sometimes; a recenter did not help. One ray, drawn and
    // picked alike, now starts on the hand (the skeleton's pinch point) and points along Meta's pointer pose.
    public class HandAimTests
    {
        static readonly Vector3 Head = new Vector3(0f, 1.2f, 0f), Pinch = new Vector3(0.2f, 1.0f, 0.4f);

        [Test]
        public void TheRay_StartsAtThePinchPoint_AndPointsAlongThePointerPose()
        {
            var pointer = new Ray(Pinch + new Vector3(0f, -0.03f, -0.05f), new Vector3(0.1f, 0.1f, 1f).normalized);
            Ray aim = HandRig.HandAim(pointer, Pinch, HandRig.Shoulder(Head, Vector3.forward, false), out bool trusted);
            Assert.IsTrue(trusted);
            Assert.Less(Vector3.Distance(aim.origin, Pinch), 0.02f, "on the hand: under 2 cm from the pinch point");
            Assert.AreEqual(0f, Vector3.Angle(aim.direction, pointer.direction), 1e-3f);
        }

        [Test]
        public void APointerPoseFarFromTheHand_GivesNoDirection_TheShoulderThroughTheHandDoes()
        {
            var below = new Ray(Pinch + Vector3.down * 0.4f, Vector3.down); // the device offset: well below the hand
            Vector3 shoulder = HandRig.Shoulder(Head, Vector3.forward, false);
            Ray aim = HandRig.HandAim(below, Pinch, shoulder, out bool trusted);
            Assert.IsFalse(trusted);
            Assert.AreEqual(Pinch, aim.origin, "still on the hand");
            Assert.AreEqual(0f, Vector3.Angle(aim.direction, Pinch - shoulder), 1e-3f, "from the shoulder through the hand");
            Assert.Greater(aim.direction.z, 0.5f, "forward, where the hand points");
        }

        [Test]
        public void TheShoulder_IsBelowTheEyes_OnTheArmsSide()
        {
            Vector3 right = HandRig.Shoulder(Head, new Vector3(0f, -0.5f, 1f), false), left = HandRig.Shoulder(Head, Vector3.forward, true);
            Assert.AreEqual(Head.y - HandRig.ShoulderDrop, right.y, 1e-5f);
            Assert.AreEqual(HandRig.ShoulderSide, right.x, 1e-5f);
            Assert.AreEqual(-HandRig.ShoulderSide, left.x, 1e-5f);
        }

        // The human, after #353: "the pointer is too sensitive; every time I pinch, it shifts off".
        [Test]
        public void TheFilter_HoldsStillThroughJitter_ButFollowsAFastSweep()
        {
            var steady = new AimSteady();
            float t = 0f, dt = 1f / 72f;
            Ray r = default;
            for (int i = 0; i < 72; i++, t += dt)
            {
                float jitter = (i % 2 == 0 ? 1f : -1f) * 0.6f * Mathf.Deg2Rad; // +-0.6 degrees, frame to frame
                r = steady.Step(new Ray(Pinch, new Vector3(Mathf.Sin(jitter), 0f, Mathf.Cos(jitter))), t, dt);
            }
            Assert.Less(Vector3.Angle(r.direction, Vector3.forward), 0.25f, "a still hand: the jitter is mostly gone");
            Assert.AreEqual(Pinch, r.origin, "the origin is never filtered");

            var sweep = new AimSteady();
            for (int i = 0; i <= 18; i++, t += dt) // 90 degrees in a quarter second
            {
                float a = i * 5f * Mathf.Deg2Rad;
                r = sweep.Step(new Ray(Pinch, new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a))), t, dt);
            }
            for (int i = 0; i < 6; i++, t += dt) r = sweep.Step(new Ray(Pinch, Vector3.right), t, dt);
            Assert.Less(Vector3.Angle(r.direction, Vector3.right), 5f, "a fast sweep: caught up within a few frames of stopping");
        }

        [Test]
        public void APinch_UsesTheAimFrom100msBefore()
        {
            var steady = new AimSteady();
            float dt = 1f / 72f, t = 0f;
            for (int i = 0; i < 40; i++, t += dt) steady.Step(new Ray(Pinch, Vector3.forward), t, dt);
            // The fingers close: over the last 80 ms the hand dips the aim 6 degrees down.
            Ray now = default;
            for (int i = 1; i <= 6; i++, t += dt)
            {
                float a = i * 1f * Mathf.Deg2Rad;
                now = steady.Step(new Ray(Pinch, new Vector3(0f, -Mathf.Sin(a), Mathf.Cos(a))), t, dt);
            }
            Ray latched = steady.Latched(t - dt, now);
            Assert.Less(Vector3.Angle(latched.direction, Vector3.forward), 0.5f, "the press lands where the hand pointed before");
            Assert.Greater(Vector3.Angle(now.direction, Vector3.forward), 1f, "while the live aim had moved");
            var empty = new AimSteady();
            Assert.AreEqual(now.direction, empty.Latched(1f, now).direction, "no history: the live aim");
        }

        [Test]
        public void OneEuroAlpha_IsSmallWhenStill_LargerWithAHigherCutoff()
        {
            float slow = AimSteady.Alpha(AimSteady.MinCutoff, 1f / 72f), fast = AimSteady.Alpha(20f, 1f / 72f);
            Assert.Less(slow, 0.15f);
            Assert.Greater(fast, 0.5f);
            Assert.Less(fast, 1f);
        }
    }
}
