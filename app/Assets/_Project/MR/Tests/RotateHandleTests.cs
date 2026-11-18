using NUnit.Framework;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    // Device batch 10: pieces turn by a round handle at their corner, snapping every 15°.
    public class RotateHandleTests
    {
        [Test]
        public void Corner_IsTheFrontRightBottomCorner_SeenFromTheUser()
        {
            // A 2 x 1 m sofa at the origin, the user 3 m in front (-z), looking +z: front = z -0.5, right = +x.
            Vector3 c = RotateHandle.FrontRightCorner(Vector3.zero, new Vector3(2f, 0.8f, 1f), 0f, 0.02f, new Vector3(0f, 1.2f, -3f));
            Assert.AreEqual(new Vector3(1f, 0.02f, -0.5f), c);
            // The user walks round behind it (+z, looking -z): their right is -x, the front is z +0.5.
            c = RotateHandle.FrontRightCorner(Vector3.zero, new Vector3(2f, 0.8f, 1f), 0f, 0f, new Vector3(0f, 1.2f, 3f));
            Assert.AreEqual(new Vector3(-1f, 0f, 0.5f), c);
            // Turned 90°: the 2 m side runs along z.
            c = RotateHandle.FrontRightCorner(Vector3.zero, new Vector3(2f, 0.8f, 1f), 90f, 0f, new Vector3(0f, 1.2f, -3f));
            Assert.AreEqual(-1f, c.z, 1e-4f);
            Assert.AreEqual(0.5f, c.x, 1e-4f);
        }

        [Test]
        public void Near_Within12cmOr4Degrees()
        {
            var head = new Vector3(0f, 1.2f, 0f);
            var corner = new Vector3(0.1f, 0f, 2f);
            Vector3 dir = (new Vector3(0f, 0f, 2f) - head).normalized; // aimed 10 cm beside the corner
            Assert.IsTrue(RotateHandle.Near(new Ray(head, dir), corner, RotateHandle.HitRadius));
            Vector3 off = (new Vector3(0.4f, 0f, 2f) - head).normalized; // 30 cm off, about 7°
            Assert.IsFalse(RotateHandle.Near(new Ray(head, off), corner, RotateHandle.HitRadius));
            Assert.IsTrue(RotateHandle.Near(new Ray(head, off), corner, RotateHandle.ShowRadius), "near enough to show it");
            Assert.IsFalse(RotateHandle.Near(new Ray(head, -dir), corner, 10f), "never behind the ray");
            // Far away a 12 cm miss is under 4°... and 4° still counts.
            var far = new Vector3(0f, 0f, 6f);
            Vector3 fourDeg = Quaternion.Euler(0f, 3.5f, 0f) * (far - head).normalized;
            Assert.IsTrue(RotateHandle.Near(new Ray(head, fourDeg), far, 0.01f));
        }

        [Test]
        public void DragYaw_FollowsTheSweepAroundTheCentre_AndSnapsTo15()
        {
            Vector3 centre = Vector3.zero, from = new Vector3(1f, 0f, 0f);
            Assert.AreEqual(0f, RotateHandle.DragYaw(0f, centre, from, from));
            Vector3 quarter = new Vector3(0f, 0f, -1f); // +x to -z: clockwise from above = +90 in Unity's yaw
            Assert.AreEqual(90f, RotateHandle.DragYaw(0f, centre, from, quarter), 1e-3f);
            Assert.Less(Vector3.Distance(Quaternion.Euler(0f, 90f, 0f) * from, quarter), 1e-4f, "the yaw convention the views use");
            Vector3 small = Quaternion.Euler(0f, 9f, 0f) * from;
            Assert.AreEqual(15f, RotateHandle.DragYaw(0f, centre, from, small), "9° snaps to 15°");
            Vector3 tiny = Quaternion.Euler(0f, 6f, 0f) * from;
            Assert.AreEqual(30f, RotateHandle.DragYaw(30f, centre, from, tiny), "6° stays at the start");
            Assert.AreEqual(45f, RotateHandle.DragYaw(45f, centre, from, centre), "on the centre: no turn");
        }

        // Device shot 20261112-140141: the disc read too big and heavy, and the angle text was larger than other labels.
        [Test]
        public void TheHandle_IsSmallAndQuietAtRest_LitAndLargerWhenHovered_AngleChipIsTheUsualSize()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/UI/ToolPanel.prefab");
            GameObject host = new GameObject("Owner"), panelGo = Object.Instantiate(prefab);
            try
            {
                var panel = panelGo.GetComponent<OwnSpace.UI.ToolPanel>();
                var handle = new RotateHandle(host.transform, panel, null);
                var head = new Vector3(0f, 1.2f, 0f);
                var corner = new Vector3(0.2f, 0.02f, 0.8f);
                float d = Vector3.Distance(head, corner);

                handle.Show(corner, head, false);
                Assert.IsFalse(handle.Lit);
                Assert.AreEqual(RotateHandle.Size * RotateHandle.ViewScale(d), handle.DiscSize, 1e-5f, "about 4.5 cm at rest (at the panel's distance)");
                Assert.LessOrEqual(RotateHandle.Size, 0.05f);
                Transform discRoot = host.transform.Find("RotateHandle/Disc");
                Assert.IsNotNull(discRoot);
                Renderer quad = discRoot.GetComponentInChildren<Renderer>();
                var block = new MaterialPropertyBlock();
                quad.GetPropertyBlock(block);
                Color rest = block.GetColor("_BaseColor");
                Assert.Less(rest.a, 0.5f, "at rest: see-through, not a dark disc");
                Assert.Greater(rest.grayscale, 0.8f, "and light");

                handle.Show(corner, head, true);
                Assert.IsTrue(handle.Lit);
                Assert.AreEqual(RotateHandle.Size * RotateHandle.ViewScale(d) * RotateHandle.LitScale, handle.DiscSize, 1e-5f, "hovered: larger");
                quad.GetPropertyBlock(block);
                Color lit = block.GetColor("_BaseColor");
                Assert.Greater(lit.a, 0.9f, "lit: the solid dark pill");
                Assert.Less(lit.grayscale, 0.5f);

                // The angle chip: a sibling of the handle, so it is the same size as any other in-room chip at that spot.
                handle.ShowAngle(75f, head);
                Transform angle = host.transform.Find("Angle");
                Assert.IsNotNull(angle, "the angle chip sits beside the handle, not inside its scale");
                var other = new GlassChip(host.transform, panel, "Other");
                other.Show("75°", angle.position, head);
                Assert.AreEqual(host.transform.Find("Other").lossyScale.x, angle.lossyScale.x, 1e-5f, "the same scale as any chip there");
                TMPro.TextMeshPro text = angle.GetComponentInChildren<TMPro.TextMeshPro>(), otherText = host.transform.Find("Other").GetComponentInChildren<TMPro.TextMeshPro>();
                Assert.AreEqual(otherText.fontSize, text.fontSize, 1e-5f, "the same text size");
                Assert.AreEqual("75°", text.text);
                handle.Hide();
                Assert.IsFalse(angle.gameObject.activeSelf);
            }
            finally { Object.DestroyImmediate(host); Object.DestroyImmediate(panelGo); }
        }
    }
}
