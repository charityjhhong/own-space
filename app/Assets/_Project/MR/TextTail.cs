using OwnSpace.UI;
using TMPro;

namespace OwnSpace.MR
{
    /// <summary>
    /// The end of a typed line that fits a width at full text size (device, build dfb22f4: the AI field's text shrank to
    /// half the size of the chips around it). A text field keeps its size and shows the end of a long text; it never
    /// shrinks. Call on a change (a key press), never per frame: it measures with a temporary text object.
    /// </summary>
    public static class TextTail
    {
        /// <summary>
        /// <paramref name="text"/> when it fits <paramref name="width"/> (m) at <paramref name="mm"/> in
        /// <paramref name="font"/>, else its longest ending that does (cut from the left).
        /// </summary>
        public static string Fit(string text, float width, float mm, TMP_FontAsset font)
        {
            if (string.IsNullOrEmpty(text) || PanelElement.MeasureWidth(text, mm, font) <= width) return text;
            int lo = 1, hi = text.Length - 1; // the first character kept: the smallest start whose ending fits
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (PanelElement.MeasureWidth(text.Substring(mid), mm, font) <= width) hi = mid; else lo = mid + 1;
            }
            return text.Substring(lo);
        }

        /// <summary><paramref name="text"/> when it fits, else its longest beginning that does (a name in a caption; no ellipsis).</summary>
        public static string Head(string text, float width, float mm, TMP_FontAsset font)
        {
            if (string.IsNullOrEmpty(text) || PanelElement.MeasureWidth(text, mm, font) <= width) return text;
            int lo = 1, hi = text.Length - 1; // the number of characters kept: the largest that fits
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (PanelElement.MeasureWidth(text.Substring(0, mid), mm, font) <= width) lo = mid; else hi = mid - 1;
            }
            return text.Substring(0, lo).TrimEnd();
        }
    }
}
