using System;
using System.Collections.Generic;

namespace OwnSpace.Contracts
{
    /// <summary>What to do with one real anchor in my zone. Values are stable; append only.</summary>
    public enum AnchorMode
    {
        KeepReal = 0,
        Redesign = 1,
    }

    [Serializable]
    public struct AnchorChoice
    {
        public string anchorId;
        public AnchorMode mode;

        /// <summary>The catalog item shown over the anchor when <see cref="mode"/> is Redesign; otherwise empty.</summary>
        public string catalogItemId;

        /// <summary>From the fit: the item is turned 90 degrees around Y.</summary>
        public bool rotated90;

        /// <summary>From the fit: uniform Y scale that lands the item's usable surface on the real one. 1 when kept real.</summary>
        public float yScale;

        /// <summary>From the fit: stretch along the item's own x and z (≥ 1) so it covers the real box. 1 when kept real.</summary>
        public float xScale;
        public float zScale;
    }

    public enum DesignZone
    {
        Mine = 0,
        Outside = 1,
    }

    public enum FinishSurface
    {
        Floor = 0,
        Wall = 1,
    }

    /// <summary>Floor and wall finish for one zone. Empty id = keep the real surface.</summary>
    [Serializable]
    public struct ZoneFinish
    {
        public string floorFinishId;
        public string wallFinishId;

        public static ZoneFinish Real => new ZoneFinish { floorFinishId = "", wallFinishId = "" };
    }

    /// <summary>
    /// A box the user draws by hand around clutter in my zone (CONCEPT 5.5). It is covered with
    /// <see cref="finishId"/>, or with the zone's own floor finish when that is empty.
    /// </summary>
    [Serializable]
    public struct HideBox
    {
        public string hideBoxId;

        /// <summary>World position of the box's floor center; the box stands on the floor.</summary>
        public Vec3 position;

        /// <summary>Meters: x = width, y = height, z = depth, in the box's own frame.</summary>
        public Vec3 size;
        public float yawDegrees;
        public string finishId;
    }

    /// <summary>A virtual piece placed freely in the outside zone.</summary>
    [Serializable]
    public struct FreePlacement
    {
        public string placementId;
        public string catalogItemId;

        /// <summary>World position of the item's floor center.</summary>
        public Vec3 position;
        public float yawDegrees;

        /// <summary>
        /// Placed by an AI plan (set when the plan is applied). The next AI plan removes these pieces before it
        /// places its own; pieces the user placed stay. False in saves from before this field: kept as the user's.
        /// </summary>
        public bool aiPlaced;
    }

    /// <summary>
    /// The whole saved design of a room. Plain fields so it serializes as-is
    /// (<c>OwnSpace.Core.DesignStateJson</c>). Empty ids mean "keep it real" / "no preset".
    /// Anchors with no entry in <see cref="anchorChoices"/> are kept real.
    /// </summary>
    [Serializable]
    public sealed class DesignState
    {
        /// <summary>2: finishes per zone. Version 1 saves (one top-level floor/wall finish) load into <see cref="outsideFinish"/>.</summary>
        public const int CurrentVersion = 2;

        public int version = CurrentVersion;

        /// <summary>The user's zone on the floor plane (x, z). Empty until the user sets it.</summary>
        public List<Vec2> zoneOutline = new List<Vec2>();
        public List<AnchorChoice> anchorChoices = new List<AnchorChoice>();
        public List<FreePlacement> placements = new List<FreePlacement>();

        /// <summary>Hand-drawn boxes hiding clutter in my zone.</summary>
        public List<HideBox> hideBoxes = new List<HideBox>();

        /// <summary>Props (catalog kind "clutter-cover", e.g. a basket) placed over clutter in my zone.</summary>
        public List<FreePlacement> coverProps = new List<FreePlacement>();
        public ZoneFinish myZoneFinish = ZoneFinish.Real;
        public ZoneFinish outsideFinish = ZoneFinish.Real;
        public string moodPresetId = "";

        /// <summary>The choice for an anchor; KeepReal when none was made.</summary>
        public AnchorChoice ChoiceFor(string anchorId)
        {
            for (int i = 0; i < anchorChoices.Count; i++)
            {
                if (anchorChoices[i].anchorId == anchorId) return anchorChoices[i];
            }
            return new AnchorChoice { anchorId = anchorId, mode = AnchorMode.KeepReal, catalogItemId = "", yScale = 1f, xScale = 1f, zScale = 1f };
        }

        /// <summary>A copy that shares nothing mutable with this one (lists are copied; items are structs).</summary>
        public DesignState Clone()
        {
            var copy = (DesignState)MemberwiseClone();
            copy.zoneOutline = new List<Vec2>(zoneOutline);
            copy.anchorChoices = new List<AnchorChoice>(anchorChoices);
            copy.placements = new List<FreePlacement>(placements);
            copy.hideBoxes = new List<HideBox>(hideBoxes);
            copy.coverProps = new List<FreePlacement>(coverProps);
            return copy;
        }

        /// <summary>Adds or replaces the choice for <c>choice.anchorId</c>.</summary>
        public void SetChoice(AnchorChoice choice)
        {
            for (int i = 0; i < anchorChoices.Count; i++)
            {
                if (anchorChoices[i].anchorId == choice.anchorId)
                {
                    anchorChoices[i] = choice;
                    return;
                }
            }
            anchorChoices.Add(choice);
        }
    }
}
