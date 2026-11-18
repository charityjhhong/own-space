using System;
using System.IO;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Contracts.Fakes;
using OwnSpace.Core;

namespace OwnSpace.Tests
{
    public class DesignStateTests
    {
        static DesignState Sample()
        {
            var s = new DesignState
            {
                zoneOutline = FakeRoomInfo.HalfZone("a"),
                outsideFinish = new ZoneFinish { floorFinishId = "floor-oak", wallFinishId = "" },
                myZoneFinish = new ZoneFinish { floorFinishId = "", wallFinishId = "wall-plaster" },
                moodPresetId = "evening",
            };
            s.SetChoice(new AnchorChoice { anchorId = "a-desk", mode = AnchorMode.Redesign, catalogItemId = "desk-oak-01", yScale = 1.02f, xScale = 1.1f, zScale = 1f });
            s.SetChoice(new AnchorChoice { anchorId = "a-bed", mode = AnchorMode.KeepReal, catalogItemId = "", yScale = 1f, xScale = 1f, zScale = 1f });
            s.placements.Add(new FreePlacement
            {
                placementId = "p1", catalogItemId = "sofa-01", position = new Vec3(1.2f, 0f, 2.5f), yawDegrees = -90f,
            });
            return s;
        }

        [Test]
        public void RoundTrip_KeepsEveryField()
        {
            var json = DesignStateJson.ToJson(Sample());
            Assert.IsTrue(DesignStateJson.TryFromJson(json, out var back));

            Assert.AreEqual(DesignState.CurrentVersion, back.version);
            Assert.AreEqual(4, back.zoneOutline.Count);
            Assert.AreEqual(-1.8f, back.zoneOutline[0].x, 1e-5f);
            Assert.AreEqual(2, back.anchorChoices.Count);
            Assert.AreEqual(AnchorMode.Redesign, back.ChoiceFor("a-desk").mode);
            Assert.AreEqual("desk-oak-01", back.ChoiceFor("a-desk").catalogItemId);
            Assert.AreEqual(1, back.placements.Count);
            Assert.AreEqual("sofa-01", back.placements[0].catalogItemId);
            Assert.AreEqual(2.5f, back.placements[0].position.z, 1e-5f);
            Assert.AreEqual(-90f, back.placements[0].yawDegrees, 1e-5f);
            Assert.AreEqual("floor-oak", back.outsideFinish.floorFinishId);
            Assert.AreEqual("", back.outsideFinish.wallFinishId);
            Assert.AreEqual("wall-plaster", back.myZoneFinish.wallFinishId);
            Assert.AreEqual("evening", back.moodPresetId);
            Assert.AreEqual(json, DesignStateJson.ToJson(back));
        }

        [Test]
        public void Enums_AreWrittenAsNames() =>
            StringAssert.Contains("\"Redesign\"", DesignStateJson.ToJson(Sample()));

        [Test]
        public void UnknownAnchor_IsKeptReal() =>
            Assert.AreEqual(AnchorMode.KeepReal, new DesignState().ChoiceFor("b-desk").mode);

        [Test]
        public void SetChoice_ReplacesExisting()
        {
            var s = Sample();
            s.SetChoice(new AnchorChoice { anchorId = "a-desk", mode = AnchorMode.KeepReal, catalogItemId = "" });
            Assert.AreEqual(2, s.anchorChoices.Count);
            Assert.AreEqual(AnchorMode.KeepReal, s.ChoiceFor("a-desk").mode);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("{ not json")]
        [TestCase("{\"version\": 999}")]
        public void BadSave_GivesEmptyState(string json)
        {
            Assert.IsFalse(DesignStateJson.TryFromJson(json, out var state));
            Assert.IsNotNull(state);
            Assert.AreEqual(0, state.anchorChoices.Count);
            Assert.AreEqual("", state.moodPresetId);
        }

        [Test]
        public void FileSave_OverwritesAtomicallyAndLoadsBack()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ownspace-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "design.json");
                Assert.IsFalse(DesignStateJson.TryLoadFromFile(path, out _));

                DesignStateJson.SaveToFile(path, new DesignState { moodPresetId = "morning" });
                File.WriteAllText(path + ".tmp", "stale leftover from a crash");
                DesignStateJson.SaveToFile(path, Sample());

                Assert.IsFalse(File.Exists(path + ".tmp"));
                Assert.IsTrue(DesignStateJson.TryLoadFromFile(path, out var back));
                Assert.AreEqual("evening", back.moodPresetId);
                Assert.AreEqual("desk-oak-01", back.ChoiceFor("a-desk").catalogItemId);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void FailedSave_KeepsThePreviousSave()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ownspace-tests-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "design.json");
            Directory.CreateDirectory(dir);
            try
            {
                DesignStateJson.SaveToFile(path, Sample());
                Directory.CreateDirectory(path + ".tmp"); // the temp file cannot be written
                Assert.That(() => DesignStateJson.SaveToFile(path, new DesignState { moodPresetId = "night" }),
                    Throws.InstanceOf<Exception>());

                Assert.IsTrue(DesignStateJson.TryLoadFromFile(path, out var kept));
                Assert.AreEqual("evening", kept.moodPresetId);
                Assert.AreEqual("desk-oak-01", kept.ChoiceFor("a-desk").catalogItemId);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void FailedSave_LeavesNoTempFile()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ownspace-tests-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "design.json");
            Directory.CreateDirectory(path); // A directory where the save file should be: the final step fails.
            try
            {
                Assert.That(() => DesignStateJson.SaveToFile(path, Sample()), Throws.InstanceOf<Exception>());
                Assert.IsFalse(File.Exists(path + ".tmp"));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        const string V1Save = @"{
  ""version"": 1,
  ""zoneOutline"": [ { ""x"": -1.8, ""y"": 0.0 }, { ""x"": 0.0, ""y"": 0.0 }, { ""x"": 0.0, ""y"": 5.0 } ],
  ""anchorChoices"": [ { ""anchorId"": ""a-desk"", ""mode"": ""Redesign"", ""catalogItemId"": ""desk-scandi-01"" } ],
  ""placements"": [],
  ""floorFinishId"": ""floor-oak-planks"",
  ""wallFinishId"": ""wall-cream"",
  ""moodPresetId"": ""evening""
}";

        [Test]
        public void V1Save_MigratesFinishesToOutsideZoneAndWritesV2()
        {
            Assert.IsTrue(DesignStateJson.TryFromJson(V1Save, out var s));
            Assert.AreEqual(2, s.version);
            Assert.AreEqual("floor-oak-planks", s.outsideFinish.floorFinishId);
            Assert.AreEqual("wall-cream", s.outsideFinish.wallFinishId);
            Assert.AreEqual("", s.myZoneFinish.floorFinishId);
            Assert.AreEqual(3, s.zoneOutline.Count);
            Assert.AreEqual("desk-scandi-01", s.ChoiceFor("a-desk").catalogItemId);
            Assert.AreEqual("evening", s.moodPresetId);

            string written = DesignStateJson.ToJson(s);
            StringAssert.Contains("\"version\": 2", written);
            Assert.IsTrue(DesignStateJson.TryFromJson(written, out var again));
            Assert.AreEqual("wall-cream", again.outsideFinish.wallFinishId);
        }

        [Test]
        public void V1Save_WithOddFinishValues_LoadsAsReal()
        {
            Assert.IsTrue(DesignStateJson.TryFromJson("{\"version\":1,\"floorFinishId\":{\"a\":1},\"wallFinishId\":7}", out var s));
            Assert.AreEqual("", s.outsideFinish.floorFinishId);
            Assert.AreEqual("", s.outsideFinish.wallFinishId);
        }

        [Test]
        public void ExplicitNulls_AreRepaired()
        {
            Assert.IsTrue(DesignStateJson.TryFromJson("{\"version\":2,\"placements\":null,\"moodPresetId\":null,\"outsideFinish\":{\"floorFinishId\":null}}", out var s));
            Assert.IsNotNull(s.placements);
            Assert.AreEqual("", s.moodPresetId);
            Assert.AreEqual("", s.outsideFinish.floorFinishId);
            Assert.AreEqual("", s.myZoneFinish.wallFinishId);
        }
    }
}
