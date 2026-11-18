using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;
using OwnSpace.Contracts;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    // Realism pass G: every placed piece sits on a soft contact shadow; one shared material.
    public class ContactShadowsTests
    {
        GameObject _root;
        bool? _xrWasOn;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            if (_xrWasOn.HasValue) { PanelSimClickPlayTests.SetXrStartOnPlay(_xrWasOn.Value); _xrWasOn = null; }
        }

        [Test]
        public void TheMesh_IsTheFootprintPlusAFixed20cmRing_CachedPerSize()
        {
            Mesh m = ContactShadows.MeshFor(new Vector3(1.2f, 0.75f, 0.6f));
            Assert.AreEqual(16, m.vertexCount);
            Assert.AreEqual(54, m.triangles.Length, "nine quads");
            Assert.AreEqual(0.6f + ContactShadows.Falloff, m.bounds.extents.x, 1e-5f, "the footprint's half width plus 20 cm");
            Assert.AreEqual(0.3f + ContactShadows.Falloff, m.bounds.extents.z, 1e-5f);
            Mesh big = ContactShadows.MeshFor(new Vector3(2f, 0.5f, 0.9f));
            Assert.AreEqual(1f + ContactShadows.Falloff, big.bounds.extents.x, 1e-5f, "a 2 m bed: still 20 cm, not 70");
            Assert.AreSame(m, ContactShadows.MeshFor(new Vector3(1.2f, 0.4f, 0.6f)), "one mesh per footprint size");
            int inner = 0;
            foreach (Color c in m.colors) if (c.a > 0.5f) inner++;
            Assert.AreEqual(4, inner, "full only at the footprint's four corners (and between them)");
            Assert.AreEqual(new Vector2(0.6f, 0.3f), m.uv2[0], "the half size, for the leg pads");
            Assert.Greater(ContactShadows.Half(Vector3.zero).x, 0f, "never a zero-size mesh");
        }

        static CatalogItem Item(string kind, params string[] tags) =>
            new CatalogItem { id = kind, kind = kind, tags = new List<string>(tags) };

        [Test]
        public void OnlyPiecesStandingOnTheFloor_GetAShadow()
        {
            Assert.IsTrue(ContactShadows.Grounded(Item("sofa")));
            Assert.IsTrue(ContactShadows.Grounded(Item("lamp", "light")), "a floor lamp");
            Assert.IsFalse(ContactShadows.Grounded(Item("wall-art")));
            Assert.IsFalse(ContactShadows.Grounded(Item("curtain")));
            Assert.IsFalse(ContactShadows.Grounded(Item("pendant-lamp")));
            Assert.IsFalse(ContactShadows.Grounded(Item("rug")), "flat on the floor already");
            Assert.IsFalse(ContactShadows.Grounded(Item("plant", "tabletop")), "on a desk: no floor shadow at its pivot");
            Assert.IsFalse(ContactShadows.Grounded(null));
        }

        // Play mode: the placeholder pieces are built with Destroy (runtime only).
        [UnityTest]
        public IEnumerator PlacedPiece_GetsOneSharedInstancedShadow_ThatRefusalNeverTints()
        {
            _xrWasOn = PanelSimClickPlayTests.SetXrStartOnPlay(false);
            yield return new EnterPlayMode();
            _root = new GameObject("Root");
            var item = new CatalogItem { id = "sofa", name = "Sofa", kind = "sofa", size = new Vec3(1.6f, 0.8f, 0.9f), zones = new List<string> { "outside" } };
            var view = new PlacementView(_root.transform, ContactShadows.Material, null);
            var state = new DesignState();
            state.placements.Add(new FreePlacement { placementId = "p1", catalogItemId = "sofa", position = new Vec3(1f, 0f, 2f) });
            state.placements.Add(new FreePlacement { placementId = "p2", catalogItemId = "sofa", position = new Vec3(2f, 0f, 2f) });
            view.Refresh(state, new Catalog(new[] { item }));

            var shadows = new List<Renderer>();
            foreach (Transform piece in _root.transform)
            {
                Transform s = piece.Find(ContactShadows.ChildName);
                Assert.IsNotNull(s, "a shadow under " + piece.name);
                Assert.AreEqual(ContactShadows.Lift, s.localPosition.y, 1e-5f, "just above the floor and my zone's passthrough hole");
                shadows.Add(s.GetComponent<Renderer>());
            }
            Assert.AreEqual(2, shadows.Count);
            Assert.AreSame(shadows[0].sharedMaterial, shadows[1].sharedMaterial, "one shared material");
            Assert.IsTrue(shadows[0].sharedMaterial.enableInstancing);
            Assert.AreEqual("OwnSpace/ContactShadow", shadows[0].sharedMaterial.shader.name);

            view.SetRefused("p1", true);
            Assert.IsFalse(shadows[0].HasPropertyBlock(), "the red refusal tint is for the piece, not its shadow");
            yield return new ExitPlayMode();
        }

        // Realism pass: the shadow is a rounded rectangle, dark under the whole base (corners and legs too), fading out.
        [Test]
        public void TheShadow_IsDarkUnderTheWholeFootprint_AndFadesToNothing()
        {
            // Device, build e3f28ec: "furniture has no shadows". A 2 x 0.9 m bed: dark right up to its edge, gone 20 cm past it.
            var half = new Vector2(1f, 0.45f);
            float s = ContactShadows.DefaultStrength;
            Assert.That(s, Is.InRange(0.45f, 0.55f), "the brief's core darkness");
            Assert.AreEqual(s, ContactShadows.Alpha(Vector2.zero, half, s), 1e-5f, "the centre");
            Assert.AreEqual(s, ContactShadows.Alpha(new Vector2(1f, 0f), half, s), 1e-5f, "right at the edge of the footprint: full");
            Assert.Greater(ContactShadows.Alpha(new Vector2(1.05f, 0f), half, s), s * 0.8f, "5 cm out: still clearly dark");
            Assert.Less(ContactShadows.Alpha(new Vector2(1.15f, 0f), half, s), s * 0.25f, "15 cm out: nearly gone");
            Assert.AreEqual(0f, ContactShadows.Alpha(new Vector2(1f + ContactShadows.Falloff, 0f), half, s), 1e-5f, "20 cm out: nothing");
            Assert.Greater(ContactShadows.Alpha(new Vector2(1.05f, 0f), half, s), ContactShadows.Alpha(new Vector2(1.1f, 0f), half, s), "fades outward");
            // Leg pads: darker near the footprint's corners, where legs stand; none mid-side.
            float leg = ContactShadows.Alpha(new Vector2(0.95f, 0.4f), half, s);
            Assert.Greater(leg, s + 0.15f, "a leg's pad is clearly darker (0.75 against 0.55)");
            Assert.AreEqual(s, ContactShadows.Alpha(new Vector2(0f, 0.45f), half, s), 1e-5f, "mid-side: no pad");
            Assert.LessOrEqual(leg, 1f);
        }
    }
}
