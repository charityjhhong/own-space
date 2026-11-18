using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Core;

namespace OwnSpace.Tests
{
    /// <summary>xr sim pass (#1): a redesigned desk smaller than the real desk box left the real desk showing on both sides.</summary>
    public class FitCoverageTests
    {
        // The strict rule (never shrunk, stretched at most MaxFootprintScale) holds for every role but Table,
        // which has its own wider range (DeskFitTests). These boxes are Bed-role pieces of desk size.
        static FurnitureAnchor DeskBox(float w, float d, float yaw = 0f) =>
            new FurnitureAnchor("desk", FurnitureRole.Bed, new OrientedBox(new Vec3(0, 0.375f, 0), new Vec3(w, 0.75f, d), yaw), 0.75f);

        static CatalogItem Desk(float w, float d) =>
            new CatalogItem { id = "d", role = FurnitureRole.Bed, size = new Vec3(w, 0.75f, d), usableHeight = 0.75f };

        // Width/depth the placed item covers along the anchor's own axes.
        static (float w, float d) Covered(FitResult r, CatalogItem item) => r.Rotated90
            ? (item.size.z * r.ZScale, item.size.x * r.XScale)
            : (item.size.x * r.XScale, item.size.z * r.ZScale);

        [Test]
        public void SmallerDesk_IsStretchedToCoverTheRealBox()
        {
            var item = Desk(1.2f, 0.6f);
            var r = FitRule.Check(DeskBox(1.5f, 0.7f), item);
            Assert.IsTrue(r.Fits, r.Status.ToString());
            var (w, d) = Covered(r, item);
            Assert.GreaterOrEqual(w, 1.5f - 1e-4f, "real desk must not show at the sides");
            Assert.GreaterOrEqual(d, 0.7f - 1e-4f, "or at the front");
            Assert.AreEqual(1.25f, r.XScale, 1e-4f);
        }

        [Test]
        public void SixteenMeterBox_NeedsMoreThanTheLimit_AndIsNotOffered()
        {
            // 1.6 / 1.2 = 1.333 > MaxFootprintScale (1.3), even with the 1 cm tolerance.
            var r = FitRule.Check(DeskBox(1.6f, 0.6f), Desk(1.2f, 0.6f));
            Assert.AreEqual(FitStatus.NeedsTooMuchStretch, r.Status);
        }

        [Test]
        public void StretchLimit_IsInclusiveWithTolerance()
        {
            float limit = 1.0f * FitLimits.MaxFootprintScale;
            Assert.IsTrue(FitRule.Check(DeskBox(limit + FitLimits.Tolerance, 0.6f), Desk(1.0f, 0.6f)).Fits);
            Assert.AreEqual(FitStatus.NeedsTooMuchStretch,
                FitRule.Check(DeskBox(limit + FitLimits.Tolerance + 0.005f, 0.6f), Desk(1.0f, 0.6f)).Status);
        }

        [Test]
        public void LargerOrEqualItem_IsNeverShrunkOrStretched()
        {
            var r = FitRule.Check(DeskBox(1.3f, 0.6f), Desk(1.38f, 0.65f));
            Assert.IsTrue(r.Fits);
            Assert.AreEqual(1f, r.XScale);
            Assert.AreEqual(1f, r.ZScale);
            Assert.AreEqual(FitStatus.FootprintTooLarge, FitRule.Check(DeskBox(1.3f, 0.6f), Desk(1.45f, 0.6f)).Status);
        }

        [Test]
        public void Rotation_PicksTheLeastStretch()
        {
            // A 2.0 x 0.95 bed box; a 1.0 x 2.0 bed fits only turned, with no stretch.
            var bedBox = new FurnitureAnchor("bed", FurnitureRole.Bed, new OrientedBox(new Vec3(0, 0.25f, 0), new Vec3(2.0f, 0.5f, 0.95f), 0f), 0.5f);
            var bed = new CatalogItem { id = "b", role = FurnitureRole.Bed, size = new Vec3(1.0f, 0.6f, 2.0f), usableHeight = 0.5f };
            var r = FitRule.Check(bedBox, bed);
            Assert.IsTrue(r.Rotated90);
            Assert.AreEqual(1f, r.XScale);
            Assert.AreEqual(1f, r.ZScale);

            // A square-ish table: 0.8 x 0.7 on a 0.9 x 0.8 box: as-is needs 1.125 / 1.143; turned needs 1.286 / 1.0. As-is wins.
            var t = FitRule.Check(DeskBox(0.9f, 0.8f), Desk(0.8f, 0.7f));
            Assert.IsFalse(t.Rotated90);
            Assert.AreEqual(0.9f / 0.8f, t.XScale, 1e-4f);
            Assert.AreEqual(0.8f / 0.7f, t.ZScale, 1e-4f);
        }
    }
}
