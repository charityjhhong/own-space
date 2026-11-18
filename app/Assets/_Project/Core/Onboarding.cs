using System;
using OwnSpace.Contracts;

namespace OwnSpace.Core
{
    /// <summary>
    /// The guided first run: scan → my zone → redesign one my-zone piece → pick a path for the outside (an AI
    /// style, or walls, floor and furniture by hand) → done, then all tabs. It never drives the design; it watches it. Call
    /// <see cref="Observe(DesignState, bool)"/> on every <c>DesignSession.Changed</c> and <see cref="Tick"/>
    /// every frame (allocation-free). A step already satisfied is passed straight away, so doing things out of
    /// order is fine, except a step the user went <see cref="Back"/> to: it holds until <see cref="Continue"/>
    /// or <see cref="Choose"/>. Skip ends it at any step. Done or skipped stays off on later launches once saved.
    /// </summary>
    public sealed class Onboarding
    {
        /// <summary>CONCEPT goal: the first payoff (a redesigned or AI-placed piece) within one minute.</summary>
        public const float PayoffTargetSeconds = 60f;

        /// <summary>Longest frame counted, so a long hitch or a resume after sleep does not inflate the clock.</summary>
        public const float MaxTickSeconds = 0.5f;

        /// <summary>Steps before Done, for "Step n of 5" (<see cref="Number"/>).</summary>
        public const int StepCount = 5;

        DesignState _last;
        bool _lastHasRoom = true;

        public Onboarding(OnboardingState state = null)
        {
            State = state ?? new OnboardingState();
        }

        public OnboardingState State { get; }
        public OnboardingStep Step => State.step;
        public OnboardingBranch Branch => State.branch;

        /// <summary>Show the onboarding UI: not done and not skipped.</summary>
        public bool IsActive => !State.skipped && State.step != OnboardingStep.Done;
        public bool IsPaused { get; private set; }

        /// <summary>The user went back to this step: it waits for <see cref="Continue"/> (or <see cref="Choose"/>) even if already satisfied.</summary>
        public bool IsHeld { get; private set; }

        /// <summary>Whether <see cref="Back"/> would move (not on the first step).</summary>
        public bool CanGoBack => IsActive && Previous(State.step, State.branch) != State.step;

        /// <summary>Null until the first payoff; then whether it came within <see cref="PayoffTargetSeconds"/>.</summary>
        public bool? PayoffWithinTarget =>
            State.firstPayoffSeconds < 0f ? (bool?)null : State.firstPayoffSeconds <= PayoffTargetSeconds;

        /// <summary>Raised with the new step whenever the step changes (forward, back, or to Done).</summary>
        public event Action<OnboardingStep> StepChanged;

        /// <summary>Raised once when onboarding ends, by finishing or skipping. Save the state then.</summary>
        public event Action Ended;

        /// <summary>Same as <see cref="Observe(DesignState, bool)"/> with a room present (the panel before the scan step existed).</summary>
        public void Observe(DesignState design) => Observe(design, true);

        /// <param name="hasRoom">A usable room scan, or the sample room, is in use.</param>
        public void Observe(DesignState design, bool hasRoom)
        {
            if (design != null) { _last = design; _lastHasRoom = hasRoom; }
            if (!IsActive || design == null) return;
            if (State.firstPayoffSeconds < 0f && (HasRedesign(design) || HasAiPiece(design))) State.firstPayoffSeconds = State.activeSeconds;
            if (State.step == OnboardingStep.ChooseBranch && State.branch == OnboardingBranch.None && !IsHeld)
                State.branch = HasAiPiece(design) ? OnboardingBranch.Ai : HasOutside(design) ? OnboardingBranch.ByHand : OnboardingBranch.None;
            while (IsActive && !IsHeld && Satisfied(State.step, design, hasRoom, State.branch)) MoveTo(Next(State.step, State.branch));
        }

        /// <summary>The path picked on the branch page; moves on from ChooseBranch (also after Back).</summary>
        public void Choose(OnboardingBranch branch)
        {
            if (!IsActive || branch == OnboardingBranch.None) return;
            State.branch = branch;
            if (State.step != OnboardingStep.ChooseBranch) return;
            IsHeld = false;
            MoveTo(Next(OnboardingStep.ChooseBranch, branch));
            Recheck();
        }

        /// <summary>One step back (from the branch's step to the branch page, which clears the branch). The step then holds.</summary>
        public void Back()
        {
            if (!CanGoBack) return;
            var to = Previous(State.step, State.branch);
            if (to == OnboardingStep.ChooseBranch) State.branch = OnboardingBranch.None;
            IsHeld = true;
            MoveTo(to);
        }

        /// <summary>"Next" on a held step: moves on when the step is satisfied (a branch page needs <see cref="Choose"/>).</summary>
        public void Continue()
        {
            if (!IsActive) return;
            IsHeld = false;
            Recheck();
        }

        public void Tick(float deltaSeconds)
        {
            if (!IsActive || IsPaused || !(deltaSeconds > 0f)) return;
            State.activeSeconds += Math.Min(deltaSeconds, MaxTickSeconds);
        }

        public void Pause() => IsPaused = true;
        public void Resume() => IsPaused = false;

        /// <summary>Ends onboarding at any step: all tabs from now on.</summary>
        public void Skip()
        {
            if (!IsActive) return;
            State.skipped = true;
            IsHeld = false;
            State.step = OnboardingStep.Done;
            StepChanged?.Invoke(OnboardingStep.Done);
            Ended?.Invoke();
        }

        /// <summary>One line for a local Debug.Log when onboarding ends. Allocates; not for per-frame use.</summary>
        public string Summary() =>
            $"Onboarding {(State.skipped ? "skipped" : State.step.ToString())} (path {State.branch}) after {State.activeSeconds:0.0}s; " +
            (State.firstPayoffSeconds < 0f
                ? "no payoff yet"
                : $"first payoff at {State.firstPayoffSeconds:0.0}s (target {PayoffTargetSeconds:0}s: {(PayoffWithinTarget == true ? "met" : "missed")})");

        void Recheck()
        {
            if (_last != null) Observe(_last, _lastHasRoom);
        }

        void MoveTo(OnboardingStep step)
        {
            State.step = step;
            StepChanged?.Invoke(step);
            if (step == OnboardingStep.Done) Ended?.Invoke();
        }

        // ---- The flow, for the panel (pure) ----

        /// <summary>The step after this one on the given path.</summary>
        public static OnboardingStep Next(OnboardingStep step, OnboardingBranch branch)
        {
            switch (step)
            {
                case OnboardingStep.Scan: return OnboardingStep.SetZone;
                case OnboardingStep.SetZone: return OnboardingStep.RedesignOne;
                case OnboardingStep.RedesignOne: return OnboardingStep.ChooseBranch;
                case OnboardingStep.ChooseBranch:
                    return branch == OnboardingBranch.Ai ? OnboardingStep.AiStyle
                        : branch == OnboardingBranch.ByHand ? OnboardingStep.Outside : OnboardingStep.ChooseBranch;
                default: return OnboardingStep.Done; // both branches end the guide
            }
        }

        /// <summary>The step before this one; the first step returns itself.</summary>
        public static OnboardingStep Previous(OnboardingStep step, OnboardingBranch branch)
        {
            switch (step)
            {
                case OnboardingStep.SetZone: return OnboardingStep.Scan;
                case OnboardingStep.RedesignOne: return OnboardingStep.SetZone;
                case OnboardingStep.ChooseBranch: return OnboardingStep.RedesignOne;
                case OnboardingStep.AiStyle:
                case OnboardingStep.Outside:
                case OnboardingStep.FillOutside:
                case OnboardingStep.PickMood: return OnboardingStep.ChooseBranch;
                default: return step;
            }
        }

        /// <summary>1-based position for "Step n of <see cref="StepCount"/>"; 0 for Done.</summary>
        public static int Number(OnboardingStep step)
        {
            switch (step)
            {
                case OnboardingStep.Scan: return 1;
                case OnboardingStep.SetZone: return 2;
                case OnboardingStep.RedesignOne: return 3;
                case OnboardingStep.ChooseBranch: return 4;
                case OnboardingStep.AiStyle:
                case OnboardingStep.Outside:
                case OnboardingStep.FillOutside:
                case OnboardingStep.PickMood: return 5;
                default: return 0;
            }
        }

        /// <summary>The page the panel shows during a step; <see cref="OnboardingPage.Tabs"/> means show <see cref="TabsFor"/>.</summary>
        public static OnboardingPage PageFor(OnboardingStep step)
        {
            switch (step)
            {
                case OnboardingStep.Scan: return OnboardingPage.ScanPrompt;
                case OnboardingStep.SetZone: return OnboardingPage.ZoneSetup;
                case OnboardingStep.ChooseBranch: return OnboardingPage.BranchChoice;
                default: return OnboardingPage.Tabs;
            }
        }

        /// <summary>The tabs the panel offers during a step (all of them once done).</summary>
        public static PanelTabs TabsFor(OnboardingStep step)
        {
            switch (step)
            {
                case OnboardingStep.Scan:
                case OnboardingStep.SetZone:
                case OnboardingStep.ChooseBranch: return PanelTabs.None;
                case OnboardingStep.AiStyle: return PanelTabs.Ai;
                case OnboardingStep.RedesignOne: return PanelTabs.MyRoom;
                case OnboardingStep.Outside:
                case OnboardingStep.FillOutside:
                case OnboardingStep.PickMood: return PanelTabs.Style | PanelTabs.Add;
                default: return PanelTabs.All;
            }
        }

        /// <summary>The tab selected when the step starts (one flag); None on single-purpose pages.</summary>
        public static PanelTabs OpenTab(OnboardingStep step)
        {
            var tabs = TabsFor(step);
            if (tabs == PanelTabs.None) return PanelTabs.None;
            return (tabs & PanelTabs.Style) != 0 ? PanelTabs.Style : (tabs & PanelTabs.MyRoom) != 0 ? PanelTabs.MyRoom : PanelTabs.Ai;
        }

        /// <summary>
        /// What passes each step: a room (scan or sample room); a zone of 3+ points; a redesigned piece; a chosen
        /// path; an AI-placed piece; an outside floor or wall finish, or any outside piece.
        /// </summary>
        public static bool Satisfied(OnboardingStep step, DesignState d, bool hasRoom, OnboardingBranch branch)
        {
            if (d == null) return false;
            switch (step)
            {
                case OnboardingStep.Scan: return hasRoom;
                case OnboardingStep.SetZone: return d.zoneOutline != null && d.zoneOutline.Count >= 3;
                case OnboardingStep.ChooseBranch: return branch != OnboardingBranch.None;
                case OnboardingStep.AiStyle: return HasAiPiece(d);
                case OnboardingStep.RedesignOne: return HasRedesign(d);
                case OnboardingStep.Outside:
                case OnboardingStep.FillOutside:
                case OnboardingStep.PickMood: return HasOutside(d);
                default: return false;
            }
        }

        static bool HasOutside(DesignState d) =>
            !string.IsNullOrEmpty(d.outsideFinish.floorFinishId) || !string.IsNullOrEmpty(d.outsideFinish.wallFinishId) ||
            (d.placements != null && d.placements.Count > 0);

        static bool HasRedesign(DesignState d)
        {
            if (d.anchorChoices == null) return false;
            for (int i = 0; i < d.anchorChoices.Count; i++)
                if (d.anchorChoices[i].mode == AnchorMode.Redesign) return true;
            return false;
        }

        static bool HasAiPiece(DesignState d)
        {
            if (d.placements == null) return false;
            for (int i = 0; i < d.placements.Count; i++)
                if (d.placements[i].aiPlaced) return true;
            return false;
        }
    }
}
