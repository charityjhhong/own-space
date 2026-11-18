using System.Collections.Generic;
using OwnSpace.Content;
using OwnSpace.Contracts;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// Shows the free placements of the outside zone: content's prefab (pivot at the bottom center,
    /// +Z front) or a tinted box of the item's size. Rebuilt on design changes only; a drag moves the
    /// shown transform directly and the design is changed on release.
    /// </summary>
    public sealed class PlacementView
    {
        sealed class Shown
        {
            public string ItemId;
            public GameObject Go;
            public Renderer[] Renderers;
            public bool Placeholder;
            public Color Base;
        }

        static readonly Color Refused = new Color(0.9f, 0.15f, 0.15f);
        readonly Transform _parent;
        readonly Material _material;
        readonly ICatalogPrefabs _prefabs;
        readonly Dictionary<string, Shown> _shown = new Dictionary<string, Shown>();
        readonly List<string> _stale = new List<string>();

        public PlacementView(Transform parent, Material material, ICatalogPrefabs prefabs)
        {
            _parent = parent;
            _material = material;
            _prefabs = prefabs;
        }

        public void Refresh(DesignState state, ICatalog catalog)
        {
            _stale.Clear();
            foreach (var pair in _shown) _stale.Add(pair.Key);
            for (int i = 0; i < state.placements.Count; i++)
            {
                FreePlacement p = state.placements[i];
                if (catalog == null || !catalog.TryGet(p.catalogItemId, out CatalogItem item)) continue;
                _stale.Remove(p.placementId);
                if (!_shown.TryGetValue(p.placementId, out Shown shown) || shown.ItemId != item.id)
                {
                    if (shown != null) Object.Destroy(shown.Go);
                    shown = Spawn(item);
                    _shown[p.placementId] = shown;
                }
                Pose(p.placementId, new Vector3(p.position.x, p.position.y, p.position.z), p.yawDegrees);
                SetRefused(p.placementId, false);
            }
            for (int i = 0; i < _stale.Count; i++)
            {
                Object.Destroy(_shown[_stale[i]].Go);
                _shown.Remove(_stale[i]);
            }
        }

        /// <summary>Moves the shown piece (drag preview). No allocation.</summary>
        public void Pose(string placementId, Vector3 floorCenter, float yawDegrees)
        {
            if (_shown.TryGetValue(placementId, out Shown s))
                s.Go.transform.SetPositionAndRotation(floorCenter, Quaternion.Euler(0f, yawDegrees, 0f));
        }

        /// <summary>Tints the piece red while its pose breaks a placement rule. No allocation.</summary>
        public void SetRefused(string placementId, bool refused)
        {
            if (!_shown.TryGetValue(placementId, out Shown s)) return;
            for (int i = 0; i < s.Renderers.Length; i++)
            {
                if (refused) XrVisuals.Tint(s.Renderers[i], Refused);
                else if (s.Placeholder) XrVisuals.Tint(s.Renderers[i], s.Base);
                else s.Renderers[i].SetPropertyBlock(null);
            }
        }

        // ---- the suggested spot: the real piece, not yet placed, its glow pulsing ----

        /// <summary>One pulse (fade in and out) takes this long (s).</summary>
        public const float PulseSeconds = 2f;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor"), BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly Color GhostGlow = new Color(0.039f, 0.455f, 0.878f);
        Shown _ghost;
        MaterialPropertyBlock _ghostBlock;

        /// <summary>The pulse at time <paramref name="t"/> (pure): 0 to 1 and back over <see cref="PulseSeconds"/>, smooth.</summary>
        public static float Pulse(float t) => 0.5f - 0.5f * Mathf.Cos(t * 2f * Mathf.PI / PulseSeconds);

        /// <summary>True while the suggested piece shows.</summary>
        public bool GhostShown => _ghost != null && _ghost.Go.activeSelf;

        /// <summary>Shows <paramref name="item"/> at a suggested spot, looking like the placed piece (made once per item).</summary>
        public void ShowGhost(CatalogItem item, Vector3 floorCenter, float yawDegrees)
        {
            if (_ghost == null || _ghost.ItemId != item.id)
            {
                if (_ghost != null) Object.Destroy(_ghost.Go);
                _ghost = Spawn(item);
                _ghost.Go.name = "Suggested " + item.id;
            }
            _ghost.Go.SetActive(true);
            _ghost.Go.transform.SetPositionAndRotation(floorCenter, Quaternion.Euler(0f, yawDegrees, 0f));
        }

        public void HideGhost() { if (_ghost != null) _ghost.Go.SetActive(false); }

        /// <summary>Pulses the suggested piece's glow (a placeholder box: its colour). No allocation.</summary>
        public void PulseGhost(float pulse)
        {
            if (!GhostShown) return;
            if (_ghostBlock == null) _ghostBlock = new MaterialPropertyBlock();
            for (int i = 0; i < _ghost.Renderers.Length; i++)
            {
                Renderer r = _ghost.Renderers[i];
                r.GetPropertyBlock(_ghostBlock);
                if (_ghost.Placeholder) _ghostBlock.SetColor(BaseColorId, Color.Lerp(_ghost.Base, GhostGlow, 0.15f + 0.35f * pulse));
                else _ghostBlock.SetColor(EmissionId, GhostGlow * (0.05f + 0.3f * pulse));
                r.SetPropertyBlock(_ghostBlock);
            }
        }

        Shown Spawn(CatalogItem item)
        {
            GameObject go;
            bool placeholder = false;
            if (_prefabs != null && _prefabs.TryGetPrefab(item.id, out GameObject prefab))
            {
                go = Object.Instantiate(prefab, _parent);
            }
            else
            {
                placeholder = true;
                go = new GameObject(item.id);
                go.transform.SetParent(_parent, false);
                Renderer box = XrVisuals.Make(PrimitiveType.Cube, go.transform, _material, RedesignView.ColorOf(item), "Box");
                box.transform.localScale = new Vector3(item.size.x, item.size.y, item.size.z);
                box.transform.localPosition = new Vector3(0f, item.size.y * 0.5f, 0f);
            }
            var shown = new Shown
            {
                ItemId = item.id, Go = go, Renderers = go.GetComponentsInChildren<Renderer>(), // before the shadow: never tinted red
                Placeholder = placeholder, Base = RedesignView.ColorOf(item),
            };
            if (ContactShadows.Grounded(item)) ContactShadows.Attach(go.transform, new Vector3(item.size.x, item.size.y, item.size.z));
            return shown;
        }
    }
}
