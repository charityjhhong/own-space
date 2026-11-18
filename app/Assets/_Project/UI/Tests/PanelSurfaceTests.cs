using NUnit.Framework;
using UnityEngine;

namespace OwnSpace.UI.Tests
{
    // Device day (6c6b59a): seated at a desk, the panel's lower third sank into the desk.
    public class PanelSurfaceTests
    {
        // A desk 0.75 m high under x in [-0.6, 0.6], z in [0.2, 0.9].
        sealed class Desk : IPanelSurfaces
        {
            public float Top = 0.75f;
            public bool TopUnder(Vector3 p, out float top)
            {
                top = Top;
                return p.x >= -0.6f && p.x <= 0.6f && p.z >= 0.2f && p.z <= 0.9f;
            }
        }

        // A desk under x in [0.12, 0.6] only.
        sealed class EdgeDesk : IPanelSurfaces
        {
            public bool TopUnder(Vector3 p, out float top) { top = 0.75f; return p.x >= 0.12f && p.x <= 0.6f && p.z >= 0.2f && p.z <= 0.9f; }
        }

        static readonly Vector3 Head = new Vector3(0f, 1.15f, 0f); // seated

        static float BottomY(Vector3 pos, Quaternion rot, float bottom) => (pos + rot * new Vector3(0f, bottom, 0f)).y;

        // Device batch 10: the AI keyboard docked 0.24 m under the panel sank into the desk.
        [Test]
        public void ADockedKeyboard_CountsInTheClamp()
        {
            Assert.AreEqual(-0.2f, ToolPanel.ClampBottom(0.23f, 0.43f, 0f), 1e-5f);
            Assert.AreEqual(0.15f, ToolPanel.ClampHalfWidth(0.30f, 0f, 0.40f), 1e-5f, "closed: the panel's width");
            Assert.AreEqual(0.20f, ToolPanel.ClampHalfWidth(0.30f, 0.25f, 0.40f), 1e-5f, "open: the 0.40 m keyboard's");
            // A desk under only the keyboard's outer corner (x 0.17-0.6) still lifts it.
            var corner = new Vector3(-0.05f, 0.9f, 0.5f);
            Quaternion straight = Quaternion.identity;
            Assert.AreEqual(corner, ToolPanel.ClampAbove(corner, straight, -0.44f, 0.23f, 0.15f, Head, new EdgeDesk()), "the panel's width misses it");
            Assert.AreNotEqual(corner, ToolPanel.ClampAbove(corner, straight, -0.44f, 0.23f, 0.20f, Head, new EdgeDesk()), "the keyboard's width finds it");
            float bottom = ToolPanel.ClampBottom(0.23f, 0.43f, 0.24f);
            Assert.AreEqual(-0.44f, bottom, 1e-5f);
            var pos = new Vector3(0f, 1.05f, 0.5f); // the panel alone clears the desk, its keyboard would not
            Quaternion rot = ToolPanel.FaceEyes(Head, pos, Quaternion.identity);
            Assert.AreEqual(pos, ToolPanel.ClampAbove(pos, rot, -0.2f, 0.23f, 0.15f, Head, new Desk()), "no keyboard: stays");
            Vector3 clamped = ToolPanel.ClampAbove(pos, rot, bottom, 0.23f, 0.15f, Head, new Desk());
            Assert.AreNotEqual(pos, clamped, "with the keyboard: it moves");
            // Rising all the way would put the top above the eyes, so it comes toward the user: the keyboard's bottom
            // edge ends either above the desk or no longer over it.
            Vector3 low = clamped + rot * new Vector3(0f, bottom, 0f);
            bool overDesk = new Desk().TopUnder(low, out _);
            Assert.IsTrue(!overDesk || low.y >= 0.75f + ToolPanel.SurfaceClearance - 1e-4f, $"keyboard bottom {low:F2} sinks into the desk");
        }

        [Test]
        public void APanelReachingIntoTheDesk_RisesAboveItsTop()
        {
            var pos = new Vector3(0f, 0.85f, 0.5f);
            Quaternion rot = ToolPanel.FaceEyes(Head, pos, Quaternion.identity);
            Vector3 clamped = ToolPanel.ClampAbove(pos, rot, -0.2f, 0.23f, 0.15f, Head, new Desk());
            Assert.GreaterOrEqual(BottomY(clamped, rot, -0.2f), 0.75f + ToolPanel.SurfaceClearance - 1e-4f, "bottom edge 5 cm above the desk");
        }

        [Test]
        public void APanelAlreadyAbove_OrNotOverTheDesk_StaysPut()
        {
            var above = new Vector3(0f, 1.1f, 0.5f);
            Quaternion rot = ToolPanel.FaceEyes(Head, above, Quaternion.identity);
            Assert.AreEqual(above, ToolPanel.ClampAbove(above, rot, -0.2f, 0.23f, 0.15f, Head, new Desk()));
            var aside = new Vector3(1.5f, 0.6f, 0.5f);
            Assert.AreEqual(aside, ToolPanel.ClampAbove(aside, rot, -0.2f, 0.23f, 0.15f, Head, new Desk()), "nothing under it");
        }

        [Test]
        public void TooHighToRise_ComesTowardTheUserInstead_NeverBelowTheSurface()
        {
            var desk = new Desk { Top = 1.0f }; // a standing desk under a low panel
            var pos = new Vector3(0f, 0.9f, 0.6f);
            Quaternion rot = ToolPanel.FaceEyes(Head, pos, Quaternion.identity);
            Vector3 clamped = ToolPanel.ClampAbove(pos, rot, -0.2f, 0.23f, 0.15f, Head, desk);
            Assert.Less(clamped.z, pos.z, "pulled toward the user");
            bool clear = !desk.TopUnder(clamped + rot * new Vector3(0f, -0.2f, 0f), out _)
                         || BottomY(clamped, rot, -0.2f) >= desk.Top + ToolPanel.SurfaceClearance - 1e-4f;
            Assert.IsTrue(clear, "off the desk or above it");
        }

        [Test]
        public void ThePanelDrawsOverTheRoom()
        {
            Shader glass = Shader.Find("OwnSpace/PanelGlass");
            Assert.GreaterOrEqual(glass.renderQueue, 3100, "after the world's transparents");
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/UI/ToolPanel.prefab");
            var go = Object.Instantiate(prefab); // the real panel, with content's Inter fonts
            try
            {
                var panel = go.GetComponent<ToolPanel>();
                panel.Configure(new[] { "Style" }, new IPanelPage[0]);
                Material text = panel.Element(0).Text.fontSharedMaterial;
                Assert.IsTrue(text.HasProperty("unity_GUIZTestMode"), text.shader.name);
                Assert.AreEqual((float)UnityEngine.Rendering.CompareFunction.Always, text.GetFloat("unity_GUIZTestMode"), "text ignores room depth");
                Assert.AreEqual(PanelFonts.Queue, text.renderQueue);
                Assert.AreNotSame(panel.Element(0).Text.font.material, text, "a copy: content's font asset is untouched");
            }
            finally { Object.DestroyImmediate(go); }
        }
    
        sealed class Tall : IPanelPage
        {
            readonly int _rows;
            public Tall(int rows) => _rows = rows;
            public int Count => _rows;
            public int Columns => 1;
            public void Describe(int i, ref CardInfo c) { c.Kind = CardKind.Chip; c.Row = i; c.Title = "Row"; }
            public bool Press(int index) => true;
        }

        // Device bug on f1d4fe0: "every time I change tabs, the panel's position changes" (a pinned panel near the desk).
        [Test]
        public void SwitchingTabs_NeverMovesAPinnedPanel_EvenOverADesk()
        {
            var go = new GameObject("Panel");
            var handsGo = new GameObject("Hands");
            handsGo.SetActive(false); // no Awake: the test owns it
            try
            {
                var hands = handsGo.AddComponent<OwnSpace.Interaction.HandRig>();
                var panel = go.AddComponent<ToolPanel>();
                typeof(ToolPanel).GetField("_hands", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(panel, hands);
                panel.Configure(new[] { "Short", "Tall", "Mid" }, new IPanelPage[] { new Tall(1), new Tall(9), new Tall(4) });
                panel.Surfaces = new Desk { Top = 0.95f }; // a desk the tall tab would reach into
                go.transform.SetPositionAndRotation(new Vector3(0f, 1.1f, 0.5f), Quaternion.identity);
                panel.Placed = true;
                Vector3 topLeft = go.transform.TransformPoint(new Vector3(-panel.Metrics.Size.x * 0.5f, panel.Metrics.Size.y * 0.5f, 0f));
                Quaternion turn = go.transform.rotation;
                for (int round = 0; round < 2; round++)
                for (int tab = 0; tab < 3; tab++)
                {
                    panel.SelectTab(tab);
                    Vector3 now = go.transform.TransformPoint(new Vector3(-panel.Metrics.Size.x * 0.5f, panel.Metrics.Size.y * 0.5f, 0f));
                    Assert.Less(Vector3.Distance(topLeft, now), 1e-5f, $"tab {tab}: the top-left stays put");
                    Assert.AreEqual(turn, go.transform.rotation);
                }
                // The keyboard opening still keeps it out of the desk (an explicit clamp, logged).
                panel.ExtraBelow = 0.25f;
                Assert.AreNotEqual(topLeft, go.transform.TransformPoint(new Vector3(-panel.Metrics.Size.x * 0.5f, panel.Metrics.Size.y * 0.5f, 0f)));
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(handsGo); }
        }
    }
}
