using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OwnSpace.Content;
using OwnSpace.Contracts;
using OwnSpace.Contracts.Fakes;
using OwnSpace.Core;
using OwnSpace.Interaction;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// The design assistant, hands only (CONCEPT 5.7). A small "Ask" orb sits in the tool tray, between
    /// the style orb and the hide cube, and follows the user like them. Pinching it opens four preset
    /// intent chips. Pinching a chip asks core's <see cref="AssistantRunner"/> to fill the outside
    /// zone; the orb pulses while it waits.
    /// The plan is applied as one undoable step. Its rationale, any fallback notice and the skipped
    /// pieces show as a short label. Pinching the orb while it waits, losing both hands for a second,
    /// pausing or re-editing the zone cancels the request, and nothing is applied.
    /// The assistant is <see cref="FakeDesignAssistant"/> until <see cref="Assistant"/> is set to the
    /// live one. The task is polled in Update, so there is no allocation per frame (only on ask and on answer).
    /// </summary>
    [DefaultExecutionOrder(7)]
    public sealed class AskMenu : MonoBehaviour, IAiDesigner
    {
        const string LogTag = "[OwnSpace.MR]";
        // The last chip opens the free-form request (typed or dictated) instead of a preset.
        static readonly string[] Intents = { "Cozy living room", "Study café", "Calm bedroom", "Bright studio", FreeTextRequest.Placeholder };
        const int Chips = 5, FreeText = Chips - 1, Orb = Chips, Targets = Chips + 1;
        const float LostHandsGrace = 1f, ResultSeconds = 8f;
        static readonly Color OrbColor = new Color(0.35f, 0.8f, 0.85f);

        [SerializeField, Tooltip("Driven by the tool panel: no orb, chips or tiles in the room (the logic and world input stay).")] bool _panelMode;
        [SerializeField] DesignStore _store;
        [SerializeField] MrukRoomInfo _room;
        [SerializeField] HandRig _hands;
        [SerializeField] ZoneSetup _zone;
        [SerializeField] Material _material;
        [SerializeField] FinishSet _finishes;
        [SerializeField] MoodPresets _moods;
        [SerializeField, Tooltip("The free-form request (system keyboard with dictation); optional.")] FreeTextRequest _freeText;
        [SerializeField, Tooltip("Direct pinch reach (m).")] float _nearRadius = 0.06f;
        [SerializeField, Tooltip("Delay of the stand-in assistant (ms), to show the busy state.")] int _fakeDelayMs = 1500;

        readonly Renderer[] _targets = new Renderer[Targets];
        readonly WorldLabel[] _chipLabels = new WorldLabel[Chips];
        readonly ToolFollow _follow = new ToolFollow(0f);
        readonly List<string> _skipped = new List<string>();
        WorldLabel _result;
        AssistantRunner _runner;
        DesignSession _runnerSession;
        CancellationTokenSource _cts;
        Task<AssistantOutcome> _pending;
        bool _open;
        float _handsLostFor;

        /// <summary>The assistant to ask. Set it before the first request to use the live one.</summary>
        public IDesignAssistant Assistant { get; set; }

        public bool Busy => _pending != null;

        // ---- the panel's AI Designer tab binds to these ----

        /// <summary>Raised when a request starts, ends, fails, is cancelled or its plan is undone.</summary>
        public event Action Changed;

        /// <summary>Core's status for the current runner (null before the first request).</summary>
        public IAssistantStatus Status => _runner;
        public string StatusMessage => _runner?.Message;

        /// <summary>The last request (the user's words or the preset's name), and its result.</summary>
        public string LastRequest { get; private set; }
        public string LastRationale { get; private set; }
        public int LastPlaced { get; private set; }
        public int LastSkipped { get; private set; }
        public bool LastFromFallback { get; private set; }
        public bool LastFailed { get; private set; }
        public bool HasPlan { get; private set; }
        int _planChange = -1;

        /// <summary>True while the AI's plan is still the last undoable change, so one Undo removes exactly it.</summary>
        public bool CanUndoPlan => HasPlan && _store != null && _store.UserChangeCount == _planChange;

        /// <summary>Forgets the last plan (Reset my design): the AI Designer tab goes back to its first state.</summary>
        public void ForgetPlan()
        {
            HasPlan = LastFailed = false;
            LastRequest = LastRationale = null;
            LastPlaced = LastSkipped = 0;
            _planChange = -1;
            Changed?.Invoke();
        }

        public bool UndoPlan()
        {
            if (!CanUndoPlan) return false;
            bool undone = _store.Undo();
            HasPlan = false;
            Changed?.Invoke();
            return undone;
        }

        /// <summary>Asks with preset <paramref name="index"/> (0-3); <paramref name="label"/> is what the panel showed.</summary>
        public bool AskPreset(int index, string label)
        {
            if (index < 0 || index >= FreeText || Busy || _targets[Orb] == null || !_targets[Orb].gameObject.activeSelf) return false;
            LastRequest = label;
            Ask(index);
            return true;
        }

        public void CancelRequest() => Cancel("Cancelled");

        void Start()
        {
            for (int i = 0; i < Targets; i++)
            {
                _targets[i] = XrVisuals.Make(PrimitiveType.Sphere, transform, _material, i == Orb ? OrbColor : XrVisuals.Idle, "AskTarget");
                _targets[i].transform.localScale = Vector3.one * (i == Orb ? 0.055f : 0.045f);
                _targets[i].gameObject.SetActive(false);
                _targets[i].enabled = !_panelMode;
                if (i < Chips) _chipLabels[i] = new WorldLabel(transform, "AskChip", Color.white);
            }
            _result = new WorldLabel(transform, "AskResult", Color.white);
            _zone.Confirmed += ShowOrb;
            _zone.EditStarted += HideAll;
            if (_zone.Current == ZoneSetup.Phase.Confirmed) ShowOrb();
        }

        void OnDestroy()
        {
            if (_zone != null) { _zone.Confirmed -= ShowOrb; _zone.EditStarted -= HideAll; }
            Cancel(null);
        }

        void OnDisable() => Cancel(null);

        void OnApplicationFocus(bool focused) { if (!focused) Cancel("Cancelled"); }

        void OnApplicationPause(bool paused) { if (paused) Cancel("Cancelled"); }

        void ShowOrb()
        {
            _follow.Snap(_targets[Orb].transform, _hands.View, _room != null ? _room.FloorOutline : null);
            _targets[Orb].gameObject.SetActive(true);
            SetOpen(false);
        }

        void HideAll()
        {
            Cancel(null);
            SetOpen(false);
            _targets[Orb].gameObject.SetActive(false);
            _result.Hide();
        }

        void SetOpen(bool open)
        {
            _open = open;
            Transform orb = _targets[Orb].transform;
            Vector3 forward = Vector3.ProjectOnPlane(orb.position - _hands.HeadPosition, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            for (int i = 0; i < Chips; i++)
            {
                _targets[i].gameObject.SetActive(open);
                if (!open) { _chipLabels[i].Hide(); continue; }
                Vector3 p = orb.position + Vector3.up * 0.12f + right * ((i - (Chips - 1) * 0.5f) * 0.11f);
                _targets[i].transform.position = p;
                _chipLabels[i].Show(Intents[i], p + Vector3.up * 0.06f, _hands.HeadPosition);
            }
        }

        void Update()
        {
            _result.Tick();
            if (!_targets[Orb].gameObject.activeSelf) return;
            Transform orb = _targets[Orb].transform;
            _follow.Tick(orb, _hands.View, _room != null ? _room.FloorOutline : null, !_open && !Busy, Time.deltaTime);

            if (Busy)
            {
                // Hands-only feedback: the orb pulses while the assistant thinks.
                orb.localScale = Vector3.one * (0.055f * (1f + 0.18f * Mathf.Sin(Time.time * 8f)));
                _handsLostFor = _hands.AnyHandTracked ? 0f : _handsLostFor + Time.deltaTime;
                if (_handsLostFor > LostHandsGrace) { Cancel("Hands lost: cancelled"); return; }
                if (_pending.IsCompleted) { Finish(); return; }
            }

            if (_panelMode) return; // the AI Designer tab asks; no orb or chips here
            HandState left = _hands.Left, right = _hands.Right;
            if ((!right.PinchStarted && !left.PinchStarted) || _hands.ClaimedThisFrame) return;
            int target = Pick(right.PinchStarted ? right.PinchPoint : left.PinchPoint);
            if (target < 0) return;
            _hands.Claim();
            if (target == Orb)
            {
                if (Busy) Cancel("Cancelled");
                else SetOpen(!_open);
                return;
            }
            if (Busy) return;
            if (target != FreeText) { Ask(target); return; }
            SetOpen(false);
            if (_freeText == null || !_freeText.Open()) Say("Typing isn't available here");
        }

        int Pick(Vector3 pinch)
        {
            int best = -1;
            float bestMiss = float.MaxValue;
            for (int i = 0; i < Targets; i++)
            {
                if (!_targets[i].gameObject.activeSelf) continue;
                Vector3 c = _targets[i].transform.position;
                if (Vector3.Distance(pinch, c) < _nearRadius) return i;
                float miss = _hands.GazeHit(c);
                if (miss >= 0f && miss < bestMiss) { bestMiss = miss; best = i; }
            }
            return best;
        }

        /// <summary>Longest free-text request sent (characters); longer ones are clipped.</summary>
        public const int MaxRequestLength = 200;

        /// <summary>
        /// Asks with the user's own words (typed or dictated): same busy, fallback, cancel and one-step undo
        /// flow as the chips. False when it is empty after cleaning, a request is running, or the zone is not set.
        /// </summary>
        public bool AskText(string text)
        {
            string request = CleanRequest(text);
            if (request.Length == 0 || Busy || _targets[Orb] == null || !_targets[Orb].gameObject.activeSelf) return false;
            LastRequest = request;
            Ask(request, logText: false);
            return true;
        }

        /// <summary>Trims, turns control characters and runs of whitespace into single spaces, clips to <see cref="MaxRequestLength"/>.</summary>
        public static string CleanRequest(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var clean = new StringBuilder(Mathf.Min(text.Length, MaxRequestLength));
            bool space = false;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c)) { space = clean.Length > 0; continue; }
                if (space && clean.Length < MaxRequestLength) clean.Append(' ');
                space = false;
                if (clean.Length >= MaxRequestLength) break;
                clean.Append(c);
            }
            return clean.ToString();
        }

        void Ask(int chip) => Ask(Intents[chip], logText: true);

        void Ask(string intent, bool logText)
        {
            SetOpen(false);
            _result.Hide();
            // A new session (e.g. after a design load) needs a new runner: the runner holds the session.
            if (_runner == null || _runnerSession != _store.Session)
            {
                _runnerSession = _store.Session;
                _runner = new AssistantRunner(Assistant ?? new FakeDesignAssistant(_fakeDelayMs),
                    _runnerSession, _runnerSession.Catalog, FinishIds(), MoodIds());
            }
            _cts = new CancellationTokenSource();
            _handsLostFor = 0f;
            XrVisuals.Tint(_targets[Orb], XrVisuals.Active);
            // The user's own words stay out of the log (only their length).
            Debug.Log(logText ? $"{LogTag} Asking the assistant: \"{intent}\"." : $"{LogTag} Asking the assistant ({intent.Length} characters of the user's words).");
            _pending = _runner.AskAsync(AssistantTask.FillOutside, intent, null, _cts.Token);
            LastFailed = false;
            Changed?.Invoke();
        }

        void Finish()
        {
            Task<AssistantOutcome> done = _pending;
            EndRequest();
            if (done.IsCanceled) return;
            if (done.IsFaulted)
            {
                Debug.LogWarning($"{LogTag} Assistant failed: {done.Exception?.GetBaseException().Message}");
                Say("Couldn't ask the assistant");
                LastFailed = true;
                Changed?.Invoke();
                return;
            }
            AssistantOutcome outcome = done.Result;
            _skipped.Clear();
            bool applied = _runner.Apply(outcome, _skipped);
            if (applied) _store.NoteUserChange(); // one Undo removes all of it
            HasPlan = applied;
            _planChange = _store.UserChangeCount;
            LastRationale = outcome.Plan.rationale;
            LastFromFallback = outcome.Plan.fromFallback;
            LastPlaced = outcome.Placements.Count - _skipped.Count;
            LastSkipped = outcome.Dropped.Count + _skipped.Count;
            Changed?.Invoke();
            Debug.Log($"{LogTag} Assistant plan applied: {outcome.Placements.Count - _skipped.Count} piece(s), " +
                      $"{outcome.Dropped.Count + _skipped.Count} skipped, fallback={outcome.Plan.fromFallback}.");
            Say(Summary(outcome, _skipped, _store.Session.Catalog));
            LogPieces(outcome, _skipped, _store.Session.Catalog, _room);
        }

        // Development builds: one line per planned piece (device 2dc475d: "AI puts the curtains in a strange place"). Says
        // whether core hangs it on a wall, and the nearest window, so a curtain's placement can be checked against it.
        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        static void LogPieces(AssistantOutcome outcome, List<string> skipped, ICatalog catalog, MrukRoomInfo room)
        {
            for (int i = 0; i < outcome.Placements.Count; i++)
            {
                FreePlacement p = outcome.Placements[i];
                CatalogItem item = null;
                catalog?.TryGet(p.catalogItemId, out item);
                string mount = item == null ? "unknown item" : PlacementRules.IsHanging(item) ? "ceiling" : PlacementRules.IsWallMounted(item) ? "wall" : "floor (free)";
                string window = "no window";
                float best = float.MaxValue;
                for (int w = 0; room != null && w < room.Windows.Count; w++)
                {
                    OrientedBox b = room.Windows[w].Box;
                    float d = Mathf.Sqrt((b.center.x - p.position.x) * (b.center.x - p.position.x) + (b.center.z - p.position.z) * (b.center.z - p.position.z));
                    if (d < best) { best = d; window = $"window {w} at ({b.center.x:F2}, {b.center.y:F2}, {b.center.z:F2}) size {b.size.x:F2} x {b.size.y:F2}, {d:F2} m away"; }
                }
                Debug.Log($"{LogTag} Plan piece {i}: {p.catalogItemId} ({item?.kind ?? "?"}) at ({p.position.x:F2}, {p.position.y:F2}, {p.position.z:F2}) yaw {p.yawDegrees:0}, " +
                          $"mount {mount}{(skipped.Contains(p.placementId) ? ", skipped" : "")}; nearest {window}.");
            }
        }

        void Cancel(string message)
        {
            if (_pending == null) return;
            _cts?.Cancel();
            EndRequest();
            Debug.Log($"{LogTag} Assistant request cancelled.");
            if (message != null) Say(message);
            Changed?.Invoke();
        }

        void EndRequest()
        {
            _pending = null;
            _cts?.Dispose();
            _cts = null;
            if (_targets[Orb] == null) return;
            _targets[Orb].transform.localScale = Vector3.one * 0.055f;
            XrVisuals.Tint(_targets[Orb], OrbColor);
        }

        void Say(string text)
        {
            if (_targets[Orb] == null || _panelMode) return; // the panel shows results
            _result.Show(text, _targets[Orb].transform.position + Vector3.up * 0.22f, _hands.HeadPosition, ResultSeconds);
        }

        /// <summary>Rationale, fallback notice and up to three skipped pieces, wrapped for a world label.</summary>
        public static string Summary(AssistantOutcome outcome, IReadOnlyList<string> skipped, ICatalog catalog)
        {
            var text = new StringBuilder();
            AppendWrapped(text, outcome.Plan.rationale);
            if (!string.IsNullOrEmpty(outcome.Plan.notice)) AppendWrapped(text, outcome.Plan.notice);
            int count = outcome.Dropped.Count + skipped.Count;
            if (count > 0)
            {
                if (text.Length > 0) text.Append('\n');
                text.Append("Skipped: ");
                for (int i = 0, shown = 0; i < count && shown < 3; i++, shown++)
                {
                    string entry = i < outcome.Dropped.Count ? outcome.Dropped[i] : skipped[i - outcome.Dropped.Count];
                    if (shown > 0) text.Append(", ");
                    text.Append(NameOf(entry, catalog));
                }
                if (count > 3) text.Append(" +").Append(count - 3);
            }
            return text.Length > 0 ? text.ToString() : "Done";
        }

        // "id: reason" -> the catalog name of id (or the id).
        static string NameOf(string entry, ICatalog catalog)
        {
            int colon = entry.IndexOf(':');
            string id = (colon >= 0 ? entry.Substring(0, colon) : entry).Trim();
            return catalog != null && catalog.TryGet(id, out CatalogItem item) && !string.IsNullOrEmpty(item.name) ? item.name : id;
        }

        /// <summary>Appends <paramref name="s"/> on a new line, broken at spaces near 38 characters.</summary>
        public static void AppendWrapped(StringBuilder text, string s, int width = 38)
        {
            if (string.IsNullOrEmpty(s)) return;
            if (text.Length > 0) text.Append('\n');
            int line = 0;
            foreach (string word in s.Split(' '))
            {
                if (word.Length == 0) continue;
                if (line > 0 && line + 1 + word.Length > width) { text.Append('\n'); line = 0; }
                else if (line > 0) { text.Append(' '); line++; }
                text.Append(word);
                line += word.Length;
            }
        }

        List<string> FinishIds()
        {
            var ids = new List<string>();
            for (int i = 0; _finishes != null && i < _finishes.Entries.Count; i++) ids.Add(_finishes.Entries[i].id);
            return ids;
        }

        List<string> MoodIds()
        {
            var ids = new List<string>();
            for (int i = 0; _moods != null && i < _moods.Entries.Count; i++) ids.Add(_moods.Entries[i].id);
            return ids;
        }
    }
}
