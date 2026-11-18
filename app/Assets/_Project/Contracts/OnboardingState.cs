using System;

namespace OwnSpace.Contracts
{
    /// <summary>
    /// First-run steps. Values are stable; append only. The guided flow is
    /// <see cref="Scan"/> → <see cref="SetZone"/> → <see cref="RedesignOne"/> → <see cref="ChooseBranch"/> →
    /// <see cref="AiStyle"/> (branch Ai) or <see cref="Outside"/> (branch ByHand) → <see cref="Done"/>, then all tabs.
    /// Saved as names, not numbers (<c>OwnSpace.Core.OnboardingJson</c>).
    /// </summary>
    public enum OnboardingStep
    {
        /// <summary>My zone: confirm the area you use.</summary>
        SetZone = 0,
        /// <summary>Redesign one real piece in my zone (My room tab).</summary>
        RedesignOne = 1,
        /// <summary>Legacy (version 1 saves only); loads as <see cref="ChooseBranch"/>.</summary>
        FillOutside = 2,
        /// <summary>Legacy (version 1 saves only); loads as <see cref="ChooseBranch"/>.</summary>
        PickMood = 3,
        Done = 4,
        /// <summary>A room scan, or the sample room, is needed first.</summary>
        Scan = 5,
        /// <summary>Pick how to do the outside: "AI designs the outside" or "I'll do it".</summary>
        ChooseBranch = 6,
        /// <summary>Branch Ai: apply an AI style to the outside (AI tab). Ends the guide.</summary>
        AiStyle = 7,
        /// <summary>Branch ByHand: outside walls and floor, or outside furniture (Style and Add tabs). Ends the guide.</summary>
        Outside = 8,
    }

    /// <summary>The path picked at <see cref="OnboardingStep.ChooseBranch"/>. Values are stable; append only.</summary>
    public enum OnboardingBranch
    {
        None = 0,
        /// <summary>"AI designs the outside."</summary>
        Ai = 1,
        /// <summary>"I'll do it": Style and Add by hand.</summary>
        ByHand = 2,
    }

    /// <summary>Panel tabs, as flags so a step can show several.</summary>
    [Flags]
    public enum PanelTabs
    {
        None = 0,
        Style = 1,
        MyRoom = 2,
        Add = 4,
        Ai = 8,
        TidyUp = 16,
        All = Style | MyRoom | Add | Ai | TidyUp,
    }

    /// <summary>What the panel shows during a step: a single-purpose page, or tabs (<c>Onboarding.TabsFor</c>).</summary>
    public enum OnboardingPage
    {
        None = 0,
        ScanPrompt = 1,
        ZoneSetup = 2,
        BranchChoice = 3,
        Tabs = 4,
    }

    /// <summary>
    /// Saved onboarding progress, in its own file next to the design save. Plain fields so it
    /// serializes as-is (<c>OwnSpace.Core.OnboardingJson</c>). Local only: nothing is sent anywhere.
    /// </summary>
    [Serializable]
    public sealed class OnboardingState
    {
        /// <summary>2: the guided flow (Scan, ChooseBranch, AiStyle, Outside) and <see cref="branch"/>.</summary>
        public const int CurrentVersion = 2;

        public int version = CurrentVersion;
        public OnboardingStep step = OnboardingStep.Scan;
        public OnboardingBranch branch = OnboardingBranch.None;
        public bool skipped;

        /// <summary>Seconds spent in onboarding while the app was running and not paused.</summary>
        public float activeSeconds;

        /// <summary><see cref="activeSeconds"/> when the first piece was redesigned or AI-placed; -1 until then.</summary>
        public float firstPayoffSeconds = -1f;
    }
}
