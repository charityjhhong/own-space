using System.Collections.Generic;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Core;

namespace OwnSpace.Tests
{
    public class PlanParserTests
    {
        static AssistantPlan Parse(string json, List<string> errors = null)
        {
            Assert.IsTrue(PlanParser.TryParse(json, out var plan, errors ?? new List<string>()));
            return plan;
        }

        [Test]
        public void ValidPlan_IsReadCompletely()
        {
            var plan = Parse("{\"placements\":[{\"catalogItemId\":\"sofa-cafe-01\",\"x\":1.2,\"z\":3,\"yawDegrees\":-90}," +
                             "{\"catalogItemId\":\"plant-cafe-01\"}],\"floorFinishId\":\"floor-dark-planks\",\"wallFinishId\":\"\"," +
                             "\"moodPresetId\":\"afternoon\",\"rationale\":\"Cafe corner.\",\"fromFallback\":true,\"notice\":\"hi\"}");
            Assert.AreEqual(2, plan.placements.Count);
            Assert.AreEqual(1.2f, plan.placements[0].x.Value, 1e-6f);
            Assert.AreEqual(270f, plan.placements[0].yawDegrees, 1e-4f);
            Assert.IsFalse(plan.placements[1].x.HasValue);
            Assert.AreEqual("floor-dark-planks", plan.floorFinishId);
            Assert.AreEqual("afternoon", plan.moodPresetId);
            Assert.IsFalse(plan.fromFallback, "core's field, never the model's");
            Assert.AreEqual("", plan.notice);
        }

        [Test]
        public void FencedJson_IsAccepted() =>
            Assert.AreEqual("night", Parse("```json\n{\"moodPresetId\":\"night\"}\n```").moodPresetId);

        [TestCase(null)]
        [TestCase("")]
        [TestCase("Sure! Here is a cozy room.")]
        [TestCase("[1,2]")]
        public void NonObject_FailsTheWholePlan(string text)
        {
            var errors = new List<string>();
            Assert.IsFalse(PlanParser.TryParse(text, out var plan, errors));
            Assert.AreEqual(0, plan.placements.Count);
            Assert.AreEqual(1, errors.Count);
        }

        [Test]
        public void BadEntries_AreDroppedWithReasons()
        {
            var errors = new List<string>();
            var plan = Parse("{\"placements\":[{\"catalogItemId\":\"Sofa!\"},{\"catalogItemId\":\"lamp-cafe-01\",\"x\":1}," +
                             "{\"catalogItemId\":\"rug-cafe-01\",\"x\":\"1\",\"z\":2},42,{\"catalogItemId\":\"rug-cafe-01\",\"x\":1e9,\"z\":2}," +
                             "{\"catalogItemId\":\"plant-cafe-01\",\"x\":0.5,\"z\":2}],\"moodPresetId\":\"Evening <script>\"}", errors);
            Assert.AreEqual(1, plan.placements.Count);
            Assert.AreEqual("plant-cafe-01", plan.placements[0].catalogItemId);
            Assert.AreEqual("", plan.moodPresetId);
            Assert.AreEqual(6, errors.Count, string.Join("\n", errors));
        }

        [TestCase("50.5")]
        [TestCase("-120")]
        [TestCase("123456789012345678901234567890")]
        [TestCase("1e400")]
        public void OutOfRangeOrHugeCoordinates_AreRefusedWithoutThrowing(string x)
        {
            var errors = new List<string>();
            AssistantPlan plan = null;
            Assert.DoesNotThrow(() => PlanParser.TryParse("{\"placements\":[{\"catalogItemId\":\"plant-cafe-01\",\"x\":" + x + ",\"z\":1}]}", out plan, errors));
            Assert.AreEqual(0, plan.placements.Count);
            Assert.AreEqual(1, errors.Count, string.Join("\n", errors));
        }

        [Test]
        public void CoordinateOnTheLimit_IsAccepted() =>
            Assert.AreEqual(1, Parse("{\"placements\":[{\"catalogItemId\":\"plant-cafe-01\",\"x\":50,\"z\":-50,\"yawDegrees\":1e30}]}").placements.Count);

        [Test]
        public void Counts_AndRationale_AreCapped()
        {
            var many = new List<string>();
            for (int i = 0; i < 20; i++) many.Add("{\"catalogItemId\":\"plant-cafe-01\"}");
            var plan = Parse("{\"placements\":[" + string.Join(",", many) + "],\"rationale\":\"" + new string('a', 1000) + "\"}");
            Assert.AreEqual(PlanParser.MaxPlacements, plan.placements.Count);
            Assert.AreEqual(PlanParser.MaxRationaleLength, plan.rationale.Length);
        }

        [Test]
        public void Recommendations_AreReadStrictly()
        {
            var errors = new List<string>();
            var plan = Parse("{\"recommendations\":[{\"anchorId\":\"a-desk\",\"catalogItemId\":\"desk-scandi-01\",\"reason\":\"Bright.\"}," +
                             "{\"anchorId\":\"a-desk\"},{\"catalogItemId\":\"desk-cafe-01\"},\"desk\"]}", errors);
            Assert.AreEqual(1, plan.recommendations.Count);
            Assert.AreEqual("desk-scandi-01", plan.recommendations[0].catalogItemId);
            Assert.AreEqual("Bright.", plan.recommendations[0].reason);
            Assert.AreEqual(3, errors.Count, string.Join("\n", errors));
        }
    }
}
