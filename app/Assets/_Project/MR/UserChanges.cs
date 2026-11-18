using System.Collections.Generic;
using OwnSpace.Core;

namespace OwnSpace.MR
{
    /// <summary>A user change kept outside core's session (e.g. a piece's fine-tuning): it undoes and redoes itself.</summary>
    public interface IUndoStep
    {
        void Undo();
        void Redo();
    }

    /// <summary>
    /// The user's undo and redo history on top of core's session steps: one user change may be several session
    /// commits (e.g. a cover = add prop + remove box) and undoes and redoes as one. MR-side changes that core does not
    /// hold (<see cref="IUndoStep"/>: Adjust fit) sit in the same history, in order. The undo side starts at a
    /// barrier (the zone commit is never undone). A new user change ends the redo history, and so does any
    /// session change that clears core's own redo stack (an AI plan applied, a load), so the counts never drift.
    /// When core's undo stack is gone (a load) the undo side is cleared too, and it never holds more session
    /// steps than core keeps (<see cref="DesignSession.MaxUndo"/>): an old entry can never undo a later AI plan.
    /// </summary>
    public sealed class UserChanges
    {
        struct Entry { public int Steps; public IUndoStep Step; }

        readonly List<Entry> _undo = new List<Entry>(), _redo = new List<Entry>();
        const int MaxEntries = 64; // MR-side steps hold no session commits; keep the list bounded anyway
        bool _busy; // our own undo or redo is running: its session changes are not someone else's

        /// <summary>Undoable user changes since the barrier.</summary>
        public int Count => _undo.Count;
        public int RedoCount => _redo.Count;

        public void Begin() { _undo.Clear(); _redo.Clear(); }

        public void Note(int sessionSteps)
        {
            if (sessionSteps <= 0) return;
            Push(new Entry { Steps = sessionSteps });
        }

        /// <summary>One MR-side user change (one per gesture or press, never per frame).</summary>
        public void Note(IUndoStep step)
        {
            if (step == null) return;
            Push(new Entry { Step = step });
        }

        void Push(Entry e)
        {
            _undo.Add(e);
            _redo.Clear();
            Clamp();
        }

        public bool CanUndo(DesignSession s) => _undo.Count > 0 && (Last(_undo).Step != null || s.CanUndo);
        public bool CanRedo(DesignSession s) => _redo.Count > 0 && (Last(_redo).Step != null || s.CanRedo);

        public bool Undo(DesignSession s)
        {
            if (!CanUndo(s)) return false;
            Entry e = Pop(_undo);
            if (e.Step != null) { e.Step.Undo(); _redo.Add(e); return true; }
            int done = 0;
            _busy = true;
            try { for (int i = 0; i < e.Steps && s.Undo(); i++) done++; }
            finally { _busy = false; }
            if (done > 0) _redo.Add(new Entry { Steps = done });
            return done > 0;
        }

        public bool Redo(DesignSession s)
        {
            if (!CanRedo(s)) return false;
            Entry e = Pop(_redo);
            if (e.Step != null) { e.Step.Redo(); _undo.Add(e); Clamp(); return true; }
            int done = 0;
            _busy = true;
            try { for (int i = 0; i < e.Steps && s.Redo(); i++) done++; }
            finally { _busy = false; }
            if (done > 0) { _undo.Add(new Entry { Steps = done }); Clamp(); }
            return done > 0;
        }

        /// <summary>Call on every session change: when core's redo stack is gone, ours is too.</summary>
        public void SessionChanged(DesignSession s)
        {
            if (_busy) return; // our own undo or redo: the history is already right
            if (!s.CanRedo) _redo.Clear();
            if (!s.CanUndo) _undo.Clear(); // a load cleared core's history: nothing of ours can be undone
        }

        // Core drops its oldest commits past MaxUndo; drop our oldest entries so they never point past them.
        void Clamp()
        {
            int total = 0;
            for (int i = 0; i < _undo.Count; i++) total += _undo[i].Steps;
            while ((total > DesignSession.MaxUndo || _undo.Count > MaxEntries) && _undo.Count > 0) { total -= _undo[0].Steps; _undo.RemoveAt(0); }
        }

        static Entry Last(List<Entry> list) => list[list.Count - 1];

        static Entry Pop(List<Entry> list)
        {
            Entry last = list[list.Count - 1];
            list.RemoveAt(list.Count - 1);
            return last;
        }
    }
}
