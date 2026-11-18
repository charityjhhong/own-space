using NUnit.Framework;
using OwnSpace.Interaction;

namespace OwnSpace.MR.Tests
{
    public class HandStateTests
    {
        const float On = 0.85f, Off = 0.6f;

        [Test]
        public void PinchStartsAndEndsWithHysteresis()
        {
            var s = new HandState();
            HandRig.Step(ref s, true, 0.9f, On, Off);
            Assert.IsTrue(s.PinchStarted && s.Pinching);
            HandRig.Step(ref s, true, 0.7f, On, Off);
            Assert.IsTrue(s.Pinching && !s.PinchStarted);
            HandRig.Step(ref s, true, 0.5f, On, Off);
            Assert.IsTrue(s.PinchEnded && !s.Pinching);
        }

        [Test]
        public void LossDuringPinch_StaysCancelledWhenRetrackedNextFrame()
        {
            var s = new HandState();
            HandRig.Step(ref s, true, 0.9f, On, Off);
            HandRig.Step(ref s, false, 0f, On, Off);
            Assert.IsTrue(s.TrackingLost);

            // Back on the very next frame, still pinching: no pinch, no new start, still latched.
            HandRig.Step(ref s, true, 0.95f, On, Off);
            Assert.IsTrue(s.TrackingLost);
            Assert.IsFalse(s.Pinching || s.PinchStarted);

            // Fingers open: latch clears; a fresh pinch starts normally.
            HandRig.Step(ref s, true, 0.3f, On, Off);
            Assert.IsFalse(s.TrackingLost);
            HandRig.Step(ref s, true, 0.9f, On, Off);
            Assert.IsTrue(s.PinchStarted);
        }
    
        [Test]
        public void TouchStrength_FullWhenTipsTouch_ZeroWhenApart()
        {
            Assert.AreEqual(1f, HandRig.TouchStrength(0.01f, 0.015f), 1e-5f);
            Assert.AreEqual(0.5f, HandRig.TouchStrength(0.03f, 0.015f), 1e-5f);
            Assert.AreEqual(0f, HandRig.TouchStrength(0.06f, 0.015f), 1e-5f);
            Assert.AreEqual(0f, HandRig.TouchStrength(float.MaxValue, 0.015f), 1e-5f);
        }

        [Test]
        public void AimSource_PinchingHandWins_RightOnATie()
        {
            Assert.AreEqual(HandRig.AimFrom.Left, HandRig.AimSource(false, true, true, true));
            Assert.AreEqual(HandRig.AimFrom.Right, HandRig.AimSource(true, true, true, true));
            Assert.AreEqual(HandRig.AimFrom.Right, HandRig.AimSource(false, false, true, true));
        }

        [Test]
        public void AimSource_FallsBackToTheOtherHandThenTheHead()
        {
            Assert.AreEqual(HandRig.AimFrom.Left, HandRig.AimSource(true, false, false, true));
            Assert.AreEqual(HandRig.AimFrom.Head, HandRig.AimSource(true, true, false, false));
        }

        static HandState Started() => new HandState { Tracked = true, Pinching = true, PinchStarted = true };
        static HandState Held() => new HandState { Tracked = true, Pinching = true };
        static HandState Open() => new HandState { Tracked = true };

        [Test]
        public void StepSimClick_BothHandsOnOneFrameStartsIt_UntilBothRelease()
        {
            bool active = false;
            Assert.IsTrue(HandRig.StepSimClick(ref active, Started(), Started()));
            Assert.IsTrue(active);
            Assert.IsFalse(HandRig.StepSimClick(ref active, Held(), Open()));
            Assert.IsTrue(active, "one hand still pinching keeps it");
            HandRig.StepSimClick(ref active, Open(), Open());
            Assert.IsFalse(active);
        }

        [Test]
        public void StepSimClick_OneHandPinchIsNotAClick()
        {
            bool active = false;
            Assert.IsFalse(HandRig.StepSimClick(ref active, Started(), Open()));
            Assert.IsFalse(active);
        }
    }
}
