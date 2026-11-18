using UnityEngine;

namespace OwnSpace.Interaction
{
    /// <summary>
    /// Keeps one hand's aim steady (the human, after #353: "the pointer is too sensitive; every time I pinch, it shifts
    /// off"). A one-euro filter on the direction: heavy smoothing while the hand is nearly still, little while it moves
    /// fast. And a short history of the filtered aim, so a pinch can use where the hand pointed just before the fingers
    /// started to close (<see cref="LatchSeconds"/> ago). One per hand, made once; no allocation per frame.
    /// </summary>
    public sealed class AimSteady
    {
        /// <summary>One-euro: the cutoff (Hz) while still, how fast it opens with the direction's speed (rad/s), and the speed's own cutoff.</summary>
        public const float MinCutoff = 1.2f, Beta = 2.5f, SpeedCutoff = 1f;
        /// <summary>A pinch uses the aim from this long before it started (s).</summary>
        public const float LatchSeconds = 0.1f;
        const int History = 24; // about 0.27 s at 90 Hz

        readonly Ray[] _rays = new Ray[History];
        readonly float[] _times = new float[History];
        int _count, _next;
        Vector3 _dir, _speed;
        bool _started;

        /// <summary>Forget everything (the hand was lost or stopped aiming).</summary>
        public void Reset() { _started = false; _count = 0; _next = 0; }

        /// <summary>Filters this frame's aim (origin unfiltered, direction one-euro), records it and returns it.</summary>
        public Ray Step(Ray raw, float time, float dt)
        {
            Vector3 d = raw.direction.sqrMagnitude > 1e-8f ? raw.direction.normalized : Vector3.forward;
            if (!_started || dt <= 0f) { _dir = d; _speed = Vector3.zero; _started = true; }
            else
            {
                Vector3 rate = (d - _dir) / dt;
                _speed = Vector3.Lerp(_speed, rate, Alpha(SpeedCutoff, dt));
                float cutoff = MinCutoff + Beta * _speed.magnitude;
                Vector3 next = Vector3.Lerp(_dir, d, Alpha(cutoff, dt));
                _dir = next.sqrMagnitude > 1e-8f ? next.normalized : d;
            }
            var ray = new Ray(raw.origin, _dir);
            _rays[_next] = ray;
            _times[_next] = time;
            _next = (_next + 1) % History;
            if (_count < History) _count++;
            return ray;
        }

        /// <summary>
        /// The recorded aim nearest <see cref="LatchSeconds"/> before <paramref name="time"/> (the newest one older than
        /// that, else the oldest kept); <paramref name="fallback"/> with no history.
        /// </summary>
        public Ray Latched(float time, Ray fallback)
        {
            if (_count == 0) return fallback;
            float target = time - LatchSeconds;
            int oldest = (_next - _count + History) % History;
            Ray best = _rays[oldest];
            for (int k = 0; k < _count; k++)
            {
                int i = (oldest + k) % History;
                if (_times[i] > target + 1e-5f) break;
                best = _rays[i];
            }
            return best;
        }

        /// <summary>The one-euro smoothing factor for a cutoff (Hz) and a time step (pure).</summary>
        public static float Alpha(float cutoff, float dt)
        {
            float tau = 1f / (2f * Mathf.PI * Mathf.Max(1e-3f, cutoff));
            return 1f / (1f + tau / Mathf.Max(1e-5f, dt));
        }
    }
}
