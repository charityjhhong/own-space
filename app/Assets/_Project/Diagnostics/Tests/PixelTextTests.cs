using System;
using NUnit.Framework;

namespace OwnSpace.Diagnostics.Tests
{
    public class PixelTextTests
    {
        static string Tenths(double v)
        {
            var buf = new char[16];
            return new string(buf, 0, PixelText.WriteTenths(buf, 0, v));
        }

        [Test] public void WritesTenthsWithRounding() { Assert.AreEqual("8.2", Tenths(8.2)); Assert.AreEqual("13.9", Tenths(13.86)); Assert.AreEqual("0.0", Tenths(0.01)); }

        [Test]
        public void WritesIntegers()
        {
            var buf = new char[16];
            Assert.AreEqual("0", new string(buf, 0, PixelText.WriteInt(buf, 0, 0)));
            Assert.AreEqual("72", new string(buf, 0, PixelText.WriteInt(buf, 0, 72)));
            Assert.AreEqual("0", new string(buf, 0, PixelText.WriteInt(buf, 0, -5)));
        }

        [Test]
        public void DrawsOneQuadPerLitPixel()
        {
            var batch = new QuadBatch(100);
            PixelText.Draw(batch, new[] { '1' }, 1, 0f, 0f, 1f);
            Assert.AreEqual(8, batch.Quads); // the glyph "1" has 8 lit pixels
        }

        [Test]
        public void StopsAtCapacityInsteadOfOverflowing()
        {
            var batch = new QuadBatch(3);
            Assert.DoesNotThrow(() => PixelText.Draw(batch, new[] { '8', '8' }, 2, 0f, 0f, 1f));
            Assert.AreEqual(3, batch.Quads);
        }
    }
}
