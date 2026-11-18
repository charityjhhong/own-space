using System;
using OwnSpace.Contracts;
using OwnSpace.Core;

namespace OwnSpace.MR
{
    /// <summary>
    /// The guided first run's steps (the human: one step at a time, then all five tabs): scan, set the zone, design
    /// the furniture in it (My room), then the outside (the AI, or by hand with Style and Add).
    /// </summary>
    public enum GuideStep { Scan, Zone, Inside, Outside, Done }

    /// <summary>How the outside is designed: by the AI, or by hand (walls and floor on Style, pieces on Add).</summary>
    public enum GuideBranch { None, Ai, Self }

    /// <summary>The guide's step model as the panel uses it: core's <see cref="OwnSpace.Core.Onboarding"/> behind <see cref="OnboardingSteps"/>.</summary>
    public interface IGuideSteps
    {
        GuideStep Step { get; }
        /// <summary>The way chosen on the outside step (None: the two-way choice shows).</summary>
        GuideBranch Branch { get; }
        bool Active { get; }
        /// <summary>The step's number, 1-5, and how many there are (core's Onboarding.Number: the outside's choice is 4, the way chosen 5).</summary>
        int Number { get; }
        int Total { get; }
        /// <summary>Whether Next (or Finish) may be pressed: the step is done (core's Onboarding.Satisfied).</summary>
        bool CanNext { get; }
        void Choose(GuideBranch branch);
        /// <summary>Back: from a chosen way to the choice, or from the choice to My room (never before the first step the panel shows).</summary>
        bool Back();
        void Next();
        void Skip();
        event Action Changed;
    }

    /// <summary>
    /// The panel's view of core's onboarding (Lead: one step model for the guide on the panel and the ghost hand).
    /// Core's flow is Scan, SetZone, RedesignOne, ChooseBranch, then AiStyle or Outside (FillOutside, PickMood), then Done.
    /// Scan and SetZone happen before the panel opens (the zone card leads them); RedesignOne is "Inside" (My room); the
    /// choice and either way are "Outside". Next is core's Continue and is enabled only when the step is satisfied; Back,
    /// Choose and Skip are core's. A satisfied step that the user did not go back to moves on by itself (core's rule).
    /// Saving is OnboardingGuide's (onboarding.json): done or skipped stays off on later launches.
    /// </summary>
    public sealed class OnboardingSteps : IGuideSteps
    {
        readonly Onboarding _onboarding;
        readonly Func<DesignState> _design;
        readonly Func<bool> _hasRoom;

        public OnboardingSteps(Onboarding onboarding, Func<DesignState> design, Func<bool> hasRoom = null)
        {
            _onboarding = onboarding;
            _design = design;
            _hasRoom = hasRoom;
            _onboarding.StepChanged += _ => Changed?.Invoke();
        }

        /// <summary>The guide's step for core's step (pure).</summary>
        public static GuideStep StepOf(OnboardingStep step)
        {
            switch (step)
            {
                case OnboardingStep.Scan: return GuideStep.Scan;
                case OnboardingStep.SetZone: return GuideStep.Zone;
                case OnboardingStep.RedesignOne: return GuideStep.Inside;
                case OnboardingStep.Done: return GuideStep.Done;
                default: return GuideStep.Outside; // ChooseBranch, AiStyle, Outside, FillOutside, PickMood
            }
        }

        /// <summary>The way shown for core's step (pure): the AI page on AiStyle, by hand on the outside steps, else the choice.</summary>
        public static GuideBranch BranchOf(OnboardingStep step)
        {
            switch (step)
            {
                case OnboardingStep.AiStyle: return GuideBranch.Ai;
                case OnboardingStep.Outside:
                case OnboardingStep.FillOutside:
                case OnboardingStep.PickMood: return GuideBranch.Self;
                default: return GuideBranch.None;
            }
        }

        public GuideStep Step => StepOf(_onboarding.Step);
        public GuideBranch Branch => BranchOf(_onboarding.Step);
        public bool Active => _onboarding.IsActive;
        public int Number => Math.Max(1, Onboarding.Number(_onboarding.Step));
        public int Total => Onboarding.StepCount;

        public bool CanNext
        {
            get
            {
                if (!Active || _onboarding.Step == OnboardingStep.ChooseBranch) return false; // the choice is made with its two cards
                DesignState d = _design != null ? _design() : null;
                return Onboarding.Satisfied(_onboarding.Step, d, _hasRoom == null || _hasRoom(), _onboarding.Branch);
            }
        }

        public event Action Changed;

        public void Choose(GuideBranch branch)
        {
            if (branch == GuideBranch.None) return;
            _onboarding.Choose(branch == GuideBranch.Ai ? OnboardingBranch.Ai : OnboardingBranch.ByHand);
        }

        public bool Back()
        {
            // Only within the outside step and back to My room: the zone card leads the scan and the zone.
            if (Step != GuideStep.Outside || !_onboarding.CanGoBack) return false;
            _onboarding.Back();
            return true;
        }

        public void Next() { if (CanNext) _onboarding.Continue(); }

        public void Skip() => _onboarding.Skip();
    }
}
