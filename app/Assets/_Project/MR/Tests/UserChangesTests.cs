using NUnit.Framework;
using OwnSpace.Contracts.Fakes;
using OwnSpace.Core;

namespace OwnSpace.MR.Tests
{
    public class UserChangesTests
    {
        DesignSession _session;
        UserChanges _changes;

        [SetUp]
        public void SetUp()
        {
            _session = new DesignSession(FakeRoomInfo.DormRoom());
            _changes = new UserChanges();
            _session.Changed += () => _changes.SessionChanged(_session);
        }

        void UserMood(string id) { _session.SetMood(id); _changes.Note(1); }

        [Test]
        public void UndoUndoRedo_ThenANewChange_EndsTheRedoHistory()
        {
            UserMood("morning");
            UserMood("evening");
            Assert.IsTrue(_changes.Undo(_session));
            Assert.IsTrue(_changes.Undo(_session));
            Assert.IsTrue(_changes.Redo(_session));
            Assert.AreEqual("morning", _session.State.moodPresetId);
            Assert.AreEqual(1, _changes.Count);
            Assert.AreEqual(1, _changes.RedoCount);
            UserMood("night");
            Assert.IsFalse(_changes.CanRedo(_session));
            Assert.AreEqual(0, _changes.RedoCount);
            Assert.AreEqual(2, _changes.Count);
        }

        [Test]
        public void ANonUserChange_ThatClearsCoreRedo_ClearsOursToo()
        {
            UserMood("morning");
            _changes.Undo(_session);
            Assert.IsTrue(_changes.CanRedo(_session));
            _session.SetMood("night"); // e.g. an AI plan: a session change not noted as a user change
            Assert.IsFalse(_session.CanRedo);
            Assert.AreEqual(0, _changes.RedoCount, "no stale redo entry");
            Assert.IsFalse(_changes.Redo(_session));
        }

        [Test]
        public void AMultiStepChange_UndoesAndRedoesAsOne()
        {
            _session.SetMood("morning");
            _session.SetMood("evening");
            _changes.Note(2);
            Assert.IsTrue(_changes.Undo(_session));
            Assert.IsTrue(string.IsNullOrEmpty(_session.State.moodPresetId));
            Assert.IsTrue(_changes.Redo(_session));
            Assert.AreEqual("evening", _session.State.moodPresetId);
            Assert.AreEqual(1, _changes.Count);
        }

        [Test]
        public void Load_ClearsBothSides_SoALaterPlanIsNotUndoneAsAUserChange()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ownspace-userchanges-" + System.Guid.NewGuid().ToString("N") + ".json");
            try
            {
                UserMood("morning");
                UserMood("evening");
                _changes.Undo(_session);
                _session.Save(path);
                Assert.IsTrue(_session.Load(path));
                Assert.AreEqual(0, _changes.Count, "core's undo is gone after a load");
                Assert.AreEqual(0, _changes.RedoCount);
                _session.SetMood("night"); // e.g. an AI plan after the load, not a user change
                Assert.IsFalse(_changes.Undo(_session), "no stale entry undoes it");
                Assert.AreEqual("night", _session.State.moodPresetId);
            }
            finally { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
        }

        [Test]
        public void NeverHoldsMoreStepsThanCoreKeeps()
        {
            for (int i = 0; i < DesignSession.MaxUndo + 10; i++) UserMood(i % 2 == 0 ? "morning" : "night");
            Assert.AreEqual(DesignSession.MaxUndo, _changes.Count);
            int undone = 0;
            while (_changes.Undo(_session)) undone++;
            Assert.AreEqual(DesignSession.MaxUndo, undone, "every kept entry still undoes a real step");
        }

        // Device (build 0ddb26d): undo did nothing after Narrower. Adjust-fit steps sit in the same history, in order.
        sealed class FakeAdjust : IUndoStep
        {
            readonly System.Collections.Generic.List<string> _log; readonly string _name;
            public FakeAdjust(System.Collections.Generic.List<string> log, string name) { _log = log; _name = name; }
            public void Undo() => _log.Add("undo " + _name);
            public void Redo() => _log.Add("redo " + _name);
        }

        [Test]
        public void AdjustSteps_UndoAndRedo_InOrderWithTheSessionsSteps()
        {
            var log = new System.Collections.Generic.List<string>();
            UserMood("morning");
            _changes.Note(new FakeAdjust(log, "narrower"));
            UserMood("evening");
            _changes.Note(new FakeAdjust(log, "drag"));

            Assert.IsTrue(_changes.Undo(_session)); // the drag
            Assert.IsTrue(_changes.Undo(_session)); // evening
            Assert.AreEqual("morning", _session.State.moodPresetId);
            Assert.IsTrue(_changes.Undo(_session)); // narrower
            Assert.IsTrue(_changes.Undo(_session)); // morning
            Assert.IsTrue(string.IsNullOrEmpty(_session.State.moodPresetId));
            Assert.IsFalse(_changes.CanUndo(_session), "nothing left: the arrow greys");
            CollectionAssert.AreEqual(new[] { "undo drag", "undo narrower" }, log);

            Assert.IsTrue(_changes.Redo(_session)); // morning: core's redo still holds evening
            Assert.IsTrue(_changes.Redo(_session)); // narrower
            Assert.IsTrue(_changes.Redo(_session)); // evening: core's redo stack is now empty
            Assert.IsTrue(_changes.CanRedo(_session), "our own redo emptying core's stack keeps the drag's redo");
            Assert.IsTrue(_changes.Redo(_session)); // the drag
            Assert.AreEqual("evening", _session.State.moodPresetId);
            CollectionAssert.AreEqual(new[] { "undo drag", "undo narrower", "redo narrower", "redo drag" }, log);
            Assert.IsFalse(_changes.CanRedo(_session));
        }

        [Test]
        public void AnAdjust_EndsTheRedoHistory_AndOnlyAdjustsCanStillUndo()
        {
            var log = new System.Collections.Generic.List<string>();
            UserMood("morning");
            _changes.Undo(_session);
            Assert.IsTrue(_changes.CanRedo(_session));
            _changes.Note(new FakeAdjust(log, "wider"));
            Assert.IsFalse(_changes.CanRedo(_session), "a new change ends the redo history");
            Assert.IsTrue(_changes.CanUndo(_session));
            Assert.IsTrue(_changes.Undo(_session));
            CollectionAssert.AreEqual(new[] { "undo wider" }, log);
        }
    }
}
