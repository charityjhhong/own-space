using System.Collections.Generic;
using NUnit.Framework;
using OwnSpace.Contracts;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    // Device batch 7: 14 of 18 pinches in the zone step "hit nothing". Corners, edges and the whole zone are easy targets.
    public class ZonePickTests
    {
        static readonly List<Vec2> Zone = new List<Vec2> { new Vec2(0f, 1f), new Vec2(2f, 1f), new Vec2(2f, 3f), new Vec2(0f, 3f) };
        static readonly Vector3 Eye = new Vector3(1f, 1.2f, 0f);
        const float Knob = 0.75f;

        static ZoneGrab Aim(Vector3 floorPoint, out int index, out float miss) =>
            ZonePick.Pick(new Ray(Eye, floorPoint - Eye), new Vector3(9f, 9f, 9f), true, Zone, 0f, Knob, out index, out miss);

        [Test]
        public void TheRayNearACornerOnTheFloor_GrabsIt()
        {
            Assert.AreEqual(ZoneGrab.Corner, Aim(new Vector3(2.15f, 0f, 2.85f), out int i, out _), "15 cm off the corner");
            Assert.AreEqual(2, i);
        }

        [Test]
        public void TheRayNearTheBall_OrItsPost_GrabsIt()
        {
            var ball = new Vector3(0f, Knob, 3f);
            Vector3 off = Quaternion.Euler(0f, 5f, 0f) * (ball - Eye);
            Assert.AreEqual(ZoneGrab.Corner, ZonePick.Pick(new Ray(Eye, off), Vector3.zero, false, Zone, 0f, Knob, out int i, out _), "5 degrees off the ball");
            Assert.AreEqual(3, i);
            Assert.AreEqual(ZoneGrab.Corner, ZonePick.Pick(new Ray(Eye, Vector3.forward), new Vector3(2.05f, 0.4f, 1f), true, Zone, 0f, Knob, out i, out _), "a hand at the post");
            Assert.AreEqual(1, i);
        }

        [Test]
        public void AnEdge_OrInside_OrNothing()
        {
            Assert.AreEqual(ZoneGrab.Edge, Aim(new Vector3(1f, 0f, 3.1f), out int e, out _), "10 cm past the far edge");
            Assert.AreEqual(2, e, "edge 2 runs from corner 2 to corner 3");
            Assert.AreEqual(ZoneGrab.Whole, Aim(new Vector3(1f, 0f, 2f), out _, out _), "inside: drag it all");
            Assert.AreEqual(ZoneGrab.None, Aim(new Vector3(4f, 0f, 6f), out _, out float miss));
            Assert.Greater(miss, 0.25f, "the miss says how far the nearest corner was");
            Assert.Less(miss, 10f);
        }

        [Test]
        public void DoneGuard_AsksOnlyWhenTheDeskIsOutside()
        {
            var desk = new FurnitureAnchor("desk", FurnitureRole.Table, new OrientedBox(new Vec3(1f, 0.4f, 2f), new Vec3(1f, 0.75f, 0.5f), 0f));
            Assert.IsFalse(ZoneSetup.DeskOutside(Zone, new[] { desk }, new Vec2(1f, 0f)));
            var away = new FurnitureAnchor("desk", FurnitureRole.Table, new OrientedBox(new Vec3(3f, 0.4f, 2f), new Vec3(1f, 0.75f, 0.5f), 0f));
            Assert.IsTrue(ZoneSetup.DeskOutside(Zone, new[] { away }, new Vec2(1f, 0f)));
            Assert.IsFalse(ZoneSetup.DeskOutside(Zone, new FurnitureAnchor[0], new Vec2(1f, 0f)), "no desk: nothing to ask");
        }

        // Device batch 7: black slivers where the floor and ceiling did not reach the walls.
        [Test]
        public void TheFloorReachesPastTheWalls_EitherWinding()
        {
            var square = new List<Vec2> { new Vec2(0f, 0f), new Vec2(4f, 0f), new Vec2(4f, 3f), new Vec2(0f, 3f) };
            var result = new List<Vec2>();
            foreach (bool reversed in new[] { false, true })
            {
                var outline = new List<Vec2>(square);
                if (reversed) outline.Reverse();
                ShellGeometry.Expand(outline, 0.2f, result);
                Assert.Greater(System.Math.Abs(RoomGeometry.SignedArea(result)), 4.4f * 3.4f - 0.01f);
                foreach (Vec2 p in result) Assert.IsTrue(p.x < -0.19f || p.x > 4.19f, "every corner pushed out");
            }
            ShellGeometry.Bounds(square, 0.2f, result);
            Assert.AreEqual(4, result.Count);
            Assert.AreEqual(-0.2f, result[0].x, 1e-5f);
        }

        // Device batch 7 addendum: a corner 2-3 m away was a tiny ball and hard to hit.
        [Test]
        public void FarCorners_AreAsEasyAsNearOnes()
        {
            Assert.AreEqual(0.09f, ZonePick.KnobSize(1f), 1e-5f, "near: the minimum");
            Assert.AreEqual(3f * 0.052f, ZonePick.KnobSize(3f), 1e-5f, "far: about 3 degrees of view");
            var far = new List<Vec2> { new Vec2(0f, 2f), new Vec2(3f, 2f), new Vec2(3f, 4f), new Vec2(0f, 4f) };
            var eye = new Vector3(1.5f, 1.2f, -1f);
            Vector3 floorNearCorner = new Vector3(3.3f, 0f, 4.2f); // ~0.36 m off a corner ~5 m away
            Assert.AreEqual(ZoneGrab.Corner, ZonePick.Pick(new Ray(eye, floorNearCorner - eye), Vector3.zero, false, far, 0f, 0.75f, out int i, out _));
            Assert.AreEqual(2, i);
        }

        // Device (build 0ddb26d, seated: floor y -0.32, head y 0.82): "ray misses the floor" three times. A flat ray still
        // grabs the side it passes close to.
        [Test]
        public void AFlatRay_ThatMissesTheFloor_StillGrabsTheSideItPassesNear()
        {
            var eye = new Vector3(1f, 1.14f, 0f); // 1.14 m above the floor at y 0
            var nearEdge = new Vector3(1f, 0.06f, 3f); // 6 cm above the far side's middle (5 m off the floor point)
            var up = new Ray(eye, (nearEdge - eye).normalized + Vector3.up * 0.0f);
            Assert.AreEqual(ZoneGrab.Edge, ZonePick.Pick(up, Vector3.zero, false, Zone, 0f, Knob, out int e, out _));
            Assert.AreEqual(2, e, "the far side");
            var level = new Ray(eye, Vector3.forward); // level: never reaches the floor
            Assert.AreEqual(ZoneGrab.None, ZonePick.Pick(level, Vector3.zero, false, Zone, 0f, Knob, out _, out float miss));
            Assert.AreEqual(1.14f, miss, 0.01f, "the miss log's closest approach: 1.14 m (the ray passes over the far side at eye height)");
            Assert.AreEqual(ZoneGrab.Edge, ZonePick.RayEdge(new Ray(new Vector3(1f, 0.5f, 0f), (new Vector3(1f, 0.02f, 1f) - new Vector3(1f, 0.5f, 0f)).normalized), Zone, 0f, out e));
            Assert.AreEqual(0, e, "the near side, 2 cm above it");
        }

        // Device: "pinch on Corner 0" and nothing moved: a flat ray's drag returned the knob itself.
        [Test]
        public void AFlatRayDrag_StillMoves_AtTheGrabbedReach()
        {
            var eye = new Vector3(0f, 1.14f, 0f);
            var flat = new Ray(eye, new Vector3(0f, -0.05f, 1f).normalized);
            Assert.IsFalse(ZonePick.FloorDrag(flat, 0f), "about 3 degrees down: too flat to follow the floor point");
            float reach = ZonePick.Reach(flat, new Vector3(0f, 0f, 2.5f));
            Assert.IsTrue(ZonePick.DragPoint(flat, 0f, false, reach, out Vector3 a));
            var turned = new Ray(eye, Quaternion.Euler(0f, 20f, 0f) * flat.direction);
            Assert.IsTrue(ZonePick.DragPoint(turned, 0f, false, reach, out Vector3 b));
            Assert.AreEqual(2.5f, a.z, 1e-4f);
            Assert.Greater(b.x, 0.8f, "turning the hand 20 degrees swings the point about 0.85 m");
            Assert.AreEqual(2.5f, new Vector2(b.x, b.z).magnitude, 1e-4f, "at the same reach");
            var down = new Ray(eye, new Vector3(0f, -1f, 1f).normalized);
            Assert.IsTrue(ZonePick.FloorDrag(down, 0f), "45 degrees down: the floor point");
            Assert.IsTrue(ZonePick.DragPoint(down, 0f, true, 0f, out Vector3 c));
            Assert.AreEqual(1.14f, c.z, 1e-3f);
        }

        // Device shot 182125: the card sat over a corner post.
        [Test]
        public void TheCard_MovesOffAZoneCorner()
        {
            var head = new Vector3(0f, 0.82f, 0f);
            var zone = new List<Vec2> { new Vec2(-0.5f, 0.5f), new Vec2(0.5f, 0.5f), new Vec2(0.5f, 1.5f), new Vec2(-0.5f, 1.5f) };
            Vector3 overBall = new Vector3(0.5f, 0.43f, 0.5f) * 0.9f + head * 0.1f; // straight in front of corner 1's ball
            Vector3 at = ZoneSetup.ClearOfZone(head, overBall, Vector3.right, zone, -0.32f, 0.75f);
            Assert.AreNotEqual(overBall, at);
            for (int i = 0; i < zone.Count; i++)
                Assert.GreaterOrEqual(Vector3.Angle(at - head, new Vector3(zone[i].x, 0.43f, zone[i].y) - head), ZoneSetup.CardClearDegrees - 1e-3f);
            var clear = new Vector3(0f, 1.6f, 0.5f);
            Assert.AreEqual(clear, ZoneSetup.ClearOfZone(head, clear, Vector3.right, zone, -0.32f, 0.75f), "already clear: stays");
        }

        // Device: Done turned into Fit / Keep and the next pinches in the same spot hit nothing. The human: Fit to desk
        // on the left, Keep anyway on the right; Keep anyway now takes Done's spot.
        [Test]
        public void TheDeskQuestion_FitOnTheLeft_KeepInDonesSpot()
        {
            var go = new GameObject("Panel");
            try
            {
                var panel = go.AddComponent<OwnSpace.UI.ToolPanel>();
                panel.Configure(new[] { "A" }, new OwnSpace.UI.IPanelPage[] { null });
                var card = new ZoneCard(go.transform, panel);
                card.Show("a", "b", "Done", null, Vector3.forward, Vector3.zero);
                Vector2 done = card.PrimaryCentre;
                card.Show(ZoneSetup.OutsideLine("desk"), "b", "Fit to desk", "Keep anyway", Vector3.forward, Vector3.zero, mainLeft: true);
                Assert.AreEqual(1, card.Hit(new Ray(Vector3.zero, Vector3.forward), new Vector3(done.x, done.y, 1f), true), "a pinch where Done was lands on Keep anyway");
                Assert.Less(card.PrimaryCentre.x, card.SecondaryCentre.x, "Fit to desk on the left");
                card.Show("a", "b", "Done", "Back", Vector3.forward, Vector3.zero);
                Assert.Less(card.SecondaryCentre.x, card.PrimaryCentre.x, "elsewhere the main action stays on the right");
                Assert.AreEqual("Your desk is outside your area", ZoneSetup.OutsideLine("desk"));
            }
            finally { Object.DestroyImmediate(go); }
        }

        // Review of #268: with no scan, a miss pinch replaced "No room scan" for good; it must stay (and come back).
        [Test]
        public void NoScan_TheScanLineSurvivesAMissAndAHover()
        {
            Assert.AreEqual(ZoneSetup.NoScanLine, ZoneSetup.MissLines(false, out string second));
            Assert.AreEqual(ZoneSetup.MissLine, second, "the miss note goes on the second line");
            Assert.AreEqual(ZoneSetup.NoScanLine, ZoneSetup.HoverLines(false, ZoneSetup.HoverEdge, out second), "a hover brings the card back");
            Assert.AreEqual(ZoneSetup.ZoneMeaning, second);
            Assert.AreEqual(ZoneSetup.MissLine, ZoneSetup.MissLines(true, out _));
            Assert.AreEqual(ZoneSetup.HoverEdge, ZoneSetup.HoverLines(true, ZoneSetup.HoverEdge, out _));
            Assert.AreEqual(ZoneSetup.ZoneLine, ZoneSetup.HoverLines(true, null, out _));
        }

        // Device (build dfb22f4): fingers on Cancel, the ray past the card, every pinch "hit nothing". A hand at the card
        // presses what is under its pinch point, whatever the ray does.
        [Test]
        public void AHandAtTheCard_PressesTheButtonUnderIt_WhateverTheRay()
        {
            var go = new GameObject("Panel");
            try
            {
                var panel = go.AddComponent<OwnSpace.UI.ToolPanel>();
                panel.Configure(new[] { "A" }, new OwnSpace.UI.IPanelPage[] { null });
                var card = new ZoneCard(go.transform, panel);
                var at = new Vector3(0f, 1f, 0.65f);
                card.Show("a", "b", "Done", "Cancel", at, new Vector3(0f, 1f, 0f)); // faces the eyes: +z away
                var away = new Ray(new Vector3(0f, 1f, 0.3f), new Vector3(-1f, 1f, 0f).normalized); // the ray up and left, past it
                Vector2 done = card.PrimaryCentre, cancel = card.SecondaryCentre;
                foreach (float z in new[] { -0.05f, -0.02f, 0f, 0.04f })
                {
                    Assert.AreEqual(0, card.Hit(away, at + new Vector3(done.x, done.y, z), true), $"Done, pinch point {z * 100f:0} cm off the card");
                    Assert.AreEqual(1, card.Hit(away, at + new Vector3(cancel.x, cancel.y, z), true), $"Cancel, pinch point {z * 100f:0} cm off the card");
                }
                Assert.AreEqual(-1, card.Hit(away, at + new Vector3(done.x, done.y, -0.2f), true), "20 cm in front: not near; the ray decides");
                Assert.IsTrue(card.NearCard(at + new Vector3(0f, 0f, -0.1f), ZoneSetup.CardClaimFront, out _), "10 cm in front: still the card's pinch, never the floor's");
                Assert.IsFalse(card.NearCard(at + new Vector3(0.4f, 0f, 0f), ZoneSetup.CardClaimFront, out _), "beside it: not the card's");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TheCard_SeatsOutOfArmsReach()
        {
            var head = new Vector3(0f, 1.2f, 0f);
            Vector3 near = ZoneSetup.OutOfReach(head, new Vector3(0f, 0.9f, 0.35f));
            Assert.AreEqual(ZoneSetup.CardMinDistance, Vector3.Distance(head, near), 1e-4f);
            var far = new Vector3(0f, 0.9f, 0.7f);
            Assert.AreEqual(far, ZoneSetup.OutOfReach(head, far));
        }

        // Mockups round 2 (zone-card.html): 16 mm padding, a 14 mm title line and a 12 mm caption line, 12 mm above the
        // buttons, buttons of 36 x (words + 24, at least 96) mm with the main one right-most.
        [Test]
        public void TheCard_FollowsTheMockupsNumbers_AndEveryLineAndButtonFits()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/UI/ToolPanel.prefab");
            GameObject go = new GameObject("Host"), panelGo = Object.Instantiate(prefab, go.transform);
            try
            {
                var card = new ZoneCard(go.transform, panelGo.GetComponent<OwnSpace.UI.ToolPanel>());
                Transform root = go.transform.Find("ZoneCard");
                float inner = ZoneCard.Width - 2f * ZoneCard.Pad;
                Assert.AreEqual(0.016f, ZoneCard.Pad, 1e-6f);
                (string, string, string, string)[] states =
                {
                    (ZoneSetup.ZoneLine, ZoneSetup.ZoneMeaning, "Done", null),
                    (ZoneSetup.DeskLine, ZoneSetup.DeskQuestion, "Fit to desk", "Keep anyway"),
                    (ZoneSetup.NoScanLine, ZoneSetup.ZoneMeaning, "Done", ZoneSetup.SampleLabel),
                    (ZoneSetup.ZoneLine, ZoneSetup.ZoneMeaning, "Done", ZoneSetup.CancelLabel),
                    (ZoneSetup.HoverCorner, ZoneSetup.ZoneMeaning, "Done", null),
                };
                foreach ((string line1, string line2, string primary, string secondary) in states)
                {
                    card.Show(line1, line2, primary, secondary, Vector3.forward, Vector3.zero);
                    float top = float.MinValue, bottom = float.MaxValue;
                    foreach (TMPro.TextMeshPro t in root.GetComponentsInChildren<TMPro.TextMeshPro>())
                    {
                        if (string.IsNullOrEmpty(t.text) || !t.gameObject.activeInHierarchy) continue;
                        t.ForceMeshUpdate();
                        Assert.IsFalse(t.isTextTruncated, t.text);
                        float size = t.text == line1 ? OwnSpace.UI.PanelElement.TitleMm : t.text == line2 ? OwnSpace.UI.PanelElement.CaptionMm : OwnSpace.UI.PanelElement.BodyMm;
                        Assert.GreaterOrEqual(t.fontSize, OwnSpace.UI.PanelElement.FontSize(OwnSpace.UI.PanelElement.FloorTextMm) - 1e-5f, t.text + ": never below 12 mm");
                        if (t.text == line2 || t.text == primary || t.text == secondary) Assert.AreEqual(OwnSpace.UI.PanelElement.FontSize(size), t.fontSize, 1e-5f, t.text + ": at its full size");
                        if (t.text == line1) top = t.transform.parent.localPosition.y;
                        if (t.text == primary) bottom = t.transform.parent.localPosition.y;
                    }
                    Assert.AreEqual(inner * 0.5f, card.PrimaryCentre.x + card.PrimarySize.x * 0.5f, 1e-5f, primary + ": the main button is the right-most, on the content edge");
                    Assert.GreaterOrEqual(card.PrimarySize.x, ZoneCard.MinButton - 1e-5f, primary + ": at least 96 mm");
                    Assert.AreEqual(ZoneCard.ButtonHeight, card.PrimarySize.y, 1e-6f);
                    if (secondary != null)
                    {
                        Assert.AreEqual(card.PrimaryCentre.x - card.PrimarySize.x * 0.5f - ZoneCard.ButtonGap, card.SecondaryCentre.x + card.SecondarySize.x * 0.5f, 1e-5f, secondary + ": 8 mm left of the main button");
                        Assert.GreaterOrEqual(card.SecondaryCentre.x - card.SecondarySize.x * 0.5f, -inner * 0.5f - 1e-5f, secondary + ": inside the card's padding");
                        Assert.GreaterOrEqual(card.SecondarySize.x, ZoneCard.MinButton - 1e-5f);
                    }
                    // Lines to buttons: 12 mm between the caption line's box and the buttons.
                    Assert.AreEqual(ZoneCard.Line1 * 0.5f + ZoneCard.Line2 + ZoneCard.Gap + ZoneCard.ButtonHeight * 0.5f, top - bottom, 1e-5f, "line 1, line 2, 12 mm, then the buttons");
                }
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
