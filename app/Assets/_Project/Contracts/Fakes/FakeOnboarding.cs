using System.Collections.Generic;

namespace OwnSpace.Contracts.Fakes
{
    /// <summary>
    /// Onboarding states and designs for building the guided panel without a headset. <see cref="At"/> starts the
    /// flow at any step. <see cref="DesignBefore"/> is a design that passes the steps before it but not that step
    /// itself (from ChooseBranch on: a zone and a redesigned desk, with no outside finish or piece).
    /// </summary>
    public static class FakeOnboarding
    {
        public static OnboardingState At(OnboardingStep step, OnboardingBranch branch = OnboardingBranch.None) =>
            new OnboardingState { step = step, branch = branch };

        public static DesignState DesignBefore(OnboardingStep step)
        {
            var d = new DesignState();
            if (step == OnboardingStep.Scan || step == OnboardingStep.SetZone) return d;
            d.zoneOutline = new List<Vec2> { new Vec2(-1.8f, 0f), new Vec2(0f, 0f), new Vec2(0f, 5f), new Vec2(-1.8f, 5f) };
            if (step == OnboardingStep.RedesignOne) return d;
            d.SetChoice(new AnchorChoice { anchorId = "a-desk", mode = AnchorMode.Redesign, catalogItemId = "desk-scandi-01", yScale = 1f, xScale = 1f, zScale = 1f });
            return d;
        }
    }
}
