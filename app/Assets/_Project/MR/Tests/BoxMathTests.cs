using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.MR;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    public class BoxMathTests
    {
        // A 2 x 1 x 1 box at (0, 0.5, 0), turned 90 degrees: its width now runs along z.
        static readonly OrientedBox Turned = new OrientedBox(new Vec3(0f, 0.5f, 0f), new Vec3(2f, 1f, 1f), 90f);

        [Test]
        public void Contains_RespectsYaw()
        {
            Assert.IsTrue(BoxMath.Contains(Turned, new Vector3(0f, 0.5f, 0.9f), 0f));
            Assert.IsFalse(BoxMath.Contains(Turned, new Vector3(0.9f, 0.5f, 0f), 0f));
            Assert.IsTrue(BoxMath.Contains(Turned, new Vector3(0.52f, 0.5f, 0f), 0.03f));
        }

        [Test]
        public void Raycast_HitsFrontFace_MissesBeside()
        {
            var down = new Ray(new Vector3(0f, 3f, 0.9f), Vector3.down);
            Assert.AreEqual(2f, BoxMath.Raycast(Turned, down), 1e-4f);

            var beside = new Ray(new Vector3(0.9f, 3f, 0f), Vector3.down);
            Assert.AreEqual(-1f, BoxMath.Raycast(Turned, beside));
        }
    }
}
