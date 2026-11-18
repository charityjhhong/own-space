using System;
using System.IO;

namespace OwnSpace.Core
{
    /// <summary>Crash-safe small-file save and a read that never throws. Shared by every core save file.</summary>
    public static class AtomicFile
    {
        /// <summary>
        /// Writes a temp file first, then replaces the old file in one step, so a crash or power
        /// loss mid-write leaves the previous file intact. Throws on failure, leaving no temp file.
        /// </summary>
        public static void WriteAllText(string path, string text)
        {
            string temp = path + ".tmp";
            try
            {
                File.WriteAllText(temp, text);
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally
            {
                // After success the temp file is gone; after a failure, don't leave it behind.
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        /// <summary>
        /// Copies a save that could not be read to <c>&lt;path&gt;.bad</c> (replacing an older copy), so the
        /// next save cannot destroy it, e.g. a file from a newer app version. Never throws.
        /// </summary>
        public static void KeepBrokenCopy(string path)
        {
            try
            {
                if (File.Exists(path)) File.Copy(path, path + ".bad", true);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }

        /// <summary>The file's text, or null when it is missing or unreadable.</summary>
        public static string TryReadAllText(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
