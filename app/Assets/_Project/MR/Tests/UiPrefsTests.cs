using System.IO;
using NUnit.Framework;

namespace OwnSpace.MR.Tests
{
    public class UiPrefsTests
    {
        string _dir;

        [SetUp] public void SetUp() { _dir = Path.Combine(Path.GetTempPath(), "ownspace-uiprefs-" + System.Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_dir); }
        [TearDown] public void TearDown() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

        [Test]
        public void Theme_RoundTrips()
        {
            string path = Path.Combine(_dir, UiPrefs.FileName);
            Assert.IsTrue(UiPrefs.SaveTheme(path, UiPrefs.Dark));
            Assert.AreEqual(UiPrefs.Dark, UiPrefs.LoadTheme(path));
            Assert.IsTrue(UiPrefs.SaveTheme(path, UiPrefs.Light));
            Assert.AreEqual(UiPrefs.Light, UiPrefs.LoadTheme(path));
        }

        [Test]
        public void MissingOrBrokenFile_GivesLight()
        {
            string path = Path.Combine(_dir, UiPrefs.FileName);
            Assert.AreEqual(UiPrefs.Light, UiPrefs.LoadTheme(path));
            File.WriteAllText(path, "{ not json");
            Assert.AreEqual(UiPrefs.Light, UiPrefs.LoadTheme(path));
        }

        [Test]
        public void UnknownTheme_SavesAndLoadsAsLight()
        {
            string path = Path.Combine(_dir, UiPrefs.FileName);
            UiPrefs.SaveTheme(path, "neon");
            Assert.AreEqual(UiPrefs.Light, UiPrefs.LoadTheme(path));
        }

        [Test]
        public void Scale_RoundTrips_KeepsTheTheme_AndRejectsNonsense()
        {
            string path = System.IO.Path.Combine(_dir, UiPrefs.FileName);
            Assert.AreEqual(1f, UiPrefs.LoadScale(path), "missing file: 1.0x");
            UiPrefs.SaveTheme(path, UiPrefs.Dark);
            UiPrefs.SaveScale(path, 1.4f);
            Assert.AreEqual(1.4f, UiPrefs.LoadScale(path), 1e-5f);
            Assert.AreEqual(UiPrefs.Dark, UiPrefs.LoadTheme(path), "the theme survives a scale save");
            UiPrefs.SaveTheme(path, UiPrefs.Light);
            Assert.AreEqual(1.4f, UiPrefs.LoadScale(path), 1e-5f, "and the scale a theme save");
            System.IO.File.WriteAllText(path, "{\"scale\":9}");
            Assert.AreEqual(1f, UiPrefs.LoadScale(path));
        }
    }
}
