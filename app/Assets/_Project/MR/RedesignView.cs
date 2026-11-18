using System.Collections.Generic;
using OwnSpace.Content;
using OwnSpace.Contracts;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// Shows the redesigned pieces: for every anchor whose choice is Redesign, the catalog item's
    /// prefab (content's <see cref="ICatalogPrefabs"/>, pivot at the bottom center, +Z front) or a
    /// tinted box of the item's size when it has none. Placed on the anchor's floor center, turned
    /// to the anchor's yaw (+90 when the fit says so), Y scaled so the usable surface matches, and
    /// X/Z stretched (in the item's own axes) so its footprint covers the real box.
    /// A piece that would overlap a window or door is never shown, however the choice got there
    /// (apply, restore, undo or a room reload); <see cref="Blocked"/> counts them.
    /// Updates only when the design or the room changes.
    /// </summary>
    public sealed class RedesignView
    {
        readonly Transform _parent;
        readonly Material _material;
        readonly ICatalogPrefabs _prefabs;
        readonly Dictionary<string, (string itemId, GameObject go)> _shown = new Dictionary<string, (string, GameObject)>();
        readonly List<string> _stale = new List<string>();

        public RedesignView(Transform parent, Material material, ICatalogPrefabs prefabs)
        {
            _parent = parent;
            _material = material;
            _prefabs = prefabs;
        }

        bool _hidden;

        /// <summary>
        /// Hides or shows every redesigned piece (the zone edit shows the real room, so the outline is never under a
        /// redesigned desk: device shot 182125). Pieces spawned while hidden stay hidden.
        /// </summary>
        public void SetVisible(bool visible)
        {
            _hidden = !visible;
            foreach (var pair in _shown) if (pair.Value.go != null && pair.Value.go.activeSelf != visible) pair.Value.go.SetActive(visible);
        }

        /// <summary>Redesign choices hidden by the last <see cref="Refresh"/> because they would cover an opening.</summary>
        public int Blocked { get; private set; }

        public void Refresh(IRoomInfo room, DesignState state, ICatalog catalog, AnchorAdjusts adjusts = null)
        {
            _adjusts = adjusts;
            Blocked = 0;
            _stale.Clear();
            foreach (var pair in _shown) _stale.Add(pair.Key);

            var furniture = room.Furniture;
            for (int i = 0; i < furniture.Count; i++)
            {
                FurnitureAnchor anchor = furniture[i];
                AnchorChoice choice = state.ChoiceFor(anchor.Id);
                if (choice.mode != AnchorMode.Redesign || !catalog.TryGet(choice.catalogItemId, out CatalogItem item)) continue;
                if (OpeningGuard.Covers(room, OpeningGuard.Placed(anchor, item, choice.rotated90, choice.xScale, choice.yScale, choice.zScale, room.FloorY)))
                {
                    Blocked++;
                    continue;
                }
                _stale.Remove(anchor.Id);

                if (!_shown.TryGetValue(anchor.Id, out var shown) || shown.itemId != item.id)
                {
                    if (shown.go != null) Object.Destroy(shown.go);
                    shown = (item.id, Spawn(item));
                    if (ContactShadows.Grounded(item)) ContactShadows.Attach(shown.go.transform, new Vector3(item.size.x, item.size.y, item.size.z));
                    _shown[anchor.Id] = shown;
                    if (_hidden) shown.go.SetActive(false);
                }
                Place(shown.go.transform, anchor, item, choice, room.FloorY, adjusts != null ? adjusts.Get(anchor.Id) : default);
            }

            for (int i = 0; i < _stale.Count; i++)
            {
                Object.Destroy(_shown[_stale[i]].go);
                _shown.Remove(_stale[i]);
            }
        }

        GameObject Spawn(CatalogItem item)
        {
            if (_prefabs != null && _prefabs.TryGetPrefab(item.id, out GameObject prefab))
            {
                return Object.Instantiate(prefab, _parent);
            }
            // Placeholder: a box of the item's size whose pivot is its bottom center, like the prefabs.
            var root = new GameObject(item.id);
            root.transform.SetParent(_parent, false);
            Renderer box = XrVisuals.Make(PrimitiveType.Cube, root.transform, _material, ColorOf(item), "Box");
            box.transform.localScale = new Vector3(item.size.x, item.size.y, item.size.z);
            box.transform.localPosition = new Vector3(0f, item.size.y * 0.5f, 0f);
            return root;
        }

        AnchorAdjusts _adjusts;

        /// <summary>Moves one shown piece (a nudge drag) without a full refresh. No allocation.</summary>
        public void Pose(string anchorId, FurnitureAnchor anchor, CatalogItem item, AnchorChoice choice, float floorY, AnchorAdjust adjust)
        {
            if (_shown.TryGetValue(anchorId, out var shown) && shown.go != null) Place(shown.go.transform, anchor, item, choice, floorY, adjust);
        }

        // Core's fit (orientation, scale) plus the user's adjustment: extra quarter turns, a floor nudge, extra width/depth.
        static void Place(Transform t, FurnitureAnchor anchor, CatalogItem item, AnchorChoice choice, float floorY, AnchorAdjust adjust)
        {
            float yaw = anchor.Box.yawDegrees + (choice.rotated90 ? 90f : 0f) + adjust.turns * 90f + adjust.yaw;
            t.SetPositionAndRotation(new Vector3(anchor.Box.center.x + adjust.dx, floorY, anchor.Box.center.z + adjust.dz), Quaternion.Euler(0f, yaw, 0f));
            // Local scale is in the item's own axes, so the turns above carry it along.
            t.localScale = AnchorAdjusts.Scale(item, choice, adjust);
        }

        public static Color ColorOf(CatalogItem item) =>
            ColorUtility.TryParseHtmlString(item.primaryColor, out Color c) ? c : new Color(0.7f, 0.6f, 0.5f);
    }
}
