using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Interaction;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    // The human: Focus, for working on the real laptop in the virtual room with the tools hidden.
    public class FocusModeTests
    {
        [Test]
        public void EnterAndExit_FlipActive_AndTellListeners_OncePerChange()
        {
            var go = new GameObject("Focus");
            int on = 0, off = 0;
            void Count(bool active) { if (active) on++; else off++; }
            FocusMode.Changed += Count;
            try
            {
                var focus = go.AddComponent<FocusMode>();
                Assert.IsFalse(FocusMode.Active);
                focus.Enter();
                focus.Enter();
                Assert.IsTrue(FocusMode.Active);
                focus.Exit();
                focus.Exit();
                Assert.IsFalse(FocusMode.Active);
                Assert.IsFalse(FocusMode.ChipShown);
                Assert.AreEqual(1, on);
                Assert.AreEqual(1, off);
            }
            finally
            {
                FocusMode.Changed -= Count;
                Object.DestroyImmediate(go);
            }
        }

        // The human: Own Space as the background room under the Meta menu, YouTube or a remote-desktop display.
        [Test]
        public void SystemUiOverTheApp_HidesTheTools_ThenBringsThemBackAsTheyWere_AndNeverShowsTheChip()
        {
            Assert.IsTrue(FocusMode.ToolsHidden(userFocus: false, systemHasInput: true));
            Assert.IsTrue(FocusMode.ToolsHidden(true, false));
            Assert.IsFalse(FocusMode.ToolsHidden(false, false));
            Assert.IsFalse(FocusMode.ChipShows(userFocus: true, systemHasInput: true, aiming: true), "no input: no chip");
            Assert.IsTrue(FocusMode.ChipShows(true, false, true));
            Assert.IsFalse(FocusMode.ChipShows(false, false, true), "outside Focus there is no exit chip");

            var go = new GameObject("Focus");
            int changes = 0; bool last = false;
            void Count(bool hidden) { changes++; last = hidden; }
            FocusMode.Changed += Count;
            try
            {
                var focus = go.AddComponent<FocusMode>();
                focus.SetSystemHasInput(true);
                Assert.IsTrue(FocusMode.Active); Assert.IsTrue(last); Assert.AreEqual(1, changes);
                focus.SetSystemHasInput(false);
                Assert.IsFalse(FocusMode.Active); Assert.IsFalse(last); Assert.AreEqual(2, changes, "back as they were");
                focus.Enter();
                focus.SetSystemHasInput(true);
                focus.SetSystemHasInput(false);
                Assert.IsTrue(FocusMode.Active, "the user's Focus outlives the system UI");
                Assert.AreEqual(3, changes, "no flicker: the tools stayed hidden throughout");
                focus.Exit();
                Assert.IsFalse(FocusMode.Active);
            }
            finally
            {
                FocusMode.Changed -= Count;
                Object.DestroyImmediate(go);
            }
        }

        // Lead, after #347: drive it from Meta's InputFocusLost / InputFocusAcquired too, whichever comes first.
        [Test]
        public void EitherInputReport_HidesOrRestores_AndARepeatChangesNothing()
        {
            Assert.IsTrue(FocusMode.AfterInputEvent(inputLost: true, paused: false), "Meta's InputFocusLost or Unity's focus(false)");
            Assert.IsFalse(FocusMode.AfterInputEvent(false, false), "either one coming back restores");
            Assert.IsTrue(FocusMode.AfterInputEvent(false, true), "paused: still hidden");

            var go = new GameObject("Focus");
            int changes = 0;
            void Count(bool hidden) => changes++;
            FocusMode.Changed += Count;
            try
            {
                var focus = go.AddComponent<FocusMode>();
                focus.SetSystemHasInput(true);
                focus.SetSystemHasInput(true); // the second report of the same loss
                Assert.AreEqual(1, changes);
                focus.SetSystemHasInput(false);
                focus.SetSystemHasInput(false);
                Assert.AreEqual(2, changes);
            }
            finally
            {
                FocusMode.Changed -= Count;
                Object.DestroyImmediate(go);
            }
        }

        // Device bug on fa32819: the chip low beside the desk could not be aimed at (a ray pointing down at the body is
        // hidden). It now appears where the eye button was, pushed off the laptop only when it is over the desk's middle.
        [Test]
        public void TheExitChip_IsWhereTheEyeButtonWas_AndOffTheLaptop()
        {
            // A 1.2 x 0.6 m desk, 0.72 m high, its long side facing a user sitting at z = -0.8.
            var desk = new OrientedBox(new Vec3(0f, 0.36f, 0f), new Vec3(1.2f, 0.72f, 0.6f), 0f);
            var head = new Vector3(0f, 1.2f, -0.8f);
            var eye = new Vector3(0.35f, 0.95f, -0.2f); // the panel's seat: right of the laptop
            Assert.AreEqual(eye, FocusMode.OffLaptop(eye, desk, head), "already clear of the laptop: exactly where the button was");

            var overLaptop = new Vector3(0.05f, 0.95f, -0.2f); // the user had dragged the panel over the middle
            Vector3 moved = FocusMode.OffLaptop(overLaptop, desk, head);
            Assert.AreEqual(FocusMode.LaptopClear, moved.x, 1e-4f, "pushed right, to the clear distance");
            Assert.AreEqual(overLaptop.y, moved.y, 1e-4f, "same height: the hand reaches it as it reached the button");
            Assert.AreEqual(overLaptop.z, moved.z, 1e-4f);
            Assert.AreEqual(-FocusMode.LaptopClear, FocusMode.OffLaptop(new Vector3(-0.05f, 0.95f, -0.2f), desk, head).x, 1e-4f, "left of centre: pushed left");

            Vector3 noSeat = FocusMode.NoSeat(head, new Vector3(0f, -0.3f, 1f));
            Assert.AreEqual(1.2f - FocusMode.NoSeatDrop, noSeat.y, 1e-4f, "without the button's place: about the panel's height");
            Assert.Greater(noSeat.y, head.y - HandRig.RestDrop - 0.01f, "never as low as a resting hand");
        }

        [Test]
        public void TheChip_StaysTwoSecondsAfterTheAimDrops_AndWhileAHoldRuns()
        {
            Assert.IsTrue(FocusMode.ChipWanted(0f, false), "aiming now");
            Assert.IsTrue(FocusMode.ChipWanted(1.9f, false), "the aim gate dropped 1.9 s ago: still there");
            Assert.IsFalse(FocusMode.ChipWanted(FocusMode.Linger, false));
            Assert.IsTrue(FocusMode.ChipWanted(FocusMode.Linger + 5f, true), "a raised pinch is held: it shows what will happen");
        }

        [Test]
        public void APinch_AimedAtTheChip_OrMadeAtIt_Presses_WithoutAimItMustBeClose()
        {
            var chip = new Vector3(0.3f, 0.95f, -0.2f);
            var far = new Vector3(0.2f, 0.8f, -0.6f);
            var aimed = new Ray(far, (chip - far).normalized);
            var up = new Ray(far, Vector3.up); // the device log: pinches landed with the ray pointing up
            Assert.IsTrue(FocusMode.Presses(Pinch(far), true, aimed, chip));
            Assert.IsFalse(FocusMode.Presses(Pinch(far), false, aimed, chip), "a hidden ray never picks");
            Assert.IsFalse(FocusMode.Presses(Pinch(far), true, up, chip));
            Assert.IsTrue(FocusMode.Presses(Pinch(chip + new Vector3(0.05f, 0f, 0f)), false, up, chip), "a pinch at the chip: no aim needed");
            var held = Pinch(chip); held.PinchStarted = false;
            Assert.IsFalse(FocusMode.Presses(held, true, aimed, chip), "only the frame a pinch starts");
        }

        [Test]
        public void ARaisedPinchHeldOneSecond_Exits_TypingNeverDoes()
        {
            var head = new Vector3(0f, 1.2f, 0f);
            float t = 0f;
            HandState raised = Pinch(new Vector3(0.2f, 1.0f, 0.35f));
            t = FocusMode.Hold(t, raised, head, 0.1f);
            raised.PinchStarted = false;
            for (int i = 0; i < 9; i++) t = FocusMode.Hold(t, raised, head, 0.1f);
            Assert.GreaterOrEqual(t, FocusMode.HoldSeconds - 1e-4f, "one second raised: out");

            HandState carried = raised; // pinching since before focus (the eye button's pinch): never counts
            Assert.AreEqual(0f, FocusMode.Hold(0f, carried, head, 0.1f));

            HandState typing = Pinch(new Vector3(0.1f, 0.75f, 0.35f)); // 0.45 m below the eyes, over the keyboard
            float k = 0f;
            for (int i = 0; i < 30; i++) { k = FocusMode.Hold(k, typing, head, 0.1f); typing.PinchStarted = false; }
            Assert.AreEqual(0f, k, "typing hands are low: three seconds of pinching does nothing");

            t = 0.5f;
            HandState lost = raised; lost.Tracked = false;
            Assert.AreEqual(0f, FocusMode.Hold(t, lost, head, 0.1f), "tracking lost: the hold starts over");
            HandState let = raised; let.Pinching = false;
            Assert.AreEqual(0f, FocusMode.Hold(t, let, head, 0.1f), "released early: nothing");
        }

        static HandState Pinch(Vector3 at) => new HandState { Tracked = true, Pinching = true, PinchStarted = true, PinchPoint = at };

        [Test]
        public void AimingAtTheChip_IsANearMissOfAFewCentimetres_InFront()
        {
            var chip = new Vector3(0.5f, 0.8f, 0.4f);
            var from = new Vector3(0.2f, 1.0f, -0.3f);
            Assert.IsTrue(FocusMode.Aims(new Ray(from, (chip - from).normalized), chip));
            Assert.IsTrue(FocusMode.Aims(new Ray(from, (chip + new Vector3(0.05f, 0f, 0f) - from).normalized), chip), "5 cm off: still the chip");
            Assert.IsFalse(FocusMode.Aims(new Ray(from, (chip + new Vector3(0.2f, 0f, 0f) - from).normalized), chip), "20 cm off: not");
            Assert.IsFalse(FocusMode.Aims(new Ray(from, (from - chip).normalized), chip), "behind the hand: not");
        }
    }
}
