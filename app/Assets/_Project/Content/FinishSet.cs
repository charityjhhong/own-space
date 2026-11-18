using System;
using System.Collections.Generic;
using UnityEngine;

namespace OwnSpace.Content
{
    /// <summary>Looks up floor and wall finishes. Implemented by <see cref="FinishSet"/>; fake it in tests.</summary>
    public interface IFinishSet
    {
        IReadOnlyList<FinishSet.Entry> Entries { get; }

        /// <summary>The finish for a design-state id (<c>floorFinishId</c> / <c>wallFinishId</c>). False for "" (keep the real surface) or an unknown id.</summary>
        bool TryGet(string finishId, out FinishSet.Entry entry);
    }

    /// <summary>
    /// Floor and wall finishes the user can choose. Generated from <c>finishes.json</c> by the content editor script
    /// (menu: OwnSpace/Content/Build Finishes); do not edit by hand. An empty finish id in the design state means
    /// "keep the real surface" and has no entry here.
    /// </summary>
    [CreateAssetMenu(menuName = "OwnSpace/Finish Set", fileName = "FinishSet")]
    public sealed class FinishSet : ScriptableObject, IFinishSet
    {
        [Serializable]
        public struct Entry
        {
            [Tooltip("Finish id stored in the design state.")] public string id;
            [Tooltip("Display name in the picker.")] public string name;
            [Tooltip("floor, wall or both.")] public string surface;
            [Tooltip("Style ids from style-board.md.")] public string[] styles;
            [Tooltip("Mood ids it suits.")] public string[] moods;
            [Tooltip("Short description for the picker and the AI.")] public string description;
            [Tooltip("Shared URP material; apply it to the floor or wall mesh.")] public Material material;
        }

        [SerializeField, Tooltip("Generated list; one entry per finish.")]
        Entry[] _entries = Array.Empty<Entry>();

        Dictionary<string, int> _byId;

        public IReadOnlyList<Entry> Entries => _entries;

        public bool TryGet(string finishId, out Entry entry)
        {
            entry = default;
            if (string.IsNullOrEmpty(finishId)) return false;
            if (_byId == null)
            {
                _byId = new Dictionary<string, int>(_entries.Length);
                for (int i = 0; i < _entries.Length; i++) _byId[_entries[i].id] = i;
            }
            if (!_byId.TryGetValue(finishId, out int index)) return false;
            entry = _entries[index];
            return entry.material != null;
        }

        void OnValidate() => _byId = null;
    }
}
