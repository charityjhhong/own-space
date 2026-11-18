using System;
using System.IO;
using OwnSpace.Contracts;
using OwnSpace.Core;
using OwnSpace.Interaction;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// First-run guidance (CONCEPT 6, P4) on top of core's <see cref="Onboarding"/>, which watches the
    /// design and decides the step. Here: a ghost hand that pinches in a slow loop at the thing to
    /// pinch next, with a two-to-four-word label, kept inside the field of view (clamped toward the
    /// target when it is off to the side): set zone → nothing here (ZoneSetup labels its Done button); redesign one → the nearest desk/bed
    /// in my zone (first payoff, aimed at under a minute); fill outside → the outside floor ahead;
    /// pick a mood → the style orb. A small grey sphere beside the hint skips onboarding.
    /// Progress saves to persistentDataPath/onboarding.json on every step and at the end; pause and
    /// focus loss stop core's clock. Per frame: transform math only, no allocation.
    /// </summary>
    [DefaultExecutionOrder(4)]
    public sealed class OnboardingGuide : MonoBehaviour
    {
        const string LogTag = "[OwnSpace.MR]";
        const float MaxAngle = 25f; // degrees from the view centre where hints stay
        const float FloorAngle = 60f; // the outside-floor hint stands on the floor within this angle

        [SerializeField] MrukRoomInfo _room;
        [SerializeField] DesignStore _store;
        [SerializeField] HandRig _hands;
        [SerializeField] Material _material;
        [SerializeField, Tooltip("Save file name under Application.persistentDataPath.")] string _fileName = "onboarding.json";
        [SerializeField, Tooltip("Direct pinch reach for the skip sphere (m).")] float _nearRadius = 0.06f;

        /// <summary>
        /// The guided first run's target on the panel (the card to pinch next), or null. PanelHost sets it. While it gives a
        /// point, the ghost hand points there with "Pinch here" instead of core's onboarding step.
        /// </summary>
        public static Func<Vector3?> GuideTarget;

        /// <summary>
        /// The guided first run's one switch (the human: "no tutorial"). Off: core's onboarding starts skipped and is
        /// never loaded or saved, so the panel opens with all five tabs from the first launch (no step header, Back, Skip or
        /// Next, no tab switching) and no ghost hand or step label shows. The scan prompt and the zone setup are not part of
        /// it: they run whenever there is no room or zone. Kept as code so turning it back on is this one line.
        /// </summary>
        public static readonly bool GuideEnabled = false;
        public const string GuideLabel = "Pinch here";
        bool _guiding;

        Onboarding _onboarding;
        string _path;

        /// <summary>Core's onboarding, loaded and saved here (the guide on the panel reads and drives it: OnboardingSteps).</summary>
        public Onboarding Onboarding => _onboarding;
        Transform _hint, _thumb, _index, _skip;
        GlassChip _label; // the step's words in the panel's look (null without a panel)
        string _labelText = "";

        void Awake()
        {
            _path = Path.Combine(Application.persistentDataPath, _fileName);
            if (!GuideEnabled)
            {
                // Skipped before anything listens: nothing is saved, so onboarding.json stays as it was.
                _onboarding = new Onboarding();
                _onboarding.Skip();
                Debug.Log($"{LogTag} Guided first run off: all five tabs from the start.");
                return;
            }
            OnboardingJson.TryLoadFromFile(_path, out OnboardingState state);
            _onboarding = new Onboarding(state);
            _onboarding.StepChanged += OnStep;
            _onboarding.Ended += OnEnded;
        }

        void Start()
        {
            BuildVisuals();
            _store.Session.Changed += Observe;
            Observe();
            SetLabel();
        }

        void OnDestroy()
        {
            if (_store != null && _store.Session != null) _store.Session.Changed -= Observe;
        }

        void OnApplicationPause(bool paused) { if (paused) _onboarding.Pause(); else _onboarding.Resume(); }
        void OnApplicationFocus(bool focused) { if (focused) _onboarding.Resume(); else _onboarding.Pause(); }

        void Observe() => _onboarding.Observe(_store.Session.State);

        void OnStep(OnboardingStep step)
        {
            Debug.Log($"{LogTag} Onboarding step: {step} at {_onboarding.State.activeSeconds:0.0}s");
            Save();
            SetLabel();
        }

        void OnEnded()
        {
            Debug.Log($"{LogTag} {_onboarding.Summary()}");
            Save();
            _hint.gameObject.SetActive(false);
        }

        void Save()
        {
            try { OnboardingJson.SaveToFile(_path, _onboarding.State); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning($"{LogTag} Onboarding not saved: {e.Message}");
            }
        }

        void Update()
        {
            _onboarding.Tick(Time.unscaledDeltaTime);
            Vector3 target = default;
            Vector3? guide = GuideTarget != null && !FocusMode.Active ? GuideTarget() : null; // focus hides the hints
            if (guide.HasValue != _guiding) { _guiding = guide.HasValue; SetLabel(); }
            bool show = guide.HasValue || (!FocusMode.Active && _onboarding.IsActive && Target(out target));
            if (guide.HasValue) target = guide.Value;
            if (_hint.gameObject.activeSelf != show) _hint.gameObject.SetActive(show);
            if (!show) return;

            Ray gaze = _hands.View;
            Vector3 to = target - gaze.origin;
            float dist = Mathf.Clamp(to.magnitude, 0.35f, 2.5f);
            // Inside the field of view: at most MaxAngle from the view centre, toward the target.
            Vector3 dir = Vector3.RotateTowards(gaze.direction, to.normalized, MaxAngle * Mathf.Deg2Rad, 0f);
            _hint.position = gaze.origin + dir * dist + Vector3.up * 0.08f;
            // The outside-floor step stands on that floor once it is in view (not floating at a wall).
            if (!_guiding && _onboarding.Step == OnboardingStep.FillOutside && Vector3.Angle(gaze.direction, to) <= FloorAngle)
            {
                _hint.position = target + Vector3.up * 0.25f;
            }
            _hint.rotation = Quaternion.LookRotation(_hint.position - gaze.origin, Vector3.up);
            _hint.localScale = Vector3.one * Mathf.Lerp(0.6f, 1.6f, (dist - 0.35f) / 2.15f); // readable at any distance

            // Ghost pinch: tips close and open about once a second.
            float open = 0.5f + 0.5f * Mathf.Sin(Time.time * 6f);
            _thumb.localPosition = new Vector3(-0.012f - 0.02f * open, -0.02f, 0f);
            _index.localPosition = new Vector3(0.012f + 0.02f * open, 0.01f, 0f);
        }

        // After every Update: skip takes a pinch only when no other target claimed it, so the shared
        // gaze radius around skip never steals a pinch meant for the step's own target.
        void LateUpdate()
        {
            if (_hint == null || !_hint.gameObject.activeSelf) return;
            bool aimed = _hands.GazeHit(_skip.position) >= 0f;
            HandState right = _hands.Right, left = _hands.Left;
            if ((!right.PinchStarted && !left.PinchStarted) || _hands.ClaimedThisFrame) return;
            Vector3 pinch = right.PinchStarted ? right.PinchPoint : left.PinchPoint;
            if (Vector3.Distance(pinch, _skip.position) < _nearRadius || aimed)
            {
                _hands.Claim();
                InputDebug.Log("onboarding: pinch on skip");
                _onboarding.Skip();
            }
        }

        /// <summary>The thing to pinch for the current step; false when it does not exist yet.</summary>
        bool Target(out Vector3 target)
        {
            target = default;
            switch (_onboarding.Step)
            {
                // SetZone: the zone setup shows its own instruction above its Done button (no ghost hand).
                case OnboardingStep.RedesignOne:
                    return NearestMyFurniture(out target);
                // FillOutside and PickMood happen on the panel, which opens on Style and never switches tabs for
                // the tips; no ghost hand, label or chip floats in the room for them (false: no world hint).
                default:
                    return false;
            }
        }

        bool NearestMyFurniture(out Vector3 target)
        {
            target = default;
            float best = float.MaxValue;
            Vector3 head = _hands.HeadPosition;
            var furniture = _room.Furniture;
            for (int i = 0; i < furniture.Count; i++)
            {
                if (!_store.Session.IsInMyZone(furniture[i])) continue;
                OrientedBox b = furniture[i].Box;
                Vector3 top = new Vector3(b.center.x, b.center.y + b.size.y * 0.5f, b.center.z);
                // Prefer desks and beds (they have a usable surface) by halving their distance.
                float d = Vector3.Distance(head, top) * (FitLimits.HasUsableSurface(furniture[i].Role) ? 0.5f : 1f);
                if (d < best) { best = d; target = top; }
            }
            return best < float.MaxValue;
        }

        void SetLabel()
        {
            if (_guiding) { _labelText = GuideLabel; if (_label != null) _label.ShowLocal(_labelText, new Vector3(0f, -0.125f, 0f)); return; }
            switch (_onboarding.Step)
            {
                case OnboardingStep.SetZone: _labelText = "Pinch Done"; break;
                case OnboardingStep.RedesignOne: _labelText = "Pinch your desk\n(or open My room)"; break;
                case OnboardingStep.FillOutside: _labelText = "Ask for a room"; break;
                case OnboardingStep.PickMood: _labelText = "Pick a mood"; break;
                default: _labelText = ""; break;
            }
            if (_label == null) return;
            if (_labelText.Length == 0) _label.Hide();
            else _label.ShowLocal(_labelText, new Vector3(0f, -0.125f, 0f));
        }

        void BuildVisuals()
        {
            _hint = new GameObject("OnboardingHint").transform;
            _hint.SetParent(transform, false);
            Color ghost = new Color(0.85f, 0.92f, 1f);
            Renderer palm = XrVisuals.Make(PrimitiveType.Sphere, _hint, _material, ghost, "GhostPalm");
            palm.transform.localPosition = new Vector3(0f, -0.06f, 0f);
            palm.transform.localScale = new Vector3(0.05f, 0.06f, 0.025f);
            _thumb = XrVisuals.Make(PrimitiveType.Sphere, _hint, _material, ghost, "GhostThumb").transform;
            _index = XrVisuals.Make(PrimitiveType.Sphere, _hint, _material, ghost, "GhostIndex").transform;
            _thumb.localScale = _index.localScale = Vector3.one * 0.018f;
            _skip = XrVisuals.Make(PrimitiveType.Sphere, _hint, _material, new Color(0.5f, 0.5f, 0.5f), "Skip").transform;
            _skip.localPosition = new Vector3(0.09f, -0.06f, 0f);
            _skip.localScale = Vector3.one * 0.025f;

            if (WorldLabel.Panel != null) _label = new GlassChip(_hint, WorldLabel.Panel, "Label"); // no raw world text (device batch 10)
            _hint.gameObject.SetActive(false);
        }
    }
}
