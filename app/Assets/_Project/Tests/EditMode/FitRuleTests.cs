using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Contracts.Fakes;
using OwnSpace.Core;

namespace OwnSpace.Tests
{
    public class FitRuleTests
    {
        // Dorm desk: 1.3 wide, 0.75 high, 0.6 deep, usable height 0.75.
        static FurnitureAnchor Desk() => FakeRoomInfo.DormRoom().Furniture[1];

        static CatalogItem Item(FurnitureRole role, float w, float h, float d, float usable = 0f) =>
            new CatalogItem { id = "test", role = role, size = new Vec3(w, h, d), usableHeight = usable };

        static CatalogItem DeskItem(float w, float d, float usable = 0.75f) =>
            Item(FurnitureRole.Table, w, usable, d, usable);

        [Test]
        public void SameSize_FitsAsIsWithUnitScale()
        {
            var r = FitRule.Check(Desk(), DeskItem(1.3f, 0.6f));
            Assert.AreEqual(FitStatus.Fits, r.Status);
            Assert.IsFalse(r.Rotated90);
            Assert.AreEqual(1f, r.YScale, 1e-5f);
        }

        [Test]
        public void SmallerFootprint_Fits() =>
            Assert.IsTrue(FitRule.Check(Desk(), DeskItem(1.0f, 0.5f)).Fits);

        [Test]
        public void UsableHeightDiffers_ScalesYToMatch()
        {
            var r = FitRule.Check(Desk(), DeskItem(1.3f, 0.6f, 0.70f));
            Assert.IsTrue(r.Fits);
            Assert.AreEqual(0.75f / 0.70f, r.YScale, 1e-5f);
        }

        const float M = FitLimits.MarginPerSide, T = FitLimits.Tolerance, Y = FitLimits.MaxYScale;

        [Test]
        public void YScaleExactlyOnLimits_Fits()
        {
            var up = FitRule.Check(Desk(), DeskItem(1.3f, 0.6f, 0.75f / (1f + Y)));
            Assert.IsTrue(up.Fits);
            Assert.AreEqual(1f + Y, up.YScale, 1e-4f);
            Assert.IsTrue(FitRule.Check(Desk(), DeskItem(1.3f, 0.6f, 0.75f / (1f - Y))).Fits);
        }

        [Test]
        public void YScaleBeyondLimitsWithinTolerance_Fits()
        {
            Assert.IsTrue(FitRule.Check(Desk(), DeskItem(1.3f, 0.6f, (0.75f - T + 0.002f) / (1f + Y))).Fits);
            Assert.IsTrue(FitRule.Check(Desk(), DeskItem(1.3f, 0.6f, (0.75f + T - 0.002f) / (1f - Y))).Fits);
        }

        [Test]
        public void YScaleBeyondLimitsAndTolerance_IsHeightOutOfRange()
        {
            Assert.AreEqual(FitStatus.HeightOutOfRange, FitRule.Check(Desk(), DeskItem(1.3f, 0.6f, (0.75f - T - 0.002f) / (1f + Y))).Status);
            Assert.AreEqual(FitStatus.HeightOutOfRange, FitRule.Check(Desk(), DeskItem(1.3f, 0.6f, (0.75f + T + 0.002f) / (1f - Y))).Status);
        }

        [Test]
        public void FootprintExactlyOnMarginPlusTolerance_Fits()
        {
            Vec3 box = Desk().Box.size;
            Assert.IsTrue(FitRule.Check(Desk(), DeskItem(box.x + 2f * M + T, box.z + 2f * M + T)).Fits);
        }

        [Test]
        public void FootprintJustBeyondMarginPlusTolerance_IsTooLarge_ButADeskShrinksToIt()
        {
            var closet = FakeRoomInfo.DormRoom().Furniture[0];
            Vec3 c = closet.Box.size;
            Assert.AreEqual(FitStatus.FootprintTooLarge, FitRule.Check(closet, Item(FurnitureRole.Storage, c.x + 2f * M + T + 0.002f, c.y, c.z)).Status);
            Assert.AreEqual(FitStatus.FootprintTooLarge, FitRule.Check(closet, Item(FurnitureRole.Storage, c.x, c.y, c.z + 2f * M + T + 0.002f)).Status);

            Vec3 box = Desk().Box.size;
            float w = box.x + 2f * M + T + 0.002f;
            var r = FitRule.Check(Desk(), DeskItem(w, box.z));
            Assert.IsTrue(r.Fits);
            Assert.AreEqual(box.x + 2f * M, w * r.XScale, 1e-4f, "a desk is shrunk to the box plus its margins");
        }

        [Test]
        public void FitsOnlyTurned_IsRotated()
        {
            // Dorm bed box: 2.0 along the wall, 0.95 deep. A catalog bed is 1.0 wide, 2.0 long.
            var bed = FakeRoomInfo.DormRoom().Furniture[2];
            var r = FitRule.Check(bed, Item(FurnitureRole.Bed, 1.0f, 0.6f, 2.0f, 0.5f));
            Assert.IsTrue(r.Fits);
            Assert.IsTrue(r.Rotated90);
        }

        [Test]
        public void DifferentRole_IsRoleMismatch() =>
            Assert.AreEqual(FitStatus.RoleMismatch, FitRule.Check(Desk(), Item(FurnitureRole.Bed, 1.3f, 0.75f, 0.6f, 0.75f)).Status);

        [Test]
        public void NoUsableHeight_MatchesBoxTop()
        {
            // Dorm closet: 0.9 x 2.0 x 0.6. A 1.9 m wardrobe scales to 2.0 m.
            var closet = FakeRoomInfo.DormRoom().Furniture[0];
            Assert.IsNull(closet.UsableHeight);
            var r = FitRule.Check(closet, Item(FurnitureRole.Storage, 0.95f, 1.9f, 0.6f));
            Assert.IsTrue(r.Fits);
            Assert.AreEqual(2.0f / 1.9f, r.YScale, 1e-5f);
            Assert.AreEqual(FitStatus.HeightOutOfRange, FitRule.Check(closet, Item(FurnitureRole.Storage, 0.9f, 1.6f, 0.6f)).Status);
            // Storage has no usable surface: the item's top is the target even if it lists a usable height.
            Assert.AreEqual(2.0f / 1.9f, FitRule.Check(closet, Item(FurnitureRole.Storage, 0.95f, 1.9f, 0.6f, 1.0f)).YScale, 1e-5f);
        }

        [TestCase(float.NaN, 0.6f, 0.75f)]
        [TestCase(1.3f, float.NaN, 0.75f)]
        [TestCase(-1.3f, 0.6f, 0.75f)]
        [TestCase(1.3f, 0f, 0.75f)]
        [TestCase(1.3f, 0.6f, float.NaN)]
        [TestCase(1.3f, 0.6f, -0.75f)]
        [TestCase(float.PositiveInfinity, 0.6f, 0.75f)]
        public void BadItemSize_IsInvalid(float w, float d, float usable) =>
            Assert.AreEqual(FitStatus.InvalidSize, FitRule.Check(Desk(), Item(FurnitureRole.Table, w, 0.75f, d, usable)).Status);

        [Test]
        public void BadAnchorSize_IsInvalid()
        {
            var box = new OrientedBox(new Vec3(0, 0, 0), new Vec3(float.NaN, 0.75f, 0.6f), 0f);
            var nanBox = new FurnitureAnchor("x", FurnitureRole.Table, box, 0.75f);
            var badHeight = new FurnitureAnchor("y", FurnitureRole.Table, Desk().Box, -0.1f);
            Assert.AreEqual(FitStatus.InvalidSize, FitRule.Check(nanBox, DeskItem(1.3f, 0.6f)).Status);
            Assert.AreEqual(FitStatus.InvalidSize, FitRule.Check(badHeight, DeskItem(1.3f, 0.6f)).Status);
        }

        [Test]
        public void MissingInput_IsMissing()
        {
            Assert.AreEqual(FitStatus.Missing, FitRule.Check(null, DeskItem(1, 1)).Status);
            Assert.AreEqual(FitStatus.Missing, FitRule.Check(Desk(), null).Status);
        }
    }
}
