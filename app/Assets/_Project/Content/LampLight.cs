using UnityEngine;

namespace OwnSpace.Content
{
    /// <summary>
    /// Light data of one lamp prefab (floor lamp or pendant), copied from the catalog <c>light</c> field. It holds data
    /// only: the app creates the real light and glow, so lights stay under its budget (at most 2 real-time lights in view).
    /// Final color: <see cref="Kelvin"/>, or the active mood's <c>lampKelvin</c> when it is 0.
    /// Final intensity: <see cref="Intensity"/> times the mood's <c>lampIntensityMultiplier</c>.
    /// </summary>
    public sealed class LampLight : MonoBehaviour
    {
        [SerializeField, Tooltip("Bulb color temperature in Kelvin. 0 = follow the mood's lamp temperature.")] float _kelvin;
        [SerializeField, Tooltip("Base intensity, 1 = a normal lamp.")] float _intensity = 1f;
        [SerializeField, Tooltip("Reach of the real-time light in meters.")] float _range = 3f;
        [SerializeField, Tooltip("Radius in meters of the soft glow drawn on nearby surfaces.")] float _glowRadius = 0.5f;
        [SerializeField, Tooltip("Height of the light source above the prefab pivot (meters).")] float _sourceHeight = 1f;

        public float Kelvin => _kelvin;
        public float Intensity => _intensity;
        public float Range => _range;
        public float GlowRadius => _glowRadius;
        public float SourceHeight => _sourceHeight;

        /// <summary>Where the light sits, in the prefab's local space.</summary>
        public Vector3 SourceLocalPosition => new Vector3(0f, _sourceHeight, 0f);

        /// <summary>Sets the data. Used by the editor script; the prefab then stores it.</summary>
        public void Configure(float kelvin, float intensity, float range, float glowRadius, float sourceHeight)
        {
            _kelvin = kelvin; _intensity = intensity; _range = range; _glowRadius = glowRadius; _sourceHeight = sourceHeight;
        }
    }
}
