using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OwnSpace.Contracts
{
    /// <summary>What the user asked the assistant for (CONCEPT 5.7).</summary>
    public enum AssistantTask
    {
        /// <summary>"Make it a cozy living room": furnish the outside zone in one step.</summary>
        FillOutside = 0,

        /// <summary>Suggest designs for one real anchor in my zone.</summary>
        RecommendDesign = 1,
    }

    /// <summary>One request. Core turns it into the compact JSON the proxy accepts; nothing else leaves the device.</summary>
    public sealed class AssistantRequest
    {
        public AssistantRequest(AssistantTask task, string intent, IRoomInfo room, DesignState design,
            IReadOnlyList<CatalogItem> allowedItems, string anchorId = null)
        {
            Task = task;
            Intent = intent ?? "";
            Room = room ?? throw new ArgumentNullException(nameof(room));
            Design = design ?? throw new ArgumentNullException(nameof(design));
            AllowedItems = allowedItems ?? Array.Empty<CatalogItem>();
            AnchorId = anchorId;
        }

        public AssistantTask Task { get; }

        /// <summary>The user's words, e.g. "make it a cozy living room".</summary>
        public string Intent { get; }
        public IRoomInfo Room { get; }
        public DesignState Design { get; }

        /// <summary>The only items the assistant may choose (ids are sent, never invented).</summary>
        public IReadOnlyList<CatalogItem> AllowedItems { get; }

        /// <summary>The anchor to recommend designs for (RecommendDesign only).</summary>
        public string AnchorId { get; }
    }

    /// <summary>
    /// A piece the assistant wants in the outside zone. No position = core finds a free spot.
    /// Not [Serializable]: the nullable floats are not JsonUtility-safe; <c>PlanParser</c> is its only reader.
    /// </summary>
    public struct PlanPlacement
    {
        public string catalogItemId;

        /// <summary>Floor position (world x, z) of the item's center; null lets core choose.</summary>
        public float? x;
        public float? z;
        public float yawDegrees;
    }

    [Serializable]
    public struct PlanRecommendation
    {
        public string anchorId;
        public string catalogItemId;
        public string reason;
    }

    /// <summary>
    /// The assistant's answer. Nothing in it is applied unchecked: core validates every id and
    /// placement with the same rules as manual edits. Empty finish or mood = leave as is.
    /// </summary>
    [Serializable]
    public sealed class AssistantPlan
    {
        public List<PlanPlacement> placements = new List<PlanPlacement>();

        /// <summary>Outside-zone finishes.</summary>
        public string floorFinishId = "";
        public string wallFinishId = "";
        public string moodPresetId = "";
        public List<PlanRecommendation> recommendations = new List<PlanRecommendation>();

        /// <summary>One or two sentences shown to the user.</summary>
        public string rationale = "";

        /// <summary>Set by core, not the model: this is a canned plan because the live call failed.</summary>
        public bool fromFallback;

        /// <summary>Set by core: what to tell the user about this plan (e.g. "offline: used a preset").</summary>
        public string notice = "";
    }

    /// <summary>
    /// The design assistant. Implementations may be slow or fail: they throw on network or format
    /// errors and should honor the cancellation token; core adds the timeout and the offline fallback.
    /// Threading: callers start <see cref="RequestAsync"/> on Unity's main thread (it may start a
    /// UnityWebRequest there). After its first await an implementation must not touch Unity APIs and
    /// should use ConfigureAwait(false); the caller resumes on its own context.
    /// </summary>
    public interface IDesignAssistant
    {
        Task<AssistantPlan> RequestAsync(AssistantRequest request, CancellationToken cancellationToken);
    }
}
