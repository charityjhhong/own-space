namespace OwnSpace.UI
{
    /// <summary>Text shaping for panel labels (pure, for tests). Call when the data changes, never per frame.</summary>
    public static class PanelText
    {
        /// <summary>
        /// A card name on at most two lines (device batch 4: "Linen two-seat sofa" broke inside "two-seat"): short
        /// names stay on one line; longer ones break at the space that makes the longer line shortest. Hyphens never
        /// break (cards do not wrap on their own), and a word longer than a line ends in an ellipsis when drawn.
        /// </summary>
        public static string TwoLines(string name, int oneLineChars = 12)
        {
            if (string.IsNullOrEmpty(name) || name.Length <= oneLineChars || name.IndexOf(' ') < 0) return name;
            int best = -1, bestLonger = int.MaxValue;
            for (int i = 0; i < name.Length; i++)
            {
                if (name[i] != ' ') continue;
                int longer = System.Math.Max(i, name.Length - i - 1);
                if (longer < bestLonger) { bestLonger = longer; best = i; }
            }
            return name.Substring(0, best) + "\n" + name.Substring(best + 1);
        }
    }
}
