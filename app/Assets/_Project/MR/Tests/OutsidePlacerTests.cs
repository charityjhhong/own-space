using System.Collections.Generic;
using NUnit.Framework;
using OwnSpace.Contracts;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    public class OutsidePlacerTests
    {
        static CatalogItem OutsideItem() => new CatalogItem { id = "sofa-1", kind = "sofa", zones = new List<string> { "outside" } };

        // Device batch 7: "place … on piece True" with a rug in the way. An armed card places on the outside floor even
        // over a piece; a piece is dragged only when the floor there is not a place for the card.
        [Test]
        public void ArmedPinch_PlacesEvenOverAPiece_ElseDragsIt()
        {
            Assert.AreEqual(OutsidePlacer.PinchAction.Place, OutsidePlacer.Decide(true, true, true));
            Assert.AreEqual(OutsidePlacer.PinchAction.Drag, OutsidePlacer.Decide(true, true, false));
            Assert.AreEqual(OutsidePlacer.PinchAction.Drag, OutsidePlacer.Decide(true, false, true), "not armed: a piece drags");
            Assert.AreEqual(OutsidePlacer.PinchAction.Place, OutsidePlacer.Decide(false, true, true));
            Assert.AreEqual(OutsidePlacer.PinchAction.OpenRow, OutsidePlacer.Decide(false, false, true));
            Assert.AreEqual(OutsidePlacer.PinchAction.None, OutsidePlacer.Decide(false, true, false));
        }

        [Test]
        public void Disarms_OnLosingBothHands_OrTheScanOrZone()
        {
            Assert.IsTrue(OutsidePlacer.ShouldDisarm(true, true, false, false), "both hands lost");
            Assert.IsFalse(OutsidePlacer.ShouldDisarm(true, true, true, false), "one hand is enough");
            Assert.IsTrue(OutsidePlacer.ShouldDisarm(true, false, true, true), "scan or zone lost");
            Assert.IsFalse(OutsidePlacer.ShouldDisarm(false, false, false, false));
        }

        [Test]
        public void Pause_And_Disarm_ClearTheArmedCard_AndRaiseArmedChanged()
        {
            var go = new GameObject("Placer");
            var placer = go.AddComponent<OutsidePlacer>();
            int changes = 0;
            placer.ArmedChanged += () => changes++;
            placer.Arm(OutsideItem());
            Assert.IsNotNull(placer.Armed);
            placer.Pause(true);
            Assert.IsNull(placer.Armed);
            placer.Arm(OutsideItem());
            placer.Disarm();
            Assert.IsNull(placer.Armed);
            Assert.AreEqual(4, changes);
            placer.Arm(new CatalogItem { id = "desk", kind = "desk", zones = new List<string> { "my" } });
            Assert.IsNull(placer.Armed, "only outside pieces can be armed");
            Object.DestroyImmediate(go);
        }

        // Device batch 7: placed pieces show what can be done with them, nothing unlabelled.
        [Test]
        public void PieceLook_HoverSelectDragRefuse()
        {
            Assert.AreEqual(OutsidePlacer.PieceLook.Plain, OutsidePlacer.LookOf(false, false, false, false));
            Assert.AreEqual(OutsidePlacer.PieceLook.Hover, OutsidePlacer.LookOf(true, false, false, false));
            Assert.AreEqual(OutsidePlacer.PieceLook.Selected, OutsidePlacer.LookOf(true, true, false, false), "selected wins over hover");
            Assert.AreEqual(OutsidePlacer.PieceLook.Selected, OutsidePlacer.LookOf(false, false, true, false), "dragging");
            Assert.AreEqual(OutsidePlacer.PieceLook.Refused, OutsidePlacer.LookOf(true, true, true, true), "a drag that breaks a rule is red");
            Assert.AreEqual(OutsidePlacer.PieceLook.Selected, OutsidePlacer.LookOf(false, true, false, true), "refused only while dragging");
        }

        // Device batch 9: the dragged piece jumped. It follows smoothly, speed-limited, and settles on the target.
        [Test]
        public void Drag_FollowsSmoothly_WithoutJumps()
        {
            Vector3 pos = Vector3.zero, v = Vector3.zero, target = new Vector3(2f, 0f, 0f);
            pos = OutsidePlacer.SmoothFollow(pos, target, ref v, 1f / 72f);
            Assert.Less(pos.x, OutsidePlacer.DragMaxSpeed / 72f + 1e-4f, "one frame moves at most the speed limit");
            for (int i = 0; i < 72 * 3; i++) pos = OutsidePlacer.SmoothFollow(pos, target, ref v, 1f / 72f);
            Assert.AreEqual(2f, pos.x, 0.01f, "settles on the hand's point");
            Assert.LessOrEqual(pos.x, 2f + 1e-3f, "no overshoot");
        }
    }
}
