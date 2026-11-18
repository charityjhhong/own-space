using System;
using System.Threading;
using System.Threading.Tasks;

namespace OwnSpace.Contracts.Fakes
{
    /// <summary>
    /// Answers from <see cref="CannedPlans"/> with no network, after an optional delay, so the
    /// request UI can be built and tested. <see cref="FailNext"/> simulates a failed call.
    /// </summary>
    public sealed class FakeDesignAssistant : IDesignAssistant
    {
        public FakeDesignAssistant(int delayMilliseconds = 0)
        {
            DelayMilliseconds = delayMilliseconds;
        }

        public int DelayMilliseconds { get; set; }

        /// <summary>When true, the next request throws (then it resets).</summary>
        public bool FailNext { get; set; }

        public int Calls { get; private set; }

        public async Task<AssistantPlan> RequestAsync(AssistantRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            // ConfigureAwait(false): never resume on Unity's main-thread context, so a caller that
            // blocks on the task (tests, or a sync wrapper) cannot deadlock.
            if (DelayMilliseconds > 0) await Task.Delay(DelayMilliseconds, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("FakeDesignAssistant: simulated failure");
            }

            var plan = CannedPlans.PlanFor(request.Intent);
            if (request.Task == AssistantTask.RecommendDesign)
            {
                // Recommend the first allowed item for the anchor; the caller filters by fit.
                plan.placements.Clear();
                plan.floorFinishId = plan.wallFinishId = "";
                foreach (var item in request.AllowedItems)
                {
                    plan.recommendations.Add(new PlanRecommendation
                    {
                        anchorId = request.AnchorId, catalogItemId = item.id, reason = "Fits your " + plan.moodPresetId + " mood.",
                    });
                    break;
                }
            }
            return plan;
        }
    }
}
