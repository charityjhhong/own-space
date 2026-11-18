using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OwnSpace.Contracts;

namespace OwnSpace.Core
{
    /// <summary>A checked assistant answer, ready for <see cref="AssistantRunner.Apply"/>.</summary>
    public sealed class AssistantOutcome
    {
        internal AssistantOutcome(AssistantTask task, AssistantPlan plan) { Task = task; Plan = plan; }

        public AssistantTask Task { get; }

        /// <summary>The plan as answered (or the canned one), with <c>fromFallback</c> and <c>notice</c> set.</summary>
        public AssistantPlan Plan { get; }

        /// <summary>Outside pieces that passed the placement rules, with final positions (nudged or laid out).</summary>
        public List<FreePlacement> Placements { get; } = new List<FreePlacement>();

        /// <summary>
        /// RecommendDesign: items that fit the anchor (checked with FitRule; the result carries the turn and
        /// scales to place it), best first. Shown to the user, who applies one with DesignSession.Redesign.
        /// </summary>
        public List<(CatalogItem item, FitResult fit, string reason)> Recommendations { get; } = new List<(CatalogItem, FitResult, string)>();

        /// <summary>What was left out and why, one "id: reason" each, for a local log (see <see cref="LogLine"/>).</summary>
        public List<string> Dropped { get; } = new List<string>();

        /// <summary>Pieces that did not fit and were swapped for a smaller one of the same kind, as "big-id -> small-id".</summary>
        public List<string> Swaps { get; } = new List<string>();

        /// <summary>
        /// FillOutside: one short line for the panel (at most 60 characters) whenever pieces were skipped,
        /// e.g. "Placed 1 of 6: not enough free floor" or "Not enough free floor outside your zone";
        /// "" when everything was placed. <see cref="AssistantRunner.Apply"/> also sets it as the status message.
        /// </summary>
        public string Summary { get; internal set; } = "";

        /// <summary>FillOutside: the free floor outside my zone, in square meters.</summary>
        public float FreeFloorArea { get; internal set; }

        /// <summary>One log line with the counts, the free floor and every skip and swap with its reason.</summary>
        public string LogLine()
        {
            var line = $"Assistant plan: {Placements.Count} placed, {Dropped.Count} skipped, free floor {FreeFloorArea:0.0} m², fallback={Plan?.fromFallback == true}";
            if (Dropped.Count > 0) line += ". Skipped: " + string.Join("; ", Dropped);
            if (Swaps.Count > 0) line += ". Swapped: " + string.Join("; ", Swaps);
            return line;
        }

        /// <summary>Accepted outside finishes and mood ("" = leave as is).</summary>
        public string FloorFinishId { get; internal set; } = "";
        public string WallFinishId { get; internal set; } = "";
        public string MoodPresetId { get; internal set; } = "";
    }

    /// <summary>
    /// Runs the assistant safely. <see cref="AskAsync"/> calls it with a timeout, falls back to the
    /// nearest canned plan when the call fails or times out (and says so), and checks every id and
    /// placement with the same rules as manual edits, nudging or laying out pieces. It never touches
    /// the session. <see cref="Apply"/> (main thread) re-checks against the live design and applies
    /// everything as one undo step.
    /// </summary>
    public sealed class AssistantRunner : IAssistantStatus
    {
        public const int DefaultTimeoutMilliseconds = 8000;

        readonly IDesignAssistant _assistant;
        readonly DesignSession _session;
        readonly ICatalog _catalog;
        readonly ICollection<string> _finishIds, _moodIds;
        readonly int _timeoutMs;

        public AssistantRunner(IDesignAssistant assistant, DesignSession session, ICatalog catalog,
            ICollection<string> finishIds, ICollection<string> moodIds, int timeoutMilliseconds = DefaultTimeoutMilliseconds)
        {
            _assistant = assistant ?? throw new ArgumentNullException(nameof(assistant));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _finishIds = finishIds;
            _moodIds = moodIds;
            _timeoutMs = timeoutMilliseconds;
        }

        /// <summary>
        /// Receives one line per ask (<see cref="AssistantOutcome.LogLine"/>) and one per apply that skipped
        /// pieces. May be called off the main thread; Unity's Debug.Log is fine.
        /// </summary>
        public Action<string> Log { get; set; }

        /// <summary>
        /// The user's head (or seat) in world space, set before asking. Plan pieces keep their view open (see
        /// <see cref="ViewComfort"/>); null takes the view from the desk or bed in my zone.
        /// </summary>
        public Vec3? UserPosition { get; set; }

        public AssistantState State { get; private set; }
        public string Message { get; private set; } = "";

        /// <summary>Raised on the thread that called AskAsync/Apply (the main thread in the app).</summary>
        public event Action Changed;

        /// <summary>Call from the main thread. Throws <see cref="OperationCanceledException"/> only if the user cancels.</summary>
        public Task<AssistantOutcome> AskAsync(AssistantTask task, string intent, string anchorId, CancellationToken cancellationToken) =>
            AskCoreAsync(task, intent, anchorId, CannedPlans.Nearest(intent), false, cancellationToken);

        /// <summary>
        /// An intent chip (or "Surprise me" via <see cref="CannedPlans.Surprise"/>): asks the live assistant
        /// for the preset's intent and, if it fails, shows that same preset. Unknown ids use the first preset.
        /// </summary>
        public Task<AssistantOutcome> AskPresetAsync(string presetId, CancellationToken cancellationToken)
        {
            var preset = CannedPlans.ById(presetId) ?? CannedPlans.Presets[0];
            return AskCoreAsync(AssistantTask.FillOutside, preset.Intent, null, preset, true, cancellationToken);
        }

        async Task<AssistantOutcome> AskCoreAsync(AssistantTask task, string intent, string anchorId,
            CannedPlans.Preset fallback, bool isPreset, CancellationToken cancellationToken)
        {
            var origin = SynchronizationContext.Current;
            SetStatus(AssistantState.Asking, "Thinking…");
            try
            {
                var outcome = await AskOnceAsync(task, intent, anchorId, fallback, isPreset, cancellationToken).ConfigureAwait(false);
                Log?.Invoke(outcome.LogLine());
                Report(origin, outcome.Plan.fromFallback ? AssistantState.Fallback : AssistantState.Idle,
                    outcome.Plan.fromFallback ? outcome.Plan.notice : outcome.Plan.rationale);
                return outcome;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Report(origin, AssistantState.Idle, "");
                throw;
            }
            catch (Exception)
            {
                Report(origin, AssistantState.Failed, "Something went wrong. Try again.");
                throw;
            }
        }

        async Task<AssistantOutcome> AskOnceAsync(AssistantTask task, string intent, string anchorId,
            CannedPlans.Preset fallback, bool isPreset, CancellationToken cancellationToken)
        {
            var snapshot = _session.State.Clone();
            // A new outside plan replaces the last one: its pieces are already gone for the free-floor check.
            if (task == AssistantTask.FillOutside) snapshot.placements.RemoveAll(IsAiPlaced);
            // A preset asks only for its curated set (see PresetSets); free text may use the whole catalog.
            var set = PresetSets.Items(_catalog, fallback);
            IReadOnlyList<CatalogItem> allowed = isPreset && set.Count > 0 ? set : _catalog.Items;
            var request = new AssistantRequest(task, intent, _session.Room, snapshot, allowed, anchorId);
            AssistantPlan plan = null;
            string failure = null;
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                // Race the call against a timer: CancelAfter alone would wait forever on an
                // assistant that ignores its token.
                Task<AssistantPlan> call;
                try { call = _assistant.RequestAsync(request, linked.Token); }
                catch (Exception e) { call = Task.FromException<AssistantPlan>(e); }
                var winner = await Task.WhenAny(call, Task.Delay(_timeoutMs, timer.Token)).ConfigureAwait(false);
                timer.Cancel(); // stop the timer when the call wins
                cancellationToken.ThrowIfCancellationRequested();
                if (winner != call)
                {
                    linked.Cancel();
                    _ = call.ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted); // observe a late failure
                    failure = "Took too long";
                }
                else
                {
                    try { plan = await call.ConfigureAwait(false) ?? throw new FormatException("empty plan"); }
                    catch (Exception) when (!cancellationToken.IsCancellationRequested) { failure = "You're offline"; }
                }
            }
            if (failure != null)
            {
                plan = PresetSets.PlanFor(fallback, set);
                plan.fromFallback = true;
                plan.notice = Notice(failure, fallback); // short, for the panel (≤ 60 chars)
            }
            cancellationToken.ThrowIfCancellationRequested();

            var outcome = PlanChecker.Check(plan, request, _catalog, _finishIds, _moodIds, UserPosition);
            if (isPreset) PresetSets.ApplyLook(outcome, fallback, _finishIds, _moodIds); // the set's look comes with its pieces
            if (task == AssistantTask.RecommendDesign && outcome.Recommendations.Count == 0)
                PlanChecker.RecommendFitting(outcome, request, _catalog); // nothing usable from the model: offer what fits
            return outcome;
        }

        /// <summary>
        /// Main thread, FillOutside only (recommendations are applied by the user through
        /// <see cref="DesignSession.Redesign"/>). Removes the pieces the previous AI plan placed, adds this plan's
        /// pieces (marked <see cref="FreePlacement.aiPlaced"/>) next to the user's own outside pieces (never
        /// removes those) and sets the outside finishes and mood, as one undo step. Each piece
        /// is checked again against the live design; one that no longer fits is skipped and, if given,
        /// listed in <paramref name="skipped"/>. The outcome is not changed. One Undo removes all of it.
        /// </summary>
        public bool Apply(AssistantOutcome outcome, List<string> skipped = null)
        {
            if (outcome == null || outcome.Task != AssistantTask.FillOutside) return false;
            var room = _session.Room;
            int added = 0, replaced = 0;
            var late = new List<string>();
            bool applied = _session.CommitBatch(s =>
            {
                replaced = s.placements.RemoveAll(IsAiPlaced);
                foreach (var placement in outcome.Placements)
                {
                    var p = placement;
                    p.aiPlaced = true;
                    bool idFree = !s.placements.Exists(q => q.placementId == p.placementId);
                    if (idFree && PlacementRules.Check(room, s, _catalog, p) == PlacementStatus.Ok) { s.placements.Add(p); added++; }
                    else late.Add(p.catalogItemId + ": " + (idFree ? PlacementRules.Check(room, s, _catalog, p).ToString() : "DuplicateId"));
                }
                var f = s.outsideFinish;
                if (outcome.FloorFinishId.Length > 0) f.floorFinishId = outcome.FloorFinishId;
                if (outcome.WallFinishId.Length > 0) f.wallFinishId = outcome.WallFinishId;
                s.outsideFinish = f;
                if (outcome.MoodPresetId.Length > 0) s.moodPresetId = outcome.MoodPresetId;
                return true;
            });
            skipped?.AddRange(late);
            if (replaced > 0) Log?.Invoke($"Assistant apply: removed {replaced} pieces from the last AI plan");
            if (late.Count > 0) Log?.Invoke($"Assistant apply: {added} added, {late.Count} no longer fit: " + string.Join("; ", late));
            // Whenever pieces were skipped the panel gets the reason, e.g. "Placed 1 of 6: not enough free floor".
            SetStatus(AssistantState.Applied, outcome.Summary.Length > 0 && late.Count == 0 ? outcome.Summary
                : added == 1 ? "Added 1 piece"
                : added > 1 ? $"Added {added} pieces"
                : outcome.Summary.Length > 0 ? outcome.Summary
                : late.Count > 0 ? "Nothing fit on the free floor" : "Added 0 pieces");
            return applied;
        }

        static readonly Predicate<FreePlacement> IsAiPlaced = p => p.aiPlaced;

        /// <summary>The fallback line for the panel, e.g. "You're offline — here's “Study café”".</summary>
        public static string Notice(string failure, CannedPlans.Preset preset) => $"{failure} — here's “{preset.Label}”";

        void SetStatus(AssistantState state, string message)
        {
            State = state;
            Message = message ?? "";
            Changed?.Invoke();
        }

        // Back on the caller's context (the main thread in the app); directly when there is none.
        void Report(SynchronizationContext origin, AssistantState state, string message)
        {
            if (origin == null || origin == SynchronizationContext.Current) SetStatus(state, message);
            else origin.Post(_ => SetStatus(state, message), null);
        }
    }
}
