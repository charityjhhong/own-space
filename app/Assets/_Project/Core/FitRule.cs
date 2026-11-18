using System;
using OwnSpace.Contracts;

namespace OwnSpace.Core
{
    public enum FitStatus
    {
        Fits,
        Missing,
        InvalidSize,
        RoleMismatch,
        HeightOutOfRange,
        FootprintTooLarge,
        /// <summary>The item is smaller than the real box by more than <see cref="FitLimits.MaxFootprintScale"/> allows to cover it.</summary>
        NeedsTooMuchStretch,
    }

    /// <summary>How a catalog item fits an anchor, and how to place it when it does.</summary>
    public readonly struct FitResult
    {
        public FitResult(FitStatus status, bool rotated90, float yScale) : this(status, rotated90, yScale, 1f, 1f) { }

        public FitResult(FitStatus status, bool rotated90, float yScale, float xScale, float zScale, bool stretched = false)
        {
            Stretched = stretched;
            Status = status;
            Rotated90 = rotated90;
            YScale = yScale;
            XScale = xScale;
            ZScale = zScale;
        }

        public FitStatus Status { get; }
        public bool Fits => Status == FitStatus.Fits;

        /// <summary>True when the item is turned 90 degrees around Y (its width runs along the anchor's depth).</summary>
        public bool Rotated90 { get; }

        /// <summary>Uniform Y scale that puts the item's target height exactly on the real one. 1 when it does not fit.</summary>
        public float YScale { get; }

        /// <summary>
        /// Scale along the item's own x (width) and z (depth) so its footprint covers the real box: ≥ 1 for
        /// most roles, 0.6–1.8 for tables (see <see cref="FitRule.TableMinScale"/>). Apply to the model's local
        /// scale together with <see cref="YScale"/>. 1 when it does not fit.
        /// </summary>
        public float XScale { get; }
        public float ZScale { get; }

        /// <summary>
        /// Not a real fit: nothing of this role fits the box, so one of the closest designs is scaled to it
        /// (see <see cref="CatalogQueries.FittingItems"/>). Show it marked "stretched".
        /// </summary>
        public bool Stretched { get; }

        internal static FitResult Fail(FitStatus status) => new FitResult(status, false, 1f);
    }

    /// <summary>
    /// The my-zone redesign rule (CONCEPT.md 5.2), using the numbers in <see cref="FitLimits"/>.
    /// A catalog item may replace a real anchor when the roles match; a uniform Y scale within
    /// 1 ± MaxYScale puts the item's target height exactly on the real one (usable surface for
    /// Table and Bed, box top for other roles or when the usable height is unknown), so the height
    /// people use never changes; and its footprint, never stretched, fits inside the real box
    /// enlarged by MarginPerSide on each side, as-is or turned 90 degrees (as-is preferred).
    /// Every length comparison allows FitLimits.Tolerance. Pure and allocation-free.
    /// </summary>
    public static class FitRule
    {
        /// <summary>
        /// Tables (desks) may be scaled per footprint axis from 0.6 to 1.8 times, shrunk to the real box plus
        /// its margin or stretched to cover it, so a real desk of any common size gets designs.
        /// </summary>
        public const float TableMinScale = 0.6f, TableMaxScale = 1.8f;

        public static bool Fits(FurnitureAnchor anchor, CatalogItem item) => Check(anchor, item).Fits;

        public static FitResult Check(FurnitureAnchor anchor, CatalogItem item)
        {
            if (anchor == null || item == null) return FitResult.Fail(FitStatus.Missing);

            Vec3 box = anchor.Box.size;
            Vec3 size = item.size;
            if (!Positive(box.x) || !Positive(box.y) || !Positive(box.z) ||
                !Positive(size.x) || !Positive(size.y) || !Positive(size.z) ||
                (anchor.UsableHeight.HasValue && !Positive(anchor.UsableHeight.Value)) ||
                !IsFinite(item.usableHeight) || item.usableHeight < 0f)
            {
                return FitResult.Fail(FitStatus.InvalidSize);
            }

            if (anchor.Role != item.role) return FitResult.Fail(FitStatus.RoleMismatch);

            bool usable = FitLimits.HasUsableSurface(anchor.Role);
            float realHeight = usable && anchor.UsableHeight.HasValue ? anchor.UsableHeight.Value : box.y;
            float itemHeight = usable && item.usableHeight > 0f ? item.usableHeight : size.y;

            // Compared in meters so the same 1 cm tolerance applies as for footprints.
            float lowest = itemHeight * (1f - FitLimits.MaxYScale) - FitLimits.Tolerance;
            float highest = itemHeight * (1f + FitLimits.MaxYScale) + FitLimits.Tolerance;
            if (realHeight < lowest || realHeight > highest) return FitResult.Fail(FitStatus.HeightOutOfRange);
            float yScale = realHeight / itemHeight;

            // Try as-is and turned 90 degrees; keep the one that needs the least stretch (as-is on a tie).
            bool table = anchor.Role == FurnitureRole.Table;
            var asIs = Footprint(size.x, size.z, box.x, box.z, table, out float ax, out float az);
            var turned = Footprint(size.z, size.x, box.x, box.z, table, out float tz, out float tx);
            if (asIs == FitStatus.Fits && (turned != FitStatus.Fits || Distortion(ax, az) <= Distortion(tx, tz)))
                return new FitResult(FitStatus.Fits, false, yScale, ax, az);
            if (turned == FitStatus.Fits) return new FitResult(FitStatus.Fits, true, yScale, tx, tz);
            return FitResult.Fail(asIs);
        }

        // One orientation: the item's footprint (a along the box's x, b along its z) must stay within the
        // box enlarged by the margin, and is stretched (never shrunk) to cover the box, at most
        // MaxFootprintScale per axis. Lengths compare with the 1 cm tolerance.
        static FitStatus Footprint(float a, float b, float boxX, float boxZ, bool table, out float scaleA, out float scaleB)
        {
            scaleA = scaleB = 1f;
            if (table) return TableAxis(a, boxX, out scaleA) && TableAxis(b, boxZ, out scaleB) ? FitStatus.Fits
                : a > boxX && b > boxZ ? FitStatus.FootprintTooLarge : FitStatus.NeedsTooMuchStretch;
            // Stored in float locals (rounded like the caller's values), so exact boundary values stay inside.
            float maxA = boxX + 2f * FitLimits.MarginPerSide + FitLimits.Tolerance;
            float maxB = boxZ + 2f * FitLimits.MarginPerSide + FitLimits.Tolerance;
            if (a > maxA || b > maxB) return FitStatus.FootprintTooLarge;
            float reachA = a * FitLimits.MaxFootprintScale + FitLimits.Tolerance;
            float reachB = b * FitLimits.MaxFootprintScale + FitLimits.Tolerance;
            if (boxX > reachA || boxZ > reachB) return FitStatus.NeedsTooMuchStretch;
            scaleA = Math.Max(1f, boxX / a);
            scaleB = Math.Max(1f, boxZ / b);
            return FitStatus.Fits;
        }

        // A table axis: kept when it already lies between the box and the box plus its margins, otherwise
        // stretched to the box or shrunk to the box plus margins, within TableMinScale..TableMaxScale.
        static bool TableAxis(float length, float box, out float scale)
        {
            float most = box + 2f * FitLimits.MarginPerSide;
            float target = length < box ? box : length > most ? most : length;
            scale = target / length;
            float min = TableMinScale - FitLimits.Tolerance / length, max = TableMaxScale + FitLimits.Tolerance / length;
            if (scale >= min && scale <= max) return true;
            scale = 1f;
            return false;
        }

        /// <summary>
        /// The closest design's look scaled onto the box: same role, Y scaled to the real height and the
        /// footprint to the box, in the orientation that distorts it least. Marked <see cref="FitResult.Stretched"/>.
        /// Fails (not Fits) only for a missing piece, invalid sizes or another role.
        /// </summary>
        public static FitResult Stretch(FurnitureAnchor anchor, CatalogItem item)
        {
            var check = Check(anchor, item);
            if (check.Fits || check.Status == FitStatus.Missing || check.Status == FitStatus.InvalidSize || check.Status == FitStatus.RoleMismatch)
                return check;
            Vec3 box = anchor.Box.size, size = item.size;
            bool usable = FitLimits.HasUsableSurface(anchor.Role);
            float realHeight = usable && anchor.UsableHeight.HasValue ? anchor.UsableHeight.Value : box.y;
            float itemHeight = usable && item.usableHeight > 0f ? item.usableHeight : size.y;
            float ax = box.x / size.x, az = box.z / size.z, tx = box.x / size.z, tz = box.z / size.x;
            bool turn = Distortion(tx, tz) < Distortion(ax, az);
            return turn ? new FitResult(FitStatus.Fits, true, realHeight / itemHeight, tz, tx, true)
                        : new FitResult(FitStatus.Fits, false, realHeight / itemHeight, ax, az, true);
        }

        /// <summary>How far a fit is from the design as made: the largest scale change on any axis, as a ratio (0 = none).</summary>
        public static float Distortion(FitResult fit) => Math.Max(Distortion(fit.XScale, fit.ZScale), Ratio(fit.YScale));

        static float Distortion(float a, float b) => Math.Max(Ratio(a), Ratio(b));
        static float Ratio(float s) => s <= 0f ? float.MaxValue : Math.Max(s, 1f / s) - 1f;

        static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        static bool Positive(float v) => IsFinite(v) && v > 0f;
    }
}
